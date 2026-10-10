namespace TwelveLegions.Server;

internal sealed record L12SingleSegmentTriggeredEffectPresentationDefinition(
    string CardId,
    int AbilitySequence,
    string RuntimeTrigger,
    string? SettlementText = null);

/// <summary>
/// 已核对为单段、单一结算结果的公开触发效果。印刷时点与运行时触发键在这里显式连接，
/// 选择不发动不建立空堆叠；一旦入栈，成功、无效和逆序失效均使用同一场景身份。
/// </summary>
internal static class L12SingleSegmentTriggeredEffectPresentations
{
    internal const string Flow = "single-trigger";

    private static readonly L12SingleSegmentTriggeredEffectPresentationDefinition[] Definitions =
    [
        .. L12SimpleDrawTriggerEffects.All.Select(spec =>
            new L12SingleSegmentTriggeredEffectPresentationDefinition(
                spec.CardId, spec.AbilitySequence, spec.Trigger, spec.SettlementText)),
        .. L12SimpleMasterHealTriggerEffects.All.Select(spec =>
            new L12SingleSegmentTriggeredEffectPresentationDefinition(
                spec.CardId, spec.AbilitySequence, spec.Trigger, spec.SettlementText)),
        .. L12SimpleTrialAdvanceTriggerEffects.All.Select(spec =>
            new L12SingleSegmentTriggeredEffectPresentationDefinition(
                spec.CardId, spec.AbilitySequence, spec.Trigger, spec.SettlementText)),
        .. L12SimpleCardStateTriggerEffects.All.Select(spec =>
            new L12SingleSegmentTriggeredEffectPresentationDefinition(
                spec.CardId, spec.AbilitySequence, spec.Trigger, spec.SettlementText)),
        .. L12SimpleSelfTroopBuffTriggerEffects.All.Select(spec =>
            new L12SingleSegmentTriggeredEffectPresentationDefinition(
                spec.CardId, spec.AbilitySequence, spec.Trigger, spec.SettlementText)),
        .. L12SimpleResourceTriggerEffects.All.Select(spec =>
            new L12SingleSegmentTriggeredEffectPresentationDefinition(
                spec.CardId, spec.AbilitySequence, spec.Trigger, spec.SettlementText)),
        .. L12OpponentHandDiscardTriggerEffects.All.Select(spec =>
            new L12SingleSegmentTriggeredEffectPresentationDefinition(
                spec.CardId, spec.AbilitySequence, spec.Trigger, spec.SettlementText)),
        new("S01-0112", 3, "death"),
        new("S01-0210", 3, "death"),
        new("S01-0304", 3, "death"),
        new("S01-0307", 2, "death"),
        new("S01-0308", 3, "death"),
        new("S01-0403", 2, "death"),
        new("S01-0407", 2, "death"),
        new("S02-0001", 1, "s2-after-opponent-tactic"),
        new("S02-0202", 2, "death"),
        new("S02-0305", 3, "master-damaged"),
        new("S02-0513", 2, "enter"),
        new("S02-0518", 2, "enter"),
        new("S02-0518", 3, "death"),
        new("S02-0520", 1, "enter"),
        new("S02-0601", 2, "death"),
        new("S02-04M1", 1, "friendly-legion-moves"),
        new("S02-04M1", 2, "friendly-back-to-front"),
        new("S02-04M1", 3, "friendly-front-to-back"),
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
            if (owner.Presentations.Any(scene => scene.Flow is not null)
                || wholeScenes.Length > 1
                || wholeScenes.Length == 0 && string.IsNullOrWhiteSpace(definition.SettlementText))
                throw new InvalidOperationException(
                    $"单段触发 {cardId}/{definition.RuntimeTrigger} 不是唯一未分段场景，必须改用组合计划");

            var sceneKey = RuntimeSceneKey(definition.RuntimeTrigger);
            L12EffectPresentationScene runtimeScene;
            if (wholeScenes.Length == 1)
            {
                var (whole, _) = wholeScenes[0];
                runtimeScene = whole with
                {
                    Trigger = sceneKey,
                    DefaultText = string.IsNullOrWhiteSpace(definition.SettlementText)
                        ? whole.DefaultText
                        : definition.SettlementText,
                    Label = "第1/1段 触发实际结算",
                    Flow = Flow,
                    SegmentIndex = 1,
                    SegmentCount = 1,
                };
            }
            else
            {
                runtimeScene = new L12EffectPresentationScene(
                    $"{owner.AbilityId}:presentation:{sceneKey.Replace(':', '-')}",
                    owner.CardId, owner.AbilityId, sceneKey, definition.SettlementText!,
                    EventType: "effect", Label: "第1/1段 触发实际结算", Flow: Flow,
                    SegmentIndex: 1, SegmentCount: 1);
            }
            var scenes = owner.Presentations.Where(scene => scene.EventType != "effect" || scene.Flow is not null)
                .Append(runtimeScene).ToArray();
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
