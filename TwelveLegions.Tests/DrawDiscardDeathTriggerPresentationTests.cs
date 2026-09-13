using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class DrawDiscardDeathTriggerPresentationTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> Cases()
    {
        yield return ["S01-0001", 2];
        yield return ["S01-0303", 1];
        yield return ["S01-0306", 2];
        yield return ["S02-0301", 1];
    }

    [Theory]
    [InlineData("S02-0502", "enter", "draw-discard-draw-2")]
    [InlineData("S01-03M2", "active", "draw-discard-draw-1")]
    public void SameTypeEnterAndActiveEffectsUseTheSharedDrawDiscardPresentation(
        string cardId, string trigger, string firstFlow)
    {
        var ability = cardId == "S01-03M2"
            ? Assert.Single(Catalog.AtomicEffects.Find(cardId)!.Abilities,
                candidate => candidate.Sequence == 1)
            : Assert.Single(Catalog.AtomicEffects.Find(cardId)!.Abilities,
                candidate => candidate.Trigger == trigger);
        var scenes = ability.Presentations.Where(scene => scene.Flow is "draw-discard-draw-1"
                or "draw-discard-draw-2" or "draw-discard-discard")
            .OrderBy(scene => scene.SegmentIndex).ToArray();

        Assert.Equal(2, scenes.Length);
        Assert.Equal(firstFlow, scenes[0].Flow);
        Assert.Equal("draw-discard-discard", scenes[1].Flow);
        Assert.Equal([1, 2], scenes.Select(scene => scene.SegmentIndex).ToArray());
        Assert.All(scenes, scene => Assert.Equal(2, scene.SegmentCount));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CatalogBindsBothRuntimeSegmentsToThePrintedDeathAbility(string cardId, int _)
    {
        var death = Assert.Single(Catalog.AtomicEffects.Find(cardId)!.Abilities,
            ability => ability.Trigger == "death");
        var scenes = death.Presentations.Where(scene => scene.Flow is "draw-discard-draw-1"
                or "draw-discard-draw-2" or "draw-discard-discard")
            .OrderBy(scene => scene.SegmentIndex).ToArray();

        Assert.Equal(2, scenes.Length);
        Assert.Equal([1, 2], scenes.Select(scene => scene.SegmentIndex).ToArray());
        Assert.All(scenes, scene => Assert.Equal(2, scene.SegmentCount));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void DrawThenDiscardDeathEffectsExposeTwoSegmentsInOneResponseScope(string cardId, int drawCount)
    {
        var game = Create(31400 + drawCount);
        var player = game.State.Players[0];
        var existing = Card("S01-0002", $"existing-{cardId}");
        player.Hand.Add(existing);
        player.Library.AddRange(Enumerable.Range(0, drawCount + 1)
            .Select(index => Card("S01-0003", $"draw-{cardId}-{index}")));

        QueueDeath(game, cardId);
        ResolveChoice(game, "mode:use");

        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal($"trigger:{cardId}:death", first.Data.GetValueOrDefault("compositePlan"));
        Assert.Equal($"draw-discard-draw-{drawCount}", first.Data.GetValueOrDefault("atomicFlow"));
        Assert.Equal("0", first.Data.GetValueOrDefault("compositeSegment"));
        Assert.Equal("single-effect", first.Data.GetValueOrDefault("compositeResponseScope"));
        var declaration = Assert.Single(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.Cards.Any(card => card.CardId == cardId));
        Assert.Equal(1, declaration.EffectSegmentIndex);
        Assert.Equal(2, declaration.EffectSegmentCount);

        PassResponses(game);

        var discard = OnlyPrompt(game);
        Assert.Equal("pending-activation", discard.Continuation);
        Assert.Equal("hand-card", discard.Kind);
        Assert.DoesNotContain("skip", discard.ValidChoices);
        Assert.Equal(drawCount + 1, discard.ValidChoices.Count);
        var selected = discard.ValidChoices.Last();
        ResolveCards(game, selected);

        Assert.DoesNotContain(player.Hand, card => card.InstanceId == selected);
        Assert.Contains(player.Graveyard, card => card.InstanceId == selected);
        var results = game.State.Events.Where(entry => entry.Type == "effect-result"
                && entry.Cards.Any(card => card.CardId == cardId))
            .OrderBy(entry => entry.EffectSegmentIndex).ToArray();
        Assert.Equal(2, results.Length);
        Assert.Equal([1, 2], results.Select(entry => entry.EffectSegmentIndex).ToArray());
        Assert.All(results, entry => Assert.Equal(2, entry.EffectSegmentCount));
        Assert.All(results, entry => Assert.Equal("resolved", entry.EffectResultStatus));
        Assert.NotEqual(results[0].EffectSceneId, results[1].EffectSceneId);

        Assert.False(game.Handle(discard.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: discard.PromptId,
                CardInstanceIds: [existing.InstanceId])).Accepted);
    }

    [Fact]
    public void HeraclesEnterUsesTheSamePostDrawPrivateDiscardProtocol()
    {
        var game = Create(31422);
        var player = game.State.Players[0];
        var source = Card("S02-0502", "heracles-source");
        var existing = Card("S01-0002", "heracles-existing");
        var drawnA = Card("S01-0003", "heracles-drawn-a");
        var drawnB = Card("S01-0004", "heracles-drawn-b");
        player.Field[0][0] = source;
        player.Hand.Add(existing);
        player.Library.AddRange([drawnA, drawnB]);

        Invoke(game, "QueueOrPushTriggeredEffect", 0, source, "enter", "赫拉克勒斯登场效果", null,
            new Dictionary<string, string>());
        ResolveChoice(game, "mode:use");
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("trigger:S02-0502:enter", first.Data["compositePlan"]);
        Assert.Equal("draw-discard-draw-2", first.Data["atomicFlow"]);
        Assert.Equal("single-effect", first.Data["compositeResponseScope"]);

        PassResponses(game);
        var discard = OnlyPrompt(game);
        Assert.Equal("pending-activation", discard.Continuation);
        Assert.Equal("post-draw-private", discard.Data["declarationTiming"]);
        Assert.DoesNotContain("skip", discard.ValidChoices);
        ResolveCards(game, drawnB.InstanceId);
        PassResponses(game);

        Assert.Contains(drawnB, player.Graveyard);
        Assert.Contains(existing, player.Hand);
        Assert.Contains(drawnA, player.Hand);
        var results = game.State.Events.Where(entry => entry.Type == "effect-result"
                && entry.Cards.Any(card => card.CardId == "S02-0502"))
            .OrderBy(entry => entry.EffectSegmentIndex).ToArray();
        Assert.Equal(2, results.Length);
        Assert.All(results, result => Assert.Equal("resolved", result.EffectResultStatus));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NegatingDrawSegmentStopsMandatoryDiscardSegment(string cardId, int drawCount)
    {
        var game = Create(31500 + drawCount);
        var player = game.State.Players[0];
        player.Hand.Add(Card("S01-0002", $"existing-negated-{cardId}"));
        player.Library.AddRange(Enumerable.Range(0, drawCount)
            .Select(index => Card("S01-0003", $"negated-draw-{cardId}-{index}")));
        var handBefore = player.Hand.Select(card => card.InstanceId).ToArray();

        QueueDeath(game, cardId);
        ResolveChoice(game, "mode:use");
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.Equal(handBefore, player.Hand.Select(card => card.InstanceId).ToArray());
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingPrompts);
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == cardId));
        Assert.Equal(1, result.EffectSegmentIndex);
        Assert.Equal(2, result.EffectSegmentCount);
        Assert.Equal("negated", result.EffectResultStatus);
    }

    [Fact]
    public void PostDrawPrivateDiscardRestoresOnceAndDoesNotLeakTheHand()
    {
        var game = Create(31601);
        var player = game.State.Players[0];
        player.Hand.Add(Card("S01-0002", "restore-existing"));
        player.Library.Add(Card("S01-0003", "restore-drawn"));

        QueueDeath(game, "S01-0303");
        ResolveChoice(game, "mode:use");
        PassResponses(game);
        var oldPrompt = OnlyPrompt(game);
        Assert.DoesNotContain("restore-existing", JsonSerializer.Serialize(game.SnapshotFor(1)),
            StringComparison.Ordinal);
        Assert.DoesNotContain("restore-drawn", JsonSerializer.Serialize(game.SnapshotFor(1)),
            StringComparison.Ordinal);

        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 2, 3, 4, 5, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var restoredPrompt = OnlyPrompt(game);
        Assert.Equal(oldPrompt.PromptId, restoredPrompt.PromptId);
        ResolveCards(game, "restore-drawn");

        Assert.False(game.Handle(oldPrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: oldPrompt.PromptId,
                CardInstanceIds: ["restore-existing"])).Accepted);
        Assert.Contains(game.State.Players[0].Graveyard, card => card.InstanceId == "restore-drawn");
        Assert.Equal(2, game.State.Events.Count(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == "S01-0303")));
    }

    [Fact]
    public void FailedAtomicDrawDoesNotOpenTheMandatoryDiscardPrompt()
    {
        var game = Create(31602);
        game.State.Players[0].Hand.Add(Card("S01-0002", "short-library-existing"));
        game.State.Players[0].Library.Add(Card("S01-0003", "only-one-card"));

        QueueDeath(game, "S01-0306");
        ResolveChoice(game, "mode:use");
        PassResponses(game);

        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Contains(game.State.Players[0].Hand, card => card.InstanceId == "short-library-existing");
        Assert.DoesNotContain(game.State.Players[0].Hand, card => card.InstanceId == "only-one-card");
        var results = game.State.Events.Where(entry => entry.Type == "effect-result"
                && entry.Cards.Any(card => card.CardId == "S01-0306"))
            .OrderBy(entry => entry.EffectSegmentIndex).ToArray();
        Assert.Equal(["failed", "skipped"], results.Select(entry => entry.EffectResultStatus!).ToArray());
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "draw-discard-trigger", "DRAW-DISCARD", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Library.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
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
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            SummonRound = -1,
            OwnerIndex = 0,
        };
    }

    private static void QueueDeath(L12GameEngine game, string cardId)
    {
        var source = Card(cardId, $"source-{cardId}");
        game.State.Players[0].Resolving.Add(source);
        Invoke(game, "QueueOrPushTriggeredEffect", 0, source, "death", "抽牌并弃牌阵亡效果", null,
            new Dictionary<string, string> { ["cause"] = "effect" });
    }

    private static object? Invoke(object target, string method, params object?[] args)
    {
        var candidate = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(info => info.Name == method && info.GetParameters().Length == args.Length);
        return candidate.Invoke(target, args);
    }

    private static L12Prompt OnlyPrompt(L12GameEngine game) => Assert.Single(game.State.PendingPrompts);

    private static void ResolveChoice(L12GameEngine game, string choice)
    {
        var prompt = OnlyPrompt(game);
        Assert.True(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
    }

    private static void ResolveCards(L12GameEngine game, params string[] choices)
    {
        var prompt = OnlyPrompt(game);
        Assert.True(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: choices.ToList())).Accepted);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            ResolveChoice(game, "pass");
    }
}
