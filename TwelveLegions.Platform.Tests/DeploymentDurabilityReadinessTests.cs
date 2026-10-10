using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class DeploymentDurabilityReadinessTests
{
    [Fact]
    public async Task EmptyInitializedStoreIsOneReadSnapshotNotAStopPermit()
    {
        await using var fixture = await Fixture.Create();
        var before = fixture.FactHashes();
        var result = await fixture.Recorder.DeploymentReadinessAsync();
        Assert.True(result.Verified);
        Assert.True(result.Clear);
        Assert.False(result.GrantsStopPermit);
        Assert.Equal("none", result.FailureCode);
        Assert.Equal(14, result.Blockers.Count);
        Assert.All(result.Blockers.Values, count => Assert.Equal(0, count));
        Assert.Equal(before, fixture.FactHashes());
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("friendly")]
    [InlineData("casual")]
    [InlineData("ranked")]
    [InlineData("tournament")]
    [InlineData("sandbox")]
    public async Task EveryModeUnfinishedIncludingHistoricalRowsBlocks(string mode)
    {
        await using var fixture = await Fixture.Create();
        fixture.Match(mode, ended: false);
        var result = await fixture.Recorder.DeploymentReadinessAsync();
        Assert.True(result.Verified);
        Assert.False(result.Clear);
        Assert.Equal(1, result.Blockers["unfinished_matches"]);
        Assert.False(result.GrantsStopPermit);
    }

    [Theory]
    [InlineData("mode_id='private-unknown-mode'", "unknown_modes")]
    [InlineData("storage_version=99", "unsupported_versions")]
    [InlineData("hash_version=2", "unsupported_versions")]
    [InlineData("started_utc='broken-date'", "invalid_metadata")]
    [InlineData("ended_utc='broken-date'", "invalid_metadata")]
    [InlineData("ended_utc='2025-01-01T00:00:00Z'", "invalid_metadata")]
    public async Task UnknownVersionsModesAndDatesFailClosed(string mutation, string blocker)
    {
        await using var fixture = await Fixture.Create();
        fixture.Match("friendly", ended: true);
        fixture.Sql("UPDATE matches SET " + mutation + ";");
        var result = await fixture.Recorder.DeploymentReadinessAsync();
        Assert.True(result.Verified);
        Assert.False(result.Clear);
        Assert.Equal(1, result.Blockers[blocker]);
        Assert.DoesNotContain("private-unknown-mode", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("quarantined")]
    [InlineData("waived")]
    [InlineData("private-unknown-status")]
    public async Task UnconfirmedOrUncontractedRankedSettlementNeverPasses(string status)
    {
        await using var fixture = await Fixture.Create();
        fixture.Match("ranked", ended: true);
        fixture.Sql("""
            INSERT INTO ranked_settlement_outbox(match_id,payload_json,payload_hash,status,created_utc,applied_utc)
            VALUES('private-match-id','private-payload','hash',$status,'2026-01-01T00:00:00Z','2026-01-02T00:00:00Z');
            """, ("$status", status));
        var result = await fixture.Recorder.DeploymentReadinessAsync();
        Assert.False(result.Clear);
        Assert.Equal(1, result.Blockers["ranked_settlement_unresolved"]);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("private-match-id", json);
        Assert.DoesNotContain("private-payload", json);
        Assert.DoesNotContain("private-unknown-status", json);
    }

    [Fact]
    public async Task AppliedRankedSettlementRequiresValidMetadataAndPreservesFacts()
    {
        await using var fixture = await Fixture.Create();
        fixture.Match("ranked", ended: true);
        fixture.Sql("""
            INSERT INTO ranked_settlement_outbox(match_id,payload_json,payload_hash,status,created_utc,applied_utc)
            VALUES('private-match-id','{}','hash','applied','2026-01-01T00:00:00Z','2026-01-02T00:00:00Z');
            """);
        Assert.True((await fixture.Recorder.DeploymentReadinessAsync()).Clear);
        fixture.Sql("UPDATE ranked_settlement_outbox SET last_error='private-error';");
        var before = fixture.FactHashes();
        var result = await fixture.Recorder.DeploymentReadinessAsync();
        Assert.False(result.Clear);
        Assert.Equal(1, result.Blockers["ranked_settlement_unresolved"]);
        Assert.DoesNotContain("private-error", JsonSerializer.Serialize(result));
        Assert.Equal(before, fixture.FactHashes());
    }

    [Theory]
    [InlineData("runtime", "active_or_unknown_runtime")]
    [InlineData("quarantine", "quarantined_recovery")]
    [InlineData("tournament", "tournament_settlement_unresolved")]
    [InlineData("response", "response_outbox_unresolved")]
    [InlineData("sandbox", "sandbox_unresolved")]
    [InlineData("journal", "journal_orphans")]
    [InlineData("v2", "missing_v2_checkpoint")]
    public async Task EachDurabilityDomainBlocksIndependently(string domain, string blocker)
    {
        await using var fixture = await Fixture.Create();
        fixture.Match(domain == "tournament" ? "tournament" : domain == "sandbox" ? "sandbox" : "ranked", ended: true);
        switch (domain)
        {
            case "runtime": fixture.Sql("""
                INSERT INTO ranked_match_runtime(match_id,room_code,status,checkpoint_json,checkpoint_hash,updated_utc)
                VALUES('private-match-id','fixture-room','active','{}','hash','2026-01-01T00:00:00Z');
                """); break;
            case "quarantine": fixture.Sql("INSERT INTO ranked_recovery_quarantine VALUES('private-match-id','private-reason','2026-01-01T00:00:00Z');"); break;
            case "tournament": fixture.Sql("""
                INSERT INTO tournament_result_outbox(match_id,tournament_id,tournament_match_id,winner,created_utc)
                VALUES('private-match-id','fixture-tournament','fixture-table',0,'2026-01-01T00:00:00Z');
                """); break;
            case "response": fixture.Sql("""
                INSERT INTO response_preference_outbox(operation_id,match_id,journal_sequence,account_id,player_index,payload_json,payload_hash,created_utc)
                VALUES('fixture-op','private-match-id',1,'private-account',0,'{}','hash','2026-01-01T00:00:00Z');
                """); break;
            case "sandbox": fixture.Sql("""
                INSERT INTO sandbox_recordings(match_id,owner_instance_id,status,last_activity_utc)
                VALUES('private-match-id','fixture-owner','active','2026-01-01T00:00:00Z');
                """); break;
            case "journal": fixture.Sql("""
                INSERT INTO match_events(match_id,sequence,received_utc,command_json,accepted,revision,state_hash,state_json)
                VALUES('orphan-secret',1,'2026-01-01T00:00:00Z','private-command',0,0,'hash','{}');
                """); break;
            case "v2": fixture.Sql("UPDATE matches SET storage_version=2,hash_version=2;"); break;
        }
        var before = fixture.FactHashes();
        var result = await fixture.Recorder.DeploymentReadinessAsync();
        Assert.True(result.Verified);
        Assert.False(result.Clear);
        Assert.Equal(1, result.Blockers[blocker]);
        Assert.Equal(before, fixture.FactHashes());
    }

    [Fact]
    public async Task EveryQueryUsesTheSameSnapshotDespiteAConcurrentWriter()
    {
        await using var fixture = await Fixture.Create();
        var wrote = false;
        var result = await fixture.Recorder.DeploymentReadinessAsync(testObserver: stage =>
        {
            if (stage != "unfinished_matches") return;
            fixture.Match("friendly", ended: false);
            wrote = true;
        });
        Assert.True(wrote);
        Assert.True(result.Clear);
        Assert.False(result.GrantsStopPermit); // External activity must still be fenced by the coordinator.
        Assert.False((await fixture.Recorder.DeploymentReadinessAsync()).Clear);
    }

    [Theory]
    [InlineData("ranked")]
    [InlineData("tournament")]
    public async Task CompletedRuntimeWithoutSettlementFactIsStillBlocked(string mode)
    {
        await using var fixture = await Fixture.Create();
        fixture.Match(mode, ended: true);
        fixture.Sql("""
            INSERT INTO ranked_match_runtime(match_id,room_code,status,checkpoint_json,checkpoint_hash,updated_utc)
            VALUES('private-match-id','fixture-room','completed','{}','hash','2026-01-02T00:00:00Z');
            """);
        var result = await fixture.Recorder.DeploymentReadinessAsync();
        Assert.True(result.Verified);
        Assert.False(result.Clear);
        Assert.Equal(1, result.Blockers["runtime_inconsistent"]);
    }

    [Theory]
    [InlineData("winner=1")]
    [InlineData("tournament_id=''")]
    [InlineData("tournament_match_id=''")]
    public async Task AppliedTournamentResultMustAgreeWithItsStructuredFacts(string mutation)
    {
        await using var fixture = await Fixture.Create();
        fixture.Match("tournament", ended: true);
        fixture.Sql("""
            UPDATE matches SET winner=0;
            INSERT INTO tournament_result_outbox(match_id,tournament_id,tournament_match_id,winner,status,created_utc,applied_utc)
            VALUES('private-match-id','fixture-event','fixture-table',0,'applied','2026-01-02T00:00:00Z','2026-01-03T00:00:00Z');
            """);
        Assert.True((await fixture.Recorder.DeploymentReadinessAsync()).Clear);
        fixture.Sql("UPDATE tournament_result_outbox SET " + mutation + ";");
        var result = await fixture.Recorder.DeploymentReadinessAsync();
        Assert.False(result.Clear);
        Assert.Equal(1, result.Blockers["tournament_settlement_unresolved"]);
    }

    [Fact]
    public async Task LegitimateReplayPayloadExpiryIsNotConfusedWithAMissingCheckpoint()
    {
        await using var fixture = await Fixture.Create();
        fixture.Match("friendly", ended: true);
        fixture.Sql("""
            UPDATE matches SET storage_version=2,hash_version=2,initial_state_json='{}';
            INSERT INTO match_state_checkpoints(match_id,sequence,revision,state_hash,state_encoding,state_blob,
                uncompressed_bytes,random_draw_count,card_fact_signal_sequence,auto_pass_empty_responses,
                conceal_hidden_response_availability,created_utc)
            VALUES('private-match-id',0,0,'hash','gzip',X'0001',2,0,0,0,0,'2026-01-01T00:00:00Z');
            UPDATE player_replay_cleanup_schedule SET next_run_utc='2026-01-02T00:00:00Z';
            """);
        Assert.True((await fixture.Recorder.DeploymentReadinessAsync()).Clear);
        var cleanup = await fixture.Recorder.RunPlayerReplayCleanupIfDueAsync(
            utcNow: DateTimeOffset.Parse("2026-10-06T00:00:00Z"));
        Assert.Equal(1, cleanup.PurgedMatches);
        Assert.True(await fixture.Recorder.IsPlayerReplayPayloadExpiredAsync("private-match-id"));
        var before = fixture.FactHashes();
        Assert.True((await fixture.Recorder.DeploymentReadinessAsync()).Clear);
        Assert.Equal(before, fixture.FactHashes());
        fixture.Sql("UPDATE player_replay_payload_expirations SET expired_utc='broken-date';");
        var broken = await fixture.Recorder.DeploymentReadinessAsync();
        Assert.False(broken.Clear);
        Assert.Equal(1, broken.Blockers["expiration_inconsistent"]);
        Assert.Equal(1, broken.Blockers["missing_v2_checkpoint"]);
    }

    [Fact]
    public async Task SandboxRetentionAnchorMustMatchItsTerminalTime()
    {
        await using var fixture = await Fixture.Create();
        fixture.Match("sandbox", ended: true);
        fixture.Sql("""
            INSERT INTO sandbox_recordings(match_id,owner_instance_id,status,last_activity_utc,retention_anchor_utc)
            VALUES('private-match-id','','completed','2026-01-02T00:00:00Z','2026-01-02T00:00:00Z');
            """);
        Assert.True((await fixture.Recorder.DeploymentReadinessAsync()).Clear);
        fixture.Sql("UPDATE sandbox_recordings SET retention_anchor_utc='2026-01-03T00:00:00Z';");
        Assert.Equal(1, (await fixture.Recorder.DeploymentReadinessAsync()).Blockers["sandbox_unresolved"]);
    }

    [Fact]
    public async Task MatchingButInvalidTournamentWinnersDoNotMakeResultValid()
    {
        await using var fixture = await Fixture.Create();
        fixture.Match("tournament", ended: true);
        fixture.Sql("""
            UPDATE matches SET winner=-1;
            INSERT INTO tournament_result_outbox(match_id,tournament_id,tournament_match_id,winner,status,created_utc,applied_utc)
            VALUES('private-match-id','fixture-event','fixture-table',-1,'applied','2026-01-02T00:00:00Z','2026-01-03T00:00:00Z');
            """);
        var result = await fixture.Recorder.DeploymentReadinessAsync();
        Assert.True(result.Verified);
        Assert.False(result.Clear);
        Assert.Equal(1, result.Blockers["tournament_settlement_unresolved"]);
    }

    [Theory]
    [InlineData("DROP TABLE response_preference_outbox;")]
    [InlineData("ALTER TABLE matches RENAME COLUMN mode_id TO unknown_mode_column;")]
    [InlineData("PRAGMA user_version=999;")]
    public async Task SchemaDamageIsUnknownNotEmptyAndIsNeverRepaired(string sql)
    {
        await using var fixture = await Fixture.Create();
        fixture.Sql(sql);
        var before = fixture.FactHashes();
        var result = await fixture.Recorder.DeploymentReadinessAsync();
        Assert.False(result.Verified);
        Assert.False(result.Clear);
        Assert.Equal("schema_invalid", result.FailureCode);
        Assert.Equal(before, fixture.FactHashes());
    }

    [Fact]
    public async Task MissingDatabaseIsNotCreatedAndCancellationDoesNotOpenIt()
    {
        await using var fixture = await Fixture.Create();
        var missing = Path.Combine(fixture.Root, "absent.db");
        await using var recorder = new MatchRecorder(missing);
        var result = await recorder.DeploymentReadinessAsync();
        Assert.False(result.Clear);
        Assert.False(File.Exists(missing));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal("cancelled", (await recorder.DeploymentReadinessAsync(cancellation.Token)).FailureCode);
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public async Task BusyAndBudgetExpiryNeverBecomeClear()
    {
        await using var fixture = await Fixture.Create();
        fixture.Sql("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
        using var writer = new SqliteConnection("Data Source=" + fixture.Path);
        writer.Open();
        using var exclusive = writer.CreateCommand();
        // DELETE-mode reserved transaction alone allows readers; an exclusive lock does not.
        exclusive.CommandText = "BEGIN EXCLUSIVE;";
        exclusive.ExecuteNonQuery();
        var clock = Stopwatch.StartNew();
        var busy = await fixture.Recorder.DeploymentReadinessAsync();
        Assert.False(busy.Clear);
        Assert.Equal("sqlite_busy", busy.FailureCode);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
        exclusive.CommandText = "ROLLBACK;";
        exclusive.ExecuteNonQuery();
        var budget = await fixture.Recorder.DeploymentReadinessAsync(budget: TimeSpan.Zero);
        Assert.False(budget.Clear);
        Assert.Equal("query_budget", budget.FailureCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "l12-drain-" + Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(Root, "matches.db");
        internal MatchRecorder Recorder { get; private set; } = null!;
        internal static async Task<Fixture> Create()
        {
            var fixture = new Fixture();
            fixture.Recorder = new MatchRecorder(fixture.Path,
                () => DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
            await fixture.Recorder.InitializeAsync();
            return fixture;
        }
        internal void Match(string mode, bool ended) => Sql("""
            INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,started_utc,ended_utc,mode_id)
            VALUES('private-match-id','fixture-room',1,'private-player-0','private-player-1','private-deck-0','private-deck-1',
              '2026-01-01T00:00:00Z',$ended,$mode);
            """, ("$ended", ended ? "2026-01-02T00:00:00Z" : DBNull.Value), ("$mode", mode));
        internal void Sql(string sql, params (string Name, object Value)[] parameters)
        {
            using var connection = new SqliteConnection("Data Source=" + Path);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            command.ExecuteNonQuery();
        }
        internal string FactHashes() => string.Join("|", new[] { Path, Path + "-wal" }.Select(file =>
        {
            if (!File.Exists(file)) return "absent";
            using var reader = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(reader));
        }));
        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
