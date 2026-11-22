using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>
/// A4 回归守卫：注册接口不得用精确文案交出"用户名是否存在"的答案；
/// 并发连接上限在非法覆盖值下必须回落到默认值，而不是放弃限制。
/// </summary>
[Collection("Platform environment")]
public sealed class RegistrationEnumerationAndConnectionLimitTests
{
    [Fact]
    public void DuplicateRegistrationDoesNotRevealUsernameExistenceExactly()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            Assert.True(store.Register("enumbtarget", "password-123").Success);

            var duplicate = store.Register("enumbtarget", "password-123");
            Assert.False(duplicate.Success);
            Assert.DoesNotContain("已存在", duplicate.Message, StringComparison.Ordinal);
            Assert.Contains("用户名不可用", duplicate.Message, StringComparison.Ordinal);

            var caseVariant = store.Register("ENUMBTARGET", "password-123");
            Assert.False(caseVariant.Success);
            Assert.Equal(duplicate.Message, caseVariant.Message);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DuplicateRegistrationStaysImmediateAndUsesTheUnifiedMessage()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            Assert.True(store.Register("delaytarget", "password-123").Success);

            // 2026-11-22 实测证明 20-40ms 抖动对 ~1.1s 的真实差距无效，已移除。
            // 这里锁住"不再有任何掩盖性延迟"与"两路文案一致"两点。
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var duplicate = await store.RegisterAsync("delaytarget", "password-123");
            stopwatch.Stop();

            Assert.False(duplicate.Success);
            Assert.Equal(L12PlatformStore.UnavailableUsernameMessage, duplicate.Message);
            Assert.True(stopwatch.ElapsedMilliseconds < 500,
                "重复注册出现可疑的人工延迟：" + stopwatch.ElapsedMilliseconds + "ms");

            var syncDuplicate = store.Register("delaytarget", "password-123");
            Assert.False(syncDuplicate.Success);
            Assert.Equal(L12PlatformStore.UnavailableUsernameMessage, syncDuplicate.Message);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, 512)]
    [InlineData("", 512)]
    [InlineData("0", 512)]
    [InlineData("-3", 512)]
    [InlineData("abc", 512)]
    [InlineData("7", 7)]
    public void ConnectionCapFallsBackToTheDefaultForInvalidOverrides(string? configured, int expected)
        => Assert.Equal(expected, L12WebSocketServer.ResolveMaxConcurrentSockets(configured));

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "l12-a4guard-" + Guid.NewGuid().ToString("n")[..12]);
        Directory.CreateDirectory(path);
        return path;
    }
}
