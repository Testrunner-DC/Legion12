using System.Runtime.CompilerServices;
using Xunit;

namespace TwelveLegions.Platform.Tests;

internal static class PlatformTestSource
{
    // Compiler-bound source location works when governed build outputs are outside
    // the checkout. Output ancestry remains a fallback; missing source still fails.
    internal static string Root([CallerFilePath] string callerPath = "")
    {
        foreach (var root in new[] { Path.GetDirectoryName(callerPath), AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "服务端WebSocket", "Program.cs"))
                    && File.Exists(Path.Combine(directory.FullName, "TwelveLegions.Platform.Tests", "TwelveLegions.Platform.Tests.csproj")))
                    return directory.FullName;
        }
        throw new DirectoryNotFoundException("Platform source checks require the actual source checkout.");
    }

    internal static string FilePath(params string[] segments)
    {
        var path = Path.Combine([Root(), .. segments]);
        return File.Exists(path) ? path : throw new FileNotFoundException("Source check file is missing.", path);
    }
}

public sealed class PlatformTestSourceLocatorTests
{
    [Fact]
    public void SourceLookupUsesExistingCheckoutEvenWithExternalBuildOutput()
    {
        var path = PlatformTestSource.FilePath("服务端WebSocket", "Program.cs");
        Assert.StartsWith(PlatformTestSource.Root(), path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("L12PlatformStore", File.ReadAllText(path));
    }

    [Fact]
    public void MissingSourceIsNotReplacedWithAnOutputOrSyntheticFile()
        => Assert.Throws<FileNotFoundException>(() => PlatformTestSource.FilePath("__missing-source-contract__.cs"));
}
