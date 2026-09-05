using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class L12RoomManager
{
    private static readonly JsonSerializerOptions RankedRecoveryJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private async Task StartRecordedGameAsync(Room room, IReadOnlyList<Session> members,
        IReadOnlyList<L12PresetDeckDefinition> decks)
    {
        if (room.Game is null) throw new InvalidOperationException("对局尚未建立");
        if (!string.Equals(room.Options.MatchModeId, "ranked", StringComparison.OrdinalIgnoreCase))
        {
            await _recorder.StartAsync(room.Game, room.Options.MatchModeId,
                members[0].AccountId, members[1].AccountId, decks);
            return;
        }
        if (members.Count != 2 || decks.Count != 2
            || members.Any(member => string.IsNullOrWhiteSpace(member.AccountId)))
            throw new InvalidOperationException("排位持久化缺少双席账号或牌库");
        await _recorder.StartRankedAsync(room.Game, members[0].AccountId!, members[1].AccountId!,
            decks, CaptureRankedRuntime(room, _utcNow()));
    }

    private L12RankedSettlementEnvelope BuildRankedSettlementEnvelope(Room room, DateTimeOffset endedAt)
    {
        if (room.Game is null || room.Game.State.Phase != L12Phase.GameOver)
            throw new InvalidOperationException("只能为已结束排位建立结算载荷");
        var members = room.Sessions.Select(id => _sessions[id]).OrderBy(member => member.PlayerIndex).ToArray();
        if (members.Length != 2 || members.Any(member => string.IsNullOrWhiteSpace(member.AccountId)))
            throw new InvalidOperationException("排位结算缺少双席账号");
        return new L12RankedSettlementEnvelope(1, room.Game.State.MatchId,
            members[0].AccountId!, members[1].AccountId!, SelectedDeck(members[0]).MasterId,
            SelectedDeck(members[1]).MasterId, room.Game.State.Winner, room.StartedAt, endedAt,
            room.MeaningfulCommandCount, RankedConclusionKind(room),
            members[0].IntegrityClientKey, members[1].IntegrityClientKey);
    }

    public async Task<L12RankedRecoverySummary> RestoreRankedRoomsAsync()
    {
        var settlementResult = await DrainRankedSettlementOutboxAsync(includeApplied: true);
        var restored = 0;
        var invalidated = 0;
        var failed = settlementResult.Failed;
        foreach (var source in await _recorder.LoadActiveRankedMatchesAsync())
        {
            Room? room = null;
            try
            {
                if (source.LoadError is not null)
                    throw new InvalidDataException(source.LoadError);
                room = BuildRestoredRankedRoom(source);
                if (!_rooms.TryAdd(room.Code, room))
                    throw new InvalidOperationException("恢复房间码与当前房间冲突");
                if (await ApplyRankedClockConclusionLockedAsync(room, _utcNow()))
                {
                    invalidated += room.Game?.State.Winner is null ? 1 : 0;
                    RemoveRestoredRankedRoom(room);
                }
                else
                {
                    restored++;
                }
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException
                                                 or JsonException or KeyNotFoundException)
            {
                Console.Error.WriteLine($"Ranked restore incompatible ({source.MatchId}): {error.Message}");
                if (room is not null) RemoveRestoredRankedRoom(room);
                try
                {
                    await FinalizeIncompatibleRankedAsync(source, error.Message);
                    invalidated++;
                }
                catch (Exception finalizeError)
                {
                    failed++;
                    Console.Error.WriteLine($"Ranked restore invalidation ({source.MatchId}): {finalizeError.Message}");
                }
            }
        }
        var afterRestore = await DrainRankedSettlementOutboxAsync();
        return new L12RankedRecoverySummary(settlementResult.Applied + afterRestore.Applied,
            restored, invalidated, failed + afterRestore.Failed);
    }

    private Room BuildRestoredRankedRoom(L12RankedRecoverySource source)
    {
        var runtime = source.Runtime ?? throw new InvalidDataException("缺少排位运行快照");
        var game = ReplayRankedEngine(source);
        if (game.State.Phase == L12Phase.GameOver)
            throw new InvalidDataException("未完成记录已包含 GameOver，拒绝恢复为活局");
        var room = new Room
        {
            Code = source.RoomCode,
            IsMatchmaking = true,
            OperationsPolicy = game.State.OperationsPolicy,
            Options = new L12RoomOptions
            {
                MatchModeId = "ranked", Spectating = "public", HandVisibility = "request",
                DisasterMode = game.State.DisasterMode, UseCardRestrictions = true,
            },
            Game = game,
            CommandSequence = runtime.CommandSequence,
            StartedAt = runtime.StartedAt,
            MeaningfulCommandCount = runtime.MeaningfulCommandCount,
            CompletionRecorded = false,
            RankedResultReported = false,
        };
        room.Ready[0] = room.Ready[1] = true;
        RestoreRankedClock(room, runtime);
        var addedPlaceholders = new List<Guid>();
        try
        {
            for (var player = 0; player < 2; player++)
            {
                var placeholderId = Guid.NewGuid();
                var generation = Math.Max(0, runtime.ConnectionGenerations[player]);
                var disconnectedAt = runtime.Connected[player]
                    ? runtime.UpdatedAt
                    : runtime.DisconnectedAt[player] ?? runtime.UpdatedAt;
                var session = new Session
                {
                    Id = placeholderId,
                    AccountId = source.AccountIds[player],
                    Name = source.PlayerNames[player],
                    RoomCode = room.Code,
                    PlayerIndex = player,
                    CustomDeck = source.Decks[player],
                    Connected = false,
                    DisconnectedAt = disconnectedAt,
                    IntegrityClientKey = runtime.IntegrityClientKeys[player],
                    ConnectionGeneration = generation,
                };
                if (!_sessions.TryAdd(placeholderId, session))
                    throw new InvalidOperationException("恢复会话占位符冲突");
                addedPlaceholders.Add(placeholderId);
                room.Sessions.Add(placeholderId);
                _accountConnectionGenerations.AddOrUpdate(source.AccountIds[player], generation,
                    (_, current) => Math.Max(current, generation));
            }
        }
        catch
        {
            foreach (var placeholder in addedPlaceholders) _sessions.TryRemove(placeholder, out _);
            throw;
        }
        return room;
    }

    private L12GameEngine ReplayRankedEngine(L12RankedRecoverySource source)
    {
        using var initialDocument = JsonDocument.Parse(source.InitialStateJson);
        var initial = initialDocument.RootElement;
        var policyElement = Property(initial, "OperationsPolicy", "operationsPolicy");
        var policy = policyElement.Deserialize<L12OperationsPolicySnapshot>(RankedRecoveryJson)
            ?? throw new InvalidDataException("初始状态缺少运营规则快照");
        var disasterMode = Property(initial, "DisasterMode", "disasterMode").GetString() ?? "season";
        var engine = new L12GameEngine(_catalog, source.MatchId, source.RoomCode, source.Seed,
            source.PlayerNames, source.Decks, disasterMode: disasterMode, operationsPolicy: policy);
        if (!string.Equals(engine.ComputeStateHash(), HashStateJson(source.InitialStateJson),
                StringComparison.Ordinal))
            throw new InvalidDataException("初始状态重放校验失败");
        long expectedSequence = 0;
        foreach (var recorded in source.Commands)
        {
            if (recorded.Sequence != ++expectedSequence)
                throw new InvalidDataException("排位命令序号不连续");
            var type = Property(recorded.Command, "Type", "type").GetString();
            CommandResult outcome;
            if (string.Equals(type, "authorityConclusion", StringComparison.OrdinalIgnoreCase))
            {
                var winnerElement = PropertyOrNull(recorded.State, "Winner", "winner");
                var winner = winnerElement is { ValueKind: JsonValueKind.Number } value
                    ? value.GetInt32() : (int?)null;
                var reason = PropertyOrNull(recorded.State, "WinnerReason", "winnerReason")?.GetString()
                    ?? "排位权威裁决";
                engine.ConcludeByAuthority(winner, reason);
                outcome = CommandResult.Ok();
            }
            else
            {
                var command = recorded.Command.Deserialize<L12Command>(RankedRecoveryJson)
                    ?? throw new InvalidDataException("排位命令载荷为空");
                outcome = engine.Handle(recorded.PlayerIndex, command);
            }
            if (outcome.Accepted != recorded.Accepted
                || engine.State.Revision != recorded.Revision
                || !string.Equals(engine.ComputeStateHash(), recorded.StateHash, StringComparison.Ordinal))
                throw new InvalidDataException($"排位命令重放校验失败：{recorded.Sequence}");
        }
        var runtime = source.Runtime!;
        if (runtime.CommandSequence != expectedSequence
            || runtime.StateRevision != engine.State.Revision
            || !string.Equals(runtime.StateHash, engine.ComputeStateHash(), StringComparison.Ordinal))
            throw new InvalidDataException("排位运行快照与最后命令边界不一致");
        return engine;
    }

    private async Task FinalizeIncompatibleRankedAsync(L12RankedRecoverySource source, string reason)
    {
        var now = _utcNow();
        var runtime = source.Runtime;
        var started = runtime?.StartedAt
            ?? (DateTimeOffset.TryParse(source.StartedUtc, out var parsed) ? parsed : now);
        var envelope = new L12RankedSettlementEnvelope(1, source.MatchId,
            source.AccountIds.ElementAtOrDefault(0) ?? string.Empty,
            source.AccountIds.ElementAtOrDefault(1) ?? string.Empty,
            source.Decks.ElementAtOrDefault(0)?.MasterId ?? string.Empty,
            source.Decks.ElementAtOrDefault(1)?.MasterId ?? string.Empty,
            null, started, now, runtime?.MeaningfulCommandCount ?? 0,
            "restore-incompatible", runtime?.IntegrityClientKeys.ElementAtOrDefault(0) ?? string.Empty,
            runtime?.IntegrityClientKeys.ElementAtOrDefault(1) ?? string.Empty);
        await _recorder.FinalizeIncompatibleRankedAsync(source, envelope,
            $"排位恢复不兼容：{reason}");
    }

    private void RemoveRestoredRankedRoom(Room room)
    {
        ((ICollection<KeyValuePair<string, Room>>)_rooms)
            .Remove(new KeyValuePair<string, Room>(room.Code, room));
        foreach (var id in room.Sessions) _sessions.TryRemove(id, out _);
    }

    private async Task<(int Applied, int Failed)> DrainRankedSettlementOutboxAsync(string? matchId = null,
        bool includeApplied = false)
    {
        if (_platform is null) return (0, 0);
        var applied = 0;
        var failed = 0;
        var entries = includeApplied
            ? await _recorder.ListRankedSettlementReconciliationAsync()
            : await _recorder.ListPendingRankedSettlementsAsync();
        foreach (var item in entries
                     .Where(item => matchId is null || string.Equals(item.MatchId, matchId,
                         StringComparison.OrdinalIgnoreCase)))
        {
            if (item.LoadError is not null || item.Payload is null)
            {
                failed++;
                try
                {
                    await _recorder.QuarantineRankedSettlementAsync(item.MatchId, item.PayloadHash,
                        $"outbox-payload-invalid:{item.LoadError ?? "unknown"}");
                }
                catch (Exception quarantineError)
                {
                    Console.Error.WriteLine($"Ranked outbox quarantine ({item.MatchId}): {quarantineError.Message}");
                }
                continue;
            }
            try
            {
                var payload = item.Payload;
                if (item.Status == "applied")
                {
                    try
                    {
                        _platform.VerifyRankedSettlementApplied(payload);
                        await _recorder.ClearAppliedRankedSettlementErrorAsync(item.MatchId,
                            item.PayloadHash);
                        continue;
                    }
                    catch (Exception verificationError)
                    {
                        if (!_platform.CanReplayMissingRankedSettlement(payload, out var reason))
                            throw new InvalidOperationException(
                                $"已确认 outbox 与平台账本冲突：{reason}", verificationError);
                    }
                }
                var context = new L12RankedIntegrityContext(payload.StartedAt, payload.EndedAt,
                    payload.MeaningfulCommandCount, payload.ConclusionKind,
                    payload.FirstNetworkFingerprint, payload.SecondNetworkFingerprint);
                if (payload.Winner is { } winner)
                    _platform.SettleRankedMatch(payload.MatchId, payload.FirstAccountId,
                        payload.SecondAccountId, winner, payload.FirstMasterId,
                        payload.SecondMasterId, context);
                else
                    _platform.RecordInvalidRankedMatch(payload.MatchId, payload.FirstAccountId,
                        payload.SecondAccountId, payload.FirstMasterId, payload.SecondMasterId, context);
                _platform.VerifyRankedSettlementApplied(payload);
                if (item.Status == "pending")
                    await _recorder.MarkRankedSettlementAppliedAsync(item.MatchId, item.PayloadHash);
                else
                    await _recorder.ClearAppliedRankedSettlementErrorAsync(item.MatchId,
                        item.PayloadHash);
                applied++;
            }
            catch (Exception error)
            {
                failed++;
                try { await _recorder.RecordRankedSettlementFailureAsync(item.MatchId, error.Message); }
                catch (Exception recordError)
                {
                    Console.Error.WriteLine($"Ranked outbox failure record ({item.MatchId}): {recordError.Message}");
                }
                Console.Error.WriteLine($"Ranked outbox replay ({item.MatchId}): {error.Message}");
            }
        }
        return (applied, failed);
    }

    private async Task<bool> ReloadRankedRoomFromRecorderAsync(Room room)
    {
        var source = (await _recorder.LoadActiveRankedMatchesAsync())
            .SingleOrDefault(item => string.Equals(item.MatchId, room.Game?.State.MatchId,
                StringComparison.OrdinalIgnoreCase));
        if (source is null || source.LoadError is not null || source.Runtime is null) return false;
        var engine = ReplayRankedEngine(source);
        room.Game = engine;
        room.CommandSequence = source.Runtime.CommandSequence;
        room.StartedAt = source.Runtime.StartedAt;
        room.MeaningfulCommandCount = source.Runtime.MeaningfulCommandCount;
        room.CompletionRecorded = false;
        room.RankedResultReported = false;
        room.Closed = false;
        RestoreRankedClock(room, source.Runtime);
        return true;
    }

    private static JsonElement Property(JsonElement element, string pascal, string camel)
        => PropertyOrNull(element, pascal, camel)
           ?? throw new InvalidDataException($"缺少权威状态字段：{pascal}");

    private static JsonElement? PropertyOrNull(JsonElement element, string pascal, string camel)
        => element.TryGetProperty(pascal, out var value) || element.TryGetProperty(camel, out value)
            ? value : null;

    private static string HashStateJson(string json)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
}
