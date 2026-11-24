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
            var fixtureTournament = Assert.Single(store.Tournaments(admin).Items.Where(item =>
                item.Name == "[验收] 赛事中心报名与锁牌流程"));
            Assert.Equal(2, fixtureTournament.Participants.Count);
            Assert.Contains(fixtureTournament.Participants, item => item.AccountId == admin.Id);
            Assert.All(fixtureTournament.Participants, item => Assert.NotNull(item.Deck));
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

    [Fact]
    public void RehearsalAccounts_StayOffUnlessSwitchedOnAndAcceptTheConfiguredPassword()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-rehearsal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var previousSwitch = Environment.GetEnvironmentVariable(
            L12PlatformStore.RehearsalAccountsEnvironmentKey);
        var previousPassword = Environment.GetEnvironmentVariable(
            L12PlatformStore.RehearsalPasswordEnvironmentKey);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts);

            // Default: nothing is seeded, so a production node that never sets the switch is untouched.
            Environment.SetEnvironmentVariable(L12PlatformStore.RehearsalAccountsEnvironmentKey, null);
            Environment.SetEnvironmentVariable(L12PlatformStore.RehearsalPasswordEnvironmentKey, "rehearsal-secret");
            Assert.Equal(0, store.EnsureTournamentRehearsalAccounts());
            Assert.DoesNotContain(store.Accounts(), account => account.Username.StartsWith("彩排"));

            // Switch without a usable password is still a no-op rather than a weak default.
            Environment.SetEnvironmentVariable(L12PlatformStore.RehearsalAccountsEnvironmentKey, "1");
            Environment.SetEnvironmentVariable(L12PlatformStore.RehearsalPasswordEnvironmentKey, "short");
            Assert.Equal(0, store.EnsureTournamentRehearsalAccounts());

            Environment.SetEnvironmentVariable(L12PlatformStore.RehearsalPasswordEnvironmentKey,
                "rehearsal-secret");
            Assert.Equal(8, store.EnsureTournamentRehearsalAccounts());

            // Idempotent: a second start resets the same eight accounts instead of adding more.
            Assert.Equal(0, store.EnsureTournamentRehearsalAccounts());
            var seeded = store.Accounts().Where(account => account.Username.StartsWith("彩排")).ToArray();
            Assert.Equal(8, seeded.Length);
            Assert.All(seeded, account => Assert.False(account.Disabled));
        }
        finally
        {
            Environment.SetEnvironmentVariable(L12PlatformStore.RehearsalAccountsEnvironmentKey, previousSwitch);
            Environment.SetEnvironmentVariable(L12PlatformStore.RehearsalPasswordEnvironmentKey, previousPassword);
            Directory.Delete(root, recursive: true);
        }
    }
}
