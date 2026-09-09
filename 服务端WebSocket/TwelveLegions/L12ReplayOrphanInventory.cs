using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

// Explicit, operator-driven inventory only. No cleanup or startup scheduling lives here.
internal sealed record L12ReplayOrphanFinding(string MatchId, bool HasRecoveryOrSettlementReference,
    int ObservedRows = 0, bool RowCountCapped = false, bool HasActiveRoomReference = false,
    bool HasBugMatchReference = false, bool ExternalReferencesChecked = false)
{
    // This is evidence for operator review, never an authorization to purge.
    public string Disposition => HasRecoveryOrSettlementReference || HasActiveRoomReference || HasBugMatchReference
        ? "protected-reference" : ExternalReferencesChecked ? "requires-room-and-evidence-review" : "external-references-unchecked";
}
internal sealed record L12ReplayOrphanPage(string Table, int ScannedMatches, string? NextAfterMatchId,
    bool Complete, IReadOnlyList<L12ReplayOrphanFinding> Findings);

internal static class L12ReplayOrphanInventory
{
    private static readonly IReadOnlySet<string> AllowedTables = new HashSet<string>(StringComparer.Ordinal)
    {
        "match_events", "match_action_requests", "match_action_events", "match_state_checkpoints",
        "match_participants", "match_deck_cards", "match_card_facts", "match_card_fact_summaries",
        "match_card_fact_compactions", "sandbox_recordings"
    };

    // A missing parent is not permission to delete: external Bug evidence and active rooms
    // must be reconciled separately. Match IDs are the only data exposed, never payloads.
    internal static async Task<L12ReplayOrphanPage> ReadPageAsync(string databasePath, string table,
        string? afterMatchId = null, int pageSize = 100, CancellationToken cancellationToken = default,
        IReadOnlyCollection<string>? activeMatchIds = null, L12ReplayEvidenceReferences? evidence = null)
    {
        if (!AllowedTables.Contains(table)) throw new ArgumentException("Unsupported inventory table", nameof(table));
        if (pageSize is < 1 or > 250) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (!File.Exists(databasePath)) throw new FileNotFoundException("Inventory database not found", databasePath);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 2
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        var tables = new HashSet<string>(StringComparer.Ordinal);
        using (var schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText = "SELECT name FROM sqlite_schema WHERE type='table';";
            await using var reader = await schema.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) tables.Add(reader.GetString(0));
        }
        if (!tables.Contains("matches")) throw new InvalidDataException("Missing matches table; inventory stopped");
        if (!tables.Contains(table)) return new(table, 0, afterMatchId, true, []);
        var references = new[] { "ranked_match_runtime", "ranked_settlement_outbox", "ranked_recovery_quarantine" }
            .Where(tables.Contains)
            .Select(name => $"EXISTS(SELECT 1 FROM {name} r WHERE r.match_id=c.match_id)").ToArray();
        var referenceSql = references.Length == 0 ? "0" : string.Join(" OR ", references);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Page across all IDs, not OFFSET or a whole-table orphan count. A clean page
        // still advances the cursor, preventing repeated rescans of the same prefix.
        command.CommandText = $"""
            WITH candidates AS (
                SELECT DISTINCT match_id FROM {table}
                {(afterMatchId is null ? "" : "WHERE match_id > $after")}
                ORDER BY match_id LIMIT $limit
            )
            SELECT c.match_id, EXISTS(SELECT 1 FROM matches m WHERE m.match_id=c.match_id),
                ({referenceSql}) FROM candidates c ORDER BY c.match_id;
            """;
        command.Parameters.AddWithValue("$after", (object?)afterMatchId ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", pageSize);
        var scanned = 0;
        var next = afterMatchId;
        var findings = new List<L12ReplayOrphanFinding>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;
                next = reader.GetString(0);
                if (!reader.GetBoolean(1)) findings.Add(new(next, reader.GetBoolean(2)));
            }
        }
        var active = activeMatchIds?.ToHashSet(StringComparer.Ordinal);
        var bugMatches = evidence?.MatchIds.ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < findings.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var finding = findings[index];
            using var count = connection.CreateCommand();
            count.Transaction = transaction;
            // Count at most 1001 rows for one orphan. A huge orphan must not turn a small
            // metadata inventory into an unbounded payload/table scan.
            count.CommandText = $"SELECT COUNT(*) FROM (SELECT 1 FROM {table} WHERE match_id=$id LIMIT 1001);";
            count.Parameters.AddWithValue("$id", finding.MatchId);
            var observed = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));
            findings[index] = finding with
            {
                ObservedRows = observed, RowCountCapped = observed == 1001,
                HasActiveRoomReference = active?.Contains(finding.MatchId) == true,
                HasBugMatchReference = bugMatches?.Contains(finding.MatchId) == true,
                ExternalReferencesChecked = active is not null && bugMatches is not null,
            };
        }
        return new(table, scanned, next, scanned < pageSize, findings);
    }
}
