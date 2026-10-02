using System.Reflection;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CounterTacticTriggerConsistencyTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly string[] OrdinaryResponseCounterIds =
    [
        "S01-0016", "S01-0018", "S01-0019", "S01-0020", "S01-0120", "S01-0224",
        "S02-0015", "S02-0016", "S02-0017", "S02-0018", "S02-0106",
    ];

    private static readonly (string CardId, string Trigger)[] TriggeredCounterRoutes =
    [
        ("S01-0017", "reaction"),
        ("S01-0021", "reaction"),
        ("S01-0223", "reaction"),
        ("S01-0320", "reaction"),
        ("S01-0420", "reaction"),
        ("S02-0523", "trojan-after-attack"),
        ("ST01-10", "reaction"),
    ];

    public static IEnumerable<object[]> TriggeredCounters()
        => TriggeredCounterRoutes.Select(route => new object[] { route.CardId, route.Trigger });

    private static L12GameEngine Create(int seed = 91901, string? firstMasterId = null)
    {
        var basis = Catalog.DeckAt(0);
        var firstDeck = firstMasterId is null ? basis : new L12PresetDeckDefinition
        {
            Name = "触发一致性主宰",
            MasterId = firstMasterId,
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "counter-trigger-consistency", "COUNTER-TRIGGER", seed,
            ["甲", "乙"], [firstDeck, basis], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 1;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Resolving.Clear();
            player.UsedAbilities.Clear();
        }
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
            IsCounterTactic = definition.IsCounterTactic,
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
            SummonRound = -1,
        };
    }

    private static L12CardInstance Legion(string instanceId, string faction = "universal", int cost = 3)
        => new()
        {
            InstanceId = instanceId,
            CardId = $"test-{instanceId}",
            Name = instanceId,
            CardType = "legion",
            Faction = faction,
            Cost = cost,
            BaseTroops = 2000,
            Troops = 2000,
            SummonRound = -1,
        };

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    private static IEnumerable<L12TriggerCandidate> QueuedCandidates(L12GameEngine game)
        => game.State.PendingTriggerBatches.SelectMany(batch => batch.Candidates)
            .Concat(game.State.PendingTriggerStackCandidates);

    private static int ActiveSourceCount(L12GameEngine game, string sourceInstanceId)
        => QueuedCandidates(game).Count(candidate => candidate.SourceInstanceId == sourceInstanceId)
            + game.State.EffectStack.Count(item => item.SourceInstanceId == sourceInstanceId)
            + game.State.DeferredEffectStack.Count(item => item.SourceInstanceId == sourceInstanceId);

    private static void QueueSeparateLeaveEvents(L12GameEngine game, params L12CardInstance[] leftCards)
    {
        game.State.IsResolvingStack = true;
        foreach (var left in leftCards)
        {
            var candidates = Assert.IsAssignableFrom<IEnumerable<L12TriggerCandidate>>(
                Invoke(game, "BuildS1LeaveReactionCandidates", 0, left, true));
            Invoke(game, "QueueTriggerCandidates", (object)candidates.ToArray());
        }
        game.State.IsResolvingStack = false;
        Invoke(game, "AdvanceTriggerBatches");
    }

    private static void QueueTrojanAfterAttack(L12GameEngine game, L12CardInstance horse,
        int ownerIndex = 0, int attackerIndex = 1)
    {
        var candidate = Assert.IsType<L12TriggerCandidate>(Invoke(game, "CreateTriggerCandidate",
            ownerIndex, horse, "trojan-after-attack", "【对方进攻后】反击战术",
            new Dictionary<string, string> { ["attacker"] = attackerIndex.ToString() }, horse));
        Invoke(game, "QueueTriggerCandidates", (object)new[] { candidate });
    }

    private static L12Prompt OnlyPrompt(L12GameEngine game)
        => Assert.Single(game.State.PendingPrompts);

    private static void ResolveChoice(L12GameEngine game, string choice)
    {
        var prompt = OnlyPrompt(game);
        var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt",
            PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassAllResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 12 && game.State.PendingPrompts.Count > 0; safety++)
        {
            var prompt = OnlyPrompt(game);
            if (prompt.Kind != "response") return;
            var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt",
                PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static L12TriggerCandidate Candidate(L12CardInstance source, string trigger, string suffix = "candidate")
        => new()
        {
            CandidateId = $"{suffix}-{source.InstanceId}",
            Controller = 0,
            SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId,
            SourceName = source.Name,
            Trigger = trigger,
            Text = "反击战术触发",
            SourceSnapshot = source,
        };

    private static bool LifecycleCheck(L12GameEngine game, string method, L12TriggerCandidate candidate)
        => Assert.IsType<bool>(Invoke(game, method, candidate));

    private static L12GameEngine RestoreAsV2(L12GameEngine game)
    {
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        return L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
    }

    private static L12GameEngine RestoreAsV2WithoutCandidateSourceSnapshot(L12GameEngine game,
        string candidateId)
    {
        var checkpoint = JsonNode.Parse(game.SerializeFullState())!.AsObject();
        checkpoint["StateFormatVersion"] = 2;
        var candidates = checkpoint["PendingTriggerStackCandidates"]!.AsArray()
            .Concat(checkpoint["PendingTriggerBatches"]!.AsArray().SelectMany(batch =>
                batch!.AsObject()["Candidates"]!.AsArray()));
        var legacyCandidate = Assert.Single(candidates, node =>
            node!.AsObject()["CandidateId"]!.GetValue<string>() == candidateId)!.AsObject();
        Assert.True(legacyCandidate.Remove("SourceSnapshot"));
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        return L12GameEngine.RestoreCheckpoint(Catalog, checkpoint.ToJsonString(), random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
    }

    [Fact]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void FullCounterPoolHasElevenOrdinaryResponsesAndSevenTriggeredSetActivations()
    {
        var expected = OrdinaryResponseCounterIds
            .Concat(TriggeredCounterRoutes.Select(route => route.CardId))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var actual = Catalog.Cards.Values.Where(card => card.IsCounterTactic)
            .Select(card => card.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        Assert.Equal(18, actual.Length);
        Assert.Equal(expected, actual);
        Assert.Equal(11, OrdinaryResponseCounterIds.Length);
        Assert.Equal(7, TriggeredCounterRoutes.Length);
    }

    [Fact]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void OrdinaryCounterResponsesKeepTheirExistingResponsePipelineInsteadOfUsingTheFreshTriggerGate()
    {
        var game = Create(91889);
        foreach (var cardId in OrdinaryResponseCounterIds)
        {
            var source = Card(cardId, $"ordinary-response-{cardId}");
            var response = Candidate(source, "response-negate");
            Assert.False(LifecycleCheck(game, "IsFreshSetCounterTacticTrigger", response));
            Assert.True(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", response));
        }

        var defender = game.State.Players[1];
        var absoluteDefense = Card("S01-0016", "ordinary-response-legal-source");
        absoluteDefense.Hidden = true;
        absoluteDefense.OwnerIndex = defender.PlayerIndex;
        defender.Field[1][0] = absoluteDefense;
        defender.Hand.Add(Legion("ordinary-response-discard-cost"));
        game.State.PendingDefense = new L12PendingDefense
        {
            AttackerPlayer = 0,
            AttackerInstanceId = "ordinary-response-attacker",
            Target = new L12AttackTarget("master"),
            Stage = L12CombatStage.AttackerAttackTiming,
        };
        var attack = new L12StackItem
        {
            StackItemId = "ordinary-response-attack",
            Controller = 0,
            SourceInstanceId = "ordinary-response-attacker",
            SourceCardId = "test-attacker",
            SourceName = "进攻军团",
            Trigger = "opponent-attack",
            Text = "进攻",
        };

        var choices = Assert.IsType<List<string>>(Invoke(game, "LegalResponseSources", 1, attack));

        Assert.Contains(absoluteDefense.InstanceId, choices);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0213")]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void NonCounterHandLegionReactionIsNotFilteredByTheSetCounterLifecycleGate()
    {
        var game = Create(918891);
        var player = game.State.Players[0];
        var kaba = Card("S01-0213", "hand-legion-reaction-source");
        kaba.OwnerIndex = player.PlayerIndex;
        player.Hand.Add(kaba);
        var candidate = Candidate(kaba, "reaction");

        Assert.False(LifecycleCheck(game, "IsFreshSetCounterTacticTrigger", candidate));
        Assert.True(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", candidate));

        Invoke(game, "QueueTriggerCandidates", (object)new[] { candidate });

        var prompt = OnlyPrompt(game);
        Assert.Equal("pending-activation", prompt.Continuation);
        Assert.Contains("mode:none", prompt.ValidChoices);
    }

    [Theory]
    [MemberData(nameof(TriggeredCounters))]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void EveryTriggeredCounterRequiresItsExactCoveredSourceInsteadOfSnapshotAuthority(
        string cardId, string trigger)
    {
        var game = Create(91890 + Array.FindIndex(TriggeredCounterRoutes,
            route => route.CardId == cardId));
        var player = game.State.Players[0];
        var source = Card(cardId, $"lifecycle-{cardId}");
        source.Hidden = true;
        source.OwnerIndex = player.PlayerIndex;
        player.Field[1][0] = source;
        var candidate = Candidate(source, trigger);

        Assert.True(LifecycleCheck(game, "IsFreshSetCounterTacticTrigger", candidate));
        Assert.True(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", candidate));

        source.CannotRespondUntilRound = game.State.Round;
        Assert.False(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", candidate));
        source.CannotRespondUntilRound = 0;

        source.Hidden = false;
        Assert.False(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", candidate));

        player.Field[1][0] = null;
        source.Hidden = true;
        player.Resolving.Add(source);
        Assert.False(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", candidate));

        player.Resolving.Remove(source);
        player.Graveyard.Add(source);
        Assert.False(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", candidate));

        player.Graveyard.Remove(source);
        player.Removed.Add(source);
        Assert.False(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", candidate));

        player.Removed.Remove(source);
        Assert.False(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", candidate));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0224")]
    [Trait("L12Evidence", "card:S02-0523")]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void LastKnownSourceContinuationsAreNotMistakenForFreshSetActivations()
    {
        var game = Create(91899);
        var wisdom = Card("S01-0224", "wisdom-continuation-source");
        var horse = Card("S02-0523", "trojan-expiry-source");
        game.State.Players[0].Graveyard.Add(wisdom);
        game.State.Players[0].Graveyard.Add(horse);
        var wisdomReward = Candidate(wisdom, "wisdom-reward");
        var trojanExpiry = Candidate(horse, "trojan-expiry");

        Assert.False(LifecycleCheck(game, "IsFreshSetCounterTacticTrigger", wisdomReward));
        Assert.False(LifecycleCheck(game, "IsFreshSetCounterTacticTrigger", trojanExpiry));
        Assert.True(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", wisdomReward));
        Assert.True(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", trojanExpiry));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0223")]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void SeparateEligibleLeavesEachAskAfterDeclineWithoutConsumingTheSetCounter()
    {
        var game = Create(91901);
        var player = game.State.Players[0];
        var counter = Card("S01-0223", "counter-decline-then-ask-again");
        counter.Hidden = true;
        counter.SetRound = 0;
        counter.OwnerIndex = player.PlayerIndex;
        player.Field[1][0] = counter;
        var first = Legion("separate-leave-first");
        var second = Legion("separate-leave-second");

        QueueSeparateLeaveEvents(game, first, second);

        Assert.Equal(2, ActiveSourceCount(game, counter.InstanceId));
        var firstDecision = OnlyPrompt(game);
        Assert.Equal("pending-activation", firstDecision.Continuation);
        Assert.Contains("mode:none", firstDecision.ValidChoices);

        ResolveChoice(game, "mode:none");

        Assert.Same(counter, player.Field[1][0]);
        Assert.True(counter.Hidden);
        var secondDecision = OnlyPrompt(game);
        Assert.NotEqual(firstDecision.PromptId, secondDecision.PromptId);
        Assert.Equal("pending-activation", secondDecision.Continuation);
        Assert.Contains("mode:use", secondDecision.ValidChoices);
        ResolveChoice(game, "mode:none");
        Assert.Same(counter, player.Field[1][0]);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(QueuedCandidates(game));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0223")]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void SimultaneousEligibleLeavesRemainTwoRealOpportunitiesInsteadOfBeingBatchDeduplicated()
    {
        var game = Create(919015);
        var player = game.State.Players[0];
        var counter = Card("S01-0223", "simultaneous-gift-opportunities");
        counter.Hidden = true;
        counter.OwnerIndex = player.PlayerIndex;
        player.Field[1][0] = counter;
        var first = Legion("simultaneous-eligible-first");
        var second = Legion("simultaneous-eligible-second");
        var deaths = new (int Controller, L12CardInstance Card, L12CardInstance SourceSnapshot)[]
        {
            (0, first, first),
            (0, second, second),
        };

        Invoke(game, "QueueSimultaneousDeathTriggers", (object)deaths);

        Assert.Equal(2, ActiveSourceCount(game, counter.InstanceId));
        var order = OnlyPrompt(game);
        Assert.Equal("trigger-batch-order", order.Continuation);
        Assert.Equal(2, order.ValidChoices.Count);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0223")]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void FirstLegalStackConsumesThePhysicalCounterAndSkipsQueuedLaterTimingEvenWhenNegated()
    {
        var game = Create(91902);
        var player = game.State.Players[0];
        var counter = Card("S01-0223", "counter-stack-once");
        counter.Hidden = true;
        counter.SetRound = 0;
        counter.OwnerIndex = player.PlayerIndex;
        player.Field[1][0] = counter;
        var independent = Card("S01-0223", "independent-physical-counter");
        independent.Hidden = true;
        independent.OwnerIndex = player.PlayerIndex;
        player.Field[1][1] = independent;
        var independentCandidate = Candidate(independent, "reaction", "independent");

        QueueSeparateLeaveEvents(game,
            Legion("legal-leave-first"), Legion("legal-leave-second"));
        var oldLifecycleCandidates = QueuedCandidates(game)
            .Where(candidate => candidate.SourceInstanceId == counter.InstanceId)
            .ToArray();
        Assert.Equal(2, oldLifecycleCandidates.Length);

        ResolveChoice(game, "mode:use");
        if (game.State.EffectStack.Count == 0)
            ResolveChoice(game, "mode:none");

        var stacked = Assert.Single(game.State.EffectStack);
        Assert.Equal(counter.InstanceId, stacked.SourceInstanceId);
        Assert.Null(player.Field[1][0]);
        Assert.Contains(player.Resolving, card => card.InstanceId == counter.InstanceId);
        Assert.All(oldLifecycleCandidates, candidate =>
            Assert.Equal("true", candidate.Data.GetValueOrDefault("counterTacticResponseLifecycleConsumed")));
        Assert.False(stacked.Data.ContainsKey("counterTacticResponseLifecycleConsumed"));
        Assert.DoesNotContain(QueuedCandidates(game),
            candidate => candidate.SourceInstanceId == counter.InstanceId);
        Assert.Same(independent, player.Field[1][1]);
        Assert.True(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", independentCandidate));
        stacked.Negated = true;

        PassAllResponses(game);

        Assert.DoesNotContain(player.Field.SelectMany(row => row), card => card?.InstanceId == counter.InstanceId);
        Assert.Contains(player.Graveyard, card => card.InstanceId == counter.InstanceId);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.DoesNotContain(QueuedCandidates(game),
            candidate => candidate.SourceInstanceId == counter.InstanceId);
        Assert.DoesNotContain(game.State.EffectStack,
            item => item.SourceInstanceId == counter.InstanceId);
        Assert.Contains(game.State.Events, entry => entry.Type == "private-trigger-effect-skipped"
            && entry.Text.Contains(counter.Name, StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-skipped"
            && entry.Text.Contains(counter.Name, StringComparison.Ordinal));

        player.Graveyard.Remove(counter);
        Invoke(game, "ResetCardForPrivateZone", counter);
        player.Hand.Add(counter);
        game.State.ActivePlayer = player.PlayerIndex;
        player.Morale.AddRange(
        [
            new L12MoraleCard { InstanceId = "consumed-reset-morale-a", CardId = "S01-01C1" },
            new L12MoraleCard { InstanceId = "consumed-reset-morale-b", CardId = "S01-01C1" },
        ]);
        var reset = game.Handle(player.PlayerIndex, new L12Command("playCard", counter.InstanceId,
            Row: 1, Slot: 0, CardInstanceIds: ["consumed-reset-morale-a", "consumed-reset-morale-b"]));
        Assert.True(reset.Accepted, reset.Error);
        game.State.ActivePlayer = 1 - player.PlayerIndex;
        Assert.All(oldLifecycleCandidates, candidate =>
            Assert.False(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", candidate)));
        var newLifecycleCandidate = Assert.Single(Assert.IsAssignableFrom<IEnumerable<L12TriggerCandidate>>(
            Invoke(game, "BuildS1LeaveReactionCandidates", player.PlayerIndex,
                Legion("new-lifecycle-leave"), true)),
            candidate => candidate.SourceInstanceId == counter.InstanceId);
        Assert.DoesNotContain(oldLifecycleCandidates,
            candidate => candidate.CandidateId == newLifecycleCandidate.CandidateId);
        Assert.True(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", newLifecycleCandidate));
    }

    [Fact]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void InvalidFreshCounterIsPrunedBeforeAControllerReceivesATriggerOrderPrompt()
    {
        var game = Create(919020);
        var player = game.State.Players[0];
        var validSource = Card("S01-0223", "valid-order-counter");
        validSource.Hidden = true;
        validSource.OwnerIndex = player.PlayerIndex;
        player.Field[1][0] = validSource;
        var invalidSource = Card("S01-0320", "invalid-order-counter");
        invalidSource.Hidden = true;
        invalidSource.OwnerIndex = player.PlayerIndex;
        var invalid = Candidate(invalidSource, "reaction", "invalid-order");
        var valid = Candidate(validSource, "reaction", "valid-order");

        Invoke(game, "QueueTriggerCandidates", (object)new[] { invalid, valid });

        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation == "trigger-batch-order");
        var declaration = OnlyPrompt(game);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Equal("true", invalid.Data.GetValueOrDefault("counterTacticResponseLifecycleConsumed"));
        Assert.False(valid.Data.ContainsKey("counterTacticResponseLifecycleConsumed"));
        Assert.DoesNotContain(QueuedCandidates(game), candidate => candidate.CandidateId == invalid.CandidateId);
    }

    [Fact]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    [Trait("L12Evidence", "privacy:legacy-null-source-snapshot")]
    public void LegacyV2CandidateWithoutSourceSnapshotIsPrunedPrivatelyWithoutRevealingItsName()
    {
        var game = Create(9190201);
        var counter = Card("S01-0223", "legacy-null-snapshot-prune");
        counter.Hidden = true;
        counter.OwnerIndex = 0;
        var candidate = Assert.IsType<L12TriggerCandidate>(Invoke(game, "CreateTriggerCandidate",
            0, counter, "reaction", "【我方高费用军团离场时】反击战术",
            new Dictionary<string, string>(), counter));
        game.State.IsResolvingStack = true;
        Invoke(game, "QueueTriggerCandidates", (object)new[] { candidate });

        game = RestoreAsV2WithoutCandidateSourceSnapshot(game, candidate.CandidateId);
        var restored = Assert.Single(QueuedCandidates(game));
        Assert.Null(restored.SourceSnapshot);
        game.State.IsResolvingStack = false;
        Invoke(game, "AdvanceTriggerBatches");

        var raw = Assert.Single(game.State.Events, entry =>
            entry.Text.Contains(counter.Name, StringComparison.Ordinal));
        Assert.Equal("private-trigger-effect-skipped", raw.Type);
        Assert.Contains(game.SnapshotFor(0).RecentEvents, entry =>
            entry.Sequence == raw.Sequence && entry.Text.Contains(counter.Name, StringComparison.Ordinal));
        Assert.DoesNotContain(game.SnapshotFor(1).RecentEvents, entry =>
            entry.Text.Contains(counter.Name, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    [Trait("L12Evidence", "privacy:legacy-null-source-snapshot")]
    public void LegacyV2CandidateWithoutSourceSnapshotIsRejectedPrivatelyAtDeclarationCommit()
    {
        var game = Create(9190202);
        var player = game.State.Players[0];
        var counter = Card("S01-0223", "legacy-null-snapshot-declaration");
        counter.Hidden = true;
        counter.OwnerIndex = player.PlayerIndex;
        player.Field[1][0] = counter;
        player.Graveyard.Add(Card("S01-0212", "legacy-null-snapshot-declaration-guard"));
        var candidate = Assert.IsType<L12TriggerCandidate>(Invoke(game, "CreateTriggerCandidate",
            0, counter, "reaction", "【我方高费用军团离场时】反击战术",
            new Dictionary<string, string>(), counter));
        Invoke(game, "QueueTriggerCandidates", (object)new[] { candidate });
        ResolveChoice(game, "mode:use");
        Assert.Equal("pending-activation", OnlyPrompt(game).Continuation);
        player.Field[1][0] = null;
        counter.Hidden = false;
        player.Resolving.Add(counter);

        game = RestoreAsV2WithoutCandidateSourceSnapshot(game, candidate.CandidateId);
        var restored = Assert.Single(QueuedCandidates(game));
        Assert.Null(restored.SourceSnapshot);
        ResolveChoice(game, "mode:none");

        var raw = Assert.Single(game.State.Events, entry =>
            entry.Text.Contains("盖伏来源", StringComparison.Ordinal));
        Assert.Equal("private-trigger-ability-rejected", raw.Type);
        Assert.DoesNotContain(game.SnapshotFor(1).RecentEvents, entry =>
            entry.Text.Contains(counter.Name, StringComparison.Ordinal)
            || entry.Text.Contains("盖伏来源", StringComparison.Ordinal));
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    [Trait("L12Evidence", "privacy:explicit-public-source-snapshot")]
    public void ExplicitFaceUpSourceSnapshotKeepsInvalidPruneEventPublic()
    {
        var game = Create(9190203);
        var counter = Card("S01-0223", "explicit-face-up-snapshot-prune");
        counter.Hidden = false;
        counter.OwnerIndex = 0;
        var candidate = Assert.IsType<L12TriggerCandidate>(Invoke(game, "CreateTriggerCandidate",
            0, counter, "reaction", "【我方高费用军团离场时】反击战术",
            new Dictionary<string, string>(), counter));
        game.State.IsResolvingStack = true;
        Invoke(game, "QueueTriggerCandidates", (object)new[] { candidate });
        game = RestoreAsV2(game);
        game.State.IsResolvingStack = false;

        Invoke(game, "AdvanceTriggerBatches");

        var raw = Assert.Single(game.State.Events, entry =>
            entry.Text.Contains(counter.Name, StringComparison.Ordinal));
        Assert.Equal("effect-skipped", raw.Type);
        Assert.Contains(game.SnapshotFor(1).RecentEvents, entry =>
            entry.Sequence == raw.Sequence && entry.Text.Contains(counter.Name, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0223")]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void PendingDeclarationRechecksTheCoveredSourceBeforeItCanCommit()
    {
        var game = Create(919021);
        var player = game.State.Players[0];
        var counter = Card("S01-0223", "counter-invalidated-during-declaration");
        counter.Hidden = true;
        counter.OwnerIndex = player.PlayerIndex;
        player.Field[1][0] = counter;
        player.Graveyard.Add(Card("S01-0212", "declaration-source-recheck-guard"));
        QueueSeparateLeaveEvents(game, Legion("declaration-source-recheck"));
        var pendingCandidate = Assert.Single(QueuedCandidates(game),
            candidate => candidate.SourceInstanceId == counter.InstanceId);

        ResolveChoice(game, "mode:use");
        Assert.Empty(game.State.EffectStack);
        player.Field[1][0] = null;
        counter.Hidden = false;
        player.Resolving.Add(counter);

        ResolveChoice(game, "mode:none");

        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Equal("true", pendingCandidate.Data.GetValueOrDefault("counterTacticResponseLifecycleConsumed"));
        Assert.Contains(game.State.Events, entry => entry.Type == "private-trigger-ability-rejected"
            && entry.Text.Contains("盖伏来源", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "ability-rejected"
            && entry.Text.Contains(counter.Name, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0223")]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void ConsumedCounterAndQueuedSiblingKeepTheSameResultAcrossV2Restore()
    {
        var game = Create(919022);
        var player = game.State.Players[0];
        var counter = Card("S01-0223", "counter-v2-consumed");
        counter.Hidden = true;
        counter.OwnerIndex = player.PlayerIndex;
        player.Field[1][0] = counter;
        QueueSeparateLeaveEvents(game, Legion("v2-leave-first"), Legion("v2-leave-second"));
        ResolveChoice(game, "mode:use");
        if (game.State.EffectStack.Count == 0)
            ResolveChoice(game, "mode:none");
        Assert.Single(game.State.EffectStack).Negated = true;

        game = RestoreAsV2(game);
        PassAllResponses(game);

        var restoredPlayer = game.State.Players[0];
        Assert.Contains(restoredPlayer.Graveyard, card => card.InstanceId == counter.InstanceId);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.DoesNotContain(QueuedCandidates(game),
            candidate => candidate.SourceInstanceId == counter.InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0223")]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void DifferentPhysicalCountersStayIndependentAndPrivateResetThenSettingStartsANewLifecycle()
    {
        var game = Create(919023);
        var player = game.State.Players[0];
        var first = Card("S01-0223", "physical-counter-first");
        var second = Card("S01-0223", "physical-counter-second");
        first.Hidden = second.Hidden = true;
        first.OwnerIndex = second.OwnerIndex = player.PlayerIndex;
        player.Field[1][0] = first;
        player.Field[1][1] = second;
        var firstCandidate = Candidate(first, "reaction", "first");
        var secondCandidate = Candidate(second, "reaction", "second");

        Assert.True(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", firstCandidate));
        Assert.True(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", secondCandidate));
        player.Field[1][0] = null;
        first.Hidden = false;
        player.Resolving.Add(first);
        Assert.False(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", firstCandidate));
        Assert.True(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", secondCandidate));

        player.Resolving.Remove(first);
        Invoke(game, "ResetCardForPrivateZone", first);
        player.Hand.Add(first);
        player.Field[1][1] = null;
        game.State.ActivePlayer = 0;
        player.Morale.AddRange(
        [
            new L12MoraleCard { InstanceId = "reset-set-morale-a", CardId = "S01-01C1" },
            new L12MoraleCard { InstanceId = "reset-set-morale-b", CardId = "S01-01C1" },
        ]);
        var reset = game.Handle(0, new L12Command("playCard", first.InstanceId,
            Row: 1, Slot: 0, CardInstanceIds: ["reset-set-morale-a", "reset-set-morale-b"]));
        Assert.True(reset.Accepted, reset.Error);
        game.State.ActivePlayer = 1;
        Assert.True(first.Hidden);
        Assert.Same(first, player.Field[1][0]);
        var resetCandidate = Assert.Single(Assert.IsAssignableFrom<IEnumerable<L12TriggerCandidate>>(
            Invoke(game, "BuildS1LeaveReactionCandidates", player.PlayerIndex,
                Legion("reset-lifecycle-leave"), true)),
            candidate => candidate.SourceInstanceId == first.InstanceId);
        Assert.True(LifecycleCheck(game, "CanDeclareFreshSetCounterTacticTrigger", resetCandidate));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0224")]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void WisdomRewardUsesItsSuccessfulResponseSnapshotAfterTheCounterReachedTheGraveyard()
    {
        var game = Create(919024);
        var player = game.State.Players[0];
        var wisdom = Card("S01-0224", "wisdom-in-grave-for-reward");
        var drawn = Legion("wisdom-reward-draw");
        player.Graveyard.Add(wisdom);
        player.Library.Add(drawn);
        var candidate = Assert.IsType<L12TriggerCandidate>(Invoke(game, "CreateTriggerCandidate",
            0, wisdom, "wisdom-reward", "对方效果成功完成结算后的效果", null, wisdom));

        Invoke(game, "QueueTriggerCandidates", (object)new[] { candidate });
        PassAllResponses(game);

        Assert.Contains(player.Hand, card => card.InstanceId == drawn.InstanceId);
        Assert.Contains(player.Graveyard, card => card.InstanceId == wisdom.InstanceId);
        Assert.Empty(QueuedCandidates(game));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0523")]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void FaceUpTrojanHorseKeepsItsContinuousStateAndExpiryComposite()
    {
        var game = Create(919025);
        var owner = game.State.Players[0];
        var host = game.State.Players[1];
        var horse = Card("S02-0523", "face-up-trojan-expiry");
        var drawn = Legion("trojan-expiry-draw");
        horse.Hidden = false;
        horse.OwnerIndex = owner.PlayerIndex;
        horse.DiscardAtEndOfTurnUntilTurn = game.State.TurnSerial;
        host.Field[0][0] = horse;
        owner.Library.Add(drawn);

        var queued = Assert.IsType<bool>(Invoke(game, "ResolveS2DelayedEndTurnCards", owner.PlayerIndex));

        Assert.True(queued);
        Assert.Contains(owner.Graveyard, card => card.InstanceId == horse.InstanceId);
        Assert.Contains(owner.Hand, card => card.InstanceId == drawn.InstanceId);
        Assert.DoesNotContain(host.Field.SelectMany(row => row), card => card?.InstanceId == horse.InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0523")]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void TrojanHorseIsConsumedAtLegalStackAndNegationCannotRestoreItsSetLifecycle()
    {
        var game = Create(9190251);
        var owner = game.State.Players[0];
        var host = game.State.Players[1];
        var horse = Card("S02-0523", "trojan-consumed-when-stacked");
        horse.Hidden = true;
        horse.OwnerIndex = owner.PlayerIndex;
        owner.Field[1][0] = horse;
        QueueTrojanAfterAttack(game, horse);

        ResolveChoice(game, "mode:use");
        ResolveChoice(game, "1:1");

        var stacked = Assert.Single(game.State.EffectStack);
        Assert.Equal(horse.InstanceId, stacked.SourceInstanceId);
        Assert.DoesNotContain(owner.Field.SelectMany(row => row), card => card?.InstanceId == horse.InstanceId);
        Assert.Contains(owner.Resolving, card => card.InstanceId == horse.InstanceId);
        stacked.Negated = true;
        PassAllResponses(game);

        Assert.Contains(owner.Graveyard, card => card.InstanceId == horse.InstanceId);
        Assert.DoesNotContain(owner.Resolving, card => card.InstanceId == horse.InstanceId);
        Assert.DoesNotContain(owner.Field.SelectMany(row => row), card => card?.InstanceId == horse.InstanceId);
        Assert.DoesNotContain(host.Field.SelectMany(row => row), card => card?.InstanceId == horse.InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0523")]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void TrojanHorseWithInvalidatedDestinationStaysConsumedAndGoesToGraveyard()
    {
        var game = Create(9190252);
        var owner = game.State.Players[0];
        var host = game.State.Players[1];
        var horse = Card("S02-0523", "trojan-invalidated-destination");
        horse.Hidden = true;
        horse.OwnerIndex = owner.PlayerIndex;
        owner.Field[1][0] = horse;
        QueueTrojanAfterAttack(game, horse);

        ResolveChoice(game, "mode:use");
        ResolveChoice(game, "1:1");
        var occupant = Legion("trojan-destination-occupant");
        host.Field[1][1] = occupant;
        PassAllResponses(game);

        Assert.Same(occupant, host.Field[1][1]);
        Assert.Contains(owner.Graveyard, card => card.InstanceId == horse.InstanceId);
        Assert.DoesNotContain(owner.Resolving, card => card.InstanceId == horse.InstanceId);
        Assert.DoesNotContain(owner.Field.SelectMany(row => row), card => card?.InstanceId == horse.InstanceId);
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == horse.InstanceId));
        Assert.Equal("failed", result.EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0523")]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void TrojanHorseMovesFromResolvingToFaceUpHostAndItsExpiryContinuationStillSettles()
    {
        var game = Create(9190253);
        var owner = game.State.Players[0];
        var host = game.State.Players[1];
        var horse = Card("S02-0523", "trojan-successful-lifecycle");
        var drawn = Legion("trojan-successful-expiry-draw");
        horse.Hidden = true;
        horse.OwnerIndex = owner.PlayerIndex;
        owner.Field[1][0] = horse;
        owner.Library.Add(drawn);
        QueueTrojanAfterAttack(game, horse);

        ResolveChoice(game, "mode:use");
        ResolveChoice(game, "1:1");
        Assert.Contains(owner.Resolving, card => card.InstanceId == horse.InstanceId);
        PassAllResponses(game);

        Assert.Same(horse, host.Field[1][1]);
        Assert.False(horse.Hidden);
        Assert.Equal(owner.PlayerIndex, horse.OwnerIndex);
        Assert.DoesNotContain(owner.Resolving, card => card.InstanceId == horse.InstanceId);
        Assert.DoesNotContain(owner.Graveyard, card => card.InstanceId == horse.InstanceId);
        Assert.True(horse.DiscardAtEndOfTurnUntilTurn >= game.State.TurnSerial);

        game.State.TurnSerial = horse.DiscardAtEndOfTurnUntilTurn;
        Assert.True(Assert.IsType<bool>(Invoke(game, "ResolveS2DelayedEndTurnCards", owner.PlayerIndex)));

        Assert.Contains(owner.Graveyard, card => card.InstanceId == horse.InstanceId);
        Assert.Contains(owner.Hand, card => card.InstanceId == drawn.InstanceId);
        Assert.DoesNotContain(host.Field.SelectMany(row => row), card => card?.InstanceId == horse.InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0223")]
    [Trait("L12Evidence", "family:counter-tactic-response-lifecycle")]
    public void StackedCounterCompositeContinuesFromLastKnownSourceWithoutASecondQualificationGate()
    {
        var game = Create(919026);
        var player = game.State.Players[0];
        var counter = Card("S01-0223", "counter-composite-last-known");
        var drawn = Legion("counter-composite-draw");
        counter.Hidden = true;
        counter.OwnerIndex = player.PlayerIndex;
        player.Field[1][0] = counter;
        player.Library.Add(drawn);
        player.Graveyard.Add(Card("S01-0212", "counter-composite-declaration-guard"));
        QueueSeparateLeaveEvents(game, Legion("counter-composite-leave"));

        ResolveChoice(game, "mode:use");
        var declaration = OnlyPrompt(game);
        Assert.Equal("pending-activation", declaration.Continuation);
        var command = new L12Command("resolvePrompt", PromptId: declaration.PromptId, Choice: "mode:none");
        var submitted = game.Handle(declaration.PlayerIndex, command);
        Assert.True(submitted.Accepted, submitted.Error);
        Assert.Single(game.State.EffectStack);
        var duplicate = game.Handle(declaration.PlayerIndex, command);
        Assert.False(duplicate.Accepted);
        Assert.Single(game.State.EffectStack);
        Assert.True(player.Resolving.Remove(counter));
        player.Graveyard.Add(counter);

        PassAllResponses(game);

        Assert.Contains(player.Hand, card => card.InstanceId == drawn.InstanceId);
        Assert.Contains(player.Graveyard, card => card.InstanceId == counter.InstanceId);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(QueuedCandidates(game));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-02M1")]
    [Trait("L12Evidence", "family:once-per-turn-trigger-reservation")]
    public void NephthysReservesOneCandidatePerBatchAndDeclineReleasesTheReservation()
    {
        var game = Create(91903, "S02-02M1");
        var player = game.State.Players[0];
        var scarab = Card("S02-0201", "nephthys-scarab");
        player.Graveyard.Add(scarab);
        var first = Legion("nephthys-first", "taiyangcheng", 2);
        var second = Legion("nephthys-second", "taiyangcheng", 2);
        var deaths = new (int Controller, L12CardInstance Card, L12CardInstance SourceSnapshot)[]
        {
            (0, first, first),
            (0, second, second),
        };

        Invoke(game, "QueueSimultaneousDeathTriggers", (object)deaths);

        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation == "trigger-batch-order");
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:none", prompt.ValidChoices);
        var onceKey = L12MasterTriggeredUsageRules.Key("nephthysScarab", player.PlayerIndex, game.State.TurnSerial);
        Assert.Contains($"{onceKey}:pending", player.UsedAbilities);

        var declined = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "mode:none"));
        Assert.True(declined.Accepted, declined.Error);
        Assert.DoesNotContain(onceKey, player.UsedAbilities);
        Assert.DoesNotContain($"{onceKey}:pending", player.UsedAbilities);
        Assert.NotNull(Invoke(game, "BuildNephthysOwnDeathCandidate", 0,
            Legion("nephthys-later", "taiyangcheng", 2)));
    }

    [Theory]
    [InlineData("S01-0223", "leave")]
    [InlineData("S01-0320", "leave")]
    [InlineData("S01-0420", "after-attack")]
    [Trait("L12Evidence", "card:S02-0003")]
    [Trait("L12Evidence", "family:counter-tactic-common-disable")]
    public void CourtMagicianStopsCounterTriggerCandidatesAtTheCommonQueue(string cardId, string trigger)
    {
        var game = Create(91910);
        var counter = Card(cardId, $"disabled-{cardId}");
        counter.Hidden = true;
        game.State.Players[0].Field[1][0] = counter;
        game.State.CounterTacticsDisabledUntilTurnSerial = int.MaxValue;
        game.State.CounterTacticsDisabledExpiresAtPlayerTurnStart = 1;
        var candidate = new L12TriggerCandidate
        {
            CandidateId = $"disabled-candidate-{cardId}",
            Controller = 0,
            SourceInstanceId = counter.InstanceId,
            SourceCardId = counter.CardId,
            SourceName = counter.Name,
            Trigger = trigger,
            Text = "反击战术触发",
            SourceSnapshot = counter,
        };

        Invoke(game, "QueueTriggerCandidates", (object)new[] { candidate });

        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0003")]
    [Trait("L12Evidence", "family:counter-tactic-common-disable")]
    public void CourtMagicianRechecksAQueuedCounterBeforeDeclaration()
    {
        var game = Create(91911);
        var counter = Card("S01-0223", "disabled-pending-counter");
        counter.Hidden = true;
        game.State.Players[0].Field[1][0] = counter;
        game.State.PendingTriggerStackCandidates.Add(new L12TriggerCandidate
        {
            CandidateId = "disabled-pending-candidate",
            Controller = 0,
            SourceInstanceId = counter.InstanceId,
            SourceCardId = counter.CardId,
            SourceName = counter.Name,
            Trigger = "leave",
            Text = "反击战术触发",
            SourceSnapshot = counter,
        });
        game.State.CounterTacticsDisabledUntilTurnSerial = int.MaxValue;
        game.State.CounterTacticsDisabledExpiresAtPlayerTurnStart = 1;

        Invoke(game, "AdvancePendingTriggerStackCandidates");

        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0003")]
    [Trait("L12Evidence", "family:counter-tactic-common-disable")]
    public void CourtMagicianBlocksSetCountersButNotHandLegionResponses()
    {
        var game = Create(91912);
        var defender = game.State.Players[1];
        var counter = Card("S01-0016", "disabled-set-counter");
        counter.Hidden = true;
        defender.Field[1][0] = counter;
        var puppet = Card("S02-0005", "available-hand-puppet");
        defender.Hand.Add(puppet);
        game.State.PendingDefense = new L12PendingDefense
        {
            AttackerPlayer = 0,
            AttackerInstanceId = "attacker",
            Target = new L12AttackTarget("master"),
            Stage = L12CombatStage.AttackerAttackTiming,
        };
        game.State.CounterTacticsDisabledUntilTurnSerial = int.MaxValue;
        game.State.CounterTacticsDisabledExpiresAtPlayerTurnStart = 1;
        var top = new L12StackItem
        {
            StackItemId = "opponent-attack",
            Controller = 0,
            SourceInstanceId = "attacker",
            SourceCardId = "test-attacker",
            SourceName = "进攻军团",
            Trigger = "opponent-attack",
            Text = "进攻",
        };

        var choices = Assert.IsType<List<string>>(Invoke(game, "LegalResponseSources", 1, top));

        Assert.DoesNotContain(counter.InstanceId, choices);
        Assert.Contains(puppet.InstanceId, choices);
    }

}
