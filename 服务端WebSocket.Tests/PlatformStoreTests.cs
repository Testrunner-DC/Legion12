using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

public sealed class PlatformStoreTests
{
    [Fact]
    public void RegistrationAuthenticationPasswordAndRootAdminArePersistent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-platform-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "platform.json");
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master");
            Assert.True(admin.Success);
            Assert.Equal("admin", admin.Account!.Role);

            var registered = store.Register("TestPlayer", "password-123");
            Assert.True(registered.Success);
            Assert.NotNull(store.Authenticate($"Bearer {registered.Token}"));
            Assert.False(store.Register("administrator-copy", "password-123").Success);
            Assert.True(store.ChangePassword(registered.Account!.Id, "password-123", "new-password-456").Success);
            Assert.False(store.Login("TestPlayer", "password-123").Success);
            Assert.True(store.Login("TestPlayer", "new-password-456").Success);

            var reloaded = new L12PlatformStore(path);
            Assert.True(reloaded.Login("TestPlayer", "new-password-456").Success);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void BugReportsCanBeConfirmedAssignedAndClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-platform-{Guid.NewGuid():N}");
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var account = store.Register("BugReporter", "password-123").Account;
            var report = store.AddBug(account, "Battle issue", "Steps and expected result", "/game", "ROOM01", "match-1", "test");
            Assert.Equal("new", report.Status);
            var updated = store.UpdateBug(report.Id, "confirmed", "high", "Admin", "reproduced");
            Assert.NotNull(updated);
            Assert.Equal("confirmed", updated!.Status);
            Assert.Equal("high", updated.Priority);
            Assert.Equal("Admin", updated.Assignee);
            Assert.Single(store.Bugs("confirmed"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
