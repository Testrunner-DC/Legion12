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
                    if (room.GmControllerSessionId is not { } controllerId
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

        // 房间快照是第二道保护：数据库只选 completed/abandoned，这里另外
        // 保护仍在内存房间中的沙盒（包括已 GameOver 但 GM 尚未离开的房间）。
        var activeSandboxMatches = _rooms.Values
            .Where(candidate => candidate.IsSandbox && candidate.Game is not null)
            .Select(candidate => candidate.Game!.State.MatchId)
            .ToArray();
        return await _recorder.RunSandboxReplayCleanupIfDueAsync(activeSandboxMatches, now, cancellationToken);
    }
}
