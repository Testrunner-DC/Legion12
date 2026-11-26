using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

/// <summary>
/// 2026-11-26 清单P5：异画同型残差。牌库里带异画的陵墓守卫在入场流程里已经按玩家异画解析，
/// 但太阳城阵营补建到墓地的守卫实例走裸 <c>CreateCard</c>，会掉回目录原画。
/// 同型规则：凡是为某位玩家补建印刷卡实例，都必须落该玩家的异画。
/// </summary>
public sealed class TombGuardBackfillAlternateArtTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void BackfilledTombGuardsCarryTheOwnersAlternateArt()
    {
        const string alternateArtUrl = "/cards/alternate/tomb-guard.png";
        var alternateArtUrls = new[]
        {
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["S01-0212"] = alternateArtUrl,
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };
        var catalog = Catalog;
        Assert.Contains("S01-0212", catalog.Cards.Keys);
        var deck = new L12PresetDeckDefinition
        {
            Name = "太阳城守卫补建",
            MasterId = "S01-02M1",
            CardIds = [],
            MoraleIds = [],
            SpecialIds = [],
        };
        var game = new L12GameEngine(catalog, "tomb-guard-backfill", "TOMBGUARD", 20261126,
            ["甲", "乙"], [deck, catalog.DeckAt(0)], skipPreparation: true,
            concealHiddenResponseAvailability: false, alternateArtUrls: alternateArtUrls);
        Assert.Equal("taiyangcheng", game.State.Players[0].Faction);

        var guards = game.State.Players[0].Graveyard
            .Where(card => string.Equals(card.CardId, "S01-0212", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(3, guards.Length);
        Assert.All(guards, guard => Assert.Equal(alternateArtUrl, guard.ImageUrl));

        // 对手没有选中该异画：仍是目录原画，说明按控制者解析而不是全局替换。
        var opponentGuards = game.State.Players[1].Graveyard
            .Count(card => string.Equals(card.CardId, "S01-0212", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, opponentGuards);
    }
}
