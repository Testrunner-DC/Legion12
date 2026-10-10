using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PlayerReportedStackOrderingRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(string masterId, int seed)
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "Player reported stack ordering",
            MasterId = masterId,
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [.. basis.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "player-reported-stack-ordering", "STACKORDER", seed,
            ["甲", "乙"], [deck, basis], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
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
            player.Removed.Clear();
            player.ExtraRelics.Clear();
            player.Relic = null;
            player.Morale.Clear();
            player.UsedAbilities.Clear();
            player.SpecialZones.CanopicProgress.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int? owner = null)
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
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
            OwnerIndex = owner,
        };
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice);
        var result = game.Handle(prompt.PlayerIndex, command);
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 50
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
        Assert.NotEqual("response", game.State.PendingPrompts.FirstOrDefault()?.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("L12Evidence", "ability:isisCanopic")]
    [Trait("L12Evidence", "cancel-zero-zone-mutation")]
    [Trait("L12Evidence", "reconnect-idempotency")]
    public void IsisCanopicCancellationNeverMovesTheJarOrPaysTheGuardCost(bool cancelAfterJarSelection)
    {
        var game = Create("S01-02M1", cancelAfterJarSelection ? 10102 : 10101);
        var player = game.State.Players[0];
        for (var slot = 0; slot < 3; slot++)
            player.Field[0][slot] = Card("S01-0212", $"isis-cancel-guard-{slot}", 0);
        player.Graveyard.Add(Card("S01-0216", "isis-cancel-canopic", 0));

        var started = game.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "isisCanopic"));
        Assert.True(started.Accepted, started.Error);
        Assert.Equal("isis-cancel-canopic", Assert.Single(game.State.PendingPrompts)
            .ValidChoices.Single(choice => choice != "skip"));

        if (cancelAfterJarSelection)
        {
            Resolve(game, "isis-cancel-canopic");
            Assert.Contains("skip", Assert.Single(game.State.PendingPrompts).ValidChoices);
        }

        game = Restore(game);
        player = game.State.Players[0];
        var cancellation = Assert.Single(game.State.PendingPrompts);
        var command = new L12Command("resolvePrompt", PromptId: cancellation.PromptId, Choice: "skip");
        Assert.True(game.Handle(0, command).Accepted);
        Assert.False(game.Handle(0, command).Accepted);

        Assert.Empty(player.SpecialZones.CanopicProgress);
        Assert.Contains(player.Graveyard, card => card.InstanceId == "isis-cancel-canopic");
        Assert.Equal(3, player.Field[0].Count(card => card?.CardId == "S01-0212"));
        Assert.DoesNotContain(player.Graveyard, card => card.CardId == "S01-0212");
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(player.UsedAbilities, key => key.Contains("isisCanopic", StringComparison.Ordinal));

        // The same legal card remains addressable after cancellation; it did not become a
        // detached instance in the progress/relic presentation zone.
        var retry = game.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "isisCanopic"));
        Assert.True(retry.Accepted, retry.Error);
        Assert.Contains("isis-cancel-canopic", Assert.Single(game.State.PendingPrompts).ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0102")]
    [Trait("L12Evidence", "card:S01-0005")]
    [Trait("L12Evidence", "disaster-after-complete-stack")]
    [Trait("L12Evidence", "reconnect-idempotency")]
    public void LiMuVolleyFullyResolvesBeforeItsEntrySchedulesTheDisaster()
    {
        var game = Create("S02-01M1", 10103);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var liMu = Card("S02-0102", "limu-disaster-source", 0);
        var volley = Card("S01-0005", "limu-disaster-volley", 0);
        var target = Card("S01-0103", "limu-disaster-target", 1);
        player.Hand.Add(liMu);
        player.Library.AddRange([volley, Card("S01-0001", "limu-disaster-filler", 0)]);
        opponent.Field[0][0] = target;
        for (var index = 0; index < liMu.Cost; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S02-01C1",
                InstanceId = $"limu-disaster-morale-{index}",
            });
        game.State.DisasterValue = 7;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(Card("S01-DS10", "limu-scheduled-disaster"));

        var played = game.Handle(0,
            new L12Command("playCard", liMu.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        Assert.True(game.State.CheckDisasterAfterStack);
        Assert.Null(game.State.ActiveDisaster);

        Resolve(game, "mode:use");
        Assert.Null(game.State.ActiveDisaster);
        Resolve(game, "mode:none");
        PassResponses(game);
        var freePlay = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-limu-tactic", freePlay.Data["action"]);
        Resolve(game, "play");

        Assert.Contains(volley, player.Resolving);
        Assert.True(game.State.CheckDisasterAfterStack);
        Assert.Null(game.State.ActiveDisaster);
        game = Restore(game);
        player = game.State.Players[0];
        opponent = game.State.Players[1];
        volley = Assert.Single(player.Resolving, card => card.InstanceId == "limu-disaster-volley");
        target = Assert.IsType<L12CardInstance>(opponent.Field[0][0]);

        var declaration = Assert.Single(game.State.PendingPrompts);
        var declareFront = new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            Choice: "mode:front");
        Assert.True(game.Handle(0, declareFront).Accepted);
        Assert.False(game.Handle(0, declareFront).Accepted);
        Assert.Null(game.State.ActiveDisaster);
        Assert.True(game.State.EffectStack.Count > 0 || game.State.DeferredEffectStack.Count > 0
            || game.State.PendingPrompts.Count > 0);

        PassResponses(game);

        Assert.Equal(target.BaseTroops - 2000, target.Troops);
        Assert.Contains(player.Graveyard, card => card.InstanceId == volley.InstanceId);
        Assert.Equal("limu-scheduled-disaster", game.State.ActiveDisaster?.InstanceId);
        Assert.False(game.State.CheckDisasterAfterStack);
        var volleySettled = Assert.Single(game.State.Events, action => action.Type == "effect-result"
            && action.Cards.Any(card => card.InstanceId == volley.InstanceId));
        var disasterOpened = Assert.Single(game.State.Events, action => action.Type == "disaster"
            && action.Cards.Any(card => card.InstanceId == "limu-scheduled-disaster"));
        Assert.True(volleySettled.Sequence < disasterOpened.Sequence);
    }
}
