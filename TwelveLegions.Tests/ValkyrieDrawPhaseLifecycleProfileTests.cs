using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ValkyrieDrawPhaseLifecycleProfileTests
{
    private const string AbilityId = "S01-03M1:ability:static:f1ba346550e4decc";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence(AbilityId, "normal", "reconnect", "presentation-consumers")]
    public void FirstTurnDrawPhaseMillsTwoSequentialCardsAndRestoresWithoutRepeating()
    {
        var game = Create(72480);
        game.State.FirstPlayer = 0;
        game.State.ActivePlayer = 0;
        game.State.Round = 1;
        var player = game.State.Players[0];
        var handBefore = player.Hand.Count;
        var libraryBefore = player.Library.Count;
        var graveBefore = player.Graveyard.Count;

        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);

        Assert.Equal(handBefore, player.Hand.Count);
        Assert.Equal(libraryBefore - 2, player.Library.Count);
        Assert.Equal(graveBefore + 2, player.Graveyard.Count);
        Assert.Contains(game.State.Events, entry => entry.Type == "phase-detail"
            && entry.Text.Contains("弃置牌库顶部2张牌", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "draw-skipped" && entry.PlayerIndex == 0);
        var visibleGraveCount = GraveyardProjection(game).GetArrayLength();
        Assert.Equal(player.Graveyard.Count, visibleGraveCount);

        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        player = game.State.Players[0];
        Assert.Equal(handBefore, player.Hand.Count);
        Assert.Equal(libraryBefore - 2, player.Library.Count);
        Assert.Equal(graveBefore + 2, player.Graveyard.Count);
        Assert.Equal(visibleGraveCount, GraveyardProjection(game).GetArrayLength());
        Assert.Single(game.State.Events, entry => entry.Type == "phase-detail"
            && entry.Text.Contains("弃置牌库顶部2张牌", StringComparison.Ordinal));
    }

    [Fact]
    [L12AbilityEvidence(AbilityId, "short-library-sequential")]
    public void OneCardLibraryDiscardsItsAvailableTopCardWithoutInventingASecondCard()
    {
        var game = Create(72481);
        game.State.FirstPlayer = 0;
        game.State.ActivePlayer = 0;
        game.State.Round = 1;
        var player = game.State.Players[0];
        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        var onlyCard = player.Library[0];
        player.Library.Clear();
        player.Library.Add(onlyCard);
        var graveBefore = player.Graveyard.Count;

        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);

        Assert.Empty(player.Library);
        Assert.Contains(onlyCard, player.Graveyard);
        Assert.Equal(graveBefore + 1, player.Graveyard.Count);
        Assert.NotEqual(L12Phase.GameOver, game.State.Phase);
    }

    private static JsonElement GraveyardProjection(L12GameEngine game)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return snapshot.GetProperty("players")[0].GetProperty("graveyard");
    }

    private static L12GameEngine Create(int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "瓦尔基里抽牌阶段测试",
            MasterId = "S01-03M1",
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        return new L12GameEngine(Catalog, "valkyrie-draw-phase", "VALKYRIE-DRAW", seed,
            ["甲", "乙"], [deck, baseDeck], skipPreparation: true, disasterMode: "none",
            stateFormatVersion: 2);
    }
}
