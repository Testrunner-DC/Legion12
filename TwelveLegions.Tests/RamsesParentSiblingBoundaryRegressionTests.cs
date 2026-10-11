using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Tests;

// Explicit synthetic V2 scheduling-boundary fixture, NOT a reachable player-match reproduction.
// Ordinary paid commands establish the child chain. An existing typed death candidate/stack is
// intentionally inserted at the Ramses response checkpoint to guard the scheduler boundary.
public sealed class RamsesParentSiblingBoundaryRegressionTests(ITestOutputHelper output)
{
    private const string Ramses = "guard-ramses", Ptolemy = "guard-ptolemy", Next = "guard-nefertiti";
    private const string Tactic = "guard-prior-siege", Mozi = "guard-mozi", Hannibal = "guard-hannibal";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(0, "pending-batch")] [InlineData(1, "pending-batch")]
    [InlineData(0, "deferred-stack")] [InlineData(1, "deferred-stack")]
    [InlineData(0, "pending-candidates")] [InlineData(1, "pending-candidates")]
    [Trait("L12Evidence", "synthetic-v2:ramses-parent-sibling-boundary")]
    public void SyntheticExistingParentSiblingWaitsForWholeRamsesChildChain(int controller, string boundary)
    {
        var run = Run(controller, true, true, boundary);
        AssertComplete(run);
        Assert.True(run.ParentWaitedForWholeChain,
            $"SYNTHETIC_PARENT_SIBLING_AFTER_WHOLE_RAMSES_CHAIN boundary={boundary}; " +
            "not a natural player-card arrival reproduction; all synthetic sibling responses and child work completed");
    }

    [Theory]
    [InlineData(0, "pending-batch")] [InlineData(1, "pending-batch")]
    [InlineData(0, "deferred-stack")] [InlineData(1, "deferred-stack")]
    [InlineData(0, "pending-candidates")] [InlineData(1, "pending-candidates")]
    public void LastDelegateGeneratedDeathAndAuthorityChildFinishBeforeOldParent(int controller, string boundary)
    {
        var run = Run(controller, true, true, boundary, lastDelegate: true);
        AssertComplete(run);
        Assert.True(run.ParentWaitedForWholeChain);
        Assert.True(run.DisasterLast);
    }

    private static void AssertComplete(RunResult run)
    {
        Assert.True(run.Clean);
        Assert.Equal(2, run.TacticPaid);
        Assert.Equal(6, run.RamsesPaid);
        Assert.True(run.RealPriorRecorded);
        Assert.True(run.MoziDied);
        Assert.True(run.ChildDrew);
        Assert.Equal(1, run.RepeatResolves);
        Assert.Equal(1, run.ChildResolves);
        Assert.Equal(2, run.DelegateResolves);
        foreach (var stack in run.RequiredResponseStacks)
        {
            var responses = run.Responses.Where(response => response.StackItemId == stack).ToArray();
            Assert.Equal(2, responses.Length);
            Assert.Equal(new[] { 0, 1 }, responses.Select(response => response.Actor).Order().ToArray());
        }
    }

    [Theory]
    [InlineData(0, "invalid")] [InlineData(1, "invalid")]
    [InlineData(0, "decline")] [InlineData(1, "decline")]
    [InlineData(0, "cancel")] [InlineData(1, "cancel")]
    [InlineData(0, "no-op-last")] [InlineData(1, "no-op-last")]
    public void SyntheticSkippedOrCancelledDelegateCannotFallThroughHeldParent(int controller, string variant)
        => RunSyntheticBoundary(controller, variant, marker: true);

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void WithoutMarkerOriginalDeferredLifoThenCandidateThenBatchPriorityIsUnchanged(int controller)
        => RunSyntheticBoundary(controller, "ordinary", marker: false);

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void TerminalAtFrontRestoresAndReleasesOnceWithoutEffectOrResponse(int controller)
        => RunSyntheticBoundary(controller, "terminal-front", marker: true);

    // These tests explicitly construct accepted V2 queue boundaries, not player commands.
    // Every subsequent response/cancellation is the actual six-bound typed public command.
    private void RunSyntheticBoundary(int controller, string variant, bool marker)
    {
        var catalog = Catalog;
        var game = Create(catalog, controller, false);
        foreach (var p in game.State.Players)
        {
            p.Field[0] = new L12CardInstance?[3]; p.Field[1] = new L12CardInstance?[3];
            p.Hand.Clear(); p.Graveyard.Clear();
        }
        var player = game.State.Players[controller];
        var first = Card(catalog, variant == "invalid" ? "S01-0209" : "S01-0205", "boundary-first", controller);
        var last = Card(catalog, "S01-0211", "boundary-last", controller);
        player.Field[0][0] = first; player.Field[0][1] = last;
        player.Field[1][0] = Card(catalog, "S01-0212", "boundary-tomb", controller);
        var parent = Card(catalog, "S01-0202", "boundary-ramses", controller);
        L12TriggerCandidate Candidate(string id)
        {
            var source = Card(catalog, "S01-0211", id, controller);
            player.Graveyard.Add(source);
            return (L12TriggerCandidate)Call(game, "CreateTriggerCandidate", controller, source, "enter", "【登场时】效果", null, null)!;
        }
        L12StackItem Deferred(string id)
        {
            var c = Candidate(id);
            var item = new L12StackItem
            {
                StackItemId = $"stack-{++game.State.StackSequence}", Controller = controller,
                SourceInstanceId = id, SourceCardId = c.SourceCardId, SourceName = c.SourceName,
                SourceSnapshot = c.SourceSnapshot, Trigger = "enter", Text = c.Text,
            };
            foreach (var pair in c.Data) item.Data[pair.Key] = pair.Value;
            return item;
        }
        game.State.DeferredEffectStack.Add(Deferred("parent-deferred-1"));
        game.State.DeferredEffectStack.Add(Deferred("parent-deferred-2"));
        game.State.PendingTriggerStackCandidates.Add(Candidate("parent-candidate-1"));
        // Normal trigger-order admission is singleton. A separately retained diagnostic
        // proves that two old candidates already stall the marker-free generic path;
        // that unrelated synthetic topology is not repaired by this Ramses contract.
        game.State.PendingTriggerBatches.Add(new()
        {
            BatchId = $"batch-{++game.State.TriggerBatchSequence}", Controller = controller,
            Candidates = [Candidate("parent-batch")],
        });
        game.State.PendingTriggerBatches.Add(new()
        {
            BatchId = $"batch-{++game.State.TriggerBatchSequence}", Controller = controller,
            Candidates = [Candidate("parent-batch-second")],
        });
        if (marker)
        {
            var delegates = variant == "terminal-front" ? new[] { (last, "【登场时】效果") }
                : variant == "no-op-last" ? new[] { (last, "【登场时】效果") }
                : new[] { (first, "【登场时】效果"), (last, "【登场时】效果") };
            Call(game, "RegisterRamsesDelegateContinuation", new L12StackItem
            {
                StackItemId = "synthetic-completed-parent", Controller = controller,
                SourceInstanceId = parent.InstanceId, SourceCardId = parent.CardId, SourceName = parent.Name,
                Trigger = "enter", Text = "synthetic boundary only", SourceSnapshot = parent,
            }, delegates, false);
            if (variant == "terminal-front")
                game.State.PendingTriggerBatches.RemoveAt(0); // Explicit idle terminal micro-boundary.
        }
        var facts = new List<L12CardFactSignal>(); var events = new List<L12ActionEvent>();
        var commands = new List<object>(); var checkpoints = new List<object>();
        var fc = game.CardFactSignalSequence; var ec = game.State.EventSequence;
        void Collect()
        {
            facts.AddRange(game.CardFactSignals.Where(f => f.Sequence > fc));
            events.AddRange(game.State.Events.Where(e => e.Sequence > ec));
            if (facts.Count > 0) fc = facts[^1].Sequence;
            if (events.Count > 0) ec = events[^1].Sequence;
        }
        void Restore(string stage)
        {
            Collect(); var before = game.SerializeFullState();
            game = L12GameEngine.RestoreCheckpoint(catalog, before, game.RandomState!.Value, game.CardFactSignalSequence,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
            Assert.Equal(before, game.SerializeFullState());
            checkpoints.Add(new { stage, state = before, sha256 = Hash(before) });
        }
        Restore("registered-or-explicit-terminal-front");
        Call(game, "RequestStackProgress", false); Collect();
        for (var safety = 0; !Clean(game); safety++)
        {
            Assert.True(safety < 48, "SYNTHETIC_BOUNDARY_STALLED");
            Restore("before-command");
            if (game.State.PendingPrompts.Count != 1)
                Emit($"synthetic-{variant}-p{controller}-stalled", new { variant, controller, marker, checkpoints, commands, facts, events,
                    state = game.SerializeFullState(), stateSha256 = Hash(game.SerializeFullState()),
                    limits = "Constructed typed V2 scheduling boundary; no natural player-card reachability claimed." });
            var prompt = Assert.Single(game.State.PendingPrompts);
            var choice = prompt.Kind == "response" ? "pass"
                : variant == "decline" ? "mode:none"
                : variant == "cancel" && prompt.ValidChoices.Contains("mode:use") ? "mode:use" : "skip";
            Assert.Contains(choice, prompt.ValidChoices);
            var command = Typed(prompt, choice);
            var result = game.Handle(prompt.PlayerIndex, command);
            commands.Add(new { actor = prompt.PlayerIndex, command, result.Accepted, result.Error });
            Assert.True(result.Accepted, result.Error); Collect();
        }
        Restore("after-release-and-clean");
        var activated = facts.Where(f => f.Kind == "activate" && f.Data?.GetValueOrDefault("eventType") == "effect-trigger")
            .Select(f => f.CardInstanceId).ToArray();
        string[] parents = ["parent-deferred-2", "parent-deferred-1", "parent-candidate-1", "parent-batch", "parent-batch-second"];
        // Deferred items were already declared before this synthetic boundary, so only
        // their resolves are observed here. Pending candidates/batch declare afterwards.
        var resolved = facts.Where(f => f.Kind == "resolve" && f.Data?.GetValueOrDefault("trigger") == "enter")
            .Select(f => f.CardInstanceId).ToArray();
        Assert.Equal(marker && variant != "terminal-front" ? new[] { last.InstanceId }.Concat(parents) : parents, resolved);
        Assert.Equal(marker && variant != "terminal-front" ? new[] { last.InstanceId, "parent-candidate-1", "parent-batch", "parent-batch-second" }
            : new[] { "parent-candidate-1", "parent-batch", "parent-batch-second" }, activated);
        Assert.DoesNotContain(facts, f => f.Data?.GetValueOrDefault("trigger") == "ramsesContinuationEnd");
        Assert.DoesNotContain(game.State.Events, e => e.Type == "effect-trigger" && e.Cards.Any(c => c.InstanceId == parent.InstanceId));
        if (variant == "invalid") Assert.Contains(events, e => e.Type == "effect-skipped");
        if (variant is "decline" or "cancel")
        {
            Assert.False(game.State.Players[controller].Field[0][0]!.HasCharge);
            Assert.Equal("boundary-tomb", game.State.Players[controller].Field[1][0]!.InstanceId);
            Assert.DoesNotContain(facts, f => f.CardInstanceId == first.InstanceId && f.Kind == "push");
        }
        Emit($"synthetic-{variant}-p{controller}", new { variant, controller, marker, checkpoints, commands, facts, events,
            finalState = game.SerializeFullState(), finalStateSha256 = Hash(game.SerializeFullState()), clean = Clean(game),
            limits = "Constructed typed V2 scheduling boundaries; NOT natural paid match coverage. Terminal-front removes an unexecuted singleton solely to model the idle micro-boundary. Deferred items were already declared before the checkpoint." });
    }

    [Theory]
    [InlineData("missing-held")] [InlineData("empty-chain")] [InlineData("duplicate-held")]
    [InlineData("wrong-trigger")] [InlineData("mixed-batch")]
    public void MalformedTerminalMetadataFailsClosedWithoutExecutingAnEffect(string damage)
    {
        var game = Create(Catalog, 0, false);
        var marker = new L12TriggerCandidate
        {
            CandidateId = "synthetic-marker", Controller = 0, SourceInstanceId = Ramses, SourceCardId = "S01-0202",
            SourceName = "拉美西斯二世", Trigger = damage == "wrong-trigger" ? "enter" : "ramsesContinuationEnd", Text = string.Empty,
            Data = new() { ["ramsesContinuationEnd"] = "true", ["ramsesChainId"] = "synthetic-chain",
                ["ramsesHeldDeferredStackIds"] = "", ["ramsesHeldPendingCandidateIds"] = "" },
        };
        var batch = new L12TriggerBatch { BatchId = "synthetic-terminal", Controller = 0, Candidates = [marker] };
        if (damage == "missing-held") marker.Data.Remove("ramsesHeldDeferredStackIds");
        if (damage == "empty-chain") marker.Data["ramsesChainId"] = "";
        if (damage == "duplicate-held") marker.Data["ramsesHeldPendingCandidateIds"] = "x|x";
        if (damage == "mixed-batch") batch.Candidates.Add(marker);
        game.State.PendingTriggerBatches.Add(batch);
        var before = game.SerializeFullState();
        game = L12GameEngine.RestoreCheckpoint(Catalog, before, game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
        Assert.Equal(before, game.SerializeFullState());
        var exception = Assert.Throws<TargetInvocationException>(() => Call(game, "AdvanceTriggerBatches"));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(before, game.SerializeFullState());
    }

    private static object? Call(L12GameEngine game, string method, params object?[] arguments)
        => typeof(L12GameEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, arguments);

    private RunResult Run(int controller, bool restore, bool disaster, string boundary, bool lastDelegate = false)
    {
        var catalog = Catalog;
        var game = Create(catalog, controller, disaster);
        var caseId = $"synthetic-{boundary}-{(lastDelegate ? "last-" : "")}{(disaster ? "threshold" : "child")}-p{controller}-restore-{restore.ToString().ToLowerInvariant()}";
        var facts = new List<L12CardFactSignal>(); var events = new List<L12ActionEvent>();
        var commands = new List<object>(); var responses = new List<Response>();
        var factCursor = game.CardFactSignalSequence; var eventCursor = game.State.EventSequence;
        var restoreCount = 0; string? priorCheckpoint = null;
        string? injectedCheckpoint = null; bool? parentWaited = null;
        const string parentId = "guard-synthetic-parent-mozi";
        void Collect()
        {
            facts.AddRange(game.CardFactSignals.Where(f => f.Sequence > factCursor));
            events.AddRange(game.State.Events.Where(e => e.Sequence > eventCursor));
            if (facts.Count > 0) factCursor = facts[^1].Sequence;
            if (events.Count > 0) eventCursor = events[^1].Sequence;
        }
        void Restore()
        {
            if (!restore) return;
            Collect(); var before = game.SerializeFullState();
            game = L12GameEngine.RestoreCheckpoint(catalog, before, game.RandomState!.Value, game.CardFactSignalSequence,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
            Assert.Equal(before, game.SerializeFullState()); restoreCount++;
        }
        void Submit(int actor, L12Command command, string stage)
        {
            Restore();
            var before = JsonSerializer.SerializeToElement(new { game.State.PendingPrompts, game.State.EffectStack,
                game.State.PendingTriggerBatches, game.State.PendingTriggerStackCandidates, game.State.DeferredEffectStack }, JsonOptions);
            var result = game.Handle(actor, command); Collect();
            commands.Add(new { stage, actor, command, result.Accepted, result.Error, before,
                after = JsonSerializer.SerializeToElement(new { game.State.PendingPrompts, game.State.EffectStack,
                    game.State.PendingTriggerBatches, game.State.PendingTriggerStackCandidates, game.State.DeferredEffectStack,
                    game.State.DisasterValue, game.State.CheckDisasterAfterStack, game.State.ActiveDisaster }, JsonOptions),
                stateSha256 = Hash(game.SerializeFullState()) });
            Assert.True(result.Accepted, $"FIXTURE_COMMAND_REJECTED {stage}: {result.Error}");
        }
        void Drain(string stage)
        {
            for (var safety = 0; !Clean(game); safety++)
            {
                Assert.True(safety < 64, "FIXTURE_STALLED");
                var prompt = Assert.Single(game.State.PendingPrompts);
                if (prompt.Kind == "response")
                {
                    var top = game.State.EffectStack.Last();
                    if (top.SourceInstanceId == parentId && parentWaited is null)
                        parentWaited = facts.Any(f => f.Kind == "resolve" && f.CardInstanceId == Ptolemy)
                            && facts.Any(f => f.Kind == "resolve" && f.CardInstanceId == Next)
                            && facts.Any(f => f.Kind == "resolve" && f.CardInstanceId?.StartsWith("repeat-effect-", StringComparison.Ordinal) == true)
                            && facts.Any(f => f.Kind == "resolve" && f.CardInstanceId == Mozi && f.Data?.GetValueOrDefault("trigger") == "authority-event");
                    responses.Add(new(top.StackItemId, top.SourceInstanceId, prompt.PlayerIndex));
                    Submit(prompt.PlayerIndex, Typed(prompt, "pass"), stage + "-pass");
                }
                else if (prompt.Kind == "hand-card" && prompt.PlayerIndex == 1 - controller)
                {
                    Assert.Contains("guard-opponent-hand-0", prompt.ValidChoices);
                    Submit(prompt.PlayerIndex, Typed(prompt, ids: ["guard-opponent-hand-0"]), stage + "-nefertiti-discard");
                }
                else
                    Assert.Fail($"UNEXPECTED_PROMPT {prompt.Kind}/{prompt.Continuation}: {prompt.Text}; {string.Join(',', prompt.ValidChoices)}");
            }
        }
        try
        {
            Assert.Null(game.State.Players[controller].LastActiveTacticCardId);
            var ready = game.State.Players[controller].Morale.Count(m => !m.Tapped);
            Submit(controller, new("playCard", Tactic), "real-paid-prior-tactic");
            var tacticPaid = ready - game.State.Players[controller].Morale.Count(m => !m.Tapped);
            Drain("prior-tactic");
            var realPriorRecorded = game.State.Players[controller].LastActiveTacticCardId == "S01-0008"
                && game.State.Players[controller].LastActiveTacticTurnSerial == game.State.TurnSerial
                && game.State.Players[controller].Graveyard.Any(c => c.InstanceId == Tactic);
            Assert.True(realPriorRecorded);
            Assert.Equal(1000, game.State.Players[1 - controller].Field[0][0]!.Troops);
            Assert.Equal(Mozi, game.State.Players[1 - controller].Field[0][0]!.InstanceId);
            Assert.DoesNotContain(events, e => e.Type == "leave" && e.Cards.Any(c => c.InstanceId == Mozi));
            priorCheckpoint = game.SerializeFullState();

            ready = game.State.Players[controller].Morale.Count(m => !m.Tapped);
            Submit(controller, new("playCard", Ramses, Row: 0, Slot: 0), "real-paid-ramses");
            var ramsesPaid = ready - game.State.Players[controller].Morale.Count(m => !m.Tapped);
            var selection = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("pending-activation", selection.Continuation);
            Assert.All(new[] { Ptolemy, Next }, id => Assert.Contains(id, selection.ValidChoices));
            Submit(controller, Typed(selection, ids: lastDelegate ? [Next, Ptolemy] : [Ptolemy, Next]), "selected-delegate-order");
            // Deliberately construct an older sibling at a real current parent response boundary.
            // This is accepted/restored V2 state, not evidence that normal card play reaches it.
            Assert.Equal(Ramses, game.State.EffectStack.Last().SourceInstanceId);
            var siblingSource = Card(catalog, "S01-0110", parentId, 1 - controller);
            game.State.Players[1 - controller].Graveyard.Add(siblingSource);
            var siblingData = new Dictionary<string, string>
            {
                ["cause"] = "effect", ["triggerEffectText"] = "阵亡时 抽取1张牌。",
                ["presentationSceneId"] = "S01-0110:ability:death:8a11998853c0aa4d:presentation:death",
            };
            var snapshot = JsonSerializer.Deserialize<L12CardInstance>(JsonSerializer.Serialize(siblingSource))!;
            if (boundary is "pending-batch" or "pending-candidates")
            {
                siblingData["defer-qualification-until-turn"] = "true";
                var candidate = new L12TriggerCandidate
                {
                    CandidateId = $"trigger-{++game.State.TriggerBatchSequence}", Controller = 1 - controller,
                    SourceInstanceId = parentId, SourceCardId = siblingSource.CardId, SourceName = siblingSource.Name,
                    Trigger = "death", Text = "【阵亡时】效果", SourceSnapshot = snapshot, Data = siblingData,
                };
                if (boundary == "pending-batch")
                    game.State.PendingTriggerBatches.Add(new L12TriggerBatch
                    {
                        BatchId = $"batch-{++game.State.TriggerBatchSequence}", Controller = 1 - controller, Candidates = [candidate],
                    });
                else
                {
                    game.State.PendingTriggerStackCandidates.Add(candidate);
                }
            }
            else
            {
                Assert.Equal("deferred-stack", boundary);
                var item = new L12StackItem
                {
                    StackItemId = $"stack-{++game.State.StackSequence}", Controller = 1 - controller,
                    SourceInstanceId = parentId, SourceCardId = siblingSource.CardId, SourceName = siblingSource.Name,
                    Trigger = "death", Text = "阵亡时 抽取1张牌。", SourceSnapshot = snapshot,
                };
                foreach (var pair in siblingData) item.Data[pair.Key] = pair.Value;
                game.State.DeferredEffectStack.Add(item);
            }
            injectedCheckpoint = game.SerializeFullState();
            Restore();
            Assert.Equal(injectedCheckpoint, game.SerializeFullState());
            if (disaster) { Assert.True(game.State.CheckDisasterAfterStack); Assert.Null(game.State.ActiveDisaster); }
            Drain("ramses-chain"); Restore(); Collect();

            var child = Assert.Single(facts, f => f.CardInstanceId == Mozi && f.Kind == "resolve" && f.Data?.GetValueOrDefault("trigger") == "death");
            var childHandAdd = Assert.Single(facts, f => f.CardInstanceId == Mozi && f.Kind == "resolve" && f.Data?.GetValueOrDefault("trigger") == "authority-event");
            var next = Assert.Single(facts, f => f.CardInstanceId == Next && f.Kind == "activate" && f.Data?.GetValueOrDefault("eventType") == "effect-trigger");
            var repeat = Assert.Single(facts, f => f.CardInstanceId?.StartsWith("repeat-effect-", StringComparison.Ordinal) == true && f.Kind == "resolve");
            var terminals = facts.Where(f => f.Kind == "resolve" && (f.CardInstanceId is Ptolemy or Next || f.CardInstanceId == Mozi || f == repeat)).ToArray();
            var childDraw = events.Any(e => e.Type == "draw" && e.PlayerIndex == 1 - controller && e.Sequence >
                events.Single(e => e.Type == "leave" && e.Cards.Any(c => c.InstanceId == Mozi)).Sequence);
            var disasterEvent = disaster ? Assert.Single(events, e => e.Type == "disaster") : null;
            var required = responses.Where(r => r.SourceInstanceId is Tactic or Ramses or Ptolemy or Next or Mozi
                || r.SourceInstanceId.StartsWith("repeat-effect-", StringComparison.Ordinal)).Select(r => r.StackItemId).Distinct().ToArray();
            // Six card-effect windows plus Mozi's real effect-hand-add authority window.
            Assert.Equal(7, required.Length);
            var run = new RunResult(Clean(game), tacticPaid, ramsesPaid, realPriorRecorded,
                game.State.Players[1 - controller].Graveyard.Any(c => c.InstanceId == Mozi), childDraw,
                facts.Count(f => f == repeat), facts.Count(f => f == child), terminals.Count(f => f.CardInstanceId is Ptolemy or Next),
                child.Sequence, childHandAdd.Sequence, next.Sequence, child.Sequence < next.Sequence && childHandAdd.Sequence < next.Sequence,
                disasterEvent is null || events.Where(e => e.Type is "effect" or "effect-result" or "draw" or "leave"
                    && (e.Cards.Any(c => c.InstanceId is Ptolemy or Next or Mozi || c.InstanceId.StartsWith("repeat-effect-", StringComparison.Ordinal))
                        || e.Type == "draw")).All(e => e.Sequence < disasterEvent.Sequence), required, responses.ToArray(), parentWaited == true);
            var siblingResponses = responses.Where(r => r.SourceInstanceId == parentId).ToArray();
            Assert.Equal(4, siblingResponses.Length);
            Assert.Equal(2, siblingResponses.Select(r => r.StackItemId).Distinct().Count());
            Assert.Single(facts, f => f.CardInstanceId == parentId && f.Kind == "resolve" && f.Data?.GetValueOrDefault("trigger") == "death");
            Assert.Single(facts, f => f.CardInstanceId == parentId && f.Kind == "resolve" && f.Data?.GetValueOrDefault("trigger") == "authority-event");
            if (boundary == "pending-candidates")
            {
                var parentActivations = facts.Where(f => f.Kind == "activate"
                    && (f.CardInstanceId == parentId || f.CardInstanceId == parentId + "-second")
                    && f.Data?.GetValueOrDefault("eventType") == "effect-trigger").ToArray();
                Assert.Equal(new[] { parentId }, parentActivations.Select(f => f.CardInstanceId));
            }
            Assert.DoesNotContain(facts, f => f.Data?.GetValueOrDefault("trigger") == "ramsesContinuationEnd");
            Assert.DoesNotContain(responses, r => r.StackItemId.StartsWith("ramses-end-", StringComparison.Ordinal));
            var finalState = game.SerializeFullState();
            Emit(caseId, new { schema = 1, caseId, controller, restore, disaster, restoreCount, run,
                loadedProductSha256 = FileHash(typeof(L12GameEngine).Assembly.Location), commands, responses, facts, events,
                priorCheckpoint, injectedCheckpoint, boundary, parentWaited, finalState, finalStateSha256 = Hash(finalState),
                factsSha256 = Hash(JsonSerializer.Serialize(facts, JsonOptions)), eventsSha256 = Hash(JsonSerializer.Serialize(events, JsonOptions)),
                limits = "EXPLICITLY SYNTHETIC V2 SCHEDULING BOUNDARY. The parent death sibling is manually registered at a real Ramses response checkpoint, then restored byte-identically. Its typed schema/data follows the actually captured Mozi candidate. This is NOT a naturally reachable player-card chain. The prior tactic/Ramses payment and generated child remain real commands; no naive product proposal is executed." });
            output.WriteLine(JsonSerializer.Serialize(new { caseId, run, commands = commands.Count, restoreCount }));
            return run;
        }
        catch (Exception ex)
        {
            Collect(); Emit(caseId + "-fixture-failed", new { caseId, error = ex.ToString(), commands, responses, facts, events, priorCheckpoint, injectedCheckpoint, boundary, parentWaited, state = game.SerializeFullState() });
            throw;
        }
    }

    private static L12GameEngine Create(L12Catalog catalog, int controller, bool disaster)
    {
        var game = new L12GameEngine(catalog, "ramses-generated-child", "LOCAL", 68012, ["Synthetic A", "Synthetic B"], new[] { 0, 0 },
            skipPreparation: true, disasterMode: disaster ? "all" : "none", autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2, utcNow: () => Now);
        game.State.PendingPrompts.Clear(); game.State.ActivePlayer = controller; game.State.FirstPlayer = 0;
        game.State.Round = 2; game.State.TurnSerial = 3; game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null; game.State.DisasterDeck.Clear(); game.State.RemovedDisasters.Clear();
        game.State.DisasterValue = disaster ? 6 : 0;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3]; player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Removed.Clear(); player.Resolving.Clear();
            player.ExtraRelics.Clear(); player.Relic = null; player.SpecialZones.CanopicProgress.Clear();
            player.Morale.Clear(); player.MoraleDeck.Clear(); player.UsedAbilities.Clear(); player.TemporaryMorale = 0; player.FreeTacticCount = 0;
            for (var i = 0; i < 12; i++) player.Library.Add(Card(catalog, "S01-0002", $"guard-library-{player.PlayerIndex}-{i}", player.PlayerIndex));
        }
        var owner = game.State.Players[controller]; var opponent = game.State.Players[1 - controller];
        owner.Hand.Add(Card(catalog, "S01-0008", Tactic, controller)); owner.Hand.Add(Card(catalog, "S01-0202", Ramses, controller));
        owner.Field[0][1] = Card(catalog, "S01-0211", Ptolemy, controller); owner.Field[0][2] = Card(catalog, "S01-0209", Next, controller);
        opponent.Field[0][0] = Card(catalog, "S01-0110", Mozi, 1 - controller);
        // Hannibal's printed aura is same-row adjacency, not an all-field bonus.
        opponent.Field[0][1] = Card(catalog, "S02-0516", Hannibal, 1 - controller);
        for (var i = 0; i < 6; i++) opponent.Hand.Add(Card(catalog, "S01-0002", $"guard-opponent-hand-{i}", 1 - controller));
        for (var i = 0; i < 8; i++) owner.Morale.Add(new() { CardId = "S01-01C1", InstanceId = $"guard-morale-{i}" });
        if (disaster) game.State.DisasterDeck.Add(Card(catalog, "S01-DS02", "guard-disaster", controller));
        return game;
    }

    private static L12CardInstance Card(L12Catalog catalog, string id, string instance, int owner)
    {
        var d = catalog.Cards[id];
        return new() { CardId = id, InstanceId = instance, OwnerIndex = owner, Name = d.NameZh, CardType = d.CardType,
            IsCounterTactic = d.IsCounterTactic, Faction = d.Faction, ImageUrl = d.ImageUrl, Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue,
            EffectText = d.Effect, Traits = [.. d.Traits], Profession = d.Profession, BaseTroops = d.Troops ?? 0, Troops = d.Troops ?? 0,
            DisasterLevel = d.DisasterLevel ?? 0, TrialValue = d.TrialValue ?? 0, SummonRound = -1 };
    }
    private static L12Command Typed(L12Prompt p, string? choice = null, List<string>? ids = null)
        => new("resolvePrompt", PromptId: p.PromptId, Choice: choice, CardInstanceIds: ids, ActivationId: p.ActivationId,
            SourceInstanceId: p.SourceInstanceId, SourceCardId: p.SourceCardId, Step: p.Step, CreatedRevision: p.CreatedRevision, Controller: p.Controller);
    private static bool Clean(L12GameEngine g) => g.State.PendingPrompts.Count == 0 && g.State.PendingActivations.Count == 0
        && g.State.EffectStack.Count == 0 && g.State.DeferredEffectStack.Count == 0 && g.State.PendingTriggerBatches.Count == 0
        && g.State.PendingTriggerStackCandidates.Count == 0 && g.State.ResponseWindow is null && !g.State.IsResolvingStack
        && g.State.PendingDefense is null && g.State.Players.All(p => p.Resolving.Count == 0);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private void Emit(string name, object value)
    {
        var directory = Environment.GetEnvironmentVariable("L12_RAMSES_GUARD_EVIDENCE_DIR"); if (string.IsNullOrEmpty(directory)) return;
        Assert.True(Path.IsPathFullyQualified(directory)); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".json");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)); writer.Write(JsonSerializer.Serialize(value, JsonOptions));
    }
    private sealed record Response(string StackItemId, string SourceInstanceId, int Actor);
    private sealed record RunResult(bool Clean, int TacticPaid, int RamsesPaid, bool RealPriorRecorded, bool MoziDied, bool ChildDrew,
        int RepeatResolves, int ChildResolves, int DelegateResolves, long ChildResolveSequence, long ChildHandAddResolveSequence, long NextActivationSequence,
        bool ChildResolvedBeforeNext, bool DisasterLast, string[] RequiredResponseStacks, Response[] Responses, bool ParentWaitedForWholeChain);
}
