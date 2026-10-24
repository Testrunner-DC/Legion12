using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Tests;

// Synthetic initial boards from the real catalog, not the players' original games.
// Every tested attack uses Handle.
// Desired-rule assertions deliberately stay RED until an independently approved fix.
public sealed class CurrentBugIntake20261023Tests(ITestOutputHelper output)
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-23T02:00:00+08:00");
    private const string Attacker = "intake-attacker", Defender = "intake-defender";

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [Trait("Bug", "BUG-20261022-25fcda37")]
    public void WrathWithOnlyReadyHannibalMustPermitTheOtherwiseLegalMasterAttack(int actor, bool restore)
    {
        var game = AttackBoard(actor, "S02-0516", rested: false, wrath: true);
        game = RestoreIfRequested(game, restore);
        var beforeInvalid = game.SerializeFullState();
        var forbidden = game.Handle(actor, new("attack", Attacker,
            Target: new L12AttackTarget("legion", Defender)));
        Assert.False(forbidden.Accepted);
        Assert.Contains("无法被进攻", forbidden.Error);
        Assert.Equal(beforeInvalid, game.SerializeFullState());

        var targets = game.SnapshotFor(actor).LegalAttackTargets;
        var previewAllowsMaster = targets.TryGetValue(Attacker, out var choices) && choices.Contains("master");
        var attack = game.Handle(actor, new("attack", Attacker, Target: new L12AttackTarget("master")));
        output.WriteLine($"BUG-25fcda37 actor={actor} restored={restore}: ready Hannibal rejected={forbidden.Accepted == false}; master preview={previewAllowsMaster}; master command={attack.Accepted}; error={attack.Error}");
        Assert.True(attack.Accepted,
            $"An unattackable Hannibal is not a mandatory legal target. previewMaster={previewAllowsMaster}; actual error={attack.Error}");
        Assert.True(previewAllowsMaster);
        Assert.Equal("master", game.State.PendingDefense?.Target.Type);
    }

    [Theory]
    [InlineData(0, false, "S01-0003", false)]
    [InlineData(1, true, "S02-0516", true)]
    [Trait("Bug", "BUG-20261022-25fcda37:positive-control")]
    public void WrathStillRequiresAnOrdinaryOrRestedHannibalLegionTarget(
        int actor, bool restore, string targetCard, bool rested)
    {
        var game = RestoreIfRequested(AttackBoard(actor, targetCard, rested, wrath: true), restore);
        var rejected = game.Handle(actor, new("attack", Attacker, Target: new L12AttackTarget("master")));
        Assert.False(rejected.Accepted);
        Assert.Contains("暴怒之罪", rejected.Error);
        Assert.False(game.State.Players[actor].Field[0][0]!.Tapped);
        var attack = game.Handle(actor, new("attack", Attacker,
            Target: new L12AttackTarget("legion", Defender)));
        Assert.True(attack.Accepted, attack.Error);
        output.WriteLine($"Wrath positive actor={actor} restored={restore}: legal {targetCard}/rested={rested} forces legion, and legion command accepted.");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void WrathReadyHannibalDoesNotHideAnotherLegalMandatoryLegion(int actor, bool restore)
    {
        var game = AttackBoard(actor, "S02-0516", rested: false, wrath: true);
        game.State.Players[1 - actor].Field[0][2] = Card("S01-0003", "intake-other-legal", 1 - actor);
        game = RestoreIfRequested(game, restore);
        var master = game.Handle(actor, new("attack", Attacker, Target: new L12AttackTarget("master")));
        Assert.False(master.Accepted);
        Assert.Contains("暴怒之罪", master.Error);
        var ordinary = game.Handle(actor, new("attack", Attacker,
            Target: new L12AttackTarget("legion", "intake-other-legal")));
        Assert.True(ordinary.Accepted, ordinary.Error);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, true, true)]
    public void WrathUsesTheSameAmakineTrialProtectionAsActualLegionAttacks(
        int actor, bool restore, bool trialRested)
    {
        var game = AttackBoard(actor, "S02-0604", rested: trialRested, wrath: true);
        var protector = Card("S02-0616", "intake-protecting-amakine", 1 - actor);
        protector.Tapped = true;
        // The ordinary front-row attacker cannot reach this rear protector.
        game.State.Players[1 - actor].Field[1][2] = protector;
        game = RestoreIfRequested(game, restore);
        if (!trialRested)
        {
            var protectedTarget = game.Handle(actor, new("attack", Attacker,
                Target: new L12AttackTarget("legion", Defender)));
            Assert.False(protectedTarget.Accepted);
            Assert.Contains("阿麦金", protectedTarget.Error);
        }
        var master = game.Handle(actor, new("attack", Attacker, Target: new L12AttackTarget("master")));
        Assert.Equal(!trialRested, master.Accepted);
        if (trialRested)
        {
            Assert.Contains("暴怒之罪", master.Error);
            var ordinary = game.Handle(actor, new("attack", Attacker,
                Target: new L12AttackTarget("legion", Defender)));
            Assert.True(ordinary.Accepted, ordinary.Error);
        }
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, true, true)]
    public void WrathPreservesRealRangeAndTauntTargetRestrictions(int actor, bool restore, bool taunt)
    {
        var game = AttackBoard(actor, "S01-0003", rested: false, wrath: true);
        var defender = game.State.Players[1 - actor];
        if (taunt)
            defender.Field[0][1] = Card("S02-0007", "intake-front-taunt", 1 - actor);
        else
        {
            defender.Field[1][0] = defender.Field[0][0];
            defender.Field[0][0] = null;
        }
        game = RestoreIfRequested(game, restore);
        var wrongTarget = game.Handle(actor, new("attack", Attacker,
            Target: new L12AttackTarget("legion", Defender)));
        Assert.False(wrongTarget.Accepted);
        Assert.Contains(taunt ? "挑衅" : "近战军团无法进攻对方后排", wrongTarget.Error);
        var master = game.Handle(actor, new("attack", Attacker, Target: new L12AttackTarget("master")));
        Assert.Equal(!taunt, master.Accepted);
        if (taunt)
        {
            var legal = game.Handle(actor, new("attack", Attacker,
                Target: new L12AttackTarget("legion", "intake-front-taunt")));
            Assert.True(legal.Accepted, legal.Error);
        }
    }

    [Theory]
    [InlineData(0, false, "master")]
    [InlineData(1, true, "legion")]
    [InlineData(0, true, "none")]
    public void WrathDoesNotExpandTargetSpecificSummonTurnPermissions(
        int actor, bool restore, string permission)
    {
        var game = AttackBoard(actor, "S01-0003", rested: false, wrath: true);
        var attacker = game.State.Players[actor].Field[0][0]!;
        // Synthetic recorded permission state, not a claim of recreating its granting card.
        attacker.SummonRound = game.State.Round;
        if (permission == "master") attacker.CanAttackMasterOnSummonUntilTurn = game.State.TurnSerial;
        if (permission == "legion") attacker.CanAttackLegionsOnSummonUntilTurn = game.State.TurnSerial;
        game = RestoreIfRequested(game, restore);
        var master = game.Handle(actor, new("attack", Attacker, Target: new L12AttackTarget("master")));
        Assert.False(master.Accepted);
        Assert.Contains(permission == "master" ? "暴怒之罪" : "刚登场", master.Error);
        var legion = game.Handle(actor, new("attack", Attacker,
            Target: new L12AttackTarget("legion", Defender)));
        Assert.Equal(permission == "legion", legion.Accepted);
        if (!legion.Accepted) Assert.Contains("刚登场", legion.Error);
    }

    public static IEnumerable<object[]> RealMasterOnlyEntryCases()
    {
        foreach (var cardId in new[] { "S02-0301", "ST04-09" })
        foreach (var actor in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
        foreach (var target in new[] { "ordinary", "ready-hannibal", "out-of-range", "none" })
            yield return [cardId, actor, restore, target];
    }

    [Theory]
    [MemberData(nameof(RealMasterOnlyEntryCases))]
    public void WrathSeparatesRealMasterOnlyEntryPermissionFromMandatoryLegionEligibility(
        string cardId, int actor, bool restore, string target)
    {
        var game = Create(actor);
        var player = game.State.Players[actor];
        var opponent = game.State.Players[1 - actor];
        if (target != "none")
            opponent.Field[target == "out-of-range" ? 1 : 0][0]
                = Card(target == "ready-hannibal" ? "S02-0516" : "S01-0003", Defender, 1 - actor);
        game.State.ActiveDisaster = Card("S02-DS05", "entry-wrath", actor);
        var source = Card(cardId, Attacker, actor);
        player.Hand.Add(source);
        for (var index = 0; index < source.Cost; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"entry-morale-{actor}-{index}", CardId = "S01-03C1",
            });
        var played = game.Handle(actor, new("playCard", source.InstanceId, Row: 0, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        for (var step = 0; game.State.PendingPrompts.Count > 0 && step < 8; step++)
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.Equal("response", prompt.Kind);
            var pass = game.Handle(prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(pass.Accepted, pass.Error);
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Equal(cardId, player.Field[0][0]!.CardId);
        Assert.Equal(game.State.Round, source.SummonRound);
        Assert.Equal(game.State.TurnSerial, source.CanAttackMasterOnSummonUntilTurn);
        Assert.NotEqual(game.State.TurnSerial, source.CanAttackLegionsOnSummonUntilTurn);
        Assert.False(source.HasCharge);

        game = RestoreIfRequested(game, restore);
        var before = game.SerializeFullState();
        var targets = game.SnapshotFor(actor).LegalAttackTargets;
        var choices = targets.GetValueOrDefault(Attacker) ?? [];
        Assert.DoesNotContain(Defender, choices);
        if (target != "none")
        {
            var legion = game.Handle(actor, new("attack", Attacker, Target: new L12AttackTarget("legion", Defender)));
            Assert.False(legion.Accepted);
            Assert.Contains("刚登场", legion.Error);
            Assert.Equal(before, game.SerializeFullState());
        }
        var master = game.Handle(actor, new("attack", Attacker, Target: new L12AttackTarget("master")));
        var mustAttackLegion = target == "ordinary";
        output.WriteLine($"real entry {cardId}, actor={actor}, restore={restore}, target={target}: master={master.Accepted}; error={master.Error}");
        Assert.Equal(!mustAttackLegion, master.Accepted);
        Assert.Equal(!mustAttackLegion, choices.Contains("master"));
        if (mustAttackLegion)
        {
            Assert.Contains("暴怒之罪", master.Error);
            Assert.Equal(before, game.SerializeFullState());
            Assert.False(game.State.Players[actor].Field[0][0]!.Tapped);
            Assert.Null(game.State.PendingDefense);
        }
        else Assert.Equal("master", game.State.PendingDefense?.Target.Type);
    }

    private static L12GameEngine AttackBoard(int actor, string targetCard, bool rested, bool wrath)
    {
        var game = Create(actor);
        game.State.Players[actor].Field[0][0] = Card("S01-0002", Attacker, actor);
        game.State.Players[1 - actor].Field[0][0] = Card(targetCard, Defender, 1 - actor);
        game.State.Players[1 - actor].Field[0][0]!.Tapped = rested;
        // Isolate the printed continuous restriction; this does not replay the disaster trigger.
        game.State.ActiveDisaster = wrath ? Card("S02-DS05", "intake-wrath", actor) : null;
        return game;
    }

    private static L12GameEngine RestoreIfRequested(L12GameEngine game, bool restore)
    {
        if (!restore) return game;
        var checkpoint = game.SerializeFullState();
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.Equal(checkpoint, restored.SerializeFullState());
        Assert.Equal(game.RandomState, restored.RandomState);
        return restored;
    }

    private static L12GameEngine Create(int actor)
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "Synthetic current Bug intake", MasterId = "S02-05M2",
            CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "synthetic-current-bug-intake", "SYNTHETIC", 102300 + actor,
            ["Synthetic A", "Synthetic B"], [deck, deck], skipPreparation: true,
            disasterMode: "none", autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2, utcNow: () => Now);
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear();
            player.Resolving.Clear(); player.Removed.Clear(); player.Morale.Clear();
            player.MoraleDeck.Clear(); player.UsedAbilities.Clear();
            player.Relic = null; player.ExtraRelics.Clear(); player.Hp = 8;
            for (var index = 0; index < 12; index++)
                player.Library.Add(Card("S01-0003", $"intake-library-{player.PlayerIndex}-{index}", player.PlayerIndex));
        }
        game.State.PendingPrompts.Clear(); game.State.PendingActivations.Clear();
        game.State.EffectStack.Clear(); game.State.DeferredEffectStack.Clear();
        game.State.PendingTriggerBatches.Clear(); game.State.PendingTriggerStackCandidates.Clear();
        game.State.SuspendedCombatContexts.Clear(); game.State.Events.Clear();
        game.State.AuthorityEvents.Clear(); game.State.Log.Clear();
        game.State.LastAction = null; game.State.ResponseWindow = null;
        game.State.PendingDefense = null; game.State.ActiveDisaster = null;
        game.State.ActivePlayer = actor; game.State.FirstPlayer = actor;
        game.State.Round = 3; game.State.TurnSerial = 6; game.State.Phase = L12Phase.Main;
        return game;
    }

    private static L12CardInstance Card(string cardId, string id, int owner)
    {
        var card = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = id, CardId = card.Id, Name = card.NameZh, CardType = card.CardType,
            Faction = card.Faction, ImageUrl = card.ImageUrl, Cost = card.Cost ?? 0,
            HasPrintedCost = card.Cost.HasValue, EffectText = card.Effect,
            Traits = [.. card.Traits], Profession = card.Profession, EffectiveProfession = card.Profession,
            BaseTroops = card.Troops ?? 0, Troops = card.Troops ?? 0,
            DisasterLevel = card.DisasterLevel ?? 0, TrialValue = card.TrialValue ?? 0,
            OwnerIndex = owner, SummonRound = -1,
        };
    }
}
