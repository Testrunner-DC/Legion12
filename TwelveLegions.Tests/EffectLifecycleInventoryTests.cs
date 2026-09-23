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
        L12AbilityTestReference[] TestReferences, L12LifecycleProfile? Profile);
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
        var profiles = EffectLifecycleProfiles.Read(catalog);
        var cards = catalog.AtomicEffects.All.OrderBy(card => card.CardId, StringComparer.Ordinal).ToArray();
        var rows = cards.SelectMany(card => card.Abilities.OrderBy(ability => ability.Sequence).Select(ability =>
        {
            var profile = profiles.GetValueOrDefault(ability.AbilityId);
            var entry = profile is null ? EntryEvidence(ability, fine, routes) : "shared-rule-owner";
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
            if (profile is not null)
            {
                gaps.Remove("protocol-profile");
                gaps.RemoveAll(profile.NotApplicable.ContainsKey);
                gaps.AddRange(profile.AdditionalChecks);
            }
            if (ability.Atoms.Any(atom => atom.Stage == "cost")) gaps.Add("payment-cancel");
            if (ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.SelectTarget))
            {
                gaps.Add("single-candidate-choice");
                gaps.Add("multi-target-applicability");
            }
            return new AbilityRow(card.CardId, card.Name, ability, entry, candidates, gaps.ToArray(),
                evidence.Where(reference => reference.AbilityId == ability.AbilityId).ToArray(), profile);
        })).ToArray();
        return new Inventory(3, cards.Length,
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
        text.AppendLine("fine-definition = 原子顺序/参数与本能力匹配；composite-definition = 本能力显式Flow与登记路由匹配；shared-rule-owner = 精确能力已绑定共用规则入口及适用性档案；owner-unreviewed = 还需定位实际入口。任何一种归属证据均不等于生命周期验收通过。");
        text.AppendLine("同卡同触发只算候选，不能把另一能力的程序继承为本能力已覆盖。无能力卡单列，不能从分母中静默消失。");
        text.AppendLine("具名用例按完整能力ID（含结构哈希）绑定；只记录列出的测试范围，不把声明期恢复冒充结算期恢复，也不把源代码引用当实际执行回执。完整异常矩阵仍待核对；不适用路径必须说明理由。");
        text.AppendLine("共同待核对项：生命周期档案、展示消费者、正例、无目标、无效、目标失效、重复提交、重连。费用段另核对取消兜底，对象选择另核对唯一候选/多目标适用性。");
        text.AppendLine("完整原子参数、Cost/效果正文、场景与路由候选保存在同次生成的JSON审计产物；程序标签verified仅为既有目录状态。").AppendLine();
        text.AppendLine("## 已核对生命周期档案（不是执行回执）").AppendLine();
        foreach (var group in inventory.Abilities.Where(row => row.Profile is not null).GroupBy(row => row.Profile!.Id))
        {
            var profile = group.First().Profile!;
            text.AppendLine($"### {profile.Id}").AppendLine();
            text.AppendLine($"精确绑定能力数：{group.Count()}。运行入口："
                + string.Join("；", profile.RuntimeOwners.Select(owner => $"{owner.Key} = "
                    + (owner.Value.Contains('.') ? owner.Value : $"L12GameEngine.{owner.Value}"))) + "。").AppendLine();
            foreach (var exclusion in profile.NotApplicable)
                text.AppendLine($"- {exclusion.Key}：{exclusion.Value}");
            text.AppendLine();
        }
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
    public void ReviewedRuleActionProfilesHaveExactOwnersAndReasonedExemptions()
    {
        var inventory = Build(Catalog);
        var rows = inventory.Abilities.Where(row => row.Profile?.Id == "rule-action:cavalry-move").ToArray();
        Assert.Equal(EffectLifecycleProfiles.NativeCavalryAbilityIds.Order(), rows.Select(row => row.Definition.AbilityId).Order());
        Assert.All(rows, row =>
        {
            Assert.Equal("shared-rule-owner", row.EntryEvidence);
            Assert.Equal("rule-action:cavalry-move", row.Profile!.Id);
            Assert.Equal("CavalryMove", row.Profile.RuntimeOwners["command"]);
            Assert.Equal("BuildRuleActionViews", row.Profile.RuntimeOwners["button"]);
            Assert.DoesNotContain("runtime-owner", row.ReviewGaps);
            Assert.DoesNotContain("protocol-profile", row.ReviewGaps);
            Assert.All(row.Profile.NotApplicable, exclusion =>
            {
                Assert.NotEmpty(exclusion.Value);
                Assert.DoesNotContain(exclusion.Key, row.ReviewGaps);
            });
            Assert.Contains("source-invalidated", row.ReviewGaps);
            Assert.Contains("destination-invalidated", row.ReviewGaps);
            Assert.Contains("normal", row.ReviewGaps); // Linked cases are not a release receipt.
            Assert.Equal(4, row.TestReferences.Length);
            Assert.All(row.TestReferences, reference => Assert.Equal("linked-not-execution-receipt", reference.Status));
            Assert.Contains(row.TestReferences, reference => reference.Scopes.Contains("reconnect-after-command"));
            Assert.Contains(row.TestReferences, reference => reference.Scopes.Contains("single-candidate-choice"));
        });
    }

    [Fact]
    public void ACardOrRuleActionLabelAloneCannotInheritAReviewedOwner()
    {
        var catalog = Catalog;
        var inventory = Build(catalog);
        foreach (var id in EffectLifecycleProfiles.NativeCavalryAbilityIds)
        {
            var row = inventory.Abilities.Single(item => item.Definition.AbilityId == id);
            Assert.All(inventory.Abilities.Where(item => item.CardId == row.CardId && item.Definition.AbilityId != id),
                other => Assert.NotEqual("rule-action:cavalry-move", other.Profile?.Id));
            var unrelated = row.Definition with { AbilityId = id + "-unreviewed", StructureHash = "changed" };
            Assert.Equal("owner-unreviewed", EntryEvidence(unrelated, [], []));
            Assert.False(EffectLifecycleProfiles.Read(catalog).ContainsKey(unrelated.AbilityId));
        }
    }

    [Fact]
    public void PrintedRangeProfilesBindOnlyExactContinuousSegmentsAndDoNotClaimBattleCompletion()
    {
        var inventory = Build(Catalog);
        var rows = inventory.Abilities.Where(row => row.Profile?.Id == "continuous:printed-range").ToArray();
        Assert.Equal(47, rows.Length);
        Assert.Equal(EffectLifecycleProfiles.PrintedRangedAbilityIds.Order(), rows.Select(row => row.Definition.AbilityId).Order());
        Assert.All(rows, row =>
        {
            Assert.Equal("shared-rule-owner", row.EntryEvidence);
            Assert.Equal("L12StructuredCardRules.CombatProfile", row.Profile!.RuntimeOwners["condition-and-permission"]);
            Assert.Equal("TryValidateAttackTarget", row.Profile.RuntimeOwners["target-revalidation"]);
            Assert.All(row.Profile.NotApplicable, exclusion =>
            {
                Assert.NotEmpty(exclusion.Value);
                Assert.DoesNotContain(exclusion.Key, row.ReviewGaps);
            });
            Assert.DoesNotContain("destination-invalidated", row.ReviewGaps); // Not the cavalry protocol.
            Assert.Contains("target-invalidated", row.ReviewGaps);
            Assert.Contains("ranged-no-loss", row.ReviewGaps); // Profile/preview checks are not damage tests.
            var evidence = Assert.Single(row.TestReferences, reference => reference.TestMethod.EndsWith(
                nameof(PrintedRangedProfileTests.PrintedRangeUsesCurrentRowAndRestoresAuthoritativePreview), StringComparison.Ordinal));
            Assert.Contains("reconnect-profile", evidence.Scopes);
            Assert.DoesNotContain("reconnect", evidence.Scopes);
            Assert.Equal("linked-not-execution-receipt", evidence.Status);
        });
        Assert.Equal("active:paid-extended-range",
            inventory.Abilities.Single(row => row.CardId == "S01-0003" && row.Definition.Trigger == "active").Profile?.Id);
    }

    [Fact]
    public void PaidExtendedRangeProfilesBindOnlyTheTwoActiveSegmentsAndKeepTheirCostsDistinct()
    {
        var inventory = Build(Catalog);
        var rows = inventory.Abilities.Where(row => row.Profile?.Id == "active:paid-extended-range").ToArray();
        Assert.Equal(EffectLifecycleProfiles.PaidExtendedRangeAbilityIds.Order(),
            rows.Select(row => row.Definition.AbilityId).Order());
        Assert.All(rows, row =>
        {
            Assert.Equal("shared-rule-owner", row.EntryEvidence);
            Assert.Equal("L12StructuredCardSemantics.ExtendedRangeRule", row.Profile!.RuntimeOwners["definition"]);
            Assert.Equal("TryCommitS1ExtendedActiveAbility", row.Profile.RuntimeOwners["cost-commit"]);
            Assert.Equal("TryResolveS1ExtendedActive", row.Profile.RuntimeOwners["settlement"]);
            Assert.Equal("TryValidateAttackTarget", row.Profile.RuntimeOwners["attack-revalidation"]);
            Assert.DoesNotContain("no-target", row.ReviewGaps);
            Assert.DoesNotContain("target-invalidated", row.ReviewGaps);
            Assert.Contains("payment-cancel", row.ReviewGaps);
            Assert.Contains(row.TestReferences, reference => reference.Scopes.Contains("negated-settlement"));
            Assert.Contains(row.TestReferences, reference => reference.Scopes.Contains("repeat-activation"));
            Assert.Contains(row.TestReferences, reference => reference.Scopes.Contains("source-invalidated-settlement"));
            Assert.Contains(row.TestReferences, reference => reference.Scopes.Contains("reconnect-payment"));
            Assert.Contains(row.TestReferences, reference => reference.Scopes.Contains("authoritative-attack"));
        });
        Assert.Contains("消耗2士气", rows.Single(row => row.CardId == "S01-0003").Definition.CostText);
        Assert.Contains("返还1士气", rows.Single(row => row.CardId == "S01-0113").Definition.CostText);
    }

    [Fact]
    public void ActiveRestProfileOwnsTheCommonCostBoundaryWithoutClaimingCardSpecificSettlement()
    {
        var inventory = Build(Catalog);
        var rows = inventory.Abilities.Where(row => row.Profile?.Id == "cost:active-rest").ToArray();
        Assert.Equal(27, rows.Length);
        Assert.Equal(EffectLifecycleProfiles.ActiveRestAbilityIds.Order(),
            rows.Select(row => row.Definition.AbilityId).Order());
        Assert.All(rows, row =>
        {
            Assert.Equal("shared-rule-owner", row.EntryEvidence);
            Assert.Equal("L12StructuredCardRules.IsActiveRestAbility", row.Profile!.RuntimeOwners["runtime-identity"]);
            Assert.Equal("CommitStructuredActiveRestCost", row.Profile.RuntimeOwners["cost-commit"]);
            Assert.Equal("AddActivePaidCostPresentation", row.Profile.RuntimeOwners["cost-presentation"]);
            Assert.Contains(row.Definition.Atoms,
                atom => atom.Kind == L12AtomKinds.RestSource && atom.Stage == "cost");
            Assert.Contains("normal", row.ReviewGaps);
            Assert.Contains("negated", row.ReviewGaps);
            Assert.Contains("target-invalidated", row.ReviewGaps);
            Assert.Contains("reconnect", row.ReviewGaps);
            var evidence = Assert.Single(row.TestReferences,
                reference => reference.TestMethod.EndsWith(
                    nameof(ActiveRestCommonLifecycleProfileTests.EveryPrintedActiveRestSegmentUsesTheSharedCostBoundary),
                    StringComparison.Ordinal));
            Assert.Contains("active-rest-cost", evidence.Scopes);
            Assert.Equal("linked-not-execution-receipt", evidence.Status);
        });
    }

    [Fact]
    public void SelfDamageEntryDiscountProfileBindsOnlyTheSixHandPlayCostSegments()
    {
        var inventory = Build(Catalog);
        var rows = inventory.Abilities.Where(row => row.Profile?.Id == "hand-play:self-damage-entry-discount").ToArray();
        Assert.Equal(EffectLifecycleProfiles.SelfDamageEntryDiscountAbilityIds.Order(),
            rows.Select(row => row.Definition.AbilityId).Order());
        Assert.All(rows, row =>
        {
            Assert.Equal("shared-rule-owner", row.EntryEvidence);
            Assert.Equal("L12StructuredCardRules.SelfDamageEntryDiscount", row.Profile!.RuntimeOwners["definition"]);
            Assert.Equal("PlayCard", row.Profile.RuntimeOwners["declaration-and-choice"]);
            Assert.Equal("PayMasterDamageCostAndCanContinue", row.Profile.RuntimeOwners["self-damage-payment"]);
            Assert.DoesNotContain("no-target", row.ReviewGaps);
            Assert.DoesNotContain("negated", row.ReviewGaps);
            Assert.Contains("payment-cancel", row.ReviewGaps);
            var evidence = Assert.Single(row.TestReferences,
                reference => reference.TestMethod.EndsWith(
                    nameof(SelfDamageEntryDiscountLifecycleProfileTests.EveryPrintedSelfDamageDiscountUsesOneHandPlayCostProtocol),
                    StringComparison.Ordinal));
            Assert.Contains("last-health-terminal", evidence.Scopes);
        });
    }

    [Fact]
    public void CombatKeywordProfilesBindEveryPrintedKeywordDefinitionExactlyOnce()
    {
        var inventory = Build(Catalog);
        var expected = EffectLifecycleProfiles.CombatKeywordDefinitionAbilityIds
            .SelectMany(pair => pair.Value.Select(id => (Id: id, Keyword: pair.Key)))
            .OrderBy(entry => entry.Id).ToArray();
        var rows = inventory.Abilities
            .Where(row => row.Profile?.Id.StartsWith("keyword:", StringComparison.Ordinal) == true)
            .OrderBy(row => row.Definition.AbilityId).ToArray();
        Assert.Equal(expected.Select(entry => entry.Id), rows.Select(row => row.Definition.AbilityId));
        Assert.Equal(expected.Select(entry => $"keyword:{entry.Keyword}"), rows.Select(row => row.Profile!.Id));
        Assert.All(rows, row =>
        {
            Assert.Equal("shared-rule-owner", row.EntryEvidence);
            Assert.Equal("L12StructuredCardRules.HasKeywordDefinition", row.Profile!.RuntimeOwners["definition"]);
            Assert.DoesNotContain("negated", row.ReviewGaps);
            Assert.DoesNotContain("payment-cancel", row.ReviewGaps);
            Assert.Contains("parent-grant-boundary", row.ReviewGaps);
            Assert.Contains(row.TestReferences, reference => reference.TestMethod.EndsWith(
                nameof(CombatKeywordDefinitionLifecycleProfileTests.EveryKeywordDefinitionHasOneStructuredSemanticOwner),
                StringComparison.Ordinal));
        });
    }

    [Fact]
    public void GrantedKeywordProfilesBindEveryGrantedKeywordDefinitionExactlyOnce()
    {
        var inventory = Build(Catalog);
        var expected = EffectLifecycleProfiles.GrantedKeywordDefinitionAbilityIds
            .SelectMany(pair => pair.Value.Select(id => (Id: id, Keyword: pair.Key)))
            .OrderBy(entry => entry.Id).ToArray();
        var rows = inventory.Abilities
            .Where(row => row.Profile?.Id.StartsWith("keyword-granted:", StringComparison.Ordinal) == true)
            .OrderBy(row => row.Definition.AbilityId).ToArray();
        Assert.Equal(expected.Select(entry => entry.Id), rows.Select(row => row.Definition.AbilityId));
        Assert.Equal(expected.Select(entry => $"keyword-granted:{entry.Keyword}"), rows.Select(row => row.Profile!.Id));
        Assert.All(rows, row =>
        {
            Assert.Equal("shared-rule-owner", row.EntryEvidence);
            Assert.Equal("L12StructuredCardRules.HasPrintedKeywordReference", row.Profile!.RuntimeOwners["definition"]);
            Assert.DoesNotContain("negated", row.ReviewGaps);
            Assert.DoesNotContain("payment-cancel", row.ReviewGaps);
            Assert.Contains("parent-grant-boundary", row.ReviewGaps);
            Assert.Contains(row.TestReferences, reference => reference.TestMethod.EndsWith(
                nameof(CombatKeywordDefinitionLifecycleProfileTests.EveryGrantedKeywordDefinitionHasOneStructuredSemanticOwner),
                StringComparison.Ordinal));
        });
    }

    [Fact]
    public void DesertHandSummonProfileUsesOneSharedCandidateRuleAndExplainsItsUnavailablePath()
    {
        var row = Assert.Single(Build(Catalog).Abilities,
            item => item.Definition.AbilityId == EffectLifecycleProfiles.DesertHandSummonAbilityId);
        Assert.Equal("shared-rule-owner", row.EntryEvidence);
        Assert.Equal("composite:desert-hand-summon", row.Profile!.Id);
        Assert.Equal("IsDesertHandSummonCandidate", row.Profile.RuntimeOwners["candidate-generation"]);
        Assert.Equal("TryResolveS2FactionTactic", row.Profile.RuntimeOwners["settlement-revalidation"]);
        Assert.DoesNotContain("runtime-owner", row.ReviewGaps);
        Assert.DoesNotContain("protocol-profile", row.ReviewGaps);
        Assert.DoesNotContain("no-target", row.ReviewGaps);
        Assert.Contains("cost-prepaid", row.ReviewGaps);
        Assert.Contains("settlement-slot-invalidated", row.ReviewGaps);
        Assert.Contains("payment-cancel", row.ReviewGaps);
        Assert.Equal(6, row.TestReferences.Length);
        Assert.Contains(row.TestReferences, reference => reference.Scopes.Contains("normal"));
        Assert.Contains(row.TestReferences, reference => reference.Scopes.Contains("negated"));
        Assert.Contains(row.TestReferences, reference => reference.Scopes.Contains("target-invalidated"));
        Assert.Contains(row.TestReferences, reference => reference.Scopes.Contains("reconnect"));
        Assert.Contains(row.TestReferences, reference => reference.Scopes.Contains("payment-cancel"));
    }

    [Fact]
    public void CounterDeploymentProfileBindsOnlyItsTwoHandCounterSegments()
    {
        var rows = Build(Catalog).Abilities.Where(item => item.Profile?.Id == "composite:counter-deployment").ToArray();
        Assert.Equal(EffectLifecycleProfiles.CounterDeploymentAbilityIds.Order(),
            rows.Select(row => row.Definition.AbilityId).Order());
        Assert.All(rows, row =>
        {
            Assert.Equal("shared-rule-owner", row.EntryEvidence);
            Assert.Equal("IsCounterDeploymentCandidate", row.Profile!.RuntimeOwners["candidate-generation"]);
            Assert.Equal("SetDeclaredCounterTactics", row.Profile.RuntimeOwners["settlement-revalidation"]);
            Assert.Contains("independent-target-settlement", row.ReviewGaps);
            Assert.Contains("no-target", row.ReviewGaps);
            Assert.Contains("negated", row.ReviewGaps);
            Assert.Contains("reconnect", row.ReviewGaps);
        });
        var defense = Assert.Single(rows, row => row.CardId == "S02-0009");
        Assert.Contains(defense.TestReferences, reference => reference.Scopes.Contains("normal"));
        Assert.Contains(defense.TestReferences, reference => reference.Scopes.Contains("target-invalidated"));
        Assert.Contains(defense.TestReferences, reference => reference.Scopes.Contains("no-target"));
        Assert.Contains(defense.TestReferences, reference => reference.Scopes.Contains("negated"));
        Assert.Contains(defense.TestReferences, reference => reference.Scopes.Contains("reconnect"));
        Assert.Contains(defense.TestReferences, reference => reference.Scopes.Contains("duplicate-submit"));
        var uesugi = Assert.Single(rows, row => row.CardId == "S01-0403");
        Assert.Contains(uesugi.TestReferences, reference => reference.Scopes.Contains("normal"));
        Assert.Contains(uesugi.TestReferences, reference => reference.Scopes.Contains("target-invalidated"));
    }

    [Fact]
    public void StrictHandEntryProfileBindsOnlyTheThreeReviewedPrivateEntrySegments()
    {
        var rows = Build(Catalog).Abilities.Where(item => item.Profile?.Id == "private-zone:strict-hand-entry").ToArray();
        Assert.Equal(EffectLifecycleProfiles.StrictHandEntryAbilityIds.Order(),
            rows.Select(row => row.Definition.AbilityId).Order());
        Assert.All(rows, row =>
        {
            Assert.Equal("shared-rule-owner", row.EntryEvidence);
            Assert.Equal("TrySummonFromHand", row.Profile!.RuntimeOwners["settlement-revalidation"]);
            Assert.Contains("stale-instance-no-replacement", row.Profile.AdditionalChecks);
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
