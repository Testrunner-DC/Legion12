using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TrialAdvancePaidSelfStateLifecycleTests
{
    private const string LancelotAbilityId = "S02-0602:ability:enter:1ec4fb001f87c88e";
    private const string FinnAbilityId = "S02-0610:ability:after-trial:451b6d549a5c98c4";
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var deck = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "paid-self-state", "PAID-SELF", seed,
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

    private static void QueueTrigger(L12GameEngine game, L12CardInstance source)
    {
        var method = typeof(L12GameEngine).GetMethod("QueueOrPushTriggeredEffect",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(game, [0, source, "enter", "生命周期测试触发", null, null]);
    }

    private static (L12GameEngine Game, L12CardInstance Source, string AbilityId) BeginLancelot(int seed,
        int runes = 1)
    {
        var game = Create(seed);
        var source = Card("S02-0602", $"lancelot-{seed}");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].SpecialZones.Runes = runes;
        QueueTrigger(game, source);
        return (game, source, LancelotAbilityId);
    }

    private static (L12GameEngine Game, L12CardInstance Source, string AbilityId) BeginFinn(int seed,
        int runes = 1)
    {
        var game = Create(seed);
        var source = Card("S02-0610", $"finn-{seed}");
        var trial = Card("S02-06S4", $"trial-{seed}");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].SpecialZones.Trials.Add(trial);
        game.State.Players[0].SpecialZones.Runes = runes;
        var result = game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "trialAdvance"));
        Assert.True(result.Accepted, result.Error);
        PassResponses(game);
        return (game, source, FinnAbilityId);
    }

    private static L12ActionEvent Result(L12GameEngine game, string abilityId, string status)
        => Assert.Single(game.State.Events, entry => entry.EffectAbilityId == abilityId
            && entry.EffectResultStatus == status);

    [Fact]
    [L12AbilityEvidence(LancelotAbilityId, "normal", "presentation-consumers")]
    [L12AbilityEvidence(FinnAbilityId, "normal", "presentation-consumers")]
    public void PaidSelfStateEffectsResolveAndExposeTheirExactAbilityResult()
    {
        var lancelotCase = BeginLancelot(9101);
        Assert.Contains("mode:use", Assert.Single(lancelotCase.Game.State.PendingPrompts).ValidChoices);
        Resolve(lancelotCase.Game, "mode:use");
        Assert.Equal(0, lancelotCase.Game.State.Players[0].SpecialZones.Runes);
        PassResponses(lancelotCase.Game);
        Assert.True(lancelotCase.Source.HasCharge);
        Assert.False(string.IsNullOrWhiteSpace(Result(lancelotCase.Game,
            lancelotCase.AbilityId, "resolved").EffectSceneId));

        var finnCase = BeginFinn(9102);
        Assert.Contains("mode:use", Assert.Single(finnCase.Game.State.PendingPrompts).ValidChoices);
        Resolve(finnCase.Game, "mode:use");
        Assert.Equal(0, finnCase.Game.State.Players[0].SpecialZones.Runes);
        PassResponses(finnCase.Game);
        Assert.False(finnCase.Source.Tapped);
        Assert.False(string.IsNullOrWhiteSpace(Result(finnCase.Game,
            finnCase.AbilityId, "resolved").EffectSceneId));
    }

    [Fact]
    [L12AbilityEvidence(LancelotAbilityId, "no-target")]
    [L12AbilityEvidence(FinnAbilityId, "no-target")]
    public void MissingRunePreventsEitherOptionalEffectFromEnteringTheStack()
    {
        var lancelotCase = BeginLancelot(9103, runes: 0);
        Assert.Empty(lancelotCase.Game.State.PendingPrompts);
        Assert.Empty(lancelotCase.Game.State.EffectStack);
        Assert.False(lancelotCase.Source.HasCharge);

        var finnCase = BeginFinn(9104, runes: 0);
        Assert.Empty(finnCase.Game.State.PendingPrompts);
        Assert.Empty(finnCase.Game.State.EffectStack);
        Assert.True(finnCase.Source.Tapped);
    }

    [Fact]
    [L12AbilityEvidence(LancelotAbilityId, "negated")]
    [L12AbilityEvidence(FinnAbilityId, "negated")]
    public void NegationDoesNotRefundThePrepaidRuneOrApplyTheStateChange()
    {
        var lancelotCase = BeginLancelot(9105);
        Resolve(lancelotCase.Game, "mode:use");
        Assert.Single(lancelotCase.Game.State.EffectStack).Negated = true;
        PassResponses(lancelotCase.Game);
        Assert.Equal(0, lancelotCase.Game.State.Players[0].SpecialZones.Runes);
        Assert.False(lancelotCase.Source.HasCharge);
        _ = Result(lancelotCase.Game, lancelotCase.AbilityId, "negated");

        var finnCase = BeginFinn(9106);
        Resolve(finnCase.Game, "mode:use");
        Assert.Single(finnCase.Game.State.EffectStack).Negated = true;
        PassResponses(finnCase.Game);
        Assert.Equal(0, finnCase.Game.State.Players[0].SpecialZones.Runes);
        Assert.True(finnCase.Source.Tapped);
        _ = Result(finnCase.Game, finnCase.AbilityId, "negated");
    }

    [Fact]
    [L12AbilityEvidence(LancelotAbilityId, "target-invalidated")]
    [L12AbilityEvidence(FinnAbilityId, "target-invalidated")]
    public void SourceLeavingAfterPaymentIsFailedSettlementRatherThanCancellation()
    {
        var lancelotCase = BeginLancelot(9107);
        Resolve(lancelotCase.Game, "mode:use");
        lancelotCase.Game.State.Players[0].Field[0][0] = null;
        lancelotCase.Game.State.Players[0].Graveyard.Add(lancelotCase.Source);
        PassResponses(lancelotCase.Game);
        Assert.Equal(0, lancelotCase.Game.State.Players[0].SpecialZones.Runes);
        _ = Result(lancelotCase.Game, lancelotCase.AbilityId, "failed");
        Assert.DoesNotContain(lancelotCase.Game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Cards.Any(card => card.InstanceId == lancelotCase.Source.InstanceId));

        var finnCase = BeginFinn(9108);
        Resolve(finnCase.Game, "mode:use");
        finnCase.Game.State.Players[0].Field[0][0] = null;
        finnCase.Game.State.Players[0].Graveyard.Add(finnCase.Source);
        PassResponses(finnCase.Game);
        Assert.Equal(0, finnCase.Game.State.Players[0].SpecialZones.Runes);
        _ = Result(finnCase.Game, finnCase.AbilityId, "failed");
        Assert.DoesNotContain(finnCase.Game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Cards.Any(card => card.InstanceId == finnCase.Source.InstanceId));
    }

    [Fact]
    [L12AbilityEvidence(LancelotAbilityId, "payment-cancel")]
    [L12AbilityEvidence(FinnAbilityId, "payment-cancel")]
    public void DecliningBeforePaymentConsumesNeitherRuneNorStackSlot()
    {
        var lancelotCase = BeginLancelot(9109);
        Resolve(lancelotCase.Game, "mode:none");
        Assert.Equal(1, lancelotCase.Game.State.Players[0].SpecialZones.Runes);
        Assert.Empty(lancelotCase.Game.State.EffectStack);

        var finnCase = BeginFinn(9110);
        Resolve(finnCase.Game, "mode:none");
        Assert.Equal(1, finnCase.Game.State.Players[0].SpecialZones.Runes);
        Assert.Empty(finnCase.Game.State.EffectStack);
    }

    [Fact]
    [L12AbilityEvidence(LancelotAbilityId, "duplicate-submit", "reconnect")]
    [L12AbilityEvidence(FinnAbilityId, "duplicate-submit", "reconnect")]
    public void DeclarationPromptSurvivesReconnectAndRejectsItsDuplicateSubmission()
    {
        foreach (var original in new[] { BeginLancelot(9111), BeginFinn(9112) })
        {
            var promptId = Assert.Single(original.Game.State.PendingPrompts).PromptId;
            var checkpoint = original.Game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
            var game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
                original.Game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
                original.Game.CardFactSignalSequence, autoPassEmptyResponses: false,
                concealHiddenResponseAvailability: false);
            Assert.Equal(promptId, Assert.Single(game.State.PendingPrompts).PromptId);

            Resolve(game, "mode:use");
            var duplicate = game.Handle(0, new L12Command("resolvePrompt", PromptId: promptId,
                Choice: "mode:use"));
            Assert.False(duplicate.Accepted);
            Assert.Equal(0, game.State.Players[0].SpecialZones.Runes);
            PassResponses(game);
            _ = Result(game, original.AbilityId, "resolved");
        }
    }
}
