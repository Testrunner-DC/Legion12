using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SingleActiveStatePresentationTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine CreateWithFirstMaster(string masterId, int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = $"{masterId}单段状态效果回归",
            MasterId = masterId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        return new L12GameEngine(Catalog, "single-active-state", "SINGLE-ACTIVE-STATE", seed,
            ["甲", "乙"], [deck, baseDeck], skipPreparation: true);
    }

    private static L12GameEngine Create(int seed)
        => new(Catalog, "single-active-state", "SINGLE-ACTIVE-STATE", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true);

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

    private static void PrepareMain(L12GameEngine game)
    {
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
    }

    private static void AddReadyMorale(L12PlayerState player, int count)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"single-state-morale-{player.PlayerIndex}-{index}",
                CardId = "S01-01C1",
            });
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

    private static void HoldOpponentResponseWindow(L12GameEngine game)
    {
        var opponent = game.State.Players[1];
        var counter = Card("S01-0019", $"single-state-response-{game.State.StackSequence}");
        counter.Hidden = true;
        counter.SetRound = 0;
        opponent.Field[1][2] = counter;
        opponent.Field[0][2] ??= Card("S01-0004", $"single-state-response-target-{game.State.StackSequence}");
    }

    private static L12ActionEvent Result(L12GameEngine game, string cardId)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == cardId));

    [Fact]
    [Trait("L12Evidence", "ability:thorCharge")]
    public void ThorChargePublishesOneResolvedTargetlessSegment()
    {
        var game = CreateWithFirstMaster("S02-03M1", 91301);
        var player = game.State.Players[0];
        PrepareMain(game);
        player.Hp = 3;
        AddReadyMorale(player, 2);
        HoldOpponentResponseWindow(game);

        Assert.True(game.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "thorCharge")).Accepted);
        var item = Assert.Single(game.State.EffectStack);
        Assert.Empty(item.Targets);
        Assert.False(string.IsNullOrWhiteSpace(item.Data.GetValueOrDefault("presentationSceneId")));

        PassResponses(game);

        Assert.True(player.MasterCannotHeal);
        Assert.Contains($"s2-thor-charge:{game.State.TurnSerial}", player.UsedAbilities);
        var result = Result(game, "S02-03M1");
        Assert.Equal("resolved", result.EffectResultStatus);
        Assert.Equal(1, result.EffectSegmentIndex);
        Assert.Equal(1, result.EffectSegmentCount);
    }

    [Fact]
    [Trait("L12Evidence", "ability:thorCharge")]
    public void NegatedThorChargeKeepsPaidMoraleAndPublishesNegatedResult()
    {
        var game = CreateWithFirstMaster("S02-03M1", 91302);
        var player = game.State.Players[0];
        PrepareMain(game);
        player.Hp = 3;
        AddReadyMorale(player, 2);
        HoldOpponentResponseWindow(game);

        Assert.True(game.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "thorCharge")).Accepted);
        Assert.All(player.Morale, morale => Assert.True(morale.Tapped));
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.False(player.MasterCannotHeal);
        Assert.All(player.Morale, morale => Assert.True(morale.Tapped));
        Assert.Equal("negated", Result(game, "S02-03M1").EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:skyCityDiscount")]
    public void SkyCityDiscountSurvivesCheckpointAndRejectsTheOldResponsePromptTwice()
    {
        var game = Create(91304);
        PrepareMain(game);
        var trial = Card("ST06-S1", "sky-city-single-state");
        trial.TrialCompleted = true;
        game.State.Players[0].SpecialZones.Trials.Add(trial);
        HoldOpponentResponseWindow(game);

        Assert.True(game.Handle(0, new L12Command("activateAbility", trial.InstanceId,
            Ability: "skyCityDiscount")).Accepted);
        var responsePrompt = Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

        PassResponses(game);

        Assert.Equal(1, game.State.Players[0].NextOtherworldLegionEntryDiscount);
        var result = Result(game, "ST06-S1");
        Assert.Equal("resolved", result.EffectResultStatus);
        Assert.Equal(1, result.EffectSegmentIndex);
        Assert.Equal(1, result.EffectSegmentCount);
        Assert.False(game.Handle(responsePrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: responsePrompt.PromptId, Choice: "pass")).Accepted);
        Assert.False(game.Handle(0, new L12Command("activateAbility", trial.InstanceId,
            Ability: "skyCityDiscount")).Accepted);
    }
}
