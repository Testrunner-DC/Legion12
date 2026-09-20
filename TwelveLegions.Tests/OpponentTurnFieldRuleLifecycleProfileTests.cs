using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class OpponentTurnFieldRuleLifecycleProfileTests
{
    private const string AbilityId = "S01-0212:ability:static:2f33fb3652e7bd28";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence(AbilityId, "exact-card-family", "cost-and-troops-same-definition")]
    public void OpponentTurnFieldRuleIsAClosedStructuredFamily()
    {
        Assert.Equal(AbilityId, EffectLifecycleProfiles.OpponentTurnFieldRuleAbilityId);
        var rule = Assert.IsType<L12OpponentTurnFieldRule>(
            L12StructuredCardSemantics.OpponentTurnFieldRule("S01-0212"));
        Assert.Equal(1, rule.CostAdjustment);
        Assert.Equal(1000, rule.FrontRowTroopsBonus);
        Assert.Equal(rule.CostAdjustment, L12StructuredCardRules.OpponentTurnCostModifier("S01-0212"));
        Assert.Equal(rule.FrontRowTroopsBonus, L12StructuredCardRules.OpponentTurnFrontTroopsBonus("S01-0212"));
        Assert.Null(L12StructuredCardSemantics.OpponentTurnFieldRule("S01-0003"));
        Assert.Equal(0, L12StructuredCardRules.OpponentTurnCostModifier("S01-0003"));
    }

    [Theory]
    [L12AbilityEvidence(AbilityId, "opponent-turn", "controller-turn", "front-row", "back-row",
        "reconnect-idempotence")]
    [InlineData("S01-0212", 1, 0, 1, 1000)]
    [InlineData("S01-0212", 1, 1, 1, 0)]
    [InlineData("S01-0212", 0, 0, 0, 0)]
    [InlineData("S01-0212", 0, 1, 0, 0)]
    public void RuntimeRecalculatesCostAndTroopsFromCurrentTurnAndRow(
        string cardId, int activePlayer, int row, int expectedCostAdjustment, int expectedTroopsBonus)
    {
        var game = Create(72200 + activePlayer * 10 + row);
        var player = game.State.Players[0];
        var guard = Card(cardId, $"guard-{activePlayer}-{row}");
        player.Field[row][0] = guard;
        game.State.ActivePlayer = activePlayer;

        game.SnapshotFor(0);
        var firstCost = guard.CurrentCost;
        var firstTroops = guard.Troops;
        game.SnapshotFor(1);

        Assert.Equal(guard.Cost + expectedCostAdjustment, firstCost);
        Assert.Equal(guard.BaseTroops + expectedTroopsBonus, firstTroops);
        Assert.Equal(firstCost, guard.CurrentCost);
        Assert.Equal(firstTroops, guard.Troops);
    }

    [Fact]
    [L12AbilityEvidence(AbilityId, "leave-reset")]
    public void LeavingTheFieldClearsBothDerivedValues()
    {
        var game = Create(72220);
        var player = game.State.Players[0];
        var guard = Card("S01-0212", "leaving-guard");
        player.Field[0][0] = guard;
        game.State.ActivePlayer = 1;
        game.SnapshotFor(0);
        Assert.Equal(guard.Cost + 1, guard.CurrentCost);
        Assert.Equal(guard.BaseTroops + 1000, guard.Troops);

        var moved = Assert.IsType<bool>(Invoke(game, "MoveFieldCardToZone",
            player, guard, "removed", "被测试效果移除", true));

        Assert.True(moved);
        Assert.Contains(guard, player.Graveyard);
        Assert.Equal(0, guard.ContinuousCostModifier);
        Assert.Equal(guard.BaseTroops, guard.Troops);
    }

    [Fact]
    [L12AbilityEvidence(AbilityId, "current-controller")]
    public void OpponentTurnUsesCurrentControllerRatherThanPrintedOwner()
    {
        var game = Create(72221);
        var controller = game.State.Players[1];
        var guard = Card("S01-0212", "controlled-guard");
        guard.OwnerIndex = 0;
        controller.Field[0][0] = guard;

        game.State.ActivePlayer = 1;
        game.SnapshotFor(0);
        Assert.Equal(guard.Cost, guard.CurrentCost);
        Assert.Equal(guard.BaseTroops, guard.Troops);

        game.State.ActivePlayer = 0;
        game.SnapshotFor(0);
        Assert.Equal(guard.Cost + 1, guard.CurrentCost);
        Assert.Equal(guard.BaseTroops + 1000, guard.Troops);
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "opponent-turn-field-rule", "OPPONENT-FIELD", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 6;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Removed.Clear();
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
            HasPrintedCost = definition.Cost is not null,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
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
