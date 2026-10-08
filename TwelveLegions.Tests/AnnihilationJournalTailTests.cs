using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AnnihilationJournalTailTests(
    LegacyAnnihilationCheckpointFixture fixture)
    : IClassFixture<LegacyAnnihilationCheckpointFixture>
{
    [Fact]
    public async Task LegacyNamedEventHashCannotBeSubstitutedIntoACanonicalEndTurnTail()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-annihilation-journal-tail",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var templatePath = Path.Combine(root, "template.db");
        var healthyPath = Path.Combine(root, "healthy.db");
        var mutatedPath = Path.Combine(root, "legacy-event-hash.db");

        try
        {
            var evidence = await PersistCanonicalEndTurnTailAsync(templatePath);
            SqliteConnection.ClearAllPools();
            Backup(templatePath, healthyPath);
            Backup(templatePath, mutatedPath);

            await AssertHealthyControlAsync(healthyPath, evidence);

            var eventBefore = await ReadEventAsync(mutatedPath, evidence.MatchId);
            var checkpointBefore = await ReadCheckpointAsync(mutatedPath, evidence.MatchId);
            Assert.Equal(1, eventBefore.Sequence);
            Assert.Equal(evidence.CanonicalHash, eventBefore.StateHash);
            Assert.NotEqual(evidence.CanonicalHash, evidence.LegacyEventHash);

            await ReplaceOnlyTailStateHashAsync(mutatedPath, evidence.MatchId,
                evidence.LegacyEventHash);

            var eventAfter = await ReadEventAsync(mutatedPath, evidence.MatchId);
            var checkpointAfter = await ReadCheckpointAsync(mutatedPath, evidence.MatchId);
            Assert.Equal(eventBefore with { StateHash = evidence.LegacyEventHash }, eventAfter);
            Assert.Equal(checkpointBefore, checkpointAfter);

            await using var freshRecorder = new MatchRecorder(mutatedPath);
            await freshRecorder.InitializeAsync();
            freshRecorder.AttachCatalog(fixture.Catalog);
            var failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
                freshRecorder.LoadJournalEngineAsync(evidence.MatchId));
            Assert.Equal("v2 恢复尾部校验失败：1", failure.Message);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<TailEvidence> PersistCanonicalEndTurnTailAsync(string path)
    {
        var legacy = fixture.CreateCheckpoint(explicitLegacyAtomicFlow: false);
        var engine = L12GameEngine.RestoreCheckpoint(fixture.Catalog,
            legacy.StateJson, legacy.RandomState, legacy.CardFactSignalSequence,
            autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

        var prompt = Assert.Single(engine.State.PendingPrompts);
        var settled = engine.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
        Assert.True(settled.Accepted, settled.Error);
        Assert.Empty(engine.State.EffectStack);
        Assert.Empty(engine.State.PendingPrompts);

        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        recorder.AttachCatalog(fixture.Catalog);
        await recorder.StartAsync(engine);

        var actor = engine.State.ActivePlayer;
        var eventSequenceBeforeEndTurn = engine.State.EventSequence;
        var command = new L12Command("endTurn");
        var result = engine.Handle(actor, command);
        Assert.True(result.Accepted, result.Error);

        var canonicalHash = engine.ComputeStateHash();
        var legacyEventHash = ComputeLegacyNamedEventHash(engine,
            eventSequenceBeforeEndTurn);
        await recorder.AppendAsync(engine, 1, actor,
            JsonSerializer.Serialize(command), result);

        return new TailEvidence(engine.State.MatchId, canonicalHash, legacyEventHash);
    }

    private string ComputeLegacyNamedEventHash(L12GameEngine canonical,
        long eventSequenceBeforeEndTurn)
    {
        var canonicalStateJson = canonical.SerializeFullState();
        var clone = L12GameEngine.RestoreCheckpoint(fixture.Catalog,
            canonicalStateJson, Assert.IsType<L12RandomState>(canonical.RandomState),
            canonical.CardFactSignalSequence,
            autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

        var candidates = clone.State.Events
            .Select((item, index) => (Event: item, Index: index))
            .Where(item => item.Event.Sequence > eventSequenceBeforeEndTurn
                && item.Event.Type == "damage"
                && item.Event.Text.Contains(
                    LegacyAnnihilationCheckpointFixture.CanonicalName,
                    StringComparison.Ordinal))
            .ToArray();
        var candidate = Assert.Single(candidates);
        var legacyText = candidate.Event.Text.Replace(
            LegacyAnnihilationCheckpointFixture.CanonicalName,
            LegacyAnnihilationCheckpointFixture.LegacyName,
            StringComparison.Ordinal);
        clone.State.Events[candidate.Index] = candidate.Event with { Text = legacyText };

        var matchingLogIndexes = clone.State.Log
            .Select((text, index) => (Text: text, Index: index))
            .Where(item => string.Equals(item.Text, candidate.Event.Text,
                StringComparison.Ordinal))
            .Select(item => item.Index)
            .ToArray();
        var logIndex = Assert.Single(matchingLogIndexes);
        clone.State.Log[logIndex] = legacyText;

        if (clone.State.LastAction?.Sequence == candidate.Event.Sequence)
            clone.State.LastAction = clone.State.LastAction with { Text = legacyText };

        var legacyJson = clone.SerializeFullState();
        using var decoded = JsonDocument.Parse(legacyJson);
        var serializedEventTexts = decoded.RootElement.GetProperty("Events").EnumerateArray()
            .Select(item => item.GetProperty("Text").GetString()).ToArray();
        Assert.DoesNotContain(candidate.Event.Text, serializedEventTexts);
        Assert.Contains(legacyText, serializedEventTexts);
        return clone.ComputeStateHash();
    }

    private async Task AssertHealthyControlAsync(string path, TailEvidence evidence)
    {
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        recorder.AttachCatalog(fixture.Catalog);
        var recovery = Assert.IsType<L12JournalRecoveryState>(
            await recorder.LoadJournalEngineAsync(evidence.MatchId));
        Assert.Equal(1, recovery.CommandSequence);
        Assert.Equal(evidence.CanonicalHash, recovery.Engine.ComputeStateHash());
    }

    private static void Backup(string sourcePath, string destinationPath)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static async Task ReplaceOnlyTailStateHashAsync(string path,
        string matchId, string stateHash)
    {
        await using var connection = Open(path);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE match_events SET state_hash=$hash
            WHERE match_id=$match AND sequence=1;
            """;
        command.Parameters.AddWithValue("$hash", stateHash);
        command.Parameters.AddWithValue("$match", matchId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<PersistedEventRow> ReadEventAsync(string path,
        string matchId)
    {
        await using var connection = Open(path);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,match_id,sequence,received_utc,player_index,command_json,
                   accepted,error,revision,state_hash,state_json,request_id
            FROM match_events WHERE match_id=$match AND sequence=1;
            """;
        command.Parameters.AddWithValue("$match", matchId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var row = new PersistedEventRow(
            reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2),
            reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.GetString(5), reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetInt64(8),
            reader.GetString(9), reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11));
        Assert.False(await reader.ReadAsync());
        return row;
    }

    private static async Task<PersistedCheckpointRow> ReadCheckpointAsync(string path,
        string matchId)
    {
        await using var connection = Open(path);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT match_id,sequence,revision,state_hash,state_encoding,hex(state_blob),
                   uncompressed_bytes,random_draw_count,random_state_version,
                   COALESCE(hex(random_state_blob),''),card_fact_signal_sequence,
                   auto_pass_empty_responses,conceal_hidden_response_availability,created_utc
            FROM match_state_checkpoints WHERE match_id=$match AND sequence=0;
            """;
        command.Parameters.AddWithValue("$match", matchId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var row = new PersistedCheckpointRow(
            reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.GetInt32(6), reader.GetInt64(7), reader.GetInt32(8),
            reader.GetString(9), reader.GetInt64(10), reader.GetInt32(11),
            reader.GetInt32(12), reader.GetString(13));
        Assert.False(await reader.ReadAsync());
        return row;
    }

    private static SqliteConnection Open(string path)
        => new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());

    private sealed record TailEvidence(
        string MatchId, string CanonicalHash, string LegacyEventHash);

    private sealed record PersistedEventRow(
        long Id, string MatchId, long Sequence, string ReceivedUtc,
        int? PlayerIndex, string CommandJson, int Accepted, string? Error,
        long Revision, string StateHash, string StateJson, string? RequestId);

    private sealed record PersistedCheckpointRow(
        string MatchId, long Sequence, long Revision, string StateHash,
        string StateEncoding, string StateBlobHex, int UncompressedBytes,
        long RandomDrawCount, int RandomStateVersion, string RandomStateBlobHex,
        long CardFactSignalSequence, int AutoPassEmptyResponses,
        int ConcealHiddenResponseAvailability, string CreatedUtc);
}
