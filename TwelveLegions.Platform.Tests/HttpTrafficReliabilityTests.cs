using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class HttpTrafficReliabilityTests
{
    private const int ExpectedPublicMediaLimit = 600;

    [Theory]
    [InlineData("desktop")]
    [InlineData("mobile")]
    [InlineData("thumbnail")]
    public void PublicMediaGalleryReadsDoNotSpendPublishedCatalogAllowance(string variant)
    {
        var guard = new L12HttpTrafficGuard();
        var media = Enumerable.Range(0, 198)
            .Select(index => guard.Acquire(Request(HttpMethods.Get, PublicMediaPath(index, variant)), "gallery-client", null))
            .ToArray();

        var arts = guard.Acquire(Request(HttpMethods.Get, "/api/alternate-arts"), "gallery-client", null);
        var presentation = guard.Acquire(Request(HttpMethods.Get, "/api/gallery/presentation"), "gallery-client", null);
        Assert.True(arts.Allowed, "198 canonical media GETs must not exhaust the public alternate-art allowance");
        Assert.True(presentation.Allowed, "Media browsing must not block public gallery metadata");
        Assert.Equal("anonymous-read", arts.Policy);
        Assert.Equal(L12HttpTrafficGuard.AnonymousReadLimit - 1, arts.Remaining);
        Assert.Equal(L12HttpTrafficGuard.AnonymousReadLimit - 2, presentation.Remaining);
        Assert.All(media, decision =>
        {
            Assert.True(decision.Allowed);
            Assert.Equal("public-media", decision.Policy);
            Assert.Equal(ExpectedPublicMediaLimit, decision.Limit);
        });
        Assert.Equal(ExpectedPublicMediaLimit - media.Length, media[^1].Remaining);
    }

    [Fact]
    public void PublicMediaHardLimitIsSharedAcrossVariantsAndAccountsButNotClients()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero));
        var guard = new L12HttpTrafficGuard(clock);
        var variants = new[] { "desktop", "mobile", "thumbnail" };
        for (var index = 0; index < ExpectedPublicMediaLimit; index++)
        {
            var decision = guard.Acquire(Request(HttpMethods.Get, PublicMediaPath(index, variants[index % 3])),
                "gallery-client", index % 2 == 0 ? null : $"rotated-account-{index}");
            Assert.True(decision.Allowed);
            Assert.Equal("public-media", decision.Policy);
            Assert.Equal(ExpectedPublicMediaLimit - index - 1, decision.Remaining);
        }

        var denied = guard.Acquire(Request(HttpMethods.Get, PublicMediaPath()), "gallery-client", "new-account");
        Assert.False(denied.Allowed);
        Assert.Equal("public-media", denied.Policy);
        Assert.Equal(ExpectedPublicMediaLimit, denied.Limit);
        Assert.Equal(0, denied.Remaining);
        Assert.Equal(60, denied.RetryAfterSeconds);
        Assert.True(guard.Acquire(Request(HttpMethods.Get, PublicMediaPath()), "other-client", null).Allowed);

        clock.Advance(TimeSpan.FromMilliseconds(59_100));
        Assert.Equal(1, guard.Acquire(Request(HttpMethods.Get, PublicMediaPath()), "gallery-client", null).RetryAfterSeconds);
        clock.Advance(TimeSpan.FromMilliseconds(900));
        var reset = guard.Acquire(Request(HttpMethods.Get, PublicMediaPath()), "gallery-client", null);
        Assert.True(reset.Allowed);
        Assert.Equal(ExpectedPublicMediaLimit - 1, reset.Remaining);
    }

    [Fact]
    public void PublicMediaAndBusinessExhaustionAndExpiryCannotResetEachOther()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero));
        var guard = new L12HttpTrafficGuard(clock);
        var business = Request(HttpMethods.Get, "/api/gallery/presentation");
        var media = Request(HttpMethods.Get, PublicMediaPath());
        for (var index = 0; index < L12HttpTrafficGuard.AnonymousReadLimit; index++)
            Assert.True(guard.Acquire(business, "client", null).Allowed);
        Assert.False(guard.Acquire(business, "client", null).Allowed);

        clock.Advance(TimeSpan.FromSeconds(10));
        for (var index = 0; index < ExpectedPublicMediaLimit; index++)
            Assert.True(guard.Acquire(media, "client", null).Allowed);
        Assert.False(guard.Acquire(media, "client", null).Allowed);
        var stillExhausted = guard.Acquire(business, "client", null);
        Assert.False(stillExhausted.Allowed);
        Assert.Equal("anonymous-read", stillExhausted.Policy);
        Assert.Equal(50, stillExhausted.RetryAfterSeconds);

        clock.Advance(TimeSpan.FromSeconds(50));
        var businessReset = guard.Acquire(business, "client", null);
        Assert.True(businessReset.Allowed);
        Assert.Equal(L12HttpTrafficGuard.AnonymousReadLimit - 1, businessReset.Remaining);
        var mediaStillExhausted = guard.Acquire(media, "client", null);
        Assert.False(mediaStillExhausted.Allowed);
        Assert.Equal(10, mediaStillExhausted.RetryAfterSeconds);

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(ExpectedPublicMediaLimit - 1, guard.Acquire(media, "client", null).Remaining);
        Assert.Equal(L12HttpTrafficGuard.AnonymousReadLimit - 2, guard.Acquire(business, "client", null).Remaining);
    }

    [Fact]
    public void PublicMediaCannotSpendOrReplenishAccountAndAuthenticatedClientLimits()
    {
        var guard = new L12HttpTrafficGuard();
        var business = Request(HttpMethods.Get, "/api/me");
        var media = Request(HttpMethods.Get, PublicMediaPath());
        for (var index = 0; index < L12HttpTrafficGuard.AccountReadLimit; index++)
            Assert.True(guard.Acquire(business, "client", "account").Allowed);
        Assert.False(guard.Acquire(business, "client", "account").Allowed);
        for (var index = 0; index < ExpectedPublicMediaLimit; index++)
            Assert.True(guard.Acquire(media, "client", "account").Allowed);
        Assert.False(guard.Acquire(business, "client", "account").Allowed);
        Assert.False(guard.Acquire(media, "client", "another-account").Allowed);

        for (var index = L12HttpTrafficGuard.AccountReadLimit; index < L12HttpTrafficGuard.AuthenticatedClientReadLimit; index++)
            Assert.True(guard.Acquire(business, "client", $"account-{index}").Allowed);
        var aggregateDenied = guard.Acquire(business, "client", "last-account");
        Assert.False(aggregateDenied.Allowed);
        Assert.Equal("authenticated-client-read", aggregateDenied.Policy);
        Assert.Equal(L12HttpTrafficGuard.AuthenticatedClientReadLimit, aggregateDenied.Limit);
        Assert.True(guard.Acquire(business, "another-client", "last-account").Allowed);
    }

    [Fact]
    public void PublicMediaPartitionsStayBoundedAndRejectedBusinessAdmissionIsAtomic()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero));
        var guard = new L12HttpTrafficGuard(clock, maximumPartitions: 2);
        var media = Request(HttpMethods.Get, PublicMediaPath());
        var business = Request(HttpMethods.Get, "/api/me");
        var first = guard.Acquire(media, "client", "account");
        Assert.True(first.Allowed);
        Assert.Equal("public-media", first.Policy);
        Assert.False(guard.Acquire(business, "client", "account").Allowed);
        var anonymous = guard.Acquire(business, "client", null);
        Assert.True(anonymous.Allowed);
        Assert.Equal(L12HttpTrafficGuard.AnonymousReadLimit - 1, anonymous.Remaining);
        Assert.False(guard.Acquire(media, "other-client", null).Allowed);
        Assert.Equal(ExpectedPublicMediaLimit - 2, guard.Acquire(media, "client", null).Remaining);

        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(ExpectedPublicMediaLimit - 1, guard.Acquire(media, "other-client", null).Remaining);
        Assert.Equal(L12HttpTrafficGuard.AnonymousReadLimit - 1, guard.Acquire(business, "other-client", null).Remaining);
    }

    public static IEnumerable<object[]> PublicMediaNonContentRequests()
    {
        var valid = PublicMediaPath();
        foreach (var path in new[]
        {
            "/api/site/media", "/api/site/media/", "/api/site/mediax/a/thumbnail/b.webp",
            valid + "/extra", valid + "/", valid.Replace("/thumbnail/", "/original/"),
            valid.Replace("/thumbnail/", "/unknown/"), valid.Replace("/thumbnail/", "/%74humbnail/"),
            valid.Replace("/thumbnail/", "/%2fthumbnail/"), valid.Replace("/thumbnail/", "/../thumbnail/"),
            valid.Replace("/thumbnail/", "/thumbnail%252f/"), valid.Replace(".webp", ".png"),
            valid.Replace(".webp", ".webp.exe"), valid.Replace(new string('a', 64), new string('a', 63)),
            valid.Replace(new string('a', 64), new string('g', 64)),
            valid.Replace(new string('0', 32), "not-a-media-id"), valid.Replace("/media/", "/media//"),
            "/api/admin/site/media", "/api/gallery/presentation", "/api/alternate-arts",
        }) yield return [HttpMethods.Get, path, "anonymous-read", L12HttpTrafficGuard.AnonymousReadLimit];
        yield return [HttpMethods.Head, valid, "anonymous-read", L12HttpTrafficGuard.AnonymousReadLimit];
        foreach (var method in new[] { HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete })
            yield return [method, valid, "mutation", L12HttpTrafficGuard.MutationLimit];
        yield return [HttpMethods.Post, "/api/admin/site/media", "expensive", L12HttpTrafficGuard.ExpensiveLimit];
        yield return [HttpMethods.Post, "/api/auth/login", "authentication", L12HttpTrafficGuard.AuthenticationLimit];
        yield return [HttpMethods.Post, "/api/telemetry/page-view", "telemetry", L12HttpTrafficGuard.TelemetryLimit];
        yield return [HttpMethods.Options, valid, "unlimited", int.MaxValue];
    }

    [Theory]
    [MemberData(nameof(PublicMediaNonContentRequests))]
    public void PublicMediaDoesNotRelaxOtherPathsOrMethods(string method, string path, string expectedPolicy, int expectedLimit)
    {
        var guard = new L12HttpTrafficGuard();
        var request = Request(method, path);
        var allowed = guard.Acquire(request, "client", null);
        Assert.True(allowed.Allowed);
        Assert.Equal(expectedPolicy, allowed.Policy);
        Assert.Equal(expectedLimit, allowed.Limit);
        if (expectedLimit == int.MaxValue) return;
        for (var index = 1; index < expectedLimit; index++)
            Assert.True(guard.Acquire(request, "client", null).Allowed);
        var denied = guard.Acquire(request, "client", null);
        Assert.False(denied.Allowed);
        Assert.Equal(expectedPolicy, denied.Policy);
        Assert.Equal(0, denied.Remaining);
        Assert.InRange(denied.RetryAfterSeconds, 1, 60);
    }

    private static string PublicMediaPath(int index = 0, string variant = "thumbnail")
        => $"/api/site/media/{index:x32}/{variant}/{new string('a', 64)}.webp";

    [Fact]
    public async Task PublicMediaLivePipelineChecksRawPathWithoutChargingCatalogReads()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-public-media-" + Guid.NewGuid().ToString("N"));
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
            var address = Assert.Single(server.Addresses).TrimEnd('/');
            using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };

            // These synthetic files do not exist: 404 tests admission, not image delivery.
            using var canonical = await client.GetAsync(PublicMediaPath());
            Assert.Equal(HttpStatusCode.NotFound, canonical.StatusCode);
            Assert.Equal("600", Assert.Single(canonical.Headers.GetValues("RateLimit-Limit")));
            Assert.Equal("599", Assert.Single(canonical.Headers.GetValues("RateLimit-Remaining")));

            var encoded = new Uri(address + PublicMediaPath().Replace("/thumbnail/", "/%74humbnail/"),
                new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true });
            using var normalized = await client.GetAsync(encoded);
            Assert.Equal(HttpStatusCode.NotFound, normalized.StatusCode);
            Assert.Equal("120", Assert.Single(normalized.Headers.GetValues("RateLimit-Limit")));
            Assert.Equal("119", Assert.Single(normalized.Headers.GetValues("RateLimit-Remaining")));

            using var queryOnly = await client.GetAsync(PublicMediaPath() + "?probe=%2f");
            Assert.Equal(HttpStatusCode.NotFound, queryOnly.StatusCode);
            Assert.Equal("598", Assert.Single(queryOnly.Headers.GetValues("RateLimit-Remaining")));
            using var arts = await client.GetAsync("/api/alternate-arts");
            using var presentation = await client.GetAsync("/api/gallery/presentation");
            Assert.Equal(HttpStatusCode.OK, arts.StatusCode);
            Assert.Equal(HttpStatusCode.OK, presentation.StatusCode);
            Assert.Equal("118", Assert.Single(arts.Headers.GetValues("RateLimit-Remaining")));
            Assert.Equal("117", Assert.Single(presentation.Headers.GetValues("RateLimit-Remaining")));
            await server.StopAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

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
    public void PerformanceWindowIsBoundedAndReportsBudgetFailuresOnlyWithEnoughSamples()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
        var monitor = new L12HttpPerformanceMonitor(clock);
        for (var i = 0; i < L12HttpPerformanceMonitor.MinimumSamples - 2; i++)
            monitor.Record(TimeSpan.FromMilliseconds(50), StatusCodes.Status200OK, 2);
        monitor.Record(TimeSpan.FromMilliseconds(50), StatusCodes.Status200OK, 2, mutation: true);

        var insufficient = monitor.Snapshot();
        Assert.False(insufficient.SampleSufficient);
        Assert.Null(insufficient.WithinBudget);
        Assert.Empty(insufficient.BudgetFailures);

        monitor.Record(TimeSpan.FromMilliseconds(50), StatusCodes.Status200OK, 3, mutation: true);
        var healthy = monitor.Snapshot();
        Assert.True(healthy.SampleSufficient);
        Assert.True(healthy.WithinBudget);
        Assert.Equal("<=100ms", healthy.P95LatencyBand);
        Assert.Equal(3, healthy.PeakInFlight);
        Assert.Equal(18, healthy.ReadSampleCount);
        Assert.Equal(2, healthy.MutationSampleCount);

        monitor.Record(TimeSpan.FromMilliseconds(1_001), StatusCodes.Status429TooManyRequests);
        monitor.Record(TimeSpan.FromMilliseconds(1_001), StatusCodes.Status500InternalServerError);
        var degraded = monitor.Snapshot();
        Assert.False(degraded.WithinBudget);
        Assert.Contains("slow_request_rate", degraded.BudgetFailures);
        Assert.Contains("server_error_rate", degraded.BudgetFailures);
        Assert.Equal(1, degraded.RateLimitedCount);
        Assert.Equal(">1000ms", degraded.P95LatencyBand);

        clock.Advance(TimeSpan.FromSeconds(L12HttpPerformanceMonitor.WindowSeconds + 1));
        var expired = monitor.Snapshot();
        Assert.Equal(0, expired.SampleCount);
        Assert.False(expired.SampleSufficient);
    }

    [Fact]
    public async Task ConcurrentSnapshotNeverObservesTornSampleComposition()
    {
        var monitor = new L12HttpPerformanceMonitor();
        var writers = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var index = 0; index < 2_000; index++)
                monitor.Record(TimeSpan.FromMilliseconds(index % 1_100), StatusCodes.Status200OK,
                    worker + 1, mutation: (index & 1) == 0);
        })).ToArray();
        while (writers.Any(task => !task.IsCompleted))
        {
            var snapshot = monitor.Snapshot();
            Assert.Equal(snapshot.SampleCount, snapshot.ReadSampleCount + snapshot.MutationSampleCount);
            Assert.InRange(snapshot.SlowRequestCount, 0, snapshot.SampleCount);
            await Task.Yield();
        }
        await Task.WhenAll(writers);
        var final = monitor.Snapshot();
        Assert.Equal(16_000, final.SampleCount);
        Assert.Equal(final.SampleCount, final.ReadSampleCount + final.MutationSampleCount);
    }

    [Fact]
    public void DiagnosticRefreshCannotDiluteRepresentativeSamplesAndExpectedOutcomesStaySeparate()
    {
        var monitor = new L12HttpPerformanceMonitor();
        for (var index = 0; index < 100; index++)
            monitor.Record(TimeSpan.FromMilliseconds(1), StatusCodes.Status200OK, diagnostic: true);
        monitor.Record(TimeSpan.FromMilliseconds(20), StatusCodes.Status503ServiceUnavailable,
            expectedUnavailable: true);
        monitor.Record(TimeSpan.FromMilliseconds(20), L12HttpExceptionBoundary.ClientClosedRequestStatusCode);

        var snapshot = monitor.Snapshot();
        Assert.Equal(2, snapshot.SampleCount);
        Assert.Equal(100, snapshot.DiagnosticRequestCount);
        Assert.Equal(1, snapshot.ExpectedUnavailableCount);
        Assert.Equal(1, snapshot.ClientCancelledCount);
        Assert.Equal(0, snapshot.ServerErrorCount);
    }

    [Fact]
    public async Task PerformanceMiddlewareRecordsFinalStatusWithoutPerRequestStorage()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();
        var monitor = new L12HttpPerformanceMonitor();

        await monitor.InvokeAsync(context, async () =>
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsync("{}");
        });

        var snapshot = monitor.Snapshot();
        Assert.Equal(1, snapshot.SampleCount);
        Assert.Equal(1, snapshot.RateLimitedCount);
        Assert.Equal(0, snapshot.InFlight);
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
    public async Task CancellationAfterResponseStartedIsStillClassifiedAs499()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();
        var monitor = new L12HttpPerformanceMonitor();

        await monitor.InvokeAsync(context, () => L12HttpExceptionBoundary.InvokeAsync(context, async () =>
        {
            await context.Response.StartAsync();
            throw new OperationCanceledException(cancellation.Token);
        }));

        var snapshot = monitor.Snapshot();
        Assert.Equal(1, snapshot.ClientCancelledCount);
        Assert.Equal(0, snapshot.ServerErrorCount);
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
            Assert.StartsWith("app;dur=", Assert.Single(denied.Headers.GetValues("Server-Timing")),
                StringComparison.Ordinal);
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
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = path;
        return context.Request;
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan duration) => _now += duration;
    }
}
