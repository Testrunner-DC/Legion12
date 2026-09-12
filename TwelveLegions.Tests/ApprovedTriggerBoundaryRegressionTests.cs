using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ApprovedTriggerBoundaryRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, bool autoPassEmptyResponses = true)
    {
        var game = new L12GameEngine(Catalog, "approved-trigger-boundaries", "APPROVED", seed,
            ["甲", "乙"], [0, 1], skipPreparation: true,
            autoPassEmptyResponses: autoPassEmptyResponses,
            concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Phase = L12Phase.Main;
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId)
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
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
        };
    }

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    [Fact]
    public void NaturalDisasterNeverOffersAPlacedCounterTacticResponse()
    {
        var game = Create(12001, autoPassEmptyResponses: false);
        var counter = Card("S01-0019", "disaster-counter");
        counter.Hidden = true;
        game.State.Players[1].Field[1][0] = counter;
        game.State.Players[1].Field[0][0] = Card("S01-0001", "counter-buff-target");
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(Card("S01-DS10", "natural-disaster"));

        Invoke(game, "BeginDisasterTrigger", false, false);

        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Empty(game.State.EffectStack);
        Assert.Same(counter, game.State.Players[1].Field[1][0]);
    }

    [Fact]
    public void AuthorityDisasterAlsoSuppressesCountersButOrdinaryEnterStillOffersThem()
    {
        var authority = Create(12002, autoPassEmptyResponses: false);
        var authorityCounter = Card("S01-0019", "authority-disaster-counter");
        authorityCounter.Hidden = true;
        authority.State.Players[1].Field[1][0] = authorityCounter;
        authority.State.Players[1].Field[0][0] = Card("S01-0001", "authority-buff-target");
        var disaster = Card("S01-DS10", "authority-disaster");

        Invoke(authority, "PushEffect", 0, disaster, "authority-event", "权威天灾事件", null,
            new Dictionary<string, string> { ["eventType"] = "authority-disaster" });

        Assert.DoesNotContain(authority.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Empty(authority.State.EffectStack);
        Assert.Same(authorityCounter, authority.State.Players[1].Field[1][0]);

        var ordinary = Create(12003, autoPassEmptyResponses: false);
        var ordinaryCounter = Card("S01-0019", "ordinary-enter-counter");
        ordinaryCounter.Hidden = true;
        ordinary.State.Players[1].Field[1][0] = ordinaryCounter;
        ordinary.State.Players[1].Field[0][0] = Card("S01-0001", "ordinary-buff-target");
        var entering = Card("S01-0104", "ordinary-enter-source");
        ordinary.State.Players[0].Field[0][0] = entering;

        Invoke(ordinary, "PushEffect", 0, entering, "enter", "普通登场效果", null, null);
        var firstPriority = Assert.Single(ordinary.State.PendingPrompts);
        Assert.True(ordinary.Handle(0, new L12Command("resolvePrompt", PromptId: firstPriority.PromptId,
            Choice: "pass")).Accepted);
        var opponentResponse = Assert.Single(ordinary.State.PendingPrompts);
        Assert.Contains(ordinaryCounter.InstanceId, opponentResponse.ValidChoices);
    }

    [Fact]
    public void MiyamotoWithFalseDrawConditionCreatesNoOptionalTransactionOrTechnicalLog()
    {
        var game = Create(12004);
        var miyamoto = Card("S01-0405", "miyamoto-false-condition");
        miyamoto.SummonRound = -1;
        game.State.Players[0].Field[0][0] = miyamoto;
        game.State.Players[0].Hand.Clear();
        game.State.Players[1].Hand.Clear();
        game.State.Players[0].Hand.Add(Card("S01-0001", "miyamoto-own-a"));
        game.State.Players[0].Hand.Add(Card("S01-0002", "miyamoto-own-b"));
        game.State.Players[1].Hand.Add(Card("S01-0003", "miyamoto-opponent"));

        var attack = game.Handle(0, new L12Command("attack", miyamoto.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.True(attack.Accepted, attack.Error);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        Assert.DoesNotContain(game.State.Events, entry => entry.Type is "ability-cancelled" or "ability-rejected");
        Assert.Equal(L12CombatStage.DefenseChoice, game.State.PendingDefense?.Stage);
    }

    [Fact]
    public void ReconnectSilentlyConvergesAnAttackDeclarationWhoseSourceLeftPlay()
    {
        var game = Create(12005);
        var miyamoto = Card("S01-0405", "miyamoto-left-play");
        miyamoto.SummonRound = -1;
        game.State.Players[0].Field[0][0] = miyamoto;
        game.State.Players[0].Hand.Clear();
        game.State.Players[1].Hand.Clear();
        game.State.Players[1].Hand.Add(Card("S01-0001", "miyamoto-opponent-hand"));
        game.State.Players[0].Library.Insert(0, Card("S01-0002", "miyamoto-draw"));
        Assert.True(game.Handle(0, new L12Command("attack", miyamoto.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        Assert.Single(game.State.PendingActivations);

        game.State.Players[0].Field[0][0] = null;
        game.State.Players[0].Graveyard.Add(miyamoto);
        _ = game.SnapshotFor(0);

        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        Assert.DoesNotContain(game.State.Events, entry => entry.Type is "ability-cancelled" or "ability-rejected");
        Assert.Null(game.State.PendingDefense);
        Assert.True(game.Handle(0, new L12Command("endTurn")).Accepted);
    }

    [Fact]
    public void InvalidatedResponseDeclarationLeavesNoPromptActivationOrResponseWindow()
    {
        var game = Create(12006, autoPassEmptyResponses: false);
        var source = Card("S01-0104", "response-root-source");
        game.State.Players[0].Field[0][0] = source;
        var response = Card("S01-0019", "pending-response-counter");
        response.Hidden = true;
        game.State.Players[1].Field[1][0] = response;
        game.State.Players[1].Field[0][0] = Card("S01-0001", "pending-response-target");
        Invoke(game, "PushEffect", 0, source, "enter", "普通登场效果", null, null);
        var firstPriority = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: firstPriority.PromptId,
            Choice: "pass")).Accepted);
        var responsePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: responsePrompt.PromptId,
            Choice: response.InstanceId)).Accepted);
        Assert.Single(game.State.PendingActivations);

        game.State.EffectStack.Clear();
        _ = game.SnapshotFor(1);

        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Null(game.State.ResponseWindow);
    }

    [Fact]
    public void CardDisasterAdjustmentSchedulesExactlyOneRevealAfterTheCurrentStackCloses()
    {
        var game = Create(12007);
        game.State.DisasterValue = 7;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(Card("S01-DS10", "scheduled-first-disaster"));
        game.State.DisasterDeck.Add(Card("S01-DS01", "must-remain-in-deck"));
        var counter = Card("S01-0019", "scheduled-disaster-counter");
        counter.Hidden = true;
        game.State.Players[1].Field[1][0] = counter;
        game.State.Players[1].Field[0][0] = Card("S01-0001", "scheduled-disaster-buff-target");
        game.State.EffectStack.Add(new L12StackItem
        {
            StackItemId = "holding-stack",
            Controller = 0,
            SourceInstanceId = "holding-source",
            SourceCardId = "S01-0111",
            SourceName = "诸葛亮",
            Trigger = "enter",
            Text = "当前卡效尚未收束",
        });

        Invoke(game, "AdjustDisasterValue", 2, 0, "测试调整至 {value}");
        Assert.Equal(9, game.State.DisasterValue);
        Assert.True(game.State.CheckDisasterAfterStack);
        Invoke(game, "AfterStackSettled");
        Assert.Null(game.State.ActiveDisaster);

        game.State.EffectStack.Clear();
        Invoke(game, "AfterStackSettled");

        Assert.Equal("scheduled-first-disaster", game.State.ActiveDisaster?.InstanceId);
        Assert.Equal(0, game.State.DisasterValue);
        Assert.False(game.State.CheckDisasterAfterStack);
        Assert.Single(game.State.DisasterDeck);
        Assert.Same(counter, game.State.Players[1].Field[1][0]);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Invoke(game, "AfterStackSettled");
        Assert.Single(game.State.DisasterDeck);
    }

    [Fact]
    public void DisasterValueEightDoesNotCrossTheAuthoritativeGreaterThanEightThreshold()
    {
        var game = Create(12008);
        game.State.DisasterValue = 7;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(Card("S01-DS10", "threshold-disaster"));

        Invoke(game, "AdjustDisasterValue", 1, 0, "测试调整至 {value}");
        Invoke(game, "AfterStackSettled");

        Assert.Equal(8, game.State.DisasterValue);
        Assert.False(game.State.CheckDisasterAfterStack);
        Assert.Null(game.State.ActiveDisaster);
        Assert.Single(game.State.DisasterDeck);
    }

    [Fact]
    public void EveryOptionalAttackPublicPlanSilentlyRejectsMissingSourceAndInvalidDeclarations()
    {
        var plans = Assert.IsAssignableFrom<System.Collections.IEnumerable>(typeof(L12GameEngine)
            .GetField("AttackPublicTriggerPlans", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        var createCandidate = typeof(L12GameEngine).GetMethod("CreateTriggerCandidate",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var queueCandidates = typeof(L12GameEngine).GetMethod("QueueTriggerCandidates",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var canDeclare = typeof(L12GameEngine).GetMethod("CanDeclareAttackPlan",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var optionalCount = 0;
        var invalidConditionCount = 0;

        foreach (var entry in plans)
        {
            var entryType = entry!.GetType();
            var cardId = (string)entryType.GetProperty("Key")!.GetValue(entry)!;
            var plan = entryType.GetProperty("Value")!.GetValue(entry)!;
            if (!(bool)plan.GetType().GetProperty("Optional")!.GetValue(plan)!) continue;
            optionalCount++;
            var planId = (string)plan.GetType().GetProperty("PlanId")!.GetValue(plan)!;

            var missingSource = Create(13000 + optionalCount);
            var source = Card(cardId, $"optional-missing-source-{cardId}");
            var candidate = Assert.IsType<L12TriggerCandidate>(createCandidate.Invoke(missingSource,
                [0, source, "attack", "框架级可选进攻触发", new Dictionary<string, string>
                    { ["attackPlan"] = planId }, source]));
            queueCandidates.Invoke(missingSource, [(object)new[] { candidate }]);
            Assert.Empty(missingSource.State.PendingActivations);
            Assert.Empty(missingSource.State.PendingTriggerStackCandidates);
            Assert.DoesNotContain(missingSource.State.PendingPrompts,
                prompt => prompt.Continuation == "pending-activation");
            Assert.DoesNotContain(missingSource.State.Events,
                item => item.Type is "ability-cancelled" or "ability-rejected");
            Assert.True(missingSource.Handle(0, new L12Command("endTurn")).Accepted);

            var hostile = Create(14000 + optionalCount);
            hostile.State.Players[0].Hand.Clear();
            hostile.State.Players[0].Library.Clear();
            hostile.State.Players[0].Graveyard.Clear();
            hostile.State.Players[0].Morale.Clear();
            hostile.State.Players[0].TemporaryMorale = 0;
            hostile.State.Players[0].SpecialZones.Runes = 0;
            hostile.State.Players[0].Hp = 1;
            hostile.State.Players[1].Hand.Clear();
            for (var row = 0; row < 2; row++)
                for (var slot = 0; slot < 3; slot++)
                    hostile.State.Players[1].Field[row][slot] = null;
            var fieldSource = Card(cardId, $"optional-hostile-source-{cardId}");
            hostile.State.Players[0].Field[0][0] = fieldSource;
            var hostileCandidate = Assert.IsType<L12TriggerCandidate>(createCandidate.Invoke(hostile,
                [0, fieldSource, "attack", "框架级无候选进攻触发", new Dictionary<string, string>
                    { ["attackPlan"] = planId }, fieldSource]));
            if ((bool)canDeclare.Invoke(hostile, [hostileCandidate, plan, fieldSource])!) continue;
            invalidConditionCount++;
            queueCandidates.Invoke(hostile, [(object)new[] { hostileCandidate }]);
            Assert.Empty(hostile.State.PendingActivations);
            Assert.Empty(hostile.State.PendingTriggerStackCandidates);
            Assert.DoesNotContain(hostile.State.PendingPrompts,
                prompt => prompt.Continuation == "pending-activation");
        }

        Assert.True(optionalCount >= 20, $"仅扫描到 {optionalCount} 个可选进攻公开计划");
        Assert.True(invalidConditionCount >= 15, $"仅构造出 {invalidConditionCount} 个无候选/条件不成立计划");
    }

    [Fact]
    public void EveryDeclineOnlyPublicTriggerEntryPointIsSilentlySkippedByTheFrameworkGuard()
    {
        var cases = new (string CardId, string Trigger, Dictionary<string, string> Data)[]
        {
            ("S02-04M1", "active", new() { ["ability"] = "tsukuyomiFollowMove", ["moved"] = "already-moved" }),
            ("S02-0523", "trojan-after-attack", new() { ["attacker"] = "1" }),
            ("S01-02M3", "medjed-master-damage", new()),
            ("S02-02M1", "nephthys-own-death", new()),
            ("S02-01S1", "master-morale-return", new() { ["mode"] = "xiaotian" }),
            ("S01-0213", "reaction", new()),
            ("S02-0203", "enter", new()),
            ("S02-0205", "enter", new()),
            ("S01-0206", "attack", new()),
            ("S01-0315", "enter", new()),
            ("S01-0407", "enter", new()),
        };
        var createCandidate = typeof(L12GameEngine).GetMethod("CreateTriggerCandidate",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var queueCandidates = typeof(L12GameEngine).GetMethod("QueueTriggerCandidates",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        for (var caseIndex = 0; caseIndex < cases.Length; caseIndex++)
        {
            var entry = cases[caseIndex];
            var game = Create(15000 + caseIndex);
            game.State.Players[0].Hand.Clear();
            game.State.Players[0].Library.Clear();
            game.State.Players[0].Graveyard.Clear();
            game.State.Players[0].Morale.Clear();
            game.State.Players[0].TemporaryMorale = 0;
            game.State.Players[0].SpecialZones.Runes = 0;
            for (var playerIndex = 0; playerIndex < 2; playerIndex++)
            for (var row = 0; row < 2; row++)
            for (var slot = 0; slot < 3; slot++)
                game.State.Players[playerIndex].Field[row][slot] = Card("S01-0001",
                    $"decline-only-blocker-{caseIndex}-{playerIndex}-{row}-{slot}");

            var source = Card(entry.CardId, $"decline-only-source-{caseIndex}");
            if (source.CardType == "legion") game.State.Players[0].Field[0][0] = source;
            var candidate = Assert.IsType<L12TriggerCandidate>(createCandidate.Invoke(game,
                [0, source, entry.Trigger, "框架级仅拒绝项可选触发", entry.Data, source]));
            queueCandidates.Invoke(game, [(object)new[] { candidate }]);

            Assert.Empty(game.State.PendingActivations);
            Assert.Empty(game.State.PendingTriggerStackCandidates);
            Assert.Empty(game.State.PendingTriggerBatches);
            Assert.DoesNotContain(game.State.PendingPrompts,
                prompt => prompt.Continuation is "pending-activation" or "trigger-batch-order");
            Assert.Empty(game.State.EffectStack);
            Assert.DoesNotContain(game.State.Events,
                item => item.Type is "ability-cancelled" or "ability-rejected" or "effect-trigger");
            Assert.True(game.Handle(0, new L12Command("endTurn")).Accepted);
        }

        // 刘备与布伦希尔德的冒号前是可支付 Cost；没有后段登场对象时仍可发动并支付，
        // 因此由冒号 Cost 同类回归覆盖，不能再列入“仅剩不发动选项”的静默守卫样本。
        Assert.Equal(11, cases.Length);
    }

    [Fact]
    public void DeclineOnlyGuardNeverSwallowsAnIndependentMandatoryContinuation()
    {
        var guard = typeof(L12GameEngine).GetMethod("ShouldSilentlySkipUnavailableOptionalTrigger",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var decline = new L12ActivationSelectionStep
        {
            Kind = "option", DeclarationKey = "mode", Text = "不发动",
            ValidChoices = ["mode:none"], MinChoose = 1, MaxChoose = 1,
        };
        var conditional = new L12ActivationSelectionStep
        {
            Kind = "field-legion", DeclarationKey = "target", Text = "条件目标",
            ValidChoices = [], MinChoose = 1, MaxChoose = 1, RequiredDeclaredChoice = "mode:use",
        };
        var mandatory = new L12ActivationSelectionStep
        {
            Kind = "field-legion", DeclarationKey = "mandatory", Text = "独立必发目标",
            ValidChoices = ["target"], MinChoose = 1, MaxChoose = 1,
        };

        Assert.True((bool)guard.Invoke(null,
            [(IReadOnlyList<L12ActivationSelectionStep>)new[] { decline, conditional }])!);
        Assert.False((bool)guard.Invoke(null,
            [(IReadOnlyList<L12ActivationSelectionStep>)new[] { decline, mandatory }])!);
    }
}
