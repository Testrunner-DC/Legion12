using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SummonTurnCounterProtectionLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData("S01-0201")]
    [InlineData("S01-0202")]
    [InlineData("ST02-01")]
    [L12AbilityEvidence("S01-0201:ability:static:7d31de8999ce168a", "duplicate-submit", "reconnect", "reconnect-derived-state")]
    [L12AbilityEvidence("S01-0202:ability:static:76a4a87caae11a73", "duplicate-submit", "reconnect", "reconnect-derived-state")]
    [L12AbilityEvidence("ST02-01:ability:continuous:42ada4e462a2fb94", "duplicate-submit", "reconnect", "reconnect-derived-state")]
    public void AllowedResponsePromptSurvivesRestoreAndCannotBeSubmittedTwice(string sourceCardId)
    {
        var game = Create(72320 + sourceCardId.Length);
        var owner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var source = Card(sourceCardId, $"protected-{sourceCardId}");
        var response = Card("S01-0019", $"allowed-response-{sourceCardId}");
        var responseTarget = Card("S01-0001", $"response-target-{sourceCardId}");
        owner.Field[0][0] = source;
        opponent.Field[0][0] = responseTarget;
        response.Hidden = true;
        response.SetRound = 0;
        opponent.Field[1][0] = response;
        opponent.Hand.Add(Card("S01-0005", $"discard-{sourceCardId}"));
        source.SummonRound = game.State.Round;

        PushEffect(game, source);
        if (game.State.PendingPrompts.FirstOrDefault(prompt => prompt.PlayerIndex == 0 && prompt.Kind == "response") is { } ownerPrompt)
            Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: ownerPrompt.PromptId, Choice: "pass")).Accepted);

        var beforeRestore = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.PlayerIndex == 1 && prompt.Kind == "response");
        Assert.Contains(response.InstanceId, beforeRestore.ValidChoices);

        game = Restore(game);
        var restored = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.PlayerIndex == 1 && prompt.Kind == "response");
        Assert.Equal(beforeRestore.PromptId, restored.PromptId);
        Assert.Contains(response.InstanceId, restored.ValidChoices);

        var pass = new L12Command("resolvePrompt", PromptId: restored.PromptId, Choice: "pass");
        Assert.True(game.Handle(1, pass).Accepted);
        var afterFirst = game.SerializeFullState();
        Assert.False(game.Handle(1, pass).Accepted);
        Assert.Equal(afterFirst, game.SerializeFullState());
    }

    private static void PushEffect(L12GameEngine game, L12CardInstance source)
    {
        var method = typeof(L12GameEngine).GetMethod("PushEffect", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(L12GameEngine), "PushEffect");
        method.Invoke(game, [0, source, "enter", "受保护的登场时效果", null,
            new Dictionary<string, string>()]);
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "summon-turn-protection", "SUMMON-PROTECTION", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
        }
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static L12CardInstance Card(string cardId, string instanceId)
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
            OwnerIndex = 0,
        };
    }
}
