using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TutorialResponseLessonTests
{
    [Fact]
    public async Task ResponseStartsFromLegalCommittedPublicRecipeWithOrdinaryPrivacy()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var initial = await f.Start("response");
        Assert.Equal("response-artifact", Step(initial));
        Assert.Equal("active", initial.GetProperty("tutorial").GetProperty("status").GetString());
        Assert.Equal("public-response-v1", initial.GetProperty("tutorial").GetProperty("recipeId").GetString());
        Assert.False(initial.GetProperty("gmEnabled").GetBoolean());
        Assert.False(initial.GetProperty("state").GetProperty("players")[1].TryGetProperty("hand", out _));
        var durable = (await f.Recorder.LoadJournalEngineAsync(Match(initial)))!;
        Assert.Equal(0, durable.CommandSequence);
        Assert.Equal(f.RuntimeGame().ComputeStateHash(), durable.Engine.ComputeStateHash());
        Assert.Equal("S01-0117", durable.Engine.State.Players[0].Relic?.CardId);
        Assert.Contains(durable.Engine.State.Players[0].Hand, c => c.CardId == "ST01-10");
        Assert.Equal(2, durable.Engine.State.Players[0].Hand.Count);
        Assert.True(durable.Engine.State.Players[0].Morale.Count(c => !c.Tapped) >= 3);
        Assert.Empty(await f.Recorder.ListMatchesForAccountAsync(f.AccountId, "learner"));
    }

    [Fact]
    public async Task RealAfterAttackCounterKeepsDamageCostsAndDuplicateFinalRequestDoesNotAdvance()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var initial = await f.Start("response");
        var hp = f.RuntimeGame().State.Players[0].Hp;
        var cover = await ReachCover(f, initial);
        var counter = f.RuntimeGame().State.Players[0].Hand.Single(c => c.CardId == "ST01-10");
        await Cover(f, counter.InstanceId, true);
        var window = await Window(f);
        Assert.Equal(hp - 1, f.RuntimeGame().State.Players[0].Hp);
        var returned = await Choice(f, window, "mode:use", "counter-use");
        Assert.Equal("response-return", Step(returned));
        var own = f.RuntimeGame().State.Players[0];
        var returnId = Prompt(returned).GetProperty("validChoices").EnumerateArray().Select(v => v.GetString()!)
            .First(id => own.Morale.Any(resource => resource.InstanceId == id));
        var entrant = await Choice(f, returned, returnId, "counter-return");
        var entrantId = entrant.GetProperty("tutorial").GetProperty("focus").GetProperty("legionInstanceId").GetString()!;
        var position = await Choice(f, entrant, entrantId, "counter-entry");
        var positionPrompt = Prompt(position).GetProperty("promptId").GetString()!;
        var final = await Choice(f, position, "0:0", "counter-position");
        final = await Pass(f, final);
        Assert.Equal("completed", Step(final));
        own = f.RuntimeGame().State.Players[0];
        Assert.Equal(hp - 1, own.Hp);
        Assert.Contains(own.MoraleDeck, resource => resource.InstanceId == returnId);
        Assert.Single(own.Graveyard.Where(card => card.InstanceId == counter.InstanceId));
        Assert.Single(own.Field.SelectMany(row => row).Where(card => card?.InstanceId == entrantId && !card.Tapped));
        Assert.DoesNotContain(f.RuntimeGame().State.Events, e => e.EffectResultStatus == "negated"
            && e.Cards.Any(card => card.CardId == "ST04-09"));
        var finalHash = f.RuntimeGame().ComputeStateHash();
        var sequence = (await f.Recorder.LoadJournalEngineAsync(Match(initial)))!.CommandSequence;
        await Send(f, new("resolvePrompt", PromptId: positionPrompt, CardInstanceIds: ["0:0"]), "counter-position");
        Assert.Equal(finalHash, f.RuntimeGame().ComputeStateHash());
        Assert.Equal(sequence, (await f.Recorder.LoadJournalEngineAsync(Match(initial)))!.CommandSequence);
        Assert.Empty(await f.Recorder.ListMatchesForAccountAsync(f.AccountId, "learner"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryAutoPaymentAndExplicitTwoMoraleKeepRulesAndWrongPositionNeverWrites(bool explicitPayment)
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var initial = await f.Start("response");
        await ReachCover(f, initial);
        var own = f.RuntimeGame().State.Players[0];
        var counter = own.Hand.Single(card => card.CardId == "ST01-10");
        var before = f.RuntimeGame().ComputeStateHash();
        Assert.DoesNotContain(await f.Act(new("playCard", counter.InstanceId, 0, 0), "wrong-row"), IsGame);
        Assert.Equal(before, f.RuntimeGame().ComputeStateHash());
        var single = own.Morale.Where(card => !card.Tapped).Take(1).Select(card => card.InstanceId).ToList();
        Assert.DoesNotContain(await f.Act(new("playCard", counter.InstanceId, 1, 2,
            CardInstanceIds: single), "insufficient-cover"), IsGame);
        Assert.Equal(before, f.RuntimeGame().ComputeStateHash());
        await Cover(f, counter.InstanceId, explicitPayment);
        Assert.True(f.RuntimeGame().State.Players[0].Field[1][2]!.Hidden);
        Assert.Equal(2, f.RuntimeGame().State.Players[0].Morale.Count(card => card.Tapped));
    }

    [Fact]
    public async Task DecliningShowsActualOtherResultAndCannotCompleteBySurrender()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        await ReachCover(f, await f.Start("response"));
        var counter = f.RuntimeGame().State.Players[0].Hand.Single(card => card.CardId == "ST01-10");
        await Cover(f, counter.InstanceId, true);
        var window = await Window(f);
        var result = await Pass(f, await Choice(f, window, "mode:none", "decline-counter"));
        Assert.Equal("response-retry", Step(result));
        Assert.Equal("active", result.GetProperty("tutorial").GetProperty("status").GetString());
        Assert.Contains(f.RuntimeGame().State.Players[0].Field[1], card => card?.InstanceId == counter.InstanceId && card.Hidden);
        var surrender = await Send(f, new("surrender"), "surrender");
        Assert.Equal("active", surrender.GetProperty("tutorial").GetProperty("status").GetString());
        Assert.Equal(L12Phase.GameOver, f.RuntimeGame().State.Phase);
    }

    [Fact]
    public async Task ReconnectAndRepeatedReadsDoNotPlayOpponentOrChangePendingCounter()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        await ReachCover(f, await f.Start("response"));
        await Cover(f, f.RuntimeGame().State.Players[0].Hand.Single(card => card.CardId == "ST01-10").InstanceId, true);
        var window = await Window(f);
        var hash = f.RuntimeGame().ComputeStateHash();
        f.Manager.Disconnect(f.Student);
        var replacement = Guid.NewGuid();
        Assert.True((await f.Manager.ConnectAsync(replacement, f.AccountId, "learner")).Recovered);
        for (var i = 0; i < 2; i++)
        {
            var recovered = TutorialRoomLifecycleTests.Message(await f.Manager.RecoveryStateAsync(replacement), "gameState");
            Assert.Equal(window.GetProperty("tutorial").GetRawText(), recovered.GetProperty("tutorial").GetRawText());
            Assert.Equal(hash, f.RuntimeGame().ComputeStateHash());
        }
        Assert.DoesNotContain(await f.Act(new("surrender"), "stale-session"), IsGame);
        Assert.Equal(hash, f.RuntimeGame().ComputeStateHash());
        Assert.Contains(f.Manager.LeaveRoom(replacement), message => Type(message) == "roomLeft");
    }

    [Fact]
    public async Task FailedOpponentAppendFreezesAtLatestHumanCommitWithoutReplayingOrPublishingAttack()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var initial = await f.Start("response");
        await ReachCover(f, initial);
        await Cover(f, f.RuntimeGame().State.Players[0].Hand.Single(card => card.CardId == "ST01-10").InstanceId, true);
        var previous = (await f.Recorder.LoadJournalEngineAsync(Match(initial)))!;
        var writes = 0;
        f.Recorder.StorageFailureInjector = stage =>
        {
            if (stage == "before-match-command-commit" && ++writes == 2) throw new IOException("synthetic opponent append failure");
        };
        Assert.Contains(await f.Act(new("endTurn"), "latest-human"), message => Type(message) == "tutorialRestartRequired");
        f.Recorder.StorageFailureInjector = null;
        var durable = (await f.Recorder.LoadJournalEngineAsync(Match(initial)))!;
        Assert.Equal(previous.CommandSequence + 1, durable.CommandSequence);
        Assert.Null(durable.Engine.State.PendingDefense);
        Assert.Equal(durable.Engine.ComputeStateHash(), f.RuntimeGame().ComputeStateHash());
        Assert.Contains(await f.Manager.RecoveryStateAsync(f.Student), message => Type(message) == "tutorialRestartRequired");
        Assert.DoesNotContain(await f.Act(new("endTurn"), "must-not-replay"), IsGame);
        Assert.Equal(durable.Engine.ComputeStateHash(), f.RuntimeGame().ComputeStateHash());
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

    private static async Task<JsonElement> ReachCover(TutorialRoomLifecycleTests.Fixture f, JsonElement initial)
    {
        var artifactId = f.RuntimeGame().State.Players[0].Relic!.InstanceId;
        var cost = await Send(f, new("activateAbility", artifactId, Ability: "artifactSearch"), "search-start");
        var costId = cost.GetProperty("tutorial").GetProperty("focus").GetProperty("costCardInstanceId").GetString()!;
        var search = await Pass(f, await Choice(f, cost, costId, "search-cost"));
        var legionId = search.GetProperty("tutorial").GetProperty("focus").GetProperty("legionInstanceId").GetString()!;
        var order = await Choice(f, search, legionId, "search-pick");
        var prompt = Prompt(order);
        var covered = await Pass(f, await Send(f, new("resolvePrompt", PromptId: prompt.GetProperty("promptId").GetString(),
            TopCardInstanceIds: prompt.GetProperty("validChoices").EnumerateArray().Select(v => v.GetString()!).ToList(),
            BottomCardInstanceIds: []), "search-order"));
        Assert.Equal("response-cover", Step(covered));
        return covered;
    }
    private static async Task Cover(TutorialRoomLifecycleTests.Fixture f, string counterId, bool explicitPayment)
    {
        var payment = explicitPayment ? f.RuntimeGame().State.Players[0].Morale.Where(card => !card.Tapped)
            .Take(2).Select(card => card.InstanceId).ToList() : null;
        Assert.Equal("response-end-turn", Step(await Pass(f, await Send(f,
            new("playCard", counterId, 1, 2, CardInstanceIds: payment), "cover"))));
    }
    private static async Task<JsonElement> Window(TutorialRoomLifecycleTests.Fixture f)
    {
        var defense = await Pass(f, await Send(f, new("endTurn"), "end-turn"));
        Assert.Equal("response-defend", Step(defense));
        var window = await Pass(f, await Send(f, new("resolveDefense", CardInstanceIds: []), "no-block"));
        Assert.Equal("response-choose", Step(window));
        return window;
    }
    private static async Task<JsonElement> Choice(TutorialRoomLifecycleTests.Fixture f, JsonElement state, string choice, string id)
        => await Send(f, new("resolvePrompt", PromptId: Prompt(state).GetProperty("promptId").GetString(), CardInstanceIds: [choice]), id);
    private static async Task<JsonElement> Pass(TutorialRoomLifecycleTests.Fixture f, JsonElement state)
    {
        for (var count = 0; Step(state) == "response-pass"; count++)
        {
            Assert.InRange(count, 0, 15);
            state = await Choice(f, state, "pass", "pass-" + Guid.NewGuid().ToString("N"));
        }
        return state;
    }
    private static async Task<JsonElement> Send(TutorialRoomLifecycleTests.Fixture f, L12Command command, string request)
    {
        var message = TutorialRoomLifecycleTests.Message(await f.Act(command, request), "gameState");
        Assert.Equal(request, message.GetProperty("requestId").GetString());
        var durable = (await f.Recorder.LoadJournalEngineAsync(Match(message)))!;
        Assert.Equal(f.RuntimeGame().ComputeStateHash(), durable.Engine.ComputeStateHash());
        return message;
    }
    private static JsonElement Prompt(JsonElement message) => Assert.Single(message.GetProperty("state").GetProperty("prompts").EnumerateArray().ToArray());
    private static string? Type(OutgoingMessage message) => JsonSerializer.SerializeToElement(message.Payload).GetProperty("type").GetString();
    private static bool IsGame(OutgoingMessage message) => Type(message) == "gameState";
    private static string Step(JsonElement message) => message.GetProperty("tutorial").GetProperty("stepId").GetString()!;
    private static string Match(JsonElement message) => message.GetProperty("state").GetProperty("matchId").GetString()!;
}
