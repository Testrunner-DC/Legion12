using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CardAnalyticsFairnessTests
{
    [Fact]
    public async Task DashboardIsRankedOnlyAndComparisonCarriesOnlyExactFairStrata()
    {
        var directory = TestDirectory("fair-strata");
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        await SeedMatchAsync(connection, "include-a1", "ranked", 2, "effects-v2", "MASTER-A", "MASTER-B",
            0, 0, true, "repeat-a", "opponent-1");
        await SeedMatchAsync(connection, "include-a2", "ranked", 2, "effects-v2", "MASTER-A", "MASTER-B",
            0, 0, true, "repeat-a", "opponent-2");
        await SeedMatchAsync(connection, "control-a1", "ranked", 2, "effects-v2", "MASTER-A", "MASTER-B",
            0, 1, false, "control-1", "opponent-3");
        await SeedMatchAsync(connection, "control-a2", "ranked", 2, "effects-v2", "MASTER-A", "MASTER-B",
            0, 1, false, "control-2", "opponent-4");
        await SeedMatchAsync(connection, "include-no-control", "ranked", 2, "effects-v2", "MASTER-A", "MASTER-C",
            0, 0, true, "repeat-a", "opponent-5");
        await SeedMatchAsync(connection, "include-other-version", "ranked", 2, "effects-v3", "MASTER-A", "MASTER-B",
            0, 0, true, "repeat-a", "opponent-6");
        await SeedMatchAsync(connection, "friendly-excluded", "friendly", 2, "effects-v2", "MASTER-A", "MASTER-B",
            0, 0, true, "repeat-a", "opponent-7");
        await SeedMatchAsync(connection, "legacy-excluded", "ranked", 0, null, "MASTER-A", "MASTER-B",
            0, 0, true, "repeat-a", "opponent-8");

        var page = await recorder.ListCardAnalyticsAsync(new L12CardAnalyticsQuery(ModeId: "friendly",
            MinimumSampleSize: 1, CandidateCardIds: ["TARGET"]));
        var item = Assert.Single(page.Items, candidate => candidate.CardId == "TARGET");
        Assert.Equal(4, item.SampleSize);
        Assert.Equal(12, item.EligibleSampleSize);
        Assert.Null(item.Usage);
        Assert.DoesNotContain(item.Coverage.Metrics, metric => metric.Metric == "draw");

        var structure = Assert.IsType<L12AnalyticsSampleStructure>(item.SampleStructure);
        Assert.Equal(4, structure.DistinctMatches);
        Assert.Equal(1, structure.DistinctPlayers);
        Assert.Equal(4, structure.KnownPlayerSamples);
        Assert.Equal(4, structure.MaximumPlayerContribution);
        Assert.Equal(1, structure.MaximumPlayerContributionRate);
        Assert.Equal("player-clustered", structure.DependencyStatus);
        Assert.Equal("approximation", structure.Uncertainty.Status);

        var comparison = Assert.IsType<L12AnalyticsStratifiedComparison>(item.Comparison);
        Assert.Equal(2, comparison.CarriedSamples);
        Assert.Equal(2, comparison.ComparisonSamples);
        Assert.Equal(2, comparison.InsufficientStrata);
        Assert.Equal(2, comparison.ExcludedIncludedSamples);
        Assert.Equal(0, comparison.WinRate);
        Assert.Equal(1, comparison.Delta);
        Assert.Equal("unknown", comparison.Uncertainty!.Status);

        var currentVersion = await recorder.ListCardAnalyticsAsync(new L12CardAnalyticsQuery(
            MinimumSampleSize: 1, CandidateCardIds: ["TARGET"], EffectVersion: "effects-v2"));
        Assert.Equal(3, Assert.Single(currentVersion.Items,
            candidate => candidate.CardId == "TARGET").SampleSize);
        Assert.Equal(10, currentVersion.Summary.SampleSize);

        var detail = Assert.IsType<L12CardAnalyticsDetail>(await recorder.GetCardAnalyticsAsync("TARGET",
            new L12CardAnalyticsQuery(MinimumSampleSize: 1), includeRecentMatches: false));
        Assert.DoesNotContain(detail.Breakdowns, row => row.Dimension == "mode");
        Assert.Contains(detail.Breakdowns, row => row.Dimension == "effect-version"
            && row.Value == "effects-v2");
        Assert.All(detail.Summary.Usage!.Metrics, metric =>
        {
            Assert.Null(metric.EligibleSamples);
            Assert.Equal("event-observed-only", metric.CoverageStatus);
        });
    }

    [Fact]
    public async Task AnalyticsCacheIsBoundedAndEpochInvalidationRejectsStaleStatistics()
    {
        var directory = TestDirectory("bounded-cache");
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await SeedMatchAsync(connection, "cache-1", "ranked", 2, "effects-v2", "MASTER-A", "MASTER-B",
            0, 0, true, "cache-player", "cache-opponent");

        for (var index = 0; index < 40; index++)
            _ = await recorder.ListCardAnalyticsAsync(new L12CardAnalyticsQuery(MinimumSampleSize: 1,
                CandidateCardIds: ["TARGET"], Search: $"cache-{index}"));
        Assert.InRange(recorder.AnalyticsResultCacheCount, 1, 32);

        var before = await recorder.ListCardAnalyticsAsync(new L12CardAnalyticsQuery(
            MinimumSampleSize: 1, CandidateCardIds: ["TARGET"]));
        Assert.Equal(1, Assert.Single(before.Items,
            candidate => candidate.CardId == "TARGET").SampleSize);
        await SeedMatchAsync(connection, "cache-2", "ranked", 2, "effects-v2", "MASTER-A", "MASTER-B",
            0, 0, true, "cache-player-2", "cache-opponent-2");
        recorder.InvalidateAnalyticsCache();
        var after = await recorder.ListCardAnalyticsAsync(new L12CardAnalyticsQuery(
            MinimumSampleSize: 1, CandidateCardIds: ["TARGET"]));
        Assert.Equal(2, Assert.Single(after.Items,
            candidate => candidate.CardId == "TARGET").SampleSize);
    }

    [Fact]
    public async Task StorageMaintenanceRetriesSoonWhenBoundedBatchIsFull()
    {
        var now = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
        var directory = TestDirectory("maintenance-backlog");
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path, () => now);
        await recorder.InitializeAsync();
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        for (var index = 0; index < 26; index++)
            await SeedMatchAsync(connection, $"backlog-{index:D2}", "ranked", 2, "effects-v2",
                "MASTER-A", "MASTER-B", 0, index % 2, false, $"p-{index}", $"o-{index}");

        var first = await recorder.RunCardFactStorageMaintenanceIfDueAsync(now);
        Assert.Equal(25, first.Compacted);
        Assert.Equal((0, 0), await recorder.RunCardFactStorageMaintenanceIfDueAsync(now.AddMinutes(4)));
        var retry = await recorder.RunCardFactStorageMaintenanceIfDueAsync(now.AddMinutes(5));
        Assert.Equal(1, retry.Compacted);
    }

    [Fact]
    public async Task FactRetentionUsesIndependentBoundariesForNonAnalyticsAndSummarizedRankedMatches()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var directory = TestDirectory("fact-retention-boundaries");
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path, () => now);
        await recorder.InitializeAsync();
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        await SeedRetentionFactAsync(connection, "friendly-11", "friendly", 0, null,
            now.AddDays(-11), 0);
        await SeedRetentionFactAsync(connection, "casual-11", "casual", 0, null,
            now.AddDays(-11), 1);
        await SeedRetentionFactAsync(connection, "legacy-ranked-11", "ranked", 0, null,
            now.AddDays(-11), 0);
        await SeedRetentionFactAsync(connection, "draw-ranked-11", "ranked", 2, "effects-v2",
            now.AddDays(-11), null);
        await SeedRetentionFactAsync(connection, "error-ranked-11", "ranked", 2, "effects-v2",
            now.AddDays(-11), 0, "invalid");
        await SeedRetentionFactAsync(connection, "friendly-10", "friendly", 0, null,
            now.AddDays(-10), 0);
        await SeedRetentionFactAsync(connection, "friendly-9", "friendly", 0, null,
            now.AddDays(-9), 0);
        await SeedRetentionFactAsync(connection, "sandbox-11", "sandbox", 0, null,
            now.AddDays(-11), 0);
        await SeedRetentionFactAsync(connection, "unrecognized-mode-11", "future-mode", 0, null,
            now.AddDays(-11), 0);
        await SeedRetentionFactAsync(connection, "active-old", "friendly", 0, null,
            null, null);
        await SeedRetentionFactAsync(connection, "ranked-31-compacted", "ranked", 2, "effects-v2",
            now.AddDays(-31), 0);
        await SeedRetentionFactAsync(connection, "ranked-30-compacted", "ranked", 2, "effects-v2",
            now.AddDays(-30), 0);

        Assert.Equal(2, await recorder.CompactCompletedCardFactsBatchAsync());
        await SeedRetentionFactAsync(connection, "ranked-31-uncompacted", "ranked", 2, "effects-v2",
            now.AddDays(-31), 0);

        Assert.Equal(6, await recorder.PruneCompactedCardFactDetailsAsync(now));

        var inspect = connection.CreateCommand();
        inspect.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM match_card_facts
                 WHERE match_id IN ('friendly-11','casual-11','legacy-ranked-11',
                     'draw-ranked-11','error-ranked-11')),
                (SELECT COUNT(*) FROM match_card_fact_summaries
                 WHERE match_id IN ('friendly-11','casual-11','legacy-ranked-11',
                     'draw-ranked-11','error-ranked-11')),
                (SELECT COUNT(*) FROM match_card_fact_compactions
                 WHERE match_id IN ('friendly-11','casual-11','legacy-ranked-11',
                     'draw-ranked-11','error-ranked-11')),
                (SELECT COUNT(*) FROM match_card_facts
                 WHERE match_id IN ('friendly-10','friendly-9','sandbox-11','active-old','unrecognized-mode-11',
                     'ranked-30-compacted','ranked-31-uncompacted')),
                (SELECT COUNT(*) FROM match_card_facts WHERE match_id='ranked-31-compacted'),
                (SELECT COUNT(*) FROM match_card_fact_summaries WHERE match_id='ranked-31-compacted'),
                (SELECT COUNT(*) FROM match_card_fact_compactions
                 WHERE match_id='ranked-31-compacted' AND details_pruned_utc IS NOT NULL),
                (SELECT COUNT(*) FROM match_card_fact_compactions
                 WHERE match_id='ranked-31-uncompacted');
            """;
        await using var reader = await inspect.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(0, reader.GetInt32(2));
        Assert.Equal(7, reader.GetInt32(3));
        Assert.Equal(0, reader.GetInt32(4));
        Assert.True(reader.GetInt32(5) > 0);
        Assert.Equal(1, reader.GetInt32(6));
        Assert.Equal(0, reader.GetInt32(7));
    }

    private static async Task SeedMatchAsync(SqliteConnection connection, string matchId, string mode,
        int analyticsVersion, string? effectVersion, string master0, string master1, int firstPlayer,
        int winner, bool player0IncludesTarget, string? account0, string? account1)
    {
        var match = connection.CreateCommand();
        match.CommandText = """
            INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,
                started_utc,ended_utc,winner,mode_id,first_player,effect_version,analytics_version)
            VALUES($id,'ROOM',1,'P0','P1','D0','D1','2026-09-09T00:00:00.0000000+00:00',
                '2026-09-09T00:10:00.0000000+00:00',$winner,$mode,$first,$effect,$analytics);
            """;
        match.Parameters.AddWithValue("$id", matchId);
        match.Parameters.AddWithValue("$winner", winner);
        match.Parameters.AddWithValue("$mode", mode);
        match.Parameters.AddWithValue("$first", firstPlayer);
        match.Parameters.AddWithValue("$effect", (object?)effectVersion ?? DBNull.Value);
        match.Parameters.AddWithValue("$analytics", analyticsVersion);
        await match.ExecuteNonQueryAsync();
        for (var playerIndex = 0; playerIndex < 2; playerIndex++)
        {
            var participant = connection.CreateCommand();
            participant.CommandText = """
                INSERT INTO match_participants(match_id,player_index,account_id,display_name,master_id,
                    master_name,deck_name,deck_snapshot_coverage)
                VALUES($match,$player,$account,$name,$master,$master,'deck','exact');
                """;
            participant.Parameters.AddWithValue("$match", matchId);
            participant.Parameters.AddWithValue("$player", playerIndex);
            participant.Parameters.AddWithValue("$account",
                (object?)(playerIndex == 0 ? account0 : account1) ?? DBNull.Value);
            participant.Parameters.AddWithValue("$name", $"P{playerIndex}");
            participant.Parameters.AddWithValue("$master", playerIndex == 0 ? master0 : master1);
            await participant.ExecuteNonQueryAsync();
            var deck = connection.CreateCommand();
            deck.CommandText = """
                INSERT INTO match_deck_cards(match_id,player_index,section,card_id,quantity)
                VALUES($match,$player,'main',$card,1);
                """;
            deck.Parameters.AddWithValue("$match", matchId);
            deck.Parameters.AddWithValue("$player", playerIndex);
            deck.Parameters.AddWithValue("$card",
                playerIndex == 0 && player0IncludesTarget ? "TARGET" : $"OTHER-{playerIndex}");
            await deck.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedRetentionFactAsync(SqliteConnection connection, string matchId,
        string mode, int analyticsVersion, string? effectVersion, DateTimeOffset? endedAt,
        int? winner, string? error = null)
    {
        var match = connection.CreateCommand();
        match.CommandText = """
            INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,
                started_utc,ended_utc,winner,error,mode_id,first_player,effect_version,analytics_version)
            VALUES($id,'ROOM',1,'P0','P1','D0','D1','2026-07-01T00:00:00.0000000+00:00',
                $ended,$winner,$error,$mode,0,$effect,$analytics);
            """;
        match.Parameters.AddWithValue("$id", matchId);
        match.Parameters.AddWithValue("$ended", (object?)endedAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        match.Parameters.AddWithValue("$winner", (object?)winner ?? DBNull.Value);
        match.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        match.Parameters.AddWithValue("$mode", mode);
        match.Parameters.AddWithValue("$effect", (object?)effectVersion ?? DBNull.Value);
        match.Parameters.AddWithValue("$analytics", analyticsVersion);
        await match.ExecuteNonQueryAsync();

        var participant = connection.CreateCommand();
        participant.CommandText = """
            INSERT INTO match_participants(match_id,player_index,display_name,master_id,master_name,
                deck_name,deck_snapshot_coverage)
            VALUES($match,0,'P0','MASTER-A','MASTER-A','deck','exact');
            INSERT INTO match_deck_cards(match_id,player_index,section,card_id,quantity)
            VALUES($match,0,'main','TARGET',1);
            """;
        participant.Parameters.AddWithValue("$match", matchId);
        await participant.ExecuteNonQueryAsync();

        var fact = connection.CreateCommand();
        fact.CommandText = """
            INSERT INTO match_card_facts(match_id,fact_key,command_sequence,revision,round,turn,
                phase,occurred_utc,kind,player_index,card_id,card_instance_id,source_zone,
                destination_zone,coverage,metadata_json)
            VALUES($match,'legacy-fact',1,1,1,1,'Main','2026-07-01T00:00:00.0000000+00:00',
                'draw',0,'TARGET','target-1','library','hand','exact','{}');
            """;
        fact.Parameters.AddWithValue("$match", matchId);
        await fact.ExecuteNonQueryAsync();
    }

    private static string TestDirectory(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-card-analytics", name,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
