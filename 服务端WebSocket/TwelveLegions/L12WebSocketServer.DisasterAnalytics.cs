using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TwelveLegions.Server;

public sealed partial class L12WebSocketServer
{
    private static IResult PublicDeckStatisticsStatus(string status) => status switch
    {
        "unauthorized" => Results.Unauthorized(),
        "not_found" => Results.NotFound(),
        "read_conflict" => Results.Json(new { code = "public_deck_read_conflict", refreshRequired = true }, statusCode: 409),
        "feature_disabled" => Results.Json(new { code = "feature_disabled", message = "公开牌库当前未开放" }, statusCode: 503),
        _ => Results.BadRequest(new { message = "牌库统计查询参数无效" }),
    };
    private bool IsCurrentDisasterReader(HttpRequest request, L12AuthenticatedSession? captured)
    {
        var current = _platform.AuthenticateSession(request.Headers.Authorization);
        return captured is null ? request.Headers.Authorization.Count == 0
            : current?.SessionId == captured.SessionId && current.Account.Id == captured.Account.Id
              && current.Account.PermissionVersion == captured.Account.PermissionVersion;
    }

    private static L12DisasterAnalyticsReport ProjectDisasterStatistics(L12DisasterAnalyticsReport report,
        L12AccountView viewer) => L12Authorization.HasPermission(viewer, L12Permission.AdminMatchesRead) ? report
        : report with { Items = report.Items.Where(r => r.Action == "ban").ToArray(), PrivatePickGroupsHidden = true };

    private async Task<IResult> DisasterRead(HttpRequest request, Func<Task<IResult>> read)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        try { return await read(); }
        catch (ArgumentException error) { return ApiError(request, "invalid_disaster_query", error.Message, 400); }
        catch (CardAnalyticsUnavailableException error) { return ApiError(request, error.Code, error.Message, error.StatusCode); }
        catch (InvalidDataException) { return ApiError(request, "disaster_facts_unavailable", "天灾统计记录暂不可用，请稍后重试。", 503); }
        catch (OperationCanceledException) when (request.HttpContext.RequestAborted.IsCancellationRequested)
        { return Results.StatusCode(499); }
        catch (OperationCanceledException) { return Results.StatusCode(503); }
    }

    private void MapDisasterAnalyticsEndpoints()
    {
        var app = _app ?? throw new InvalidOperationException("服务尚未初始化");
        app.MapGet("/api/admin/analytics/disasters", (HttpRequest request) => DisasterRead(request, async () =>
        {
            const L12Permission permission = L12Permission.AdminAnalyticsRead;
            if (!TryAuthorize(request, permission, out var captured, out var failure)) return failure;
            var query = CardAnalyticsQuery(request) with { DisasterMode = QueryValue(request, "disasterMode"),
                DisasterPoolId = QueryValue(request, "disasterPoolId"), DisasterOrigin = QueryValue(request, "disasterOrigin") };
            var report = await _recorder.ReadScopedDisasterAnalyticsAsync(query,
                QueryValue(request, "includedCardId"), QueryValue(request, "deckSignature"), request.HttpContext.RequestAborted);
            if (!IsCurrentDisasterReader(request, captured)) return Results.Unauthorized();
            var current = _platform.AuthenticateSession(request.Headers.Authorization)!;
            if (!L12Authorization.HasPermission(current.Account, permission))
                return ApiError(request, "permission_denied", "没有读取统计的权限。", 403);
            _platform.RecordAdminRead(current.Account, permission, "analytics", "read-scoped-disasters",
                "disasters", AuditContext(request, permission), request.HttpContext.RequestAborted);
            return Results.Ok(ProjectDisasterStatistics(report, current.Account));
        }));
        app.MapGet("/api/me/matches/{matchId}/disasters", (HttpRequest request, string matchId) => DisasterRead(request, async () =>
        {
            var captured = _platform.AuthenticateSession(request.Headers.Authorization);
            if (captured is null) return Results.Unauthorized();
            var summary = await _recorder.ReadVisibleDisasterSetupAsync(matchId, captured.Account.Id,
                cancellationToken: request.HttpContext.RequestAborted);
            if (!IsCurrentDisasterReader(request, captured)) return Results.Unauthorized();
            return summary is null ? Results.NotFound() : Results.Ok(summary);
        }));
        app.MapGet("/api/public/tournaments/code/{code}/rounds/{round:int}/tables/{table:int}/disasters",
            (HttpRequest request, string code, int round, int table) => DisasterRead(request, async () =>
        {
            var captured = _platform.AuthenticateSession(request.Headers.Authorization);
            if (request.Headers.Authorization.Count > 0 && captured is null) return Results.Unauthorized();
            var lease = _platform.CaptureTournamentDisasterRead(code, round, table, captured?.Account);
            if (lease is null) return Results.NotFound();
            var summary = await _recorder.ReadVisibleDisasterSetupAsync(lease.RecordedMatchId, captured?.Account.Id,
                tournamentPublic: true, request.HttpContext.RequestAborted, [lease.PlayerA, lease.PlayerB]);
            if (!IsCurrentDisasterReader(request, captured)) return Results.Unauthorized();
            if (!_platform.RevalidateTournamentDisasterRead(code, lease, captured?.Account))
                return ApiError(request, "disaster_read_changed", "赛事信息已更新，请重新查看。", 409);
            return summary is null ? Results.NotFound() : Results.Ok(summary);
        }));
        app.MapGet("/api/public-decks/{reference}/disasters", (HttpRequest request, string reference) => DisasterRead(request, async () =>
        {
            var captured = _platform.AuthenticateSession(request.Headers.Authorization);
            if (request.Headers.Authorization.Count > 0 && captured is null) return Results.Unauthorized();
            // The same visibility/token/exclusion lease used by the existing public-deck statistics.
            if (request.Query.Keys.Any(k => k != "expectedReadToken") || request.Query["expectedReadToken"].Count > 1)
                return ApiError(request, "invalid_disaster_query", "公开禁用统计不支持拆分筛选。", 400);
            var read = _platform.CapturePublicDeckStatisticsRead(_catalog, reference,
                QueryValue(request, "expectedReadToken"), captured);
            if (read.Status != "available") return PublicDeckStatisticsStatus(read.Status);
            var lease = read.Lease!;
            var report = await _recorder.ReadPublicDeckDisasterStatisticsAsync(lease.Id,
                new(lease.ExcludedMatchIds, lease.ExcludedAccountIds), request.HttpContext.RequestAborted);
            var state = _platform.RevalidatePublicDeckStatisticsRead(_catalog, lease, captured);
            if (state != "available") return PublicDeckStatisticsStatus(state);
            return Results.Ok(report);
        }));
    }
}
