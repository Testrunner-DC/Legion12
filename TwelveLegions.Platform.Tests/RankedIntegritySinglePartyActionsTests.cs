using System.Text.Json;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RankedIntegritySinglePartyActionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedAccountWithdrawalDoesNotChangeOpponentOrExcludeTheWholeMatch(bool restrict)
    {
        using var fixture = new Fixture();
        var selectedBefore = fixture.PlayerState(fixture.First.Id);
        fixture.Settle("selected", 0);
        var opponentBefore = fixture.PlayerState(fixture.Second.Id);
        var input = Input("one-side", [fixture.First.Id], restrict, fixture.First.Id);
        var preview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, input);
        Assert.True(preview.CanConfirm, string.Join(";", preview.BlockingReasons));
        var decision = fixture.Store.ConfirmRankedIntegrityAction(fixture.Admin, input,
            preview.Revision, new("one-side"));

        Assert.Equal(opponentBefore, fixture.PlayerState(fixture.Second.Id));
        Assert.Equal(selectedBefore, fixture.PlayerState(fixture.First.Id));
        Assert.Equal("voided", fixture.Store.RankedSettlement("selected", fixture.First.Id)!.RewardStatus);
        Assert.Equal("applied", fixture.Store.RankedSettlement("selected", fixture.Second.Id)!.RewardStatus);
        Assert.DoesNotContain("selected", fixture.Store.RankedIntegrityExcludedMatchIds());
        Assert.Equal(fixture.First.Id, Assert.Single(decision.AccountEffects).AccountId);
        Assert.Equal(new[] { fixture.First.Id }, decision.PlayerNotificationAccountIds);
        Assert.Equal(decision.DecisionId, Assert.Single(
            fixture.Store.RankedIntegrityNotifications(fixture.First).Items).DecisionId);
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(fixture.Second).Items);
        Assert.Equal(restrict, fixture.Store.RankedEntryBlock(fixture.First.Id, DateTimeOffset.UtcNow) is not null);
        Assert.Null(fixture.Store.RankedEntryBlock(fixture.Second.Id, DateTimeOffset.UtcNow));

        var reloaded = fixture.Reload();
        Assert.Equal(opponentBefore, PlayerState(reloaded, fixture.Second.Id));
        Assert.Equal(selectedBefore, PlayerState(reloaded, fixture.First.Id));
        Assert.Equal("voided", reloaded.RankedSettlement("selected", fixture.First.Id)!.RewardStatus);
        Assert.Equal("applied", reloaded.RankedSettlement("selected", fixture.Second.Id)!.RewardStatus);
        Assert.Empty(reloaded.RankedIntegrityNotifications(fixture.Second).Items);
        Assert.Equal(restrict, reloaded.RankedEntryBlock(fixture.First.Id, DateTimeOffset.UtcNow) is not null);
    }

    [Fact]
    public void ChangingTheSelectedAccountCannotReplayTheSameRequest()
    {
        using var fixture = new Fixture();
        fixture.Settle("selected", 0);
        var input = Input("scope-replay", [fixture.First.Id], false, fixture.First.Id);
        var decision = fixture.Confirm(input);
        var replay = fixture.Store.ConfirmRankedIntegrityAction(fixture.Admin, input, -1, new("retry"));
        Assert.Equal(decision.DecisionId, replay.DecisionId);
        Assert.Single(fixture.Store.RankedIntegrityNotifications(fixture.First).Items);
        Assert.Throws<L12RankedIntegrityActionException>(() => fixture.Store.ConfirmRankedIntegrityAction(
            fixture.Admin, Input("scope-replay", [fixture.Second.Id], false, fixture.Second.Id),
            -1, new("changed-target")));
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(fixture.Second).Items);
    }

    [Theory]
    [InlineData("outsider")]
    [InlineData("")]
    public void EmptyOrNonParticipantTargetIsRejectedBeforeAnyMutation(string target)
    {
        using var fixture = new Fixture();
        fixture.Settle("selected", 0);
        var beforeFirst = fixture.PlayerState(fixture.First.Id);
        var beforeSecond = fixture.PlayerState(fixture.Second.Id);
        var input = Input("invalid-target", target.Length == 0 ? [] : [target], false, target);
        Assert.Throws<L12RankedIntegrityActionException>(() =>
            fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, input));
        Assert.Equal(beforeFirst, fixture.PlayerState(fixture.First.Id));
        Assert.Equal(beforeSecond, fixture.PlayerState(fixture.Second.Id));
        Assert.Empty(fixture.Store.RankedIntegrityDecisions(fixture.Admin).Items);
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(fixture.First).Items);
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(fixture.Second).Items);
    }

    [Fact]
    public void FailedSaveDoesNotLeaveEitherAccountPartiallyDisposed()
    {
        using var fixture = new Fixture();
        fixture.Settle("selected", 0);
        var beforeFirst = fixture.PlayerState(fixture.First.Id);
        var beforeSecond = fixture.PlayerState(fixture.Second.Id);
        var input = Input("failed-one-side", [fixture.First.Id], true, fixture.First.Id);
        var preview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, input);
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-commit") throw new IOException("synthetic single-party commit failure");
        };
        try
        {
            Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.ConfirmRankedIntegrityAction(
                fixture.Admin, input, preview.Revision, new("failed-one-side")));
        }
        finally { fixture.Store.StorageFailureInjector = null; }
        foreach (var store in new[] { fixture.Store, fixture.Reload() })
        {
            Assert.Equal(beforeFirst, PlayerState(store, fixture.First.Id));
            Assert.Equal(beforeSecond, PlayerState(store, fixture.Second.Id));
            Assert.Empty(store.RankedIntegrityDecisions(fixture.Admin).Items);
            Assert.Empty(store.AdminAudit(category: "ranked-integrity"));
            Assert.Empty(store.RankedIntegrityNotifications(fixture.First).Items);
            Assert.Empty(store.RankedIntegrityNotifications(fixture.Second).Items);
            Assert.Null(store.RankedEntryBlock(fixture.First.Id, DateTimeOffset.UtcNow));
            Assert.Null(store.RankedEntryBlock(fixture.Second.Id, DateTimeOffset.UtcNow));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectingTheLoserCannotRefundTheirLostScoreOrHiddenRating(bool restrict)
    {
        using var fixture = new Fixture();
        fixture.Settle("selected", 0);
        var loss = fixture.Store.RankedSettlement("selected", fixture.Second.Id)!;
        Assert.Equal("loss", loss.Outcome);
        Assert.True(loss.Delta < 0, "The fixture must contain a real negative settlement, not just a zero-point loss.");
        AssertBlockedWithoutChanges(fixture, Input("loser", [fixture.Second.Id], restrict, fixture.Second.Id));
    }

    [Fact]
    public void SubsequentSettlementsCannotBeGuessedForAnIndividualWithdrawal()
    {
        using var fixture = new Fixture();
        fixture.Settle("selected", 0);
        fixture.Settle("later", 1);
        AssertBlockedWithoutChanges(fixture, Input("not-latest", [fixture.First.Id], false, fixture.First.Id));
    }

    [Fact]
    public void MissingExactFactsCannotFallBackToTheWholeMatchEloInverse()
    {
        using var fixture = new Fixture();
        fixture.Settle("selected", 0);
        var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Store)!;
        ((IList)data.GetType().GetProperty("RankedSettlementProfileFacts")!.GetValue(data)!).Clear();
        AssertBlockedWithoutChanges(fixture, Input("no-facts", [fixture.First.Id], false, fixture.First.Id));
    }

    [Fact]
    public void AChangedHiddenRatingCannotBeOverwrittenByTheStoredBeforeSnapshot()
    {
        using var fixture = new Fixture();
        fixture.Settle("selected", 0);
        var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Store)!;
        var rows = (IEnumerable)data.GetType().GetProperty("RankedProfiles")!.GetValue(data)!;
        var profileRow = rows.Cast<object>().Single(row => (string)row.GetType().GetProperty("AccountId")!.GetValue(row)! == fixture.First.Id);
        var rating = profileRow.GetType().GetProperty("HiddenRating")!;
        rating.SetValue(profileRow, (double)rating.GetValue(profileRow)! + 1);
        AssertBlockedWithoutChanges(fixture, Input("rating-mismatch", [fixture.First.Id], false, fixture.First.Id));
    }

    [Fact]
    public void RevocationRestoresOnlyTheSelectedAccountEvenWhenTheOpponentHasPlayedAgain()
    {
        using var fixture = new Fixture();
        fixture.Settle("selected", 0);
        var selectedApplied = fixture.PlayerState(fixture.First.Id);
        var decision = fixture.Confirm(Input("revoke-original", [fixture.First.Id], true, fixture.First.Id));
        var third = fixture.Store.Register("thirdone", "Password123!");
        Assert.True(third.Success, third.Message);
        fixture.Store.SelectRankedFaction(third.Account!.Id, "fate");
        fixture.Store.SettleRankedMatch("opponent-later", fixture.Second.Id, third.Account.Id, 0);
        var opponentAfterLaterMatch = fixture.PlayerState(fixture.Second.Id);
        var revoke = Input("revoke-own-only", [fixture.First.Id], false, fixture.First.Id, decision.DecisionId);
        var undone = fixture.Confirm(revoke);
        Assert.Equal("account", undone.Scope);
        Assert.Equal(new[] { fixture.First.Id }, undone.TargetAccountIds);
        Assert.Equal(selectedApplied, fixture.PlayerState(fixture.First.Id));
        Assert.Equal(opponentAfterLaterMatch, fixture.PlayerState(fixture.Second.Id));
        Assert.Equal("applied", fixture.Store.RankedSettlement("selected", fixture.First.Id)!.RewardStatus);
        Assert.Equal("applied", fixture.Store.RankedSettlement("selected", fixture.Second.Id)!.RewardStatus);
        Assert.Null(fixture.Store.RankedEntryBlock(fixture.First.Id, DateTimeOffset.UtcNow));
        Assert.Equal(2, fixture.Store.RankedIntegrityNotifications(fixture.First).Items.Count);
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(fixture.Second).Items);
        Assert.Equal(selectedApplied, PlayerState(fixture.Reload(), fixture.First.Id));
    }

    [Fact]
    public void RevocationCannotExpandItsOriginalTargetOrTurnIntoAWholeMatchCorrection()
    {
        using var fixture = new Fixture();
        fixture.Settle("selected", 0);
        var decision = fixture.Confirm(Input("exact-revoke", [fixture.First.Id], false, fixture.First.Id));
        Assert.Throws<L12RankedIntegrityActionException>(() => fixture.Store.PreviewRankedIntegrityAction(fixture.Admin,
            Input("wrong-revoke", [fixture.Second.Id], false, fixture.Second.Id, decision.DecisionId)));
        var whole = new L12RankedIntegrityActionInput("whole-revoke", "revoked", ["selected"], [], null,
            "合成证据", "不得把个人撤销扩成整场", decision.DecisionId);
        Assert.Throws<L12RankedIntegrityActionException>(() => fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, whole));
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(fixture.Second).Items);
    }

    [Fact]
    public void APersonalFindingDoesNotClearTheOtherParticipantOrTheWholeReviewQueue()
    {
        using var fixture = new Fixture();
        fixture.SettleRisky("selected");
        var before = Assert.Single(fixture.Store.RankedIntegrityAudits(fixture.Admin, matchId: "selected", reviewOnly: true));
        Assert.Equal("none", before.Enforcement);
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(fixture.First).Items);
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(fixture.Second).Items);
        Assert.Equal("applied", fixture.Store.RankedSettlement("selected", fixture.Second.Id)!.RewardStatus);
        fixture.Confirm(Input("partial-finding", [fixture.First.Id], false, fixture.First.Id));
        var audit = Assert.Single(fixture.Store.RankedIntegrityAudits(fixture.Admin, matchId: "selected", reviewOnly: true));
        Assert.Equal("partial", audit.EffectiveDisposition);
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(fixture.Second).Items);
        Assert.Equal("applied", fixture.Store.RankedSettlement("selected", fixture.Second.Id)!.RewardStatus);
        Assert.DoesNotContain("selected", fixture.Store.ReadCommittedStatisticsExclusions().MatchIds);
        Assert.DoesNotContain(fixture.First.Id, fixture.Store.ReadCommittedStatisticsExclusions().AccountIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticHeldRewardsCannotBeIndividuallyWithdrawnOrBlockTheOtherParticipant(bool restrict)
    {
        using var fixture = new Fixture();
        var ended = DateTimeOffset.UtcNow;
        fixture.Store.SettleRankedMatch("selected", fixture.First.Id, fixture.Second.Id, 0,
            integrity: new(ended.AddSeconds(-40), ended, 0, "surrender", null, null, 1));
        var audit = Assert.Single(fixture.Store.RankedIntegrityAudits(fixture.Admin, matchId: "selected"));
        Assert.Equal("reward-held", audit.Enforcement);
        Assert.Contains(audit.Signals, signal => signal.Code == "repeated-unilateral-extreme-pair");
        foreach (var account in new[] { fixture.First, fixture.Second })
        {
            Assert.Equal("held", fixture.Store.RankedSettlement("selected", account.Id)!.RewardStatus);
            var notice = Assert.Single(fixture.Store.RankedIntegrityNotifications(account).Items);
            Assert.Equal("review", notice.Outcome);
            Assert.Equal(1, fixture.Store.RankedIntegrityNotifications(account).UnreadCount);
        }
        Assert.Contains("selected", fixture.Store.RankedIntegrityExcludedMatchIds());
        var before = HeldStateHash(fixture, fixture.Store);
        var input = Input("held-must-stay-whole", [fixture.First.Id], restrict, fixture.First.Id);
        var preview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, input);
        Assert.False(preview.CanConfirm);
        Assert.NotEmpty(preview.BlockingReasons);
        Assert.Empty(preview.PlayerNotificationAccountIds);
        Assert.Equal(before, HeldStateHash(fixture, fixture.Store));
        Assert.Throws<L12RankedIntegrityActionException>(() => fixture.Store.ConfirmRankedIntegrityAction(
            fixture.Admin, input, preview.Revision, new("held-must-stay-whole")));
        Assert.Equal(before, HeldStateHash(fixture, fixture.Store));
        Assert.Equal(before, HeldStateHash(fixture, fixture.Reload()));
    }

    [Theory]
    [InlineData("foreign-account")]
    [InlineData("foreign-match")]
    [InlineData("empty-matches")]
    [InlineData("confirmed-revoke-kind")]
    [InlineData("revoked-void-kind")]
    [InlineData("revoked-noninverse")]
    public void RecoveryRejectsPersonalCorrectionsOutsideTheirDecisionScopeEvenWithValidSnapshotHashes(string corruption)
    {
        using var fixture = new Fixture();
        fixture.Settle("selected", 0);
        var original = fixture.Confirm(Input("stored-scope", [fixture.First.Id], false, fixture.First.Id));
        var isRevocation = corruption.StartsWith("revoked-", StringComparison.Ordinal);
        var related = isRevocation
            ? fixture.Confirm(Input("stored-revoke", [fixture.First.Id], false, fixture.First.Id, original.DecisionId))
            : original;
        MutatePersistedCorrection(fixture, related.DecisionId, correction =>
        {
            switch (corruption)
            {
                case "foreign-account":
                    correction["AccountId"] = fixture.Second.Id;
                    correction["Before"]!["AccountId"] = fixture.Second.Id;
                    correction["After"]!["AccountId"] = fixture.Second.Id;
                    break;
                case "foreign-match": correction["MatchIds"] = new JsonArray("placement-0"); break;
                case "empty-matches": correction["MatchIds"] = new JsonArray(); break;
                case "confirmed-revoke-kind": correction["Kind"] = "revoke"; break;
                case "revoked-void-kind": correction["Kind"] = "void-applied"; break;
                case "revoked-noninverse":
                    correction["Before"]!["SevenValue"] = correction["Before"]!["SevenValue"]!.GetValue<int>() + 1;
                    break;
            }
        });
        var storedBefore = StoredSnapshotState(fixture);
        // All former generic checks remain satisfied: known decision/account, matching snapshot
        // account ids and non-null MatchIds. The defect is the missing personal-scope binding.
        using (var connection = OpenFixtureDatabase(fixture))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT snapshot_json,snapshot_sha256 FROM platform_state WHERE singleton_id=1;";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            var body = reader.GetString(0);
            Assert.Equal(HashText(body), reader.GetString(1));
            var data = JsonNode.Parse(body)!;
            var decision = data["RankedIntegrityDecisions"]!.AsArray().Single(row => row!["Id"]!.GetValue<string>() == related.DecisionId)!;
            var correction = data["RankedIntegrityCorrections"]!.AsArray().Single(row => row!["DecisionId"]!.GetValue<string>() == related.DecisionId)!;
            Assert.Equal("account", decision["Scope"]!.GetValue<string>());
            Assert.Equal(fixture.First.Id, Assert.Single(decision["TargetAccountIds"]!.AsArray())!.GetValue<string>());
            var accountId = correction["AccountId"]!.GetValue<string>();
            Assert.Contains(accountId, new[] { fixture.First.Id, fixture.Second.Id });
            Assert.Equal(accountId, correction["Before"]!["AccountId"]!.GetValue<string>());
            Assert.Equal(accountId, correction["After"]!["AccountId"]!.GetValue<string>());
            Assert.NotNull(correction["MatchIds"]);
        }
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Reload());
        Assert.Equal(storedBefore, StoredSnapshotState(fixture));
    }

    [Theory]
    [InlineData("missing-confirmed")]
    [InlineData("missing-revoke")]
    [InlineData("duplicate-target")]
    [InlineData("partial-matches")]
    public void RecoveryRequiresTheCompleteUniquePersonalCorrectionSet(string corruption)
    {
        using var fixture = new Fixture();
        fixture.Settle("selected", 0);
        var input = Input("complete-set", [fixture.First.Id], false, fixture.First.Id);
        if (corruption == "partial-matches")
        {
            fixture.Settle("selected-two", 0);
            input = input with { MatchIds = ["selected", "selected-two"] };
        }
        var original = fixture.Confirm(input);
        var related = corruption == "missing-revoke"
            ? fixture.Confirm(Input("complete-set-revoke", [fixture.First.Id], false, fixture.First.Id, original.DecisionId))
            : original;
        var duplicateId = Guid.NewGuid().ToString("N");
        MutatePersistedCorrections(fixture, corrections =>
        {
            var correction = corrections.Single(row => row!["DecisionId"]!.GetValue<string>() == related.DecisionId)!.AsObject();
            if (corruption.StartsWith("missing-", StringComparison.Ordinal))
                Assert.True(corrections.Remove(correction));
            else if (corruption == "duplicate-target")
            {
                var duplicate = correction.DeepClone().AsObject();
                duplicate["Id"] = duplicateId; // Preserve uniqueness so the old ID guard is not the refusal.
                corrections.Add(duplicate);
            }
            else correction["MatchIds"] = new JsonArray("selected"); // A valid subset, but not the complete actual scope.
        });
        using (var connection = OpenFixtureDatabase(fixture))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT snapshot_json,snapshot_sha256 FROM platform_state WHERE singleton_id=1;";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            var body = reader.GetString(0);
            Assert.Equal(HashText(body), reader.GetString(1));
            var data = JsonNode.Parse(body)!;
            var decision = data["RankedIntegrityDecisions"]!.AsArray()
                .Single(row => row!["Id"]!.GetValue<string>() == related.DecisionId)!;
            var corrections = data["RankedIntegrityCorrections"]!.AsArray();
            Assert.Equal(corrections.Count, corrections.Select(row => row!["Id"]!.GetValue<string>()).Distinct().Count());
            var scoped = corrections.Where(row => row!["DecisionId"]!.GetValue<string>() == related.DecisionId).ToArray();
            Assert.Equal(corruption.StartsWith("missing-", StringComparison.Ordinal) ? 0 : corruption == "duplicate-target" ? 2 : 1, scoped.Length);
            foreach (var row in scoped)
            {
                Assert.Equal(fixture.First.Id, row!["AccountId"]!.GetValue<string>());
                Assert.Equal("void-applied", row["Kind"]!.GetValue<string>());
                var matches = row["MatchIds"]!.AsArray().Select(id => id!.GetValue<string>()).ToArray();
                Assert.NotEmpty(matches);
                Assert.All(matches, id => Assert.Contains(id, decision["MatchIds"]!.AsArray().Select(match => match!.GetValue<string>())));
            }
        }
        var before = StoredSnapshotState(fixture);
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Reload());
        Assert.Equal(before, StoredSnapshotState(fixture));
    }

    [Fact]
    public void PersonalCorrectionMatchesAreEachTargetsActualIntersectionNotEverySelectedMatch()
    {
        using var fixture = new Fixture();
        fixture.Settle("selected", 0);
        var third = fixture.Store.Register("thirdone", "Password123!");
        var fourth = fixture.Store.Register("fourthone", "Password123!");
        Assert.True(third.Success, third.Message);
        Assert.True(fourth.Success, fourth.Message);
        fixture.Store.SelectRankedFaction(third.Account!.Id, "fate");
        fixture.Store.SelectRankedFaction(fourth.Account!.Id, "chaos");
        fixture.Store.SettleRankedMatch("second-selected", third.Account.Id, fourth.Account.Id, 0);
        var input = Input("disjoint-targets", [fixture.First.Id, third.Account.Id], false, fixture.First.Id)
            with { MatchIds = ["selected", "second-selected"] };
        var decision = fixture.Confirm(input);
        var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Store)!;
        var rows = ((IEnumerable)data.GetType().GetProperty("RankedIntegrityCorrections")!.GetValue(data)!).Cast<object>().ToArray();
        Assert.Equal(2, rows.Length);
        foreach (var row in rows)
        {
            var accountId = (string)row.GetType().GetProperty("AccountId")!.GetValue(row)!;
            var matches = (IEnumerable<string>)row.GetType().GetProperty("MatchIds")!.GetValue(row)!;
            Assert.Equal(new[] { accountId == fixture.First.Id ? "selected" : "second-selected" }, matches);
        }
        Assert.Equal(2, decision.TargetAccountIds.Count);
        Assert.Equal("voided", fixture.Reload().RankedSettlement("selected", fixture.First.Id)!.RewardStatus);
        Assert.Equal("voided", fixture.Reload().RankedSettlement("second-selected", third.Account.Id)!.RewardStatus);
        Assert.Equal("applied", fixture.Store.RankedSettlement("selected", fixture.Second.Id)!.RewardStatus);
        Assert.Equal("applied", fixture.Store.RankedSettlement("second-selected", fourth.Account.Id)!.RewardStatus);
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(fixture.Second).Items);
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(fourth.Account).Items);
    }

    private static string HeldStateHash(Fixture fixture, L12PlatformStore store)
    {
        var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(store)!;
        var facts = new[] { "RankedHeldRewards", "RankedSettlementProfileFacts", "RankedIntegrityCorrections" }
            .ToDictionary(name => name, name => JsonSerializer.Serialize(data.GetType().GetProperty(name)!.GetValue(data)));
        return HashText(JsonSerializer.Serialize(new
        {
            store.Version, First = PlayerState(store, fixture.First.Id), Second = PlayerState(store, fixture.Second.Id),
            FirstSettlement = store.RankedSettlement("selected", fixture.First.Id),
            SecondSettlement = store.RankedSettlement("selected", fixture.Second.Id),
            Decisions = store.RankedIntegrityDecisions(fixture.Admin),
            FirstNotices = store.RankedIntegrityNotifications(fixture.First),
            SecondNotices = store.RankedIntegrityNotifications(fixture.Second),
            Audit = store.AdminAudit(category: "ranked-integrity"),
            Excluded = store.RankedIntegrityExcludedMatchIds().Order(StringComparer.Ordinal).ToArray(),
            CommittedExcluded = store.ReadCommittedStatisticsExclusions().MatchIds.Order(StringComparer.Ordinal).ToArray(),
            Facts = facts,
        }));
    }

    private static string HashText(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static SqliteConnection OpenFixtureDatabase(Fixture fixture)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.Store.TransactionalStoragePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void MutatePersistedCorrection(Fixture fixture, string decisionId, Action<JsonObject> mutate)
        => MutatePersistedCorrections(fixture, corrections => mutate(corrections
            .Single(row => row!["DecisionId"]!.GetValue<string>() == decisionId)!.AsObject()));

    private static void MutatePersistedCorrections(Fixture fixture, Action<JsonArray> mutate)
    {
        using var connection = OpenFixtureDatabase(fixture);
        using var transaction = connection.BeginTransaction();
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT snapshot_json FROM platform_state WHERE singleton_id=1;";
        var core = JsonNode.Parse((string)read.ExecuteScalar()!)!.AsObject();
        var mirror = JsonNode.Parse(File.ReadAllText(fixture.MirrorPath))!.AsObject();
        foreach (var data in new[] { core, mirror }) mutate(data["RankedIntegrityCorrections"]!.AsArray());
        var coreJson = core.ToJsonString();
        var mirrorJson = mirror.ToJsonString();
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE platform_state SET snapshot_json=$json,snapshot_sha256=$hash WHERE singleton_id=1;
            UPDATE storage_meta SET value=$mirror WHERE key='fallback_json_sha256';
            """;
        update.Parameters.AddWithValue("$json", coreJson);
        update.Parameters.AddWithValue("$hash", HashText(coreJson));
        update.Parameters.AddWithValue("$mirror", HashText(mirrorJson));
        update.ExecuteNonQuery();
        transaction.Commit();
        File.WriteAllText(fixture.MirrorPath, mirrorJson);
    }

    private static string StoredSnapshotState(Fixture fixture)
    {
        using var connection = OpenFixtureDatabase(fixture);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT snapshot_json,snapshot_sha256,storage_revision,business_version,
                (SELECT value FROM storage_meta WHERE key='fallback_json_sha256')
            FROM platform_state WHERE singleton_id=1;
            """;
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        return HashText(JsonSerializer.Serialize(new
        {
            Core = reader.GetString(0), CoreHash = reader.GetString(1), Storage = reader.GetInt64(2),
            Business = reader.GetInt64(3), MirrorHash = reader.GetString(4),
            Mirror = File.ReadAllText(fixture.MirrorPath),
        }));
    }

    private static void AssertBlockedWithoutChanges(Fixture fixture, L12RankedIntegrityActionInput input)
    {
        var first = fixture.PlayerState(fixture.First.Id);
        var second = fixture.PlayerState(fixture.Second.Id);
        var preview = fixture.Store.PreviewRankedIntegrityAction(fixture.Admin, input);
        Assert.False(preview.CanConfirm);
        Assert.NotEmpty(preview.BlockingReasons);
        Assert.Empty(preview.PlayerNotificationAccountIds);
        Assert.Throws<L12RankedIntegrityActionException>(() => fixture.Store.ConfirmRankedIntegrityAction(fixture.Admin,
            input, preview.Revision, new(input.RequestId)));
        Assert.Equal(first, fixture.PlayerState(fixture.First.Id));
        Assert.Equal(second, fixture.PlayerState(fixture.Second.Id));
        Assert.Empty(fixture.Store.RankedIntegrityDecisions(fixture.Admin).Items);
        Assert.Empty(fixture.Store.AdminAudit(category: "ranked-integrity"));
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(fixture.First).Items);
        Assert.Empty(fixture.Store.RankedIntegrityNotifications(fixture.Second).Items);
    }

    // Keep the future wire input explicit so the baseline suite compiles against the old DTO:
    // the old deserializer silently drops scope/targetAccountIds and really executes a whole-match action.
    // The regression must observe that real wrong behavior, not fail because a new C# member is absent.
    private static L12RankedIntegrityActionInput Input(string requestId, string[] targets,
        bool restrict, string restrictedAccount, string? revokesDecisionId = null)
        => JsonSerializer.Deserialize<L12RankedIntegrityActionInput>(JsonSerializer.Serialize(new
        {
            requestId, disposition = revokesDecisionId is null ? "confirmed" : "revoked",
            scope = "account", targetAccountIds = targets, revokesDecisionId,
            matchIds = new[] { "selected" },
            restrictedAccountIds = restrict ? new[] { restrictedAccount } : Array.Empty<string>(),
            restrictionDays = restrict ? (int?)7 : null,
            evidence = "已核对合成对局的原始结算及账号范围。", reason = "仅对明确选中的账号撤回该场收益。",
        }), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static string PlayerState(L12PlatformStore store, string id)
        => JsonSerializer.Serialize(new { Profile = store.RankedProfile(id), HiddenRating = store.HiddenRating(id) });

    private sealed class Fixture : IDisposable
    {
        private string Root { get; } = Path.Combine(Path.GetTempPath(), "ri1-" + Guid.NewGuid().ToString("N"));
        public string MirrorPath => Path.Combine(Root, "platform.json");
        public L12PlatformStore Store { get; }
        public L12AccountView Admin { get; }
        public L12AccountView First { get; }
        public L12AccountView Second { get; }

        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Store = new L12PlatformStore(Path.Combine(Root, "platform.json"));
            var first = Store.Register("firstone", "Password123!");
            var second = Store.Register("secondone", "Password123!");
            var admin = Store.Login("Admin", "L12master");
            Assert.True(first.Success, first.Message);
            Assert.True(second.Success, second.Message);
            Assert.True(admin.Success, admin.Message);
            First = first.Account!;
            Second = second.Account!;
            Admin = admin.Account!;
            Store.SelectRankedFaction(First.Id, "order");
            Store.SelectRankedFaction(Second.Id, "chaos");
            for (var index = 0; index < 5; index++) Settle($"placement-{index}", index % 2);
        }

        public string PlayerState(string id) => RankedIntegritySinglePartyActionsTests.PlayerState(Store, id);
        public void Settle(string id, int winner) => Store.SettleRankedMatch(id, First.Id, Second.Id, winner);
        public void SettleRisky(string id)
        {
            var ended = DateTimeOffset.UtcNow;
            Store.SettleRankedMatch(id, First.Id, Second.Id, 0,
                integrity: new(ended.AddSeconds(-40), ended, 1, "surrender", null, null, 1));
        }
        public L12PlatformStore Reload() => new(Path.Combine(Root, "platform.json"));
        public L12RankedIntegrityDecisionView Confirm(L12RankedIntegrityActionInput input)
        {
            var preview = Store.PreviewRankedIntegrityAction(Admin, input);
            Assert.True(preview.CanConfirm, string.Join(";", preview.BlockingReasons));
            return Store.ConfirmRankedIntegrityAction(Admin, input, preview.Revision, new(input.RequestId));
        }
        public void Dispose()
        {
            Store.StorageFailureInjector = null;
            SqliteConnection.ClearAllPools();
            Directory.Delete(Root, recursive: true);
        }
    }
}
