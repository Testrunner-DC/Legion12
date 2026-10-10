using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace TwelveLegions.Server;

internal sealed record L12ResponsePreferenceOutboxEnvelope(
    string OperationId,
    string MatchId,
    long JournalSequence,
    string AccountId,
    int PlayerIndex,
    string TargetMode,
    DateTimeOffset CreatedAtUtc);

internal sealed record L12ResponsePreferenceOutboxEntry(
    long Id,
    L12ResponsePreferenceOutboxEnvelope? Payload,
    string PayloadHash,
    int Attempts,
    string? LoadError);

public sealed partial class MatchRecorder
{
    private static async Task InitializeResponsePreferenceOutboxAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS response_preference_outbox (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                operation_id TEXT NOT NULL UNIQUE,
                match_id TEXT NOT NULL,
                journal_sequence INTEGER NOT NULL,
                account_id TEXT NOT NULL,
                player_index INTEGER NOT NULL,
                payload_json TEXT NOT NULL,
                payload_hash TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'pending',
                attempts INTEGER NOT NULL DEFAULT 0,
                last_error TEXT,
                created_utc TEXT NOT NULL,
                applied_utc TEXT,
                UNIQUE(match_id,journal_sequence)
            );
            CREATE INDEX IF NOT EXISTS ix_response_preference_outbox_pending
                ON response_preference_outbox(status,id);
            CREATE INDEX IF NOT EXISTS ix_response_preference_outbox_account
                ON response_preference_outbox(account_id,status,id);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertOrVerifyResponsePreferenceOutboxAsync(SqliteConnection connection,
        SqliteTransaction transaction, L12ResponsePreferenceOutboxEnvelope envelope)
    {
        if (!L12GameEngine.IsValidResponseMode(envelope.TargetMode)
            || envelope.PlayerIndex is < 0 or > 1 || string.IsNullOrWhiteSpace(envelope.AccountId))
            throw new InvalidDataException("响应设置 outbox 载荷无效");
        var json = JsonSerializer.Serialize(envelope);
        var hash = PersistenceHash(json);
        var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT OR IGNORE INTO response_preference_outbox(
                operation_id,match_id,journal_sequence,account_id,player_index,payload_json,payload_hash,created_utc)
            VALUES($operation,$match,$sequence,$account,$player,$json,$hash,$created);
            """;
        insert.Parameters.AddWithValue("$operation", envelope.OperationId);
        insert.Parameters.AddWithValue("$match", envelope.MatchId);
        insert.Parameters.AddWithValue("$sequence", envelope.JournalSequence);
        insert.Parameters.AddWithValue("$account", envelope.AccountId);
        insert.Parameters.AddWithValue("$player", envelope.PlayerIndex);
        insert.Parameters.AddWithValue("$json", json);
        insert.Parameters.AddWithValue("$hash", hash);
        insert.Parameters.AddWithValue("$created", envelope.CreatedAtUtc.ToUniversalTime().ToString("O"));
        await insert.ExecuteNonQueryAsync();
        var verify = connection.CreateCommand();
        verify.Transaction = transaction;
        verify.CommandText = "SELECT payload_hash FROM response_preference_outbox WHERE operation_id=$operation;";
        verify.Parameters.AddWithValue("$operation", envelope.OperationId);
        if (!string.Equals((string?)await verify.ExecuteScalarAsync(), hash, StringComparison.Ordinal))
            throw new InvalidOperationException("响应设置 outbox 幂等载荷冲突");
    }

    internal async Task<IReadOnlyList<L12ResponsePreferenceOutboxEntry>> ListPendingResponsePreferencesAsync(
        string? accountId = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,payload_json,payload_hash,attempts FROM response_preference_outbox
            WHERE status='pending' AND ($account IS NULL OR account_id=$account) ORDER BY id;
            """;
        command.Parameters.AddWithValue("$account", (object?)accountId ?? DBNull.Value);
        var rows = new List<L12ResponsePreferenceOutboxEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var json = reader.GetString(1);
            var hash = reader.GetString(2);
            L12ResponsePreferenceOutboxEnvelope? payload = null;
            string? error = null;
            try
            {
                if (PersistenceHash(json) != hash) throw new InvalidDataException("payload hash mismatch");
                payload = JsonSerializer.Deserialize<L12ResponsePreferenceOutboxEnvelope>(json)
                    ?? throw new InvalidDataException("payload empty");
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                error = SafePersistenceError(exception.Message);
            }
            rows.Add(new(reader.GetInt64(0), payload, hash, reader.GetInt32(3), error));
        }
        return rows;
    }

    internal async Task MarkResponsePreferenceAppliedAsync(long id, string payloadHash)
    {
        await using var connection = await OpenWriteConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE response_preference_outbox SET status='applied',applied_utc=$utc,last_error=NULL
            WHERE id=$id AND payload_hash=$hash AND status='pending';
            """;
        command.Parameters.AddWithValue("$utc", _utcNow().ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$hash", payloadHash);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("响应设置 outbox 确认冲突");
    }

    internal async Task RecordResponsePreferenceFailureAsync(long id, string error)
    {
        await using var connection = await OpenWriteConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE response_preference_outbox SET attempts=attempts+1,last_error=$error
            WHERE id=$id AND status='pending';
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$error", SafePersistenceError(error));
        await command.ExecuteNonQueryAsync();
    }

    internal async Task<L12ResponsePreferenceOutboxEnvelope?> LatestPendingResponsePreferenceAsync(string accountId)
        => (await ListPendingResponsePreferencesAsync(accountId)).LastOrDefault(item => item.Payload is not null)?.Payload;
}
