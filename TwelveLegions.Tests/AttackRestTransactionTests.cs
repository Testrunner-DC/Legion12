using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AttackRestTransactionTests
{
    private static L12GameEngine Create(int seed, int stateFormatVersion = 2)
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var game = new L12GameEngine(catalog, "attack-rest-transaction", "ATTACK-REST", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: true,
            concealHiddenResponseAvailability: false, stateFormatVersion: stateFormatVersion);
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

    private static L12CardInstance Legion(string instanceId, int troops = 3000, int disasterLevel = 0)
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
            DisasterLevel = disasterLevel,
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
        Assert.Equal(new L12PlayerCardStateTransition(attacker.InstanceId, false, true),
            attack.PlayerCardStateTransition);
        Assert.Equal(attack.PlayerCardStateTransition, Assert.Single(game.UnpersistedEvents,
            entry => entry.Sequence == attack.Sequence).PlayerCardStateTransition);
        foreach (var recipientSnapshot in new[]
                 {
                     game.SnapshotFor(0), game.SnapshotFor(1),
                     game.SnapshotForSpectator(), game.SnapshotForReferee(),
                 })
            Assert.Equal(attack.PlayerCardStateTransition, Assert.Single(recipientSnapshot.RecentEvents,
                entry => entry.Sequence == attack.Sequence).PlayerCardStateTransition);
        Assert.DoesNotContain(game.State.Events.Where(entry => entry.Sequence != attack.Sequence),
            entry => entry.PlayerCardStateTransition is not null);
    }

    [Fact]
    public void LegionTargetAttackBindsOnePublicRestFactToTheSameAttackSequence()
    {
        var game = Create(93007);
        var attacker = Legion("legion-target-attacker");
        var defender = Legion("legion-target-defender", troops: 2000);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = defender;

        var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", defender.InstanceId)));

        Assert.True(result.Accepted, result.Error);
        var attack = Assert.Single(game.State.Events, entry => entry.Type == "attack");
        Assert.Equal([attacker.InstanceId, defender.InstanceId],
            attack.Cards.Select(card => card.InstanceId).ToArray());
        Assert.Equal(new L12PlayerCardStateTransition(attacker.InstanceId, false, true),
            attack.PlayerCardStateTransition);
        Assert.Equal(attack.PlayerCardStateTransition, Assert.Single(game.UnpersistedEvents,
            entry => entry.Sequence == attack.Sequence).PlayerCardStateTransition);
        foreach (var snapshot in new[]
                 {
                     game.SnapshotFor(0), game.SnapshotFor(1),
                     game.SnapshotForSpectator(), game.SnapshotForReferee(),
                 })
        {
            var projected = Assert.Single(snapshot.RecentEvents,
                entry => entry.Sequence == attack.Sequence);
            Assert.Equal("attack", projected.Type);
            Assert.Equal(attack.PlayerCardStateTransition, projected.PlayerCardStateTransition);
        }
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
        var owner = Assert.Single(game.State.Events,
            entry => entry.Type == (stopped ? "attack-ended" : "attack"));
        Assert.Equal(new L12PlayerCardStateTransition(attacker.InstanceId, false, true),
            owner.PlayerCardStateTransition);
        if (game.State.LastAction?.Sequence == owner.Sequence)
            Assert.Equal(owner.PlayerCardStateTransition, game.State.LastAction.PlayerCardStateTransition);
        Assert.Equal(owner.PlayerCardStateTransition, Assert.Single(game.UnpersistedEvents,
            entry => entry.Sequence == owner.Sequence).PlayerCardStateTransition);
        Assert.All(game.State.Events.Where(entry => entry.Type == "dice"),
            entry => Assert.Null(entry.PlayerCardStateTransition));
        Assert.DoesNotContain(game.State.Events.Where(entry => entry.Sequence != owner.Sequence),
            entry => entry.PlayerCardStateTransition is not null);
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
        Assert.DoesNotContain(game.State.Events, entry => entry.PlayerCardStateTransition is not null);
    }

    [Fact]
    public void CancelledAttackCostSelectionNeverCommitsRestOrStateFact()
    {
        var game = Create(93003);
        var attacker = Legion("cancelled-cost-attacker", disasterLevel: 1);
        var cost = Legion("cancelled-cost-card");
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[0].Hand.Add(cost);
        game.State.ActiveDisaster = new L12CardInstance
        {
            InstanceId = "lust-disaster", CardId = "ST-DS02", Name = "色欲之罪",
            CardType = "disaster", Faction = "disaster",
        };

        var begin = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.True(begin.Accepted, begin.Error);
        Assert.False(attacker.Tapped);
        var prompt = Assert.Single(game.State.PendingPrompts);
        var cancelChoice = Assert.Single(prompt.ValidChoices,
            choice => choice.Equals("skip", StringComparison.OrdinalIgnoreCase));
        var cancel = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: cancelChoice));
        Assert.True(cancel.Accepted, cancel.Error);
        Assert.False(attacker.Tapped);
        Assert.Equal(0, attacker.AttacksThisTurn);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type is "attack" or "dice" or "attack-ended");
        Assert.DoesNotContain(game.State.Events, entry => entry.PlayerCardStateTransition is not null);
    }

    [Fact]
    public void InvalidatedAttackCostNeverCommitsRestOrStateFact()
    {
        var game = Create(93004);
        var attacker = Legion("invalidated-cost-attacker", disasterLevel: 1);
        var cost = Legion("invalidated-cost-card");
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[0].Hand.Add(cost);
        game.State.ActiveDisaster = new L12CardInstance
        {
            InstanceId = "lust-disaster-invalidated", CardId = "ST-DS02", Name = "色欲之罪",
            CardType = "disaster", Faction = "disaster",
        };

        var begin = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(begin.Accepted, begin.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        game.State.Players[0].Hand.Remove(cost);

        var rejected = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [cost.InstanceId]));

        Assert.True(rejected.Accepted, rejected.Error);
        Assert.False(attacker.Tapped);
        Assert.Equal(0, attacker.AttacksThisTurn);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type is "attack" or "dice" or "attack-ended");
        Assert.DoesNotContain(game.State.Events, entry => entry.PlayerCardStateTransition is not null);
    }

    [Fact]
    public void LegacyProtocolAttackKeepsItsOriginalEventShape()
    {
        var game = Create(93005, stateFormatVersion: 0);
        var attacker = Legion("legacy-attacker");
        game.State.Players[0].Field[0][0] = attacker;

        var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.True(result.Accepted, result.Error);
        Assert.False(game.State.PresentationFactProtocolEnabled);
        Assert.Null(Assert.Single(game.State.Events, entry => entry.Type == "attack")
            .PlayerCardStateTransition);
    }

    [Fact]
    public async Task LegacyJournalAttackReplaysToItsExactRecordedStateHashWithoutNewFact()
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var current = Create(93006);
        var attacker = Legion("legacy-journal-attacker");
        current.State.Players[0].Field[0][0] = attacker;
        var legacyState = JsonNode.Parse(current.SerializeFullState())!.AsObject();
        Assert.True(legacyState.Remove(nameof(L12GameState.PresentationFactProtocolEnabled)));
        var legacy = L12GameEngine.RestoreCheckpoint(catalog, legacyState.ToJsonString(),
            current.RandomState!.Value, current.CardFactSignalSequence,
            autoPassEmptyResponses: true, concealHiddenResponseAvailability: false);
        Assert.False(legacy.State.PresentationFactProtocolEnabled);

        var directory = Path.Combine(Path.GetTempPath(), "l12-legacy-attack-rest-journal",
            Guid.NewGuid().ToString("N"));
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        recorder.AttachCatalog(catalog);
        await recorder.StartAsync(legacy, "sandbox");
        var command = new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"));
        var result = legacy.Handle(0, command);
        Assert.True(result.Accepted, result.Error);
        Assert.Null(Assert.Single(legacy.State.Events, entry => entry.Type == "attack")
            .PlayerCardStateTransition);
        await recorder.AppendAsync(legacy, 1, 0, JsonSerializer.Serialize(command), result,
            "legacy-attack-rest-command");
        var expectedHash = legacy.ComputeStateHash();

        var recovered = Assert.IsType<L12JournalRecoveryState>(
            await recorder.LoadJournalEngineAsync(legacy.State.MatchId));
        Assert.False(recovered.Engine.State.PresentationFactProtocolEnabled);
        Assert.Equal(expectedHash, recovered.Engine.ComputeStateHash());
        Assert.Null(Assert.Single(recovered.Engine.State.Events, entry => entry.Type == "attack")
            .PlayerCardStateTransition);
    }
}
