using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicEffectsTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void EveryCatalogCardHasAuditableAtomicComposition()
    {
        var catalog = Catalog;
        Assert.Equal(catalog.Cards.Count, catalog.AtomicEffects.All.Count);
        foreach (var definition in catalog.Cards.Values)
        {
            var mapped = catalog.AtomicEffects.Find(definition.Id);
            Assert.NotNull(mapped);
            Assert.Equal(definition.Effect?.Trim() ?? string.Empty, mapped.EffectText);
            if (string.IsNullOrWhiteSpace(definition.Effect)) continue;
            Assert.NotEmpty(mapped.Abilities);
            foreach (var ability in mapped.Abilities)
            {
                Assert.Equal(L12AtomKinds.Trigger, ability.Atoms[0].Kind);
                Assert.Equal(Enumerable.Range(1, ability.Atoms.Count), ability.Atoms.Select(atom => atom.Order));
                Assert.Equal(ability.HasLegacyFallback, ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Legacy));
            }
        }
    }

    [Fact]
    public void AtomKindsAreAllRegisteredAndCoverageIsInternallyConsistent()
    {
        var catalog = Catalog;
        var registered = L12EffectAtomRegistry.All.Select(atom => atom.Kind).ToHashSet(StringComparer.Ordinal);
        var used = catalog.AtomicEffects.All.SelectMany(card => card.Abilities).SelectMany(ability => ability.Atoms).ToArray();
        Assert.All(used, atom => Assert.Contains(atom.Kind, registered));
        var coverage = catalog.AtomicEffects.Coverage();
        Assert.Equal(catalog.Cards.Count, coverage.TotalCards);
        Assert.Equal(used.Length, coverage.TotalAtoms);
        Assert.Equal(coverage.TotalAbilities, coverage.DeclarativeReadyAbilities + coverage.VerifiedAbilities + coverage.LegacyBackedAbilities);
        Assert.Equal(used.Length, coverage.ByAtomKind.Values.Sum());
    }

    [Fact]
    public void LegacyFallbackIsAnExplicitSingleExecutionBoundary()
    {
        var ability = new L12AtomicAbility("test", "TEST", 1, "测试", "主动",
        [
            Atom("a1", L12AtomKinds.Trigger, 1),
            Atom("a2", L12AtomKinds.Draw, 2),
            Atom("a3", L12AtomKinds.Legacy, 3),
            Atom("a4", L12AtomKinds.HealMaster, 4),
        ], "partially-atomized", true, "test", 1m);
        var runtime = new RecordingRuntime();
        var result = new L12AtomicEffectInterpreter().Execute(ability, runtime);
        Assert.False(result.Completed);
        Assert.True(result.UsedLegacyFallback);
        Assert.Equal(2, result.NextAtomIndex);
        Assert.Equal([L12AtomKinds.Trigger, L12AtomKinds.Draw], runtime.Executed);
    }

    [Fact]
    public void SelectionAtomPausesBeforeMutatingLaterAtoms()
    {
        var ability = new L12AtomicAbility("test", "TEST", 1, "测试", "主动",
        [Atom("a1", L12AtomKinds.Trigger, 1), Atom("a2", L12AtomKinds.SelectTarget, 2), Atom("a3", L12AtomKinds.Draw, 3)],
            "declarative-ready", false, "test", 1m);
        var runtime = new RecordingRuntime();
        var result = new L12AtomicEffectInterpreter().Execute(ability, runtime);
        Assert.True(result.NeedsInput);
        Assert.Equal(1, result.NextAtomIndex);
        Assert.Equal([L12AtomKinds.Trigger], runtime.Executed);
    }

    [Fact]
    public void AdminQuerySupportsStatusProductAndAtomFilters()
    {
        var effects = Catalog.AtomicEffects;
        var result = effects.Query("", "partially-atomized", "S02", L12AtomKinds.Special, 1, 200);
        Assert.All(result.Items, card =>
        {
            Assert.Equal("S02", card.Product);
            Assert.Equal("partially-atomized", card.MigrationStatus);
            Assert.Contains(L12AtomKinds.Special, card.AtomKinds);
        });
    }

    [Fact]
    public void VerifiedProgramsAreTheSameDefinitionsShownByAdmin()
    {
        var catalog = Catalog;
        Assert.NotEmpty(L12VerifiedAtomicPrograms.All);
        foreach (var program in L12VerifiedAtomicPrograms.All)
        {
            Assert.DoesNotContain(program.Atoms, atom => atom.Kind == L12AtomKinds.Legacy);
            Assert.All(program.Atoms, atom => Assert.True(atom.RuntimeExecutable));
            var card = Assert.IsType<L12AtomicCardEffect>(catalog.AtomicEffects.Find(program.CardId));
            var ability = Assert.Single(card.Abilities, candidate => candidate.Trigger == program.Trigger);
            Assert.Equal("verified", ability.MigrationStatus);
            Assert.Equal("verified-runtime-program", ability.MappingSource);
            Assert.Equal(program.Atoms, ability.Atoms);
        }
        Assert.Equal(L12VerifiedAtomicPrograms.All.Count,
            catalog.AtomicEffects.Coverage().VerifiedAbilities);
    }

    [Fact]
    public void AbilitySplitterSeparatesIndependentTimingWindows()
    {
        var wuZetian = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S01-0102"));
        Assert.Contains(wuZetian.Abilities, ability => ability.Trigger == "enter");
        Assert.Contains(wuZetian.Abilities, ability => ability.Trigger == "death" && ability.MigrationStatus == "verified");

        var yoshitsune = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S01-0409"));
        Assert.Contains(yoshitsune.Abilities, ability => ability.Trigger == "attack" && ability.MigrationStatus == "verified");
        Assert.Contains(yoshitsune.Abilities, ability => ability.Trigger == "after-attack" && ability.MigrationStatus == "verified");
    }

    private static L12EffectAtom Atom(string id, string kind, int order)
        => new(id, kind, kind, order, new Dictionary<string, string>(), L12EffectAtomRegistry.Get(kind).RuntimeExecutable, "test");

    private sealed class RecordingRuntime : IL12AtomicRuntime
    {
        public List<string> Executed { get; } = [];
        public bool Check(string expression) => true;
        public void Execute(L12EffectAtom atom) => Executed.Add(atom.Kind);
    }
}
