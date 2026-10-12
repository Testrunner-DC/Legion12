using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

/// <summary>Synthetic public-board fixtures; not a player replay. Recovery means RestoreCheckpoint, not durable Journal.</summary>
public sealed class ProjectedCostTargetRegressionTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void CostlessLegionIsExcludedFromProjectedCandidates(int actor, bool yomi)
    {
        using var f = new Fixture(actor, yomi, false, $"candidate-{yomi}");
        f.Enemy.Field[0][0] = Card("S02-01S1", "dog", 1 - actor);
        f.Enemy.Field[0][1] = CostCard("first", yomi ? 4 : 1, 1 - actor);
        f.Enemy.Field[0][2] = CostCard("second", yomi ? 2 : 2, 1 - actor);
        f.Begin();
        var first = f.Prompt();
        var firstAllowedDog = first.ValidChoices.Contains("dog");
        f.Resolve("first");
        var second = f.Prompt();
        var secondAllowedDog = second.ValidChoices.Contains("dog");
        f.Note("candidates", new { firstAllowedDog, secondAllowedDog, second.ValidChoices });
        Assert.False(secondAllowedDog, "COSTLESS_MUST_NOT_BE_PROJECTED_COST_ZERO_OR_THRESHOLD");
        Assert.Equal(!yomi, firstAllowedDog); // Amaterasu's first segment is not a cost filter.
        f.Resolve(yomi ? "second" : "first");
        f.Drain();
        f.AssertPayment();
        Assert.Contains(f.Enemy.Field.SelectMany(row => row), c => c?.InstanceId == "dog");
        f.Complete();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void LegacyCostlessPromptSubmissionIsRejectedBeforePayment(int actor, bool yomi)
    {
        using var f = new Fixture(actor, yomi, true, $"stale-{yomi}");
        f.Enemy.Field[0][0] = CostCard("first", yomi ? 4 : 1, 1 - actor);
        f.Enemy.Field[0][1] = Card("S02-01S1", "dog", 1 - actor);
        f.Begin();
        f.Resolve("first");
        // Synthetic old/forged candidate list. Real costless card identity is never changed.
        var prompt = f.Prompt();
        var activation = Assert.Single(f.Game.State.PendingActivations);
        if (!prompt.ValidChoices.Contains("dog")) prompt.ValidChoices.Add("dog");
        if (!activation.SelectionSteps[activation.CurrentStep].ValidChoices.Contains("dog"))
            activation.SelectionSteps[activation.CurrentStep].ValidChoices.Add("dog");
        f.Note("synthetic-legacy-candidate", new { instanceId = "dog", hasPrintedCost = false });
        f.Recover();
        f.Resolve("dog"); // Outer prompt handling can accept while the inner activation rejects.
        Assert.True(f.Player.Morale.All(m => !m.Tapped), "COSTLESS_STALE_SUBMISSION_MUST_NOT_PAY");
        Assert.DoesNotContain(f.Player.UsedAbilities, key => key.StartsWith("active:master-", StringComparison.Ordinal));
        Assert.Empty(f.Game.State.EffectStack);
        Assert.Empty(f.Game.State.DeferredEffectStack);
        Assert.Contains(f.Game.State.Events, e => e.Type == "ability-rejected");
        f.Complete();
    }

    public static IEnumerable<object[]> PaidAndFreeCases()
    {
        foreach (var actor in new[] { 0, 1 })
        foreach (var yomi in new[] { false, true })
        foreach (var free in new[] { false, true })
        foreach (var restore in new[] { false, true })
            yield return [actor, yomi, free, restore];
    }

    [Theory]
    [MemberData(nameof(PaidAndFreeCases))]
    public void RealZeroAndProjectedBoundariesKeepPaymentAndFaithParity(int actor, bool yomi, bool free, bool restore)
    {
        using var f = new Fixture(actor, yomi, restore, $"legal-{yomi}-free-{free}");
        f.Enemy.Field[0][0] = Card("S02-01S1", "dog", 1 - actor);
        f.Enemy.Field[0][1] = CostCard("first", yomi ? 4 : 1, 1 - actor);
        f.Enemy.Field[0][2] = Card("S02-0201", "true-zero", 1 - actor);
        f.Enemy.Field[1][0] = CostCard("boundary", yomi ? 2 : 2, 1 - actor);
        f.Enemy.Field[1][1] = CostCard("above-first", yomi ? 5 : 3, 1 - actor);
        f.Enemy.Field[1][2] = CostCard("above-second", yomi ? 3 : 3, 1 - actor);
        Assert.False(f.Find("dog").HasPrintedCost);
        Assert.True(f.Find("true-zero").HasPrintedCost);
        Assert.Equal(0, f.Find("true-zero").Cost);
        f.Begin(free);
        Assert.Equal(!yomi, f.Prompt().ValidChoices.Contains("dog"));
        Assert.Equal(!yomi, f.Prompt().ValidChoices.Contains("above-first"));
        f.Resolve("first");
        var p = f.Prompt();
        Assert.DoesNotContain("dog", p.ValidChoices);
        Assert.Contains("true-zero", p.ValidChoices);
        Assert.Equal(!yomi, p.ValidChoices.Contains("first")); // Yomi's second target must differ.
        Assert.Equal(yomi, p.ValidChoices.Contains("boundary"));
        Assert.DoesNotContain("above-second", p.ValidChoices);
        if (!yomi) Assert.DoesNotContain("mode:none", p.ValidChoices);
        f.AssertNotYetPaid(free);
        var submission = Fixture.Typed(p, "true-zero");
        f.Send(p.PlayerIndex, submission);
        f.AssertPayment(free);
        f.Send(p.PlayerIndex, submission, false); // Duplicate submission must not pay again.
        f.Drain();
        Assert.Contains(f.Enemy.Graveyard, c => c.InstanceId == "true-zero");
        Assert.Contains(f.Enemy.Field.SelectMany(row => row), c => c?.InstanceId == "dog");
        if (yomi)
        {
            Assert.Contains(f.Enemy.Graveyard, c => c.InstanceId == "first");
            Assert.Equal(1, f.Find("boundary").CurrentCost);
            Assert.Equal(4, f.Find("above-first").CurrentCost);
            Assert.Equal(2, f.Find("above-second").CurrentCost);
            Assert.Contains(f.Player.Hand, c => c.InstanceId == "draw-card");
            Assert.Equal(["yomi-draw", "yomi-cost-debuff", "yomi-kill3", "yomi-kill1"], f.MasterFlows);
        }
        else
        {
            Assert.Equal(0, f.Find("first").CurrentCost);
            Assert.Equal(2, f.Find("boundary").CurrentCost);
            Assert.Equal(["amaterasu-debuff", "amaterasu-kill"], f.MasterFlows);
        }
        f.AssertPayment(free);
        if (!free) f.Send(actor, new("activateAbility", $"master-{actor}", Ability: f.Ability), false);
        f.Complete();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void AmaterasuCostlessOnlyFirstTargetRemainsLegalAndSecondIsNone(int actor, bool free)
    {
        using var f = new Fixture(actor, false, true, $"dog-only-free-{free}");
        f.Enemy.Field[0][0] = Card("S02-01S1", "dog", 1 - actor);
        f.Begin(free);
        Assert.Contains("dog", f.Prompt().ValidChoices);
        f.Resolve("dog");
        Assert.Equal(["mode:none"], f.Prompt().ValidChoices);
        f.Resolve("mode:none"); f.Drain();
        Assert.False(f.Find("dog").HasPrintedCost);
        Assert.False(L12StructuredCardRules.CurrentCostEquals(f.Find("dog"), 0));
        f.AssertPayment(free); f.Complete();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AmaterasuCurrentTwoProjectsToOneNotZero(int actor)
    {
        using var f = new Fixture(actor, false, true, "two-to-one");
        f.Enemy.Field[0][0] = CostCard("first", 2, 1 - actor);
        f.Enemy.Field[0][1] = Card("S02-01S1", "dog", 1 - actor);
        f.Begin(); f.Resolve("first");
        Assert.Equal(["mode:none"], f.Prompt().ValidChoices);
        f.Resolve("mode:none"); f.Drain();
        Assert.Equal(1, f.Find("first").CurrentCost);
        f.AssertPayment(); f.Complete();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void RealFaithLegacyCostlessSubmissionHasNoMasterPaymentOrStack(int actor, bool yomi)
    {
        using var f = new Fixture(actor, yomi, true, $"faith-old-dog-{yomi}");
        f.Enemy.Field[0][0] = CostCard("first", yomi ? 4 : 1, 1 - actor);
        f.Enemy.Field[0][1] = Card("S02-01S1", "dog", 1 - actor);
        f.Begin(true); f.Resolve("first");
        f.InjectLegacyChoice("dog");
        var moraleBefore = f.Player.Morale.Select(m => (m.InstanceId, m.Tapped)).ToArray();
        f.Resolve("dog");
        Assert.Equal(moraleBefore, f.Player.Morale.Select(m => (m.InstanceId, m.Tapped)));
        Assert.DoesNotContain(f.Player.UsedAbilities, key => key.StartsWith("active:master-", StringComparison.Ordinal));
        Assert.Contains(f.Game.State.Events, e => e.Type == "ability-rejected");
        Assert.DoesNotContain(f.Game.State.Events, e => e.Type == "effect" && e.Text.Contains("无视全部消耗触发"));
        f.Complete();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void MandatoryKillRejectsForgedNoneAndLegacyNoneBeforeCost(int actor, bool free)
    {
        using var f = new Fixture(actor, false, true, $"forged-none-free-{free}");
        f.Enemy.Field[0][0] = CostCard("first", 1, 1 - actor);
        f.Begin(free); f.Resolve("first");
        var p = f.Prompt();
        Assert.DoesNotContain("mode:none", p.ValidChoices);
        f.Send(actor, Fixture.Typed(p, "mode:none"), false);
        f.AssertNotYetPaid(free);
        f.InjectLegacyChoice("mode:none");
        f.Resolve("mode:none");
        Assert.Contains(f.Game.State.Events, e => e.Type == "ability-rejected");
        f.AssertNotYetPaid(free); f.Complete();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void YomiRejectsDuplicateTargetsButCanDeclineBoth(int actor, bool free)
    {
        using var f = new Fixture(actor, true, true, $"yomi-duplicate-free-{free}");
        f.Enemy.Field[0][0] = CostCard("first", 2, 1 - actor);
        f.Begin(free); f.Resolve("first");
        Assert.DoesNotContain("first", f.Prompt().ValidChoices);
        f.InjectLegacyChoice("first"); f.Resolve("first");
        Assert.Contains(f.Game.State.Events, e => e.Type == "ability-rejected");
        f.AssertNotYetPaid(free); f.Complete();
        using var skip = new Fixture(actor, true, true, $"yomi-skip-free-{free}");
        skip.Enemy.Field[0][0] = CostCard("first", 2, 1 - actor);
        skip.Begin(free);
        Assert.Contains("mode:none", skip.Prompt().ValidChoices);
        skip.Resolve("mode:none"); skip.Resolve("mode:none"); skip.Drain();
        Assert.Equal(1, skip.Find("first").CurrentCost);
        Assert.Contains(skip.Player.Hand, c => c.InstanceId == "draw-card");
        skip.AssertPayment(free); skip.Complete();
    }

    [Theory]
    [InlineData(0, "leave")]
    [InlineData(1, "leave")]
    [InlineData(0, "hidden")]
    [InlineData(1, "hidden")]
    [InlineData(0, "cost")]
    [InlineData(1, "cost")]
    [InlineData(0, "negated")]
    [InlineData(1, "negated")]
    public void PaidAmaterasuInvalidatedTargetNeverRetargetsOrRefunds(int actor, string change)
    {
        using var f = new Fixture(actor, false, true, $"paid-invalid-{change}");
        f.Enemy.Field[0][0] = CostCard("first", 1, 1 - actor);
        f.Enemy.Field[0][1] = Card("S02-0201", "other-zero", 1 - actor);
        f.Begin(); f.Resolve("first"); f.Resolve("first"); f.AssertPayment();
        // Bounded response-time invalidation fixture, not claimed as a second card's real effect.
        switch (change)
        {
            case "leave": f.Enemy.Hand.Add(f.Find("first")); f.Enemy.Field[0][0] = null; break;
            case "hidden": f.Find("first").Hidden = true; break;
            case "cost": f.Find("first").CostModifier += 1; break;
            case "negated": Assert.Single(f.Game.State.EffectStack).Negated = true; break;
        }
        f.Note("synthetic-response-invalidation", change); f.Recover(); f.Drain();
        Assert.DoesNotContain(f.Enemy.Graveyard, c => c.InstanceId is "first" or "other-zero");
        Assert.Equal(0, f.Find("other-zero").CurrentCost);
        Assert.Contains(f.Game.State.Events, e => e.Type == "effect-failed");
        f.AssertPayment(); f.Complete();
    }

    [Theory]
    [InlineData("S02-01S1", 0, -1, 0, false)]
    [InlineData("S02-01S1", 0, -1, 3, false)]
    [InlineData("S02-0201", 0, -1, 0, true)]
    [InlineData("S01-0001", 1, -1, 0, true)]
    [InlineData("S01-0001", 2, -1, 0, false)]
    [InlineData("S01-0001", 4, -1, 3, true)]
    [InlineData("S01-0001", 5, -1, 3, false)]
    [InlineData("S01-0001", 2, -1, 1, true)]
    [InlineData("S01-0001", 3, -1, 1, false)]
    public void SharedProjectedPredicateKeepsPrintedIdentityAndNonnegativeBoundary(string id, int current, int delta, int maximum, bool expected)
    {
        var card = Card(id, "predicate", 0); card.CostModifier = current - card.Cost;
        Assert.Equal(expected, L12StructuredCardRules.ProjectedCurrentCostAtMost(card, delta, maximum));
        if (id == "S02-01S1")
        {
            Assert.True(L12StructuredCardRules.SearchCostAtMost(card, 0)); // Existing library-search exception is unchanged.
            Assert.False(L12StructuredCardRules.CurrentCostEquals(card, 0));
        }
    }

    [Fact]
    public void AllEightProjectionSitesShareTheHelperWithoutChangingFinalOrSearchFilters()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "服务端WebSocket", "TwelveLegions"))) root = root.Parent;
        Assert.NotNull(root);
        string Read(string name) => File.ReadAllText(Path.Combine(root!.FullName, "服务端WebSocket", "TwelveLegions", name + ".cs"));
        foreach (var (file, count) in new[] { ("L12RuleKernelIntegration", 2), ("L12ActiveAbilities", 3), ("L12S1FactionEffects", 3) })
        {
            var source = Read(file);
            Assert.Equal(count, Regex.Matches(source, @"L12StructuredCardRules\.ProjectedCurrentCostAtMost\(").Count);
            Assert.DoesNotMatch(@"Math\.Max\(0,\s*card\.CurrentCost", source);
            Assert.DoesNotMatch(@"card\.CurrentCost\s*-\s*1\s*<=", source);
        }
        var rules = Read("L12StructuredCardRules");
        Assert.Contains("card.HasPrintedCost && Math.Max(0, card.CurrentCost + modifierDelta) <= maximum", rules);
        Assert.Contains("(card.HasPrintedCost ? card.CurrentCost : 0) <= maximum", rules);
        Assert.Contains("L12StructuredCardRules.CurrentCostEquals(card, 0)", Read("L12CompositeEffectPlans"));
        Assert.Contains("L12StructuredCardRules.CurrentCostAtMost(card, 3)", Read("L12CompositeEffectPlans"));
        Assert.Contains("L12StructuredCardRules.CurrentCostAtMost(card, 1)", Read("L12CompositeEffectPlans"));
        Assert.Contains("L12StructuredCardRules.CurrentCostEquals(card, 0)", Read("L12S1FactionEffects"));
    }

    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-12T08:00:00+08:00");
    private static JsonElement Freeze(object value) => JsonSerializer.SerializeToElement(value, JsonOptions);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var d = Catalog.Cards[id];
        return new() { InstanceId = instance, CardId = id, Name = d.NameZh, OwnerIndex = owner,
            CardType = d.CardType, Faction = d.Faction, ImageUrl = d.ImageUrl, Cost = d.Cost ?? 0,
            HasPrintedCost = d.Cost.HasValue, EffectText = d.Effect, Traits = [.. d.Traits],
            Profession = d.Profession, EffectiveProfession = d.Profession, BaseTroops = d.Troops ?? 0,
            Troops = d.Troops ?? 0, DisasterLevel = d.DisasterLevel ?? 0, SummonRound = -1 };
    }
    private static L12CardInstance CostCard(string instance, int cost, int owner)
    {
        var card = Card("S01-0001", instance, owner);
        card.CostModifier = cost - card.Cost; // Explicit synthetic pre-existing modifier, not altered printed identity.
        return card;
    }

    private sealed class Fixture : IDisposable
    {
        public L12GameEngine Game;
        public readonly int Actor;
        public readonly bool Yomi;
        private readonly bool restore;
        private readonly string scenario;
        private readonly List<JsonElement> commands = [];
        private readonly List<JsonElement> checkpoints = [];
        private readonly List<JsonElement> notes = [];
        private readonly List<JsonElement> facts = [];
        private long factCursor;
        public readonly List<string> MasterFlows = [];
        private bool complete;
        public L12PlayerState Player => Game.State.Players[Actor];
        public L12PlayerState Enemy => Game.State.Players[1 - Actor];
        public string Ability => Yomi ? "yomiSweep" : "amaterasuKill";
        public Fixture(int actor, bool yomi, bool restore, string scenario)
        {
            Actor = actor; Yomi = yomi; this.restore = restore; this.scenario = scenario;
            var baseDeck = Catalog.DeckAt(3);
            var deck = new L12PresetDeckDefinition { Name = "Synthetic projected-cost fixture",
                MasterId = yomi ? "S01-04D1" : "S01-04M1", CardIds = [.. baseDeck.CardIds],
                MoraleIds = [.. baseDeck.MoraleIds], SpecialIds = [] };
            Game = new(Catalog, "projected-cost", "LOCAL", 101200, ["Synthetic A", "Synthetic B"],
                [deck, deck], skipPreparation: true, disasterMode: "none",
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
                stateFormatVersion: 2, utcNow: () => Now);
            Game.State.PendingPrompts.Clear();
            Game.State.ActivePlayer = actor; Game.State.FirstPlayer = actor;
            Game.State.Round = 2; Game.State.TurnSerial = 4; Game.State.Phase = L12Phase.Main;
            Game.State.ActiveDisaster = null; Game.State.DisasterDeck.Clear();
            foreach (var p in Game.State.Players)
            {
                p.Field[0] = new L12CardInstance?[3]; p.Field[1] = new L12CardInstance?[3];
                p.Hand.Clear(); p.Library.Clear(); p.Graveyard.Clear(); p.Removed.Clear(); p.Resolving.Clear();
                p.Morale.Clear(); p.MoraleDeck.Clear(); p.UsedAbilities.Clear(); p.Relic = null; p.ExtraRelics.Clear();
                p.TemporaryMorale = 0; p.FreeTacticCount = 0;
            }
            for (var i = 0; i < (yomi ? 2 : 1); i++)
                Player.Morale.Add(new() { InstanceId = $"cost-{i}", CardId = "S01-04C1" });
            Player.Library.Add(Card("S01-0002", "draw-card", actor));
            factCursor = Game.CardFactSignalSequence;
        }
        public void Note(string label, object data) => notes.Add(Freeze(new { label, data }));
        public L12Prompt Prompt() => Assert.Single(Game.State.PendingPrompts);
        public L12CardInstance Find(string id) => Assert.Single(Enemy.Field.SelectMany(row => row).OfType<L12CardInstance>(), c => c.InstanceId == id);
        public void Begin(bool free = false)
        {
            if (!free) { Note("initial", Game.SerializeFullState()); Send(Actor, new("activateAbility", $"master-{Actor}", Ability: Ability)); return; }
            var heracles = Card("S02-0502", "heracles", Actor);
            Player.Hand.AddRange([heracles, Card("S02-0006", "faith-zealot", Actor)]);
            Player.Library.Insert(0, Card("S01-0002", "faith-draw-1", Actor));
            Player.Library.Insert(0, Card("S01-0002", "faith-draw-2", Actor));
            Player.Morale.Clear();
            for (var i = 0; i < heracles.CurrentCost; i++)
                Player.Morale.Add(new() { InstanceId = $"faith-entry-cost-{i}", CardId = "S01-04C1" });
            Note("initial-real-faith", Game.SerializeFullState());
            Send(Actor, new("playCard", "heracles", Row: 0, Slot: 0));
            Resolve("mode:use"); PassResponses();
            Assert.Equal("post-draw-private", Prompt().Data.GetValueOrDefault("declarationTiming"));
            Resolve("faith-zealot"); PassResponses(); Resolve("mode:use"); PassResponses();
            Assert.Equal("s2-faith-zealot", Prompt().Data.GetValueOrDefault("action"));
            Resolve(Ability);
            Assert.All(Player.Morale, m => Assert.True(m.Tapped));
            Assert.Contains(Player.Graveyard, c => c.InstanceId == "faith-zealot");
        }
        public void InjectLegacyChoice(string value)
        {
            var p = Prompt(); var activation = Assert.Single(Game.State.PendingActivations);
            if (!p.ValidChoices.Contains(value)) p.ValidChoices.Add(value);
            if (!activation.SelectionSteps[activation.CurrentStep].ValidChoices.Contains(value))
                activation.SelectionSteps[activation.CurrentStep].ValidChoices.Add(value);
            Note("synthetic-legacy-candidate", value); Recover();
        }
        private void Collect()
        {
            foreach (var fact in Game.CardFactSignals.Where(f => f.Sequence > factCursor).OrderBy(f => f.Sequence))
            { facts.Add(Freeze(fact)); factCursor = fact.Sequence; }
            foreach (var item in Game.State.EffectStack)
            {
                var flow = item.Data.GetValueOrDefault("atomicFlow", "");
                if ((flow.StartsWith("yomi-", StringComparison.Ordinal) || flow.StartsWith("amaterasu-", StringComparison.Ordinal))
                    && !MasterFlows.Contains(flow)) MasterFlows.Add(flow);
            }
        }
        public static L12Command Typed(L12Prompt p, string choice) => new("resolvePrompt", PromptId: p.PromptId,
            Choice: choice, ActivationId: p.ActivationId, SourceInstanceId: p.SourceInstanceId,
            SourceCardId: p.SourceCardId, Step: p.Step, CreatedRevision: p.CreatedRevision, Controller: p.Controller);
        public void Send(int player, L12Command command, bool expected = true)
        {
            var before = Game.SerializeFullState(); var prompt = Freeze(Game.State.PendingPrompts);
            var result = Game.Handle(player, command);
            commands.Add(Freeze(new { player, command, promptsBefore = prompt, result.Accepted, result.Error,
                beforeHash = Hash(before), afterHash = Hash(Game.SerializeFullState()) }));
            Collect();
            Assert.Equal(expected, result.Accepted);
            if (restore) Recover();
        }
        public void Resolve(string choice) { var p = Prompt(); Send(p.PlayerIndex, Typed(p, choice)); }
        public void Recover()
        {
            Collect();
            var before = Game.SerializeFullState(); var random = Game.RandomState!.Value; var sequence = Game.CardFactSignalSequence;
            var next = L12GameEngine.RestoreCheckpoint(Catalog, before, random, sequence,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
            checkpoints.Add(Freeze(new { beforeHash = Hash(before), afterHash = Hash(next.SerializeFullState()),
                randomBefore = random, randomAfter = next.RandomState, sequenceBefore = sequence, sequenceAfter = next.CardFactSignalSequence }));
            Assert.Equal(before, next.SerializeFullState()); Assert.Equal(random, next.RandomState);
            Assert.Equal(sequence, next.CardFactSignalSequence); Game = next;
        }
        public void Drain()
        {
            for (var safety = 0; safety < 60 && Game.State.PendingPrompts.Count > 0; safety++)
            {
                var p = Prompt();
                if (p.Kind == "response") Resolve("pass");
                else
                {
                    Assert.Equal("S01-0001", p.SourceCardId);
                    Assert.Equal("pending-activation", p.Continuation);
                    Assert.Contains("mode:none", p.ValidChoices); Resolve("mode:none");
                }
            }
        }
        private void PassResponses()
        {
            for (var safety = 0; safety < 60 && Game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++) Resolve("pass");
        }
        public void AssertNotYetPaid(bool free)
        {
            Assert.All(Player.Morale, m => Assert.Equal(free, m.Tapped));
            Assert.DoesNotContain(Player.UsedAbilities, key => key.StartsWith($"active:master-{Actor}:", StringComparison.Ordinal));
        }
        public void AssertPayment(bool free = false)
        {
            Assert.All(Player.Morale, m => Assert.True(m.Tapped));
            if (free) Assert.DoesNotContain(Player.UsedAbilities, key => key.StartsWith($"active:master-{Actor}:", StringComparison.Ordinal));
            else Assert.Contains(Player.UsedAbilities, key => key.StartsWith($"active:master-{Actor}:", StringComparison.Ordinal));
        }
        public void Complete()
        {
            Assert.Empty(Game.State.PendingPrompts); Assert.Empty(Game.State.PendingActivations);
            Assert.Empty(Game.State.EffectStack); Assert.Empty(Game.State.DeferredEffectStack);
            Assert.Empty(Game.State.PendingTriggerBatches); Assert.Empty(Game.State.PendingTriggerStackCandidates);
            Assert.Null(Game.State.ResponseWindow); Assert.Null(Game.State.FreeMasterActivation);
            Assert.False(Game.State.IsResolvingStack); Assert.All(Game.State.Players, p => Assert.Empty(p.Resolving));
            Recover(); complete = true;
        }
        public void Dispose()
        {
            var folder = Environment.GetEnvironmentVariable("L12_PROJECTED_COST_EVIDENCE_DIR");
            if (string.IsNullOrEmpty(folder)) return;
            Assert.True(Path.IsPathFullyQualified(folder)); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{scenario}-p{Actor}-restore-{restore}.json");
            using var stream = new FileStream(path, FileMode.CreateNew);
            Collect();
            JsonSerializer.Serialize(stream, new { scenario, Actor, Yomi, restore, complete, commands, checkpoints, notes, facts, MasterFlows,
                events = Game.State.Events, finalState = Game.SerializeFullState(), finalStateSha256 = Hash(Game.SerializeFullState()),
                randomState = Game.RandomState, cardFactSignalSequence = Game.CardFactSignalSequence }, JsonOptions);
        }
    }
}
