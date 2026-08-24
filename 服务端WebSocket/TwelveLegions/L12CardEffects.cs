namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private static readonly HashSet<string> ImmediateEnterCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "S01-0101", "S01-0102", "S01-0103", "S01-0105", "S01-0108", "S01-0109", "S01-0117",
        "S01-0401", "S01-0404", "S01-0405", "S01-0410", "S01-0413", "S01-0415", "S01-0416", "S01-0417",
    };

    private static readonly HashSet<string> ImmediateTactics = new(StringComparer.OrdinalIgnoreCase)
    {
        "S01-0012", "S01-0015", "S01-0118", "S01-0119", "S01-0418", "S01-0419",
    };

    private static bool HasImmediateEffect(L12CardInstance card, string trigger)
        => trigger == "enter" ? ImmediateEnterCards.Contains(card.CardId) || HasS1ExtendedImmediateEffect(card.CardId, trigger)
                || HasS2UniversalImmediateEffect(card.CardId, trigger)
                || HasS2FactionImmediateEffect(card.CardId, trigger)
            : ImmediateTactics.Contains(card.CardId) || HasS1ExtendedImmediateEffect(card.CardId, trigger)
                || HasS2UniversalImmediateEffect(card.CardId, trigger)
                || HasS2FactionImmediateEffect(card.CardId, trigger);

    private void ResolveOnPlayContinuousEffects(int playerIndex, L12CardInstance card)
    {
        var player = State.Players[playerIndex];
        if (card.CardType == "legion" && card.Faction == "asgard"
            && player.UsedAbilities.Contains($"s2-thor-charge:{State.TurnSerial}"))
        {
            card.HasCharge = true;
            AddEvent("effect", playerIndex, $"{card.Name}获得雷神索尔赋予的冲锋", card);
        }
        if (card.CardType != "legion" || player.NextLegionChargeMaxCost is not int maxCost || card.CurrentCost > maxCost) return;
        player.NextLegionChargeMaxCost = null;
        card.HasCharge = true;
        AddEvent("effect", playerIndex, $"{card.Name} 获得〈全军出击〉赋予的冲锋", card);
    }

    private void ResolveCardEffect(L12StackItem item)
    {
        if (TryResolveVerifiedAtomicProgram(item)) return;
        switch (item.Trigger)
        {
            case "enter": ResolveEnterEffect(item); break;
            case "promotion-enter": ResolveS2PromotionEnter(item); break;
            case "play": ResolveTacticEffect(item); break;
            case "attack": ResolveAttackEffect(item); break;
            case "death": ResolveDeathEffect(item); break;
            case "leave": ResolveLeaveEffect(item); break;
            case "after-attack": ResolveAfterAttackEffect(item); break;
            case "after-damage": ResolveS1FactionAfterDamage(item); break;
            case "active": ResolveActiveEffect(item); break;
            case "reaction": ResolveS1ReactionEffect(item); break;
            case "s2-reaction": ResolveS2CounterEffect(item); break;
            case "authority-event": ResolveAuthorityEvent(item); break;
            case "disaster": ResolveDisasterEffect(item); break;
            case "s2-after-opponent-tactic": ResolveS2ExorcistReturn(item); break;
            case "discard-trigger": ResolveS2DiscardTrigger(item); break;
            case "forge-ready-after-kill": ResolveForgeReadyAfterKill(item); break;
            case "morrigan-enemy-death": ResolveS2MorriganEnemyDeath(item); break;
            case "nephthys-own-death": ResolveS2NephthysOwnDeath(item); break;
            case "master-morale-return": ResolveS2MasterMoraleReturn(item); break;
            case "medjed-master-damage": ResolveMedjedMasterDamageReaction(item); break;
            case "trojan-after-attack": ResolveS2TrojanHorseAfterAttack(item); break;
            default: FinishStackItem(item); break;
        }
    }

    private L12CardInstance? FindSource(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        if (item.SourceCardId == player.MasterId)
            return CreateCard(player.MasterId, item.SourceInstanceId);
        if (item.SourceInstanceId == $"faction-{item.Controller}" && !string.IsNullOrWhiteSpace(item.SourceCardId))
            return CreateCard(item.SourceCardId, item.SourceInstanceId);
        var source = FindOnField(player, item.SourceInstanceId, out _, out _)
            ?? (player.Relic?.InstanceId == item.SourceInstanceId ? player.Relic : null)
            ?? player.ExtraRelics.FirstOrDefault(card => card.InstanceId == item.SourceInstanceId)
            ?? player.SpecialZones.Trials.FirstOrDefault(card => card.InstanceId == item.SourceInstanceId)
            ?? (player.Morale.FirstOrDefault(card => card.InstanceId == item.SourceInstanceId) is { } morale
                ? CreateCard(morale.IsGodPower ? "S02-05C1" : morale.CardId, morale.InstanceId)
                : null)
            ?? player.Resolving.FirstOrDefault(card => card.InstanceId == item.SourceInstanceId)
            ?? player.Hand.FirstOrDefault(card => card.InstanceId == item.SourceInstanceId)
            ?? player.Graveyard.LastOrDefault(card => card.InstanceId == item.SourceInstanceId)
            ?? (State.ActiveDisaster?.InstanceId == item.SourceInstanceId ? State.ActiveDisaster : null);
        if (source is not null) return source;
        return State.Players.Where(candidate => candidate.PlayerIndex != item.Controller)
            .SelectMany(candidate => candidate.Field.SelectMany(row => row).Where(card => card is not null).Cast<L12CardInstance>()
                .Concat(candidate.ExtraRelics)
                .Concat(candidate.Resolving)
                .Concat(candidate.Hand)
                .Concat(candidate.Graveyard)
                .Concat(candidate.Removed)
                .Concat(candidate.Relic is null ? [] : [candidate.Relic]))
            .FirstOrDefault(card => card.InstanceId == item.SourceInstanceId);
    }

    private void ResolveEnterEffect(L12StackItem item)
    {
        var card = FindSource(item);
        if (card is null) { FinishStackItem(item); return; }
        var player = State.Players[item.Controller];
        switch (card.CardId)
        {
            case "S01-0101": PromptOptionalEnemyLegion(item, "lubu-kill", "可返还 2 张士气：击杀对方 1 张天灾等级 1 或 2 的军团",
                target => target.DisasterLevel is 1 or 2, CanReturnMorale(player, 2)); return;
            case "S01-0102":
            {
                var choices = State.Players[1 - item.Controller].Field.SelectMany(row => row)
                    .Where(target => target is { Tapped: true }).Select(target => target!.InstanceId).ToList();
                if (!CanReturnMorale(player, 1) || choices.Count == 0) { FinishStackItem(item); return; }
                CreatePrompt(item.Controller, "optional-targets", "可返还 1 张士气：选择对方最多 2 张休整军团，下个对方重置阶段不能转为活跃",
                    choices, 0, Math.Min(2, choices.Count), "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "wuzetian-lock" });
                return;
            }
            case "S01-0103": BeginLiJingEffect(item); return;
            case "S01-0105": BeginLiuBeiEnter(item); return;
            case "S01-0108":
                if (CanReturnMorale(player, 1))
                    CreatePrompt(item.Controller, "optional", "是否返还 1 张士气，使花木兰获得冲锋？", ["yes", "no"], 1, 1,
                        "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "mulan-charge" });
                else FinishStackItem(item);
                return;
            case "S01-0405":
                if (!player.Field[0].Any(other => other is not null && other.InstanceId != card.InstanceId))
                {
                    card.HasCharge = true;
                    AddEvent("effect", item.Controller, "宫本武藏因我方前排没有其他军团而获得冲锋", card);
                }
                FinishStackItem(item); return;
            case "S01-0415":
                AddEvent("hidden-reveal", item.Controller, $"{card.Name}展示后发动隐匿", card);
                card.Hidden = true;
                FinishStackItem(item); return;
            case "S01-0416":
            {
                var choices = player.Field[0].Where(target => target is not null
                        && target.InstanceId != card.InstanceId && target.Faction == "gaotianyuan" && target.Troops <= 5000)
                    .Select(target => target!.InstanceId).ToArray();
                if (choices.Length == 0) { FinishStackItem(item); return; }
                CreatePrompt(item.Controller, "target", "选择我方前排 1 张其他兵力不高于 5000 的【高天原】军团，本回合兵力 +1000",
                    choices, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "inaihime-buff" });
                return;
            }
            case "S01-0417":
                PromptEnemyLegion(item, "kusanagi-enter-kill", "选择对方 1 张费用不高于 2 的军团并击杀",
                    target => target.CurrentCost <= 2, optional: false); return;
            default:
                if (!TryResolveS1ExtendedEnter(item, card) && !TryResolveS2UniversalEnter(item, card)
                    && !TryResolveS2FactionEnter(item, card)) FinishStackItem(item);
                return;
        }
    }

    private void ResolveTacticEffect(L12StackItem item)
    {
        var card = FindSource(item);
        if (card is null) { FinishStackItem(item); return; }
        var player = State.Players[item.Controller];
        switch (card.CardId)
        {
            case "S01-0015":
                if (!Draw(player, 1)) { SetWinner(1 - item.Controller, "议和谈判抽牌时牌库为空"); FinishStackItem(item); return; }
                CreatePrompt(1 - item.Controller, "opponent-confirm", "是否同意〈议和谈判〉？", ["agree", "refuse"], 1, 1,
                    "card-effect", item.StackItemId, isPrivate: false,
                    data: new Dictionary<string, string> { ["action"] = "peace-talk" });
                return;
            case "S01-0118":
            {
                var front = player.Field[0].Where(target => target is not null).Select(target => target!.InstanceId).ToArray();
                if (front.Length == 0) { FinishStackItem(item); return; }
                CreatePrompt(item.Controller, "target", "选择我方前排 1 张军团，本回合兵力 +2000", front, 1, 1,
                    "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "march-buff" });
                return;
            }
            case "S01-0417":
                CreatePrompt(item.Controller, "optional", "是否将离场的〈草薙剑〉放回牌库顶部？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "kusanagi-return-top" });
                return;
            case "S01-0119": BeginTopDeckReorder(item, 5, "observing-stars"); return;
            case "S01-0418":
                PromptEnemyLegion(item, "divine-punishment-kill", "选择对方 1 张费用不高于 7 的军团并击杀",
                    target => target.CurrentCost <= 7, optional: false); return;
            case "S01-0419": BeginOiranGift(item); return;
            default:
                if (!TryResolveS1ExtendedTactic(item, card) && !TryResolveS2UniversalTactic(item, card)
                    && !TryResolveS2FactionTactic(item, card)) FinishStackItem(item);
                return;
        }
    }

    private void ResolveAttackEffect(L12StackItem item)
    {
        var card = FindSource(item);
        if (card is null || State.PendingDefense is null) { FinishStackItem(item); return; }
        if (State.PendingDefense.SuppressAttackTriggers)
        {
            AddEvent("effect", item.Controller, "贯穿进攻不触发【进攻时】效果", card);
            FinishStackItem(item);
            return;
        }
        var player = State.Players[item.Controller];
        switch (card.CardId)
        {
            case "S01-0104":
                if (CanReturnMorale(player, 1))
                    CreatePrompt(item.Controller, "optional", "是否返还 1 张士气，使韩信本回合兵力 +1000 并获得强攻？", ["yes", "no"], 1, 1,
                        "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "hanxin-attack" });
                else FinishStackItem(item);
                return;
            case "S01-0106":
                if (CanReturnMorale(player, 1))
                    CreatePrompt(item.Controller, "optional", "是否返还 1 张士气，使关羽本回合兵力 +1000 并获得必中？", ["yes", "no"], 1, 1,
                        "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "guanyu-attack" });
                else FinishStackItem(item);
                return;
            case "S01-0401":
                foreach (var enemy in State.Players[1 - item.Controller].Field.SelectMany(row => row).Where(target => target is not null))
                    enemy!.CostModifier--;
                PromptEnemyLegion(item, "honda-kill-zero", "选择对方 1 张当前费用为 0 的军团并击杀",
                    target => target.CurrentCost == 0, optional: false);
                return;
            case "S01-0413":
            {
                var counters = State.Players[1 - item.Controller].Field[1]
                    .Where(target => target is { CardType: "tactic" }).Select(target => target!.InstanceId).ToArray();
                if (counters.Length == 0) { FinishStackItem(item); return; }
                CreatePrompt(item.Controller, "covered-counter", "选择对方后排 1 张覆盖的反击战术，本回合无法发动",
                    counters, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "hiromasa-disable" });
                return;
            }
            case "S01-0416":
            {
                var choices = player.Field[0].Where(target => target is not null
                        && target.InstanceId != card.InstanceId && target.Faction == "gaotianyuan" && target.Troops <= 5000)
                    .Select(target => target!.InstanceId).ToArray();
                if (choices.Length == 0) { FinishStackItem(item); return; }
                CreatePrompt(item.Controller, "target", "选择我方前排 1 张其他兵力不高于 5000 的【高天原】军团，本回合兵力 +1000",
                    choices, 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "inaihime-buff" });
                return;
            }
            default:
                if (!TryResolveS1ExtendedAttack(item, card) && !TryResolveS2FactionAttack(item, card)) FinishStackItem(item);
                return;
        }
    }

    private void ResolveDeathEffect(L12StackItem item)
    {
        var card = FindSource(item);
        if (card is null) { FinishStackItem(item); return; }
        switch (card.CardId)
        {
            case "S01-0108":
            {
                if (State.ActivePlayer == item.Controller) { FinishStackItem(item); return; }
                if (item.Data.TryGetValue("declaredTargets", out var declared))
                {
                    var morale = State.Players[1 - item.Controller].Morale.FirstOrDefault(card => card.InstanceId == declared);
                    if (morale is not null) morale.CannotUntapUntilRound = State.Round + 1;
                    FinishStackItem(item); return;
                }
                var choices = State.Players[1 - item.Controller].Morale.Where(morale => morale.Tapped).Select(morale => morale.InstanceId).ToArray();
                if (choices.Length == 0) { FinishStackItem(item); return; }
                CreatePrompt(item.Controller, "target-morale", "选择对方 1 张休整士气，使其下个重置阶段无法转为活跃", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "mulan-lock-morale" });
                return;
            }
            default:
                if (!TryResolveS1ExtendedDeath(item, card) && !TryResolveS2FactionDeath(item, card)) FinishStackItem(item);
                return;
        }
    }

    private void ResolveLeaveEffect(L12StackItem item)
    {
        var card = FindSource(item);
        if (card?.CardId == "S01-0204" && TryResolveS1FactionDeath(item, card)) return;
        FinishStackItem(item);
    }

    private void ResolveAfterAttackEffect(L12StackItem item)
    {
        var card = FindSource(item);
        if (card is null) { FinishStackItem(item); return; }
        var player = State.Players[item.Controller];
        switch (card.CardId)
        {
            case "S01-0101":
                if (CanReturnMorale(player, 4))
                    CreatePrompt(item.Controller, "optional", "是否返还 4 张士气，将吕布转为活跃？", ["yes", "no"], 1, 1,
                        "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "lubu-ready" });
                else FinishStackItem(item);
                return;
            case "S01-0414":
                CreatePrompt(item.Controller, "optional", "是否将桂小五郎返回牌库顶部？", ["yes", "no"], 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "katsura-return" });
                return;
            default:
                if (!TryResolveS1ExtendedAfterAttack(item, card) && !TryResolveS2UniversalAfterAttack(item, card)
                    && !TryResolveS2FactionAfterAttack(item, card)) FinishStackItem(item);
                return;
        }
    }

    private void QueueAfterAttackEffects(int playerIndex, L12CardInstance attacker, bool killedTarget)
    {
        if (State.Phase == L12Phase.GameOver) return;
        if (FindOnField(State.Players[playerIndex], attacker.InstanceId, out _, out _) is null) return;
        var candidates = new List<L12TriggerCandidate>();
        if (attacker.CardId is "S01-0101" or "S01-0414" or "S01-0409" || S1ExtendedAfterAttackCards.Contains(attacker.CardId)
            || IsS1FactionAfterAttackCard(attacker.CardId) || S2UniversalAfterAttackCards.Contains(attacker.CardId)
            || IsS2FactionAfterAttackCard(attacker.CardId))
            candidates.Add(CreateTriggerCandidate(playerIndex, attacker, "after-attack", "【进攻后】效果",
                new Dictionary<string, string> { ["killed"] = killedTarget ? "true" : "false" }));
        if (killedTarget && attacker.ReadyAfterNextKillUntilTurn == State.TurnSerial)
        {
            var sourceName = attacker.ReadyAfterNextKillSourceName ?? "效果";
            attacker.ReadyAfterNextKillUntilTurn = -1;
            attacker.ReadyAfterNextKillSourceName = null;
            candidates.Add(CreateTriggerCandidate(playerIndex, attacker, "forge-ready-after-kill", $"{sourceName}赋予的击杀后转为活跃效果",
                new Dictionary<string, string> { ["source-name"] = sourceName }));
        }
        QueueTriggerCandidates(candidates);
    }

    private void ResolveForgeReadyAfterKill(L12StackItem item)
    {
        var card = FindSource(item);
        var sourceName = item.Data.GetValueOrDefault("source-name") ?? "效果";
        if (card is not null) ReadyCardByEffect(item.Controller, card, card, $"{card.Name}因{sourceName}转为活跃");
        FinishStackItem(item);
    }

    private void PromptOptionalEnemyLegion(L12StackItem item, string action, string text,
        Func<L12CardInstance, bool> predicate, bool canPay)
    {
        if (!canPay) { FinishStackItem(item); return; }
        PromptEnemyLegion(item, action, text, predicate, optional: true);
    }

    private void PromptEnemyLegion(L12StackItem item, string action, string text,
        Func<L12CardInstance, bool> predicate, bool optional)
    {
        var choices = State.Players[1 - item.Controller].Field.SelectMany(row => row)
            .Where(target => target is not null && !target.Hidden && predicate(target))
            .Select(target => target!.InstanceId).ToList();
        if (choices.Count == 0) { FinishStackItem(item); return; }
        if (optional) choices.Add("skip");
        CreatePrompt(item.Controller, "target", text, choices, 1, 1, "card-effect", item.StackItemId,
            data: new Dictionary<string, string> { ["action"] = action });
    }
}
