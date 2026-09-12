using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TwelveLegions.Server;

internal sealed record L12EffectPresentationPlan(
    string PlanId,
    string CardId,
    string Trigger,
    IReadOnlyList<L12CompositeEffectSegmentSpec> Segments);

internal sealed record L12EffectPresentationBranch(
    string Flow,
    string Label,
    string DefaultText,
    IReadOnlyDictionary<string, string> RequiredChoices);

internal sealed record L12StandaloneEffectPresentationBranch(
    string CardId,
    string Trigger,
    L12EffectPresentationBranch Branch,
    int? AbilitySequence = null);

/// <summary>
/// 后台动效的分段与公开分支目录。分段来自复合效果权威计划；只有玩家已公开声明的
/// 选项可以出现在 RequiredChoices 中，目标实例、匿名手牌与其他私密数据均不进入目录。
/// </summary>
internal static class L12EffectPresentationVariants
{
    private static readonly IReadOnlyDictionary<string, int> PlanAbilitySequences =
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["active:S01-0105:searchBrothers"] = 2,
            ["active:S01-0116:xishiExchange"] = 2,
            ["active:S01-01D1:palaceReward"] = 1,
            ["active:S01-01D1:palaceExchange"] = 2,
            ["active:S01-01M1:drawCycle"] = 1,
            ["active:S01-02D1:sunTopThree"] = 2,
            ["active:S01-03D1:valhallaRecover"] = 2,
            ["active:S01-04D1:yomiSweep"] = 2,
            ["active:S01-04M1:amaterasuKill"] = 1,
            ["active:S01-04M1:amaterasuReady"] = 2,
            ["active:S02-05D1:divinityRecover"] = 2,
            ["response:S01-0020"] = 1,
            ["response:S01-0120"] = 1,
            ["trigger:S01-0021:reaction"] = 1,
            ["trigger:ST01-10:reaction"] = 1,
            ["response:S02-0016"] = 1,
            ["response:S02-0017"] = 1,
            ["response:S02-0018"] = 1,
            ["wisdom-reward:S01-0224"] = 1,
            ["trigger:S01-0320"] = 1,
            ["trigger:S02-0304:margaretMasterDamage"] = 1,
        });

    private static readonly L12EffectPresentationBranch[] PublicBranches =
    [
        Branch("volley-effect", "对方前排兵力-2000", "对方前排所有军团本回合兵力-2000", "volleyMode", "mode:front"),
        Branch("volley-effect", "对方后排兵力-2000", "对方后排所有军团本回合兵力-2000", "volleyMode", "mode:back"),
        Branch("volley-effect", "单体兵力-4000", "对方1张军团本回合兵力-4000", "volleyMode", "mode:single"),
        Branch("duat-effect", "击杀军团", "击杀对方1张兵力不高于5000的军团", "duatMode", "mode:kill"),
        Branch("duat-effect", "回收卡牌", "墓地最多1张其他【太阳城】卡牌加入手牌", "duatMode", "mode:recover"),
        Branch("hunter-gift", "震击伤害+2000", "所选奥林匹斯军团本回合震击伤害+2000", "mode", "mode:shock"),
        Branch("hunter-gift", "进攻时兵力+2000", "所选奥林匹斯远程军团本回合进攻时兵力+2000", "mode", "mode:ranged"),
        Branch("ruined-ritual", "盲选并弃置手牌", "盲选并弃置对方1张手牌", "mode", "mode:discard"),
        Branch("ruined-ritual", "登场效果无效", "令该军团登场效果无效且本回合兵力-3000", "mode", "mode:suppress"),
        Branch("ritual-disaster", "天灾值-2", "将天灾值减少2点", "disasterValue", "-2"),
        Branch("ritual-disaster", "天灾值-1", "将天灾值减少1点", "disasterValue", "-1"),
        Branch("ritual-disaster", "天灾值不变", "天灾值不变", "disasterValue", "0"),
        Branch("ritual-disaster", "天灾值+1", "将天灾值增加1点", "disasterValue", "1"),
        Branch("ritual-disaster", "天灾值+2", "将天灾值增加2点", "disasterValue", "2"),
        Branch("black-lotus-disaster", "天灾值-1", "将天灾值减少1点", "disasterMode", "-1"),
        Branch("black-lotus-disaster", "天灾值不变", "天灾值不变", "disasterMode", "0"),
        Branch("black-lotus-disaster", "天灾值+1", "将天灾值增加1点", "disasterMode", "1"),
        Branch("tenka-effect", "对方前排费用-2", "对方前排所有军团本回合费用-2", ("mode", "mode:row-cost"), ("row", "row:0")),
        Branch("tenka-effect", "对方后排费用-2", "对方后排所有军团本回合费用-2", ("mode", "mode:row-cost"), ("row", "row:1")),
        Branch("tenka-effect", "前排进攻兵力+1000", "本回合我方前排所有【高天原】军团进攻时兵力+1000", "mode", "mode:front-attack"),
        Branch("tenka-effect", "活跃军团免费位移", "本回合我方所有活跃的【高天原】军团可免费进行1格位移", "mode", "mode:free-move"),
        Branch("desert-transaction", "按0张结算", "不再弃置，按数量0处理", "desertRepeatCount", "count:0"),
        Branch("desert-transaction", "按1张结算", "不再弃置，按数量1处理", "desertRepeatCount", "count:1"),
        Branch("desert-transaction", "按2张结算", "不再弃置，按数量2处理", "desertRepeatCount", "count:2"),
        Branch("desert-transaction", "按3张结算", "不再弃置，按数量3处理", "desertRepeatCount", "count:3"),
    ];

    private static readonly L12StandaloneEffectPresentationBranch[] StandalonePublicBranches =
    [
        new("ST06-04", "enter", Branch("mordred-enter-choice", "获得1符文",
            "莫德雷德使我方获得1符文", "mode", "mode:rune")),
        new("ST06-04", "enter", Branch("mordred-enter-choice", "获得冲锋",
            "莫德雷德本回合获得冲锋", "mode", "mode:charge")),
        new("ST04-M1", "legion-attack-timing", Branch("kagutsuchi-buff", "消耗士气",
            "迦具土消耗1士气：使进攻军团本回合兵力+2000", "mode", "mode:morale")),
        new("ST04-M1", "legion-attack-timing", Branch("kagutsuchi-buff", "弃置手牌",
            "迦具土弃置1张手牌：使进攻军团本回合兵力+2000", "mode", "mode:discard")),
        new("S02-0602", "after-attack", Branch("trial-advance:lancelot-kill", "推进试炼",
            "兰斯洛特推进1点试炼", "mode", "mode:trial")),
        new("S02-0602", "after-attack", Branch("trial-advance:lancelot-kill", "获得符文",
            "兰斯洛特使我方获得1符文", "mode", "mode:rune")),
        new("S02-0614", "enter", Branch("trial-advance:constance-entry", "获得符文",
            "康斯坦丝使我方获得1符文", "mode", "mode:rune")),
        new("S02-0614", "enter", Branch("trial-advance:constance-entry", "推进试炼",
            "康斯坦丝推进1点试炼", "mode", "mode:trial")),
        new("S02-0615", "death", Branch("gwen-choice", "恢复血量",
            "格温莉安使我方主宰增加1点血量", "mode", "mode:heal")),
        new("S02-0615", "death", Branch("gwen-choice", "抽取卡牌",
            "格温莉安使我方抽取1张牌", "mode", "mode:draw")),
        new("S01-02M1", "active", Branch("isis-reward-choice", "抽取1张牌",
            "伊西斯完成卡诺匹斯操作后抽取1张牌", "rewardMode", "mode:draw"), 1),
        new("S01-02M1", "active", Branch("isis-reward-choice", "主宰增加1点血量",
            "伊西斯完成卡诺匹斯操作后使我方主宰增加1点血量", "rewardMode", "mode:heal"), 1),
        new("S02-05D1", "active", Branch("divinity-power", "分配6000兵力伤害",
            "诸神巅对对方军团分配合计6000兵力伤害", "mode", "mode:damage"), 3),
        new("S01-02M3", "active", Branch("medjed-debuff", "兵力-1000",
            "梅杰德使对方1张军团本回合兵力-1000", "mode", "mode:normal"), 1),
        new("S01-02M3", "active", Branch("medjed-debuff", "兵力-3000",
            "梅杰德额外休整陵墓守卫，使对方1张军团本回合兵力-3000", "mode", "mode:strong"), 1),
        new("S02-06C1", "active", Branch("otherworld-rune-use", "试炼+1",
            "彼界阵营符文效果使当前试炼进度+1", "mode", "mode:trial"), 2),
        new("S02-06C1", "active", Branch("otherworld-rune-use", "抽取1张牌",
            "彼界阵营符文效果使我方抽取1张牌", "mode", "mode:draw"), 2),
        new("S02-0603", "active", Branch("merlin-rune", "兵力-3000",
            "梅林使对方1张军团本回合兵力-3000", "mode", "mode:debuff"), 3),
        new("S02-0603", "active", Branch("merlin-rune", "检索主动战术",
            "梅林检索1张费用不高于4的主动战术，展示后加入手牌", "mode", "mode:search"), 3),
        new("ST06-09", "active", Branch("light-sword-active", "军团兵力+2000",
            "光之剑使我方前排1张【彼界】军团本回合兵力+2000", "mode", "mode:buff"), 2),
        new("ST06-09", "active", Branch("light-sword-active", "获得1符文",
            "光之剑使我方获得1符文", "mode", "mode:rune"), 2),
        new("S02-0604", "active", Branch("galahad-grail-reward", "仅抽取1张牌",
            "加拉哈德抽取1张牌", "healMode", "mode:none"), 3),
        new("S02-0604", "active", Branch("galahad-grail-reward", "抽牌并回复血量",
            "加拉哈德抽取1张牌，并使我方主宰增加1点血量", "healMode", "mode:heal"), 3),
        new("S01-0016", "response-negate", Branch("absolute-defense-response", "抵挡本次进攻",
            "抵挡本次进攻", "mode", "mode:block"), 1),
        new("S01-0016", "response-negate", Branch("absolute-defense-response", "无效该效果",
            "无效该效果", "mode", "mode:negate"), 1),
        new("S01-0017", "reaction", Branch("last-stand-response", "单体兵力-2000",
            "选择对方1张休整的军团，直到下个我方回合结束前兵力-2000", "mode", "mode:single"), 1),
        new("S01-0017", "reaction", Branch("last-stand-response", "全部休整军团兵力-1000",
            "对方所有休整的军团，直到下个我方回合结束前兵力-1000", "mode", "mode:all"), 1),
        .. SkyCityBranches(),
    ];

    internal static IReadOnlyList<L12EffectPresentationBranch> PublicBranchDefinitions => PublicBranches;
    internal static IReadOnlyList<L12StandaloneEffectPresentationBranch> StandaloneBranchDefinitions
        => StandalonePublicBranches;

    internal static L12AtomicAbility[] Attach(L12AtomicAbility[] abilities)
    {
        if (abilities.Length == 0) return abilities;
        var cardId = abilities[0].CardId;
        var plans = L12CompositeEffectPlans.PresentationPlansForCard(cardId).ToArray();
        var standalone = StandalonePublicBranches.Where(branch => branch.CardId.Equals(cardId,
            StringComparison.OrdinalIgnoreCase)).ToArray();
        if (plans.Length == 0 && standalone.Length == 0) return abilities;

        var result = abilities.ToArray();
        foreach (var plan in plans)
        {
            var ownerIndex = ResolveOwnerIndex(result, plan);

            var owner = result[ownerIndex];
            var additions = BuildPlanScenes(owner, plan).ToArray();
            result[ownerIndex] = owner with
            {
                Presentations = owner.Presentations.Concat(additions).ToArray(),
            };
        }

        foreach (var group in standalone.GroupBy(branch =>
                     (Trigger: StandaloneOwnerTrigger(branch).ToLowerInvariant(), branch.AbilitySequence)))
        {
            var owners = result.Select((ability, index) => (ability, index))
                .Where(item => group.Key.AbilitySequence is int sequence
                    ? item.ability.Sequence == sequence
                    : item.ability.Trigger.Equals(group.Key.Trigger, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.index).ToArray();
            if (owners.Length != 1)
                throw new InvalidOperationException(
                    $"独立动效分支 {cardId}/{group.Key.Trigger}/#{group.Key.AbilitySequence} 无法唯一归属能力；"
                    + $"可用能力：{string.Join(", ", result.Select(ability => $"#{ability.Sequence}/{ability.Trigger}"))}");
            var owner = result[owners[0]];
            result[owners[0]] = owner with
            {
                Presentations = owner.Presentations.Concat(group.Select(item =>
                    StandaloneScene(owner, item.Branch))).ToArray(),
            };
        }

        ValidateConfiguration(result.SelectMany(ability => ability.Presentations));
        return result;
    }

    private static string StandaloneOwnerTrigger(L12StandaloneEffectPresentationBranch branch)
        // The runtime combat event is named after-attack, while the printed atomic ability
        // is the more precise after-kill clause.  Presentation ownership follows the printed
        // ability and runtime selection still follows branch.Trigger/presentationFlow.
        => branch.CardId.Equals("S02-0602", StringComparison.OrdinalIgnoreCase)
            && branch.Trigger.Equals("after-attack", StringComparison.OrdinalIgnoreCase)
                ? "after-kill"
                : branch.Trigger;

    private static L12EffectPresentationScene StandaloneScene(L12AtomicAbility ability,
        L12EffectPresentationBranch branch)
    {
        var sceneKey = $"branch:{NormalizeKey(branch.Flow)}:{ChoiceHash(branch.RequiredChoices)}";
        return new L12EffectPresentationScene(
            $"{ability.AbilityId}:presentation:{sceneKey}", ability.CardId, ability.AbilityId,
            sceneKey, branch.DefaultText, EventType: "effect", Label: branch.Label,
            Flow: branch.Flow, BranchLabel: branch.Label, RequiredChoices: branch.RequiredChoices);
    }

    private static int ResolveOwnerIndex(IReadOnlyList<L12AtomicAbility> abilities,
        L12EffectPresentationPlan plan)
    {
        if (PlanAbilitySequences.TryGetValue(plan.PlanId, out var sequence))
        {
            var mapped = abilities.Select((ability, index) => (ability, index))
                .Where(item => item.ability.Sequence == sequence).Select(item => item.index).ToArray();
            if (mapped.Length == 1) return mapped[0];
            throw new InvalidOperationException($"动效计划 {plan.PlanId} 的能力序号 {sequence} 无法唯一定位");
        }

        // HandPlayPlans are card-level programs: when the printed text was split into several
        // atomic clauses, the composite controller is anchored to the first printed clause.
        // This is a declared ownership rule, not an arbitrary fallback.
        if (plan.Trigger == "play" && plan.PlanId.Equals(plan.CardId, StringComparison.OrdinalIgnoreCase))
        {
            var first = abilities.Select((ability, index) => (ability, index))
                .Where(item => item.ability.Sequence == 1).Select(item => item.index).ToArray();
            if (first.Length == 1) return first[0];
            throw new InvalidOperationException($"卡级动效计划 {plan.PlanId} 缺少唯一的首段能力");
        }

        var triggerMatches = abilities.Select((ability, index) => (ability, index))
            .Where(item => item.ability.Trigger.Equals(plan.Trigger, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.index).ToArray();
        if (triggerMatches.Length == 1) return triggerMatches[0];
        if (abilities.Count == 1) return 0;
        throw new InvalidOperationException(
            $"动效计划 {plan.PlanId} 无法按时点 {plan.Trigger} 唯一归属能力，请显式登记能力序号");
    }

    internal static void ValidateConfiguration(IEnumerable<L12EffectPresentationScene> scenes)
    {
        var variants = scenes.Where(scene => scene.Flow is not null).ToArray();
        var duplicateId = variants.GroupBy(scene => scene.SceneId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
            throw new InvalidOperationException($"动效分段配置存在重复 SceneId：{duplicateId.Key}");

        foreach (var group in variants.GroupBy(scene => (scene.AbilityId, scene.Flow, scene.SegmentIndex)))
        {
            var specific = group.Where(scene => scene.RequiredChoices is { Count: > 0 }).ToArray();
            for (var left = 0; left < specific.Length; left++)
            for (var right = left + 1; right < specific.Length; right++)
            {
                if (!ChoicesCanOverlap(specific[left].RequiredChoices!, specific[right].RequiredChoices!)) continue;
                throw new InvalidOperationException(
                    $"动效分支配置可同时命中：{specific[left].SceneId} / {specific[right].SceneId}");
            }
        }
    }

    internal static string SceneKeyPrefix(string planId)
        => $"segment:{NormalizeKey(planId)}:";

    private static IEnumerable<L12EffectPresentationScene> BuildPlanScenes(
        L12AtomicAbility ability, L12EffectPresentationPlan plan)
    {
        for (var index = 0; index < plan.Segments.Count; index++)
        {
            var segment = plan.Segments[index];
            var segmentChoices = segment.RequiredMode is null
                ? null
                : Choices((segment.RequiredDeclarationKey ?? "mode", segment.RequiredMode));
            var branches = PublicBranches.Where(branch => branch.Flow.Equals(segment.Flow,
                StringComparison.OrdinalIgnoreCase)).ToArray();
            if (branches.Length == 0)
                yield return Scene(ability, plan, segment, index, segment.Text,
                    segment.RequiredMode is null ? null : segment.Text, segmentChoices);

            foreach (var branch in branches)
            {
                var merged = MergeChoices(segmentChoices, branch.RequiredChoices);
                yield return Scene(ability, plan, segment, index, branch.DefaultText, branch.Label, merged);
            }
        }
    }

    private static L12EffectPresentationScene Scene(L12AtomicAbility ability, L12EffectPresentationPlan plan,
        L12CompositeEffectSegmentSpec segment, int index, string text, string? branchLabel,
        IReadOnlyDictionary<string, string>? requiredChoices)
    {
        var requirements = requiredChoices is { Count: > 0 } ? requiredChoices : null;
        var branchKey = requirements is null ? string.Empty : $":branch-{ChoiceHash(requirements)}";
        var sceneKey = $"{SceneKeyPrefix(plan.PlanId)}{NormalizeKey(segment.Flow)}{branchKey}";
        return new L12EffectPresentationScene(
            $"{ability.AbilityId}:presentation:{sceneKey}", ability.CardId, ability.AbilityId,
            sceneKey, text, EventType: "effect",
            Label: $"第{index + 1}/{plan.Segments.Count}段 {branchLabel ?? segment.Text}",
            Flow: segment.Flow, SegmentIndex: index + 1, SegmentCount: plan.Segments.Count,
            BranchLabel: branchLabel, RequiredChoices: requirements);
    }

    private static bool ChoicesCanOverlap(IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
        => !left.Any(pair => right.TryGetValue(pair.Key, out var value)
            && !value.Equals(pair.Value, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, string> MergeChoices(
        IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string> right)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (left is not null)
            foreach (var pair in left) merged[pair.Key] = pair.Value;
        foreach (var pair in right) merged[pair.Key] = pair.Value;
        return new ReadOnlyDictionary<string, string>(merged);
    }

    private static L12EffectPresentationBranch Branch(string flow, string label, string text,
        string key, string value) => Branch(flow, label, text, (key, value));

    private static L12EffectPresentationBranch Branch(string flow, string label, string text,
        params (string Key, string Value)[] choices)
        => new(flow, label, text, Choices(choices));

    private static L12StandaloneEffectPresentationBranch[] SkyCityBranches()
    {
        var result = new List<L12StandaloneEffectPresentationBranch>();
        for (var mask = 1; mask < 8; mask++)
        {
            var runes = (mask & 1) != 0;
            var heal = (mask & 2) != 0;
            var draw = (mask & 4) != 0;
            var labels = new[] { runes ? "获得2符文" : null, heal ? "主宰增加2点血量" : null,
                    draw ? "抽取1张牌" : null }
                .Where(value => value is not null).Cast<string>().ToArray();
            result.Add(new("ST06-S1", "trial-complete", Branch("sky-city-completion",
                string.Join("、", labels), string.Join("；", labels),
                ("runeMode", runes ? "mode:use" : "mode:none"),
                ("healMode", heal ? "mode:use" : "mode:none"),
                ("drawMode", draw ? "mode:use" : "mode:none"))));
        }
        return result.ToArray();
    }

    private static IReadOnlyDictionary<string, string> Choices(
        params (string Key, string Value)[] values)
        => new ReadOnlyDictionary<string, string>(values.ToDictionary(pair => pair.Key,
            pair => pair.Value, StringComparer.OrdinalIgnoreCase));

    private static string ChoiceHash(IReadOnlyDictionary<string, string> choices)
    {
        var canonical = string.Join('|', choices.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key.ToLowerInvariant()}={pair.Value.ToLowerInvariant()}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..12].ToLowerInvariant();
    }

    private static string NormalizeKey(string value)
    {
        var normalized = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "effect" : normalized;
    }
}

internal static partial class L12CompositeEffectPlans
{
    private static readonly IReadOnlyDictionary<string, (string CardId, string Trigger)> StarterHandPlayOwners =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["starter-tomb-guard-revive"] = ("ST02-10", "play"),
            ["legendary-bloodline"] = ("ST03-10", "play"),
            ["invasion-fire"] = ("ST04-10", "play"),
            ["hunter-gift"] = ("ST05-10", "play"),
        };

    private static readonly IReadOnlyDictionary<string, (string CardId, string Trigger)> StarterContinuationOwners =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["starter-aeneas-promotion"] = ("ST05-01", "promotion-enter"),
            ["starter-athena-active"] = ("ST05-M1", "active"),
        };

    internal static IReadOnlyList<L12EffectPresentationPlan> PresentationPlansForCard(string cardId)
        => AllPresentationPlans().Where(plan => plan.CardId.Equals(cardId,
            StringComparison.OrdinalIgnoreCase)).ToArray();

    internal static IReadOnlyList<L12EffectPresentationPlan> AllPresentationPlans()
    {
        var plans = new List<L12EffectPresentationPlan>();
        plans.AddRange(HandPlayPlans.Select(pair =>
            new L12EffectPresentationPlan(pair.Key, pair.Key, "play", pair.Value)));
        plans.AddRange(ActivePlans.Select(pair =>
            new L12EffectPresentationPlan(pair.Key, PresentationPlanCardId(pair.Key), "active", pair.Value)));
        plans.AddRange(ResponseAndTriggerPlans.Select(pair =>
        {
            var cardId = PresentationPlanCardId(pair.Key);
            return new L12EffectPresentationPlan(pair.Key, cardId,
                PresentationTriggerForPlan(pair.Key, cardId), pair.Value);
        }));
        foreach (var (planId, segments) in StarterHandPlayPlans)
        {
            if (!StarterHandPlayOwners.TryGetValue(planId, out var owner))
                throw new InvalidOperationException($"起始牌组手牌计划 {planId} 缺少卡牌归属");
            plans.Add(new(planId, owner.CardId, owner.Trigger, segments));
        }
        foreach (var (planId, segments) in StarterContinuationPlans)
        {
            if (!StarterContinuationOwners.TryGetValue(planId, out var owner))
                throw new InvalidOperationException($"起始牌组续段计划 {planId} 缺少卡牌归属");
            plans.Add(new(planId, owner.CardId, owner.Trigger, segments));
        }
        return plans;
    }

    private static string PresentationPlanCardId(string planId)
    {
        var parts = planId.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.FirstOrDefault(part => part.StartsWith("S", StringComparison.OrdinalIgnoreCase)) ?? planId;
    }

    private static string PresentationTriggerForPlan(string planId, string cardId)
    {
        if (planId.StartsWith("response:", StringComparison.OrdinalIgnoreCase))
            return cardId.StartsWith("S02-", StringComparison.OrdinalIgnoreCase) ? "s2-reaction" : "reaction";
        if (planId.StartsWith("wisdom-reward:", StringComparison.OrdinalIgnoreCase)) return "reaction";
        var parts = planId.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && planId.StartsWith("trigger:", StringComparison.OrdinalIgnoreCase)) return parts[^1];
        return "reaction";
    }
}
