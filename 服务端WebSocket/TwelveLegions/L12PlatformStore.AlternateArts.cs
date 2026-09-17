namespace TwelveLegions.Server;

public sealed record L12AlternateArtView(string Id, string BaseCardId, string DisplayName, string MediaAssetId,
    string ImageUrl, string ThumbnailUrl, bool Active, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record L12AlternateArtGrantView(string Id, string AccountId, string Username, string AlternateArtId,
    string SourceKind, string SourceReference, DateTimeOffset GrantedAt, DateTimeOffset? RevokedAt);
public sealed record L12AlternateArtDraft(string? Id, string BaseCardId, string DisplayName, string MediaAssetId,
    bool Active = true);
public sealed record L12AlternateArtGrantDraft(string AlternateArtId, string Username, string SourceKind,
    string SourceReference = "");
public sealed record L12AlternateArtAwardRuleView(string Id, string AlternateArtId, string Kind, string SeasonId,
    string EventId, int MinimumTierIndex, bool Active, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record L12AlternateArtAwardRuleDraft(string? Id, string AlternateArtId, string Kind, string SeasonId,
    string EventId, int MinimumTierIndex, bool Active = true);
public sealed record L12AlternateArtEventDispatchDraft(string RuleId, IReadOnlyList<string> Usernames);

public sealed partial class L12PlatformStore
{
    private static readonly IReadOnlySet<string> AlternateArtGrantSources =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "manual", "rank-reached", "season-final", "event" };
    private static readonly IReadOnlySet<string> AlternateArtAwardKinds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rank-reached", "season-final", "event" };

    public IReadOnlyList<L12AlternateArtView> AlternateArts(bool includeInactive = false)
    {
        lock (_gate) return _data.AlternateArts.Where(row => includeInactive || row.Active)
            .OrderByDescending(row => row.UpdatedAt).Select(ToAlternateArtView).ToArray();
    }

    public IReadOnlyList<L12AlternateArtView> OwnedAlternateArts(string accountId)
    {
        lock (_gate)
        {
            var ownedIds = _data.AlternateArtGrants.Where(row => row.AccountId == accountId && row.RevokedAt is null)
                .Select(row => row.AlternateArtId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _data.AlternateArts.Where(row => row.Active && ownedIds.Contains(row.Id))
                .OrderByDescending(row => row.UpdatedAt).Select(ToAlternateArtView).ToArray();
        }
    }

    public L12AlternateArtView SaveAlternateArt(L12AccountView actor, L12AlternateArtDraft draft,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var baseCardId = draft.BaseCardId?.Trim() ?? string.Empty;
            if (!_officialCards.ContainsKey(baseCardId)) throw new ArgumentException("异画必须绑定到现有的规则卡牌编号");
            var media = ActiveMedia(draft.MediaAssetId) ?? throw new ArgumentException("异画素材不存在或已删除");
            if (!string.Equals(media.Kind, "card-art", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("异画必须使用“卡牌异画”素材上传入口的图片");
            var displayName = LimitSiteText(draft.DisplayName, 100);
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("异画名称不能为空");
            var row = string.IsNullOrWhiteSpace(draft.Id) ? null : _data.AlternateArts.FirstOrDefault(item => item.Id == draft.Id);
            var previous = row is null ? null : System.Text.Json.JsonSerializer.Serialize(ToAlternateArtView(row));
            if (row is null)
            {
                row = new AlternateArtRow { CreatedByAccountId = actor.Id };
                _data.AlternateArts.Add(row);
            }
            row.BaseCardId = baseCardId;
            row.DisplayName = displayName;
            row.MediaAssetId = media.Id;
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
            var art = _data.AlternateArts.FirstOrDefault(row => row.Id == draft.AlternateArtId && row.Active)
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
            var art = _data.AlternateArts.FirstOrDefault(row => row.Id == draft.AlternateArtId)
                ?? throw new KeyNotFoundException("异画不存在");
            var kind = draft.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!AlternateArtAwardKinds.Contains(kind)) throw new ArgumentException("发放规则类型无效");
            var seasonId = LimitSiteText(draft.SeasonId, 100);
            var eventId = LimitSiteText(draft.EventId, 160);
            if (kind is "rank-reached" or "season-final" && string.IsNullOrWhiteSpace(seasonId))
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

    private bool IsActiveAlternateArt(string alternateArtId)
        => _data.AlternateArts.Any(row => row.Id == alternateArtId && row.Active);

    private AlternateArtGrantRow GrantAlternateArtToAccountLocked(string accountId, string alternateArtId,
        string sourceKind, string sourceReference, string grantedByAccountId)
    {
        var art = _data.AlternateArts.FirstOrDefault(row => row.Id == alternateArtId && row.Active)
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
        IReadOnlyDictionary<string, string>? selections)
    {
        if (string.IsNullOrWhiteSpace(accountId) || selections is null || selections.Count == 0) return new Dictionary<string, string>();
        lock (_gate)
        {
            var owned = _data.AlternateArtGrants.Where(row => row.AccountId == accountId && row.RevokedAt is null)
                .Select(row => row.AlternateArtId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var selection in selections.Take(128))
            {
                var art = _data.AlternateArts.FirstOrDefault(row => row.Id == selection.Value && row.Active &&
                    string.Equals(row.BaseCardId, selection.Key, StringComparison.OrdinalIgnoreCase));
                if (art is not null && owned.Contains(art.Id)) result[art.BaseCardId] = SiteMediaUrl(art.MediaAssetId);
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
            var art = _data.AlternateArts.FirstOrDefault(row => row.Id == artId && row.Active
                && string.Equals(row.BaseCardId, cardId, StringComparison.OrdinalIgnoreCase));
            if (art is not null && owned.Contains(art.Id)) result[art.BaseCardId] = art.Id;
        }
        return result;
    }

    private L12AlternateArtView ToAlternateArtView(AlternateArtRow row) => new(row.Id, row.BaseCardId, row.DisplayName,
        row.MediaAssetId, SiteMediaUrl(row.MediaAssetId), SiteMediaUrl(row.MediaAssetId, "thumbnail"), row.Active,
        row.CreatedAt, row.UpdatedAt);

    private L12AlternateArtGrantView ToAlternateArtGrantView(AlternateArtGrantRow row)
        => new(row.Id, row.AccountId, _data.Accounts.FirstOrDefault(account => account.Id == row.AccountId)?.Username ?? "已删除账号",
            row.AlternateArtId, row.SourceKind, row.SourceReference, row.GrantedAt, row.RevokedAt);

    private static L12AlternateArtAwardRuleView ToAlternateArtAwardRuleView(AlternateArtAwardRuleRow row)
        => new(row.Id, row.AlternateArtId, row.Kind, row.SeasonId, row.EventId, row.MinimumTierIndex,
            row.Active, row.CreatedAt, row.UpdatedAt);
}
