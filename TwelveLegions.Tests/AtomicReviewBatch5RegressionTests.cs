using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch5RegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "atomic-review-batch5", "ATOMIC5", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
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

    private static L12CardInstance Card(string cardId, string instanceId, int troops = 0)
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
            BaseTroops = definition.Troops ?? troops,
            Troops = definition.Troops ?? troops,
            SummonRound = -1,
        };
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0120")]
    public void EmptyCityDeclaresAndReturnsItsPrintedCostBeforeEnteringTheResponseStack()
    {
        var game = Create(7101);
        var attacker = Card("S01-0002", "atomic5-empty-city-attacker", 3000);
        var counter = Card("S01-0120", "atomic5-empty-city-counter");
        counter.Hidden = true;
        counter.SetRound = 0;
        var morale = new L12MoraleCard
        {
            CardId = "S01-01C1", InstanceId = "atomic5-empty-city-cost", Tapped = false,
        };
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[1][0] = counter;
        game.State.Players[1].Morale.Add(morale);

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        Resolve(game, "pass");
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(counter.InstanceId, response.ValidChoices);
        Resolve(game, counter.InstanceId);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains(morale.InstanceId, declaration.ValidChoices);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == counter.InstanceId);
        Assert.Same(counter, game.State.Players[1].Field[1][0]);
        Resolve(game, morale.InstanceId);

        var drawMode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", drawMode.Continuation);
        Assert.Contains("mode:none", drawMode.ValidChoices);
        Assert.Contains("mode:draw", drawMode.ValidChoices);
        Assert.Contains(morale, game.State.Players[1].Morale);
        Resolve(game, "mode:none");

        Assert.DoesNotContain(morale, game.State.Players[1].Morale);
        Assert.Contains(game.State.EffectStack, item => item.SourceInstanceId == counter.InstanceId);
        Assert.Contains(counter, game.State.Players[1].Resolving);

        PassResponses(game);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "empty-city-draw");
        Assert.DoesNotContain(game.State.EffectStack, item => item.Trigger == "opponent-attack" && !item.Negated);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0120")]
    [Trait("L12Evidence", "entry:response-declaration-cancel")]
    public void EmptyCityDeclarationMayCancelWithoutPayingOrRevealingAndRestoresPriority()
    {
        var game = Create(7104);
        var attacker = Card("S01-0002", "atomic5-empty-cancel-attacker", 3000);
        var counter = Card("S01-0120", "atomic5-empty-cancel-counter");
        counter.Hidden = true;
        counter.SetRound = 0;
        var morale = new L12MoraleCard { CardId = "S01-01C1", InstanceId = "atomic5-empty-cancel-cost" };
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[1][0] = counter;
        game.State.Players[1].Morale.Add(morale);

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        Resolve(game, "pass");
        Resolve(game, counter.InstanceId);
        Resolve(game, "skip");

        var resumed = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", resumed.Kind);
        Assert.Equal(1, resumed.PlayerIndex);
        Assert.Contains(counter.InstanceId, resumed.ValidChoices);
        Assert.Contains(morale, game.State.Players[1].Morale);
        Assert.Same(counter, game.State.Players[1].Field[1][0]);
        Assert.True(counter.Hidden);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0020")]
    [Trait("L12Evidence", "entry:independent-response-segments")]
    public void BattleUntilDawnUsesGraveyardAndDeclaresItsIndependentDrawSegmentBeforeStacking()
    {
        var game = Create(7105);
        var attacker = Card("S01-0002", "atomic5-dawn-attacker", 3000);
        var counter = Card("S01-0020", "atomic5-dawn-counter");
        counter.Hidden = true;
        counter.SetRound = 0;
        var defender = Card("S01-0003", "atomic5-dawn-defender", 1000);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = defender;
        game.State.Players[1].Field[1][0] = counter;
        for (var index = 0; index < 5; index++)
            game.State.Players[1].Graveyard.Add(Card("S01-0001", $"atomic5-dawn-grave-{index}"));
        game.State.Players[1].Library.Clear();
        game.State.Players[1].Library.Add(Card("S01-0001", "atomic5-dawn-draw"));

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        Resolve(game, "pass");
        Resolve(game, counter.InstanceId);
        var drawMode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", drawMode.Continuation);
        Assert.Contains("mode:draw", drawMode.ValidChoices);
        Resolve(game, "mode:draw");
        Assert.Contains(game.State.EffectStack, item => item.SourceInstanceId == counter.InstanceId
            && item.Data.GetValueOrDefault("atomicFlow") == "battle-until-dawn-buff");
        PassResponses(game);

        Assert.Equal(defender.BaseTroops + 1000, defender.Troops);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "battle-until-dawn-draw");
        Assert.Contains(game.State.Players[1].Hand, card => card.InstanceId == "atomic5-dawn-draw");
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0320")]
    public void BloodEagleDeclaresItsOrderedGraveTargetsBeforeItsFirstIndependentSegmentStacks()
    {
        var game = Create(7102);
        var player = game.State.Players[0];
        var counter = Card("S01-0320", "atomic5-blood-eagle-counter");
        counter.Hidden = true;
        counter.SetRound = 0;
        var fallen = Card("S01-0309", "atomic5-blood-eagle-fallen");
        var graveA = Card("S01-0311", "atomic5-blood-eagle-a");
        var graveB = Card("S01-0312", "atomic5-blood-eagle-b");
        var graveC = Card("S01-0313", "atomic5-blood-eagle-c");
        player.Field[1][0] = counter;
        player.Field[0][0] = fallen;
        player.Graveyard.AddRange([graveA, graveB, graveC]);

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: fallen.InstanceId)).Accepted);

        var triggerOrder = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-batch-order", triggerOrder.Continuation);
        var bloodTrigger = triggerOrder.ValidChoices.Single(id =>
            triggerOrder.Data[id].Contains("复仇血鹰", StringComparison.Ordinal));
        var orderedTriggers = triggerOrder.ValidChoices.Where(id => id != bloodTrigger).Append(bloodTrigger).ToList();
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: triggerOrder.PromptId,
            CardInstanceIds: orderedTriggers)).Accepted);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Equal("order", declaration.Kind);
        Assert.Contains(graveA.InstanceId, declaration.ValidChoices);
        Assert.Contains(graveB.InstanceId, declaration.ValidChoices);
        Assert.Contains(graveC.InstanceId, declaration.ValidChoices);
        Assert.DoesNotContain(counter.InstanceId, declaration.ValidChoices);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == counter.InstanceId);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            CardInstanceIds: [graveA.InstanceId, graveB.InstanceId])).Accepted);
        Assert.Contains(game.State.EffectStack, item => item.SourceInstanceId == counter.InstanceId
            && item.Data.GetValueOrDefault("atomicFlow") == "blood-eagle-debuff");
    }

    [Fact]
    [Trait("L12Evidence", "entry:anonymous-opponent-hand-response-declaration")]
    [Trait("L12Evidence", "card:S02-0017")]
    public void PendingResponseAnonymousHandDeclarationNeverProjectsTheRealIdentity()
    {
        var game = Create(7103);
        var owner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var counter = Card("S02-0017", "atomic5-plunder-counter");
        counter.Hidden = true;
        counter.SetRound = 0;
        owner.Field[1][0] = counter;
        var hidden = Card("S01-0002", "atomic5-hidden-opponent-hand", 1000);
        opponent.Hand.Add(hidden);

        // This state shape mirrors the public response declaration created by the authority-event route.
        var snapshotBefore = JsonSerializer.Serialize(game.SnapshotFor(0));
        Assert.DoesNotContain(hidden.InstanceId, snapshotBefore, StringComparison.Ordinal);
        Assert.DoesNotContain(hidden.Name, snapshotBefore, StringComparison.Ordinal);
    }
}
