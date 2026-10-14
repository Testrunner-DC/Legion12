using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class GlobalAnalyticsDateAndPeakTests
{
    [Theory]
    [InlineData(20, 8, 0)]
    [InlineData(20, 20, 0)]
    [InlineData(8, 20, 50)]
    [InlineData(0, -8, 0)]
    public async Task MinutePeakKeepsTheTimestampOfItsFirstMaximum(int first, int second, int seconds)
    {
        await using var fixture = await Fixture.CreateAsync();
        var initial = fixture.Now;
        await fixture.Recorder.RecordSiteActivityAsync(null, null, first);
        fixture.Now = initial.AddSeconds(50);
        await fixture.Recorder.RecordSiteActivityAsync(null, null, second);
        var report = await fixture.Recorder.ReadGlobalAnalyticsAsync(new(2026, 9, 20), new(2026, 9, 20), []);
        var day = Assert.Single(report.Days);
        Assert.Equal(Math.Max(0, Math.Max(first, second)), day.PeakOnline);
        Assert.Equal(initial.AddSeconds(seconds), DateTimeOffset.Parse(day.PeakOnlineAt!));
        fixture.Passed = true;
    }

    [Fact]
    public async Task PeakTimeAndExistingAggregatesSurviveRecorderReopen()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.Now;
        await fixture.Recorder.RecordSiteActivityAsync("returning", "/cards?private=ignored", 20);
        fixture.Now = first.AddSeconds(30);
        await fixture.Recorder.RecordSiteActivityAsync("returning", "/cards", 8);
        fixture.Now = first.AddMinutes(1);
        await fixture.Recorder.RecordSiteActivityAsync("returning", "/battle", 10);
        await fixture.Recorder.DisposeAsync();
        fixture.Recorder = new MatchRecorder(fixture.DatabasePath, () => fixture.Now);
        await fixture.Recorder.InitializeAsync();
        var report = await fixture.Recorder.ReadGlobalAnalyticsAsync(new(2026, 9, 20), new(2026, 9, 20),
            [new L12AccountView("returning", "synthetic", "player", first.AddDays(-10), true)]);
        var day = Assert.Single(report.Days);
        Assert.Equal(20, day.PeakOnline);
        Assert.Equal(first, DateTimeOffset.Parse(day.PeakOnlineAt!));
        Assert.Equal(15, day.AverageOnline);
        Assert.Equal(1, day.DailyActiveUsers);
        Assert.Equal(3, day.PageViews);
        fixture.Passed = true;
    }

    [Fact]
    public async Task UtcDayAndMetadataKeepActiveNewUsersDistinctFromAllRegistrations()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Now = new(2026, 9, 20, 23, 59, 50, TimeSpan.Zero);
        await fixture.Recorder.RecordSiteActivityAsync("returning", "/before", 20);
        fixture.Now = fixture.Now.AddSeconds(20);
        await fixture.Recorder.RecordSiteActivityAsync("new-active", "/after", 8);
        var accounts = new[]
        {
            new L12AccountView("returning", "synthetic-old", "player", fixture.Now.AddDays(-10), true),
            new L12AccountView("new-active", "synthetic-active", "player", fixture.Now, true),
            new L12AccountView("new-idle", "synthetic-idle", "player", fixture.Now, true),
        };
        var report = await fixture.Recorder.ReadGlobalAnalyticsAsync(new(2026, 9, 20), new(2026, 9, 21), accounts);
        Assert.Equal(2, report.Days.Count);
        Assert.Equal(20, report.Days[0].PeakOnline);
        Assert.Equal(8, report.Days[1].PeakOnline);
        Assert.Equal(1, report.Days[1].NewUsers);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("UTC", json.RootElement.GetProperty("timeZone").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("days")[1].GetProperty("sampleMinutes").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("days")[1].GetProperty("newRegisteredUsers").GetInt32());
        fixture.Passed = true;
    }

    [Theory]
    [InlineData("fromDate=2026-09-16&toDate=2026-10-14", "2026-09-16", "2026-10-14")]
    [InlineData("from=2026-09-16&to=2026-10-14", "2026-09-16", "2026-10-14")]
    [InlineData("fromUtc=2026-09-15T16%3A00%3A00Z&toUtc=2026-10-13T16%3A00%3A00Z", "2026-09-15", "2026-10-13")]
    [InlineData("fromDate=2026-09-16&toDate=2026-10-14&fromUtc=2026-09-15T16%3A00%3A00Z", "2026-09-16", "2026-10-14")]
    public async Task RealHttpUsesCanonicalCalendarDatesWithoutReinterpretingLegacyTimestamps(
        string query, string expectedFrom, string expectedTo)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartHttpAsync();
        using var response = await fixture.GetAsync(query, fixture.AdminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedFrom, json.RootElement.GetProperty("fromDate").GetString());
        Assert.Equal(expectedTo, json.RootElement.GetProperty("toDate").GetString());
        Assert.Equal("UTC", json.RootElement.GetProperty("timeZone").GetString());
        fixture.Passed = true;
    }

    [Theory]
    [InlineData("fromDate=2026-02-30")]
    [InlineData("fromDate=2026-09-16T00%3A00%3A00Z")]
    [InlineData("fromDate=2026-09-17&toDate=2026-09-16")]
    public async Task RealHttpRejectsMalformedOrReversedCalendarDates(string query)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartHttpAsync();
        using var response = await fixture.GetAsync(query, fixture.AdminToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        fixture.Passed = true;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealHttpRetainsAnalyticsAuthorizationBeforeParsing(bool regularPlayer)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartHttpAsync();
        using var response = await fixture.GetAsync("fromDate=invalid", regularPlayer ? fixture.PlayerToken : null);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        fixture.Passed = true;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "l12-global-analytics-" + Guid.NewGuid().ToString("N"));
        private string? _previousHost;
        private L12WebSocketServer? _server;
        private HttpClient? _client;
        public MatchRecorder Recorder { get; set; } = null!;
        public DateTimeOffset Now { get; set; } = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);
        public string DatabasePath => Path.Combine(_root, "matches.db");
        public string? AdminToken { get; private set; }
        public string? PlayerToken { get; private set; }
        public bool Passed { get; set; }
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            Directory.CreateDirectory(fixture._root);
            fixture.Recorder = new MatchRecorder(fixture.DatabasePath, () => fixture.Now);
            await fixture.Recorder.InitializeAsync();
            return fixture;
        }
        public async Task StartHttpAsync()
        {
            _previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(_root, "platform.json"), catalog.PresetDecks);
            var admin = store.Login("Admin", "L12master");
            Assert.True(admin.Success);
            AdminToken = admin.Token;
            var player = store.Register("globaltest9", "synthetic-123");
            Assert.True(player.Success);
            PlayerToken = player.Token;
            _server = new(new L12RoomManager(catalog, Recorder, store), Recorder, store, catalog);
            await _server.StartAsync(0);
            _client = new HttpClient { BaseAddress = new Uri(Assert.Single(_server.Addresses)), Timeout = TimeSpan.FromSeconds(5) };
        }
        public async Task<HttpResponseMessage> GetAsync(string query, string? token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/analytics/global?" + query);
            if (token is not null) request.Headers.Authorization = new("Bearer", token);
            return await _client!.SendAsync(request);
        }
        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_server is not null) { await _server.StopAsync(); await _server.DisposeAsync(); Environment.SetEnvironmentVariable("L12_LISTEN_HOST", _previousHost); }
            await Recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (Passed) Directory.Delete(_root, true);
            else Console.WriteLine("Failed synthetic global analytics fixture retained: " + _root);
        }
    }
}
