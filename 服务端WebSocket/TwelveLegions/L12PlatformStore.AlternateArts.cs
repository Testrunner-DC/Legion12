namespace TwelveLegions.Server;

public sealed record L12AlternateArtView(string Id, string ArtCode, string BaseCardId, string DisplayName, string MediaAssetId,
    string ImageUrl, string ThumbnailUrl, bool Active, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string ProductId = "", string ProductName = "", string CardImageId = "", bool BuiltIn = false,
    string BaseCardName = "", DateTimeOffset? GrantedAt = null, string GrantReason = "");
public sealed record L12AlternateArtSearchPage(IReadOnlyList<L12AlternateArtView> Items, int Total, int Page, int PageSize);
public sealed record L12AlternateArtGrantNotificationView(string Id, string AlternateArtId, string DisplayName,
    string ArtCode, string BaseCardId, string BaseCardName, string Reason, DateTimeOffset GrantedAt,
    string ImageUrl, string ThumbnailUrl, string CardImageId, bool BuiltIn);
public sealed record L12AlternateArtProductView(string Id, string Name, bool Active,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record L12AlternateArtGrantView(string Id, string AccountId, string Username, string AlternateArtId,
    string SourceKind, string SourceReference, DateTimeOffset GrantedAt, DateTimeOffset? RevokedAt);
public sealed record L12AlternateArtDraft(string? Id, string ArtCode, string BaseCardId, string DisplayName, string MediaAssetId,
    bool Active = true, string ProductId = "");
public sealed record L12AlternateArtProductDraft(string? Id, string Name, bool Active = true);
public sealed record L12AlternateArtGrantDraft(string AlternateArtId, string Username, string SourceKind,
    string SourceReference = "");
public sealed record L12AlternateArtAwardRuleView(string Id, string AlternateArtId, string Kind, string SeasonId,
    string EventId, int MinimumTierIndex, bool Active, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string MasterId = "");
public sealed record L12AlternateArtAwardRuleDraft(string? Id, string AlternateArtId, string Kind, string SeasonId,
    string EventId, int MinimumTierIndex, bool Active = true, string MasterId = "");
public sealed record L12AlternateArtEventDispatchDraft(string RuleId, IReadOnlyList<string> Usernames);
public sealed record L12AlternateArtRankedParticipantDispatchDraft(string AlternateArtId, string SeasonId = "");
public sealed record L12AlternateArtRankedParticipantDispatchPreview(int EligibleAccounts, int AlreadyGranted,
    int ToGrant, string SourceReference, string SeasonId);

public sealed partial class L12PlatformStore
{
    private static readonly IReadOnlySet<string> AlternateArtGrantSources =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "manual", "rank-reached", "season-final", "master-champion-season-final", "event", "ranked-participants" };
    private static readonly IReadOnlySet<string> AlternateArtAwardKinds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rank-reached", "season-final", "master-champion-season-final", "event" };

    public IReadOnlyList<L12AlternateArtProductView> AlternateArtProducts(bool includeInactive = false)
    {
        lock (_gate) return _data.AlternateArtProducts.Where(row => includeInactive || row.Active)
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).Select(ToAlternateArtProductView).ToArray();
    }

    public L12AlternateArtProductView SaveAlternateArtProduct(L12AccountView actor, L12AlternateArtProductDraft draft,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var name = LimitSiteText(draft.Name, 100);
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("异画归属产品名称不能为空");
            var row = string.IsNullOrWhiteSpace(draft.Id) ? null : _data.AlternateArtProducts.FirstOrDefault(item => item.Id == draft.Id);
            if (row is null && _data.AlternateArtProducts.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("同名异画归属产品已存在");
            var previous = row is null ? null : System.Text.Json.JsonSerializer.Serialize(ToAlternateArtProductView(row));
            if (row is null) { row = new AlternateArtProductRow { CreatedByAccountId = actor.Id }; _data.AlternateArtProducts.Add(row); }
            row.Name = name;
            row.Active = draft.Active;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            var view = ToAlternateArtProductView(row);
            AddAdminAudit(actor, "alternate-art-product", previous is null ? "create" : "update", row.Id, previous,
                System.Text.Json.JsonSerializer.Serialize(view), null, context);
            Save();
            return view;
        }
    }

    public IReadOnlyList<L12AlternateArtView> AlternateArts(bool includeInactive = false)
    {
        lock (_gate) return _officialAlternateArts.Values.Select(ToAlternateArtView)
            .Concat(_data.AlternateArts.Where(row => includeInactive || row.Active).Select(ToAlternateArtView))
            .OrderBy(row => row.ArtCode, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public L12AlternateArtSearchPage SearchAlternateArts(string? name, string? artCode, string? baseCard,
        int page = 1, int pageSize = 20, bool includeInactive = true)
    {
        lock (_gate)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var nameTerm = LimitSiteText(name, 100);
            var codeTerm = LimitSiteText(artCode, 80);
            var baseTerm = LimitSiteText(baseCard, 100);
            var query = _officialAlternateArts.Values.Select(ToAlternateArtView)
                .Concat(_data.AlternateArts.Where(row => includeInactive || row.Active).Select(ToAlternateArtView));
            if (!string.IsNullOrWhiteSpace(nameTerm))
                query = query.Where(row => row.DisplayName.Contains(nameTerm, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(codeTerm))
                query = query.Where(row => row.ArtCode.Contains(codeTerm, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(baseTerm))
                query = query.Where(row => row.BaseCardId.Contains(baseTerm, StringComparison.OrdinalIgnoreCase)
                    || row.BaseCardName.Contains(baseTerm, StringComparison.OrdinalIgnoreCase));
            var ordered = query.OrderBy(row => row.ArtCode, StringComparer.OrdinalIgnoreCase).ToArray();
            return new L12AlternateArtSearchPage(ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
                ordered.Length, page, pageSize);
        }
    }

    public IReadOnlyList<L12AlternateArtView> OwnedAlternateArts(string accountId)
    {
        lock (_gate)
        {
            var grants = _data.AlternateArtGrants.Where(row => row.AccountId == accountId && row.RevokedAt is null)
                .GroupBy(row => row.AlternateArtId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(row => row.GrantedAt).First(),
                    StringComparer.OrdinalIgnoreCase);
            return _officialAlternateArts.Values.Select(ToAlternateArtView)
                .Concat(_data.AlternateArts.Where(row => row.Active).Select(ToAlternateArtView))
                .Where(row => grants.ContainsKey(row.Id))
                .Select(row => row with
                {
                    GrantedAt = grants[row.Id].GrantedAt,
                    GrantReason = AlternateArtGrantReason(grants[row.Id]),
                })
                .OrderBy(row => row.ArtCode, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public IReadOnlyList<L12AlternateArtGrantNotificationView> PendingAlternateArtGrantNotifications(string accountId)
    {
        lock (_gate)
        {
            return _data.AlternateArtGrants.Where(row => row.AccountId == accountId && row.RevokedAt is null
                    && row.NotificationSeenAt is null)
                .OrderBy(row => row.GrantedAt).Take(20)
                .Select(row =>
                {
                    var art = FindActiveAlternateArtLocked(row.AlternateArtId);
                    return art is null ? null : new L12AlternateArtGrantNotificationView(row.Id, art.Id,
                        art.DisplayName, art.ArtCode, art.BaseCardId, art.BaseCardName, AlternateArtGrantReason(row),
                        row.GrantedAt, art.ImageUrl, art.ThumbnailUrl, art.CardImageId, art.BuiltIn);
                })
                .Where(row => row is not null).Cast<L12AlternateArtGrantNotificationView>().ToArray();
        }
    }

    public void AcknowledgeAlternateArtGrantNotification(string accountId, string grantId)
    {
        lock (_gate)
        {
            var row = _data.AlternateArtGrants.FirstOrDefault(item => item.Id == grantId
                && item.AccountId == accountId && item.RevokedAt is null)
                ?? throw new KeyNotFoundException("异画权益提示不存在");
            if (row.NotificationSeenAt is not null) return;
            row.NotificationSeenAt = DateTimeOffset.UtcNow;
            Save();
        }
    }

    public L12AlternateArtView SaveAlternateArt(L12AccountView actor, L12AlternateArtDraft draft,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var existing = string.IsNullOrWhiteSpace(draft.Id) ? null : _data.AlternateArts.FirstOrDefault(item => item.Id == draft.Id);
            if (!string.IsNullOrWhiteSpace(draft.Id) && _officialAlternateArts.ContainsKey(draft.Id))
                throw new ArgumentException("内置异画由卡牌图库管理，不能在此编辑");
            var artCode = LimitSiteText(draft.ArtCode, 80).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(artCode) && existing is null) throw new ArgumentException("新异画必须设置独立编号");
            if (!string.IsNullOrWhiteSpace(artCode) && (_officialAlternateArts.Values.Any(item =>
                    item.ArtCode.Equals(artCode, StringComparison.OrdinalIgnoreCase))
                || _data.AlternateArts.Any(item => item.Id != draft.Id
                    && item.ArtCode.Equals(artCode, StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException("异画编号已被使用");
            var baseCardId = draft.BaseCardId?.Trim() ?? string.Empty;
            if (!_officialCards.TryGetValue(baseCardId, out var baseCard))
                throw new ArgumentException("异画必须绑定到现有的规则卡牌编号");
            var media = ActiveMedia(draft.MediaAssetId) ?? throw new ArgumentException("异画素材不存在或已删除");
            if (!string.Equals(media.Kind, "card-art", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("异画必须使用“卡牌异画”素材上传入口的图片");
            // 异画是原卡的外观权益，名称属于规则卡牌身份，不能由上传表单另造或改写。
            var displayName = LimitSiteText(baseCard.NameZh, 100);
            var productId = draft.ProductId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(productId) && !_data.AlternateArtProducts.Any(item => item.Id == productId && item.Active))
                throw new ArgumentException("异画归属产品不存在或已停用");
            var row = existing;
            var previous = row is null ? null : System.Text.Json.JsonSerializer.Serialize(ToAlternateArtView(row));
            if (row is null)
            {
                row = new AlternateArtRow { CreatedByAccountId = actor.Id };
                _data.AlternateArts.Add(row);
            }
            row.BaseCardId = baseCardId;
            row.ArtCode = artCode;
            row.DisplayName = displayName;
            row.MediaAssetId = media.Id;
            row.ProductId = productId;
            row.Active = draft.Active;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            var view = ToAlternateArtView(row);
            AddAdminAudit(actor, "alternate-art", previous is null ? "create" : "update", row.Id, previous,
                System.Text.Json.JsonSerializer.Serialize(view), null, context);
            Save();
            return view;
        }
    }

    public L12AlternateArtGrantView GrantAlternateArt(L12AccountView actor, L12AlternateArtGrantDraft draft,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var art = FindActiveAlternateArtLocked(draft.AlternateArtId)
                ?? throw new KeyNotFoundException("异画不存在或未启用");
            var account = _data.Accounts.FirstOrDefault(row => string.Equals(row.Username, draft.Username?.Trim(), StringComparison.OrdinalIgnoreCase)
                && !row.Deleted) ?? throw new KeyNotFoundException("目标玩家不存在");
            var source = draft.SourceKind?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!AlternateArtGrantSources.Contains(source)) throw new ArgumentException("派发来源必须为 manual、rank-reached、season-final 或 event");
            var reference = LimitSiteText(draft.SourceReference, 240);
            var row = _data.AlternateArtGrants.FirstOrDefault(item => item.AccountId == account.Id && item.AlternateArtId == art.Id
                && item.RevokedAt is null && string.Equals(item.SourceKind, source, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.SourceReference, reference, StringComparison.Ordinal));
            if (row is null)
            {
                row = new AlternateArtGrantRow { AccountId = account.Id, AlternateArtId = art.Id, GrantedByAccountId = actor.Id };
                _data.AlternateArtGrants.Add(row);
            }
            row.SourceKind = source;
            row.SourceReference = reference;
            row.GrantedAt = DateTimeOffset.UtcNow;
            row.NotificationSeenAt = null;
            row.RevokedAt = null;
            row.RevokedByAccountId = null;
            var view = ToAlternateArtGrantView(row);
            AddAdminAudit(actor, "alternate-art-grant", "grant", row.Id, null,
                $"art={art.Id};account={account.Id};source={source};reference={reference}", null, context);
            Save();
            return view;
        }
    }

    public void RevokeAlternateArtGrant(L12AccountView actor, string grantId, L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var row = _data.AlternateArtGrants.FirstOrDefault(item => item.Id == grantId)
                ?? throw new KeyNotFoundException("异画权益记录不存在");
            if (row.RevokedAt is not null) return;
            row.RevokedAt = DateTimeOffset.UtcNow;
            row.RevokedByAccountId = actor.Id;
            AddAdminAudit(actor, "alternate-art-grant", "revoke", row.Id, row.AlternateArtId, row.AccountId, null, context);
            Save();
        }
    }

    public IReadOnlyList<L12AlternateArtGrantView> AlternateArtGrants(string? username = null)
    {
        lock (_gate)
        {
            var accounts = _data.Accounts.ToDictionary(row => row.Id, row => row.Username);
            return _data.AlternateArtGrants.Where(row => string.IsNullOrWhiteSpace(username)
                    || (accounts.TryGetValue(row.AccountId, out var name) && string.Equals(name, username.Trim(), StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(row => row.GrantedAt).Take(1000).Select(ToAlternateArtGrantView).ToArray();
        }
    }

    public IReadOnlyList<L12AlternateArtAwardRuleView> AlternateArtAwardRules()
    {
        lock (_gate) return _data.AlternateArtAwardRules.OrderByDescending(row => row.UpdatedAt)
            .Select(ToAlternateArtAwardRuleView).ToArray();
    }

    public L12AlternateArtAwardRuleView SaveAlternateArtAwardRule(L12AccountView actor,
        L12AlternateArtAwardRuleDraft draft, L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var art = FindAlternateArtLocked(draft.AlternateArtId)
                ?? throw new KeyNotFoundException("异画不存在");
            var kind = draft.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!AlternateArtAwardKinds.Contains(kind)) throw new ArgumentException("发放规则类型无效");
            var seasonId = LimitSiteText(draft.SeasonId, 100);
            var eventId = LimitSiteText(draft.EventId, 160);
            if (kind is "rank-reached" or "season-final" or "master-champion-season-final" && string.IsNullOrWhiteSpace(seasonId))
                throw new ArgumentException("段位和赛季结算规则必须填写赛季编号");
            if (kind == "event" && string.IsNullOrWhiteSpace(eventId)) throw new ArgumentException("活动规则必须填写活动编号");
            if (draft.MinimumTierIndex is < 0 or > 4) throw new ArgumentException("最低段位只能为 0 至 4");
            var row = string.IsNullOrWhiteSpace(draft.Id) ? null : _data.AlternateArtAwardRules.FirstOrDefault(item => item.Id == draft.Id);
            var previous = row is null ? null : System.Text.Json.JsonSerializer.Serialize(ToAlternateArtAwardRuleView(row));
            if (row is null)
            {
                row = new AlternateArtAwardRuleRow { CreatedByAccountId = actor.Id };
                _data.AlternateArtAwardRules.Add(row);
            }
            row.AlternateArtId = art.Id;
            row.Kind = kind;
            row.SeasonId = seasonId;
            row.EventId = eventId;
            row.MasterId = LimitSiteText(draft.MasterId, 100);
            row.MinimumTierIndex = draft.MinimumTierIndex;
            row.Active = draft.Active;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            var view = ToAlternateArtAwardRuleView(row);
            AddAdminAudit(actor, "alternate-art-award-rule", previous is null ? "create" : "update", row.Id,
                previous, System.Text.Json.JsonSerializer.Serialize(view), null, context);
            Save();
            return view;
        }
    }

    public IReadOnlyList<L12AlternateArtGrantView> DispatchAlternateArtEvent(L12AccountView actor,
        L12AlternateArtEventDispatchDraft draft, L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var rule = _data.AlternateArtAwardRules.FirstOrDefault(row => row.Id == draft.RuleId && row.Active && row.Kind == "event")
                ?? throw new KeyNotFoundException("可执行的活动异画规则不存在");
            var usernames = (draft.Usernames ?? []).Select(value => value?.Trim()).Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(500).ToArray();
            if (usernames.Length == 0) throw new ArgumentException("请至少填写一个玩家账号");
            var accounts = usernames.Select(username => _data.Accounts.FirstOrDefault(row =>
                    string.Equals(row.Username, username, StringComparison.OrdinalIgnoreCase) && !row.Deleted)
                ?? throw new KeyNotFoundException($"目标玩家不存在：{username}")).ToArray();
            var views = new List<L12AlternateArtGrantView>();
            foreach (var account in accounts)
            {
                var grant = GrantAlternateArtToAccountLocked(account.Id, rule.AlternateArtId, "event", rule.EventId, actor.Id);
                views.Add(ToAlternateArtGrantView(grant));
            }
            AddAdminAudit(actor, "alternate-art-award-rule", "dispatch-event", rule.Id, null,
                $"event={rule.EventId};players={usernames.Length}", null, context);
            Save();
            return views;
        }
    }

    public L12AlternateArtRankedParticipantDispatchPreview PreviewRankedParticipantAlternateArtDispatch(
        L12AlternateArtRankedParticipantDispatchDraft draft)
    {
        lock (_gate)
        {
            var art = FindActiveAlternateArtLocked(draft.AlternateArtId)
                ?? throw new KeyNotFoundException("异画不存在或未启用");
            // 空值只表示“本赛季”，绝不再隐式扩展为所有历史赛季。
            // 历史赛季同时读取归档档案与主宰战绩，保证已经结算的赛季仍可补发。
            var requestedSeasonId = LimitSiteText(draft.SeasonId, 100);
            var seasonId = string.IsNullOrWhiteSpace(requestedSeasonId)
                ? RequireOperationsConfig().Season.Id
                : requestedSeasonId;
            var sourceReference = $"ranked-participants:{seasonId}";
            var accounts = RankedParticipantAccountIdsLocked(seasonId)
                .Where(accountId => _data.Accounts.Any(account => account.Id == accountId && !account.Deleted)).ToArray();
            var alreadyGranted = accounts.Count(accountId => _data.AlternateArtGrants.Any(row => row.AccountId == accountId
                && row.AlternateArtId == art.Id && row.RevokedAt is null && row.SourceKind == "ranked-participants"
                && row.SourceReference == sourceReference));
            return new L12AlternateArtRankedParticipantDispatchPreview(accounts.Length, alreadyGranted,
                accounts.Length - alreadyGranted, sourceReference, seasonId);
        }
    }

    public IReadOnlyList<L12AlternateArtGrantView> DispatchRankedParticipantAlternateArt(L12AccountView actor,
        L12AlternateArtRankedParticipantDispatchDraft draft, L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var preview = PreviewRankedParticipantAlternateArtDispatch(draft);
            var accounts = RankedParticipantAccountIdsLocked(preview.SeasonId)
                .Where(accountId => _data.Accounts.Any(account => account.Id == accountId && !account.Deleted)).ToArray();
            var grants = accounts.Select(accountId => ToAlternateArtGrantView(GrantAlternateArtToAccountLocked(accountId,
                draft.AlternateArtId, "ranked-participants", preview.SourceReference, actor.Id))).ToArray();
            AddAdminAudit(actor, "alternate-art-grant", "dispatch-ranked-participants", draft.AlternateArtId, null,
                $"source={preview.SourceReference};eligible={preview.EligibleAccounts};new={preview.ToGrant}", null, context);
            Save();
            return grants;
        }
    }

    private IReadOnlyCollection<string> RankedParticipantAccountIdsLocked(string seasonId)
        => _data.RankedProfiles.Where(row => row.SeasonId.Equals(seasonId, StringComparison.OrdinalIgnoreCase)
                && row.PlacementPlayed + row.Wins + row.Losses > 0)
            .Select(row => row.AccountId)
            .Concat(_data.RankedProfileHistory.Where(row => row.SeasonId.Equals(seasonId, StringComparison.OrdinalIgnoreCase)
                    && row.PlacementPlayed + row.Wins + row.Losses > 0)
                .Select(row => row.AccountId))
            .Concat(_data.RankedMasterRecords.Where(row => row.SeasonId.Equals(seasonId, StringComparison.OrdinalIgnoreCase)
                    && row.Games > 0)
                .Select(row => row.AccountId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void ApplyRankReachedAlternateArtAwardsLocked(string accountId, string seasonId, int tierIndex)
    {
        foreach (var rule in _data.AlternateArtAwardRules.Where(row => row.Active && row.Kind == "rank-reached"
                     && string.Equals(row.SeasonId, seasonId, StringComparison.OrdinalIgnoreCase)
                     && tierIndex >= row.MinimumTierIndex))
            if (IsActiveAlternateArt(rule.AlternateArtId))
                GrantAlternateArtToAccountLocked(accountId, rule.AlternateArtId, "rank-reached", seasonId, "system");
    }

    private void ApplySeasonFinalAlternateArtAwardsLocked(string accountId, string seasonId, int tierIndex)
    {
        foreach (var rule in _data.AlternateArtAwardRules.Where(row => row.Active && row.Kind == "season-final"
                     && string.Equals(row.SeasonId, seasonId, StringComparison.OrdinalIgnoreCase)
                     && tierIndex >= row.MinimumTierIndex))
            if (IsActiveAlternateArt(rule.AlternateArtId))
                GrantAlternateArtToAccountLocked(accountId, rule.AlternateArtId, "season-final", seasonId, "system");
    }

    private void ApplyMasterChampionSeasonFinalAlternateArtAwardsLocked(
        IReadOnlyDictionary<string, RankedMasterRecordRow> champions, string seasonId)
    {
        foreach (var rule in _data.AlternateArtAwardRules.Where(row => row.Active && row.Kind == "master-champion-season-final"
                     && string.Equals(row.SeasonId, seasonId, StringComparison.OrdinalIgnoreCase)))
        {
            if (!IsActiveAlternateArt(rule.AlternateArtId)) continue;
            foreach (var champion in champions.Values.Where(item => string.IsNullOrWhiteSpace(rule.MasterId)
                         || item.MasterId.Equals(rule.MasterId, StringComparison.OrdinalIgnoreCase)))
                GrantAlternateArtToAccountLocked(champion.AccountId, rule.AlternateArtId,
                    "master-champion-season-final", $"{seasonId}:{champion.MasterId}", "system");
        }
    }

    private bool IsActiveAlternateArt(string alternateArtId)
        => FindActiveAlternateArtLocked(alternateArtId) is not null;

    private AlternateArtGrantRow GrantAlternateArtToAccountLocked(string accountId, string alternateArtId,
        string sourceKind, string sourceReference, string grantedByAccountId)
    {
        var art = FindActiveAlternateArtLocked(alternateArtId)
            ?? throw new InvalidOperationException("异画已停用或不存在，无法派发");
        var row = _data.AlternateArtGrants.FirstOrDefault(item => item.AccountId == accountId && item.AlternateArtId == art.Id
            && item.RevokedAt is null && string.Equals(item.SourceKind, sourceKind, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.SourceReference, sourceReference, StringComparison.Ordinal));
        if (row is not null) return row;
        row = new AlternateArtGrantRow { AccountId = accountId, AlternateArtId = art.Id, SourceKind = sourceKind,
            SourceReference = sourceReference, GrantedByAccountId = grantedByAccountId };
        _data.AlternateArtGrants.Add(row);
        return row;
    }

    internal IReadOnlyDictionary<string, string> ResolveOwnedAlternateArtUrls(string? accountId,
        IReadOnlyDictionary<string, string>? selections,
        IReadOnlyDictionary<string, List<string>>? copies = null)
    {
        if (string.IsNullOrWhiteSpace(accountId)
            || ((selections is null || selections.Count == 0) && (copies is null || copies.Count == 0)))
            return new Dictionary<string, string>();
        lock (_gate)
        {
            var owned = _data.AlternateArtGrants.Where(row => row.AccountId == accountId && row.RevokedAt is null)
                .Select(row => row.AlternateArtId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var selection in (selections ?? new Dictionary<string, string>()).Take(128))
            {
                var art = FindActiveAlternateArtLocked(selection.Value);
                if (art is not null && owned.Contains(art.Id)
                    && string.Equals(art.BaseCardId, selection.Key, StringComparison.OrdinalIgnoreCase))
                    result[art.BaseCardId] = art.BuiltIn
                        ? $"l12-card-id:{art.CardImageId}"
                        : SiteMediaUrl(art.MediaAssetId);
            }
            foreach (var group in (copies ?? new Dictionary<string, List<string>>()).Take(128))
            {
                for (var index = 0; index < group.Value.Count && index < 50; index++)
                {
                    var artId = group.Value[index];
                    if (string.IsNullOrWhiteSpace(artId)) continue;
                    var art = FindActiveAlternateArtLocked(artId);
                    if (art is null || !owned.Contains(art.Id)
                        || !string.Equals(art.BaseCardId, group.Key, StringComparison.OrdinalIgnoreCase)) continue;
                    result[$"{art.BaseCardId}#{index + 1}"] = art.BuiltIn
                        ? $"l12-card-id:{art.CardImageId}"
                        : SiteMediaUrl(art.MediaAssetId);
                }
            }
            return result;
        }
    }

    private Dictionary<string, string> SanitizeOwnedAlternateArtSelections(string accountId,
        IReadOnlyDictionary<string, string>? selections)
    {
        if (selections is null || selections.Count == 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var owned = _data.AlternateArtGrants.Where(row => row.AccountId == accountId && row.RevokedAt is null)
            .Select(row => row.AlternateArtId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in selections.Take(128))
        {
            var cardId = selection.Key?.Trim();
            var artId = selection.Value?.Trim();
            if (string.IsNullOrWhiteSpace(cardId) || string.IsNullOrWhiteSpace(artId)) continue;
            var art = FindActiveAlternateArtLocked(artId);
            if (art is not null && owned.Contains(art.Id)
                && string.Equals(art.BaseCardId, cardId, StringComparison.OrdinalIgnoreCase))
                result[art.BaseCardId] = art.Id;
        }
        return result;
    }

    private Dictionary<string, List<string>> SanitizeOwnedAlternateArtCopies(string accountId,
        IReadOnlyList<string> cardIds, IReadOnlyDictionary<string, List<string>>? copies)
    {
        if (copies is null || copies.Count == 0) return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var owned = _data.AlternateArtGrants.Where(row => row.AccountId == accountId && row.RevokedAt is null)
            .Select(row => row.AlternateArtId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cardCounts = cardIds.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in copies.Take(128))
        {
            var cardId = group.Key?.Trim() ?? string.Empty;
            if (!cardCounts.TryGetValue(cardId, out var count) || group.Value is null) continue;
            var normalized = group.Value.Take(Math.Min(50, count)).Select(value =>
            {
                var artId = value?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(artId)) return string.Empty;
                var art = FindActiveAlternateArtLocked(artId);
                return art is not null && owned.Contains(art.Id)
                    && string.Equals(art.BaseCardId, cardId, StringComparison.OrdinalIgnoreCase) ? art.Id : string.Empty;
            }).ToList();
            while (normalized.Count < count) normalized.Add(string.Empty);
            if (normalized.Any(value => !string.IsNullOrWhiteSpace(value))) result[cardId] = normalized;
        }
        return result;
    }

    private L12AlternateArtView ToAlternateArtView(AlternateArtRow row) => new(row.Id, row.ArtCode, row.BaseCardId, row.DisplayName,
        row.MediaAssetId, SiteMediaUrl(row.MediaAssetId), SiteMediaUrl(row.MediaAssetId, "thumbnail"), row.Active,
        row.CreatedAt, row.UpdatedAt, row.ProductId,
        _data.AlternateArtProducts.FirstOrDefault(item => item.Id == row.ProductId)?.Name ?? "", "", false,
        _officialCards.TryGetValue(row.BaseCardId, out var baseCard) ? baseCard.NameZh : row.BaseCardId);
    private L12AlternateArtView ToAlternateArtView(L12OfficialAlternateArtDefinition row)
        => new(row.Id, row.ArtCode, row.BaseCardId, row.DisplayName, "", "", "", true,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "", row.ProductName, row.CardImageId, true,
            _officialCards.TryGetValue(row.BaseCardId, out var baseCard) ? baseCard.NameZh : row.BaseCardId);
    private L12AlternateArtView? FindAlternateArtLocked(string alternateArtId)
    {
        if (_officialAlternateArts.TryGetValue(alternateArtId, out var official))
            return ToAlternateArtView(official);
        var custom = _data.AlternateArts.FirstOrDefault(row => row.Id == alternateArtId);
        return custom is null ? null : ToAlternateArtView(custom);
    }

    private L12AlternateArtView? FindActiveAlternateArtLocked(string alternateArtId)
    {
        var art = FindAlternateArtLocked(alternateArtId);
        return art is { Active: true } ? art : null;
    }
    private static L12AlternateArtProductView ToAlternateArtProductView(AlternateArtProductRow row)
        => new(row.Id, row.Name, row.Active, row.CreatedAt, row.UpdatedAt);

    private L12AlternateArtGrantView ToAlternateArtGrantView(AlternateArtGrantRow row)
        => new(row.Id, row.AccountId, _data.Accounts.FirstOrDefault(account => account.Id == row.AccountId)?.Username ?? "已删除账号",
            row.AlternateArtId, row.SourceKind, row.SourceReference, row.GrantedAt, row.RevokedAt);

    private static string AlternateArtGrantReason(AlternateArtGrantRow row)
    {
        var label = row.SourceKind switch
        {
            "rank-reached" => "赛季达到段位",
            "season-final" => "赛季结算",
            "master-champion-season-final" => "赛季最强主宰",
            "event" => "活动派发",
            "ranked-participants" => "赛季排位参与",
            _ => "管理员派发",
        };
        return string.IsNullOrWhiteSpace(row.SourceReference) ? label : $"{label}（{row.SourceReference}）";
    }

    private static L12AlternateArtAwardRuleView ToAlternateArtAwardRuleView(AlternateArtAwardRuleRow row)
        => new(row.Id, row.AlternateArtId, row.Kind, row.SeasonId, row.EventId, row.MinimumTierIndex,
            row.Active, row.CreatedAt, row.UpdatedAt, row.MasterId);
}
