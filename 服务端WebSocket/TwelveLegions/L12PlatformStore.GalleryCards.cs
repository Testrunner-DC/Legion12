namespace TwelveLegions.Server;

public sealed record L12GalleryPresentationCatalogView(
    IReadOnlyList<L12AlternateArtView> RegularCards,
    IReadOnlyList<L12AlternateArtProductView> FilterProducts);
public sealed record L12RegularGalleryCardMetadataDraft(string CardId, IReadOnlyList<string> ProductIds,
    IReadOnlyList<string> RarityIds, string? Artist = null);
public sealed record L12RegularGalleryCardImageDraft(string CardId, string MediaAssetId);
public sealed record L12AlternateArtProductFilterVisibilityDraft(string ProductId, bool ShowInGalleryFilter);

public sealed partial class L12PlatformStore
{
    private const string RegularGalleryCardMetadataPrefix = "regular-card:";

    public L12GalleryPresentationCatalogView GalleryPresentationCatalog()
    {
        lock (_gate)
        {
            var cards = _officialCards.Values.Select(card => ToRegularGalleryCardViewLocked(card, false))
                .OrderBy(card => card.ArtCode, StringComparer.OrdinalIgnoreCase).ToArray();
            var products = ResolveProductsLocked(AllAlternateArtProductsLocked().Select(item => item.Id).ToArray(), false)
                .Select(WithGalleryFilterPreferenceLocked)
                .OrderBy(item => item.CategoryName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            return new(cards, products);
        }
    }

    public L12AlternateArtSearchPage SearchRegularGalleryCards(string? name, string? cardCode,
        string? productId, int page = 1, int pageSize = 20)
    {
        lock (_gate)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var nameTerm = LimitSiteText(name, 100);
            var codeTerm = LimitSiteText(cardCode, 80);
            var canonicalProductId = string.IsNullOrWhiteSpace(productId)
                ? string.Empty : ResolveMergedProductIdLocked(productId.Trim());
            var query = _officialCards.Values.Select(card => ToRegularGalleryCardViewLocked(card, true));
            if (!string.IsNullOrWhiteSpace(nameTerm))
                query = query.Where(card => card.DisplayName.Contains(nameTerm, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(codeTerm))
                query = query.Where(card => card.ArtCode.Contains(codeTerm, StringComparison.OrdinalIgnoreCase)
                    || card.Id.Contains(codeTerm, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(canonicalProductId))
                query = query.Where(card => card.Products?.Any(item => item.Id == canonicalProductId) == true);
            var ordered = query.OrderBy(card => card.ArtCode, StringComparer.OrdinalIgnoreCase).ToArray();
            return new(ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), ordered.Length, page, pageSize);
        }
    }

    public L12AlternateArtView SaveRegularGalleryCardMetadata(L12AccountView actor,
        L12RegularGalleryCardMetadataDraft draft, L12AdminAuditContext? context = null)
    {
        RequireLocalGalleryDefinitions();
        lock (_gate)
        {
            var cardId = draft.CardId?.Trim() ?? string.Empty;
            if (!_officialCards.TryGetValue(cardId, out var card)) throw new KeyNotFoundException("常规卡不存在");
            var productIds = NormalizeIds(draft.ProductIds).Select(ResolveMergedProductIdLocked)
                .Distinct(StringComparer.Ordinal).ToArray();
            var rarityIds = NormalizeIds(draft.RarityIds);
            var knownProducts = AllAlternateArtProductsLocked().Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            if (productIds.Any(id => !knownProducts.Contains(id))) throw new ArgumentException("常规卡归属产品不存在");
            var knownRarities = _data.AlternateArtRarities.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            if (rarityIds.Any(id => !knownRarities.Contains(id))) throw new ArgumentException("常规卡罕贵不存在");

            var before = ToRegularGalleryCardViewLocked(card, true);
            var key = RegularGalleryMetadataKey(cardId);
            var row = _data.AlternateArtMetadata.FirstOrDefault(item => item.AlternateArtId == key);
            if (row is null)
            {
                row = new AlternateArtMetadataRow { AlternateArtId = key };
                _data.AlternateArtMetadata.Add(row);
            }
            row.ProductIds = productIds.ToList();
            row.RarityIds = rarityIds.ToList();
            if (draft.Artist is not null) row.Artist = LimitSiteText(draft.Artist, 120);
            row.UpdatedByAccountId = actor.Id;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            var after = ToRegularGalleryCardViewLocked(card, true);
            AddAdminAudit(actor, "gallery-regular-card-metadata", "update", cardId,
                System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(after),
                null, context);
            Save();
            return after;
        }
    }

    public L12AlternateArtView SaveRegularGalleryCardImage(L12AccountView actor,
        L12RegularGalleryCardImageDraft draft, L12AdminAuditContext? context = null)
    {
        RequireLocalGalleryDefinitions();
        lock (_gate)
        {
            var cardId = draft.CardId?.Trim() ?? string.Empty;
            if (!_officialCards.TryGetValue(cardId, out var card)) throw new KeyNotFoundException("常规卡不存在");
            var mediaId = draft.MediaAssetId?.Trim() ?? string.Empty;
            SiteMediaRow? media = null;
            if (!string.IsNullOrWhiteSpace(mediaId))
            {
                media = ActiveMedia(mediaId) ?? throw new ArgumentException("常规卡素材不存在或已删除");
                if (!string.Equals(media.Kind, "card-art", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("常规卡必须使用“卡牌异画”素材上传入口的图片");
            }

            var before = ToRegularGalleryCardViewLocked(card, true);
            var key = RegularGalleryMetadataKey(cardId);
            var row = _data.AlternateArtMetadata.FirstOrDefault(item => item.AlternateArtId == key);
            if (row is null)
            {
                row = new AlternateArtMetadataRow
                {
                    AlternateArtId = key,
                    ProductIds = DefaultRegularProductIdsLocked(cardId).ToList(),
                };
                _data.AlternateArtMetadata.Add(row);
            }
            row.ImageMediaAssetId = media?.Id ?? string.Empty;
            row.UpdatedByAccountId = actor.Id;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            var after = ToRegularGalleryCardViewLocked(card, true);
            AddAdminAudit(actor, "gallery-regular-card-image", media is null ? "restore-catalog-image" : "replace-image",
                cardId, System.Text.Json.JsonSerializer.Serialize(before),
                System.Text.Json.JsonSerializer.Serialize(after), null, context);
            Save();
            return after;
        }
    }

    public L12AlternateArtProductView SaveGalleryProductFilterVisibility(L12AccountView actor,
        L12AlternateArtProductFilterVisibilityDraft draft, L12AdminAuditContext? context = null)
    {
        RequireLocalGalleryDefinitions();
        lock (_gate)
        {
            var productId = ResolveMergedProductIdLocked(draft.ProductId?.Trim() ?? string.Empty);
            var product = AllAlternateArtProductsLocked().FirstOrDefault(item => item.Id == productId)
                ?? throw new KeyNotFoundException("收录产品不存在");
            var before = WithGalleryFilterPreferenceLocked(product);
            var row = _data.AlternateArtProductPreferences.FirstOrDefault(item => item.ProductId == productId);
            if (row is null)
            {
                row = new AlternateArtProductPreferenceRow { ProductId = productId };
                _data.AlternateArtProductPreferences.Add(row);
            }
            row.ShowInGalleryFilter = draft.ShowInGalleryFilter;
            row.UpdatedByAccountId = actor.Id;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            var after = WithGalleryFilterPreferenceLocked(product);
            AddAdminAudit(actor, "gallery-product-filter", "update", productId,
                System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(after),
                null, context);
            Save();
            return after;
        }
    }

    private L12AlternateArtProductView WithGalleryFilterPreferenceLocked(L12AlternateArtProductView product)
    {
        var canonicalId = ResolveMergedProductIdLocked(product.Id);
        var preference = _data.AlternateArtProductPreferences.FirstOrDefault(item => item.ProductId == canonicalId);
        return product with { ShowInGalleryFilter = preference?.ShowInGalleryFilter ?? true };
    }

    private L12AlternateArtView ToRegularGalleryCardViewLocked(L12CardDefinition card, bool admin)
    {
        var metadata = _data.AlternateArtMetadata.FirstOrDefault(item =>
            item.AlternateArtId == RegularGalleryMetadataKey(card.Id));
        var productIds = metadata?.ProductIds ?? DefaultRegularProductIdsLocked(card.Id);
        var products = ResolveProductsLocked(productIds, admin);
        IReadOnlyList<L12AlternateArtRarityView>? rarities = metadata is not null && metadata.RarityIds.Count > 0
            ? ResolveRaritiesLocked(metadata.RarityIds) : null;
        var mediaId = metadata?.ImageMediaAssetId ?? string.Empty;
        var imageUrl = string.IsNullOrWhiteSpace(mediaId) ? card.ImageUrl ?? string.Empty : SiteMediaUrl(mediaId);
        var thumbnailUrl = string.IsNullOrWhiteSpace(mediaId) ? string.Empty : SiteMediaUrl(mediaId, "thumbnail");
        return new(card.Id, card.Number, card.Id, card.NameZh, mediaId, imageUrl, thumbnailUrl, true,
            DateTimeOffset.UnixEpoch, metadata?.UpdatedAt ?? DateTimeOffset.UnixEpoch,
            products.FirstOrDefault()?.Id ?? string.Empty, products.FirstOrDefault()?.Name ?? string.Empty,
            card.Id, true, card.NameZh, null, string.Empty, products, rarities, "regular-card",
            card.Effect ?? string.Empty, metadata?.Artist ?? string.Empty);
    }

    private IReadOnlyList<string> DefaultRegularProductIdsLocked(string cardId)
        => _officialCardProducts.TryGetValue(cardId, out var products)
            ? products.Select(value => ResolveMergedProductIdLocked($"catalog:{value}"))
                .Distinct(StringComparer.Ordinal).ToArray()
            : [];

    private static string RegularGalleryMetadataKey(string cardId) => RegularGalleryCardMetadataPrefix + cardId;
}
