using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed record L12TournamentRulesSnapshotView(
    string Ruleset,
    string DisasterMode,
    string BanList,
    IReadOnlyList<string> DisasterCardIds,
    IReadOnlyList<L12CardRestrictionConfig> CardRestrictions,
    string DeckVisibility,
    string Hash,
    DateTimeOffset CapturedAt);

public sealed record L12TournamentDeckSnapshotView(
    string Name,
    string? Code,
    string Hash,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? LockedAt,
    string MasterId,
    IReadOnlyList<string> CardIds,
    IReadOnlyList<string> MoraleIds,
    IReadOnlyList<string> SpecialIds);

public sealed record L12TournamentStaffView(string AccountId, string Username);

public sealed record L12TournamentParticipantView(
    string AccountId,
    string Username,
    bool CheckedIn,
    bool Dropped,
    bool Eliminated,
    int Seed,
    L12TournamentDeckSnapshotView? Deck);

public sealed record L12TournamentStandingView(
    int RoundNumber,
    int Rank,
    string AccountId,
    string Username,
    int Wins,
    int Losses,
    int Draws,
    int Byes,
    int OpponentScore,
    int OpponentsOpponentScore,
    int Seed);

public sealed record L12TournamentMatchEventView(
    string Id,
    string Kind,
    string? Result,
    string? RecordedMatchId,
    string ActorId,
    string Detail,
    DateTimeOffset CreatedAt);

public sealed record L12TournamentRulingView(
    string Id,
    string MatchId,
    string Kind,
    string? TargetAccountId,
    string Decision,
    int Minutes,
    string Reason,
    string ActorId,
    string ActorName,
    DateTimeOffset CreatedAt);

public sealed record L12TournamentMatchView(
    string Id,
    int Table,
    string PlayerAAccountId,
    string PlayerAName,
    string? PlayerBAccountId,
    string PlayerBName,
    string RoomCode,
    bool ReadyA,
    bool ReadyB,
    string Status,
    string? Result,
    int TimeExtensionMinutes,
    DateTimeOffset? StartedAt,
    DateTimeOffset? Deadline,
    string? RecordedMatchId,
    IReadOnlyList<L12TournamentRulingView> Rulings,
    DateTimeOffset? GraceDeadline,
    IReadOnlyList<string> SourceMatchIds,
    string RulesHash,
    string? PlayerADeckHash,
    string? PlayerBDeckHash,
    int ReplayNumber,
    bool CanEnter,
    bool CanSpectate,
    IReadOnlyList<L12TournamentMatchEventView> Events);

public sealed record L12TournamentRoundView(
    string Id,
    int Number,
    string Status,
    bool Paused,
    DateTimeOffset? StartedAt,
    DateTimeOffset? PausedAt,
    int TotalPausedSeconds,
    IReadOnlyList<L12TournamentMatchView> Matches,
    string Stage,
    DateTimeOffset? StandingsCapturedAt,
    IReadOnlyList<L12TournamentStandingView> Standings,
    string? PairingFailure);

public sealed record L12TournamentBracketMatchView(
    string Id,
    int Table,
    string PlayerAAccountId,
    string PlayerAName,
    string? PlayerBAccountId,
    string PlayerBName,
    string? Result,
    IReadOnlyList<string> SourceMatchIds);

public sealed record L12TournamentBracketRoundView(
    int Number,
    IReadOnlyList<L12TournamentBracketMatchView> Matches);

public sealed record L12TournamentView(
    string Id,
    string Code,
    string Name,
    string OrganizerAccountId,
    string OrganizerName,
    IReadOnlyList<L12TournamentStaffView> Referees,
    string Status,
    string Format,
    string Visibility,
    int MaxPlayers,
    DateTimeOffset? StartAt,
    string Description,
    L12TournamentRulesSnapshotView Rules,
    int RoundMinutes,
    int CheckInMinutes,
    IReadOnlyList<L12TournamentParticipantView> Participants,
    IReadOnlyList<L12TournamentRoundView> Rounds,
    long Version,
    bool LegacyImported,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    int SwissRounds,
    int? CutSize,
    string RegistrationVisibility,
    int LateGraceMinutes,
    IReadOnlyList<L12TournamentStandingView> FinalSwissStandings,
    IReadOnlyList<L12TournamentBracketRoundView> EliminationBracket);

public sealed record L12TournamentListView(long PlatformVersion, IReadOnlyList<L12TournamentView> Items);

public sealed record L12TournamentCreatePayload(
    string Name,
    string Format,
    string Visibility,
    int MaxPlayers,
    DateTimeOffset? StartAt,
    string Ruleset,
    string Description,
    string DeckVisibility,
    string DisasterMode,
    string BanList,
    int RoundMinutes,
    int CheckInMinutes,
    IReadOnlyList<string>? RefereeAccountIds = null,
    IReadOnlyList<string>? DisasterCardIds = null,
    IReadOnlyList<L12CardRestrictionConfig>? CardRestrictions = null,
    int SwissRounds = 1,
    int? CutSize = null,
    string RegistrationVisibility = "public",
    int LateGraceMinutes = 5);

public sealed record L12TournamentRegistrationPayload(string DeckName, string DeckCode);
public sealed record L12TournamentStaffPayload(IReadOnlyList<string> RefereeAccountIds);
public sealed record L12TournamentCheckInPayload(string? AccountId, bool Ready);
public sealed record L12TournamentPausePayload(bool Paused, string Reason);
public sealed record L12TournamentTimeExtensionPayload(int Minutes, string Reason);
public sealed record L12TournamentRulingPayload(string Kind, string? TargetAccountId, string Decision, string Reason);
public sealed record L12TournamentMatchReferencePayload(string RecordedMatchId);
public sealed record L12TournamentRematchPayload(string Reason);

public sealed record L12LegacyTournamentParticipantInput(
    string Name,
    string? DeckName = null,
    string? DeckCode = null,
    bool CheckedIn = false,
    bool Dropped = false);

public sealed record L12LegacyTournamentMatchInput(
    string? Id,
    int Table,
    string PlayerA,
    string PlayerB,
    string? RoomCode,
    bool ReadyA,
    bool ReadyB,
    string? Status,
    string? Result,
    string? Ruling,
    int TimeExtension,
    DateTimeOffset? StartedAt,
    DateTimeOffset? Deadline);

public sealed record L12LegacyTournamentRoundInput(
    int Number,
    string? Status,
    bool Paused,
    DateTimeOffset? StartedAt,
    IReadOnlyList<L12LegacyTournamentMatchInput>? Matches);

public sealed record L12LegacyTournamentInput(
    string Id,
    string? Code,
    string Name,
    string? Organizer,
    IReadOnlyList<string>? Referees,
    string? Status,
    string? Format,
    string? Visibility,
    int MaxPlayers,
    DateTimeOffset? StartAt,
    string? Ruleset,
    string? Description,
    string? DeckVisibility,
    string? DisasterMode,
    string? BanList,
    int RoundMinutes,
    int CheckInMinutes,
    IReadOnlyList<L12LegacyTournamentParticipantInput>? Participants,
    IReadOnlyList<L12LegacyTournamentRoundInput>? Rounds,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record L12TournamentLegacyImportPayload(
    IReadOnlyList<L12LegacyTournamentInput> Tournaments,
    string? PreviewHash = null);

public sealed record L12TournamentLegacyImportView(
    string PreviewHash,
    bool Applied,
    IReadOnlyList<L12TournamentView> Tournaments);

public sealed class L12TournamentScopeException : InvalidOperationException
{
    public L12TournamentScopeException(string message) : base(message) { }
}

public sealed class L12TournamentVersionConflictException : InvalidOperationException
{
    public L12TournamentVersionConflictException(string message) : base(message) { }
}

public sealed class L12TournamentPairingException : InvalidOperationException
{
    public L12TournamentPairingException(string message) : base(message) { }
}

public sealed record L12TournamentRoomPlayer(
    string AccountId,
    string Username,
    L12PresetDeckDefinition Deck,
    string DeckHash);

public sealed record L12TournamentRoomAssignment(
    string TournamentId,
    string TournamentCode,
    string MatchId,
    string RoomCode,
    L12TournamentRoomPlayer PlayerA,
    L12TournamentRoomPlayer PlayerB,
    L12OperationsPolicySnapshot OperationsPolicy,
    string DisasterMode,
    string RulesHash,
    DateTimeOffset? Deadline,
    bool CanPlay,
    bool CanSpectate);

public sealed partial class L12PlatformStore
{
    private sealed class TournamentRulesSnapshotRow
    {
        public string Ruleset { get; set; } = "现行规则";
        public string DisasterMode { get; set; } = "season";
        public string BanList { get; set; } = string.Empty;
        public List<string> DisasterCardIds { get; set; } = [];
        public List<L12CardRestrictionConfig> CardRestrictions { get; set; } = [];
        public string DeckVisibility { get; set; } = "after";
        public string Hash { get; set; } = string.Empty;
        public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class TournamentDeckSnapshotRow
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LockedAt { get; set; }
        public string MasterId { get; set; } = string.Empty;
        public List<string> CardIds { get; set; } = [];
        public List<string> MoraleIds { get; set; } = [];
        public List<string> SpecialIds { get; set; } = [];
    }

    private sealed class TournamentParticipantRow
    {
        public string AccountId { get; set; } = string.Empty;
        public bool CheckedIn { get; set; }
        public bool Dropped { get; set; }
        public bool Eliminated { get; set; }
        public int Seed { get; set; }
        public TournamentDeckSnapshotRow Deck { get; set; } = new();
    }

    private sealed class TournamentStandingRow
    {
        public int RoundNumber { get; set; }
        public int Rank { get; set; }
        public string AccountId { get; set; } = string.Empty;
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Draws { get; set; }
        public int Byes { get; set; }
        public int OpponentScore { get; set; }
        public int OpponentsOpponentScore { get; set; }
        public int Seed { get; set; }
    }

    private sealed class TournamentMatchEventRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Kind { get; set; } = string.Empty;
        public string? Result { get; set; }
        public string? RecordedMatchId { get; set; }
        public string ActorId { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class TournamentRulingRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string MatchId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string? TargetAccountId { get; set; }
        public string Decision { get; set; } = string.Empty;
        public int Minutes { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string ActorId { get; set; } = string.Empty;
        public string ActorName { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class TournamentMatchRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public int Table { get; set; }
        public string PlayerAAccountId { get; set; } = string.Empty;
        public string? PlayerBAccountId { get; set; }
        public string RoomCode { get; set; } = string.Empty;
        public bool ReadyA { get; set; }
        public bool ReadyB { get; set; }
        public string Status { get; set; } = "waiting";
        public string? Result { get; set; }
        public int TimeExtensionMinutes { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? Deadline { get; set; }
        public DateTimeOffset? GraceDeadline { get; set; }
        public string? RecordedMatchId { get; set; }
        public List<string> SourceMatchIds { get; set; } = [];
        public string RulesHash { get; set; } = string.Empty;
        public string? PlayerADeckHash { get; set; }
        public string? PlayerBDeckHash { get; set; }
        public int ReplayNumber { get; set; }
        public List<TournamentRulingRow> Rulings { get; set; } = [];
        public List<TournamentMatchEventRow> Events { get; set; } = [];
    }

    private sealed class TournamentRoundRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public int Number { get; set; }
        public string Status { get; set; } = "checkin";
        public bool Paused { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? PausedAt { get; set; }
        public int TotalPausedSeconds { get; set; }
        public string Stage { get; set; } = "swiss";
        public DateTimeOffset? StandingsCapturedAt { get; set; }
        public List<TournamentStandingRow> Standings { get; set; } = [];
        public string? PairingFailure { get; set; }
        public List<TournamentMatchRow> Matches { get; set; } = [];
    }

    private sealed class TournamentRow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string OrganizerAccountId { get; set; } = string.Empty;
        public List<string> RefereeAccountIds { get; set; } = [];
        public string Status { get; set; } = "registration";
        public string Format { get; set; } = "swiss";
        public string Visibility { get; set; } = "public";
        public int MaxPlayers { get; set; } = 16;
        public DateTimeOffset? StartAt { get; set; }
        public string Description { get; set; } = string.Empty;
        public TournamentRulesSnapshotRow Rules { get; set; } = new();
        public int RoundMinutes { get; set; } = 50;
        public int CheckInMinutes { get; set; } = 5;
        public int SwissRounds { get; set; } = 1;
        public int? CutSize { get; set; }
        public string RegistrationVisibility { get; set; } = "public";
        public int LateGraceMinutes { get; set; } = 5;
        public List<TournamentStandingRow> FinalSwissStandings { get; set; } = [];
        public List<TournamentParticipantRow> Participants { get; set; } = [];
        public List<TournamentRoundRow> Rounds { get; set; } = [];
        public long Version { get; set; } = 1;
        public string? LegacySourceId { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? CompletedAt { get; set; }
    }

    public L12TournamentListView Tournaments(L12AccountView viewer, string? status = null, string? search = null,
        bool mine = false)
    {
        lock (_gate)
        {
            EnsurePermission(viewer, L12Permission.TournamentsRead);
            var query = _data.Tournaments.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(row => row.Status == status.Trim());
            if (mine) query = query.Where(row => IsConfiguredStaff(row, viewer.Id)
                || row.Participants.Any(item => item.AccountId == viewer.Id));
            if (!string.IsNullOrWhiteSpace(search))
            {
                var value = search.Trim();
                query = query.Where(row => row.Name.Contains(value, StringComparison.OrdinalIgnoreCase)
                    || row.Code.Contains(value, StringComparison.OrdinalIgnoreCase));
            }
            query = query.Where(row => row.Visibility == "public" || IsConfiguredStaff(row, viewer.Id)
                || row.Participants.Any(item => item.AccountId == viewer.Id));
            return new L12TournamentListView(Version, query.OrderByDescending(row => row.UpdatedAt)
                .Select(row => ToView(row, viewer)).ToArray());
        }
    }

    public L12TournamentView? Tournament(L12AccountView viewer, string idOrCode)
    {
        lock (_gate)
        {
            EnsurePermission(viewer, L12Permission.TournamentsRead);
            var row = _data.Tournaments.FirstOrDefault(item => item.Id == idOrCode
                || string.Equals(item.Code, idOrCode, StringComparison.OrdinalIgnoreCase));
            if (row is null) return null;
            if (row.Visibility != "public" && !IsConfiguredStaff(row, viewer.Id)
                && !row.Participants.Any(item => item.AccountId == viewer.Id)) return null;
            return ToView(row, viewer);
        }
    }

    public L12TournamentView? TournamentByCode(L12AccountView viewer, string code)
    {
        lock (_gate)
        {
            EnsurePermission(viewer, L12Permission.TournamentsRead);
            var normalized = code.Trim();
            if (string.IsNullOrWhiteSpace(normalized)) return null;
            var row = _data.Tournaments.FirstOrDefault(item =>
                string.Equals(item.Code, normalized, StringComparison.OrdinalIgnoreCase));
            // code 可见赛事不进入公开列表；持有稳定分享码本身即是读取详情与报名入口。
            return row is null ? null : ToView(row, viewer);
        }
    }

    public L12TournamentView CreateTournament(L12AccountView actor, L12TournamentCreatePayload payload,
        L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            EnsurePermission(actor, L12Permission.TournamentsCreate);
            var row = NewTournament(actor, payload);
            if (!apply) return ToView(row, actor);
            _data.Tournaments.Add(row);
            AddAdminAudit(actor, "tournament", "create", row.Id, null, row.Code, row.Name, context);
            Save();
            return ToView(row, actor);
        }
    }

    public L12TournamentLegacyImportView ImportLegacyTournaments(L12AccountView actor,
        L12TournamentLegacyImportPayload payload, L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            EnsurePermission(actor, L12Permission.TournamentImportLegacy);
            if (payload.Tournaments.Count is < 1 or > 20)
                throw new ArgumentException("一次只能导入 1–20 个旧赛事");
            var previewHash = LegacyPreviewHash(payload.Tournaments);
            if (apply && !FixedEquals(payload.PreviewHash, previewHash))
                throw new L12TournamentVersionConflictException("旧赛事导入必须先预览并确认同一预览摘要");
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            var prepared = new List<TournamentRow>();
            foreach (var item in payload.Tournaments)
            {
                var sourceId = RequireText(item.Id, "旧赛事 ID", 128);
                if (!sourceIds.Add(sourceId)) throw new ArgumentException("导入批次包含重复旧赛事 ID");
                var existing = _data.Tournaments.FirstOrDefault(row => row.LegacySourceId == sourceId
                    && row.OrganizerAccountId == actor.Id);
                if (existing is not null)
                {
                    prepared.Add(CloneTournament(existing));
                    continue;
                }
                prepared.Add(ConvertLegacyTournament(actor, item));
            }
            if (!apply) return new L12TournamentLegacyImportView(previewHash, false,
                prepared.Select(row => ToView(row, actor)).ToArray());
            var added = prepared.Where(row => !_data.Tournaments.Any(existing => existing.Id == row.Id
                || existing.LegacySourceId == row.LegacySourceId && existing.OrganizerAccountId == actor.Id)).ToArray();
            _data.Tournaments.AddRange(added);
            foreach (var row in added)
                AddAdminAudit(actor, "tournament", "import-legacy", row.Id, row.LegacySourceId, row.Code,
                    "localStorage legacy import", context);
            if (added.Length > 0) Save();
            return new L12TournamentLegacyImportView(previewHash, true, prepared.Select(row => ToView(_data.Tournaments.FirstOrDefault(existing =>
                existing.LegacySourceId == row.LegacySourceId && existing.OrganizerAccountId == actor.Id) ?? row,
                actor)).ToArray());
        }
    }

    public L12TournamentView RegisterTournament(L12AccountView actor, string tournamentId,
        L12TournamentRegistrationPayload payload, long expectedVersion, L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            EnsurePermission(actor, L12Permission.TournamentsRegister);
            var row = RequireTournament(tournamentId, expectedVersion);
            if (row.Status != "registration") throw new L12TournamentVersionConflictException("赛事已停止报名");
            if (row.Participants.Any(item => item.AccountId == actor.Id))
                throw new L12TournamentVersionConflictException("账号已经报名该赛事");
            if (row.Participants.Count(item => !item.Dropped) >= row.MaxPlayers)
                throw new L12TournamentVersionConflictException("赛事名额已满");
            var deck = ResolveTournamentDeckSnapshot(actor.Id, row.Rules, payload);
            return Mutate(actor, row, "register", actor.Id, context, apply, working =>
                working.Participants.Add(new TournamentParticipantRow
                {
                    AccountId = actor.Id,
                    Seed = working.Participants.Count + 1,
                    Deck = deck,
                }));
        }
    }

    public L12TournamentView UpdateTournamentRegistration(L12AccountView actor, string tournamentId,
        L12TournamentRegistrationPayload payload, long expectedVersion, L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            EnsurePermission(actor, L12Permission.TournamentsRegister);
            var row = RequireTournament(tournamentId, expectedVersion);
            if (row.Status != "registration") throw new L12TournamentVersionConflictException("赛事开始后牌库快照已锁定");
            var participant = row.Participants.FirstOrDefault(item => item.AccountId == actor.Id)
                ?? throw new KeyNotFoundException("尚未报名该赛事");
            var deck = ResolveTournamentDeckSnapshot(actor.Id, row.Rules, payload);
            return Mutate(actor, row, "registration-update", actor.Id, context, apply, working =>
            {
                var target = working.Participants.First(item => item.AccountId == participant.AccountId);
                target.Deck = deck;
                target.Dropped = false;
            });
        }
    }

    public L12TournamentView DropTournament(L12AccountView actor, string tournamentId, long expectedVersion,
        L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            EnsurePermission(actor, L12Permission.TournamentsRegister);
            var row = RequireTournament(tournamentId, expectedVersion);
            if (!row.Participants.Any(item => item.AccountId == actor.Id))
                throw new KeyNotFoundException("尚未报名该赛事");
            return Mutate(actor, row, "drop", actor.Id, context, apply, working =>
            {
                var participant = working.Participants.First(item => item.AccountId == actor.Id);
                participant.Dropped = true;
                participant.CheckedIn = false;
            });
        }
    }

    public L12TournamentView SetTournamentStaff(L12AccountView actor, string tournamentId,
        L12TournamentStaffPayload payload, long expectedVersion, L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireOrganizer(actor, row);
            var referees = NormalizeReferees(row.OrganizerAccountId, payload.RefereeAccountIds);
            return Mutate(actor, row, "staff-set", tournamentId, context, apply,
                working => working.RefereeAccountIds = [.. referees]);
        }
    }

    public L12TournamentView StartTournament(L12AccountView actor, string tournamentId, long expectedVersion,
        L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireOrganizer(actor, row);
            if (row.Status != "registration") throw new L12TournamentVersionConflictException("赛事已开始或结束");
            var active = row.Participants.Where(item => !item.Dropped).ToArray();
            if (active.Length < 2) throw new ArgumentException("至少需要两名未退赛选手");
            if (row.Format == "swiss-cut" && (row.CutSize is not { } cutSize || active.Length < cutSize))
                throw new ArgumentException("未退赛选手数少于 cut 人数");
            if (active.Any(item => string.IsNullOrWhiteSpace(item.Deck.Hash)))
                throw new ArgumentException("所有参赛者必须先提交牌库快照");
            return Mutate(actor, row, "start", tournamentId, context, apply, working =>
            {
                var now = DateTimeOffset.UtcNow;
                working.Status = "running";
                var seed = 1;
                foreach (var participant in working.Participants
                             .OrderBy(item => item.Seed > 0 ? item.Seed : int.MaxValue)
                             .ThenBy(item => item.AccountId, StringComparer.Ordinal))
                    participant.Seed = seed++;
                foreach (var participant in working.Participants.Where(item => !item.Dropped))
                {
                    participant.Eliminated = false;
                    participant.CheckedIn = false;
                    participant.Deck.LockedAt = now;
                }
                working.Rounds.Add(working.Format == "single"
                    ? CreateInitialEliminationRound(working, working.Participants
                        .Where(item => !item.Dropped).OrderBy(item => item.Seed).ToArray(), 1)
                    : CreateSwissRound(working, 1));
            });
        }
    }

    public L12TournamentView CreateNextRound(L12AccountView actor, string tournamentId, long expectedVersion,
        L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireStaff(actor, row, L12Permission.TournamentsManage);
            if (row.Status != "running") throw new L12TournamentVersionConflictException("赛事未进行中");
            var previous = row.Rounds.LastOrDefault()
                ?? throw new L12TournamentVersionConflictException("赛事尚无轮次");
            if (previous.Status != "completed") throw new L12TournamentVersionConflictException("上一轮尚未完成");
            return Mutate(actor, row, "round-create", tournamentId, context, apply, working =>
            {
                foreach (var participant in working.Participants) participant.CheckedIn = false;
                var workingPrevious = working.Rounds.Last();
                TournamentRoundRow next;
                if (workingPrevious.Stage == "elimination")
                {
                    next = CreateNextEliminationRound(working, workingPrevious, workingPrevious.Number + 1);
                }
                else if (working.Format == "swiss-cut" && workingPrevious.Number >= working.SwissRounds)
                {
                    working.FinalSwissStandings = workingPrevious.Standings.Select(CloneStanding).ToList();
                    var activeStandings = working.FinalSwissStandings.Where(item =>
                        !working.Participants.First(participant => participant.AccountId == item.AccountId).Dropped)
                        .ToArray();
                    if (activeStandings.Length < working.CutSize!.Value)
                        throw new L12TournamentPairingException("未退赛选手数少于 cut 人数，未创建淘汰轮");
                    var cut = activeStandings.Take(working.CutSize.Value).Select(item => item.AccountId)
                        .ToHashSet(StringComparer.Ordinal);
                    foreach (var participant in working.Participants)
                        participant.Eliminated = !cut.Contains(participant.AccountId);
                    var finalists = activeStandings.Take(working.CutSize.Value)
                        .Select(item => working.Participants.First(participant => participant.AccountId == item.AccountId))
                        .ToArray();
                    next = CreateInitialEliminationRound(working, finalists, workingPrevious.Number + 1);
                }
                else
                {
                    if (working.Format != "league" && workingPrevious.Number >= working.SwissRounds)
                        throw new L12TournamentPairingException("已完成计划的全部瑞士轮");
                    next = CreateSwissRound(working, workingPrevious.Number + 1);
                }
                working.Rounds.Add(next);
            });
        }
    }

    public L12TournamentView CheckInTournament(L12AccountView actor, string tournamentId, int roundNumber,
        L12TournamentCheckInPayload payload, long expectedVersion, L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            var accountId = string.IsNullOrWhiteSpace(payload.AccountId) ? actor.Id : payload.AccountId.Trim();
            if (accountId != actor.Id) RequireStaff(actor, row, L12Permission.TournamentsManage);
            var round = RequireRound(row, roundNumber);
            if (round.Status is not ("checkin" or "running"))
                throw new L12TournamentVersionConflictException("当前轮次不接受签到");
            if (!round.Matches.Any(match => match.PlayerAAccountId == accountId || match.PlayerBAccountId == accountId))
                throw new KeyNotFoundException("账号不在当前轮次配对中");
            var currentMatch = round.Matches.First(match => match.PlayerAAccountId == accountId
                || match.PlayerBAccountId == accountId);
            if (round.Status == "running" && (currentMatch.Status != "waiting"
                    || currentMatch.GraceDeadline is not { } grace || grace < DateTimeOffset.UtcNow))
                throw new L12TournamentVersionConflictException("迟到宽限期已结束");
            return Mutate(actor, row, "check-in", accountId, context, apply, working =>
            {
                var workingRound = RequireRound(working, roundNumber);
                var match = workingRound.Matches.First(item => item.PlayerAAccountId == accountId
                    || item.PlayerBAccountId == accountId);
                if (match.PlayerAAccountId == accountId) match.ReadyA = payload.Ready;
                else match.ReadyB = payload.Ready;
                var participant = working.Participants.First(item => item.AccountId == accountId);
                participant.CheckedIn = payload.Ready;
                if (workingRound.Status == "running" && match.Status == "waiting"
                    && match.ReadyA && match.ReadyB)
                {
                    var now = DateTimeOffset.UtcNow;
                    match.Status = "running";
                    match.StartedAt = now;
                    match.Deadline = now.AddMinutes(working.RoundMinutes + match.TimeExtensionMinutes);
                    match.Events.Add(NewMatchEvent("late-table-start", null, null, actor.Id,
                        "双方在宽限期内到齐，本桌启动"));
                }
            });
        }
    }

    public L12TournamentView StartTournamentRound(L12AccountView actor, string tournamentId, int roundNumber,
        long expectedVersion, L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireStaff(actor, row, L12Permission.TournamentsManage);
            var round = RequireRound(row, roundNumber);
            if (round.Status != "checkin") throw new L12TournamentVersionConflictException("轮次不在签到阶段");
            return Mutate(actor, row, "round-start", round.Id, context, apply, working =>
            {
                var target = RequireRound(working, roundNumber);
                var now = DateTimeOffset.UtcNow;
                target.Status = "running";
                target.StartedAt = now;
                foreach (var match in target.Matches.Where(item => item.Status != "completed"))
                {
                    if (match.ReadyA && match.ReadyB)
                    {
                        match.Status = "running";
                        match.StartedAt = now;
                        match.Deadline = now.AddMinutes(working.RoundMinutes + match.TimeExtensionMinutes);
                        match.Events.Add(NewMatchEvent("table-start", null, null, actor.Id,
                            "轮次启动时双方已到齐"));
                    }
                    else
                    {
                        match.Status = "waiting";
                        match.GraceDeadline = now.AddMinutes(working.LateGraceMinutes);
                        match.Events.Add(NewMatchEvent("late-grace", null, null, actor.Id,
                            $"未到齐玩家有 {working.LateGraceMinutes} 分钟宽限期"));
                    }
                }
                FinalizeRoundIfComplete(working, target);
            });
        }
    }

    public L12TournamentView PauseTournamentRound(L12AccountView actor, string tournamentId, int roundNumber,
        L12TournamentPausePayload payload, long expectedVersion, L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireStaff(actor, row, L12Permission.TournamentsManage);
            var round = RequireRound(row, roundNumber);
            if (round.Status != "running") throw new L12TournamentVersionConflictException("轮次不在进行中");
            if (round.Paused == payload.Paused) throw new L12TournamentVersionConflictException("轮次暂停状态未变化");
            var reason = RequireText(payload.Reason, "暂停/恢复理由", 500);
            return Mutate(actor, row, payload.Paused ? "round-pause" : "round-resume", round.Id, context, apply,
                working =>
                {
                    var target = RequireRound(working, roundNumber);
                    var now = DateTimeOffset.UtcNow;
                    if (payload.Paused)
                    {
                        target.Paused = true;
                        target.PausedAt = now;
                    }
                    else
                    {
                        var seconds = target.PausedAt is null ? 0 : Math.Max(0, (int)(now - target.PausedAt.Value).TotalSeconds);
                        target.Paused = false;
                        target.PausedAt = null;
                        target.TotalPausedSeconds += seconds;
                        foreach (var match in target.Matches.Where(item => item.Deadline is not null && item.Status == "running"))
                            match.Deadline = match.Deadline!.Value.AddSeconds(seconds);
                    }
                    target.Matches.FirstOrDefault()?.Rulings.Add(NewRuling(actor, target.Matches.First().Id,
                        payload.Paused ? "pause" : "resume", null, payload.Paused ? "paused" : "resumed", 0, reason));
                });
        }
    }

    public L12TournamentView ExtendTournamentMatch(L12AccountView actor, string tournamentId, string matchId,
        L12TournamentTimeExtensionPayload payload, long expectedVersion, L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireStaff(actor, row, L12Permission.TournamentsManage);
            var (_, match) = RequireMatch(row, matchId);
            if (match.Status == "completed") throw new L12TournamentVersionConflictException("对局已完成");
            if (payload.Minutes is < 1 or > 60) throw new ArgumentException("单次补时必须为 1–60 分钟");
            var reason = RequireText(payload.Reason, "补时理由", 500);
            return Mutate(actor, row, "time-extension", matchId, context, apply, working =>
            {
                var (_, target) = RequireMatch(working, matchId);
                target.TimeExtensionMinutes += payload.Minutes;
                if (target.Deadline is not null) target.Deadline = target.Deadline.Value.AddMinutes(payload.Minutes);
                target.Rulings.Add(NewRuling(actor, matchId, "time-extension", null, "extended", payload.Minutes, reason));
            });
        }
    }

    public L12TournamentView ApplyTournamentRuling(L12AccountView actor, string tournamentId, string matchId,
        L12TournamentRulingPayload payload, long expectedVersion, L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireStaff(actor, row, L12Permission.TournamentRulingsWrite);
            var (matchRound, match) = RequireMatch(row, matchId);
            var kind = payload.Kind.Trim().ToLowerInvariant();
            if (kind is not ("result" or "penalty" or "no-show"))
                throw new ArgumentException("裁决类型必须为 result、penalty 或 no-show");
            var reason = RequireText(payload.Reason, "裁决理由", 1000);
            ValidateRuling(matchRound, match, kind, payload.TargetAccountId, payload.Decision);
            return Mutate(actor, row, "ruling", matchId, context, apply, working =>
            {
                var (round, target) = RequireMatch(working, matchId);
                var decision = payload.Decision.Trim().ToLowerInvariant();
                target.Rulings.Add(NewRuling(actor, matchId, kind, payload.TargetAccountId, decision, 0, reason));
                if (kind is "result" or "no-show")
                {
                    target.Result = decision;
                    target.Status = "completed";
                    target.Events.Add(NewMatchEvent(kind, decision, null, actor.Id, reason));
                    FinalizeRoundIfComplete(working, round);
                }
            });
        }
    }

    public L12TournamentView LinkTournamentMatch(L12AccountView actor, string tournamentId, string matchId,
        L12TournamentMatchReferencePayload payload, long expectedVersion, L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireStaff(actor, row, L12Permission.TournamentRulingsWrite);
            var (_, match) = RequireMatch(row, matchId);
            var reference = RequireText(payload.RecordedMatchId, "对局记录 ID", 128);
            if (!string.IsNullOrWhiteSpace(match.RecordedMatchId) && match.RecordedMatchId != reference)
                throw new L12TournamentVersionConflictException("该桌已绑定其他对局记录");
            return Mutate(actor, row, "match-reference", matchId, context, apply, working =>
                RequireMatch(working, matchId).Match.RecordedMatchId = reference);
        }
    }

    public L12TournamentView CompleteTournament(L12AccountView actor, string tournamentId, long expectedVersion,
        L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireOrganizer(actor, row);
            if (row.Status != "running") throw new L12TournamentVersionConflictException("赛事未进行中");
            if (row.Rounds.Count == 0 || row.Rounds.Any(round => round.Status != "completed"))
                throw new L12TournamentVersionConflictException("仍有未完成轮次");
            if (row.Format is "swiss" or "league"
                && row.Rounds.Count(round => round.Stage == "swiss") < row.SwissRounds)
                throw new L12TournamentVersionConflictException("计划的瑞士轮尚未全部完成");
            if (row.Format == "swiss-cut" && !row.Rounds.Any(round => round.Stage == "elimination"))
                throw new L12TournamentVersionConflictException("尚未进入 cut 淘汰阶段");
            if (row.Format is "single" or "swiss-cut")
            {
                var elimination = row.Rounds.LastOrDefault(round => round.Stage == "elimination");
                if (elimination is null || elimination.Matches.Count != 1
                    || WinnerAccountId(elimination.Matches[0]) is null)
                    throw new L12TournamentVersionConflictException("淘汰树尚未产生最终冠军");
            }
            return Mutate(actor, row, "complete", tournamentId, context, apply, working =>
            {
                working.Status = "completed";
                working.CompletedAt = DateTimeOffset.UtcNow;
            });
        }
    }

    public L12TournamentView RequestTournamentRematch(L12AccountView actor, string tournamentId, string matchId,
        L12TournamentRematchPayload payload, long expectedVersion, L12AdminAuditContext context, bool apply)
    {
        lock (_gate)
        {
            var row = RequireTournament(tournamentId, expectedVersion);
            RequireStaff(actor, row, L12Permission.TournamentRulingsWrite);
            var (round, match) = RequireMatch(row, matchId);
            if (match.Status != "completed" || match.PlayerBAccountId is null)
                throw new L12TournamentVersionConflictException("只能对已完成的非轮空桌次发起重赛");
            if (row.Rounds.Any(item => item.Number > round.Number))
                throw new L12TournamentVersionConflictException("后续轮次已创建，不能回溯重赛");
            var reason = RequireText(payload.Reason, "重赛理由", 1000);
            return Mutate(actor, row, "rematch", matchId, context, apply, working =>
            {
                var (targetRound, target) = RequireMatch(working, matchId);
                target.Events.Add(NewMatchEvent("rematch", target.Result, target.RecordedMatchId,
                    actor.Id, reason));
                target.ReplayNumber++;
                target.RoomCode = RandomCode(8);
                target.Status = "waiting";
                target.Result = null;
                target.RecordedMatchId = null;
                target.ReadyA = target.ReadyB = false;
                target.StartedAt = null;
                target.Deadline = null;
                target.GraceDeadline = null;
                targetRound.Status = "checkin";
                targetRound.Standings = [];
                targetRound.StandingsCapturedAt = null;
                if (targetRound.Stage == "swiss") working.FinalSwissStandings = [];
                foreach (var accountId in new[] { target.PlayerAAccountId, target.PlayerBAccountId })
                {
                    var participant = working.Participants.First(item => item.AccountId == accountId);
                    participant.CheckedIn = false;
                    participant.Eliminated = false;
                }
            });
        }
    }

    public L12TournamentRoomAssignment TournamentRoomAssignment(string accountId, string tournamentId,
        string matchId, bool spectate)
    {
        lock (_gate)
        {
            var account = AccountById(accountId) ?? throw new L12TournamentScopeException("账号不存在");
            if (account.Disabled) throw new L12TournamentScopeException("账号已禁用");
            var tournament = _data.Tournaments.FirstOrDefault(item => item.Id == tournamentId
                    || string.Equals(item.Code, tournamentId, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("赛事不存在");
            var (round, match) = RequireMatch(tournament, matchId);
            if (tournament.Status != "running" || round.Status != "running" || match.Status != "running")
                throw new L12TournamentVersionConflictException("本桌赛事房间尚未启动或已结束");
            var canPlay = match.PlayerAAccountId == accountId || match.PlayerBAccountId == accountId;
            var canSpectate = IsStaff(tournament, accountId);
            if (spectate ? !canSpectate : !canPlay)
                throw new L12TournamentScopeException(spectate
                    ? "仅赛事主办者与裁判可观战本桌"
                    : "仅本桌配对玩家可进入专属房间");
            var playerA = RequireTournamentRoomPlayer(tournament, match.PlayerAAccountId,
                match.PlayerADeckHash);
            var playerB = RequireTournamentRoomPlayer(tournament, match.PlayerBAccountId!,
                match.PlayerBDeckHash);
            var current = ToPolicySnapshot(RequireOperationsConfig());
            var policy = current with
            {
                Version = tournament.Version,
                VersionId = $"tournament:{tournament.Id}:{tournament.Rules.Hash}",
                DisasterCardIds = tournament.Rules.DisasterCardIds.ToArray(),
                CardRestrictions = tournament.Rules.CardRestrictions.ToArray(),
                DefaultPresetDeckIds = [],
                DefaultRoomConfig = new L12DefaultRoomConfig("tournament", "disabled", "request",
                    tournament.Rules.DisasterMode),
                MatchModes = [new L12MatchModeConfig("tournament", "赛事对局", true)],
                Maintenance = new L12MaintenanceConfig(false, string.Empty, null, null),
            };
            return new L12TournamentRoomAssignment(tournament.Id, tournament.Code, match.Id, match.RoomCode,
                playerA, playerB, policy, tournament.Rules.DisasterMode, tournament.Rules.Hash,
                match.Deadline, canPlay, canSpectate);
        }
    }

    public L12TournamentView RecordTournamentGameResult(string tournamentId, string matchId,
        string recordedMatchId, int winnerIndex)
    {
        lock (_gate)
        {
            var tournament = _data.Tournaments.FirstOrDefault(item => item.Id == tournamentId)
                ?? throw new KeyNotFoundException("赛事不存在");
            var (round, match) = RequireMatch(tournament, matchId);
            var reference = RequireText(recordedMatchId, "对局记录 ID", 128);
            var result = winnerIndex switch
            {
                0 => "player-a",
                1 => "player-b",
                _ => throw new ArgumentException("权威对局胜者索引无效"),
            };
            if (_data.Tournaments.SelectMany(item => item.Rounds).SelectMany(item => item.Matches)
                .Any(item => item.Id != match.Id && item.RecordedMatchId == reference))
                throw new L12TournamentVersionConflictException("该对局记录已绑定其他赛事桌次");
            var staffResolution = match.Status == "completed"
                && match.Rulings.Any(item => item.Kind is "result" or "no-show");
            if (match.Status == "completed" && match.RecordedMatchId == reference
                && (match.Result == result || staffResolution))
                return ToView(tournament, ToView(AccountById(tournament.OrganizerAccountId)!));
            if (match.Status == "completed" && string.IsNullOrWhiteSpace(match.RecordedMatchId)
                && staffResolution)
            {
                // 工作人员裁决可能早于已在运行的房间结束。保留裁决赛果，
                // 但绑定真实对局记录，使房间恢复不会永久停留在待回写状态。
                match.RecordedMatchId = reference;
                match.Events.Add(NewMatchEvent("game-result-after-ruling", result, reference, "game-engine",
                    "对局在工作人员裁决后结束；保留裁决赛果并绑定对局记录"));
                tournament.Version++;
                tournament.UpdatedAt = DateTimeOffset.UtcNow;
                Save();
                return ToView(tournament, ToView(AccountById(tournament.OrganizerAccountId)!));
            }
            if (match.Status != "running")
                throw new L12TournamentVersionConflictException("本桌不在可回写赛果的进行状态");
            if (!string.IsNullOrWhiteSpace(match.RecordedMatchId) && match.RecordedMatchId != reference)
                throw new L12TournamentVersionConflictException("本桌已绑定其他对局记录，不能覆盖权威记录");
            match.RecordedMatchId = reference;
            match.Result = result;
            match.Status = "completed";
            match.Events.Add(NewMatchEvent("game-result", result, reference, "game-engine",
                "由赛事专属房间权威回写赛果"));
            FinalizeRoundIfComplete(tournament, round);
            tournament.Version++;
            tournament.UpdatedAt = DateTimeOffset.UtcNow;
            Save();
            return ToView(tournament, ToView(AccountById(tournament.OrganizerAccountId)!));
        }
    }

    private L12TournamentRoomPlayer RequireTournamentRoomPlayer(TournamentRow tournament, string accountId,
        string? expectedDeckHash)
    {
        var participant = tournament.Participants.FirstOrDefault(item => item.AccountId == accountId)
            ?? throw new L12TournamentVersionConflictException("赛事房间玩家不在报名快照中");
        if (participant.Dropped || participant.Eliminated)
            throw new L12TournamentVersionConflictException("该玩家已退赛或被淘汰");
        if (string.IsNullOrWhiteSpace(participant.Deck.MasterId)
            || participant.Deck.CardIds.Count == 0 || participant.Deck.MoraleIds.Count == 0)
            throw new L12TournamentVersionConflictException("该报名使用旧版牌库快照，需在开赛前重新选择账号牌库");
        if (!string.Equals(participant.Deck.Hash, expectedDeckHash, StringComparison.Ordinal))
            throw new L12TournamentVersionConflictException("桌次绑定的牌库快照与报名快照不一致");
        var account = AccountById(accountId) ?? throw new L12TournamentVersionConflictException("参赛账号不存在");
        return new L12TournamentRoomPlayer(accountId, account.Username, new L12PresetDeckDefinition
        {
            Name = participant.Deck.Name,
            MasterId = participant.Deck.MasterId,
            CardIds = [.. participant.Deck.CardIds],
            MoraleIds = [.. participant.Deck.MoraleIds],
            SpecialIds = [.. participant.Deck.SpecialIds],
        }, participant.Deck.Hash);
    }

    internal long? AdminCommandResourceVersion(string type, string scope)
    {
        lock (_gate)
        {
            if (type.StartsWith("account.", StringComparison.Ordinal))
            {
                const string prefix = "account:";
                if (!scope.StartsWith(prefix, StringComparison.Ordinal)) return null;
                var accountId = scope[prefix.Length..].Split('/', 2)[0];
                return _data.Accounts.FirstOrDefault(row => row.Id == accountId)?.PermissionVersion;
            }
            if (type.StartsWith("operations.config.", StringComparison.Ordinal))
                return _data.OperationsConfig?.Version ?? 1;
            if (type.StartsWith("release.", StringComparison.Ordinal))
            {
                var environment = ReleaseEnvironmentFromScope(scope);
                return environment is null ? Version : ReleaseEnvironmentVersion(environment);
            }
            if (!type.StartsWith("tournament.", StringComparison.Ordinal)) return Version;
            var tournamentId = TournamentIdFromScope(scope);
            if (tournamentId is null) return Version;
            return _data.Tournaments.FirstOrDefault(row => row.Id == tournamentId)?.Version;
        }
    }

    private L12TournamentView Mutate(L12AccountView actor, TournamentRow row, string action, string target,
        L12AdminAuditContext context, bool apply, Action<TournamentRow> mutation)
    {
        // 先在副本上完成全部校验与编排。配对等操作抛错时，
        // 不得在内存中留下半完成状态，即使 Save 尚未执行也一样。
        var working = CloneTournament(row);
        mutation(working);
        working.Version++;
        working.UpdatedAt = DateTimeOffset.UtcNow;
        if (!apply) return ToView(working, actor);
        var index = _data.Tournaments.FindIndex(item => item.Id == row.Id);
        if (index < 0) throw new KeyNotFoundException("赛事不存在");
        _data.Tournaments[index] = working;
        AddAdminAudit(actor, "tournament", action, target, (working.Version - 1).ToString(),
            working.Version.ToString(), context.Reason, context);
        Save();
        return ToView(working, actor);
    }

    private TournamentRow NewTournament(L12AccountView actor, L12TournamentCreatePayload payload)
    {
        var name = RequireText(payload.Name, "赛事名称", 100);
        var format = Allowed(payload.Format, "赛制", "single", "swiss", "swiss-cut", "league");
        var visibility = Allowed(payload.Visibility, "可见性", "public", "code");
        var registrationVisibility = Allowed(payload.RegistrationVisibility, "报名名单可见性", "public", "staff");
        if (payload.MaxPlayers is < 2 or > 256) throw new ArgumentException("赛事人数必须为 2–256");
        if (payload.RoundMinutes is < 5 or > 240) throw new ArgumentException("每轮时长必须为 5–240 分钟");
        if (payload.CheckInMinutes is < 1 or > 60) throw new ArgumentException("签到时限必须为 1–60 分钟");
        if (payload.LateGraceMinutes is < 0 or > 60) throw new ArgumentException("迟到宽限期必须为 0–60 分钟");
        if (format is "swiss" or "swiss-cut" && payload.SwissRounds is < 1 or > 20)
            throw new ArgumentException("瑞士轮数必须为 1–20");
        if (format == "swiss-cut" && (payload.CutSize is null || payload.CutSize is < 2
                || payload.CutSize > payload.MaxPlayers))
            throw new ArgumentException("瑞士 cut 人数必须为 2 至赛事人数上限");
        if (format != "swiss-cut" && payload.CutSize is not null)
            throw new ArgumentException("仅瑞士 cut 赛制可设置 cut 人数");
        var policy = ToPolicySnapshot(RequireOperationsConfig());
        var rules = RulesSnapshot(payload.Ruleset, payload.DisasterMode, payload.BanList,
            payload.DeckVisibility, payload.DisasterCardIds ?? policy.DisasterCardIds,
            payload.CardRestrictions ?? policy.CardRestrictions);
        var now = DateTimeOffset.UtcNow;
        return new TournamentRow
        {
            Code = UniqueTournamentCode(),
            Name = name,
            OrganizerAccountId = actor.Id,
            RefereeAccountIds = [.. NormalizeReferees(actor.Id, payload.RefereeAccountIds ?? [])],
            Format = format,
            Visibility = visibility,
            MaxPlayers = payload.MaxPlayers,
            StartAt = payload.StartAt,
            Description = OptionalText(payload.Description, 2000),
            Rules = rules,
            RoundMinutes = payload.RoundMinutes,
            CheckInMinutes = payload.CheckInMinutes,
            SwissRounds = format is "single" ? 0 : payload.SwissRounds,
            CutSize = format == "swiss-cut" ? payload.CutSize : null,
            RegistrationVisibility = registrationVisibility,
            LateGraceMinutes = payload.LateGraceMinutes,
            Participants =
            [
                new TournamentParticipantRow { AccountId = actor.Id, Seed = 1, Deck = new TournamentDeckSnapshotRow() },
            ],
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private TournamentRow ConvertLegacyTournament(L12AccountView actor, L12LegacyTournamentInput input)
    {
        var create = new L12TournamentCreatePayload(input.Name, input.Format ?? "swiss",
            input.Visibility ?? "public", input.MaxPlayers is >= 2 and <= 256 ? input.MaxPlayers : 16,
            input.StartAt, input.Ruleset ?? "现行规则", input.Description ?? string.Empty,
            input.DeckVisibility ?? "after", input.DisasterMode ?? "season", input.BanList ?? string.Empty,
            input.RoundMinutes is >= 5 and <= 240 ? input.RoundMinutes : 50,
            input.CheckInMinutes is >= 1 and <= 60 ? input.CheckInMinutes : 5,
            ResolveLegacyReferees(input.Referees));
        var row = NewTournament(actor, create);
        row.LegacySourceId = input.Id.Trim();
        row.Code = ValidLegacyCode(input.Code) && !_data.Tournaments.Any(item => item.Code == input.Code)
            ? input.Code!.ToUpperInvariant() : UniqueTournamentCode();
        row.Status = AllowedOrDefault(input.Status, "registration", "registration", "running", "completed");
        row.CreatedAt = input.CreatedAt ?? DateTimeOffset.UtcNow;
        row.UpdatedAt = input.UpdatedAt ?? row.CreatedAt;
        row.CompletedAt = row.Status == "completed" ? input.CompletedAt ?? row.UpdatedAt : null;
        row.Participants.Clear();
        foreach (var person in input.Participants ?? [])
        {
            var account = _data.Accounts.FirstOrDefault(item => string.Equals(item.Username, person.Name,
                StringComparison.OrdinalIgnoreCase));
            if (account is null || row.Participants.Any(item => item.AccountId == account.Id)) continue;
            row.Participants.Add(new TournamentParticipantRow
            {
                AccountId = account.Id,
                CheckedIn = person.CheckedIn,
                Dropped = person.Dropped,
                Deck = DeckSnapshot(person.DeckName ?? string.Empty, person.DeckCode ?? string.Empty),
            });
        }
        if (!row.Participants.Any(item => item.AccountId == actor.Id))
            row.Participants.Insert(0, new TournamentParticipantRow { AccountId = actor.Id });
        if (row.Participants.Count > row.MaxPlayers) row.Participants = row.Participants.Take(row.MaxPlayers).ToList();
        row.Rounds = ConvertLegacyRounds(actor, row, input.Rounds ?? []);
        if (row.Status != "registration")
            foreach (var participant in row.Participants) participant.Deck.LockedAt ??= row.UpdatedAt;
        return row;
    }

    private List<TournamentRoundRow> ConvertLegacyRounds(L12AccountView actor, TournamentRow tournament,
        IReadOnlyList<L12LegacyTournamentRoundInput> rounds)
    {
        if (rounds.Count > 100) throw new ArgumentException("旧赛事轮次超过100轮上限");
        var result = new List<TournamentRoundRow>();
        foreach (var source in rounds.OrderBy(item => item.Number))
        {
            if (source.Number < 1 || result.Any(item => item.Number == source.Number)) continue;
            var round = new TournamentRoundRow
            {
                Number = source.Number,
                Status = AllowedOrDefault(source.Status, "checkin", "pending", "checkin", "running", "completed"),
                Paused = source.Paused,
                StartedAt = source.StartedAt,
            };
            foreach (var legacyMatch in source.Matches ?? [])
            {
                var a = AccountIdByUsername(legacyMatch.PlayerA);
                var b = legacyMatch.PlayerB == "轮空" ? null : AccountIdByUsername(legacyMatch.PlayerB);
                if (a is null || b is null && legacyMatch.PlayerB != "轮空") continue;
                var match = new TournamentMatchRow
                {
                    Id = string.IsNullOrWhiteSpace(legacyMatch.Id) ? Guid.NewGuid().ToString("N") : legacyMatch.Id.Trim(),
                    Table = Math.Max(1, legacyMatch.Table),
                    PlayerAAccountId = a,
                    PlayerBAccountId = b,
                    RoomCode = ValidRoomCode(legacyMatch.RoomCode) ? legacyMatch.RoomCode!.ToUpperInvariant() : RandomCode(8),
                    ReadyA = legacyMatch.ReadyA,
                    ReadyB = b is null || legacyMatch.ReadyB,
                    Status = AllowedOrDefault(legacyMatch.Status, b is null ? "completed" : "waiting",
                        "waiting", "running", "completed"),
                    Result = OptionalText(legacyMatch.Result, 500),
                    TimeExtensionMinutes = Math.Clamp(legacyMatch.TimeExtension, 0, 1440),
                    StartedAt = legacyMatch.StartedAt,
                    Deadline = legacyMatch.Deadline,
                };
                if (!string.IsNullOrWhiteSpace(legacyMatch.Ruling))
                    match.Rulings.Add(NewRuling(actor, match.Id, "legacy-note", null, "imported", 0,
                        OptionalText(legacyMatch.Ruling, 1000)));
                round.Matches.Add(match);
            }
            result.Add(round);
        }
        return result;
    }

    private TournamentRoundRow CreateSwissRound(TournamentRow tournament, int number)
    {
        var active = tournament.Participants.Where(item => !item.Dropped && !item.Eliminated)
            .OrderBy(item => item.Seed).ThenBy(item => item.AccountId, StringComparer.Ordinal).ToArray();
        if (active.Length < 2) throw new ArgumentException("至少需要两名未退赛选手");
        var standings = number == 1
            ? active.Select((item, index) => new TournamentStandingRow
            {
                RoundNumber = 0, Rank = index + 1, AccountId = item.AccountId, Seed = item.Seed,
            }).ToList()
            : ComputeStandings(tournament, number - 1);
        var standingByAccount = standings.ToDictionary(item => item.AccountId, StringComparer.Ordinal);
        var ordered = active.OrderBy(item => standingByAccount[item.AccountId].Rank).ToArray();
        var previousPairs = tournament.Rounds.Where(round => round.Stage == "swiss")
            .SelectMany(round => round.Matches)
            .Where(match => match.PlayerBAccountId is not null)
            .Select(match => TournamentPairKey(match.PlayerAAccountId, match.PlayerBAccountId!))
            .ToHashSet(StringComparer.Ordinal);

        TournamentParticipantRow? bye = null;
        List<(TournamentParticipantRow A, TournamentParticipantRow B)>? pairs = null;
        if (ordered.Length % 2 == 0)
        {
            if (!TryBuildSwissPairs(ordered, standingByAccount, previousPairs, out pairs))
                throw new L12TournamentPairingException("无法生成不重复交手的完整配对；本轮未创建");
        }
        else
        {
            var priorByes = tournament.Rounds.Where(round => round.Stage == "swiss")
                .SelectMany(round => round.Matches)
                .Where(match => match.PlayerBAccountId is null && match.Result == "bye")
                .Select(match => match.PlayerAAccountId).ToHashSet(StringComparer.Ordinal);
            var byeCandidates = ordered.OrderBy(item => priorByes.Contains(item.AccountId) ? 1 : 0)
                .ThenByDescending(item => standingByAccount[item.AccountId].Rank).ToArray();
            foreach (var candidate in byeCandidates)
            {
                var remaining = ordered.Where(item => item.AccountId != candidate.AccountId).ToArray();
                if (!TryBuildSwissPairs(remaining, standingByAccount, previousPairs, out var attempt)) continue;
                bye = candidate;
                pairs = attempt;
                break;
            }
            if (pairs is null)
                throw new L12TournamentPairingException("无法生成不重复交手的完整配对；本轮未创建");
        }

        var round = new TournamentRoundRow { Number = number, Stage = "swiss" };
        foreach (var pair in pairs!)
            round.Matches.Add(NewTournamentMatch(tournament, pair.A, pair.B, round.Matches.Count + 1, []));
        if (bye is not null)
        {
            var byeMatch = NewTournamentMatch(tournament, bye, null, round.Matches.Count + 1, []);
            byeMatch.Events.Add(NewMatchEvent("bye", "bye", null, "system", "按最低排名且优先无轮空记录的确定性规则轮空"));
            round.Matches.Add(byeMatch);
        }
        return round;
    }

    private static bool TryBuildSwissPairs(IReadOnlyList<TournamentParticipantRow> participants,
        IReadOnlyDictionary<string, TournamentStandingRow> standings, IReadOnlySet<string> previousPairs,
        out List<(TournamentParticipantRow A, TournamentParticipantRow B)> pairs)
    {
        var builtPairs = new List<(TournamentParticipantRow A, TournamentParticipantRow B)>();
        bool Search(List<TournamentParticipantRow> remaining)
        {
            if (remaining.Count == 0) return true;
            var first = remaining[0];
            var firstStanding = standings[first.AccountId];
            var candidates = remaining.Skip(1)
                .Where(candidate => Math.Abs(firstStanding.Wins - standings[candidate.AccountId].Wins) <= 1
                    && !previousPairs.Contains(TournamentPairKey(first.AccountId, candidate.AccountId)))
                .OrderBy(candidate => standings[candidate.AccountId].Wins == firstStanding.Wins ? 0 : 1)
                .ThenByDescending(candidate => standings[candidate.AccountId].Wins < firstStanding.Wins
                    ? standings[candidate.AccountId].OpponentScore : int.MinValue)
                .ThenBy(candidate => standings[candidate.AccountId].Rank)
                .ThenBy(candidate => candidate.Seed)
                .ToArray();
            foreach (var opponent in candidates)
            {
                builtPairs.Add((first, opponent));
                var next = remaining.Where(item => item.AccountId != first.AccountId
                    && item.AccountId != opponent.AccountId).ToList();
                if (Search(next)) return true;
                builtPairs.RemoveAt(builtPairs.Count - 1);
            }
            return false;
        }

        var success = Search(participants.OrderBy(item => standings[item.AccountId].Rank).ToList());
        pairs = builtPairs;
        return success;
    }

    private TournamentRoundRow CreateInitialEliminationRound(TournamentRow tournament,
        IReadOnlyList<TournamentParticipantRow> seededParticipants, int number)
    {
        if (seededParticipants.Count < 2) throw new L12TournamentPairingException("淘汰赛至少需要两名选手");
        var bracketSize = 1;
        while (bracketSize < seededParticipants.Count) bracketSize *= 2;
        var seedSlots = EliminationSeedSlots(bracketSize);
        var bySeed = seededParticipants.Select((participant, index) => (participant, seed: index + 1))
            .ToDictionary(item => item.seed, item => item.participant);
        var round = new TournamentRoundRow { Number = number, Stage = "elimination" };
        for (var slot = 0; slot < seedSlots.Count; slot += 2)
        {
            bySeed.TryGetValue(seedSlots[slot], out var first);
            bySeed.TryGetValue(seedSlots[slot + 1], out var second);
            if (first is null && second is null) continue;
            if (first is null) (first, second) = (second, first);
            var match = NewTournamentMatch(tournament, first!, second, round.Matches.Count + 1, []);
            if (second is null)
                match.Events.Add(NewMatchEvent("bracket-bye", "bye", null, "system", "淘汰树空缺席位，该种子直接晋级"));
            round.Matches.Add(match);
        }
        if (round.Matches.All(match => match.Status == "completed")) round.Status = "completed";
        return round;
    }

    private TournamentRoundRow CreateNextEliminationRound(TournamentRow tournament,
        TournamentRoundRow previous, int number)
    {
        if (previous.Matches.Count <= 1)
            throw new L12TournamentPairingException("淘汰赛已产生最终冠军，不能再创建轮次");
        var winners = previous.Matches.OrderBy(match => match.Table).Select(match =>
        {
            var accountId = WinnerAccountId(match)
                ?? throw new L12TournamentPairingException("上轮淘汰赛存在无法晋级的赛果");
            var participant = tournament.Participants.First(item => item.AccountId == accountId);
            return (Participant: participant.Dropped ? null : participant, MatchId: match.Id);
        }).ToArray();
        var round = new TournamentRoundRow { Number = number, Stage = "elimination" };
        for (var index = 0; index < winners.Length; index += 2)
        {
            if (index + 1 >= winners.Length)
                throw new L12TournamentPairingException("淘汰树上轮晋级数量无效");
            var first = winners[index].Participant;
            var second = winners[index + 1].Participant;
            if (first is null && second is null) continue;
            if (first is null) (first, second) = (second, first);
            var match = NewTournamentMatch(tournament, first!, second, round.Matches.Count + 1,
                [winners[index].MatchId, winners[index + 1].MatchId]);
            if (second is null)
                match.Events.Add(NewMatchEvent("bracket-drop-bye", "bye", null, "system",
                    "对侧上轮胜者已退赛，本分支轮空晋级"));
            round.Matches.Add(match);
        }
        if (round.Matches.Count == 0)
            throw new L12TournamentPairingException("上轮胜者均已退赛，未创建淘汰轮");
        if (round.Matches.All(match => match.Status == "completed")) round.Status = "completed";
        return round;
    }

    private static IReadOnlyList<int> EliminationSeedSlots(int size)
    {
        var slots = new List<int> { 1, 2 };
        for (var bracket = 4; bracket <= size; bracket *= 2)
        {
            var next = new List<int>(bracket);
            foreach (var seed in slots)
            {
                next.Add(seed);
                next.Add(bracket + 1 - seed);
            }
            slots = next;
        }
        return slots;
    }

    private static TournamentMatchRow NewTournamentMatch(TournamentRow tournament,
        TournamentParticipantRow first, TournamentParticipantRow? second, int table,
        IReadOnlyList<string> sourceMatchIds)
        => new()
        {
            Table = table,
            PlayerAAccountId = first.AccountId,
            PlayerBAccountId = second?.AccountId,
            RoomCode = RandomCode(8),
            ReadyB = second is null,
            Status = second is null ? "completed" : "waiting",
            Result = second is null ? "bye" : null,
            SourceMatchIds = [.. sourceMatchIds],
            RulesHash = tournament.Rules.Hash,
            PlayerADeckHash = first.Deck.Hash,
            PlayerBDeckHash = second?.Deck.Hash,
        };

    private static List<TournamentStandingRow> ComputeStandings(TournamentRow tournament, int roundNumber)
    {
        var accounts = tournament.Participants.ToDictionary(item => item.AccountId, StringComparer.Ordinal);
        var wins = accounts.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var losses = accounts.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var draws = accounts.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var byes = accounts.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var opponents = accounts.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        var matches = tournament.Rounds.Where(round => round.Stage == "swiss" && round.Number <= roundNumber)
            .SelectMany(round => round.Matches).Where(match => match.Status == "completed").ToArray();
        foreach (var match in matches)
        {
            if (!wins.ContainsKey(match.PlayerAAccountId)) continue;
            if (match.PlayerBAccountId is null)
            {
                if (match.Result == "bye") { wins[match.PlayerAAccountId]++; byes[match.PlayerAAccountId]++; }
                continue;
            }
            if (!wins.ContainsKey(match.PlayerBAccountId)) continue;
            opponents[match.PlayerAAccountId].Add(match.PlayerBAccountId);
            opponents[match.PlayerBAccountId].Add(match.PlayerAAccountId);
            switch (match.Result)
            {
                case "player-a": case "no-show-b":
                    wins[match.PlayerAAccountId]++; losses[match.PlayerBAccountId]++;
                    break;
                case "player-b": case "no-show-a":
                    wins[match.PlayerBAccountId]++; losses[match.PlayerAAccountId]++;
                    break;
                case "draw":
                    draws[match.PlayerAAccountId]++; draws[match.PlayerBAccountId]++;
                    break;
            }
        }
        var opponentScores = accounts.Keys.ToDictionary(id => id,
            id => opponents[id].Sum(opponent => wins[opponent]), StringComparer.Ordinal);
        var opponentOpponentScores = accounts.Keys.ToDictionary(id => id,
            id => opponents[id].Sum(opponent => opponentScores[opponent]), StringComparer.Ordinal);
        var result = accounts.Values.Select(participant => new TournamentStandingRow
        {
            RoundNumber = roundNumber,
            AccountId = participant.AccountId,
            Wins = wins[participant.AccountId],
            Losses = losses[participant.AccountId],
            Draws = draws[participant.AccountId],
            Byes = byes[participant.AccountId],
            OpponentScore = opponentScores[participant.AccountId],
            OpponentsOpponentScore = opponentOpponentScores[participant.AccountId],
            Seed = participant.Seed,
        }).ToList();
        result.Sort((first, second) => CompareStanding(tournament, matches, first, second));
        for (var index = 0; index < result.Count; index++) result[index].Rank = index + 1;
        return result;
    }

    private static int CompareStanding(TournamentRow tournament, IReadOnlyList<TournamentMatchRow> matches,
        TournamentStandingRow first, TournamentStandingRow second)
    {
        var value = second.Wins.CompareTo(first.Wins);
        if (value != 0) return value;
        value = second.OpponentScore.CompareTo(first.OpponentScore);
        if (value != 0) return value;
        value = second.OpponentsOpponentScore.CompareTo(first.OpponentsOpponentScore);
        if (value != 0) return value;
        var direct = matches.LastOrDefault(match => match.PlayerBAccountId is not null
            && (match.PlayerAAccountId == first.AccountId && match.PlayerBAccountId == second.AccountId
                || match.PlayerAAccountId == second.AccountId && match.PlayerBAccountId == first.AccountId));
        var winner = direct is null ? null : WinnerAccountId(direct);
        if (winner == first.AccountId) return -1;
        if (winner == second.AccountId) return 1;
        value = first.Seed.CompareTo(second.Seed);
        return value != 0 ? value : string.CompareOrdinal(first.AccountId, second.AccountId);
    }

    private static void FinalizeRoundIfComplete(TournamentRow tournament, TournamentRoundRow round)
    {
        if (!round.Matches.All(match => match.Status == "completed")) return;
        round.Status = "completed";
        round.Paused = false;
        round.PausedAt = null;
        if (round.Stage == "swiss")
        {
            round.Standings = ComputeStandings(tournament, round.Number);
            round.StandingsCapturedAt ??= DateTimeOffset.UtcNow;
            if (tournament.Format is "swiss" or "swiss-cut" && round.Number >= tournament.SwissRounds)
                tournament.FinalSwissStandings = round.Standings.Select(CloneStanding).ToList();
            return;
        }
        foreach (var match in round.Matches)
        {
            var winner = WinnerAccountId(match);
            foreach (var accountId in new[] { match.PlayerAAccountId, match.PlayerBAccountId }
                         .Where(id => id is not null && id != winner))
            {
                var participant = tournament.Participants.FirstOrDefault(item => item.AccountId == accountId);
                if (participant is not null) participant.Eliminated = true;
            }
        }
    }

    private static TournamentStandingRow CloneStanding(TournamentStandingRow row)
        => new()
        {
            RoundNumber = row.RoundNumber, Rank = row.Rank, AccountId = row.AccountId, Wins = row.Wins,
            Losses = row.Losses, Draws = row.Draws, Byes = row.Byes, OpponentScore = row.OpponentScore,
            OpponentsOpponentScore = row.OpponentsOpponentScore, Seed = row.Seed,
        };

    private static string? WinnerAccountId(TournamentMatchRow match)
        => match.Result switch
        {
            "bye" or "player-a" or "no-show-b" => match.PlayerAAccountId,
            "player-b" or "no-show-a" => match.PlayerBAccountId,
            _ => null,
        };

    private static string TournamentPairKey(string first, string second)
        => string.CompareOrdinal(first, second) < 0 ? $"{first}|{second}" : $"{second}|{first}";

    private L12TournamentView ToView(TournamentRow row, L12AccountView viewer)
    {
        var organizer = _data.Accounts.FirstOrDefault(item => item.Id == row.OrganizerAccountId);
        var staff = row.RefereeAccountIds.Select(AccountById).Where(item => item is not null)
            .Select(item => new L12TournamentStaffView(item!.Id, item.Username)).ToArray();
        var configuredStaff = IsConfiguredStaff(row, viewer.Id);
        var canViewAllDecks = configuredStaff || row.Rules.DeckVisibility == "always"
            || row.Rules.DeckVisibility == "after" && row.Status == "completed";
        var visibleParticipantRows = row.Status == "registration" && row.RegistrationVisibility == "staff"
            && !configuredStaff
            ? row.Participants.Where(item => item.AccountId == viewer.Id)
            : row.Participants.AsEnumerable();
        var participants = visibleParticipantRows.Select(item =>
        {
            var account = AccountById(item.AccountId);
            var canViewDeck = canViewAllDecks || item.AccountId == viewer.Id;
            L12TournamentDeckSnapshotView? deck = null;
            if (canViewDeck && !string.IsNullOrWhiteSpace(item.Deck.Hash))
                deck = new L12TournamentDeckSnapshotView(item.Deck.Name, item.Deck.Code, item.Deck.Hash,
                    item.Deck.SubmittedAt, item.Deck.LockedAt, item.Deck.MasterId, item.Deck.CardIds.ToArray(),
                    item.Deck.MoraleIds.ToArray(), item.Deck.SpecialIds.ToArray());
            return new L12TournamentParticipantView(item.AccountId, account?.Username ?? "已删除账号",
                item.CheckedIn, item.Dropped, item.Eliminated, item.Seed, deck);
        }).ToArray();
        var bracket = row.Rounds.Where(round => round.Stage == "elimination")
            .Select((round, index) => new L12TournamentBracketRoundView(index + 1,
                round.Matches.OrderBy(match => match.Table).Select(match =>
                {
                    var a = AccountById(match.PlayerAAccountId);
                    var b = match.PlayerBAccountId is null ? null : AccountById(match.PlayerBAccountId);
                    return new L12TournamentBracketMatchView(match.Id, match.Table, match.PlayerAAccountId,
                        a?.Username ?? "已删除账号", match.PlayerBAccountId,
                        b?.Username ?? "轮空", match.Result, match.SourceMatchIds.ToArray());
                }).ToArray())).ToArray();
        return new L12TournamentView(row.Id, row.Code, row.Name, row.OrganizerAccountId,
            organizer?.Username ?? "已删除账号", staff, row.Status, row.Format, row.Visibility, row.MaxPlayers,
            row.StartAt, row.Description, new L12TournamentRulesSnapshotView(row.Rules.Ruleset,
                row.Rules.DisasterMode, row.Rules.BanList, row.Rules.DisasterCardIds.ToArray(),
                row.Rules.CardRestrictions.ToArray(), row.Rules.DeckVisibility, row.Rules.Hash,
                row.Rules.CapturedAt), row.RoundMinutes, row.CheckInMinutes, participants,
            row.Rounds.OrderBy(round => round.Number).Select(round => ToView(round, row, viewer, configuredStaff))
                .ToArray(), row.Version, row.LegacySourceId is not null, row.CreatedAt, row.UpdatedAt, row.CompletedAt,
            row.SwissRounds, row.CutSize, row.RegistrationVisibility, row.LateGraceMinutes,
            row.FinalSwissStandings.Select(ToStandingView).ToArray(), bracket);
    }

    private L12TournamentRoundView ToView(TournamentRoundRow row, TournamentRow tournament,
        L12AccountView viewer, bool configuredStaff)
        => new(row.Id, row.Number, row.Status, row.Paused, row.StartedAt, row.PausedAt, row.TotalPausedSeconds,
            row.Matches.Where(match => configuredStaff || match.PlayerAAccountId == viewer.Id
                    || match.PlayerBAccountId == viewer.Id)
                .OrderBy(match => match.Table).Select(match =>
            {
                var a = AccountById(match.PlayerAAccountId);
                var b = match.PlayerBAccountId is null ? null : AccountById(match.PlayerBAccountId);
                return new L12TournamentMatchView(match.Id, match.Table, match.PlayerAAccountId,
                    a?.Username ?? "已删除账号", match.PlayerBAccountId, b?.Username ?? "轮空", match.RoomCode,
                    match.ReadyA, match.ReadyB, match.Status, match.Result, match.TimeExtensionMinutes,
                    match.StartedAt, match.Deadline, match.RecordedMatchId,
                    match.Rulings.Select(ruling => new L12TournamentRulingView(ruling.Id, ruling.MatchId,
                        ruling.Kind, ruling.TargetAccountId, ruling.Decision, ruling.Minutes, ruling.Reason,
                        ruling.ActorId, ruling.ActorName, ruling.CreatedAt)).ToArray(), match.GraceDeadline,
                    match.SourceMatchIds.ToArray(), match.RulesHash, match.PlayerADeckHash, match.PlayerBDeckHash,
                    match.ReplayNumber, match.Status == "running" && (match.PlayerAAccountId == viewer.Id
                        || match.PlayerBAccountId == viewer.Id), match.Status == "running" && configuredStaff,
                    match.Events.Select(item => new L12TournamentMatchEventView(item.Id, item.Kind, item.Result,
                        item.RecordedMatchId, item.ActorId, item.Detail, item.CreatedAt)).ToArray());
            }).ToArray(), row.Stage, row.StandingsCapturedAt,
            row.Standings.Select(ToStandingView).ToArray(), row.PairingFailure);

    private L12TournamentStandingView ToStandingView(TournamentStandingRow row)
        => new(row.RoundNumber, row.Rank, row.AccountId, AccountById(row.AccountId)?.Username ?? "已删除账号",
            row.Wins, row.Losses, row.Draws, row.Byes, row.OpponentScore,
            row.OpponentsOpponentScore, row.Seed);

    private TournamentRow RequireTournament(string id, long expectedVersion)
    {
        var row = _data.Tournaments.FirstOrDefault(item => item.Id == id)
            ?? throw new KeyNotFoundException("赛事不存在");
        if (row.Version != expectedVersion)
            throw new L12TournamentVersionConflictException("赛事版本已变化，请刷新后重试");
        return row;
    }

    private static TournamentRoundRow RequireRound(TournamentRow tournament, int number)
        => tournament.Rounds.FirstOrDefault(item => item.Number == number)
           ?? throw new KeyNotFoundException("赛事轮次不存在");

    private static (TournamentRoundRow Round, TournamentMatchRow Match) RequireMatch(TournamentRow tournament,
        string matchId)
    {
        foreach (var round in tournament.Rounds)
        {
            var match = round.Matches.FirstOrDefault(item => item.Id == matchId);
            if (match is not null) return (round, match);
        }
        throw new KeyNotFoundException("赛事桌次不存在");
    }

    private void RequireOrganizer(L12AccountView actor, TournamentRow tournament)
    {
        RequireActiveTournament(tournament);
        RequireActiveTournamentAccount(actor);
        if (tournament.OrganizerAccountId != actor.Id)
            throw new L12TournamentScopeException("仅赛事主办者可执行该操作");
    }

    private void RequireStaff(L12AccountView actor, TournamentRow tournament, L12Permission permission)
    {
        RequireActiveTournament(tournament);
        RequireActiveTournamentAccount(actor);
        if (!IsStaff(tournament, actor.Id))
            throw new L12TournamentScopeException("账号不在该赛事工作人员作用域内");
    }

    private void RequireActiveTournamentAccount(L12AccountView actor)
    {
        var account = AccountById(actor.Id);
        if (account is null || account.Disabled)
            throw new L12TournamentScopeException("赛事工作人员账号不存在或已禁用");
    }

    private static void RequireActiveTournament(TournamentRow tournament)
    {
        if (tournament.Status == "completed")
            throw new L12TournamentScopeException("赛事已结束，主办者与裁判临时权限已经失效");
    }

    private static bool IsStaff(TournamentRow row, string accountId)
        => row.Status != "completed"
           && IsConfiguredStaff(row, accountId);

    private static bool IsConfiguredStaff(TournamentRow row, string accountId)
        => row.OrganizerAccountId == accountId
           || row.RefereeAccountIds.Contains(accountId, StringComparer.Ordinal);

    private static void EnsurePermission(L12AccountView actor, L12Permission permission)
    {
        if (!L12Authorization.HasPermission(actor, permission))
            throw new L12TournamentScopeException("账号缺少赛事权限");
    }

    private IReadOnlyList<string> NormalizeReferees(string organizerAccountId, IEnumerable<string> ids)
    {
        var result = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal).ToArray();
        if (result.Length > 32) throw new ArgumentException("裁判人数不能超过32人");
        foreach (var id in result)
        {
            var account = AccountById(id) ?? throw new ArgumentException($"裁判账号不存在：{id}");
            if (account.Disabled) throw new ArgumentException($"裁判账号已禁用：{account.Username}");
            if (id == organizerAccountId)
                throw new ArgumentException("赛事主办者无需重复设置为裁判");
            if (FindFriendRow(organizerAccountId, id)?.Status != "accepted")
                throw new ArgumentException($"裁判必须是赛事主办者的好友：{account.Username}");
        }
        return result;
    }

    private IReadOnlyList<string> ResolveLegacyReferees(IReadOnlyList<string>? usernames)
        => (usernames ?? []).Select(AccountIdByUsername).Where(id => id is not null)
            .Select(id => id!).Distinct(StringComparer.Ordinal).ToArray();

    private AccountRow? AccountById(string id) => _data.Accounts.FirstOrDefault(item => item.Id == id);
    private string? AccountIdByUsername(string? username) => string.IsNullOrWhiteSpace(username) ? null
        : _data.Accounts.FirstOrDefault(item => string.Equals(item.Username, username.Trim(),
            StringComparison.OrdinalIgnoreCase))?.Id;

    private TournamentRulesSnapshotRow RulesSnapshot(string ruleset, string disasterMode, string banList,
        string deckVisibility, IReadOnlyList<string> disasterCardIds,
        IReadOnlyList<L12CardRestrictionConfig> cardRestrictions)
    {
        var normalizedRuleset = RequireText(ruleset, "规则版本", 200);
        var normalizedDisaster = Allowed(disasterMode, "天灾模式", "all", "random", "season", "none");
        var normalizedVisibility = Allowed(deckVisibility, "牌库可见性", "always", "after", "private");
        var normalizedBanList = OptionalText(banList, 2000);
        var normalizedDisasters = normalizedDisaster == "none" ? [] : disasterCardIds
            .Select(id => RequireCardId(id, "赛事天灾卡号")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalizedDisaster != "none" && (normalizedDisasters.Length is < 9 or > 64
            || !string.Equals(normalizedDisasters[^1], AnnihilationCardId, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("赛事天灾池须为 9–64 张（含堙灭），且堙灭固定在最后一张");
        if (_officialCards.Count > 0 && normalizedDisasters.Any(id => !_officialCards.TryGetValue(id, out var card)
                || card.CardType != "destruction"))
            throw new ArgumentException("赛事天灾池包含非天灾卡牌");
        var normalizedRestrictions = cardRestrictions.Select(item => new L12CardRestrictionConfig(
            RequireCardId(item.CardId, "赛事构筑规则卡号"),
            item.MaxCopies is >= 0 and <= 3 ? item.MaxCopies : throw new ArgumentException("赛事构筑上限须为 0–3"),
            string.IsNullOrWhiteSpace(item.Reason) ? null : OptionalText(item.Reason, 500),
            string.IsNullOrWhiteSpace(item.MasterId) ? null : RequireCardId(item.MasterId, "赛事构筑规则主宰")))
            .ToArray();
        if (normalizedRestrictions.GroupBy(item => $"{item.MasterId ?? "*"}|{item.CardId}", StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1)) throw new ArgumentException("赛事构筑规则包含重复的主宰/卡牌组合");
        if (_officialCards.Count > 0)
        {
            var unknownCard = normalizedRestrictions.FirstOrDefault(item => !_officialCards.ContainsKey(item.CardId));
            if (unknownCard is not null) throw new ArgumentException($"赛事构筑规则卡牌不存在：{unknownCard.CardId}");
            var unknownMaster = normalizedRestrictions.FirstOrDefault(item => item.MasterId is not null
                && (!_officialCards.TryGetValue(item.MasterId, out var master) || master.CardType != "master"));
            if (unknownMaster is not null) throw new ArgumentException($"赛事构筑规则主宰不存在：{unknownMaster.MasterId}");
        }
        var captured = DateTimeOffset.UtcNow;
        var structured = JsonSerializer.Serialize(new { normalizedDisasters, normalizedRestrictions });
        var canonical = $"{normalizedRuleset}\n{normalizedDisaster}\n{normalizedBanList}\n{normalizedVisibility}\n{structured}";
        return new TournamentRulesSnapshotRow
        {
            Ruleset = normalizedRuleset,
            DisasterMode = normalizedDisaster,
            BanList = normalizedBanList,
            DisasterCardIds = [.. normalizedDisasters],
            CardRestrictions = [.. normalizedRestrictions],
            DeckVisibility = normalizedVisibility,
            Hash = Hash(canonical),
            CapturedAt = captured,
        };
    }

    private TournamentDeckSnapshotRow ResolveTournamentDeckSnapshot(string accountId,
        TournamentRulesSnapshotRow rules, L12TournamentRegistrationPayload payload)
    {
        var requestedName = OptionalText(payload.DeckName, 200);
        var saved = _data.Decks.FirstOrDefault(item => item.AccountId == accountId
            && string.Equals(item.Name, requestedName, StringComparison.OrdinalIgnoreCase));
        if (saved is null)
        {
            // 兼容旧客户端和已导入数据；新流程始终从账号牌库选择。
            if (string.IsNullOrWhiteSpace(payload.DeckCode))
                throw new ArgumentException("所选牌库不在当前登录账号的服务端牌库中");
            ValidateTournamentDeckCode(rules, payload.DeckCode);
            return DeckSnapshot(requestedName, payload.DeckCode);
        }
        ValidateStructuredTournamentDeck(rules, saved.MasterId, saved.CardIds, saved.MoraleIds, saved.SpecialIds);
        var canonical = JsonSerializer.Serialize(new
        {
            saved.Name, saved.MasterId, saved.CardIds, saved.MoraleIds, saved.SpecialIds,
        });
        return new TournamentDeckSnapshotRow
        {
            Name = saved.Name,
            Code = OptionalText(payload.DeckCode, 4096),
            MasterId = saved.MasterId,
            CardIds = [.. saved.CardIds],
            MoraleIds = [.. saved.MoraleIds],
            SpecialIds = [.. saved.SpecialIds],
            Hash = Hash(canonical),
            SubmittedAt = DateTimeOffset.UtcNow,
        };
    }

    private static void ValidateStructuredTournamentDeck(TournamentRulesSnapshotRow rules, string masterId,
        IEnumerable<string> cardIds, IEnumerable<string> moraleIds, IEnumerable<string> specialIds)
    {
        var all = new[] { masterId }.Concat(cardIds).Concat(moraleIds).Concat(specialIds);
        foreach (var group in all.GroupBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var rule = rules.CardRestrictions.FirstOrDefault(item => string.Equals(item.CardId, group.Key,
                           StringComparison.OrdinalIgnoreCase) && string.Equals(item.MasterId, masterId,
                           StringComparison.OrdinalIgnoreCase))
                       ?? rules.CardRestrictions.FirstOrDefault(item => string.Equals(item.CardId, group.Key,
                           StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(item.MasterId));
            if (rule is null || group.Count() <= rule.MaxCopies) continue;
            throw new ArgumentException(rule.MaxCopies == 0
                ? $"赛事规则禁止使用 {group.Key}"
                : $"赛事规则限制 {group.Key} 最多投入 {rule.MaxCopies} 张");
        }
    }

    private static TournamentDeckSnapshotRow DeckSnapshot(string? name, string? code)
    {
        var normalizedName = OptionalText(name, 200);
        var normalizedCode = OptionalText(code, 4096);
        if (string.IsNullOrWhiteSpace(normalizedName) && string.IsNullOrWhiteSpace(normalizedCode)) return new();
        var snapshot = new TournamentDeckSnapshotRow
        {
            Name = normalizedName,
            Code = normalizedCode,
            Hash = Hash($"{normalizedName}\n{normalizedCode}"),
            SubmittedAt = DateTimeOffset.UtcNow,
        };
        if (TryDecodeTournamentDeckCode(normalizedCode, out var masterId, out var cards,
                out var morale, out var special))
        {
            snapshot.MasterId = masterId;
            snapshot.CardIds = cards;
            snapshot.MoraleIds = morale;
            snapshot.SpecialIds = special;
        }
        return snapshot;
    }

    private static bool TryDecodeTournamentDeckCode(string? code, out string masterId,
        out List<string> cards, out List<string> morale, out List<string> special)
    {
        masterId = string.Empty;
        cards = [];
        morale = [];
        special = [];
        var value = code?.Trim() ?? string.Empty;
        if (!value.StartsWith("L12D1.", StringComparison.Ordinal)) return false;
        try
        {
            var encoded = value[6..].Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
            var root = document.RootElement;
            masterId = root.GetProperty("m").GetString() ?? string.Empty;
            cards = ReadDeckCodeArray(root, "c");
            morale = ReadDeckCodeArray(root, "r");
            special = ReadDeckCodeArray(root, "s");
            return !string.IsNullOrWhiteSpace(masterId);
        }
        catch { return false; }
    }

    private static List<string> ReadDeckCodeArray(JsonElement root, string property)
        => root.TryGetProperty(property, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Select(item => item.GetString()).OfType<string>().ToList() : [];

    private static void ValidateTournamentDeckCode(TournamentRulesSnapshotRow rules, string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        var value = code.Trim();
        // 旧赛事快照可能保存不可解析的历史码；新 L12D1 牌库码执行结构化规则校验。
        if (!value.StartsWith("L12D1.", StringComparison.Ordinal)) return;
        try
        {
            var encoded = value[6..].Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
            var root = document.RootElement;
            var masterId = root.GetProperty("m").GetString() ?? string.Empty;
            var cardIds = new List<string> { masterId };
            foreach (var key in new[] { "c", "r", "s" })
                if (root.TryGetProperty(key, out var items) && items.ValueKind == JsonValueKind.Array)
                    cardIds.AddRange(items.EnumerateArray().Select(item => item.GetString()).OfType<string>());
            foreach (var group in cardIds.GroupBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                var rule = rules.CardRestrictions.FirstOrDefault(item => string.Equals(item.CardId, group.Key,
                               StringComparison.OrdinalIgnoreCase) && string.Equals(item.MasterId, masterId,
                               StringComparison.OrdinalIgnoreCase))
                           ?? rules.CardRestrictions.FirstOrDefault(item => string.Equals(item.CardId, group.Key,
                               StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(item.MasterId));
                if (rule is null || group.Count() <= rule.MaxCopies) continue;
                throw new ArgumentException(rule.MaxCopies == 0
                    ? $"赛事规则禁止使用 {group.Key}"
                    : $"赛事规则限制 {group.Key} 最多投入 {rule.MaxCopies} 张");
            }
        }
        catch (ArgumentException) { throw; }
        catch (Exception) { throw new ArgumentException("赛事牌库码无法解析"); }
    }

    private static TournamentRulingRow NewRuling(L12AccountView actor, string matchId, string kind,
        string? targetAccountId, string decision, int minutes, string reason)
        => new()
        {
            MatchId = matchId,
            Kind = kind,
            TargetAccountId = targetAccountId,
            Decision = decision,
            Minutes = minutes,
            Reason = reason,
            ActorId = actor.Id,
            ActorName = actor.Username,
        };

    private static TournamentMatchEventRow NewMatchEvent(string kind, string? result,
        string? recordedMatchId, string actorId, string detail)
        => new()
        {
            Kind = kind,
            Result = result,
            RecordedMatchId = recordedMatchId,
            ActorId = actorId,
            Detail = detail,
        };

    private static void ValidateRuling(TournamentRoundRow round, TournamentMatchRow match, string kind,
        string? targetAccountId, string decision)
    {
        var normalized = decision.Trim().ToLowerInvariant();
        if (kind == "penalty")
        {
            if (targetAccountId != match.PlayerAAccountId && targetAccountId != match.PlayerBAccountId)
                throw new ArgumentException("处罚目标不在该桌次");
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 100)
                throw new ArgumentException("处罚决定无效");
            return;
        }
        if (match.Status == "completed") throw new L12TournamentVersionConflictException("对局已经完成");
        if (normalized is not ("player-a" or "player-b" or "draw" or "no-show-a" or "no-show-b"))
            throw new ArgumentException("赛果决定无效");
        if (round.Stage == "elimination" && normalized == "draw")
            throw new ArgumentException("淘汰赛不允许平局赛果");
        if (kind == "no-show")
        {
            if (match.GraceDeadline is null || match.GraceDeadline > DateTimeOffset.UtcNow)
                throw new L12TournamentVersionConflictException("迟到宽限期尚未结束，不能判负");
            if (normalized == "no-show-a" && match.ReadyA || normalized == "no-show-b" && match.ReadyB)
                throw new L12TournamentVersionConflictException("已签到玩家不能按未到场判负");
        }
        if (match.PlayerBAccountId is null && normalized != "player-a")
            throw new ArgumentException("轮空桌次只能判定 A 方获胜");
    }

    private static TournamentRow CloneTournament(TournamentRow row)
        => JsonSerializer.Deserialize<TournamentRow>(JsonSerializer.Serialize(row))!;

    private static void NormalizeTournament(TournamentRow row)
    {
        row.RefereeAccountIds ??= [];
        row.Participants ??= [];
        row.Rounds ??= [];
        row.FinalSwissStandings ??= [];
        row.RegistrationVisibility = row.RegistrationVisibility is "public" or "staff"
            ? row.RegistrationVisibility : "public";
        row.LateGraceMinutes = Math.Clamp(row.LateGraceMinutes, 0, 60);
        if (row.Format != "single" && row.SwissRounds < 1) row.SwissRounds = 1;
        if (row.Format == "single") row.SwissRounds = 0;
        row.Rules ??= new TournamentRulesSnapshotRow();
        row.Rules.DisasterCardIds ??= [];
        row.Rules.CardRestrictions ??= [];
        var nextSeed = 1;
        foreach (var participant in row.Participants)
        {
            participant.Deck ??= new TournamentDeckSnapshotRow();
            participant.Deck.CardIds ??= [];
            participant.Deck.MoraleIds ??= [];
            participant.Deck.SpecialIds ??= [];
            if (participant.Seed < 1) participant.Seed = nextSeed;
            nextSeed = Math.Max(nextSeed + 1, participant.Seed + 1);
        }
        foreach (var round in row.Rounds)
        {
            round.Stage = row.Format == "single" ? "elimination"
                : round.Stage is "swiss" or "elimination" ? round.Stage : "swiss";
            round.Standings ??= [];
            round.Matches ??= [];
            foreach (var match in round.Matches)
            {
                match.Rulings ??= [];
                match.Events ??= [];
                match.SourceMatchIds ??= [];
                match.RulesHash = string.IsNullOrWhiteSpace(match.RulesHash) ? row.Rules.Hash : match.RulesHash;
                match.PlayerADeckHash ??= row.Participants.FirstOrDefault(item =>
                    item.AccountId == match.PlayerAAccountId)?.Deck.Hash;
                match.PlayerBDeckHash ??= row.Participants.FirstOrDefault(item =>
                    item.AccountId == match.PlayerBAccountId)?.Deck.Hash;
            }
        }
        foreach (var round in row.Rounds.Where(item => item.Stage == "swiss" && item.Status == "completed"
                     && item.Standings.Count == 0).OrderBy(item => item.Number))
        {
            round.Standings = ComputeStandings(row, round.Number);
            round.StandingsCapturedAt ??= row.UpdatedAt;
        }
        if (row.Version < 1) row.Version = 1;
    }

    private string UniqueTournamentCode()
    {
        string code;
        do code = RandomCode(6); while (_data.Tournaments.Any(item => item.Code == code));
        return code;
    }

    private static string RandomCode(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        return new string(bytes.ToArray().Select(value => alphabet[value % alphabet.Length]).ToArray());
    }

    private static string? TournamentIdFromScope(string scope)
    {
        const string prefix = "tournament:";
        if (!scope.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var tail = scope[prefix.Length..];
        var slash = tail.IndexOf('/');
        return slash < 0 ? tail : tail[..slash];
    }

    private static string RequireText(string? value, string label, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 || normalized.Length > maxLength)
            throw new ArgumentException($"{label}长度必须为 1–{maxLength}");
        return normalized;
    }

    private static string OptionalText(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxLength) throw new ArgumentException($"字段长度不能超过 {maxLength}");
        return normalized;
    }

    private static string Allowed(string? value, string label, params string[] allowed)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!allowed.Contains(normalized, StringComparer.Ordinal))
            throw new ArgumentException($"{label}无效");
        return normalized;
    }

    private static string AllowedOrDefault(string? value, string fallback, params string[] allowed)
        => string.IsNullOrWhiteSpace(value) ? fallback : allowed.Contains(value.Trim().ToLowerInvariant(),
            StringComparer.Ordinal) ? value.Trim().ToLowerInvariant() : fallback;

    private static bool ValidLegacyCode(string? code)
        => !string.IsNullOrWhiteSpace(code) && code.Length is >= 4 and <= 12
           && code.All(character => char.IsAsciiLetterOrDigit(character));

    private static bool ValidRoomCode(string? code)
        => !string.IsNullOrWhiteSpace(code) && code.Length is >= 4 and <= 16
           && code.All(character => char.IsAsciiLetterOrDigit(character));

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string LegacyPreviewHash(IReadOnlyList<L12LegacyTournamentInput> tournaments)
        => Hash(JsonSerializer.Serialize(tournaments));
}
