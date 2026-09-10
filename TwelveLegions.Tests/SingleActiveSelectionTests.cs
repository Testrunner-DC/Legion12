using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SingleActiveSelectionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static (L12GameEngine Game, L12CardInstance Source) Create(string cardId, string ability, bool target)
    {
        var original = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition { Name = "single-selection", MasterId = "S01-04M2",
            CardIds = [.. original.CardIds], MoraleIds = [.. original.MoraleIds], SpecialIds = [] };
        var game = new L12GameEngine(Catalog, "single-selection", "SELECTION", 31010,
            ["甲", "乙"], [deck, original], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0; game.State.Phase = L12Phase.Main;
        game.State.Round = 3; game.State.TurnSerial = 4; game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear(); player.Field[0] = new L12CardInstance?[3]; player.Field[1] = new L12CardInstance?[3];
            player.Relic = null; player.ExtraRelics.Clear(); player.UsedAbilities.Clear();
        }
        var owner = game.State.Players[0];
        owner.Morale.Clear(); owner.TemporaryMorale = 0;
        for (var i = 0; i < 4; i++) owner.Morale.Add(new L12MoraleCard { CardId = "S01-04C1", InstanceId = $"resource-{i}" });
        L12CardInstance Card(string id, string instance) => (L12CardInstance)typeof(L12GameEngine)
            .GetMethod("CreateCard", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, [id, instance])!;
        var source = Card(cardId, cardId == "S01-04M2" ? "master-0" : "source");
        if (cardId != "S01-04M2") owner.Relic = source;
        if (ability == "kusanagi")
        {
            owner.Relic = Card("S01-0417", "sword");
            for (var i = 0; i < 3; i++) owner.Field[0][i] = Card("S01-0401", $"block-{i}");
            if (target) owner.Field[0][1] = null;
        }
        else if (target)
        {
            var candidate = Card("S01-0401", "candidate");
            if (ability == "artifactSearch") owner.Hand.Add(candidate);
            else game.State.Players[ability == "kusanagiDebuff" ? 1 : 0].Field[0][0] = candidate;
        }
        return (game, source);
    }

    private static L12AbilityView View(L12GameEngine game, L12CardInstance source, string ability)
        => ((List<L12AbilityView>)typeof(L12GameEngine).GetMethod("BuildAbilityViews",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game,
            [game.State.Players[0], source.CardId, source.InstanceId])!).Single(view => view.Id == ability);

    [Theory]
    [InlineData("S01-04M2", "frontBuff")]
    [InlineData("S01-04M2", "kusanagi")]
    [InlineData("S01-0117", "artifactSearch")]
    [InlineData("S01-0417", "kusanagiDebuff")]
    [InlineData("S01-0417", "kusanagiStrong")]
    public void NoCandidateDisablesButtonAndRejectsWithSameReasonWithoutPayment(string card, string ability)
    {
        var (game, source) = Create(card, ability, false);
        var view = View(game, source, ability);
        Assert.False(view.Enabled);
        var result = game.Handle(0, new L12Command("activateAbility", source.InstanceId, Ability: ability));
        Assert.False(result.Accepted); Assert.Equal(view.DisabledReason, result.Error);
        Assert.Empty(game.State.PendingPrompts); Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.All(game.State.Players[0].Morale, morale => Assert.False(morale.Tapped));
    }

    [Theory]
    [InlineData("S01-04M2", "frontBuff")]
    [InlineData("S01-04M2", "kusanagi")]
    [InlineData("S01-0117", "artifactSearch")]
    [InlineData("S01-0417", "kusanagiDebuff")]
    [InlineData("S01-0417", "kusanagiStrong")]
    public void RestoredChoiceRejectsDuplicateAndNegationPreservesPaidCosts(string card, string ability)
    {
        var (game, source) = Create(card, ability, true);
        Assert.True(View(game, source, ability).Enabled);
        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId, Ability: ability)).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Single(game.SnapshotFor(0).Prompts);
        Assert.All(game.State.Players[0].Morale, morale => Assert.False(morale.Tapped));
        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.Equal(prompt.ActivationId, Assert.Single(game.State.PendingPrompts).ActivationId);
        var choice = ability == "kusanagi" ? "0:1" : "candidate";
        Assert.Contains(choice, prompt.ValidChoices);
        var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice,
            ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
            SourceCardId: prompt.SourceCardId, Step: prompt.Step,
            CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller);
        var result = game.Handle(0, command);
        Assert.True(result.Accepted, result.Error);
        var spent = game.State.Players[0].Morale.Count(morale => morale.Tapped);
        Assert.Equal(ability == "artifactSearch" ? 0 : ability == "kusanagi" ? 2 : 1, spent);
        Assert.False(game.Handle(0, command).Accepted);
        Assert.Equal(spent, game.State.Players[0].Morale.Count(morale => morale.Tapped));
        Assert.NotEmpty(game.State.EffectStack);
        foreach (var item in game.State.EffectStack) item.Negated = true;
        for (var i = 0; i < 20 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; i++)
        {
            var response = game.State.PendingPrompts[0];
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        }
        Assert.Empty(game.State.EffectStack); Assert.Empty(game.State.PendingPrompts);
        Assert.Equal(spent, game.State.Players[0].Morale.Count(morale => morale.Tapped));
        if (ability == "artifactSearch")
            Assert.Contains(game.State.Players[0].Graveyard, entry => entry.InstanceId == "candidate");
        if (ability == "kusanagi")
        {
            Assert.Null(game.State.Players[0].Field[0][1]);
            Assert.Equal("sword", game.State.Players[0].Relic?.InstanceId);
        }
    }
}
