using Microsoft.Data.Sqlite;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class NextResetLockPersistenceCompatibilityTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(
        Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));

    [Fact]
    [Trait("L12Evidence", "next-reset-lock:durable-reader-hash-fail-closed")]
    public async Task DurableV2ReaderRejectsACheckpointWhoseNewLockWasDroppedButHashWasRetained()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-next-reset-lock",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "matches.db");
        try
        {
            var game = new L12GameEngine(Catalog, "next-reset-lock", "next-reset-durable-hash",
                921145, ["甲", "乙"], [Catalog.DeckAt(0), Catalog.DeckAt(1)],
                skipPreparation: true, autoPassEmptyResponses: false,
                concealHiddenResponseAvailability: false,
                stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion);
            var target = new L12MoraleCard
            {
                CardId = "S01-01C1",
                InstanceId = "next-reset-durable-target",
                CannotUntapAtNextReset = true,
            };
            game.State.Players[0].Morale.Add(target);
            game.State.Revision++;

            await using var recorder = new MatchRecorder(database);
            await recorder.InitializeAsync();
            recorder.AttachCatalog(Catalog);
            await recorder.StartAsync(game);

            await using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
            {
                await connection.OpenAsync();
                byte[] blob;
                int expectedBytes;
                var read = connection.CreateCommand();
                read.CommandText = """
                    SELECT state_encoding,state_blob,uncompressed_bytes
                    FROM match_state_checkpoints WHERE match_id=$match AND sequence=0;
                    """;
                read.Parameters.AddWithValue("$match", game.State.MatchId);
                await using (var reader = await read.ExecuteReaderAsync())
                {
                    Assert.True(await reader.ReadAsync());
                    Assert.Equal("json-br-v1", reader.GetString(0));
                    blob = Assert.IsType<byte[]>(reader[1]);
                    expectedBytes = reader.GetInt32(2);
                }

                var state = JsonNode.Parse(Decompress(blob, expectedBytes))!;
                Assert.True(RemovePropertyRecursively(state,
                    nameof(L12MoraleCard.CannotUntapAtNextReset)) > 0);
                var raw = Encoding.UTF8.GetBytes(state.ToJsonString());
                var update = connection.CreateCommand();
                update.CommandText = """
                    UPDATE match_state_checkpoints
                    SET state_blob=$state,uncompressed_bytes=$bytes
                    WHERE match_id=$match AND sequence=0;
                    """;
                update.Parameters.AddWithValue("$state", Compress(raw));
                update.Parameters.AddWithValue("$bytes", raw.Length);
                update.Parameters.AddWithValue("$match", game.State.MatchId);
                Assert.Equal(1, await update.ExecuteNonQueryAsync());
            }

            var failure = await Assert.ThrowsAsync<InvalidDataException>(
                () => recorder.LoadJournalEngineAsync(game.State.MatchId));
            Assert.Equal("v2 对局没有可验证的状态检查点", failure.Message);
            Assert.Contains("哈希不一致", failure.InnerException?.Message ?? string.Empty,
                StringComparison.Ordinal);
        }
        finally
        {
            PlatformTestDirectoryCleanup.Delete(directory);
        }
    }

    private static string Decompress(byte[] blob, int expectedBytes)
    {
        using var input = new MemoryStream(blob, writable: false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(expectedBytes);
        brotli.CopyTo(output);
        Assert.Equal(expectedBytes, output.Length);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
            brotli.Write(raw);
        return output.ToArray();
    }

    private static int RemovePropertyRecursively(JsonNode node, string propertyName)
    {
        var removed = 0;
        if (node is JsonObject obj)
        {
            if (obj.Remove(propertyName)) removed++;
            foreach (var child in obj.Select(pair => pair.Value).Where(value => value is not null).ToArray())
                removed += RemovePropertyRecursively(child!, propertyName);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array.Where(value => value is not null).ToArray())
                removed += RemovePropertyRecursively(child!, propertyName);
        }
        return removed;
    }
}
