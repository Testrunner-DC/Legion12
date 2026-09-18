using System.Reflection;
using TwelveLegions.Server;

namespace TwelveLegions.Tests;

// Audit metadata only: these bindings do not execute effects or create new atoms.
// Bind exact reviewed ability identities; a new structure must be reviewed again.
internal sealed record L12LifecycleProfile(string Id, IReadOnlyDictionary<string, string> RuntimeOwners,
    IReadOnlyDictionary<string, string> NotApplicable)
{
    public IReadOnlyList<string> AdditionalChecks { get; init; } = [];
}

internal static class EffectLifecycleProfiles
{
    // 〈沙漠君临〉的手牌军团必须在声明时明确选择；结算不能替换成另一张候选。
    internal const string DesertHandSummonAbilityId = "S02-0207:ability:play:528a4430c4b87fb5";

    private static readonly L12LifecycleProfile DesertHandSummon = new("composite:desert-hand-summon",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["candidate-generation"] = "IsDesertHandSummonCandidate",
            ["cost-commit"] = "TryCommitCompositePreStackCosts",
            ["settlement-revalidation"] = "TryResolveS2FactionTactic",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["no-target"] = "本效果必须先声明1张合格手牌军团；不存在候选时不能发动，且尚未提交弃置费用。",
        }) { AdditionalChecks = ["cost-prepaid", "settlement-slot-invalidated", "single-candidate-choice"] };

    internal static readonly string[] CounterDeploymentAbilityIds =
    [
        "S01-0403:ability:death:c3e5fc27d01fe269",
        "S02-0009:ability:play:ff53cfd909161da1",
    ];

    private static readonly L12LifecycleProfile CounterDeployment = new("composite:counter-deployment",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["candidate-generation"] = "IsCounterDeploymentCandidate",
            ["slot-declaration"] = "CreateActivationStepPrompt",
            ["settlement-revalidation"] = "SetDeclaredCounterTactics",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal))
        { AdditionalChecks = ["private-hand-redaction", "independent-target-settlement", "slot-invalidated"] };

    internal static readonly string[] NativeCavalryAbilityIds =
    [
        "S01-0310:ability:active:0a0575206e996652",
        "S01-0409:ability:active:56a01edf47ee1225",
        "S02-0505:ability:active:bac4cb5d348f29f1",
        "ST01-01:ability:active:69626894e55e27e5",
        "ST06-04:ability:active:719cc1c7c1084fa0",
    ];

    private static readonly L12LifecycleProfile NativeCavalry = new("rule-action:cavalry-move",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["command"] = "CavalryMove",
            ["timing"] = "CavalryMoveTimingUnavailableReason",
            ["source-eligibility"] = "CavalryMoveSourceUnavailableReason",
            ["candidate-generation"] = "CavalryMoveDestinationKeys",
            ["destination-revalidation"] = "IsLegalCavalryMoveDestination",
            ["button"] = "BuildRuleActionViews",
            ["presentation"] = "NativeCavalryMovePresentation",
            ["movement-event"] = "RecordLegionMovement",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "原生职介位移是立即执行的规则动作，不创建可响应或无效的效果堆叠；后续位移触发另行验收。",
            ["target-invalidated"] = "无入栈后目标窗口；改以提交时来源/目的地复验覆盖过期客户端选择。",
            ["payment-cancel"] = "没有卡牌或资源费用，也无支付Prompt；未提交目的地不产生动作。",
            ["multi-target-applicability"] = "一次只移动来源军团到一个空位，不存在独立多目标结算。",
        }) { AdditionalChecks = ["source-invalidated", "destination-invalidated", "single-candidate-choice"] };

    internal static readonly string[] PrintedRangedAbilityIds =
    [
        "S01-0003:ability:static:e3471cd2a7042e59",
        "S01-0110:ability:static:e3471cd2a7042e59",
        "S01-0111:ability:static:e3471cd2a7042e59",
        "S01-0112:ability:static:e3471cd2a7042e59",
        "S01-0113:ability:static:e3471cd2a7042e59",
        "S01-0114:ability:static:e3471cd2a7042e59",
        "S01-0115:ability:static:9ba2f4f5354a2a05",
        "S01-0116:ability:static:e3471cd2a7042e59",
        "S01-0208:ability:static:e3471cd2a7042e59",
        "S01-0209:ability:static:e3471cd2a7042e59",
        "S01-0210:ability:static:e3471cd2a7042e59",
        "S01-0211:ability:static:e3471cd2a7042e59",
        "S01-0213:ability:static:9ba2f4f5354a2a05",
        "S01-0214:ability:static:e3471cd2a7042e59",
        "S01-0309:ability:static:e3471cd2a7042e59",
        "S01-0313:ability:static:e3471cd2a7042e59",
        "S01-0314:ability:static:e3471cd2a7042e59",
        "S01-0316:ability:static:9ba2f4f5354a2a05",
        "S01-0409:ability:static:6c03e83e9e18abb1",
        "S01-0410:ability:static:e3471cd2a7042e59",
        "S01-0411:ability:static:e3471cd2a7042e59",
        "S01-0413:ability:static:e3471cd2a7042e59",
        "S01-0415:ability:static:9ba2f4f5354a2a05",
        "S01-0416:ability:static:e3471cd2a7042e59",
        "S02-0003:ability:continuous:e9823ffd970d6ce6",
        "S02-0204:ability:continuous:e9823ffd970d6ce6",
        "S02-0304:ability:continuous:e9823ffd970d6ce6",
        "S02-0507:ability:static:3f520b391281b325",
        "S02-0508:ability:static:aa41bff900061e1d",
        "S02-0513:ability:static:e3471cd2a7042e59",
        "S02-0514:ability:static:e3471cd2a7042e59",
        "S02-0515:ability:static:e3471cd2a7042e59",
        "S02-0517:ability:static:9ba2f4f5354a2a05",
        "S02-0614:ability:continuous:e9823ffd970d6ce6",
        "S02-0617:ability:continuous:e9823ffd970d6ce6",
        "S02-0618:ability:continuous:e9823ffd970d6ce6",
        "S02-0619:ability:continuous:f0839056592c5ee2",
        "ST01-07:ability:static:e3471cd2a7042e59",
        "ST01-08:ability:static:9ba2f4f5354a2a05",
        "ST01-09:ability:static:e3471cd2a7042e59",
        "ST02-08:ability:static:e3471cd2a7042e59",
        "ST03-05:ability:static:efd7771da618f0ac",
        "ST04-07:ability:static:e3471cd2a7042e59",
        "ST05-03:ability:continuous:3119db9911c31cf3",
        "ST05-04:ability:static:e3471cd2a7042e59",
        "ST05-08:ability:static:e3471cd2a7042e59",
        "ST05-09:ability:static:e3471cd2a7042e59",
    ];

    private static readonly L12LifecycleProfile PrintedRanged = new("continuous:printed-range",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["definition"] = "L12StructuredCardRules.GetCombatRuleAbilities",
            ["condition-and-permission"] = "L12StructuredCardRules.CombatProfile",
            ["source-row"] = "CanAttackFromRow",
            ["candidate-generation"] = "BuildLegalAttackTargets",
            ["target-revalidation"] = "TryValidateAttackTarget",
            ["combat-declaration"] = "Attack",
            ["damage-settlement"] = "ResolveDefenseCore",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "该持续能力没有独立入栈、支付或响应窗口，不能作为堆叠效果单独无效；对进攻事件的无效仍属战斗动作验收。",
            ["payment-cancel"] = "本段没有费用或支付Prompt；相邻付费扩展射程是另一段，不继承此豁免。",
            ["duplicate-submit"] = "本段没有发动命令；重复读取条件/候选须无副作用，进攻命令重复提交仍由共用战斗协议验收。",
            ["single-candidate-choice"] = "本段不创建对象选择Prompt；玩家主动提交进攻目标由进攻规则处理，不能自动替玩家进攻。",
            ["multi-target-applicability"] = "本段仅提供来源军团的持续进攻权限，不独立结算多个对象。",
        }) { AdditionalChecks = ["source-row-change", "attack-preview", "ranged-no-loss", "profession-grant"] };

    private static void ValidateOwners(L12LifecycleProfile profile)
    {
        foreach (var owner in profile.RuntimeOwners.Values)
        {
            var parts = owner.Split('.');
            var type = parts.Length == 1 ? typeof(L12GameEngine)
                : parts[0] == nameof(L12StructuredCardRules) ? typeof(L12StructuredCardRules)
                : throw new InvalidOperationException($"Unknown lifecycle owner type: {owner}");
            if (!type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                    .Any(method => method.Name == parts[^1]))
                throw new InvalidOperationException($"Missing reviewed lifecycle owner: {owner}");
        }
    }

    internal static IReadOnlyDictionary<string, L12LifecycleProfile> Read(L12Catalog catalog)
    {
        var abilities = catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .ToDictionary(ability => ability.AbilityId, StringComparer.Ordinal);
        ValidateOwners(DesertHandSummon);
        ValidateOwners(CounterDeployment);
        ValidateOwners(NativeCavalry);
        ValidateOwners(PrintedRanged);
        var bindings = new Dictionary<string, L12LifecycleProfile>(StringComparer.Ordinal);
        if (!abilities.TryGetValue(DesertHandSummonAbilityId, out var desertHandSummon)
            || desertHandSummon.CardId != "S02-0207" || desertHandSummon.Trigger != "play"
            || !desertHandSummon.Text.Contains("天灾等级与弃置军团数量相同", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed desert hand-summon profile: {DesertHandSummonAbilityId}");
        bindings.Add(DesertHandSummonAbilityId, DesertHandSummon);
        foreach (var id in CounterDeploymentAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var counterDeployment)
                || counterDeployment.Text is null || !counterDeployment.Text.Contains("反击战术", StringComparison.Ordinal)
                || counterDeployment.ExecutionModel is not ("spell" or "triggered"))
                throw new InvalidOperationException($"Stale reviewed counter-deployment profile: {id}");
            bindings.Add(id, CounterDeployment);
        }
        foreach (var id in NativeCavalryAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.ExecutionModel != "rule-action"
                || !L12EffectPresentationScenes.IsCavalryMoveRuleAction(ability))
                throw new InvalidOperationException($"Stale reviewed rule-action profile: {id}");
            bindings.Add(id, NativeCavalry);
        }
        // New matching cards remain review work; do not silently grant old evidence to them.
        var matching = abilities.Values.Where(ability => ability.ExecutionModel == "rule-action"
            && L12EffectPresentationScenes.IsCavalryMoveRuleAction(ability)).Select(ability => ability.AbilityId);
        if (!matching.ToHashSet(StringComparer.Ordinal).SetEquals(NativeCavalryAbilityIds))
            throw new InvalidOperationException("Native cavalry family changed; review its per-ability bindings.");
        foreach (var id in PrintedRangedAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.ExecutionModel != "continuous"
                || !ability.Text.Contains("进攻距离+1，远程进攻无损", StringComparison.Ordinal)
                || ability.CostText is not null)
                throw new InvalidOperationException($"Stale reviewed printed-range profile: {id}");
            bindings.Add(id, PrintedRanged);
        }
        var printedRanged = abilities.Values.Where(ability => ability.ExecutionModel == "continuous"
            && ability.Text.Contains("进攻距离+1，远程进攻无损", StringComparison.Ordinal)).Select(ability => ability.AbilityId);
        if (!printedRanged.ToHashSet(StringComparer.Ordinal).SetEquals(PrintedRangedAbilityIds))
            throw new InvalidOperationException("Printed ranged family changed; review its per-ability bindings.");
        return bindings;
    }
}
