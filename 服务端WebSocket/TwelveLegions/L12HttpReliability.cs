using System.Globalization;
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
    // Authenticated requests keep their account-level allowance, while a deliberately high
    // client aggregate prevents account rotation from bypassing admission control. The aggregate
    // is high enough that ordinary households and shared networks are governed by account limits.
    internal const int AuthenticatedClientReadLimit = 1_200;
    internal const int AuthenticatedClientMutationLimit = 300;
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

        var authenticatedAccount = !forceClientPartition && !string.IsNullOrWhiteSpace(accountId);
        var subject = authenticatedAccount ? $"account:{accountId}" : $"client:{clientIdentity}";
        var buckets = new List<(string Key, string Policy, int Limit)>
        {
            ($"{policy}|{subject}", policy, limit),
        };
        if (authenticatedAccount)
        {
            var aggregatePolicy = policy == "read" ? "authenticated-client-read" : "authenticated-client-mutation";
            var aggregateLimit = policy == "read" ? AuthenticatedClientReadLimit : AuthenticatedClientMutationLimit;
            buckets.Add(($"{aggregatePolicy}|client:{clientIdentity}", aggregatePolicy, aggregateLimit));
        }
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            _acquisitions += 1;
            if ((_acquisitions & 255) == 0 || _windows.Count >= _maximumPartitions)
                RemoveExpired(now);

            var missing = buckets.Count(bucket => !_windows.ContainsKey(bucket.Key));
            if (_windows.Count + missing > _maximumPartitions)
            {
                // Stay memory-bounded under a client-identity spray. A new partition is rejected
                // instead of evicting an active limiter and silently granting a fresh allowance.
                var earliestReset = _windows.Count == 0 ? now + WindowLength : _windows.Values.Min(window => window.ResetAt);
                return new(false, policy, limit, 0, RetryAfter(now, earliestReset));
            }

            foreach (var bucket in buckets)
            {
                if (_windows.TryGetValue(bucket.Key, out var window) && now >= window.ResetAt)
                {
                    window.ResetAt = now + WindowLength;
                    window.Count = 0;
                }
            }

            foreach (var bucket in buckets)
            {
                if (_windows.TryGetValue(bucket.Key, out var window) && window.Count >= bucket.Limit)
                    return new(false, bucket.Policy, bucket.Limit, 0, RetryAfter(now, window.ResetAt));
            }

            foreach (var bucket in buckets)
            {
                if (!_windows.ContainsKey(bucket.Key))
                    _windows.Add(bucket.Key, new Window { ResetAt = now + WindowLength });
                _windows[bucket.Key].Count += 1;
            }
            var tightest = buckets
                .Select(bucket => (Bucket: bucket, Remaining: Math.Max(0, bucket.Limit - _windows[bucket.Key].Count)))
                .OrderBy(candidate => candidate.Remaining)
                .First();
            return new(true, tightest.Bucket.Policy, tightest.Bucket.Limit, tightest.Remaining, 0);
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
        if (IsExpensive(method, path))
            return ("expensive", ExpensiveLimit, false);
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
            return ("mutation", MutationLimit, false);
        return ("read", AccountReadLimit, false);
    }

    private static bool IsExpensive(string method, PathString path)
        => (HttpMethods.IsPost(method) && path == "/api/admin/site/media")
           || (HttpMethods.IsGet(method) && path.StartsWithSegments("/api/admin/articles/modian/preview"))
           || (HttpMethods.IsPost(method) && path.StartsWithSegments("/api/admin/articles/modian/import"))
           || (HttpMethods.IsPost(method) && path.StartsWithSegments("/api/admin/releases/deploy"))
           || (HttpMethods.IsPost(method) && path.StartsWithSegments("/api/admin/releases/rollback"))
           || (HttpMethods.IsPost(method) && path.StartsWithSegments("/api/admin/security/audit-archives"));
}

/// <summary>
/// Fixed-size request telemetry for the most recent minute. It retains no completed-request record and
/// deliberately has no path, account or client dimensions, so diagnostics cannot become a
/// high-cardinality memory or logging problem of their own.
/// </summary>
internal sealed class L12HttpPerformanceMonitor
{
    private sealed class Bucket
    {
        internal Bucket(long epoch) => Epoch = epoch;
        internal readonly object Gate = new();
        internal readonly long Epoch;
        internal long Completed;
        internal long ReadSamples;
        internal long MutationSamples;
        internal long DiagnosticRequests;
        internal long DurationMilliseconds;
        internal long AtMost100Milliseconds;
        internal long AtMost300Milliseconds;
        internal long AtMost1000Milliseconds;
        internal long Over1000Milliseconds;
        internal long RateLimited;
        internal long ServerErrors;
        internal long ExpectedUnavailable;
        internal long ClientCancelled;
        internal long PeakInFlight;
    }

    internal const string OutcomeStatusItemName = "l12.http.performance.outcome-status";
    internal const string ExpectedUnavailableItemName = "l12.http.performance.expected-unavailable";

    internal const int WindowSeconds = 60;
    internal const int BucketSeconds = 10;
    internal const int BucketCount = WindowSeconds / BucketSeconds;
    internal const int SlowRequestThresholdMilliseconds = 1_000;
    internal const int MinimumSamples = 20;
    internal const int MinimumReadSamples = 10;
    internal const int MinimumMutationSamples = 2;
    internal const double MaximumSlowRequestPercent = 5;
    internal const double MaximumServerErrorPercent = 1;
    internal const double MaximumRateLimitedPercent = 20;

    private readonly Bucket?[] _buckets = new Bucket?[BucketCount];
    private readonly TimeProvider _timeProvider;
    private long _inFlight;

    internal L12HttpPerformanceMonitor(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next();
            return;
        }

        var started = _timeProvider.GetTimestamp();
        var inFlight = Interlocked.Increment(ref _inFlight);
        var diagnostic = context.Request.Path == "/api/admin/runtime/status";
        var mutation = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method);
        context.Response.OnStarting(() =>
        {
            var duration = Math.Max(0, _timeProvider.GetElapsedTime(started).TotalMilliseconds);
            if (!context.Response.Headers.ContainsKey("Server-Timing"))
                context.Response.Headers["Server-Timing"] = $"app;dur={duration.ToString("0.0", CultureInfo.InvariantCulture)}";
            return Task.CompletedTask;
        });

        var statusCode = StatusCodes.Status500InternalServerError;
        try
        {
            await next();
            statusCode = context.Response.StatusCode;
        }
        finally
        {
            var elapsed = _timeProvider.GetElapsedTime(started);
            if (context.Items.TryGetValue(OutcomeStatusItemName, out var outcome) && outcome is int outcomeStatus)
                statusCode = outcomeStatus;
            var expectedUnavailable = context.Items.TryGetValue(ExpectedUnavailableItemName, out var expected)
                && expected is true;
            Record(elapsed, statusCode, inFlight, mutation, diagnostic, expectedUnavailable);
            Interlocked.Decrement(ref _inFlight);
        }
    }

    internal void Record(TimeSpan elapsed, int statusCode, long peakInFlight = 1,
        bool mutation = false, bool diagnostic = false, bool expectedUnavailable = false)
    {
        var bucket = CurrentBucket();
        var milliseconds = Math.Max(0, (long)Math.Ceiling(elapsed.TotalMilliseconds));
        lock (bucket.Gate)
        {
            bucket.PeakInFlight = Math.Max(bucket.PeakInFlight, peakInFlight);
            if (diagnostic)
            {
                bucket.DiagnosticRequests += 1;
                return;
            }
            bucket.Completed += 1;
            if (mutation) bucket.MutationSamples += 1;
            else bucket.ReadSamples += 1;
            bucket.DurationMilliseconds += milliseconds;
            if (milliseconds <= 100) bucket.AtMost100Milliseconds += 1;
            else if (milliseconds <= 300) bucket.AtMost300Milliseconds += 1;
            else if (milliseconds <= SlowRequestThresholdMilliseconds) bucket.AtMost1000Milliseconds += 1;
            else bucket.Over1000Milliseconds += 1;
            if (statusCode == StatusCodes.Status429TooManyRequests) bucket.RateLimited += 1;
            if (statusCode == L12HttpExceptionBoundary.ClientClosedRequestStatusCode) bucket.ClientCancelled += 1;
            if (expectedUnavailable && statusCode == StatusCodes.Status503ServiceUnavailable)
                bucket.ExpectedUnavailable += 1;
            else if (statusCode >= StatusCodes.Status500InternalServerError)
                bucket.ServerErrors += 1;
        }
    }

    internal L12HttpPerformanceView Snapshot()
    {
        var currentEpoch = Epoch(_timeProvider.GetUtcNow());
        long completed = 0, reads = 0, mutations = 0, diagnostics = 0, duration = 0;
        long atMost100 = 0, atMost300 = 0, atMost1000 = 0, over1000 = 0;
        long rateLimited = 0, serverErrors = 0, expectedUnavailable = 0, clientCancelled = 0, peakInFlight = 0;
        for (var index = 0; index < _buckets.Length; index++)
        {
            var bucket = Volatile.Read(ref _buckets[index]);
            if (bucket is null || bucket.Epoch < currentEpoch - BucketCount + 1 || bucket.Epoch > currentEpoch) continue;
            lock (bucket.Gate)
            {
                // A generation replaced after the first read must not be mixed into this snapshot.
                if (!ReferenceEquals(bucket, Volatile.Read(ref _buckets[index]))) continue;
                completed += bucket.Completed;
                reads += bucket.ReadSamples;
                mutations += bucket.MutationSamples;
                diagnostics += bucket.DiagnosticRequests;
                duration += bucket.DurationMilliseconds;
                atMost100 += bucket.AtMost100Milliseconds;
                atMost300 += bucket.AtMost300Milliseconds;
                atMost1000 += bucket.AtMost1000Milliseconds;
                over1000 += bucket.Over1000Milliseconds;
                rateLimited += bucket.RateLimited;
                serverErrors += bucket.ServerErrors;
                expectedUnavailable += bucket.ExpectedUnavailable;
                clientCancelled += bucket.ClientCancelled;
                peakInFlight = Math.Max(peakInFlight, bucket.PeakInFlight);
            }
        }
        peakInFlight = Math.Max(peakInFlight, Interlocked.Read(ref _inFlight));

        var slowPercent = Percentage(over1000, completed);
        var rateLimitedPercent = Percentage(rateLimited, completed);
        var serverErrorPercent = Percentage(serverErrors, completed);
        var sufficient = completed >= MinimumSamples && reads >= MinimumReadSamples && mutations >= MinimumMutationSamples;
        var failures = new List<string>();
        if (sufficient && slowPercent > MaximumSlowRequestPercent) failures.Add("slow_request_rate");
        if (sufficient && serverErrorPercent > MaximumServerErrorPercent) failures.Add("server_error_rate");
        if (sufficient && rateLimitedPercent > MaximumRateLimitedPercent) failures.Add("rate_limited_rate");
        return new L12HttpPerformanceView(WindowSeconds, SlowRequestThresholdMilliseconds, MinimumSamples,
            MinimumReadSamples, MinimumMutationSamples, completed, reads, mutations, diagnostics,
            Interlocked.Read(ref _inFlight), peakInFlight,
            completed == 0 ? 0 : Math.Round((double)duration / completed, 1),
            P95Band(completed, atMost100, atMost300, atMost1000), over1000, slowPercent,
            rateLimited, rateLimitedPercent, serverErrors, serverErrorPercent,
            expectedUnavailable, Percentage(expectedUnavailable, completed),
            clientCancelled, Percentage(clientCancelled, completed),
            sufficient, sufficient ? failures.Count == 0 : null, failures);
    }

    private Bucket CurrentBucket()
    {
        var epoch = Epoch(_timeProvider.GetUtcNow());
        var index = (int)(epoch % BucketCount);
        while (true)
        {
            var current = Volatile.Read(ref _buckets[index]);
            if (current?.Epoch == epoch) return current;
            var replacement = new Bucket(epoch);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _buckets[index], replacement, current), current))
                return replacement;
        }
    }

    private static long Epoch(DateTimeOffset now) => now.ToUnixTimeSeconds() / BucketSeconds;

    private static double Percentage(long value, long total)
        => total == 0 ? 0 : Math.Round(value * 100d / total, 2);

    private static string P95Band(long total, long atMost100, long atMost300, long atMost1000)
    {
        if (total == 0) return "no-samples";
        var target = (long)Math.Ceiling(total * 0.95d);
        if (atMost100 >= target) return "<=100ms";
        if (atMost100 + atMost300 >= target) return "<=300ms";
        if (atMost100 + atMost300 + atMost1000 >= target) return "<=1000ms";
        return ">1000ms";
    }

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
            context.Items[L12HttpPerformanceMonitor.OutcomeStatusItemName] = ClientClosedRequestStatusCode;
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = ClientClosedRequestStatusCode;
            }
        }
        catch (BadHttpRequestException error)
        {
            context.Items[L12HttpPerformanceMonitor.OutcomeStatusItemName] = error.StatusCode;
            if (context.Response.HasStarted) throw;
            await WriteErrorAsync(context, "invalid_request", "请求格式无效", error.StatusCode);
        }
        catch (Exception error)
        {
            context.Items[L12HttpPerformanceMonitor.OutcomeStatusItemName] = StatusCodes.Status500InternalServerError;
            var correlationId = CorrelationId(context);
            var path = context.Items.TryGetValue(L12CorrelationIds.OriginalPathItemName, out var original)
                ? original?.ToString() : context.Request.Path.Value;
            Console.Error.WriteLine($"[{correlationId}] HTTP request failed: method={context.Request.Method}, path={path}, "
                + $"errorType={error.GetType().FullName}\n{error.StackTrace}");
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
