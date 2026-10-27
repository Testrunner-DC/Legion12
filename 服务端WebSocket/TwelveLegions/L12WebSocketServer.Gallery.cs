using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TwelveLegions.Server;

public sealed partial class L12WebSocketServer
{
    private void MapGalleryCatalogEndpoints()
    {
        var app = _app ?? throw new InvalidOperationException("WebSocket server app is not initialized");
        // 画廊是公开展示；权益只在构筑选用和开局二次校验时生效。
        app.MapGet("/api/alternate-arts", () => Results.Ok(_platform.AlternateArts()));
        app.MapGet("/api/gallery/presentation", () => Results.Ok(_platform.GalleryPresentationCatalog()));
        app.MapGet("/api/admin/alternate-art-products", (HttpRequest request, bool? includeInactive) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AlternateArtProducts(includeInactive == true));
        });
        app.MapGet("/api/admin/alternate-art-rarities", (HttpRequest request, bool? includeInactive) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AlternateArtRarities(includeInactive == true));
        });
        app.MapPut("/api/admin/alternate-art-rarities", (HttpRequest request, L12AlternateArtRarityDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveAlternateArtRarity(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_rarity_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        app.MapPut("/api/admin/alternate-art-products", (HttpRequest request, L12AlternateArtProductDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveAlternateArtProduct(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_product_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        app.MapPost("/api/admin/alternate-art-products/merge", (HttpRequest request, L12AlternateArtProductMergeDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.MergeAlternateArtProducts(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_product_merge_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        app.MapPut("/api/admin/alternate-art-products/filter-visibility",
            (HttpRequest request, L12AlternateArtProductFilterVisibilityDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveGalleryProductFilterVisibility(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (ArgumentException error) { return ApiError(request, "gallery_product_settings_invalid", error.Message, StatusCodes.Status400BadRequest); }
            catch (KeyNotFoundException error) { return ApiError(request, "gallery_product_missing", error.Message, StatusCodes.Status404NotFound); }
        });
        app.MapGet("/api/admin/alternate-arts", (HttpRequest request, bool? includeInactive) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AdminAlternateArts(includeInactive == true));
        });
        app.MapGet("/api/admin/alternate-arts/search", (HttpRequest request, string? name, string? artCode,
            string? baseCard, int? page, int? pageSize, bool? includeInactive) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.SearchAlternateArts(name, artCode, baseCard, page ?? 1, pageSize ?? 20,
                includeInactive != false));
        });
        app.MapGet("/api/admin/gallery/regular-cards/search", (HttpRequest request, string? name,
            string? cardCode, string? productId, int? page, int? pageSize) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.SearchRegularGalleryCards(name, cardCode, productId,
                page ?? 1, pageSize ?? 20));
        });
        app.MapPut("/api/admin/gallery/regular-cards/metadata",
            (HttpRequest request, L12RegularGalleryCardMetadataDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveRegularGalleryCardMetadata(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (KeyNotFoundException error) { return ApiError(request, "gallery_regular_card_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (ArgumentException error) { return ApiError(request, "gallery_regular_card_metadata_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        app.MapPut("/api/admin/gallery/regular-cards/image",
            (HttpRequest request, L12RegularGalleryCardImageDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveRegularGalleryCardImage(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (KeyNotFoundException error) { return ApiError(request, "gallery_regular_card_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (ArgumentException error) { return ApiError(request, "gallery_regular_card_image_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
    }

    private void MapAlternateArtAdminEndpoints()
    {
        var app = _app ?? throw new InvalidOperationException("WebSocket server app is not initialized");
        app.MapPut("/api/admin/alternate-arts", (HttpRequest request, L12AlternateArtDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveAlternateArt(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        app.MapPut("/api/admin/alternate-arts/metadata", (HttpRequest request, L12AlternateArtMetadataDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveAlternateArtMetadata(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_metadata_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        app.MapPut("/api/admin/alternate-arts/image", (HttpRequest request, L12AlternateArtImageDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveAlternateArtImage(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_image_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        app.MapGet("/api/admin/alternate-art-grants", (HttpRequest request, string? username) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AlternateArtGrants(username));
        });
        app.MapGet("/api/admin/alternate-art-grants/search", (HttpRequest request, string? query,
            string? sourceKind, string? state, int? page, int? pageSize) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            try
            {
                return Results.Ok(_platform.SearchAlternateArtGrants(query, sourceKind, state,
                    page ?? 1, pageSize ?? 20));
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "alternate_art_grant_search_invalid", error.Message,
                    StatusCodes.Status400BadRequest);
            }
        });
        app.MapPost("/api/admin/alternate-art-grants", (HttpRequest request, L12AlternateArtGrantDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var grant = _platform.GrantAlternateArt(authenticated.Account, draft, RequestAuditContext(request, permission));
                NotifyAlternateArtNotificationsChanged([grant.AccountId]);
                return Results.Ok(grant);
            }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_target_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_grant_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        app.MapDelete("/api/admin/alternate-art-grants/{id}", (HttpRequest request, string id) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { _platform.RevokeAlternateArtGrant(authenticated.Account, id, RequestAuditContext(request, permission)); return Results.NoContent(); }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_grant_missing", error.Message, StatusCodes.Status404NotFound); }
        });
        app.MapGet("/api/admin/alternate-art-award-rules", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AlternateArtAwardRules());
        });
        app.MapPut("/api/admin/alternate-art-award-rules", (HttpRequest request, L12AlternateArtAwardRuleDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveAlternateArtAwardRule(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_award_rule_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        app.MapPost("/api/admin/alternate-art-award-rules/event-dispatch", (HttpRequest request, L12AlternateArtEventDispatchDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var grants = _platform.DispatchAlternateArtEvent(authenticated.Account, draft, RequestAuditContext(request, permission));
                NotifyAlternateArtNotificationsChanged(grants.Select(grant => grant.AccountId));
                return Results.Ok(grants);
            }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_event_target_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_event_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        app.MapPost("/api/admin/alternate-art-grants/ranked-participants/preview", (HttpRequest request, L12AlternateArtRankedParticipantDispatchDraft draft) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            try { return Results.Ok(_platform.PreviewRankedParticipantAlternateArtDispatch(draft)); }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_missing", error.Message, StatusCodes.Status404NotFound); }
        });
        app.MapPost("/api/admin/alternate-art-grants/ranked-participants/dispatch", (HttpRequest request, L12AlternateArtRankedParticipantDispatchDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var grants = _platform.DispatchRankedParticipantAlternateArt(authenticated.Account, draft, RequestAuditContext(request, permission));
                NotifyAlternateArtNotificationsChanged(grants.Select(grant => grant.AccountId));
                return Results.Ok(grants);
            }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_missing", error.Message, StatusCodes.Status404NotFound); }
        });
    }
}
