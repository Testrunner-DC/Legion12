using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PlatformTestDirectoryCleanupTests
{
    [Fact]
    public void RetryOffersUnreachableHandleReleaseOnlyAfterFailureBeforeOriginalBackoff()
    {
        var events = new List<string>();
        var attempt = 0;
        RetryWithRelease(() =>
        {
            events.Add("delete:" + ++attempt);
            if (attempt < 3) throw new IOException("synthetic native lease");
        }, true, milliseconds => events.Add("delay:" + milliseconds),
            () => events.Add("pools"), () => events.Add("unreachable"));
        Assert.Equal(["pools", "delete:1", "unreachable", "delay:50", "pools", "delete:2",
            "unreachable", "delay:100", "pools", "delete:3"], events);
    }

    [Fact]
    public void EighthOriginalFailureNeverGetsAnExtraCollectionOpportunity()
    {
        var attempts = 0;
        var releases = 0;
        var delays = new List<int>();
        var errors = Enumerable.Range(1, 8).Select(attempt => new IOException("synthetic lease " + attempt)).ToArray();
        var actual = Assert.Throws<IOException>(() => RetryWithRelease(() => throw errors[attempts++],
            true, delays.Add, null, () => releases++));
        Assert.Same(errors[7], actual);
        Assert.Equal(8, attempts);
        Assert.Equal(7, releases);
        Assert.Equal(Enumerable.Range(1, 7).Select(attempt => 50 * attempt), delays);
    }

    [Fact]
    public void SharedDeleteCanReleaseAnUnreachableWindowsNativeFileLease()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "cleanup-unreachable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var lease = OpenUnreachableLease(Path.Combine(root, "synthetic.db"));

        PlatformTestDirectoryCleanup.Delete(root);

        Assert.False(Directory.Exists(root));
        Assert.False(lease.IsAlive);
        GC.KeepAlive(lease);
    }

    [Fact]
    public void ReachableWindowsNativeFileLeaseStillThrowsAfterExactlyEightAttempts()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "cleanup-reachable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "synthetic.db");
        using var lease = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
        try
        {
            var attempts = 0;
            var delays = new List<int>();
            Assert.Throws<IOException>(() => PlatformTestDirectoryCleanup.Retry(() =>
            {
                attempts++;
                Directory.Delete(root, recursive: true);
            }, windows: true, delays.Add));
            Assert.Equal(8, attempts);
            Assert.Equal(Enumerable.Range(1, 7).Select(attempt => 50 * attempt), delays);
            Assert.True(File.Exists(path));
            GC.KeepAlive(lease); // A legitimate owner must never be closed by a collection.
        }
        finally
        {
            lease.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NonWindowsOrNonIoFailureNeverRequestsFinalizationOrRetry(bool windows, bool io)
    {
        Exception error = io ? new IOException("synthetic IO") : new UnauthorizedAccessException("synthetic access");
        var attempts = 0;
        var releases = 0;
        var delays = 0;
        var actual = Assert.ThrowsAny<Exception>(() => RetryWithRelease(() =>
        {
            attempts++; throw error;
        }, windows, _ => delays++, null, () => releases++));
        Assert.Same(error, actual);
        Assert.Equal(1, attempts);
        Assert.Equal(0, releases);
        Assert.Equal(0, delays);
    }

    [Fact]
    public void ReleaseCallbackFailureIsNotSwallowedAndDoesNotRestartDeletion()
    {
        var releaseError = new InvalidOperationException("synthetic release failure");
        var attempts = 0;
        var actual = Assert.Throws<InvalidOperationException>(() => RetryWithRelease(() =>
        {
            attempts++; throw new IOException("synthetic lock");
        }, true, _ => Assert.Fail("No delay after a release failure"), null, () => throw releaseError));
        Assert.Same(releaseError, actual);
        Assert.Equal(1, attempts);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference OpenUnreachableLease(string path)
    {
        var lease = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
        return new WeakReference(lease);
    }

    // The old real four-argument Retry remains the baseline. Once the release
    // callback exists, invoke that real entry; never catch a new-entry failure and fall back.
    private static void RetryWithRelease(Action delete, bool windows, Action<int> delay,
        Action? beforeDelete, Action release)
    {
        var method = typeof(PlatformTestDirectoryCleanup).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "Retry");
        object?[] arguments = method.GetParameters().Length == 4
            ? [delete, windows, delay, beforeDelete]
            : [delete, windows, delay, beforeDelete, release];
        try { method.Invoke(null, arguments); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    [Fact]
    public void SharedRetryOrdersPoolReleaseBeforeEveryAttemptAndOriginalBackoffBetweenFailures()
    {
        var events = new List<string>();
        var attempt = 0;

        PlatformTestDirectoryCleanup.Retry(() =>
        {
            events.Add("delete:" + ++attempt);
            if (attempt < 3) throw new IOException("synthetic shared cleanup lock");
        }, windows: true, milliseconds => events.Add("delay:" + milliseconds),
            () => events.Add("release"));

        Assert.Equal(["release", "delete:1", "delay:50", "release", "delete:2", "delay:100",
            "release", "delete:3"], events);
    }

    [Fact]
    public void SharedRetryPropagatesOriginalEighthErrorWithoutNinthReleaseOrExtraDelay()
    {
        var events = new List<string>();
        var errors = Enumerable.Range(1, 8).Select(attempt => new IOException("synthetic lock " + attempt)).ToArray();
        var attempt = 0;

        var actual = Assert.Throws<IOException>(() => PlatformTestDirectoryCleanup.Retry(() =>
        {
            events.Add("delete:" + ++attempt);
            throw errors[attempt - 1];
        }, windows: true, milliseconds => events.Add("delay:" + milliseconds),
            () => events.Add("release")));

        Assert.Same(errors[7], actual);
        Assert.Equal(Enumerable.Range(1, 8).SelectMany(index => index < 8
            ? new[] { "release", "delete:" + index, "delay:" + 50 * index }
            : ["release", "delete:" + index]), events);
    }

    [Fact]
    public void SharedDeleteMissingDirectoryReturnsWithoutCreatingAnyDirectoryOrFile()
    {
        var missingParent = Path.Combine(Path.GetTempPath(), "l12-cleanup-missing-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(missingParent, "never-created");
        Assert.False(Directory.Exists(missingParent));

        PlatformTestDirectoryCleanup.Delete(directory);
        PlatformTestDirectoryCleanup.Delete(directory);

        Assert.False(Directory.Exists(missingParent));
        Assert.False(Directory.Exists(directory));
        Assert.False(File.Exists(directory));
    }
}
