using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6CRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "atomic-review-batch6c", "ATOMIC6C", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
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
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            SummonRound = -1,
        };
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 80 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
    }

    private static L12StackItem PassUntilFlow(L12GameEngine game, string flow)
    {
        for (var safety = 0; safety < 80; safety++)
        {
            var found = game.State.EffectStack.SingleOrDefault(item =>
                item.Data.GetValueOrDefault("atomicFlow") == flow);
            if (found is not null) return found;
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", prompt.Kind);
            Resolve(game, "pass");
        }
        throw new Xunit.Sdk.XunitException($"未进入预期复合段 {flow}");
    }

    private static L12CardInstance ArrangePlay(L12GameEngine game, string cardId)
    {
        var player = game.State.Players[0];
        player.FreeTacticCount = 1;
        var source = Card(cardId, $"batch6c-source-{cardId}");
        player.Hand.Add(source);
        player.Library.AddRange([
            Card("S01-0101", $"batch6c-library-{cardId}-1"),
            Card("S01-0201", $"batch6c-library-{cardId}-2"),
            Card("S01-0301", $"batch6c-library-{cardId}-3"),
            Card("S01-0401", $"batch6c-library-{cardId}-4"),
            Card("S01-0002", $"batch6c-library-{cardId}-5"),
            Card("S01-0003", $"batch6c-library-{cardId}-6"),
        ]);
        if (cardId == "S02-0306")
        {
            player.MasterDamageTakenThisTurn = 2;
            player.Hp = player.MaxHp - 2;
        }
        if (cardId == "S01-0419")
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-04C1", InstanceId = $"batch6c-rested-{cardId}", Tapped = true,
            });
        return source;
    }

    [Theory]
    [InlineData("S01-0014", "-2")]
    [InlineData("S01-0119", "mode:morale")]
    [InlineData("S01-0419", "mode:morale")]
    [InlineData("S02-0306", "mode:mill")]
    [Trait("L12Evidence", "entry:hand-play-public-declaration")]
    public void KnownSecondSegmentChoicesAreDeclaredBeforeTheCardLeavesHand(string cardId, string expectedChoice)
    {
        var game = Create(7701);
        var source = ArrangePlay(game, cardId);
        var hiddenIds = game.State.Players[0].Library.Select(card => card.InstanceId).ToArray();

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains(expectedChoice, declaration.ValidChoices);
        Assert.Contains(source, game.State.Players[0].Hand);
        Assert.DoesNotContain(source, game.State.Players[0].Resolving);
        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(hiddenIds, hiddenId => declaration.ValidChoices.Contains(hiddenId)
            || declaration.Data.Values.Any(value => value.Contains(hiddenId, StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0014")]
    [Trait("L12Evidence", "entry:independent-semantic-segments")]
    public void SacrificeToHeavenKeepsThePredeclaredDisasterSegmentWhenDrawIsNegated()
    {
        var game = Create(7702);
        var source = ArrangePlay(game, "S01-0014");
        var initialDisaster = game.State.DisasterValue;

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);
        Resolve(game, "-2");
        var draw = Assert.Single(game.State.EffectStack);
        Assert.Equal("ritual-draw", draw.Data["atomicFlow"]);
        draw.Negated = true;

        var disaster = PassUntilFlow(game, "ritual-disaster");
        Assert.Equal(initialDisaster, game.State.DisasterValue);
        Assert.Equal("-2", disaster.Data["declared:disasterValue"]);
        PassResponses(game);

        Assert.Equal(Math.Max(0, initialDisaster - 2), game.State.DisasterValue);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0015")]
    [Trait("L12Evidence", "entry:resolution-time-affected-player-choice")]
    public void PeaceTalkAsksTheOpponentOnlyWhenItsSecondSegmentResolves()
    {
        var game = Create(7703);
        var source = ArrangePlay(game, "S01-0015");

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "opponent-confirm");
        var draw = Assert.Single(game.State.EffectStack);
        Assert.Equal("peace-draw", draw.Data["atomicFlow"]);
        draw.Negated = true;

        var negotiation = PassUntilFlow(game, "peace-negotiation");
        Assert.Equal("1", negotiation.Data["compositeSegment"]);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "opponent-confirm");
        PassResponses(game);

        var decision = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("opponent-confirm", decision.Kind);
        Assert.Equal(1, decision.PlayerIndex);
        Assert.Contains("agree", decision.ValidChoices);
        Assert.Contains("refuse", decision.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0119")]
    [Trait("L12Evidence", "entry:hidden-first-segment-independent-public-option")]
    public void ObservingStarsDoesNotLeakTopCardsAndAddsActiveMoraleAfterFirstSegmentNegation()
    {
        var game = Create(7704);
        var player = game.State.Players[0];
        var source = ArrangePlay(game, "S01-0119");
        var hiddenIds = player.Library.Take(5).Select(card => card.InstanceId).ToArray();
        var moraleBefore = player.Morale.Count;

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.DoesNotContain(hiddenIds, id => mode.ValidChoices.Contains(id)
            || mode.Data.Values.Any(value => value.Contains(id, StringComparison.Ordinal)));
        Resolve(game, "mode:morale");
        var reorder = Assert.Single(game.State.EffectStack);
        Assert.Equal("observing-stars-reorder", reorder.Data["atomicFlow"]);
        reorder.Negated = true;

        PassUntilFlow(game, "observing-stars-morale");
        PassResponses(game);

        Assert.Equal(moraleBefore + 1, player.Morale.Count);
        Assert.False(player.Morale[^1].Tapped);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0419")]
    [Trait("L12Evidence", "entry:predeclared-exact-morale-target")]
    public void OiranGiftReadiesTheExactDeclaredMoraleInsteadOfTheFirstRestedMorale()
    {
        var game = Create(7705);
        var player = game.State.Players[0];
        var source = ArrangePlay(game, "S01-0419");
        var first = new L12MoraleCard { CardId = "S01-04C1", InstanceId = "batch6c-oiran-first", Tapped = true };
        var second = new L12MoraleCard { CardId = "S01-04C1", InstanceId = "batch6c-oiran-second", Tapped = true };
        player.Morale.AddRange([first, second]);

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);
        Resolve(game, "mode:morale");
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(first.InstanceId, target.ValidChoices);
        Assert.Contains(second.InstanceId, target.ValidChoices);
        Resolve(game, second.InstanceId);
        var search = Assert.Single(game.State.EffectStack);
        Assert.Equal("oiran-search", search.Data["atomicFlow"]);
        search.Negated = true;

        PassUntilFlow(game, "oiran-ready-morale");
        PassResponses(game);

        Assert.True(first.Tapped);
        Assert.False(second.Tapped);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0306")]
    [Trait("L12Evidence", "entry:once-reservation-and-independent-mill")]
    public void MimirConsumesItsOnceAndStillMillsWhenHealAndDrawAreNegated()
    {
        var game = Create(7706);
        var player = game.State.Players[0];
        var source = ArrangePlay(game, "S02-0306");
        var originalTop = player.Library.Take(2).ToArray();
        var hpBefore = player.Hp;

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);
        Resolve(game, "mode:mill");
        var recovery = Assert.Single(game.State.EffectStack);
        Assert.Equal("mimir-recover-draw", recovery.Data["atomicFlow"]);
        Assert.Contains("s2-mimir-used", player.UsedAbilities);
        recovery.Negated = true;

        PassUntilFlow(game, "mimir-mill");
        Assert.Equal(hpBefore, player.Hp);
        PassResponses(game);

        Assert.All(originalTop, card => Assert.Contains(card, player.Graveyard));
        Assert.Contains("s2-mimir-used", player.UsedAbilities);
    }

    [Fact]
    [Trait("L12Evidence", "entry:free-tactic-common-composite-plan")]
    [Trait("L12Evidence", "card:S01-0015")]
    public void LiMuFreePlayOfPeaceTalkSkipsAnEmptyDeclarationAndKeepsBothSegments()
    {
        var game = Create(7707);
        var player = game.State.Players[0];
        var liMu = Card("S02-0102", "batch6c-limu");
        var peace = Card("S01-0015", "batch6c-limu-peace");
        player.Hand.Add(liMu);
        player.Library.AddRange([
            peace,
            Card("S01-0101", "batch6c-limu-filler-1"),
            Card("S01-0201", "batch6c-limu-filler-2"),
        ]);
        for (var index = 0; index < liMu.Cost; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-01C1",
                InstanceId = $"batch6c-limu-morale-{index}",
                Tapped = false,
            });

        Assert.True(game.Handle(0, new L12Command("playCard", liMu.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        var reveal = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-limu-reveal", reveal.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: reveal.PromptId,
            Choice: "yes")).Accepted);
        var freePlay = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-limu-tactic", freePlay.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: freePlay.PromptId,
            Choice: "play")).Accepted);

        Assert.Empty(game.State.PendingActivations);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        var deferredDraw = Assert.Single(game.State.DeferredEffectStack, item =>
            item.SourceInstanceId == peace.InstanceId && item.Data.GetValueOrDefault("atomicFlow") == "peace-draw");
        Assert.Equal("0", deferredDraw.Data["compositeSegment"]);

        var liMuDraw = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-limu-draw");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: liMuDraw.PromptId,
            Choice: "no")).Accepted);
        var draw = Assert.Single(game.State.EffectStack, item =>
            item.SourceInstanceId == peace.InstanceId && item.Data.GetValueOrDefault("atomicFlow") == "peace-draw");

        while (game.State.PendingPrompts.FirstOrDefault(prompt => prompt.Kind == "response") is { } response)
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);

        var negotiation = Assert.Single(game.State.EffectStack, item =>
            item.SourceInstanceId == peace.InstanceId
            && item.Data.GetValueOrDefault("atomicFlow") == "peace-negotiation");
        Assert.Equal("1", negotiation.Data["compositeSegment"]);
    }
}
