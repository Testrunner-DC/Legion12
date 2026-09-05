namespace TwelveLegions.Server;

internal static partial class L12CompositeEffectPlans
{
    internal static readonly IReadOnlyDictionary<string, L12CompositeEffectSegmentSpec[]> StarterHandPlayPlans =
        new Dictionary<string, L12CompositeEffectSegmentSpec[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["starter-tomb-guard-revive"] = [new("starter-tomb-guard-revive", "使所选陵墓守卫活跃登场", PublicTargetKeys: ["entryCard", "entrySlot"])],
            ["legendary-bloodline"] =
            [
                new("legendary-bloodline-base", "使所选阿斯加德军团本回合兵力+2000", PublicTargetKeys: ["buffTarget"]),
                new("legendary-bloodline-grave", "墓地每有3张阿斯加德军团，所选军团本回合兵力额外+1000", PublicTargetKeys: ["buffTarget"]),
            ],
            ["invasion-fire"] = [new("invasion-fire", "叠放至所选高天原军团下方", PublicTargetKeys: ["attachTarget"])],
            ["hunter-gift"] = [new("hunter-gift", "执行所选猎神赐福", PublicTargetKeys: ["shockTarget", "rangedTarget"])],
        };

    // These plans are not hand plays, but they use the same declarative continuation
    // machinery so every sentence opens its own response window.
    internal static readonly IReadOnlyDictionary<string, L12CompositeEffectSegmentSpec[]> StarterContinuationPlans =
        new Dictionary<string, L12CompositeEffectSegmentSpec[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["starter-aeneas-promotion"] =
            [
                new("aeneas-promotion-search", "查看牌库并使最多2张远程军团活跃登场"),
                new("aeneas-promotion-shuffle", "随后重洗牌库"),
            ],
            ["starter-athena-active"] =
            [
                new("athena-morale-flip", "翻转已声明的1张士气", PublicTargetKeys: ["flipTarget"]),
                new("athena-front-buff", "选择我方前排最多2张【奥林匹斯】军团，本回合兵力+1000，且对对方主宰造成的伤害+1", PublicTargetKeys: ["buffTargets"]),
            ],
        };
}

public sealed partial class L12GameEngine
{
    private bool TryBuildStarterCompositeDeclaration(int controller, L12CardInstance source,
        L12PlayerState player, L12PlayerState opponent, List<L12ActivationSelectionStep> steps)
    {
        var plan = L12StructuredCardRules.StarterHandPlayPlanId(source.CardId);
        if (plan is null) return false;
        switch (plan)
        {
            case "starter-tomb-guard-revive":
                steps.Add(CompositeStep("grave-card", "entryCard", "沙漠送葬：选择墓地1张〈陵墓守卫〉",
                    player.Graveyard.Where(card => card.CardId == L12StructuredCardRules.StarterTombGuardCardId)
                        .Select(card => card.InstanceId), 1));
                steps.Add(CompositeStep("unused-slot", "entrySlot", "沙漠送葬：选择活跃登场位置",
                    EmptySlots(player), 1));
                break;
            case "legendary-bloodline":
            {
                var targets = PublicLegions(player)
                    .Where(card => L12StructuredCardRules.HasFaction(player, card, "asgard")).ToArray();
                var currentBonus = 2000 + player.Graveyard.Sum(card =>
                    L12StructuredCardRules.StarterGraveFactionLegionCopies(player, card, "asgard")) / 3 * 1000;
                steps.Add(CompositeStep("field-legion", "buffTarget", "传奇的血脉：选择我方1张【阿斯加德】军团",
                    targets.Select(card => card.InstanceId), 1, 1,
                    targets.ToDictionary(card => card.InstanceId, _ => $"当前加{currentBonus}",
                        StringComparer.OrdinalIgnoreCase)));
                break;
            }
            case "invasion-fire":
                steps.Add(CompositeStep("field-legion", "attachTarget", "侵略如火：选择我方1张【高天原】军团",
                    PublicLegions(player).Where(card => L12StructuredCardRules.HasFaction(player, card, "gaotianyuan"))
                        .Select(card => card.InstanceId), 1));
                break;
            case "hunter-gift":
            {
                var olympus = PublicLegions(player)
                    .Where(card => L12StructuredCardRules.HasFaction(player, card, "olympus")).ToArray();
                var ranged = olympus.Where(L12StructuredCardRules.HasAnyRowRangeBonus).ToArray();
                var modes = new List<string>();
                if (olympus.Length > 0) modes.Add("mode:shock");
                if (ranged.Length > 0) modes.Add("mode:ranged");
                steps.Add(CompositeStep("option", "mode", "猎神的赐福：选择本回合震击伤害+2000或进攻时兵力+2000",
                    modes, 1, 1, new()
                    {
                        ["mode:shock"] = "所选奥林匹斯军团本回合震击伤害+2000",
                        ["mode:ranged"] = "所选奥林匹斯远程军团本回合进攻时兵力+2000",
                    }));
                steps.Add(CompositeStep("field-legion", "shockTarget", "猎神的赐福：选择本回合震击伤害+2000的奥林匹斯军团",
                    olympus.Select(card => card.InstanceId), 1, requiredChoice: "mode:shock"));
                steps.Add(CompositeStep("field-legion", "rangedTarget", "猎神的赐福：选择本回合进攻时兵力+2000的奥林匹斯远程军团",
                    ranged.Select(card => card.InstanceId), 1, requiredChoice: "mode:ranged"));
                break;
            }
        }
        return true;
    }

    private bool ValidateStarterCompositeDeclaration(int controller, L12CardInstance source,
        IReadOnlyDictionary<string, List<string>> declared)
    {
        var plan = L12StructuredCardRules.StarterHandPlayPlanId(source.CardId);
        if (plan is null) return false;
        var player = State.Players[controller];
        bool Own(string key, Func<L12CardInstance, bool> predicate)
        {
            var id = declared.GetValueOrDefault(key, []).SingleOrDefault();
            return PublicLegions(player).Any(card => card.InstanceId == id && predicate(card));
        }
        return plan switch
        {
            "starter-tomb-guard-revive" => declared.GetValueOrDefault("entryCard", []).SingleOrDefault() is { } entry
                && player.Graveyard.Any(card => card.InstanceId == entry
                    && card.CardId == L12StructuredCardRules.StarterTombGuardCardId)
                && declared.GetValueOrDefault("entrySlot", []).SingleOrDefault() is { } slot
                && EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase),
            "legendary-bloodline" => Own("buffTarget", card => L12StructuredCardRules.HasFaction(player, card, "asgard")),
            "invasion-fire" => Own("attachTarget", card => L12StructuredCardRules.HasFaction(player, card, "gaotianyuan")),
            "hunter-gift" => declared.GetValueOrDefault("mode", []).SingleOrDefault() switch
            {
                "mode:shock" => Own("shockTarget", card => L12StructuredCardRules.HasFaction(player, card, "olympus")),
                "mode:ranged" => Own("rangedTarget", card => L12StructuredCardRules.HasFaction(player, card, "olympus")
                    && L12StructuredCardRules.HasAnyRowRangeBonus(card)),
                _ => false,
            },
            _ => false,
        };
    }
}
