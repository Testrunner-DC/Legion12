using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RankingLegacyEventLookupTests
{
    [Theory]
    [InlineData(0, "valid", true)]
    [InlineData(1, "valid", true)]
    [InlineData(1, "invalid", false)]
    [InlineData(1, "missing", true)]
    [InlineData(2, "placeholder", true)]
    [InlineData(3, "placeholder", true)]
    public async Task RankingsKeepLegacyLatestSequenceAndModernParticipantFacts(int storage, string state, bool included)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-legacy-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "matches.db");
            await using var recorder = new MatchRecorder(path);
            await recorder.InitializeAsync();
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await Seed(connection, storage, state);
            var rows = await recorder.ListRankingMatchesAsync();
            Assert.Equal(included ? 1 : 0, rows.Count);
            if (!included) return;
            var row = Assert.Single(rows);
            Assert.Equal("qa", row.MatchId);
            Assert.Equal("a", row.AccountId0);
            Assert.Equal("b", row.AccountId1);
            Assert.Equal(0, row.Winner);
            // Latest sequence, not insertion ID, determines the legacy fact.
            var fromEvent = storage < 2 && state == "valid";
            Assert.Equal(fromEvent ? "latest-A" : "recorded-A", row.Master0);
            Assert.Equal(fromEvent ? "latest-B" : "recorded-B", row.Master1);
            Assert.Equal(fromEvent ? 0 : 1, row.FirstPlayer);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    [Theory]
    [InlineData(1, "valid", 0, 1, 8)]
    [InlineData(1, "valid", 7, 0, 0)]
    [InlineData(1, "invalid", 8, 0, 0)]
    [InlineData(1, "missing", 0, 1, 0)]
    [InlineData(2, "placeholder", 8, 1, 8)]
    [InlineData(3, "placeholder", 8, 1, 8)]
    [InlineData(2, "placeholder", 0, 0, 0)]
    public async Task TitleFactsPreserveRoundValidationAndModernCheckpointFallback(int storage, string state,
        int finalRound, int count, int expectedRound)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-title-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "matches.db");
            await using var recorder = new MatchRecorder(path);
            await recorder.InitializeAsync();
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await Seed(connection, storage, state);
            var now = DateTimeOffset.UtcNow;
            var envelope = new L12RankedSettlementEnvelope(1, "qa", "a", "b", "master-A", "master-B", 0,
                now.AddMinutes(-2), now.AddMinutes(-1), 10, "normal", "", "", finalRound);
            var json = JsonSerializer.Serialize(envelope);
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ranked_settlement_outbox(match_id,payload_json,payload_hash,status,created_utc)
                VALUES('qa',$json,$hash,'applied',$now);
                """;
            command.Parameters.AddWithValue("$json", json);
            command.Parameters.AddWithValue("$hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant());
            command.Parameters.AddWithValue("$now", now.ToString("O"));
            await command.ExecuteNonQueryAsync();
            var facts = await recorder.ListRankedMasterTitleFactsAsync(now);
            Assert.Equal(count, facts.Count);
            if (count == 0) return;
            Assert.Equal(expectedRound, Assert.Single(facts).FinalRound);
            Assert.Equal("a", facts[0].FirstAccountId);
            Assert.Equal("master-A", facts[0].FirstMasterId);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task ModernTitleFactWithoutRoundUsesRealRecorderCheckpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-title-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var engine = new L12GameEngine(catalog, "checkpoint-qa", "QA", 77, ["A", "B"], [0, 1],
                skipPreparation: true, stateFormatVersion: 2);
            engine.State.Round = 8;
            await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            await recorder.StartAsync(engine, "ranked", "a", "b");
            engine.ConcludeByAuthority(0, "合成验收");
            await recorder.CompleteAsync(engine);
            var now = DateTimeOffset.UtcNow;
            var payload = new L12RankedSettlementEnvelope(1, engine.State.MatchId, "a", "b", "M0", "M1", 0,
                now.AddMinutes(-2), now.AddMinutes(-1), 10, "normal", "", "", 0);
            var json = JsonSerializer.Serialize(payload);
            await using var connection = new SqliteConnection($"Data Source={Path.Combine(root, "matches.db")}");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ranked_settlement_outbox(match_id,payload_json,payload_hash,status,created_utc)
                VALUES($id,$json,$hash,'applied',$now);
                """;
            command.Parameters.AddWithValue("$id", engine.State.MatchId);
            command.Parameters.AddWithValue("$json", json);
            command.Parameters.AddWithValue("$hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant());
            command.Parameters.AddWithValue("$now", now.ToString("O"));
            await command.ExecuteNonQueryAsync();
            var fact = Assert.Single(await recorder.ListRankedMasterTitleFactsAsync(now));
            Assert.Equal(8, fact.FinalRound);
            Assert.Equal(engine.State.MatchId, fact.MatchId);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task RankingsRetainNewestFirstAndLimitClampWithoutLegacyEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-ranking-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "matches.db");
            await using var recorder = new MatchRecorder(path);
            await recorder.InitializeAsync();
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await Seed(connection, 2, "missing");
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,
                    started_utc,ended_utc,winner,mode_id,first_player,storage_version)
                VALUES('newer','QA',1,'A','B','[]','[]','2026-10-28T02:00:00Z','2026-10-28T02:01:00Z',0,'ranked',0,2),
                      ('older','QA',1,'A','B','[]','[]','2026-10-27T00:00:00Z','2026-10-27T00:01:00Z',0,'ranked',0,2);
                """;
            await command.ExecuteNonQueryAsync();
            Assert.Equal("newer", Assert.Single(await recorder.ListRankingMatchesAsync(0)).MatchId);
            Assert.Equal(new[] { "newer", "qa" }, (await recorder.ListRankingMatchesAsync(2)).Select(row => row.MatchId));
            Assert.Equal(new[] { "newer", "qa", "older" }, (await recorder.ListRankingMatchesAsync(20_001)).Select(row => row.MatchId));
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    private static async Task Seed(SqliteConnection connection, int storage, string state)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,
                started_utc,ended_utc,winner,mode_id,account_0,account_1,first_player,storage_version)
              VALUES('qa','QA',1,'A','B','[]','[]','2026-10-28T00:00:00Z','2026-10-28T00:01:00Z',0,
                'ranked','a','b',1,$version);
            INSERT INTO match_participants(match_id,player_index,display_name,deck_name,master_name)
              VALUES('qa',0,'A','QA','recorded-A'),('qa',1,'B','QA','recorded-B');
            """;
        command.Parameters.AddWithValue("$version", storage);
        await command.ExecuteNonQueryAsync();
        if (state == "missing") return;
        command.Parameters.Clear();
        command.CommandText = """
            INSERT INTO match_events(match_id,sequence,received_utc,player_index,command_json,accepted,
                revision,state_hash,state_json)
              VALUES('qa',5,'2026-10-28T00:01:00Z',0,'{}',1,5,'synthetic',$state),
                ('qa',1,'2026-10-28T00:00:00Z',0,'{}',1,1,'synthetic',$older);
            """;
        command.Parameters.AddWithValue("$older", storage >= 2 ? "{}" : "{\"Round\":1,\"FirstPlayer\":1}");
        command.Parameters.AddWithValue("$state", storage >= 2 ? "{}" : state == "invalid" ? "not-json" :
            "{\"Round\":8,\"FirstPlayer\":0,\"Players\":[{\"MasterName\":\"latest-A\"},{\"MasterName\":\"latest-B\"}]}");
        await command.ExecuteNonQueryAsync();
    }
}
