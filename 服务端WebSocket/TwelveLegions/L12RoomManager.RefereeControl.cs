using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwelveLegions.Server;

public sealed partial class L12RoomManager
{
    private static readonly JsonSerializerOptions GmRecordedCommandJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    // This capability is independent of sandbox control and is rechecked under the room gate.
    private async Task<IReadOnlyList<OutgoingMessage>> HandleRefereeGmActionAsync(
        Guid sessionId, JsonElement commandElement, string? requestId)
    {
        if (requestId is null) return Error(sessionId, "裁判操作必须携带请求标识", "actionRejected");
        if (!_sessions.TryGetValue(sessionId, out var observer) || !observer.Connected
            || !observer.IsSpectator || observer.AccountId is null
            || observer.RoomCode is null || !_rooms.TryGetValue(observer.RoomCode, out var room))
            return Error(sessionId, "裁判观战连接已失效", "actionRejected", requestId);
        await room.Gate.WaitAsync();
        try
        {
            if (room.Closed || room.Game is null || room.Game.State.Phase == L12Phase.GameOver
                || room.TournamentId is null || room.TournamentMatchId is null
                || !_sessions.TryGetValue(sessionId, out var current) || !ReferenceEquals(current, observer)
                || !observer.Connected || !observer.IsSpectator || observer.RoomCode != room.Code
                || !room.Spectators.Contains(sessionId))
                return Error(sessionId, "本桌当前不能进行裁判干预", "actionRejected", requestId);
            try
            {
                var access = _platform?.TournamentRoomAccess(observer.AccountId, room.TournamentId,
                    room.TournamentMatchId, spectate: true);
                if (access is null || !access.CanRefereeView || access.CanPlay || access.RoomCode != room.Code)
                    return Error(sessionId, "仅非本桌参赛者的赛事裁判可以调整场面", "actionRejected", requestId);
            }
            catch (Exception error) when (error is L12TournamentScopeException
                or L12TournamentVersionConflictException or KeyNotFoundException or ArgumentException)
            {
                return Error(sessionId, error.Message, "actionRejected", requestId);
            }
            var storedRequestId = $"referee:{observer.AccountId}:{requestId}";
            if (room.TryGetProcessedActionRequest(-1, storedRequestId, out var duplicate))
                return duplicate.Accepted
                    ? BroadcastGame(room, forceCritical: true, requestSessionId: sessionId,
                        requestId: requestId, allowCriticalDelta: true)
                    : Error(sessionId, duplicate.Error ?? "裁判操作被拒绝", "actionRejected", requestId);
            if (room.RankedClock is not { Paused: true })
                return Error(sessionId, "请先暂停本桌，再调整场面", "actionRejected", requestId);
            if (commandElement.TryGetProperty("refereeMatchId", out var boundMatch)
                && (boundMatch.ValueKind != JsonValueKind.String || boundMatch.GetString() != room.Game.State.MatchId))
                return Error(sessionId, "裁判调整不属于当前对局", "actionRejected", requestId);
            L12GmCommand? command;
            try { command = commandElement.Deserialize<L12GmCommand>(new JsonSerializerOptions
                { PropertyNameCaseInsensitive = true }); }
            catch (JsonException) { return Error(sessionId, "GM 操作格式错误", "actionRejected", requestId); }
            if (command is null || string.IsNullOrWhiteSpace(command.Type))
                return Error(sessionId, "缺少 GM 操作类型", "actionRejected", requestId);
            var reason = command.RefereeReason?.Trim();
            if (string.IsNullOrEmpty(reason) || reason.Length > 500)
                return Error(sessionId, "请填写 1–500 字的裁判调整理由", "actionRejected", requestId);
            command = command with { RefereeName = observer.Name,
                RefereeAccountId = observer.AccountId, RefereeReason = reason };
            var revisionBefore = room.Game.State.Revision;
            var result = room.Game.HandleGm(command);
            room.CommandSequence++;
            try
            {
                RefreshRankedClockActorsLocked(room, _utcNow());
                await _recorder.AppendRankedAsync(room.Game, room.CommandSequence, -1,
                    JsonSerializer.Serialize(command, GmRecordedCommandJson), result, CaptureRankedRuntime(room, _utcNow()),
                    null, storedRequestId, !result.Accepted && room.Game.State.Revision != revisionBefore);
            }
            catch (Exception persistenceError)
            {
                if (!await ReloadRankedRoomFromRecorderAsync(room)) room.Closed = true;
                Console.Error.WriteLine($"Referee command persistence: {persistenceError.GetType().Name}");
                return Error(sessionId, room.Closed ? "裁判调整未能持久化，本桌已安全冻结"
                    : "裁判调整未能持久化，已恢复最后确认状态", "actionRejected", requestId);
            }
            room.RememberProcessedActionRequest(-1, storedRequestId, result.Accepted, result.Error,
                room.Game.State.Revision);
            if (!result.Accepted) return Error(sessionId, result.Error ?? "裁判操作被拒绝", "actionRejected", requestId);
            var messages = BroadcastGame(room, forceCritical: true, requestSessionId: sessionId,
                requestId: requestId).ToList();
            if (room.Game.State.Phase == L12Phase.GameOver)
            {
                var error = await CompleteTournamentRoomGameAsync(room);
                if (error is not null) messages.AddRange(Error(sessionId, error, "tournamentResultPending", requestId));
            }
            return messages;
        }
        finally { room.Gate.Release(); }
    }
}
