using System.Reflection;
using System.Text.RegularExpressions;

namespace TwelveLegions.Server;

internal static partial class L12RuntimeBuildVersion
{
    [GeneratedRegex("^[0-9a-fA-F]{7,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    internal sealed record BuildIdentity(string ServerRelease, string EngineVersion);

    internal static string NormalizeClient(string? clientReportedVersion)
    {
        var reported = clientReportedVersion?.Trim();
        return string.IsNullOrWhiteSpace(reported)
            ? "unknown-client"
            : reported[..Math.Min(reported.Length, 100)];
    }

    internal static BuildIdentity Capture()
    {
        var informationalCommit = ResolveInformationalCommit();
        var serverRelease = ResolveServerRelease(informationalCommit);
        var engineIdentity = informationalCommit ?? (IsVersion(serverRelease) ? serverRelease : "dev");
        return new BuildIdentity(serverRelease, $"l12-engine/{engineIdentity}");
    }

    private static string ResolveServerRelease(string? informationalCommit)
    {
        var configured = Environment.GetEnvironmentVariable("L12_RELEASE_VERSION");
        if (IsVersion(configured)) return configured!.Trim();

        foreach (var root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(root);
            for (var depth = 0; directory is not null && depth < 7; depth++, directory = directory.Parent)
            {
                var marker = Path.Combine(directory.FullName, ".deployment-commit");
                if (!File.Exists(marker)) continue;
                try
                {
                    var value = File.ReadAllText(marker).Trim();
                    if (IsVersion(value)) return value;
                }
                catch (IOException)
                {
                    // A transient deployment marker read must never block Bug submission.
                }
                catch (UnauthorizedAccessException)
                {
                    // Fall through to the assembly identity when the marker is not readable.
                }
            }
        }

        return informationalCommit ?? "dev";
    }

    private static string? ResolveInformationalCommit()
    {
        var informational = typeof(L12GameEngine).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Trim();
        if (string.IsNullOrWhiteSpace(informational)) return null;
        return informational.Split(['+', '.', '-'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(candidate => CommitPattern().IsMatch(candidate));
    }

    private static bool IsVersion(string? value)
        => !string.IsNullOrWhiteSpace(value) && CommitPattern().IsMatch(value.Trim());
}
