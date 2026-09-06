using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

public sealed partial class PlatformStoreTests
{
    [Fact]
    public void BlockedFriendRequestsLookPendingOnlyToSenderAndUnblockAllowsFreshRequest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-friend-privacy-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var sender = store.Register("FriendSender", "test-pass-123").Account!.Id;
            var receiver = store.Register("FriendReceiver", "test-pass-123").Account!.Id;
            Assert.True(store.SendFriendRequest(sender, receiver).Success);
            Assert.Single(store.FriendRequests(receiver));
            Assert.True(store.BlockAccount(receiver, sender).Success);
            Assert.Empty(store.FriendRequests(receiver));
            var hidden = store.SendFriendRequest(sender, receiver);
            Assert.True(hidden.Success);
            Assert.Equal("好友申请已发送", hidden.Message);
            Assert.Equal("pending", Assert.Single(store.FriendRequests(sender)).Status);
            Assert.Empty(store.FriendRequests(receiver));
            Assert.Equal("pending", Assert.Single(store.FindPlayers(sender, "FriendReceiver")).Status);
            Assert.Equal("好友申请已存在", store.SendFriendRequest(sender, receiver).Message);
            Assert.False(store.ResolveFriendRequest(receiver, sender, true).Success);
            store = new L12PlatformStore(path);
            Assert.Empty(store.FriendRequests(receiver));
            Assert.Single(store.FriendRequests(sender));
            Assert.True(store.UnblockAccount(receiver, sender));
            Assert.True(store.SendFriendRequest(sender, receiver).Success);
            Assert.Single(store.FriendRequests(receiver));
            Assert.True(store.ResolveFriendRequest(receiver, sender, true).Success);
            Assert.False(store.ResolveFriendRequest(receiver, sender, true).Success);
            Assert.Single(store.Friends(receiver));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
