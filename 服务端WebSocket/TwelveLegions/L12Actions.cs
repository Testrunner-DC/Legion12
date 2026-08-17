namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private static bool HasOptionalSelfDamageEntryDiscount(L12CardInstance card)
        => card.EffectText?.Contains("可对我方主宰造成1点伤害：此军团登场费用-1", StringComparison.Ordinal) == true;

    private CommandResult PlayCard(int playerIndex, L12Command command)
    {
        if (!CanAct(playerIndex)) return CommandResult.Reject("只能在自己的主要阶段行动");
        var player = State.Players[playerIndex];
        var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == command.CardInstanceId);
        if (card is null) return CommandResult.Reject("卡牌不在手牌中");
        if (IsCounterTactic(card.CardId)) return SetCounterTactic(playerIndex, card, command);
        if (card.CardType == "legion" && (command.Row is null or < 0 or > 1 || command.Slot is null or < 0 or > 2))
            return CommandResult.Reject("请选择合法阵地");
        if (card.CardType == "legion" && State.ActiveDisaster?.CardId == "S01-DS03" && command.Row == 1)
            return CommandResult.Reject("《腐秽大地》持续期间后排无法放置军团");
        if (card.CardType == "legion" && player.Field[command.Row!.Value][command.Slot!.Value] is { } occupant
            && !(command.Row == 1 && IsCounterTactic(occupant.CardId)))
            return CommandResult.Reject("阵地已被占用");

        var mayUseSelfDamageDiscount = card.CardType == "legion" && HasOptionalSelfDamageEntryDiscount(card) && player.Hp > 1;
        if (mayUseSelfDamageDiscount && command.Choice?.StartsWith("self-damage-cost", StringComparison.Ordinal) != true
            && command.Choice?.StartsWith("normal-cost", StringComparison.Ordinal) != true)
        {
            var normalCost = GetPlayCost(playerIndex, card, useSelfDamageDiscount: false);
            var discountedCost = GetPlayCost(playerIndex, card, useSelfDamageDiscount: true);
            if (ActiveResourceCount(player) < discountedCost) return CommandResult.Reject("活跃士气不足");
            CreatePrompt(playerIndex, "optional", $"{card.Name}：是否对我方主宰造成1点伤害，使此军团登场费用-1？", ["yes", "no"], 1, 1,
                "play-cost-choice", data: new Dictionary<string, string>
                {
                    ["cardInstanceId"] = card.InstanceId,
                    ["row"] = command.Row!.Value.ToString(),
                    ["slot"] = command.Slot!.Value.ToString(),
                    ["normalCost"] = normalCost.ToString(),
                    ["discountedCost"] = discountedCost.ToString(),
                    ["choiceMode"] = "instant",
                    ["yes"] = $"是（主宰受到1点伤害，支付{discountedCost}士气）",
                    ["no"] = $"否（支付{normalCost}士气）",
                });
            return CommandResult.Ok();
        }

        var usedAsgardSelfDamageDiscount = command.Choice?.StartsWith("self-damage-cost", StringComparison.Ordinal) == true && mayUseSelfDamageDiscount;
        var cost = GetPlayCost(playerIndex, card, usedAsgardSelfDamageDiscount);
        if (ActiveResourceCount(player) < cost) return CommandResult.Reject("活跃士气不足");
        var paymentChoice = EnsureTombGuardPlayPaymentChoice(playerIndex, card, command, cost);
        if (paymentChoice is not null) return paymentChoice;
        var useTombGuards = command.Choice?.EndsWith("|tomb-guards", StringComparison.Ordinal) == true;
        if (!useTombGuards && ActiveMoraleCountWithoutTombGuards(player) < cost) return CommandResult.Reject("不使用陵墓守卫时活跃士气不足");

        if (card.CardType == "legion")
        {
            if (command.Row is null or < 0 or > 1 || command.Slot is null or < 0 or > 2)
                return CommandResult.Reject("请选择合法阵地");
            var row = command.Row.GetValueOrDefault();
            var slot = command.Slot.GetValueOrDefault();
            if (State.ActiveDisaster?.CardId == "S01-DS03" && row == 1)
                return CommandResult.Reject("〈腐秽大地〉持续期间后排无法放置军团");
            var occupyingCard = player.Field[row][slot];
            var displacesOwnCounter = row == 1 && occupyingCard is not null && IsCounterTactic(occupyingCard.CardId);
            if (occupyingCard is not null && !displacesOwnCounter) return CommandResult.Reject("阵地已被占用");
            TryConsumeMorale(player, cost, preferTombGuards: useTombGuards, allowTombGuards: useTombGuards);
            player.Hand.Remove(card);
            if (displacesOwnCounter)
            {
                occupyingCard!.Hidden = false;
                player.Graveyard.Add(occupyingCard);
                AddEvent("counter-displaced", playerIndex,
                    $"{player.Name} 打出军团并将自己覆盖的反击战术〈{occupyingCard.Name}〉置入墓地", occupyingCard);
            }
            card.SummonRound = State.Round;
            player.Field[row][slot] = card;
        }
        else if (card.CardType == "artifact")
        {
            TryConsumeMorale(player, cost, preferTombGuards: useTombGuards, allowTombGuards: useTombGuards);
            player.Hand.Remove(card);
            if (card.Name.Contains("卡诺匹斯", StringComparison.Ordinal) && player.Relic is not null)
            {
                player.ExtraRelics.Add(card);
            }
            else
            {
                if (player.Relic is not null)
                {
                    DiscardRelic(player, player.Relic);
                    AddEvent("leave", playerIndex, "原圣物离开圣物区");
                }
                player.Relic = card;
            }
        }
        else
        {
            TryConsumeMorale(player, cost, preferTombGuards: useTombGuards, allowTombGuards: useTombGuards);
            player.Hand.Remove(card);
            player.Resolving.Add(card);
        }

        ApplyDisasterLevelOnEntry(playerIndex, card, deferTriggerUntilStackSettles: false);
        AddEvent("play", playerIndex, $"{player.Name} 打出 {card.Name}", card);
        if (usedAsgardSelfDamageDiscount) DamageMaster(playerIndex, 1, $"{card.Name}的登场费用减免");
        if (card.CardType == "tactic" && !IsCounterTactic(card.CardId)) player.LastActiveTacticCardId = card.CardId;
        ResolveOnPlayContinuousEffects(playerIndex, card);
        RecalculateContinuousTroops();
        if (card.CardType == "legion" && card.Faction == player.Faction && player.NextFactionLegionDiscount > 0)
            player.NextFactionLegionDiscount = 0;
        if (card.CardType == "legion" && card.Faction == "taiyangcheng" && card.DisasterLevel > 0)
            player.NextS2SunDisasterLegionDiscount = 0;
        if (card.CardType == "legion" && card.Faction == "olympus" && player.NextS2OlympusLegionDiscount > 0)
            player.NextS2OlympusLegionDiscount = 0;
        if (card.CardType == "tactic" && player.FreeTacticCount > 0) player.FreeTacticCount--;

        var trigger = card.CardType is "legion" or "artifact" ? "enter" : "play";
        if (HasImmediateEffect(card, trigger))
        {
            State.CheckDisasterAfterStack = card.CardType == "legion" && State.DisasterValue > 8;
            PushEffect(playerIndex, card, trigger, trigger == "enter" ? "【登场时】效果" : "战术效果");
        }
        else
        {
            if (player.Resolving.Remove(card)) player.Graveyard.Add(card);
            if (card.CardType == "legion" && State.DisasterValue > 8) BeginDisasterTrigger(opening: false);
        }
        return CommandResult.Ok();
    }

    private void ApplyDisasterLevelOnEntry(int playerIndex, L12CardInstance card, bool deferTriggerUntilStackSettles)
    {
        if (card.CardType != "legion" || card.DisasterLevel <= 0) return;
        if (State.ActiveDisaster?.CardId == "S01-DS10")
        {
            State.DisasterValue = 0;
            return;
        }
        State.DisasterValue += card.DisasterLevel;
        AddEvent("disaster-value", playerIndex, $"天灾值增加至 {State.DisasterValue}");
        if (deferTriggerUntilStackSettles && State.DisasterValue > 8)
            State.CheckDisasterAfterStack = true;
    }

    private int GetPlayCost(int playerIndex, L12CardInstance card, bool useSelfDamageDiscount = false)
    {
        if (card.CardType == "tactic" && (State.Players[playerIndex].UsedAbilities.Contains("ds01-free-tactic")
            || State.Players[playerIndex].FreeTacticCount > 0)) return 0;
        if (card.CardType == "tactic" && IsCounterTactic(card.CardId)) return card.CurrentCost;
        var modifier = card.CostModifier;
        var player = State.Players[playerIndex];
        if (card.CardType == "tactic" && !IsCounterTactic(card.CardId)) modifier += player.NextActiveTacticSurcharge;
        if (card.CardId is "S01-0104" or "S01-0107" or "S01-0114"
            && State.Players[playerIndex].Morale.Count < State.Players[1 - playerIndex].Morale.Count)
            modifier--;
        if (card.CardId == "S01-0202" && !PublicLegions(player).Any(target => target.CardId == "S01-0212")) modifier -= 2;
        if (card.CardId == "S01-0301") modifier -= player.Graveyard.Count(target => target.CardType == "legion" && target.Faction == "asgard") / 4;
        if (card.CardId == "S01-0302") modifier -= PublicLegions(player).Count();
        if (card.CardId is "S01-0305" or "S01-0306" && player.Hp <= 6) modifier--;
        if (card.CardId == "S02-0202") modifier -= player.TombNamedLegionsLeftThisTurn;
        if (card.CardId == "S02-0203" && !PublicLegions(player).Any(target => target.CardId == "S01-0212")) modifier--;
        if (card.CardId == "S02-0605") modifier -= PublicLegions(player).Count(target => target.Faction == "otherworld");
        if (card.CardId == "S02-0611" && PublicLegions(player).Any(target => target.CardId == "S02-0612")) modifier -= 2;
        if (card.CardId == "S02-0612" && PublicLegions(player).Any(target => target.CardId == "S02-0611")) modifier -= 2;
        if (card.CardId is "S02-0512" or "S02-0518" && player.SpecialZones.GodPower.Count == 0) modifier--;
        if (card.CardId == "S02-0510" && player.SpecialZones.GodPower.Count >= 5) modifier -= 3;
        if (card.CardId == "S02-0601" && player.S2ArthurDiscountUntilTurn >= State.TurnSerial) modifier -= 3;
        if (useSelfDamageDiscount && HasOptionalSelfDamageEntryDiscount(card) && player.Hp > 1) modifier--;
        if (card.CardType == "legion" && card.Faction == player.Faction && player.NextFactionLegionDiscount > 0)
            modifier -= player.NextFactionLegionDiscount;
        if (card.CardType == "legion" && card.Faction == "taiyangcheng" && card.DisasterLevel > 0)
            modifier -= player.NextS2SunDisasterLegionDiscount;
        if (card.CardType == "legion" && card.Faction == "olympus")
            modifier -= player.NextS2OlympusLegionDiscount;
        return Math.Max(0, card.Cost + modifier);
    }

    private CommandResult SetCounterTactic(int playerIndex, L12CardInstance card, L12Command command)
    {
        if (command.Row != 1 || command.Slot is null or < 0 or > 2) return CommandResult.Reject("反击战术必须覆盖在后排阵地");
        var player = State.Players[playerIndex];
        var slot = command.Slot.Value;
        if (player.Field[1][slot] is { CardType: not "tactic" }) return CommandResult.Reject("该后排阵地已有军团");
        var cost = State.ActiveDisaster?.CardId == "S01-DS03" ? 0 : 2;
        if (ActiveResourceCount(player) < cost) return CommandResult.Reject("覆盖反击战术需要消耗 2 张活跃士气");
        var paymentChoice = EnsureTombGuardPlayPaymentChoice(playerIndex, card, command, cost);
        if (paymentChoice is not null) return paymentChoice;
        var useTombGuards = command.Choice?.EndsWith("|tomb-guards", StringComparison.Ordinal) == true;
        if (!useTombGuards && ActiveMoraleCountWithoutTombGuards(player) < cost) return CommandResult.Reject("不使用陵墓守卫时活跃士气不足");
        TryConsumeMorale(player, cost, preferTombGuards: useTombGuards, allowTombGuards: useTombGuards);
        player.Hand.Remove(card);
        if (player.Field[1][slot] is not null)
        {
            var old = player.Field[1][slot]!;
            old.Hidden = false;
            player.Graveyard.Add(old);
            AddEvent("counter-replaced", playerIndex, $"{old.Name} 被新的反击战术顶替并置入墓地", old);
        }
        card.Hidden = true;
        card.SetRound = State.Round;
        card.SummonRound = State.Round;
        player.Field[1][slot] = card;
        AddEvent("counter-set", playerIndex, $"{player.Name} 在后排覆盖 1 张反击战术");
        return CommandResult.Ok();
    }

    private CommandResult? EnsureTombGuardPlayPaymentChoice(int playerIndex, L12CardInstance card, L12Command command, int cost)
    {
        if (cost <= 0 || command.Choice?.Contains("|tomb-guards", StringComparison.Ordinal) == true
            || command.Choice?.Contains("|morale-only", StringComparison.Ordinal) == true) return null;
        var player = State.Players[playerIndex];
        var guards = PublicLegions(player).Count(candidate => candidate.CardId == "S01-0212" && !candidate.Tapped && State.ActivePlayer == playerIndex);
        if (guards == 0) return null;
        var canPayWithoutGuards = ActiveMoraleCountWithoutTombGuards(player) >= cost;
        CreatePrompt(playerIndex, "optional", $"{card.Name}：是否使用活跃的陵墓守卫支付费用？", ["yes", "no"], 1, 1,
            "play-morale-choice", data: new Dictionary<string, string>
            {
                ["cardInstanceId"] = card.InstanceId,
                ["row"] = command.Row?.ToString() ?? string.Empty,
                ["slot"] = command.Slot?.ToString() ?? string.Empty,
                ["baseChoice"] = command.Choice ?? "normal-cost",
                ["canPayWithoutGuards"] = canPayWithoutGuards.ToString(),
                ["choiceMode"] = "instant",
                ["yes"] = $"使用陵墓守卫优先支付（费用 {cost}）",
                ["no"] = canPayWithoutGuards ? $"仅使用士气支付（费用 {cost}）" : "不使用并取消本次打出",
            });
        return CommandResult.Ok();
    }

    private CommandResult Attack(int playerIndex, L12Command command)
    {
        if (!CanAct(playerIndex)) return CommandResult.Reject("只能在自己的主要阶段进攻");
        if (command.Target is null) return CommandResult.Reject("缺少进攻目标");
        var attacker = FindOnField(State.Players[playerIndex], command.CardInstanceId, out var row, out _);
        if (attacker is null) return CommandResult.Reject("进攻军团不在战场");
        if (attacker.CannotAttack) return CommandResult.Reject("该军团不能进攻");
        if (!CanAttackFromRow(attacker, row)) return CommandResult.Reject("该军团在当前位置无法进攻");
        if (attacker.Tapped) return CommandResult.Reject("休整军团不能进攻");
        if (attacker.Hidden) return CommandResult.Reject("隐匿军团需先翻回正面");
        if (attacker.SummonRound >= State.Round && !attacker.HasCharge
            && !(command.Target.Type == "master" && attacker.CanAttackMasterOnSummonUntilTurn == State.TurnSerial)
            && !(command.Target.Type == "legion" && attacker.CanAttackLegionsOnSummonUntilTurn == State.TurnSerial))
            return CommandResult.Reject("刚登场的军团不能进攻");

        var defender = State.Players[1 - playerIndex];
        var isRanged = false;
        L12CardInstance? attackTarget = null;
        if (command.Target.Type == "legion")
        {
            var target = FindOnField(defender, command.Target.InstanceId, out var targetRow, out _);
            if (target is null || target.Hidden || !IsFieldLegion(target)) return CommandResult.Reject("目标不是可进攻军团");
            if (IsProtectedByRestedAmakine(defender, target)) return CommandResult.Reject("休整的阿麦金使活跃的试炼军团不可被进攻");
            var zhangFeiKey = $"zhangfei-defense:{target.InstanceId}:{State.Round}";
            if (target.CardId == "S01-0107" && targetRow == 0 && !defender.UsedAbilities.Contains(zhangFeiKey))
            {
                target.Troops += 1000;
                defender.UsedAbilities.Add(zhangFeiKey);
                AddEvent("effect", defender.PlayerIndex, $"张飞在对方回合位于前排，兵力 +1000", target);
            }
            ApplyS1FactionDefensePassives(defender, target, targetRow);
            if (row == 1 && targetRow != 0 && attacker.CanAttackBackAndMasterUntilTurn != State.TurnSerial)
                return CommandResult.Reject("后排远程军团只能进攻对方前排");
            if (row == 0 && targetRow == 1 && !HasRangeInPosition(attacker, row))
                return CommandResult.Reject("近战军团无法进攻对方后排");
            isRanged = row == 1 || targetRow == 1;
            if (isRanged && target.CannotBeRanged) return CommandResult.Reject("目标无法被远程进攻");
            var taunts = defender.Field[0].Where(card => card is not null && HasS1Taunt(card) && !card.Hidden).ToArray();
            if (taunts.Length > 0 && !HasS1Taunt(target)) return CommandResult.Reject("对方前排存在带有挑衅的军团");
            attackTarget = target;
        }
        else if (command.Target.Type == "master")
        {
            var disasterAllowsBackMaster = State.Players[playerIndex].UsedAbilities.Contains("ds01-back-master")
                && HasRangeInPosition(attacker, row);
            if (row != 0 && !disasterAllowsBackMaster
                && attacker.CanAttackMasterOnSummonUntilTurn != State.TurnSerial
                && attacker.CanAttackBackAndMasterUntilTurn != State.TurnSerial)
                return CommandResult.Reject("后排远程军团不能进攻主宰");
            var taunts = defender.Field[0].Where(card => card is not null && HasS1Taunt(card) && !card.Hidden).ToArray();
            if (taunts.Length > 0) return CommandResult.Reject("对方前排存在带有挑衅的军团");
        }
        else return CommandResult.Reject("无效进攻目标");

        attacker.Tapped = true;
        ApplyS1FactionAttackPassives(playerIndex, attacker, row);
        attacker.AttacksThisTurn++;
        if (row == 0 && State.Players[playerIndex].UsedAbilities.Contains($"susano-buff:{attacker.InstanceId}"))
        {
            attacker.Troops += 2000;
            AddEvent("effect", playerIndex, $"须佐之男使 {attacker.Name} 本次前排进攻兵力 +2000", attacker);
        }
        var damage = 1 + (attacker.HasStrongAttack ? 1 : 0);
        if (State.ActiveDisaster?.CardId == "S01-DS02" && attacker.DisasterLevel > 0) damage++;
        State.PendingDefense = new L12PendingDefense
        {
            AttackerPlayer = playerIndex,
            AttackerInstanceId = attacker.InstanceId,
            Target = command.Target,
            IsRanged = isRanged,
            SureHit = attacker.HasSureHit,
            MasterDamage = damage,
        };
        State.Phase = L12Phase.Defense;
        if (attackTarget is null)
            AddEvent("attack", playerIndex,
                $"{State.Players[playerIndex].Name}【{attacker.Name}】{attacker.Troops} vs {defender.Name}【{defender.MasterName}】血量{defender.Hp}", attacker);
        else
            AddEvent("attack", playerIndex,
                $"{State.Players[playerIndex].Name}【{attacker.Name}】{attacker.Troops} vs {defender.Name}【{attackTarget.Name}】{attackTarget.Troops}", attacker, attackTarget);
        PushEffect(playerIndex, attacker, "attack", "进攻宣言与【进攻时】效果");
        return CommandResult.Ok();
    }

    private static bool HasRangeInPosition(L12CardInstance card, int row)
    {
        if (!card.HasRangeBonus) return false;
        if (card.CardId == "S01-0415" && row != 0) return false;
        return true;
    }

    private static bool CanAttackFromRow(L12CardInstance card, int row)
        => row == 0 || (row == 1 && HasRangeInPosition(card, row));

    private CommandResult ResolveDefense(int playerIndex, L12Command command)
    {
        var pending = State.PendingDefense;
        if (State.Phase != L12Phase.Defense || pending is null) return CommandResult.Reject("当前没有待结算进攻");
        if (State.EffectStack.Count > 0 || State.PendingPrompts.Count > 0) return CommandResult.Reject("进攻响应仍在结算");
        if (playerIndex == pending.AttackerPlayer) return CommandResult.Reject("进攻方不能代替防守方结算");
        var attackerPlayer = State.Players[pending.AttackerPlayer];
        var defender = State.Players[playerIndex];
        var attacker = FindOnField(attackerPlayer, pending.AttackerInstanceId, out _, out _);
        if (attacker is null)
        {
            State.PendingDefense = null;
            State.Phase = L12Phase.Main;
            AddEvent("attack-ended", playerIndex, "进攻军团已离场，进攻结束");
            return CommandResult.Ok();
        }

        var blockIds = command.CardInstanceIds ?? [];
        var supportId = command.SupportInstanceId;
        var validation = ValidateDefenseChoice(playerIndex, pending, attacker, blockIds, supportId);
        if (!validation.Accepted) return validation;

        var declaredDefense = pending.Target.Type == "master" ? blockIds.Count > 0 : !string.IsNullOrWhiteSpace(supportId);
        if (declaredDefense)
        {
            QueueAuthorityEvent("defense", playerIndex, attacker,
                pending.Target.Type == "master" ? $"{defender.Name}声明抵挡" : $"{defender.Name}声明支援",
                subjectPlayer: playerIndex, targetInstanceId: pending.Target.InstanceId, causedByEffect: false,
                data: new Dictionary<string, string>
                {
                    ["action"] = pending.Target.Type == "master" ? "block" : "support",
                    ["blockIds"] = string.Join('|', blockIds),
                    ["supportId"] = supportId ?? string.Empty,
                });
            return CommandResult.Ok();
        }

        return ResolveDefenseCore(playerIndex, blockIds, supportId, forceInvalid: false);
    }

    private CommandResult ValidateDefenseChoice(int playerIndex, L12PendingDefense pending, L12CardInstance attacker,
        IReadOnlyList<string> blockIds, string? supportId)
    {
        var defender = State.Players[playerIndex];
        if (pending.Target.Type == "master")
        {
            if (pending.SureHit && blockIds.Count > 0) return CommandResult.Reject("必中进攻无法被抵挡");
            if (blockIds.Count != blockIds.Distinct().Count()) return CommandResult.Reject("抵挡卡牌重复");
            var cards = defender.Hand.Where(card => blockIds.Contains(card.InstanceId) && card.CardType == "legion").ToList();
            if (cards.Count != blockIds.Count) return CommandResult.Reject("只能弃置手牌中的军团");
            if (cards.Count > 0 && cards.Sum(card => card.Troops) < attacker.Troops)
                return CommandResult.Reject("弃置军团总兵力不足");
            return CommandResult.Ok();
        }

        if (string.IsNullOrWhiteSpace(supportId)) return CommandResult.Ok();
        if (pending.SureHit) return CommandResult.Reject("必中进攻无法被支援");
        var target = FindOnField(defender, pending.Target.InstanceId, out var targetRow, out var targetSlot);
        if (target is null) return CommandResult.Reject("进攻目标已经离场");
        var support = FindOnField(defender, supportId, out var supportRow, out var supportSlot);
        if (support is null || !IsFieldLegion(support) || supportRow != 1 || targetRow != 0 || supportSlot != targetSlot)
            return CommandResult.Reject("支援军团必须位于被进攻军团同列后排");
        if (support.CannotSupport || defender.BackRowCannotSupport) return CommandResult.Reject("该后排军团本回合不能支援");
        if (target.Troops + support.Troops < attacker.Troops) return CommandResult.Reject("支援后兵力仍不足");
        return CommandResult.Ok();
    }

    private CommandResult ResolveDefenseCore(int playerIndex, IReadOnlyList<string> declaredBlockIds,
        string? declaredSupportId, bool forceInvalid)
    {
        var pending = State.PendingDefense;
        if (State.Phase != L12Phase.Defense || pending is null) return CommandResult.Reject("当前没有待结算进攻");
        var attackerPlayer = State.Players[pending.AttackerPlayer];
        var defender = State.Players[playerIndex];
        var attacker = FindOnField(attackerPlayer, pending.AttackerInstanceId, out _, out _);
        if (attacker is null)
        {
            State.PendingDefense = null;
            State.Phase = L12Phase.Main;
            AddEvent("attack-ended", playerIndex, "进攻军团已离场，进攻结束");
            return CommandResult.Ok();
        }

        var killedTarget = false;
        if (pending.Target.Type == "master")
        {
            var ids = forceInvalid ? [] : declaredBlockIds;
            var cards = defender.Hand.Where(card => ids.Contains(card.InstanceId) && card.CardType == "legion").ToList();
            if (cards.Count == 0)
            {
                DamageMaster(playerIndex, pending.MasterDamage, $"{attacker.Name}的进攻");
                if (attacker.CardId == "S01-0308" && State.Phase != L12Phase.GameOver)
                    PushEffect(pending.AttackerPlayer, attacker, "after-damage", "【对主宰造成伤害时】效果");
            }
            foreach (var card in cards)
            {
                defender.Hand.Remove(card);
                defender.Graveyard.Add(card);
            }
            AddEvent("defense", playerIndex, cards.Count == 0
                ? $"{defender.Name} 的主宰受到 {pending.MasterDamage} 点伤害"
                : $"{defender.Name} 弃置 {cards.Count} 张军团抵挡", cards.ToArray());
        }
        else
        {
            var target = FindOnField(defender, pending.Target.InstanceId, out var targetRow, out var targetSlot);
            if (target is null)
            {
                State.PendingDefense = null;
                State.Phase = L12Phase.Main;
                AddEvent("attack-ended", playerIndex, "进攻目标丢失，进攻结束");
                return CommandResult.Ok();
            }
            var supported = false;
            var supportId = forceInvalid ? null : declaredSupportId;
            if (!string.IsNullOrWhiteSpace(supportId))
            {
                var support = FindOnField(defender, supportId, out _, out _)!;
                RemoveFromField(defender, support, true, "作为支援军团阵亡");
                supported = true;
                AddEvent("support", playerIndex, $"{support.Name} 支援 {target.Name}，进攻双方不损耗兵力", support, target, attacker);
            }
            if (!supported)
            {
                var targetTroops = target.Troops;
                target.Troops -= attacker.Troops;
                if (!attacker.HasAttackNoLoss && !(pending.IsRanged && attacker.HasRangedNoLoss)) attacker.Troops -= targetTroops;
                var simultaneousDeaths = new List<(int Controller, L12CardInstance Card)>();
                if (target.Troops <= 0)
                {
                    killedTarget = true;
                    if (RemoveFromField(defender, target, true, "阵亡", queueDeathTrigger: false))
                        simultaneousDeaths.Add((defender.PlayerIndex, target));
                }
                if (attacker.Troops <= 0 && RemoveFromField(attackerPlayer, attacker, true, "阵亡", queueDeathTrigger: false))
                    simultaneousDeaths.Add((attackerPlayer.PlayerIndex, attacker));
                QueueSimultaneousDeathTriggers(simultaneousDeaths);
                AddEvent("combat", playerIndex, attacker.HasAttackNoLoss || pending.IsRanged && attacker.HasRangedNoLoss
                    ? "进攻无损：被进攻军团承受兵力减损，进攻军团不减损"
                    : "双方军团同时造成等同于当前兵力的兵力减损", attacker, target);
            }
        }

        State.PendingDefense = null;
        if (State.Phase != L12Phase.GameOver) State.Phase = L12Phase.Main;
        QueueAfterAttackEffects(pending.AttackerPlayer, attacker, killedTarget);
        QueueS1PostAttackReactions(pending.AttackerPlayer);
        return CommandResult.Ok();
    }

    private CommandResult Move(int playerIndex, L12Command command)
    {
        if (!CanAct(playerIndex)) return CommandResult.Reject("只能在自己的主要阶段位移");
        if (command.Row is null or < 0 or > 1 || command.Slot is null or < 0 or > 2) return CommandResult.Reject("目标阵地无效");
        var player = State.Players[playerIndex];
        var card = FindOnField(player, command.CardInstanceId, out var sourceRow, out var sourceSlot);
        if (card is null || !IsFieldLegion(card) || card.Tapped) return CommandResult.Reject("只能移动活跃军团");
        var targetRow = command.Row.GetValueOrDefault();
        var targetSlot = command.Slot.GetValueOrDefault();
        if (State.ActiveDisaster?.CardId == "S01-DS03" && targetRow == 1)
            return CommandResult.Reject("〈腐秽大地〉持续期间无法位移至后排");
        var hasFreeMove = card.CardId is "S01-0002" or "S01-0106" or "S01-0409"
            && !player.UsedAbilities.Contains($"free-move:{card.InstanceId}");
        if (!hasFreeMove && Math.Abs(sourceRow - targetRow) + Math.Abs(sourceSlot - targetSlot) != 1)
            return CommandResult.Reject("规则位移每次只能移动至相邻空格");
        if (player.Field[targetRow][targetSlot] is not null) return CommandResult.Reject("目标阵地已占用");
        if (!hasFreeMove && !TryConsumeMorale(player, 1)) return CommandResult.Reject("位移需要消耗 1 张活跃士气");
        if (hasFreeMove) player.UsedAbilities.Add($"free-move:{card.InstanceId}");
        player.Field[sourceRow][sourceSlot] = null;
        player.Field[targetRow][targetSlot] = card;
        AddEvent("move", playerIndex, $"{card.Name} 位移", card);
        return CommandResult.Ok();
    }

    private CommandResult FlipHidden(int playerIndex, string? instanceId)
    {
        if (!CanAct(playerIndex)) return CommandResult.Reject("只能在自己的主要阶段翻回军团");
        var card = FindOnField(State.Players[playerIndex], instanceId, out _, out _);
        if (card?.CardId != "S01-0415" || !card.Hidden) return CommandResult.Reject("该军团当前不能翻回正面");
        card.Hidden = false;
        AddEvent("reveal", playerIndex, $"{card.Name} 主动翻回正面", card);
        return CommandResult.Ok();
    }

    private CommandResult ActivateAbility(int playerIndex, L12Command command)
    {
        if (!CanAct(playerIndex)) return CommandResult.Reject("只能在自己的主要阶段发动主动效果");
        return BeginActiveAbility(playerIndex, command);
    }
}
