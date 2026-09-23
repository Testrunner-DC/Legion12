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
        Assert.Equal(686, matrix.AbilityTotal);
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
    public void AbilitySpecificNotApplicableDoesNotHideSiblingEvidenceGaps()
    {
        var abilities = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities).Take(2)
            .OrderByDescending(ability => ability.AbilityId, StringComparer.Ordinal).ToArray();
        var specific = new L12LifecycleProfile("test:ability-specific",
            new SortedDictionary<string, string>(StringComparer.Ordinal),
            new SortedDictionary<string, string>(StringComparer.Ordinal))
        {
            AbilityNotApplicable = new SortedDictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                [abilities[0].AbilityId] = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["no-target"] = "首段由固定规则对象承接，没有无对象分支。",
                },
            },
        };
        var rows = abilities.Select(ability => new EffectLifecycleInventoryTests.AbilityRow(
            ability.CardId, "合成", ability, "shared-rule-owner", [], ["no-target"], [], specific)).ToArray();
        var profile = Assert.Single(EffectLifecycleCompletionMatrix.Build(
            new EffectLifecycleInventoryTests.Inventory(0, 1, [], rows)).Profiles);

        var exemptIndex = Array.IndexOf(profile.AbilityIds, abilities[0].AbilityId);
        var siblingIndex = Array.IndexOf(profile.AbilityIds, abilities[1].AbilityId);
        Assert.Equal(MatrixItemStatus.NotApplicable,
            profile.CellsByItem["no-target"][exemptIndex].Status);
        Assert.Equal(MatrixItemStatus.Missing,
            profile.CellsByItem["no-target"][siblingIndex].Status);
    }

    [Fact]
    public void SharedProtocolEvidenceCoversOnlyAbilitiesInsideTheSameProfile()
    {
        var abilities = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities).Take(3).ToArray();
        var sharedProfile = new L12LifecycleProfile("test:shared-protocol",
            new SortedDictionary<string, string>(StringComparer.Ordinal),
            new SortedDictionary<string, string>(StringComparer.Ordinal))
        {
            SharedProtocolScopes = ["normal"],
        };
        var unrelatedProfile = new L12LifecycleProfile("test:unrelated-protocol",
            new SortedDictionary<string, string>(StringComparer.Ordinal),
            new SortedDictionary<string, string>(StringComparer.Ordinal))
        {
            SharedProtocolScopes = ["normal"],
        };
        var representative = new L12AbilityTestReference(abilities[0].AbilityId,
            "Synthetic.SharedProtocolRepresentative", abilities[0].CardId, ["normal"]);
        var rows = new[]
        {
            new EffectLifecycleInventoryTests.AbilityRow(abilities[0].CardId, "代表能力", abilities[0],
                "shared-rule-owner", [], ["normal"], [representative], sharedProfile),
            new EffectLifecycleInventoryTests.AbilityRow(abilities[1].CardId, "同族能力", abilities[1],
                "shared-rule-owner", [], ["normal"], [], sharedProfile),
            new EffectLifecycleInventoryTests.AbilityRow(abilities[2].CardId, "异族能力", abilities[2],
                "shared-rule-owner", [], ["normal"], [], unrelatedProfile),
        };
        var profiles = EffectLifecycleCompletionMatrix.Build(
            new EffectLifecycleInventoryTests.Inventory(0, 3, [], rows)).Profiles;

        var shared = Assert.Single(profiles, profile => profile.ProfileId == sharedProfile.Id);
        Assert.All(shared.CellsByItem["normal"],
            cell => Assert.Equal(MatrixItemStatus.Evidenced, cell.Status));
        var unrelated = Assert.Single(profiles, profile => profile.ProfileId == unrelatedProfile.Id);
        Assert.Equal(MatrixItemStatus.Missing, unrelated.CellsByItem["normal"][0].Status);
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
