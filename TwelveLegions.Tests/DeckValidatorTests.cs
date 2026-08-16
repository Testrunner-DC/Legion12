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

    [Fact]
    public void TombGuardsDoNotCountTowardMainDeckButRemainLimitedToThreeCopies()
    {
        var preset = Catalog.PresetDecks.First(deck => Catalog.Cards[deck.MasterId].Faction == "taiyangcheng");
        var cards = preset.CardIds.Concat(Enumerable.Repeat("S01-0212", 3)).ToList();
        var valid = new L12CustomDeckSubmission
        {
            Name = "太阳城与陵墓守卫",
            MasterId = preset.MasterId,
            CardIds = cards,
            MoraleIds = preset.MoraleIds.Take(6).ToList(),
        };
        Assert.True(L12DeckValidator.TryValidate(Catalog, valid, out var deck, out var error), error);
        Assert.Equal(43, deck.CardIds.Count);

        valid.CardIds.Add("S01-0212");
        Assert.False(L12DeckValidator.TryValidate(Catalog, valid, out _, out var copyError));
        Assert.Contains("最多 3 张", copyError);
    }

    [Fact]
    public void ProliferatingBeetlesDoNotCountTowardMainDeckButRemainLimitedToThreeCopies()
    {
        var preset = Catalog.PresetDecks.First(deck => Catalog.Cards[deck.MasterId].Faction == "taiyangcheng");
        var cards = preset.CardIds.Concat(Enumerable.Repeat("S02-0201", 3)).ToList();
        var valid = new L12CustomDeckSubmission
        {
            Name = "太阳城与增殖的甲虫",
            MasterId = preset.MasterId,
            CardIds = cards,
            MoraleIds = preset.MoraleIds.Take(6).ToList(),
        };
        Assert.True(L12DeckValidator.TryValidate(Catalog, valid, out _, out var error), error);

        valid.CardIds.Add("S02-0201");
        Assert.False(L12DeckValidator.TryValidate(Catalog, valid, out _, out var copyError));
        Assert.Contains("最多 3 张", copyError);
    }
}
