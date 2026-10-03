using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class DisasterDamagePriorityTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void RingAndNightParadeWithoutStrongAttackSettlesAtTwo()
    {
        var game = Create(83014);
        var attacker = Card("S01-0001", "priority-ring-night-parade-attacker", 0);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Relic = Card("S02-0305", "priority-ring-night-parade-ring", 1);
        game.State.ActiveDisaster = Card("S01-DS02", "priority-ring-night-parade", -1);
        var hpBefore = game.State.Players[1].Hp;

        DeclareMasterAttack(game, attacker);
        Assert.Equal(2, Assert.IsType<L12PendingDefense>(game.State.PendingDefense).MasterDamage);
        SettleCombat(game);

        Assert.Equal(hpBefore - 2, game.State.Players[1].Hp);
        Assert.Equal(2, game.State.Players[1].MasterDamageTakenThisTurn);
    }

    [Theory]
    [InlineData(true, false, true, 2, 2)]
    [InlineData(true, true, true, 3, 3)]
    [InlineData(false, true, true, 3, 3)]
    [InlineData(false, false, true, 2, 2)]
    [InlineData(true, false, false, 1, 2)]
    [InlineData(true, true, false, 2, 2)]
    public void RingReplacementCannotReduceConfirmedDamageThatContainsNightParade(bool hasRing,
        bool hasStrongAttack, bool hasDisasterLevel, int expectedPreview, int expectedDamage)
    {
        var game = Create(83001);
        var attacker = Card(hasDisasterLevel ? "S01-0001" : "S01-0002", "priority-attacker", 0);
        attacker.HasStrongAttack = hasStrongAttack;
        game.State.Players[0].Field[0][0] = attacker;
        if (hasRing)
            game.State.Players[1].Relic = Card("S02-0305", "priority-ring", 1);
        game.State.ActiveDisaster = Card("S01-DS02", "priority-night-parade", -1);
        var hpBefore = game.State.Players[1].Hp;

        DeclareMasterAttack(game, attacker);
        var pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal(expectedPreview, pending.MasterDamage);
        Assert.Equal(hasDisasterLevel ? 1 : 0, pending.DeclaredDisasterMasterDamageBonus);
        SettleCombat(game);

        Assert.Equal(hpBefore - expectedDamage, game.State.Players[1].Hp);
        var defense = Assert.Single(game.State.Events, entry => entry.Type == "defense"
            && entry.PlayerCombat?.OutcomeCode == "unblocked");
        Assert.Contains($"受到 {expectedDamage} 点伤害", defense.Text, StringComparison.Ordinal);
        Assert.Equal(expectedDamage, defense.PlayerCombat?.MasterDamage);
        Assert.Contains(game.State.Events, entry => entry.Type == "damage"
            && entry.Text.Contains($"失去 {expectedDamage} 点血量", StringComparison.Ordinal));
    }

    [Fact]
    public void ResponseTimingStrongGrantKeepsDeclaredNightParadeInfluenceInTheConfirmedTotal()
    {
        var game = Create(83002);
        var attacker = Card("S01-0001", "response-strong-attacker", 0);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Relic = Card("S02-0305", "response-strong-ring", 1);
        game.State.ActiveDisaster = Card("S01-DS02", "response-strong-night-parade", -1);
        var hpBefore = game.State.Players[1].Hp;

        DeclareMasterAttack(game, attacker);
        Assert.Equal(2, Assert.IsType<L12PendingDefense>(game.State.PendingDefense).MasterDamage);
        InvokePrivate(game, "GrantStrongAttack", attacker);
        Assert.True(attacker.HasStrongAttack);
        Assert.Equal(3, Assert.IsType<L12PendingDefense>(game.State.PendingDefense).MasterDamage);

        SettleCombat(game);

        Assert.Equal(hpBefore - 3, game.State.Players[1].Hp);
        Assert.Equal(3, game.State.Players[1].MasterDamageTakenThisTurn);
    }

    [Fact]
    public void GawainAttackTimingIncrementCannotBeReducedWhenConfirmedTotalContainsNightParade()
    {
        var game = Create(83003);
        var attacker = Card("S02-0607", "gawain-priority-attacker", 0);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[0].SpecialZones.Runes = 2;
        game.State.Players[1].Relic = Card("S02-0305", "gawain-priority-ring", 1);
        game.State.ActiveDisaster = Card("S01-DS02", "gawain-priority-night-parade", -1);
        var hpBefore = game.State.Players[1].Hp;

        DeclareMasterAttack(game, attacker);
        Assert.Equal(2, Assert.IsType<L12PendingDefense>(game.State.PendingDefense).MasterDamage);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("rune-count:2", declaration.ValidChoices);
        ResolvePrompt(game, declaration, "rune-count:2");
        AdvanceUntil(game, () => attacker.GawainMasterDamageBonus == 2
            && game.State.PendingDefense?.MasterDamage == 4);

        Assert.Equal(game.State.TurnSerial, attacker.GawainMasterDamageBonusUntilTurn);
        Assert.Equal(4, Assert.IsType<L12PendingDefense>(game.State.PendingDefense).MasterDamage);
        SettleCombat(game);

        Assert.Equal(hpBefore - 4, game.State.Players[1].Hp);
        Assert.Equal(4, game.State.Players[1].MasterDamageTakenThisTurn);
    }

    [Fact]
    public void RejectedRingReductionStillCountsAsTheFirstDamage()
    {
        var game = Create(83004);
        var first = Card("S01-0001", "priority-first-attacker", 0);
        first.HasStrongAttack = true;
        var second = Card("S01-0002", "priority-second-attacker", 0);
        game.State.Players[0].Field[0][0] = first;
        game.State.Players[0].Field[0][1] = second;
        game.State.Players[1].Relic = Card("S02-0305", "priority-first-only-ring", 1);
        game.State.ActiveDisaster = Card("S01-DS02", "priority-first-only-night-parade", -1);
        var hpBefore = game.State.Players[1].Hp;

        DeclareMasterAttack(game, first);
        Assert.Equal(3, Assert.IsType<L12PendingDefense>(game.State.PendingDefense).MasterDamage);
        SettleCombat(game);
        Assert.Equal(hpBefore - 3, game.State.Players[1].Hp);
        Assert.Equal(3, game.State.Players[1].MasterDamageTakenThisTurn);

        DeclareMasterAttack(game, second);
        Assert.Equal(1, Assert.IsType<L12PendingDefense>(game.State.PendingDefense).MasterDamage);
        SettleCombat(game);

        Assert.Equal(hpBefore - 4, game.State.Players[1].Hp);
        Assert.Equal(4, game.State.Players[1].MasterDamageTakenThisTurn);
        Assert.Contains(game.State.Events, entry => entry.Type == "damage"
            && entry.Text.Contains("失去 3 点血量", StringComparison.Ordinal));
        Assert.Contains(game.State.Events, entry => entry.Type == "damage"
            && entry.Text.Contains("失去 1 点血量", StringComparison.Ordinal));
    }

    [Fact]
    public void RingStillReplacesOrdinaryThreeWithoutNightParade()
    {
        var game = Create(83015);
        var attacker = Card("S01-0002", "priority-ordinary-three-attacker", 0);
        attacker.MasterAttackDamageBonus = 2;
        attacker.MasterAttackDamageBonusUntilTurn = game.State.TurnSerial;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Relic = Card("S02-0305", "priority-ordinary-three-ring", 1);
        var hpBefore = game.State.Players[1].Hp;

        DeclareMasterAttack(game, attacker);
        var pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal(3, pending.MasterDamage);
        Assert.Equal(0, pending.DeclaredDisasterMasterDamageBonus);
        SettleCombat(game);

        Assert.Equal(hpBefore - 2, game.State.Players[1].Hp);
        Assert.Equal(2, game.State.Players[1].MasterDamageTakenThisTurn);
    }

    [Fact]
    public void PiercingUsesTheSameReplacementPriorityAndKeepsAttackTriggerSuppression()
    {
        var game = Create(83005);
        var attacker = Card("S02-0611", "priority-piercing-attacker", 0);
        var defeated = Card("S01-0002", "priority-piercing-defeated", 1, troops: 1000);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = defeated;
        game.State.Players[1].Relic = Card("S02-0305", "priority-piercing-ring", 1);
        game.State.ActiveDisaster = Card("S01-DS02", "priority-piercing-night-parade", -1);
        var hpBefore = game.State.Players[1].Hp;

        var attack = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", defeated.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);
        AdvanceUntil(game, () => game.State.PendingDefense is { Target.Type: "master" });

        var piercing = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal(2, piercing.MasterDamage);
        Assert.True(piercing.SuppressAttackTriggers);
        Assert.Single(game.State.Events, entry => entry.Type == "piercing");
        SettleCombat(game);

        Assert.Equal(hpBefore - 2, game.State.Players[1].Hp);
        Assert.Single(game.State.Events, entry => entry.Type == "piercing");
    }

    [Fact]
    public void EffectBlockPreventsDamageAndDoesNotConsumeRingPriority()
    {
        var game = Create(83006);
        var first = Card("S01-0001", "priority-blocked-attacker", 0);
        var second = Card("S01-0001", "priority-after-block-attacker", 0);
        var mercenary = Card("S01-0002", "priority-mercenary-block", 1);
        var counter = Card("S01-0016", "priority-absolute-defense", 1);
        counter.Hidden = true;
        counter.SetRound = 0;
        game.State.Players[0].Field[0][0] = first;
        game.State.Players[0].Field[0][1] = second;
        game.State.Players[1].Field[1][0] = counter;
        game.State.Players[1].Hand.Add(mercenary);
        game.State.Players[1].Relic = Card("S02-0305", "priority-block-ring", 1);
        game.State.ActiveDisaster = Card("S01-DS02", "priority-block-night-parade", -1);
        var hpBefore = game.State.Players[1].Hp;

        DeclareMasterAttack(game, first);
        var blockPrompt = FindPrompt(game, prompt => prompt.Kind == "response"
            && prompt.ValidChoices.Contains(counter.InstanceId));
        ResolvePrompt(game, blockPrompt, counter.InstanceId);
        SettleCombat(game);

        Assert.Equal(hpBefore, game.State.Players[1].Hp);
        Assert.Equal(0, game.State.Players[1].MasterDamageTakenThisTurn);
        Assert.Contains(game.State.Events, entry => entry.Type == "defense"
            && entry.PlayerCombat?.OutcomeCode == "blocked");

        DeclareMasterAttack(game, second);
        SettleCombat(game);
        Assert.Equal(hpBefore - 2, game.State.Players[1].Hp);
        Assert.Equal(2, game.State.Players[1].MasterDamageTakenThisTurn);
    }

    [Theory]
    [InlineData(true, 2, 2)]
    [InlineData(false, 1, 2)]
    public void DeclaredDisasterInfluenceSurvivesV2RestoreWithoutUsingTheCurrentDisaster(bool declaredWithBonus,
        int expectedPreview, int expectedDamage)
    {
        var game = Create(83007);
        var attacker = Card("S01-0001", "priority-restore-attacker", 0);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Relic = Card("S02-0305", "priority-restore-ring", 1);
        game.State.ActiveDisaster = declaredWithBonus
            ? Card("S01-DS02", "priority-restore-original-night-parade", -1)
            : null;
        var hpBefore = game.State.Players[1].Hp;

        DeclareMasterAttack(game, attacker);
        var declaredPending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal(expectedPreview, declaredPending.MasterDamage);
        Assert.Equal(declaredWithBonus ? 1 : 0,
            declaredPending.DeclaredDisasterMasterDamageBonus);
        Assert.Contains($"\"DeclaredDisasterMasterDamageBonus\":{(declaredWithBonus ? 1 : 0)}",
            game.SerializeFullState(), StringComparison.Ordinal);
        game.State.ActiveDisaster = declaredWithBonus
            ? null
            : Card("S01-DS02", "priority-restore-later-night-parade", -1);
        game = Restore(game);

        var restoredPending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal(expectedPreview, restoredPending.MasterDamage);
        Assert.Equal(declaredWithBonus ? 1 : 0,
            restoredPending.DeclaredDisasterMasterDamageBonus);
        SettleCombat(game);
        Assert.Equal(hpBefore - expectedDamage, game.State.Players[1].Hp);
    }

    [Fact]
    public void ExplicitZeroDisasterComponentDoesNotConfuseAttachedStrongAttackAfterRestore()
    {
        var game = Create(83012);
        var attacker = Card("S01-0001", "priority-attached-strong-attacker", 0);
        var attachedStrongAttack = Card("S02-06S2", "priority-attached-strong-source", 0);
        attacker.AttachedCards.Add(attachedStrongAttack);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Relic = Card("S02-0305", "priority-attached-strong-ring", 1);
        var hpBefore = game.State.Players[1].Hp;

        DeclareMasterAttack(game, attacker);
        var pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal(2, pending.MasterDamage);
        Assert.Equal(0, pending.DeclaredDisasterMasterDamageBonus);

        Assert.True(attacker.AttachedCards.Remove(attachedStrongAttack));
        game.State.Players[0].Graveyard.Add(attachedStrongAttack);
        game = Restore(game);

        pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal(2, pending.MasterDamage);
        Assert.Equal(0, pending.DeclaredDisasterMasterDamageBonus);
        Assert.Empty(Assert.IsType<L12CardInstance>(game.State.Players[0].Field[0][0]).AttachedCards);
        SettleCombat(game);
        Assert.Equal(hpBefore - 2, game.State.Players[1].Hp);
    }

    [Fact]
    public void LegacyV2WithoutExplicitDisasterComponentKeepsPreviouslyDeclaredTotalSemantics()
    {
        var game = Create(83013);
        var attacker = Card("S01-0001", "priority-legacy-attacker", 0);
        attacker.HasStrongAttack = true;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Relic = Card("S02-0305", "priority-legacy-ring", 1);
        game.State.ActiveDisaster = Card("S01-DS02", "priority-legacy-night-parade", -1);
        var hpBefore = game.State.Players[1].Hp;

        DeclareMasterAttack(game, attacker);
        var document = JsonNode.Parse(game.SerializeFullState())!.AsObject();
        Assert.True(document["PendingDefense"]!.AsObject()
            .Remove("DeclaredDisasterMasterDamageBonus"));
        game = Restore(game, document.ToJsonString());

        var pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Null(pending.DeclaredDisasterMasterDamageBonus);
        Assert.Equal(3, pending.MasterDamage);
        Assert.DoesNotContain("DeclaredDisasterMasterDamageBonus", game.SerializeFullState(),
            StringComparison.Ordinal);
        SettleCombat(game);
        Assert.Equal(hpBefore - 2, game.State.Players[1].Hp);
    }

    [Fact]
    public void UndeployedFinalFieldIsIgnoredAndCannotReviveTheSupersededPriority()
    {
        var game = Create(83016);
        var attacker = Card("S01-0001", "priority-unknown-old-field-attacker", 0);
        attacker.HasStrongAttack = true;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Relic = Card("S02-0305", "priority-unknown-old-field-ring", 1);
        game.State.ActiveDisaster = Card("S01-DS02", "priority-unknown-old-field-night-parade", -1);
        var hpBefore = game.State.Players[1].Hp;

        DeclareMasterAttack(game, attacker);
        var document = JsonNode.Parse(game.SerializeFullState())!.AsObject();
        var pendingDocument = document["PendingDefense"]!.AsObject();
        Assert.True(pendingDocument.Remove("DeclaredDisasterMasterDamageBonus"));
        pendingDocument["DeclaredFinalDisasterMasterDamageBonus"] = 1;
        game = Restore(game, document.ToJsonString());

        var pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Null(pending.DeclaredDisasterMasterDamageBonus);
        Assert.Equal(3, pending.MasterDamage);
        var restored = game.SerializeFullState();
        Assert.DoesNotContain("DeclaredDisasterMasterDamageBonus", restored, StringComparison.Ordinal);
        Assert.DoesNotContain("DeclaredFinalDisasterMasterDamageBonus", restored, StringComparison.Ordinal);
        SettleCombat(game);
        Assert.Equal(hpBefore - 2, game.State.Players[1].Hp);
    }

    [Fact]
    public void RestoringSettledHistoricalHpAndDamageEventDoesNotRecalculateTheFact()
    {
        var game = Create(83017);
        game.State.Players[1].Hp = 7;
        game.State.Players[1].MasterDamageTakenThisTurn = 3;
        var historical = new L12ActionEvent(++game.State.EventSequence, "damage", 1,
            "历史已结算事实：主宰失去 3 点血量", []);
        game.State.Events.Add(historical);
        game.State.LastAction = historical;

        game = Restore(game);

        Assert.Equal(7, game.State.Players[1].Hp);
        Assert.Equal(3, game.State.Players[1].MasterDamageTakenThisTurn);
        var restored = Assert.Single(game.State.Events,
            entry => entry.Sequence == historical.Sequence && entry.Type == "damage");
        Assert.Equal(historical.Text, restored.Text);
        Assert.Null(game.State.PendingDefense);
    }

    [Fact]
    public async Task JournalRecoveryAndDuplicateDefensePreserveOnePrioritySettlement()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-disaster-damage-priority",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var game = Create(83008, matchId: "disaster-damage-priority-journal");
            var attacker = Card("S01-0001", "priority-journal-attacker", 0);
            game.State.Players[0].Field[0][0] = attacker;
            game.State.Players[1].Relic = Card("S02-0305", "priority-journal-ring", 1);
            game.State.ActiveDisaster = Card("S01-DS02", "priority-journal-night-parade", -1);
            var hpBefore = game.State.Players[1].Hp;
            var database = Path.Combine(directory, "matches.db");

            await using var recorder = new MatchRecorder(database);
            await recorder.InitializeAsync();
            recorder.AttachCatalog(Catalog);
            await recorder.StartAsync(game);
            long sequence = 0;

            async Task<CommandResult> ApplyAsync(int playerIndex, L12Command command, string? requestId = null)
            {
                var result = game.Handle(playerIndex, command);
                await recorder.AppendAsync(game, ++sequence, playerIndex, JsonSerializer.Serialize(command), result,
                    requestId);
                return result;
            }

            var attack = await ApplyAsync(0, new L12Command("attack", attacker.InstanceId,
                Target: new L12AttackTarget("master")), "priority-attack");
            Assert.True(attack.Accepted, attack.Error);
            Assert.Equal(2, Assert.IsType<L12PendingDefense>(game.State.PendingDefense).MasterDamage);
            await SettleCombatAsync(game, ApplyAsync);
            Assert.Equal(hpBefore - 2, game.State.Players[1].Hp);

            var duplicate = await ApplyAsync(1, new L12Command("resolveDefense", CardInstanceIds: []),
                "priority-duplicate-defense");
            Assert.False(duplicate.Accepted);
            Assert.Equal(hpBefore - 2, game.State.Players[1].Hp);

            var recovery = Assert.IsType<L12JournalRecoveryState>(
                await recorder.LoadJournalEngineAsync(game.State.MatchId));
            Assert.Equal(sequence, recovery.CommandSequence);
            Assert.Equal(game.ComputeStateHash(), recovery.Engine.ComputeStateHash());
            Assert.Equal(hpBefore - 2, recovery.Engine.State.Players[1].Hp);
            Assert.Equal(2, recovery.Engine.State.Players[1].MasterDamageTakenThisTurn);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DeclaredDisasterInfluenceDoesNotLeakIntoNonLethalNeutralOrZeroDamage()
    {
        var nonLethal = Create(83009);
        nonLethal.State.Players[1].Hp = 10;
        nonLethal.State.Players[1].Relic = Card("S02-0305", "priority-nonlethal-ring", 1);
        nonLethal.State.ActiveDisaster = Card("S01-DS02", "priority-nonlethal-night-parade", -1);
        InvokePrivate(nonLethal, "DamageMasterNonLethal", 1, 1, "非进攻伤害", 0, false);
        Assert.Equal(8, nonLethal.State.Players[1].Hp);
        Assert.Equal(2, nonLethal.State.Players[1].MasterDamageTakenThisTurn);

        var neutral = Create(83010);
        neutral.State.Players[1].Hp = 10;
        neutral.State.Players[1].Relic = Card("S02-0305", "priority-neutral-ring", 1);
        neutral.State.ActiveDisaster = Card("S01-DS02", "priority-neutral-night-parade", -1);
        InvokePrivate(neutral, "DamageMasterNonLethal", 1, 1, "中立天灾伤害", null, true);
        Assert.Equal(9, neutral.State.Players[1].Hp);
        Assert.Equal(1, neutral.State.Players[1].MasterDamageTakenThisTurn);

        var resolvedZero = Assert.IsType<int>(InvokePrivate(neutral, "ResolveMasterDamageAmount",
            1, 0, null, true, null, 1));
        Assert.Equal(0, resolvedZero);
    }

    private static void DeclareMasterAttack(L12GameEngine game, L12CardInstance attacker)
    {
        var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(result.Accepted, result.Error);
    }

    private static void SettleCombat(L12GameEngine game)
    {
        AdvanceUntil(game, () => game.State.PendingDefense is null
            && game.State.PendingPrompts.Count == 0
            && game.State.EffectStack.Count == 0
            && game.State.DeferredEffectStack.Count == 0
            && game.State.PendingTriggerBatches.Count == 0
            && game.State.PendingTriggerStackCandidates.Count == 0);
    }

    private static async Task SettleCombatAsync(L12GameEngine game,
        Func<int, L12Command, string?, Task<CommandResult>> applyAsync)
    {
        for (var guard = 0; guard < 160; guard++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                var result = await applyAsync(prompt.PlayerIndex, DefaultPromptCommand(prompt), null);
                Assert.True(result.Accepted, result.Error);
                continue;
            }

            if (game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice } pending)
            {
                var result = await applyAsync(1 - pending.AttackerPlayer,
                    new L12Command("resolveDefense", CardInstanceIds: []), null);
                Assert.True(result.Accepted, result.Error);
                continue;
            }

            if (game.State.PendingDefense is null
                && game.State.EffectStack.Count == 0
                && game.State.DeferredEffectStack.Count == 0
                && game.State.PendingTriggerBatches.Count == 0
                && game.State.PendingTriggerStackCandidates.Count == 0)
                return;

            throw new InvalidOperationException(DescribeStall(game));
        }

        throw new Xunit.Sdk.XunitException("天灾伤害优先级Journal场景未在限定步骤内完成");
    }

    private static void AdvanceUntil(L12GameEngine game, Func<bool> completed)
    {
        for (var guard = 0; guard < 160 && !completed(); guard++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                var result = game.Handle(prompt.PlayerIndex, DefaultPromptCommand(prompt));
                Assert.True(result.Accepted, result.Error);
                continue;
            }

            if (game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice } pending)
            {
                var result = game.Handle(1 - pending.AttackerPlayer,
                    new L12Command("resolveDefense", CardInstanceIds: []));
                Assert.True(result.Accepted, result.Error);
                continue;
            }

            throw new InvalidOperationException(DescribeStall(game));
        }

        Assert.True(completed(), "天灾伤害优先级场景未在限定步骤内到达预期状态");
    }

    private static L12Prompt FindPrompt(L12GameEngine game, Func<L12Prompt, bool> predicate)
    {
        for (var guard = 0; guard < 100; guard++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (predicate(prompt)) return prompt;
            var result = game.Handle(prompt.PlayerIndex, DefaultPromptCommand(prompt));
            Assert.True(result.Accepted, result.Error);
        }

        throw new Xunit.Sdk.XunitException("未找到预期响应提示");
    }

    private static void ResolvePrompt(L12GameEngine game, L12Prompt prompt, string choice)
    {
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static L12Command DefaultPromptCommand(L12Prompt prompt)
    {
        if (prompt.MinChoose > 1)
            return new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: prompt.ValidChoices.Take(prompt.MinChoose).ToList());

        var choice = prompt.Kind == "response" ? "pass"
            : prompt.ValidChoices.Contains("mode:none") ? "mode:none"
            : prompt.ValidChoices.Contains("skip") ? "skip"
            : prompt.ValidChoices.Contains("no") ? "no"
            : prompt.ValidChoices.Contains("decline") ? "decline"
            : prompt.ValidChoices[0];
        return new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice);
    }

    private static string DescribeStall(L12GameEngine game)
        => $"天灾伤害优先级场景停滞：Phase={game.State.Phase}, "
           + $"PendingDefense={game.State.PendingDefense?.Stage}, Prompts={game.State.PendingPrompts.Count}, "
           + $"Stack={game.State.EffectStack.Count}, Deferred={game.State.DeferredEffectStack.Count}, "
           + $"Batches={game.State.PendingTriggerBatches.Count}, "
           + $"Candidates={game.State.PendingTriggerStackCandidates.Count}";

    private static object? InvokePrivate(L12GameEngine game, string methodName, params object?[] arguments)
    {
        var method = typeof(L12GameEngine).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(game, arguments);
    }

    private static L12GameEngine Create(int seed, string matchId = "disaster-damage-priority")
    {
        var game = new L12GameEngine(Catalog, matchId, "DISASTER-PRIORITY", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Resolving.Clear();
            player.MasterDamageTakenThisTurn = 0;
        }
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 3;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => Restore(game, game.SerializeFullState());

    private static L12GameEngine Restore(L12GameEngine game, string checkpoint)
        => L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static L12CardInstance Card(string cardId, string instanceId, int ownerIndex, int? troops = null)
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
            EffectiveProfession = definition.Profession,
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
            OwnerIndex = ownerIndex,
        };
    }
}
