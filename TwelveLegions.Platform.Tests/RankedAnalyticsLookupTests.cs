using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RankedAnalyticsLookupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwentyThousandMatchesResolveEachDistinctIdentityOnlyOnce(bool legacyNames)
    {
        using var f = new RankedIdentityFixture();
        var firstLogin = f.Store.Register("lkfirst", "Password123!");
        var secondLogin = f.Store.Register("lksecond", "Password123!");
        Assert.True(firstLogin.Success, firstLogin.Message);
        Assert.True(secondLogin.Success, secondLogin.Message);
        var first = firstLogin.Account!;
        var second = secondLogin.Account!;
        var masters = f.Catalog.Cards.Values.Where(card => card.CardType == "master" && card.Id != "S01-02M2")
            .OrderBy(card => card.Id, StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        Assert.Equal(2, masters.Length);
        var now = DateTimeOffset.UtcNow;
        var source = Enumerable.Range(0, 20_000).Select(index => new L12RankingMatch(
            "lookup-" + index, "first", "second", now.AddHours(-2).ToString("O"), now.AddHours(-1).ToString("O"),
            index % 2, masters[0].NameZh, masters[1].NameZh, index % 2,
            legacyNames ? null : masters[0].Id, legacyNames ? null : masters[1].Id,
            index % 2 == 0 ? first.Id : first.Id.ToUpperInvariant(), second.Id)).ToArray();
        var lookups = new Dictionary<string, int>(StringComparer.Ordinal);
        f.Store.RankedAnalyticsLookupObserver = kind => lookups[kind] = lookups.GetValueOrDefault(kind) + 1;
        var result = f.Store.RankedAnalytics(source, "7d", now);
        Assert.Equal(20_000, result.Summary.Matches);
        Assert.Equal(2, result.Summary.ActiveMasters);
        Assert.All(result.Masters, row => Assert.Equal(20_000, row.Games));
        Assert.Equal(2, lookups.GetValueOrDefault("master"));
        Assert.Equal(2, lookups.GetValueOrDefault("account"));
        lookups.Clear();
        var players = f.Store.RankedIntervalLeaderboard(source, "7d", observedAt: now);
        Assert.Equal(20_000, players.Sum(row => row.Wins));
        Assert.Equal(2, lookups.GetValueOrDefault("master"));
        Assert.Equal(2, lookups.GetValueOrDefault("account"));
    }

    [Fact]
    public void RequestLocalLookupsKeepLegacyIdentitySemanticsAndRefreshAccountAuthority()
    {
        using var f = new RankedIdentityFixture();
        var firstResult = f.Store.Register("lkfirst", "Password123!");
        var secondResult = f.Store.Register("lksecond", "Password123!");
        Assert.True(firstResult.Success, firstResult.Message);
        Assert.True(secondResult.Success, secondResult.Message);
        var first = firstResult.Account!; var second = secondResult.Account!;
        var master = f.Catalog.Cards.Values.First(card => card.CardType == "master" && card.Id != "S01-02M2");
        var now = DateTimeOffset.UtcNow;
        L12RankingMatch Match(string id) => new(id, "old-name-a", "old-name-b",
            now.AddHours(-2).ToString("O"), now.AddHours(-1).ToString("O"), 0,
            master.NameZh, master.NameZh, 0, master.Id, master.Id, first.Id, second.Id);
        var valid = Match("valid");
        var source = new[] { valid, valid with { MatchId = "VALID", Winner = 1 },
            Match("legacy") with { MasterId0 = null, MasterId1 = null },
            Match("unknown-account") with { AccountId0 = "unknown-historical-id" },
            Match("missing-account") with { AccountId0 = null },
            Match("bad-explicit") with { MasterId0 = "unknown-master" },
            Match("blank-explicit") with { MasterId0 = "" },
            Match("bad-legacy") with { MasterId0 = null, Master0 = "unknown-name" },
            Match("wrong-namespace") with { MasterId0 = null, Master0 = master.Id },
            Match("bad-clock") with { StartedUtc = "broken" } };
        var firstRead = f.Store.RankedAnalytics(source, "7d", now);
        Assert.Equal(4, firstRead.Summary.Matches);
        Assert.Equal(8, Assert.Single(firstRead.Masters).Games);
        Assert.Equal(4, Assert.Single(firstRead.Masters).Wins);
        f.Store.SetAccountDisabled(f.Admin, first.Id, true, "synthetic disable", f.Context, true);
        Assert.Equal(2, f.Store.RankedAnalytics(source, "7d", now).Summary.Matches);
        f.Store.SetAccountDisabled(f.Admin, first.Id, false, "synthetic enable", f.Context, true);
        Assert.Equal(4, f.Store.RankedAnalytics(source, "7d", now).Summary.Matches);
        // A name-only/null-account row cannot earn a stable-account interval credit.
        Assert.Equal(3, f.Store.RankedIntervalLeaderboard(source, "7d", observedAt: now).Sum(row => row.Wins));
        // Re-reading the same source is not a settled cache or a write to the underlying matches.
        Assert.Equal(0, valid.Winner);
        Assert.Equal(1, source[1].Winner);
    }
}
