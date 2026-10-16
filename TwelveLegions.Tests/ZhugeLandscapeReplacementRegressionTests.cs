using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ZhugeLandscapeReplacementRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-16T08:00:00Z");

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = cardId,
            OwnerIndex = owner,
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static L12MoraleCard Morale(string instanceId, bool tapped = false)
        => new() { InstanceId = instanceId, CardId = "S01-04C1", Tapped = tapped };

    private static L12GameEngine Create(int controller)
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "诸葛亮替换山河社稷图回归",
            MasterId = "S01-04M1",
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "synthetic-zhuge-landscape-replacement", "LOCAL", 299117,
            ["合成甲", "合成乙"], [deck, deck], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            utcNow: () => Now, stateFormatVersion: 2);
        game.State.PendingPrompts.Clear();
        game.State.ActivePlayer = controller;
        game.State.FirstPlayer = controller;
        game.State.Round = 3;
        game.State.TurnSerial = 10;
        game.State.Phase = L12Phase.Main;

        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Removed.Clear();
            player.Resolving.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
            for (var i = 0; i < 4; i++)
            {
                player.Library.Add(Card("S01-0003", $"library-{player.PlayerIndex}-{i}", player.PlayerIndex));
                player.MoraleDeck.Add(Morale($"morale-deck-{player.PlayerIndex}-{i}"));
            }
        }

        return game;
    }

    private static void Handle(L12GameEngine game, int actor, L12Command command)
    {
        var result = game.Handle(actor, command);
        Assert.True(result.Accepted, result.Error);
    }

    private static void ResolveOnlyPrompt(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Handle(game, prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var count = 0;
             count < 60 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response";
             count++)
            ResolveOnlyPrompt(game, "pass");
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }

    private static L12GameEngine RestoreAndAssertStable(L12GameEngine game, bool restore)
    {
        var fullState = game.SerializeFullState();
        var stateHash = game.ComputeStateHash();
        var randomState = game.RandomState
            ?? throw new InvalidOperationException("V2 regression fixture requires deterministic RNG state");
        var cardFactSignalSequence = game.CardFactSignalSequence;
        var revision = game.State.Revision;
        var eventSequence = game.State.EventSequence;
        var promptSequence = game.State.PromptSequence;
        var stackSequence = game.State.StackSequence;
        var activationSequence = game.State.ActivationSequence;
        var triggerBatchSequence = game.State.TriggerBatchSequence;
        var authorityEventSequence = game.State.AuthorityEventSequence;

        var result = restore
            ? L12GameEngine.RestoreCheckpoint(Catalog, fullState, randomState, cardFactSignalSequence,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now)
            : game;

        Assert.Equal(fullState, result.SerializeFullState());
        Assert.Equal(stateHash, result.ComputeStateHash());
        Assert.True(result.RandomState.HasValue);
        Assert.Equal(randomState, result.RandomState.Value);
        Assert.Equal(cardFactSignalSequence, result.CardFactSignalSequence);
        Assert.Equal(revision, result.State.Revision);
        Assert.Equal(eventSequence, result.State.EventSequence);
        Assert.Equal(promptSequence, result.State.PromptSequence);
        Assert.Equal(stackSequence, result.State.StackSequence);
        Assert.Equal(activationSequence, result.State.ActivationSequence);
        Assert.Equal(triggerBatchSequence, result.State.TriggerBatchSequence);
        Assert.Equal(authorityEventSequence, result.State.AuthorityEventSequence);
        return result;
    }

    private static IEnumerable<L12CardInstance> AllCards(L12PlayerState player)
        => player.Hand.Concat(player.Library).Concat(player.Graveyard).Concat(player.Removed)
            .Concat(player.Resolving).Concat(player.ExtraRelics)
            .Concat(player.Field.SelectMany(row => row).OfType<L12CardInstance>())
            .Concat(player.Relic is null ? [] : [player.Relic]);

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "bug:BUG-20260914-7b7f071d")]
    public void ZhugeAttackReplacingExistingLandscapeStillAddsTheNewEntryMorale(
        int controller, bool restore)
    {
        var game = Create(controller);
        var owner = game.State.Players[controller];
        var defender = game.State.Players[1 - controller];
        var zhuge = Card("S01-0111", $"zhuge-{controller}", controller);
        var oldLandscape = Card("S01-0117", $"old-landscape-{controller}", controller);
        var newLandscape = Card("S01-0117", $"new-landscape-{controller}", controller);
        var costMorale = Morale($"zhuge-return-cost-{controller}");
        owner.Field[0][0] = zhuge;
        owner.Relic = oldLandscape;
        owner.Morale.Add(costMorale);
        owner.Library.Insert(0, newLandscape);
        defender.Hp = Math.Max(defender.Hp, 20);
        var initialMoraleDeckCount = owner.MoraleDeck.Count;

        Handle(game, controller, new L12Command("attack", zhuge.InstanceId,
            Target: new L12AttackTarget("master")));
        PassResponses(game);
        var activation = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(controller, activation.PlayerIndex);
        Assert.Equal("zhuge-peek-pay", activation.Data["action"]);
        ResolveOnlyPrompt(game, "yes");
        PassResponses(game);
        var artifactChoice = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(controller, artifactChoice.PlayerIndex);
        Assert.Equal("zhuge-artifact", artifactChoice.Data["action"]);
        Assert.Contains("play", artifactChoice.ValidChoices);
        ResolveOnlyPrompt(game, "play");

        owner = game.State.Players[controller];
        Assert.Empty(owner.Morale);
        Assert.Equal(initialMoraleDeckCount + 1, owner.MoraleDeck.Count);
        Assert.Equal(1, owner.ReturnedMoraleThisTurn);
        Assert.Equal(newLandscape.InstanceId, owner.Relic?.InstanceId);
        Assert.Equal(oldLandscape.InstanceId,
            Assert.Single(owner.Graveyard, card => card.InstanceId == oldLandscape.InstanceId).InstanceId);

        game = RestoreAndAssertStable(game, restore);
        PassResponses(game);
        owner = game.State.Players[controller];
        defender = game.State.Players[1 - controller];

        Assert.Equal(newLandscape.InstanceId, owner.Relic?.InstanceId);
        Assert.Single(game.State.Players.SelectMany(AllCards), card => card.InstanceId == newLandscape.InstanceId);
        Assert.Equal(oldLandscape.InstanceId,
            Assert.Single(owner.Graveyard, card => card.InstanceId == oldLandscape.InstanceId).InstanceId);
        Assert.Single(game.State.Players.SelectMany(AllCards), card => card.InstanceId == oldLandscape.InstanceId);
        Assert.Single(owner.Morale);
        Assert.False(owner.Morale[0].Tapped);
        Assert.Equal(initialMoraleDeckCount, owner.MoraleDeck.Count);
        Assert.Equal(1, owner.ReturnedMoraleThisTurn);
        Assert.Single(game.State.Events, entry =>
            entry.Text.Contains("山河社稷图从士气牌库追加 1 张活跃士气", StringComparison.Ordinal));

        Assert.Equal(L12CombatStage.DefenseChoice, game.State.PendingDefense?.Stage);
        Handle(game, 1 - controller, new L12Command("resolveDefense", CardInstanceIds: []));
        PassResponses(game);
        Assert.Null(game.State.PendingDefense);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Null(game.State.ResponseWindow);
        Assert.Null(game.State.Winner);

        var turnSerial = game.State.TurnSerial;
        Handle(game, controller, new L12Command("endTurn"));
        Assert.Equal(1 - controller, game.State.ActivePlayer);
        Assert.True(game.State.TurnSerial > turnSerial);
        Assert.NotEqual(L12Phase.GameOver, game.State.Phase);
        Assert.Null(game.State.Winner);
        Assert.NotEmpty(defender.Library);
    }
}
