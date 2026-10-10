using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SequentialCardOperationTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12CardInstance Card(string id) => new()
    {
        InstanceId = id, CardId = "S01-0003", Name = id, CardType = "legion", Faction = "common", OwnerIndex = 0,
    };

    private static L12GameEngine Game(int available)
    {
        var game = new L12GameEngine(Catalog, "sequential-cards", "SEQUENTIAL", 31701,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.Phase = L12Phase.Main;
        game.State.ActivePlayer = 0;
        game.State.PendingPrompts.Clear();
        foreach (var player in game.State.Players)
        {
            player.Library.Clear(); player.Hand.Clear(); player.Graveyard.Clear(); player.Resolving.Clear();
            player.Field[0] = new L12CardInstance?[3]; player.Field[1] = new L12CardInstance?[3];
        }
        game.State.Players[0].Library.AddRange(Enumerable.Range(0, available).Select(index => Card($"step-{index}")));
        return game;
    }

    [Theory]
    [InlineData("draw", 0, 0)]
    [InlineData("draw", 0, 1)]
    [InlineData("draw", 1, 2)]
    [InlineData("draw", 2, 2)]
    [InlineData("draw", 3, 2)]
    [InlineData("mill", 0, 0)]
    [InlineData("mill", 0, 1)]
    [InlineData("mill", 1, 2)]
    [InlineData("mill", 2, 2)]
    [InlineData("mill", 3, 2)]
    public void SharedLibraryOperationsPreserveEachCompletedStep(string operation, int available, int requested)
    {
        var game = Game(available);
        var player = game.State.Players[0];
        var result = operation == "draw" ? L12LibraryOps.Draw(player, requested) : L12LibraryOps.Mill(player, requested);
        var completed = Math.Min(available, requested);
        Assert.Equal(requested <= available, result.Success);
        Assert.Equal(Enumerable.Range(0, completed).Select(index => $"step-{index}"), result.Cards.Select(card => card.InstanceId));
        Assert.Equal(available - completed, player.Library.Count);
        Assert.Equal(completed, (operation == "draw" ? player.Hand : player.Graveyard).Count);
        Assert.Equal(available, player.Library.Count + player.Hand.Count + player.Graveyard.Count);
    }

    [Theory]
    [InlineData("Draw", 0, 0)]
    [InlineData("Draw", 0, 1)]
    [InlineData("Draw", 1, 2)]
    [InlineData("Draw", 2, 2)]
    [InlineData("Mill", 0, 0)]
    [InlineData("Mill", 0, 1)]
    [InlineData("Mill", 1, 2)]
    [InlineData("Mill", 2, 2)]
    public void EnginePublishesCompletedCardsBeforeDeckLoss(string operation, int available, int requested)
    {
        var game = Game(available);
        var player = game.State.Players[0];
        var source = Card("operation-source");
        player.Resolving.Add(source);
        var origin = new L12StackItem
        {
            StackItemId = "operation-origin", Controller = 0, SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId, SourceName = source.Name, SourceSnapshot = source,
            Trigger = "active", Text = "逐张牌库动作",
        };
        game.State.EffectStack.Add(origin);
        game.State.IsResolvingStack = true;
        var eventStart = game.State.Events.Count;
        var factStart = game.CardFactSignals.Count;
        typeof(L12GameEngine).GetMethod(operation, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [player, requested, operation == "Draw" ? (object)true : "逐张测试"]);
        var completed = Math.Min(available, requested);
        Assert.Equal(completed, (operation == "Draw" ? player.Hand : player.Graveyard).Count);
        Assert.Equal(available - completed, player.Library.Count);
        Assert.Equal(requested > available ? 1 : (int?)null, game.State.Winner);
        var events = game.State.Events.Skip(eventStart).ToArray();
        var moves = events.Where(entry => entry.Type == (operation == "Draw" ? "draw" : "mill")).ToArray();
        Assert.Equal(completed > 0 ? 1 : 0, moves.Length);
        if (operation == "Mill")
            Assert.Equal(Enumerable.Range(0, completed).Select(index => $"step-{index}"),
                moves.SelectMany(entry => entry.Cards).Select(card => card.InstanceId));
        else
            Assert.Equal(Enumerable.Range(0, completed).Select(index => $"step-{index}"),
                game.CardFactSignals.Skip(factStart).Where(fact => fact.Kind == "draw").Select(fact => fact.CardInstanceId));
        if (requested > available)
        {
            var loss = Assert.Single(events, entry => entry.Type == "game-over");
            Assert.All(moves, move => Assert.True(move.Sequence < loss.Sequence));
            Assert.Equal("failed", origin.Data.GetValueOrDefault("effectResultStatus"));
            Assert.Empty(game.State.PendingPrompts);
            Assert.Empty(game.State.EffectStack);
        }
    }

    [Theory]
    [InlineData("draw", 2)]
    [InlineData("draw", 3)]
    [InlineData("mill", 2)]
    [InlineData("mill", 3)]
    public void EachMovedCardIsObservedBeforeTheNextMoveEvenWithMergedLogs(string operation, int requested)
    {
        var game = Game(2);
        var player = game.State.Players[0];
        var observed = new List<(string Id, int Library, int Destination)>();
        void Observe(L12CardInstance card) => observed.Add((card.InstanceId, player.Library.Count,
            (operation == "draw" ? player.Hand : player.Graveyard).Count));
        var result = operation == "draw" ? L12LibraryOps.Draw(player, requested, Observe)
            : L12LibraryOps.Mill(player, requested, Observe);
        Assert.Equal(new[] { ("step-0", 1, 1), ("step-1", 0, 2) }, observed);
        Assert.Equal(requested == 2, result.Success);
        Assert.Equal(2, result.Cards.Count);
    }

    [Theory]
    [InlineData("Draw")]
    [InlineData("Mill")]
    public void RecoveryKeepsCompletedCardAndCannotRepeatTheFinishedOperation(string operation)
    {
        var game = Game(1);
        typeof(L12GameEngine).GetMethod(operation, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [game.State.Players[0], 2, operation == "Draw" ? (object)true : "重连边界"]);
        Assert.Equal(1, game.State.Winner);
        var eventCount = game.State.Events.Count;
        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 2, 3, 4, 5, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.Equal("step-0", Assert.Single(operation == "Draw" ? game.State.Players[0].Hand
            : game.State.Players[0].Graveyard).InstanceId);
        Assert.Empty(game.State.Players[0].Library);
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: "finished-operation", Choice: "pass")).Accepted);
        Assert.Equal(eventCount, game.State.Events.Count);
        Assert.Single(game.State.Events, entry => entry.Type == "game-over");
    }
}
