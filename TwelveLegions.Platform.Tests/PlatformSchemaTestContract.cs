namespace TwelveLegions.Platform.Tests;

internal static class PlatformSchemaTestContract
{
    // Mirrors L12PlatformStore.PlatformStorageSchemaVersion (12 -> 13 moves sessions into
    // account_sessions, 13 -> 14 moves login throttles into login_throttles).
    internal const int Current = 14;
    internal const int Unsupported = Current + 1;
}