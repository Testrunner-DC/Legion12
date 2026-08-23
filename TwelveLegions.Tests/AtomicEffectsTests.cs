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
            Assert.Contains("verified-runtime-program", ability.MappingSource, StringComparison.Ordinal);
            Assert.Equal(program.Atoms, ability.Atoms);
        }
        Assert.True(catalog.AtomicEffects.Coverage().VerifiedAbilities >= L12VerifiedAtomicPrograms.All.Count);
    }

    [Fact]
    public void AbilityIdentitySurvivesAbilityReorderingButInvalidatesChangedStructure()
    {
        var original = new L12AtomicAbility("legacy", "TEST", 1, "登场时 可抽取1张牌。", "enter",
            [Atom("a1", L12AtomKinds.Trigger, 1), Atom("a2", L12AtomKinds.Draw, 2)],
            "declarative-ready", false, "test", 1m, "triggered");
        var beforeReorder = L12AtomicAbilityIdentity.Assign("TEST", original, 1);
        var afterReorder = L12AtomicAbilityIdentity.Assign("TEST", original with { Sequence = 4 }, 4);
        var changed = L12AtomicAbilityIdentity.Assign("TEST", original with
        {
            Atoms = [Atom("a1", L12AtomKinds.Trigger, 1), Atom("a2", L12AtomKinds.Draw, 2), Atom("a3", L12AtomKinds.HealMaster, 3)],
        }, 1);

        Assert.Equal(beforeReorder.AbilityId, afterReorder.AbilityId);
        Assert.Equal(beforeReorder.StructureHash, afterReorder.StructureHash);
        Assert.NotEqual(beforeReorder.AbilityId, changed.AbilityId);
        Assert.NotEqual(beforeReorder.StructureHash, changed.StructureHash);
        Assert.Equal("TEST:ability:4", afterReorder.LegacyAbilityId);
    }

    [Theory]
    [InlineData("S02-0511")]
    [InlineData("S02-0517")]
    public void CanAttackLegionsOnSummonProgramsAreVerifiedAndDoNotFallBackToLegacy(string cardId)
    {
        var program = Assert.IsType<L12VerifiedAtomicProgram>(L12VerifiedAtomicPrograms.Find(cardId, "enter"));
        var state = Assert.Single(program.Atoms, atom => atom.Kind == L12AtomKinds.SetState);
        Assert.Equal("source.canAttackLegionsOnSummonUntilTurn", state.Parameters["key"]);
        Assert.Equal("current-turn", state.Parameters["value"]);

        var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find(cardId));
        var ability = Assert.Single(card.Abilities, candidate => candidate.Trigger == "enter");
        Assert.Equal("verified", ability.MigrationStatus);
        Assert.False(ability.HasLegacyFallback);
    }

    [Theory]
    [InlineData("S02-0505", "enter", L12AtomKinds.Keyword, "charge")]
    [InlineData("S02-0509", "enter", L12AtomKinds.SetState, "controller.freeTacticCount")]
    public void SecondOlympusBatchUsesVerifiedRuntimePrograms(
        string cardId, string trigger, string expectedKind, string expectedValue)
    {
        var program = Assert.IsType<L12VerifiedAtomicProgram>(L12VerifiedAtomicPrograms.Find(cardId, trigger));
        var operation = Assert.Single(program.Atoms, atom => atom.Kind == expectedKind);
        Assert.DoesNotContain(program.Atoms, atom => atom.Kind == L12AtomKinds.Legacy);

        if (expectedKind == L12AtomKinds.Keyword)
            Assert.Equal(expectedValue, operation.Parameters["keyword"]);
        else if (expectedKind == L12AtomKinds.SetState)
            Assert.Equal(expectedValue, operation.Parameters["key"]);
        else
            Assert.Equal(expectedValue, operation.Parameters["amount"]);

        var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find(cardId));
        var ability = Assert.Single(card.Abilities, candidate => candidate.Trigger == trigger);
        Assert.Equal("verified", ability.MigrationStatus);
        Assert.False(ability.HasLegacyFallback);
        Assert.Equal(program.Atoms, ability.Atoms);
    }

    [Fact]
    public void OptionalAeneasDeathDrawIsNotMisreportedAsUnconditionalVerifiedRuntime()
    {
        Assert.Null(L12VerifiedAtomicPrograms.Find("S02-0512", "death"));
        var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S02-0512"));
        var ability = Assert.Single(card.Abilities, candidate => candidate.Trigger == "death");
        Assert.True(ability.HasLegacyFallback);
        Assert.NotEqual("verified", ability.MigrationStatus);
        Assert.Contains(ability.Atoms, atom => atom.Kind == L12AtomKinds.Optional);
    }

    [Fact]
    public void AbilitySplitterSeparatesIndependentTimingWindows()
    {
        var wuZetian = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S01-0102"));
        Assert.Contains(wuZetian.Abilities, ability => ability.Trigger == "enter");
        Assert.Contains(wuZetian.Abilities, ability => ability.Trigger == "death" && ability.MigrationStatus == "verified");

        var yoshitsune = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S01-0409"));
        Assert.Contains(yoshitsune.Abilities, ability => ability.Trigger == "attack"
            && ability.MigrationStatus == "partially-atomized"
            && ability.HasLegacyFallback);
        Assert.Contains(yoshitsune.Abilities, ability => ability.Trigger == "after-attack" && ability.MigrationStatus == "verified");
    }

    [Fact]
    public void StructuredCombatCardsExposeExecutionModelAndAtomicStages()
    {
        var atalanta = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S02-0507"));
        Assert.Equal(5, atalanta.Abilities.Count);
        Assert.Contains(atalanta.Abilities, ability => ability.ExecutionModel == "summon-flow"
            && ability.Atoms.Any(atom => atom.Stage == "cost")
            && ability.Atoms.Any(atom => atom.Stage == "target"));
        Assert.Contains(atalanta.Abilities, ability => ability.ExecutionModel == "continuous"
            && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.SetState
                && atom.Parameters.GetValueOrDefault("value") == "弓手"));
        Assert.Contains(atalanta.Abilities, ability => ability.Trigger == "attack"
            && ability.Atoms.Any(atom => atom.Stage == "duration"
                && atom.Parameters.GetValueOrDefault("duration") == "current-attack"));

        var yoshitsune = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S01-0409"));
        Assert.Contains(yoshitsune.Abilities, ability => ability.ExecutionModel == "continuous");
        Assert.Contains(yoshitsune.Abilities, ability => ability.ExecutionModel == "activated");
        Assert.Contains(yoshitsune.Abilities, ability => ability.Trigger == "after-attack"
            && ability.MappingSource.Contains("verified-runtime-program", StringComparison.Ordinal));
    }

    [Fact]
    public void UserReviewedOlympusCardsExposeReviewMarkersAndRequestedAbilityBoundaries()
    {
        var assisted = new[] { "S02-0501", "S02-0503", "S02-0504", "S02-0505", "S02-0507", "S02-0508", "S02-0509", "S02-0510", "S02-0511", "S02-0512", "S02-0513", "S02-0514", "S02-0515", "S02-0516", "S02-0517", "S02-0518", "S02-0519", "S02-0520", "S02-0522", "S02-0523", "S02-05M1" };
        Assert.All(assisted, cardId =>
        {
            var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find(cardId));
            Assert.Equal("human-assisted", card.ReviewStatus);
            Assert.All(card.Abilities, ability =>
            {
                Assert.Equal("human-assisted", ability.ReviewStatus);
                Assert.Equal("user-20260823", ability.ReviewSource);
            });
        });

        Assert.All(new[] { "S02-0502", "S02-0506", "S02-0521", "S02-05M2", "S02-05C1", "S02-05C1A" }, cardId =>
        {
            var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find(cardId));
            Assert.Equal("confirmed", card.ReviewStatus);
            Assert.All(card.Abilities, ability => Assert.Equal("confirmed", ability.ReviewStatus));
        });

        var promotedAchilles = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S02-0503"));
        Assert.Contains(promotedAchilles.Abilities, ability => ability.ExecutionModel == "replacement" || ability.ExecutionModel == "granted-continuous");
        Assert.Contains(promotedAchilles.Abilities, ability => ability.ExecutionModel == "keyword-definition"
            && ability.Atoms.Any(atom => atom.Parameters.GetValueOrDefault("keywordRef") == "taunt"));

        var hippolyta = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S02-0510"));
        Assert.Contains(hippolyta.Abilities.SelectMany(ability => ability.Atoms), atom =>
            atom.Parameters.GetValueOrDefault("operation") == "enable-free-front-back-move"
            && atom.Parameters.GetValueOrDefault("button") == "免费位移");
        var forge = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S02-0520"));
        Assert.Equal(4, forge.Abilities.Count);
        Assert.Contains(forge.Abilities, ability => ability.ExecutionModel == "activated"
            && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.SelectMode));

        var artemis = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S02-05M1"));
        Assert.Equal(4, artemis.Abilities.Count);
        Assert.Contains(artemis.Abilities.SelectMany(ability => ability.Atoms), atom =>
            atom.Parameters.GetValueOrDefault("keywordRef") == "strong-attack");
        Assert.Contains(artemis.Abilities.SelectMany(ability => ability.Atoms), atom =>
            atom.Parameters.GetValueOrDefault("keywordRef") == "shock");

        var prometheus = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find("S02-05M2"));
        Assert.Contains(prometheus.Abilities.SelectMany(ability => ability.Atoms), atom =>
            atom.Parameters.GetValueOrDefault("operation") == "consume");
    }

    [Fact]
    public void RevealAtomsRequireOpponentConfirmationAndPublicCardLog()
    {
        foreach (var cardId in new[] { "S02-0501", "S02-0509", "S02-0514", "S02-0518", "S02-0521", "S02-05M2" })
        {
            var card = Assert.IsType<L12AtomicCardEffect>(Catalog.AtomicEffects.Find(cardId));
            var revealAtoms = card.Abilities.SelectMany(ability => ability.Atoms)
                .Where(atom => atom.Kind == L12AtomKinds.Visibility).ToArray();
            Assert.NotEmpty(revealAtoms);
            Assert.All(revealAtoms, atom =>
            {
                Assert.Equal("both-players", atom.Parameters.GetValueOrDefault("visibility"));
                Assert.Equal("required", atom.Parameters.GetValueOrDefault("opponentConfirmation"));
                Assert.Equal("public-card-link", atom.Parameters.GetValueOrDefault("log"));
            });
        }
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
