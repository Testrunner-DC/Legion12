namespace TwelveLegions.Platform.Tests;

internal static class PlatformSchemaTestContract
{
    // Mirrors L12PlatformStore.PlatformStorageSchemaVersion (12 -> 13 moves sessions into
    // account_sessions).
    internal const int Current = 13;
    internal const int Unsupported = Current + 1;
}