using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Tests;

// Public-command, synthetic setup fixture. A real prior tactic establishes LastActiveTactic.
// No private resolver calls, manually registered triggers, or card-identity overrides.
public sealed class RamsesGeneratedChildOrderRegressionTests(ITestOutputHelper output)
{
    private const string Ramses = "guard-ramses", Ptolemy = "guard-ptolemy", Next = "guard-nefertiti";
    private const string Tactic = "guard-prior-siege", Mozi = "guard-mozi", Hannibal = "guard-hannibal";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-12T00:00:00Z");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(0, true)] [InlineData(1, true)]
    [Trait("L12Evidence", "entry:ramses-generated-death-child-before-next-delegate")]
    public void RealPriorTacticRepeatAndDeathChildCompleteBeforeNextDelegate(int controller, bool restore)
    {
        var run = Run(controller, restore, false);
        AssertComplete(run);
        Assert.True(run.ChildResolvedBeforeNext,
            $"RAMSES_GENERATED_CHILD_BEFORE_NEXT childResolve={run.ChildResolveSequence} handAddResolve={run.ChildHandAddResolveSequence} nextActivate={run.NextActivationSequence}; "
            + $"realPriorTactic=true actualStateDeath=true childDraw=true clean={run.Clean}");
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(0, true)] [InlineData(1, true)]
    [Trait("L12Evidence", "entry:ramses-generated-child-threshold-disaster-last")]
    public void ThresholdDisasterWaitsForRepeatedTacticDeathChildAndAllDelegates(int controller, bool restore)
    {
        var run = Run(controller, restore, true);
        AssertComplete(run);
        Assert.True(run.DisasterLast, "RAMSES_DISASTER_LAST must follow both delegates, repeated tactic and actual Mozi death trigger.");
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

    private RunResult Run(int controller, bool restore, bool disaster)
    {
        var catalog = Catalog;
        var game = Create(catalog, controller, disaster);
        var caseId = $"{(disaster ? "threshold" : "child")}-p{controller}-restore-{restore.ToString().ToLowerInvariant()}";
        var facts = new List<L12CardFactSignal>(); var events = new List<L12ActionEvent>();
        var commands = new List<object>(); var responses = new List<Response>();
        var factCursor = game.CardFactSignalSequence; var eventCursor = game.State.EventSequence;
        var restoreCount = 0; string? priorCheckpoint = null;
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
            Submit(controller, Typed(selection, ids: [Ptolemy, Next]), "selected-ptolemy-then-nefertiti");
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
                        || e.Type == "draw")).All(e => e.Sequence < disasterEvent.Sequence), required, responses.ToArray());
            var finalState = game.SerializeFullState();
            Emit(caseId, new { schema = 1, caseId, controller, restore, disaster, restoreCount, run,
                loadedProductSha256 = FileHash(typeof(L12GameEngine).Assembly.Location), commands, responses, facts, events,
                priorCheckpoint, finalState, finalStateSha256 = Hash(finalState),
                factsSha256 = Hash(JsonSerializer.Serialize(facts, JsonOptions)), eventsSha256 = Hash(JsonSerializer.Serialize(events, JsonOptions)),
                limits = "Synthetic initial board and resources. Both cards are really paid, prior tactic is fully resolved and recorded by the public engine. Mozi state death is caused by the repeated real tactic. No manual LastActiveTactic assignment. No parent sibling fabricated; no naive proposal executed." });
            output.WriteLine(JsonSerializer.Serialize(new { caseId, run, commands = commands.Count, restoreCount }));
            return run;
        }
        catch (Exception ex)
        {
            Collect(); Emit(caseId + "-fixture-failed", new { caseId, error = ex.ToString(), commands, responses, facts, events, priorCheckpoint, state = game.SerializeFullState() });
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
        bool ChildResolvedBeforeNext, bool DisasterLast, string[] RequiredResponseStacks, Response[] Responses);
}
