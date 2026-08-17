using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class LatestBugRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 6401)
        => new(Catalog, "latest-regression", "LATEST", seed, ["甲", "乙"], [0, 0], skipPreparation: true);

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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
        };
    }

    private static L12CardInstance[] SnapshotHand(L12GameEngine game, int playerIndex)
    {
        var view = game.SnapshotFor(playerIndex).Players[playerIndex];
        return Assert.IsType<L12CardInstance[]>(view.GetType().GetProperty("hand")!.GetValue(view));
    }

    private static void AddReadyMorale(L12PlayerState player, int count)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"regression-morale-{player.PlayerIndex}-{index}",
                CardId = "S01-01C1",
                Tapped = false,
            });
    }

    [Fact]
    public void RagashaOpponentTurnBonusDoesNotStackWhenMercenaryBlocksTheAttack()
    {
        var game = Create();
        var attacker = Card("S01-0103", "ragasha-attacker");
        var ragasha = Card("S01-0312", "ragasha-defender");
        var mercenary = Card("S01-0002", "ragasha-mercenary");
        attacker.SummonRound = ragasha.SummonRound = 0;
        game.State.Players[0].Hand.Clear();
        game.State.Players[1].Hand.Clear();
        game.State.Players[1].Hand.Add(mercenary);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = ragasha;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        game.SnapshotFor(0);
        var expectedTroops = ragasha.BaseTroops + 1000;
        Assert.Equal(expectedTroops, ragasha.Troops);

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", ragasha.InstanceId))).Accepted);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(mercenary.InstanceId, response.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: response.PromptId,
            Choice: mercenary.InstanceId)).Accepted);

        Assert.Same(ragasha, game.State.Players[1].Field[0][0]);
        Assert.Equal(expectedTroops, ragasha.Troops);
        Assert.Contains(mercenary, game.State.Players[1].Graveyard);
    }

    [Fact]
    public void BloodAxeErikAdvertisesItsFourMoraleSelfDamagePlayCost()
    {
        var game = Create(6402);
        var player = game.State.Players[0];
        var erik = Card("S01-0308", "discounted-erik");
        player.Hand.Clear();
        player.Hand.Add(erik);
        AddReadyMorale(player, 4);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var snapshot = Assert.Single(SnapshotHand(game, 0));
        Assert.Equal(4, snapshot.PlayCost);
        Assert.True(game.Handle(0, new L12Command("playCard", erik.InstanceId, Row: 0, Slot: 0)).Accepted);
        Assert.Equal("play-cost-choice", Assert.Single(game.State.PendingPrompts).Continuation);
    }

    [Fact]
    public void CanopicJarFreeTacticOpportunityAdvertisesZeroUntilConsumed()
    {
        var game = Create(6403);
        var player = game.State.Players[0];
        var tactic = Card("S01-0015", "free-tactic");
        player.Hand.Clear();
        player.Hand.Add(tactic);
        player.FreeTacticCount = 1;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.Equal(0, Assert.Single(SnapshotHand(game, 0)).PlayCost);
        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        Assert.Equal(0, player.FreeTacticCount);
    }

    [Fact]
    public void DefeatedLegionLeavesImmediatelyAndResetsAllTransientFieldState()
    {
        var game = Create(6404);
        var attacker = Card("S01-0001", "reset-attacker");
        var target = Card("S01-0102", "reset-target");
        attacker.SummonRound = target.SummonRound = 0;
        attacker.Troops = 6000;
        target.Troops = 1000;
        target.Tapped = true;
        target.HasCharge = true;
        target.HasStrongAttack = true;
        target.HasSureHit = true;
        target.CostModifier = -2;
        target.TimedModifiers.Add(new L12TimedModifier { TroopsDelta = -1000, ExpiresAfterTurn = 99, Source = "test" });
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = target;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);

        Assert.Null(game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Null(game.State.Players[1].Field[0][0]);
        Assert.Contains(target, game.State.Players[1].Graveyard);
        Assert.Equal(target.BaseTroops, target.Troops);
        Assert.False(target.Tapped);
        Assert.False(target.HasCharge);
        Assert.False(target.HasStrongAttack);
        Assert.False(target.HasSureHit);
        Assert.Equal(0, target.CostModifier);
        Assert.Empty(target.TimedModifiers);
        Assert.Contains(game.State.Events, entry => entry.Type == "support-skipped");
    }
}
