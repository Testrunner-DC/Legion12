using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// All activations and settlements use public commands;
// marker inspection is not a replacement for the resulting progress/zone assertions.
public sealed class AngusTacticActivationEntryBoundaryTests
{
    private const string Angus = "S02-06M2", Marker = "compositeTacticActivationSource";
    private const string Source = "boundary-source", Target = "boundary-target", Trial = "boundary-trial";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-16T12:00:00Z");

    public static IEnumerable<object[]> Seats()
    {
        foreach (var owner in new[] { 0, 1 })
        foreach (var restore in new[] { false, true }) yield return [owner, restore];
    }
    public static IEnumerable<object[]> Counters()
    {
        foreach (var card in new[] { "S01-0019", "S01-0016", "S02-0106" })
        foreach (var row in Seats()) yield return [card, row[0], row[1]];
    }

    [Theory, MemberData(nameof(Seats))]
    public void RealSetTrojanFirstActivationCountsButItsLaterExpiryDoesNot(int owner, bool restore)
    {
        var f = new Fixture(owner, restore);
        f.Game.State.Players[owner].Hand.Add(Card("S02-0523", Source, owner));
        f.Game.State.Players[1 - owner].Field[0][0] = Card("S02-0004", "boundary-attacker", 1 - owner);
        f.Send(owner, new("playCard", Source, Row: 1, Slot: 0));
        Assert.True(f.Game.State.Players[owner].Field[1][0]!.Hidden);
        Assert.Equal(2, f.Game.State.Players[owner].Morale.Count(card => card.Tapped));
        Assert.DoesNotContain(f.OnceKey, f.Game.State.Players[owner].UsedAbilities);
        f.Send(owner, new("endTurn"));
        Assert.Equal(1 - owner, f.Game.State.ActivePlayer);
        f.Send(1 - owner, new("attack", "boundary-attacker", Target: new("master")));
        f.PassResponses();
        f.Send(owner, new("resolveDefense", CardInstanceIds: []));
        f.PassResponses();
        f.Choose("mode:use");
        var slotCommand = f.Choose("1:1");
        f.AssertObserved(Source, "trojan-after-attack");
        f.RejectUnchanged(owner, slotCommand);
        f.Drain();
        Assert.Equal(1, f.Progress); Assert.Equal(1, f.AngusTriggers);
        Assert.Equal(Source, f.Game.State.Players[1 - owner].Field[1][1]?.InstanceId);
        Assert.False(f.Game.State.Players[1 - owner].Field[1][1]!.Hidden);
        f.AssertClean();
        f.Send(1 - owner, new("endTurn")); f.Drain();
        Assert.Equal(owner, f.Game.State.ActivePlayer);
        Assert.Equal(Source, f.Game.State.Players[1 - owner].Field[1][1]?.InstanceId);
        var expiryTurnKey = f.OnceKey;
        Assert.DoesNotContain(expiryTurnKey, f.Game.State.Players[owner].UsedAbilities);
        var libraryBefore = f.Game.State.Players[owner].Library.Count;
        f.Send(owner, new("endTurn")); f.Drain();
        Assert.Null(f.Game.State.Players[1 - owner].Field[1][1]);
        Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == Source);
        Assert.Equal(libraryBefore - 1, f.Game.State.Players[owner].Library.Count);
        Assert.Equal(1, f.Progress); Assert.Equal(1, f.AngusTriggers);
        Assert.DoesNotContain(expiryTurnKey, f.Game.State.Players[owner].UsedAbilities);
        Assert.Equal(2, f.Game.State.Events.Count(entry => entry.Type == "effect-result"
            && entry.Cards.FirstOrDefault()?.InstanceId == Source && entry.EffectSegmentCount == 2));
        f.AssertClean();
    }

    [Theory, MemberData(nameof(Seats))]
    public void RealLiMuMayBeginAtSegmentOneWithoutBeingMistakenForATactic(int owner, bool restore)
    {
        // Current tactic plans have mandatory first segments. This is the actual
        // existing >0 initial-segment case, and it is correctly a legion negative.
        var f = new Fixture(owner, restore);
        f.Game.State.Players[owner].Hand.Add(Card("S02-0102", Source, owner));
        f.Send(owner, new("playCard", Source, Row: 1, Slot: 0));
        f.Choose("mode:none"); f.Choose("mode:use");
        var item = Assert.Single(f.Game.State.EffectStack, item => item.SourceInstanceId == Source);
        Assert.Equal("1", item.Data["compositeSegment"]);
        Assert.Equal("limu-draw", item.Data["atomicFlow"]);
        Assert.False(item.Data.ContainsKey(Marker));
        Assert.DoesNotContain(f.OnceKey, f.Game.State.Players[owner].UsedAbilities);
        var before = f.Game.State.Players[owner].Hand.Count;
        f.Drain();
        Assert.Equal(before + 1, f.Game.State.Players[owner].Hand.Count);
        Assert.Equal(Catalog.Cards["S02-0102"].Cost!.Value,
            f.Game.State.Players[owner].Morale.Count(card => card.Tapped));
        Assert.Equal(0, f.Progress); Assert.Equal(0, f.AngusTriggers);
        f.AssertClean();
    }

    [Theory, MemberData(nameof(Seats))]
    public void RealFreeChildTacticOwnsItsIdentityAndBothSegmentsSettleOnlyOneAngus(int owner, bool restore)
    {
        const string parentId = "boundary-limu-parent";
        var f = new Fixture(owner, restore);
        var actor = f.Game.State.Players[owner];
        actor.Hand.Add(Card("S02-0102", parentId, owner));
        actor.Library.Insert(0, Card("S01-0015", Source, owner));
        f.Send(owner, new("playCard", parentId, Row: 1, Slot: 0));
        f.Choose("mode:use"); f.Choose("mode:none"); f.PassResponses();
        Assert.Equal("s2-limu-tactic", Assert.Single(f.Game.State.PendingPrompts).Data.GetValueOrDefault("action"));
        Assert.DoesNotContain(f.OnceKey, f.Game.State.Players[owner].UsedAbilities);
        var play = f.Choose("play");
        f.AssertObserved(Source, "play");
        var child = Assert.Single(f.Game.State.EffectStack.Concat(f.Game.State.DeferredEffectStack),
            item => item.SourceInstanceId == Source);
        Assert.Equal("free", child.Data.GetValueOrDefault("effectGeneratedPlay"));
        Assert.Equal($"{owner}:{Source}", child.Data[Marker]);
        Assert.NotEqual($"{owner}:{parentId}", child.Data[Marker]);
        f.RejectUnchanged(owner, play);
        var handBefore = f.Game.State.Players[owner].Hand.Count;
        f.Drain();
        Assert.Equal(handBefore + 1, f.Game.State.Players[owner].Hand.Count);
        Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == Source);
        Assert.Equal(parentId, f.Game.State.Players[owner].Field[1][0]?.InstanceId);
        Assert.Equal(Catalog.Cards["S02-0102"].Cost!.Value,
            f.Game.State.Players[owner].Morale.Count(card => card.Tapped));
        Assert.Contains("0", f.ChildSegments); Assert.Contains("1", f.ChildSegments);
        Assert.Equal(1, f.Progress); Assert.Equal(1, f.AngusTriggers);
        Assert.Contains(f.OnceKey, f.Game.State.Players[owner].UsedAbilities);
        f.AssertClean();
    }

    [Theory, MemberData(nameof(Counters))]
    public void RealSetS1S2AndNegateResponsesRegisterBeforeTheirOwnResolution(string cardId, int owner, bool restore)
    {
        var f = new Fixture(owner, restore);
        f.Game.State.Players[owner].Hand.Add(Card(cardId, Source, owner));
        f.Game.State.Players[owner].Hand.Add(Card("S01-0003", "boundary-discard", owner));
        f.Game.State.Players[owner].Field[0][0] = Card("S01-0002", Target, owner, 10000);
        f.Game.State.Players[owner].Library.Insert(0, Card("S01-0103", "boundary-yin-top", owner));
        f.Game.State.Players[1 - owner].Hand.Add(Card("S01-0008", "boundary-opponent-tactic", 1 - owner));
        f.Send(owner, new("playCard", Source, Row: 1, Slot: 0));
        Assert.Equal(2, f.Game.State.Players[owner].Morale.Count(card => card.Tapped));
        Assert.DoesNotContain(f.OnceKey, f.Game.State.Players[owner].UsedAbilities);
        f.Send(owner, new("endTurn"));
        f.Send(1 - owner, new("playCard", "boundary-opponent-tactic"));
        if (Assert.Single(f.Game.State.PendingPrompts).PlayerIndex != owner) f.Choose("pass");
        var response = Assert.Single(f.Game.State.PendingPrompts);
        Assert.Equal(owner, response.PlayerIndex); Assert.Contains(Source, response.ValidChoices);
        f.Choose(Source);
        if (cardId == "S01-0019") f.Choose(Target);
        if (cardId == "S01-0016") f.Choose("boundary-discard");
        f.AssertObserved(Source, cardId == "S01-0016" ? "response-negate"
            : cardId == "S01-0019" ? "reaction" : "s2-reaction");
        f.Drain();
        Assert.Equal(1, f.Progress); Assert.Equal(1, f.AngusTriggers);
        Assert.Contains(f.OnceKey, f.Game.State.Players[owner].UsedAbilities);
        Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == Source);
        if (cardId == "S01-0016")
        {
            Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == "boundary-discard");
            Assert.Equal("negated", Assert.Single(f.Game.State.Events, entry => entry.Type == "effect-result"
                && entry.Cards.FirstOrDefault()?.InstanceId == "boundary-opponent-tactic").EffectResultStatus);
        }
        else
        {
            Assert.DoesNotContain(f.Game.State.Events, entry => entry.Type == "effect-result"
                && entry.Cards.FirstOrDefault()?.InstanceId == "boundary-opponent-tactic" && entry.EffectResultStatus == "negated");
            if (cardId == "S01-0019") Assert.Equal(11000, f.Game.State.Players[owner].Field[0][0]!.Troops);
            else Assert.Equal("boundary-yin-top", f.Game.State.Players[owner].Library.Last().InstanceId);
        }
        Assert.Equal(2, f.Game.State.Players[1 - owner].Morale.Count(card => card.Tapped));
        f.AssertClean();
    }

    private sealed class Fixture
    {
        internal L12GameEngine Game;
        internal readonly HashSet<string> ChildSegments = [];
        private readonly int _owner;
        private readonly bool _restore;
        internal string OnceKey => $"trigger:angus-tactic:{Game.State.TurnSerial}";
        internal int Progress => Assert.Single(Game.State.Players[_owner].SpecialZones.Trials).TrialProgress;
        internal int AngusTriggers => Game.State.Events.Count(entry => entry.Type == "effect-trigger"
            && entry.Cards.FirstOrDefault()?.CardId == Angus);

        internal Fixture(int owner, bool restore)
        {
            _owner = owner; _restore = restore;
            var basis = Catalog.DeckAt(0); var decks = new[] { basis, basis };
            decks[owner] = new L12PresetDeckDefinition
            {
                Name = "Synthetic Angus entry boundary", MasterId = Angus,
                CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = ["S02-06S6"],
            };
            Game = new(Catalog, "synthetic-angus-boundary", "SYNTHETIC", 161624 + owner,
                ["Synthetic A", "Synthetic B"], decks, skipPreparation: true, disasterMode: "none",
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
                stateFormatVersion: 2, utcNow: () => Now);
            // The setter is second player: its real endTurn enters the next round,
            // so a newly covered counter has passed the actual setting restriction.
            Game.State.ActivePlayer = owner; Game.State.FirstPlayer = 1 - owner;
            Game.State.Round = 3; Game.State.TurnSerial = 10; Game.State.Phase = L12Phase.Main;
            foreach (var p in Game.State.Players)
            {
                foreach (var row in p.Field) Array.Clear(row);
                p.Hand.Clear(); p.Library.Clear(); p.Graveyard.Clear(); p.Resolving.Clear(); p.Removed.Clear();
                p.Morale.Clear(); p.MoraleDeck.Clear(); p.UsedAbilities.Clear();
                p.Relic = null; p.ExtraRelics.Clear(); p.TemporaryMorale = 0;
                p.SpecialZones.Trials.Clear(); p.SpecialZones.Runes = 0; p.Hp = 8;
                for (var i = 0; i < 12; i++) p.Library.Add(Card("S01-0003", $"boundary-library-{p.PlayerIndex}-{i}", p.PlayerIndex));
                for (var i = 0; i < 8; i++) p.Morale.Add(new()
                { CardId = "S02-05C1A", InstanceId = $"boundary-resource-{p.PlayerIndex}-{i}", Tapped = false });
            }
            Game.State.Players[owner].SpecialZones.Trials.Add(Card("S02-06S6", Trial, owner));
            Game.State.PendingPrompts.Clear(); Game.State.PendingActivations.Clear();
            Game.State.EffectStack.Clear(); Game.State.DeferredEffectStack.Clear();
            Game.State.PendingTriggerBatches.Clear(); Game.State.PendingTriggerStackCandidates.Clear();
            Game.State.Events.Clear(); Game.State.AuthorityEvents.Clear(); Game.State.Log.Clear();
        }

        private void Restore()
        {
            if (!_restore) return;
            var raw = Game.SerializeFullState(); var hash = Game.ComputeStateHash();
            var random = Game.RandomState!.Value; var fact = Game.CardFactSignalSequence;
            Game = L12GameEngine.RestoreCheckpoint(Catalog, raw, random, fact,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
            Assert.Equal(raw, Game.SerializeFullState()); Assert.Equal(hash, Game.ComputeStateHash());
            Assert.Equal(JsonSerializer.Serialize(random), JsonSerializer.Serialize(Game.RandomState!.Value));
            Assert.Equal(fact, Game.CardFactSignalSequence);
        }
        internal void Send(int actor, L12Command command)
        {
            Restore(); var result = Game.Handle(actor, command);
            Assert.True(result.Accepted, $"{command.Type}: {result.Error}"); Restore();
            foreach (var item in Game.State.EffectStack.Concat(Game.State.DeferredEffectStack)
                         .Where(item => item.SourceInstanceId == Source && item.SourceCardId == "S01-0015"
                             && item.Trigger == "play" && item.Data.GetValueOrDefault("compositePlan") == "S01-0015"
                             && item.Data.GetValueOrDefault("atomicFlow") is "peace-draw" or "peace-negotiation"))
            {
                Assert.True(item.Data.GetValueOrDefault(Marker) == $"{_owner}:{Source}",
                    $"Missing activation receipt: trigger={item.Trigger}; flow={item.Data.GetValueOrDefault("atomicFlow")}; plan={item.Data.GetValueOrDefault("compositePlan")}; segment={item.Data.GetValueOrDefault("compositeSegment")}");
                ChildSegments.Add(item.Data["compositeSegment"]);
            }
        }
        internal L12Command Choose(string choice)
        {
            var p = Assert.Single(Game.State.PendingPrompts); Assert.Contains(choice, p.ValidChoices);
            var command = new L12Command("resolvePrompt", PromptId: p.PromptId, CardInstanceIds: [choice],
                ActivationId: p.ActivationId, SourceInstanceId: p.SourceInstanceId, SourceCardId: p.SourceCardId,
                Step: p.Step, CreatedRevision: p.CreatedRevision, Controller: p.Controller);
            Send(p.PlayerIndex, command); return command;
        }
        internal void AssertObserved(string sourceId, string trigger)
        {
            var item = Assert.Single(Game.State.EffectStack.Concat(Game.State.DeferredEffectStack), item => item.SourceInstanceId == sourceId);
            Assert.Equal(trigger, item.Trigger);
            Assert.Equal($"{_owner}:{sourceId}", item.Data.GetValueOrDefault(Marker));
            Assert.Contains(OnceKey, Game.State.Players[_owner].UsedAbilities); Assert.Equal(0, Progress);
        }
        internal void RejectUnchanged(int actor, L12Command command)
        {
            var raw = Game.SerializeFullState(); var hash = Game.ComputeStateHash();
            Assert.False(Game.Handle(actor, command).Accepted);
            Assert.Equal(raw, Game.SerializeFullState()); Assert.Equal(hash, Game.ComputeStateHash()); Restore();
        }
        internal void PassResponses()
        {
            for (var guard = 0; Game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; guard++)
            { Assert.True(guard < 40); Choose("pass"); }
        }
        internal void Drain()
        {
            for (var guard = 0; Game.State.PendingPrompts.Count > 0; guard++)
            {
                Assert.True(guard < 80, "Boundary command chain did not quiesce");
                var p = Assert.Single(Game.State.PendingPrompts);
                if (p.Kind == "response") Choose("pass");
                else if (p.ValidChoices.Contains(Target)) Choose(Target);
                else if (p.ValidChoices.Contains("refuse")) Choose("refuse");
                else if (p.ValidChoices.Contains("mode:none")) Choose("mode:none");
                else throw new InvalidOperationException($"Unexpected boundary prompt {p.Kind}/{p.Continuation}");
            }
        }
        internal void AssertClean()
        {
            Assert.Empty(Game.State.PendingPrompts); Assert.Empty(Game.State.PendingActivations);
            Assert.Empty(Game.State.EffectStack); Assert.Empty(Game.State.DeferredEffectStack);
            Assert.Empty(Game.State.PendingTriggerBatches); Assert.Empty(Game.State.PendingTriggerStackCandidates);
            Assert.Null(Game.State.ResponseWindow); Assert.Equal(L12Phase.Main, Game.State.Phase);
        }
    }

    private static L12CardInstance Card(string cardId, string id, int owner, int? troops = null)
    {
        var d = Catalog.Cards[cardId];
        return new()
        {
            InstanceId = id, CardId = d.Id, Name = d.NameZh, CardType = d.CardType,
            IsCounterTactic = d.IsCounterTactic, Faction = d.Faction, ImageUrl = d.ImageUrl,
            Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue, EffectText = d.Effect,
            Traits = [.. d.Traits], Profession = d.Profession, EffectiveProfession = d.Profession,
            BaseTroops = troops ?? d.Troops ?? 0, Troops = troops ?? d.Troops ?? 0,
            DisasterLevel = d.DisasterLevel ?? 0, TrialValue = d.TrialValue ?? 0,
            OwnerIndex = owner, SummonRound = -1,
        };
    }
}
