using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BackRowAttackTroopsSetLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData("S01-0409", 2000)]
    [InlineData("S02-0507", 3000)]
    [L12AbilityEvidence("S01-0409:ability:attack:c900a6435336564c", "normal", "reconnect", "presentation-consumers", "post-attack-revert")]
    [L12AbilityEvidence("S02-0507:ability:attack:d20040947938d125", "normal", "reconnect", "presentation-consumers", "post-attack-revert")]
    public void BackRowAttackUsesItsDeclaredSetValueAndRevertsAfterSettlement(string cardId, int setValue)
    {
        var game = Create(72360 + setValue);
        var attacker = Card(cardId, $"back-row-{cardId}", Catalog.Cards[cardId].Troops ?? 0);
        var target = Card("S01-0001", $"target-{cardId}", 5000);
        game.State.Players[0].Field[1][0] = attacker;
        game.State.Players[1].Field[0][0] = target;

        Assert.Equal(setValue, L12StructuredCardRules.CombatProfile(attacker, 1).AttackTroopsSetValue);
        Assert.Contains(target.InstanceId, game.SnapshotFor(0).LegalAttackTargets[attacker.InstanceId]);

        game = Restore(game);
        var restoredAttacker = game.State.Players[0].Field[1][0]!;
        var baseTroops = restoredAttacker.BaseTroops;
        var attack = game.Handle(0, new L12Command("attack", restoredAttacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);
        ResolveAllPrompts(game);

        Assert.Equal(baseTroops, game.State.Players[0].Field[1][0]!.Troops);
        Assert.Equal(5000 - setValue, game.State.Players[1].Field[0][0]!.Troops);
        var playerLog = Assert.Single(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains($"兵力视为{setValue}", StringComparison.Ordinal));
        Assert.Equal("触发 进攻时效果", playerLog.PlayerLogSemantic?.ActionLabel);
        Assert.Equal($"本次进攻兵力变为{setValue}", playerLog.PlayerLogSemantic?.OutcomeLabel);
        Assert.Equal(restoredAttacker.InstanceId, playerLog.PlayerLogSemantic?.SourceInstanceId);
        Assert.Equal(restoredAttacker.InstanceId, playerLog.PlayerLogSemantic?.TargetInstanceId);
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
        var game = new L12GameEngine(Catalog, "back-row-attack-set", "BACK-ROW-SET", seed,
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
