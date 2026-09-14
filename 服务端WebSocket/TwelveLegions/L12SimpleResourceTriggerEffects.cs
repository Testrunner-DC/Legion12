namespace TwelveLegions.Server;

internal sealed record L12SimpleResourceTriggerSpec(
    string CardId,
    int AbilitySequence,
    string Trigger,
    string Name,
    string Operation,
    int Amount,
    bool Optional,
    bool RequiresOnceReservation,
    string SettlementText,
    string EventText,
    string? TargetFilter = null,
    string? CandidateCondition = null,
    string? DataAbility = null,
    string? DataMode = null,
    bool FromFactionEffect = false,
    bool OwnsStandaloneAtomicAbility = true);

/// <summary>
/// 单段资源触发的权威规格。候选、是否选发、士气对象、次数提交、响应文案、
/// 结算复验和日志都读取这里；卡牌路由不再分别实现追加士气、翻转士气或获得符文。
/// </summary>
internal static class L12SimpleResourceTriggerEffects
{
    internal const string AddRestedMorale = "add-rested-morale";
    internal const string FlipMoraleToGodPower = "flip-morale-to-god-power";
    internal const string GainRunes = "gain-runes";
    internal const string AnyMorale = "any-morale";
    internal const string RestedMorale = "rested-morale";

    internal static readonly L12SimpleResourceTriggerSpec[] All =
    [
        new("S02-0603", 2, "enter", "梅林", GainRunes, 1,
            Optional: false, RequiresOnceReservation: false,
            SettlementText: "登场时 获得1符文。",
            EventText: "梅林登场时使我方获得1符文"),
        new("S02-0606", 2, "enter", "帕西瓦尔", GainRunes, 1,
            Optional: false, RequiresOnceReservation: false,
            SettlementText: "登场时 获得1符文。",
            EventText: "帕西瓦尔登场时使我方获得1符文"),
        new("S02-0607", 1, "enter", "高文", GainRunes, 1,
            Optional: false, RequiresOnceReservation: false,
            SettlementText: "登场时 获得1符文。",
            EventText: "高文登场时使我方获得1符文"),
        new("S02-0616", 2, "enter", "阿麦金", GainRunes, 1,
            Optional: true, RequiresOnceReservation: false,
            SettlementText: "登场时 可获得1符文。",
            EventText: "阿麦金登场时使我方获得1符文"),
        new("S02-0618", 3, "enter", "伊丽莎白·都铎", GainRunes, 1,
            Optional: false, RequiresOnceReservation: false,
            SettlementText: "登场时 获得1符文。",
            EventText: "伊丽莎白·都铎登场时使我方获得1符文"),
        new("ST06-03", 1, "enter", "加雷斯", GainRunes, 1,
            Optional: true, RequiresOnceReservation: false,
            SettlementText: "登场时 可获得1符文。",
            EventText: "加雷斯登场时使我方获得1符文"),
        new("ST06-08", 1, "enter", "纯白的灵鹿", GainRunes, 1,
            Optional: true, RequiresOnceReservation: false,
            SettlementText: "登场时 可获得1符文。",
            EventText: "纯白的灵鹿登场时使我方获得1符文"),
        new("S02-01S1", 2, "death", "哮天犬·稚", AddRestedMorale, 1,
            Optional: true, RequiresOnceReservation: false,
            SettlementText: "阵亡时 可从士气牌库追加1张休整的士气。",
            EventText: "哮天犬·稚从士气牌库追加1张休整士气",
            CandidateCondition: "morale-deck-not-empty"),
        new("S02-0508", 2, "death", "阿塔兰忒", FlipMoraleToGodPower, 1,
            Optional: false, RequiresOnceReservation: false,
            SettlementText: "阵亡时 翻转1张士气。",
            EventText: "阿塔兰忒阵亡时翻转1张士气",
            TargetFilter: AnyMorale),
        new("S02-05M1", 1, "friendly-ranged-death", "阿尔忒弥斯", FlipMoraleToGodPower, 1,
            Optional: true, RequiresOnceReservation: true,
            SettlementText: "回合1次 我方远程军团阵亡时，可翻转1张休整的士气。",
            EventText: "阿尔忒弥斯将声明的休整士气翻转为神力",
            TargetFilter: RestedMorale, DataAbility: "artemisDeathFlip"),
        new("S02-06M1", 1, "morrigan-enemy-death", "莫瑞甘", GainRunes, 1,
            Optional: true, RequiresOnceReservation: true,
            SettlementText: "我方 回合1次 对方军团阵亡时，可获得1符文。",
            EventText: "莫瑞甘因对方军团阵亡使我方获得1符文"),
        new("S02-0102", 1, "master-morale-return", "李牧", AddRestedMorale, 1,
            Optional: true, RequiresOnceReservation: true,
            SettlementText: "我方 回合1次 我方士气因主宰效果返还4张及以上时，可从士气牌库追加1张休整的士气。",
            EventText: "李牧从士气牌库追加1张休整士气",
            CandidateCondition: "morale-deck-not-empty", DataMode: "limu"),
        new("S02-06S4", 2, "friendly-round-table-enter", "寻找圣杯之旅", GainRunes, 1,
            Optional: true, RequiresOnceReservation: true,
            SettlementText: "我方 回合1次 我方【圆桌骑士】登场时，可获得1符文。",
            EventText: "〈寻找圣杯之旅〉使我方获得1符文",
            DataAbility: "grailRoundTableRune"),
        new("S02-06M2", 2, "trial-advance", "安格斯·麦·奥格", GainRunes, 1,
            Optional: true, RequiresOnceReservation: true,
            SettlementText: "我方 回合1次 推进试炼进度时，可获得1符文。",
            EventText: "安格斯·麦·奥格使我方获得1符文",
            DataAbility: "angusTrialAdvanceRune"),
        new("S02-01M1", 2, "master-legion-returned", "孙悟空", AddRestedMorale, 1,
            Optional: true, RequiresOnceReservation: false,
            SettlementText: "若我方士气少于对方，可从士气牌库追加1张休整的士气。",
            EventText: "孙悟空返回主宰区后追加1张休整士气",
            CandidateCondition: "controller-morale-less-than-opponent", DataAbility: "wukongReturnMorale",
            OwnsStandaloneAtomicAbility: false),
        new("S01-01C1", 2, "morale-returned-to-zero", "士气·天廷", AddRestedMorale, 2,
            Optional: true, RequiresOnceReservation: true,
            SettlementText: "我方 回合1次 我方士气为0张时，可从士气牌库追加2张休整的士气。",
            EventText: "天廷阵营效果：追加 2 张休整士气",
            CandidateCondition: "controller-morale-zero-or-return-locked", DataAbility: "factionZeroRecovery",
            FromFactionEffect: true),
    ];

    internal static L12SimpleResourceTriggerSpec? Find(string cardId, string trigger,
        IReadOnlyDictionary<string, string>? data = null)
        => All.SingleOrDefault(spec => spec.CardId.Equals(cardId, StringComparison.OrdinalIgnoreCase)
            && spec.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase)
            && (spec.DataAbility is null || spec.DataAbility.Equals(
                data?.GetValueOrDefault("ability"), StringComparison.OrdinalIgnoreCase))
            && (spec.DataMode is null || spec.DataMode.Equals(
                data?.GetValueOrDefault("mode"), StringComparison.OrdinalIgnoreCase)));
}

public sealed partial class L12GameEngine
{
    private bool SimpleResourceSettlementConditionMet(L12StackItem item,
        L12SimpleResourceTriggerSpec spec)
    {
        var player = State.Players[item.Controller];
        var opponent = State.Players[1 - item.Controller];
        return spec.CandidateCondition switch
        {
            null => true,
            "morale-deck-not-empty" => player.MoraleDeck.Count > 0,
            "controller-morale-less-than-opponent" => player.Morale.Count < opponent.Morale.Count
                && player.MoraleDeck.Count > 0,
            "controller-morale-zero-or-return-locked" => (player.Morale.Count == 0
                    || item.Data.GetValueOrDefault("factionZeroEligibleAtReturn") == "true")
                && player.MoraleDeck.Count > 0,
            _ => false,
        };
    }

    private bool TryResolveSimpleResourceTrigger(L12StackItem item)
    {
        var spec = L12SimpleResourceTriggerEffects.Find(item.SourceCardId, item.Trigger, item.Data);
        if (spec is null) return false;
        var player = State.Players[item.Controller];
        var source = FindSource(item) ?? item.SourceSnapshot;
        if (spec.Optional && PublicTriggerDeclared(item, "mode") != "mode:use")
        {
            FinishStackItem(item);
            return true;
        }

        if (!SimpleResourceSettlementConditionMet(item, spec))
        {
            AddEvent("effect-cancelled", item.Controller,
                $"〈{spec.Name}〉的资源条件在结算时失效；该项效果不结算，已登记的回合次数不恢复",
                source is null ? [] : [source]);
            FinishStackItem(item);
            return true;
        }

        switch (spec.Operation)
        {
            case L12SimpleResourceTriggerEffects.AddRestedMorale:
            {
                var added = AddMorale(player, spec.Amount, tapped: true,
                    fromFactionEffect: spec.FromFactionEffect);
                if (added > 0)
                    AddEvent(spec.FromFactionEffect ? "faction-effect" : "morale", item.Controller,
                        spec.EventText, source is null ? [] : [source]);
                else
                    AddEvent("effect-cancelled", item.Controller,
                        $"〈{spec.Name}〉结算时士气牌库已空；无法追加士气",
                        source is null ? [] : [source]);
                break;
            }
            case L12SimpleResourceTriggerEffects.GainRunes:
                L12S2ZoneOps.GainRunes(player, spec.Amount);
                AddEvent("runes", item.Controller, spec.EventText, source is null ? [] : [source]);
                break;
            case L12SimpleResourceTriggerEffects.FlipMoraleToGodPower:
            {
                var targetId = PublicTriggerDeclared(item, "moraleTarget");
                var target = player.Morale.FirstOrDefault(card => card.InstanceId == targetId
                    && !card.IsGodPower
                    && (spec.TargetFilter != L12SimpleResourceTriggerEffects.RestedMorale || card.Tapped));
                if (target is null)
                    AddEvent("effect-cancelled", item.Controller,
                        $"〈{spec.Name}〉声明的士气目标在结算时失效；该项效果不结算，已登记的回合次数不恢复",
                        source is null ? [] : [source]);
                else
                {
                    L12S2ZoneOps.FlipMoraleFace(player, target.InstanceId, toGodPower: true);
                    AddEvent("morale", item.Controller, spec.EventText, source is null ? [] : [source]);
                }
                break;
            }
            default:
                AddEvent("effect-cancelled", item.Controller,
                    $"〈{spec.Name}〉的资源操作未登记；效果不结算", source is null ? [] : [source]);
                break;
        }
        FinishStackItem(item);
        return true;
    }
}
