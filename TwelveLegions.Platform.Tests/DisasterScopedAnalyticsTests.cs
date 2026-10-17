using System.Globalization;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DisasterScopedAnalyticsTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public async Task RealIncludedCardAndPopularDeckSignatureSelectOnlyTheirActualParticipant(int first)
    {
        await using var f = new DisasterFactFixture("ranked");
        var decks = new[] { f.Catalog.DeckAt(0), f.Catalog.DeckAt(1) };
        f.Game = new L12GameEngine(f.Catalog, "synthetic-scoped", "SCOPED", 4071,
            ["synthetic-a", "synthetic-b"], decks, stateFormatVersion: 2, utcNow: () => f.Now);
        await f.Recorder.InitializeAsync();
        await f.Recorder.StartAsync(f.Game, "ranked", "synthetic-account-a", "synthetic-account-b", decks);
        await f.Prepare(first);
        await f.End();
        var card = decks[0].CardIds.Except(decks[1].CardIds, StringComparer.Ordinal).First();
        var masterBefore = await f.Recorder.ReadMasterAnalyticsAsync(new(MasterId: decks[0].MasterId));
        var signature = Assert.Single(masterBefore.PopularDecks, deck => deck.Signature == Signature(decks[0])).Signature;
        var facts = await f.Read();
        var ownDecisions = facts.Decisions.Where(decision => decision.ActorIndex == 0).ToArray();
        var expectedOpportunities = ownDecisions.Sum(decision => decision.Candidates.Length);
        var byCard = await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), card, null);
        var byDeck = await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), null, signature);
        foreach (var report in new[] { byCard, byDeck })
        {
            Assert.Equal(new L12DisasterAnalyticsCoverage(1, 1, 1, 1, 0, 0, 0), report.Coverage);
            Assert.Equal(expectedOpportunities, report.Items.Sum(row => row.Opportunities));
            Assert.Equal(ownDecisions.Length, report.Items.Sum(row => row.Selections));
            Assert.All(report.Items, row => Assert.Equal(first == 0 ? "first" : "second", row.Initiative));
            Assert.Null(report.SelectedMasterId);
            Assert.DoesNotContain(f.Game.State.MatchId, JsonSerializer.Serialize(report));
        }
        Assert.Equal(byCard.Items, byDeck.Items);
        var masterAfter = await f.Recorder.ReadMasterAnalyticsAsync(new(MasterId: decks[0].MasterId));
        Assert.Equal(masterBefore.Items, masterAfter.Items);
        Assert.Equal(masterBefore.Cards!.Summary, masterAfter.Cards!.Summary);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public async Task MirrorCardAndDeckKeepTwoSeatsButOneDistinctMatchAndUnselectedLegacyMasterHasNoItems(int first)
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        await f.Prepare(first);
        await f.End();
        var deck = f.Catalog.DeckAt(0);
        foreach (var report in new[]
        {
            await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), deck.CardIds[0], null),
            await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), null, Signature(deck)),
        })
        {
            Assert.Equal(new L12DisasterAnalyticsCoverage(2, 1, 2, 1, 0, 0, 0), report.Coverage);
            Assert.Equal(5, report.Items.Sum(row => row.Selections));
            Assert.Equal(3, report.Items.Where(row => row.Initiative == "first").Sum(row => row.Selections));
            Assert.Equal(2, report.Items.Where(row => row.Initiative == "second").Sum(row => row.Selections));
        }
        var legacy = await f.Recorder.ReadMasterAnalyticsAsync(new());
        Assert.Equal(2, legacy.Disasters!.Coverage.UsableSamples);
        Assert.Empty(legacy.Disasters.Items);
    }

    [Theory]
    [InlineData("none", false, false, 0, 0, 2)]
    [InlineData("random", false, false, 0, 0, 2)]
    [InlineData("all", true, false, 2, 0, 0)]
    [InlineData("all", false, true, 0, 2, 0)]
    public async Task ScopedCoveragePreservesUnknownIncompleteAndNonApplicableWithoutFalseZeroPreference(
        string mode, bool skip, bool interrupt, long unknown, long incomplete, long notApplicable)
    {
        await using var f = await DisasterFactFixture.Create("ranked", mode, skip);
        if (interrupt) await f.Step();
        else if (!skip) await f.Prepare();
        await f.End();
        var report = await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), f.Catalog.DeckAt(0).CardIds[0], null);
        Assert.Empty(report.Items);
        Assert.Equal(new L12DisasterAnalyticsCoverage(2, 1, 0, 0, unknown, incomplete, notApplicable), report.Coverage);
    }

    [Fact]
    public async Task OriginPoolAndInitiativeUseSharedScopedFactsAndDistinctCacheEntries()
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        await f.Step();
        var prompt = f.Game.State.PendingPrompts[0];
        Assert.True(f.Game.TryCreateRankedSetupTimeoutCommand(prompt.PlayerIndex, out var command));
        await f.Record(prompt.PlayerIndex, command, serverTimeout: true);
        await f.Prepare();
        await f.End();
        var card = f.Catalog.DeckAt(0).CardIds[0];
        var all = await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), card, null);
        var timeout = await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(DisasterOrigin: "server-setup-timeout"), card, null);
        Assert.Equal(all.Coverage, timeout.Coverage);
        Assert.Equal(1, timeout.Items.Sum(row => row.Selections));
        Assert.All(timeout.Items, row => Assert.Equal("server-setup-timeout", row.Origin));
        var player = await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(DisasterOrigin: "player"), card, null);
        Assert.Equal(4, player.Items.Sum(row => row.Selections));
        var pool = Assert.Single(all.Pools);
        Assert.Equal(all.Items, (await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(DisasterPoolId: pool.Id), card, null)).Items);
        var absentPool = await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(DisasterPoolId: new string('F', 64)), card, null);
        Assert.Empty(absentPool.Items);
        Assert.Equal(all.Coverage, absentPool.Coverage);
        var absentMode = await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(DisasterMode: "season"), card, null);
        Assert.Empty(absentMode.Items);
        var first = await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(Initiative: "first"), card, null);
        Assert.Equal(1, first.Coverage.ParticipantSamples);
        Assert.Equal(3, first.Items.Sum(row => row.Selections));
        Assert.All(first.Items, row => Assert.Equal("first", row.Initiative));
        var cached = new CardAnalyticsRequestTiming();
        Assert.Same(all, await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), card, null, default, cached));
        Assert.True(cached.CacheHit);
    }

    [Fact]
    public async Task EveryExistingEligibilityFilterAndExclusionIsAppliedBeforeCardAndDeckSubset()
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        await f.Prepare();
        await f.End();
        var deck = f.Catalog.DeckAt(0);
        foreach (var query in new[]
        {
            new L12CardAnalyticsQuery(ExcludedMatchIds: [f.Game.State.MatchId]),
            new L12CardAnalyticsQuery(ExcludedAccountIds: ["synthetic-account-a"]),
            new L12CardAnalyticsQuery(EffectVersion: "synthetic-other-version"),
            new L12CardAnalyticsQuery(RulesVersion: "synthetic-other-rules"),
            new L12CardAnalyticsQuery(FromUtc: f.Now.AddDays(1)),
            new L12CardAnalyticsQuery(ToUtc: f.Now.AddDays(-1)),
            new L12CardAnalyticsQuery(SeasonId: "synthetic-other-season"),
            new L12CardAnalyticsQuery(MasterId: "synthetic-other-master"),
            new L12CardAnalyticsQuery(OpponentMasterId: "synthetic-other-master"),
        })
        foreach (var report in new[]
        {
            await f.Recorder.ReadScopedDisasterAnalyticsAsync(query, deck.CardIds[0], null),
            await f.Recorder.ReadScopedDisasterAnalyticsAsync(query, null, Signature(deck)),
        })
        {
            Assert.Equal(0, report.Coverage.ParticipantSamples);
            Assert.Empty(report.Items);
        }
        var absentCard = await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), "synthetic-absent", null);
        Assert.Equal(0, absentCard.Coverage.ParticipantSamples);
        Assert.Equal(2, (await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), deck.CardIds[0], null)).Coverage.ParticipantSamples);
    }

    [Theory]
    [InlineData("casual")] [InlineData("friendly")] [InlineData("tournament")]
    public async Task NonRankedFactsNeverLeakIntoExistingRankedScope(string mode)
    {
        await using var f = await DisasterFactFixture.Create(mode);
        await f.Prepare();
        await f.End();
        var report = await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(ModeId: mode), f.Catalog.DeckAt(0).CardIds[0], null);
        Assert.Equal(0, report.Coverage.ParticipantSamples);
        Assert.Empty(report.Items);
    }

    [Fact]
    public async Task CanonicalDeckValidationRejectsMalformedUnboundedDuplicateUnsortedAndSqlLikeTargets()
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        foreach (var (card, signature) in new (string?, string?)[]
        {
            (null, null), (" ", " "), ("S01-01L1", "main:S01-01L1x1"),
            (new string('A', 129), null), ("id|other", null), ("id';DROP", null),
            (null, new string('A', 16_385)), (null, "main:Ax0"), (null, "main:Ax01"),
            (null, "main:Ax1001"), (null, "main:Ax-1"), (null, "main:Ax1|"),
            (null, "main:Ax1|main:Ax2"), (null, "main:Bx1|main:Ax1"),
            (null, "morale:Ax1|main:Bx1"), (null, "bench:Ax1"),
            (null, "main:Ax1;SELECT 1"), (null, "main:A:Bx1"),
        })
            await Assert.ThrowsAsync<ArgumentException>(() => f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), card, signature));
        foreach (var query in new[] { new L12CardAnalyticsQuery(DisasterMode: "bad"),
            new L12CardAnalyticsQuery(DisasterOrigin: "client"), new L12CardAnalyticsQuery(DisasterPoolId: "bad"),
            new L12CardAnalyticsQuery(Initiative: "bad"), new L12CardAnalyticsQuery(FromUtc: f.Now, ToUtc: f.Now) })
            await Assert.ThrowsAsync<ArgumentException>(() => f.Recorder.ReadScopedDisasterAnalyticsAsync(query, "S01-01L1", null));
        Assert.Equal(0, f.Recorder.CardAnalyticsExecutionCounts.Active);
        Assert.Equal(0, f.Recorder.AnalyticsResultCacheCount);
    }

    [Fact]
    public async Task CacheTargetsCannotAliasAndEpochInvalidationRecomputesSubsetAndFailsClosedOnCorruptFacts()
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        await f.Prepare();
        await f.End();
        var deck = f.Catalog.DeckAt(0);
        var all = await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), deck.CardIds[0], null);
        var signature = Signature(deck);
        Assert.Equal(2, (await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), null, signature)).Coverage.UsableSamples);
        Assert.Equal(0, (await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), null, signature + "|special:synthetic-extra-cardx1")).Coverage.ParticipantSamples);
        await f.Execute("DELETE FROM match_disaster_candidates WHERE step=0 AND card_id=(SELECT MIN(card_id) FROM match_disaster_candidates WHERE step=0);");
        Assert.Same(all, await f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), deck.CardIds[0], null));
        f.Recorder.InvalidateAnalyticsCache();
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), deck.CardIds[0], null));
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), null, signature));
        Assert.Equal(0, f.Recorder.CardAnalyticsExecutionCounts.Active);
    }

    [Fact]
    public async Task ScopedRequestsJoinExistingBoundedWorkerAndOneCancellationDoesNotCancelRemainingWaiter()
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        await f.Prepare();
        await f.End();
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        Task<string?> Block(string key) => f.Recorder.ExecuteCardAnalyticsAsync<string>(key, f.Recorder.AnalyticsCacheEpoch,
            default, new CardAnalyticsRequestTiming(), execution =>
            {
                entered.Signal();
                release.Wait(execution.CancellationToken);
                return key;
            }, cacheResult: false);
        var blockerA = Block("scoped-test-block-a");
        var blockerB = Block("scoped-test-block-b");
        await WaitUntil(() => entered.IsSet);
        using var cancellation = new CancellationTokenSource();
        var ownerTiming = new CardAnalyticsRequestTiming();
        var joinedTiming = new CardAnalyticsRequestTiming();
        var card = f.Catalog.DeckAt(0).CardIds[0];
        try
        {
            var owner = f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), card, null, cancellation.Token, ownerTiming);
            var joined = f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), card, null, default, joinedTiming);
            Assert.Equal(3, f.Recorder.CardAnalyticsExecutionCounts.Active);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner);
            Assert.False(joined.IsCompleted);
            release.Set();
            Assert.Equal(2, (await joined).Coverage.UsableSamples);
            Assert.Contains("singleflight;desc=\"joined\"", joinedTiming.ToServerTiming());
            Assert.Equal(1, f.Recorder.AnalyticsResultCacheCount);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                f.Recorder.ReadScopedDisasterAnalyticsAsync(new(), card, null, cancellation.Token));
        }
        finally
        {
            release.Set();
            await Task.WhenAll(blockerA, blockerB);
        }
        await WaitUntil(() => f.Recorder.CardAnalyticsExecutionCounts.Active == 0);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private static string Signature(L12PresetDeckDefinition deck)
        => string.Join('|', new[] { (Section: "main", Cards: deck.CardIds), (Section: "morale", Cards: deck.MoraleIds),
                (Section: "special", Cards: deck.SpecialIds) }
            .SelectMany(section => section.Cards.GroupBy(card => card, StringComparer.Ordinal)
                .Select(cards => (section.Section, Card: cards.Key, Quantity: cards.Count())))
            .OrderBy(card => card.Section, StringComparer.Ordinal).ThenBy(card => card.Card, StringComparer.Ordinal)
            .Select(card => card.Section + ":" + card.Card + "x" + card.Quantity.ToString(CultureInfo.InvariantCulture)));
}
