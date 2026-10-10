namespace TwelveLegions.Server;

internal static class L12PrivateDeckPersistenceStartup
{
    internal const string EnvironmentKey = "L12_PRIVATE_DECK_OBJECT_PERSISTENCE";

    // Captured only by Program before storage initialization; this is not a live config source.
    internal static bool Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim();
        if (string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)) return false;
        throw new InvalidOperationException($"{EnvironmentKey} 仅允许 true 或 false；修改后需重启服务。");
    }
}

public sealed record L12PrivateDeckPersistenceStatusView(
    bool ConfiguredEnabled,
    string EffectiveMode,
    string StorageMode,
    bool Writable,
    bool FallbackMirrorHealthy,
    bool RestartRequiredForChange);
