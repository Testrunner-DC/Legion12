using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TutorialResponseLessonTests
{
    [Fact]
    public async Task ResponseStartsFromLegalCommittedSceneWithOwnCardsAndOrdinaryPrivacy()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var initial = await f.Start("response");
        Assert.Equal("response-legion", Step(initial));
        Assert.Equal("active", initial.GetProperty("tutorial").GetProperty("status").GetString());
        Assert.False(initial.GetProperty("gmEnabled").GetBoolean());
        Assert.False(initial.GetProperty("state").GetProperty("players")[1].TryGetProperty("hand", out _));
        var durable = (await f.Recorder.LoadJournalEngineAsync(Match(initial)))!;
        Assert.Equal(0, durable.CommandSequence);
        Assert.Equal(Game(f).ComputeStateHash(), durable.Engine.ComputeStateHash());
        Assert.Contains(durable.Engine.State.Players[0].Hand, c => c.CardId == "S01-0116");
        Assert.Contains(durable.Engine.State.Players[0].Hand, c => c.CardId == "S01-0016");
        Assert.Contains(durable.Engine.State.Players[1].Hand, c => c.CardId == "S01-0008");
        Assert.True(durable.Engine.State.Players[0].Morale.Count(c => !c.Tapped) >= 3);
        Assert.Empty(await f.Recorder.ListMatchesForAccountAsync(f.AccountId, "learner"));
    }

    [Fact]
    public async Task RealCounterPaysTwoMoraleAndDiscardThenNegatesOnceWithoutRefundOrDuplicateAdvance()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var initial = await f.Start("response");
        var game = Game(f);
        var legion = game.State.Players[0].Hand.First(c => c.CardId == "S01-0116");
        var counter = game.State.Players[0].Hand.First(c => c.CardId == "S01-0016");
        var siege = game.State.Players[1].Hand.First(c => c.CardId == "S01-0008");
        var beforeTroops = legion.Troops;
        var beforeTapped = game.State.Players[0].Morale.Count(c => c.Tapped);
        await Prepare(f);
        Assert.Equal(beforeTapped + 3, game.State.Players[0].Morale.Count(c => c.Tapped));
        var setRound = counter.SetRound;
        var window = await Send(f, new("endTurn"), "end-turn");
        Assert.Equal("response-choose", Step(window));
        Assert.True(game.State.Round > setRound);
        var beforeDuplicate = game.ComputeStateHash();
        await Send(f, new("endTurn"), "end-turn");
        Assert.Equal(beforeDuplicate, game.ComputeStateHash());
        Assert.Equal(beforeTroops, legion.Troops);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, response.PlayerIndex);
        Assert.Contains(counter.InstanceId, response.ValidChoices);
        var cost = await Send(f, new("resolvePrompt", PromptId: response.PromptId,
            CardInstanceIds: [counter.InstanceId]), "counter");
        Assert.Equal("response-discard", Step(cost));
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("discard-cost", payment.Kind);
        var discard = game.State.Players[0].Hand.First(c => payment.ValidChoices.Contains(c.InstanceId));
        var tappedAtDiscard = game.State.Players[0].Morale.Count(c => c.Tapped);
        var final = await Send(f, new("resolvePrompt", PromptId: payment.PromptId,
            CardInstanceIds: [discard.InstanceId]), "discard");
        Assert.Equal("completed", Step(final));
        Assert.Equal(beforeTroops, legion.Troops);
        Assert.Equal(tappedAtDiscard, game.State.Players[0].Morale.Count(c => c.Tapped));
        Assert.Equal(2, game.State.Players[1].Morale.Count(c => c.Tapped));
        Assert.Single(game.State.Players[0].Graveyard.Where(c => c.InstanceId == discard.InstanceId));
        Assert.Single(game.State.Players[0].Graveyard.Where(c => c.InstanceId == counter.InstanceId));
        Assert.Single(game.State.Players[1].Graveyard.Where(c => c.InstanceId == siege.InstanceId));
        Assert.Single(game.State.Events.Where(e => e.Type == "effect-result" && e.EffectResultStatus == "negated"
            && e.Cards.FirstOrDefault()?.InstanceId == siege.InstanceId));
        Assert.Empty(game.State.PendingPrompts); Assert.Empty(game.State.EffectStack);
        var finalHash = game.ComputeStateHash();
        var sequence = (await f.Recorder.LoadJournalEngineAsync(Match(initial)))!.CommandSequence;
        Assert.True(sequence >= 6);
        await Send(f, new("resolvePrompt", PromptId: payment.PromptId, CardInstanceIds: [discard.InstanceId]), "discard");
        Assert.Equal(finalHash, game.ComputeStateHash());
        Assert.Equal(sequence, (await f.Recorder.LoadJournalEngineAsync(Match(initial)))!.CommandSequence);
        Assert.Empty(await f.Recorder.ListMatchesForAccountAsync(f.AccountId, "learner"));
    }

    [Fact]
    public async Task OrdinaryAutoPaymentAndExplicitTwoMoraleKeepRulesAndWrongPositionNeverWrites()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        await f.Start("response");
        var game = Game(f);
        var legion = game.State.Players[0].Hand.First(c => c.CardId == "S01-0116");
        var before = game.ComputeStateHash();
        Assert.DoesNotContain(await f.Act(new("playCard", legion.InstanceId, 1, 0), "wrong-row"), IsGame);
        Assert.Equal(before, game.ComputeStateHash());
        var paid = await Send(f, new("playCard", legion.InstanceId, 0, 0), "legion-start");
        Assert.Equal("response-cover", Step(paid));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Single(game.State.Players[0].Morale.Where(c => c.Tapped));
        var counter = game.State.Players[0].Hand.First(c => c.CardId == "S01-0016");
        var paymentHash = game.ComputeStateHash();
        Assert.DoesNotContain(await f.Act(new("playCard", counter.InstanceId, 1, 2,
            CardInstanceIds: Ready(game, 0, 1)), "cover-insufficient"), IsGame);
        Assert.Equal(paymentHash, game.ComputeStateHash());
        var covered = await Send(f, new("playCard", counter.InstanceId, 1, 2,
            CardInstanceIds: Ready(game, 0, 2)), "cover-paid");
        Assert.Equal("response-end-turn", Step(covered));
        Assert.True(game.State.Players[0].Field[1][2]!.Hidden);
        Assert.Equal(3, game.State.Players[0].Morale.Count(c => c.Tapped));
    }

    [Fact]
    public async Task PassingShowsActualOtherResultAndCannotCompleteBySurrender()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        await f.Start("response"); await Prepare(f);
        await Send(f, new("endTurn"), "end");
        var game = Game(f);
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = await Send(f, new("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: ["pass"]), "pass");
        Assert.Equal("response-retry", Step(result));
        Assert.Equal("active", result.GetProperty("tutorial").GetProperty("status").GetString());
        Assert.Contains(game.State.Players[0].Field[1], c => c?.CardId == "S01-0016" && c.Hidden);
        Assert.DoesNotContain(game.State.Events, e => e.EffectResultStatus == "negated");
        var surrender = await Send(f, new("surrender"), "surrender");
        Assert.Equal("active", surrender.GetProperty("tutorial").GetProperty("status").GetString());
        Assert.Equal(L12Phase.GameOver, game.State.Phase);
    }

    [Fact]
    public async Task ReconnectAndRepeatedReadsDoNotPlayOpponentOrChangeThePendingCounter()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        await f.Start("response"); await Prepare(f);
        var window = await Send(f, new("endTurn"), "end");
        var hash = Game(f).ComputeStateHash();
        f.Manager.Disconnect(f.Student);
        var replacement = Guid.NewGuid();
        Assert.True((await f.Manager.ConnectAsync(replacement, f.AccountId, "learner")).Recovered);
        for (var i = 0; i < 2; i++)
        {
            var recovered = TutorialRoomLifecycleTests.Message(await f.Manager.RecoveryStateAsync(replacement), "gameState");
            Assert.Equal(window.GetProperty("tutorial").GetRawText(), recovered.GetProperty("tutorial").GetRawText());
            Assert.Equal(hash, Game(f).ComputeStateHash());
        }
        Assert.DoesNotContain(await f.Act(new("surrender"), "stale-session"), IsGame);
        Assert.Equal(hash, Game(f).ComputeStateHash());
        Assert.Contains(f.Manager.LeaveRoom(replacement), m => Type(m) == "roomLeft");
    }

    [Fact]
    public async Task FailedOpponentAppendFreezesAtLatestHumanCommitWithoutReplayingOrPublishingSiege()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var initial = await f.Start("response"); await Prepare(f);
        var writes = 0;
        f.Recorder.StorageFailureInjector = stage =>
        {
            if (stage == "before-match-command-commit" && ++writes == 2)
                throw new IOException("synthetic response opponent append failure");
        };
        var rejected = await f.Act(new("endTurn"), "human-end-committed");
        Assert.Contains(rejected, m => Type(m) == "tutorialRestartRequired");
        f.Recorder.StorageFailureInjector = null;
        var durable = (await f.Recorder.LoadJournalEngineAsync(Match(initial)))!;
        Assert.Equal(3, durable.CommandSequence);
        Assert.Equal(1, durable.Engine.State.ActivePlayer);
        Assert.Contains(durable.Engine.State.Players[1].Hand, c => c.CardId == "S01-0008");
        var hash = durable.Engine.ComputeStateHash();
        Assert.Equal(hash, Game(f).ComputeStateHash());
        var recovered = await f.Manager.RecoveryStateAsync(f.Student);
        Assert.Contains(recovered, m => Type(m) == "tutorialRestartRequired");
        Assert.Equal("response-end-turn", Step(TutorialRoomLifecycleTests.Message(recovered, "gameState")));
        Assert.DoesNotContain(await f.Act(new("endTurn"), "must-not-replay"), IsGame);
        Assert.Equal(hash, Game(f).ComputeStateHash());
        Assert.Contains(f.Manager.LeaveRoom(f.Student), m => Type(m) == "roomLeft");
    }

    [Fact]
    public void WireAcceptsResponseOnlyWithTheOriginalStrictRequestShape()
    {
        Assert.True(L12WebSocketServer.TryReadTutorialRequest(JsonSerializer.SerializeToElement(new
            { type = "createTutorial", lessonId = "response", requestId = "response-wire" }), out var lesson, out _));
        Assert.Equal("response", lesson);
        Assert.False(L12WebSocketServer.TryReadTutorialRequest(JsonSerializer.SerializeToElement(new
            { type = "createTutorial", lessonId = "response", requestId = "response-wire", opponentCard = "S01-0008" }), out _, out _));
        Assert.False(L12WebSocketServer.TryReadTutorialRequest(JsonSerializer.SerializeToElement(new
            { type = "createTutorial", lessonId = "response-other", requestId = "response-wire" }), out _, out _));
    }

    private static List<string> Ready(L12GameEngine game, int actor, int count) => game.State.Players[actor]
        .Morale.Where(c => !c.Tapped).Take(count).Select(c => c.InstanceId).ToList();
    private static async Task Prepare(TutorialRoomLifecycleTests.Fixture f)
    {
        var game = Game(f);
        var legion = game.State.Players[0].Hand.First(c => c.CardId == "S01-0116");
        Assert.Equal("response-cover", Step(await Send(f, new("playCard", legion.InstanceId, 0, 0,
            CardInstanceIds: Ready(game, 0, 1)), "legion")));
        var counter = game.State.Players[0].Hand.First(c => c.CardId == "S01-0016");
        Assert.Equal("response-end-turn", Step(await Send(f, new("playCard", counter.InstanceId, 1, 2,
            CardInstanceIds: Ready(game, 0, 2)), "cover")));
    }
    private static async Task<JsonElement> Send(TutorialRoomLifecycleTests.Fixture f, L12Command command, string request)
    {
        var message = TutorialRoomLifecycleTests.Message(await f.Act(command, request), "gameState");
        Assert.Equal(request, message.GetProperty("requestId").GetString());
        var durable = (await f.Recorder.LoadJournalEngineAsync(Match(message)))!;
        Assert.Equal(Game(f).ComputeStateHash(), durable.Engine.ComputeStateHash());
        return message;
    }
    private static string? Type(OutgoingMessage message) => JsonSerializer.SerializeToElement(message.Payload)
        .GetProperty("type").GetString();
    private static bool IsGame(OutgoingMessage message) => Type(message) == "gameState";

    private static string Step(JsonElement message) => message.GetProperty("tutorial").GetProperty("stepId").GetString()!;
    private static string Match(JsonElement message) => message.GetProperty("state").GetProperty("matchId").GetString()!;
    private static L12GameEngine Game(TutorialRoomLifecycleTests.Fixture f)
    {
        var room = f.RuntimeRoom();
        return (L12GameEngine)room.GetType().GetProperty("Game", BindingFlags.Public | BindingFlags.Instance)!.GetValue(room)!;
    }
}
