using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class GrantedChargeKeywordLifecycleProfileTests
{
    private static readonly string[] AbilityIds =
    [
        "S02-03M1:ability:granted:f4dd24f1fb07f3d5",
        "S02-0403:ability:granted:f4dd24f1fb07f3d5",
        "S02-0405:ability:granted:f4dd24f1fb07f3d5",
        "ST01-01:ability:granted:c502e9ac1489cd1a",
    ];

    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence("S02-03M1:ability:granted:f4dd24f1fb07f3d5", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("S02-0403:ability:granted:f4dd24f1fb07f3d5", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("S02-0405:ability:granted:f4dd24f1fb07f3d5", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("ST01-01:ability:granted:c502e9ac1489cd1a", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    public void GrantedChargeUsesOneCurrentFlagForProjectionAttackAndLeaveResetAcrossRestore()
    {
        foreach (var abilityId in AbilityIds)
        {
            var cardId = abilityId[..abilityId.IndexOf(":ability:", StringComparison.Ordinal)];
            Assert.True(L12StructuredCardRules.HasPrintedKeywordReference(cardId, "charge"));
        }

        var game = Create(72560);
        var legion = Card("S01-0001", "granted-charge-legion", 0);
        legion.HasCharge = true;
        legion.SummonRound = game.State.Round;
        game.State.Players[0].Field[0][0] = legion;
        Assert.Contains("冲锋", ActiveKeywords(game, 0));

        game = Restore(game);
        legion = game.State.Players[0].Field[0][0]!;
        Assert.True(legion.HasCharge);
        Assert.Contains("冲锋", ActiveKeywords(game, 0));
        var result = game.Handle(0, new L12Command("attack", legion.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(result.Accepted, result.Error);

        game = Create(72561);
        legion = Card("S01-0001", "granted-charge-leave", 0);
        legion.HasCharge = true;
        legion.SummonRound = game.State.Round;
        game.State.Players[0].Field[0][0] = legion;
        result = game.HandleGm(new L12GmCommand("returnCardToHand", 0, CardInstanceId: legion.InstanceId));
        Assert.True(result.Accepted, result.Error);
        legion = Assert.Single(game.State.Players[0].Hand, card => card.InstanceId == legion.InstanceId);
        Assert.False(legion.HasCharge);
    }

    private static string[] ActiveKeywords(L12GameEngine game, int row)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return snapshot.GetProperty("players")[0].GetProperty("field")[row][0]
            .GetProperty("activeKeywords").EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "granted-charge", "GRANTED-CHARGE", seed,
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
