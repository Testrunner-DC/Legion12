using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TutorialFinalChapterTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task PreparationUsesRealAllDisasterSetupAndReloadsEveryStudentAndOpponentDecision()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var message = await f.Start("preparation");
        Assert.Equal("preparation-initiative", Step(message));
        Assert.Equal(1, message.GetProperty("tutorial").GetProperty("version").GetInt32());
        Assert.False(message.GetProperty("gmEnabled").GetBoolean());
        Assert.False(message.GetProperty("state").GetProperty("players")[1].TryGetProperty("hand", out _));
        Assert.Equal("all", f.RuntimeGame().State.DisasterMode);
        Assert.Equal("ST01-M1", f.RuntimeGame().State.Players[0].MasterId);
        Assert.Equal("ST04-M1", f.RuntimeGame().State.Players[1].MasterId);
        Assert.DoesNotContain(await f.Manager.HandleGmActionAsync(f.Student,
            JsonSerializer.SerializeToElement(new { type = "addMorale", targetPlayer = 0, value = 9 })), IsGame);

        var initiative = OwnPrompt(message, "setup-initiative");
        Assert.Contains("first", Choices(initiative));
        message = await Resolve(f, message, ["first"], "preparation-initiative");
        Assert.Equal("preparation-ban", Step(message));

        var firstBan = OwnPrompt(message, "setup-ban");
        var firstBanCommand = new L12Command("resolvePrompt", PromptId: PromptId(firstBan),
            CardInstanceIds: [Choices(firstBan)[0]]);
        message = Game(await f.Act(firstBanCommand, "preparation-ban-one"));
        Assert.Equal("preparation-ban", Step(message));
        var secondBan = OwnPrompt(message, "setup-ban");
        Assert.NotEqual(PromptId(firstBan), PromptId(secondBan));
        var matchId = message.GetProperty("state").GetProperty("matchId").GetString()!;
        var afterOpponentBan = (await f.Recorder.LoadJournalEngineAsync(matchId))!;
        var afterOpponentBanHash = afterOpponentBan.Engine.ComputeStateHash();
        var duplicate = Game(await f.Act(firstBanCommand, "preparation-ban-one"));
        Assert.Equal(PromptId(secondBan), PromptId(OwnPrompt(duplicate, "setup-ban")));
        Assert.Equal(afterOpponentBan.CommandSequence,
            (await f.Recorder.LoadJournalEngineAsync(matchId))!.CommandSequence);
        Assert.Equal(afterOpponentBanHash,
            (await f.Recorder.LoadJournalEngineAsync(matchId))!.Engine.ComputeStateHash());

        message = await Resolve(f, message, [Choices(secondBan)[0]], "preparation-ban-two");
        Assert.Equal("preparation-reveal", Step(message));
        var reveal = OwnPrompt(message, "setup-public-confirm");
        Assert.Empty(Choices(reveal));
        Assert.Equal(0, reveal.GetProperty("minChoose").GetInt32());
        message = await Resolve(f, message, [], "preparation-reveal");
        Assert.Equal("preparation-select", Step(message));
        var selection = OwnPrompt(message, "setup-first-pick");
        Assert.Equal(3, Choices(selection).Count);
        message = await Resolve(f, message, [Choices(selection)[0]], "preparation-select");
        Assert.Equal("preparation-mulligan", Step(message));
        Assert.Empty(message.GetProperty("state").GetProperty("prompts").EnumerateArray());

        message = Game(await f.Act(new("mulligan", CardInstanceIds: []), "preparation-mulligan"));
        Assert.Equal("completed", Step(message));
        Assert.Equal("completed", message.GetProperty("tutorial").GetProperty("status").GetString());
        var state = f.RuntimeGame().State;
        Assert.Equal(L12Phase.Main, state.Phase);
        Assert.All(state.Players, player => Assert.True(player.MulliganDone));
        Assert.Equal(3, state.BannedDisasters.Count);
        Assert.Equal(3, state.SelectedDisasters.Count);
        Assert.Equal(2, state.ChosenDisasters.Count);
        Assert.Equal(4, state.DisasterDeck.Count);
        Assert.Contains(state.Events, entry => entry.Type == "disaster-public");
        Assert.Contains(state.Events, entry => entry.Type == "disaster-deck-ready");
        var durable = (await f.Recorder.LoadJournalEngineAsync(matchId))!;
        Assert.Equal(f.RuntimeGame().ComputeStateHash(), durable.Engine.ComputeStateHash());
        Assert.Equal(3, state.Events.Count(entry => entry.Type == "disaster-banned"));
        Assert.True(durable.CommandSequence >= 9,
            "initiative, three bans, two public confirmations, two selections and two mulligans must be journaled");
    }

    [Fact]
    public async Task PreparationAlsoCompletesWhenStudentChoosesSecond()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var message = await f.Start("preparation");

        message = await Resolve(f, message, ["second"], "preparation-second-initiative");
        Assert.Equal("preparation-ban", Step(message));
        var ban = OwnPrompt(message, "setup-ban");
        message = await Resolve(f, message, [Choices(ban)[0]], "preparation-second-ban");
        Assert.Equal("preparation-reveal", Step(message));
        message = await Resolve(f, message, [], "preparation-second-reveal");
        Assert.Equal("preparation-select", Step(message));
        var selection = OwnPrompt(message, "setup-second-pick");
        Assert.Equal(2, Choices(selection).Count);
        message = await Resolve(f, message, [Choices(selection)[0]], "preparation-second-select");
        Assert.Equal("preparation-mulligan", Step(message));

        message = Game(await f.Act(new("mulligan", CardInstanceIds: []), "preparation-second-mulligan"));
        Assert.Equal("completed", Step(message));
        Assert.Equal("completed", message.GetProperty("tutorial").GetProperty("status").GetString());
        Assert.Equal(L12Phase.Main, f.RuntimeGame().State.Phase);
        Assert.All(f.RuntimeGame().State.Players, player => Assert.True(player.MulliganDone));
        AssertQueuesEmpty(f.RuntimeGame().State);
        var matchId = message.GetProperty("state").GetProperty("matchId").GetString()!;
        Assert.Equal(f.RuntimeGame().ComputeStateHash(),
            (await f.Recorder.LoadJournalEngineAsync(matchId))!.Engine.ComputeStateHash());
    }

    [Fact]
    public async Task PreparationHumanFailureRestoresInitialChoiceAndOpponentFailureFreezesAtCommittedStudentBan()
    {
        await using (var human = await TutorialRoomLifecycleTests.Fixture.Create())
        {
            var initial = await human.Start("preparation");
            var beforeHash = human.RuntimeGame().ComputeStateHash();
            var prompt = OwnPrompt(initial, "setup-initiative");
            var command = new L12Command("resolvePrompt", PromptId: PromptId(prompt), CardInstanceIds: ["first"]);
            human.Recorder.StorageFailureInjector = stage =>
            {
                if (stage == "before-match-command-commit")
                    throw new IOException("synthetic preparation human append failure");
            };
            Assert.DoesNotContain(await human.Act(command, "preparation-human-failure"), IsGame);
            human.Recorder.StorageFailureInjector = null;
            var recovered = TutorialRoomLifecycleTests.Message(
                await human.Manager.RecoveryStateAsync(human.Student), "gameState");
            Assert.Equal(beforeHash, human.RuntimeGame().ComputeStateHash());
            Assert.Equal("preparation-initiative", Step(recovered));
            Assert.Equal("preparation-ban",
                Step(Game(await human.Act(command, "preparation-human-failure"))));
        }

        await using var opponent = await TutorialRoomLifecycleTests.Fixture.Create();
        var message = await opponent.Start("preparation");
        message = await Resolve(opponent, message, ["first"], "preparation-before-opponent-failure");
        var matchId = message.GetProperty("state").GetProperty("matchId").GetString()!;
        var sequenceBefore = (await opponent.Recorder.LoadJournalEngineAsync(matchId))!.CommandSequence;
        var ban = OwnPrompt(message, "setup-ban");
        var writes = 0;
        opponent.Recorder.StorageFailureInjector = stage =>
        {
            if (stage == "before-match-command-commit" && ++writes == 2)
                throw new IOException("synthetic preparation opponent append failure");
        };
        try
        {
            var failed = await opponent.Act(new("resolvePrompt", PromptId: PromptId(ban),
                CardInstanceIds: [Choices(ban)[0]]), "preparation-opponent-failure");
            Assert.Contains(failed, item => MessageType(item) == "tutorialRestartRequired");
            var durable = (await opponent.Recorder.LoadJournalEngineAsync(matchId))!;
            Assert.Equal(sequenceBefore + 1, durable.CommandSequence);
            Assert.Equal("setup-ban", Assert.Single(durable.Engine.State.PendingPrompts).Continuation);
            Assert.Equal(1, Assert.Single(durable.Engine.State.PendingPrompts).PlayerIndex);
            Assert.Contains(await opponent.Manager.RecoveryStateAsync(opponent.Student),
                item => MessageType(item) == "tutorialRestartRequired");
        }
        finally { opponent.Recorder.StorageFailureInjector = null; }
    }

    [Fact]
    public async Task FinaleStartsAtLegalTwoHpCheckpointAndTwoRealAttacksWinWithExactJournal()
    {
        await using var f = await TutorialRoomLifecycleTests.Fixture.Create();
        var message = await f.Start("finale");
        Assert.Equal("finale-attack", Step(message));
        Assert.Equal(1, message.GetProperty("tutorial").GetProperty("version").GetInt32());
        Assert.False(message.GetProperty("gmEnabled").GetBoolean());
        Assert.False(message.GetProperty("state").GetProperty("players")[1].TryGetProperty("hand", out _));
        Assert.DoesNotContain(await f.Manager.HandleGmActionAsync(f.Student,
            JsonSerializer.SerializeToElement(new { type = "damageMaster", targetPlayer = 1, value = 99 })), IsGame);

        var game = f.RuntimeGame();
        var state = game.State;
        var checkpointHash = game.ComputeStateHash();
        var matchId = state.MatchId;
        Assert.Equal("none", state.DisasterMode);
        Assert.Equal(L12Phase.Main, state.Phase);
        Assert.Equal(0, state.ActivePlayer);
        Assert.Equal(2, state.Players[1].Hp);
        var attackers = FieldLegions(state.Players[0]);
        Assert.Equal(2, attackers.Count);
        Assert.All(attackers, attacker =>
        {
            Assert.Equal("ST01-05", attacker.CardId);
            Assert.False(attacker.Tapped);
            Assert.False(attacker.CannotAttack);
            Assert.True(attacker.SummonRound < state.Round);
        });
        Assert.InRange(state.Players[0].Hand.Count, 0, 3);
        AssertQueuesEmpty(state);
        var checkpoint = (await f.Recorder.LoadJournalEngineAsync(matchId))!;
        Assert.Equal(0, checkpoint.CommandSequence);
        Assert.Equal(checkpointHash, checkpoint.Engine.ComputeStateHash());

        var firstAttack = new L12Command("attack", attackers[0].InstanceId, Target: new("master"));
        message = Game(await f.Act(firstAttack, "finale-attack-one"));
        Assert.Equal("finale-attack", Step(message));
        Assert.Equal(1, f.RuntimeGame().State.Players[1].Hp);
        Assert.Equal(L12Phase.Main, f.RuntimeGame().State.Phase);
        var afterFirst = (await f.Recorder.LoadJournalEngineAsync(matchId))!;
        var firstHash = afterFirst.Engine.ComputeStateHash();
        var duplicate = Game(await f.Act(firstAttack, "finale-attack-one"));
        Assert.Equal("finale-attack", Step(duplicate));
        Assert.Equal(1, f.RuntimeGame().State.Players[1].Hp);
        Assert.Equal(afterFirst.CommandSequence,
            (await f.Recorder.LoadJournalEngineAsync(matchId))!.CommandSequence);
        Assert.Equal(firstHash, (await f.Recorder.LoadJournalEngineAsync(matchId))!.Engine.ComputeStateHash());

        var secondAttack = new L12Command("attack", attackers[1].InstanceId, Target: new("master"));
        message = Game(await f.Act(secondAttack, "finale-attack-two"));
        state = f.RuntimeGame().State;
        Assert.Equal(L12Phase.GameOver, state.Phase);
        Assert.Equal(0, state.Winner);
        Assert.Equal(0, state.Players[1].Hp);
        Assert.Equal("completed", Step(message));
        Assert.Equal("completed", message.GetProperty("tutorial").GetProperty("status").GetString());
        var unblocked = state.Events.Where(entry => entry.Type == "defense"
            && entry.PlayerCombat?.OutcomeCode == "unblocked").ToArray();
        Assert.True(unblocked.Length >= 2);
        Assert.Equal(2, unblocked.TakeLast(2).Count(entry => entry.PlayerCombat?.MasterDamage == 1));
        AssertQueuesEmpty(state);
        var completed = (await f.Recorder.LoadJournalEngineAsync(matchId))!;
        Assert.Equal(f.RuntimeGame().ComputeStateHash(), completed.Engine.ComputeStateHash());
        Assert.True(completed.CommandSequence >= 5,
            "two student attacks, the once-per-turn Kagutsuchi decline and two no-block defenses must be journaled");
    }

    [Fact]
    public async Task FinaleSurrenderIsRetryAndHumanOrOpponentAppendFailureNeverPublishesFreeDamage()
    {
        await using (var surrendered = await TutorialRoomLifecycleTests.Fixture.Create())
        {
            var initial = await surrendered.Start("finale");
            var final = Game(await surrendered.Act(new("surrender"), "finale-surrender"));
            Assert.Equal(L12Phase.GameOver, surrendered.RuntimeGame().State.Phase);
            Assert.Equal(1, surrendered.RuntimeGame().State.Winner);
            Assert.Equal("finale-retry", Step(final));
            Assert.Equal("active", final.GetProperty("tutorial").GetProperty("status").GetString());
            Assert.Equal(2, surrendered.RuntimeGame().State.Players[1].Hp);
            Assert.Equal("finale-attack", Step(initial));
        }

        await using (var human = await TutorialRoomLifecycleTests.Fixture.Create())
        {
            await human.Start("finale");
            var attacker = FieldLegions(human.RuntimeGame().State.Players[0])[0];
            var beforeHash = human.RuntimeGame().ComputeStateHash();
            var command = new L12Command("attack", attacker.InstanceId, Target: new("master"));
            human.Recorder.StorageFailureInjector = stage =>
            {
                if (stage == "before-match-command-commit")
                    throw new IOException("synthetic finale human append failure");
            };
            Assert.DoesNotContain(await human.Act(command, "finale-human-failure"), IsGame);
            human.Recorder.StorageFailureInjector = null;
            Assert.Equal(beforeHash, human.RuntimeGame().ComputeStateHash());
            Assert.Equal(2, human.RuntimeGame().State.Players[1].Hp);
            Game(await human.Act(command, "finale-human-failure"));
            Assert.Equal(1, human.RuntimeGame().State.Players[1].Hp);
        }

        await using var opponent = await TutorialRoomLifecycleTests.Fixture.Create();
        var started = await opponent.Start("finale");
        var matchId = started.GetProperty("state").GetProperty("matchId").GetString()!;
        var attackerId = FieldLegions(opponent.RuntimeGame().State.Players[0])[0].InstanceId;
        var writes = 0;
        opponent.Recorder.StorageFailureInjector = stage =>
        {
            if (stage == "before-match-command-commit" && ++writes == 2)
                throw new IOException("synthetic finale opponent append failure");
        };
        try
        {
            var failed = await opponent.Act(new("attack", attackerId, Target: new("master")),
                "finale-opponent-failure");
            Assert.Contains(failed, item => MessageType(item) == "tutorialRestartRequired");
            var durable = (await opponent.Recorder.LoadJournalEngineAsync(matchId))!;
            Assert.Equal(1, durable.CommandSequence);
            Assert.Equal(2, durable.Engine.State.Players[1].Hp);
            Assert.NotEmpty(durable.Engine.State.PendingPrompts);
            Assert.Contains(await opponent.Manager.RecoveryStateAsync(opponent.Student),
                item => MessageType(item) == "tutorialRestartRequired");
        }
        finally { opponent.Recorder.StorageFailureInjector = null; }
    }

    private static async Task<JsonElement> Resolve(TutorialRoomLifecycleTests.Fixture f,
        JsonElement message, List<string> choices, string requestId)
    {
        var prompt = OwnPrompt(message);
        Assert.All(choices, choice => Assert.Contains(choice, Choices(prompt)));
        return Game(await f.Act(new("resolvePrompt", PromptId: PromptId(prompt),
            CardInstanceIds: choices), requestId));
    }

    private static JsonElement OwnPrompt(JsonElement message, string? continuation = null)
    {
        var prompt = Assert.Single(message.GetProperty("state").GetProperty("prompts")
            .EnumerateArray().ToArray());
        if (continuation is not null)
        {
            var expectedKind = continuation switch
            {
                "setup-initiative" => "initiative",
                "setup-ban" => "disaster-ban",
                "setup-public-confirm" => "disaster-reveal",
                "setup-first-pick" or "setup-second-pick" => "disaster-pick",
                _ => throw new ArgumentOutOfRangeException(nameof(continuation)),
            };
            Assert.Equal(expectedKind, prompt.GetProperty("kind").GetString());
        }
        return prompt;
    }

    private static List<string> Choices(JsonElement prompt)
        => prompt.GetProperty("validChoices").EnumerateArray().Select(value => value.GetString()!).ToList();

    private static string PromptId(JsonElement prompt) => prompt.GetProperty("promptId").GetString()!;

    private static string Step(JsonElement message)
        => message.GetProperty("tutorial").GetProperty("stepId").GetString()!;

    private static JsonElement Game(IReadOnlyList<OutgoingMessage> messages)
        => TutorialRoomLifecycleTests.Message(messages, "gameState");

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
