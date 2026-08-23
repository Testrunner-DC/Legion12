using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace TwelveLegions.Server;

public static class L12AtomKinds
{
    public const string Trigger = "trigger.observe";
    public const string Condition = "condition.expression";
    public const string Optional = "control.optional";
    public const string SelectTarget = "selection.target";
    public const string SelectMode = "selection.mode";
    public const string PayMorale = "cost.pay-morale";
    public const string ReturnMorale = "cost.return-morale";
    public const string RestSource = "cost.rest-source";
    public const string Discard = "cost.discard";
    public const string DamageMaster = "operation.damage-master";
    public const string HealMaster = "operation.heal-master";
    public const string Draw = "operation.draw";
    public const string AddMorale = "operation.add-morale";
    public const string ModifyTroops = "operation.modify-troops";
    public const string MoveZone = "operation.move-zone";
    public const string Ready = "operation.ready";
    public const string Rest = "operation.rest";
    public const string Move = "operation.move";
    public const string AttackRule = "operation.attack-rule";
    public const string Keyword = "operation.keyword";
    public const string Shuffle = "operation.shuffle";
    public const string Duration = "duration.apply";
    public const string Visibility = "visibility.policy";
    public const string Special = "special.domain";
    public const string SetState = "operation.set-state";
    public const string Legacy = "legacy.resolve";
}

public sealed record L12EffectAtomDescriptor(
    string Kind,
    string Category,
    string Label,
    string Description,
    bool RuntimeExecutable,
    string KernelContract);

public sealed record L12EffectAtom(
    string AtomId,
    string Kind,
    string Label,
    int Order,
    IReadOnlyDictionary<string, string> Parameters,
    bool RuntimeExecutable,
    string Source,
    string Stage = "resolution");

public sealed record L12AtomicAbility(
    string AbilityId,
    string CardId,
    int Sequence,
    string Text,
    string Trigger,
    IReadOnlyList<L12EffectAtom> Atoms,
    string MigrationStatus,
    bool HasLegacyFallback,
    string MappingSource,
    decimal Confidence,
    string ExecutionModel = "legacy",
    string ReviewStatus = "unreviewed",
    string ReviewSource = "automatic");

public sealed record L12AtomicCardEffect(
    string CardId,
    string Name,
    string Product,
    string Faction,
    string CardType,
    string? ImageUrl,
    string EffectText,
    IReadOnlyList<L12AtomicAbility> Abilities,
    string MigrationStatus,
    int AtomCount,
    int ExecutableAtomCount,
    int LegacyAtomCount,
    string[] AtomKinds,
    string ReviewStatus = "unreviewed",
    string ReviewSource = "automatic");

public sealed record L12AtomicCoverage(
    int TotalCards,
    int CardsWithText,
    int TotalAbilities,
    int TotalAtoms,
    int DeclarativeReadyAbilities,
    int VerifiedAbilities,
    int LegacyBackedAbilities,
    IReadOnlyDictionary<string, int> ByStatus,
    IReadOnlyDictionary<string, int> ByAtomKind);

public sealed record L12AtomicEffectPage(
    IReadOnlyList<L12AtomicCardEffect> Items,
    int Total,
    int Page,
    int PageSize,
    L12AtomicCoverage Coverage);

/// <summary>
/// 卡效原子注册表是规则内核和后台可视化共用的唯一词典。新增原子必须先在这里登记，
/// 禁止前端自行猜测颜色、分类或执行能力。
/// </summary>
public static class L12EffectAtomRegistry
{
    private static readonly IReadOnlyDictionary<string, L12EffectAtomDescriptor> Items =
        new ReadOnlyDictionary<string, L12EffectAtomDescriptor>(new Dictionary<string, L12EffectAtomDescriptor>(StringComparer.Ordinal)
        {
            [L12AtomKinds.Trigger] = new(L12AtomKinds.Trigger, "时机", "监听时点", "只响应规则内核发布的权威时点。", true, "AuthorityEvent / TriggerBatch"),
            [L12AtomKinds.Condition] = new(L12AtomKinds.Condition, "校验", "检查条件", "在声明和结算时重新检查合法条件。", true, "PendingActivation validation"),
            [L12AtomKinds.Optional] = new(L12AtomKinds.Optional, "流程", "可选发动", "建立是/否选择；选择后立即确认。", true, "Prompt(option)"),
            [L12AtomKinds.SelectTarget] = new(L12AtomKinds.SelectTarget, "选择", "选择对象", "按区域、阵营、类型、数量和数值筛选合法对象。", true, "PendingActivation target declaration"),
            [L12AtomKinds.SelectMode] = new(L12AtomKinds.SelectMode, "选择", "选择模式", "从完整效果文本按钮中选择一个模式。", true, "PendingActivation mode declaration"),
            [L12AtomKinds.PayMorale] = new(L12AtomKinds.PayMorale, "费用", "支付士气", "消耗活跃士气、神力或规则允许的替代资源。", true, "ResourcePayment"),
            [L12AtomKinds.ReturnMorale] = new(L12AtomKinds.ReturnMorale, "费用", "返还士气", "玩家选择具体士气并返还士气牌库。", true, "ResourceReturn"),
            [L12AtomKinds.RestSource] = new(L12AtomKinds.RestSource, "费用", "休整来源", "将能力来源由活跃转为休整。", true, "RestCard"),
            [L12AtomKinds.Discard] = new(L12AtomKinds.Discard, "费用", "弃置卡牌", "以明确的弃置原因移动卡牌，不视为阵亡。", true, "ZoneMove(Discard)"),
            [L12AtomKinds.DamageMaster] = new(L12AtomKinds.DamageMaster, "结算", "主宰受伤", "对指定主宰造成伤害并保留来源语义。", true, "DamageMaster"),
            [L12AtomKinds.HealMaster] = new(L12AtomKinds.HealMaster, "结算", "恢复生命", "恢复主宰生命且不超过上限。", true, "HealMaster"),
            [L12AtomKinds.Draw] = new(L12AtomKinds.Draw, "牌库", "抽牌", "从牌库顶抽牌；空牌库按规则判负。", true, "LibraryOps.Draw"),
            [L12AtomKinds.AddMorale] = new(L12AtomKinds.AddMorale, "结算", "追加士气", "从士气牌库追加活跃或休整士气。", true, "MoraleOps.Add"),
            [L12AtomKinds.ModifyTroops] = new(L12AtomKinds.ModifyTroops, "数值", "修改兵力", "通过派生兵力层叠加临时、持续或设定值修正。", true, "DerivedStats"),
            [L12AtomKinds.MoveZone] = new(L12AtomKinds.MoveZone, "区域", "移动区域", "在手牌、牌库、墓地、战场、圣物区、额外区和移出区之间移动。", true, "ZoneMove"),
            [L12AtomKinds.Ready] = new(L12AtomKinds.Ready, "状态", "转为活跃", "使合法对象转为活跃。", true, "ReadyCardByEffect"),
            [L12AtomKinds.Rest] = new(L12AtomKinds.Rest, "状态", "转为休整", "使合法对象转为休整。", true, "RestCard"),
            [L12AtomKinds.Move] = new(L12AtomKinds.Move, "战场", "位移", "执行通常移动、骑兵位移或效果位移。", true, "FieldMovement"),
            [L12AtomKinds.AttackRule] = new(L12AtomKinds.AttackRule, "战斗", "修改进攻规则", "修改距离、进攻损失、目标或伤害。", true, "CombatKernel"),
            [L12AtomKinds.Keyword] = new(L12AtomKinds.Keyword, "状态", "赋予关键字", "赋予强攻、必中、冲锋、免死等有期限状态。", true, "DerivedStatus"),
            [L12AtomKinds.Shuffle] = new(L12AtomKinds.Shuffle, "牌库", "洗牌", "对指定牌库执行可复盘的确定性洗牌。", true, "LibraryOps.Shuffle"),
            [L12AtomKinds.Duration] = new(L12AtomKinds.Duration, "期限", "设置期限", "明确本次进攻、本回合、本局或持续生效期限。", true, "TimedModifier"),
            [L12AtomKinds.Visibility] = new(L12AtomKinds.Visibility, "信息", "可见性", "区分公开、仅控制者、双方分别确认及裁判可见信息。", true, "Snapshot visibility"),
            [L12AtomKinds.Special] = new(L12AtomKinds.Special, "专属机制", "特殊区域/规则", "晋升、神力、试炼、符文、卡诺匹斯、陵墓守卫或天灾专属操作。", false, "Specialized rule kernel"),
            [L12AtomKinds.SetState] = new(L12AtomKinds.SetState, "状态", "设置规则状态", "写入由规则内核定义、具有明确生命周期的权威状态。", true, "Authoritative state mutation"),
            [L12AtomKinds.Legacy] = new(L12AtomKinds.Legacy, "迁移", "旧实现兜底", "尚未由可执行原子完全覆盖的语义，继续调用现有权威实现且不得重复结算。", false, "Legacy effect branch"),
        });

    public static IReadOnlyCollection<L12EffectAtomDescriptor> All => Items.Values.ToArray();
    public static L12EffectAtomDescriptor Get(string kind) => Items[kind];
    public static bool TryGet(string kind, out L12EffectAtomDescriptor descriptor) => Items.TryGetValue(kind, out descriptor!);
}

/// <summary>
/// 将卡面原文分解为稳定的可视化能力与原子序列。它只做保守映射：无法唯一确定的语义
/// 一律保留 legacy.resolve，绝不凭关键词直接改变实战状态。
/// </summary>
public sealed class L12AtomicEffectCatalog
{
    private static readonly string[] TimingTokens =
    [
        "登场时", "阵亡时", "离场时", "进攻时", "进攻后", "受到伤害时", "造成伤害时", "回合开始时",
        "回合结束时", "天灾触发时", "主动休整", "主动翻回正面", "主动", "持续", "我方回合", "对方回合", "盖伏", "反击"
    ];

    private readonly IReadOnlyDictionary<string, L12AtomicCardEffect> _cards;

    private L12AtomicEffectCatalog(IReadOnlyDictionary<string, L12AtomicCardEffect> cards) => _cards = cards;

    public static L12AtomicEffectCatalog Build(IEnumerable<L12CardDefinition> cards)
    {
        var mapped = cards.OrderBy(card => card.Id, StringComparer.OrdinalIgnoreCase)
            .Select(BuildCard)
            .ToDictionary(card => card.CardId, StringComparer.OrdinalIgnoreCase);
        return new L12AtomicEffectCatalog(mapped);
    }

    public L12AtomicCardEffect? Find(string cardId) => _cards.GetValueOrDefault(cardId);
    public IReadOnlyCollection<L12AtomicCardEffect> All => _cards.Values.ToArray();

    public L12AtomicEffectPage Query(string? search, string? status, string? product, string? atomKind, int page, int pageSize)
    {
        var query = _cards.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(card => card.CardId.Contains(search, StringComparison.OrdinalIgnoreCase)
                || card.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || card.EffectText.Contains(search, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(card => card.MigrationStatus == status);
        if (!string.IsNullOrWhiteSpace(product)) query = query.Where(card => card.Product.Equals(product, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(atomKind)) query = query.Where(card => card.AtomKinds.Contains(atomKind, StringComparer.Ordinal));
        var filtered = query.OrderBy(card => card.CardId, StringComparer.OrdinalIgnoreCase).ToArray();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);
        return new L12AtomicEffectPage(filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), filtered.Length, page, pageSize, Coverage());
    }

    public L12AtomicCoverage Coverage()
    {
        var abilities = _cards.Values.SelectMany(card => card.Abilities).ToArray();
        var atoms = abilities.SelectMany(ability => ability.Atoms).ToArray();
        return new L12AtomicCoverage(
            _cards.Count,
            _cards.Values.Count(card => !string.IsNullOrWhiteSpace(card.EffectText)),
            abilities.Length,
            atoms.Length,
            abilities.Count(ability => ability.MigrationStatus == "declarative-ready"),
            abilities.Count(ability => ability.MigrationStatus == "verified"),
            abilities.Count(ability => ability.HasLegacyFallback),
            new ReadOnlyDictionary<string, int>(_cards.Values.GroupBy(card => card.MigrationStatus)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)),
            new ReadOnlyDictionary<string, int>(atoms.GroupBy(atom => atom.Kind)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)));
    }

    private static L12AtomicCardEffect BuildCard(L12CardDefinition card)
    {
        var text = card.Effect?.Trim() ?? string.Empty;
        var abilities = L12StructuredCardRules.TryGetStructuredAbilities(card, out var structured)
            ? structured.Select((ability, index) => BuildStructuredAbility(card, ability, index + 1)).ToArray()
            : SplitAbilities(text).Select((clause, index) => BuildAbility(card, clause, index + 1)).ToArray();
        var legacy = abilities.Sum(ability => ability.Atoms.Count(atom => atom.Kind == L12AtomKinds.Legacy));
        var executable = abilities.Sum(ability => ability.Atoms.Count(atom => atom.RuntimeExecutable));
        var atomCount = abilities.Sum(ability => ability.Atoms.Count);
        var status = string.IsNullOrWhiteSpace(text) ? "no-effect"
            : abilities.Length > 0 && abilities.All(ability => ability.MigrationStatus == "verified") ? "verified"
            : abilities.Any(ability => ability.MigrationStatus == "verified") ? "partially-atomized"
            : legacy == 0 ? "declarative-ready"
            : executable == 0 ? "legacy-backed"
            : "partially-atomized";
        var reviewStatus = abilities.Any(ability => ability.ReviewStatus == "confirmed") ? "confirmed"
            : abilities.Any(ability => ability.ReviewStatus == "human-assisted") ? "human-assisted"
            : "unreviewed";
        var reviewSource = string.Join("+", abilities.Select(ability => ability.ReviewSource)
            .Where(source => source != "automatic").Distinct(StringComparer.Ordinal));
        return new L12AtomicCardEffect(card.Id, card.NameZh, card.Product, card.Faction, card.CardType, card.ImageUrl,
            text, abilities, status, atomCount, executable, legacy,
            abilities.SelectMany(ability => ability.Atoms).Select(atom => atom.Kind).Distinct(StringComparer.Ordinal).Order().ToArray(),
            reviewStatus, string.IsNullOrEmpty(reviewSource) ? "automatic" : reviewSource);
    }

    private static L12AtomicAbility BuildStructuredAbility(
        L12CardDefinition card, L12StructuredAbilityTemplate template, int sequence)
    {
        if (L12VerifiedAtomicPrograms.Find(card.Id, template.Trigger) is { } verified)
            return verified.ToAbility(card, template.Text, sequence) with
            {
                ExecutionModel = template.ExecutionModel,
                MappingSource = "shared-structured-rule+verified-runtime-program",
                ReviewStatus = template.ReviewStatus,
                ReviewSource = template.ReviewSource,
            };
        var atoms = new List<L12EffectAtom>();
        var triggerDescriptor = L12EffectAtomRegistry.Get(L12AtomKinds.Trigger);
        atoms.Add(new L12EffectAtom("atom-1", L12AtomKinds.Trigger, template.Trigger, 1,
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["timing"] = template.Trigger }),
            triggerDescriptor.RuntimeExecutable, "shared-structured-rule", "trigger"));
        foreach (var source in template.Atoms)
        {
            var descriptor = L12EffectAtomRegistry.Get(source.Kind);
            atoms.Add(new L12EffectAtom($"atom-{atoms.Count + 1}", source.Kind, source.Label, atoms.Count + 1,
                source.Parameters, descriptor.RuntimeExecutable || source.Kind == L12AtomKinds.Special,
                "shared-structured-rule", source.Stage));
        }
        var legacyDescriptor = L12EffectAtomRegistry.Get(L12AtomKinds.Legacy);
        atoms.Add(new L12EffectAtom($"atom-{atoms.Count + 1}", L12AtomKinds.Legacy, "调用现有权威卡效分支", atoms.Count + 1,
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
            {
                ["reason"] = "人工结构已审查，尚未完成逐卡运行时等价迁移",
            }), legacyDescriptor.RuntimeExecutable, "migration-guard", "resolution"));
        return new L12AtomicAbility($"{card.Id}:ability:{sequence}", card.Id, sequence, template.Text,
            template.Trigger, atoms, "partially-atomized", true, "shared-structured-rule+legacy-runtime", 1m, template.ExecutionModel,
            template.ReviewStatus, template.ReviewSource);
    }

    private static string[] SplitAbilities(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var normalized = text.Replace("\r", string.Empty).Replace("\n", "。").Trim();
        var boundary = new Regex(@"登场时|阵亡时|离场时|进攻时|进攻后|击杀时|受到伤害时|造成伤害时|回合开始时|回合结束时|天灾触发时|主动休整|主动翻回正面|主动\s|盖伏|反击", RegexOptions.Compiled);
        var starts = boundary.Matches(normalized).Select(match => match.Index).Where(index => index > 0).Distinct().Order().ToArray();
        var segments = new List<string>();
        var cursor = 0;
        foreach (var start in starts)
        {
            var segment = normalized[cursor..start].Trim(' ', '。', '；', ';');
            if (segment.Length > 0) segments.Add(segment);
            cursor = start;
        }
        var tail = normalized[cursor..].Trim(' ', '。', '；', ';');
        if (tail.Length > 0) segments.Add(tail);
        var chunks = segments.SelectMany(segment => Regex.Split(segment, @"(?<=[。；;])"))
            .Select(chunk => chunk.Trim(' ', '。', '；', ';')).Where(chunk => chunk.Length > 0).ToList();
        if (chunks.Count == 0) return [text];
        var abilities = new List<string>();
        foreach (var chunk in chunks)
        {
            var beginsNewAbility = abilities.Count == 0 || TimingTokens.Any(token => chunk.StartsWith(token, StringComparison.Ordinal))
                || Regex.IsMatch(chunk, @"^(我方|对方).{0,12}(时|回合\d*次)");
            if (beginsNewAbility) abilities.Add(chunk);
            else abilities[^1] = $"{abilities[^1]}。{chunk}";
        }
        return abilities.ToArray();
    }

    private static L12AtomicAbility BuildAbility(L12CardDefinition card, string text, int sequence)
    {
        var trigger = DetectTrigger(card, text);
        if (L12VerifiedAtomicPrograms.Find(card.Id, trigger) is { } verified)
            return verified.ToAbility(card, text, sequence);
        var atoms = new List<L12EffectAtom>();
        Add(atoms, L12AtomKinds.Trigger, trigger, new() { ["timing"] = trigger }, "inferred");
        if (text.Contains('可')) Add(atoms, L12AtomKinds.Optional, "可选择发动", new(), "inferred");
        if (ContainsAny(text, "若", "当", "只要", "不高于", "高于", "以下", "以上", "且", "没有", "存在"))
            Add(atoms, L12AtomKinds.Condition, ExtractCondition(text), new() { ["expression"] = ExtractCondition(text) }, "inferred");
        if (ContainsAny(text, "选择", "选取"))
            Add(atoms, L12AtomKinds.SelectTarget, "声明合法对象", new() { ["text"] = text }, "inferred");
        if (ContainsAny(text, "选择以下", "选择1项", "选择一项", "或：", "或使"))
            Add(atoms, L12AtomKinds.SelectMode, "选择效果模式", new() { ["text"] = text }, "inferred");
        if (text.Contains("消耗") && text.Contains("士气")) AddNumeric(atoms, L12AtomKinds.PayMorale, text, "支付士气");
        if (text.Contains("返还") && text.Contains("士气")) AddNumeric(atoms, L12AtomKinds.ReturnMorale, text, "返还士气");
        if (text.Contains("主动休整")) Add(atoms, L12AtomKinds.RestSource, "休整能力来源", new(), "inferred");
        if (text.Contains("弃置")) AddNumeric(atoms, L12AtomKinds.Discard, text, "弃置卡牌");
        if (text.Contains("抽") && text.Contains("牌")) AddNumeric(atoms, L12AtomKinds.Draw, text, "抽牌");
        if (ContainsAny(text, "受到伤害", "造成伤害", "对主宰造成")) AddNumeric(atoms, L12AtomKinds.DamageMaster, text, "主宰受到伤害");
        if (ContainsAny(text, "恢复", "生命+")) AddNumeric(atoms, L12AtomKinds.HealMaster, text, "恢复生命");
        if (text.Contains("追加") && text.Contains("士气")) AddNumeric(atoms, L12AtomKinds.AddMorale, text, "追加士气");
        if (text.Contains("兵力") && ContainsAny(text, "+", "-", "设为", "变为")) AddNumeric(atoms, L12AtomKinds.ModifyTroops, text, "修改兵力");
        if (ContainsAny(text, "置入墓地", "加入手牌", "返回牌库", "放回牌库", "登场", "移出游戏"))
            Add(atoms, L12AtomKinds.MoveZone, "移动卡牌区域", new() { ["text"] = text }, "inferred");
        if (text.Contains("活跃")) Add(atoms, L12AtomKinds.Ready, "转为活跃", new(), "inferred");
        if (text.Contains("休整") && !text.Contains("主动休整")) Add(atoms, L12AtomKinds.Rest, "转为休整", new(), "inferred");
        if (text.Contains("位移")) Add(atoms, L12AtomKinds.Move, "执行位移", new() { ["text"] = text }, "inferred");
        if (ContainsAny(text, "进攻距离", "进攻无损", "远程进攻", "进攻目标", "抵挡"))
            Add(atoms, L12AtomKinds.AttackRule, "修改进攻规则", new() { ["text"] = text }, "inferred");
        foreach (var keyword in new[] { "强攻", "必中", "冲锋", "免死", "隐匿", "召唤失调" }.Where(text.Contains))
            Add(atoms, L12AtomKinds.Keyword, $"赋予/检查{keyword}", new() { ["keyword"] = keyword }, "inferred");
        if (text.Contains("洗牌")) Add(atoms, L12AtomKinds.Shuffle, "洗牌", new(), "inferred");
        if (ContainsAny(text, "本次进攻", "本回合", "下个回合", "本局"))
            Add(atoms, L12AtomKinds.Duration, "应用效果期限", new() { ["duration"] = ExtractDuration(text) }, "inferred");
        if (ContainsAny(text, "展示", "查看", "公开", "对手"))
            Add(atoms, L12AtomKinds.Visibility, "应用信息可见性", new() { ["text"] = text }, "inferred");
        if (card.CardType is "disaster" or "destruction" or "trial" || ContainsAny(text, "晋升", "神力", "试炼", "符文", "卡诺匹斯", "陵墓守卫", "天灾"))
            Add(atoms, L12AtomKinds.Special, "进入专属规则内核", new() { ["domain"] = DetectDomain(card, text) }, "inferred");

        var operational = atoms.Any(atom => atom.Kind.StartsWith("operation.", StringComparison.Ordinal)
            || atom.Kind.StartsWith("cost.", StringComparison.Ordinal) || atom.Kind == L12AtomKinds.Special);
        var needsLegacy = text.Length > 0 && (!operational || atoms.Any(atom => !atom.RuntimeExecutable));
        if (needsLegacy)
            Add(atoms, L12AtomKinds.Legacy, "调用现有权威卡效分支", new() { ["reason"] = "尚未通过逐卡等价回归" }, "migration-guard");
        var status = needsLegacy ? (atoms.Count > 2 ? "partially-atomized" : "legacy-backed") : "declarative-ready";
        return new L12AtomicAbility($"{card.Id}:ability:{sequence}", card.Id, sequence, text, trigger, atoms,
            status, needsLegacy, "card-text+registry", needsLegacy ? 0.70m : 0.90m,
            ExecutionModelFor(trigger, text));
    }

    private static void Add(List<L12EffectAtom> atoms, string kind, string label, Dictionary<string, string> parameters, string source)
    {
        var descriptor = L12EffectAtomRegistry.Get(kind);
        atoms.Add(new L12EffectAtom($"atom-{atoms.Count + 1}", kind, label, atoms.Count + 1,
            new ReadOnlyDictionary<string, string>(parameters), descriptor.RuntimeExecutable, source, StageFor(kind)));
    }

    private static void AddNumeric(List<L12EffectAtom> atoms, string kind, string text, string label)
    {
        var pattern = kind switch
        {
            L12AtomKinds.PayMorale => @"消耗\s*(?<value>\d+)\s*张?士气",
            L12AtomKinds.ReturnMorale => @"返还\s*(?<value>\d+)\s*张?士气",
            L12AtomKinds.Draw => @"抽(?:取)?\s*(?<value>\d+)\s*张牌",
            L12AtomKinds.DamageMaster => @"(?:受到|造成|对[^，。；]*造成)\s*(?<value>\d+)\s*点",
            L12AtomKinds.HealMaster => @"(?:恢复\s*(?<value>\d+)\s*点|生命\s*\+\s*(?<value2>\d+))",
            L12AtomKinds.AddMorale => @"追加\s*(?<value>\d+)\s*张?[^，。；]*士气",
            L12AtomKinds.ModifyTroops => @"兵力[^，。；]*?[+-]\s*(?<value>\d+)",
            L12AtomKinds.Discard => @"弃置[^，。；]*?(?<value>\d+)\s*张",
            _ => @"(?!)",
        };
        var match = Regex.Match(text, pattern);
        var value = match.Groups["value"].Success ? match.Groups["value"].Value : match.Groups["value2"].Value;
        Add(atoms, kind, string.IsNullOrEmpty(value) ? label : $"{label} {value}",
            new() { ["amount"] = string.IsNullOrEmpty(value) ? "dynamic" : value, ["text"] = text }, "inferred");
    }

    private static bool ContainsAny(string value, params string[] tokens) => tokens.Any(value.Contains);
    private static string DetectTrigger(L12CardDefinition card, string text)
        => text.Contains("登场时") ? "enter"
            : text.Contains("阵亡时") ? "death"
            : text.Contains("进攻后") || text.Contains("击杀时") ? "after-attack"
            : text.Contains("进攻时") ? "attack"
            : text.Contains("回合开始时") ? "turn-start"
            : text.Contains("回合结束时") ? "turn-end"
            : text.Contains("主动休整") || text.Contains("主动翻回正面") || Regex.IsMatch(text, @"主动\s") ? "active"
            : card.CardType is "disaster" or "destruction" && text.StartsWith("触发", StringComparison.Ordinal) ? "disaster"
            : card.CardType == "tactic" ? "play"
            : "static";
    private static string ExtractCondition(string text) => text.Length <= 80 ? text : text[..80] + "…";
    private static string ExtractDuration(string text) => new[] { "本次进攻", "本回合", "下个回合", "本局" }.FirstOrDefault(text.Contains) ?? "持续";
    private static string ExecutionModelFor(string trigger, string text)
        => trigger switch
        {
            "enter" or "death" or "after-attack" or "attack" or "turn-start" or "turn-end" or "disaster" => "triggered",
            "active" or "play" => "activated",
            "promotion" => "summon-flow",
            "static" when ContainsAny(text, "作为代替", "代替承受", "代替阵亡") => "replacement",
            "static" => "continuous",
            _ => "legacy",
        };
    private static string StageFor(string kind)
        => kind switch
        {
            L12AtomKinds.Trigger => "trigger",
            L12AtomKinds.Condition or L12AtomKinds.Optional => "condition",
            L12AtomKinds.PayMorale or L12AtomKinds.ReturnMorale or L12AtomKinds.RestSource or L12AtomKinds.Discard => "cost",
            L12AtomKinds.SelectTarget or L12AtomKinds.SelectMode => "target",
            L12AtomKinds.Duration => "duration",
            _ => "resolution",
        };
    private static string DetectDomain(L12CardDefinition card, string text)
        => card.CardType is "disaster" or "destruction" || text.Contains("天灾") ? "disaster"
            : card.CardType == "trial" || text.Contains("试炼") ? "trial"
            : text.Contains("晋升") ? "promotion"
            : text.Contains("神力") ? "god-power"
            : text.Contains("符文") ? "rune"
            : text.Contains("卡诺匹斯") ? "canopic"
            : text.Contains("陵墓守卫") ? "tomb-guard"
            : "special";
}

public sealed record L12VerifiedAtomicProgram(
    string CardId,
    string Trigger,
    IReadOnlyList<L12EffectAtom> Atoms)
{
    public L12AtomicAbility ToAbility(L12CardDefinition card, string text, int sequence)
        => new($"{card.Id}:ability:{sequence}", card.Id, sequence, text, Trigger, Atoms,
            "verified", false, "verified-runtime-program", 1m,
            Trigger is "active" or "play" ? "activated" : Trigger == "static" ? "continuous" : "triggered");
}

/// <summary>
/// 已通过逐项等价回归、并由实战解释器接管的原子程序。后台和实战只能从这里读取，
/// 禁止另建一份仅供展示的“假迁移”配置。
/// </summary>
public static class L12VerifiedAtomicPrograms
{
    private static readonly IReadOnlyDictionary<string, L12VerifiedAtomicProgram> Programs =
        new ReadOnlyDictionary<string, L12VerifiedAtomicProgram>(Build());

    public static IReadOnlyCollection<L12VerifiedAtomicProgram> All => Programs.Values.ToArray();
    public static L12VerifiedAtomicProgram? Find(string cardId, string trigger)
        => Programs.GetValueOrDefault(Key(cardId, trigger));

    private static Dictionary<string, L12VerifiedAtomicProgram> Build()
    {
        var programs = new[]
        {
            Program("S01-0012", "play",
                Atom(L12AtomKinds.SetState, "记录下一张军团的冲锋条件", ("key", "controller.nextLegionChargeMaxCost"), ("value", "6"), ("event", "本回合下一张费用不高于 6 的军团获得冲锋"))),
            Program("S01-0102", "death",
                Atom(L12AtomKinds.Draw, "抽取 1 张牌", ("amount", "1"), ("emptyLossReason", "武则天阵亡效果抽牌时牌库为空")),
                Atom(L12AtomKinds.HealMaster, "我方主宰恢复 1 点生命", ("amount", "1"), ("reason", "武则天的【阵亡时】效果"))),
            Program("S01-0109", "enter",
                Atom(L12AtomKinds.AddMorale, "追加 3 张休整士气", ("amount", "3"), ("tapped", "true"), ("event", "白起从士气牌库追加 {value} 张休整士气"))),
            Program("S01-0117", "enter",
                Atom(L12AtomKinds.AddMorale, "追加 1 张活跃士气", ("amount", "1"), ("tapped", "false"), ("event", "山河社稷图从士气牌库追加 {value} 张活跃士气"))),
            Program("S01-0401", "enter", Atom(L12AtomKinds.Keyword, "获得冲锋", ("keyword", "charge"), ("event", "{source} 获得冲锋"))),
            Program("S01-0404", "enter", Atom(L12AtomKinds.Keyword, "获得冲锋", ("keyword", "charge"), ("event", "{source} 获得冲锋"))),
            Program("S01-0410", "enter", Atom(L12AtomKinds.Keyword, "获得冲锋", ("keyword", "charge"), ("event", "{source} 获得冲锋"))),
            Program("S01-0413", "enter",
                Atom(L12AtomKinds.Condition, "我方手牌不高于 5 张", ("expression", "controller.hand<=5")),
                Atom(L12AtomKinds.Draw, "抽取 1 张牌", ("amount", "1"), ("emptyLossReason", "源博雅效果抽牌时牌库为空"), ("event", "源博雅抽取 1 张牌"))),
            Program("S01-0405", "attack",
                Atom(L12AtomKinds.Condition, "我方手牌数量不高于对方", ("expression", "controller.hand<=opponent.hand")),
                Atom(L12AtomKinds.Draw, "抽取 1 张牌", ("amount", "1"), ("emptyLossReason", "宫本武藏效果抽牌时牌库为空"), ("event", "宫本武藏抽取 1 张牌"))),
            Program("S01-0409", "after-attack",
                Atom(L12AtomKinds.Condition, "本次进攻击杀对象", ("expression", "item.killed=true")),
                Atom(L12AtomKinds.Draw, "抽取 1 张牌", ("amount", "1"), ("emptyLossReason", "源义经击杀效果抽牌时牌库为空"), ("event", "源义经因击杀抽取 1 张牌"))),
            Program("S01-0415", "active",
                Atom(L12AtomKinds.Condition, "服部半藏当前为覆盖状态", ("expression", "source.hidden=true")),
                Atom(L12AtomKinds.SetState, "翻回正面并恢复为军团，不重置原登场回合",
                    ("key", "source.hidden"), ("value", "false"), ("preserveSummonRound", "true"),
                    ("event", "{source} 主动翻回正面，恢复为军团"))),
            Program("S02-0001", "enter",
                Atom(L12AtomKinds.SetState, "对方下个回合主动战术费用 +1", ("key", "opponent.nextActiveTacticSurcharge"), ("operation", "increment"), ("value", "1"), ("event", "对方下个回合从手牌打出的主动战术费用 +1"))),
            Program("S02-0014", "play",
                Atom(L12AtomKinds.Condition, "我方手牌不高于 4 张", ("expression", "controller.hand<=4")),
                Atom(L12AtomKinds.Draw, "抽取 2 张牌", ("amount", "2"), ("emptyLossReason", "〈瞬间的思路〉抽牌时牌库为空"), ("eventType", "draw"), ("event", "〈瞬间的思路〉抽取 2 张牌"))),
            Program("S01-DS02", "disaster",
                Atom(L12AtomKinds.DamageMaster, "双方主宰各受到 1 点非致命伤害", ("amount", "1"), ("target", "both"), ("lethal", "false"), ("neutralSource", "true"), ("reason", "〈百鬼夜行〉"))),
            Program("S02-DS05", "disaster",
                Atom(L12AtomKinds.DamageMaster, "双方主宰各受到 1 点非致命伤害", ("amount", "1"), ("target", "both"), ("lethal", "false"), ("neutralSource", "true"), ("reason", "〈暴怒之罪〉"))),
        };
        return programs.ToDictionary(program => Key(program.CardId, program.Trigger), StringComparer.OrdinalIgnoreCase);
    }

    private static L12VerifiedAtomicProgram Program(string cardId, string trigger, params L12EffectAtom[] operations)
    {
        var atoms = new List<L12EffectAtom>
        {
            Atom(L12AtomKinds.Trigger, trigger, ("timing", trigger))
        };
        atoms.AddRange(operations);
        return new L12VerifiedAtomicProgram(cardId, trigger, atoms.Select((atom, index) => atom with
        {
            AtomId = $"atom-{index + 1}",
            Order = index + 1,
            Stage = atom.Kind switch
            {
                L12AtomKinds.Trigger => "trigger",
                L12AtomKinds.Condition or L12AtomKinds.Optional => "condition",
                L12AtomKinds.PayMorale or L12AtomKinds.ReturnMorale or L12AtomKinds.RestSource or L12AtomKinds.Discard => "cost",
                L12AtomKinds.SelectTarget or L12AtomKinds.SelectMode => "target",
                L12AtomKinds.Duration => "duration",
                _ => "resolution",
            },
        }).ToArray());
    }

    private static L12EffectAtom Atom(string kind, string label, params (string Key, string Value)[] parameters)
    {
        var descriptor = L12EffectAtomRegistry.Get(kind);
        return new L12EffectAtom("", kind, label, 0,
            new ReadOnlyDictionary<string, string>(parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
            descriptor.RuntimeExecutable, "verified-runtime");
    }

    private static string Key(string cardId, string trigger) => $"{cardId}:{trigger}";
}

public interface IL12AtomicRuntime
{
    bool Check(string expression);
    void Execute(L12EffectAtom atom);
}

public sealed record L12AtomicExecutionResult(bool Completed, bool NeedsInput, bool UsedLegacyFallback, int NextAtomIndex);

/// <summary>
/// 原子解释器只执行注册表标记为可执行且已由调用方提供权威适配的原子。
/// 遇到选择或 legacy.resolve 会明确暂停，绝不同时执行新旧两套效果。
/// </summary>
public sealed class L12AtomicEffectInterpreter
{
    public L12AtomicExecutionResult Execute(L12AtomicAbility ability, IL12AtomicRuntime runtime, int startAt = 0)
    {
        for (var index = startAt; index < ability.Atoms.Count; index++)
        {
            var atom = ability.Atoms[index];
            if (atom.Kind == L12AtomKinds.Legacy) return new(false, false, true, index);
            if (atom.Kind is L12AtomKinds.SelectTarget or L12AtomKinds.SelectMode or L12AtomKinds.Optional)
                return new(false, true, false, index);
            if (atom.Kind == L12AtomKinds.Condition)
            {
                if (!runtime.Check(atom.Parameters.GetValueOrDefault("expression") ?? string.Empty)) return new(true, false, false, index + 1);
                continue;
            }
            runtime.Execute(atom);
        }
        return new(true, false, false, ability.Atoms.Count);
    }
}
