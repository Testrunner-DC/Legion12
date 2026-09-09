using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class Batch300RankingSourceTests
{
    [Fact]
    public async Task AnalyticsIncludesAppliedAndLegacyWithoutReplayButRejectsUnverifiedRows()
    {
        var path = Path.Combine(Path.GetTempPath(), "l12-batch300", Guid.NewGuid().ToString("N"), "matches.db");
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        foreach (var kind in new[] { "legacy", "valid", "pending", "corrupt", "mismatch", "invalid", "error", "quarantined", "master-mismatch", "bad-time", "bad-json" })
        {
            var engine = new L12GameEngine(catalog, kind, "QA300", 77, ["甲", "乙"], [0, 1], skipPreparation: true);
            await recorder.StartAsync(engine.State, "ranked", "a", "b");
            engine.ConcludeByAuthority(0, "测试结束");
            await recorder.CompleteAsync(engine);
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE matches SET started_utc=$start,ended_utc=$end,storage_version=2 WHERE match_id=$id; DELETE FROM match_events WHERE match_id=$id; DELETE FROM match_state_checkpoints WHERE match_id=$id;";
            command.Parameters.AddWithValue("$id", kind);
            command.Parameters.AddWithValue("$start", now.AddMinutes(-10).ToString("O"));
            command.Parameters.AddWithValue("$end", now.ToString("O"));
            await command.ExecuteNonQueryAsync();
            if (kind == "bad-time")
            {
                command.CommandText = "UPDATE matches SET started_utc='not-a-date' WHERE match_id=$id;";
                await command.ExecuteNonQueryAsync();
                continue;
            }
            if (kind == "bad-json")
            {
                command.CommandText = "UPDATE matches SET storage_version=1 WHERE match_id=$id; INSERT INTO match_events(match_id,sequence,received_utc,command_json,accepted,revision,state_hash,state_json) VALUES($id,1,$end,'{}',1,1,'test','{broken');";
                await command.ExecuteNonQueryAsync();
                continue;
            }
            if (kind == "legacy") continue;
            // Room start and recorder start are separate clocks: exact timestamp equality is not required.
            var payload = new L12RankedSettlementEnvelope(1, kind, "a", "b", engine.State.Players[0].MasterId,
                kind == "master-mismatch" ? "wrong" : engine.State.Players[1].MasterId, kind == "mismatch" ? 1 : 0,
                now.AddMinutes(-10).AddMilliseconds(-5), now, 20,
                kind == "invalid" ? "maintenance-invalidated" : "normal", "", "", 10);
            var json = JsonSerializer.Serialize(payload);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO ranked_settlement_outbox(match_id,payload_json,payload_hash,status,created_utc) VALUES($id,$json,$hash,$status,$end);";
            command.Parameters.AddWithValue("$id", kind);
            command.Parameters.AddWithValue("$json", json);
            command.Parameters.AddWithValue("$hash", kind == "corrupt" ? "wrong" : hash);
            command.Parameters.AddWithValue("$status", kind == "pending" ? "pending" : "applied");
            command.Parameters.AddWithValue("$end", now.ToString("O"));
            await command.ExecuteNonQueryAsync();
            if (kind == "error")
            {
                command.CommandText = "UPDATE ranked_settlement_outbox SET last_error='failed reconciliation' WHERE match_id=$id;";
                await command.ExecuteNonQueryAsync();
            }
            if (kind == "quarantined")
            {
                command.CommandText = "INSERT INTO ranked_recovery_quarantine(match_id,reason,created_utc) VALUES($id,'corrupt',$end);";
                await command.ExecuteNonQueryAsync();
            }
        }
        var rows = await recorder.ListRankedAnalyticsMatchesAsync();
        Assert.Equal(new[] { "legacy", "valid" }, rows.Select(r => r.MatchId).Order().ToArray());
        Assert.Equal(2, (await recorder.ListRankingMatchesAsync()).Count);
        var store = new L12PlatformStore(Path.ChangeExtension(path, ".json"), catalog.PresetDecks, officialCards: catalog.Cards);
        var report = store.RankedAnalytics(rows, "7d");
        Assert.Equal(2, report.Summary.Matches);
        Assert.Equal(now, report.Summary.UpdatedAt);
        Assert.Equal(4, report.Masters.Sum(m => m.Games));
        Assert.Equal(2, report.Masters.Sum(m => m.FirstGames));
        Assert.Equal(2, report.Masters.Sum(m => m.SecondGames));
        Assert.Equal(2, store.RankedAnalytics(rows.Concat(rows).ToArray(), "7d").Summary.Matches);
        var old = rows[0] with { MatchId = "old", StartedUtc = now.AddDays(-10).ToString("O"), EndedUtc = now.AddDays(-9).ToString("O") };
        Assert.Equal(2, store.RankedAnalytics(rows.Append(old).ToArray(), "7d").Summary.Matches);
        Assert.Equal(3, store.RankedAnalytics(rows.Append(old).ToArray(), "30d").Summary.Matches);
        var future = old with { MatchId = "future", StartedUtc = now.AddDays(1).ToString("O"), EndedUtc = now.AddDays(2).ToString("O") };
        Assert.Equal(2, store.RankedAnalytics(rows.Append(future).ToArray(), "season").Summary.Matches);
        var authoritative = rows.Single(r => r.MatchId == "valid");
        var correct = store.RankedAnalytics([authoritative], "7d");
        var renamed = store.RankedAnalytics([authoritative with { Master0 = "错误主宰名", Master1 = "" }], "7d");
        Assert.Equal(correct.Masters.Select(m => m.MasterId), renamed.Masters.Select(m => m.MasterId));
        Assert.Equal(1, renamed.Summary.Matches);
    }
}
