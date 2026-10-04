namespace TwelveLegions.Server;

public sealed record L12PublicTournamentCountsView(
    int Registered,
    int CheckedIn,
    int Active,
    int Waitlisted);

public sealed record L12PublicTournamentSummaryView(
    string Code,
    string Name,
    string OrganizerName,
    string Status,
    string Format,
    string Phase,
    int MaxPlayers,
    DateTimeOffset? StartAt,
    int RoundMinutes,
    int CheckInMinutes,
    L12PublicTournamentCountsView Counts);

public sealed record L12PublicTournamentSummaryPage(
    IReadOnlyList<L12PublicTournamentSummaryView> Items,
    int Page,
    int PageSize,
    int Total,
    int TotalPages);

public sealed record L12PublicTournamentCardRestrictionView(
    string CardId,
    int MaxCopies,
    string? MasterId);

public sealed record L12PublicTournamentRulesView(
    string Ruleset,
    string DisasterMode,
    string BanList,
    IReadOnlyList<string> DisasterCardIds,
    IReadOnlyList<L12PublicTournamentCardRestrictionView> CardRestrictions,
    string DeckVisibility,
    int SwissRounds,
    int? CutSize,
    int LateGraceMinutes,
    L12RankedTimeControlConfig TimeControl);

public sealed record L12PublicTournamentParticipantView(string Name, string Status);

public sealed record L12PublicTournamentMatchView(
    int Table,
    string? PlayerAName,
    string? PlayerBName,
    string Status,
    string? Result,
    DateTimeOffset? StartedAt,
    DateTimeOffset? Deadline);

public sealed record L12PublicTournamentStandingView(
    int RoundNumber,
    int Rank,
    string Name,
    int Wins,
    int Losses,
    int Draws,
    int Byes,
    int OpponentScore,
    int OpponentsOpponentScore);

public sealed record L12PublicTournamentRoundView(
    int Number,
    string Stage,
    string Status,
    DateTimeOffset? StartedAt,
    IReadOnlyList<L12PublicTournamentMatchView> Matches,
    IReadOnlyList<L12PublicTournamentStandingView> Standings);

public sealed record L12PublicTournamentDetailView(
    string Code,
    string Name,
    string OrganizerName,
    string Status,
    string Format,
    string Phase,
    int MaxPlayers,
    DateTimeOffset? StartAt,
    int RoundMinutes,
    int CheckInMinutes,
    L12PublicTournamentCountsView Counts,
    string Description,
    string RegistrationVisibility,
    bool RegistrationOpen,
    L12PublicTournamentRulesView Rules,
    IReadOnlyList<L12PublicTournamentParticipantView> Participants,
    IReadOnlyList<L12PublicTournamentRoundView> Rounds,
    IReadOnlyList<L12PublicTournamentStandingView> FinalStandings);

public sealed partial class L12PlatformStore
{
    public L12PublicTournamentSummaryPage PublicTournamentSummaries(string? section, string? format,
        string? search, int page = 1, int pageSize = 24, DateTimeOffset? startFrom = null,
        DateTimeOffset? startTo = null)
    {
        lock (_gate)
        {
            var normalizedSection = string.IsNullOrWhiteSpace(section)
                ? "discover" : Allowed(section, "公开赛事分类", "discover", "history");
            var normalizedFormat = string.IsNullOrWhiteSpace(format)
                ? null : Allowed(format, "赛制", "single", "swiss", "swiss-cut", "league");
            var normalizedSearch = search?.Trim() ?? string.Empty;
            if (normalizedSearch.Length > 100) throw new ArgumentException("搜索文字不能超过 100 个字符");
            if (startFrom is not null && startTo is not null && startFrom > startTo)
                throw new ArgumentException("开始时间范围无效");
            page = Math.Clamp(page, 1, 10_000);
            pageSize = Math.Clamp(pageSize, 1, 100);

            IEnumerable<TournamentRow> query = _data.Tournaments.Where(row => row.Visibility == "public");
            query = normalizedSection == "history"
                ? query.Where(row => row.Status is "completed" or "canceled")
                : query.Where(row => row.Status is not ("completed" or "canceled"));
            if (normalizedFormat is not null)
                query = query.Where(row => row.Format == normalizedFormat);
            if (startFrom is not null) query = query.Where(row => row.StartAt >= startFrom);
            if (startTo is not null) query = query.Where(row => row.StartAt < startTo);
            if (normalizedSearch.Length > 0)
                query = query.Where(row => row.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                    || row.Code.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                    || PublicTournamentAccountName(row.OrganizerAccountId)
                        .Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));

            var rows = (normalizedSection == "history"
                    ? query.OrderByDescending(row => row.CompletedAt ?? row.CanceledAt ?? row.UpdatedAt)
                        .ThenBy(row => row.Code, StringComparer.Ordinal)
                    : query.OrderBy(row => row.StartAt ?? DateTimeOffset.MaxValue)
                        .ThenByDescending(row => row.UpdatedAt)
                        .ThenBy(row => row.Code, StringComparer.Ordinal))
                .ToArray();
            var total = rows.Length;
            var offset = (page - 1L) * pageSize;
            L12PublicTournamentSummaryView[] items = offset >= total ? [] : rows.Skip((int)offset).Take(pageSize)
                .Select(ToPublicTournamentSummary).ToArray();
            return new L12PublicTournamentSummaryPage(items, page, pageSize, total,
                total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
        }
    }

    public L12PublicTournamentDetailView? PublicTournamentByCode(string code)
    {
        lock (_gate)
        {
            var normalized = code.Trim();
            if (normalized.Length is < 1 or > 64) return null;
            var row = _data.Tournaments.FirstOrDefault(item => item.Visibility == "public"
                && string.Equals(item.Code, normalized, StringComparison.OrdinalIgnoreCase));
            return row is null ? null : ToPublicTournamentDetail(row);
        }
    }

    private L12PublicTournamentSummaryView ToPublicTournamentSummary(TournamentRow row)
        => new(row.Code, row.Name, PublicTournamentAccountName(row.OrganizerAccountId), row.Status,
            row.Format, row.Phase, row.MaxPlayers, row.StartAt, row.RoundMinutes, row.CheckInMinutes,
            PublicTournamentCounts(row));

    private L12PublicTournamentDetailView ToPublicTournamentDetail(TournamentRow row)
    {
        var namesVisible = row.RegistrationVisibility == "public";
        var participants = namesVisible
            ? row.Participants.Where(item => !item.Removed)
                .Select(item => new L12PublicTournamentParticipantView(
                    PublicTournamentAccountName(item.AccountId), PublicTournamentParticipantStatus(item)))
                .ToArray()
            : [];
        var rounds = row.Rounds.OrderBy(item => item.Number).Select(round =>
            new L12PublicTournamentRoundView(round.Number, round.Stage, round.Status, round.StartedAt,
                round.Matches.OrderBy(item => item.Table).Select(match =>
                    new L12PublicTournamentMatchView(match.Table,
                        namesVisible ? PublicTournamentAccountName(match.PlayerAAccountId) : null,
                        namesVisible && match.PlayerBAccountId is not null
                            ? PublicTournamentAccountName(match.PlayerBAccountId) : null,
                        match.Status, match.Status == "completed" ? match.Result : null,
                        match.StartedAt, match.Deadline)).ToArray(),
                namesVisible && round.Status == "completed" && round.StandingsCapturedAt is not null
                    ? round.Standings.OrderBy(item => item.Rank).Select(ToPublicTournamentStanding).ToArray()
                    : [])).ToArray();
        var rules = new L12PublicTournamentRulesView(row.Rules.Ruleset, row.Rules.DisasterMode,
            row.Rules.BanList, row.Rules.DisasterCardIds.ToArray(), row.Rules.CardRestrictions
                .Select(item => new L12PublicTournamentCardRestrictionView(item.CardId, item.MaxCopies,
                    item.MasterId)).ToArray(), row.Rules.DeckVisibility, row.SwissRounds, row.CutSize,
            row.LateGraceMinutes, NormalizeRankedTimeControl(row.TimeControl));
        return new L12PublicTournamentDetailView(row.Code, row.Name,
            PublicTournamentAccountName(row.OrganizerAccountId), row.Status, row.Format, row.Phase,
            row.MaxPlayers, row.StartAt, row.RoundMinutes, row.CheckInMinutes, PublicTournamentCounts(row),
            row.Description, row.RegistrationVisibility, row.RegistrationOpen, rules, participants, rounds,
            namesVisible
                ? row.FinalSwissStandings.OrderBy(item => item.Rank).Select(ToPublicTournamentStanding).ToArray()
                : []);
    }

    private L12PublicTournamentStandingView ToPublicTournamentStanding(TournamentStandingRow row)
        => new(row.RoundNumber, row.Rank, PublicTournamentAccountName(row.AccountId), row.Wins,
            row.Losses, row.Draws, row.Byes, row.OpponentScore, row.OpponentsOpponentScore);

    private static L12PublicTournamentCountsView PublicTournamentCounts(TournamentRow row)
        => new(row.Participants.Count(item => !item.Removed && !item.Waitlisted),
            row.Participants.Count(item => !item.Removed && !item.Dropped && !item.Waitlisted
                && item.TournamentCheckedInAt is not null),
            row.Participants.Count(item => !item.Removed && !item.Dropped && !item.Waitlisted
                && item.TournamentCheckedInAt is not null),
            row.Participants.Count(item => !item.Removed && !item.Dropped && item.Waitlisted));

    private static string PublicTournamentParticipantStatus(TournamentParticipantRow row)
        => row.Waitlisted ? "waitlisted"
            : row.Dropped ? "dropped"
            : row.Eliminated ? "eliminated"
            : row.TournamentCheckedInAt is not null ? "checked-in"
            : "registered";

    private string PublicTournamentAccountName(string accountId)
        => AccountById(accountId) is { } account ? PublicUsername(account) : "已删除账号";
}
