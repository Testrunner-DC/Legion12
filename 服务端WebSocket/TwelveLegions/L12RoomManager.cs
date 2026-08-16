using System.Collections.Concurrent;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record OutgoingMessage(Guid SessionId, object Payload);

public sealed class L12RoomManager
{
    private sealed class Session
    {
        public required Guid Id { get; init; }
        public required string Name { get; set; }
        public string? RoomCode { get; set; }
        public int? PlayerIndex { get; set; }
        public int SelectedDeckIndex { get; set; }
        public L12PresetDeckDefinition? CustomDeck { get; set; }
        public bool IsSpectator { get; set; }
        public bool Connected { get; set; } = true;
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
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    private readonly L12Catalog _catalog;
    private readonly MatchRecorder _recorder;
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);

    public L12RoomManager(L12Catalog catalog, MatchRecorder recorder)
    {
        _catalog = catalog;
        _recorder = recorder;
    }

    public object Connect(Guid sessionId, string? requestedName)
    {
        var name = NormalizeName(requestedName);
        _sessions[sessionId] = new Session { Id = sessionId, Name = name };
        return new { type = "session", sessionId, name };
    }

    public IReadOnlyList<OutgoingMessage> CreateRoom(Guid sessionId, L12RoomOptions? options = null)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return Error(sessionId, "会话不存在");
        if (session.RoomCode is not null) return Error(sessionId, "已经加入房间");
        Room room;
        do { room = new Room { Code = GenerateRoomCode(), Options = NormalizeOptions(options) }; }
        while (!_rooms.TryAdd(room.Code, room));
        room.Sessions.Add(sessionId);
        session.RoomCode = room.Code;
        session.PlayerIndex = 0;
        session.SelectedDeckIndex = 0;
        return BroadcastRoom(room);
    }

    public IReadOnlyList<OutgoingMessage> SpectateRoom(Guid sessionId, string? roomCode)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return Error(sessionId, "会话不存在");
        if (session.RoomCode is not null) return Error(sessionId, "已经加入房间");
        var code = (roomCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!_rooms.TryGetValue(code, out var room)) return Error(sessionId, "房间不存在");
        if (room.Game is null) return Error(sessionId, "对局尚未开始");
        if (room.Options.Spectating == "disabled") return Error(sessionId, "该房间禁止观战");
        if (room.Options.Spectating == "friends") return Error(sessionId, "该房间仅限好友观战；好友身份校验尚未接入");
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
                    playerNames, room.Sessions.Select(id => SelectedDeck(_sessions[id])).ToArray());
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

    public IReadOnlyList<OutgoingMessage> Disconnect(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return [];
        session.Connected = false;
        if (session.RoomCode is null || !_rooms.TryGetValue(session.RoomCode, out var room)) return [];
        return BroadcastRoom(room);
    }

    private IReadOnlyList<OutgoingMessage> BroadcastRoom(Room room)
    {
        var players = room.Sessions.Select(id =>
        {
            var member = _sessions[id];
            var deck = SelectedDeck(member);
            var master = _catalog.Cards[deck.MasterId];
            return new
            {
                member.Name, playerIndex = member.PlayerIndex, member.Connected,
                ready = room.Ready[member.PlayerIndex!.Value],
                deckIndex = member.CustomDeck is null ? member.SelectedDeckIndex : -1,
                customDeck = member.CustomDeck is not null,
                deckName = deck.Name, masterName = master.NameZh, faction = master.Faction,
            };
        }).ToArray();
        var decks = _catalog.PresetDecks.Select((deck, index) => new
        {
            index, deck.Name, deck.MasterId,
            masterName = _catalog.Cards[deck.MasterId].NameZh,
            faction = _catalog.Cards[deck.MasterId].Faction,
        }).ToArray();
        return room.Sessions.Select(id => new OutgoingMessage(id, new
        {
            type = "roomState", roomCode = room.Code, yourPlayerIndex = _sessions[id].PlayerIndex,
            players, decks, started = room.Game is not null,
        })).ToArray();
    }

    private IReadOnlyList<OutgoingMessage> BroadcastGame(Room room)
        => room.Sessions.Select(id => new OutgoingMessage(id, new
        {
            type = "gameState", state = room.Game!.SnapshotFor(_sessions[id].PlayerIndex!.Value),
        })).Concat(room.Spectators.Select(id => new OutgoingMessage(id, new
        {
            type = "gameState", spectating = true, state = room.Game!.SnapshotForSpectator(),
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

    private static L12RoomOptions NormalizeOptions(L12RoomOptions? options)
    {
        var spectating = options?.Spectating is "friends" or "disabled" ? options.Spectating : "public";
        var handVisibility = options?.HandVisibility == "public" ? "public" : "request";
        return new L12RoomOptions { Spectating = spectating, HandVisibility = handVisibility };
    }
}
