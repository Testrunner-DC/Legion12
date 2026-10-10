using System.Collections;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RankedPlayerBadgeCommittedProjectionTests
{
    [Fact]
    public void MasterTitlesUseCommittedFactsAndExpireWithoutAnotherCommit()
    {
        using var fixture = new RankedIdentityFixture();
        var champion = fixture.CreateAccount("titleowner");
        var opponents = Enumerable.Range(0, 10)
            .Select(index => fixture.CreateAccount($"title{index:D2}" )).ToArray();
        var masterIds = SelectableMasters(fixture).Take(3).ToArray();
        Assert.Equal(3, masterIds.Length);
        var config = fixture.Store.RankedConfig(fixture.Admin);
        var firstTitle = config.MasterTitles.Single(row => row.MasterId.Equals(masterIds[0],
            StringComparison.OrdinalIgnoreCase)).Title;
        var secondTitle = config.MasterTitles.Single(row => row.MasterId.Equals(masterIds[1],
            StringComparison.OrdinalIgnoreCase)).Title;
        var now = new DateTimeOffset(2026, 10, 7, 4, 0, 0, TimeSpan.Zero);
        var cutoff = now - L12PlatformStore.RankedMasterTitleWindow;
        var firstFacts = TitleFacts("expiry-a", champion.Id, opponents, masterIds[0], masterIds[2],
            cutoff, oneAtExactCutoff: true);
        var secondFacts = TitleFacts("expiry-b", champion.Id, opponents, masterIds[1], masterIds[2],
            cutoff, oneAtExactCutoff: false);
        Assert.Equal(40, fixture.Store.ImportRankedMasterTitleFacts([.. firstFacts, .. secondFacts]));
        fixture.CommitProfile(champion.Id, "order", sevenValue: 100_000, placementPlayed: 5,
            selectedMasterTitle: firstTitle);
        var version = fixture.Store.Version;
        var cache = RankedIdentityTestState.RollbackCache(fixture.Store);
        var committed = RankedIdentityTestState.CommittedProjection(fixture.Store);

        var boundary = fixture.Store.RankedBattleIdentityAt(champion.Id, 0, now);
        var afterBoundary = fixture.Store.RankedBattleIdentityAt(champion.Id, 0, now.AddTicks(1));
        var allExpired = fixture.Store.RankedBattleIdentityAt(champion.Id, 0,
            now.AddDays(5).AddTicks(1));

        Assert.Equal(firstTitle, boundary.MasterTitle);
        Assert.Equal(secondTitle, afterBoundary.MasterTitle);
        Assert.Null(allExpired.MasterTitle);
        Assert.Contains(fixture.Store.RankedMasterChampionsAt(now), row =>
            row.MasterId.Equals(masterIds[0], StringComparison.OrdinalIgnoreCase)
            && row.Username == champion.Username);
        Assert.DoesNotContain(fixture.Store.RankedMasterChampionsAt(now.AddTicks(1)), row =>
            row.MasterId.Equals(masterIds[0], StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fixture.Store.RankedMasterChampionsAt(now.AddTicks(1)), row =>
            row.MasterId.Equals(masterIds[1], StringComparison.OrdinalIgnoreCase));
        Assert.Equal(version, fixture.Store.Version);
        Assert.Same(cache, RankedIdentityTestState.RollbackCache(fixture.Store));
        Assert.Same(committed, RankedIdentityTestState.CommittedProjection(fixture.Store));
    }

    [Fact]
    public async Task ExclusionOnlyChangesBadgeAfterTheExclusionCommits()
    {
        using var fixture = new RankedIdentityFixture();
        var champion = fixture.CreateAccount("titleexcl");
        var opponents = Enumerable.Range(0, 10)
            .Select(index => fixture.CreateAccount($"excl{index:D2}" )).ToArray();
        var masterIds = SelectableMasters(fixture).Take(2).ToArray();
        var now = new DateTimeOffset(2026, 10, 7, 4, 0, 0, TimeSpan.Zero);
        var facts = TitleFacts("exclude", champion.Id, opponents, masterIds[0], masterIds[1],
            now - TimeSpan.FromDays(20), oneAtExactCutoff: false);
        fixture.Store.ImportRankedMasterTitleFacts(facts);
        fixture.CommitProfile(champion.Id, "order", sevenValue: 100_000, placementPlayed: 5);
        Assert.NotNull(fixture.Store.RankedBattleIdentityAt(champion.Id, 0, now).MasterTitle);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("synthetic exclusion barrier");
        };
        var writer = Task.Factory.StartNew(() => fixture.Store.ExecuteAdminTransaction(() =>
        {
            AddTransitionWaiver(fixture.Store, facts[0].MatchId);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            Assert.NotNull(fixture.Store.RankedBattleIdentityAt(champion.Id, 0, now).MasterTitle);
        }
        finally
        {
            release.Set();
            await writer.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Store.StorageFailureInjector = null;
        }
        Assert.Null(fixture.Store.RankedBattleIdentityAt(champion.Id, 0, now).MasterTitle);
    }

    [Theory]
    [InlineData("held-only", true)]
    [InlineData("review-only", false)]
    [InlineData("review-held", true)]
    [InlineData("normal-held", false)]
    [InlineData("confirmed-revoked", false)]
    [InlineData("confirmed-then-normal", false)]
    [InlineData("normal-then-confirmed", true)]
    [InlineData("system-error", true)]
    [InlineData("transition-waived", true)]
    public void PureExclusionProjectionMatchesExistingLockedRule(string scenario, bool expected)
    {
        using var fixture = new RankedIdentityFixture();
        var matchId = $"projection-{scenario}";
        var fact = new L12RankedMasterTitleMatchFact(matchId, "ghost-first", "ghost-second",
            "S01-01M1", "S01-01M2", 0, 6, DateTimeOffset.UtcNow, "completed", true);

        var observed = EvaluateExclusionRules(fixture.Store, fact, scenario);

        Assert.Equal(expected, observed.Locked);
        Assert.Equal(observed.Locked, observed.Projected);
    }

    [Fact]
    public void ImportedGhostFactsAffectPopulationButGhostCannotBecomeChampion()
    {
        using var fixture = new RankedIdentityFixture();
        var real = fixture.CreateAccount("realmaster");
        var opponents = Enumerable.Range(0, 10)
            .Select(index => fixture.CreateAccount($"ghost{index:D2}" )).ToArray();
        var masterIds = SelectableMasters(fixture).Take(2).ToArray();
        var now = new DateTimeOffset(2026, 10, 7, 4, 0, 0, TimeSpan.Zero);
        var start = now - TimeSpan.FromDays(10);
        var ghostFacts = TitleFacts("ghost", "imported-account-without-row", opponents,
            masterIds[0], masterIds[1], start, oneAtExactCutoff: false, winner: 0,
            count: 179);
        var realFacts = TitleFacts("real", real.Id, opponents, masterIds[0], masterIds[1],
            start, oneAtExactCutoff: false, winner: 1);
        fixture.Store.ImportRankedMasterTitleFacts([.. ghostFacts, .. realFacts]);
        fixture.CommitProfile(real.Id, "fate", sevenValue: 100_000, placementPlayed: 5);

        var champion = Assert.Single(fixture.Store.RankedMasterChampionsAt(now), row =>
            row.MasterId.Equals(masterIds[0], StringComparison.OrdinalIgnoreCase));
        Assert.Equal(real.Username, champion.Username);
        Assert.NotNull(fixture.Store.RankedBattleIdentityAt(real.Id, 0, now).MasterTitle);

        var thresholdFact = TitleFacts("ghost", "imported-account-without-row", opponents,
            masterIds[0], masterIds[1], start, oneAtExactCutoff: false, winner: 0,
            count: 1, startIndex: 179);
        Assert.Equal(1, fixture.Store.ImportRankedMasterTitleFacts(thresholdFact));
        Assert.DoesNotContain(fixture.Store.RankedMasterChampionsAt(now), row =>
            row.MasterId.Equals(masterIds[0], StringComparison.OrdinalIgnoreCase));
        Assert.Null(fixture.Store.RankedBattleIdentityAt(real.Id, 0, now).MasterTitle);
    }

    [Fact]
    public async Task CustomFactionHighestTiersAndRoomConsumerPreserveBothPlayerIndices()
    {
        using var fixture = new RankedIdentityFixture();
        var original = fixture.Store.RankedConfig(fixture.Admin);
        var customFactions = original.Factions.Select((faction, factionIndex) => faction with
        {
            Name = $"custom-{faction.Id}",
            FirstTitle = $"first-{faction.Id}",
            TopFiveTitle = $"five-{faction.Id}",
            Tiers = faction.Tiers.Select((tier, tierIndex) => tier with
            {
                Name = $"tier-{factionIndex}-{tierIndex}",
            }).ToArray(),
        }).ToArray();
        fixture.Store.UpdateRankedConfig(fixture.Admin, original with { Factions = customFactions },
            "synthetic committed badge projection", new L12AdminAuditContext("ranked-badge-config",
                "admin.operations.write"));
        var order = fixture.CreateAccount("badgeorder");
        var chaos = fixture.CreateAccount("badgechaos");
        var fate = fixture.CreateAccount("badgefate");
        var lower = fixture.CreateAccount("badgelower");
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            RankedIdentityTestState.SetProfile(fixture.Store, order.Id, fixture.CurrentSeasonId,
                "order", 130_000, 5);
            RankedIdentityTestState.SetProfile(fixture.Store, chaos.Id, fixture.CurrentSeasonId,
                "chaos", 120_000, 5);
            RankedIdentityTestState.SetProfile(fixture.Store, fate.Id, fixture.CurrentSeasonId,
                "fate", 110_000, 5);
            RankedIdentityTestState.SetProfile(fixture.Store, lower.Id, fixture.CurrentSeasonId,
                "order", 60_000, 5);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });

        var orderIdentity = fixture.Store.RankedBattleIdentity(order.Id, 0);
        var chaosIdentity = fixture.Store.RankedBattleIdentity(chaos.Id, 1);
        var fateIdentity = fixture.Store.RankedBattleIdentity(fate.Id, 0);
        var lowerIdentity = fixture.Store.RankedBattleIdentity(lower.Id, 1);
        Assert.Equal("custom-order", orderIdentity.Faction);
        Assert.Equal("tier-0-4", orderIdentity.Tier);
        Assert.Equal("first-order", orderIdentity.PlacementTitle);
        Assert.Equal(1, orderIdentity.Rank);
        Assert.Equal("custom-chaos", chaosIdentity.Faction);
        Assert.Equal("tier-1-4", chaosIdentity.Tier);
        Assert.Equal("first-chaos", chaosIdentity.PlacementTitle);
        Assert.Equal(2, chaosIdentity.Rank);
        Assert.Equal("custom-fate", fateIdentity.Faction);
        Assert.Equal("tier-2-4", fateIdentity.Tier);
        Assert.Equal("first-fate", fateIdentity.PlacementTitle);
        Assert.Equal(3, fateIdentity.Rank);
        Assert.False(lowerIdentity.HighestTier);
        Assert.Equal("tier-0-3", lowerIdentity.Tier);
        Assert.Null(lowerIdentity.Rank);
        Assert.Null(lowerIdentity.PlacementTitle);

        var recorder = new MatchRecorder(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(
            fixture.Path)!, "badge-matches.db"));
        await recorder.InitializeAsync();
        try
        {
            var manager = new L12RoomManager(fixture.Catalog, recorder, fixture.Store);
            var firstSession = Guid.NewGuid();
            var secondSession = Guid.NewGuid();
            manager.Connect(firstSession, order.Id, order.Username);
            manager.Connect(secondSession, chaos.Id, chaos.Username);
            var created = JsonSerializer.SerializeToElement(Assert.Single(manager.CreateRoom(firstSession)).Payload);
            var roomCode = created.GetProperty("roomCode").GetString()!;
            manager.JoinRoom(secondSession, roomCode);
            var room = Room(manager, roomCode);
            var gate = (SemaphoreSlim)room.GetType().GetProperty("Gate")!.GetValue(room)!;
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            fixture.Store.StorageFailureInjector = stage =>
            {
                if (stage != "before-commit") return;
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("synthetic room/platform lock barrier");
            };
            var writer = Task.Factory.StartNew(() => fixture.CommitProfile(order.Id, "order",
                sevenValue: 0, placementPlayed: 0), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Task<IReadOnlyList<L12RankedBattleIdentityView>>? badgeRead = null;
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
                badgeRead = Task.Factory.StartNew(() =>
                {
                    Assert.True(gate.Wait(0));
                    try
                    {
                        return (IReadOnlyList<L12RankedBattleIdentityView>)typeof(L12RoomManager)
                            .GetMethod("RankedPlayerBadges", BindingFlags.Instance
                                | BindingFlags.NonPublic)!.Invoke(manager, new[] { room })!;
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                var badges = await badgeRead.WaitAsync(TimeSpan.FromSeconds(1));
                Assert.Equal(new[] { 0, 1 }, badges.OrderBy(row => row.PlayerIndex)
                    .Select(row => row.PlayerIndex).ToArray());
                Assert.Contains(badges, row => row.PlayerIndex == 0 && row.Faction == "custom-order");
                Assert.Contains(badges, row => row.PlayerIndex == 1 && row.Faction == "custom-chaos");
            }
            finally
            {
                release.Set();
                await writer.WaitAsync(TimeSpan.FromSeconds(5));
                fixture.Store.StorageFailureInjector = null;
                if (badgeRead is not null)
                    await badgeRead.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            await recorder.DisposeAsync();
        }
    }

    private static IReadOnlyList<string> SelectableMasters(RankedIdentityFixture fixture)
        => fixture.Catalog.Cards.Values.Where(card => card.CardType == "master"
                && card.Id != "S01-02M2")
            .OrderBy(card => card.Id, StringComparer.OrdinalIgnoreCase).Select(card => card.Id)
            .ToArray();

    private static L12RankedMasterTitleMatchFact[] TitleFacts(string prefix, string accountId,
        IReadOnlyList<L12AccountView> opponents, string masterId, string opponentMasterId,
        DateTimeOffset start, bool oneAtExactCutoff, int winner = 0, int count = 20,
        int startIndex = 0)
        => Enumerable.Range(startIndex, count).Select(index =>
        {
            var day = oneAtExactCutoff && index == startIndex ? 0 : 1 + index % 5;
            return new L12RankedMasterTitleMatchFact($"{prefix}-{index:D2}", accountId,
                opponents[index % opponents.Count].Id, masterId, opponentMasterId, winner, 6,
                start.AddDays(day).AddMinutes(index), "completed", true);
        }).ToArray();

    private static (bool Locked, bool Projected) EvaluateExclusionRules(L12PlatformStore store,
        L12RankedMasterTitleMatchFact fact, string scenario)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.NonPublic;
        lock (RankedIdentityTestState.Gate(store))
        {
            var data = typeof(L12PlatformStore).GetProperty("_data", instance)!.GetValue(store)!;
            switch (scenario)
            {
                case "held-only":
                    AddHold(data, fact.MatchId);
                    break;
                case "review-only":
                    AddDecision(data, fact.MatchId, "review", revision: 1, id: "review");
                    break;
                case "review-held":
                    AddHold(data, fact.MatchId);
                    AddDecision(data, fact.MatchId, "review", revision: 1, id: "review");
                    break;
                case "normal-held":
                    AddHold(data, fact.MatchId);
                    AddDecision(data, fact.MatchId, "normal", revision: 1, id: "normal");
                    break;
                case "confirmed-revoked":
                    AddDecision(data, fact.MatchId, "confirmed", revision: 1, id: "confirmed");
                    AddDecision(data, fact.MatchId, "revoked", revision: 2, id: "revocation",
                        revokesDecisionId: "confirmed");
                    break;
                case "confirmed-then-normal":
                    AddDecision(data, fact.MatchId, "confirmed", revision: 1, id: "confirmed");
                    AddDecision(data, fact.MatchId, "normal", revision: 2, id: "normal");
                    break;
                case "normal-then-confirmed":
                    AddDecision(data, fact.MatchId, "normal", revision: 1, id: "normal");
                    AddDecision(data, fact.MatchId, "confirmed", revision: 2, id: "confirmed");
                    break;
                case "system-error":
                    AddDecision(data, fact.MatchId, "system-error", revision: 1,
                        id: "system-error");
                    break;
                case "transition-waived":
                    AddTransitionWaiver(store, fact.MatchId);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
            }

            var locked = (bool)typeof(L12PlatformStore).GetMethod("IsRankedMatchExcludedLocked",
                instance)!.Invoke(store, new object[] { fact.MatchId })!;
            var projectedSet = (IEnumerable)typeof(L12PlatformStore)
                .GetMethod("ProjectRankedMasterTitleExclusions", staticFlags)!
                .Invoke(null, new object[] { data, new[] { fact } })!;
            var projected = projectedSet.Cast<object>().Any(item =>
                ((string)item).Equals(fact.MatchId, StringComparison.OrdinalIgnoreCase));
            return (locked, projected);
        }
    }

    private static void AddDecision(object data, string matchId, string disposition, long revision,
        string id, string? revokesDecisionId = null)
    {
        var decisions = (IList)data.GetType().GetProperty("RankedIntegrityDecisions")!
            .GetValue(data)!;
        var decision = Activator.CreateInstance(decisions.GetType().GetGenericArguments()[0],
            nonPublic: true)!;
        decision.GetType().GetProperty("Id")!.SetValue(decision, id);
        decision.GetType().GetProperty("RequestId")!.SetValue(decision, $"request-{id}");
        decision.GetType().GetProperty("RequestFingerprint")!.SetValue(decision,
            new string('a', 64));
        decision.GetType().GetProperty("Revision")!.SetValue(decision, revision);
        decision.GetType().GetProperty("Disposition")!.SetValue(decision, disposition);
        decision.GetType().GetProperty("RevokesDecisionId")!.SetValue(decision,
            revokesDecisionId);
        ((IList)decision.GetType().GetProperty("MatchIds")!.GetValue(decision)!).Add(matchId);
        decisions.Add(decision);
    }

    private static void AddHold(object data, string matchId)
    {
        var holds = (IList)data.GetType().GetProperty("RankedHeldRewards")!.GetValue(data)!;
        var hold = Activator.CreateInstance(holds.GetType().GetGenericArguments()[0],
            nonPublic: true)!;
        hold.GetType().GetProperty("MatchId")!.SetValue(hold, matchId);
        holds.Add(hold);
    }

    private static void AddTransitionWaiver(L12PlatformStore store, string matchId)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var data = typeof(L12PlatformStore).GetProperty("_data", flags)!.GetValue(store)!;
        var repairs = (IList)data.GetType().GetProperty("RankedSeasonResetRepairs")!.GetValue(data)!;
        var repair = Activator.CreateInstance(repairs.GetType().GetGenericArguments()[0], nonPublic: true)!;
        repair.GetType().GetProperty("SeasonId")!.SetValue(repair, "synthetic-season");
        repair.GetType().GetProperty("PreviousSeasonId")!.SetValue(repair, "synthetic-previous");
        repair.GetType().GetProperty("AppliedAt")!.SetValue(repair, DateTimeOffset.UtcNow);
        repair.GetType().GetProperty("AppliedBy")!.SetValue(repair, "synthetic-test");
        repair.GetType().GetProperty("Reason")!.SetValue(repair, "synthetic projection waiver");
        repair.GetType().GetProperty("EvidenceFingerprint")!.SetValue(repair,
            new string('b', 64));
        var matchIds = (IList)repair.GetType().GetProperty("TransitionMatchIds")!.GetValue(repair)!;
        matchIds.Add(matchId);
        repairs.Add(repair);
    }

    private static object Room(L12RoomManager manager, string roomCode)
    {
        var rooms = typeof(L12RoomManager).GetField("_rooms",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        var arguments = new object?[] { roomCode, null };
        Assert.True((bool)rooms.GetType().GetMethod("TryGetValue")!.Invoke(rooms, arguments)!);
        return arguments[1]!;
    }
}
