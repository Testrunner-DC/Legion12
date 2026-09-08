using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PlayerReplayRetentionTests
{
    [Fact]
    public async Task CleanupPreservesBothPlayersWindowAndEvidenceWhilePurgingOnlyReplayPayload()
    {
        var directory = TestDirectory("union-evidence");
        var path = Path.Combine(directory, "matches.db");
        var origin = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
        var now = origin;
        await using var recorder = new MatchRecorder(path, () => now);
        await recorder.InitializeAsync();
        var initial = await recorder.RunPlayerReplayCleanupIfDueAsync(utcNow: now);
        Assert.False(initial.Ran);
        Assert.Equal(origin.AddDays(1), initial.NextRunUtc);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            for (var index = 0; index < MatchRecorder.PlayerReplayWindowSize; index++)
                await SeedMatchAsync(connection, transaction, $"recent-{index:D2}",
                    index % 2 == 0 ? "casual" : "friendly", "account-a", "account-b",
                    origin.AddHours(-30 + index), origin.AddHours(-30 + index).AddMinutes(20),
                    storageVersion: index % 2 == 0 ? 1 : 2);
            await SeedMatchAsync(connection, transaction, "union-protected", "friendly",
                "account-a", "account-c", origin.AddHours(-121), origin.AddHours(-120));
            await SeedMatchAsync(connection, transaction, "old-ranked", "ranked",
                "account-a", "account-b", origin.AddHours(-131), origin.AddHours(-130));
            await SeedMatchAsync(connection, transaction, "old-casual", "casual",
                "account-a", "account-b", origin.AddHours(-111), origin.AddHours(-110));
            await SeedMatchAsync(connection, transaction, "old-friendly", "friendly",
                "account-a", "account-b", origin.AddHours(-110), origin.AddHours(-109),
                storageVersion: 2,
                commandJson: """{"type":"authorityConclusion","agreedDraw":true}""");
            await SeedMatchAsync(connection, transaction, "bug-held", "casual",
                "account-a", "account-b", origin.AddHours(-109), origin.AddHours(-108));
            await SeedMatchAsync(connection, transaction, "room-held", "friendly",
                "account-a", "account-b", origin.AddHours(-108), origin.AddHours(-107),
                roomCode: "BUG001");
            await SeedMatchAsync(connection, transaction, "late-bug-held", "casual",
                "account-a", "account-b", origin.AddHours(-107), origin.AddHours(-106));
            await SeedMatchAsync(connection, transaction, "running", "casual",
                "account-a", "account-b", origin.AddHours(1), null);
            await transaction.CommitAsync();
        }

        var recent = await recorder.ListRecentPlayerReplayMatchesAsync(
            "account-a", "甲", limit: 200);
        Assert.Equal(MatchRecorder.PlayerReplayWindowSize, recent.Count);
        Assert.Equal("recent-29", recent[0].MatchId);
        Assert.Equal("recent-00", recent[^1].MatchId);
        Assert.All(recent, item => Assert.Equal(1, item.CommandCount));
        Assert.Empty(await recorder.ListRecentPlayerReplayMatchesAsync("account-z", "甲"));
        Assert.False(await recorder.IsWithinRecentPlayerReplayWindowAsync(
            "union-protected", "account-a", "甲"));
        Assert.True(await recorder.IsWithinRecentPlayerReplayWindowAsync(
            "union-protected", "account-c", "丙"));
        Assert.False(await recorder.IsWithinRecentPlayerReplayWindowAsync(
            "running", "account-a", "甲"));

        var evidenceReads = 0;
        L12ReplayEvidenceReferences EvidenceProvider()
        {
            evidenceReads++;
            return evidenceReads == 1
                ? new L12ReplayEvidenceReferences([], [])
                : new L12ReplayEvidenceReferences(["late-bug-held"], []);
        }

        now = origin.AddDays(1);
        var cleanup = await recorder.RunPlayerReplayCleanupIfDueAsync(
            activeMatchIds: ["running"],
            protectedEvidenceMatchIds: ["bug-held"],
            protectedEvidenceRoomCodes: ["BUG001"],
            evidenceProvider: EvidenceProvider,
            utcNow: now);
        Assert.True(cleanup.Ran);
        Assert.Equal(2, cleanup.PurgedMatches);
        Assert.Equal(2, cleanup.RetainedCommandRows);
        Assert.True(cleanup.ClearedPayloadBytes > 0);
        Assert.False(cleanup.HasMoreEligibleMatches);
        Assert.Equal(now.AddDays(1), cleanup.NextRunUtc);
        Assert.Equal(2, evidenceReads);

        await AssertPayloadPurgedPreservingArchiveAsync(path, "old-casual");
        await AssertPayloadPurgedPreservingArchiveAsync(path, "old-friendly",
            """{"type":"authorityConclusion","agreedDraw":true}""");
        foreach (var protectedMatch in new[]
                 {
                     "union-protected", "old-ranked", "bug-held", "room-held",
                     "late-bug-held", "running",
                 })
        {
            var rows = await ReadRowsAsync(path, protectedMatch);
            Assert.Equal(0, rows.ExpirationRows);
            Assert.NotNull(rows.InitialStateJson);
            Assert.Equal(1, rows.RequestRows);
            Assert.Equal(1, rows.ActionEventRows);
            Assert.Equal(1, rows.CheckpointRows);
        }
    }

    [Fact]
    public async Task StableTieBreakLeaseAndBoundedCatchUpPreventPermanentBacklog()
    {
        var directory = TestDirectory("tie-lease-backlog");
        var path = Path.Combine(directory, "matches.db");
        var origin = new DateTimeOffset(2026, 9, 1, 4, 0, 0, TimeSpan.Zero);
        var now = origin;
        await using var first = new MatchRecorder(path, () => now);
        await first.InitializeAsync();
        Assert.False((await first.RunPlayerReplayCleanupIfDueAsync(utcNow: now)).Ran);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            for (var index = 0; index < 131; index++)
                await SeedMatchAsync(connection, transaction, $"tie-{index:D3}", "casual",
                    "account-a", "account-b", origin.AddHours(-2), origin.AddHours(-1));
            await transaction.CommitAsync();
        }

        var visible = await first.ListRecentPlayerReplayMatchesAsync("account-a", "甲", 200);
        Assert.Equal(30, visible.Count);
        Assert.Equal("tie-130", visible[0].MatchId);
        Assert.Equal("tie-101", visible[^1].MatchId);
        Assert.True(await first.IsWithinRecentPlayerReplayWindowAsync(
            "tie-101", "account-a", "甲"));
        Assert.False(await first.IsWithinRecentPlayerReplayWindowAsync(
            "tie-100", "account-a", "甲"));

        now = origin.AddDays(1);
        await using var second = new MatchRecorder(path, () => now);
        await second.InitializeAsync();
        var runs = await Task.WhenAll(
            first.RunPlayerReplayCleanupIfDueAsync(utcNow: now),
            second.RunPlayerReplayCleanupIfDueAsync(utcNow: now));
        var ran = Assert.Single(runs, result => result.Ran);
        Assert.Equal(MatchRecorder.PlayerReplayCleanupMaximumMatchesPerRun, ran.PurgedMatches);
        Assert.True(ran.HasMoreEligibleMatches);
        Assert.Equal(now.AddMinutes(5), ran.NextRunUtc);
        Assert.Equal(100, await CountExpirationsAsync(path));

        var early = await first.RunPlayerReplayCleanupIfDueAsync(utcNow: now.AddMinutes(4));
        Assert.False(early.Ran);
        Assert.Equal(now.AddMinutes(5), early.NextRunUtc);

        now = now.AddMinutes(5);
        var drained = await first.RunPlayerReplayCleanupIfDueAsync(utcNow: now);
        Assert.True(drained.Ran);
        Assert.Equal(1, drained.PurgedMatches);
        Assert.False(drained.HasMoreEligibleMatches);
        Assert.Equal(now.AddDays(1), drained.NextRunUtc);
        Assert.Equal(101, await CountExpirationsAsync(path));
        await AssertPayloadPurgedPreservingArchiveAsync(path, "tie-100");
        var protectedBoundary = await ReadRowsAsync(path, "tie-101");
        Assert.Equal(0, protectedBoundary.ExpirationRows);
        Assert.NotNull(protectedBoundary.InitialStateJson);

        await using var restarted = new MatchRecorder(path, () => now);
        await restarted.InitializeAsync();
        var persisted = await restarted.RunPlayerReplayCleanupIfDueAsync(
            utcNow: now.AddHours(12));
        Assert.False(persisted.Ran);
        Assert.Equal(now.AddDays(1), persisted.NextRunUtc);
    }

    [Fact]
    public async Task PurgeFailureRollsBackEveryPayloadMutationAndReleasesLease()
    {
        var directory = TestDirectory("atomic-failure");
        var path = Path.Combine(directory, "matches.db");
        var origin = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var now = origin;
        await using var recorder = new MatchRecorder(path, () => now);
        await recorder.InitializeAsync();
        Assert.False((await recorder.RunPlayerReplayCleanupIfDueAsync(utcNow: now)).Ran);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            for (var index = 0; index < 31; index++)
                await SeedMatchAsync(connection, transaction, $"rollback-{index:D2}", "friendly",
                    "account-a", "account-b", origin.AddHours(-32 + index),
                    origin.AddHours(-31 + index));
            await transaction.CommitAsync();
        }
        var before = await ReadRowsAsync(path, "rollback-00");
        Assert.NotNull(before.InitialStateJson);
        Assert.NotEqual("{}", before.StateJson);
        Assert.Equal(1, before.RequestRows);
        Assert.Equal(1, before.ActionEventRows);
        Assert.Equal(1, before.CheckpointRows);

        var injected = false;
        recorder.StorageFailureInjector = stage =>
        {
            if (stage != "before-player-replay-purge-commit" || injected) return;
            injected = true;
            throw new IOException("injected player replay purge failure");
        };
        now = origin.AddDays(1);
        await Assert.ThrowsAsync<IOException>(() =>
            recorder.RunPlayerReplayCleanupIfDueAsync(utcNow: now));
        recorder.StorageFailureInjector = null;
        Assert.True(injected);
        Assert.Equal(before, await ReadRowsAsync(path, "rollback-00"));
        await AssertCleanupLeaseReleasedAsync(path);

        var retried = await recorder.RunPlayerReplayCleanupIfDueAsync(utcNow: now);
        Assert.True(retried.Ran);
        Assert.Equal(1, retried.PurgedMatches);
        await AssertPayloadPurgedPreservingArchiveAsync(path, "rollback-00");
    }

    private static async Task SeedMatchAsync(
        SqliteConnection connection, SqliteTransaction transaction,
        string matchId, string modeId, string account0, string account1,
        DateTimeOffset startedUtc, DateTimeOffset? endedUtc,
        int storageVersion = 1, string? roomCode = null, string? commandJson = null)
    {
        var stateJson = storageVersion >= MatchRecorder.JournalStorageVersion
            ? "{}"
            : $"{{\"privateState\":\"{new string('x', 512)}\"}}";
        var initialState = $"{{\"initialPrivateState\":\"{new string('i', 256)}\"}}";
        var actionEvent = $"{{\"eventPayload\":\"{new string('e', 192)}\"}}";
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO matches(
                match_id,room_code,seed,player_0,player_1,deck_0,deck_1,started_utc,
                ended_utc,winner,final_hash,error,mode_id,account_0,account_1,
                initial_state_json,storage_version)
            VALUES($id,$room,1,'甲','乙','构筑甲','构筑乙',$started,$ended,$winner,$hash,NULL,
                   $mode,$account0,$account1,$initial,$storage);
            INSERT INTO match_participants(
                match_id,player_index,account_id,display_name,master_id,master_name,
                deck_name,deck_snapshot_coverage)
            VALUES($id,0,$account0,'甲','master-a','主将甲','构筑甲','exact'),
                  ($id,1,$account1,'乙','master-b','主将乙','构筑乙','exact');
            INSERT INTO match_deck_cards(match_id,player_index,section,card_id,quantity)
            VALUES($id,0,'main','card-a',1),($id,1,'main','card-b',1);
            INSERT INTO match_card_facts(
                match_id,fact_key,command_sequence,revision,round,turn,phase,occurred_utc,
                kind,player_index,account_id,card_id,coverage,metadata_json)
            VALUES($id,'fact-1',1,1,1,1,'Main',$started,'played',0,$account0,
                   'card-a','exact','{}');
            INSERT INTO match_events(
                match_id,sequence,received_utc,player_index,command_json,accepted,error,
                revision,state_hash,state_json,request_id)
            VALUES($id,1,$started,0,$command,1,NULL,1,'state-hash',$state,
                   $request);
            INSERT INTO match_action_requests(
                match_id,player_index,request_id,command_sequence,accepted,error,revision,
                state_hash,created_utc)
            VALUES($id,0,$request,1,1,NULL,1,'state-hash',$started);
            INSERT INTO match_action_events(
                match_id,event_sequence,command_sequence,revision,event_json,created_utc)
            VALUES($id,1,1,1,$actionEvent,$started);
            INSERT INTO match_state_checkpoints(
                match_id,sequence,revision,state_hash,state_encoding,state_blob,
                uncompressed_bytes,random_draw_count,random_state_version,random_state_blob,
                card_fact_signal_sequence,auto_pass_empty_responses,
                conceal_hidden_response_availability,created_utc)
            VALUES($id,1,1,'state-hash','test',$checkpoint,256,0,0,NULL,0,0,0,$started);
            """;
        command.Parameters.AddWithValue("$id", matchId);
        command.Parameters.AddWithValue("$room", roomCode ?? $"R-{matchId}");
        command.Parameters.AddWithValue("$started", startedUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$ended",
            endedUtc is null ? DBNull.Value : endedUtc.Value.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$winner", endedUtc is null ? DBNull.Value : 0);
        command.Parameters.AddWithValue("$hash", endedUtc is null ? DBNull.Value : $"final-{matchId}");
        command.Parameters.AddWithValue("$mode", modeId);
        command.Parameters.AddWithValue("$account0", account0);
        command.Parameters.AddWithValue("$account1", account1);
        command.Parameters.AddWithValue("$initial", initialState);
        command.Parameters.AddWithValue("$storage", storageVersion);
        command.Parameters.AddWithValue("$command", commandJson ?? """{"type":"passPriority"}""");
        command.Parameters.AddWithValue("$state", stateJson);
        command.Parameters.AddWithValue("$request", $"request-{matchId}");
        command.Parameters.AddWithValue("$actionEvent", actionEvent);
        command.Parameters.AddWithValue("$checkpoint", new byte[128]);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertPayloadPurgedPreservingArchiveAsync(
        string path, string matchId,
        string expectedCommandJson = """{"type":"passPriority"}""")
    {
        var rows = await ReadRowsAsync(path, matchId);
        Assert.Equal(1, rows.MatchRows);
        Assert.Null(rows.InitialStateJson);
        Assert.Equal(1, rows.CommandRows);
        Assert.Equal(expectedCommandJson, rows.CommandJson);
        Assert.Equal("{}", rows.StateJson);
        Assert.Equal(0, rows.RequestRows);
        Assert.Equal(0, rows.ActionEventRows);
        Assert.Equal(0, rows.CheckpointRows);
        Assert.Equal(2, rows.ParticipantRows);
        Assert.Equal(2, rows.DeckRows);
        Assert.Equal(1, rows.CardFactRows);
        Assert.Equal(0, rows.Winner);
        Assert.Equal($"final-{matchId}", rows.FinalHash);
        Assert.Equal(1, rows.ExpirationRows);
        Assert.Equal(1, rows.TombstoneCommandCount);
        Assert.True(rows.TombstonePayloadBytes > 0);
    }

    private static async Task<ReplayRows> ReadRowsAsync(string path, string matchId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM matches WHERE match_id=$match),
                (SELECT initial_state_json FROM matches WHERE match_id=$match),
                (SELECT COUNT(*) FROM match_events WHERE match_id=$match),
                (SELECT command_json FROM match_events WHERE match_id=$match LIMIT 1),
                (SELECT state_json FROM match_events WHERE match_id=$match LIMIT 1),
                (SELECT COUNT(*) FROM match_action_requests WHERE match_id=$match),
                (SELECT COUNT(*) FROM match_action_events WHERE match_id=$match),
                (SELECT COUNT(*) FROM match_state_checkpoints WHERE match_id=$match),
                (SELECT COUNT(*) FROM match_participants WHERE match_id=$match),
                (SELECT COUNT(*) FROM match_deck_cards WHERE match_id=$match),
                (SELECT COUNT(*) FROM match_card_facts WHERE match_id=$match),
                (SELECT winner FROM matches WHERE match_id=$match),
                (SELECT final_hash FROM matches WHERE match_id=$match),
                (SELECT COUNT(*) FROM player_replay_payload_expirations WHERE match_id=$match),
                COALESCE((SELECT retained_command_count
                          FROM player_replay_payload_expirations WHERE match_id=$match),0),
                COALESCE((SELECT cleared_payload_bytes
                          FROM player_replay_payload_expirations WHERE match_id=$match),0);
            """;
        command.Parameters.AddWithValue("$match", matchId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new ReplayRows(
            reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
            reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetInt32(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetInt32(13), reader.GetInt64(14), reader.GetInt64(15));
    }

    private static async Task<int> CountExpirationsAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM player_replay_payload_expirations;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task AssertCleanupLeaseReleasedAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT lease_owner,lease_expires_utc
            FROM player_replay_cleanup_schedule WHERE singleton_id=1;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(0));
        Assert.True(reader.IsDBNull(1));
    }

    private sealed record ReplayRows(
        int MatchRows,
        string? InitialStateJson,
        int CommandRows,
        string? CommandJson,
        string? StateJson,
        int RequestRows,
        int ActionEventRows,
        int CheckpointRows,
        int ParticipantRows,
        int DeckRows,
        int CardFactRows,
        int? Winner,
        string? FinalHash,
        int ExpirationRows,
        long TombstoneCommandCount,
        long TombstonePayloadBytes);

    private static string TestDirectory(string suffix)
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-player-replay-retention",
            $"{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
