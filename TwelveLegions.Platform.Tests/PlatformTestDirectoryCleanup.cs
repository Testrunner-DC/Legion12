using Microsoft.Data.Sqlite;

namespace TwelveLegions.Platform.Tests;

internal static class PlatformTestDirectoryCleanup
{
    internal static void Delete(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            SqliteConnection.ClearAllPools();
            return;
        }
        Retry(() =>
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }, OperatingSystem.IsWindows(), Thread.Sleep, SqliteConnection.ClearAllPools);
    }

    internal static void Retry(Action delete, bool windows, Action<int> delay,
        Action? beforeDelete = null)
    {
        ArgumentNullException.ThrowIfNull(delete);
        ArgumentNullException.ThrowIfNull(delay);
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            try
            {
                beforeDelete?.Invoke();
                delete();
                return;
            }
            catch (IOException) when (windows && attempt < 8)
            {
                delay(50 * attempt);
            }
        }
    }
}
