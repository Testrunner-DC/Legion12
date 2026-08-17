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
}
