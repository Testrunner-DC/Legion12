namespace TwelveLegions.Server;

internal sealed record L12SingleSegmentTriggeredEffectPresentationDefinition(
    string CardId,
    int AbilitySequence,
    string RuntimeTrigger);

/// <summary>
/// 已核对为单段、单一结算结果的公开触发效果。印刷时点与运行时触发键在这里显式连接，
/// 选择不发动不建立空堆叠；一旦入栈，成功、无效和逆序失效均使用同一场景身份。
/// </summary>
internal static class L12SingleSegmentTriggeredEffectPresentations
{
    internal const string Flow = "single-trigger";

    private static readonly L12SingleSegmentTriggeredEffectPresentationDefinition[] Definitions =
    [
        new("S02-0001", 1, "s2-after-opponent-tactic"),
    ];

    internal static IReadOnlyList<L12SingleSegmentTriggeredEffectPresentationDefinition> All => Definitions;

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
                    $"单段触发 {cardId}/{definition.RuntimeTrigger} 缺少能力序号 {definition.AbilitySequence}");
            var owner = result[ownerIndex];
            if (owner.Trigger is "active" or "static" or "continuous")
                throw new InvalidOperationException(
                    $"单段触发 {cardId}/{definition.RuntimeTrigger} 不能归属 {owner.Trigger} 能力");

            var wholeScenes = owner.Presentations.Select((scene, index) => (scene, index))
                .Where(item => item.scene.EventType == "effect" && item.scene.Flow is null)
                .ToArray();
            if (wholeScenes.Length != 1 || owner.Presentations.Any(scene => scene.Flow is not null))
                throw new InvalidOperationException(
                    $"单段触发 {cardId}/{definition.RuntimeTrigger} 不是唯一未分段场景，必须改用组合计划");

            var scenes = owner.Presentations.ToArray();
            var (whole, sceneIndex) = wholeScenes[0];
            scenes[sceneIndex] = whole with
            {
                Trigger = RuntimeSceneKey(definition.RuntimeTrigger),
                Label = "第1/1段 触发实际结算",
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
        => $"runtime-trigger:{runtimeTrigger}";
}
