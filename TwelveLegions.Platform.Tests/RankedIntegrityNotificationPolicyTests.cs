using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RankedIntegrityNotificationPolicyTests
{
    [Theory]
    [InlineData("review")]
    [InlineData("normal")]
    [InlineData("insufficient")]
    public void NoImpactDecisionIsSilentButAuditedDurableAndIdempotent(string disposition)
    {
        using var fixture = new Fixture();
        fixture.Settle("quiet");
        var beforeFirst = fixture.Store.RankedProfile(fixture.First.Id);
        var beforeSecond = fixture.Store.RankedProfile(fixture.Second.Id);
        var input = Input("quiet-request", disposition, "quiet");
        var preview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, input);
        Assert.True(preview.CanConfirm);
        Assert.Empty(preview.PlayerNotificationAccountIds);
        Assert.All(preview.AccountEffects, effect =>
        {
            Assert.Equal("unchanged", effect.RewardOutcome);
            Assert.Equal(0, effect.ScoreDelta);
            Assert.Null(effect.RestrictionUntil);
        });
        var decision = fixture.Store.ConfirmRankedIntegrityAction(fixture.Admin, input,
            preview.Revision, new("quiet-request"));
        Assert.Empty(decision.PlayerNotificationAccountIds);

        AssertSilent(fixture.Store, fixture.First);
        AssertSilent(fixture.Store, fixture.Second);
        Assert.Equal(beforeFirst, fixture.Store.RankedProfile(fixture.First.Id));
        Assert.Equal(beforeSecond, fixture.Store.RankedProfile(fixture.Second.Id));
        Assert.Equal("applied", fixture.Store.RankedSettlement("quiet", fixture.First.Id)!.RewardStatus);
        Assert.Equal(decision.DecisionId, fixture.Store.ConfirmRankedIntegrityAction(fixture.Admin,
            input, -1, new("quiet-replay")).DecisionId);
        Assert.Single(fixture.Store.RankedIntegrityDecisions(fixture.Admin).Items);
        Assert.Single(fixture.Store.AdminAudit(category: "ranked-integrity"));
        Assert.Throws<L12RankedIntegrityActionException>(() => fixture.Store.ConfirmRankedIntegrityAction(
            fixture.Admin, input with { Reason = "changed request" }, preview.Revision, new("conflict")));

        var reloaded = fixture.Reload();
        var saved = Assert.Single(reloaded.RankedIntegrityDecisions(fixture.Admin).Items);
        Assert.Equal(decision.DecisionId, saved.DecisionId);
        Assert.Equal(disposition, saved.Disposition);
        Assert.Equal(input.Evidence, saved.Evidence);
        Assert.Equal(input.Reason, saved.Reason);
        Assert.Empty(saved.PlayerNotificationAccountIds);
        Assert.Equal(decision.Revision, reloaded.PreviewRankedIntegrityAction(fixture.Admin, input).Revision);
        Assert.Single(reloaded.AdminAudit(category: "ranked-integrity"));
        AssertSilent(reloaded, fixture.First);
        AssertSilent(reloaded, fixture.Second);
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("insufficient")]
    public void AutomaticHoldAndActualReleaseStillNotifyEvenWithZeroScore(string disposition)
    {
        using var fixture = new Fixture();
        fixture.Hold();
        foreach (var account in fixture.Players)
        {
            var heldNotice = Assert.Single(fixture.Store.RankedIntegrityNotifications(account).Items);
            Assert.Equal("review", heldNotice.Outcome);
            Assert.Equal(0, heldNotice.ScoreDelta);
        }
        var input = Input("release", disposition, "held-2");
        var preview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, input);
        Assert.True(preview.CanConfirm, string.Join(";", preview.BlockingReasons));
        Assert.All(preview.AccountEffects, effect =>
        {
            Assert.Equal("released", effect.RewardOutcome);
            Assert.Equal(0, effect.ScoreDelta); // Placement progress changes without a SevenValue delta.
        });
        var decision = fixture.Store.ConfirmRankedIntegrityAction(fixture.Admin, input,
            preview.Revision, new("release"));
        Assert.Equal(2, preview.PlayerNotificationAccountIds.Count);
        Assert.Equal(preview.PlayerNotificationAccountIds, decision.PlayerNotificationAccountIds);
        foreach (var account in fixture.Players)
        {
            Assert.Equal(2, fixture.Store.RankedIntegrityNotifications(account).UnreadCount);
            Assert.Contains(fixture.Reload().RankedIntegrityNotifications(account).Items,
                notice => notice.DecisionId == decision.DecisionId && notice.Outcome == disposition);
            Assert.Equal("released", fixture.Store.RankedSettlement("held-2", account.Id)!.RewardStatus);
        }
    }

    [Theory]
    [InlineData("confirmed", false)]
    [InlineData("confirmed", true)]
    [InlineData("system-error", false)]
    public void ZeroScoreHistoricalVoidRestrictionAndRevocationKeepNotifications(string disposition, bool restrict)
    {
        using var fixture = new Fixture();
        fixture.Settle("historical");
        fixture.Settle("later", winner: 1);
        var firstBefore = fixture.Store.RankedProfile(fixture.First.Id);
        var secondBefore = fixture.Store.RankedProfile(fixture.Second.Id);
        var input = Input("void", disposition, "historical") with
        {
            RestrictedAccountIds = restrict ? [fixture.First.Id] : [],
            RestrictionDays = restrict ? 7 : null,
        };
        var preview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, input);
        Assert.True(preview.CanConfirm, string.Join(";", preview.BlockingReasons));
        Assert.All(preview.AccountEffects, effect =>
        {
            Assert.Equal(0, effect.ScoreDelta);
            Assert.Equal("voided-profile-preserved", effect.RewardOutcome);
        });
        var decision = fixture.Store.ConfirmRankedIntegrityAction(fixture.Admin, input,
            preview.Revision, new("void"));
        Assert.Equal(2, decision.PlayerNotificationAccountIds.Count);
        Assert.Equal(preview.PlayerNotificationAccountIds, decision.PlayerNotificationAccountIds);
        foreach (var account in fixture.Players)
            Assert.Equal(decision.DecisionId, Assert.Single(
                fixture.Store.RankedIntegrityNotifications(account).Items).DecisionId);
        Assert.Equal(restrict, fixture.Store.RankedEntryBlock(fixture.First.Id, DateTimeOffset.UtcNow) is not null);
        Assert.Null(fixture.Store.RankedEntryBlock(fixture.Second.Id, DateTimeOffset.UtcNow));
        Assert.Equal(firstBefore, fixture.Store.RankedProfile(fixture.First.Id));
        Assert.Equal(secondBefore, fixture.Store.RankedProfile(fixture.Second.Id));

        var revoke = Input("undo", "revoked", "historical") with { RevokesDecisionId = decision.DecisionId };
        var undone = fixture.Confirm(revoke);
        foreach (var account in fixture.Players)
        {
            Assert.Equal(2, fixture.Store.RankedIntegrityNotifications(account).UnreadCount);
            Assert.Contains(fixture.Reload().RankedIntegrityNotifications(account).Items,
                notice => notice.DecisionId == undone.DecisionId && notice.RelatedDecisionId == decision.DecisionId);
        }
        Assert.Null(fixture.Store.RankedEntryBlock(fixture.First.Id, DateTimeOffset.UtcNow));
        Assert.Equal("applied", fixture.Store.RankedSettlement("historical", fixture.First.Id)!.RewardStatus);
    }

    [Fact]
    public void ManualReviewOfAnExistingHoldDoesNotCreateAnotherPlayerNotification()
    {
        using var fixture = new Fixture();
        fixture.Hold();
        var input = Input("manual-review", "review", "held-2");
        var preview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, input);
        Assert.True(preview.CanConfirm);
        Assert.Empty(preview.PlayerNotificationAccountIds);
        Assert.All(preview.AccountEffects, effect => Assert.Equal("unchanged", effect.RewardOutcome));
        var decision = fixture.Confirm(input);
        Assert.Empty(decision.PlayerNotificationAccountIds);
        foreach (var account in fixture.Players)
        {
            Assert.Single(fixture.Store.RankedIntegrityNotifications(account).Items);
            Assert.Equal("held", fixture.Store.RankedSettlement("held-2", account.Id)!.RewardStatus);
        }
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("insufficient")]
    public void MixedBatchReleaseOnlyNotifiesAccountsWhoseHeldRewardsActuallyChange(string disposition)
    {
        using var fixture = new Fixture();
        fixture.Hold();
        var third = fixture.Store.Register("thirdone", "Password123!");
        var fourth = fixture.Store.Register("fourthone", "Password123!");
        Assert.True(third.Success, third.Message);
        Assert.True(fourth.Success, fourth.Message);
        fixture.Store.SelectRankedFaction(third.Account!.Id, "order");
        fixture.Store.SelectRankedFaction(fourth.Account!.Id, "chaos");
        fixture.Store.SettleRankedMatch("unheld", third.Account.Id, fourth.Account.Id, 0);
        var thirdBefore = fixture.Store.RankedProfile(third.Account.Id);
        var fourthBefore = fixture.Store.RankedProfile(fourth.Account.Id);
        var input = Input("mixed-release", disposition, "held-2") with { MatchIds = ["held-2", "unheld"] };
        var preview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, input);
        Assert.True(preview.CanConfirm, string.Join(";", preview.BlockingReasons));
        Assert.Equal(2, preview.PlayerNotificationAccountIds.Count);
        Assert.All(preview.AccountEffects.Where(effect => effect.AccountId == third.Account.Id
            || effect.AccountId == fourth.Account.Id), effect => Assert.Equal("unchanged", effect.RewardOutcome));
        var decision = fixture.Confirm(input);
        Assert.Equal(preview.PlayerNotificationAccountIds, decision.PlayerNotificationAccountIds);
        AssertSilent(fixture.Store, third.Account);
        AssertSilent(fixture.Store, fourth.Account);
        Assert.Equal(thirdBefore, fixture.Store.RankedProfile(third.Account.Id));
        Assert.Equal(fourthBefore, fixture.Store.RankedProfile(fourth.Account.Id));
        Assert.Equal("applied", fixture.Store.RankedSettlement("unheld", third.Account.Id)!.RewardStatus);
        Assert.All(fixture.Players, account => Assert.Equal(2,
            fixture.Store.RankedIntegrityNotifications(account).UnreadCount));
    }

    [Fact]
    public void PrivateAppealReplyIsNotSuppressedOrSentToOpponent()
    {
        using var fixture = new Fixture();
        fixture.Settle("appeal");
        var decision = fixture.Confirm(Input("sanction", "confirmed", "appeal"));
        var appeal = fixture.Store.SubmitRankedIntegrityAppeal(fixture.First, decision.DecisionId,
            "appeal-request", "请复核本次处置证据。");
        fixture.Store.ReviewRankedIntegrityAppeal(fixture.Admin, appeal.Id,
            new("appeal-answer", appeal.Revision, "answered", "已复核，原处置保持。"), new("appeal-answer"));
        Assert.Equal(2, fixture.Store.RankedIntegrityNotifications(fixture.First).UnreadCount);
        Assert.Single(fixture.Store.RankedIntegrityNotifications(fixture.Second).Items);
        Assert.Contains(fixture.Reload().RankedIntegrityNotifications(fixture.First).Items,
            notice => notice.Outcome == "appeal-answered");
    }

    [Fact]
    public void LegacyNoImpactNotificationsRemainVisibleAndReplayProjectsActualSavedRecipients()
    {
        using var fixture = new Fixture();
        fixture.Settle("legacy");
        var input = Input("legacy-request", "review", "legacy");
        var decision = fixture.Confirm(input);
        // Recreate the legacy notification row shape, not a new decision/storage format.
        var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance
            | BindingFlags.NonPublic)!.GetValue(fixture.Store)!;
        var notices = (IList)data.GetType().GetProperty("RankedIntegrityNotifications")!.GetValue(data)!;
        var row = Activator.CreateInstance(notices.GetType().GetGenericArguments()[0], nonPublic: true)!;
        foreach (var (property, value) in new (string, object)[]
        {
            ("AccountId", fixture.First.Id), ("DecisionId", decision.DecisionId),
            ("MatchIds", new List<string> { "legacy" }), ("Outcome", "review"),
            ("Reason", input.Reason), ("DecidedAt", decision.CreatedAt),
        }) row.GetType().GetProperty(property)!.SetValue(row, value);
        notices.Add(row);
        Assert.True(fixture.Store.Register("savelegacy", "Password123!").Success);
        var reloaded = fixture.Reload();
        var saved = Assert.Single(reloaded.RankedIntegrityNotifications(fixture.First).Items);
        Assert.Equal("review", saved.Outcome);
        Assert.Equal(1, reloaded.RankedIntegrityNotifications(fixture.First).UnreadCount);
        AssertSilent(reloaded, fixture.Second);
        var replay = reloaded.ConfirmRankedIntegrityAction(fixture.Admin, input, -1, new("legacy-replay"));
        Assert.Equal(new[] { fixture.First.Id }, replay.PlayerNotificationAccountIds);
        Assert.Equal(replay.PlayerNotificationAccountIds,
            reloaded.PreviewRankedIntegrityAction(fixture.Admin, input).PlayerNotificationAccountIds);
        Assert.Equal(saved.Id, Assert.Single(reloaded.RankedIntegrityNotifications(fixture.First).Items).Id);
    }

    [Theory]
    [InlineData("review")]
    [InlineData("confirmed")]
    public void FailedSaveRollsBackDecisionAuditNotificationsAndAllPlayerEffects(string disposition)
    {
        using var fixture = new Fixture();
        fixture.Settle("abort");
        var firstBefore = fixture.Store.RankedProfile(fixture.First.Id);
        var secondBefore = fixture.Store.RankedProfile(fixture.Second.Id);
        var input = Input("abort-request", disposition, "abort");
        var preview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, input);
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-commit") throw new IOException("synthetic integrity commit failure");
        };
        try
        {
            Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.ConfirmRankedIntegrityAction(fixture.Admin,
                input, preview.Revision, new("abort-request")));
        }
        finally { fixture.Store.StorageFailureInjector = null; }
        Assert.Empty(fixture.Store.RankedIntegrityDecisions(fixture.Admin).Items);
        Assert.Empty(fixture.Store.AdminAudit(category: "ranked-integrity"));
        Assert.Equal(preview.Revision, fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, input).Revision);
        foreach (var store in new[] { fixture.Store, fixture.Reload() })
        {
            AssertSilent(store, fixture.First);
            AssertSilent(store, fixture.Second);
            Assert.Equal(firstBefore, store.RankedProfile(fixture.First.Id));
            Assert.Equal(secondBefore, store.RankedProfile(fixture.Second.Id));
            Assert.Equal("applied", store.RankedSettlement("abort", fixture.First.Id)!.RewardStatus);
            Assert.Empty(store.RankedIntegrityDecisions(fixture.Admin).Items);
        }
        fixture.Confirm(input); // The same request can succeed after a failed commit.
    }

    [Fact]
    public async Task HttpNoImpactConfirmDoesNotAdvancePlayerPushRevisionAndActualSanctionDoes()
    {
        using var fixture = new Fixture();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            await using var recorder = new MatchRecorder(Path.Combine(fixture.Root, "matches.db"));
            await recorder.InitializeAsync();
            await using var server = new L12WebSocketServer(new L12RoomManager(catalog, recorder,
                fixture.Store), recorder, fixture.Store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            foreach (var disposition in new[] { "review", "normal", "insufficient" })
            {
                fixture.Settle(disposition);
                var input = Input($"http-{disposition}", disposition, disposition);
                var preview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, input);
                using var response = await Post(input, preview.Revision);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                foreach (var account in fixture.Players)
                {
                    Assert.Equal(0, Revision(server, account.Id));
                    AssertSilent(fixture.Store, account);
                }
            }
            fixture.Settle("http-sanction");
            var sanction = Input("http-sanction", "confirmed", "http-sanction");
            var sanctionPreview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, sanction);
            using var sanctionResponse = await Post(sanction, sanctionPreview.Revision);
            Assert.Equal(HttpStatusCode.OK, sanctionResponse.StatusCode);
            foreach (var account in fixture.Players) Assert.Equal(1, Revision(server, account.Id));

            fixture.Settle("http-abort");
            var abort = Input("http-abort", "review", "http-abort");
            var abortPreview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, abort);
            var injectedInsideConfirm = false;
            fixture.Store.StorageFailureInjector = stage =>
            {
                // Authentication may also persist session activity. Fail only after this decision
                // has reached the real confirmation transaction, not in an earlier HTTP guard.
                if (stage == "before-commit" && fixture.Store.RankedIntegrityDecisions(fixture.Admin)
                        .Items.Any(row => row.RequestId == abort.RequestId))
                {
                    injectedInsideConfirm = true;
                    throw new IOException("synthetic HTTP confirm commit failure");
                }
            };
            using var failedResponse = await Post(abort, abortPreview.Revision);
            fixture.Store.StorageFailureInjector = null;
            Assert.True(injectedInsideConfirm);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failedResponse.StatusCode);
            Assert.DoesNotContain(fixture.Store.RankedIntegrityDecisions(fixture.Admin).Items,
                row => row.RequestId == abort.RequestId);
            foreach (var account in fixture.Players) Assert.Equal(1, Revision(server, account.Id));
            await server.StopAsync();

            async Task<HttpResponseMessage> Post(L12RankedIntegrityActionInput input, long expectedRevision)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/ranked/integrity/decisions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminToken);
                request.Content = JsonContent.Create(new { input, expectedRevision });
                return await client.SendAsync(request);
            }
        }
        finally
        {
            fixture.Store.StorageFailureInjector = null;
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
        }
    }

    private static long Revision(L12WebSocketServer server, string accountId)
        => (long)typeof(L12WebSocketServer).GetMethod("AccountResourceRevision",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(server, [accountId, "rankedIntegrity"])!;

    private static void AssertSilent(L12PlatformStore store, L12AccountView account)
    {
        var all = store.RankedIntegrityNotifications(account);
        Assert.Empty(all.Items);
        Assert.Equal(0, all.UnreadCount);
        Assert.Empty(store.RankedIntegrityNotifications(account, unreadOnly: true).Items);
    }

    private static L12RankedIntegrityActionInput Input(string request, string disposition, string match)
        => new(request, disposition, [match], [], null, "已核对服务器结算与完整性证据。", "按证据登记复核结论。");

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ri-" + Guid.NewGuid().ToString("N"));
        public L12PlatformStore Store { get; }
        public L12AccountView First { get; }
        public L12AccountView Second { get; }
        public L12AccountView Admin { get; }
        public string AdminToken { get; }
        public L12AccountView[] Players => [First, Second];

        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Store = new L12PlatformStore(Path.Combine(Root, "platform.json"));
            var first = Store.Register("firstone", "Password123!");
            var second = Store.Register("secondone", "Password123!");
            Assert.True(first.Success, first.Message);
            Assert.True(second.Success, second.Message);
            First = first.Account!;
            Second = second.Account!;
            var admin = Store.Login("Admin", "L12master");
            Assert.True(admin.Success, admin.Message);
            Admin = admin.Account!;
            AdminToken = admin.Token!;
            Store.SelectRankedFaction(First.Id, "order");
            Store.SelectRankedFaction(Second.Id, "chaos");
        }

        public void Settle(string id, int winner = 0) => Store.SettleRankedMatch(id, First.Id, Second.Id, winner);
        public L12PlatformStore Reload() => new(Path.Combine(Root, "platform.json"));
        public L12RankedIntegrityDecisionView Confirm(L12RankedIntegrityActionInput input)
        {
            var preview = Store.PreviewRankedIntegrityAction(Admin, input);
            Assert.True(preview.CanConfirm, string.Join(";", preview.BlockingReasons));
            return Store.ConfirmRankedIntegrityAction(Admin, input, preview.Revision, new(input.RequestId));
        }
        public void Hold()
        {
            var baseTime = DateTimeOffset.UtcNow.AddMinutes(-3);
            for (var index = 0; index < 3; index++)
            {
                var endedAt = baseTime.AddMinutes(index);
                Store.SettleRankedMatch($"held-{index}", First.Id, Second.Id, 0,
                    integrity: new(endedAt.AddSeconds(-45), endedAt, 0, "surrender", null, null, 1));
            }
            Assert.Equal("held", Store.RankedSettlement("held-2", First.Id)!.RewardStatus);
        }
        public void Dispose()
        {
            Store.StorageFailureInjector = null;
            SqliteConnection.ClearAllPools();
            Directory.Delete(Root, recursive: true);
        }
    }
}
