using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class CommittedTitleFactReuseTests(ITestOutputHelper output)
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void ReuseContractNamesEveryPublicFactField()
        => Assert.Equal(new[] { "ConclusionKind", "EndedAt", "FinalRound", "FirstAccountId", "FirstMasterId",
                "IsRanked", "MatchId", "SecondAccountId", "SecondMasterId", "Winner" },
            typeof(L12RankedMasterTitleMatchFact).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name).Order(StringComparer.Ordinal));

    [Fact]
    public void UnchangedFactsReuseIndexAcrossUnrelatedCommitAndEqualReplacement()
    {
        using var f = new RankedIdentityFixture();
        var player = f.CreateAccount("factreuse");
        Seed(f, player.Id, 2048);
        var before = Index(f.Store);
        var oldGeneration = RankedIdentityTestState.RankedProjection(f.Store);
        var prepare = typeof(L12PlatformStore).GetMethod("PrepareCommittedRankedIdentity", PrivateInstance)!;
        var data = Data(f.Store);
        _ = prepare.Invoke(f.Store, [data]);
        var startBytes = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var iteration = 0; iteration < 5; iteration++) _ = prepare.Invoke(f.Store, [data]);
        output.WriteLine("TITLE_PREPARE_2048_FACTS_5_RUNS bytes={0} ms={1:F3}",
            GC.GetAllocatedBytesForCurrentThread() - startBytes, Stopwatch.GetElapsedTime(start).TotalMilliseconds);

        f.CommitProfile(player.Id, "order", 100_000, 5);
        Assert.NotSame(oldGeneration, RankedIdentityTestState.RankedProjection(f.Store));
        Assert.True(f.Store.RankedBattleIdentity(player.Id, 0).HighestTier);
        Assert.Same(before, Index(f.Store));

        Commit(f, () =>
        {
            foreach (var row in Records(f.Store).Cast<object>())
            {
                var property = row.GetType().GetProperty("TitleFacts")!;
                var facts = (List<L12RankedMasterTitleMatchFact>)property.GetValue(row)!;
                property.SetValue(row, facts.Select(fact => fact with { }).ToList());
            }
        });
        Assert.Same(before, Index(f.Store));
        AssertMatchesRestart(f, player.Id);
    }

    [Theory]
    [InlineData("match")]
    [InlineData("first-account")]
    [InlineData("second-account")]
    [InlineData("first-master")]
    [InlineData("second-master")]
    [InlineData("winner")]
    [InlineData("round")]
    [InlineData("timestamp")]
    [InlineData("offset")]
    [InlineData("conclusion")]
    [InlineData("ranked")]
    [InlineData("case")]
    [InlineData("reorder")]
    [InlineData("append")]
    [InlineData("remove")]
    [InlineData("duplicate")]
    public void EveryFactFieldOrderAndCountChangeInvalidatesIndex(string change)
    {
        using var f = new RankedIdentityFixture();
        var player = f.CreateAccount("factchange");
        Seed(f, player.Id, 4);
        var before = Index(f.Store);
        Commit(f, () =>
        {
            var facts = Facts(f.Store);
            var first = facts[0];
            switch (change)
            {
                case "reorder": (facts[0], facts[1]) = (facts[1], facts[0]); break;
                case "append": facts.Add(first with { MatchId = "new-fact" }); break;
                case "remove": facts.RemoveAt(0); break;
                case "duplicate": facts.Add(first with { }); break;
                default:
                    facts[0] = change switch
                    {
                        "match" => first with { MatchId = "changed-fact" },
                        "first-account" => first with { FirstAccountId = "changed-first" },
                        "second-account" => first with { SecondAccountId = "changed-second" },
                        "first-master" => first with { FirstMasterId = "S01-03M1" },
                        "second-master" => first with { SecondMasterId = "S01-03M1" },
                        "winner" => first with { Winner = 1 },
                        "round" => first with { FinalRound = 9 },
                        "timestamp" => first with { EndedAt = first.EndedAt.AddTicks(1) },
                        "offset" => first with { EndedAt = first.EndedAt.ToOffset(TimeSpan.FromHours(8)) },
                        "conclusion" => first with { ConclusionKind = "surrender" },
                        "ranked" => first with { IsRanked = false },
                        "case" => first with { MatchId = first.MatchId.ToUpperInvariant() },
                        _ => throw new ArgumentOutOfRangeException(nameof(change)),
                    };
                    break;
            }
        });
        Assert.NotSame(before, Index(f.Store));
        AssertMatchesRestart(f, player.Id);
    }

    [Fact]
    public void ChangedFactsPreparedBeforeFailedCommitNeverReplaceCommittedIndex()
    {
        using var f = new RankedIdentityFixture();
        var player = f.CreateAccount("factroll");
        Seed(f, player.Id, 4);
        var before = Index(f.Store);
        var snapshot = RankedIdentityTestState.RollbackCache(f.Store);
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage == "after-ranked-identity-index-build") throw new IOException("synthetic fact-index failure");
        };
        Assert.Throws<L12PlatformStorageUnavailableException>(() => Commit(f,
            () => Facts(f.Store).Add(Facts(f.Store)[0] with { MatchId = "failed-fact" })));
        f.Store.StorageFailureInjector = null;
        Assert.Same(before, Index(f.Store));
        Assert.Same(snapshot, RankedIdentityTestState.RollbackCache(f.Store));
        Assert.DoesNotContain(Facts(f.Store), fact => fact.MatchId == "failed-fact");
        f.CommitProfile(player.Id, "order", 0, 0);
        Assert.Same(before, Index(f.Store));
        AssertMatchesRestart(f, player.Id);
    }

    [Fact]
    public void AccountEligibilityStillRefreshesWhenFactsReuseTheIndex()
    {
        using var f = new RankedIdentityFixture();
        var player = f.CreateAccount("factelig");
        Seed(f, player.Id, 20);
        f.CommitProfile(player.Id, "order", 100_000, 5);
        var before = Index(f.Store);
        var oldRanked = RankedIdentityTestState.RankedProjection(f.Store);
        Assert.Contains(player.Id, (IReadOnlySet<string>)oldRanked.GetType().GetProperty("ChampionEligibleAccountIds")!.GetValue(oldRanked)!);
        Assert.True(f.Store.RankedBattleIdentity(player.Id, 0).HighestTier);
        f.Store.SetAccountDisabled(f.Admin, player.Id, true, "synthetic", f.Context, true);
        Assert.Same(before, Index(f.Store));
        var ranked = RankedIdentityTestState.RankedProjection(f.Store);
        Assert.DoesNotContain(player.Id, (IReadOnlySet<string>)ranked.GetType().GetProperty("ChampionEligibleAccountIds")!.GetValue(ranked)!);
        Assert.Contains(player.Id, (IReadOnlySet<string>)ranked.GetType().GetProperty("KnownInactiveAccountIds")!.GetValue(ranked)!);
        Assert.Throws<KeyNotFoundException>(() => f.Store.RankedBattleIdentity(player.Id, 0));
    }

    [Fact]
    public void FreshStoreRebuildsItsOwnIndexThenCanReuseIt()
    {
        using var f = new RankedIdentityFixture();
        var player = f.CreateAccount("factrestart");
        Seed(f, player.Id, 20);
        var restarted = new L12PlatformStore(f.Path, f.Catalog.PresetDecks, officialCards: f.Catalog.Cards);
        Assert.NotSame(Index(f.Store), Index(restarted));
        var before = Index(restarted);
        restarted.ExecuteAdminTransaction(() => { RankedIdentityTestState.RequestSave(restarted); return true; });
        Assert.Same(before, Index(restarted));
        Assert.Equal(f.Store.RankedBattleIdentity(player.Id, 0), restarted.RankedBattleIdentity(player.Id, 0));
    }

    [Fact]
    public void CurrentExclusionsAreRebuiltWithoutRebuildingTheUnchangedFactIndex()
    {
        using var f = new RankedIdentityFixture();
        var player = f.CreateAccount("factheld");
        Seed(f, player.Id, 20);
        var before = Index(f.Store);
        var match = Facts(f.Store)[0].MatchId;
        Commit(f, () =>
        {
            var data = Data(f.Store);
            var held = (IList)data.GetType().GetProperty("RankedHeldRewards")!.GetValue(data)!;
            var row = Activator.CreateInstance(held.GetType().GetGenericArguments()[0], nonPublic: true)!;
            row.GetType().GetProperty("MatchId")!.SetValue(row, match);
            row.GetType().GetProperty("FirstAccountId")!.SetValue(row, player.Id);
            row.GetType().GetProperty("SecondAccountId")!.SetValue(row, f.Admin.Id);
            foreach (var (property, account) in new[] { ("FirstBefore", player.Id), ("FirstAfter", player.Id),
                         ("SecondBefore", f.Admin.Id), ("SecondAfter", f.Admin.Id) })
            {
                var snapshot = row.GetType().GetProperty(property)!.GetValue(row)!;
                snapshot.GetType().GetProperty("AccountId")!.SetValue(snapshot, account);
            }
            held.Add(row);
        });
        var ranked = RankedIdentityTestState.RankedProjection(f.Store);
        Assert.Same(before, Index(f.Store));
        Assert.Contains(match, (IReadOnlySet<string>)ranked.GetType().GetProperty("ExcludedMatchIds")!.GetValue(ranked)!);
        AssertMatchesRestart(f, player.Id);
    }

    private static void Seed(RankedIdentityFixture f, string accountId, int count)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        f.Store.ImportRankedMasterTitleFacts(Enumerable.Range(0, count).Select(index =>
            new L12RankedMasterTitleMatchFact($"fact-{index:D5}", accountId, f.Admin.Id,
                "S01-01M1", "S01-02M1", 0, 8, now.AddTicks(index), "normal", true)).ToArray());
    }

    private static object Data(L12PlatformStore store)
        => typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;

    private static IList Records(L12PlatformStore store)
        => (IList)Data(store).GetType().GetProperty("RankedMasterRecords")!.GetValue(Data(store))!;

    private static List<L12RankedMasterTitleMatchFact> Facts(L12PlatformStore store)
    {
        var row = Records(store).Cast<object>().First(item =>
            ((List<L12RankedMasterTitleMatchFact>)item.GetType().GetProperty("TitleFacts")!.GetValue(item)!).Count > 0);
        return (List<L12RankedMasterTitleMatchFact>)row.GetType().GetProperty("TitleFacts")!.GetValue(row)!;
    }

    private static object Index(L12PlatformStore store)
    {
        var ranked = RankedIdentityTestState.RankedProjection(store);
        return ranked.GetType().GetProperty("MasterTitleIndex")!.GetValue(ranked)!;
    }

    private static void Commit(RankedIdentityFixture f, Action mutation)
        => f.Store.ExecuteAdminTransaction(() => { mutation(); RankedIdentityTestState.RequestSave(f.Store); return true; });

    private static void AssertMatchesRestart(RankedIdentityFixture f, string playerId)
    {
        var restarted = new L12PlatformStore(f.Path, f.Catalog.PresetDecks, officialCards: f.Catalog.Cards);
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(JsonSerializer.Serialize(f.Store.RankedMasterChampionsAt(now)),
            JsonSerializer.Serialize(restarted.RankedMasterChampionsAt(now)));
        Assert.Equal(f.Store.RankedBattleIdentity(playerId, 0), restarted.RankedBattleIdentity(playerId, 0));
    }
}
