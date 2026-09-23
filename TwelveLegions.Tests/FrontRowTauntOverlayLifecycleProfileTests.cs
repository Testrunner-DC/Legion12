using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class FrontRowTauntOverlayLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData("S01-0107", true)]
    [InlineData("S01-0204", false)]
    [InlineData("S01-0312", true)]
    [InlineData("ST01-04", false)]
    [L12AbilityEvidence("S01-0107:ability:static:af427a4637e1c138", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-0204:ability:static:af427a4637e1c138", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-0312:ability:static:af427a4637e1c138", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("ST01-04:ability:static:af427a4637e1c138", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-0107:ability:static:715fe715dcb8ea28", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-0204:ability:static:4108715d77479b32", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-0312:ability:static:b2e1a67373ad69cc", "normal", "reconnect", "presentation-consumers")]
    public void EveryOverlayTauntUsesCurrentRowInRulesAndPublicProjection(string cardId, bool hasOpponentTurnBonus)
    {
        var game = Create(72380 + cardId.Length);
        var card = Card(cardId, $"front-taunt-{cardId}");
        game.State.Players[0].Field[0][0] = card;
        game.State.ActivePlayer = 1;

        Assert.True(L12StructuredCardRules.HasTaunt(card, 0));
        Assert.Equal(["挑衅"], ActiveKeywords(game, 0));
        Assert.Equal(card.BaseTroops + (hasOpponentTurnBonus ? 1000 : 0), ProjectedTroops(game, 0));

        game = Restore(game);
        var restored = game.State.Players[0].Field[0][0]!;
        Assert.True(L12StructuredCardRules.HasTaunt(restored, 0));
        Assert.Equal(["挑衅"], ActiveKeywords(game, 0));
        Assert.Equal(restored.BaseTroops + (hasOpponentTurnBonus ? 1000 : 0), ProjectedTroops(game, 0));

        game.State.Players[0].Field[0][0] = null;
        game.State.Players[0].Field[1][0] = restored;
        game.State.Revision++;
        Assert.False(L12StructuredCardRules.HasTaunt(restored, 1));
        Assert.Empty(ActiveKeywords(game, 1));
        Assert.Equal(restored.BaseTroops, ProjectedTroops(game, 1));
    }

    private static string[] ActiveKeywords(L12GameEngine game, int row)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return snapshot.GetProperty("players")[0].GetProperty("field")[row][0]
            .GetProperty("activeKeywords").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
    }

    private static int ProjectedTroops(L12GameEngine game, int row)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return snapshot.GetProperty("players")[0].GetProperty("field")[row][0]
            .GetProperty("troops").GetInt32();
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "front-row-taunt-overlay", "FRONT-TAUNT", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
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
