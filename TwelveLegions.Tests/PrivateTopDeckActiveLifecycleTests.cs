using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PrivateTopDeckActiveLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "private-top-deck-active", "PRIVATE-TOP-DECK-ACTIVE",
            seed, ["甲", "乙"], [3, 3], skipPreparation: true,
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
            foreach (var row in player.Field) Array.Clear(row);
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
        };
    }

    private static L12MoraleCard AddGodPower(L12PlayerState player, string id)
    {
        var power = new L12MoraleCard { InstanceId = id, CardId = "S02-05C1", IsGodPower = true };
        player.Morale.Add(power);
        return power;
    }

    private static void HoldResponse(L12GameEngine game)
    {
        var opponent = game.State.Players[1];
        var counter = Card("S01-0019", "top-deck-response");
        counter.Hidden = true;
        counter.SetRound = 0;
        opponent.Field[1][2] = counter;
        opponent.Field[0][2] = Card("S01-0004", "top-deck-response-target");
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static L12ActionEvent Result(L12GameEngine game, string sourceId)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == sourceId));

    [Fact]
    public void TopDeckPrivateTransactionsExposeDedicatedStructuredScenes()
    {
        var shanhe = Catalog.AtomicEffects.Find("S01-0117")!.Abilities.Single(ability => ability.Sequence == 2);
        Assert.Contains(shanhe.Presentations, scene => scene.Flow == "shanhe-draw");
        Assert.Contains(shanhe.Presentations, scene => scene.Flow == "shanhe-top-three");
        Assert.Contains(Catalog.AtomicEffects.Find("S02-05M2")!.Abilities.Single().Presentations,
            scene => scene.Flow == "prometheus-top-three");
        Assert.Contains(Catalog.AtomicEffects.Find("ST05-06")!.Abilities.Single().Presentations,
            scene => scene.Flow == "telemachus-top-three");
    }

    [Fact]
    [Trait("L12Evidence", "ability:prometheusTopThree")]
    public void PrometheusRequiresTheEligiblePickAndRestoresItsPrivatePromptFromCheckpoint()
    {
        var game = Create(91361);
        var player = game.State.Players[0];
        var source = Card("S02-05M2", "prometheus-required-source");
        player.Field[0][0] = source;
        var power = AddGodPower(player, "prometheus-required-power");
        var eligible = Card("S02-0502", "prometheus-required-eligible");
        var otherA = Card("S02-0003", "prometheus-required-other-a");
        var otherB = Card("S02-0402", "prometheus-required-other-b");
        player.Library.AddRange([eligible, otherA, otherB]);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "prometheusTopThree")).Accepted);
        PassResponses(game);
        var firstPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-prometheus-pick", firstPrompt.Data["action"]);
        Assert.Equal("required-add", firstPrompt.Data["choiceMode"]);
        Assert.Equal([eligible.InstanceId], firstPrompt.ValidChoices);
        Assert.DoesNotContain("skip", firstPrompt.ValidChoices);

        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        var restored = Assert.Single(game.State.PendingPrompts);
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: restored.PromptId,
            Choice: "skip")).Accepted);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: restored.PromptId,
            Choice: eligible.InstanceId)).Accepted);
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: order.PromptId,
            BottomCardInstanceIds: [otherB.InstanceId, otherA.InstanceId])).Accepted);

        Assert.Contains(game.State.Players[0].Hand, card => card.InstanceId == eligible.InstanceId);
        Assert.True(power.Tapped);
        var result = Result(game, source.InstanceId);
        Assert.Equal("resolved", result.EffectResultStatus);
        Assert.Equal(1, result.EffectSegmentIndex);
        Assert.Equal(1, result.EffectSegmentCount);
    }

    [Fact]
    [Trait("L12Evidence", "ability:prometheusTopThree")]
    public void PrometheusWithoutAnEligibleTopCardGoesDirectlyToRequiredReorder()
    {
        var game = Create(91362);
        var player = game.State.Players[0];
        var source = Card("S02-05M2", "prometheus-miss-source");
        player.Field[0][0] = source;
        AddGodPower(player, "prometheus-miss-power");
        var top = new[]
        {
            Card("S02-0003", "prometheus-miss-a"),
            Card("S02-0402", "prometheus-miss-b"),
            Card("S01-0101", "prometheus-miss-c"),
        };
        player.Library.AddRange(top);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "prometheusTopThree")).Accepted);
        PassResponses(game);

        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("reorder-order", order.Data["action"]);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-prometheus-pick");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: order.PromptId,
            BottomCardInstanceIds: top.Reverse().Select(card => card.InstanceId).ToList())).Accepted);
        Assert.Equal("resolved", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:prometheusTopThree")]
    public void PrometheusEmptyLibrarySkipsAndKeepsItsPaidGodPower()
    {
        var game = Create(91363);
        var player = game.State.Players[0];
        var source = Card("S02-05M2", "prometheus-empty-source");
        player.Field[0][0] = source;
        var power = AddGodPower(player, "prometheus-empty-power");

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "prometheusTopThree")).Accepted);
        PassResponses(game);

        Assert.True(power.Tapped);
        Assert.True(power.IsGodPower);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal("skipped", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:prometheusTopThree")]
    public void NegatedPrometheusDoesNotRevealTopCardsAndKeepsItsPaidCost()
    {
        var game = Create(91364);
        var player = game.State.Players[0];
        var source = Card("S02-05M2", "prometheus-negated-source");
        player.Field[0][0] = source;
        var power = AddGodPower(player, "prometheus-negated-power");
        player.Library.Add(Card("S02-0502", "prometheus-negated-top"));
        HoldResponse(game);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "prometheusTopThree")).Accepted);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(power.Tapped);
        Assert.Single(player.Library);
        Assert.Empty(player.Hand);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal("negated", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:telemachusTopThree")]
    public void TelemachusEmptyLibraryStillPaysActiveRestAndPublishesSkipped()
    {
        var game = Create(91365);
        var player = game.State.Players[0];
        var source = Card("ST05-06", "telemachus-empty-source");
        player.Field[0][0] = source;

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "telemachusTopThree")).Accepted);
        var decision = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: decision.PromptId,
            Choice: "mode:use")).Accepted);
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal("skipped", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:artifactSearch")]
    public void ShanheEmptyLibraryKeepsDiscardCostAndPublishesSkipped()
    {
        var game = Create(91366);
        var player = game.State.Players[0];
        var source = Card("S01-0117", "shanhe-empty-source");
        var discard = Card("S01-0101", "shanhe-empty-cost");
        player.Relic = source;
        player.Hand.Add(discard);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "artifactSearch")).Accepted);
        var cost = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: cost.PromptId,
            Choice: discard.InstanceId)).Accepted);
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Contains(discard, player.Graveyard);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal("skipped", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:artifactDraw")]
    public void ShanheDrawEmptyLibraryPublishesFailedAfterReturningItsActiveMorale()
    {
        var game = Create(91367);
        var player = game.State.Players[0];
        var source = Card("S01-0117", "shanhe-draw-empty-source");
        player.Relic = source;
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = "shanhe-draw-empty-cost",
            CardId = "S01-01C1",
        });

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "artifactDraw")).Accepted);
        if (game.State.PendingPrompts.FirstOrDefault() is { Kind: not "response" } payment)
        {
            Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId,
                CardInstanceIds: ["shanhe-draw-empty-cost"])).Accepted);
        }
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Empty(player.Morale);
        Assert.Equal(1, game.State.Winner);
        Assert.Equal("failed", Result(game, source.InstanceId).EffectResultStatus);
    }
}
