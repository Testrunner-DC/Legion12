using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TutorialPublicResponseRoomTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task PublicRecipeUsesRealRoomCommandsAndReloadsExactCompletedJournal()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var message = await Start(f);
        var state = message.GetProperty("state");
        var tutorial = message.GetProperty("tutorial");
        Assert.Equal(2, tutorial.GetProperty("version").GetInt32());
        Assert.Equal("public-response-v1", tutorial.GetProperty("recipeId").GetString());
        Assert.Equal("response-artifact", Step(message));
        Assert.False(message.GetProperty("gmEnabled").GetBoolean());
        Assert.False(state.GetProperty("players")[1].TryGetProperty("hand", out _));
        Assert.Equal(2, state.GetProperty("players")[0].GetProperty("hand").GetArrayLength());

        var initial = f.RuntimeGame();
        var own = initial.State.Players[0];
        var opponent = initial.State.Players[1];
        Assert.Equal("ST01-M1", own.MasterId);
        Assert.Equal("ST04-M1", opponent.MasterId);
        Assert.Empty(FieldLegions(own));
        var focus = tutorial.GetProperty("focus");
        var artifactId = RequiredString(focus, "artifactInstanceId");
        var counterId = RequiredString(focus, "counterInstanceId");
        Assert.Equal("S01-0117", own.Relic?.CardId);
        Assert.Equal(artifactId, own.Relic?.InstanceId);
        Assert.Equal("ST01-10", own.Hand.Single(card => card.InstanceId == counterId).CardId);
        Assert.Equal(JsonValueKind.Null, focus.GetProperty("costCardInstanceId").ValueKind);
        Assert.Equal(JsonValueKind.Null, focus.GetProperty("legionInstanceId").ValueKind);
        var boar = Assert.Single(FieldLegions(opponent));
        Assert.Equal("ST04-09", boar.CardId);
        Assert.DoesNotContain(await f.Manager.HandleGmActionAsync(f.Student,
            JsonSerializer.SerializeToElement(new { type = "addMorale", targetPlayer = 0, value = 9 })), IsGame);

        var activate = new L12Command("activateAbility", artifactId, Ability: "artifactSearch");
        message = await Act(f, activate, "response-activate");
        Assert.Equal("response-search-cost", Step(message));
        var costId = RequiredString(message.GetProperty("tutorial").GetProperty("focus"), "costCardInstanceId");
        Assert.NotEqual(counterId, costId);
        var matchId = state.GetProperty("matchId").GetString()!;
        var afterActivation = (await f.Recorder.LoadJournalEngineAsync(matchId))!;
        var activationHash = afterActivation.Engine.ComputeStateHash();
        var duplicateActivation = await Act(f, activate, "response-activate");
        Assert.Equal("response-search-cost", Step(duplicateActivation));
        Assert.Equal(afterActivation.CommandSequence,
            (await f.Recorder.LoadJournalEngineAsync(matchId))!.CommandSequence);
        Assert.Equal(activationHash, (await f.Recorder.LoadJournalEngineAsync(matchId))!.Engine.ComputeStateHash());

        message = await ResolveChoice(f, message, costId, "response-search-cost");
        message = await PassOnlyPromptsUntil(f, message, "response-search-pick");
        var entrantId = RequiredString(message.GetProperty("tutorial").GetProperty("focus"), "legionInstanceId");
        Assert.Equal("ST01-08", f.RuntimeGame().State.Players[0].Library
            .Single(card => card.InstanceId == entrantId).CardId);
        message = await ResolveChoice(f, message, entrantId, "response-search-pick");
        Assert.Equal("response-search-order", Step(message));
        var order = SinglePrompt(message);
        message = await Act(f, new("resolvePrompt", PromptId: PromptId(order),
            TopCardInstanceIds: order.GetProperty("validChoices").EnumerateArray()
                .Select(choice => choice.GetString()!).ToList(), BottomCardInstanceIds: []), "response-search-order");
        message = await PassOnlyPromptsUntil(f, message, "response-cover");

        var live = f.RuntimeGame();
        own = live.State.Players[0];
        Assert.Contains(own.Graveyard, card => card.InstanceId == costId);
        var counter = own.Hand.Single(card => card.InstanceId == counterId);
        var coverPayment = own.Morale.Where(card => !card.Tapped).Take(counter.CurrentCost)
            .Select(card => card.InstanceId).ToList();
        Assert.Equal(counter.CurrentCost, coverPayment.Count);
        var cover = new L12Command("playCard", counterId, 1, 2, CardInstanceIds: coverPayment);
        message = await Act(f, cover, "response-cover");
        message = await PassOnlyPromptsUntil(f, message, "response-end-turn");
        var afterCover = (await f.Recorder.LoadJournalEngineAsync(matchId))!;
        var coverHash = afterCover.Engine.ComputeStateHash();
        var duplicateCover = await Act(f, cover, "response-cover");
        Assert.Equal("response-end-turn", Step(duplicateCover));
        Assert.Equal(afterCover.CommandSequence, (await f.Recorder.LoadJournalEngineAsync(matchId))!.CommandSequence);
        Assert.Equal(coverHash, (await f.Recorder.LoadJournalEngineAsync(matchId))!.Engine.ComputeStateHash());

        var hpBefore = f.RuntimeGame().State.Players[0].Hp;
        message = await Act(f, new("endTurn"), "response-end-turn");
        message = await PassOnlyPromptsUntil(f, message, "response-defend");
        var afterOpponentScript = (await f.Recorder.LoadJournalEngineAsync(matchId))!;
        var opponentSequence = afterOpponentScript.CommandSequence;
        var opponentHash = afterOpponentScript.Engine.ComputeStateHash();
        var duplicateEnd = await Act(f, new("endTurn"), "response-end-turn");
        Assert.Equal("response-defend", Step(duplicateEnd));
        Assert.Equal(opponentSequence, (await f.Recorder.LoadJournalEngineAsync(matchId))!.CommandSequence);
        Assert.Equal(opponentHash, (await f.Recorder.LoadJournalEngineAsync(matchId))!.Engine.ComputeStateHash());

        message = await Act(f, new("resolveDefense", CardInstanceIds: []), "response-no-block");
        message = await PassOnlyPromptsUntil(f, message, "response-choose");
        Assert.Equal(hpBefore - 1, f.RuntimeGame().State.Players[0].Hp);
        message = await ResolveChoice(f, message, "mode:use", "response-use");
        Assert.Equal("response-return", Step(message));
        var returnPrompt = SinglePrompt(message);
        var returnedMorale = returnPrompt.GetProperty("validChoices").EnumerateArray()
            .Select(choice => choice.GetString()!)
            .First(id => f.RuntimeGame().State.Players[0].Morale
                .Any(card => card.InstanceId == id && !card.Tapped) && !coverPayment.Contains(id));
        message = await ResolveChoice(f, message, returnedMorale, "response-return");
        Assert.Equal("response-summon", Step(message));
        message = await ResolveChoice(f, message, entrantId, "response-summon");
        Assert.Equal("response-position", Step(message));
        message = await ResolveChoice(f, message, "0:0", "response-position");
        message = await PassOnlyPromptsUntil(f, message, "completed");

        live = f.RuntimeGame();
        own = live.State.Players[0];
        Assert.Equal(hpBefore - 1, own.Hp);
        Assert.Contains(own.Graveyard, card => card.InstanceId == costId);
        Assert.Contains(own.Graveyard, card => card.InstanceId == counterId);
        Assert.All(coverPayment, id => Assert.Contains(own.Morale, card => card.InstanceId == id && card.Tapped));
        Assert.Contains(own.MoraleDeck, card => card.InstanceId == returnedMorale);
        var entered = Assert.Single(FieldLegions(own));
        Assert.Equal(entrantId, entered.InstanceId);
        Assert.False(entered.Tapped);
        Assert.DoesNotContain(live.State.Events, entry => entry.Type == "effect-negated"
            && entry.Cards.Any(card => card.InstanceId == boar.InstanceId));
        AssertQueuesEmpty(live.State);
        var loaded = (await f.Recorder.LoadJournalEngineAsync(matchId))!;
        Assert.Equal(live.ComputeStateHash(), loaded.Engine.ComputeStateHash());
        AssertQueuesEmpty(loaded.Engine.State);
        Assert.Equal("completed", message.GetProperty("tutorial").GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReconnectKeepsExactKnownFocusWithoutProjectingFutureLibraryOrOpponentHand()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var initial = await Start(f);
        var artifact = RequiredString(initial.GetProperty("tutorial").GetProperty("focus"), "artifactInstanceId");
        var beforeHash = f.RuntimeGame().ComputeStateHash();
        f.Manager.Disconnect(f.Student);
        var replacement = Guid.NewGuid();
        Assert.True((await f.Manager.ConnectAsync(replacement, f.AccountId, "learner")).Recovered);
        var recovered = TutorialRoomLifecycleTests.Message(await f.Manager.RecoveryStateAsync(replacement), "gameState");
        Assert.Equal(beforeHash, f.RuntimeGame().ComputeStateHash());
        Assert.Equal("response-artifact", Step(recovered));
        Assert.Equal(artifact, RequiredString(recovered.GetProperty("tutorial").GetProperty("focus"), "artifactInstanceId"));
        Assert.Equal(JsonValueKind.Null,
            recovered.GetProperty("tutorial").GetProperty("focus").GetProperty("legionInstanceId").ValueKind);
        Assert.False(recovered.GetProperty("state").GetProperty("players")[1].TryGetProperty("hand", out _));
    }

    [Fact]
    public async Task DecliningHiddenPassKeepsDamageAndPaidCostsThenRequiresRestart()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var path = await ReachHiddenPassChoice(f);
        var hp = f.RuntimeGame().State.Players[0].Hp;
        var message = await ResolveChoice(f, path.Message, "mode:none", "response-decline");
        message = await PassOnlyPromptsUntil(f, message, "response-retry");
        var own = f.RuntimeGame().State.Players[0];
        Assert.Equal(hp, own.Hp);
        Assert.Contains(own.Graveyard, card => card.InstanceId == path.CostId);
        Assert.All(path.CoverPayment, id => Assert.Contains(own.Morale, card => card.InstanceId == id && card.Tapped));
        Assert.DoesNotContain(own.Graveyard, card => card.InstanceId == path.CounterId);
        Assert.Equal("active", message.GetProperty("tutorial").GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReturningOnePreviouslyPaidCoveredMoraleIsStillARealCompletion()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var path = await ReachHiddenPassChoice(f);
        var entrantId = RequiredString(path.Message.GetProperty("tutorial").GetProperty("focus"),
            "legionInstanceId");
        var returnedPayment = path.CoverPayment[0];
        var message = await ResolveChoice(f, path.Message, "mode:use", "paid-return-use");
        var returnPrompt = SinglePrompt(message);
        Assert.Contains(returnedPayment, returnPrompt.GetProperty("validChoices").EnumerateArray()
            .Select(choice => choice.GetString()));
        message = await ResolveChoice(f, message, returnedPayment, "paid-return-cost");
        message = await ResolveChoice(f, message, entrantId, "paid-return-summon");
        message = await ResolveChoice(f, message, "0:0", "paid-return-position");
        message = await PassOnlyPromptsUntil(f, message, "completed");
        var own = f.RuntimeGame().State.Players[0];
        Assert.Contains(own.MoraleDeck, card => card.InstanceId == returnedPayment);
        Assert.All(path.CoverPayment.Skip(1), id =>
            Assert.Contains(own.Morale, card => card.InstanceId == id && card.Tapped));
        AssertQueuesEmpty(f.RuntimeGame().State);
    }

    [Fact]
    public async Task HumanAppendFailureReloadsAndSameRequestCanRetryWithoutDoubleActivation()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var initial = await Start(f);
        var artifact = RequiredString(initial.GetProperty("tutorial").GetProperty("focus"), "artifactInstanceId");
        var initialHash = f.RuntimeGame().ComputeStateHash();
        var command = new L12Command("activateAbility", artifact, Ability: "artifactSearch");
        f.Recorder.StorageFailureInjector = stage =>
        {
            if (stage == "before-match-command-commit") throw new IOException("synthetic response human failure");
        };
        Assert.DoesNotContain(await f.Act(command, "response-retry-activate"), IsGame);
        f.Recorder.StorageFailureInjector = null;
        var recovered = TutorialRoomLifecycleTests.Message(await f.Manager.RecoveryStateAsync(f.Student), "gameState");
        Assert.Equal("response-artifact", Step(recovered));
        Assert.Equal(initialHash, f.RuntimeGame().ComputeStateHash());
        Assert.Equal("response-search-cost", Step(await Act(f, command, "response-retry-activate")));
    }

    [Fact]
    public async Task OpponentAppendFailureFreezesAtDurableEndTurnWithoutReplayingStudent()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var path = await ReachEndTurn(f);
        var writes = 0;
        f.Recorder.StorageFailureInjector = stage =>
        {
            if (stage == "before-match-command-commit" && ++writes == 2)
                throw new IOException("synthetic response opponent failure");
        };
        try
        {
            var failed = await f.Act(new("endTurn"), "response-failed-opponent");
            Assert.Contains(failed, message => MessageType(message) == "tutorialRestartRequired");
            var durable = (await f.Recorder.LoadJournalEngineAsync(path.MatchId))!;
            Assert.Equal(path.SequenceBefore + 1, durable.CommandSequence);
            Assert.Equal(1, durable.Engine.State.ActivePlayer);
            Assert.Null(durable.Engine.State.PendingDefense);
            Assert.Empty(durable.Engine.State.PendingPrompts);
            Assert.Contains(await f.Manager.RecoveryStateAsync(f.Student),
                message => MessageType(message) == "tutorialRestartRequired");
        }
        finally { f.Recorder.StorageFailureInjector = null; }
    }

    private static async Task<(JsonElement Message, string CounterId, string CostId,
        List<string> CoverPayment)> ReachHiddenPassChoice(TutorialRoomLifecycleTests.Fixture f)
    {
        var path = await ReachEndTurn(f);
        var message = await Act(f, new("endTurn"), "helper-end-turn");
        message = await PassOnlyPromptsUntil(f, message, "response-defend");
        message = await Act(f, new("resolveDefense", CardInstanceIds: []), "helper-no-block");
        message = await PassOnlyPromptsUntil(f, message, "response-choose");
        return (message, path.CounterId, path.CostId, path.CoverPayment);
    }

    private static async Task<(JsonElement Message, string CounterId, string CostId,
        List<string> CoverPayment, string MatchId, long SequenceBefore)> ReachEndTurn(
        TutorialRoomLifecycleTests.Fixture f)
    {
        var message = await Start(f);
        var matchId = message.GetProperty("state").GetProperty("matchId").GetString()!;
        var focus = message.GetProperty("tutorial").GetProperty("focus");
        var artifactId = RequiredString(focus, "artifactInstanceId");
        var counterId = RequiredString(focus, "counterInstanceId");
        message = await Act(f, new("activateAbility", artifactId, Ability: "artifactSearch"), "helper-activate");
        var costId = RequiredString(message.GetProperty("tutorial").GetProperty("focus"), "costCardInstanceId");
        message = await ResolveChoice(f, message, costId, "helper-cost");
        message = await PassOnlyPromptsUntil(f, message, "response-search-pick");
        var entrantId = RequiredString(message.GetProperty("tutorial").GetProperty("focus"), "legionInstanceId");
        message = await ResolveChoice(f, message, entrantId, "helper-search");
        var order = SinglePrompt(message);
        message = await Act(f, new("resolvePrompt", PromptId: PromptId(order),
            TopCardInstanceIds: order.GetProperty("validChoices").EnumerateArray()
                .Select(choice => choice.GetString()!).ToList(), BottomCardInstanceIds: []), "helper-order");
        message = await PassOnlyPromptsUntil(f, message, "response-cover");
        var own = f.RuntimeGame().State.Players[0];
        var counter = own.Hand.Single(card => card.InstanceId == counterId);
        var payment = own.Morale.Where(card => !card.Tapped).Take(counter.CurrentCost)
            .Select(card => card.InstanceId).ToList();
        message = await Act(f, new("playCard", counterId, 1, 2, CardInstanceIds: payment), "helper-cover");
        message = await PassOnlyPromptsUntil(f, message, "response-end-turn");
        return (message, counterId, costId, payment, matchId,
            (await f.Recorder.LoadJournalEngineAsync(matchId))!.CommandSequence);
    }

    private static async Task<JsonElement> Start(TutorialRoomLifecycleTests.Fixture f)
        => TutorialRoomLifecycleTests.Message(
            await f.Manager.CreateTutorialAsync(f.Student, "response", "start-response"), "gameState");

    private static async Task<JsonElement> Act(TutorialRoomLifecycleTests.Fixture f,
        L12Command command, string requestId)
        => TutorialRoomLifecycleTests.Message(await f.Act(command, requestId), "gameState");

    private static async Task<JsonElement> ResolveChoice(TutorialRoomLifecycleTests.Fixture f,
        JsonElement message, string choice, string requestId)
    {
        var prompt = SinglePrompt(message);
        Assert.Contains(choice, prompt.GetProperty("validChoices").EnumerateArray()
            .Select(value => value.GetString()));
        return await Act(f, new("resolvePrompt", PromptId: PromptId(prompt), CardInstanceIds: [choice]), requestId);
    }

    private static async Task<JsonElement> PassOnlyPromptsUntil(TutorialRoomLifecycleTests.Fixture f,
        JsonElement message, string expectedStep)
    {
        for (var budget = 0; budget < 16 && Step(message) == "response-pass"; budget++)
        {
            var prompt = SinglePrompt(message);
            var choices = prompt.GetProperty("validChoices").EnumerateArray()
                .Select(value => value.GetString()).ToArray();
            Assert.Equal("pass", Assert.Single(choices));
            message = await Act(f, new("resolvePrompt", PromptId: PromptId(prompt),
                CardInstanceIds: ["pass"]), "helper-pass-" + Guid.NewGuid().ToString("N"));
        }
        Assert.True(expectedStep == Step(message),
            $"expected {expectedStep}, found {Step(message)}; {RuntimeFacts(f)}");
        return message;
    }

    private static string RuntimeFacts(TutorialRoomLifecycleTests.Fixture f)
    {
        var state = f.RuntimeGame().State;
        var own = state.Players[0];
        var tutorial = f.RuntimeRoom().GetType().GetProperty("Tutorial")!.GetValue(f.RuntimeRoom())!;
        object? Property(string name) => tutorial.GetType().GetProperty(name)!.GetValue(tutorial);
        return JsonSerializer.Serialize(new
        {
            hp = own.Hp,
            initialHp = Property("ResponseInitialHp"),
            step = Property("StepId"),
            artifact = own.Relic is null ? null : new { own.Relic.InstanceId, own.Relic.Tapped },
            grave = own.Graveyard.Select(card => card.InstanceId).ToArray(),
            field = FieldLegions(own).Select(card => new { card.InstanceId, card.Tapped }).ToArray(),
            morale = own.Morale.Select(card => new { card.InstanceId, card.Tapped }).ToArray(),
            moraleDeck = own.MoraleDeck.Select(card => card.InstanceId).ToArray(),
            initialMorale = Property("InitialMoraleInstanceIds"),
            coverPayment = Property("CounterPaymentInstanceIds"),
            prompts = state.PendingPrompts.Count,
            activations = state.PendingActivations.Count,
            effects = state.EffectStack.Count,
            deferred = state.DeferredEffectStack.Count,
            batches = state.PendingTriggerBatches.Count,
            candidates = state.PendingTriggerStackCandidates.Count,
            suspended = state.SuspendedCombatContexts.Count,
            responseWindow = state.ResponseWindow is not null,
            resolving = state.IsResolvingStack,
        });
    }

    private static JsonElement SinglePrompt(JsonElement message)
        => Assert.Single(message.GetProperty("state").GetProperty("prompts").EnumerateArray().ToArray());

    private static string PromptId(JsonElement prompt) => prompt.GetProperty("promptId").GetString()!;

    private static string Step(JsonElement message)
        => message.GetProperty("tutorial").GetProperty("stepId").GetString()!;

    private static string RequiredString(JsonElement value, string property)
    {
        var result = value.GetProperty(property).GetString();
        return Assert.IsType<string>(result);
    }

    private static List<L12CardInstance> FieldLegions(L12PlayerState player)
        => player.Field.SelectMany(row => row).Where(card => card?.CardType == "legion")
            .Cast<L12CardInstance>().ToList();

    private static void AssertQueuesEmpty(L12GameState state)
    {
        Assert.Null(state.PendingDefense);
        Assert.Empty(state.SuspendedCombatContexts);
        Assert.Empty(state.PendingPrompts);
        Assert.Empty(state.PendingActivations);
        Assert.Empty(state.EffectStack);
        Assert.Empty(state.DeferredEffectStack);
        Assert.Empty(state.PendingTriggerBatches);
        Assert.Empty(state.PendingTriggerStackCandidates);
        Assert.Null(state.ResponseWindow);
        Assert.False(state.IsResolvingStack);
    }

    private static string? MessageType(OutgoingMessage message)
        => JsonSerializer.SerializeToElement(message.Payload, Wire).GetProperty("type").GetString();

    private static bool IsGame(OutgoingMessage message) => MessageType(message) == "gameState";
}
