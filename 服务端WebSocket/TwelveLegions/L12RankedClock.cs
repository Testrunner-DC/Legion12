using System.Text.Json;

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
    IReadOnlyList<L12RankedClockPlayerView> Players,
    L12RankedTimeControlConfig TimeControl);

public sealed partial class L12RoomManager
{
    private static readonly JsonSerializerOptions RankedSetupCommandJson = new(JsonSerializerDefaults.Web);

    private sealed class RankedClockState
    {
        public required L12RankedTimeControlConfig TimeControl { get; init; }
        public required long[] TotalRemainingMs { get; init; }
        public required long[] OperationRemainingMs { get; init; }
        public bool[] Acting { get; } = [false, false];
        public DateTimeOffset LastSettledAt { get; set; }
        public string? ConclusionKind { get; set; }
        public bool AuthorityEventRecorded { get; set; }
        public bool NormalClockStarted { get; set; }
        public bool RestoreBaselinePending { get; set; }
        public bool SetupBroadcastPending { get; set; }
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
        var timeControl = _platform?.RankedTimeControl() ?? L12PlatformStore.DefaultRankedTimeControl();
        room.RankedClock = new RankedClockState
        {
            TimeControl = timeControl,
            TotalRemainingMs = [timeControl.TotalTimeSeconds * 1000L, timeControl.TotalTimeSeconds * 1000L],
            OperationRemainingMs =
            [
                SetupDecisionLimit(room.Game!, timeControl).TotalMillisecondsAsLong(),
                SetupDecisionLimit(room.Game!, timeControl).TotalMillisecondsAsLong(),
            ],
            LastSettledAt = room.StartedAt,
        };
        RefreshRankedClockActorsLocked(room, room.StartedAt);
    }

    private L12RankedRuntimeCheckpoint CaptureRankedRuntime(Room room, DateTimeOffset now,
        string? status = null)
    {
        if (room.Game is null || room.RankedClock is not { } clock)
            throw new InvalidOperationException("排位运行快照缺少对局或计时状态");
        var players = Enumerable.Range(0, 2).Select(index => PlayerSession(room, index)
            ?? throw new InvalidOperationException("排位运行快照缺少玩家席位")).ToArray();
        var generation = checked(++room.RankedCheckpointGeneration);
        return new L12RankedRuntimeCheckpoint(1, room.Game.State.MatchId, room.Code,
            status ?? (room.Game.State.Phase == L12Phase.GameOver ? "completed" : "active"),
            generation, room.CommandSequence, room.Game.State.Revision, room.Game.ComputeStateHash(),
            room.StartedAt, room.MeaningfulCommandCount,
            [.. clock.TotalRemainingMs], [.. clock.OperationRemainingMs], [.. clock.Acting],
            clock.LastSettledAt, clock.ConclusionKind, clock.AuthorityEventRecorded,
            players.Select(player => player.Connected).ToArray(),
            players.Select(player => player.DisconnectedAt).ToArray(),
            players.Select(player => player.IntegrityClientKey).ToArray(),
            players.Select(player => player.ConnectionGeneration).ToArray(), now, clock.TimeControl);
    }

    private static void RestoreRankedClock(Room room, L12RankedRuntimeCheckpoint runtime)
    {
        var timeControl = L12PlatformStore.NormalizeRankedTimeControl(runtime.TimeControl);
        var clock = new RankedClockState
        {
            TimeControl = timeControl,
            TotalRemainingMs = [.. runtime.TotalRemainingMs],
            OperationRemainingMs = [.. runtime.OperationRemainingMs],
            LastSettledAt = runtime.LastSettledAt,
            ConclusionKind = runtime.ConclusionKind,
            AuthorityEventRecorded = runtime.AuthorityEventRecorded,
            NormalClockStarted = room.Game is not null && !IsRankedPreparationPhase(room.Game),
            // A durable checkpoint stores remaining time, not a wall-clock deadline. The first
            // settlement after reconstruction establishes a fresh process baseline so service
            // downtime is never charged and the persisted remainder is never reset to 60 seconds.
            RestoreBaselinePending = true,
        };
        Array.Copy(runtime.Acting, clock.Acting, 2);
        room.RankedClock = clock;
        room.RankedCheckpointGeneration = runtime.CheckpointGeneration;
    }

    private void SettleRankedClockLocked(Room room, DateTimeOffset now)
    {
        if (room.RankedClock is not { } clock || room.Game is not { } game
            || game.State.Phase == L12Phase.GameOver) return;
        if (clock.RestoreBaselinePending)
        {
            clock.RestoreBaselinePending = false;
            clock.LastSettledAt = now;
            return;
        }
        if (now <= clock.LastSettledAt) return;
        var elapsed = (long)(now - clock.LastSettledAt).TotalMilliseconds;
        var preparation = IsRankedPreparationPhase(game);
        for (var index = 0; index < 2; index++)
        {
            if (!clock.Acting[index]) continue;
            if (preparation)
            {
                if (!game.HasTimedRankedSetupDecision(index)) continue;
                // Preparation decisions are server-authoritative and keep running while the
                // player's network is down. The independent four-minute reconnect window still
                // decides the match if the player remains disconnected.
                clock.OperationRemainingMs[index] = Math.Max(0,
                    clock.OperationRemainingMs[index] - elapsed);
                continue;
            }
            if (!PlayerConnected(room, index)) continue;
            clock.TotalRemainingMs[index] = Math.Max(0, clock.TotalRemainingMs[index] - elapsed);
            clock.OperationRemainingMs[index] = Math.Max(0, clock.OperationRemainingMs[index] - elapsed);
        }
        clock.LastSettledAt = now;
    }

    private void RefreshRankedClockActorsLocked(Room room, DateTimeOffset now, int? completedActor = null)
    {
        if (room.RankedClock is not { } clock || room.Game is null) return;
        var preparation = IsRankedPreparationPhase(room.Game);
        var required = RequiredDecisionPlayers(room.Game);
        if (!preparation && !clock.NormalClockStarted)
        {
            // The command that leaves setup was settled against the preparation state before it
            // ran. Establish the normal-clock baseline only now, so command processing latency is
            // not charged to whichever player happened to complete the last setup decision.
            clock.NormalClockStarted = true;
            Array.Fill(clock.OperationRemainingMs, clock.TimeControl.OperationTimeSeconds * 1000L);
            clock.LastSettledAt = now;
        }
        else SettleRankedClockLocked(room, now);
        for (var index = 0; index < 2; index++)
        {
            var wasActing = clock.Acting[index];
            clock.Acting[index] = required.Contains(index);
            if (clock.Acting[index] && (!preparation || room.Game.HasTimedRankedSetupDecision(index))
                && (!wasActing || completedActor == index))
                clock.OperationRemainingMs[index] = preparation
                    ? SetupDecisionLimit(room.Game, clock.TimeControl).TotalMillisecondsAsLong()
                    : clock.TimeControl.OperationTimeSeconds * 1000L;
        }
        if (now > clock.LastSettledAt) clock.LastSettledAt = now;
    }

    private static bool IsRankedPreparationPhase(L12GameEngine game)
        => game.State.Phase is L12Phase.Initiative or L12Phase.DisasterPreparation or L12Phase.Mulligan;

    private static TimeSpan SetupDecisionLimit(L12GameEngine game, L12RankedTimeControlConfig timeControl)
        => TimeSpan.FromSeconds(game.State.Phase switch
        {
            L12Phase.DisasterPreparation => timeControl.DisasterDecisionSeconds,
            L12Phase.Mulligan => timeControl.MulliganDecisionSeconds,
            _ => 0,
        });

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

    private async Task<bool> ApplyRankedSetupTimeoutsLockedAsync(Room room, DateTimeOffset now)
    {
        if (room.RankedClock is not { } clock || room.Game is null
            || !IsRankedPreparationPhase(room.Game)) return false;
        var applied = false;
        // At most two players can expire together (the simultaneous mulligan). Recompute after
        // every accepted default because it can create the next preparation step and a fresh clock.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var playerIndex = Enumerable.Range(0, 2).FirstOrDefault(index => clock.Acting[index]
                && clock.OperationRemainingMs[index] <= 0
                && room.Game.HasTimedRankedSetupDecision(index), -1);
            if (playerIndex < 0) break;
            if (!room.Game.TryCreateRankedSetupTimeoutCommand(playerIndex, out var command))
                throw new InvalidOperationException("排位准备超时没有当前合法默认操作");

            var result = room.Game.Handle(playerIndex, command);
            if (!result.Accepted)
                throw new InvalidOperationException($"排位准备超时默认操作被拒绝：{result.Error}");
            room.CommandSequence++;
            RefreshRankedClockActorsLocked(room, now, playerIndex);
            try
            {
                await _recorder.AppendRankedAsync(room.Game, room.CommandSequence, playerIndex,
                    JsonSerializer.Serialize(command, RankedSetupCommandJson), result,
                    CaptureRankedRuntime(room, now), settlement: null);
            }
            catch (Exception error)
            {
                // The synthetic timeout command follows the same transaction/replay boundary as a
                // player command. Never expose an uncommitted automatic choice.
                if (!await ReloadRankedRoomFromRecorderAsync(room)) room.Closed = true;
                throw new InvalidOperationException("排位准备超时操作持久化失败，已回退到最后确认状态", error);
            }
            applied = true;
            if (room.RankedClock is { } current) current.SetupBroadcastPending = true;
            clock = room.RankedClock!;
        }
        return applied;
    }

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
                    && now - session.DisconnectedAt.Value >= TimeSpan.FromSeconds(clock.TimeControl.ReconnectGraceSeconds);
            }).ToArray();
            if (disconnectedExpired[0] && disconnectedExpired[1])
            {
                clock.ConclusionKind = "both-disconnect-timeout";
                room.Game.ConcludeByAuthority(null,
                    $"双方掉线超过{RankedDurationLabel(clock.TimeControl.ReconnectGraceSeconds)}且均未能恢复，对局无效");
                concluded = true;
            }
            else if (disconnectedExpired[0] || disconnectedExpired[1])
            {
                var loser = disconnectedExpired[0] ? 0 : 1;
                clock.ConclusionKind = "disconnect-timeout";
                room.Game.ConcludeByAuthority(1 - loser,
                    $"{room.Game.State.Players[loser].Name}掉线超过{RankedDurationLabel(clock.TimeControl.ReconnectGraceSeconds)}未能重连");
                concluded = true;
            }
            else if (await ApplyRankedSetupTimeoutsLockedAsync(room, now))
            {
                clock = room.RankedClock!;
            }
            if (!concluded && room.Game.State.Phase != L12Phase.GameOver
                && !IsRankedPreparationPhase(room.Game))
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
                            ? $"双方{RankedDurationLabel(clock.TimeControl.TotalTimeSeconds)}总操作时间同时耗尽，对局无效"
                            : $"双方单次操作时间同时超过{RankedDurationLabel(clock.TimeControl.OperationTimeSeconds)}，对局无效");
                    }
                    else
                    {
                        var loser = expired[0];
                        clock.ConclusionKind = totalExpired.Length > 0 ? "total-timeout" : "operation-timeout";
                        var reason = totalExpired.Length > 0
                            ? $"{RankedDurationLabel(clock.TimeControl.TotalTimeSeconds)}总操作时间耗尽"
                            : $"单次操作超过{RankedDurationLabel(clock.TimeControl.OperationTimeSeconds)}";
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
                clock.AuthorityEventRecorded = true;
                var settlement = BuildRankedSettlementEnvelope(room, now);
                await _recorder.AppendRankedAuthorityAsync(room.Game, room.CommandSequence,
                    room.Game.State.WinnerReason ?? "排位权威裁决",
                    CaptureRankedRuntime(room, now, "completed"), settlement);
                room.CompletionRecorded = true;
            }
            catch (Exception error)
            {
                // 命令事件、最终 matches 行、runtime 与 outbox 是同一事务。失败后必须
                // 从最后提交边界重建，绝不继续广播未落盘的 GameOver。
                if (!await ReloadRankedRoomFromRecorderAsync(room)) room.Closed = true;
                Console.Error.WriteLine($"Ranked authority event persistence ({room.Code}): {error.Message}");
                throw new InvalidOperationException("排位权威结论持久化失败，房间已回退到最后提交状态", error);
            }
        }
        await CompleteTournamentRoomGameAsync(room);
        return concluded;
    }

    private static string RankedDurationLabel(int seconds)
        => seconds % 60 == 0 ? $"{seconds / 60}分钟" : $"{seconds}秒";

    public async Task<IReadOnlyList<OutgoingMessage>> TickRankedClocksAsync(DateTimeOffset? utcNow = null)
    {
        var messages = new List<OutgoingMessage>();
        var checkpoints = new List<L12RankedRuntimeCheckpoint>();
        var now = utcNow ?? _utcNow();
        foreach (var room in _rooms.Values.Where(room => room.RankedClock is not null && room.Game is not null
                     && (room.Game.State.Phase != L12Phase.GameOver || !room.CompletionRecorded
                         || !room.RankedResultReported)).ToArray())
        {
            await room.Gate.WaitAsync();
            try
            {
                var broadcast = await ApplyRankedClockConclusionLockedAsync(room, now);
                if (room.RankedClock is { SetupBroadcastPending: true } clock)
                {
                    clock.SetupBroadcastPending = false;
                    broadcast = true;
                }
                if (broadcast)
                    messages.AddRange(BroadcastGame(room));
                if (room.Game?.State.Phase != L12Phase.GameOver && !room.Closed)
                    checkpoints.Add(CaptureRankedRuntime(room, now));
            }
            catch (Exception error)
            {
                // 一个房间的暂时性存储故障不能阻止其他排位房间的权威计时。
                Console.Error.WriteLine($"Ranked clock room ({room.Code}): {error.Message}");
            }
            finally { room.Gate.Release(); }
        }
        try
        {
            // 所有活跃排位的 1 秒 checkpoint 共用一个 WAL 事务，避免逐房间 fsync。
            await _recorder.PersistRankedRuntimeBatchAsync(checkpoints);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Ranked clock checkpoint batch: {error.Message}");
        }
        await TickMaintenanceLockedRoomsAsync(now, messages);
        await TickSettlementRoomsAsync(now, messages);
        return messages;
    }

    private async Task TickMaintenanceLockedRoomsAsync(DateTimeOffset now, List<OutgoingMessage> messages)
    {
        var policy = CaptureOperationsPolicy();
        // Immediate maintenance closes only new-game entry. It deliberately shields already-running games
        // from the scheduled-maintenance invalidation path while the manual override is active.
        if (policy.ImmediateMaintenance?.Enabled == true) return;
        if (!policy.Maintenance.Enabled) return;
        var starts = policy.Maintenance.StartsAt;
        var remaining = starts is { } scheduledStart ? scheduledStart - now : TimeSpan.Zero;
        var active = policy.IsMaintenanceActive(now);
        var warningMinutes = starts is not null && remaining.TotalMinutes is >= 0 and <= 30
            ? Math.Max(0, (int)Math.Ceiling(remaining.TotalMinutes / 5d) * 5) : -1;
        if (!active && warningMinutes < 0) return;

        foreach (var room in _rooms.Values.Where(candidate => candidate.Game is not null
                     && !IsAuthorizedMaintenanceSandbox(candidate)
                     && (candidate.Game.State.Phase != L12Phase.GameOver
                         || !candidate.CompletionRecorded)).ToArray())
        {
            await room.Gate.WaitAsync();
            try
            {
                if (active)
                {
                    if (room.Game!.State.Phase != L12Phase.GameOver)
                    {
                        if (room.RankedClock is { } clock) clock.ConclusionKind = "maintenance-invalidated";
                        room.Game.ConcludeByAuthority(null, "服务器维护开始，当前对局无效");
                    }
                    if (room.RankedClock is not null)
                    {
                        await ApplyRankedClockConclusionLockedAsync(room, now);
                    }
                    else if (!room.CompletionRecorded)
                    {
                        if (!room.MaintenanceAuthorityEventRecorded)
                        {
                            room.CommandSequence++;
                            await _recorder.AppendAuthorityAsync(room.Game, room.CommandSequence,
                                room.Game.State.WinnerReason ?? "服务器维护开始，当前对局无效");
                            room.MaintenanceAuthorityEventRecorded = true;
                        }
                        var completionError = await CompleteTournamentRoomGameAsync(room);
                        if (completionError is not null)
                            Console.Error.WriteLine($"Maintenance completion ({room.Code}): {completionError}");
                    }
                    messages.AddRange(BroadcastGame(room));
                    continue;
                }
                if (room.LastMaintenanceWarningMinutes == warningMinutes) continue;
                room.LastMaintenanceWarningMinutes = warningMinutes;
                var text = $"服务器将于北京时间{starts!.Value.ToOffset(TimeSpan.FromHours(8)):HH:mm}开始维护，距离维护还有{warningMinutes}分钟，维护开始当前对局将会被废弃，请尽快结束。";
                foreach (var audience in room.Sessions.Concat(room.Spectators).Distinct())
                    messages.Add(new OutgoingMessage(audience, new { type = "maintenanceWarning", message = text,
                        startsAt = starts, minutesRemaining = warningMinutes }));
            }
            finally { room.Gate.Release(); }
        }
    }

    private L12RankedClockView? RankedClockView(Room room)
    {
        if (room.RankedClock is not { } clock) return null;
        var now = _utcNow();
        var preparation = room.Game is not null && IsRankedPreparationPhase(room.Game);
        var timedPreparation = preparation && Enumerable.Range(0, 2)
            .Any(index => room.Game!.HasTimedRankedSetupDecision(index));
        var elapsed = now > clock.LastSettledAt
            ? (long)(now - clock.LastSettledAt).TotalMilliseconds : 0L;
        var players = Enumerable.Range(0, 2).Select(index =>
        {
            var session = PlayerSession(room, index);
            long? reconnect = session is { Connected: false, DisconnectedAt: not null }
                ? Math.Max(0L, (long)(TimeSpan.FromSeconds(clock.TimeControl.ReconnectGraceSeconds)
                    - (now - session.DisconnectedAt.Value)).TotalMilliseconds)
                : null;
            var setupRunning = elapsed > 0 && preparation && timedPreparation && clock.Acting[index]
                && room.Game!.HasTimedRankedSetupDecision(index)
                && room.Game.State.Phase != L12Phase.GameOver;
            var normalRunning = elapsed > 0 && !preparation && clock.Acting[index]
                && session?.Connected == true
                && room.Game?.State.Phase != L12Phase.GameOver;
            return new L12RankedClockPlayerView(index,
                normalRunning ? Math.Max(0, clock.TotalRemainingMs[index] - elapsed) : clock.TotalRemainingMs[index],
                setupRunning || normalRunning
                    ? Math.Max(0, clock.OperationRemainingMs[index] - elapsed)
                    : clock.OperationRemainingMs[index],
                clock.Acting[index], session?.Connected == true, reconnect);
        }).ToArray();
        return new L12RankedClockView(now.ToUnixTimeMilliseconds(), clock.TimeControl.TotalTimeSeconds * 1000L,
            preparation ? (timedPreparation ? SetupDecisionLimit(room.Game!, clock.TimeControl).TotalMillisecondsAsLong() : 0L)
                : clock.TimeControl.OperationTimeSeconds * 1000L,
            clock.TimeControl.ReconnectGraceSeconds * 1000L, players, clock.TimeControl);
    }
}

file static class L12RankedClockTimeSpanExtensions
{
    public static long TotalMillisecondsAsLong(this TimeSpan value) => (long)value.TotalMilliseconds;
}
