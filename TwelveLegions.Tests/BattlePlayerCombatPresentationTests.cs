using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BattlePlayerCombatPresentationTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12CardInstance Legion(string id, bool hidden = false) => new()
    {
        InstanceId = id, CardId = $"test-{id}", Name = "同名军团", CardType = "legion",
        Faction = "universal", Cost = 1, BaseTroops = 3000, Troops = 3000,
        SummonRound = -1, Hidden = hidden,
    };

    private static L12CardInstance PrintedCard(string cardId, string id)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = id, CardId = cardId, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction,
            Cost = definition.Cost ?? 0, BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0, SummonRound = -1,
            EffectText = definition.Effect, Traits = [.. definition.Traits],
            Profession = definition.Profession,
        };
    }

    private static L12GameEngine Game(int seed = 93107, bool autoPassEmptyResponses = false)
        => new(Catalog, "combat-log", "COMBAT", seed,
        ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: autoPassEmptyResponses,
        concealHiddenResponseAvailability: false, stateFormatVersion: 2);

    private static void Ready(L12GameEngine game)
    {
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
        }
        game.State.Phase = L12Phase.Main;
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
    }

    private static void PassToDefenseChoice(L12GameEngine game)
    {
        for (var step = 0; step < 12 && game.State.PendingDefense?.Stage != L12CombatStage.DefenseChoice; step++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", prompt.Kind);
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
        Assert.Equal(L12CombatStage.DefenseChoice, game.State.PendingDefense?.Stage);
    }

    private static void PassPendingResponses(L12GameEngine game)
    {
        for (var step = 0; step < 12 && game.State.PendingPrompts.Count > 0; step++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", prompt.Kind);
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThunderLowRollPublishesOnePublicCombatAbortAfterCommittedAttack(bool masterTarget)
    {
        L12GameEngine? stopped = null;
        L12CardInstance? committedAttacker = null;
        L12CardInstance? committedTarget = null;
        for (var seed = 1; seed <= 100; seed++)
        {
            var game = Game(seed);
            Ready(game);
            var attacker = Legion($"thunder-attacker-{seed}");
            var target = Legion($"thunder-target-{seed}");
            game.State.Players[0].Field[0][0] = attacker;
            if (!masterTarget) game.State.Players[1].Field[0][0] = target;
            game.State.ActiveDisaster = PrintedCard("S01-DS04", $"thunder-disaster-{seed}");
            Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
                Target: masterTarget ? new L12AttackTarget("master")
                    : new L12AttackTarget("legion", target.InstanceId))).Accepted);
            if (game.State.Events.Any(entry => entry.Type == "attack-ended"))
            {
                stopped = game;
                committedAttacker = attacker;
                committedTarget = masterTarget ? null : target;
                break;
            }
        }
        var result = Assert.IsType<L12GameEngine>(stopped);
        var attackerCard = Assert.IsType<L12CardInstance>(committedAttacker);
        Assert.True(attackerCard.Tapped);
        Assert.Equal(1, attackerCard.AttacksThisTurn);
        Assert.Null(result.State.PendingDefense);
        Assert.Single(result.State.Events, entry => entry.Type == "dice");
        Assert.DoesNotContain(result.State.Events, entry => entry.Type is "attack" or "defense" or "combat" or "damage");
        var aborted = Assert.Single(result.State.Events, entry => entry.Type == "attack-ended");
        var combat = Assert.IsType<L12PlayerCombatPresentation>(aborted.PlayerCombat);
        Assert.StartsWith("combat-", combat.CombatId);
        Assert.Equal("attack-aborted", combat.EventKind);
        Assert.Equal("aborted", combat.OutcomeCode);
        Assert.Equal("thunder-roll-failed", combat.PublicReasonCode);
        Assert.Equal(attackerCard.InstanceId, combat.AttackerInstanceId);
        Assert.Equal(committedTarget?.InstanceId, combat.TargetInstanceId);
        Assert.Equal(3000, combat.AttackerTroops);
        Assert.Equal(masterTarget ? null : 3000, combat.DefenderTroops);
        foreach (var snapshot in new[] { result.SnapshotFor(0), result.SnapshotFor(1),
                     result.SnapshotForSpectator(), result.SnapshotForReferee(), result.SnapshotForGm(0) })
        {
            var visible = Assert.Single(snapshot.RecentEvents, entry => entry.Type == "attack-ended");
            Assert.Equal(combat.CombatId, visible.PlayerCombat?.CombatId);
            Assert.Equal("thunder-roll-failed", visible.PlayerCombat?.PublicReasonCode);
            Assert.Equal(attackerCard.InstanceId, visible.PlayerCombat?.AttackerInstanceId);
            Assert.Equal(committedTarget?.InstanceId, visible.PlayerCombat?.TargetInstanceId);
            Assert.Equal(3000, visible.PlayerCombat?.AttackerTroops);
            Assert.Equal(masterTarget ? null : 3000, visible.PlayerCombat?.DefenderTroops);
        }
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, result.SerializeFullState(),
            result.RandomState!.Value, result.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        Assert.Equal(combat.CombatId, Assert.Single(restored.State.Events,
            entry => entry.Type == "attack-ended").PlayerCombat?.CombatId);
    }

    [Theory]
    [InlineData(2000, "defeated")]
    [InlineData(500, "not-defeated")]
    public void SureHitMasterAttackRetargetedByPuppetPublishesActualFinalTarget(int troops, string outcome)
    {
        var game = Game(6215, autoPassEmptyResponses: true);
        Ready(game);
        var attacker = PrintedCard("S02-0003", $"puppet-attacker-{troops}");
        attacker.HasSureHit = true;
        attacker.Troops = troops;
        var puppet = PrintedCard("S02-0005", $"puppet-response-{troops}");
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Hand.Add(puppet);

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        var declaration = Assert.Single(game.State.Events, entry => entry.Type == "attack");
        Assert.Null(declaration.PlayerCombat?.TargetInstanceId);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(puppet.InstanceId, response.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: response.PromptId,
            Choice: puppet.InstanceId)).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("stack-response-puppet-slot", slot.Continuation);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: "0:1")).Accepted);

        var resolved = Assert.Single(game.State.Events, entry => entry.Type == "combat");
        Assert.Equal(declaration.PlayerCombat?.CombatId, resolved.PlayerCombat?.CombatId);
        Assert.Equal(outcome, resolved.PlayerCombat?.OutcomeCode);
        Assert.Equal(puppet.InstanceId, resolved.PlayerCombat?.TargetInstanceId);
        Assert.Contains(resolved.Cards, card => card.InstanceId == puppet.InstanceId);
        foreach (var snapshot in new[] { game.SnapshotFor(0), game.SnapshotFor(1),
                     game.SnapshotForSpectator(), game.SnapshotForReferee(), game.SnapshotForGm(0) })
        {
            var publicResult = Assert.Single(snapshot.RecentEvents, entry => entry.Type == "combat");
            Assert.Equal(puppet.InstanceId, publicResult.PlayerCombat?.TargetInstanceId);
            Assert.Contains(publicResult.Cards, card => card.InstanceId == puppet.InstanceId);
        }
    }

    [Fact]
    public void CombatIdSurvivesCheckpointAndBothPlayerViews()
    {
        var game = Game();
        Ready(game);
        var attacker = Legion("attacker");
        var target = Legion("target");
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = target;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        var attack = Assert.Single(game.State.Events, entry => entry.Type == "attack");
        var id = Assert.IsType<string>(attack.PlayerCombat?.CombatId);
        Assert.Equal("attack", attack.PlayerCombat?.EventKind);
        Assert.Equal(attacker.InstanceId, attack.PlayerCombat?.AttackerInstanceId);
        Assert.Equal(target.InstanceId, attack.PlayerCombat?.TargetInstanceId);
        Assert.Equal(id, game.State.PendingDefense?.CombatId);
        Assert.Equal(id, Assert.Single(game.SnapshotFor(0).RecentEvents, entry => entry.Type == "attack")
            .PlayerCombat?.CombatId);
        Assert.Equal(id, Assert.Single(game.SnapshotFor(1).RecentEvents, entry => entry.Type == "attack")
            .PlayerCombat?.CombatId);
        Assert.Equal(id, Assert.Single(game.SnapshotForSpectator().RecentEvents, entry => entry.Type == "attack")
            .PlayerCombat?.CombatId);
        foreach (var snapshot in new[] { game.SnapshotFor(0), game.SnapshotFor(1),
                     game.SnapshotForSpectator(), game.SnapshotForReferee(), game.SnapshotForGm(0) })
        {
            var publicAttack = Assert.Single(snapshot.RecentEvents, entry => entry.Type == "attack");
            Assert.Equal(attacker.InstanceId, publicAttack.PlayerCombat?.AttackerInstanceId);
            Assert.Equal(target.InstanceId, publicAttack.PlayerCombat?.TargetInstanceId);
            Assert.Equal(3000, publicAttack.PlayerCombat?.AttackerTroops);
            Assert.Equal(3000, publicAttack.PlayerCombat?.DefenderTroops);
        }

        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        Assert.Equal(id, restored.State.PendingDefense?.CombatId);
        Assert.Equal(id, Assert.Single(restored.State.Events, entry => entry.Type == "attack")
            .PlayerCombat?.CombatId);
    }

    [Fact]
    public void MasterBlockAndUnblockedDamageCarryDifferentAuthoritativeOutcomes()
    {
        var unblocked = Game();
        Ready(unblocked);
        var attacker = Legion("unblocked-attacker");
        unblocked.State.Players[0].Field[0][0] = attacker;
        var hpBefore = unblocked.State.Players[1].Hp;
        Assert.True(unblocked.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        PassToDefenseChoice(unblocked);
        var unblockedId = unblocked.State.PendingDefense?.CombatId;
        Assert.True(unblocked.Handle(1, new L12Command("resolveDefense", CardInstanceIds: [])).Accepted);
        PassPendingResponses(unblocked);
        var open = Assert.Single(unblocked.State.Events, entry => entry.Type == "defense");
        Assert.Equal(unblockedId, open.PlayerCombat?.CombatId);
        Assert.Equal("unblocked", open.PlayerCombat?.OutcomeCode);
        Assert.Equal(hpBefore - unblocked.State.Players[1].Hp, open.PlayerCombat?.MasterDamage);
        Assert.DoesNotContain(unblocked.State.Events, entry => entry.Type == "damage"
            && entry.PlayerCombat is not null);

        var blocked = Game();
        Ready(blocked);
        var blocker = Legion("blocker");
        blocked.State.Players[0].Field[0][0] = Legion("blocked-attacker");
        blocked.State.Players[1].Hand.Add(blocker);
        Assert.True(blocked.Handle(0, new L12Command("attack", "blocked-attacker",
            Target: new L12AttackTarget("master"))).Accepted);
        PassToDefenseChoice(blocked);
        var blockedId = blocked.State.PendingDefense?.CombatId;
        Assert.True(blocked.Handle(1, new L12Command("resolveDefense",
            CardInstanceIds: [blocker.InstanceId])).Accepted);
        PassPendingResponses(blocked);
        var defense = Assert.Single(blocked.State.Events, entry => entry.Type == "defense");
        Assert.Equal(blockedId, defense.PlayerCombat?.CombatId);
        Assert.Equal("blocked", defense.PlayerCombat?.OutcomeCode);
        Assert.Null(defense.PlayerCombat?.MasterDamage);
        Assert.Contains(defense.Cards, card => card.InstanceId == blocker.InstanceId);
    }

    [Fact]
    public void CombatIdRemainsUniqueAfterRestartAndAnotherAttack()
    {
        var game = Game();
        Ready(game);
        game.State.Players[0].Field[0][0] = Legion("first-attacker");
        Assert.True(game.Handle(0, new L12Command("attack", "first-attacker",
            Target: new L12AttackTarget("master"))).Accepted);
        PassToDefenseChoice(game);
        var firstId = Assert.IsType<string>(game.State.PendingDefense?.CombatId);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        Assert.Equal(firstId, restored.State.PendingDefense?.CombatId);
        Assert.True(restored.Handle(1, new L12Command("resolveDefense", CardInstanceIds: [])).Accepted);
        PassPendingResponses(restored);
        Assert.Equal(firstId, Assert.Single(restored.State.Events, entry => entry.Type == "defense")
            .PlayerCombat?.CombatId);
        Assert.Null(restored.State.PendingDefense);
        restored.State.Players[0].Field[0][1] = Legion("second-attacker");
        restored.State.Phase = L12Phase.Main;
        restored.State.ActivePlayer = 0;
        Assert.True(restored.Handle(0, new L12Command("attack", "second-attacker",
            Target: new L12AttackTarget("master"))).Accepted);
        var secondId = Assert.IsType<string>(restored.State.PendingDefense?.CombatId);
        Assert.NotEqual(firstId, secondId);
        Assert.Equal(2, restored.State.Events.Count(entry => entry.PlayerCombat?.EventKind == "attack"));
    }

    [Fact]
    public void SupportChoiceRevalidationFailureKeepsItsOwnPublicOutcome()
    {
        var game = Game();
        Ready(game);
        var attacker = Legion("support-invalid-attacker");
        var target = Legion("support-invalid-target");
        var supporter = Legion("support-invalid-supporter");
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[1].Field[1][0] = supporter;
        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        PassToDefenseChoice(game);
        var id = game.State.PendingDefense?.CombatId;
        Assert.True(game.Handle(1, new L12Command("resolveDefense",
            SupportInstanceId: supporter.InstanceId)).Accepted);
        game.State.Players[1].Field[1][0] = null;
        game.State.Players[1].Graveyard.Add(supporter);
        PassPendingResponses(game);
        Assert.Contains(game.State.Events, entry => entry.Type == "defense-invalid"
            && entry.PlayerCombat?.CombatId == id
            && entry.PlayerCombat?.OutcomeCode == "invalid-support"
            && entry.PlayerCombat?.PublicReasonCode == "choice-unavailable");
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "defense-invalid"
            && (entry.Text.Contains("重新校验", StringComparison.Ordinal)
                || entry.Text.Contains("权威事件", StringComparison.Ordinal)));
    }

    [Fact]
    public void DeclinedExtraDefenseCostIsNotPresentedAsSuccessfulBlock()
    {
        var game = Game();
        Ready(game);
        var richard = PrintedCard("S02-0608", "richard-extra-cost");
        richard.SummonRound = 0;
        var blocker = Legion("richard-blocker");
        blocker.Troops = 12000;
        var spare = Legion("richard-spare");
        game.State.Players[0].Field[0][0] = richard;
        game.State.Players[1].Hand.AddRange([blocker, spare]);
        Assert.True(game.Handle(0, new L12Command("attack", richard.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-order", order.Kind);
        Assert.Equal(2, order.ValidChoices.Count);
        var defenseId = Assert.Single(order.ValidChoices,
            choice => order.Data[choice].Contains("抵挡", StringComparison.Ordinal));
        var squireId = Assert.Single(order.ValidChoices, choice => choice != defenseId);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: order.PromptId,
            CardInstanceIds: [defenseId, squireId])).Accepted);
        PassToDefenseChoice(game);
        var id = game.State.PendingDefense?.CombatId;
        Assert.True(game.Handle(1, new L12Command("resolveDefense",
            CardInstanceIds: [blocker.InstanceId])).Accepted);
        for (var step = 0; step < 12 && game.State.PendingPrompts.Count > 0; step++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.Data.GetValueOrDefault("action") == "s2-richard-defense-extra-discard")
            {
                Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                    Choice: "decline")).Accepted);
                break;
            }
            Assert.Equal("response", prompt.Kind);
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
        Assert.Contains(game.State.Events, entry => entry.PlayerCombat?.CombatId == id
            && entry.PlayerCombat?.OutcomeCode == "invalid-block"
            && entry.PlayerCombat?.PublicReasonCode == "extra-cost-unpaid");
        Assert.DoesNotContain(game.State.Events, entry => entry.PlayerCombat?.CombatId == id
            && entry.PlayerCombat?.OutcomeCode == "blocked");
    }

    [Fact]
    public void RecipientProjectionOnlyPublishesWhitelistedCombatFields()
    {
        var game = Game();
        var hidden = Legion("hidden-hand", hidden: true);
        var actionEvent = new L12ActionEvent(7, "defense-invalid", 1, "内部错误：私有候选", [hidden])
        {
            PlayerCombat = new("battle-7", "defense-invalid", "invalid-support",
                "internal-error-private-card", hidden.InstanceId, hidden.InstanceId, 3000, 3000, -1),
        };
        foreach (var viewer in new[] { 0, 1, -1 })
        {
            var projected = L12RecipientVisibility.ProjectActionEvent(game.State, actionEvent,
                viewer, revealAllDisasters: false);
            Assert.Equal("battle-7", projected.PlayerCombat?.CombatId);
            Assert.Equal("invalid-support", projected.PlayerCombat?.OutcomeCode);
            Assert.Equal("unknown", projected.PlayerCombat?.PublicReasonCode);
            Assert.Null(projected.PlayerCombat?.AttackerInstanceId);
            Assert.Null(projected.PlayerCombat?.TargetInstanceId);
            Assert.Null(projected.PlayerCombat?.AttackerTroops);
            Assert.Null(projected.PlayerCombat?.DefenderTroops);
            Assert.Null(projected.PlayerCombat?.MasterDamage);
        }
        var injected = actionEvent with
        {
            PlayerCombat = actionEvent.PlayerCombat! with
            {
                EventKind = "backend-internal-step", OutcomeCode = "secret-target-selected",
            },
        };
        var safe = L12RecipientVisibility.ProjectActionEvent(game.State, injected, 0,
            revealAllDisasters: false);
        Assert.Equal("unknown", safe.PlayerCombat?.EventKind);
        Assert.Equal("unknown", safe.PlayerCombat?.OutcomeCode);
    }

    [Fact]
    public void LegacyActionEventsAndPendingCombatDeserializeWithoutPresentationFields()
    {
        var oldEvent = JsonSerializer.Deserialize<L12ActionEvent>("""
            {"Sequence":1,"Type":"attack","PlayerIndex":0,"Text":"旧记录","Cards":[]}
            """);
        Assert.NotNull(oldEvent);
        Assert.Null(oldEvent.PlayerCombat);
        var oldPending = JsonSerializer.Deserialize<L12PendingDefense>("""
            {"AttackerPlayer":0,"AttackerInstanceId":"old-attacker","Target":{"Type":"master"}}
            """);
        Assert.NotNull(oldPending);
        Assert.Null(oldPending.CombatId);
    }
}
