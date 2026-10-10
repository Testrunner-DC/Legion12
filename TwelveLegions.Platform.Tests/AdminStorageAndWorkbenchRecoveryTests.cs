using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class AdminStorageAndWorkbenchRecoveryTests
{
    [Fact]
    public void StorageProbeFailuresReturnAnActionableUnavailableSnapshotWithoutSensitivePaths()
    {
        var probe = new L12ServerStorageProbe(
            () => new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero),
            () => throw new IOException("volume enumeration failed"),
            _ => throw new UnauthorizedAccessException("category access failed"),
            () => throw new IOException("process sample failed"));

        var snapshot = L12ServerStorageMonitor.Read(probe);

        Assert.Equal("unavailable", snapshot.SampleState);
        Assert.Equal("unknown", snapshot.Health);
        Assert.Empty(snapshot.Volumes);
        Assert.All(snapshot.Categories, category => Assert.False(category.Available));
        Assert.True(snapshot.UnavailableSourceCount >= 3);
        Assert.Contains("关联 ID", snapshot.RecommendedAction);
        var json = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("ProcessId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Path", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/opt/", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CategoryOverflowKeepsHealthyVolumeFactsAndMarksSnapshotPartial()
    {
        var probe = new L12ServerStorageProbe(
            () => DateTimeOffset.UtcNow,
            () => new L12StorageVolumeProbeResult([new(1_000, 600)], 0),
            _ => throw new OverflowException("simulated directory total overflow"),
            () => 256);

        var snapshot = L12ServerStorageMonitor.Read(probe);

        Assert.Equal("partial", snapshot.SampleState);
        Assert.Equal("healthy", snapshot.Health);
        Assert.Equal(400, Assert.Single(snapshot.Volumes).UsedBytes);
        Assert.All(snapshot.Categories, category => Assert.False(category.Available));
        Assert.DoesNotContain(snapshot.Categories, category => category.Available && category.Bytes == 0);
    }

    [Fact]
    public void EmptyVolumeEnumerationIsUnavailableInsteadOfAHealthyZeroValue()
    {
        var probe = new L12ServerStorageProbe(
            () => DateTimeOffset.UtcNow,
            () => new L12StorageVolumeProbeResult([], 0),
            _ => (0, true),
            () => 256);

        var snapshot = L12ServerStorageMonitor.Read(probe);

        Assert.Equal("unavailable", snapshot.SampleState);
        Assert.Equal("unknown", snapshot.Health);
        Assert.Empty(snapshot.Volumes);
        Assert.True(snapshot.UnavailableSourceCount > 0);
        Assert.DoesNotContain("0%", snapshot.Conclusion, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingCategoryIsUnavailableWithoutHidingUsableVolumes()
    {
        var probe = new L12ServerStorageProbe(
            () => DateTimeOffset.UtcNow,
            () => new L12StorageVolumeProbeResult([new(1_000, 100)], 0),
            _ => (0, false),
            () => 256);

        var snapshot = L12ServerStorageMonitor.Read(probe);

        Assert.Equal("partial", snapshot.SampleState);
        Assert.Equal("critical", snapshot.Health);
        Assert.All(snapshot.Categories, category => Assert.False(category.Available));
        Assert.Contains("部分来源不可用", snapshot.Conclusion);
    }

    [Fact]
    public async Task StorageFailureUses503CorrelationWhileWorkbenchKeepsOtherSections()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-admin-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            var rooms = new L12RoomManager(catalog, recorder, store);
            server = new L12WebSocketServer(rooms, recorder, store, catalog,
                storageSnapshot: () => throw new IOException("simulated inaccessible mount"));
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            var admin = store.Login("Admin", "L12master");
            var player = store.Register("u5e2c9a03aa", "password-123");

            using (var forbidden = Authorized("/api/admin/server-storage", player.Token!, "storage-forbidden-1"))
            using (var response = await client.SendAsync(forbidden))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var expired = store.Login("Admin", "L12master");
            var expiredAuthentication = store.AuthenticateTokenSession(expired.Token)!;
            store.RevokeOwnSession(expiredAuthentication, expiredAuthentication.SessionId);
            using (var expiredRequest = Authorized("/api/admin/server-storage", expired.Token!, "storage-expired-1"))
            using (var response = await client.SendAsync(expiredRequest))
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            using (var storage = Authorized("/api/admin/server-storage", admin.Token!,
                       "655e4e0d-01c3-40fc-ae37-53aa267bec57"))
            using (var response = await client.SendAsync(storage))
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                var error = await response.Content.ReadFromJsonAsync<L12ApiError>();
                Assert.Equal("server_storage_unavailable", error!.Code);
                Assert.Equal("655e4e0d-01c3-40fc-ae37-53aa267bec57", error.CorrelationId);
            }

            using (var workbench = Authorized("/api/admin/workbench/summary", admin.Token!,
                       "6d8e5e83-77ab-4a39-a9f2-d1a54d7683b8"))
            using (var response = await client.SendAsync(workbench))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var summary = await response.Content.ReadFromJsonAsync<L12AdminWorkbenchSummaryView>();
                Assert.True(summary!.Partial);
                Assert.Contains("storage", summary.UnavailableSections!);
                Assert.Contains(summary.Anomalies, item => item.Id == "runtime");
                var unavailable = Assert.Single(summary.Anomalies, item => item.Id == "storage-unavailable");
                Assert.Contains("6d8e5e83-77ab-4a39-a9f2-d1a54d7683b8", unavailable.Detail);
            }
        }
        finally
        {
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Directory.Delete(root, true);
        }
    }

    private static HttpRequestMessage Authorized(string path, string token, string correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(L12CorrelationIds.HeaderName, correlationId);
        return request;
    }
}
