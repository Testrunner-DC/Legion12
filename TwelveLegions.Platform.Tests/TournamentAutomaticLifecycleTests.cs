using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class TournamentAutomaticLifecycleTests
{
    [Theory]
    [InlineData("automated")]
    [InlineData(" automated ")]
    [InlineData("\tAuToMaTeD\n")]
    public void AutomatedCreationRequiresAdministratorAndLegacyRowsRemainManaged(string operationMode)
    {
        var fixture = CreateFixture("permission", 1);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var automatic = Payload(now.AddMinutes(10), "single") with { OperationMode = operationMode };
            var versionBeforeUnauthorizedCreate = fixture.Store.Version;
            Assert.Throws<L12TournamentScopeException>(() => fixture.Store.CreateTournament(
                fixture.Players[0], automatic, Context("player-forged-auto"), true));
            Assert.Equal(versionBeforeUnauthorizedCreate, fixture.Store.Version);

            var created = fixture.Store.CreateTournament(fixture.Admin, automatic,
                Context("admin-auto"), true);
            Assert.Equal("automated", created.OperationMode);
            Assert.Equal(3, created.IntermissionMinutes);
            Assert.Equal(5, created.LateGraceMinutes);
            Assert.Equal("scheduled", created.AutomationStatus);
            Assert.Equal(now.AddMinutes(5).UtcDateTime, created.NextAutomaticActionAt!.Value.UtcDateTime,
                TimeSpan.FromSeconds(1));

            var managed = fixture.Store.CreateTournament(fixture.Players[0],
                Payload(now.AddMinutes(20), "swiss") with { OperationMode = "managed" },
                Context("ordinary-managed"), true);
            Assert.Equal("managed", managed.OperationMode);
            Assert.Equal("disabled", managed.AutomationStatus);

            var restarted = fixture.Restart();
            var restoredAuto = restarted.Tournament(fixture.Admin, created.Id)!;
            var restoredManaged = restarted.Tournament(fixture.Players[0], managed.Id)!;
            Assert.Equal("automated", restoredAuto.OperationMode);
            Assert.Equal(3, restoredAuto.IntermissionMinutes);
            Assert.Equal("managed", restoredManaged.OperationMode);
            Assert.Equal("disabled", restoredManaged.AutomationStatus);
        }
        finally { fixture.Dispose(); }
    }

    [Fact]
    public void AutomaticSingleEliminationUsesExistingResultAndArchivesIdempotently()
    {
        var fixture = CreateFixture("single", 1);
        try
        {
            var start = DateTimeOffset.UtcNow.AddMinutes(10);
            var tournament = RegisterAndLock(fixture, Payload(start, "single"));

            Assert.Equal(1, fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(-5)).Changed);
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            Assert.Equal("pre-check-in", tournament.Phase);

            Assert.Equal(1, fixture.Store.AdvanceAutomaticTournaments(start).Changed);
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            Assert.Equal("preparing-round", tournament.AutomationStatus);
            Assert.Equal(start.AddMinutes(3).UtcDateTime, tournament.NextAutomaticActionAt!.Value.UtcDateTime,
                TimeSpan.FromSeconds(1));
            tournament = ReadyAll(fixture, tournament);

            Assert.Equal(1, fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(3)).Changed);
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            var match = Assert.Single(Assert.Single(tournament.Rounds).Matches);
            Assert.Equal("running", match.Status);

            tournament = fixture.Store.RecordTournamentGameResult(tournament.Id, match.Id,
                "automatic-single-result", 0);
            Assert.Equal("completed", Assert.Single(tournament.Rounds).Status);
            Assert.Equal(1, fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(4)).Changed);
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            Assert.Equal("completed", tournament.Status);
            Assert.Equal("completed", tournament.AutomationStatus);
            Assert.Equal(0, fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(5)).Changed);
        }
        finally { fixture.Dispose(); }
    }

    [Fact]
    public void MissingPlayerLosesAfterIndependentGraceAndLeavesLaterRounds()
    {
        var fixture = CreateFixture("noshow", 1);
        try
        {
            var start = DateTimeOffset.UtcNow.AddMinutes(10);
            var tournament = RegisterAndLock(fixture, Payload(start, "single"));
            fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(-5));
            fixture.Store.AdvanceAutomaticTournaments(start);
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            var round = Assert.Single(tournament.Rounds);
            var match = Assert.Single(round.Matches);
            var ready = match.PlayerAAccountId == fixture.Admin.Id ? fixture.Admin : fixture.Players[0];
            var absentId = match.PlayerAAccountId == ready.Id ? match.PlayerBAccountId! : match.PlayerAAccountId;
            tournament = fixture.Store.CheckInTournament(ready, tournament.Id, round.Number,
                new L12TournamentCheckInPayload(null, true), tournament.Version,
                Context("only-one-ready"), true);

            fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(3));
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            match = Assert.Single(Assert.Single(tournament.Rounds).Matches);
            Assert.Equal(start.AddMinutes(8).UtcDateTime, match.GraceDeadline!.Value.UtcDateTime,
                TimeSpan.FromSeconds(1));

            Assert.Equal(1, fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(8)).Changed);
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            match = Assert.Single(Assert.Single(tournament.Rounds).Matches);
            Assert.Equal(ready.Id == match.PlayerAAccountId ? "no-show-b" : "no-show-a", match.Result);
            Assert.True(tournament.Participants.Single(item => item.AccountId == absentId).Dropped);
            Assert.Equal(1, fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(9)).Changed);
            Assert.Equal("completed", fixture.Store.Tournament(fixture.Admin, tournament.Id)!.Status);
        }
        finally { fixture.Dispose(); }
    }

    [Theory]
    [InlineData("single", "attention-required")]
    [InlineData("swiss", "round-running")]
    public void DoubleNoShowNeverInventsAnEliminationWinner(string format, string expectedStatus)
    {
        var fixture = CreateFixture("double" + format, 1);
        try
        {
            var start = DateTimeOffset.UtcNow.AddMinutes(10);
            var tournament = RegisterAndLock(fixture, Payload(start, format));
            fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(-5));
            fixture.Store.AdvanceAutomaticTournaments(start);
            fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(3));

            Assert.Equal(1, fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(8)).Changed);
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            var match = Assert.Single(Assert.Single(tournament.Rounds).Matches);
            Assert.Equal(expectedStatus, tournament.AutomationStatus);
            Assert.True(tournament.Participants.Where(item => !item.Waitlisted).All(item => item.Dropped));
            if (format == "single")
            {
                Assert.Null(match.Result);
                Assert.Contains("无法自动决定晋级者", tournament.AutomationAttentionReason);
                Assert.Equal(0, fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(20)).Changed);
                tournament = fixture.Store.ApplyTournamentRuling(fixture.Judge, tournament.Id, match.Id,
                    new L12TournamentRulingPayload("result", null, "player-a", "管理员明确决定晋级者"),
                    tournament.Version, Context("resolve-elimination-double-no-show"), true);
                Assert.Equal("round-running", tournament.AutomationStatus);
                Assert.Equal(1, fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(21)).Changed);
                Assert.Equal("completed", fixture.Store.Tournament(fixture.Admin, tournament.Id)!.Status);
            }
            else
            {
                Assert.Equal("double-no-show", match.Result);
                Assert.Equal("completed", match.Status);
            }
        }
        finally { fixture.Dispose(); }
    }

    [Fact]
    public void RestartKeepsTheScheduledRoundAndDuplicateTicksDoNotAdvanceTwice()
    {
        var fixture = CreateFixture("restart", 1);
        try
        {
            var start = DateTimeOffset.UtcNow.AddMinutes(10);
            var tournament = RegisterAndLock(fixture, Payload(start, "single"));
            fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(-5));
            fixture.Store.AdvanceAutomaticTournaments(start);
            var restarted = fixture.Restart();
            tournament = restarted.Tournament(fixture.Admin, tournament.Id)!;
            tournament = ReadyAll(fixture with { Store = restarted }, tournament);

            Assert.Equal(0, restarted.AdvanceAutomaticTournaments(start.AddMinutes(2)).Changed);
            Assert.Equal(1, restarted.AdvanceAutomaticTournaments(start.AddMinutes(3)).Changed);
            Assert.Equal(0, restarted.AdvanceAutomaticTournaments(start.AddMinutes(3)).Changed);
            tournament = restarted.Tournament(fixture.Admin, tournament.Id)!;
            Assert.Single(tournament.Rounds);
            Assert.Equal("running", Assert.Single(tournament.Rounds).Status);
        }
        finally { fixture.Dispose(); }
    }

    [Fact]
    public void FourPlayerEliminationRulingRestoresOnlyTheChosenAutomaticDropAcrossRestart()
    {
        var fixture = CreateFixture("bracket", 3);
        try
        {
            var start = DateTimeOffset.UtcNow.AddMinutes(10);
            var tournament = RegisterAndLock(fixture, Payload(start, "single"));
            fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(-5));
            fixture.Store.AdvanceAutomaticTournaments(start);
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            var firstRound = Assert.Single(tournament.Rounds);
            Assert.Equal(2, firstRound.Matches.Count);
            var normal = firstRound.Matches.OrderBy(item => item.Table).First();
            var unresolved = firstRound.Matches.OrderBy(item => item.Table).Last();
            tournament = ReadyMatch(fixture, tournament, normal, true, true);

            fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(3));
            tournament = fixture.Store.RecordTournamentGameResult(tournament.Id, normal.Id,
                "normal-other-branch", 0);
            Assert.Equal(1, fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(8)).Changed);
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            Assert.Equal("attention-required", tournament.AutomationStatus);
            Assert.Null(tournament.Rounds[0].Matches.Single(item => item.Id == unresolved.Id).Result);

            var restarted = fixture.Restart();
            tournament = restarted.Tournament(fixture.Admin, tournament.Id)!;
            tournament = restarted.ApplyTournamentRuling(fixture.Judge, tournament.Id, unresolved.Id,
                new L12TournamentRulingPayload("result", null, "player-a", "独立裁判明确本支晋级者"),
                tournament.Version, Context("resolve-four-player-double-no-show"), true);
            var selectedWinner = unresolved.PlayerAAccountId;
            Assert.False(tournament.Participants.Single(item => item.AccountId == selectedWinner).Dropped);
            Assert.True(tournament.Participants.Single(item => item.AccountId == unresolved.PlayerBAccountId).Dropped);

            Assert.Equal(1, restarted.AdvanceAutomaticTournaments(start.AddMinutes(9)).Changed);
            tournament = restarted.Tournament(fixture.Admin, tournament.Id)!;
            Assert.Equal(2, tournament.Rounds.Count);
            var final = Assert.Single(tournament.Rounds[1].Matches);
            Assert.Contains(selectedWinner, new[] { final.PlayerAAccountId, final.PlayerBAccountId });
            Assert.Contains(normal.PlayerAAccountId, new[] { final.PlayerAAccountId, final.PlayerBAccountId });
            tournament = ReadyAll(fixture with { Store = restarted }, tournament);
            Assert.Equal(1, restarted.AdvanceAutomaticTournaments(start.AddMinutes(12)).Changed);
            tournament = restarted.Tournament(fixture.Admin, tournament.Id)!;
            final = Assert.Single(tournament.Rounds[1].Matches);
            Assert.Equal("running", final.Status);
            tournament = restarted.RecordTournamentGameResult(tournament.Id, final.Id,
                "four-player-final", 0);
            Assert.Equal(1, restarted.AdvanceAutomaticTournaments(start.AddMinutes(13)).Changed);
            Assert.Equal("completed", restarted.Tournament(fixture.Admin, tournament.Id)!.Status);
        }
        finally { fixture.Dispose(); }
    }

    [Fact]
    public void PausedAutomaticRoundFreezesAllGraceDeadlinesAcrossRestart()
    {
        var fixture = CreateFixture("pause", 5);
        try
        {
            var start = DateTimeOffset.UtcNow.AddMinutes(10);
            var tournament = RegisterAndLock(fixture, Payload(start, "swiss"));
            fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(-5));
            fixture.Store.AdvanceAutomaticTournaments(start);
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            var matches = Assert.Single(tournament.Rounds).Matches.OrderBy(item => item.Table).ToArray();
            Assert.Equal(3, matches.Length);
            tournament = ReadyMatch(fixture, tournament, matches[0], true, true);
            tournament = ReadyMatch(fixture, tournament, matches[1], true, false);

            fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(3));
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            var runningBefore = tournament.Rounds[0].Matches.Single(item => item.Id == matches[0].Id);
            var singleBefore = tournament.Rounds[0].Matches.Single(item => item.Id == matches[1].Id);
            var doubleBefore = tournament.Rounds[0].Matches.Single(item => item.Id == matches[2].Id);
            Assert.Equal("running", runningBefore.Status);
            Assert.Null(runningBefore.Deadline);
            var originalSingleGrace = singleBefore.GraceDeadline!.Value;
            var originalDoubleGrace = doubleBefore.GraceDeadline!.Value;

            tournament = fixture.Store.PauseTournamentRound(fixture.Admin, tournament.Id, 1,
                new L12TournamentPausePayload(true, "冻结轮次计时"), tournament.Version,
                Context("pause-automatic-round"), true);
            Thread.Sleep(1100);
            var restarted = fixture.Restart();
            Assert.Equal(0, restarted.AdvanceAutomaticTournaments(start.AddHours(2)).Changed);
            tournament = restarted.Tournament(fixture.Admin, tournament.Id)!;
            Assert.True(tournament.Rounds[0].Paused);
            Assert.All(tournament.Participants, item => Assert.False(item.Dropped));
            Assert.Null(tournament.Rounds[0].Matches.Single(item => item.Id == matches[1].Id).Result);
            Assert.Null(tournament.Rounds[0].Matches.Single(item => item.Id == matches[2].Id).Result);

            tournament = restarted.PauseTournamentRound(fixture.Admin, tournament.Id, 1,
                new L12TournamentPausePayload(false, "恢复轮次计时"), tournament.Version,
                Context("resume-automatic-round"), true);
            var runningAfter = tournament.Rounds[0].Matches.Single(item => item.Id == matches[0].Id);
            var singleAfter = tournament.Rounds[0].Matches.Single(item => item.Id == matches[1].Id);
            var doubleAfter = tournament.Rounds[0].Matches.Single(item => item.Id == matches[2].Id);
            Assert.Equal("running", runningAfter.Status);
            Assert.Null(runningAfter.Deadline);
            Assert.True(singleAfter.GraceDeadline > originalSingleGrace);
            Assert.True(doubleAfter.GraceDeadline > originalDoubleGrace);
            Assert.Equal(singleAfter.GraceDeadline, tournament.NextAutomaticActionAt);
            Assert.Equal(0, restarted.AdvanceAutomaticTournaments(originalSingleGrace.AddMilliseconds(500)).Changed);

            var settlementAt = new[] { singleAfter.GraceDeadline!.Value, doubleAfter.GraceDeadline!.Value }.Max();
            Assert.Equal(1, restarted.AdvanceAutomaticTournaments(settlementAt).Changed);
            tournament = restarted.Tournament(fixture.Admin, tournament.Id)!;
            Assert.Equal("running", tournament.Rounds[0].Matches.Single(item => item.Id == matches[0].Id).Status);
            Assert.Equal("no-show-b", tournament.Rounds[0].Matches.Single(item => item.Id == matches[1].Id).Result);
            Assert.Equal("double-no-show", tournament.Rounds[0].Matches.Single(item => item.Id == matches[2].Id).Result);
            tournament = restarted.RecordTournamentGameResult(tournament.Id, matches[0].Id,
                "paused-running-table", 0);
            Assert.Equal(1, restarted.AdvanceAutomaticTournaments(settlementAt.AddMinutes(1)).Changed);
            Assert.Equal("completed", restarted.Tournament(fixture.Admin, tournament.Id)!.Status);
        }
        finally { fixture.Dispose(); }
    }

    [Fact]
    public void AutomaticNoShowRematchRestoresEntryAndRejectsStaleRulingVersion()
    {
        var fixture = CreateFixture("rematch", 1);
        try
        {
            var start = DateTimeOffset.UtcNow.AddMinutes(10);
            var tournament = RegisterAndLock(fixture, Payload(start, "single"));
            fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(-5));
            fixture.Store.AdvanceAutomaticTournaments(start);
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            var match = Assert.Single(Assert.Single(tournament.Rounds).Matches);
            var ready = Account(fixture, match.PlayerAAccountId);
            var absent = Account(fixture, match.PlayerBAccountId!);
            tournament = fixture.Store.CheckInTournament(ready, tournament.Id, 1,
                new L12TournamentCheckInPayload(null, true), tournament.Version,
                Context("rematch-one-ready"), true);
            fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(3));
            fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(8));
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            match = Assert.Single(tournament.Rounds[0].Matches);
            Assert.True(tournament.Participants.Single(item => item.AccountId == absent.Id).Dropped);
            var staleVersion = tournament.Version;

            tournament = fixture.Store.ApplyTournamentRuling(fixture.Judge, tournament.Id, match.Id,
                new L12TournamentRulingPayload("penalty", absent.Id, "warning", "记录迟到处罚"),
                tournament.Version, Context("penalty-before-rematch"), true);
            Assert.Throws<L12TournamentVersionConflictException>(() => fixture.Store.RequestTournamentRematch(
                fixture.Judge, tournament.Id, match.Id, new L12TournamentRematchPayload("旧版本重赛不得覆盖裁决"),
                staleVersion, Context("stale-rematch"), true));

            tournament = fixture.Store.RequestTournamentRematch(fixture.Judge, tournament.Id, match.Id,
                new L12TournamentRematchPayload("裁判批准重赛"), tournament.Version,
                Context("valid-rematch"), true);
            Assert.False(tournament.Participants.Single(item => item.AccountId == absent.Id).Dropped);
            var restarted = fixture.Restart();
            tournament = restarted.Tournament(fixture.Admin, tournament.Id)!;
            tournament = ReadyAll(fixture with { Store = restarted }, tournament);
            Assert.Equal(1, restarted.AdvanceAutomaticTournaments(
                tournament.NextAutomaticActionAt!.Value).Changed);
            tournament = restarted.Tournament(fixture.Admin, tournament.Id)!;
            match = Assert.Single(tournament.Rounds[0].Matches);
            var assignment = restarted.TournamentRoomAssignment(absent.Id, tournament.Id, match.Id, false);
            Assert.True(assignment.CanPlay);
            Assert.Equal("running", match.Status);
        }
        finally { fixture.Dispose(); }
    }

    [Fact]
    public void ExplicitDropAfterAutomaticNoShowCannotBeUndoneByRematch()
    {
        var fixture = CreateFixture("drop-race", 1);
        try
        {
            var start = DateTimeOffset.UtcNow.AddMinutes(10);
            var tournament = RegisterAndLock(fixture, Payload(start, "single"));
            fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(-5));
            fixture.Store.AdvanceAutomaticTournaments(start);
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            var match = Assert.Single(tournament.Rounds[0].Matches);
            var ready = Account(fixture, match.PlayerAAccountId);
            var absent = Account(fixture, match.PlayerBAccountId!);
            tournament = fixture.Store.CheckInTournament(ready, tournament.Id, 1,
                new L12TournamentCheckInPayload(null, true), tournament.Version,
                Context("drop-race-ready"), true);
            fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(3));
            fixture.Store.AdvanceAutomaticTournaments(start.AddMinutes(8));
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            var beforeExplicitDrop = tournament.Version;
            tournament = fixture.Store.DropTournament(absent, tournament.Id, tournament.Version,
                Context("explicit-drop-after-no-show"), true);
            Assert.Throws<L12TournamentVersionConflictException>(() => fixture.Store.RequestTournamentRematch(
                fixture.Judge, tournament.Id, match.Id, new L12TournamentRematchPayload("竞争中的旧重赛"),
                beforeExplicitDrop, Context("stale-rematch-after-drop"), true));
            Assert.Throws<L12TournamentVersionConflictException>(() => fixture.Store.RequestTournamentRematch(
                fixture.Judge, tournament.Id, match.Id, new L12TournamentRematchPayload("不得恢复主动退赛"),
                tournament.Version, Context("fresh-rematch-after-drop"), true));
            tournament = fixture.Store.Tournament(fixture.Admin, tournament.Id)!;
            Assert.True(tournament.Participants.Single(item => item.AccountId == absent.Id).Dropped);
            Assert.Equal("completed", Assert.Single(tournament.Rounds[0].Matches).Status);
        }
        finally { fixture.Dispose(); }
    }

    private static L12TournamentView RegisterAndLock(Fixture fixture, L12TournamentCreatePayload payload)
    {
        var tournament = fixture.Store.CreateTournament(fixture.Admin,
            payload with { RefereeAccountIds = [fixture.Judge.Id] }, Context("create-auto"), true);
        tournament = SelectDeck(fixture.Store, fixture.Admin, tournament, true);
        foreach (var player in fixture.Players)
            tournament = SelectDeck(fixture.Store, player, tournament, false);
        return tournament;
    }

    private static L12TournamentView SelectDeck(L12PlatformStore store, L12AccountView account,
        L12TournamentView tournament, bool update)
    {
        var deck = store.Decks(account.Id)[0];
        tournament = update
            ? store.UpdateTournamentRegistration(account, tournament.Id, new L12TournamentRegistrationPayload(),
                tournament.Version, Context("update-registration-" + account.Id), true)
            : store.RegisterTournament(account, tournament.Id, new L12TournamentRegistrationPayload(),
                tournament.Version, Context("register-" + account.Id), true);
        return store.PreCheckInTournament(account, tournament.Id,
            new L12TournamentPreCheckInPayload(deck.Name, string.Empty, deck.Id), tournament.Version,
            Context("lock-deck-" + account.Id), true);
    }

    private static L12TournamentView ReadyAll(Fixture fixture, L12TournamentView tournament)
    {
        var accounts = fixture.Players.Append(fixture.Admin).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var round = tournament.Rounds.Last();
        foreach (var accountId in round.Matches.SelectMany(match => new[]
                 { match.PlayerAAccountId, match.PlayerBAccountId }).Where(item => item is not null).Distinct()!)
            tournament = fixture.Store.CheckInTournament(accounts[accountId!], tournament.Id, round.Number,
                new L12TournamentCheckInPayload(null, true), tournament.Version,
                Context("round-ready-" + accountId), true);
        return tournament;
    }

    private static L12TournamentView ReadyMatch(Fixture fixture, L12TournamentView tournament,
        L12TournamentMatchView match, bool readyA, bool readyB)
    {
        if (readyA)
            tournament = fixture.Store.CheckInTournament(Account(fixture, match.PlayerAAccountId),
                tournament.Id, tournament.Rounds.Last().Number,
                new L12TournamentCheckInPayload(null, true), tournament.Version,
                Context("ready-match-a-" + match.Id), true);
        if (readyB && match.PlayerBAccountId is { } secondId)
            tournament = fixture.Store.CheckInTournament(Account(fixture, secondId),
                tournament.Id, tournament.Rounds.Last().Number,
                new L12TournamentCheckInPayload(null, true), tournament.Version,
                Context("ready-match-b-" + match.Id), true);
        return tournament;
    }

    private static L12AccountView Account(Fixture fixture, string accountId)
        => fixture.Players.Append(fixture.Admin).Single(item => item.Id == accountId);

    private static L12TournamentCreatePayload Payload(DateTimeOffset startAt, string format)
        => new("自动赛事回归", format, "public", 8, startAt, "现行规则", "automatic lifecycle",
            "after", "none", string.Empty, 50, 5, [], [], [],
            format == "single" ? 0 : 1, null, "public", 5, null, "automated", 3);

    private static L12AdminAuditContext Context(string reason)
        => new(Guid.NewGuid().ToString("N"), Reason: reason);

    private static Fixture CreateFixture(string name, int players)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-auto-tournament-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var path = Path.Combine(root, "platform.json");
        var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
        var registeredAdmin = store.Register("ta" + name[..Math.Min(name.Length, 8)] + "a", "password-123").Account!;
        Assert.True(store.SetRole(registeredAdmin.Id, "admin"));
        var admin = store.Accounts().Single(item => item.Id == registeredAdmin.Id);
        var accounts = Enumerable.Range(0, players).Select(index =>
            store.Register("tp" + name[..Math.Min(name.Length, 7)] + index, "password-123").Account!).ToArray();
        var judge = store.Register("tj" + name[..Math.Min(name.Length, 8)], "password-123").Account!;
        Assert.True(store.SendFriendRequest(admin.Id, judge.Id).Success);
        Assert.True(store.ResolveFriendRequest(judge.Id, admin.Id, true).Success);
        return new Fixture(root, path, catalog, store, admin, judge, accounts);
    }

    private sealed record Fixture(string Root, string Path, L12Catalog Catalog, L12PlatformStore Store,
        L12AccountView Admin, L12AccountView Judge, L12AccountView[] Players) : IDisposable
    {
        public L12PlatformStore Restart() => new(Path, Catalog.PresetDecks, officialCards: Catalog.Cards);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
