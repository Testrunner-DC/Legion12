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
