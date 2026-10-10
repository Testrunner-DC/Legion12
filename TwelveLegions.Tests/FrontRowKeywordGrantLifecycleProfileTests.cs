using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class FrontRowKeywordGrantLifecycleProfileTests
{
    private const string AbilityId = "S02-0512:ability:static:e44e97f2fb745816";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence(AbilityId, "normal", "target-invalidated", "reconnect", "presentation-consumers")]
    public void AeneasTauntAlwaysFollowsItsCurrentRowAcrossSnapshotAndRestore()
    {
        var game = Create(72301);
        var player = game.State.Players[0];
        var aeneas = Card("S02-0512", "aeneas-front-row");
        player.Field[0][0] = aeneas;

        Assert.True(L12StructuredCardRules.HasTaunt(aeneas, 0));
        Assert.Equal(["挑衅"], ActiveKeywords(game, 0, 0));

        player.Field[0][0] = null;
        player.Field[1][0] = aeneas;

        Assert.False(L12StructuredCardRules.HasTaunt(aeneas, 1));
        Assert.Empty(ActiveKeywords(game, 1, 0));

        game = Restore(game);
        var restored = Assert.IsType<L12CardInstance>(game.State.Players[0].Field[1][0]);
        Assert.False(L12StructuredCardRules.HasTaunt(restored, 1));
        Assert.Empty(ActiveKeywords(game, 1, 0));

        game.State.Players[0].Field[1][0] = null;
        game.State.Players[0].Field[0][0] = restored;
        Assert.True(L12StructuredCardRules.HasTaunt(restored, 0));
        Assert.Equal(["挑衅"], ActiveKeywords(game, 0, 0));
    }

    private static string[] ActiveKeywords(L12GameEngine game, int row, int slot)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return snapshot.GetProperty("players")[0].GetProperty("field")[row][slot]
            .GetProperty("activeKeywords").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "front-row-keyword-grant", "FRONT-KEYWORD", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 6;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Removed.Clear();
        }
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog,
            game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,"),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

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
            HasPrintedCost = definition.Cost is not null,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = 0,
        };
    }
}
