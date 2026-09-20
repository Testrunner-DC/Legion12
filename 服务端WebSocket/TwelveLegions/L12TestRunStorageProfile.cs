namespace TwelveLegions.Server;

internal static class L12TestRunStorageProfile
{
    internal const string EnvironmentKey = "L12_TESTRUN_MATCH_STORAGE";
    internal const string EphemeralValue = "ephemeral";

    internal static bool Prepare(string runtimePath, string? profile, string? publicBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(profile)) return false;
        if (!string.Equals(profile, EphemeralValue, StringComparison.Ordinal))
            throw new InvalidOperationException($"{EnvironmentKey} 仅允许 {EphemeralValue}");
        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "testrun.legion-12.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort || uri.AbsolutePath != "/" || uri.Query != ""
            || uri.Fragment != "" || uri.UserInfo != "")
            throw new InvalidOperationException("临时对局存储仅允许用于隔离测试域");

        var runtime = new DirectoryInfo(runtimePath);
        if (!runtime.Exists) throw new DirectoryNotFoundException(runtimePath);
        var physical = runtime.LinkTarget is null
            ? runtime.FullName
            : (runtime.ResolveLinkTarget(returnFinalTarget: true)
                ?? throw new IOException("测试服 runtime 链接无法解析")).FullName;
        physical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physical));
        if (!string.Equals(Path.GetFileName(physical), "legion12-testrun-runtime",
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("临时对局存储拒绝非测试服 runtime");

        foreach (var name in new[] { "matches.db", "matches.db-wal", "matches.db-shm" })
        {
            var path = Path.Combine(runtimePath, name);
            if (File.Exists(path)) File.Delete(path);
        }
        return true;
    }
}
