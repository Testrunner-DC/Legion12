using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class DeckCountValidationParityTests
{
    private static L12Catalog Catalog() => L12Catalog.Load(Environment.GetEnvironmentVariable("L12_CQ1_CATALOG_PATH")
        ?? Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));

    // The same fixture source also compiles against the frozen pre-CQ1 DLL.
    // CQ1_BASELINE excludes only calls to APIs that did not exist in that DLL.
    [Fact]
    public void ArrayAndCountValidationPreserveFrozenOriginalResultsAndErrorOrder()
    {
        var catalog = Catalog();
        var results = new List<object>();
        foreach (var fixture in Fixtures(catalog))
        {
            var valid = L12DeckValidator.TryValidate(catalog, fixture.Deck, out var normalized, out var error, fixture.Restrictions);
            Assert.Equal(fixture.Error is null, valid);
            Assert.Equal(fixture.Error ?? string.Empty, error);
            results.Add(new { fixture.Name, valid, error, normalized });
#if !CQ1_BASELINE
            var counts = new L12DeckCountSubmission(fixture.Deck.Name, fixture.Deck.MasterId,
                L12DeckValidator.CountCards(fixture.Deck.CardIds), L12DeckValidator.CountCards(fixture.Deck.MoraleIds),
                L12DeckValidator.CountCards(fixture.Deck.SpecialIds), L12DeckValidator.CountCards(fixture.Deck.BenchIds),
                fixture.Deck.AlternateArtCopies.ToDictionary(item => item.Key, item => (IReadOnlyList<string>?)item.Value));
            Assert.Equal(valid, L12DeckValidator.TryValidateCounts(catalog, counts, out var countError, fixture.Restrictions));
            Assert.Equal(error, countError);
#endif
        }
        var evidence = Environment.GetEnvironmentVariable("L12_CQ1_PARITY_EVIDENCE");
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(evidence, JsonSerializer.Serialize(results));
        Console.WriteLine($"CQ1 original parity fixtures: {results.Count}");
    }

#if !CQ1_BASELINE
    [Fact]
    public void CountsRejectNullNonpositiveAndOverflowWithoutAllocatingCopies()
    {
        var catalog = Catalog();
        var source = catalog.PresetDecks[0];
        var morale = L12DeckValidator.CountCards(source.MoraleIds);
        var special = L12DeckValidator.CountCards(source.SpecialIds);
        Assert.False(L12DeckValidator.TryValidateCounts(catalog, null!, out var nullSubmission));
        Assert.Equal("牌库计数字段无效", nullSubmission);
        Assert.False(L12DeckValidator.TryValidateCounts(catalog, new(null, source.MasterId, [], morale, special), out var nullName));
        Assert.Equal("牌库名称须为 1–24 个字符", nullName);
        Assert.False(L12DeckValidator.TryValidateCounts(catalog, new("count", null, [], morale, special), out var nullMaster));
        Assert.Equal("请选择有效的主宰", nullMaster);
        foreach (var main in new IReadOnlyList<L12DeckCardQuantity>?[]
        {
            null, [new(source.CardIds[0], 0)], [new(source.CardIds[0], -1)], [new(null!, 1)], [null!],
        })
        {
            Assert.False(L12DeckValidator.TryValidateCounts(catalog, new("count", source.MasterId, main, morale, special), out var error));
            Assert.Equal("牌库计数字段无效", error);
        }
        Assert.False(L12DeckValidator.TryValidateCounts(catalog, new("count", source.MasterId,
            [new(source.CardIds[0], long.MaxValue), new(source.CardIds[0], 1)], morale, special), out var overflow));
        Assert.Equal("牌库数量超出可处理范围", overflow);
        Assert.False(L12DeckValidator.TryValidateCounts(catalog, new("count", source.MasterId,
            [new(source.CardIds[0], int.MaxValue)], morale, special), out var huge));
        Assert.Contains("当前 2147483647 张", huge);
    }
#endif

    private sealed record Fixture(string Name, L12CustomDeckSubmission Deck, string? Error,
        IReadOnlyList<L12CardRestrictionConfig>? Restrictions = null);

    private static IEnumerable<Fixture> Fixtures(L12Catalog catalog)
    {
        foreach (var preset in catalog.PresetDecks) yield return new("preset:" + preset.Name, Copy(preset), null);
        var source = catalog.PresetDecks.First(deck => catalog.Cards[deck.MasterId].Faction != "otherworld");
        var master = catalog.Cards[source.MasterId];
        var first = catalog.Cards[source.CardIds[0]];
        var rune = catalog.MoraleIdentities.ForFaction(master.Faction).CanonicalCardId;
        var foreign = catalog.Cards.Values.First(card => card.CardType == "legion" && card.Faction != "universal"
            && card.Faction != master.Faction && !L12SpecialDeckRules.IsDerivedSpecialCard(card));
        var derived = catalog.Cards.Values.First(card => L12SpecialDeckRules.IsDerivedSpecialCard(card));
        var copies = source.CardIds.Count(id => id.Equals(first.Id, StringComparison.OrdinalIgnoreCase));
        yield return new("name-before-master-count", Copy(source, name: " ", master: "missing", main: []), "牌库名称须为 1–24 个字符");
        yield return new("name-before-case-duplicate-art", Copy(source, name: " ",
            art: new() { [first.Id] = ["art"], [first.Id.ToLowerInvariant()] = ["art"] }), "牌库名称须为 1–24 个字符");
        yield return new("long-name", Copy(source, name: new string('n', 25)), "牌库名称须为 1–24 个字符");
        yield return new("master-before-count", Copy(source, master: "missing", main: []), "请选择有效的主宰");
        yield return new("osiris", Copy(source, master: "S01-02M2"), "复苏的奥西里斯不能被选择为主宰；选择伊西斯时会自动置入额外区并在开局进入墓地");
        yield return new("restriction-before-count", Copy(source, main: [first.Id]), $"当前赛季禁用卡牌：{first.Id}（golden）", [new(first.Id, 0, "golden")]);
        yield return new("restriction-total-across-regions", Copy(source, main: source.CardIds.Append(rune).ToList()),
            $"当前赛季同编号卡牌最多 {source.MoraleIds.Count} 张：{rune}", [new(rune, source.MoraleIds.Count)]);
        yield return new("restriction-includes-master", Copy(source), $"当前赛季禁用卡牌：{master.Id}", [new(master.Id, 0)]);
        yield return new("master-override-global", Copy(source), null, [new(first.Id, 0), new(first.Id, copies, MasterId: master.Id)]);
        yield return new("wrong-master-override", Copy(source), $"当前赛季禁用卡牌：{first.Id}", [new(first.Id, 0), new(first.Id, 50, MasterId: "missing")]);
        yield return new("empty-main", Copy(source, main: []), "主牌库须为 40–50 张（规则标明不计入构筑的卡牌除外，当前 0 张）");
        yield return new("51-main", Copy(source, main: Enumerable.Repeat(first.Id, 51).ToList()), "主牌库须为 40–50 张（规则标明不计入构筑的卡牌除外，当前 51 张）");
        yield return new("copy-limit-before-type", Copy(source, main: Enumerable.Repeat(first.Id, 40).ToList()), $"同编号卡牌最多 {first.DeckLimit} 张：{first.Id}");
        yield return new("unknown-main", Copy(source, main: Replace(source.CardIds, "UNKNOWN")), "牌库包含未知卡牌：UNKNOWN");
        yield return new("derived-main", Copy(source, main: source.CardIds.Append(derived.Id).ToList()),
            $"{derived.NameZh} 为 Limit {L12StructuredCardSemantics.DerivedSpecialCardLimit(derived.Id)} 的衍生卡，不能放入主牌库");
        yield return new("rune-main", Copy(source, main: Replace(source.CardIds, rune)), $"{catalog.Cards[rune].NameZh} 不能放入主牌库");
        yield return new("foreign-main", Copy(source, main: Replace(source.CardIds, foreign.Id)), $"{foreign.NameZh} 与主宰阵营不符");
        yield return new("missing-art-base-before-morale", Copy(source, morale: [], art: new() { ["UNKNOWN"] = ["art"] }), "异画副本没有对应的主牌：UNKNOWN");
        yield return new("art-over-copy", Copy(source, art: new() { [first.Id] = Enumerable.Repeat("art", copies + 1).ToList() }),
            $"{first.NameZh} 的原画与异画总数超过同编号投入数量");
        yield return new("art-null", Copy(source, art: new() { [first.Id] = null! }), $"{first.NameZh} 的原画与异画总数超过同编号投入数量");
        yield return new("morale-count-before-trial-bench", Copy(source, morale: [], special: ["UNKNOWN"], bench: ["UNKNOWN"]),
            $"{master.NameZh} 的士气牌库须为 {(master.Faction == "taiyangcheng" ? 6 : 8)} 张");
        yield return new("morale-unknown", Copy(source, morale: Replace(source.MoraleIds, "UNKNOWN")), "无效的士气卡：UNKNOWN");
        var alternateRune = catalog.MoraleIdentities.ForFaction(master.Faction).VersionCardIds.FirstOrDefault(id => id != rune);
        if (alternateRune is not null) yield return new("morale-version-canonicalization", Copy(source, morale: source.MoraleIds.Select(_ => alternateRune).ToList()), null);
        yield return new("trial-before-bench", Copy(source, special: ["UNKNOWN"], bench: ["UNKNOWN"]), $"{master.NameZh} 不能携带试炼");
        var trialSource = catalog.PresetDecks.First(deck => deck.SpecialIds.Count > 0);
        var largerTrialMaster = catalog.Cards.Values.First(card => card.CardType == "master" && L12SpecialDeckRules.TrialCapacity(card) > 1);
        var capacity = L12SpecialDeckRules.TrialCapacity(largerTrialMaster);
        yield return new("trial-capacity-before-duplicate", Copy(trialSource, special: []),
            $"{catalog.Cards[trialSource.MasterId].NameZh} 的试炼区须为 {trialSource.SpecialIds.Count} 张（当前 0 张）");
        yield return new("duplicate-trial", Copy(trialSource, master: largerTrialMaster.Id,
            special: Enumerable.Repeat(trialSource.SpecialIds[0], capacity).ToList()), "试炼区不能放入重复卡牌");
        yield return new("invalid-trial", Copy(trialSource, special: Replace(trialSource.SpecialIds, "UNKNOWN")), "无效的特殊区卡牌：UNKNOWN");
        yield return new("bench-size-before-card", Copy(source, bench: Enumerable.Repeat("UNKNOWN", 201).ToList()), "备选区最多保存 200 张卡牌");
        yield return new("bench-unknown", Copy(source, bench: ["UNKNOWN"]), "备选区包含未知卡牌：UNKNOWN");
        yield return new("bench-derived", Copy(source, bench: [derived.Id]), $"{derived.NameZh} 不能放入备选区");
        yield return new("bench-faction", Copy(source, bench: [foreign.Id]), $"备选区中的 {foreign.NameZh} 与主宰阵营不符");
        yield return new("bench-copies", Copy(source, bench: Enumerable.Repeat(first.Id, Math.Min(first.DeckLimit, 50) + 1).ToList()), $"备选区同编号卡牌最多 {first.DeckLimit} 张：{first.Id}");
        yield return new("bench-excluded-from-season-restriction", Copy(source, bench: [foreign.Id]), $"备选区中的 {foreign.NameZh} 与主宰阵营不符", [new(foreign.Id, 0)]);
        yield return new("case-insensitive-main-counts", Copy(source, main: source.CardIds.Select((id, index) => index % 2 == 0 ? id.ToLowerInvariant() : id).ToList()), null);
        yield return new("output-order-and-trim", Copy(source, name: " valid ", main: source.CardIds.AsEnumerable().Reverse().ToList(),
            art: new() { [first.Id] = [" art "] }), null);
    }

    private static List<string> Replace(IReadOnlyList<string> source, string first) => [first, .. source.Skip(1)];
    private static L12CustomDeckSubmission Copy(L12PresetDeckDefinition source, string? name = null, string? master = null,
        List<string>? main = null, List<string>? morale = null, List<string>? special = null, List<string>? bench = null,
        Dictionary<string, List<string>>? art = null) => new()
    {
        Name = name ?? source.Name, MasterId = master ?? source.MasterId, CardIds = main ?? [.. source.CardIds],
        MoraleIds = morale ?? [.. source.MoraleIds], SpecialIds = special ?? [.. source.SpecialIds],
        BenchIds = bench ?? [.. source.BenchIds], AlternateArtCopies = art ?? new(),
    };
}
