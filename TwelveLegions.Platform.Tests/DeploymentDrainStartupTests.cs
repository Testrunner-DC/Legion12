using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DeploymentDrainStartupTests
{
    private const string Commit = "1234567890123456789012345678901234567890";
    private const string Process = "11111111111111111111111111111111";

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("true", true)]
    [InlineData(" TRUE ", true)]
    public void StartupFlagIsExplicitAndDefaultsOff(string? value, bool expected)
        => Assert.Equal(expected, L12DeploymentDrainStartup.Parse(value));

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("on")]
    [InlineData("true,false")]
    [InlineData("private-secret-value")]
    public void InvalidFlagIsRejectedWithoutEchoingInput(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => L12DeploymentDrainStartup.Parse(value));
        Assert.Equal($"{L12DeploymentDrainStartup.EnvironmentKey} only accepts true or false", error.Message);
    }

    [Fact]
    public void DisabledStartupDoesNotCreateAControlOrStorageFile()
    {
        using var directory = new StartupDirectory();
        Assert.Null(L12DeploymentDrainStartup.Create(false, directory.Path,
            new("dev", "l12-engine/dev")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Theory]
    [InlineData("dev", "l12-engine/dev")]
    [InlineData("1234567", "l12-engine/1234567")]
    [InlineData(Commit, "l12-engine/another-commit")]
    public void EnabledStartupRequiresAnExactPackagedIdentity(string release, string engine)
    {
        using var directory = new StartupDirectory();
        Assert.Throws<InvalidOperationException>(() => L12DeploymentDrainStartup.Create(true,
            directory.Path, new(release, engine), Process));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public void EnabledStartupSharesTheExactProcessAndBuildIdentity()
    {
        using var directory = new StartupDirectory();
        var coordinator = L12DeploymentDrainStartup.Create(true, directory.Path,
            new(Commit, $"l12-engine/{Commit}"), Process)!;
        var snapshot = coordinator.Snapshot();
        Assert.Equal(L12DeploymentDrainPhase.Open, snapshot.Phase);
        Assert.Equal(Commit, snapshot.ActiveCommit);
        Assert.Equal(Process, snapshot.ProcessInstance);
        Assert.False(snapshot.StopConsumed);
        Assert.False(snapshot.FenceUnknown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AClosedOrUnknownFenceCannotBeBypassedByDisablingTheFeature(bool corrupt)
    {
        using var directory = new StartupDirectory();
        if (corrupt)
            File.WriteAllText(System.IO.Path.Combine(directory.Path, L12DeploymentFileFenceStore.FileName), "not-json");
        else
        {
            var coordinator = L12DeploymentDrainStartup.Create(true, directory.Path,
                new(Commit, $"l12-engine/{Commit}"), Process)!;
            Assert.True(L12DeploymentDrainOwner.TryCreate("22222222222222222222222222222222",
                "3333333333333333333333333333333333333333", Process, out var owner));
            Assert.True(coordinator.BeginDrain(owner!).Succeeded);
        }
        var fencePath = System.IO.Path.Combine(directory.Path, L12DeploymentFileFenceStore.FileName);
        var before = File.ReadAllBytes(fencePath);
        Assert.Throws<InvalidOperationException>(() => L12DeploymentDrainStartup.Create(false,
            directory.Path, new("dev", "l12-engine/dev")));
        Assert.Equal(before, File.ReadAllBytes(fencePath));
    }

    [Fact]
    public void ANewStartupRetainsDrainingAndDoesNotTrustAnOldProcessPermit()
    {
        using var directory = new StartupDirectory();
        var first = L12DeploymentDrainStartup.Create(true, directory.Path,
            new(Commit, $"l12-engine/{Commit}"), Process)!;
        Assert.True(L12DeploymentDrainOwner.TryCreate("22222222222222222222222222222222",
            "3333333333333333333333333333333333333333", Process, out var owner));
        Assert.True(first.BeginDrain(owner!).Succeeded);
        var sealing = first.TryBeginSeal(owner!);
        var permit = first.CompleteSeal(owner!, sealing.Snapshot.Epoch,
            L12DeploymentExternalReadiness.Clear).Permit!;
        var second = L12DeploymentDrainStartup.Create(true, directory.Path,
            new(Commit, $"l12-engine/{Commit}"), "44444444444444444444444444444444")!;
        Assert.Equal(L12DeploymentDrainPhase.Draining, second.Snapshot().Phase);
        Assert.False(second.IsCurrentSealPermit(permit));
        Assert.False(second.Snapshot().StopConsumed);
        Assert.False(second.TryAcquireAdmission(out _));
        Assert.True(File.Exists(System.IO.Path.Combine(directory.Path, L12DeploymentFileFenceStore.FileName)));
    }

    private sealed class StartupDirectory : IDisposable
    {
        private readonly string _base = System.IO.Path.GetFullPath("D:/GPT/Legion12/artifacts/deployment-drain-startup-tests");
        internal string Path { get; }
        internal StartupDirectory()
        {
            Path = System.IO.Path.Combine(_base, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            var directory = new DirectoryInfo(System.IO.Path.GetFullPath(Path));
            if (directory.Parent?.FullName.TrimEnd(System.IO.Path.DirectorySeparatorChar)
                != _base.TrimEnd(System.IO.Path.DirectorySeparatorChar)
                || !Guid.TryParseExact(directory.Name, "N", out _))
                throw new InvalidOperationException("Unexpected startup fixture cleanup target");
            directory.Delete(recursive: true);
        }
    }
}
