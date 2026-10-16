using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

/// <summary>
/// Regression for the approved rule: each physical covered counter-tactic source receives
/// its own trigger candidate. This file is self-contained and depends only on the repository test
/// project plus its copied Data directory; it must not reference the D: evidence directory.
///
/// Expected baseline before the product patch: all seven cases are RED because the three
/// affected builders select only the first physical card with FirstOrDefault.
/// </summary>
public sealed class CounterTacticPhysicalSourceMultiplicityRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12PresetDeckDefinition Deck(string masterId, string name)
    {
        var basis = Catalog.DeckAt(0);
        return new L12PresetDeckDefinition
        {
            Name = name,
            MasterId = masterId,
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
    }

    private static L12GameEngine Create(int seed = 921701, string? firstMasterId = null,
        string? secondMasterId = null)
    {
        var basis = Catalog.DeckAt(0);
        var firstDeck = firstMasterId is null ? basis : Deck(firstMasterId, "第一方测试牌库");
        var secondDeck = secondMasterId is null ? basis : Deck(secondMasterId, "第二方测试牌库");
        var game = new L12GameEngine(Catalog, "counter-physical-source-multiplicity", "COUNTER-PHYSICAL",
            seed, ["甲", "乙"], [firstDeck, secondDeck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        Reset(game, activePlayer: 1);
        return game;
    }

    private static L12GameEngine CreatePaidGiftChain(int actor, int seed)
    {
        var owner = 1 - actor;
        var decks = new L12PresetDeckDefinition[2];
        decks[actor] = Deck("S01-01M1", "纷乱箭方");
        decks[owner] = Deck("S01-01M1", "不朽之礼方");
        var game = new L12GameEngine(Catalog, "paid-two-gifts-three-guards", "COUNTER-PHYSICAL",
            seed, ["甲", "乙"], decks, skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        Reset(game, activePlayer: owner);

        var giftOwner = game.State.Players[owner];
        var arrowsActor = game.State.Players[actor];
        for (var index = 0; index < 3; index++)
            giftOwner.Field[0][index] = Card("S01-0212", $"guard-{owner}-{index}", owner);
        for (var index = 0; index < 2; index++)
            giftOwner.Hand.Add(Card("S01-0223", $"gift-{owner}-{index}", owner));
        for (var index = 0; index < 4; index++)
            giftOwner.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"gift-morale-{owner}-{index}",
                CardId = "S01-01C1",
            });
        for (var index = 0; index < 6; index++)
            giftOwner.Library.Add(Card("S01-0201", $"gift-draw-{owner}-{index}", owner));

        arrowsActor.Hand.Add(Card("S02-0011", $"arrows-{actor}", actor));
        for (var index = 0; index < 3; index++)
            arrowsActor.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"arrows-morale-{actor}-{index}",
                CardId = "S01-01C1",
            });
        for (var index = 0; index < 3; index++)
            arrowsActor.Library.Add(Card("S01-0001", $"actor-draw-{actor}-{index}", actor));
        game.State.Revision++;
        return game;
    }

    private static void Reset(L12GameEngine game, int activePlayer)
    {
        game.State.PendingPrompts.Clear();
        game.State.PendingActivations.Clear();
        game.State.EffectStack.Clear();
        game.State.DeferredEffectStack.Clear();
        game.State.PendingTriggerBatches.Clear();
        game.State.PendingTriggerStackCandidates.Clear();
        game.State.ResponseWindow = null;
        game.State.PendingDefense = null;
        game.State.ActivePlayer = activePlayer;
        game.State.FirstPlayer = activePlayer;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
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
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
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

    private static object? Invoke(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, methodName);
        return method.Invoke(target, args);
    }

    private static IEnumerable<L12TriggerCandidate> QueuedCandidates(L12GameEngine game)
        => game.State.PendingTriggerBatches.SelectMany(batch => batch.Candidates)
            .Concat(game.State.PendingTriggerStackCandidates);

    private static L12GameEngine RestoreAsV2(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

    private static L12Command Typed(L12Prompt prompt, string? choice = null,
        IReadOnlyList<string>? cards = null)
        => new("resolvePrompt", PromptId: prompt.PromptId, Choice: choice,
            CardInstanceIds: cards is null ? null : [.. cards], ActivationId: prompt.ActivationId,
            SourceInstanceId: prompt.SourceInstanceId, SourceCardId: prompt.SourceCardId,
            Step: prompt.Step, CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller);

    private static string Fingerprint(L12GameEngine game) => JsonSerializer.Serialize(new
    {
        game.State.ActivePlayer,
        game.State.Round,
        game.State.TurnSerial,
        game.State.Phase,
        prompts = game.State.PendingPrompts.Select(prompt => new
        {
            prompt.PromptId,
            prompt.PlayerIndex,
            prompt.Kind,
            prompt.Continuation,
            prompt.ActivationId,
            prompt.SourceInstanceId,
            prompt.SourceCardId,
            prompt.Step,
            prompt.CreatedRevision,
            prompt.Controller,
            prompt.ValidChoices,
        }),
        stack = game.State.EffectStack,
        deferred = game.State.DeferredEffectStack,
        activations = game.State.PendingActivations,
        batches = game.State.PendingTriggerBatches,
        candidates = game.State.PendingTriggerStackCandidates,
        players = game.State.Players.Select(player => new
        {
            field = player.Field.Select(row => row.Select(card => card is null ? null : new
                { card.InstanceId, card.CardId, card.Hidden, card.Tapped, card.CurrentCost, card.CurrentTroops })),
            hand = player.Hand.Select(card => card.InstanceId),
            library = player.Library.Select(card => card.InstanceId),
            grave = player.Graveyard.Select(card => card.InstanceId),
            removed = player.Removed.Select(card => card.InstanceId),
            resolving = player.Resolving.Select(card => card.InstanceId),
            morale = player.Morale.Select(morale => new { morale.InstanceId, morale.Tapped }),
        }),
        game.State.ResponseWindow,
        game.State.PendingDefense,
    });

    private static int ZoneCount(L12PlayerState player, string instanceId)
        => player.Field.SelectMany(row => row).Count(card => card?.InstanceId == instanceId)
            + player.Hand.Count(card => card.InstanceId == instanceId)
            + player.Library.Count(card => card.InstanceId == instanceId)
            + player.Graveyard.Count(card => card.InstanceId == instanceId)
            + player.Removed.Count(card => card.InstanceId == instanceId)
            + player.Resolving.Count(card => card.InstanceId == instanceId)
            + (player.Relic?.InstanceId == instanceId ? 1 : 0)
            + player.ExtraRelics.Count(card => card.InstanceId == instanceId);

    private static void AddReadyMorale(L12PlayerState player, int count, string prefix)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"{prefix}-{index}",
                CardId = "S01-01C1",
            });
    }

    private static void AssertFullyCleared(L12GameEngine game)
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

    private static L12TriggerCandidate[] QueueTwoPhysicalSourcesForPrune(
        L12GameEngine game, string cardId, L12CardInstance first, L12CardInstance second)
    {
        var player = game.State.Players[0];
        first.Hidden = second.Hidden = true;
        first.OwnerIndex = second.OwnerIndex = player.PlayerIndex;
        player.Field[1][0] = first;
        player.Field[1][1] = second;

        game.State.IsResolvingStack = true;
        if (cardId == "S01-0021")
        {
            player.Hand.Add(Card("S01-0003", $"{cardId}-prune-entry", player.PlayerIndex));
            Invoke(game, "QueueS1MasterDamageReactionCore", player.PlayerIndex, 1, false, false);
        }
        else
        {
            if (cardId == "S01-0223")
            {
                player.Graveyard.Add(Card("S01-0212", $"{cardId}-prune-guard", player.PlayerIndex));
                player.Library.Add(Card("S01-0201", $"{cardId}-prune-draw", player.PlayerIndex));
            }
            else
            {
                player.Graveyard.Add(Card("S01-0311", $"{cardId}-prune-grave-a", player.PlayerIndex));
                player.Graveyard.Add(Card("S01-0312", $"{cardId}-prune-grave-b", player.PlayerIndex));
            }
            var left = Card("S01-0309", $"{cardId}-prune-leave", player.PlayerIndex);
            var built = Assert.IsAssignableFrom<IEnumerable<L12TriggerCandidate>>(
                Invoke(game, "BuildS1LeaveReactionCandidates", player.PlayerIndex, left, true, true))
                .Where(candidate => candidate.SourceCardId == cardId)
                .ToArray();
            Invoke(game, "QueueTriggerCandidates", (object)built);
        }

        return QueuedCandidates(game).Where(candidate => candidate.SourceCardId == cardId).ToArray();
    }

    [Theory]
    [InlineData("S01-0223")]
    [InlineData("S01-0320")]
    [Trait("L12Evidence", "family:counter-tactic-physical-source-multiplicity")]
    public void LeaveReactionBuilderCreatesOneCandidatePerPhysicalCoveredCounter(string cardId)
    {
        var game = Create(921701 + (cardId == "S01-0223" ? 1 : 2));
        var player = game.State.Players[0];
        var first = Card(cardId, $"{cardId}-physical-first");
        var second = Card(cardId, $"{cardId}-physical-second");
        first.Hidden = second.Hidden = true;
        first.OwnerIndex = second.OwnerIndex = player.PlayerIndex;
        player.Field[1][0] = first;
        player.Field[1][1] = second;
        var left = Card("S01-0212", $"{cardId}-eligible-leave");
        left.CostModifier = 3;

        var candidates = Assert.IsAssignableFrom<IEnumerable<L12TriggerCandidate>>(
            Invoke(game, "BuildS1LeaveReactionCandidates", player.PlayerIndex, left, true, true))
            .Where(candidate => candidate.SourceCardId == cardId)
            .ToArray();

        Assert.Equal(new[] { first.InstanceId, second.InstanceId },
            candidates.Select(candidate => candidate.SourceInstanceId).ToArray());
        Assert.All(candidates, candidate => Assert.Equal("reaction", candidate.Trigger));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0021")]
    [Trait("L12Evidence", "family:counter-tactic-physical-source-multiplicity")]
    public void MasterDamageBuilderCreatesOneCandidatePerPhysicalRegencyCounter()
    {
        var game = Create(921704);
        var player = game.State.Players[0];
        var first = Card("S01-0021", "regency-physical-first");
        var second = Card("S01-0021", "regency-physical-second");
        first.Hidden = second.Hidden = true;
        first.OwnerIndex = second.OwnerIndex = player.PlayerIndex;
        player.Field[1][0] = first;
        player.Field[1][1] = second;

        game.State.IsResolvingStack = true;
        Invoke(game, "QueueS1MasterDamageReactionCore", player.PlayerIndex, 1, false, false);

        var candidates = QueuedCandidates(game)
            .Where(candidate => candidate.SourceCardId == "S01-0021")
            .ToArray();
        Assert.Equal(new[] { first.InstanceId, second.InstanceId },
            candidates.Select(candidate => candidate.SourceInstanceId).ToArray());
        Assert.All(candidates, candidate => Assert.Equal("reaction", candidate.Trigger));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "interaction:S02-0011+S01-0212+S01-0223")]
    [Trait("L12Evidence", "family:counter-tactic-physical-source-multiplicity")]
    public void PaidTwoGiftChaoticArrowsChainLetsEachPhysicalGiftResolveOnce(int actor, bool restoreEveryPrompt)
    {
        var owner = 1 - actor;
        var game = CreatePaidGiftChain(actor, 921710 + actor * 10 + (restoreEveryPrompt ? 1 : 0));
        var giftIds = Enumerable.Range(0, 2).Select(index => $"gift-{owner}-{index}").ToArray();
        var guardIds = Enumerable.Range(0, 3).Select(index => $"guard-{owner}-{index}").ToArray();

        for (var giftIndex = 0; giftIndex < giftIds.Length; giftIndex++)
        {
            var payment = Enumerable.Range(giftIndex * 2, 2)
                .Select(index => $"gift-morale-{owner}-{index}").ToArray();
            var cover = game.Handle(owner, new L12Command("playCard", giftIds[giftIndex], Row: 1,
                Slot: giftIndex, CardInstanceIds: [.. payment]));
            Assert.True(cover.Accepted, cover.Error);
        }
        Assert.Equal(4, game.State.Players[owner].Morale.Count(morale => morale.Tapped));
        Assert.All(giftIds, id => Assert.Contains(game.State.Players[owner].Field[1],
            card => card?.InstanceId == id && card.Hidden));

        var end = game.Handle(owner, new L12Command("endTurn"));
        Assert.True(end.Accepted, end.Error);
        Assert.Equal(actor, game.State.ActivePlayer);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        _ = game.SnapshotFor(owner);
        Assert.All(guardIds, id => Assert.True(game.State.Players[owner].Field.SelectMany(row => row)
            .Single(card => card?.InstanceId == id)!.CurrentCost > 2));

        var ownerHandBefore = game.State.Players[owner].Hand.Count;
        var ownerLibraryBefore = game.State.Players[owner].Library.Count;
        var play = game.Handle(actor, new L12Command("playCard", $"arrows-{actor}"));
        Assert.True(play.Accepted, play.Error);
        Assert.Single(game.State.PendingPrompts);
        Resolve(cards: guardIds);

        var declarationUses = 0;
        var selectedGuardIds = new List<string>();
        for (var safety = 0; game.State.PendingPrompts.Count > 0; safety++)
        {
            Assert.True(safety < 80, "提示链未在有界次数内清空");
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.Kind == "response")
            {
                Resolve(choice: "pass");
                continue;
            }
            if (prompt.Kind == "trigger-order"
                || prompt.Continuation.Contains("trigger-batch-order", StringComparison.Ordinal))
            {
                Resolve(cards: prompt.ValidChoices.ToArray());
                continue;
            }
            if (prompt.ValidChoices.Contains("mode:use") && prompt.ValidChoices.Contains("mode:none"))
            {
                declarationUses++;
                Resolve(choice: "mode:use");
                continue;
            }

            var guardChoice = prompt.ValidChoices.FirstOrDefault(id =>
                guardIds.Contains(id, StringComparer.Ordinal)
                && !selectedGuardIds.Contains(id, StringComparer.Ordinal));
            if (guardChoice is not null)
            {
                selectedGuardIds.Add(guardChoice);
                Resolve(choice: guardChoice);
                continue;
            }
            if (game.State.PendingActivations.Count > 0 && prompt.ValidChoices.Count > 0)
            {
                Resolve(choice: prompt.ValidChoices[0]);
                continue;
            }
            throw new Xunit.Sdk.XunitException($"Unexpected prompt: {JsonSerializer.Serialize(new
            {
                prompt.Kind,
                prompt.Continuation,
                prompt.SourceCardId,
                prompt.ValidChoices,
            })}");
        }

        var giftOwner = game.State.Players[owner];
        var arrowsActor = game.State.Players[actor];
        var resolvedByGift = giftIds.ToDictionary(id => id, id => game.State.Events.Count(entry =>
            entry.Type == "effect-result" && entry.EffectSegmentIndex == 1
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.InstanceId == id)));

        Assert.Equal(2, declarationUses);
        Assert.Equal(2, selectedGuardIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(resolvedByGift.Values, count => Assert.Equal(1, count));
        Assert.All(giftIds, id => Assert.Equal(1, ZoneCount(giftOwner, id)));
        Assert.All(guardIds, id => Assert.Equal(1, ZoneCount(giftOwner, id)));
        Assert.Equal(2, giftIds.Sum(id => giftOwner.Graveyard.Count(card => card.InstanceId == id)));
        Assert.Equal(2, guardIds.Count(id => giftOwner.Field.SelectMany(row => row)
            .Any(card => card?.InstanceId == id)));
        Assert.Equal(ownerHandBefore + 2, giftOwner.Hand.Count);
        Assert.Equal(ownerLibraryBefore - 2, giftOwner.Library.Count);
        Assert.Equal(3, arrowsActor.Morale.Count(morale => morale.Tapped));
        Assert.Single(arrowsActor.Graveyard, card => card.InstanceId == $"arrows-{actor}");
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Null(game.State.ResponseWindow);
        Assert.Null(game.State.PendingDefense);

        void Resolve(string? choice = null, IReadOnlyList<string>? cards = null)
        {
            Assert.Single(game.State.PendingPrompts);
            if (restoreEveryPrompt)
            {
                var before = Fingerprint(game);
                game = RestoreAsV2(game);
                Assert.Equal(before, Fingerprint(game));
            }
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (cards is null)
                Assert.Contains(choice, prompt.ValidChoices);
            else
                Assert.All(cards, id => Assert.Contains(id, prompt.ValidChoices));
            var result = game.Handle(prompt.PlayerIndex, Typed(prompt, choice, cards));
            Assert.True(result.Accepted, result.Error);
        }
    }

    [Theory]
    [InlineData("S01-0021", "left")]
    [InlineData("S01-0223", "face-up")]
    [InlineData("S01-0320", "unavailable")]
    [Trait("L12Evidence", "family:counter-tactic-physical-source-multiplicity")]
    [Trait("L12Evidence", "privacy:hidden-counter-prune")]
    public void InvalidFirstPhysicalSourceIsPrunedWhileSecondKeepsItsPrivateDeclaration(
        string cardId, string invalidation)
    {
        var game = Create(921730 + cardId.Length);
        var player = game.State.Players[0];
        var first = Card(cardId, $"{cardId}-invalid-first", player.PlayerIndex);
        var second = Card(cardId, $"{cardId}-valid-second", player.PlayerIndex);
        var candidates = QueueTwoPhysicalSourcesForPrune(game, cardId, first, second);
        Assert.Equal(new[] { first.InstanceId, second.InstanceId },
            candidates.Select(candidate => candidate.SourceInstanceId).ToArray());

        switch (invalidation)
        {
            case "left":
                player.Field[1][0] = null;
                player.Graveyard.Add(first);
                break;
            case "face-up":
                first.Hidden = false;
                break;
            case "unavailable":
                first.CannotRespondUntilRound = game.State.Round;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidation));
        }

        game.State.IsResolvingStack = false;
        Invoke(game, "AdvanceTriggerBatches");

        var invalid = Assert.Single(candidates, candidate => candidate.SourceInstanceId == first.InstanceId);
        var valid = Assert.Single(candidates, candidate => candidate.SourceInstanceId == second.InstanceId);
        Assert.Equal("true", invalid.Data.GetValueOrDefault("counterTacticResponseLifecycleConsumed"));
        Assert.False(valid.Data.ContainsKey("counterTacticResponseLifecycleConsumed"));
        Assert.DoesNotContain(QueuedCandidates(game), candidate => candidate.SourceInstanceId == first.InstanceId);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        var activation = Assert.Single(game.State.PendingActivations);
        Assert.Equal(second.InstanceId, activation.SourceInstanceId);
        Assert.Single(game.SnapshotFor(player.PlayerIndex).Prompts);
        Assert.Empty(game.SnapshotFor(1 - player.PlayerIndex).Prompts);
        var privateSkip = Assert.Single(game.State.Events, entry =>
            entry.Type == "private-trigger-effect-skipped"
            && entry.Text.Contains(first.Name, StringComparison.Ordinal));
        Assert.Contains(game.SnapshotFor(player.PlayerIndex).RecentEvents,
            entry => entry.Sequence == privateSkip.Sequence);
        Assert.DoesNotContain(game.SnapshotFor(1 - player.PlayerIndex).RecentEvents,
            entry => entry.Sequence == privateSkip.Sequence);
    }

    [Theory]
    [InlineData("S01-0021")]
    [InlineData("S01-0223")]
    [InlineData("S01-0320")]
    [Trait("L12Evidence", "family:counter-tactic-physical-source-multiplicity")]
    [Trait("L12Evidence", "privacy:hidden-counter-prune")]
    public void TwoInvalidPhysicalSourcesLeaveNoEmptyOrderOrDeclarationPrompt(string cardId)
    {
        var game = Create(921740 + cardId.Length);
        var player = game.State.Players[0];
        var first = Card(cardId, $"{cardId}-invalid-both-first", player.PlayerIndex);
        var second = Card(cardId, $"{cardId}-invalid-both-second", player.PlayerIndex);
        var candidates = QueueTwoPhysicalSourcesForPrune(game, cardId, first, second);
        Assert.Equal(2, candidates.Length);

        first.Hidden = false;
        second.CannotRespondUntilRound = game.State.Round;
        game.State.IsResolvingStack = false;
        Invoke(game, "AdvanceTriggerBatches");

        Assert.All(candidates, candidate =>
            Assert.Equal("true", candidate.Data.GetValueOrDefault("counterTacticResponseLifecycleConsumed")));
        AssertFullyCleared(game);
        var privateSkips = game.State.Events.Where(entry =>
            entry.Type == "private-trigger-effect-skipped"
            && entry.Text.Contains(first.Name, StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, privateSkips.Length);
        Assert.All(privateSkips, skipped => Assert.Contains(game.SnapshotFor(0).RecentEvents,
            entry => entry.Sequence == skipped.Sequence));
        Assert.All(privateSkips, skipped => Assert.DoesNotContain(game.SnapshotFor(1).RecentEvents,
            entry => entry.Sequence == skipped.Sequence));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0021")]
    [Trait("L12Evidence", "entry:real-master-ability-damage")]
    [Trait("L12Evidence", "family:counter-tactic-physical-source-multiplicity")]
    public void RealMasterDamageLetsBothPhysicalRegenciesDeclareAndResolveOnce()
    {
        var game = Create(921750, secondMasterId: "S01-01M1");
        var defender = game.State.Players[0];
        var attacker = game.State.Players[1];
        var regencyIds = new[] { "regency-real-first", "regency-real-second" };
        var entrantIds = new[] { "regency-entry-first", "regency-entry-second" };
        for (var index = 0; index < regencyIds.Length; index++)
        {
            var regency = Card("S01-0021", regencyIds[index], defender.PlayerIndex);
            regency.Hidden = true;
            regency.SetRound = 0;
            defender.Field[1][index] = regency;
            defender.Hand.Add(Card("S01-0003", entrantIds[index], defender.PlayerIndex));
        }
        AddReadyMorale(attacker, 4, "regency-real-morale");

        var started = game.Handle(attacker.PlayerIndex,
            new L12Command("activateAbility", $"master-{attacker.PlayerIndex}", Ability: "nonLethal"));
        Assert.True(started.Accepted, started.Error);
        var declaredSources = new HashSet<string>(StringComparer.Ordinal);
        var declaredEntrants = new HashSet<string>(StringComparer.Ordinal);

        for (var safety = 0; game.State.PendingPrompts.Count > 0; safety++)
        {
            Assert.True(safety < 100, "摄政双实体声明链未在有界次数内清空");
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.Kind == "response")
            {
                Submit(choice: "pass");
                continue;
            }
            if (prompt.Kind == "trigger-order" || prompt.Continuation.Contains("trigger-batch-order", StringComparison.Ordinal))
            {
                Submit(cards: prompt.ValidChoices.Where(choice => choice != "skip").ToArray());
                continue;
            }

            var activation = game.State.PendingActivations.FirstOrDefault(item =>
                item.ActivationId == prompt.ActivationId
                || item.ActivationId == prompt.Data.GetValueOrDefault("activationId"));
            if (activation?.SourceCardId == "S01-0021")
            {
                declaredSources.Add(activation.SourceInstanceId);
                var entrant = entrantIds.FirstOrDefault(id =>
                    prompt.ValidChoices.Contains(id) && declaredEntrants.Add(id));
                if (entrant is not null)
                {
                    Submit(choice: entrant);
                    continue;
                }
                if (prompt.Kind == "slot")
                {
                    Submit(choice: prompt.ValidChoices[0]);
                    continue;
                }
            }
            if (prompt.ValidChoices.Contains("mode:none"))
            {
                Submit(choice: "mode:none");
                continue;
            }
            if (prompt.ValidChoices.Contains("skip"))
            {
                Submit(choice: "skip");
                continue;
            }
            throw new Xunit.Sdk.XunitException($"未预期的摄政双实体提示：{prompt.Kind}/{prompt.Continuation}");
        }

        Assert.Equal(regencyIds.Order(), declaredSources.Order());
        Assert.Equal(entrantIds.Order(), declaredEntrants.Order());
        Assert.All(regencyIds, id => Assert.Contains(defender.Graveyard, card => card.InstanceId == id));
        Assert.All(entrantIds, id => Assert.Contains(defender.Field.SelectMany(row => row),
            card => card?.InstanceId == id));
        Assert.All(regencyIds, id => Assert.Single(game.State.Events, entry =>
            entry.Type == "effect-result" && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.InstanceId == id)));
        AssertFullyCleared(game);

        void Submit(string? choice = null, IReadOnlyList<string>? cards = null)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            var result = game.Handle(prompt.PlayerIndex, Typed(prompt, choice, cards));
            Assert.True(result.Accepted, result.Error);
        }
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0320")]
    [Trait("L12Evidence", "entry:real-gm-destroy-command")]
    [Trait("L12Evidence", "family:counter-tactic-physical-source-multiplicity")]
    public void RealDeathLetsBothPhysicalBloodEaglesDeclareAndResolveOnce()
    {
        var game = Create(921760);
        var owner = game.State.Players[0];
        var bloodIds = new[] { "blood-real-first", "blood-real-second" };
        for (var index = 0; index < bloodIds.Length; index++)
        {
            var blood = Card("S01-0320", bloodIds[index], owner.PlayerIndex);
            blood.Hidden = true;
            blood.SetRound = 0;
            owner.Field[1][index] = blood;
        }
        var fallen = Card("S01-0309", "blood-real-fallen", owner.PlayerIndex);
        owner.Field[0][0] = fallen;
        var targetIds = Enumerable.Range(0, 4).Select(index => $"blood-real-target-{index}").ToArray();
        owner.Graveyard.Add(Card("S01-0311", targetIds[0], owner.PlayerIndex));
        owner.Graveyard.Add(Card("S01-0312", targetIds[1], owner.PlayerIndex));
        owner.Graveyard.Add(Card("S01-0311", targetIds[2], owner.PlayerIndex));
        owner.Graveyard.Add(Card("S01-0312", targetIds[3], owner.PlayerIndex));

        var destroyed = game.HandleGm(new L12GmCommand("destroyCard", owner.PlayerIndex,
            CardInstanceId: fallen.InstanceId));
        Assert.True(destroyed.Accepted, destroyed.Error);
        var declaredSources = new HashSet<string>(StringComparer.Ordinal);
        var selectedTargets = new HashSet<string>(StringComparer.Ordinal);

        for (var safety = 0; game.State.PendingPrompts.Count > 0; safety++)
        {
            Assert.True(safety < 120, "复仇血鹰双实体声明链未在有界次数内清空");
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.Kind == "trigger-order" || prompt.Continuation.Contains("trigger-batch-order", StringComparison.Ordinal))
            {
                Submit(cards: prompt.ValidChoices.Where(choice => choice != "skip").ToArray());
                continue;
            }
            if (prompt.Kind == "response")
            {
                Submit(choice: prompt.ValidChoices.Contains("pass") ? "pass" : "no");
                continue;
            }

            var activation = game.State.PendingActivations.FirstOrDefault(item =>
                item.ActivationId == prompt.ActivationId
                || item.ActivationId == prompt.Data.GetValueOrDefault("activationId"));
            if (activation?.SourceCardId == "S01-0320")
            {
                declaredSources.Add(activation.SourceInstanceId);
                var eligible = targetIds.Where(id => prompt.ValidChoices.Contains(id)
                    && !selectedTargets.Contains(id)).ToArray();
                if (prompt.MaxChoose >= 2 && eligible.Length >= 2)
                {
                    var chosen = eligible.Take(2).ToArray();
                    selectedTargets.UnionWith(chosen);
                    Submit(cards: chosen);
                    continue;
                }
                var destination = prompt.ValidChoices.FirstOrDefault(selectedTargets.Contains);
                if (destination is not null)
                {
                    Submit(choice: destination);
                    continue;
                }
            }
            if (prompt.ValidChoices.Contains("mode:none"))
            {
                Submit(choice: "mode:none");
                continue;
            }
            if (prompt.ValidChoices.Contains("no"))
            {
                Submit(choice: "no");
                continue;
            }
            throw new Xunit.Sdk.XunitException($"未预期的复仇血鹰双实体提示：{prompt.Kind}/{prompt.Continuation}");
        }

        Assert.Equal(bloodIds.Order(), declaredSources.Order());
        Assert.Equal(4, selectedTargets.Count);
        Assert.All(bloodIds, id => Assert.Contains(owner.Graveyard, card => card.InstanceId == id));
        Assert.All(targetIds, id => Assert.Equal(1, ZoneCount(owner, id)));
        Assert.All(bloodIds, id => Assert.Single(game.State.Events, entry =>
            entry.Type == "effect-result" && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.InstanceId == id)));
        AssertFullyCleared(game);

        void Submit(string? choice = null, IReadOnlyList<string>? cards = null)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            var result = game.Handle(prompt.PlayerIndex, Typed(prompt, choice, cards));
            Assert.True(result.Accepted, result.Error);
        }
    }
}
