using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SaladinCompositeLineLifecycleProfileTests
{
    private const string AbilityId = "S01-0206:ability:static:cb39cf42a7feea7b";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence(AbilityId, "normal", "reconnect", "presentation-consumers")]
    public void CompositeLinePublishesAndExecutesSharedCavalryMoveAcrossRestore()
    {
        var game = Create(72550);
        var saladin = Card("S01-0206", "saladin-profile-move", 0);
        saladin.SummonRound = -1;
        game.State.Players[0].Field[0][0] = saladin;
        var action = Assert.Single(FieldCard(game, 0, 0).GetProperty("ruleActions").EnumerateArray());
        Assert.Equal("cavalryMove", action.GetProperty("id").GetString());
        Assert.True(action.GetProperty("enabled").GetBoolean());
        Assert.Contains("1:2", action.GetProperty("targetKeys").EnumerateArray().Select(value => value.GetString()));

        game = Restore(game);
        var result = game.Handle(0, new L12Command("cavalryMove", saladin.InstanceId, Row: 1, Slot: 2));
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(saladin.InstanceId, game.State.Players[0].Field[1][2]!.InstanceId);
        Assert.Single(game.State.Events, entry => entry.Type == "move"
            && entry.Cards.Any(card => card.InstanceId == saladin.InstanceId));

        game = Restore(game);
        var before = game.SerializeFullState();
        result = game.Handle(0, new L12Command("cavalryMove", saladin.InstanceId, Row: 0, Slot: 1));
        Assert.False(result.Accepted);
        Assert.Equal(before, game.SerializeFullState());
    }

    [Fact]
    [L12AbilityEvidence(AbilityId, "normal", "reconnect", "presentation-consumers")]
    public void FrontRowAdjacencyBonusUsesCurrentPositionAndExpiresAfterAttackAcrossRestore()
    {
        var game = Create(72551);
        var attacker = Card("S01-0205", "saladin-profile-attacker", 0);
        var saladin = Card("S01-0206", "saladin-profile-source", 0);
        attacker.SummonRound = saladin.SummonRound = -1;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[0].Field[0][1] = saladin;
        var baseTroops = attacker.Troops;

        game = Restore(game);
        var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(baseTroops + 1000, game.State.Players[0].Field[0][0]!.Troops);
        Assert.Equal(1000, game.State.PendingDefense?.TemporaryAttackerTroopsBonus);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("相邻太阳城军团", StringComparison.Ordinal));
        result = game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: []));
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(baseTroops, game.State.Players[0].Field[0][0]!.Troops);

        game.State.Phase = L12Phase.Main;
        game.State.Players[0].Field[0][0]!.Tapped = false;
        game.State.Players[0].Field[0][1] = null;
        game.State.Players[0].Field[1][1] = saladin;
        result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(baseTroops, game.State.Players[0].Field[0][0]!.Troops);
        Assert.Equal(0, game.State.PendingDefense?.TemporaryAttackerTroopsBonus);
    }

    private static JsonElement FieldCard(L12GameEngine game, int row, int slot)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return snapshot.GetProperty("players")[0].GetProperty("field")[row][slot];
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "saladin-line", "SALADIN-LINE", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, disasterMode: "none",
            stateFormatVersion: 2);
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
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

    private static L12CardInstance Card(string cardId, string instanceId, int ownerIndex)
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
            OwnerIndex = ownerIndex,
        };
    }
}
