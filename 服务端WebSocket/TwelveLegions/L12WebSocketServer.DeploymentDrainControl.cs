namespace TwelveLegions.Server;

internal sealed record L12DeploymentDrainControlRequest(string OperationId, string TargetCommit,
    string ProcessInstance);

public sealed partial class L12WebSocketServer
{
    private readonly L12DeploymentDrainController? _deploymentController;

    private void MapDeploymentDrainEndpoints()
    {
        var app = _app ?? throw new InvalidOperationException("Server must be initialized before mapping control routes");
        app.MapGet("/api/admin/deployment-drain/status", (HttpRequest request) =>
        {
            if (!TryAuthorizeDeploymentControl(request, L12Permission.ReleasesRead, out var failure)) return failure;
            if (_deploymentController is null) return DeploymentProtocolUnavailable(request);
            return DeploymentControlResponse(_deploymentController.Status(), StatusCodes.Status200OK);
        });
        app.MapPost("/api/admin/deployment-drain/begin", (HttpRequest request, L12DeploymentDrainControlRequest body) =>
        {
            if (!TryPrepareDeploymentControl(request, body, out var owner, out var failure)) return failure;
            var result = _deploymentController!.Begin(owner!);
            return DeploymentControlResponse(result, DeploymentTransitionStatus(result));
        });
        app.MapPost("/api/admin/deployment-drain/seal", async (HttpRequest request, L12DeploymentDrainControlRequest body) =>
        {
            if (!TryPrepareDeploymentControl(request, body, out var owner, out var failure)) return failure;
            var result = await _deploymentController!.SealAsync(owner!, request.HttpContext.RequestAborted);
            return DeploymentControlResponse(result, result.Granted ? StatusCodes.Status200OK
                : result.Code is "probe_unknown" or "probe_cancelled" ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status409Conflict);
        });
        app.MapPost("/api/admin/deployment-drain/cancel", (HttpRequest request, L12DeploymentDrainControlRequest body) =>
        {
            if (!TryPrepareDeploymentControl(request, body, out var owner, out var failure)) return failure;
            var result = _deploymentController!.Cancel(owner!);
            return DeploymentControlResponse(result, DeploymentTransitionStatus(result));
        });
    }

    private bool TryAuthorizeDeploymentControl(HttpRequest request, L12Permission permission, out IResult failure)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        // Use the existing fresh account/session authority, not a cached role or special token.
        // Denial must not write an audit through a Sealed barrier or reopen it to log a 401/403.
        var authenticated = _platform.AuthenticateSession(request.Headers.Authorization);
        if (authenticated is null)
        {
            failure = ApiError(request, "authentication_required", "请先登录账号", StatusCodes.Status401Unauthorized);
            return false;
        }
        if (!L12Authorization.HasPermission(authenticated.Account, permission))
        {
            failure = ApiError(request, "permission_denied", "当前账号没有执行此操作的权限", StatusCodes.Status403Forbidden);
            return false;
        }
        failure = Results.Empty;
        return true;
    }

    private bool TryPrepareDeploymentControl(HttpRequest request, L12DeploymentDrainControlRequest body,
        out L12DeploymentDrainOwner? owner, out IResult failure)
    {
        owner = null;
        if (!TryAuthorizeDeploymentControl(request, L12Permission.ReleasesExecute, out failure)) return false;
        if (_deploymentController is null) { failure = DeploymentProtocolUnavailable(request); return false; }
        if (!L12DeploymentDrainOwner.TryCreate(body.OperationId, body.TargetCommit, body.ProcessInstance, out owner))
        {
            failure = ApiError(request, "invalid_deployment_owner", "发布操作标识无效", StatusCodes.Status400BadRequest);
            return false;
        }
        // This read precedes every management transition and never runs under the coordinator lock.
        // Actual deploy/rollback still use their original High command-bus/artifact/version gates.
        if (!_platform.HighRiskAuditAvailable())
        {
            failure = ApiError(request, "audit_unavailable", "发布审计暂不可用", StatusCodes.Status503ServiceUnavailable);
            return false;
        }
        failure = Results.Empty;
        return true;
    }

    private static int DeploymentTransitionStatus(L12DeploymentDrainControlResult result)
        => result.Code is "Applied" or "Idempotent" ? StatusCodes.Status200OK
            : result.Code is "FenceFailure" or "UnknownFence" ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status409Conflict;

    private static IResult DeploymentProtocolUnavailable(HttpRequest request)
        => ApiError(request, "deployment_protocol_unavailable", "本实例尚未启用发布保护协议", StatusCodes.Status503ServiceUnavailable);

    private static IResult DeploymentControlResponse(L12DeploymentDrainControlResult result, int statusCode)
        => Results.Json(new
        {
            protocolVersion = L12DeploymentDrainCoordinator.ProtocolVersion,
            code = result.Code,
            phase = result.Snapshot.Phase.ToString().ToLowerInvariant(),
            result.Snapshot.Epoch,
            result.Snapshot.ProcessInstance,
            result.Snapshot.ActiveCommit,
            owner = result.Snapshot.Owner is { } owner ? new
            {
                operationId = owner.OperationId,
                targetCommit = owner.TargetCommit,
                processInstance = owner.ProcessInstance,
            } : null,
            result.Snapshot.AdmissionLeases,
            result.Snapshot.ActivityLeases,
            result.Snapshot.FenceSynchronized,
            result.Snapshot.FenceUnknown,
            stopPermitted = result.Granted,
            result.Permit,
            readiness = new { result.Rooms, result.Durability, result.Platform },
        }, statusCode: statusCode);
}
