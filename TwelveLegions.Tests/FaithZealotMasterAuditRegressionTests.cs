using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class FaithZealotMasterAuditRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> ConstructibleMasterEligibility()
    {
        yield return ["S01-01M1", new[] { "drawCycle" }];
        yield return ["S01-01M2", Array.Empty<string>()];
        yield return ["S01-02M1", Array.Empty<string>()];
        yield return ["S01-02M3", new[] { "medjedDebuff" }];
        yield return ["S01-03M1", new[] { "valkyrieRecover" }];
        yield return ["S01-03M2", new[] { "lokiCycle", "lokiHeal" }];
        yield return ["S01-04M1", new[] { "amaterasuKill" }];
        yield return ["S01-04M2", new[] { "frontBuff", "kusanagi" }];
        yield return ["S02-01M1", Array.Empty<string>()];
        yield return ["S02-02M1", Array.Empty<string>()];
        yield return ["S02-03M1", new[] { "thorCharge" }];
        yield return ["S02-04M1", Array.Empty<string>()];
        yield return ["S02-05M1", Array.Empty<string>()];
        yield return ["S02-05M2", Array.Empty<string>()];
        yield return ["S02-06M1", Array.Empty<string>()];
        yield return ["S02-06M2", Array.Empty<string>()];
        yield return ["ST01-M1", Array.Empty<string>()];
        yield return ["ST02-M1", new[] { "horusRevive" }];
        yield return ["ST03-M1", Array.Empty<string>()];
        yield return ["ST04-M1", Array.Empty<string>()];
        yield return ["ST05-M1", Array.Empty<string>()];
        yield return ["ST06-M1", Array.Empty<string>()];
    }

    public static IEnumerable<object[]> DivinityEligibility()
    {
        yield return ["S01-01D1", Array.Empty<string>()];
        yield return ["S01-02D1", new[] { "sunTopThree", "sunBottomEnemy" }];
        yield return ["S01-03D1", new[] { "valhallaRecover" }];
        yield return ["S01-04D1", new[] { "yomiSweep" }];
        yield return ["S02-05D1", Array.Empty<string>()];
        yield return ["S02-06D1", Array.Empty<string>()];
    }

    [Fact]
    public void AuditRosterMatchesAllConstructibleMastersAndEveryDivinityInTheCatalog()
    {
        var auditedMasters = ConstructibleMasterEligibility().Select(row => Assert.IsType<string>(row[0]))
            .Order(StringComparer.Ordinal).ToArray();
        var catalogMasters = Catalog.Cards.Values
            .Where(card => card.CardType == "master" && card.Id != "S01-02M2")
            .Select(card => card.Id).Order(StringComparer.Ordinal).ToArray();
        var auditedDivinities = DivinityEligibility().Select(row => Assert.IsType<string>(row[0]))
            .Order(StringComparer.Ordinal).ToArray();
        var catalogDivinities = Catalog.Cards.Values.Where(card => card.CardType == "divinity")
            .Select(card => card.Id).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(22, auditedMasters.Length);
        Assert.Equal(catalogMasters, auditedMasters);
        Assert.Equal(6, auditedDivinities.Length);
        Assert.Equal(catalogDivinities, auditedDivinities);
    }

    [Theory]
    [MemberData(nameof(ConstructibleMasterEligibility))]
    public void EveryConstructibleMasterHasAnExactPublicFaithChoiceSet(string masterId, string[] expected)
        => AssertFaithChoiceSet(masterId, expected);

    [Theory]
    [MemberData(nameof(DivinityEligibility))]
    public void EveryDivinityHasAnIndependentExactPublicFaithChoiceSet(string masterId, string[] expected)
        => AssertFaithChoiceSet(masterId, expected);

    [Fact]
    public void PublicFaithChoiceContainsOnlyStaticAbilityIdentitiesAndNoPrivateCardCandidates()
    {
        var game = Create(25300);
        var player = game.State.Players[0];
        SetMaster(player, "S01-03M2");
        var privateIds = new[]
        {
            "faith-private-hand", "faith-private-library", "faith-private-grave",
        };
        player.Hand.Add(Card("S01-0001", privateIds[0]));
        player.Library.Add(Card("S01-0002", privateIds[1]));
        player.Graveyard.Add(Card("S01-0003", privateIds[2]));

        Invoke(game, "BeginFaithZealotMasterChoice", CompletedFaithItem());

        var prompt = Prompt(game);
        Assert.False(prompt.IsPrivate);
        Assert.Equal(["lokiCycle", "lokiHeal", "skip"], prompt.ValidChoices);
        var publicProjection = prompt.ValidChoices
            .Concat(prompt.Data.Keys).Concat(prompt.Data.Values).Append(prompt.Text).ToArray();
        Assert.All(privateIds, privateId =>
            Assert.DoesNotContain(publicProjection, value => value.Contains(privateId, StringComparison.Ordinal)));
    }

    [Fact]
    public void FreeMedjedStrongModeNeedsNoMoraleOrTombGuardButKeepsEnemyTargetLegality()
    {
        var game = Create(25301);
        var enemy = Card("S01-0001", "faith-medjed-enemy", troops: 5000);
        game.State.Players[1].Field[0][0] = enemy;

        var faith = OpenFaithChoice(game, "S01-02M3");
        Resolve(game, faith, "medjedDebuff");
        var mode = Prompt(game);
        Assert.Equal(["mode:normal", "mode:strong", "skip"], mode.ValidChoices);
        Resolve(game, mode, "mode:strong");
        Resolve(game, Prompt(game), enemy.InstanceId);
        PassResponses(game);

        Assert.Equal(2000, enemy.Troops);
        Assert.Empty(game.State.Players[0].Morale);
        Assert.Empty(game.State.Players[0].UsedAbilities);
        Assert.Null(game.State.FreeMasterActivation);
    }

    [Fact]
    public void FreeLokiHealWaivesBothMoraleAndGraveReturnCost()
    {
        var game = Create(25302);
        var player = game.State.Players[0];
        player.Hp = 5;

        Resolve(game, OpenFaithChoice(game, "S01-03M2"), "lokiHeal");
        PassResponses(game);

        Assert.Equal(6, player.Hp);
        Assert.Empty(player.Graveyard);
        Assert.Empty(player.Library);
        Assert.Empty(player.Morale);
        Assert.Empty(player.UsedAbilities);
        Assert.Null(game.State.FreeMasterActivation);
    }

    [Fact]
    public void FreeLokiCycleWaivesTheCostButStillPerformsItsPrintedDrawThenDiscard()
    {
        var game = Create(253021);
        var player = game.State.Players[0];
        var drawn = Card("S01-0001", "faith-loki-cycle-draw");
        player.Library.Add(drawn);

        Resolve(game, OpenFaithChoice(game, "S01-03M2"), "lokiCycle");
        PassResponses(game);
        var discard = Prompt(game);
        Assert.True(discard.IsPrivate);
        Assert.Contains(drawn.InstanceId, discard.ValidChoices);
        Resolve(game, discard, drawn.InstanceId);

        Assert.Contains(drawn, player.Graveyard);
        Assert.Empty(player.Hand);
        Assert.Empty(player.Morale);
        Assert.DoesNotContain("active:master-0:loki", player.UsedAbilities);
    }

    [Fact]
    public void FreeValkyrieWaivesMoraleAndSelfDamageButKeepsBothGraveSelections()
    {
        var game = Create(25303);
        var player = game.State.Players[0];
        player.Hp = 1;
        var toHand = Card("S01-0001", "faith-valkyrie-hand");
        var toBottom = Card("S01-0002", "faith-valkyrie-bottom");
        player.Graveyard.AddRange([toHand, toBottom]);

        Resolve(game, OpenFaithChoice(game, "S01-03M1"), "valkyrieRecover");
        ResolveMany(game, Prompt(game), toHand.InstanceId, toBottom.InstanceId);
        Resolve(game, Prompt(game), toHand.InstanceId);
        PassResponses(game);

        Assert.Equal(1, player.Hp);
        Assert.Contains(toHand, player.Hand);
        Assert.Equal(toBottom, Assert.Single(player.Library));
        Assert.Empty(player.Morale);
        Assert.Empty(player.UsedAbilities);
    }

    [Fact]
    public void FreeAmaterasuKeepsDeclaredSegmentsAndCanKillTheJustDebuffedTarget()
    {
        var game = Create(25304);
        var player = game.State.Players[0];
        var enemyPlayer = game.State.Players[1];
        var enemy = Card("S01-0001", "faith-amaterasu-enemy", cost: 1);
        enemyPlayer.Field[0][0] = enemy;

        Resolve(game, OpenFaithChoice(game, "S01-04M1"), "amaterasuKill");
        Resolve(game, Prompt(game), enemy.InstanceId);
        var kill = Prompt(game);
        Assert.Contains(enemy.InstanceId, kill.ValidChoices);
        Resolve(game, kill, enemy.InstanceId);
        PassResponses(game);

        Assert.Contains(enemy, enemyPlayer.Graveyard);
        Assert.DoesNotContain(enemy, enemyPlayer.Field.SelectMany(row => row).OfType<L12CardInstance>());
        Assert.Empty(player.Morale);
        Assert.Empty(player.UsedAbilities);
    }

    [Fact]
    public void FreeYomiRunsAllFourCompositeSegmentsWithoutMoraleOrOnceUsage()
    {
        var game = Create(25305);
        var player = game.State.Players[0];
        var enemyPlayer = game.State.Players[1];
        var draw = Card("S01-0003", "faith-yomi-draw");
        var killThree = Card("S02-0004", "faith-yomi-three", cost: 4);
        var killOne = Card("S02-0005", "faith-yomi-one", cost: 2);
        player.Library.Add(draw);
        enemyPlayer.Field[0][0] = killThree;
        enemyPlayer.Field[0][1] = killOne;

        Resolve(game, OpenFaithChoice(game, "S01-04D1"), "yomiSweep");
        Resolve(game, Prompt(game), killThree.InstanceId);
        Resolve(game, Prompt(game), killOne.InstanceId);
        PassResponses(game, maximum: 20);

        Assert.Contains(draw, player.Hand);
        Assert.Contains(killThree, enemyPlayer.Graveyard);
        Assert.Contains(killOne, enemyPlayer.Graveyard);
        Assert.Empty(player.Morale);
        Assert.Empty(player.UsedAbilities);
    }

    [Fact]
    public void FreeSunBottomEnemyKeepsTroopAndPublicTargetLegalityWithoutChargingMorale()
    {
        var game = Create(253051, firstFactionIndex: 2);
        var player = game.State.Players[0];
        var enemyPlayer = game.State.Players[1];
        var legal = Card("S01-0001", "faith-sun-bottom-legal", troops: 4000);
        var tooLarge = Card("S01-0002", "faith-sun-bottom-too-large", troops: 5000);
        enemyPlayer.Field[0][0] = legal;
        enemyPlayer.Field[0][1] = tooLarge;

        Resolve(game, OpenFaithChoice(game, "S01-02D1"), "sunBottomEnemy");
        var target = Prompt(game);
        Assert.Contains(legal.InstanceId, target.ValidChoices);
        Assert.DoesNotContain(tooLarge.InstanceId, target.ValidChoices);
        Resolve(game, target, legal.InstanceId);
        PassResponses(game);

        Assert.Equal(legal, Assert.Single(enemyPlayer.Library));
        Assert.Same(tooLarge, enemyPlayer.Field[0][1]);
        Assert.Empty(player.Morale);
        Assert.DoesNotContain("active:master-0:sunBottomEnemy", player.UsedAbilities);
    }

    [Fact]
    public void FreeSusanoFrontBuffKeepsFactionTargetAndOnlyWritesItsEffectStateMarker()
    {
        var game = Create(253052, firstFactionIndex: 1);
        var player = game.State.Players[0];
        var legal = Card("S01-0401", "faith-susano-buff-legal");
        var wrongFaction = Card("S01-0201", "faith-susano-buff-wrong");
        player.Field[0][0] = legal;
        player.Field[0][1] = wrongFaction;

        Resolve(game, OpenFaithChoice(game, "S01-04M2"), "frontBuff");
        var target = Prompt(game);
        Assert.Contains(legal.InstanceId, target.ValidChoices);
        Assert.DoesNotContain(wrongFaction.InstanceId, target.ValidChoices);
        Resolve(game, target, legal.InstanceId);
        PassResponses(game);

        Assert.Contains($"susano-buff:{legal.InstanceId}", player.UsedAbilities);
        Assert.DoesNotContain("active:master-0:frontBuff", player.UsedAbilities);
        Assert.Empty(player.Morale);
    }

    [Fact]
    public void FreeSusanoKusanagiKeepsRelicAndFrontSlotLegalityWithoutChargingMorale()
    {
        var game = Create(253053, firstFactionIndex: 1);
        var player = game.State.Players[0];
        var sword = Card("S01-0417", "faith-susano-sword");
        player.Relic = sword;

        Resolve(game, OpenFaithChoice(game, "S01-04M2"), "kusanagi");
        var slot = Prompt(game);
        Assert.Contains("0:0", slot.ValidChoices);
        Assert.DoesNotContain("1:0", slot.ValidChoices);
        Resolve(game, slot, "0:0");
        PassResponses(game);

        Assert.Null(player.Relic);
        Assert.Same(sword, player.Field[0][0]);
        Assert.Empty(player.Morale);
        Assert.DoesNotContain("active:master-0:kusanagi", player.UsedAbilities);
    }

    [Theory]
    [InlineData("S01-02D1", "sunTopThree", 2, "sun-top-three-search", "sun-top-three-recover")]
    [InlineData("S01-03D1", "valhallaRecover", 3, "valhalla-mill", "valhalla-recover")]
    public void RingModifiedDivinityRecoveryKeepsEffectiveFactionThroughNormalCommitAndResolution(
        string masterId, string ability, int factionIndex, string firstFlow, string recoveryFlow)
    {
        var (game, player, recover, wrongFaction) = RingRecoveryFixture(
            masterId, factionIndex, seed: 25310 + factionIndex, withMorale: true);

        var started = game.Handle(0,
            new L12Command("activateAbility", CardInstanceId: "master-0", Ability: ability));
        Assert.True(started.Accepted, started.Error);
        var declaration = Prompt(game);
        Assert.Contains(recover.InstanceId, declaration.ValidChoices);
        Assert.DoesNotContain(wrongFaction.InstanceId, declaration.ValidChoices);
        AssertInvalidChoiceLeavesDeclarationOpen(game, declaration, wrongFaction.InstanceId);
        Resolve(game, declaration, recover.InstanceId);

        SettleRecoveryWithNegatedFirstSegment(game, firstFlow, recoveryFlow);

        Assert.Contains(recover, player.Hand);
        Assert.Contains(wrongFaction, player.Graveyard);
        Assert.Contains($"active:master-0:{ability}", player.UsedAbilities);
    }

    [Theory]
    [InlineData("S01-02D1", "sunTopThree", 2, "sun-top-three-search", "sun-top-three-recover")]
    [InlineData("S01-03D1", "valhallaRecover", 3, "valhalla-mill", "valhalla-recover")]
    public void RingModifiedDivinityRecoveryKeepsEffectiveFactionThroughFaithCommitAndResolution(
        string masterId, string ability, int factionIndex, string firstFlow, string recoveryFlow)
    {
        var (game, player, recover, wrongFaction) = RingRecoveryFixture(
            masterId, factionIndex, seed: 25320 + factionIndex, withMorale: false);

        Resolve(game, OpenFaithChoice(game, masterId), ability);
        var declaration = Prompt(game);
        Assert.Contains(recover.InstanceId, declaration.ValidChoices);
        Assert.DoesNotContain(wrongFaction.InstanceId, declaration.ValidChoices);
        AssertInvalidChoiceLeavesDeclarationOpen(game, declaration, wrongFaction.InstanceId);
        Resolve(game, declaration, recover.InstanceId);

        SettleRecoveryWithNegatedFirstSegment(game, firstFlow, recoveryFlow);

        Assert.Contains(recover, player.Hand);
        Assert.Contains(wrongFaction, player.Graveyard);
        Assert.Empty(player.Morale);
        Assert.DoesNotContain($"active:master-0:{ability}", player.UsedAbilities);
    }

    [Fact]
    public void CancellingAFreeTargetDeclarationResumesThePostResolutionChain()
    {
        var game = Create(25306);
        var player = game.State.Players[0];
        var own = Card("S01-0401", "faith-susano-cancel");
        player.Field[0][0] = own;
        game.State.EffectStack.Add(UnderlyingStackItem("faith-cancel-underlying"));

        Resolve(game, OpenFaithChoice(game, "S01-04M2"), "frontBuff");
        var declaration = Prompt(game);
        Assert.True(declaration.IsPrivate);
        Resolve(game, declaration, "skip");

        Assert.Null(game.State.FreeMasterActivation);
        Assert.Empty(game.State.PendingActivations);
        Assert.Contains(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Empty(player.UsedAbilities);
        PassResponses(game);
        Assert.Empty(game.State.EffectStack);
        Assert.False(game.State.IsResolvingStack);
    }

    [Fact]
    public void InvalidatedFreeSlotDoesNotMoveOrChargeAndResumesThePostResolutionChain()
    {
        var game = Create(25307);
        var player = game.State.Players[0];
        var sword = Card("S01-0417", "faith-kusanagi-relic");
        var blocker = Card("S01-0001", "faith-kusanagi-blocker");
        player.Relic = sword;
        game.State.EffectStack.Add(UnderlyingStackItem("faith-invalid-underlying"));

        Resolve(game, OpenFaithChoice(game, "S01-04M2"), "kusanagi");
        var slot = Prompt(game);
        Assert.Contains("0:0", slot.ValidChoices);
        player.Field[0][0] = blocker;
        Resolve(game, slot, "0:0");

        Assert.Same(sword, player.Relic);
        Assert.Same(blocker, player.Field[0][0]);
        Assert.Null(game.State.FreeMasterActivation);
        Assert.Contains(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Empty(player.Morale);
        Assert.Empty(player.UsedAbilities);
        PassResponses(game);
        Assert.Empty(game.State.EffectStack);
        Assert.False(game.State.IsResolvingStack);
    }

    [Fact]
    public void ReconnectReconciliationClosesAnOrphanedFreeDeclaration()
    {
        var game = Create(25308);
        var player = game.State.Players[0];
        player.Field[0][0] = Card("S01-0401", "faith-susano-reconnect");
        game.State.EffectStack.Add(UnderlyingStackItem("faith-reconnect-underlying"));

        Resolve(game, OpenFaithChoice(game, "S01-04M2"), "frontBuff");
        Assert.Single(game.State.PendingActivations);
        game.State.PendingPrompts.Clear();

        _ = game.SnapshotFor(0);

        Assert.Null(game.State.FreeMasterActivation);
        Assert.Empty(game.State.PendingActivations);
        Assert.Contains(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Contains(game.State.Events, entry => entry.Type == "ability-rejected"
            && entry.Text.Contains("已安全取消", StringComparison.Ordinal));
        PassResponses(game);
        Assert.Empty(game.State.EffectStack);
        Assert.False(game.State.IsResolvingStack);
    }

    [Fact]
    public void DuplicateFaithChoiceCannotCreateASecondFreeEffectOrChargeResources()
    {
        var game = Create(25309);
        var player = game.State.Players[0];
        player.Hp = 3;
        player.Morale.Add(new L12MoraleCard { CardId = "S01-03C1", InstanceId = "faith-thor-morale" });
        var faith = OpenFaithChoice(game, "S02-03M1");

        Resolve(game, faith, "thorCharge");
        var duplicate = game.Handle(0,
            new L12Command("resolvePrompt", PromptId: faith.PromptId, Choice: "thorCharge"));
        Assert.False(duplicate.Accepted);
        PassResponses(game);

        Assert.False(Assert.Single(player.Morale).Tapped);
        Assert.DoesNotContain("active:master-0:thorCharge", player.UsedAbilities);
        Assert.Single(game.State.Events,
            entry => entry.Type == "effect"
                && entry.Text.Contains("无视全部消耗", StringComparison.Ordinal));
        Assert.True(player.MasterCannotHeal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OtherworldFactionGainRuneRequiresAnExplicitTemporaryOrOrdinaryPaymentChoice(
        bool payWithTemporaryMorale)
    {
        var game = Create(payWithTemporaryMorale ? 25330 : 25331, firstFactionIndex: 5);
        var player = game.State.Players[0];
        var ordinaryOne = new L12MoraleCard
        {
            CardId = "S02-06C1", InstanceId = "faith-audit-otherworld-ordinary-1",
        };
        var ordinaryTwo = new L12MoraleCard
        {
            CardId = "S02-06C1", InstanceId = "faith-audit-otherworld-ordinary-2",
        };
        player.Morale.AddRange([ordinaryOne, ordinaryTwo]);
        player.TemporaryMorale = 2;

        var started = game.Handle(0,
            new L12Command("activateAbility", "faction-0", Ability: "factionGainRune"));

        Assert.True(started.Accepted, started.Error);
        var payment = Prompt(game);
        Assert.Equal("resource-payment", payment.Kind);
        Assert.True(payment.IsPrivate);
        Assert.Contains("temporary-morale:1", payment.ValidChoices);
        Assert.Contains("temporary-morale:2", payment.ValidChoices);
        Assert.Contains(ordinaryOne.InstanceId, payment.ValidChoices);
        Assert.Contains(ordinaryTwo.InstanceId, payment.ValidChoices);
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Equal(2, player.TemporaryMorale);
        Assert.False(ordinaryOne.Tapped);
        Assert.False(ordinaryTwo.Tapped);

        ResolveMany(game, payment, payWithTemporaryMorale
            ? ["temporary-morale:1", "temporary-morale:2"]
            : [ordinaryOne.InstanceId, ordinaryTwo.InstanceId]);
        PassResponses(game);

        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.Equal(payWithTemporaryMorale ? 0 : 2, player.TemporaryMorale);
        Assert.Equal(!payWithTemporaryMorale, ordinaryOne.Tapped);
        Assert.Equal(!payWithTemporaryMorale, ordinaryTwo.Tapped);
        Assert.Contains("active:faction-0:factionGainRune", player.UsedAbilities);
    }

    [Fact]
    public void OtherworldFactionGainRuneRejectsDuplicateTemporaryPaymentWithoutChargingAnything()
    {
        var game = Create(25332, firstFactionIndex: 5);
        var player = game.State.Players[0];
        player.TemporaryMorale = 2;
        player.Morale.Add(new L12MoraleCard
        {
            CardId = "S02-06C1", InstanceId = "faith-audit-otherworld-duplicate-control",
        });

        var started = game.Handle(0,
            new L12Command("activateAbility", "faction-0", Ability: "factionGainRune"));
        Assert.True(started.Accepted, started.Error);
        var payment = Prompt(game);

        var duplicate = game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId,
            CardInstanceIds: ["temporary-morale:1", "temporary-morale:1"]));

        Assert.False(duplicate.Accepted);
        Assert.Same(payment, Prompt(game));
        Assert.Equal(2, player.TemporaryMorale);
        Assert.False(Assert.Single(player.Morale).Tapped);
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.DoesNotContain("active:faction-0:factionGainRune", player.UsedAbilities);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void HorusAvailabilityCountsTemporaryMoraleAsAnOrdinaryPaymentResource(
        int temporaryMorale, bool expectedEnabled)
    {
        var game = Create(25340 + temporaryMorale);
        var player = game.State.Players[0];
        SetMaster(player, "ST02-M1");
        player.TemporaryMorale = temporaryMorale;
        player.Field[0][0] = Card("ST01-01", $"faith-audit-horus-field-a-{temporaryMorale}");
        player.Field[0][1] = Card("ST01-02", $"faith-audit-horus-field-b-{temporaryMorale}");
        player.Graveyard.Add(Card("ST02-07", $"faith-audit-horus-grave-{temporaryMorale}"));

        var abilities = Assert.IsType<List<L12AbilityView>>(
            Invoke(game, "BuildAbilityViews", player, "ST02-M1", "master-0"));
        var view = Assert.Single(abilities, ability => ability.Id == "horusRevive");

        Assert.Equal(expectedEnabled, view.Enabled);
        var started = game.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "horusRevive"));
        Assert.Equal(expectedEnabled, started.Accepted);
        if (!expectedEnabled)
        {
            Assert.Empty(game.State.PendingPrompts);
            Assert.Equal(0, player.TemporaryMorale);
            return;
        }

        // 唯一可用的临时士气会被声明器自动锁定；流程应直接进入两张战场费用的选择，
        // 而不是在能力投影层误判为不可发动。
        var fieldCost = Prompt(game);
        Assert.Equal("active-target", fieldCost.Kind);
        Assert.Equal("board-target", fieldCost.Data.GetValueOrDefault("choiceMode"));
        Assert.Contains(player.Field[0][0]!.InstanceId, fieldCost.ValidChoices);
        Assert.Contains(player.Field[0][1]!.InstanceId, fieldCost.ValidChoices);
        Assert.Equal(1, player.TemporaryMorale);
    }

    private static void AssertFaithChoiceSet(string masterId, string[] expected)
    {
        var game = Create(DeterministicSeed(masterId));
        SetMaster(game.State.Players[0], masterId);
        game.State.IsResolvingStack = false;
        Invoke(game, "BeginFaithZealotMasterChoice", CompletedFaithItem());

        if (expected.Length == 0)
        {
            Assert.Empty(game.State.PendingPrompts);
            Assert.False(game.State.IsResolvingStack);
            return;
        }

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.False(prompt.IsPrivate);
        Assert.Equal("faith-zealot-post-resolution", prompt.Continuation);
        Assert.Equal(expected.Append("skip"), prompt.ValidChoices);
        Assert.All(expected, ability => Assert.True(prompt.Data.ContainsKey(ability)));
    }

    private static L12GameEngine Create(int seed, int firstFactionIndex = 0)
    {
        var game = new L12GameEngine(Catalog, "faith-zealot-audit", "FAITH253", seed,
            ["甲", "乙"], [firstFactionIndex, 1], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        game.State.PendingPrompts.Clear();
        game.State.PendingActivations.Clear();
        game.State.EffectStack.Clear();
        game.State.ResponseWindow = null;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
            player.UsedAbilities.Clear();
        }
        return game;
    }

    private static int DeterministicSeed(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in value) hash = hash * 31 + character;
            return hash & int.MaxValue;
        }
    }

    private static (L12GameEngine Game, L12PlayerState Player, L12CardInstance Recover,
        L12CardInstance WrongFaction) RingRecoveryFixture(
        string masterId, int factionIndex, int seed, bool withMorale)
    {
        var game = Create(seed, factionIndex);
        var player = game.State.Players[0];
        SetMaster(player, masterId);
        player.ExtraRelics.Add(Card("S02-0008", $"faith-ring-{seed}"));
        var recover = Card("S01-0001", $"faith-ring-universal-{seed}");
        var wrongFaction = Card("S01-0401", $"faith-ring-wrong-{seed}");
        player.Graveyard.AddRange([recover, wrongFaction]);
        player.Library.AddRange([
            Card("S01-0002", $"faith-ring-library-a-{seed}"),
            Card("S01-0003", $"faith-ring-library-b-{seed}"),
            Card("S01-0004", $"faith-ring-library-c-{seed}"),
        ]);
        if (withMorale)
        {
            player.Morale.AddRange(Enumerable.Range(0, 2).Select(index => new L12MoraleCard
            {
                CardId = masterId == "S01-02D1" ? "S01-02C1" : "S01-03C1",
                InstanceId = $"faith-ring-morale-{seed}-{index}",
            }));
        }
        return (game, player, recover, wrongFaction);
    }

    private static void AssertInvalidChoiceLeavesDeclarationOpen(
        L12GameEngine game, L12Prompt declaration, string invalidChoice)
    {
        var invalid = game.Handle(declaration.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: declaration.PromptId, Choice: invalidChoice));
        Assert.False(invalid.Accepted);
        Assert.Same(declaration, Assert.Single(game.State.PendingPrompts));
    }

    private static void SettleRecoveryWithNegatedFirstSegment(
        L12GameEngine game, string firstFlow, string recoveryFlow)
    {
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal(firstFlow, first.Data.GetValueOrDefault("atomicFlow"));
        first.Negated = true;
        _ = PassUntilFlow(game, recoveryFlow);
        PassResponses(game, maximum: 20);
    }

    private static L12StackItem PassUntilFlow(L12GameEngine game, string flow)
    {
        for (var safety = 0; safety < 20; safety++)
        {
            var item = game.State.EffectStack.FirstOrDefault(candidate =>
                candidate.Data.GetValueOrDefault("atomicFlow") == flow);
            if (item is not null) return item;
            var prompt = Prompt(game);
            Assert.Equal("response", prompt.Kind);
            Resolve(game, prompt, "pass");
        }
        throw new Xunit.Sdk.XunitException($"未进入预期效果段 {flow}");
    }

    private static L12Prompt OpenFaithChoice(L12GameEngine game, string masterId)
    {
        SetMaster(game.State.Players[0], masterId);
        // FinishStackItem 在建立这个结算后互动前会先释放正在结算标记。
        game.State.IsResolvingStack = false;
        Invoke(game, "BeginFaithZealotMasterChoice", CompletedFaithItem());
        var prompt = Prompt(game);
        Assert.Equal("faith-zealot-post-resolution", prompt.Continuation);
        return prompt;
    }

    private static L12StackItem CompletedFaithItem() => new()
    {
        StackItemId = "completed-faith",
        Controller = 0,
        SourceInstanceId = "faith-zealot-source",
        SourceCardId = "S02-0006",
        SourceName = "信仰狂热者",
        Trigger = "discarded-by-effect",
        Text = "信仰狂热者触发",
    };

    private static L12StackItem UnderlyingStackItem(string instanceId)
    {
        var source = Card("S02-0004", instanceId);
        var item = new L12StackItem
        {
            StackItemId = $"stack-{instanceId}",
            Controller = 0,
            SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId,
            SourceName = source.Name,
            SourceSnapshot = source,
            Trigger = "active",
            Text = "用于验证结算后互动恢复的底层堆叠项",
        };
        item.Data["ability"] = "faith-resume-probe";
        return item;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int? troops = null, int? cost = null)
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
            Cost = cost ?? definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            SummonRound = -1,
        };
    }

    private static void SetMaster(L12PlayerState player, string cardId)
    {
        typeof(L12PlayerState).GetProperty(nameof(L12PlayerState.MasterId))!.SetValue(player, cardId);
        typeof(L12PlayerState).GetProperty(nameof(L12PlayerState.MasterName))!
            .SetValue(player, Catalog.Cards[cardId].NameZh);
    }

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == args.Length);
        return method.Invoke(target, args);
    }

    private static L12Prompt Prompt(L12GameEngine game) => Assert.Single(game.State.PendingPrompts);

    private static void Resolve(L12GameEngine game, L12Prompt prompt, string choice)
    {
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void ResolveMany(L12GameEngine game, L12Prompt prompt, params string[] choices)
    {
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: choices.ToList()));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game, int maximum = 12)
    {
        var count = 0;
        while (game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt && count++ < maximum)
            Resolve(game, prompt, "pass");
        Assert.True(count < maximum, "响应窗口未在限定次数内结束");
    }
}
