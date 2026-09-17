using System.Reflection;
using TwelveLegions.Server;

namespace TwelveLegions.Tests;

// Audit metadata only: these bindings do not execute effects or create new atoms.
// Bind exact reviewed ability identities; a new structure must be reviewed again.
internal sealed record L12LifecycleProfile(string Id, IReadOnlyDictionary<string, string> RuntimeOwners,
    IReadOnlyDictionary<string, string> NotApplicable);

internal static class EffectLifecycleProfiles
{
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
        });

    internal static IReadOnlyDictionary<string, L12LifecycleProfile> Read(L12Catalog catalog)
    {
        var abilities = catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .ToDictionary(ability => ability.AbilityId, StringComparer.Ordinal);
        foreach (var method in NativeCavalry.RuntimeOwners.Values)
            if (typeof(L12GameEngine).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance) is null)
                throw new InvalidOperationException($"Missing reviewed lifecycle owner: {method}");
        var bindings = new Dictionary<string, L12LifecycleProfile>(StringComparer.Ordinal);
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
        if (!matching.ToHashSet(StringComparer.Ordinal).SetEquals(bindings.Keys))
            throw new InvalidOperationException("Native cavalry family changed; review its per-ability bindings.");
        return bindings;
    }
}
