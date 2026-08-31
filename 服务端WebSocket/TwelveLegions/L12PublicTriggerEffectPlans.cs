namespace TwelveLegions.Server;

/// <summary>
/// 公开触发效果的统一声明计划。触发候选只有在可选模式、公开费用对象、公开目标与公开位置
/// 全部声明并再次验证后才能入栈；结算时只重验对应效果段，不回滚已经合法支付的费用。
/// 依赖隐藏信息的对象不得加入这里，必须在效果合法开始并公开该信息后走延迟公开声明。
/// </summary>
public sealed partial class L12GameEngine
{
    private const string PublicTriggerTombGuardCard = "S01-0212";
    private const string PublicTriggerSigurdCard = "S01-0310";
    private const string PublicTriggerScarabCard = "S02-0201";
    private static readonly IReadOnlyDictionary<string, string> FifthBatchPublicTriggerPlans =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0320|reaction"] = "blood-eagle",
            ["S01-0224|wisdom-reward"] = "wisdom-reward",
        };

    private static string? FifthBatchPublicTriggerPlan(string cardId, string trigger)
        => FifthBatchPublicTriggerPlans.GetValueOrDefault($"{cardId}|{trigger}");

    private static bool HasPublicTriggerDeclarationPlan(string cardId, string trigger,
        IReadOnlyDictionary<string, string>? data = null)
        => FifthBatchPublicTriggerPlan(cardId, trigger) is not null
            || (cardId, trigger, data?.GetValueOrDefault("ability"), data?.GetValueOrDefault("mode")) switch
        {
            ("S02-04M1", "active", "tsukuyomiFollowMove" or "tsukuyomiReadyMorale", _) => true,
            ("S02-0523", "trojan-after-attack", _, _) => true,
            ("S01-02M3", "medjed-master-damage", _, _) => true,
            ("S02-02M1", "nephthys-own-death", _, _) => true,
            ("S02-01S1", "master-morale-return", _, "xiaotian") => true,
            ("S01-0105" or "S01-0207" or "S01-0208" or "S01-0309" or "S02-0203"
                or "S02-0205" or "S01-0407", "enter", _, _) => true,
            ("S01-0021" or "S01-0213" or "S01-0223", "reaction", _, _) => true,
            ("S02-0202", "death", _, _) => true,
            ("S01-0206", "attack", _, _) => true,
            _ => false,
        };

    /// <summary>
    /// 传统的直接 PushEffect 路径也必须接入同一触发候选声明链，否则手牌打出、GM 置入或
    /// 其他效果登场会绕过公开声明。只有已登记公共计划的效果改走候选，其余效果保持原流程。
    /// </summary>
    private void QueueOrPushTriggeredEffect(int controller, L12CardInstance source, string trigger, string text,
        IEnumerable<string>? targets = null, Dictionary<string, string>? data = null)
    {
        if (!HasPublicTriggerDeclarationPlan(source.CardId, trigger, data))
        {
            PushEffect(controller, source, trigger, text, targets, data);
            return;
        }

        var candidate = CreateTriggerCandidate(controller, source, trigger, text, data);
        if (targets is not null)
            candidate.Data["declaredTargets"] = string.Join('|', targets);
        QueueTriggerCandidates([candidate]);
    }

    private bool TryBeginPublicTriggerDeclaration(L12TriggerCandidate candidate, L12CardInstance source)
    {
        var player = State.Players[candidate.Controller];
        var opponent = State.Players[1 - candidate.Controller];
        List<L12ActivationSelectionStep>? steps = null;
        var fifthBatchPlan = FifthBatchPublicTriggerPlan(candidate.SourceCardId, candidate.Trigger);

        if (fifthBatchPlan == "blood-eagle")
        {
            var asgard = player.Graveyard.Where(card => card.InstanceId != candidate.SourceInstanceId
                    && CanEnterHandOrLibrary(card)
                    && L12StructuredCardRules.HasFaction(player, card, "asgard"))
                .Select(card => card.InstanceId).ToList();
            if (asgard.Count < 2)
            {
                var direct = CompositeFirstSegmentData("trigger:S01-0320",
                    new Dictionary<string, List<string>> { ["mode"] = ["mode:none"] });
                foreach (var pair in direct) candidate.Data[pair.Key] = pair.Value;
                candidate.Data["declaration-complete"] = "true";
                return false;
            }
            steps =
            [
                PublicTriggerStep("order", "graveOrder",
                    "复仇血鹰：预先选择并排序墓地2张【阿斯加德】卡牌；第1张加入手牌，第2张置于牌库底部",
                    asgard, min: 2, max: 2),
            ];
        }
        else if (fifthBatchPlan == "wisdom-reward")
        {
            candidate.Data["preserveIndependentStack"] = "true";
            var recover = player.Graveyard.Where(card => card.InstanceId != candidate.SourceInstanceId
                    && card.CurrentCost <= 3 && card.CardType is "tactic" or "artifact")
                .Select(card => card.InstanceId).ToList();
            if (recover.Count == 0)
            {
                var direct = CompositeFirstSegmentData("wisdom-reward:S01-0224",
                    new Dictionary<string, List<string>> { ["mode"] = ["mode:none"] });
                foreach (var pair in direct) candidate.Data[pair.Key] = pair.Value;
                candidate.Data["declaration-complete"] = "true";
                return false;
            }
            steps =
            [
                PublicTriggerStep("option", "mode", "智慧法典：预先声明是否发动独立的墓地回收段",
                    ["mode:none", "mode:recover"]),
                PublicTriggerStep("grave-card", "recoverTarget",
                    "智慧法典：预先选择墓地1张费用不高于3的其他战术或圣物",
                    recover, requiredChoice: "mode:recover"),
            ];
        }
        else switch ((candidate.SourceCardId, candidate.Trigger, candidate.Data.GetValueOrDefault("ability")))
        {
            case ("S02-04M1", "active", "tsukuyomiFollowMove"):
            {
                var movedId = candidate.Data.GetValueOrDefault("moved");
                var targets = PublicLegions(player).Where(card => !card.Tapped && card.InstanceId != movedId
                        && FindOnField(player, card.InstanceId, out var row, out var slot) is not null
                        && AdjacentEmptySlots(player, row, slot).Any())
                    .Select(card => card.InstanceId).ToList();
                var canUse = targets.Count > 0 && ActiveResourceCount(player) > 0;
                steps =
                [
                    PublicTriggerStep("option", "mode", "月读：预先声明是否消耗1士气并位移另一张活跃军团",
                        canUse ? ["mode:none", "mode:use"] : ["mode:none"]),
                    PublicTriggerStep("composite-ordinary-payment", "cost", "月读：预先选择消耗的1份公开资源",
                        CompositeOrdinaryPaymentChoices(player), requiredChoice: "mode:use"),
                    PublicTriggerStep("field-legion", "target", "月读：预先选择另一张活跃军团进行1格位移",
                        targets, requiredChoice: "mode:use"),
                    PublicTriggerStep("adjacent-slot", "slot", "月读：预先选择该军团位移后的相邻空位",
                        ["dynamic"], referenceKey: "target", requiredChoice: "mode:use"),
                ];
                break;
            }
            case ("S02-04M1", "active", "tsukuyomiReadyMorale"):
                steps =
                [
                    PublicTriggerStep("target-morale", "morale", "月读：预先选择1张休整士气转为活跃",
                        player.Morale.Where(card => card.Tapped).Select(card => card.InstanceId)),
                ];
                break;
            case ("S02-0523", "trojan-after-attack", _):
            {
                var hostIndex = int.TryParse(candidate.Data.GetValueOrDefault("attacker"), out var attacker)
                    && attacker is >= 0 and <= 1 ? attacker : 1 - candidate.Controller;
                var slots = EmptySlots(State.Players[hostIndex]).ToList();
                steps =
                [
                    PublicTriggerStep("option", "mode", "特洛伊木马：预先声明是否置入对方战场",
                        slots.Count > 0 ? ["mode:none", "mode:use"] : ["mode:none"]),
                    PublicTriggerStep("enemy-slot", "slot", "特洛伊木马：预先选择置入对方战场的空位",
                        slots, requiredChoice: "mode:use"),
                ];
                break;
            }
            case ("S01-02M3", "medjed-master-damage", _):
            {
                candidate.Data["cleanupReservation"] = "pending:medjedDamageResponse";
                var guards = player.Graveyard.Where(card => card.CardId == PublicTriggerTombGuardCard)
                    .Select(card => card.InstanceId).Prepend("mode:none").ToList();
                steps =
                [
                    PublicTriggerStep("optional-card", "entryCard", "梅杰德：预先选择墓地1张〈陵墓守卫〉活跃登场，或不发动",
                        guards),
                    PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", "梅杰德：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                    PublicTriggerStep("effect-entry-slot", "entrySlot", "梅杰德：预先选择〈陵墓守卫〉活跃登场的位置",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                ];
                break;
            }
            case ("S02-02M1", "nephthys-own-death", _):
            {
                var scarabs = player.Graveyard.Where(card => card.CardId == PublicTriggerScarabCard)
                    .Select(card => card.InstanceId).Prepend("mode:none").ToList();
                steps =
                [
                    PublicTriggerStep("optional-card", "entryCard", "奈芙蒂斯：预先选择墓地1张〈增殖的甲虫〉活跃登场，或不发动",
                        scarabs),
                    PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", "奈芙蒂斯：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                    PublicTriggerStep("effect-entry-slot", "entrySlot", "奈芙蒂斯：预先选择〈增殖的甲虫〉活跃登场的位置",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                ];
                break;
            }
            case ("S02-01S1", "master-morale-return", _) when candidate.Data.GetValueOrDefault("mode") == "xiaotian":
            {
                var slots = Enumerable.Range(0, 3).Where(slot => player.Field[0][slot] is null)
                    .Select(slot => $"0:{slot}").ToList();
                steps =
                [
                    PublicTriggerStep("option", "mode", "杨戬专属：预先声明是否使〈哮天犬·稚〉在前排活跃登场",
                        slots.Count > 0 ? ["mode:none", "mode:use"] : ["mode:none"]),
                    PublicTriggerStep("slot", "slot", "哮天犬·稚：预先选择前排活跃登场的位置",
                        slots, requiredChoice: "mode:use"),
                ];
                break;
            }
            case ("S01-0105", "enter", _):
            {
                var brothers = player.Hand.Where(card => card.CardId is "S01-0106" or "S01-0107")
                    .Select(card => card.InstanceId).ToList();
                var canUse = brothers.Count > 0 && player.Morale.Count > 0 && EmptySlots(player).Any();
                steps =
                [
                    PublicTriggerStep("option", "mode", "刘备：预先声明是否返还1士气并使关羽或张飞活跃登场",
                        canUse ? ["mode:none", "mode:use"] : ["mode:none"]),
                    PublicTriggerStep("target-morale", "returnCost", "刘备：预先选择返还的1张士气",
                        player.Morale.Select(card => card.InstanceId), requiredChoice: "mode:use"),
                    PublicTriggerStep("hand-card", "entryCard", "刘备：预先选择手牌1张关羽或张飞",
                        brothers, requiredChoice: "mode:use"),
                    PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", "刘备：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard", requiredChoice: "mode:use"),
                    PublicTriggerStep("effect-entry-slot", "entrySlot", "刘备：预先选择活跃登场的位置",
                        ["dynamic"], referenceKey: "entryCard", requiredChoice: "mode:use"),
                ];
                break;
            }
            case ("S01-0207", "enter", _):
            {
                var guards = player.Graveyard.Where(card => card.CardId == PublicTriggerTombGuardCard)
                    .Select(card => card.InstanceId).ToList();
                var maximum = Math.Min(2, Math.Min(guards.Count, EmptySlots(player).Count()));
                steps = maximum == 0 ? [] :
                [
                    PublicTriggerStep("cards", "entryCards", "图坦卡蒙：预先选择墓地最多2张陵墓守卫活跃登场",
                        guards, min: 1, max: maximum),
                    PublicTriggerStep("unused-slot", "entrySlot1", "图坦卡蒙：预先选择第1张陵墓守卫登场位置",
                        EmptySlots(player)),
                    PublicTriggerStep("unused-slot", "entrySlot2", "图坦卡蒙：预先选择第2张陵墓守卫登场位置",
                        EmptySlots(player), referenceKey: "entryCards", minReferenceCount: 2),
                ];
                break;
            }
            case ("S01-0208", "enter", _):
            case ("S02-0202", "death", _):
            {
                var guards = player.Graveyard.Where(card => card.CardId == PublicTriggerTombGuardCard)
                    .Select(card => card.InstanceId).ToList();
                steps =
                [
                    PublicTriggerStep("grave-card", "entryCard", $"{source.Name}：预先选择墓地1张陵墓守卫登场", guards),
                    PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", $"{source.Name}：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard"),
                    PublicTriggerStep("effect-entry-slot", "entrySlot", $"{source.Name}：预先选择登场位置",
                        ["dynamic"], referenceKey: "entryCard"),
                ];
                break;
            }
            case ("S01-0309", "enter", _):
            {
                var sigurd = player.Hand.Concat(player.Graveyard).Where(card => card.CardId == PublicTriggerSigurdCard)
                    .Select(card => card.InstanceId).Prepend("mode:none").ToList();
                steps =
                [
                    PublicTriggerStep("optional-card", "entryCard", "布伦希尔德：预先选择齐格鲁德，或不发动", sigurd),
                    PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", "布伦希尔德：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                    PublicTriggerStep("effect-entry-slot", "entrySlot", "布伦希尔德：预先选择活跃登场位置",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                ];
                break;
            }
            case ("S01-0021", "reaction", _):
            {
                var legions = player.Hand.Where(card => card.CardType == "legion" && card.CurrentCost <= 3
                        && EffectEntryBattlefieldChoices(candidate.Controller, card).Any())
                    .Select(card => card.InstanceId).Prepend("mode:none").ToList();
                steps =
                [
                    PublicTriggerStep("optional-card", "entryCard", "摄政皇权：预先选择手牌1张费用不高于3的军团，或不发动", legions),
                    PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", "摄政皇权：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                    PublicTriggerStep("effect-entry-slot", "entrySlot", "摄政皇权：预先选择活跃登场位置",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                ];
                break;
            }
            case ("S01-0213", "reaction", _):
            {
                var slots = EmptySlots(player).ToList();
                steps =
                [
                    PublicTriggerStep("option", "mode", "锡瓦的卡巴：预先声明是否从手牌活跃登场",
                        slots.Count > 0 ? ["mode:none", "mode:use"] : ["mode:none"]),
                    PublicTriggerStep("slot", "entrySlot", "锡瓦的卡巴：预先选择活跃登场位置",
                        slots, requiredChoice: "mode:use"),
                ];
                break;
            }
            case ("S01-0223", "reaction", _):
            {
                candidate.Data["preserveIndependentStack"] = "true";
                var guards = player.Graveyard.Where(card => card.CardId == PublicTriggerTombGuardCard)
                    .Select(card => card.InstanceId).Prepend("mode:none").ToList();
                steps =
                [
                    PublicTriggerStep("optional-card", "entryCard", "不朽之礼：预先声明抽牌后登场的陵墓守卫，或不登场", guards),
                    PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", "不朽之礼：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                    PublicTriggerStep("effect-entry-slot", "entrySlot", "不朽之礼：预先选择活跃登场位置",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                ];
                break;
            }
            case ("S02-0203", "enter", _):
            case ("S02-0205", "enter", _):
            {
                var scarabs = player.Graveyard.Where(card => card.CardId == PublicTriggerScarabCard)
                    .Select(card => card.InstanceId).Prepend("mode:none").ToList();
                steps =
                [
                    PublicTriggerStep("optional-card", "entryCard", $"{source.Name}：预先选择墓地1张增殖的甲虫，或不发动", scarabs),
                    PublicTriggerStep("effect-entry-battlefield", "entryBattlefield", $"{source.Name}：预先选择登场战场",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                    PublicTriggerStep("effect-entry-slot", "entrySlot", $"{source.Name}：预先选择活跃登场位置",
                        ["dynamic"], referenceKey: "entryCard", skipWhenReferenceIsNone: true),
                ];
                break;
            }
            case ("S01-0206", "attack", _):
            {
                var guards = PublicLegions(player).Where(card => card.CardId == PublicTriggerTombGuardCard)
                    .Select(card => card.InstanceId).Prepend("mode:none").ToList();
                steps =
                [
                    PublicTriggerStep("optional-card", "moveTarget", "萨拉丁：预先选择位移的陵墓守卫，或不发动", guards),
                    PublicTriggerStep("unused-slot", "moveSlot", "萨拉丁：预先选择陵墓守卫位移后的位置",
                        EmptySlots(player), referenceKey: "moveTarget", skipWhenReferenceIsNone: true),
                ];
                break;
            }
            case ("S01-0407", "enter", _):
            {
                var emptySlotAvailable = EmptySlots(player).Any();
                var moverCards = PublicLegions(player)
                    .Where(card => emptySlotAvailable || card.Tapped).ToList();
                var movers = moverCards.Select(card => card.InstanceId).ToList();
                var minimum = emptySlotAvailable ? 1 : 2;
                var canUse = movers.Count >= minimum;
                steps =
                [
                    PublicTriggerStep("option", "mode", "坂本龙马：预先声明是否位移我方最多2张军团",
                        canUse ? ["mode:none", "mode:use"] : ["mode:none"]),
                    PublicTriggerStep("cards", "moveTargets", "坂本龙马：预先选择并排序要位移的最多2张军团",
                        movers, min: minimum, max: Math.Min(2, movers.Count), requiredChoice: "mode:use"),
                    PublicTriggerStep("public-move-slot", "moveSlot1", "坂本龙马：预先选择第1张军团位移位置",
                        ["dynamic"], referenceKey: "moveTargets", requiredChoice: "mode:use"),
                    PublicTriggerStep("public-move-slot", "moveSlot2", "坂本龙马：预先选择第2张军团位移位置",
                        ["dynamic"], referenceKey: "moveTargets", minReferenceCount: 2, referenceChoiceIndex: 1,
                        requiredChoice: "mode:use"),
                ];
                break;
            }
        }

        if (steps is null) return false;
        var result = BeginPendingActivationSequence(candidate.Controller, source, "public-trigger-declaration",
            steps, candidate.CandidateId);
        if (result.Accepted) return true;

        RemoveUnstackedTriggerCandidate(candidate, result.Error ?? "公开声明已失效，效果未入栈");
        return true;
    }

    private static L12ActivationSelectionStep PublicTriggerStep(string kind, string key, string text,
        IEnumerable<string> choices, int min = 1, int max = 1, string? referenceKey = null,
        bool skipWhenReferenceIsNone = false, string? requiredChoice = null,
        int minReferenceCount = 0, int referenceChoiceIndex = 0)
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
            RequiredDeclaredChoice = requiredChoice,
            MinimumReferenceCount = minReferenceCount,
            ReferenceChoiceIndex = referenceChoiceIndex,
            ChoiceLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode:none"] = "不发动",
                ["mode:use"] = "发动",
            },
        };

    private bool TryCompletePublicTriggerDeclaration(L12TriggerCandidate candidate, L12PendingActivation activation)
    {
        var key = (candidate.SourceCardId, candidate.Trigger, candidate.Data.GetValueOrDefault("ability"));
        var fifthBatchPlan = FifthBatchPublicTriggerPlan(candidate.SourceCardId, candidate.Trigger);
        var handled = fifthBatchPlan is not null || key switch
        {
            ("S02-04M1", "active", "tsukuyomiFollowMove") => true,
            ("S02-04M1", "active", "tsukuyomiReadyMorale") => true,
            ("S02-0523", "trojan-after-attack", _) => true,
            ("S01-02M3", "medjed-master-damage", _) => true,
            ("S02-02M1", "nephthys-own-death", _) => true,
            ("S02-01S1", "master-morale-return", _) => true,
            ("S01-0105", "enter", _) => true,
            ("S01-0207", "enter", _) => true,
            ("S01-0208", "enter", _) => true,
            ("S01-0309", "enter", _) => true,
            ("S01-0021", "reaction", _) => true,
            ("S01-0213", "reaction", _) => true,
            ("S01-0223", "reaction", _) => true,
            ("S02-0202", "death", _) => true,
            ("S02-0203", "enter", _) => true,
            ("S02-0205", "enter", _) => true,
            ("S01-0206", "attack", _) => true,
            ("S01-0407", "enter", _) => true,
            _ => false,
        };
        if (!handled)
            return false;

        var player = State.Players[candidate.Controller];
        var mode = activation.DeclaredValues.GetValueOrDefault("mode", []).SingleOrDefault();
        var entryCard = activation.DeclaredValues.GetValueOrDefault("entryCard", []).SingleOrDefault();
        var declaredNone = activation.DeclaredValues.Values.Any(values =>
            values.Contains("mode:none", StringComparer.OrdinalIgnoreCase));
        if (declaredNone && candidate.Data.GetValueOrDefault("preserveIndependentStack") != "true")
        {
            CleanupPublicTriggerReservation(candidate);
            State.PendingTriggerStackCandidates.Remove(candidate);
            AddEvent("ability-cancelled", candidate.Controller, $"〈{candidate.SourceName}〉的可选触发效果未发动，未进入堆叠");
            AdvanceTriggerBatches();
            return true;
        }

        string? error = null;
        if (key == ("S02-04M1", "active", "tsukuyomiFollowMove"))
        {
            var cost = activation.DeclaredValues.GetValueOrDefault("cost", []);
            var targetId = activation.DeclaredValues.GetValueOrDefault("target", []).SingleOrDefault();
            var slot = activation.DeclaredValues.GetValueOrDefault("slot", []).SingleOrDefault();
            var target = FindOnField(player, targetId, out var row, out var oldSlot);
            var onceKey = $"active:master-{candidate.Controller}:tsukuyomiFollowMove";
            if (player.UsedAbilities.Contains(onceKey) || target is null || target.Tapped
                || target.InstanceId == candidate.Data.GetValueOrDefault("moved") || slot is null
                || !AdjacentEmptySlots(player, row, oldSlot).Contains(slot, StringComparer.OrdinalIgnoreCase)
                || !CanConsumeSelectedResources(player, 1,
                    cost.Count == 1 && cost[0] == "temporary-morale:1" ? [] : cost))
                error = "月读的费用、公开目标或位移位置已失效；未支付费用且效果未入栈";
            else
            {
                _ = TryConsumeSelectedResources(player, 1,
                    cost.Count == 1 && cost[0] == "temporary-morale:1" ? [] : cost);
                player.UsedAbilities.Add(onceKey);
            }
        }
        else if (key == ("S02-04M1", "active", "tsukuyomiReadyMorale"))
        {
            var moraleId = activation.DeclaredValues.GetValueOrDefault("morale", []).SingleOrDefault();
            if (!player.Morale.Any(card => card.InstanceId == moraleId && card.Tapped))
                error = "月读声明的休整士气已失效；效果未入栈";
        }
        else if (key.Item1 == "S02-0523")
        {
            var hostIndex = int.TryParse(candidate.Data.GetValueOrDefault("attacker"), out var attacker)
                && attacker is >= 0 and <= 1 ? attacker : 1 - candidate.Controller;
            var slot = activation.DeclaredValues.GetValueOrDefault("slot", []).SingleOrDefault();
            if (!IsSetTrojanHorse(FindOnField(player, candidate.SourceInstanceId, out _, out _))
                || slot is null || !EmptySlots(State.Players[hostIndex]).Contains(slot, StringComparer.OrdinalIgnoreCase))
                error = "特洛伊木马的来源或公开置入位置已失效；效果未入栈";
        }
        else if (key.Item1 == "S01-02M3")
        {
            var slot = activation.DeclaredValues.GetValueOrDefault("entrySlot", []).SingleOrDefault();
            if (entryCard is null || !player.Graveyard.Any(card => card.InstanceId == entryCard
                    && card.CardId == PublicTriggerTombGuardCard)
                || slot is null || !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase)
                || player.UsedAbilities.Contains("trigger:medjedDamageResponse"))
                error = "梅杰德的公开军团或登场位置已失效；效果未入栈";
            else
                player.UsedAbilities.Add("trigger:medjedDamageResponse");
        }
        else if (key.Item1 == "S02-02M1")
        {
            var onceKey = $"s2-nephthys-scarab:{State.TurnSerial}";
            var slot = activation.DeclaredValues.GetValueOrDefault("entrySlot", []).SingleOrDefault();
            if (State.ActivePlayer == candidate.Controller || player.MasterId != "S02-02M1"
                || player.UsedAbilities.Contains(onceKey) || entryCard is null
                || !player.Graveyard.Any(card => card.InstanceId == entryCard
                    && card.CardId == PublicTriggerScarabCard)
                || slot is null || !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase))
                error = "奈芙蒂斯的公开军团或登场位置已失效；效果未入栈";
            else
            {
                player.UsedAbilities.Add(onceKey);
                candidate.Data["onceKey"] = onceKey;
            }
        }
        else if (key.Item1 == "S02-01S1")
        {
            var onceKey = candidate.Data.GetValueOrDefault("onceKey") ?? string.Empty;
            var slot = activation.DeclaredValues.GetValueOrDefault("slot", []).SingleOrDefault();
            if (string.IsNullOrWhiteSpace(onceKey) || player.UsedAbilities.Contains(onceKey)
                || slot is null || !Enumerable.Range(0, 3).Where(index => player.Field[0][index] is null)
                    .Select(index => $"0:{index}").Contains(slot, StringComparer.OrdinalIgnoreCase))
                error = "哮天犬·稚的公开登场位置已失效；效果未入栈";
            else
                player.UsedAbilities.Add(onceKey);
        }
        else if (key.Item1 == "S01-0105")
        {
            var costId = activation.DeclaredValues.GetValueOrDefault("returnCost", []).SingleOrDefault();
            if (entryCard is null || !player.Hand.Any(card => card.InstanceId == entryCard
                    && card.CardId is "S01-0106" or "S01-0107")
                || costId is null || !CanReturnSelectedMoraleById(player, [costId], 1)
                || !ValidateDeclaredEntry(candidate.Controller, activation, entryCard, player.Hand))
                error = "刘备的士气费用、关羽/张飞或登场位置已失效；未返还士气且效果未入栈";
            else
                _ = ReturnSelectedMoraleById(player, [costId], 1);
        }
        else if (key.Item1 == "S01-0207")
        {
            var cards = activation.DeclaredValues.GetValueOrDefault("entryCards", []);
            var slots = new[] { "entrySlot1", "entrySlot2" }
                .SelectMany(name => activation.DeclaredValues.GetValueOrDefault(name, [])).ToArray();
            if (cards.Count is < 1 or > 2 || slots.Length != cards.Count
                || cards.Distinct(StringComparer.OrdinalIgnoreCase).Count() != cards.Count
                || slots.Distinct(StringComparer.OrdinalIgnoreCase).Count() != slots.Length
                || cards.Any(id => !player.Graveyard.Any(card => card.InstanceId == id
                    && card.CardId == PublicTriggerTombGuardCard))
                || slots.Any(slot => !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase)))
                error = "图坦卡蒙声明的陵墓守卫或登场位置已失效；效果未入栈";
        }
        else if (key.Item1 is "S01-0208" or "S02-0202")
        {
            if (entryCard is null || !player.Graveyard.Any(card => card.InstanceId == entryCard
                    && card.CardId == PublicTriggerTombGuardCard)
                || !ValidateDeclaredEntry(candidate.Controller, activation, entryCard, player.Graveyard))
                error = $"{candidate.SourceName}声明的陵墓守卫或登场位置已失效；效果未入栈";
        }
        else if (key.Item1 == "S01-0309")
        {
            var sigurdZone = player.Hand.Concat(player.Graveyard).ToList();
            if (entryCard is null || !sigurdZone.Any(card => card.InstanceId == entryCard
                    && card.CardId == PublicTriggerSigurdCard)
                || !ValidateDeclaredEntry(candidate.Controller, activation, entryCard, sigurdZone))
                error = "布伦希尔德声明的齐格鲁德或登场位置已失效；未承受伤害且效果未入栈";
            else
                DamageMaster(candidate.Controller, 1, "布伦希尔德登场效果费用");
        }
        else if (key.Item1 == "S01-0021")
        {
            if (entryCard is null || !player.Hand.Any(card => card.InstanceId == entryCard
                    && card.CardType == "legion" && card.CurrentCost <= 3)
                || !ValidateDeclaredEntry(candidate.Controller, activation, entryCard, player.Hand))
                error = "摄政皇权声明的手牌军团或登场位置已失效；效果未入栈";
        }
        else if (key.Item1 == "S01-0213")
        {
            var slot = activation.DeclaredValues.GetValueOrDefault("entrySlot", []).SingleOrDefault();
            if (!player.Hand.Any(card => card.InstanceId == candidate.SourceInstanceId)
                || slot is null || !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase))
                error = "锡瓦的卡巴来源或公开登场位置已失效；效果未入栈";
        }
        else if (key.Item1 == "S01-0223")
        {
            if (entryCard != "mode:none"
                && (entryCard is null || !player.Graveyard.Any(card => card.InstanceId == entryCard
                        && card.CardId == PublicTriggerTombGuardCard)
                    || !ValidateDeclaredEntry(candidate.Controller, activation, entryCard, player.Graveyard)))
                error = "不朽之礼声明的陵墓守卫或登场位置已失效；抽牌段仍会独立结算";
            if (error is not null)
            {
                activation.DeclaredValues["entryCard"] = ["mode:none"];
                activation.DeclaredValues.Remove("entryBattlefield");
                activation.DeclaredValues.Remove("entrySlot");
                AddEvent("effect-cancelled", candidate.Controller, error);
                error = null;
            }
        }
        else if (fifthBatchPlan == "blood-eagle")
        {
            var order = activation.DeclaredValues.GetValueOrDefault("graveOrder", []);
            if (order.Count != 2 || order.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2
                || order.Any(id => !player.Graveyard.Any(card => card.InstanceId == id
                    && card.InstanceId != candidate.SourceInstanceId && CanEnterHandOrLibrary(card)
                    && L12StructuredCardRules.HasFaction(player, card, "asgard"))))
                error = "复仇血鹰声明的墓地顺序已失效；第一段仍会独立进入堆叠";
            if (error is not null)
            {
                activation.DeclaredValues.Clear();
                activation.DeclaredValues["mode"] = ["mode:none"];
                AddEvent("effect-cancelled", candidate.Controller, error);
                error = null;
            }
            else
                activation.DeclaredValues["mode"] = ["mode:recover"];
        }
        else if (fifthBatchPlan == "wisdom-reward")
        {
            var recovery = activation.DeclaredValues.GetValueOrDefault("recoverTarget", []).SingleOrDefault();
            if (mode == "mode:recover" && (recovery is null || !player.Graveyard.Any(card =>
                    card.InstanceId == recovery && card.InstanceId != candidate.SourceInstanceId && card.CurrentCost <= 3
                    && card.CardType is "tactic" or "artifact")))
            {
                activation.DeclaredValues["mode"] = ["mode:none"];
                activation.DeclaredValues.Remove("recoverTarget");
                AddEvent("effect-cancelled", candidate.Controller,
                    "智慧法典声明的墓地目标已失效；抽牌段仍会独立结算");
            }
        }
        else if (key.Item1 is "S02-0203" or "S02-0205")
        {
            if (entryCard is null || !player.Graveyard.Any(card => card.InstanceId == entryCard
                    && card.CardId == PublicTriggerScarabCard)
                || !ValidateDeclaredEntry(candidate.Controller, activation, entryCard, player.Graveyard))
                error = $"{candidate.SourceName}声明的增殖甲虫或登场位置已失效；效果未入栈";
        }
        else if (key.Item1 == "S01-0206" && key.Item2 == "attack")
        {
            var targetId = activation.DeclaredValues.GetValueOrDefault("moveTarget", []).SingleOrDefault();
            var slot = activation.DeclaredValues.GetValueOrDefault("moveSlot", []).SingleOrDefault();
            if (FindOnField(player, targetId, out _, out _) is not { CardId: PublicTriggerTombGuardCard }
                || slot is null || !EmptySlots(player).Contains(slot, StringComparer.OrdinalIgnoreCase))
                error = "萨拉丁声明的陵墓守卫或位移位置已失效；效果未入栈";
        }
        else if (key.Item1 == "S01-0407")
        {
            var targets = activation.DeclaredValues.GetValueOrDefault("moveTargets", []);
            var slots = new[] { "moveSlot1", "moveSlot2" }
                .SelectMany(name => activation.DeclaredValues.GetValueOrDefault(name, [])).ToArray();
            if (!ValidateRyomaMoveDeclaration(player, targets, slots))
                error = "坂本龙马声明的军团或位移位置已失效；效果未入栈";
        }

        if (error is not null)
        {
            RemoveUnstackedTriggerCandidate(candidate, error);
            return true;
        }

        foreach (var pair in activation.DeclaredValues)
            candidate.Data[$"declared:{pair.Key}"] = string.Join('|', pair.Value);
        if (fifthBatchPlan == "blood-eagle")
        {
            var composite = CompositeFirstSegmentData("trigger:S01-0320", activation.DeclaredValues);
            foreach (var pair in composite) candidate.Data[pair.Key] = pair.Value;
        }
        else if (fifthBatchPlan == "wisdom-reward")
        {
            var composite = CompositeFirstSegmentData("wisdom-reward:S01-0224", activation.DeclaredValues);
            foreach (var pair in composite) candidate.Data[pair.Key] = pair.Value;
        }
        candidate.Data["declaration-complete"] = "true";
        CleanupPublicTriggerReservation(candidate);
        AdvanceTriggerBatches();
        return true;
    }

    private void RemoveUnstackedTriggerCandidate(L12TriggerCandidate candidate, string reason)
    {
        CleanupPublicTriggerReservation(candidate);
        State.PendingTriggerStackCandidates.Remove(candidate);
        AddEvent("ability-rejected", candidate.Controller, reason);
        AdvanceTriggerBatches();
    }

    private void CleanupPublicTriggerReservation(L12TriggerCandidate candidate)
    {
        var reservation = candidate.Data.GetValueOrDefault("cleanupReservation");
        if (!string.IsNullOrWhiteSpace(reservation))
            State.Players[candidate.Controller].UsedAbilities.Remove(reservation);
    }

    private bool ValidateDeclaredEntry(int controller, L12PendingActivation activation, string cardId,
        IEnumerable<L12CardInstance> zone)
    {
        var card = zone.FirstOrDefault(candidate => candidate.InstanceId == cardId);
        var battlefield = ParseEffectEntryBattlefieldChoice(
            activation.DeclaredValues.GetValueOrDefault("entryBattlefield", []).SingleOrDefault());
        var slot = activation.DeclaredValues.GetValueOrDefault("entrySlot", []).SingleOrDefault();
        return card is not null && battlefield is not null
            && EffectEntryBattlefieldChoices(controller, card).Contains(battlefield.Value)
            && slot is not null && EmptySlots(State.Players[battlefield.Value])
                .Contains(slot, StringComparer.OrdinalIgnoreCase);
    }

    private bool ValidateRyomaMoveDeclaration(L12PlayerState player, IReadOnlyList<string> targets,
        IReadOnlyList<string> slots)
    {
        if (targets.Count is < 1 or > 2 || targets.Count != slots.Count
            || targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count
            || slots.Distinct(StringComparer.OrdinalIgnoreCase).Count() != slots.Count) return false;
        if (targets.Count == 2
            && FindOnField(player, targets[0], out var firstRow, out var firstSlot) is { Tapped: true }
            && FindOnField(player, targets[1], out var secondRow, out var secondSlot) is { Tapped: true }
            && slots[0] == $"{secondRow}:{secondSlot}" && slots[1] == $"{firstRow}:{firstSlot}")
            return true;
        var available = EmptySlots(player).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < targets.Count; index++)
        {
            if (FindOnField(player, targets[index], out var row, out var slot) is null
                || !available.Remove(slots[index])) return false;
            available.Add($"{row}:{slot}");
        }
        return true;
    }

    private static string PublicTriggerDeclared(L12StackItem item, string key)
        => item.Data.GetValueOrDefault($"declared:{key}", string.Empty);

    private void CreateDelayedPublicResolutionPrompt(L12StackItem item, string kind, string text,
        IEnumerable<string> choices, string action, Dictionary<string, string> data)
    {
        data["action"] = action;
        data["declarationTiming"] = "post-hidden-reveal";
        CreatePrompt(item.Controller, kind, text, choices, 1, 1, "card-effect", item.StackItemId,
            isPrivate: false, data: data);
    }
}
