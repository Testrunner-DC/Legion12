using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// BUG-20261118-8fe7d553「图坦卡蒙登场叫不出墓地的陵墓守卫」。
// 卡面条件「若我方军团数量少于对方」在结算时判定，而〈图坦卡蒙〉此时已入场并计入我方
// （用户裁定 2026-11-25）；计划阶段必须用同一口径预估，否则玩家先选完墓地守卫才在
// 结算处被「军团数量条件失效」拒付。两条用例钉住同一边界的两侧：
//   我方0张、对方1张 -> 登场后 1 == 1，条件不成立，不给选择步骤；
//   我方0张、对方2张 -> 登场后 1 < 2，条件成立，必须给出选择步骤并完成召唤。
public sealed class TutankhamunTombGuardPlanTests
{
    private const string TutankhamunCardId = "S01-0207";
    private const string TombGuardCardId = "S01-0212";
    private const string FillerLegionCardId = "S01-0002";
    private const string SourceId = "tut-plan-source";
    private const string FirstGuardId = "tut-plan-guard-1";
    private const string SecondGuardId = "tut-plan-guard-2";

    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    // 我方战场在登场前为空，对方按 opposingLegions 张公开军团铺满前排再溢出到后排。
    private static L12GameEngine Create(int seed, int opposingLegions)
    {
        var game = new L12GameEngine(Catalog, "tut-tomb-guard-plan", "TUT-PLAN", seed,
            ["甲", "乙"], [3, 3], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Relic = null;
            player.TemporaryMorale = 0;
            player.Hp = 8;
            foreach (var row in player.Field) Array.Clear(row);
        }
        game.State.PendingPrompts.Clear();
        game.State.PendingActivations.Clear();
        game.State.EffectStack.Clear();
        game.State.Events.Clear();

        var own = game.State.Players[0];
        own.TemporaryMorale = 12; // 图坦卡蒙费用4，用临时士气支付，不参与本用例的判定点。
        own.Hand.Add(Card(TutankhamunCardId, SourceId, 0));
        own.Graveyard.Add(Card(TombGuardCardId, FirstGuardId, 0));
        own.Graveyard.Add(Card(TombGuardCardId, SecondGuardId, 0));
        for (var index = 0; index < opposingLegions; index++)
            game.State.Players[1].Field[index / 3][index % 3] =
                Card(FillerLegionCardId, $"tut-plan-opposing-{index}", 1);
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            ImageUrl = definition.ImageUrl,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }

    private static void Choose(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: choices.Length == 1 ? choices[0] : null,
            CardInstanceIds: choices.Length > 1 ? [.. choices] : null,
            ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
            SourceCardId: prompt.SourceCardId, Step: prompt.Step, CreatedRevision: prompt.CreatedRevision,
            Controller: prompt.Controller));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 12 && game.State.PendingPrompts.Count > 0; safety++)
        {
            Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
            Choose(game, "pass");
        }
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0207")]
    [Trait("L12Evidence", "bug:BUG-20261118-8fe7d553")]
    [Trait("L12Evidence", "entry:tomb-guard-plan-precondition")]
    public void EntryOffersNoGraveGuardDeclarationWhenTheCountConditionCannotHoldAfterEntry()
    {
        // 我方0张、对方1张：图坦卡蒙登场后 1 == 1，卡面条件不成立。
        var game = Create(91501, opposingLegions: 1);
        var player = game.State.Players[0];

        var played = game.Handle(0, new L12Command("playCard", SourceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        Assert.Equal(SourceId, player.Field[0][0]?.InstanceId);

        // 计划阶段就不给选择步骤：任何提示都不得把陵墓守卫列为候选，也不得留下等待声明。
        Assert.DoesNotContain(game.State.PendingPrompts, prompt =>
            prompt.ValidChoices.Contains(FirstGuardId) || prompt.ValidChoices.Contains(SecondGuardId));
        Assert.Empty(game.State.PendingActivations);

        PassResponses(game);

        // 效果结束时不留下等待选择的交互，也没有偷偷登场任何陵墓守卫。
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Equal(2, player.Graveyard.Count(card => card.CardId == TombGuardCardId));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "put"
            && entry.Cards.Any(card => card.CardId == TombGuardCardId));
        Assert.Single(player.Field.SelectMany(row => row).OfType<L12CardInstance>());
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0207")]
    [Trait("L12Evidence", "bug:BUG-20261118-8fe7d553")]
    [Trait("L12Evidence", "entry:tomb-guard-plan-precondition")]
    public void EntryOffersAndCompletesTheGraveGuardSummonWhenTheCountConditionStillHolds()
    {
        // 我方0张、对方2张：图坦卡蒙登场后 1 < 2，卡面条件成立，选择步骤必须出现。
        var game = Create(91502, opposingLegions: 2);
        var player = game.State.Players[0];

        var played = game.Handle(0, new L12Command("playCard", SourceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        Assert.Equal(SourceId, player.Field[0][0]?.InstanceId);
        Assert.True(player.Field.SelectMany(row => row).OfType<L12CardInstance>().Count()
            < game.State.Players[1].Field.SelectMany(row => row).OfType<L12CardInstance>().Count(),
            "登场后我方军团数量仍少于对方，卡面条件成立");

        var cardsPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", cardsPrompt.Continuation);
        Assert.Equal("cards", cardsPrompt.Kind);
        Assert.Contains(FirstGuardId, cardsPrompt.ValidChoices);
        Assert.Contains(SecondGuardId, cardsPrompt.ValidChoices);

        Choose(game, FirstGuardId, SecondGuardId);
        Choose(game, "0:1");
        Choose(game, "0:2");
        Assert.Empty(game.State.PendingActivations);
        var committed = Assert.Single(game.State.EffectStack);
        Assert.Equal(SourceId, committed.SourceInstanceId);
        Assert.Equal("enter", committed.Trigger);
        Assert.Equal($"{FirstGuardId}|{SecondGuardId}", committed.Data["declared:entryCards"]);

        PassResponses(game);

        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Equal(FirstGuardId, player.Field[0][1]?.InstanceId);
        Assert.Equal(SecondGuardId, player.Field[0][2]?.InstanceId);
        Assert.False(player.Field[0][1]!.Tapped);
        Assert.False(player.Field[0][2]!.Tapped);
        Assert.DoesNotContain(player.Graveyard, card => card.CardId == TombGuardCardId);
        Assert.Contains(game.State.Events, entry => entry.Type == "put"
            && entry.Cards.Any(card => card.InstanceId == FirstGuardId));
        Assert.Contains(game.State.Events, entry => entry.Type == "put"
            && entry.Cards.Any(card => card.InstanceId == SecondGuardId));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Cards.Any(card => card.InstanceId == SourceId));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0207")]
    [Trait("L12Evidence", "bug:BUG-20261118-8fe7d553")]
    [Trait("L12Evidence", "entry:tomb-guard-suppressed-records-noop")]
    public void SuppressedEntryLeavesAnObservableNoopFactWithAPlayerFacingReason()
    {
        // 条件不成立时「不登场」是正确行为，但不能静默结束：玩家必须在战报里看到
        // 一条 noop 事实与原因，否则同一个效果在不同路径上有的留事实、有的不留。
        var game = Create(91503, opposingLegions: 1);
        var played = game.Handle(0, new L12Command("playCard", SourceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        PassResponses(game);

        Assert.Contains(game.State.Events, entry => entry.Type == "effect-noop"
            && entry.Cards.Any(card => card.InstanceId == SourceId));
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == SourceId)
            && entry.PlayerLogSemantic is { OutcomeLabel: not null }
            && entry.PlayerLogSemantic!.OutcomeLabel.Contains("原因：", StringComparison.Ordinal));
    }
}
