using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class ProcessMetricArchiveStartupTests
{
    private const string Commit = "abcdef0123456789";

    [Fact]
    public void OrdinaryRuntimeProducesOnlyItsExactPhysicalMetricsProcessCapability()
    {
        using var fixture = new Fixture();
        var runtime = fixture.Directory("ordinary", "runtime");

        Assert.True(L12ResolvedProcessMetricArchiveRoot.TryResolve(runtime, out var resolved, out var reason));
        Assert.Equal(L12ProcessMetricStartupReason.None, reason);
        Assert.Equal(Path.Combine(runtime, "metrics", "process"), resolved!.PhysicalArchivePath);
        Assert.Equal(nameof(L12ResolvedProcessMetricArchiveRoot), resolved.ToString());
        Assert.DoesNotContain(runtime, JsonSerializer.Serialize(resolved), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(runtime, JsonSerializer.Serialize(resolved,
            new JsonSerializerOptions { IncludeFields = true }), StringComparison.OrdinalIgnoreCase);
        fixture.Success();
    }

    [Fact]
    public async Task OneAbsoluteRuntimeLeafLinkCapturesPhysicalRootAcrossLaterRetarget()
    {
        using var fixture = new Fixture();
        var first = fixture.Directory("physical-a");
        var second = fixture.Directory("physical-b");
        var logical = fixture.Path("release", "publish", "runtime");
        Directory.CreateDirectory(Path.GetDirectoryName(logical)!);
        CreateDirectoryLink(logical, first);

        Assert.True(L12ResolvedProcessMetricArchiveRoot.TryResolve(logical, out var resolved, out var reason));
        Assert.Equal(L12ProcessMetricStartupReason.None, reason);
        var captured = Path.Combine(first, "metrics", "process");
        Assert.Equal(captured, resolved!.PhysicalArchivePath);

        Directory.Delete(logical);
        CreateDirectoryLink(logical, second);
        Assert.Equal(captured, resolved.PhysicalArchivePath);
        Assert.NotEqual(Path.Combine(second, "metrics", "process"), resolved.PhysicalArchivePath);
        await using var archive = new L12ProcessMetricFileArchive(resolved, Commit, Guid.NewGuid());
        Directory.Delete(logical);
        fixture.Success();
    }

    [Fact]
    public void MultiHopRuntimeLinkAndReparsePhysicalChildrenAreRejected()
    {
        using var fixture = new Fixture();
        var target = fixture.Directory("target");
        var intermediate = fixture.Path("intermediate");
        var logical = fixture.Path("release", "runtime");
        Directory.CreateDirectory(Path.GetDirectoryName(logical)!);
        CreateDirectoryLink(intermediate, target);
        CreateDirectoryLink(logical, intermediate);

        Assert.False(L12ResolvedProcessMetricArchiveRoot.TryResolve(logical, out _, out var multiHop));
        Assert.Equal(L12ProcessMetricStartupReason.UnsafePhysicalRuntime, multiHop);

        Directory.Delete(logical);
        Directory.Delete(intermediate);
        var childTarget = fixture.Directory("elsewhere");
        var metrics = Path.Combine(target, "metrics");
        CreateDirectoryLink(metrics, childTarget);
        CreateDirectoryLink(logical, target);
        Assert.False(L12ResolvedProcessMetricArchiveRoot.TryResolve(logical, out _, out var childReason));
        Assert.Equal(L12ProcessMetricStartupReason.UnsafePhysicalRuntime, childReason);
        Directory.Delete(logical);
        Directory.Delete(metrics);
        fixture.Success();
    }

    [Fact]
    public void ParentDirectoryLinkCannotExpandTheSingleRuntimeLeafException()
    {
        using var fixture = new Fixture();
        var physicalRuntime = fixture.Directory("physical-runtime");
        var realParent = fixture.Directory("real-release");
        var runtimeLeaf = Path.Combine(realParent, "runtime");
        var linkedParent = fixture.Path("linked-release");
        CreateDirectoryLink(runtimeLeaf, physicalRuntime);
        CreateDirectoryLink(linkedParent, realParent);

        Assert.False(L12ResolvedProcessMetricArchiveRoot.TryResolve(
            Path.Combine(linkedParent, "runtime"), out _, out var reason));
        Assert.Equal(L12ProcessMetricStartupReason.UnsafePhysicalRuntime, reason);

        Directory.Delete(linkedParent);
        Directory.Delete(runtimeLeaf);
        fixture.Success();
    }

    [Fact]
    public void MissingWrongLeafAndPhysicalArchiveStringBypassAreRejectedWithoutWrites()
    {
        using var fixture = new Fixture();
        var missing = fixture.Path("missing", "runtime");
        var wrongLeaf = fixture.Directory("wrong-name");
        var physical = fixture.Directory("physical-target");
        var physicalArchive = Path.Combine(physical, "metrics", "process");

        Assert.False(L12ResolvedProcessMetricArchiveRoot.TryResolve(missing, out _, out var missingReason));
        Assert.Equal(L12ProcessMetricStartupReason.InvalidRuntimePath, missingReason);
        Assert.False(L12ResolvedProcessMetricArchiveRoot.TryResolve(wrongLeaf, out _, out var wrongReason));
        Assert.Equal(L12ProcessMetricStartupReason.InvalidRuntimePath, wrongReason);
        Assert.Throws<ArgumentException>(() => new L12ProcessMetricFileArchive(
            physicalArchive, Commit, Guid.NewGuid()));
        Assert.False(Directory.Exists(physicalArchive));
        fixture.Success();
    }

    [Fact]
    public async Task StartupOwnsOneCollectorAndStopsCleanlyWithoutStartingIoOnCaller()
    {
        using var fixture = new Fixture();
        var runtimePath = fixture.Directory("runtime");
        var result = L12ProcessMetricArchiveStartup.TryStart(runtimePath, Commit, Guid.NewGuid());

        Assert.True(result.Started);
        Assert.NotNull(result.Runtime);
        Assert.True(await result.Runtime!.StopAsync(TimeSpan.FromSeconds(2)));
        Assert.True(await result.Runtime.StopAsync(TimeSpan.FromMilliseconds(100)));
        fixture.Success();
    }

    [Fact]
    public void StartupRejectsIdentityWithoutEchoingPathOrValue()
    {
        using var fixture = new Fixture();
        var runtimePath = fixture.Directory("runtime");
        const string sentinel = "private-path/invalid-commit";

        var invalidCommit = L12ProcessMetricArchiveStartup.TryStart(runtimePath, sentinel, Guid.NewGuid());
        var invalidInstance = L12ProcessMetricArchiveStartup.TryStart(runtimePath, Commit, Guid.Empty);

        Assert.Equal(L12ProcessMetricStartupReason.InvalidIdentity, invalidCommit.Reason);
        Assert.Equal(L12ProcessMetricStartupReason.InvalidIdentity, invalidInstance.Reason);
        Assert.DoesNotContain(sentinel, invalidCommit.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(runtimePath, invalidCommit.ToString(), StringComparison.OrdinalIgnoreCase);
        fixture.Success();
    }

    [Fact]
    public async Task UnconfirmedCollectorExitNeverDisposesArchive()
    {
        var collector = new FakeCollector(_ => Task.FromResult(false));
        var archive = new CountingArchive();
        var runtime = new L12ProcessMetricRuntime(collector, archive);
        Assert.True(runtime.Start());

        Assert.False(await runtime.StopAsync(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(0, archive.DisposeCalls);
        Assert.Equal(1, collector.StartCalls);
    }

    [Fact]
    public async Task SynchronousDisposePrefixCannotExceedOneTotalStopBudget()
    {
        var collector = new FakeCollector(_ => Task.FromResult(true));
        var archive = new BlockingArchive();
        var runtime = new L12ProcessMetricRuntime(collector, archive);
        Assert.True(runtime.Start());

        var watch = Stopwatch.StartNew();
        var first = await runtime.StopAsync(TimeSpan.FromMilliseconds(100));
        watch.Stop();
        Assert.False(first);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.True(archive.Entered.Wait(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, archive.DisposeCalls);

        archive.Release.Set();
        Assert.True(await runtime.StopAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, archive.DisposeCalls);
    }

    [Fact]
    public async Task SynchronousCollectorStopPrefixCannotBlockCallerOrTriggerEarlyDispose()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var collector = new FakeCollector(_ =>
        {
            entered.Set();
            release.Wait();
            return Task.FromResult(true);
        });
        var archive = new CountingArchive();
        var runtime = new L12ProcessMetricRuntime(collector, archive);
        Assert.True(runtime.Start());

        var stop = runtime.StopAsync(TimeSpan.FromMilliseconds(100));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(1)));
        Assert.False(await stop.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, archive.DisposeCalls);

        release.Set();
        Assert.True(await runtime.StopAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, archive.DisposeCalls);
    }

    [Fact]
    public void ProgramCapturesIdentityOnceStartsAfterServerAndStopsMetricsBeforeServer()
    {
        var source = File.ReadAllText(FindProgramSource());
        var capture = source.IndexOf("var runtimeBuild = L12RuntimeBuildVersion.Capture()", StringComparison.Ordinal);
        var captureCall = source.IndexOf("L12RuntimeBuildVersion.Capture()", StringComparison.Ordinal);
        var instance = source.IndexOf("var processMetricInstance = Guid.NewGuid()", StringComparison.Ordinal);
        var serverStart = source.IndexOf("await server.StartAsync(port)", StringComparison.Ordinal);
        var metricStart = source.IndexOf("L12ProcessMetricArchiveStartup.TryStart(runtimePath,",
            StringComparison.Ordinal);
        var metricStop = source.IndexOf("processMetricRuntime?.StopAsync(", StringComparison.Ordinal);
        var serverStop = source.IndexOf("await server.StopAsync()", StringComparison.Ordinal);

        Assert.True(capture >= 0 && instance > capture);
        Assert.Equal(captureCall,
            source.LastIndexOf("L12RuntimeBuildVersion.Capture()", StringComparison.Ordinal));
        Assert.True(serverStart >= 0 && serverStart < metricStart);
        Assert.True(metricStart < metricStop && metricStop < serverStop);
        Assert.Equal(metricStart, source.LastIndexOf("L12ProcessMetricArchiveStartup.TryStart(runtimePath,",
            StringComparison.Ordinal));
        Assert.DoesNotContain("await using var processMetric", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception.Message", source, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimePath}.\"", source, StringComparison.Ordinal);
    }

    private static string FindProgramSource([CallerFilePath] string callerFile = "")
    {
        var sourceDirectory = Path.GetDirectoryName(callerFile);
        if (!string.IsNullOrEmpty(sourceDirectory))
        {
            var repository = Directory.GetParent(sourceDirectory);
            var candidate = repository is null ? "" : Path.Combine(repository.FullName,
                "服务端WebSocket", "Program.cs");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Program.cs was not found beside the compiled test source");
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        var start = new ProcessStartInfo(
            Environment.GetEnvironmentVariable("L12_TEST_FIXTURE_PWSH") ?? "pwsh.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path "
            + Quote(link) + " -Value " + Quote(target) + " | Out-Null");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("junction fixture unavailable");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("owned junction fixture creation timed out");
        }
        output.GetAwaiter().GetResult();
        error.GetAwaiter().GetResult();
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class FakeCollector(Func<TimeSpan, Task<bool>> stop) : IL12ProcessMetricCollectorLifecycle
    {
        internal int StartCalls;
        public bool Start()
        {
            Interlocked.Increment(ref StartCalls);
            return true;
        }
        public Task<bool> StopAsync(TimeSpan timeout) => stop(timeout);
    }

    private sealed class CountingArchive : IAsyncDisposable
    {
        internal int DisposeCalls;
        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref DisposeCalls);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingArchive : IAsyncDisposable
    {
        internal readonly ManualResetEventSlim Entered = new();
        internal readonly ManualResetEventSlim Release = new();
        internal int DisposeCalls;
        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref DisposeCalls);
            Entered.Set();
            Release.Wait();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private bool _successful;
        internal string Root { get; } = OperatingSystem.IsWindows()
            ? System.IO.Path.Combine("D:\\GPT\\Legion12\\cache\\primary\\test-temp",
                "l12-process-metric-startup-" + Guid.NewGuid().ToString("N"))
            : System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "l12-process-metric-startup-" + Guid.NewGuid().ToString("N"));

        internal Fixture() => System.IO.Directory.CreateDirectory(Root);
        internal string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);
        internal string Directory(params string[] parts)
        {
            var path = Path(parts);
            System.IO.Directory.CreateDirectory(path);
            return path;
        }
        internal void Success() => _successful = true;
        public void Dispose()
        {
            if (_successful && System.IO.Directory.Exists(Root))
                System.IO.Directory.Delete(Root, recursive: true);
        }
    }
}
