using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// Offline audit only. This reads the same catalog as the engine/admin; it neither
// registers new effects nor treats a card-level route or test as ability coverage.
public sealed class EffectLifecycleInventoryTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal sealed record AbilityRow(string CardId, string Name, L12AtomicAbility Definition,
        string EntryEvidence, string[] RouteCandidates, string[] ReviewGaps,
        L12AbilityTestReference[] TestReferences);
    internal sealed record Inventory(int Schema, int CardCount, string[] CardsWithoutAbilities,
        AbilityRow[] Abilities);

    private static string[] Flows(IEnumerable<L12EffectAtom> atoms) => atoms
        .Where(atom => atom.Kind == L12AtomKinds.CompositeFlow)
        .Select(atom => atom.Parameters.GetValueOrDefault("flow") ?? "")
        .Where(flow => flow.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static string AtomSignature(IEnumerable<L12EffectAtom> atoms) => JsonSerializer.Serialize(
        atoms.OrderBy(atom => atom.Order).Select(atom => new
        {
            atom.Kind, atom.Stage, atom.Order,
            Parameters = atom.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
        }));

    internal static string EntryEvidence(L12AtomicAbility ability,
        IEnumerable<L12VerifiedAtomicProgram> finePrograms, IEnumerable<L12VerifiedAtomicProgram> routes)
    {
        var fine = finePrograms.Where(program => program.CardId == ability.CardId
            && program.Trigger == ability.Trigger).ToArray();
        if (fine.Any(program => AtomSignature(program.Atoms) == AtomSignature(ability.Atoms)))
            return "fine-definition";
        var flows = Flows(ability.Atoms);
        var registeredFlows = fine.Concat(routes.Where(program => program.CardId == ability.CardId
            && program.Trigger == ability.Trigger)).SelectMany(program => Flows(program.Atoms))
            .ToHashSet(StringComparer.Ordinal);
        if (flows.Length > 0 && flows.All(registeredFlows.Contains)) return "composite-definition";
        // A rule-action/continuous label is not an audited runtime owner. Keep it pending.
        return "owner-unreviewed";
    }

    internal static Inventory Build(L12Catalog catalog)
    {
        var fine = L12VerifiedAtomicPrograms.All;
        var routes = L12RuntimeEffectRoutes.AllPrograms;
        var evidence = EffectLifecycleEvidence.Read(catalog);
        var cards = catalog.AtomicEffects.All.OrderBy(card => card.CardId, StringComparer.Ordinal).ToArray();
        var rows = cards.SelectMany(card => card.Abilities.OrderBy(ability => ability.Sequence).Select(ability =>
        {
            var entry = EntryEvidence(ability, fine, routes);
            var candidates = fine.Concat(routes).Where(program => program.CardId == card.CardId
                    && program.Trigger == ability.Trigger)
                .Select(program => string.IsNullOrEmpty(program.ProgramId)
                    ? $"{program.CardId}:{program.Trigger}:fine" : program.ProgramId)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var gaps = new List<string>
            {
                "protocol-profile", "presentation-consumers", "normal", "no-target",
                "negated", "target-invalidated", "duplicate-submit", "reconnect",
            };
            if (entry == "owner-unreviewed") gaps.Insert(0, "runtime-owner");
            if (ability.Atoms.Any(atom => atom.Stage == "cost")) gaps.Add("payment-cancel");
            if (ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.SelectTarget))
            {
                gaps.Add("single-candidate-choice");
                gaps.Add("multi-target-applicability");
            }
            return new AbilityRow(card.CardId, card.Name, ability, entry, candidates, gaps.ToArray(),
                evidence.Where(reference => reference.AbilityId == ability.AbilityId).ToArray());
        })).ToArray();
        return new Inventory(2, cards.Length,
            cards.Where(card => card.Abilities.Count == 0).Select(card => card.CardId).ToArray(), rows);
    }

    internal static string Render(Inventory inventory)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(inventory, JsonOptions)))).ToLowerInvariant();
        var text = new StringBuilder();
        text.AppendLine("# 逐能力效果一致性台账（自动基线）").AppendLine();
        text.AppendLine("由 `scripts/export-l12-effect-lifecycle-inventory.ps1` 从实际 L12Catalog 生成；不要手工修改此表。");
        text.AppendLine("本表只盘点定义与待核对项，不是测试通过证明。执行进度与最终验收仍以 [实施计划](../EFFECT-LIFECYCLE-ROADMAP.md) 为准。").AppendLine();
        text.AppendLine($"卡牌：{inventory.CardCount}；能力段：{inventory.Abilities.Length}；无能力卡：{inventory.CardsWithoutAbilities.Length}。");
        text.AppendLine("这是当前运行目录的分段基线；印刷卡文分段正确性、旧入口完整归属仍需审查，不能把自动导出当成P0完成。");
        text.AppendLine($"内容指纹：`{fingerprint}`。").AppendLine();
        text.AppendLine("| 定义证据 | 能力数 |").AppendLine("| --- | ---: |");
        foreach (var group in inventory.Abilities.GroupBy(row => row.EntryEvidence).OrderBy(group => group.Key, StringComparer.Ordinal))
            text.AppendLine($"| {group.Key} | {group.Count()} |");
        text.AppendLine();
        text.AppendLine("fine-definition = 原子顺序/参数与本能力匹配；composite-definition = 本能力显式Flow与登记路由匹配；owner-unreviewed = 还需定位实际入口。前两者也不等于生命周期验收通过。");
        text.AppendLine("同卡同触发只算候选，不能把另一能力的程序继承为本能力已覆盖。无能力卡单列，不能从分母中静默消失。");
        text.AppendLine("具名用例按完整能力ID（含结构哈希）绑定；只记录列出的测试范围，不把声明期恢复冒充结算期恢复，也不把源代码引用当实际执行回执。完整异常矩阵仍待核对；不适用路径必须说明理由。");
        text.AppendLine("共同待核对项：生命周期档案、展示消费者、正例、无目标、无效、目标失效、重复提交、重连。费用段另核对取消兜底，对象选择另核对唯一候选/多目标适用性。");
        text.AppendLine("完整原子参数、Cost/效果正文、场景与路由候选保存在同次生成的JSON审计产物；程序标签verified仅为既有目录状态。").AppendLine();
        text.AppendLine("## 已关联具名证据（不是整能力验收通过）").AppendLine();
        text.AppendLine("| 能力ID | 测试方法 / 参数卡牌 | 已核对的用例范围 |");
        text.AppendLine("| --- | --- | --- |");
        foreach (var reference in inventory.Abilities.SelectMany(row => row.TestReferences))
            text.AppendLine($"| {Cell(reference.AbilityId)} | {Cell(reference.TestMethod)} / {reference.CaseCardId} | {Cell(string.Join(", ", reference.Scopes))} |");
        text.AppendLine();
        text.AppendLine("## 能力清单").AppendLine();
        text.AppendLine("| 卡牌/效果段 | 稳定能力ID | 时点/模型 | 定义证据 | Cost | 原子顺序 | 场景数 | 正文 |");
        text.AppendLine("| --- | --- | --- | --- | --- | --- | ---: | --- |");
        foreach (var row in inventory.Abilities)
        {
            var ability = row.Definition;
            var atoms = string.Join(" → ", ability.Atoms.OrderBy(atom => atom.Order)
                .Select(atom => $"{atom.Stage}:{atom.Kind}"));
            text.AppendLine($"| {Cell(row.CardId + " " + row.Name)} #{ability.Sequence} | {Cell(ability.AbilityId)} | {Cell(ability.Trigger + "/" + ability.ExecutionModel)} | {row.EntryEvidence} | {Cell(ability.CostText ?? "—")} | {Cell(atoms)} | {ability.Presentations.Count} | {Cell(ability.Text)} |");
        }
        text.AppendLine().AppendLine("## 无能力卡").AppendLine();
        text.AppendLine(inventory.CardsWithoutAbilities.Length == 0 ? "无。" : string.Join("、", inventory.CardsWithoutAbilities));
        return text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Cell(string value) => value.Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("|", "&#124;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal)
        .Replace("\n", "<br>", StringComparison.Ordinal);

    [Fact]
    public void InventoryAccountsForEveryAbilityAndDoesNotClaimTestCoverage()
    {
        var catalog = Catalog;
        var inventory = Build(catalog);
        Assert.Equal(catalog.Cards.Count, inventory.CardCount);
        Assert.Equal(catalog.AtomicEffects.Coverage().TotalAbilities, inventory.Abilities.Length);
        Assert.Equal(inventory.Abilities.Length, inventory.Abilities.Select(row => row.Definition.AbilityId).Distinct().Count());
        Assert.Equal(catalog.Cards.Count, inventory.Abilities.Select(row => row.CardId)
            .Concat(inventory.CardsWithoutAbilities).Distinct().Count());
        Assert.All(inventory.Abilities, row =>
        {
            Assert.Contains("normal", row.ReviewGaps);
            Assert.Contains("reconnect", row.ReviewGaps);
            Assert.NotEmpty(row.Definition.StructureHash);
            Assert.Equal(row.Definition, catalog.AtomicEffects.Find(row.CardId)!.Abilities
                .Single(ability => ability.AbilityId == row.Definition.AbilityId));
        });
    }

    [Fact]
    public void SameCardAndTriggerCannotGrantAnotherAbilityRuntimeEvidence()
    {
        var ability = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .First(ability => EntryEvidence(ability, L12VerifiedAtomicPrograms.All, []) == "fine-definition");
        var unrelated = ability with { Atoms = [ability.Atoms[0]], Text = "同卡同触发但不同效果" };
        Assert.Equal("owner-unreviewed", EntryEvidence(unrelated, L12VerifiedAtomicPrograms.All, L12RuntimeEffectRoutes.AllPrograms));
    }

    [Fact]
    public void MatchingFlowIsRequiredInsteadOfSameCardRouteExistence()
    {
        var ability = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .First(ability => EntryEvidence(ability, L12VerifiedAtomicPrograms.All, L12RuntimeEffectRoutes.AllPrograms) == "composite-definition");
        var unrelated = ability with { Atoms = ability.Atoms.Where(atom => atom.Kind != L12AtomKinds.CompositeFlow).ToArray() };
        Assert.Equal("owner-unreviewed", EntryEvidence(unrelated, [], L12RuntimeEffectRoutes.AllPrograms));
    }

    [Fact]
    public void InventoryRenderingIsDeterministicAndEscapesCardText()
    {
        Assert.Equal(Render(Build(Catalog)), Render(Build(Catalog)));
        Assert.Equal("&lt;主动&gt;&#124;A<br>B&amp;C", Cell("<主动>|A\nB&C"));
    }

    [Fact]
    public void PublicResponseFamilyHasExactLiveCasesWithoutClaimingFullLifecycleCoverage()
    {
        var catalog = Catalog;
        var evidence = EffectLifecycleEvidence.Read(catalog).Where(reference => reference.TestMethod.EndsWith(
            nameof(StackResponseChoiceRegressionTests.PublicResponseDeclarationsRestoreAndRejectDuplicateFinalSubmission), StringComparison.Ordinal)).ToArray();
        var plans = (IReadOnlyDictionary<string, string>)typeof(L12GameEngine)
            .GetField("PublicResponsePlans", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        Assert.Equal(plans.Keys.Order(), evidence.Select(reference => reference.CaseCardId).Distinct().Order());
        Assert.All(evidence, reference =>
        {
            Assert.Contains("reconnect-declaration", reference.Scopes);
            Assert.DoesNotContain("reconnect", reference.Scopes);
            Assert.Equal("linked-not-execution-receipt", reference.Status);
        });
    }

    [Fact]
    public void CommittedInventoryMatchesRuntimeDefinitions()
    {
        var inventory = Build(Catalog);
        var markdown = Render(inventory);
        var target = Path.Combine(SourceRoot(), "docs", "l12", "EFFECT-ABILITY-INVENTORY.md");
        if (Environment.GetEnvironmentVariable("L12_UPDATE_EFFECT_INVENTORY") == "1")
            File.WriteAllText(target, markdown, new UTF8Encoding(false));
        Assert.True(File.Exists(target), "Run scripts/export-l12-effect-lifecycle-inventory.ps1 to generate the ability inventory.");
        Assert.Equal(markdown, File.ReadAllText(target).Replace("\r\n", "\n", StringComparison.Ordinal));
        var output = Environment.GetEnvironmentVariable("L12_EFFECT_INVENTORY_JSON");
        if (!string.IsNullOrWhiteSpace(output))
            File.WriteAllText(output, JsonSerializer.Serialize(inventory, JsonOptions), new UTF8Encoding(false));
    }

    private static string SourceRoot([CallerFilePath] string sourcePath = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, ".."));
}
