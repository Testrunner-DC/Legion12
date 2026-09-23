using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// P0 长尾归属批（temp/longtail-mapping.md）的具名证据：每个档案一个 Fact，
// 携带全部段的 L12AbilityEvidence（scopes 用档案的 AdditionalChecks 标签），
// 断言封闭集合（重扫口径与 EffectLifecycleProfiles.Read 的守卫一致）与卡号-段对应。
public sealed class PipelineLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12AtomicAbility[] AllAbilities()
        => Catalog.AtomicEffects.All.SelectMany(card => card.Abilities).ToArray();

    private static L12AtomicAbility[] AssertClosedSet(string[] expectedIds, IEnumerable<L12AtomicAbility> rescan)
    {
        var segments = rescan.ToArray();
        Assert.Equal(expectedIds.Order(StringComparer.Ordinal).ToArray(),
            segments.Select(ability => ability.AbilityId).Order(StringComparer.Ordinal).ToArray());
        // 卡号-段对应：能力 ID 的卡号前缀必须与所属卡一致，且每卡段数与声明一致。
        Assert.All(segments, ability =>
            Assert.StartsWith($"{ability.CardId}:ability:", ability.AbilityId, StringComparison.Ordinal));
        var expectedByCard = expectedIds.GroupBy(id => id.Split(':')[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var actualByCard = segments.GroupBy(ability => ability.CardId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Assert.Equal(expectedByCard, actualByCard);
        return segments;
    }

    private static bool IsPrintedActiveText(string text) =>
        (text.Contains("可消耗", StringComparison.Ordinal) || text.Contains("可弃置", StringComparison.Ordinal)
            || text.Contains("可返还", StringComparison.Ordinal)
            || text.Contains("可对我方主宰造成", StringComparison.Ordinal)
            || text.Contains("可将", StringComparison.Ordinal)
            || text.Contains("可选择以下一项", StringComparison.Ordinal)
            || text.Contains("回合1次 本回合", StringComparison.Ordinal))
        && !text.StartsWith("受到伤害时", StringComparison.Ordinal)
        && !text.StartsWith("触发", StringComparison.Ordinal);

    [Fact]
    [L12AbilityEvidence("S01-01M1:ability:static:c03878ecc263c0e6", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-01M1:ability:static:d024f673ff236321", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-02D1:ability:static:dbf8222a61a31140", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-02D1:ability:static:0c86a6851cf9d2ce", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-02M3:ability:static:705baec08fc6bc02", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-03D1:ability:static:d89d0b3dade7b6c8", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-03D1:ability:static:342ed2c72fcd22aa", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-03M1:ability:static:d047647f18d541e4", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-04D1:ability:static:fcd47c32a0a46e1a", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-04D1:ability:static:3c467d3eba318af6", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-04M1:ability:static:2c285709f5669922", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-04M1:ability:static:51c3f1e1976210f8", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-04M2:ability:static:ce8699cac703af1c", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-04M2:ability:attack:ebb2054e23f75cd1", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-0314:ability:active:a923615d65edc8ea", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-0417:ability:static:f10ff922d718f82e", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-0004:ability:active:6f9f6988e1ea4be0", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-02M1:ability:static:53475d8f080332f1", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-02M2:ability:static:f3398615ac233d89", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-01M1:ability:active:4834e3b50d036f27", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0301:ability:active:61c655977499e4be", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-03M1:ability:active:54e6f9c40764f804", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-02M1:ability:active:014219b1c6c557fa", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0205:ability:active:bf422a987e0ab5de", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-06D1:ability:static:65b6607da57e5096", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-06M1:ability:active:08922e53e852b78f", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-06S1:ability:static:75769d93e0ca669f", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-06S5:ability:static:5444a7c87e0351bd", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-06S6:ability:after-attack:b158f5749a6c161e", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0404:ability:granted:2c2b9693ca8cf3b8", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0404:ability:granted:e7c384ccba9ff2f3", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-05M1:ability:active:b2bece6897eb980b", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-05M2:ability:active:e4b2c63a32960f8e", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-05D1:ability:active:1e9195c93dff4ee9", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0520:ability:mode-promotion-discount:98eb71c68928c091", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0520:ability:mode-ready-after-kill:927badbb354c7607", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0603:ability:granted:8cd73702b7db90b0", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0603:ability:granted:ee3b46417c9fc4f7", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0604:ability:trial-completed:9d25a05a194bedc1", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0013:ability:active-while-attached:f64dc7647e481c5f", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-01M2:ability:static:f3ee48a69ee29306", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-0307:ability:static:b89287bced985f8c", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-03M2:ability:static:e3e85412fe04e44b", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-0215:ability:mode-ready-guard:3e3294affff84b58", "per-card-branch", "authoritative-consumer")]
    [L12AbilityEvidence("S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53", "per-card-branch", "authoritative-consumer")]
    public void EveryPipelineActiveEffectSegmentBindsToTheSharedActivePipeline()
    {
        var cards = EffectLifecycleProfiles.PipelineActiveEffectAbilityIds
            .Select(id => id.Split(':')[0]).ToHashSet(StringComparer.Ordinal);
        var rescan = AllAbilities().Where(ability => cards.Contains(ability.CardId)
            && !EffectLifecycleProfiles.ActiveRestAbilityIds.Contains(ability.AbilityId, StringComparer.Ordinal)
            && !EffectLifecycleProfiles.MoraleFaceFlipAbilityIds.Contains(ability.AbilityId, StringComparer.Ordinal)
            && (ability.Trigger is "active" or "active-while-attached" or "trial-completed"
                || ability.Trigger.StartsWith("mode-", StringComparison.Ordinal)
                || (ability.Trigger == "granted" && ability.ExecutionModel == "granted-effect"
                    && ability.CardId is "S02-0404" or "S02-0603")
                || (ability.Trigger == "attack" && ability.CardId == "S01-04M2")
                || (ability.Trigger == "after-attack" && ability.CardId == "S02-06S6")
                || (ability.Trigger == "static" && IsPrintedActiveText(ability.Text))));
        var segments = AssertClosedSet(EffectLifecycleProfiles.PipelineActiveEffectAbilityIds, rescan);
        // S02-06S6 台账标 after-attack，运行时是「可消耗X符文」三模式主动能力：以台账 ID 绑定并锁定该措辞。
        Assert.Contains("可消耗X符文", segments.Single(ability => ability.CardId == "S02-06S6").Text,
            StringComparison.Ordinal);
        // 次数/资格注册表覆盖两张代表卡；逐卡分支属于能力内容而非独立生命周期。
        Assert.NotNull(L12ActiveUsageRules.Find("S02-06M1", "morriganReadyOnKill"));
        Assert.NotNull(L12ActiveUsageRules.Find("S02-06C1", "runeUse"));
    }

    [Fact]
    [L12AbilityEvidence("S01-01M1:ability:static:0924c3a5995ba164", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S01-01M1:ability:death:ee5adb706424f233", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S01-04M2:ability:leave:4e83a7191108369b", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S01-0204:ability:leave:a59801f7c2874f4a", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S01-0311:ability:static:3409dd9fa29f684f", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S01-0311:ability:after-attack:65ce2315ff4c0465", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S01-0414:ability:static:e001b352b3693d93", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0001:ability:after-opponent-tactic:6d30a9b672845491", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0305:ability:master-damaged:a4a2c92cad3ad28c", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-02M1:ability:friendly-legion-death:a366c9a7f75b5f29", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-06M2:ability:tactic-effect-resolved:e802cc6dcf73fe92", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S01-02M3:ability:static:3a86c87f975d5851", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S01-02M3:ability:static:c339139cc1c9b00c", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0103:ability:attack:607e6460eed6637b", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0511:ability:attack:c367ee3457cbd5f4", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0516:ability:attack:077dc7337586413c", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0605:ability:attack:82a5bf2622bf4d20", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0607:ability:attack:25d5c998d14502d7", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0608:ability:attack:4581df1cc635dd68", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0608:ability:attack:0999d120e02e3c50", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0612:ability:attack:c195f409c875e9eb", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0617:ability:attack:c8dd6c6601a73ebb", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0610:ability:after-trial:451b6d549a5c98c4", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0611:ability:enter:0cc32f023a1b4f11", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0614:ability:enter:601eddfb8abbb8d2", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0602:ability:after-kill:e290e1e434e45531", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-06S4:ability:trial-complete:f95fed6f3ff0efc0", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-06S5:ability:static:1e799825eedf3331", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-06S3:ability:static:3616e3ca17ffd729", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S01-01D1:ability:static:103012fd4239104f", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0401:ability:continuous:9601da1d8445f865", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0006:ability:discarded:89d3ee4207648aa1", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-01S1:ability:master-morale-return:8d098fe32e7b253b", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-04M1:ability:friendly-legion-moves:654df25d049352f7", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-04M1:ability:friendly-front-to-back:e46218b2ac936410", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0503:ability:after-attack:e3ced12ddde14fdb", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba", "per-card-plan", "authoritative-consumer")]
    [L12AbilityEvidence("ST01-C1:ability:static:605b9aa3d8a1ed93", "per-card-plan", "authoritative-consumer")]
    public void EveryPipelinePublicTriggerSegmentBindsToTheSharedTriggerPipeline()
    {
        var cards = EffectLifecycleProfiles.PipelinePublicTriggerAbilityIds
            .Select(id => id.Split(':')[0]).ToHashSet(StringComparer.Ordinal);
        var attackCards = new[] { "S02-0103", "S02-0511", "S02-0516", "S02-0605",
            "S02-0607", "S02-0608", "S02-0612", "S02-0617" };
        var rescan = AllAbilities().Where(ability => cards.Contains(ability.CardId)
            && ((ability.Trigger == "attack" && attackCards.Contains(ability.CardId, StringComparer.Ordinal))
                || (ability.Trigger == "death" && ability.CardId == "S01-01M1")
                || (ability.Trigger == "leave" && ability.CardId is "S01-04M2" or "S01-0204")
                || (ability.Trigger == "after-attack" && ability.CardId is "S01-0311" or "S02-0503")
                || ability.Trigger is "after-opponent-tactic" or "master-damaged-by-effect"
                    or "tactic-effect-resolved" or "after-trial" or "trial-complete" or "friendly-legion-death"
                    or "discarded" or "master-morale-return" or "friendly-legion-moves"
                    or "friendly-back-to-front" or "friendly-front-to-back" or "after-opponent-attack"
                || (ability.Trigger == "master-damaged" && ability.ExecutionModel == "triggered")
                || (ability.Trigger == "enter" && ability.CardId is "S02-0611" or "S02-0614")
                || (ability.Trigger == "after-kill" && ability.CardId == "S02-0602")
                || (ability.Trigger == "continuous" && ability.CardId == "S02-0401")
                || (ability.Trigger == "static"
                    && (ability.CardId is "S01-0414" or "S01-01D1" or "S01-0311"
                        || (ability.CardId == "S01-01M1"
                            && ability.Text.Contains("可在前排活跃登场", StringComparison.Ordinal))
                        || (ability.CardId == "S01-02M3"
                            && (ability.Text.StartsWith("对方 ", StringComparison.Ordinal)
                                || ability.Text.StartsWith("受到伤害时", StringComparison.Ordinal)))
                        || ((ability.CardId is "S02-06S5" or "S02-06S3")
                            && ability.Text.StartsWith("触发", StringComparison.Ordinal))
                        || (ability.CardId == "ST01-C1"
                            && ability.Text.Contains("士气为0张时", StringComparison.Ordinal))))));
        var segments = AssertClosedSet(EffectLifecycleProfiles.PipelinePublicTriggerAbilityIds, rescan);
        // 梅杰德#2/#3 是同一句被拆开的两半：合并后语义完整（主宰伤害→陵墓守卫登场守卫）。
        var medjed = segments.Where(ability => ability.CardId == "S01-02M3").ToArray();
        Assert.Equal(2, medjed.Length);
        Assert.StartsWith("对方 ", medjed.Single(ability => ability.Text.Contains("主宰因对方进攻或效果", StringComparison.Ordinal)).Text,
            StringComparison.Ordinal);
        Assert.StartsWith("受到伤害时", medjed.Single(ability => ability.Text.Contains("受到伤害时", StringComparison.Ordinal)).Text,
            StringComparison.Ordinal);
        // 批次规划器按控制者分批，不逐卡分支；空候选不产生批次。
        Assert.Empty(L12TriggerBatchPlanner.Plan([], 0));
    }

    [Fact]
    [L12AbilityEvidence("S02-0012:ability:play:bafe1ab6a18493c0", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0206:ability:play:ca021e5c16b59965", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0206:ability:play:bd784d08e38e0ed8", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0307:ability:play:f9b21f21c30d2eb3", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0306:ability:master-effect-damage-threshold:978e2dc72d59418c", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0405:ability:play:0a13775c2081e642", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0405:ability:play:b03190adf1322a1a", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0406:ability:play:35815c7115c7ce71", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0521:ability:play-additional:2b5a094468a81a7a", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0522:ability:play:a09dadaebc5e13de", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0522:ability:play-additional:49fb773d1512e5b3", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0620:ability:play:ac4a80f231805917", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0620:ability:play:c2e3d34e7ac83c86", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0621:ability:play:9a7d744018bd9e66", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0621:ability:play:ecdfaa719e9112ba", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0622:ability:hand-play:5b5e4bf8f495f21a", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0622:ability:play:d5226a525c565d25", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0302:ability:hand-play:4e8ff9ea92325bac", "per-card-flow", "authoritative-consumer")]
    [L12AbilityEvidence("ST03-01:ability:entry-discount:373b8092202cdf17", "per-card-flow", "authoritative-consumer")]
    public void EveryPipelineHandPlaySegmentBindsToTheSharedPlayPipeline()
    {
        var cards = EffectLifecycleProfiles.PipelineHandPlayAbilityIds
            .Select(id => id.Split(':')[0]).ToHashSet(StringComparer.Ordinal);
        var rescan = AllAbilities().Where(ability => cards.Contains(ability.CardId)
            && !EffectLifecycleProfiles.MoraleFaceFlipAbilityIds.Contains(ability.AbilityId, StringComparer.Ordinal)
            && ability.Trigger is "play" or "play-additional" or "hand-play" or "master-effect-damage-threshold"
                or "entry-discount");
        AssertClosedSet(EffectLifecycleProfiles.PipelineHandPlayAbilityIds, rescan);
    }

    [Fact]
    [L12AbilityEvidence("S02-0005:ability:opponent-attacks-master:806afb384f303aee", "capability-registry-pending", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c", "capability-registry-pending", "authoritative-consumer")]
    public void EveryPipelineResponseSegmentBindsToTheSharedResponsePipeline()
    {
        var rescan = AllAbilities().Where(ability => ability.CardId is "S02-0005" or "S02-0106"
            && ability.Trigger is "opponent-attacks-master" or "opponent-attack-or-effect");
        var segments = AssertClosedSet(EffectLifecycleProfiles.PipelineResponseAbilityIds, rescan);
        // 两段的资格谓词仍逐卡（傀儡硬编码、乾坤·阴在 CanUseS2CounterAtStack），未纳入响应身份注册表。
        Assert.All(segments, ability => Assert.Equal("reaction", ability.ExecutionModel));
    }

    [Fact]
    [L12AbilityEvidence("S01-0107:ability:static:715fe715dcb8ea28", "row-condition-current", "authoritative-consumer", "structured-split-siblings")]
    [L12AbilityEvidence("S01-0312:ability:static:b2e1a67373ad69cc", "row-condition-current", "authoritative-consumer", "structured-split-siblings")]
    [L12AbilityEvidence("S01-0204:ability:static:4108715d77479b32", "row-condition-current", "authoritative-consumer", "structured-split-siblings")]
    public void EveryFrontRowCompositeLineIsCarriedByItsStructuredSplitSiblings()
    {
        // 全池重扫整行印刷声明：无【】括号的「获得挑衅」措辞，区别于 overlay 拆分段。
        var rescan = AllAbilities().Where(ability => ability.ExecutionModel == "continuous"
            && ability.Text.Contains("「位于前排」获得挑衅", StringComparison.Ordinal));
        var segments = AssertClosedSet(EffectLifecycleProfiles.FrontRowCompositeLineAbilityIds, rescan);
        Assert.All(segments, ability =>
        {
            Assert.Equal("static", ability.Trigger);
            Assert.Contains("（对方只可进攻带有此效果的军团）", ability.Text, StringComparison.Ordinal);
        });
        // 语义由结构化拆分段承载：张飞/拉葛莎的对方回合兵力出口已归属；
        // 陵墓构造体是纯挑衅行（无兵力半句），其 overlay 拆分段已归属前排挑衅族。
        Assert.All(new[] { "S01-0107", "S01-0312" }, cardId =>
            Assert.Equal(1000, L12StructuredCardRules.OpponentTurnFrontTroopsBonus(cardId)));
        Assert.Contains(EffectLifecycleProfiles.FrontRowTauntOverlayAbilityIds,
            id => id.StartsWith("S01-0204:", StringComparison.Ordinal));
    }

    [Fact]
    [L12AbilityEvidence("ST-DS01:ability:disaster:00612b44a6a3ac99", "authoritative-consumer")]
    [L12AbilityEvidence("ST-DS03:ability:disaster:c974ef724419ccdf", "authoritative-consumer")]
    [L12AbilityEvidence("S01-DS02:ability:turn-end:9d632a451357ff71", "authoritative-consumer")]
    [L12AbilityEvidence("S01-DS10:ability:turn-start:a790e35d0012c86f", "authoritative-consumer")]
    public void EveryDisasterAuthoritySegmentBindsToTheDisasterPipeline()
    {
        // ST 天灾触发段 + 全部天灾卡的回合开始/结束段；S01/S02 天灾触发段已有细原子/复合定义，不入本族。
        var rescan = AllAbilities().Where(ability => Catalog.Cards[ability.CardId].CardType == "destruction"
            && ((ability.Trigger == "disaster" && ability.CardId is "ST-DS01" or "ST-DS03")
                || ability.Trigger is "turn-start" or "turn-end"));
        var segments = AssertClosedSet(EffectLifecycleProfiles.PipelineDisasterAuthorityAbilityIds, rescan);
        Assert.All(segments, ability => Assert.Equal("triggered", ability.ExecutionModel));
    }

    [Fact]
    [L12AbilityEvidence("S01-03M1:ability:static:f1ba346550e4decc", "authoritative-consumer")]
    public void ValkyrieDrawPhaseSegmentBindsToTheTurnStartOutlet()
    {
        var rescan = AllAbilities().Where(ability => ability.CardId == "S01-03M1"
            && ability.Text.Contains("抽牌阶段改为弃置", StringComparison.Ordinal));
        AssertClosedSet([EffectLifecycleProfiles.ValkyrieDrawPhaseAbilityId], rescan);
    }

    [Fact]
    [L12AbilityEvidence("S01-02M1:ability:static:68187ab0edb25d9c", "authoritative-consumer")]
    public void IsisSetupSegmentBindsToTheSharedSetupOutlet()
    {
        var rescan = AllAbilities().Where(ability => ability.CardId == "S01-02M1"
            && ability.Text.Contains("游戏开始时", StringComparison.Ordinal));
        AssertClosedSet([EffectLifecycleProfiles.IsisSetupAbilityId], rescan);
    }

    [Fact]
    [L12AbilityEvidence("S02-01M1:ability:leave:cf42cfffe1b9b9bc", "authoritative-consumer")]
    public void WukongMasterLegionReturnSegmentBindsToTheDepartureOutlet()
    {
        var rescan = AllAbilities().Where(ability => ability.CardId == "S02-01M1" && ability.Trigger == "leave");
        var segments = AssertClosedSet([EffectLifecycleProfiles.MasterLegionReturnAbilityId], rescan);
        Assert.Equal("replacement", segments[0].ExecutionModel);
    }

    [Fact]
    [L12AbilityEvidence("S02-0013:ability:host-leaves-artifact:b2720c3b205be910", "authoritative-consumer")]
    public void AttachedTacticsDiscardSegmentBindsToTheSharedDiscardOutlet()
    {
        var rescan = AllAbilities().Where(ability => ability.CardId == "S02-0013"
            && ability.Trigger == "host-leaves-artifact");
        AssertClosedSet([EffectLifecycleProfiles.AttachedTacticsDiscardAbilityId], rescan);
    }

    [Fact]
    [L12AbilityEvidence("S02-0305:ability:master-damaged:4c8ce907eed1f778", "authoritative-consumer")]
    public void AnderstorpDamageFloorSegmentBindsToTheReplacementOutlet()
    {
        var rescan = AllAbilities().Where(ability => ability.CardId == "S02-0305"
            && ability.Trigger == "master-damaged" && ability.ExecutionModel == "replacement");
        var segments = AssertClosedSet([EffectLifecycleProfiles.AnderstorpDamageFloorAbilityId], rescan);
        Assert.Contains("第一次伤害变为2", segments[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    [L12AbilityEvidence("S02-06S3:ability:static:f7e019a543066afd", "authoritative-consumer")]
    [L12AbilityEvidence("S02-06S3:ability:death:84330d935c195208", "authoritative-consumer")]
    public void LakeLadySwordSegmentsBindToTheSwordReplacementPath()
    {
        var rescan = AllAbilities().Where(ability => ability.CardId == "S02-06S3"
            && ((ability.Trigger == "static" && ability.Text.StartsWith("持续", StringComparison.Ordinal))
                || ability.Trigger == "death"));
        var segments = AssertClosedSet(EffectLifecycleProfiles.LakeLadySwordAbilityIds, rescan);
        Assert.All(segments, ability => Assert.Contains("王者之剑", ability.Text, StringComparison.Ordinal));
    }

    [Fact]
    [L12AbilityEvidence("S02-06D1:ability:static:b173428fa383ae26", "authoritative-consumer")]
    [L12AbilityEvidence("S02-06M2:ability:rule:f86cd3914a10b001", "authoritative-consumer")]
    public void TrialCapacitySegmentsBindToTheDeckRuleAndValidator()
    {
        var rescan = AllAbilities().Where(ability =>
            (ability.CardId == "S02-06D1" && ability.Trigger == "static"
                && ability.Text.Contains("可携带", StringComparison.Ordinal))
            || (ability.CardId == "S02-06M2" && ability.Trigger == "rule"));
        AssertClosedSet(EffectLifecycleProfiles.TrialCapacityAbilityIds, rescan);
    }

    [Fact]
    [L12AbilityEvidence("S02-0016:ability:granted:dfd998389876f15e", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0016:ability:granted:df2c369f365d4497", "authoritative-consumer")]
    public void RuinedRitualGrantedModesBindToTheCounterResponseOutlets()
    {
        var rescan = AllAbilities().Where(ability => ability.CardId == "S02-0016" && ability.Trigger == "granted");
        AssertClosedSet(EffectLifecycleProfiles.RuinedRitualModesAbilityIds, rescan);
    }

    [Fact]
    [L12AbilityEvidence("S02-0012:ability:granted:e5bb0cce96aba072", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0012:ability:granted:1c5ef0343f70615c", "authoritative-consumer")]
    public void PrayerGrantedModesBindToTheUniversalTacticOutlets()
    {
        var rescan = AllAbilities().Where(ability => ability.CardId == "S02-0012" && ability.Trigger == "granted");
        AssertClosedSet(EffectLifecycleProfiles.PrayerModesAbilityIds, rescan);
    }

    [Fact]
    [L12AbilityEvidence("S02-0406:ability:granted:6aa04cbf27f6b4b7", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0406:ability:granted:4f1f5a1d4791b5ef", "authoritative-consumer")]
    [L12AbilityEvidence("S02-0406:ability:granted:4f26e688b66affd4", "authoritative-consumer")]
    public void TenkaGrantedModesBindToTheFactionTacticOutlets()
    {
        var rescan = AllAbilities().Where(ability => ability.CardId == "S02-0406" && ability.Trigger == "granted");
        AssertClosedSet(EffectLifecycleProfiles.TenkaModesAbilityIds, rescan);
    }

    [Fact]
    [L12AbilityEvidence("S02-0602:ability:granted:7a7545729484412a", "authoritative-consumer")]
    public void LancelotKillGrantedModeBindsToTheTrialAdvanceOutlet()
    {
        // 同卡「获得1符文」granted 段属 granted 符文族，不入本档案。
        var rescan = AllAbilities().Where(ability => ability.CardId == "S02-0602"
            && ability.Trigger == "granted" && ability.ExecutionModel == "granted-effect"
            && !ability.Text.Contains("获得1符文", StringComparison.Ordinal));
        AssertClosedSet([EffectLifecycleProfiles.LancelotKillModesAbilityId], rescan);
    }

    [Fact]
    [L12AbilityEvidence("S02-0614:ability:granted:45f31f84b8f800cd", "authoritative-consumer")]
    public void ConstanceGrantedModeBindsToTheTrialAdvanceOutlet()
    {
        // 同卡「获得1符文」granted 段属 granted 符文族，不入本档案。
        var rescan = AllAbilities().Where(ability => ability.CardId == "S02-0614"
            && ability.Trigger == "granted"
            && !ability.Text.Contains("获得1符文", StringComparison.Ordinal));
        AssertClosedSet([EffectLifecycleProfiles.ConstanceModesAbilityId], rescan);
    }

    [Fact]
    [L12AbilityEvidence("S02-06D1:ability:turn-start:97dca04b36fe51bf", "authoritative-consumer")]
    public void AvalonTurnStartSegmentBindsToTheTrialAdvancePipeline()
    {
        var rescan = AllAbilities().Where(ability => ability.CardId == "S02-06D1" && ability.Trigger == "turn-start");
        var segments = AssertClosedSet([EffectLifecycleProfiles.AvalonTurnStartAbilityId], rescan);
        Assert.Contains("试炼+1并获得1符文", segments[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    [L12AbilityEvidence("S02-06S2:ability:static:0f86ac377c8c63ee", "attached-source-current", "authoritative-consumer")]
    public void KingsSwordAttachedSegmentBindsToTheContinuousTroopsAndStrongAttackOutlets()
    {
        // 王者之剑全池仅此一段；附叠强攻身份由结构语义注册表承载。
        var rescan = AllAbilities().Where(ability => ability.CardId == "S02-06S2");
        var segments = AssertClosedSet([EffectLifecycleProfiles.KingsSwordAttachedAbilityId], rescan);
        Assert.Equal("continuous", segments[0].ExecutionModel);
        Assert.Contains("原本兵力+1000", segments[0].Text, StringComparison.Ordinal);
        Assert.True(L12StructuredCardSemantics.GrantsStrongAttackWhileAttached("S02-06S2"));
        Assert.False(L12StructuredCardSemantics.GrantsStrongAttackWhileAttached("S02-06S3"));
    }

    [Fact]
    [L12AbilityEvidence("S01-0206:ability:static:cb39cf42a7feea7b", "composite-line-declaration", "authoritative-consumer")]
    public void SaladinCompositeLineBindsToTheSharedMoveAndAttackPassiveOutlets()
    {
        // 萨拉丁的 static 印刷行仅此一段（其余为进攻/阵亡触发段）。
        var rescan = AllAbilities().Where(ability => ability.CardId == "S01-0206" && ability.Trigger == "static");
        var segments = AssertClosedSet([EffectLifecycleProfiles.SaladinLineAbilityId], rescan);
        Assert.Contains("可进行1次位移", segments[0].Text, StringComparison.Ordinal);
        Assert.Contains("相邻的【太阳城】军团", segments[0].Text, StringComparison.Ordinal);
    }
    [Fact]
    [L12AbilityEvidence("ST04-07:ability:continuous:8f395636980d57ca", "row-condition-current", "authoritative-consumer")]
    public void CooperativeSupportSegmentBindsToTheSharedSupportValidation()
    {
        var segments = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.ExecutionModel == "continuous"
                && ability.Text.Contains("协防", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal([EffectLifecycleProfiles.CooperativeSupportAbilityId],
            segments.Select(ability => ability.AbilityId).ToArray());
    }
}
