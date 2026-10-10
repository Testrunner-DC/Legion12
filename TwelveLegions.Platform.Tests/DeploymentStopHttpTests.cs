using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DeploymentStopHttpTests
{
    [Theory]
    [InlineData("/api/admin/deployment-drain/consume")]
    [InlineData("/API/ADMIN/DEPLOYMENT-DRAIN/CONSUME/")]
    [InlineData("/api/admin/v1/deployment-drain/consume")]
    public async Task ConsumedPermitIsIdempotentAndCannotBeReopenedOverHttp(string route)
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        var admin = fixture.LoginAdmin();
        await fixture.StartServerAsync();
        var permit = await SealAsync(fixture);
        var revision = fixture.Platform.Version;
        using var client = Client(fixture, admin.Token);
        using var first = await client.PostAsJsonAsync(route, permit);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("no-store", first.Headers.CacheControl?.ToString() ?? string.Empty);
        var payload = await JsonAsync(first);
        Assert.True(payload.GetProperty("stopConsumed").GetBoolean());
        Assert.True(payload.GetProperty("stopPermitted").GetBoolean());
        Assert.Equal("Applied", payload.GetProperty("code").GetString());
        Assert.Equal(permit.SealId, payload.GetProperty("permit").GetProperty("sealId").GetString());
        using var repeated = await client.PostAsJsonAsync(route, permit);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal("Idempotent", (await JsonAsync(repeated)).GetProperty("code").GetString());
        using var cancel = await client.PostAsJsonAsync("/api/admin/deployment-drain/cancel", new
        {
            fixture.Owner.OperationId, fixture.Owner.TargetCommit, fixture.Owner.ProcessInstance,
        });
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        Assert.True(fixture.Coordinator.Snapshot().StopConsumed);
        Assert.Equal(L12DeploymentDrainPhase.Sealed, fixture.Coordinator.Snapshot().Phase);
        Assert.False(fixture.Coordinator.TryAcquireAdmission(out _));
        Assert.False(fixture.Coordinator.TryAcquireActivity(out _));
        Assert.Equal(revision, fixture.Platform.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsumeRequiresFreshAuthorizedSessionAndDoesNotWriteDeniedAudit(bool anonymous)
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        var player = fixture.CreatePlayer("pl");
        var login = fixture.Platform.Login(player.Username, "Password123!");
        Assert.True(login.Success);
        await fixture.StartServerAsync();
        var permit = await SealAsync(fixture);
        var revision = fixture.Platform.Version;
        using var client = Client(fixture, anonymous ? null : login.Token);
        using var response = await client.PostAsJsonAsync("/api/admin/deployment-drain/consume", permit);
        Assert.Equal(anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(fixture.Coordinator.Snapshot().StopConsumed);
        Assert.Equal(revision, fixture.Platform.Version);
    }

    [Fact]
    public async Task StalePermitDoesNotConsumeAndValidPermitStillWorks()
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        var admin = fixture.LoginAdmin();
        await fixture.StartServerAsync();
        var permit = await SealAsync(fixture);
        using var client = Client(fixture, admin.Token);
        using var stale = await client.PostAsJsonAsync("/api/admin/deployment-drain/consume",
            permit with { Epoch = permit.Epoch - 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.False(fixture.Coordinator.Snapshot().StopConsumed);
        using var valid = await client.PostAsJsonAsync("/api/admin/deployment-drain/consume", permit);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.True(fixture.Coordinator.Snapshot().StopConsumed);
    }

    [Fact]
    public async Task MalformedPermitAndUnavailableAuditFailClosed()
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        var admin = fixture.LoginAdmin();
        await fixture.StartServerAsync();
        var permit = await SealAsync(fixture);
        using var client = Client(fixture, admin.Token);
        using var malformed = await client.PostAsJsonAsync("/api/admin/deployment-drain/consume",
            permit with { SealId = "private-invalid-value" });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.False(fixture.Coordinator.Snapshot().StopConsumed);
        fixture.Platform.AuditAvailabilityProbeOverride = () => false;
        using var unavailable = await client.PostAsJsonAsync("/api/admin/deployment-drain/consume", permit);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.False(fixture.Coordinator.Snapshot().StopConsumed);
    }

    private static async Task<L12DeploymentSealPermit> SealAsync(DeploymentDrainFixture fixture)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (fixture.Coordinator.Snapshot() is { ActivityLeases: > 0 } or { AdmissionLeases: > 0 })
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "Startup producer did not release its lease");
            await Task.Delay(20);
        }
        Assert.True(fixture.Controller.Begin(fixture.Owner).Code is "Applied" or "Idempotent");
        var sealedResult = await fixture.Controller.SealAsync(fixture.Owner);
        Assert.True(sealedResult.Granted, sealedResult.Code);
        return sealedResult.Permit!;
    }

    [Fact]
    public async Task InvalidatedConsumedPermitCannotBeReturnedAsAStopConfirmation()
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        var admin = fixture.LoginAdmin();
        await fixture.StartServerAsync();
        var permit = await SealAsync(fixture);
        Assert.True(fixture.Coordinator.ConsumeStopPermit(permit).Succeeded);
        Assert.Throws<InvalidOperationException>(() => fixture.Coordinator.TryRunSealedTransportCleanup(
            () => throw new InvalidOperationException("synthetic unexpected transport state")));
        using var client = Client(fixture, admin.Token);
        using var response = await client.PostAsJsonAsync("/api/admin/deployment-drain/consume", permit);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var payload = await JsonAsync(response);
        Assert.True(payload.GetProperty("stopConsumed").GetBoolean());
        Assert.False(payload.GetProperty("stopPermitted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("permit").ValueKind);
        Assert.False(fixture.Coordinator.TryAcquireAdmission(out _));
        Assert.False(fixture.Coordinator.TryAcquireActivity(out _));
    }

    private static HttpClient Client(DeploymentDrainFixture fixture, string? token)
    {
        var client = new HttpClient { BaseAddress = fixture.BaseAddress! };
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task ValidSealedTransportCleanupThenExplicitCancelAllowsExactSessionReplacement()
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        var player = fixture.CreatePlayer("re");
        var originalId = Guid.NewGuid();
        _ = await fixture.Rooms.ConnectAsync(originalId, player.Id, player.Username);
        Assert.True(fixture.Controller.Begin(fixture.Owner).Code is "Applied" or "Idempotent");
        var sealedResult = await fixture.Controller.SealAsync(fixture.Owner);
        Assert.True(sealedResult.Granted, sealedResult.Code);
        Assert.Empty(fixture.Rooms.DisconnectTransportAfterDeploymentSeal(originalId));
        Assert.Equal("Applied", fixture.Controller.Cancel(fixture.Owner).Code);
        var replacementId = Guid.NewGuid();
        var replacement = await fixture.Rooms.ConnectAsync(replacementId, player.Id, player.Username);
        Assert.Equal(replacementId, replacement.SessionId);
        Assert.Equal(originalId, replacement.ReplacedSessionId);
        var sessionField = typeof(L12RoomManager).GetField("_sessions",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var sessions = sessionField.GetValue(fixture.Rooms)!;
        var keys = (IEnumerable<Guid>)sessions.GetType().GetProperty("Keys")!.GetValue(sessions)!;
        Assert.Equal(replacementId, Assert.Single(keys));
        Assert.True(fixture.Rooms.CaptureDeploymentRoomReadiness().Clear);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
