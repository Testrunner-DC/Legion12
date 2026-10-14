using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class AdminWorkbenchTimingTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(100)]
    [InlineData(20_000)]
    public async Task SyntheticAuditScaleRecordsColdWarmStagesWithoutChangingCore(int rows)
    {
        await using var f = await Fixture.Create(false);
        SeedAudit(f.Store, rows);
        var before = File.ReadAllBytes(f.MirrorPath);
        var version = f.Store.Version;
        for (var run = 0; run < 2; run++)
        {
            using var request = f.Request(f.AdminToken);
            using var response = await f.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var summary = await response.Content.ReadFromJsonAsync<L12AdminWorkbenchSummaryView>();
            Assert.False(summary!.Partial);
            Assert.Equal(6, summary.RecentActivities.Count);
            output.WriteLine(JsonSerializer.Serialize(new { SyntheticRows = rows, Run = run, HandlerStagesMilliseconds = Timings(response), ProductionWrites = 0 }));
        }
        Assert.Equal(version, f.Store.Version);
        Assert.Equal(before, File.ReadAllBytes(f.MirrorPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealSummaryMeasuresEveryAllowedSectionIncludingFailedStorage(bool failStorage)
    {
        await using var f = await Fixture.Create(failStorage);
        using var request = f.Request(f.AdminToken);
        using var response = await f.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<L12AdminWorkbenchSummaryView>();
        Assert.NotNull(summary);
        Assert.Equal(failStorage, summary.Partial);
        Assert.Equal(1, f.StorageCalls);
        var timing = Timings(response);
        Assert.Equal(new[] { "wb-audit", "wb-auth", "wb-bugs", "wb-renames", "wb-runtime", "wb-security", "wb-storage", "wb-total" }, timing.Keys.OrderBy(k => k));
        Assert.All(timing.Values, value => Assert.True(double.IsFinite(value) && value >= 0));
        Assert.True(timing["wb-storage"] >= 20, "controlled 25ms storage read is measured even when it throws");
        Assert.True(timing["wb-total"] >= timing["wb-storage"]);
        Assert.Contains(summary.Pending, item => item.Id == "bugs");
        Assert.Contains(summary.Anomalies, item => item.Id == "runtime");
        if (failStorage) Assert.Contains("storage", summary.UnavailableSections!);
        var raw = string.Join(",", response.Headers.GetValues("Server-Timing"));
        Assert.DoesNotContain(f.AdminToken, raw);
        Assert.DoesNotContain("synthetic-storage-private-marker", raw);
        Assert.DoesNotContain("correlation-private-marker", raw);
    }

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("player", 403)]
    [InlineData("revoked", 401)]
    public async Task RejectedAuthenticationOrPermissionDoesNotReadSectionsOrExposeTiming(string identity, int status)
    {
        await using var f = await Fixture.Create(false);
        var token = identity switch { "player" => f.PlayerToken, "revoked" => f.RevokedToken, _ => null };
        using var request = f.Request(token);
        using var response = await f.Client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(0, f.StorageCalls);
        // The existing HTTP monitor may expose generic app duration, never module data.
        Assert.DoesNotContain(Timings(response).Keys, key => key.StartsWith("wb-", StringComparison.Ordinal));
    }

    private static Dictionary<string, double> Timings(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Server-Timing", out var values), "fixed per-module timing must be present on the real response");
        return string.Join(",", values!).Split(',').Select(value => value.Trim().Split(";dur="))
            .ToDictionary(value => value[0], value => double.Parse(value[1], CultureInfo.InvariantCulture));
    }

    private static void SeedAudit(L12PlatformStore store, int rows)
    {
        using var db = new SqliteConnection($"Data Source={store.TransactionalStoragePath};Pooling=False");
        db.Open();
        using var select = db.CreateCommand();
        select.CommandText = "SELECT payload_json FROM admin_audit_events ORDER BY created_utc DESC LIMIT 1;";
        var payload = JsonNode.Parse((string)select.ExecuteScalar()!)!.AsObject();
        using var tx = db.BeginTransaction();
        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "INSERT INTO admin_audit_events VALUES($id,$created,$category,$outcome,$actor,$command,$correlation,$payload,$sha);";
        foreach (var name in new[] { "$id", "$created", "$category", "$outcome", "$actor", "$command", "$correlation", "$payload", "$sha" }) command.Parameters.AddWithValue(name, "");
        var now = DateTimeOffset.UtcNow.AddDays(-1);
        for (var i = 0; i < rows; i++)
        {
            payload["Id"] = $"wb-synthetic-{i:D5}";
            payload["CreatedAt"] = now.AddTicks(-i).ToString("O");
            var body = payload.ToJsonString();
            command.Parameters["$id"].Value = payload["Id"]!.GetValue<string>();
            command.Parameters["$created"].Value = payload["CreatedAt"]!.GetValue<string>();
            foreach (var (parameter, field) in new[] { ("$category", "Category"), ("$outcome", "Outcome"), ("$actor", "ActorId"), ("$command", "CommandId"), ("$correlation", "CorrelationId") })
                command.Parameters[parameter].Value = (object?)payload[field]?.GetValue<string>() ?? DBNull.Value;
            command.Parameters["$payload"].Value = body;
            command.Parameters["$sha"].Value = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            command.ExecuteNonQuery();
        }
        tx.Commit();
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "l12-workbench-timing-" + Guid.NewGuid().ToString("N"));
        private readonly string? previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        private MatchRecorder? recorder;
        private L12WebSocketServer? server;
        private int storageCalls;
        public int StorageCalls => storageCalls;
        public HttpClient Client { get; private set; } = null!;
        public L12PlatformStore Store { get; private set; } = null!;
        public string MirrorPath => Path.Combine(root, "platform.json");
        public string AdminToken { get; private set; } = null!;
        public string PlayerToken { get; private set; } = null!;
        public string RevokedToken { get; private set; } = null!;

        public static async Task<Fixture> Create(bool failStorage)
        {
            var f = new Fixture();
            try
            {
                Directory.CreateDirectory(f.root);
                Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
                var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
                f.recorder = new MatchRecorder(Path.Combine(f.root, "matches.db"));
                await f.recorder.InitializeAsync();
                var store = new L12PlatformStore(Path.Combine(f.root, "platform.json"), catalog.PresetDecks,
                    officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts);
                f.Store = store;
                f.AdminToken = store.Login("Admin", "L12master").Token!;
                f.PlayerToken = store.Register("wbplayer", "password-123").Token!;
                f.RevokedToken = store.Login("Admin", "L12master").Token!;
                var session = store.AuthenticateTokenSession(f.RevokedToken)!;
                store.RevokeOwnSession(session, session.SessionId);
                f.server = new L12WebSocketServer(new L12RoomManager(catalog, f.recorder, store), f.recorder, store, catalog,
                    storageSnapshot: () =>
                    {
                        Interlocked.Increment(ref f.storageCalls);
                        Thread.Sleep(25); // Controlled local dependency delay; not production sampling.
                        if (failStorage) throw new IOException("synthetic-storage-private-marker");
                        return L12ServerStorageMonitor.Read(new(() => DateTimeOffset.UtcNow,
                            () => new([new(1_000, 600)], 0), _ => (0, true), () => 256));
                    });
                await f.server.StartAsync(0);
                f.Client = new HttpClient { BaseAddress = new Uri(Assert.Single(f.server.Addresses)) };
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        public HttpRequestMessage Request(string? token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/workbench/summary");
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add(L12CorrelationIds.HeaderName, "correlation-private-marker");
            return request;
        }
        public async ValueTask DisposeAsync()
        {
            Client?.Dispose();
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
