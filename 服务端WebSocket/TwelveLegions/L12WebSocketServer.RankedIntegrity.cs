using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TwelveLegions.Server;

public sealed partial class L12WebSocketServer
{
    private sealed record IntegrityConfirmRequest(L12RankedIntegrityActionInput Input, long ExpectedRevision);
    private sealed record IntegrityAppealRequest(string DecisionId, string RequestId, string Statement);

    private IResult IntegrityRequest(HttpRequest request, Func<object?> execute)
    {
        try { return Results.Ok(execute()); }
        catch (KeyNotFoundException error) { return ApiError(request, "integrity_not_found", error.Message, 404); }
        catch (ArgumentException error) { return ApiError(request, "integrity_invalid", error.Message, 400); }
        catch (InvalidOperationException error) { return ApiError(request, "integrity_conflict", error.Message, 409); }
        catch (UnauthorizedAccessException) { return ApiError(request, "integrity_forbidden", "没有访问此处置的权限", 403); }
    }

    private void MapRankedIntegrityEndpoints()
    {
        _app!.MapPost("/api/admin/ranked/integrity/preview", (HttpRequest request, L12RankedIntegrityActionInput input) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminMatchGovernanceWrite, out var auth, out var failure)) return failure;
            return IntegrityRequest(request, () => _platform.PreviewRankedIntegrityAction(auth.Account, input));
        });
        _app.MapPost("/api/admin/ranked/integrity/decisions", (HttpRequest request, IntegrityConfirmRequest body) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminMatchGovernanceWrite, out var auth, out var failure)) return failure;
            return IntegrityRequest(request, () => _platform.ConfirmRankedIntegrityAction(auth.Account, body.Input,
                body.ExpectedRevision, RequestAuditContext(request, L12Permission.AdminMatchGovernanceWrite)));
        });
        _app.MapGet("/api/admin/ranked/integrity/decisions", (HttpRequest request, string? cursor, int? limit) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminAuditRead, out var auth, out var failure)) return failure;
            return IntegrityRequest(request, () => _platform.RankedIntegrityDecisions(auth.Account, cursor, limit ?? 20));
        });
        _app.MapGet("/api/ranked/integrity/notifications", (HttpRequest request, string? cursor, int? limit, bool? unreadOnly) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            return IntegrityRequest(request, () => _platform.RankedIntegrityNotifications(account, cursor, limit ?? 20, unreadOnly ?? false));
        });
        _app.MapPost("/api/ranked/integrity/notifications/{id}/ack", (HttpRequest request, string id) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            return IntegrityRequest(request, () => { _platform.AcknowledgeRankedIntegrityNotification(account, id); return new { acknowledged = true }; });
        });
        _app.MapPost("/api/ranked/integrity/appeals", (HttpRequest request, IntegrityAppealRequest body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            return IntegrityRequest(request, () => _platform.SubmitRankedIntegrityAppeal(account, body.DecisionId, body.RequestId, body.Statement));
        });
        _app.MapGet("/api/ranked/integrity/appeals", (HttpRequest request, string? cursor, int? limit) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            return IntegrityRequest(request, () => _platform.RankedIntegrityAppealsForPlayer(account, cursor, limit ?? 20));
        });
        _app.MapGet("/api/admin/ranked/integrity/appeals", (HttpRequest request, string? decisionId, string? status, string? cursor, int? limit) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminAuditRead, out var auth, out var failure)) return failure;
            return IntegrityRequest(request, () => _platform.RankedIntegrityAppeals(auth.Account, decisionId, status, cursor, limit ?? 20));
        });
        _app.MapPost("/api/admin/ranked/integrity/appeals/{id}/review", (HttpRequest request, string id, L12RankedIntegrityAppealReviewInput body) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminMatchGovernanceWrite, out var auth, out var failure)) return failure;
            return IntegrityRequest(request, () => _platform.ReviewRankedIntegrityAppeal(auth.Account, id, body,
                RequestAuditContext(request, L12Permission.AdminMatchGovernanceWrite)));
        });
    }
}
