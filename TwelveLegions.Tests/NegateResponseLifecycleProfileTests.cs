using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class NegateResponseLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData("S01-0016", "normal")]
    [InlineData("S01-0016", "negated")]
    [InlineData("S01-0016", "target-missing")]
    [InlineData("S01-0018", "normal")]
    [InlineData("S01-0018", "negated")]
    [InlineData("S01-0018", "target-missing")]
    [L12AbilityEvidence("S01-0016:ability:reaction:eda8f9987e9ccfe3",
        "normal", "negated", "target-invalidated", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-0018:ability:reaction:248207b49df4bd77",
        "normal", "negated", "target-invalidated", "reconnect", "presentation-consumers")]
    public void NegateResponseKeepsDeclaredTargetAndOutcomeAcrossCheckpoint(string responseCardId, string outcome)
    {
        var game = Create(70320 + responseCardId[^1] + outcome.Length);
        var rootSource = Card("S01-0109", $"root-source-{responseCardId}-{outcome}", 0);
        game.State.Players[0].Field[0][0] = rootSource;
        var root = new L12StackItem
        {
            StackItemId = $"root-{responseCardId}-{outcome}",
            Controller = 0,
            SourceInstanceId = rootSource.InstanceId,
            SourceCardId = rootSource.CardId,
            SourceName = rootSource.Name,
            SourceSnapshot = rootSource.Clone(),
            Trigger = "enter",
            Text = "登场时 测试效果。",
        };
        game.State.EffectStack.Add(root);

        var response = Card(responseCardId, $"response-{responseCardId}-{outcome}", 1);
        response.Hidden = true;
        response.SetRound = 0;
        game.State.Players[1].Field[1][0] = response;
        Invoke(game, "CommitNegateResponse", 1, response, root.StackItemId);
        var responseItem = game.State.EffectStack[^1];
        Assert.Equal([root.StackItemId], responseItem.Targets);
        var declaration = Assert.Single(game.State.Events, action => action.EffectResultStatus == "declared"
            && action.Cards.Any(card => card.InstanceId == response.InstanceId));
        Assert.False(string.IsNullOrWhiteSpace(declaration.EffectSceneId));
        Assert.Contains(declaration.EffectSceneId!, JsonSerializer.Serialize(game.SnapshotFor(0)),
            StringComparison.Ordinal);
        Assert.Contains(declaration.EffectSceneId!, JsonSerializer.Serialize(game.SnapshotFor(1)),
            StringComparison.Ordinal);

        if (outcome == "negated") responseItem.Negated = true;
        if (outcome == "target-missing") game.State.EffectStack.Remove(root);
        game = Restore(game);
        game.State.PendingPrompts.Clear();
        game.State.ResponseWindow = null;
        Invoke(game, "ResolveTopStack");

        var result = Assert.Single(game.State.Events, action => action.Type == "effect-result"
            && action.Cards.Any(card => card.InstanceId == response.InstanceId));
        Assert.Equal(declaration.EffectSceneId, result.EffectSceneId);
        Assert.Equal(outcome switch
        {
            "normal" => "resolved",
            "negated" => "negated",
            _ => "failed",
        }, result.EffectResultStatus);
        if (outcome != "target-missing")
        {
            var restoredRoot = Assert.Single(game.State.EffectStack,
                item => item.StackItemId == root.StackItemId);
            Assert.Equal(outcome == "normal", restoredRoot.Negated);
        }
    }

    private static L12GameEngine Create(int seed)
    {
        var deck = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "negate-response-lifecycle", "NEGATE-RESPONSE", seed,
            ["甲", "乙"], [deck, deck], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
        }
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

    private static object? Invoke(object target, string method, params object?[] args)
    {
        var candidate = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(info => info.Name == method && info.GetParameters().Length == args.Length);
        return candidate.Invoke(target, args);
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            OwnerIndex = owner,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            SummonRound = -1,
        };
    }
}
