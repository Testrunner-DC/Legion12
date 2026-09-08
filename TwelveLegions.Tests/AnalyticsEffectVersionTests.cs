using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AnalyticsEffectVersionTests
{
    private static L12CardDefinition Card(string id, string effect = "抽取1张牌。", string image = "a.webp")
        => new() { Id = id, Number = id, NameZh = id, CardType = "legion", Product = "S1",
            Faction = "test", Cost = 1, Troops = 2000, Effect = effect, ImageUrl = image };

    [Fact]
    public void VersionTracksRulesAndEngineButIgnoresImageAndEnumerationOrder()
    {
        var original = L12AnalyticsEffectVersion.Compute([Card("A"), Card("B")], "engine/one");
        Assert.Equal(original, L12AnalyticsEffectVersion.Compute(
            [Card("B", image: "new.webp"), Card("A")], "engine/one"));
        Assert.NotEqual(original, L12AnalyticsEffectVersion.Compute(
            [Card("A", "抽取2张牌。"), Card("B")], "engine/one"));
        Assert.NotEqual(original, L12AnalyticsEffectVersion.Compute([Card("A"), Card("B")], "engine/two"));
    }

    [Fact]
    public async Task OnlyNewRankedEngineMatchesReceiveAnalyticsEligibility()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-analytics-version", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "matches.db");
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        recorder.AttachCatalog(catalog);
        foreach (var mode in new[] { "ranked", "friendly", "sandbox" })
        {
            var game = new L12GameEngine(catalog, mode, "TEST01", 11,
                ["甲", "乙"], [catalog.DeckAt(0), catalog.DeckAt(1)]);
            await recorder.StartAsync(game, mode, decks: [catalog.DeckAt(0), catalog.DeckAt(1)]);
        }
        var legacy = new L12GameEngine(catalog, "state-only", "TEST02", 12,
            ["甲", "乙"], [catalog.DeckAt(0), catalog.DeckAt(1)]);
        await recorder.StartAsync(legacy.State, "ranked");
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var select = connection.CreateCommand();
        select.CommandText = "SELECT match_id,effect_version,analytics_version FROM matches ORDER BY match_id";
        await using var rows = await select.ExecuteReaderAsync();
        var count = 0;
        while (await rows.ReadAsync())
        {
            count++;
            var match = rows.GetString(0);
            Assert.Equal(match == "ranked" ? 2 : 0, rows.GetInt32(2));
            if (match != "ranked") Assert.True(rows.IsDBNull(1));
            else Assert.Equal(recorder.CurrentAnalyticsEffectVersion, rows.GetString(1));
        }
        Assert.Equal(4, count);
        Assert.Equal(recorder.CurrentAnalyticsEffectVersion, recorder.ResolveAnalyticsEffectVersion(null));
        Assert.Null(recorder.ResolveAnalyticsEffectVersion("all"));
    }
}
