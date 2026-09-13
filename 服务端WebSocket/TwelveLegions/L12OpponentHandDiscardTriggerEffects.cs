namespace TwelveLegions.Server;

internal sealed record L12OpponentHandDiscardTriggerSpec(
    string CardId,
    int AbilitySequence,
    string Trigger,
    string Name,
    string? Condition,
    string SettlementText,
    string PromptText);

/// <summary>
/// 已核对为“触发后仅令对方选择并弃置1张手牌”的效果。触发条件只在候选建立时判断；
/// 手牌身份直到响应窗口结束后才向受影响玩家展示，且候选唯一时仍必须由该玩家点击。
/// </summary>
internal static class L12OpponentHandDiscardTriggerEffects
{
    internal const string Flow = "opponent-hand-discard-one";
    internal const string Continuation = "opponent-hand-discard-one-choice";

    internal static readonly L12OpponentHandDiscardTriggerSpec[] All =
    [
        new("S01-0209", 2, "enter", "纳芙蒂蒂", "opponent.hand>=6",
            "登场时 若对方手牌数量不低于6张，对方弃置1张手牌。",
            "纳芙蒂蒂：选择弃置1张手牌"),
        new("S01-0308", 2, "after-damage", "血斧艾瑞克", null,
            "此军团对对方主宰造成伤害时：对方弃置1张手牌。",
            "血斧艾瑞克：选择弃置1张手牌"),
        new("S02-0515", 2, "enter", "海伦", "controller.god-power>=1",
            "登场时 若我方神力为1张及以上，对方弃置1张手牌。",
            "海伦：选择弃置1张手牌"),
        new("S02-0605", 4, "death", "鲍斯", null,
            "阵亡时 对方弃置1张手牌。",
            "鲍斯：选择弃置1张手牌"),
        new("ST04-02", 1, "attack", "佐佐木小次郎", "controller.hand<=opponent.hand",
            "进攻时 若我方手牌数量不高于对方，对方弃置1张手牌。",
            "佐佐木小次郎：选择弃置1张手牌"),
    ];

    internal static L12OpponentHandDiscardTriggerSpec? Find(string cardId, string trigger)
        => All.SingleOrDefault(spec => spec.CardId.Equals(cardId, StringComparison.OrdinalIgnoreCase)
            && spec.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));
}

public sealed partial class L12GameEngine
{
    private bool PrepareOpponentHandDiscardTriggerCandidate(L12TriggerCandidate candidate)
    {
        var spec = L12OpponentHandDiscardTriggerEffects.Find(candidate.SourceCardId, candidate.Trigger);
        if (spec is null || candidate.Data.GetValueOrDefault("opponentDiscardConditionLocked") == "true")
            return true;

        var controller = State.Players[candidate.Controller];
        var opponent = State.Players[1 - candidate.Controller];
        var conditionMet = spec.Condition switch
        {
            null => true,
            "opponent.hand>=6" => opponent.Hand.Count >= 6,
            "controller.god-power>=1" => controller.Morale.Any(card => card.IsGodPower),
            "controller.hand<=opponent.hand" => controller.Hand.Count <= opponent.Hand.Count,
            _ => throw new InvalidOperationException(
                $"Unsupported opponent hand discard trigger condition: {spec.Condition}"),
        };
        if (!conditionMet || opponent.Hand.Count == 0) return false;

        candidate.Data["opponentDiscardConditionLocked"] = "true";
        candidate.Data["verifiedAtomicConditionLocked"] = "true";
        return true;
    }

    private bool TryResolveOpponentHandDiscardTrigger(L12StackItem item)
    {
        var spec = L12OpponentHandDiscardTriggerEffects.Find(item.SourceCardId, item.Trigger);
        if (spec is null) return false;

        var opponent = State.Players[1 - item.Controller];
        if (opponent.Hand.Count == 0)
        {
            RecordTargetSettlementFailure(item, "opponent.hand",
                $"〈{spec.Name}〉结算时对方手牌已空，无法选择弃置对象");
            FinishStackItem(item);
            return true;
        }

        CreateDelayedPublicResolutionPrompt(item, "hand-card", spec.PromptText,
            opponent.Hand.Select(card => card.InstanceId),
            L12OpponentHandDiscardTriggerEffects.Continuation, new(),
            isPrivate: true, chooser: opponent.PlayerIndex, min: 1, max: 1);
        return true;
    }

    private bool TryContinueOpponentHandDiscardTrigger(L12StackItem item, L12Prompt prompt,
        IReadOnlyCollection<string> chosen)
    {
        if (prompt.Data.GetValueOrDefault("action") != L12OpponentHandDiscardTriggerEffects.Continuation)
            return false;

        var spec = L12OpponentHandDiscardTriggerEffects.Find(item.SourceCardId, item.Trigger);
        var opponent = State.Players[1 - item.Controller];
        var target = chosen.Count == 1
            ? opponent.Hand.FirstOrDefault(card => chosen.Contains(card.InstanceId,
                StringComparer.OrdinalIgnoreCase))
            : null;
        if (spec is null || target is null)
        {
            RecordTargetSettlementFailure(item, chosen.FirstOrDefault(),
                spec is null
                    ? "弃牌效果缺少结构化定义"
                    : $"〈{spec.Name}〉选择的对方手牌已不在手牌中");
            FinishStackItem(item);
            return true;
        }

        MoveHandToGrave(opponent, target.InstanceId, causedByEffect: true,
            FindSource(item) ?? item.SourceSnapshot);
        FinishStackItem(item);
        return true;
    }
}
