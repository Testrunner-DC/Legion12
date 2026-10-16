using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// Synthetic initial boards only. The ordinary tactic, Ptolemy, every response,
// and every effect decision use public Handle commands.
public sealed class TacticEffectActivationSemanticsRegressionTests
{
    private const string Angus = "S02-06M2", TrialCard = "S02-06S6";
    private const string EvilRitual = "S01-0006", PeaceTalk = "S01-0015", Ptolemy = "S01-0211";
    private const string Tactic = "effect-activation-tactic", PtolemyInstance = "effect-activation-ptolemy";
    private const string Discard = "effect-activation-discard", Trial = "effect-activation-trial";
    private const string Marker = "compositeTacticActivationSource";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-17T04:00:00Z");

    public static IEnumerable<object[]> Cases()
    {
        foreach (var tactic in new[] { EvilRitual, PeaceTalk })
        foreach (var owner in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
            yield return [tactic, owner, restore];
    }

    [Theory, MemberData(nameof(Cases))]
    [Trait("L12Evidence", "cards:S02-06M2,S01-0006,S01-0015,S01-0211")]
    [Trait("L12Evidence", "entry:public-tactic-effect-repeat-vs-composite-continuation")]
    public void PtolemyRepeatPublishesOneTacticEffectActivationReceiptAndCompositeContinuationReusesIt(
        string tacticCardId, int owner, bool restoreEveryCommand)
    {
        var f = new Fixture(tacticCardId, owner, restoreEveryCommand);
        var ownerLibraryBefore = f.Game.State.Players[owner].Library.Count;
        var opponentLibraryBefore = f.Game.State.Players[1 - owner].Library.Count;
        var opponentHpBefore = f.Game.State.Players[1 - owner].Hp;

        f.Send(owner, new("playCard", Tactic));
        f.Drain();

        Assert.Equal(tacticCardId, f.Game.State.Players[owner].LastActiveTacticCardId);
        Assert.Equal(f.Game.State.TurnSerial, f.Game.State.Players[owner].LastActiveTacticTurnSerial);
        Assert.Equal(1, f.Progress);
        Assert.Equal(1, f.AngusTriggers);
        Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == Tactic);
        if (tacticCardId == EvilRitual)
        {
            Assert.Equal(opponentHpBefore - 1, f.Game.State.Players[1 - owner].Hp);
            Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == Discard);
        }
        else
        {
            Assert.Equal(ownerLibraryBefore - 2, f.Game.State.Players[owner].Library.Count);
            Assert.Equal(opponentLibraryBefore - 1, f.Game.State.Players[1 - owner].Library.Count);
        }

        f.Send(owner, new("playCard", PtolemyInstance, Row: 1, Slot: 0));
        var repeated = f.DriveUntilRepeatedEffect();

        Assert.Equal("true", repeated.Data.GetValueOrDefault("repeatedEffectOnly"));
        Assert.StartsWith("repeat-effect-", repeated.SourceInstanceId, StringComparison.Ordinal);
        Assert.Equal(tacticCardId, repeated.SourceCardId);
        var repeatIdentity = $"{owner}:{repeated.SourceInstanceId}";
        Assert.Equal(repeatIdentity, repeated.Data.GetValueOrDefault(Marker));

        f.Drain();

        var repeatSegments = f.RepeatSegments.OrderBy(segment => segment.Segment).ToArray();
        Assert.NotEmpty(repeatSegments);
        Assert.All(repeatSegments, segment =>
        {
            Assert.Equal("true", segment.RepeatedEffectOnly);
            Assert.Equal(repeatIdentity, segment.ActivationReceipt);
        });
        if (tacticCardId == EvilRitual)
        {
            var segment = Assert.Single(repeatSegments);
            Assert.Equal("0", segment.Segment);
            Assert.Equal("evil-ritual-effect", segment.Flow);
            Assert.Equal(opponentHpBefore - 2, f.Game.State.Players[1 - owner].Hp);
            Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == Discard);
        }
        else
        {
            Assert.Equal(["0", "1"], repeatSegments.Select(segment => segment.Segment));
            Assert.Equal(["peace-draw", "peace-negotiation"], repeatSegments.Select(segment => segment.Flow));
            Assert.Single(repeatSegments.Select(segment => segment.ActivationReceipt).Distinct());
            Assert.Equal(ownerLibraryBefore - 4, f.Game.State.Players[owner].Library.Count);
            Assert.Equal(opponentLibraryBefore - 2, f.Game.State.Players[1 - owner].Library.Count);
        }

        // The ordinary tactic has already consumed Angus's printed once-per-turn gate.
        // Do not clear that gate to manufacture a second progress event: the repeated
        // effect is proven here by its source-bound receipt and actual settlement.
        Assert.Equal(1, f.Progress);
        Assert.Equal(1, f.AngusTriggers);
        Assert.Contains($"trigger:angus-tactic:{f.Game.State.TurnSerial}",
            f.Game.State.Players[owner].UsedAbilities);
        Assert.Contains(f.Game.State.Players[owner].Field[1], card => card?.InstanceId == PtolemyInstance);
        Assert.DoesNotContain(f.Game.State.Players[owner].Graveyard,
            card => card.InstanceId.StartsWith("repeat-effect-", StringComparison.Ordinal));
        Assert.DoesNotContain(f.Game.State.Players[owner].Resolving,
            card => card.InstanceId.StartsWith("repeat-effect-", StringComparison.Ordinal));
        Assert.Equal(Catalog.Cards[tacticCardId].Cost + Catalog.Cards[Ptolemy].Cost, f.PaidMorale);
        f.AssertClean();
        if (restoreEveryCommand) Assert.True(f.Restores >= 6);
    }

    private sealed record ObservedRepeatSegment(string StackItemId, string Segment, string Flow,
        string? RepeatedEffectOnly, string? ActivationReceipt);

    private sealed class Fixture
    {
        internal L12GameEngine Game;
        internal int Restores;
        internal readonly List<ObservedRepeatSegment> RepeatSegments = [];
        private readonly HashSet<string> _observedRepeatStackIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _tacticCardId;
        private readonly int _owner;
        private readonly bool _restore;
        internal int Progress => Assert.Single(Game.State.Players[_owner].SpecialZones.Trials,
            card => card.InstanceId == Trial).TrialProgress;
        internal int AngusTriggers => Game.State.Events.Count(entry => entry.Type == "effect-trigger"
            && entry.Cards.FirstOrDefault()?.CardId == Angus
            && entry.Text.Contains("试炼", StringComparison.Ordinal));
        internal int PaidMorale => Game.State.Players[_owner].Morale.Count(card => card.Tapped);

        internal Fixture(string tacticCardId, int owner, bool restore)
        {
            _tacticCardId = tacticCardId;
            _owner = owner;
            _restore = restore;
            var basis = Catalog.DeckAt(0);
            var decks = new[] { basis, basis };
            decks[owner] = new L12PresetDeckDefinition
            {
                Name = "Synthetic tactic-effect semantics",
                MasterId = Angus,
                CardIds = [.. basis.CardIds],
                MoraleIds = [.. basis.MoraleIds],
                SpecialIds = [TrialCard],
            };
            Game = new(Catalog, "tactic-effect-semantics", "SYNTHETIC", 161800 + owner,
                ["Synthetic A", "Synthetic B"], decks, skipPreparation: true, disasterMode: "none",
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
                stateFormatVersion: 2, utcNow: () => Now);
            Game.State.ActivePlayer = owner;
            Game.State.FirstPlayer = owner;
            Game.State.Round = 3;
            Game.State.TurnSerial = 10;
            Game.State.Phase = L12Phase.Main;
            foreach (var player in Game.State.Players)
            {
                foreach (var row in player.Field) Array.Clear(row);
                player.Hand.Clear();
                player.Library.Clear();
                player.Graveyard.Clear();
                player.Resolving.Clear();
                player.Removed.Clear();
                player.Morale.Clear();
                player.MoraleDeck.Clear();
                player.UsedAbilities.Clear();
                player.Relic = null;
                player.ExtraRelics.Clear();
                player.TemporaryMorale = 0;
                player.SpecialZones.Trials.Clear();
                player.SpecialZones.Runes = 0;
                player.Hp = 8;
                for (var index = 0; index < 12; index++)
                    player.Library.Add(Card("S01-0003", $"effect-library-{player.PlayerIndex}-{index}",
                        player.PlayerIndex));
            }
            Game.State.PendingPrompts.Clear();
            Game.State.PendingActivations.Clear();
            Game.State.EffectStack.Clear();
            Game.State.DeferredEffectStack.Clear();
            Game.State.PendingTriggerBatches.Clear();
            Game.State.PendingTriggerStackCandidates.Clear();
            Game.State.Events.Clear();
            Game.State.AuthorityEvents.Clear();
            Game.State.Log.Clear();

            var actor = Game.State.Players[owner];
            actor.SpecialZones.Trials.Add(Card(TrialCard, Trial, owner));
            actor.Hand.Add(Card(tacticCardId, Tactic, owner));
            actor.Hand.Add(Card(Ptolemy, PtolemyInstance, owner));
            actor.Hand.Add(Card("S01-0003", Discard, owner));
            for (var index = 0; index < 10; index++) actor.Morale.Add(new()
            {
                CardId = "S02-05C1A",
                InstanceId = $"effect-resource-{owner}-{index}",
                Tapped = false,
            });
            Restore();
        }

        private void Restore()
        {
            if (!_restore) return;
            var raw = Game.SerializeFullState();
            var hash = Game.ComputeStateHash();
            var random = Game.RandomState!.Value;
            var fact = Game.CardFactSignalSequence;
            var revision = Game.State.Revision;
            Game = L12GameEngine.RestoreCheckpoint(Catalog, raw, random, fact,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
            Assert.Equal(raw, Game.SerializeFullState());
            Assert.Equal(hash, Game.ComputeStateHash());
            Assert.Equal(JsonSerializer.Serialize(random), JsonSerializer.Serialize(Game.RandomState!.Value));
            Assert.Equal(fact, Game.CardFactSignalSequence);
            Assert.Equal(revision, Game.State.Revision);
            Restores++;
            CaptureRepeatSegments();
        }

        internal void Send(int actor, L12Command command)
        {
            Restore();
            var result = Game.Handle(actor, command);
            Assert.True(result.Accepted, $"{command.Type}: {result.Error}");
            CaptureRepeatSegments();
            Restore();
        }

        internal L12StackItem DriveUntilRepeatedEffect()
        {
            for (var guard = 0; guard < 80; guard++)
            {
                CaptureRepeatSegments();
                var repeated = Game.State.EffectStack.LastOrDefault(item =>
                    item.SourceInstanceId.StartsWith("repeat-effect-", StringComparison.Ordinal));
                if (repeated is not null) return repeated;
                AnswerCurrentPrompt();
            }
            throw new InvalidOperationException("Ptolemy did not publish a repeated tactic effect.");
        }

        internal void Drain()
        {
            for (var guard = 0; Game.State.PendingPrompts.Count > 0; guard++)
            {
                Assert.True(guard < 100, "Tactic-effect command chain did not quiesce");
                AnswerCurrentPrompt();
            }
            CaptureRepeatSegments();
        }

        private void AnswerCurrentPrompt()
        {
            var prompt = Assert.Single(Game.State.PendingPrompts);
            string[] choices;
            if (prompt.Kind == "response") choices = ["pass"];
            else if (prompt.ValidChoices.Contains(Discard)) choices = [Discard];
            else if (prompt.ValidChoices.Contains("agree")) choices = ["agree"];
            else if (prompt.ValidChoices.Contains("mode:none")) choices = ["mode:none"];
            else throw new InvalidOperationException(
                $"Unexpected {_tacticCardId} prompt {prompt.Kind}/{prompt.Continuation}: "
                + $"{prompt.Text}; {string.Join(',', prompt.ValidChoices)}");
            Choose(choices);
        }

        private void Choose(string[] choices)
        {
            var prompt = Assert.Single(Game.State.PendingPrompts);
            Assert.All(choices, choice => Assert.Contains(choice, prompt.ValidChoices));
            Send(prompt.PlayerIndex, Typed(prompt, choices));
        }

        private void CaptureRepeatSegments()
        {
            foreach (var item in Game.State.EffectStack.Concat(Game.State.DeferredEffectStack)
                         .Where(item => item.SourceCardId == _tacticCardId
                             && item.SourceInstanceId.StartsWith("repeat-effect-", StringComparison.Ordinal)
                             && item.Trigger == "play"
                             && item.Data.GetValueOrDefault("repeatedEffectOnly") == "true"))
            {
                if (!_observedRepeatStackIds.Add(item.StackItemId)) continue;
                RepeatSegments.Add(new(item.StackItemId,
                    item.Data.GetValueOrDefault("compositeSegment") ?? "",
                    item.Data.GetValueOrDefault("atomicFlow") ?? "",
                    item.Data.GetValueOrDefault("repeatedEffectOnly"),
                    item.Data.GetValueOrDefault(Marker)));
            }
        }

        internal void AssertClean()
        {
            Assert.Empty(Game.State.PendingPrompts);
            Assert.Empty(Game.State.PendingActivations);
            Assert.Empty(Game.State.EffectStack);
            Assert.Empty(Game.State.DeferredEffectStack);
            Assert.Empty(Game.State.PendingTriggerBatches);
            Assert.Empty(Game.State.PendingTriggerStackCandidates);
            Assert.Empty(Game.State.Players[_owner].Resolving);
            Assert.Null(Game.State.ResponseWindow);
            Assert.False(Game.State.IsResolvingStack);
            Assert.Equal(L12Phase.Main, Game.State.Phase);
        }
    }

    private static L12Command Typed(L12Prompt prompt, string[] choices) => new("resolvePrompt",
        PromptId: prompt.PromptId, CardInstanceIds: choices.ToList(),
        ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
        SourceCardId: prompt.SourceCardId, Step: prompt.Step,
        CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller);

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new()
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            IsCounterTactic = definition.IsCounterTactic,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }
}
