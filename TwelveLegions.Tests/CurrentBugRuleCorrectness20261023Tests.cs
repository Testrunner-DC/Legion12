using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CurrentBugRuleCorrectness20261023Tests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private const string GodPowerId = "rule-correctness-god-power";
    private const string PlagueId = "rule-correctness-plague";
    private const string SortieId = "rule-correctness-sortie";
    private const string WuZetianId = "rule-correctness-wuzetian";

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("Bug", "BUG-20261022-9cd672de:shared-ruling-representative")]
    public void MoraleLockMayDeclareGodPowerAndBlocksOnlyTheNextAffectedReset(int actor, bool restoreEveryCommand)
    {
        // Human ruling: god power is morale. The original report did not name
        // its locking card; Plague is a representative, not an invented original replay.
        var f = new Fixture(actor, restoreEveryCommand);
        var opponent = 1 - actor;
        f.Game.State.Players[actor].Hand.Add(Card("S01-0011", PlagueId, actor));
        f.Game.State.Players[opponent].Morale[0] = new L12MoraleCard
        {
            CardId = "S02-05C1", InstanceId = GodPowerId, IsGodPower = true, Tapped = true,
        };
        f.Game.State.Players[opponent].Morale[1].Tapped = true;
        var unlockedControl = f.Game.State.Players[opponent].Morale[1].InstanceId;
        f.Game.State.Revision++;
        var startRound = f.Game.State.Round;
        var startTurn = f.Game.State.TurnSerial;

        f.Send(actor, new L12Command("playCard", PlagueId));
        var declaration = Assert.Single(f.Game.State.PendingPrompts);
        Assert.Equal(actor, declaration.PlayerIndex);
        Assert.Contains(GodPowerId, declaration.ValidChoices);
        Assert.DoesNotContain(f.Game.State.Players[actor].Morale[0].InstanceId, declaration.ValidChoices);
        f.Choose(cards: [GodPowerId]);
        f.Settle();

        var target = GodPower(f.Game, opponent);
        Assert.True(target.IsGodPower);
        Assert.True(target.Tapped);
        Assert.True(target.CannotUntapAtNextReset);
        Assert.Equal(0, target.CannotUntapUntilRound);
        Assert.Contains(f.Game.State.Players[actor].Graveyard, card => card.InstanceId == PlagueId);

        f.Send(actor, new L12Command("endTurn"));
        f.Settle();
        Assert.Equal(opponent, f.Game.State.ActivePlayer);
        Assert.Equal(startRound + 1, f.Game.State.Round);
        Assert.Equal(startTurn + 1, f.Game.State.TurnSerial);
        target = GodPower(f.Game, opponent);
        Assert.True(target.Tapped);
        Assert.True(target.IsGodPower);
        Assert.False(target.CannotUntapAtNextReset);
        Assert.False(Assert.Single(f.Game.State.Players[opponent].Morale,
            morale => morale.InstanceId == unlockedControl).Tapped);

        f.Send(opponent, new L12Command("endTurn"));
        f.Settle();
        Assert.Equal(actor, f.Game.State.ActivePlayer);
        Assert.True(GodPower(f.Game, opponent).Tapped);
        f.Send(actor, new L12Command("endTurn"));
        f.Settle();
        Assert.Equal(opponent, f.Game.State.ActivePlayer);
        Assert.Equal(startRound + 3, f.Game.State.Round);
        Assert.Equal(startTurn + 3, f.Game.State.TurnSerial);
        target = GodPower(f.Game, opponent);
        Assert.False(target.Tapped);
        Assert.False(target.CannotUntapAtNextReset);
        Assert.Equal(0, target.CannotUntapUntilRound);
        Assert.True(target.IsGodPower);
        Assert.Equal("S02-05C1", target.CardId);
        Assert.Equal(2, f.Game.State.Events.Count(entry => entry.Type == "phase"
            && entry.PlayerIndex == opponent && entry.Text == "执行重置阶段"));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("Bug", "BUG-20261022-8c189d6c")]
    public void SortieDoesNotGrantChargeToUnreducedSevenCostWuZetian(int actor, bool restoreEveryCommand)
    {
        var f = new Fixture(actor, restoreEveryCommand);
        Assert.Equal(7, Catalog.Cards["S01-0102"].Cost);
        f.Game.State.Players[actor].Hand.Add(Card("S01-0012", SortieId, actor));
        f.Game.State.Players[actor].Hand.Add(Card("S01-0102", WuZetianId, actor));
        f.Game.State.Revision++;

        f.Send(actor, new L12Command("playCard", SortieId));
        f.Settle();
        Assert.Equal(6, f.Game.State.Players[actor].NextLegionChargeMaxCost);
        Assert.Equal(1, f.Game.State.Players[actor].Morale.Count(morale => morale.Tapped));

        f.Send(actor, new L12Command("playCard", WuZetianId, Row: 0, Slot: 0));
        f.Settle();
        var player = f.Game.State.Players[actor];
        var legion = Assert.IsType<L12CardInstance>(player.Field[0][0]);
        Assert.Equal(WuZetianId, legion.InstanceId);
        Assert.Equal(7, legion.Cost);
        Assert.Equal(7, legion.CurrentCost);
        Assert.Equal(0, legion.CostModifier);
        Assert.Equal(0, legion.ContinuousCostModifier);
        Assert.Equal(8, player.Morale.Count(morale => morale.Tapped));
        Assert.False(legion.HasCharge);
        Assert.False(legion.Tapped);
        Assert.Equal(f.Game.State.Round, legion.SummonRound);
        Assert.Equal(6, player.NextLegionChargeMaxCost);
        Assert.DoesNotContain(f.Game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("全军出击", StringComparison.Ordinal)
            && entry.Cards.Any(card => card.InstanceId == WuZetianId));

        f.Checkpoint();
        var before = f.Game.SerializeFullState();
        var beforeRandom = f.Game.RandomState;
        var beforeFacts = f.Game.CardFactSignalSequence;
        var attack = f.Game.Handle(actor, new L12Command("attack", WuZetianId,
            Target: new L12AttackTarget("master")));
        Assert.False(attack.Accepted);
        Assert.Equal("刚登场的军团不能进攻", attack.Error);
        Assert.Equal(before, f.Game.SerializeFullState());
        Assert.Equal(beforeRandom, f.Game.RandomState);
        Assert.Equal(beforeFacts, f.Game.CardFactSignalSequence);
        f.Checkpoint();
        Assert.False(f.Game.State.Players[actor].Field[0][0]!.Tapped);
        f.AssertIdle();
    }

    private static L12MoraleCard GodPower(L12GameEngine game, int owner)
        => Assert.Single(game.State.Players[owner].Morale, morale => morale.InstanceId == GodPowerId);

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = definition.Id, OwnerIndex = owner,
            Name = definition.NameZh, CardType = definition.CardType, Faction = definition.Faction,
            ImageUrl = definition.ImageUrl, Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue, IsCounterTactic = definition.IsCounterTactic,
            EffectText = definition.Effect, Traits = [.. definition.Traits], Profession = definition.Profession,
            EffectiveProfession = definition.Profession, BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0, DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0, SummonRound = -1,
        };
    }

    private sealed class Fixture
    {
        private readonly bool _restoreEveryCommand;
        internal L12GameEngine Game { get; private set; }

        internal Fixture(int actor, bool restoreEveryCommand)
        {
            _restoreEveryCommand = restoreEveryCommand;
            var basis = Catalog.DeckAt(0);
            var deck = new L12PresetDeckDefinition
            {
                Name = "Synthetic rule correctness", MasterId = "S02-05M2",
                CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = [],
            };
            Game = new L12GameEngine(Catalog, "current-rule-correctness", "RULE-CORRECT", 102360 + actor,
                ["Synthetic A", "Synthetic B"], [deck, deck], skipPreparation: true, disasterMode: "none",
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2,
                utcNow: () => DateTimeOffset.Parse("2026-10-23T03:00:00+08:00"));
            foreach (var player in Game.State.Players)
            {
                foreach (var row in player.Field) Array.Clear(row);
                player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Resolving.Clear();
                player.Removed.Clear(); player.Morale.Clear(); player.MoraleDeck.Clear(); player.UsedAbilities.Clear();
                player.Relic = null; player.ExtraRelics.Clear(); player.Hp = 8;
                for (var index = 0; index < 12; index++)
                    player.Library.Add(Card("S02-0004", $"rule-library-{player.PlayerIndex}-{index}", player.PlayerIndex));
                for (var index = 0; index < 8; index++)
                    player.Morale.Add(new L12MoraleCard
                    {
                        CardId = "S02-05C1", InstanceId = $"rule-payment-{player.PlayerIndex}-{index}",
                    });
            }
            Game.State.ActivePlayer = actor; Game.State.FirstPlayer = actor;
            Game.State.Round = 3; Game.State.TurnSerial = 6; Game.State.Phase = L12Phase.Main;
            Game.State.PendingPrompts.Clear(); Game.State.PendingActivations.Clear();
            Game.State.EffectStack.Clear(); Game.State.DeferredEffectStack.Clear();
            Game.State.PendingTriggerBatches.Clear(); Game.State.PendingTriggerStackCandidates.Clear();
            Game.State.SuspendedCombatContexts.Clear(); Game.State.Events.Clear(); Game.State.AuthorityEvents.Clear();
            Game.State.Log.Clear(); Game.State.LastAction = null; Game.State.ResponseWindow = null;
            Game.State.PendingDefense = null; Game.State.ActiveDisaster = null;
            Game.State.Revision++;
        }

        internal void Send(int actor, L12Command command)
        {
            Checkpoint();
            var result = Game.Handle(actor, command);
            Assert.True(result.Accepted, $"{command.Type}: {result.Error}");
            Checkpoint();
        }

        internal void Choose(string? choice = null, List<string>? cards = null)
        {
            var prompt = Assert.Single(Game.State.PendingPrompts);
            Send(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                Choice: choice, CardInstanceIds: cards, ActivationId: prompt.ActivationId,
                SourceInstanceId: prompt.SourceInstanceId, SourceCardId: prompt.SourceCardId,
                Step: prompt.Step, CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller));
        }

        internal void Settle()
        {
            for (var count = 0; Game.State.PendingPrompts.Count > 0; count++)
            {
                Assert.True(count < 32, "Rule-correctness flow exceeded its bounded prompt count.");
                var prompt = Assert.Single(Game.State.PendingPrompts);
                if (prompt.Kind == "response") Choose("pass");
                else if (prompt.Kind == "resource-payment" && prompt.Continuation == "play-morale-choice")
                {
                    var cost = int.Parse(prompt.Data["cost"]);
                    var readyMorale = Game.State.Players[prompt.PlayerIndex].Morale
                        .Where(morale => !morale.Tapped && !morale.IsGodPower
                            && prompt.ValidChoices.Contains(morale.InstanceId))
                        .Take(cost).Select(morale => morale.InstanceId).ToList();
                    Assert.Equal(cost, readyMorale.Count);
                    Choose(cards: readyMorale);
                }
                else if (prompt.SourceCardId == "S01-0102" && prompt.ValidChoices.Contains("mode:none"))
                    Choose("mode:none");
                else throw new Xunit.Sdk.XunitException(
                    $"Unexpected prompt {prompt.Kind}/{prompt.Continuation}/{prompt.SourceCardId}.");
            }
            AssertIdle();
        }

        internal void Checkpoint()
        {
            if (!_restoreEveryCommand) return;
            var beforeHash = Game.ComputeStateHash();
            var before = Game.SerializeFullState();
            var beforeRandom = Game.RandomState!.Value;
            var restored = L12GameEngine.RestoreCheckpoint(Catalog, before, beforeRandom,
                Game.CardFactSignalSequence, autoPassEmptyResponses: false,
                concealHiddenResponseAvailability: false);
            Assert.Equal(before, restored.SerializeFullState());
            Assert.Equal(beforeHash, restored.ComputeStateHash());
            Assert.Equal(beforeRandom, restored.RandomState);
            Game = restored;
        }

        internal void AssertIdle()
        {
            Assert.Equal(L12Phase.Main, Game.State.Phase);
            Assert.Empty(Game.State.PendingPrompts);
            Assert.Empty(Game.State.PendingActivations);
            Assert.Empty(Game.State.EffectStack);
            Assert.Empty(Game.State.DeferredEffectStack);
            Assert.Empty(Game.State.PendingTriggerBatches);
            Assert.Empty(Game.State.PendingTriggerStackCandidates);
            Assert.Null(Game.State.PendingDefense);
            Assert.Null(Game.State.ResponseWindow);
        }
    }
}
