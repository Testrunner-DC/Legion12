using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// Shared combat lifecycle guard; no inference about the unavailable historical match.
public sealed class MedjedPiercingGraveBoundaryRegressionTests
{
    private const string VictimId = "medjed-piercing-killed-guard";
    private const string GraveGuardId = "medjed-piercing-preexisting-grave-guard";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-16T02:00:00Z");

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(0, false, true)]
    [InlineData(1, false, true)]
    [InlineData(0, true, false)]
    [InlineData(1, true, false)]
    [InlineData(0, true, true)]
    [InlineData(1, true, true)]
    public void RealZhaoYunPiercingChecksMedjedAgainstGraveyardAtDamageTime(
        int attackerIndex, bool preexistingGraveGuard, bool restoreEveryCommand)
    {
        var defenderIndex = 1 - attackerIndex;
        var basis = Catalog.DeckAt(0);
        var medjed = new L12PresetDeckDefinition
        {
            Name = "Synthetic Medjed piercing boundary", MasterId = "S01-02M3",
            CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = [],
        };
        var decks = new[] { basis, basis };
        decks[defenderIndex] = medjed;
        var game = new L12GameEngine(Catalog, "synthetic-medjed-piercing", "LOCAL", 20260912,
            ["Synthetic A", "Synthetic B"], decks, skipPreparation: true, disasterMode: "none",
            concealHiddenResponseAvailability: false, stateFormatVersion: 2, utcNow: () => Now);
        game.State.ActivePlayer = attackerIndex;
        game.State.FirstPlayer = attackerIndex;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        game.State.DisasterValue = 0;
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear(); player.Graveyard.Clear(); player.Resolving.Clear(); player.Removed.Clear();
            player.Morale.Clear(); player.UsedAbilities.Clear(); player.Relic = null; player.ExtraRelics.Clear();
            player.Hp = 8;
        }
        game.State.PendingPrompts.Clear(); game.State.PendingActivations.Clear();
        game.State.EffectStack.Clear(); game.State.DeferredEffectStack.Clear();
        game.State.PendingTriggerBatches.Clear(); game.State.PendingTriggerStackCandidates.Clear();
        game.State.Events.Clear(); game.State.AuthorityEvents.Clear(); game.State.Log.Clear();
        var attacker = game.State.Players[attackerIndex];
        attacker.Field[0][0] = Card("ST01-01", "medjed-piercing-zhao", attackerIndex);
        attacker.Morale.Add(new L12MoraleCard { CardId = "S01-M1", InstanceId = "piercing-pay-0" });
        attacker.Morale.Add(new L12MoraleCard { CardId = "S01-M1", InstanceId = "piercing-pay-1" });
        var defender = game.State.Players[defenderIndex];
        for (var slot = 0; slot < (preexistingGraveGuard ? 2 : 3); slot++)
            defender.Field[0][slot] = Card("S01-0212", slot == 0 ? VictimId : $"medjed-piercing-guard-{slot}", defenderIndex);
        if (preexistingGraveGuard) defender.Graveyard.Add(Card("S01-0212", GraveGuardId, defenderIndex));
        Assert.Equal(3, defender.Field.SelectMany(row => row).Count(card => card?.CardId == "S01-0212")
            + defender.Graveyard.Count(card => card.CardId == "S01-0212"));
        if (restoreEveryCommand) game = RestoreStrict(game);

        var damageBoundaryObserved = false;
        var legalMedjedOfferObserved = false;
        var killedGuardOffered = false;
        var medjedPromptCount = 0;
        var nestedDamage = 0;
        Execute(attackerIndex, new L12Command("attack", "medjed-piercing-zhao",
            Target: new L12AttackTarget("legion", VictimId)), "attack");

        for (var iteration = 0; iteration < 100; iteration++)
        {
            if (game.State.PendingDefense is { Target.Type: "master", Stage: L12CombatStage.DefenseChoice } pending)
            {
                Assert.True(pending.SuppressAttackTriggers);
                Assert.Equal(2000, pending.AttackValue);
                Assert.Contains(game.State.Players[defenderIndex].Resolving, card => card.InstanceId == VictimId);
                Assert.DoesNotContain(game.State.Players[defenderIndex].Graveyard, card => card.InstanceId == VictimId);
                Assert.Single(game.State.SuspendedCombatContexts);
                damageBoundaryObserved = true;
            }
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is not null)
            {
                string choice;
                if (prompt.Kind == "response") choice = "pass";
                else if (prompt.SourceCardId == "S01-02M3")
                {
                    medjedPromptCount++;
                    killedGuardOffered |= prompt.ValidChoices.Contains(VictimId);
                    Assert.DoesNotContain(VictimId, prompt.ValidChoices);
                    if (prompt.ValidChoices.Contains(GraveGuardId))
                    {
                        Assert.True(preexistingGraveGuard);
                        Assert.Equal(7, game.State.Players[defenderIndex].Hp);
                        Assert.Contains(game.State.Players[defenderIndex].Resolving, card => card.InstanceId == VictimId);
                        legalMedjedOfferObserved = true;
                        choice = GraveGuardId;
                    }
                    else choice = prompt.ValidChoices.First(value => value is not ("skip" or "mode:none"));
                }
                else if (prompt.ValidChoices.Contains("mode:use")) choice = "mode:use";
                else choice = prompt.ValidChoices.First(value => value is not ("skip" or "mode:none"));
                Execute(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice), prompt.Kind);
                continue;
            }
            if (game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice } defense)
            {
                var hp = game.State.Players[defenderIndex].Hp;
                Execute(1 - defense.AttackerPlayer, new L12Command("resolveDefense"), "resolveDefense");
                nestedDamage += hp - game.State.Players[defenderIndex].Hp;
                continue;
            }
            if (game.State.PendingDefense is null && game.State.EffectStack.Count == 0
                && game.State.PendingActivations.Count == 0) break;
            throw new InvalidOperationException("Unadvanced authoritative combat boundary");
        }

        Assert.True(damageBoundaryObserved);
        Assert.Equal(1, nestedDamage);
        Assert.Equal(7, game.State.Players[defenderIndex].Hp);
        Assert.Equal(preexistingGraveGuard, legalMedjedOfferObserved);
        if (!preexistingGraveGuard) Assert.Equal(0, medjedPromptCount);
        Assert.False(killedGuardOffered);
        Assert.Null(game.State.PendingDefense);
        Assert.Empty(game.State.SuspendedCombatContexts);
        Assert.Empty(game.State.PendingPrompts); Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack); Assert.Empty(game.State.DeferredEffectStack);
        Assert.Empty(game.State.PendingTriggerBatches); Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Single(game.State.Players[defenderIndex].Graveyard, card => card.InstanceId == VictimId);
        Assert.Empty(game.State.Players[defenderIndex].Resolving);
        Assert.Equal(2,
            game.State.Players[defenderIndex].Field.SelectMany(row => row).Count(card => card?.CardId == "S01-0212"));
        if (preexistingGraveGuard)
        {
            var returned = Assert.Single(game.State.Players[defenderIndex].Field.SelectMany(row => row)
                .OfType<L12CardInstance>().Where(card => card.InstanceId == GraveGuardId));
            Assert.False(returned.Tapped);
        }
        _ = RestoreStrict(game);
        Execute(attackerIndex, new L12Command("endTurn"), "endTurn");
        Assert.NotEqual(attackerIndex, game.State.ActivePlayer);
        void Execute(int actor, L12Command command, string stage)
        {
            var result = game.Handle(actor, command);
            Assert.True(result.Accepted, $"{stage}: {result.Error}");
            if (restoreEveryCommand) game = RestoreStrict(game);
        }
    }

    private static L12GameEngine RestoreStrict(L12GameEngine game)
    {
        var raw = game.SerializeFullState(); var hash = game.ComputeStateHash();
        var random = game.RandomState!.Value; var sequence = game.CardFactSignalSequence;
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, raw, random, sequence,
            concealHiddenResponseAvailability: false, utcNow: () => Now);
        Assert.Equal(raw, restored.SerializeFullState()); Assert.Equal(hash, restored.ComputeStateHash());
        Assert.Equal(random, restored.RandomState); Assert.Equal(sequence, restored.CardFactSignalSequence);
        return restored;
    }

    private static L12CardInstance Card(string id, string instanceId, int owner)
    {
        var definition = Catalog.Cards[id];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = id, Name = definition.NameZh, CardType = definition.CardType,
            Faction = definition.Faction, Cost = definition.Cost ?? 0, HasPrintedCost = definition.Cost.HasValue,
            ImageUrl = definition.ImageUrl, EffectText = definition.Effect, Traits = [.. definition.Traits],
            Profession = definition.Profession, EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0, OwnerIndex = owner, SummonRound = -1,
        };
    }
}
