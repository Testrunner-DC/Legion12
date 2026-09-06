using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RankedMasterTitleProjectionTests
{
    private const string TargetMaster = "S01-04M1";
    private const string OtherMaster = "ST03-M1";
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void RollingWindowAndEveryEligibilityFactAreAppliedWithoutGuessing()
    {
        var path = Path.Combine(Path.GetTempPath(), "l12-master-title-window",
            Guid.NewGuid().ToString("N"), "platform.json");
        var catalog = Catalog;
        var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
        var candidate = store.Register("window-candidate", "Password123!").Account!;
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var facts = CandidateFacts("window", candidate.Id, 20, now).ToList();
        facts[0] = facts[0] with { EndedAt = now - L12PlatformStore.RankedMasterTitleWindow };
        facts.Add(Fact("window-non-ranked", candidate.Id, "other-a", 0, 6, now, isRanked: false));
        facts.Add(Fact("window-short", candidate.Id, "other-b", 0, 5, now));
        facts.Add(Fact("window-disconnect", candidate.Id, "other-c", 0, 6, now,
            conclusion: "disconnect-timeout"));
        facts.Add(Fact("window-same-account", candidate.Id, candidate.Id, 0, 6, now));
        facts.Add(Fact("window-no-winner", candidate.Id, "other-d", null, 6, now));
        facts.Add(Fact("window-unknown", candidate.Id, "other-e", 0, 6, now,
            conclusion: "unknown"));
        facts.Add(Fact("window-invalid-master", candidate.Id, "other-f", 0, 6, now,
            secondMaster: "not-a-master"));

        Assert.Equal(facts.Count, store.ImportRankedMasterTitleFacts(facts));
        var champion = Assert.Single(store.RankedMasterChampionsAt(now),
            item => item.MasterId == TargetMaster);
        Assert.Equal(candidate.Username, champion.Username);
        Assert.Equal(20, champion.Games);
        Assert.Empty(store.RankedMasterChampionsAt(now.AddTicks(1)));

        var reloaded = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
        Assert.Equal(candidate.Username, Assert.Single(reloaded.RankedMasterChampionsAt(now),
            item => item.MasterId == TargetMaster).Username);
    }

    [Fact]
    public void MirrorMatchesCountOnceGloballyButOnceForEachPlayersPersonalGames()
    {
        var catalog = Catalog;
        var store = new L12PlatformStore(Path.Combine(Path.GetTempPath(), "l12-master-title-mirror",
            Guid.NewGuid().ToString("N"), "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var candidate = store.Register("mirror-candidate", "Password123!").Account!;
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var personal = CandidateFacts("mirror-personal", candidate.Id, 20, now,
            secondMaster: TargetMaster);
        var firstBackground = MirrorBackgroundFacts("mirror-background-a", 90, now);

        store.ImportRankedMasterTitleFacts(personal.Concat(firstBackground).ToArray());
        Assert.Equal(20, Assert.Single(store.RankedMasterChampionsAt(now),
            item => item.MasterId == TargetMaster).Games);

        store.ImportRankedMasterTitleFacts(MirrorBackgroundFacts("mirror-background-b", 90, now));
        Assert.Empty(store.RankedMasterChampionsAt(now));

        store.ImportRankedMasterTitleFacts(CandidateFacts("mirror-personal-more", candidate.Id,
            20, now.AddMinutes(-30), secondMaster: TargetMaster));
        Assert.Equal(40, Assert.Single(store.RankedMasterChampionsAt(now),
            item => item.MasterId == TargetMaster).Games);
    }

    [Fact]
    public void LeaveOnePlayerOutRemovesTheWholeMirrorMatch()
    {
        var catalog = Catalog;
        var store = new L12PlatformStore(Path.Combine(Path.GetTempPath(), "l12-master-title-loo",
            Guid.NewGuid().ToString("N"), "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var registered = new[]
        {
            store.Register("loo-first", "Password123!").Account!,
            store.Register("loo-second", "Password123!").Account!,
        }.OrderBy(account => account.Id, StringComparer.Ordinal).ToArray();
        var candidateB = registered[0];
        var candidateA = registered[1];
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var mirror = Enumerable.Range(0, 20).Select(index => Fact($"loo-mirror-{index}",
            candidateA.Id, $"loo-a-opponent-{index % 10}", index < 15 ? 0 : 1, 6,
            now.AddDays(-(index % 5)).AddMinutes(-index - 1), secondMaster: TargetMaster));
        var ordinary = Enumerable.Range(0, 20).Select(index => Fact($"loo-ordinary-{index}",
            candidateB.Id, $"loo-b-opponent-{index % 10}", index < 15 ? 0 : 1, 6,
            now.AddDays(-(index % 5)).AddMinutes(-index - 1)));

        store.ImportRankedMasterTitleFacts(mirror.Concat(ordinary).ToArray());

        var champion = Assert.Single(store.RankedMasterChampionsAt(now),
            item => item.MasterId == TargetMaster);
        Assert.Equal(candidateA.Username, champion.Username);
        Assert.Equal(15, champion.Wins);
    }

    [Fact]
    public void ExactTiesUseStableUniqueAccountOrderInsteadOfNickname()
    {
        var catalog = Catalog;
        var store = new L12PlatformStore(Path.Combine(Path.GetTempPath(), "l12-master-title-tie",
            Guid.NewGuid().ToString("N"), "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var accounts = new[]
        {
            store.Register("zzz-nickname", "Password123!").Account!,
            store.Register("aaa-nickname", "Password123!").Account!,
        };
        var expected = accounts.OrderBy(account => account.Id, StringComparer.Ordinal).First();
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var facts = accounts.SelectMany((account, player) => Enumerable.Range(0, 20).Select(index =>
            Fact($"tie-{player}-{index}", account.Id, $"tie-{player}-opponent-{index % 10}",
                index % 2, 6, now.AddDays(-(index % 5)).AddMinutes(-index - player - 1))));

        store.ImportRankedMasterTitleFacts(facts.ToArray());

        Assert.Equal(expected.Username, Assert.Single(store.RankedMasterChampionsAt(now),
            item => item.MasterId == TargetMaster).Username);
    }

    [Fact]
    public void MissingLegacyRoundCanOnlyBeUpgradedByAnOtherwiseIdenticalAuthoritativeFact()
    {
        var catalog = Catalog;
        var store = new L12PlatformStore(Path.Combine(Path.GetTempPath(), "l12-master-title-upgrade",
            Guid.NewGuid().ToString("N"), "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var candidate = store.Register("upgrade-candidate", "Password123!").Account!;
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var incomplete = Fact("upgrade-match", candidate.Id, "upgrade-opponent", 0, 0, now);
        var complete = incomplete with { FinalRound = 6 };

        Assert.Equal(1, store.ImportRankedMasterTitleFacts([incomplete]));
        Assert.Equal(1, store.ImportRankedMasterTitleFacts([complete]));
        Assert.Equal(0, store.ImportRankedMasterTitleFacts([complete]));
        Assert.Throws<InvalidOperationException>(() =>
            store.ImportRankedMasterTitleFacts([complete with { FinalRound = 7 }]));
    }

    private static L12RankedMasterTitleMatchFact[] CandidateFacts(string prefix, string accountId,
        int count, DateTimeOffset now, string secondMaster = OtherMaster)
        => Enumerable.Range(0, count).Select(index => Fact($"{prefix}-{index}", accountId,
            $"{prefix}-opponent-{index % 10}", 0, 6,
            now.AddDays(-(index % 5)).AddMinutes(-index - 1), secondMaster: secondMaster)).ToArray();

    private static L12RankedMasterTitleMatchFact[] MirrorBackgroundFacts(string prefix, int count,
        DateTimeOffset now)
        => Enumerable.Range(0, count).Select(index => Fact($"{prefix}-{index}",
            $"{prefix}-first-{index}", $"{prefix}-second-{index}", index % 2, 6,
            now.AddDays(-(index % 5)).AddMinutes(-index - 1), secondMaster: TargetMaster)).ToArray();

    private static L12RankedMasterTitleMatchFact Fact(string matchId, string firstAccountId,
        string secondAccountId, int? winner, int finalRound, DateTimeOffset endedAt,
        string conclusion = "normal", bool isRanked = true, string secondMaster = OtherMaster)
        => new(matchId, firstAccountId, secondAccountId, TargetMaster, secondMaster, winner,
            finalRound, endedAt, conclusion, isRanked);
}
