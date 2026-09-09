namespace TwelveLegions.Server;

public sealed partial class L12RoomManager
{
    internal static readonly TimeSpan SandboxReconnectGrace = TimeSpan.FromMinutes(5);

    internal async Task<L12SandboxReplayCleanupResult> RunSandboxReplayMaintenanceAsync(
        CancellationToken cancellationToken = default)
    {
        var now = _utcNow();
        await _sessionRecoveryGate.WaitAsync(cancellationToken);
        try
        {
            var staleRooms = _rooms.Values.Where(room => room.IsSandbox && room.Game is not null
                && room.Game.State.Phase != L12Phase.GameOver
                && room.GmControllerSessionId is { } controllerId
                && _sessions.TryGetValue(controllerId, out var controller)
                && !controller.Connected && controller.DisconnectedAt is { } disconnectedAt
                && now - disconnectedAt >= SandboxReconnectGrace).ToArray();
            foreach (var room in staleRooms)
            {
                await room.Gate.WaitAsync(cancellationToken);
                try
                {
                    Guid[] spectators;
                    lock (room.Spectators) spectators = [.. room.Spectators];
                    if (room.Game?.State.Phase == L12Phase.GameOver
                        || room.GmControllerSessionId is not { } controllerId
                        || !_sessions.TryGetValue(controllerId, out var controller)
                        || controller.Connected || controller.DisconnectedAt is not { } disconnectedAt
                        || now - disconnectedAt < SandboxReconnectGrace
                        || room.Sessions.Concat(spectators).Any(id => _sessions.TryGetValue(id, out var member)
                                                                  && !member.IsVirtual && member.Connected))
                        continue;
                    if (!room.CompletionRecorded)
                    {
                        if (!await _recorder.AbandonSandboxAsync(room.Game!,
                                "沙盒控制者断线超过重连宽限"))
                            continue;
                        room.CompletionRecorded = true;
                    }
                    room.Closed = true;
                    _rooms.TryRemove(room.Code, out _);
                    lock (room.Spectators) room.Spectators.Clear();
                    foreach (var sessionId in room.Sessions.Concat(spectators).Distinct().ToArray())
                    {
                        if (!_sessions.TryGetValue(sessionId, out var member)) continue;
                        ClearRoomMembership(member);
                        if (member.IsVirtual || !member.Connected) _sessions.TryRemove(sessionId, out _);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    // 持久化失败时保留内存房间和 active 元数据，不先删房再丢录像。
                    Console.Error.WriteLine($"Sandbox disconnect retirement ({room.Code}): {error.Message}");
                }
                finally
                {
                    room.Gate.Release();
                }
            }
        }
        finally
        {
            _sessionRecoveryGate.Release();
        }

        // 断线沙盒退场仍及时执行；磁盘清理只进入持久化的每日低峰窗口。
        var result = new L12SandboxReplayCleanupResult(false, 0, null, _recorder.NextStorageCleanupUtc(now));
        await _recorder.RunDailyStorageMaintenanceIfDueAsync(async token =>
        {
            result = await RunScheduledStorageCleanupAsync(now, token);
        }, now, cancellationToken);
        return result;
    }

    private async Task<L12SandboxReplayCleanupResult> RunScheduledStorageCleanupAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var replayCleanup = new L12SandboxReplayCleanupResult(false, 0, null, _recorder.NextStorageCleanupUtc(now));
        var retentionEnvironment = Environment.GetEnvironmentVariable("L12_STORAGE_RETENTION_ENVIRONMENT");
        async Task Sandbox(CancellationToken token)
        {
            // Test environment uses its independently validated 14-day payload policy below.
            if (retentionEnvironment == "testrun") return;
            var activeSandboxMatches = _rooms.Values.Where(room => room.IsSandbox && room.Game is not null)
                .Select(room => room.Game!.State.MatchId).ToArray();
            replayCleanup = await _recorder.RunSandboxReplayCleanupIfDueAsync(
                activeSandboxMatches, now, token, _platform is null ? null : _platform.UnresolvedReplayEvidence);
        }
        async Task PlayerReplay(CancellationToken token)
        {
            // Daily payload retention is independent of the weekly sandbox schedule.
            // Keep every in-memory match as well as unresolved Bug evidence out of the purge.
            var activeMatches = _rooms.Values.Where(room => room.Game is not null)
                .Select(room => room.Game!.State.MatchId).ToArray();
            if (retentionEnvironment == "testrun")
                await _recorder.RunTestRunReplayRetentionAsync(retentionEnvironment,
                    Environment.GetEnvironmentVariable("L12_PUBLIC_BASE_URL") ?? "",
                    MatchRecorder.TestRunRuntimePath, MatchRecorder.ProductionRuntimePath,
                    activeMatchIds: activeMatches, utcNow: now, cancellationToken: token,
                    evidenceProvider: _platform is null ? null : _platform.UnresolvedReplayEvidence);
            else
                await _recorder.RunPlayerReplayCleanupIfDueAsync(activeMatchIds: activeMatches,
                    utcNow: now, cancellationToken: token,
                    evidenceProvider: _platform is null ? null : _platform.UnresolvedReplayEvidence);
        }
        async Task Audit(CancellationToken token)
        {
            if (_platform is null) return;
            var holds = await _recorder.ReadAuditRetentionHoldsAsync(token);
            var active = _rooms.Values.Where(room => room.Game is not null).Select(room => room.Game!.State.MatchId);
            _platform.RunAuditLifecycle(now, holds.Concat(active).Distinct().ToArray(), token);
        }
        await MatchRecorder.RunStorageSlicesAsync(now,
            [("Sandbox", Sandbox), ("Card analytics", token => _recorder.RunCardFactStorageMaintenanceIfDueAsync(now,token)),
             ("Player replay", PlayerReplay), ("Audit lifecycle", Audit)], cancellationToken);
        return replayCleanup;
    }
}
