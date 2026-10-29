using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // A comparison view of this connection's current transaction, not a retained
    // authority/cache. Read only after stale-row cleanup; update after each write.
    private sealed class DeckSnapshotMetadata
    {
        private readonly Dictionary<(string Primary, string? Secondary), object?[]> rows = new();

        internal static DeckSnapshotMetadata Read(SqliteConnection connection, SqliteTransaction transaction,
            string sql, int keyColumns)
        {
            var result = new DeckSnapshotMetadata();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = (reader.GetString(0), keyColumns == 2 ? reader.GetString(1) : null);
                var values = new object?[reader.FieldCount - keyColumns];
                for (var index = 0; index < values.Length; index++)
                    values[index] = reader.IsDBNull(index + keyColumns) ? null : reader.GetValue(index + keyColumns);
                result.rows.Add(key, values);
            }
            return result;
        }

        private static object? Comparable(object? value)
            => value is null or DBNull ? null : value is int number ? (long)number : value;

        internal bool Matches(string primary, string? secondary, object?[] expected)
        {
            if (!rows.TryGetValue((primary, secondary), out var actual) || actual.Length != expected.Length)
                return false;
            for (var index = 0; index < actual.Length; index++)
                if (!Equals(actual[index], Comparable(expected[index]))) return false;
            return true;
        }

        internal void Written(string primary, string? secondary, object?[] values)
            => rows[(primary, secondary)] = values.Select(Comparable).ToArray();
    }
}
