using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ImmortalReplacementLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence("S01-0220:ability:death:00f139f8bc316591", "normal", "reconnect", "presentation-consumers", "single-use", "troops-set-to-1000", "authoritative-consumer")]
    [L12AbilityEvidence("S01-0411:ability:death:00f139f8bc316591", "normal", "reconnect", "presentation-consumers", "single-use", "troops-set-to-1000", "authoritative-consumer")]
    public void GrantedImmortalReplacementSurvivesRestoreThenReplacesExactlyOneLethalRemoval()
    {
        foreach (var sourceCardId in new[] { "S01-0220", "S01-0411" })
        {
            var game = Create(72750 + sourceCardId.Length);
            var target = Card("S01-0002", $"immortal-replacement-{sourceCardId}");
            target.ImmortalUses = 1;
            target.ImmortalUntilTurn = int.MaxValue;
            target.ImmortalExpiresAtPlayerTurnStart = 0;
            game.State.Players[0].Field[0][0] = target;
            Assert.Contains("免死", ActiveKeywords(game));

            game = Restore(game);
            target = game.State.Players[0].Field[0][0]!;
            Assert.Contains("免死", ActiveKeywords(game));
            var first = game.HandleGm(new L12GmCommand("destroyCard", 0,
                CardInstanceId: target.InstanceId));
            Assert.False(first.Accepted);
            Assert.Same(target, game.State.Players[0].Field[0][0]);
            Assert.Equal(0, target.ImmortalUses);
            Assert.Equal(1000, target.SetTroopsValue);
            Assert.Equal(1000, target.CurrentTroops);
            Assert.Contains(game.State.Events, entry => entry.Type == "effect"
                && entry.Text.Contains("兵力设定为 1000", StringComparison.Ordinal));

            var second = game.HandleGm(new L12GmCommand("destroyCard", 0,
                CardInstanceId: target.InstanceId));
            Assert.True(second.Accepted, second.Error);
            Assert.Null(game.State.Players[0].Field[0][0]);
            Assert.Contains(target, game.State.Players[0].Graveyard);
        }
    }

    private static string[] ActiveKeywords(L12GameEngine game)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return snapshot.GetProperty("players")[0].GetProperty("field")[0][0]
            .GetProperty("activeKeywords").EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "immortal-replacement-profile", "IMMORTAL-REPLACEMENT", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, disasterMode: "none", stateFormatVersion: 2);
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
        }
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: true, concealHiddenResponseAvailability: false);

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
