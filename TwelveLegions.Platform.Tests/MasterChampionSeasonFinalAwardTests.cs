using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// 清单P8（用户 2026-11-27）：最强主宰奖励改成「长期配置」后，必须在每个赛季结算时都能自动派发。
// 完整赛季归档依赖存储事务/租约/就绪编排，这里通过测试接缝直接驱动派发那一段。
[Collection("Platform environment")]
public sealed class MasterChampionSeasonFinalAwardTests
{
    private const string MasterId = "S01-01M1";

    /// <summary>
    /// 最强主宰投影的门槛：个人 ≥20 场、跨 ≥5 个自然日、≥10 个不同对手（2026-11-27 实测）。
    /// 夹具按这三点构造，避免把「按场次稀疏」误判成派发缺陷。
    /// </summary>
    private static L12RankedMasterTitleMatchFact[] ChampionWins(string champion, string[] opponents)
    {
        var now = DateTimeOffset.UtcNow;
        return Enumerable.Range(0, 24).Select(index => new L12RankedMasterTitleMatchFact(
            "master-champion-" + index, champion, opponents[index % opponents.Length], MasterId, "S01-02M1",
            0, 8, now.AddDays(-(index % 6)).AddHours(-index), "normal", true)).ToArray();
    }

    [Fact]
    public void LongTermMasterChampionRuleAwardsChampionsInEverySeason()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-master-champion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts);
        var admin = store.Login("Admin", "L12master").Account!;
        var champion = store.Register("主宰冠军", "Password123!").Account!;
        var opponents = Enumerable.Range(0, 10)
            .Select(index => store.Register("冠军对手" + index, "Password123!").Account!.Id).ToArray();
        store.ImportRankedMasterTitleFacts(ChampionWins(champion.Id, opponents));

        var champions = (System.Collections.IDictionary)typeof(L12PlatformStore)
            .GetMethod("CurrentMasterChampions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(store, null)!;
        Assert.True(champions.Count > 0, "最强主宰投影为空，测试夹具不成立");

        var artId = store.AlternateArts().First(row => row.Active && row.Kind == "alternate-art").Id;
        // 不填赛季 = 长期生效（用户 2026-11-27 裁定）。
        var rule = store.SaveAlternateArtAwardRule(admin,
            new L12AlternateArtAwardRuleDraft(null, artId, "master-champion-season-final", "", "", 0, true, MasterId));
        Assert.Equal(string.Empty, rule.SeasonId);

        var first = store.ApplyMasterChampionSeasonFinalAwardsForTests("season-one");
        var grant = Assert.Single(first.Where(row => row.SourceKind == "master-champion-season-final"));
        Assert.Equal(champion.Id, grant.AccountId);
        Assert.Equal(artId, grant.AlternateArtId);
        Assert.Equal("season-one:" + MasterId, grant.SourceReference);

        // 幂等：同一赛季重复结算不产生第二条。
        var second = store.ApplyMasterChampionSeasonFinalAwardsForTests("season-one");
        Assert.Equal(first.Count, second.Count);

        // 下一个赛季同样自动派发（这正是「不必每季手配」的判据）。
        var nextSeason = store.ApplyMasterChampionSeasonFinalAwardsForTests("season-two");
        Assert.Contains(nextSeason, row => row.SourceReference == "season-two:" + MasterId);

        // 停用长期规则后不再派发新赛季。
        store.SaveAlternateArtAwardRule(admin,
            new L12AlternateArtAwardRuleDraft(rule.Id, artId, "master-champion-season-final", "", "", 0, false, MasterId));
        var afterDisable = store.ApplyMasterChampionSeasonFinalAwardsForTests("season-three");
        Assert.DoesNotContain(afterDisable, row => row.SourceReference == "season-three:" + MasterId);
    }
}
