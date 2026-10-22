using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BackRowAttackTroopsSetLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private const string AttackerId = "attack-set-attacker";
    private const string TargetId = "attack-set-target";
    private const string AuraId = "attack-set-hannibal";

    [Theory]
    [InlineData("S01-0409", 2000)]
    [InlineData("S02-0507", 3000)]
    [L12AbilityEvidence("S01-0409:ability:attack:c900a6435336564c", "normal", "reconnect", "presentation-consumers", "post-attack-revert")]
    [L12AbilityEvidence("S02-0507:ability:attack:d20040947938d125", "normal", "reconnect", "presentation-consumers", "post-attack-revert")]
    public void BackRowAttackUsesItsDeclaredSetValueAndRevertsAfterSettlement(string cardId, int setValue)
    {
        var game = Create(72360 + setValue);
        var attacker = Card(cardId, $"back-row-{cardId}", Catalog.Cards[cardId].Troops ?? 0);
        var target = Card("S01-0001", $"target-{cardId}", 5000);
        game.State.Players[0].Field[1][0] = attacker;
        game.State.Players[1].Field[0][0] = target;

        Assert.Equal(setValue, L12StructuredCardRules.CombatProfile(attacker, 1).AttackTroopsSetValue);
        Assert.Contains(target.InstanceId, game.SnapshotFor(0).LegalAttackTargets[attacker.InstanceId]);

        game = Restore(game);
        var restoredAttacker = game.State.Players[0].Field[1][0]!;
        var baseTroops = restoredAttacker.BaseTroops;
        var attack = game.Handle(0, new L12Command("attack", restoredAttacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);
        ResolveAllPrompts(game);

        Assert.Equal(baseTroops, game.State.Players[0].Field[1][0]!.Troops);
        Assert.Equal(5000 - setValue, game.State.Players[1].Field[0][0]!.Troops);
        var playerLog = Assert.Single(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains($"兵力视为{setValue}", StringComparison.Ordinal));
        Assert.Equal("触发 进攻时效果", playerLog.PlayerLogSemantic?.ActionLabel);
        Assert.Equal($"本次进攻兵力变为{setValue}", playerLog.PlayerLogSemantic?.OutcomeLabel);
        Assert.Equal(restoredAttacker.InstanceId, playerLog.PlayerLogSemantic?.SourceInstanceId);
        Assert.Equal(restoredAttacker.InstanceId, playerLog.PlayerLogSemantic?.TargetInstanceId);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [Trait("Bug", "BUG-20261022-8ca3c0fb")]
    public void AtalantaBackRowMustAddHannibalAuraAfterSettingAttackTroops(int actor, bool restore)
        // Human ruling: set the attack base to 3000, then apply the continuous +1000.
        => VerifyAttack("S02-0507", actor, restore, row: 1, auraSources: 1, penalty: false, expected: 4000);

    [Theory]
    [InlineData(0, false, 1, 0, 3000)]
    [InlineData(1, true, 0, 1, 5000)]
    [Trait("Bug", "BUG-20261022-8ca3c0fb:positive-control")]
    public void AtalantaNoAuraAndFrontRowKeepTheirOwnAttackTroopsRules(
        int actor, bool restore, int row, int auraSources, int expected)
        => VerifyAttack("S02-0507", actor, restore, row, auraSources, penalty: false, expected: expected);

    [Theory]
    [InlineData("S01-0409", 0, false, 1, 0, false, 2000)]
    [InlineData("S01-0409", 1, true, 1, 1, false, 3000)]
    [InlineData("S01-0409", 0, true, 1, 0, true, 1000)]
    [InlineData("S01-0409", 1, true, 1, 1, true, 2000)]
    [InlineData("S01-0409", 0, true, 1, 2, true, 3000)]
    [InlineData("S01-0409", 1, true, 0, 0, false, 4000)]
    [InlineData("S02-0507", 1, true, 1, 0, true, 2000)]
    [InlineData("S02-0507", 0, true, 1, 1, true, 3000)]
    [InlineData("S02-0507", 1, true, 1, 2, true, 4000)]
    public void SharedAttackSetBaseAddsSignedContinuousLayersWithoutChangingFrontRowRules(
        string cardId, int actor, bool restore, int row, int auraSources, bool penalty, int expected)
        // Human also ruled that both continuous bonuses and penalties follow the set base.
        => VerifyAttack(cardId, actor, restore, row, auraSources, penalty, expected);

    [Theory]
    [InlineData("S01-0409", 2000, 0, false, 500)]
    [InlineData("S01-0409", 2000, 1, true, 1500)]
    [InlineData("S02-0507", 3000, 1, true, 500)]
    [InlineData("S02-0507", 3000, 0, true, 1500)]
    public void AttackSetDoesNotRefillConsumedContinuousBonusOrHealExistingDamage(
        string cardId, int setValue, int actor, bool restore, int damage)
    {
        var game = CreateScenario(cardId, actor);
        _ = game.SnapshotFor(actor);
        var attacker = ScenarioAttacker(game, actor);
        Assert.Equal(5000, attacker.Troops);
        // A bounded synthetic damage fixture uses the real layer-consumption helper;
        // it does not claim that a particular damage card was played.
        L12DerivedStats.ApplyTroopsDamage(attacker, damage);
        game.State.Revision++;
        var consumed = Math.Min(1000, damage);
        var remainingBonus = 1000 - consumed;
        var beforeAttack = 5000 - damage;
        AssertStableTroops(game, actor, beforeAttack);
        Assert.Equal(consumed, attacker.ContinuousTroopsBonusConsumed);

        game = RestoreIfRequested(game, restore);
        DeclareAttack(game, actor, setValue + remainingBonus);
        AssertStableTroops(game, actor, setValue + remainingBonus);
        Assert.Equal(consumed, ScenarioAttacker(game, actor).ContinuousTroopsBonusConsumed);
        FinishScenarioCombat(ref game, restore);

        attacker = ScenarioAttacker(game, actor);
        AssertStableTroops(game, actor, beforeAttack);
        Assert.Equal(consumed, attacker.ContinuousTroopsBonusConsumed);
        Assert.Equal(consumed, Assert.Single(attacker.ContinuousTroopsBonusLayers).Value.Consumed);
        Assert.Equal(remainingBonus, attacker.ContinuousTroopsModifier);
        Assert.Equal(5000 - setValue - remainingBonus, game.State.Players[1 - actor].Field[0][0]!.Troops);
        Assert.Null(attacker.SetTroopsValue);
    }

    [Theory]
    [InlineData("S01-0409", 2000, 0, false)]
    [InlineData("S01-0409", 2000, 1, true)]
    [InlineData("S02-0507", 3000, 1, false)]
    [InlineData("S02-0507", 3000, 0, true)]
    public void LosingAuraDuringResponseRemovesOnlyItsRemainderAndRevertsAttackExactlyOnce(
        string cardId, int setValue, int actor, bool abort)
    {
        var game = CreateScenario(cardId, actor);
        _ = game.SnapshotFor(actor);
        L12DerivedStats.ApplyTroopsDamage(ScenarioAttacker(game, actor), 500);
        game.State.Revision++;
        game = RestoreIfRequested(game, true);
        DeclareAttack(game, actor, setValue + 500);
        Assert.NotEmpty(game.State.PendingPrompts);
        Assert.NotNull(game.State.PendingDefense);

        // A real sandbox zone command removes the source in the open response window.
        // This tests the common departure/recalculation path, not a fabricated card effect.
        var removeAura = game.HandleGm(new L12GmCommand("returnCardToHand", actor, CardInstanceId: AuraId));
        Assert.True(removeAura.Accepted, removeAura.Error);
        Assert.Contains(game.State.Players[actor].Hand, card => card.InstanceId == AuraId);
        AssertStableTroops(game, actor, setValue);
        Assert.Empty(ScenarioAttacker(game, actor).ContinuousTroopsBonusLayers);
        Assert.Equal(0, ScenarioAttacker(game, actor).ContinuousTroopsModifier);
        if (abort)
        {
            var removeTarget = game.HandleGm(new L12GmCommand("returnCardToHand", 1 - actor,
                CardInstanceId: TargetId));
            Assert.True(removeTarget.Accepted, removeTarget.Error);
        }
        FinishScenarioCombat(ref game, restore: true);

        AssertStableTroops(game, actor, 4000);
        Assert.Empty(ScenarioAttacker(game, actor).ContinuousTroopsBonusLayers);
        Assert.Equal(0, ScenarioAttacker(game, actor).ContinuousTroopsBonusConsumed);
        if (abort)
        {
            Assert.Contains(game.State.Players[1 - actor].Hand, card => card.InstanceId == TargetId);
            Assert.Contains(game.State.Events, entry => entry.Type == "attack-aborted");
        }
        else
            Assert.Equal(5000 - setValue, game.State.Players[1 - actor].Field[0][0]!.Troops);
    }

    [Theory]
    [InlineData("S01-0409", 2000, 0)]
    [InlineData("S02-0507", 3000, 1)]
    public void ExistingAttackOnlyBonusRemainsAfterSetBaseAndContinuousLayers(string cardId, int setValue, int actor)
    {
        var game = CreateScenario(cardId, actor);
        var attacker = ScenarioAttacker(game, actor);
        attacker.AttackOnlyTroopsBonus = 1000;
        attacker.AttackOnlyTroopsBonusUntilTurn = game.State.TurnSerial;
        game = RestoreIfRequested(game, true);
        DeclareAttack(game, actor, setValue + 2000);
        FinishScenarioCombat(ref game, restore: true);
        AssertStableTroops(game, actor, 5000);
        if (setValue + 2000 >= 5000)
            Assert.Contains(game.State.Players[1 - actor].Graveyard, card => card.InstanceId == TargetId);
        else Assert.Equal(5000 - setValue - 2000, game.State.Players[1 - actor].Field[0][0]!.Troops);
    }

    private static void VerifyAttack(string cardId, int actor, bool restore, int row,
        int auraSources, bool penalty, int expected)
    {
        var game = CreateScenario(cardId, actor, row, auraSources, penalty);
        _ = game.SnapshotFor(actor);
        var beforeAttack = 4000 + auraSources * 1000 - (penalty ? 1000 : 0);
        Assert.Equal(beforeAttack, ScenarioAttacker(game, actor).Troops);
        game = RestoreIfRequested(game, restore);
        DeclareAttack(game, actor, expected);
        var setValue = L12StructuredCardRules.CombatProfile(ScenarioAttacker(game, actor), row).AttackTroopsSetValue;
        var setLog = game.State.Events.Where(entry => entry.Type == "effect"
            && entry.PlayerLogSemantic?.TargetInstanceId == AttackerId
            && entry.PlayerLogSemantic.ActionLabel == "触发 进攻时效果").ToArray();
        if (setValue is { } basis)
        {
            var entry = Assert.Single(setLog);
            var modifier = auraSources * 1000 - (penalty ? 1000 : 0);
            var signed = modifier > 0 ? $"+{modifier}" : modifier.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(modifier == 0 ? $"本次进攻兵力变为{expected}"
                : $"本次进攻兵力变为{expected}（基值{basis}，持续修正{signed}）",
                entry.PlayerLogSemantic!.OutcomeLabel);
            Assert.Equal(expected, Assert.Single(entry.Cards).Troops);
        }
        else Assert.Empty(setLog);
        FinishScenarioCombat(ref game, restore);
        if (expected >= 5000)
            Assert.Contains(game.State.Players[1 - actor].Graveyard, card => card.InstanceId == TargetId);
        else Assert.Equal(5000 - expected, game.State.Players[1 - actor].Field[0][0]!.Troops);
        if (row == 1) AssertStableTroops(game, actor, beforeAttack);
    }

    private static void DeclareAttack(L12GameEngine game, int actor, int expected)
    {
        var attack = game.Handle(actor, new L12Command("attack", AttackerId,
            Target: new L12AttackTarget("legion", TargetId)));
        Assert.True(attack.Accepted, attack.Error);
        var declared = Assert.Single(game.State.Events, entry => entry.Type == "attack");
        Assert.Equal(expected, Assert.IsType<L12PlayerCombatPresentation>(declared.PlayerCombat).AttackerTroops);
    }

    private static L12CardInstance ScenarioAttacker(L12GameEngine game, int actor)
        => game.State.Players[actor].Field.SelectMany(row => row).Single(card => card?.InstanceId == AttackerId)!;

    private static void AssertStableTroops(L12GameEngine game, int actor, int expected)
    {
        for (var count = 0; count < 2; count++)
        {
            _ = game.SnapshotFor(actor);
            _ = game.SnapshotFor(1 - actor);
            _ = game.SnapshotForSpectator();
            _ = game.SnapshotForReferee();
            Assert.Equal(expected, ScenarioAttacker(game, actor).Troops);
        }
    }

    private static void FinishScenarioCombat(ref L12GameEngine game, bool restore)
    {
        for (var count = 0; count < 24; count++)
        {
            game = RestoreIfRequested(game, restore);
            if (game.State.PendingPrompts.Count == 0)
            {
                Assert.Null(game.State.PendingDefense);
                Assert.Null(game.State.ResponseWindow);
                Assert.Empty(game.State.EffectStack);
                Assert.Empty(game.State.DeferredEffectStack);
                Assert.Empty(game.State.PendingTriggerBatches);
                Assert.Empty(game.State.PendingTriggerStackCandidates);
                return;
            }
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", prompt.Kind);
            Assert.Contains("pass", prompt.ValidChoices);
            var pass = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(pass.Accepted, pass.Error);
        }
        throw new Xunit.Sdk.XunitException("Attack-set combat did not finish within its bounded response chain.");
    }

    private static L12GameEngine RestoreIfRequested(L12GameEngine game, bool restore)
    {
        if (!restore) return game;
        var checkpoint = game.SerializeFullState();
        var restored = Restore(game);
        Assert.Equal(checkpoint, restored.SerializeFullState());
        Assert.Equal(game.RandomState, restored.RandomState);
        return restored;
    }

    private static L12GameEngine CreateScenario(string cardId, int actor, int row = 1,
        int auraSources = 1, bool penalty = false)
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "Synthetic attack-set layers", MasterId = "S02-05M2",
            CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "attack-set-layers", "ATTACK-SET", 102340 + actor,
            ["Synthetic A", "Synthetic B"], [deck, deck], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2,
            utcNow: () => DateTimeOffset.Parse("2026-10-23T02:00:00+08:00"));
        foreach (var player in game.State.Players)
        {
            foreach (var fieldRow in player.Field) Array.Clear(fieldRow);
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Resolving.Clear();
            player.Removed.Clear(); player.Morale.Clear(); player.MoraleDeck.Clear(); player.UsedAbilities.Clear();
            player.Relic = null; player.ExtraRelics.Clear(); player.Hp = 8;
            for (var index = 0; index < 12; index++)
                player.Library.Add(Card("S01-0003", $"attack-set-library-{player.PlayerIndex}-{index}",
                    Catalog.Cards["S01-0003"].Troops ?? 0, player.PlayerIndex));
        }
        game.State.ActivePlayer = actor; game.State.FirstPlayer = actor;
        game.State.Round = 3; game.State.TurnSerial = 6; game.State.Phase = L12Phase.Main;
        game.State.PendingPrompts.Clear(); game.State.PendingActivations.Clear();
        game.State.EffectStack.Clear(); game.State.DeferredEffectStack.Clear();
        game.State.PendingTriggerBatches.Clear(); game.State.PendingTriggerStackCandidates.Clear();
        game.State.SuspendedCombatContexts.Clear(); game.State.Events.Clear(); game.State.AuthorityEvents.Clear();
        game.State.Log.Clear(); game.State.LastAction = null; game.State.ResponseWindow = null;
        game.State.PendingDefense = null; game.State.ActiveDisaster = null;
        var attackerSlot = auraSources == 2 ? 1 : 0;
        game.State.Players[actor].Field[row][attackerSlot] = Card(cardId, AttackerId, Catalog.Cards[cardId].Troops ?? 0, actor);
        if (auraSources > 0)
            game.State.Players[actor].Field[row][auraSources == 2 ? 0 : 1] = Card("S02-0516", AuraId,
                Catalog.Cards["S02-0516"].Troops ?? 0, actor);
        if (auraSources == 2)
            game.State.Players[actor].Field[row][2] = Card("S02-0516", "attack-set-second-hannibal",
                Catalog.Cards["S02-0516"].Troops ?? 0, actor);
        if (penalty)
            game.State.Players[actor].Field[1 - row][0] = Card("S02-0523", "attack-set-trojan",
                Catalog.Cards["S02-0523"].Troops ?? 0, 1 - actor);
        game.State.Players[1 - actor].Field[0][0] = Card("S01-0002", TargetId, 5000, 1 - actor);
        return game;
    }

    private static void ResolveAllPrompts(L12GameEngine game)
    {
        for (var count = 0; count < 30 && game.State.PendingPrompts.FirstOrDefault() is { } prompt; count++)
        {
            var command = prompt.Kind == "response"
                ? new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")
                : new L12Command("resolveDefense");
            var result = game.Handle(prompt.PlayerIndex, command);
            Assert.True(result.Accepted, result.Error);
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "back-row-attack-set", "BACK-ROW-SET", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 3;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Morale.Clear();
        }
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static L12CardInstance Card(string cardId, string instanceId, int troops, int owner = 0)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost is not null,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = troops,
            Troops = troops,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }
}
