namespace TwelveLegions.Server;

internal sealed record L12SingleSegmentResponseEffectPresentationDefinition(
    string CardId,
    int AbilitySequence,
    string RuntimeTrigger);

/// <summary>
/// 运行时响应使用 response-* / reaction 等堆叠时点，卡面原子则保留印刷时点。
/// 本目录显式连接二者，只接纳没有公开分支、没有独立后续段的单一结算响应。
/// </summary>
internal static class L12SingleSegmentResponseEffectPresentations
{
    internal const string Flow = "single-response";

    private static readonly L12SingleSegmentResponseEffectPresentationDefinition[] Definitions =
    [
        new("S01-0002", 2, "response-block"),
        new("S01-0018", 1, "response-negate"),
        new("S01-0019", 1, "reaction"),
        new("S02-0005", 2, "response-retarget-master"),
    ];

    internal static IReadOnlyList<L12SingleSegmentResponseEffectPresentationDefinition> All => Definitions;

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
                    $"单段响应 {cardId}/{definition.RuntimeTrigger} 缺少能力序号 {definition.AbilitySequence}");
            var owner = result[ownerIndex];
            if (owner.Presentations.Any(scene => scene.Flow == Flow
                    && scene.Trigger.Equals(RuntimeSceneKey(definition.RuntimeTrigger),
                        StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    $"单段响应 {cardId}/{definition.RuntimeTrigger} 重复登记");
            if (owner.Presentations.Any(scene => scene.Flow is not null))
                throw new InvalidOperationException(
                    $"单段响应 {cardId}/{definition.RuntimeTrigger} 已有结构化分支或后续段，必须改用组合计划");

            var sceneKey = RuntimeSceneKey(definition.RuntimeTrigger);
            var scene = new L12EffectPresentationScene(
                $"{owner.AbilityId}:presentation:{sceneKey}", owner.CardId, owner.AbilityId,
                sceneKey, owner.Text, EventType: "effect", Label: "第1/1段 响应实际结算",
                Flow: Flow, SegmentIndex: 1, SegmentCount: 1);
            result[ownerIndex] = owner with
            {
                Presentations = owner.Presentations.Concat([scene]).ToArray(),
            };
        }

        L12EffectPresentationVariants.ValidateConfiguration(
            result.SelectMany(ability => ability.Presentations));
        return result;
    }

    internal static bool TryResolveScene(L12AtomicCardEffect card, string runtimeTrigger,
        out string? sceneId)
    {
        sceneId = null;
        var definition = Definitions.SingleOrDefault(item => item.CardId.Equals(card.CardId,
            StringComparison.OrdinalIgnoreCase) && item.RuntimeTrigger.Equals(runtimeTrigger,
            StringComparison.OrdinalIgnoreCase));
        if (definition is null) return false;
        var sceneKey = RuntimeSceneKey(runtimeTrigger);
        var matches = card.Abilities.Where(ability => ability.Sequence == definition.AbilitySequence)
            .SelectMany(ability => ability.Presentations)
            .Where(scene => scene.EventType == "effect" && scene.Flow == Flow
                && scene.Trigger.Equals(sceneKey, StringComparison.OrdinalIgnoreCase)
                && scene.SegmentIndex == 1 && scene.SegmentCount == 1)
            .Take(2).ToArray();
        if (matches.Length != 1) return false;
        sceneId = matches[0].SceneId;
        return true;
    }

    private static string RuntimeSceneKey(string runtimeTrigger)
        => $"runtime-response:{runtimeTrigger}";
}
