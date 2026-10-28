using Microsoft.Data.Sqlite;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class SeasonCutoverSettingsAndFinalPopulationTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task AutomaticCutoverAppliesSavedRankedAndConstructionToRealConsumers(
        int globalMaximum, bool reopenAfterArm)
    {
        var root = TempRoot();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var due = now.AddHours(1);
            var (store, admin, catalog) = Prepare(root, due);
            var first = RegisterRanked(store, "切季设置甲", "order");
            var second = RegisterRanked(store, "切季设置乙", "chaos");
            var originalDecks = store.Decks(first.Id).Select(deck => deck.MasterId).ToArray();
            var before = store.SeasonCatalog(admin, now);
            var oldRanked = store.RankedConfig(admin) with { PendingGradient = null };
            var oldPolicy = store.CaptureOperationsPolicy().ForRankedMatch();
            var allowed = catalog.PresetDecks.First(deck => deck.MasterId == "S01-01M1");
            var restricted = catalog.PresetDecks.First(deck => deck.MasterId == "S01-04M2");
            Assert.True(L12DeckValidator.TryValidatePreset(catalog, allowed, out var allowedError,
                oldPolicy.CardRestrictions), allowedError);
            Assert.True(L12DeckValidator.TryValidatePreset(catalog, restricted, out var restrictedError,
                oldPolicy.CardRestrictions), restrictedError);
            Assert.Equal(3, restricted.CardIds.Count(id => id == "S01-0002"));

            var newRanked = ChangedRanked(before.Next!.Configuration.Ranked);
            var proposed = new L12SeasonDefinitionDraft("S-cutover-settings", "设置联动赛季",
                due, due.AddDays(90), before.Next.Configuration with
                {
                    Ranked = newRanked,
                    CardRestrictions = [
                        new("S01-0002", globalMaximum, "下赛季全局禁限"),
                        new("S01-0002", 3, "下赛季主宰专属上限", allowed.MasterId),
                    ],
                    DefaultPresetDeckIds = [allowed.MasterId],
                });
            var draft = SaveNext(store, admin, before, proposed);
            var armed = Arm(store, admin, draft, now);

            // A saved/armed future slot must not change either current consumer.
            AssertJson(oldRanked, store.RankedConfig(admin) with { PendingGradient = null });
            AssertJson(oldPolicy.CardRestrictions, store.CaptureOperationsPolicy().ForRankedMatch()
                .CardRestrictions);
            Assert.Equal(oldRanked.TimeControl, store.RankedTimeControl());
            Assert.True(L12DeckValidator.TryValidatePreset(catalog, restricted, out _,
                store.CaptureOperationsPolicy().ForRankedMatch().CardRestrictions));
            if (reopenAfterArm)
            {
                store = Open(root, catalog);
                admin = store.Login("Admin", "L12master").Account!;
                Assert.Equal("armed", store.SeasonCatalog(admin, now).Next!.ActivationPlan!.Status);
                AssertJson(oldRanked, store.RankedConfig(admin) with { PendingGradient = null });
            }

            var clock = due.AddTicks(-1);
            await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"), () => clock);
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store, () => clock);
            await using var server = new L12WebSocketServer(rooms, recorder, store, catalog,
                seasonActivationUtcNow: () => clock);
            Assert.False(await server.RunSeasonFinalizationOnceAsync(clock));
            Assert.False(await server.RunSeasonActivationOnceAsync(clock));
            AssertJson(oldRanked, store.RankedConfig(admin) with { PendingGradient = null });

            clock = due;
            Assert.True(await server.RunSeasonFinalizationOnceAsync(clock));
            // Ending the old season alone must not partially install the future settings.
            AssertJson(oldRanked, store.RankedConfig(admin) with { PendingGradient = null });
            AssertJson(oldPolicy.CardRestrictions, store.CaptureOperationsPolicy().ForRankedMatch()
                .CardRestrictions);
            Assert.True(await server.RunSeasonActivationOnceAsync(clock));
            var active = store.SeasonCatalog(admin, clock);
            Assert.Equal(draft.SeasonId, active.Current.SeasonId);
            Assert.Null(active.Next);
            Assert.Equal("completed", active.Current.ActivationPlan!.Status);
            Assert.Equal(armed.ActivationPlan!.ArmedAt, active.Current.ActivationPlan.ArmedAt);
            AssertSettings(store, admin, draft.Configuration);
            AssertJson(oldRanked, Assert.Single(active.Archives).Configuration.Ranked);

            var livePolicy = store.CaptureOperationsPolicy().ForRankedMatch();
            Assert.True(L12DeckValidator.TryValidatePreset(catalog, allowed, out allowedError,
                livePolicy.CardRestrictions), allowedError);
            Assert.False(L12DeckValidator.TryValidatePreset(catalog, restricted, out restrictedError,
                livePolicy.CardRestrictions));
            Assert.Contains("S01-0002", restrictedError);
            Assert.Contains(globalMaximum == 0 ? "禁用" : "最多 1 张", restrictedError);
            // Existing match snapshots and non-season modes retain their own rule scope.
            Assert.True(L12DeckValidator.TryValidatePreset(catalog, restricted, out _,
                oldPolicy.CardRestrictions));
            Assert.Empty(store.CaptureOperationsPolicy().ForCasualMatch().CardRestrictions);

            // Exercise the actual settlement, not just the new config's JSON projection.
            store.SettleRankedMatch("new-season-placement", first.Id, second.Id, 0);
            var placement = store.RankedSettlement("new-season-placement", first.Id)!;
            Assert.True(placement.Placement);
            Assert.Equal(1, placement.PlacementRequired);
            Assert.Equal(1299, placement.After);
            store.SettleRankedMatch("new-season-base", first.Id, second.Id, 0);
            var settled = store.RankedSettlement("new-season-base", first.Id)!;
            Assert.False(settled.Placement);
            Assert.Equal(731, settled.Delta);
            Assert.Equal(731, Assert.Single(settled.Components.Where(item => item.Kind == "base")).Value);
            Assert.Equal(2030, settled.After);
            Assert.Equal(newRanked.Factions[0].Tiers[1].Name, settled.TierAfter);

            var newcomer = store.Register("切季后新账号", "Password123!").Account!;
            Assert.Equal(new[] { allowed.MasterId }, store.Decks(newcomer.Id)
                .Select(deck => deck.MasterId).ToArray());
            Assert.Equal(originalDecks, store.Decks(first.Id).Select(deck => deck.MasterId).ToArray());
            Assert.False(await server.RunSeasonActivationOnceAsync(clock.AddSeconds(1)));
            Assert.Single(store.SeasonArchives(admin));
            var reopened = Open(root, catalog);
            AssertSettings(reopened, reopened.Login("Admin", "L12master").Account!, draft.Configuration);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task FinalPopulationAndResultsAreReadAtSeasonEndNotAtPreviewOrArm()
    {
        var root = TempRoot();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var due = now.AddHours(1);
            var (store, admin, catalog) = Prepare(root, due);
            var first = RegisterRanked(store, "预约前参赛甲", "order");
            var second = RegisterRanked(store, "预约前参赛乙", "chaos");
            var idle = RegisterRanked(store, "仅选派系未参赛", "fate");
            for (var index = 0; index < 6; index++)
                store.SettleRankedMatch($"before-arm-{index}", first.Id, second.Id, 0);
            var atPreview = store.RankedProfile(first.Id);
            var seasons = store.SeasonCatalog(admin, now);
            var draft = SaveNext(store, admin, seasons, new L12SeasonDefinitionDraft(
                "S-final-population", "最终参赛集合赛季", due, due.AddDays(90),
                seasons.Next!.Configuration));
            var preview = store.PreviewSeasonActivation(admin, draft.DefinitionId,
                seasons.Current.Revision, draft.Revision, seasons.OperationsVersion,
                Ready(seasons.Current.SeasonId), now);
            Assert.Equal(2, preview.SettlementParticipantCount);
            Assert.Equal(2, preview.HistoryRecordCount);
            Assert.Equal(2, preview.SummaryNotificationCount);
            store.ArmSeasonActivation(admin, draft.DefinitionId, seasons.Current.Revision,
                draft.Revision, seasons.OperationsVersion, preview.PreviewToken,
                Ready(seasons.Current.SeasonId), "预约并非最终名单", now, Context("arm-population"));
            Assert.Empty(store.PendingSeasonSummaryNotifications(first.Id));

            // Both a new participant and changed scores arrive after the accepted preview/arm.
            var late = RegisterRanked(store, "预约后首次参赛", "fate");
            store.SettleRankedMatch("after-arm-new-player", late.Id, second.Id, 0);
            store.SettleRankedMatch("after-arm-updated-score", first.Id, second.Id, 0);
            var expected = new[] { first, second, late }.ToDictionary(account => account.Id,
                account => store.RankedProfile(account.Id));
            Assert.NotEqual(atPreview.SevenValue, expected[first.Id].SevenValue);
            Assert.Empty(store.RankedOverview(late.Id).History);
            Assert.All(expected.Keys, id => Assert.Empty(store.PendingSeasonSummaryNotifications(id)));
            Assert.Equal("armed", store.SeasonCatalog(admin, now).Next!.ActivationPlan!.Status);

            // Reopen from actual persistent data, then execute the production coordinator entries.
            store = Open(root, catalog);
            admin = store.Login("Admin", "L12master").Account!;
            await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"), () => due);
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store, () => due);
            await using var server = new L12WebSocketServer(rooms, recorder, store, catalog,
                seasonActivationUtcNow: () => due);
            Assert.True(await server.RunSeasonFinalizationOnceAsync(due));
            foreach (var (id, profile) in expected)
            {
                var summary = Assert.Single(store.PendingSeasonSummaryNotifications(id));
                Assert.Equal(seasons.Current.SeasonId, summary.SeasonId);
                Assert.Equal(profile.SevenValue, summary.SevenValue);
                var history = Assert.Single(store.RankedOverview(id).History);
                Assert.Equal(profile.SevenValue, history.SevenValue);
                Assert.Equal(profile.Wins, history.Wins);
                Assert.Equal(profile.Losses, history.Losses);
            }
            Assert.Empty(store.PendingSeasonSummaryNotifications(idle.Id));
            Assert.Empty(store.RankedOverview(idle.Id).History);
            Assert.True(await server.RunSeasonActivationOnceAsync(due));
            Assert.False(await server.RunSeasonFinalizationOnceAsync(due.AddSeconds(1)));
            Assert.False(await server.RunSeasonActivationOnceAsync(due.AddSeconds(1)));
            Assert.All(expected.Keys, id => Assert.Single(store.PendingSeasonSummaryNotifications(id)));
            Assert.Single(store.AdminAudit("operations").Where(item => item.Action == "season-finalize"));
        }
        finally { Cleanup(root); }
    }

    private static L12RankedConfigView ChangedRanked(L12RankedConfigView source)
        => source with
        {
            PlacementMatches = 1,
            PlacementMaximum = 1299,
            BroadcastEnabled = false,
            TimeControl = new(1800, 120, 90, 30, 40),
            Broadcast = new(21, 2, 35, 6, 7, 1, false, true, false, true, false),
            PendingGradient = null,
            Factions = source.Factions.Select(faction => faction with
            {
                Name = $"次季{faction.Name}",
                FirstTitle = $"次季{faction.FirstTitle}",
                TopFiveTitle = $"次季{faction.TopFiveTitle}",
                Tiers = faction.Tiers.Select((tier, index) => tier with
                {
                    Name = $"次季第{index + 1}段",
                    Minimum = new[] { 0, 2000, 5000, 8000, 12000 }[index],
                    BaseDelta = 731,
                    WinStreakCap = 0,
                    LossProtectionCap = 0,
                    RatingGapCap = 0,
                    StreakTerminationReward = 0,
                }).ToArray(),
            }).ToArray(),
            MasterTitles = source.MasterTitles.Select(master => master with
                { Title = $"次季{master.Title}" }).ToArray(),
        };

    private static void AssertSettings(L12PlatformStore store, L12AccountView admin,
        L12SeasonScopedConfig expected)
    {
        AssertJson(expected.Ranked, store.RankedConfig(admin));
        Assert.Equal(expected.Ranked.TimeControl, store.RankedTimeControl());
        Assert.Equal(expected.Ranked.Broadcast, store.RankedBroadcastSettings());
        var operations = store.OperationsConfig(admin).Config;
        AssertJson(expected.CardRestrictions, operations.CardRestrictions);
        Assert.Equal(expected.DefaultPresetDeckIds.ToArray(), operations.DefaultPresetDeckIds.ToArray());
        var policy = store.CaptureOperationsPolicy().ForRankedMatch();
        AssertJson(expected.CardRestrictions, policy.CardRestrictions);
        AssertJson(expected, store.SeasonCatalog(admin).Current.Configuration);
        Assert.Null(store.RankedConfig(admin).PendingGradient);
    }

    private static L12SeasonDefinitionView SaveNext(L12PlatformStore store, L12AccountView admin,
        L12SeasonCatalogView seasons, L12SeasonDefinitionDraft draft)
    {
        var next = seasons.Next!;
        var preview = store.PreviewSeasonDefinition(admin, next.DefinitionId, draft,
            next.Revision, seasons.OperationsVersion, Context("preview-next"));
        Assert.True(preview.Valid);
        return store.ApplySeasonDefinition(admin, next.DefinitionId, preview.Normalized,
            next.Revision, seasons.OperationsVersion, preview.PreviewToken, "保存下赛季全部设置",
            Context("save-next")).Definition;
    }

    private static L12SeasonDefinitionView Arm(L12PlatformStore store, L12AccountView admin,
        L12SeasonDefinitionView draft, DateTimeOffset now)
    {
        var seasons = store.SeasonCatalog(admin, now);
        var ready = Ready(seasons.Current.SeasonId);
        var preview = store.PreviewSeasonActivation(admin, draft.DefinitionId,
            seasons.Current.Revision, draft.Revision, seasons.OperationsVersion, ready, now);
        return store.ArmSeasonActivation(admin, draft.DefinitionId, seasons.Current.Revision,
            draft.Revision, seasons.OperationsVersion, preview.PreviewToken, ready,
            "预约未来切季，不提前生效", now, Context("arm-settings"));
    }

    private static (L12PlatformStore Store, L12AccountView Admin, L12Catalog Catalog) Prepare(
        string root, DateTimeOffset due)
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var store = Open(root, catalog);
        var admin = store.Login("Admin", "L12master").Account!;
        var operations = store.OperationsConfig(admin);
        store.ApplyOperationsConfig(admin, operations.Config with
        {
            Season = operations.Config.Season with { EndsAt = due },
            CardRestrictions = [],
        }, operations.Version, "测试隔离：确定当前赛季截止与无禁限基线", Context("prepare"));
        return (store, admin, catalog);
    }

    private static L12PlatformStore Open(string root, L12Catalog catalog)
        => new(Path.Combine(root, "platform.json"), catalog.PresetDecks, officialCards: catalog.Cards);

    private static L12AccountView RegisterRanked(L12PlatformStore store, string name, string faction)
    {
        var account = store.Register(name, "Password123!").Account!;
        store.SelectRankedFaction(account.Id, faction);
        return account;
    }

    private static void AssertJson<T>(T expected, T actual)
        => Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));

    private static L12RankedSeasonCutoverReadiness Ready(string seasonId)
        => new(seasonId, 0, 0, 0, 0);

    private static L12AdminAuditContext Context(string id)
        => new(id, RequestMethod: "TEST", RequestPath: "/test");

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-cutover-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(root, true);
    }
}
