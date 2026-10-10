using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed partial class CavalryMoveRuleActionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "cavalry-rule-action", "CAVALRY", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        foreach (var row in player.Field) Array.Clear(row);
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId,
        string? profession = null, string? cardType = null)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = definition.Id, Name = definition.NameZh,
            CardType = cardType ?? definition.CardType, Faction = definition.Faction,
            ImageUrl = definition.ImageUrl, EffectText = definition.Effect,
            Traits = [.. definition.Traits], Profession = profession ?? definition.Profession,
            EffectiveProfession = profession ?? definition.Profession, Cost = definition.Cost ?? 0,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            OwnerIndex = 0, SummonRound = -1,
        };
    }

    private static JsonElement FieldCard(L12GameEngine game, int row, int slot)
        => JsonSerializer.SerializeToElement(game.SnapshotFor(0), Json)
            .GetProperty("players")[0].GetProperty("field")[row][slot];

    [Theory]
    [InlineData("S01-0310", 2)]
    [InlineData("S01-0409", 3)]
    [InlineData("S02-0505", 3)]
    [InlineData("ST01-01", 1)]
    [InlineData("ST06-04", 1)]
    public void PrintedCavalryMoveClausesAreRuleActionsInsteadOfStackEffects(string cardId, int sequence)
    {
        var ability = Catalog.AtomicEffects.Find(cardId)!.Abilities.Single(item => item.Sequence == sequence);

        Assert.Equal("rule-action", ability.ExecutionModel);
        Assert.False(ability.HasLegacyFallback);
        Assert.DoesNotContain(ability.Presentations, scene => scene.EventType == "effect");
        var scene = Assert.Single(ability.Presentations);
        Assert.Equal("rule-action:cavalry-move", scene.Flow);
        Assert.Equal(ability.Text, scene.DefaultText);
    }

    [Fact]
    public void SnapshotProjectsAuthoritativeButtonTextAndEligibility()
    {
        var game = Create(91501);
        var player = game.State.Players[0];
        var cavalry = Card("S01-0310", "cavalry-view");
        player.Field[0][0] = cavalry;

        var action = Assert.Single(FieldCard(game, 0, 0).GetProperty("ruleActions").EnumerateArray());
        Assert.Equal("cavalryMove", action.GetProperty("id").GetString());
        Assert.Equal("骑兵位移", action.GetProperty("label").GetString());
        Assert.Contains("我方回合1次", action.GetProperty("text").GetString());
        Assert.True(action.GetProperty("enabled").GetBoolean());
        Assert.Contains("1:2", action.GetProperty("targetKeys").EnumerateArray()
            .Select(item => item.GetString()));

        player.Field[0][1] = Card("S01-0001", "front-1");
        player.Field[0][2] = Card("S01-0002", "front-2");
        game.State.ActiveDisaster = Card("S01-DS03", "corrupt-land");
        action = Assert.Single(FieldCard(game, 0, 0).GetProperty("ruleActions").EnumerateArray());
        Assert.False(action.GetProperty("enabled").GetBoolean());
        Assert.Contains("没有可位移的前排空位", action.GetProperty("disabledReason").GetString());
        Assert.Empty(action.GetProperty("targetKeys").EnumerateArray());
    }

    [Fact]
    public void CorruptEarthProjectsOnlyFrontRowDestinationsAndCommandUsesTheSameRule()
    {
        var game = Create(91504);
        var player = game.State.Players[0];
        var cavalry = Card("S02-0505", "cavalry-destinations");
        player.Field[0][0] = cavalry;
        game.State.ActiveDisaster = Card("S01-DS03", "corrupt-land-candidates");

        var action = Assert.Single(FieldCard(game, 0, 0).GetProperty("ruleActions").EnumerateArray());
        var targets = action.GetProperty("targetKeys").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
        Assert.Equal(["0:1", "0:2"], targets);
        Assert.DoesNotContain(targets, item => item?.StartsWith("1:", StringComparison.Ordinal) == true);

        var illegal = game.Handle(0,
            new L12Command("cavalryMove", cavalry.InstanceId, Row: 1, Slot: 0));
        Assert.False(illegal.Accepted);
        Assert.Contains("无法位移至后排", illegal.Error);

        var legal = game.Handle(0,
            new L12Command("cavalryMove", cavalry.InstanceId, Row: 0, Slot: 1));
        Assert.True(legal.Accepted, legal.Error);
    }

    [Fact]
    public void SuccessfulMoveUsesOneActionEventForLogMotionReplayAndPresentation()
    {
        var game = Create(91502);
        var cavalry = Card("ST01-01", "cavalry-success");
        game.State.Players[0].Field[0][0] = cavalry;

        var result = game.Handle(0, new L12Command("cavalryMove", cavalry.InstanceId, Row: 1, Slot: 2));

        Assert.True(result.Accepted, result.Error);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        var movement = Assert.Single(game.State.Events, item => item.Type == "move");
        Assert.NotNull(movement.EffectSceneId);
        Assert.Contains("我方 回合1次", movement.EffectText);
        Assert.Equal(cavalry.InstanceId, Assert.Single(movement.Cards).InstanceId);
        Assert.Same(cavalry, game.State.Players[0].Field[1][2]);
    }

    [Fact]
    public void DuplicateMoveAfterCheckpointRestoreIsRejectedWithoutAnotherEvent()
    {
        var game = Create(91503);
        var cavalry = Card("ST06-04", "cavalry-restore");
        game.State.Players[0].Field[0][0] = cavalry;
        Assert.True(game.Handle(0,
            new L12Command("cavalryMove", cavalry.InstanceId, Row: 1, Slot: 2)).Accepted);
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState();
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        var restored = game.State.Players[0].Field[1][2]!;
        var moveEvents = game.State.Events.Count(item => item.Type == "move");

        var duplicate = game.Handle(0,
            new L12Command("cavalryMove", restored.InstanceId, Row: 0, Slot: 1));

        Assert.False(duplicate.Accepted);
        Assert.Contains("本回合已经进行过", duplicate.Error);
        Assert.Equal(moveEvents, game.State.Events.Count(item => item.Type == "move"));
        Assert.Same(restored, game.State.Players[0].Field[1][2]);
    }
}
