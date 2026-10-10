using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class KingsSwordAttachedLifecycleProfileTests
{
    private const string AbilityId = "S02-06S2:ability:static:0f86ac377c8c63ee";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence(AbilityId, "normal", "reconnect", "presentation-consumers")]
    public void AttachedSwordProjectsTroopsAndStrongAttackFromTheSameCurrentRelationship()
    {
        var game = Create(72420);
        var arthur = Card("S02-0601", "king-sword-arthur");
        var sword = Card("S02-06S2", "king-sword-token");
        var baseTroops = arthur.BaseTroops;
        arthur.AttachedCards.Add(sword);
        game.State.Players[0].Field[0][0] = arthur;

        var attached = ProjectedCard(game);
        Assert.Equal(baseTroops + 1000, attached.GetProperty("troops").GetInt32());
        Assert.Contains(attached.GetProperty("activeKeywords").EnumerateArray(),
            item => item.GetString() == "强攻");

        game = Restore(game);
        var restored = game.State.Players[0].Field[0][0]!;
        attached = ProjectedCard(game);
        Assert.Equal(baseTroops + 1000, attached.GetProperty("troops").GetInt32());
        Assert.Contains(attached.GetProperty("activeKeywords").EnumerateArray(),
            item => item.GetString() == "强攻");

        restored.AttachedCards.Clear();
        game.State.Revision++;
        var detached = ProjectedCard(game);
        Assert.Equal(baseTroops, detached.GetProperty("troops").GetInt32());
        Assert.DoesNotContain(detached.GetProperty("activeKeywords").EnumerateArray(),
            item => item.GetString() == "强攻");
    }

    private static JsonElement ProjectedCard(L12GameEngine game)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return snapshot.GetProperty("players")[0].GetProperty("field")[0][0];
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "king-sword-attached", "KING-SWORD", seed,
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
