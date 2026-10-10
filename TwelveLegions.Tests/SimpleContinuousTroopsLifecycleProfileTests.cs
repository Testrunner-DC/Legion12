using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SimpleContinuousTroopsLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(
        Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData("S01-0203")]
    [InlineData("S02-0516")]
    [InlineData("S02-0519")]
    [InlineData("S02-0523")]
    [L12AbilityEvidence("S01-0203:ability:static:0a317a499dc4420e",
        "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-0516:ability:static:a29458736f52d0a9",
        "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-0519:ability:static:2b21805b14115304",
        "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-0523:ability:static:05da64c53e8a7606",
        "normal", "reconnect", "presentation-consumers")]
    public void ContinuousTroopsRulesRecalculateFromCurrentStateAfterReconnect(string cardId)
    {
        var game = Create();
        var owner = game.State.Players[1];
        var subject = Card(cardId == "S02-0523" ? "S02-0501" : cardId,
            $"continuous-subject-{cardId}");
        owner.Field[0][0] = subject;

        switch (cardId)
        {
            case "S01-0203":
                AssertProjection(game, owner.PlayerIndex, subject.InstanceId, subject.BaseTroops + 1000);
                owner.Field[1][2] = Card("S01-0212", "continuous-tomb-guard");
                AssertProjection(game, owner.PlayerIndex, subject.InstanceId, subject.BaseTroops);
                break;
            case "S02-0516":
                var adjacent = Card("S02-0501", "continuous-hannibal-adjacent");
                owner.Field[0][1] = subject;
                owner.Field[0][0] = adjacent;
                AssertProjection(game, owner.PlayerIndex, adjacent.InstanceId, adjacent.BaseTroops + 1000);
                owner.Field[0][1] = null;
                AssertProjection(game, owner.PlayerIndex, adjacent.InstanceId, adjacent.BaseTroops);
                subject = adjacent;
                break;
            case "S02-0519":
                AssertProjection(game, owner.PlayerIndex, subject.InstanceId, subject.BaseTroops + 2000);
                game.State.ActivePlayer = owner.PlayerIndex;
                AssertProjection(game, owner.PlayerIndex, subject.InstanceId, subject.BaseTroops);
                game.State.ActivePlayer = 0;
                break;
            case "S02-0523":
                var horse = Card("S02-0523", "continuous-trojan-horse");
                horse.OwnerIndex = 0;
                owner.Field[1][2] = horse;
                AssertProjection(game, owner.PlayerIndex, subject.InstanceId,
                    Math.Max(0, subject.BaseTroops - 1000));
                owner.Field[1][2] = null;
                AssertProjection(game, owner.PlayerIndex, subject.InstanceId, subject.BaseTroops);
                owner.Field[1][2] = horse;
                break;
        }

        var beforeRestore = CurrentProjectedTroops(game, owner.PlayerIndex, subject.InstanceId);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        Assert.Equal(beforeRestore,
            CurrentProjectedTroops(game, owner.PlayerIndex, subject.InstanceId));
    }

    private static void AssertProjection(L12GameEngine game, int playerIndex, string instanceId,
        int expectedTroops)
        => Assert.Equal(expectedTroops, CurrentProjectedTroops(game, playerIndex, instanceId));

    private static int CurrentProjectedTroops(L12GameEngine game, int playerIndex, string instanceId)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(playerIndex),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var row in snapshot.GetProperty("players")[playerIndex].GetProperty("field").EnumerateArray())
        foreach (var card in row.EnumerateArray())
            if (card.ValueKind != JsonValueKind.Null
                && card.GetProperty("instanceId").GetString() == instanceId)
                return card.GetProperty("troops").GetInt32();
        throw new Xunit.Sdk.XunitException($"Snapshot is missing {instanceId}");
    }

    private static L12GameEngine Create()
    {
        var game = new L12GameEngine(Catalog, "simple-continuous-troops", "SIMPLE-TROOPS", 92680,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            disasterMode: "none");
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
