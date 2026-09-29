using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AttackRestTransactionTests
{
    private static L12GameEngine Create(int seed)
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var game = new L12GameEngine(catalog, "attack-rest-transaction", "ATTACK-REST", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: true,
            concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
        }
        return game;
    }

    private static L12CardInstance Legion(string instanceId, int troops = 3000)
        => new()
        {
            InstanceId = instanceId,
            CardId = $"test-{instanceId}",
            Name = instanceId,
            CardType = "legion",
            Faction = "universal",
            Cost = 1,
            BaseTroops = troops,
            Troops = troops,
            SummonRound = -1,
        };

    private static (L12GameEngine Game, L12CardInstance Attacker) RunThunderAttack(bool expectStopped)
    {
        for (var seed = 1; seed <= 100; seed++)
        {
            var game = Create(seed);
            var attacker = Legion($"thunder-attacker-{seed}");
            game.State.Players[0].Field[0][0] = attacker;
            game.State.ActiveDisaster = new L12CardInstance
            {
                InstanceId = $"thunder-disaster-{seed}", CardId = "S01-DS04", Name = "雷霆天怒",
                CardType = "disaster", Faction = "disaster",
            };

            var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
                Target: new L12AttackTarget("master")));
            Assert.True(result.Accepted, result.Error);
            var stopped = game.State.Events.All(entry => entry.Type != "attack");
            if (stopped == expectStopped) return (game, attacker);
        }

        throw new InvalidOperationException($"Unable to find a deterministic thunder outcome: stopped={expectStopped}");
    }

    [Fact]
    public void NormalAttackCommitsRestAndAttackCountOnceBeforeItsPublicEvent()
    {
        var game = Create(93001);
        var attacker = Legion("normal-attacker");
        game.State.Players[0].Field[0][0] = attacker;

        var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.True(result.Accepted, result.Error);
        Assert.True(attacker.Tapped);
        Assert.Equal(1, attacker.AttacksThisTurn);
        var attack = Assert.Single(game.State.Events, entry => entry.Type == "attack");
        var snapshot = Assert.Single(attack.Cards, card => card.InstanceId == attacker.InstanceId);
        Assert.True(snapshot.Tapped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThunderOutcomeObservesOneCommittedRestBeforeEveryAttackTimeEvent(bool stopped)
    {
        var (game, attacker) = RunThunderAttack(stopped);

        Assert.True(attacker.Tapped);
        Assert.Equal(1, attacker.AttacksThisTurn);
        var attackTimeEvents = game.State.Events.Where(entry => entry.Type is "dice" or "attack" or "attack-ended").ToArray();
        Assert.NotEmpty(attackTimeEvents);
        Assert.All(attackTimeEvents, entry =>
        {
            var snapshot = Assert.Single(entry.Cards, card => card.InstanceId == attacker.InstanceId);
            Assert.True(snapshot.Tapped);
        });
        Assert.Equal(stopped, game.State.PendingDefense is null);
    }

    [Fact]
    public void RejectedAttackNeverCrossesTheRestCommitBoundary()
    {
        var game = Create(93002);
        var attacker = Legion("rejected-attacker");
        game.State.Players[0].Field[0][0] = attacker;

        var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", "missing-target")));

        Assert.False(result.Accepted);
        Assert.False(attacker.Tapped);
        Assert.Equal(0, attacker.AttacksThisTurn);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type is "attack" or "dice" or "attack-ended");
    }
}
