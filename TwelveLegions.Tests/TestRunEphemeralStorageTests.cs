using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TestRunEphemeralStorageTests
{
    [Fact]
    public void ExactTestRunProfile_ClearsOnlyMatchDatabaseFiles()
    {
        var root = CreateRuntime();
        try
        {
            foreach (var name in new[] { "matches.db", "matches.db-wal", "matches.db-shm", "platform.json" })
                File.WriteAllText(Path.Combine(root, name), name);

            Assert.True(L12TestRunStorageProfile.Prepare(root, "ephemeral",
                "https://legion-12.com/testrun"));
            Assert.False(File.Exists(Path.Combine(root, "matches.db")));
            Assert.False(File.Exists(Path.Combine(root, "matches.db-wal")));
            Assert.False(File.Exists(Path.Combine(root, "matches.db-shm")));
            Assert.True(File.Exists(Path.Combine(root, "platform.json")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void MissingProfile_DoesNotClearMatchDatabase()
    {
        var root = CreateRuntime();
        try
        {
            var database = Path.Combine(root, "matches.db");
            File.WriteAllText(database, "keep");
            Assert.False(L12TestRunStorageProfile.Prepare(root, null,
                "https://legion-12.com/testrun"));
            Assert.True(File.Exists(database));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("https://legion-12.com")]
    [InlineData("http://legion-12.com/testrun")]
    [InlineData("https://legion-12.com/other")]
    [InlineData("https://legion-12.com/testrun/other")]
    public void EphemeralProfile_RejectsNonTestRunOrigin(string origin)
    {
        var root = CreateRuntime();
        try
        {
            var database = Path.Combine(root, "matches.db");
            File.WriteAllText(database, "keep");
            Assert.Throws<InvalidOperationException>(() =>
                L12TestRunStorageProfile.Prepare(root, "ephemeral", origin));
            Assert.True(File.Exists(database));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void EphemeralProfile_RejectsProductionRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-{Guid.NewGuid():N}", "legion12-runtime");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<InvalidOperationException>(() => L12TestRunStorageProfile.Prepare(root,
                "ephemeral", "https://legion-12.com/testrun"));
        }
        finally { Directory.Delete(Path.GetDirectoryName(root)!, recursive: true); }
    }

    private static string CreateRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-{Guid.NewGuid():N}",
            "legion12-testrun-runtime");
        Directory.CreateDirectory(root);
        return root;
    }
}
