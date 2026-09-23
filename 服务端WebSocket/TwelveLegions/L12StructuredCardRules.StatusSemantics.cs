namespace TwelveLegions.Server;

public sealed record L12ExtendedRangeRule(string Text, string CostText, int ConsumeMorale, int ReturnMorale, bool AllowsMaster);
public sealed record L12PrintedEntryCostRule(string Condition, int Adjustment, int Threshold = 0,
    string? Faction = null, string? ReferenceCardId = null);
public sealed record L12HandPlayBlockRule(string BlockedCardType, bool AllowsSameCardId, int Priority,
    string Reason);
public sealed record L12OpponentTurnFieldRule(int CostAdjustment, int FrontRowTroopsBonus);
public sealed record L12FieldMoraleResourceRule(string ResourceType, string DisplayName,
    bool ControllerTurnOnly, bool RequiresActive);
public sealed record L12MoraleZoneResourceRule(string ResourceType, string DisplayName,
    bool ReturnsToOwnerGraveyard);
public sealed record L12MasterAbilityGateRule(string AbilityId, string RequiredMasterId,
    string DisabledReason);
public sealed record L12MasterFieldAuraRule(string TargetCardId, int TroopsAdjustment, int CostAdjustment);

/// <summary>
/// Runtime identity predicates backed by the structured card rule layer.
/// Keeping these identities here prevents presentation and lifecycle consumers
/// from introducing new card-id branches into the rule engine.
/// </summary>
public static class L12StructuredCardSemantics
{
    private const string HeavenEarthChangeCardId = "S02-DS01";
    private const string HannibalCardId = "S02-0516";
    private const string IsisCardId = "S01-02M1";
    private const string KingsSwordCardId = "S02-06S2";
    private const string MedjedCardId = "S01-02M3";
    private const string GramCardId = "S01-0317";
    private const string HattoriHanzoCardId = "S01-0415";
    private const string KusanagiCardId = "S01-0417";
    private const string TombGuardCardId = "S01-0212";
    private const string ProliferatingScarabCardId = "S02-0201";
    private static readonly Dictionary<string, L12ExtendedRangeRule> ExtendedRangeRules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S01-0003"] = new("位于后排 可消耗2士气：此军团本回合可进攻对方后排和主宰。", "消耗2士气", 2, 0, true),
        ["S01-0113"] = new("「位于后排」可返还1士气：此军团本回合可进攻对方后排。", "返还1士气", 0, 1, false),
    };
    private static readonly Dictionary<string, L12PrintedEntryCostRule> PrintedEntryCostRules =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0104"] = new("controller-morale-less-than-opponent", -1),
            ["S01-0107"] = new("controller-morale-less-than-opponent", -1),
            ["S01-0114"] = new("controller-morale-less-than-opponent", -1),
            ["S01-0202"] = new("controller-field-card-absent", -2, ReferenceCardId: "S01-0212"),
            ["S01-0301"] = new("grave-faction-legions-per-threshold", -1, 4, "asgard"),
            ["S01-0302"] = new("friendly-field-legion-count", -1),
            ["S01-0305"] = new("controller-hp-at-most", -1, 6),
            ["S01-0306"] = new("controller-hp-at-most", -1, 6),
            ["S02-0202"] = new("named-legions-left-this-turn", -1),
            ["S02-0203"] = new("controller-field-card-absent", -1, ReferenceCardId: "S01-0212"),
        };
    private static readonly Dictionary<string, L12HandPlayBlockRule> HandPlayBlockRules =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["S02-0305"] = new("artifact", false, 0, "〈安德华拉诺特〉使我方无法从手牌打出圣物"),
            ["S02-0205"] = new("artifact", true, 1, "〈黄金圣甲虫〉位于我方圣物区，我方无法从手牌打出其他圣物"),
        };
    private static readonly HashSet<string> SummonTurnCounterTacticProtectionCards =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "S01-0201", "S01-0202", "ST02-01",
        };
    private static readonly HashSet<string> RelicZoneLimitExemptCards =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "S01-0216", "S01-0217", "S01-0218", "S01-0219", "S01-0220",
        };
    private static readonly HashSet<string> OutOfDeckGraveyardLifecycleCards =
        new(StringComparer.OrdinalIgnoreCase)
        {
            TombGuardCardId, ProliferatingScarabCardId,
        };

    // 特殊响应能力身份注册：绝对防御型（响应对方进攻/效果，抵挡或无效，弃置1手牌）、
    // 落穴型（无效对方军团登场效果）、佣兵部队型（对方进攻我方军团时从手牌弃置自身抵挡）。
    // 候选枚举、公开卡池判定、提交与费用分支全部改读此注册表；新增同型响应卡只在此登记，
    // 禁止在引擎中新增卡号分支。
    public static bool IsAbsoluteDefenseResponse(string cardId) => cardId == "S01-0016";
    public static bool IsPitfallEntryNegationResponse(string cardId) => cardId == "S01-0018";
    public static bool IsMercenaryHandBlockResponse(string cardId) => cardId == "S01-0002";
    private static readonly Dictionary<string, L12OpponentTurnFieldRule> OpponentTurnFieldRules =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [TombGuardCardId] = new(1, 1000),
        };
    private static readonly Dictionary<string, L12FieldMoraleResourceRule> FieldMoraleResourceRules =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [TombGuardCardId] = new("tomb-guard", "陵墓守卫", ControllerTurnOnly: true, RequiresActive: true),
        };
    private static readonly Dictionary<string, L12MoraleZoneResourceRule> MoraleZoneResourceRules =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["S02-0010"] = new("black-lotus", "黑色莲花", ReturnsToOwnerGraveyard: true),
        };
    private static readonly Dictionary<string, int> DerivedSpecialCardLimits =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["S02-01S1"] = 1,
            [KingsSwordCardId] = 1,
        };
    private static readonly Dictionary<string, L12MasterFieldAuraRule> MasterFieldAuraRules =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-02D1"] = new(TombGuardCardId, 1000, 1),
        };
    private static readonly Dictionary<string, L12MasterAbilityGateRule> MasterAbilityGateRules =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["S02-0301"] = new("thorHammerRevive", "S02-03M1",
                "仅〈雷神索尔〉可发动墓地中〈雷神之锤〉的效果"),
        };
    private static readonly HashSet<string> AttachedStrongAttackCards = new(StringComparer.OrdinalIgnoreCase)
    {
        KingsSwordCardId,
        "ST04-10",
    };

    public static bool IsHeavenEarthChange(string? cardId)
        => string.Equals(cardId, HeavenEarthChangeCardId, StringComparison.OrdinalIgnoreCase);

    public static bool IsHannibal(string? cardId)
        => string.Equals(cardId, HannibalCardId, StringComparison.OrdinalIgnoreCase);

    public static bool IsIsis(string? cardId)
        => string.Equals(cardId, IsisCardId, StringComparison.OrdinalIgnoreCase);

    public static bool IsKingsSword(string? cardId)
        => string.Equals(cardId, KingsSwordCardId, StringComparison.OrdinalIgnoreCase);

    public static int DerivedSpecialCardLimit(string? cardId)
        => cardId is null ? 0 : DerivedSpecialCardLimits.GetValueOrDefault(cardId);

    public static bool IsDerivedSpecialCard(string? cardId)
        => DerivedSpecialCardLimit(cardId) > 0;

    public static L12MasterFieldAuraRule? MasterFieldAuraRule(string? masterId)
        => masterId is null ? null : MasterFieldAuraRules.GetValueOrDefault(masterId);

    public static bool IsMedjed(string? cardId)
        => string.Equals(cardId, MedjedCardId, StringComparison.OrdinalIgnoreCase);

    public static bool HasBackRowExtendedRangeActive(string? cardId)
        => ExtendedRangeRule(cardId) is not null;

    public static L12ExtendedRangeRule? ExtendedRangeRule(string? cardId)
        => cardId is null ? null : ExtendedRangeRules.GetValueOrDefault(cardId);

    public static L12PrintedEntryCostRule? PrintedEntryCostRule(string? cardId)
        => cardId is null ? null : PrintedEntryCostRules.GetValueOrDefault(cardId);

    public static L12HandPlayBlockRule? HandPlayBlockRule(string? cardId)
        => cardId is null ? null : HandPlayBlockRules.GetValueOrDefault(cardId);

    public static bool HasSummonTurnCounterTacticProtection(string? cardId)
        => cardId is not null && SummonTurnCounterTacticProtectionCards.Contains(cardId);

    public static bool IgnoresRelicZoneLimit(string? cardId)
        => cardId is not null && RelicZoneLimitExemptCards.Contains(cardId);

    public static bool HasOutOfDeckGraveyardLifecycle(string? cardId)
        => cardId is not null && OutOfDeckGraveyardLifecycleCards.Contains(cardId);

    public static L12OpponentTurnFieldRule? OpponentTurnFieldRule(string? cardId)
        => cardId is null ? null : OpponentTurnFieldRules.GetValueOrDefault(cardId);

    public static L12FieldMoraleResourceRule? FieldMoraleResourceRule(string? cardId)
        => cardId is null ? null : FieldMoraleResourceRules.GetValueOrDefault(cardId);

    public static L12MoraleZoneResourceRule? MoraleZoneResourceRule(string? cardId)
        => cardId is null ? null : MoraleZoneResourceRules.GetValueOrDefault(cardId);

    public static L12MasterAbilityGateRule? MasterAbilityGate(string? cardId, string? abilityId)
    {
        if (cardId is null || abilityId is null
            || !MasterAbilityGateRules.TryGetValue(cardId, out var rule)
            || !string.Equals(rule.AbilityId, abilityId, StringComparison.OrdinalIgnoreCase))
            return null;
        return rule;
    }

    public static string? MasterAbilityGateFailureReason(L12PlayerState player, string? cardId,
        string? abilityId)
    {
        var rule = MasterAbilityGate(cardId, abilityId);
        return rule is not null
            && !string.Equals(player.MasterId, rule.RequiredMasterId, StringComparison.OrdinalIgnoreCase)
                ? rule.DisabledReason
                : null;
    }

    public static bool IsGram(string? cardId)
        => string.Equals(cardId, GramCardId, StringComparison.OrdinalIgnoreCase);

    public static bool IsHattoriHanzo(string? cardId)
        => string.Equals(cardId, HattoriHanzoCardId, StringComparison.OrdinalIgnoreCase);

    public static bool IsKusanagi(string? cardId)
        => string.Equals(cardId, KusanagiCardId, StringComparison.OrdinalIgnoreCase);

    public static bool IsTombGuard(string? cardId)
        => string.Equals(cardId, TombGuardCardId, StringComparison.OrdinalIgnoreCase);

    public static bool IsProliferatingScarab(string? cardId)
        => string.Equals(cardId, ProliferatingScarabCardId, StringComparison.OrdinalIgnoreCase);

    /// <summary>全卡池附叠后持续赋予“强攻”的唯一身份表。</summary>
    public static bool GrantsStrongAttackWhileAttached(string? cardId)
        => cardId is not null && AttachedStrongAttackCards.Contains(cardId);

    /// <summary>
    /// 战斗、快照和效果赋予统一读取此查询；自身、临时与任意多个附叠来源只投影一个关键词。
    /// </summary>
    public static bool HasEffectiveStrongAttack(L12CardInstance card)
        => card.HasStrongAttack || card.AttachedCards.Any(attached =>
            GrantsStrongAttackWhileAttached(attached.CardId));
}
