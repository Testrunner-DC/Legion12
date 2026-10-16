using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// Public synthetic setup, not the original private match. Charge, attack, the
// returned morale, sure hit and both response branches use real Handle commands.
// Covered counter cards are initial fixtures: their placement fees are not tested.
public sealed class GuanYuChargeSureHitActualChainRegressionTests
{
    private const string Sortie = "actual-full-army-sortie", GuanYu = "actual-charge-guanyu";
    private const string Target = "actual-front-target", EmptyCity = "actual-empty-city";
    private const string Dawn = "actual-battle-until-dawn", OwnDefense = "actual-independent-defense";
    private const string EnemyDefense = "actual-root-block-defense", Discard = "actual-defense-discard";
    private const string OwnMorale = "actual-guanyu-return-cost", EnemyMorale = "actual-empty-city-available-cost";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-16T12:00:00Z");
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> Cases()
    {
        foreach (var owner in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
        foreach (var negateDawn in new[] { false, true }) yield return [owner, restore, negateDawn];
    }

    [Theory, MemberData(nameof(Cases))]
    [Trait("L12Evidence", "card:S01-0012,S01-0106,S01-0120,S01-0020,S01-0016")]
    [Trait("L12Evidence", "entry:actual-charge-guanyu-sure-hit-response")]
    public void ActualChargeGuanYuRejectsEmptyCityButPreservesDawnAndIndependentNegation(
        int owner, bool restoreEveryCommand, bool negateDawn)
    {
        var f = new Fixture(owner, restoreEveryCommand);
        var enemy = 1 - owner;
        var playSortie = new L12Command("playCard", Sortie);
        f.Send(owner, playSortie);
        f.PayTemporaryResourcesIfAsked();
        f.PassResponses();
        f.AssertClean();
        Assert.Equal(6, f.Game.State.Players[owner].NextLegionChargeMaxCost);
        Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == Sortie);
        f.RejectUnchanged(owner, playSortie);

        var playGuanYu = new L12Command("playCard", GuanYu, Row: 0, Slot: 0);
        f.Send(owner, playGuanYu);
        f.PayTemporaryResourcesIfAsked();
        f.AssertClean();
        Assert.Equal(GuanYu, f.Attacker.InstanceId);
        Assert.Equal(f.Game.State.Round, f.Attacker.SummonRound);
        Assert.True(f.Attacker.HasCharge);
        Assert.False(f.Attacker.HasSureHit);
        Assert.False(f.Attacker.Tapped);
        Assert.Null(f.Game.State.Players[owner].NextLegionChargeMaxCost);
        f.AssertHandPlayFees();
        Assert.Single(f.Game.State.Players[owner].Morale, morale => morale.InstanceId == OwnMorale);

        // This attack is in the entry round, not an older legion with an injected
        // HasCharge/HasSureHit flag or a fabricated opponent-attack stack item.
        var attack = new L12Command("attack", GuanYu, Target: new("legion", Target));
        f.Send(owner, attack);
        Assert.True(f.Attacker.Tapped);
        Assert.Equal(1, f.Attacker.AttacksThisTurn);
        Assert.False(f.Game.State.PendingDefense!.SureHit);
        var mode = Assert.Single(f.Game.State.PendingPrompts);
        Assert.Equal("pending-activation", mode.Continuation);
        Assert.Equal(GuanYu, mode.SourceInstanceId);
        f.Choose("mode:use");
        var cost = Assert.Single(f.Game.State.PendingPrompts);
        Assert.Contains(OwnMorale, cost.ValidChoices);
        var paidChoice = f.Choose(OwnMorale);
        var guanYuEffect = Assert.Single(f.Game.State.EffectStack);
        Assert.Equal(GuanYu, guanYuEffect.SourceInstanceId);
        Assert.Equal("attack", guanYuEffect.Trigger);
        Assert.Equal("guanyu", guanYuEffect.Data["attackPlan"]);
        f.AssertCostsRetained();
        Assert.False(f.Game.State.PendingDefense!.SureHit);
        f.RejectUnchanged(owner, paidChoice);

        // Do not test the block gate before the ability has actually resolved.
        // Follow the authoritative priority, not a presumed starting seat.
        f.PassUntil(game => game.State.EffectStack.LastOrDefault()?.Trigger == "opponent-attack"
            && ResponsePlayer(game) == enemy);
        var root = Assert.Single(f.Game.State.EffectStack);
        Assert.Equal(1, f.FactionRecoveryDeclines);
        Assert.Equal("opponent-attack", root.Trigger);
        var rootId = root.StackItemId;
        Assert.Equal(GuanYu, root.SourceInstanceId);
        Assert.True(f.Attacker.HasSureHit);
        Assert.True(f.Game.State.PendingDefense!.SureHit);
        var combatId = f.Game.State.PendingDefense.CombatId;
        Assert.False(f.Game.State.PendingDefense.BlockedByResponse);
        Assert.Equal(6000, f.Attacker.Troops);
        Assert.Equal(2000, f.Defender.Troops);
        f.AssertCostsRetained();
        var rootPrompt = Assert.Single(f.Game.State.PendingPrompts);
        Assert.Equal("response", rootPrompt.Kind);
        Assert.Equal(enemy, rootPrompt.PlayerIndex);
        Assert.DoesNotContain(EmptyCity, rootPrompt.ValidChoices);
        Assert.DoesNotContain(EnemyDefense, rootPrompt.ValidChoices);
        Assert.Contains(Dawn, rootPrompt.ValidChoices);
        f.AssertUnusedBlockers();

        // A real current response prompt must reject the hidden Empty City ID;
        // rejection may not reveal, return morale, spend priority or block combat.
        f.RejectUnchanged(enemy, Typed(rootPrompt, EmptyCity));
        Assert.Equal(rootPrompt.PromptId, Assert.Single(f.Game.State.PendingPrompts).PromptId);
        Assert.False(f.Game.State.PendingDefense!.BlockedByResponse);
        f.AssertUnusedBlockers();
        f.AssertCostsRetained();

        var dawnDeclaration = f.Choose(Dawn);
        // With an empty graveyard the optional draw segment is unavailable:
        // the real declaration goes straight to the buff response, not an
        // invented one-choice "do not draw" step.
        Assert.Equal("response", Assert.Single(f.Game.State.PendingPrompts).Kind);
        Assert.DoesNotContain(f.Game.State.PendingActivations, item => item.SourceInstanceId == Dawn);
        var dawnEffect = Assert.Single(f.Game.State.EffectStack,
            item => item.SourceInstanceId == Dawn && item.Trigger == "reaction");
        var dawnId = dawnEffect.StackItemId;
        Assert.Equal("battle-until-dawn-buff", dawnEffect.Data["atomicFlow"]);
        Assert.Contains(rootId, dawnEffect.Targets);
        f.RejectUnchanged(enemy, dawnDeclaration);

        if (negateDawn)
        {
            f.PassUntil(game => ResponsePlayer(game) == owner);
            var opportunity = Assert.Single(f.Game.State.PendingPrompts);
            Assert.Equal("response", opportunity.Kind);
            Assert.Contains(OwnDefense, opportunity.ValidChoices);
            Assert.True(f.Game.State.PendingDefense!.SureHit);
            f.Choose(OwnDefense);
            var discardPrompt = Assert.Single(f.Game.State.PendingPrompts);
            Assert.Equal("stack-response-discard", discardPrompt.Continuation);
            Assert.Equal(dawnId, discardPrompt.StackItemId);
            Assert.Contains(Discard, discardPrompt.ValidChoices);
            var discardCommand = f.Choose(Discard);
            var negate = Assert.Single(f.Game.State.EffectStack,
                item => item.SourceInstanceId == OwnDefense && item.Trigger == "response-negate");
            Assert.Equal(new[] { dawnId }, negate.Targets);
            Assert.DoesNotContain(rootId, negate.Targets);
            Assert.Equal("无效堆叠中的效果", negate.Text);
            Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == Discard);
            f.RejectUnchanged(owner, discardCommand);
        }

        // The lower attack root may continue immediately when its response chain
        // unwinds. Do not invent another response window after Dawn; use the real
        // combat fact's pre-damage troops to prove the buff/negation distinction.
        f.FinishCombat();
        var combat = Assert.Single(f.Game.State.Events,
            entry => entry.Type == "combat" && entry.PlayerCombat?.CombatId == combatId);
        Assert.Equal(GuanYu, combat.PlayerCombat!.AttackerInstanceId);
        Assert.Equal(Target, combat.PlayerCombat.TargetInstanceId);
        Assert.Equal(6000, combat.PlayerCombat.AttackerTroops);
        Assert.Equal(negateDawn ? 2000 : 3000, combat.PlayerCombat.DefenderTroops);
        Assert.DoesNotContain(f.Game.State.Events, entry => entry.PlayerCombat is { } combatFact
            && combatFact.CombatId == combatId && combatFact.OutcomeCode == "blocked");
        Assert.Contains(f.Game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.FirstOrDefault()?.InstanceId == Dawn
            && entry.EffectResultStatus == (negateDawn ? "negated" : "resolved"));
        Assert.Single(f.Game.State.Players[enemy].Graveyard, card => card.InstanceId == Dawn);
        Assert.Equal(12, f.Game.State.Players[enemy].Library.Count);
        Assert.Single(f.Game.State.Players[enemy].Hand);
        if (negateDawn)
            Assert.Single(f.Game.State.Players[owner].Graveyard, card => card.InstanceId == OwnDefense);
        else
        {
            Assert.Equal(OwnDefense, f.Game.State.Players[owner].Field[1][0]?.InstanceId);
            Assert.True(f.Game.State.Players[owner].Field[1][0]!.Hidden);
            Assert.Contains(f.Game.State.Players[owner].Hand, card => card.InstanceId == Discard);
        }
        f.AssertUnusedBlockers();
        f.AssertCostsRetained();
        f.AssertClean();
        Assert.Null(f.Game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, f.Game.State.Phase);
        Assert.Equal(owner, f.Game.State.ActivePlayer);
        Assert.Equal(GuanYu, f.Attacker.InstanceId);
        Assert.True(f.Attacker.Tapped);
        Assert.Equal(1, f.Attacker.AttacksThisTurn);
        Assert.Null(f.Game.State.Players[enemy].Field[0][0]);
        Assert.Single(f.Game.State.Players[enemy].Graveyard, card => card.InstanceId == Target);
        f.AssertUnusedBlockers();
        f.AssertCostsRetained();
        f.RejectUnchanged(owner, attack);
        f.RejectUnchanged(owner, playGuanYu);
        if (restoreEveryCommand) Assert.True(f.Restores >= 16);
        f.RoundTripAlways();
    }

    private static int? ResponsePlayer(L12GameEngine game)
        => game.State.PendingPrompts.SingleOrDefault(prompt => prompt.Kind == "response")?.PlayerIndex;

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
            BaseTroops = d.Troops ?? 0, Troops = d.Troops ?? 0,
            DisasterLevel = d.DisasterLevel ?? 0, OwnerIndex = owner, SummonRound = -1,
        };
    }

    private sealed class Fixture
    {
        internal L12GameEngine Game;
        internal int Restores;
        internal int FactionRecoveryDeclines;
        private readonly int _owner;
        private readonly bool _restore;
        internal L12CardInstance Attacker => Game.State.Players[_owner].Field[0][0]!;
        internal L12CardInstance Defender => Game.State.Players[1 - _owner].Field[0][0]!;

        internal Fixture(int owner, bool restore)
        {
            _owner = owner; _restore = restore;
            var deck = Catalog.DeckAt(0);
            Game = new(Catalog, "guanyu-actual-charge-response", "SYNTHETIC", 161800 + owner,
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
            var defender = Game.State.Players[1 - owner];
            actor.TemporaryMorale = 20;
            actor.Hand.Add(Card("S01-0012", Sortie, owner));
            actor.Hand.Add(Card("S01-0106", GuanYu, owner));
            actor.Hand.Add(Card("S01-0003", Discard, owner));
            defender.Hand.Add(Card("S01-0003", "synthetic-enemy-discard", 1 - owner));
            actor.Morale.Add(new() { CardId = "S01-01C1", InstanceId = OwnMorale });
            defender.Morale.Add(new() { CardId = "S01-01C1", InstanceId = EnemyMorale });
            defender.Field[0][0] = Card("S01-0003", Target, 1 - owner);
            Cover(actor, 0, "S01-0016", OwnDefense);
            Cover(defender, 0, "S01-0120", EmptyCity);
            Cover(defender, 1, "S01-0020", Dawn);
            Cover(defender, 2, "S01-0016", EnemyDefense);
            Checkpoint();
        }

        private static void Cover(L12PlayerState player, int slot, string cardId, string id)
        {
            var card = Card(cardId, id, player.PlayerIndex);
            card.Hidden = true; card.SetRound = 0;
            player.Field[1][slot] = card;
        }

        internal void AssertHandPlayFees() => Assert.Equal(
            20 - (Catalog.Cards["S01-0012"].Cost ?? 0) - (Catalog.Cards["S01-0106"].Cost ?? 0),
            Game.State.Players[_owner].TemporaryMorale);

        internal void AssertCostsRetained()
        {
            AssertHandPlayFees();
            Assert.Empty(Game.State.Players[_owner].Morale);
            Assert.Single(Game.State.Players[_owner].MoraleDeck, morale => morale.InstanceId == OwnMorale);
            Assert.Single(Game.State.Players[1 - _owner].Morale, morale => morale.InstanceId == EnemyMorale);
        }

        internal void AssertUnusedBlockers()
        {
            var field = Game.State.Players[1 - _owner].Field[1];
            Assert.Equal(EmptyCity, field[0]?.InstanceId); Assert.True(field[0]!.Hidden);
            Assert.Equal(EnemyDefense, field[2]?.InstanceId); Assert.True(field[2]!.Hidden);
            Assert.DoesNotContain(Game.State.Players[1 - _owner].Resolving,
                card => card.InstanceId is EmptyCity or EnemyDefense);
            Assert.DoesNotContain(Game.State.Players[1 - _owner].Graveyard,
                card => card.InstanceId is EmptyCity or EnemyDefense);
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
            var choices = prompt.ValidChoices.Where(id => id.StartsWith("temporary-morale:", StringComparison.Ordinal))
                .Take(cost).ToList();
            Assert.Equal(cost, choices.Count);
            Assert.DoesNotContain(OwnMorale, choices);
            Send(prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: choices,
                ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
                SourceCardId: prompt.SourceCardId, Step: prompt.Step,
                CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller));
            Assert.False(Assert.Single(Game.State.Players[_owner].Morale,
                morale => morale.InstanceId == OwnMorale).Tapped);
        }

        internal void PassResponses()
        {
            for (var i = 0; Game.State.PendingPrompts.Count > 0; i++)
            {
                Assert.True(i < 80, "Response chain did not quiesce");
                Assert.Equal("response", Assert.Single(Game.State.PendingPrompts).Kind);
                Choose("pass");
            }
        }

        internal void PassUntil(Func<L12GameEngine, bool> condition)
        {
            for (var i = 0; !condition(Game); i++)
            {
                Assert.True(i < 80, "Expected real combat/response boundary was not reached");
                var prompt = Assert.Single(Game.State.PendingPrompts);
                if (prompt.Kind == "option" && prompt.Continuation == "pending-activation"
                    && prompt.PlayerIndex == _owner && prompt.SourceCardId == "S01-01C1"
                    && prompt.SourceInstanceId == $"faction-{_owner}")
                {
                    // Returning the last morale genuinely offers the Tian Ting
                    // faction's zero-morale optional trigger. Decline it publicly
                    // instead of removing its state or suppressing its trigger.
                    Assert.Contains("mode:none", prompt.ValidChoices);
                    Choose("mode:none");
                    FactionRecoveryDeclines++;
                    continue;
                }
                Assert.True(prompt.Kind == "response",
                    $"Boundary {prompt.Kind}/{prompt.Continuation}: {prompt.Text}; {string.Join(',', prompt.ValidChoices)}; masters={string.Join(',', Game.State.Players.Select(p => p.MasterId))}");
                Choose("pass");
            }
        }

        internal void FinishCombat()
        {
            for (var i = 0; Game.State.PendingDefense is not null || Game.State.PendingPrompts.Count > 0; i++)
            {
                Assert.True(i < 80, "Actual GuanYu combat did not finish");
                if (Game.State.PendingPrompts.Count > 0)
                {
                    Assert.Equal("response", Assert.Single(Game.State.PendingPrompts).Kind);
                    Choose("pass");
                }
                else
                {
                    Assert.Equal(L12CombatStage.DefenseChoice, Game.State.PendingDefense!.Stage);
                    Send(1 - _owner, new("resolveDefense", CardInstanceIds: []));
                }
            }
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
