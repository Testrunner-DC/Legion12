using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SeasonCutoverRepairIntegrationTests
{
    [Fact]
    public async Task WaivedCompleteV1EnvelopeIsNotDrainedClearsCutoverGateAndRemainsAuditHeld()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            $"l12-season-cutover-waived-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var matchPath = Path.Combine(directory, "matches.db");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"),
                catalog.PresetDecks, officialCards: catalog.Cards);
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var first = platform.Register($"wf{suffix}", "Password123!").Account!;
            var second = platform.Register($"ws{suffix}", "Password123!").Account!;
            platform.SelectRankedFaction(first.Id, "order");
            platform.SelectRankedFaction(second.Id, "chaos");
            await using var recorder = new MatchRecorder(matchPath);
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var matchId = Guid.NewGuid().ToString("N");
            var started = new DateTimeOffset(2026, 9, 3, 10, 17, 41, TimeSpan.Zero);
            var ended = started.AddDays(2).AddHours(10).AddMinutes(54);
            var envelope = new L12RankedSettlementEnvelope(
                1, matchId, first.Id, second.Id, string.Empty, string.Empty, null,
                started, ended, 0, "restore-incompatible", string.Empty, string.Empty,
                13, string.Empty, string.Empty);
            var payload = JsonSerializer.Serialize(envelope);
            var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
                .ToLowerInvariant();

            await using (var connection = new SqliteConnection($"Data Source={matchPath}"))
            {
                await connection.OpenAsync();
                var seed = connection.CreateCommand();
                seed.CommandText = """
                    INSERT INTO matches(
                        match_id,room_code,seed,player_0,player_1,deck_0,deck_1,
                        started_utc,ended_utc,mode_id,account_0,account_1,season_id,
                        initial_state_json,error)
                    VALUES($match,'WAIVED',1,'first','second','deck-a','deck-b',
                           $started,$ended,'ranked',$first,$second,NULL,'{}',NULL);
                    INSERT INTO ranked_settlement_outbox(
                        match_id,payload_json,payload_hash,status,attempts,last_error,
                        created_utc,applied_utc)
                    VALUES($match,$payload,$hash,'waived',1,
                           'season-cutover-waived: explicit operator adjudication',$ended,$applied);
                    """;
                seed.Parameters.AddWithValue("$match", matchId);
                seed.Parameters.AddWithValue("$started", started.ToString("O"));
                seed.Parameters.AddWithValue("$ended", ended.ToString("O"));
                seed.Parameters.AddWithValue("$applied", ended.AddSeconds(1).ToString("O"));
                seed.Parameters.AddWithValue("$first", first.Id);
                seed.Parameters.AddWithValue("$second", second.Id);
                seed.Parameters.AddWithValue("$payload", payload);
                seed.Parameters.AddWithValue("$hash", payloadHash);
                Assert.Equal(2, await seed.ExecuteNonQueryAsync());
            }

            var restored = await manager.RestoreRankedRoomsAsync();

            Assert.Equal(0, restored.SettlementsApplied);
            Assert.Equal(0, restored.Failed);
            Assert.Null(platform.RankedSettlement(matchId, first.Id));
            Assert.Null(platform.RankedSettlement(matchId, second.Id));
            Assert.Contains(matchId, await recorder.ReadAuditRetentionHoldsAsync(CancellationToken.None));

            await using var verify = new SqliteConnection($"Data Source={matchPath};Mode=ReadOnly");
            await verify.OpenAsync();
            var gate = verify.CreateCommand();
            gate.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM matches m WHERE m.ended_utc IS NULL
                    AND LOWER(TRIM(COALESCE(m.mode_id,'')))
                      NOT IN ('friendly','casual','tournament','sandbox')),
                  (SELECT COUNT(*) FROM ranked_settlement_outbox WHERE status='pending'),
                  (SELECT COUNT(*) FROM ranked_settlement_outbox
                    WHERE status='applied' AND last_error IS NOT NULL),
                  ((SELECT COUNT(*) FROM ranked_settlement_outbox WHERE status='quarantined')
                    + (SELECT COUNT(*) FROM ranked_recovery_quarantine));
                """;
            await using (var gateReader = await gate.ExecuteReaderAsync())
            {
                Assert.True(await gateReader.ReadAsync());
                Assert.Equal(0, gateReader.GetInt32(0));
                Assert.Equal(0, gateReader.GetInt32(1));
                Assert.Equal(0, gateReader.GetInt32(2));
                Assert.Equal(0, gateReader.GetInt32(3));
            }
            var status = verify.CreateCommand();
            status.CommandText = """
                SELECT status || '|' || attempts || '|' || last_error
                FROM ranked_settlement_outbox WHERE match_id=$match;
                """;
            status.Parameters.AddWithValue("$match", matchId);
            Assert.Equal("waived|1|season-cutover-waived: explicit operator adjudication",
                Convert.ToString(await status.ExecuteScalarAsync()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
