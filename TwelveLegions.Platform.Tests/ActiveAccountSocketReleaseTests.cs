using System.Collections.Concurrent;
using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class ActiveAccountSocketReleaseTests
{
    private static bool Release(ConcurrentDictionary<string, Guid> owners, string account, Guid expected)
        => Assert.IsType<bool>(typeof(L12WebSocketServer)
            .GetMethod("TryReleaseActiveAccountSocket", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [owners, account, expected]));

    [Fact]
    public void CurrentOwnerCanReleaseAndRepeatedCleanupIsANoop()
    {
        var owners = new ConcurrentDictionary<string, Guid>(StringComparer.Ordinal);
        var session = Guid.NewGuid();
        owners["synthetic-account"] = session;
        Assert.True(Release(owners, "synthetic-account", session));
        Assert.False(Release(owners, "synthetic-account", session));
        Assert.Empty(owners);
    }

    [Fact]
    public async Task AStaleCleanupCapturedBeforeNewClaimCannotDeleteTheNewOwner()
    {
        var owners = new ConcurrentDictionary<string, Guid>(StringComparer.Ordinal);
        var oldSession = Guid.NewGuid();
        var newSession = Guid.NewGuid();
        owners["synthetic-account"] = oldSession;
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = Task.Run(async () =>
        {
            Assert.Equal(oldSession, owners["synthetic-account"]);
            captured.SetResult();
            await resume.Task;
            return Release(owners, "synthetic-account", oldSession);
        });
        await captured.Task.WaitAsync(TimeSpan.FromSeconds(2));
        owners["synthetic-account"] = newSession;
        resume.SetResult();
        Assert.False(await cleanup.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(newSession, owners["synthetic-account"]);
        Assert.True(Release(owners, "synthetic-account", newSession));
    }

    [Fact]
    public void SuccessiveOldOwnersCannotDeleteTheNewestClaimOrAnotherAccount()
    {
        var owners = new ConcurrentDictionary<string, Guid>(StringComparer.Ordinal);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var other = Guid.NewGuid();
        owners["synthetic-account"] = first;
        owners["other-account"] = other;
        owners["synthetic-account"] = second;
        owners["synthetic-account"] = third;
        Assert.False(Release(owners, "synthetic-account", first));
        Assert.False(Release(owners, "synthetic-account", second));
        Assert.Equal(third, owners["synthetic-account"]);
        Assert.Equal(other, owners["other-account"]);
        Assert.False(Release(owners, "missing-account", third));
    }
}
