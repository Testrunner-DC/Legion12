namespace TwelveLegions.Platform.Tests;

internal static class PlatformSchemaTestContract
{
    // Mirrors L12PlatformStore.PlatformStorageSchemaVersion (12 -> 13 moves sessions into
    // account_sessions, 13 -> 14 moves login throttles into login_throttles, 14 -> 15 moves
    // friends and blocks into account_friends/account_blocks).
    internal const int Current = 15;
    internal const int Unsupported = Current + 1;
}