using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TwelveLegions.Server;

public sealed partial class L12WebSocketServer
{
    private sealed record HistoryPreferencesRequest(bool? PublicHistory);

    private void MapFriendHistoryPreferenceRoute()
    {
        _app!.MapPut("/api/me/history-preferences", (HttpRequest request, HistoryPreferencesRequest body) =>
        {
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            if (body.PublicHistory is not { } enabled)
                return ApiError(request, "invalid_history_preference", "请选择是否公开战绩", StatusCodes.Status400BadRequest);
            var result = _platform.SetOwnHistoryPreference(request.Headers.Authorization, enabled);
            return result.Value is { } value ? Results.Ok(value) : FriendHistoryFailure(request, result.Status);
        });
    }

    private void MapFriendHistoryReadRoute()
    {
        _app!.MapGet("/api/friends/{accountId}/history", async (HttpRequest request, string accountId, string? range) =>
        {
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            var selectedRange = string.IsNullOrWhiteSpace(range) ? "season" : range.Trim().ToLowerInvariant();
            if (selectedRange is not ("season" or "7d" or "30d"))
                return ApiError(request, "invalid_statistics_range", "战绩时间范围只支持 7d、30d 或 season", StatusCodes.Status400BadRequest);
            var authorization = request.Headers.Authorization.ToString();
            var capture = _platform.CaptureFriendHistoryRead(authorization, accountId);
            if (capture.Lease is not { } lease) return FriendHistoryFailure(request, capture.Status);
            try
            {
                var history = await _recorder.ReadFriendHistoryAsync(lease, selectedRange, request.HttpContext.RequestAborted);
                history = history with { Statistics = _platform.MergeTestRunAcceptanceStatistics(lease.TargetId, history.Statistics) };
                var status = _platform.RevalidateFriendHistoryRead(authorization, lease);
                if (status != "available") return FriendHistoryFailure(request, status);
                return Results.Ok(history);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_statistics_range", error.Message, StatusCodes.Status400BadRequest);
            }
        });
    }

    private IResult FriendHistoryFailure(HttpRequest request, string status) => status switch
    {
        "unauthorized" => ApiError(request, "authentication_required", "请重新登录后查看", StatusCodes.Status401Unauthorized),
        "private" => ApiError(request, "friend_history_private", "该好友未公开战绩", StatusCodes.Status403Forbidden),
        "read_conflict" => ApiError(request, "friend_history_changed", "战绩资料已变化，请重新查看", StatusCodes.Status409Conflict),
        _ => ApiError(request, "friend_history_unavailable", "暂时无法查看该好友的战绩", StatusCodes.Status404NotFound),
    };
}
