using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class GrantedFrontRowTauntOnKillLifecycleProfileTests
{
    private const string AbilityId = "S02-0503:ability:granted-static:e67d03cee97f98a6";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence(AbilityId, "normal", "target-invalidated", "reconnect", "presentation-consumers")]
    public void AchillesKillGrantSurvivesCombatRestoreAndStillRequiresTheCurrentFrontRow()
    {
        var game = Create(72440);
        var achilles = Card("S02-0503", "achilles-kill-grant");
        var victim = Card("S02-0005", "achilles-kill-victim", 1000, ownerIndex: 1);
        achilles.SummonRound = -1;
        victim.SummonRound = -1;
        game.State.Players[0].Field[0][0] = achilles;
        game.State.Players[1].Field[0][0] = victim;

        var attack = game.Handle(0, new L12Command("attack", achilles.InstanceId,
            Target: new L12AttackTarget("legion", victim.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);
        game = Restore(game);
        ResolveCombatAndResponses(game);

        var restoredAchilles = game.State.Players[0].Field[0][0]!;
        Assert.True(game.State.Players[1].Graveyard.Any(card => card.InstanceId == victim.InstanceId),
            $"field={string.Join(',', game.State.Players[1].Field.SelectMany(row => row).Where(card => card is not null).Select(card => $"{card!.InstanceId}:{card.Troops}"))}; "
            + $"grave={string.Join(',', game.State.Players[1].Graveyard.Select(card => card.InstanceId))}; "
            + $"events={string.Join(" | ", game.State.Events.Select(entry => $"{entry.Type}:{entry.Text}"))}");
        Assert.True(restoredAchilles.TauntUntilTurn > game.State.TurnSerial);
        Assert.True(restoredAchilles.TauntRequiresFrontRow);
        Assert.True(L12StructuredCardRules.HasTaunt(restoredAchilles, 0));
        Assert.Contains(ActiveKeywords(game, 0), keyword => keyword == "挑衅");
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("因击杀军团", StringComparison.Ordinal));

        game.State.Players[0].Field[0][0] = null;
        game.State.Players[0].Field[1][0] = restoredAchilles;
        game.State.Revision++;
        Assert.False(L12StructuredCardRules.HasTaunt(restoredAchilles, 1));
        Assert.DoesNotContain(ActiveKeywords(game, 1), keyword => keyword == "挑衅");
    }

    private static void ResolveCombatAndResponses(L12GameEngine game)
    {
        for (var count = 0; count < 50; count++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                var choice = prompt.Kind == "response" ? "pass"
                    : prompt.ValidChoices.Contains("no") ? "no"
                    : prompt.ValidChoices.Contains("skip") ? "skip"
                    : prompt.ValidChoices.FirstOrDefault() ?? "pass";
                var result = game.Handle(prompt.PlayerIndex,
                    new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
                Assert.True(result.Accepted, result.Error);
                continue;
            }
            if (game.State.PendingDefense is not null)
            {
                var result = game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: []));
                Assert.True(result.Accepted, result.Error);
                continue;
            }
            break;
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Null(game.State.PendingDefense);
        Assert.Empty(game.State.EffectStack);
    }

    private static string[] ActiveKeywords(L12GameEngine game, int row)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return snapshot.GetProperty("players")[0].GetProperty("field")[row][0]
            .GetProperty("activeKeywords").EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "granted-front-row-taunt", "GRANTED-TAUNT", seed,
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

    private static L12CardInstance Card(string cardId, string instanceId, int? troops = null, int ownerIndex = 0)
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
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            OwnerIndex = ownerIndex,
        };
    }
}
