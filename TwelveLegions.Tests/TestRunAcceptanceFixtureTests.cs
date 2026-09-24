using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TestRunAcceptanceFixtureTests
{
    [Fact]
    public void AcceptanceProfile_RequiresIsolatedTestRunAndExactValue()
    {
        Assert.False(L12TestRunStorageProfile.AcceptanceDataEnabled(false,
            L12TestRunStorageProfile.AcceptanceDataValue));
        Assert.False(L12TestRunStorageProfile.AcceptanceDataEnabled(true, null));
        Assert.False(L12TestRunStorageProfile.AcceptanceDataEnabled(true, "acceptance-v1"));
        Assert.True(L12TestRunStorageProfile.AcceptanceDataEnabled(true,
            L12TestRunStorageProfile.AcceptanceDataValue));
    }

    [Fact]
    public void AcceptanceFixtures_AreUsefulAndIdempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts);

            var first = store.EnsureTestRunAcceptanceFixtures();
            var second = store.EnsureTestRunAcceptanceFixtures();
            var admin = store.Accounts().Single(account => account.Username == "Admin");
            var decks = store.Decks(admin.Id).Where(deck => deck.Name.StartsWith("[验收] ")).ToArray();
            var published = store.PublishedDecks(admin.Id)
                .Where(deck => deck.Deck.Name.StartsWith("[验收] ")).ToArray();

            Assert.Equal("Admin", first.Owner);
            Assert.Equal(first, second);
            Assert.Equal(3, decks.Length);
            Assert.Equal(2, published.Length);
            Assert.Equal(18, first.RankedPlayers);
            Assert.True(first.RankedMatches >= 100);
            Assert.True(first.ActiveMasters >= 2);
            Assert.Equal(9, first.HistoricalHonors);
            Assert.True(store.RankedLeaderboard(limit: 50).Count >= 18);
            Assert.NotEmpty(store.RankedSeasonHonors());
            var analytics = store.RankedAnalytics(store.TestRunAcceptanceRankedMatches(), "season");
            Assert.Equal(first.RankedMatches, analytics.Summary.Matches);
            Assert.Equal(first.ActiveMasters, analytics.Summary.ActiveMasters);
            Assert.NotEmpty(analytics.Masters);
            Assert.NotEmpty(analytics.Matchups);
            Assert.True(store.RankedMasterChampions().Count >= 3);
            var statistics = store.MergeTestRunAcceptanceStatistics(admin.Id,
                new L12PlayerStatisticsView(new(0, 0, 0, 0, 0, 0, 0, 0),
                    new(0, 0, 0, 0, 0, 0, 0, 0), [], null));
            Assert.True(statistics.Ranked.Games >= 20);
            Assert.NotEmpty(statistics.Masters);
            Assert.Contains(published, deck => deck.Deck.SpecialIds.Count > 0);
            Assert.All(published, deck =>
            {
                var details = store.PublicDeckDetails(deck.Id);
                Assert.NotNull(details);
                Assert.True(details.ContentRevision > 0);
                Assert.False(string.IsNullOrWhiteSpace(details.Guide.BuildIdea));
                Assert.NotEmpty(details.Matchups);
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
