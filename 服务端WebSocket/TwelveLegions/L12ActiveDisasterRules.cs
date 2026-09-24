namespace TwelveLegions.Server;

/// <summary>
/// 活跃天灾持续规则的统一查询层。所有"当前生效天灾是否……"的判定只在这里回答；
/// 引擎、效果计划与可用性投影不得再按天灾卡号分支。新增天灾持续规则时在此登记。
/// ST 预组天灾（色欲之罪）已有独立语义谓词（L12StarterStructuredCardRules.Batch3B），不在本层。
/// </summary>
public static class L12ActiveDisasterRules
{
    // 最终天灾是规则内容身份，不属于平台存储配置。
    public const string AnnihilationCardId = "S01-DS10";

    // S01-DS01 黯陨晨星：主要阶段开始时回合玩家掷骰（由天灾管线 BeginMainPhaseDisasterEffect 承载）。
    public const string DarkMorningStarCardId = "S01-DS01";

    // S01-DS02 百鬼夜行：带有天灾等级的军团进攻主宰时，造成的伤害+1。
    public static bool DisasterLegionMasterDamageBonus(string? activeDisasterId)
        => activeDisasterId == "S01-DS02";

    // S01-DS03 腐秽大地：后排无法放置军团。
    public static bool ForbidsBackRowLegionPlacement(string? activeDisasterId)
        => activeDisasterId == "S01-DS03";

    // S01-DS03 腐秽大地：打出反击战术无需消耗费用。
    public static bool CounterTacticsAreFree(string? activeDisasterId)
        => activeDisasterId == "S01-DS03";

    // S01-DS04 雷霆天怒：兵力高于2000的军团进攻时掷骰，低点数进攻中止。
    public static bool HighTroopsAttackRollsDice(string? activeDisasterId)
        => activeDisasterId == "S01-DS04";

    // S01-DS08 虚构的圣杯：玩家使用圣物效果时，对其主宰造成1点非致命伤害。
    public static bool RelicEffectUseDamagesMaster(string? activeDisasterId)
        => activeDisasterId == "S01-DS08";

    // S01-DS10 堙灭：天灾值锁定为0、不再推进，最终天灾不再触发。
    public static bool DisasterValueLocked(string? activeDisasterId)
        => activeDisasterId == AnnihilationCardId;

    // S02-DS01 天地异变：牌库翻转进行游戏。
    public static bool LibraryFlipped(string? activeDisasterId)
        => activeDisasterId == "S02-DS01";

    // S02-DS01 天地异变：无法从手牌打出与牌库顶部相同兵种的军团。
    public static bool HandLegionBlockedMatchingLibraryTop(string? activeDisasterId)
        => activeDisasterId == "S02-DS01";

    // S02-DS02 迷雾绝境：不可进攻处于活跃状态的前排军团。
    public static bool ActiveFrontRowUnattackable(string? activeDisasterId)
        => activeDisasterId == "S02-DS02";

    // S02-DS02 迷雾绝境：兵力不高于2000的军团无法进攻主宰。
    public static bool MasterUnattackableByTroopsAtMost2000(string? activeDisasterId)
        => activeDisasterId == "S02-DS02";

    // S02-DS02 迷雾绝境：挑衅效果无效。
    public static bool TauntSuppressed(string? activeDisasterId)
        => activeDisasterId == "S02-DS02";

    // S02-DS03 无眠之夜：玩家使用主动休整时，对其主宰造成1点非致命伤害。
    public static bool ActiveRestUseDamagesMaster(string? activeDisasterId)
        => activeDisasterId == "S02-DS03";

    // S02-DS04 风暴乱象：远程军团无法发动远程进攻（2026-09-22 裁定后的正确文本）。
    public static bool RangedLegionsCannotRangedAttack(string? activeDisasterId)
        => activeDisasterId == "S02-DS04";

    // S02-DS05 暴怒之罪：必须优先进攻范围内的对方军团。
    public static bool MustAttackLegionBeforeMaster(string? activeDisasterId)
        => activeDisasterId == "S02-DS05";

    // S02-DS06 傲慢之罪：发动主宰效果需要额外消耗1士气。
    public static bool MasterEffectCostsExtraMorale(string? activeDisasterId)
        => activeDisasterId == "S02-DS06";

    // S02-DS06 傲慢之罪：手牌所有军团登场费用+1。
    public static bool HandLegionEntryCostsExtra(string? activeDisasterId)
        => activeDisasterId == "S02-DS06";

    /// <summary>本层已登记持续规则的天灾封闭集合；新增天灾持续规则必须先登记再消费。</summary>
    public static bool HasRegisteredContinuousRule(string? activeDisasterId)
        => activeDisasterId is "S01-DS01" or "S01-DS02" or "S01-DS03" or "S01-DS04"
            or "S01-DS08" or "S01-DS10" or "S02-DS01" or "S02-DS02" or "S02-DS03"
            or "S02-DS04" or "S02-DS05" or "S02-DS06";
}
