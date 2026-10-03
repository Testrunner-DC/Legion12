using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AchillesRangedDamageProjectionRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void RestoredRangedAttackProjectsExtraDamageRatherThanAnAchillesTroopsBuff()
    {
        var game = Create(72550);
        var attacker = Card("S01-0208", "achilles-projection-ranged-attacker", 0);
        var achilles = Card("S02-0503", "achilles-projection-target", 1);
        attacker.Troops = 4000;
        attacker.SummonRound = -1;
        achilles.Troops = 7000;
        achilles.SummonRound = -1;
        game.State.Players[0].Field[1][0] = attacker;
        game.State.Players[1].Field[0][0] = achilles;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", achilles.InstanceId))).Accepted);
        Assert.True(game.State.PendingDefense?.IsRanged);
        game = Restore(game);
        PassAll(game);

        var restoredAchilles = Assert.IsType<L12CardInstance>(game.State.Players[1].Field[0][0]);
        Assert.Equal(2000, restoredAchilles.Troops);
        Assert.Equal(5000, 7000 - restoredAchilles.Troops);
        Assert.Equal(4000, game.State.Players[0].Field[1][0]?.Troops);

        var adjustment = Assert.Single(game.State.Events, entry => entry.PlayerLogSemantic?.ActionLabel == "触发 远程伤害修正");
        Assert.Equal("受到的本次战斗伤害 4000→5000", adjustment.PlayerLogSemantic?.OutcomeLabel);
        Assert.Null(adjustment.PlayerTroopsModifier);
        Assert.DoesNotContain(game.State.Events, entry => entry.PlayerTroopsModifier?.TargetInstanceId == achilles.InstanceId
            && entry.PlayerTroopsModifier.TroopsDelta > 0);

        AssertSnapshotTroops(game.SnapshotFor(0), achilles.InstanceId, 2000);
        AssertSnapshotTroops(game.SnapshotFor(1), achilles.InstanceId, 2000);
    }

    private static void AssertSnapshotTroops(L12GameSnapshot snapshot, string instanceId, int expectedTroops)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(snapshot));
        var cards = document.RootElement.GetProperty("Players")[1].GetProperty("field")
            .EnumerateArray().SelectMany(row => row.EnumerateArray())
            .Where(card => card.ValueKind == JsonValueKind.Object
                && card.TryGetProperty("InstanceId", out var id) && id.GetString() == instanceId)
            .ToArray();
        var card = Assert.Single(cards);
        Assert.Equal(expectedTroops, card.GetProperty("Troops").GetInt32());
        Assert.Equal(5000, 7000 - card.GetProperty("Troops").GetInt32());
    }

    private static void PassAll(L12GameEngine game)
    {
        for (var count = 0; count < 150 && game.State.PendingDefense is not null; count++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                var choice = prompt.Kind == "response" ? "pass"
                    : prompt.ValidChoices.Contains("no") ? "no"
                    : prompt.ValidChoices.Contains("skip") ? "skip" : prompt.ValidChoices.First();
                var result = game.Handle(prompt.PlayerIndex,
                    new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
                Assert.True(result.Accepted, result.Error);
                continue;
            }
            if (game.State.PendingDefense.Stage == L12CombatStage.DefenseChoice)
            {
                var result = game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: []));
                Assert.True(result.Accepted, result.Error);
            }
        }
        Assert.Null(game.State.PendingDefense);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "achilles-ranged-projection", "ACHILLES-RANGED", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
        }
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static L12CardInstance Card(string cardId, string instanceId, int ownerIndex)
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
            OwnerIndex = ownerIndex,
        };
    }
}
