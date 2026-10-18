using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DisasterStrategyQueryTests
{
    [Fact]
    public void DedicatedStrategyReaderExistsRatherThanRelabellingMarginalMasterRows()
        => Assert.NotNull(typeof(MatchRecorder).GetMethod("ReadDisasterStrategyAsync",
            BindingFlags.Public | BindingFlags.Instance, [typeof(L12CardAnalyticsQuery), typeof(CancellationToken)]));

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public async Task MirrorAndSeatSwapPreservePerspectiveAndActualThreeThenIndependentTwoCandidates(int first)
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        await f.Prepare(first); await f.End();
        var facts = await f.Read();
        var master = f.Catalog.DeckAt(0).MasterId;
        foreach (var side in new[] { "first", "second" })
        {
            var report = await f.Recorder.ReadDisasterStrategyAsync(new(MasterId: master, OpponentMasterId: master, Initiative: side));
            Assert.Equal(new L12DisasterAnalyticsCoverage(1, 1, 1, 1, 0, 0, 0), report.Coverage);
            Assert.Equal(5, report.Items.Sum(x => x.Selections));
            Assert.Equal(side == "first" ? 3 : 2, report.Items.Where(x => x.Party == "self").Sum(x => x.Selections));
            Assert.Equal(side == "first" ? 2 : 3, report.Items.Where(x => x.Party == "opponent").Sum(x => x.Selections));
            Assert.All(report.Items, x => Assert.Equal((double)x.Selections / x.Opportunities, x.SelectionRate!.Value, 6));
            Assert.Equal(5, report.Conditions.Count);
            foreach (var condition in report.Conditions)
            {
                var actual = Assert.Single(facts.Decisions, d => d.Step == condition.Step);
                Assert.Equal(actual.Candidates, condition.CandidateCardIds);
                Assert.Equal(actual.ChosenCardId, Assert.Single(condition.Choices).CardId);
                Assert.All(condition.PriorPublicBans, b => Assert.True(b.Step < condition.Step && b.Step < 3));
                Assert.Equal(condition.Step < 3 ? null : facts.PublicCardId, condition.KnownPublicCardId);
                Assert.Equal(condition.Step < 3 ? condition.Step : 3, condition.PriorPublicBans.Count);
            }
            var firstPick = Assert.Single(report.Conditions, c => c.Step == 4);
            var secondPick = Assert.Single(report.Conditions, c => c.Step == 5);
            Assert.Equal(3, firstPick.CandidateCardIds.Count); Assert.Equal(2, secondPick.CandidateCardIds.Count);
            Assert.Empty(firstPick.CandidateCardIds.Intersect(secondPick.CandidateCardIds));
            Assert.DoesNotContain("matchId", JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
    }

    [Fact]
    public async Task OnlyTheActualABMatchContributesOpponentBehaviorAndNonBReferenceIsSeparate()
    {
        await using var f = new DisasterFactFixture("ranked");
        await f.Recorder.InitializeAsync();
        await Match(f, "ab", 0, 1, 0);
        await Match(f, "ba", 1, 0, 1);
        await Match(f, "bc", 1, 2, 0);
        await Match(f, "ac", 0, 2, 0);
        var a = f.Catalog.DeckAt(0).MasterId; var b = f.Catalog.DeckAt(1).MasterId;
        Assert.NotEqual(a, b); Assert.NotEqual(b, f.Catalog.DeckAt(2).MasterId);
        var report = await f.Recorder.ReadDisasterStrategyAsync(new(MasterId: a, OpponentMasterId: b, Initiative: "first"));
        Assert.Equal(2, report.Coverage.DistinctMatches);
        Assert.Equal(10, report.Items.Sum(x => x.Selections));
        Assert.Equal(4, report.Items.Where(x => x.Party == "opponent").Sum(x => x.Selections));
        Assert.All(report.Items.Where(x => x.Step == 0), x =>
        {
            Assert.Equal(2, x.Opportunities);
            Assert.Equal(1, x.Comparison.ReferenceOpportunities);
            Assert.Null(x.Comparison.Difference);
        });
        var excluded = await f.Recorder.ReadDisasterStrategyAsync(new(MasterId: a, OpponentMasterId: b,
            Initiative: "first", ExcludedMatchIds: ["ab", "ba"]));
        Assert.Empty(excluded.Items); Assert.Equal(0, excluded.Coverage.ParticipantSamples);
        var second = await f.Recorder.ReadDisasterStrategyAsync(new(MasterId: a, OpponentMasterId: b, Initiative: "second"));
        Assert.Empty(second.Items);
    }

    [Fact]
    public async Task CommonStrataComparisonUsesOnlyOtherOpponentsAndSmallOrAbsentReferencesStayUnknown()
    {
        await using var f = new DisasterFactFixture("ranked"); await f.Recorder.InitializeAsync();
        await Match(f, "ab", 0, 1, 0); await Duplicate(f, "ab", "target", 29);
        await Match(f, "ac", 0, 2, 0); await Duplicate(f, "ac", "reference", 29);
        var query = new L12CardAnalyticsQuery(MasterId: f.Catalog.DeckAt(0).MasterId,
            OpponentMasterId: f.Catalog.DeckAt(1).MasterId, Initiative: "first");
        var report = await f.Recorder.ReadDisasterStrategyAsync(query);
        Assert.Equal(30, report.Coverage.DistinctMatches);
        Assert.All(report.Items.Where(x => x.Step == 0), x =>
        {
            Assert.Equal(30, x.Comparison.ReferenceOpportunities);
            Assert.Equal(30, x.Comparison.ComparableOpportunities);
            Assert.Equal(0, x.Comparison.Difference);
        });
        var noReference = await f.Recorder.ReadDisasterStrategyAsync(query with
        { ExcludedMatchIds = Enumerable.Range(0, 29).Select(i => "reference" + i).Append("ac").ToArray() });
        Assert.All(noReference.Items, x => { Assert.Equal("unknown", x.Comparison.Status); Assert.Null(x.Comparison.Difference); });
        var cached = await f.Recorder.ReadDisasterStrategyAsync(query);
        Assert.Same(report, cached);
    }

    [Theory]
    [InlineData("same-count-candidate")]
    [InlineData("future-public")]
    [InlineData("cross-pick")]
    [InlineData("sequence")]
    public async Task EqualRowCountsCannotProveATrueCandidateChain(string corruption)
    {
        await using var f = await DisasterFactFixture.Create("ranked"); await f.Prepare(); await f.End();
        await f.Execute(corruption switch
        {
            "same-count-candidate" => "UPDATE match_disaster_candidates SET card_id=(SELECT chosen_card_id FROM match_disaster_decisions WHERE step=0) WHERE step=1 AND card_id=(SELECT MIN(card_id) FROM match_disaster_candidates WHERE step=1 AND card_id<>(SELECT chosen_card_id FROM match_disaster_decisions WHERE step=1));",
            "future-public" => "UPDATE match_disaster_captures SET public_sequence=(SELECT offered_sequence+100 FROM match_disaster_decisions WHERE step=4);",
            "cross-pick" => "UPDATE match_disaster_candidates SET card_id=(SELECT MIN(card_id) FROM match_disaster_candidates WHERE step=4) WHERE step=5 AND card_id=(SELECT MIN(card_id) FROM match_disaster_candidates WHERE step=5 AND card_id<>(SELECT chosen_card_id FROM match_disaster_decisions WHERE step=5));",
            _ => "UPDATE match_disaster_decisions SET resolved_sequence=(SELECT offered_sequence+1 FROM match_disaster_decisions WHERE step=1) WHERE step=0;",
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Recorder.ReadDisasterStrategyAsync(new(
            MasterId: f.Catalog.DeckAt(0).MasterId, OpponentMasterId: f.Catalog.DeckAt(0).MasterId, Initiative: "first")));
    }

    [Theory]
    [InlineData("none", false, 0, 1)] [InlineData("random", false, 0, 1)] [InlineData("all", true, 1, 0)]
    public async Task MissingAndNonDraftCoverageNeverBecomesARecommendation(string mode, bool skip, long unknown, long notApplicable)
    {
        await using var f = await DisasterFactFixture.Create("ranked", mode, skip);
        if (!skip) await f.Prepare(); await f.End();
        var report = await f.Recorder.ReadDisasterStrategyAsync(new(MasterId: f.Catalog.DeckAt(0).MasterId,
            OpponentMasterId: f.Catalog.DeckAt(0).MasterId, Initiative: "first"));
        Assert.Empty(report.Items); Assert.Empty(report.Conditions);
        Assert.Equal(unknown, report.Coverage.UnknownSamples); Assert.Equal(notApplicable, report.Coverage.NotApplicableSamples);
    }

    [Fact]
    public async Task MissingOpponentAndCancellationDoNotStartUnboundedJobs()
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        var noB = await f.Recorder.ReadDisasterStrategyAsync(new(MasterId: f.Catalog.DeckAt(0).MasterId, Initiative: "first"));
        Assert.Equal("choose-opponent", noB.Status); Assert.Empty(noB.Items);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Recorder.ReadDisasterStrategyAsync(new(
            MasterId: "a", OpponentMasterId: "b", Initiative: "first"), cancel.Token));
        Assert.Equal(0, f.Recorder.CardAnalyticsExecutionCounts.Active);
    }

    [Fact]
    public async Task SecondPlayersInformationSetDoesNotConditionOnOpponentsHiddenFirstPick()
    {
        await using var f = new DisasterFactFixture("ranked"); await f.Recorder.InitializeAsync();
        await Match(f, "hidden-a", 0, 1, 1, firstPickChoice: 0);
        var pickA = (await f.Read()).Decisions.Single(d => d.Step == 4).ChosenCardId;
        await Match(f, "hidden-b", 0, 1, 1, firstPickChoice: 1);
        Assert.NotEqual(pickA, (await f.Read()).Decisions.Single(d => d.Step == 4).ChosenCardId);
        var report = await f.Recorder.ReadDisasterStrategyAsync(new(MasterId: f.Catalog.DeckAt(0).MasterId,
            OpponentMasterId: f.Catalog.DeckAt(1).MasterId, Initiative: "second"));
        var condition = Assert.Single(report.Conditions, c => c.Step == 5);
        Assert.Equal("self", condition.Party); Assert.Equal(2, condition.Samples);
        Assert.All(condition.PriorPublicBans, b => Assert.True(b.Step < 3));
        Assert.Equal(2, condition.CandidateCardIds.Count);
        // Different hidden opposing choices merge into the same legitimately known decision context.
        Assert.Equal(2, Assert.Single(condition.Choices).Selections);
    }

    [Fact]
    public async Task DifferentNativePoolModesDoNotCreateAnUnmatchedReferenceAndOriginsRemainSeparate()
    {
        await using var f = new DisasterFactFixture("ranked"); await f.Recorder.InitializeAsync();
        await Match(f, "all-target", 0, 1, 0);
        await Match(f, "season-reference", 0, 2, 0, disaster: "season");
        var report = await f.Recorder.ReadDisasterStrategyAsync(new(MasterId: f.Catalog.DeckAt(0).MasterId,
            OpponentMasterId: f.Catalog.DeckAt(1).MasterId, Initiative: "first"));
        Assert.All(report.Items, x => { Assert.Equal("unknown", x.Comparison.Status); Assert.Null(x.Comparison.Difference); });
        var timeout = await f.Recorder.ReadDisasterStrategyAsync(new(MasterId: f.Catalog.DeckAt(0).MasterId,
            OpponentMasterId: f.Catalog.DeckAt(1).MasterId, Initiative: "first", DisasterOrigin: "server-setup-timeout"));
        Assert.Empty(timeout.Items); Assert.Equal(report.Coverage, timeout.Coverage);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("reference")]
    public async Task TargetAndReferencePoolDigestsMustMatchTheirOwnNativeSeed(string corruptedCohort)
    {
        await using var f = new DisasterFactFixture("ranked"); await f.Recorder.InitializeAsync();
        await Match(f, "valid-target", 0, 1, 0);
        await Match(f, "valid-reference", 0, 2, 0, disaster: "season");
        var selected = corruptedCohort == "target" ? "valid-target" : "valid-reference";
        var other = corruptedCohort == "target" ? "valid-reference" : "valid-target";
        // Equal-looking cohort identities must not turn a different native pool into support.
        Assert.Equal(2, await f.Scalar("SELECT COUNT(DISTINCT seed_hash) FROM match_disaster_captures;"));
        await f.Execute($"UPDATE match_disaster_captures SET seed_hash=(SELECT seed_hash FROM match_disaster_captures WHERE match_id='{other}') WHERE match_id='{selected}';");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Recorder.ReadDisasterStrategyAsync(new(
            MasterId: f.Catalog.DeckAt(0).MasterId, OpponentMasterId: f.Catalog.DeckAt(1).MasterId,
            Initiative: "first")));
    }

    private static async Task Match(DisasterFactFixture f, string id, int deckA, int deckB, int first,
        int firstPickChoice = 0, string disaster = "all")
    {
        var decks = new[] { f.Catalog.DeckAt(deckA), f.Catalog.DeckAt(deckB) };
        f.Game = new(f.Catalog, id, "STRATEGY", 4071, ["synthetic-a", "synthetic-b"], decks, disasterMode: disaster, stateFormatVersion: 2, utcNow: () => f.Now);
        f.Sequence = 0;
        await f.Recorder.StartAsync(f.Game, "ranked", "synthetic-account-a", "synthetic-account-b", decks);
        var guard = 0;
        while (f.Game.State.Phase is L12Phase.Initiative or L12Phase.DisasterPreparation)
        {
            Assert.True(guard++ < 12);
            var prompt = f.Game.State.PendingPrompts[0];
            if (prompt.Kind == "disaster-pick" && f.Game.State.DisasterPreparationStep == 4)
                await f.Record(prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [prompt.ValidChoices[firstPickChoice]]));
            else await f.Step(first);
        }
        await f.End();
    }

    // Repeated synthetic persisted facts isolate rate arithmetic; not a claim of independent
    // engine executions or population-level statistical confidence.
    private static async Task Duplicate(DisasterFactFixture f, string source, string prefix, int count)
    {
        await using var connection = new SqliteConnection($"Data Source={f.Database};Pooling=False"); await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        foreach (var table in new[] { "matches", "match_participants", "match_deck_cards", "match_disaster_captures", "match_disaster_decisions", "match_disaster_candidates" })
        {
            var columns = new List<string>();
            using (var schema = connection.CreateCommand())
            {
                schema.Transaction = transaction; schema.CommandText = $"PRAGMA table_info({table});";
                using var reader = schema.ExecuteReader(); while (reader.Read()) columns.Add(reader.GetString(1));
            }
            for (var i = 0; i < count; i++)
            {
                using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = $"INSERT INTO {table}({string.Join(',', columns)}) SELECT {string.Join(',', columns.Select(c => c == "match_id" ? "$id" : c))} FROM {table} WHERE match_id=$source;";
                command.Parameters.AddWithValue("$id", prefix + i); command.Parameters.AddWithValue("$source", source); await command.ExecuteNonQueryAsync();
            }
        }
        transaction.Commit();
    }
}
