using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RestedFreeMoveLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(
        Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence(EffectLifecycleProfiles.RestedFreeFrontBackMoveAbilityId,
        "normal", "reconnect", "presentation-consumers")]
    public void RestedHippolytaKeepsTheSharedFreeMoveRuleAfterReconnect()
    {
        var game = Create();
        var player = game.State.Players[0];
        var hippolyta = Card("S02-0510", "free-move-hippolyta");
        var mover = Card("S02-0502", "free-move-legion");
        hippolyta.Tapped = true;
        player.Field[0][0] = mover;
        player.Field[0][2] = hippolyta;
        Assert.Empty(player.Morale);

        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var sourceView = snapshot.GetProperty("players")[0].GetProperty("field")[0][2];
        Assert.Contains("前后位移无需消耗费用",
            sourceView.GetProperty("effectText").GetString(), StringComparison.Ordinal);

        var moved = game.Handle(0, new L12Command("move", mover.InstanceId, Row: 1, Slot: 0));

        Assert.True(moved.Accepted, moved.Error);
        Assert.Equal(mover.InstanceId, game.State.Players[0].Field[1][0]?.InstanceId);
        Assert.Empty(game.State.Players[0].Morale);
    }

    private static L12GameEngine Create()
    {
        var game = new L12GameEngine(Catalog, "rested-free-move", "FREE-MOVE", 92670,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            disasterMode: "none", stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Morale.Clear();
        }
        return game;
    }

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
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            SummonRound = -1,
        };
    }
}
