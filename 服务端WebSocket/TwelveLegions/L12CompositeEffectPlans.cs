using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

internal sealed record L12CompositeEffectSegmentSpec(
    string Flow,
    string Text,
    string? RequiredMode = null,
    string? CostKind = null,
    string? CostKey = null,
    int Cost = 0,
    string[]? PublicTargetKeys = null,
    bool PreStackCost = false);

/// <summary>
/// 多段卡效的权威计划。卡牌差异只存在于这份声明数据；通用运行时负责在支付前
/// 收齐公开模式、目标与费用对象，并让每个独立效果段各自进入堆叠和响应窗口。
/// </summary>
internal static class L12CompositeEffectPlans
{
    private static readonly IReadOnlyDictionary<string, L12CompositeEffectSegmentSpec[]> HandPlayPlans =
        new Dictionary<string, L12CompositeEffectSegmentSpec[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0005"] =
            [
                new("volley-effect", "执行已声明的〈万箭齐发〉模式", PublicTargetKeys: ["singleTarget"]),
            ],
            ["S01-0006"] =
            [
                new("evil-ritual-effect", "对对方主宰造成1点非致命伤害",
                    CostKind: "discard-hand", CostKey: "discardCost", Cost: 1, PreStackCost: true),
            ],
            ["S01-0009"] =
            [
                new("strategic-transfer-effect", "依次结算已声明的回手与强化目标",
                    PublicTargetKeys: ["returnTarget", "buffTarget"]),
            ],
            ["S01-0010"] =
            [
                new("forged-orders-effect", "令已声明的对方军团进行前后1格位移",
                    PublicTargetKeys: ["moveTargets", "moveSlot1", "moveSlot2"]),
            ],
            ["S01-0011"] =
            [
                new("plague-effect", "令已声明的对方军团或士气在下个重置阶段无法转为活跃",
                    PublicTargetKeys: ["lockTarget"]),
            ],
            ["S01-0118"] =
            [
                new("march-buff-effect", "选择我方前排1张军团，本回合兵力+2000",
                    PublicTargetKeys: ["buffTarget"]),
                new("march-kill-effect", "返还2士气：击杀对方1张兵力不高于6000的军团",
                    "mode:kill", "morale-return", "marchCost", 2, ["killTarget"], true),
            ],
            ["S01-0221"] =
            [
                new("duat-effect", "执行已声明的〈杜阿特之门〉模式",
                    PublicTargetKeys: ["killTarget", "recoverTarget"]),
            ],
            ["S01-0318"] =
            [
                new("valkyrie-summon-effect", "令已声明的【阿斯加德】军团活跃登场",
                    CostKind: "conditional-master-damage", CostKey: "masterDamageCost", Cost: 1,
                    PublicTargetKeys: ["entryCard", "entrySlot"], PreStackCost: true),
            ],
            ["S01-0319"] =
            [
                new("hunt-kill-effect", "击杀已声明的对方军团",
                    CostKind: "grave-bottom", CostKey: "graveCost", Cost: 4,
                    PublicTargetKeys: ["killTarget"], PreStackCost: true),
            ],
            ["S02-0009"] =
            [
                new("defense-deployment-set", "将已私密声明的反击战术置入公开声明的后排位置",
                    PublicTargetKeys: ["entrySlot1", "entrySlot2"]),
                new("defense-deployment-draw", "若手牌数量不高于4，抽取1张牌"),
            ],
            ["S02-0010"] =
            [
                new("black-lotus-disaster", "将天灾值增加或减少已声明的数值"),
                new("black-lotus-morale", "消耗3士气：将此战术休整置入士气区",
                    "mode:morale", "ordinary-payment", "lotusCost", 3, PreStackCost: true),
            ],
            ["S02-0011"] =
            [
                new("chaotic-arrows-effect", "击杀已声明的最多3张军团",
                    PublicTargetKeys: ["killTargets"]),
            ],
            ["S02-0013"] =
            [
                new("holy-lock-effect", "叠放至已声明的对方圣物之上",
                    PublicTargetKeys: ["artifactTarget"]),
            ],
            ["S02-0522"] =
            [
                new("nyx-primary", "选择对方1张军团，本回合兵力-3000"),
                new("nyx-secondary", "消耗并翻转1神力：选择对方1张军团，本回合兵力-2000",
                    "mode:second", "god-power-flip", "secondCost", 1),
            ],
            ["S02-0105"] =
            [
                new("qianyang-kill", "击杀对方1张原本兵力不高于3000的军团"),
                new("qianyang-draw", "返还1士气：抽取1张牌",
                    "mode:draw", "morale-return", "drawCost", 1),
            ],
            ["S02-0521"] =
            [
                new("glory-flip", "翻转最多3张士气"),
                new("glory-search", "消耗并翻转2神力：检索1张【奥林匹斯】卡牌",
                    "mode:search", "god-power-flip", "searchCost", 2),
            ],
            ["S02-0620"] =
            [
                new("rune-gain", "获得1符文"),
                new("rune-search", "消耗1士气：查看牌库顶部3张牌",
                    "mode:search", "ordinary-payment", "searchCost", 1),
            ],
            ["S02-0621"] =
            [
                new("round-table-search", "检索1张【圆桌骑士】军团"),
                new("round-table-buff", "消耗1士气：选择我方1张【圆桌骑士】军团，本回合兵力+2000",
                    "mode:buff", "ordinary-payment", "buffCost", 1),
            ],
            ["S02-0207"] =
            [
                new("desert-transaction", "弃置最多3张军团并将等量天灾等级的【太阳城】军团活跃登场"),
            ],
            ["S02-0307"] =
            [
                new("hela-curse", "海拉：使已声明的对方军团本回合兵力-3000"),
            ],
            ["S02-0206"] =
            [
                new("fearless-assassination", "无畏的刺杀：强化已声明的我方前排【太阳城】军团"),
            ],
            ["S02-0406"] =
            [
                new("tenka-effect", "天下布武：执行已声明的模式"),
            ],
        };

    private static readonly IReadOnlyDictionary<string, L12CompositeEffectSegmentSpec[]> ActivePlans =
        new Dictionary<string, L12CompositeEffectSegmentSpec[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["active:S01-04D1:yomiSweep"] =
            [
                new("yomi-draw", "黄泉之门：抽取1张牌"),
                new("yomi-cost-debuff", "黄泉之门：对方所有军团本回合费用-1"),
                new("yomi-kill3", "黄泉之门：结算已声明的费用不高于3击杀目标"),
                new("yomi-kill1", "黄泉之门：结算已声明的费用不高于1击杀目标"),
            ],
        };

    private static readonly IReadOnlyDictionary<string, L12CompositeEffectSegmentSpec[]> ResponseAndTriggerPlans =
        new Dictionary<string, L12CompositeEffectSegmentSpec[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["response:S01-0020"] =
            [
                new("battle-until-dawn-buff", "我方所有军团本回合兵力+1000"),
                new("battle-until-dawn-draw", "若墓地卡牌数量不低于5，可抽取1张牌"),
            ],
            ["response:S01-0120"] =
            [
                new("empty-city-block", "返还1士气：抵挡本次进攻"),
                new("empty-city-draw", "若我方前排没有军团，可抽取1张牌"),
            ],
            ["wisdom-reward:S01-0224"] =
            [
                new("wisdom-draw", "抽取1张牌"),
                new("wisdom-recover", "可令墓地1张费用不高于3的其他战术或圣物回到手牌", "mode:recover"),
            ],
            ["trigger:S01-0320"] =
            [
                new("blood-eagle-debuff", "对方所有军团直到下个我方回合结束前兵力-1000"),
                new("blood-eagle-recover", "墓地2张【阿斯加德】卡牌分别回到手牌与牌库底部", "mode:recover"),
            ],
            ["response:S02-0016"] =
            [
                new("ruined-ritual", "执行已声明的弃置手牌或登场效果无效模式"),
            ],
            ["response:S02-0017"] =
            [
                new("supply-plunder-return", "将所选的1张对方手牌返回牌库顶部"),
                new("supply-plunder-draw", "随后我方抽取1张牌"),
            ],
            ["response:S02-0018"] =
            [
                new("poison-negate", "令本次因效果转为活跃无效"),
                new("poison-discard", "随后受影响玩家弃置1张手牌"),
            ],
        };

    public static bool RequiresHandPlayDeclaration(string cardId) => HandPlayPlans.ContainsKey(cardId);

    public static bool RequiresTriggerDeclaration(string cardId, string trigger)
        => cardId.Equals("S02-0516", StringComparison.OrdinalIgnoreCase)
            && trigger.Equals("attack", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<L12CompositeEffectSegmentSpec> Segments(string cardId)
        => HandPlayPlans.TryGetValue(cardId, out var handPlay) ? handPlay
            : ActivePlans.TryGetValue(cardId, out var active) ? active
            : ResponseAndTriggerPlans.GetValueOrDefault(cardId, []);
}

public sealed partial class L12GameEngine
{
    private const string CompositePlanChoicePrefix = "composite-plan:";

    private CommandResult BeginCompositeHandPlayDeclaration(int playerIndex, L12CardInstance source)
        => BeginCompositeDeclaration(playerIndex, source, "composite-play");

    private CommandResult BeginCommittedCompositeEffectDeclaration(int playerIndex, L12CardInstance source,
        L12StackItem parent, string completion)
    {
        var result = BeginCompositeDeclaration(playerIndex, source, "composite-committed-play");
        if (!result.Accepted) return result;
        var activation = State.PendingActivations.Last(candidate => candidate.Controller == playerIndex
            && candidate.SourceInstanceId == source.InstanceId
            && candidate.Ability == "composite-committed-play");
        activation.CommittedParentStackItemId = parent.StackItemId;
        activation.CommittedCompletion = completion;
        return result;
    }

    private CommandResult BeginCompositeDeclaration(int playerIndex, L12CardInstance source, string ability)
    {
        var player = State.Players[playerIndex];
        var opponent = State.Players[1 - playerIndex];
        var steps = new List<L12ActivationSelectionStep>();

        switch (source.CardId)
        {
            case "S01-0005":
                steps.Add(CompositeStep("option", "volleyMode", "万箭齐发：预先声明效果模式",
                    ["mode:front", "mode:back", "mode:single"], 1, 1,
                    new()
                    {
                        ["mode:front"] = "对方前排所有军团本回合兵力-2000",
                        ["mode:back"] = "对方后排所有军团本回合兵力-2000",
                        ["mode:single"] = "对方1张军团本回合兵力-4000",
                    }));
                steps.Add(CompositeStep("enemy-legion", "singleTarget", "万箭齐发：预先选择兵力-4000的目标",
                    PublicLegions(opponent).Select(card => card.InstanceId), 1,
                    requiredChoice: "mode:single"));
                break;

            case "S01-0006":
                steps.Add(CompositeStep("hand-card", "discardCost", "邪恶仪式：预先选择弃置的1张手牌费用",
                    player.Hand.Where(card => card.InstanceId != source.InstanceId).Select(card => card.InstanceId), 1));
                break;

            case "S01-0009":
                steps.Add(CompositeStep("field-legion", "returnTarget", "战略转移：预先选择回到手牌的我方军团",
                    PublicLegions(player).Select(card => card.InstanceId), 1));
                steps.Add(CompositeStep("field-legion", "buffTarget", "战略转移：预先选择本回合兵力+2000的我方军团",
                    PublicLegions(player).Select(card => card.InstanceId), 1));
                break;

            case "S01-0010":
            {
                var movable = PublicLegions(opponent).Where(card =>
                {
                    if (FindOnField(opponent, card.InstanceId, out var row, out var slot) is null) return false;
                    return opponent.Field[1 - row][slot] is null
                        && !(State.ActiveDisaster?.CardId == "S01-DS03" && 1 - row == 1);
                }).Select(card => card.InstanceId);
                steps.Add(CompositeStep("enemy-legion", "moveTargets", "伪造密令：预先选择最多2张要位移的对方军团",
                    movable, 1, 2));
                steps.Add(CompositeStep("composite-opposite-slot", "moveSlot1", "伪造密令：声明第1张军团的位移位置",
                    ["dynamic"], 1, referenceKey: "moveTargets", minimumReferenceCount: 1, referenceChoiceIndex: 0));
                steps.Add(CompositeStep("composite-opposite-slot", "moveSlot2", "伪造密令：声明第2张军团的位移位置",
                    ["dynamic"], 1, referenceKey: "moveTargets", minimumReferenceCount: 2, referenceChoiceIndex: 1));
                break;
            }

            case "S01-0011":
                steps.Add(CompositeStep("card", "lockTarget", "瘟疫感染：预先选择下个重置阶段无法转为活跃的目标",
                    PublicLegions(opponent).Select(card => card.InstanceId)
                        .Concat(opponent.Morale.Select(card => card.InstanceId)), 1));
                break;

            case "S01-0118":
            {
                var modes = new List<string> { "mode:none" };
                var killTargets = PublicLegions(opponent).Where(card => card.Troops <= 6000)
                    .Select(card => card.InstanceId).ToArray();
                if (player.Morale.Count >= 2 && killTargets.Length > 0) modes.Add("mode:kill");
                steps.Add(CompositeStep("option", "mode", "神妙行军：预先声明是否发动独立击杀段",
                    modes, 1, 1, new()
                    {
                        ["mode:none"] = "只结算前排军团兵力+2000",
                        ["mode:kill"] = "随后返还2士气并击杀1张兵力不高于6000的军团",
                    }));
                steps.Add(CompositeStep("field-legion", "buffTarget", "神妙行军：预先选择本回合兵力+2000的前排军团",
                    player.Field[0].Where(card => card is not null && IsFieldLegion(card))
                        .Select(card => card!.InstanceId), 1));
                steps.Add(CompositeStep("resource-return", "marchCost", "神妙行军：预先选择第二段返还的2张士气",
                    player.Morale.Select(card => card.InstanceId), 2, 2, requiredChoice: "mode:kill"));
                steps.Add(CompositeStep("enemy-legion", "killTarget", "神妙行军：预先选择第二段击杀目标",
                    killTargets, 1, requiredChoice: "mode:kill"));
                break;
            }

            case "S01-0221":
            {
                var killTargets = PublicLegions(opponent).Where(card => card.Troops <= 5000)
                    .Select(card => card.InstanceId).ToArray();
                var recoverTargets = player.Graveyard.Where(card => card.CardId != source.CardId
                        && L12StructuredCardRules.HasFaction(player, card, "taiyangcheng") && CanEnterHandOrLibrary(card))
                    .Select(card => card.InstanceId).ToArray();
                var modes = new List<string>();
                if (killTargets.Length > 0) modes.Add("mode:kill");
                modes.Add("mode:recover");
                steps.Add(CompositeStep("option", "duatMode", "杜阿特之门：预先声明效果模式",
                    modes, 1, 1, new()
                    {
                        ["mode:kill"] = "击杀对方1张兵力不高于5000的军团",
                        ["mode:recover"] = "墓地最多1张其他【太阳城】卡牌加入手牌",
                    }));
                steps.Add(CompositeStep("enemy-legion", "killTarget", "杜阿特之门：预先选择击杀目标",
                    killTargets, 1, requiredChoice: "mode:kill"));
                steps.Add(CompositeStep("optional-card", "recoverTarget", "杜阿特之门：预先选择回收目标，或不选择",
                    recoverTargets.Prepend("mode:none"), 1, requiredChoice: "mode:recover"));
                break;
            }

            case "S01-0318":
                steps.Add(CompositeStep("grave-card", "entryCard", "女武神的召唤：预先选择费用不高于5的【阿斯加德】军团",
                    player.Graveyard.Where(card => card.CardType == "legion" && card.CurrentCost <= 5
                            && L12StructuredCardRules.HasFaction(player, card, "asgard"))
                        .Select(card => card.InstanceId), 1));
                steps.Add(CompositeStep("unused-slot", "entrySlot", "女武神的召唤：预先选择活跃登场位置",
                    EmptySlots(player), 1));
                steps.Add(CompositeStep("cost-marker", "masterDamageCost", "女武神的召唤：确认主宰伤害费用",
                    ["cost:master-damage"], 1, 1, autoSelectWhenExact: true));
                break;

            case "S01-0319":
                steps.Add(CompositeStep("order", "graveCost", "猎杀时刻：预先选择并排序置于牌库底部的4张墓地卡牌",
                    player.Graveyard.Where(CanEnterHandOrLibrary).Select(card => card.InstanceId), 4, 4));
                steps.Add(CompositeStep("enemy-legion", "killTarget", "猎杀时刻：预先选择击杀目标",
                    PublicLegions(opponent).Where(card => card.Troops <= 6000).Select(card => card.InstanceId), 1));
                break;

            case "S02-0009":
                steps.Add(CompositeStep("hand-cards", "entryCards", "防御部署：私密选择手牌中最多2张反击战术",
                    player.Hand.Where(card => card.InstanceId != source.InstanceId && IsCounterTactic(card.CardId))
                        .Select(card => card.InstanceId), 0, 2));
                steps.Add(CompositeStep("composite-defense-slot", "entrySlot1", "防御部署：公开声明第1张反击战术的后排位置",
                    Enumerable.Range(0, 3).Where(slot => player.Field[1][slot] is null).Select(slot => $"1:{slot}"), 1,
                    referenceKey: "entryCards", minimumReferenceCount: 1));
                steps.Add(CompositeStep("composite-defense-slot", "entrySlot2", "防御部署：公开声明第2张反击战术的后排位置",
                    Enumerable.Range(0, 3).Where(slot => player.Field[1][slot] is null).Select(slot => $"1:{slot}"), 1,
                    referenceKey: "entryCards", minimumReferenceCount: 2));
                break;

            case "S02-0010":
            {
                var modes = new List<string> { "mode:none" };
                if (ActiveResourceCount(player) >= 3) modes.Add("mode:morale");
                steps.Add(CompositeStep("option", "disasterMode", "黑色莲花：预先声明天灾值调整",
                    ["-1", "0", "1"], 1, 1));
                steps.Add(CompositeStep("option", "mode", "黑色莲花：预先声明是否发动独立士气段",
                    modes, 1, 1, new()
                    {
                        ["mode:none"] = "调整天灾值后置入墓地",
                        ["mode:morale"] = "随后消耗3士气并将此战术休整置入士气区",
                    }));
                steps.Add(CompositeStep("composite-ordinary-payment", "lotusCost", "黑色莲花：预先选择第二段支付的3份资源",
                    CompositeOrdinaryPaymentChoices(player), 3, 3, requiredChoice: "mode:morale"));
                break;
            }

            case "S02-0011":
                steps.Add(CompositeStep("enemy-legion", "killTargets", "纷乱箭：预先选择最多3张原本兵力不高于2000的军团",
                    PublicLegions(opponent).Where(card => card.BaseTroops <= 2000)
                        .Select(card => card.InstanceId), 1, 3));
                break;

            case "S02-0013":
                steps.Add(CompositeStep("artifact-target", "artifactTarget", "神圣伽锁：预先选择叠放的对方圣物",
                    new[] { opponent.Relic }.Concat(opponent.ExtraRelics).Where(card => card is not null)
                        .Select(card => card!.InstanceId), 1));
                break;

            case "S02-0522":
                steps.Add(CompositeStep("option", "mode", "倪克斯的陨星：预先声明是否发动第二段效果",
                    ["mode:none", "mode:second"], 1, 1,
                    new() { ["mode:none"] = "只执行兵力-3000", ["mode:second"] = "追加消耗并翻转1神力的兵力-2000" }));
                steps.Add(CompositeStep("enemy-legion", "primaryTarget", "倪克斯的陨星：预先选择第一段目标",
                    PublicLegions(opponent).Select(card => card.InstanceId), 1));
                steps.Add(CompositeStep("target-morale", "secondCost", "倪克斯的陨星：预先选择第二段消耗并翻转的1神力",
                    player.Morale.Where(card => card.IsGodPower && !card.Tapped).Select(card => card.InstanceId), 1,
                    requiredChoice: "mode:second"));
                steps.Add(CompositeStep("enemy-legion", "secondaryTarget", "倪克斯的陨星：预先选择第二段目标",
                    PublicLegions(opponent).Select(card => card.InstanceId), 1, requiredChoice: "mode:second"));
                break;

            case "S02-0105":
                steps.Add(CompositeStep("option", "mode", "乾坤 阳：预先声明是否发动返还士气并抽牌",
                    ["mode:none", "mode:draw"], 1, 1,
                    new() { ["mode:none"] = "只执行击杀", ["mode:draw"] = "击杀后返还1士气并抽1张牌" }));
                steps.Add(CompositeStep("enemy-legion", "killTarget", "乾坤 阳：预先选择击杀目标",
                    PublicLegions(opponent).Where(card => card.BaseTroops <= 3000 && !card.Hidden)
                        .Select(card => card.InstanceId), 1));
                steps.Add(CompositeStep("resource-return", "drawCost", "乾坤 阳：预先选择返还的1张士气",
                    player.Morale.Select(card => card.InstanceId), 1, requiredChoice: "mode:draw"));
                break;

            case "S02-0521":
                steps.Add(CompositeStep("option", "mode", "荣耀之路：预先声明是否发动神力检索段",
                    ["mode:none", "mode:search"], 1, 1,
                    new() { ["mode:none"] = "只翻转士气", ["mode:search"] = "追加消耗并翻转2神力进行检索" }));
                steps.Add(CompositeStep("target-morale", "flipTargets", "荣耀之路：预先选择最多3张要翻转的士气",
                    player.Morale.Where(card => !card.IsGodPower).Select(card => card.InstanceId), 0, 3));
                steps.Add(CompositeStep("composite-glory-god-power-cost", "searchCost",
                    "荣耀之路：预先选择检索段消耗并翻转的2张神力", ["dynamic:1", "dynamic:2"], 2, 2,
                    requiredChoice: "mode:search"));
                break;

            case "S02-0620":
                steps.Add(CompositeStep("option", "mode", "符文之力：预先声明是否发动牌库查看段",
                    ["mode:none", "mode:search"], 1, 1,
                    new() { ["mode:none"] = "只获得1符文", ["mode:search"] = "追加消耗1士气查看牌库顶部3张" }));
                steps.Add(CompositeStep("composite-ordinary-payment", "searchCost", "符文之力：预先选择支付的1份资源",
                    CompositeOrdinaryPaymentChoices(player), 1, requiredChoice: "mode:search"));
                break;

            case "S02-0621":
                steps.Add(CompositeStep("option", "mode", "圆桌领域：预先声明是否发动兵力强化段",
                    ["mode:none", "mode:buff"], 1, 1,
                    new() { ["mode:none"] = "只执行牌库检索", ["mode:buff"] = "追加消耗1士气并强化军团" }));
                steps.Add(CompositeStep("field-legion", "buffTarget", "圆桌领域：预先选择强化的【圆桌骑士】军团",
                    PublicLegions(player).Where(card => card.HasTrait("圆桌骑士")).Select(card => card.InstanceId), 1,
                    requiredChoice: "mode:buff"));
                steps.Add(CompositeStep("composite-ordinary-payment", "buffCost", "圆桌领域：预先选择支付的1份资源",
                    CompositeOrdinaryPaymentChoices(player), 1, requiredChoice: "mode:buff"));
                break;

            case "S02-0207":
                steps.Add(CompositeStep("field-legion", "discardTargets", "沙漠君临：预先选择最多3张要弃置的我方军团",
                    PublicLegions(player).Select(card => card.InstanceId), 0, 3));
                steps.Add(CompositeStep("composite-desert-hand", "summonTarget",
                    "沙漠君临：预先选择天灾等级与弃置数量相同的【太阳城】军团", ["dynamic"], 1));
                steps.Add(CompositeStep("composite-desert-slot", "summonSlot", "沙漠君临：预先选择活跃登场的位置",
                    ["dynamic"], 1));
                break;

            case "S02-0307":
                steps.Add(CompositeStep("enemy-legion", "curseTarget", "海拉：预先选择兵力-3000的对方军团",
                    PublicLegions(opponent).Where(card => !card.Hidden).Select(card => card.InstanceId), 1));
                break;

            case "S02-0206":
                steps.Add(CompositeStep("field-legion", "buffTarget", "无畏的刺杀：预先选择我方前排1张【太阳城】军团",
                    player.Field[0].Where(card => card is not null && IsFieldLegion(card)
                            && card.Faction == "taiyangcheng" && !card.Hidden)
                        .Select(card => card!.InstanceId), 1));
                break;

            case "S02-0406":
                steps.Add(CompositeStep("option", "mode", "天下布武：预先声明本次效果模式",
                    ["mode:row-cost", "mode:front-attack", "mode:free-move"], 1, 1,
                    new()
                    {
                        ["mode:row-cost"] = "选择对方1排所有军团，本回合费用-2",
                        ["mode:front-attack"] = "本回合我方前排所有【高天原】军团进攻时兵力+1000",
                        ["mode:free-move"] = "本回合我方所有活跃的【高天原】军团可免费进行1格位移",
                    }));
                steps.Add(CompositeStep("option", "row", "天下布武：预先选择对方前排或后排",
                    ["row:0", "row:1"], 1, 1, requiredChoice: "mode:row-cost"));
                break;
        }

        if (steps.Count == 0) return CommandResult.Reject("该卡牌没有复合效果声明计划");
        return BeginPendingActivationSequence(playerIndex, source, ability, steps,
            triggerCandidateId: null, playCardInstanceId: source.InstanceId, responseTargetStackItemId: null);
    }

    private static L12ActivationSelectionStep CompositeStep(string kind, string key, string text,
        IEnumerable<string> choices, int min, int max = 1, Dictionary<string, string>? labels = null,
        string? requiredChoice = null, string? referenceKey = null, int minimumReferenceCount = 0,
        int referenceChoiceIndex = 0, bool autoSelectWhenExact = false)
        => new()
        {
            Kind = kind,
            DeclarationKey = key,
            Text = text,
            ValidChoices = choices.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MinChoose = min,
            MaxChoose = max,
            ChoiceLabels = labels ?? [],
            RequiredDeclaredChoice = requiredChoice,
            ReferenceDeclarationKey = referenceKey,
            MinimumReferenceCount = minimumReferenceCount,
            ReferenceChoiceIndex = referenceChoiceIndex,
            AutoSelectWhenExact = autoSelectWhenExact,
        };

    private IEnumerable<string> CompositeOrdinaryPaymentChoices(L12PlayerState player)
    {
        if (player.TemporaryMorale > 0) yield return "temporary-morale:1";
        foreach (var morale in player.Morale.Where(card => !card.Tapped)) yield return morale.InstanceId;
        foreach (var guard in ActiveTombGuardResources(player)) yield return guard.InstanceId;
    }

    private void CompleteCompositeHandPlayDeclaration(L12PendingActivation activation)
    {
        var player = State.Players[activation.Controller];
        var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == activation.PlayCardInstanceId
            && candidate.CardId == activation.SourceCardId);
        if (card is null)
        {
            AddEvent("ability-rejected", activation.Controller, "复合效果来源已不在手牌，未支付费用也未入栈");
            return;
        }
        var choice = EncodeCompositeDeclaration(activation.DeclaredValues);
        var result = PlayCard(activation.Controller, new L12Command("playCard", card.InstanceId, Choice: choice));
        if (!result.Accepted)
            AddEvent("ability-rejected", activation.Controller, result.Error ?? "复合效果声明失效，未支付费用也未入栈");
    }

    private void CompleteCommittedCompositeEffectDeclaration(L12PendingActivation activation)
    {
        var player = State.Players[activation.Controller];
        var source = player.Resolving.FirstOrDefault(card => card.InstanceId == activation.PlayCardInstanceId
            && card.CardId == activation.SourceCardId);
        var error = "复合战术来源已离开结算区";
        if (source is null || !ValidateCompositeHandPlayDeclaration(activation.Controller, source,
                activation.DeclaredValues, out error))
        {
            AbortCommittedCompositeEffectDeclaration(activation, error);
            return;
        }
        if (!TryCommitCompositePreStackCosts(activation.Controller, source, activation.DeclaredValues))
        {
            AbortCommittedCompositeEffectDeclaration(activation, "复合战术的发动费用已失效；未发生部分支付");
            return;
        }
        PushEffect(activation.Controller, source, "play", $"由其他效果免费打出的〈{source.Name}〉战术效果",
            CompositeFirstSegmentTargets(source.CardId, activation.DeclaredValues),
            CompositeFirstSegmentData(source.CardId, activation.DeclaredValues));
        ResumeCommittedCompositeParent(activation);
    }

    private void AbortCommittedCompositeEffectDeclaration(L12PendingActivation activation, string reason)
    {
        var player = State.Players[activation.Controller];
        var source = player.Resolving.FirstOrDefault(card => card.InstanceId == activation.PlayCardInstanceId);
        if (source is not null)
        {
            player.Resolving.Remove(source);
            ResetCardAfterLeavingField(source);
            player.Graveyard.Add(source);
        }
        AddEvent("ability-rejected", activation.Controller, reason);
        ResumeCommittedCompositeParent(activation);
    }

    private void ResumeCommittedCompositeParent(L12PendingActivation activation)
    {
        var parent = State.EffectStack.FirstOrDefault(item => item.StackItemId == activation.CommittedParentStackItemId);
        if (parent is null) return;
        if (activation.CommittedCompletion == "s2-limu-draw") PromptS2LiMuDraw(parent);
        else FinishStackItem(parent);
    }

    private static string EncodeCompositeDeclaration(IReadOnlyDictionary<string, List<string>> declared)
    {
        var json = JsonSerializer.Serialize(declared);
        return CompositePlanChoicePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static bool TryDecodeCompositeDeclaration(string? choice,
        out Dictionary<string, List<string>> declared)
    {
        declared = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (choice?.StartsWith(CompositePlanChoicePrefix, StringComparison.Ordinal) != true) return false;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(choice[CompositePlanChoicePrefix.Length..]));
            var decoded = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
            if (decoded is null) return false;
            declared = new Dictionary<string, List<string>>(decoded, StringComparer.OrdinalIgnoreCase);
            return true;
        }
        catch (FormatException) { return false; }
        catch (JsonException) { return false; }
    }

    private bool ValidateCompositeHandPlayDeclaration(int controller, L12CardInstance card,
        IReadOnlyDictionary<string, List<string>> declared, out string error)
    {
        error = "复合效果的模式、目标或费用对象已失效";
        var player = State.Players[controller];
        var opponent = State.Players[1 - controller];
        var mode = declared.GetValueOrDefault("mode", []).SingleOrDefault();
        bool Enemy(string key, Func<L12CardInstance, bool>? predicate = null)
        {
            var id = declared.GetValueOrDefault(key, []).SingleOrDefault();
            return PublicLegions(opponent).Any(target => target.InstanceId == id && !target.Hidden
                && (predicate?.Invoke(target) ?? true));
        }
        bool Own(string key, Func<L12CardInstance, bool>? predicate = null)
        {
            var id = declared.GetValueOrDefault(key, []).SingleOrDefault();
            return PublicLegions(player).Any(target => target.InstanceId == id && (predicate?.Invoke(target) ?? true));
        }
        bool GodPowerCost(string key, int count)
        {
            var ids = declared.GetValueOrDefault(key, []);
            return ids.Count == count && ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == count
                && ids.All(id => player.Morale.Any(resource => resource.InstanceId == id && resource.IsGodPower && !resource.Tapped));
        }
        bool OrdinaryCost(string key)
        {
            var id = declared.GetValueOrDefault(key, []).SingleOrDefault();
            return id == "temporary-morale:1" ? player.TemporaryMorale > 0
                : id is not null && CompositeOrdinaryPaymentChoices(player).Contains(id, StringComparer.OrdinalIgnoreCase);
        }
        bool OrdinaryCosts(string key, int count)
        {
            var ids = declared.GetValueOrDefault(key, []);
            if (ids.Count != count || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != count) return false;
            var selected = ids.Where(id => id != "temporary-morale:1").ToArray();
            return ids.Contains("temporary-morale:1", StringComparer.OrdinalIgnoreCase) == (player.TemporaryMorale > 0)
                && CanConsumeSelectedResources(player, count, selected);
        }
        bool EnemyMany(string key, int maximum, Func<L12CardInstance, bool>? predicate = null)
        {
            var ids = declared.GetValueOrDefault(key, []);
            return ids.Count is > 0 && ids.Count <= maximum
                && ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == ids.Count
                && ids.All(id => PublicLegions(opponent).Any(target => target.InstanceId == id && !target.Hidden
                    && (predicate?.Invoke(target) ?? true)));
        }
        bool Grave(string key, Func<L12CardInstance, bool>? predicate = null)
        {
            var id = declared.GetValueOrDefault(key, []).SingleOrDefault();
            return player.Graveyard.Any(target => target.InstanceId == id && (predicate?.Invoke(target) ?? true));
        }
        bool OwnSlot(string key, int requiredRow = -1)
        {
            var slotText = declared.GetValueOrDefault(key, []).SingleOrDefault();
            if (slotText?.Split(':') is not [var rowText, var slotValue]
                || !int.TryParse(rowText, out var row) || !int.TryParse(slotValue, out var slot)
                || row is < 0 or > 1 || slot is < 0 or > 2 || requiredRow >= 0 && row != requiredRow) return false;
            return player.Field[row][slot] is null;
        }

        var valid = card.CardId switch
        {
            "S01-0005" => declared.GetValueOrDefault("volleyMode", []).SingleOrDefault() is { } volleyMode
                && volleyMode is "mode:front" or "mode:back" or "mode:single"
                && (volleyMode != "mode:single" || Enemy("singleTarget")),
            "S01-0006" => declared.GetValueOrDefault("discardCost", []) is [var discardId]
                && discardId != card.InstanceId && player.Hand.Any(candidate => candidate.InstanceId == discardId),
            "S01-0009" => Own("returnTarget") && Own("buffTarget"),
            "S01-0010" => ValidateForgedOrdersDeclaration(opponent, declared),
            "S01-0011" => declared.GetValueOrDefault("lockTarget", []).SingleOrDefault() is { } lockTarget
                && (PublicLegions(opponent).Any(target => target.InstanceId == lockTarget && !target.Hidden)
                    || opponent.Morale.Any(target => target.InstanceId == lockTarget)),
            "S01-0118" => (mode is "mode:none" or "mode:kill") && Own("buffTarget", target =>
                    FindOnField(player, target.InstanceId, out var row, out _) is not null && row == 0)
                && (mode == "mode:none" || declared.GetValueOrDefault("marchCost", []) is { Count: 2 } marchCost
                    && marchCost.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2
                    && marchCost.All(id => player.Morale.Any(resource => resource.InstanceId == id))
                    && Enemy("killTarget", target => target.Troops <= 6000)),
            "S01-0221" => declared.GetValueOrDefault("duatMode", []).SingleOrDefault() is { } duatMode
                && duatMode is "mode:kill" or "mode:recover"
                && (duatMode != "mode:kill" || Enemy("killTarget", target => target.Troops <= 5000))
                && (duatMode != "mode:recover" || declared.GetValueOrDefault("recoverTarget", []).SingleOrDefault() is { } recover
                    && (recover == "mode:none" || Grave("recoverTarget", target => target.CardId != card.CardId
                        && L12StructuredCardRules.HasFaction(player, target, "taiyangcheng") && CanEnterHandOrLibrary(target)))),
            "S01-0318" => declared.GetValueOrDefault("masterDamageCost", []).SingleOrDefault() == "cost:master-damage"
                && Grave("entryCard", target => target.CardType == "legion" && target.CurrentCost <= 5
                    && L12StructuredCardRules.HasFaction(player, target, "asgard")) && OwnSlot("entrySlot"),
            "S01-0319" => declared.GetValueOrDefault("graveCost", []) is { Count: 4 } graveCost
                && graveCost.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4
                && graveCost.All(id => player.Graveyard.Any(target => target.InstanceId == id && CanEnterHandOrLibrary(target)))
                && Enemy("killTarget", target => target.Troops <= 6000),
            "S02-0009" => ValidateDefenseDeploymentDeclaration(player, card, declared),
            "S02-0010" => declared.GetValueOrDefault("disasterMode", []).SingleOrDefault() is "-1" or "0" or "1"
                && (mode is "mode:none" or "mode:morale")
                && (mode == "mode:none" || OrdinaryCosts("lotusCost", 3)),
            "S02-0011" => EnemyMany("killTargets", 3, target => target.BaseTroops <= 2000),
            "S02-0013" => declared.GetValueOrDefault("artifactTarget", []).SingleOrDefault() is { } artifactId
                && new[] { opponent.Relic }.Concat(opponent.ExtraRelics)
                    .Any(target => target?.InstanceId == artifactId && target.CardType == "artifact"),
            "S02-0522" => mode is "mode:none" or "mode:second"
                && Enemy("primaryTarget")
                && (mode == "mode:none" || GodPowerCost("secondCost", 1) && Enemy("secondaryTarget")),
            "S02-0105" => mode is "mode:none" or "mode:draw"
                && Enemy("killTarget", target => target.BaseTroops <= 3000)
                && (mode == "mode:none" || declared.GetValueOrDefault("drawCost", []).SingleOrDefault() is { } moraleId
                    && player.Morale.Any(resource => resource.InstanceId == moraleId)),
            "S02-0521" => mode is "mode:none" or "mode:search"
                && declared.GetValueOrDefault("flipTargets", []).Count <= 3
                && declared.GetValueOrDefault("flipTargets", []).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    == declared.GetValueOrDefault("flipTargets", []).Count
                && declared.GetValueOrDefault("flipTargets", []).All(id => player.Morale.Any(resource => resource.InstanceId == id && !resource.IsGodPower))
                && (mode == "mode:none" || ValidateGloryPlannedCost(player, declared)),
            "S02-0620" => mode is "mode:none" or "mode:search"
                && (mode == "mode:none" || OrdinaryCost("searchCost")),
            "S02-0621" => mode is "mode:none" or "mode:buff"
                && (mode == "mode:none" || Own("buffTarget", target => target.HasTrait("圆桌骑士")) && OrdinaryCost("buffCost")),
            "S02-0207" => ValidateDesertDeclaration(player, card, declared),
            "S02-0307" => player.Library.Count >= 1 && Enemy("curseTarget"),
            "S02-0206" => Own("buffTarget", target => target.Faction == "taiyangcheng"
                && FindOnField(player, target.InstanceId, out var row, out _) is not null && row == 0),
            "S02-0406" => mode is "mode:row-cost" or "mode:front-attack" or "mode:free-move"
                && (mode != "mode:row-cost" || declared.GetValueOrDefault("row", []).SingleOrDefault() is "row:0" or "row:1"),
            _ => false,
        };
        return valid;
    }

    private bool ValidateForgedOrdersDeclaration(L12PlayerState opponent,
        IReadOnlyDictionary<string, List<string>> declared)
    {
        var targets = declared.GetValueOrDefault("moveTargets", []);
        if (targets.Count is < 1 or > 2 || targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count)
            return false;
        for (var index = 0; index < targets.Count; index++)
        {
            var target = FindOnField(opponent, targets[index], out var row, out var slot);
            var declaredSlot = declared.GetValueOrDefault($"moveSlot{index + 1}", []).SingleOrDefault();
            if (target is null || target.Hidden || declaredSlot != $"{1 - row}:{slot}"
                || opponent.Field[1 - row][slot] is not null
                || State.ActiveDisaster?.CardId == "S01-DS03" && 1 - row == 1) return false;
        }
        return true;
    }

    private bool ValidateDefenseDeploymentDeclaration(L12PlayerState player, L12CardInstance source,
        IReadOnlyDictionary<string, List<string>> declared)
    {
        var cards = declared.GetValueOrDefault("entryCards", []);
        if (cards.Count > 2 || cards.Distinct(StringComparer.OrdinalIgnoreCase).Count() != cards.Count
            || cards.Any(id => player.Hand.All(candidate => candidate.InstanceId != id
                || candidate.InstanceId == source.InstanceId || !IsCounterTactic(candidate.CardId)))) return false;
        var slots = Enumerable.Range(1, cards.Count)
            .Select(index => declared.GetValueOrDefault($"entrySlot{index}", []).SingleOrDefault()).ToArray();
        return slots.Length == slots.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            && slots.All(slotText => slotText?.Split(':') is ["1", var slotValue]
                && int.TryParse(slotValue, out var slot) && slot is >= 0 and <= 2 && player.Field[1][slot] is null);
    }

    private bool TryCommitCompositePreStackCosts(int controller, L12CardInstance source,
        IReadOnlyDictionary<string, List<string>>? declared)
    {
        var player = State.Players[controller];
        if (source.CardId == "S02-0307")
        {
            var result = L12LibraryOps.Mill(player, 1);
            if (!result.Success) return false;
            var discarded = result.Cards[0];
            AddEvent("cost", controller, $"〈{source.Name}〉弃置牌库顶部1张牌作为发动费用", source, discarded);
            NotifyCardDiscarded(player, discarded, "library", causedByEffect: false);
        }
        var declaration = declared ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var segments = L12CompositeEffectPlans.Segments(source.CardId);
        var mode = declaration.GetValueOrDefault("mode", []).SingleOrDefault();
        var preStackCosts = segments.Where(segment => segment.PreStackCost
            && (segment.RequiredMode is null || segment.RequiredMode == mode)).ToArray();
        if (preStackCosts.Length > 0)
            return preStackCosts.All(segment => TryPayCompositeDeclaredCost(controller, source, segment, declaration));
        var first = segments.FirstOrDefault();
        return first is null || TryPayCompositeDeclaredCost(controller, source, first, declaration);
    }

    private static bool ValidateGloryPlannedCost(L12PlayerState player,
        IReadOnlyDictionary<string, List<string>> declared)
    {
        var flipIds = declared.GetValueOrDefault("flipTargets", []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var costIds = declared.GetValueOrDefault("searchCost", []);
        return costIds.Count == 2 && costIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2
            && costIds.All(id => player.Morale.Any(resource => resource.InstanceId == id && !resource.Tapped
                && (resource.IsGodPower || flipIds.Contains(id))));
    }

    private bool ValidateDesertDeclaration(L12PlayerState player, L12CardInstance source,
        IReadOnlyDictionary<string, List<string>> declared)
    {
        var discards = declared.GetValueOrDefault("discardTargets", []);
        if (discards.Count > 3 || discards.Distinct(StringComparer.OrdinalIgnoreCase).Count() != discards.Count
            || discards.Any(id => FindOnField(player, id, out _, out _) is not { } card || !IsFieldLegion(card)))
            return false;
        var summonId = declared.GetValueOrDefault("summonTarget", []).SingleOrDefault();
        var summon = player.Hand.FirstOrDefault(card => card.InstanceId == summonId && card.InstanceId != source.InstanceId
            && card.CardType == "legion" && card.Faction == "taiyangcheng" && card.DisasterLevel == discards.Count);
        var slotText = declared.GetValueOrDefault("summonSlot", []).SingleOrDefault();
        if (summon is null || slotText?.Split(':') is not [var rowText, var slotValue]
            || !int.TryParse(rowText, out var row) || !int.TryParse(slotValue, out var slot)
            || row is < 0 or > 1 || slot is < 0 or > 2) return false;
        var occupant = player.Field[row][slot];
        return occupant is null || discards.Contains(occupant.InstanceId, StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, string> CompositeFirstSegmentData(string cardId,
        IReadOnlyDictionary<string, List<string>> declared)
    {
        var segments = L12CompositeEffectPlans.Segments(cardId);
        var data = new Dictionary<string, string>
        {
            ["compositePlan"] = cardId,
            ["compositeSegment"] = "0",
            ["atomicFlow"] = segments[0].Flow,
            ["atomicContinuation"] = "true",
        };
        foreach (var pair in declared) data[$"declared:{pair.Key}"] = string.Join('|', pair.Value);
        return data;
    }

    private static string[] CompositeFirstSegmentTargets(string cardId,
        IReadOnlyDictionary<string, List<string>> declared)
        => CompositeSegmentTargets(L12CompositeEffectPlans.Segments(cardId)[0], declared);

    private static string[] CompositeSegmentTargets(L12CompositeEffectSegmentSpec segment,
        IReadOnlyDictionary<string, List<string>> declared)
        => (segment.PublicTargetKeys ?? []).SelectMany(key => declared.GetValueOrDefault(key, []))
            .Where(value => !value.StartsWith("mode:", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string[] CompositeDeclared(L12StackItem item, string key)
        => item.Data.GetValueOrDefault($"declared:{key}", string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries);

    private static (string[] ResourceIds, int TemporaryMorale) CompositeReservedBasePayment(
        IReadOnlyDictionary<string, List<string>>? declared)
    {
        if (declared is null) return ([], 0);
        var selected = new[] { "flipTargets", "secondCost", "drawCost", "searchCost", "buffCost", "lotusCost" }
            .SelectMany(key => declared.GetValueOrDefault(key, []))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return (selected.Where(id => id != "temporary-morale:1").ToArray(),
            selected.Contains("temporary-morale:1", StringComparer.OrdinalIgnoreCase) ? 1 : 0);
    }

    private bool QueueNextCompositeSegment(L12StackItem item, L12CardInstance? source)
    {
        var planId = item.Data.GetValueOrDefault("compositePlan");
        if (source is null || string.IsNullOrWhiteSpace(planId)
            || !int.TryParse(item.Data.GetValueOrDefault("compositeSegment"), out var current)) return false;
        var segments = L12CompositeEffectPlans.Segments(planId);
        var mode = CompositeDeclared(item, "mode").SingleOrDefault();
        for (var nextIndex = current + 1; nextIndex < segments.Count; nextIndex++)
        {
            var next = segments[nextIndex];
            if (next.RequiredMode is not null && mode != next.RequiredMode) continue;
            if (!ValidateCompositeSegmentTargets(item.Controller, next.Flow, item))
            {
                AddEvent("effect-cancelled", item.Controller,
                    $"〈{source.Name}〉的“{next.Text}”因公开目标已失效而取消；其余独立段继续", source);
                continue;
            }
            if (!next.PreStackCost && !TryPayCompositeSegmentCost(item.Controller, source, next, item))
            {
                AddEvent("effect-cancelled", item.Controller,
                    $"〈{source.Name}〉的“{next.Text}”因费用对象或公开目标失效而取消；未发生部分支付，其余独立段继续", source);
                continue;
            }
            var data = new Dictionary<string, string>(item.Data, StringComparer.OrdinalIgnoreCase)
            {
                ["compositeSegment"] = nextIndex.ToString(),
                ["atomicFlow"] = next.Flow,
                ["atomicContinuation"] = "true",
            };
            PushEffect(item.Controller, source, item.Trigger, next.Text,
                CompositeSegmentTargets(next, item.Data.Where(pair => pair.Key.StartsWith("declared:", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(pair => pair.Key["declared:".Length..], pair => pair.Value
                        .Split('|', StringSplitOptions.RemoveEmptyEntries).ToList(), StringComparer.OrdinalIgnoreCase)), data);
            return true;
        }
        return false;
    }

    private bool ValidateCompositeSegmentTargets(int controller, string flow, L12StackItem item)
        => flow switch
        {
            "nyx-secondary" => DeclaredEnemyTarget(controller,
                CompositeDeclared(item, "secondaryTarget").SingleOrDefault()) is not null,
            "round-table-buff" => FindOnField(State.Players[controller],
                    CompositeDeclared(item, "buffTarget").SingleOrDefault(), out _, out _) is { } target
                && target.HasTrait("圆桌骑士"),
            "march-kill-effect" => DeclaredEnemyTarget(controller,
                CompositeDeclared(item, "killTarget").SingleOrDefault(), target => target.Troops <= 6000) is not null,
            "yomi-kill3" => CompositeDeclared(item, "kill3Target").SingleOrDefault() is { } kill3
                && (kill3 == "mode:none" || DeclaredEnemyTarget(controller, kill3, card => card.CurrentCost <= 3) is not null),
            "yomi-kill1" => CompositeDeclared(item, "kill1Target").SingleOrDefault() is { } kill1
                && (kill1 == "mode:none" || DeclaredEnemyTarget(controller, kill1, card => card.CurrentCost <= 1) is not null),
            "wisdom-recover" => CompositeDeclared(item, "recoverTarget").SingleOrDefault() is { } wisdom
                && State.Players[controller].Graveyard.Any(card => card.InstanceId == wisdom
                    && card.InstanceId != item.SourceInstanceId && card.CurrentCost <= 3
                    && card.CardType is "tactic" or "artifact"),
            "blood-eagle-recover" => CompositeDeclared(item, "graveOrder") is [var handCard, var bottomCard]
                && !handCard.Equals(bottomCard, StringComparison.OrdinalIgnoreCase)
                && new[] { handCard, bottomCard }.Any(id => State.Players[controller].Graveyard.Any(card =>
                    card.InstanceId == id && card.InstanceId != item.SourceInstanceId
                    && CanEnterHandOrLibrary(card)
                    && L12StructuredCardRules.HasFaction(State.Players[controller], card, "asgard"))),
            _ => true,
        };

    private bool TryPayCompositeSegmentCost(int controller, L12CardInstance source,
        L12CompositeEffectSegmentSpec segment, L12StackItem item)
    {
        if (segment.Cost == 0 || segment.CostKey is null || segment.CostKind is null) return true;
        var declared = item.Data.Where(pair => pair.Key.StartsWith("declared:", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key["declared:".Length..], pair => pair.Value
                .Split('|', StringSplitOptions.RemoveEmptyEntries).ToList(), StringComparer.OrdinalIgnoreCase);
        return TryPayCompositeDeclaredCost(controller, source, segment, declared);
    }

    private bool TryPayCompositeDeclaredCost(int controller, L12CardInstance source,
        L12CompositeEffectSegmentSpec segment, IReadOnlyDictionary<string, List<string>> declared)
    {
        if (segment.Cost == 0 || segment.CostKey is null || segment.CostKind is null) return true;
        var player = State.Players[controller];
        var ids = declared.GetValueOrDefault(segment.CostKey, []).ToArray();
        switch (segment.CostKind)
        {
            case "god-power-flip":
            {
                var resources = player.Morale.Where(card => ids.Contains(card.InstanceId, StringComparer.OrdinalIgnoreCase)
                    && card.IsGodPower && !card.Tapped).ToArray();
                if (resources.Length != segment.Cost || resources.Select(card => card.InstanceId)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count() != segment.Cost) return false;
                foreach (var resource in resources) { resource.Tapped = true; resource.IsGodPower = false; }
                AddEvent("cost", controller, $"〈{source.Name}〉消耗并翻转{segment.Cost}神力", source);
                return true;
            }
            case "morale-return":
                return ids.Length == segment.Cost && ReturnSelectedMoraleById(player, ids, segment.Cost);
            case "ordinary-payment":
            {
                var selected = ids.Where(id => id != "temporary-morale:1").ToArray();
                return TryConsumeSelectedResources(player, segment.Cost, selected);
            }
            case "discard-hand":
            {
                var costs = player.Hand.Where(card => ids.Contains(card.InstanceId, StringComparer.OrdinalIgnoreCase)
                    && card.InstanceId != source.InstanceId).ToArray();
                if (costs.Length != segment.Cost || costs.Select(card => card.InstanceId)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count() != segment.Cost) return false;
                foreach (var cost in costs) MoveHandToGrave(player, cost.InstanceId, causedByEffect: false);
                AddEvent("cost", controller, $"〈{source.Name}〉弃置{segment.Cost}张手牌作为发动费用",
                    [source, .. costs]);
                return true;
            }
            case "conditional-master-damage":
                if (player.Hp > 5) DamageMaster(controller, segment.Cost, $"〈{source.Name}〉的发动费用");
                else AddEvent("cost", controller, $"〈{source.Name}〉的主宰伤害费用因血量不高于5而不减少血量", source);
                return State.Phase != L12Phase.GameOver;
            case "grave-bottom":
            {
                var costs = ids.Select(id => player.Graveyard.FirstOrDefault(card => card.InstanceId == id
                    && CanEnterHandOrLibrary(card))).ToArray();
                if (costs.Length != segment.Cost || costs.Any(card => card is null)
                    || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != segment.Cost) return false;
                MoveGraveToLibraryBottom(player, costs.Cast<L12CardInstance>());
                AddEvent("cost", controller, $"〈{source.Name}〉将墓地{segment.Cost}张卡牌依声明顺序置于牌库底部", source);
                return true;
            }
            default:
                return false;
        }
    }
}
