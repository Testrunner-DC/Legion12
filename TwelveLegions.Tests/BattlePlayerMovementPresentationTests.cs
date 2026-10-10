using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BattlePlayerMovementPresentationTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Game()
    {
        var game = new L12GameEngine(Catalog, "movement-log", "MOVEMENT", 93001,
            ["甲", "乙"], [0, 0], skipPreparation: true, stateFormatVersion: 2);
        game.State.Phase = L12Phase.Main;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        foreach (var player in game.State.Players)
        foreach (var row in player.Field) Array.Clear(row);
        return game;
    }

    private static L12CardInstance Card(string id, bool hidden = false) => new()
    {
        InstanceId = id, CardId = "S01-0001", Name = "同名军团", CardType = "legion",
        Faction = "universal", Cost = 1, BaseTroops = 3000, Troops = 3000,
        SummonRound = -1, Hidden = hidden,
    };

    private static L12PlayerBattlefieldMovementFact Fact(string id, int? player = 0,
        int? fromRow = 0, int? fromSlot = 0, int? toRow = 1, int? toSlot = 2)
        => new(id, player, fromRow, fromSlot, toRow, toSlot);

    [Fact]
    public void OrdinaryMovePublishesHistoricalEndpointsToEveryPublicRecipientAndCheckpoint()
    {
        var game = Game();
        var mover = Card("ordinary-mover");
        game.State.Players[0].Field[0][0] = mover;
        var morale = game.State.Players[0].MoraleDeck[0];
        game.State.Players[0].MoraleDeck.RemoveAt(0);
        morale.Tapped = false;
        game.State.Players[0].Morale.Add(morale);

        var result = game.Handle(0, new L12Command("move", mover.InstanceId, Row: 1, Slot: 0));

        Assert.True(result.Accepted, result.Error);
        var original = Assert.Single(game.State.Events, item => item.Type == "move");
        var expected = Fact(mover.InstanceId, toSlot: 0);
        Assert.Equal(expected, Assert.Single(original.PlayerBattlefieldMovement!.Facts!));
        foreach (var snapshot in new[] { game.SnapshotFor(0), game.SnapshotFor(1), game.SnapshotForSpectator() })
        {
            var projected = Assert.Single(snapshot.RecentEvents, item => item.Type == "move");
            Assert.Equal(expected, Assert.Single(projected.PlayerBattlefieldMovement!.Facts!));
        }
        var checkpoint = game.SerializeFullState();
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence);
        Assert.Equal(expected, Assert.Single(
            Assert.Single(restored.SnapshotFor(0).RecentEvents, item => item.Type == "move")
                .PlayerBattlefieldMovement!.Facts!));
    }

    [Fact]
    public void CavalryMoveRetainsPresentationSceneAndStructuredEndpoints()
    {
        var game = Game();
        var definition = Catalog.Cards["ST01-01"];
        var cavalry = new L12CardInstance
        {
            InstanceId = "cavalry-mover", CardId = definition.Id, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction,
            Profession = definition.Profession, EffectiveProfession = definition.Profession,
            Traits = [.. definition.Traits], Cost = definition.Cost ?? 0,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            SummonRound = -1,
        };
        game.State.Players[0].Field[0][0] = cavalry;
        var result = game.Handle(0, new L12Command("cavalryMove", cavalry.InstanceId, Row: 1, Slot: 2));
        Assert.True(result.Accepted, result.Error);
        var movement = Assert.Single(game.State.Events, item => item.Type == "move");
        Assert.NotNull(movement.EffectSceneId);
        Assert.Equal(Fact(cavalry.InstanceId), Assert.Single(movement.PlayerBattlefieldMovement!.Facts!));
    }

    [Fact]
    public void ProjectionRejectsUnlinkedHiddenInvalidAndNonBattlefieldFacts()
    {
        var game = Game();
        var visible = Card("visible");
        var hidden = Card("hidden", hidden: true);
        var raw = new L12ActionEvent(1, "move", 0, "位置不可从文本推断", [visible, hidden])
        {
            PlayerBattlefieldMovement = new([
                Fact("visible"), Fact("hidden"), Fact("missing"),
                Fact("visible", player: 2), Fact("visible", fromSlot: 3),
                Fact("visible", toRow: 0, toSlot: 0),
            ]),
        };
        var safe = L12RecipientVisibility.ProjectActionEvent(game.State, raw, 1, false);
        Assert.Equal(Fact("visible"), Assert.Single(safe.PlayerBattlefieldMovement!.Facts!));
        var wrongType = L12RecipientVisibility.ProjectActionEvent(game.State,
            raw with { Type = "put" }, 1, false);
        Assert.Null(wrongType.PlayerBattlefieldMovement);
        var noPublicCard = L12RecipientVisibility.ProjectActionEvent(game.State,
            raw with { Cards = [hidden] }, 1, false);
        Assert.Null(noPublicCard.PlayerBattlefieldMovement);
    }

    [Fact]
    public void LegacyEventWithoutMovementFieldKeepsItsMeaningAfterJsonRoundTrip()
    {
        var legacy = new L12ActionEvent(1, "move", 0, "旧记录", [Card("old")]);
        var restored = JsonSerializer.Deserialize<L12ActionEvent>(JsonSerializer.Serialize(legacy));
        Assert.NotNull(restored);
        Assert.Null(restored.PlayerBattlefieldMovement);
    }
}
