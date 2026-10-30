using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RankedMasterTitleExclusionProjectionTests(ITestOutputHelper output)
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly MethodInfo Project = typeof(L12PlatformStore).GetMethod(
        "ProjectRankedMasterTitleExclusions", BindingFlags.NonPublic | BindingFlags.Static)!;
    private sealed record Decision(string Id, long Revision, string Disposition, string[] MatchIds,
        string Scope = "match", string? RevokesDecisionId = null);
    private sealed record Input(Decision[] Decisions, string[] Held, string[] Waived);

    [Fact]
    public void RepeatedHistoricalMatchScansAreRemovedWithoutChangingRealGallerySave()
    {
        using var f = new RankedIdentityFixture();
        var player = f.CreateAccount("titlecost");
        var facts = Facts(512, player.Id, f.Admin.Id);
        f.Store.ImportRankedMasterTitleFacts(facts);
        var input = new Input(Enumerable.Range(0, 128).Select(i => new Decision($"d-{i}", i + 1,
            i % 2 == 0 ? "confirmed" : "normal",
            Enumerable.Range(i * 4, 4).Select(j => $"match-{j}").ToArray())).ToArray(), [], []);
        Commit(f, () => Populate(Data(f.Store), input));
        var data = Data(f.Store);
        var old = Measure(() => OldProjection(input, facts), 5);
        var candidate = Measure(() => IndexedProjection(input, facts), 5);
        var actual = Measure(() => Production(data, facts), 5);
        Equal(old.Value, candidate.Value);
        Equal(old.Value, actual.Value);
        Print("helper_512_facts_128_decisions_5_runs_old_oracle", old);
        Print("helper_512_facts_128_decisions_5_runs_indexed_candidate", candidate);
        Print("helper_512_facts_128_decisions_5_runs_product", actual);

        var index = RankedValue(f.Store, "MasterTitleIndex");
        var beforeFacts = JsonSerializer.Serialize(facts);
        var beforeTitles = JsonSerializer.Serialize(f.Store.RankedMasterChampionsAt(DateTimeOffset.UtcNow));
        var version = f.Store.Version;
        var save = Measure(() => f.Store.SaveAlternateArtProduct(f.Admin,
            new L12AlternateArtProductDraft(null, "synthetic title exclusion control"),
            new L12AdminAuditContext("synthetic-title-exclusions", "admin.content.draft",
                RequestMethod: "TEST", RequestPath: "/test/title-exclusions")), 1, warm: false);
        Print("whole_real_gallery_save_not_attributable_to_helper", save);
        Assert.Equal(version + 1, f.Store.Version);
        Assert.Equal("synthetic title exclusion control", save.Value.Name);
        Assert.Same(index, RankedValue(f.Store, "MasterTitleIndex"));
        Equal(old.Value, Exclusions(f.Store));
        Assert.Equal(beforeFacts, JsonSerializer.Serialize(facts));
        Assert.Equal(beforeTitles, JsonSerializer.Serialize(f.Store.RankedMasterChampionsAt(DateTimeOffset.UtcNow)));
        var restarted = new L12PlatformStore(f.Path, f.Catalog.PresetDecks, officialCards: f.Catalog.Cards);
        Equal(old.Value, Exclusions(restarted));
        // Allocation, not elapsed-time, enforces removal of per-fact LINQ scan machinery.
        // Both controls use the same bounded facts/decisions; no online/peak claim.
        Assert.True(candidate.Bytes < old.Bytes * 3 / 4, "The proposed local algorithm must first demonstrate a benefit.");
        Assert.True(actual.Bytes < old.Bytes * 3 / 4,
            $"Repeated per-match decision scans remain: product={actual.Bytes}, old={old.Bytes}, candidate={candidate.Bytes} bytes.");
    }

    [Fact]
    public void OriginalScopeCasingRevocationTieHeldAndWaiverSemanticsAreExact()
    {
        var facts = new[] { "tie", "revoke", "held", "normal", "review", "personal", "scope", "missing", "CASE", "tie" }
            .Select(Fact).ToArray();
        var input = new Input([
            new("first", 10, "normal", ["tie"]), new("second", 10, "confirmed", ["TIE"]),
            new("OLD", 3, "confirmed", ["revoke"]), new("new", 4, "system-error", ["REVOKE"]),
            new("undo", 5, "revoked", ["REVOKE"], RevokesDecisionId: "NEW"),
            new("undo-old", 6, "revoked", [], "account", "old"),
            new("allow", 2, "insufficient", ["normal"]), new("review", 2, "review", ["review"]),
            new("person", 100, "confirmed", ["personal"], "account"),
            new("wrong-case-scope", 100, "confirmed", ["scope"], "Match"),
            new("multiple", 1, "confirmed", ["case", "CaSe", "not-in-facts"])
        ], ["HELD", "normal", "review"], ["waived-outside-facts"]);
        var expected = new[] { "held", "review", "case", "waived-outside-facts" };
        Equal(expected, OldProjection(input, facts));
        Equal(expected, RunIsolated(input, facts));
        // Equal revision retains the first matching source row, not a dictionary overwrite.
        var reversed = input with { Decisions = input.Decisions.Reverse().ToArray() };
        Assert.Contains("tie", RunIsolated(reversed, facts));
        Equal(OldProjection(reversed, facts), RunIsolated(reversed, facts));
    }

    [Theory]
    [InlineData("confirmed", false, true)]
    [InlineData("system-error", false, true)]
    [InlineData("review", false, false)]
    [InlineData("review", true, true)]
    [InlineData("normal", true, false)]
    [InlineData("insufficient", true, false)]
    [InlineData("revoked", true, false)]
    public void LatestExplicitDispositionKeepsTheOriginalHeldRule(string disposition, bool held, bool excluded)
    {
        var input = new Input([new("d", 1, disposition, ["match"])], held ? ["MATCH"] : [], []);
        Assert.Equal(excluded, RunIsolated(input, [Fact("match")]).Contains("match"));
    }

    [Fact]
    public void EmptyAndSparseFactsDoNotBuildAnUnrelatedHistoricalMatchMap()
    {
        var input = new Input(Enumerable.Range(0, 128).Select(i =>
            new Decision($"d-{i}", i, "confirmed", Enumerable.Range(0, 8)
                .Select(j => $"unrelated-{i}-{j}").ToArray())).ToArray(), [], ["outside"]);
        foreach (var facts in new[] { Array.Empty<L12RankedMasterTitleMatchFact>(), new[] { Fact("absent") } })
        {
            var data = NewData();
            Populate(data, input);
            var actual = Measure(() => Production(data, facts), 5);
            var old = Measure(() => OldProjection(input, facts), 5);
            Equal(new[] { "outside" }, actual.Value);
            Print($"sparse_{facts.Length}_facts_product", actual);
            Print($"sparse_{facts.Length}_facts_old_oracle", old);
            Assert.True(actual.Bytes <= old.Bytes + 4096,
                "A sparse projection must not materialize the unrelated historical match universe.");
        }
    }

    [Fact]
    public void NoFactsOrAllWaivedNeverInspectDecisionMatchLists()
    {
        foreach (var facts in new[] { Array.Empty<L12RankedMasterTitleMatchFact>(), new[] { Fact("waived") } })
        {
            var data = NewData();
            Populate(data, new Input([new("unused", 1, "confirmed", ["unrelated"])], [], ["WAIVED", "outside"]));
            // Preserve the old helper's lazy boundary even for an unused malformed row.
            // This is not a new persistent-data admission rule (normal storage validation still rejects it).
            Set(Rows(data, "RankedIntegrityDecisions")[0]!, "MatchIds", null);
            Equal(new[] { "waived", "outside" }, Production(data, facts));
        }
    }

    [Fact]
    public void NewDecisionsAndRevocationsRefreshEachCommitAndFailedCandidatesNeverPublish()
    {
        using var f = new RankedIdentityFixture();
        var player = f.CreateAccount("titlefresh");
        var facts = Facts(2, player.Id, f.Admin.Id);
        f.Store.ImportRankedMasterTitleFacts(facts);
        var index = RankedValue(f.Store, "MasterTitleIndex");
        var decision = new Decision("fresh", 1, "confirmed", ["MATCH-0"]);
        Commit(f, () => AddDecision(Data(f.Store), decision));
        Assert.Contains("match-0", Exclusions(f.Store));
        Assert.DoesNotContain("match-1", Exclusions(f.Store));
        Assert.Same(index, RankedValue(f.Store, "MasterTitleIndex"));
        var bundle = RankedIdentityTestState.CommittedProjection(f.Store);
        var cache = RankedIdentityTestState.RollbackCache(f.Store);
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage == "after-ranked-identity-index-build") throw new IOException("synthetic exclusion candidate failure");
        };
        Assert.Throws<L12PlatformStorageUnavailableException>(() => Commit(f, () =>
            AddDecision(Data(f.Store), new("undo", 2, "revoked", ["match-0"], RevokesDecisionId: "FRESH"))));
        f.Store.StorageFailureInjector = null;
        Assert.Same(bundle, RankedIdentityTestState.CommittedProjection(f.Store));
        Assert.Same(cache, RankedIdentityTestState.RollbackCache(f.Store));
        Assert.Contains("match-0", Exclusions(f.Store));
        var restarted = new L12PlatformStore(f.Path, f.Catalog.PresetDecks, officialCards: f.Catalog.Cards);
        Assert.Contains("match-0", Exclusions(restarted));
        Commit(f, () => AddDecision(Data(f.Store), new("undo", 2, "revoked", ["match-0"], RevokesDecisionId: "FRESH")));
        Assert.Empty(Exclusions(f.Store));
        Assert.Same(index, RankedValue(f.Store, "MasterTitleIndex"));
    }

    [Fact]
    public void NullFactRowsRemainReadableAcrossCommitGallerySaveAndRestart()
    {
        using var f = new RankedIdentityFixture();
        var player = f.CreateAccount("titlenull");
        f.Store.ImportRankedMasterTitleFacts(Facts(2, player.Id, f.Admin.Id));
        // Normal import still rejects null IDs. Recovery historically accepts an existing
        // nullable fact, which title eligibility ignores; do not tighten that read boundary.
        Assert.Throws<ArgumentException>(() => f.Store.ImportRankedMasterTitleFacts([Fact(null!)]));
        Commit(f, () =>
        {
            var records = Rows(Data(f.Store), "RankedMasterRecords");
            foreach (var record in records.Cast<object>())
            {
                var facts = (List<L12RankedMasterTitleMatchFact>)record.GetType().GetProperty("TitleFacts")!.GetValue(record)!;
                for (var i = 0; i < facts.Count; i++)
                    if (facts[i].MatchId == "match-0") facts[i] = facts[i] with { MatchId = null! };
            }
            // Other committed projections already require nonnull decision keys. This
            // durable control isolates the title-fact boundary, not a new global contract.
            AddDecision(Data(f.Store), new("unrelated", 1, "normal", ["unrelated"]));
        });
        AssertHasNullFact(f.Store);
        Assert.Empty(Exclusions(f.Store));
        var identity = f.Store.RankedBattleIdentity(player.Id, 0);
        var titles = JsonSerializer.Serialize(f.Store.RankedMasterChampionsAt(DateTimeOffset.UtcNow));
        var saved = f.Store.SaveAlternateArtProduct(f.Admin,
            new L12AlternateArtProductDraft(null, "synthetic nullable title fact"),
            new L12AdminAuditContext("synthetic-title-null", "admin.content.draft"));
        Assert.Equal("synthetic nullable title fact", saved.Name);
        var restarted = new L12PlatformStore(f.Path, f.Catalog.PresetDecks, officialCards: f.Catalog.Cards);
        AssertHasNullFact(restarted);
        Assert.Empty(Exclusions(restarted));
        Assert.Equal(identity, restarted.RankedBattleIdentity(player.Id, 0));
        Assert.Equal(titles, JsonSerializer.Serialize(restarted.RankedMasterChampionsAt(DateTimeOffset.UtcNow)));

        static void AssertHasNullFact(L12PlatformStore store)
            => Assert.Contains(Rows(Data(store), "RankedMasterRecords").Cast<object>()
                .SelectMany(row => (List<L12RankedMasterTitleMatchFact>)row.GetType().GetProperty("TitleFacts")!.GetValue(row)!),
                fact => fact.MatchId is null);
    }

    [Theory]
    [InlineData("no-match", false)]
    [InlineData("confirmed", true)]
    [InlineData("held", true)]
    [InlineData("normal-held", false)]
    [InlineData("review-held", true)]
    [InlineData("tie-normal-first", false)]
    [InlineData("tie-confirmed-first", true)]
    [InlineData("revoked", false)]
    [InlineData("waived", true)]
    public void NullableFactAndDecisionKeysRetainOriginalNullMatching(string scenario, bool excluded)
    {
        Decision[] decisions = scenario switch
        {
            "no-match" => [new("different", 1, "confirmed", ["", " ", "other"])],
            "held" or "waived" => [],
            "normal-held" => [new("normal", 1, "normal", [null!])],
            "review-held" => [new("review", 1, "review", [null!])],
            "tie-normal-first" => [new("first", 2, "normal", [null!]), new("second", 2, "confirmed", [null!])],
            "tie-confirmed-first" => [new("first", 2, "confirmed", [null!]), new("second", 2, "normal", [null!])],
            "revoked" => [new("original", 1, "confirmed", [null!]),
                new("undo", 2, "revoked", [null!], RevokesDecisionId: "ORIGINAL")],
            _ => [new("confirmed", 1, "confirmed", [null!, "", " ", null!])],
        };
        var input = new Input(decisions,
            scenario is "held" or "normal-held" or "review-held" ? [null!] : [],
            scenario == "waived" ? [null!, "outside"] : []);
        var facts = new[] { Fact(null!), Fact(""), Fact(" "), Fact("no-match"), Fact(null!) };
        var old = OldProjection(input, facts);
        Assert.Equal(excluded, old.Contains(null!));
        Equal(old, RunIsolated(input, facts));
    }

    [Fact]
    public void DeterministicMixedHistoriesMatchTheOriginalAlgorithm()
    {
        var random = new Random(20261031);
        var facts = Facts(64, "first", "second").Concat([Fact("MATCH-0")]).ToArray();
        for (var round = 0; round < 12; round++)
        {
            var decisions = Enumerable.Range(0, 80).Select(i => new Decision($"d-{i}", random.Next(8),
                new[] { "normal", "confirmed", "review", "system-error", "insufficient", "revoked" }[random.Next(6)],
                Enumerable.Range(0, random.Next(5)).Select(_ => $"match-{random.Next(80)}").ToArray(),
                random.Next(4) == 0 ? "account" : "match", random.Next(3) == 0 ? $"D-{random.Next(80)}" : null)).ToArray();
            var input = new Input(decisions, ["match-0", "MATCH-7", "outside"], ["match-1", "waived-outside"]);
            Equal(OldProjection(input, facts), RunIsolated(input, facts));
        }
    }

    // Frozen copy of the pre-change algorithm; typed rows are captured outside the measured region.
    private static FrozenSet<string> OldProjection(Input input, IEnumerable<L12RankedMasterTitleMatchFact> facts)
    {
        var waived = input.Waived.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var revoked = input.Decisions.Where(row => row.Disposition == "revoked" && !string.IsNullOrWhiteSpace(row.RevokesDecisionId))
            .Select(row => row.RevokesDecisionId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var active = input.Decisions.Where(row => row.Scope == "match" && !revoked.Contains(row.Id)).ToArray();
        var held = input.Held.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(waived, StringComparer.OrdinalIgnoreCase);
        foreach (var match in facts.Select(row => row.MatchId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (excluded.Contains(match)) continue;
            var latest = active.Where(row => row.MatchIds.Contains(match, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(row => row.Revision).FirstOrDefault();
            var isHeld = held.Contains(match);
            if (latest is null ? isHeld : latest.Disposition is "confirmed" or "system-error"
                || latest.Disposition == "review" && isHeld) excluded.Add(match);
        }
        return excluded.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenSet<string> IndexedProjection(Input input, IEnumerable<L12RankedMasterTitleMatchFact> facts)
    {
        var waived = input.Waived.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = facts.Select(row => row.MatchId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var revoked = input.Decisions.Where(row => row.Disposition == "revoked" && !string.IsNullOrWhiteSpace(row.RevokesDecisionId))
            .Select(row => row.RevokesDecisionId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var latest = new Dictionary<string, Decision>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in input.Decisions.Where(row => row.Scope == "match" && !revoked.Contains(row.Id)))
            foreach (var match in row.MatchIds)
                if (matches.Contains(match) && !waived.Contains(match)
                    && (!latest.TryGetValue(match, out var previous) || row.Revision > previous.Revision)) latest[match] = row;
        var held = input.Held.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(waived, StringComparer.OrdinalIgnoreCase);
        foreach (var match in matches)
        {
            if (excluded.Contains(match)) continue;
            latest.TryGetValue(match, out var row);
            if (row is null ? held.Contains(match) : row.Disposition is "confirmed" or "system-error"
                || row.Disposition == "review" && held.Contains(match)) excluded.Add(match);
        }
        return excluded.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    private static object Data(L12PlatformStore store) => typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;
    private static object NewData() => Activator.CreateInstance(Project.GetParameters()[0].ParameterType, nonPublic: true)!;
    private static IList Rows(object data, string name) => (IList)data.GetType().GetProperty(name)!.GetValue(data)!;
    private static void Set(object row, string property, object? value) => row.GetType().GetProperty(property)!.SetValue(row, value);
    private static object NewRow(IList rows) => Activator.CreateInstance(rows.GetType().GetGenericArguments()[0], nonPublic: true)!;
    private static void AddDecision(object data, Decision decision)
    {
        var rows = Rows(data, "RankedIntegrityDecisions");
        var row = NewRow(rows);
        Set(row, "Id", decision.Id); Set(row, "Revision", decision.Revision); Set(row, "Disposition", decision.Disposition);
        Set(row, "Scope", decision.Scope); Set(row, "MatchIds", decision.MatchIds.ToList());
        Set(row, "RevokesDecisionId", decision.RevokesDecisionId);
        Set(row, "RequestId", $"synthetic-{decision.Id}");
        Set(row, "RequestFingerprint", new string('a', 64)); Set(row, "ActorId", "$system");
        Set(data, "RankedIntegrityRevision", Math.Max(decision.Revision,
            (long)data.GetType().GetProperty("RankedIntegrityRevision")!.GetValue(data)!));
        rows.Add(row);
    }
    private static void Populate(object data, Input input)
    {
        foreach (var decision in input.Decisions) AddDecision(data, decision);
        foreach (var match in input.Held)
        {
            var rows = Rows(data, "RankedHeldRewards"); var row = NewRow(rows); Set(row, "MatchId", match); rows.Add(row);
        }
        if (input.Waived.Length > 0)
        {
            var repairs = Rows(data, "RankedSeasonResetRepairs"); var repair = NewRow(repairs);
            Set(repair, "TransitionMatchIds", input.Waived.ToList()); repairs.Add(repair);
        }
    }
    private static IReadOnlySet<string> RunIsolated(Input input, L12RankedMasterTitleMatchFact[] facts)
    { var data = NewData(); Populate(data, input); return Production(data, facts); }
    private static IReadOnlySet<string> Production(object data, L12RankedMasterTitleMatchFact[] facts)
        => (IReadOnlySet<string>)Project.Invoke(null, [data, facts])!;
    private static object RankedValue(L12PlatformStore store, string property)
    { var ranked = RankedIdentityTestState.RankedProjection(store); return ranked.GetType().GetProperty(property)!.GetValue(ranked)!; }
    private static IReadOnlySet<string> Exclusions(L12PlatformStore store) => (IReadOnlySet<string>)RankedValue(store, "ExcludedMatchIds");
    private static void Commit(RankedIdentityFixture f, Action mutate)
        => f.Store.ExecuteAdminTransaction(() => { mutate(); RankedIdentityTestState.RequestSave(f.Store); return true; });
    private static L12RankedMasterTitleMatchFact Fact(string match) => new(match, "first", "second", "S01-01M1", "S01-02M1",
        0, 8, DateTimeOffset.UtcNow.AddMinutes(-1), "normal", true);
    private static L12RankedMasterTitleMatchFact[] Facts(int count, string first, string second)
        => Enumerable.Range(0, count).Select(i => Fact($"match-{i}") with { FirstAccountId = first, SecondAccountId = second }).ToArray();
    private static void Equal(IEnumerable<string> expected, IEnumerable<string> actual)
        => Assert.Equal(expected.Select(value => value?.ToUpperInvariant()).Order(StringComparer.Ordinal),
            actual.Select(value => value?.ToUpperInvariant()).Order(StringComparer.Ordinal));
    private static (T Value, long Bytes, double Ms) Measure<T>(Func<T> action, int count, bool warm = true)
    {
        if (warm) _ = action();
        var bytes = GC.GetAllocatedBytesForCurrentThread(); var tick = Stopwatch.GetTimestamp(); T value = default!;
        for (var i = 0; i < count; i++) value = action();
        return (value, GC.GetAllocatedBytesForCurrentThread() - bytes, Stopwatch.GetElapsedTime(tick).TotalMilliseconds);
    }
    private void Print<T>(string label, (T Value, long Bytes, double Ms) measurement)
        => output.WriteLine("{0} allocated_bytes={1} elapsed_ms={2:F3}", label, measurement.Bytes, measurement.Ms);
}
