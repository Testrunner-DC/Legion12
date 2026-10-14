namespace TwelveLegions.Server;

// 阵亡原因用于卡牌触发资格；是否产生印刷【击杀时】仍由 L12KillSourceEvent 单独决定。
// “在战斗流程中”不是阵亡原因，战斗内的卡牌效果击杀仍是 CardEffect。
internal enum L12DefeatCause
{
    CardEffect,
    Combat,
}

internal enum L12KillSourceKind
{
    CombatDamage,
    CardEffect,
    StateBasedCardEffect,
}

/// <summary>
/// 击杀来源与“是否触发印刷【击杀时】”是两个正交事实。FAQ53 的震击连带阵亡
/// 是来源卡造成的击杀，但不是最原始战斗击杀，因此只能消费“下一次击杀后”赋予效果。
/// </summary>
internal sealed record L12KillSourceEvent(
    string EventId,
    L12KillSourceKind Kind,
    int SourceController,
    string SourceInstanceId,
    string SourceCardId,
    bool TriggersPrintedKillTiming,
    bool CausedBySourceCard,
    IReadOnlyList<string> TargetInstanceIds,
    bool SourceWasAttackingLegion = false);

internal sealed record L12PendingKillSourceEvent(L12KillSourceEvent Event);

public sealed partial class L12GameEngine
{
    private static string DefeatTriggerCause(L12DefeatCause cause) => cause switch
    {
        L12DefeatCause.CardEffect => "effect",
        L12DefeatCause.Combat => "combat",
        _ => throw new ArgumentOutOfRangeException(nameof(cause)),
    };

    private static L12DefeatCause EffectLethalDefeatCause(L12Prompt prompt)
    {
        // 旧检查点没有该字段。唯一既有战斗规则入口是支援者阵亡，旧载荷保存
        // 这一具名原因；其余本入口来自效果。不以当前 PendingDefense 推断来源。
        if (!prompt.Data.TryGetValue("defeatCause", out var stored))
            return prompt.Data.GetValueOrDefault("reason") == "作为支援军团阵亡"
                ? L12DefeatCause.Combat : L12DefeatCause.CardEffect;
        return stored switch
        {
            nameof(L12DefeatCause.CardEffect) => L12DefeatCause.CardEffect,
            nameof(L12DefeatCause.Combat) => L12DefeatCause.Combat,
            _ => throw new InvalidOperationException("致命替代检查点含有未知阵亡原因"),
        };
    }

    private const string PiercingAttackingLegionFact = "sourceWasAttackingLegion";
    private readonly List<L12PendingKillSourceEvent> _pendingKillSourceEvents = [];
    private readonly HashSet<string> _consumedGrantedKillSourceEvents = new(StringComparer.Ordinal);

    private static bool HasEligiblePiercingTriggerFacts(IReadOnlyDictionary<string, string> data)
        => data.GetValueOrDefault("killed") == "true"
            && data.GetValueOrDefault("combatKillConfirmed") == "true"
            && data.GetValueOrDefault(PiercingAttackingLegionFact) == "true";

    private void RecordPotentialStateBasedSourceKills(L12StackItem item, L12CardInstance source,
        IEnumerable<L12CardInstance> targets)
    {
        var targetIds = targets.Select(card => card.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (targetIds.Length == 0) return;
        var eventId = $"effect-kill:{item.StackItemId}:{source.InstanceId}";
        var existing = _pendingKillSourceEvents.FirstOrDefault(pending => pending.Event.EventId == eventId);
        if (existing is not null)
        {
            var merged = existing.Event.TargetInstanceIds.Concat(targetIds)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            _pendingKillSourceEvents.Remove(existing);
            _pendingKillSourceEvents.Add(new(existing.Event with { TargetInstanceIds = merged }));
            return;
        }
        _pendingKillSourceEvents.Add(new(new L12KillSourceEvent(
            eventId,
            L12KillSourceKind.StateBasedCardEffect,
            item.Controller,
            source.InstanceId,
            source.CardId,
            TriggersPrintedKillTiming: false,
            CausedBySourceCard: true,
            targetIds)));
    }

    private void ResolvePendingStateBasedKillSources(
        IReadOnlyCollection<(int Controller, L12CardInstance Card, L12CardInstance SourceSnapshot)> defeated)
    {
        if (_pendingKillSourceEvents.Count == 0) return;
        var defeatedIds = defeated.Select(entry => entry.Card.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = _pendingKillSourceEvents.ToArray();
        _pendingKillSourceEvents.Clear();
        foreach (var entry in pending)
        {
            var attributed = entry.Event.TargetInstanceIds.Where(defeatedIds.Contains).ToArray();
            if (attributed.Length == 0) continue;
            ResolveTypedKillSourceEvent(entry.Event with { TargetInstanceIds = attributed });
        }
    }

    private void ClearPendingStateBasedKillSources() => _pendingKillSourceEvents.Clear();

    private void ResolveTypedKillSourceEvent(L12KillSourceEvent killEvent)
    {
        if (killEvent.TargetInstanceIds.Count == 0) return;
        var controller = State.Players[killEvent.SourceController];
        var source = FindOnField(controller, killEvent.SourceInstanceId, out _, out _)
            ?? controller.Resolving.FirstOrDefault(card => card.InstanceId == killEvent.SourceInstanceId);
        if (source is null) return;

        AddEvent("kill-source", killEvent.SourceController,
            killEvent.TriggersPrintedKillTiming
                ? $"〈{source.Name}〉以最原始战斗伤害完成击杀"
                : $"〈{source.Name}〉的效果造成{killEvent.TargetInstanceIds.Count}张军团阵亡（FAQ53：不触发印刷【击杀时】）",
            source);
        TrackCardFact("kill", killEvent.SourceController, source, amount: killEvent.TargetInstanceIds.Count,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sourceKind"] = killEvent.Kind.ToString(),
                ["targetInstanceIds"] = string.Join('|', killEvent.TargetInstanceIds),
                ["triggersPrintedKillTiming"] = killEvent.TriggersPrintedKillTiming ? "true" : "false",
                [PiercingAttackingLegionFact] = killEvent.SourceWasAttackingLegion ? "true" : "false",
            });

        var candidates = new List<L12TriggerCandidate>();
        var isPrintedPiercing = L12StructuredCardRules.HasPrintedKeywordReference(source.CardId, "piercing");
        var printedKillTimingIsLegal = killEvent.TriggersPrintedKillTiming
            && (NativeCombatKillCards.Contains(source.CardId) || isPrintedPiercing)
            && (source.CardId != "S02-0002" || killEvent.SourceController == State.ActivePlayer);
        var hasGrantedPiercing = source.CardId == "S02-0608"
            && controller.UsedAbilities.Contains($"crusade-piercing:{source.InstanceId}:{State.TurnSerial}");
        var piercingRoleIsLegal = killEvent.SourceWasAttackingLegion;
        if ((printedKillTimingIsLegal && (!isPrintedPiercing || piercingRoleIsLegal))
            || (killEvent.TriggersPrintedKillTiming && hasGrantedPiercing && piercingRoleIsLegal))
        {
            var trigger = source.CardId == "S02-0002" ? "after-kill" : "after-attack";
            candidates.Add(CreateTriggerCandidate(killEvent.SourceController, source, trigger, "【击杀时】效果",
                new Dictionary<string, string>
                {
                    ["killed"] = "true",
                    ["combatKillConfirmed"] = "true",
                    ["defeatedInstanceId"] = string.Join('|', killEvent.TargetInstanceIds),
                    ["combatTiming"] = "kill",
                    ["killSourceKind"] = killEvent.Kind.ToString(),
                    [PiercingAttackingLegionFact] = killEvent.SourceWasAttackingLegion ? "true" : "false",
                }));
        }

        if (killEvent.CausedBySourceCard && source.ReadyAfterNextKillUntilTurn == State.TurnSerial
            && _consumedGrantedKillSourceEvents.Add(killEvent.EventId))
        {
            var sourceName = source.ReadyAfterNextKillSourceName ?? "效果";
            source.ReadyAfterNextKillUntilTurn = -1;
            source.ReadyAfterNextKillSourceName = null;
            candidates.Add(CreateTriggerCandidate(killEvent.SourceController, source, "forge-ready-after-kill",
                $"{sourceName}赋予的击杀后转为活跃效果",
                new Dictionary<string, string>
                {
                    ["source-name"] = sourceName,
                    ["combatTiming"] = killEvent.TriggersPrintedKillTiming ? "kill" : "effect-kill",
                    ["killSourceKind"] = killEvent.Kind.ToString(),
                    ["sourceKillCount"] = killEvent.TargetInstanceIds.Count.ToString(),
                }));
        }
        QueueTriggerCandidates(candidates);
    }
}
