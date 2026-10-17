using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DisasterSetupAnalyticsQueryTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public async Task MirrorMatchHasTwoParticipantSamplesOneDistinctMatchAndExactOpportunityDenominators(int first)
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        await f.Prepare(first);
        await f.End();
        var report = await f.Recorder.ReadMasterAnalyticsAsync(new(MasterId: f.Catalog.DeckAt(0).MasterId));
        var disasters = Assert.IsType<L12DisasterAnalyticsReport>(report.Disasters);
        Assert.Equal(new L12DisasterAnalyticsCoverage(2, 1, 2, 1, 0, 0, 0), disasters.Coverage);
        Assert.Equal(3 * f.Game.DisasterCaptureSeed!.Pool.Length + 2, disasters.Items.Sum(row => row.Opportunities));
        Assert.Equal(5, disasters.Items.Sum(row => row.Selections));
        Assert.All(disasters.Items, row =>
        {
            Assert.Equal(1, row.DistinctMatches);
            Assert.Equal((double)row.Selections / row.Opportunities, row.SelectionRate!.Value, 5);
            Assert.Null(row.SelectedWinRate);
            Assert.Equal(row.Step is 1 or 5 ? "second" : "first", row.Initiative);
        });
        var matchup = Assert.Single(report.Matchups);
        Assert.Equal(1L, matchup.FirstSamples);
        Assert.Equal(1L, matchup.SecondSamples);
        Assert.Equal(2, Assert.Single(report.Items).ParticipantSamples);
        var serialized = JsonSerializer.Serialize(disasters);
        Assert.DoesNotContain(f.Game.State.MatchId, serialized);
        Assert.DoesNotContain("synthetic-account", serialized);
        Assert.DoesNotContain("Candidates", serialized);
    }

    [Theory]
    [InlineData("none", false, 0, 0, 2)]
    [InlineData("random", false, 0, 0, 2)]
    [InlineData("all", true, 0, 2, 0)]
    public async Task UnknownAndNonDraftFactsDoNotBecomeZeroPreference(string mode, bool skip,
        long usable, long unknown, long notApplicable)
    {
        await using var f = await DisasterFactFixture.Create("ranked", mode, skip);
        if (!skip) await f.Prepare();
        await f.End();
        var report = await f.Recorder.ReadMasterAnalyticsAsync(new(MasterId: f.Catalog.DeckAt(0).MasterId));
        Assert.Empty(report.Disasters!.Items);
        Assert.Equal(usable, report.Disasters.Coverage.UsableSamples);
        Assert.Equal(unknown, report.Disasters.Coverage.UnknownSamples);
        Assert.Equal(notApplicable, report.Disasters.Coverage.NotApplicableSamples);
        Assert.Equal(2, Assert.Single(report.Items).ParticipantSamples);
    }

    [Fact]
    public async Task InterruptedDraftKeepsOriginalMasterDenominatorButNoCompletedChoiceRows()
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        await f.Step();
        await f.End();
        var report = await f.Recorder.ReadMasterAnalyticsAsync(new(MasterId: f.Catalog.DeckAt(0).MasterId));
        Assert.Equal(2, report.Disasters!.Coverage.IncompleteSamples);
        Assert.Empty(report.Disasters.Items);
        Assert.Equal(2, Assert.Single(report.Items).ParticipantSamples);
    }

    [Fact]
    public async Task OriginAndPoolFiltersAreInCacheKeysAndDoNotChangeOriginalMasterOrCardDenominators()
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        await f.Step();
        var prompt = f.Game.State.PendingPrompts[0];
        Assert.True(f.Game.TryCreateRankedSetupTimeoutCommand(prompt.PlayerIndex, out var command));
        await f.Record(prompt.PlayerIndex, command, serverTimeout: true);
        await f.Prepare();
        await f.End();
        var query = new L12CardAnalyticsQuery(MasterId: f.Catalog.DeckAt(0).MasterId);
        var all = await f.Recorder.ReadMasterAnalyticsAsync(query);
        var timeout = await f.Recorder.ReadMasterAnalyticsAsync(query with { DisasterOrigin = "server-setup-timeout" });
        Assert.All(timeout.Disasters!.Items, row => Assert.Equal("server-setup-timeout", row.Origin));
        Assert.Equal(1, timeout.Disasters.Items.Sum(row => row.Selections));
        Assert.Equal(all.Disasters!.Coverage, timeout.Disasters.Coverage);
        Assert.Equal(all.Items, timeout.Items);
        Assert.Equal(all.Cards!.Summary.SampleSize, timeout.Cards!.Summary.SampleSize);
        var pool = Assert.Single(all.Disasters.Pools);
        var samePool = await f.Recorder.ReadMasterAnalyticsAsync(query with { DisasterPoolId = pool.Id });
        Assert.Equal(all.Disasters.Items, samePool.Disasters!.Items);
        var missingPool = await f.Recorder.ReadMasterAnalyticsAsync(query with { DisasterPoolId = new string('F', 64) });
        Assert.Empty(missingPool.Disasters!.Items);
        Assert.Equal(all.Items, missingPool.Items);
        var first = await f.Recorder.ReadMasterAnalyticsAsync(query with { Initiative = "first" });
        Assert.Equal(1, first.Disasters!.Coverage.ParticipantSamples);
        Assert.Equal(3, first.Disasters.Items.Sum(row => row.Selections));
        Assert.All(first.Disasters.Items, row => Assert.Equal("first", row.Initiative));
    }

    [Fact]
    public async Task ExcludedMatchesAndAccountsAndCurrentVersionAreSharedAcrossBothReports()
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        await f.Prepare();
        await f.End();
        foreach (var query in new[]
        {
            new L12CardAnalyticsQuery(ExcludedMatchIds: [f.Game.State.MatchId]),
            new L12CardAnalyticsQuery(ExcludedAccountIds: ["synthetic-account-a"]),
            new L12CardAnalyticsQuery(EffectVersion: "synthetic-other-version"),
            new L12CardAnalyticsQuery(FromUtc: f.Now.AddDays(1)),
            new L12CardAnalyticsQuery(SeasonId: "synthetic-other-season"),
            new L12CardAnalyticsQuery(OpponentMasterId: "synthetic-missing-master"),
        })
        {
            var report = await f.Recorder.ReadMasterAnalyticsAsync(query);
            Assert.Empty(report.Items);
            Assert.Equal(0, report.Disasters!.Coverage.ParticipantSamples);
            Assert.Empty(report.Disasters.Items);
        }
    }

    [Fact]
    public async Task CancellationAndInvalidFilterDoNotLeaveJobsOrTransactions()
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Recorder.ReadMasterAnalyticsAsync(new(), cancelled.Token));
        foreach (var query in new[] { new L12CardAnalyticsQuery(DisasterMode: "bad"),
            new L12CardAnalyticsQuery(DisasterOrigin: "client-decides"), new L12CardAnalyticsQuery(DisasterPoolId: "bad") })
            await Assert.ThrowsAsync<ArgumentException>(() => f.Recorder.ReadMasterAnalyticsAsync(query));
        Assert.Equal(0, f.Recorder.CardAnalyticsExecutionCounts.Active);
        await f.Prepare();
        await f.End();
        Assert.Equal(2, (await f.Recorder.ReadMasterAnalyticsAsync(new())).Disasters!.Coverage.UsableSamples);
    }

    [Fact]
    public async Task MissingCandidateFailsClosedInsteadOfPublishingFalseRates()
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        await f.Prepare();
        await f.End();
        await f.Execute("DELETE FROM match_disaster_candidates WHERE step=0 AND card_id=(SELECT MIN(card_id) FROM match_disaster_candidates WHERE step=0);");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Recorder.ReadMasterAnalyticsAsync(new()));
        Assert.Equal(0, f.Recorder.CardAnalyticsExecutionCounts.Active);
    }
}
