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
        var masterSource = cardId is "S01-04M2" or "S01-04M1" or "S01-02D1";
        var deck = new L12PresetDeckDefinition { Name = "single-selection", MasterId = masterSource ? cardId : "S01-04M2",
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
        var source = Card(cardId, masterSource ? "master-0" : "source");
        if (!masterSource)
        {
            if (source.CardType == "legion") owner.Field[1][2] = source;
            else owner.Relic = source;
        }
        if (ability == "kusanagi")
        {
            owner.Relic = Card("S01-0417", "sword");
            for (var i = 0; i < 3; i++) owner.Field[0][i] = Card("S01-0401", $"block-{i}");
            if (target) owner.Field[0][1] = null;
        }
        else if (target)
        {
            var candidate = Card(ability == "ankhDraw" ? "S01-0212" : "S01-0401", "candidate");
            if (ability == "sunBottomEnemy") candidate.Troops = 3000;
            if (ability == "artifactSearch") owner.Hand.Add(candidate);
            else game.State.Players[ability is "kusanagiDebuff" or "olgaDebuff" or "sunBottomEnemy" or "amaterasuKill" ? 1 : 0].Field[0][0] = candidate;
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
    [InlineData("S01-02D1", "sunBottomEnemy")]
    [InlineData("S01-0215", "ankhDraw")]
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

    [Fact]
    public void OlgaPaysColonCostEvenWithoutEffectTarget()
    {
        var (game, source) = Create("S01-0314", "olgaDebuff", false);
        Assert.True(View(game, source, "olgaDebuff").Enabled);
        var result = game.Handle(0, new L12Command("activateAbility", source.InstanceId, Ability: "olgaDebuff"));
        Assert.True(result.Accepted, result.Error);
        Assert.Contains(game.State.Players[0].Graveyard, card => card.InstanceId == source.InstanceId);
        Assert.Null(game.State.Players[0].Field[1][2]);
        for (var i = 0; i < 20 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; i++)
        {
            var response = game.State.PendingPrompts[0];
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        }
        Assert.Empty(game.State.PendingPrompts); Assert.Empty(game.State.EffectStack);
        var resultEvent = Assert.Single(game.State.Events, action => action.Type == "effect-result"
            && action.Cards.Any(card => card.InstanceId == source.InstanceId));
        Assert.Equal("skipped", resultEvent.EffectResultStatus);
        Assert.Equal(1, resultEvent.EffectSegmentIndex);
        Assert.Equal(1, resultEvent.EffectSegmentCount);
        Assert.False(game.Handle(0, new L12Command("activateAbility", source.InstanceId, Ability: "olgaDebuff")).Accepted);
    }

    [Theory]
    [InlineData("S01-04M2", "frontBuff")]
    [InlineData("S01-04M2", "kusanagi")]
    [InlineData("S01-0117", "artifactSearch")]
    [InlineData("S01-0417", "kusanagiDebuff")]
    [InlineData("S01-0417", "kusanagiStrong")]
    [InlineData("S01-0314", "olgaDebuff")]
    [InlineData("S01-02D1", "sunBottomEnemy")]
    [InlineData("S01-0215", "ankhDraw")]
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
        Assert.Equal(ability is "artifactSearch" or "olgaDebuff" or "ankhDraw" ? 0
            : ability is "kusanagi" or "sunBottomEnemy" ? 2 : 1, spent);
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
        if (ability == "olgaDebuff")
        {
            Assert.Contains(game.State.Players[0].Graveyard, entry => entry.InstanceId == "source");
            var resultEvent = Assert.Single(game.State.Events, action => action.Type == "effect-result"
                && action.Cards.Any(card => card.InstanceId == source.InstanceId));
            Assert.Equal("negated", resultEvent.EffectResultStatus);
        }
        if (ability == "ankhDraw")
        {
            Assert.True(game.State.Players[0].Relic!.Tapped);
            Assert.True(game.State.Players[0].Field[0][0]!.Tapped);
        }
        if (ability == "kusanagi")
        {
            Assert.Null(game.State.Players[0].Field[0][1]);
            Assert.Equal("sword", game.State.Players[0].Relic?.InstanceId);
        }
    }

    [Fact]
    public void AmaterasuStillDeclaresSecondTargetBeforePayment()
    {
        var (game, source) = Create("S01-04M1", "amaterasuKill", true);
        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId, Ability: "amaterasuKill")).Accepted);
        var first = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: first.PromptId, Choice: "candidate")).Accepted);
        Assert.NotEmpty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.All(game.State.Players[0].Morale, morale => Assert.False(morale.Tapped));
    }
}
