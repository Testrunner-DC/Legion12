using System.Text.RegularExpressions;

namespace TwelveLegions.Server;

public sealed record L12VerifiedReleaseArtifactView(
    string Id,
    string Commit,
    string ReleaseSha256,
    string? CardsHash,
    string? CardsSha256,
    DateTimeOffset VerifiedAt,
    IReadOnlyList<string> VerificationGates,
    IReadOnlyList<string> Environments);

public sealed record L12ReleaseProbeResult(bool Success, string Code, long DurationMs);

public sealed record L12ReleaseRuntimeObservation(
    string Environment,
    bool Configured,
    string State,
    string? ActiveArtifactId,
    string? ActiveCommit,
    L12ReleaseProbeResult Health,
    L12ReleaseProbeResult WebSocket,
    DateTimeOffset ObservedAt);

public sealed record L12ReleaseAdapterRequest(
    string CommandId,
    string Action,
    string Environment,
    L12VerifiedReleaseArtifactView Artifact,
    string? PreviousArtifactId,
    string? RollbackTargetRunId);

public sealed record L12ReleaseAdapterExecutionResult(
    bool ActivationSucceeded,
    L12ReleaseProbeResult Health,
    L12ReleaseProbeResult WebSocket,
    bool RollbackAttempted,
    bool RollbackSucceeded,
    string Code);

public interface IL12ReleaseControlAdapter
{
    IReadOnlyList<L12VerifiedReleaseArtifactView> VerifiedArtifacts { get; }
    bool VerifyArtifactHash(string artifactId);
    L12ReleaseAdapterExecutionResult Execute(L12ReleaseAdapterRequest request);
    IReadOnlyList<L12ReleaseRuntimeObservation> ObserveRuntime();
}

public sealed class L12DisabledReleaseControlAdapter : IL12ReleaseControlAdapter
{
    public IReadOnlyList<L12VerifiedReleaseArtifactView> VerifiedArtifacts => [];
    public bool VerifyArtifactHash(string artifactId) => false;
    public L12ReleaseAdapterExecutionResult Execute(L12ReleaseAdapterRequest request)
        => new(false, new(false, "adapter-unconfigured", 0), new(false, "adapter-unconfigured", 0),
            false, false, "adapter-unconfigured");
    public IReadOnlyList<L12ReleaseRuntimeObservation> ObserveRuntime()
        => L12PlatformStore.ReleaseEnvironmentsAllowed.Select(environment => new L12ReleaseRuntimeObservation(
            environment, false, "unconfigured", null, null, new(false, "adapter-unconfigured", 0),
            new(false, "adapter-unconfigured", 0), DateTimeOffset.UtcNow)).ToArray();
}

public sealed record L12ReleaseCommandPayload(
    string Action,
    string Environment,
    L12VerifiedReleaseArtifactView Artifact,
    string? RollbackTargetRunId = null);

public sealed record L12ReleasePlanView(
    string Action,
    string Environment,
    long EnvironmentVersion,
    string? CurrentArtifactId,
    L12VerifiedReleaseArtifactView TargetArtifact,
    string? RollbackTargetRunId,
    IReadOnlyList<string> Steps,
    bool WillExecute);

public sealed record L12ReleaseCheckView(
    string Kind,
    bool Success,
    string Code,
    long DurationMs,
    DateTimeOffset CheckedAt);

public sealed record L12ReleaseRunView(
    string Id,
    string CommandId,
    string Action,
    string Environment,
    string ArtifactId,
    string Commit,
    string ReleaseSha256,
    string? CardsHash,
    string Status,
    string ActorId,
    string ActorName,
    string? PreviousArtifactId,
    string? RollbackTargetRunId,
    bool RollbackAttempted,
    bool RollbackSucceeded,
    string ResultCode,
    IReadOnlyList<L12ReleaseCheckView> Checks,
    long EnvironmentVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record L12ReleaseEnvironmentView(
    string Environment,
    long Version,
    string State,
    bool AdapterConfigured,
    string? ActiveArtifactId,
    string? ActiveCommit,
    string? LastRunId,
    L12ReleaseProbeResult Health,
    L12ReleaseProbeResult WebSocket,
    DateTimeOffset ObservedAt);

public sealed record L12ReleaseOperationView(
    bool Applied,
    L12ReleasePlanView Plan,
    L12ReleaseRunView? Run);

public sealed class L12ReleaseScopeException : InvalidOperationException
{
    public L12ReleaseScopeException(string message) : base(message) { }
}

public sealed class L12ReleaseVersionConflictException : InvalidOperationException
{
    public L12ReleaseVersionConflictException(string message) : base(message) { }
}

public sealed class L12ReleaseArtifactException : InvalidOperationException
{
    public string Code { get; }
    public L12ReleaseArtifactException(string code, string message) : base(message) => Code = code;
}

public sealed partial class L12PlatformStore
{
    internal static readonly string[] ReleaseEnvironmentsAllowed = ["staging", "production"];
    private static readonly Regex ReleaseIdPattern = new("^[a-zA-Z0-9_.-]{1,128}$", RegexOptions.Compiled);
    private static readonly Regex CommitPattern = new("^[0-9a-f]{40}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AdapterCodePattern = new("^[a-z0-9_.-]{1,64}$", RegexOptions.Compiled);

    private sealed class ReleaseEnvironmentRow
    {
        public string Environment { get; set; } = string.Empty;
        public long Version { get; set; }
        public string State { get; set; } = "idle";
        public string? ActiveArtifactId { get; set; }
        public string? ActiveCommit { get; set; }
        public string? ActiveReleaseSha256 { get; set; }
        public string? LastRunId { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class ReleaseCheckRow
    {
        public string Kind { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Code { get; set; } = string.Empty;
        public long DurationMs { get; set; }
        public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class ReleaseRunRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string CommandId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string ArtifactId { get; set; } = string.Empty;
        public string Commit { get; set; } = string.Empty;
        public string ReleaseSha256 { get; set; } = string.Empty;
        public string? CardsHash { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ActorId { get; set; } = string.Empty;
        public string ActorName { get; set; } = string.Empty;
        public string? PreviousArtifactId { get; set; }
        public string? RollbackTargetRunId { get; set; }
        public bool RollbackAttempted { get; set; }
        public bool RollbackSucceeded { get; set; }
        public string ResultCode { get; set; } = string.Empty;
        public List<ReleaseCheckRow> Checks { get; set; } = [];
        public long EnvironmentVersion { get; set; }
        public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public IReadOnlyList<L12VerifiedReleaseArtifactView> ReleaseArtifacts(L12AccountView actor,
        IL12ReleaseControlAdapter adapter)
    {
        lock (_gate)
        {
            EnsurePermission(actor, L12Permission.ReleasesRead);
            return ValidArtifacts(adapter).OrderByDescending(item => item.VerifiedAt).ToArray();
        }
    }

    public IReadOnlyList<L12ReleaseEnvironmentView> ReleaseEnvironments(L12AccountView actor,
        IL12ReleaseControlAdapter adapter)
    {
        lock (_gate)
        {
            EnsurePermission(actor, L12Permission.ReleaseRuntimeRead);
            IReadOnlyList<L12ReleaseRuntimeObservation> observations;
            try { observations = adapter.ObserveRuntime() ?? []; }
            catch { observations = []; }
            var byEnvironment = observations.Where(item => IsReleaseEnvironment(item.Environment))
                .GroupBy(item => item.Environment, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            return ReleaseEnvironmentsAllowed.Select(environment =>
            {
                var state = _data.ReleaseEnvironments.FirstOrDefault(item => item.Environment == environment);
                byEnvironment.TryGetValue(environment, out var observation);
                var observedArtifact = SafeIdentifier(observation?.ActiveArtifactId);
                var drifted = state?.ActiveArtifactId is not null && observedArtifact is not null
                    && state.ActiveArtifactId != observedArtifact;
                return new L12ReleaseEnvironmentView(environment, state?.Version ?? 0,
                    drifted ? "drift" : SafeState(observation?.State, state?.State ?? "idle"),
                    observation?.Configured ?? false, observedArtifact ?? state?.ActiveArtifactId,
                    SafeCommit(observation?.ActiveCommit) ?? state?.ActiveCommit, state?.LastRunId,
                    SafeProbe(observation?.Health, observation?.Configured == true ? "health-unavailable" : "adapter-unconfigured"),
                    SafeProbe(observation?.WebSocket, observation?.Configured == true ? "ws-unavailable" : "adapter-unconfigured"),
                    observation?.ObservedAt ?? state?.UpdatedAt ?? DateTimeOffset.UtcNow);
            }).ToArray();
        }
    }

    public IReadOnlyList<L12ReleaseRunView> ReleaseRuns(L12AccountView actor, string? environment = null,
        string? status = null, int limit = 100)
    {
        lock (_gate)
        {
            EnsurePermission(actor, L12Permission.ReleasesRead);
            var query = _data.ReleaseRuns.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(environment))
                query = query.Where(item => item.Environment == environment.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(item => item.Status == status.Trim());
            return query.OrderByDescending(item => item.StartedAt).Take(Math.Clamp(limit, 1, 500))
                .Select(ToView).ToArray();
        }
    }

    public L12ReleaseRunView? ReleaseRun(L12AccountView actor, string id)
    {
        lock (_gate)
        {
            EnsurePermission(actor, L12Permission.ReleasesRead);
            var row = _data.ReleaseRuns.FirstOrDefault(item => item.Id == id);
            return row is null ? null : ToView(row);
        }
    }

    internal L12ReleaseCommandPayload CaptureReleaseDeploy(L12AccountView actor, string artifactId,
        string environment, IL12ReleaseControlAdapter adapter)
    {
        lock (_gate)
        {
            EnsurePermission(actor, L12Permission.ReleasesExecute);
            var normalizedEnvironment = RequireReleaseEnvironment(environment);
            var artifact = RequireVerifiedArtifact(adapter, artifactId, normalizedEnvironment);
            return new L12ReleaseCommandPayload("deploy", normalizedEnvironment, artifact);
        }
    }

    internal L12ReleaseCommandPayload CaptureReleaseRollback(L12AccountView actor, string targetRunId,
        IL12ReleaseControlAdapter adapter)
    {
        lock (_gate)
        {
            EnsurePermission(actor, L12Permission.ReleasesExecute);
            var target = _data.ReleaseRuns.FirstOrDefault(item => item.Id == targetRunId)
                ?? throw new KeyNotFoundException("回滚目标发布记录不存在");
            if (target.Status != "succeeded")
                throw new L12ReleaseScopeException("只能回滚到成功且已完成健康/WS冒烟的发布记录");
            var artifact = RequireVerifiedArtifact(adapter, target.ArtifactId, target.Environment);
            if (!string.Equals(artifact.Commit, target.Commit, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(artifact.ReleaseSha256, target.ReleaseSha256, StringComparison.OrdinalIgnoreCase))
                throw new L12ReleaseArtifactException("artifact-snapshot-mismatch", "工件与历史发布快照不一致");
            return new L12ReleaseCommandPayload("rollback", target.Environment, artifact, target.Id);
        }
    }

    internal L12ReleaseOperationView PlanRelease(L12AccountView actor, L12ReleaseCommandPayload payload,
        long expectedVersion, IL12ReleaseControlAdapter adapter)
    {
        lock (_gate)
        {
            EnsurePermission(actor, L12Permission.ReleasesExecute);
            ValidateReleasePayload(payload, expectedVersion, adapter);
            return new L12ReleaseOperationView(false, BuildPlan(payload, expectedVersion, false), null);
        }
    }

    internal L12ReleaseOperationView ExecuteRelease(L12AccountView actor, L12ReleaseCommandPayload payload,
        long expectedVersion, IL12ReleaseControlAdapter adapter, L12AdminAuditContext context)
    {
        lock (_gate)
        {
            EnsurePermission(actor, L12Permission.ReleasesExecute);
            ValidateReleasePayload(payload, expectedVersion, adapter);
            var environment = GetOrCreateReleaseEnvironment(payload.Environment);
            var plan = BuildPlan(payload, expectedVersion, true);
            var startedAt = DateTimeOffset.UtcNow;
            L12ReleaseAdapterExecutionResult adapterResult;
            try
            {
                adapterResult = adapter.Execute(new L12ReleaseAdapterRequest(context.CommandId ?? string.Empty,
                    payload.Action, payload.Environment, payload.Artifact, environment.ActiveArtifactId,
                    payload.RollbackTargetRunId));
            }
            catch
            {
                adapterResult = new L12ReleaseAdapterExecutionResult(false,
                    new L12ReleaseProbeResult(false, "adapter-exception", 0),
                    new L12ReleaseProbeResult(false, "adapter-exception", 0), false, false,
                    "adapter-exception");
            }
            adapterResult = SafeExecutionResult(adapterResult);
            var succeeded = adapterResult.ActivationSucceeded && adapterResult.Health.Success
                && adapterResult.WebSocket.Success;
            var rolledBack = !succeeded && adapterResult.RollbackAttempted && adapterResult.RollbackSucceeded;
            var previousArtifactId = environment.ActiveArtifactId;
            environment.Version++;
            environment.UpdatedAt = DateTimeOffset.UtcNow;
            if (succeeded)
            {
                environment.ActiveArtifactId = payload.Artifact.Id;
                environment.ActiveCommit = payload.Artifact.Commit;
                environment.ActiveReleaseSha256 = payload.Artifact.ReleaseSha256;
                environment.State = "healthy";
            }
            else if (rolledBack)
            {
                environment.State = "rolled-back";
            }
            else
            {
                if (adapterResult.ActivationSucceeded)
                {
                    environment.ActiveArtifactId = payload.Artifact.Id;
                    environment.ActiveCommit = payload.Artifact.Commit;
                    environment.ActiveReleaseSha256 = payload.Artifact.ReleaseSha256;
                }
                environment.State = "degraded";
            }
            var checkedAt = DateTimeOffset.UtcNow;
            var run = new ReleaseRunRow
            {
                CommandId = context.CommandId ?? string.Empty,
                Action = payload.Action,
                Environment = payload.Environment,
                ArtifactId = payload.Artifact.Id,
                Commit = payload.Artifact.Commit,
                ReleaseSha256 = payload.Artifact.ReleaseSha256,
                CardsHash = payload.Artifact.CardsHash,
                Status = succeeded ? "succeeded" : rolledBack ? "rolled-back" : "failed",
                ActorId = actor.Id,
                ActorName = actor.Username,
                PreviousArtifactId = previousArtifactId,
                RollbackTargetRunId = payload.RollbackTargetRunId,
                RollbackAttempted = adapterResult.RollbackAttempted,
                RollbackSucceeded = adapterResult.RollbackSucceeded,
                ResultCode = adapterResult.Code,
                EnvironmentVersion = environment.Version,
                StartedAt = startedAt,
                CompletedAt = checkedAt,
                Checks =
                [
                    new ReleaseCheckRow { Kind = "artifact-hash", Success = true, Code = "verified", CheckedAt = startedAt },
                    new ReleaseCheckRow { Kind = "activation", Success = adapterResult.ActivationSucceeded,
                        Code = adapterResult.ActivationSucceeded ? "activated" : adapterResult.Code, CheckedAt = checkedAt },
                    Check("health", adapterResult.Health, checkedAt),
                    Check("websocket-smoke", adapterResult.WebSocket, checkedAt),
                    new ReleaseCheckRow { Kind = "rollback", Success = !adapterResult.RollbackAttempted || adapterResult.RollbackSucceeded,
                        Code = adapterResult.RollbackAttempted
                            ? adapterResult.RollbackSucceeded ? "rollback-succeeded" : "rollback-failed"
                            : "not-required", CheckedAt = checkedAt },
                ],
            };
            _data.ReleaseRuns.Add(run);
            environment.LastRunId = run.Id;
            AddAdminAudit(actor, "release", payload.Action, payload.Environment, previousArtifactId,
                environment.ActiveArtifactId, run.ResultCode, context with { Outcome = run.Status });
            Save();
            return new L12ReleaseOperationView(true, plan, ToView(run));
        }
    }

    internal long ReleaseEnvironmentVersion(string environment)
    {
        lock (_gate)
        {
            var normalized = RequireReleaseEnvironment(environment);
            return _data.ReleaseEnvironments.FirstOrDefault(item => item.Environment == normalized)?.Version ?? 0;
        }
    }

    internal static bool CanReviewReleaseCommand(L12AdminCommandView command, L12AccountView reviewer)
        => command.Type.StartsWith("release.", StringComparison.Ordinal)
           && ReleaseEnvironmentFromScope(command.Scope) is not null
           && L12Authorization.HasPermission(reviewer, L12Permission.ReleaseApprovalsReview);

    private L12ReleasePlanView BuildPlan(L12ReleaseCommandPayload payload, long expectedVersion, bool willExecute)
    {
        var current = _data.ReleaseEnvironments.FirstOrDefault(item => item.Environment == payload.Environment);
        return new L12ReleasePlanView(payload.Action, payload.Environment, expectedVersion,
            current?.ActiveArtifactId, payload.Artifact, payload.RollbackTargetRunId,
            ["verify-artifact-hash", "activate-declarative-adapter", "health-check", "websocket-smoke", "rollback-on-failure"],
            willExecute);
    }

    private void ValidateReleasePayload(L12ReleaseCommandPayload payload, long expectedVersion,
        IL12ReleaseControlAdapter adapter)
    {
        if (payload.Action is not ("deploy" or "rollback")) throw new L12ReleaseScopeException("发布动作不受支持");
        var environment = RequireReleaseEnvironment(payload.Environment);
        var version = _data.ReleaseEnvironments.FirstOrDefault(item => item.Environment == environment)?.Version ?? 0;
        if (version != expectedVersion) throw new L12ReleaseVersionConflictException("发布环境版本已变化，请刷新后重试");
        var current = RequireVerifiedArtifact(adapter, payload.Artifact.Id, environment);
        if (!ArtifactEquals(current, payload.Artifact))
            throw new L12ReleaseArtifactException("artifact-snapshot-mismatch", "已验证工件快照已变化");
        if (payload.Action == "rollback" && string.IsNullOrWhiteSpace(payload.RollbackTargetRunId))
            throw new L12ReleaseScopeException("回滚命令缺少目标发布记录");
    }

    private L12VerifiedReleaseArtifactView RequireVerifiedArtifact(IL12ReleaseControlAdapter adapter,
        string artifactId, string environment)
    {
        var artifact = ValidArtifacts(adapter).FirstOrDefault(item => item.Id == artifactId)
            ?? throw new L12ReleaseArtifactException("artifact-not-verified", "工件不存在或未通过完整验证");
        if (!artifact.Environments.Contains(environment, StringComparer.OrdinalIgnoreCase))
            throw new L12ReleaseScopeException("该工件未获准用于目标环境");
        bool verified;
        try { verified = adapter.VerifyArtifactHash(artifact.Id); }
        catch { verified = false; }
        if (!verified) throw new L12ReleaseArtifactException("artifact-hash-mismatch", "工件哈希复核失败");
        return artifact;
    }

    private static IReadOnlyList<L12VerifiedReleaseArtifactView> ValidArtifacts(IL12ReleaseControlAdapter adapter)
    {
        IReadOnlyList<L12VerifiedReleaseArtifactView> artifacts;
        try { artifacts = adapter.VerifiedArtifacts ?? []; }
        catch { return []; }
        return artifacts.Where(IsValidArtifact)
            .GroupBy(item => item.Id, StringComparer.Ordinal).Where(group => group.Count() == 1)
            .Select(group => group.Single() with
            {
                VerificationGates = group.Single().VerificationGates.Where(IsSafeGate).Distinct(StringComparer.Ordinal).ToArray(),
                Environments = group.Single().Environments.Where(IsReleaseEnvironment).Distinct(StringComparer.Ordinal).ToArray(),
            }).Where(item => item.VerificationGates.Count > 0 && item.Environments.Count > 0).ToArray();
    }

    private static bool IsValidArtifact(L12VerifiedReleaseArtifactView artifact)
        => ReleaseIdPattern.IsMatch(artifact.Id) && CommitPattern.IsMatch(artifact.Commit)
           && Sha256Pattern.IsMatch(artifact.ReleaseSha256)
           && (string.IsNullOrWhiteSpace(artifact.CardsHash) || artifact.CardsHash.Length is >= 40 and <= 64
               && artifact.CardsHash.All(Uri.IsHexDigit))
           && (string.IsNullOrWhiteSpace(artifact.CardsSha256) || Sha256Pattern.IsMatch(artifact.CardsSha256));

    private static bool ArtifactEquals(L12VerifiedReleaseArtifactView left, L12VerifiedReleaseArtifactView right)
        => left.Id == right.Id && string.Equals(left.Commit, right.Commit, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.ReleaseSha256, right.ReleaseSha256, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.CardsHash, right.CardsHash, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.CardsSha256, right.CardsSha256, StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeGate(string gate)
        => !string.IsNullOrWhiteSpace(gate) && gate.Length <= 64
           && gate.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');

    private static string RequireReleaseEnvironment(string environment)
    {
        var normalized = environment?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!IsReleaseEnvironment(normalized)) throw new L12ReleaseScopeException("发布环境必须为 staging 或 production");
        return normalized;
    }

    private static bool IsReleaseEnvironment(string? environment)
        => environment is not null && ReleaseEnvironmentsAllowed.Contains(environment, StringComparer.OrdinalIgnoreCase);

    private static string? ReleaseEnvironmentFromScope(string scope)
    {
        const string prefix = "release:";
        if (!scope.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var environment = scope[prefix.Length..];
        return IsReleaseEnvironment(environment) ? environment.ToLowerInvariant() : null;
    }

    private ReleaseEnvironmentRow GetOrCreateReleaseEnvironment(string environment)
    {
        var normalized = RequireReleaseEnvironment(environment);
        var row = _data.ReleaseEnvironments.FirstOrDefault(item => item.Environment == normalized);
        if (row is not null) return row;
        row = new ReleaseEnvironmentRow { Environment = normalized };
        _data.ReleaseEnvironments.Add(row);
        return row;
    }

    private static ReleaseCheckRow Check(string kind, L12ReleaseProbeResult result, DateTimeOffset checkedAt)
        => new() { Kind = kind, Success = result.Success, Code = SafeCode(result.Code),
            DurationMs = Math.Clamp(result.DurationMs, 0, 300_000), CheckedAt = checkedAt };

    private static L12ReleaseAdapterExecutionResult SafeExecutionResult(L12ReleaseAdapterExecutionResult result)
        => result with
        {
            Health = SafeProbe(result.Health, "health-invalid"),
            WebSocket = SafeProbe(result.WebSocket, "ws-invalid"),
            Code = SafeCode(result.Code),
        };

    private static L12ReleaseProbeResult SafeProbe(L12ReleaseProbeResult? result, string fallback)
        => result is null ? new(false, fallback, 0) : new(result.Success, SafeCode(result.Code),
            Math.Clamp(result.DurationMs, 0, 300_000));

    private static string SafeCode(string? code)
        => code is not null && AdapterCodePattern.IsMatch(code) ? code : "adapter-invalid-code";

    private static string SafeState(string? state, string fallback)
        => state is "idle" or "healthy" or "degraded" or "rolled-back" or "unconfigured" ? state : fallback;

    private static string? SafeIdentifier(string? value)
        => value is not null && ReleaseIdPattern.IsMatch(value) ? value : null;

    private static string? SafeCommit(string? value)
        => value is not null && CommitPattern.IsMatch(value) ? value.ToLowerInvariant() : null;

    private static L12ReleaseRunView ToView(ReleaseRunRow row)
        => new(row.Id, row.CommandId, row.Action, row.Environment, row.ArtifactId, row.Commit,
            row.ReleaseSha256, row.CardsHash, row.Status, row.ActorId, row.ActorName, row.PreviousArtifactId,
            row.RollbackTargetRunId, row.RollbackAttempted, row.RollbackSucceeded, row.ResultCode,
            row.Checks.Select(item => new L12ReleaseCheckView(item.Kind, item.Success, item.Code,
                item.DurationMs, item.CheckedAt)).ToArray(), row.EnvironmentVersion, row.StartedAt, row.CompletedAt);

    private static void NormalizeReleaseEnvironment(ReleaseEnvironmentRow row)
    {
        row.Environment = row.Environment?.Trim().ToLowerInvariant() ?? string.Empty;
        row.State = SafeState(row.State, "idle");
        row.ActiveArtifactId = SafeIdentifier(row.ActiveArtifactId);
        row.ActiveCommit = SafeCommit(row.ActiveCommit);
        if (row.Version < 0) row.Version = 0;
    }

    private static void NormalizeReleaseRun(ReleaseRunRow row)
    {
        row.Checks ??= [];
        row.ResultCode = SafeCode(row.ResultCode);
        foreach (var check in row.Checks)
        {
            check.Code = SafeCode(check.Code);
            check.DurationMs = Math.Clamp(check.DurationMs, 0, 300_000);
        }
    }
}
