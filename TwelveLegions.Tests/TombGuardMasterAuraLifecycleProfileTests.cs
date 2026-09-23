using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TombGuardMasterAuraLifecycleProfileTests
{
    private const string AbilityId = "S01-02D1:ability:static:d1339da6822c9ae1";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence(AbilityId, "normal", "field-only", "cost-and-troops-same-definition",
        "current-cost-consumers", "presentation-consumers", "reconnect", "reconnect-derived-state")]
    public void GodsLandContinuouslyAddsOneCostAndOneThousandTroopsToFieldTombGuards()
    {
        var game = Create("S01-02D1", 92810);
        var owner = game.State.Players[0];
        var front = Card("S01-0212", "gods-land-front");
        var back = Card("S01-0212", "gods-land-back");
        var hand = Card("S01-0212", "gods-land-hand");
        owner.Field[0][0] = front;
        owner.Field[1][0] = back;
        owner.Hand.Add(hand);

        AssertProjection(game, front.InstanceId, front.Cost + 1, front.BaseTroops + 1000);
        AssertProjection(game, back.InstanceId, back.Cost + 1, back.BaseTroops + 1000);
        Assert.Equal(hand.Cost, hand.CurrentCost);
        Assert.False(L12StructuredCardRules.CurrentCostAtMost(front, front.Cost));
        Assert.True(L12StructuredCardRules.CurrentCostAtMost(front, front.Cost + 1));

        game = Restore(game);
        front = game.State.Players[0].Field[0][0]!;
        back = game.State.Players[0].Field[1][0]!;
        AssertProjection(game, front.InstanceId, front.Cost + 1, front.BaseTroops + 1000);
        AssertProjection(game, back.InstanceId, back.Cost + 1, back.BaseTroops + 1000);

        Assert.True(Assert.IsType<bool>(Invoke(game, "MoveFieldCardToZone",
            game.State.Players[0], front, "graveyard", "离开战场", false)));
        Assert.Equal(0, front.ContinuousCostModifier);
        Assert.Equal(front.BaseTroops, front.Troops);
    }

    [Fact]
    [L12AbilityEvidence(AbilityId, "current-controller", "non-target-unaffected")]
    public void AuraUsesTheCurrentBattlefieldControllerAndExactCardIdentity()
    {
        var ordinary = Create("S01-02D1", 92811);
        var owner = ordinary.State.Players[0];
        var other = Card("S01-0003", "gods-land-other");
        owner.Field[0][0] = other;
        AssertProjection(ordinary, other.InstanceId, other.Cost, other.BaseTroops);

        var noAura = Create("S01-02M1", 92812);
        var guard = Card("S01-0212", "no-gods-land-guard");
        noAura.State.Players[0].Field[0][0] = guard;
        noAura.State.ActivePlayer = 0;
        AssertProjection(noAura, guard.InstanceId, guard.Cost, guard.BaseTroops);
    }

    private static void AssertProjection(L12GameEngine game, string instanceId, int cost, int troops)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var card = snapshot.GetProperty("players")[0].GetProperty("field")
            .EnumerateArray().SelectMany(row => row.EnumerateArray())
            .Single(item => item.ValueKind != JsonValueKind.Null
                && item.GetProperty("instanceId").GetString() == instanceId);
        Assert.Equal(cost, card.GetProperty("currentCost").GetInt32());
        Assert.Equal(troops, card.GetProperty("troops").GetInt32());
    }

    private static L12GameEngine Create(string masterId, int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "众神之乡持续规则",
            MasterId = masterId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "gods-land-aura", "GODS-LAND", seed,
            ["甲", "乙"], [deck, baseDeck], skipPreparation: true, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
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
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = 0,
        };
    }

    private static object? Invoke(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
        return method.Invoke(target, arguments);
    }
}
