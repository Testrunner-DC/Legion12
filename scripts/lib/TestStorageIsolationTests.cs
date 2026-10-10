using Xunit;

namespace TwelveLegions.TestInfrastructure;

public sealed class TestStorageIsolationTests
{
    [Fact]
    public void SupervisedRun_DotnetTemporaryFilesStayWithinOwnedRoot()
    {
        var ownedRoot = Environment.GetEnvironmentVariable("L12_TEST_TEMP_ROOT");
        // Direct developer test runs have no supervisor. Both full CI projects
        // and the Batch/Release wrappers set this value before creating hosts.
        if (string.IsNullOrEmpty(ownedRoot)) return;
        var expected = Path.GetFullPath(ownedRoot).TrimEnd(Path.DirectorySeparatorChar);
        var actual = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        Assert.Equal(expected, actual);
        var probe = Path.GetTempFileName();
        try
        {
            Assert.Equal(expected, Path.GetDirectoryName(Path.GetFullPath(probe)));
            File.WriteAllText(probe, "synthetic isolation probe");
            Assert.Equal("synthetic isolation probe", File.ReadAllText(probe));
        }
        finally { File.Delete(probe); }
    }
}
