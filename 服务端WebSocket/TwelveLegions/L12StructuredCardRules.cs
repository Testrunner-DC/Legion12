namespace TwelveLegions.Server;

/// <summary>
/// 卡面中会随位置改变的职介与战斗参数。这里是实战判定和原子化后台共用的权威定义，
/// 禁止再在进攻流程中按卡号分别判断距离、远程无损或“兵力视为”。
/// </summary>
public sealed record L12ConditionalCombatProfile(
    string? EffectiveProfession,
    bool HasRangeBonus,
    bool HasRangedNoLoss,
    bool HasAttackNoLoss,
    bool CannotBeRanged,
    int? AttackTroopsSetValue,
    int IncomingRangedCombatDamageAdjustment,
    string ConditionExpression);

public static partial class L12StructuredCardRules
{
    private static readonly HashSet<string> AlwaysRangedCards = new(StringComparer.Ordinal)
    {
        "S01-0003", "S01-0110", "S01-0111", "S01-0112", "S01-0113", "S01-0114", "S01-0116",
        "S01-0208", "S01-0209", "S01-0210", "S01-0211", "S01-0214", "S01-0309", "S01-0313",
        "S01-0314", "S01-0410", "S01-0411", "S01-0413", "S01-0416", "S02-0614", "S02-0617",
        "S02-0618", "ST01-07", "ST01-09", "ST02-08", "ST03-05", "ST04-07", "ST05-03",
        "ST05-04", "ST05-08", "ST05-09",
    };

    private static readonly HashSet<string> FrontOnlyRangedCards = new(StringComparer.Ordinal)
    {
        "S01-0115", "S01-0213", "S01-0316", "S01-0415", "S02-0619", "ST01-08",
    };

    private static readonly HashSet<string> FrontRowTauntOverlayCards = new(StringComparer.Ordinal)
    {
        "S01-0107", "S01-0204", "S01-0312", "S02-0615",
        "ST01-04", "ST02-02", "ST04-01", "ST06-02",
    };

    private static readonly HashSet<string> AlwaysAttackNoLossCards = new(StringComparer.Ordinal)
    {
        "S01-0101",
    };

    private static readonly HashSet<string> CannotBeRangedCards = new(StringComparer.Ordinal)
    {
        "S01-0101",
    };

    // 卡面冒号前的可选主宰伤害属于登场费用替代，必须由结构化身份语义驱动，
    // 禁止根据可变的 EffectText 推断按钮合法性或最终费用。
    private static readonly HashSet<string> OptionalSelfDamageEntryDiscountCards = new(StringComparer.Ordinal)
    {
        "S01-0303", "S01-0304", "S01-0308", "S01-0310", "S01-0314", "S02-0303",
    };

    // 只有写明“触发”的天灾在翻开时播放触发式效果展示；纯持续天灾只公开卡牌。
    // 此列表同时供实战展示和原子审计使用，不再扫描卡面文本。
    private static readonly HashSet<string> TriggeredDisasterCards = new(StringComparer.Ordinal)
    {
        "S01-DS02", "S01-DS03", "S01-DS04", "S01-DS05", "S01-DS06", "S01-DS07", "S01-DS09",
        "S02-DS02", "S02-DS03", "S02-DS04", "S02-DS05", "S02-DS06", "ST-DS01", "ST-DS03",
    };

    // 主动休整是规则费用类型而非按钮文案。无眠之夜等监听入口查询此集合，
    // 不得从实例 EffectText 推断，以免卡面纠错或展示裁剪改变实战结果。
    private static readonly HashSet<string> ActiveRestSourceCards = new(StringComparer.Ordinal)
    {
        "S01-0105", "S01-0109", "S01-0117", "S01-01D1", "S01-0214", "S01-0215", "S01-0317",
        "S01-03D1", "S01-04D1", "S02-0003", "S02-0104", "S02-0204", "S02-0205", "S02-0404",
        "S02-0510", "S02-0513", "S02-0520", "S02-05D1", "S02-0603", "S02-0616", "S02-06D1",
        "ST02-05", "ST03-05", "ST03-07", "ST04-06", "ST05-06", "ST06-09",
    };

    // 卡面明确写明“登场回合不受反击战术效果影响”的军团。
    // 响应窗口只查询这一处结构化规则，禁止再从 EffectText.Contains 推断。
    private static readonly HashSet<string> SummonTurnCounterTacticProtectionCards = new(StringComparer.Ordinal)
    {
        "S01-0201", "S01-0202", "ST02-01",
    };

    // 冒号前存在“先选择并支付登场时效果费用”的卡，必须在效果入栈前完成预声明。
    // 身份映射集中在结构化规则层；运行时入口只查询规则能力，禁止重新出现分散卡号分支。
    private static readonly HashSet<string> PreStackEnterCostCards = new(StringComparer.Ordinal)
    {
        "S02-0101",
    };

    public static string EffectiveFaction(L12PlayerState owner, L12CardInstance card)
    {
        if (!string.Equals(card.Faction, "universal", StringComparison.Ordinal)) return card.Faction;
        var ringActive = owner.Relic?.CardId == "S02-0008"
            || owner.ExtraRelics.Any(relic => relic.CardId == "S02-0008");
        return ringActive ? owner.Faction : card.Faction;
    }

    public static bool HasFaction(L12PlayerState owner, L12CardInstance card, string faction)
        => string.Equals(EffectiveFaction(owner, card), faction, StringComparison.Ordinal);

    public static bool HasOptionalSelfDamageEntryDiscount(string cardId)
        => OptionalSelfDamageEntryDiscountCards.Contains(cardId);

    public static bool HasTriggeredDisasterEffect(string cardId)
        => TriggeredDisasterCards.Contains(cardId);

    public static bool HasActiveRestAbility(string cardId)
        => ActiveRestSourceCards.Contains(cardId);

    public static bool HasAnyRowRangeBonus(L12CardInstance card)
        => CombatProfile(card, 0).HasRangeBonus || CombatProfile(card, 1).HasRangeBonus;

    public static bool HasAnyRowRangedNoLoss(L12CardInstance card)
        => CombatProfile(card, 0).HasRangedNoLoss || CombatProfile(card, 1).HasRangedNoLoss;

    public static bool HasAnyRowAttackNoLoss(L12CardInstance card)
        => CombatProfile(card, 0).HasAttackNoLoss || CombatProfile(card, 1).HasAttackNoLoss;

    public static bool CannotBeRangedInAnyRow(L12CardInstance card)
        => CombatProfile(card, 0).CannotBeRanged || CombatProfile(card, 1).CannotBeRanged;

    public static string? HandPlayBlockReason(L12PlayerState controller, L12CardInstance card)
    {
        // 〈猎杀时刻〉冒号前的“将墓地4张卡牌返回牌库底部”是发动费用，
        // 必须在支付士气、移出手牌和入栈之前完成合法性校验。
        if (card.CardId == "S01-0319" && controller.Graveyard.Count < 4)
            return "〈猎杀时刻〉需要墓地至少有4张卡牌作为发动费用";
        if (card.CardType != "artifact") return null;
        var artifactZone = controller.Relic is null
            ? controller.ExtraRelics
            : controller.ExtraRelics.Prepend(controller.Relic);
        if (artifactZone.Any(source => source.CardId == "S02-0305"))
            return "〈安德华拉诺特〉使我方无法从手牌打出圣物";
        // “其他圣物”不包含另一张〈黄金圣甲虫〉。同名圣物可正常打出并按
        // 圣物顶替规则处理；不同名圣物仍由此权威查询同时禁用按钮与提交。
        if (card.CardId != "S02-0205"
            && artifactZone.Any(source => source.CardId == "S02-0205"))
            return "〈黄金圣甲虫〉位于我方圣物区，我方无法从手牌打出其他圣物";
        return null;
    }

    public static int HandPlayCostModifier(L12PlayerState controller, L12CardInstance card)
    {
        if (!TryGetStructuredAbilities(card.CardId, out var abilities)) return 0;
        var godPowerCount = controller.Morale.Count(morale => morale.IsGodPower);
        var modifier = 0;
        foreach (var ability in abilities.Where(ability => ability.ExecutionModel == "continuous"))
        {
            var condition = ability.Atoms.FirstOrDefault(atom => atom.Kind == L12AtomKinds.Condition)
                ?.Parameters.GetValueOrDefault("expression");
            if (!HandConditionMatches(condition, controller, card, godPowerCount)) continue;
            foreach (var atom in ability.Atoms.Where(atom => atom.Kind == L12AtomKinds.SetState
                && atom.Parameters.GetValueOrDefault("key") == "source.derived-cost"
                && atom.Parameters.GetValueOrDefault("operation") == "add"))
                if (int.TryParse(atom.Parameters.GetValueOrDefault("value"), out var value))
                    modifier += value;
        }
        return modifier;
    }

    public static bool HasTaunt(L12CardInstance card, int row)
    {
        if (card.TauntUntilTurn >= 0) return !card.TauntRequiresFrontRow || row == 0;
        var abilities = GetCombatRuleAbilities(card.CardId);
        return abilities.Where(ability => IsContinuous(ability.ExecutionModel) && ConditionMatchesRow(ability, row))
            .Where(ability => !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Condition
                && atom.Parameters.GetValueOrDefault("expression")?.Contains("source.has-ability=", StringComparison.OrdinalIgnoreCase) == true))
            .Any(ability => AbilityGrantsKeyword(ability, abilities, "taunt"));
    }

    public static bool CannotReceiveBackRowSupport(L12CardInstance card, int row)
    {
        var abilities = GetCombatRuleAbilities(card.CardId);
        return abilities.Where(ability => IsContinuous(ability.ExecutionModel) && ConditionMatchesRow(ability, row))
            .SelectMany(ability => ability.Atoms)
            .Any(atom => atom.Kind == L12AtomKinds.AttackRule
                && atom.Parameters.GetValueOrDefault("cannotReceiveBackRowSupport") == "true");
    }

    public static bool HasCooperativeSupport(L12CardInstance card, int row)
    {
        if (row != 1) return false;
        return GetCombatRuleAbilities(card.CardId)
            .Where(ability => IsContinuous(ability.ExecutionModel) && ConditionMatchesRow(ability, row))
            .SelectMany(ability => ability.Atoms)
            .Any(atom => atom.Kind == L12AtomKinds.Keyword
                && atom.Parameters.GetValueOrDefault("keywordRef") == "cooperative-support");
    }

    private static bool HandConditionMatches(string? expression, L12PlayerState controller,
        L12CardInstance source, int godPowerCount)
    {
        if (string.IsNullOrWhiteSpace(expression) || !expression.Contains("source.zone=hand", StringComparison.Ordinal))
            return false;
        if (!controller.Hand.Any(card => card.InstanceId == source.InstanceId)) return false;
        if (expression.Contains("controller.god-power=0", StringComparison.Ordinal) && godPowerCount != 0) return false;
        if (expression.Contains("controller.god-power>=5", StringComparison.Ordinal) && godPowerCount < 5) return false;

        if (expression.Contains("controller.hp<=7", StringComparison.Ordinal) && controller.Hp > 7) return false;

        const string fieldCardPrefix = "controller.field.card-id=";
        var fieldCardId = expression.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(term => term.StartsWith(fieldCardPrefix, StringComparison.Ordinal))?[fieldCardPrefix.Length..];
        if (fieldCardId is not null && !controller.Field.SelectMany(row => row)
                .Any(card => card?.CardId == fieldCardId))
            return false;
        return true;
    }

    public static L12ConditionalCombatProfile CombatProfile(L12CardInstance card, int row)
    {
        var profession = card.Profession;
        var ranged = false;
        var rangedNoLoss = false;
        var attackNoLoss = false;
        var cannotBeRanged = false;
        var professionDerived = false;
        int? attackTroopsSetValue = null;
        var incomingRangedCombatDamageAdjustment = 0;
        var matchedConditions = new List<string>();
        var abilities = GetCombatRuleAbilities(card.CardId);
        foreach (var ability in abilities.Where(ability => ConditionMatchesRow(ability, row)))
        {
            var expression = ConditionExpression(ability);
            if (!string.IsNullOrWhiteSpace(expression)) matchedConditions.Add(expression);
            if (IsContinuous(ability.ExecutionModel))
            {
                foreach (var atom in ability.Atoms)
                {
                    if (atom.Kind == L12AtomKinds.SetState
                        && atom.Parameters.GetValueOrDefault("key") == "source.derived-profession")
                    {
                        profession = atom.Parameters.GetValueOrDefault("value") ?? profession;
                        professionDerived = true;
                    }
                    if (atom.Kind != L12AtomKinds.AttackRule) continue;
                    ranged |= atom.Parameters.GetValueOrDefault("rangeBonus") == "1";
                    rangedNoLoss |= atom.Parameters.GetValueOrDefault("rangedNoLoss") == "true";
                    attackNoLoss |= atom.Parameters.GetValueOrDefault("attackNoLoss") == "true";
                    cannotBeRanged |= atom.Parameters.GetValueOrDefault("cannotBeRanged") == "true";
                    if (int.TryParse(atom.Parameters.GetValueOrDefault("incomingRangedCombatDamageAdjustment"),
                            out var adjustment))
                        incomingRangedCombatDamageAdjustment += adjustment;
                }
            }
            if (ability.Trigger != "attack") continue;
            foreach (var atom in ability.Atoms.Where(atom => atom.Kind == L12AtomKinds.ModifyTroops
                && atom.Parameters.GetValueOrDefault("operation") == "set"))
                if (int.TryParse(atom.Parameters.GetValueOrDefault("value"), out var setValue))
                    attackTroopsSetValue = setValue;
        }

        // 职介本身是规则能力的来源，而不只是展示标签。任何持续效果将军团
        // “视为【弓手】”后，都必须立即获得弓手的完整职介能力，禁止再要求
        // 每张赋予职介的卡重复写距离与远程无损，或在进攻流程中按卡号特判。
        if (professionDerived && string.Equals(profession, "弓手", StringComparison.Ordinal))
        {
            ranged = true;
            rangedNoLoss = true;
        }

        return new(profession, ranged, ranged && rangedNoLoss, attackNoLoss, cannotBeRanged, attackTroopsSetValue,
            incomingRangedCombatDamageAdjustment,
            matchedConditions.Count == 0 ? "always" : string.Join(';', matchedConditions.Distinct(StringComparer.Ordinal)));
    }

    public static bool ProtectsMasterFromTroops(L12CardInstance card, int row, int attackerTroops)
        => GetCombatRuleAbilities(card.CardId)
            .Where(ability => IsContinuous(ability.ExecutionModel) && ConditionMatchesRow(ability, row))
            .SelectMany(ability => ability.Atoms)
            .Where(atom => atom.Kind == L12AtomKinds.AttackRule)
            .Select(atom => atom.Parameters.GetValueOrDefault("protectMasterFromTroopsAtMost"))
            .Any(value => int.TryParse(value, out var threshold) && attackerTroops <= threshold);

    public static string? EffectiveProfession(L12CardInstance card, int row)
        => CombatProfile(card, row).EffectiveProfession;

    public static bool HasProfession(L12CardInstance card, int row, string profession)
        => string.Equals(EffectiveProfession(card, row), profession, StringComparison.Ordinal);

    public static bool HasSummonTurnCounterTacticProtection(L12CardInstance card, int currentRound)
        => card.SummonRound == currentRound && SummonTurnCounterTacticProtectionCards.Contains(card.CardId);

    public static bool RequiresPreStackEnterCost(L12CardInstance card)
        => PreStackEnterCostCards.Contains(card.CardId);

    public static bool RequiresPreStackHandPlayTarget(string cardId)
        => cardId == "S02-0622";

    public static bool RequiresReadySourceForActiveChoice(string cardId)
        => cardId == "S01-0215";

    public static bool RequiresOwnLegionResponseTarget(string cardId)
        => cardId == "S01-0019";

    public static string? PostAttackDeclarationKind(string cardId, string trigger)
    {
        if (trigger != "reaction") return null;
        return cardId switch
        {
            "S01-0017" => "last-stand",
            "S01-0420" => "seppuku",
            _ => null,
        };
    }

    public static bool CanOfferPostAttackReaction(string cardId, bool hasAnyOpponentLegion,
        bool hasRestedOpponentLegion)
        => cardId switch
        {
            "S01-0017" => hasRestedOpponentLegion,
            "S01-0420" => hasAnyOpponentLegion,
            "S02-0523" => true,
            "ST01-10" => true,
            _ => false,
        };

    public static IReadOnlyList<L12StructuredAbilityTemplate> GetCombatRuleAbilities(string cardId)
    {
        var result = new List<L12StructuredAbilityTemplate>();
        if (TryGetStructuredAbilities(cardId, out var structured)) result.AddRange(structured);
        result.AddRange(GetCombatOverlayAbilities(cardId));
        return result;
    }

    public static IReadOnlyList<L12StructuredAbilityTemplate> GetCombatOverlayAbilities(string cardId)
    {
        var result = new List<L12StructuredAbilityTemplate>();
        if (AlwaysRangedCards.Contains(cardId)) result.Add(RangedAbility());
        if (FrontOnlyRangedCards.Contains(cardId)) result.Add(RangedAbility(frontOnly: true));
        if (FrontRowTauntOverlayCards.Contains(cardId)) result.Add(FrontRowTauntAbility());
        if (AlwaysAttackNoLossCards.Contains(cardId)) result.Add(AttackNoLossAbility());
        if (CannotBeRangedCards.Contains(cardId)) result.Add(CannotBeRangedAbility());
        return result;
    }

    private static bool AbilityGrantsKeyword(L12StructuredAbilityTemplate ability,
        IReadOnlyList<L12StructuredAbilityTemplate> abilities, string keyword)
    {
        if (ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Keyword
            && atom.Parameters.GetValueOrDefault("keywordRef") == keyword)) return true;
        foreach (var atom in ability.Atoms.Where(atom => atom.Kind == L12AtomKinds.SetState))
        {
            var reference = atom.Parameters.GetValueOrDefault("abilityRef");
            if (string.IsNullOrWhiteSpace(reference)) continue;
            var separator = reference.LastIndexOf(':');
            if (separator < 0 || !int.TryParse(reference[(separator + 1)..], out var sequence)) continue;
            if (sequence < 1 || sequence > abilities.Count) continue;
            if (abilities[sequence - 1].Atoms.Any(candidate => candidate.Kind == L12AtomKinds.Keyword
                && candidate.Parameters.GetValueOrDefault("keywordRef") == keyword)) return true;
        }
        return false;
    }

    private static bool IsContinuous(string executionModel)
        => executionModel is "continuous" or "granted-continuous";

    private static string? ConditionExpression(L12StructuredAbilityTemplate ability)
        => ability.Atoms.FirstOrDefault(atom => atom.Kind == L12AtomKinds.Condition)
            ?.Parameters.GetValueOrDefault("expression");

    private static bool ConditionMatchesRow(L12StructuredAbilityTemplate ability, int row)
    {
        var expression = ConditionExpression(ability);
        if (string.IsNullOrWhiteSpace(expression)) return true;
        if (expression.Contains("source.row=front", StringComparison.Ordinal) && row != 0) return false;
        if (expression.Contains("source.row=back", StringComparison.Ordinal) && row != 1) return false;
        return true;
    }

    public static bool TryGetStructuredAbilities(L12CardDefinition card, out IReadOnlyList<L12StructuredAbilityTemplate> abilities)
        => TryGetStructuredAbilities(card.Id, out abilities);

    public static bool TryGetStructuredAbilities(string cardId, out IReadOnlyList<L12StructuredAbilityTemplate> abilities)
    {
        if (TryGetStarterBatch1Abilities(cardId, out abilities)) return true;
        if (TryGetStarterTargetedBatch2AAbilities(cardId, out abilities)) return true;
        if (TryGetStarterTargetedBatch2BAbilities(cardId, out abilities)) return true;
        if (TryGetStarterBatch3AAbilities(cardId, out abilities)) return true;
        if (TryGetStarterBatch3BAbilities(cardId, out abilities)) return true;
        if (TryGetHumanAssistedS02BatchAbilities(cardId, out abilities)) return true;
        abilities = cardId switch
        {
            "S01-0215" => AnkhSteleAbilities(),
            "S02-0501" => HeraclesPromotedAbilities(),
            "S02-0502" => HeraclesAbilities(),
            "S02-0503" => AchillesPromotedAbilities(),
            "S02-0504" => AchillesAbilities(),
            "S02-0505" => PerseusPromotedAbilities(),
            "S02-0506" => PerseusAbilities(),
            "S02-0507" => AtalantaAbilities(),
            "S02-0508" => AtalantaBaseAbilities(),
            "S02-0509" => OdysseusAbilities(),
            "S02-0510" => HippolytaAbilities(),
            "S02-0511" => ProloteAbilities(),
            "S02-0512" => AeneasAbilities(),
            "S02-0513" => AristotleAbilities(),
            "S02-0514" => PlatoAbilities(),
            "S02-0515" => HelenAbilities(),
            "S02-0516" => HannibalAbilities(),
            "S02-0517" => PenthesileaAbilities(),
            "S02-0518" => TheseusAbilities(),
            "S02-0519" => SpartanWarriorAbilities(),
            "ST04-07" => CooperativeSupportAbilities(),
            "S02-0520" => ForgeAbilities(),
            "S02-0521" => GloryRoadAbilities(),
            "S02-0522" => NyxMeteorAbilities(),
            "S02-0523" => TrojanHorseAbilities(),
            "S02-05M1" => ArtemisAbilities(),
            "S02-05M2" => PrometheusAbilities(),
            "S02-05C1" => GodPowerAbilities(),
            "S02-05C1A" => OlympusMoraleAbilities(),
            "S02-01M1" => WukongAbilities(),
            "S01-0409" => YoshitsuneAbilities(),
            _ => [],
        };
        return abilities.Count > 0;
    }

    private static IReadOnlyList<L12StructuredAbilityTemplate> AnkhSteleAbilities() =>
    [
        new("enter", "triggered", "登场时 选择我方1张<陵墓守卫>，本回合兵力+2000。",
        [
            new(L12AtomKinds.SelectTarget, "选择我方 1 张<陵墓守卫>", "target", new()
            {
                ["zone"] = "controller.field", ["filter"] = "card-id=S01-0212", ["min"] = "1", ["max"] = "1",
            }),
            new(L12AtomKinds.ModifyTroops, "所选军团兵力 +2000", "resolution", new()
            {
                ["operation"] = "add", ["value"] = "2000", ["selection"] = "declared-targets",
            }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ], "confirmed", "user-20260829"),
        new("active", "activated", "主动休整 选择 ABILITY 3 或 ABILITY 4。",
        [
            new(L12AtomKinds.Condition, "我方回合且来源活跃", "condition", new()
            {
                ["expression"] = "controller.turn;source.ready=true",
            }),
            new(L12AtomKinds.SelectMode, "选择一项效果", "target", new()
            {
                ["options"] = "S01-0215:ability:3|S01-0215:ability:4",
            }),
            new(L12AtomKinds.RestSource, "将安卡神碑转为休整", "cost", new()),
        ], "confirmed", "user-20260829"),
        new("mode-ready-guard", "granted-effect", "弃置1张手牌：选择我方1张休整的<陵墓守卫>转为活跃。",
        [
            new(L12AtomKinds.SelectTarget, "选择我方 1 张休整的<陵墓守卫>", "target", new()
            {
                ["zone"] = "controller.field", ["filter"] = "card-id=S01-0212;state=rested", ["min"] = "1", ["max"] = "1",
                ["runtimeAbility"] = "ankhReady",
            }),
            new(L12AtomKinds.Discard, "弃置 1 张手牌", "cost", new()
            {
                ["zone"] = "controller.hand", ["amount"] = "1", ["reason"] = "ability-cost",
            }),
            new(L12AtomKinds.Ready, "所选<陵墓守卫>转为活跃", "resolution", new()
            {
                ["selection"] = "declared-targets",
            }),
        ], "confirmed", "user-20260829"),
        new("mode-rest-and-draw", "granted-effect", "将我方1张<陵墓守卫>转为休整：抽取1张牌。",
        [
            new(L12AtomKinds.SelectTarget, "选择我方 1 张活跃的<陵墓守卫>", "target", new()
            {
                ["zone"] = "controller.field", ["filter"] = "card-id=S01-0212;state=ready", ["min"] = "1", ["max"] = "1",
                ["runtimeAbility"] = "ankhDraw",
            }),
            new(L12AtomKinds.Rest, "所选<陵墓守卫>转为休整", "cost", new()
            {
                ["selection"] = "declared-targets",
            }),
            new(L12AtomKinds.Draw, "抽取 1 张牌", "resolution", new() { ["amount"] = "1" }),
        ], "confirmed", "user-20260829"),
    ];

    private static IReadOnlyList<L12StructuredAbilityTemplate> WukongAbilities() => Assisted(
    [
        new("active", "active", "我方 回合1次 可返还2至8士气：将此主宰作为【斗士】军团在我方前排活跃登场，兵力=本次返还的士气数量×1000，且在登场回合即可进攻。",
        [
            new(L12AtomKinds.Condition, "我方回合且本回合未发动", "condition", new() { ["expression"] = "controller.turn;once-per-turn" }),
            new(L12AtomKinds.SelectTarget, "在场面上选择返还 2 至 8 张士气", "target", new() { ["zone"] = "controller.morale", ["min"] = "2", ["max"] = "8", ["presentation"] = "direct-board" }),
            new(L12AtomKinds.ReturnMorale, "返还所选士气", "cost", new() { ["amount"] = "selected-count", ["selection"] = "declared-targets" }),
            new(L12AtomKinds.MoveZone, "作为【斗士】军团在我方前排活跃登场", "resolution", new() { ["operation"] = "master-enter-as-legion", ["profession"] = "斗士", ["row"] = "front", ["state"] = "active" }),
            new(L12AtomKinds.ModifyTroops, "兵力设为返还士气数量 ×1000", "resolution", new() { ["operation"] = "set", ["value"] = "selected-count*1000" }),
            new(L12AtomKinds.Keyword, "获得本回合可进攻", "resolution", new() { ["keywordRef"] = "charge" }),
        ]),
        new("static", "continuous", "「作为军团」在登场的回合即可进攻。",
        [
            new(L12AtomKinds.Condition, "作为军团且处于登场回合", "condition", new() { ["expression"] = "source.is-master-legion;source.entered-this-turn" }),
            new(L12AtomKinds.AttackRule, "本回合可进攻", "resolution", new() { ["canAttack"] = "true" }),
            new(L12AtomKinds.Duration, "持续至登场回合结束", "duration", new() { ["duration"] = "entry-turn" }),
        ]),
        new("after-attack-or-turn-end", "triggered", "「作为军团」我方回合结束时 或 进攻后 返回主宰区，若我方士气少于对方，可从士气牌库追加1张休整的士气。",
        [
            new(L12AtomKinds.Condition, "作为军团", "condition", new() { ["expression"] = "source.is-master-legion" }),
            new(L12AtomKinds.MoveZone, "返回主宰区", "resolution", new() { ["operation"] = "return-master-zone" }),
            new(L12AtomKinds.Condition, "我方士气少于对方", "condition", new() { ["expression"] = "controller.morale-count<opponent.morale-count" }),
            new(L12AtomKinds.Optional, "可追加士气", "condition", new()),
            new(L12AtomKinds.AddMorale, "从士气牌库追加 1 张休整士气", "resolution", new() { ["amount"] = "1", ["state"] = "rested" }),
        ]),
        new("leave", "replacement", "「作为军团」离场时 返回主宰区。",
        [
            new(L12AtomKinds.Condition, "作为军团且即将离场", "condition", new() { ["expression"] = "source.is-master-legion;source.would-leave-field" }),
            new(L12AtomKinds.MoveZone, "代替离场并返回主宰区", "replacement", new() { ["operation"] = "replace-leave-with-return-master-zone" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> AtalantaAbilities() => Assisted(
    [
        new("promotion", "summon-flow", "晋升 消耗并翻转1神力，叠放至我方同名非【晋升者】军团上方登场。",
        [
            new(L12AtomKinds.Condition, "存在我方同名非【晋升者】军团", "condition", new() { ["expression"] = "controller.same-name-non-promoted-foundation" }),
            new(L12AtomKinds.Special, "消耗并翻转 1 神力", "cost", new() { ["domain"] = "god-power", ["amount"] = "1", ["operation"] = "consume-and-flip" }),
            new(L12AtomKinds.SelectTarget, "选择同名晋升基底", "target", new() { ["zone"] = "controller.field", ["filter"] = "same-name-non-promoted", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.MoveZone, "叠放至基底上方晋升登场", "resolution", new() { ["operation"] = "promotion-enter", ["inheritState"] = "true" }),
        ]),
        new("promotion-enter", "triggered", "晋升登场 可抽取1张牌。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Draw, "抽取 1 张牌", "resolution", new() { ["amount"] = "1" }),
        ]),
        new("enter", "triggered", "登场时 可抽取1张牌。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Draw, "抽取 1 张牌", "resolution", new() { ["amount"] = "1" }),
        ]),
        new("static", "continuous", "「位于后排」此军团视为【弓手】，进攻距离+1，远程进攻无损。",
        [
            new(L12AtomKinds.Condition, "位于后排", "condition", new() { ["expression"] = "source.row=back" }),
            new(L12AtomKinds.SetState, "职介视为【弓手】", "resolution", new() { ["key"] = "source.derived-profession", ["value"] = "弓手" }),
            new(L12AtomKinds.AttackRule, "进攻距离 +1", "resolution", new() { ["rangeBonus"] = "1" }),
            new(L12AtomKinds.AttackRule, "远程进攻无损", "resolution", new() { ["rangedNoLoss"] = "true" }),
            new(L12AtomKinds.Duration, "位于后排期间持续", "duration", new() { ["duration"] = "while-source-row-back" }),
        ]),
        new("attack", "triggered", "「位于后排」进攻时 此军团兵力视为3000。",
        [
            new(L12AtomKinds.Condition, "位于后排", "condition", new() { ["expression"] = "source.row=back" }),
            new(L12AtomKinds.ModifyTroops, "本次进攻兵力视为 3000", "resolution", new() { ["operation"] = "set", ["value"] = "3000" }),
            new(L12AtomKinds.Duration, "仅本次进攻", "duration", new() { ["duration"] = "current-attack" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> HeraclesPromotedAbilities() => Assisted(
    [
        PromotionAbility(2),
        new("promotion-enter", "triggered", "晋升登场 可展示手牌中1张军团并将其放回牌库顶部：击杀对方1张费用不高于展示军团其费用的军团。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.SelectTarget, "选择手牌中的 1 张军团", "target", new() { ["zone"] = "controller.hand", ["filter"] = "card-type=legion", ["min"] = "1", ["max"] = "1" }),
            PublicReveal("公开展示所选军团"),
            new(L12AtomKinds.MoveZone, "将展示军团放回牌库顶部", "cost", new() { ["from"] = "controller.hand", ["to"] = "controller.library-top", ["reason"] = "promotion-enter-cost" }),
            new(L12AtomKinds.SelectTarget, "选择对方费用不高于展示军团费用的军团", "target", new() { ["zone"] = "opponent.field", ["filter"] = "card-type=legion;cost<=revealed-card.cost", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.MoveZone, "击杀所选军团", "resolution", new() { ["from"] = "opponent.field", ["to"] = "owner.graveyard", ["reason"] = "effect-kill" }),
        ]),
        new("enter", "triggered", "登场时 可对双方主宰各造成1点非致命伤害。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.DamageMaster, "双方主宰各受到 1 点非致命伤害", "resolution", new() { ["amount"] = "1", ["target"] = "both", ["lethal"] = "false" }),
        ]),
        new("attack", "triggered", "进攻时 本回合获得强攻。",
        [
            new(L12AtomKinds.Keyword, "获得【强攻】", "resolution", new() { ["keywordRef"] = "strong-attack", ["grantedAbility"] = "keyword.strong-attack" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> HeraclesAbilities() => Confirmed(
    [
        new("enter", "triggered", "登场时 可抽取2张牌，并弃置1张手牌。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Draw, "抽取 2 张牌", "resolution", new() { ["amount"] = "2" }),
            new(L12AtomKinds.SelectTarget, "选择 1 张手牌", "target", new() { ["zone"] = "controller.hand", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.Discard, "弃置所选手牌", "resolution", new() { ["amount"] = "1", ["reason"] = "effect-discard" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> AchillesPromotedAbilities() => Assisted(
    [
        PromotionAbility(2),
        new("static", "continuous", "进攻无损，此军团受到远程进攻兵力额外-1000。",
        [
            new(L12AtomKinds.AttackRule, "进攻无损", "resolution", new() { ["attackNoLoss"] = "true" }),
            new(L12AtomKinds.AttackRule, "受到远程进攻时额外承受1000战斗伤害", "resolution", new() { ["incomingRangedCombatDamageAdjustment"] = "1000" }),
            new(L12AtomKinds.Duration, "位于战场期间持续", "duration", new() { ["duration"] = "while-on-field" }),
        ]),
        new("promotion-enter", "triggered", "晋升登场 本回合可进攻对方军团。",
        [
            new(L12AtomKinds.AttackRule, "本回合可进攻对方军团", "resolution", new() { ["canAttackOpponentLegion"] = "true" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
        new("after-attack", "triggered", "击杀时 直到我方下个回合结束前，此军团获得 ABILITY 5。",
        [
            new(L12AtomKinds.Condition, "本次进攻击杀对象", "condition", new() { ["expression"] = "item.killed=true" }),
            new(L12AtomKinds.SetState, "获得 ABILITY 5", "resolution", new() { ["abilityRef"] = "S02-0503:ability:5", ["value"] = "true" }),
            new(L12AtomKinds.Duration, "直到我方下个回合结束前", "duration", new() { ["duration"] = "until-controller-next-turn-end" }),
        ]),
        new("granted-static", "granted-continuous", "「位于前排」获得 ABILITY 6。",
        [
            new(L12AtomKinds.Condition, "位于前排且已获得 ABILITY 5", "condition", new() { ["expression"] = "source.row=front;source.has-ability=S02-0503:ability:5" }),
            new(L12AtomKinds.SetState, "启用 ABILITY 6", "resolution", new() { ["abilityRef"] = "S02-0503:ability:6", ["value"] = "true" }),
        ]),
        new("keyword-definition", "keyword-definition", "挑衅 对方只可进攻拥有挑衅效果的军团，若有多个具有挑衅效果的军团，则可以选择其中1个进行进攻。",
        [
            new(L12AtomKinds.Keyword, "【挑衅】规则引用", "resolution", new() { ["keywordRef"] = "taunt", ["targetRule"] = "opponent-must-attack-taunt-legion" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> CooperativeSupportAbilities() =>
    [
        RangedAbility(),
        new("continuous", "continuous", "「位于后排」获得协防。（可支援我方任意前排军团，可联合支援）",
        [
            new(L12AtomKinds.Condition, "位于后排", "condition", new() { ["expression"] = "source.row=back" }),
            new(L12AtomKinds.Keyword, "获得【协防】", "resolution", new()
            {
                ["keywordRef"] = "cooperative-support",
                ["supportRule"] = "any-friendly-front;allow-multiple",
            }),
        ]),
    ];

    private static IReadOnlyList<L12StructuredAbilityTemplate> AchillesAbilities() => Assisted(
    [
        new("static", "continuous", "「位于前排」我方主宰无法被兵力不高于2000的军团进攻。",
        [
            new(L12AtomKinds.Condition, "位于前排", "condition", new() { ["expression"] = "source.row=front" }),
            new(L12AtomKinds.AttackRule, "保护我方主宰免受兵力不高于 2000 的军团进攻", "resolution", new() { ["protectMasterFromTroopsAtMost"] = "2000" }),
            new(L12AtomKinds.Duration, "位于前排期间持续", "duration", new() { ["duration"] = "while-source-row-front" }),
        ]),
        new("lethal-replacement", "replacement", "「位于前排」回合1次 即将阵亡时，可消耗并翻转1神力：代替承受本次致命进攻或效果。",
        [
            new(L12AtomKinds.Condition, "位于前排、即将阵亡且本回合未发动", "condition", new() { ["expression"] = "source.row=front;source.would-die=true;source.once-per-turn-unused=true" }),
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Special, "消耗并翻转 1 神力", "cost", new() { ["domain"] = "god-power", ["amount"] = "1", ["operation"] = "consume-and-flip" }),
            new(L12AtomKinds.SetState, "代替承受致命结果并保持致命时刻状态", "resolution", new() { ["operation"] = "replace-lethal-result", ["preserveTroops"] = "true", ["preserveTappedState"] = "true" }),
            new(L12AtomKinds.Duration, "回合 1 次", "duration", new() { ["duration"] = "once-per-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> PerseusPromotedAbilities() => Assisted(
    [
        PromotionAbility(1),
        new("promotion-enter", "triggered", "晋升登场 可选择对方1张休整的军团，使其在下个对方重置阶段无法转为活跃。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.SelectTarget, "选择对方 1 张休整的军团", "target", new() { ["zone"] = "opponent.field", ["filter"] = "card-type=legion;tapped=true", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.SetState, "下个对方重置阶段无法转为活跃", "resolution", new() { ["key"] = "target.skip-next-reset-ready", ["value"] = "true" }),
            new(L12AtomKinds.Duration, "持续至下个对方重置阶段", "duration", new() { ["duration"] = "until-opponent-next-reset" }),
        ]),
        new("active", "activated", "我方 回合1次 可进行1次位移。",
        [
            new(L12AtomKinds.Condition, "我方回合、此军团活跃且本回合未发动", "condition", new() { ["expression"] = "controller.turn;source.ready=true;source.once-per-turn-unused=true" }),
            new(L12AtomKinds.Move, "进行 1 次骑兵位移", "resolution", new() { ["operation"] = "cavalry-move", ["amount"] = "1" }),
            new(L12AtomKinds.Duration, "回合 1 次", "duration", new() { ["duration"] = "once-per-turn" }),
        ]),
        new("enter", "triggered", "登场时 获得 ABILITY 5。",
        [
            new(L12AtomKinds.SetState, "获得 ABILITY 5", "resolution", new() { ["abilityRef"] = "S02-0505:ability:5", ["value"] = "true" }),
        ]),
        new("keyword-definition", "keyword-definition", "冲锋 在登场的回合即可进行进攻。",
        [
            new(L12AtomKinds.Keyword, "【冲锋】规则引用", "resolution", new() { ["keywordRef"] = "charge", ["attackRule"] = "ignore-summoning-sickness" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> PerseusAbilities() => Confirmed(
    [
        new("enter", "triggered", "登场时 可弃置1张手牌：将墓地1张<珀尔修斯·晋升>加入手牌。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.SelectTarget, "选择 1 张手牌", "target", new() { ["zone"] = "controller.hand", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.Discard, "弃置所选手牌", "cost", new() { ["amount"] = "1", ["reason"] = "ability-cost" }),
            new(L12AtomKinds.SelectTarget, "选择墓地中的〈珀尔修斯·晋升〉", "target", new() { ["zone"] = "controller.graveyard", ["filter"] = "card-id=S02-0505", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.MoveZone, "将所选卡牌加入手牌", "resolution", new() { ["from"] = "controller.graveyard", ["to"] = "controller.hand" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> AtalantaBaseAbilities() => Assisted(
    [
        new("static", "continuous", "进攻距离+1，远程进攻无损。",
        [
            new(L12AtomKinds.AttackRule, "进攻距离 +1", "resolution", new() { ["rangeBonus"] = "1", ["professionAssignment"] = "弓手" }),
            new(L12AtomKinds.AttackRule, "远程进攻无损", "resolution", new() { ["rangedNoLoss"] = "true" }),
            new(L12AtomKinds.Duration, "位于战场期间持续", "duration", new() { ["duration"] = "while-on-field" }),
        ]),
        new("death", "triggered", "阵亡时 翻转1张士气。",
        [
            new(L12AtomKinds.Special, "翻转 1 张士气", "resolution", new() { ["domain"] = "morale", ["operation"] = "flip", ["amount"] = "1" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> OdysseusAbilities() => Assisted(
    [
        new("static", "continuous", "「位于手牌」若我方神力为0张，此军团登场费用-1。",
        [
            new(L12AtomKinds.Condition, "位于手牌且我方神力为 0 张", "condition", new() { ["expression"] = "source.zone=hand;controller.god-power=0" }),
            new(L12AtomKinds.SetState, "登场费用 -1", "resolution", new() { ["key"] = "source.derived-cost", ["operation"] = "add", ["value"] = "-1" }),
            new(L12AtomKinds.Duration, "满足条件期间持续", "duration", new() { ["duration"] = "while-condition-true" }),
        ]),
        new("enter", "triggered", "登场时 本回合从手牌中打出的下1张战术卡无需消耗费用。",
        [
            new(L12AtomKinds.SetState, "下 1 张手牌战术无需消耗费用", "resolution", new() { ["key"] = "controller.next-hand-tactic-free", ["value"] = "1" }),
            new(L12AtomKinds.Duration, "使用后或本回合结束时失效", "duration", new() { ["duration"] = "next-use-or-turn-end" }),
        ]),
        new("attack", "triggered", "进攻时 可展示手牌中的1张战术卡：此军团本回合兵力+1000。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.SelectTarget, "选择手牌中的 1 张战术卡", "target", new() { ["zone"] = "controller.hand", ["filter"] = "card-type=tactic", ["min"] = "1", ["max"] = "1" }),
            PublicReveal("公开展示所选战术"),
            new(L12AtomKinds.ModifyTroops, "此军团兵力 +1000", "resolution", new() { ["operation"] = "add", ["value"] = "1000" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> HippolytaAbilities() => Assisted(
    [
        new("static", "continuous", "「位于手牌」若我方神力为5张及以上，此军团登场费用-3。",
        [
            new(L12AtomKinds.Condition, "位于手牌且我方神力为 5 张及以上", "condition", new() { ["expression"] = "source.zone=hand;controller.god-power>=5" }),
            new(L12AtomKinds.SetState, "登场费用 -3", "resolution", new() { ["key"] = "source.derived-cost", ["operation"] = "add", ["value"] = "-3" }),
            new(L12AtomKinds.Duration, "满足条件期间持续", "duration", new() { ["duration"] = "while-condition-true" }),
        ]),
        new("static", "continuous", "此军团休整时，我方军团前后位移无需消耗费用。",
        [
            new(L12AtomKinds.Condition, "此军团位于战场且处于休整", "condition", new() { ["expression"] = "source.zone=field;source.tapped=true" }),
            new(L12AtomKinds.Move, "为符合通常位移条件的军团启用【免费位移】", "resolution", new() { ["operation"] = "enable-free-front-back-move", ["button"] = "免费位移", ["requiresReady"] = "true", ["direction"] = "front-back" }),
            new(L12AtomKinds.Duration, "此军团休整期间持续", "duration", new() { ["duration"] = "while-source-tapped-on-field" }),
        ]),
        new("active", "activated", "主动休整 消耗3士气并弃置1张手牌：选择墓地1张费用不高于4的【奥林匹斯】军团活跃登场。",
        [
            new(L12AtomKinds.Condition, "我方回合且此军团活跃", "condition", new() { ["expression"] = "controller.turn;source.ready=true" }),
            new(L12AtomKinds.RestSource, "将此军团转为休整", "cost", new()),
            new(L12AtomKinds.PayMorale, "支付 3 士气", "cost", new() { ["amount"] = "3" }),
            new(L12AtomKinds.SelectTarget, "选择 1 张手牌", "target", new() { ["zone"] = "controller.hand", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.Discard, "弃置所选手牌", "cost", new() { ["amount"] = "1", ["reason"] = "ability-cost" }),
            new(L12AtomKinds.SelectTarget, "选择墓地中费用不高于 4 的【奥林匹斯】军团", "target", new() { ["zone"] = "controller.graveyard", ["filter"] = "card-type=legion;faction=olympus;cost<=4", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.SelectTarget, "选择我方战场空位", "target", new() { ["zone"] = "controller.field-empty-slot", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.MoveZone, "所选军团活跃登场", "resolution", new() { ["from"] = "controller.graveyard", ["to"] = "controller.field", ["tapped"] = "false" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> ProloteAbilities() => Assisted(
    [
        new("enter", "triggered", "登场时 本回合可进攻对方军团。",
        [
            new(L12AtomKinds.AttackRule, "本回合可进攻对方军团", "resolution", new() { ["canAttackOpponentLegion"] = "true" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
        new("attack", "triggered", "进攻时 若目标为对方军团，可消耗并翻转1神力：本回合兵力+1000，并获得 ABILITY 3。",
        [
            new(L12AtomKinds.Condition, "进攻目标为对方军团", "condition", new() { ["expression"] = "attack.target=opponent-legion" }),
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Special, "消耗并翻转 1 神力", "cost", new() { ["domain"] = "god-power", ["amount"] = "1", ["operation"] = "consume-and-flip" }),
            new(L12AtomKinds.ModifyTroops, "此军团兵力 +1000", "resolution", new() { ["operation"] = "add", ["value"] = "1000" }),
            new(L12AtomKinds.SetState, "获得 ABILITY 3", "resolution", new() { ["abilityRef"] = "S02-0511:ability:3", ["value"] = "true" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
        new("keyword-definition", "keyword-definition", "震击 进攻时，被进攻者左右相邻的军团在本回合中兵力-2000。",
        [
            new(L12AtomKinds.Keyword, "【震击】规则引用", "resolution", new() { ["keywordRef"] = "shock", ["adjacentTroopsDelta"] = "-2000" }),
            new(L12AtomKinds.Duration, "相邻军团兵力修正持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> AeneasAbilities() => Assisted(
    [
        new("static", "continuous", "「位于手牌」若我方神力为0张，此军团登场费用-1。",
        [
            new(L12AtomKinds.Condition, "位于手牌且我方神力为 0 张", "condition", new() { ["expression"] = "source.zone=hand;controller.god-power=0" }),
            new(L12AtomKinds.SetState, "登场费用 -1", "resolution", new() { ["key"] = "source.derived-cost", ["operation"] = "add", ["value"] = "-1" }),
            new(L12AtomKinds.Duration, "满足条件期间持续", "duration", new() { ["duration"] = "while-condition-true" }),
        ]),
        new("static", "continuous", "「位于前排」获得 ABILITY 3。",
        [
            new(L12AtomKinds.Condition, "位于前排", "condition", new() { ["expression"] = "source.row=front" }),
            new(L12AtomKinds.SetState, "启用 ABILITY 3", "resolution", new() { ["abilityRef"] = "S02-0512:ability:3", ["value"] = "true" }),
        ]),
        new("keyword-definition", "keyword-definition", "挑衅 对方只可进攻拥有挑衅效果的军团，若有多个具有挑衅效果的军团，则可以选择其中1个进行进攻。",
        [
            new(L12AtomKinds.Keyword, "【挑衅】规则引用", "resolution", new() { ["keywordRef"] = "taunt", ["targetRule"] = "opponent-must-attack-taunt-legion" }),
        ]),
        new("death", "triggered", "阵亡时 可抽取1张牌。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Draw, "抽取 1 张牌", "resolution", new() { ["amount"] = "1" }),
        ]),
    ]);

    private static L12StructuredAbilityTemplate RangedAbility(bool frontOnly = false) =>
        new("static", "continuous", frontOnly ? "「位于前排」进攻距离+1，远程进攻无损。" : "进攻距离+1，远程进攻无损。",
        frontOnly
            ?
            [
                new(L12AtomKinds.Condition, "位于前排", "condition", new() { ["expression"] = "source.row=front" }),
                new(L12AtomKinds.AttackRule, "进攻距离 +1", "resolution", new() { ["rangeBonus"] = "1" }),
                new(L12AtomKinds.AttackRule, "远程进攻无损", "resolution", new() { ["rangedNoLoss"] = "true" }),
                new(L12AtomKinds.Duration, "位于前排期间持续", "duration", new() { ["duration"] = "while-source-row-front" }),
            ]
            :
            [
                new(L12AtomKinds.AttackRule, "进攻距离 +1", "resolution", new() { ["rangeBonus"] = "1" }),
                new(L12AtomKinds.AttackRule, "远程进攻无损", "resolution", new() { ["rangedNoLoss"] = "true" }),
                new(L12AtomKinds.Duration, "位于战场期间持续", "duration", new() { ["duration"] = "while-on-field" }),
            ]);

    private static L12StructuredAbilityTemplate FrontRowTauntAbility() =>
        new("static", "continuous", "「位于前排」获得【挑衅】。",
        [
            new(L12AtomKinds.Condition, "位于前排", "condition", new() { ["expression"] = "source.row=front" }),
            new(L12AtomKinds.Keyword, "【挑衅】规则引用", "resolution", new()
            {
                ["keywordRef"] = "taunt",
                ["targetRule"] = "opponent-must-attack-taunt-legion",
            }),
            new(L12AtomKinds.Duration, "位于前排期间持续", "duration", new() { ["duration"] = "while-source-row-front" }),
        ]);

    private static L12StructuredAbilityTemplate FrontRowMasterProtectionAbility() =>
        new("static", "continuous", "「位于前排」我方主宰无法被兵力不高于2000的军团进攻。",
        [
            new(L12AtomKinds.Condition, "位于前排", "condition", new() { ["expression"] = "source.row=front" }),
            new(L12AtomKinds.AttackRule, "保护我方主宰免受兵力不高于 2000 的军团进攻", "resolution", new()
            {
                ["protectMasterFromTroopsAtMost"] = "2000",
            }),
            new(L12AtomKinds.Duration, "位于前排期间持续", "duration", new() { ["duration"] = "while-source-row-front" }),
        ]);

    private static L12StructuredAbilityTemplate AttackNoLossAbility() =>
        new("static", "continuous", "进攻无损。",
        [
            new(L12AtomKinds.AttackRule, "进攻无损", "resolution", new() { ["attackNoLoss"] = "true" }),
            new(L12AtomKinds.Duration, "位于战场期间持续", "duration", new() { ["duration"] = "while-on-field" }),
        ]);

    private static L12StructuredAbilityTemplate CannotBeRangedAbility() =>
        new("static", "continuous", "无法被远程进攻。",
        [
            new(L12AtomKinds.AttackRule, "无法被远程进攻", "resolution", new() { ["cannotBeRanged"] = "true" }),
            new(L12AtomKinds.Duration, "位于战场期间持续", "duration", new() { ["duration"] = "while-on-field" }),
        ]);

    private static L12StructuredAtomTemplate GodPowerCost(int amount, bool flip) =>
        new(L12AtomKinds.Special, flip ? $"消耗并翻转 {amount} 神力" : $"消耗 {amount} 神力", "cost", new()
        {
            ["domain"] = "god-power",
            ["amount"] = amount.ToString(),
            ["operation"] = flip ? "consume-and-flip" : "consume",
        });

    private static IReadOnlyList<L12StructuredAbilityTemplate> AristotleAbilities() => Assisted(
    [
        RangedAbility(),
        new("enter", "triggered", "登场时 可翻转1张士气。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Special, "翻转 1 张士气", "resolution", new() { ["domain"] = "morale", ["operation"] = "flip", ["amount"] = "1" }),
        ]),
        new("active", "activated", "主动休整 本回合我方下1张【奥林匹斯】军团登场费用-1。",
        [
            new(L12AtomKinds.Condition, "我方回合且来源活跃", "condition", new() { ["expression"] = "controller.turn;source.ready=true" }),
            new(L12AtomKinds.RestSource, "将此军团转为休整", "cost", new()),
            new(L12AtomKinds.SetState, "本回合下一张【奥林匹斯】军团登场费用 -1", "resolution", new() { ["key"] = "controller.next-olympus-legion-cost", ["operation"] = "add", ["value"] = "-1", ["uses"] = "1" }),
            new(L12AtomKinds.Duration, "持续至本回合结束或被下一张符合卡牌消耗", "duration", new() { ["duration"] = "this-turn-or-next-use" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> PlatoAbilities() => Assisted(
    [
        RangedAbility(),
        new("enter", "triggered", "登场时 可查看牌库顶部3张牌，选择其中1张<柏拉图>以外的【奥林匹斯】卡牌，展示并加入手牌，其余卡牌自选顺序返回牌库底部。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Special, "查看牌库顶部 3 张牌", "resolution", new() { ["operation"] = "inspect-library-top", ["amount"] = "3", ["visibility"] = "controller-only" }),
            new(L12AtomKinds.SelectTarget, "选择其中 1 张〈柏拉图〉以外的【奥林匹斯】卡牌", "target", new() { ["zone"] = "controller.inspected-library", ["filter"] = "faction=olympus;card-id!=S02-0514", ["min"] = "1", ["max"] = "1" }),
            PublicReveal("公开展示所选卡牌"),
            new(L12AtomKinds.MoveZone, "将所选卡牌加入手牌", "resolution", new() { ["from"] = "controller.inspected-library", ["to"] = "controller.hand" }),
            new(L12AtomKinds.SelectTarget, "自选其余卡牌顺序", "target", new() { ["zone"] = "controller.inspected-library", ["operation"] = "reorder-all", ["destination"] = "controller.library-bottom" }),
            new(L12AtomKinds.MoveZone, "其余卡牌按所选顺序返回牌库底部", "resolution", new() { ["from"] = "controller.inspected-library", ["to"] = "controller.library-bottom", ["order"] = "player-selected" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> HelenAbilities() => Assisted(
    [
        RangedAbility(),
        new("enter", "triggered", "登场时 若我方神力为1张及以上，对方弃置1张手牌。",
        [
            new(L12AtomKinds.Condition, "我方神力为 1 张及以上", "condition", new() { ["expression"] = "controller.god-power>=1" }),
            new(L12AtomKinds.SelectTarget, "对方选择 1 张手牌", "target", new() { ["zone"] = "opponent.hand", ["min"] = "1", ["max"] = "1", ["selectionController"] = "opponent" }),
            new(L12AtomKinds.Discard, "对方弃置所选手牌", "resolution", new() { ["reason"] = "effect-discard", ["ownerZone"] = "graveyard" }),
        ]),
        new("lethal-replacement", "replacement", "「位于前排」回合1次 即将阵亡时，可弃置手牌中的1张<海伦>以外的军团卡代替承受本次致命进攻或效果。",
        [
            new(L12AtomKinds.Condition, "位于前排、即将阵亡且本回合未发动", "condition", new() { ["expression"] = "source.row=front;source.would-die=true;source.once-per-turn-unused=true" }),
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.SelectTarget, "选择手牌中 1 张〈海伦〉以外的军团", "target", new() { ["zone"] = "controller.hand", ["filter"] = "card-type=legion;card-id!=S02-0515", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.Discard, "弃置所选军团", "cost", new() { ["reason"] = "ability-cost" }),
            new(L12AtomKinds.SetState, "代替承受致命结果并保持致命时刻状态", "resolution", new() { ["operation"] = "replace-lethal-result", ["preserveTroops"] = "true", ["preserveTappedState"] = "true" }),
            new(L12AtomKinds.Duration, "回合 1 次", "duration", new() { ["duration"] = "once-per-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> HannibalAbilities() => Assisted(
    [
        new("static", "continuous", "此军团活跃时不可被进攻。",
        [
            new(L12AtomKinds.Condition, "此军团处于活跃", "condition", new() { ["expression"] = "source.ready=true" }),
            new(L12AtomKinds.AttackRule, "此军团不可成为进攻目标", "resolution", new() { ["targetableByAttack"] = "false" }),
            new(L12AtomKinds.Duration, "活跃期间持续", "duration", new() { ["duration"] = "while-source-ready" }),
        ]),
        new("static", "continuous", "此军团左右相邻军团兵力+1000。",
        [
            new(L12AtomKinds.ModifyTroops, "左右相邻军团兵力 +1000", "resolution", new() { ["target"] = "controller.adjacent-legions", ["operation"] = "add", ["value"] = "1000" }),
            new(L12AtomKinds.Duration, "此军团位于战场期间持续", "duration", new() { ["duration"] = "while-source-on-field" }),
        ]),
        new("attack", "triggered", "进攻时 可消耗1神力：选择双方各1张军团，本回合兵力-2000。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            GodPowerCost(1, false),
            new(L12AtomKinds.SelectTarget, "选择我方 1 张军团", "target", new() { ["zone"] = "controller.field", ["filter"] = "card-type=legion", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.SelectTarget, "选择对方 1 张军团", "target", new() { ["zone"] = "opponent.field", ["filter"] = "card-type=legion", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.ModifyTroops, "所选双方军团兵力各 -2000", "resolution", new() { ["target"] = "declared-both-legions", ["operation"] = "add", ["value"] = "-2000" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> PenthesileaAbilities() => Assisted(
    [
        RangedAbility(true),
        new("enter", "triggered", "登场时 本回合可进攻对方军团。",
        [
            new(L12AtomKinds.AttackRule, "本回合可进攻对方军团", "resolution", new() { ["canAttackOpponentLegion"] = "true" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
        new("attack", "triggered", "进攻时 可消耗并翻转1神力：本回合兵力+2000。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            GodPowerCost(1, true),
            new(L12AtomKinds.ModifyTroops, "此军团兵力 +2000", "resolution", new() { ["operation"] = "add", ["value"] = "2000" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> SpartanWarriorAbilities() => Assisted(
    [
        new("attack", "triggered", "进攻时 可消耗并翻转1神力：此军团本回合兵力+2000。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            GodPowerCost(1, true),
            new(L12AtomKinds.ModifyTroops, "此军团兵力 +2000", "resolution", new() { ["operation"] = "add", ["value"] = "2000" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
        new("static", "continuous", "对方回合 此军团兵力+2000。",
        [
            new(L12AtomKinds.Condition, "当前为对方回合", "condition", new() { ["expression"] = "opponent.turn" }),
            new(L12AtomKinds.ModifyTroops, "此军团兵力 +2000", "resolution", new() { ["operation"] = "add", ["value"] = "2000" }),
            new(L12AtomKinds.Duration, "对方回合期间持续", "duration", new() { ["duration"] = "while-opponent-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> ForgeAbilities() => Assisted(
    [
        new("enter", "triggered", "登场时 可翻转1张士气。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Special, "翻转 1 张士气", "resolution", new() { ["domain"] = "morale", ["operation"] = "flip", ["amount"] = "1" }),
        ]),
        new("active", "activated", "主动休整 消耗1士气：选择 ABILITY 3 或 ABILITY 4。",
        [
            new(L12AtomKinds.Condition, "我方回合且来源活跃", "condition", new() { ["expression"] = "controller.turn;source.ready=true" }),
            new(L12AtomKinds.RestSource, "将此圣物转为休整", "cost", new()),
            new(L12AtomKinds.PayMorale, "支付 1 士气", "cost", new() { ["amount"] = "1" }),
            new(L12AtomKinds.SelectMode, "选择一项效果", "target", new() { ["options"] = "S02-0520:ability:3|S02-0520:ability:4" }),
        ]),
        new("mode-promotion-discount", "granted-effect", "本回合我方下1张军团「晋升登场」消耗并翻转的神力-1。",
        [
            new(L12AtomKinds.SetState, "下一次晋升登场消耗并翻转的神力 -1", "resolution", new() { ["key"] = "controller.next-promotion-god-power-cost", ["operation"] = "add", ["value"] = "-1", ["uses"] = "1" }),
            new(L12AtomKinds.Duration, "持续至本回合结束或被下一次晋升登场消耗", "duration", new() { ["duration"] = "this-turn-or-next-use" }),
        ]),
        new("mode-ready-after-kill", "granted-effect", "选择我方1张【晋升者】以外的【奥林匹斯】军团，在本回合其下一次击杀对方军团后转为活跃。",
        [
            new(L12AtomKinds.SelectTarget, "选择我方 1 张【晋升者】以外的【奥林匹斯】军团", "target", new() { ["zone"] = "controller.field", ["filter"] = "card-type=legion;faction=olympus;trait!=晋升者", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.SetState, "下一次击杀对方军团后转为活跃", "resolution", new() { ["key"] = "target.ready-after-next-kill", ["value"] = "true", ["uses"] = "1" }),
            new(L12AtomKinds.Duration, "持续至本回合结束或下一次击杀", "duration", new() { ["duration"] = "this-turn-or-next-kill" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> GloryRoadAbilities() => Confirmed(
    [
        new("play", "spell-resolution", "翻转最多3张士气。",
        [
            new(L12AtomKinds.SelectTarget, "选择 0 至 3 张士气", "target", new() { ["zone"] = "controller.morale", ["min"] = "0", ["max"] = "3" }),
            new(L12AtomKinds.Special, "翻转所选士气", "resolution", new() { ["domain"] = "morale", ["operation"] = "flip-selected" }),
        ]),
        new("play-additional", "additional-resolution", "可消耗并翻转2神力：查看我方牌库，选择1张【奥林匹斯】卡牌展示并加入手牌，随后重洗牌库。",
        [
            new(L12AtomKinds.Optional, "可发动额外效果", "condition", new()),
            GodPowerCost(2, true),
            new(L12AtomKinds.Special, "查看我方牌库", "resolution", new() { ["operation"] = "inspect-library", ["visibility"] = "controller-only" }),
            new(L12AtomKinds.SelectTarget, "选择牌库中 1 张【奥林匹斯】卡牌", "target", new() { ["zone"] = "controller.library", ["filter"] = "faction=olympus", ["min"] = "1", ["max"] = "1" }),
            PublicReveal("公开展示所选卡牌"),
            new(L12AtomKinds.MoveZone, "将所选卡牌加入手牌", "resolution", new() { ["from"] = "controller.library", ["to"] = "controller.hand" }),
            new(L12AtomKinds.Shuffle, "重洗牌库", "resolution", new() { ["zone"] = "controller.library" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> NyxMeteorAbilities() => Assisted(
    [
        new("play", "spell-resolution", "选择对方1张军团，本回合兵力-3000。",
        [
            new(L12AtomKinds.SelectTarget, "选择对方 1 张军团", "target", new() { ["zone"] = "opponent.field", ["filter"] = "card-type=legion", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.ModifyTroops, "所选军团兵力 -3000", "resolution", new() { ["operation"] = "add", ["value"] = "-3000" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
        new("play-additional", "additional-resolution", "可消耗并翻转1神力：选择对方1张军团，本回合兵力-2000。",
        [
            new(L12AtomKinds.Optional, "可发动额外效果", "condition", new()),
            GodPowerCost(1, true),
            new(L12AtomKinds.SelectTarget, "选择对方 1 张军团", "target", new() { ["zone"] = "opponent.field", ["filter"] = "card-type=legion", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.ModifyTroops, "所选军团兵力 -2000", "resolution", new() { ["operation"] = "add", ["value"] = "-2000" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> TrojanHorseAbilities() => Assisted(
    [
        new("after-opponent-attack", "reaction", "对方 进攻后：可将此战术置入对方战场任意空位，直到下个我方回合结束。随后弃置此战术，抽取1张牌。",
        [
            new(L12AtomKinds.Condition, "对方完成一次进攻且对方战场有空位", "condition", new() { ["expression"] = "opponent.attack-ended;opponent.field.has-empty-slot=true" }),
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.SelectTarget, "选择对方战场任意空位", "target", new() { ["zone"] = "opponent.field-empty-slot", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.MoveZone, "将此战术置入所选空位", "resolution", new() { ["from"] = "controller.hand-or-covered", ["to"] = "opponent.field", ["as"] = "tactic" }),
            new(L12AtomKinds.Duration, "持续至下个我方回合结束", "duration", new() { ["duration"] = "until-controller-next-turn-end" }),
            new(L12AtomKinds.MoveZone, "期限结束后弃置此战术", "resolution", new() { ["from"] = "opponent.field", ["to"] = "owner.graveyard", ["reason"] = "effect-discard" }),
            new(L12AtomKinds.Draw, "抽取 1 张牌", "resolution", new() { ["amount"] = "1" }),
        ]),
        new("static", "continuous", "此战术在对方战场时：对方所有军团兵力-1000。",
        [
            new(L12AtomKinds.Condition, "此战术位于对方战场", "condition", new() { ["expression"] = "source.zone=opponent.field" }),
            new(L12AtomKinds.ModifyTroops, "对方所有军团兵力 -1000", "resolution", new() { ["target"] = "opponent.all-legions", ["operation"] = "add", ["value"] = "-1000" }),
            new(L12AtomKinds.Duration, "此战术位于对方战场期间持续", "duration", new() { ["duration"] = "while-source-on-opponent-field" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> ArtemisAbilities() => Assisted(
    [
        new("friendly-ranged-death", "triggered", "回合1次 我方远程军团阵亡时，可翻转1张休整的士气。",
        [
            new(L12AtomKinds.Condition, "我方远程军团阵亡且本回合未发动", "condition", new() { ["expression"] = "friendly.ranged-legion-died;source.once-per-turn-unused=true" }),
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Special, "翻转 1 张休整士气", "resolution", new() { ["domain"] = "morale", ["operation"] = "flip-rested", ["amount"] = "1" }),
            new(L12AtomKinds.Duration, "回合 1 次", "duration", new() { ["duration"] = "once-per-turn" }),
        ]),
        new("active", "activated", "我方 回合1次 可消耗并翻转1神力或弃置1张手牌：选择我方1张费用为3至6的【奥林匹斯】军团，本回合获得 ABILITY 3 或 ABILITY 4。",
        [
            new(L12AtomKinds.Condition, "我方回合且本回合未发动", "condition", new() { ["expression"] = "controller.turn;source.once-per-turn-unused=true" }),
            new(L12AtomKinds.SelectMode, "选择消耗并翻转 1 神力或弃置 1 张手牌", "cost", new() { ["options"] = "god-power|discard-hand" }),
            new(L12AtomKinds.SelectTarget, "选择我方 1 张费用为 3 至 6 的【奥林匹斯】军团", "target", new() { ["zone"] = "controller.field", ["filter"] = "card-type=legion;faction=olympus;cost>=3;cost<=6", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.SelectMode, "选择赋予强攻或震击", "target", new() { ["options"] = "S02-05M1:ability:3|S02-05M1:ability:4" }),
            new(L12AtomKinds.Duration, "持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
        new("keyword-definition", "keyword-definition", "强攻 此军团因进攻对主宰造成伤害时，额外再造成1点伤害。",
        [
            new(L12AtomKinds.Keyword, "【强攻】规则引用", "resolution", new() { ["keywordRef"] = "strong-attack", ["extraMasterDamage"] = "1" }),
        ]),
        new("keyword-definition", "keyword-definition", "震击 进攻时，被进攻者左右相邻的军团在本回合中兵力-2000。",
        [
            new(L12AtomKinds.Keyword, "【震击】规则引用", "resolution", new() { ["keywordRef"] = "shock", ["adjacentTroopsDelta"] = "-2000" }),
            new(L12AtomKinds.Duration, "相邻军团兵力修正持续至本回合结束", "duration", new() { ["duration"] = "this-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> PrometheusAbilities() => Confirmed(
    [
        new("active", "activated", "我方 回合1次 消耗1神力：查看牌库顶部3张牌，选择其中1张【奥林匹斯】卡牌，展示并加入手牌，其余卡牌自选顺序返回牌库顶部或底部。",
        [
            new(L12AtomKinds.Condition, "我方回合且本回合未发动", "condition", new() { ["expression"] = "controller.turn;source.once-per-turn-unused=true" }),
            GodPowerCost(1, false),
            new(L12AtomKinds.Special, "查看牌库顶部 3 张牌", "resolution", new() { ["operation"] = "inspect-library-top", ["amount"] = "3", ["visibility"] = "controller-only" }),
            new(L12AtomKinds.SelectTarget, "选择其中 1 张【奥林匹斯】卡牌", "target", new() { ["zone"] = "controller.inspected-library", ["filter"] = "faction=olympus", ["min"] = "1", ["max"] = "1" }),
            PublicReveal("公开展示所选卡牌"),
            new(L12AtomKinds.MoveZone, "将所选卡牌加入手牌", "resolution", new() { ["from"] = "controller.inspected-library", ["to"] = "controller.hand" }),
            new(L12AtomKinds.SelectTarget, "自选其余卡牌顺序", "target", new() { ["zone"] = "controller.inspected-library", ["operation"] = "reorder-all" }),
            new(L12AtomKinds.SelectMode, "选择其余卡牌全部返回牌库顶部或底部", "target", new() { ["options"] = "library-top|library-bottom" }),
            new(L12AtomKinds.MoveZone, "其余卡牌按所选顺序返回所选位置", "resolution", new() { ["from"] = "controller.inspected-library", ["to"] = "selected-library-end", ["order"] = "player-selected" }),
            new(L12AtomKinds.Duration, "回合 1 次", "duration", new() { ["duration"] = "once-per-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> GodPowerAbilities() => Confirmed(
    [
        new("static", "continuous", "规则上，此卡可视为1张士气。",
        [
            new(L12AtomKinds.SetState, "规则上视为 1 张士气", "resolution", new() { ["key"] = "source.counts-as-morale", ["value"] = "true" }),
            new(L12AtomKinds.Duration, "位于士气区期间持续", "duration", new() { ["duration"] = "while-in-morale-zone" }),
        ]),
        new("active", "activated", "我方 回合1次 可消耗并翻转1神力：抽取1张牌。",
        [
            new(L12AtomKinds.Condition, "我方回合且本回合未发动", "condition", new() { ["expression"] = "controller.turn;source.once-per-turn-unused=true" }),
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            GodPowerCost(1, true),
            new(L12AtomKinds.Draw, "抽取 1 张牌", "resolution", new() { ["amount"] = "1" }),
            new(L12AtomKinds.Duration, "回合 1 次", "duration", new() { ["duration"] = "once-per-turn" }),
        ]),
    ]);

    private static IReadOnlyList<L12StructuredAbilityTemplate> OlympusMoraleAbilities() => Confirmed(
    [
        new("active", "activated", "我方 回合1次 可消耗1士气：翻转1张士气。",
        [
            new(L12AtomKinds.Condition, "我方回合且本回合未发动", "condition", new() { ["expression"] = "controller.turn;source.once-per-turn-unused=true" }),
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.PayMorale, "支付 1 士气", "cost", new() { ["amount"] = "1" }),
            new(L12AtomKinds.SelectTarget, "选择 1 张士气", "target", new() { ["zone"] = "controller.morale", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.Special, "翻转所选士气", "resolution", new() { ["domain"] = "morale", ["operation"] = "flip-selected" }),
            new(L12AtomKinds.Duration, "回合 1 次", "duration", new() { ["duration"] = "once-per-turn" }),
        ]),
    ]);
    private static IReadOnlyList<L12StructuredAbilityTemplate> TheseusAbilities() => Assisted(
    [
        new("static", "continuous", "「位于手牌」若我方神力为0张，此军团登场费用-1。",
        [
            new(L12AtomKinds.Condition, "位于手牌且我方神力为 0 张", "condition", new() { ["expression"] = "source.zone=hand;controller.god-power=0" }),
            new(L12AtomKinds.SetState, "登场费用 -1", "resolution", new() { ["key"] = "source.derived-cost", ["operation"] = "add", ["value"] = "-1" }),
            new(L12AtomKinds.Duration, "满足条件期间持续", "duration", new() { ["duration"] = "while-condition-true" }),
        ]),
        new("enter", "triggered", "登场时 可翻转1张休整的士气。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Special, "翻转 1 张休整士气", "resolution", new() { ["domain"] = "morale", ["operation"] = "flip-rested", ["amount"] = "1" }),
        ]),
        new("death", "triggered", "阵亡时 可选择墓地1张【晋升者】军团加入手牌。",
        [
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.SelectTarget, "选择墓地中的 1 张【晋升者】军团", "target", new() { ["zone"] = "controller.graveyard", ["filter"] = "card-type=legion;trait=晋升者", ["min"] = "1", ["max"] = "1" }),
            PublicReveal("公开展示所选【晋升者】军团"),
            new(L12AtomKinds.MoveZone, "将所选卡牌加入手牌", "resolution", new() { ["from"] = "controller.graveyard", ["to"] = "controller.hand" }),
        ]),
    ]);

    private static L12StructuredAbilityTemplate PromotionAbility(int godPower) =>
        new("promotion", "summon-flow", $"晋升 消耗并翻转{godPower}神力，叠放至我方同名非【晋升者】军团上方登场。",
        [
            new(L12AtomKinds.Condition, "存在我方同名非【晋升者】军团", "condition", new() { ["expression"] = "controller.same-name-non-promoted-foundation" }),
            new(L12AtomKinds.Special, $"消耗并翻转 {godPower} 神力", "cost", new() { ["domain"] = "god-power", ["amount"] = godPower.ToString(), ["operation"] = "consume-and-flip" }),
            new(L12AtomKinds.SelectTarget, "选择同名晋升基底", "target", new() { ["zone"] = "controller.field", ["filter"] = "same-name-non-promoted", ["min"] = "1", ["max"] = "1" }),
            new(L12AtomKinds.MoveZone, "叠放至基底上方晋升登场", "resolution", new() { ["operation"] = "promotion-enter", ["inheritState"] = "true" }),
        ]);

    private static L12StructuredAtomTemplate PublicReveal(string label) =>
        new(L12AtomKinds.Visibility, label, "resolution", new()
        {
            ["visibility"] = "both-players",
            ["presentation"] = "battlefield-overlay-no-mask",
            ["durationMs"] = "3000",
            ["opponentConfirmation"] = "none",
            ["log"] = "public-card-link",
        });

    private static IReadOnlyList<L12StructuredAbilityTemplate> Assisted(IReadOnlyList<L12StructuredAbilityTemplate> abilities)
        => abilities.Select(ability => ability with { ReviewStatus = "human-assisted", ReviewSource = "user-20260823" }).ToArray();

    private static IReadOnlyList<L12StructuredAbilityTemplate> Confirmed(IReadOnlyList<L12StructuredAbilityTemplate> abilities)
        => abilities.Select(ability => ability with { ReviewStatus = "confirmed", ReviewSource = "user-20260823" }).ToArray();

    private static IReadOnlyList<L12StructuredAbilityTemplate> YoshitsuneAbilities() =>
    [
        new("static", "continuous", "「位于后排」进攻距离+1，远程进攻无损。",
        [
            new(L12AtomKinds.Condition, "位于后排", "condition", new() { ["expression"] = "source.row=back" }),
            new(L12AtomKinds.AttackRule, "进攻距离 +1", "resolution", new() { ["rangeBonus"] = "1" }),
            new(L12AtomKinds.AttackRule, "远程进攻无损", "resolution", new() { ["rangedNoLoss"] = "true" }),
            new(L12AtomKinds.Duration, "位于后排期间持续", "duration", new() { ["duration"] = "while-source-row-back" }),
        ]),
        new("attack", "triggered", "「位于后排」进攻时 此军团兵力视为2000。",
        [
            new(L12AtomKinds.Condition, "位于后排", "condition", new() { ["expression"] = "source.row=back" }),
            new(L12AtomKinds.ModifyTroops, "本次进攻兵力视为 2000", "resolution", new() { ["operation"] = "set", ["value"] = "2000" }),
            new(L12AtomKinds.Duration, "仅本次进攻", "duration", new() { ["duration"] = "current-attack" }),
        ]),
        new("active", "activated", "我方 回合1次 可进行1次位移。",
        [
            new(L12AtomKinds.Condition, "我方回合且本回合未发动", "condition", new() { ["expression"] = "controller.turn-and-once" }),
            new(L12AtomKinds.Move, "进行 1 次骑兵位移", "resolution", new() { ["operation"] = "cavalry-move", ["amount"] = "1" }),
            new(L12AtomKinds.Duration, "回合 1 次", "duration", new() { ["duration"] = "once-per-turn" }),
        ]),
        new("after-attack", "triggered", "击杀时 可抽取1张牌。",
        [
            new(L12AtomKinds.Condition, "本次进攻击杀对象", "condition", new() { ["expression"] = "item.killed=true" }),
            new(L12AtomKinds.Optional, "可发动", "condition", new()),
            new(L12AtomKinds.Draw, "抽取 1 张牌", "resolution", new() { ["amount"] = "1" }),
        ]),
    ];
}

public sealed record L12StructuredAbilityTemplate(
    string Trigger,
    string ExecutionModel,
    string Text,
    IReadOnlyList<L12StructuredAtomTemplate> Atoms,
    string ReviewStatus = "unreviewed",
    string ReviewSource = "automatic",
    bool RuntimeRouteOwner = true);

public sealed record L12StructuredAtomTemplate(
    string Kind,
    string Label,
    string Stage,
    Dictionary<string, string> Parameters);
