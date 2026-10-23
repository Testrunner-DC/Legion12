using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class DisasterReplacementContinuousStateTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> ReplacementEntries()
    {
        foreach (var entry in new[] { "turn", "entry", "gm" })
        foreach (var owner in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
            yield return [entry, owner, restore];
    }

    [Theory]
    [MemberData(nameof(ReplacementEntries))]
    public void BuzhouReadsTroopsAfterLustHasEnded(string entry, int owner, bool restore)
    {
        var game = Create();
        var player = game.State.Players[owner];
        player.Field[0][0] = Card("S01-0404", "sanada", owner);
        player.Field[0][1] = Card("S01-0404", "independent-bonus", owner);
        player.Field[0][1]!.AttachedCards.Add(Card("S02-06S2", "independent-sword", owner));
        player.Field[0][2] = Card("S01-0404", "independent-penalty", owner);
        player.Field[0][2]!.Troops -= 1000;
        player.Field[1][2] = Card("S01-0404", "rear-sanada", owner);
        var oldDisaster = Card("ST-DS02", "old-lust", 0);
        game.State.ActiveDisaster = oldDisaster;
        game.State.DisasterDeck.Add(Card("ST-DS01", "new-buzhou", 0));
        // Materialize the same derived layer that the previous live command/snapshot left behind.
        _ = game.SnapshotFor(owner);
        Assert.Equal(5000, player.Field[0][0]!.CurrentTroops);
        Assert.Equal(6000, player.Field[0][1]!.CurrentTroops);
        Assert.Equal(4000, player.Field[0][2]!.CurrentTroops);
        if (restore) game = Restore(game);

        Trigger(game, entry);

        player = game.State.Players[owner];
        Assert.Null(player.Field[0][0]);
        Assert.Null(player.Field[0][2]);
        Assert.Single(player.Graveyard, card => card.InstanceId == "sanada");
        Assert.Single(player.Graveyard, card => card.InstanceId == "independent-penalty");
        Assert.Equal("independent-bonus", player.Field[0][1]?.InstanceId);
        Assert.Equal(5000, player.Field[0][1]!.CurrentTroops);
        Assert.Equal("rear-sanada", player.Field[1][2]?.InstanceId);
        Assert.Equal(4000, player.Field[1][2]!.CurrentTroops);
        Assert.All(player.Field.SelectMany(row => row).OfType<L12CardInstance>(), card =>
            Assert.DoesNotContain(card.ContinuousTroopsBonusLayers.Keys, key => key.Contains("old-lust")));
        Assert.Equal("new-buzhou", game.State.ActiveDisaster?.InstanceId);
        Assert.Single(game.State.RemovedDisasters, card => card.InstanceId == oldDisaster.InstanceId);
        Assert.Single(game.State.Events, item => item.Type == "disaster" && item.Cards.Any(card => card.InstanceId == "new-buzhou"));
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
    }

    [Theory]
    [InlineData("ds01-free-tactic", false)]
    [InlineData("ds01-free-tactic", true)]
    [InlineData("ds01-back-master", false)]
    [InlineData("ds01-back-master", true)]
    public void ResolvedMorningStarTurnGrantSurvivesReplacementUntilTurnEnd(string grant, bool restore)
    {
        var game = Create();
        game.State.ActiveDisaster = Card("S01-DS01", "old-morning-star", 0);
        game.State.Players[0].UsedAbilities.Add(grant);
        game.State.DisasterDeck.Add(Card("ST-DS01", "new-buzhou", 0));
        if (restore) game = Restore(game);

        Trigger(game, "gm");

        Assert.Contains(grant, game.State.Players[0].UsedAbilities);
        var result = game.Handle(0, new L12Command("endTurn"));
        Assert.True(result.Accepted, result.Error);
        Assert.DoesNotContain(grant, game.State.Players[0].UsedAbilities);
    }

    public static IEnumerable<object[]> OutgoingDisasters()
        => Catalog.Cards.Values.Where(card => card.CardType == "destruction" && card.Id != "S01-DS10")
            .OrderBy(card => card.Id, StringComparer.Ordinal)
            .SelectMany(card => new[] { new object[] { card.Id, false }, new object[] { card.Id, true } });

    [Theory]
    [MemberData(nameof(OutgoingDisasters))]
    public void EveryReplaceableDisasterUsesTheSameCurrentStateBoundary(string outgoing, bool restore)
    {
        var game = Create();
        game.State.ActiveDisaster = Card(outgoing, "outgoing", 0);
        game.State.Players[0].Field[0][0] = Card("S01-0404", "threshold-sanada", 0);
        game.State.DisasterDeck.Add(Card("ST-DS01", "incoming", 0));
        _ = game.SnapshotFor(0);
        if (restore) game = Restore(game);

        Trigger(game, "gm");

        Assert.Null(game.State.Players[0].Field[0][0]);
        Assert.Single(game.State.Players[0].Graveyard, card => card.InstanceId == "threshold-sanada");
        Assert.Equal("incoming", game.State.ActiveDisaster?.InstanceId);
        Assert.Single(game.State.RemovedDisasters, card => card.InstanceId == "outgoing");
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LustExpirationRetainsTheRealAdjacentAuraAndItsSource(bool restore)
    {
        var game = Create();
        var owner = game.State.Players[0];
        owner.Field[0][0] = Card("S01-0404", "adjacent-sanada", 0);
        owner.Field[0][1] = Card("S02-0516", "hannibal", 0);
        owner.Field[0][1]!.Troops += 3000;
        game.State.ActiveDisaster = Card("ST-DS02", "old-lust", 0);
        game.State.DisasterDeck.Add(Card("ST-DS01", "new-buzhou", 0));
        _ = game.SnapshotFor(0);
        Assert.Equal(6000, owner.Field[0][0]!.CurrentTroops);
        if (restore) game = Restore(game);

        Trigger(game, "gm");

        owner = game.State.Players[0];
        Assert.Equal("adjacent-sanada", owner.Field[0][0]?.InstanceId);
        Assert.Equal(5000, owner.Field[0][0]!.CurrentTroops);
        Assert.Contains(owner.Field[0][0]!.ContinuousTroopsBonusLayers.Keys, key => key.Contains("hannibal"));
        Assert.DoesNotContain(owner.Field[0][0]!.ContinuousTroopsBonusLayers.Keys, key => key.Contains("old-lust"));
        Assert.Equal("hannibal", owner.Field[0][1]?.InstanceId);
        Assert.Empty(owner.Graveyard);
    }

    private static void Trigger(L12GameEngine game, string entry)
    {
        game.State.DisasterValue = 8;
        CommandResult result;
        if (entry == "gm") result = game.HandleGm(new L12GmCommand("triggerDisaster"));
        else if (entry == "turn") result = game.Handle(0, new L12Command("endTurn"));
        else
        {
            game.State.Players[0].Hand.Add(Card("S01-0404", "entry-source", 0));
            for (var i = 0; i < 5; i++)
                game.State.Players[0].Morale.Add(new L12MoraleCard { CardId = "S01-04C1", InstanceId = $"pay-{i}" });
            result = game.Handle(0, new L12Command("playCard", "entry-source", Row: 1, Slot: 0));
        }
        Assert.True(result.Accepted, result.Error);
        for (var safety = 0; safety < 40 && game.State.PendingPrompts.FirstOrDefault() is { } prompt; safety++)
        {
            Assert.Equal("response", prompt.Kind);
            result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static L12GameEngine Create()
    {
        var game = new L12GameEngine(Catalog, "disaster-replacement", "SYNTHETIC", 2026102307,
            ["P0", "P1"], [0, 0], skipPreparation: true, disasterMode: "all", stateFormatVersion: 2,
            autoPassEmptyResponses: true, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 3;
        game.State.TurnSerial = 25;
        game.State.Phase = L12Phase.Main;
        game.State.DisasterDeck.Clear();
        game.State.RemovedDisasters.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
            player.SpecialZones.Trials.Clear();
        }
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: true, concealHiddenResponseAvailability: false);

    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var definition = Catalog.Cards[id];
        return new L12CardInstance
        {
            CardId = id, InstanceId = instance, OwnerIndex = owner, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction, ImageUrl = definition.ImageUrl,
            EffectText = definition.Effect, Cost = definition.Cost ?? 0, HasPrintedCost = definition.Cost.HasValue,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0, Profession = definition.Profession,
            EffectiveProfession = definition.Profession, Traits = [.. definition.Traits], SummonRound = -1,
        };
    }
}
