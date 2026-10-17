using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PlatformTestDirectoryCleanupTests
{
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
