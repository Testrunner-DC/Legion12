using System.Text.RegularExpressions;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class FrontRowTauntAndTrialLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static readonly string[] FrontRowTauntCards =
        ["S01-0107", "S01-0204", "S01-0312", "ST01-04"];

    [Fact]
    [L12AbilityEvidence("S01-0107:ability:static:af427a4637e1c138", "row-condition-current", "authoritative-consumer", "closed-overlay-card-set")]
    [L12AbilityEvidence("S01-0204:ability:static:af427a4637e1c138", "row-condition-current", "authoritative-consumer", "closed-overlay-card-set")]
    [L12AbilityEvidence("S01-0312:ability:static:af427a4637e1c138", "row-condition-current", "authoritative-consumer", "closed-overlay-card-set")]
    [L12AbilityEvidence("ST01-04:ability:static:af427a4637e1c138", "row-condition-current", "authoritative-consumer", "closed-overlay-card-set")]
    public void EveryFrontRowTauntOverlaySegmentBindsToTheSharedCombatOutlet()
    {
        var expected = EffectLifecycleProfiles.FrontRowTauntOverlayAbilityIds
            .Order(StringComparer.Ordinal).ToArray();
        var segments = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.ExecutionModel == "continuous"
                && ability.Text.Contains("「位于前排」获得【挑衅】", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(expected, segments.Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray());
        Assert.All(segments, ability =>
        {
            Assert.Equal("static", ability.Trigger);
            Assert.Contains(ability.Atoms, atom => atom.Kind == L12AtomKinds.Condition
                && atom.Parameters.GetValueOrDefault("expression") == "source.row=front");
            Assert.Contains(ability.Atoms, atom => atom.Kind == L12AtomKinds.Keyword
                && atom.Parameters.GetValueOrDefault("keywordRef") == "taunt");
        });
        // S01 三卡由共享 overlay 注入；程咬金经结构化批4产出同文同原子段。
        // 同文本之外不得出现第二份逐卡来源。
        Assert.All(new[] { "S01-0107", "S01-0204", "S01-0312" }, cardId => Assert.Contains(
            L12StructuredCardRules.GetCombatOverlayAbilities(cardId),
            template => template.Text.Contains("「位于前排」获得【挑衅】", StringComparison.Ordinal)));
        Assert.DoesNotContain(L12StructuredCardRules.GetCombatOverlayAbilities("ST01-04"),
            template => template.Text.Contains("「位于前排」获得【挑衅】", StringComparison.Ordinal));
        Assert.DoesNotContain(L12StructuredCardRules.GetCombatOverlayAbilities("S01-0101"),
            template => template.Text.Contains("「位于前排」获得【挑衅】", StringComparison.Ordinal));
        Assert.DoesNotContain(L12StructuredCardRules.GetCombatOverlayAbilities("S02-0302"),
            template => template.Text.Contains("「位于前排」获得【挑衅】", StringComparison.Ordinal));
    }

    [Fact]
    [L12AbilityEvidence("S02-0004:ability:continuous:16dc08d7324d1649", "row-condition-current", "opponent-turn-troops", "ability-ref-chain", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0007:ability:continuous:58ce6286f39b73ee", "row-condition-current", "opponent-turn-troops", "ability-ref-chain", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0615:ability:continuous:16dc08d7324d1649", "row-condition-current", "opponent-turn-troops", "ability-ref-chain", "authoritative-consumer")]
    [L12AbilityEvidence("ST02-02:ability:continuous:c51a646e6338a9d1", "row-condition-current", "opponent-turn-troops", "ability-ref-chain", "authoritative-consumer")]
    [L12AbilityEvidence("ST04-01:ability:continuous:59dd263106457575", "row-condition-current", "opponent-turn-troops", "ability-ref-chain", "authoritative-consumer")]
    [L12AbilityEvidence("ST06-02:ability:continuous:c51a646e6338a9d1", "row-condition-current", "opponent-turn-troops", "ability-ref-chain", "authoritative-consumer")]
    public void EveryFrontRowKeywordTroopsLineSharesOneConditionChainAndBonusOutlet()
    {
        var expected = EffectLifecycleProfiles.FrontRowKeywordTroopsAbilityIds
            .Order(StringComparer.Ordinal).ToArray();
        var segments = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.ExecutionModel == "continuous"
                && ability.Text.Contains("「位于前排」获得ABILITY", StringComparison.Ordinal)
                && ability.Text.Contains("对方回合此军团兵力+1000", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(expected, segments.Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray());
        Assert.All(segments, ability =>
        {
            Assert.Equal("continuous", ability.Trigger);
            // 授予链：组合行 abilityRef 必须指向本卡的挑衅关键词定义段。
            var reference = Assert.Single(ability.Atoms, atom => atom.Kind == L12AtomKinds.SetState
                && atom.Parameters.ContainsKey("abilityRef")).Parameters["abilityRef"];
            var sequence = int.Parse(reference[(reference.LastIndexOf(':') + 1)..]);
            var card = Catalog.AtomicEffects.All.First(item => item.CardId == ability.CardId);
            var definition = card.Abilities.Single(item => item.Sequence == sequence);
            // 引用目标是本卡的关键词定义段：S02 为 keyword-definition 或 granted 形态，ST 为 keyword-definition。
            Assert.Contains(definition.Trigger, new[] { "keyword-definition", "granted" });
            Assert.Contains(definition.Atoms, atom => atom.Kind == L12AtomKinds.Keyword
                && atom.Parameters.GetValueOrDefault("keywordRef") == "taunt");
            // 对方回合兵力 +1000 的单入口声明。
            Assert.Equal(1000, L12StructuredCardRules.OpponentTurnFrontTroopsBonus(ability.CardId));
        });
    }

    [Fact]
    [L12AbilityEvidence("S01-0409:ability:attack:c900a6435336564c", "row-condition-current", "set-value-parameter", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0507:ability:attack:d20040947938d125", "row-condition-current", "set-value-parameter", "authoritative-consumer")]
    public void EveryBackRowTroopsSetSegmentSharesTheCombatProfileOutlet()
    {
        var expected = EffectLifecycleProfiles.BackRowAttackTroopsSetAbilityIds
            .Order(StringComparer.Ordinal).ToArray();
        var segments = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "attack"
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.ModifyTroops
                    && atom.Parameters.GetValueOrDefault("operation") == "set"))
            .ToArray();
        Assert.Equal(expected, segments.Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray());
        // 视为值只来自段原子参数：源义经 2000、阿塔兰忒·晋升 3000，机制同一。
        Assert.Equal("2000", segments.Single(ability => ability.CardId == "S01-0409")
            .Atoms.Single(atom => atom.Kind == L12AtomKinds.ModifyTroops).Parameters["value"]);
        Assert.Equal("3000", segments.Single(ability => ability.CardId == "S02-0507")
            .Atoms.Single(atom => atom.Kind == L12AtomKinds.ModifyTroops).Parameters["value"]);
        Assert.All(segments, ability => Assert.Contains(ability.Atoms,
            atom => atom.Kind == L12AtomKinds.Condition
                && atom.Parameters.GetValueOrDefault("expression")?.Contains("source.row=back", StringComparison.Ordinal) == true));
    }

    [Fact]
    [L12AbilityEvidence("S02-0602:ability:granted:6235a3f3a12afdbb", "parent-grant-boundary", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0614:ability:granted:6235a3f3a12afdbb", "parent-grant-boundary", "authoritative-consumer")]
    public void EveryGrantedGainRuneSegmentSharesTheSingleRuneSettlement()
    {
        var expected = EffectLifecycleProfiles.GrantedGainRuneAbilityIds
            .Order(StringComparer.Ordinal).ToArray();
        var segments = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "granted"
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.GainRune))
            .ToArray();
        Assert.Equal(expected, segments.Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray());
        Assert.All(segments, ability =>
        {
            Assert.Equal("granted-effect", ability.ExecutionModel);
            Assert.Equal("获得1符文。", ability.Text);
        });
    }

    [Fact]
    [L12AbilityEvidence("S01-01C1:ability:active:3a8789b35c0c2be4", "canonical-version-parity", "morale-cost-table", "authoritative-consumer")]
    [L12AbilityEvidence("ST01-C1:ability:static:6907bfcf5dbbfeb4", "canonical-version-parity", "morale-cost-table", "authoritative-consumer")]
    [L12AbilityEvidence("S01-02C1:ability:static:91802cda49d575fb", "canonical-version-parity", "morale-cost-table", "authoritative-consumer")]
    [L12AbilityEvidence("S01-02C1:ability:static:ddab147dd97c360f", "canonical-version-parity", "morale-cost-table", "authoritative-consumer")]
    [L12AbilityEvidence("ST02-C1:ability:static:f2b97501194b5c40", "canonical-version-parity", "morale-cost-table", "authoritative-consumer")]
    [L12AbilityEvidence("ST02-C1:ability:static:29d1864e955f856e", "canonical-version-parity", "morale-cost-table", "authoritative-consumer")]
    [L12AbilityEvidence("S01-03C1:ability:static:fa92f5d792a32bdc", "canonical-version-parity", "morale-cost-table", "authoritative-consumer")]
    [L12AbilityEvidence("ST03-C1:ability:static:36b1c5751cc508f9", "canonical-version-parity", "morale-cost-table", "authoritative-consumer")]
    [L12AbilityEvidence("S01-04C1:ability:static:7f60c31c00b0f718", "canonical-version-parity", "morale-cost-table", "authoritative-consumer")]
    [L12AbilityEvidence("ST04-C1:ability:static:d9cac21fb706e3c8", "canonical-version-parity", "morale-cost-table", "authoritative-consumer")]
    [L12AbilityEvidence("S02-05C1:ability:active:5dec5c18aaf62a03", "canonical-version-parity", "morale-cost-table", "authoritative-consumer")]
    [L12AbilityEvidence("S02-05C1A:ability:active:5dec5c18aaf62a03", "canonical-version-parity", "morale-cost-table", "authoritative-consumer")]
    [L12AbilityEvidence("S02-06C1:ability:static:7339369656140c39", "canonical-version-parity", "morale-cost-table", "authoritative-consumer")]
    [L12AbilityEvidence("ST06-C1:ability:static:88a76dc195d499ee", "canonical-version-parity", "morale-cost-table", "authoritative-consumer")]
    public void EveryMoraleActiveEffectSegmentRunsThroughTheSharedPipeline()
    {
        var expected = EffectLifecycleProfiles.MoraleActiveEffectAbilityIds
            .Order(StringComparer.Ordinal).ToArray();
        var actual = Catalog.AtomicEffects.All
            .Where(card => Catalog.Cards[card.CardId].CardType == "rune")
            .SelectMany(card => card.Abilities)
            .Where(ability => ability.Atoms.Any(atom => atom.Stage == "cost")
                && ability.Text.Contains("可消耗", StringComparison.Ordinal)
                && !ability.Text.Contains("翻转1张士气", StringComparison.Ordinal))
            .Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
        // 版本卡归一：每个 S01/S02 基准卡的 ST 版本在目录身份下同属一个阵营效果。
        Assert.All(actual, segment =>
        {
            var cardId = segment.Split(':')[0];
            Assert.Equal("rune", Catalog.Cards[cardId].CardType);
        });
    }

    [Fact]
    [L12AbilityEvidence("S01-00C1:ability:static:db1ae0a9efb4bff8", "counts-as-morale-structural", "authoritative-consumer")]
    [L12AbilityEvidence("S02-05C1:ability:static:5879d4c3fe97b3cf", "counts-as-morale-structural", "authoritative-consumer")]
    [L12AbilityEvidence("S02-05C1A:ability:static:5879d4c3fe97b3cf", "counts-as-morale-structural", "authoritative-consumer")]
    public void EveryMoraleResourceIdentitySegmentIsStructurallySatisfied()
    {
        var expected = EffectLifecycleProfiles.MoraleResourceIdentityAbilityIds
            .Order(StringComparer.Ordinal).ToArray();
        var actual = Catalog.AtomicEffects.All
            .Where(card => Catalog.Cards[card.CardId].CardType == "rune")
            .SelectMany(card => card.Abilities)
            .Where(ability => ability.ExecutionModel == "continuous"
                && !ability.Atoms.Any(atom => atom.Stage == "cost")
                && (ability.Text == "额外通用士气" || ability.Text.Contains("视为", StringComparison.Ordinal)))
            .Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    [L12AbilityEvidence("S01-0220:ability:death:00f139f8bc316591", "single-use", "troops-set-to-1000", "authoritative-consumer")]
    [L12AbilityEvidence("S01-0411:ability:death:00f139f8bc316591", "single-use", "troops-set-to-1000", "authoritative-consumer")]
    public void EveryImmortalReplacementSegmentSharesTheLethalReplacementPipeline()
    {
        var expected = EffectLifecycleProfiles.ImmortalReplacementAbilityIds
            .Order(StringComparer.Ordinal).ToArray();
        var actual = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "death"
                && ability.Text.Contains("作为代替", StringComparison.Ordinal)
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.ModifyTroops))
            .Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    [L12AbilityEvidence("S02-0606:ability:after-kill:7680beaaf4313595", "original-combat-kill-only", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0611:ability:after-kill:7680beaaf4313595", "original-combat-kill-only", "authoritative-consumer")]
    public void EveryAfterKillPiercingSegmentSharesThePrintedPiercingOutlet()
    {
        var expected = EffectLifecycleProfiles.AfterKillPiercingAbilityIds
            .Order(StringComparer.Ordinal).ToArray();
        var actual = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "after-kill"
                && ability.Text.Contains("本回合获得", StringComparison.Ordinal))
            .Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
        Assert.All(actual, id =>
        {
            var cardId = id.Split(':')[0];
            Assert.True(L12StructuredCardRules.HasPrintedKeywordReference(cardId, "piercing"));
        });
    }

    [Fact]
    [L12AbilityEvidence("S02-0512:ability:static:e44e97f2fb745816", "row-condition-current", "ability-ref-chain", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0503:ability:granted-static:e67d03cee97f98a6", "parent-grant-boundary", "front-row-required", "authoritative-consumer")]
    public void FrontRowKeywordGrantLinesResolveThroughTheirDeclaredOutlets()
    {
        // 埃涅阿斯：静态授予行经 abilityRef 链到本卡挑衅定义段，由 HasTaunt 结构化扫描消费。
        var aeneas = Catalog.AtomicEffects.All.First(card => card.CardId == "S02-0512");
        var grant = aeneas.Abilities.Single(ability => ability.AbilityId == EffectLifecycleProfiles.AeneasFrontRowGrantAbilityId);
        var reference = Assert.Single(grant.Atoms, atom => atom.Kind == L12AtomKinds.SetState
            && atom.Parameters.ContainsKey("abilityRef")).Parameters["abilityRef"];
        var sequence = int.Parse(reference[(reference.LastIndexOf(':') + 1)..]);
        Assert.Contains(aeneas.Abilities.Single(ability => ability.Sequence == sequence).Atoms,
            atom => atom.Kind == L12AtomKinds.Keyword
                && atom.Parameters.GetValueOrDefault("keywordRef") == "taunt");
        // 阿喀琉斯·晋升：granted-static 段被结构化扫描排除，实际授予走击杀后的实例授予入口；
        // 本段只锁定定义形态，运行时授予路径由档案 RuntimeOwners 声明。
        var achilles = Catalog.AtomicEffects.All.First(card => card.CardId == "S02-0503");
        var granted = achilles.Abilities.Single(ability => ability.AbilityId == EffectLifecycleProfiles.AchillesFrontRowGrantAbilityId);
        Assert.Equal("granted-continuous", granted.ExecutionModel);
        Assert.Contains("「位于前排」获得 ABILITY", granted.Text, StringComparison.Ordinal);
    }

    [Fact]
    [L12AbilityEvidence("S01-0101:ability:static:b5c9e323c0a061cc", "structured-split-siblings", "authoritative-consumer")]
    public void DuelCombatLineSharesTheCombatProfileOutletWithItsStructuredSiblings()
    {
        var line = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Single(ability => ability.AbilityId == EffectLifecycleProfiles.DuelCombatLineAbilityId);
        Assert.Equal("S01-0101", line.CardId);
        // 整行声明的语义由同卡两个结构化拆分段承载；overlay 模板提供运行时原子。
        var overlays = L12StructuredCardRules.GetCombatOverlayAbilities("S01-0101");
        Assert.Contains(overlays, template => template.Atoms.Any(atom => atom.Kind == L12AtomKinds.AttackRule
            && atom.Parameters.GetValueOrDefault("attackNoLoss") == "true"));
        Assert.Contains(overlays, template => template.Atoms.Any(atom => atom.Kind == L12AtomKinds.AttackRule
            && atom.Parameters.GetValueOrDefault("cannotBeRanged") == "true"));
        Assert.DoesNotContain(L12StructuredCardRules.GetCombatOverlayAbilities("S01-0106"),
            template => template.Atoms.Any(atom => atom.Kind == L12AtomKinds.AttackRule
                && atom.Parameters.GetValueOrDefault("attackNoLoss") == "true"));
    }

    [Fact]
    [L12AbilityEvidence("S01-0203:ability:static:0a317a499dc4420e", "shared-recalc-outlet", "condition-current", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0516:ability:static:a29458736f52d0a9", "shared-recalc-outlet", "condition-current", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0519:ability:static:2b21805b14115304", "shared-recalc-outlet", "condition-current", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0523:ability:static:05da64c53e8a7606", "shared-recalc-outlet", "condition-current", "authoritative-consumer")]
    public void EverySimpleContinuousTroopsRuleFeedsTheSharedRecalcOutlet()
    {
        var expected = EffectLifecycleProfiles.SimpleContinuousTroopsAbilityIds
            .Order(StringComparer.Ordinal).ToArray();
        var cards = new[] { "S01-0203", "S02-0516", "S02-0519", "S02-0523" };
        var actual = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.ExecutionModel == "continuous"
                && cards.Contains(ability.CardId, StringComparer.Ordinal)
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.ModifyTroops)
                && ability.Atoms.All(atom => atom.Stage != "cost"))
            .Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    [L12AbilityEvidence("S02-0510:ability:static:5193793609facf70", "source-rested-current", "authoritative-consumer")]
    public void RestedFreeFrontBackMoveSegmentFeedsTheSharedMoveCommand()
    {
        var segments = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.ExecutionModel == "continuous"
                && ability.Text.Contains("前后位移无需消耗费用", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal([EffectLifecycleProfiles.RestedFreeFrontBackMoveAbilityId],
            segments.Select(ability => ability.AbilityId).ToArray());
    }

    [Fact]
    [L12AbilityEvidence("S01-0016:ability:reaction:eda8f9987e9ccfe3", "capability-registry", "pool-parity", "authoritative-consumer")]
    [L12AbilityEvidence("S01-0018:ability:reaction:248207b49df4bd77", "capability-registry", "pool-parity", "authoritative-consumer")]
    [L12AbilityEvidence("S01-0002:ability:reaction:a472c4e7c34abf4b", "capability-registry", "self-discard-cost", "authoritative-consumer")]
    public void SpecialResponseSegmentsReadTheSharedCapabilityRegistry()
    {
        // 封闭集合：语义注册表三张卡恰好各有一段 reaction 段——绝对防御/落穴走 response-negate，
        // 佣兵部队走 response-block。其他 reaction 段属于反击战术等已归属族，不在本族。
        var reactions = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "reaction").ToArray();
        var negate = reactions.Where(ability =>
            L12StructuredCardSemantics.IsAbsoluteDefenseResponse(ability.CardId)
            || L12StructuredCardSemantics.IsPitfallEntryNegationResponse(ability.CardId)).ToArray();
        var handBlock = reactions.Where(ability =>
            L12StructuredCardSemantics.IsMercenaryHandBlockResponse(ability.CardId)).ToArray();
        Assert.Equal(EffectLifecycleProfiles.NegateResponseAbilityIds.Order(StringComparer.Ordinal),
            negate.Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal([EffectLifecycleProfiles.MercenaryHandBlockAbilityId],
            handBlock.Select(ability => ability.AbilityId).ToArray());
        // 身份注册表本身封闭：邻近卡号不得命中。
        Assert.False(L12StructuredCardSemantics.IsAbsoluteDefenseResponse("S01-0017"));
        Assert.False(L12StructuredCardSemantics.IsPitfallEntryNegationResponse("S01-0019"));
        Assert.False(L12StructuredCardSemantics.IsMercenaryHandBlockResponse("S01-0003"));
    }

    [Fact]
    public void ResponseEligibilityHasNoCardIdBranchesOutsideTheCapabilityRegistry()
    {
        // 收敛守卫：响应资格/卡池/提交链路不得再出现三张响应卡的卡号字面量。
        var sourcePath = Path.Combine(SourceRoot(), "服务端WebSocket", "TwelveLegions", "L12PromptsAndSetup.cs");
        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("\"S01-0016\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"S01-0018\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"S01-0002\"", source, StringComparison.Ordinal);
    }

    private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "")
        => Directory.GetParent(Path.GetDirectoryName(sourcePath)!)!.FullName;

    [Fact]
    [L12AbilityEvidence("S01-DS01:ability:static:9b5681c438931452", "registry-closed-set", "authoritative-consumer")]
    [L12AbilityEvidence("S01-DS02:ability:static:4408d437a8ab5e5a", "registry-closed-set", "authoritative-consumer")]
    [L12AbilityEvidence("S01-DS03:ability:static:70004a014a03d2a0", "registry-closed-set", "authoritative-consumer")]
    [L12AbilityEvidence("S01-DS04:ability:static:017c7359962a2512", "registry-closed-set", "authoritative-consumer")]
    [L12AbilityEvidence("S01-DS08:ability:static:3e7cd5724f09420c", "registry-closed-set", "authoritative-consumer")]
    [L12AbilityEvidence("S01-DS10:ability:static:33501d2503c08b73", "registry-closed-set", "authoritative-consumer")]
    [L12AbilityEvidence("S02-DS01:ability:static:31558cb4f3e2c3da", "registry-closed-set", "authoritative-consumer")]
    [L12AbilityEvidence("S02-DS02:ability:static:01aeea1f7fc7e317", "registry-closed-set", "authoritative-consumer")]
    [L12AbilityEvidence("S02-DS03:ability:continuous:fed4f60f2af1523c", "registry-closed-set", "authoritative-consumer")]
    [L12AbilityEvidence("S02-DS04:ability:static:00575cc9fcb1aaaa", "registry-closed-set", "authoritative-consumer")]
    [L12AbilityEvidence("S02-DS05:ability:static:335d304b639c5d3f", "registry-closed-set", "authoritative-consumer")]
    [L12AbilityEvidence("S02-DS06:ability:static:c1632b7b22b87c4f", "registry-closed-set", "authoritative-consumer")]
    [L12AbilityEvidence("S01-DS04:ability:attack:68f2ff0b600a41e8", "registry-closed-set", "authoritative-consumer")]
    [L12AbilityEvidence("S02-DS05:ability:attack:4326fa5eef9e6e3a", "registry-closed-set", "authoritative-consumer")]
    public void EveryDisasterContinuousRuleSegmentReadsTheSharedRuleRegistry()
    {
        var segments = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => Catalog.Cards[ability.CardId].CardType == "destruction"
                && ability.CardId != "ST-DS02"
                && ((ability.ExecutionModel is "continuous" or "rule"
                        && ability.Text.Contains("持续", StringComparison.Ordinal))
                    || ability.Trigger == "attack"))
            .ToArray();
        Assert.Equal(EffectLifecycleProfiles.DisasterContinuousRuleAbilityIds.Order(StringComparer.Ordinal),
            segments.Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray());
        Assert.All(segments, ability =>
            Assert.True(L12ActiveDisasterRules.HasRegisteredContinuousRule(ability.CardId)));
        // 注册表封闭：ST 天灾与未登记卡号不得命中。
        Assert.False(L12ActiveDisasterRules.HasRegisteredContinuousRule("ST-DS01"));
        Assert.False(L12ActiveDisasterRules.HasRegisteredContinuousRule("ST-DS02"));
        Assert.False(L12ActiveDisasterRules.HasRegisteredContinuousRule("S01-DS05"));
    }

    [Fact]
    public void DisasterContinuousRulesHaveNoCardIdBranchesOutsideTheRegistry()
    {
        // 收敛守卫：规则消费链路文件不得再出现 S01/S02 天灾卡号字面量。
        // 例外（不在守卫范围）：AtomicEffects 路由登记、L12Disasters 触发分发、
        // 牌库/最终天灾构筑（L12GameEngine/L12PromptsAndSetup）与展示命名。
        var server = Path.Combine(SourceRoot(), "服务端WebSocket", "TwelveLegions");
        foreach (var file in new[]
                 {
                     "L12Actions.cs", "L12RuleKernelIntegration.cs", "L12CompositeEffectPlans.cs",
                     "L12EffectContinuations.cs", "L12EffectGeneratedPlay.cs", "L12RuleActions.cs",
                     "L12ActionAvailability.cs", "L12ActiveMoraleQuote.cs",
                 })
        {
            var source = File.ReadAllText(Path.Combine(server, file));
            Assert.False(Regex.IsMatch(source, "\"S0[12]-DS\\d+\""),
                $"{file} must not keep disaster card-id branches");
        }
    }

    [Fact]
    [L12AbilityEvidence("S01-0205:ability:death:7016351513168cdb", "once-per-turn", "substitution-kind", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0504:ability:lethal-replacement:3fb565d50830f260", "once-per-turn", "front-row-required", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0515:ability:lethal-replacement:654c3040d6da8b4d", "once-per-turn", "front-row-required", "authoritative-consumer")]
    public void EveryLethalReplacementSegmentSharesTheOfferPipeline()
    {
        // 霍列姆赫布按 death 触发登记、阿喀琉斯/海伦按 lethal-replacement 登记，均走同一弹框管线；
        // 湖中仙女的馈赠走 RemoveFromField 内的剑替代路径，不属本族。
        var segments = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => (ability.Trigger == "lethal-replacement"
                    || (ability.Trigger == "death" && ability.Text.Contains("代替承受", StringComparison.Ordinal)))
                && ability.CardId != "S02-06S3")
            .ToArray();
        Assert.Equal(EffectLifecycleProfiles.LethalReplacementOfferAbilityIds.Order(StringComparer.Ordinal),
            segments.Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray());
        Assert.All(segments, ability =>
            Assert.Contains("代替承受", ability.Text, StringComparison.Ordinal));
    }

    [Fact]
    [L12AbilityEvidence("S02-0006:ability:continuous:7f3bdf9055e53845", "usage-commit", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0306:ability:continuous:a5a8e191442bbfac", "usage-commit", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0008:ability:continuous:766cca673a9815ad", "effective-faction", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0301:ability:continuous:e48cf407ce847427", "master-gate", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0305:ability:game-setup:cf14affeb486a9f7", "setup-defaults", "authoritative-consumer")]
    [L12AbilityEvidence("S02-03M1:ability:game-setup:46b2a85c54cecc56", "setup-defaults", "authoritative-consumer")]
    [L12AbilityEvidence("S01-01D1:ability:setup:281db2829152b981", "setup-defaults", "authoritative-consumer")]
    [L12AbilityEvidence("S01-02D1:ability:setup:281db2829152b981", "setup-defaults", "authoritative-consumer")]
    [L12AbilityEvidence("S01-03D1:ability:setup:281db2829152b981", "setup-defaults", "authoritative-consumer")]
    [L12AbilityEvidence("S01-04D1:ability:setup:281db2829152b981", "setup-defaults", "authoritative-consumer")]
    [L12AbilityEvidence("S02-05D1:ability:setup:281db2829152b981", "setup-defaults", "authoritative-consumer")]
    [L12AbilityEvidence("S02-06D1:ability:setup:281db2829152b981", "setup-defaults", "authoritative-consumer")]
    public void RuleDeclarationSegmentsBindToTheirSharedRegistries()
    {
        // 卡名共享次数族：信仰狂热者与密米尔之泉共读同一注册表键。
        Assert.Equal(EffectLifecycleProfiles.CardNameOncePerTurnAbilityIds.Order(StringComparer.Ordinal),
            Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
                .Where(ability => ability.Trigger == "continuous" && ability.ExecutionModel == "rule"
                    && L12CardNameUsageRules.Keys.ContainsKey(ability.CardId))
                .Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray());
        Assert.False(L12CardNameUsageRules.Keys.ContainsKey("S02-0007"));
        // 开场规则族：全池恰好两段。
        Assert.Equal(EffectLifecycleProfiles.GameSetupRuleAbilityIds.Order(StringComparer.Ordinal),
            Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
                .Where(ability => ability.Trigger == "game-setup")
                .Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray());
        // 六张主城开场追加士气段台账标 setup（自动追加，无选发窗口），同属开场管线档案。
        Assert.Equal(EffectLifecycleProfiles.GameSetupAutoMoraleAbilityIds.Order(StringComparer.Ordinal),
            Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
                .Where(ability => ability.Trigger == "setup")
                .Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray());
        // 阵营映射与主宰门槛各为全池唯一。
        Assert.Equal([EffectLifecycleProfiles.UniversalFactionMappingAbilityId],
            Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
                .Where(ability => ability.ExecutionModel == "rule"
                    && ability.Text.Contains("视为与我方主宰阵营相同", StringComparison.Ordinal))
                .Select(ability => ability.AbilityId).ToArray());
        Assert.Equal([EffectLifecycleProfiles.ThorHammerMasterGateAbilityId],
            Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
                .Where(ability => ability.ExecutionModel == "rule"
                    && ability.Text.Contains("当我方主宰为", StringComparison.Ordinal))
                .Select(ability => ability.AbilityId).ToArray());
    }

    [Fact]
    [L12AbilityEvidence("S02-0604:ability:trial:2117897dcefd3125", "trial-value-matches-card-data", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0606:ability:trial:bb29c925c9fcdc82", "trial-value-matches-card-data", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0609:ability:trial:bb29c925c9fcdc82", "trial-value-matches-card-data", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0610:ability:trial:bb29c925c9fcdc82", "trial-value-matches-card-data", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0613:ability:trial:bb29c925c9fcdc82", "trial-value-matches-card-data", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0614:ability:trial:bb29c925c9fcdc82", "trial-value-matches-card-data", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0617:ability:trial:bb29c925c9fcdc82", "trial-value-matches-card-data", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0618:ability:trial:2117897dcefd3125", "trial-value-matches-card-data", "authoritative-consumer")]
    public void EveryPrintedTrialValueSegmentMatchesCardDataAndBindsToTheRuleAction()
    {
        var expected = EffectLifecycleProfiles.TrialValueAbilityIds
            .Order(StringComparer.Ordinal).ToArray();
        var segments = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "trial").ToArray();
        Assert.Equal(expected, segments.Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray());
        Assert.All(segments, ability =>
        {
            Assert.Equal("rule", ability.ExecutionModel);
            var atom = Assert.Single(ability.Atoms, item => item.Kind == L12AtomKinds.Special
                && item.Parameters.GetValueOrDefault("semantic") == "trial-value");
            var definition = Catalog.Cards[ability.CardId];
            Assert.True(L12StructuredCardRules.IsTrialLegion(definition));
            Assert.Equal(definition.TrialValue.GetValueOrDefault().ToString(), atom.Parameters["value"]);
        });
    }

    [Fact]
    [L12AbilityEvidence("S02-0501:ability:promotion:3eb467465ef47272", "god-power-consume-and-flip", "single-candidate-choice", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0503:ability:promotion:3eb467465ef47272", "god-power-consume-and-flip", "single-candidate-choice", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0505:ability:promotion:e890e8664470e824", "god-power-consume-and-flip", "single-candidate-choice", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0507:ability:promotion:e890e8664470e824", "god-power-consume-and-flip", "single-candidate-choice", "authoritative-consumer")]
    public void EveryPrintedPromotionSegmentMatchesItsGodPowerCostAndSharedEntry()
    {
        var expected = EffectLifecycleProfiles.PromotionEntryAbilityIds
            .Order(StringComparer.Ordinal).ToArray();
        var segments = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "promotion").ToArray();
        Assert.Equal(expected, segments.Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray());
        Assert.All(segments, ability =>
        {
            Assert.Equal("summon-flow", ability.ExecutionModel);
            var cost = Assert.Single(ability.Atoms, atom => atom.Kind == L12AtomKinds.Special
                && atom.Stage == "cost" && atom.Parameters.GetValueOrDefault("domain") == "god-power");
            Assert.Equal("consume-and-flip", cost.Parameters["operation"]);
            // 段原子费用必须与印刷文本“消耗并翻转N神力”的数值一致。
            var marker = $"消耗并翻转{cost.Parameters["amount"]}神力";
            Assert.Contains(marker, ability.Text, StringComparison.Ordinal);
            Assert.Contains(ability.Atoms, atom => atom.Kind == L12AtomKinds.SelectTarget
                && atom.Parameters.GetValueOrDefault("filter") == "same-name-non-promoted"
                && atom.Parameters.GetValueOrDefault("min") == "1"
                && atom.Parameters.GetValueOrDefault("max") == "1");
        });
    }

    [Fact]
    public void StarterAeneasPromotionCardKeepsRuntimeIdentityWithoutPrintedSegment()
    {
        // ST05-01 埃涅阿斯·晋升是【晋升者】：卡面带“晋升 消耗并翻转1神力……”规则行，
        // 运行时由共享晋升入口（基底映射 ST05-01→S02-0512、费用读卡文）承担。
        // 结构化目录不为它生成 promotion 印刷段；封闭集合守卫与本测试共同锁定该不对称——
        // 未来为它补印段或移除运行时晋升能力，都必须重新审查本族。
        var definition = Catalog.Cards["ST05-01"];
        Assert.Equal("olympus", definition.Faction);
        Assert.Contains("晋升者", definition.Traits);
        Assert.Contains("晋升 消耗并翻转1神力，叠放至我方同名非【晋升者】军团上方登场。",
            definition.Effect, StringComparison.Ordinal);
        Assert.DoesNotContain(Catalog.AtomicEffects.All
            .First(card => card.CardId == "ST05-01").Abilities, ability => ability.Trigger == "promotion");
    }

    [Fact]
    public void StarterTrialLegionsCarryIdentityFromCardDataWithoutPrintedSegment()
    {
        // ST06-06/07/08 的结构化路径不生成 trial 印刷段；试炼身份纯由卡牌数据承载。
        // 未来若补印段，封闭集合守卫与本测试都必须重新审查。
        foreach (var cardId in new[] { "ST06-06", "ST06-07", "ST06-08" })
        {
            var definition = Catalog.Cards[cardId];
            Assert.True(L12StructuredCardRules.IsTrialLegion(definition));
            Assert.DoesNotContain(Catalog.AtomicEffects.All
                .First(card => card.CardId == cardId).Abilities, ability => ability.Trigger == "trial");
        }
    }
}
