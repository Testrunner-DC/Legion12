namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private CommandResult? TryBeginS2RemainingAbility(int playerIndex, L12CardInstance source, string ability)
    {
        var player = State.Players[playerIndex];
        switch (ability)
        {
            case "thorHammerRevive" when source.CardId == "S02-0301":
            {
                if (player.MasterId != "S02-03M1" || !player.Graveyard.Contains(source))
                    return CommandResult.Reject("仅〈雷神索尔〉可发动墓地中〈雷神之锤〉的效果");
                var otherGraveCards = player.Graveyard.Where(card => card.InstanceId != source.InstanceId && CanEnterHandOrLibrary(card))
                    .ToArray();
                var slots = EmptySlots(player).ToList();
                if (otherGraveCards.Sum(L12StructuredCardRules.StarterGraveCardCopies) < 3 || slots.Count == 0)
                    return CommandResult.Reject("需要墓地中其他卡牌合计能视为3张，且战场存在空位");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    GraveCostSelectionStep(player, "雷神之锤：依次选择合计视为3张、返回牌库底部的其他墓地卡牌",
                        "graveCost", otherGraveCards, required: 3),
                    new L12ActivationSelectionStep { Kind = "slot", Text = "雷神之锤：预先选择活跃登场的位置", ValidChoices = slots },
                ]);
            }
            case "wukongTransform" when source.CardId == "S02-01M1":
            {
                if (PublicLegions(player).Any(card => card.IsMasterLegion))
                    return CommandResult.Reject("孙悟空已经作为军团登场");
                if (!EmptySlots(player).Any(slot => slot.StartsWith("0:", StringComparison.Ordinal)))
                    return CommandResult.Reject("我方前排没有空位");
                if (player.Morale.Count < 2) return CommandResult.Reject("至少需要返还2张士气");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    new L12ActivationSelectionStep
                    {
                        Kind = "active-target",
                        DeclarationKey = "returnCost",
                        Text = "孙悟空：选择返还2至8张士气",
                        ValidChoices = player.Morale.Select(card => card.InstanceId).ToList(),
                        MinChoose = 2,
                        MaxChoose = Math.Min(8, player.Morale.Count),
                    },
                    new L12ActivationSelectionStep
                    {
                        Kind = "slot",
                        DeclarationKey = "entrySlot",
                        Text = "孙悟空：预先选择前排活跃登场位置",
                        ValidChoices = EmptySlots(player)
                            .Where(slot => slot.StartsWith("0:", StringComparison.Ordinal)).ToList(),
                        MinChoose = 1,
                        MaxChoose = 1,
                    },
                ]);
            }
            case "thorCharge" when source.CardId == "S02-03M1":
                if (player.Hp > 3) return CommandResult.Reject("我方主宰血量需要不高于3");
                return CommitActiveAbility(playerIndex, source, ability, null);
            case "divinityFlipMorale" when source.CardId == "S02-05D1":
                if (!player.Morale.Any(card => !card.IsGodPower)) return CommandResult.Reject("没有可翻转的士气");
                return CommitActiveAbility(playerIndex, source, ability, null);
            case "divinityPower" when source.CardId == "S02-05D1":
                return TryBeginPublicActiveDeclaration(playerIndex, source, ability);
            case "divinityFreePromotion" when source.CardId == "S02-05D1":
                if (player.MasterTapped) return CommandResult.Reject("诸神巅必须为活跃状态");
                return CommitActiveAbility(playerIndex, source, ability, null);
            case "artemisBuff" when source.CardId == "S02-05M1":
            {
                var targets = PublicLegions(player).Where(card => L12StructuredCardRules.HasFaction(player, card, "olympus")
                        && card.CurrentCost is >= 3 and <= 6)
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
                var grave = player.Graveyard.Where(card => L12StructuredCardRules.HasFaction(player, card, "olympus")
                        && card.CardType == "legion" && card.CurrentCost <= 4)
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
            case "thorHammerRevive" when source.CardId == "S02-0301":
            {
                var declared = SplitDeclared(target);
                var slot = declared.SingleOrDefault(value => EmptySlots(player).Contains(value));
                if (player.MasterId != "S02-03M1" || !player.Graveyard.Contains(source) || slot is null
                    || !L12StructuredCardRules.TryResolveGraveCostDeclaration(player,
                        declared.Where(value => value != slot), 3, string.Empty, legionOnly: false,
                        out var costs, out _)
                    || costs.Any(card => card.InstanceId == source.InstanceId))
                    return CommandResult.Reject("墓地卡牌或可用战场位置已失效");
                MoveGraveToLibraryBottom(player, costs);
                player.UsedAbilities.Add(onceKey);
                PushEffect(playerIndex, source, "active", "主动效果",
                    data: new Dictionary<string, string> { ["ability"] = ability, ["slot"] = slot });
                return CommandResult.Ok();
            }
            case "wukongTransform" when source.CardId == "S02-01M1":
            {
                var declared = SplitDeclared(target);
                if (declared.Length is < 3 or > 9) return CommandResult.Reject("需要选择2至8张士气和1个前排位置");
                var slot = declared[^1];
                var ids = declared[..^1];
                if (ids.Length is < 2 or > 8 || ids.Distinct().Count() != ids.Length)
                    return CommandResult.Reject("需要选择2至8张士气");
                var cards = ids.Select(id => player.Morale.FirstOrDefault(card => card.InstanceId == id)).ToArray();
                if (cards.Any(card => card is null)) return CommandResult.Reject("选择的士气已失效");
                if (!EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase)
                    || !slot.StartsWith("0:", StringComparison.Ordinal))
                    return CommandResult.Reject("声明的前排位置已失效");
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
                PushEffect(playerIndex, source, "active", "主神效果",
                    data: new Dictionary<string, string> { ["ability"] = ability });
                return CommandResult.Ok();
            case "divinityPower" when source.CardId == "S02-05D1":
            {
                var declared = SplitDeclared(target);
                if (declared.Length == 0 || declared[0] is not ("mode:recover" or "mode:damage"))
                    return CommandResult.Reject("诸神巅的效果声明不完整");
                var data = new Dictionary<string, string> { ["ability"] = ability, ["mode"] = declared[0] };
                if (declared[0] == "mode:damage")
                {
                    if (declared.Length != 7 || declared.Skip(1).Any(id => id != "mode:none"
                            && DeclaredEnemyTarget(playerIndex, id) is null)
                        || PublicLegions(State.Players[1 - playerIndex]).Any() && declared.Skip(1).Any(id => id == "mode:none"))
                        return CommandResult.Reject("诸神巅的伤害目标声明已失效");
                    data["targets"] = string.Join('|', declared.Skip(1));
                }
                else
                {
                    var recover = declared.Length >= 3
                        ? player.Graveyard.FirstOrDefault(card => card.InstanceId == declared[1]
                            && L12StructuredCardRules.HasFaction(player, card, "olympus"))
                        : null;
                    var noEntry = declared.Length == 3 && declared[2] == "mode:none";
                    var entry = declared.Length == 5
                        ? player.Hand.Concat(player.Graveyard).FirstOrDefault(card => card.InstanceId == declared[2]
                            && L12StructuredCardRules.HasFaction(player, card, "olympus")
                            && card.CardType == "legion" && card.CurrentCost <= 4)
                        : null;
                    var battlefield = entry is null ? null : ParseEffectEntryBattlefieldChoice(declared[3]);
                    var (row, slot) = entry is null ? (-1, -1) : ParseSlot(declared[4]);
                    if (recover is null || !noEntry && (entry is null || battlefield != playerIndex
                            || row is < 0 or > 1 || slot is < 0 or > 2 || player.Field[row][slot] is not null))
                        return CommandResult.Reject("诸神巅的回收卡牌或登场声明已失效");
                    data["recover"] = recover.InstanceId;
                    if (entry is not null)
                    {
                        data["entry"] = entry.InstanceId;
                        data["slot"] = declared[4];
                    }
                    var compositeDeclaration = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["recoverCard"] = [recover.InstanceId],
                        ["entryMode"] = [entry is null ? "mode:none" : "mode:entry"],
                    };
                    if (entry is not null)
                    {
                        compositeDeclaration["entryCard"] = [entry.InstanceId];
                        compositeDeclaration["entrySlot"] = [declared[4]];
                    }
                    data = CompositeFirstSegmentData("active:S02-05D1:divinityRecover", compositeDeclaration);
                    data["ability"] = ability;
                    data["mode"] = declared[0];
                }
                if (!L12S2ZoneOps.ConsumeAndFlipGodPower(player, 2))
                    return CommandResult.Reject("需要2张活跃的神力");
                player.UsedAbilities.Add(onceKey);
                PushEffect(playerIndex, source, "active", "主神效果", data: data);
                return CommandResult.Ok();
            }
            case "divinityFreePromotion" when source.CardId == "S02-05D1":
                if (player.MasterTapped) return CommandResult.Reject("诸神巅必须为活跃状态");
                player.MasterTapped = true;
                PushEffect(playerIndex, source, "active", "主神效果", data: new Dictionary<string, string> { ["ability"] = ability });
                return CommandResult.Ok();
            case "artemisBuff" when source.CardId == "S02-05M1":
            {
                var declared = SplitDeclared(target);
                if (declared.Length != 3) return CommandResult.Reject("阿尔忒弥斯的支付、目标和效果选择不完整");
                var legion = FindOnField(player, declared[1], out _, out _);
                if (legion is null || !L12StructuredCardRules.HasFaction(player, legion, "olympus")
                    || legion.CurrentCost is < 3 or > 6) return CommandResult.Reject("目标已不合法");
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
                var revive = player.Graveyard.FirstOrDefault(card => card.InstanceId == declared[1]
                    && L12StructuredCardRules.HasFaction(player, card, "olympus")
                    && card.CardType == "legion" && card.CurrentCost <= 4);
                if (discard is null || revive is null || !EmptySlots(player).Contains(declared[2])) return CommandResult.Reject("选择的卡牌或位置已失效");
                source.Tapped = true; player.Hand.Remove(discard); player.Graveyard.Add(discard);
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
            case "thorHammerRevive" when source?.CardId == "S02-0301":
            {
                if (player.Graveyard.Contains(source) && EmptySlots(player).Contains(item.Data.GetValueOrDefault("slot")))
                    SummonFromAnyPrivateZone(player, source.InstanceId, item.Data["slot"], tapped: false);
                FinishStackItem(item);
                return true;
            }
            case "wukongTransform" when source?.CardId == "S02-01M1":
            {
                var (row, slot) = ParseSlot(item.Data["slot"]);
                if (row != 0 || slot is < 0 or > 2 || player.Field[row][slot] is not null)
                {
                    AddEvent("effect-cancelled", item.Controller,
                        "孙悟空声明的前排登场位置已失效；登场取消，已返还士气及回合次数不恢复",
                        source);
                    FinishStackItem(item);
                    return true;
                }
                var masterLegion = CreateCard(source.CardId, $"master-legion-{item.Controller}-{State.TurnSerial}");
                masterLegion.OwnerIndex = item.Controller; masterLegion.IsMasterLegion = true; masterLegion.HasCharge = true;
                masterLegion.SummonRound = State.Round;
                masterLegion.SetTroopsValue = int.Parse(item.Data["count"]) * 1000;
                masterLegion.Troops = masterLegion.SetTroopsValue.Value;
                player.Field[row][slot] = masterLegion;
                AddEvent("put", item.Controller, $"孙悟空从主宰区作为兵力{masterLegion.Troops}的【斗士】军团在前排活跃登场", masterLegion);
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
            {
                if (item.Data["mode"] == "mode:damage")
                {
                    foreach (var id in item.Data.GetValueOrDefault("targets", string.Empty)
                        .Split('|', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var target = DeclaredEnemyTarget(item.Controller, id);
                        if (target is not null)
                            AddTimedModifier(target, -1000, 0, ExpiryAtNextOwnEnd(item.Controller), "诸神巅");
                    }
                    FinishStackItem(item);
                    return true;
                }
                if (AtomicFlowKey(item, source) == "divinity-recover")
                {
                    var recoveredId = CompositeDeclared(item, "recoverCard").SingleOrDefault();
                    var recovered = player.Graveyard.FirstOrDefault(card => card.InstanceId == recoveredId
                        && L12StructuredCardRules.HasFaction(player, card, "olympus"));
                    if (recovered is not null)
                    {
                        player.Graveyard.Remove(recovered);
                        AddCardToHandByEffect(player, recovered, "grave",
                            $"诸神巅将〈{recovered.Name}〉从墓地加入手牌");
                    }
                    else
                        AddEvent("effect-cancelled", item.Controller,
                            "诸神巅声明的墓地回收目标已失效；后续登场段仍独立继续");
                    FinishStackItem(item);
                    return true;
                }
                if (AtomicFlowKey(item, source) == "divinity-entry")
                {
                    var entry = CompositeDeclared(item, "entryCard").SingleOrDefault();
                    var slot = CompositeDeclared(item, "entrySlot").SingleOrDefault();
                    if (string.IsNullOrWhiteSpace(entry) || string.IsNullOrWhiteSpace(slot)
                        || !TrySummonFromAnyPrivateZone(player, item.Controller, entry, slot,
                            tapped: false))
                        AddEvent("effect-cancelled", item.Controller,
                            "诸神巅选择的军团或位置已失效；该军团不登场，已支付神力不恢复");
                    FinishStackItem(item);
                    return true;
                }
                FinishStackItem(item);
                return true;
            }
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
            {
                var morale = player.Morale.FirstOrDefault(card => card.InstanceId == PublicTriggerDeclared(item, "moraleTarget")
                    && card.Tapped && !card.IsGodPower);
                if (morale is not null)
                {
                    L12S2ZoneOps.FlipMoraleFace(player, morale.InstanceId, toGodPower: true);
                    AddEvent("morale", item.Controller, "阿尔忒弥斯将声明的休整士气翻转为神力",
                        source is not null ? [source] : []);
                }
                else
                    AddEvent("effect-cancelled", item.Controller,
                        "阿尔忒弥斯选择的士气目标已失效；该项效果不结算，已登记的回合次数不恢复");
                FinishStackItem(item);
                return true;
            }
            case "hippolytaRevive" when source?.CardId == "S02-0510":
                SummonFromAnyPrivateZone(player, item.Data["revive"], item.Data["slot"], tapped: false);
                FinishStackItem(item); return true;
            case "angusTacticTrial" when item.SourceCardId == "S02-06M2":
                FinishStackItem(item);
                return true;
            case "grailRoundTableRune" when item.SourceCardId == "S02-06S4":
                L12S2ZoneOps.GainRunes(player, 1);
                AddEvent("runes", item.Controller, "〈寻找圣杯之旅〉使我方获得1符文",
                    source is not null ? [source] : []);
                FinishStackItem(item);
                return true;
            case "wukongReturnMorale" when item.SourceCardId == "S02-01M1":
                if (PublicTriggerDeclared(item, "mode") == "mode:use" && player.MoraleDeck.Count > 0
                    && player.Morale.Count < State.Players[1 - item.Controller].Morale.Count)
                {
                    var added = AddMorale(player, 1, tapped: true);
                    if (added > 0) AddEvent("morale", item.Controller,
                        "孙悟空返回主宰区后追加1张休整士气", source is null ? [] : [source]);
                }
                else if (PublicTriggerDeclared(item, "mode") == "mode:use")
                    AddEvent("effect-cancelled", item.Controller,
                        "孙悟空返回后的士气条件在结算时失效；追加士气效果取消",
                        source is null ? [] : [source]);
                FinishStackItem(item);
                return true;
            case "anderstorpRingDraw" when item.SourceCardId == "S02-0305":
                if (!Draw(player, 1))
                    SetWinner(1 - item.Controller, "〈安德华拉诺特〉效果抽牌时牌库为空");
                FinishStackItem(item);
                return true;
            case "tsukuyomiFrontAttackBuff" when item.SourceCardId == "S02-04M1":
            {
                var legion = FindOnField(player, item.Data.GetValueOrDefault("target"), out _, out _);
                if (legion is not null)
                {
                    if (legion.TsukuyomiFrontMoveBonusTurn != State.TurnSerial)
                    {
                        legion.TsukuyomiFrontMoveBonusTurn = State.TurnSerial;
                        legion.TsukuyomiFrontMoveBonusCount = 0;
                    }
                    legion.TsukuyomiFrontMoveBonusCount++;
                    AddEvent("effect", item.Controller,
                        $"月读使〈{legion.Name}〉本回合每次进攻兵力+1000", legion);
                }
                else
                    AddEvent("effect-cancelled", item.Controller,
                        "月读后排位移至前排的军团已离场；本次进攻兵力效果取消");
                FinishStackItem(item);
                return true;
            }
            case "tsukuyomiFollowMove" when item.SourceCardId == "S02-04M1":
            {
                var targetId = PublicTriggerDeclared(item, "target");
                var destination = PublicTriggerDeclared(item, "slot");
                var targetController = int.TryParse(item.Data.GetValueOrDefault("targetPlayerIndex"), out var parsedController)
                    && parsedController is >= 0 and <= 1 ? parsedController : -1;
                var targetPlayer = targetController >= 0 ? State.Players[targetController] : null;
                var oldRow = -1;
                var oldSlot = -1;
                var legion = targetPlayer is null ? null
                    : FindOnField(targetPlayer, targetId, out oldRow, out oldSlot);
                if (legion is not null && !legion.Tapped
                    && legion.InstanceId != item.Data.GetValueOrDefault("moved")
                    && AdjacentEmptySlots(targetPlayer!, oldRow, oldSlot).Contains(destination, StringComparer.OrdinalIgnoreCase))
                {
                    var (newRow, newSlot) = ParseSlot(destination);
                    targetPlayer!.Field[oldRow][oldSlot] = null;
                    targetPlayer.Field[newRow][newSlot] = legion;
                    legion.LastMovedTurn = State.TurnSerial;
                    AddTimedModifier(legion, 0, -1, ExpiryAtNextOwnEnd(item.Controller), "月读");
                    AddEvent("move", item.Controller, $"月读使〈{legion.Name}〉位移1格", legion);
                    RecordLegionMovement(targetController, legion, oldRow, newRow);
                }
                else
                    AddEvent("effect-cancelled", item.Controller, "月读的公开位移目标或位置已失效；已支付费用不回滚");
                FinishStackItem(item);
                return true;
            }
            case "tsukuyomiReadyMorale" when item.SourceCardId == "S02-04M1":
            {
                var morale = player.Morale.FirstOrDefault(card => card.InstanceId == PublicTriggerDeclared(item, "morale") && card.Tapped);
                if (morale is not null)
                {
                    morale.Tapped = false;
                    AddEvent("morale", item.Controller, "月读使选择的休整士气转为活跃");
                }
                else
                    AddEvent("effect-cancelled", item.Controller, "月读选择的休整士气已失效；该士气无法转为活跃");
                FinishStackItem(item);
                return true;
            }
            default:
                return false;
        }
    }

    private void ResolveS2TrojanHorseAfterAttack(L12StackItem item)
    {
        var owner = State.Players[item.Controller];
        var horse = FindOnField(owner, item.SourceInstanceId, out _, out _);
        var host = int.TryParse(item.Data.GetValueOrDefault("attacker"), out var attacker) && attacker is >= 0 and <= 1
            ? State.Players[attacker]
            : State.Players[1 - item.Controller];
        var destination = PublicTriggerDeclared(item, "slot");
        if (!IsSetTrojanHorse(horse) || !EmptySlots(host).Contains(destination, StringComparer.OrdinalIgnoreCase))
        {
            AddEvent("effect-cancelled", item.Controller, "特洛伊木马选择的来源或置入位置已失效；该卡不置入战场");
            FinishStackItem(item);
            return;
        }
        var resolvedHorse = horse!;
        _ = FindOnField(owner, resolvedHorse.InstanceId, out var sourceRow, out var sourceSlot);
        var (row, slot) = ParseSlot(destination);
        owner.Field[sourceRow][sourceSlot] = null;
        resolvedHorse.OwnerIndex = item.Controller;
        resolvedHorse.Hidden = false;
        resolvedHorse.SetRound = State.Round;
        resolvedHorse.DiscardAtEndOfTurnUntilTurn = ExpiryAtNextOwnEnd(item.Controller);
        host.Field[row][slot] = resolvedHorse;
        AddEvent("put", item.Controller, $"{resolvedHorse.Name}置入{host.Name}战场，直到下个我方回合结束", resolvedHorse);
        RecalculateContinuousTroops();
        FinishStackItem(item);
    }

    private bool TryContinueS2RemainingEffect(L12StackItem item, L12Prompt prompt, List<string> chosen)
    {
        switch (prompt.Data.GetValueOrDefault("action"))
        {
            default:
                return false;
        }
    }

    private static bool IsTrojanHorse(L12CardInstance? card) => card is { CardId: "S02-0523" };

    private static bool IsSetTrojanHorse(L12CardInstance? card) => IsTrojanHorse(card) && card!.Hidden;

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
            new Dictionary<string, string>
            {
                ["ability"] = "grailRoundTableRune", ["entered"] = legion.InstanceId,
                ["onceKey"] = key, ["cleanupReservation"] = pendingKey,
            });
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
        var pendingKey = $"{key}:pending";
        if (player.MasterId != "S02-05M1" || !defeated.LastKnownWasRanged
            || !player.Morale.Any(card => card.Tapped && !card.IsGodPower)
            || player.UsedAbilities.Contains(key) || !player.UsedAbilities.Add(pendingKey)) return null;
        var master = CreateCard("S02-05M1", $"master-{owner}");
        return CreateTriggerCandidate(owner, master, "active", "我方远程军团阵亡时效果",
            new Dictionary<string, string>
            {
                ["ability"] = "artemisDeathFlip", ["defeated"] = defeated.CardId,
                ["onceKey"] = key, ["cleanupReservation"] = pendingKey,
            });
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
        for (var hostIndex = 0; hostIndex < State.Players.Length; hostIndex++)
        {
            var host = State.Players[hostIndex];
            for (var row = 0; row < host.Field.Length; row++)
            for (var slot = 0; slot < host.Field[row].Length; slot++)
            {
                var delayed = host.Field[row][slot];
                if (delayed is null || L12StructuredCardRules.HasDedicatedEndTurnDiscardRoute(delayed.CardId)
                    || delayed.OwnerIndex != endingPlayer
                    || delayed.DiscardAtEndOfTurnUntilTurn < 0
                    || delayed.DiscardAtEndOfTurnUntilTurn > State.TurnSerial) continue;
                _ = RemoveFromField(host, delayed, true, "在所有者回合结束时弃置",
                    leaveKind: L12FieldLeaveKind.Discard);
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
        var pendingKey = $"{key}:pending";
        if (State.ActivePlayer != playerIndex || player.Relic?.CardId != "S02-0305"
            || player.UsedAbilities.Contains(key) || !player.UsedAbilities.Add(pendingKey))
            return null;
        return CreateTriggerCandidate(playerIndex, player.Relic, "active", "主宰受到伤害时效果",
            new Dictionary<string, string>
            {
                ["ability"] = "anderstorpRingDraw",
                ["onceKey"] = key, ["cleanupReservation"] = pendingKey,
            });
    }

    private void RecordLegionMovement(int playerIndex, L12CardInstance moved, int fromRow, int toRow)
    {
        moved.LastMovedTurn = State.TurnSerial;
        var player = State.Players[playerIndex];
        if (fromRow == 1 && toRow == 0)
        {
            var watcher = State.Players[1 - playerIndex];
            foreach (var source in PublicLegions(watcher).Where(card =>
                         L12StructuredCardRules.StarterRemainingPlan(card.CardId, "opponent-back-to-front")
                         == "tomb-defender-debuff"))
            {
                var onceKey = $"trigger:starter-tomb-defender:{source.InstanceId}:{State.TurnSerial}";
                var pendingKey = $"{onceKey}:pending";
                if (watcher.UsedAbilities.Contains(onceKey) || !watcher.UsedAbilities.Add(pendingKey)) continue;
                QueueTriggerCandidates([
                    CreateTriggerCandidate(watcher.PlayerIndex, source, "opponent-back-to-front",
                        "对方军团从后排位移至前排时效果", new Dictionary<string, string>
                        {
                            ["target"] = moved.InstanceId,
                            ["onceKey"] = onceKey,
                            ["cleanupReservation"] = pendingKey,
                        })
                ]);
            }
        }
        if (player.MasterId != "S02-04M1" || State.ActivePlayer != playerIndex) return;
        var master = CreateCard("S02-04M1", $"master-{playerIndex}");
        var candidates = new List<L12TriggerCandidate>();
        if (fromRow == 1 && toRow == 0)
            candidates.Add(CreateTriggerCandidate(playerIndex, master, "active",
                "军团从后排位移至前排时效果", new Dictionary<string, string>
                {
                    ["ability"] = "tsukuyomiFrontAttackBuff", ["target"] = moved.InstanceId,
                }));
        if (fromRow == 0 && toRow == 1 && player.Morale.Any(card => card.Tapped))
            candidates.Add(CreateTriggerCandidate(playerIndex, master, "active", "军团从前排位移至后排时效果",
                new Dictionary<string, string> { ["ability"] = "tsukuyomiReadyMorale", ["moved"] = moved.InstanceId }));
        var key = $"active:master-{playerIndex}:tsukuyomiFollowMove";
        if (!player.UsedAbilities.Contains(key) && player.Morale.Any(card => !card.Tapped)
            && State.Players.Any(targetController => PublicLegions(targetController).Any(card =>
                card.InstanceId != moved.InstanceId && !card.Tapped
                && FindOnField(targetController, card.InstanceId, out var row, out var slot) is not null
                && AdjacentEmptySlots(targetController, row, slot).Any())))
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
        L12CardInstance? returnedSnapshot = null;
        foreach (var masterLegion in PublicLegions(player).Where(card => card.IsMasterLegion && card.CardId == "S02-01M1").ToArray())
        {
            if (FindOnField(player, masterLegion.InstanceId, out var row, out var slot) is null) continue;
            returnedSnapshot ??= CaptureLastKnownSourceSnapshot(masterLegion);
            player.Field[row][slot] = null;
            ResetCardAfterLeavingField(masterLegion);
            returnedAny = true;
            AddEvent("return", player.PlayerIndex, $"孙悟空在{timing}返回主宰区", masterLegion);
        }
        if (returnedSnapshot is not null && player.Morale.Count < State.Players[1 - player.PlayerIndex].Morale.Count
            && player.MoraleDeck.Count > 0)
            QueueTriggerCandidates([
                CreateTriggerCandidate(player.PlayerIndex, returnedSnapshot, "active",
                    "孙悟空返回主宰区后的可选士气效果",
                    new Dictionary<string, string>
                    {
                        ["ability"] = "wukongReturnMorale",
                        ["resumeEndTurn"] = resumeEndTurn ? "true" : "false",
                    }, returnedSnapshot)
            ]);
        RecalculateContinuousTroops();
        return returnedAny;
    }
}
