using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class NephthysEffectiveFactionDiscount20261026Tests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(
        Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int owner, int seed)
    {
        var basis = Catalog.DeckAt(0);
        var nephthys = new L12PresetDeckDefinition
        {
            Name = "奈芙蒂斯有效阵营减费回归",
            MasterId = "S02-02M1",
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var decks = owner == 0
            ? new[] { nephthys, basis }
            : new[] { basis, nephthys };
        var game = new L12GameEngine(Catalog, "nephthys-effective-faction", "NEPHTHYS-FACTION",
            seed, ["甲", "乙"], decks, skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        game.State.ActivePlayer = owner;
        game.State.FirstPlayer = owner;
        game.State.Round = 2;
        game.State.TurnSerial = 5;
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
            player.Relic = null;
            player.ExtraRelics.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
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
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static void AddReadyMorale(L12PlayerState player, int count, string prefix)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-02C1",
                InstanceId = $"{prefix}-{index}",
            });
    }

    private static void ResolveOnlyPrompt(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt",
            PromptId: prompt.PromptId, CardInstanceIds: [.. choices]));
        Assert.True(result.Accepted, result.Error);
    }

    private static int PassResponses(L12GameEngine game)
    {
        var count = 0;
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
            count++;
        }
        return count;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

    private static int HandPlayCost(L12GameEngine game, int owner, string instanceId)
    {
        var view = game.SnapshotFor(owner).Players[owner];
        var hand = Assert.IsType<L12CardInstance[]>(view.GetType().GetProperty("hand")!.GetValue(view));
        return Assert.Single(hand, card => card.InstanceId == instanceId).PlayCost!.Value;
    }

    [Theory]
    [InlineData(0, true, true)]
    [InlineData(1, true, false)]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [Trait("L12Evidence", "bug:BUG-20261025-48d498f3")]
    [Trait("L12Evidence", "card:S02-02M1")]
    [Trait("L12Evidence", "card:S02-0008")]
    [Trait("L12Evidence", "card:S01-0001")]
    [Trait("L12Evidence", "reconnect:v2-nephthys-effective-faction-discount")]
    public void RingEffectiveFactionControlsNephthysQuotePaymentAndSingleConsumption(
        int owner, bool ringActive, bool restoreBeforeResolution)
    {
        var game = Create(owner, 26102610 + owner * 10 + (ringActive ? 1 : 0));
        var player = game.State.Players[owner];
        if (ringActive)
            player.Relic = Card("S02-0008", $"ring-{owner}", owner);

        var firstSacrifice = Card("S02-0004", $"sacrifice-a-{owner}", owner);
        var secondSacrifice = Card("S02-0005", $"sacrifice-b-{owner}", owner);
        player.Field[0][0] = firstSacrifice;
        player.Field[1][1] = secondSacrifice;
        var blackbeard = Card("S01-0001", $"blackbeard-first-{owner}", owner);
        var laterBlackbeard = Card("S01-0001", $"blackbeard-later-{owner}", owner);
        player.Hand.AddRange([blackbeard, laterBlackbeard]);
        AddReadyMorale(player, 10, $"nephthys-payment-{owner}");

        var activation = game.Handle(owner, new L12Command("activateAbility", $"master-{owner}",
            Ability: "nephthysSacrifice"));
        Assert.True(activation.Accepted, activation.Error);
        ResolveOnlyPrompt(game, firstSacrifice.InstanceId, secondSacrifice.InstanceId);
        if (restoreBeforeResolution) game = Restore(game);
        Assert.Equal(2, PassResponses(game));

        player = game.State.Players[owner];
        Assert.Equal(2, player.NextS2SunDisasterLegionDiscount);
        Assert.Equal(2, player.Graveyard.Count(card =>
            card.InstanceId is var id && (id == firstSacrifice.InstanceId || id == secondSacrifice.InstanceId)));

        var expectedQuotedCost = ringActive ? blackbeard.Cost - 2 : blackbeard.Cost;
        Assert.Equal(expectedQuotedCost, HandPlayCost(game, owner, blackbeard.InstanceId));
        var payment = player.Morale.Where(card => !card.Tapped).Take(expectedQuotedCost)
            .Select(card => card.InstanceId).ToList();
        var play = game.Handle(owner, new L12Command("playCard", blackbeard.InstanceId,
            CardInstanceIds: payment, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);

        Assert.Equal(expectedQuotedCost, player.Morale.Count(card => card.Tapped));
        Assert.Equal(ringActive ? 0 : 2, player.NextS2SunDisasterLegionDiscount);
        Assert.Equal(laterBlackbeard.Cost, HandPlayCost(game, owner, laterBlackbeard.InstanceId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("L12Evidence", "discount:next-effective-master-faction-legion")]
    [Trait("L12Evidence", "card:S02-0008")]
    [Trait("L12Evidence", "card:S01-0001")]
    public void RingEffectiveFactionAlsoControlsGenericFactionDiscountQuoteAndConsumption(
        bool ringActive)
    {
        const int owner = 0;
        var game = Create(owner, 26102620 + (ringActive ? 1 : 0));
        var player = game.State.Players[owner];
        if (ringActive)
            player.Relic = Card("S02-0008", "generic-faction-ring", owner);

        player.NextFactionLegionDiscount = 2;
        var blackbeard = Card("S01-0001", "generic-faction-blackbeard-first", owner);
        var laterBlackbeard = Card("S01-0001", "generic-faction-blackbeard-later", owner);
        player.Hand.AddRange([blackbeard, laterBlackbeard]);
        AddReadyMorale(player, 10, "generic-faction-payment");

        var expectedQuotedCost = ringActive ? blackbeard.Cost - 2 : blackbeard.Cost;
        Assert.Equal(expectedQuotedCost, HandPlayCost(game, owner, blackbeard.InstanceId));
        var payment = player.Morale.Where(card => !card.Tapped).Take(expectedQuotedCost)
            .Select(card => card.InstanceId).ToList();
        var play = game.Handle(owner, new L12Command("playCard", blackbeard.InstanceId,
            CardInstanceIds: payment, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);

        Assert.Equal(expectedQuotedCost, player.Morale.Count(card => card.Tapped));
        Assert.Equal(ringActive ? 0 : 2, player.NextFactionLegionDiscount);
        Assert.Equal(laterBlackbeard.Cost, HandPlayCost(game, owner, laterBlackbeard.InstanceId));
    }
}
