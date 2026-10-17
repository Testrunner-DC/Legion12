using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

// Deliberately separate from the administrative DTO: this projection cannot carry another
// player's private draft candidates, hidden composition or personal deck identity.
public sealed record L12VisibleDisasterDecision(int Step, string Action, int ActorIndex,
    string? ChosenCardId, string? Origin, IReadOnlyList<string> OwnCandidateCardIds);
public sealed record L12VisibleDisasterSetup(string Coverage, string Status, string? Mode,
    int? FirstPlayer, string Visibility, IReadOnlyList<L12VisibleDisasterDecision> Decisions,
    IReadOnlyList<string> VisibleCompositionCardIds);
public sealed record L12PublicDisasterBan(string CardId, long Opportunities, long Bans, double? BanRate);
public sealed record L12PublicDisasterStatistics(string Status, long UsableMatches,
    IReadOnlyList<L12PublicDisasterBan> Items, int RecentDays = 90, int MinimumMatches = 3);

public sealed partial class MatchRecorder
{
    internal async Task<L12VisibleDisasterSetup?> ReadVisibleDisasterSetupAsync(string matchId,
        string? accountId, bool tournamentPublic = false, CancellationToken cancellationToken = default,
        IReadOnlyCollection<string>? expectedAccounts = null)
    {
        if (string.IsNullOrWhiteSpace(matchId) || matchId.Length > 128
            || !tournamentPublic && string.IsNullOrWhiteSpace(accountId)) return null;
        var readOnly = new SqliteConnectionStringBuilder(_connectionString)
            { Mode = SqliteOpenMode.ReadOnly, DefaultTimeout = 2 }.ToString();
        await using var connection = new SqliteConnection(readOnly);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        int first, own;
        using (var command = DisasterCommand(connection, transaction, """
            SELECT first_player,account_0,account_1 FROM matches
            WHERE match_id=$id AND ended_utc IS NOT NULL AND COALESCE(error,'')=''
              AND mode_id<>'sandbox' AND ($tournament=0 OR mode_id='tournament');
            """, ("$id", matchId), ("$tournament", tournamentPublic ? 1 : 0)))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            if (expectedAccounts is not null && (expectedAccounts.Count != 2 || reader.IsDBNull(1) || reader.IsDBNull(2)
                || !new HashSet<string>(expectedAccounts, StringComparer.Ordinal).SetEquals([reader.GetString(1), reader.GetString(2)]))) return null;
            first = reader.IsDBNull(0) ? -1 : reader.GetInt32(0);
            own = accountId is null ? -1 : !reader.IsDBNull(1) && reader.GetString(1) == accountId ? 0
                : !reader.IsDBNull(2) && reader.GetString(2) == accountId ? 1 : -1;
            if (!tournamentPublic && own < 0) return null;
        }
        var facts = await ReadDisasterSetupFactsInSnapshotAsync(connection, transaction, matchId,
            first, cancellationToken);
        var result = new L12VisibleDisasterSetup(facts.Coverage, facts.Status, facts.Mode,
            first is 0 or 1 ? first : null, own >= 0 ? "self" : "public",
            facts.Decisions.Where(d => d.ResolvedSequence is not null && (d.Action == "ban" || d.ActorIndex == own))
                .Select(d => new L12VisibleDisasterDecision(d.Step, d.Action, d.ActorIndex, d.ChosenCardId,
                    d.Origin, d.Action == "pick" && d.ActorIndex == own ? d.Candidates : [])).ToArray(),
            facts.Composition.Where(c => c.Source is "fixed" or "random-public" || own >= 0 && c.OwnerIndex == own)
                .Select(c => c.CardId).Distinct(StringComparer.Ordinal).ToArray());
        transaction.Commit();
        return result;
    }

    internal async Task<L12PublicDisasterStatistics> ReadPublicDeckDisasterStatisticsAsync(string publicationId,
        L12CommittedStatisticsExclusions exclusions, CancellationToken cancellationToken = default)
    {
        if (!exclusions.Available) throw new InvalidDataException("统计排除集不可用");
        if (string.IsNullOrWhiteSpace(publicationId) || publicationId.Length > 64)
            throw new ArgumentException("公开牌库统计引用无效");
        // Fixed UTC day boundaries and no client-selected slices: pagination and arbitrary
        // date/filter differences must not turn a public aggregate into a private draft query.
        var until = new DateTimeOffset(_utcNow().UtcDateTime.Date, TimeSpan.Zero).AddDays(1);
        var query = NormalizeAnalyticsQuery(new(FromUtc: until.AddDays(-90), ToUtc: until,
            ExcludedMatchIds: exclusions.MatchIds.ToArray(), ExcludedAccountIds: exclusions.AccountIds.ToArray()));
        var key = AnalyticsResultCacheKey("public-disaster-bans", query,
            System.Text.Json.JsonSerializer.Serialize(publicationId));
        var epoch = AnalyticsCacheEpoch;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CardAnalyticsHardBudget);
        return (await ExecuteCardAnalyticsAsync(key, epoch, budget.Token, new CardAnalyticsRequestTiming(), execution =>
        {
            using var transaction = execution.Connection.BeginTransaction(deferred: true);
            PrepareAnalyticsScopeAsync(execution.Connection, query, includeFacts: false, transaction: transaction)
                .GetAwaiter().GetResult();
            using (var command = DisasterCommand(execution.Connection, transaction, """
                DELETE FROM temp.l12_analytics_eligible AS e WHERE NOT EXISTS(
                  SELECT 1 FROM match_public_deck_bindings b WHERE b.match_id=e.match_id
                    AND b.player_index=e.player_index AND b.publication_id=$publication);
                """, ("$publication", publicationId))) command.ExecuteNonQuery();
            var report = ComputeDisasterAnalytics(execution, transaction, query, allowUnselectedMaster: true);
            // No private opportunity groups, origins, initiative slices, match IDs or pool list
            // are serialized at this boundary, even when the caller is an administrator.
            var usable = report.Coverage.UsableMatches;
            var items = usable < 3 ? [] : report.Items.Where(r => r.Action == "ban")
                .GroupBy(r => r.CardId, StringComparer.Ordinal)
                .Where(g => g.Max(r => r.DistinctMatches) >= 3)
                .Select(g => new L12PublicDisasterBan(g.Key, g.Sum(r => r.Opportunities),
                    g.Sum(r => r.Selections), RateOrNull(g.Sum(r => r.Selections), g.Sum(r => r.Opportunities))))
                .OrderBy(r => r.CardId, StringComparer.Ordinal).ToArray();
            transaction.Commit();
            return new L12PublicDisasterStatistics(usable < 3 ? "insufficient" : "available",
                usable < 3 ? 0 : usable, items);
        }))!;
    }
}
