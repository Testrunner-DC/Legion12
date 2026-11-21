using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>
/// A5 回归守卫：对局日志的每条写连接都必须以 NORMAL 同步级别打开。
/// synchronous 是连接级 PRAGMA，只在 InitializeAsync 里设一次不会覆盖后续连接。
/// </summary>
[Collection("Platform environment")]
public sealed class MatchRecorderSynchronousModeTests
{
    [Fact]
    public async Task WriteConnectionsUseNormalSynchronousMode()
    {
        var root = TempRoot();
        try
        {
            var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();

            await using var connection = await recorder.OpenWriteConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA synchronous;";
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
        finally
        {
            // 连接池会持有数据库文件句柄，先清池再删临时目录。
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "l12-a5guard-" + Guid.NewGuid().ToString("n")[..12]);
        Directory.CreateDirectory(path);
        return path;
    }
}
