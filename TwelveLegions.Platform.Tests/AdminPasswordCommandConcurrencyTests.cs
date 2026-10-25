using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class AdminPasswordCommandConcurrencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparationHasNoCommandOrBusinessWritesAndNotifiesOnlyAfterCommit(bool delete)
    {
        using var f = new Fixture();
        using var barrier = new WorkBarrier();
        f.Store.BeforeAdminPasswordWorkForTesting = _ => barrier.Block();
        var version = f.Store.Version;
        var audit = f.Store.AdminAudit(null, 100).Count;
        var notifications = new List<bool>();
        f.Store.SessionsRevoked += _ => notifications.Add(f.Store.IsSessionActive(f.TargetSession.SessionId));
        var task = Run(() => f.Send(delete));
        try
        {
            barrier.Wait();
            Assert.Equal(version, f.Store.Version);
            Assert.Equal(audit, f.Store.AdminAudit(null, 100).Count);
            Assert.Null(f.Store.FindAdminCommand(f.Actor.Account.Id, "credential-test"));
            Assert.True(f.Store.IsSessionActive(f.TargetSession.SessionId));
            Assert.Equal(0, f.Applies);
        }
        finally { barrier.Release(); await Drain(task); }
        var result = await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(200, result.Status);
        Assert.Equal(1, f.Applies);
        Assert.Equal(new[] { false }, notifications);
        Assert.Equal("executed", result.Command!.Status);
        Assert.False(f.Store.IsSessionActive(f.TargetSession.SessionId));
        Assert.False(new L12PlatformStore(f.Path).IsSessionActive(f.TargetSession.SessionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BusyAdmissionIsZeroWriteAndSameKeyCanRetry(bool delete)
    {
        using var f = new Fixture();
        f.Store.PasswordWorkBudget = new(1, 0, TimeSpan.Zero);
        var before = f.State();
        using (f.Store.PasswordWorkBudget.Acquire())
            Assert.Throws<L12AuthenticationBusyException>(() => f.Send(delete));
        Assert.Equal(before, f.State());
        Assert.Equal(0, f.Applies);
        Assert.Null(f.Store.FindAdminCommand(f.Actor.Account.Id, "credential-test"));
        Assert.Equal(200, f.Send(delete).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplayRedactsTemporaryPasswordAndConflictSkipsPasswordWork(bool delete)
    {
        using var f = new Fixture();
        var first = f.Send(delete);
        Assert.Equal(200, first.Status);
        if (!delete) Assert.Equal(32, first.Password!.Length);
        f.Store.BeforeAdminPasswordWorkForTesting = _ => throw new InvalidOperationException("Replay must skip KDF.");
        var replay = f.Send(delete);
        Assert.Equal(200, replay.Status);
        Assert.True(replay.Replayed);
        Assert.Null(replay.Password);
        Assert.Equal(1, f.Applies);
        var conflict = f.Send(delete, reason: "different reason");
        Assert.Equal("idempotency_conflict", conflict.Code);
        Assert.Equal(409, conflict.Status);
        if (!delete)
        {
            Assert.DoesNotContain(first.Password!, f.State());
            Assert.DoesNotContain(first.Password!, File.ReadAllText(f.Path));
            Assert.DoesNotContain(first.Password!, JsonSerializer.Serialize(first.Command));
            var login = f.Store.Login("pwtarget", first.Password!);
            Assert.True(login.Success, login.Message);
            Assert.True(login.Account!.MustChangePassword);
        }
    }

    [Theory]
    [InlineData("dry", 200)]
    [InlineData("version", 409)]
    [InlineData("invalid-key", 400)]
    [InlineData("missing", 404)]
    [InlineData("self", 403)]
    [InlineData("root", 403)]
    [InlineData("reason", 400)]
    [InlineData("deleted", 403)]
    public void PreclassifiedOutcomesSkipPasswordWork(string scenario, int status)
    {
        foreach (var delete in new[] { false, true })
        {
            using var f = new Fixture();
            var id = scenario switch { "missing" => "missing-account", "self" => f.Actor.Account.Id,
                "root" => f.Root.Account.Id, _ => f.Target.Id };
            if (scenario == "deleted")
                f.Store.DeleteAccountPersonalData(f.Root.Account, id, "synthetic", new("pre-delete"), true);
            f.Store.BeforeAdminPasswordWorkForTesting = _ => throw new InvalidOperationException("No KDF expected.");
            var result = f.Send(delete, target: id, dryRun: scenario == "dry",
                expected: scenario == "version" ? 99 : f.Store.Account(id)?.PermissionVersion,
                key: scenario == "invalid-key" ? "bad key" : "credential-test",
                reason: scenario == "reason" ? "" : "synthetic");
            Assert.Equal(status, result.Status);
            Assert.Equal(0, f.Applies);
        }
    }

    [Theory]
    [InlineData("revoke", 401)]
    [InlineData("disable", 401)]
    [InlineData("expire", 401)]
    [InlineData("demote", 403)]
    [InlineData("require-password", 403)]
    [InlineData("require-name", 403)]
    public async Task FinalCommitReauthenticatesSameSessionAndCurrentPermissions(string change, int status)
    {
        foreach (var delete in new[] { false, true })
        {
            using var f = new Fixture();
            using var barrier = new WorkBarrier();
            f.Store.BeforeAdminPasswordWorkForTesting = _ => barrier.Block();
            var targetBefore = f.RowState(f.Target.Id);
            var task = Run(() => f.Send(delete));
            try
            {
                barrier.Wait();
                if (change == "revoke") f.Store.RevokeOwnSession(f.Actor, f.Actor.SessionId);
                if (change == "disable") f.Store.SetAccountDisabled(f.Root.Account, f.Actor.Account.Id, true, "synthetic", new("disable"), true);
                if (change == "demote") Assert.True(f.Store.SetRole(f.Root.Account, f.Actor.Account.Id, "player"));
                if (change == "expire") f.Mutate("Sessions", f.Actor.SessionId, "ExpiresAt", DateTimeOffset.UtcNow.AddMinutes(-1));
                if (change == "require-password") f.Mutate("Accounts", f.Actor.Account.Id, "MustChangePassword", true);
                if (change == "require-name") f.Mutate("Accounts", f.Actor.Account.Id, "MustChangeUsername", true);
            }
            finally { barrier.Release(); await Drain(task); }
            Assert.Equal(status, (await task.WaitAsync(TimeSpan.FromSeconds(10))).Status);
            Assert.Equal(targetBefore, f.RowState(f.Target.Id));
            Assert.True(f.Store.IsSessionActive(f.TargetSession.SessionId));
            Assert.Equal(0, f.Applies);
            Assert.Null(f.Store.FindAdminCommand(f.Actor.Account.Id, "credential-test"));
        }
    }

    [Theory]
    [InlineData("PasswordHash", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("Salt", "AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("PasswordAlgorithm", "unknown")]
    [InlineData("PasswordIterations", 600000)]
    [InlineData("PasswordCredentialVersion", 2)]
    [InlineData("Username", "pwchanged")]
    [InlineData("PermissionVersion", 2)]
    [InlineData("MustChangePassword", true)]
    [InlineData("MustChangeUsername", true)]
    [InlineData("Disabled", true)]
    [InlineData("Deleted", true)]
    [InlineData("Role", "admin")]
    public async Task EveryCapturedTargetFieldIsRecheckedBeforeAnyApply(string field, object value)
    {
        foreach (var delete in new[] { false, true })
        {
            using var f = new Fixture();
            using var barrier = new WorkBarrier();
            f.Store.BeforeAdminPasswordWorkForTesting = _ => barrier.Block();
            var task = Run(() => f.Send(delete));
            string changed;
            try
            {
                barrier.Wait();
                f.Mutate("Accounts", f.Target.Id, field, value);
                changed = f.RowState(f.Target.Id);
            }
            finally { barrier.Release(); await Drain(task); }
            var result = await task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(409, result.Status);
            Assert.True(result.Code is "account_changed" or "version_conflict");
            Assert.Equal(changed, f.RowState(f.Target.Id));
            Assert.Equal(0, f.Applies);
            Assert.Equal("failed", result.Command!.Status);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ConcurrentSameKeyReplaysWinnerBeforeStaleVersionOrCas(bool delete, bool conflicting)
    {
        using var f = new Fixture();
        f.Store.PasswordWorkBudget = new(2, 0, TimeSpan.Zero);
        using var first = new WorkBarrier();
        using var second = new WorkBarrier();
        var entered = 0;
        f.Store.BeforeAdminPasswordWorkForTesting = _ =>
        {
            if (Interlocked.Increment(ref entered) == 1) first.Block(); else second.Block();
        };
        var a = Run(() => f.Send(delete));
        Task<Result>? b = null;
        Result winner;
        try
        {
            first.Wait();
            b = Run(() => f.Send(delete, reason: conflicting ? "another request" : "synthetic"));
            second.Wait();
            first.Release();
            winner = await a.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(200, winner.Status);
        }
        finally
        {
            first.Release(); second.Release();
            await Drain(b is null ? a : Task.WhenAll(a, b));
        }
        var loser = await b!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(conflicting ? 409 : 200, loser.Status);
        Assert.Equal(!conflicting, loser.Replayed);
        Assert.Null(loser.Password);
        if (!conflicting) Assert.Equal(winner.Command!.Id, loser.Command!.Id);
        else Assert.Equal("idempotency_conflict", loser.Code);
        Assert.Equal(1, f.Applies);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectStoreEntryRejectsOuterTransactionBeforeWork(bool delete)
    {
        using var f = new Fixture();
        var before = f.State();
        f.Store.BeforeAdminPasswordWorkForTesting = _ => throw new Exception("Must reject before work.");
        Assert.Throws<InvalidOperationException>(() => f.Store.ExecuteAdminTransaction(() =>
        {
            if (delete) f.Store.DeleteAccountPersonalData(f.Root.Account, f.Target.Id, "synthetic", new("direct"), true);
            else f.Store.AdminResetPassword(f.Root.Account, f.Target.Id, "synthetic", new("direct"), true);
            return true;
        }));
        Assert.Equal(before, f.State());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparationFaultReleasesBudgetWithoutAnyWrite(bool delete)
    {
        using var f = new Fixture();
        f.Store.PasswordWorkBudget = new(1, 0, TimeSpan.Zero);
        var before = f.State();
        f.Store.BeforeAdminPasswordWorkForTesting = _ => throw new IOException("synthetic preparation failure");
        Assert.Throws<IOException>(() => f.Send(delete));
        Assert.Equal(before, f.State());
        Assert.Equal(0, f.Applies);
        f.Store.BeforeAdminPasswordWorkForTesting = null;
        Assert.Equal(200, f.Send(delete).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCommitRollsBackCredentialCommandAndNotifications(bool delete)
    {
        using var f = new Fixture();
        f.Store.PasswordWorkBudget = new(1, 0, TimeSpan.Zero);
        var before = f.State();
        var notifications = 0;
        f.Store.SessionsRevoked += _ => notifications++;
        f.Store.StorageFailureInjector = stage => { if (stage == "before-commit") throw new IOException("synthetic"); };
        Assert.Throws<L12PlatformStorageUnavailableException>(() => f.Send(delete));
        f.Store.StorageFailureInjector = null;
        Assert.Equal(before, f.State());
        Assert.Equal(0, notifications);
        Assert.Null(f.Store.FindAdminCommand(f.Actor.Account.Id, "credential-test"));
        Assert.True(new L12PlatformStore(f.Path).IsSessionActive(f.TargetSession.SessionId));
        Assert.Equal(200, f.Send(delete).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentStoreWriteConflictsWithoutReplayingCredentialMutation(bool delete)
    {
        using var f = new Fixture();
        using var barrier = new WorkBarrier();
        f.Store.BeforeAdminPasswordWorkForTesting = _ => barrier.Block();
        var notifications = 0;
        f.Store.SessionsRevoked += _ => notifications++;
        var task = Run(() => f.Send(delete));
        try
        {
            barrier.Wait();
            var writer = new L12PlatformStore(f.Path);
            Assert.True(writer.ChangePassword(f.Target.Id, "password-123", "winner-password").Success);
        }
        finally { barrier.Release(); await Drain(task); }
        await Assert.ThrowsAsync<L12PlatformStorageConflictException>(async () =>
            await task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, notifications);
        Assert.Null(f.Store.FindAdminCommand(f.Actor.Account.Id, "credential-test"));
        Assert.True(f.Store.Login("pwtarget", "winner-password").Success);
        Assert.False(f.Store.Account(f.Target.Id)!.Deleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledTargetStillSupportsOriginalAdministrativeOperation(bool delete)
    {
        using var f = new Fixture();
        f.Store.SetAccountDisabled(f.Root.Account, f.Target.Id, true, "synthetic", new("disable"), true);
        Assert.Equal(200, f.Send(delete, expected: f.Store.Account(f.Target.Id)!.PermissionVersion).Status);
        Assert.True(f.Store.Account(f.Target.Id)!.Disabled);
    }

    [Fact]
    public void PreparedCredentialIsOpaqueAndBoundToOwnerActionActorCommandAndTarget()
    {
        using var f = new Fixture();
        var prepared = f.Store.PrepareAdminPassword(f.Actor.Account, f.Target.Id, "synthetic", "reset", "bound");
        Assert.Equal("{}", JsonSerializer.Serialize(prepared));
        Assert.Equal("PreparedAdminPassword", prepared.ToString());
        Assert.Throws<L12SecurityPolicyException>(() => f.Store.ValidatePreparedAdminPassword(f.Actor.Account,
            f.Target.Id, "synthetic", "delete", prepared, "bound"));
        Assert.Throws<L12SecurityPolicyException>(() => f.Store.ValidatePreparedAdminPassword(f.Actor.Account,
            f.Target.Id, "synthetic", "reset", prepared, "other"));
        Assert.Throws<L12SecurityPolicyException>(() => f.Store.ValidatePreparedAdminPassword(f.Root.Account,
            f.Target.Id, "synthetic", "reset", prepared, "bound"));
        Assert.Throws<L12SecurityPolicyException>(() => f.Store.ValidatePreparedAdminPassword(f.Actor.Account,
            f.Root.Account.Id, "synthetic", "reset", prepared, "bound"));
        var reopened = new L12PlatformStore(f.Path);
        Assert.Throws<L12SecurityPolicyException>(() => reopened.ValidatePreparedAdminPassword(f.Actor.Account,
            f.Target.Id, "synthetic", "reset", prepared, "bound"));
    }

    private static Task<T> Run<T>(Func<T> work) => Task.Factory.StartNew(work,
        CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    // Drain before fixture disposal even if an assertion failed inside a barrier.
    // A completed task's fault is rethrown by the normal assertion below; during
    // an earlier failure it must not replace that primary diagnostic.
    private static async Task Drain(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception) when (task.IsCompleted) { }
    }

    private sealed class WorkBarrier : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new(), _release = new();
        internal void Block() { _entered.Set(); if (!_release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic barrier."); }
        internal void Wait() => Assert.True(_entered.Wait(TimeSpan.FromSeconds(5)));
        internal void Release() => _release.Set();
        public void Dispose() { _entered.Dispose(); _release.Dispose(); }
    }

    private sealed record Result(int Status, string Code, bool Replayed, string? Password, L12AdminCommandView? Command);
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "l12-apw", Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(_root, "platform.json");
        internal L12PlatformStore Store { get; }
        internal L12AuthenticatedSession Root { get; }
        internal L12AuthenticatedSession Actor { get; }
        internal L12AccountView Target => TargetSession.Account;
        internal L12AuthenticatedSession TargetSession { get; }
        internal int Applies;
        private readonly string _authorization;
        internal Fixture()
        {
            Store = new(Path);
            Root = Store.AuthenticateTokenSession(Store.Login("Admin", "L12master").Token)!;
            var actor = Store.Register("pwoperator", "password-123");
            Assert.True(actor.Success, actor.Message);
            Assert.True(Store.SetRole(Root.Account, actor.Account!.Id, "admin"));
            Actor = Store.AuthenticateTokenSession(actor.Token)!;
            _authorization = "Bearer " + actor.Token;
            var target = Store.Register("pwtarget", "password-123");
            Assert.True(target.Success, target.Message);
            TargetSession = Store.AuthenticateTokenSession(target.Token)!;
        }
        internal Result Send(bool delete, string key = "credential-test", string reason = "synthetic",
            long? expected = null, string? target = null, bool dryRun = false)
        {
            var id = Guid.NewGuid().ToString("N");
            var targetId = target ?? Target.Id;
            var version = expected ?? Target.PermissionVersion;
            var type = delete ? "account.logical-delete" : "account.password-admin-reset";
            var audit = new L12AdminAuditContext("synthetic", "admin.accounts.status.write", id, key, version, dryRun, reason);
            var bus = new L12AdminCommandBus(Store);
            if (delete)
            {
                var command = new L12AdminCommandEnvelope<AccountDeletionCommandPayload>(id, key, type,
                    Actor.Account, DateTimeOffset.UtcNow, $"account:{targetId}", reason, dryRun, version, new(targetId, reason), audit);
                var result = bus.ExecuteAccountDeletion(command, _authorization, Actor.SessionId,
                    (current, prepared) => { Interlocked.Increment(ref Applies); return L12AdminCommandResult<L12AccountDeletionView>.Ok(
                        Store.ApplyAccountDeletion(current.Actor, targetId, reason, current.AuditContext, prepared!)); },
                    current => L12AdminCommandResult<L12AccountDeletionView>.Ok(Store.DeleteAccountPersonalData(current.Actor, targetId, reason, current.AuditContext, false)));
                return new(result.StatusCode, result.Code, result.Replayed, null, result.Command);
            }
            else
            {
                var command = new L12AdminCommandEnvelope<AccountPasswordResetCommandPayload>(id, key, type,
                    Actor.Account, DateTimeOffset.UtcNow, $"account:{targetId}", reason, dryRun, version, new(targetId, reason), audit);
                var result = bus.ExecuteAccountPasswordReset(command, _authorization, Actor.SessionId,
                    (current, prepared) => { Interlocked.Increment(ref Applies); return L12AdminCommandResult<L12AdminPasswordResetView>.Ok(
                        Store.ApplyAdminResetPassword(current.Actor, targetId, reason, current.AuditContext, prepared!)); },
                    current => L12AdminCommandResult<L12AdminPasswordResetView>.Ok(Store.AdminResetPassword(current.Actor, targetId, reason, current.AuditContext, false)));
                return new(result.StatusCode, result.Code, result.Replayed, result.Value?.TemporaryPassword, result.Command);
            }
        }
        private object Data => typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Store)!;
        internal string State() => JsonSerializer.Serialize(Data);
        internal string RowState(string id) => JsonSerializer.Serialize(Row("Accounts", id));
        private object Row(string collection, string id) => ((IEnumerable)Data.GetType().GetProperty(collection)!.GetValue(Data)!).Cast<object>()
            .Single(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == id);
        internal void Mutate(string collection, string id, string field, object value) => Store.ExecuteAdminTransaction(() =>
        {
            var row = Row(collection, id);
            row.GetType().GetProperty(field)!.SetValue(row, value);
            typeof(L12PlatformStore).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Store, [false]);
            return true;
        });
        public void Dispose()
        {
            Store.StorageFailureInjector = null;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
