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

public sealed class L12RoomManager
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
        public bool[] Ready { get; } = [false, false];
        public L12GameEngine? Game { get; set; }
        public long CommandSequence { get; set; }
        public bool IsSandbox { get; init; }
        public Guid? GmControllerSessionId { get; set; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    private readonly L12Catalog _catalog;
    private readonly MatchRecorder _recorder;
    private readonly L12PlatformStore? _platform;
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FriendInvitation> _friendInvitations = new(StringComparer.OrdinalIgnoreCase);

    public L12RoomManager(L12Catalog catalog, MatchRecorder recorder, L12PlatformStore? platform = null)
    {
        _catalog = catalog;
        _recorder = recorder;
        _platform = platform;
    }

    public object Connect(Guid sessionId, string accountId, string? requestedName)
    {
        var name = NormalizeName(requestedName);
        var disconnected = _sessions.FirstOrDefault(pair =>
            pair.Key != sessionId && !pair.Value.Connected && !pair.Value.IsVirtual
            && pair.Value.RoomCode is not null
            && string.Equals(pair.Value.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
        if (disconnected.Value is not null)
        {
            var recovered = disconnected.Value;
            recovered.Connected = true;
            _sessions[sessionId] = recovered;
            if (recovered.RoomCode is not null && _rooms.TryGetValue(recovered.RoomCode, out var room))
            {
                lock (room.Sessions)
                {
                    var playerSlot = room.Sessions.IndexOf(disconnected.Key);
                    if (playerSlot >= 0) room.Sessions[playerSlot] = sessionId;
                }
                lock (room.Spectators)
                {
                    var spectatorSlot = room.Spectators.IndexOf(disconnected.Key);
                    if (spectatorSlot >= 0) room.Spectators[spectatorSlot] = sessionId;
                }
                if (room.GmControllerSessionId == disconnected.Key) room.GmControllerSessionId = sessionId;
            }
            _sessions.TryRemove(disconnected.Key, out _);
            return new { type = "session", sessionId, name, recovered = true, roomCode = recovered.RoomCode };
        }
        _sessions[sessionId] = new Session { Id = sessionId, AccountId = accountId, Name = name };
        return new { type = "session", sessionId, name, recovered = false, roomCode = (string?)null };
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
                        !isSelf && friends && !viewerInRoom, false,
                        isSelf ? "当前账号" : !friends ? "成为好友后可邀请对战" : viewerInRoom ? "请先离开当前房间" : null);

                if (session.IsSpectator)
                    return new L12OnlinePresence(group.Key, "spectating", null, false, false,
                        isSelf ? "当前账号" : "该玩家正在观战");
                if (room.Game is null)
                    return new L12OnlinePresence(group.Key, "inRoom", null, false, false,
                        isSelf ? "当前账号" : "该玩家正在房间中");

                var canSpectate = !isSelf && !viewerInRoom && room.Options.Spectating != "disabled"
                    && (room.Options.Spectating != "friends" || friends);
                var reason = isSelf ? "当前账号"
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

        var room = new Room { Code = invitation.RoomCode, Options = NormalizeOptions(null) };
        if (!_rooms.TryAdd(room.Code, room)) return Error(sessionId, "预留房间码已失效，请重新邀请");
        room.Sessions.Add(host.Id);
        room.Sessions.Add(sessionId);
        host.RoomCode = recipient.RoomCode = room.Code;
        host.PlayerIndex = 0;
        recipient.PlayerIndex = 1;
        host.SelectedDeckIndex = 0;
        recipient.SelectedDeckIndex = Math.Min(1, _catalog.PresetDecks.Count - 1);
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
    {
        if (!_sessions.TryGetValue(sessionId, out var session)
            || session.RoomCode is null
            || !_rooms.TryGetValue(session.RoomCode, out var room)) return [];
        return room.Game is null
            ? BroadcastRoom(room)
            : BroadcastRoom(room).Concat(BroadcastGame(room)).ToArray();
    }

    public IReadOnlyList<OutgoingMessage> CreateRoom(Guid sessionId, L12RoomOptions? options = null)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return Error(sessionId, "会话不存在");
        if (session.RoomCode is not null) return Error(sessionId, "已经加入房间");
        var normalizedOptions = NormalizeOptions(options);
        if (normalizedOptions.DisasterMode == "season")
            return Error(sessionId, "赛季天灾池需由管理员后台配置，当前仅作功能占位");
        Room room;
        do { room = new Room { Code = GenerateRoomCode(), Options = normalizedOptions }; }
        while (!_rooms.TryAdd(room.Code, room));
        room.Sessions.Add(sessionId);
        session.RoomCode = room.Code;
        session.PlayerIndex = 0;
        session.SelectedDeckIndex = 0;
        return BroadcastRoom(room);
    }

    public async Task<IReadOnlyList<OutgoingMessage>> CreateSandboxAsync(Guid sessionId, L12SandboxRequest? request)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return Error(sessionId, "会话不存在");
        if (session.RoomCode is not null) return Error(sessionId, "已经加入房间");
        request ??= new L12SandboxRequest();
        if (request.DisasterMode == "season") return Error(sessionId, "赛季天灾池尚未配置");

        L12PresetDeckDefinition playerDeck;
        L12PresetDeckDefinition opponentDeck;
        if (request.PlayerDeck is not null)
        {
            if (!L12DeckValidator.TryValidate(_catalog, request.PlayerDeck, out playerDeck!, out var playerError))
                return Error(sessionId, $"我方牌库无效：{playerError}", "deckRejected");
        }
        else playerDeck = _catalog.DeckAt(0);
        if (request.OpponentDeck is not null)
        {
            if (!L12DeckValidator.TryValidate(_catalog, request.OpponentDeck, out opponentDeck!, out var opponentError))
                return Error(sessionId, $"对手牌库无效：{opponentError}", "deckRejected");
        }
        else opponentDeck = _catalog.DeckAt(Math.Min(1, _catalog.PresetDecks.Count - 1));

        Room room;
        do
        {
            room = new Room
            {
                Code = GenerateRoomCode(),
                IsSandbox = true,
                GmControllerSessionId = sessionId,
                Options = NormalizeOptions(new L12RoomOptions
                {
                    Spectating = "disabled",
                    HandVisibility = "public",
                    DisasterMode = request.DisasterMode,
                }, allowCustom: true),
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
            SelectedDeckIndex = 1,
            CustomDeck = opponentDeck,
            IsVirtual = true,
        };
        _sessions[opponentId] = opponent;
        room.Sessions.Add(sessionId);
        room.Sessions.Add(opponentId);
        room.Ready[0] = room.Ready[1] = true;
        session.RoomCode = room.Code;
        session.PlayerIndex = 0;
        session.SelectedDeckIndex = 0;
        session.CustomDeck = playerDeck;

        room.Game = new L12GameEngine(
            _catalog, Guid.NewGuid().ToString("N"), room.Code, Random.Shared.Next(),
            [session.Name, opponent.Name], [playerDeck, opponentDeck], skipPreparation: true,
            disasterMode: room.Options.DisasterMode);
        room.Game.InitializeGmDisasters();
        await _recorder.StartAsync(room.Game.State);
        foreach (var playerIndex in new[] { 0, 1 })
        {
            var bootstrap = new L12Command("mulligan", CardInstanceIds: []);
            var result = room.Game.Handle(playerIndex, bootstrap);
            room.CommandSequence++;
            await _recorder.AppendAsync(room.Game, room.CommandSequence, playerIndex,
                JsonSerializer.Serialize(bootstrap), result);
        }
        return BroadcastRoom(room).Concat(BroadcastGame(room)).ToArray();
    }

    public IReadOnlyList<OutgoingMessage> SpectateRoom(Guid sessionId, string? roomCode)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return Error(sessionId, "会话不存在");
        if (session.RoomCode is not null) return Error(sessionId, "已经加入房间");
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
        lock (room.Sessions)
        {
            if (room.Sessions.Count >= 2) return Error(sessionId, "房间已满");
            room.Sessions.Add(sessionId);
            session.RoomCode = room.Code;
            session.PlayerIndex = 1;
            session.SelectedDeckIndex = Math.Min(1, _catalog.PresetDecks.Count - 1);
        }
        return BroadcastRoom(room);
    }

    public IReadOnlyList<OutgoingMessage> SelectDeck(Guid sessionId, int deckIndex)
    {
        if (!TryGetMembership(sessionId, out var session, out var room, out var error)) return Error(sessionId, error);
        if (room.Game is not null) return Error(sessionId, "对局已经开始");
        if (deckIndex < 0 || deckIndex >= _catalog.PresetDecks.Count) return Error(sessionId, "无效的预组");
        session.SelectedDeckIndex = deckIndex;
        session.CustomDeck = null;
        room.Ready[session.PlayerIndex!.Value] = false;
        return BroadcastRoom(room);
    }

    public IReadOnlyList<OutgoingMessage> SelectCustomDeck(Guid sessionId, L12CustomDeckSubmission submission)
    {
        if (!TryGetMembership(sessionId, out var session, out var room, out var error)) return Error(sessionId, error);
        if (room.Game is not null) return Error(sessionId, "对局已经开始");
        if (!L12DeckValidator.TryValidate(_catalog, submission, out var deck, out error))
            return Error(sessionId, error, "deckRejected");
        session.CustomDeck = deck;
        room.Ready[session.PlayerIndex!.Value] = false;
        return BroadcastRoom(room);
    }

    public async Task<IReadOnlyList<OutgoingMessage>> SetReadyAsync(Guid sessionId, bool ready)
    {
        if (!TryGetMembership(sessionId, out var session, out var room, out var error)) return Error(sessionId, error);
        await room.Gate.WaitAsync();
        try
        {
            if (room.Game is not null) return Error(sessionId, "对局已经开始");
            room.Ready[session.PlayerIndex!.Value] = ready;
            if (room.Sessions.Count == 2 && room.Ready.All(value => value))
            {
                var playerNames = room.Sessions.Select(id => _sessions[id].Name).ToArray();
                room.Game = new L12GameEngine(
                    _catalog, Guid.NewGuid().ToString("N"), room.Code, Random.Shared.Next(),
                    playerNames, room.Sessions.Select(id => SelectedDeck(_sessions[id])).ToArray(),
                    disasterMode: room.Options.DisasterMode);
                await _recorder.StartAsync(room.Game.State);
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
            L12Command? command;
            try
            {
                command = commandElement.Deserialize<L12Command>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException) { return Error(sessionId, "操作格式错误"); }
            if (command is null || string.IsNullOrWhiteSpace(command.Type)) return Error(sessionId, "缺少操作类型");
            var result = room.Game.Handle(session.PlayerIndex!.Value, command);
            room.CommandSequence++;
            await _recorder.AppendAsync(room.Game, room.CommandSequence, session.PlayerIndex.Value, commandElement.GetRawText(), result);
            if (!result.Accepted) return Error(sessionId, result.Error ?? "操作被拒绝", "actionRejected");
            if (room.Game.State.Phase == L12Phase.GameOver) await _recorder.CompleteAsync(room.Game);
            return BroadcastGame(room);
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
            await _recorder.AppendAsync(room.Game, room.CommandSequence, session.PlayerIndex!.Value,
                commandElement.GetRawText(), result);
            if (!result.Accepted) return Error(sessionId, result.Error ?? "GM 操作被拒绝", "actionRejected");
            if (room.Game.State.Phase == L12Phase.GameOver) await _recorder.CompleteAsync(room.Game);
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
            await _recorder.AppendAsync(room.Game, room.CommandSequence, actingPlayerIndex,
                commandElement.GetRawText(), result);
            if (!result.Accepted) return Error(sessionId, result.Error ?? "沙盒操作被拒绝", "actionRejected");
            if (room.Game.State.Phase == L12Phase.GameOver) await _recorder.CompleteAsync(room.Game);
            return BroadcastGame(room);
        }
        finally { room.Gate.Release(); }
    }

    public IReadOnlyList<OutgoingMessage> Disconnect(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return [];
        session.Connected = false;
        if (session.RoomCode is null || !_rooms.TryGetValue(session.RoomCode, out var room)) return [];
        return BroadcastRoom(room);
    }

    public IReadOnlyList<OutgoingMessage> LeaveRoom(Guid sessionId)
    {
        if (!TryGetMembership(sessionId, out var session, out var room, out var error)) return Error(sessionId, error);
        if (!room.IsSandbox && room.Game is not null && room.Game.State.Phase != L12Phase.GameOver)
            return Error(sessionId, "对局已开始，请在对局内投降后离开");

        var playerIndex = session.PlayerIndex!.Value;
        if (playerIndex == 0)
        {
            _rooms.TryRemove(room.Code, out _);
            var messages = new List<OutgoingMessage>();
            foreach (var id in room.Sessions.ToArray())
            {
                if (!_sessions.TryGetValue(id, out var member)) continue;
                member.RoomCode = null;
                member.PlayerIndex = null;
                member.CustomDeck = null;
                messages.Add(new OutgoingMessage(id, new
                {
                    type = id == sessionId ? "roomLeft" : "roomClosed",
                    message = id == sessionId ? "房间已关闭" : "房主已关闭房间",
                }));
                if (member.IsVirtual) _sessions.TryRemove(id, out _);
            }
            return messages;
        }

        room.Sessions.Remove(sessionId);
        room.Ready[playerIndex] = false;
        session.RoomCode = null;
        session.PlayerIndex = null;
        session.CustomDeck = null;
        return new[] { new OutgoingMessage(sessionId, new { type = "roomLeft", message = "已离开房间" }) }
            .Concat(BroadcastRoom(room)).ToArray();
    }

    private IReadOnlyList<OutgoingMessage> BroadcastRoom(Room room)
    {
        var decks = _catalog.PresetDecks.Select((deck, index) => new
        {
            index, deck.Name, deck.MasterId,
            masterName = _catalog.Cards[deck.MasterId].NameZh,
            faction = _catalog.Cards[deck.MasterId].Faction,
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
            });
        }).ToArray();
    }

    private IReadOnlyList<OutgoingMessage> BroadcastGame(Room room)
        => room.Sessions.Select(id => new OutgoingMessage(id, new
        {
            type = "gameState", state = room.IsSandbox && room.GmControllerSessionId == id
                ? room.Game!.SnapshotForGm(_sessions[id].PlayerIndex!.Value)
                : room.Game!.SnapshotFor(_sessions[id].PlayerIndex!.Value),
            gmEnabled = room.IsSandbox && room.GmControllerSessionId == id,
        })).Concat(room.Spectators.Select(id => new OutgoingMessage(id, new
        {
            type = "gameState", spectating = true, gmEnabled = false, state = room.Game!.SnapshotForSpectator(),
        }))).ToArray();

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

    private static L12RoomOptions NormalizeOptions(L12RoomOptions? options, bool allowCustom = false)
    {
        var spectating = options?.Spectating is "friends" or "disabled" ? options.Spectating : "public";
        var handVisibility = options?.HandVisibility == "public" ? "public" : "request";
        var disasterMode = options?.DisasterMode is "random" or "season" or "none"
            || (allowCustom && options?.DisasterMode == "custom") ? options!.DisasterMode : "all";
        return new L12RoomOptions { Spectating = spectating, HandVisibility = handVisibility, DisasterMode = disasterMode };
    }
}
