using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MoraleResourceIdentityLifecycleTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData("S01-00C1")]
    [InlineData("S02-05C1")]
    [InlineData("S02-05C1A")]
    [L12AbilityEvidence("S01-00C1:ability:static:db1ae0a9efb4bff8",
        "normal", "reconnect", "presentation-consumers", "counts-as-morale-structural", "authoritative-consumer")]
    [L12AbilityEvidence("S02-05C1:ability:static:5879d4c3fe97b3cf",
        "normal", "reconnect", "presentation-consumers", "counts-as-morale-structural", "authoritative-consumer")]
    [L12AbilityEvidence("S02-05C1A:ability:static:5879d4c3fe97b3cf",
        "normal", "reconnect", "presentation-consumers", "counts-as-morale-structural", "authoritative-consumer")]
    public void PrintedResourceIdentitySurvivesProjectionRecoveryAndSharedPayment(string cardId)
    {
        var game = Create();
        var player = game.State.Players[0];
        var resource = new L12MoraleCard { CardId = cardId, InstanceId = $"resource-{cardId}" };
        player.Morale.Add(resource);

        Assert.Equal(1, ActiveResourceCount(game, player));
        Assert.True(CanConsumeSelectedResources(game, player, resource.InstanceId));
        AssertProjectedAsSpendableMorale(game, resource);

        var serialized = game.SerializeFullState();
        game = L12GameEngine.RestoreCheckpoint(Catalog, serialized, game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        player = game.State.Players[0];
        resource = Assert.Single(player.Morale);
        Assert.Equal(1, ActiveResourceCount(game, player));
        Assert.True(CanConsumeSelectedResources(game, player, resource.InstanceId));
        AssertProjectedAsSpendableMorale(game, resource);

        Assert.True(TryConsumeMorale(game, player));
        Assert.True(resource.Tapped);
        Assert.Equal(0, ActiveResourceCount(game, player));
        Assert.False(CanConsumeSelectedResources(game, player, resource.InstanceId));
    }

    private static L12GameEngine Create()
    {
        var game = new L12GameEngine(Catalog, "morale-resource-identity", "MORALE", 74532,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.Phase = L12Phase.Main;
        game.State.ActivePlayer = 0;
        foreach (var player in game.State.Players)
        {
            player.Morale.Clear();
            player.TemporaryMorale = 0;
            foreach (var row in player.Field) Array.Clear(row);
        }
        return game;
    }

    private static int ActiveResourceCount(L12GameEngine game, L12PlayerState player)
        => Assert.IsType<int>(Call(game, "ActiveResourceCount", player));

    private static bool CanConsumeSelectedResources(L12GameEngine game, L12PlayerState player, string id)
        => Assert.IsType<bool>(Call(game, "CanConsumeSelectedResources", player, 1, new[] { id }, null, 0));

    private static bool TryConsumeMorale(L12GameEngine game, L12PlayerState player)
        => Assert.IsType<bool>(Call(game, "TryConsumeMorale", player, 1, false, true));

    private static object? Call(L12GameEngine game, string method, params object?[] args)
        => typeof(L12GameEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, args);

    private static void AssertProjectedAsSpendableMorale(L12GameEngine game, L12MoraleCard resource)
    {
        var player = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .GetProperty("players")[0];
        Assert.Equal(1, player.GetProperty("spendableResourceCount").GetInt32());
        var projected = Assert.Single(player.GetProperty("morale").EnumerateArray(), item =>
            item.GetProperty("instanceId").GetString() == resource.InstanceId);
        Assert.Equal(resource.CardId, projected.GetProperty("cardId").GetString());
        Assert.False(projected.GetProperty("tapped").GetBoolean());
    }
}
