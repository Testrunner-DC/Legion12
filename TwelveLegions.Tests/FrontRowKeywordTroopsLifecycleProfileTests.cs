using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class FrontRowKeywordTroopsLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData("S02-0004")]
    [InlineData("S02-0007")]
    [InlineData("S02-0615")]
    [InlineData("ST02-02")]
    [InlineData("ST04-01")]
    [InlineData("ST06-02")]
    [L12AbilityEvidence("S02-0004:ability:continuous:16dc08d7324d1649", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-0007:ability:continuous:58ce6286f39b73ee", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-0615:ability:continuous:16dc08d7324d1649", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("ST02-02:ability:continuous:c51a646e6338a9d1", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("ST04-01:ability:continuous:59dd263106457575", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("ST06-02:ability:continuous:c51a646e6338a9d1", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-0004:ability:granted:be3174252606645e", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("S02-0007:ability:granted:be3174252606645e", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    public void FrontRowTauntAndOpponentTurnTroopsAlwaysUseCurrentState(string cardId)
    {
        var game = Create(72400 + cardId.Length);
        var card = Card(cardId, $"front-keyword-troops-{cardId}");
        var baseTroops = card.BaseTroops;
        game.State.Players[0].Field[0][0] = card;
        game.State.ActivePlayer = 1;

        var front = ProjectedCard(game, 0);
        Assert.True(L12StructuredCardRules.HasTaunt(card, 0));
        Assert.Equal(baseTroops + 1000, front.GetProperty("troops").GetInt32());
        Assert.Contains(front.GetProperty("activeKeywords").EnumerateArray(),
            item => item.GetString() == "挑衅");

        game = Restore(game);
        var restored = game.State.Players[0].Field[0][0]!;
        front = ProjectedCard(game, 0);
        Assert.True(L12StructuredCardRules.HasTaunt(restored, 0));
        Assert.Equal(baseTroops + 1000, front.GetProperty("troops").GetInt32());

        game.State.Players[0].Field[0][0] = null;
        game.State.Players[0].Field[1][0] = restored;
        game.State.Revision++;
        var back = ProjectedCard(game, 1);
        Assert.False(L12StructuredCardRules.HasTaunt(restored, 1));
        Assert.Equal(baseTroops, back.GetProperty("troops").GetInt32());
        Assert.DoesNotContain(back.GetProperty("activeKeywords").EnumerateArray(),
            item => item.GetString() == "挑衅");

        game.State.Players[0].Field[1][0] = null;
        game.State.Players[0].Field[0][0] = restored;
        game.State.ActivePlayer = 0;
        game.State.Revision++;
        Assert.Equal(baseTroops, ProjectedCard(game, 0).GetProperty("troops").GetInt32());
        Assert.True(L12StructuredCardRules.HasTaunt(restored, 0));
    }

    private static JsonElement ProjectedCard(L12GameEngine game, int row)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return snapshot.GetProperty("players")[0].GetProperty("field")[row][0];
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "front-row-keyword-troops", "FRONT-TROOPS", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, stateFormatVersion: 2);
        game.State.Round = 2;
        game.State.TurnSerial = 6;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
        }
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
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
