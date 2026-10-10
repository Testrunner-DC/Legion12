using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class NephthysSacrificeLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "奈芙蒂斯弃置生命周期",
            MasterId = "S02-02M1",
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "nephthys-sacrifice", "NEPHTHYS-SACRIFICE", seed,
            ["甲", "乙"], [deck, baseDeck], skipPreparation: true,
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
            player.ExtraRelics.Clear();
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
            OwnerIndex = 0,
        };
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

    private static (L12CardInstance First, L12CardInstance Second, L12Prompt Prompt) Begin(L12GameEngine game)
    {
        var first = Card("S02-0004", "nephthys-first");
        var second = Card("S02-0005", "nephthys-second");
        game.State.Players[0].Field[0][0] = first;
        game.State.Players[0].Field[1][1] = second;
        var result = game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "nephthysSacrifice"));
        Assert.True(result.Accepted, result.Error);
        return (first, second, Assert.Single(game.State.PendingPrompts));
    }

    private static void Declare(L12GameEngine game, L12Prompt prompt, params string[] ids)
    {
        var result = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [.. ids]));
        Assert.True(result.Accepted, result.Error);
    }

    private static L12ActionEvent Result(L12GameEngine game)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == "S02-02M1"));

    [Fact]
    public void PrintedSecondAbilityUsesOneStructuredNonCostScene()
    {
        var ability = Catalog.AtomicEffects.Find("S02-02M1")!.Abilities
            .Single(candidate => candidate.Sequence == 2);
        var scene = Assert.Single(ability.Presentations, candidate =>
            candidate.Flow == "nephthys-sacrifice-discount");

        Assert.Null(ability.CostText);
        Assert.Equal(1, scene.SegmentIndex);
        Assert.Equal(1, scene.SegmentCount);
    }

    [Fact]
    [Trait("L12Evidence", "ability:nephthysSacrifice")]
    public void CancellingSelectionDiscardsNothingAndUsesNoTurnCount()
    {
        var game = Create(91601);
        var (first, second, prompt) = Begin(game);
        Assert.Contains("skip", prompt.ValidChoices);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "skip")).Accepted);

        Assert.Contains(first, game.State.Players[0].Field[0]);
        Assert.Contains(second, game.State.Players[0].Field[1]);
        Assert.Empty(game.State.Players[0].Graveyard);
        Assert.DoesNotContain(game.State.Players[0].UsedAbilities,
            key => key.Contains("nephthysSacrifice", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:nephthysSacrifice")]
    public void DeclaredLegionsRemainOnFieldUntilTheEffectResolves()
    {
        var game = Create(91602);
        var (first, second, prompt) = Begin(game);
        Declare(game, prompt, first.InstanceId, second.InstanceId);

        Assert.Contains(first, game.State.Players[0].Field[0]);
        Assert.Contains(second, game.State.Players[0].Field[1]);
        Assert.Empty(game.State.Players[0].Graveyard);
        Assert.Single(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "ability:nephthysSacrifice")]
    public void NegationDoesNotDiscardLegionsOrGrantDiscount()
    {
        var game = Create(91603);
        var (first, second, prompt) = Begin(game);
        Declare(game, prompt, first.InstanceId, second.InstanceId);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.Contains(first, game.State.Players[0].Field[0]);
        Assert.Contains(second, game.State.Players[0].Field[1]);
        Assert.Empty(game.State.Players[0].Graveyard);
        Assert.Equal(0, game.State.Players[0].NextS2SunDisasterLegionDiscount);
        Assert.Equal("negated", Result(game).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:nephthysSacrifice")]
    public void ReverseSettlementUsesOnlyLegionsThatRemainLegalAndAreActuallyDiscarded()
    {
        var game = Create(91604);
        var (first, second, prompt) = Begin(game);
        Declare(game, prompt, first.InstanceId, second.InstanceId);
        game.State.Players[0].Field[1][1] = null;
        game.State.Players[0].Hand.Add(second);
        PassResponses(game);

        Assert.Contains(first, game.State.Players[0].Graveyard);
        Assert.Contains(second, game.State.Players[0].Hand);
        Assert.Equal(1, game.State.Players[0].NextS2SunDisasterLegionDiscount);
        Assert.Equal("resolved", Result(game).EffectResultStatus);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("1张已声明军团在逆结算后失效", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:nephthysSacrifice")]
    public void AllTargetsLeavingDuringResponseFailsWithoutDiscount()
    {
        var game = Create(91605);
        var (first, second, prompt) = Begin(game);
        Declare(game, prompt, first.InstanceId, second.InstanceId);
        game.State.Players[0].Field[0][0] = null;
        game.State.Players[0].Field[1][1] = null;
        game.State.Players[0].Hand.AddRange([first, second]);
        PassResponses(game);

        Assert.Equal(0, game.State.Players[0].NextS2SunDisasterLegionDiscount);
        Assert.Equal("failed", Result(game).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:nephthysSacrifice")]
    public void V2RestoreDiscardsEachFrozenLegionOnceAndRejectsTheOldPrompt()
    {
        var game = Create(91606);
        var (first, second, prompt) = Begin(game);
        Declare(game, prompt, first.InstanceId, second.InstanceId);

        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        PassResponses(game);

        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [first.InstanceId, second.InstanceId])).Accepted);
        Assert.Equal(2, game.State.Players[0].Graveyard.Count(card =>
            card.InstanceId is "nephthys-first" or "nephthys-second"));
        Assert.Equal(2, game.State.Players[0].NextS2SunDisasterLegionDiscount);
        Assert.Equal("resolved", Result(game).EffectResultStatus);
        Assert.Single(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("实际弃置2张军团", StringComparison.Ordinal));
    }
}
