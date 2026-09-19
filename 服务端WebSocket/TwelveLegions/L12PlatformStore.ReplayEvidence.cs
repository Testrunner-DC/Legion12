namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    internal static readonly TimeSpan BugReplayEvidenceMaximumAge = TimeSpan.FromDays(7);

    // Only identifiers are copied: no report text, histories or player data in maintenance caches.
    internal L12ReplayEvidenceReferences UnresolvedReplayEvidence()
        => ReplayEvidenceAt(DateTimeOffset.UtcNow);

    internal L12ReplayEvidenceReferences ReplayEvidenceAt(DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            var cutoff = utcNow.ToUniversalTime().Subtract(BugReplayEvidenceMaximumAge);
            var matches = new HashSet<string>(StringComparer.Ordinal);
            var rooms = new HashSet<string>(StringComparer.Ordinal);
            foreach (var matchId in RankedIntegrityProtectedMatchIds()) Add(matches, matchId);
            foreach (var report in _data.BugReports)
            {
                // A report protects replay payload only after human triage, only for an explicit
                // top-level binding, and never beyond the short diagnostic window. Auto-captured
                // diagnostics may describe a stale page/session and must not create fuzzy holds.
                if (report.Status is not ("confirmed" or "in-progress")
                    || report.CreatedAt.ToUniversalTime() < cutoff) continue;
                Add(matches, report.MatchId);
                Add(rooms, report.RoomCode);
            }
            foreach (var report in _data.PlayerMatchReports.Where(row => row.Status == "reviewing"
                         && row.CreatedAt.ToUniversalTime() >= cutoff))
            {
                Add(matches, report.MatchId);
                Add(rooms, report.RoomCode);
            }
            foreach (var request in _data.MatchDrawRequests.Where(row => row.AdminStatus == "reviewing"
                         && row.RequestedAt.ToUniversalTime() >= cutoff))
            {
                Add(matches, request.MatchId);
                Add(rooms, request.RoomCode);
            }
            return new L12ReplayEvidenceReferences(matches.ToArray(), rooms.ToArray());
        }

        static void Add(HashSet<string> target, params string?[] values)
        {
            foreach (var value in values)
                if (!string.IsNullOrWhiteSpace(value)) target.Add(value.Trim());
        }
    }
}
