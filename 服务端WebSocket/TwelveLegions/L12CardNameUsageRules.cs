namespace TwelveLegions.Server;

/// <summary>
/// 卡文明确点名的每回合限制由同一控制者的所有同名实例共享。
/// 普通实例/能力组次数不进入此表；身份由规则参数决定，不在运行时解析卡文。
/// </summary>
public static class L12CardNameUsageRules
{
    public static IReadOnlyDictionary<string, string> Keys { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S02-0006"] = "card-name:S02-0006",
            // 保留原有存档键；密米尔本来就是控制者内卡名共享。
            ["S02-0306"] = "s2-mimir-used",
        };

    public static string Key(string cardId) => Keys[cardId];

    public static bool HasUsed(L12PlayerState player, string cardId)
        => player.UsedAbilities.Contains(Key(cardId))
            // 旧检查点已记实例次数视为本回合已用，不能重连后绕过新卡名限制。
            || cardId == "S02-0006" && player.UsedAbilities.Any(key =>
                key.StartsWith("trigger:faith-zealot:", StringComparison.OrdinalIgnoreCase));

    public static bool TryUse(L12PlayerState player, string cardId)
        => !HasUsed(player, cardId) && player.UsedAbilities.Add(Key(cardId));
}
