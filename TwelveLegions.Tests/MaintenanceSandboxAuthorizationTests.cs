using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

[Collection(SqlitePoolIsolationCollection.Name)]
public sealed class MaintenanceSandboxAuthorizationTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public async Task MaintenanceAllowsOnlyCurrentOperationsAdminToCreateSandboxAndKeepsOtherModesClosed()
    {
        var root = TempRoot();
        MatchRecorder? recorder = null;
        try
        {
            var catalog = Catalog;
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var platform = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            var admin = platform.Login("Admin", "L12master").Account!;
            var player = platform.Register("maintp1", "password-123").Account!;
            var formerAdmin = platform.Register("maintold", "password-123").Account!;
            Assert.True(platform.SetRole(admin, formerAdmin.Id, "admin"));

            var fenced = false;
            var fenceReadFails = false;
            var manager = new L12RoomManager(catalog, recorder, platform,
                maintenanceSandboxFenceActive: () => fenceReadFails
                    ? throw new IOException("simulated deployment fence read failure")
                    : fenced);
            var adminSession = Guid.NewGuid();
            var playerSession = Guid.NewGuid();
            var formerAdminSession = Guid.NewGuid();
            var legacySession = Guid.NewGuid();
            manager.Connect(adminSession, admin.Id, admin.Username);
            // The display name is deliberately spoofed here: authorization must come from the account id.
            manager.Connect(playerSession, player.Id, "Admin");
            manager.Connect(formerAdminSession, formerAdmin.Id, formerAdmin.Username);
            manager.Connect(legacySession, "Admin");

            var current = platform.OperationsConfig(admin);
            var maintenance = platform.BeginImmediateMaintenance(admin, 3, current.Version,
                "maintenance sandbox authorization", Context("start-maintenance"));
            Assert.True(maintenance.Current.ImmediateMaintenance!.Enabled);
            Assert.True(platform.SetRole(admin, formerAdmin.Id, "player"));

            AssertBlocked(await manager.CreateSandboxAsync(playerSession, new L12SandboxRequest()),
                "maintenance_active");
            AssertBlocked(await manager.CreateSandboxAsync(formerAdminSession, new L12SandboxRequest()),
                "maintenance_active");
            AssertBlocked(await manager.CreateSandboxAsync(legacySession, new L12SandboxRequest()),
                "maintenance_active");

            // The exception is sandbox-only, even for the operations administrator.
            AssertBlocked(manager.CreateRoom(adminSession), "maintenance_active");
            AssertBlocked(await manager.JoinMatchmakingAsync(adminSession, "casual", null),
                "maintenance_active");
            AssertBlocked(await manager.JoinMatchmakingAsync(adminSession, "ranked", null),
                "maintenance_active");

            fenced = true;
            AssertBlocked(await manager.CreateSandboxAsync(adminSession, new L12SandboxRequest()),
                "sandbox_deployment_fenced");
            Assert.True(platform.EffectiveOperationsPolicy().Maintenance.EntryBlocked);
            Assert.True(platform.OperationsConfig(admin).ImmediateMaintenance!.Enabled);

            fenced = false;
            fenceReadFails = true;
            AssertBlocked(await manager.CreateSandboxAsync(adminSession, new L12SandboxRequest()),
                "sandbox_deployment_fenced");

            fenceReadFails = false;
            var created = await manager.CreateSandboxAsync(adminSession, new L12SandboxRequest());
            var envelope = EnvelopeFor(adminSession, created, "gameState");
            Assert.True(envelope.GetProperty("gmEnabled").GetBoolean());
            Assert.NotEqual("GameOver", envelope.GetProperty("state").GetProperty("phase").GetString());
        }
        finally
        {
            if (recorder is not null) await recorder.DisposeAsync();
            await DeleteTempRootAsync(root);
        }
    }

    [Fact]
    public async Task ScheduledMaintenanceInvalidatesOrdinaryRoomsButKeepsOnlyAuthorizedAdminSandboxRunning()
    {
        var root = TempRoot();
        MatchRecorder? recorder = null;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var catalog = Catalog;
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var platform = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            var admin = platform.Login("Admin", "L12master").Account!;
            var host = platform.Register("mainthost", "password-123").Account!;
            var guest = platform.Register("maintguest", "password-123").Account!;
            var sandboxPlayer = platform.Register("maintsbox", "password-123").Account!;
            var manager = new L12RoomManager(catalog, recorder, platform, () => now,
                maintenanceSandboxFenceActive: () => false);

            var hostSession = Guid.NewGuid();
            var guestSession = Guid.NewGuid();
            var playerSandboxSession = Guid.NewGuid();
            var adminSandboxSession = Guid.NewGuid();
            manager.Connect(hostSession, host.Id, host.Username);
            manager.Connect(guestSession, guest.Id, guest.Username);
            manager.Connect(playerSandboxSession, sandboxPlayer.Id, sandboxPlayer.Username);
            manager.Connect(adminSandboxSession, admin.Id, admin.Username);

            var room = manager.CreateRoom(hostSession);
            var roomCode = Payload(room[0]).GetProperty("roomCode").GetString();
            manager.JoinRoom(guestSession, roomCode);
            await manager.SetReadyAsync(hostSession, true);
            await manager.SetReadyAsync(guestSession, true);
            await manager.CreateSandboxAsync(playerSandboxSession, new L12SandboxRequest());

            var current = platform.OperationsConfig(admin);
            platform.ApplyOperationsConfig(admin, current.Config with
            {
                Maintenance = new L12MaintenanceConfig(true, "预约维护",
                    now.AddMinutes(-1), now.AddHours(1), 2, 2),
            }, current.Version, "activate scheduled maintenance", Context("scheduled-maintenance"));

            var adminSandbox = await manager.CreateSandboxAsync(adminSandboxSession, new L12SandboxRequest());
            Assert.NotEqual("GameOver", StateFor(adminSandboxSession, adminSandbox)
                .GetProperty("phase").GetString());

            await manager.TickRankedClocksAsync(now);

            Assert.Equal("GameOver", StateFor(hostSession, manager.RecoveryState(hostSession))
                .GetProperty("phase").GetString());
            Assert.Equal("GameOver", StateFor(playerSandboxSession, manager.RecoveryState(playerSandboxSession))
                .GetProperty("phase").GetString());
            Assert.NotEqual("GameOver", StateFor(adminSandboxSession, manager.RecoveryState(adminSandboxSession))
                .GetProperty("phase").GetString());

            var continued = await manager.HandleGmActionAsync(adminSandboxSession,
                JsonSerializer.SerializeToElement(new { type = "setLife", targetPlayer = 1, value = 23 }));
            Assert.Equal(23, StateFor(adminSandboxSession, continued)
                .GetProperty("players")[1].GetProperty("master").GetProperty("hp").GetInt32());
        }
        finally
        {
            if (recorder is not null) await recorder.DisposeAsync();
            await DeleteTempRootAsync(root);
        }
    }

    private static L12AdminAuditContext Context(string correlationId)
        => new(correlationId, L12Authorization.Key(L12Permission.AdminOperationsWrite));

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-maintenance-sandbox", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task DeleteTempRootAsync(string root)
    {
        if (!Directory.Exists(root)) return;
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(root, "matches.db"),
        }.ToString();
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            using (var poolKey = new SqliteConnection(connectionString))
                SqliteConnection.ClearPool(poolKey);
            try
            {
                Directory.Delete(root, true);
                return;
            }
            catch (IOException) when (OperatingSystem.IsWindows() && attempt < 8)
            {
                await Task.Delay(50 * attempt);
            }
        }
    }

    private static void AssertBlocked(IReadOnlyList<OutgoingMessage> messages, string code)
    {
        var payload = Payload(Assert.Single(messages));
        Assert.Equal("operationsBlocked", payload.GetProperty("type").GetString());
        Assert.Equal(code, payload.GetProperty("code").GetString());
    }

    private static JsonElement StateFor(Guid sessionId, IReadOnlyList<OutgoingMessage> messages)
        => EnvelopeFor(sessionId, messages, "gameState").GetProperty("state").Clone();

    private static JsonElement EnvelopeFor(Guid sessionId, IReadOnlyList<OutgoingMessage> messages, string type)
        => messages.Where(message => message.SessionId == sessionId)
            .Select(Payload)
            .Single(payload => payload.GetProperty("type").GetString() == type);

    private static JsonElement Payload(OutgoingMessage message)
        => JsonSerializer.SerializeToElement(message.Payload, WebJson);
}
