using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SingleActiveDrawPresentationTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine CreateWithFirstMaster(string masterId, int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = $"{masterId}单段抽牌效果回归",
            MasterId = masterId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        return new L12GameEngine(Catalog, "single-active-draw", "SINGLE-ACTIVE-DRAW", seed,
            ["甲", "乙"], [deck, baseDeck], skipPreparation: true);
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

    private static void PrepareMain(L12GameEngine game)
    {
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
    }

    private static void HoldOpponentResponseWindow(L12GameEngine game)
    {
        var opponent = game.State.Players[1];
        var counter = Card("S01-0019", $"single-draw-response-{game.State.StackSequence}");
        counter.Hidden = true;
        counter.SetRound = 0;
        opponent.Field[1][2] = counter;
        opponent.Field[0][2] ??= Card("S01-0004", $"single-draw-target-{game.State.StackSequence}");
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

    private static L12ActionEvent Result(L12GameEngine game, string sourceInstanceId)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == sourceInstanceId));

    private static L12MoraleCard AddReadyGodPower(L12PlayerState player, string instanceId)
    {
        var power = new L12MoraleCard
        {
            InstanceId = instanceId,
            CardId = "S02-05C1",
            IsGodPower = true,
        };
        player.Morale.Add(power);
        return power;
    }

    [Fact]
    [Trait("L12Evidence", "ability:godPowerDraw")]
    public void GodPowerDrawPublishesResolvedAndUsesTheSameSegmentAfterCheckpoint()
    {
        var game = CreateWithFirstMaster("S02-05M1", 91321);
        var player = game.State.Players[0];
        PrepareMain(game);
        player.Library.Clear();
        player.Hand.Clear();
        var drawn = Card("S01-0101", "god-power-drawn");
        player.Library.Add(drawn);
        var power = AddReadyGodPower(player, "god-power-draw-cost");
        HoldOpponentResponseWindow(game);

        Assert.True(game.Handle(0, new L12Command("activateAbility", power.InstanceId,
            Ability: "godPowerDraw")).Accepted);
        Assert.True(power.Tapped);
        Assert.False(power.IsGodPower);
        var responsePrompt = Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        var stack = Assert.Single(game.State.EffectStack);
        Assert.False(string.IsNullOrWhiteSpace(stack.Data.GetValueOrDefault("presentationSceneId")));
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

        PassResponses(game);

        Assert.Contains(game.State.Players[0].Hand, card => card.InstanceId == drawn.InstanceId);
        var result = Result(game, power.InstanceId);
        Assert.Equal("resolved", result.EffectResultStatus);
        Assert.Equal(1, result.EffectSegmentIndex);
        Assert.Equal(1, result.EffectSegmentCount);
        Assert.False(game.Handle(responsePrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: responsePrompt.PromptId, Choice: "pass")).Accepted);
    }

    [Fact]
    [Trait("L12Evidence", "ability:godPowerDraw")]
    public void NegatedGodPowerDrawKeepsItsPaidGodPowerAndDrawsNothing()
    {
        var game = CreateWithFirstMaster("S02-05M1", 91322);
        var player = game.State.Players[0];
        PrepareMain(game);
        player.Library.Clear();
        player.Hand.Clear();
        player.Library.Add(Card("S01-0101", "god-power-negated-draw"));
        var power = AddReadyGodPower(player, "god-power-negated-cost");
        HoldOpponentResponseWindow(game);

        Assert.True(game.Handle(0, new L12Command("activateAbility", power.InstanceId,
            Ability: "godPowerDraw")).Accepted);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(power.Tapped);
        Assert.False(power.IsGodPower);
        Assert.Empty(player.Hand);
        Assert.Single(player.Library);
        Assert.Contains(player.UsedAbilities, key => key.Contains("godPowerDraw", StringComparison.Ordinal));
        Assert.Equal("negated", Result(game, power.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:godPowerDraw")]
    public void GodPowerDrawWithAnEmptyLibraryPublishesFailedInsteadOfResolved()
    {
        var game = CreateWithFirstMaster("S02-05M1", 91323);
        var player = game.State.Players[0];
        PrepareMain(game);
        player.Library.Clear();
        player.Hand.Clear();
        var power = AddReadyGodPower(player, "god-power-empty-cost");
        HoldOpponentResponseWindow(game);

        Assert.True(game.Handle(0, new L12Command("activateAbility", power.InstanceId,
            Ability: "godPowerDraw")).Accepted);
        PassResponses(game);

        Assert.True(power.Tapped);
        Assert.False(power.IsGodPower);
        Assert.Equal(1, game.State.Winner);
        Assert.Equal("failed", Result(game, power.InstanceId).EffectResultStatus);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("牌库为空", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:sifCycle")]
    public void SifCycleReturnsItsCostBeforeResponseAndPublishesResolvedAfterCheckpoint()
    {
        var game = CreateWithFirstMaster("ST03-M1", 91324);
        var player = game.State.Players[0];
        PrepareMain(game);
        player.Library.Clear();
        player.Hand.Clear();
        player.Graveyard.Clear();
        var drawn = Card("ST01-01", "sif-cycle-drawn");
        var costs = new[]
        {
            Card("ST03-01", "sif-cycle-cost-1"),
            Card("ST03-02", "sif-cycle-cost-2"),
            Card("ST03-03", "sif-cycle-cost-3"),
        };
        player.Library.Add(drawn);
        player.Graveyard.AddRange(costs);
        HoldOpponentResponseWindow(game);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "sifCycle")).Accepted);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: costPrompt.PromptId,
            CardInstanceIds: costs.Select(card => card.InstanceId).ToList())).Accepted);
        Assert.All(costs, cost => Assert.Contains(player.Library, card => card.InstanceId == cost.InstanceId));
        Assert.Empty(player.Graveyard);
        var responsePrompt = Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

        PassResponses(game);

        Assert.Contains(game.State.Players[0].Hand, card => card.InstanceId == drawn.InstanceId);
        Assert.All(costs, cost => Assert.Contains(game.State.Players[0].Library,
            card => card.InstanceId == cost.InstanceId));
        Assert.Equal("resolved", Result(game, "master-0").EffectResultStatus);
        Assert.False(game.Handle(responsePrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: responsePrompt.PromptId, Choice: "pass")).Accepted);
        Assert.False(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "sifCycle")).Accepted);
    }

    [Fact]
    [Trait("L12Evidence", "ability:sifCycle")]
    public void NegatedSifCycleKeepsReturnedGraveCostAndDrawsNothing()
    {
        var game = CreateWithFirstMaster("ST03-M1", 91325);
        var player = game.State.Players[0];
        PrepareMain(game);
        player.Library.Clear();
        player.Hand.Clear();
        player.Graveyard.Clear();
        var drawn = Card("ST01-01", "sif-negated-draw");
        var costs = new[]
        {
            Card("ST03-01", "sif-negated-cost-1"),
            Card("ST03-02", "sif-negated-cost-2"),
            Card("ST03-03", "sif-negated-cost-3"),
        };
        player.Library.Add(drawn);
        player.Graveyard.AddRange(costs);
        HoldOpponentResponseWindow(game);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "sifCycle")).Accepted);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: costPrompt.PromptId,
            CardInstanceIds: costs.Select(card => card.InstanceId).ToList())).Accepted);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.Empty(player.Graveyard);
        Assert.Empty(player.Hand);
        Assert.Equal(drawn.InstanceId, player.Library[0].InstanceId);
        Assert.All(costs, cost => Assert.Contains(player.Library, card => card.InstanceId == cost.InstanceId));
        Assert.Equal("negated", Result(game, "master-0").EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:sifCycle")]
    public void SifCyclePublishesFailedIfEarlierResponsesRemoveEveryCardFromTheLibrary()
    {
        var game = CreateWithFirstMaster("ST03-M1", 91326);
        var player = game.State.Players[0];
        PrepareMain(game);
        player.Library.Clear();
        player.Hand.Clear();
        player.Graveyard.Clear();
        var costs = new[]
        {
            Card("ST03-01", "sif-empty-cost-1"),
            Card("ST03-02", "sif-empty-cost-2"),
            Card("ST03-03", "sif-empty-cost-3"),
        };
        player.Graveyard.AddRange(costs);
        HoldOpponentResponseWindow(game);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "sifCycle")).Accepted);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: costPrompt.PromptId,
            CardInstanceIds: costs.Select(card => card.InstanceId).ToList())).Accepted);
        Assert.Equal(3, player.Library.Count);
        player.Removed.AddRange(player.Library);
        player.Library.Clear();

        PassResponses(game);

        Assert.Empty(player.Hand);
        Assert.Empty(player.Graveyard);
        Assert.Equal(1, game.State.Winner);
        Assert.Equal("failed", Result(game, "master-0").EffectResultStatus);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("西芙", StringComparison.Ordinal));
    }
}
