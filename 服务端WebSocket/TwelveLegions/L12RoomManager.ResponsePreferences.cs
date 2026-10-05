using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class L12RoomManager
{
    private sealed record EffectiveResponsePreference(string Mode, bool SyncPending);
    private readonly SemaphoreSlim _responsePreferenceOutboxGate = new(1, 1);

    private async Task<EffectiveResponsePreference> ResolveResponsePreferenceAsync(string? accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId) || _platform is null)
            return new(L12GameEngine.DefaultResponseMode, false);
        var pending = await _recorder.LatestPendingResponsePreferenceAsync(accountId);
        return pending is not null
            ? new(pending.TargetMode, true)
            : new(_platform.ResponsePreference(accountId), false);
    }

    private async Task<EffectiveResponsePreference[]> ResolveResponsePreferencesAsync(IEnumerable<Session> sessions)
    {
        var result = new List<EffectiveResponsePreference>();
        foreach (var session in sessions) result.Add(await ResolveResponsePreferenceAsync(session.AccountId));
        return result.ToArray();
    }

    private async Task RefreshRoomResponsePreferencesAsync(Room room)
    {
        foreach (var sessionId in room.Sessions)
        {
            if (!_sessions.TryGetValue(sessionId, out var session) || session.PlayerIndex is null) continue;
            var effective = await ResolveResponsePreferenceAsync(session.AccountId);
            room.ResponsePreferenceSyncPending[session.PlayerIndex.Value] = effective.SyncPending;
        }
    }

    public async Task<IReadOnlyList<OutgoingMessage>> GetResponsePreferenceAsync(Guid sessionId)
    {
        if (!TryGetMembership(sessionId, out var session, out var room, out var error)
            || session.IsSpectator || session.PlayerIndex is null || room.Game is null)
            return Error(sessionId, error ?? "响应设置只对参战玩家开放", "responsePreferenceRejected");
        await room.Gate.WaitAsync();
        try
        {
            var player = session.PlayerIndex.Value;
            var effective = await ResolveResponsePreferenceAsync(session.AccountId);
            room.ResponsePreferenceSyncPending[player] = effective.SyncPending;
            return [ResponsePreferenceState(sessionId, room, player)];
        }
        finally { room.Gate.Release(); }
    }

    public async Task<IReadOnlyList<OutgoingMessage>> SetResponsePreferenceAsync(Guid sessionId,
        string? mode, string? requestId)
    {
        if (!TryGetMembership(sessionId, out var session, out var room, out var error)
            || session.IsSpectator || session.PlayerIndex is null || room.Game is null
            || string.IsNullOrWhiteSpace(session.AccountId))
            return Error(sessionId, error ?? "响应设置只对参战账号开放", "responsePreferenceRejected", requestId);
        if (!L12GameEngine.IsValidResponseMode(mode))
            return Error(sessionId, "响应设置无效", "responsePreferenceRejected", requestId);

        await room.Gate.WaitAsync();
        try
        {
            var player = session.PlayerIndex.Value;
            var oldMode = room.Game.ResponseModeFor(player);
            if (oldMode == mode)
            {
                var synced = !room.ResponsePreferenceSyncPending[player]
                    || await DrainResponsePreferenceOutboxAsync(session.AccountId);
                room.ResponsePreferenceSyncPending[player] = !synced;
                return [ResponsePreferenceResult(sessionId, true, synced, oldMode, !synced, requestId)];
            }

            var oldModes = room.Game.State.PlayerResponseModes is null
                ? null : (string[])room.Game.State.PlayerResponseModes.Clone();
            var oldRevision = room.Game.State.Revision;
            var oldSequence = room.CommandSequence;
            var result = room.Game.ApplyResponsePreference(player, mode);
            room.CommandSequence++;
            var operationId = string.IsNullOrWhiteSpace(requestId)
                ? $"response-pref-{Guid.NewGuid():N}" : $"response-pref-{requestId}";
            var commandJson = JsonSerializer.Serialize(new
            {
                type = "setResponsePreference", responseMode = mode, operationId,
            });
            var envelope = new L12ResponsePreferenceOutboxEnvelope(operationId,
                room.Game.State.MatchId, room.CommandSequence, session.AccountId!, player, mode!, _utcNow());
            try
            {
                await _recorder.AppendResponsePreferenceAsync(room.Game, room.CommandSequence, player,
                    commandJson, result, envelope,
                    room.RankedClock is null ? null : CaptureRankedRuntime(room, _utcNow()), requestId);
            }
            catch
            {
                room.Game.RestoreResponsePreference(oldModes, oldRevision);
                room.CommandSequence = oldSequence;
                return [ResponsePreferenceResult(sessionId, false, false, oldMode,
                    room.ResponsePreferenceSyncPending[player], requestId)];
            }

            var accountSynced = await DrainResponsePreferenceOutboxAsync(session.AccountId);
            room.ResponsePreferenceSyncPending[player] = !accountSynced;
            var messages = BroadcastGame(room, forceCritical: true).ToList();
            messages.Add(ResponsePreferenceResult(sessionId, true, accountSynced,
                room.Game.ResponseModeFor(player), !accountSynced, requestId));
            return messages;
        }
        finally { room.Gate.Release(); }
    }

    private static OutgoingMessage ResponsePreferenceResult(Guid sessionId, bool matchApplied,
        bool accountSynced, string confirmedMode, bool syncPending, string? requestId)
        => new(sessionId, new
        {
            type = "responsePreferenceResult", matchApplied, accountSynced, confirmedMode, syncPending, requestId,
        });

    private static OutgoingMessage ResponsePreferenceState(Guid sessionId, Room room, int player)
        => new(sessionId, new
        {
            type = "responsePreferenceState", confirmedMode = room.Game!.ResponseModeFor(player),
            syncPending = room.ResponsePreferenceSyncPending[player],
        });

    private async Task<bool> DrainResponsePreferenceOutboxAsync(string? accountId = null)
    {
        if (_platform is null) return true;
        await _responsePreferenceOutboxGate.WaitAsync();
        try
        {
            foreach (var item in await _recorder.ListPendingResponsePreferencesAsync(accountId))
            {
                if (item.Payload is null || item.LoadError is not null)
                {
                    await _recorder.RecordResponsePreferenceFailureAsync(item.Id,
                        item.LoadError ?? "响应设置 outbox 载荷为空");
                    return false;
                }
                try
                {
                    _platform.ApplyResponsePreference(item.Payload.AccountId,
                        item.Payload.TargetMode, item.Payload.OperationId);
                    await _recorder.MarkResponsePreferenceAppliedAsync(item.Id, item.PayloadHash);
                }
                catch (Exception exception)
                {
                    try { await _recorder.RecordResponsePreferenceFailureAsync(item.Id, exception.Message); }
                    catch { }
                    return false;
                }
            }
            return true;
        }
        finally { _responsePreferenceOutboxGate.Release(); }
    }

    private async Task<bool> ExpireResponseWindowLockedAsync(Room room, DateTimeOffset now)
    {
        var game = room.Game;
        var lease = game?.CaptureResponseAutoCloseLease();
        if (game is null || lease is null || now.ToUniversalTime() < lease.DeadlineUtc.ToUniversalTime())
            return false;
        var oldSequence = room.CommandSequence;
        if (!game.TryExpireResponseAutoClose(lease.PromptId, lease.StackItemId,
                lease.PriorityPlayer, lease.DeadlineUtc, now)) return false;
        room.CommandSequence++;
        var commandJson = JsonSerializer.Serialize(new
        {
            type = "responseAutoClose", promptId = lease.PromptId,
            stackItemId = lease.StackItemId, priorityPlayer = lease.PriorityPlayer,
            deadlineUtc = lease.DeadlineUtc, observedAtUtc = now,
        });
        try
        {
            if (room.RankedClock is not null)
                await _recorder.AppendRankedAsync(game, room.CommandSequence, -1, commandJson,
                    CommandResult.Ok(), CaptureRankedRuntime(room, now), null);
            else
                await _recorder.AppendAsync(game, room.CommandSequence, -1, commandJson, CommandResult.Ok());
            return true;
        }
        catch
        {
            room.CommandSequence = oldSequence;
            _ = room.RankedClock is not null
                ? await ReloadRankedRoomFromRecorderAsync(room)
                : await ReloadJournalRoomFromRecorderAsync(room);
            return false;
        }
    }

    public async Task<IReadOnlyList<OutgoingMessage>> TickResponseWindowsAsync(DateTimeOffset? utcNow = null)
    {
        var messages = new List<OutgoingMessage>();
        var now = utcNow ?? _utcNow();
        foreach (var room in _rooms.Values.ToArray())
        {
            if (!room.Gate.Wait(0)) continue;
            try
            {
                if (room.Closed || room.Game is null) continue;
                if (await ExpireResponseWindowLockedAsync(room, now))
                    messages.AddRange(BroadcastGame(room, forceCritical: true));
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Response window watchdog ({room.Code}): {exception.Message}");
            }
            finally { room.Gate.Release(); }
        }
        await DrainResponsePreferenceOutboxAsync();
        foreach (var room in _rooms.Values.ToArray())
        {
            if (!room.Gate.Wait(0)) continue;
            try
            {
                if (room.Closed || room.Game is null) continue;
                for (var player = 0; player < room.Sessions.Count; player++)
                {
                    if (!room.ResponsePreferenceSyncPending[player]) continue;
                    var session = _sessions[room.Sessions[player]];
                    if (await _recorder.LatestPendingResponsePreferenceAsync(session.AccountId ?? string.Empty)
                        is not null) continue;
                    room.ResponsePreferenceSyncPending[player] = false;
                    messages.Add(ResponsePreferenceState(session.Id, room, player));
                }
            }
            finally { room.Gate.Release(); }
        }
        return messages;
    }
}
