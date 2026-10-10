namespace TwelveLegions.Server;

internal static class L12DeploymentDrainStartup
{
    internal const string EnvironmentKey = "L12_DEPLOYMENT_DRAIN";

    // A startup-only feature switch, never a mutable operational setting. Invalid
    // input is rejected before opening storage. Existing closed fences cannot be
    // bypassed by disabling the feature or restarting a process.
    internal static bool Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(value.Trim(), "false", StringComparison.OrdinalIgnoreCase)) return false;
        throw new InvalidOperationException($"{EnvironmentKey} only accepts true or false");
    }

    internal static L12DeploymentDrainCoordinator? Create(bool enabled, string runtimeDirectory,
        L12RuntimeBuildVersion.BuildIdentity build, string? processInstance = null)
    {
        var store = new L12DeploymentFileFenceStore(runtimeDirectory);
        if (!enabled)
        {
            if (store.Load().Kind != L12DeploymentDrainFenceLoadKind.Missing)
                throw new InvalidOperationException("A persisted or unknown deployment fence requires the barrier");
            return null;
        }

        if (!L12DeploymentDrainCoordinator.IsCommit(build.ServerRelease)
            || !string.Equals(build.EngineVersion, $"l12-engine/{build.ServerRelease}", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Deployment barrier requires the exact packaged build identity");
        return new L12DeploymentDrainCoordinator(processInstance ?? Guid.NewGuid().ToString("N"),
            build.ServerRelease, store);
    }
}
