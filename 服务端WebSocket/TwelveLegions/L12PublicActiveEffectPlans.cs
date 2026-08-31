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
                var grave = player.Graveyard.Where(card => card.Faction == "gaotianyuan")
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
                var grave = player.Graveyard.Where(card => card.Faction == "olympus")
                    .Select(card => card.InstanceId).ToList();
                var entry = player.Hand.Concat(player.Graveyard)
                    .Where(card => card.Faction == "olympus" && card.CardType == "legion" && card.CurrentCost <= 4)
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
        bool skipWhenReferenceIsNone = false, int? costThreshold = null, string? requiredChoice = null)
        => new()
        {
            Kind = kind,
            DeclarationKey = key,
            Text = text,
            ValidChoices = choices.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MinChoose = min,
            MaxChoose = max,
            ReferenceDeclarationKey = referenceKey,
            SkipWhenReferenceIsNone = skipWhenReferenceIsNone,
            CostThreshold = costThreshold,
            RequiredDeclaredChoice = requiredChoice,
            ChoiceLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode:none"] = "不选择公开对象",
            },
        };

    private static string PublicDeclaredEnemyId(string? declaration)
        => (declaration ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(value => !value.StartsWith("mode:", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("battlefield:", StringComparison.OrdinalIgnoreCase)
                && value.Split(':').Length != 2) ?? string.Empty;
}
