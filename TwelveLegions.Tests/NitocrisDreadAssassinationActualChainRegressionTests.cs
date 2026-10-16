using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// Synthetic public setup, not the private player match. No restriction/helper is
// injected: only real S02-0206 play applies it. GM rests only the unrelated control.
public sealed class NitocrisDreadAssassinationActualChainRegressionTests
{
    private const string Tactic = "actual-assassination";
    private const string Restricted = "actual-restricted-guard", Control = "actual-control-guard";
    private const string First = "actual-nitocris-no-target", Second = "actual-nitocris-control";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-16T12:00:00Z");
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "card:S02-0206,S01-0210,S01-0212")]
    [Trait("L12Evidence", "entry:actual-assassination-nitocris-restriction")]
    public void ActualAssassinationBlocksNitocrisReadyWithoutSuppressingAnIndependentLegalGuard(
        int owner, bool restoreEveryCommand)
    {
        var f = new Fixture(owner, restoreEveryCommand);
        var turn = f.Game.State.TurnSerial;
        var troops = f.RestrictedGuard.Troops;
        Assert.True(f.RestrictedGuard.Tapped);
        Assert.False(f.ControlGuard.Tapped);
        Assert.True(f.RestrictedGuard.CannotReadyByEffectUntilTurn < turn);
        var play = new L12Command("playCard", Tactic);
        f.Send(owner, play);
        var targetPrompt = Assert.Single(f.Game.State.PendingPrompts);
        Assert.Equal("pending-activation", targetPrompt.Continuation);
        Assert.Contains(Restricted, targetPrompt.ValidChoices);
        var paidChoice = f.Choose(Restricted);
        f.PayTemporaryResourcesIfAsked();
        Assert.True(f.Game.State.EffectStack.Count == 1,
            $"Declaration boundary: {string.Join(',', f.Game.State.PendingPrompts.Select(p => p.Kind + '/' + p.Continuation))}");
        var actual = Assert.Single(f.Game.State.EffectStack);
        Assert.Equal(Tactic, actual.SourceInstanceId);
        Assert.Equal("S02-0206", actual.SourceCardId);
        Assert.Equal("fearless-assassination", actual.Data["atomicFlow"]);
        Assert.Equal(Restricted, actual.Data["declared:buffTarget"]);
        f.AssertPaid(Cost("S02-0206"));
        f.PassResponses();
        f.AssertClean();
        f.AssertRestricted(turn);
        Assert.Equal(troops + 3000, f.RestrictedGuard.Troops);
        Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == Tactic);
        f.RejectUnchanged(owner, paidChoice);
        f.RejectUnchanged(owner, play);

        // No legal target: the unrelated guard is active; the only rested guard
        // carries the restriction from actual play. No empty declaration is legal.
        f.Send(owner, new("playCard", First, Row: 1, Slot: 0));
        f.PayTemporaryResourcesIfAsked();
        Assert.Equal(First, f.Game.State.Players[owner].Field[1][0]?.InstanceId);
        f.AssertPaid(Cost("S02-0206") + Cost("S01-0210"));
        f.AssertClean();
        f.AssertRestricted(turn);
        f.AssertNoRestrictedReady();
        f.RejectUnchanged(owner, new("resolvePrompt", PromptId: "no-nitocris-activation",
            CardInstanceIds: [Restricted], SourceInstanceId: First, SourceCardId: "S01-0210", Controller: owner));

        // Prepare only the independent control via a recorded public GM command;
        // never reset or otherwise mutate the affected guard's state/expiry.
        f.RestControl();
        f.AssertRestricted(turn);
        Assert.True(f.ControlGuard.Tapped);
        Assert.True(f.ControlGuard.CannotReadyByEffectUntilTurn < turn);
        f.Send(owner, new("playCard", Second, Row: 1, Slot: 1));
        f.PayTemporaryResourcesIfAsked();
        var declaration = Assert.Single(f.Game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Equal(Second, declaration.SourceInstanceId);
        Assert.Equal("S01-0210", declaration.SourceCardId);
        Assert.Equal(new[] { Control }, declaration.ValidChoices);
        Assert.DoesNotContain(Restricted, declaration.ValidChoices);
        var totalCost = Cost("S02-0206") + 2 * Cost("S01-0210");
        f.AssertPaid(totalCost);

        // Force the forbidden object against a real current transaction, so the
        // server rejection is stronger than merely rejecting a nonexistent ID.
        f.RejectUnchanged(owner, Typed(declaration, Restricted));
        Assert.Equal(declaration.PromptId, Assert.Single(f.Game.State.PendingPrompts).PromptId);
        f.AssertRestricted(turn);
        var validChoice = f.Choose(Control);
        f.PassResponses();
        f.AssertClean();
        Assert.False(f.ControlGuard.Tapped);
        f.AssertRestricted(turn);
        f.AssertPaid(totalCost);
        Assert.Single(f.Game.State.AuthorityEvents, entry => entry.Type == "effect-ready"
            && entry.SourceInstanceId == Second && entry.TargetInstanceId == Control && entry.Resolved);
        f.AssertNoRestrictedReady();
        f.RejectUnchanged(owner, validChoice);
        f.RejectUnchanged(owner, new("playCard", Second, Row: 1, Slot: 1));
        f.AssertPaid(totalCost);
        Assert.Equal(troops + 3000, f.RestrictedGuard.Troops);
        foreach (var id in new[] { Restricted, Control, First, Second, Tactic }) Assert.Equal(1, f.Count(id));

        // Exercise the printed lifetime rather than manually clearing its flags.
        f.Send(owner, new("endTurn"));
        f.PassResponses();
        f.AssertClean();
        Assert.DoesNotContain(f.Game.State.Players[owner].Field.SelectMany(row => row),
            card => card?.InstanceId == Restricted);
        Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == Restricted);
        Assert.Contains(f.Game.State.Players[owner].Field.SelectMany(row => row), card => card?.InstanceId == Control);
        Assert.Equal(1, f.Count(Restricted));
        if (restoreEveryCommand) Assert.True(f.Restores >= 12);
        f.RoundTripAlways();
    }

    private static int Cost(string cardId) => Catalog.Cards[cardId].Cost ?? 0;
    private static L12Command Typed(L12Prompt p, string choice) => new("resolvePrompt",
        PromptId: p.PromptId, CardInstanceIds: [choice], ActivationId: p.ActivationId,
        SourceInstanceId: p.SourceInstanceId, SourceCardId: p.SourceCardId,
        Step: p.Step, CreatedRevision: p.CreatedRevision, Controller: p.Controller);

    private static L12CardInstance Card(string cardId, string id, int owner)
    {
        var d = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = id, CardId = cardId, Name = d.NameZh, CardType = d.CardType,
            IsCounterTactic = d.IsCounterTactic, Faction = d.Faction, ImageUrl = d.ImageUrl,
            Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue, EffectText = d.Effect,
            Traits = [.. d.Traits], Profession = d.Profession, EffectiveProfession = d.Profession,
            BaseTroops = d.Troops ?? 0, Troops = d.Troops ?? 0, OwnerIndex = owner, SummonRound = -1,
        };
    }

    private sealed class Fixture
    {
        internal L12GameEngine Game;
        internal int Restores;
        private readonly int _owner;
        private readonly bool _restore;
        internal L12CardInstance RestrictedGuard => Game.State.Players[_owner].Field[0][0]!;
        internal L12CardInstance ControlGuard => Game.State.Players[_owner].Field[0][1]!;

        internal Fixture(int owner, bool restore)
        {
            _owner = owner; _restore = restore;
            var deck = Catalog.DeckAt(0);
            Game = new(Catalog, "nitocris-actual-assassination", "SYNTHETIC", 161700 + owner,
                ["Synthetic A", "Synthetic B"], [deck, deck], skipPreparation: true,
                disasterMode: "none", autoPassEmptyResponses: false,
                concealHiddenResponseAvailability: false, stateFormatVersion: 2, utcNow: () => Now);
            Game.State.ActivePlayer = owner; Game.State.FirstPlayer = owner;
            Game.State.Round = 3; Game.State.TurnSerial = 10; Game.State.Phase = L12Phase.Main;
            foreach (var p in Game.State.Players)
            {
                foreach (var row in p.Field) Array.Clear(row);
                p.Hand.Clear(); p.Library.Clear(); p.Graveyard.Clear(); p.Resolving.Clear();
                p.Removed.Clear(); p.Morale.Clear(); p.MoraleDeck.Clear();
                p.Relic = null; p.ExtraRelics.Clear(); p.UsedAbilities.Clear(); p.TemporaryMorale = 0;
                for (var i = 0; i < 12; i++)
                    p.Library.Add(Card("S01-0003", $"synthetic-library-{p.PlayerIndex}-{i}", p.PlayerIndex));
            }
            var actor = Game.State.Players[owner];
            actor.TemporaryMorale = 20;
            actor.Field[0][0] = Card("S01-0212", Restricted, owner);
            actor.Field[0][0]!.Tapped = true;
            actor.Field[0][1] = Card("S01-0212", Control, owner);
            actor.Hand.Add(Card("S02-0206", Tactic, owner));
            actor.Hand.Add(Card("S01-0210", First, owner));
            actor.Hand.Add(Card("S01-0210", Second, owner));
            // A genuine legal response holds the declaration on the stack.
            // The engine intentionally creates no empty response-only window.
            var responder = Game.State.Players[1 - owner];
            var counter = Card("S01-0016", "synthetic-absolute-defense", 1 - owner);
            counter.Hidden = true;
            counter.SetRound = 0;
            responder.Field[1][2] = counter;
            responder.TemporaryMorale = Cost("S01-0016");
            responder.Hand.Add(Card("S01-0002", "synthetic-counter-cost", 1 - owner));
            Checkpoint();
        }

        internal void AssertPaid(int cost)
        {
            Assert.Equal(20 - cost, Game.State.Players[_owner].TemporaryMorale);
            Assert.Empty(Game.State.Players[_owner].Morale);
        }

        internal void AssertRestricted(int turn)
        {
            Assert.Equal(turn, Game.State.TurnSerial);
            Assert.True(RestrictedGuard.Tapped);
            Assert.True(RestrictedGuard.CannotReadyByEffectUntilTurn >= turn);
            Assert.True(RestrictedGuard.SureHitAgainstLegionsUntilTurn >= turn);
            Assert.True(RestrictedGuard.DiscardAtEndOfTurnUntilTurn >= turn);
        }

        internal void AssertNoRestrictedReady() => Assert.DoesNotContain(Game.State.AuthorityEvents,
            entry => entry.Type == "effect-ready" && entry.TargetInstanceId == Restricted);

        internal void Send(int actor, L12Command command)
        {
            Checkpoint();
            var result = Game.Handle(actor, command);
            Assert.True(result.Accepted, $"{command.Type}: {result.Error}");
            Checkpoint();
        }

        internal L12Command Choose(string choice)
        {
            var prompt = Assert.Single(Game.State.PendingPrompts);
            Assert.Contains(choice, prompt.ValidChoices);
            var command = Typed(prompt, choice);
            Send(prompt.PlayerIndex, command);
            return command;
        }

        internal void PayTemporaryResourcesIfAsked()
        {
            if (Game.State.PendingPrompts.Count != 1
                || Game.State.PendingPrompts[0].Kind != "resource-payment") return;
            var prompt = Assert.Single(Game.State.PendingPrompts);
            Assert.Equal("play-morale-choice", prompt.Continuation);
            var cost = int.Parse(prompt.Data["cost"]);
            var choices = prompt.ValidChoices.Where(id => id.StartsWith("temporary-morale:", StringComparison.Ordinal))
                .Take(cost).ToList();
            Assert.Equal(cost, choices.Count);
            Send(prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: choices,
                ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
                SourceCardId: prompt.SourceCardId, Step: prompt.Step,
                CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller));
        }

        internal void PassResponses()
        {
            for (var safety = 0; Game.State.PendingPrompts.Count > 0; safety++)
            {
                Assert.True(safety < 40, "Actual Nitocris command chain did not quiesce");
                Assert.Equal("response", Assert.Single(Game.State.PendingPrompts).Kind);
                Choose("pass");
            }
        }

        internal void RestControl()
        {
            Checkpoint();
            var result = Game.HandleGm(new("setCardState", _owner, CardInstanceId: Control, Destination: "rested"));
            Assert.True(result.Accepted, result.Error);
            Checkpoint();
        }

        internal void RejectUnchanged(int actor, L12Command command)
        {
            Checkpoint();
            var raw = Game.SerializeFullState(); var hash = Game.ComputeStateHash();
            var rng = JsonSerializer.Serialize(Game.RandomState!.Value);
            var facts = Game.CardFactSignalSequence; var revision = Game.State.Revision;
            Assert.False(Game.Handle(actor, command).Accepted);
            Assert.Equal(raw, Game.SerializeFullState()); Assert.Equal(hash, Game.ComputeStateHash());
            Assert.Equal(rng, JsonSerializer.Serialize(Game.RandomState!.Value));
            Assert.Equal(facts, Game.CardFactSignalSequence); Assert.Equal(revision, Game.State.Revision);
            Checkpoint();
        }

        internal int Count(string id) => Game.State.Players.Sum(p =>
            p.Field.SelectMany(row => row).Count(card => card?.InstanceId == id)
            + p.Hand.Count(card => card.InstanceId == id) + p.Library.Count(card => card.InstanceId == id)
            + p.Graveyard.Count(card => card.InstanceId == id) + p.Resolving.Count(card => card.InstanceId == id)
            + p.Removed.Count(card => card.InstanceId == id));

        private void Checkpoint() { if (_restore) RoundTripAlways(); }

        internal void RoundTripAlways()
        {
            var raw = Game.SerializeFullState(); var hash = Game.ComputeStateHash();
            var rng = Game.RandomState!.Value; var facts = Game.CardFactSignalSequence;
            var revision = Game.State.Revision;
            Game = L12GameEngine.RestoreCheckpoint(Catalog, raw, rng, facts,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
            Assert.Equal(raw, Game.SerializeFullState()); Assert.Equal(hash, Game.ComputeStateHash());
            Assert.Equal(rng, Game.RandomState!.Value); Assert.Equal(facts, Game.CardFactSignalSequence);
            Assert.Equal(revision, Game.State.Revision); Restores++;
        }

        internal void AssertClean()
        {
            Assert.Empty(Game.State.PendingPrompts); Assert.Empty(Game.State.PendingActivations);
            Assert.Empty(Game.State.EffectStack); Assert.Empty(Game.State.DeferredEffectStack);
            Assert.Empty(Game.State.PendingTriggerBatches); Assert.Empty(Game.State.PendingTriggerStackCandidates);
            Assert.Null(Game.State.ResponseWindow);
        }
    }
}
