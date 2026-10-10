using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TrialAdvanceModeBranchLifecycleTests
{
    private const string LancelotTrialId = "S02-0602:ability:granted:7a7545729484412a";
    private const string LancelotRuneId = "S02-0602:ability:granted:6235a3f3a12afdbb";
    private const string ConstanceRuneId = "S02-0614:ability:granted:6235a3f3a12afdbb";
    private const string ConstanceTrialId = "S02-0614:ability:granted:45f31f84b8f800cd";
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var deck = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "trial-mode-branch", "TRIAL-MODE", seed,
            ["甲", "乙"], [deck, deck], skipPreparation: true,
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

    private static void AddOpenTrial(L12GameEngine game, int seed)
        => game.State.Players[0].SpecialZones.Trials.Add(Card("S02-06S4", $"mode-trial-{seed}"));

    private static void QueueTrigger(L12GameEngine game, L12CardInstance source)
    {
        var method = typeof(L12GameEngine).GetMethod("QueueOrPushTriggeredEffect",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var trigger = source.CardId == "S02-0602" ? "after-attack" : "enter";
        Dictionary<string, string>? data = source.CardId == "S02-0602"
            ? new() { ["killed"] = "true", ["combatKillConfirmed"] = "true" }
            : null;
        method.Invoke(game, [0, source, trigger, "分支生命周期测试", null, data]);
    }

    private static (L12GameEngine Game, L12CardInstance Source) Begin(string cardId, int seed,
        bool withTrial = true)
    {
        var game = Create(seed);
        var source = Card(cardId, $"mode-source-{seed}");
        game.State.Players[0].Field[0][0] = source;
        if (withTrial) AddOpenTrial(game, seed);
        QueueTrigger(game, source);
        return (game, source);
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
    }

    private static L12ActionEvent Result(L12GameEngine game, string abilityId, string status)
        => Assert.Single(game.State.Events, entry => entry.EffectAbilityId == abilityId
            && entry.EffectResultStatus == status);

    [Fact]
    [L12AbilityEvidence(LancelotTrialId, "normal", "presentation-consumers")]
    [L12AbilityEvidence(LancelotRuneId, "normal", "presentation-consumers")]
    [L12AbilityEvidence(ConstanceRuneId, "normal", "presentation-consumers")]
    [L12AbilityEvidence(ConstanceTrialId, "normal", "presentation-consumers")]
    public void EachDeclaredModePublishesAndSettlesItsOwnGrantedAbility()
    {
        var lancelotTrial = Begin("S02-0602", 9131);
        Resolve(lancelotTrial.Game, "mode:trial");
        PassResponses(lancelotTrial.Game);
        Assert.Equal(1, lancelotTrial.Game.State.Players[0].SpecialZones.Trials[0].TrialProgress);
        Assert.False(string.IsNullOrWhiteSpace(Result(lancelotTrial.Game, LancelotTrialId,
            "resolved").EffectSceneId));

        var lancelotRune = Begin("S02-0602", 9132);
        Resolve(lancelotRune.Game, "mode:rune");
        PassResponses(lancelotRune.Game);
        Assert.Equal(1, lancelotRune.Game.State.Players[0].SpecialZones.Runes);
        Assert.False(string.IsNullOrWhiteSpace(Result(lancelotRune.Game, LancelotRuneId,
            "resolved").EffectSceneId));

        var constanceRune = Begin("S02-0614", 9133);
        Resolve(constanceRune.Game, "mode:rune");
        PassResponses(constanceRune.Game);
        Assert.Equal(1, constanceRune.Game.State.Players[0].SpecialZones.Runes);
        Assert.False(constanceRune.Source.Tapped);
        Assert.False(string.IsNullOrWhiteSpace(Result(constanceRune.Game, ConstanceRuneId,
            "resolved").EffectSceneId));

        var constanceTrial = Begin("S02-0614", 9134);
        Resolve(constanceTrial.Game, "mode:trial");
        Assert.True(constanceTrial.Source.Tapped);
        PassResponses(constanceTrial.Game);
        Assert.Equal(1, constanceTrial.Game.State.Players[0].SpecialZones.Trials[0].TrialProgress);
        Assert.False(string.IsNullOrWhiteSpace(Result(constanceTrial.Game, ConstanceTrialId,
            "resolved").EffectSceneId));
    }

    [Fact]
    [L12AbilityEvidence(LancelotTrialId, "no-target")]
    [L12AbilityEvidence(ConstanceTrialId, "no-target")]
    public void TrialModeIsNotOfferedWhenThereIsNoOpenTrial()
    {
        var lancelot = Begin("S02-0602", 9135, withTrial: false);
        var lancelotPrompt = Assert.Single(lancelot.Game.State.PendingPrompts);
        Assert.DoesNotContain("mode:trial", lancelotPrompt.ValidChoices);
        Assert.Contains("mode:rune", lancelotPrompt.ValidChoices);

        var constance = Begin("S02-0614", 9136, withTrial: false);
        var constancePrompt = Assert.Single(constance.Game.State.PendingPrompts);
        Assert.DoesNotContain("mode:trial", constancePrompt.ValidChoices);
        Assert.Contains("mode:rune", constancePrompt.ValidChoices);
    }

    [Fact]
    [L12AbilityEvidence(LancelotTrialId, "negated")]
    [L12AbilityEvidence(ConstanceTrialId, "negated")]
    public void NegatingTrialModeStopsProgressAndDoesNotUndoItsRuleActionCost()
    {
        var lancelot = Begin("S02-0602", 9137);
        Resolve(lancelot.Game, "mode:trial");
        Assert.Single(lancelot.Game.State.EffectStack).Negated = true;
        PassResponses(lancelot.Game);
        Assert.Equal(0, lancelot.Game.State.Players[0].SpecialZones.Trials[0].TrialProgress);
        _ = Result(lancelot.Game, LancelotTrialId, "negated");

        var constance = Begin("S02-0614", 9138);
        Resolve(constance.Game, "mode:trial");
        Assert.True(constance.Source.Tapped);
        Assert.Single(constance.Game.State.EffectStack).Negated = true;
        PassResponses(constance.Game);
        Assert.True(constance.Source.Tapped);
        Assert.Equal(0, constance.Game.State.Players[0].SpecialZones.Trials[0].TrialProgress);
        _ = Result(constance.Game, ConstanceTrialId, "negated");
    }

    [Fact]
    [L12AbilityEvidence(LancelotTrialId, "duplicate-submit", "reconnect")]
    [L12AbilityEvidence(LancelotRuneId, "reconnect")]
    [L12AbilityEvidence(ConstanceRuneId, "reconnect")]
    [L12AbilityEvidence(ConstanceTrialId, "duplicate-submit", "reconnect")]
    public void ModeDeclarationSurvivesReconnectAndRejectsTheExpiredPrompt()
    {
        var cases = new[]
        {
            (CardId: "S02-0602", Mode: "mode:trial", AbilityId: LancelotTrialId, Seed: 9139),
            (CardId: "S02-0602", Mode: "mode:rune", AbilityId: LancelotRuneId, Seed: 9140),
            (CardId: "S02-0614", Mode: "mode:rune", AbilityId: ConstanceRuneId, Seed: 9141),
            (CardId: "S02-0614", Mode: "mode:trial", AbilityId: ConstanceTrialId, Seed: 9142),
        };
        foreach (var testCase in cases)
        {
            var original = Begin(testCase.CardId, testCase.Seed);
            var promptId = Assert.Single(original.Game.State.PendingPrompts).PromptId;
            var checkpoint = original.Game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
            var game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
                original.Game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
                original.Game.CardFactSignalSequence, autoPassEmptyResponses: false,
                concealHiddenResponseAvailability: false);
            Assert.Equal(promptId, Assert.Single(game.State.PendingPrompts).PromptId);

            Resolve(game, testCase.Mode);
            var duplicate = game.Handle(0, new L12Command("resolvePrompt", PromptId: promptId,
                Choice: testCase.Mode));
            Assert.False(duplicate.Accepted);
            PassResponses(game);
            _ = Result(game, testCase.AbilityId, "resolved");
        }
    }
}
