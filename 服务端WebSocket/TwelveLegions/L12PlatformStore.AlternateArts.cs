namespace TwelveLegions.Server;

public sealed record L12AlternateArtView(string Id, string ArtCode, string BaseCardId, string DisplayName, string MediaAssetId,
    string ImageUrl, string ThumbnailUrl, bool Active, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string ProductId = "", string ProductName = "", string CardImageId = "", bool BuiltIn = false,
    string BaseCardName = "", DateTimeOffset? GrantedAt = null, string GrantReason = "",
    IReadOnlyList<L12AlternateArtProductView>? Products = null,
    IReadOnlyList<L12AlternateArtRarityView>? Rarities = null, string Kind = "alternate-art",
    string Effect = "");
public sealed record L12AlternateArtSearchPage(IReadOnlyList<L12AlternateArtView> Items, int Total, int Page, int PageSize);
public sealed record L12AlternateArtGrantNotificationView(string Id, string AlternateArtId, string DisplayName,
    string ArtCode, string BaseCardId, string BaseCardName, string Reason, DateTimeOffset GrantedAt,
    string ImageUrl, string ThumbnailUrl, string CardImageId, bool BuiltIn);
public sealed record L12AlternateArtProductView(string Id, string Name, bool Active,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string CategoryId = "", string CategoryName = "",
    string Source = "legacy", string Status = "active");
public sealed record L12AlternateArtRarityView(string Id, string Code, string Name, bool Active,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record L12AlternateArtGrantView(string Id, string AccountId, string Username, string AlternateArtId,
    string SourceKind, string SourceReference, DateTimeOffset GrantedAt, DateTimeOffset? RevokedAt);
public sealed record L12AlternateArtDraft(string? Id, string ArtCode, string BaseCardId, string DisplayName, string MediaAssetId,
    bool Active = true, string ProductId = "", string Kind = "alternate-art", string Effect = "");
public sealed record L12AlternateArtProductDraft(string? Id, string Name, bool Active = true);
public sealed record L12AlternateArtRarityDraft(string? Id, string Code, string Name, bool Active = true);
public sealed record L12AlternateArtMetadataDraft(string AlternateArtId, IReadOnlyList<string> ProductIds,
    IReadOnlyList<string> RarityIds);
public sealed record L12AlternateArtGrantDraft(string AlternateArtId, string AccountId, string SourceKind,
    string SourceReference = "");
public sealed record L12AlternateArtAwardRuleView(string Id, string AlternateArtId, string Kind, string SeasonId,
    string EventId, int MinimumTierIndex, bool Active, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string MasterId = "");
public sealed record L12AlternateArtAwardRuleDraft(string? Id, string AlternateArtId, string Kind, string SeasonId,
    string EventId, int MinimumTierIndex, bool Active = true, string MasterId = "");
public sealed record L12AlternateArtEventDispatchDraft(string RuleId, IReadOnlyList<string> AccountIds);
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
        lock (_gate) return AllAlternateArtProductsLocked().Where(row => includeInactive || row.Active)
            .OrderBy(row => row.CategoryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public L12AlternateArtProductView SaveAlternateArtProduct(L12AccountView actor, L12AlternateArtProductDraft draft,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var name = LimitSiteText(draft.Name, 100);
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("异画归属产品名称不能为空");
            var requestedId = RemoveProductSourcePrefix(draft.Id, "legacy:");
            var row = string.IsNullOrWhiteSpace(requestedId) ? null : _data.AlternateArtProducts.FirstOrDefault(item => item.Id == requestedId);
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
        lock (_gate) return _officialAlternateArts.Values.Select(row => ToAlternateArtView(row, false))
            .Concat(_data.AlternateArts.Where(row => includeInactive || row.Active).Select(row => ToAlternateArtView(row, false)))
            .OrderBy(row => row.ArtCode, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<L12AlternateArtView> AdminAlternateArts(bool includeInactive = true)
    {
        lock (_gate) return _officialAlternateArts.Values.Select(row => ToAlternateArtView(row, true))
            .Concat(_data.AlternateArts.Where(row => includeInactive || row.Active).Select(row => ToAlternateArtView(row, true)))
            .OrderBy(row => row.ArtCode, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<L12AlternateArtRarityView> AlternateArtRarities(bool includeInactive = false)
    {
        lock (_gate) return _data.AlternateArtRarities.Where(row => includeInactive || row.Active)
            .OrderBy(row => row.Code, StringComparer.OrdinalIgnoreCase).Select(ToAlternateArtRarityView).ToArray();
    }

    public L12AlternateArtRarityView SaveAlternateArtRarity(L12AccountView actor, L12AlternateArtRarityDraft draft,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var code = LimitSiteText(draft.Code, 40).ToUpperInvariant();
            var name = LimitSiteText(draft.Name, 80);
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("罕贵编号和名称不能为空");
            var row = string.IsNullOrWhiteSpace(draft.Id) ? null
                : _data.AlternateArtRarities.FirstOrDefault(item => item.Id == draft.Id);
            if (_data.AlternateArtRarities.Any(item => item.Id != row?.Id &&
                item.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("罕贵编号已存在");
            var previous = row is null ? null : System.Text.Json.JsonSerializer.Serialize(ToAlternateArtRarityView(row));
            if (row is null)
            {
                row = new AlternateArtRarityRow { CreatedByAccountId = actor.Id };
                _data.AlternateArtRarities.Add(row);
            }
            row.Code = code;
            row.Name = name;
            row.Active = draft.Active;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            var view = ToAlternateArtRarityView(row);
            AddAdminAudit(actor, "alternate-art-rarity", previous is null ? "create" : "update", row.Id,
                previous, System.Text.Json.JsonSerializer.Serialize(view), null, context);
            Save();
            return view;
        }
    }

    public L12AlternateArtView SaveAlternateArtMetadata(L12AccountView actor, L12AlternateArtMetadataDraft draft,
        L12AdminAuditContext? context = null)
    {
        lock (_gate)
        {
            var artId = draft.AlternateArtId?.Trim() ?? string.Empty;
            var before = FindAlternateArtLocked(artId, true) ?? throw new KeyNotFoundException("异画不存在");
            var productIds = NormalizeIds(draft.ProductIds);
            var rarityIds = NormalizeIds(draft.RarityIds);
            var knownProducts = AllAlternateArtProductsLocked().Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            if (productIds.Any(id => !knownProducts.Contains(id))) throw new ArgumentException("异画归属产品不存在");
            var knownRarities = _data.AlternateArtRarities.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            if (rarityIds.Any(id => !knownRarities.Contains(id))) throw new ArgumentException("异画罕贵不存在");
            var row = _data.AlternateArtMetadata.FirstOrDefault(item => item.AlternateArtId == artId);
            if (row is null)
            {
                row = new AlternateArtMetadataRow { AlternateArtId = artId };
                _data.AlternateArtMetadata.Add(row);
            }
            row.ProductIds = productIds.ToList();
            row.RarityIds = rarityIds.ToList();
            row.UpdatedByAccountId = actor.Id;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            var after = FindAlternateArtLocked(artId, true)!;
            AddAdminAudit(actor, "alternate-art-metadata", "update", artId,
                System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(after), null, context);
            Save();
            return after;
        }
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
            var query = _officialAlternateArts.Values.Select(row => ToAlternateArtView(row, true))
                .Concat(_data.AlternateArts.Where(row => includeInactive || row.Active).Select(row => ToAlternateArtView(row, true)));
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
            return _officialAlternateArts.Values.Select(row => ToAlternateArtView(row, false))
                .Concat(_data.AlternateArts.Where(row => row.Active).Select(row => ToAlternateArtView(row, false)))
                .Where(row => grants.ContainsKey(row.Id))
                .Select(row =>
                {
                    var grant = grants[row.Id];
                    return row with
                    {
                        GrantedAt = grant.GrantedAt,
                        GrantReason = AlternateArtGrantReason(grant),
                    };
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
            var kind = draft.Kind?.Trim().ToLowerInvariant() == "gallery-card" ? "gallery-card" : "alternate-art";
            if (existing is not null && !string.Equals(existing.Kind, kind, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("画廊条目创建后不能在异画与独立联动卡之间切换类型");
            var baseCardId = draft.BaseCardId?.Trim() ?? string.Empty;
            L12CardDefinition? baseCard = null;
            if (kind == "alternate-art" && !_officialCards.TryGetValue(baseCardId, out baseCard))
                throw new ArgumentException("异画必须绑定到现有的规则卡牌编号");
            if (kind == "gallery-card") baseCardId = string.Empty;
            var media = ActiveMedia(draft.MediaAssetId) ?? throw new ArgumentException("异画素材不存在或已删除");
            if (!string.Equals(media.Kind, "card-art", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("异画必须使用“卡牌异画”素材上传入口的图片");
            // 异画是原卡的外观权益，名称属于规则卡牌身份，不能由上传表单另造或改写。
            var displayName = kind == "alternate-art" ? LimitSiteText(baseCard!.NameZh, 100)
                : LimitSiteText(draft.DisplayName, 100);
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("独立联动卡必须填写名称");
            var productId = RemoveProductSourcePrefix(draft.ProductId, "legacy:");
            if (!string.IsNullOrWhiteSpace(productId) && !_data.AlternateArtProducts.Any(item => item.Id == productId && item.Active))
                throw new ArgumentException("异画归属产品不存在或已停用");
            var row = existing;
            var previous = row is null ? null : System.Text.Json.JsonSerializer.Serialize(ToAlternateArtView(row, true));
            if (row is null)
            {
                row = new AlternateArtRow { CreatedByAccountId = actor.Id };
                _data.AlternateArts.Add(row);
            }
            row.BaseCardId = baseCardId;
            row.ArtCode = artCode;
            row.DisplayName = displayName;
            row.Kind = kind;
            row.Effect = LimitSiteText(draft.Effect, 2000);
            row.MediaAssetId = media.Id;
            row.ProductId = productId;
            row.Active = draft.Active;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(productId))
            {
                var metadata = _data.AlternateArtMetadata.FirstOrDefault(item => item.AlternateArtId == row.Id);
                if (metadata is null)
                {
                    metadata = new AlternateArtMetadataRow { AlternateArtId = row.Id };
                    _data.AlternateArtMetadata.Add(metadata);
                }
                metadata.ProductIds = [$"legacy:{productId}"];
                metadata.UpdatedByAccountId = actor.Id;
                metadata.UpdatedAt = DateTimeOffset.UtcNow;
            }
            var view = ToAlternateArtView(row, true);
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
            var art = RequireGrantableAlternateArtLocked(draft.AlternateArtId);
            var accountId = draft.AccountId?.Trim() ?? string.Empty;
            var account = _data.Accounts.FirstOrDefault(row => row.Id == accountId && !row.Deleted)
                ?? throw new KeyNotFoundException("目标玩家不存在");
            if (account.Disabled) throw new ArgumentException("目标玩家账号已禁用，不能派发异画");
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
            row.RecipientReason = reference;
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
            if (art.Kind == "gallery-card") throw new ArgumentException("独立联动卡不能设置异画发放规则");
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
            var accountIds = (draft.AccountIds ?? []).Select(value => value?.Trim()).Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal).Take(500).ToArray();
            if (accountIds.Length == 0) throw new ArgumentException("请至少选择一个玩家账号");
            var accounts = accountIds.Select(accountId => _data.Accounts.FirstOrDefault(row => row.Id == accountId && !row.Deleted)
                ?? throw new KeyNotFoundException($"目标玩家不存在：{accountId}")).ToArray();
            var disabled = accounts.FirstOrDefault(account => account.Disabled);
            if (disabled is not null) throw new ArgumentException($"目标玩家账号已禁用：{disabled.Id}");
            var views = new List<L12AlternateArtGrantView>();
            foreach (var account in accounts)
            {
                var grant = GrantAlternateArtToAccountLocked(account.Id, rule.AlternateArtId, "event", rule.EventId, actor.Id);
                views.Add(ToAlternateArtGrantView(grant));
            }
            AddAdminAudit(actor, "alternate-art-award-rule", "dispatch-event", rule.Id, null,
                $"event={rule.EventId};players={accountIds.Length}", null, context);
            Save();
            return views;
        }
    }

    public L12AlternateArtRankedParticipantDispatchPreview PreviewRankedParticipantAlternateArtDispatch(
        L12AlternateArtRankedParticipantDispatchDraft draft)
    {
        lock (_gate)
        {
            var art = RequireGrantableAlternateArtLocked(draft.AlternateArtId);
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
        => _data.RankedProfiles.Where(row => SeasonIdsEqual(row.SeasonId, seasonId)
                && row.PlacementPlayed + row.Wins + row.Losses > 0)
            .Select(row => row.AccountId)
            .Concat(_data.RankedProfileHistory.Where(row => SeasonIdsEqual(row.SeasonId, seasonId)
                    && row.PlacementPlayed + row.Wins + row.Losses > 0)
                .Select(row => row.AccountId))
            .Concat(_data.RankedMasterRecords.Where(row => SeasonIdsEqual(row.SeasonId, seasonId)
                    && row.Games > 0)
                .Select(row => row.AccountId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void ApplyRankReachedAlternateArtAwardsLocked(string accountId, string seasonId, int tierIndex)
    {
        foreach (var rule in _data.AlternateArtAwardRules.Where(row => row.Active && row.Kind == "rank-reached"
                     && SeasonIdsEqual(row.SeasonId, seasonId)
                     && tierIndex >= row.MinimumTierIndex))
            if (IsActiveGrantableAlternateArt(rule.AlternateArtId))
                GrantAlternateArtToAccountLocked(accountId, rule.AlternateArtId, "rank-reached", seasonId, "system");
    }

    private void ApplySeasonFinalAlternateArtAwardsLocked(string accountId, string seasonId, int tierIndex)
    {
        foreach (var rule in _data.AlternateArtAwardRules.Where(row => row.Active && row.Kind == "season-final"
                     && SeasonIdsEqual(row.SeasonId, seasonId)
                     && tierIndex >= row.MinimumTierIndex))
            if (IsActiveGrantableAlternateArt(rule.AlternateArtId))
                GrantAlternateArtToAccountLocked(accountId, rule.AlternateArtId, "season-final", seasonId, "system");
    }

    private void ApplyMasterChampionSeasonFinalAlternateArtAwardsLocked(
        IReadOnlyDictionary<string, RankedMasterRecordRow> champions, string seasonId)
    {
        foreach (var rule in _data.AlternateArtAwardRules.Where(row => row.Active && row.Kind == "master-champion-season-final"
                     && SeasonIdsEqual(row.SeasonId, seasonId)))
        {
            if (!IsActiveGrantableAlternateArt(rule.AlternateArtId)) continue;
            foreach (var champion in champions.Values.Where(item => string.IsNullOrWhiteSpace(rule.MasterId)
                         || item.MasterId.Equals(rule.MasterId, StringComparison.OrdinalIgnoreCase)))
                GrantAlternateArtToAccountLocked(champion.AccountId, rule.AlternateArtId,
                    "master-champion-season-final", $"{seasonId}:{champion.MasterId}", "system");
        }
    }

    private bool IsActiveGrantableAlternateArt(string alternateArtId)
        => FindActiveAlternateArtLocked(alternateArtId) is { Kind: not "gallery-card" };

    private L12AlternateArtView RequireGrantableAlternateArtLocked(string alternateArtId)
    {
        var art = FindActiveAlternateArtLocked(alternateArtId)
            ?? throw new KeyNotFoundException("异画不存在或未启用");
        if (art.Kind == "gallery-card")
            throw new ArgumentException("独立联动卡仅用于画廊展示，不能派发为异画权益");
        return art;
    }

    private AlternateArtGrantRow GrantAlternateArtToAccountLocked(string accountId, string alternateArtId,
        string sourceKind, string sourceReference, string grantedByAccountId)
    {
        var art = RequireGrantableAlternateArtLocked(alternateArtId);
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
            var owned = OwnedAlternateArtIdsLocked(accountId);
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
        var owned = OwnedAlternateArtIdsLocked(accountId);
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
        var owned = OwnedAlternateArtIdsLocked(accountId);
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

    private HashSet<string> OwnedAlternateArtIdsLocked(string accountId)
    {
        var owned = _data.AlternateArtGrants
            .Where(row => row.AccountId == accountId && row.RevokedAt is null)
            .Select(row => row.AlternateArtId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return owned;
    }

    private void EnsureAlternateArtMetadataState()
    {
        lock (_gate)
        {
            var changed = false;
            _data.AlternateArtMetadata ??= [];
            _data.AlternateArtRarities ??= [];
            foreach (var row in _data.AlternateArtMetadata)
            {
                row.ProductIds ??= [];
                row.RarityIds ??= [];
            }
            foreach (var code in _officialAlternateArts.Values.SelectMany(item => item.RarityCodes)
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var id = CatalogRarityId(code);
                if (_data.AlternateArtRarities.Any(item => item.Id == id)) continue;
                _data.AlternateArtRarities.Add(new AlternateArtRarityRow
                {
                    Id = id, Code = code.ToUpperInvariant(), Name = code.ToUpperInvariant(), Active = true,
                    CreatedByAccountId = "catalog", CreatedAt = DateTimeOffset.UnixEpoch,
                    UpdatedAt = DateTimeOffset.UnixEpoch,
                });
                changed = true;
            }
            foreach (var art in _data.AlternateArts.Where(item => !string.IsNullOrWhiteSpace(item.ProductId)))
            {
                if (_data.AlternateArtMetadata.Any(item => item.AlternateArtId == art.Id)) continue;
                _data.AlternateArtMetadata.Add(new AlternateArtMetadataRow
                {
                    AlternateArtId = art.Id, ProductIds = [$"legacy:{art.ProductId}"],
                    UpdatedByAccountId = art.CreatedByAccountId, UpdatedAt = art.UpdatedAt,
                });
                changed = true;
            }
            if (changed) Save();
        }
    }

    private IReadOnlyList<L12AlternateArtProductView> AllAlternateArtProductsLocked()
    {
        var catalog = _officialCardProducts.Values.SelectMany(value => value)
            .Concat(_officialAlternateArts.Values.SelectMany(value => value.ProductNames))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal)
            .Select(value =>
            {
                var category = CatalogProductCategoryLocked(value);
                return new L12AlternateArtProductView($"catalog:{value}", value, true,
                    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, category?.Id ?? "",
                    category?.Name ?? "", "catalog", "active");
            });
        var site = _data.Articles.Where(row => row.Kind == "product").Select(row =>
            new L12AlternateArtProductView($"site:{row.Id}", row.Title, row.Status != "archived",
                row.CreatedAt, row.UpdatedAt, row.CategoryId ?? "", row.Category, "site", row.Status));
        var legacy = _data.AlternateArtProducts.Select(ToAlternateArtProductView);
        return catalog.Concat(site).Concat(legacy).ToArray();
    }

    private SiteCategoryRow? CatalogProductCategoryLocked(string product)
    {
        var slug = product.StartsWith("ST", StringComparison.OrdinalIgnoreCase) ? "starter-decks" : "card-series";
        return _data.SiteCategories.FirstOrDefault(item => item.Kind == "product" && item.Slug == slug);
    }

    private IReadOnlyList<L12AlternateArtProductView> ResolveProductsLocked(IReadOnlyList<string> ids, bool admin)
    {
        var all = AllAlternateArtProductsLocked().ToDictionary(item => item.Id, StringComparer.Ordinal);
        var result = new List<L12AlternateArtProductView>();
        foreach (var id in ids)
        {
            if (!all.TryGetValue(id, out var item)) continue;
            if (admin) { result.Add(item); continue; }
            if (item.Source == "site")
            {
                var articleId = RemoveProductSourcePrefix(item.Id, "site:");
                var article = _data.Articles.FirstOrDefault(row => row.Id == articleId && row.Kind == "product");
                if (article?.Status != "published" || article.Published is null) continue;
                result.Add(item with
                {
                    Name = article.Published.Title,
                    CategoryId = article.Published.CategoryId ?? "",
                    CategoryName = article.Published.Category,
                    Status = "published",
                });
                continue;
            }
            if (item.Active) result.Add(item);
        }
        return result;
    }

    private IReadOnlyList<L12AlternateArtRarityView> ResolveRaritiesLocked(IReadOnlyList<string> ids)
    {
        var byId = _data.AlternateArtRarities.ToDictionary(item => item.Id, StringComparer.Ordinal);
        return ids.Where(byId.ContainsKey).Select(id => ToAlternateArtRarityView(byId[id])).ToArray();
    }

    private (IReadOnlyList<string> Products, IReadOnlyList<string> Rarities) EffectiveMetadataLocked(
        string artId, IReadOnlyList<string> defaultProducts, IReadOnlyList<string> defaultRarities)
    {
        var metadata = _data.AlternateArtMetadata.FirstOrDefault(item => item.AlternateArtId == artId);
        return metadata is null ? (defaultProducts, defaultRarities) : (metadata.ProductIds, metadata.RarityIds);
    }

    private L12AlternateArtView ToAlternateArtView(AlternateArtRow row, bool admin)
    {
        var defaults = string.IsNullOrWhiteSpace(row.ProductId) ? Array.Empty<string>() : [$"legacy:{row.ProductId}"];
        var metadata = EffectiveMetadataLocked(row.Id, defaults, []);
        var products = ResolveProductsLocked(metadata.Products, admin);
        var rarities = ResolveRaritiesLocked(metadata.Rarities);
        return new(row.Id, row.ArtCode, row.BaseCardId, row.DisplayName,
            row.MediaAssetId, SiteMediaUrl(row.MediaAssetId), SiteMediaUrl(row.MediaAssetId, "thumbnail"), row.Active,
            row.CreatedAt, row.UpdatedAt, products.FirstOrDefault()?.Id ?? "",
            products.FirstOrDefault()?.Name ?? "", "", false,
            _officialCards.TryGetValue(row.BaseCardId, out var baseCard) ? baseCard.NameZh : "",
            null, "", products, rarities, row.Kind, row.Effect);
    }

    private L12AlternateArtView ToAlternateArtView(L12OfficialAlternateArtDefinition row, bool admin)
    {
        var defaultProducts = row.ProductNames.Select(value => $"catalog:{value}").ToArray();
        var defaultRarities = row.RarityCodes.Select(CatalogRarityId).ToArray();
        var metadata = EffectiveMetadataLocked(row.Id, defaultProducts, defaultRarities);
        var products = ResolveProductsLocked(metadata.Products, admin);
        var rarities = ResolveRaritiesLocked(metadata.Rarities);
        return new(row.Id, row.ArtCode, row.BaseCardId, row.DisplayName, "", "", "", true,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, products.FirstOrDefault()?.Id ?? "",
            products.FirstOrDefault()?.Name ?? "", row.CardImageId, true,
            _officialCards.TryGetValue(row.BaseCardId, out var baseCard) ? baseCard.NameZh : row.BaseCardId,
            null, "", products, rarities);
    }

    private L12AlternateArtView? FindAlternateArtLocked(string alternateArtId, bool admin = false)
    {
        if (_officialAlternateArts.TryGetValue(alternateArtId, out var official))
            return ToAlternateArtView(official, admin);
        var custom = _data.AlternateArts.FirstOrDefault(row => row.Id == alternateArtId);
        return custom is null ? null : ToAlternateArtView(custom, admin);
    }

    private L12AlternateArtView? FindActiveAlternateArtLocked(string alternateArtId)
    {
        var art = FindAlternateArtLocked(alternateArtId);
        return art is { Active: true } ? art : null;
    }
    private static L12AlternateArtProductView ToAlternateArtProductView(AlternateArtProductRow row)
        => new($"legacy:{row.Id}", row.Name, row.Active, row.CreatedAt, row.UpdatedAt,
            Source: "legacy", Status: row.Active ? "active" : "inactive");
    private static L12AlternateArtRarityView ToAlternateArtRarityView(AlternateArtRarityRow row)
        => new(row.Id, row.Code, row.Name, row.Active, row.CreatedAt, row.UpdatedAt);
    private static string CatalogRarityId(string code)
        => $"catalog-rarity:{Uri.EscapeDataString(code.Trim().ToLowerInvariant())}";
    private static string RemoveProductSourcePrefix(string? value, string prefix)
        => value?.StartsWith(prefix, StringComparison.Ordinal) == true ? value[prefix.Length..] : value?.Trim() ?? string.Empty;
    private static IReadOnlyList<string> NormalizeIds(IReadOnlyList<string>? values)
        => (values ?? []).Select(value => value?.Trim() ?? string.Empty).Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal).Take(100).ToArray();

    private L12AlternateArtGrantView ToAlternateArtGrantView(AlternateArtGrantRow row)
        => new(row.Id, row.AccountId, _data.Accounts.FirstOrDefault(account => account.Id == row.AccountId)?.Username ?? "已删除账号",
            row.AlternateArtId, row.SourceKind, row.SourceReference, row.GrantedAt, row.RevokedAt);

    private static string AlternateArtGrantReason(AlternateArtGrantRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.RecipientReason)) return row.RecipientReason.Trim();
        // 兼容升级前由后台直接派发的默认 manual 记录；自动派发的赛季／活动标识只用于审计与幂等，不能展示给玩家。
        return row.SourceKind.Equals("manual", StringComparison.OrdinalIgnoreCase)
            && !row.GrantedByAccountId.Equals("system", StringComparison.OrdinalIgnoreCase)
                ? row.SourceReference?.Trim() ?? string.Empty
                : string.Empty;
    }

    private static L12AlternateArtAwardRuleView ToAlternateArtAwardRuleView(AlternateArtAwardRuleRow row)
        => new(row.Id, row.AlternateArtId, row.Kind, row.SeasonId, row.EventId, row.MinimumTierIndex,
            row.Active, row.CreatedAt, row.UpdatedAt, row.MasterId);
}
