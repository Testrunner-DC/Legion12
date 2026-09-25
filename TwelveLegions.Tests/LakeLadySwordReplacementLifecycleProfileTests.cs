using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class LakeLadySwordReplacementLifecycleProfileTests
{
    private const string StaticAbilityId = "S02-06S3:ability:static:f7e019a543066afd";
    private const string DeathAbilityId = "S02-06S3:ability:death:84330d935c195208";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence(StaticAbilityId, "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence(DeathAbilityId, "normal", "reconnect", "presentation-consumers")]
    public void CompletedTrialAutomaticallyPaysCurrentSwordAndReplacesLethalEffectAcrossRestore()
    {
        var game = Create(72540);
        var player = game.State.Players[0];
        var arthur = Card("S02-0601", "lake-profile-arthur", 0);
        var sword = Card("S02-06S2", "lake-profile-sword", 0);
        var trial = Card("S02-06S3", "lake-profile-trial", 0);
        trial.TrialCompleted = true;
        arthur.AttachedCards.Add(sword);
        player.Field[0][0] = arthur;
        player.SpecialZones.Trials.Add(trial);
        Assert.Contains(StatusEffects(game, arthur.InstanceId), effect =>
            effect.GetProperty("label").GetString()!.Contains("代替承受致命", StringComparison.Ordinal));

        game = Restore(game);
        player = game.State.Players[0];
        arthur = Assert.IsType<L12CardInstance>(player.Field[0][0]);
        sword = Assert.Single(arthur.AttachedCards);
        var result = game.HandleGm(new L12GmCommand("destroyCard", 0, CardInstanceId: arthur.InstanceId));

        Assert.False(result.Accepted);
        Assert.Same(arthur, player.Field[0][0]);
        Assert.Empty(arthur.AttachedCards);
        Assert.Contains(player.Graveyard, card => card.InstanceId == sword.InstanceId);
        Assert.DoesNotContain(player.Graveyard, card => card.InstanceId == arthur.InstanceId);
        var playerLog = Assert.Single(game.State.Events, entry => entry.Type == "replacement"
            && entry.Cards.Any(card => card.InstanceId == arthur.InstanceId)
            && entry.Text.Contains("王者之剑", StringComparison.Ordinal));
        Assert.Equal("触发 致命代替", playerLog.PlayerLogSemantic?.ActionLabel);
        Assert.Equal($"移除〈{sword.Name}〉，〈{arthur.Name}〉未阵亡", playerLog.PlayerLogSemantic?.OutcomeLabel);
        Assert.Equal("lake-profile-trial", playerLog.PlayerLogSemantic?.SourceInstanceId);
        Assert.Equal(arthur.InstanceId, playerLog.PlayerLogSemantic?.TargetInstanceId);

        result = game.HandleGm(new L12GmCommand("destroyCard", 0, CardInstanceId: arthur.InstanceId));
        Assert.True(result.Accepted, result.Error);
        Assert.Null(player.Field[0][0]);
        Assert.Contains(player.Graveyard, card => card.InstanceId == arthur.InstanceId);
        Assert.Single(game.State.Events, entry => entry.Type == "replacement"
            && entry.Cards.Any(card => card.InstanceId == arthur.InstanceId));
    }

    private static JsonElement.ArrayEnumerator StatusEffects(L12GameEngine game, string instanceId)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var card = snapshot.GetProperty("players")[0].GetProperty("field")[0].EnumerateArray()
            .First(item => item.ValueKind != JsonValueKind.Null
                && item.GetProperty("instanceId").GetString() == instanceId);
        return card.GetProperty("statusEffects").EnumerateArray();
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "lake-lady-profile", "LAKE-LADY", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, disasterMode: "none",
            stateFormatVersion: 2);
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Graveyard.Clear();
            player.SpecialZones.Trials.Clear();
        }
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
