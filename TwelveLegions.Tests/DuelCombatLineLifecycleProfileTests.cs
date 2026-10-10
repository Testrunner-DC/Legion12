using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class DuelCombatLineLifecycleProfileTests
{
    private const string AbilityId = "S01-0101:ability:static:b5c9e323c0a061cc";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence(AbilityId, "normal", "reconnect", "presentation-consumers")]
    public void LuBuNoLossAndRangedImmunityShareTheAuthoritativeCombatProfileAcrossRestore()
    {
        var attackGame = Create(72340);
        var luBu = Card("S01-0101", "lubu-attacker", 3000);
        var defender = Card("S01-0001", "lubu-defender", 5000);
        attackGame.State.Players[0].Field[0][0] = luBu;
        attackGame.State.Players[1].Field[0][0] = defender;
        Assert.True(L12StructuredCardRules.CombatProfile(luBu, 0).HasAttackNoLoss);
        Assert.Contains(defender.InstanceId, attackGame.SnapshotFor(0).LegalAttackTargets[luBu.InstanceId]);

        attackGame = Restore(attackGame);
        var attack = attackGame.Handle(0, new L12Command("attack", luBu.InstanceId,
            Target: new L12AttackTarget("legion", defender.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);
        ResolveAllPrompts(attackGame);
        Assert.Equal(3000, attackGame.State.Players[0].Field[0][0]!.Troops);
        Assert.Equal(2000, attackGame.State.Players[1].Field[0][0]!.Troops);

        var rangedGame = Create(72341);
        var ranged = Card("S01-0003", "ranged-attacker", 3000);
        luBu = Card("S01-0101", "lubu-ranged-target", 3000);
        rangedGame.State.Players[0].Field[1][0] = ranged;
        rangedGame.State.Players[1].Field[0][0] = luBu;
        Assert.True(L12StructuredCardRules.CombatProfile(luBu, 0).CannotBeRanged);
        Assert.DoesNotContain(luBu.InstanceId,
            rangedGame.SnapshotFor(0).LegalAttackTargets.GetValueOrDefault(ranged.InstanceId, []));

        rangedGame = Restore(rangedGame);
        Assert.DoesNotContain(luBu.InstanceId,
            rangedGame.SnapshotFor(0).LegalAttackTargets.GetValueOrDefault(ranged.InstanceId, []));
        var rangedAttack = rangedGame.Handle(0, new L12Command("attack", ranged.InstanceId,
            Target: new L12AttackTarget("legion", luBu.InstanceId)));
        Assert.False(rangedAttack.Accepted);
        Assert.False(rangedGame.State.Players[0].Field[1][0]!.Tapped);
    }

    private static void ResolveAllPrompts(L12GameEngine game)
    {
        for (var count = 0; count < 30 && game.State.PendingPrompts.FirstOrDefault() is { } prompt; count++)
        {
            var command = prompt.Kind == "response"
                ? new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")
                : new L12Command("resolveDefense");
            var result = game.Handle(prompt.PlayerIndex, command);
            Assert.True(result.Accepted, result.Error);
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "duel-combat-line", "DUEL-COMBAT", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 3;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Morale.Clear();
        }
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static L12CardInstance Card(string cardId, string instanceId, int troops)
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
            BaseTroops = troops,
            Troops = troops,
            OwnerIndex = 0,
            SummonRound = -1,
        };
    }
}
