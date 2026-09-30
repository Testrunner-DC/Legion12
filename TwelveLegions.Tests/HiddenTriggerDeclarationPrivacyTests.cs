using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class HiddenTriggerDeclarationPrivacyTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] AuthorityCounters =
        ["EventSequence", "PromptSequence", "StackSequence", "ActivationSequence", "TriggerBatchSequence", "AuthorityEventSequence"];
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static object Call(L12GameEngine game, string method, params object[] args)
        => typeof(L12GameEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, args)!;
    private static L12GameEngine Restore(L12GameEngine game)
        => (L12GameEngine)typeof(L12GameEngine).GetMethod("RestoreCheckpoint", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [Catalog, game.SerializeFullState(),
                typeof(L12GameEngine).GetProperty("RandomState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game),
                typeof(L12GameEngine).GetProperty("CardFactSignalSequence", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game),
                false, true, null])!;
    private static L12CardInstance Card(L12GameEngine game, string id, string instance, int owner)
    {
        var card = (L12CardInstance)Call(game, "CreateCard", id, instance);
        card.OwnerIndex = owner;
        return card;
    }
    private static L12GameEngine Create(int controller, bool sibling, bool eligible)
    {
        var fixture = typeof(TriggerResourceOrderingRegressionTests);
        var game = (L12GameEngine)fixture.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [controller, "ST01-M1"])!;
        game = Restore(game); // production concealHiddenResponseAvailability=true
        game.State.ActivePlayer = 1 - controller;
        var player = game.State.Players[controller];
        var hidden = Card(game, "ST01-10", "concealed-trigger", controller);
        hidden.Hidden = true;
        player.Field[1][2] = hidden;
        if (sibling) player.Hand.Add(Card(game, "S01-0213", "hand-sibling", controller));
        if (eligible) MakeEligible(game, controller);
        return game;
    }
    private static void MakeEligible(L12GameEngine game, int controller)
    {
        game.State.Players[controller].Morale.Add(new L12MoraleCard { CardId = "ST01-C1", InstanceId = "return-cost" });
        game.State.Players[controller].Hand.Add(Card(game, "ST01-05", "entrant", controller));
    }
    private static void Choose(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(choice, prompt.ValidChoices);
        var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }
    private static void DeclineSibling(L12GameEngine game)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-order", prompt.Kind);
        var hidden = Assert.Single(prompt.ValidChoices, id => prompt.Data[$"sourceInstance:{id}"] == "concealed-trigger");
        var sibling = Assert.Single(prompt.ValidChoices, id => prompt.Data[$"sourceInstance:{id}"] == "hand-sibling");
        var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [hidden, sibling]));
        Assert.True(result.Accepted, result.Error);
        Choose(game, "mode:none");
    }
    private static JsonElement Replay(L12GameEngine game, int viewer)
        => (JsonElement)typeof(MatchRecorder).GetMethod("SanitizeRecordedState",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
                [JsonDocument.Parse(game.SerializeFullState()).RootElement, viewer])!;

    private static void AssertNoAuthorityCounters(JsonElement replay)
    {
        foreach (var counter in AuthorityCounters) Assert.False(replay.TryGetProperty(counter, out _), counter);
    }

    private static void DeclineAll(L12GameEngine game)
    {
        for (var attempts = 0; attempts < 20 && game.State.PendingPrompts.Count > 0; attempts++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.Kind != "trigger-order") { Choose(game, "mode:none"); continue; }
            var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt",
                PromptId: prompt.PromptId, CardInstanceIds: prompt.ValidChoices.ToList()));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.Empty(game.State.PendingPrompts);
    }

    [Theory]
    [InlineData(0, false, 1)] [InlineData(1, false, 1)]
    [InlineData(0, true, 1)] [InlineData(1, true, 1)]
    [InlineData(0, false, 2)] [InlineData(1, false, 2)]
    [InlineData(0, true, 2)] [InlineData(1, true, 2)]
    public void EqualPublicBoardsDoNotDisclosePrivateTriggerAllocatorCounts(int controller, bool sibling, int hiddenCount)
    {
        var trigger = Create(controller, sibling, false);
        var control = Create(controller, sibling, false);
        for (var index = 0; index < hiddenCount; index++)
        {
            var instance = $"paired-covered-{index}";
            var hidden = Card(trigger, "ST01-10", instance, controller);
            var nonTiming = Card(control, "S01-0223", instance, controller);
            hidden.Hidden = nonTiming.Hidden = true;
            trigger.State.Players[controller].Field[1][2 - index] = hidden;
            control.State.Players[controller].Field[1][2 - index] = nonTiming;
        }
        void Compare()
        {
            foreach (var viewer in new[] { 1 - controller, -1 })
            {
                var beforeTrigger = trigger.SerializeFullState();
                var beforeControl = control.SerializeFullState();
                var actual = Replay(trigger, viewer);
                var baseline = Replay(control, viewer);
                Assert.Equal(baseline.GetProperty("Players")[controller].GetProperty("Field").GetRawText(),
                    actual.GetProperty("Players")[controller].GetProperty("Field").GetRawText());
                AssertNoAuthorityCounters(actual);
                AssertNoAuthorityCounters(baseline);
                Assert.Equal(beforeTrigger, trigger.SerializeFullState());
                Assert.Equal(beforeControl, control.SerializeFullState());
                foreach (var queue in new[] { "PendingPrompts", "PendingActivations", "PendingTriggerBatches", "PendingTriggerStackCandidates" })
                {
                    Assert.Empty(actual.GetProperty(queue).EnumerateArray());
                    Assert.Empty(baseline.GetProperty(queue).EnumerateArray());
                }
            }
        }
        Compare();
        Call(trigger, "QueueS1PostAttackReactions", 1 - controller);
        Call(control, "QueueS1PostAttackReactions", 1 - controller);
        Compare();
        DeclineAll(trigger);
        DeclineAll(control);
        // Prove the authority really took different private paths; projection must not reset it.
        Assert.True(trigger.State.ActivationSequence > control.State.ActivationSequence);
        var beforeRestore = JsonDocument.Parse(trigger.SerializeFullState()).RootElement;
        trigger = Restore(trigger);
        control = Restore(control);
        var afterRestore = JsonDocument.Parse(trigger.SerializeFullState()).RootElement;
        foreach (var counter in AuthorityCounters)
            Assert.Equal(beforeRestore.GetProperty(counter).GetInt64(), afterRestore.GetProperty(counter).GetInt64());
        Compare();
        Assert.All(trigger.State.Players[controller].Field[1].Where(card => card is not null), card => Assert.True(card!.Hidden));
        var privateSequences = trigger.State.Events.Where(entry => entry.Type.StartsWith("private-trigger-"))
            .Select(entry => entry.Sequence).ToHashSet();
        Assert.NotEmpty(privateSequences);
        Assert.Contains(trigger.SnapshotFor(controller).RecentEvents, entry => privateSequences.Contains(entry.Sequence));
        Assert.Contains(trigger.SnapshotForGm(controller).RecentEvents, entry => privateSequences.Contains(entry.Sequence));
        Assert.DoesNotContain(trigger.SnapshotForSpectator().RecentEvents, entry => privateSequences.Contains(entry.Sequence));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public async Task PlayerReplayOmitsAllocatorsWhileArchiveAndPublicUseRemainIntact(int controller)
    {
        var game = Create(controller, false, true);
        Call(game, "QueueS1PostAttackReactions", 1 - controller);
        Choose(game, "mode:use");
        Choose(game, "return-cost");
        Choose(game, "entrant");
        Choose(game, "0:0");
        Assert.Null(game.State.Players[controller].Field[1][2]);
        Assert.False(Assert.Single(game.State.Players[controller].Resolving, card => card.CardId == "ST01-10").Hidden);
        Assert.Single(game.State.EffectStack);
        game = Restore(game);
        var directory = Path.Combine(Path.GetTempPath(), "l12-tests", Guid.NewGuid().ToString("N"));
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        await recorder.StartAsync(game);
        var blockedCommand = new L12Command("endTurn");
        var blocked = game.Handle(controller, blockedCommand);
        Assert.False(blocked.Accepted);
        await recorder.AppendAsync(game, 1, controller, JsonSerializer.Serialize(blockedCommand), blocked);
        var authority = JsonDocument.Parse(game.SerializeFullState()).RootElement;
        game.ConcludeByAuthority(controller, "回放投影测试结束");
        await recorder.CompleteAsync(game);
        var raw = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(game.State.MatchId));
        var archived = Assert.Single(raw.Commands).State;
        foreach (var counter in AuthorityCounters)
            Assert.Equal(authority.GetProperty(counter).GetInt64(), archived.GetProperty(counter).GetInt64());
        foreach (var viewer in new[] { 0, 1 })
        {
            var detail = Assert.IsType<L12MatchDetail>(await recorder.GetMatchForPlayerAsync(game.State.MatchId, viewer == 0 ? "甲" : "乙"));
            var command = Assert.Single(detail.Commands);
            AssertNoAuthorityCounters(command.State);
            Assert.Equal(1, command.Sequence);
            Assert.Equal(archived.GetProperty("Revision").GetInt64(), command.State.GetProperty("Revision").GetInt64());
            Assert.Equal(archived.GetProperty("EffectStack").GetRawText(), command.State.GetProperty("EffectStack").GetRawText());
            Assert.Contains("ST01-10", command.State.GetProperty("Players")[controller].GetProperty("Resolving").GetRawText());
            var visibleSequences = archived.GetProperty("Events").EnumerateArray()
                .Where(entry => !entry.GetProperty("Type").GetString()!.StartsWith("private-trigger-")
                    || entry.GetProperty("PlayerIndex").GetInt32() == viewer)
                .Select(entry => entry.GetProperty("Sequence").GetInt64());
            Assert.Equal(visibleSequences, command.State.GetProperty("Events").EnumerateArray()
                .Select(entry => entry.GetProperty("Sequence").GetInt64()));
        }
        var archivedAfter = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(game.State.MatchId));
        Assert.Equal(archived.GetRawText(), Assert.Single(archivedAfter.Commands).State.GetRawText());
    }
    private static void AssertPrivate(L12GameEngine game, int controller)
    {
        foreach (var snapshot in new[] { game.SnapshotFor(1 - controller), game.SnapshotForSpectator() })
        {
            var text = JsonSerializer.Serialize(snapshot, Json);
            Assert.DoesNotContain("ST01-10", text);
            Assert.DoesNotContain("暗度陈仓", text);
            var json = JsonSerializer.SerializeToElement(snapshot, Json);
            Assert.Empty(json.GetProperty("prompts").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("waitingPrompt").ValueKind);
            var events = json.GetProperty("recentEvents").EnumerateArray().ToArray();
            Assert.DoesNotContain(events, item => item.GetProperty("type").GetString() is
                "activation-declare" or "ability-cancelled" or "ability-rejected" or "trigger-order" or "effect-skipped");
            Assert.DoesNotContain(events, item => item.GetProperty("type").GetString()!.StartsWith("private-trigger-"));
        }
        // Referee may see the covered battlefield identity, but not private declarations.
        var referee = JsonSerializer.SerializeToElement(game.SnapshotForReferee(), Json);
        Assert.DoesNotContain(referee.GetProperty("recentEvents").EnumerateArray(), item =>
            item.GetProperty("type").GetString() is "activation-declare" or "ability-cancelled" or "trigger-order");
        Assert.Contains("ST01-10", JsonSerializer.Serialize(game.SnapshotForGm(controller), Json));
        Assert.Contains("ST01-10", JsonSerializer.Serialize(game.SnapshotFor(controller), Json));
        var state = JsonDocument.Parse(game.SerializeFullState()).RootElement;
        foreach (var viewer in new[] { 1 - controller, -1 })
        {
            var replay = (JsonElement)typeof(MatchRecorder).GetMethod("SanitizeRecordedState",
                BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [state, viewer])!;
            Assert.DoesNotContain("ST01-10", replay.GetRawText());
            Assert.DoesNotContain("private-trigger-", replay.GetRawText());
        }
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(0, true)] [InlineData(1, true)]
    public void UnavailableHiddenTriggerDoesNotPublishADeclarationOrCancellation(int controller, bool sibling)
    {
        var game = Create(controller, sibling, false);
        Call(game, "QueueS1PostAttackReactions", 1 - controller);
        AssertPrivate(game, controller);
        if (sibling) DeclineSibling(game);
        Assert.Empty(game.State.PendingPrompts);
        Assert.True(game.State.Players[controller].Field[1][2]!.Hidden);
        AssertPrivate(Restore(game), controller);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(0, true)] [InlineData(1, true)]
    public void DecliningEligibleHiddenTriggerStaysPrivateAcrossCheckpoint(int controller, bool sibling)
    {
        var game = Create(controller, sibling, true);
        Call(game, "QueueS1PostAttackReactions", 1 - controller);
        AssertPrivate(game, controller);
        game = Restore(game);
        if (sibling) DeclineSibling(game);
        AssertPrivate(game, controller);
        Choose(game, "mode:none");
        Assert.Single(game.State.Players[controller].Morale);
        AssertPrivate(Restore(game), controller);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(0, true)] [InlineData(1, true)]
    public void LateEligibilityRevealsOnlyAfterCommittedUseAndNegationStillWorks(int controller, bool negate)
    {
        var game = Create(controller, true, false);
        game.State.IsResolvingStack = true;
        Call(game, "QueueS1PostAttackReactions", 1 - controller);
        AssertPrivate(game, controller);
        game = Restore(game);
        MakeEligible(game, controller);
        game.State.IsResolvingStack = false;
        Call(game, "AdvanceTriggerBatches");
        DeclineSibling(game);
        AssertPrivate(game, controller);
        Choose(game, "mode:use");
        AssertPrivate(Restore(game), controller);
        Choose(game, "return-cost");
        AssertPrivate(game, controller);
        Choose(game, "entrant");
        AssertPrivate(game, controller);
        Choose(game, "0:0");
        Assert.Empty(game.State.Players[controller].Morale);
        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal("ST01-10", item.SourceCardId);
        Assert.Contains("ST01-10", JsonSerializer.Serialize(game.SnapshotForSpectator(), Json));
        if (negate) item.Negated = true;
        for (var i = 0; i < 20 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; i++) Choose(game, "pass");
        Assert.Equal(!negate, game.State.Players[controller].Field[0][0]?.InstanceId == "entrant");
        Assert.DoesNotContain(game.SnapshotForSpectator().RecentEvents, action => action.Type.StartsWith("private-trigger-"));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void LosingEligibilityAfterChoosingUseRejectsPrivatelyWithoutPayment(int controller)
    {
        var game = Create(controller, false, true);
        Call(game, "QueueS1PostAttackReactions", 1 - controller);
        Choose(game, "mode:use");
        Choose(game, "return-cost");
        Choose(game, "entrant");
        game.State.Players[controller].Hand.Clear();
        Choose(game, "0:0");
        Assert.Single(game.State.Players[controller].Morale);
        Assert.Empty(game.State.EffectStack);
        Assert.True(game.State.Players[controller].Field[1][2]!.Hidden);
        AssertPrivate(Restore(game), controller);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void LaterRevealDoesNotRetroactivelyPublishPrivateDeclineEvents(int controller)
    {
        var game = Create(controller, false, true);
        Call(game, "QueueS1PostAttackReactions", 1 - controller);
        Choose(game, "mode:none");
        var privateSequences = game.State.Events.Where(entry => entry.Type.StartsWith("private-trigger-"))
            .Select(entry => entry.Sequence).ToHashSet();
        Assert.NotEmpty(privateSequences);
        game.State.Players[controller].Field[1][2]!.Hidden = false;
        game = Restore(game);
        Assert.DoesNotContain(game.SnapshotForSpectator().RecentEvents, entry => privateSequences.Contains(entry.Sequence));
        Assert.Contains(game.SnapshotFor(controller).RecentEvents, entry => privateSequences.Contains(entry.Sequence));
        Assert.DoesNotContain(game.SnapshotForReferee().RecentEvents, entry => privateSequences.Contains(entry.Sequence));
        Assert.Contains(game.SnapshotForGm(controller).RecentEvents, entry => privateSequences.Contains(entry.Sequence));
    }

    [Theory]
    [InlineData(0, "S01-0223")] [InlineData(1, "S01-0223")]
    [InlineData(0, "S01-0213")] [InlineData(1, "S01-0213")]
    public void SharedBoundaryAlsoProtectsOtherSetAndHandTriggerSources(int controller, string cardId)
    {
        var game = Create(controller, false, false);
        var player = game.State.Players[controller];
        var source = Card(game, cardId, "other-private-source", controller);
        player.Field[1][2] = null;
        if (cardId == "S01-0213") player.Hand.Add(source);
        else { source.Hidden = true; player.Field[1][2] = source; }
        var candidate = new L12TriggerCandidate
        {
            CandidateId = "private-source-candidate", Controller = controller,
            SourceInstanceId = source.InstanceId, SourceCardId = source.CardId,
            SourceName = source.Name, SourceSnapshot = source.Clone(), Trigger = "reaction", Text = source.EffectText ?? source.Name,
        };
        Call(game, "QueueTriggerCandidates", (object)new[] { candidate });
        Assert.Contains("mode:none", Assert.Single(game.State.PendingPrompts).ValidChoices);
        foreach (var snapshot in new[] { game.SnapshotFor(1 - controller), game.SnapshotForSpectator() })
            Assert.DoesNotContain(cardId, JsonSerializer.Serialize(snapshot, Json));
        Choose(game, "mode:none");
        game = Restore(game);
        foreach (var snapshot in new[] { game.SnapshotFor(1 - controller), game.SnapshotForSpectator() })
            Assert.DoesNotContain(cardId, JsonSerializer.Serialize(snapshot, Json));
        Assert.Empty(game.State.EffectStack);
    }
}
