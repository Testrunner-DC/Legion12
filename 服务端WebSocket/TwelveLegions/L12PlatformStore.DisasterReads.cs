namespace TwelveLegions.Server;

internal sealed record L12TournamentDisasterReadLease(string TournamentId, long Version, string MatchId,
    string RecordedMatchId, int Round, int Table, string PlayerA, string PlayerB);

public sealed partial class L12PlatformStore
{
    internal L12TournamentDisasterReadLease? CaptureTournamentDisasterRead(string code, int roundNumber,
        int table, L12AccountView? viewer)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64 || roundNumber < 1 || table < 1) return null;
        lock (_gate)
        {
            if (viewer is { Disabled: true } or { Deleted: true }) return null;
            var row = _data.Tournaments.FirstOrDefault(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));
            if (row is null || row.Visibility != "public" && (viewer is null
                || !L12Authorization.HasPermission(viewer, L12Permission.TournamentsRead))) return null;
            // Authenticated sharing-code visibility is the existing TournamentByCode policy.
            var match = row.Rounds.FirstOrDefault(r => r.Number == roundNumber)?.Matches.FirstOrDefault(m => m.Table == table);
            if (match is null || match.Status != "completed" || match.Result == "bye"
                || string.IsNullOrWhiteSpace(match.RecordedMatchId) || string.IsNullOrWhiteSpace(match.PlayerBAccountId)) return null;
            return new(row.Id, row.Version, match.Id, match.RecordedMatchId, roundNumber, table,
                match.PlayerAAccountId, match.PlayerBAccountId);
        }
    }

    internal bool RevalidateTournamentDisasterRead(string code, L12TournamentDisasterReadLease lease, L12AccountView? viewer)
        => CaptureTournamentDisasterRead(code, lease.Round, lease.Table, viewer) == lease;
}
