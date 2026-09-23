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
    [L12AbilityEvidence("S01-01C1:ability:active:3a8789b35c0c2be4", "no-target", "negated", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("ST01-C1:ability:static:6907bfcf5dbbfeb4", "no-target", "negated", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-02C1:ability:static:91802cda49d575fb", "no-target", "negated", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-02C1:ability:static:ddab147dd97c360f", "no-target", "negated", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("ST02-C1:ability:static:f2b97501194b5c40", "no-target", "negated", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("ST02-C1:ability:static:29d1864e955f856e", "no-target", "negated", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-03C1:ability:static:fa92f5d792a32bdc", "no-target", "negated", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("ST03-C1:ability:static:36b1c5751cc508f9", "no-target", "negated", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-04C1:ability:static:7f60c31c00b0f718", "no-target", "negated", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("ST04-C1:ability:static:d9cac21fb706e3c8", "no-target", "negated", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-05C1:ability:active:5dec5c18aaf62a03", "no-target", "negated", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-05C1A:ability:active:5dec5c18aaf62a03", "no-target", "negated", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-06C1:ability:static:7339369656140c39", "no-target", "negated", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("ST06-C1:ability:static:88a76dc195d499ee", "no-target", "negated", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-01C1:ability:active:3a8789b35c0c2be4", "payment-cancel")]
    [L12AbilityEvidence("ST01-C1:ability:static:6907bfcf5dbbfeb4", "payment-cancel")]
    [L12AbilityEvidence("S01-02C1:ability:static:91802cda49d575fb", "payment-cancel")]
    [L12AbilityEvidence("S01-02C1:ability:static:ddab147dd97c360f", "payment-cancel")]
    [L12AbilityEvidence("ST02-C1:ability:static:f2b97501194b5c40", "payment-cancel")]
    [L12AbilityEvidence("ST02-C1:ability:static:29d1864e955f856e", "payment-cancel")]
    [L12AbilityEvidence("S01-03C1:ability:static:fa92f5d792a32bdc", "payment-cancel")]
    [L12AbilityEvidence("ST03-C1:ability:static:36b1c5751cc508f9", "payment-cancel")]
    [L12AbilityEvidence("S01-04C1:ability:static:7f60c31c00b0f718", "payment-cancel")]
    [L12AbilityEvidence("ST04-C1:ability:static:d9cac21fb706e3c8", "payment-cancel")]
    [L12AbilityEvidence("S02-06C1:ability:static:7339369656140c39", "payment-cancel")]
    [L12AbilityEvidence("ST06-C1:ability:static:88a76dc195d499ee", "payment-cancel")]
    public void MoraleActiveEffectFamilySharesCancellationResumeAndPaidNegationProtocol()
    {
        var bindings = EffectLifecycleProfiles.Read(Catalog);
        foreach (var abilityId in EffectLifecycleProfiles.MoraleActiveEffectAbilityIds)
        {
            var profile = bindings[abilityId];
            Assert.Equal("morale:active-effect-pipeline", profile.Id);
            Assert.Equal("CommitActiveAbilityCore", profile.RuntimeOwners["commit"]);
            Assert.Equal("ResolveActiveEffect", profile.RuntimeOwners["settlement-dispatch"]);
            Assert.Equal("SnapshotFor", profile.RuntimeOwners["presentation"]);
        }

        var unavailable = Create(91309);
        PrepareMain(unavailable);
        unavailable.State.Players[0].Morale.Clear();
        unavailable.State.Players[0].TemporaryMorale = 0;
        var unavailableResult = unavailable.Handle(0,
            new L12Command("activateAbility", "faction-0", Ability: "factionAddActive"));
        Assert.False(unavailableResult.Accepted);
        Assert.Empty(unavailable.State.EffectStack);
        Assert.Empty(unavailable.State.PendingPrompts);

        var game = Create(91310);
        PrepareMain(game);
        var player = game.State.Players[0];
        player.Morale.Clear();
        player.MoraleDeck.Clear();
        AddReadyMorale(player, 2);
        player.TemporaryMorale = 2;
        player.MoraleDeck.Add(new L12MoraleCard
        {
            InstanceId = "shared-morale-result",
            CardId = "S01-01C1",
        });
        HoldOpponentResponseWindow(game);

        var activation = game.Handle(0,
            new L12Command("activateAbility", "faction-0", Ability: "factionAddActive"));
        Assert.True(activation.Accepted, activation.Error);
        var originalPayment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-payment", originalPayment.Kind);
        Assert.Equal("active-morale-choice", originalPayment.Continuation);
        Assert.Contains("cancel", originalPayment.ValidChoices);
        var ordinaryIds = player.Morale.Select(card => card.InstanceId).ToArray();

        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var restoredPayment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(originalPayment.PromptId, restoredPayment.PromptId);

        var cancelled = game.Handle(0,
            new L12Command("resolvePrompt", PromptId: restoredPayment.PromptId, Choice: "cancel"));
        Assert.True(cancelled.Accepted, cancelled.Error);
        player = game.State.Players[0];
        Assert.All(player.Morale, morale => Assert.False(morale.Tapped));
        Assert.Equal(2, player.TemporaryMorale);
        Assert.Empty(player.UsedAbilities);
        Assert.Empty(game.State.EffectStack);
        Assert.False(game.Handle(0,
            new L12Command("resolvePrompt", PromptId: restoredPayment.PromptId, Choice: "cancel")).Accepted);

        Assert.True(game.Handle(0,
            new L12Command("activateAbility", "faction-0", Ability: "factionAddActive")).Accepted);
        var payment = Assert.Single(game.State.PendingPrompts);
        var paid = game.Handle(0,
            new L12Command("resolvePrompt", PromptId: payment.PromptId, CardInstanceIds: ordinaryIds.ToList()));
        Assert.True(paid.Accepted, paid.Error);
        var response = Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        var item = Assert.Single(game.State.EffectStack);
        Assert.False(string.IsNullOrWhiteSpace(item.Data.GetValueOrDefault("presentationSceneId")));
        Assert.False(string.IsNullOrWhiteSpace(item.Data.GetValueOrDefault("paidCostSummary")));
        item.Negated = true;

        checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.Equal(response.PromptId,
            Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response").PromptId);
        PassResponses(game);

        player = game.State.Players[0];
        Assert.Equal(2, player.Morale.Count(morale => morale.Tapped));
        Assert.Equal(2, player.TemporaryMorale);
        Assert.Contains(player.UsedAbilities,
            key => key.Contains("factionAddActive", StringComparison.Ordinal));
        Assert.Equal("negated", Result(game, "S01-01C1").EffectResultStatus);
        Assert.False(game.Handle(response.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
    }

    [Fact]
    [Trait("L12Evidence", "ability:factionAddActive")]
    [L12AbilityEvidence("S01-01C1:ability:active:3a8789b35c0c2be4", "presentation-consumers")]
    [L12AbilityEvidence("ST01-C1:ability:static:6907bfcf5dbbfeb4", "presentation-consumers")]
    public void TiantingActiveMoraleUsesTheSingleActiveSettlementScene()
    {
        var game = Create(91300);
        var player = game.State.Players[0];
        PrepareMain(game);
        player.Morale.Clear();
        player.MoraleDeck.Clear();
        AddReadyMorale(player, 2);
        player.MoraleDeck.Add(new L12MoraleCard
        {
            InstanceId = "single-state-tianting-added",
            CardId = "S01-01C1",
        });
        HoldOpponentResponseWindow(game);

        var activation = game.Handle(0,
            new L12Command("activateAbility", "faction-0", Ability: "factionAddActive"));
        Assert.True(activation.Accepted, activation.Error);
        var declaration = Assert.Single(game.State.Events, entry => entry.Type == "effect-activation"
            && entry.Cards.Any(card => card.CardId == "S01-01C1"));
        Assert.NotNull(declaration.EffectSceneId);
        PassResponses(game);

        Assert.Equal(3, player.Morale.Count);
        Assert.Equal(2, player.Morale.Count(morale => morale.Tapped));
        Assert.Equal("resolved", Result(game, "S01-01C1").EffectResultStatus);
        Assert.Equal(declaration.EffectSceneId, Result(game, "S01-01C1").EffectSceneId);
    }

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
