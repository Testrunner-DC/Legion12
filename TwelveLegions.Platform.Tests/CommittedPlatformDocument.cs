using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace GrandUMI.Tests;

// The core snapshot no longer carries the sessions: since schema 13 they live in
// account_sessions. A few legacy-JSON fixtures still need one complete platform document to
// write back as platform.json, so this joins the two independent tables back together. The
// table is the only source; the compatibility mirror is stale between full writes.
internal static class CommittedPlatformDocument
{
    internal static JsonObject CommittedSnapshotWithSessions(string path)
    {
        using var connection = new SqliteConnection(
            $"Data Source={Path.ChangeExtension(path, ".db")};Mode=ReadOnly;Pooling=False");
        connection.Open();
        var document = JsonNode.Parse(CommittedSnapshot(connection))!.AsObject();
        document["Sessions"] = CommittedSessions(connection);
        return document;
    }

    private static string CommittedSnapshot(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT snapshot_json FROM platform_state WHERE singleton_id=1;";
        return Convert.ToString(command.ExecuteScalar())!;
    }

    private static JsonArray CommittedSessions(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,token_hash,account_id,created_utc,expires_utc,revoked_utc,auth_strength,permission_version
            FROM account_sessions ORDER BY id;
            """;
        using var reader = command.ExecuteReader();
        var sessions = new JsonArray();
        while (reader.Read())
        {
            sessions.Add(new JsonObject
            {
                ["Id"] = reader.GetString(0),
                ["TokenHash"] = reader.GetString(1),
                ["AccountId"] = reader.GetString(2),
                ["CreatedAt"] = reader.GetString(3),
                ["ExpiresAt"] = reader.GetString(4),
                ["RevokedAt"] = reader.IsDBNull(5) ? null : reader.GetString(5),
                ["AuthStrength"] = reader.GetString(6),
                ["PermissionVersion"] = reader.GetInt32(7),
            });
        }
        return sessions;
    }
}
