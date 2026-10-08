using System.Text.Json;

namespace TwelveLegions.Server;

internal enum L12DeploymentIngressKind
{
    Bypass,
    Admission,
    Activity,
    Mixed,
}

public sealed partial class L12WebSocketServer
{
    private static bool IsDeploymentControlPath(PathString path)
        => path == "/api/admin/deployment-drain/status"
            || path == "/api/admin/deployment-drain/begin"
            || path == "/api/admin/deployment-drain/seal"
            || path == "/api/admin/deployment-drain/consume"
            || path == "/api/admin/deployment-drain/cancel";

    internal static L12DeploymentIngressKind ClassifyDeploymentHttpIngress(string method, string path)
    {
        // Endpoint routing is case-insensitive and accepts a trailing slash. Match
        // that contract; a path spelling variant must not bypass the barrier.
        path = path.TrimEnd('/');
        if (HttpMethods.IsOptions(method) || !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || IsDeploymentControlPath(new PathString(path)))
            return L12DeploymentIngressKind.Bypass;
        if (HttpMethods.IsPost(method) && path.StartsWith("/api/tournaments/", StringComparison.OrdinalIgnoreCase)
            && (path.EndsWith("/start", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/rounds", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/rematch", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/check-in", StringComparison.OrdinalIgnoreCase)))
            return L12DeploymentIngressKind.Admission;
        return L12DeploymentIngressKind.Activity;
    }

    private bool TryAcquireHttpDeploymentGuard(HttpRequest request, out DeploymentIngressGuard? guard)
    {
        guard = null;
        var coordinator = _rooms.DeploymentDrain;
        var kind = ClassifyDeploymentHttpIngress(request.Method, request.Path.Value ?? string.Empty);
        if (coordinator is null || kind == L12DeploymentIngressKind.Bypass) return true;
        if (!TryAcquireDeploymentLease(coordinator, kind, out var lease)) return false;
        // The lease covers request-body processing, the command bus outer commit, room
        // command consumption and the actual response; never just the store callback.
        guard = new DeploymentIngressGuard(lease!, enterAdmissionScope: true);
        return true;
    }

    private sealed class DeploymentIngressGuard : IDisposable
    {
        private readonly L12DeploymentDrainLease _lease;
        private readonly IDisposable? _executionScope;
        private int _disposed;

        internal DeploymentIngressGuard(L12DeploymentDrainLease lease, bool enterAdmissionScope)
        {
            _lease = lease;
            try
            {
                if (enterAdmissionScope && lease.Kind == L12DeploymentLeaseKind.Admission
                    && lease.IsOriginal)
                    _executionScope = lease.EnterExecutionScope();
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _executionScope?.Dispose(); }
            finally { _lease.Dispose(); }
        }
    }

    internal static L12DeploymentIngressKind ClassifyDeploymentIngress(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String)
                return L12DeploymentIngressKind.Activity;

            return ClassifyDeploymentIngress(type.GetString(), root);
        }
        catch (JsonException)
        {
            return L12DeploymentIngressKind.Activity;
        }
    }

    internal static L12DeploymentDrainLease? TryAcquireQueuedDeploymentLease(
        L12DeploymentDrainCoordinator coordinator, string json)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        var kind = ClassifyDeploymentIngress(json);
        if (kind == L12DeploymentIngressKind.Bypass) return null;
        return TryAcquireDeploymentLease(coordinator, kind, out var lease) ? lease : null;
    }

    private static L12DeploymentIngressKind ClassifyDeploymentIngress(string? messageType,
        JsonElement root)
        => messageType switch
        {
            "ping" or "deploymentProbe" => L12DeploymentIngressKind.Bypass,
            "hello" or "pollMatchmaking" or "syncState" or "enterTournamentMatch"
                => L12DeploymentIngressKind.Mixed,
            "createRoom" or "updateRoomOptions" or "createSandbox" or "joinMatchmaking"
                or "joinRoom" or "inviteFriend" or "selectDeck" or "selectCustomDeck"
                => L12DeploymentIngressKind.Admission,
            "resolveFriendInvitation" => ReadBoolean(root, "accept", false)
                ? L12DeploymentIngressKind.Admission : L12DeploymentIngressKind.Activity,
            "ready" => ReadBoolean(root, "ready", true)
                ? L12DeploymentIngressKind.Admission : L12DeploymentIngressKind.Activity,
            _ => L12DeploymentIngressKind.Activity,
        };

    private static bool ReadBoolean(JsonElement root, string propertyName, bool fallback)
        => root.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean() : fallback;

    private static bool TryAcquireDeploymentLease(L12DeploymentDrainCoordinator coordinator,
        L12DeploymentIngressKind kind, out L12DeploymentDrainLease? lease)
    {
        lease = null;
        return kind switch
        {
            L12DeploymentIngressKind.Admission => coordinator.TryAcquireAdmission(out lease),
            L12DeploymentIngressKind.Activity => coordinator.TryAcquireActivity(out lease),
            L12DeploymentIngressKind.Mixed => coordinator.TryAcquireAdmission(out lease)
                || coordinator.TryAcquireActivity(out lease),
            _ => false,
        };
    }

    private bool TryAcquireHelloDeploymentGuard(out DeploymentIngressGuard? guard)
    {
        guard = null;
        var coordinator = _rooms.DeploymentDrain;
        if (coordinator is null) return true;
        if (!TryAcquireDeploymentLease(coordinator, L12DeploymentIngressKind.Mixed, out var lease))
            return false;
        guard = new DeploymentIngressGuard(lease!, enterAdmissionScope: true);
        return true;
    }

    private bool TryAcquireDeploymentActivityGuard(out DeploymentIngressGuard? guard)
    {
        guard = null;
        var coordinator = _rooms.DeploymentDrain;
        if (coordinator is null) return true;
        if (!coordinator.TryAcquireActivity(out var lease)) return false;
        guard = new DeploymentIngressGuard(lease!, enterAdmissionScope: false);
        return true;
    }

    private static object CreateDeploymentDrainActivePayload(string json)
    {
        string? requestId = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            requestId = DeploymentRequestId(document.RootElement);
        }
        catch (JsonException) { }
        return DeploymentDrainActivePayload(requestId);
    }

    private static IReadOnlyList<OutgoingMessage> DeploymentDrainActive(Guid sessionId,
        JsonElement root)
        => [new OutgoingMessage(sessionId, DeploymentDrainActivePayload(DeploymentRequestId(root)))];

    private static object DeploymentDrainActivePayload(string? requestId) => new
    {
        type = "error",
        code = "deploymentDrainActive",
        message = "服务器正在更新，请稍后再试",
        requestId,
    };

    private static string? DeploymentRequestId(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("requestId", out var value)
            || value.ValueKind != JsonValueKind.String)
            return null;
        var requestId = value.GetString();
        return requestId is { Length: <= 128 } ? requestId : null;
    }

    private void CleanupRejectedHello(Guid sessionId, string accountId)
    {
        _establishedInboundConnections.TryRemove(sessionId, out _);
        _socketPlatformSessions.TryRemove(sessionId, out _);
        TryReleaseActiveAccountSocket(_activeAccountSockets, accountId, sessionId);
        _socketCapabilities.TryRemove(sessionId, out _);
        if (_snapshotCodecs.TryGetValue(sessionId, out var snapshotCodec))
            snapshotCodec.SetDeltaEnabled(false);
        try { _ = _rooms.Disconnect(sessionId); }
        catch (L12DeploymentBarrierClosedException) { }
    }

    private IReadOnlyList<OutgoingMessage> DisconnectForTransportClose(Guid sessionId)
    {
        var coordinator = _rooms.DeploymentDrain;
        if (coordinator is not null)
        {
            var snapshot = coordinator.Snapshot();
            if (snapshot.Phase == L12DeploymentDrainPhase.Sealed && snapshot.FenceSynchronized)
                return _rooms.DisconnectTransportAfterDeploymentSeal(sessionId);
        }
        return _rooms.Disconnect(sessionId);
    }
}
