using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PublicTriggerPromptPresentationRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "public-trigger-prompt-presentation", "PROMPT-PUBLIC", seed,
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
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.UsedAbilities.Clear();
            player.SpecialZones.Trials.Clear();
            player.SpecialZones.Runes = 0;
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
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
            OwnerIndex = 0,
        };
    }

    private static L12Prompt QueueEntryPrompt(L12GameEngine game, string cardId)
    {
        var source = Card(cardId, $"public-prompt-{cardId}");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].SpecialZones.Trials.Add(Card("S02-06S4", $"public-prompt-trial-{cardId}"));
        game.State.Players[0].SpecialZones.Runes = 1;

        var queue = typeof(L12GameEngine).GetMethod("QueueOrPushTriggeredEffect",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(queue);
        queue.Invoke(game, [0, source, "enter", "【登场时】效果", null, null]);
        return Assert.Single(game.State.PendingPrompts);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0604")]
    [Trait("L12Evidence", "prompt:galahad-entry-public-metadata")]
    public void GalahadEntryPromptPublishesCardNamePrintedEffectAndPlainDecisionLabels()
    {
        var prompt = QueueEntryPrompt(Create(90601), "S02-0604");

        Assert.Equal("pending-activation", prompt.Continuation);
        Assert.Equal("effect-decision", prompt.Data["uiPattern"]);
        Assert.Equal("加拉哈德", prompt.Data["sourceName"]);
        Assert.Equal("登场时 可发动试炼。", prompt.Data["effectText"]);
        Assert.Equal("发动", prompt.ChoiceLabels["mode:trial"]);
        Assert.Equal("不发动", prompt.ChoiceLabels["mode:none"]);
    }

    [Fact]
    [Trait("L12Evidence", "prompt:binary-public-trigger-shared-labels")]
    public void AnotherBinaryPublicTriggerUsesTheSameDecisionLabels()
    {
        var prompt = QueueEntryPrompt(Create(90602), "S02-0610");

        Assert.Equal("芬恩", prompt.Data["sourceName"]);
        Assert.Equal("登场时 可发动试炼。", prompt.Data["effectText"]);
        Assert.Equal("发动", prompt.ChoiceLabels["mode:trial"]);
        Assert.Equal("不发动", prompt.ChoiceLabels["mode:none"]);
    }

    [Fact]
    [Trait("L12Evidence", "prompt:multi-mode-public-trigger-keeps-semantic-labels")]
    public void MultiModePublicTriggerKeepsEachModesDistinctMeaning()
    {
        var prompt = QueueEntryPrompt(Create(90603), "S02-0614");

        Assert.Equal(["mode:none", "mode:rune", "mode:trial"], prompt.ValidChoices);
        Assert.Equal("不发动", prompt.ChoiceLabels["mode:none"]);
        Assert.Equal("获得1枚符文", prompt.ChoiceLabels["mode:rune"]);
        Assert.Equal("试炼进度+1", prompt.ChoiceLabels["mode:trial"]);
    }

    [Fact]
    [Trait("L12Evidence", "prompt:binary-mode-selection-is-not-always-an-effect-decision")]
    public void OrdinaryBinaryModeSelectionKeepsItsExplicitOutcomeLabels()
    {
        var game = Create(90604);
        var galahad = Card("S02-0604", "public-prompt-active-galahad");
        var completedGrail = Card("S02-06S4", "public-prompt-completed-grail");
        completedGrail.TrialCompleted = true;
        game.State.Players[0].Field[0][0] = galahad;
        game.State.Players[0].SpecialZones.Trials.Add(completedGrail);

        var result = game.Handle(0, new L12Command("activateAbility", galahad.InstanceId,
            Ability: "galahadGrailReward"));

        Assert.True(result.Accepted, result.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.False(prompt.Data.ContainsKey("uiPattern"));
        Assert.Equal("抽取1张牌", prompt.ChoiceLabels["mode:none"]);
        Assert.Equal("抽取1张牌；我方主宰增加1点血量", prompt.ChoiceLabels["mode:heal"]);
    }
}
