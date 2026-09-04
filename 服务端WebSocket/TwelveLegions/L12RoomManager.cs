using System.Collections.Concurrent;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record OutgoingMessage(Guid SessionId, object Payload);

public sealed record L12OnlinePresence(
    string AccountId,
    string Activity,
    string? RoomCode,
    bool CanInvite,
    bool CanSpectate,
    string? ActionReason);

public sealed record L12RoomRuntimeStats(
    int OnlineAccountCount,
    int ConnectedSessionCount,
    int RoomCount,
    int ActiveGameCount);

public sealed partial class L12RoomManager
{
    private sealed class Session
    {
        public required Guid Id { get; init; }
        public string? AccountId { get; init; }
        public required string Name { get; set; }
        public string? RoomCode { get; set; }
        public int? PlayerIndex { get; set; }
        public int SelectedDeckIndex { get; set; }
        public L12PresetDeckDefinition? CustomDeck { get; set; }
        public bool IsSpectator { get; set; }
        public bool IsVirtual { get; init; }
        public bool Connected { get; set; } = true;
        public DateTimeOffset? DisconnectedAt { get; set; }
        public string IntegrityClientKey { get; set; } = string.Empty;
    }

    private sealed class FriendInvitation
    {
        public required string Id { get; init; }
        public required string RoomCode { get; init; }
        public required string FromAccountId { get; init; }
        public required string ToAccountId { get; init; }
        public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    }

    private sealed class Room
    {
        public required string Code { get; init; }
        public List<Guid> Sessions { get; } = [];
        public List<Guid> Spectators { get; } = [];
        public L12RoomOptions Options { get; set; } = new();
        public required L12OperationsPolicySnapshot OperationsPolicy { get; set; }
        public bool[] Ready { get; } = [false, false];
        public L12GameEngine? Game { get; set; }
        public long CommandSequence { get; set; }
        public bool IsSandbox { get; init; }
        public Guid? GmControllerSessionId { get; set; }
        public string? TournamentId { get; init; }
        public string? TournamentCode { get; init; }
        public string? TournamentMatchId { get; init; }
        public string? TournamentRulesHash { get; init; }
        public bool TournamentResultReported { get; set; }
        public bool IsMatchmaking { get; init; }
        public bool RankedResultReported { get; set; }
        public bool CompletionRecorded { get; set; }
        public RankedClockState? RankedClock { get; set; }
        public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
        public int MeaningfulCommandCount { get; set; }
        public bool Closed { get; set; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    private sealed record MatchmakingEntry(Guid SessionId, string AccountId, string Mode,
        L12PresetDeckDefinition Deck, DateTimeOffset JoinedAt, double HiddenRating);

    private readonly L12Catalog _catalog;
    private readonly MatchRecorder _recorder;
    private readonly L12PlatformStore? _platform;
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FriendInvitation> _friendInvitations = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _tournamentRoomGate = new();
    private readonly object _matchmakingGate = new();
    private readonly SemaphoreSlim _sessionRecoveryGate = new(1, 1);
    private readonly List<MatchmakingEntry> _matchmaking = [];
    private readonly Func<DateTimeOffset> _utcNow;

    public L12RoomManager(L12Catalog catalog, MatchRecorder recorder, L12PlatformStore? platform = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _catalog = catalog;
        _recorder = recorder;
        _platform = platform;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public object Connect(Guid sessionId, string accountId, string? requestedName,
        string? integrityClientKey = null)
        => ConnectAsync(sessionId, accountId, requestedName, integrityClientKey).GetAwaiter().GetResult();

    public async Task<object> ConnectAsync(Guid sessionId, string accountId, string? requestedName,
        string? integrityClientKey = null)
    {
        var name = NormalizeName(requestedName);
        await _sessionRecoveryGate.WaitAsync();
        try
        {
            var disconnected = _sessions.FirstOrDefault(pair =>
                pair.Key != sessionId && !pair.Value.Connected && !pair.Value.IsVirtual
                && pair.Value.RoomCode is not null
                && string.Equals(pair.Value.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
            if (disconnected.Value is not null)
            {
                var recovered = disconnected.Value;
                if (recovered.RoomCode is not null && _rooms.TryGetValue(recovered.RoomCode, out var recoveredRoom))
                {
                    await recoveredRoom.Gate.WaitAsync();
                    try
                    {
                        var now = _utcNow();
                        // 超过重连截止时间的连接不能抢在周期看门狗前清除掉线证据。
                        await ApplyRankedClockConclusionLockedAsync(recoveredRoom, now);
                        recovered.Connected = true;
                        recovered.DisconnectedAt = null;
                        if (!string.IsNullOrWhiteSpace(integrityClientKey))
                            recovered.IntegrityClientKey = integrityClientKey;
                        var playerSlot = recoveredRoom.Sessions.IndexOf(disconnected.Key);
                        if (playerSlot >= 0) recoveredRoom.Sessions[playerSlot] = sessionId;
                        var spectatorSlot = recoveredRoom.Spectators.IndexOf(disconnected.Key);
                        if (spectatorSlot >= 0) recoveredRoom.Spectators[spectatorSlot] = sessionId;
                        if (recoveredRoom.GmControllerSessionId == disconnected.Key)
                            recoveredRoom.GmControllerSessionId = sessionId;
                        RefreshRankedClockActorsLocked(recoveredRoom, now);
                    }
                    finally { recoveredRoom.Gate.Release(); }
                }
                else
                {
                    recovered.Connected = true;
                    recovered.DisconnectedAt = null;
                    if (!string.IsNullOrWhiteSpace(integrityClientKey))
                        recovered.IntegrityClientKey = integrityClientKey;
                }
                _sessions[sessionId] = recovered;
                _sessions.TryRemove(disconnected.Key, out _);
                return new { type = "session", sessionId, name, recovered = true, roomCode = recovered.RoomCode };
            }

            _sessions[sessionId] = new Session
            {
                Id = sessionId, AccountId = accountId, Name = name,
                IntegrityClientKey = integrityClientKey ?? string.Empty,
            };
            return new { type = "session", sessionId, name, recovered = false, roomCode = (string?)null };
        }
        finally { _sessionRecoveryGate.Release(); }
    }

    public object Connect(Guid sessionId, string? requestedName)
        => Connect(sessionId, $"legacy:{NormalizeName(requestedName).ToLowerInvariant()}", requestedName);

    public bool IsAccountOnline(string accountId)
        => _sessions.Values.Any(session => session.Connected && !session.IsVirtual && session.AccountId == accountId);

    public IReadOnlySet<string> OnlineAccountIds()
        => _sessions.Values
            .Where(session => session.Connected && !session.IsVirtual && !string.IsNullOrWhiteSpace(session.AccountId))
            .Select(session => session.AccountId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool CanChangeRankedFaction(string accountId)
    {
        lock (_matchmakingGate) return !_sessions.Values.Any(session => session.AccountId == accountId
            && (session.RoomCode is not null || _matchmaking.Any(entry => entry.AccountId == accountId)));
    }

    public async Task<IReadOnlyList<OutgoingMessage>> JoinMatchmakingAsync(Guid sessionId, string? mode,
        L12CustomDeckSubmission? submission)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.AccountId is null)
            return Error(sessionId, "请先登录账号", "matchmakingRejected");
        var normalizedMode = (mode ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedMode is not ("ranked" or "casual"))
            return Error(sessionId, "匹配模式无效", "matchmakingRejected");
        if (session.RoomCode is not null)
            return Error(sessionId, "请先离开当前房间", "matchmakingRejected");
        if (_platform is null) return Error(sessionId, "匹配服务不可用", "matchmakingRejected");
        var current = CaptureOperationsPolicy();
        if (TryOperationsEntryBlock(sessionId, current, normalizedMode, out var blocked)) return blocked;
        var policy = normalizedMode == "ranked" ? current.ForRankedMatch() : current.ForCasualMatch();
        if (normalizedMode == "ranked" && string.IsNullOrWhiteSpace(_platform.RankedProfile(session.AccountId).Faction))
            return Error(sessionId, "请先选择本赛季派系", "matchmakingRejected");
        L12PresetDeckDefinition deck;
        if (submission is not null)
        {
            if (!L12DeckValidator.TryValidate(_catalog, submission, out deck!, out var error,
                    policy.CardRestrictions)) return Error(sessionId, error, "matchmakingRejected");
        }
        else
        {
            if (!TryDefaultPresetIndexes(policy, out var preset, out _))
                return Error(sessionId, "当前规则下没有可用牌库", "matchmakingRejected");
            deck = _catalog.DeckAt(preset);
        }

        MatchmakingEntry? opponent = null;
        MatchmakingEntry entry;
        lock (_matchmakingGate)
        {
            var existing = _matchmaking.FirstOrDefault(item => item.AccountId == session.AccountId
                && item.Mode == normalizedMode && IsQueueEntryValid(item));
            entry = new MatchmakingEntry(sessionId, session.AccountId, normalizedMode, deck,
                existing?.JoinedAt ?? DateTimeOffset.UtcNow, _platform.HiddenRating(session.AccountId));
            _matchmaking.RemoveAll(item => item.AccountId == entry.AccountId || !IsQueueEntryValid(item));
            opponent = _matchmaking.Where(item => item.Mode == normalizedMode && item.AccountId != entry.AccountId
                    && IsQueueEntryValid(item) && IsRatingCompatible(entry, item))
                .OrderBy(item => item.JoinedAt).FirstOrDefault();
            if (opponent is null) _matchmaking.Add(entry);
            else _matchmaking.Remove(opponent);
        }
        if (opponent is null)
            return [new OutgoingMessage(sessionId, new { type = "matchmakingState", queued = true,
                mode = normalizedMode, joinedAt = entry.JoinedAt, message = "已进入匹配队列" })];

        if (!_sessions.TryGetValue(opponent.SessionId, out var other) || !IsQueueEntryValid(opponent))
            return await JoinMatchmakingAsync(sessionId, normalizedMode, submission);
        current = CaptureOperationsPolicy();
        if (TryOperationsEntryBlock(sessionId, current, normalizedMode, out var pairingBlocked))
            return pairingBlocked.Concat(MatchmakingError(opponent.SessionId, "匹配期间运营规则已变更，请重新加入队列")).ToArray();
        policy = normalizedMode == "ranked" ? current.ForRankedMatch() : current.ForCasualMatch();
        var firstDeckValid = L12DeckValidator.TryValidatePreset(_catalog, entry.Deck, out var firstError,
            policy.CardRestrictions);
        var secondDeckValid = L12DeckValidator.TryValidatePreset(_catalog, opponent.Deck, out var secondError,
            policy.CardRestrictions);
        if (!firstDeckValid || !secondDeckValid)
        {
            var message = string.IsNullOrWhiteSpace(firstError) ? secondError : firstError;
            return [new OutgoingMessage(sessionId, new { type = "matchmakingRejected", message }),
                new OutgoingMessage(opponent.SessionId, new { type = "matchmakingRejected", message })];
        }

        var room = new Room
        {
            Code = GenerateAvailableRoomCode(), IsMatchmaking = true, OperationsPolicy = policy,
            Options = new L12RoomOptions { MatchModeId = normalizedMode, Spectating = "public",
                HandVisibility = "request", DisasterMode = normalizedMode == "ranked" ? "season" : "all",
                UseCardRestrictions = normalizedMode == "ranked" },
        };
        room.Sessions.Add(opponent.SessionId);
        room.Sessions.Add(sessionId);
        room.Ready[0] = room.Ready[1] = true;
        other.RoomCode = room.Code; other.PlayerIndex = 0; other.CustomDeck = opponent.Deck;
        session.RoomCode = room.Code; session.PlayerIndex = 1; session.CustomDeck = entry.Deck;
        room.Game = new L12GameEngine(_catalog, Guid.NewGuid().ToString("N"), room.Code, Random.Shared.Next(),
            [other.Name, session.Name], [opponent.Deck, entry.Deck], disasterMode: room.Options.DisasterMode,
            operationsPolicy: policy);
        InitializeRankedClock(room);
        try
        {
            if (!_rooms.TryAdd(room.Code, room)) throw new InvalidOperationException("匹配房间码冲突");
            await _recorder.StartAsync(room.Game, normalizedMode, other.AccountId, session.AccountId,
                [opponent.Deck, entry.Deck]);
        }
        catch
        {
            _rooms.TryRemove(room.Code, out _);
            ClearRoomMembership(other); ClearRoomMembership(session);
            lock (_matchmakingGate)
            {
                if (IsQueueEntryValid(opponent)) _matchmaking.Add(opponent);
                if (IsQueueEntryValid(entry)) _matchmaking.Add(entry);
            }
            return [new OutgoingMessage(sessionId, new { type = "matchmakingRejected", message = "建立匹配房间失败，已恢复队列" }),
                new OutgoingMessage(opponent.SessionId, new { type = "matchmakingRejected", message = "建立匹配房间失败，已恢复队列" })];
        }
        var found = room.Sessions.Select(id => new OutgoingMessage(id, new { type = "matchmakingFound",
            mode = normalizedMode, roomCode = room.Code, matchId = room.Game.State.MatchId,
            message = "匹配成功，正在建立对局" }));
        return found.Concat(BroadcastRoom(room)).Concat(BroadcastGame(room)).ToArray();
    }

    public IReadOnlyList<OutgoingMessage> CancelMatchmaking(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return Error(sessionId, "会话不存在");
        lock (_matchmakingGate) _matchmaking.RemoveAll(entry => entry.SessionId == sessionId
            || (!string.IsNullOrWhiteSpace(session.AccountId) && entry.AccountId == session.AccountId));
        return [new OutgoingMessage(sessionId, new { type = "matchmakingState", queued = false,
            message = "已取消匹配" })];
    }

    public Task<IReadOnlyList<OutgoingMessage>> PollMatchmakingAsync(Guid sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session) && session.RoomCode is not null)
            return RecoveryStateAsync(sessionId);
        MatchmakingEntry? existing;
        lock (_matchmakingGate)
            existing = _matchmaking.FirstOrDefault(entry => entry.SessionId == sessionId && IsQueueEntryValid(entry));
        if (existing is null)
            return Task.FromResult<IReadOnlyList<OutgoingMessage>>(
                [new OutgoingMessage(sessionId, new { type = "matchmakingState", queued = false,
                    message = "当前不在匹配队列" })]);
        var deck = new L12CustomDeckSubmission
        {
            Name = existing.Deck.Name,
            MasterId = existing.Deck.MasterId,
            CardIds = [.. existing.Deck.CardIds],
            MoraleIds = [.. existing.Deck.MoraleIds],
            SpecialIds = [.. existing.Deck.SpecialIds],
        };
        return JoinMatchmakingAsync(sessionId, existing.Mode, deck);
    }

    private bool IsQueueEntryValid(MatchmakingEntry entry)
        => _sessions.TryGetValue(entry.SessionId, out var session) && session.Connected
            && session.RoomCode is null && session.AccountId == entry.AccountId;

    private static bool IsRatingCompatible(MatchmakingEntry first, MatchmakingEntry second)
    {
        if (first.Mode != "ranked") return true;
        var now = DateTimeOffset.UtcNow;
        var sharedSeconds = Math.Min((now - first.JoinedAt).TotalSeconds, (now - second.JoinedAt).TotalSeconds);
        var allowed = sharedSeconds < 15 ? 100 : sharedSeconds < 30 ? 175 : sharedSeconds < 60 ? 275
            : sharedSeconds < 90 ? 400 : 500;
        return Math.Abs(first.HiddenRating - second.HiddenRating) <= allowed;
    }

    public L12RoomRuntimeStats RuntimeStats()
    {
        var connected = _sessions.Values.Where(session => session.Connected && !session.IsVirtual).ToArray();
        return new L12RoomRuntimeStats(
            connected.Where(session => !string.IsNullOrWhiteSpace(session.AccountId))
                .Select(session => session.AccountId!).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            connected.Length,
            _rooms.Count,
            _rooms.Values.Count(room => room.Game is not null && room.Game.State.Phase != L12Phase.GameOver));
    }

    public IReadOnlyDictionary<string, L12OnlinePresence> DescribeOnlinePresence(string viewerAccountId)
    {
        var viewerInRoom = _sessions.Values.Any(session => session.Connected && !session.IsVirtual
            && string.Equals(session.AccountId, viewerAccountId, StringComparison.OrdinalIgnoreCase)
            && session.RoomCode is not null);
        var currentPolicy = CaptureOperationsPolicy();
        var maintenanceActive = currentPolicy.IsMaintenanceActive(DateTimeOffset.UtcNow);
        return _sessions.Values
            .Where(session => session.Connected && !session.IsVirtual && !string.IsNullOrWhiteSpace(session.AccountId))
            .GroupBy(session => session.AccountId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group =>
            {
                var session = group.OrderByDescending(candidate => candidate.RoomCode is not null && !candidate.IsSpectator
                        && _rooms.TryGetValue(candidate.RoomCode, out var candidateRoom) && candidateRoom.Game is not null)
                    .ThenByDescending(candidate => candidate.RoomCode is not null)
                    .First();
                var isSelf = string.Equals(group.Key, viewerAccountId, StringComparison.OrdinalIgnoreCase);
                var friends = !isSelf && _platform?.AreFriends(viewerAccountId, group.Key) == true;
                if (session.RoomCode is null || !_rooms.TryGetValue(session.RoomCode, out var room))
                    return new L12OnlinePresence(group.Key, "idle", null,
                        !maintenanceActive && !isSelf && friends && !viewerInRoom, false,
                        isSelf ? "当前账号" : maintenanceActive ? currentPolicy.Maintenance.Message
                            : !friends ? "成为好友后可邀请对战" : viewerInRoom ? "请先离开当前房间" : null);

                if (session.IsSpectator)
                    return new L12OnlinePresence(group.Key, "spectating", null, false, false,
                        isSelf ? "当前账号" : "该玩家正在观战");
                if (room.Game is null)
                    return new L12OnlinePresence(group.Key, "inRoom", null, false, false,
                        isSelf ? "当前账号" : "该玩家正在房间中");

                var canSpectate = !maintenanceActive && !isSelf && !viewerInRoom
                    && room.Options.Spectating != "disabled"
                    && (room.Options.Spectating != "friends" || friends);
                var reason = isSelf ? "当前账号"
                    : maintenanceActive ? currentPolicy.Maintenance.Message
                    : viewerInRoom ? "请先离开当前房间"
                    : room.Options.Spectating == "disabled" ? "该房间禁止观战"
                    : room.Options.Spectating == "friends" && !friends ? "该房间仅限好友观战"
                    : null;
                return new L12OnlinePresence(group.Key, "playing", canSpectate ? room.Code : null,
                    false, canSpectate, reason);
            }, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<OutgoingMessage> InviteFriend(Guid sessionId, string? targetAccountId)
    {
        if (!_sessions.TryGetValue(sessionId, out var sender) || sender.AccountId is null)
            return Error(sessionId, "会话不存在");
        var policy = CaptureOperationsPolicy();
        if (TryOperationsEntryBlock(sessionId, policy, "friendly", out var blocked))
            return blocked;
        if (sender.RoomCode is not null) return Error(sessionId, "请先离开当前房间再邀请好友");
        var targetId = (targetAccountId ?? string.Empty).Trim();
        if (_platform is null || !_platform.AreFriends(sender.AccountId, targetId)) return Error(sessionId, "只能邀请已成为好友的玩家");
        var recipients = _sessions.Values.Where(session => session.Connected && session.AccountId == targetId).ToArray();
        if (recipients.Length == 0) return Error(sessionId, "该好友当前不在线");
        if (recipients.Any(session => session.RoomCode is not null)) return Error(sessionId, "该好友已在房间或对局中");

        var invitation = new FriendInvitation
        {
            Id = Guid.NewGuid().ToString("N"),
            RoomCode = GenerateAvailableRoomCode(),
            FromAccountId = sender.AccountId,
            ToAccountId = targetId,
        };
        _friendInvitations[invitation.Id] = invitation;
        var payload = new
        {
            type = "friendInvitation", invitationId = invitation.Id, invitation.RoomCode,
            fromAccountId = sender.AccountId, fromName = sender.Name,
        };
        return recipients.Select(recipient => new OutgoingMessage(recipient.Id, payload))
            .Append(new OutgoingMessage(sessionId, new
            {
                type = "friendInvitationSent", invitationId = invitation.Id, invitation.RoomCode,
                targetAccountId = targetId, message = $"邀请已发送，预留房间码 {invitation.RoomCode}",
            })).ToArray();
    }

    public IReadOnlyList<OutgoingMessage> ResolveFriendInvitation(Guid sessionId, string? invitationId, bool accept)
    {
        if (!_sessions.TryGetValue(sessionId, out var recipient) || recipient.AccountId is null)
            return Error(sessionId, "会话不存在");
        L12OperationsPolicySnapshot? policy = null;
        if (accept)
        {
            var currentPolicy = CaptureOperationsPolicy();
            if (TryOperationsEntryBlock(sessionId, currentPolicy, "friendly", out var blocked))
                return blocked;
            policy = currentPolicy.ForFriendlyRoom(useCardRestrictions: false, disasterMode: "all");
            if (!TryDefaultPresetIndexes(policy, out _, out _))
                return OperationsBlocked(sessionId, "no_legal_default_preset", "当前没有可用于新房间的合法官方预组");
        }
        var id = (invitationId ?? string.Empty).Trim();
        if (!_friendInvitations.TryRemove(id, out var invitation)
            || invitation.ToAccountId != recipient.AccountId
            || invitation.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-10))
            return Error(sessionId, "邀请不存在或已失效");
        var senders = _sessions.Values.Where(session => session.Connected
            && session.AccountId == invitation.FromAccountId).ToArray();
        if (!accept)
            return senders.Select(sender => new OutgoingMessage(sender.Id,
                    new { type = "friendInvitationRejected", invitationId = id, message = $"{recipient.Name} 拒绝了对战邀请" }))
                .Append(new OutgoingMessage(sessionId, new { type = "friendInvitationResolved", invitationId = id }))
                .ToArray();
        var host = senders.FirstOrDefault(session => session.RoomCode is null);
        if (host is null) return Error(sessionId, "发起方已离线或进入其他房间");
        if (recipient.RoomCode is not null) return Error(sessionId, "你已在其他房间");

        var room = new Room
        {
            Code = invitation.RoomCode,
            OperationsPolicy = policy!,
            Options = NormalizeFriendlyOptions(null, policy!.DefaultRoomConfig),
        };
        if (!_rooms.TryAdd(room.Code, room)) return Error(sessionId, "预留房间码已失效，请重新邀请");
        room.Sessions.Add(host.Id);
        room.Sessions.Add(sessionId);
        host.RoomCode = recipient.RoomCode = room.Code;
        host.PlayerIndex = 0;
        recipient.PlayerIndex = 1;
        TryDefaultPresetIndexes(policy!, out var hostDeckIndex, out var recipientDeckIndex);
        host.SelectedDeckIndex = hostDeckIndex;
        recipient.SelectedDeckIndex = recipientDeckIndex;
        var created = room.Sessions.Select(memberId => new OutgoingMessage(memberId, new
        {
            type = "friendRoomCreated", roomCode = room.Code, hostAccountId = host.AccountId,
            message = "好友已接受邀请，已直接创建房间",
        }));
        return created.Concat(BroadcastRoom(room)).ToArray();
    }

    /// <summary>
    /// 同账号重新握手后立即恢复房间、完整对局快照、待处理 Prompt 与可见信息。
    /// WebSocket 只负责连接；权威状态仍由房间和 L12GameEngine 持有。
    /// </summary>
    public IReadOnlyList<OutgoingMessage> RecoveryState(Guid sessionId)
        => RecoveryStateAsync(sessionId).GetAwaiter().GetResult();

    /// <summary>
    /// 为后台 Bug 记录附加最小权威快照。只记录流程标识和计数，不记录手牌、
    /// 牌库内容或私有选择，既可定位断线/卡死，也不会向反馈接口泄露隐藏信息。
    /// </summary>
    public async Task<L12BugDiagnosticView?> CaptureBugDiagnosticAsync(string? matchId, string? roomCode)
    {
        if (string.IsNullOrWhiteSpace(matchId) && string.IsNullOrWhiteSpace(roomCode)) return null;
        var room = _rooms.Values.FirstOrDefault(candidate =>
            (!string.IsNullOrWhiteSpace(matchId)
                && string.Equals(candidate.Game?.State.MatchId, matchId, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(roomCode)
                && string.Equals(candidate.Code, roomCode, StringComparison.OrdinalIgnoreCase)));
        if (room?.Game is null) return null;

        await room.Gate.WaitAsync();
        try
        {
            var state = room.Game.State;
            return new L12BugDiagnosticView(_utcNow(), state.MatchId, room.Code, state.Phase.ToString(), state.Round,
                state.TurnSerial, state.ActivePlayer, state.Revision, room.CommandSequence,
                state.EffectStack.Concat(state.DeferredEffectStack).Select(item =>
                    $"{item.Trigger}:{item.SourceCardId}:{item.Data.GetValueOrDefault("atomicFlow", "-")}").ToArray(),
                state.PendingPrompts.Select(prompt =>
                    $"{prompt.Kind}:{prompt.Continuation}:{prompt.Data.GetValueOrDefault("action", "-")}").ToArray(),
                state.Events.TakeLast(20).Select(entry => entry.Type).ToArray());
        }
        finally { room.Gate.Release(); }
    }

    public async Task<IReadOnlyList<OutgoingMessage>> RecoveryStateAsync(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)
            || session.RoomCode is null
            || !_rooms.TryGetValue(session.RoomCode, out var room)) return [];
        await room.Gate.WaitAsync();
        try
        {
            if (room.Game is not null)
                await ApplyRankedClockConclusionLockedAsync(room, _utcNow());
            if (room.TournamentId is not null)
            {
                await StartTournamentGameIfReadyLockedAsync(room);
                if (room.Game?.State.Phase == L12Phase.GameOver)
                    await CompleteTournamentRoomGameAsync(room);
            }
            return room.Game is null ? BroadcastRoom(room)
                : BroadcastRoom(room).Concat(BroadcastGame(room)).ToArray();
        }
        finally { room.Gate.Release(); }
    }

    public IReadOnlyList<OutgoingMessage> CreateRoom(Guid sessionId, L12RoomOptions? options = null)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return Error(sessionId, "会话不存在");
        if (session.RoomCode is not null) return Error(sessionId, "已经加入房间");
        var currentPolicy = CaptureOperationsPolicy();
        var normalizedOptions = NormalizeFriendlyOptions(options, currentPolicy.DefaultRoomConfig);
        if (TryOperationsEntryBlock(sessionId, currentPolicy, normalizedOptions.MatchModeId, out var blocked))
            return blocked;
        var policy = currentPolicy.ForFriendlyRoom(normalizedOptions.UseCardRestrictions,
            normalizedOptions.DisasterMode);
        if (!TryDefaultPresetIndexes(policy, out var hostDeckIndex, out _))
            return OperationsBlocked(sessionId, "no_legal_default_preset", "当前没有可用于新房间的合法官方预组");
        Room room;
        do
        {
            room = new Room
            {
                Code = GenerateRoomCode(),
                Options = normalizedOptions,
                OperationsPolicy = policy,
            };
        }
        while (!_rooms.TryAdd(room.Code, room));
        room.Sessions.Add(sessionId);
        session.RoomCode = room.Code;
        session.PlayerIndex = 0;
        session.SelectedDeckIndex = hostDeckIndex;
        return BroadcastRoom(room);
    }

    public IReadOnlyList<OutgoingMessage> UpdateRoomOptions(Guid sessionId, L12RoomOptions? options)
    {
        if (!TryGetMembership(sessionId, out var session, out var room, out var error))
            return Error(sessionId, error);
        if (room.TournamentId is not null)
            return Error(sessionId, "赛事房间已绑定规则快照，不能通过普通房间命令修改");
        if (room.Game is not null) return Error(sessionId, "对局开始后不能修改房间规则");
        if (session.PlayerIndex != 0 || room.Sessions.FirstOrDefault() != sessionId)
            return Error(sessionId, "只有房主可以修改房间规则");

        var currentPolicy = CaptureOperationsPolicy();
        var normalized = NormalizeFriendlyOptions(options, currentPolicy.DefaultRoomConfig);
        if (TryOperationsEntryBlock(sessionId, currentPolicy, normalized.MatchModeId, out var blocked))
            return blocked;
        var scopedPolicy = currentPolicy.ForFriendlyRoom(normalized.UseCardRestrictions, normalized.DisasterMode);
        if (!TryDefaultPresetIndexes(scopedPolicy, out _, out _))
            return OperationsBlocked(sessionId, "no_legal_default_preset", "当前规则下没有可用于房间的合法官方预组");

        room.Options = normalized;
        room.OperationsPolicy = scopedPolicy;
        for (var index = 0; index < room.Ready.Length; index++) room.Ready[index] = false;
        return BroadcastRoom(room);
    }

    public async Task<IReadOnlyList<OutgoingMessage>> CreateSandboxAsync(Guid sessionId, L12SandboxRequest? request)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return Error(sessionId, "会话不存在");
        if (session.RoomCode is not null) return Error(sessionId, "已经加入房间");
        request ??= new L12SandboxRequest();
        var currentPolicy = CaptureOperationsPolicy();
        if (currentPolicy.IsMaintenanceActive(DateTimeOffset.UtcNow))
            return MaintenanceBlocked(sessionId, currentPolicy);
        var disasterMode = (request.DisasterMode ?? string.Empty).Trim().ToLowerInvariant();
        if (disasterMode is not ("all" or "random" or "custom" or "none"))
            return Error(sessionId, "沙盒天灾模式仅支持全部、随机、自定或不使用天灾", "sandboxRejected");
        var policy = currentPolicy.ForSandbox(disasterMode);
        if (!TryDefaultPresetIndexes(policy, out var playerDeckIndex, out var opponentDeckIndex))
            return OperationsBlocked(sessionId, "no_legal_default_preset", "当前没有可用于新沙盒的合法官方预组");

        L12PresetDeckDefinition playerDeck;
        L12PresetDeckDefinition opponentDeck;
        if (request.PlayerDeck is not null)
        {
            if (!L12DeckValidator.TryValidate(_catalog, request.PlayerDeck, out playerDeck!, out var playerError,
                    policy.CardRestrictions))
                return Error(sessionId, $"我方牌库无效：{playerError}", "deckRejected");
        }
        else playerDeck = _catalog.DeckAt(playerDeckIndex);
        if (request.OpponentDeck is not null)
        {
            if (!L12DeckValidator.TryValidate(_catalog, request.OpponentDeck, out opponentDeck!, out var opponentError,
                    policy.CardRestrictions))
                return Error(sessionId, $"对手牌库无效：{opponentError}", "deckRejected");
        }
        else opponentDeck = _catalog.DeckAt(opponentDeckIndex);

        Room room;
        do
        {
            room = new Room
            {
                Code = GenerateRoomCode(),
                IsSandbox = true,
                GmControllerSessionId = sessionId,
                OperationsPolicy = policy,
                Options = new L12RoomOptions
                {
                    MatchModeId = "sandbox",
                    Spectating = "disabled",
                    HandVisibility = "public",
                    DisasterMode = disasterMode,
                    UseCardRestrictions = false,
                },
            };
        }
        while (!_rooms.TryAdd(room.Code, room));

        var opponentId = Guid.NewGuid();
        var opponent = new Session
        {
            Id = opponentId,
            Name = "测试对手",
            RoomCode = room.Code,
            PlayerIndex = 1,
            SelectedDeckIndex = opponentDeckIndex,
            CustomDeck = opponentDeck,
            IsVirtual = true,
        };
        _sessions[opponentId] = opponent;
        room.Sessions.Add(sessionId);
        room.Sessions.Add(opponentId);
        room.Ready[0] = room.Ready[1] = true;
        session.RoomCode = room.Code;
        session.PlayerIndex = 0;
        session.SelectedDeckIndex = playerDeckIndex;
        session.CustomDeck = playerDeck;

        room.Game = new L12GameEngine(
            _catalog, Guid.NewGuid().ToString("N"), room.Code, Random.Shared.Next(),
            [session.Name, opponent.Name], [playerDeck, opponentDeck], skipPreparation: true,
            disasterMode: room.Options.DisasterMode, operationsPolicy: room.OperationsPolicy);
        room.Game.InitializeGmDisasters();
        // 沙盒状态只保留在内存和 GM 导出中，不进入玩家对局记录数据库。
        foreach (var playerIndex in new[] { 0, 1 })
        {
            var bootstrap = new L12Command("mulligan", CardInstanceIds: []);
            var result = room.Game.Handle(playerIndex, bootstrap);
            room.CommandSequence++;
            _ = result;
        }
        return BroadcastRoom(room).Concat(BroadcastGame(room)).ToArray();
    }

    public IReadOnlyList<OutgoingMessage> SpectateRoom(Guid sessionId, string? roomCode)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return Error(sessionId, "会话不存在");
        if (session.RoomCode is not null) return Error(sessionId, "已经加入房间");
        var currentPolicy = CaptureOperationsPolicy();
        if (currentPolicy.IsMaintenanceActive(DateTimeOffset.UtcNow))
            return MaintenanceBlocked(sessionId, currentPolicy);
        var code = (roomCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!_rooms.TryGetValue(code, out var room)) return Error(sessionId, "房间不存在");
        if (room.Game is null) return Error(sessionId, "对局尚未开始");
        if (room.Options.Spectating == "disabled") return Error(sessionId, "该房间禁止观战");
        if (room.Options.Spectating == "friends")
        {
            if (session.AccountId is null) return Error(sessionId, "请先登录账号");
            var playerAccounts = room.Sessions.Select(id => _sessions[id].AccountId).Where(id => id is not null).ToArray();
            if (_platform is null || !playerAccounts.Any(accountId => _platform.AreFriends(session.AccountId, accountId!)))
                return Error(sessionId, "该房间仅限参赛玩家的好友观战");
        }
        lock (room.Spectators)
        {
            if (room.Closed || !_rooms.TryGetValue(code, out var currentRoom)
                || !ReferenceEquals(room, currentRoom))
                return Error(sessionId, "房间已经关闭");
            room.Spectators.Add(sessionId);
            session.RoomCode = room.Code;
            session.PlayerIndex = null;
            session.IsSpectator = true;
        }
        return [new OutgoingMessage(sessionId, new
        {
            type = "gameState",
            spectating = true,
            state = room.Game.SnapshotForSpectator(),
        })];
    }

    public IReadOnlyList<OutgoingMessage> JoinRoom(Guid sessionId, string? roomCode)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return Error(sessionId, "会话不存在");
        if (session.RoomCode is not null) return Error(sessionId, "已经加入房间");
        var code = (roomCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!_rooms.TryGetValue(code, out var room)) return Error(sessionId, "房间不存在");
        var currentPolicy = CaptureOperationsPolicy();
        if (TryOperationsEntryBlock(sessionId, currentPolicy, room.Options.MatchModeId, out var blocked,
                room.OperationsPolicy))
            return blocked;
        if (!TryDefaultPresetIndexes(room.OperationsPolicy, out _, out var joiningDeckIndex))
            return OperationsBlocked(sessionId, "no_legal_default_preset", "该房间没有可用的合法官方预组");
        lock (room.Sessions)
        {
            if (room.Sessions.Count >= 2) return Error(sessionId, "房间已满");
            room.Sessions.Add(sessionId);
            session.RoomCode = room.Code;
            session.PlayerIndex = 1;
            session.SelectedDeckIndex = joiningDeckIndex;
        }
        return BroadcastRoom(room);
    }

    /// <summary>
    /// 赛事玩家不输入房间码；服务端按已登录账号与权威配对直接放入固定席位。
    /// 房间首次进入时由赛事快照创建，牌库和规则不再接受客户端选择。
    /// </summary>
    public async Task<IReadOnlyList<OutgoingMessage>> EnterTournamentMatchAsync(Guid sessionId,
        string? tournamentId, string? matchId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.AccountId is null)
            return Error(sessionId, "请先登录账号");
        if (_platform is null) return Error(sessionId, "赛事房间编排服务不可用");
        L12TournamentRoomAssignment assignment;
        try
        {
            assignment = _platform.TournamentRoomAssignment(session.AccountId,
                tournamentId?.Trim() ?? string.Empty, matchId?.Trim() ?? string.Empty, spectate: false);
        }
        catch (Exception error) when (error is L12TournamentScopeException or L12TournamentVersionConflictException
                                      or KeyNotFoundException or ArgumentException)
        {
            return Error(sessionId, error.Message, "tournamentRoomRejected");
        }
        if (!L12DeckValidator.TryValidatePreset(_catalog, assignment.PlayerA.Deck, out var deckError,
                assignment.OperationsPolicy.CardRestrictions)
            || !L12DeckValidator.TryValidatePreset(_catalog, assignment.PlayerB.Deck, out deckError,
                assignment.OperationsPolicy.CardRestrictions))
            return Error(sessionId, $"赛事牌库快照无效：{deckError}", "tournamentRoomRejected");
        if (session.RoomCode is not null)
        {
            if (string.Equals(session.RoomCode, assignment.RoomCode, StringComparison.OrdinalIgnoreCase)
                && _rooms.TryGetValue(session.RoomCode, out var current))
            {
                await current.Gate.WaitAsync();
                try
                {
                    await StartTournamentGameIfReadyLockedAsync(current);
                    if (current.Game?.State.Phase == L12Phase.GameOver)
                        await CompleteTournamentRoomGameAsync(current);
                    return current.Game is null ? BroadcastRoom(current)
                        : BroadcastRoom(current).Concat(BroadcastGame(current)).ToArray();
                }
                finally { current.Gate.Release(); }
            }
            return Error(sessionId, "请先离开当前房间", "tournamentRoomRejected");
        }

        Room room;
        lock (_tournamentRoomGate)
        {
            if (!_rooms.TryGetValue(assignment.RoomCode, out room!))
            {
                room = CreateTournamentRoom(assignment);
                if (!_rooms.TryAdd(room.Code, room)) room = _rooms[room.Code];
            }
            if (room.TournamentId != assignment.TournamentId
                || room.TournamentMatchId != assignment.MatchId
                || room.TournamentRulesHash != assignment.RulesHash)
                return Error(sessionId, "赛事房间绑定冲突", "tournamentRoomRejected");
        }

        await room.Gate.WaitAsync();
        try
        {
            var playerIndex = assignment.PlayerA.AccountId == session.AccountId ? 0 : 1;
            var occupiedId = room.Sessions[playerIndex];
            if (_sessions.TryGetValue(occupiedId, out var occupied) && !occupied.IsVirtual
                && occupied.Connected && occupiedId != sessionId)
                return Error(sessionId, "该账号已在本桌连接", "tournamentRoomRejected");
            if (occupiedId != sessionId)
            {
                room.Sessions[playerIndex] = sessionId;
                if (occupied is not null) ClearRoomMembership(occupied);
                if (occupied?.IsVirtual == true) _sessions.TryRemove(occupiedId, out _);
            }
            var player = playerIndex == 0 ? assignment.PlayerA : assignment.PlayerB;
            session.RoomCode = room.Code;
            session.PlayerIndex = playerIndex;
            session.IsSpectator = false;
            session.CustomDeck = player.Deck;
            room.Ready[playerIndex] = true;
            await StartTournamentGameIfReadyLockedAsync(room);
            return room.Game is null ? BroadcastRoom(room)
                : BroadcastRoom(room).Concat(BroadcastGame(room)).ToArray();
        }
        finally { room.Gate.Release(); }
    }

    public IReadOnlyList<OutgoingMessage> SpectateTournamentMatch(Guid sessionId, string? tournamentId,
        string? matchId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.AccountId is null)
            return Error(sessionId, "请先登录账号");
        if (session.RoomCode is not null) return Error(sessionId, "请先离开当前房间");
        if (_platform is null) return Error(sessionId, "赛事房间编排服务不可用");
        L12TournamentRoomAssignment assignment;
        try
        {
            assignment = _platform.TournamentRoomAssignment(session.AccountId,
                tournamentId?.Trim() ?? string.Empty, matchId?.Trim() ?? string.Empty, spectate: true);
        }
        catch (Exception error) when (error is L12TournamentScopeException or L12TournamentVersionConflictException
                                      or KeyNotFoundException or ArgumentException)
        {
            return Error(sessionId, error.Message, "tournamentRoomRejected");
        }
        if (!_rooms.TryGetValue(assignment.RoomCode, out var room) || room.Game is null)
            return Error(sessionId, "本桌对局尚未开始", "tournamentRoomRejected");
        if (room.TournamentId != assignment.TournamentId || room.TournamentMatchId != assignment.MatchId)
            return Error(sessionId, "赛事房间绑定冲突", "tournamentRoomRejected");
        lock (room.Spectators)
        {
            if (!room.Spectators.Contains(sessionId)) room.Spectators.Add(sessionId);
            session.RoomCode = room.Code;
            session.PlayerIndex = null;
            session.IsSpectator = true;
        }
        return [new OutgoingMessage(sessionId, new
        {
            type = "gameState", spectating = true, gmEnabled = false,
            tournamentId = room.TournamentId, tournamentCode = room.TournamentCode,
            tournamentMatchId = room.TournamentMatchId,
            state = room.Game.SnapshotForSpectator(),
        })];
    }

    private Room CreateTournamentRoom(L12TournamentRoomAssignment assignment)
    {
        var room = new Room
        {
            Code = assignment.RoomCode,
            TournamentId = assignment.TournamentId,
            TournamentCode = assignment.TournamentCode,
            TournamentMatchId = assignment.MatchId,
            TournamentRulesHash = assignment.RulesHash,
            OperationsPolicy = assignment.OperationsPolicy,
            Options = new L12RoomOptions
            {
                MatchModeId = "tournament",
                Spectating = "disabled",
                HandVisibility = "request",
                DisasterMode = assignment.DisasterMode,
                UseCardRestrictions = true,
            },
        };
        foreach (var item in new[] { assignment.PlayerA, assignment.PlayerB }.Select((player, index) => (player, index)))
        {
            var virtualId = Guid.NewGuid();
            _sessions[virtualId] = new Session
            {
                Id = virtualId,
                AccountId = item.player.AccountId,
                Name = item.player.Username,
                RoomCode = room.Code,
                PlayerIndex = item.index,
                CustomDeck = item.player.Deck,
                IsVirtual = true,
                Connected = false,
            };
            room.Sessions.Add(virtualId);
            room.Ready[item.index] = true;
        }
        return room;
    }

    private async Task StartTournamentGameIfReadyLockedAsync(Room room)
    {
        if (room.Game is not null || room.TournamentId is null
            || !room.Sessions.All(id => _sessions.TryGetValue(id, out var member)
                && !member.IsVirtual && member.Connected)) return;
        var members = room.Sessions.Select(id => _sessions[id]).ToArray();
        var game = new L12GameEngine(_catalog, Guid.NewGuid().ToString("N"), room.Code,
            Random.Shared.Next(), members.Select(member => member.Name).ToArray(),
            members.Select(SelectedDeck).ToArray(), disasterMode: room.Options.DisasterMode,
            operationsPolicy: room.OperationsPolicy);
        // 只有对局记录成功落库后才发布可操作引擎；失败时下一次进入/恢复可安全重试。
        await _recorder.StartAsync(game, "tournament", members[0].AccountId, members[1].AccountId,
            members.Select(SelectedDeck).ToArray());
        room.Game = game;
    }

    public IReadOnlyList<OutgoingMessage> SelectDeck(Guid sessionId, int deckIndex)
    {
        if (!TryGetMembership(sessionId, out var session, out var room, out var error)) return Error(sessionId, error);
        if (room.TournamentId is not null) return Error(sessionId, "赛事房间已锁定报名牌库", "deckRejected");
        if (room.Game is not null) return Error(sessionId, "对局已经开始");
        if (deckIndex < 0 || deckIndex >= _catalog.PresetDecks.Count) return Error(sessionId, "无效的预组");
        if (!IsPresetAllowed(room.OperationsPolicy, deckIndex, out error))
            return Error(sessionId, error, "deckRejected");
        session.SelectedDeckIndex = deckIndex;
        session.CustomDeck = null;
        room.Ready[session.PlayerIndex!.Value] = false;
        return BroadcastRoom(room);
    }

    public IReadOnlyList<OutgoingMessage> SelectCustomDeck(Guid sessionId, L12CustomDeckSubmission submission)
    {
        if (!TryGetMembership(sessionId, out var session, out var room, out var error)) return Error(sessionId, error);
        if (room.TournamentId is not null) return Error(sessionId, "赛事房间已锁定报名牌库", "deckRejected");
        if (room.Game is not null) return Error(sessionId, "对局已经开始");
        if (!L12DeckValidator.TryValidate(_catalog, submission, out var deck, out error,
                room.OperationsPolicy.CardRestrictions))
            return Error(sessionId, error, "deckRejected");
        session.CustomDeck = deck;
        room.Ready[session.PlayerIndex!.Value] = false;
        return BroadcastRoom(room);
    }

    public async Task<IReadOnlyList<OutgoingMessage>> SetReadyAsync(Guid sessionId, bool ready)
    {
        if (!TryGetMembership(sessionId, out var session, out var room, out var error)) return Error(sessionId, error);
        if (room.TournamentId is not null)
            return Error(sessionId, "赛事房间由轮次签到与进入身份自动准备");
        await room.Gate.WaitAsync();
        try
        {
            if (room.Game?.State.Phase == L12Phase.GameOver)
            {
                // 完成记录已经在对局进入 GameOver 时落盘；这里只重置房间内的赛局槽与准备状态，
                // 保留成员、房号、牌库和固定运营策略，供双方重新准备开启全新 match。
                room.Game = null;
                room.CommandSequence = 0;
                Array.Fill(room.Ready, false);
            }
            else if (room.Game is not null) return Error(sessionId, "对局已经开始");
            if (ready)
            {
                var currentPolicy = CaptureOperationsPolicy();
                if (TryOperationsEntryBlock(sessionId, currentPolicy, room.Options.MatchModeId, out var blocked,
                        room.OperationsPolicy))
                    return blocked;
                foreach (var memberId in room.Sessions)
                {
                    var member = _sessions[memberId];
                    if (!L12DeckValidator.TryValidatePreset(_catalog, SelectedDeck(member), out var deckError,
                            room.OperationsPolicy.CardRestrictions))
                        return Error(sessionId, $"{member.Name} 的牌库不符合该房间固定规则：{deckError}",
                            "deckRejected");
                }
            }
            room.Ready[session.PlayerIndex!.Value] = ready;
            if (room.Sessions.Count == 2 && room.Ready.All(value => value))
            {
                var playerNames = room.Sessions.Select(id => _sessions[id].Name).ToArray();
                var selectedDecks = room.Sessions.Select(id => SelectedDeck(_sessions[id])).ToArray();
                room.Game = new L12GameEngine(
                    _catalog, Guid.NewGuid().ToString("N"), room.Code, Random.Shared.Next(),
                    playerNames, selectedDecks,
                    disasterMode: room.Options.DisasterMode, operationsPolicy: room.OperationsPolicy);
                InitializeRankedClock(room);
                var startedMembers = room.Sessions.Select(id => _sessions[id]).ToArray();
                await _recorder.StartAsync(room.Game, room.Options.MatchModeId,
                    startedMembers[0].AccountId, startedMembers[1].AccountId, selectedDecks);
            }
            return room.Game is null ? BroadcastRoom(room) : BroadcastGame(room);
        }
        finally { room.Gate.Release(); }
    }

    public async Task<IReadOnlyList<OutgoingMessage>> HandleActionAsync(Guid sessionId, JsonElement commandElement)
    {
        if (!TryGetMembership(sessionId, out var session, out var room, out var error)) return Error(sessionId, error);
        await room.Gate.WaitAsync();
        try
        {
            if (room.Game is null) return Error(sessionId, "对局尚未开始");
            if (await ApplyRankedClockConclusionLockedAsync(room, _utcNow()))
                return BroadcastGame(room);
            if (room.Game.State.Phase == L12Phase.GameOver)
            {
                var reportError = await CompleteTournamentRoomGameAsync(room);
                var completed = BroadcastGame(room).ToList();
                if (reportError is not null) completed.AddRange(Error(sessionId, reportError, "tournamentResultPending"));
                return completed;
            }
            L12Command? command;
            try
            {
                command = commandElement.Deserialize<L12Command>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException) { return Error(sessionId, "操作格式错误"); }
            if (command is null || string.IsNullOrWhiteSpace(command.Type)) return Error(sessionId, "缺少操作类型");
            var meaningful = IsMeaningfulRankedCommand(room.Game, command);
            var result = room.Game.Handle(session.PlayerIndex!.Value, command);
            room.CommandSequence++;
            await _recorder.AppendAsync(room.Game, room.CommandSequence, session.PlayerIndex.Value, commandElement.GetRawText(), result);
            if (!result.Accepted) return Error(sessionId, result.Error ?? "操作被拒绝", "actionRejected");
            if (meaningful) room.MeaningfulCommandCount++;
            RefreshRankedClockActorsLocked(room, _utcNow(), session.PlayerIndex.Value);
            if (room.Game.State.Phase != L12Phase.GameOver) return BroadcastGame(room);
            var errorMessage = await CompleteTournamentRoomGameAsync(room);
            var messages = BroadcastGame(room).ToList();
            if (errorMessage is not null)
                messages.AddRange(Error(sessionId, errorMessage, "tournamentResultPending"));
            return messages;
        }
        finally { room.Gate.Release(); }
    }

    public async Task<IReadOnlyList<OutgoingMessage>> HandleGmActionAsync(Guid sessionId, JsonElement commandElement)
    {
        if (!TryGetMembership(sessionId, out var session, out var room, out var error)) return Error(sessionId, error);
        if (!room.IsSandbox || room.GmControllerSessionId != sessionId)
            return Error(sessionId, "GM 指令只允许由单人测试沙盒的创建者执行", "actionRejected");
        await room.Gate.WaitAsync();
        try
        {
            if (room.Game is null) return Error(sessionId, "沙盒对局尚未开始");
            L12GmCommand? command;
            try
            {
                command = commandElement.Deserialize<L12GmCommand>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException) { return Error(sessionId, "GM 操作格式错误", "actionRejected"); }
            if (command is null || string.IsNullOrWhiteSpace(command.Type))
                return Error(sessionId, "缺少 GM 操作类型", "actionRejected");
            var result = room.Game.HandleGm(command);
            room.CommandSequence++;
            // 沙盒不写入正式对局记录。
            if (!result.Accepted) return Error(sessionId, result.Error ?? "GM 操作被拒绝", "actionRejected");
            return BroadcastGame(room);
        }
        finally { room.Gate.Release(); }
    }

    /// <summary>
    /// 单人测试沙盒的规则内操作通道。沙盒创建者可以明确指定由哪一方执行普通
    /// L12Command；正式房间、观战者和虚拟对手均不能使用，避免把 GM 权限混入
    /// 普通 gameAction 或复制一套结算规则。
    /// </summary>
    public async Task<IReadOnlyList<OutgoingMessage>> HandleSandboxActionAsync(
        Guid sessionId, int actingPlayerIndex, JsonElement commandElement)
    {
        if (!TryGetMembership(sessionId, out _, out var room, out var error)) return Error(sessionId, error);
        if (!room.IsSandbox || room.GmControllerSessionId != sessionId)
            return Error(sessionId, "沙盒代行操作只允许由单人测试沙盒的创建者执行", "actionRejected");
        if (actingPlayerIndex is < 0 or > 1)
            return Error(sessionId, "沙盒代行玩家无效", "actionRejected");

        await room.Gate.WaitAsync();
        try
        {
            if (room.Game is null) return Error(sessionId, "沙盒对局尚未开始");
            L12Command? command;
            try
            {
                command = commandElement.Deserialize<L12Command>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException) { return Error(sessionId, "沙盒操作格式错误", "actionRejected"); }
            if (command is null || string.IsNullOrWhiteSpace(command.Type))
                return Error(sessionId, "缺少沙盒操作类型", "actionRejected");

            var result = room.Game.Handle(actingPlayerIndex, command);
            room.CommandSequence++;
            // 沙盒不写入正式对局记录。
            if (!result.Accepted) return Error(sessionId, result.Error ?? "沙盒操作被拒绝", "actionRejected");
            if (room.Game.State.Phase != L12Phase.GameOver) return BroadcastGame(room);
            var errorMessage = await CompleteTournamentRoomGameAsync(room);
            var messages = BroadcastGame(room).ToList();
            if (errorMessage is not null) messages.AddRange(Error(sessionId, errorMessage, "tournamentResultPending"));
            return messages;
        }
        finally { room.Gate.Release(); }
    }

    public IReadOnlyList<OutgoingMessage> Disconnect(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return [];
        lock (_matchmakingGate) _matchmaking.RemoveAll(entry => entry.SessionId == sessionId);
        if (session.RoomCode is null || !_rooms.TryGetValue(session.RoomCode, out var room))
        {
            if (!session.Connected) return [];
            session.Connected = false;
            session.DisconnectedAt ??= _utcNow();
            return [];
        }
        room.Gate.Wait();
        try
        {
            if (!session.Connected) return [];
            var now = _utcNow();
            SettleRankedClockLocked(room, now);
            session.Connected = false;
            session.DisconnectedAt ??= now;
            RefreshRankedClockActorsLocked(room, now);
            return BroadcastRoom(room).Concat(room.Game is null ? [] : BroadcastGame(room)).ToArray();
        }
        finally { room.Gate.Release(); }
    }

    public IReadOnlyList<OutgoingMessage> LeaveRoom(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var currentSession)) return Error(sessionId, "会话不存在");
        lock (_matchmakingGate) _matchmaking.RemoveAll(entry => entry.SessionId == sessionId);
        if (currentSession.IsSpectator)
        {
            if (currentSession.RoomCode is { } spectatorRoomCode
                && _rooms.TryGetValue(spectatorRoomCode, out var spectatorRoom))
            {
                lock (spectatorRoom.Spectators) spectatorRoom.Spectators.Remove(sessionId);
            }
            ClearRoomMembership(currentSession);
            return [new OutgoingMessage(sessionId, new
            {
                type = "roomLeft",
                message = "已退出观战并返回大厅",
            })];
        }
        if (!TryGetMembership(sessionId, out var session, out var room, out var error)) return Error(sessionId, error);
        if (!room.IsSandbox && room.Game is not null && room.Game.State.Phase != L12Phase.GameOver)
            return Error(sessionId, "对局已开始，请在对局内投降后离开");

        if (room.TournamentId is not null)
        {
            var tournamentPlayerIndex = session.PlayerIndex!.Value;
            var virtualId = Guid.NewGuid();
            _sessions[virtualId] = new Session
            {
                Id = virtualId,
                AccountId = session.AccountId,
                Name = session.Name,
                RoomCode = room.Code,
                PlayerIndex = tournamentPlayerIndex,
                CustomDeck = session.CustomDeck,
                IsVirtual = true,
                Connected = false,
            };
            room.Sessions[tournamentPlayerIndex] = virtualId;
            ClearRoomMembership(session);
            return [new OutgoingMessage(sessionId, new
            {
                type = "roomLeft", message = "已离开赛事房间，可在赛事中重新进入",
            })];
        }

        var playerIndex = session.PlayerIndex!.Value;
        if (playerIndex == 0)
        {
            var messages = new List<OutgoingMessage>();
            Guid[] spectators;
            lock (room.Spectators)
            {
                room.Closed = true;
                spectators = [.. room.Spectators];
                room.Spectators.Clear();
                _rooms.TryRemove(room.Code, out _);
            }
            foreach (var id in room.Sessions.ToArray())
            {
                if (!_sessions.TryGetValue(id, out var member)) continue;
                ClearRoomMembership(member);
                messages.Add(new OutgoingMessage(id, new
                {
                    type = id == sessionId ? "roomLeft" : "roomClosed",
                    message = id == sessionId ? "房间已关闭" : "房主已关闭房间",
                }));
                if (member.IsVirtual) _sessions.TryRemove(id, out _);
            }
            foreach (var id in spectators)
            {
                if (!_sessions.TryGetValue(id, out var spectator)) continue;
                ClearRoomMembership(spectator);
                if (spectator.Connected)
                    messages.Add(new OutgoingMessage(id, new
                    {
                        type = "roomClosed",
                        message = "所观战的房间已关闭",
                    }));
            }
            return messages;
        }

        room.Sessions.Remove(sessionId);
        room.Ready[playerIndex] = false;
        ClearRoomMembership(session);
        return new[] { new OutgoingMessage(sessionId, new { type = "roomLeft", message = "已离开房间" }) }
            .Concat(BroadcastRoom(room)).ToArray();
    }

    private static void ClearRoomMembership(Session session)
    {
        session.RoomCode = null;
        session.PlayerIndex = null;
        session.CustomDeck = null;
        session.IsSpectator = false;
    }

    private IReadOnlyList<OutgoingMessage> BroadcastRoom(Room room)
    {
        var decks = _catalog.PresetDecks.Select((deck, index) => (deck, index))
            .Where(item => IsPresetAllowed(room.OperationsPolicy, item.index, out _))
            .Select(item => new
            {
                item.index, item.deck.Name, item.deck.MasterId,
                masterName = _catalog.Cards[item.deck.MasterId].NameZh,
                faction = _catalog.Cards[item.deck.MasterId].Faction,
            }).ToArray();
        return room.Sessions.Select(id =>
        {
            var viewer = _sessions[id];
            var players = room.Sessions.Select(memberId =>
            {
                var member = _sessions[memberId];
                var deck = SelectedDeck(member);
                var master = _catalog.Cards[deck.MasterId];
                return new
                {
                    member.Name, playerIndex = member.PlayerIndex, member.Connected,
                    ready = room.Ready[member.PlayerIndex!.Value],
                    deckIndex = member.CustomDeck is null ? member.SelectedDeckIndex : -1,
                    customDeck = member.CustomDeck is not null,
                    deckName = member.PlayerIndex == viewer.PlayerIndex ? deck.Name : string.Empty,
                    masterName = master.NameZh, faction = master.Faction,
                };
            }).ToArray();
            return new OutgoingMessage(id, new
            {
                type = "roomState", roomCode = room.Code, yourPlayerIndex = viewer.PlayerIndex,
                players, decks, options = room.Options, started = room.Game is not null, sandbox = room.IsSandbox,
                tournamentId = room.TournamentId, tournamentCode = room.TournamentCode,
                tournamentMatchId = room.TournamentMatchId,
                operationsPolicyVersion = room.OperationsPolicy.Version,
            });
        }).ToArray();
    }

    private IReadOnlyList<OutgoingMessage> BroadcastGame(Room room)
    {
        Guid[] spectators;
        lock (room.Spectators) spectators = [.. room.Spectators];
        var rankedClock = RankedClockView(room);
        var playerBadges = RankedPlayerBadges(room);
        return room.Sessions.Select(id => new OutgoingMessage(id, new
        {
            type = "gameState", state = room.IsSandbox && room.GmControllerSessionId == id
                ? room.Game!.SnapshotForGm(_sessions[id].PlayerIndex!.Value)
                : room.Game!.SnapshotFor(_sessions[id].PlayerIndex!.Value),
            gmEnabled = room.IsSandbox && room.GmControllerSessionId == id,
            tournamentId = room.TournamentId, tournamentCode = room.TournamentCode,
            tournamentMatchId = room.TournamentMatchId,
            playerBadges,
            rankedClock,
            rankedSettlement = room.Options.MatchModeId == "ranked" && _platform is not null
                ? _platform.RankedSettlement(room.Game!.State.MatchId, _sessions[id].AccountId ?? string.Empty) : null,
        })).Concat(spectators.Select(id => new OutgoingMessage(id, new
        {
            type = "gameState", spectating = true, gmEnabled = false,
            tournamentId = room.TournamentId, tournamentCode = room.TournamentCode,
            tournamentMatchId = room.TournamentMatchId, playerBadges, rankedClock,
            state = room.Game!.SnapshotForSpectator(),
        }))).ToArray();
    }

    private IReadOnlyList<L12RankedBattleIdentityView> RankedPlayerBadges(Room room)
    {
        if (_platform is null) return [];
        var result = new List<L12RankedBattleIdentityView>();
        foreach (var id in room.Sessions)
        {
            if (!_sessions.TryGetValue(id, out var session) || session.PlayerIndex is null
                || string.IsNullOrWhiteSpace(session.AccountId)) continue;
            try { result.Add(_platform.RankedBattleIdentity(session.AccountId, session.PlayerIndex.Value)); }
            catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException) { }
        }
        return result;
    }

    private async Task<string?> CompleteTournamentRoomGameAsync(Room room)
    {
        if (room.Game is null || room.Game.State.Phase != L12Phase.GameOver) return null;
        if (!room.IsSandbox && !room.CompletionRecorded)
        {
            try
            {
                await _recorder.CompleteAsync(room.Game);
                room.CompletionRecorded = true;
            }
            catch (Exception error)
            {
                return $"对局记录结束写入待重试：{error.Message}";
            }
        }
        if (room.Options.MatchModeId == "ranked" && !room.RankedResultReported)
        {
            if (_platform is null) return "排位结算服务不可用，已保留对局记录待重试";
            var members = room.Sessions.Select(id => _sessions[id]).ToArray();
            if (members.Length != 2 || members.Any(member => string.IsNullOrWhiteSpace(member.AccountId)))
                return "排位结算缺少玩家账号，已保留对局记录待重试";
            var integrity = new L12RankedIntegrityContext(room.StartedAt, _utcNow(),
                room.MeaningfulCommandCount, RankedConclusionKind(room),
                members[0].IntegrityClientKey, members[1].IntegrityClientKey);
            try
            {
                if (room.Game.State.Winner is { } rankedWinner)
                    _platform.SettleRankedMatch(room.Game.State.MatchId, members[0].AccountId!,
                        members[1].AccountId!, rankedWinner, SelectedDeck(members[0]).MasterId,
                        SelectedDeck(members[1]).MasterId, integrity);
                else
                    _platform.RecordInvalidRankedMatch(room.Game.State.MatchId, members[0].AccountId!,
                        members[1].AccountId!, SelectedDeck(members[0]).MasterId,
                        SelectedDeck(members[1]).MasterId, integrity);
                room.RankedResultReported = true;
            }
            catch (Exception error)
            {
                return $"排位结算待重试：{error.Message}";
            }
        }
        if (room.TournamentId is null || room.TournamentMatchId is null || room.TournamentResultReported)
            return null;
        if (_platform is null) return "赛事赛果回写服务不可用，已保留对局记录待重试";
        if (room.Game.State.Winner is not { } winner) return "对局已结束但缺少胜者，已保留记录待裁决";
        try
        {
            _platform.RecordTournamentGameResult(room.TournamentId, room.TournamentMatchId,
                room.Game.State.MatchId, winner);
            room.TournamentResultReported = true;
            return null;
        }
        catch (Exception error)
        {
            return $"赛果回写待重试：{error.Message}";
        }
    }

    private static bool IsMeaningfulRankedCommand(L12GameEngine game, L12Command command)
    {
        if (command.Type is "playCard" or "attack" or "resolveDefense" or "move" or "cavalryMove"
            or "activateAbility" or "flipHidden") return true;
        if (command.Type != "resolvePrompt" || string.IsNullOrWhiteSpace(command.PromptId)) return false;
        var prompt = game.State.PendingPrompts.FirstOrDefault(item => item.PromptId == command.PromptId);
        return prompt is not null && !string.Equals(prompt.Continuation, "setup-initiative",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string RankedConclusionKind(Room room)
    {
        if (!string.IsNullOrWhiteSpace(room.RankedClock?.ConclusionKind))
            return room.RankedClock.ConclusionKind!;
        var reason = room.Game?.State.WinnerReason ?? string.Empty;
        if (reason.Contains("投降", StringComparison.OrdinalIgnoreCase)) return "surrender";
        return "normal";
    }

    private L12PresetDeckDefinition SelectedDeck(Session session)
        => session.CustomDeck ?? _catalog.DeckAt(session.SelectedDeckIndex);

    private bool TryGetMembership(Guid sessionId, out Session session, out Room room, out string error)
    {
        room = null!;
        if (!_sessions.TryGetValue(sessionId, out session!)) { error = "会话不存在"; return false; }
        if (session.IsSpectator || session.PlayerIndex is null) { error = "观战者不能执行对局操作"; return false; }
        if (session.RoomCode is null || !_rooms.TryGetValue(session.RoomCode, out room!))
        { error = "尚未加入房间"; return false; }
        error = string.Empty;
        return true;
    }

    private static IReadOnlyList<OutgoingMessage> Error(Guid sessionId, string message, string type = "error")
        => [new OutgoingMessage(sessionId, new { type, message })];

    private static IReadOnlyList<OutgoingMessage> MatchmakingError(Guid sessionId, string message)
        => Error(sessionId, message, "matchmakingRejected");

    private static IReadOnlyList<OutgoingMessage> OperationsBlocked(Guid sessionId, string code, string message)
        => [new OutgoingMessage(sessionId, new { type = "operationsBlocked", code, message })];

    private static IReadOnlyList<OutgoingMessage> MaintenanceBlocked(Guid sessionId,
        L12OperationsPolicySnapshot policy)
        => OperationsBlocked(sessionId, "maintenance_active",
            string.IsNullOrWhiteSpace(policy.Maintenance.Message) ? "系统维护中，暂不接受新的对局入口" : policy.Maintenance.Message);

    private bool TryOperationsEntryBlock(Guid sessionId, L12OperationsPolicySnapshot policy,
        string? matchModeId, out IReadOnlyList<OutgoingMessage> blocked,
        L12OperationsPolicySnapshot? pinnedRoomPolicy = null)
    {
        if (policy.IsMaintenanceActive(DateTimeOffset.UtcNow))
        {
            blocked = MaintenanceBlocked(sessionId, policy);
            return true;
        }
        if (!string.Equals(matchModeId, "friendly", StringComparison.OrdinalIgnoreCase)
            && !(pinnedRoomPolicy ?? policy).IsMatchModeEnabled(matchModeId))
        {
            blocked = OperationsBlocked(sessionId, "match_mode_disabled", "所选对战模式当前未开放");
            return true;
        }
        blocked = [];
        return false;
    }

    private L12OperationsPolicySnapshot CaptureOperationsPolicy()
        => _platform?.CaptureOperationsPolicy() ?? L12OperationsPolicyDefaults.FromCatalog(_catalog);

    private bool TryDefaultPresetIndexes(L12OperationsPolicySnapshot policy, out int first, out int second)
    {
        var indexes = Enumerable.Range(0, _catalog.PresetDecks.Count)
            .Where(index => IsPresetAllowed(policy, index, out _)).ToArray();
        if (indexes.Length == 0)
        {
            first = second = -1;
            return false;
        }
        first = indexes[0];
        second = indexes[Math.Min(1, indexes.Length - 1)];
        return true;
    }

    private bool IsPresetAllowed(L12OperationsPolicySnapshot policy, int deckIndex, out string error)
    {
        if (deckIndex < 0 || deckIndex >= _catalog.PresetDecks.Count)
        {
            error = "无效的预组";
            return false;
        }
        var deck = _catalog.DeckAt(deckIndex);
        if (!L12DeckValidator.TryValidatePreset(_catalog, deck, out error, policy.CardRestrictions))
            return false;
        error = string.Empty;
        return true;
    }

    private static string NormalizeName(string? name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? $"旅人{Random.Shared.Next(1000, 9999)}" : name.Trim();
        return value.Length > 16 ? value[..16] : value;
    }

    private static string GenerateRoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 6).Select(_ => alphabet[Random.Shared.Next(alphabet.Length)]).ToArray());
    }

    private string GenerateAvailableRoomCode()
    {
        string code;
        do { code = GenerateRoomCode(); }
        while (_rooms.ContainsKey(code) || _friendInvitations.Values.Any(invitation => invitation.RoomCode == code));
        return code;
    }

    private static L12RoomOptions NormalizeFriendlyOptions(L12RoomOptions? options, L12DefaultRoomConfig defaults)
    {
        var spectating = options?.Spectating is "public" or "friends" or "disabled"
            ? options.Spectating : defaults.Spectating;
        var handVisibility = options?.HandVisibility is "request" or "public"
            ? options.HandVisibility : defaults.HandVisibility;
        var configuredDisasterMode = options?.DisasterMode?.Trim().ToLowerInvariant();
        var disasterMode = configuredDisasterMode is "all" or "random" or "none"
            ? configuredDisasterMode
            : defaults.DisasterMode is "all" or "random" or "none" ? defaults.DisasterMode : "all";
        return new L12RoomOptions
        {
            MatchModeId = "friendly",
            Spectating = spectating,
            HandVisibility = handVisibility,
            DisasterMode = disasterMode,
            UseCardRestrictions = options?.UseCardRestrictions == true,
        };
    }
}
