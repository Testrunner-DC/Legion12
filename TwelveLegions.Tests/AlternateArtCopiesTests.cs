using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AlternateArtCopiesTests
{
    [Fact]
    public void ValidatorRejectsAppearanceCopiesBeyondTheSharedBaseCardCount()
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var preset = catalog.PresetDecks[0];
        var cardId = preset.CardIds.First();
        var count = preset.CardIds.Count(id => id.Equals(cardId, StringComparison.OrdinalIgnoreCase));
        var submission = new L12CustomDeckSubmission
        {
            Name = "异画共享上限测试",
            MasterId = preset.MasterId,
            CardIds = preset.CardIds.ToList(),
            MoraleIds = preset.MoraleIds.ToList(),
            SpecialIds = preset.SpecialIds.ToList(),
            AlternateArtCopies = new(StringComparer.OrdinalIgnoreCase)
            {
                [cardId] = Enumerable.Repeat("alternate-art", count + 1).ToList(),
            },
        };

        Assert.False(L12DeckValidator.TryValidate(catalog, submission, out _, out var error));
        Assert.Contains("原画与异画总数", error);
    }

    [Fact]
    public void PerCopyAlternateArtDoesNotReplaceTheOriginalCopy()
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var preset = catalog.PresetDecks[0];
        var cardId = preset.CardIds.First();
        var deck = new L12PresetDeckDefinition
        {
            Name = "异画副本运行时测试",
            MasterId = preset.MasterId,
            CardIds = [cardId, cardId],
            MoraleIds = preset.MoraleIds.ToList(),
            SpecialIds = preset.SpecialIds.ToList(),
        };
        const string alternateUrl = "/api/site-media/alternate-test";
        var engine = new L12GameEngine(catalog, "alternate-art-copy-test", "COPY", 7,
            ["玩家一", "玩家二"], [deck, deck], skipPreparation: true,
            alternateArtUrls:
            [
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [$"{cardId}#1"] = alternateUrl,
                },
                new Dictionary<string, string>(),
            ]);

        var cards = engine.State.Players[0].Library.Concat(engine.State.Players[0].Hand)
            .Concat(engine.State.Players[0].Graveyard).ToArray();
        Assert.Equal(alternateUrl, Assert.Single(cards, card => card.InstanceId == "p0-c1").ImageUrl);
        Assert.NotEqual(alternateUrl, Assert.Single(cards, card => card.InstanceId == "p0-c2").ImageUrl);
    }
}
