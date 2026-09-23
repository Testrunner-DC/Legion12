using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PrintedRangedProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12CardInstance Card(string id, string instance)
    {
        var definition = Catalog.Cards[id];
        return new() { InstanceId = instance, CardId = id, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction, Profession = definition.Profession,
            EffectText = definition.Effect, Cost = definition.Cost ?? 0, Troops = 3000, BaseTroops = 3000,
            SummonRound = -1 };
    }

    private static L12GameEngine Create(string id, int row)
    {
        var game = new L12GameEngine(Catalog, "printed-range", "RANGE", 91742, ["甲", "乙"], [0, 0],
            skipPreparation: true, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0; game.State.Round = 3; game.State.TurnSerial = 5; game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            foreach (var slots in player.Field) Array.Clear(slots);
            player.Hand.Clear(); player.Morale.Clear(); player.UsedAbilities.Clear();
        }
        game.State.Players[0].Field[row][0] = Card(id, "source");
        game.State.Players[1].Field[0][0] = Card("ST01-05", "front");
        game.State.Players[1].Field[1][0] = Card("ST01-05", "back");
        return game;
    }

    private static CommandResult ResolveCombatWithoutCardSpecificAttackTriggers(L12GameEngine game)
    {
        var pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        pending.Stage = L12CombatStage.DefenseChoice;
        game.State.Phase = L12Phase.Defense;
        game.State.PendingPrompts.Clear();
        game.State.PendingActivations.Clear();
        game.State.PendingTriggerStackCandidates.Clear();
        game.State.PendingTriggerBatches.Clear();
        game.State.EffectStack.Clear();
        var method = typeof(L12GameEngine).GetMethod("ResolveDefenseCore",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return Assert.IsType<CommandResult>(method.Invoke(game,
            [1, Array.Empty<string>(), Array.Empty<string>(), false]));
    }

    // Row masks are reviewed expectations, not inferred from production atoms.
    [Theory]
    [InlineData("S01-0003", "S01-0003:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0110", "S01-0110:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0111", "S01-0111:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0112", "S01-0112:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0113", "S01-0113:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0114", "S01-0114:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0115", "S01-0115:ability:static:9ba2f4f5354a2a05", 1)]
    [InlineData("S01-0116", "S01-0116:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0208", "S01-0208:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0209", "S01-0209:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0210", "S01-0210:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0211", "S01-0211:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0213", "S01-0213:ability:static:9ba2f4f5354a2a05", 1)]
    [InlineData("S01-0214", "S01-0214:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0309", "S01-0309:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0313", "S01-0313:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0314", "S01-0314:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0316", "S01-0316:ability:static:9ba2f4f5354a2a05", 1)]
    [InlineData("S01-0409", "S01-0409:ability:static:6c03e83e9e18abb1", 2)]
    [InlineData("S01-0410", "S01-0410:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0411", "S01-0411:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0413", "S01-0413:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S01-0415", "S01-0415:ability:static:9ba2f4f5354a2a05", 1)]
    [InlineData("S01-0416", "S01-0416:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S02-0003", "S02-0003:ability:continuous:e9823ffd970d6ce6", 3)]
    [InlineData("S02-0204", "S02-0204:ability:continuous:e9823ffd970d6ce6", 3)]
    [InlineData("S02-0304", "S02-0304:ability:continuous:e9823ffd970d6ce6", 3)]
    [InlineData("S02-0507", "S02-0507:ability:static:3f520b391281b325", 2)]
    [InlineData("S02-0508", "S02-0508:ability:static:aa41bff900061e1d", 3)]
    [InlineData("S02-0513", "S02-0513:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S02-0514", "S02-0514:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S02-0515", "S02-0515:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("S02-0517", "S02-0517:ability:static:9ba2f4f5354a2a05", 1)]
    [InlineData("S02-0614", "S02-0614:ability:continuous:e9823ffd970d6ce6", 3)]
    [InlineData("S02-0617", "S02-0617:ability:continuous:e9823ffd970d6ce6", 3)]
    [InlineData("S02-0618", "S02-0618:ability:continuous:e9823ffd970d6ce6", 3)]
    [InlineData("S02-0619", "S02-0619:ability:continuous:f0839056592c5ee2", 1)]
    [InlineData("ST01-07", "ST01-07:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("ST01-08", "ST01-08:ability:static:9ba2f4f5354a2a05", 1)]
    [InlineData("ST01-09", "ST01-09:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("ST02-08", "ST02-08:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("ST03-05", "ST03-05:ability:static:efd7771da618f0ac", 3)]
    [InlineData("ST04-07", "ST04-07:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("ST05-03", "ST05-03:ability:continuous:3119db9911c31cf3", 3)]
    [InlineData("ST05-04", "ST05-04:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("ST05-08", "ST05-08:ability:static:e3471cd2a7042e59", 3)]
    [InlineData("ST05-09", "ST05-09:ability:static:e3471cd2a7042e59", 3)]
    [L12AbilityEvidence("S01-0003:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0110:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0111:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0112:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0113:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0114:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0115:ability:static:9ba2f4f5354a2a05", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0116:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0208:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0209:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0210:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0211:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0213:ability:static:9ba2f4f5354a2a05", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0214:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0309:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0313:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0314:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0316:ability:static:9ba2f4f5354a2a05", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0409:ability:static:6c03e83e9e18abb1", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0410:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0411:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0413:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0415:ability:static:9ba2f4f5354a2a05", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S01-0416:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S02-0003:ability:continuous:e9823ffd970d6ce6", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S02-0204:ability:continuous:e9823ffd970d6ce6", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S02-0304:ability:continuous:e9823ffd970d6ce6", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S02-0507:ability:static:3f520b391281b325", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S02-0508:ability:static:aa41bff900061e1d", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S02-0513:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S02-0514:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S02-0515:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S02-0517:ability:static:9ba2f4f5354a2a05", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S02-0614:ability:continuous:e9823ffd970d6ce6", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S02-0617:ability:continuous:e9823ffd970d6ce6", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S02-0618:ability:continuous:e9823ffd970d6ce6", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("S02-0619:ability:continuous:f0839056592c5ee2", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("ST01-07:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("ST01-08:ability:static:9ba2f4f5354a2a05", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("ST01-09:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("ST02-08:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("ST03-05:ability:static:efd7771da618f0ac", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("ST04-07:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("ST05-03:ability:continuous:3119db9911c31cf3", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("ST05-04:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("ST05-08:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    [L12AbilityEvidence("ST05-09:ability:static:e3471cd2a7042e59", "conditional-profile", "attack-preview", "no-target-preview",
        "normal", "reconnect", "presentation-consumers", "ranged-no-loss", "source-row-change")]
    public void PrintedRangeUsesCurrentRowAndRestoresAuthoritativePreview(string cardId, string abilityId, int rows)
    {
        var ability = Assert.Single(Catalog.AtomicEffects.Find(cardId)!.Abilities, item => item.AbilityId == abilityId);
        Assert.Equal("continuous", ability.ExecutionModel);
        Assert.Null(ability.CostText);
        foreach (var row in new[] { 0, 1 })
        {
            var game = Create(cardId, row);
            var source = game.State.Players[0].Field[row][0]!;
            var expectedRange = (rows & (1 << row)) != 0;
            var profile = L12StructuredCardRules.CombatProfile(source, row);
            Assert.Equal(expectedRange, profile.HasRangeBonus);
            Assert.Equal(expectedRange, profile.HasRangedNoLoss);
            var expected = row == 0
                ? (expectedRange ? new[] { "back", "front", "master" } : new[] { "front", "master" })
                : (expectedRange ? new[] { "front" } : Array.Empty<string>());
            var before = game.SerializeFullState();
            Assert.Equal(expected, game.SnapshotFor(0).LegalAttackTargets.GetValueOrDefault("source", []).Order());
            Assert.Equal(before, game.SerializeFullState()); // Query/repeated queries do not execute an effect.
            game = L12GameEngine.RestoreCheckpoint(Catalog, before, game.RandomState!.Value,
                game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
            Assert.Equal(expected, game.SnapshotFor(0).LegalAttackTargets.GetValueOrDefault("source", []).Order());

            if (expectedRange)
            {
                var targetId = row == 0 ? "back" : "front";
                var targetRow = row == 0 ? 1 : 0;
                game.State.Players[1].Field[1 - targetRow][0] = null;
                var target = Assert.IsType<L12CardInstance>(game.State.Players[1].Field[targetRow][0]);
                target.Troops = 10000;
                var attacker = Assert.IsType<L12CardInstance>(game.State.Players[0].Field[row][0]);
                var attack = game.Handle(0, new L12Command("attack", attacker.InstanceId,
                    Target: new L12AttackTarget("legion", targetId)));
                Assert.True(attack.Accepted, attack.Error);
                var attackerBefore = attacker.CurrentTroops;
                var pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
                Assert.True(pending.IsRanged);
                Assert.True(pending.RangedNoLoss);
                var settlement = ResolveCombatWithoutCardSpecificAttackTriggers(game);
                Assert.True(settlement.Accepted, settlement.Error);
                Assert.Equal(attackerBefore, attacker.Troops);
                Assert.True(target.Troops < 10000);
                Assert.Contains(game.State.Events, entry => entry.Type == "combat"
                    && entry.Text.Contains("进攻无损", StringComparison.Ordinal));
            }

            game = Create(cardId, row);
            foreach (var slots in game.State.Players[1].Field) Array.Clear(slots);
            var withoutLegions = game.SnapshotFor(0).LegalAttackTargets.GetValueOrDefault("source", []);
            Assert.Equal(row == 0 ? new[] { "master" } : [], withoutLegions);
            Assert.Empty(game.State.PendingPrompts);
            Assert.Empty(game.State.EffectStack);
        }
    }

    [Theory]
    [InlineData("S01-0003", 1, 3000)]
    [InlineData("S01-0115", 0, 3000)]
    [InlineData("S01-0409", 1, 2000)]
    [InlineData("S02-0507", 1, 3000)]
    [L12AbilityEvidence("S01-0003:ability:static:e3471cd2a7042e59", "normal-ranged-combat", "reconnect-before-attack", "duplicate-attack")]
    [L12AbilityEvidence("S01-0115:ability:static:9ba2f4f5354a2a05", "normal-ranged-combat", "reconnect-before-attack", "duplicate-attack")]
    [L12AbilityEvidence("S01-0409:ability:static:6c03e83e9e18abb1", "normal-ranged-combat", "reconnect-before-attack", "duplicate-attack")]
    [L12AbilityEvidence("S02-0507:ability:static:3f520b391281b325", "normal-ranged-combat", "reconnect-before-attack", "duplicate-attack")]
    public void RepresentativeRangeConditionsActuallyPreventRetaliationAfterRestore(string cardId, int row, int expectedDamage)
    {
        var game = Create(cardId, row);
        var targetId = row == 0 ? "back" : "front";
        var targetRow = row == 0 ? 1 : 0;
        game.State.Players[1].Field[1 - targetRow][0] = null;
        game.State.Players[1].Field[targetRow][0]!.Troops = 5000;
        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var command = new L12Command("attack", "source", Target: new L12AttackTarget("legion", targetId));
        var result = game.Handle(0, command);
        Assert.True(result.Accepted, result.Error);
        for (var count = 0; count < 20 && game.State.PendingPrompts.FirstOrDefault() is { } prompt; count++)
        {
            Assert.Equal("response", prompt.Kind);
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Equal(3000, game.State.Players[0].Field[row][0]!.Troops);
        Assert.Equal(5000 - expectedDamage, game.State.Players[1].Field[targetRow][0]!.Troops);
        var beforeDuplicate = game.SerializeFullState();
        Assert.False(game.Handle(0, command).Accepted);
        Assert.Equal(beforeDuplicate, game.SerializeFullState());
    }
}
