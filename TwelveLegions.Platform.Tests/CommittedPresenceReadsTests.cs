using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class CommittedPresenceReadsTests
{
    [Fact]
    public void PresenceFriendshipsAndRequestDirectionsMatchTheExistingAuthority()
    {
        using var f = new RankedIdentityFixture();
        var viewer = f.CreateAccount("presenceone");
        var friend = f.CreateAccount("presfriend");
        var incoming = f.CreateAccount("presin");
        var outgoing = f.CreateAccount("presout");
        Assert.True(f.Store.SendFriendRequest(viewer.Id, friend.Id).Success);
        Assert.True(f.Store.ResolveFriendRequest(friend.Id, viewer.Id, true).Success);
        Assert.True(f.Store.SendFriendRequest(incoming.Id, viewer.Id).Success);
        Assert.True(f.Store.SendFriendRequest(viewer.Id, outgoing.Id).Success);
        var view = f.Store.ReadCommittedPresencePlatformView(viewer.Id);
        var expected = f.Store.Friends(viewer.Id).Concat(f.Store.FriendRequests(viewer.Id)).ToArray();
        Assert.All(expected, row => Assert.Equal(row, view.Relationships[row.AccountId]));
        Assert.True(view.AcceptedFriendIds.Contains(friend.Id));
        Assert.False(view.AcceptedFriendIds.Contains(incoming.Id));
        Assert.Equal("incoming", view.Relationships[incoming.Id].Direction);
        Assert.Equal("outgoing", view.Relationships[outgoing.Id].Direction);
        Assert.True(f.Store.BlockAccount(viewer.Id, friend.Id).Success);
        Assert.False(f.Store.ReadCommittedPresencePlatformView(viewer.Id).AcceptedFriendIds.Contains(friend.Id));
        Assert.True(view.AcceptedFriendIds.Contains(friend.Id));
        // Suppressed incoming requests are visible only to their requester, as before.
        Assert.True(f.Store.SendFriendRequest(friend.Id, viewer.Id).Success);
        Assert.DoesNotContain(friend.Id, f.Store.ReadCommittedPresencePlatformView(viewer.Id).Relationships.Keys);
        Assert.Equal("outgoing", f.Store.ReadCommittedPresencePlatformView(friend.Id).Relationships[viewer.Id].Direction);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PresenceUsesTheCommittedGenerationWhileFriendRemovalIsStaged(bool fail)
    {
        using var f = new RankedIdentityFixture();
        var viewer = f.CreateAccount("presowner");
        var friend = f.CreateAccount("prestarget");
        Assert.True(f.Store.SendFriendRequest(viewer.Id, friend.Id).Success);
        Assert.True(f.Store.ResolveFriendRequest(friend.Id, viewer.Id, true).Success);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("synthetic presence barrier");
            if (fail) throw new IOException("synthetic rejected friendship removal");
        };
        var writer = Task.Factory.StartNew(() => f.Store.RemoveFriend(viewer.Id, friend.Id),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            var view = await Task.Run(() => f.Store.ReadCommittedPresencePlatformView(viewer.Id))
                .WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(view.AcceptedFriendIds.Contains(friend.Id));
        }
        finally
        {
            release.Set();
            if (fail) await Assert.ThrowsAsync<L12PlatformStorageUnavailableException>(async () => await writer);
            else Assert.True(await writer);
            f.Store.StorageFailureInjector = null;
        }
        Assert.Equal(fail, f.Store.ReadCommittedPresencePlatformView(viewer.Id).AcceptedFriendIds.Contains(friend.Id));
    }
}
