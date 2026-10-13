using System.Collections;
using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class CommittedAuthenticationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TokenAuthenticationDoesNotWaitForAnUnrelatedCommit(bool fail)
    {
        using var fixture = new RankedIdentityFixture();
        var login = fixture.Store.Register("authlight", "Password123!");
        Assert.True(login.Success);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("synthetic auth barrier");
            if (fail) throw new IOException("synthetic rejected commit");
        };
        var writer = Task.Factory.StartNew(() => fixture.Store.Register("authwriter", "Password123!"),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            var read = Task.Run(() => fixture.Store.AuthenticateTokenSession(login.Token));
            var result = await read.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(login.Account!.Id, result!.Account.Id);
        }
        finally
        {
            release.Set();
            if (fail) await Assert.ThrowsAsync<L12PlatformStorageUnavailableException>(async () => await writer);
            else Assert.True((await writer).Success);
            fixture.Store.StorageFailureInjector = null;
        }
        Assert.NotNull(fixture.Store.AuthenticateTokenSession(login.Token));
    }

    [Theory]
    [InlineData("role", false)]
    [InlineData("role", true)]
    [InlineData("disabled", false)]
    [InlineData("disabled", true)]
    public async Task AuthenticationPublishesOnlyTheSuccessfulGeneration(string change, bool fail)
    {
        using var fixture = new RankedIdentityFixture();
        var login = fixture.Store.Register("authpublish", "Password123!");
        Assert.True(login.Success);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("synthetic publication barrier");
            if (fail) throw new IOException("synthetic rejected publication");
        };
        var writer = Task.Factory.StartNew(() => fixture.Store.ExecuteAdminTransaction(() =>
        {
            var row = Account(fixture.Store, login.Account!.Id);
            row.GetType().GetProperty(change == "role" ? "Role" : "Disabled")!
                .SetValue(row, change == "role" ? "admin" : true);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            var before = await Task.Run(() => fixture.Store.AuthenticateTokenSession(login.Token))
                .WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal("player", before!.Account.Role);
            Assert.False(before.Account.Disabled);
        }
        finally
        {
            release.Set();
            if (fail) await Assert.ThrowsAsync<L12PlatformStorageUnavailableException>(async () => await writer);
            else Assert.True(await writer);
            fixture.Store.StorageFailureInjector = null;
        }
        var after = fixture.Store.AuthenticateTokenSession(login.Token);
        if (!fail && change == "disabled") Assert.Null(after);
        else Assert.Equal(!fail && change == "role" ? "admin" : "player", after!.Account.Role);
    }

    [Fact]
    public void OwningTransactionReadsItsStagedAuthenticationAndRollbackDoesNotPublishIt()
    {
        using var fixture = new RankedIdentityFixture();
        var login = fixture.Store.Register("authstaged", "Password123!");
        Assert.True(login.Success);
        Assert.Throws<IOException>(() => fixture.Store.ExecuteAdminTransaction<bool>(() =>
        {
            Account(fixture.Store, login.Account!.Id).GetType().GetProperty("Disabled")!
                .SetValue(Account(fixture.Store, login.Account.Id), true);
            Assert.Null(fixture.Store.AuthenticateTokenSession(login.Token));
            throw new IOException("synthetic staged rollback");
        }));
        Assert.NotNull(fixture.Store.AuthenticateTokenSession(login.Token));
    }

    [Fact]
    public async Task NaturalExpiryIsCheckedAtReadTimeWithoutAnotherSave()
    {
        using var fixture = new RankedIdentityFixture();
        var login = fixture.Store.Register("authexpiry", "Password123!");
        Assert.True(login.Success);
        var active = fixture.Store.AuthenticateTokenSession(login.Token)!;
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            var data = Data(fixture.Store);
            var sessions = (IEnumerable)data.GetType().GetProperty("Sessions")!.GetValue(data)!;
            var row = sessions.Cast<object>().Single(x => (string)x.GetType().GetProperty("Id")!.GetValue(x)! == active.SessionId);
            row.GetType().GetProperty("ExpiresAt")!.SetValue(row, DateTimeOffset.UtcNow.AddSeconds(1));
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        Assert.NotNull(fixture.Store.AuthenticateTokenSession(login.Token));
        var version = fixture.Store.Version;
        await Task.Delay(1100);
        Assert.Null(fixture.Store.AuthenticateTokenSession(login.Token));
        Assert.Equal(version, fixture.Store.Version);
    }

    private static object Data(L12PlatformStore store)
        => typeof(L12PlatformStore).GetProperty("_data", Private)!.GetValue(store)!;
    private static object Account(L12PlatformStore store, string id)
    {
        var data = Data(store);
        return ((IEnumerable)data.GetType().GetProperty("Accounts")!.GetValue(data)!).Cast<object>()
            .First(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == id);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("disabled-first")]
    [InlineData("missing-first")]
    public void EmptyOrDuplicateTokenAuthorityCannotPublishOrFallThrough(string kind)
    {
        using var fixture = new RankedIdentityFixture();
        var first = fixture.Store.Register("authbadone", "Password123!");
        var second = fixture.Store.Register("authbadnext", "Password123!");
        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        var firstSession = fixture.Store.AuthenticateTokenSession(first.Token)!;
        var secondSession = fixture.Store.AuthenticateTokenSession(second.Token)!;
        var index = typeof(L12PlatformStore).GetField("_committedSessionActivity", Private)!.GetValue(fixture.Store);
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.ExecuteAdminTransaction(() =>
        {
            var data = Data(fixture.Store);
            var sessions = ((IEnumerable)data.GetType().GetProperty("Sessions")!.GetValue(data)!).Cast<object>().ToArray();
            var a = sessions.Single(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == firstSession.SessionId);
            var b = sessions.Single(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == secondSession.SessionId);
            var property = a.GetType().GetProperty("TokenHash")!;
            property.SetValue(b, kind == "empty" ? "" : property.GetValue(a));
            if (kind == "disabled-first") Account(fixture.Store, first.Account!.Id).GetType().GetProperty("Disabled")!
                .SetValue(Account(fixture.Store, first.Account.Id), true);
            if (kind == "missing-first") a.GetType().GetProperty("AccountId")!.SetValue(a, "synthetic-missing");
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        }));
        Assert.Same(index, typeof(L12PlatformStore).GetField("_committedSessionActivity", Private)!.GetValue(fixture.Store));
        Assert.Equal(first.Account!.Id, fixture.Store.AuthenticateTokenSession(first.Token)!.Account.Id);
        Assert.Equal(second.Account!.Id, fixture.Store.AuthenticateTokenSession(second.Token)!.Account.Id);
    }
}
