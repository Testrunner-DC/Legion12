namespace TwelveLegions.Server;

/// <summary>Batch 6J-A: legacy enter/promotion-enter effects declare public decisions before stacking.</summary>
public sealed partial class L12GameEngine
{
    private static readonly IReadOnlyDictionary<string, string> Batch6JAEnterPlans =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["S01-0101|enter"] = "lubu", ["S01-0102|enter"] = "wuzetian",
        ["S01-0103|enter"] = "lijing", ["S01-0108|enter"] = "mulan",
        ["S01-0110|enter"] = "mozi", ["S01-0111|enter"] = "zhuge",
        ["S01-0112|enter"] = "sunwu", ["S01-0201|enter"] = "thutmose",
        ["S01-0202|enter"] = "ramses", ["S01-0205|enter"] = "horemheb",
        ["S01-0210|enter"] = "nitocris", ["S01-0215|enter"] = "ankh",
        ["S01-0217|enter"] = "canopic-one", ["S01-0220|enter"] = "canopic-four",
        ["S01-0313|enter"] = "oddr", ["S01-0316|enter"] = "egil",
        ["S01-0317|enter"] = "gram", ["S01-0402|enter"] = "nobunaga",
        ["S01-0403|enter"] = "uesugi", ["S01-0406|enter"] = "hijikata",
        ["S01-0408|enter"] = "takasugi", ["S01-0411|enter"] = "abe",
        ["S01-0412|enter"] = "tachibana", ["S01-0416|enter"] = "inahime",
        ["S01-0417|enter"] = "kusanagi", ["S02-0003|enter"] = "court-magician",
        ["S02-0008|enter"] = "ring", ["S02-0204|enter"] = "imhotep",
        ["S02-0303|enter"] = "canute", ["S02-0401|enter"] = "takeda-search",
        ["S02-0401|enter-followup"] = "takeda-followup", ["S02-0402|enter"] = "ii-naotora",
        ["S02-0404|enter"] = "magatama-search", ["S02-0501|enter"] = "heracles-promoted-entry",
        ["S02-0501|promotion-enter"] = "heracles-promotion", ["S02-0502|enter"] = "heracles",
        ["S02-0505|promotion-enter"] = "perseus-promotion", ["S02-0506|enter"] = "perseus",
        ["S02-0513|enter"] = "morale-flip", ["S02-0518|enter"] = "theseus-flip",
        ["S02-0520|enter"] = "morale-flip-two", ["S02-0601|enter"] = "arthur",
        ["S02-0608|enter"] = "richard", ["S02-0613|enter"] = "joan",
        ["S02-0617|enter"] = "robin", ["S02-0619|enter"] = "claudia",
    };

    private static string? Batch6JAEnterPlan(string cardId, string trigger)
        => Batch6JAEnterPlans.GetValueOrDefault($"{cardId}|{trigger}");

    private bool PrepareBatch6JAEnterCandidate(L12TriggerCandidate candidate)
    {
        var plan = Batch6JAEnterPlan(candidate.SourceCardId, candidate.Trigger);
        if (plan is null) return true;
        candidate.Data["batch6JAConditionLocked"] = "true";
        // “可翻转1张士气”的唯一决定就是选择目标。目标选择界面本身提供不发动，
        // 不再先询问一次“是否发动”，并将目标选择延后到效果真正结算时。
        if (plan is "morale-flip" or "theseus-flip" or "morale-flip-two")
            candidate.Data["declaration-complete"] = "true";
        if (plan is "canopic-one" or "canopic-four"
            && !PublicLegions(State.Players[candidate.Controller]).Any(card =>
                L12StructuredCardRules.HasFaction(State.Players[candidate.Controller], card, "taiyangcheng")))
        {
            var compositePlan = $"trigger:{plan}:enter";
            candidate.Data["compositePlan"] = compositePlan;
            candidate.Data["compositeSegment"] = "1";
            candidate.Data["atomicFlow"] = plan == "canopic-one" ? "canopic-one-discard" : "canopic-four-discard";
            candidate.Data["atomicContinuation"] = "true";
            candidate.Data["stackText"] = plan == "canopic-one"
                ? "卡诺匹斯罐一：随后弃置此圣物" : "卡诺匹斯罐四：随后弃置此圣物";
            candidate.Data["declaration-complete"] = "true";
        }
        return true;
    }

    private bool TryBeginBatch6JAEnterDeclaration(L12TriggerCandidate candidate, L12CardInstance source)
    {
        var plan = Batch6JAEnterPlan(candidate.SourceCardId, candidate.Trigger);
        if (plan is null) return false;
        var steps = Batch6JAEnterSteps(candidate, source, plan);
        if (steps.Count == 0 || steps.Any(step => step.RequiredDeclaredChoice is null
                && step.ValidChoices.Count < step.MinChoose))
        {
            State.PendingTriggerStackCandidates.Remove(candidate);
            AddEvent("ability-cancelled", candidate.Controller,
                $"〈{candidate.SourceName}〉没有合法的公开声明对象，未生成空堆叠项", source);
            AdvanceTriggerBatches();
            return true;
        }
        var result = BeginPendingActivationSequence(candidate.Controller, source, candidate.Text, steps,
            candidate.CandidateId, null, null);
        if (!result.Accepted)
            RemoveUnstackedTriggerCandidate(candidate, result.Error ?? $"〈{candidate.SourceName}〉的公开声明无法建立");
        return true;
    }

    private List<L12ActivationSelectionStep> Batch6JAEnterSteps(L12TriggerCandidate candidate,
        L12CardInstance source, string plan)
    {
        var player = State.Players[candidate.Controller];
        var opponent = State.Players[1 - candidate.Controller];
        var own = PublicLegions(player).ToArray();
        var enemy = PublicLegions(opponent).ToArray();
        var steps = new List<L12ActivationSelectionStep>();
        void Optional(string text) => steps.Add(PublicTriggerStep("option", "mode", text,
            ["mode:none", "mode:use"]));
        void One(string kind, string key, string text, IEnumerable<string> choices, string? required = null)
            => steps.Add(PublicTriggerStep(kind, key, text, choices, 1, 1, requiredChoice: required));
        void Many(string kind, string key, string text, IEnumerable<string> choices, int min, int max,
            string? required = null, string? selectionConstraint = null)
            => steps.Add(PublicTriggerStep(kind, key, text, choices, min, max, requiredChoice: required,
                selectionConstraint: selectionConstraint));
        IEnumerable<string> Morale(int count) => player.Morale.Where(card => !card.IsGodPower)
            .Select(card => card.InstanceId);

        switch (plan)
        {
            case "lubu":
                if (!CanReturnMorale(player, 2) || !enemy.Any(card => card.DisasterLevel is 1 or 2)) break;
                Optional("吕布：预先声明是否返还2士气发动登场效果");
                Many("target-morale", "returnCost", "吕布：预先选择返还的2张士气", Morale(2), 2, 2, "mode:use");
                One("enemy-legion", "target", "吕布：预先选择击杀目标",
                    enemy.Where(card => card.DisasterLevel is 1 or 2).Select(card => card.InstanceId), "mode:use"); break;
            case "wuzetian":
                if (!CanReturnMorale(player, 1) || !enemy.Any(card => card.Tapped)) break;
                Optional("武则天：预先声明是否返还1士气发动登场效果");
                One("target-morale", "returnCost", "武则天：预先选择返还的1张士气", Morale(1), "mode:use");
                Many("enemy-legion", "targets", "武则天：预先选择1至2张休整军团",
                    enemy.Where(card => card.Tapped).Select(card => card.InstanceId), 1, 2, "mode:use"); break;
            case "lijing":
                if (player.Library.Count > 0) Optional("李靖：预先声明是否展示牌库顶牌");
                break;
            case "mulan":
                if (!CanReturnMorale(player, 1)) break;
                Optional("花木兰：预先声明是否返还1士气获得冲锋");
                One("target-morale", "returnCost", "花木兰：预先选择返还的1张士气", Morale(1), "mode:use"); break;
            case "mozi":
                if (!CanReturnMorale(player, 1)
                    || !own.Any(card => L12StructuredCardRules.HasFaction(player, card, "tianting"))) break;
                Optional("墨子：预先声明是否返还1士气发动登场效果");
                One("target-morale", "returnCost", "墨子：预先选择返还的1张士气", Morale(1), "mode:use");
                Many("field-legion", "targets", "墨子：预先选择1至2张天廷军团",
                    own.Where(card => L12StructuredCardRules.HasFaction(player, card, "tianting"))
                        .Select(card => card.InstanceId), 1, 2, "mode:use"); break;
            case "zhuge":
                steps.Add(PublicTriggerStep("option", "disasterMode", "诸葛亮：预先声明是否发动随后天灾值调整段",
                    ["mode:none", "mode:use"]));
                steps.Add(PublicTriggerStep("option", "disasterValue", "诸葛亮：预先声明天灾值增加或减少1",
                    ["-1", "1"], requiredChoice: "mode:use")); break;
            case "sunwu":
                if (!CanReturnMorale(player, 1)) break;
                Optional("孙武：预先声明是否返还1士气获得下次战术免费");
                One("target-morale", "returnCost", "孙武：预先选择返还的1张士气", Morale(1), "mode:use"); break;
            case "thutmose": One("enemy-legion", "target", "图特摩斯三世：预先选择击杀目标",
                enemy.Where(card => card.Troops <= 5000).Select(card => card.InstanceId)); break;
            case "ramses": Many("field-legion", "targets", "拉美西斯二世：预先选择1至3张其他太阳城军团并确定顺序",
                own.Where(card => L12StructuredCardRules.HasFaction(player, card, "taiyangcheng")
                        && card.CardId != source.CardId)
                    .Select(card => card.InstanceId), 1, 3); break;
            case "horemheb":
                var tombGuards = own.Where(card => card.CardId == "S01-0212").ToArray();
                if (tombGuards.Length == 0) break;
                Optional("霍列姆赫布：预先声明是否弃置陵墓守卫获得冲锋");
                One("field-legion", "discardCost", "霍列姆赫布：预先选择弃置的陵墓守卫",
                    tombGuards.Select(card => card.InstanceId), "mode:use"); break;
            case "nitocris": One("field-legion", "target", "尼托克丽丝：预先选择转为活跃的陵墓守卫",
                own.Where(card => card.CardId == "S01-0212" && card.Tapped).Select(card => card.InstanceId)); break;
            case "ankh": One("field-legion", "target", "安卡神碑：选择本回合兵力+2000的陵墓守卫",
                own.Where(card => card.CardId == "S01-0212").Select(card => card.InstanceId)); break;
            case "canopic-one": One("field-legion", "targets", "卡诺匹斯罐一：选择本回合兵力+2000并获得强攻的太阳城军团",
                own.Where(card => L12StructuredCardRules.HasFaction(player, card, "taiyangcheng"))
                    .Select(card => card.InstanceId)); break;
            case "canopic-four": Many("field-legion", "targets", "卡诺匹斯罐四：预先选择1至2张太阳城军团",
                own.Where(card => L12StructuredCardRules.HasFaction(player, card, "taiyangcheng"))
                    .Select(card => card.InstanceId), 1, 2); break;
            case "oddr": Optional("神箭奥德尔：预先声明是否承受1点主宰伤害并抽1张牌"); break;
            case "egil":
                if (player.Library.Count < 2 || enemy.Length == 0) break;
                Optional("夺命诗人埃吉尔：预先声明是否支付主宰伤害与牌库费用");
                One("enemy-legion", "target", "夺命诗人埃吉尔：预先选择兵力-2000目标",
                    enemy.Select(card => card.InstanceId), "mode:use"); break;
            case "gram":
                if (player.Library.Count < 2 || !enemy.Any(card => card.Troops <= 3000)) break;
                Optional("神剑格拉墨：预先声明是否弃置牌库顶2张发动效果");
                One("enemy-legion", "target", "神剑格拉墨：预先选择返回牌库底目标",
                    enemy.Where(card => card.Troops <= 3000 && !L12SpecialDeckRules.IsDerivedSpecialCard(card))
                        .Select(card => card.InstanceId), "mode:use"); break;
            case "nobunaga": One("enemy-legion", "target", "织田信长：预先选择击杀目标",
                enemy.Where(card => card.CurrentCost <= 4).Select(card => card.InstanceId)); break;
            case "uesugi":
            {
                var x = State.Players.SelectMany(owner => owner.Field[1]).Count(card => card is { CardType: "tactic" });
                One("enemy-legion", "target", $"上杉谦信：预先选择费用不高于{x}的击杀目标",
                    enemy.Where(card => card.CurrentCost <= x).Select(card => card.InstanceId)); break;
            }
            case "hijikata":
                One("enemy-legion", "target1", "土方岁三：预先选择费用不高于2的目标",
                    enemy.Where(card => card.CurrentCost <= 2).Select(card => card.InstanceId));
                One("enemy-legion", "target2", "土方岁三：预先选择另一张费用不高于1的目标",
                    enemy.Where(card => card.CurrentCost <= 1).Select(card => card.InstanceId)); break;
            case "takasugi": One("enemy-legion", "target", "高杉晋作：预先选择费用-2目标",
                enemy.Select(card => card.InstanceId)); break;
            case "abe": One("field-legion", "target", "安倍晴明：预先选择获得免死的我方军团",
                own.Select(card => card.InstanceId)); break;
            case "tachibana": One("enemy-legion", "target", "立花誾千代：预先选择费用-3目标",
                enemy.Select(card => card.InstanceId)); break;
            case "inahime": One("field-legion", "target", "稻姬本多小松：预先选择我方前排其他高天原军团",
                PublicFactionLegions(player, "gaotianyuan").Where(card => card.InstanceId != source.InstanceId
                    && FindOnField(player, card.InstanceId, out var row, out _) is not null && row == 0
                    && card.Troops <= 5000).Select(card => card.InstanceId)); break;
            case "kusanagi": One("enemy-legion", "target", "草薙剑：预先选择费用不高于2的击杀目标",
                enemy.Where(card => card.CurrentCost <= 2).Select(card => card.InstanceId)); break;
            case "court-magician":
            {
                var counters = State.Players.SelectMany(owner => owner.Field[1])
                    .Where(card => card is not null && IsCounterTactic(card.CardId)).Select(card => card!.InstanceId);
                if (!counters.Any()) break;
                Optional("宫廷魔术师：预先声明是否发动登场效果");
                One("field-card", "target", "宫廷魔术师：预先选择场上的反击目标", counters, "mode:use"); break;
            }
            case "ring":
                // 牌库中的命中身份和是否命中都是隐藏信息；声明阶段只能使用公开的牌库数量。
                // 玩家支付弃牌费用后，合法结算才查看并处理未命中/展示加入手牌。
                if (player.Hand.Count == 0 || player.Library.Count == 0) break;
                Optional("万物统御之戒：预先声明是否弃置1张手牌检索");
                One("hand-card", "discardCost", "万物统御之戒：私密选择弃置的手牌",
                    player.Hand.Select(card => card.InstanceId), "mode:use"); break;
            case "arthur":
                if (player.SpecialZones.Runes < 1) break;
                Optional(FindKingsSwordOwner(player) is null
                    ? "亚瑟王：预先声明是否消耗1符文发动登场效果"
                    : "场上已存在〈王者之剑〉。若继续发动，仍会消耗1符文，但不会叠放、生成或转移〈王者之剑〉。");
                One("option", "runeCost", "亚瑟王：预先声明1符文费用", ["rune-count:1"], "mode:use"); break;
            case "heracles-promoted-entry": Optional("赫拉克勒斯·晋升：预先声明是否对双方主宰造成非致命伤害"); break;
            case "heracles": Optional("赫拉克勒斯：预先声明是否抽2后弃1"); break;
            case "morale-flip" or "theseus-flip" or "morale-flip-two":
                break;
            case "joan":
                if (player.Hand.Count == 0) break;
                Optional("圣女贞德：预先声明是否弃置1张手牌发动效果");
                One("hand-card", "discardCost", "圣女贞德：私密选择弃置的手牌",
                    player.Hand.Select(card => card.InstanceId), "mode:use"); break;
            case "robin":
                if (!EmptySlots(player).Any()) break;
                Optional("罗宾汉：预先声明是否从私密区域选择侍从骑士登场");
                One("field-slot", "entrySlot", "罗宾汉：预先选择公开登场位置", EmptySlots(player), "mode:use"); break;
            case "claudia":
                if (player.SpecialZones.Runes < 1 || enemy.Length == 0) break;
                Optional("克劳迪娅：预先声明是否消耗1符文发动效果");
                One("option", "runeCost", "克劳迪娅：预先声明1符文费用", ["rune-count:1"], "mode:use");
                One("enemy-legion", "target", "克劳迪娅：预先选择兵力-2000目标",
                    enemy.Select(card => card.InstanceId), "mode:use"); break;
            case "richard": Optional("狮心王理查一世：预先声明是否叠放1张侍从骑士"); break;
            case "magatama-search" or "takeda-search": Optional($"〈{source.Name}〉：预先声明是否查看牌库并检索"); break;
            case "takeda-followup":
            {
                var sanadas = player.Hand.Where(card => card.CardId == "S01-0404").Select(card => card.InstanceId).ToArray();
                var slots = EmptySlots(player).ToArray();
                var morale = player.Morale.Where(card => card.Tapped).Select(card => card.InstanceId).ToArray();
                if (sanadas.Length == 0 || slots.Length == 0) break;
                Optional("武田信玄：预先声明是否发动随后真田幸村登场段");
                One("hand-card", "entryCard", "武田信玄：私密选择手牌中的真田幸村", sanadas, "mode:use");
                One("field-slot", "entrySlot", "武田信玄：预先选择公开登场位置", slots, "mode:use");
                if (morale.Length > 0)
                    One("target-morale", "moraleTarget", "武田信玄：预先选择随后转为活跃的休整士气", morale, "mode:use");
                break;
            }
            case "canute":
            {
                var targets = own.Concat(player.Graveyard).Where(card => card.CardType == "legion"
                    && L12StructuredCardRules.HasFaction(player, card, "asgard")
                    && card.InstanceId != source.InstanceId && HasDeathTrigger(card))
                    .Select(card => card.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (targets.Length == 0) break;
                Optional("卡纽特大帝：预先声明是否触发阿斯加德军团的阵亡效果");
                Many("cards", "targets", "卡纽特大帝：预先选择1至2张非同名目标", targets, 1, 2,
                    "mode:use", "distinct-card-names"); break;
            }
            case "ii-naotora":
            {
                var targets = own.Where(card => L12StructuredCardRules.HasFaction(player, card, "gaotianyuan") && card.Tapped)
                    .Select(card => card.InstanceId).ToArray();
                if (player.Hand.Count == 0 || targets.Length == 0) break;
                Optional("井伊直虎：预先声明是否弃置1张手牌发动效果");
                One("hand-card", "discardCost", "井伊直虎：私密选择弃置的手牌",
                    player.Hand.Select(card => card.InstanceId), "mode:use");
                One("field-legion", "target", "井伊直虎：预先选择转为活跃的高天原军团", targets, "mode:use"); break;
            }
            case "imhotep":
            {
                if (player.Hand.Count >= opponent.Hand.Count) break;
                var targets = player.Graveyard.Where(card => L12StructuredCardRules.HasFaction(player, card, "taiyangcheng")
                    && card.CardType == "legion" && card.Cost >= 6).Select(card => card.InstanceId).ToArray();
                if (targets.Length == 0) break;
                Optional("伊姆何泰普：预先声明是否回收墓地军团");
                One("grave-card", "target", "伊姆何泰普：预先选择墓地目标", targets, "mode:use"); break;
            }
            case "perseus":
            {
                var targets = player.Graveyard.Where(card => card.CardId == "S02-0505").Select(card => card.InstanceId).ToArray();
                if (player.Hand.Count == 0 || targets.Length == 0) break;
                Optional("珀尔修斯：预先声明是否弃牌回收晋升者");
                One("hand-card", "discardCost", "珀尔修斯：私密选择弃置的手牌",
                    player.Hand.Select(card => card.InstanceId), "mode:use");
                One("grave-card", "target", "珀尔修斯：预先选择墓地晋升者", targets, "mode:use"); break;
            }
            case "heracles-promotion":
            {
                var hand = player.Hand.Where(card => card.CardType == "legion").ToArray();
                if (hand.Length == 0 || enemy.Length == 0) break;
                Optional("赫拉克勒斯·晋升：预先声明是否展示军团并放回牌库顶");
                One("hand-card", "discardCost", "赫拉克勒斯·晋升：私密选择展示并放回牌库顶的军团",
                    hand.Select(card => card.InstanceId), "mode:use");
                One("enemy-legion", "target", "赫拉克勒斯·晋升：预先选择击杀目标",
                    enemy.Select(card => card.InstanceId), "mode:use"); break;
            }
            case "perseus-promotion":
                if (!enemy.Any(card => card.Tapped)) break;
                Optional("珀尔修斯·晋升：预先声明是否锁定休整军团");
                One("enemy-legion", "target", "珀尔修斯·晋升：预先选择休整目标",
                    enemy.Where(card => card.Tapped).Select(card => card.InstanceId), "mode:use"); break;
        }
        return steps;
    }

    private bool TryCompleteBatch6JAEnterDeclaration(L12TriggerCandidate candidate, L12PendingActivation activation)
    {
        var plan = Batch6JAEnterPlan(candidate.SourceCardId, candidate.Trigger);
        if (plan is null) return false;
        var player = State.Players[candidate.Controller];
        var source = FindAuthoritativeCard(candidate.SourceInstanceId) ?? candidate.SourceSnapshot
            ?? CreateCard(candidate.SourceCardId, candidate.SourceInstanceId);
        var mode = activation.DeclaredValues.GetValueOrDefault("mode", []).SingleOrDefault();
        var preserve = plan is "zhuge" or "richard";
        if (plan == "takeda-search" && mode == "mode:none")
        {
            State.PendingTriggerStackCandidates.Remove(candidate);
            QueueBatch6JATakedaFollowup(candidate.Controller, source);
            return true;
        }
        if (mode == "mode:none" && !preserve)
        {
            State.PendingTriggerStackCandidates.Remove(candidate);
            AddEvent("ability-cancelled", candidate.Controller, $"〈{candidate.SourceName}〉未发动，未生成空堆叠项");
            AdvanceTriggerBatches();
            return true;
        }
        var currentSteps = Batch6JAEnterSteps(candidate, source, plan);
        string? error = currentSteps.Count == 0 ? $"〈{candidate.SourceName}〉的声明条件或对象已失效；效果未入栈" : null;
        foreach (var step in currentSteps)
        {
            if (step.RequiredDeclaredChoice is { } required
                && !activation.DeclaredValues.Values.Any(values => values.Contains(required,
                    StringComparer.OrdinalIgnoreCase))) continue;
            var values = activation.DeclaredValues.GetValueOrDefault(step.DeclarationKey ?? string.Empty, []);
            if (values.Count < step.MinChoose || values.Count > step.MaxChoose
                || values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count
                || values.Any(value => !step.ValidChoices.Contains(value, StringComparer.OrdinalIgnoreCase)))
                error = $"〈{candidate.SourceName}〉的公开声明已失效；未支付费用且效果未入栈";
        }
        if (plan == "hijikata" && activation.DeclaredValues.GetValueOrDefault("target1", []).SingleOrDefault()
            == activation.DeclaredValues.GetValueOrDefault("target2", []).SingleOrDefault())
            error = "土方岁三的两个公开目标必须不同；效果未入栈";
        if (plan == "canute")
        {
            var selected = activation.DeclaredValues.GetValueOrDefault("targets", []);
            var names = selected.Select(id => FindAuthoritativeCard(id)?.Name).Where(name => name is not null).ToArray();
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
                error = "卡纽特大帝的公开目标必须非同名；效果未入栈";
        }
        if (plan == "heracles-promotion" && mode == "mode:use")
        {
            var handId = activation.DeclaredValues.GetValueOrDefault("discardCost", []).SingleOrDefault();
            var shown = player.Hand.FirstOrDefault(card => card.InstanceId == handId && card.CardType == "legion");
            var targetId = activation.DeclaredValues.GetValueOrDefault("target", []).SingleOrDefault();
            var target = DeclaredEnemyTarget(candidate.Controller, targetId);
            if (shown is null || target is null || target.CurrentCost > shown.CurrentCost)
                error = "赫拉克勒斯·晋升声明的手牌军团或击杀目标已失效；效果未入栈";
        }
        if (error is not null)
        {
            RemoveUnstackedTriggerCandidate(candidate, error);
            return true;
        }

        candidate.Data["declaration-committing"] = "true";
        if (mode == "mode:use")
        {
            var morale = activation.DeclaredValues.GetValueOrDefault("returnCost", []);
            if (morale.Count > 0) _ = ReturnSelectedMoraleById(player, morale, morale.Count);
            if (activation.DeclaredValues.GetValueOrDefault("runeCost", []).Count > 0)
                _ = L12S2ZoneOps.SpendRunes(player, 1);
            var discard = activation.DeclaredValues.GetValueOrDefault("discardCost", []).SingleOrDefault();
            if (discard is not null)
            {
                if (plan == "heracles-promotion")
                {
                    var card = player.Hand.First(entry => entry.InstanceId == discard);
                    player.Hand.Remove(card); player.Library.Insert(0, card);
                    AddEvent("reveal", candidate.Controller, $"赫拉克勒斯·晋升展示〈{card.Name}〉并放回牌库顶部", card);
                }
                else if (plan == "horemheb")
                {
                    var guard = FindOnField(player, discard, out _, out _);
                    if (guard is not null)
                        RemoveFromField(player, guard, true, "被霍列姆赫布作为费用弃置",
                            leaveKind: L12FieldLeaveKind.Discard);
                }
                else MoveHandToGrave(player, discard, causedByEffect: false);
            }
            if (plan is "oddr" or "egil") DamageMaster(candidate.Controller, 1, $"{candidate.SourceName}登场效果费用");
            if (plan is "egil" or "gram") Mill(player, 2, $"{candidate.SourceName}登场效果费用");
        }
        foreach (var pair in activation.DeclaredValues)
            candidate.Data[$"declared:{pair.Key}"] = string.Join('|', pair.Value);
        if (plan == "zhuge")
            foreach (var pair in CompositeFirstSegmentData("trigger:S01-0111:enter", activation.DeclaredValues)) candidate.Data[pair.Key] = pair.Value;
        else if (plan == "canopic-one")
            foreach (var pair in CompositeFirstSegmentData("trigger:S01-0217:enter", activation.DeclaredValues)) candidate.Data[pair.Key] = pair.Value;
        else if (plan == "canopic-four")
            foreach (var pair in CompositeFirstSegmentData("trigger:S01-0220:enter", activation.DeclaredValues)) candidate.Data[pair.Key] = pair.Value;
        if (plan == "takeda-search") candidate.Data["batch6JAFollowup"] = "takeda";
        candidate.Data.Remove("declaration-committing");
        candidate.Data["declaration-complete"] = "true";
        AdvanceTriggerBatches();
        return true;
    }

    private bool TryResolveBatch6JAEnterEffect(L12StackItem item, L12CardInstance source)
    {
        var plan = Batch6JAEnterPlan(item.SourceCardId, item.Trigger);
        if (plan is null && !AtomicFlowKey(item, source).StartsWith("batch6ja-", StringComparison.OrdinalIgnoreCase)) return false;
        return ResolveBatch6JAEnterEffect(item, source, plan);
    }

    private void QueueBatch6JATakedaFollowup(int controller, L12CardInstance source)
        => QueueOrPushTriggeredEffect(controller, source, "enter-followup",
            "武田信玄：随后选择将手牌1张〈真田幸村〉活跃登场，并将1张士气转为活跃");

    private bool ResolveBatch6JAEnterEffect(L12StackItem item, L12CardInstance source, string? plan)
    {
        var player = State.Players[item.Controller];
        var opponent = State.Players[1 - item.Controller];
        string One(string key) => PublicTriggerDeclared(item, key).Split('|', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        string[] Many(string key) => PublicTriggerDeclared(item, key).Split('|', StringSplitOptions.RemoveEmptyEntries);
        var flow = AtomicFlowKey(item, source);
        if (flow == "zhuge-reveal")
        {
            var next = State.DisasterDeck.FirstOrDefault();
            AddEvent("reveal", item.Controller, next is null ? "诸葛亮查看天灾牌库：没有下一张天灾" : $"诸葛亮查看下一张天灾：{next.Name}", next is null ? [] : [next]);
            FinishStackItem(item); return true;
        }
        if (flow == "zhuge-disaster")
        {
            if (int.TryParse(One("disasterValue"), out var delta))
                AdjustDisasterValue(delta, item.Controller, $"诸葛亮将天灾值{(delta > 0 ? "增加" : "减少")}1");
            FinishStackItem(item); return true;
        }
        if (flow is "canopic-one" or "canopic-four")
        {
            foreach (var id in Many("targets"))
                if (FindOnField(player, id, out _, out _) is { } target)
                {
                    if (flow.EndsWith("one", StringComparison.OrdinalIgnoreCase))
                    { AddTimedModifier(target, 2000, 0, State.TurnSerial, source.Name); GrantStrongAttack(target); }
                    else GrantImmortalUntilNextTurnStart(target, item.Controller);
                }
            FinishStackItem(item); return true;
        }
        if (flow is "canopic-one-discard" or "canopic-four-discard")
        {
            if (FindAuthoritativeCard(item.SourceInstanceId) is { } relic) DiscardRelic(player, relic);
            FinishStackItem(item); return true;
        }

        switch (plan)
        {
            case "lubu": KillTarget(item, One("target"), "被吕布效果击杀"); break;
            case "wuzetian": foreach (var id in Many("targets")) if (FindOnField(opponent, id, out _, out _) is { } card) card.CannotUntapUntilRound = State.Round + 1; break;
            case "lijing": BeginLiJingEffect(item); return true;
            case "mulan": if (FindOnField(player, item.SourceInstanceId, out _, out _) is { } mulan) mulan.HasCharge = true; break;
            case "mozi": foreach (var id in Many("targets")) if (FindOnField(player, id, out _, out _) is { } card) GrantImmortalUntilNextTurnStart(card, item.Controller); break;
            case "sunwu": player.FreeTacticCount++; break;
            case "thutmose" or "nobunaga" or "kusanagi": KillTarget(item, One("target"), $"被{source.Name}击杀"); break;
            case "ramses":
            {
                var inherited = L12StructuredCardRules.HasSummonTurnCounterTacticProtection(source, State.Round);
                var targets = Many("targets").Select(id => FindOnField(player, id, out _, out _))
                    .Where(target => target is not null && HasImmediateEffect(target, "enter")).Cast<L12CardInstance>().ToArray();
                FinishStackItem(item);
                foreach (var target in targets.Reverse())
                    QueueOrPushTriggeredEffect(item.Controller, target, "enter", $"{source.Name}再次发动{target.Name}的登场时效果",
                        data: inherited ? new() { ["inheritedCounterTacticProtection"] = "true",
                            ["counterTacticProtectionSourceInstanceId"] = source.InstanceId } : null);
                return true;
            }
            case "horemheb": if (FindOnField(player, item.SourceInstanceId, out _, out _) is { } horemheb) horemheb.HasCharge = true; break;
            case "nitocris": if (FindOnField(player, One("target"), out _, out _) is { } nitocris) ReadyCardByEffect(item.Controller, source, nitocris, $"{nitocris.Name}因效果转为活跃"); break;
            case "ankh": if (FindOnField(player, One("target"), out _, out _) is { } ankh) AddTimedModifier(ankh, 2000, 0, State.TurnSerial, source.Name); break;
            case "oddr": Draw(player, 1); break;
            case "egil": if (FindOnField(opponent, One("target"), out _, out _) is { } egil) AddTimedModifier(egil, -2000, 0, State.TurnSerial, source.Name); break;
            case "gram": ReturnEnemyFieldToLibraryBottom(item.Controller, One("target")); break;
            case "uesugi": KillTarget(item, One("target"), "被上杉谦信击杀"); break;
            case "hijikata": foreach (var id in new[] { One("target1"), One("target2") }) if (!string.IsNullOrEmpty(id)) KillTarget(item, id, "被土方岁三击杀"); break;
            case "takasugi":
                if (!Draw(player, 1)) SetWinner(1 - item.Controller, "高杉晋作效果抽牌时牌库为空");
                if (FindOnField(opponent, One("target"), out _, out _) is { } taka) AddTimedModifier(taka, 0, -2, State.TurnSerial, source.Name); break;
            case "abe": if (FindOnField(player, One("target"), out _, out _) is { } abe) GrantImmortalUntilNextTurnStart(abe, item.Controller); break;
            case "tachibana": if (FindOnField(opponent, One("target"), out _, out _) is { } tachibana) AddTimedModifier(tachibana, 0, -3, State.TurnSerial, source.Name); break;
            case "inahime":
                if (FindOnField(player, One("target"), out var inaRow, out _) is { } ina && inaRow == 0
                    && IsFieldLegion(ina) && ina.InstanceId != item.SourceInstanceId && ina.Troops <= 5000
                    && L12StructuredCardRules.HasFaction(player, ina, "gaotianyuan"))
                    AddTimedModifier(ina, 1000, 0, State.TurnSerial, source.Name);
                else AddEvent("effect-cancelled", item.Controller,
                    "稻姬本多小松选择的前排高天原军团失效；该项兵力增加不结算", source);
                break;
            case "court-magician":
            {
                var target = FindPublicCard(One("target"), out var owner);
                if (target is not null && IsCounterTactic(target.CardId))
                    RemoveFromField(State.Players[owner], target, true, "被宫廷魔术师置入墓地",
                        leaveKind: L12FieldLeaveKind.PutIntoGraveyard);
                break;
            }
            case "ring":
            {
                var choices = player.Library.Where(card => card.Faction == "universal").Select(card => card.InstanceId).ToArray();
                if (choices.Length == 0) { ShuffleLibrary(player, "万物统御之戒检索未命中"); break; }
                CreatePrompt(item.Controller, "library-search", "万物统御之戒：选择牌库1张【通用】卡牌展示并加入手牌", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new() { ["action"] = "s2-ring-search" }); return true;
            }
            case "arthur":
                if (FindKingsSwordOwner(player) is { } existingSwordOwner)
                    AddEvent("effect-noop", item.Controller,
                        $"〈王者之剑〉为 Limit 1，符文已消耗；剑仍叠放在〈{existingSwordOwner.Name}〉下方，本次效果无事发生",
                        source, existingSwordOwner);
                else if (FindOnField(player, item.SourceInstanceId, out _, out _) is { } arthur)
                {
                    var sword = player.Graveyard.FirstOrDefault(card => card.CardId == "S02-06S2")
                        ?? CreateCard("S02-06S2", $"p{item.Controller}-arthur-sword-{State.TurnSerial}");
                    player.Graveyard.Remove(sword); sword.OwnerIndex = item.Controller; arthur.AttachedCards.Add(sword);
                    RecalculateContinuousTroops();
                    AddEvent("attach", item.Controller, "〈王者之剑〉叠放至〈亚瑟王〉下方", arthur, sword);
                }
                break;
            case "heracles-promoted-entry": DamageMasterNonLethal(0, 1, "赫拉克勒斯·晋升登场效果"); DamageMasterNonLethal(1, 1, "赫拉克勒斯·晋升登场效果"); break;
            case "heracles":
                if (!Draw(player, 2)) { SetWinner(1 - item.Controller, "赫拉克勒斯登场效果抽牌时牌库为空"); break; }
                CreatePrompt(item.Controller, "hand-card", "赫拉克勒斯：抽取2张牌后弃置1张手牌",
                    player.Hand.Select(card => card.InstanceId), 1, 1, "card-effect", item.StackItemId,
                    data: new() { ["action"] = "s2-olympus-draw-discard" }); return true;
            case "morale-flip" or "theseus-flip" or "morale-flip-two":
                return PromptS2FlipMorale(item, source, optional: true, onlyTapped: plan == "theseus-flip");
            case "joan": ProtectMasterUntilNextTurnStart(player, item.Controller); break;
            case "robin":
            {
                var candidates = player.Hand.Concat(player.Library).Concat(player.Graveyard).Where(card => card.CardId == "S02-0609").ToArray();
                if (candidates.Length == 0) break;
                item.Data["declared:entrySlot"] = One("entrySlot");
                var data = new Dictionary<string, string> { ["action"] = "s2-robin-summon-squire", ["skip"] = "不发动" };
                foreach (var candidate in candidates)
                {
                    AddPromptCardData(data, candidate);
                    data[$"{candidate.InstanceId}:zone"] = player.Hand.Contains(candidate) ? "手牌"
                        : player.Library.Contains(candidate) ? "牌库" : "墓地";
                }
                if (candidates.Length == 0) { FinishStackItem(item); return true; }
                CreatePrompt(item.Controller, "optional-card", "罗宾汉：选择1张侍从骑士活跃登场", candidates.Select(card => card.InstanceId).Append("skip"), 1, 1,
                    "card-effect", item.StackItemId, data: data); return true;
            }
            case "claudia": if (FindOnField(opponent, One("target"), out _, out _) is { } claudia) AddTimedModifier(claudia, -2000, 0, ExpiryAtNextOwnEnd(item.Controller), source.Name); break;
            case "magatama-search":
            {
                var choices = player.Library.Where(card => L12StructuredCardRules.HasFaction(player, card, "gaotianyuan")
                    && card.CardType == "legion" && card.Profession == "骑兵").Select(card => card.InstanceId).Append("skip").ToArray();
                CreatePrompt(item.Controller, "optional-card", "八尺琼勾玉：选择高天原骑兵展示并加入手牌", choices, 1, 1, "card-effect", item.StackItemId,
                    data: new() { ["action"] = "s2-magatama-search", ["skip"] = "不加入手牌" }); return true;
            }
            case "takeda-search":
            {
                var choices = player.Library.Where(card => L12StructuredCardRules.HasFaction(player, card, "gaotianyuan")
                        && card.CardType == "legion" && card.BaseTroops <= 5000)
                    .Select(card => card.InstanceId).Append("skip").ToArray();
                CreatePrompt(item.Controller, "optional-card", "武田信玄：选择符合条件的高天原军团展示并加入手牌", choices, 1, 1,
                    "card-effect", item.StackItemId, data: new() { ["action"] = "s2-takeda-search" }); return true;
            }
            case "takeda-followup":
            {
                var summoned = TrySummonFromAnyPrivateZone(player, player.PlayerIndex, One("entryCard"), One("entrySlot"), tapped: false);
                if (summoned && player.Morale.FirstOrDefault(card => card.InstanceId == One("moraleTarget") && card.Tapped) is { } readyMorale)
                    ReadyMoraleByEffect(item.Controller, source, readyMorale, "武田信玄使1张士气转为活跃");
                break;
            }
            case "canute":
            {
                var triggers = Many("targets").Select(id => FindAuthoritativeCard(id))
                    .Where(card => card is not null && card.CardType == "legion"
                        && L12StructuredCardRules.HasFaction(player, card, "asgard") && HasDeathTrigger(card))
                    .Cast<L12CardInstance>().DistinctBy(card => card.Name, StringComparer.Ordinal).Take(2)
                    .Select(card => CreateTriggerCandidate(item.Controller, card, "death", "【阵亡时】效果")).ToArray();
                if (triggers.Length > 0)
                {
                    QueueTriggerCandidates(triggers);
                    AddEvent("effect", item.Controller, $"卡纽特大帝触发了{triggers.Length}张军团的阵亡时效果", source);
                }
                break;
            }
            case "ii-naotora": if (FindOnField(player, One("target"), out _, out _) is { } ii) ReadyCardByEffect(item.Controller, source, ii, $"{ii.Name}因效果转为活跃"); break;
            case "imhotep":
            case "perseus":
            {
                var target = player.Graveyard.FirstOrDefault(card => card.InstanceId == One("target"));
                if (target is not null) { player.Graveyard.Remove(target); AddCardToHandByEffect(player, target, "graveyard", $"{source.Name}将{target.Name}加入手牌"); }
                break;
            }
            case "heracles-promotion": KillTarget(item, One("target"), "被赫拉克勒斯·晋升击杀"); break;
            case "perseus-promotion": if (FindOnField(opponent, One("target"), out _, out _) is { } perseus) perseus.CannotUntapUntilRound = Math.Max(perseus.CannotUntapUntilRound, State.Round + 1); break;
            case "richard":
                AdvanceTrial(item.Controller, 2, source);
                if (FindOnField(player, item.SourceInstanceId, out _, out _) is { } richard)
                {
                    GrantImmortalUntilNextTurnStart(richard, item.Controller);
                    if (PublicTriggerDeclared(item, "mode") == "mode:use") return PromptS2RichardEntryAttach(item, richard);
                }
                break;
        }
        FinishStackItem(item);
        return true;
    }
}
