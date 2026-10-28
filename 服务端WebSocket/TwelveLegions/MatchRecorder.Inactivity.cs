using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    internal sealed class InactivityCommit
    {
        internal bool Attempted;
        internal bool Committed;
    }
    private int _foregroundAppendOwners;
    private CancellationTokenSource? _inactivityWriteCancellation;

    // Reuse the one authoritative writer gate; maintenance never queues ahead of gameplay.
    internal bool InactivityWriteAvailable => Volatile.Read(ref _foregroundAppendOwners) == 0
        && _appendWriteGate.CurrentCount > 0;

    private void InterruptInactivityWrite()
    {
        var cancellation = Volatile.Read(ref _inactivityWriteCancellation);
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { } // A completed owner disposed after the read.
    }

    private async Task EnterForegroundAppendAsync()
    {
        Interlocked.Increment(ref _foregroundAppendOwners);
        InterruptInactivityWrite();
        try { await _appendWriteGate.WaitAsync(); }
        catch { Interlocked.Decrement(ref _foregroundAppendOwners); throw; }
    }

    private void ExitForegroundAppend()
    {
        _appendWriteGate.Release();
        Interlocked.Decrement(ref _foregroundAppendOwners);
    }

    internal async Task<bool> VerifyInactivityRecoveryAsync(L12GameEngine engine, CancellationToken token = default)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        await connection.OpenAsync(token);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT mode_id,ended_utc,winner,final_hash FROM matches WHERE match_id=$match;";
        command.Parameters.AddWithValue("$match", engine.State.MatchId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token) || reader.GetString(0) is not ("friendly" or "casual"))
            throw new InvalidDataException("无活动恢复缺少原模式记录");
        var completed = !reader.IsDBNull(1);
        if (completed != (engine.State.Phase == L12Phase.GameOver)
            || completed && (!reader.IsDBNull(2) || engine.State.Winner is not null
                || engine.State.EndedByAgreedDraw || reader.IsDBNull(3)
                || !string.Equals(reader.GetString(3), engine.ComputeStateHash(), StringComparison.Ordinal)))
            throw new InvalidDataException("无活动恢复的日志与赛果不一致");
        return completed;
    }

    internal Task<bool> TryAppendInactivityDrawAsync(L12GameEngine candidate, long sequence, InactivityCommit commit,
        CancellationToken cancellationToken = default)
    {
        if (candidate.State.Phase != L12Phase.GameOver || candidate.State.Winner is not null
            || candidate.State.EndedByAgreedDraw
            || candidate.UnpersistedEvents.LastOrDefault()?.Type != "game-draw")
            throw new InvalidOperationException("无活动结算必须是尚未提交的权威平局候选");
        return AppendWithCardFactsAsync(candidate, sequence, -1, JsonSerializer.Serialize(new
        {
            type = "authorityConclusion", reason = candidate.State.WinnerReason,
            winner = (int?)null, agreedDraw = false, inactivityDraw = true,
        }), CommandResult.Ok(), null, null, null, false, null,
            inactivityCommit: commit, maintenanceCancellation: cancellationToken);
    }
}
