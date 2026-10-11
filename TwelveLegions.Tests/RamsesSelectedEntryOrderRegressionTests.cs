using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Tests;

// Synthetic public-command fixture. No product-path, private resolver or browser dependency.
// Optional evidence output is selected by the runner, not by a machine-specific path here.
public sealed class RamsesSelectedEntryOrderRegressionTests(ITestOutputHelper output)
{
    private const string RamsesId = "order-ramses";
    private const string A = "order-construct";
    private const string B = "order-nefertiti";
    private const string C = "order-ptolemy";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "entry:ramses-player-selected-declaration-order")]
    public void PaidRamsesDeclaresMixedEnterEffectsInSelectedOrder(int controller, bool restoreEachCommand)
    {
        var run = Run(controller, restoreEachCommand, [B, C, A], "mixed-bca");
        AssertCompleteFixture(run);

        // User-confirmed rule: one selected delegate declares and completes its whole
        // response/effect work before the next selected delegate declares. This is NOT
        // the generic simultaneous-trigger prompt's reverse-resolution contract.
        var actual = run.Activations.Select(fact => fact.CardInstanceId).ToArray();
        Assert.True(run.Selected.SequenceEqual(actual),
            $"RAMSES_SELECTED_DECLARATION_ORDER expected={string.Join(',', run.Selected)} actual={string.Join(',', actual)}; "
            + $"completion={string.Join(',', run.Completions.Select(fact => fact.CardInstanceId))}; "
            + $"allCommandsAccepted=true clean=true paid={run.Paid}; finalStateSha256={run.FinalStateSha256}");
        Assert.Equal(run.Selected, run.Pushes.Select(fact => fact.CardInstanceId).ToArray());
        Assert.Equal(run.Selected, run.Completions.Select(fact => fact.CardInstanceId).ToArray());
        for (var index = 0; index < run.Selected.Length - 1; index++)
        {
            var completed = Assert.Single(run.Completions, fact => fact.CardInstanceId == run.Selected[index]);
            var next = Assert.Single(run.Activations, fact => fact.CardInstanceId == run.Selected[index + 1]);
            Assert.True(completed.Sequence < next.Sequence,
                "The next selected delegate must not declare before the current delegate completes.");
        }
    }

    [Fact]
    [Trait("L12Evidence", "entry:ramses-all-direct-control-response-cleanup")]
    public void PaidRamsesAllDirectControlPreservesIndependentResponsesAndCleanup()
    {
        var run = Run(0, true, [C, A], "all-direct-ca");
        AssertCompleteFixture(run);
        Assert.DoesNotContain(run.Facts, fact => fact.CardInstanceId == B);
        // Control deliberately checks payment, independent responses, exactly-once
        // work and recovery, not the currently broken declaration-order contract.
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void RestoringEveryCommandPreservesFactsEventsAndFinalState(int controller)
    {
        var uninterrupted = Run(controller, false, [B, C, A], "paired-bca");
        var restored = Run(controller, true, [B, C, A], "paired-bca");
        Assert.Equal(uninterrupted.FactsSha256, restored.FactsSha256);
        Assert.Equal(uninterrupted.EventsSha256, restored.EventsSha256);
        Assert.Equal(uninterrupted.FinalStateSha256, restored.FinalStateSha256);
        AssertCompleteFixture(uninterrupted); AssertCompleteFixture(restored);
    }

    [Theory]
    [InlineData("none")] [InlineData("relic")] [InlineData("extra-relic")]
    public void FullCatalogRamsesDelegatesNeverOverlapPreStackEnterPayment(string ringLocation)
    {
        var catalog = Catalog;
        var owner = new L12PlayerState { PlayerIndex = 0, Name = "synthetic", DeckName = "synthetic",
            Faction = "taiyangcheng", MasterId = "S01-02M1", MasterName = "伊西斯" };
        var ring = Card(catalog, "S02-0008", "catalog-ring", 0);
        if (ringLocation == "relic") owner.Relic = ring;
        if (ringLocation == "extra-relic") owner.ExtraRelics.Add(ring);
        var immediate = typeof(L12GameEngine).GetMethod("HasImmediateEffect", BindingFlags.Static | BindingFlags.NonPublic)!;
        var eligible = catalog.Cards.Keys.Select(id => Card(catalog, id, "catalog-" + id, 0))
            .Where(card => card.CardType == "legion" && card.CardId != "S01-0202"
                && L12StructuredCardRules.HasFaction(owner, card, "taiyangcheng")
                && (bool)immediate.Invoke(null, [card, "enter"])!).ToArray();
        Assert.NotEmpty(eligible);
        Assert.DoesNotContain(eligible, L12StructuredCardRules.RequiresPreStackEnterCost);
        if (ringLocation != "none") Assert.Contains(eligible, card => card.Faction == "universal");
        output.WriteLine($"Catalog={catalog.Cards.Count}; ring={ringLocation}; eligible={string.Join(',', eligible.Select(c => c.CardId).Order())}");
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)]
    [InlineData(0, true)] [InlineData(1, true)]
    public void LastPtolemyWithNoLegalRepeatedTargetReleasesSyntheticDeferredParent(int controller, bool restore)
    {
        var catalog = Catalog; var game = Create(catalog, controller);
        const string tacticId = "noop-real-prior", victimId = "noop-only-enemy";
        game.State.Players[controller].Hand.Add(Card(catalog, "S02-0622", tacticId, controller));
        game.State.Players[1 - controller].Field[0][0] = Card(catalog, "S01-0002", victimId, 1 - controller);
        for (var i = 0; i < 3; i++) game.State.Players[controller].Morale.Add(new() { InstanceId = $"noop-extra-{i}", CardId = "S01-01C1" });
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
        void Restore()
        {
            Collect(); if (!restore) return;
            var before = game.SerializeFullState();
            game = L12GameEngine.RestoreCheckpoint(catalog, before, game.RandomState!.Value, game.CardFactSignalSequence,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
            Assert.Equal(before, game.SerializeFullState()); checkpoints.Add(new { state = before, sha256 = Hash(before) });
        }
        void Submit(int actor, L12Command command)
        {
            Restore(); var result = game.Handle(actor, command); Collect();
            commands.Add(new { actor, command, result.Accepted, result.Error }); Assert.True(result.Accepted, result.Error);
        }
        void Drain()
        {
            for (var i = 0; !Clean(game); i++)
            {
                Assert.True(i < 32); var prompt = Assert.Single(game.State.PendingPrompts);
                var choice = prompt.Kind == "response" ? "pass" : prompt.ValidChoices.Contains(victimId) ? victimId
                    : prompt.ValidChoices.First(c => c is "count:0" or "runes:0" or "mode:none");
                Submit(prompt.PlayerIndex, Typed(prompt, choice));
            }
        }
        Submit(controller, new("playCard", tacticId)); Drain();
        Assert.Equal(6, game.State.Players[controller].Morale.Count(m => !m.Tapped));
        Assert.Equal("S02-0622", game.State.Players[controller].LastActiveTacticCardId);
        Assert.Equal(game.State.TurnSerial, game.State.Players[controller].LastActiveTacticTurnSerial);
        Assert.Contains(game.State.Players[controller].Graveyard, c => c.InstanceId == tacticId);
        Assert.Contains(game.State.Players[1 - controller].Graveyard, c => c.InstanceId == victimId);
        Submit(controller, new("playCard", RamsesId, Row: 0, Slot: 0));
        Assert.Equal(0, game.State.Players[controller].Morale.Count(m => !m.Tapped));
        Submit(controller, Typed(Assert.Single(game.State.PendingPrompts), ids: [C]));
        var source = game.State.Players[controller].Field[0][1]!;
        // The prior tactic and Ramses are real paid commands. Only this older parent
        // is synthetic, to exercise ResumeAfterPostResolutionGeneratedInteraction.
        game.State.DeferredEffectStack.Add(new()
        {
            StackItemId = $"stack-{++game.State.StackSequence}", Controller = controller, SourceInstanceId = A,
            SourceCardId = source.CardId, SourceName = source.Name, SourceSnapshot = source,
            Trigger = "enter", Text = "【登场时】效果",
        });
        Restore(); Drain(); Restore(); Collect();
        var child = Assert.Single(facts, f => f.Kind == "resolve" && f.CardInstanceId == C);
        var parent = Assert.Single(facts, f => f.Kind == "resolve" && f.CardInstanceId == A);
        Assert.True(child.Sequence < parent.Sequence);
        Assert.Single(events, e => e.Type == "effect-noop" && e.Text.Contains("没有合法目标", StringComparison.Ordinal));
        Assert.DoesNotContain(facts, f => f.Kind == "push" && f.CardInstanceId?.StartsWith("repeat-effect-", StringComparison.Ordinal) == true);
        Assert.True(Clean(game));
        Emit($"last-generated-noop-p{controller}-restore-{restore}", new { controller, restore, commands, checkpoints, facts, events,
            finalState = game.SerializeFullState(), finalStateSha256 = Hash(game.SerializeFullState()),
            factsSha256 = Hash(JsonSerializer.Serialize(facts, JsonOptions)), eventsSha256 = Hash(JsonSerializer.Serialize(events, JsonOptions)),
            clean = Clean(game), limits = "Real paid prior tactic removes the only legal enemy; real Ramses delegates last Ptolemy. Only the old DeferredEffectStack parent is synthetic." });
    }

    private SceneRun Run(int controller, bool restoreEachCommand, string[] selected, string variant)
    {
        var catalog = Catalog;
        var game = Create(catalog, controller);
        var facts = new List<L12CardFactSignal>();
        var events = new List<L12ActionEvent>();
        var commands = new List<object>();
        var responses = new List<ResponseObservation>();
        var lastFact = game.CardFactSignalSequence;
        var lastEvent = game.State.EventSequence;
        var restoreCount = 0;
        var caseId = $"{variant}-p{controller}-restore-{restoreEachCommand.ToString().ToLowerInvariant()}";

        void Collect()
        {
            facts.AddRange(game.CardFactSignals.Where(fact => fact.Sequence > lastFact));
            if (facts.Count > 0) lastFact = facts[^1].Sequence;
            events.AddRange(game.State.Events.Where(entry => entry.Sequence > lastEvent));
            if (events.Count > 0) lastEvent = events[^1].Sequence;
        }
        void RestoreBoundary()
        {
            if (!restoreEachCommand) return;
            Collect();
            var before = game.SerializeFullState();
            game = L12GameEngine.RestoreCheckpoint(catalog, before, game.RandomState!.Value,
                game.CardFactSignalSequence, autoPassEmptyResponses: false,
                concealHiddenResponseAvailability: false, utcNow: () => Now);
            Assert.Equal(before, game.SerializeFullState());
            restoreCount++;
        }
        void Submit(int actor, L12Command command, string stage)
        {
            var before = JsonSerializer.SerializeToElement(game.State.PendingPrompts, JsonOptions);
            var result = game.Handle(actor, command);
            Collect();
            commands.Add(new
            {
                stage, actor, command, result.Accepted, result.Error, beforePrompts = before,
                afterPrompts = JsonSerializer.SerializeToElement(game.State.PendingPrompts, JsonOptions),
                stack = JsonSerializer.SerializeToElement(game.State.EffectStack, JsonOptions),
                deferred = JsonSerializer.SerializeToElement(game.State.DeferredEffectStack, JsonOptions),
                pendingCandidates = JsonSerializer.SerializeToElement(game.State.PendingTriggerStackCandidates, JsonOptions),
                pendingBatches = JsonSerializer.SerializeToElement(game.State.PendingTriggerBatches, JsonOptions),
                stateSha256 = Hash(game.SerializeFullState()),
            });
            Assert.True(result.Accepted, $"FIXTURE_COMMAND_REJECTED {stage}: {result.Error}");
        }

        string? paidCheckpoint = null;
        try
        {
            RestoreBoundary();
            var readyBefore = game.State.Players[controller].Morale.Count(card => !card.Tapped);
            Submit(controller, new L12Command("playCard", RamsesId, Row: 0, Slot: 0), "real-paid-playCard");
            var paid = readyBefore - game.State.Players[controller].Morale.Count(card => !card.Tapped);
            Assert.Equal(8, readyBefore);
            Assert.Equal(6, paid);
            Assert.Equal(RamsesId, game.State.Players[controller].Field[0][0]?.InstanceId);
            Assert.DoesNotContain(game.State.Players[controller].Hand, card => card.InstanceId == RamsesId);
            Assert.Equal(0, game.State.Players[controller].TemporaryMorale);
            paidCheckpoint = game.SerializeFullState();

            RestoreBoundary();
            var selection = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("pending-activation", selection.Continuation);
            Assert.Equal("S01-0202", selection.SourceCardId);
            Assert.Equal("board-target", selection.Data.GetValueOrDefault("choiceMode"));
            Assert.Equal(1, selection.MinChoose);
            Assert.Equal(3, selection.MaxChoose);
            Assert.All(selected, id => Assert.Contains(id, selection.ValidChoices));
            var dto = JsonSerializer.SerializeToElement(game.SnapshotFor(controller), JsonOptions);
            var dtoPrompt = dto.GetProperty("prompts").EnumerateArray().Single();
            foreach (var field in new[] { "step", "createdRevision", "controller" })
                Assert.Equal(JsonValueKind.Number, dtoPrompt.GetProperty(field).ValueKind);
            Submit(controller, Typed(selection, ids: selected.ToList()), "typed-selected-order");

            for (var safety = 0; !Clean(game); safety++)
            {
                Assert.True(safety < 64, "FIXTURE_STALLED_AFTER_64_COMMANDS");
                RestoreBoundary();
                var prompt = Assert.Single(game.State.PendingPrompts);
                if (prompt.Kind == "response")
                {
                    Assert.Contains("pass", prompt.ValidChoices);
                    var top = game.State.EffectStack.Last();
                    responses.Add(new(top.StackItemId, top.SourceInstanceId, prompt.PlayerIndex, prompt.PromptId));
                    Submit(prompt.PlayerIndex, Typed(prompt, choice: "pass"), $"response-{safety}");
                }
                else
                {
                    // In this bounded no-prior-tactic / empty-Tomb-Guard scene the
                    // only follow-up selection must be Nefertiti's actual discard.
                    Assert.Contains(B, selected);
                    Assert.Equal(1 - controller, prompt.PlayerIndex);
                    Assert.Equal("hand-card", prompt.Kind);
                    Assert.Equal(1, prompt.MinChoose);
                    Assert.Equal(1, prompt.MaxChoose);
                    Assert.Contains("opponent-hand-0", prompt.ValidChoices);
                    Submit(prompt.PlayerIndex, Typed(prompt, ids: ["opponent-hand-0"]), "actual-opponent-discard");
                }
            }
            RestoreBoundary();
            Collect();
            var activations = facts.Where(fact => selected.Contains(fact.CardInstanceId ?? "")
                && fact.Kind == "activate" && fact.Data?.GetValueOrDefault("eventType") == "effect-trigger").ToArray();
            var pushes = facts.Where(fact => selected.Contains(fact.CardInstanceId ?? "") && fact.Kind == "push").ToArray();
            var completions = facts.Where(fact => selected.Contains(fact.CardInstanceId ?? "")
                && fact.Kind is "resolve" or "fizzle" or "negate"
                && fact.Data?.GetValueOrDefault("trigger") == "enter").ToArray();
            var finalState = game.SerializeFullState();
            var run = new SceneRun(selected, facts.ToArray(), activations, pushes, completions,
                responses.ToArray(), paid, Clean(game), game.State.Phase, game.State.ActivePlayer,
                controller, game.State.Players[1 - controller].Hand.Count,
                game.State.Players[1 - controller].Graveyard.Select(card => card.InstanceId).ToArray(),
                Hash(finalState), Hash(JsonSerializer.Serialize(facts, JsonOptions)), Hash(JsonSerializer.Serialize(events, JsonOptions)));
            var evidence = new
            {
                schema = 1, caseId, controller, restoreEachCommand, restoreCount, selected, paid,
                loadedProductSha256 = FileHash(typeof(L12GameEngine).Assembly.Location),
                commands, responses, facts, events, paidCheckpoint, finalState,
                finalStateSha256 = run.FinalStateSha256,
                factsSha256 = Hash(JsonSerializer.Serialize(facts, JsonOptions)),
                eventsSha256 = Hash(JsonSerializer.Serialize(events, JsonOptions)),
                activationOrder = activations.Select(fact => fact.CardInstanceId),
                completionOrder = completions.Select(fact => fact.CardInstanceId),
                clean = run.Clean,
                limits = "Synthetic real paid command chain; A has no Tomb Guards and C has no earlier tactic. No successful benefit is claimed for these no-op resolves. No disaster or generated-child coverage in this first RED batch.",
            };
            Emit(caseId, evidence);
            output.WriteLine(JsonSerializer.Serialize(new { caseId, paid, restoreCount, commands = commands.Count,
                activations = activations.Select(fact => fact.CardInstanceId),
                completions = completions.Select(fact => fact.CardInstanceId), run.FinalStateSha256, run.Clean }));
            return run;
        }
        catch (Exception exception)
        {
            Collect();
            Emit(caseId + "-fixture-failed", new { caseId, error = exception.ToString(), commands, responses, facts,
                events, paidCheckpoint, currentState = game.SerializeFullState() });
            throw;
        }
    }

    private static void AssertCompleteFixture(SceneRun run)
    {
        Assert.Equal(6, run.Paid);
        Assert.True(run.Clean);
        Assert.Equal(L12Phase.Main, run.Phase);
        Assert.Equal(run.Controller, run.ActivePlayer);
        foreach (var id in run.Selected)
        {
            Assert.Single(run.Activations, fact => fact.CardInstanceId == id);
            Assert.Single(run.Pushes, fact => fact.CardInstanceId == id);
            var completion = Assert.Single(run.Completions, fact => fact.CardInstanceId == id);
            Assert.Equal("resolve", completion.Kind);
            var responses = run.Responses.Where(response => response.SourceInstanceId == id).ToArray();
            Assert.Equal(2, responses.Length);
            Assert.Equal(new[] { 0, 1 }, responses.Select(response => response.PlayerIndex).Order().ToArray());
            Assert.Single(responses.Select(response => response.StackItemId).Distinct());
        }
        if (run.Selected.Contains(B))
        {
            Assert.Equal(5, run.OpponentHandCount);
            Assert.Contains("opponent-hand-0", run.OpponentGraveIds);
        }
        else
        {
            Assert.Equal(6, run.OpponentHandCount);
            Assert.DoesNotContain("opponent-hand-0", run.OpponentGraveIds);
        }
    }

    private static L12GameEngine Create(L12Catalog catalog, int controller)
    {
        var game = new L12GameEngine(catalog, "synthetic-ramses-order", "LOCAL", 68003,
            ["Synthetic A", "Synthetic B"], new[] { 0, 0 }, skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2, utcNow: () => Now);
        game.State.PendingPrompts.Clear();
        game.State.ActivePlayer = controller; game.State.FirstPlayer = 0;
        game.State.Round = 2; game.State.TurnSerial = 3; game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null; game.State.DisasterDeck.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3]; player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Removed.Clear();
            player.Resolving.Clear(); player.ExtraRelics.Clear(); player.Relic = null;
            player.SpecialZones.CanopicProgress.Clear(); player.Morale.Clear(); player.MoraleDeck.Clear();
            player.UsedAbilities.Clear(); player.TemporaryMorale = 0; player.FreeTacticCount = 0;
            for (var index = 0; index < 12; index++)
                player.Library.Add(Card(catalog, "S01-0001", $"library-{player.PlayerIndex}-{index}", player.PlayerIndex));
        }
        var owner = game.State.Players[controller]; var opponent = game.State.Players[1 - controller];
        owner.Hand.Add(Card(catalog, "S01-0202", RamsesId, controller));
        owner.Field[0][1] = Card(catalog, "S01-0204", A, controller);
        owner.Field[0][2] = Card(catalog, "S01-0209", B, controller);
        owner.Field[1][0] = Card(catalog, "S01-0211", C, controller);
        for (var index = 0; index < 6; index++)
            opponent.Hand.Add(Card(catalog, "S01-0001", $"opponent-hand-{index}", 1 - controller));
        for (var index = 0; index < 8; index++)
            owner.Morale.Add(new() { InstanceId = $"order-payment-{controller}-{index}", CardId = "S01-01C1", Tapped = false });
        return game;
    }

    private static L12CardInstance Card(L12Catalog catalog, string id, string instanceId, int owner)
    {
        var definition = catalog.Cards[id];
        return new()
        {
            CardId = id, InstanceId = instanceId, OwnerIndex = owner, Name = definition.NameZh,
            CardType = definition.CardType, IsCounterTactic = definition.IsCounterTactic,
            Faction = definition.Faction, ImageUrl = definition.ImageUrl, Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue, EffectText = definition.Effect, Traits = [.. definition.Traits],
            Profession = definition.Profession, BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0, TrialValue = definition.TrialValue ?? 0, SummonRound = -1,
        };
    }

    private static L12Command Typed(L12Prompt prompt, string? choice = null, List<string>? ids = null)
        => new("resolvePrompt", PromptId: prompt.PromptId, Choice: choice, CardInstanceIds: ids,
            ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
            SourceCardId: prompt.SourceCardId, Step: prompt.Step,
            CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller);

    private static bool Clean(L12GameEngine game)
        => game.State.PendingPrompts.Count == 0 && game.State.PendingActivations.Count == 0
            && game.State.EffectStack.Count == 0 && game.State.DeferredEffectStack.Count == 0
            && game.State.PendingTriggerBatches.Count == 0 && game.State.PendingTriggerStackCandidates.Count == 0
            && game.State.ResponseWindow is null && !game.State.IsResolvingStack && game.State.PendingDefense is null
            && game.State.Players.All(player => player.Resolving.Count == 0);

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private void Emit(string caseId, object evidence)
    {
        var directory = Environment.GetEnvironmentVariable("L12_RAMSES_ORDER_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Assert.True(Path.IsPathFullyQualified(directory), "Evidence directory must be an absolute runner-selected path.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, caseId + ".json");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            writer.Write(JsonSerializer.Serialize(evidence, JsonOptions));
        output.WriteLine($"Evidence: {Path.GetFileName(path)} SHA256={FileHash(path)}");
    }

    private sealed record ResponseObservation(string StackItemId, string SourceInstanceId, int PlayerIndex, string PromptId);
    private sealed record SceneRun(string[] Selected, L12CardFactSignal[] Facts, L12CardFactSignal[] Activations,
        L12CardFactSignal[] Pushes, L12CardFactSignal[] Completions, ResponseObservation[] Responses,
        int Paid, bool Clean, L12Phase Phase, int ActivePlayer, int Controller, int OpponentHandCount,
        string[] OpponentGraveIds, string FinalStateSha256, string FactsSha256, string EventsSha256);
}
