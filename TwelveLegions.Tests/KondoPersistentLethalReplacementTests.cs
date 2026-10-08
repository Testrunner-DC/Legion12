using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class KondoPersistentLethalReplacementTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [Trait("L12Bug", "BUG-20261007-08cd2642")]
    public void ConsumedMerlinLethalStateSurvivesCommandsProjectionAndV2Restore(
        bool restoreAtPrompt, bool restoreAfterReplacement, bool splitLayersForOrderProbe)
    {
        var setup = KondoPersistentLethalReplacementFixture.BeginMerlinLethal(splitLayersForOrderProbe);
        var game = restoreAtPrompt
            ? KondoPersistentLethalReplacementFixture.Restore(setup.Game)
            : setup.Game;
        KondoPersistentLethalReplacementFixture.Resolve(game, setup.KondoId);

        var okita = KondoPersistentLethalReplacementFixture.FieldCard(game, 1, setup.OkitaId);
        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == setup.KondoId);
        var marker = Assert.Single(
            KondoPersistentLethalReplacementFixture.ProtectionKeys(game, 1, setup.OkitaId));
        Assert.Contains(game.State.Players[1].UsedAbilities,
            key => key.StartsWith($"lethal-substitution:{okita.CardId}:{okita.InstanceId}:",
                StringComparison.Ordinal));

        // A pure cost modifier, projection reads and collection order are not new
        // lethal events and cannot consume this exact protection.
        okita.TimedModifiers.Add(new L12TimedModifier
        {
            TroopsDelta = 0,
            CostDelta = -1,
            ConsumedTroopsBonus = 0,
            ExpiresAfterTurn = game.State.TurnSerial,
            Source = "unrelated-cost-only-modifier",
        });
        _ = game.SnapshotFor(0);
        _ = game.SnapshotFor(1);
        okita.TimedModifiers.Reverse();
        Assert.Equal(marker, Assert.Single(
            KondoPersistentLethalReplacementFixture.ProtectionKeys(game, 1, setup.OkitaId)));

        if (restoreAfterReplacement)
        {
            game = KondoPersistentLethalReplacementFixture.Restore(game);
            okita = KondoPersistentLethalReplacementFixture.FieldCard(game, 1, setup.OkitaId);
            Assert.Equal(marker, Assert.Single(
                KondoPersistentLethalReplacementFixture.ProtectionKeys(game, 1, setup.OkitaId)));
        }

        var probe = game.Handle(0, new L12Command("attack", setup.ProbeAttackerId,
            Target: new L12AttackTarget("master")));
        Assert.True(probe.Accepted, probe.Error);
        Assert.Same(okita,
            KondoPersistentLethalReplacementFixture.FieldCard(game, 1, setup.OkitaId));
        KondoPersistentLethalReplacementFixture.CompleteAttack(game);
        Assert.Same(okita,
            KondoPersistentLethalReplacementFixture.FieldCard(game, 1, setup.OkitaId));

        // A later independent real combat changes the lethal state and can defeat
        // the card; the consumed Merlin event must not become permanent immunity.
        var attack = game.Handle(0, new L12Command("attack", setup.IndependentAttackerId,
            Target: new L12AttackTarget("legion", setup.OkitaId)));
        Assert.True(attack.Accepted, attack.Error);
        KondoPersistentLethalReplacementFixture.PassResponses(game);

        Assert.DoesNotContain(game.State.Players[1].Field.SelectMany(row => row),
            card => card?.InstanceId == setup.OkitaId);
        Assert.Contains(setup.OkitaId, game.State.Players[1].Resolving
            .Concat(game.State.Players[1].Graveyard).Select(card => card.InstanceId));
        Assert.Empty(KondoPersistentLethalReplacementFixture.ProtectionKeys(game, 1, setup.OkitaId));
    }

    [Fact]
    [Trait("L12Bug", "BUG-20261007-08cd2642-positive-reset")]
    public void PositiveTroopsClearOnlyTheOldProtectionForThatInstance()
    {
        var setup = KondoPersistentLethalReplacementFixture.BeginMerlinLethal();
        var game = setup.Game;
        KondoPersistentLethalReplacementFixture.Resolve(game, setup.KondoId);
        var okita = KondoPersistentLethalReplacementFixture.FieldCard(game, 1, setup.OkitaId);
        game.State.Players[1].UsedAbilities.Add("unrelated-runtime-key");
        Assert.Single(KondoPersistentLethalReplacementFixture.ProtectionKeys(game, 1, setup.OkitaId));

        L12DerivedStats.ClearDamageAndSetCurrentUntilTurnEnd(okita, 1000, game.State.TurnSerial);
        var command = game.Handle(0, new L12Command("attack", setup.ProbeAttackerId,
            Target: new L12AttackTarget("master")));
        Assert.True(command.Accepted, command.Error);

        Assert.Equal(1000, okita.Troops);
        Assert.Empty(KondoPersistentLethalReplacementFixture.ProtectionKeys(game, 1, setup.OkitaId));
        Assert.Contains("unrelated-runtime-key", game.State.Players[1].UsedAbilities);
    }

    [Fact]
    [Trait("L12Bug", "BUG-20261007-08cd2642-new-source")]
    public void SameTroopsFromADifferentDebuffSourceIsANewLethalState()
    {
        var setup = KondoPersistentLethalReplacementFixture.BeginMerlinLethal();
        var game = setup.Game;
        KondoPersistentLethalReplacementFixture.Resolve(game, setup.KondoId);
        var okita = KondoPersistentLethalReplacementFixture.FieldCard(game, 1, setup.OkitaId);
        var oldTroops = okita.Troops;
        var merlin = Assert.Single(okita.TimedModifiers, modifier => modifier.TroopsDelta < 0);
        okita.TimedModifiers.Remove(merlin);
        okita.TimedModifiers.Add(new L12TimedModifier
        {
            TroopsDelta = merlin.TroopsDelta,
            CostDelta = merlin.CostDelta,
            ConsumedTroopsBonus = merlin.ConsumedTroopsBonus,
            ExpiresAfterTurn = merlin.ExpiresAfterTurn,
            Source = "independent-debuff-source",
        });
        Assert.Equal(oldTroops, okita.Troops);

        var nextCommand = game.Handle(0, new L12Command("attack", setup.ProbeAttackerId,
            Target: new L12AttackTarget("master")));
        Assert.True(nextCommand.Accepted, nextCommand.Error);

        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == setup.OkitaId);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation == "effect-lethal-replacement");
        Assert.Empty(KondoPersistentLethalReplacementFixture.ProtectionKeys(game, 1, setup.OkitaId));
    }

    [Fact]
    public void EqualContinuousPenaltyFromANewSourceIsNotTheConsumedLethalEvent()
    {
        var setup = KondoPersistentLethalReplacementFixture.BeginMerlinLethal();
        var game = setup.Game;
        L12CardInstance Horse(string instanceId) => new()
        {
            InstanceId = instanceId,
            CardId = "S02-0523",
            Name = "特洛伊木马",
            CardType = "tactic",
            Faction = "olympus",
            Cost = 2,
            OwnerIndex = 0,
            Hidden = false,
        };
        game.State.Players[1].Field[1][2] = Horse("old-continuous-penalty-source");
        KondoPersistentLethalReplacementFixture.Resolve(game, setup.KondoId);
        var okita = KondoPersistentLethalReplacementFixture.FieldCard(game, 1, setup.OkitaId);
        var protectedTroops = okita.Troops;
        Assert.Single(KondoPersistentLethalReplacementFixture.ProtectionKeys(game, 1, setup.OkitaId));

        // Model two aura sources exchanged within one authoritative effect. Their
        // total is equal, but the new source is not the event already substituted.
        game.State.Players[1].Field[1][2] = Horse("new-continuous-penalty-source");
        Assert.Equal(protectedTroops, okita.Troops);
        var next = game.Handle(0, new L12Command("attack", setup.ProbeAttackerId,
            Target: new L12AttackTarget("master")));
        Assert.True(next.Accepted, next.Error);

        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == setup.OkitaId);
        Assert.Empty(KondoPersistentLethalReplacementFixture.ProtectionKeys(game, 1, setup.OkitaId));
    }

    [Fact]
    [Trait("L12Bug", "BUG-20261007-08cd2642-private-reset")]
    public void MovingProtectedCardToPrivateZoneClearsOnlyItsPersistentRuntimeKeys()
    {
        var setup = KondoPersistentLethalReplacementFixture.BeginMerlinLethal();
        var game = setup.Game;
        KondoPersistentLethalReplacementFixture.Resolve(game, setup.KondoId);
        var okita = KondoPersistentLethalReplacementFixture.FieldCard(game, 1, setup.OkitaId);
        game.State.Players[1].UsedAbilities.Add("unrelated-runtime-key");
        Assert.Single(KondoPersistentLethalReplacementFixture.ProtectionKeys(game, 1, setup.OkitaId));

        Assert.True(KondoPersistentLethalReplacementFixture.MoveToHand(game, 1, okita));

        Assert.Contains(okita, game.State.Players[1].Hand);
        Assert.Empty(KondoPersistentLethalReplacementFixture.ProtectionKeys(game, 1, setup.OkitaId));
        Assert.DoesNotContain(game.State.Players.SelectMany(player => player.UsedAbilities),
            key => key.Split(':').Contains(setup.OkitaId, StringComparer.OrdinalIgnoreCase));
        Assert.Contains("unrelated-runtime-key", game.State.Players[1].UsedAbilities);
    }

    [Fact]
    [Trait("L12Bug", "BUG-20261007-08cd2642-decline-control")]
    public void DecliningKondoStillAppliesMerlinsOriginalLethalResult()
    {
        var setup = KondoPersistentLethalReplacementFixture.BeginMerlinLethal();

        KondoPersistentLethalReplacementFixture.Resolve(setup.Game, "decline");

        Assert.Contains(setup.Game.State.Players[1].Graveyard,
            card => card.InstanceId == setup.OkitaId);
        Assert.Contains(setup.Game.State.Players[1].Field.SelectMany(row => row),
            card => card?.InstanceId == setup.KondoId);
        Assert.Empty(KondoPersistentLethalReplacementFixture.ProtectionKeys(
            setup.Game, 1, setup.OkitaId));
    }
}
