using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DeploymentControlHttpTests
{
    [Fact]
    public async Task ExactControlRoutesReturnStableProtocolAndDoNotCreateReleaseCommands()
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        var admin = fixture.LoginAdmin();
        await fixture.StartServerAsync();
        await WaitForNoLeasesAsync(fixture.Coordinator);
        using var client = CreateClient(fixture, admin.Token);
        var commandsBefore = fixture.Platform.AdminCommands().Count;

        using var statusResponse = await client.GetAsync("/api/admin/deployment-drain/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        AssertNoStore(statusResponse);
        var open = await ReadJsonAsync(statusResponse);
        AssertProtocolEnvelope(open, "status", "open", stopPermitted: false);
        Assert.Equal(JsonValueKind.Null, open.GetProperty("owner").ValueKind);
        Assert.Equal(JsonValueKind.Null, open.GetProperty("permit").ValueKind);

        using var beginResponse = await PostOwnerAsync(client,
            "/api/admin/deployment-drain/begin", fixture.Owner);
        Assert.Equal(HttpStatusCode.OK, beginResponse.StatusCode);
        AssertNoStore(beginResponse);
        var draining = await ReadJsonAsync(beginResponse);
        AssertProtocolEnvelope(draining, "Applied", "draining", stopPermitted: false);
        Assert.Equal(DeploymentDrainFixture.ProcessInstance,
            draining.GetProperty("processInstance").GetString());
        Assert.Equal(DeploymentDrainFixture.ActiveCommit,
            draining.GetProperty("activeCommit").GetString());
        var owner = draining.GetProperty("owner");
        Assert.Equal(DeploymentDrainFixture.OperationId, owner.GetProperty("operationId").GetString());
        Assert.Equal(DeploymentDrainFixture.TargetCommit, owner.GetProperty("targetCommit").GetString());
        Assert.Equal(DeploymentDrainFixture.ProcessInstance, owner.GetProperty("processInstance").GetString());
        Assert.Equal(0L, draining.GetProperty("admissionLeases").GetInt64());
        Assert.Equal(0L, draining.GetProperty("activityLeases").GetInt64());
        Assert.Equal(JsonValueKind.Null, draining.GetProperty("permit").ValueKind);

        // A successful HTTP seal proves that the control request itself did not hold an
        // Activity lease while the controller checked the stable zero-inflight boundary.
        using var sealResponse = await PostOwnerAsync(client,
            "/api/admin/deployment-drain/seal", fixture.Owner);
        Assert.Equal(HttpStatusCode.OK, sealResponse.StatusCode);
        AssertNoStore(sealResponse);
        var sealedResult = await ReadJsonAsync(sealResponse);
        AssertProtocolEnvelope(sealedResult, "Applied", "sealed", stopPermitted: false);
        Assert.True(sealedResult.GetProperty("sealReady").GetBoolean());
        Assert.Equal(0L, sealedResult.GetProperty("admissionLeases").GetInt64());
        Assert.Equal(0L, sealedResult.GetProperty("activityLeases").GetInt64());
        var permit = sealedResult.GetProperty("permit");
        Assert.Equal(DeploymentDrainFixture.OperationId, permit.GetProperty("operationId").GetString());
        Assert.Equal(DeploymentDrainFixture.TargetCommit, permit.GetProperty("targetCommit").GetString());
        Assert.Equal(DeploymentDrainFixture.ActiveCommit, permit.GetProperty("activeCommit").GetString());
        Assert.Equal(DeploymentDrainFixture.ProcessInstance, permit.GetProperty("processInstance").GetString());
        Assert.Equal(sealedResult.GetProperty("epoch").GetInt64(), permit.GetProperty("epoch").GetInt64());
        var firstSealId = permit.GetProperty("sealId").GetString();
        Assert.True(Guid.TryParseExact(firstSealId, "N", out _));
        var readiness = sealedResult.GetProperty("readiness");
        Assert.True(readiness.GetProperty("rooms").GetProperty("verified").GetBoolean());
        Assert.True(readiness.GetProperty("durability").GetProperty("verified").GetBoolean());
        Assert.True(readiness.GetProperty("platform").GetProperty("verified").GetBoolean());

        using var repeatedResponse = await PostOwnerAsync(client,
            "/api/admin/deployment-drain/seal", fixture.Owner);
        Assert.Equal(HttpStatusCode.OK, repeatedResponse.StatusCode);
        var repeated = await ReadJsonAsync(repeatedResponse);
        AssertProtocolEnvelope(repeated, "Idempotent", "sealed", stopPermitted: false);
        Assert.True(repeated.GetProperty("sealReady").GetBoolean());
        Assert.Equal(firstSealId, repeated.GetProperty("permit").GetProperty("sealId").GetString());

        using var sealedStatusResponse = await client.GetAsync("/api/admin/deployment-drain/status");
        Assert.Equal(HttpStatusCode.OK, sealedStatusResponse.StatusCode);
        var sealedStatus = await ReadJsonAsync(sealedStatusResponse);
        AssertProtocolEnvelope(sealedStatus, "status", "sealed", stopPermitted: false);
        Assert.Equal(JsonValueKind.Null, sealedStatus.GetProperty("permit").ValueKind);
        Assert.Equal(commandsBefore, fixture.Platform.AdminCommands().Count);
    }

    [Fact]
    public async Task CancelRequiresTheMatchingOwnerAndAnOldOwnerCannotSealAfterCancel()
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        var admin = fixture.LoginAdmin();
        await fixture.StartServerAsync();
        await WaitForNoLeasesAsync(fixture.Coordinator);
        using var client = CreateClient(fixture, admin.Token);
        var other = fixture.CreateOwner("55555555555555555555555555555555",
            "6666666666666666666666666666666666666666");

        using (var begin = await PostOwnerAsync(client,
                   "/api/admin/deployment-drain/begin", fixture.Owner))
            Assert.Equal(HttpStatusCode.OK, begin.StatusCode);

        using var wrongCancelResponse = await PostOwnerAsync(client,
            "/api/admin/deployment-drain/cancel", other);
        Assert.Equal(HttpStatusCode.Conflict, wrongCancelResponse.StatusCode);
        var wrongCancel = await ReadJsonAsync(wrongCancelResponse);
        AssertProtocolEnvelope(wrongCancel, "OwnerMismatch", "draining", stopPermitted: false);

        using var cancelResponse = await PostOwnerAsync(client,
            "/api/admin/deployment-drain/cancel", fixture.Owner);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = await ReadJsonAsync(cancelResponse);
        AssertProtocolEnvelope(cancelled, "Applied", "open", stopPermitted: false);
        Assert.Equal(JsonValueKind.Null, cancelled.GetProperty("owner").ValueKind);

        using var staleSealResponse = await PostOwnerAsync(client,
            "/api/admin/deployment-drain/seal", fixture.Owner);
        Assert.Equal(HttpStatusCode.Conflict, staleSealResponse.StatusCode);
        var stale = await ReadJsonAsync(staleSealResponse);
        AssertProtocolEnvelope(stale, "OwnerMismatch", "open", stopPermitted: false);
        Assert.Equal(JsonValueKind.Null, stale.GetProperty("permit").ValueKind);
    }

    [Fact]
    public async Task FreshRoleRevocationAndAnonymousDenialsAreReadOnlyAndNeverReopenDrain()
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        var primaryAdmin = fixture.LoginAdmin();
        var revokedAdmin = fixture.LoginAdmin();
        var registration = fixture.Platform.Register(
            "p" + Guid.NewGuid().ToString("N")[..8], "Password123!");
        Assert.True(registration.Success, registration.Message);
        Assert.True(fixture.Platform.SetRole(primaryAdmin.Account!, registration.Account!.Id, "admin"));
        await fixture.StartServerAsync();
        await WaitForNoLeasesAsync(fixture.Coordinator);

        using (var promoted = CreateClient(fixture, registration.Token))
        using (var promotedStatus = await promoted.GetAsync("/api/admin/deployment-drain/status"))
            Assert.Equal(HttpStatusCode.OK, promotedStatus.StatusCode);

        Assert.True(fixture.Platform.SetRole(primaryAdmin.Account!, registration.Account!.Id, "player"));
        var revokedSession = fixture.Platform.AuthenticateSession($"Bearer {revokedAdmin.Token}");
        Assert.NotNull(revokedSession);
        Assert.Equal(1, fixture.Platform.RevokeOwnSession(revokedSession!,
            revokedSession!.SessionId).RevokedCount);
        var versionAfterIdentityChanges = fixture.Platform.Version;
        var commandsBefore = fixture.Platform.AdminCommands().Count;

        using (var player = CreateClient(fixture, registration.Token))
        using (var denied = await player.GetAsync("/api/admin/deployment-drain/status"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            AssertNoStore(denied);
            Assert.Equal("permission_denied", (await ReadJsonAsync(denied)).GetProperty("code").GetString());
        }
        using (var anonymous = CreateClient(fixture))
        using (var denied = await anonymous.GetAsync("/api/admin/deployment-drain/status"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            AssertNoStore(denied);
            Assert.Equal("authentication_required", (await ReadJsonAsync(denied)).GetProperty("code").GetString());
        }
        using (var revoked = CreateClient(fixture, revokedAdmin.Token))
        using (var denied = await revoked.GetAsync("/api/admin/deployment-drain/status"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.Equal("authentication_required", (await ReadJsonAsync(denied)).GetProperty("code").GetString());
        }
        Assert.Equal(versionAfterIdentityChanges, fixture.Platform.Version);
        Assert.Equal(L12DeploymentDrainPhase.Open, fixture.Coordinator.Snapshot().Phase);

        using var admin = CreateClient(fixture, primaryAdmin.Token);
        using (var begin = await PostOwnerAsync(admin,
                   "/api/admin/deployment-drain/begin", fixture.Owner))
            Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        using (var player = CreateClient(fixture, registration.Token))
        using (var deniedCancel = await PostOwnerAsync(player,
                   "/api/admin/deployment-drain/cancel", fixture.Owner))
            Assert.Equal(HttpStatusCode.Forbidden, deniedCancel.StatusCode);

        Assert.Equal(L12DeploymentDrainPhase.Draining, fixture.Coordinator.Snapshot().Phase);
        Assert.Equal(0, fixture.Coordinator.Snapshot().ActivityLeases);
        Assert.Equal(versionAfterIdentityChanges, fixture.Platform.Version);
        Assert.Equal(commandsBefore, fixture.Platform.AdminCommands().Count);
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("target")]
    [InlineData("process")]
    public async Task NonCanonicalOwnerFieldsAreRejectedBeforeAnyTransition(string invalidField)
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        var admin = fixture.LoginAdmin();
        await fixture.StartServerAsync();
        await WaitForNoLeasesAsync(fixture.Coordinator);
        using var client = CreateClient(fixture, admin.Token);
        var body = new
        {
            operationId = invalidField == "operation" ? "not-a-guid" : DeploymentDrainFixture.OperationId,
            targetCommit = invalidField == "target" ? "abc" : DeploymentDrainFixture.TargetCommit,
            processInstance = invalidField == "process"
                ? Guid.ParseExact(DeploymentDrainFixture.ProcessInstance, "N").ToString("D")
                : DeploymentDrainFixture.ProcessInstance,
        };

        using var response = await client.PostAsJsonAsync(
            "/api/admin/deployment-drain/begin", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
        var error = await ReadJsonAsync(response);
        Assert.Equal("invalid_deployment_owner", error.GetProperty("code").GetString());
        Assert.Equal(L12DeploymentDrainPhase.Open, fixture.Coordinator.Snapshot().Phase);
        Assert.Equal(0, fixture.Coordinator.Snapshot().AdmissionLeases);
        Assert.Equal(0, fixture.Coordinator.Snapshot().ActivityLeases);
        Assert.Empty(fixture.Platform.AdminCommands());
    }

    [Fact]
    public async Task HighRiskAuditFailureBlocksMutationsButNotReadOnlyStatus()
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync();
        var admin = fixture.LoginAdmin();
        await fixture.StartServerAsync();
        await WaitForNoLeasesAsync(fixture.Coordinator);
        fixture.Platform.AuditAvailabilityProbeOverride = () => false;
        try
        {
            using var client = CreateClient(fixture, admin.Token);
            using var status = await client.GetAsync("/api/admin/deployment-drain/status");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            AssertProtocolEnvelope(await ReadJsonAsync(status), "status", "open", stopPermitted: false);

            using var begin = await PostOwnerAsync(client,
                "/api/admin/deployment-drain/begin", fixture.Owner);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, begin.StatusCode);
            AssertNoStore(begin);
            Assert.Equal("audit_unavailable", (await ReadJsonAsync(begin)).GetProperty("code").GetString());
            Assert.Equal(L12DeploymentDrainPhase.Open, fixture.Coordinator.Snapshot().Phase);
            Assert.Empty(fixture.Platform.AdminCommands());
        }
        finally
        {
            fixture.Platform.AuditAvailabilityProbeOverride = null;
        }
    }

    [Fact]
    public async Task UnconfiguredServerReturnsProtocolUnavailableWithoutCreatingASecondAuthority()
    {
        await using var fixture = await DeploymentDrainFixture.CreateAsync(attachCoordinator: false);
        var admin = fixture.LoginAdmin();
        await fixture.StartServerAsync();
        using var client = CreateClient(fixture, admin.Token);

        using var status = await client.GetAsync("/api/admin/deployment-drain/status");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status.StatusCode);
        AssertNoStore(status);
        Assert.Equal("deployment_protocol_unavailable",
            (await ReadJsonAsync(status)).GetProperty("code").GetString());

        using var begin = await PostOwnerAsync(client,
            "/api/admin/deployment-drain/begin", fixture.Owner);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, begin.StatusCode);
        Assert.Equal("deployment_protocol_unavailable",
            (await ReadJsonAsync(begin)).GetProperty("code").GetString());
        Assert.Equal(L12DeploymentDrainPhase.Open, fixture.Coordinator.Snapshot().Phase);
        Assert.Empty(fixture.Platform.AdminCommands());
    }

    private static HttpClient CreateClient(DeploymentDrainFixture fixture, string? token = null)
    {
        var client = new HttpClient { BaseAddress = fixture.BaseAddress! };
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> PostOwnerAsync(HttpClient client, string path,
        L12DeploymentDrainOwner owner)
        => client.PostAsJsonAsync(path, new
        {
            owner.OperationId,
            owner.TargetCommit,
            owner.ProcessInstance,
        });

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static void AssertProtocolEnvelope(JsonElement root, string code, string phase,
        bool stopPermitted)
    {
        Assert.Equal(L12DeploymentDrainCoordinator.ProtocolVersion,
            root.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(code, root.GetProperty("code").GetString());
        Assert.Equal(phase, root.GetProperty("phase").GetString());
        Assert.Equal(stopPermitted, root.GetProperty("stopPermitted").GetBoolean());
        Assert.Equal(DeploymentDrainFixture.ProcessInstance,
            root.GetProperty("processInstance").GetString());
        Assert.Equal(DeploymentDrainFixture.ActiveCommit,
            root.GetProperty("activeCommit").GetString());
        Assert.True(root.TryGetProperty("epoch", out _));
        Assert.True(root.TryGetProperty("admissionLeases", out _));
        Assert.True(root.TryGetProperty("activityLeases", out _));
        Assert.True(root.TryGetProperty("fenceSynchronized", out _));
        Assert.True(root.TryGetProperty("fenceUnknown", out _));
        Assert.True(root.TryGetProperty("permit", out _));
        Assert.True(root.TryGetProperty("readiness", out _));
    }

    private static void AssertNoStore(HttpResponseMessage response)
        => Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

    private static async Task WaitForNoLeasesAsync(L12DeploymentDrainCoordinator coordinator)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        do
        {
            var snapshot = coordinator.Snapshot();
            if (snapshot.AdmissionLeases == 0 && snapshot.ActivityLeases == 0) return;
            await Task.Delay(20);
        } while (DateTimeOffset.UtcNow < deadline);
        var final = coordinator.Snapshot();
        Assert.Equal(0, final.AdmissionLeases);
        Assert.Equal(0, final.ActivityLeases);
    }
}
