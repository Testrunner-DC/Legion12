using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MedeaRealEntryRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create()
    {
        var game = new L12GameEngine(Catalog, "medea-real-entry", "MEDEAENTRY", 100301,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner, int? disasterLevel = null)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            OwnerIndex = owner,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            IsCounterTactic = definition.IsCounterTactic,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = disasterLevel ?? definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static void AddReadyMorale(L12PlayerState player, int count)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "ST05-C1",
                InstanceId = $"medea-entry-morale-{index}",
            });
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

    private static void ResolveOnlyPrompt(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 20
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            ResolveOnlyPrompt(game, "pass");
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }

    private static void AddDrawCards(L12PlayerState player, string prefix)
        => player.Library.AddRange([
            Card("ST03-09", $"{prefix}-draw-one", player.PlayerIndex),
            Card("ST05-02", $"{prefix}-draw-two", player.PlayerIndex),
        ]);

    private static L12CardInstance SetPitfall(L12GameEngine game, string instanceId)
    {
        var pitfall = Card("S01-0018", instanceId, 1);
        pitfall.Hidden = true;
        pitfall.SetRound = 0;
        game.State.Players[1].Field[1][0] = pitfall;
        return pitfall;
    }

    [Fact]
    [Trait("L12Evidence", "card:ST05-04")]
    [Trait("L12Evidence", "entry:real-hand-play-current-field-troops")]
    [Trait("L12Evidence", "reconnect:v2-pending-entry-declaration")]
    public void RealMedeaEntryCountsHerOwnTwoThousandTroopsAndMayDrawWhenStillBehind()
    {
        var game = Create();
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var medea = Card("ST05-04", "real-entry-medea", 0);
        player.Hand.Add(medea);
        player.Library.AddRange([
            Card("ST03-09", "medea-real-draw-one", 0),
            Card("ST05-02", "medea-real-draw-two", 0),
        ]);
        opponent.Field[0][0] = Card("S02-0004", "medea-five-thousand-opponent", 1);
        AddReadyMorale(player, medea.Cost);

        var played = game.Handle(0,
            new L12Command("playCard", medea.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        Assert.Equal(2000, Assert.IsType<L12CardInstance>(player.Field[0][0]).Troops);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains("mode:use", declaration.ValidChoices);
        Assert.Empty(game.State.EffectStack);

        game = Restore(game);
        player = game.State.Players[0];
        Assert.Equal("real-entry-medea", Assert.IsType<L12CardInstance>(player.Field[0][0]).InstanceId);
        Assert.Contains("mode:use", Assert.Single(game.State.PendingPrompts).ValidChoices);

        ResolveOnlyPrompt(game, "mode:use");
        PassResponses(game);

        Assert.Equal(2, player.Hand.Count);
        Assert.Empty(player.Library);
        Assert.Empty(game.State.EffectStack);
    }

    [Theory]
    [InlineData("ST03-09", 2000)]
    [InlineData(null, 0)]
    [Trait("L12Evidence", "card:ST05-04")]
    [Trait("L12Evidence", "entry:strict-less-than-boundary")]
    public void MedeaDoesNotOfferDrawWhenHerCurrentSideIsEqualOrGreater(string? opponentCardId,
        int expectedOpponentTroops)
    {
        var game = Create();
        var player = game.State.Players[0];
        var medea = Card("ST05-04", $"medea-not-behind-{expectedOpponentTroops}", 0);
        player.Hand.Add(medea);
        AddDrawCards(player, "not-behind");
        AddReadyMorale(player, medea.Cost);
        if (opponentCardId is not null)
            game.State.Players[1].Field[0][0] = Card(opponentCardId, "equal-opponent", 1);

        var played = game.Handle(0,
            new L12Command("playCard", medea.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        Assert.Equal(expectedOpponentTroops,
            game.State.Players[1].Field.SelectMany(row => row).Where(card => card is not null)
                .Sum(card => card!.Troops));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal(2, player.Library.Count);
        Assert.DoesNotContain(player.Hand, card => card.InstanceId.StartsWith("not-behind-draw-",
            StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:ST05-04")]
    [Trait("L12Evidence", "entry:current-continuous-troops")]
    public void MedeaConditionUsesCurrentContinuousTroopsOnBothFields()
    {
        var game = Create();
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var medea = Card("ST05-04", "medea-continuous-current", 0);
        player.Hand.Add(medea);
        opponent.Field[0][0] = Card("S01-0203", "opponent-menes-current-six-thousand", 1);
        AddDrawCards(player, "continuous");
        AddReadyMorale(player, medea.Cost);

        var played = game.Handle(0,
            new L12Command("playCard", medea.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        Assert.Equal(6000, Assert.IsType<L12CardInstance>(opponent.Field[0][0]).Troops);
        Assert.Contains("mode:use", Assert.Single(game.State.PendingPrompts).ValidChoices);
        ResolveOnlyPrompt(game, "mode:use");
        PassResponses(game);
        Assert.Equal(2, player.Hand.Count);
        Assert.Empty(player.Library);
    }

    [Fact]
    [Trait("L12Evidence", "card:ST05-04")]
    [Trait("L12Evidence", "entry:optional-decline")]
    public void MedeaMayDeclineTheQualifiedDrawWithoutChangingTheLibrary()
    {
        var game = Create();
        var player = game.State.Players[0];
        var medea = Card("ST05-04", "medea-decline", 0);
        player.Hand.Add(medea);
        game.State.Players[1].Field[0][0] = Card("S02-0004", "medea-decline-opponent", 1);
        AddDrawCards(player, "decline");
        AddReadyMorale(player, medea.Cost);

        var played = game.Handle(0,
            new L12Command("playCard", medea.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        ResolveOnlyPrompt(game, "mode:none");

        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal(2, player.Library.Count);
        Assert.Empty(player.Hand);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:ST05-04")]
    [Trait("L12Evidence", "card:S01-0018")]
    [Trait("L12Evidence", "entry:qualified-draw-negated")]
    public void PitfallCanNegateMedeasQualifiedEntryDraw()
    {
        var game = Create();
        var player = game.State.Players[0];
        var medea = Card("ST05-04", "medea-negated", 0);
        var pitfall = SetPitfall(game, "medea-entry-pitfall");
        player.Hand.Add(medea);
        game.State.Players[1].Field[0][0] = Card("S02-0004", "medea-negated-opponent", 1);
        AddDrawCards(player, "negated");
        AddReadyMorale(player, medea.Cost);

        var played = game.Handle(0,
            new L12Command("playCard", medea.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        ResolveOnlyPrompt(game, "mode:use");
        ResolveOnlyPrompt(game, "pass");
        Assert.Contains(pitfall.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);
        ResolveOnlyPrompt(game, pitfall.InstanceId);
        PassResponses(game);

        Assert.Equal(2, player.Library.Count);
        Assert.Empty(player.Hand);
        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == pitfall.InstanceId);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:ST05-04")]
    [Trait("L12Evidence", "entry:qualified-empty-library")]
    public void QualifiedMedeaWithAnEmptyLibraryResolvesWithoutLeavingPendingState()
    {
        var game = Create();
        var player = game.State.Players[0];
        var medea = Card("ST05-04", "medea-empty-library", 0);
        player.Hand.Add(medea);
        game.State.Players[1].Field[0][0] = Card("S02-0004", "medea-empty-library-opponent", 1);
        AddReadyMorale(player, medea.Cost);

        var played = game.Handle(0,
            new L12Command("playCard", medea.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        ResolveOnlyPrompt(game, "mode:use");
        PassResponses(game);

        Assert.Empty(player.Library);
        Assert.Empty(player.Hand);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:ST05-04")]
    [Trait("L12Evidence", "entry:effect-generated-current-field-troops")]
    [Trait("L12Evidence", "reconnect:v2-effect-entry-declaration")]
    public void EffectEntryMedeaUsesTheSameQualifiedDrawAndRestoresItsDeclaration()
    {
        var game = Create();
        var player = game.State.Players[0];
        var alvida = Card("S01-0307", "medea-effect-entry-alvida", 0);
        var medea = Card("ST05-04", "medea-effect-entry", 0, disasterLevel: 2);
        player.Field[0][0] = alvida;
        player.Hand.Add(medea);
        game.State.Players[1].Field[0][0] = Card("S02-0004", "medea-effect-opponent", 1);
        AddDrawCards(player, "effect-entry");

        var activated = game.Handle(0,
            new L12Command("activateAbility", alvida.InstanceId, Ability: "alvidaSummon"));
        Assert.True(activated.Accepted, activated.Error);
        ResolveOnlyPrompt(game, medea.InstanceId);
        ResolveOnlyPrompt(game, "0:1");
        PassResponses(game);
        Assert.Contains("mode:use", Assert.Single(game.State.PendingPrompts).ValidChoices);

        game = Restore(game);
        player = game.State.Players[0];
        Assert.Equal(medea.InstanceId,
            Assert.IsType<L12CardInstance>(player.Field[0][1]).InstanceId);
        Assert.Contains("mode:use", Assert.Single(game.State.PendingPrompts).ValidChoices);
        ResolveOnlyPrompt(game, "mode:use");
        PassResponses(game);

        Assert.Equal(2, player.Hand.Count);
        Assert.Empty(player.Library);
        Assert.Contains(player.Graveyard, card => card.InstanceId == alvida.InstanceId);
        Assert.Empty(game.State.EffectStack);
    }
}
