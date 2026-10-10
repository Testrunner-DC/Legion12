using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record L12EffectWorkbenchErrata(
    string Id,
    string PreviousText,
    string CorrectedText,
    string Reason,
    string EffectiveVersion,
    IReadOnlyList<string> Products);

public sealed record L12EffectWorkbenchAbilityDraft(
    string AbilityId,
    string StructureHash,
    string Trigger,
    bool Optional,
    string CostText,
    string ResolutionText,
    string ResponseBoundary,
    string TargetSummary,
    string BranchSummary);

public sealed record L12EffectWorkbenchSceneDraft(
    string SceneId,
    string Text,
    string StyleId,
    string PublicLevel);

public sealed record L12EffectWorkbenchDraft(
    string EffectText,
    string BaseProduct,
    IReadOnlyList<string> IncludedProducts,
    string EffectiveScope,
    string StyleId,
    string PublicLevel,
    IReadOnlyList<L12EffectWorkbenchErrata> Errata,
    IReadOnlyList<L12EffectWorkbenchAbilityDraft> Abilities,
    IReadOnlyList<L12EffectWorkbenchSceneDraft> Scenes);

public sealed record L12EffectWorkbenchValidation(
    bool Valid,
    bool RequiresDevelopment,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record L12EffectWorkbenchPreview(
    string Channel,
    string Label,
    string Text,
    string PublicLevel,
    string StyleId);

public sealed record L12EffectPresentationStyle(
    string Id,
    string Name,
    string Description,
    string ApplicableScenes,
    string LegendTitle,
    string LegendBody,
    string LegendTone,
    string DesktopPreview,
    string MobilePreview);

public sealed record L12EffectWorkbenchVersion(
    string Id,
    long Version,
    string Status,
    string PublishedBy,
    DateTimeOffset PublishedAt,
    string Reason,
    string SourceStructureHash);

public sealed record L12EffectWorkbenchView(
    string CardId,
    string CardName,
    long Version,
    string Status,
    string SourceStructureHash,
    L12EffectWorkbenchDraft Draft,
    L12EffectWorkbenchValidation Validation,
    IReadOnlyList<L12EffectWorkbenchPreview> Previews,
    string? UpdatedBy,
    DateTimeOffset? UpdatedAt,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    string? PublishedVersionId,
    IReadOnlyList<L12EffectWorkbenchVersion> History);

public sealed record L12EffectWorkbenchSaveRequest(
    L12EffectWorkbenchDraft Draft,
    long? ExpectedVersion = null,
    string? Reason = null);

public sealed record L12EffectWorkbenchActionRequest(
    long? ExpectedVersion = null,
    string? Reason = null,
    string? VersionId = null);

public sealed partial class L12PlatformStore
{
    private sealed class EffectWorkbenchRow
    {
        public string CardId { get; set; } = string.Empty;
        public long Version { get; set; }
        public string Status { get; set; } = "draft";
        public string SourceStructureHash { get; set; } = string.Empty;
        public string DraftJson { get; set; } = string.Empty;
        public string? UpdatedBy { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public string? ReviewedBy { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public string? PublishedVersionId { get; set; }
    }

    private sealed class EffectWorkbenchVersionRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string CardId { get; set; } = string.Empty;
        public long Version { get; set; }
        public string Status { get; set; } = "published";
        public string SourceStructureHash { get; set; } = string.Empty;
        public string DraftJson { get; set; } = string.Empty;
        public string PublishedBy { get; set; } = string.Empty;
        public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
        public string Reason { get; set; } = string.Empty;
    }

    private static readonly JsonSerializerOptions EffectWorkbenchJson = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<L12EffectPresentationStyle> EffectPresentationStyles { get; } =
    [
        new("standard", "标准强调", "用于大多数能力声明与结算，保持卡牌、标题和正文均衡。",
            "按钮、弹框、日志、回放", "标准强调", "卡名\n能力文本", "cyan",
            "居中卡牌 + 右侧文本", "紧凑卡牌 + 下方文本"),
        new("reveal", "卡面展示", "适合公开、检索、翻开与加入手牌；先确认卡面，再显示说明。",
            "卡面动画、公开弹框", "卡面展示", "公开〈卡牌名称〉", "gold",
            "完整卡图 + 说明栏", "缩略卡图 + 两行说明"),
        new("response", "响应询问", "适合可选发动、反制与费用确认，强调可执行选择。",
            "响应条、确认弹框", "是否发动？", "确认发动 / 不发动", "violet",
            "问题 + 并列操作", "问题 + 底部操作"),
        new("result", "结果播报", "适合不再需要选择的结算结果，信息简短且不阻断后续操作。",
            "日志、战报、回放", "结算完成", "目标兵力 -3000", "green",
            "轻量横幅", "轻量底部提示"),
    ];

    public L12EffectWorkbenchView EffectWorkbench(L12AtomicCardEffect effect)
    {
        lock (_gate) return EffectWorkbenchLocked(effect);
    }

    public L12EffectWorkbenchView SaveEffectWorkbenchDraft(L12AccountView actor, L12AtomicCardEffect effect,
        L12EffectWorkbenchSaveRequest request, L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            if (request.Draft is null) throw new ArgumentException("卡效草稿不能为空", nameof(request));
            var row = _data.EffectWorkbenchDrafts.FirstOrDefault(item =>
                item.CardId.Equals(effect.CardId, StringComparison.OrdinalIgnoreCase));
            if (request.ExpectedVersion is long expected && expected != (row?.Version ?? 0))
                throw new InvalidOperationException("卡效草稿已被其他操作更新，请刷新后重试");
            var normalized = NormalizeDraft(request.Draft);
            var validation = ValidateEffectWorkbench(effect, normalized);
            row ??= new EffectWorkbenchRow { CardId = effect.CardId };
            if (!_data.EffectWorkbenchDrafts.Contains(row)) _data.EffectWorkbenchDrafts.Add(row);
            var previous = row.DraftJson;
            row.Version++;
            row.Status = "draft";
            row.SourceStructureHash = SourceStructureHash(effect);
            row.DraftJson = JsonSerializer.Serialize(normalized, EffectWorkbenchJson);
            row.UpdatedBy = actor.Username;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.ReviewedBy = null;
            row.ReviewedAt = null;
            AddAdminAudit(actor, "effect-workbench", "save-draft", effect.CardId,
                previous, row.DraftJson, request.Reason, context);
            Save();
            return ToWorkbenchView(effect, row, normalized, validation);
        }
    }

    public L12EffectWorkbenchView ValidateEffectWorkbenchDraft(L12AccountView actor, L12AtomicCardEffect effect,
        L12EffectWorkbenchActionRequest request, L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var row = RequiredWorkbenchRow(effect.CardId, request.ExpectedVersion);
            var draft = DeserializeDraft(row.DraftJson);
            var validation = ValidateEffectWorkbench(effect, draft);
            row.Status = validation.Valid ? "validated" : validation.RequiresDevelopment ? "needs-development" : "draft";
            row.UpdatedBy = actor.Username;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            AddAdminAudit(actor, "effect-workbench", "validate", effect.CardId, null, row.Status,
                string.Join("；", validation.Errors.Concat(validation.Warnings)), context);
            Save();
            return ToWorkbenchView(effect, row, draft, validation);
        }
    }

    public L12EffectWorkbenchView ReviewEffectWorkbenchDraft(L12AccountView actor, L12AtomicCardEffect effect,
        L12EffectWorkbenchActionRequest request, L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var row = RequiredWorkbenchRow(effect.CardId, request.ExpectedVersion);
            var draft = DeserializeDraft(row.DraftJson);
            var validation = ValidateEffectWorkbench(effect, draft);
            if (!validation.Valid) throw new ArgumentException("草稿尚未通过结构校验，不能进入人工复核");
            row.Status = "reviewed";
            row.ReviewedBy = actor.Username;
            row.ReviewedAt = DateTimeOffset.UtcNow;
            AddAdminAudit(actor, "effect-workbench", "review", effect.CardId, null, "reviewed",
                request.Reason, context);
            Save();
            return ToWorkbenchView(effect, row, draft, validation);
        }
    }

    public L12EffectWorkbenchView PublishEffectWorkbenchDraft(L12AccountView actor, L12AtomicCardEffect effect,
        L12EffectWorkbenchActionRequest request, L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var row = RequiredWorkbenchRow(effect.CardId, request.ExpectedVersion);
            var draft = DeserializeDraft(row.DraftJson);
            var validation = ValidateEffectWorkbench(effect, draft);
            if (!validation.Valid) throw new ArgumentException("草稿尚未通过结构校验，不能发布");
            if (row.Status != "reviewed") throw new InvalidOperationException("草稿必须先完成人工复核再发布");
            if (string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("发布必须填写变更原因");
            if (!row.SourceStructureHash.Equals(SourceStructureHash(effect), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("原子结构已变化，请重新载入、校验并复核");

            var version = new EffectWorkbenchVersionRow
            {
                CardId = effect.CardId,
                Version = row.Version,
                SourceStructureHash = row.SourceStructureHash,
                DraftJson = row.DraftJson,
                PublishedBy = actor.Username,
                PublishedAt = DateTimeOffset.UtcNow,
                Reason = request.Reason?.Trim() ?? string.Empty,
            };
            _data.EffectWorkbenchVersions.Add(version);
            if (draft.EffectiveScope == "new-matches") ApplyPublishedSceneTexts(actor, effect, draft, context);
            row.Status = "published";
            row.PublishedVersionId = version.Id;
            row.UpdatedBy = actor.Username;
            row.UpdatedAt = version.PublishedAt;
            AddAdminAudit(actor, "effect-workbench", "publish", effect.CardId, null, version.Id,
                version.Reason, context);
            Save();
            return ToWorkbenchView(effect, row, draft, validation);
        }
    }

    public L12EffectWorkbenchView RollbackEffectWorkbench(L12AccountView actor, L12AtomicCardEffect effect,
        L12EffectWorkbenchActionRequest request, L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var row = RequiredWorkbenchRow(effect.CardId, request.ExpectedVersion);
            if (string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("回退必须填写原因");
            var target = _data.EffectWorkbenchVersions.FirstOrDefault(item => item.Id == request.VersionId
                && item.CardId.Equals(effect.CardId, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("未找到要回退的卡效版本");
            var draft = DeserializeDraft(target.DraftJson);
            var validation = ValidateEffectWorkbench(effect, draft);
            if (!validation.Valid) throw new ArgumentException("目标版本与当前原子结构不兼容，不能直接回退");
            row.Version++;
            row.Status = "published";
            row.SourceStructureHash = SourceStructureHash(effect);
            row.DraftJson = JsonSerializer.Serialize(draft, EffectWorkbenchJson);
            row.UpdatedBy = actor.Username;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.ReviewedBy = actor.Username;
            row.ReviewedAt = row.UpdatedAt;
            var rollbackVersion = new EffectWorkbenchVersionRow
            {
                CardId = effect.CardId,
                Version = row.Version,
                SourceStructureHash = row.SourceStructureHash,
                DraftJson = row.DraftJson,
                PublishedBy = actor.Username,
                PublishedAt = row.UpdatedAt.Value,
                Reason = $"回退至 {target.Id}：{request.Reason?.Trim()}",
            };
            _data.EffectWorkbenchVersions.Add(rollbackVersion);
            row.PublishedVersionId = rollbackVersion.Id;
            if (draft.EffectiveScope == "new-matches") ApplyPublishedSceneTexts(actor, effect, draft, context);
            AddAdminAudit(actor, "effect-workbench", "rollback", effect.CardId, target.Id,
                rollbackVersion.Id, request.Reason, context);
            Save();
            return ToWorkbenchView(effect, row, draft, validation);
        }
    }

    private L12EffectWorkbenchView EffectWorkbenchLocked(L12AtomicCardEffect effect)
    {
        var row = _data.EffectWorkbenchDrafts.FirstOrDefault(item =>
            item.CardId.Equals(effect.CardId, StringComparison.OrdinalIgnoreCase));
        var draft = row is null ? DefaultDraft(effect) : DeserializeDraft(row.DraftJson);
        var validation = ValidateEffectWorkbench(effect, draft);
        return ToWorkbenchView(effect, row, draft, validation);
    }

    private L12EffectWorkbenchView ToWorkbenchView(L12AtomicCardEffect effect, EffectWorkbenchRow? row,
        L12EffectWorkbenchDraft draft, L12EffectWorkbenchValidation validation)
    {
        var history = _data.EffectWorkbenchVersions
            .Where(item => item.CardId.Equals(effect.CardId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.PublishedAt)
            .Select(item => new L12EffectWorkbenchVersion(item.Id, item.Version, item.Status,
                item.PublishedBy, item.PublishedAt, item.Reason, item.SourceStructureHash)).ToArray();
        return new L12EffectWorkbenchView(effect.CardId, effect.Name, row?.Version ?? 0,
            row?.Status ?? "source", SourceStructureHash(effect), draft, validation, BuildPreviews(effect, draft),
            row?.UpdatedBy, row?.UpdatedAt, row?.ReviewedBy, row?.ReviewedAt, row?.PublishedVersionId, history);
    }

    private static L12EffectWorkbenchDraft DefaultDraft(L12AtomicCardEffect effect)
        => new(effect.EffectText, effect.Product, [effect.Product], "new-matches", "standard", "public", [],
            effect.Abilities.Select(ability => new L12EffectWorkbenchAbilityDraft(
                ability.AbilityId, ability.StructureHash, ability.Trigger,
                ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Optional), ability.CostText ?? string.Empty,
                ability.ResolutionText, ResponseBoundary(ability), TargetSummary(ability), BranchSummary(ability))).ToArray(),
            effect.Abilities.SelectMany(ability => ability.Presentations.Select(scene =>
                new L12EffectWorkbenchSceneDraft(scene.SceneId, scene.EffectiveText,
                    StyleFor(scene.EventType), "public"))).ToArray());

    private static L12EffectWorkbenchDraft NormalizeDraft(L12EffectWorkbenchDraft draft)
        => draft with
        {
            EffectText = NormalizeText(draft.EffectText),
            BaseProduct = NormalizeSingleLine(draft.BaseProduct),
            IncludedProducts = (draft.IncludedProducts ?? []).Select(NormalizeSingleLine).Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            EffectiveScope = NormalizeSingleLine(draft.EffectiveScope).ToLowerInvariant(),
            StyleId = NormalizeSingleLine(draft.StyleId).ToLowerInvariant(),
            PublicLevel = NormalizeSingleLine(draft.PublicLevel).ToLowerInvariant(),
            Errata = (draft.Errata ?? []).Select(item => item with
            {
                Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : NormalizeSingleLine(item.Id),
                PreviousText = NormalizeText(item.PreviousText), CorrectedText = NormalizeText(item.CorrectedText),
                Reason = NormalizeSingleLine(item.Reason), EffectiveVersion = NormalizeSingleLine(item.EffectiveVersion),
                Products = (item.Products ?? []).Select(NormalizeSingleLine).Where(product => product.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            }).ToArray(),
            Abilities = (draft.Abilities ?? []).Select(item => item with
            {
                AbilityId = NormalizeSingleLine(item.AbilityId), StructureHash = NormalizeSingleLine(item.StructureHash),
                Trigger = NormalizeSingleLine(item.Trigger), CostText = NormalizeText(item.CostText),
                ResolutionText = NormalizeText(item.ResolutionText), ResponseBoundary = NormalizeSingleLine(item.ResponseBoundary),
                TargetSummary = NormalizeSingleLine(item.TargetSummary), BranchSummary = NormalizeSingleLine(item.BranchSummary),
            }).ToArray(),
            Scenes = (draft.Scenes ?? []).Select(item => item with
            {
                SceneId = NormalizeSingleLine(item.SceneId), Text = NormalizeText(item.Text),
                StyleId = NormalizeSingleLine(item.StyleId).ToLowerInvariant(),
                PublicLevel = NormalizeSingleLine(item.PublicLevel).ToLowerInvariant(),
            }).ToArray(),
        };

    private static L12EffectWorkbenchValidation ValidateEffectWorkbench(L12AtomicCardEffect effect,
        L12EffectWorkbenchDraft draft)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var requiresDevelopment = false;
        if (string.IsNullOrWhiteSpace(draft.BaseProduct)) errors.Add("必须填写所属产品");
        if (draft.BaseProduct.Length > 120) errors.Add("所属产品不得超过 120 字符");
        if (draft.IncludedProducts.Count > 50 || draft.IncludedProducts.Any(item => item.Length > 120))
            errors.Add("收录产品最多 50 项，每项不得超过 120 字符");
        if (!draft.IncludedProducts.Contains(draft.BaseProduct, StringComparer.OrdinalIgnoreCase))
            errors.Add("收录产品必须包含所属产品");
        if (draft.EffectiveScope is not ("new-matches" or "display-only")) errors.Add("无效的生效范围");
        if (!StyleIds().Contains(draft.StyleId)) errors.Add("无效的默认呈现样式");
        if (!PublicLevels.Contains(draft.PublicLevel)) errors.Add("无效的默认公开等级");
        if (draft.EffectText.Length > 8000) errors.Add("卡牌文本不得超过 8000 字符");

        var abilityById = draft.Abilities.GroupBy(item => item.AbilityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var ability in effect.Abilities)
        {
            if (!abilityById.TryGetValue(ability.AbilityId, out var matches) || matches.Length != 1)
            {
                errors.Add($"能力 {ability.Sequence} 必须且只能保留一个结构段");
                continue;
            }
            var item = matches[0];
            if (!item.StructureHash.Equals(ability.StructureHash, StringComparison.OrdinalIgnoreCase))
                errors.Add($"能力 {ability.Sequence} 的原子结构已变化，请刷新草稿");
            if (!NormalizeText(item.CostText).Equals(NormalizeText(ability.CostText), StringComparison.Ordinal)
                || !NormalizeText(item.ResolutionText).Equals(NormalizeText(ability.ResolutionText), StringComparison.Ordinal)
                || !item.Trigger.Equals(ability.Trigger, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"能力 {ability.Sequence} 修改了运行语义；请先进入效果一致性开发再发布");
                requiresDevelopment = true;
            }
        }
        if (abilityById.Keys.Any(id => effect.Abilities.All(item => !item.AbilityId.Equals(id,
                StringComparison.OrdinalIgnoreCase)))) errors.Add("草稿包含不属于当前卡牌的能力段");

        var sourceScenes = effect.Abilities.SelectMany(item => item.Presentations)
            .ToDictionary(item => item.SceneId, StringComparer.Ordinal);
        var sceneIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scene in draft.Scenes)
        {
            if (!sceneIds.Add(scene.SceneId)) errors.Add($"呈现场景重复：{scene.SceneId}");
            if (!sourceScenes.TryGetValue(scene.SceneId, out var source))
            {
                errors.Add($"呈现场景已失效：{scene.SceneId}");
                continue;
            }
            if (!StyleIds().Contains(scene.StyleId)) errors.Add($"场景 {source.Label} 的呈现样式无效");
            if (!PublicLevels.Contains(scene.PublicLevel)) errors.Add($"场景 {source.Label} 的公开等级无效");
            try { L12EffectPresentationText.Validate(scene.Text, source.Placeholders); }
            catch (ArgumentException error) { errors.Add($"场景 {source.Label}：{error.Message}"); }
        }
        if (sourceScenes.Keys.Any(id => !sceneIds.Contains(id))) warnings.Add("部分既有呈现场景未进入草稿，将继续沿用默认文本");
        if (draft.Errata.Count > 100) errors.Add("单张卡最多维护 100 条勘误");
        if (draft.Errata.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            errors.Add("勘误记录 ID 不得重复");
        foreach (var errata in draft.Errata)
        {
            if (string.IsNullOrWhiteSpace(errata.PreviousText) || string.IsNullOrWhiteSpace(errata.CorrectedText))
                errors.Add("勘误必须同时填写旧文与新文");
            if (string.IsNullOrWhiteSpace(errata.Reason)) errors.Add("勘误必须填写原因");
            if (errata.PreviousText.Length > 8000 || errata.CorrectedText.Length > 8000
                || errata.Reason.Length > 1000 || errata.EffectiveVersion.Length > 120)
                errors.Add("勘误文本、原因或生效版本超过允许长度");
            if (errata.Products.Count == 0) errors.Add("勘误必须至少关联一个收录产品");
        }
        if (!NormalizeText(draft.EffectText).Equals(NormalizeText(effect.EffectText), StringComparison.Ordinal))
            warnings.Add("卡牌正文与当前权威目录不同；发布后先作为版本化勘误资料，运行规则仍由现有原子结构约束");
        return new L12EffectWorkbenchValidation(errors.Count == 0, requiresDevelopment, errors, warnings);
    }

    private void ApplyPublishedSceneTexts(L12AccountView actor, L12AtomicCardEffect effect,
        L12EffectWorkbenchDraft draft, L12AdminAuditContext? context)
    {
        var sourceScenes = effect.Abilities.SelectMany(item => item.Presentations)
            .ToDictionary(item => item.SceneId, StringComparer.Ordinal);
        foreach (var item in draft.Scenes)
        {
            if (!sourceScenes.TryGetValue(item.SceneId, out var scene)) continue;
            var normalized = L12EffectPresentationText.Validate(item.Text, scene.Placeholders);
            var row = _data.EffectPresentationOverrides.FirstOrDefault(entry => entry.SceneId == item.SceneId);
            if (normalized == scene.DefaultText)
            {
                if (row is not null) _data.EffectPresentationOverrides.Remove(row);
                continue;
            }
            row ??= new EffectPresentationOverrideRow
            {
                CardId = scene.CardId, AbilityId = scene.AbilityId, SceneId = scene.SceneId,
            };
            if (!_data.EffectPresentationOverrides.Contains(row)) _data.EffectPresentationOverrides.Add(row);
            row.Text = normalized;
            row.UpdatedBy = actor.Username;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        AddAdminAudit(actor, "effect-presentation", "publish-workbench", effect.CardId, null,
            draft.Scenes.Count.ToString(), "卡效工作台统一发布", context);
    }

    private EffectWorkbenchRow RequiredWorkbenchRow(string cardId, long? expectedVersion)
    {
        var row = _data.EffectWorkbenchDrafts.FirstOrDefault(item =>
            item.CardId.Equals(cardId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("请先保存卡效草稿");
        if (expectedVersion is long expected && expected != row.Version)
            throw new InvalidOperationException("卡效草稿已被其他操作更新，请刷新后重试");
        return row;
    }

    private static L12EffectWorkbenchDraft DeserializeDraft(string json)
        => JsonSerializer.Deserialize<L12EffectWorkbenchDraft>(json, EffectWorkbenchJson)
           ?? throw new InvalidDataException("卡效草稿数据损坏");

    private static IReadOnlyList<L12EffectWorkbenchPreview> BuildPreviews(L12AtomicCardEffect effect,
        L12EffectWorkbenchDraft draft)
    {
        var text = draft.Scenes.FirstOrDefault()?.Text ?? draft.EffectText;
        var level = draft.Scenes.FirstOrDefault()?.PublicLevel ?? draft.PublicLevel;
        var style = draft.Scenes.FirstOrDefault()?.StyleId ?? draft.StyleId;
        var ability = draft.Abilities.FirstOrDefault();
        var button = ability is null ? "查看效果" : ability.Trigger switch
        {
            "active" => "发动效果", "attack" => "发动进攻效果", "enter" => "发动登场效果",
            "death" => "发动阵亡效果", _ => "确认发动",
        };
        return
        [
            new("archive", "图鉴卡文", draft.EffectText, draft.PublicLevel, draft.StyleId),
            new("button", "操作按钮", button, "controller", draft.StyleId),
            new("dialog", "发动 / 支付 / 选择弹框", text, level, style),
            new("animation", "卡面动画", text, level, style),
            new("log", "对局日志 / 战报", text, level, style),
            new("replay", "回放", text, level, style),
        ];
    }

    private static string SourceStructureHash(L12AtomicCardEffect effect)
    {
        var source = string.Join("|", effect.Abilities.OrderBy(item => item.Sequence)
            .Select(item => $"{item.AbilityId}:{item.StructureHash}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }

    private static string ResponseBoundary(L12AtomicAbility ability)
        => ability.Atoms.Any(atom => atom.Kind.Contains("response", StringComparison.OrdinalIgnoreCase))
            || ability.Trigger.Contains("response", StringComparison.OrdinalIgnoreCase)
            ? "响应窗口内" : "不创建额外响应窗口";

    private static string TargetSummary(L12AtomicAbility ability)
        => string.Join("；", ability.Atoms.Where(atom => atom.Kind == L12AtomKinds.SelectTarget)
            .Select(atom => atom.Parameters.TryGetValue("target", out var target) ? target : atom.Label)
            .DefaultIfEmpty("无显式目标"));

    private static string BranchSummary(L12AtomicAbility ability)
        => string.Join("；", ability.Presentations.Where(scene => !string.IsNullOrWhiteSpace(scene.BranchLabel))
            .Select(scene => scene.BranchLabel!).Distinct().DefaultIfEmpty("单一路径"));

    private static string StyleFor(string eventType) => eventType switch
    {
        "reveal" or "hidden-reveal" => "reveal",
        "effect-response" => "response",
        _ => "standard",
    };

    private static string NormalizeText(string? value)
        => (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    private static string NormalizeSingleLine(string? value)
        => NormalizeText(value).Replace('\n', ' ').Trim();

    private static HashSet<string> StyleIds()
        => EffectPresentationStyles.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> PublicLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "public", "controller", "owner", "hidden",
    };
}
