using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace TwelveLegions.Server;

internal sealed record L12SyntheticStorageMeasurement(
    int Connections,
    int SelectStatements,
    int InsertStatements,
    int UpdateStatements,
    int DeleteStatements,
    int OtherStatements,
    int DeckDomainStatements,
    int AuditDomainStatements,
    int PlatformDomainStatements,
    int SchemaStatements,
    int OtherDomainStatements,
    long InsertRows,
    long UpdateRows,
    long DeleteRows,
    long InclusiveAffectedRows,
    long TransactionNanoseconds,
    long ProfiledSqlNanoseconds,
    long SqlitePageWrites,
    long SqlitePagePayloadBytes,
    long MirrorBytes,
    int InstrumentationErrors);

internal sealed class L12SyntheticStorageMeasurementScope : IDisposable
{
    private readonly L12StorageMeasurementSession _session;
    private bool _completed;

    internal L12SyntheticStorageMeasurementScope(L12StorageMeasurementSession session)
        => _session = session;

    internal L12SyntheticStorageMeasurement Complete()
    {
        if (_completed) throw new InvalidOperationException("Synthetic storage measurement scope is already complete");
        _completed = true;
        return L12PlatformStore.CompleteSyntheticStorageMeasurement(_session);
    }

    public void Dispose()
    {
        if (!_completed)
        {
            _completed = true;
            L12PlatformStore.AbandonSyntheticStorageMeasurement(_session);
        }
    }
}

internal sealed class L12StorageMeasurementSession
{
    internal readonly List<L12StorageConnectionMeasurement> Connections = [];
    internal long MirrorBytes;
}

internal sealed class L12StorageConnectionMeasurement
{
    internal required sqlite3 Handle { get; init; }
    internal required int PageSize { get; init; }
    internal long TransactionStartedTimestamp;
    internal long TransactionNanoseconds;
    internal long ProfiledSqlNanoseconds;
    internal int SelectStatements;
    internal int InsertStatements;
    internal int UpdateStatements;
    internal int DeleteStatements;
    internal int OtherStatements;
    internal int DeckDomainStatements;
    internal int AuditDomainStatements;
    internal int PlatformDomainStatements;
    internal int SchemaStatements;
    internal int OtherDomainStatements;
    internal long InsertRows;
    internal long UpdateRows;
    internal long DeleteRows;
    internal long InclusiveAffectedRows;
    internal long SqlitePageWrites;
    internal int InstrumentationErrors;
}

public sealed partial class L12PlatformStore
{
    private static readonly AsyncLocal<L12StorageMeasurementSession?> SyntheticStorageMeasurement = new();
    private static readonly strdelegate_profile SyntheticStorageProfileCallback = OnSyntheticStorageProfile;

    internal static L12SyntheticStorageMeasurementScope BeginSyntheticStorageMeasurement()
    {
        if (SyntheticStorageMeasurement.Value is not null)
            throw new InvalidOperationException("Synthetic storage measurement scopes cannot be nested");
        var session = new L12StorageMeasurementSession();
        SyntheticStorageMeasurement.Value = session;
        return new L12SyntheticStorageMeasurementScope(session);
    }

    internal static L12SyntheticStorageMeasurement CompleteSyntheticStorageMeasurement(
        L12StorageMeasurementSession session)
    {
        if (!ReferenceEquals(SyntheticStorageMeasurement.Value, session))
            throw new InvalidOperationException("Synthetic storage measurement scope ownership changed");
        SyntheticStorageMeasurement.Value = null;
        return SummarizeSyntheticStorageMeasurement(session);
    }

    internal static void AbandonSyntheticStorageMeasurement(L12StorageMeasurementSession session)
    {
        if (ReferenceEquals(SyntheticStorageMeasurement.Value, session))
            SyntheticStorageMeasurement.Value = null;
    }

    private static void AttachSyntheticStorageMeasurement(SqliteConnection connection)
    {
        var session = SyntheticStorageMeasurement.Value;
        if (session is null) return;

        int pageSize;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA page_size;";
            pageSize = Convert.ToInt32(command.ExecuteScalar());
        }

        var handle = connection.Handle
            ?? throw new InvalidOperationException("Opened SQLite connection has no native handle");
        var state = new L12StorageConnectionMeasurement
        {
            Handle = handle,
            PageSize = pageSize,
        };
        session.Connections.Add(state);
        raw.sqlite3_profile(handle, SyntheticStorageProfileCallback, state);
    }

    private static void RecordSyntheticMirrorWrite(string json)
    {
        var session = SyntheticStorageMeasurement.Value;
        if (session is not null) session.MirrorBytes += Encoding.UTF8.GetByteCount(json);
    }

    private static void OnSyntheticStorageProfile(object context, string sql, long elapsedNanoseconds)
    {
        if (context is not L12StorageConnectionMeasurement state) return;
        try { OnSyntheticStorageProfileCore(state, sql, elapsedNanoseconds); }
        catch { state.InstrumentationErrors++; }
    }

    private static void OnSyntheticStorageProfileCore(L12StorageConnectionMeasurement state, string sql,
        long elapsedNanoseconds)
    {
        state.ProfiledSqlNanoseconds += Math.Max(0, elapsedNanoseconds);
        if (string.IsNullOrWhiteSpace(sql))
        {
            state.OtherStatements++;
            state.OtherDomainStatements++;
            return;
        }
        var verb = StorageSqlVerb(sql);
        switch (verb)
        {
            case "SELECT":
                state.SelectStatements++;
                break;
            case "INSERT":
                state.InsertStatements++;
                state.InsertRows += raw.sqlite3_changes(state.Handle);
                break;
            case "UPDATE":
                state.UpdateStatements++;
                state.UpdateRows += raw.sqlite3_changes(state.Handle);
                break;
            case "DELETE":
                state.DeleteStatements++;
                state.DeleteRows += raw.sqlite3_changes(state.Handle);
                break;
            default:
                state.OtherStatements++;
                break;
        }

        CountStorageDomain(state, verb, sql);
        if (verb == "BEGIN") state.TransactionStartedTimestamp = Stopwatch.GetTimestamp();
        if (verb is "COMMIT" or "ROLLBACK") CaptureSyntheticConnectionTotals(state);
    }

    private static string StorageSqlVerb(string sql)
    {
        var span = sql.AsSpan().TrimStart();
        var length = 0;
        while (length < span.Length && char.IsLetter(span[length])) length++;
        return length == 0 ? string.Empty : span[..length].ToString().ToUpperInvariant();
    }

    private static void CountStorageDomain(L12StorageConnectionMeasurement state, string verb, string sql)
    {
        if (verb is "PRAGMA" or "CREATE" or "ALTER" or "DROP")
        {
            state.SchemaStatements++;
            return;
        }

        if (sql.Contains("account_decks", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("deck_payloads", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("published_deck", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("tournament_deck_refs", StringComparison.OrdinalIgnoreCase))
            state.DeckDomainStatements++;
        else if (sql.Contains("admin_audit", StringComparison.OrdinalIgnoreCase)
                 || sql.Contains("audit_", StringComparison.OrdinalIgnoreCase))
            state.AuditDomainStatements++;
        else if (sql.Contains("platform_state", StringComparison.OrdinalIgnoreCase)
                 || sql.Contains("storage_meta", StringComparison.OrdinalIgnoreCase)
                 || sql.Contains("season_finalization", StringComparison.OrdinalIgnoreCase)
                 || sql.Contains("platform_season_identity", StringComparison.OrdinalIgnoreCase))
            state.PlatformDomainStatements++;
        else
            state.OtherDomainStatements++;
    }

    private static void CaptureSyntheticConnectionTotals(L12StorageConnectionMeasurement state)
    {
        if (state.TransactionStartedTimestamp != 0)
        {
            state.TransactionNanoseconds += (long)(Stopwatch.GetElapsedTime(
                state.TransactionStartedTimestamp).TotalMilliseconds * 1_000_000d);
            state.TransactionStartedTimestamp = 0;
        }
        state.InclusiveAffectedRows = raw.sqlite3_total_changes(state.Handle);
        var result = raw.sqlite3_db_status(state.Handle, raw.SQLITE_DBSTATUS_CACHE_WRITE,
            out var pageWrites, out _, 0);
        if (result == raw.SQLITE_OK) state.SqlitePageWrites = pageWrites;
    }

    private static L12SyntheticStorageMeasurement SummarizeSyntheticStorageMeasurement(
        L12StorageMeasurementSession session)
        => new(
            session.Connections.Count,
            session.Connections.Sum(row => row.SelectStatements),
            session.Connections.Sum(row => row.InsertStatements),
            session.Connections.Sum(row => row.UpdateStatements),
            session.Connections.Sum(row => row.DeleteStatements),
            session.Connections.Sum(row => row.OtherStatements),
            session.Connections.Sum(row => row.DeckDomainStatements),
            session.Connections.Sum(row => row.AuditDomainStatements),
            session.Connections.Sum(row => row.PlatformDomainStatements),
            session.Connections.Sum(row => row.SchemaStatements),
            session.Connections.Sum(row => row.OtherDomainStatements),
            session.Connections.Sum(row => row.InsertRows),
            session.Connections.Sum(row => row.UpdateRows),
            session.Connections.Sum(row => row.DeleteRows),
            session.Connections.Sum(row => row.InclusiveAffectedRows),
            session.Connections.Sum(row => row.TransactionNanoseconds),
            session.Connections.Sum(row => row.ProfiledSqlNanoseconds),
            session.Connections.Sum(row => row.SqlitePageWrites),
            session.Connections.Sum(row => row.SqlitePageWrites * row.PageSize),
            session.MirrorBytes,
            session.Connections.Sum(row => row.InstrumentationErrors));
}
