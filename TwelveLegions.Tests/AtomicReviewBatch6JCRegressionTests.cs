using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6JCRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var deck = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "atomic-review-batch6jc", "ATOMIC6JC", seed,
            ["甲", "乙"], [deck, deck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 8;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.UsedAbilities.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
            OwnerIndex = owner,
        };
    }

    private static void InvokeVoid(L12GameEngine game, string methodName, params object?[] arguments)
    {
        var method = typeof(L12GameEngine).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(game, arguments);
    }

    private static L12Prompt ResolveOnlyPrompt(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var command = choices.Length == 1
            ? new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choices[0])
            : new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [.. choices]);
        var result = game.Handle(prompt.PlayerIndex, command);
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static L12MoraleCard Morale(string instanceId)
        => new() { CardId = "S01-01C1", InstanceId = instanceId };

    private static L12StackItem BeginOkitaTop(L12GameEngine game, L12CardInstance top)
    {
        var player = game.State.Players[0];
        var okita = Card("S02-0403", $"batch6jc-okita-{top.InstanceId}");
        player.Field[0][0] = okita;
        player.Library.Add(top);
        var item = new L12StackItem
        {
            StackItemId = $"batch6jc-parent-{top.InstanceId}",
            Controller = 0,
            SourceInstanceId = okita.InstanceId,
            SourceCardId = okita.CardId,
            SourceName = okita.Name,
            Trigger = "attack",
            Text = "冲田总司进攻时效果",
        };
        game.State.EffectStack.Add(item);
        InvokeVoid(game, "BeginS2OkitaAttack", item);
        return item;
    }

    private static L12StackItem BeginLiMuRevealedTactic(L12GameEngine game, L12CardInstance tactic,
        int liMuRow = 0)
    {
        var player = game.State.Players[0];
        var liMu = Card("S02-0102", $"batch6jc-limu-{tactic.InstanceId}");
        player.Field[liMuRow][0] = liMu;
        player.Library.Add(tactic);
        var item = new L12StackItem
        {
            StackItemId = $"batch6jc-limu-parent-{tactic.InstanceId}",
            Controller = 0,
            SourceInstanceId = liMu.InstanceId,
            SourceCardId = liMu.CardId,
            SourceName = liMu.Name,
            Trigger = "enter",
            Text = "李牧展示段",
        };
        item.Data["s2-limu-top"] = tactic.InstanceId;
        game.State.EffectStack.Add(item);
        InvokeVoid(game, "PlayS2LiMuRevealedTactic", item);
        return item;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.Count > 0; safety++)
        {
            var prompt = game.State.PendingPrompts[0];
            if (prompt.Kind != "response") break;
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static L12GameEngine RestoreAndAssertStableSequences(L12GameEngine game)
    {
        var stateHash = game.ComputeStateHash();
        var randomState = game.RandomState ?? throw new InvalidOperationException("V2 fixture requires deterministic RNG state");
        var cardFactSignalSequence = game.CardFactSignalSequence;
        var revision = game.State.Revision;
        var eventSequence = game.State.EventSequence;
        var promptSequence = game.State.PromptSequence;
        var stackSequence = game.State.StackSequence;
        var activationSequence = game.State.ActivationSequence;
        var triggerBatchSequence = game.State.TriggerBatchSequence;
        var authorityEventSequence = game.State.AuthorityEventSequence;

        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), randomState,
            cardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

        Assert.True(restored.RandomState.HasValue);
        Assert.Equal(randomState, restored.RandomState.Value);
        Assert.Equal(cardFactSignalSequence, restored.CardFactSignalSequence);
        Assert.Equal(revision, restored.State.Revision);
        Assert.Equal(eventSequence, restored.State.EventSequence);
        Assert.Equal(promptSequence, restored.State.PromptSequence);
        Assert.Equal(stackSequence, restored.State.StackSequence);
        Assert.Equal(activationSequence, restored.State.ActivationSequence);
        Assert.Equal(triggerBatchSequence, restored.State.TriggerBatchSequence);
        Assert.Equal(authorityEventSequence, restored.State.AuthorityEventSequence);
        Assert.Equal(stateHash, restored.ComputeStateHash());
        return restored;
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "entry:batch6jc-limu-observing-stars-full-morale-parent-close")]
    [Trait("L12Evidence", "card:S02-0102")]
    [Trait("L12Evidence", "card:S01-0119")]
    public void LiMuFreePlayingObservingStarsAtFullMoraleCompletesEverySegment(int actor, bool restore)
    {
        var game = Create(970517 + actor * 2 + (restore ? 1 : 0));
        game.State.ActivePlayer = actor;
        game.State.FirstPlayer = actor;
        var player = game.State.Players[actor];
        var opponent = game.State.Players[1 - actor];
        var liMuId = $"batch6jc-limu-observing-source-{actor}";
        var observingStarsId = $"batch6jc-limu-observing-tactic-{actor}";
        var liMu = Card("S02-0102", liMuId, actor);
        var observingStars = Card("S01-0119", observingStarsId, actor);
        var fillers = new[]
        {
            Card("S01-0101", $"batch6jc-observing-{actor}-1", actor),
            Card("S01-0201", $"batch6jc-observing-{actor}-2", actor),
            Card("S01-0301", $"batch6jc-observing-{actor}-3", actor),
            Card("S01-0401", $"batch6jc-observing-{actor}-4", actor),
            Card("S01-0002", $"batch6jc-observing-{actor}-5", actor),
            Card("S01-0003", $"batch6jc-observing-{actor}-6", actor),
        };
        player.Hand.Add(liMu);
        player.Library.Add(observingStars);
        player.Library.AddRange(fillers);
        opponent.Library.AddRange([
            Card("S01-0101", $"batch6jc-next-player-{actor}-1", 1 - actor),
            Card("S01-0201", $"batch6jc-next-player-{actor}-2", 1 - actor),
            Card("S01-0301", $"batch6jc-next-player-{actor}-3", 1 - actor),
        ]);
        Assert.True(player.MoraleDeck.Count >= liMu.Cost);
        foreach (var morale in player.MoraleDeck.ToArray())
        {
            player.MoraleDeck.Remove(morale);
            morale.Tapped = false;
            player.Morale.Add(morale);
        }
        var fullMoraleCount = player.Morale.Count;
        var fullMoraleIds = player.Morale.Select(card => card.InstanceId).Order().ToArray();

        var playLiMu = game.Handle(actor, new L12Command("playCard", liMuId, Row: 0, Slot: 0));
        Assert.True(playLiMu.Accepted, playLiMu.Error);
        player = game.State.Players[actor];
        Assert.Equal(liMu.Cost, player.Morale.Count(card => card.Tapped));
        var paidMoraleIds = player.Morale.Where(card => card.Tapped).Select(card => card.InstanceId)
            .Order().ToArray();
        Assert.Equal(fullMoraleIds, player.Morale.Select(card => card.InstanceId).Order());
        Assert.Equal(observingStarsId, Assert.Single(player.Library, card => card.InstanceId == observingStarsId).InstanceId);

        if (restore)
        {
            game = RestoreAndAssertStableSequences(game);
            player = game.State.Players[actor];
            Assert.Equal(paidMoraleIds,
                player.Morale.Where(card => card.Tapped).Select(card => card.InstanceId).Order());
            Assert.Equal(observingStarsId,
                Assert.Single(player.Library, card => card.InstanceId == observingStarsId).InstanceId);
        }

        ResolveOnlyPrompt(game, "mode:use");
        ResolveOnlyPrompt(game, "mode:none");
        PassResponses(game);
        var freePlay = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(actor, freePlay.PlayerIndex);
        Assert.Equal("s2-limu-tactic", freePlay.Data["action"]);
        Assert.Equal(observingStarsId, freePlay.Data["previewCardId"]);
        var liMuParentId = Assert.Single(game.State.EffectStack,
            item => item.SourceInstanceId == liMuId).StackItemId;

        if (restore)
        {
            var freePlayPromptId = freePlay.PromptId;
            game = RestoreAndAssertStableSequences(game);
            player = game.State.Players[actor];
            freePlay = Assert.Single(game.State.PendingPrompts);
            Assert.Equal(freePlayPromptId, freePlay.PromptId);
            Assert.Equal(observingStarsId, freePlay.Data["previewCardId"]);
            Assert.Contains(game.State.EffectStack, item => item.StackItemId == liMuParentId);
        }

        var commitFreePlay = game.Handle(actor,
            new L12Command("resolvePrompt", PromptId: freePlay.PromptId, Choice: "play"));
        Assert.True(commitFreePlay.Accepted, commitFreePlay.Error);
        player = game.State.Players[actor];
        var reorderStack = Assert.Single(game.State.EffectStack, item =>
            item.SourceInstanceId == observingStarsId
            && item.Data.GetValueOrDefault("atomicFlow") == "observing-stars-reorder");
        Assert.Equal("free", reorderStack.Data.GetValueOrDefault("effectGeneratedPlay"));
        Assert.Equal("library", reorderStack.Data.GetValueOrDefault("originZone"));
        Assert.Contains(player.Resolving, card => card.InstanceId == observingStarsId);

        PassResponses(game);
        var reorder = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(actor, reorder.PlayerIndex);
        Assert.Equal("all-top-bottom", reorder.Data["placementMode"]);
        var inspected = fillers.Take(5).Select(card => card.InstanceId).ToArray();
        Assert.Equal(inspected, reorder.ValidChoices);

        if (restore)
        {
            var reorderPromptId = reorder.PromptId;
            game = RestoreAndAssertStableSequences(game);
            player = game.State.Players[actor];
            reorder = Assert.Single(game.State.PendingPrompts);
            Assert.Equal(reorderPromptId, reorder.PromptId);
            Assert.Equal(inspected, reorder.ValidChoices);
            Assert.Empty(player.MoraleDeck);
        }

        var bottom = inspected.Reverse().ToList();
        var resolveReorder = game.Handle(actor, new L12Command("resolvePrompt", PromptId: reorder.PromptId,
            TopCardInstanceIds: [], BottomCardInstanceIds: bottom));
        Assert.True(resolveReorder.Accepted, resolveReorder.Error);
        PassResponses(game);
        player = game.State.Players[actor];

        Assert.Empty(player.MoraleDeck);
        Assert.Equal(fullMoraleCount, player.Morale.Count);
        Assert.Equal(paidMoraleIds,
            player.Morale.Where(card => card.Tapped).Select(card => card.InstanceId).Order());
        Assert.Equal(bottom, player.Library.TakeLast(bottom.Count).Select(card => card.InstanceId));
        Assert.Contains(player.Graveyard, card => card.InstanceId == observingStarsId);
        Assert.DoesNotContain(player.Resolving, card => card.InstanceId == observingStarsId);
        Assert.Contains(player.Field.SelectMany(row => row), card => card?.InstanceId == liMuId);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Null(game.State.ResponseWindow);
        Assert.Null(game.State.PendingDefense);
        Assert.Null(game.State.Winner);
        Assert.DoesNotContain(game.State.EffectStack, item => item.StackItemId == liMuParentId);
        Assert.DoesNotContain(game.State.DeferredEffectStack, item => item.StackItemId == liMuParentId);
        Assert.DoesNotContain(game.State.PendingActivations,
            activation => activation.CommittedParentStackItemId == liMuParentId);

        var turnSerial = game.State.TurnSerial;
        var endTurn = game.Handle(actor, new L12Command("endTurn"));
        Assert.True(endTurn.Accepted, endTurn.Error);
        Assert.Equal(1 - actor, game.State.ActivePlayer);
        Assert.True(game.State.TurnSerial > turnSerial);
        Assert.NotEqual(L12Phase.GameOver, game.State.Phase);
        Assert.Null(game.State.Winner);
        Assert.NotEmpty(game.State.Players[1 - actor].Library);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-okita-slot-pending-activation")]
    public void OkitaLegionUsesPendingActivationAndMayCoverOwnCounterTactic()
    {
        var game = Create(9701);
        var player = game.State.Players[0];
        var top = Card("S01-0410", "batch6jc-okita-legion");
        for (var row = 0; row < 2; row++)
        for (var fieldSlot = 0; fieldSlot < 3; fieldSlot++)
            if (row != 0 || fieldSlot != 0)
                player.Field[row][fieldSlot] = Card("S01-0101", $"batch6jc-fill-{row}-{fieldSlot}");
        var counter = Card("S01-0018", "batch6jc-covered-counter");
        counter.Hidden = true;
        player.Field[1][2] = counter;

        BeginOkitaTop(game, top);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-okita-top", mode.Data.GetValueOrDefault("action"));
        ResolveOnlyPrompt(game, "play");

        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", slot.Continuation);
        Assert.Contains("1:2", slot.ValidChoices);
        Assert.DoesNotContain("s2-okita-slot", slot.Data.Values);
        Assert.Single(game.State.PendingActivations,
            activation => activation.Ability == "effect-generated-free-play");
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-okita-invalid-slot-no-fallback")]
    public void OkitaDeclaredSlotFailureDoesNotOverwriteOrFallBackToHand()
    {
        var game = Create(9702);
        var player = game.State.Players[0];
        var top = Card("S01-0410", "batch6jc-invalid-slot-legion");
        BeginOkitaTop(game, top);
        ResolveOnlyPrompt(game, "play");
        var slot = Assert.Single(game.State.PendingPrompts);
        var declared = slot.ValidChoices.First();
        var parts = declared.Split(':');
        var blocker = Card("S01-0101", "batch6jc-late-blocker");
        player.Field[int.Parse(parts[0])][int.Parse(parts[1])] = blocker;

        ResolveOnlyPrompt(game, declared);

        Assert.Contains(top, player.Library);
        Assert.DoesNotContain(top, player.Hand);
        Assert.DoesNotContain(top, player.Field.SelectMany(row => row));
        Assert.Same(blocker, player.Field[int.Parse(parts[0])][int.Parse(parts[1])]);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.EffectResultStatus == "failed"
            && entry.Text.Contains("位置已失效", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("位置已失效", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-okita-source-invalid-failed")]
    public void OkitaCommittedCardLeavingItsOriginIsFailedNotCancelled()
    {
        var game = Create(97021);
        var player = game.State.Players[0];
        var top = Card("S01-0410", "batch6jc-source-invalid-legion");
        BeginOkitaTop(game, top);
        ResolveOnlyPrompt(game, "play");
        var slot = Assert.Single(game.State.PendingPrompts);
        var declared = slot.ValidChoices.First();
        player.Library.Remove(top);
        player.Hand.Add(top);

        ResolveOnlyPrompt(game, declared);

        Assert.Contains(top, player.Hand);
        Assert.DoesNotContain(top, player.Field.SelectMany(row => row));
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("打出来源已失效", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("打出来源已失效", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:effect-generated-play-invalid-origin")]
    public void GeneratedPlayRejectsAnInvalidOriginAsFailed()
    {
        var game = Create(97022);
        var card = Card("S01-0410", "batch6jc-invalid-origin-legion");
        var parent = new L12StackItem
        {
            StackItemId = "batch6jc-invalid-origin-parent",
            Controller = 0,
            SourceInstanceId = "batch6jc-invalid-origin-source",
            SourceCardId = "S02-0403",
            SourceName = "冲田总司",
            Trigger = "attack",
            Text = "效果生成打出",
        };
        game.State.EffectStack.Add(parent);

        InvokeVoid(game, "BeginEffectGeneratedFreePlay", 0, card, parent, "library", "〈冲田总司〉");

        Assert.Empty(game.State.EffectStack);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("打出来源已失效", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("打出来源已失效", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:effect-generated-play-no-entry-slot")]
    public void GeneratedLegionPlayWithoutALegalSlotIsFailedAndKeepsTheCardInItsOrigin()
    {
        var game = Create(97023);
        var player = game.State.Players[0];
        var card = Card("S01-0410", "batch6jc-no-slot-legion");
        player.Library.Add(card);
        for (var row = 0; row < 2; row++)
        for (var slot = 0; slot < 3; slot++)
            player.Field[row][slot] = Card("S01-0101", $"batch6jc-no-slot-{row}-{slot}");
        var parent = new L12StackItem
        {
            StackItemId = "batch6jc-no-slot-parent",
            Controller = 0,
            SourceInstanceId = "batch6jc-no-slot-source",
            SourceCardId = "S02-0403",
            SourceName = "冲田总司",
            Trigger = "attack",
            Text = "效果生成打出",
        };
        game.State.EffectStack.Add(parent);

        InvokeVoid(game, "BeginEffectGeneratedFreePlay", 0, card, parent, "library", "〈冲田总司〉");

        Assert.Contains(card, player.Library);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.EffectResultStatus == "failed"
            && entry.Text.Contains("没有合法登场位置", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("没有合法登场位置", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-okita-legion-authority-entry")]
    public void OkitaLegionFreePlayUsesTheNonHandEntryAuthorityEvent()
    {
        var game = Create(9703);
        var player = game.State.Players[0];
        var top = Card("S01-0410", "batch6jc-authority-legion");
        BeginOkitaTop(game, top);
        ResolveOnlyPrompt(game, "play");
        var slot = Assert.Single(game.State.PendingPrompts);
        var declared = slot.ValidChoices.First();

        ResolveOnlyPrompt(game, declared);

        Assert.DoesNotContain(top, player.Library);
        Assert.Contains(top, player.Field.SelectMany(row => row));
        Assert.Equal(game.State.Round, top.SummonRound);
        Assert.Contains(game.State.AuthorityEvents, entry => entry.Type == "non-hand-entry"
            && entry.TargetInstanceId == top.InstanceId && entry.OriginZone == "library");
        Assert.Contains(game.State.Events, entry => entry.Type == "play"
            && entry.Cards.Any(card => card.InstanceId == top.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-okita-counter-cover-commit")]
    public void OkitaLegionCommitDisplacesCoveredCounterBeforeAuthorityEntry()
    {
        var game = Create(97031);
        var player = game.State.Players[0];
        var top = Card("S01-0410", "batch6jc-cover-legion");
        var counter = Card("S01-0018", "batch6jc-cover-counter");
        counter.Hidden = true;
        player.Field[1][2] = counter;
        BeginOkitaTop(game, top);
        ResolveOnlyPrompt(game, "play");
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("1:2", slot.ValidChoices);

        ResolveOnlyPrompt(game, "1:2");

        Assert.Same(top, player.Field[1][2]);
        Assert.Contains(counter, player.Graveyard);
        Assert.False(counter.Hidden);
        Assert.Contains(game.State.Events, entry => entry.Type == "counter-displaced"
            && entry.Cards.Any(card => card.InstanceId == counter.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0523")]
    [Trait("L12Evidence", "entry:face-up-tactic-slot-occupancy")]
    public void EffectGeneratedLegionCannotDisplaceAFaceUpTrojanHorse()
    {
        var game = Create(97032);
        var player = game.State.Players[0];
        var top = Card("S01-0410", "batch6jc-face-up-occupied-legion");
        var horse = Card("S02-0523", "batch6jc-face-up-trojan");
        horse.OwnerIndex = 1;
        horse.Hidden = false;
        player.Field[1][2] = horse;
        BeginOkitaTop(game, top);
        ResolveOnlyPrompt(game, "play");

        var slot = Assert.Single(game.State.PendingPrompts);

        Assert.DoesNotContain("1:2", slot.ValidChoices);
        Assert.Same(horse, player.Field[1][2]);
        Assert.Contains(top, player.Library);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-limu-simple-common-free-play")]
    public void LiMuSimpleTacticUsesTheCommonEffectGeneratedPlayTransaction()
    {
        var game = Create(9704);
        var tactic = Card("S01-0008", "batch6jc-limu-simple");

        BeginLiMuRevealedTactic(game, tactic);

        Assert.DoesNotContain(tactic, game.State.Players[0].Library);
        Assert.Contains(tactic, game.State.Players[0].Resolving);
        var child = Assert.Single(game.State.EffectStack,
            item => item.SourceInstanceId == tactic.InstanceId);
        Assert.Equal("free", child.Data.GetValueOrDefault("effectGeneratedPlay"));
        Assert.Equal("library", child.Data.GetValueOrDefault("originZone"));
    }

    [Fact]
    [Trait("L12Evidence", "entry:auxiliary-limu-counter-bottom")]
    public void LiMuRevealingAbsoluteDefenseReturnsItToTheLibraryBottomWithoutOpeningAPlayFlow()
    {
        var game = Create(97041);
        var player = game.State.Players[0];
        var liMu = Card("S02-0102", "auxiliary-limu-counter-source");
        var absoluteDefense = Card("S01-0016", "auxiliary-limu-absolute-defense");
        player.Field[0][0] = liMu;
        player.Library.Clear();
        player.Library.Add(absoluteDefense);
        var item = new L12StackItem
        {
            StackItemId = "auxiliary-limu-counter-parent",
            Controller = 0,
            SourceInstanceId = liMu.InstanceId,
            SourceCardId = liMu.CardId,
            SourceName = liMu.Name,
            Trigger = "enter",
            Text = "李牧展示段",
        };
        game.State.EffectStack.Add(item);

        InvokeVoid(game, "RevealS2LiMuTop", item);

        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Same(absoluteDefense, Assert.Single(player.Library));
        Assert.Contains(game.State.Events, entry => entry.Type == "library"
            && entry.Cards.Any(card => card.InstanceId == absoluteDefense.InstanceId));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "stack-push"
            && entry.Cards.Any(card => card.InstanceId == absoluteDefense.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-limu-composite-common-free-play")]
    public void LiMuCompositeTacticKeepsPrivateZoneUntilItsPublicDeclarationCommits()
    {
        var game = Create(9705);
        var target = Card("S01-0101", "batch6jc-volley-target", owner: 1);
        game.State.Players[1].Field[0][0] = target;
        var tactic = Card("S01-0005", "batch6jc-limu-composite");

        BeginLiMuRevealedTactic(game, tactic);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains(tactic, game.State.Players[0].Resolving);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == tactic.InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-composite-free-play-marker")]
    public void CompositeFreeTacticCarriesTheSameAuthorityMarkerAfterDeclaration()
    {
        var game = Create(97051);
        game.State.Players[1].Field[0][0] = Card("S01-0101", "batch6jc-composite-target", owner: 1);
        var tactic = Card("S01-0005", "batch6jc-marked-composite");
        BeginLiMuRevealedTactic(game, tactic);

        ResolveOnlyPrompt(game, "mode:front");

        var child = Assert.Single(game.State.EffectStack,
            item => item.SourceInstanceId == tactic.InstanceId);
        Assert.Equal("free", child.Data.GetValueOrDefault("effectGeneratedPlay"));
        Assert.Equal("library", child.Data.GetValueOrDefault("originZone"));
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-limu-auto-completed-composite-context")]
    [Trait("L12Evidence", "ruling:R2-S01-0118")]
    public void LiMuFreeMarchWithoutAFrontLegionCommitsBeforeItsAutoCompletedDeclaration()
    {
        var game = Create(970511);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var tactic = Card("S01-0118", "batch6jc-limu-auto-march");
        enemy.Field[0][0] = Card("S01-0402", "batch6jc-limu-auto-march-target", owner: 1);

        var parent = BeginLiMuRevealedTactic(game, tactic, liMuRow: 1);

        Assert.DoesNotContain(game.State.PendingActivations, activation =>
            activation.SourceInstanceId == tactic.InstanceId);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        var child = Assert.Single(game.State.EffectStack,
            item => item.SourceInstanceId == tactic.InstanceId
                && item.Data.GetValueOrDefault("atomicFlow") == "march-buff-segment");
        Assert.Equal("free", child.Data.GetValueOrDefault("effectGeneratedPlay"));
        Assert.Equal("library", child.Data.GetValueOrDefault("originZone"));
        Assert.NotEmpty(game.State.PendingPrompts);
        Assert.All(game.State.PendingPrompts, prompt => Assert.Equal("response", prompt.Kind));
        Assert.DoesNotContain(game.State.EffectStack, item => item.StackItemId == parent.StackItemId);
    }

    [Theory]
    [InlineData("S02-0105")]
    [InlineData("S02-0522")]
    [InlineData("S02-0307")]
    [Trait("L12Evidence", "entry:batch6jc-limu-auto-completed-empty-target-matrix")]
    public void LiMuFreeCompositeWithNoFirstTargetDoesNotLeaveAnOrphanDeclaration(string cardId)
    {
        var game = Create(970512 + cardId[^1]);
        var tactic = Card(cardId, $"batch6jc-limu-empty-{cardId}");

        var parent = BeginLiMuRevealedTactic(game, tactic, liMuRow: 1);

        Assert.DoesNotContain(tactic, game.State.Players[0].Library);
        Assert.DoesNotContain(game.State.PendingActivations, activation =>
            activation.SourceInstanceId == tactic.InstanceId);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        Assert.DoesNotContain(game.State.EffectStack, item => item.StackItemId == parent.StackItemId);
        Assert.True(game.State.EffectStack.Count(item => item.SourceInstanceId == tactic.InstanceId) <= 1);
        Assert.All(game.State.PendingPrompts, prompt => Assert.Equal("response", prompt.Kind));
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-limu-auto-completed-checkpoint")]
    [Trait("L12Evidence", "ruling:R2-S01-0118")]
    public void LiMuFreeMarchAutoCompletionSurvivesResponseAndLateDeclarationCheckpoints()
    {
        var game = Create(970516);
        var player = game.State.Players[0];
        var tactic = Card("S01-0118", "batch6jc-limu-checkpoint-march");
        var target = Card("S01-0402", "batch6jc-limu-checkpoint-target", owner: 1);
        player.Morale.AddRange([
            Morale("batch6jc-limu-checkpoint-morale-1"),
            Morale("batch6jc-limu-checkpoint-morale-2"),
        ]);
        game.State.Players[1].Field[0][0] = target;
        BeginLiMuRevealedTactic(game, tactic, liMuRow: 1);

        var responseRestored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.Equal(game.State.PendingPrompts.Select(prompt => prompt.PromptId),
            responseRestored.State.PendingPrompts.Select(prompt => prompt.PromptId));

        PassResponses(game);
        PassResponses(responseRestored);
        var originalLatePrompt = Assert.Single(game.State.PendingPrompts);
        var restoredLatePrompt = Assert.Single(responseRestored.State.PendingPrompts);
        Assert.Equal(originalLatePrompt.ValidChoices, restoredLatePrompt.ValidChoices);
        Assert.Contains("mode:use", restoredLatePrompt.ValidChoices);

        var lateRestored = L12GameEngine.RestoreCheckpoint(Catalog, responseRestored.SerializeFullState(),
            responseRestored.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            responseRestored.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.Equal(restoredLatePrompt.PromptId, Assert.Single(lateRestored.State.PendingPrompts).PromptId);

        CompleteMarchKill(game, target.InstanceId);
        CompleteMarchKill(lateRestored, target.InstanceId);

        AssertMarchFinished(game, tactic.InstanceId, target.InstanceId);
        AssertMarchFinished(lateRestored, tactic.InstanceId, target.InstanceId);
    }

    private static void CompleteMarchKill(L12GameEngine game, string targetInstanceId)
    {
        ResolveOnlyPrompt(game, "mode:use");
        var payment = Assert.Single(game.State.PendingPrompts);
        ResolveOnlyPrompt(game, [.. payment.ValidChoices.Where(choice => choice != "skip").Take(2)]);
        ResolveOnlyPrompt(game, targetInstanceId);
        PassResponses(game);
    }

    private static void AssertMarchFinished(L12GameEngine game, string tacticInstanceId, string targetInstanceId)
    {
        Assert.Contains(game.State.Players[0].Graveyard, card => card.InstanceId == tacticInstanceId);
        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == targetInstanceId);
        Assert.DoesNotContain(game.State.PendingActivations, activation =>
            activation.SourceInstanceId == tacticInstanceId);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.ActivationId is { } activationId
            && game.State.PendingActivations.Any(activation => activation.ActivationId == activationId
                && activation.SourceInstanceId == tacticInstanceId));
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == tacticInstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-free-tactic-negated-no-rollback")]
    public void NegatingAFreeTacticDoesNotReturnItToTheLibrary()
    {
        var game = Create(97052);
        var tactic = Card("S01-0008", "batch6jc-negated-free-tactic");
        BeginLiMuRevealedTactic(game, tactic);
        var child = Assert.Single(game.State.EffectStack,
            item => item.SourceInstanceId == tactic.InstanceId);
        child.Negated = true;

        PassResponses(game);

        Assert.Contains(tactic, game.State.Players[0].Graveyard);
        Assert.DoesNotContain(tactic, game.State.Players[0].Library);
        Assert.DoesNotContain(tactic, game.State.Players[0].Resolving);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-okita-source-left")]
    public void OkitaLeavingAfterTheRevealedPlayChoiceDoesNotSwallowTheCommittedCard()
    {
        var game = Create(97053);
        var player = game.State.Players[0];
        var top = Card("S01-0410", "batch6jc-source-left-legion");
        var parent = BeginOkitaTop(game, top);
        ResolveOnlyPrompt(game, "play");
        var slot = Assert.Single(game.State.PendingPrompts);
        var declared = slot.ValidChoices.First();
        var okita = player.Field.SelectMany(row => row)
            .Single(card => card?.InstanceId == parent.SourceInstanceId)!;
        for (var row = 0; row < 2; row++)
        for (var fieldSlot = 0; fieldSlot < 3; fieldSlot++)
            if (ReferenceEquals(player.Field[row][fieldSlot], okita)) player.Field[row][fieldSlot] = null;
        player.Graveyard.Add(okita);

        ResolveOnlyPrompt(game, declared);

        Assert.Contains(top, player.Field.SelectMany(row => row));
        Assert.DoesNotContain(top, player.Library);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-okita-artifact-normal-entry")]
    public void OkitaArtifactUsesNormalReplacementAndQueuesItsEnterEffect()
    {
        var game = Create(97054);
        var player = game.State.Players[0];
        var oldRelic = Card("S01-0205", "batch6jc-old-relic");
        player.Relic = oldRelic;
        var artifact = Card("S02-0404", "batch6jc-okita-artifact");
        BeginOkitaTop(game, artifact);

        ResolveOnlyPrompt(game, "play");

        Assert.Same(artifact, player.Relic);
        Assert.Contains(oldRelic, player.Graveyard);
        Assert.DoesNotContain(artifact, player.Library);
        Assert.Equal(game.State.Round, artifact.SummonRound);
        Assert.Contains(game.State.Events, entry => entry.Type == "play"
            && entry.Cards.Any(card => card.InstanceId == artifact.InstanceId));
        Assert.True(game.State.DeferredEffectStack.Concat(game.State.EffectStack)
                .Any(item => item.SourceInstanceId == artifact.InstanceId && item.Trigger == "enter")
            || game.State.PendingTriggerStackCandidates.Any(candidate => candidate.SourceInstanceId == artifact.InstanceId
                && candidate.Trigger == "enter")
            || game.State.PendingPrompts.Any(prompt => prompt.Data.GetValueOrDefault("action") == "s2-magatama-search"),
            "八尺琼勾玉的登场检索必须进入堆叠或已合法开始其隐藏检索提示");
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-okita-ineligible-hand-authority")]
    public void OkitaIneligibleTopUsesTheEffectHandAddAuthorityEvent()
    {
        var game = Create(9706);
        var player = game.State.Players[0];
        var top = Card("S02-0609", "batch6jc-okita-ineligible");

        BeginOkitaTop(game, top);

        Assert.DoesNotContain(top, player.Library);
        Assert.Contains(top, player.Hand);
        Assert.Contains(game.State.AuthorityEvents, entry => entry.Type == "effect-hand-add"
            && entry.TargetInstanceId == top.InstanceId && entry.OriginZone == "library");
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6jc-private-hand-add-redaction")]
    public void HiddenEffectHandAddAuthorityEventDoesNotExposeTheCardToTheOpponent()
    {
        var game = Create(9707);
        var player = game.State.Players[0];
        var secret = Card("S01-0008", "batch6jc-secret-hand-add");
        player.Library.Add(secret);
        player.Library.Remove(secret);

        InvokeVoid(game, "AddCardToHandByEffect", player, secret, "library", "隐藏检索加入手牌");

        var opponent = JsonSerializer.Serialize(game.SnapshotFor(1));
        Assert.DoesNotContain(secret.InstanceId, opponent, StringComparison.Ordinal);
        Assert.DoesNotContain(secret.Name, opponent, StringComparison.Ordinal);
        Assert.Contains(secret, player.Hand);
        Assert.Contains(game.State.AuthorityEvents, entry => entry.Type == "effect-hand-add"
            && entry.TargetInstanceId == secret.InstanceId);
    }
}
