using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PlatformMirrorMemoryTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    [Fact]
    public void PreparedMirrorMatchesLegacyIndentedUtf8AndChecksum()
    {
        using var fixture = new Fixture();
        var state = State(fixture.Store);
        Content(state)["unicode"] = "中文🙂\n缩进\"与反斜线\\";
        var options = MirrorOptions();
        var legacyBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state, state.GetType(), options));

        var prepared = L12PreparedPlatformMirror.Create(state, state.GetType(), options);
        var output = System.IO.Path.Combine(fixture.Root, "prepared.json");
        prepared.WriteAtomically(output);

        Assert.Equal(legacyBytes.LongLength, prepared.ByteCount);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(legacyBytes)).ToLowerInvariant(), prepared.Checksum);
        Assert.Equal(legacyBytes, File.ReadAllBytes(output));
    }

    [Fact]
    public void TwelveMiBMirrorPreparationStaysBelowWholeStringAllocation()
    {
        var value = new Dictionary<string, string>(StringComparer.Ordinal);
        var payload = new string('x', 64 * 1024);
        for (var index = 0; index < 192; index++) value[$"合成-{index:D3}"] = payload;
        var options = new JsonSerializerOptions { WriteIndented = true };
        _ = L12PreparedPlatformMirror.Create(new Dictionary<string, string> { ["warmup"] = "值" },
            typeof(Dictionary<string, string>), options);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var prepared = L12PreparedPlatformMirror.Create(value, value.GetType(), options);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(prepared.ByteCount > 12L * 1024 * 1024);
        Assert.True(allocated < 8L * 1024 * 1024,
            $"12MiB 镜像准备分配了 {allocated} 字节，疑似重新生成了完整 UTF-16/UTF-8 缓冲");
    }

    [Fact]
    public void CorruptPreparedMirrorCannotReplaceCommittedFileAndCleansOnlyItsOwnTemporaryFile()
    {
        using var fixture = new Fixture();
        var path = System.IO.Path.Combine(fixture.Root, "corrupt-target.json");
        var original = Encoding.UTF8.GetBytes("已提交镜像");
        File.WriteAllBytes(path, original);
        var foreign = path + ".foreign.tmp";
        File.WriteAllText(foreign, "不得清理");
        var value = new Dictionary<string, string> { ["内容"] = "中文🙂" };
        var prepared = L12PreparedPlatformMirror.Create(value, value.GetType(),
            new JsonSerializerOptions { WriteIndented = true });
        var corruptPayload = prepared.CompressedPayload.ToArray();
        corruptPayload[0] ^= 0xff;
        var corruptCompression = new L12PreparedPlatformMirror(corruptPayload,
            corruptPayload.Length, prepared.Checksum, prepared.ByteCount);
        var corruptChecksum = new L12PreparedPlatformMirror(prepared.CompressedPayload.ToArray(),
            prepared.CompressedLength, new string('0', 64), prepared.ByteCount);

        Assert.Throws<InvalidDataException>(() => corruptCompression.WriteAtomically(path));
        Assert.Throws<InvalidDataException>(() => corruptChecksum.WriteAtomically(path));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal("不得清理", File.ReadAllText(foreign));
        Assert.Equal(new[] { foreign }, Directory.EnumerateFiles(fixture.Root, "corrupt-target.json.*.tmp"));
    }

    [Fact]
    public void PrecommitFailureAndCasLoserDoNotPublishMirrorOrLeaveTemporaryFiles()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var mirrorBefore = File.ReadAllBytes(fixture.Path);
        var revisionBefore = StorageRevision(store.TransactionalStoragePath);
        store.StorageFailureInjector = stage =>
        {
            if (stage == "after-rollback-serialize") throw new IOException("合成预提交失败");
        };

        Assert.Throws<L12PlatformStorageUnavailableException>(() =>
            store.Register("prefail01", "Password123!"));
        store.StorageFailureInjector = null;
        Assert.Equal(revisionBefore, StorageRevision(store.TransactionalStoragePath));
        Assert.Equal(mirrorBefore, File.ReadAllBytes(fixture.Path));
        Assert.Empty(MirrorTemporaryFiles(fixture));

        var winner = new L12PlatformStore(fixture.Path);
        var loser = new L12PlatformStore(fixture.Path);
        Assert.True(winner.Register("winner01", "Password123!").Success);
        var winnerMirror = File.ReadAllBytes(fixture.Path);
        Assert.Throws<L12PlatformStorageConflictException>(() =>
            loser.Register("loser01", "Password123!"));
        Assert.Equal(winnerMirror, File.ReadAllBytes(fixture.Path));
        Assert.Empty(MirrorTemporaryFiles(fixture));
    }

    [Fact]
    public void PostcommitMirrorFailureKeepsSqliteCommitRecoverableAndCleansTemporaryFile()
    {
        using var fixture = new Fixture();
        File.Delete(fixture.Path);
        Directory.CreateDirectory(fixture.Path);

        var registration = fixture.Store.Register("commit01", "Password123!");

        Assert.True(registration.Success);
        Assert.False(fixture.Store.StorageStatus().FallbackMirrorHealthy);
        Assert.True(fixture.Store.Login("commit01", "Password123!").Success);
        Assert.Empty(MirrorTemporaryFiles(fixture));
        Directory.Delete(fixture.Path);
        var restarted = new L12PlatformStore(fixture.Path);
        Assert.True(restarted.Login("commit01", "Password123!").Success);
        Assert.True(restarted.StorageStatus().DatabaseIntegrityValid);
        Assert.True(restarted.StorageStatus().SnapshotChecksumValid);
    }

    private static object State(L12PlatformStore store) => typeof(L12PlatformStore)
        .GetProperty("_data", PrivateInstance)!.GetValue(store)!;

    private static Dictionary<string, string> Content(object state)
        => (Dictionary<string, string>)state.GetType().GetProperty("Content")!.GetValue(state)!;

    private static JsonSerializerOptions MirrorOptions() => (JsonSerializerOptions)typeof(L12PlatformStore)
        .GetField("PlatformMirrorJsonOptions", PrivateStatic)!.GetValue(null)!;

    private static long StorageRevision(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT storage_revision FROM platform_state WHERE singleton_id=1;";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string[] MirrorTemporaryFiles(Fixture fixture)
        => Directory.EnumerateFiles(fixture.Root, System.IO.Path.GetFileName(fixture.Path) + "*.tmp")
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "l12-platform-mirror", Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(Root, "platform.json");
        internal L12PlatformStore Store { get; }

        internal Fixture()
        {
            Directory.CreateDirectory(Root);
            Store = new L12PlatformStore(Path);
        }

        public void Dispose()
        {
            Store.StorageFailureInjector = null;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
