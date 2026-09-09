namespace TwelveLegions.Server;

public sealed partial class L12RoomManager
{
    private static readonly TimeSpan SettlementRetention = TimeSpan.FromMinutes(30);

    private IReadOnlyList<OutgoingMessage> LeaveSettlementLocked(Session session, Room room)
    {
        var index = session.PlayerIndex!.Value;
        room.SettlementStartedAt ??= _utcNow();
        room.SettlementLeft[index] = true;
        // 保留席位与卡组身份给尚在结算页的玩家，退出者可立即在大厅开启新对局。
        var placeholder = Guid.NewGuid();
        _sessions[placeholder] = new Session
        {
            Id = placeholder, AccountId = session.AccountId, Name = session.Name,
            RoomCode = room.Code, PlayerIndex = index, SelectedDeckIndex = session.SelectedDeckIndex,
            CustomDeck = session.CustomDeck, IsVirtual = true, Connected = false,
        };
        room.Sessions[index] = placeholder;
        ClearRoomMembership(session);
        var result = new List<OutgoingMessage> { new(session.Id, new { type = "roomLeft", message = "已离开结算并返回大厅" }) };
        if (CanRetireSettlement(room) && SettlementParticipantsLeft(room))
            result.AddRange(CloseSettlementLocked(room));
        return result;
    }

    private bool SettlementParticipantsLeft(Room room)
        => room.Sessions.All(id => !_sessions.TryGetValue(id, out var member) || member.IsVirtual
            || member.PlayerIndex is { } index && room.SettlementLeft[index]);

    private static bool CanRetireSettlement(Room room)
        => room.CompletionRecorded
           && (!string.Equals(room.Options.MatchModeId, "ranked", StringComparison.OrdinalIgnoreCase) || room.RankedResultReported)
           && (room.TournamentId is null || room.TournamentResultReported);

    private IReadOnlyList<OutgoingMessage> CloseSettlementLocked(Room room)
    {
        room.Closed = true;
        _rooms.TryRemove(room.Code, out _);
        Guid[] spectators;
        lock (room.Spectators) { spectators = [.. room.Spectators]; room.Spectators.Clear(); }
        var result = new List<OutgoingMessage>();
        foreach (var id in room.Sessions.Concat(spectators).Distinct())
        {
            if (!_sessions.TryGetValue(id, out var member) || member.RoomCode != room.Code) continue;
            ClearRoomMembership(member);
            if (member.IsVirtual) _sessions.TryRemove(id, out _);
            else if (member.Connected) result.Add(new(id, new { type = "roomClosed", message = "结算房间已关闭（双方已离开或保留时间已达30分钟）" }));
        }
        return result;
    }

    private async Task TickSettlementRoomsAsync(DateTimeOffset now, List<OutgoingMessage> messages)
    {
        foreach (var room in _rooms.Values.Where(item => item.Game?.State.Phase == L12Phase.GameOver).ToArray())
        {
            await room.Gate.WaitAsync();
            try
            {
                if (room.Closed || room.Game?.State.Phase != L12Phase.GameOver) continue;
                room.SettlementStartedAt ??= now;
                if (!SettlementParticipantsLeft(room) && now - room.SettlementStartedAt < SettlementRetention) continue;
                if (!CanRetireSettlement(room)) await CompleteTournamentRoomGameAsync(room);
                // 持久化或结算异常不丢弃现场；成功后下次周期退场。
                if (CanRetireSettlement(room)) messages.AddRange(CloseSettlementLocked(room));
            }
            finally { room.Gate.Release(); }
        }
    }
}
