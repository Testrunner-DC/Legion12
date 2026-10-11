using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class NextResetLockLifecycleRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "next-reset-lock:elizabeth-ragnarok")]
    public void ElizabethLockSurvivesRagnarokExtraTurnUntilOpponentsActualReset(int actor, bool restoreV2)
    {
        var game = CreateElizabethScenario(actor, withRagnarok: true);
        var opponent = 1 - actor;
        var initialRound = game.State.Round;
        var initialTurnSerial = game.State.TurnSerial;

        PlayElizabethAndResolve(ref game, actor, restoreV2);

        var target = TargetMorale(game, opponent);
        Assert.True(target.Tapped);
        Assert.Equal(actor, game.State.ActivePlayer);
        Assert.Equal(initialRound + 1, game.State.Round);
        Assert.Equal(initialTurnSerial + 1, game.State.TurnSerial);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Equal("S01-DS09", game.State.ActiveDisaster?.CardId);
        Assert.Contains(game.State.Events,
            entry => entry.Type == "extra-turn" && entry.PlayerIndex == actor);

        if (restoreV2) game = RestoreAndVerifyHash(game);
        var end = game.Handle(actor, new L12Command("endTurn"));
        Assert.True(end.Accepted, end.Error);
        ResolvePending(ref game, restoreV2);

        target = TargetMorale(game, opponent);
        Assert.Equal(opponent, game.State.ActivePlayer);
        Assert.Equal(initialRound + 2, game.State.Round);
        Assert.Equal(initialTurnSerial + 2, game.State.TurnSerial);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.True(target.Tapped);
        Assert.False(target.CannotUntapAtNextReset);
        AssertCoreFlowClosed(game);
        AdvanceThroughSecondAffectedReset(ref game, opponent, restoreV2);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "next-reset-lock:elizabeth-no-extra-turn-control")]
    public void ElizabethLockStillBlocksTheImmediateOpponentResetWithoutAnExtraTurn(int actor, bool restoreV2)
    {
        var game = CreateElizabethScenario(actor, withRagnarok: false);
        var opponent = 1 - actor;
        var initialRound = game.State.Round;
        var initialTurnSerial = game.State.TurnSerial;

        PlayElizabethAndResolve(ref game, actor, restoreV2);

        var target = TargetMorale(game, opponent);
        Assert.True(target.Tapped);
        Assert.Equal(actor, game.State.ActivePlayer);
        Assert.Equal(initialRound, game.State.Round);
        Assert.Equal(initialTurnSerial, game.State.TurnSerial);

        if (restoreV2) game = RestoreAndVerifyHash(game);
        var end = game.Handle(actor, new L12Command("endTurn"));
        Assert.True(end.Accepted, end.Error);
        ResolvePending(ref game, restoreV2);

        target = TargetMorale(game, opponent);
        Assert.Equal(opponent, game.State.ActivePlayer);
        Assert.Equal(initialRound + 1, game.State.Round);
        Assert.Equal(initialTurnSerial + 1, game.State.TurnSerial);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.True(target.Tapped);
        Assert.False(target.CannotUntapAtNextReset);
        AssertCoreFlowClosed(game);
        AdvanceThroughSecondAffectedReset(ref game, opponent, restoreV2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "next-reset-lock:mulan-death-writer")]
    public void MulanDeathLocksTheDeclaredOpposingMoraleForItsNextReset(int actor)
    {
        var game = CreateCleanScenario(921100 + actor, actor);
        var opponent = 1 - actor;
        game.State.ActivePlayer = opponent;
        var source = Card("S01-0108", $"next-reset-mulan-{actor}", actor);
        var target = new L12MoraleCard
        {
            CardId = "S01-01C1",
            InstanceId = $"next-reset-mulan-target-{actor}",
            Tapped = true,
        };
        game.State.Players[actor].Field[0][0] = source;
        game.State.Players[opponent].Morale.Add(target);
        var item = StackItem(actor, source, "death", $"next-reset-mulan-stack-{actor}");
        item.Data["declared:moraleTarget"] = target.InstanceId;
        game.State.EffectStack.Add(item);

        InvokePrivate(game, "ResolveDeathEffect", item);

        Assert.True(target.CannotUntapAtNextReset);
        Assert.Equal(0, target.CannotUntapUntilRound);
        Assert.DoesNotContain(item, game.State.EffectStack);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "next-reset-lock:wuzetian-legacy-writer")]
    public void WuzetianLegacyMoraleReturnContinuationUsesTheOneShotResetLock(int actor)
    {
        var game = CreateCleanScenario(921110 + actor, actor);
        var opponent = 1 - actor;
        var source = Card("S01-0102", $"next-reset-wuzetian-{actor}", actor);
        var target = Card("S01-0003", $"next-reset-wuzetian-target-{actor}", opponent);
        target.Tapped = true;
        game.State.Players[actor].Field[0][0] = source;
        game.State.Players[opponent].Field[0][0] = target;
        var item = StackItem(actor, source, "enter", $"next-reset-wuzetian-stack-{actor}");
        game.State.EffectStack.Add(item);
        IReadOnlyDictionary<string, string> continuation = new Dictionary<string, string>
        {
            ["targets"] = target.InstanceId,
        };

        InvokePrivate(game, "CompleteEffectMoraleReturn", item, "wuzetian-lock", continuation);

        Assert.True(target.CannotUntapAtNextReset);
        Assert.Equal(0, target.CannotUntapUntilRound);
        Assert.DoesNotContain(item, game.State.EffectStack);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("L12Evidence", "next-reset-lock:plague-legion-and-morale-writer")]
    public void PlagueWriterLocksEitherDeclaredTargetKindWithoutCreatingARoundDeadline(bool targetMorale)
    {
        var game = CreateCleanScenario(targetMorale ? 921121 : 921120, 0);
        var source = Card("S01-0011", "next-reset-plague", 0);
        var targetId = targetMorale ? "next-reset-plague-morale" : "next-reset-plague-legion";
        L12CardInstance? legion = null;
        L12MoraleCard? morale = null;
        if (targetMorale)
        {
            morale = new L12MoraleCard { CardId = "S01-01C1", InstanceId = targetId, Tapped = true };
            game.State.Players[1].Morale.Add(morale);
        }
        else
        {
            legion = Card("S01-0003", targetId, 1);
            legion.Tapped = true;
            game.State.Players[1].Field[0][0] = legion;
        }
        game.State.Players[0].Resolving.Add(source);
        var item = StackItem(0, source, "play", $"next-reset-plague-stack-{targetMorale}");
        item.Data["atomicFlow"] = "plague-effect";
        item.Data["declared:lockTarget"] = targetId;
        game.State.EffectStack.Add(item);

        Assert.True(Assert.IsType<bool>(InvokePrivate(game, "TryResolveS1ExtendedTactic", item, source)));

        if (targetMorale)
        {
            Assert.True(Assert.IsType<L12MoraleCard>(morale).CannotUntapAtNextReset);
            Assert.Equal(0, morale!.CannotUntapUntilRound);
        }
        else
        {
            Assert.True(Assert.IsType<L12CardInstance>(legion).CannotUntapAtNextReset);
            Assert.Equal(0, legion!.CannotUntapUntilRound);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "next-reset-lock:perseus-current-writer")]
    public void PromotedPerseusCurrentPublicEntryRouteUsesTheOneShotResetLock(int actor)
    {
        var game = CreateCleanScenario(921130 + actor, actor);
        var opponent = 1 - actor;
        var source = Card("S02-0505", $"next-reset-perseus-{actor}", actor);
        var target = Card("S01-0003", $"next-reset-perseus-target-{actor}", opponent);
        target.Tapped = true;
        game.State.Players[actor].Field[0][0] = source;
        game.State.Players[opponent].Field[0][0] = target;
        var item = StackItem(actor, source, "promotion-enter", $"next-reset-perseus-stack-{actor}");
        item.Data["declared:mode"] = "mode:use";
        item.Data["declared:target"] = target.InstanceId;
        game.State.EffectStack.Add(item);

        Assert.True(Assert.IsType<bool>(InvokePrivate(game, "TryResolveBatch6JAEnterEffect", item, source)));

        Assert.True(target.CannotUntapAtNextReset);
        Assert.Equal(0, target.CannotUntapUntilRound);
        Assert.DoesNotContain(item, game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "next-reset-lock:one-shot-and-legacy-independent")]
    public void RepeatedNewLockIsIdempotentAndConsumptionNeverClearsTheLegacyRoundLock()
    {
        var game = CreateCleanScenario(921140, 0);
        var player = game.State.Players[0];
        var target = Card("S01-0003", "next-reset-coexisting-locks", 0);
        target.Tapped = true;
        target.CannotUntapUntilRound = game.State.Round + 2;
        player.Field[0][0] = target;
        InvokePrivate(game, "LockUntilNextReset", target);
        InvokePrivate(game, "LockUntilNextReset", target);

        InvokePrivate(game, "Untap", player);

        Assert.True(target.Tapped);
        Assert.False(target.CannotUntapAtNextReset);
        Assert.Equal(game.State.Round + 2, target.CannotUntapUntilRound);
        game.State.Round = target.CannotUntapUntilRound;
        InvokePrivate(game, "Untap", player);
        Assert.True(target.Tapped);
        game.State.Round++;
        InvokePrivate(game, "Untap", player);
        Assert.False(target.Tapped);
    }

    [Fact]
    [Trait("L12Evidence", "next-reset-lock:private-zone-and-morale-return-reset")]
    public void PrivateZoneResetAndMoraleReturnClearBothNewAndLegacyResetLocks()
    {
        var game = CreateCleanScenario(921141, 0);
        var player = game.State.Players[0];
        var card = Card("S01-0003", "next-reset-private-card", 0);
        card.Tapped = true;
        card.CannotUntapAtNextReset = true;
        card.CannotUntapUntilRound = 99;

        InvokePrivate(game, "ResetCardForPrivateZone", card);

        Assert.False(card.Tapped);
        Assert.False(card.CannotUntapAtNextReset);
        Assert.Equal(0, card.CannotUntapUntilRound);

        var morale = new L12MoraleCard
        {
            CardId = "S01-01C1",
            InstanceId = "next-reset-returned-morale",
            Tapped = true,
            CannotUntapAtNextReset = true,
            CannotUntapUntilRound = 99,
        };
        player.Morale.Add(morale);
        IReadOnlyCollection<string> selected = [morale.InstanceId];

        Assert.True(Assert.IsType<bool>(InvokePrivate(game, "ReturnSelectedMoraleById",
            player, selected, 1, false)));
        Assert.DoesNotContain(morale, player.Morale);
        Assert.Contains(morale, player.MoraleDeck);
        Assert.False(morale.Tapped);
        Assert.False(morale.CannotUntapAtNextReset);
        Assert.Equal(0, morale.CannotUntapUntilRound);
    }

    [Fact]
    [Trait("L12Evidence", "next-reset-lock:promotion-inheritance")]
    public void PromotionCarriesTheNewAndLegacyResetLocksIndependently()
    {
        var foundation = Card("S01-0003", "next-reset-foundation", 0);
        foundation.CannotUntapAtNextReset = true;
        foundation.CannotUntapUntilRound = 9;
        var promoted = Card("S02-0505", "next-reset-promoted", 0);
        promoted.CannotUntapUntilRound = 4;

        L12S2ZoneOps.InheritPromotionState(foundation, promoted);

        Assert.True(promoted.CannotUntapAtNextReset);
        Assert.Equal(9, promoted.CannotUntapUntilRound);
    }

    [Fact]
    [Trait("L12Evidence", "next-reset-lock:effect-ready-boundary")]
    public void ResetLocksDoNotBroadenTheIndependentEffectReadyProhibition()
    {
        var game = CreateCleanScenario(921142, 0);
        var target = Card("S01-0003", "next-reset-effect-ready", 0);
        target.Tapped = true;
        target.CannotUntapAtNextReset = true;
        target.CannotUntapUntilRound = game.State.Round + 10;

        Assert.True(Assert.IsType<bool>(InvokePrivate(game, "CanReadyCardByEffect", target)));

        target.CannotReadyByEffectUntilTurn = game.State.TurnSerial;
        Assert.False(Assert.IsType<bool>(InvokePrivate(game, "CanReadyCardByEffect", target)));
    }

    [Fact]
    [Trait("L12Evidence", "next-reset-lock:projection-separates-reset-and-effect-ready")]
    public void CardProjectionDescribesResetAndEffectReadyLocksAsIndependentRestrictions()
    {
        var game = CreateCleanScenario(921146, 0);
        var target = Card("S01-0003", "next-reset-projection-target", 0);
        target.Tapped = true;
        target.CannotUntapAtNextReset = true;
        target.CannotReadyByEffectUntilTurn = game.State.TurnSerial;
        game.State.Players[0].Field[0][0] = target;
        game.State.Revision++;

        var labels = ProjectedStatusLabels(game, target.InstanceId);
        Assert.Contains("下个重置阶段无法转为活跃", labels);
        Assert.Contains("本回合无法因效果转为活跃", labels);

        target.CannotUntapAtNextReset = false;
        target.CannotUntapUntilRound = game.State.Round;
        target.CannotReadyByEffectUntilTurn = -1;
        game.State.Revision++;
        labels = ProjectedStatusLabels(game, target.InstanceId);
        Assert.Contains("重置阶段无法转为活跃", labels);
        Assert.DoesNotContain("下个重置阶段无法转为活跃", labels);
        Assert.DoesNotContain("本回合无法因效果转为活跃", labels);
    }

    [Fact]
    [Trait("L12Evidence", "next-reset-lock:v2-default-and-current-roundtrip")]
    public void V2OmissionPreservesLegacyShapeWhileTrueLocksRoundTripWithTheStateHash()
    {
        var legacy = CreateCleanScenario(921143, 0);
        var legacyTarget = Card("S01-0003", "next-reset-legacy-omission", 0);
        legacy.State.Players[0].Field[0][0] = legacyTarget;
        legacy.State.Revision++;
        var legacyJson = legacy.SerializeFullState();
        Assert.DoesNotContain(nameof(L12CardInstance.CannotUntapAtNextReset), legacyJson,
            StringComparison.Ordinal);
        var restoredLegacy = RestoreAndVerifyHash(legacy);
        Assert.False(Assert.IsType<L12CardInstance>(restoredLegacy.State.Players[0].Field[0][0])
            .CannotUntapAtNextReset);

        var current = CreateCleanScenario(921144, 0);
        var card = Card("S01-0003", "next-reset-current-card", 0);
        card.CannotUntapAtNextReset = true;
        var morale = new L12MoraleCard
        {
            CardId = "S01-01C1",
            InstanceId = "next-reset-current-morale",
            CannotUntapAtNextReset = true,
        };
        current.State.Players[0].Field[0][0] = card;
        current.State.Players[0].Morale.Add(morale);
        current.State.Revision++;
        var currentJson = current.SerializeFullState();
        Assert.Contains($"\"{nameof(L12CardInstance.CannotUntapAtNextReset)}\":true", currentJson,
            StringComparison.Ordinal);

        var restoredCurrent = RestoreAndVerifyHash(current);
        Assert.True(Assert.IsType<L12CardInstance>(restoredCurrent.State.Players[0].Field[0][0])
            .CannotUntapAtNextReset);
        Assert.True(Assert.Single(restoredCurrent.State.Players[0].Morale).CannotUntapAtNextReset);
    }

    private static L12GameEngine CreateElizabethScenario(int actor, bool withRagnarok)
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "下次重置锁定回归",
            MasterId = "S01-01M1",
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "next-reset-lock", "NEXTRESET", 921000 + actor,
            ["甲", "乙"], [deck, deck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion);
        game.State.PendingPrompts.Clear();
        game.State.PendingActivations.Clear();
        game.State.EffectStack.Clear();
        game.State.DeferredEffectStack.Clear();
        game.State.PendingTriggerBatches.Clear();
        game.State.PendingTriggerStackCandidates.Clear();
        game.State.ResponseWindow = null;
        game.State.PendingDefense = null;
        game.State.ActivePlayer = actor;
        game.State.FirstPlayer = actor;
        game.State.Round = 2;
        game.State.TurnSerial = 10;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        game.State.DisasterValue = withRagnarok ? 7 : 0;
        game.State.DisasterDeck.Clear();
        if (withRagnarok)
            game.State.DisasterDeck.Add(Card("S01-DS09", "next-reset-ragnarok", owner: null));

        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Removed.Clear();
            player.Resolving.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
            for (var index = 0; index < 10; index++)
                player.Library.Add(Card("S02-0004", $"next-reset-library-{player.PlayerIndex}-{index}",
                    player.PlayerIndex));
        }

        var owner = game.State.Players[actor];
        var opponent = game.State.Players[1 - actor];
        owner.Hand.Add(Card("ST06-01", "next-reset-elizabeth", actor));
        for (var index = 0; index < 7; index++)
        {
            owner.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"next-reset-payment-{actor}-{index}",
                CardId = "S01-01C1",
            });
        }
        opponent.Morale.Add(new L12MoraleCard
        {
            InstanceId = "next-reset-target-morale",
            CardId = "S01-01C2",
            Tapped = true,
        });
        game.State.Revision++;
        return game;
    }

    private static L12GameEngine CreateCleanScenario(int seed, int actor, string? matchId = null)
    {
        var game = new L12GameEngine(Catalog, "next-reset-lock", matchId ?? $"NEXTRESET-{seed}", seed,
            ["甲", "乙"], [Catalog.DeckAt(0), Catalog.DeckAt(1)], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion);
        game.State.PendingPrompts.Clear();
        game.State.PendingActivations.Clear();
        game.State.EffectStack.Clear();
        game.State.DeferredEffectStack.Clear();
        game.State.PendingTriggerBatches.Clear();
        game.State.PendingTriggerStackCandidates.Clear();
        game.State.ResponseWindow = null;
        game.State.PendingDefense = null;
        game.State.ActivePlayer = actor;
        game.State.FirstPlayer = actor;
        game.State.Round = 3;
        game.State.TurnSerial = 12;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        game.State.DisasterValue = 0;
        game.State.DisasterDeck.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Removed.Clear();
            player.Resolving.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
        }
        game.State.Revision++;
        return game;
    }

    private static L12StackItem StackItem(int actor, L12CardInstance source, string trigger,
        string stackItemId)
        => new()
        {
            StackItemId = stackItemId,
            Controller = actor,
            SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId,
            SourceName = source.Name,
            SourceSnapshot = source.Clone(),
            Trigger = trigger,
            Text = source.EffectText ?? source.Name,
        };

    private static object? InvokePrivate(L12GameEngine game, string methodName, params object?[] arguments)
    {
        var candidates = typeof(L12GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)
            .Where(method => method.Name == methodName && method.GetParameters().Length == arguments.Length)
            .Where(method => method.GetParameters().Zip(arguments).All(pair => pair.Second is null
                || pair.First.ParameterType.IsInstanceOfType(pair.Second)))
            .ToArray();
        var method = Assert.Single(candidates);
        return method.Invoke(game, arguments);
    }

    private static string[] ProjectedStatusLabels(L12GameEngine game, string instanceId)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var card = snapshot.GetProperty("players")[0].GetProperty("field")[0].EnumerateArray()
            .First(item => item.ValueKind != JsonValueKind.Null
                && item.GetProperty("instanceId").GetString() == instanceId);
        return card.GetProperty("statusEffects").EnumerateArray()
            .Select(effect => effect.GetProperty("label").GetString() ?? string.Empty).ToArray();
    }

    private static void PlayElizabethAndResolve(ref L12GameEngine game, int actor, bool restoreV2)
    {
        var paymentIds = game.State.Players[actor].Morale.Select(morale => morale.InstanceId).ToList();
        var play = game.Handle(actor, new L12Command("playCard", "next-reset-elizabeth",
            Row: 0, Slot: 0, CardInstanceIds: paymentIds));
        Assert.True(play.Accepted, play.Error);
        Assert.Equal(7, game.State.Players[actor].Morale.Count(morale => morale.Tapped));
        Assert.DoesNotContain(game.State.Players[actor].Hand,
            card => card.InstanceId == "next-reset-elizabeth");
        Assert.Contains(game.State.Players[actor].Field.SelectMany(row => row),
            card => card?.InstanceId == "next-reset-elizabeth");
        ResolvePending(ref game, restoreV2);
    }

    private static void ResolvePending(ref L12GameEngine game, bool restoreV2)
    {
        var guard = 0;
        while (game.State.PendingPrompts.Count > 0)
        {
            Assert.True(++guard <= 50, "提示流程未在限定次数内结束");
            if (restoreV2) game = RestoreAndVerifyHash(game);
            var prompt = game.State.PendingPrompts[0];
            string? choice = null;
            IReadOnlyList<string>? cards = null;
            if (prompt.ValidChoices.Contains("next-reset-target-morale"))
                cards = ["next-reset-target-morale"];
            else if (prompt.Kind == "response" || prompt.ValidChoices.Contains("pass"))
                choice = "pass";
            else if (prompt.Continuation == "disaster-trigger-confirm" || prompt.ValidChoices.Count == 0)
            {
                // No additional declaration is required.
            }
            else if (prompt.Kind is "trigger-order" or "order"
                     && prompt.MinChoose == prompt.ValidChoices.Count
                     && prompt.MaxChoose == prompt.ValidChoices.Count)
                cards = [.. prompt.ValidChoices];
            else
                throw new InvalidOperationException(
                    $"未处理的提示：{prompt.Kind}/{prompt.Continuation}/{string.Join(',', prompt.ValidChoices)}");

            var result = game.Handle(prompt.PlayerIndex, Typed(prompt, choice, cards));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static void AdvanceThroughSecondAffectedReset(ref L12GameEngine game, int affectedPlayer,
        bool restoreV2)
    {
        Assert.Equal(affectedPlayer, game.State.ActivePlayer);
        if (restoreV2) game = RestoreAndVerifyHash(game);
        var firstEnd = game.Handle(affectedPlayer, new L12Command("endTurn"));
        Assert.True(firstEnd.Accepted, firstEnd.Error);
        ResolvePending(ref game, restoreV2);
        Assert.Equal(1 - affectedPlayer, game.State.ActivePlayer);

        if (restoreV2) game = RestoreAndVerifyHash(game);
        var secondEnd = game.Handle(1 - affectedPlayer, new L12Command("endTurn"));
        Assert.True(secondEnd.Accepted, secondEnd.Error);
        ResolvePending(ref game, restoreV2);

        var target = TargetMorale(game, affectedPlayer);
        Assert.Equal(affectedPlayer, game.State.ActivePlayer);
        Assert.False(target.Tapped);
        Assert.False(target.CannotUntapAtNextReset);
        AssertCoreFlowClosed(game);
    }

    private static L12Command Typed(L12Prompt prompt, string? choice,
        IReadOnlyList<string>? cards)
        => new("resolvePrompt", PromptId: prompt.PromptId, Choice: choice,
            CardInstanceIds: cards is null ? null : [.. cards],
            ActivationId: prompt.ActivationId,
            SourceInstanceId: prompt.SourceInstanceId,
            SourceCardId: prompt.SourceCardId,
            Step: prompt.Step,
            CreatedRevision: prompt.CreatedRevision,
            Controller: prompt.Controller);

    private static L12GameEngine RestoreAndVerifyHash(L12GameEngine game)
    {
        var beforeHash = game.ComputeStateHash();
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.Equal(beforeHash, restored.ComputeStateHash());
        return restored;
    }

    private static L12MoraleCard TargetMorale(L12GameEngine game, int opponent)
        => Assert.Single(game.State.Players[opponent].Morale,
            morale => morale.InstanceId == "next-reset-target-morale");

    private static void AssertCoreFlowClosed(L12GameEngine game)
    {
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Null(game.State.ResponseWindow);
        Assert.Null(game.State.PendingDefense);
    }

    private static L12CardInstance Card(string cardId, string instanceId, int? owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = cardId,
            OwnerIndex = owner,
            Name = definition.NameZh,
            CardType = definition.CardType,
            IsCounterTactic = definition.IsCounterTactic,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
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
}
