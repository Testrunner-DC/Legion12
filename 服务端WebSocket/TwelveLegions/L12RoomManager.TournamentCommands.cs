namespace TwelveLegions.Server;

public sealed record L12TournamentRoomCommandDrainResult(
    int Applied, int Failed, int Pending, IReadOnlyList<OutgoingMessage> Messages);

public sealed partial class L12RoomManager
{
    private readonly SemaphoreSlim _tournamentRoomCommandGate = new(1, 1);

    public async Task<L12TournamentRoomCommandDrainResult> DrainTournamentRoomCommandsAsync(
        string? tournamentId = null)
    {
        if (_platform is null) return new(0, 0, 0, []);
        await _tournamentRoomCommandGate.WaitAsync();
        try
        {
            var applied = 0;
            var failed = 0;
            var messages = new List<OutgoingMessage>();
            foreach (var command in _platform.PendingTournamentRoomCommands(tournamentId))
            {
                try
                {
                    var result = command.Kind switch
                    {
                        "pause" when command.Paused is { } paused
                            => await ApplyPendingTournamentPauseAsync(command.TournamentId,
                                command.MatchId, command.RoomCode, paused, command.Reason),
                        "cancel" => await ApplyPendingTournamentCancelAsync(command.TournamentId,
                            command.MatchId, command.RoomCode, command.Reason),
                        _ => throw new InvalidDataException($"未知赛事房间待办类型：{command.Kind}"),
                    };
                    if (!result.Applied)
                        throw new InvalidOperationException("目标赛事房间尚未恢复，保留待办稍后重试");
                    messages.AddRange(result.Messages);
                    _platform.RecordTournamentRoomCommandAttempt(command.TournamentId, command.Id,
                        completed: true, error: null);
                    applied++;
                }
                catch (Exception error)
                {
                    failed++;
                    try
                    {
                        _platform.RecordTournamentRoomCommandAttempt(command.TournamentId, command.Id,
                            completed: false, error.Message);
                    }
                    catch (Exception persistenceError)
                    {
                        Console.Error.WriteLine($"Tournament room command failure persistence ({command.Id}): "
                                                + persistenceError.Message);
                    }
                    Console.Error.WriteLine($"Tournament room command ({command.Id}): {error.Message}");
                }
            }
            return new L12TournamentRoomCommandDrainResult(applied, failed,
                _platform.PendingTournamentRoomCommands(tournamentId).Count, messages);
        }
        finally { _tournamentRoomCommandGate.Release(); }
    }

    private async Task<(bool Applied, IReadOnlyList<OutgoingMessage> Messages)>
        ApplyPendingTournamentPauseAsync(string tournamentId, string matchId, string roomCode,
            bool paused, string reason)
    {
        var room = _rooms.Values.FirstOrDefault(item => item.TournamentId == tournamentId
            && item.TournamentMatchId == matchId);
        if (room?.RankedClock is null || room.Game is null)
            return (await _recorder.HasUnfinishedRankedMatchForRoomAsync(roomCode) ? false : true, []);
        return (true, await PauseTournamentClockAsync(tournamentId, matchId, paused, reason));
    }

    private async Task<(bool Applied, IReadOnlyList<OutgoingMessage> Messages)>
        ApplyPendingTournamentCancelAsync(string tournamentId, string matchId, string roomCode, string reason)
    {
        var room = _rooms.Values.FirstOrDefault(item => item.TournamentId == tournamentId
            && item.TournamentMatchId == matchId);
        if (room?.Game is null)
            return (await _recorder.HasUnfinishedRankedMatchForRoomAsync(roomCode) ? false : true, []);
        var messages = await CancelTournamentMatchRoomAsync(room, reason);
        return (true, messages);
    }
}
