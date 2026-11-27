using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// 清单P8 的第二段：完整赛季归档链路（存储事务 + 租约 + 就绪）也要把长期派发规则跑通，
// 而不只是「派发那一段」。这里走服务器自己的自动结算入口 RunSeasonFinalizationOnceAsync。
[Collection("Platform environment")]
public sealed class MasterChampionSeasonFinalizationEndToEndTests
{
    private const string MasterId = "S01-01M1";

    private static L12AdminAuditContext Context(string id)
        => new(id, RequestMethod: "TEST", RequestPath: "/test/master-champion-finalization");

    // 注意：最强主宰投影用的是真实 UtcNow 的窗口，不是注入的结算时刻；
    // 事实必须落在「现在」之前的窗口内，否则决赛投影为空（第一版就踩了这个）。
    private static L12RankedMasterTitleMatchFact[] ChampionWins(string champion, string[] opponents)
    {
        var now = DateTimeOffset.UtcNow;
        return Enumerable.Range(0, 24).Select(index => new L12RankedMasterTitleMatchFact(
            "champion-finalize-" + index, champion, opponents[index % opponents.Length], MasterId, "S01-02M1",
            0, 8, now.AddDays(-(index % 6)).AddHours(-1 - index), "normal", true)).ToArray();
    }

    [Fact]
    public async Task SeasonFinalizationAppliesLongTermMasterChampionRule()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-champion-finalize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var due = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero);
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts);
            var admin = store.Login("Admin", "L12master").Account!;
            var operations = store.OperationsConfig(admin);
            store.ApplyOperationsConfig(admin, operations.Config with
            {
                Season = operations.Config.Season with { EndsAt = due },
            }, operations.Version, "prepare master champion season finalization", Context("prepare-finalization"));
            var seasons = store.SeasonCatalog(admin);
            store.DeleteSeasonDraft(admin, seasons.Next!.DefinitionId, seasons.Next.Revision,
                "remove next season draft for deterministic finalization", Context("remove-next-season"));
            var outgoingSeasonId = store.SeasonCatalog(admin).Current.SeasonId;

            var champion = store.Register("结算冠军", "Password123!").Account!;
            var opponents = Enumerable.Range(0, 10)
                .Select(index => store.Register("结算对手" + index, "Password123!").Account!.Id).ToArray();
            store.ImportRankedMasterTitleFacts(ChampionWins(champion.Id, opponents));

            var artId = store.AlternateArts().First(row => row.Active && row.Kind == "alternate-art").Id;
            store.SaveAlternateArtAwardRule(admin, new L12AlternateArtAwardRuleDraft(
                null, artId, "master-champion-season-final", "", "", 0, true, MasterId));

            await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"), () => due);
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store, () => due);
            await using var server = new L12WebSocketServer(rooms, recorder, store, catalog,
                seasonActivationUtcNow: () => due);

            Assert.True(await server.RunSeasonFinalizationOnceAsync(due));
            Assert.NotNull(store.SeasonCatalog(admin, due).Current.FinalizedAt);

            var grants = store.AlternateArtGrants();
            var grant = Assert.Single(grants.Where(row => row.SourceKind == "master-champion-season-final"));
            Assert.Equal(champion.Id, grant.AccountId);
            Assert.Equal(artId, grant.AlternateArtId);
            Assert.Equal(outgoingSeasonId + ":" + MasterId, grant.SourceReference);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}