namespace GrandUMI.Tests;

/// <summary>
/// 统一解析 GrandUMI 兼容测试所需的卡牌资料与 DSL 定义。
/// 仓库迁移到 D 盘后，历史测试仍只查找已经移除的“卡牌数据”目录，
/// 导致测试运行位置一变化就整体失效。这里以仓库标记文件向上定位，
/// 同时兼容旧目录名称与当前前端公开资料目录。
/// </summary>
internal static class TestDataPaths
{
    private const string OverrideVariable = "GRANDUMI_CARD_DATA_ROOT";

    public static string CardDataRoot => ResolveCardDataRoot();

    public static string DslDefinitionsRoot => ResolveFromAncestors(
        Path.Combine("服务端WebSocket", "Effects", "Definitions"),
        static path => File.Exists(Path.Combine(path, "OP16.json")),
        "Effects/Definitions");

    private static string ResolveCardDataRoot()
    {
        var overridden = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            var fullPath = Path.GetFullPath(overridden);
            if (HasCardData(fullPath)) return fullPath;
            throw new InvalidOperationException(
                $"环境变量 {OverrideVariable} 指向的目录不含 OP15.json/OP16.json: {fullPath}");
        }

        foreach (var relativePath in new[]
                 {
                     "卡牌数据",
                     Path.Combine("opcgpro-vue", "public", "data")
                 })
        {
            var resolved = TryResolveFromAncestors(relativePath, HasCardData);
            if (resolved is not null) return resolved;
        }

        throw new InvalidOperationException(
            "找不到 GrandUMI 卡牌数据目录；已检查卡牌数据与 opcgpro-vue/public/data。" +
            $"可通过 {OverrideVariable} 显式指定。");
    }

    private static bool HasCardData(string path)
        => Directory.Exists(path)
           && File.Exists(Path.Combine(path, "OP15.json"))
           && File.Exists(Path.Combine(path, "OP16.json"));

    private static string ResolveFromAncestors(
        string relativePath,
        Func<string, bool> validator,
        string description)
        => TryResolveFromAncestors(relativePath, validator)
           ?? throw new InvalidOperationException($"找不到 {description} 目录");

    private static string? TryResolveFromAncestors(
        string relativePath,
        Func<string, bool> validator)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (validator(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
