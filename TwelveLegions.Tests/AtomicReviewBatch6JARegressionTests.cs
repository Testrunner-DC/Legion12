using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6JARegressionTests
{
    private static readonly L12Catalog TestCatalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static L12Catalog Catalog => TestCatalog;

    private static readonly (string CardId, string Trigger)[] ReviewedEntries =
    [
        ("S01-0101", "enter"), ("S01-0102", "enter"), ("S01-0103", "enter"),
        ("S01-0108", "enter"), ("S01-0110", "enter"), ("S01-0111", "enter"),
        ("S01-0112", "enter"), ("S01-0201", "enter"), ("S01-0202", "enter"),
        ("S01-0205", "enter"), ("S01-0210", "enter"), ("S01-0215", "enter"),
        ("S01-0217", "enter"), ("S01-0220", "enter"), ("S01-0313", "enter"),
        ("S01-0316", "enter"), ("S01-0317", "enter"), ("S01-0402", "enter"),
        ("S01-0403", "enter"), ("S01-0406", "enter"), ("S01-0408", "enter"),
        ("S01-0411", "enter"), ("S01-0412", "enter"), ("S01-0416", "enter"),
        ("S01-0417", "enter"), ("S02-0003", "enter"), ("S02-0008", "enter"),
        ("S02-0204", "enter"), ("S02-0303", "enter"), ("S02-0401", "enter"),
        ("S02-0402", "enter"), ("S02-0404", "enter"), ("S02-0501", "enter"),
        ("S02-0501", "promotion-enter"), ("S02-0502", "enter"),
        ("S02-0505", "promotion-enter"), ("S02-0506", "enter"),
        ("S02-0513", "enter"), ("S02-0518", "enter"), ("S02-0520", "enter"),
        ("S02-0601", "enter"), ("S02-0608", "enter"), ("S02-0613", "enter"),
        ("S02-0617", "enter"), ("S02-0619", "enter"),
    ];

    public static IEnumerable<object[]> ReviewedRows()
        => ReviewedEntries.Select(entry => new object[] { entry.CardId, entry.Trigger });

    private static L12GameEngine Create(int seed)
        => new(Catalog, "atomic-review-batch6ja", "ATOMIC6JA", seed, ["甲", "乙"], [0, 1],
            skipPreparation: true);

    private static L12CardInstance Card(string cardId, string instanceId, int? troops = null)
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
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
        };
    }

    private static void AddMorale(L12PlayerState player, string id, bool tapped = false)
        => player.Morale.Add(new L12MoraleCard { InstanceId = id, CardId = "S01-01C1", Tapped = tapped });

    private static (L12GameEngine Game, L12CardInstance Source) Arrange(string cardId, string trigger, int seed)
    {
        var game = Create(seed);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        player.Hand.Clear();
        player.Library.Clear();
        player.Graveyard.Clear();
        enemy.Hand.Clear();
        enemy.Library.Clear();
        enemy.Graveyard.Clear();
        for (var index = 0; index < 6; index++) AddMorale(player, $"batch6ja-morale-{index}", tapped: index == 5);
        player.SpecialZones.Runes = 4;
        player.Hp = Math.Max(player.Hp, 6);

        player.Hand.Add(Card("S01-0002", "batch6ja-discard"));
        player.Hand.Add(Card("S01-0404", "batch6ja-sanada"));
        player.Hand.Add(Card("S02-0609", "batch6ja-hand-squire"));
        player.Library.Add(Card("S01-0105", "batch6ja-hidden-top"));
        player.Library.Add(Card("S02-0001", "batch6ja-universal-search"));
        player.Library.Add(Card("S02-0609", "batch6ja-library-squire"));
        player.Library.Add(Card("S01-0404", "batch6ja-library-sanada"));
        player.Library.Add(Card("S02-0403", "batch6ja-library-cavalry", 4000));
        player.Graveyard.Add(Card("S01-0212", "batch6ja-grave-guard"));
        player.Graveyard.Add(Card("S02-0505", "batch6ja-promotion"));
        player.Graveyard.Add(Card("S02-0609", "batch6ja-grave-squire"));
        player.Graveyard.Add(Card("S01-0202", "batch6ja-imhotep-target"));
        player.Graveyard.Add(Card("S02-01S1", "batch6ja-canute-death"));
        player.Graveyard.Add(Card("S01-0301", "batch6ja-canute-asgard-death"));
        for (var index = 0; index < 4; index++)
            enemy.Hand.Add(Card("S01-0002", $"batch6ja-enemy-hand-{index}"));

        var ownSun = Card("S01-0201", "batch6ja-own-sun", 4000);
        var ownTian = Card("S01-0104", "batch6ja-own-tianting", 4000);
        var guard = Card("S01-0212", "batch6ja-field-guard", 1000);
        var restedGao = Card("S01-0404", "batch6ja-rested-gao", 3000);
        guard.Tapped = true;
        restedGao.Tapped = true;
        player.Field[0][0] = ownSun;
        player.Field[0][1] = ownTian;
        player.Field[0][2] = Card("S01-0404", "batch6ja-front-gao", 3000);
        player.Field[1][0] = guard;
        player.Field[1][1] = restedGao;

        var enemyLow = Card("S01-0203", "batch6ja-enemy-low", 1000);
        var enemyOther = Card("S01-0212", "batch6ja-enemy-other", 2000);
        enemyOther.CostModifier = -1;
        enemyLow.Tapped = true;
        enemy.Field[0][0] = enemyLow;
        enemy.Field[0][1] = enemyOther;
        enemy.Field[0][2] = Card("S01-0212", "batch6ja-enemy-cheap", 1000);
        var covered = Card("S01-0019", "batch6ja-covered-counter");
        covered.Hidden = true;
        enemy.Field[1][0] = covered;

        var source = Card(cardId, $"batch6ja-source-{cardId}-{trigger}");
        Invoke(game, "QueueOrPushTriggeredEffect", 0, source, trigger, "6J-A登场公开声明测试", null,
            new Dictionary<string, string>());
        return (game, source);
    }

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    private static L12Prompt OnlyPrompt(L12GameEngine game) => Assert.Single(game.State.PendingPrompts);

    [Theory]
    [MemberData(nameof(ReviewedRows))]
    [Trait("L12Evidence", "entry:batch6ja-enter-selection-boundary")]
    public void EveryReviewedEntryUsesItsConfiguredSelectionBoundary(string cardId, string trigger)
    {
        var index = Array.FindIndex(ReviewedEntries, entry => entry == (cardId, trigger));
        var fixture = Arrange(cardId, trigger, 9900 + index);

        var prompt = OnlyPrompt(fixture.Game);
        if (cardId is "S02-0513" or "S02-0518" or "S02-0520")
        {
            Assert.Equal("stack-response", prompt.Continuation);
            Assert.Single(fixture.Game.State.EffectStack);
        }
        else
        {
            Assert.Equal("pending-activation", prompt.Continuation);
            Assert.Empty(fixture.Game.State.EffectStack);
        }
    }

    [Theory]
    [InlineData("S01-0103")]
    [InlineData("S02-0008")]
    [InlineData("S02-0401")]
    [InlineData("S02-0404")]
    [InlineData("S02-0617")]
    [Trait("L12Evidence", "entry:batch6ja-hidden-library-not-predeclared")]
    public void HiddenLibraryIdentityDoesNotAppearInEntryDeclaration(string cardId)
    {
        var fixture = Arrange(cardId, "enter", 9960 + cardId[^1]);
        var prompt = OnlyPrompt(fixture.Game);
        var opponent = JsonSerializer.Serialize(fixture.Game.SnapshotFor(1));

        Assert.DoesNotContain("batch6ja-hidden-top", prompt.ValidChoices);
        Assert.DoesNotContain("batch6ja-library-squire", prompt.ValidChoices);
        Assert.DoesNotContain("batch6ja-library-cavalry", prompt.ValidChoices);
        Assert.DoesNotContain("batch6ja-hidden-top", opponent, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6m-ring-hidden-match-existence")]
    public void RingEntrySearchDeclarationDoesNotPeekForAHiddenUniversalMatch()
    {
        var game = Create(10005);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Library.Clear();
        player.Hand.Add(Card("S01-0002", "batch6m-ring-discard"));
        player.Library.Add(Card("S02-0401", "batch6m-ring-non-universal"));
        var ring = Card("S02-0008", "batch6m-ring-source");

        Invoke(game, "QueueOrPushTriggeredEffect", 0, ring, "enter", "万物统御之戒登场效果", null,
            new Dictionary<string, string>());

        var mode = OnlyPrompt(game);
        Assert.Equal("pending-activation", mode.Continuation);
        Assert.Contains("mode:none", mode.ValidChoices);
        Assert.Contains("mode:use", mode.ValidChoices);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6ja-colon-cost-prepaid")]
    public void PublicAndPrivateColonCostsArePaidBeforeEntryStack()
    {
        var fixture = Arrange("S02-0619", "enter", 9970);
        var game = fixture.Game;
        Resolve(game, "mode:use");
        Resolve(game, "rune-count:1");
        Resolve(game, "batch6ja-enemy-low");

        Assert.Equal(3, game.State.Players[0].SpecialZones.Runes);
        Assert.Single(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0101")]
    [Trait("L12Evidence", "entry:enter-later-target-cancel-no-payment-no-stack")]
    public void LuBuCancellingTheLaterTargetChoiceReturnsNoMoraleAndCreatesNoStackItem()
    {
        var fixture = Arrange("S01-0101", "enter", 99701);
        var player = fixture.Game.State.Players[0];
        var originalMorale = player.Morale.Select(card => card.InstanceId).ToArray();

        var mode = OnlyPrompt(fixture.Game);
        Assert.Equal(["mode:none", "mode:use"], mode.ValidChoices);
        Assert.DoesNotContain("skip", mode.ValidChoices);
        Resolve(fixture.Game, "mode:use");
        ResolveMany(fixture.Game, originalMorale.Take(2).ToArray());
        var target = OnlyPrompt(fixture.Game);
        Assert.Contains("skip", target.ValidChoices);
        Resolve(fixture.Game, "skip");

        Assert.Equal(originalMorale.Order(), player.Morale.Select(card => card.InstanceId).Order());
        Assert.Empty(fixture.Game.State.EffectStack);
        Assert.Empty(fixture.Game.State.PendingActivations);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6ja-zhuge-independent-followup")]
    public void ZhugeRevealAndDisasterAdjustmentAreIndependentStackItems()
    {
        var fixture = Arrange("S01-0111", "enter", 9971);
        Resolve(fixture.Game, "mode:use");
        Resolve(fixture.Game, "1");
        var first = Assert.Single(fixture.Game.State.EffectStack);
        Assert.Equal("zhuge-reveal", first.Data.GetValueOrDefault("atomicFlow"));
        first.Negated = true;
        PassResponses(fixture.Game);
        var second = Assert.Single(fixture.Game.State.EffectStack);
        Assert.Equal("zhuge-disaster", second.Data.GetValueOrDefault("atomicFlow"));
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6ja-canopic-independent-discard")]
    public void CanopicFourDiscardStillStacksAfterTargetSegmentIsNegated()
    {
        var fixture = Arrange("S01-0220", "enter", 9972);
        Resolve(fixture.Game, "batch6ja-own-sun");
        var first = Assert.Single(fixture.Game.State.EffectStack);
        first.Negated = true;
        PassResponses(fixture.Game);
        Assert.Contains(fixture.Game.State.EffectStack,
            item => item.Data.GetValueOrDefault("atomicFlow") == "canopic-four-discard");
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6ja-canopic-no-target-direct-discard")]
    public void CanopicWithoutALegalFirstTargetStartsWithItsIndependentDiscardSegment()
    {
        var game = Create(9977);
        var player = game.State.Players[0];
        for (var row = 0; row < 2; row++)
            for (var slot = 0; slot < 3; slot++) player.Field[row][slot] = null;
        var source = Card("S01-0220", "batch6ja-no-target-canopic");
        player.ExtraRelics.Add(source);
        player.Hand.Add(Card("S01-0002", "batch6ja-canopic-own-response"));
        game.State.Players[1].Hand.Add(Card("S01-0002", "batch6ja-canopic-enemy-response"));
        var canopicCounter = Card("S01-0019", "batch6ja-canopic-covered-counter");
        canopicCounter.Hidden = true;
        game.State.Players[1].Field[1][0] = canopicCounter;

        Invoke(game, "QueueOrPushTriggeredEffect", 0, source, "enter", "卡诺匹斯罐四无合法首段目标", null,
            new Dictionary<string, string>());
        Invoke(game, "AdvanceTriggerBatches");

        Assert.Empty(game.State.PendingPrompts);
        Assert.Contains(player.Graveyard, card => card.InstanceId == source.InstanceId);
        Assert.Contains(game.State.Events, entry => entry.Type == "stack-push"
            && entry.Text.Contains("随后弃置此圣物", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6ja-negated-cost-not-refunded")]
    public void NegatedClaudiaEntryDoesNotRefundThePrepaidRune()
    {
        var fixture = Arrange("S02-0619", "enter", 9973);
        Resolve(fixture.Game, "mode:use");
        Resolve(fixture.Game, "rune-count:1");
        Resolve(fixture.Game, "batch6ja-enemy-low");
        var item = Assert.Single(fixture.Game.State.EffectStack);
        item.Negated = true;

        PassResponses(fixture.Game);

        Assert.Equal(3, fixture.Game.State.Players[0].SpecialZones.Runes);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6ja-invalid-target-no-refund")]
    public void ClaudiaInvalidTargetCancelsOnlyItsResolutionAndKeepsTheRunePaid()
    {
        var fixture = Arrange("S02-0619", "enter", 9974);
        Resolve(fixture.Game, "mode:use");
        Resolve(fixture.Game, "rune-count:1");
        Resolve(fixture.Game, "batch6ja-enemy-low");
        fixture.Game.State.Players[1].Field[0][0] = null;

        PassResponses(fixture.Game);

        Assert.Equal(3, fixture.Game.State.Players[0].SpecialZones.Runes);
        Assert.DoesNotContain(fixture.Game.State.EffectStack,
            item => item.SourceCardId == "S02-0619");
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6ja-takeda-no-empty-search-stack")]
    public void TakedaDecliningSearchStartsWithTheIndependentFollowupDeclaration()
    {
        var fixture = Arrange("S02-0401", "enter", 9975);

        Resolve(fixture.Game, "mode:none");

        var followup = OnlyPrompt(fixture.Game);
        Assert.Equal("pending-activation", followup.Continuation);
        Assert.Contains("随后", followup.Text);
        Assert.Empty(fixture.Game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6ja-no-legal-target-no-empty-stack")]
    public void ForcedEntryWithoutALegalTargetCreatesNoEmptyStackItem()
    {
        var game = Create(9976);
        foreach (var player in game.State.Players)
            for (var row = 0; row < 2; row++)
                for (var slot = 0; slot < 3; slot++) player.Field[row][slot] = null;
        var source = Card("S01-0402", "batch6ja-no-target-oda");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].Hand.Add(Card("S01-0002", "batch6ja-oda-own-response"));
        game.State.Players[1].Hand.Add(Card("S01-0002", "batch6ja-oda-enemy-response"));
        var odaCounter = Card("S01-0019", "batch6ja-oda-covered-counter");
        odaCounter.Hidden = true;
        game.State.Players[1].Field[1][0] = odaCounter;

        Invoke(game, "QueueOrPushTriggeredEffect", 0, source, "enter", "织田信长无合法目标", null,
            new Dictionary<string, string>());
        Invoke(game, "AdvanceTriggerBatches");

        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(game.State.Events, entry => entry.Type == "ability-cancelled");
    }

    private static void Resolve(L12GameEngine game, string choice)
    {
        var prompt = OnlyPrompt(game);
        Assert.True(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
    }

    private static void ResolveMany(L12GameEngine game, params string[] choices)
    {
        var prompt = OnlyPrompt(game);
        Assert.True(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: choices.ToList())).Accepted);
    }

    private static void PassResponses(L12GameEngine game)
    {
        var currentStackId = game.State.EffectStack.LastOrDefault()?.StackItemId;
        var guard = 0;
        while (currentStackId is not null
            && game.State.EffectStack.Any(item => item.StackItemId == currentStackId)
            && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response" && guard++ < 4)
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }
}
