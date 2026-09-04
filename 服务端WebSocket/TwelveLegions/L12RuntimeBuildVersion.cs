using System.Text.RegularExpressions;

namespace TwelveLegions.Server;

internal static partial class L12RuntimeBuildVersion
{
    [GeneratedRegex("^[0-9a-fA-F]{7,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    internal static string Resolve(string? clientReportedVersion)
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
                    // Fall through to the client version when the marker is not readable.
                }
            }
        }

        var reported = clientReportedVersion?.Trim();
        return string.IsNullOrWhiteSpace(reported) ? "dev" : reported[..Math.Min(reported.Length, 100)];
    }

    private static bool IsVersion(string? value)
        => !string.IsNullOrWhiteSpace(value) && CommitPattern().IsMatch(value.Trim());
}
