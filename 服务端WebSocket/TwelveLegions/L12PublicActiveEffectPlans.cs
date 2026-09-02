namespace TwelveLegions.Server;

/// <summary>
/// 公开主动效果共用声明入口。这里只构造 PendingActivation 声明步；声明完成前不支付费用、
/// 不改变来源状态、不进入堆叠。完成后仍由 CommitActiveAbility 的统一费用事务提交。
/// </summary>
public sealed partial class L12GameEngine
{
    private CommandResult? TryBeginPublicActiveDeclaration(int playerIndex, L12CardInstance source, string ability)
    {
        var player = State.Players[playerIndex];
        var enemy = State.Players[1 - playerIndex];
        switch ((source.CardId, ability))
        {
            // 基础费用只有消耗2士气；额外治疗是抽牌结算后的可选段。
            // 是否满足血量与额外支付条件必须以主效果结算后的状态为准。
            case ("S01-03C1", "asgardDraw"):
                return null;
            // 费用只有“消耗2士气”。抽牌后的可选位移属于效果结算，不得在支付阶段
            // 提前询问模式、军团或位置；ResolveActiveEffect 会在抽牌处理后建立场面选择。
            case ("S01-04C1", "factionDrawMove"):
                return null;
            case ("S01-02D1", "sunTopThree"):
            {
                var grave = player.Graveyard.Where(card => L12StructuredCardRules.HasFaction(player, card, "taiyangcheng")
                        && CanEnterHandOrLibrary(card))
                    .Select(card => card.InstanceId).Prepend("mode:none").ToList();
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    PublicActiveStep("optional-card", "graveCard",
                        "众神之乡：预先声明牌顶处理后加入手牌的墓地【太阳城】卡牌，或不选择", grave),
                ]);
            }
            case ("S01-03D1", "valhallaRecover"):
            {
                var grave = player.Graveyard.Where(card => L12StructuredCardRules.HasFaction(player, card, "asgard")
                        && CanEnterHandOrLibrary(card))
                    .Select(card => card.InstanceId).Prepend("mode:none").ToList();
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    PublicActiveStep("optional-card", "graveCard",
                        "英灵殿：预先声明弃置牌库顶2张后加入手牌的墓地【阿斯加德】卡牌，或不选择", grave),
                ]);
            }
            case ("S01-02M1", "isisCanopic"):
            {
                var guards = PublicLegions(player).Where(card => card.CardId == "S01-0212")
                    .Select(card => card.InstanceId).ToList();
                if (guards.Count < 3) return CommandResult.Reject("战场需要3张陵墓守卫");
                var completedIds = player.SpecialZones.CanopicProgress.Select(card => card.CardId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var canopics = player.Graveyard.Where(card => card.Name.Contains("卡诺匹斯", StringComparison.Ordinal)
                        && card.CardType == "artifact" && !completedIds.Contains(card.CardId))
                    .Select(card => card.InstanceId).ToList();
                if (canopics.Count == 0) return CommandResult.Reject("墓地没有可置入圣物区的卡诺匹斯圣物");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    PublicActiveStep("field-legion", "guardCosts", "伊西斯：预先选择弃置的3张陵墓守卫",
                        guards, min: 3, max: 3, autoSelectWhenExact: true),
                    PublicActiveStep("grave-card", "canopicTarget", "伊西斯：预先选择墓地1张卡诺匹斯圣物",
                        canopics),
                    PublicActiveStep("option", "rewardMode", "伊西斯：预先声明完成操作后的奖励",
                        ["mode:draw", "mode:heal"]),
                ]);
            }
            case ("S01-01M2", "mengpoMorale"):
            {
                if (player.Morale.Count >= enemy.Morale.Count || player.Hand.Count == 0)
                    return CommandResult.Reject("士气需少于对方，且需弃置1张手牌");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [PublicActiveStep("hand-card", "discardCost", "孟婆：预先选择弃置的1张手牌",
                    player.Hand.Select(card => card.InstanceId))]);
            }
            case ("S01-04M1", "amaterasuReady"):
            {
                if (player.Hand.Count == 0) return CommandResult.Reject("需要弃置1张手牌");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    PublicActiveStep("hand-card", "discardCost", "天照大神：预先选择弃置的1张手牌",
                        player.Hand.Select(card => card.InstanceId)),
                    PublicActiveStep("target-morale", "moraleTargets", "天照大神：预先选择转为活跃的最多2张休整士气",
                        player.Morale.Where(card => card.Tapped).Select(card => card.InstanceId), min: 0, max: 2),
                ]);
            }
            case ("S01-0214", "cleopatraGuard"):
            {
                var guards = player.Graveyard.Where(card => card.CardId == "S01-0212")
                    .Select(card => card.InstanceId).ToList();
                if (guards.Count == 0 || !EmptySlots(player).Any())
                    return CommandResult.Reject("墓地需要1张陵墓守卫且战场需要空位");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    PublicActiveStep("card", "entryCard", "克利奥帕特拉七世：预先选择墓地1张陵墓守卫", guards),
                    PublicActiveStep("effect-entry-battlefield", "entryBattlefield",
                        "克利奥帕特拉七世：预先选择陵墓守卫登场的战场", ["dynamic"],
                        referenceKey: "entryCard"),
                    PublicActiveStep("effect-entry-slot", "entrySlot",
                        "克利奥帕特拉七世：预先选择陵墓守卫活跃登场的位置", ["dynamic"],
                        referenceKey: "entryCard"),
                ]);
            }
            case ("S01-02C1", "sunGuard"):
            {
                var guards = player.Graveyard.Where(card => card.CardId == "S01-0212")
                    .Select(card => card.InstanceId).ToList();
                if (guards.Count == 0 || !EmptySlots(player).Any())
                    return CommandResult.Reject("墓地需要1张陵墓守卫且战场需要空位");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    PublicActiveStep("card", "entryCard", "不朽之礼：预先选择墓地1张陵墓守卫", guards),
                    PublicActiveStep("effect-entry-battlefield", "entryBattlefield",
                        "不朽之礼：预先选择陵墓守卫登场的战场", ["dynamic"],
                        referenceKey: "entryCard"),
                    PublicActiveStep("effect-entry-slot", "entrySlot",
                        "不朽之礼：预先选择陵墓守卫活跃登场的位置", ["dynamic"],
                        referenceKey: "entryCard"),
                ]);
            }
            case ("S01-0307", "alvidaSummon"):
            {
                var legions = player.Hand.Where(card => card.CardType == "legion" && card.DisasterLevel == 2)
                    .Select(card => card.InstanceId).ToList();
                if (legions.Count == 0 || !EmptySlots(player).Any())
                    return CommandResult.Reject("手牌需要1张天灾等级2的军团且战场需要空位");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    PublicActiveStep("hand-card", "entryCard", "阿尔维达：预先选择手牌1张天灾等级2的军团", legions),
                    PublicActiveStep("effect-entry-battlefield", "entryBattlefield",
                        "阿尔维达：预先选择军团登场的战场", ["dynamic"], referenceKey: "entryCard"),
                    PublicActiveStep("effect-entry-slot", "entrySlot",
                        "阿尔维达：预先选择军团活跃登场的位置", ["dynamic"], referenceKey: "entryCard"),
                ]);
            }
            case ("S01-03M2", "lokiHeal"):
            {
                var grave = player.Graveyard.Where(CanEnterHandOrLibrary).Select(card => card.InstanceId).ToList();
                if (grave.Count < 2) return CommandResult.Reject("墓地需要至少2张可返回牌库的卡牌");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [PublicActiveStep("cards", "graveCards", "洛基：预先选择墓地2张返回牌库底部的卡牌",
                    grave, min: 2, max: 2)]);
            }
            case ("S01-01D1", "palaceExchange"):
            {
                if (source.Tapped) return CommandResult.Reject("凌霄宝殿必须为活跃状态");
                var reviveChoices = new List<string> { "mode:none" };
                if (EmptySlots(player).Any())
                    reviveChoices.AddRange(player.Graveyard.Where(card => card.CardType == "legion"
                            && L12StructuredCardRules.HasFaction(player, card, "tianting"))
                        .Select(card => card.InstanceId));
                if (!PublicLegions(enemy).Any(card => player.Morale.Count >= card.CurrentCost))
                    return CommandResult.Reject("没有士气足以返还其费用的合法敌方军团");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    PublicActiveStep("optional-card", "entryCard",
                        "凌霄宝殿：预先声明要活跃登场的墓地【天廷】军团，或不登场", reviveChoices),
                    PublicActiveStep("effect-entry-battlefield", "entryBattlefield",
                        "凌霄宝殿：预先选择军团登场的战场", ["dynamic"], referenceKey: "entryCard",
                        skipWhenReferenceIsNone: true),
                    PublicActiveStep("effect-entry-slot", "entrySlot",
                        "凌霄宝殿：预先选择军团活跃登场的位置", ["dynamic"], referenceKey: "entryCard",
                        skipWhenReferenceIsNone: true),
                    PublicActiveStep("public-palace-enemy", "enemyTarget",
                        "凌霄宝殿：预先选择要击杀并按费用返还士气的敌方军团", ["dynamic"],
                        referenceKey: "entryCard"),
                ]);
            }
            case ("S01-04D1", "yomiSweep"):
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    PublicActiveStep("public-enemy-after-cost-debuff", "kill3Target",
                        "黄泉之门：预先声明费用-1后可击杀的费用不高于3目标，或不选择", ["dynamic"],
                        costThreshold: 3),
                    PublicActiveStep("public-enemy-after-cost-debuff", "kill1Target",
                        "黄泉之门：预先声明另一张费用-1后可击杀的费用不高于1目标，或不选择", ["dynamic"],
                        costThreshold: 1),
                ]);
            case ("S01-04D1", "yomiRecover"):
            {
                if (source.Tapped) return CommandResult.Reject("黄泉之门必须为活跃状态");
                var grave = player.Graveyard.Where(card => L12StructuredCardRules.HasFaction(player, card, "gaotianyuan"))
                    .Select(card => card.InstanceId).ToList();
                if (grave.Count == 0) return CommandResult.Reject("墓地没有可回收的【高天原】卡牌");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [PublicActiveStep("grave-card", "graveCard",
                    "黄泉之门：预先选择墓地1张【高天原】卡牌加入手牌", grave)]);
            }
            case ("S01-04M1", "amaterasuKill"):
            {
                var targets = PublicLegions(enemy).Select(card => card.InstanceId).ToList();
                if (targets.Count == 0) return CommandResult.Reject("对方战场没有可选择的军团");
                return BeginPendingActivationSequence(playerIndex, source, ability,
                [
                    PublicActiveStep("active-target", "debuffTarget",
                        "天照大神：预先选择本回合费用-1的敌方军团", targets),
                    PublicActiveStep("public-enemy-after-declared-cost-debuff", "killTarget",
                        "天照大神：预先声明随后击杀的费用为0军团，或不选择", ["dynamic"],
                        referenceKey: "debuffTarget"),
                ]);
            }
            case ("S02-05D1", "divinityPower"):
            {
                if (player.Morale.Count(card => card.IsGodPower && !card.Tapped) < 2)
                    return CommandResult.Reject("需要2张活跃的神力");
                var enemies = PublicLegions(enemy).Select(card => card.InstanceId).ToList();
                var damageChoices = enemies.Count == 0 ? new List<string> { "mode:none" } : enemies;
                var grave = player.Graveyard.Where(card => L12StructuredCardRules.HasFaction(player, card, "olympus"))
                    .Select(card => card.InstanceId).ToList();
                var entry = player.Hand.Concat(player.Graveyard)
                    .Where(card => L12StructuredCardRules.HasFaction(player, card, "olympus")
                        && card.CardType == "legion" && card.CurrentCost <= 4)
                    .Select(card => card.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Prepend("mode:none").ToList();
                var steps = new List<L12ActivationSelectionStep>
                {
                    PublicActiveStep("option", "mode", "诸神巅：预先选择一项效果",
                        ["mode:recover", "mode:damage"]),
                    PublicActiveStep("grave-card", "recoverCard",
                        "诸神巅：预先选择墓地1张【奥林匹斯】卡牌加入手牌", grave,
                        requiredChoice: "mode:recover"),
                    PublicActiveStep("optional-card", "entryCard",
                        "诸神巅：预先选择随后活跃登场的费用不高于4军团，或不登场", entry,
                        requiredChoice: "mode:recover"),
                    PublicActiveStep("effect-entry-battlefield", "entryBattlefield",
                        "诸神巅：预先选择军团登场的战场", ["dynamic"], referenceKey: "entryCard",
                        skipWhenReferenceIsNone: true, requiredChoice: "mode:recover"),
                    PublicActiveStep("effect-entry-slot", "entrySlot",
                        "诸神巅：预先选择军团活跃登场的位置", ["dynamic"], referenceKey: "entryCard",
                        skipWhenReferenceIsNone: true, requiredChoice: "mode:recover"),
                };
                for (var index = 1; index <= 6; index++)
                    steps.Add(PublicActiveStep("active-target", $"damageTarget{index}",
                        $"诸神巅：预先分配第{index}点1000兵力伤害", damageChoices,
                        requiredChoice: "mode:damage"));
                if (grave.Count == 0)
                    steps[0].ValidChoices.Remove("mode:recover");
                if (steps[0].ValidChoices.Count == 0)
                    return CommandResult.Reject("没有可声明的诸神巅效果");
                return BeginPendingActivationSequence(playerIndex, source, ability, steps);
            }
            default:
                return null;
        }
    }

    private static L12ActivationSelectionStep PublicActiveStep(string kind, string key, string text,
        IEnumerable<string> choices, int min = 1, int max = 1, string? referenceKey = null,
        bool skipWhenReferenceIsNone = false, int? costThreshold = null, string? requiredChoice = null,
        bool autoSelectWhenExact = false)
        => new()
        {
            Kind = kind,
            DeclarationKey = key,
            Text = text,
            ValidChoices = choices.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MinChoose = min,
            MaxChoose = max,
            AutoSelectWhenExact = autoSelectWhenExact,
            ReferenceDeclarationKey = referenceKey,
            SkipWhenReferenceIsNone = skipWhenReferenceIsNone,
            CostThreshold = costThreshold,
            RequiredDeclaredChoice = requiredChoice,
            ChoiceLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode:none"] = "不发动",
            },
        };

    private static string PublicDeclaredEnemyId(string? declaration)
        => (declaration ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(value => !value.StartsWith("mode:", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("battlefield:", StringComparison.OrdinalIgnoreCase)
                && value.Split(':').Length != 2) ?? string.Empty;
}
