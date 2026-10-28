using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TimedTroopsReductionBonusAbsorptionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private const string TargetId = "absorption-target";
    private const string AuraId = "absorption-hannibal";
    private const string ScarabId = "absorption-scarab";
    private const string DiscardId = "absorption-discard";

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [Trait("Bug", "BUG-20261028-cebc6c9c")]
    public void RealScarabReductionAbsorbsHannibalBonusBeforeAuraLeaves(int targetOwner, bool restore)
    {
        var actor = 1 - targetOwner;
        var game = Create(actor);
        Assert.Equal(2000, Find(game, targetOwner, TargetId).BaseTroops);
        AssertTroops(game, targetOwner, 3000);
        game = RestoreIfRequested(game, restore);

        game = Send(game, actor, new L12Command("activateAbility", ScarabId, Ability: "scarabDebuff"), restore);
        var discard = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("hand-card", discard.Kind);
        game = Resolve(game, discard, DiscardId, null, restore);
        var targets = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(TargetId, targets.ValidChoices);
        game = Resolve(game, targets, null, [TargetId], restore);
        for (var guard = 0; game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } response; guard++)
        {
            Assert.True(guard < 12, "Scarab response did not finish within the bounded sequence.");
            game = Resolve(game, response, "pass", null, restore);
        }
        Assert.Empty(game.State.PendingPrompts);
        AssertTroops(game, targetOwner, 2000);
        Assert.Contains(game.State.Players[actor].Graveyard, card => card.InstanceId == DiscardId);

        // Real sandbox departure, not a fabricated Hannibal death effect or the player's original match.
        var result = game.HandleGm(new L12GmCommand("returnCardToHand", targetOwner, CardInstanceId: AuraId));
        Assert.True(result.Accepted, result.Error);
        game = RestoreIfRequested(game, restore);
        Assert.Contains(game.State.Players[targetOwner].Hand, card => card.InstanceId == AuraId);
        AssertTroops(game, targetOwner, 2000);
        AssertTroops(game, targetOwner, 2000); // Repeated projection must not consume the reduction twice.
    }

    private static L12GameEngine Create(int actor, bool protocol = true)
    {
        var game = new L12GameEngine(Catalog, "troops-absorption", "ABSORB", 102800 + actor,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, disasterMode: "none", stateFormatVersion: 2);
        Assert.Equal(2, game.State.StateFormatVersion);
        Assert.True(game.RandomState.HasValue);
        // Select the protocol era explicitly for both new and legacy regressions.
        var checkpoint = JsonNode.Parse(game.SerializeFullState())!.AsObject();
        checkpoint[nameof(L12GameState.TimedTroopsReductionAbsorptionProtocolEnabled)] = protocol;
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint.ToJsonString(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3]; player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Relic = null; player.ExtraRelics.Clear();
        }
        game.State.ActivePlayer = actor; game.State.FirstPlayer = actor;
        game.State.Round = 3; game.State.TurnSerial = 7; game.State.Phase = L12Phase.Main;
        game.State.Players[1 - actor].Field[0][0] = Card("S02-0515", TargetId, 1 - actor);
        game.State.Players[1 - actor].Field[0][1] = Card("S02-0516", AuraId, 1 - actor);
        game.State.Players[actor].Relic = Card("S02-0205", ScarabId, actor);
        game.State.Players[actor].Hand.Add(Card("S01-0002", DiscardId, actor));
        game.State.Revision++;
        return game;
    }

    [Fact]
    public void MultipleReductionsKeepTheirOwnTimedAndContinuousSharesSeparateFromDamage()
    {
        var game = Create(0); _ = game.SnapshotFor(0);
        var card = Find(game, 1, TargetId);
        Add(game, card, 1000, 11, "later");
        Add(game, card, 1000, 10, "sooner");
        Add(game, card, -1000, 12, "first");
        Add(game, card, -1500, 12, "second");
        var first = card.TimedModifiers.Single(entry => entry.Source == "first");
        var second = card.TimedModifiers.Single(entry => entry.Source == "second");
        Assert.Equal("sooner", card.TimedModifiers.Single(entry => entry.TroopsLayerId == first.TroopsReduction!.Absorptions[0].LayerId).Source);
        Assert.Equal(new[] { "timed", "continuous" }, second.TroopsReduction!.Absorptions.Select(entry => entry.Kind));
        L12DerivedStats.ApplyTroopsDamage(card, 250);
        var before = card.Troops;
        Invoke(game, "CancelTroopsReduction", card, first);
        card.TimedModifiers.Remove(first);
        Assert.Equal(before + 1000, card.Troops);
        Assert.Equal(500, Assert.Single(card.ContinuousTroopsBonusLayers).Value.ReductionConsumed);
        Assert.Equal(250, Assert.Single(card.ContinuousTroopsBonusLayers).Value.Consumed);
        Invoke(game, "CancelTroopsReduction", card, second);
        card.TimedModifiers.Remove(second);
        Assert.Equal(before + 2500, card.Troops);
        Assert.Equal(250, Assert.Single(card.ContinuousTroopsBonusLayers).Value.Consumed);
        Assert.Equal(0, Assert.Single(card.ContinuousTroopsBonusLayers).Value.ReductionConsumed);
        game.State.Revision++;
        _ = RestoreIfRequested(game, true);
    }

    [Fact]
    public void AnExpiredGrantCannotBeResurrectedByCancellingItsHistoricalShare()
    {
        var game = Create(0); _ = game.SnapshotFor(0);
        var card = Find(game, 1, TargetId);
        Add(game, card, -1000, 12, "negative");
        var negative = Assert.Single(card.TimedModifiers);
        var share = Assert.Single(negative.TroopsReduction!.Absorptions);
        var oldId = share.LayerId;
        L12DerivedStats.ApplyContinuousModifiers(card, new Dictionary<string, int>(), 0, 7, true);
        Assert.Equal(1000, share.Amount); Assert.Equal(0, share.RestorableAmount);
        L12DerivedStats.ApplyContinuousModifiers(card,
            new Dictionary<string, int> { [share.SourceKey!] = 1000 }, 0, 7, true);
        Assert.NotEqual(oldId, Assert.Single(card.ContinuousTroopsBonusLayers).Value.GrantId);
        var before = card.Troops;
        Invoke(game, "CancelTroopsReduction", card, negative);
        Assert.Equal(before, card.Troops);
    }

    [Fact]
    public void SameGrantShrinkRetiresDamageThenNewestRestorableShareWithoutStealingOlderShares()
    {
        var game = Create(0); _ = game.SnapshotFor(0);
        var card = Find(game, 1, TargetId);
        var key = Assert.Single(card.ContinuousTroopsBonusLayers).Key;
        L12DerivedStats.ApplyContinuousModifiers(card, new Dictionary<string, int> { [key] = 3000 }, 0, 7, true);
        Add(game, card, -500, 12, "older");
        Add(game, card, -1000, 12, "newer");
        L12DerivedStats.ApplyTroopsDamage(card, 500);
        var older = card.TimedModifiers.Single(entry => entry.Source == "older");
        var newer = card.TimedModifiers.Single(entry => entry.Source == "newer");
        L12DerivedStats.ApplyContinuousModifiers(card, new Dictionary<string, int> { [key] = 2000 }, 0, 7, true);
        Assert.Equal(1000, L12DerivedStats.RemainingTroopsBonus(Assert.Single(card.ContinuousTroopsBonusLayers).Value));
        Assert.Equal(0, Assert.Single(card.ContinuousTroopsBonusLayers).Value.Consumed);
        Assert.Equal(500, Assert.Single(older.TroopsReduction!.Absorptions).RestorableAmount);
        Assert.Equal(500, Assert.Single(newer.TroopsReduction!.Absorptions).RestorableAmount);
        Assert.Equal(1000, Assert.Single(newer.TroopsReduction.Absorptions).Amount);
        var before = card.Troops;
        Invoke(game, "CancelTroopsReduction", card, newer);
        card.TimedModifiers.Remove(newer);
        Assert.Equal(before + 500, card.Troops);
        Assert.Equal(500, Assert.Single(older.TroopsReduction.Absorptions).RestorableAmount);
        Assert.Equal(500, Assert.Single(card.ContinuousTroopsBonusLayers).Value.ReductionConsumed);
    }

    [Fact]
    public void TurnRebuildKeepsTheFullUnexpiredPenaltyThenReallocatesWithoutApplyingItTwice()
    {
        var game = Create(0); _ = game.SnapshotFor(0);
        var card = Find(game, 1, TargetId);
        Add(game, card, -1000, 12, "negative");
        L12DerivedStats.ApplyTroopsDamage(card, 500);
        L12DerivedStats.ResetForCompletedTurn(card, 7, 0);
        Assert.Equal(2000, card.Troops);
        Invoke(game, "RebuildTimedTroopsAbsorptions", card);
        Assert.Equal(2000, card.Troops);
        Assert.Equal(-1000, Assert.Single(card.TimedModifiers).TroopsDelta);
        Assert.Equal(1000, Assert.Single(card.TimedModifiers).TroopsReduction!.Absorptions.Sum(entry => entry.Amount));
        L12DerivedStats.ApplyContinuousModifiers(card, new Dictionary<string, int>(), 0, 8, true);
        Assert.Equal(2000, card.Troops);
        L12DerivedStats.ResetForCompletedTurn(card, 8, 1);
        Invoke(game, "RebuildTimedTroopsAbsorptions", card);
        Assert.Equal(1000, card.Troops);
        Assert.Empty(Assert.Single(card.TimedModifiers).TroopsReduction!.Absorptions);
    }

    private static void Add(L12GameEngine game, L12CardInstance card, int amount, int expiry, string source)
        => Invoke(game, "AddTimedModifier", card, amount, 0, expiry, source, null);

    private static object? Invoke(L12GameEngine game, string name, params object?[] arguments)
        => typeof(L12GameEngine).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, arguments);

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)]
    [InlineData(0, true)] [InlineData(1, true)]
    public void RealMerlinHelenCancellationReturnsOnlyTheStillLiveOriginalAura(int actor, bool removeAura)
    {
        var owner = 1 - actor;
        var game = Create(actor);
        game.State.Players[actor].Relic = null;
        game.State.Players[actor].Field[0][0] = Card("S02-0603", "absorption-merlin", actor);
        game.State.Players[actor].SpecialZones.Runes = 1;
        game.State.Players[owner].Hand.Add(Card("S01-0002", "absorption-helen-payment", owner));
        game.State.Revision++;
        game = Send(game, actor, new("activateAbility", "absorption-merlin", Ability: "merlinRune"), true);
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), "mode:debuff", null, true);
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), TargetId, null, true);
        for (var count = 0; game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } response; count++)
        {
            Assert.True(count < 12);
            game = Resolve(game, response, "pass", null, true);
        }
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("effect-lethal-replacement", prompt.Continuation);
        Assert.Equal("2", prompt.Data["lethalTroopsCancellation:version"]);
        var target = Find(game, owner, TargetId);
        Assert.Equal(0, target.Troops);
        Assert.Equal(1000, Assert.Single(Assert.Single(target.TimedModifiers).TroopsReduction!.Absorptions).Amount);
        if (removeAura)
        {
            var removed = game.HandleGm(new("returnCardToHand", owner, CardInstanceId: AuraId));
            Assert.True(removed.Accepted, removed.Error);
            game = RestoreIfRequested(game, true);
            prompt = Assert.Single(game.State.PendingPrompts);
        }
        game = Resolve(game, prompt, "absorption-helen-payment", null, true);
        target = Find(game, owner, TargetId);
        Assert.Equal(removeAura ? 2000 : 3000, target.CurrentTroops);
        Assert.Empty(target.TimedModifiers);
        Assert.Contains(game.State.Players[owner].Graveyard, card => card.InstanceId == "absorption-helen-payment");
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "leave" && entry.Cards.Any(card => card.InstanceId == TargetId));
    }

    [Theory]
    [InlineData("target")] [InlineData("duplicate-id")] [InlineData("over-amount")]
    [InlineData("over-restorable")] [InlineData("cross-source")] [InlineData("flag")]
    [InlineData("counter")]
    public void MalformedReductionCheckpointIsRejectedBeforeItCanCreateOrStealTroops(string corruption)
    {
        var game = Create(0); _ = game.SnapshotFor(0);
        Add(game, Find(game, 1, TargetId), -500, 12, "first");
        Add(game, Find(game, 1, TargetId), -250, 12, "second");
        game.State.Revision++;
        var json = JsonNode.Parse(game.SerializeFullState())!.AsObject();
        var card = json["Players"]![1]!["Field"]![0]![0]!;
        var modifiers = card["TimedModifiers"]!.AsArray();
        var reduction = modifiers[0]!["TroopsReduction"]!;
        var share = reduction["Absorptions"]![0]!;
        switch (corruption)
        {
            case "target": reduction["TargetInstanceId"] = "another-card"; break;
            case "duplicate-id": modifiers[1]!["TroopsLayerId"] = modifiers[0]!["TroopsLayerId"]!.GetValue<long>(); break;
            case "over-amount": share["Amount"] = 501; break;
            case "over-restorable": share["RestorableAmount"] = 501; break;
            case "cross-source": share["SourceKey"] = "another-source"; break;
            case "flag": Assert.True(json.Remove(nameof(L12GameState.TimedTroopsReductionAbsorptionProtocolEnabled))); break;
            case "counter": card["ContinuousTroopsBonusLayers"]!.AsObject().First().Value!["ReductionConsumed"] = 500; break;
        }
        Assert.Throws<InvalidDataException>(() => L12GameEngine.RestoreCheckpoint(Catalog, json.ToJsonString(),
            game.RandomState!.Value, game.CardFactSignalSequence));
    }

    [Fact]
    public void EveryPublicViewAndEventOmitsTheInternalReductionLedgerWithoutMutatingAuthority()
    {
        var game = Create(0); _ = game.SnapshotFor(0);
        var card = Find(game, 1, TargetId);
        Add(game, card, -500, 12, "private-ledger");
        Invoke(game, "AddEvent", "effect", (int?)1, "公开兵力变化", new L12CardInstance[] { card });
        game.State.Revision++;
        var before = game.SerializeFullState();
        foreach (var snapshot in new[] { game.SnapshotFor(0), game.SnapshotFor(1), game.SnapshotForSpectator(),
                     game.SnapshotForReferee(), game.SnapshotForGm(0) })
        {
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.DoesNotContain("troopsReduction", json);
            Assert.DoesNotContain("troopsLayerId", json);
            Assert.DoesNotContain("grantId", json);
            Assert.DoesNotContain("restorableAmount", json);
        }
        Assert.Equal(before, game.SerializeFullState());
    }

    [Theory]
    [InlineData(1000, 0, "continuous")]
    [InlineData(4000, 1000, "attack")]
    public void RealAttackSeparatesSetBaseDifferenceFromActualPositiveBonus(int printedTroops, int attackBonus, string expectedKind)
    {
        var game = Create(0);
        var definition = Catalog.Cards["S01-0409"];
        var attacker = new L12CardInstance
        {
            InstanceId = "ledger-attacker", CardId = definition.Id, Name = definition.NameZh,
            CardType = "legion", Faction = definition.Faction, Traits = [.. definition.Traits],
            Profession = definition.Profession, OwnerIndex = 0, BaseTroops = printedTroops,
            Troops = printedTroops, SummonRound = -1,
            AttackOnlyTroopsBonus = attackBonus, AttackOnlyTroopsBonusUntilTurn = 7,
        };
        game.State.Players[0].Field[1][0] = attacker;
        game.State.Players[0].Field[1][1] = Card("S02-0516", "attacker-aura", 0);
        game.State.Players[1].Field[0][0] = new L12CardInstance
        {
            InstanceId = TargetId, CardId = "S01-0002", Name = "基础军团目标", CardType = "legion",
            Faction = Catalog.Cards["S01-0002"].Faction, OwnerIndex = 1,
            BaseTroops = 6000, Troops = 6000, SummonRound = -1,
        };
        game.State.Players[1].Field[0][1] = null;
        game.State.Revision++;
        game = Send(game, 0, new("attack", attacker.InstanceId, Target: new("legion", TargetId)), true);
        attacker = Find(game, 0, "ledger-attacker");
        var pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal(attackBonus, pending.ActualAttackerTroopsBonus);
        Assert.Equal(2000 - printedTroops + attackBonus, pending.TemporaryAttackerTroopsBonus);
        Add(game, attacker, -500, 12, "during-attack");
        var modifier = Assert.Single(attacker.TimedModifiers);
        Assert.Equal(expectedKind, Assert.Single(modifier.TroopsReduction!.Absorptions).Kind);
        if (attackBonus > 0) Assert.Equal(500, pending.ActualAttackerTroopsBonus);
        else Assert.Equal(0, pending.ActualAttackerTroopsBonus);
        game.State.Revision++;
        game = RestoreIfRequested(game, true);
        for (var count = 0; game.State.PendingPrompts.FirstOrDefault() is { } prompt; count++)
        {
            Assert.True(count < 24);
            Assert.Equal("response", prompt.Kind);
            game = Resolve(game, prompt, "pass", null, true);
        }
        Assert.Null(game.State.PendingDefense);
        attacker = Find(game, 0, "ledger-attacker");
        Assert.Equal(printedTroops + 1000 - (expectedKind == "continuous" ? 500 : 0), attacker.Troops);
        Assert.Equal(expectedKind == "attack" ? 0 : 500,
            Assert.Single(Assert.Single(attacker.TimedModifiers).TroopsReduction!.Absorptions).RestorableAmount);
    }

    [Fact]
    public void GeneratedChildWithZeroBonusUsesAndRestoresOnlyTheExactSuspendedParentLayer()
    {
        var game = Create(0);
        var attacker = Card("S01-0001", "piercing-ledger-attacker", 0);
        attacker.AttackOnlyTroopsBonus = 1000; attacker.AttackOnlyTroopsBonusUntilTurn = 7;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Revision++;
        game = Send(game, 0, new("attack", attacker.InstanceId, Target: new("legion", TargetId)), true);
        attacker = Find(game, 0, "piercing-ledger-attacker");
        var parent = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal(1000, parent.ActualAttackerTroopsBonus);
        // Bounded timeline-boundary fixture: enter the actual generated-attack
        // routine after removing only the already-open response work. This is not
        // represented as a played piercing card or the reported private game.
        game.State.PendingPrompts.Clear(); game.State.EffectStack.Clear(); game.State.ResponseWindow = null;
        Invoke(game, "BeginPiercingAttack", 0, attacker);
        var child = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.NotEqual(parent.CombatId, child.CombatId);
        Assert.Equal(0, child.ActualAttackerTroopsBonus);
        Assert.Same(parent, Assert.Single(game.State.SuspendedCombatContexts));
        Add(game, attacker, -500, 12, "parent-half");
        var negative = Assert.Single(attacker.TimedModifiers);
        Assert.Equal(parent.CombatId, Assert.Single(negative.TroopsReduction!.Absorptions).CombatId);
        Assert.Equal(500, parent.ActualAttackerTroopsBonus);
        Assert.Equal(0, child.ActualAttackerTroopsBonus);
        game.State.Revision++;
        game = RestoreIfRequested(game, true);
        attacker = Find(game, 0, "piercing-ledger-attacker");
        parent = Assert.Single(game.State.SuspendedCombatContexts);
        child = game.State.PendingDefense!;
        negative = Assert.Single(attacker.TimedModifiers);
        Invoke(game, "RevertPendingCombatTroopsModifiers", child, attacker);
        Invoke(game, "FinishCurrentCombatContext");
        var before = attacker.Troops;
        Invoke(game, "CancelTroopsReduction", attacker, negative);
        attacker.TimedModifiers.Remove(negative);
        Assert.Equal(before + 500, attacker.Troops);
        Assert.Equal(1000, parent.ActualAttackerTroopsBonus);
        Add(game, attacker, -1000, 12, "parent-all");
        negative = Assert.Single(attacker.TimedModifiers);
        Invoke(game, "RevertPendingCombatTroopsModifiers", parent, attacker);
        Invoke(game, "FinishCurrentCombatContext");
        before = attacker.Troops;
        Invoke(game, "CancelTroopsReduction", attacker, negative);
        Assert.Equal(before, attacker.Troops);
        Assert.Equal(0, Assert.Single(negative.TroopsReduction!.Absorptions).RestorableAmount);
    }

    [Fact]
    public void PromotionClonesTheLedgerAndPrivateReturnClearsOnlyTheDepartingInstance()
    {
        var game = Create(0); _ = game.SnapshotFor(0);
        var foundation = Find(game, 1, TargetId);
        Add(game, foundation, 1000, 12, "positive");
        Add(game, foundation, -1500, 12, "negative");
        var clone = foundation.Clone();
        var originalNegative = foundation.TimedModifiers.Single(entry => entry.TroopsDelta < 0);
        var cloneNegative = clone.TimedModifiers.Single(entry => entry.TroopsDelta < 0);
        Assert.NotSame(originalNegative.TroopsReduction, cloneNegative.TroopsReduction);
        Assert.NotSame(originalNegative.TroopsReduction!.Absorptions[0], cloneNegative.TroopsReduction!.Absorptions[0]);
        cloneNegative.TroopsReduction.Absorptions[0].RestorableAmount = 0;
        Assert.Equal(1000, originalNegative.TroopsReduction.Absorptions[0].RestorableAmount);
        var promoted = Card("S02-0501", "ledger-promoted", 1);
        L12S2ZoneOps.InheritPromotionState(foundation, promoted);
        Assert.Equal(promoted.BaseTroops + 500, promoted.Troops);
        Assert.Equal(promoted.InstanceId, promoted.TimedModifiers.Single(entry => entry.TroopsDelta < 0).TroopsReduction!.TargetInstanceId);
        Assert.NotSame(originalNegative.TroopsReduction, promoted.TimedModifiers.Single(entry => entry.TroopsDelta < 0).TroopsReduction);
        var leave = game.HandleGm(new("returnCardToHand", 1, CardInstanceId: TargetId));
        Assert.True(leave.Accepted, leave.Error);
        var returned = Assert.Single(game.State.Players[1].Hand, card => card.InstanceId == TargetId);
        Assert.Equal(0, returned.TroopsLayerSequence);
        Assert.Empty(returned.TimedModifiers);
        Assert.Empty(returned.ContinuousTroopsBonusLayers);
        Assert.NotNull(promoted.TimedModifiers.Single(entry => entry.TroopsDelta < 0).TroopsReduction);
    }

    [Fact]
    public void LegacyV2KeepsTheOldNegativeLayerAlgorithmAndSerializedShape()
    {
        var game = Create(0, protocol: false); _ = game.SnapshotFor(0);
        var card = Find(game, 1, TargetId);
        Add(game, card, -1000, 12, "legacy-negative");
        game.State.Revision++;
        Assert.Equal(2000, card.Troops);
        var result = game.HandleGm(new("returnCardToHand", 1, CardInstanceId: AuraId));
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(1000, Find(game, 1, TargetId).Troops);
        var state = game.SerializeFullState();
        Assert.DoesNotContain(nameof(L12GameState.TimedTroopsReductionAbsorptionProtocolEnabled), state);
        Assert.DoesNotContain(nameof(L12TimedModifier.TroopsLayerId), state);
        Assert.DoesNotContain(nameof(L12TimedModifier.TroopsReduction), state);
        Assert.DoesNotContain(nameof(L12TroopsBonusLayer.GrantId), state);
        var restored = RestoreIfRequested(game, true);
        Assert.False(restored.State.TimedTroopsReductionAbsorptionProtocolEnabled);
        Assert.Equal(state, restored.SerializeFullState());
    }

    [Fact]
    public void CancellingACompoundLethalModifierKeepsItsCostDimensionAndOtherLayers()
    {
        var game = Create(0); _ = game.SnapshotFor(0);
        var card = Find(game, 1, TargetId);
        Invoke(game, "AddTimedModifier", card, -3000, -2, 12, "compound", null);
        Assert.Equal(0, card.Troops); Assert.Equal(-2, card.CostModifier);
        Assert.True(Assert.IsType<bool>(Invoke(game, "TryCancelPendingLethalTroopsEffectContribution", card, "因兵力不高于0阵亡")));
        Assert.Equal(3000, card.Troops); Assert.Equal(-2, card.CostModifier);
        var remainder = Assert.Single(card.TimedModifiers);
        Assert.Equal(0, remainder.TroopsDelta); Assert.Equal(-2, remainder.CostDelta);
        Assert.Null(remainder.TroopsReduction);
        Assert.Equal(0, Assert.Single(card.ContinuousTroopsBonusLayers).Value.ReductionConsumed);
        game.State.Revision++;
        _ = RestoreIfRequested(game, true);
    }

    [Fact]
    public void CorruptV2CancellationFingerprintCannotConsumeThePromptOrHelenPayment()
    {
        var game = Create(0);
        game.State.Players[0].Relic = null;
        game.State.Players[0].Field[0][0] = Card("S02-0603", "recipe-merlin", 0);
        game.State.Players[0].SpecialZones.Runes = 1;
        game.State.Players[1].Hand.Add(Card("S01-0002", "recipe-payment", 1));
        game.State.Revision++;
        game = Send(game, 0, new("activateAbility", "recipe-merlin", Ability: "merlinRune"), true);
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), "mode:debuff", null, true);
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), TargetId, null, true);
        for (var count = 0; game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } response; count++)
        {
            Assert.True(count < 12); game = Resolve(game, response, "pass", null, true);
        }
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("2", prompt.Data["lethalTroopsCancellation:version"]);
        prompt.Data["lethalTroopsCancellation:fingerprint"] = new string('F', 64);
        game.State.Revision++;
        game = RestoreIfRequested(game, true);
        prompt = Assert.Single(game.State.PendingPrompts);
        var before = game.SerializeFullState(); var hash = game.ComputeStateHash();
        Assert.Throws<InvalidDataException>(() => game.Handle(1, new("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "recipe-payment", ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
            SourceCardId: prompt.SourceCardId, Step: prompt.Step, CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller)));
        Assert.Equal(before, game.SerializeFullState()); Assert.Equal(hash, game.ComputeStateHash());
        Assert.Same(prompt, Assert.Single(game.State.PendingPrompts));
        Assert.Contains(game.State.Players[1].Hand, card => card.InstanceId == "recipe-payment");
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)]
    [InlineData(0, true)] [InlineData(1, true)]
    public void NewTurnRebuildUsesTheNewActivePlayerBeforeAbsorbingConditionalBonuses(int actor, bool extraTurn)
    {
        var owner = 1 - actor;
        var game = Create(actor);
        game.State.Players[owner].Field[0][0] = Card("S01-0107", TargetId, owner);
        game.State.Players[owner].Field[0][1] = null;
        game.State.Revision++;
        _ = game.SnapshotFor(actor);
        var card = Find(game, owner, TargetId);
        Assert.Equal(5000, card.Troops);
        Add(game, card, -1000, 12, "cross-turn-negative");
        Assert.Equal(4000, card.Troops);
        if (extraTurn) game.State.ExtraTurnsForPlayer = actor;
        game.State.Revision++;
        game = Send(game, actor, new("endTurn"), true);
        card = Find(game, owner, TargetId);
        Assert.Equal(extraTurn ? actor : owner, game.State.ActivePlayer);
        Assert.Equal(extraTurn ? 4000 : 3000, card.Troops);
        Assert.Equal(extraTurn ? 1000 : 0, Assert.Single(card.TimedModifiers).TroopsReduction!.Absorptions.Sum(entry => entry.Amount));
        if (extraTurn)
        {
            game = Send(game, actor, new("endTurn"), true);
            card = Find(game, owner, TargetId);
            Assert.Equal(owner, game.State.ActivePlayer);
            Assert.Equal(3000, card.Troops);
            Assert.Empty(Assert.Single(card.TimedModifiers).TroopsReduction!.Absorptions);
        }
    }

    [Theory]
    [InlineData("attacker")] [InlineData("combat")]
    [InlineData("remaining")] [InlineData("grant")] [InlineData("duplicate-combat")]
    public void RestoredAttackSharesCannotCrossCombatOrAttackerBoundaries(string corruption)
    {
        var game = Create(0);
        var attacker = Card("S01-0001", "restored-attack-owner", 0);
        attacker.AttackOnlyTroopsBonus = 1000; attacker.AttackOnlyTroopsBonusUntilTurn = 7;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Revision++;
        game = Send(game, 0, new("attack", attacker.InstanceId, Target: new("legion", TargetId)), true);
        attacker = Find(game, 0, attacker.InstanceId);
        Add(game, attacker, -500, 12, "attack-negative");
        game.State.Revision++;
        var json = JsonNode.Parse(game.SerializeFullState())!.AsObject();
        var pending = json["PendingDefense"]!;
        var share = json["Players"]![0]!["Field"]![0]![0]!["TimedModifiers"]![0]!["TroopsReduction"]!["Absorptions"]![0]!;
        switch (corruption)
        {
            case "attacker": share["AttackerInstanceId"] = "another-attacker"; break;
            case "combat": share["CombatId"] = "another-combat"; break;
            case "remaining": pending["ActualAttackerTroopsBonus"] = 1000; break;
            case "grant": pending.AsObject().Remove("GrantedAttackerTroopsBonus"); break;
            case "duplicate-combat": json["SuspendedCombatContexts"]!.AsArray().Add(pending.DeepClone()); break;
        }
        Assert.Throws<InvalidDataException>(() => L12GameEngine.RestoreCheckpoint(Catalog, json.ToJsonString(),
            game.RandomState!.Value, game.CardFactSignalSequence));
    }

    [Fact]
    public void LegalSupportDoesNotConsumeOrRefundAnotherCardsImmediateAttackBonus()
    {
        var game = Create(0);
        var support = Find(game, 1, TargetId);
        support.AttackOnlyTroopsBonus = 2000; support.AttackOnlyTroopsBonusUntilTurn = 7;
        game.State.Players[1].Field[1][0] = support;
        game.State.Players[1].Field[1][1] = game.State.Players[1].Field[0][1];
        game.State.Players[1].Field[0][1] = null;
        game.State.Players[1].Field[0][0] = Card("S01-0002", "supported-front", 1);
        var attacker = Card("S01-0001", "support-attacker", 0);
        attacker.AttackOnlyTroopsBonus = 1000; attacker.AttackOnlyTroopsBonusUntilTurn = 7;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Revision++;
        game = Send(game, 0, new("attack", attacker.InstanceId, Target: new("legion", "supported-front")), true);
        var pending = game.State.PendingDefense!;
        attacker = Find(game, 0, attacker.InstanceId); support = Find(game, 1, TargetId);
        var valid = Assert.IsType<CommandResult>(Invoke(game, "ValidateDefenseChoice", 1, pending, attacker,
            Array.Empty<string>(), new string[] { TargetId }));
        Assert.True(valid.Accepted, valid.Error);
        pending.DeclaredSupportIds.Add(TargetId);
        Add(game, support, -500, 12, "support-negative");
        var negative = Assert.Single(support.TimedModifiers);
        Assert.Equal("continuous", Assert.Single(negative.TroopsReduction!.Absorptions).Kind);
        Assert.Equal(1000, pending.ActualAttackerTroopsBonus);
        Assert.Equal(1000, pending.TemporaryAttackerTroopsBonus);
        Invoke(game, "CancelTroopsReduction", support, negative);
        support.TimedModifiers.Remove(negative);
        Assert.Equal(1000, pending.ActualAttackerTroopsBonus);
        Assert.Equal(1000, pending.TemporaryAttackerTroopsBonus);
        game.State.Revision++;
        _ = RestoreIfRequested(game, true);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)]
    [InlineData(0, true)] [InlineData(1, true)]
    public void NearestOwnerTurnExpiryFollowsTheAlreadyScheduledExtraTurn(int actor, bool extraForOpponent)
    {
        var game = Create(actor);
        game.State.Players[1 - actor].Field[0][1] = null;
        game.State.Revision++; _ = game.SnapshotFor(actor);
        var card = Find(game, 1 - actor, TargetId);
        Invoke(game, "AddTimedModifier", card, 1000, 0, 9, "current-owner-end",
            new L12OwnerTurnEndExpiry { Controller = actor, GrantedTurnSerial = 7 });
        Invoke(game, "AddTimedModifier", card, 1000, 0, 8, "opponent-owner-end",
            new L12OwnerTurnEndExpiry { Controller = 1 - actor, GrantedTurnSerial = 7 });
        game.State.ExtraTurnsForPlayer = extraForOpponent ? 1 - actor : actor;
        Add(game, card, -500, 12, "nearest-expiry-negative");
        var share = Assert.Single(card.TimedModifiers.Single(entry => entry.TroopsDelta < 0).TroopsReduction!.Absorptions);
        var source = card.TimedModifiers.Single(entry => entry.TroopsLayerId == share.LayerId);
        Assert.Equal(extraForOpponent ? "opponent-owner-end" : "current-owner-end", source.Source);
        Assert.Equal(3500, card.Troops);
        game.State.Revision++;
        _ = RestoreIfRequested(game, true);
    }

    [Fact]
    public void SharedLiveAndReplayEventProjectionClonesOnlyTrackedSnapshotsIncludingAttachments()
    {
        var game = Create(0); _ = game.SnapshotFor(0);
        var tracked = Find(game, 1, TargetId);
        Add(game, tracked, -500, 12, "tracked-event");
        var ordinary = Card("S01-0002", "untracked-event", 1);
        var attachedHost = Card("S01-0002", "attached-host", 1);
        attachedHost.AttachedCards.Add(tracked.Clone());
        var eventCards = new[] { tracked.Clone(), ordinary.Clone(), attachedHost.Clone() };
        var actionEvent = new L12ActionEvent(900, "effect", 1, "公开变化", eventCards);
        var before = JsonSerializer.Serialize(actionEvent);
        foreach (var (viewer, allDisasters, allHands) in new[]
                 { (0, false, false), (1, false, false), (-1, false, false), (-1, true, true), (0, true, true) })
        {
            // This is the exact common entry used by both live snapshots and
            // MatchRecorder's persisted player replay, not a second projection.
            var projected = L12RecipientVisibility.ProjectActionEvent(game.State, actionEvent, viewer, allDisasters, allHands);
            Assert.NotSame(actionEvent, projected); Assert.NotSame(eventCards, projected.Cards);
            Assert.NotSame(eventCards[0], projected.Cards[0]);
            Assert.Same(eventCards[1], projected.Cards[1]);
            Assert.NotSame(eventCards[2], projected.Cards[2]);
            Assert.NotSame(eventCards[2].AttachedCards[0], projected.Cards[2].AttachedCards[0]);
            Assert.Equal(eventCards.Select(card => card.Troops), projected.Cards.Select(card => card.Troops));
            Assert.All(projected.Cards, card => Assert.False(L12DerivedStats.HasTroopsReductionState(card)));
            Assert.Equal(before, JsonSerializer.Serialize(actionEvent));
        }
        var plain = actionEvent with { Cards = [ordinary.Clone()] };
        var unchanged = L12RecipientVisibility.ProjectActionEvent(game.State, plain, 0, false);
        Assert.Same(plain, unchanged); Assert.Same(plain.Cards, unchanged.Cards);
        Assert.Same(plain.Cards[0], unchanged.Cards[0]);
        var hidden = actionEvent with { Type = "private-trigger-effect", PlayerIndex = 1 };
        var privateResult = L12RecipientVisibility.ProjectActionEvent(game.State, hidden, 0, false);
        Assert.Equal("private", privateResult.Type); Assert.Empty(privateResult.Cards);
        Assert.Equal(before, JsonSerializer.Serialize(actionEvent));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task JournalTailPreservesExplicitProtocolAndEveryStepHash(bool protocol)
    {
        var game = Create(0, protocol); _ = game.SnapshotFor(0);
        Add(game, Find(game, 1, TargetId), -1000, 12, "journal-negative");
        game.State.Revision++;
        var path = Path.Combine(Path.GetTempPath(), "troops-absorption-journal", Guid.NewGuid().ToString("N"), "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync(); recorder.AttachCatalog(Catalog);
        await recorder.StartAsync(game, "sandbox");
        for (var sequence = 1; sequence <= 2; sequence++)
        {
            var actor = game.State.ActivePlayer;
            var command = new L12Command("endTurn");
            var result = game.Handle(actor, command); Assert.True(result.Accepted, result.Error);
            Assert.Empty(game.State.PendingPrompts);
            await recorder.AppendAsync(game, sequence, actor, JsonSerializer.Serialize(command), result, $"absorption-end-{sequence}");
        }
        var recovered = Assert.IsType<L12JournalRecoveryState>(await recorder.LoadJournalEngineAsync(game.State.MatchId));
        Assert.Equal(2, recovered.CommandSequence);
        Assert.Equal(protocol, recovered.Engine.State.TimedTroopsReductionAbsorptionProtocolEnabled);
        Assert.Equal(game.SerializeFullState(), recovered.Engine.SerializeFullState());
        Assert.Equal(game.ComputeStateHash(), recovered.Engine.ComputeStateHash());
    }

    private static L12CardInstance Card(string id, string instanceId, int owner)
    {
        var d = Catalog.Cards[id];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = d.Id, OwnerIndex = owner, Name = d.NameZh,
            CardType = d.CardType, Faction = d.Faction, ImageUrl = d.ImageUrl,
            Cost = d.Cost ?? 0, EffectText = d.Effect, Traits = [.. d.Traits], Profession = d.Profession,
            EffectiveProfession = d.Profession, BaseTroops = d.Troops ?? 0, Troops = d.Troops ?? 0,
            SummonRound = -1,
        };
    }

    private static L12CardInstance Find(L12GameEngine game, int owner, string id)
        => Assert.Single(game.State.Players[owner].Field.SelectMany(row => row), card => card?.InstanceId == id)!;

    private static void AssertTroops(L12GameEngine game, int owner, int expected)
    {
        var view = JsonSerializer.SerializeToElement(game.SnapshotFor(owner), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var target = view.GetProperty("players")[owner].GetProperty("field")[0][0];
        Assert.Equal(TargetId, target.GetProperty("instanceId").GetString());
        Assert.Equal(expected, target.GetProperty("troops").GetInt32());
        Assert.Equal(expected, Find(game, owner, TargetId).CurrentTroops);
    }

    private static L12GameEngine Send(L12GameEngine game, int actor, L12Command command, bool restore)
    {
        game = RestoreIfRequested(game, restore);
        var result = game.Handle(actor, command);
        Assert.True(result.Accepted, $"{command.Type}: {result.Error}");
        return RestoreIfRequested(game, restore);
    }

    private static L12GameEngine Resolve(L12GameEngine game, L12Prompt prompt, string? choice, List<string>? cards, bool restore)
        => Send(game, prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: choice, CardInstanceIds: cards, ActivationId: prompt.ActivationId,
            SourceInstanceId: prompt.SourceInstanceId, SourceCardId: prompt.SourceCardId,
            Step: prompt.Step, CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller), restore);

    private static L12GameEngine RestoreIfRequested(L12GameEngine game, bool restore)
    {
        if (!restore) return game;
        var state = game.SerializeFullState(); var hash = game.ComputeStateHash(); var random = game.RandomState!.Value;
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, state, random, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.Equal(state, restored.SerializeFullState()); Assert.Equal(hash, restored.ComputeStateHash());
        return restored;
    }
}
