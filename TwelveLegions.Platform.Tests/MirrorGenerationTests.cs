using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// B-2 步骤2（方向 A）：`platform.json` 兼容镜像只由初始快照/赛季提交/牌库迁移重写，
// 普通保存不重写这份 ~130 MB 的离线副本。这里固定三件事：镜像写入时记录它所属的代次、
// 状态视图如实报告该代次、以及 JSON 兜底把「恢复副本落后多少」说出来。
public sealed class MirrorGenerationTests
{
    private const string Password = "password-123";

    [Fact]
    public void OrdinarySavesKeepTheRecordedMirrorChecksumEqualToTheFile()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var initialGeneration = store.StorageStatus().FallbackMirrorVersion;
            Assert.NotNull(initialGeneration);
            Assert.True(initialGeneration <= store.Version,
                $"mirror generation={initialGeneration} version={store.Version}");

            Assert.True(store.Register("mirrorown", Password).Success);
            Assert.True(store.Login("mirrorown", Password).Success);
            Assert.True(store.Login("mirrorown", Password).Success);

            var status = store.StorageStatus();
            Assert.True(status.FallbackMirrorHealthy);
            Assert.Equal(initialGeneration, status.FallbackMirrorVersion);
            Assert.True(status.FallbackMirrorVersion < store.Version,
                $"mirror generation={status.FallbackMirrorVersion} version={store.Version}");
            using var connection = OpenReadOnly(store.TransactionalStoragePath);
            Assert.Equal(Sha256(File.ReadAllBytes(path)),
                Text(connection, "SELECT value FROM storage_meta WHERE key='fallback_json_sha256';"));
            Assert.Equal(status.FallbackMirrorVersion!.Value.ToString(),
                Text(connection, "SELECT value FROM storage_meta WHERE key='fallback_json_generation';"));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void RecoveryFromTheMirrorReportsHowFarBehindItIs()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            Assert.True(store.Register("recoverown", Password).Success);
            Assert.True(store.Login("recoverown", Password).Success);
            var mirrorGeneration = store.StorageStatus().FallbackMirrorVersion!.Value;
            var token = store.Login("recoverown", Password).Token!;
            var database = store.TransactionalStoragePath;

            // The database is gone: the store rebuilds it from the offline copy, and the rebuilt
            // generation is the mirror's generation, not the latest one.
            SqliteConnection.ClearAllPools();
            File.Delete(database);
            File.Delete(database + "-wal");
            File.Delete(database + "-shm");

            var recovered = new L12PlatformStore(path);
            var status = recovered.StorageStatus();
            Assert.Equal("sqlite", status.Mode);
            Assert.Equal(mirrorGeneration, status.FallbackMirrorVersion);
            // Everything written after the last mirror write is simply absent from that copy.
            Assert.DoesNotContain(recovered.Accounts(), row => row.Username == "recoverown");
            Assert.Null(recovered.AuthenticateToken(token));
        }
        finally { Cleanup(root); }
    }

    private static string Sha256(byte[] value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(value)).ToLowerInvariant();

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string Text(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-mirror-generation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string root)
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(root, true); }
        catch (IOException) { }
    }
}
