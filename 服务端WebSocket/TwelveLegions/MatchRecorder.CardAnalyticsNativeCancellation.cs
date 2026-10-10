using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

// Lifetime is nested inside an open, nonpooled connection. Registration.Dispose waits
// for any cross-thread callback before the native handle can be closed or reused.
internal sealed class CardAnalyticsNativeCancellation : IDisposable
{
    private readonly SQLitePCL.sqlite3 _handle;
    private readonly CancellationTokenRegistration _registration;

    internal CardAnalyticsNativeCancellation(SqliteConnection connection, CancellationToken token)
    {
        _handle = connection.Handle ?? throw new InvalidOperationException("SQLite connection is not open");
        SQLitePCL.raw.sqlite3_progress_handler(_handle, 1000,
            state => ((CancellationToken)state).IsCancellationRequested ? 1 : 0, token);
        _registration = token.Register(static state =>
            SQLitePCL.raw.sqlite3_interrupt((SQLitePCL.sqlite3)state!), _handle);
    }

    public void Dispose()
    {
        _registration.Dispose();
        SQLitePCL.raw.sqlite3_progress_handler(_handle, 0, null, null);
    }
}
