namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private CommandResult? TryBeginS2RemainingAbility(int playerIndex, L12CardInstance source, string ability)
    {
        var player = State.Players[playerIndex];
        switch (ability)
        {
            case "wukongTransform" when source.CardId == "S02-01M1":
            {
                if (PublicLegions(player).Any(card => card.IsMasterLegion))
                    return CommandResult.Reject("孙悟空已经作为军团登场");
                if (!EmptySlots(player).Any(slot => slot.StartsWith("0:", StringComparison.Ordinal)))
                    return CommandResult.Reject("我方前排没有空位");
                if (player.Morale.Count < 2) return CommandResult.Reject("至少需要返还2张士气");
                return BeginPendingActivation(playerIndex, source, ability,
                    player.Morale.Select(card => card.InstanceId).ToArray(),
                    "孙悟空：选择返还2至8张士气", 2, Math.Min(8, player.Morale.Count));
            }
            case "thorCharge" when source.CardId == "S02-03M1":
                if (player.Hp > 3) return CommandResult.Reject("我方主宰血量需要不高于3");
                return CommitActiveAbility(playerIndex, source, ability, null);
            case "divinityFlipMorale" when source.CardId == "S02-05D1":
                if (!player.Morale.Any(card => !card.IsGodPower)) return CommandResult.Reject("没有可翻转的士气");
                return CommitActiveAbility(playerIndex, source, ability, null);
            case "divinityPower" when source.CardId == "S02-05D1":
                if (player.Morale.Count(card => card.IsGodPower && !card.Tapped) < 2)
                    return CommandResult.Reject("需要2张活跃的神力");
                return BeginPendingActivation(playerIndex, source, ability,
                    ["mode:recover", "mode:damage"], "诸神巅：选择一项效果");
            case "divinityFreePromotion" when source.CardId == "S02-05D1":
                if (player.MasterTapped) return CommandResult.Reject("诸神巅必须为活跃状态");
                return CommitActiveAbility(playerIndex, source, ability, null);
            case "artemisBuff" when source.CardId == "S02-05M1":
            {
                var targets = PublicLegions(player).Where(card => card.Faction == "olympus" && card.CurrentCost is >= 3 and <= 6)
                    .Select(card => card.InstanceId).ToList();
                if (targets.Count == 0) return CommandResult.Reject("没有费用3至6的【奥林匹斯】军团");
                var payment = new List<string>();
                if (player.Morale.Any(card => card.IsGodPower && !card.Tapped)) payment.Add("pay:god-power");
                if (player.Hand.Count > 0) payment.AddRange(player.Hand.Select(card => $"discard:{card.InstanceId}"));
                if (payment.Count == 0) return CommandResult.Reject("没有可支付的神力或手牌");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    new L12ActivationSelectionStep { Kind = "option", Text = "阿尔忒弥斯：选择消耗1神力或弃置1张手牌", ValidChoices = payment },
                    new L12ActivationSelectionStep { Kind = "field-legion", Text = "选择我方1张费用3至6的【奥林匹斯】军团", ValidChoices = targets },
                    new L12ActivationSelectionStep { Kind = "option", Text = "选择获得强攻或震击", ValidChoices = ["buff:strong", "buff:shock"] },
                ]);
            }
            case "hippolytaRevive" when source.CardId == "S02-0510":
            {
                if (source.Tapped) return CommandResult.Reject("希波吕忒必须为活跃状态");
                var grave = player.Graveyard.Where(card => card.Faction == "olympus" && card.CardType == "legion" && card.CurrentCost <= 4)
                    .Select(card => card.InstanceId).ToList();
                if (player.Hand.Count == 0 || grave.Count == 0 || !EmptySlots(player).Any())
                    return CommandResult.Reject("需要手牌、墓地中费用不高于4的【奥林匹斯】军团和空战场位置");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    new L12ActivationSelectionStep { Kind = "hand-card", Text = "希波吕忒：选择弃置1张手牌", ValidChoices = player.Hand.Select(card => card.InstanceId).ToList() },
                    new L12ActivationSelectionStep { Kind = "grave-card", Text = "选择墓地1张费用不高于4的【奥林匹斯】军团", ValidChoices = grave },
                    new L12ActivationSelectionStep { Kind = "slot", Text = "选择该军团活跃登场的位置", ValidChoices = EmptySlots(player).ToList() },
                ]);
            }
            default:
                return null;
        }
    }

    private CommandResult? TryCommitS2RemainingAbility(int playerIndex, L12CardInstance source, string ability,
        string? target, string onceKey)
    {
        var player = State.Players[playerIndex];
        switch (ability)
        {
            case "wukongTransform" when source.CardId == "S02-01M1":
            {
                var ids = SplitDeclared(target);
                if (ids.Length is < 2 or > 8 || ids.Distinct().Count() != ids.Length) return CommandResult.Reject("需要选择2至8张士气");
                var cards = ids.Select(id => player.Morale.FirstOrDefault(card => card.InstanceId == id)).ToArray();
                if (cards.Any(card => card is null)) return CommandResult.Reject("选择的士气已失效");
                var slot = EmptySlots(player).FirstOrDefault(choice => choice.StartsWith("0:", StringComparison.Ordinal));
                if (slot is null) return CommandResult.Reject("我方前排没有空位");
                if (!ReturnSelectedMorale(player, cards.Cast<L12MoraleCard>().ToArray()))
                    return CommandResult.Reject("选择的士气已失效");
                player.UsedAbilities.Add(onceKey);
                PushEffect(playerIndex, source, "active", "主宰效果", data: new Dictionary<string, string>
                {
                    ["ability"] = ability, ["count"] = ids.Length.ToString(), ["slot"] = slot,
                });
                return CommandResult.Ok();
            }
            case "thorCharge" when source.CardId == "S02-03M1":
                if (player.Hp > 3 || !TryConsumeMorale(player, 2)) return CommandResult.Reject("需要主宰血量不高于3且消耗2士气");
                player.UsedAbilities.Add(onceKey);
                PushEffect(playerIndex, source, "active", "主宰效果", data: new Dictionary<string, string> { ["ability"] = ability });
                return CommandResult.Ok();
            case "divinityFlipMorale" when source.CardId == "S02-05D1":
                player.UsedAbilities.Add(onceKey);
                PushEffect(playerIndex, source, "active", "主神效果", data: new Dictionary<string, string> { ["ability"] = ability });
                return CommandResult.Ok();
            case "divinityPower" when source.CardId == "S02-05D1":
                if (target is not ("mode:recover" or "mode:damage") || !L12S2ZoneOps.ConsumeAndFlipGodPower(player, 2))
                    return CommandResult.Reject("选项无效或需要2张活跃的神力");
                player.UsedAbilities.Add(onceKey);
                PushEffect(playerIndex, source, "active", "主神效果", data: new Dictionary<string, string> { ["ability"] = ability, ["mode"] = target });
                return CommandResult.Ok();
            case "divinityFreePromotion" when source.CardId == "S02-05D1":
                if (player.MasterTapped) return CommandResult.Reject("诸神巅必须为活跃状态");
                player.MasterTapped = true;
                player.UsedAbilities.Add(onceKey);
                PushEffect(playerIndex, source, "active", "主神效果", data: new Dictionary<string, string> { ["ability"] = ability });
                return CommandResult.Ok();
            case "artemisBuff" when source.CardId == "S02-05M1":
            {
                var declared = SplitDeclared(target);
                if (declared.Length != 3) return CommandResult.Reject("阿尔忒弥斯的支付、目标和效果选择不完整");
                var legion = FindOnField(player, declared[1], out _, out _);
                if (legion is null || legion.Faction != "olympus" || legion.CurrentCost is < 3 or > 6) return CommandResult.Reject("目标已不合法");
                if (declared[0] == "pay:god-power")
                {
                    if (!L12S2ZoneOps.ConsumeAndFlipGodPower(player, 1)) return CommandResult.Reject("需要1张活跃神力");
                }
                else if (declared[0].StartsWith("discard:", StringComparison.Ordinal))
                {
                    var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == declared[0][8..]);
                    if (card is null) return CommandResult.Reject("弃置的手牌已失效");
                    player.Hand.Remove(card); player.Graveyard.Add(card);
                }
                else return CommandResult.Reject("支付方式不合法");
                player.UsedAbilities.Add(onceKey);
                PushEffect(playerIndex, source, "active", "主宰效果", data: new Dictionary<string, string>
                {
                    ["ability"] = ability, ["target"] = legion.InstanceId, ["buff"] = declared[2],
                });
                return CommandResult.Ok();
            }
            case "hippolytaRevive" when source.CardId == "S02-0510":
            {
                var declared = SplitDeclared(target);
                if (declared.Length != 3 || source.Tapped || !TryConsumeMorale(player, 3)) return CommandResult.Reject("发动条件或士气不足");
                var discard = player.Hand.FirstOrDefault(card => card.InstanceId == declared[0]);
                var revive = player.Graveyard.FirstOrDefault(card => card.InstanceId == declared[1] && card.Faction == "olympus" && card.CardType == "legion" && card.CurrentCost <= 4);
                if (discard is null || revive is null || !EmptySlots(player).Contains(declared[2])) return CommandResult.Reject("选择的卡牌或位置已失效");
                source.Tapped = true; player.Hand.Remove(discard); player.Graveyard.Add(discard); player.UsedAbilities.Add(onceKey);
                PushEffect(playerIndex, source, "active", "主动休整效果", data: new Dictionary<string, string>
                {
                    ["ability"] = ability, ["revive"] = revive.InstanceId, ["slot"] = declared[2],
                });
                return CommandResult.Ok();
            }
            default:
                return null;
        }
    }

    private bool TryResolveS2RemainingAbility(L12StackItem item, L12CardInstance? source, string ability)
    {
        var player = State.Players[item.Controller];
        switch (ability)
        {
            case "wukongTransform" when source?.CardId == "S02-01M1":
            {
                var masterLegion = CreateCard(source.CardId, $"master-legion-{item.Controller}-{State.TurnSerial}");
                masterLegion.OwnerIndex = item.Controller; masterLegion.IsMasterLegion = true; masterLegion.HasCharge = true;
                masterLegion.SetTroopsValue = int.Parse(item.Data["count"]) * 1000;
                masterLegion.Troops = masterLegion.SetTroopsValue.Value;
                var (row, slot) = ParseSlot(item.Data["slot"]);
                player.Field[row][slot] = masterLegion;
                AddEvent("put", item.Controller, $"孙悟空作为兵力{masterLegion.Troops}的【斗士】军团在前排活跃登场", masterLegion);
                FinishStackItem(item); return true;
            }
            case "thorCharge" when source?.CardId == "S02-03M1":
                player.MasterCannotHeal = true;
                player.UsedAbilities.Add($"s2-thor-charge:{State.TurnSerial}");
                AddEvent("effect", item.Controller, "本回合我方【阿斯加德】军团登场时获得冲锋；主宰本局无法因效果增加血量", source);
                FinishStackItem(item); return true;
            case "divinityFlipMorale" when source?.CardId == "S02-05D1":
                return PromptS2FlipMorale(item, source);
            case "divinityFreePromotion" when source?.CardId == "S02-05D1":
                player.NextS2PromotionGodPowerDiscount = Math.Max(player.NextS2PromotionGodPowerDiscount, 99);
                AddEvent("effect", item.Controller, "本回合我方下一张【奥林匹斯】军团晋升登场无需消耗并翻转神力", source);
                FinishStackItem(item); return true;
            case "divinityPower" when source?.CardId == "S02-05D1":
                if (item.Data["mode"] == "mode:damage") return PromptNextDivinityDamage(item);
                return PromptDivinityRecover(item);
            case "artemisBuff" when source?.CardId == "S02-05M1":
            {
                var legion = FindOnField(player, item.Data["target"], out _, out _);
                if (legion is not null)
                {
                    if (item.Data["buff"] == "buff:strong") GrantStrongAttack(legion);
                    else legion.HasShock = true;
                    player.UsedAbilities.Add($"s2-artemis-buff:{legion.InstanceId}:{State.TurnSerial}");
                }
                FinishStackItem(item); return true;
            }
            case "artemisDeathFlip" when item.SourceCardId == "S02-05M1":
                return PromptS2FlipMorale(item, source ?? CreateCard("S02-05M1", item.SourceInstanceId), optional: true, onlyTapped: true);
            case "hippolytaRevive" when source?.CardId == "S02-0510":
                SummonFromAnyPrivateZone(player, item.Data["revive"], item.Data["slot"], tapped: false);
                FinishStackItem(item); return true;
            case "angusTacticTrial" when item.SourceCardId == "S02-06M2":
                CreatePrompt(item.Controller, "optional", "安格斯·麦·奥格：我方战术效果结算成功，是否使试炼+1？",
                    ["yes", "no"], 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-angus-trial", ["yes"] = "试炼+1", ["no"] = "不发动" });
                return true;
            case "grailRoundTableRune" when item.SourceCardId == "S02-06S4":
                CreatePrompt(item.Controller, "optional", "寻找圣杯之旅：我方【圆桌骑士】登场，是否获得1符文？",
                    ["yes", "no"], 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-grail-round-table-rune", ["yes"] = "获得1符文", ["no"] = "不发动" });
                return true;
            case "anderstorpRingDraw" when item.SourceCardId == "S02-0305":
                CreatePrompt(item.Controller, "optional", "安德华拉诺特：我方主宰受到伤害，是否抽取1张牌？",
                    ["yes", "no"], 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-ring-draw", ["yes"] = "抽取1张牌", ["no"] = "不发动" });
                return true;
            case "tsukuyomiFollowMove" when item.SourceCardId == "S02-04M1":
                CreatePrompt(item.Controller, "optional", "月读：是否消耗1士气，使另一张活跃军团进行1格位移？",
                    ["yes", "no"], 1, 1, "card-effect", item.StackItemId,
                    data: new Dictionary<string, string> { ["action"] = "s2-tsukuyomi-pay", ["yes"] = "消耗1士气并位移另一张军团", ["no"] = "不发动" });
                return true;
            case "tsukuyomiReadyMorale" when item.SourceCardId == "S02-04M1":
            {
                var choices = player.Morale.Where(card => card.Tapped).Select(card => card.InstanceId).ToList();
                if (choices.Count == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "target-morale", "月读：选择1张休整士气转为活跃", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-tsukuyomi-ready" });
                return true;
            }
            default:
                return false;
        }
    }

    private void ResolveS2TrojanHorseAfterAttack(L12StackItem item)
    {
        var owner = State.Players[item.Controller];
        var horse = owner.Hand.FirstOrDefault(card => card.InstanceId == item.SourceInstanceId && card.CardId == "S02-0523");
        var host = int.TryParse(item.Data.GetValueOrDefault("attacker"), out var attacker) && attacker is >= 0 and <= 1
            ? State.Players[attacker]
            : State.Players[1 - item.Controller];
        if (horse is null || !EmptySlots(host).Any()) { FinishStackItem(item); return; }
        CreatePrompt(item.Controller, "optional", "特洛伊木马：是否置入对方战场任意空位？", ["yes", "no"], 1, 1,
            "card-effect", item.StackItemId, data: new Dictionary<string, string>
            {
                ["action"] = "s2-trojan-confirm", ["yes"] = "置入对方战场", ["no"] = "不发动",
            });
    }

    private bool TryContinueS2RemainingEffect(L12StackItem item, L12Prompt prompt, List<string> chosen)
    {
        switch (prompt.Data.GetValueOrDefault("action"))
        {
            case "s2-trojan-confirm":
            {
                if (chosen[0] != "yes") { FinishStackItem(item); return true; }
                var host = State.Players[1 - item.Controller];
                CreatePrompt(item.Controller, "enemy-slot", "特洛伊木马：选择置入对方战场的空位", EmptySlots(host), 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-trojan-slot" });
                return true;
            }
            case "s2-trojan-slot":
            {
                var owner = State.Players[item.Controller];
                var host = State.Players[1 - item.Controller];
                var horse = owner.Hand.FirstOrDefault(card => card.InstanceId == item.SourceInstanceId && card.CardId == "S02-0523");
                if (horse is null || !EmptySlots(host).Contains(chosen[0])) { FinishStackItem(item); return true; }
                var (row, slot) = ParseSlot(chosen[0]);
                owner.Hand.Remove(horse);
                horse.OwnerIndex = item.Controller;
                horse.SetRound = State.Round;
                horse.DiscardAtEndOfTurnUntilTurn = ExpiryAtNextOwnEnd(item.Controller);
                host.Field[row][slot] = horse;
                AddEvent("put", item.Controller, $"{horse.Name}置入{host.Name}战场，直到下个我方回合结束", horse);
                RecalculateContinuousTroops();
                FinishStackItem(item);
                return true;
            }
            case "s2-divinity-damage":
            {
                var target = DeclaredEnemyTarget(item.Controller, chosen[0]);
                if (target is not null) AddTimedModifier(target, -1000, 0, ExpiryAtNextOwnEnd(item.Controller), "诸神巅");
                var allocated = int.Parse(item.Data.GetValueOrDefault("allocated") ?? "0") + 1;
                item.Data["allocated"] = allocated.ToString();
                if (allocated < 6 && PublicLegions(State.Players[1 - item.Controller]).Any()) PromptNextDivinityDamage(item);
                else FinishStackItem(item);
                return true;
            }
            case "s2-divinity-recover":
            {
                var player = State.Players[item.Controller];
                var recovered = player.Graveyard.FirstOrDefault(card => card.InstanceId == chosen[0] && card.Faction == "olympus");
                if (recovered is null) { FinishStackItem(item); return true; }
                player.Graveyard.Remove(recovered);
                AddCardToHandByEffect(player, recovered, "grave", $"诸神巅将〈{recovered.Name}〉从墓地加入手牌");
                var legions = player.Hand.Concat(player.Graveyard)
                    .Where(card => card.Faction == "olympus" && card.CardType == "legion" && card.CurrentCost <= 4)
                    .DistinctBy(card => card.InstanceId).ToList();
                if (legions.Count == 0 || !EmptySlots(player).Any()) { FinishStackItem(item); return true; }
                var choices = legions.Select(card => card.InstanceId).Append("skip").ToList();
                CreatePrompt(item.Controller, "optional-card", "随后可将手牌或墓地1张费用不高于4的军团活跃登场", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-divinity-hand", ["skip"] = "不登场" });
                return true;
            }
            case "s2-divinity-hand":
                if (chosen[0] == "skip") { FinishStackItem(item); return true; }
                item.Data["hand"] = chosen[0];
                CreatePrompt(item.Controller, "slot", "选择该军团活跃登场的位置", EmptySlots(State.Players[item.Controller]), 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-divinity-hand-slot" });
                return true;
            case "s2-divinity-hand-slot":
                SummonFromAnyPrivateZone(State.Players[item.Controller], item.Data["hand"], chosen[0], tapped: false);
                FinishStackItem(item); return true;
            case "s2-angus-trial":
                if (chosen[0] == "yes") AdvanceTrial(item.Controller, 1, CreateCard("S02-06M2", $"master-{item.Controller}"));
                FinishStackItem(item); return true;
            case "s2-grail-round-table-rune":
            {
                var player = State.Players[item.Controller];
                var key = $"trigger:grail-round-table:{State.TurnSerial}";
                var pendingKey = $"{key}:pending";
                player.UsedAbilities.Remove(pendingKey);
                if (chosen[0] == "yes" && player.UsedAbilities.Add(key))
                {
                    L12S2ZoneOps.GainRunes(player, 1);
                    AddEvent("runes", item.Controller, "〈寻找圣杯之旅〉使我方获得1符文", FindSource(item) is { } trial ? [trial] : []);
                }
                FinishStackItem(item); return true;
            }
            case "s2-ring-draw":
                if (chosen[0] == "yes" && !Draw(State.Players[item.Controller], 1))
                    SetWinner(1 - item.Controller, "〈安德华拉诺特〉效果抽牌时牌库为空");
                FinishStackItem(item); return true;
            case "s2-tsukuyomi-pay":
            {
                var player = State.Players[item.Controller];
                if (chosen[0] != "yes" || !TryConsumeMorale(player, 1)) { FinishStackItem(item); return true; }
                player.UsedAbilities.Add($"active:master-{item.Controller}:tsukuyomiFollowMove");
                var targets = PublicLegions(player).Where(card => !card.Tapped && card.InstanceId != item.Data.GetValueOrDefault("moved")
                    && FindOnField(player, card.InstanceId, out var row, out var slot) is not null && AdjacentEmptySlots(player, row, slot).Any())
                    .Select(card => card.InstanceId).ToList();
                if (targets.Count == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "field-legion", "月读：选择另一张活跃军团进行1格位移", targets, 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-tsukuyomi-target" });
                return true;
            }
            case "s2-tsukuyomi-target":
            {
                var player = State.Players[item.Controller];
                var legion = FindOnField(player, chosen[0], out var row, out var slot);
                if (legion is null) { FinishStackItem(item); return true; }
                item.Data["follow"] = legion.InstanceId;
                CreatePrompt(item.Controller, "slot", "月读：选择该军团位移后的相邻空位", AdjacentEmptySlots(player, row, slot), 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-tsukuyomi-slot" });
                return true;
            }
            case "s2-tsukuyomi-slot":
            {
                var player = State.Players[item.Controller];
                var legion = FindOnField(player, item.Data["follow"], out var oldRow, out var oldSlot);
                if (legion is not null)
                {
                    var (newRow, newSlot) = ParseSlot(chosen[0]);
                    if (AdjacentEmptySlots(player, oldRow, oldSlot).Contains(chosen[0]))
                    {
                        player.Field[oldRow][oldSlot] = null; player.Field[newRow][newSlot] = legion;
                        legion.LastMovedTurn = State.TurnSerial;
                        AddTimedModifier(legion, 0, -1, ExpiryAtNextOwnEnd(item.Controller), "月读");
                        AddEvent("move", item.Controller, $"月读使〈{legion.Name}〉位移1格", legion);
                        NotifyS2LegionMoved(item.Controller, legion, oldRow, newRow);
                    }
                }
                FinishStackItem(item); return true;
            }
            case "s2-tsukuyomi-ready":
            {
                var morale = State.Players[item.Controller].Morale.FirstOrDefault(card => card.InstanceId == chosen[0] && card.Tapped);
                if (morale is not null)
                {
                    morale.Tapped = false;
                    AddEvent("morale", item.Controller, "月读使选择的休整士气转为活跃");
                }
                FinishStackItem(item);
                return true;
            }
            default:
                return false;
        }
    }

    private bool PromptNextDivinityDamage(L12StackItem item)
    {
        var targets = PublicLegions(State.Players[1 - item.Controller]).Select(card => card.InstanceId).ToList();
        if (targets.Count == 0) { FinishStackItem(item); return true; }
        CreatePrompt(item.Controller, "enemy-legion", $"诸神巅：分配第{int.Parse(item.Data.GetValueOrDefault("allocated") ?? "0") + 1}点1000兵力伤害",
            targets, 1, 1, "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-divinity-damage" });
        return true;
    }

    private bool PromptDivinityRecover(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var choices = player.Graveyard.Where(card => card.Faction == "olympus").Select(card => card.InstanceId).ToList();
        if (choices.Count == 0) { FinishStackItem(item); return true; }
        CreatePrompt(item.Controller, "grave-card", "诸神巅：选择墓地1张【奥林匹斯】卡牌加入手牌", choices, 1, 1,
            "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "s2-divinity-recover" });
        return true;
    }

    private static string[] SplitDeclared(string? target)
        => (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);

    private void QueueS2AngusTacticTrial(int playerIndex, L12CardInstance tactic)
    {
        var player = State.Players[playerIndex];
        var key = $"trigger:angus-tactic:{State.TurnSerial}";
        if (player.MasterId != "S02-06M2" || !player.UsedAbilities.Add(key)) return;
        var master = CreateCard("S02-06M2", $"master-{playerIndex}");
        QueueTriggerCandidates([
            CreateTriggerCandidate(playerIndex, master, "active", "战术效果结算成功时效果",
                new Dictionary<string, string> { ["ability"] = "angusTacticTrial", ["tactic"] = tactic.CardId })
        ]);
    }

    private L12TriggerCandidate? BuildS2GrailRoundTableEntryCandidate(int playerIndex, L12CardInstance legion)
    {
        var player = State.Players[playerIndex];
        var key = $"trigger:grail-round-table:{State.TurnSerial}";
        var pendingKey = $"{key}:pending";
        var trial = player.SpecialZones.Trials.FirstOrDefault(card => card.CardId == "S02-06S4" && card.TrialCompleted);
        if (State.ActivePlayer != playerIndex || legion.CardType != "legion" || !legion.HasTrait("圆桌骑士")
            || trial is null || player.UsedAbilities.Contains(key) || !player.UsedAbilities.Add(pendingKey)) return null;
        return CreateTriggerCandidate(playerIndex, trial, "active", "我方【圆桌骑士】登场时效果",
            new Dictionary<string, string> { ["ability"] = "grailRoundTableRune", ["entered"] = legion.InstanceId });
    }

    private void QueueS2GrailRoundTableEntry(int playerIndex, L12CardInstance legion)
    {
        var candidate = BuildS2GrailRoundTableEntryCandidate(playerIndex, legion);
        if (candidate is not null) QueueTriggerCandidates([candidate]);
    }

    private L12TriggerCandidate? BuildArtemisRangedDeathCandidate(int owner, L12CardInstance defeated)
    {
        var player = State.Players[owner];
        var key = $"trigger:artemis-ranged-death:{State.TurnSerial}";
        if (player.MasterId != "S02-05M1" || !defeated.LastKnownWasRanged
            || !player.Morale.Any(card => card.Tapped && !card.IsGodPower) || !player.UsedAbilities.Add(key)) return null;
        var master = CreateCard("S02-05M1", $"master-{owner}");
        return CreateTriggerCandidate(owner, master, "active", "我方远程军团阵亡时效果",
            new Dictionary<string, string> { ["ability"] = "artemisDeathFlip", ["defeated"] = defeated.CardId });
    }

    private void ResolveS2DelayedEndTurnCards(int endingPlayer)
    {
        var owner = State.Players[endingPlayer];
        for (var hostIndex = 0; hostIndex < State.Players.Length; hostIndex++)
        {
            var host = State.Players[hostIndex];
            for (var row = 0; row < host.Field.Length; row++)
            for (var slot = 0; slot < host.Field[row].Length; slot++)
            {
                var horse = host.Field[row][slot];
                if (horse?.CardId != "S02-0523" || horse.OwnerIndex != endingPlayer
                    || horse.DiscardAtEndOfTurnUntilTurn > State.TurnSerial) continue;
                host.Field[row][slot] = null;
                ResetCardAfterLeavingField(horse);
                owner.Graveyard.Add(horse);
                AddEvent("grave", endingPlayer, $"{horse.Name}在我方回合结束时置入墓地", horse);
                if (!Draw(owner, 1))
                {
                    SetWinner(1 - endingPlayer, "〈特洛伊木马〉效果抽牌时牌库为空");
                    return;
                }
                AddEvent("draw", endingPlayer, $"{horse.Name}的效果使{owner.Name}抽取1张牌", horse);
            }
        }
        RecalculateContinuousTroops();
    }

    private int AdjustAnderstorpRingDamage(L12PlayerState player, int amount)
        => State.ActivePlayer != player.PlayerIndex && player.Relic?.CardId == "S02-0305"
            && player.MasterDamageTakenThisTurn == 0 ? 2 : amount;

    private L12TriggerCandidate? BuildAnderstorpRingDrawCandidate(int playerIndex)
    {
        var player = State.Players[playerIndex];
        var key = $"trigger:anderstorp-draw:{State.TurnSerial}";
        if (State.ActivePlayer != playerIndex || player.Relic?.CardId != "S02-0305" || !player.UsedAbilities.Add(key))
            return null;
        return CreateTriggerCandidate(playerIndex, player.Relic, "active", "主宰受到伤害时效果",
            new Dictionary<string, string> { ["ability"] = "anderstorpRingDraw" });
    }

    private void NotifyS2LegionMoved(int playerIndex, L12CardInstance moved, int fromRow, int toRow)
    {
        var player = State.Players[playerIndex];
        if (player.MasterId != "S02-04M1" || State.ActivePlayer != playerIndex) return;
        var master = CreateCard("S02-04M1", $"master-{playerIndex}");
        var candidates = new List<L12TriggerCandidate>();
        if (fromRow == 1 && toRow == 0)
            player.UsedAbilities.Add($"s2-tsukuyomi-attack:{moved.InstanceId}:{State.TurnSerial}");
        if (fromRow == 0 && toRow == 1 && player.Morale.Any(card => card.Tapped))
            candidates.Add(CreateTriggerCandidate(playerIndex, master, "active", "军团从前排位移至后排时效果",
                new Dictionary<string, string> { ["ability"] = "tsukuyomiReadyMorale", ["moved"] = moved.InstanceId }));
        var key = $"active:master-{playerIndex}:tsukuyomiFollowMove";
        if (!player.UsedAbilities.Contains(key) && player.Morale.Any(card => !card.Tapped)
            && PublicLegions(player).Any(card => card.InstanceId != moved.InstanceId && !card.Tapped))
            candidates.Add(CreateTriggerCandidate(playerIndex, master, "active", "军团位移时效果",
                new Dictionary<string, string> { ["ability"] = "tsukuyomiFollowMove", ["moved"] = moved.InstanceId }));
        QueueTriggerCandidates(candidates);
    }

    private void ReturnWukongMasterLegionAfterAttack(int playerIndex, L12CardInstance attacker)
    {
        if (!attacker.IsMasterLegion || attacker.CardId != "S02-01M1") return;
        ReturnWukongMasterLegions(State.Players[playerIndex], "进攻结算后", resumeEndTurn: false);
    }

    private bool ReturnWukongMasterLegions(L12PlayerState player, string timing, bool resumeEndTurn)
    {
        var returnedAny = false;
        foreach (var masterLegion in PublicLegions(player).Where(card => card.IsMasterLegion && card.CardId == "S02-01M1").ToArray())
        {
            if (FindOnField(player, masterLegion.InstanceId, out var row, out var slot) is null) continue;
            player.Field[row][slot] = null;
            ResetCardAfterLeavingField(masterLegion);
            returnedAny = true;
            AddEvent("return", player.PlayerIndex, $"孙悟空在{timing}返回主宰区", masterLegion);
        }
        if (returnedAny && player.Morale.Count < State.Players[1 - player.PlayerIndex].Morale.Count)
        {
            CreatePrompt(player.PlayerIndex, "optional", "孙悟空返回主宰区：我方士气少于对方，是否追加1张休整士气？",
                ["yes", "no"], 1, 1, "s2-wukong-return-morale", isPrivate: true,
                data: new Dictionary<string, string>
                {
                    ["yes"] = "追加1张休整士气", ["no"] = "不发动",
                    ["resumeEndTurn"] = resumeEndTurn ? "true" : "false",
                });
        }
        RecalculateContinuousTroops();
        return returnedAny;
    }
}
