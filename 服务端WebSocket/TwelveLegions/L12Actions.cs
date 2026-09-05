namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private static bool HasOptionalSelfDamageEntryDiscount(L12CardInstance card)
        => L12StructuredCardRules.HasOptionalSelfDamageEntryDiscount(card.CardId);

    private void CreateOptionalGraveEntryCostPrompt(int playerIndex, string kind, string text,
        IReadOnlyCollection<L12CardInstance> cards, int minimum, int maximum, string continuation,
        Dictionary<string, string> data)
    {
        foreach (var candidate in cards) AddPromptCardData(data, candidate);
        CreateMappedChoicePrompt(playerIndex, kind, text, cards.Select(candidate => candidate.InstanceId),
            minimum, maximum, continuation, data, isPrivate: true);
    }

    private void CreateMappedChoicePrompt(int playerIndex, string kind, string text,
        IEnumerable<string> choices, int minimum, int maximum, string continuation,
        Dictionary<string, string> data, bool isPrivate = false)
        => CreatePrompt(playerIndex, kind, text, choices, minimum, maximum, continuation,
            isPrivate: isPrivate, data: data);

    private bool TryCreateGraveRepresentationPrompt(int playerIndex,
        IReadOnlyList<L12CardInstance> selectedCards, string faction, int minimum, int maximum,
        bool legionOnly, string continuation, Dictionary<string, string> data)
    {
        var choices = L12StructuredCardRules.GraveRepresentationChoices(State.Players[playerIndex], selectedCards,
            faction, minimum, maximum, legionOnly);
        if (choices.Count == 0) return false;
        foreach (var pair in choices) data[pair.Key] = pair.Value;
        CreateMappedChoicePrompt(playerIndex, "option",
            $"墓地费用：选择〈渴求死亡的勇士〉本次视为几张{(legionOnly ? "军团" : "卡牌")}",
            choices.Keys, 1, 1, continuation, data, isPrivate: true);
        return true;
    }

    private CommandResult PlayCard(int playerIndex, L12Command command)
    {
        if (!CanAct(playerIndex)) return CommandResult.Reject("只能在自己的主要阶段行动");
        var player = State.Players[playerIndex];
        var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == command.CardInstanceId);
        if (card is null) return CommandResult.Reject("卡牌不在手牌中");
        var targetPlayerIndex = card.CardId == "S01-0004" ? command.TargetPlayerIndex ?? playerIndex : playerIndex;
        if (targetPlayerIndex is < 0 or > 1) return CommandResult.Reject("目标战场无效");
        if (card.CardId != "S01-0004" && command.TargetPlayerIndex is not null && command.TargetPlayerIndex != playerIndex)
            return CommandResult.Reject("该卡牌不能置入对方战场");
        var targetBattlefield = State.Players[targetPlayerIndex];
        if (L12StructuredCardRules.HandPlayBlockReason(player, card) is { } playBlockReason)
            return CommandResult.Reject(playBlockReason);
        if (State.ActiveDisaster?.CardId == "S02-DS01" && card.CardType == "legion"
            && player.Library.FirstOrDefault() is { } visibleTop
            && !string.IsNullOrWhiteSpace(card.Profession)
            && card.Profession == visibleTop.Profession)
            return CommandResult.Reject($"〈天地异变〉持续期间，无法从手牌打出与牌库顶部相同兵种（{card.Profession}）的军团");
        if (card.CardId == "S02-0306" && player.MasterDamageTakenThisTurn < 2)
            return CommandResult.Reject("本回合我方主宰受到的累计伤害不足2点");
        if (card.CardId == "S02-0306" && player.UsedAbilities.Contains("s2-mimir-used"))
            return CommandResult.Reject("〈密米尔之泉〉每回合只可使用1次");
        if (IsCounterTactic(card.CardId)) return SetCounterTactic(playerIndex, card, command);
        var christinaReplacementKey = $"starter-christina-free-tactic:{State.TurnSerial}";
        var usesChristinaReplacement = card.CardType == "tactic" && !IsCounterTactic(card.CardId)
            && card.CurrentCost <= 3 && player.UsedAbilities.Contains(christinaReplacementKey);
        Dictionary<string, List<string>>? compositeDeclaration = null;
        if (L12CompositeEffectPlans.HasHandPlayPlan(card.CardId))
        {
            if (L12CompositeEffectPlans.RequiresHandPlayDeclaration(card.CardId))
            {
                if (!TryDecodeCompositeDeclaration(command.Choice, out var decoded))
                    return BeginCompositeHandPlayDeclaration(playerIndex, card);
                if (!ValidateCompositeHandPlayDeclaration(playerIndex, card, decoded, out var declarationError))
                    return CommandResult.Reject(declarationError);
                compositeDeclaration = decoded;
            }
            else compositeDeclaration = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
        if (L12StructuredCardRules.RequiresPreStackHandPlayTarget(card.CardId)
            && command.Target?.Type != "legion")
        {
            var targets = PublicLegions(State.Players[1 - playerIndex]).Select(target => target.InstanceId).ToArray();
            return BeginPendingHandPlay(playerIndex, card, targets, "槲寄生符咒：选择对方1张军团");
        }
        if (L12StructuredCardRules.RequiresPreStackHandPlayTarget(card.CardId)
            && DeclaredEnemyTarget(playerIndex, command.Target!.InstanceId) is null)
            return CommandResult.Reject("槲寄生符咒的目标已不合法");
        if (IsS2PromotionCard(card)
            && command.Choice?.StartsWith("normal-entry", StringComparison.Ordinal) != true)
            return BeginS2PromotionEntry(playerIndex, card, command);
        if (card.CardType == "legion" && (command.Row is null or < 0 or > 1 || command.Slot is null or < 0 or > 2))
            return CommandResult.Reject("请选择合法阵地");
        if (card.CardType == "legion" && State.ActiveDisaster?.CardId == "S01-DS03" && command.Row == 1)
            return CommandResult.Reject("《腐秽大地》持续期间后排无法放置军团");
        if (card.CardType == "legion" && targetBattlefield.Field[command.Row!.Value][command.Slot!.Value] is { } occupant)
        {
            var canReplaceOwnCounter = targetPlayerIndex == playerIndex && command.Row == 1 && IsCounterTactic(occupant.CardId);
            if (!canReplaceOwnCounter) return CommandResult.Reject("阵地已被占用");
        }

        if (card.CardId == "ST03-01" && command.Choice?.StartsWith("sigurd:", StringComparison.Ordinal) != true)
        {
            var choices = player.Graveyard.Where(CanEnterHandOrLibrary).ToArray();
            if (choices.Length == 0)
                command = command with { Choice = "sigurd:" };
            else
            {
                var data = new Dictionary<string, string>
                {
                    ["cardInstanceId"] = card.InstanceId,
                    ["row"] = command.Row!.Value.ToString(),
                    ["slot"] = command.Slot!.Value.ToString(),
                    ["targetPlayerIndex"] = targetPlayerIndex.ToString(),
                };
                CreateOptionalGraveEntryCostPrompt(playerIndex, "card-select",
                    "蛇眼西格德：可选择墓地1张牌返回牌库底部，使此军团登场费用-1",
                    choices, 0, 1, "starter-sigurd-grave-cost", data);
                return CommandResult.Ok();
            }
        }

        if (card.CardId == "S02-0302" && command.Choice?.StartsWith("rollo:", StringComparison.Ordinal) != true)
        {
            var choices = player.Graveyard.Where(candidate => candidate.Faction == "asgard" && CanEnterHandOrLibrary(candidate))
                .ToArray();
            if (choices.Length == 0)
                command = command with { Choice = "rollo:" };
            else
            {
                CreateOptionalGraveEntryCostPrompt(playerIndex, "order", "〈步行者罗洛〉：依选择顺序将墓地最多8张【阿斯加德】卡牌返回牌库底部",
                    choices, 0, Math.Min(8, choices.Length), "s2-rollo-grave-cost",
                    new Dictionary<string, string>
                    {
                        ["cardInstanceId"] = card.InstanceId,
                        ["row"] = command.Row!.Value.ToString(),
                        ["slot"] = command.Slot!.Value.ToString(),
                        ["targetPlayerIndex"] = targetPlayerIndex.ToString(),
                    });
                return CommandResult.Ok();
            }
        }

        if (card.CardId == "S02-0622" && command.Choice?.Contains("runes:", StringComparison.Ordinal) != true)
        {
            var maximum = Math.Min(player.SpecialZones.Runes, (card.Cost + 1) / 2);
            if (maximum == 0)
            {
                command = command with { Choice = "runes:0" };
            }
            else
            {
                var choices = Enumerable.Range(1, maximum).Select(index => $"rune:{index}").ToArray();
                CreatePrompt(playerIndex, "resource-payment", "〈槲寄生符咒〉：请直接点击要消耗的符文", choices, 0, maximum,
                    "s2-mistletoe-rune-cost", data: new Dictionary<string, string>
                    {
                        ["cardInstanceId"] = card.InstanceId,
                        ["targetInstanceId"] = command.Target?.InstanceId ?? string.Empty,
                        ["choiceMode"] = "resource-payment",
                        ["resourceKind"] = "rune",
                    });
                return CommandResult.Ok();
            }
        }

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
                    ["targetPlayerIndex"] = targetPlayerIndex.ToString(),
                    ["normalCost"] = normalCost.ToString(),
                    ["discountedCost"] = discountedCost.ToString(),
                    ["choiceMode"] = "instant",
                    ["yes"] = $"是（主宰受到1点伤害，支付{discountedCost}士气）",
                    ["no"] = $"否（支付{normalCost}士气）",
                });
            return CommandResult.Ok();
        }

        var usedAsgardSelfDamageDiscount = command.Choice?.StartsWith("self-damage-cost", StringComparison.Ordinal) == true && mayUseSelfDamageDiscount;
        var mistletoeRunes = card.CardId == "S02-0622"
            ? ParseDeclaredRuneCount(command.Choice, player.SpecialZones.Runes)
            : 0;
        var rolloReturns = card.CardId == "S02-0302" ? ParseRolloGraveOrder(command.Choice) : [];
        var rolloRepresentation = card.CardId == "S02-0302" ? ParseRolloGraveRepresentation(command.Choice) : null;
        var sigurdReturnId = card.CardId == "ST03-01" && command.Choice?.StartsWith("sigurd:", StringComparison.Ordinal) == true
            ? command.Choice["sigurd:".Length..]
            : string.Empty;
        var sigurdReturn = string.IsNullOrWhiteSpace(sigurdReturnId)
            ? null
            : player.Graveyard.FirstOrDefault(candidate => candidate.InstanceId == sigurdReturnId && CanEnterHandOrLibrary(candidate));
        if (!string.IsNullOrWhiteSpace(sigurdReturnId) && sigurdReturn is null)
            return CommandResult.Reject("〈蛇眼西格德〉选择的墓地卡牌已失效");
        var rolloCards = rolloReturns.Select(id => player.Graveyard.FirstOrDefault(candidate => candidate.InstanceId == id
                && candidate.Faction == "asgard" && CanEnterHandOrLibrary(candidate)))
            .ToArray();
        var representedRolloCount = 0;
        if (rolloReturns.Length > 8 || rolloReturns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != rolloReturns.Length
            || rolloReturns.Any(id => !player.Graveyard.Any(candidate => candidate.InstanceId == id
                && candidate.Faction == "asgard" && CanEnterHandOrLibrary(candidate)))
            || rolloReturns.Length > 0 && (!L12StructuredCardRules.TryGetGraveRepresentationCount(player,
                    rolloCards.OfType<L12CardInstance>().ToArray(), rolloRepresentation, "asgard", legionOnly: false,
                    out representedRolloCount) || representedRolloCount > 8))
            return CommandResult.Reject("〈步行者罗洛〉选择的墓地卡牌已失效或数量不合法");
        var rolloReturnCount = rolloReturns.Length == 0 ? 0 : representedRolloCount;
        var cost = GetPlayCostWithSigurdDiscount(playerIndex, card, usedAsgardSelfDamageDiscount, mistletoeRunes, rolloReturnCount,
            useSigurdDiscount: sigurdReturn is not null);
        var compositeReservation = CompositeReservedBasePayment(compositeDeclaration);
        if (ActiveResourceCountExcluding(player, compositeReservation.ResourceIds,
                compositeReservation.TemporaryMorale) < cost)
            return CommandResult.Reject("预留复合效果费用后，活跃士气不足");
        if (mistletoeRunes > 0 && player.SpecialZones.Runes < mistletoeRunes)
            return CommandResult.Reject("可用符文数量不足");
        var paymentChoice = EnsurePlayResourcePaymentChoice(playerIndex, card, command, cost,
            compositeReservation.ResourceIds, compositeReservation.TemporaryMorale);
        if (paymentChoice is not null) return paymentChoice;
        var paid = command.CardInstanceIds is not null
            ? TryConsumeSelectedResources(player, cost, command.CardInstanceIds,
                compositeReservation.ResourceIds, compositeReservation.TemporaryMorale)
            : TryConsumeSelectedResources(player, cost,
                SelectAutomaticOrdinaryResourcePaymentIds(player, cost, compositeReservation.ResourceIds,
                    compositeReservation.TemporaryMorale),
                compositeReservation.ResourceIds, compositeReservation.TemporaryMorale);
        if (!paid) return CommandResult.Reject("选择的支付资源已失效或数量不正确");
        if (usesChristinaReplacement)
        {
            player.UsedAbilities.Remove(christinaReplacementKey);
            DamageMaster(playerIndex, 1, "克里斯蒂娜主动效果");
            AddEvent("cost", playerIndex, "克里斯蒂娜使本次主动战术无需消耗费用，改为我方主宰受到1点伤害", card);
        }
        if (rolloReturns.Length > 0)
            MoveGraveToLibraryBottom(player, rolloReturns.Select(id => player.Graveyard.First(card => card.InstanceId == id)).ToArray());
        if (sigurdReturn is not null)
            MoveGraveToLibraryBottom(player, [sigurdReturn]);
        if (card.CardType == "legion")
        {
            if (command.Row is null or < 0 or > 1 || command.Slot is null or < 0 or > 2)
                return CommandResult.Reject("请选择合法阵地");
            var row = command.Row.GetValueOrDefault();
            var slot = command.Slot.GetValueOrDefault();
            if (State.ActiveDisaster?.CardId == "S01-DS03" && row == 1)
                return CommandResult.Reject("〈腐秽大地〉持续期间后排无法放置军团");
            var occupyingCard = targetBattlefield.Field[row][slot];
            var displacesOwnCounter = targetPlayerIndex == playerIndex && row == 1 && occupyingCard is not null && IsCounterTactic(occupyingCard.CardId);
            if (occupyingCard is not null && !displacesOwnCounter) return CommandResult.Reject("阵地已被占用");
            player.Hand.Remove(card);
            if (displacesOwnCounter)
            {
                occupyingCard!.Hidden = false;
                ResetCardAfterLeavingField(occupyingCard);
                player.Graveyard.Add(occupyingCard);
                AddEvent("counter-displaced", playerIndex,
                    $"{player.Name} 打出军团并将自己覆盖的反击战术〈{occupyingCard.Name}〉置入墓地", occupyingCard);
            }
            card.OwnerIndex ??= playerIndex;
            card.SummonRound = State.Round;
            targetBattlefield.Field[row][slot] = card;
        }
        else if (card.CardType == "artifact")
        {
            player.Hand.Remove(card);
            card.OwnerIndex ??= playerIndex;
            // 圣物的实际打出回合也是其入场回合；若同回合随后被规则视为军团，
            // 公共进攻合法性仍应据此施加召唤失调。
            card.SummonRound = State.Round;
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
            if (mistletoeRunes > 0) L12S2ZoneOps.SpendRunes(player, mistletoeRunes);
            player.Hand.Remove(card);
            player.Resolving.Add(card);
            if (!TryCommitCompositePreStackCosts(playerIndex, card, compositeDeclaration))
                return CommandResult.Reject("复合战术的发动费用已失效；未发生部分支付");
        }

        ApplyDisasterLevelOnEntry(playerIndex, card, deferTriggerUntilStackSettles: true);
        AddEvent("play", playerIndex, $"{player.Name} 打出 {card.Name}", card);
        if (card.CardId == "S01-0004" && targetPlayerIndex != playerIndex)
            AddEvent("put", targetPlayerIndex, $"{card.Name}置入{targetBattlefield.Name}的战场，由{targetBattlefield.Name}控制，所有者仍为{player.Name}", card);
        if (usedAsgardSelfDamageDiscount) DamageMaster(playerIndex, 1, $"{card.Name}的登场费用减免");
        if (card.CardType == "tactic" && !IsCounterTactic(card.CardId))
        {
            player.LastActiveTacticCardId = card.CardId;
            player.LastActiveTacticTurnSerial = State.TurnSerial;
        }
        ResolveOnPlayContinuousEffects(playerIndex, card);
        RecalculateContinuousTroops();
        if (card.CardId == "S01-0403" && player.UsedAbilities.Remove("s2-fortune-next-uesugi"))
        {
            card.HasCharge = true;
            AddEvent("effect", playerIndex, "〈武运在天 铠甲在前〉使〈上杉谦信〉获得冲锋", card);
        }
        if (card.CardType == "legion" && card.Faction == player.Faction && player.NextFactionLegionDiscount > 0)
            player.NextFactionLegionDiscount = 0;
        if (card.CardType == "legion" && card.Faction == "taiyangcheng" && card.DisasterLevel > 0)
            player.NextS2SunDisasterLegionDiscount = 0;
        if (card.CardType == "legion" && L12StructuredCardRules.HasFaction(player, card, "olympus")
            && player.NextS2OlympusLegionDiscount > 0)
            player.NextS2OlympusLegionDiscount = 0;
        if (card.CardType == "legion" && L12StructuredCardRules.HasFaction(player, card, "otherworld")
            && player.NextOtherworldLegionEntryDiscount > 0)
            player.NextOtherworldLegionEntryDiscount = 0;
        if (card.CardType == "legion" && player.NextLegionEntryDiscount > 0)
            player.NextLegionEntryDiscount = 0;
        if (card.CardType == "tactic" && player.FreeTacticCount > 0)
            player.FreeTacticCount--;
        else if (card.CardType == "tactic" && !IsCounterTactic(card.CardId))
            player.UsedAbilities.Remove("ds01-free-tactic");

        var trigger = card.CardType is "legion" or "artifact" ? "enter" : "play";
        if (HasImmediateEffect(card, trigger))
        {
            State.CheckDisasterAfterStack |= card.CardType == "legion" && State.DisasterValue > 8;
            if (trigger == "enter" && L12StructuredCardRules.RequiresPreStackEnterCost(card))
                BeginYingzhengEnterActivation(playerIndex, card);
            else
            {
                Dictionary<string, string>? declaredData = compositeDeclaration is not null
                    ? CompositeFirstSegmentData(card.CardId, compositeDeclaration)
                    : L12StructuredCardRules.RequiresPreStackHandPlayTarget(card.CardId)
                        && command.Target is { Type: "legion" }
                        ? new Dictionary<string, string> { ["target"] = command.Target.InstanceId ?? string.Empty }
                        : null;
                QueueOrPushTriggeredEffect(playerIndex, card, trigger,
                    trigger == "enter" ? "【登场时】效果" : "战术效果",
                    targets: compositeDeclaration is null ? null : CompositeFirstSegmentTargets(card.CardId, compositeDeclaration),
                    data: declaredData);
            }
            if (card.CardType == "legion") QueueS2GrailRoundTableEntry(playerIndex, card);
        }
        else
        {
            if (player.Resolving.Remove(card))
            {
                ResetCardAfterLeavingField(card);
                player.Graveyard.Add(card);
            }
            if (card.CardType == "legion") QueueS2GrailRoundTableEntry(playerIndex, card);
            TrySettleScheduledDisasterIfIdle();
        }
        return CommandResult.Ok();
    }

    private static bool IsS2PromotionCard(L12CardInstance card)
        => card.Faction == "olympus" && card.CardType == "legion"
            && card.HasTrait("晋升者");

    private static readonly IReadOnlyDictionary<string, string> S2PromotionFoundationCardIds
        = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["S02-0501"] = "S02-0502",
        ["S02-0503"] = "S02-0504",
        ["S02-0505"] = "S02-0506",
        ["S02-0507"] = "S02-0508",
        ["ST05-01"] = "S02-0512",
    };

    private static string? S2PromotionFoundationCardId(string promotionCardId)
        => S2PromotionFoundationCardIds.GetValueOrDefault(promotionCardId);

    private static string S2PromotionFoundationName(L12CardInstance promoted)
        => promoted.Name.EndsWith("·晋升", StringComparison.Ordinal)
            ? promoted.Name[..^"·晋升".Length]
            : promoted.Name;

    private static L12CardInstance[] S2PromotionFoundations(L12PlayerState player, L12CardInstance promoted)
    {
        var foundationCardId = S2PromotionFoundationCardId(promoted.CardId);
        var foundationName = S2PromotionFoundationName(promoted);
        return PublicLegions(player)
            .Where(card => card.Faction == "olympus"
                && (card.CardId == foundationCardId || card.Name == foundationName)
                && !card.HasTrait("晋升者"))
            .ToArray();
    }

    private Dictionary<string, string[]> BuildS2PromotionOptions(L12PlayerState player)
    {
        var activeGodPower = player.Morale.Count(card => card.IsGodPower && !card.Tapped);
        return player.Hand
            .Where(IsS2PromotionCard)
            .Select(promoted => new
            {
                Promoted = promoted,
                Cost = Math.Max(0, S2PromotionGodPowerCost(promoted) - player.NextS2PromotionGodPowerDiscount),
                Foundations = S2PromotionFoundations(player, promoted),
            })
            .Where(option => option.Cost <= activeGodPower && option.Foundations.Length > 0)
            .ToDictionary(option => option.Promoted.InstanceId,
                option => option.Foundations.Select(card => card.InstanceId).ToArray(),
                StringComparer.Ordinal);
    }

    private static int S2PromotionGodPowerCost(L12CardInstance card)
    {
        const string marker = "消耗并翻转";
        var start = card.EffectText?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
        if (start < 0) return 0;
        start += marker.Length;
        var end = card.EffectText!.IndexOf("神力", start, StringComparison.Ordinal);
        return end > start && int.TryParse(card.EffectText.AsSpan(start, end - start), out var parsed)
            ? parsed
            : 0;
    }

    private CommandResult BeginS2PromotionEntry(int playerIndex, L12CardInstance promoted, L12Command command)
    {
        if (command.Choice?.StartsWith("promotion:", StringComparison.Ordinal) == true
            || command.Choice == "promotion-mode")
            return PlayS2Promotion(playerIndex, promoted, command);

        var player = State.Players[playerIndex];
        var hasFoundation = S2PromotionFoundations(player, promoted).Length > 0;
        var promotionCost = Math.Max(0, S2PromotionGodPowerCost(promoted) - player.NextS2PromotionGodPowerDiscount);
        var canPromote = hasFoundation && player.Morale.Count(card => card.IsGodPower && !card.Tapped) >= promotionCost;
        var normalCost = GetPlayCost(playerIndex, promoted, useSelfDamageDiscount: false);
        var hasNormalSlot = command.Row is >= 0 and <= 1 && command.Slot is >= 0 and <= 2
            && player.Field[command.Row.Value][command.Slot.Value] is null;
        var canEnterNormally = hasNormalSlot && ActiveResourceCount(player) >= normalCost;

        if (!canPromote && !canEnterNormally)
            return CommandResult.Reject(hasFoundation ? "活跃士气或活跃神力不足" : "没有合法阵地或可供晋升的同名军团");
        if (canPromote && !canEnterNormally)
            return PlayS2Promotion(playerIndex, promoted, command with { Choice = "promotion-mode" });
        if (!canPromote)
            return PlayCard(playerIndex, command with { Choice = "normal-entry" });

        var data = new Dictionary<string, string>
        {
            ["cardInstanceId"] = promoted.InstanceId,
            ["row"] = command.Row!.Value.ToString(),
            ["slot"] = command.Slot!.Value.ToString(),
            ["normal"] = $"正常登场（支付{normalCost}士气并选择当前战场位置）",
            ["promotion"] = $"晋升登场（消耗并翻转{promotionCost}神力，叠放至同名军团上方）",
            ["cancel"] = "取消打出",
            ["choiceMode"] = "instant",
        };
        CreatePrompt(playerIndex, "option", $"{promoted.Name}：选择登场方式", ["cancel", "normal", "promotion"], 1, 1,
            "s2-promotion-mode", isPrivate: true, data: data);
        return CommandResult.Ok();
    }

    private CommandResult PlayS2Promotion(int playerIndex, L12CardInstance promoted, L12Command command)
    {
        var player = State.Players[playerIndex];
        var foundations = S2PromotionFoundations(player, promoted);
        if (foundations.Length == 0) return CommandResult.Reject("战场上没有可供晋升的同名非【晋升者】军团");

        var foundationId = command.Choice?.StartsWith("promotion:", StringComparison.Ordinal) == true
            ? command.Choice["promotion:".Length..]
            : null;
        if (foundationId is null)
        {
            var data = new Dictionary<string, string>
            {
                ["cardInstanceId"] = promoted.InstanceId,
                ["choiceMode"] = "instant",
            };
            foreach (var candidate in foundations) AddPromptCardData(data, candidate);
            CreatePrompt(playerIndex, "friendly-target", $"{promoted.Name}：选择要叠放的同名非【晋升者】军团",
                foundations.Select(card => card.InstanceId), 1, 1, "s2-promotion-foundation", isPrivate: true, data: data);
            return CommandResult.Ok();
        }

        var foundation = foundations.FirstOrDefault(card => card.InstanceId == foundationId);
        if (foundation is null) return CommandResult.Reject("选择的晋升基础已不合法");
        var printedCost = S2PromotionGodPowerCost(promoted);
        var actualCost = Math.Max(0, printedCost - player.NextS2PromotionGodPowerDiscount);
        if (!L12S2ZoneOps.Promote(player, foundation, promoted, actualCost))
            return CommandResult.Reject($"需要{actualCost}张活跃的神力完成晋升");

        if (player.NextS2PromotionGodPowerDiscount > 0) player.NextS2PromotionGodPowerDiscount = 0;
        ApplyDisasterLevelOnEntry(playerIndex, promoted, deferTriggerUntilStackSettles: true);
        AddEvent("promotion", playerIndex, $"{player.Name}将〈{promoted.Name}〉叠放至〈{foundation.Name}〉上方晋升登场", promoted, foundation);
        ResolveOnPlayContinuousEffects(playerIndex, promoted);
        RecalculateContinuousTroops();

        var candidates = new List<L12TriggerCandidate>();
        if (HasImmediateEffect(promoted, "promotion-enter"))
            candidates.Add(CreateTriggerCandidate(playerIndex, promoted, "promotion-enter", "【晋升登场】效果"));
        if (HasImmediateEffect(promoted, "enter"))
            candidates.Add(CreateTriggerCandidate(playerIndex, promoted, "enter", "【登场时】效果"));
        if (BuildS2GrailRoundTableEntryCandidate(playerIndex, promoted) is { } grailCandidate)
            candidates.Add(grailCandidate);
        if (candidates.Count > 0)
        {
            State.CheckDisasterAfterStack |= State.DisasterValue > 8;
            QueueTriggerCandidates(candidates);
        }
        else
            TrySettleScheduledDisasterIfIdle();
        return CommandResult.Ok();
    }

    private void ApplyDisasterLevelOnEntry(int playerIndex, L12CardInstance card, bool deferTriggerUntilStackSettles)
    {
        if (!DisastersEnabled || card.CardType != "legion" || card.DisasterLevel <= 0) return;
        if (State.ActiveDisaster?.CardId == "S01-DS10")
        {
            SetDisasterValue(0);
            return;
        }
        AdjustDisasterValue(card.DisasterLevel, playerIndex, "天灾值增加至 {value}");
        if (deferTriggerUntilStackSettles && State.DisasterValue > 8)
            State.CheckDisasterAfterStack = true;
    }

    private static int ParseDeclaredRuneCount(string? choice, int available)
    {
        var token = (choice ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => part.StartsWith("runes:", StringComparison.Ordinal));
        return token is not null && int.TryParse(token.AsSpan("runes:".Length), out var parsed)
            ? Math.Clamp(parsed, 0, available)
            : 0;
    }

    private static string[] ParseRolloGraveOrder(string? choice)
    {
        if (choice?.StartsWith("rollo:", StringComparison.Ordinal) != true) return [];
        return choice["rollo:".Length..].Split('|', 2)[0]
            .Split(',', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string? ParseRolloGraveRepresentation(string? choice)
        => choice?.StartsWith("rollo:", StringComparison.Ordinal) == true
            ? choice["rollo:".Length..].Split('|', 2).Skip(1).FirstOrDefault()
            : null;

    private int GetPlayCost(int playerIndex, L12CardInstance card, bool useSelfDamageDiscount = false, int spentRunes = 0,
        int rolloReturnCount = 0)
        => GetPlayCostWithSigurdDiscount(playerIndex, card, useSelfDamageDiscount, spentRunes, rolloReturnCount,
            useSigurdDiscount: false);

    private int GetPlayCostWithSigurdDiscount(int playerIndex, L12CardInstance card, bool useSelfDamageDiscount, int spentRunes,
        int rolloReturnCount, bool useSigurdDiscount)
    {
        var player = State.Players[playerIndex];
        var counterTactic = card.CardType == "tactic" && IsCounterTactic(card.CardId);
        if (card.CardType == "tactic" && !counterTactic && card.CurrentCost <= 3
            && player.UsedAbilities.Contains($"starter-christina-free-tactic:{State.TurnSerial}")) return 0;
        if (card.CardType == "tactic" && (player.FreeTacticCount > 0
            || (!counterTactic && player.UsedAbilities.Contains("ds01-free-tactic")))) return 0;
        if (counterTactic) return card.CurrentCost;
        var modifier = card.CostModifier;
        if (card.CardType == "tactic" && !IsCounterTactic(card.CardId)) modifier += player.NextActiveTacticSurcharge;
        if (card.CardId is "S01-0104" or "S01-0107" or "S01-0114"
            && State.Players[playerIndex].Morale.Count < State.Players[1 - playerIndex].Morale.Count)
            modifier--;
        if (card.CardId == "S01-0202" && !PublicLegions(player).Any(target => target.CardId == "S01-0212")) modifier -= 2;
        if (card.CardId == "S01-0301") modifier -= CountGraveFactionLegions(player, "asgard") / 4;
        if (card.CardId == "S01-0302") modifier -= PublicLegions(player).Count();
        if (card.CardId is "S01-0305" or "S01-0306" && player.Hp <= 6) modifier--;
        if (card.CardId == "S02-0202") modifier -= player.TombNamedLegionsLeftThisTurn;
        if (card.CardId == "S02-0203" && !PublicLegions(player).Any(target => target.CardId == "S01-0212")) modifier--;
        modifier += L12StructuredCardRules.HandPlayCostModifier(player, card);
        if (card.CardId == "S02-0601" && player.S2ArthurDiscountUntilTurn >= State.TurnSerial) modifier -= 3;
        if (card.CardId == "S01-0403" && player.UsedAbilities.Contains("s2-fortune-next-uesugi")) modifier -= 2;
        if (useSelfDamageDiscount && HasOptionalSelfDamageEntryDiscount(card) && player.Hp > 1) modifier--;
        if (card.CardType == "legion" && card.Faction == player.Faction && player.NextFactionLegionDiscount > 0)
            modifier -= player.NextFactionLegionDiscount;
        if (card.CardType == "legion") modifier -= player.NextLegionEntryDiscount;
        if (card.CardType == "legion" && card.Faction == "taiyangcheng" && card.DisasterLevel > 0)
            modifier -= player.NextS2SunDisasterLegionDiscount;
        if (card.CardType == "legion" && L12StructuredCardRules.HasFaction(player, card, "olympus"))
            modifier -= player.NextS2OlympusLegionDiscount;
        if (card.CardType == "legion" && L12StructuredCardRules.HasFaction(player, card, "otherworld"))
            modifier -= player.NextOtherworldLegionEntryDiscount;
        if (card.CardType == "legion" && State.ActiveDisaster?.CardId == "S02-DS06")
            modifier++;
        if (card.CardId == "S02-0622") modifier -= Math.Max(0, spentRunes) * 2;
        if (card.CardId == "S02-0302") modifier -= Math.Clamp(rolloReturnCount, 0, 8) / 2;
        if (card.CardId == "ST03-01" && useSigurdDiscount) modifier--;
        return Math.Max(0, card.Cost + modifier);
    }

    private CommandResult SetCounterTactic(int playerIndex, L12CardInstance card, L12Command command)
    {
        if (command.Row != 1 || command.Slot is null or < 0 or > 2) return CommandResult.Reject("反击战术必须覆盖在后排阵地");
        var player = State.Players[playerIndex];
        var slot = command.Slot.Value;
        if (player.Field[1][slot] is { CardType: not "tactic" }) return CommandResult.Reject("该后排阵地已有军团");
        var freeFromDisaster = State.ActiveDisaster?.CardId == "S01-DS03";
        var freeFromEffect = !freeFromDisaster && player.FreeTacticCount > 0;
        var cost = freeFromDisaster || freeFromEffect ? 0 : 2;
        if (ActiveResourceCount(player) < cost) return CommandResult.Reject("覆盖反击战术需要消耗 2 张活跃士气");
        var paymentChoice = EnsurePlayResourcePaymentChoice(playerIndex, card, command, cost);
        if (paymentChoice is not null) return paymentChoice;
        var paid = command.CardInstanceIds is not null
            ? TryConsumeSelectedResources(player, cost, command.CardInstanceIds)
            : TryConsumeMorale(player, cost);
        if (!paid) return CommandResult.Reject("选择的支付资源已失效或数量不正确");
        player.Hand.Remove(card);
        if (player.Field[1][slot] is not null)
        {
            var old = player.Field[1][slot]!;
            old.Hidden = false;
            ResetCardAfterLeavingField(old);
            player.Graveyard.Add(old);
            AddEvent("counter-replaced", playerIndex, $"{old.Name} 被新的反击战术顶替并置入墓地", old);
        }
        card.Hidden = true;
        card.OwnerIndex ??= playerIndex;
        card.SetRound = State.Round;
        card.SummonRound = State.Round;
        player.Field[1][slot] = card;
        if (freeFromEffect) player.FreeTacticCount--;
        AddEvent("counter-set", playerIndex, $"{player.Name} 在后排覆盖 1 张反击战术");
        return CommandResult.Ok();
    }

    private CommandResult? EnsurePlayResourcePaymentChoice(int playerIndex, L12CardInstance card, L12Command command, int cost,
        IReadOnlyCollection<string>? excludedResourceIds = null, int temporaryMoraleReserve = 0)
    {
        if (cost <= 0 || command.CardInstanceIds is not null) return null;
        var player = State.Players[playerIndex];
        if (!NeedsManualOrdinaryResourcePayment(player, cost, excludedResourceIds, temporaryMoraleReserve)) return null;
        CreateResourcePaymentPrompt(playerIndex, cost, "play-morale-choice", null, new Dictionary<string, string>
        {
            ["cardInstanceId"] = card.InstanceId,
            ["row"] = command.Row?.ToString() ?? string.Empty,
            ["slot"] = command.Slot?.ToString() ?? string.Empty,
            ["baseChoice"] = command.Choice ?? "normal-cost",
            ["targetPlayerIndex"] = (command.TargetPlayerIndex ?? playerIndex).ToString(),
            ["targetInstanceId"] = command.Target?.InstanceId ?? string.Empty,
        }, excludedResourceIds, temporaryMoraleReserve);
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
        if (!TryValidateAttackTarget(playerIndex, attacker, row, command.Target,
                out var attackTarget, out var isRanged, out var attackError))
            return CommandResult.Reject(attackError);
        var defender = State.Players[1 - playerIndex];

        if (L12StructuredCardRules.RequiresStarterDisasterAttackDiscard(State.ActiveDisaster?.CardId, attacker))
        {
            var selected = command.CardInstanceIds?.ToArray();
            if (selected is null)
            {
                if (State.Players[playerIndex].Hand.Count == 0)
                    return CommandResult.Reject("当前天灾持续期间，手牌为空时不能发动进攻");
                return BeginStarterDisasterAttackDiscard(playerIndex, attacker, command.Target);
            }
            if (selected.Length != 1 || State.Players[playerIndex].Hand.All(card => card.InstanceId != selected[0]))
                return CommandResult.Reject("选择的进攻弃牌费用已失效");
            var discarded = State.Players[playerIndex].Hand.First(card => card.InstanceId == selected[0]);
            MoveHandToGrave(State.Players[playerIndex], discarded.InstanceId, causedByEffect: false);
            AddEvent("cost", playerIndex, "色欲之罪使进攻方弃置1张手牌", attacker, discarded);
        }

        if (State.ActiveDisaster?.CardId == "S01-DS04" && attacker.Troops > 2000)
        {
            var thunderRoll = _random.Next(1, 7);
            AddEvent("dice", playerIndex, $"〈雷霆天怒〉：{attacker.Name}进攻时掷骰结果为 {thunderRoll}", attacker);
            if (thunderRoll <= 2)
            {
                attacker.Tapped = true;
                attacker.AttacksThisTurn++;
                AddEvent("attack-ended", playerIndex,
                    $"〈雷霆天怒〉使{attacker.Name}转为休整，进攻结束", attacker);
                return CommandResult.Ok();
            }
        }

        attacker.Tapped = true;
        var combatProfile = L12StructuredCardRules.CombatProfile(attacker, row);
        var attackNoLoss = combatProfile.HasAttackNoLoss
            || attacker.AttackNoLossUntilTurn >= State.TurnSerial
            || attacker.NextAttackNoLossUses > 0;
        if (attacker.NextAttackNoLossUses > 0) attacker.NextAttackNoLossUses--;
        var temporaryAttackerTroopsBonus = 0;
        if (combatProfile.AttackTroopsSetValue is { } setAttackTroops)
        {
            temporaryAttackerTroopsBonus = setAttackTroops - attacker.Troops;
            attacker.Troops = setAttackTroops;
            AddEvent("effect", playerIndex,
                $"{attacker.Name}位于后排，本次进攻兵力视为{setAttackTroops}", attacker);
        }
        temporaryAttackerTroopsBonus += ApplyS1FactionAttackPassives(playerIndex, attacker, row);
        attacker.AttacksThisTurn++;
        if (row == 0 && L12StructuredCardRules.HasFaction(State.Players[playerIndex], attacker, "gaotianyuan")
            && State.Players[playerIndex].UsedAbilities.Contains($"s2-tenka-front-attack:{State.TurnSerial}"))
        {
            temporaryAttackerTroopsBonus += 1000;
            attacker.Troops += 1000;
            AddEvent("effect", playerIndex, $"天下布武使{attacker.Name}本次前排进攻兵力+1000", attacker);
        }
        if (row == 0 && State.Players[playerIndex].UsedAbilities.Contains($"susano-buff:{attacker.InstanceId}"))
        {
            temporaryAttackerTroopsBonus += 2000;
            attacker.Troops += 2000;
            AddEvent("effect", playerIndex, $"须佐之男使 {attacker.Name} 本次前排进攻兵力 +2000", attacker);
        }
        if (attacker.TsukuyomiFrontMoveBonusTurn == State.TurnSerial
            && attacker.TsukuyomiFrontMoveBonusCount > 0)
        {
            var tsukuyomiBonus = attacker.TsukuyomiFrontMoveBonusCount * 1000;
            temporaryAttackerTroopsBonus += tsukuyomiBonus;
            attacker.Troops += tsukuyomiBonus;
            AddEvent("effect", playerIndex, $"月读使{attacker.Name}本次进攻兵力+{tsukuyomiBonus}", attacker);
        }
        if (attacker.AttackOnlyTroopsBonusUntilTurn == State.TurnSerial && attacker.AttackOnlyTroopsBonus != 0)
        {
            temporaryAttackerTroopsBonus += attacker.AttackOnlyTroopsBonus;
            attacker.Troops += attacker.AttackOnlyTroopsBonus;
            AddEvent("effect", playerIndex, $"〈{attacker.Name}〉本次进攻兵力+{attacker.AttackOnlyTroopsBonus}", attacker);
        }
        var damage = 1 + (L12StructuredCardSemantics.HasEffectiveStrongAttack(attacker) ? 1 : 0);
        if (attacker.CardId == "S02-0607" && attacker.GawainMasterDamageBonusUntilTurn == State.TurnSerial)
            damage += attacker.GawainMasterDamageBonus;
        if (attacker.MasterAttackDamageBonusUntilTurn == State.TurnSerial)
            damage += attacker.MasterAttackDamageBonus;
        if (State.ActiveDisaster?.CardId == "S01-DS02" && attacker.DisasterLevel > 0) damage++;
        var kagutsuchiCandidate = BuildStarterKagutsuchiCandidate(playerIndex, attacker);
        var hasPrintedAttackerAttackTiming = HasImmediateEffect(attacker, "attack");
        var hasAttackerAttackTiming = hasPrintedAttackerAttackTiming || kagutsuchiCandidate is not null;
        State.PendingDefense = new L12PendingDefense
        {
            AttackerPlayer = playerIndex,
            AttackerInstanceId = attacker.InstanceId,
            Target = command.Target,
            Stage = hasAttackerAttackTiming
                ? L12CombatStage.AttackerAttackTiming
                : L12CombatStage.DefenderAttackTiming,
            IsRanged = isRanged,
            RangedNoLoss = combatProfile.HasRangedNoLoss,
            AttackNoLoss = attackNoLoss,
            SureHit = attacker.HasSureHit
                || (attackTarget is not null && attacker.SureHitAgainstLegionsUntilTurn >= State.TurnSerial),
            MasterDamage = damage,
            TemporaryAttackerTroopsBonus = temporaryAttackerTroopsBonus,
        };
        State.Phase = L12Phase.Defense;
        if (attackTarget is null)
            AddEvent("attack", playerIndex,
                $"{State.Players[playerIndex].Name}【{attacker.Name}】{attacker.Troops} vs {defender.Name}【{defender.MasterName}】血量{defender.Hp}", attacker);
        else
            AddEvent("attack", playerIndex,
                $"{State.Players[playerIndex].Name}【{attacker.Name}】{attacker.Troops} vs {defender.Name}【{attackTarget.Name}】{attackTarget.Troops}", attacker, attackTarget);
        if (hasAttackerAttackTiming)
        {
            if (kagutsuchiCandidate is not null)
            {
                var candidates = new List<L12TriggerCandidate>();
                if (hasPrintedAttackerAttackTiming)
                    candidates.Add(CreateTriggerCandidate(playerIndex, attacker, "attack",
                        "进攻方【进攻时】效果"));
                candidates.Add(kagutsuchiCandidate);
                QueueTriggerCandidates(candidates);
            }
            else if (L12CompositeEffectPlans.RequiresTriggerDeclaration(attacker.CardId, "attack"))
                QueueTriggerCandidates([
                    CreateTriggerCandidate(playerIndex, attacker, "attack", "进攻方【进攻时】效果")
                ]);
            else
                QueueOrPushTriggeredEffect(playerIndex, attacker, "attack", "进攻方【进攻时】效果");
        }
        else
            AdvanceCombatTimelineIfIdle();
        return CommandResult.Ok();
    }

    private bool TryValidateAttackTarget(int playerIndex, L12CardInstance attacker, int row,
        L12AttackTarget target, out L12CardInstance? attackTarget, out bool isRanged, out string error)
    {
        attackTarget = null;
        isRanged = false;
        error = string.Empty;
        if (attacker.SummonRound >= State.Round && !attacker.HasCharge
            && !(target.Type == "master" && attacker.CanAttackMasterOnSummonUntilTurn == State.TurnSerial)
            && !(target.Type == "legion" && attacker.CanAttackLegionsOnSummonUntilTurn == State.TurnSerial))
        {
            error = "刚登场的军团不能进攻";
            return false;
        }

        var defender = State.Players[1 - playerIndex];
        if (target.Type == "master")
            return CanAttackMasterTarget(playerIndex, attacker, row, defender, out error);
        if (target.Type != "legion")
        {
            error = "无效进攻目标";
            return false;
        }

        var card = FindOnField(defender, target.InstanceId, out var targetRow, out _);
        if (card is null || card.Hidden || !IsFieldLegion(card)) error = "目标不是可进攻军团";
        else if (card.CardId == "S02-0516" && !card.Tapped) error = "活跃的汉尼拔无法被进攻";
        else if (State.ActiveDisaster?.CardId == "S02-DS02" && targetRow == 0 && !card.Tapped)
            error = "〈迷雾绝境〉生效时不可进攻处于活跃状态的前排军团";
        else if (IsProtectedByRestedAmakine(defender, card)) error = "休整的阿麦金使活跃的试炼军团不可被进攻";
        else if (row == 1 && targetRow != 0 && attacker.CanAttackBackAndMasterUntilTurn != State.TurnSerial)
            error = "后排远程军团只能进攻对方前排";
        else if (row == 0 && targetRow == 1 && !HasRangeInPosition(attacker, row))
            error = "近战军团无法进攻对方后排";
        else
        {
            isRanged = row == 1 || targetRow == 1;
            if (isRanged && State.ActiveDisaster?.CardId == "S02-DS04")
                error = "〈风暴乱象〉生效时军团无法发动远程进攻";
            else if (isRanged && L12StructuredCardRules.CombatProfile(card, targetRow).CannotBeRanged)
                error = "目标无法被远程进攻";
            else
            {
                var taunts = State.ActiveDisaster?.CardId == "S02-DS02"
                    || defender.UsedAbilities.Contains($"starter-taunt-disabled:{State.TurnSerial}") ? []
                    : defender.Field[0].Where(candidate => candidate is not null
                        && HasS1Taunt(candidate, 0) && !candidate.Hidden).ToArray();
                if (taunts.Length > 0 && !HasS1Taunt(card, targetRow)) error = "对方前排存在带有挑衅的军团";
            }
        }
        if (!string.IsNullOrEmpty(error)) return false;
        attackTarget = card;
        return true;
    }

    private static bool HasRangeInPosition(L12CardInstance card, int row)
        => L12StructuredCardRules.CombatProfile(card, row).HasRangeBonus;

    private static bool HasFrontRowLowTroopMasterProtection(L12PlayerState defender, int attackerTroops)
        => defender.Field[0].Any(card => card is not null && !card.Hidden && IsFieldLegion(card)
            && L12StructuredCardRules.ProtectsMasterFromTroops(card, 0, attackerTroops));

    private bool HasMandatoryDisasterLegionTarget(L12CardInstance attacker, int row, L12PlayerState defender)
    {
        for (var targetRow = 0; targetRow < 2; targetRow++)
        for (var slot = 0; slot < 3; slot++)
        {
            var target = defender.Field[targetRow][slot];
            if (target is null || target.Hidden || !IsFieldLegion(target)) continue;
            if (State.ActiveDisaster?.CardId == "S02-DS02" && targetRow == 0 && !target.Tapped) continue;
            if (row == 1 && targetRow != 0 && attacker.CanAttackBackAndMasterUntilTurn != State.TurnSerial) continue;
            if (row == 0 && targetRow == 1 && !HasRangeInPosition(attacker, row)) continue;
            var ranged = row == 1 || targetRow == 1;
            if (ranged && State.ActiveDisaster?.CardId == "S02-DS04") continue;
            if (ranged && L12StructuredCardRules.CombatProfile(target, targetRow).CannotBeRanged) continue;
            return true;
        }
        return false;
    }

    private static bool CanAttackFromRow(L12CardInstance card, int row)
        => row == 0 || (row == 1 && HasRangeInPosition(card, row));

    private bool CanAttackMasterTarget(int playerIndex, L12CardInstance attacker, int row,
        L12PlayerState defender, out string error)
    {
        error = string.Empty;
        if (defender.MasterCannotBeAttackedUntilTurn >= State.TurnSerial)
            error = "对方主宰当前不能被进攻";
        else if (attacker.CardId == "S01-0212" && State.Players[playerIndex].MasterId == "S02-02M1")
            error = "奈芙蒂斯使我方陵墓守卫无法进攻主宰";
        else if (HasFrontRowLowTroopMasterProtection(defender, attacker.Troops))
            error = "对方前排军团使主宰无法被兵力不高于2000的军团进攻";
        else if (State.ActiveDisaster?.CardId == "S02-DS02" && attacker.Troops <= 2000)
            error = "〈迷雾绝境〉生效时兵力不高于2000的军团无法进攻主宰";
        else if (State.ActiveDisaster?.CardId == "S02-DS05" && HasMandatoryDisasterLegionTarget(attacker, row, defender))
            error = "〈暴怒之罪〉生效时必须优先进攻范围内的对方军团";
        else
        {
            var disasterAllowsBackMaster = State.Players[playerIndex].UsedAbilities.Contains("ds01-back-master")
                && HasRangeInPosition(attacker, row);
            if (row != 0 && !disasterAllowsBackMaster
                && attacker.CanAttackMasterOnSummonUntilTurn != State.TurnSerial
                && attacker.CanAttackBackAndMasterUntilTurn != State.TurnSerial)
                error = "后排远程军团不能进攻主宰";
            else
            {
                var taunts = State.ActiveDisaster?.CardId == "S02-DS02"
                    || defender.UsedAbilities.Contains($"starter-taunt-disabled:{State.TurnSerial}") ? []
                    : defender.Field[0].Where(card => card is not null && HasS1Taunt(card, 0) && !card.Hidden).ToArray();
                if (taunts.Length > 0) error = "对方前排存在带有挑衅的军团";
            }
        }
        return error.Length == 0;
    }

    private CommandResult ResolveDefense(int playerIndex, L12Command command)
    {
        var pending = State.PendingDefense;
        if (State.Phase != L12Phase.Defense || pending is null || pending.Stage != L12CombatStage.DefenseChoice)
            return CommandResult.Reject("当前没有待选择的抵挡或支援");
        if (State.EffectStack.Count > 0 || State.PendingPrompts.Count > 0) return CommandResult.Reject("进攻响应仍在结算");
        if (playerIndex == pending.AttackerPlayer) return CommandResult.Reject("进攻方不能代替防守方结算");
        if (TryAbortCombatAtSafeBoundary(pending, playerIndex)) return CommandResult.Ok();
        var attackerPlayer = State.Players[pending.AttackerPlayer];
        var defender = State.Players[playerIndex];
        var attacker = FindOnField(attackerPlayer, pending.AttackerInstanceId, out _, out _);
        if (attacker is null) return CommandResult.Ok();

        var blockIds = pending.Target.Type == "master" ? command.CardInstanceIds ?? [] : [];
        var supportIds = pending.Target.Type == "legion"
            ? (command.CardInstanceIds?.Count > 0 ? command.CardInstanceIds
                : string.IsNullOrWhiteSpace(command.SupportInstanceId) ? [] : [command.SupportInstanceId])
            : [];
        var validation = ValidateDefenseChoice(playerIndex, pending, attacker, blockIds, supportIds);
        if (!validation.Accepted) return validation;

        var declaredDefense = pending.Target.Type == "master" ? blockIds.Count > 0 : supportIds.Count > 0;
        if (declaredDefense)
        {
            QueueAuthorityEvent("defense", playerIndex, attacker,
                pending.Target.Type == "master" ? $"{defender.Name}声明抵挡" : $"{defender.Name}声明支援",
                subjectPlayer: playerIndex, targetInstanceId: pending.Target.InstanceId, causedByEffect: false,
                data: new Dictionary<string, string>
                {
                    ["action"] = pending.Target.Type == "master" ? "block" : "support",
                    ["blockIds"] = string.Join('|', blockIds),
                    ["supportIds"] = string.Join('|', supportIds),
                });
            return CommandResult.Ok();
        }

        return ResolveDefenseCore(playerIndex, blockIds, supportIds, forceInvalid: false);
    }

    private CommandResult ValidateDefenseChoice(int playerIndex, L12PendingDefense pending, L12CardInstance attacker,
        IReadOnlyList<string> blockIds, IReadOnlyList<string> supportIds)
    {
        var defender = State.Players[playerIndex];
        if (pending.Target.Type == "master")
        {
            if (pending.SureHit && blockIds.Count > 0) return CommandResult.Reject("必中进攻无法被抵挡");
            if (blockIds.Count != blockIds.Distinct().Count()) return CommandResult.Reject("抵挡卡牌重复");
            var cards = defender.Hand.Where(card => blockIds.Contains(card.InstanceId) && card.CardType == "legion").ToList();
            if (cards.Count != blockIds.Count) return CommandResult.Reject("只能弃置手牌中的军团");
            if (cards.Count > 0 && cards.Sum(card => card.Troops) < EffectiveAttackValue(pending, attacker))
                return CommandResult.Reject("弃置军团总兵力不足");
            return CommandResult.Ok();
        }

        if (supportIds.Count == 0) return CommandResult.Ok();
        if (pending.SureHit) return CommandResult.Reject("必中进攻无法被支援");
        if (supportIds.Count != supportIds.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return CommandResult.Reject("支援军团不能重复选择");
        var target = FindOnField(defender, pending.Target.InstanceId, out var targetRow, out var targetSlot);
        if (target is null) return CommandResult.Reject("进攻目标已经离场");
        if (targetRow != 0 || defender.BackRowCannotSupport
            || L12StructuredCardRules.CannotReceiveBackRowSupport(target, targetRow))
            return CommandResult.Reject("该军团不能获得后排支援");
        var supporters = new List<L12CardInstance>();
        foreach (var supportId in supportIds)
        {
            var support = FindOnField(defender, supportId, out var supportRow, out var supportSlot);
            if (support is null || !IsFieldLegion(support) || supportRow != 1)
                return CommandResult.Reject("只能选择我方后排军团进行支援");
            if (supportSlot != targetSlot && !L12StructuredCardRules.HasCooperativeSupport(support, supportRow))
                return CommandResult.Reject("非同列后排军团必须具有协防");
            if (support.CannotSupport) return CommandResult.Reject($"〈{support.Name}〉当前无法支援");
            supporters.Add(support);
        }
        if (target.Troops + supporters.Sum(card => card.Troops) < EffectiveAttackValue(pending, attacker))
            return CommandResult.Reject("支援后合计兵力仍不足");
        return CommandResult.Ok();
    }

    private bool HasLegalLegionSupport(L12PendingDefense pending)
    {
        if (pending.Target.Type != "legion" || pending.SureHit) return false;
        var defender = State.Players[1 - pending.AttackerPlayer];
        var attacker = FindOnField(State.Players[pending.AttackerPlayer], pending.AttackerInstanceId, out _, out _);
        var target = FindOnField(defender, pending.Target.InstanceId, out var targetRow, out var targetSlot);
        if (attacker is null || target is null || targetRow != 0 || defender.BackRowCannotSupport
            || L12StructuredCardRules.CannotReceiveBackRowSupport(target, targetRow)) return false;
        var supporters = defender.Field[1]
            .Where(card => card is not null && IsFieldLegion(card) && !card.CannotSupport)
            .Cast<L12CardInstance>()
            .Where(card => FindOnField(defender, card.InstanceId, out var row, out var slot) is not null
                && (slot == targetSlot || L12StructuredCardRules.HasCooperativeSupport(card, row)))
            .ToArray();
        return supporters.Length > 0
            && target.Troops + supporters.Sum(card => card.Troops) >= EffectiveAttackValue(pending, attacker);
    }

    private bool AutoResolveLegionDefenseWithoutSupport()
    {
        var pending = State.PendingDefense;
        if (pending is null || pending.Stage != L12CombatStage.DefenseChoice
            || pending.Target.Type != "legion" || HasLegalLegionSupport(pending)) return false;
        AddEvent("support-skipped", 1 - pending.AttackerPlayer, "没有可进行支援的军团，跳过支援选择");
        ResolveDefenseCore(1 - pending.AttackerPlayer, [], [], forceInvalid: false);
        return true;
    }

    private CommandResult ResolveDefenseCore(int playerIndex, IReadOnlyList<string> declaredBlockIds,
        IReadOnlyList<string> declaredSupportIds, bool forceInvalid)
    {
        var pending = State.PendingDefense;
        if (State.Phase != L12Phase.Defense || pending is null
            || pending.Stage is not (L12CombatStage.DefenseChoice or L12CombatStage.CombatDamage))
            return CommandResult.Reject("当前没有待结算进攻");
        if (TryAbortCombatAtSafeBoundary(pending, playerIndex)) return CommandResult.Ok();
        var attackerPlayer = State.Players[pending.AttackerPlayer];
        var defender = State.Players[playerIndex];
        var attacker = FindOnField(attackerPlayer, pending.AttackerInstanceId, out _, out _);
        if (attacker is null) return CommandResult.Ok();

        pending.DeclaredBlockIds.Clear();
        pending.DeclaredBlockIds.AddRange(declaredBlockIds);
        pending.DeclaredSupportIds.Clear();
        pending.DeclaredSupportIds.AddRange(declaredSupportIds);
        pending.DeclaredSupportId = declaredSupportIds.FirstOrDefault();
        if (!forceInvalid)
        {
            var revalidation = ValidateDefenseChoice(playerIndex, pending, attacker, declaredBlockIds, declaredSupportIds);
            if (!revalidation.Accepted)
            {
                forceInvalid = true;
                AddEvent("defense-invalid", playerIndex, $"防御结算前重新校验失败：{revalidation.Error}；本次抵挡/支援无效");
            }
        }
        pending.ForceInvalidDefense = forceInvalid;
        pending.Stage = L12CombatStage.CombatDamage;
        if (pending.Target.Type == "master")
        {
            var ids = forceInvalid ? [] : declaredBlockIds;
            var cards = defender.Hand.Where(card => ids.Contains(card.InstanceId) && card.CardType == "legion").ToList();
            pending.Stage = L12CombatStage.AttackerAfterAttack;
            if (cards.Count == 0)
            {
                DamageMaster(playerIndex, pending.MasterDamage, $"{attacker.Name}的进攻", pending.AttackerPlayer,
                    combatDamage: true);
                if (L12VerifiedAtomicPrograms.Find(attacker.CardId, "after-damage") is not null
                    && State.Phase != L12Phase.GameOver)
                    QueueTriggerCandidates([
                        CreateTriggerCandidate(pending.AttackerPlayer, attacker, "after-damage",
                            "【对主宰造成伤害时】效果")
                    ]);
            }
            foreach (var card in cards)
            {
                defender.Hand.Remove(card);
                defender.Graveyard.Add(card);
            }
            AddEvent("defense", playerIndex, cards.Count == 0
                ? $"{defender.Name} 的主宰受到 {pending.MasterDamage} 点伤害"
                : $"{defender.Name} 弃置 {cards.Count} 张军团抵挡", cards.ToArray());
            AdvanceCombatTimelineIfIdle();
            return CommandResult.Ok();
        }

        var target = FindOnField(defender, pending.Target.InstanceId, out var targetRow, out _);
        if (target is null)
        {
            TryAbortCombatAtSafeBoundary(pending, playerIndex);
            return CommandResult.Ok();
        }

        var supportIds = forceInvalid ? [] : declaredSupportIds;
        if (supportIds.Count > 0)
        {
            var supporters = supportIds.Select(id => FindOnField(defender, id, out _, out _))
                .Where(card => card is not null).Cast<L12CardInstance>().ToArray();
            if (supporters.Length > 0)
            {
                pending.Stage = L12CombatStage.AttackerAfterAttack;
                foreach (var support in supporters) RemoveFromField(defender, support, true, "作为支援军团阵亡");
                AddEvent("support", playerIndex, $"{string.Join('、', supporters.Select(card => card.Name))}联合支援{target.Name}，支援者阵亡；交战双方不损兵且不产生击杀",
                    supporters.Append(target).Append(attacker).ToArray());
                AdvanceCombatTimelineIfIdle();
                return CommandResult.Ok();
            }
        }

        var targetTroops = target.Troops;
        var attackValue = EffectiveAttackValue(pending, attacker);
        var targetProfile = L12StructuredCardRules.CombatProfile(target, targetRow);
        var rangedDamageAdjustment = pending.IsRanged
            ? targetProfile.IncomingRangedCombatDamageAdjustment
            : 0;
        var defenderDamage = Math.Max(0, attackValue + rangedDamageAdjustment);
        if (defenderDamage < attackValue)
            AddEvent("effect", defender.PlayerIndex,
                $"{target.Name}受到远程进攻，使最终战斗伤害由 {attackValue} 降为 {defenderDamage}", target, attacker);
        else if (defenderDamage > attackValue)
            AddEvent("effect", defender.PlayerIndex,
                $"{target.Name}受到远程进攻，使最终战斗伤害由 {attackValue} 增为 {defenderDamage}", target, attacker);
        var attackerTakesDamage = !pending.AttackNoLoss && !(pending.IsRanged && pending.RangedNoLoss);
        if (targetTroops - defenderDamage <= 0
            && TryOfferCombatLethalReplacement(defender, target, pending)) return CommandResult.Ok();
        if (attackerTakesDamage && attacker.Troops - targetTroops <= 0
            && TryOfferCombatLethalReplacement(attackerPlayer, attacker, pending)) return CommandResult.Ok();

        var targetReplaced = pending.LethalReplacementDecisions.GetValueOrDefault(target.InstanceId);
        var attackerReplaced = pending.LethalReplacementDecisions.GetValueOrDefault(attacker.InstanceId);
        string? substituteDefenderDeath = null;
        string? substituteAttackerDeath = null;
        if (targetReplaced && pending.LethalReplacementSubstitutes.GetValueOrDefault(target.InstanceId) is { } targetSubstitute)
        {
            var kind = CardLethalSubstitutionKind(defender, target);
            if (!TryApplyCardLethalSubstitution(defender, target, kind, targetSubstitute,
                    "本次致命进攻", deferFieldDeath: true, out substituteDefenderDeath))
            {
                targetReplaced = false;
                pending.LethalReplacementDecisions[target.InstanceId] = false;
            }
        }
        if (attackerReplaced && pending.LethalReplacementSubstitutes.GetValueOrDefault(attacker.InstanceId) is { } attackerSubstitute)
        {
            var kind = CardLethalSubstitutionKind(attackerPlayer, attacker);
            if (!TryApplyCardLethalSubstitution(attackerPlayer, attacker, kind, attackerSubstitute,
                    "本次致命进攻", deferFieldDeath: true, out substituteAttackerDeath))
            {
                attackerReplaced = false;
                pending.LethalReplacementDecisions[attacker.InstanceId] = false;
            }
        }
        if (!targetReplaced) L12DerivedStats.ApplyTroopsDamage(target, defenderDamage);
        if (attackerTakesDamage && !attackerReplaced)
            pending.TemporaryAttackerTroopsBonus = L12DerivedStats.ApplyTroopsDamage(attacker, targetTroops,
                pending.TemporaryAttackerTroopsBonus);

        var defenderDefeated = target.Troops <= 0 && RemoveFromField(defender, target, true, "阵亡（等待触发完成后进入墓地）",
            queueDeathTrigger: false, bypassLethalReplacement: true, deferGraveyard: true);
        var attackerDefeated = attacker.Troops <= 0 && RemoveFromField(attackerPlayer, attacker, true,
            "阵亡（等待触发完成后进入墓地）", queueDeathTrigger: false,
            bypassLethalReplacement: true, deferGraveyard: true);
        if (defenderDefeated) pending.DefeatedDefenderInstanceId = target.InstanceId;
        else if (substituteDefenderDeath is not null) pending.DefeatedDefenderInstanceId = substituteDefenderDeath;
        if (attackerDefeated) pending.DefeatedAttackerInstanceId = attacker.InstanceId;
        else if (substituteAttackerDeath is not null) pending.DefeatedAttackerInstanceId = substituteAttackerDeath;
        defenderDefeated |= substituteDefenderDeath is not null;
        attackerDefeated |= substituteAttackerDeath is not null;
        pending.Stage = defenderDefeated
            ? L12CombatStage.KillTriggers
            : attackerDefeated
                ? L12CombatStage.DefenderKillTriggers
                : L12CombatStage.AttackerAfterAttack;
        AddEvent("combat", playerIndex, pending.AttackNoLoss || pending.IsRanged && pending.RangedNoLoss
            ? $"进攻无损：防守军团承受 {defenderDamage} 点战斗伤害，进攻军团不减损"
            : $"进攻者以冻结进攻值 {attackValue} 造成 {defenderDamage} 点战斗伤害；防守军团以当前兵力 {targetTroops} 反击",
            attacker, target);
        AdvanceCombatTimelineIfIdle();
        return CommandResult.Ok();
    }

    private bool TryOfferCombatLethalReplacement(L12PlayerState controller, L12CardInstance card, L12PendingDefense pending)
    {
        if (!pending.LethalReplacementDecisions.ContainsKey(card.InstanceId)
            && TryApplyLakeLadySwordReplacement(controller, card, "致命进攻"))
        {
            pending.LethalReplacementDecisions[card.InstanceId] = true;
            return false;
        }
        if (TryOfferCardLethalSubstitution(controller, card, "combat-lethal-replacement", "本次致命进攻"))
            return true;
        if (!CanUseAchillesLethalReplacement(controller, card)) return false;
        if (pending.LethalReplacementDecisions.ContainsKey(card.InstanceId)) return false;
        CreatePrompt(controller.PlayerIndex, "optional",
            $"〈{card.Name}〉即将阵亡，是否消耗并翻转1神力，代替承受本次致命进攻？",
            ["yes", "no"], 1, 1, "combat-lethal-replacement", isPrivate: false,
            data: new Dictionary<string, string>
            {
                ["cardInstanceId"] = card.InstanceId,
                ["preservedTroops"] = card.Troops.ToString(),
                ["preservedTapped"] = card.Tapped ? "true" : "false",
                ["yes"] = "消耗并翻转1神力，保持当前兵力与活跃/休整状态",
                ["no"] = "不发动",
            });
        return true;
    }

    private void ResolveCombatLethalReplacement(int playerIndex, L12Prompt prompt, string choice)
    {
        var pending = State.PendingDefense;
        if (pending is null) return;
        var player = State.Players[playerIndex];
        var cardId = prompt.Data.GetValueOrDefault("cardInstanceId");
        var card = FindOnField(player, cardId, out _, out _);
        if (card is null) return;
        if (prompt.Data.ContainsKey("replacementKind"))
        {
            var appliedSubstitution = ResolveCombatCardLethalSubstitution(player, card, pending, prompt, choice);
            pending.LethalReplacementDecisions[card.InstanceId] = appliedSubstitution;
            ResolveDefenseCore(1 - pending.AttackerPlayer, pending.DeclaredBlockIds,
                pending.DeclaredSupportIds, pending.ForceInvalidDefense);
            return;
        }
        var applied = choice == "yes" && CanUseAchillesLethalReplacement(player, card)
            && L12S2ZoneOps.ConsumeAndFlipGodPower(player, 1);
        pending.LethalReplacementDecisions[card.InstanceId] = applied;
        if (applied)
        {
            player.UsedAbilities.Add(AchillesReplacementKey(card));
            card.Troops = int.Parse(prompt.Data["preservedTroops"]);
            card.Tapped = prompt.Data["preservedTapped"] == "true";
            AddEvent("replacement", playerIndex,
                $"{card.Name}消耗并翻转1神力，代替承受致命进攻并保持当时状态", card);
        }
        ResolveDefenseCore(1 - pending.AttackerPlayer, pending.DeclaredBlockIds,
            pending.DeclaredSupportIds, pending.ForceInvalidDefense);
    }

    private void BeginPiercingAttack(int playerIndex, L12CardInstance attacker)
    {
        var player = State.Players[playerIndex];
        if (State.Phase == L12Phase.GameOver
            || FindOnField(player, attacker.InstanceId, out var row, out _) is null) return;
        var opponent = State.Players[1 - playerIndex];
        if (!CanAttackMasterTarget(playerIndex, attacker, row, opponent, out var error))
        {
            AddEvent("effect-failed", playerIndex, $"{attacker.Name}的贯穿进攻失败：{error}", attacker);
            return;
        }
        if (State.PendingDefense is { } parentCombat)
            State.SuspendedCombatContexts.Add(parentCombat);
        State.PendingDefense = new L12PendingDefense
        {
            AttackerPlayer = playerIndex,
            AttackerInstanceId = attacker.InstanceId,
            Target = new L12AttackTarget("master"),
            Stage = L12CombatStage.DefenderAttackTiming,
            SureHit = attacker.HasSureHit,
            MasterDamage = 1,
            SuppressAttackTriggers = true,
        };
        State.Phase = L12Phase.Defense;
        AddEvent("piercing", playerIndex,
            $"贯穿：{attacker.Name}以剩余兵力{attacker.Troops}对{opponent.Name}的主宰发动1次进攻；此次进攻不触发【进攻时】效果",
            attacker);
        AdvanceCombatTimelineIfIdle();
    }

    private CommandResult Move(int playerIndex, L12Command command)
    {
        if (!CanAct(playerIndex)) return CommandResult.Reject("只能在自己的主要阶段位移");
        if (command.Row is null or < 0 or > 1 || command.Slot is null or < 0 or > 2) return CommandResult.Reject("目标阵地无效");
        var player = State.Players[playerIndex];
        var card = FindOnField(player, command.CardInstanceId, out var sourceRow, out var sourceSlot);
        if (card is null || !IsFieldLegion(card) || card.Tapped || card.Hidden) return CommandResult.Reject("只能移动活跃且未覆盖的军团");
        var targetRow = command.Row.GetValueOrDefault();
        var targetSlot = command.Slot.GetValueOrDefault();
        if (State.ActiveDisaster?.CardId == "S01-DS03" && targetRow == 1)
            return CommandResult.Reject("〈腐秽大地〉持续期间无法位移至后排");
        if (Math.Abs(sourceRow - targetRow) + Math.Abs(sourceSlot - targetSlot) != 1)
            return CommandResult.Reject("规则位移每次只能移动至相邻空格");
        if (player.Field[targetRow][targetSlot] is not null) return CommandResult.Reject("目标阵地已占用");
        var tenkaFreeMoveKey = $"s2-tenka-free-move:{card.InstanceId}:{State.TurnSerial}";
        var hasTenkaFreeMove = player.UsedAbilities.Contains(tenkaFreeMoveKey);
        var hasHippolytaFreeFrontBackMove = sourceRow != targetRow
            && PublicLegions(player).Any(candidate => candidate.CardId == "S02-0510" && candidate.Tapped);
        if (!hasTenkaFreeMove && !hasHippolytaFreeFrontBackMove && command.CardInstanceIds is null && NeedsManualOrdinaryResourcePayment(player, 1))
        {
            CreateResourcePaymentPrompt(playerIndex, 1, "move-morale-choice", null, new Dictionary<string, string>
            {
                ["cardInstanceId"] = card.InstanceId,
                ["row"] = targetRow.ToString(),
                ["slot"] = targetSlot.ToString(),
            });
            return CommandResult.Ok();
        }
        if (!hasTenkaFreeMove && !hasHippolytaFreeFrontBackMove && !(command.CardInstanceIds is not null
            ? TryConsumeSelectedResources(player, 1, command.CardInstanceIds)
            : TryConsumeMorale(player, 1)))
            return CommandResult.Reject("移动需要消耗 1 张活跃士气");
        player.Field[sourceRow][sourceSlot] = null;
        player.Field[targetRow][targetSlot] = card;
        card.LastMovedTurn = State.TurnSerial;
        if (hasTenkaFreeMove) player.UsedAbilities.Remove(tenkaFreeMoveKey);
        AddEvent("move", playerIndex, $"{card.Name} 移动至相邻阵地", card);
        RecordLegionMovement(playerIndex, card, sourceRow, targetRow);
        return CommandResult.Ok();
    }

    private CommandResult CavalryMove(int playerIndex, L12Command command)
    {
        if (!CanAct(playerIndex)) return CommandResult.Reject("只能在自己的主要阶段发动骑兵位移");
        if (command.Row is null or < 0 or > 1 || command.Slot is null or < 0 or > 2) return CommandResult.Reject("目标阵地无效");
        var player = State.Players[playerIndex];
        var card = FindOnField(player, command.CardInstanceId, out var sourceRow, out var sourceSlot);
        if (card is null || !IsFieldLegion(card) || card.Tapped || card.Hidden
            || !L12StructuredCardRules.HasProfession(card, sourceRow, "骑兵"))
            return CommandResult.Reject("只能令活跃且未覆盖的【骑兵】进行骑兵位移");
        if (card.LastCavalryMoveTurn == State.TurnSerial) return CommandResult.Reject("该军团本回合已经进行过骑兵位移");
        var targetRow = command.Row.Value;
        var targetSlot = command.Slot.Value;
        if (State.ActiveDisaster?.CardId == "S01-DS03" && targetRow == 1)
            return CommandResult.Reject("〈腐秽大地〉持续期间无法位移至后排");
        if (player.Field[targetRow][targetSlot] is not null) return CommandResult.Reject("目标阵地已占用");
        player.Field[sourceRow][sourceSlot] = null;
        player.Field[targetRow][targetSlot] = card;
        card.LastMovedTurn = State.TurnSerial;
        card.LastCavalryMoveTurn = State.TurnSerial;
        AddEvent("move", playerIndex, $"{card.Name} 发动骑兵位移", card);
        RecordLegionMovement(playerIndex, card, sourceRow, targetRow);
        return CommandResult.Ok();
    }

    private void RevertPendingCombatTroopsModifiers(L12PendingDefense pending, L12CardInstance? attacker)
    {
        if (attacker is not null && pending.TemporaryAttackerTroopsBonus != 0)
            attacker.Troops -= pending.TemporaryAttackerTroopsBonus;
        pending.TemporaryAttackerTroopsBonus = 0;
    }

    private CommandResult FlipHidden(int playerIndex, string? instanceId)
    {
        return CommandResult.Reject("隐匿后的覆盖卡不能作为军团主动翻回");
    }

    private CommandResult ActivateAbility(int playerIndex, L12Command command)
    {
        if (!CanAct(playerIndex)) return CommandResult.Reject("只能在自己的主要阶段发动主动效果");
        return BeginActiveAbility(playerIndex, command);
    }
}
