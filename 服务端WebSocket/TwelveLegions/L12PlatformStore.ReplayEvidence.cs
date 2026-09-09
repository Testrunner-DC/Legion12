namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // Only identifiers are copied: no report text, histories or player data in maintenance caches.
    internal L12ReplayEvidenceReferences UnresolvedReplayEvidence()
    {
        lock (_gate)
        {
            var matches = new HashSet<string>(StringComparer.Ordinal);
            var rooms = new HashSet<string>(StringComparer.Ordinal);
            foreach (var matchId in RankedIntegrityProtectedMatchIds()) Add(matches, matchId);
            foreach (var report in _data.BugReports)
            {
                if (report.Status is "resolved" or "closed") continue;
                Add(matches, report.MatchId, report.Diagnostic?.MatchId,
                    report.ClientDiagnostic?.MatchId, report.ConnectionDiagnostic?.MatchId);
                Add(rooms, report.RoomCode, report.Diagnostic?.RoomCode,
                    report.ClientDiagnostic?.RoomCode, report.ConnectionDiagnostic?.RoomCode);
            }
            foreach (var report in _data.PlayerMatchReports.Where(row => row.Status is not ("resolved" or "closed")))
            {
                Add(matches, report.MatchId);
                Add(rooms, report.RoomCode);
            }
            foreach (var request in _data.MatchDrawRequests.Where(row => row.AdminStatus is not ("resolved" or "closed")))
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
