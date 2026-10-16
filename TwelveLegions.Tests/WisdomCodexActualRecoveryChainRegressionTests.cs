using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// Public synthetic setup, not the original private match. The opponent really
// plays Full Army Sortie and discards for Wisdom Codex; reward declaration,
// recovery, draw and every response boundary all run through Handle commands.
public sealed class WisdomCodexActualRecoveryChainRegressionTests
{
    private const string Sortie = "actual-wisdom-sortie";
    private const string Wisdom = "actual-wisdom-source";
    private const string AnotherWisdom = "actual-wisdom-same-name";
    private const string Recovery = "actual-wisdom-recovery";
    private const string Discard = "actual-wisdom-discard";
    private const string Drawn = "actual-wisdom-drawn";
    private const string LibraryRemainder = "actual-wisdom-library-remainder";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-16T12:00:00Z");
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "card:S01-0012,S01-0224")]
    [Trait("L12Evidence", "entry:actual-tactic-wisdom-reward-recovery")]
    public void ActualSortieDiscardRewardRejectsWisdomNamesAndRecoversAnotherTacticOnce(
        int owner, bool restoreEveryCommand)
    {
        var f = new Fixture(owner, restoreEveryCommand);
        var opponent = 1 - owner;
        var originalHand = f.Game.State.Players[owner].Hand.Count;
        var originalLibrary = f.Game.State.Players[owner].Library.Count;

        var play = new L12Command("playCard", Sortie);
        f.Send(opponent, play);
        f.PayTemporaryResourcesIfAsked();
        f.PassUntil(game => game.State.PendingPrompts.SingleOrDefault() is { Kind: "response" } prompt
            && prompt.PlayerIndex == owner && prompt.ValidChoices.Contains(Wisdom));
        var response = Assert.Single(f.Game.State.PendingPrompts);
        Assert.Equal("response", response.Kind);
        Assert.Equal(owner, response.PlayerIndex);
        Assert.Contains(Wisdom, response.ValidChoices);
        var useWisdom = f.Choose(Wisdom);
        f.PassResponses();

        var discardPrompt = Assert.Single(f.Game.State.PendingPrompts);
        Assert.Equal(opponent, discardPrompt.PlayerIndex);
        Assert.Equal("wisdom-discard", discardPrompt.Data["action"]);
        Assert.Contains(Discard, discardPrompt.ValidChoices);
        Assert.Contains("abandon", discardPrompt.ValidChoices);
        var discard = f.Choose(Discard);
        f.PassResponses();

        // The real target tactic succeeded only after the opponent paid Wisdom's
        // discard requirement. Its authoritative state change and result fact are
        // established before the independent reward is declared.
        Assert.Equal(6, f.Game.State.Players[opponent].NextLegionChargeMaxCost);
        Assert.Single(f.Game.State.Players[opponent].Graveyard, card => card.InstanceId == Sortie);
        Assert.Single(f.Game.State.Players[opponent].Graveyard, card => card.InstanceId == Discard);
        Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == Wisdom);

        var rewardMode = Assert.Single(f.Game.State.PendingPrompts);
        Assert.Equal(owner, rewardMode.PlayerIndex);
        Assert.Equal("pending-activation", rewardMode.Continuation);
        Assert.Contains("mode:recover", rewardMode.ValidChoices);
        Assert.DoesNotContain(Drawn, f.Game.State.Players[owner].Hand.Select(card => card.InstanceId));
        var chooseRecovery = f.Choose("mode:recover");

        var recoveryTarget = Assert.Single(f.Game.State.PendingPrompts);
        Assert.Equal(owner, recoveryTarget.PlayerIndex);
        Assert.Equal("pending-activation", recoveryTarget.Continuation);
        Assert.Contains(Recovery, recoveryTarget.ValidChoices);
        Assert.DoesNotContain(Wisdom, recoveryTarget.ValidChoices);
        Assert.DoesNotContain(AnotherWisdom, recoveryTarget.ValidChoices);

        // Submit an existing but forbidden same-name grave card against the real
        // current activation. Rejection must not consume the prompt or mutate any
        // serialized state, hash, RNG, fact sequence or revision.
        f.RejectUnchanged(owner, Typed(recoveryTarget, AnotherWisdom));
        Assert.Equal(recoveryTarget.PromptId, Assert.Single(f.Game.State.PendingPrompts).PromptId);
        var recover = f.Choose(Recovery);
        f.PassResponses();
        f.AssertClean();

        var player = f.Game.State.Players[owner];
        Assert.Equal(originalHand + 2, player.Hand.Count);
        Assert.Equal(originalLibrary - 1, player.Library.Count);
        Assert.Single(player.Hand, card => card.InstanceId == Drawn);
        Assert.Single(player.Hand, card => card.InstanceId == Recovery);
        Assert.Single(player.Library, card => card.InstanceId == LibraryRemainder);
        Assert.DoesNotContain(player.Library, card => card.InstanceId == Drawn);
        Assert.DoesNotContain(player.Graveyard, card => card.InstanceId == Recovery);
        Assert.Single(player.Graveyard, card => card.InstanceId == Wisdom);
        Assert.Single(player.Graveyard, card => card.InstanceId == AnotherWisdom);
        Assert.Single(f.Game.State.Events, entry => entry.Type == "draw"
            && entry.PlayerIndex == owner
            && entry.Cards.Any(card => card.InstanceId == Wisdom));
        Assert.DoesNotContain(f.Game.State.PendingTriggerStackCandidates,
            candidate => candidate.Trigger == "wisdom-reward");

        foreach (var id in new[] { Sortie, Wisdom, AnotherWisdom, Recovery, Discard, Drawn, LibraryRemainder })
            Assert.Equal(1, f.Count(id));

        // Old successful commands are idempotently rejected after their
        // transaction has closed, with the same full no-mutation guarantee.
        f.RejectUnchanged(opponent, play);
        f.RejectUnchanged(owner, useWisdom);
        f.RejectUnchanged(opponent, discard);
        f.RejectUnchanged(owner, chooseRecovery);
        f.RejectUnchanged(owner, recover);
        if (restoreEveryCommand) Assert.True(f.Restores >= 14);
        f.RoundTripAlways();
    }

    private static L12Command Typed(L12Prompt prompt, string choice) => new("resolvePrompt",
        PromptId: prompt.PromptId, CardInstanceIds: [choice], ActivationId: prompt.ActivationId,
        SourceInstanceId: prompt.SourceInstanceId, SourceCardId: prompt.SourceCardId,
        Step: prompt.Step, CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller);

    private static L12CardInstance Card(string cardId, string id, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = id, CardId = cardId, Name = definition.NameZh, CardType = definition.CardType,
            IsCounterTactic = definition.IsCounterTactic, Faction = definition.Faction,
            ImageUrl = definition.ImageUrl, Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue, EffectText = definition.Effect,
            Traits = [.. definition.Traits], Profession = definition.Profession,
            EffectiveProfession = definition.Profession, BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0, DisasterLevel = definition.DisasterLevel ?? 0,
            OwnerIndex = owner, SummonRound = -1,
        };
    }

    private sealed class Fixture
    {
        internal L12GameEngine Game;
        internal int Restores;
        private readonly int _owner;
        private readonly bool _restore;

        internal Fixture(int owner, bool restore)
        {
            _owner = owner;
            _restore = restore;
            var opponent = 1 - owner;
            var deck = Catalog.DeckAt(0);
            Game = new(Catalog, "wisdom-actual-tactic-recovery", "SYNTHETIC", 161900 + owner,
                ["Synthetic A", "Synthetic B"], [deck, deck], skipPreparation: true,
                disasterMode: "none", autoPassEmptyResponses: false,
                concealHiddenResponseAvailability: false, stateFormatVersion: 2, utcNow: () => Now);
            Game.State.ActivePlayer = opponent;
            Game.State.FirstPlayer = opponent;
            Game.State.Round = 3;
            Game.State.TurnSerial = 10;
            Game.State.Phase = L12Phase.Main;
            foreach (var player in Game.State.Players)
            {
                foreach (var row in player.Field) Array.Clear(row);
                player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Resolving.Clear();
                player.Removed.Clear(); player.Morale.Clear(); player.MoraleDeck.Clear();
                player.Relic = null; player.ExtraRelics.Clear(); player.UsedAbilities.Clear();
                player.TemporaryMorale = 0;
            }

            var codexOwner = Game.State.Players[owner];
            var source = Card("S01-0224", Wisdom, owner);
            source.Hidden = true;
            source.SetRound = 0;
            codexOwner.Field[1][0] = source;
            codexOwner.Graveyard.Add(Card("S01-0224", AnotherWisdom, owner));
            codexOwner.Graveyard.Add(Card("S01-0012", Recovery, owner));
            codexOwner.Library.Add(Card("S01-0003", Drawn, owner));
            codexOwner.Library.Add(Card("S01-0003", LibraryRemainder, owner));

            var tacticOwner = Game.State.Players[opponent];
            tacticOwner.TemporaryMorale = 5;
            tacticOwner.Hand.Add(Card("S01-0012", Sortie, opponent));
            tacticOwner.Hand.Add(Card("S01-0003", Discard, opponent));
            for (var i = 0; i < 12; i++)
                tacticOwner.Library.Add(Card("S01-0003", $"synthetic-opponent-library-{opponent}-{i}", opponent));
            Checkpoint();
        }

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
            var choices = prompt.ValidChoices
                .Where(id => id.StartsWith("temporary-morale:", StringComparison.Ordinal))
                .Take(cost).ToList();
            Assert.Equal(cost, choices.Count);
            Send(prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: choices, ActivationId: prompt.ActivationId,
                SourceInstanceId: prompt.SourceInstanceId, SourceCardId: prompt.SourceCardId,
                Step: prompt.Step, CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller));
        }

        internal void PassResponses()
        {
            for (var safety = 0; Game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            {
                Assert.True(safety < 80, "Actual Wisdom Codex response chain did not quiesce");
                Choose("pass");
            }
        }

        internal void PassUntil(Func<L12GameEngine, bool> condition)
        {
            for (var safety = 0; !condition(Game); safety++)
            {
                Assert.True(safety < 80, "Expected real Wisdom Codex response boundary was not reached");
                var prompt = Assert.Single(Game.State.PendingPrompts);
                Assert.Equal("response", prompt.Kind);
                Choose("pass");
            }
        }

        internal void RejectUnchanged(int actor, L12Command command)
        {
            Checkpoint();
            var raw = Game.SerializeFullState();
            var hash = Game.ComputeStateHash();
            var random = JsonSerializer.Serialize(Game.RandomState!.Value);
            var facts = Game.CardFactSignalSequence;
            var revision = Game.State.Revision;
            Assert.False(Game.Handle(actor, command).Accepted);
            Assert.Equal(raw, Game.SerializeFullState());
            Assert.Equal(hash, Game.ComputeStateHash());
            Assert.Equal(random, JsonSerializer.Serialize(Game.RandomState!.Value));
            Assert.Equal(facts, Game.CardFactSignalSequence);
            Assert.Equal(revision, Game.State.Revision);
            Checkpoint();
        }

        internal int Count(string instanceId) => Game.State.Players.Sum(player =>
            player.Field.SelectMany(row => row).Count(card => card?.InstanceId == instanceId)
            + player.Hand.Count(card => card.InstanceId == instanceId)
            + player.Library.Count(card => card.InstanceId == instanceId)
            + player.Graveyard.Count(card => card.InstanceId == instanceId)
            + player.Resolving.Count(card => card.InstanceId == instanceId)
            + player.Removed.Count(card => card.InstanceId == instanceId)
            + (player.Relic?.InstanceId == instanceId ? 1 : 0)
            + player.ExtraRelics.Count(card => card.InstanceId == instanceId));

        private void Checkpoint()
        {
            if (_restore) RoundTripAlways();
        }

        internal void RoundTripAlways()
        {
            var raw = Game.SerializeFullState();
            var hash = Game.ComputeStateHash();
            var random = Game.RandomState!.Value;
            var facts = Game.CardFactSignalSequence;
            var revision = Game.State.Revision;
            Game = L12GameEngine.RestoreCheckpoint(Catalog, raw, random, facts,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
                utcNow: () => Now);
            Assert.Equal(raw, Game.SerializeFullState());
            Assert.Equal(hash, Game.ComputeStateHash());
            Assert.Equal(random, Game.RandomState!.Value);
            Assert.Equal(facts, Game.CardFactSignalSequence);
            Assert.Equal(revision, Game.State.Revision);
            Restores++;
        }

        internal void AssertClean()
        {
            Assert.Empty(Game.State.PendingPrompts);
            Assert.Empty(Game.State.PendingActivations);
            Assert.Empty(Game.State.EffectStack);
            Assert.Empty(Game.State.DeferredEffectStack);
            Assert.Empty(Game.State.PendingTriggerBatches);
            Assert.Empty(Game.State.PendingTriggerStackCandidates);
            Assert.Null(Game.State.ResponseWindow);
        }
    }
}
