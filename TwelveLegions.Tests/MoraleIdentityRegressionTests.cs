using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MoraleIdentityRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static TheoryData<string, string, string, string?> Factions => new()
    {
        { "tianting", "S01-01C1", "士气·天廷", null },
        { "taiyangcheng", "S01-02C1", "士气·太阳城", null },
        { "asgard", "S01-03C1", "士气·阿斯加德", null },
        { "gaotianyuan", "S01-04C1", "士气·高天原", null },
        { "olympus", "S02-05C1", "士气·奥林匹斯", "S02-05C1" },
        { "otherworld", "S02-06C1", "士气·彼界", null },
    };

    [Theory]
    [MemberData(nameof(Factions))]
    public void CanonicalMoraleIdentityUsesTheEarliestOrdinaryFace(
        string faction, string canonicalId, string displayName, string? godPowerId)
    {
        var identity = Catalog.MoraleIdentities.ForFaction(faction);

        Assert.Equal(canonicalId, identity.CanonicalCardId);
        Assert.Equal(displayName, identity.DisplayName);
        Assert.Equal(godPowerId, identity.GodPowerCardId);
        Assert.False(identity.CanonicalCardId.StartsWith("ST", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(displayName, Catalog.Cards[canonicalId].NameZh);
        if (godPowerId is not null)
        {
            Assert.Equal("神力·奥林匹斯", identity.GodPowerDisplayName);
            Assert.Equal("S02-05C1(B)", identity.GodPowerDisplayNumber);
            Assert.Contains("消耗并翻转1神力", identity.GodPowerEffectText);
        }
    }

    [Theory]
    [MemberData(nameof(Factions))]
    public void EveryMoraleVersionNormalizesToTheFactionCanonicalCard(
        string faction, string canonicalId, string _, string? godPowerId)
    {
        var identity = Catalog.MoraleIdentities.ForFaction(faction);
        foreach (var versionId in identity.VersionCardIds)
        {
            Assert.True(Catalog.MoraleIdentities.IsVersionForFaction(versionId, faction));
            Assert.Equal(canonicalId, Catalog.MoraleIdentities.CanonicalDeckCardId(versionId));
        }
        if (godPowerId is not null)
            Assert.Equal(canonicalId, Catalog.MoraleIdentities.CanonicalDeckCardId(godPowerId));
    }

    [Theory]
    [MemberData(nameof(Factions))]
    public void DeckValidationAcceptsOldAndStarterVersionsButStoresOnlyCanonicalMorale(
        string faction, string canonicalId, string _, string? godPowerId)
    {
        var preset = Catalog.PresetDecks.First(deck => Catalog.Cards[deck.MasterId].Faction == faction);
        var alias = Catalog.MoraleIdentities.ForFaction(faction).VersionCardIds.Last();
        var submission = new L12CustomDeckSubmission
        {
            Name = $"{faction}-士气归一化",
            MasterId = preset.MasterId,
            CardIds = [.. preset.CardIds],
            MoraleIds = Enumerable.Repeat(alias, preset.MoraleIds.Count).ToList(),
            SpecialIds = [.. preset.SpecialIds],
        };

        Assert.True(L12DeckValidator.TryValidate(Catalog, submission, out var normalized, out var error), error);
        Assert.All(normalized.MoraleIds, id => Assert.Equal(canonicalId, id));
        if (godPowerId is not null)
            Assert.Equal(canonicalId, Catalog.MoraleIdentities.CanonicalDeckCardId(godPowerId));
    }

    [Fact]
    public void GameInitializationDefensivelyNormalizesOlympusAndOtherworldStarterMorale()
    {
        var olympusBase = Catalog.PresetDecks.First(deck => Catalog.Cards[deck.MasterId].Faction == "olympus");
        var otherworldBase = Catalog.PresetDecks.First(deck => Catalog.Cards[deck.MasterId].Faction == "otherworld");
        var olympus = CopyWithMorale(olympusBase, "ST05-C1");
        var otherworld = CopyWithMorale(otherworldBase, "ST06-C1");

        var game = new L12GameEngine(Catalog, "morale-identities", "MORALE", 69031,
            ["奥林匹斯", "彼界"], [olympus, otherworld], disasterMode: "none");

        Assert.All(game.State.Players[0].MoraleDeck, card =>
        {
            Assert.Equal("S02-05C1", card.CardId);
            Assert.False(card.IsGodPower);
        });
        Assert.All(game.State.Players[1].MoraleDeck, card => Assert.Equal("S02-06C1", card.CardId));
    }

    [Fact]
    public void OfficialPresetsNeverExposeStarterOrGodPowerFacesAsDefaultMorale()
    {
        foreach (var deck in Catalog.PresetDecks)
        {
            var faction = Catalog.Cards[deck.MasterId].Faction;
            var expected = Catalog.MoraleIdentities.ForFaction(faction).CanonicalCardId;
            Assert.All(deck.MoraleIds, cardId => Assert.Equal(expected, cardId));
        }
    }

    private static L12PresetDeckDefinition CopyWithMorale(L12PresetDeckDefinition source, string moraleId)
        => new()
        {
            Name = source.Name,
            MasterId = source.MasterId,
            CardIds = [.. source.CardIds],
            MoraleIds = Enumerable.Repeat(moraleId, source.MoraleIds.Count).ToList(),
            SpecialIds = [.. source.SpecialIds],
        };
}
