using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ThorGrantedEntryResponseRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var basis = Catalog.DeckAt(0);
        var thorDeck = new L12PresetDeckDefinition
        {
            Name = "索尔赋予登场响应回归",
            MasterId = "S02-03M1",
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "thor-granted-entry-response", "THORENTRY", seed,
            ["甲", "乙"], [thorDeck, basis], skipPreparation: true,
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
        game.State.Players[0].Hp = 3;
        for (var index = 0; index < 10; index++)
            game.State.Players[0].Morale.Add(new L12MoraleCard
            {
                CardId = "S01-03C1",
                InstanceId = $"thor-entry-morale-{seed}-{index}",
            });
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

    private static void ResolveOnlyPrompt(L12GameEngine game, params string[] orderedChoices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: [.. orderedChoices]));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 30
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            ResolveOnlyPrompt(game, "pass");
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }

    private static void ResolveThorCharge(L12GameEngine game)
    {
        var result = game.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "thorCharge"));
        Assert.True(result.Accepted, result.Error);
        PassResponses(game);
        Assert.Contains($"s2-thor-charge:{game.State.TurnSerial}",
            game.State.Players[0].UsedAbilities);
    }

    private static L12CardInstance SetCounter(L12GameEngine game, string cardId, string instanceId)
    {
        var counter = Card(cardId, instanceId, 1);
        counter.Hidden = true;
        counter.SetRound = 0;
        game.State.Players[1].Field[1][0] = counter;
        return counter;
    }

    private static void AdvanceToThorGrantedEntryResponse(L12GameEngine game)
    {
        for (var safety = 0; safety < 30; safety++)
        {
            if (game.State.EffectStack.LastOrDefault()?.Data.GetValueOrDefault("thorGrantedEntryCharge") == "true"
                && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response") return;
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", prompt.Kind);
            ResolveOnlyPrompt(game, "pass");
        }
        Assert.Fail("雷神索尔赋予的登场冲锋未进入独立响应窗口");
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0101")]
    [Trait("L12Evidence", "entry:ordinary-printed-entry-keeps-public-declaration")]
    public void OrdinaryPrintedEntryWithoutThorKeepsItsPreStackDeclarationRoute()
    {
        var game = Create(100309);
        var player = game.State.Players[0];
        var yingzheng = Card("S02-0101", "ordinary-entry-yingzheng", 0);
        var returnCost = Card("S02-0101", "ordinary-entry-yingzheng-cost", 0);
        player.Hand.AddRange([yingzheng, returnCost]);

        var played = game.Handle(0,
            new L12Command("playCard", yingzheng.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-yingzheng-enter-cost", costPrompt.Continuation);
        Assert.Contains(returnCost.InstanceId, costPrompt.ValidChoices);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-03M1")]
    [Trait("L12Evidence", "card:S01-0018")]
    [Trait("L12Evidence", "entry:granted-entry-effect-response-boundary")]
    [Trait("L12Evidence", "reconnect:v2-opponent-entry-response")]
    public void PitfallCanNegateTheEntryChargeGrantedByThor()
    {
        var game = Create(100303);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var entering = Card("ST03-09", "thor-granted-entry-negated", 0);
        var pitfall = SetCounter(game, "S01-0018", "thor-granted-entry-pitfall");
        player.Hand.Add(entering);
        ResolveThorCharge(game);

        var played = game.Handle(0,
            new L12Command("playCard", entering.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        var controllerPriority = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", controllerPriority.Kind);
        var grantedEntry = Assert.Single(game.State.EffectStack);
        Assert.Equal(entering.InstanceId, grantedEntry.SourceInstanceId);
        Assert.Equal("enter", grantedEntry.Trigger);
        ResolveOnlyPrompt(game, "pass");

        var defenderPriority = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, defenderPriority.PlayerIndex);
        Assert.Contains(pitfall.InstanceId, defenderPriority.ValidChoices);

        game = Restore(game);
        Assert.Contains(pitfall.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);
        ResolveOnlyPrompt(game, pitfall.InstanceId);
        PassResponses(game);

        var restoredEntering = Assert.IsType<L12CardInstance>(game.State.Players[0].Field[0][0]);
        Assert.Equal(entering.InstanceId, restoredEntering.InstanceId);
        Assert.False(restoredEntering.HasCharge);
        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == pitfall.InstanceId);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-03M1")]
    [Trait("L12Evidence", "card:S01-0016")]
    [Trait("L12Evidence", "entry:granted-entry-effect-absolute-defense")]
    public void AbsoluteDefenseCanAlsoNegateTheEntryChargeGrantedByThor()
    {
        var game = Create(100305);
        var entering = Card("ST03-09", "thor-granted-entry-absolute-negated", 0);
        var absoluteDefense = SetCounter(game, "S01-0016", "thor-granted-entry-absolute-defense");
        var discard = Card("S01-0003", "thor-granted-entry-absolute-discard", 1);
        game.State.Players[0].Hand.Add(entering);
        game.State.Players[1].Hand.Add(discard);
        ResolveThorCharge(game);

        var played = game.Handle(0,
            new L12Command("playCard", entering.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        ResolveOnlyPrompt(game, "pass");
        Assert.Contains(absoluteDefense.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);
        ResolveOnlyPrompt(game, absoluteDefense.InstanceId);
        Assert.Contains(discard.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);
        ResolveOnlyPrompt(game, discard.InstanceId);
        PassResponses(game);

        var fieldCard = Assert.IsType<L12CardInstance>(game.State.Players[0].Field[0][0]);
        Assert.False(fieldCard.HasCharge);
        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == absoluteDefense.InstanceId);
        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == discard.InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-03M1")]
    [Trait("L12Evidence", "entry:printed-and-granted-enter-independent-order")]
    public void PrintedAndThorGrantedEntryEffectsShareTimingButRemainIndependentStackItems()
    {
        var game = Create(100306);
        var player = game.State.Players[0];
        var entering = Card("S01-0301", "thor-printed-and-granted-entry", 0);
        var pitfall = SetCounter(game, "S01-0018", "thor-only-granted-pitfall");
        player.Hand.Add(entering);
        player.Library.AddRange([
            Card("ST03-09", "thor-printed-mill-one", 0),
            Card("ST03-06", "thor-printed-mill-two", 0),
        ]);
        ResolveThorCharge(game);

        var played = game.Handle(0,
            new L12Command("playCard", entering.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-batch-order", order.Continuation);
        var thorCandidate = Assert.Single(order.ValidChoices,
            id => order.Data[id].Contains("雷神索尔", StringComparison.Ordinal));
        var printedCandidate = Assert.Single(order.ValidChoices, id => id != thorCandidate);
        ResolveOnlyPrompt(game, printedCandidate, thorCandidate);

        Assert.Equal("true", Assert.Single(game.State.EffectStack)
            .Data.GetValueOrDefault("thorGrantedEntryCharge"));
        ResolveOnlyPrompt(game, "pass");
        Assert.Contains(pitfall.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);
        ResolveOnlyPrompt(game, pitfall.InstanceId);
        PassResponses(game);

        var fieldCard = Assert.IsType<L12CardInstance>(player.Field[0][0]);
        Assert.False(fieldCard.HasCharge);
        Assert.Empty(player.Library);
        Assert.Contains(player.Graveyard, card => card.InstanceId == "thor-printed-mill-one");
        Assert.Contains(player.Graveyard, card => card.InstanceId == "thor-printed-mill-two");
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-03M1")]
    [Trait("L12Evidence", "entry:effect-generated-granted-enter-response")]
    public void EffectEntryUsesTheSameThorGrantedResponseBoundary()
    {
        var game = Create(100307);
        var player = game.State.Players[0];
        var alvida = Card("S01-0307", "thor-effect-entry-alvida", 0);
        var entering = Card("ST03-09", "thor-effect-entry-legion", 0, disasterLevel: 2);
        var pitfall = SetCounter(game, "S01-0018", "thor-effect-entry-pitfall");
        player.Field[0][0] = alvida;
        player.Hand.Add(entering);
        ResolveThorCharge(game);

        var activated = game.Handle(0,
            new L12Command("activateAbility", alvida.InstanceId, Ability: "alvidaSummon"));
        Assert.True(activated.Accepted, activated.Error);
        ResolveOnlyPrompt(game, entering.InstanceId);
        ResolveOnlyPrompt(game, "0:1");
        AdvanceToThorGrantedEntryResponse(game);
        ResolveOnlyPrompt(game, "pass");
        Assert.Contains(pitfall.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);
        ResolveOnlyPrompt(game, pitfall.InstanceId);
        PassResponses(game);

        var fieldCard = Assert.IsType<L12CardInstance>(player.Field[0][1]);
        Assert.Equal(entering.InstanceId, fieldCard.InstanceId);
        Assert.False(fieldCard.HasCharge);
        Assert.Contains(player.Graveyard, card => card.InstanceId == alvida.InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-03M1")]
    [Trait("L12Evidence", "entry:granted-entry-source-left-field")]
    public void ThorGrantedEntryDoesNotApplyAfterItsSourceLeavesTheField()
    {
        var game = Create(100308);
        var entering = Card("ST03-09", "thor-granted-entry-left-field", 0);
        game.State.Players[0].Hand.Add(entering);
        ResolveThorCharge(game);
        Assert.True(game.Handle(0,
            new L12Command("playCard", entering.InstanceId, Row: 0, Slot: 0)).Accepted);

        var destroyed = game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: entering.InstanceId));
        Assert.True(destroyed.Accepted, destroyed.Error);
        PassResponses(game);

        var graveCard = Assert.Single(game.State.Players[0].Graveyard,
            card => card.InstanceId == entering.InstanceId);
        Assert.False(graveCard.HasCharge);
        Assert.Null(game.State.Players[0].Field[0][0]);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-03M1")]
    [Trait("L12Evidence", "entry:granted-entry-effect-resolves")]
    [Trait("L12Evidence", "reconnect:v2-granted-entry-resolution")]
    public void ThorGrantedEntryChargeStillResolvesWhenBothPlayersPass()
    {
        var game = Create(100304);
        var entering = Card("ST03-09", "thor-granted-entry-resolves", 0);
        game.State.Players[0].Hand.Add(entering);
        ResolveThorCharge(game);

        var played = game.Handle(0,
            new L12Command("playCard", entering.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);

        game = Restore(game);
        PassResponses(game);

        var restoredEntering = Assert.IsType<L12CardInstance>(game.State.Players[0].Field[0][0]);
        Assert.Equal(entering.InstanceId, restoredEntering.InstanceId);
        Assert.True(restoredEntering.HasCharge);
        Assert.Empty(game.State.EffectStack);
    }
}
