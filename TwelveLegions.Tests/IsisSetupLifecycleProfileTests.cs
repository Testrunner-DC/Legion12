using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class IsisSetupLifecycleProfileTests
{
    private const string AbilityId = "S01-02M1:ability:static:68187ab0edb25d9c";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence(AbilityId, "normal", "reconnect", "presentation-consumers")]
    public void IsisSetupCreatesExactlyOneOsirisInItsOwnersGraveyardAcrossRestore()
    {
        var game = Create(72460);
        var player = game.State.Players[0];
        var osiris = Assert.Single(player.Graveyard, card => card.CardId == "S01-02M2");
        Assert.DoesNotContain(player.Hand, card => card.CardId == "S01-02M2");
        Assert.DoesNotContain(player.Library, card => card.CardId == "S01-02M2");
        Assert.Contains(GraveyardProjection(game), card => card.GetProperty("instanceId").GetString() == osiris.InstanceId);

        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        player = game.State.Players[0];
        var restored = Assert.Single(player.Graveyard, card => card.CardId == "S01-02M2");
        Assert.Equal(osiris.InstanceId, restored.InstanceId);
        Assert.Contains(GraveyardProjection(game), card => card.GetProperty("instanceId").GetString() == osiris.InstanceId);
    }

    private static JsonElement.ArrayEnumerator GraveyardProjection(L12GameEngine game)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return snapshot.GetProperty("players")[0].GetProperty("graveyard").EnumerateArray();
    }

    private static L12GameEngine Create(int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var isisDeck = new L12PresetDeckDefinition
        {
            Name = "伊西斯开局规则测试",
            MasterId = "S01-02M1",
            CardIds = [.. baseDeck.CardIds.Where(cardId => cardId != "S01-02M2")],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        return new L12GameEngine(Catalog, "isis-setup", "ISIS-SETUP", seed,
            ["甲", "乙"], [isisDeck, baseDeck], skipPreparation: true, stateFormatVersion: 2);
    }
}
