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
    bool PreStackCost = false,
    string? RequiredDeclarationKey = null);

/// <summary>
/// 多段卡效的权威计划。卡牌差异只存在于这份声明数据；通用运行时负责在支付前
/// 收齐公开模式、目标与费用对象，并让每个独立效果段各自进入堆叠和响应窗口。
/// </summary>
internal static partial class L12CompositeEffectPlans
{
    // 同一能力可以包含多个按顺序结算的子句，但只有卡面明确写成独立效果时，
    // 才为每个子句分别开放响应。这里登记“整项能力共用一次响应”的计划，
    // 避免再次用卡号分支散落到结算器中。
    private static readonly HashSet<string> SingleResponseEffectPlans = new(StringComparer.OrdinalIgnoreCase)
    {
        "active:S01-04M1:amaterasuReady",
    };

    internal static bool UsesSingleResponseEffect(string? planId)
        => !string.IsNullOrWhiteSpace(planId) && SingleResponseEffectPlans.Contains(planId);

    private static readonly IReadOnlyDictionary<string, L12CompositeEffectSegmentSpec[]> HandPlayPlans =
        new Dictionary<string, L12CompositeEffectSegmentSpec[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0005"] =
            [
                new("volley-effect", "执行已选择的〈万箭齐发〉效果", PublicTargetKeys: ["singleTarget"]),
            ],
            ["S01-0006"] =
            [
                new("evil-ritual-effect", "对对方主宰造成1点非致命伤害",
                    CostKind: "discard-hand", CostKey: "discardCost", Cost: 1, PreStackCost: true),
            ],
            ["S01-0007"] =
            [
                new("camp-search", "查看牌库顶部3张牌，选择军团并排列其余牌"),
                new("camp-heal", "消耗1士气：我方主宰增加1点血量",
                    "mode:heal", "ordinary-payment", "campHealCost", 1, PreStackCost: true,
                    RequiredDeclarationKey: "campMode"),
                new("camp-draw", "消耗1士气：抽取1张牌",
                    "mode:draw", "ordinary-payment", "campDrawCost", 1, PreStackCost: true,
                    RequiredDeclarationKey: "campMode"),
            ],
            ["S01-0009"] =
            [
                new("strategic-transfer-effect", "依次结算已选择的回手与兵力+2000目标",
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
            ["S01-0013"] =
            [
                new("scout-reveal", "查看对方所有手牌"),
                new("scout-shuffle-effect", "消耗1士气：令对方选择1张手牌洗回牌库",
                    "mode:use", "ordinary-payment", "scoutCost", 1, PreStackCost: true),
            ],
            ["S01-0014"] =
            [
                new("ritual-draw", "抽取1张牌"),
                new("ritual-disaster", "将天灾值增加或减少已声明的数值"),
            ],
            ["S01-0015"] =
            [
                new("peace-draw", "我方抽取1张牌"),
                new("peace-negotiation", "询问对方是否议和谈判"),
            ],
            ["S01-0118"] =
            [
                new("march-buff-effect", "选择我方前排1张军团，本回合兵力+2000",
                    PublicTargetKeys: ["buffTarget"]),
            ],
            ["S01-0119"] =
            [
                new("observing-stars-reorder", "查看并排列牌库顶部5张牌"),
                new("observing-stars-morale", "从士气牌库追加1张活跃士气", "mode:morale"),
            ],
            ["S01-0221"] =
            [
                new("duat-effect", "执行已选择的〈杜阿特之门〉效果",
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
            ["S01-0419"] =
            [
                new("oiran-search", "查看牌库顶部3张牌，将符合条件的1张加入手牌并排列其余牌"),
                new("oiran-ready-morale", "将已声明的1张休整士气转为活跃", "mode:morale",
                    PublicTargetKeys: ["moraleTarget"]),
            ],
            ["S01-0418"] =
            [
                new("divine-punishment-effect", "击杀已声明的费用不高于7军团",
                    PublicTargetKeys: ["killTarget"]),
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
            ["S02-0306"] =
            [
                new("mimir-recover-draw", "我方主宰增加1点血量并抽取1张牌"),
                new("mimir-mill", "弃置我方牌库顶部2张牌", "mode:mill"),
            ],
            ["S02-0405"] =
            [
                new("fortune-search", "查看牌库顶部5张牌并处理圣物、上杉谦信与牌库底顺序"),
                new("fortune-next-uesugi", "我方本回合打出的下一张〈上杉谦信〉费用-2且获得冲锋"),
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
                new("fearless-assassination", "无畏的刺杀：使已选择的我方前排【太阳城】军团本回合兵力+3000并获得必中"),
            ],
            ["S02-0406"] =
            [
                new("tenka-effect", "天下布武：执行已选择的效果"),
            ],
        };

    private static readonly IReadOnlyDictionary<string, L12CompositeEffectSegmentSpec[]> ActivePlans =
        new Dictionary<string, L12CompositeEffectSegmentSpec[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["active:S01-02D1:sunTopThree"] =
            [
                new("sun-top-three-search", "众神之乡：公开并处理牌库顶部3张牌"),
                new("sun-top-three-recover", "众神之乡：随后将已声明的墓地太阳城卡牌加入手牌",
                    "mode:recover", PublicTargetKeys: ["graveCard"], RequiredDeclarationKey: "recoverMode"),
            ],
            ["active:S01-03D1:valhallaRecover"] =
            [
                new("valhalla-mill", "英灵殿：弃置牌库顶部2张牌"),
                new("valhalla-recover", "英灵殿：随后将已声明的墓地阿斯加德卡牌加入手牌",
                    "mode:recover", PublicTargetKeys: ["graveCard"], RequiredDeclarationKey: "recoverMode"),
            ],
            ["active:S01-0105:searchBrothers"] =
            [
                new("liubei-search", "刘备：检索〈关羽〉或〈张飞〉，展示并加入手牌"),
                new("liubei-shuffle", "刘备：随后重洗牌库"),
            ],
            ["active:S01-0116:xishiExchange"] =
            [
                new("xishi-summon", "西施：将已声明的其他军团活跃登场",
                    "mode:summon", PublicTargetKeys: ["entryCard", "entrySlot"],
                    RequiredDeclarationKey: "summonMode"),
                new("xishi-draw", "西施：随后抽取1张牌"),
            ],
            ["active:S01-01M1:drawCycle"] =
            [
                new("yangjian-draw", "杨戬：抽取1张牌"),
                new("yangjian-return", "杨戬：随后将1张手牌放回牌库顶部或底部"),
            ],
            ["active:S01-01D1:palaceReward"] =
            [
                new("palace-reward-morale", "凌霄宝殿：从士气牌库追加2张休整士气"),
                new("palace-reward-draw", "凌霄宝殿：随后抽取1张牌"),
            ],
            ["active:S01-01D1:palaceExchange"] =
            [
                new("palace-exchange-kill", "凌霄宝殿：击杀已声明的对方军团",
                    PublicTargetKeys: ["enemyTarget"]),
                new("palace-exchange-revive", "凌霄宝殿：随后令已声明的【天廷】军团活跃登场",
                    "mode:revive", PublicTargetKeys: ["entryCard", "entrySlot"],
                    RequiredDeclarationKey: "reviveMode"),
            ],
            ["active:S01-04D1:yomiSweep"] =
            [
                new("yomi-draw", "黄泉之门：抽取1张牌"),
                new("yomi-cost-debuff", "黄泉之门：对方所有军团本回合费用-1"),
                new("yomi-kill3", "黄泉之门：结算已声明的费用不高于3击杀目标"),
                new("yomi-kill1", "黄泉之门：结算已声明的费用不高于1击杀目标"),
            ],
            ["active:S01-04M1:amaterasuKill"] =
            [
                new("amaterasu-debuff", "天照大神：令已声明的对方军团本回合费用-1",
                    PublicTargetKeys: ["debuffTarget"]),
                new("amaterasu-kill", "天照大神：随后击杀已声明的费用为0军团",
                    PublicTargetKeys: ["killTarget"]),
            ],
            ["active:S01-04M1:amaterasuReady"] =
            [
                new("amaterasu-ready", "天照大神：将已声明的最多2张士气转为活跃",
                    PublicTargetKeys: ["moraleTargets"]),
                new("amaterasu-front-buff", "天照大神：我方前排所有【高天原】军团本回合兵力+1000"),
            ],
            ["active:S02-05D1:divinityRecover"] =
            [
                new("divinity-recover", "奥林匹斯 诸神巅：将已声明的墓地卡牌加入手牌",
                    PublicTargetKeys: ["recoverCard"]),
                new("divinity-entry", "奥林匹斯 诸神巅：随后令已声明的军团活跃登场",
                    RequiredMode: "mode:entry", PublicTargetKeys: ["entryCard", "entrySlot"],
                    RequiredDeclarationKey: "entryMode"),
            ],
        };

    private static readonly IReadOnlyDictionary<string, L12CompositeEffectSegmentSpec[]> ResponseAndTriggerPlans =
        new Dictionary<string, L12CompositeEffectSegmentSpec[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["trigger:S01-0201:attack"] =
            [
                new("thutmose-debuff", "图特摩斯三世：对方所有军团本回合兵力-1000"),
                new("thutmose-kill", "图特摩斯三世：随后击杀已声明的兵力不高于1000军团",
                    "mode:kill", PublicTargetKeys: ["killTarget"], RequiredDeclarationKey: "killMode"),
            ],
            ["trigger:S01-0201:death"] =
            [
                new("thutmose-debuff", "图特摩斯三世：对方所有军团本回合兵力-1000"),
                new("thutmose-kill", "图特摩斯三世：随后击杀已声明的兵力不高于1000军团",
                    "mode:kill", PublicTargetKeys: ["killTarget"], RequiredDeclarationKey: "killMode"),
            ],
            ["trigger:S01-0401:attack"] =
            [
                new("honda-debuff", "本多忠胜：对方所有军团本回合费用-1"),
                new("honda-kill", "本多忠胜：随后击杀已声明的费用为0军团",
                    PublicTargetKeys: ["killTarget"]),
            ],
            ["trigger:S01-0216:enter"] =
            [
                new("canopic-box-search", "卡诺匹斯箱：检索、展示并加入1张卡诺匹斯罐，随后重洗牌库"),
                new("canopic-box-heal-discard", "卡诺匹斯箱：随后主宰增加1点血量并弃置此圣物"),
            ],
            ["trigger:S01-0218:enter"] =
            [
                new("canopic-two-free", "卡诺匹斯罐二：本回合从手牌打出的下1张战术无需费用"),
                new("canopic-two-discard", "卡诺匹斯罐二：随后弃置此圣物"),
            ],
            ["trigger:S01-0219:enter"] =
            [
                new("canopic-three-morale", "卡诺匹斯罐三：本回合获得2点临时士气"),
                new("canopic-three-discard", "卡诺匹斯罐三：随后弃置此圣物"),
            ],
            ["trigger:S01-0001:enter"] =
            [
                new("teach-enter-discard", "黑胡子蒂奇：双方各弃置合计2张手牌"),
                new("teach-enter-draw", "黑胡子蒂奇：随后我方抽取2张牌，对方抽取1张牌"),
            ],
            ["response:S01-0020"] =
            [
                new("battle-until-dawn-buff", "我方所有军团本回合兵力+1000"),
                new("battle-until-dawn-draw", "若墓地卡牌数量不低于5，可抽取1张牌",
                    "mode:draw", RequiredDeclarationKey: "drawMode"),
            ],
            ["response:S01-0120"] =
            [
                new("empty-city-block", "返还1士气：抵挡本次进攻"),
                new("empty-city-draw", "若我方前排没有军团，可抽取1张牌",
                    "mode:draw", RequiredDeclarationKey: "drawMode"),
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
            ["trigger:S02-0304:margaretMasterDamage"] =
            [
                new("margaret-heal", "玛格丽特一世：我方主宰增加1点血量"),
                new("margaret-heal-lock", "玛格丽特一世：随后本回合我方主宰血量无法因军团效果增加"),
            ],
            ["trigger:S02-0102:enter"] =
            [
                new("limu-reveal", "李牧：展示牌库顶部1张牌并处理其免费打出或返回牌库底部",
                    RequiredMode: "mode:use", RequiredDeclarationKey: "revealMode"),
                new("limu-draw", "李牧：随后可抽取1张牌",
                    RequiredMode: "mode:use", RequiredDeclarationKey: "drawMode"),
            ],
            ["trigger:S02-0101:enter"] =
            [
                new("yingzheng-kill", "始皇帝 嬴政：击杀除此军团以外的所有军团"),
                new("yingzheng-return", "始皇帝 嬴政：随后返还所有士气并限制本回合追加士气"),
            ],
            ["trigger:S01-0111:enter"] =
            [
                new("zhuge-reveal", "诸葛亮：查看下一张天灾"),
                new("zhuge-disaster", "诸葛亮：随后将天灾值增加或减少1",
                    RequiredMode: "mode:use", RequiredDeclarationKey: "disasterMode"),
            ],
            ["trigger:S01-0217:enter"] =
            [
                new("canopic-one", "卡诺匹斯罐一：使已选择的太阳城军团本回合兵力+2000并获得强攻"),
                new("canopic-one-discard", "卡诺匹斯罐一：随后弃置此圣物"),
            ],
            ["trigger:S01-0220:enter"] =
            [
                new("canopic-four", "卡诺匹斯罐四：令已声明的太阳城军团获得免死"),
                new("canopic-four-discard", "卡诺匹斯罐四：随后弃置此圣物"),
            ],
            ["response:S02-0016"] =
            [
                new("ruined-ritual", "执行已选择的弃置手牌或登场效果无效效果"),
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

    private static readonly HashSet<string> HandPlayPlansWithoutControllerDeclaration =
        new(StringComparer.OrdinalIgnoreCase) { "S01-0015", "S02-0405" };

    public static bool HasHandPlayPlan(string cardId)
        => HandPlayPlans.ContainsKey(cardId)
            || L12StructuredCardRules.StarterHandPlayPlanId(cardId) is { } starter
                && StarterHandPlayPlans.ContainsKey(starter);

    public static bool RequiresHandPlayDeclaration(string cardId)
        => L12StructuredCardRules.StarterHandPlayPlanId(cardId) is not null
            || HasHandPlayPlan(cardId) && !HandPlayPlansWithoutControllerDeclaration.Contains(cardId);

    public static bool RequiresTriggerDeclaration(string cardId, string trigger)
        => cardId.Equals("S02-0516", StringComparison.OrdinalIgnoreCase)
            && trigger.Equals("attack", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<L12CompositeEffectSegmentSpec> Segments(string cardId)
        => L12StructuredCardRules.StarterHandPlayPlanId(cardId) is { } starterPlan
            && StarterHandPlayPlans.TryGetValue(starterPlan, out var starter) ? starter
            : StarterContinuationPlans.TryGetValue(cardId, out var starterContinuation) ? starterContinuation
            : HandPlayPlans.TryGetValue(cardId, out var handPlay) ? handPlay
            : ActivePlans.TryGetValue(cardId, out var active) ? active
            : ResponseAndTriggerPlans.GetValueOrDefault(cardId, []);
}

public sealed partial class L12GameEngine
{
    private const string CompositePlanChoicePrefix = "composite-plan:";

    private CommandResult BeginCompositeHandPlayDeclaration(int playerIndex, L12CardInstance source)
        => BeginCompositeDeclaration(playerIndex, source, "composite-play", effectOnlyRepeat: false);

    private CommandResult BeginCommittedCompositeEffectDeclaration(int playerIndex, L12CardInstance source,
        L12StackItem parent, string completion)
    {
        if (!L12CompositeEffectPlans.RequiresHandPlayDeclaration(source.CardId))
        {
            var direct = new L12PendingActivation
            {
                ActivationId = $"activation-{++State.ActivationSequence}",
                Controller = playerIndex,
                SourceInstanceId = source.InstanceId,
                SourceCardId = source.CardId,
                Ability = "composite-committed-play",
                Text = $"自动提交〈{source.Name}〉无公开声明的复合计划",
                ValidChoices = [],
                PlayCardInstanceId = source.InstanceId,
                CommittedParentStackItemId = parent.StackItemId,
                CommittedCompletion = completion,
            };
            CompleteCommittedCompositeEffectDeclaration(direct);
            return CommandResult.Ok();
        }
        var result = BeginCompositeDeclaration(playerIndex, source, "composite-committed-play", effectOnlyRepeat: false);
        if (!result.Accepted) return result;
        var activation = State.PendingActivations.Last(candidate => candidate.Controller == playerIndex
            && candidate.SourceInstanceId == source.InstanceId
            && candidate.Ability == "composite-committed-play");
        activation.CommittedParentStackItemId = parent.StackItemId;
        activation.CommittedCompletion = completion;
        return result;
    }

    private CommandResult BeginRepeatedCompositeEffectDeclaration(int playerIndex, L12CardInstance source)
        => BeginCompositeDeclaration(playerIndex, source, "composite-repeated-effect", effectOnlyRepeat: true);

    private CommandResult BeginCompositeDeclaration(int playerIndex, L12CardInstance source, string ability,
        bool effectOnlyRepeat)
    {
        var player = State.Players[playerIndex];
        var opponent = State.Players[1 - playerIndex];
        var steps = new List<L12ActivationSelectionStep>();

        if (!TryBuildStarterCompositeDeclaration(playerIndex, source, player, opponent, steps))
        switch (source.CardId)
        {
            case "S01-0005":
                steps.Add(CompositeStep("option", "volleyMode", "万箭齐发：选择以下一项",
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

            case "S01-0007":
            {
                var canPay = ActiveResourceCount(player) >= 1;
                steps.Add(CompositeStep("option", "campMode", "野外扎营：选择是否消耗1士气，并选择主宰增加血量或抽牌",
                    canPay ? ["mode:none", "mode:heal", "mode:draw"] : ["mode:none"], 1, 1,
                    new()
                    {
                        ["mode:none"] = "查看牌库顶部3张牌，选择1张同阵营军团展示并加入手牌，其余置入牌库底部",
                        ["mode:heal"] = "消耗1士气：我方主宰增加1点血量",
                        ["mode:draw"] = "消耗1士气：抽取1张牌",
                    }));
                steps.Add(CompositeStep("composite-ordinary-payment", "campHealCost",
                    "野外扎营：预先选择治疗段消耗的1份资源", CompositeOrdinaryPaymentChoices(player), 1,
                    requiredChoice: "mode:heal"));
                steps.Add(CompositeStep("composite-ordinary-payment", "campDrawCost",
                    "野外扎营：选择抽取1张牌所消耗的1份资源", CompositeOrdinaryPaymentChoices(player), 1,
                    requiredChoice: "mode:draw"));
                break;
            }

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

            case "S01-0013":
            {
                var canUse = ActiveResourceCount(player) >= 1 && opponent.Hand.Count > 0;
                steps.Add(CompositeStep("option", "mode", "前线侦查：选择是否消耗1士气，使对方将1张手牌洗回牌库",
                    canUse ? ["mode:none", "mode:use"] : ["mode:none"], 1, 1,
                    new()
                    {
                        ["mode:none"] = "查看对方所有手牌",
                        ["mode:use"] = "消耗1士气：对方选择其1张手牌洗回牌库",
                    }));
                steps.Add(CompositeStep("composite-ordinary-payment", "scoutCost",
                    "前线侦查：预先选择洗回手牌段消耗的1份资源", CompositeOrdinaryPaymentChoices(player), 1,
                    requiredChoice: "mode:use"));
                break;
            }

            case "S01-0014":
                steps.Add(CompositeStep("option", "disasterValue", "祭天仪式：选择将天灾值增加或减少最多2点",
                    ["-2", "-1", "0", "1", "2"], 1, 1));
                break;

            case "S01-0118":
            {
                steps.Add(CompositeStep("field-legion", "buffTarget", "神妙行军：选择我方前排1张军团，本回合兵力+2000",
                    player.Field[0].Where(card => card is not null && IsFieldLegion(card))
                        .Select(card => card!.InstanceId), 1));
                break;
            }

            case "S01-0119":
            {
                var modes = new List<string> { "mode:none" };
                if (player.MoraleDeck.Count > 0) modes.Add("mode:morale");
                steps.Add(CompositeStep("option", "mode", "观星：选择是否从士气牌库追加1张活跃士气",
                    modes, 1, 1, new()
                    {
                        ["mode:none"] = "查看牌库顶部5张牌，自选顺序放回牌库顶部或底部",
                        ["mode:morale"] = "从士气牌库追加1张活跃士气",
                    }));
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
                steps.Add(CompositeStep("option", "duatMode", "杜阿特之门：选择以下一项",
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
            {
                var graveCards = player.Graveyard.Where(CanEnterHandOrLibrary).ToArray();
                steps.Add(GraveCostSelectionStep(player,
                    "猎杀时刻：选择并排序可合计视为4张、置于牌库底部的墓地卡牌",
                    "graveCost", graveCards, required: 4));
                steps.Add(CompositeStep("enemy-legion", "killTarget", "猎杀时刻：预先选择击杀目标",
                    PublicLegions(opponent).Where(card => card.Troops <= 6000).Select(card => card.InstanceId), 1));
                break;
            }

            case "S01-0419":
            {
                var rested = player.Morale.Where(card => card.Tapped).Select(card => card.InstanceId).ToArray();
                steps.Add(CompositeStep("option", "mode", "花魁的馈赠：选择是否将我方最多1张休整士气转为活跃",
                    rested.Length > 0 ? ["mode:none", "mode:morale"] : ["mode:none"], 1, 1,
                    new()
                    {
                        ["mode:none"] = "查看牌库顶部3张牌，选择1张其他【高天原】卡牌展示并加入手牌，其余返回牌库底部",
                        ["mode:morale"] = "将我方最多1张休整士气转为活跃",
                    }));
                steps.Add(CompositeStep("target-morale", "moraleTarget", "花魁的馈赠：预先选择转为活跃的休整士气",
                    rested, 1, requiredChoice: "mode:morale"));
                break;
            }

            case "S01-0418":
                steps.Add(CompositeStep("enemy-legion", "killTarget", "天诛：预先选择费用不高于7的击杀目标",
                    PublicLegions(opponent).Where(card => card.CurrentCost <= 7).Select(card => card.InstanceId), 1));
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
                steps.Add(CompositeStep("option", "mode", "黑色莲花：选择是否消耗3士气，将此战术休整置入士气区",
                    modes, 1, 1, new()
                    {
                        ["mode:none"] = "调整天灾值后置入墓地",
                        ["mode:morale"] = "消耗3士气：将此战术休整置入士气区并视为1张士气",
                    }));
                steps.Add(CompositeStep("composite-ordinary-payment", "lotusCost", "黑色莲花：选择将此战术置入士气区所消耗的3份资源",
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

            case "S02-0306":
                steps.Add(CompositeStep("option", "mode", "密米尔之泉：选择是否弃置我方牌库顶部2张牌",
                    ["mode:none", "mode:mill"], 1, 1, new()
                    {
                        ["mode:none"] = "我方主宰增加1点血量，抽取1张牌",
                        ["mode:mill"] = "弃置我方牌库顶部2张牌",
                    }));
                break;

            case "S02-0522":
                steps.Add(CompositeStep("option", "mode", "倪克斯的陨星：选择是否消耗并翻转1神力，使对方1张军团本回合兵力-2000",
                    ["mode:none", "mode:second"], 1, 1,
                    new()
                    {
                        ["mode:none"] = "选择对方1张军团，本回合兵力-3000",
                        ["mode:second"] = "消耗并翻转1神力：选择对方1张军团，本回合兵力-2000",
                    }));
                steps.Add(CompositeStep("enemy-legion", "primaryTarget", "倪克斯的陨星：选择本回合兵力-3000的目标",
                    PublicLegions(opponent).Select(card => card.InstanceId), 1));
                steps.Add(CompositeStep("target-morale", "secondCost", "倪克斯的陨星：选择为兵力-2000效果消耗并翻转的1神力",
                    player.Morale.Where(card => card.IsGodPower && !card.Tapped).Select(card => card.InstanceId), 1,
                    requiredChoice: "mode:second"));
                steps.Add(CompositeStep("enemy-legion", "secondaryTarget", "倪克斯的陨星：选择本回合兵力-2000的目标",
                    PublicLegions(opponent).Select(card => card.InstanceId), 1, requiredChoice: "mode:second"));
                break;

            case "S02-0105":
                steps.Add(CompositeStep("option", "mode", "乾坤 阳：预先声明是否发动返还士气并抽牌",
                    ["mode:none", "mode:draw"], 1, 1,
                    new()
                    {
                        ["mode:none"] = "击杀对方1张原本兵力不高于3000的军团",
                        ["mode:draw"] = "返还1士气：抽取1张牌",
                    }));
                steps.Add(CompositeStep("enemy-legion", "killTarget", "乾坤 阳：预先选择击杀目标",
                    PublicLegions(opponent).Where(card => card.BaseTroops <= 3000 && !card.Hidden)
                        .Select(card => card.InstanceId), 1));
                steps.Add(CompositeStep("resource-return", "drawCost", "乾坤 阳：预先选择返还的1张士气",
                    player.Morale.Select(card => card.InstanceId), 1, requiredChoice: "mode:draw"));
                break;

            case "S02-0521":
                steps.Add(CompositeStep("option", "mode", "荣耀之路：预先声明是否发动神力检索段",
                    ["mode:none", "mode:search"], 1, 1,
                    new()
                    {
                        ["mode:none"] = "翻转最多3张士气",
                        ["mode:search"] = "消耗并翻转2神力：查看牌库，选择1张【奥林匹斯】卡牌展示并加入手牌，随后重洗牌库",
                    }));
                steps.Add(CompositeStep("target-morale", "flipTargets", "荣耀之路：预先选择最多3张要翻转的士气",
                    player.Morale.Where(card => !card.IsGodPower).Select(card => card.InstanceId), 0, 3));
                steps.Add(CompositeStep("composite-glory-god-power-cost", "searchCost",
                    "荣耀之路：预先选择检索段消耗并翻转的2张神力", ["dynamic:1", "dynamic:2"], 2, 2,
                    requiredChoice: "mode:search"));
                break;

            case "S02-0620":
                steps.Add(CompositeStep("option", "mode", "符文之力：预先声明是否发动牌库查看段",
                    ["mode:none", "mode:search"], 1, 1,
                    new()
                    {
                        ["mode:none"] = "获得1符文",
                        ["mode:search"] = "消耗1士气：查看牌库顶部3张牌，选择1张其他【彼界】卡牌展示并加入手牌，其余返回牌库底部",
                    }));
                steps.Add(CompositeStep("composite-ordinary-payment", "searchCost", "符文之力：预先选择支付的1份资源",
                    CompositeOrdinaryPaymentChoices(player), 1, requiredChoice: "mode:search"));
                break;

            case "S02-0621":
            {
                var roundTableTargets = PublicLegions(player)
                    .Where(card => card.HasTrait("圆桌骑士"))
                    .Select(card => card.InstanceId)
                    .ToArray();
                var roundTablePayments = CompositeOrdinaryPaymentChoices(player).ToArray();
                var roundTableModes = roundTableTargets.Length > 0 && roundTablePayments.Length > 0
                    ? new[] { "mode:none", "mode:buff" }
                    : ["mode:none"];
                steps.Add(CompositeStep("option", "mode", "圆桌领域：选择是否消耗1士气使军团本回合兵力+2000",
                    roundTableModes, 1, 1,
                    new()
                    {
                        ["mode:none"] = "查看牌库，选择1张【圆桌骑士】军团展示并加入手牌，随后重洗牌库",
                        ["mode:buff"] = "消耗1士气：选择我方1张【圆桌骑士】军团，本回合兵力+2000",
                    }));
                steps.Add(CompositeStep("field-legion", "buffTarget", "圆桌领域：选择本回合兵力+2000的【圆桌骑士】军团",
                    roundTableTargets, 1,
                    requiredChoice: "mode:buff"));
                steps.Add(CompositeStep("composite-ordinary-payment", "buffCost", "圆桌领域：预先选择支付的1份资源",
                    roundTablePayments, 1, requiredChoice: "mode:buff"));
                break;
            }

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
                            && L12StructuredCardRules.HasFaction(player, card, "taiyangcheng") && !card.Hidden)
                        .Select(card => card!.InstanceId), 1));
                break;

            case "S02-0406":
                steps.Add(CompositeStep("option", "mode", "天下布武：选择以下一项",
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

        // Composite mode:none means “skip this optional segment and continue the remaining
        // printed effect”. Cancelling the declaration is a different action (for a hand play,
        // it also leaves the card and its printed cost untouched), so expose both only through
        // the explicit separate-choice policy and let the shared prompt layer label them apart.
        foreach (var modeStep in steps.Where(step => step.Kind == "option"
                     && step.ValidChoices.Contains("mode:none", StringComparer.OrdinalIgnoreCase)))
            modeStep.CancellationPolicy = L12ActivationCancellationPolicy.SeparateChoice;

        if (effectOnlyRepeat)
        {
            if (source.CardId == "S02-0207")
            {
                // The discarded legions are the colon cost, so an effect-only
                // repeat declares only the cost-dependent count. No battlefield
                // card is selected or moved as payment a second time.
                steps.RemoveAll(step => step.DeclarationKey == "discardTargets");
                steps.Insert(0, CompositeStep("option", "desertRepeatCount",
                    "沙漠君临：声明不再支付的原费用数量",
                    ["count:0", "count:1", "count:2", "count:3"], 1, 1,
                    new()
                    {
                        ["count:0"] = "不再弃置，按数量0处理",
                        ["count:1"] = "不再弃置，按数量1处理",
                        ["count:2"] = "不再弃置，按数量2处理",
                        ["count:3"] = "不再弃置，按数量3处理",
                    }));
            }
            var costKeys = L12CompositeEffectPlans.Segments(source.CardId)
                .Where(segment => segment.CostKey is not null)
                .Select(segment => segment.CostKey!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            steps.RemoveAll(step => step.DeclarationKey is not null && costKeys.Contains(step.DeclarationKey));
            foreach (var segment in L12CompositeEffectPlans.Segments(source.CardId)
                         .Where(segment => segment.RequiredMode is not null))
            {
                var declarationKey = segment.RequiredDeclarationKey ?? "mode";
                var modeStep = steps.FirstOrDefault(step => declarationKey.Equals(step.DeclarationKey,
                    StringComparison.OrdinalIgnoreCase));
                if (modeStep is null || modeStep.ValidChoices.Contains(segment.RequiredMode!,
                        StringComparer.OrdinalIgnoreCase)) continue;
                modeStep.ValidChoices.Add(segment.RequiredMode!);
                modeStep.ChoiceLabels.TryAdd(segment.RequiredMode!, segment.Text.Replace("消耗并翻转", "无需再次支付：")
                    .Replace("消耗", "无需再次支付：").Replace("返还", "无需再次支付："));
            }
        }

        if (steps.Count == 0)
        {
            if (!effectOnlyRepeat) return CommandResult.Reject("该卡牌没有复合效果声明计划");
            CompleteRepeatedCompositeEffectDeclaration(new L12PendingActivation
            {
                ActivationId = $"activation-{++State.ActivationSequence}",
                Controller = playerIndex,
                SourceInstanceId = source.InstanceId,
                SourceCardId = source.CardId,
                Ability = ability,
                Text = $"自动提交〈{source.Name}〉的重复效果",
                ValidChoices = [],
                PlayCardInstanceId = source.InstanceId,
            });
            return CommandResult.Ok();
        }
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
        var data = CompositeFirstSegmentData(source.CardId, activation.DeclaredValues)
            ?? new Dictionary<string, string>();
        data["effectGeneratedPlay"] = "free";
        data["originZone"] = "library";
        PushEffect(activation.Controller, source, "play", $"由其他效果免费打出的〈{source.Name}〉战术效果",
            CompositeFirstSegmentTargets(source.CardId, activation.DeclaredValues), data);
        ResumeCommittedCompositeParent(activation);
    }

    private void CompleteRepeatedCompositeEffectDeclaration(L12PendingActivation activation)
    {
        var source = CreateCard(activation.SourceCardId, activation.SourceInstanceId);
        if (!ValidateCompositeHandPlayDeclaration(activation.Controller, source,
                activation.DeclaredValues, out var error, effectOnlyRepeat: true))
        {
            AddEvent("effect-cancelled", activation.Controller,
                $"〈{source.Name}〉的重复效果声明失效：{error}", source);
            ResumeAfterPostResolutionGeneratedInteraction();
            return;
        }
        var data = CompositeFirstSegmentData(source.CardId, activation.DeclaredValues)
            ?? new Dictionary<string, string>();
        data["repeatedEffectOnly"] = "true";
        PushEffect(activation.Controller, source, "play", $"托勒密十三世再次发动的〈{source.Name}〉效果",
            CompositeFirstSegmentTargets(source.CardId, activation.DeclaredValues), data);
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
        FinishStackItem(parent);
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
        IReadOnlyDictionary<string, List<string>> declared, out string error, bool effectOnlyRepeat = false)
    {
        error = "复合效果的选项、目标或费用对象已失效";
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
            "S01-0006" => effectOnlyRepeat || declared.GetValueOrDefault("discardCost", []) is [var discardId]
                && discardId != card.InstanceId && player.Hand.Any(candidate => candidate.InstanceId == discardId),
            "S01-0007" => declared.GetValueOrDefault("campMode", []).SingleOrDefault() is { } campMode
                && campMode is "mode:none" or "mode:heal" or "mode:draw"
                && (effectOnlyRepeat || campMode != "mode:heal" || OrdinaryCost("campHealCost"))
                && (effectOnlyRepeat || campMode != "mode:draw" || OrdinaryCost("campDrawCost")),
            "S01-0009" => Own("returnTarget") && Own("buffTarget"),
            "S01-0010" => ValidateForgedOrdersDeclaration(opponent, declared),
            "S01-0011" => declared.GetValueOrDefault("lockTarget", []).SingleOrDefault() is { } lockTarget
                && (PublicLegions(opponent).Any(target => target.InstanceId == lockTarget && !target.Hidden)
                    || opponent.Morale.Any(target => target.InstanceId == lockTarget)),
            "S01-0013" => mode is "mode:none" or "mode:use"
                && (mode != "mode:use" || opponent.Hand.Count > 0 && (effectOnlyRepeat || OrdinaryCost("scoutCost"))),
            "S01-0014" => declared.GetValueOrDefault("disasterValue", []).SingleOrDefault()
                is "-2" or "-1" or "0" or "1" or "2",
            "S01-0015" => declared.Count == 0,
            "S01-0118" => Own("buffTarget", target =>
                FindOnField(player, target.InstanceId, out var row, out _) is not null && row == 0),
            "S01-0119" => mode is "mode:none" or "mode:morale"
                && (mode == "mode:none" || player.MoraleDeck.Count > 0),
            "S01-0221" => declared.GetValueOrDefault("duatMode", []).SingleOrDefault() is { } duatMode
                && duatMode is "mode:kill" or "mode:recover"
                && (duatMode != "mode:kill" || Enemy("killTarget", target => target.Troops <= 5000))
                && (duatMode != "mode:recover" || declared.GetValueOrDefault("recoverTarget", []).SingleOrDefault() is { } recover
                    && (recover == "mode:none" || Grave("recoverTarget", target => target.CardId != card.CardId
                        && L12StructuredCardRules.HasFaction(player, target, "taiyangcheng") && CanEnterHandOrLibrary(target)))),
            "S01-0318" => (effectOnlyRepeat || declared.GetValueOrDefault("masterDamageCost", []).SingleOrDefault() == "cost:master-damage")
                && Grave("entryCard", target => target.CardType == "legion" && target.CurrentCost <= 5
                    && L12StructuredCardRules.HasFaction(player, target, "asgard")) && OwnSlot("entrySlot"),
            "S01-0319" => (effectOnlyRepeat || declared.GetValueOrDefault("graveCost", []) is { Count: >= 2 and <= 4 } graveCost
                && graveCost.Distinct(StringComparer.OrdinalIgnoreCase).Count() == graveCost.Count
                && graveCost.Select(id => player.Graveyard.FirstOrDefault(target => target.InstanceId == id
                        && CanEnterHandOrLibrary(target))).OfType<L12CardInstance>().ToArray() is { } graveCards
                && graveCards.Length == graveCost.Count
                && L12StructuredCardRules.IsExactGraveCardRepresentation(player, graveCards,
                    declared.GetValueOrDefault("graveCostCopies", []).SingleOrDefault(), 4))
                && Enemy("killTarget", target => target.Troops <= 6000),
            "S01-0419" => mode is "mode:none" or "mode:morale"
                && (mode == "mode:none" || declared.GetValueOrDefault("moraleTarget", []).SingleOrDefault() is { } moraleTarget
                    && player.Morale.Any(card => card.InstanceId == moraleTarget && card.Tapped)),
            "S01-0418" => Enemy("killTarget", target => target.CurrentCost <= 7),
            "S02-0009" => ValidateDefenseDeploymentDeclaration(player, card, declared),
            "S02-0010" => declared.GetValueOrDefault("disasterMode", []).SingleOrDefault() is "-1" or "0" or "1"
                && (mode is "mode:none" or "mode:morale")
                && (effectOnlyRepeat || mode == "mode:none" || OrdinaryCosts("lotusCost", 3)),
            "S02-0011" => EnemyMany("killTargets", 3, target => target.BaseTroops <= 2000),
            "S02-0013" => declared.GetValueOrDefault("artifactTarget", []).SingleOrDefault() is { } artifactId
                && new[] { opponent.Relic }.Concat(opponent.ExtraRelics)
                    .Any(target => target?.InstanceId == artifactId && target.CardType == "artifact"),
            "S02-0306" => (effectOnlyRepeat || player.MasterDamageTakenThisTurn >= 2
                && !player.UsedAbilities.Contains("s2-mimir-used"))
                && mode is "mode:none" or "mode:mill",
            "S02-0522" => mode is "mode:none" or "mode:second"
                && Enemy("primaryTarget")
                && (mode == "mode:none" || (effectOnlyRepeat || GodPowerCost("secondCost", 1)) && Enemy("secondaryTarget")),
            "S02-0105" => mode is "mode:none" or "mode:draw"
                && Enemy("killTarget", target => target.BaseTroops <= 3000)
                && (mode == "mode:none" || effectOnlyRepeat || declared.GetValueOrDefault("drawCost", []).SingleOrDefault() is { } moraleId
                    && player.Morale.Any(resource => resource.InstanceId == moraleId)),
            "S02-0521" => mode is "mode:none" or "mode:search"
                && declared.GetValueOrDefault("flipTargets", []).Count <= 3
                && declared.GetValueOrDefault("flipTargets", []).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    == declared.GetValueOrDefault("flipTargets", []).Count
                && declared.GetValueOrDefault("flipTargets", []).All(id => player.Morale.Any(resource => resource.InstanceId == id && !resource.IsGodPower))
                && (effectOnlyRepeat || mode == "mode:none" || ValidateGloryPlannedCost(player, declared)),
            "S02-0620" => mode is "mode:none" or "mode:search"
                && (effectOnlyRepeat || mode == "mode:none" || OrdinaryCost("searchCost")),
            "S02-0621" => mode is "mode:none" or "mode:buff"
                && (mode == "mode:none" || Own("buffTarget", target => target.HasTrait("圆桌骑士"))
                    && (effectOnlyRepeat || OrdinaryCost("buffCost"))),
            "S02-0207" => ValidateDesertDeclaration(player, card, declared, effectOnlyRepeat),
            "S02-0307" => (effectOnlyRepeat || player.Library.Count >= 1) && Enemy("curseTarget"),
            "S02-0206" => Own("buffTarget", target => L12StructuredCardRules.HasFaction(player, target, "taiyangcheng")
                && FindOnField(player, target.InstanceId, out var row, out _) is not null && row == 0),
            "S02-0406" => mode is "mode:row-cost" or "mode:front-attack" or "mode:free-move"
                && (mode != "mode:row-cost" || declared.GetValueOrDefault("row", []).SingleOrDefault() is "row:0" or "row:1"),
            _ => ValidateStarterCompositeDeclaration(controller, card, declared),
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
        if (source.CardId == "S02-0207")
        {
            var discardIds = (declared ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase))
                .GetValueOrDefault("discardTargets", []);
            var discards = discardIds.Select(id => FindOnField(player, id, out _, out _))
                .Where(card => card is not null && IsFieldLegion(card)).Cast<L12CardInstance>().ToArray();
            // 先验证全部费用，再移动任一实例，避免部分支付。
            if (discardIds.Count > 3 || discards.Length != discardIds.Count
                || discards.Select(card => card.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != discardIds.Count)
                return false;
            foreach (var discard in discards)
                if (!RemoveFromField(player, discard, true, "作为〈沙漠君临〉的发动费用弃置",
                        leaveKind: L12FieldLeaveKind.Discard)) return false;
            AddEvent("cost", controller, $"〈{source.Name}〉在入栈前弃置{discards.Length}张我方军团作为发动费用",
                new[] { source }.Concat(discards).ToArray());
        }
        var declaration = declared ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var segments = L12CompositeEffectPlans.Segments(source.CardId);
        var preStackCosts = segments.Where(segment => segment.PreStackCost
            && CompositeSegmentEnabled(segment, declaration)).ToArray();
        if (preStackCosts.Length > 0
            && !preStackCosts.All(segment => TryPayCompositeDeclaredCost(controller, source, segment, declaration)))
            return false;
        var first = segments.FirstOrDefault(segment => CompositeSegmentEnabled(segment, declaration));
        if (preStackCosts.Length == 0 && first is not null
            && !TryPayCompositeDeclaredCost(controller, source, first, declaration)) return false;
        if (source.CardId == "S02-0306") player.UsedAbilities.Add("s2-mimir-used");
        return true;
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
        IReadOnlyDictionary<string, List<string>> declared, bool effectOnlyRepeat)
    {
        var discards = declared.GetValueOrDefault("discardTargets", []);
        var discardCount = discards.Count;
        if (effectOnlyRepeat)
        {
            var countText = declared.GetValueOrDefault("desertRepeatCount", []).SingleOrDefault();
            if (countText?.Split(':') is not ["count", var countValue]
                || !int.TryParse(countValue, out discardCount) || discardCount is < 0 or > 3) return false;
        }
        else if (discards.Count > 3
            || discards.Distinct(StringComparer.OrdinalIgnoreCase).Count() != discards.Count
            || discards.Any(id => FindOnField(player, id, out _, out _) is not { } card || !IsFieldLegion(card)))
        {
            return false;
        }
        var summonId = declared.GetValueOrDefault("summonTarget", []).SingleOrDefault();
        var summon = player.Hand.FirstOrDefault(card => card.InstanceId == summonId && card.InstanceId != source.InstanceId
            && card.CardType == "legion" && L12StructuredCardRules.HasFaction(player, card, "taiyangcheng")
            && card.DisasterLevel == discardCount);
        var slotText = declared.GetValueOrDefault("summonSlot", []).SingleOrDefault();
        if (summon is null || slotText?.Split(':') is not [var rowText, var slotValue]
            || !int.TryParse(rowText, out var row) || !int.TryParse(slotValue, out var slot)
            || row is < 0 or > 1 || slot is < 0 or > 2) return false;
        var occupant = player.Field[row][slot];
        return occupant is null || !effectOnlyRepeat
            && discards.Contains(occupant.InstanceId, StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, string> CompositeFirstSegmentData(string cardId,
        IReadOnlyDictionary<string, List<string>> declared)
    {
        var segments = L12CompositeEffectPlans.Segments(cardId);
        var firstIndex = Enumerable.Range(0, segments.Count)
            .First(index => CompositeSegmentEnabled(segments[index], declared));
        var data = new Dictionary<string, string>
        {
            ["compositePlan"] = cardId,
            ["compositeSegment"] = firstIndex.ToString(),
            ["atomicFlow"] = segments[firstIndex].Flow,
            ["atomicContinuation"] = "true",
        };
        if (L12CompositeEffectPlans.UsesSingleResponseEffect(cardId))
            data["compositeResponseScope"] = "single-effect";
        foreach (var pair in declared) data[$"declared:{pair.Key}"] = string.Join('|', pair.Value);
        return data;
    }

    private static string[] CompositeFirstSegmentTargets(string cardId,
        IReadOnlyDictionary<string, List<string>> declared)
    {
        var segments = L12CompositeEffectPlans.Segments(cardId);
        return CompositeSegmentTargets(segments.First(segment => CompositeSegmentEnabled(segment, declared)), declared);
    }

    private static bool CompositeSegmentEnabled(L12CompositeEffectSegmentSpec segment,
        IReadOnlyDictionary<string, List<string>> declared)
        => segment.RequiredMode is null || declared
            .GetValueOrDefault(segment.RequiredDeclarationKey ?? "mode", [])
            .Contains(segment.RequiredMode, StringComparer.OrdinalIgnoreCase);

    private static bool CompositeSegmentEnabled(L12CompositeEffectSegmentSpec segment, L12StackItem item)
        => segment.RequiredMode is null || CompositeDeclared(item, segment.RequiredDeclarationKey ?? "mode")
            .Contains(segment.RequiredMode, StringComparer.OrdinalIgnoreCase);

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
        var selected = new[]
            { "flipTargets", "secondCost", "drawCost", "searchCost", "buffCost", "lotusCost",
                "campHealCost", "campDrawCost", "scoutCost" }
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
        var singleResponseEffect = item.Data.GetValueOrDefault("compositeResponseScope") == "single-effect"
            || L12CompositeEffectPlans.UsesSingleResponseEffect(planId);
        // 整项能力只响应一次：首段被无效时，后续只是同一效果内部的结算子句，
        // 必须一并停止，不能再创建一个看似独立的新效果。
        if (singleResponseEffect && item.Negated) return false;
        if (singleResponseEffect && item.Trigger != "authority-event")
        {
            // 若本段产生“因效果转为活跃”的权威时点，必须先让这些时点全部完成响应，
            // 再继续同一效果的后续子句。延续载体选择延迟队列中最早创建的一项；队列
            // 按 LIFO 处理，因此它会在同批其余时点之后最后结算。毒药发作无效任一项
            // 时会同时标记同源兄弟，载体也随之被无效，后续子句便不会错误先行结算。
            var continuationCarrier = State.DeferredEffectStack.FirstOrDefault(candidate =>
                candidate.Trigger == "authority-event"
                && candidate.Data.GetValueOrDefault("eventType") == "effect-ready"
                && candidate.Data.GetValueOrDefault("originStackId") == item.StackItemId);
            if (continuationCarrier is not null)
            {
                foreach (var pair in item.Data.Where(pair =>
                             pair.Key.StartsWith("composite", StringComparison.OrdinalIgnoreCase)
                             || pair.Key.StartsWith("declared:", StringComparison.OrdinalIgnoreCase)
                             || pair.Key is "repeatedEffectOnly" or "effectGeneratedPlay" or "originZone"))
                    continuationCarrier.Data[pair.Key] = pair.Value;
                continuationCarrier.Data["compositeOriginTrigger"] = item.Trigger;
                return true;
            }
        }
        var segments = L12CompositeEffectPlans.Segments(planId);
        for (var nextIndex = current + 1; nextIndex < segments.Count; nextIndex++)
        {
            var next = segments[nextIndex];
            if (!CompositeSegmentEnabled(next, item)) continue;
            if (!ValidateCompositeSegmentTargets(item.Controller, next.Flow, item))
            {
                AddEvent("effect-cancelled", item.Controller,
                    $"〈{source.Name}〉的“{next.Text}”因目标已失效而取消；其余效果继续结算", source);
                continue;
            }
            if (item.Data.GetValueOrDefault("repeatedEffectOnly") != "true"
                && !next.PreStackCost && !TryPayCompositeSegmentCost(item.Controller, source, next, item))
            {
                AddEvent("effect-cancelled", item.Controller,
                    $"〈{source.Name}〉的“{next.Text}”因费用对象或目标失效而取消；未发生部分支付，其余效果继续结算", source);
                continue;
            }
            var data = new Dictionary<string, string>(item.Data, StringComparer.OrdinalIgnoreCase)
            {
                ["compositeSegment"] = nextIndex.ToString(),
                ["atomicFlow"] = next.Flow,
                ["atomicContinuation"] = "true",
            };
            // 首段已经完成双方响应；后续子句只继续结算，不再重复询问或允许
            // 对同一项能力中的单个句子另行无效。
            if (singleResponseEffect) data["unrespondable"] = "true";
            // The Wisdom Codex reward belongs to the exact stack item whose cost was paid.
            // A semantic follow-up is a new effect and must not inherit that one-shot marker.
            data.Remove("wisdomRewards");
            var continuationTrigger = item.Data.GetValueOrDefault("compositeOriginTrigger") ?? item.Trigger;
            PushEffect(item.Controller, source, continuationTrigger, next.Text,
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
            "honda-kill" => DeclaredEnemyTarget(controller,
                CompositeDeclared(item, "killTarget").SingleOrDefault(), card => card.CurrentCost == 0) is not null,
            "amaterasu-kill" => CompositeDeclared(item, "killTarget").SingleOrDefault() is { } amaterasuKill
                && (amaterasuKill == "mode:none" || DeclaredEnemyTarget(controller, amaterasuKill,
                    card => card.CurrentCost == 0) is not null),
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
            "oiran-ready-morale" => CompositeDeclared(item, "moraleTarget").SingleOrDefault() is { } moraleTarget
                && State.Players[controller].Morale.Any(card => card.InstanceId == moraleTarget && card.Tapped),
            "palace-exchange-revive" => CompositeDeclared(item, "entryCard").SingleOrDefault() is { } palaceCard
                && CompositeDeclared(item, "entryBattlefield").SingleOrDefault() == $"battlefield:{controller}"
                && CompositeDeclared(item, "entrySlot").SingleOrDefault() is { } palaceSlot
                && State.Players[controller].Graveyard.Any(card => card.InstanceId == palaceCard
                    && card.CardType == "legion" && L12StructuredCardRules.HasFaction(State.Players[controller], card, "tianting")
                    && card.CurrentCost <= (int.TryParse(item.Data.GetValueOrDefault("paid"), out var paid) ? paid : 0))
                && ParseSlot(palaceSlot) is var palacePosition
                && palacePosition.Item1 is >= 0 and <= 1 && palacePosition.Item2 is >= 0 and <= 2
                && State.Players[controller].Field[palacePosition.Item1][palacePosition.Item2] is null,
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
                var representation = declared.GetValueOrDefault($"{segment.CostKey}Copies", []).SingleOrDefault();
                if (costs.Length == 0 || costs.Any(card => card is null)
                    || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length
                    || !L12StructuredCardRules.IsExactGraveCardRepresentation(player,
                        costs.Cast<L12CardInstance>().ToArray(), representation, segment.Cost)) return false;
                MoveGraveToLibraryBottom(player, costs.Cast<L12CardInstance>());
                var physicalText = costs.Length == segment.Cost
                    ? $"{segment.Cost}张卡牌"
                    : $"{costs.Length}张实体卡牌（按效果合计视为{segment.Cost}张）";
                AddEvent("cost", controller, $"〈{source.Name}〉将墓地{physicalText}依声明顺序置于牌库底部", source);
                return true;
            }
            default:
                return false;
        }
    }
}
