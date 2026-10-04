using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PublicCardStateFactTransactionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(string matchId, int seed, int stateFormatVersion = 2,
        bool autoPassEmptyResponses = false)
    {
        var game = new L12GameEngine(Catalog, matchId, "CARD-STATE-FACT", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: autoPassEmptyResponses,
            concealHiddenResponseAvailability: false, stateFormatVersion: stateFormatVersion);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.Relic = null;
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            OwnerIndex = owner,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
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

    private static void ResolvePrompt(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void ResolvePrompt(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: [.. choices]));
        Assert.True(result.Accepted, result.Error);
    }

    private static L12GameEngine CreateOlympus(string matchId, int seed)
    {
        var basis = Catalog.DeckAt(0);
        var olympus = new L12PresetDeckDefinition
        {
            Name = "public-state-olympus",
            MasterId = "S02-05M1",
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, matchId, "CARD-STATE-OLYMPUS", seed,
            ["甲", "乙"], [olympus, basis], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.Relic = null;
        }
        return game;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 40
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            ResolvePrompt(game, "pass");
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }

    private static object? InvokePrivate(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName,
                         BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(target.GetType().Name, methodName);
        return method.Invoke(target, args);
    }

    private static void CaptureActivePaidCostSnapshot(L12GameEngine game, int controller,
        L12CardInstance source)
    {
        var snapshot = InvokePrivate(game, "CaptureActivePaidCostSnapshot", controller, source);
        var field = typeof(L12GameEngine).GetField("_activePaidCostSnapshot",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(nameof(L12GameEngine), "_activePaidCostSnapshot");
        field.SetValue(game, snapshot);
    }

    private static L12StackItem ResolveInfiltratorEnter(L12GameEngine game,
        L12CardInstance infiltrator)
    {
        game.State.Players[0].Field[0][0] = infiltrator;
        var item = new L12StackItem
        {
            StackItemId = $"enter-{infiltrator.InstanceId}",
            Controller = 0,
            SourceInstanceId = infiltrator.InstanceId,
            SourceCardId = infiltrator.CardId,
            SourceName = infiltrator.Name,
            SourceSnapshot = infiltrator.Clone(),
            Trigger = "enter",
            Text = infiltrator.EffectText ?? infiltrator.Name,
        };
        game.State.EffectStack.Add(item);
        Assert.True(Assert.IsType<bool>(InvokePrivate(game, "TryResolveS1ExtendedEnter", item, infiltrator)));
        return item;
    }

    private static L12ActionEvent AssertStateFact(L12GameEngine game, string instanceId,
        bool fromTapped, bool toTapped)
    {
        var actionEvent = Assert.Single(game.State.Events, entry => entry.Type == "state"
            && entry.PlayerCardStateTransition?.InstanceId == instanceId);
        Assert.Equal(new L12PlayerCardStateTransition(instanceId, fromTapped, toTapped),
            actionEvent.PlayerCardStateTransition);
        var card = Assert.Single(actionEvent.Cards, candidate => candidate.InstanceId == instanceId);
        Assert.Equal(toTapped, card.Tapped);
        Assert.Equal(actionEvent.PlayerCardStateTransition, Assert.Single(game.UnpersistedEvents,
            entry => entry.Sequence == actionEvent.Sequence).PlayerCardStateTransition);
        return actionEvent;
    }

    [Fact]
    public void ActiveRestPaymentPublishesOneStateFactBeforeItsResponseWindow()
    {
        var game = Create("public-state-active-rest", 26100411);
        var magician = Card("S02-0003", "public-state-magician");
        game.State.Players[0].Field[0][0] = magician;

        var result = game.Handle(0, new L12Command("activateAbility", magician.InstanceId,
            Ability: "disableCounters"));

        Assert.True(result.Accepted, result.Error);
        Assert.True(magician.Tapped);
        var fact = AssertStateFact(game, magician.InstanceId, false, true);
        Assert.Contains(game.State.EffectStack, item => item.SourceInstanceId == magician.InstanceId
            && item.PresentationFactSequences is { } sequences && sequences.SequenceEqual([fact.Sequence]));
        Assert.All(new[] { game.SnapshotFor(0), game.SnapshotFor(1), game.SnapshotForSpectator(), game.SnapshotForReferee() },
            snapshot => Assert.Equal(fact.PlayerCardStateTransition,
                Assert.Single(snapshot.RecentEvents, entry => entry.Sequence == fact.Sequence).PlayerCardStateTransition));
    }

    [Fact]
    public void OnePaymentChangingSourceAndGuardPublishesTwoIndependentSequencesAndOwnerReferences()
    {
        var game = Create("public-state-multi-payment", 26100412);
        var player = game.State.Players[0];
        var source = Card("S01-0215", "public-state-ankh");
        var guard = Card("S01-0212", "public-state-guard");
        player.Relic = source;
        player.Field[0][0] = guard;
        player.Library.Add(Card("S01-0001", "public-state-draw"));

        var begin = game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "ankhDraw"));
        Assert.True(begin.Accepted, begin.Error);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "state");

        ResolvePrompt(game, guard.InstanceId);

        Assert.True(source.Tapped);
        Assert.True(guard.Tapped);
        var sourceFact = AssertStateFact(game, source.InstanceId, false, true);
        var guardFact = AssertStateFact(game, guard.InstanceId, false, true);
        Assert.NotEqual(sourceFact.Sequence, guardFact.Sequence);
        Assert.Equal([sourceFact.Sequence, guardFact.Sequence],
            Assert.IsType<List<long>>(Assert.Single(game.State.EffectStack).PresentationFactSequences));

        PassResponses(game);
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == source.InstanceId));
        Assert.Equal([sourceFact.Sequence, guardFact.Sequence],
            Assert.IsType<long[]>(result.PlayerPresentationFactSequences));
    }

    [Fact]
    public void PreResponseFieldGuardPaymentKeepsItsFactAcrossTheLaterTargetPrompt()
    {
        var game = CreateOlympus("public-state-pre-response-payment", 26100417);
        var player = game.State.Players[0];
        var guard = Card("S01-0212", "public-state-pre-response-guard");
        player.Field[0][0] = guard;
        var target = new L12MoraleCard
        {
            InstanceId = "public-state-pre-response-target", CardId = "S02-05C1",
        };
        player.Morale.Add(target);

        var begin = game.Handle(0, new L12Command("activateAbility", "faction-0",
            Ability: "olympusMoraleFlip"));
        Assert.True(begin.Accepted, begin.Error);
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(guard.InstanceId, payment.ValidChoices);
        ResolvePrompt(game, guard.InstanceId);

        Assert.True(guard.Tapped);
        var fact = AssertStateFact(game, guard.InstanceId, false, true);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("active-ability", targetPrompt.Continuation);
        ResolvePrompt(game, target.InstanceId);

        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal([fact.Sequence], Assert.IsType<List<long>>(item.PresentationFactSequences));
        PassResponses(game);
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == "faction-0"));
        Assert.Equal([fact.Sequence], Assert.IsType<long[]>(result.PlayerPresentationFactSequences));
    }

    [Fact]
    public void SuccessfulReadyPublishesOnlyAfterAuthorityCommitAndBindsTheResult()
    {
        var game = Create("public-state-ready", 26100413, autoPassEmptyResponses: true);
        var player = game.State.Players[0];
        var gram = Card("S01-0317", "public-state-gram");
        gram.Tapped = true;
        player.Relic = gram;
        player.Morale.AddRange([
            new L12MoraleCard { InstanceId = "public-state-morale-1", CardId = "S01-03R1" },
            new L12MoraleCard { InstanceId = "public-state-morale-2", CardId = "S01-03R1" },
        ]);

        var result = game.Handle(0, new L12Command("activateAbility", gram.InstanceId,
            Ability: "gramReady"));

        Assert.True(result.Accepted, result.Error);
        Assert.False(gram.Tapped);
        var fact = AssertStateFact(game, gram.InstanceId, true, false);
        var owner = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.PlayerPresentationFactSequences is { Length: > 0 });
        Assert.Equal([fact.Sequence], Assert.IsType<long[]>(owner.PlayerPresentationFactSequences));
    }

    [Fact]
    public void FailedReadyRevalidationPublishesNoReadyStateFactOrOwnerReference()
    {
        var game = Create("public-state-ready-failed", 26100418, autoPassEmptyResponses: true);
        var player = game.State.Players[0];
        var gram = Card("S01-0317", "public-state-blocked-gram");
        gram.Tapped = true;
        gram.CannotReadyByEffectUntilTurn = game.State.TurnSerial;
        player.Relic = gram;
        player.Morale.AddRange([
            new L12MoraleCard { InstanceId = "public-state-blocked-morale-1", CardId = "S01-03R1" },
            new L12MoraleCard { InstanceId = "public-state-blocked-morale-2", CardId = "S01-03R1" },
        ]);

        var result = game.Handle(0, new L12Command("activateAbility", gram.InstanceId,
            Ability: "gramReady"));

        Assert.True(result.Accepted, result.Error);
        Assert.True(gram.Tapped);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "state"
            && entry.PlayerCardStateTransition?.InstanceId == gram.InstanceId);
        Assert.DoesNotContain(game.State.Events, entry => entry.PlayerPresentationFactSequences is { Length: > 0 });
    }

    [Fact]
    public void SameCardGetsIndependentFactsForTwoLaterLegalRestPayments()
    {
        var game = Create("public-state-repeat-payment", 26100419, autoPassEmptyResponses: true);
        var player = game.State.Players[0];
        var gram = Card("S01-0317", "public-state-repeat-gram");
        player.Relic = gram;

        void AddGraveCosts(string prefix)
        {
            for (var index = 0; index < 4; index++)
                player.Graveyard.Add(Card("S01-0301", $"{prefix}-{index}"));
        }

        void PayGramDamage(string prefix)
        {
            AddGraveCosts(prefix);
            var selected = player.Graveyard.Where(card => card.InstanceId.StartsWith(prefix,
                StringComparison.Ordinal)).Select(card => card.InstanceId).ToArray();
            var begin = game.Handle(0, new L12Command("activateAbility", gram.InstanceId,
                Ability: "gramDamage"));
            Assert.True(begin.Accepted, begin.Error);
            ResolvePrompt(game, selected);
            PassResponses(game);
        }

        PayGramDamage("public-state-repeat-first-cost");
        var first = AssertStateFact(game, gram.InstanceId, false, true);

        player.Morale.AddRange([
            new L12MoraleCard { InstanceId = "public-state-repeat-morale-1", CardId = "S01-03R1" },
            new L12MoraleCard { InstanceId = "public-state-repeat-morale-2", CardId = "S01-03R1" },
        ]);
        var ready = game.Handle(0, new L12Command("activateAbility", gram.InstanceId,
            Ability: "gramReady"));
        Assert.True(ready.Accepted, ready.Error);
        PassResponses(game);
        Assert.False(gram.Tapped);

        PayGramDamage("public-state-repeat-second-cost");
        var rests = game.State.Events.Where(entry => entry.Type == "state"
            && entry.PlayerCardStateTransition == new L12PlayerCardStateTransition(
                gram.InstanceId, false, true)).ToArray();
        Assert.Equal(2, rests.Length);
        Assert.Equal(first.Sequence, rests[0].Sequence);
        Assert.True(rests[1].Sequence > rests[0].Sequence);
    }

    [Fact]
    public void InfiltratorEnteringActivePublishesOneActualRestFact()
    {
        var game = Create("public-state-infiltrator-active-entry", 26100420,
            autoPassEmptyResponses: true);
        var infiltrator = Card("S01-0004", "public-state-infiltrator-active");
        infiltrator.Tapped = false;

        ResolveInfiltratorEnter(game, infiltrator);

        Assert.True(infiltrator.Tapped);
        AssertStateFact(game, infiltrator.InstanceId, false, true);
    }

    [Fact]
    public void InfiltratorAlreadyRestedOnEntryPublishesNoSyntheticStateFact()
    {
        var game = Create("public-state-infiltrator-rested-entry", 26100421,
            autoPassEmptyResponses: true);
        var infiltrator = Card("S01-0004", "public-state-infiltrator-rested");
        infiltrator.Tapped = true;

        ResolveInfiltratorEnter(game, infiltrator);

        Assert.True(infiltrator.Tapped);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "state"
            && entry.PlayerCardStateTransition?.InstanceId == infiltrator.InstanceId);
    }

    [Fact]
    public void LegacyExistingPaidSummaryRemainsByteExactAndAddsNoStateShape()
    {
        var game = Create("public-state-legacy-existing-summary", 26100422,
            stateFormatVersion: 0);
        var magician = Card("S02-0003", "public-state-legacy-existing-summary-card");
        game.State.Players[0].Field[0][0] = magician;
        CaptureActivePaidCostSnapshot(game, 0, magician);
        magician.Tapped = true;
        var data = new Dictionary<string, string>
        {
            ["ability"] = "disableCounters",
            [L12GameEngine.PaidCostSummaryDataKey] = "旧格式精确Cost回执",
        };

        InvokePrivate(game, "AddActivePaidCostPresentation", 0, magician, data);

        Assert.Equal("旧格式精确Cost回执", data[L12GameEngine.PaidCostSummaryDataKey]);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "state"
            || entry.PlayerCardStateTransition is not null
            || entry.PlayerPresentationFactSequences is not null);
        Assert.Equal(2, data.Count);
    }

    [Fact]
    public void ProtocolExistingPaidSummaryStillPublishesTheNewActualStateFact()
    {
        var game = Create("public-state-protocol-existing-summary", 26100423);
        var magician = Card("S02-0003", "public-state-protocol-existing-summary-card");
        game.State.Players[0].Field[0][0] = magician;
        CaptureActivePaidCostSnapshot(game, 0, magician);
        magician.Tapped = true;
        var data = new Dictionary<string, string>
        {
            ["ability"] = "disableCounters",
            [L12GameEngine.PaidCostSummaryDataKey] = "新协议既有精确Cost回执",
        };

        InvokePrivate(game, "AddActivePaidCostPresentation", 0, magician, data);

        Assert.Equal("新协议既有精确Cost回执", data[L12GameEngine.PaidCostSummaryDataKey]);
        AssertStateFact(game, magician.InstanceId, false, true);
        Assert.Equal(4, data.Count);
    }

    [Fact]
    public void UsualTrialActionPublishesOneRestFactOnItsOwnTransaction()
    {
        var game = Create("public-state-trial-action", 26100414, autoPassEmptyResponses: true);
        var trialLegion = Card("S02-0609", "public-state-trial-legion");
        game.State.Players[0].Field[0][0] = trialLegion;
        var openTrial = Card("S02-06S4", "public-state-open-trial");
        openTrial.TrialProgress = 0;
        game.State.Players[0].SpecialZones.Trials.Add(openTrial);

        var result = game.Handle(0, new L12Command("activateAbility", trialLegion.InstanceId,
            Ability: "trialAdvance"));

        Assert.True(result.Accepted, result.Error);
        Assert.True(trialLegion.Tapped);
        var fact = AssertStateFact(game, trialLegion.InstanceId, false, true);
        var trialAction = Assert.Single(game.State.Events, entry => entry.Type == "trial-action");
        Assert.Equal([fact.Sequence], Assert.IsType<long[]>(trialAction.PlayerPresentationFactSequences));
    }

    [Fact]
    public void RecipientStripsAStateFactWhosePublicSnapshotDoesNotMatchToTapped()
    {
        var game = Create("public-state-recipient-state-mismatch", 26100415);
        var card = Card("S02-0003", "public-state-recipient-card");
        card.Tapped = false;
        game.State.Players[0].Field[0][0] = card;
        var malformed = new L12ActionEvent(1, "state", 0, "mismatched state", [card.Clone()])
        {
            PlayerCardStateTransition = new(card.InstanceId, false, true),
        };

        var projected = L12RecipientVisibility.ProjectActionEvent(game.State, malformed, 1, false);

        Assert.Null(projected.PlayerCardStateTransition);
    }

    [Fact]
    public void LegacyProtocolActiveRestKeepsItsOriginalEventShape()
    {
        var game = Create("public-state-legacy", 26100416, stateFormatVersion: 0,
            autoPassEmptyResponses: true);
        var magician = Card("S02-0003", "public-state-legacy-magician");
        game.State.Players[0].Field[0][0] = magician;

        var result = game.Handle(0, new L12Command("activateAbility", magician.InstanceId,
            Ability: "disableCounters"));

        Assert.True(result.Accepted, result.Error);
        Assert.False(game.State.PresentationFactProtocolEnabled);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "state"
            || entry.PlayerCardStateTransition is not null
            || entry.PlayerPresentationFactSequences is not null);
    }
}
