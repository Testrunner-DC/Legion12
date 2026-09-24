using Microsoft.AspNetCore.Http;

namespace TwelveLegions.Server;

internal readonly record struct L12HttpRateDecision(bool Allowed, string Policy, int Limit,
    int Remaining, int RetryAfterSeconds);

/// <summary>
/// Bounded, process-local admission guard for the public HTTP API. Domain-specific controls
/// (for example failed-login throttles and idempotency keys) remain authoritative.
/// </summary>
internal sealed class L12HttpTrafficGuard
{
    private sealed class Window
    {
        public DateTimeOffset ResetAt { get; set; }
        public int Count { get; set; }
    }

    internal const int AnonymousReadLimit = 120;
    internal const int AccountReadLimit = 240;
    internal const int MutationLimit = 60;
    internal const int AuthenticationLimit = 30;
    internal const int TelemetryLimit = 30;
    internal const int ExpensiveLimit = 10;
    internal static readonly TimeSpan WindowLength = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly Dictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly int _maximumPartitions;
    private long _acquisitions;

    internal L12HttpTrafficGuard(TimeProvider? timeProvider = null, int maximumPartitions = 8_192)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maximumPartitions = Math.Max(1, maximumPartitions);
    }

    internal L12HttpRateDecision Acquire(HttpRequest request, string clientIdentity, string? accountId)
    {
        var (policy, limit, forceClientPartition) = Classify(request.Method, request.Path);
        if (policy == "read" && string.IsNullOrWhiteSpace(accountId))
        {
            policy = "anonymous-read";
            limit = AnonymousReadLimit;
            forceClientPartition = true;
        }
        if (limit == int.MaxValue)
            return new(true, policy, limit, int.MaxValue, 0);

        var subject = !forceClientPartition && !string.IsNullOrWhiteSpace(accountId)
            ? $"account:{accountId}" : $"client:{clientIdentity}";
        var key = $"{policy}|{subject}";
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            _acquisitions += 1;
            if ((_acquisitions & 255) == 0 || _windows.Count >= _maximumPartitions)
                RemoveExpired(now);

            if (!_windows.TryGetValue(key, out var window))
            {
                // Stay memory-bounded under a client-identity spray. A new partition is rejected
                // instead of evicting an active limiter and silently granting a fresh allowance.
                if (_windows.Count >= _maximumPartitions)
                    return new(false, policy, limit, 0, (int)WindowLength.TotalSeconds);
                window = new Window { ResetAt = now + WindowLength };
                _windows.Add(key, window);
            }
            else if (now >= window.ResetAt)
            {
                window.ResetAt = now + WindowLength;
                window.Count = 0;
            }

            if (window.Count >= limit)
                return new(false, policy, limit, 0, RetryAfter(now, window.ResetAt));

            window.Count += 1;
            return new(true, policy, limit, Math.Max(0, limit - window.Count), 0);
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var key in _windows.Where(pair => now >= pair.Value.ResetAt).Select(pair => pair.Key).ToArray())
            _windows.Remove(key);
    }

    private static int RetryAfter(DateTimeOffset now, DateTimeOffset resetAt)
        => Math.Max(1, (int)Math.Ceiling((resetAt - now).TotalSeconds));

    private static (string Policy, int Limit, bool ForceClientPartition) Classify(string method, PathString path)
    {
        if (!path.StartsWithSegments("/api") || HttpMethods.IsOptions(method))
            return ("unlimited", int.MaxValue, true);
        if (path.StartsWithSegments("/api/telemetry"))
            return ("telemetry", TelemetryLimit, true);
        if (path == "/api/auth/login" || path == "/api/auth/register"
            || path.StartsWithSegments("/api/auth/password") || path.StartsWithSegments("/api/auth/email"))
            return ("authentication", AuthenticationLimit, true);
        if (IsExpensive(path))
            return ("expensive", ExpensiveLimit, false);
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
            return ("mutation", MutationLimit, false);
        return ("read", AccountReadLimit, false);
    }

    private static bool IsExpensive(PathString path)
        => path == "/api/admin/site/media"
           || path.StartsWithSegments("/api/admin/articles/modian/import")
           || path.StartsWithSegments("/api/admin/releases/deploy")
           || path.StartsWithSegments("/api/admin/releases/rollback")
           || path.StartsWithSegments("/api/admin/security/audit-archives");
}

internal static class L12HttpExceptionBoundary
{
    internal const int ClientClosedRequestStatusCode = 499;

    internal static async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        try
        {
            await next();
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = ClientClosedRequestStatusCode;
            }
        }
        catch (BadHttpRequestException error)
        {
            if (context.Response.HasStarted) throw;
            await WriteErrorAsync(context, "invalid_request", "请求格式无效", error.StatusCode);
        }
        catch (Exception error)
        {
            var correlationId = CorrelationId(context);
            var path = context.Items.TryGetValue(L12CorrelationIds.OriginalPathItemName, out var original)
                ? original?.ToString() : context.Request.Path.Value;
            Console.Error.WriteLine($"[{correlationId}] HTTP request failed: method={context.Request.Method}, path={path}\n{error}");
            if (context.Response.HasStarted) throw;
            await WriteErrorAsync(context, "internal_error", "服务器暂时无法处理该请求", StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, string code, string message, int statusCode)
    {
        var correlationId = CorrelationId(context);
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.Headers[L12CorrelationIds.HeaderName] = correlationId;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new L12ApiError(code, message, correlationId),
            cancellationToken: CancellationToken.None);
    }

    private static string CorrelationId(HttpContext context)
    {
        if (context.Items.TryGetValue(L12CorrelationIds.ContextItemName, out var value)
            && value is string correlationId && L12CorrelationIds.IsValid(correlationId))
            return correlationId;
        var created = L12CorrelationIds.AcceptOrCreate(context.Request.Headers[L12CorrelationIds.HeaderName]);
        context.Items[L12CorrelationIds.ContextItemName] = created;
        return created;
    }
}
