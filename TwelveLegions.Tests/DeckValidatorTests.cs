using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class DeckValidatorTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void AcceptsAValidCustomDeck()
    {
        var preset = Catalog.PresetDecks.First(deck => Catalog.Cards[deck.MasterId].Faction == "tianting");
        var submission = new L12CustomDeckSubmission
        {
            Name = "我的天廷牌库",
            MasterId = preset.MasterId,
            CardIds = preset.CardIds.ToList(),
            MoraleIds = preset.MoraleIds.ToList(),
            SpecialIds = preset.SpecialIds.ToList(),
        };

        Assert.True(L12DeckValidator.TryValidate(Catalog, submission, out var deck, out var error), error);
        Assert.Equal(40, deck.CardIds.Count);
        Assert.Equal(8, deck.MoraleIds.Count);
    }

    [Fact]
    public void RejectsTooManyCopiesAndCrossFactionCards()
    {
        var preset = Catalog.PresetDecks.First(deck => Catalog.Cards[deck.MasterId].Faction == "tianting");
        var repeated = preset.CardIds[0];
        var excessive = new L12CustomDeckSubmission
        {
            Name = "重复卡牌",
            MasterId = preset.MasterId,
            CardIds = Enumerable.Repeat(repeated, 40).ToList(),
            MoraleIds = preset.MoraleIds.ToList(),
        };
        Assert.False(L12DeckValidator.TryValidate(Catalog, excessive, out _, out var copyError));
        Assert.Contains("最多 3 张", copyError);

        var crossFaction = Catalog.Cards.Values.First(card =>
            card.CardType == "legion" && card.Faction is not "universal" and not "tianting");
        var cards = preset.CardIds.ToList();
        cards[0] = crossFaction.Id;
        var mismatched = new L12CustomDeckSubmission
        {
            Name = "跨阵营牌库",
            MasterId = preset.MasterId,
            CardIds = cards,
            MoraleIds = preset.MoraleIds.ToList(),
        };
        Assert.False(L12DeckValidator.TryValidate(Catalog, mismatched, out _, out var factionError));
        Assert.Contains("阵营不符", factionError);
    }

    [Theory]
    [InlineData("S01-0216", "taiyangcheng")]
    [InlineData("S01-0217", "taiyangcheng")]
    [InlineData("S01-0218", "taiyangcheng")]
    [InlineData("S01-0219", "taiyangcheng")]
    [InlineData("S01-0220", "taiyangcheng")]
    [InlineData("S02-0008", "tianting")]
    [InlineData("S02-0301", "asgard")]
    public void LimitOneCardsAreRejectedAtTheSecondCopy(string cardId, string faction)
    {
        var preset = Catalog.PresetDecks.First(deck => Catalog.Cards[deck.MasterId].Faction == faction);
        var cards = preset.CardIds.ToList();
        while (cards.Count(id => id == cardId) < 2)
        {
            var replace = cards.FindIndex(id => id != cardId && !L12SpecialDeckRules.DoesNotCountTowardMainDeck(Catalog.Cards[id]));
            Assert.True(replace >= 0);
            cards[replace] = cardId;
        }
        var submission = new L12CustomDeckSubmission
        {
            Name = $"限1-{cardId}",
            MasterId = preset.MasterId,
            CardIds = cards,
            MoraleIds = preset.MoraleIds.ToList(),
            SpecialIds = preset.SpecialIds.ToList(),
        };

        Assert.False(L12DeckValidator.TryValidate(Catalog, submission, out _, out var error));
        Assert.Contains("最多 1 张", error);
    }

    [Fact]
    public void CatalogKeepsTheCompleteLimitOneCardSet()
    {
        var limited = Catalog.Cards.Values
            .Where(card => card.DeckLimit == 1)
            .Select(card => card.Id)
            .Order()
            .ToArray();

        Assert.Equal([
            "S01-0216", "S01-0217", "S01-0218", "S01-0219", "S01-0220",
            "S02-0008", "S02-0301",
        ], limited);
    }

    [Fact]
    public void TombGuardsDoNotCountTowardMainDeckButRemainLimitedToThreeCopies()
    {
        var preset = Catalog.PresetDecks.First(deck => Catalog.Cards[deck.MasterId].Faction == "taiyangcheng");
        var valid = new L12CustomDeckSubmission
        {
            Name = "太阳城与陵墓守卫",
            MasterId = preset.MasterId,
            CardIds = preset.CardIds.ToList(),
            MoraleIds = preset.MoraleIds.ToList(),
        };
        Assert.True(L12DeckValidator.TryValidate(Catalog, valid, out var deck, out var error), error);
        Assert.Equal(43, deck.CardIds.Count);

        valid.CardIds.Add("S01-0212");
        Assert.False(L12DeckValidator.TryValidate(Catalog, valid, out _, out var copyError));
        Assert.Contains("最多 3 张", copyError);
    }

    [Fact]
    public void RuleTextSpecialLegionsDoNotCountAndStartInOwnersGraveyard()
    {
        var preset = Catalog.PresetDecks.First(deck => Catalog.Cards[deck.MasterId].Faction == "taiyangcheng");
        var submission = new L12CustomDeckSubmission
        {
            Name = "太阳城特殊军团",
            MasterId = preset.MasterId,
            CardIds = [.. preset.CardIds, "S02-0201", "S02-0201", "S02-0201"],
            MoraleIds = preset.MoraleIds.ToList(),
            SpecialIds = preset.SpecialIds.ToList(),
        };

        Assert.True(L12DeckValidator.TryValidate(Catalog, submission, out var deck, out var error), error);
        Assert.Equal(preset.CardIds.Count + 3, deck.CardIds.Count);

        var opponent = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "special-start", "SPECIAL", 90201,
            ["甲", "乙"], [deck, opponent], skipPreparation: true, disasterMode: "none");
        var owner = game.State.Players[0];

        Assert.Equal(3, owner.Graveyard.Count(card => card.CardId == "S02-0201"));
        Assert.DoesNotContain(owner.Hand, card => card.CardId == "S02-0201");
        Assert.DoesNotContain(owner.Library, card => card.CardId == "S02-0201");
        Assert.Equal(3, owner.Graveyard.Count(card => card.CardId == "S01-0212"));
    }

    [Fact]
    public void AllRuleTextStartingGraveyardCardsShareTheSameDeckRules()
    {
        var specialLegions = Catalog.Cards.Values
            .Where(card => card.Effect?.Contains("游戏开始时置入墓地", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(new[] { "S01-0212", "S02-0201" }, specialLegions.Select(card => card.Id).Order().ToArray());
        Assert.All(specialLegions, card =>
        {
            Assert.True(L12SpecialDeckRules.DoesNotCountTowardMainDeck(card));
            Assert.True(L12SpecialDeckRules.StartsInGraveyard(card));
        });
    }

    [Fact]
    public void EveryNumberedLegionHasPrintedCostAndTroops()
    {
        var incomplete = Catalog.Cards.Values
            .Where(card => card.CardType == "legion"
                && System.Text.RegularExpressions.Regex.IsMatch(card.Id, @"^S\d{2}-\d{4}$")
                && (card.Cost is null || card.Troops is null))
            .Select(card => $"{card.Id} {card.NameZh}")
            .Order()
            .ToArray();

        Assert.True(incomplete.Length == 0, $"普通军团缺少印刷费用或兵力：{string.Join("、", incomplete)}");
    }

    [Fact]
    public void OtherworldPresetKeepsItsTrialOutsideTheMainDeck()
    {
        var preset = Catalog.PresetDecks.Single(deck => deck.MasterId == "S02-06M1");
        var submission = new L12CustomDeckSubmission
        {
            Name = preset.Name,
            MasterId = preset.MasterId,
            CardIds = preset.CardIds.ToList(),
            MoraleIds = preset.MoraleIds.ToList(),
            SpecialIds = preset.SpecialIds.ToList(),
        };

        Assert.True(L12DeckValidator.TryValidate(Catalog, submission, out var deck, out var error), error);
        Assert.Equal(42, deck.CardIds.Count);
        Assert.Equal(["S02-06S4"], deck.SpecialIds);
    }

    [Fact]
    public void AcceptsADivinityAsTheDeckMaster()
    {
        var preset = Catalog.PresetDecks.Single(deck => deck.MasterId == "S02-06M1");
        var submission = new L12CustomDeckSubmission
        {
            Name = "阿瓦隆牌库",
            MasterId = "S02-06D1",
            CardIds = preset.CardIds.ToList(),
            MoraleIds = preset.MoraleIds.ToList(),
            SpecialIds = preset.SpecialIds.ToList(),
        };

        Assert.True(L12DeckValidator.TryValidate(Catalog, submission, out var deck, out var error), error);
        Assert.Equal("S02-06D1", deck.MasterId);
    }
}
