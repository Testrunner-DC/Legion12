using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class TournamentFullFlowTests
{
    [Fact]
    public void SwissPairingIsRepeatFreeAndFailsAtomicallyWhenNoCompletePairingExists()
    {
        var root = TempRoot();
        try
        {
            var (store, organizer, players) = CreateStoreAndPlayers(root, 4, "SwissHard");
            var tournament = CreateAndRegister(store, organizer, players,
                Payload("swiss") with { SwissRounds = 4 });
            tournament = store.StartTournament(organizer, tournament.Id, tournament.Version,
                Context("start"), true);

            var pairings = new HashSet<string>(StringComparer.Ordinal);
            for (var roundNumber = 1; roundNumber <= 3; roundNumber++)
            {
                var round = tournament.Rounds.Single(item => item.Number == roundNumber);
                foreach (var match in round.Matches.Where(item => item.PlayerBAccountId is not null))
                {
                    var key = PairKey(match.PlayerAAccountId, match.PlayerBAccountId!);
                    Assert.True(pairings.Add(key), $"第 {roundNumber} 轮出现重复交手 {key}");
                    tournament = store.ApplyTournamentRuling(organizer, tournament.Id, match.Id,
                        new L12TournamentRulingPayload("result", null, "draw", "test result"),
                        tournament.Version, Context($"r{roundNumber}-{match.Id}"), true);
                }

                var completed = tournament.Rounds.Single(item => item.Number == roundNumber);
                Assert.Equal("completed", completed.Status);
                Assert.Equal(4, completed.Standings.Count);
                Assert.Equal(Enumerable.Range(1, 4), completed.Standings.Select(item => item.Rank));
                Assert.All(completed.Standings, item => Assert.Equal(roundNumber, item.RoundNumber));
                if (roundNumber < 3)
                    tournament = store.CreateNextRound(organizer, tournament.Id, tournament.Version,
                        Context($"next-{roundNumber}"), true);
            }

            var before = tournament.Version;
            var error = Assert.Throws<L12TournamentPairingException>(() =>
                store.CreateNextRound(organizer, tournament.Id, tournament.Version,
                    Context("no-legal-pairing"), true));

            Assert.Contains("无法生成不重复交手的完整配对", error.Message);
            var unchanged = store.Tournament(organizer, tournament.Id)!;
            Assert.Equal(before, unchanged.Version);
            Assert.Equal(3, unchanged.Rounds.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SwissDownfloatChoosesHighestOpponentScoreFromOneWinLowerGroup()
    {
        var root = TempRoot();
        try
        {
            var (store, organizer, players) = CreateStoreAndPlayers(root, 8, "Downfloat");
            var tournament = CreateAndRegister(store, organizer, players,
                Payload("swiss") with { SwissRounds = 3 });
            tournament = store.StartTournament(organizer, tournament.Id, tournament.Version,
                Context("downfloat-start"), true);
            var accountBySeed = tournament.Participants.ToDictionary(item => item.Seed,
                item => item.AccountId);

            var firstRoundWinners = new HashSet<string>(
                new[] { 1, 3, 5, 7 }.Select(seed => accountBySeed[seed]), StringComparer.Ordinal);
            foreach (var match in tournament.Rounds[0].Matches)
                tournament = store.ApplyTournamentRuling(organizer, tournament.Id, match.Id,
                    new L12TournamentRulingPayload("result", null,
                        DecisionForWinner(match, firstRoundWinners), "seeded first round"),
                    tournament.Version, Context($"downfloat-r1-{match.Id}"), true);
            tournament = store.CreateNextRound(organizer, tournament.Id, tournament.Version,
                Context("downfloat-r2"), true);

            var secondRoundWinners = new HashSet<string>(
                new[] { 1, 4, 5 }.Select(seed => accountBySeed[seed]), StringComparer.Ordinal);
            foreach (var match in tournament.Rounds[1].Matches)
            {
                var isDraw = new[] { match.PlayerAAccountId, match.PlayerBAccountId! }
                    .ToHashSet(StringComparer.Ordinal).SetEquals(
                        new[] { accountBySeed[6], accountBySeed[8] });
                tournament = store.ApplyTournamentRuling(organizer, tournament.Id, match.Id,
                    new L12TournamentRulingPayload("result", null,
                        isDraw ? "draw" : DecisionForWinner(match, secondRoundWinners), "shape downfloat"),
                    tournament.Version, Context($"downfloat-r2-{match.Id}"), true);
            }
            tournament = store.CreateNextRound(organizer, tournament.Id, tournament.Version,
                Context("downfloat-r3"), true);

            var thirdRoundPairs = tournament.Rounds[2].Matches.Where(item => item.PlayerBAccountId is not null)
                .Select(item => PairKey(item.PlayerAAccountId, item.PlayerBAccountId!)).ToHashSet(StringComparer.Ordinal);
            Assert.Contains(PairKey(accountBySeed[4], accountBySeed[6]), thirdRoundPairs);
            Assert.DoesNotContain(PairKey(accountBySeed[4], accountBySeed[8]), thirdRoundPairs);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SwissByesAreDeterministicAndRotateUntilEveryPlayerHasReceivedOne()
    {
        var root = TempRoot();
        try
        {
            var (store, organizer, players) = CreateStoreAndPlayers(root, 3, "SwissBye");
            var tournament = CreateAndRegister(store, organizer, players,
                Payload("swiss") with { SwissRounds = 3 });
            tournament = store.StartTournament(organizer, tournament.Id, tournament.Version,
                Context("bye-start"), true);
            var byes = new List<string>();
            var pairs = new HashSet<string>(StringComparer.Ordinal);

            for (var roundNumber = 1; roundNumber <= 3; roundNumber++)
            {
                var round = tournament.Rounds.Single(item => item.Number == roundNumber);
                var bye = Assert.Single(round.Matches.Where(item => item.Result == "bye"));
                byes.Add(bye.PlayerAAccountId);
                var played = Assert.Single(round.Matches.Where(item => item.PlayerBAccountId is not null));
                Assert.True(pairs.Add(PairKey(played.PlayerAAccountId, played.PlayerBAccountId!)));
                tournament = store.ApplyTournamentRuling(organizer, tournament.Id, played.Id,
                    new L12TournamentRulingPayload("result", null, "draw", "rotate bye"),
                    tournament.Version, Context($"bye-r{roundNumber}"), true);
                if (roundNumber < 3)
                    tournament = store.CreateNextRound(organizer, tournament.Id, tournament.Version,
                        Context($"bye-next-{roundNumber}"), true);
            }

            Assert.Equal(3, byes.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(3, pairs.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PreStartDropsStayOutOfPairingsWithoutDuplicatingFixedSeeds()
    {
        var root = TempRoot();
        try
        {
            var (store, organizer, players) = CreateStoreAndPlayers(root, 4, "SwissSeedDrop");
            var tournament = CreateAndRegister(store, organizer, players,
                Payload("swiss") with { SwissRounds = 1 });
            var dropped = players[0];
            tournament = store.DropTournament(dropped, tournament.Id, tournament.Version,
                Context("pre-start-drop"), true);
            tournament = store.StartTournament(organizer, tournament.Id, tournament.Version,
                Context("start-after-drop"), true);

            Assert.Equal(tournament.Participants.Count,
                tournament.Participants.Select(item => item.Seed).Distinct().Count());
            Assert.DoesNotContain(tournament.Rounds[0].Matches,
                match => match.PlayerAAccountId == dropped.Id || match.PlayerBAccountId == dropped.Id);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SwissCutPersistsFinalSwissStandingsAndAdvancesOnlyWinners()
    {
        var root = TempRoot();
        try
        {
            var (store, organizer, players) = CreateStoreAndPlayers(root, 8, "SwissCut");
            var tournament = CreateAndRegister(store, organizer, players,
                Payload("swiss-cut") with { SwissRounds = 1, CutSize = 4 });
            tournament = store.StartTournament(organizer, tournament.Id, tournament.Version,
                Context("cut-start"), true);
            foreach (var match in tournament.Rounds[0].Matches)
                tournament = store.ApplyTournamentRuling(organizer, tournament.Id, match.Id,
                    new L12TournamentRulingPayload("result", null, "player-a", "swiss result"),
                    tournament.Version, Context($"swiss-{match.Id}"), true);

            tournament = store.CreateNextRound(organizer, tournament.Id, tournament.Version,
                Context("create-cut"), true);

            Assert.Equal(8, tournament.FinalSwissStandings.Count);
            Assert.Equal("elimination", tournament.Rounds[1].Stage);
            Assert.Equal(2, tournament.Rounds[1].Matches.Count);
            var cutAccounts = tournament.FinalSwissStandings.Take(4).Select(item => item.AccountId).ToHashSet();
            Assert.All(tournament.Rounds[1].Matches, match =>
            {
                Assert.Contains(match.PlayerAAccountId, cutAccounts);
                Assert.Contains(match.PlayerBAccountId!, cutAccounts);
            });

            var semifinalWinners = new HashSet<string>(StringComparer.Ordinal);
            var semifinalLosers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var match in tournament.Rounds[1].Matches)
            {
                semifinalWinners.Add(match.PlayerAAccountId);
                semifinalLosers.Add(match.PlayerBAccountId!);
                tournament = store.ApplyTournamentRuling(organizer, tournament.Id, match.Id,
                    new L12TournamentRulingPayload("result", null, "player-a", "semifinal result"),
                    tournament.Version, Context($"semi-{match.Id}"), true);
            }
            tournament = store.CreateNextRound(organizer, tournament.Id, tournament.Version,
                Context("create-final"), true);

            var final = Assert.Single(tournament.Rounds[2].Matches);
            Assert.Equal("elimination", tournament.Rounds[2].Stage);
            Assert.Contains(final.PlayerAAccountId, semifinalWinners);
            Assert.Contains(final.PlayerBAccountId!, semifinalWinners);
            Assert.DoesNotContain(final.PlayerAAccountId, semifinalLosers);
            Assert.DoesNotContain(final.PlayerBAccountId!, semifinalLosers);
            Assert.Equal(2, final.SourceMatchIds.Count);
            Assert.Equal(4, tournament.EliminationBracket[0].Matches.Count * 2);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SwissCutExcludesPlayersWhoDropAfterFinalSwissStandingsAreCaptured()
    {
        var root = TempRoot();
        try
        {
            var (store, organizer, players) = CreateStoreAndPlayers(root, 8, "SwissCutDrop");
            var accounts = players.Append(organizer).ToDictionary(item => item.Id, StringComparer.Ordinal);
            var tournament = CreateAndRegister(store, organizer, players,
                Payload("swiss-cut") with { SwissRounds = 1, CutSize = 4 });
            tournament = store.StartTournament(organizer, tournament.Id, tournament.Version,
                Context("cut-drop-start"), true);
            foreach (var match in tournament.Rounds[0].Matches)
                tournament = store.ApplyTournamentRuling(organizer, tournament.Id, match.Id,
                    new L12TournamentRulingPayload("result", null, "player-a", "swiss result"),
                    tournament.Version, Context($"cut-drop-{match.Id}"), true);

            var droppedId = tournament.FinalSwissStandings[0].AccountId;
            var replacementId = tournament.FinalSwissStandings[4].AccountId;
            tournament = store.DropTournament(accounts[droppedId], tournament.Id, tournament.Version,
                Context("drop-before-cut"), true);
            tournament = store.CreateNextRound(organizer, tournament.Id, tournament.Version,
                Context("create-cut-after-drop"), true);

            var cutAccounts = tournament.Rounds[1].Matches
                .SelectMany(match => new[] { match.PlayerAAccountId, match.PlayerBAccountId! })
                .ToHashSet(StringComparer.Ordinal);
            Assert.DoesNotContain(droppedId, cutAccounts);
            Assert.Contains(replacementId, cutAccounts);
            Assert.Equal(8, tournament.FinalSwissStandings.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PureSingleEliminationBuildsARealBracketAndCompletesOnlyAfterFinal()
    {
        var root = TempRoot();
        try
        {
            var (store, organizer, players) = CreateStoreAndPlayers(root, 4, "SingleBracket");
            var tournament = CreateAndRegister(store, organizer, players, Payload("single"));
            tournament = store.StartTournament(organizer, tournament.Id, tournament.Version,
                Context("single-start"), true);
            Assert.Equal("elimination", tournament.Rounds[0].Stage);
            Assert.Equal(2, tournament.Rounds[0].Matches.Count);
            var semifinalWinners = new HashSet<string>(StringComparer.Ordinal);
            var semifinalLosers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var match in tournament.Rounds[0].Matches)
            {
                semifinalWinners.Add(match.PlayerAAccountId);
                semifinalLosers.Add(match.PlayerBAccountId!);
                tournament = store.ApplyTournamentRuling(organizer, tournament.Id, match.Id,
                    new L12TournamentRulingPayload("result", null, "player-a", "single semifinal"),
                    tournament.Version, Context($"single-semi-{match.Id}"), true);
            }
            Assert.Throws<L12TournamentVersionConflictException>(() =>
                store.CompleteTournament(organizer, tournament.Id, tournament.Version,
                    Context("single-too-early"), true));

            tournament = store.CreateNextRound(organizer, tournament.Id, tournament.Version,
                Context("single-final"), true);
            var final = Assert.Single(tournament.Rounds[1].Matches);
            Assert.Contains(final.PlayerAAccountId, semifinalWinners);
            Assert.Contains(final.PlayerBAccountId!, semifinalWinners);
            Assert.DoesNotContain(final.PlayerAAccountId, semifinalLosers);
            Assert.DoesNotContain(final.PlayerBAccountId!, semifinalLosers);
            tournament = store.ApplyTournamentRuling(organizer, tournament.Id, final.Id,
                new L12TournamentRulingPayload("result", null, "player-b", "single final"),
                tournament.Version, Context("single-final-result"), true);
            tournament = store.CompleteTournament(organizer, tournament.Id, tournament.Version,
                Context("single-complete"), true);

            Assert.Equal("completed", tournament.Status);
            Assert.Equal(2, tournament.EliminationBracket.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void EliminationWinnerWhoDropsBeforeNextRoundBecomesABracketBye()
    {
        var root = TempRoot();
        try
        {
            var (store, organizer, players) = CreateStoreAndPlayers(root, 4, "BracketDrop");
            var accounts = players.Append(organizer).ToDictionary(item => item.Id, StringComparer.Ordinal);
            var tournament = CreateAndRegister(store, organizer, players, Payload("single"));
            tournament = store.StartTournament(organizer, tournament.Id, tournament.Version,
                Context("bracket-drop-start"), true);
            var semifinals = tournament.Rounds[0].Matches.ToArray();
            foreach (var match in semifinals)
                tournament = store.ApplyTournamentRuling(organizer, tournament.Id, match.Id,
                    new L12TournamentRulingPayload("result", null, "player-a", "semifinal winner"),
                    tournament.Version, Context($"bracket-drop-{match.Id}"), true);

            var droppedWinner = semifinals[0].PlayerAAccountId;
            var advancingWinner = semifinals[1].PlayerAAccountId;
            tournament = store.DropTournament(accounts[droppedWinner], tournament.Id, tournament.Version,
                Context("drop-semifinal-winner"), true);
            tournament = store.CreateNextRound(organizer, tournament.Id, tournament.Version,
                Context("create-final-bye"), true);

            var final = Assert.Single(tournament.Rounds[1].Matches);
            Assert.Equal(advancingWinner, final.PlayerAAccountId);
            Assert.Null(final.PlayerBAccountId);
            Assert.Equal("bye", final.Result);
            Assert.Equal("completed", tournament.Rounds[1].Status);
            Assert.DoesNotContain(droppedWinner,
                new[] { final.PlayerAAccountId, final.PlayerBAccountId });
            tournament = store.CompleteTournament(organizer, tournament.Id, tournament.Version,
                Context("complete-after-bracket-bye"), true);
            Assert.Equal("completed", tournament.Status);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task TournamentRoomUsesBoundDeckAndRulesSnapshotsAndWritesResultIdempotently()
    {
        var root = TempRoot();
        MatchRecorder? recorder = null;
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var organizer = store.Register("RoomFlowHost", "password-123").Account!;
            var player = store.Register("RoomFlowPlayer", "password-123").Account!;
            var outsider = store.Register("RoomFlowOutside", "password-123").Account!;
            var referee = store.Register("RoomFlowJudge", "password-123").Account!;
            MakeFriends(store, organizer, referee);
            var tournament = CreateAndRegister(store, organizer, [player],
                Payload("single") with { RefereeAccountIds = [referee.Id] });
            tournament = store.StartTournament(organizer, tournament.Id, tournament.Version,
                Context("room-start"), true);
            var match = Assert.Single(tournament.Rounds[0].Matches);
            tournament = store.CheckInTournament(organizer, tournament.Id, 1,
                new L12TournamentCheckInPayload(organizer.Id, true), tournament.Version,
                Context("ready-a"), true);
            tournament = store.CheckInTournament(player, tournament.Id, 1,
                new L12TournamentCheckInPayload(null, true), tournament.Version,
                Context("ready-b"), true);
            tournament = store.StartTournamentRound(organizer, tournament.Id, 1, tournament.Version,
                Context("round-start"), true);

            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store);
            var hostSession = Guid.NewGuid();
            var playerSession = Guid.NewGuid();
            var outsiderSession = Guid.NewGuid();
            var refereeSession = Guid.NewGuid();
            rooms.Connect(hostSession, organizer.Id, organizer.Username);
            rooms.Connect(playerSession, player.Id, player.Username);
            rooms.Connect(outsiderSession, outsider.Id, outsider.Username);
            rooms.Connect(refereeSession, referee.Id, referee.Username);

            var denied = await rooms.EnterTournamentMatchAsync(outsiderSession, tournament.Id, match.Id);
            Assert.Equal("tournamentRoomRejected", MessageType(Assert.Single(denied).Payload));
            var firstEntry = await rooms.EnterTournamentMatchAsync(hostSession, tournament.Id, match.Id);
            Assert.Contains(firstEntry, message => MessageType(message.Payload) == "roomState");
            var rulesChange = rooms.UpdateRoomOptions(hostSession, new L12RoomOptions
            {
                MatchModeId = "friendly",
                DisasterMode = "none",
                Spectating = "enabled",
                UseCardRestrictions = false,
            });
            Assert.Equal("error", MessageType(Assert.Single(rulesChange).Payload));
            var secondEntry = await rooms.EnterTournamentMatchAsync(playerSession, tournament.Id, match.Id);
            var gameMessage = secondEntry.Single(message => message.SessionId == playerSession
                && MessageType(message.Payload) == "gameState");
            using (var gameDocument = JsonDocument.Parse(JsonSerializer.Serialize(gameMessage.Payload)))
            {
                Assert.Equal(tournament.Id, gameDocument.RootElement.GetProperty("tournamentId").GetString());
                Assert.Equal(tournament.Code, gameDocument.RootElement.GetProperty("tournamentCode").GetString());
                Assert.Equal(match.Id, gameDocument.RootElement.GetProperty("tournamentMatchId").GetString());
            }
            var spectate = rooms.SpectateTournamentMatch(refereeSession, tournament.Id, match.Id);
            var spectatorMessage = Assert.Single(spectate);
            using (var spectatorDocument = JsonDocument.Parse(JsonSerializer.Serialize(spectatorMessage.Payload)))
                Assert.Equal(tournament.Code,
                    spectatorDocument.RootElement.GetProperty("tournamentCode").GetString());

            using var surrender = JsonDocument.Parse("{\"type\":\"surrender\"}");
            var gameOver = await rooms.HandleActionAsync(hostSession, surrender.RootElement);
            Assert.Contains(gameOver, message => MessageType(message.Payload) == "gameState");
            Assert.DoesNotContain(gameOver, message => MessageType(message.Payload) == "tournamentResultPending");
            var recorded = Assert.Single(await recorder.ListMatchesAsync());
            Assert.Equal(match.RoomCode, recorded.RoomCode);
            Assert.NotNull(recorded.EndedUtc);
            var result = store.Tournament(organizer, tournament.Id)!;
            var automaticallyRecorded = result.Rounds.SelectMany(round => round.Matches)
                .Single(item => item.Id == match.Id);
            Assert.Equal("player-b", automaticallyRecorded.Result);
            Assert.Equal(recorded.MatchId, automaticallyRecorded.RecordedMatchId);
            var replay = store.RecordTournamentGameResult(tournament.Id, match.Id, recorded.MatchId, 1);

            Assert.Equal(result.Version, replay.Version);
            var completedMatch = result.Rounds.SelectMany(round => round.Matches).Single(item => item.Id == match.Id);
            Assert.Equal("player-b", completedMatch.Result);
            Assert.Equal(recorded.MatchId, completedMatch.RecordedMatchId);
            Assert.Single(completedMatch.Events.Where(item => item.Kind == "game-result"));
            Assert.Equal(tournament.Rules.Hash, completedMatch.RulesHash);
            Assert.All(result.Participants, item => Assert.False(string.IsNullOrWhiteSpace(item.Deck!.Hash)));
        }
        finally
        {
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void StaffRulingPrecedesLateGameResultWhileTheRecordBindingStaysIdempotent()
    {
        var root = TempRoot();
        try
        {
            var (store, organizer, players) = CreateStoreAndPlayers(root, 2, "RulingRace");
            var tournament = CreateAndRegister(store, organizer, players, Payload("single"));
            tournament = store.StartTournament(organizer, tournament.Id, tournament.Version,
                Context("ruling-race-start"), true);
            var match = Assert.Single(tournament.Rounds[0].Matches);
            foreach (var account in players.Append(organizer))
                tournament = store.CheckInTournament(account, tournament.Id, 1,
                    new L12TournamentCheckInPayload(null, true), tournament.Version,
                    Context($"ruling-race-ready-{account.Id}"), true);
            tournament = store.StartTournamentRound(organizer, tournament.Id, 1, tournament.Version,
                Context("ruling-race-round"), true);
            tournament = store.ApplyTournamentRuling(organizer, tournament.Id, match.Id,
                new L12TournamentRulingPayload("result", null, "player-a", "staff adjudication"),
                tournament.Version, Context("staff-result-first"), true);

            var bound = store.RecordTournamentGameResult(tournament.Id, match.Id, "late-game-record", 1);
            var resolved = Assert.Single(bound.Rounds[0].Matches);
            Assert.Equal("player-a", resolved.Result);
            Assert.Equal("late-game-record", resolved.RecordedMatchId);
            Assert.Contains(resolved.Events, item => item.Kind == "game-result-after-ruling"
                && item.Result == "player-b");
            var replay = store.RecordTournamentGameResult(tournament.Id, match.Id, "late-game-record", 1);
            Assert.Equal(bound.Version, replay.Version);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void StartingRoundStartsReadyTablesWithoutWaitingForOtherTables()
    {
        var root = TempRoot();
        try
        {
            var (store, organizer, players) = CreateStoreAndPlayers(root, 4, "PartialReady");
            var tournament = CreateAndRegister(store, organizer, players,
                Payload("single") with { LateGraceMinutes = 5 });
            tournament = store.StartTournament(organizer, tournament.Id, tournament.Version,
                Context("partial-start"), true);
            var first = tournament.Rounds[0].Matches[0];
            var second = tournament.Rounds[0].Matches[1];
            var accounts = players.Append(organizer).ToDictionary(item => item.Id, StringComparer.Ordinal);

            foreach (var accountId in new[] { first.PlayerAAccountId, first.PlayerBAccountId! })
                tournament = store.CheckInTournament(accounts[accountId], tournament.Id, 1,
                    new L12TournamentCheckInPayload(null, true), tournament.Version,
                    Context($"ready-{accountId}"), true);
            tournament = store.CheckInTournament(accounts[second.PlayerAAccountId], tournament.Id, 1,
                new L12TournamentCheckInPayload(null, true), tournament.Version,
                Context("one-player-late-table"), true);

            tournament = store.StartTournamentRound(organizer, tournament.Id, 1, tournament.Version,
                Context("start-ready-tables"), true);

            var round = Assert.Single(tournament.Rounds);
            Assert.Equal("running", round.Status);
            Assert.Equal("running", round.Matches.Single(item => item.Id == first.Id).Status);
            var waiting = round.Matches.Single(item => item.Id == second.Id);
            Assert.Equal("waiting", waiting.Status);
            Assert.NotNull(waiting.GraceDeadline);
            Assert.Contains(waiting.Events, item => item.Kind == "late-grace");
            Assert.NotNull(store.TournamentRoomAssignment(first.PlayerAAccountId, tournament.Id, first.Id, false));
            Assert.Throws<L12TournamentVersionConflictException>(() =>
                store.TournamentRoomAssignment(second.PlayerAAccountId, tournament.Id, second.Id, false));

            var before = tournament.Version;
            Assert.Throws<L12TournamentVersionConflictException>(() =>
                store.ApplyTournamentRuling(organizer, tournament.Id, second.Id,
                    new L12TournamentRulingPayload("no-show", second.PlayerBAccountId, "no-show-b", "too early"),
                    tournament.Version, Context("early-no-show"), true));
            Assert.Equal(before, store.Tournament(organizer, tournament.Id)!.Version);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ZeroMinuteGraceAllowsImmediateDeterministicNoShowRuling()
    {
        var root = TempRoot();
        try
        {
            var (store, organizer, players) = CreateStoreAndPlayers(root, 2, "NoShow");
            var tournament = CreateAndRegister(store, organizer, players,
                Payload("single") with { LateGraceMinutes = 0 });
            tournament = store.StartTournament(organizer, tournament.Id, tournament.Version,
                Context("no-show-start"), true);
            var match = Assert.Single(tournament.Rounds[0].Matches);
            tournament = store.StartTournamentRound(organizer, tournament.Id, 1, tournament.Version,
                Context("zero-grace-start"), true);
            tournament = store.ApplyTournamentRuling(organizer, tournament.Id, match.Id,
                new L12TournamentRulingPayload("no-show", match.PlayerBAccountId, "no-show-b", "grace elapsed"),
                tournament.Version, Context("no-show-b"), true);

            var completed = Assert.Single(tournament.Rounds[0].Matches);
            Assert.Equal("completed", completed.Status);
            Assert.Equal("no-show-b", completed.Result);
            Assert.Equal("completed", tournament.Rounds[0].Status);
            Assert.Contains(completed.Events, item => item.Kind == "no-show");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CodeOnlyTournamentIsHiddenFromDiscoveryButReadableThroughStableShareCode()
    {
        var root = TempRoot();
        try
        {
            var (store, organizer, players) = CreateStoreAndPlayers(root, 2, "ShareLink");
            var tournament = CreateAndRegister(store, organizer, players,
                Payload("swiss") with { Visibility = "code" });
            var visitor = store.Register("ShareLinkVisitor", "password-123").Account!;

            Assert.DoesNotContain(store.Tournaments(visitor, null, null, false).Items,
                item => item.Id == tournament.Id);
            var shared = store.TournamentByCode(visitor, tournament.Code);
            Assert.NotNull(shared);
            Assert.Equal(tournament.Id, shared.Id);
            var visitorDeck = store.Decks(visitor.Id)[0];
            var joined = store.RegisterTournament(visitor, shared.Id,
                new L12TournamentRegistrationPayload(visitorDeck.Name, string.Empty), shared.Version,
                Context("join-from-share"), true);
            Assert.Contains(joined.Participants, item => item.AccountId == visitor.Id
                && item.Deck?.Name == visitorDeck.Name);
        }
        finally { Directory.Delete(root, true); }
    }

    private static (L12PlatformStore Store, L12AccountView Organizer, L12AccountView[] Players)
        CreateStoreAndPlayers(string root, int totalPlayers, string prefix)
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var organizer = store.Register($"{prefix}Host", "password-123").Account!;
        var players = Enumerable.Range(2, totalPlayers - 1)
            .Select(index => store.Register($"{prefix}P{index}", "password-123").Account!).ToArray();
        return (store, organizer, players);
    }

    private static L12TournamentView CreateAndRegister(L12PlatformStore store, L12AccountView organizer,
        IReadOnlyList<L12AccountView> players, L12TournamentCreatePayload payload)
    {
        var tournament = store.CreateTournament(organizer, payload, Context("create"), true);
        tournament = SelectDeck(store, organizer, tournament, update: true);
        foreach (var player in players)
            tournament = SelectDeck(store, player, tournament, update: false);
        return tournament;
    }

    private static L12TournamentView SelectDeck(L12PlatformStore store, L12AccountView account,
        L12TournamentView tournament, bool update)
    {
        var decks = store.Decks(account.Id);
        var deck = decks[Math.Abs(StringComparer.Ordinal.GetHashCode(account.Id)) % decks.Count];
        var payload = new L12TournamentRegistrationPayload(deck.Name, string.Empty);
        return update
            ? store.UpdateTournamentRegistration(account, tournament.Id, payload, tournament.Version,
                Context($"deck-{account.Id}"), true)
            : store.RegisterTournament(account, tournament.Id, payload, tournament.Version,
                Context($"deck-{account.Id}"), true);
    }

    private static L12TournamentCreatePayload Payload(string format)
        => new("完整流程赛事", format, "public", 32, DateTimeOffset.UtcNow.AddHours(1), "S01/S02",
            "full flow", "after", "season", string.Empty, 50, 5,
            RegistrationVisibility: "public", LateGraceMinutes: 5);

    private static void MakeFriends(L12PlatformStore store, L12AccountView first, L12AccountView second)
    {
        Assert.True(store.SendFriendRequest(first.Id, second.Id).Success);
        Assert.True(store.ResolveFriendRequest(second.Id, first.Id, true).Success);
    }

    private static string PairKey(string first, string second)
        => string.CompareOrdinal(first, second) < 0 ? $"{first}|{second}" : $"{second}|{first}";

    private static string DecisionForWinner(L12TournamentMatchView match, IReadOnlySet<string> winners)
    {
        if (winners.Contains(match.PlayerAAccountId)) return "player-a";
        if (match.PlayerBAccountId is not null && winners.Contains(match.PlayerBAccountId)) return "player-b";
        throw new InvalidOperationException("测试未为该桌指定胜者");
    }

    private static string MessageType(object payload)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return document.RootElement.GetProperty("type").GetString() ?? string.Empty;
    }

    private static L12AdminAuditContext Context(string correlationId)
        => new(correlationId, "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test/tournaments");

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-tournament-full-flow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
