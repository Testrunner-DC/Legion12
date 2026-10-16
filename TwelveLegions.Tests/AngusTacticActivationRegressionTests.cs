using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// Synthetic initial board. Both tactics, the counter and its discard cost use
// public Handle commands; no private Angus queue or fabricated stack item.
public sealed class AngusTacticActivationRegressionTests
{
    private const string Angus = "S02-06M2", TacticCard = "S01-0008";
    private const string First = "angus-first-tactic", Second = "angus-second-tactic";
    private const string Counter = "angus-real-absolute-defense", Discard = "angus-counter-discard";
    private const string Target = "angus-opposing-front-legion", Trial = "angus-open-trial";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-16T08:00:00Z");

    [Fact]
    public void ApprovedActivationTextHasAnExplicitReviewedAbilityIdentity()
    {
        var ability = Assert.Single(L12AtomicEffectCatalog.Build(Catalog.Cards.Values).Find(Angus)!.Abilities,
            item => item.Trigger == "tactic-effect-resolved");
        Assert.Equal("回合1次 当我方发动战术效果时，试炼+1。", ability.Text);
        Assert.Equal("S02-06M2:ability:tactic-effect-resolved:7869c48f01e64d83", ability.AbilityId);
    }

    [Fact]
    public void TriggerPresentationRetainsTheCompleteApprovedActivationClause()
    {
        var source = new L12CardInstance
        {
            CardId = Angus, InstanceId = "angus-display-source", EffectText = Catalog.Cards[Angus].Effect,
            Name = Catalog.Cards[Angus].NameZh, CardType = "master", Faction = Catalog.Cards[Angus].Faction,
        };
        Assert.Equal("回合1次 当我方发动战术效果时，试炼+1。",
            L12GameEngine.ResolveTriggeredEffectDisplayText(source, "tactic-effect-resolved",
                "战术效果发动时效果", new Dictionary<string, string> { ["ability"] = "angusTacticTrial" }));
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var negateOriginal in new[] { false, true })
        foreach (var owner in new[] { 0, 1 })
        foreach (var restore in new[] { false, true }) yield return [owner, negateOriginal, restore];
    }

    [Theory, MemberData(nameof(Cases))]
    [Trait("L12Evidence", "cards:S02-06M2,S01-0008,S01-0016")]
    [Trait("L12Evidence", "entry:angus-real-tactic-negation-result")]
    public void RealOriginalTacticNormalAndNegatedOutcomesBothAdvanceAngusExactlyOnce(
        int owner, bool negateOriginal, bool restoreEveryCommand)
        => Run(owner, negateOriginal, restoreEveryCommand, assertActivationTiming: false);

    [Theory, MemberData(nameof(Cases))]
    [Trait("L12Evidence", "cards:S02-06M2,S01-0008,S01-0016")]
    [Trait("L12Evidence", "entry:angus-registers-at-tactic-activation")]
    public void AcceptedTacticRegistersAngusBeforeItsOriginalResponseAndSettlement(
        int owner, bool negateOriginal, bool restoreEveryCommand)
        => Run(owner, negateOriginal, restoreEveryCommand, assertActivationTiming: true);

    private static void Run(int owner, bool negateOriginal, bool restoreEveryCommand, bool assertActivationTiming)
    {
        var f = new Fixture(owner, restoreEveryCommand);
        Assert.Equal(0, f.Progress);
        Assert.DoesNotContain(f.OnceKey, f.Game.State.Players[owner].UsedAbilities);
        Assert.Equal(0, PendingAngusCount(f.Game));

        var play = new L12Command("playCard", First, CardInstanceIds: ["angus-resource-0", "angus-resource-1"]);
        f.Send(owner, play);

        var original = Assert.Single(f.Game.State.EffectStack, item => item.SourceInstanceId == First);
        Assert.Equal("play", original.Trigger);
        Assert.False(original.Negated);
        Assert.Contains(f.Game.State.Players[owner].Resolving, card => card.InstanceId == First);
        Assert.Equal(2, f.PaidMorale);
        Assert.Equal(10000, f.Game.State.Players[1 - owner].Field[0][0]!.Troops);
        Assert.Equal(0, f.Progress); // Registration is not immediate trial settlement.
        Assert.DoesNotContain(f.Game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.FirstOrDefault()?.InstanceId == First);
        var response = Assert.Single(f.Game.State.PendingPrompts);
        Assert.Equal("response", response.Kind);
        Assert.Equal(f.Game.State.ResponseWindow!.PriorityPlayer, response.PlayerIndex);
        f.RejectUnchanged(owner, play);

        if (assertActivationTiming)
        {
            // This assertion distinguishes the new ruling from merely removing
            // !Negated at FinishStackItem: the original has not resolved at all.
            Assert.Contains(f.OnceKey, f.Game.State.Players[owner].UsedAbilities);
            Assert.Equal(1, PendingAngusCount(f.Game));
            Assert.DoesNotContain(f.Game.State.Players[1 - owner].UsedAbilities,
                key => key.StartsWith("trigger:angus-tactic:", StringComparison.Ordinal));
        }

        if (negateOriginal)
        {
            // Never infer response priority from the seat: follow the actual
            // response prompt until the opponent can commit Absolute Defense.
            if (Assert.Single(f.Game.State.PendingPrompts).PlayerIndex == owner) f.Choose("pass");
            response = Assert.Single(f.Game.State.PendingPrompts);
            Assert.Equal(1 - owner, response.PlayerIndex);
            Assert.Equal(f.Game.State.ResponseWindow!.PriorityPlayer, response.PlayerIndex);
            Assert.Contains(Counter, response.ValidChoices);
            f.Choose(Counter);
            var cost = Assert.Single(f.Game.State.PendingPrompts);
            Assert.Equal("discard-cost", cost.Kind);
            Assert.Contains(Discard, cost.ValidChoices);
            var paidCommand = f.Choose(Discard);
            Assert.Contains(f.Game.State.Players[1 - owner].Graveyard, card => card.InstanceId == Discard);
            Assert.DoesNotContain(f.Game.State.Players[1 - owner].Hand, card => card.InstanceId == Discard);
            var counterItem = Assert.Single(f.Game.State.EffectStack, item => item.SourceInstanceId == Counter);
            Assert.Equal("response-negate", counterItem.Trigger);
            Assert.Contains(original.StackItemId, counterItem.Targets);
            f.RejectUnchanged(1 - owner, paidCommand);
        }

        f.Drain();
        AssertTacticOutcome(f.Game, First, negateOriginal);
        Assert.Equal(negateOriginal ? 10000 : 9000, f.Game.State.Players[1 - owner].Field[0][0]!.Troops);
        Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == First);
        Assert.DoesNotContain(f.Game.State.Players[owner].Resolving, card => card.InstanceId == First);
        Assert.Equal(2, f.PaidMorale); // Original negation never refunds the play cost.
        if (negateOriginal)
        {
            Assert.Single(f.Game.State.Players[1 - owner].Graveyard, card => card.InstanceId == Counter);
            Assert.Single(f.Game.State.Players[1 - owner].Graveyard, card => card.InstanceId == Discard);
        }
        else
        {
            Assert.Equal(Counter, f.Game.State.Players[1 - owner].Field[1][2]?.InstanceId);
            Assert.Contains(f.Game.State.Players[1 - owner].Hand, card => card.InstanceId == Discard);
        }
        Assert.Equal(1, f.Progress);
        Assert.Contains(f.OnceKey, f.Game.State.Players[owner].UsedAbilities);
        Assert.Equal(1, AngusTacticTriggerCount(f.Game));
        f.AssertClean();

        // A different real tactic in the same turn still resolves, but cannot
        // create a second Angus trigger or refund/reuse the first trigger.
        f.Send(owner, new L12Command("playCard", Second,
            CardInstanceIds: ["angus-resource-2", "angus-resource-3"]));
        Assert.Equal(0, PendingAngusCount(f.Game));
        f.Drain();
        AssertTacticOutcome(f.Game, Second, negated: false);
        Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == Second);
        Assert.Equal(negateOriginal ? 9000 : 8000, f.Game.State.Players[1 - owner].Field[0][0]!.Troops);
        Assert.Equal(4, f.PaidMorale);
        Assert.Equal(1, f.Progress);
        Assert.Equal(1, AngusTacticTriggerCount(f.Game));
        Assert.Equal(0, f.Game.State.Players[owner].SpecialZones.Runes); // Declined separate rune trigger.
        Assert.DoesNotContain(f.Game.State.Players[1 - owner].UsedAbilities,
            key => key.StartsWith("trigger:angus-tactic:", StringComparison.Ordinal));
        f.RejectUnchanged(owner, play);
        f.AssertClean();
        if (restoreEveryCommand) Assert.True(f.Restores >= 8);
    }

    private static int PendingAngusCount(L12GameEngine game)
        => game.State.PendingTriggerBatches.SelectMany(batch => batch.Candidates)
            .Concat(game.State.PendingTriggerStackCandidates)
            .Count(candidate => candidate.SourceCardId == Angus
                && candidate.Data.GetValueOrDefault("ability") == "angusTacticTrial")
            + game.State.EffectStack.Concat(game.State.DeferredEffectStack)
                .Count(item => item.SourceCardId == Angus
                    && item.Data.GetValueOrDefault("ability") == "angusTacticTrial");

    private static void AssertTacticOutcome(L12GameEngine game, string sourceId, bool negated)
    {
        var results = game.State.Events.Where(entry => entry.Type == "effect-result"
            && entry.Cards.FirstOrDefault()?.InstanceId == sourceId).ToArray();
        var applied = game.State.Events.Where(entry => entry.Type == "effect"
            && entry.Cards.FirstOrDefault()?.InstanceId == sourceId).ToArray();
        if (negated)
        {
            Assert.Equal("negated", Assert.Single(results).EffectResultStatus);
            Assert.Empty(applied);
        }
        else
        {
            // AddEffectResultEvent deliberately omits redundant successful legacy
            // receipts without a Flow/fact receipt. Siege has a real public effect
            // event instead; its troop change, payment and zone are asserted above.
            Assert.Single(applied);
            Assert.True(results.Length <= 1);
            Assert.All(results, entry => Assert.Equal("resolved", entry.EffectResultStatus));
        }
    }

    private static int AngusTacticTriggerCount(L12GameEngine game)
        // The separate optional rune trigger is declined before it enters the
        // stack in this fixture, so only the mandatory tactic trigger is published.
        // Do not require a redundant successful legacy effect-result for Angus.
        => game.State.Events.Count(entry => entry.Type == "effect-trigger"
            && entry.Cards.FirstOrDefault()?.CardId == Angus);

    private sealed class Fixture
    {
        internal L12GameEngine Game;
        internal int Restores;
        private readonly int _owner;
        private readonly bool _restore;
        internal string OnceKey => $"trigger:angus-tactic:{Game.State.TurnSerial}";
        internal int Progress => Assert.Single(Game.State.Players[_owner].SpecialZones.Trials,
            card => card.InstanceId == Trial).TrialProgress;
        internal int PaidMorale => Game.State.Players[_owner].Morale.Count(card => card.Tapped);

        internal Fixture(int owner, bool restore)
        {
            _owner = owner; _restore = restore;
            var basis = Catalog.DeckAt(0);
            var decks = new[] { basis, basis };
            decks[owner] = new L12PresetDeckDefinition
            {
                Name = "Synthetic Angus activation", MasterId = Angus,
                CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = ["S02-06S6"],
            };
            Game = new(Catalog, "synthetic-angus-activation", "SYNTHETIC", 161600 + owner,
                ["Synthetic A", "Synthetic B"], decks, skipPreparation: true, disasterMode: "none",
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
                stateFormatVersion: 2, utcNow: () => Now);
            Game.State.ActivePlayer = owner; Game.State.FirstPlayer = owner;
            Game.State.Round = 3; Game.State.TurnSerial = 10; Game.State.Phase = L12Phase.Main;
            foreach (var player in Game.State.Players)
            {
                foreach (var row in player.Field) Array.Clear(row);
                player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Resolving.Clear();
                player.Removed.Clear(); player.Morale.Clear(); player.MoraleDeck.Clear(); player.UsedAbilities.Clear();
                player.Relic = null; player.ExtraRelics.Clear(); player.TemporaryMorale = 0;
                player.SpecialZones.Trials.Clear(); player.SpecialZones.Runes = 0; player.Hp = 8;
                for (var i = 0; i < 4; i++)
                    player.Library.Add(Card("S01-0003", $"angus-library-{player.PlayerIndex}-{i}", player.PlayerIndex));
            }
            Game.State.PendingPrompts.Clear(); Game.State.PendingActivations.Clear();
            Game.State.EffectStack.Clear(); Game.State.DeferredEffectStack.Clear();
            Game.State.PendingTriggerBatches.Clear(); Game.State.PendingTriggerStackCandidates.Clear();
            Game.State.Events.Clear(); Game.State.AuthorityEvents.Clear(); Game.State.Log.Clear();
            var actor = Game.State.Players[owner];
            actor.SpecialZones.Trials.Add(Card("S02-06S6", Trial, owner));
            actor.Hand.Add(Card(TacticCard, First, owner)); actor.Hand.Add(Card(TacticCard, Second, owner));
            for (var i = 0; i < 4; i++) actor.Morale.Add(new()
            {
                CardId = "S02-05C1A", InstanceId = $"angus-resource-{i}", Tapped = false,
            });
            var opponent = Game.State.Players[1 - owner];
            opponent.Hand.Add(Card("S01-0003", Discard, 1 - owner));
            var target = Card("S01-0002", Target, 1 - owner, 10000);
            opponent.Field[0][0] = target;
            var counter = Card("S01-0016", Counter, 1 - owner);
            counter.Hidden = true; counter.SetRound = 0;
            opponent.Field[1][2] = counter;
            Restore();
        }

        private void Restore()
        {
            if (!_restore) return;
            var raw = Game.SerializeFullState(); var hash = Game.ComputeStateHash();
            var random = Game.RandomState!.Value; var fact = Game.CardFactSignalSequence;
            var revision = Game.State.Revision;
            Game = L12GameEngine.RestoreCheckpoint(Catalog, raw, random, fact,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
            Assert.Equal(raw, Game.SerializeFullState()); Assert.Equal(hash, Game.ComputeStateHash());
            Assert.Equal(JsonSerializer.Serialize(random), JsonSerializer.Serialize(Game.RandomState!.Value));
            Assert.Equal(fact, Game.CardFactSignalSequence); Assert.Equal(revision, Game.State.Revision);
            Restores++;
        }

        internal void Send(int actor, L12Command command)
        {
            Restore();
            var result = Game.Handle(actor, command);
            Assert.True(result.Accepted, $"{command.Type}: {result.Error}");
            Restore();
        }

        internal L12Command Choose(string choice)
        {
            var prompt = Assert.Single(Game.State.PendingPrompts);
            Assert.Contains(choice, prompt.ValidChoices);
            var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [choice],
                ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
                SourceCardId: prompt.SourceCardId, Step: prompt.Step, CreatedRevision: prompt.CreatedRevision,
                Controller: prompt.Controller);
            Send(prompt.PlayerIndex, command);
            return command;
        }

        internal void RejectUnchanged(int actor, L12Command command)
        {
            Restore();
            var raw = Game.SerializeFullState(); var hash = Game.ComputeStateHash();
            var random = JsonSerializer.Serialize(Game.RandomState!.Value); var fact = Game.CardFactSignalSequence;
            var result = Game.Handle(actor, command);
            Assert.False(result.Accepted);
            Assert.Equal(raw, Game.SerializeFullState()); Assert.Equal(hash, Game.ComputeStateHash());
            Assert.Equal(random, JsonSerializer.Serialize(Game.RandomState!.Value));
            Assert.Equal(fact, Game.CardFactSignalSequence);
            Restore();
        }

        internal void Drain()
        {
            for (var guard = 0; Game.State.PendingPrompts.Count > 0; guard++)
            {
                Assert.True(guard < 40, "Angus activation command chain did not quiesce");
                var prompt = Assert.Single(Game.State.PendingPrompts);
                if (prompt.Kind == "response") Choose("pass");
                else
                {
                    // The separate optional own-turn rune trigger is not the
                    // tactic trigger under review; decline it through its UI.
                    Assert.Contains("mode:none", prompt.ValidChoices);
                    Choose("mode:none");
                }
            }
        }

        internal void AssertClean()
        {
            Assert.Empty(Game.State.PendingPrompts); Assert.Empty(Game.State.PendingActivations);
            Assert.Empty(Game.State.EffectStack); Assert.Empty(Game.State.DeferredEffectStack);
            Assert.Empty(Game.State.PendingTriggerBatches); Assert.Empty(Game.State.PendingTriggerStackCandidates);
            Assert.Empty(Game.State.Players[_owner].Resolving);
            Assert.Null(Game.State.ResponseWindow); Assert.False(Game.State.IsResolvingStack);
            Assert.Equal(L12Phase.Main, Game.State.Phase);
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
            BaseTroops = troops ?? d.Troops ?? 0, Troops = troops ?? d.Troops ?? 0, DisasterLevel = d.DisasterLevel ?? 0,
            TrialValue = d.TrialValue ?? 0, OwnerIndex = owner, SummonRound = -1,
        };
    }
}
