using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    internal async Task<IReadOnlyCollection<string>> ReadAuditRetentionHoldsAsync(CancellationToken token)
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString) { Mode = SqliteOpenMode.ReadOnly };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(token);
        using var query = connection.CreateCommand();
        // Only identifiers and a bounded result. Too many pending records stops archival rather
        // than treating an incomplete protection list as authoritative.
        query.CommandText = """
            SELECT match_id FROM ranked_match_runtime r WHERE status<>'completed'
                OR NOT EXISTS(SELECT 1 FROM ranked_settlement_outbox o WHERE o.match_id=r.match_id AND o.status='applied')
            UNION ALL SELECT match_id FROM ranked_settlement_outbox WHERE status<>'applied'
            UNION ALL SELECT match_id FROM ranked_recovery_quarantine LIMIT 1001;
            """;
        var ids = new List<string>();
        await using var reader = await query.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) ids.Add(reader.GetString(0));
        if (ids.Count > 1000) throw new InvalidDataException("待恢复/对账记录过多，审计淘汰已暂停");
        return ids;
    }
}
