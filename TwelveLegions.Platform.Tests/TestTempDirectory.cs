namespace TwelveLegions.Platform.Tests;

// A test that stops a live server still has handles closing in the background: SQLite pools, the
// recorder and the HTTP listener release them on their own schedule. Deleting the temporary tree
// must therefore be retried, otherwise an otherwise-passing test fails in its finally block under
// load (observed on AdminPasswordWorkIsolationTests: Directory.Delete raced the recorder suspect).
internal static class TestTempDirectory
{
    internal static void Delete(string path, int attempts = 10, int delayMilliseconds = 100)
    {
        if (!Directory.Exists(path)) return;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(path, true);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(delayMilliseconds);
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                Thread.Sleep(delayMilliseconds);
            }
        }
    }
}