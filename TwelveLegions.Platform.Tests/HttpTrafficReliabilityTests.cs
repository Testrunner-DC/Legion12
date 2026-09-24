using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class HttpTrafficReliabilityTests
{
    [Fact]
    public void ReadBucketsAreBoundedSeparatedAndResetAfterTheWindow()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
        var guard = new L12HttpTrafficGuard(clock);
        var anonymous = Request(HttpMethods.Get, "/api/operations/effective-policy");

        L12HttpRateDecision decision = default;
        for (var i = 0; i < L12HttpTrafficGuard.AnonymousReadLimit; i++)
            decision = guard.Acquire(anonymous, "192.0.2.10", null);
        Assert.True(decision.Allowed);
        Assert.Equal(0, decision.Remaining);
        var denied = guard.Acquire(anonymous, "192.0.2.10", null);
        Assert.False(denied.Allowed);
        Assert.Equal(60, denied.RetryAfterSeconds);

        Assert.True(guard.Acquire(anonymous, "192.0.2.11", null).Allowed);
        Assert.True(guard.Acquire(anonymous, "192.0.2.10", "account-1").Allowed);
        clock.Advance(TimeSpan.FromMinutes(1));
        var reset = guard.Acquire(anonymous, "192.0.2.10", null);
        Assert.True(reset.Allowed);
        Assert.Equal(L12HttpTrafficGuard.AnonymousReadLimit - 1, reset.Remaining);
    }

    [Fact]
    public void MutationAuthenticationTelemetryAndExpensiveTrafficUseSeparatePolicies()
    {
        var guard = new L12HttpTrafficGuard();
        Assert.Equal("mutation", guard.Acquire(Request(HttpMethods.Post, "/api/decks"), "client", "account").Policy);
        Assert.Equal("authentication", guard.Acquire(Request(HttpMethods.Post, "/api/auth/login"), "client", null).Policy);
        Assert.Equal("telemetry", guard.Acquire(Request(HttpMethods.Post, "/api/telemetry/page-view"), "client", null).Policy);
        Assert.Equal("expensive", guard.Acquire(Request(HttpMethods.Post, "/api/admin/site/media"), "client", "admin").Policy);
        Assert.Equal("read", guard.Acquire(Request(HttpMethods.Get, "/api/admin/site/media"), "client", "admin").Policy);
        Assert.Equal("read", guard.Acquire(Request(HttpMethods.Get, "/api/admin/security/audit-archives"), "client", "admin").Policy);
        Assert.Equal("expensive", guard.Acquire(Request(HttpMethods.Post, "/api/admin/security/audit-archives"), "client", "admin").Policy);
        Assert.Equal("expensive", guard.Acquire(Request(HttpMethods.Get, "/api/admin/articles/modian/preview"), "client", "admin").Policy);
        Assert.Equal("unlimited", guard.Acquire(Request(HttpMethods.Get, "/health"), "client", null).Policy);
    }

    [Fact]
    public void AuthenticatedClientAggregateStopsAccountRotationWithoutSharingAcrossClients()
    {
        var guard = new L12HttpTrafficGuard();
        var request = Request(HttpMethods.Get, "/api/operations/effective-policy");
        L12HttpRateDecision lastAllowed = default;
        for (var i = 0; i < L12HttpTrafficGuard.AuthenticatedClientReadLimit; i++)
        {
            lastAllowed = guard.Acquire(request, "shared-client", $"account-{i}");
            Assert.True(lastAllowed.Allowed);
        }
        Assert.Equal("authenticated-client-read", lastAllowed.Policy);
        Assert.Equal(0, lastAllowed.Remaining);

        var denied = guard.Acquire(request, "shared-client", "rotated-account");
        Assert.False(denied.Allowed);
        Assert.Equal("authenticated-client-read", denied.Policy);
        Assert.Equal(L12HttpTrafficGuard.AuthenticatedClientReadLimit, denied.Limit);
        Assert.True(guard.Acquire(request, "other-client", "rotated-account").Allowed);
    }

    [Fact]
    public void AggregateAdmissionIsAtomicWhenPartitionCapacityIsExhausted()
    {
        var guard = new L12HttpTrafficGuard(maximumPartitions: 1);
        var denied = guard.Acquire(Request(HttpMethods.Get, "/api/me"), "client", "account");
        Assert.False(denied.Allowed);

        // A failed two-partition acquisition must not leave a half-created account or client bucket.
        var anonymous = guard.Acquire(Request(HttpMethods.Get, "/api/me"), "anonymous", null);
        Assert.True(anonymous.Allowed);
    }

    [Fact]
    public void PartitionSprayFailsClosedWithoutEvictingAnActiveAllowance()
    {
        var guard = new L12HttpTrafficGuard(maximumPartitions: 2);
        var request = Request(HttpMethods.Get, "/api/operations/effective-policy");
        Assert.True(guard.Acquire(request, "client-a", null).Allowed);
        Assert.True(guard.Acquire(request, "client-b", null).Allowed);
        var overflow = guard.Acquire(request, "client-c", null);
        Assert.False(overflow.Allowed);
        Assert.Equal(60, overflow.RetryAfterSeconds);
        Assert.True(guard.Acquire(request, "client-a", null).Allowed);
    }

    [Fact]
    public async Task ExceptionBoundaryReturnsOpaqueCorrelatedJson()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/test";
        context.Items[L12CorrelationIds.ContextItemName] = "test-correlation-1";
        var previousError = Console.Error;
        using var capturedError = new StringWriter();
        try
        {
            Console.SetError(capturedError);
            await L12HttpExceptionBoundary.InvokeAsync(context,
                () => throw new InvalidOperationException("private database detail"));
        }
        finally
        {
            Console.SetError(previousError);
        }

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("test-correlation-1", context.Response.Headers[L12CorrelationIds.HeaderName]);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("internal_error", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("test-correlation-1", document.RootElement.GetProperty("correlationId").GetString());
        Assert.DoesNotContain("database", document.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private database detail", capturedError.ToString(), StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException", capturedError.ToString(), StringComparison.Ordinal);
        Assert.Contains("test-correlation-1", capturedError.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExceptionBoundaryDoesNotTurnClientCancellationIntoServerFailure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.Response.Body = new MemoryStream();
        await L12HttpExceptionBoundary.InvokeAsync(context,
            () => throw new OperationCanceledException(cancellation.Token));
        Assert.Equal(L12HttpExceptionBoundary.ClientClosedRequestStatusCode, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task LivePipelineReturnsRetryMetadataAndCorrelationId()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-http-traffic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            await using var server = new L12WebSocketServer(
                new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            for (var i = 0; i < L12HttpTrafficGuard.AnonymousReadLimit; i++)
            {
                using var allowed = await client.GetAsync("/api/operations/effective-policy");
                Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            }

            using var denied = await client.GetAsync("/api/operations/effective-policy");
            Assert.Equal(HttpStatusCode.TooManyRequests, denied.StatusCode);
            Assert.NotNull(denied.Headers.RetryAfter);
            Assert.Equal("0", Assert.Single(denied.Headers.GetValues("RateLimit-Remaining")));
            Assert.True(denied.Headers.Contains(L12CorrelationIds.HeaderName));
            var error = await denied.Content.ReadFromJsonAsync<L12ApiError>();
            Assert.Equal("rate_limited", error!.Code);
            Assert.Equal(Assert.Single(denied.Headers.GetValues(L12CorrelationIds.HeaderName)), error.CorrelationId);

            // These routes are mapped before middleware registration. They must still pass through
            // the same guard rather than bypassing it because of source-order placement.
            using var preMappedRoute = await client.GetAsync("/api/ranked/integrity/notifications");
            Assert.Equal(HttpStatusCode.TooManyRequests, preMappedRoute.StatusCode);
            await server.StopAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static HttpRequest Request(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        return context.Request;
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan duration) => _now += duration;
    }
}
