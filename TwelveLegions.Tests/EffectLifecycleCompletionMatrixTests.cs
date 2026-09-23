using System.Runtime.CompilerServices;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// 生命周期完成矩阵的生成与守卫。矩阵只读台账与证据目录，不写运行时。
public sealed class EffectLifecycleCompletionMatrixTests
{
    private static readonly string MatrixPath = Path.Combine(
        SourceRoot(), "docs", "l12", "EFFECT-LIFECYCLE-COMPLETION-MATRIX.md");

    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static CompletionMatrix BuildMatrix()
        => EffectLifecycleCompletionMatrix.Build(EffectLifecycleInventoryTests.Build(Catalog));

    [Fact]
    public void MatrixGenerationIsDeterministic()
    {
        var first = EffectLifecycleCompletionMatrix.Render(BuildMatrix());
        var second = EffectLifecycleCompletionMatrix.Render(BuildMatrix());
        Assert.Equal(first, second);
        Assert.Equal(BuildMatrix().Fingerprint, BuildMatrix().Fingerprint);
    }

    [Fact]
    public void EveryAbilitySegmentIsInsideTheDenominator()
    {
        var inventory = EffectLifecycleInventoryTests.Build(Catalog);
        var matrix = BuildMatrix();
        Assert.Equal(inventory.Abilities.Length, matrix.AbilityTotal);
        Assert.Equal(681, matrix.AbilityTotal);
        // 分母 = 档案绑定段 + 各未归属桶；互不重叠、合计全覆盖。
        var bound = matrix.Profiles.SelectMany(profile => profile.AbilityIds).ToArray();
        Assert.Equal(bound.Length, bound.Distinct(StringComparer.Ordinal).Count());
        var unbound = inventory.Abilities.Where(row => row.Profile is null)
            .Select(row => row.Definition.AbilityId).ToArray();
        Assert.Equal(matrix.AbilityTotal, bound.Length + unbound.Length);
        Assert.Empty(bound.Intersect(unbound, StringComparer.Ordinal));
        Assert.Equal(matrix.EntryEvidenceBuckets.Values.Sum(), matrix.AbilityTotal);
    }

    [Fact]
    public void NotApplicableAlwaysCarriesAReasonAndEvidenceRequiresExactScopes()
    {
        var matrix = BuildMatrix();
        foreach (var profile in matrix.Profiles)
        foreach (var (item, cells) in profile.CellsByItem)
        {
            Assert.All(cells.Where(cell => cell.Status == MatrixItemStatus.NotApplicable),
                cell => Assert.False(string.IsNullOrWhiteSpace(cell.Reason),
                    $"{profile.ProfileId}/{item} 的不适用必须给出理由"));
            Assert.All(cells.Where(cell => cell.Status == MatrixItemStatus.Evidenced),
                cell => Assert.False(string.IsNullOrWhiteSpace(cell.Reason),
                    $"{profile.ProfileId}/{item} 的证据必须指向具名测试方法"));
        }
    }

    [Fact]
    public void MissingScopeBlocksProfileCompletion()
    {
        // 合成行：有缺口、无证据的档案不得标为已完成。
        var ability = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .First(item => item.AbilityId == EffectLifecycleProfiles.DesertHandSummonAbilityId);
        var syntheticProfile = new L12LifecycleProfile("test:synthetic",
            new SortedDictionary<string, string>(StringComparer.Ordinal),
            new SortedDictionary<string, string>(StringComparer.Ordinal));
        var row = new EffectLifecycleInventoryTests.AbilityRow(ability.CardId, "合成", ability,
            "shared-rule-owner", [], ["normal", "reconnect"], [], syntheticProfile);
        var inventory = new EffectLifecycleInventoryTests.Inventory(0, 1, [], [row]);
        var matrix = EffectLifecycleCompletionMatrix.Build(inventory);
        var profile = Assert.Single(matrix.Profiles);
        Assert.False(profile.Complete);
        Assert.Equal(MatrixItemStatus.Missing, profile.CellsByItem["normal"][0].Status);

        // 结构性不适用：无费用原子的段，payment-cancel 自动不适用且带理由。
        var costFree = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .First(item => item.Trigger == "keyword-definition");
        var costFreeRow = new EffectLifecycleInventoryTests.AbilityRow(costFree.CardId, "合成",
            costFree, "shared-rule-owner", [], [], [], syntheticProfile);
        var costFreeMatrix = EffectLifecycleCompletionMatrix.Build(
            new EffectLifecycleInventoryTests.Inventory(0, 1, [], [costFreeRow]));
        var cell = costFreeMatrix.Profiles[0].CellsByItem["payment-cancel"][0];
        Assert.Equal(MatrixItemStatus.NotApplicable, cell.Status);
        Assert.False(string.IsNullOrWhiteSpace(cell.Reason));
    }

    [Fact]
    public void CommittedMatrixMatchesRuntimeDefinitions()
    {
        var rendered = EffectLifecycleCompletionMatrix.Render(BuildMatrix());
        if (Environment.GetEnvironmentVariable("L12_UPDATE_COMPLETION_MATRIX") == "1")
        {
            File.WriteAllText(MatrixPath, rendered);
            return;
        }
        Assert.True(File.Exists(MatrixPath),
            "完成矩阵缺失；以 L12_UPDATE_COMPLETION_MATRIX=1 重新生成。");
        Assert.Equal(File.ReadAllText(MatrixPath).Replace("\r\n", "\n", StringComparison.Ordinal), rendered);
    }

    private static string SourceRoot([CallerFilePath] string sourcePath = "")
        => Directory.GetParent(Path.GetDirectoryName(sourcePath)!)!.FullName;
}
