using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TwelveLegions.Server;

namespace TwelveLegions.Tests;

// 生命周期完成矩阵（离线审计，机器可读）。规则：
// - 只认精确能力 ID 绑定的具名证据 scope（完全匹配或以矩阵项为前缀的 "item-sub" 形式）；
// - NotApplicable 必须有理由；结构性不适用（无费用原子/无对象选择原子）自动生成理由；
// - 档案归属、运行入口、测试证据分别展示，互不冒充；
// - 全部能力段进入分母；生成器确定性输出。

internal enum MatrixItemStatus { Evidenced, Missing, NotApplicable }

internal sealed record MatrixItemCell(MatrixItemStatus Status, string Reason);

internal sealed record ProfileMatrixRow(
    string ProfileId,
    IReadOnlyDictionary<string, string> RuntimeOwners,
    IReadOnlyList<string> AdditionalChecks,
    string[] AbilityIds,
    Dictionary<string, MatrixItemCell[]> CellsByItem,
    bool Complete);

internal sealed record CompletionMatrix(
    int Schema,
    int AbilityTotal,
    int ProfileCount,
    int CompleteProfileCount,
    IReadOnlyDictionary<string, int> EntryEvidenceBuckets,
    ProfileMatrixRow[] Profiles,
    string Fingerprint);

internal static class EffectLifecycleCompletionMatrix
{
    // 最低矩阵项（与交接文档第 3.1 节一一对应）。
    internal static readonly string[] Items =
    [
        "normal", "no-target", "negated", "target-invalidated", "duplicate-submit",
        "reconnect", "payment-cancel", "single-candidate-choice", "multi-target-applicability",
        "presentation-consumers",
    ];

    internal static readonly IReadOnlyDictionary<string, string> ItemLabels =
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["normal"] = "正常结算",
            ["no-target"] = "无目标/不能发动",
            ["negated"] = "已支付后被无效",
            ["target-invalidated"] = "已声明对象逆结算失效",
            ["duplicate-submit"] = "重复或过期提交",
            ["reconnect"] = "Prompt/堆叠/选择阶段重连",
            ["payment-cancel"] = "有费用时的取消/支付失败兜底",
            ["single-candidate-choice"] = "有对象选择时的唯一候选仍选择",
            ["multi-target-applicability"] = "多目标协议的部分失效继续",
            ["presentation-consumers"] = "展示消费者覆盖（按钮/弹框/动效/日志/战报/回放）",
        };

    private static bool ScopeCovers(string item, string scope)
        => scope == item || scope.StartsWith(item + "-", StringComparison.Ordinal);

    private static MatrixItemCell Cell(EffectLifecycleInventoryTests.AbilityRow row, string item)
    {
        var profile = row.Profile!;
        if (profile.NotApplicable.TryGetValue(item, out var reason))
            return new(MatrixItemStatus.NotApplicable, reason);
        var hasCost = row.Definition.Atoms.Any(atom => atom.Stage == "cost");
        var targetMax = row.Definition.Atoms
            .Where(atom => atom.Kind == L12AtomKinds.SelectTarget)
            .Select(atom => int.TryParse(atom.Parameters.GetValueOrDefault("max"), out var max) ? max : 1)
            .DefaultIfEmpty(0).Max();
        if (item == "payment-cancel" && !hasCost)
            return new(MatrixItemStatus.NotApplicable, "本段没有费用原子，结构性不适用。");
        if (item == "single-candidate-choice" && targetMax == 0)
            return new(MatrixItemStatus.NotApplicable, "本段没有对象选择原子，结构性不适用。");
        if (item == "multi-target-applicability" && targetMax <= 1)
            return new(MatrixItemStatus.NotApplicable, "本段最多选择1个对象，结构性不适用。");
        if (row.TestReferences.Any(reference => reference.Scopes.Any(scope => ScopeCovers(item, scope))))
            return new(MatrixItemStatus.Evidenced, string.Join(", ",
                row.TestReferences.Where(reference => reference.Scopes.Any(scope => ScopeCovers(item, scope)))
                    .Select(reference => reference.TestMethod).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)));
        return new(MatrixItemStatus.Missing, "");
    }

    internal static CompletionMatrix Build(EffectLifecycleInventoryTests.Inventory inventory)
    {
        var profileRows = inventory.Abilities.Where(row => row.Profile is not null)
            .GroupBy(row => row.Profile!.Id).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var profile = group.First().Profile!;
                var abilityIds = group.Select(row => row.Definition.AbilityId)
                    .Order(StringComparer.Ordinal).ToArray();
                var cells = Items.ToDictionary(item => item,
                    item => group.Select(row => Cell(row, item)).ToArray(), StringComparer.Ordinal);
                var complete = cells.Values.All(column => column.All(cell => cell.Status != MatrixItemStatus.Missing));
                return new ProfileMatrixRow(profile.Id, profile.RuntimeOwners, profile.AdditionalChecks,
                    abilityIds, cells, complete);
            }).ToArray();
        var buckets = inventory.Abilities.GroupBy(row => row.EntryEvidence, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var matrix = new CompletionMatrix(1, inventory.Abilities.Length, profileRows.Length,
            profileRows.Count(row => row.Complete), buckets, profileRows, "");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(matrix with { Fingerprint = "" }, JsonOptions)))).ToLowerInvariant();
        return matrix with { Fingerprint = fingerprint };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static string Render(CompletionMatrix matrix)
    {
        var text = new StringBuilder();
        text.AppendLine("# 生命周期完成矩阵（自动基线）").AppendLine();
        text.AppendLine("由 `scripts/export-l12-effect-lifecycle-inventory.ps1` 随台账同次生成；不要手工修改此表。");
        text.AppendLine("只认精确能力 ID 绑定的具名证据 scope（完全匹配或 `矩阵项-子项` 前缀形式）；不适用必须给出理由；归属、运行入口与测试证据分别展示。");
        text.AppendLine($"能力段分母：{matrix.AbilityTotal}（含未归属段；档案外段不构成完成证据）。档案：{matrix.ProfileCount}，已完成：{matrix.CompleteProfileCount}，未完成：{matrix.ProfileCount - matrix.CompleteProfileCount}。");
        text.AppendLine($"内容指纹：`{matrix.Fingerprint}`。").AppendLine();
        text.AppendLine("| 定义证据分桶 | 能力数 |").AppendLine("| --- | ---: |");
        foreach (var bucket in matrix.EntryEvidenceBuckets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            text.AppendLine($"| {bucket.Key} | {bucket.Value} |");
        text.AppendLine();
        text.AppendLine("## 矩阵项").AppendLine();
        foreach (var item in EffectLifecycleCompletionMatrix.Items)
            text.AppendLine($"- `{item}`：{ItemLabels[item]}");
        text.AppendLine();
        foreach (var profile in matrix.Profiles)
        {
            text.AppendLine($"## {profile.ProfileId}（{(profile.Complete ? "已完成" : "未完成")}）").AppendLine();
            text.AppendLine($"绑定能力段：{profile.AbilityIds.Length}。运行入口："
                + string.Join("；", profile.RuntimeOwners.Select(owner => $"{owner.Key} = "
                    + (owner.Value.Contains('.') ? owner.Value : $"L12GameEngine.{owner.Value}"))) + "。");
            if (profile.AdditionalChecks.Count > 0)
                text.AppendLine($"档案附加检查：{string.Join(", ", profile.AdditionalChecks)}。");
            text.AppendLine();
            text.AppendLine("| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |").AppendLine("| --- | --- | --- | --- |");
            foreach (var item in Items)
            {
                var cells = profile.CellsByItem[item];
                var evidenced = cells.Where(cell => cell.Status == MatrixItemStatus.Evidenced).ToArray();
                var missingIds = profile.AbilityIds.Where((_, index) => cells[index].Status == MatrixItemStatus.Missing)
                    .Select(Escape).ToArray();
                var notApplicable = cells.Where(cell => cell.Status == MatrixItemStatus.NotApplicable).ToArray();
                text.AppendLine($"| {item} | {evidenced.Length} | {missingIds.Length}"
                    + (missingIds.Length > 0 ? $"（{string.Join("、", missingIds)}）" : "")
                    + $" | {notApplicable.Length} |");
            }
            text.AppendLine();
            var missingDetails = Items
                .Select(item => (Item: item, Cells: profile.CellsByItem[item]))
                .Where(entry => entry.Cells.Any(cell => cell.Status == MatrixItemStatus.Missing))
                .ToArray();
            if (missingDetails.Length > 0)
            {
                text.AppendLine("缺失明细与建议补测范围：");
                foreach (var (item, cells) in missingDetails)
                {
                    var ids = profile.AbilityIds.Where((_, index) => cells[index].Status == MatrixItemStatus.Missing)
                        .Select(Escape).ToArray();
                    text.AppendLine($"- `{item}`（{ItemLabels[item]}）：{string.Join("、", ids)}");
                }
                text.AppendLine();
            }
            var presentationOwner = profile.RuntimeOwners
                .Where(owner => owner.Key.Contains("presentation", StringComparison.Ordinal))
                .Select(owner => owner.Value).ToArray();
            text.AppendLine(presentationOwner.Length > 0
                ? $"展示消费者出口：{string.Join("、", presentationOwner)}。"
                : "展示消费者出口：档案未声明展示边界。");
            text.AppendLine();
        }
        return text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Escape(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);
}
