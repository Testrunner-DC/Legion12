namespace TwelveLegions.Server;

public sealed partial class L12WebSocketServer
{
    private static readonly TimeSpan SeasonActivationLeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SeasonActivationRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SeasonActivationMaximumSleep = TimeSpan.FromHours(1);
    private readonly string _seasonActivationWorkerId = $"season-worker:{Guid.NewGuid():N}";
    private readonly SemaphoreSlim _seasonActivationSignal = new(0, 1);
    private CancellationTokenSource? _seasonActivationCancellation;
    private Task? _seasonActivationTask;

    private void StartSeasonActivationCoordinator()
    {
        _seasonActivationCancellation = new CancellationTokenSource();
        _seasonActivationTask = RunSeasonActivationCoordinatorAsync(_seasonActivationCancellation.Token);
    }

    private async Task StopSeasonActivationCoordinatorAsync()
    {
        var cancellation = _seasonActivationCancellation;
        var task = _seasonActivationTask;
        _seasonActivationCancellation = null;
        _seasonActivationTask = null;
        if (cancellation is null) return;
        cancellation.Cancel();
        try
        {
            if (task is not null) await task;
        }
        catch (OperationCanceledException) { }
        finally { cancellation.Dispose(); }
    }

    private void ScheduleSeasonActivationEvaluation()
    {
        try { _seasonActivationSignal.Release(); }
        catch (SemaphoreFullException) { }
    }

    private async Task RunSeasonActivationCoordinatorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = _seasonActivationUtcNow();
            var wakeAt = _platform.NextSeasonActivationWakeAt(now, SeasonActivationRetryDelay);
            if (wakeAt is null)
            {
                await _seasonActivationSignal.WaitAsync(cancellationToken);
                continue;
            }

            var delay = wakeAt.Value - now;
            if (delay > TimeSpan.Zero)
            {
                var boundedDelay = delay > SeasonActivationMaximumSleep
                    ? SeasonActivationMaximumSleep : delay;
                if (await _seasonActivationSignal.WaitAsync(boundedDelay, cancellationToken)) continue;
            }

            var activated = false;
            try { activated = await RunSeasonActivationOnceAsync(_seasonActivationUtcNow()); }
            catch (Exception error)
            {
                Console.Error.WriteLine($"自动切季协调器失败，稍后重试: {error.Message}");
            }
            if (!activated)
                await _seasonActivationSignal.WaitAsync(SeasonActivationRetryDelay, cancellationToken);
        }
    }

    internal async Task<bool> RunSeasonActivationOnceAsync(DateTimeOffset now)
    {
        L12SeasonActivationClaim? claim;
        try
        {
            claim = _platform.TryClaimDueSeasonActivation(_seasonActivationWorkerId, now,
                SeasonActivationLeaseDuration);
        }
        catch (L12PlatformStorageUnavailableException error)
        {
            Console.Error.WriteLine($"自动切季租约持久化失败，稍后重试: {error.Message}");
            return false;
        }
        if (claim is null) return false;
        var actor = new L12AccountView("system:season-activation", "赛季自动切换", "admin",
            DateTimeOffset.UnixEpoch, false);
        var correlationId = $"season-auto-{claim.IntentKey[..16]}-{claim.Generation}";
        try
        {
            var result = await _rooms.ExecuteRankedSeasonCutoverAsync<L12SeasonActivationView?>(readiness =>
            {
                var commitNow = _seasonActivationUtcNow();
                if (!readiness.Ready)
                {
                    _platform.RecordSeasonActivationWaiting(claim, commitNow,
                        "season_cutover_not_ready", "当前赛季仍有未完成或未对账的排位对局");
                    return null;
                }
                return _platform.ActivateClaimedSeason(actor, claim, "按预约时间自动切换赛季",
                    readiness, commitNow, new L12AdminAuditContext(correlationId,
                        Permission: L12Authorization.Key(L12Permission.AdminOperationsWrite),
                        IdempotencyKey: claim.IntentKey, ExpectedVersion: claim.ExpectedOperationsVersion,
                        Reason: "按预约时间自动切换赛季", RequestMethod: "SYSTEM",
                        RequestPath: "/internal/seasons/automatic-activation"));
            });
            if (result is null) return false;
            try
            {
                NotifyOperationsPolicyChanged();
                NotifySeasonSummaryNotificationsChanged(
                    _platform.SeasonSummaryRecipients(result.Archive.SeasonId));
            }
            catch (Exception notificationError)
            {
                Console.Error.WriteLine($"自动切季已提交，但资源通知失败: {notificationError.Message}");
            }
            return true;
        }
        catch (L12PlatformStorageUnavailableException error)
        {
            Console.Error.WriteLine($"自动切季持久化失败，租约到期后重试: {error.Message}");
            return false;
        }
        catch (Exception error)
        {
            try
            {
                var code = error is L12OperationsConfigException operationsError
                    ? operationsError.Code : "season_activation_attempt_failed";
                _platform.RecordSeasonActivationWaiting(claim, _seasonActivationUtcNow(),
                    code, error.Message);
            }
            catch (Exception diagnosticError)
            {
                Console.Error.WriteLine($"自动切季失败且诊断状态写入失败: {diagnosticError.Message}");
            }
            return false;
        }
    }
}
