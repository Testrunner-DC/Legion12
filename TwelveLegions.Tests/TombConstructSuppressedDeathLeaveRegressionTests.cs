using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Tests;

// Synthetic initial board only; the primary regression uses real paid play/turn/master/counter
// commands. No machine path, product DLL path, private player data, or GM action is required.
public sealed class TombConstructSuppressedDeathLeaveRegressionTests(ITestOutputHelper output)
{
    private const string Construct = "mengpo-construct", GuardA = "mengpo-guard-a", GuardB = "mengpo-guard-b";
    private const string Kill = "mengpo-tian-zhu", Counter = "mengpo-absolute", Discard = "mengpo-absolute-cost";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-12T13:00:00Z");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(0, false, false)] [InlineData(0, true, false)]
    [InlineData(1, false, false)] [InlineData(1, true, false)]
    [InlineData(0, false, true)] [InlineData(0, true, true)]
    [InlineData(1, false, true)] [InlineData(1, true, true)]
    [Trait("L12Evidence", "repro:BUG-20261021-187df451-mandatory-death-leave")]
    public void MandatoryDeathOrSuppressedLeaveRejectsEveryDeclineWithoutMutation(
        int actor, bool restore, bool suppressDeath)
    {
        var f = Prepared(actor, restore, "mandatory-" + suppressDeath);
        if (suppressDeath) f.SuppressWithMengpo();
        f.KillWithTianZhu();
        f.Drain(stopAtConstruct: true);
        for (var index = 0; index < f.GuardIds.Count; index++)
        {
            var prompt = f.Prompt();
            Assert.Equal("pending-activation", prompt.Continuation);
            Assert.Equal("S01-0204", prompt.SourceCardId);
            var activation = Assert.Single(f.Game.State.PendingActivations);
            Assert.All(activation.SelectionSteps,
                step => Assert.Equal(L12ActivationCancellationPolicy.NotAllowed, step.CancellationPolicy));
            foreach (var decline in new[] { "skip", "mode:none", "decline", "refuse", "cancel" })
            {
                Assert.DoesNotContain(decline, prompt.ValidChoices);
                f.Send(prompt.PlayerIndex, Fixture.Typed(prompt, decline), "reject-mandatory-" + decline, false);
            }
            var valid = Fixture.Typed(prompt, "0:" + index);
            f.Send(prompt.PlayerIndex, valid, "mandatory-slot-" + index);
            f.Send(prompt.PlayerIndex, valid, "reject-duplicate-mandatory-slot", false);
        }
        f.Drain();
        var run = f.Complete("mandatory-declaration");
        Assert.Equal(suppressDeath ? 0 : 1, run.DeathCount);
        Assert.Equal(suppressDeath ? 1 : 0, run.LeaveCount);
        AssertGuardsReturned(f);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [Trait("L12Evidence", "repro:BUG-20260921-d1e22956-paid-mengpo-before-death")]
    public void RealMengpoSuppressionKeepsIndependentLeaveAfterPaidTianZhu(int actor, bool restore)
    {
        var f = Prepared(actor, restore, "suppressed");
        f.SuppressWithMengpo();
        f.KillWithTianZhu();
        f.Drain();
        var run = f.Complete("paid-suppressed");
        Assert.Equal(0, run.DeathCount);
        // Scoped user ruling: suppressing death must not remove the independent leave.
        // Normal death still remains one path, with the old negation fallback unchanged.
        Assert.True(run.LeaveCount == 1,
            $"SUPPRESSED_DEATH_MUST_KEEP_LEAVE expected=1 actual={run.LeaveCount}; "
            + $"guardsOnField={run.GuardsOnField}; clean={run.Clean}; finalHash={run.FinalStateSha256}");
        AssertGuardsReturned(f);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void NoMengpoControlKeepsOneDeathAndNoRedundantLeave(int actor)
    {
        var f = Prepared(actor, true, "no-suppression");
        f.KillWithTianZhu();
        f.Drain();
        var run = f.Complete("paid-normal");
        Assert.Equal(1, run.DeathCount); Assert.Equal(0, run.LeaveCount);
        AssertGuardsReturned(f);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void RealAbsoluteDefenseNegatesDeclaredDeathAndKeepsLeaveFallback(int actor)
    {
        var f = Prepared(actor, true, "negated-death");
        f.KillWithTianZhu();
        f.Drain(negateTrigger: "death");
        var run = f.Complete("paid-negated-death");
        Assert.True(f.CounterCommitted);
        Assert.Equal(1, run.DeathCount); Assert.Equal(1, run.LeaveCount);
        Assert.Contains(f.Game.State.Events, e => e.Type == "effect-result" && e.EffectResultStatus == "negated"
            && e.Cards.Any(c => c.InstanceId == Construct));
        Assert.Contains(f.Game.State.Players[actor].Graveyard, c => c.InstanceId == Discard);
        AssertGuardsReturned(f);
    }


    [Theory]
    [InlineData(0, false)] [InlineData(0, true)] [InlineData(1, false)] [InlineData(1, true)]
    public void RealCombatDepartureAfterMengpoKeepsLeave(int actor, bool restore)
    {
        var f = new Fixture(actor, restore, output, "combat");
        // Printed 6000-troop attacker is part of the explicitly synthetic starting board.
        f.Game.State.Players[actor].Field[0][0] = Card(Catalog, "S01-0001", "combat-attacker", actor);
        f.PlayConstructAndChangeTurn(); f.SuppressWithMengpo();
        f.Send(actor, new("attack", "combat-attacker", Target: new L12AttackTarget("legion", Construct)), "real-attack");
        f.Drain();
        var run = f.Complete("combat");
        Assert.Equal(0, run.DeathCount); Assert.Equal(1, run.LeaveCount); AssertGuardsReturned(f);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)] [InlineData(1, false)] [InlineData(1, true)]
    public void SyntheticSimultaneousLethalBoundaryKeepsSuppressedLeave(int actor, bool restore)
    {
        var f = Prepared(actor, restore, "simultaneous");
        f.SuppressWithMengpo();
        var host = f.Game.State.Players[f.Owner].Field[0][0]!;
        var peer = Card(Catalog, "S01-0212", "simultaneous-peer", f.Owner);
        f.Game.State.Players[f.Owner].Field[0][1] = peer;
        host.Troops = -10000; peer.Troops = -10000;
        f.Bound("synthetic-two-lethal-troops-injection");
        f.InvokeBoundary("ResolveStateBasedLegionDeaths", false);
        f.Drain(); var run = f.Complete("simultaneous");
        Assert.Equal(0, run.DeathCount); Assert.Equal(1, run.LeaveCount);
        Assert.Contains(f.Game.State.Players[f.Owner].Graveyard, c => c.InstanceId == peer.InstanceId);
        AssertGuardsReturned(f);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(0, 1)] [InlineData(1, 1)]
    [InlineData(0, 3)] [InlineData(1, 3)]
    public void RealSuppressionZeroOneThreeGuardsNeverAsksAnEmptyChoice(int actor, int count)
    {
        var f = new Fixture(actor, true, output, "guard-count-" + count);
        f.Game.State.Players[f.Owner].Graveyard.Clear(); f.GuardIds.Clear();
        for (var i = 0; i < count; i++)
        {
            var id = "count-guard-" + i; f.GuardIds.Add(id);
            f.Game.State.Players[f.Owner].Graveyard.Add(Card(Catalog, "S01-0212", id, f.Owner));
        }
        f.PlayConstructAndChangeTurn(); f.SuppressWithMengpo(); f.KillWithTianZhu(); f.Drain();
        var run = f.Complete("guard-count");
        Assert.Equal(0, run.DeathCount); Assert.Equal(count == 0 ? 0 : 1, run.LeaveCount);
        Assert.Equal(count, run.GuardsOnField); AssertGuardsReturned(f);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void RealTurnExpiryRestoresNormalDeathWithoutExtraLeave(int actor)
    {
        var f = Prepared(actor, true, "expired");
        f.SuppressWithMengpo();
        f.Send(actor, new("endTurn"), "end-suppression-turn"); f.Drain();
        f.Send(f.Owner, new("endTurn"), "return-to-attacker-turn"); f.Drain();
        Assert.True(f.Game.State.Players[f.Owner].Field[0][0]!.SuppressDeathUntilTurn < f.Game.State.TurnSerial);
        f.KillWithTianZhu(); f.Drain(); var run = f.Complete("expired");
        Assert.Equal(1, run.DeathCount); Assert.Equal(0, run.LeaveCount); AssertGuardsReturned(f);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void RealNegationOfSuppressedLeaveDoesNotCreateRecursiveFallback(int actor)
    {
        var f = Prepared(actor, true, "negated-leave");
        f.SuppressWithMengpo(); f.KillWithTianZhu(); f.Drain(negateTrigger: "leave");
        var run = f.Complete("negated-leave");
        Assert.True(f.CounterCommitted); Assert.Equal(0, run.DeathCount);
        Assert.Equal(1, run.LeaveCount); Assert.Equal(0, run.GuardsOnField);
        foreach (var id in f.GuardIds) Assert.Contains(f.Game.State.Players[f.Owner].Graveyard, c => c.InstanceId == id);
        Assert.Contains(f.Game.State.Players[actor].Graveyard, c => c.InstanceId == Discard);
    }

    [Theory]
    [InlineData(0, "source")] [InlineData(1, "source")]
    [InlineData(0, "target")] [InlineData(1, "target")]
    [InlineData(0, "slot")] [InlineData(1, "slot")]
    public void SyntheticSettlementInvalidationPreservesSnapshotAndUnaffectedGuard(int actor, string change)
    {
        var f = Prepared(actor, true, "stale-" + change);
        f.SuppressWithMengpo(); f.KillWithTianZhu(); f.Drain(stopAtConstruct: true);
        f.Choose("0:0", "first-declared-slot"); f.Choose("0:1", "second-declared-slot");
        var owner = f.Game.State.Players[f.Owner];
        Assert.Equal("leave", Assert.Single(f.Game.State.EffectStack).Trigger);
        if (change == "source")
        {
            var host = Assert.Single(owner.Graveyard, c => c.InstanceId == Construct);
            owner.Graveyard.Remove(host); owner.Library.Insert(0, host);
        }
        else if (change == "target")
        {
            var guard = Assert.Single(owner.Graveyard, c => c.InstanceId == GuardA);
            owner.Graveyard.Remove(guard); owner.Removed.Add(guard);
        }
        else owner.Field[0][0] = Card(Catalog, "S01-0212", "stale-slot-blocker", f.Owner);
        f.Bound("synthetic-settlement-" + change);
        // Synthetic insertion must refresh derived costs before checkpointing, as a real command does.
        f.InvokeBoundary("RecalculateContinuousTroops");
        f.Recover("after-invalidation");
        f.Drain(); var run = f.Complete("stale");
        Assert.Equal(0, run.DeathCount); Assert.Equal(1, run.LeaveCount);
        Assert.Equal(change == "source" ? 2 : 1, run.GuardsOnField);
        Assert.Equal(GuardB, f.Game.State.Players[f.Owner].Field[0][1]!.InstanceId);
        if (change == "source") Assert.Equal(Construct, f.Game.State.Players[f.Owner].Library[0].InstanceId);
        if (change == "target") Assert.Contains(f.Game.State.Players[f.Owner].Removed, c => c.InstanceId == GuardA);
        if (change == "slot") Assert.Contains(f.Game.State.Players[f.Owner].Graveyard, c => c.InstanceId == GuardA);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void RealTypedLeaveSelectionRejectsStaleBindingAndDuplicateWithoutMutation(int actor)
    {
        var f = Prepared(actor, true, "typed-rejection");
        f.SuppressWithMengpo(); f.KillWithTianZhu(); f.Drain(stopAtConstruct: true);
        var p = f.Prompt(); var command = Fixture.Typed(p, "0:0");
        f.Send(p.PlayerIndex, command with { SourceInstanceId = "stale-source" }, "reject-wrong-binding", false);
        f.Send(p.PlayerIndex, command, "valid-first-slot");
        f.Send(p.PlayerIndex, command, "reject-repeated-prompt", false);
        f.Drain(); var run = f.Complete("typed-rejection");
        Assert.Equal(1, run.LeaveCount); AssertGuardsReturned(f);
    }

    [Theory]
    [InlineData(0, "queue-false")] [InlineData(1, "queue-false")]
    [InlineData(0, "global-disaster")] [InlineData(1, "global-disaster")]
    public void SyntheticProducerOptOutStillSuppressesAllDeathDepartureCandidates(int actor, string optOut)
    {
        var f = Prepared(actor, true, "optout-" + optOut); f.SuppressWithMengpo();
        var host = f.Game.State.Players[f.Owner].Field[0][0]!;
        if (optOut == "queue-false")
            Assert.True((bool)f.InvokeBoundary("RemoveFromField", f.Game.State.Players[f.Owner], host, true,
                "synthetic explicit producer opt-out", false, L12FieldLeaveKind.Defeat, false, false)!);
        else
        {
            host.Troops = -10000;
            f.Game.State.Phase = L12Phase.Disaster;
            f.Bound("synthetic-global-disaster-lethal-boundary");
            f.InvokeBoundary("ResolveStateBasedLegionDeaths", false);
        }
        f.Drain(); var run = f.Complete("optout");
        Assert.Equal(0, run.DeathCount); Assert.Equal(0, run.LeaveCount); Assert.Equal(0, run.GuardsOnField);
        foreach (var id in f.GuardIds) Assert.Contains(f.Game.State.Players[f.Owner].Graveyard, c => c.InstanceId == id);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void SyntheticCrossOwnerAttachedGuardReturnsOnlyToItsActualOwner(int actor)
    {
        var f = new Fixture(actor, true, output, "cross-owner");
        // Ownership differs in synthetic initial grave; printed cards and the real paid entry are unchanged.
        foreach (var guard in f.Game.State.Players[f.Owner].Graveyard) guard.OwnerIndex = actor;
        f.PlayConstructAndChangeTurn(); f.SuppressWithMengpo(); f.KillWithTianZhu(); f.Drain();
        var run = f.Complete("cross-owner");
        Assert.Equal(0, run.DeathCount); Assert.Equal(1, run.LeaveCount); Assert.Equal(2, run.GuardsOnField);
        foreach (var id in f.GuardIds)
        {
            var entry = Assert.Single(f.AllCards(), c => c.Card.InstanceId == id);
            Assert.StartsWith($"{actor}:field:", entry.Zone); Assert.True(entry.Card.Tapped);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void SyntheticBorrowedDeathWithoutDepartureDoesNotCreateLeave(int actor)
    {
        var f = Prepared(actor, true, "borrowed-death");
        var host = f.Game.State.Players[f.Owner].Field[0][0]!;
        // Scheduler boundary, NOT a claim that Canute can select a Duat construct.
        var candidate = (L12TriggerCandidate)f.InvokeBoundary("CreateDeathTriggerCandidate", f.Owner, host,
            new Dictionary<string, string> { ["cause"] = "synthetic-borrowed-death" })!;
        f.InvokeBoundary("QueueTriggerCandidates", (object)new[] { candidate });
        f.Drain(); var run = f.Complete("borrowed-death");
        Assert.Equal(1, run.DeathCount); Assert.Equal(0, run.LeaveCount); Assert.Equal(0, run.GuardsOnField);
        Assert.Equal(Construct, f.Game.State.Players[f.Owner].Field[0][0]!.InstanceId);
        Assert.Equal(2, f.Game.State.Players[f.Owner].Field[0][0]!.AttachedCards.Count);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void RealSuppressionOnDeathOnlyPaladinDoesNotInventAnOwnLeave(int actor)
    {
        var f = new Fixture(actor, true, output, "death-only-paladin");
        f.Game.State.Players[f.Owner].Field[0][1] = Card(Catalog, "S02-0202", "paladin", f.Owner);
        f.PlayConstructAndChangeTurn(); f.SuppressWithMengpo("paladin"); f.KillWithTianZhu("paladin"); f.Drain();
        var run = f.Complete("death-only");
        Assert.Equal(0, run.LeaveCount);
        Assert.Contains(f.Game.State.Players[f.Owner].Graveyard, c => c.InstanceId == "paladin");
        Assert.Equal(Construct, f.Game.State.Players[f.Owner].Field[0][0]!.InstanceId);
        Assert.Equal(0, f.ObservedTriggerCount("paladin", "death"));
        Assert.Equal(0, f.ObservedTriggerCount("paladin", "leave"));
    }

    private Fixture Prepared(int actor, bool restore, string scenario)
    {
        var f = new Fixture(actor, restore, output, scenario);
        f.PlayConstructAndChangeTurn();
        return f;
    }

    private static void AssertGuardsReturned(Fixture f)
    {
        foreach (var id in f.GuardIds)
        {
            var card = Assert.Single(f.AllCards(), x => x.Card.InstanceId == id);
            Assert.StartsWith($"{f.Owner}:field:", card.Zone);
            Assert.DoesNotContain("/attached", card.Zone);
            Assert.True(card.Card.Tapped);
            Assert.DoesNotContain(f.Game.State.Players[f.Owner].Graveyard, c => c.InstanceId == id);
        }
        Assert.Contains(f.Game.State.Players[f.Owner].Graveyard, c => c.InstanceId == Construct);
    }

    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);
    private static JsonElement Freeze(object value) => JsonSerializer.SerializeToElement(value, JsonOptions);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static L12CardInstance Card(L12Catalog catalog, string id, string instance, int owner)
    {
        var d = catalog.Cards[id];
        return new()
        {
            InstanceId = instance, CardId = id, OwnerIndex = owner, Name = d.NameZh, CardType = d.CardType,
            IsCounterTactic = d.IsCounterTactic, Faction = d.Faction, ImageUrl = d.ImageUrl,
            Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue, EffectText = d.Effect, Traits = [.. d.Traits],
            Profession = d.Profession, BaseTroops = d.Troops ?? 0, Troops = d.Troops ?? 0,
            DisasterLevel = d.DisasterLevel ?? 0, TrialValue = d.TrialValue ?? 0, SummonRound = 0,
        };
    }

    private sealed record RunReceipt(int Actor, bool Restored, int Commands, int StrictRestores,
        int DeathCount, int LeaveCount, int GuardsOnField, bool Clean, string FinalStateSha256,
        string FactsSha256, string EventsSha256, string CommandsSha256);

    private sealed class Fixture
    {
        private readonly L12Catalog catalog = Catalog;
        private readonly ITestOutputHelper output;
        private readonly string scenario;
        private readonly bool restore;
        private long factCursor, eventCursor;
        private readonly List<JsonElement> facts = [], events = [], commands = [], checkpoints = [];
        private readonly Dictionary<string, JsonElement> observedStacks = [];
        private readonly List<object> boundaries = [];
        private int restores;
        public int Actor { get; }
        public int Owner => 1 - Actor;
        public L12GameEngine Game { get; private set; }
        public List<string> GuardIds { get; } = [GuardA, GuardB];
        public bool CounterCommitted { get; private set; }

        public Fixture(int actor, bool restore, ITestOutputHelper output, string scenario)
        {
            Actor = actor; this.restore = restore; this.output = output; this.scenario = scenario;
            var baseline = catalog.DeckAt(0);
            var deck = new L12PresetDeckDefinition
            {
                Name = "Synthetic Mengpo", MasterId = "S01-01M2", CardIds = [..baseline.CardIds],
                MoraleIds = [..baseline.MoraleIds], SpecialIds = [..baseline.SpecialIds],
            };
            Game = new L12GameEngine(catalog, "synthetic-mengpo-tomb-construct", "LOCAL", 622056,
                ["Synthetic A", "Synthetic B"], new[] { deck, deck }, skipPreparation: true,
                disasterMode: "none", autoPassEmptyResponses: false,
                concealHiddenResponseAvailability: false, stateFormatVersion: 2, utcNow: () => Now);
            Game.State.PendingPrompts.Clear();
            Game.State.ActivePlayer = Owner; Game.State.FirstPlayer = 0; Game.State.Round = 2;
            Game.State.TurnSerial = 3; Game.State.Phase = L12Phase.Main;
            Game.State.ActiveDisaster = null; Game.State.DisasterDeck.Clear();
            foreach (var p in Game.State.Players)
            {
                p.Field[0] = new L12CardInstance?[3]; p.Field[1] = new L12CardInstance?[3];
                p.Hand.Clear(); p.Library.Clear(); p.Graveyard.Clear(); p.Removed.Clear(); p.Resolving.Clear();
                p.Relic = null; p.ExtraRelics.Clear(); p.SpecialZones.CanopicProgress.Clear();
                p.SpecialZones.GodPower.Clear(); p.SpecialZones.Trials.Clear();
                p.Morale.Clear(); p.MoraleDeck.Clear(); p.UsedAbilities.Clear();
                p.TemporaryMorale = 0; p.FreeTacticCount = 0;
                for (var i = 0; i < 12; i++)
                    p.Library.Add(Card(catalog, "S02-0007", $"library-{p.PlayerIndex}-{i}", p.PlayerIndex));
                for (var i = 0; i < 8; i++)
                {
                    p.Morale.Add(new() { InstanceId = $"morale-{p.PlayerIndex}-{i}", CardId = "S01-01C1" });
                    p.MoraleDeck.Add(new() { InstanceId = $"morale-deck-{p.PlayerIndex}-{i}", CardId = "S01-01C1" });
                }
            }
            var owner = Game.State.Players[Owner]; var mage = Game.State.Players[Actor];
            owner.Hand.Add(Card(catalog, "S01-0204", Construct, Owner));
            owner.Graveyard.Add(Card(catalog, "S01-0212", GuardA, Owner));
            owner.Graveyard.Add(Card(catalog, "S01-0212", GuardB, Owner));
            mage.Hand.Add(Card(catalog, "S01-0418", Kill, Actor));
            mage.Hand.Add(Card(catalog, "S02-0007", Discard, Actor));
            var counter = Card(catalog, "S01-0016", Counter, Actor);
            counter.Hidden = true; counter.SetRound = 0; mage.Field[1][0] = counter;
            factCursor = Game.CardFactSignalSequence; eventCursor = Game.State.EventSequence;
            Bound("synthetic-initial-state");
        }

        public IEnumerable<(string Zone, L12CardInstance Card)> AllCards()
        {
            static IEnumerable<(string, L12CardInstance)> Descendants(string zone, L12CardInstance card)
            {
                yield return (zone, card);
                foreach (var child in card.AttachedCards)
                foreach (var nested in Descendants(zone + "/attached", child)) yield return nested;
            }
            foreach (var p in Game.State.Players)
            {
                foreach (var (zone, list) in new[] { ("hand", p.Hand), ("library", p.Library),
                    ("graveyard", p.Graveyard), ("removed", p.Removed), ("resolving", p.Resolving) })
                    foreach (var card in list)
                    foreach (var entry in Descendants($"{p.PlayerIndex}:{zone}", card)) yield return entry;
                for (var row = 0; row < 2; row++)
                for (var slot = 0; slot < 3; slot++)
                    if (p.Field[row][slot] is { } card)
                        foreach (var entry in Descendants($"{p.PlayerIndex}:field:{row}:{slot}", card)) yield return entry;
            }
        }

        private void Unique()
        {
            foreach (var id in new[] { Construct, Kill, Counter, Discard }.Concat(GuardIds))
                Assert.Single(AllCards(), c => c.Card.InstanceId == id);
        }

        private void Collect()
        {
            foreach (var e in Game.State.Events.Where(e => e.Sequence > eventCursor).OrderBy(e => e.Sequence))
            { events.Add(Freeze(e)); eventCursor = e.Sequence; }
            foreach (var f in Game.CardFactSignals.Where(f => f.Sequence > factCursor).OrderBy(f => f.Sequence))
            { facts.Add(Freeze(f)); factCursor = f.Sequence; }
            foreach (var item in Game.State.EffectStack.Concat(Game.State.DeferredEffectStack))
                observedStacks.TryAdd(item.StackItemId, Freeze(item));
        }

        public object? InvokeBoundary(string method, params object?[] args)
        {
            Bound("synthetic-call-before-" + method);
            var result = typeof(L12GameEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Game, args);
            Collect(); Unique(); if (restore) Recover("synthetic-call-after-" + method);
            return result;
        }

        public void Bound(string label) => boundaries.Add(new
        {
            label, state = Game.SerializeFullState(), randomState = Game.RandomState,
            cardFactSignalSequence = Game.CardFactSignalSequence,
        });

        public void Recover(string label, bool replace = true)
        {
            Collect(); Unique();
            var before = Game.SerializeFullState(); var random = Game.RandomState!.Value;
            var sequence = Game.CardFactSignalSequence;
            var recovered = L12GameEngine.RestoreCheckpoint(catalog, before, random, sequence,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
            var after = recovered.SerializeFullState();
            checkpoints.Add(Freeze(new { label, replace, beforeHash = Hash(before), afterHash = Hash(after),
                randomBefore = random, randomAfter = recovered.RandomState,
                sequenceBefore = sequence, sequenceAfter = recovered.CardFactSignalSequence }));
            Assert.Equal(before, after); Assert.Equal(random, recovered.RandomState);
            Assert.Equal(sequence, recovered.CardFactSignalSequence); restores++;
            if (replace) Game = recovered;
        }

        public L12Prompt Prompt() => Assert.Single(Game.State.PendingPrompts);
        public static L12Command Typed(L12Prompt p, string? choice = null, List<string>? ids = null) => new("resolvePrompt",
            PromptId: p.PromptId, Choice: choice, CardInstanceIds: ids, ActivationId: p.ActivationId,
            SourceInstanceId: p.SourceInstanceId, SourceCardId: p.SourceCardId, Step: p.Step,
            CreatedRevision: p.CreatedRevision, Controller: p.Controller);

        public void Send(int player, L12Command command, string label, bool expected = true)
        {
            var before = Game.SerializeFullState(); var prompts = Freeze(Game.State.PendingPrompts);
            var result = Game.Handle(player, command); Collect(); Unique();
            var after = Game.SerializeFullState();
            commands.Add(Freeze(new { label, player, command, expected, result.Accepted, result.Error,
                beforeHash = Hash(before), afterHash = Hash(after), promptsBefore = prompts,
                promptsAfter = Game.State.PendingPrompts, stack = Game.State.EffectStack,
                batches = Game.State.PendingTriggerBatches, pending = Game.State.PendingTriggerStackCandidates,
                stateAfter = after, randomState = Game.RandomState, cardFactSignalSequence = Game.CardFactSignalSequence }));
            Assert.True(result.Accepted == expected, $"{label}: accepted={result.Accepted} error={result.Error}");
            if (!expected) Assert.Equal(before, after);
            if (restore) Recover(label);
        }

        public void Choose(string choice, string label)
        {
            var p = Prompt(); Assert.Contains(choice, p.ValidChoices);
            Send(p.PlayerIndex, Typed(p, choice), label);
        }

        public void Drain(string? negateTrigger = null, bool stopAtConstruct = false)
        {
            for (var i = 0; i < 100; i++)
            {
                if (Game.State.PendingPrompts.Count == 0)
                {
                    if (Game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice } defense)
                    {
                        Send(1 - defense.AttackerPlayer, new("resolveDefense", CardInstanceIds: []), "real-decline-defenders");
                        continue;
                    }
                    return;
                }
                var p = Prompt();
                if (p.Kind == "response")
                {
                    if (negateTrigger is not null && !CounterCommitted && p.PlayerIndex == Actor
                        && p.ValidChoices.Contains(Counter)
                        && Game.State.EffectStack.Any(s => s.SourceInstanceId == Construct && s.Trigger == negateTrigger))
                    { Choose(Counter, "real-absolute-" + negateTrigger); continue; }
                    Choose("pass", "response-pass"); continue;
                }
                if (p.Continuation == "stack-response-discard")
                {
                    Choose(Discard, "pay-absolute-discard"); CounterCommitted = true;
                    Assert.Contains(Game.State.Players[Actor].Graveyard, c => c.InstanceId == Discard);
                    continue;
                }
                if (p.Continuation == "pending-activation" && p.SourceCardId == "S01-0204")
                {
                    if (stopAtConstruct) return;
                    var slot = new[] { "0:0", "0:1", "0:2", "1:0", "1:1", "1:2" }.First(s => p.ValidChoices.Contains(s));
                    Choose(slot, "declare-guard-slot"); continue;
                }
                throw new InvalidOperationException("Unexpected prompt: " + Json(p));
            }
            throw new InvalidOperationException("Drain exceeded bounded 100 commands");
        }

        public void PlayConstructAndChangeTurn()
        {
            var before = Game.State.Players[Owner].Morale.Count(m => !m.Tapped);
            Send(Owner, new("playCard", Construct, Row: 0, Slot: 0), "real-paid-construct-play");
            Drain();
            Assert.Equal(6, before - Game.State.Players[Owner].Morale.Count(m => !m.Tapped));
            var host = Game.State.Players[Owner].Field[0][0]!;
            Assert.Equal(Construct, host.InstanceId);
            Assert.Equal(GuardIds.Order(), host.AttachedCards.Select(c => c.InstanceId).Order());
            Assert.DoesNotContain(Game.State.Players[Owner].Graveyard, c => GuardIds.Contains(c.InstanceId));
            Bound("real-entry-complete");
            Send(Owner, new("endTurn"), "real-end-turn"); Drain();
            Assert.Equal(Actor, Game.State.ActivePlayer); Assert.Equal(L12Phase.Main, Game.State.Phase);
        }

        public void SuppressWithMengpo(string target = Construct)
        {
            var before = Game.State.Players[Actor].Morale.Count;
            Send(Actor, new("activateAbility", $"master-{Actor}", Ability: "mengpoSilence"), "real-mengpo");
            Choose(target, "declare-mengpo-target");
            for (var i = 0; i < 8 && Game.State.PendingPrompts.Count > 0 && Prompt().Kind != "response"; i++)
            {
                var p = Prompt();
                var payment = Game.State.Players[Actor].Morale.First(m => p.ValidChoices.Contains(m.InstanceId));
                Choose(payment.InstanceId, "return-real-morale");
            }
            Assert.Equal(1, before - Game.State.Players[Actor].Morale.Count);
            Assert.True(AllCards().Single(c => c.Card.InstanceId == target).Card.SuppressDeathUntilTurn < Game.State.TurnSerial);
            Drain();
            Assert.Equal(Game.State.TurnSerial, AllCards().Single(c => c.Card.InstanceId == target).Card.SuppressDeathUntilTurn);
            Bound("real-suppression-complete");
        }

        public void KillWithTianZhu(string target = Construct)
        {
            var before = Game.State.Players[Actor].Morale.Count(m => !m.Tapped);
            Send(Actor, new("playCard", Kill), "real-paid-tian-zhu");
            Assert.Equal("pending-activation", Prompt().Continuation);
            Choose(target, "declare-tian-zhu-target");
            Assert.Equal(4, before - Game.State.Players[Actor].Morale.Count(m => !m.Tapped));
        }

        public bool Clean => Game.State.PendingPrompts.Count == 0 && Game.State.PendingActivations.Count == 0
            && Game.State.EffectStack.Count == 0 && Game.State.DeferredEffectStack.Count == 0
            && Game.State.PendingTriggerBatches.Count == 0 && Game.State.PendingTriggerStackCandidates.Count == 0
            && Game.State.ResponseWindow is null && !Game.State.IsResolvingStack && Game.State.PendingDefense is null
            && Game.State.Players.All(p => p.Resolving.Count == 0);

        public int ObservedTriggerCount(string instance, string trigger)
            => observedStacks.Values.Count(s => s.GetProperty("sourceInstanceId").GetString() == instance
                && s.GetProperty("trigger").GetString() == trigger);

        public RunReceipt Complete(string label)
        {
            Collect(); Unique(); Recover("final", false);
            var items = observedStacks.Values.Where(s => s.GetProperty("sourceInstanceId").GetString() == Construct).ToArray();
            var final = Game.SerializeFullState();
            var result = new RunReceipt(Actor, restore, commands.Count, restores,
                items.Count(s => s.GetProperty("trigger").GetString() == "death"),
                items.Count(s => s.GetProperty("trigger").GetString() == "leave"),
                AllCards().Count(c => GuardIds.Contains(c.Card.InstanceId) && c.Zone.Contains(":field:") && !c.Zone.Contains("/attached")),
                Clean, Hash(final), Hash(Json(facts)), Hash(Json(events)),
                Hash(Json(commands.Select(c => new { player = c.GetProperty("player"), command = c.GetProperty("command") }))));
            output.WriteLine(Json(result));
            var evidence = Environment.GetEnvironmentVariable("L12_MENGPO_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                Assert.True(Path.IsPathFullyQualified(evidence)); Directory.CreateDirectory(evidence);
                var path = Path.Combine(evidence, $"{label}-{scenario}-p{Actor}-restore-{restore}.json");
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                JsonSerializer.Serialize(stream, new { result, facts, events, commands, checkpoints, boundaries,
                    observedStacks, finalState = final, randomState = Game.RandomState,
                    cardFactSignalSequence = Game.CardFactSignalSequence }, JsonOptions);
                output.WriteLine("Evidence: " + Path.GetFileName(path));
            }
            Assert.True(result.Clean, "Final chain must be fully empty");
            return result;
        }
    }
}
