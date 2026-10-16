using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// Synthetic shared-settlement guard, not a reconstruction of the unavailable private match.
public sealed class SummonOccupiedSlotResultRegressionTests
{
    private const string SourceId = "occupied-result-tutankhamun";
    private const string GuardId = "occupied-result-declared-guard";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-16T02:00:00Z");

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(0, false, true)]
    [InlineData(1, false, true)]
    [InlineData(0, true, false)]
    [InlineData(1, true, false)]
    [InlineData(0, true, true)]
    [InlineData(1, true, true)]
    [Trait("L12Evidence", "card:S01-0207")]
    [Trait("L12Evidence", "entry:occupied-slot-terminal-result")]
    public void RealTutankhamunEntryPublishesFailureWhenItsDeclaredSlotBecomesOccupied(
        int controller, bool occupyDeclaredSlot, bool restoreEveryCommand)
        => AssertRealTutankhamunEntry(controller, occupyDeclaredSlot, restoreEveryCommand, opposingLegions: 3);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "card:S01-0207")]
    [Trait("L12Evidence", "entry:declared-quantity-condition-settlement-failed")]
    public void RealTutankhamunEntryPublishesFailureWhenResponseMakesLegionCountsEqual(
        int controller, bool restoreEveryCommand)
        => AssertRealTutankhamunEntry(controller, occupyDeclaredSlot: true, restoreEveryCommand, opposingLegions: 2);

    private static void AssertRealTutankhamunEntry(int controller, bool occupyDeclaredSlot,
        bool restoreEveryCommand, int opposingLegions)
    {
        var game = Create(controller, opposingLegions: opposingLegions);
        if (restoreEveryCommand) game = RestoreStrict(game);

        Execute(controller, new L12Command("playCard", SourceId, Row: 0, Slot: 0));
        Assert.Equal(SourceId, game.State.Players[controller].Field[0][0]?.InstanceId);
        Assert.Equal(16, game.State.Players[controller].TemporaryMorale);
        Assert.Empty(game.State.EffectStack);

        // Even a unique public grave object must be chosen before the response window.
        var objectPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", objectPrompt.Continuation);
        Assert.Contains(GuardId, objectPrompt.ValidChoices);
        Choose(GuardId);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", slotPrompt.Continuation);
        Assert.Contains("0:1", slotPrompt.ValidChoices);
        Choose("0:1");

        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal(SourceId, item.SourceInstanceId);
        Assert.Equal("enter", item.Trigger);
        Assert.Equal(GuardId, item.Data["declared:entryCards"]);
        Assert.Equal("0:1", item.Data["declared:entrySlot1"]);
        var declaration = Assert.Single(game.State.Events, entry => entry.EffectResultStatus == "declared"
            && entry.Cards.Any(card => card.InstanceId == SourceId));
        Assert.False(string.IsNullOrWhiteSpace(declaration.EffectSceneId));
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", response.Kind);
        // New effect windows start with the active player's priority, not necessarily the opponent.
        // Follow the authoritative prompt until the opponent has the response opportunity.
        Assert.Equal(game.State.ResponseWindow!.PriorityPlayer, response.PlayerIndex);
        if (response.PlayerIndex != 1 - controller)
        {
            Choose("pass");
            response = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", response.Kind);
            Assert.Equal(game.State.ResponseWindow!.PriorityPlayer, response.PlayerIndex);
        }
        Assert.Equal(1 - controller, response.PlayerIndex);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "put"
            && entry.Cards.Any(card => card.InstanceId == GuardId));

        string? blockerId = null;
        if (occupyDeclaredSlot)
        {
            var occupied = game.HandleGm(new L12GmCommand("placeCard", controller,
                CardId: "S01-0002", Row: 0, Slot: 1, TriggerEffects: false));
            Assert.True(occupied.Accepted, occupied.Error);
            blockerId = game.State.Players[controller].Field[0][1]!.InstanceId;
            if (restoreEveryCommand) game = RestoreStrict(game);
            Assert.Equal(response.PromptId, Assert.Single(game.State.PendingPrompts).PromptId);
            Assert.Single(game.State.EffectStack);
        }

        // The two-enemy boundary changes from 1 < 2 at declaration to 2 == 2 after occupation.
        // The original three-enemy control retains its valid quantity condition.
        Assert.Equal(opposingLegions > (occupyDeclaredSlot ? 2 : 1),
            game.State.Players[controller].Field.SelectMany(row => row).OfType<L12CardInstance>().Count()
            < game.State.Players[1 - controller].Field.SelectMany(row => row).OfType<L12CardInstance>().Count());
        for (var safety = 0; safety < 12 && game.State.PendingPrompts.Count > 0; safety++)
        {
            Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
            Choose("pass");
        }

        var player = game.State.Players[controller];
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Equal(SourceId, player.Field[0][0]?.InstanceId);
        Assert.Equal(16, player.TemporaryMorale); // The legitimate play cost is never refunded.
        Assert.Equal(1, CountInstance(game, SourceId));
        Assert.Equal(1, CountInstance(game, GuardId));
        Assert.Equal(2, player.Field.SelectMany(row => row).OfType<L12CardInstance>().Count());
        Assert.Null(player.Field[0][2]);
        Assert.All(player.Field[1], card => Assert.Null(card));

        if (occupyDeclaredSlot)
        {
            Assert.Equal(blockerId, player.Field[0][1]?.InstanceId);
            Assert.Equal(1, CountInstance(game, blockerId!));
            Assert.Single(player.Graveyard, card => card.InstanceId == GuardId);
            Assert.DoesNotContain(game.State.Events, entry => entry.Type == "put"
                && entry.Cards.Any(card => card.InstanceId == GuardId));
        }
        else
        {
            Assert.Equal(GuardId, player.Field[0][1]?.InstanceId);
            Assert.False(player.Field[0][1]!.Tapped);
            Assert.DoesNotContain(player.Graveyard, card => card.InstanceId == GuardId);
            Assert.Single(game.State.Events, entry => entry.Type == "put"
                && entry.Cards.Any(card => card.InstanceId == GuardId));
        }

        // Check the terminal fact last: an unperformed summon must not silently look successful.
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == SourceId));
        Assert.Equal(occupyDeclaredSlot ? "failed" : "resolved", result.EffectResultStatus);
        Assert.Equal(declaration.EffectSceneId, result.EffectSceneId);
        Assert.Equal(declaration.EffectAbilityId, result.EffectAbilityId);
        Assert.Equal(declaration.EffectSegmentId, result.EffectSegmentId);
        if (opposingLegions == 2 && occupyDeclaredSlot)
            Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
                && entry.Text.Contains("军团数量条件失效", StringComparison.Ordinal)
                && entry.Cards.Any(card => card.InstanceId == SourceId));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == SourceId)
            && entry.EffectResultStatus is "declined" or "skipped");

        var beforeDuplicate = game.SerializeFullState();
        Assert.False(game.Handle(controller, new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId,
            Choice: "0:1")).Accepted);
        Assert.Equal(beforeDuplicate, game.SerializeFullState());
        _ = RestoreStrict(game);

        void Execute(int actor, L12Command command)
        {
            var accepted = game.Handle(actor, command);
            Assert.True(accepted.Accepted, accepted.Error);
            if (restoreEveryCommand) game = RestoreStrict(game);
        }

        void Choose(string choice)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            Execute(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                Choice: choice, ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
                SourceCardId: prompt.SourceCardId, Step: prompt.Step, CreatedRevision: prompt.CreatedRevision,
                Controller: prompt.Controller));
        }
    }

    public static IEnumerable<object[]> TwoGuardRows()
    {
        foreach (var controller in new[] { 0, 1 })
        foreach (var occupiedMask in new[] { 0, 1, 2, 3 })
        foreach (var restore in new[] { false, true })
            yield return [controller, occupiedMask, restore];
    }

    [Theory]
    [MemberData(nameof(TwoGuardRows))]
    public void RealTutankhamunTwoGuardEntryContinuesEveryLegalObjectAndFailsOnlyIfAllFail(
        int controller, int occupiedMask, bool restore)
    {
        var scenario = new Scenario(controller, restore);
        const string secondGuard = "occupied-result-second-guard";
        scenario.Game.State.Players[controller].Graveyard.Add(Card("S01-0212", secondGuard, controller));
        scenario.Game.State.Players[1 - controller].Field[1][0] = Card("S01-0002", "occupied-result-fourth-enemy", 1 - controller);
        scenario.Checkpoint();
        scenario.Execute(controller, new L12Command("playCard", SourceId, Row: 0, Slot: 0));
        scenario.Choose(GuardId, secondGuard);
        scenario.Choose("0:1");
        scenario.Choose("0:2");
        scenario.ReachOpponentResponse(controller);
        var blockers = new Dictionary<int, string>();
        for (var index = 0; index < 2; index++)
            if ((occupiedMask & (1 << index)) != 0)
                blockers[index] = scenario.Occupy(controller, index + 1);
        // Even both blockers leave 3 < 4, so this exercises settlement rather than the quantity guard.
        Assert.True(scenario.Game.State.Players[controller].Field.SelectMany(row => row).OfType<L12CardInstance>().Count() < 4);
        scenario.PassResponses();

        var player = scenario.Game.State.Players[controller];
        var ids = new[] { GuardId, secondGuard };
        for (var index = 0; index < ids.Length; index++)
        {
            var failed = blockers.ContainsKey(index);
            Assert.Equal(failed ? blockers[index] : ids[index], player.Field[0][index + 1]?.InstanceId);
            Assert.Equal(failed ? 1 : 0, player.Graveyard.Count(card => card.InstanceId == ids[index]));
            Assert.Equal(failed ? 0 : 1, scenario.Game.State.Events.Count(entry => entry.Type == "put"
                && entry.Cards.Any(card => card.InstanceId == ids[index])));
            Assert.Equal(1, CountInstance(scenario.Game, ids[index]));
        }
        Assert.Equal(16, player.TemporaryMorale);
        Assert.Equal(occupiedMask == 3 ? "failed" : "resolved", ResultFor(scenario.Game, SourceId).EffectResultStatus);
        AssertSettled(scenario.Game);
        _ = RestoreStrict(scenario.Game);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void RealTutankhamunEntryStillResolvesAfterItsSourceReturnsToHand(int controller, bool restore)
    {
        var scenario = new Scenario(controller, restore);
        scenario.Checkpoint();
        scenario.Execute(controller, new L12Command("playCard", SourceId, Row: 0, Slot: 0));
        scenario.Choose(GuardId);
        scenario.Choose("0:1");
        scenario.ReachOpponentResponse(controller);
        scenario.Gm(new L12GmCommand("returnCardToHand", controller, CardInstanceId: SourceId));
        scenario.PassResponses();
        Assert.Single(scenario.Game.State.Players[controller].Hand, card => card.InstanceId == SourceId);
        Assert.Equal(GuardId, scenario.Game.State.Players[controller].Field[0][1]?.InstanceId);
        Assert.Equal(1, CountInstance(scenario.Game, SourceId));
        Assert.Equal(1, CountInstance(scenario.Game, GuardId));
        Assert.Equal("resolved", ResultFor(scenario.Game, SourceId).EffectResultStatus);
        AssertSettled(scenario.Game);
    }

    public static IEnumerable<object[]> OtherSummonRows()
    {
        foreach (var family in new[] { "valkyrie", "thor", "legacy-gift" })
        foreach (var controller in new[] { 0, 1 })
        foreach (var occupied in new[] { false, true })
        foreach (var restore in new[] { false, true })
            yield return [family, controller, occupied, restore];
    }

    [Theory]
    [MemberData(nameof(OtherSummonRows))]
    public void OtherDeclaredGraveSummonsPublishTheRealOutcomeWithoutRevertingTheirCosts(
        string family, int controller, bool occupied, bool restore)
    {
        var scenario = new Scenario(controller, restore, family == "thor" ? "S02-03M1" : null);
        var player = scenario.Game.State.Players[controller];
        player.Hand.Clear(); player.Graveyard.Clear();
        var sourceId = $"occupied-result-{family}-source";
        var entryId = family == "thor" ? sourceId : $"occupied-result-{family}-entry";
        var sourceCardId = family == "valkyrie" ? "S01-0318" : family == "thor" ? "S02-0301" : "S01-0223";
        var entryCardId = family == "valkyrie" ? "S01-0312" : family == "thor" ? "S02-0301" : "S01-0212";
        var source = Card(sourceCardId, sourceId, controller);
        var costIds = Enumerable.Range(0, 3).Select(index => $"occupied-result-thor-cost-{index}").ToArray();
        if (family == "thor")
        {
            player.Graveyard.Add(source);
            foreach (var id in costIds) player.Graveyard.Add(Card("S02-0001", id, controller));
        }
        else
        {
            player.Graveyard.Add(Card(entryCardId, entryId, controller));
            if (family == "valkyrie") player.Hand.Add(source);
            else
            {
                // A pre-migration stack cannot be created by current card play. Restore its old payload,
                // then settle through the real response/pass commands, not a private resolver invocation.
                player.Resolving.Add(source);
                player.Library.Add(Card("S01-0002", "occupied-result-legacy-draw", controller));
                var item = new L12StackItem
                {
                    StackItemId = "occupied-result-legacy-stack", Controller = controller,
                    SourceInstanceId = sourceId, SourceCardId = sourceCardId, SourceName = source.Name,
                    SourceSnapshot = source.Clone(), Trigger = "reaction", Text = "Synthetic legacy Immortal Gift",
                };
                item.Data["declared:entryCard"] = entryId;
                item.Data["declared:entrySlot"] = "0:1";
                scenario.Game.State.EffectStack.Add(item);
                typeof(L12GameEngine).GetMethod("BeginResponseWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(scenario.Game, [item]);
            }
        }
        scenario.Checkpoint();
        if (family == "valkyrie")
        {
            scenario.Execute(controller, new L12Command("playCard", sourceId));
            scenario.Choose(entryId);
            scenario.Choose("0:1");
        }
        else if (family == "thor")
        {
            scenario.Execute(controller, new L12Command("activateAbility", sourceId, Ability: "thorHammerRevive"));
            scenario.Choose(costIds);
            scenario.Choose("0:1");
        }
        var committed = Assert.Single(scenario.Game.State.EffectStack);
        Assert.Equal(sourceId, committed.SourceInstanceId);
        if (family == "legacy-gift") Assert.False(committed.Data.ContainsKey("compositePlan"));
        scenario.ReachOpponentResponse(controller);
        string? blockerId = occupied ? scenario.Occupy(controller, 1) : null;
        scenario.PassResponses();
        player = scenario.Game.State.Players[controller];
        Assert.Equal(occupied ? blockerId : entryId, player.Field[0][1]?.InstanceId);
        Assert.Equal(occupied ? 1 : 0, player.Graveyard.Count(card => card.InstanceId == entryId));
        Assert.Equal(1, CountInstance(scenario.Game, entryId));
        Assert.Equal(occupied ? 0 : 1, scenario.Game.State.Events.Count(entry => entry.Type == "put"
            && entry.Cards.Any(card => card.InstanceId == entryId)));
        Assert.Equal(occupied ? "failed" : "resolved", ResultFor(scenario.Game, sourceId).EffectResultStatus);
        Assert.Null(player.Field[0][0]); Assert.Null(player.Field[0][2]);
        Assert.All(player.Field[1], card => Assert.Null(card));
        if (family == "valkyrie")
        {
            Assert.Equal(7, player.Hp);
            Assert.Equal(17, player.TemporaryMorale);
        }
        else if (family == "thor")
        {
            Assert.Equal(costIds, player.Library.Select(card => card.InstanceId));
            Assert.DoesNotContain(player.Graveyard, card => costIds.Contains(card.InstanceId));
            Assert.Contains("card-name:S02-0301", player.UsedAbilities);
        }
        else
            Assert.Single(player.Hand, card => card.InstanceId == "occupied-result-legacy-draw");
        AssertSettled(scenario.Game);
        _ = RestoreStrict(scenario.Game);
    }

    public static IEnumerable<object[]> CancelRows()
    {
        foreach (var family in new[] { "tutankhamun", "valkyrie", "thor" })
        foreach (var controller in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
            yield return [family, controller, restore];
    }

    [Theory]
    [MemberData(nameof(CancelRows))]
    public void DecliningBeforeCommitDoesNotBecomeAnOccupiedSlotFailure(string family, int controller, bool restore)
    {
        var scenario = new Scenario(controller, restore, family == "thor" ? "S02-03M1" : null);
        var player = scenario.Game.State.Players[controller];
        var sourceId = SourceId;
        if (family != "tutankhamun")
        {
            player.Hand.Clear(); player.Graveyard.Clear();
            sourceId = $"occupied-result-cancel-{family}";
            var source = Card(family == "thor" ? "S02-0301" : "S01-0318", sourceId, controller);
            if (family == "thor")
            {
                player.Graveyard.Add(source);
                for (var index = 0; index < 3; index++)
                    player.Graveyard.Add(Card("S02-0001", $"cancel-thor-cost-{index}", controller));
            }
            else
            {
                player.Hand.Add(source);
                player.Graveyard.Add(Card("S01-0312", "cancel-valkyrie-target", controller));
            }
        }
        scenario.Checkpoint();
        scenario.Execute(controller, family == "thor"
            ? new L12Command("activateAbility", sourceId, Ability: "thorHammerRevive")
            : new L12Command("playCard", sourceId, Row: 0, Slot: 0));
        Assert.Contains("skip", Assert.Single(scenario.Game.State.PendingPrompts).ValidChoices);
        scenario.Choose("skip");
        player = scenario.Game.State.Players[controller];
        Assert.Equal(8, player.Hp);
        Assert.Equal(family == "tutankhamun" ? 16 : 20, player.TemporaryMorale);
        Assert.DoesNotContain(scenario.Game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "failed");
        Assert.Equal(1, CountInstance(scenario.Game, sourceId));
        AssertSettled(scenario.Game);
    }

    private sealed class Scenario
    {
        private readonly bool restore;
        public L12GameEngine Game { get; private set; }
        public Scenario(int controller, bool restore, string? master = null)
        {
            this.restore = restore;
            Game = Create(controller, master);
        }
        public void Checkpoint() { if (restore) Game = RestoreStrict(Game); }
        public void Execute(int actor, L12Command command)
        {
            var result = Game.Handle(actor, command);
            Assert.True(result.Accepted, result.Error);
            Checkpoint();
        }
        public void Choose(params string[] choices)
        {
            var prompt = Assert.Single(Game.State.PendingPrompts);
            Execute(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                Choice: choices.Length == 1 ? choices[0] : null,
                CardInstanceIds: choices.Length > 1 ? [.. choices] : null,
                ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
                SourceCardId: prompt.SourceCardId, Step: prompt.Step, CreatedRevision: prompt.CreatedRevision,
                Controller: prompt.Controller));
        }
        public void Gm(L12GmCommand command)
        {
            var result = Game.HandleGm(command);
            Assert.True(result.Accepted, result.Error);
            Checkpoint();
        }
        public string Occupy(int controller, int slot)
        {
            Gm(new L12GmCommand("placeCard", controller, CardId: "S01-0002", Row: 0, Slot: slot, TriggerEffects: false));
            return Game.State.Players[controller].Field[0][slot]!.InstanceId;
        }
        public void ReachOpponentResponse(int controller)
        {
            var prompt = Assert.Single(Game.State.PendingPrompts);
            Assert.Equal("response", prompt.Kind);
            Assert.Equal(Game.State.ResponseWindow!.PriorityPlayer, prompt.PlayerIndex);
            if (prompt.PlayerIndex == controller) Choose("pass");
            Assert.Equal(1 - controller, Assert.Single(Game.State.PendingPrompts).PlayerIndex);
        }
        public void PassResponses()
        {
            for (var safety = 0; safety < 20 && Game.State.PendingPrompts.Count > 0; safety++)
            {
                Assert.Equal("response", Assert.Single(Game.State.PendingPrompts).Kind);
                Choose("pass");
            }
        }
    }

    private static L12ActionEvent ResultFor(L12GameEngine game, string sourceId)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == sourceId));

    private static void AssertSettled(L12GameEngine game)
    {
        Assert.Empty(game.State.PendingPrompts); Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack); Assert.Empty(game.State.DeferredEffectStack);
        Assert.Empty(game.State.PendingTriggerBatches); Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Equal(L12Phase.Main, game.State.Phase);
    }

    private static L12GameEngine Create(int controller, string? master = null, int opposingLegions = 3)
    {
        var basis = Catalog.DeckAt(0);
        var decks = new[] { basis, basis };
        if (master is not null)
            decks[controller] = new L12PresetDeckDefinition
            {
                Name = "Synthetic summon result master", MasterId = master,
                CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = [],
            };
        var game = new L12GameEngine(Catalog, "synthetic-occupied-summon-result", "LOCAL", 20261016,
            ["Synthetic A", "Synthetic B"], decks, skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2, utcNow: () => Now);
        game.State.ActivePlayer = controller;
        game.State.FirstPlayer = controller;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear();
            player.Resolving.Clear(); player.Removed.Clear(); player.Morale.Clear();
            player.UsedAbilities.Clear(); player.Relic = null; player.ExtraRelics.Clear();
            player.TemporaryMorale = 0;
            player.Hp = 8;
        }
        game.State.PendingPrompts.Clear(); game.State.PendingActivations.Clear();
        game.State.EffectStack.Clear(); game.State.DeferredEffectStack.Clear();
        game.State.PendingTriggerBatches.Clear(); game.State.PendingTriggerStackCandidates.Clear();
        game.State.Events.Clear(); game.State.AuthorityEvents.Clear(); game.State.Log.Clear();
        game.State.Players[controller].TemporaryMorale = 20;
        game.State.Players[controller].Hand.Add(Card("S01-0207", SourceId, controller));
        game.State.Players[controller].Graveyard.Add(Card("S01-0212", GuardId, controller));
        // Three opposing legions isolate slot failure; two also exercise quantity-condition invalidation.
        for (var slot = 0; slot < opposingLegions; slot++)
            game.State.Players[1 - controller].Field[0][slot] = Card("S01-0002",
                $"occupied-result-opposing-legion-{slot}", 1 - controller);
        return game;
    }

    private static L12GameEngine RestoreStrict(L12GameEngine game)
    {
        var raw = game.SerializeFullState();
        var hash = game.ComputeStateHash();
        var random = game.RandomState!.Value;
        var sequence = game.CardFactSignalSequence;
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, raw, random, sequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
        Assert.Equal(raw, restored.SerializeFullState());
        Assert.Equal(hash, restored.ComputeStateHash());
        Assert.Equal(random, restored.RandomState);
        Assert.Equal(sequence, restored.CardFactSignalSequence);
        Assert.Equal(game.State.Revision, restored.State.Revision);
        return restored;
    }

    private static int CountInstance(L12GameEngine game, string instanceId)
        => game.State.Players.Sum(player => player.Field.SelectMany(row => row).Count(card => card?.InstanceId == instanceId)
            + player.Hand.Count(card => card.InstanceId == instanceId) + player.Library.Count(card => card.InstanceId == instanceId)
            + player.Graveyard.Count(card => card.InstanceId == instanceId) + player.Resolving.Count(card => card.InstanceId == instanceId)
            + player.Removed.Count(card => card.InstanceId == instanceId));

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = cardId, Name = definition.NameZh, CardType = definition.CardType,
            Faction = definition.Faction, Cost = definition.Cost ?? 0, HasPrintedCost = definition.Cost.HasValue,
            ImageUrl = definition.ImageUrl, EffectText = definition.Effect, Traits = [.. definition.Traits],
            Profession = definition.Profession, EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0, OwnerIndex = owner, SummonRound = -1,
        };
    }
}
