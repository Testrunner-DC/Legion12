using System.Diagnostics;
using System.Text;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

public sealed class DeploymentFileFenceStoreTests
{
    private const string Active = "1111111111111111111111111111111111111111";
    private const string Target = "2222222222222222222222222222222222222222";

    [Fact]
    public void DurableFenceRestartsClosedAndCannotReissueAnOldPermit()
    {
        using var fixture = new Fixture();
        var old = fixture.Coordinator();
        var owner = fixture.Owner(old);
        Assert.True(old.BeginDrain(owner).Succeeded);
        var sealing = old.TryBeginSeal(owner);
        var sealedResult = old.CompleteSeal(owner, sealing.Snapshot.Epoch, L12DeploymentExternalReadiness.Clear);
        Assert.NotNull(sealedResult.Permit);
        var restart = fixture.Coordinator();
        Assert.Equal(L12DeploymentDrainPhase.Draining, restart.Snapshot().Phase);
        Assert.False(restart.IsCurrentSealPermit(sealedResult.Permit));
        Assert.False(restart.TryAcquireAdmission(out _));
        Assert.True(restart.Cancel(restart.Snapshot().Owner!).Succeeded);
        Assert.False(File.Exists(fixture.Path));
    }

    [Fact]
    public void OlderProcessCannotClearOrOverwriteNewerFence()
    {
        using var fixture = new Fixture();
        var first = fixture.Coordinator();
        Assert.True(first.BeginDrain(fixture.Owner(first)).Succeeded);
        var next = fixture.Coordinator();
        var epoch = next.TryBeginSeal(next.Snapshot().Owner!);
        Assert.True(next.CompleteSeal(next.Snapshot().Owner!, epoch.Snapshot.Epoch,
            L12DeploymentExternalReadiness.Clear).Succeeded);
        var before = File.ReadAllBytes(fixture.Path);
        Assert.False(first.Cancel(first.Snapshot().Owner!).Succeeded);
        Assert.Equal(before, File.ReadAllBytes(fixture.Path));
        Assert.False(first.TryBeginSeal(first.Snapshot().Owner!).Succeeded);
        Assert.False(first.TryAcquireAdmission(out _));
    }

    [Fact]
    public void CompareAndClearChecksOwnerAndTheObservedBytes()
    {
        using var fixture = new Fixture();
        var coordinator = fixture.Coordinator();
        Assert.True(coordinator.BeginDrain(fixture.Owner(coordinator)).Succeeded);
        var store = new L12DeploymentFileFenceStore(fixture.Root);
        var loaded = store.Load().Fence!;
        Assert.False(store.TryClear(Guid.NewGuid().ToString("N"), Target));
        Assert.False(store.TryClear(loaded.OperationId, Active));
        File.AppendAllText(fixture.Path, " "); // Still valid JSON, but a different observed object.
        Assert.False(store.TryClear(loaded.OperationId, Target));
        Assert.True(File.Exists(fixture.Path));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("malformed")]
    [InlineData("large")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("version")]
    [InlineData("open")]
    [InlineData("numeric")]
    [InlineData("directory")]
    public void UnknownFenceStaysClosedAndIsNotRepaired(string scenario)
    {
        using var fixture = new Fixture();
        var coordinator = fixture.Coordinator();
        Assert.True(coordinator.BeginDrain(fixture.Owner(coordinator)).Succeeded);
        var text = File.ReadAllText(fixture.Path);
        switch (scenario)
        {
            case "empty": File.WriteAllText(fixture.Path, ""); break;
            case "malformed": File.WriteAllText(fixture.Path, "{"); break;
            case "large": File.WriteAllText(fixture.Path, new string(' ', 8193)); break;
            case "duplicate": File.WriteAllText(fixture.Path, text.Insert(1, "\"Epoch\":1,")); break;
            case "missing": File.WriteAllText(fixture.Path, text.Replace("\"Epoch\":1,", "")); break;
            case "extra": File.WriteAllText(fixture.Path, text.Insert(1, "\"ignored\":1,")); break;
            case "version": File.WriteAllText(fixture.Path, text.Replace("\"ProtocolVersion\":1", "\"ProtocolVersion\":99")); break;
            case "open": File.WriteAllText(fixture.Path, text.Replace("\"Draining\"", "\"Open\"")); break;
            case "numeric": File.WriteAllText(fixture.Path, text.Replace("\"Draining\"", "1")); break;
            case "directory": File.Delete(fixture.Path); Directory.CreateDirectory(fixture.Path); break;
        }
        var before = scenario == "directory" ? null : File.ReadAllBytes(fixture.Path);
        var restart = fixture.Coordinator();
        Assert.True(restart.Snapshot().FenceUnknown);
        Assert.False(restart.TryAcquireAdmission(out _));
        Assert.False(restart.BeginDrain(fixture.Owner(restart)).Succeeded);
        Assert.False(restart.Cancel(fixture.Owner(restart)).Succeeded);
        if (scenario != "directory")
            Assert.Equal(before, File.ReadAllBytes(fixture.Path));
    }

    [Fact]
    public void MissingParentAndBusyStoreFailClosedWithoutCreatingParents()
    {
        using var fixture = new Fixture();
        var absent = new L12DeploymentFileFenceStore(System.IO.Path.Combine(fixture.Root, "absent"));
        Assert.Equal(L12DeploymentDrainFenceLoadKind.Unknown, absent.Load().Kind);
        Assert.False(Directory.Exists(System.IO.Path.Combine(fixture.Root, "absent")));
        var coordinator = fixture.Coordinator();
        using var held = new FileStream(fixture.Path + ".lock", FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(L12DeploymentDrainTransitionCode.FenceFailure,
            coordinator.BeginDrain(fixture.Owner(coordinator)).Code);
        Assert.False(coordinator.TryAcquireAdmission(out _));
        Assert.False(File.Exists(fixture.Path));
    }

    [Fact]
    public void DirectFenceAndLockReparsePointsAreRejectedWithoutChangingTheirTargets()
    {
        using var fixture = new Fixture();
        var target = System.IO.Path.Combine(fixture.Root, "owned-target");
        Directory.CreateDirectory(target);
        var sentinel = System.IO.Path.Combine(target, "sentinel.txt");
        File.WriteAllText(sentinel, "owned-marker-target");
        CreateDirectoryLink(fixture.Path, target);
        Assert.True((File.GetAttributes(fixture.Path) & FileAttributes.ReparsePoint) != 0);
        var linkedFence = fixture.Coordinator();
        Assert.True(linkedFence.Snapshot().FenceUnknown);
        Assert.False(linkedFence.BeginDrain(fixture.Owner(linkedFence)).Succeeded);
        Assert.Equal("owned-marker-target", File.ReadAllText(sentinel));
        Directory.Delete(fixture.Path); // Only the junction/link, never its target.

        var coordinator = fixture.Coordinator();
        CreateDirectoryLink(fixture.Path + ".lock", target);
        Assert.True((File.GetAttributes(fixture.Path + ".lock") & FileAttributes.ReparsePoint) != 0);
        Assert.Equal(L12DeploymentDrainTransitionCode.FenceFailure,
            coordinator.BeginDrain(fixture.Owner(coordinator)).Code);
        Assert.False(coordinator.TryAcquireAdmission(out _));
        Assert.False(File.Exists(fixture.Path));
        Assert.Equal("owned-marker-target", File.ReadAllText(sentinel));
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        // Windows file symlinks require a system privilege. Junctions exercise
        // the same actual ReparsePoint guard without changing system policy.
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var script = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path "
            + Quote(link) + " -Value " + Quote(target) + " | Out-Null";
        var start = new ProcessStartInfo(
            Environment.GetEnvironmentVariable("L12_TEST_FIXTURE_PWSH") ?? "pwsh.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Owned junction fixture creation timed out");
        }
        Assert.Equal(0, process.ExitCode);
        output.GetAwaiter().GetResult();
        error.GetAwaiter().GetResult();
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "l12-fence-" + Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(Root, L12DeploymentFileFenceStore.FileName);
        internal Fixture() => Directory.CreateDirectory(Root);
        internal L12DeploymentDrainCoordinator Coordinator()
            => new(Guid.NewGuid().ToString("N"), Active, new L12DeploymentFileFenceStore(Root));
        internal L12DeploymentDrainOwner Owner(L12DeploymentDrainCoordinator coordinator)
        {
            Assert.True(L12DeploymentDrainOwner.TryCreate(Guid.NewGuid().ToString("N"), Target,
                coordinator.ProcessInstance, out var owner));
            return owner!;
        }
        public void Dispose()
        {
            // Remove only our two known links before deleting their owned target.
            // Recursive deletion must never walk a junction or encounter it after
            // its target has already disappeared.
            foreach (var link in new[] { Path, Path + ".lock" })
                if (Directory.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0)
                    Directory.Delete(link);
            Directory.Delete(Root, recursive: true);
        }
    }
}
