using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

/// <summary>
/// 2026-09-23 裁定：天灾不会触发任何效果——包括弃置进墓的触发、军团的阵亡时/离场时、
/// 位移触发、入手响应窗与主宰伤害反应。本类锁定五个曾缺口的抑制与各自的对照组。
/// </summary>
public sealed class DisasterTriggerSuppressionRulingTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, string? firstMaster = null, bool autoPass = true)
    {
        var first = Catalog.DeckAt(0);
        if (firstMaster is not null)
            first = new L12PresetDeckDefinition
            {
                Name = $"{firstMaster}天灾抑制裁定牌库",
                MasterId = firstMaster,
                CardIds = [.. first.CardIds],
                MoraleIds = [.. first.MoraleIds],
                SpecialIds = [],
            };
        var game = new L12GameEngine(Catalog, "disaster-trigger-suppression", "DTSR", seed,
            ["甲", "乙"], [first, Catalog.DeckAt(0)], skipPreparation: true,
            autoPassEmptyResponses: autoPass, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Morale.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
        };
    }

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    private static void PushDisasterOnStack(L12GameEngine game, string cardId, string name)
        => game.State.EffectStack.Add(new L12StackItem
        {
            StackItemId = $"disaster-{cardId}", Controller = 0, SourceInstanceId = $"src-{cardId}",
            SourceCardId = cardId, SourceName = name, Trigger = "disaster", Text = "天灾结算",
        });

    private static bool HasMasterDamageReactions(L12GameEngine game)
        => game.State.PendingTriggerStackCandidates.Any(candidate =>
               candidate.Trigger is "reaction" or "master-damaged-by-effect" or "medjed-master-damage")
            || game.State.PendingTriggerBatches.Count > 0
            || game.State.PendingActivations.Count > 0;

    [Fact]
    public void DisasterDamageQueuesNoMasterDamageReactions()
    {
        // 天灾结算期间的主宰伤害：S01-0021 反击、玛格丽特、梅杰德均不得排队。
        var game = Create(2301);
        var player = game.State.Players[0];
        player.Field[1][0] = Card("S01-0021", "s1-counter");
        player.Field[0][0] = Card("S02-0304", "margaret");
        PushDisasterOnStack(game, "S01-DS02", "百鬼夜行");
        var hpBefore = player.Hp;

        Invoke(game, "DamageMasterNonLethal", 0, 1, "〈百鬼夜行〉", null, true);

        Assert.Equal(hpBefore - 1, player.Hp);
        Assert.False(HasMasterDamageReactions(game));

        // 对照组：堆叠上没有天灾时，同类伤害正常排队伤害反应。
        game.State.EffectStack.Clear();
        Invoke(game, "DamageMasterNonLethal", 0, 1, "〈百鬼夜行〉", null, true);
        Assert.True(HasMasterDamageReactions(game));
    }

    [Fact]
    public void FinalDisasterTurnStartDamageQueuesNoDamageReactions()
    {
        // 堙灭的回合开始伤害走灾难专用入口：反击、玛格丽特、圣物抽牌全部不触发。
        var game = Create(2302);
        var player = game.State.Players[0];
        player.Field[1][0] = Card("S01-0021", "final-counter");
        player.Field[0][0] = Card("S02-0304", "final-margaret");
        player.Relic = Card("S02-0305", "final-ring");
        player.Library.Add(Card("S01-0001", "final-would-be-draw"));
        game.State.ActiveDisaster = Card("S01-DS10", "final-disaster");
        var hpBefore = player.Hp;

        Invoke(game, "ResolveTurnStartDisasterEffectIfNeeded");

        Assert.Equal(hpBefore - 1, player.Hp);
        Assert.False(HasMasterDamageReactions(game));
        Assert.DoesNotContain(game.State.EffectStack,
            item => item.Data.GetValueOrDefault("ability") == "anderstorpRingDraw");
    }

    [Fact]
    public void DisasterRuleDamageFromContinuousEffectsQueuesNoReactions()
    {
        // 天灾持续规则导致的伤害（虚构的圣杯/无眠之夜）同样不触发主宰伤害反应。
        var game = Create(2303);
        var player = game.State.Players[0];
        player.Field[1][0] = Card("S01-0021", "continuous-counter");
        player.Field[0][0] = Card("S02-0304", "continuous-margaret");
        var hpBefore = player.Hp;

        Invoke(game, "DamageMasterNonLethalFromDisaster", 0, 1, "〈虚构的圣杯〉：发动圣物效果");

        Assert.Equal(hpBefore - 1, player.Hp);
        Assert.False(HasMasterDamageReactions(game));
    }

    [Fact]
    public void DisasterDepartureReturnsWukongToMasterZoneWithoutQueueingHisEffect()
    {
        // 上位规则（2026-09-23 用户裁定）：主宰变为军团后，任何情况下离场都返回主宰区；
        // 天灾只抑制其离场时效果（士气追加）的触发。天灾清场：返回主宰区，无触发排队。
        var game = Create(2304);
        var player = game.State.Players[0];
        var wukong = Card("S02-01M1", "wukong-master-legion");
        wukong.IsMasterLegion = true;
        player.Field[0][0] = wukong;
        game.State.Players[1].Morale.Add(new L12MoraleCard { InstanceId = "opp-morale", CardId = "S02-0010" });
        PushDisasterOnStack(game, "S01-DS03", "腐秽大地");

        Invoke(game, "RemoveFromField", player, wukong, true, "腐秽大地", false,
            L12FieldLeaveKind.PutIntoGraveyard, false, false);

        Assert.DoesNotContain(wukong, player.Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Type == "return");
        Assert.DoesNotContain(game.State.PendingTriggerStackCandidates,
            candidate => candidate.Trigger == "master-legion-returned");

        // 对照组：非天灾离场返回主宰区并正常排队离场时效果。
        var control = Create(2305);
        var controlPlayer = control.State.Players[0];
        var controlWukong = Card("S02-01M1", "control-wukong");
        controlWukong.IsMasterLegion = true;
        controlPlayer.Field[0][0] = controlWukong;
        control.State.Players[1].Morale.Add(new L12MoraleCard { InstanceId = "control-opp-morale", CardId = "S02-0010" });
        controlPlayer.MoraleDeck.Add(new L12MoraleCard { InstanceId = "control-deck-morale", CardId = "S02-0010" });
        Invoke(control, "RemoveFromField", controlPlayer, controlWukong, true, "普通离场", false,
            L12FieldLeaveKind.PutIntoGraveyard, false, false);
        Assert.DoesNotContain(controlWukong, controlPlayer.Graveyard);
        Assert.Contains(control.State.Events, entry => entry.Type == "return");
        Assert.Contains(control.State.PendingTriggerStackCandidates,
            candidate => candidate.Trigger == "master-legion-returned");
    }

    [Fact]
    public void DisasterMovementDoesNotQueueTsukuyomiTriggers()
    {
        // 风暴乱象的位移不触发月读的两类位移触发（夹具对齐既有月读测试：后排→前排位移）。
        var game = Create(2306, "S02-04M1", autoPass: false);
        var player = game.State.Players[0];
        var moved = Card("S02-0401", "storm-tsukuyomi-moved");
        player.Field[1][0] = moved;
        player.Field[0][2] = Card("S02-0402", "storm-tsukuyomi-target");
        player.Morale.Add(new L12MoraleCard { InstanceId = "storm-m1", CardId = "S02-04C1" });
        player.Morale.Add(new L12MoraleCard { InstanceId = "storm-m2", CardId = "S02-04C1" });
        PushDisasterOnStack(game, "S02-DS04", "风暴乱象");

        Invoke(game, "RecordLegionMovement", 0, moved, 1, 0);

        Assert.Equal(game.State.TurnSerial, moved.LastMovedTurn);
        Assert.DoesNotContain(game.State.PendingTriggerStackCandidates,
            candidate => candidate.Data.GetValueOrDefault("ability")?.StartsWith("tsukuyomi", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation == "trigger-batch-order");

        // 对照组：非天灾位移正常排队月读触发（与既有月读测试同夹具）。
        var control = Create(2307, "S02-04M1", autoPass: false);
        var controlPlayer = control.State.Players[0];
        var controlMoved = Card("S02-0401", "control-tsukuyomi-moved");
        controlPlayer.Field[1][0] = controlMoved;
        controlPlayer.Field[0][2] = Card("S02-0402", "control-tsukuyomi-target");
        controlPlayer.Morale.Add(new L12MoraleCard { InstanceId = "control-m1", CardId = "S02-04C1" });
        controlPlayer.Morale.Add(new L12MoraleCard { InstanceId = "control-m2", CardId = "S02-04C1" });
        Invoke(control, "RecordLegionMovement", 0, controlMoved, 1, 0);
        Assert.True(control.State.PendingTriggerStackCandidates.Any(candidate =>
                candidate.Data.GetValueOrDefault("ability")?.StartsWith("tsukuyomi", StringComparison.Ordinal) == true)
            || control.State.PendingTriggerBatches.Count > 0
            || control.State.PendingPrompts.Count > 0, "对照组应产生月读触发");
    }

    [Fact]
    public void DisasterDiscardDoesNotQueueFaithZealotTrigger()
    {
        // 天灾导致的弃手牌不触发信仰狂热者的弃置时效果。
        var game = Create(2308);
        var player = game.State.Players[0];
        var zealot = Card("S02-0006", "zealot-in-hand");
        player.Hand.Add(zealot);
        PushDisasterOnStack(game, "S02-DS02", "迷雾绝境");

        Invoke(game, "MoveHandToGrave", player, zealot.InstanceId, true, null);

        Assert.Contains(zealot, player.Graveyard);
        Assert.DoesNotContain(game.State.PendingTriggerStackCandidates,
            candidate => candidate.Trigger == "discard-trigger");

        // 对照组：玩家效果弃置正常触发。
        var control = Create(2309);
        var controlPlayer = control.State.Players[0];
        var controlZealot = Card("S02-0006", "control-zealot");
        controlPlayer.Hand.Add(controlZealot);
        Invoke(control, "MoveHandToGrave", controlPlayer, controlZealot.InstanceId, true, null);
        Assert.Contains(control.State.PendingTriggerStackCandidates,
            candidate => candidate.Trigger == "discard-trigger");
    }

    [Fact]
    public void DisasterHandAddCreatesNoAuthorityResponseWindow()
    {
        // 天灾导致的回手/抽牌不登记入手权威事件，对方的粮草掠夺没有响应窗。
        var game = Create(2310);
        var raider = Card("S02-0017", "covered-raider");
        raider.Hidden = true;
        game.State.Players[1].Field[1][0] = raider;
        PushDisasterOnStack(game, "S02-DS04", "风暴乱象");
        game.State.IsResolvingStack = true;

        Invoke(game, "NotifyCardAddedToHandByEffect", game.State.Players[0], Card("S01-0001", "storm-returned"),
            "field", "风暴乱象回手");

        Assert.DoesNotContain(game.State.AuthorityEvents, entry => entry.Type == "effect-hand-add");
        Assert.Empty(game.State.DeferredEffectStack.Where(item => item.Data.GetValueOrDefault("eventType") == "effect-hand-add"));

        // 对照组：普通效果结算期间入手正常登记权威事件（挂入延迟堆叠，随堆叠关闭处理）。
        var control = Create(2311);
        var controlRaider = Card("S02-0017", "control-raider");
        controlRaider.Hidden = true;
        control.State.Players[1].Field[1][0] = controlRaider;
        control.State.IsResolvingStack = true;
        control.State.EffectStack.Add(new L12StackItem
        {
            StackItemId = "control-origin", Controller = 1, SourceInstanceId = "control-origin-src",
            SourceCardId = "S01-0001", SourceName = "黑胡子蒂奇", Trigger = "enter", Text = "登场时",
        });
        Invoke(control, "NotifyCardAddedToHandByEffect", control.State.Players[0],
            Card("S01-0001", "control-returned"), "field", "普通效果回手");
        Assert.Contains(control.State.AuthorityEvents, entry => entry.Type == "effect-hand-add");
    }

}
