namespace TwelveLegions.Server;

public sealed record L12RankedClockPlayerView(
    int PlayerIndex,
    long TotalRemainingMs,
    long OperationRemainingMs,
    bool Acting,
    bool Connected,
    long? ReconnectRemainingMs);

public sealed record L12RankedClockView(
    long ServerUtcMs,
    long TotalLimitMs,
    long OperationLimitMs,
    long ReconnectLimitMs,
    IReadOnlyList<L12RankedClockPlayerView> Players);

public sealed partial class L12RoomManager
{
    internal static readonly TimeSpan RankedTotalTime = TimeSpan.FromMinutes(25);
    internal static readonly TimeSpan RankedOperationTime = TimeSpan.FromMinutes(4);
    internal static readonly TimeSpan RankedReconnectTime = TimeSpan.FromMinutes(4);

    private sealed class RankedClockState
    {
        public long[] TotalRemainingMs { get; } = [
            (long)RankedTotalTime.TotalMilliseconds,
            (long)RankedTotalTime.TotalMilliseconds,
        ];
        public long[] OperationRemainingMs { get; } = [
            (long)RankedOperationTime.TotalMilliseconds,
            (long)RankedOperationTime.TotalMilliseconds,
        ];
        public bool[] Acting { get; } = [false, false];
        public DateTimeOffset LastSettledAt { get; set; }
        public string? ConclusionKind { get; set; }
        public bool AuthorityEventRecorded { get; set; }
    }

    private void InitializeRankedClock(Room room)
    {
        room.StartedAt = _utcNow();
        room.MeaningfulCommandCount = 0;
        room.CompletionRecorded = false;
        room.RankedResultReported = false;
        if (!string.Equals(room.Options.MatchModeId, "ranked", StringComparison.OrdinalIgnoreCase))
        {
            room.RankedClock = null;
            return;
        }
        room.RankedClock = new RankedClockState { LastSettledAt = room.StartedAt };
        RefreshRankedClockActorsLocked(room, room.StartedAt);
    }

    private void SettleRankedClockLocked(Room room, DateTimeOffset now)
    {
        if (room.RankedClock is not { } clock || room.Game?.State.Phase == L12Phase.GameOver) return;
        if (now <= clock.LastSettledAt) return;
        var elapsed = (long)(now - clock.LastSettledAt).TotalMilliseconds;
        for (var index = 0; index < 2; index++)
        {
            if (!clock.Acting[index] || !PlayerConnected(room, index)) continue;
            clock.TotalRemainingMs[index] = Math.Max(0, clock.TotalRemainingMs[index] - elapsed);
            clock.OperationRemainingMs[index] = Math.Max(0, clock.OperationRemainingMs[index] - elapsed);
        }
        clock.LastSettledAt = now;
    }

    private void RefreshRankedClockActorsLocked(Room room, DateTimeOffset now, int? completedActor = null)
    {
        if (room.RankedClock is not { } clock || room.Game is null) return;
        SettleRankedClockLocked(room, now);
        var required = RequiredDecisionPlayers(room.Game);
        for (var index = 0; index < 2; index++)
        {
            var wasActing = clock.Acting[index];
            clock.Acting[index] = required.Contains(index);
            if (clock.Acting[index] && (!wasActing || completedActor == index))
                clock.OperationRemainingMs[index] = (long)RankedOperationTime.TotalMilliseconds;
        }
        if (now > clock.LastSettledAt) clock.LastSettledAt = now;
    }

    private static HashSet<int> RequiredDecisionPlayers(L12GameEngine game)
    {
        var state = game.State;
        if (state.Phase == L12Phase.GameOver) return [];
        if (state.PendingPrompts.Count > 0)
            return state.PendingPrompts.Select(prompt => prompt.PlayerIndex).ToHashSet();
        if (state.ResponseWindow is { } response) return [response.PriorityPlayer];
        if (state.Phase == L12Phase.Mulligan)
            return state.Players.Where(player => !player.MulliganDone).Select(player => player.PlayerIndex).ToHashSet();
        if (state.PendingDefense is { Stage: L12CombatStage.DefenseChoice } defense)
            return [1 - defense.AttackerPlayer];
        return state.Phase == L12Phase.Main ? [state.ActivePlayer] : [];
    }

    private bool PlayerConnected(Room room, int playerIndex)
        => PlayerSession(room, playerIndex)?.Connected == true;

    private Session? PlayerSession(Room room, int playerIndex)
        => room.Sessions.Select(id => _sessions.TryGetValue(id, out var session) ? session : null)
            .FirstOrDefault(session => session?.PlayerIndex == playerIndex);

    private async Task<bool> ApplyRankedClockConclusionLockedAsync(Room room, DateTimeOffset now)
    {
        if (room.RankedClock is not { } clock || room.Game is null) return false;
        var concluded = false;
        if (room.Game.State.Phase != L12Phase.GameOver)
        {
            SettleRankedClockLocked(room, now);
            var disconnectedExpired = Enumerable.Range(0, 2).Select(index =>
            {
                var session = PlayerSession(room, index);
                return session is { Connected: false, DisconnectedAt: not null }
                    && now - session.DisconnectedAt.Value >= RankedReconnectTime;
            }).ToArray();
            if (disconnectedExpired[0] && disconnectedExpired[1])
            {
                clock.ConclusionKind = "both-disconnect-timeout";
                room.Game.ConcludeByAuthority(null, "双方掉线超过4分钟且均未能恢复，对局无效");
                concluded = true;
            }
            else if (disconnectedExpired[0] || disconnectedExpired[1])
            {
                var loser = disconnectedExpired[0] ? 0 : 1;
                clock.ConclusionKind = "disconnect-timeout";
                room.Game.ConcludeByAuthority(1 - loser,
                    $"{room.Game.State.Players[loser].Name}掉线超过4分钟未能重连");
                concluded = true;
            }
            else
            {
                var totalExpired = Enumerable.Range(0, 2)
                    .Where(index => clock.TotalRemainingMs[index] <= 0).ToArray();
                var operationExpired = Enumerable.Range(0, 2).Where(index => clock.Acting[index]
                    && PlayerConnected(room, index) && clock.OperationRemainingMs[index] <= 0).ToArray();
                var expired = totalExpired.Length > 0 ? totalExpired : operationExpired;
                if (expired.Length > 0)
                {
                    if (expired.Length == 2)
                    {
                        clock.ConclusionKind = totalExpired.Length > 0
                            ? "both-total-timeout" : "both-operation-timeout";
                        room.Game.ConcludeByAuthority(null, totalExpired.Length > 0
                            ? "双方25分钟总操作时间同时耗尽，对局无效"
                            : "双方单次操作时间同时超过4分钟，对局无效");
                    }
                    else
                    {
                        var loser = expired[0];
                        clock.ConclusionKind = totalExpired.Length > 0 ? "total-timeout" : "operation-timeout";
                        var reason = totalExpired.Length > 0 ? "25分钟总操作时间耗尽" : "单次操作超过4分钟";
                        room.Game.ConcludeByAuthority(1 - loser, $"{room.Game.State.Players[loser].Name}{reason}");
                    }
                    concluded = true;
                }
            }
        }
        if (room.Game.State.Phase != L12Phase.GameOver) return false;
        if (clock.ConclusionKind is not null && !clock.AuthorityEventRecorded)
        {
            try
            {
                room.CommandSequence++;
                await _recorder.AppendAuthorityAsync(room.Game, room.CommandSequence,
                    room.Game.State.WinnerReason ?? "排位权威裁决");
                clock.AuthorityEventRecorded = true;
            }
            catch (Exception error)
            {
                // 权威状态保持结束，周期看门狗会重试持久化；不得继续结算形成部分写入。
                room.CommandSequence--;
                Console.Error.WriteLine($"Ranked authority event persistence ({room.Code}): {error.Message}");
                return concluded;
            }
        }
        await CompleteTournamentRoomGameAsync(room);
        return concluded;
    }

    public async Task<IReadOnlyList<OutgoingMessage>> TickRankedClocksAsync(DateTimeOffset? utcNow = null)
    {
        var messages = new List<OutgoingMessage>();
        foreach (var room in _rooms.Values.Where(room => room.RankedClock is not null && room.Game is not null
                     && (room.Game.State.Phase != L12Phase.GameOver || !room.CompletionRecorded
                         || !room.RankedResultReported)).ToArray())
        {
            await room.Gate.WaitAsync();
            try
            {
                if (await ApplyRankedClockConclusionLockedAsync(room, utcNow ?? _utcNow()))
                    messages.AddRange(BroadcastGame(room));
            }
            catch (Exception error)
            {
                // 一个房间的暂时性存储故障不能阻止其他排位房间的权威计时。
                Console.Error.WriteLine($"Ranked clock room ({room.Code}): {error.Message}");
            }
            finally { room.Gate.Release(); }
        }
        return messages;
    }

    private L12RankedClockView? RankedClockView(Room room)
    {
        if (room.RankedClock is not { } clock) return null;
        var now = _utcNow();
        SettleRankedClockLocked(room, now);
        var players = Enumerable.Range(0, 2).Select(index =>
        {
            var session = PlayerSession(room, index);
            long? reconnect = session is { Connected: false, DisconnectedAt: not null }
                ? Math.Max(0L, (long)(RankedReconnectTime - (now - session.DisconnectedAt.Value)).TotalMilliseconds)
                : null;
            return new L12RankedClockPlayerView(index, clock.TotalRemainingMs[index],
                clock.OperationRemainingMs[index], clock.Acting[index], session?.Connected == true, reconnect);
        }).ToArray();
        return new L12RankedClockView(now.ToUnixTimeMilliseconds(), (long)RankedTotalTime.TotalMilliseconds,
            (long)RankedOperationTime.TotalMilliseconds, (long)RankedReconnectTime.TotalMilliseconds, players);
    }
}
