namespace TwelveLegions.Server;

/// <summary>
/// 进攻的唯一可恢复时间线。每个阶段都先写入状态，再建立 Prompt/Stack；
/// 因此断线重连与任一响应结算后都可以从安全边界继续，而不会重复伤害或触发。
/// </summary>
public sealed partial class L12GameEngine
{
    internal static readonly HashSet<string> NativeCombatKillCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S01-0409", "S02-0002", "S02-0503", "S02-0602", "S02-0606", "S02-0611",
    };

    internal static readonly HashSet<string> AttackerAfterAttackCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S01-0101", "S01-0311", "S01-0414",
    };

    internal static readonly HashSet<string> DefenderAfterAttackCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S01-0017", "S01-0213", "S01-0420", "S02-0523",
    };

    private static int EffectiveAttackValue(L12PendingDefense pending, L12CardInstance attacker)
        => pending.AttackValue > 0 ? pending.AttackValue : attacker.Troops;

    private bool CombatTimelineIsIdle()
        => !State.IsResolvingStack
            && State.EffectStack.Count == 0
            && State.DeferredEffectStack.Count == 0
            && State.PendingTriggerBatches.Count == 0
            && State.PendingTriggerStackCandidates.Count == 0
            && State.PendingActivations.Count == 0
            && State.PendingPrompts.Count == 0
            && State.ResponseWindow is null;

    private void AdvanceCombatTimelineIfIdle()
    {
        if (!CombatTimelineIsIdle() || State.Phase == L12Phase.GameOver) return;
        while (State.PendingDefense is { } pending && CombatTimelineIsIdle())
        {
            if (pending.Stage is L12CombatStage.AttackerAttackTiming
                or L12CombatStage.DefenderAttackTiming
                or L12CombatStage.DefenseChoice
                or L12CombatStage.CombatDamage)
            {
                if (TryAbortCombatAtSafeBoundary(pending, 1 - pending.AttackerPlayer)) continue;
            }

            switch (pending.Stage)
            {
                case L12CombatStage.AttackerAttackTiming:
                    pending.Stage = L12CombatStage.DefenderAttackTiming;
                    AddEvent("combat-stage", pending.AttackerPlayer,
                        "进攻方【进攻时】效果及其响应已全部结算，进入防守方【对方进攻时】");
                    continue;

                case L12CombatStage.DefenderAttackTiming:
                    if (pending.BlockedByResponse)
                    {
                        pending.Stage = L12CombatStage.AttackerAfterAttack;
                        AddEvent("attack-ended", 1 - pending.AttackerPlayer,
                            "响应效果抵挡本次进攻；已结算的进攻时效果不回退");
                        continue;
                    }
                    if (!pending.DefenderAttackTimingOpened)
                    {
                        pending.DefenderAttackTimingOpened = true;
                        OpenDefenderAttackTiming(pending);
                        return;
                    }
                    var attacker = FindOnField(State.Players[pending.AttackerPlayer], pending.AttackerInstanceId,
                        out _, out _);
                    if (attacker is null) continue;
                    pending.AttackValue = Math.Max(0, attacker.Troops);
                    pending.Stage = L12CombatStage.DefenseChoice;
                    State.Phase = L12Phase.Defense;
                    AddEvent("combat-stage", 1 - pending.AttackerPlayer,
                        $"防守方【对方进攻时】响应已全部结算；本次进攻数值冻结为 {pending.AttackValue}，进入抵挡/支援");
                    if (AutoResolveLegionDefenseWithoutSupport()) return;
                    return;

                case L12CombatStage.DefenseChoice:
                case L12CombatStage.CombatDamage:
                    return;

                case L12CombatStage.KillTriggers:
                    if (!pending.StageEffectsQueued)
                    {
                        pending.StageEffectsQueued = true;
                        QueueCombatKillTriggers(pending, pending.AttackerPlayer, pending.AttackerInstanceId,
                            pending.DefeatedDefenderInstanceId);
                        if (!CombatTimelineIsIdle()) return;
                    }
                    pending.StageEffectsQueued = false;
                    pending.Stage = pending.DefeatedAttackerInstanceId is null
                        ? L12CombatStage.DefenderDeathTriggers
                        : L12CombatStage.DefenderKillTriggers;
                    continue;

                case L12CombatStage.DefenderKillTriggers:
                    if (!pending.StageEffectsQueued)
                    {
                        pending.StageEffectsQueued = true;
                        QueueCombatKillTriggers(pending, 1 - pending.AttackerPlayer,
                            pending.Target.InstanceId, pending.DefeatedAttackerInstanceId);
                        if (!CombatTimelineIsIdle()) return;
                    }
                    pending.StageEffectsQueued = false;
                    pending.Stage = L12CombatStage.AttackerDeathTriggers;
                    continue;

                case L12CombatStage.AttackerDeathTriggers:
                    if (!pending.StageEffectsQueued)
                    {
                        pending.StageEffectsQueued = true;
                        QueueCombatDeathTriggers(pending.AttackerPlayer, pending.DefeatedAttackerInstanceId);
                        if (!CombatTimelineIsIdle()) return;
                    }
                    pending.StageEffectsQueued = false;
                    pending.Stage = pending.DefeatedDefenderInstanceId is null
                        ? L12CombatStage.FinalizeDeaths
                        : L12CombatStage.DefenderDeathTriggers;
                    continue;

                case L12CombatStage.DefenderDeathTriggers:
                    if (!pending.StageEffectsQueued)
                    {
                        pending.StageEffectsQueued = true;
                        QueueCombatDeathTriggers(1 - pending.AttackerPlayer, pending.DefeatedDefenderInstanceId);
                        if (!CombatTimelineIsIdle()) return;
                    }
                    pending.StageEffectsQueued = false;
                    pending.Stage = L12CombatStage.FinalizeDeaths;
                    continue;

                case L12CombatStage.FinalizeDeaths:
                    FinalizeCombatDeath(pending.AttackerPlayer, pending.DefeatedAttackerInstanceId);
                    FinalizeCombatDeath(1 - pending.AttackerPlayer, pending.DefeatedDefenderInstanceId);
                    pending.Stage = L12CombatStage.AttackerAfterAttack;
                    continue;

                case L12CombatStage.AttackerAfterAttack:
                {
                    var survivingAttacker = FindOnField(State.Players[pending.AttackerPlayer],
                        pending.AttackerInstanceId, out _, out _);
                    RevertPendingCombatTroopsModifiers(pending, survivingAttacker);
                    if (!pending.AttackerAfterAttackStarted)
                    {
                        pending.AttackerAfterAttackStarted = true;
                        if (survivingAttacker is not null)
                            ReturnWukongMasterLegionAfterAttack(pending.AttackerPlayer, survivingAttacker);
                        if (!CombatTimelineIsIdle()) return;
                    }
                    if (!pending.StageEffectsQueued)
                    {
                        pending.StageEffectsQueued = true;
                        if (survivingAttacker is not null)
                            QueueAttackerAfterAttackTriggers(pending.AttackerPlayer, survivingAttacker);
                        if (!CombatTimelineIsIdle()) return;
                    }
                    pending.StageEffectsQueued = false;
                    pending.Stage = L12CombatStage.DefenderAfterAttack;
                    continue;
                }

                case L12CombatStage.DefenderAfterAttack:
                    if (!pending.StageEffectsQueued)
                    {
                        pending.StageEffectsQueued = true;
                        QueueDefenderAfterAttackTriggers(pending.AttackerPlayer);
                        if (!CombatTimelineIsIdle()) return;
                    }
                    pending.StageEffectsQueued = false;
                    pending.Stage = L12CombatStage.Complete;
                    continue;

                case L12CombatStage.Complete:
                    CompleteCurrentCombat(pending);
                    continue;
            }
        }
    }

    private void OpenDefenderAttackTiming(L12PendingDefense pending)
    {
        var attacker = FindOnField(State.Players[pending.AttackerPlayer], pending.AttackerInstanceId, out _, out _);
        if (attacker is null) return;
        var item = new L12StackItem
        {
            StackItemId = $"stack-{++State.StackSequence}",
            Controller = pending.AttackerPlayer,
            SourceInstanceId = attacker.InstanceId,
            SourceCardId = attacker.CardId,
            SourceName = attacker.Name,
            Trigger = "opponent-attack",
            Text = "防守方【对方进攻时】响应时点",
        };
        item.Data["combatTiming"] = "defender-attack";
        State.EffectStack.Add(item);
        AddEvent("combat-stage", 1 - pending.AttackerPlayer, "防守方取得【对方进攻时】响应时点", attacker);
        BeginStackItem(item);
    }

    private bool TryAbortCombatAtSafeBoundary(L12PendingDefense pending, int eventPlayer)
    {
        if (!ReferenceEquals(State.PendingDefense, pending)) return false;
        var attacker = FindOnField(State.Players[pending.AttackerPlayer], pending.AttackerInstanceId, out _, out _);
        var targetExists = pending.Target.Type == "master"
            || FindOnField(State.Players[1 - pending.AttackerPlayer], pending.Target.InstanceId, out _, out _) is not null;
        if (attacker is not null && targetExists) return false;

        RevertPendingCombatTroopsModifiers(pending, attacker);
        var reason = attacker is null ? "进攻军团已离场" : "被进攻军团已离场";
        AddEvent("attack-aborted", eventPlayer, $"{reason}，本次进攻在当前安全边界自动结束并返回主要阶段");
        FinishCurrentCombatContext();
        return true;
    }

    private void CompleteCurrentCombat(L12PendingDefense pending)
    {
        var attacker = FindOnField(State.Players[pending.AttackerPlayer], pending.AttackerInstanceId, out _, out _);
        RevertPendingCombatTroopsModifiers(pending, attacker);
        AddEvent("attack-ended", pending.AttackerPlayer, "本次进攻的【进攻后】与防守方结束效果已全部结算");
        FinishCurrentCombatContext();
    }

    private void FinishCurrentCombatContext()
    {
        State.PendingDefense = null;
        if (State.SuspendedCombatContexts.Count > 0)
        {
            var index = State.SuspendedCombatContexts.Count - 1;
            State.PendingDefense = State.SuspendedCombatContexts[index];
            State.SuspendedCombatContexts.RemoveAt(index);
            State.Phase = L12Phase.Defense;
            AddEvent("combat-resume", State.PendingDefense.AttackerPlayer, "生成式进攻结束，恢复上一层交战时序");
            return;
        }
        if (State.Phase != L12Phase.GameOver) State.Phase = L12Phase.Main;
    }

    private void QueueCombatKillTriggers(L12PendingDefense pending, int killerController,
        string? killerInstanceId, string? defeatedInstanceId)
    {
        // 击杀时效果只能由本次交战的权威伤害结果建立。响应抵挡、支援、目标存活，
        // 或恢复自旧快照但缺少被击杀实例时，都不得仅凭后续的 killed 标记生成触发。
        if (pending.BlockedByResponse || string.IsNullOrWhiteSpace(killerInstanceId)
            || string.IsNullOrWhiteSpace(defeatedInstanceId)) return;
        var isAttackerKill = killerController == pending.AttackerPlayer;
        if (isAttackerKill
                ? killerInstanceId != pending.AttackerInstanceId
                  || defeatedInstanceId != pending.DefeatedDefenderInstanceId
                : killerInstanceId != pending.Target.InstanceId
                  || defeatedInstanceId != pending.DefeatedAttackerInstanceId)
            return;
        var killerPlayer = State.Players[killerController];
        var killer = FindOnField(killerPlayer, killerInstanceId, out _, out _)
            ?? killerPlayer.Resolving.FirstOrDefault(candidate => candidate.InstanceId == killerInstanceId);
        if (killer is null) return;
        ResolveTypedKillSourceEvent(new L12KillSourceEvent(
            $"combat-kill:{State.TurnSerial}:{pending.AttackerInstanceId}:{killerInstanceId}",
            L12KillSourceKind.CombatDamage,
            killerController,
            killer.InstanceId,
            killer.CardId,
            TriggersPrintedKillTiming: true,
            CausedBySourceCard: true,
            [defeatedInstanceId]));
    }

    private void QueueAttackerAfterAttackTriggers(int playerIndex, L12CardInstance attacker)
    {
        if (!AttackerAfterAttackCards.Contains(attacker.CardId)) return;
        QueueTriggerCandidates([
            CreateTriggerCandidate(playerIndex, attacker, "after-attack", "【进攻后】效果",
                new Dictionary<string, string> { ["killed"] = "false", ["combatTiming"] = "attacker-after-attack" }),
        ]);
    }

    private void QueueDefenderAfterAttackTriggers(int attackerPlayer)
        => QueueS1PostAttackReactions(attackerPlayer);

    private void QueueCombatDeathTriggers(int controller, string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return;
        var card = State.Players[controller].Resolving.FirstOrDefault(candidate => candidate.InstanceId == instanceId);
        if (card is null) return;
        var candidates = BuildS1LeaveReactionCandidates(controller, card).ToList();
        if (HasDeathTrigger(card))
            candidates.Add(CreateTriggerCandidate(controller, card, "death", "【阵亡时】效果",
                new Dictionary<string, string> { ["cause"] = "combat", ["combatTiming"] = "death" }));
        var morrigan = BuildMorriganEnemyDeathCandidate(controller);
        if (morrigan is not null) candidates.Add(morrigan);
        var nephthys = BuildNephthysOwnDeathCandidate(controller, card);
        if (nephthys is not null) candidates.Add(nephthys);
        var artemis = BuildArtemisRangedDeathCandidate(controller, card);
        if (artemis is not null) candidates.Add(artemis);
        QueueTriggerCandidates(candidates);
    }

    private bool IsPendingCombatDeath(string instanceId)
        => EnumerateCombatContexts().Any(context => context.DefeatedAttackerInstanceId == instanceId
            || context.DefeatedDefenderInstanceId == instanceId);

    private IEnumerable<L12PendingDefense> EnumerateCombatContexts()
    {
        if (State.PendingDefense is not null) yield return State.PendingDefense;
        foreach (var context in State.SuspendedCombatContexts) yield return context;
    }

    private void FinalizeCombatDeath(int battlefieldController, string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return;
        var battlefield = State.Players[battlefieldController];
        var card = battlefield.Resolving.FirstOrDefault(candidate => candidate.InstanceId == instanceId);
        if (card is null) return;
        battlefield.Resolving.Remove(card);
        var owner = CardOwner(card, battlefield);
        var promotionFoundations = DetachPromotionFoundations(card);
        if (card.AttachedCards.Count > 0) DiscardAttachedCards(card, $"{card.Name}阵亡");
        ResetCardAfterLeavingField(card);
        if (L12SpecialDeckRules.VanishesWhenLeavingField(card))
        {
            AddEvent("derived-vanished", owner.PlayerIndex,
                $"衍生卡〈{card.Name}〉在阵亡触发完成后消灭，不进入其他区域", card);
            MovePromotionFoundationsToZone(promotionFoundations, owner, "vanished", $"{card.Name}阵亡");
        }
        else
        {
            owner.Graveyard.Add(card);
            MovePromotionFoundationsToZone(promotionFoundations, owner, "graveyard", $"{card.Name}阵亡");
            AddEvent("grave", owner.PlayerIndex, $"{card.Name}的相关阵亡触发已完成，置入所有者墓地", card);
        }
        RecalculateContinuousTroops();
    }
}
