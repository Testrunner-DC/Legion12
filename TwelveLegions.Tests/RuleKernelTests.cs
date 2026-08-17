using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RuleKernelTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12PlayerState Player() => new()
    {
        PlayerIndex = 0,
        Name = "测试者",
        DeckName = "测试牌库",
        Faction = "tianting",
        MasterId = "S01-01M1",
        MasterName = "杨戬",
        Hp = 8,
        MaxHp = 8,
    };

    private static L12CardInstance Card(string id, int troops = 2000, string? name = null, string faction = "tianting", string[]? traits = null) => new()
    {
        InstanceId = id,
        CardId = id,
        Name = name ?? id,
        CardType = "legion",
        Faction = faction,
        Cost = 1,
        Traits = traits is null ? [] : [.. traits],
        BaseTroops = troops,
        Troops = troops,
    };

    [Fact]
    public void DrawIsAtomicWhenLibraryIsTooSmall()
    {
        var player = Player(); player.Library.Add(Card("a"));
        var result = L12LibraryOps.Draw(player, 2);
        Assert.False(result.Success); Assert.Single(player.Library); Assert.Empty(player.Hand);
    }

    [Fact]
    public void DrawMovesExactlyTheRequestedTopCards()
    {
        var player = Player(); player.Library.AddRange([Card("a"), Card("b"), Card("c")]);
        var result = L12LibraryOps.Draw(player, 2);
        Assert.True(result.Success); Assert.Equal(["a", "b"], player.Hand.Select(card => card.InstanceId)); Assert.Equal("c", Assert.Single(player.Library).InstanceId);
    }

    [Fact]
    public void ViewTopDoesNotMutateTheLibrary()
    {
        var player = Player(); player.Library.AddRange([Card("a"), Card("b")]);
        var result = L12LibraryOps.ViewTop(player, 2);
        Assert.True(result.Success); Assert.Equal(2, player.Library.Count); Assert.Empty(player.Hand); Assert.Empty(player.Graveyard);
    }

    [Fact]
    public void MillIsAtomicAndMovesCardsToGraveyard()
    {
        var player = Player(); player.Library.AddRange([Card("a"), Card("b")]);
        Assert.False(L12LibraryOps.Mill(player, 3).Success); Assert.Equal(2, player.Library.Count);
        Assert.True(L12LibraryOps.Mill(player, 2).Success); Assert.Empty(player.Library); Assert.Equal(2, player.Graveyard.Count);
    }

    [Fact]
    public void SearchAndTopBottomOperationsPreserveDeclaredOrder()
    {
        var player = Player(); var a = Card("a"); var b = Card("b"); var c = Card("c");
        player.Library.AddRange([a, b, c]);
        Assert.Equal(["b"], L12LibraryOps.Search(player, card => card.InstanceId == "b").Select(card => card.InstanceId));
        Assert.True(L12LibraryOps.PutOnBottom(player, [a])); Assert.Equal(["b", "c", "a"], player.Library.Select(card => card.InstanceId));
        Assert.True(L12LibraryOps.PutOnTop(player, [a, c])); Assert.Equal(["a", "c", "b"], player.Library.Select(card => card.InstanceId));
    }

    [Fact]
    public void ReorderTopRequiresExactlyTheCurrentTopSet()
    {
        var player = Player(); player.Library.AddRange([Card("a"), Card("b"), Card("c")]);
        Assert.False(L12LibraryOps.ReorderTop(player, ["a", "c"]));
        Assert.True(L12LibraryOps.ReorderTop(player, ["b", "a"]));
        Assert.Equal(["b", "a", "c"], player.Library.Select(card => card.InstanceId));
    }

    [Fact]
    public void ImmortalSetValueIsFollowedByContinuousModifiers()
    {
        var card = Card("legion", 5000);
        L12DerivedStats.ApplyContinuousModifier(card, -2000, 4);
        L12DerivedStats.SetUntilTurnEnd(card, 1000, 4);
        Assert.Equal(-1000, card.Troops);
        L12DerivedStats.ResetForCompletedTurn(card, 4);
        Assert.Equal(3000, card.Troops);
    }

    [Fact]
    public void TriggerPlannerBuildsActivePlayerBatchBeforeOpponentBatch()
    {
        static L12TriggerCandidate Trigger(string id, int controller) => new()
        {
            CandidateId = id, Controller = controller, SourceInstanceId = id, SourceCardId = id,
            SourceName = id, Trigger = "death", Text = "阵亡时",
        };
        var plan = L12TriggerBatchPlanner.Plan([Trigger("enemy", 1), Trigger("mine-a", 0), Trigger("mine-b", 0)], 0);
        Assert.Equal(2, plan.Count); Assert.All(plan[0], trigger => Assert.Equal(0, trigger.Controller)); Assert.Equal(1, plan[1][0].Controller);
    }

    [Fact]
    public void SimultaneousDeathTriggersBuildActivePlayersStackBatchBeforeOpponentsBatch()
    {
        var game = new L12GameEngine(Catalog, "trigger-integration", "TRIGGER", 8131,
            ["甲", "乙"], [0, 0], skipPreparation: true);
        static L12CardInstance Take(L12PlayerState player, string cardId)
        {
            var card = player.Hand.Concat(player.Library).First(candidate => candidate.CardId == cardId);
            player.Hand.Remove(card); player.Library.Remove(card); return card;
        }
        var attacker = Take(game.State.Players[0], "S01-0102");
        var defender = Take(game.State.Players[1], "S01-0102");
        attacker.Troops = defender.Troops = 1000;
        attacker.SummonRound = defender.SummonRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = defender;
        var response = Take(game.State.Players[0], "S01-0016");
        response.Hidden = true; response.SetRound = 0;
        game.State.Players[0].Field[1][1] = response;
        game.State.Players[0].Hand.Add(Take(game.State.Players[0], "S01-0103"));
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", defender.InstanceId))).Accepted);
        while (game.State.EffectStack.LastOrDefault()?.Trigger == "attack"
            && game.State.PendingPrompts.FirstOrDefault() is { } prompt
            && prompt.ValidChoices.Contains("pass"))
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);

        Assert.Equal(2, game.State.EffectStack.Count);
        Assert.Equal(0, game.State.EffectStack[0].Controller);
        Assert.Equal(1, game.State.EffectStack[1].Controller);
        var responsePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, responsePrompt.PlayerIndex);
        Assert.Contains(response.InstanceId, responsePrompt.ValidChoices);
    }

    [Fact]
    public void PendingActivationDeclaresAndValidatesTargetBeforePaying()
    {
        var game = new L12GameEngine(Catalog, "pending", "PENDING", 8128, ["甲", "乙"], [1, 1], skipPreparation: true);
        var player = game.State.Players[0];
        while (player.Morale.Count < 2) { var morale = player.MoraleDeck[0]; player.MoraleDeck.RemoveAt(0); player.Morale.Add(morale); }
        var target = player.Hand.Concat(player.Library).First(card => card.CardType == "legion" && card.Faction == "gaotianyuan");
        player.Hand.Remove(target); player.Library.Remove(target); player.Field[0][0] = target;
        game.State.ActivePlayer = 0; game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "S01-04M2", Ability: "frontBuff")).Accepted);
        Assert.Single(game.State.PendingActivations);
        Assert.Equal(2, player.Morale.Count(card => !card.Tapped));
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: target.InstanceId)).Accepted);
        Assert.Empty(game.State.PendingActivations);
        Assert.Equal(1, player.Morale.Count(card => !card.Tapped));
    }

    [Fact]
    public void InvalidatedPendingTargetDoesNotPayCost()
    {
        var game = new L12GameEngine(Catalog, "pending-invalid", "PENDINV", 8129, ["甲", "乙"], [1, 1], skipPreparation: true);
        var player = game.State.Players[0];
        while (player.Morale.Count < 2) { var morale = player.MoraleDeck[0]; player.MoraleDeck.RemoveAt(0); player.Morale.Add(morale); }
        var target = player.Hand.Concat(player.Library).First(card => card.CardType == "legion" && card.Faction == "gaotianyuan");
        player.Hand.Remove(target); player.Library.Remove(target); player.Field[0][0] = target;
        game.State.ActivePlayer = 0; game.State.Phase = L12Phase.Main;
        Assert.True(game.Handle(0, new L12Command("activateAbility", "S01-04M2", Ability: "frontBuff")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts); player.Field[0][0] = null;
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: target.InstanceId)).Accepted);
        Assert.Equal(2, player.Morale.Count(card => !card.Tapped));
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    public void S2RuneAndGodPowerCostsAreAtomic()
    {
        var player = Player();
        L12S2ZoneOps.GainRunes(player, 2);
        Assert.False(L12S2ZoneOps.SpendRunes(player, 3)); Assert.Equal(2, player.SpecialZones.Runes);
        Assert.True(L12S2ZoneOps.SpendRunes(player, 2)); Assert.Equal(0, player.SpecialZones.Runes);
        player.SpecialZones.GodPower.AddRange([Card("power-a"), Card("power-b")]);
        Assert.False(L12S2ZoneOps.ConsumeAndFlipGodPower(player, 3)); Assert.All(player.SpecialZones.GodPower, card => Assert.False(card.Tapped));
        Assert.True(L12S2ZoneOps.ConsumeAndFlipGodPower(player, 2)); Assert.All(player.SpecialZones.GodPower, card => Assert.True(card.Tapped));
    }

    [Fact]
    public void S2PromotionKeepsSharedStateAndAttachesFoundation()
    {
        var player = Player();
        var foundation = Card("foundation", 4000, "赫拉克勒斯", "olympus"); foundation.Tapped = true; foundation.HasStrongAttack = true;
        var promoted = Card("promoted", 6000, "赫拉克勒斯·晋升", "olympus", ["奥林匹斯", "晋升者"]);
        player.Field[0][1] = foundation; player.Hand.Add(promoted);
        player.SpecialZones.GodPower.AddRange([Card("power-a"), Card("power-b")]);
        Assert.True(L12S2ZoneOps.Promote(player, foundation, promoted, 2));
        Assert.Same(promoted, player.Field[0][1]); Assert.True(promoted.Tapped); Assert.True(promoted.HasStrongAttack);
        Assert.Same(foundation, Assert.Single(promoted.AttachedCards)); Assert.Empty(player.Hand);
    }

    [Fact]
    public void MultiStepActivationDeclaresCardAndPositionBeforeReturningMorale()
    {
        var game = new L12GameEngine(Catalog, "pending-multi", "PENDMULT", 8130, ["甲", "乙"], [0, 0], skipPreparation: true);
        var player = game.State.Players[0];
        while (player.Morale.Count < 2) { var morale = player.MoraleDeck[0]; player.MoraleDeck.RemoveAt(0); player.Morale.Add(morale); }
        var xishi = Card("S01-0116", 1000, "西施");
        var summon = Card("summon", 2000, "测试军团");
        player.Field[0][0] = xishi; player.Hand.Add(summon);
        game.State.ActivePlayer = 0; game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", xishi.InstanceId, Ability: "xishiExchange")).Accepted);
        var cardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: cardPrompt.PromptId, Choice: summon.InstanceId)).Accepted);
        Assert.Equal(2, player.Morale.Count); Assert.Single(game.State.PendingActivations);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId, Choice: "0:1")).Accepted);
        Assert.Single(player.Morale); Assert.Same(summon, player.Field[0][1]); Assert.Contains(xishi, player.Graveyard);
    }
}
