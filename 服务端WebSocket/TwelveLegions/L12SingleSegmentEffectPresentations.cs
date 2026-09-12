namespace TwelveLegions.Server;

internal sealed record L12SingleSegmentEffectPresentationDefinition(
    string CardId,
    int AbilitySequence,
    string RuntimeAbilityId);

/// <summary>
/// 已核对为“单段、单一结算结果”的主动效果目录。目录只负责把既有整段卡文升级为
/// 可追踪的结构化结算段，不改变资格、费用、选择或实际结算。复合、分支及私有区域
/// 流程必须进入各自的组合计划，禁止为了获得动效结果而在此降格登记。
/// </summary>
internal static class L12SingleSegmentEffectPresentations
{
    internal const string Flow = "single-active";

    private static readonly L12SingleSegmentEffectPresentationDefinition[] Definitions =
    [
        new("S01-0109", 2, "addMorale"),
        new("S01-0314", 3, "olgaDebuff"),
        new("S02-0003", 3, "disableCounters"),
        new("S02-0204", 3, "imhotepDiscount"),
        new("S02-0513", 3, "aristotleDiscount"),
        new("S02-06D1", 4, "avalonDebuff"),
        new("ST02-05", 1, "oasisDancerBuff"),
        new("ST03-05", 2, "christinaFreeTactic"),
    ];

    internal static IReadOnlyList<L12SingleSegmentEffectPresentationDefinition> All => Definitions;

    internal static L12AtomicAbility[] Attach(L12AtomicAbility[] abilities)
    {
        if (abilities.Length == 0) return abilities;
        var cardId = abilities[0].CardId;
        var definitions = Definitions.Where(item => item.CardId.Equals(cardId,
            StringComparison.OrdinalIgnoreCase)).ToArray();
        if (definitions.Length == 0) return abilities;

        var result = abilities.ToArray();
        foreach (var definition in definitions)
        {
            var ownerIndex = Array.FindIndex(result, ability => ability.Sequence == definition.AbilitySequence);
            if (ownerIndex < 0)
                throw new InvalidOperationException(
                    $"单段动效 {cardId}/{definition.RuntimeAbilityId} 缺少能力序号 {definition.AbilitySequence}");
            var owner = result[ownerIndex];
            if (!owner.Trigger.Equals("active", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"单段动效 {cardId}/{definition.RuntimeAbilityId} 必须归属 active 能力，实际为 {owner.Trigger}");

            var wholeScenes = owner.Presentations.Select((scene, index) => (scene, index))
                .Where(item => item.scene.EventType == "effect" && item.scene.Flow is null)
                .ToArray();
            if (wholeScenes.Length != 1 || owner.Presentations.Any(scene => scene.Flow is not null))
                throw new InvalidOperationException(
                    $"单段动效 {cardId}/{definition.RuntimeAbilityId} 不是唯一未分段场景，必须改用组合计划");

            var scenes = owner.Presentations.ToArray();
            var (whole, sceneIndex) = wholeScenes[0];
            scenes[sceneIndex] = whole with
            {
                Label = "第1/1段 实际结算",
                Flow = Flow,
                SegmentIndex = 1,
                SegmentCount = 1,
            };
            result[ownerIndex] = owner with { Presentations = scenes };
        }

        L12EffectPresentationVariants.ValidateConfiguration(
            result.SelectMany(ability => ability.Presentations));
        return result;
    }

    internal static bool TryResolveScene(L12AtomicCardEffect card, string trigger,
        string? runtimeAbilityId, out string? sceneId)
    {
        sceneId = null;
        if (!trigger.Equals("active", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(runtimeAbilityId)) return false;
        var definition = Definitions.SingleOrDefault(item => item.CardId.Equals(card.CardId,
            StringComparison.OrdinalIgnoreCase) && item.RuntimeAbilityId.Equals(runtimeAbilityId,
            StringComparison.OrdinalIgnoreCase));
        if (definition is null) return false;

        var matches = card.Abilities.Where(ability => ability.Sequence == definition.AbilitySequence)
            .SelectMany(ability => ability.Presentations)
            .Where(scene => scene.EventType == "effect" && scene.Flow == Flow
                && scene.SegmentIndex == 1 && scene.SegmentCount == 1)
            .Take(2).ToArray();
        if (matches.Length != 1) return false;
        sceneId = matches[0].SceneId;
        return true;
    }
}
