using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class AdminTransactionMemoryTests(ITestOutputHelper output)
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargeAdminSnapshotDoesNotAllocateAWholeJsonString(bool nested)
    {
        using var fixture = new Fixture();
        // 12MiB 合成正文；每次旧快照至少另分配 24MiB UTF-16 字符串。
        var content = Content(fixture.Store);
        var payload = new string('x', 64 * 1024);
        for (var index = 0; index < 192; index++) content["synthetic-" + index] = payload;
        bool Execute() => fixture.Store.ExecuteAdminTransaction(() =>
            !nested || fixture.Store.ExecuteAdminTransaction(() => true));
        Assert.True(Execute());
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(Execute());
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"嵌套={nested}，快照额外分配={allocated}字节");
        Assert.True(allocated < 8 * 1024 * 1024,
            $"后台快照分配了 {allocated} 字节，超过流式压缩快照的 8MiB 预算");
        Assert.Equal(payload, Content(fixture.Store)["synthetic-191"]);
        Assert.Equal("sqlite", fixture.Store.StorageStatus().Mode);
    }

    [Fact]
    public void NestedFailurePreservesExactOuterStagedStateAndOuterFailureRestoresEntry()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        Content(store)["unicode"] = "合成中文🙂\n\"\\";
        var before = FullJson(store);
        Assert.Throws<InvalidOperationException>(() => store.ExecuteAdminTransaction<bool>(() =>
        {
            Content(store)["outer"] = "未提交的外层内容";
            var outer = FullJson(store);
            Assert.Throws<IOException>(() => store.ExecuteAdminTransaction<bool>(() =>
            {
                Content(store)["outer"] = "必须回滚的内层内容";
                Content(store)["inner"] = "必须移除";
                throw new IOException("合成内层失败");
            }));
            Assert.Equal(outer, FullJson(store));
            throw new InvalidOperationException("合成外层失败");
        }));
        Assert.Equal(before, FullJson(store));
        Assert.True(store.Login("memoryuser", "synthetic-password").Success);
    }

    [Fact]
    public void CommitFailurePreservesDecksAndAuthenticationAndAllowsRetry()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var before = FullJson(store);
        store.StorageFailureInjector = stage =>
        {
            if (stage == "before-commit") throw new IOException("合成提交失败");
        };
        void Change() => store.ExecuteAdminTransaction(() =>
        {
            store.SetContent("rules.notice", "合成事务提交");
            return true;
        });
        Assert.Throws<L12PlatformStorageUnavailableException>(Change);
        store.StorageFailureInjector = null;
        Assert.Equal(before, FullJson(store));
        Assert.Single(store.Decks(fixture.Owner));
        Assert.Single(store.PublishedDecks(fixture.Owner));
        Assert.True(store.Login("memoryuser", "synthetic-password").Success);
        Change();
        Assert.Equal("合成事务提交", new L12PlatformStore(fixture.Path).GetContent("rules.notice"));
        Assert.True(new L12PlatformStore(fixture.Path).Login("memoryuser", "synthetic-password").Success);
    }

    [Fact]
    public void CorruptTransactionSnapshotFailsClosedForReadsAuthenticationAndWrites()
    {
        using var fixture = new Fixture();
        var error = Assert.Throws<TargetInvocationException>(() => typeof(L12PlatformStore)
            .GetMethod("RestoreAdminTransactionSnapshot", PrivateInstance)!.Invoke(fixture.Store, [new byte[] { 1, 2, 3 }]));
        Assert.IsType<L12PlatformStorageUnavailableException>(error.InnerException);
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.Accounts());
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.Login("memoryuser", "synthetic-password"));
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.Register("blocked", "synthetic-password"));
    }

    [Fact]
    public void StorageDiagnosticsKeepNestedErrorTypesWithoutSensitiveMessages()
    {
        var error = new AggregateException("合成敏感账号", new IOException("合成私有正文"),
            new InvalidDataException("合成令牌", new OutOfMemoryException("合成密码")));
        var summary = L12PlatformStore.StorageFailureSummary("合成恢复", error);
        Assert.Contains(nameof(AggregateException), summary);
        Assert.Contains(nameof(IOException), summary);
        Assert.Contains(nameof(InvalidDataException), summary);
        Assert.Contains(nameof(OutOfMemoryException), summary);
        Assert.DoesNotContain("合成敏感账号", summary);
        Assert.DoesNotContain("合成私有正文", summary);
        Assert.DoesNotContain("合成令牌", summary);
        Assert.DoesNotContain("合成密码", summary);
    }

    private static object State(L12PlatformStore store) => typeof(L12PlatformStore)
        .GetProperty("_data", PrivateInstance)!.GetValue(store)!;
    private static Dictionary<string, string> Content(L12PlatformStore store)
    {
        var state = State(store);
        return (Dictionary<string, string>)state.GetType().GetProperty("Content")!.GetValue(state)!;
    }
    private static string FullJson(L12PlatformStore store)
    {
        var state = State(store);
        return JsonSerializer.Serialize(state, state.GetType());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "l12-admin-memory", Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(_root, "platform.json");
        internal L12PlatformStore Store { get; }
        internal string Owner { get; }
        internal Fixture()
        {
            Store = new L12PlatformStore(Path);
            var registration = Store.Register("memoryuser", "synthetic-password");
            Assert.True(registration.Success);
            Owner = registration.Account!.Id;
            var deck = new L12PresetDeckDefinition
                { Name = "合成牌组", MasterId = "M1", CardIds = ["C1"], MoraleIds = [], SpecialIds = [] };
            Assert.True(Store.CreateDeck(Owner, deck).Success);
            Assert.NotNull(Store.PublishDeck(Owner, deck, null));
        }
        public void Dispose()
        {
            Store.StorageFailureInjector = null;
            SqliteConnection.ClearAllPools();
            Directory.Delete(_root, true);
        }
    }
}
