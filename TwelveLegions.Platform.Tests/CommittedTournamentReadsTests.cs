using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class CommittedTournamentReadsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public async Task TournamentAccessDoesNotWaitForAnUnrelatedSave()
    {
        using var fixture = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("synthetic tournament barrier");
        };
        var writer = Task.Factory.StartNew(() => fixture.Store.Register("ctrunrel", "password-123"),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            var stopwatch = Stopwatch.StartNew();
            var access = await Task.Run(() => fixture.Store.TournamentRoomAccess(fixture.Referee.Id,
                    fixture.Tournament.Id, fixture.Match.Id, spectate: true))
                .WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(access.CanRefereeView);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        }
        finally
        {
            release.Set();
            Assert.True((await writer).Success);
            fixture.Store.StorageFailureInjector = null;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaffRevocationPublishesOnlyAfterSuccessfulCommit(bool fail)
    {
        using var fixture = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("synthetic revocation barrier");
            if (fail) throw new IOException("synthetic rejected tournament commit");
        };
        var beforeVersion = fixture.Tournament.Version;
        var writer = Task.Factory.StartNew(() => fixture.Store.SetTournamentStaff(fixture.Organizer,
                fixture.Tournament.Id, new L12TournamentStaffPayload([]), beforeVersion,
                Context("staff-revoke"), true),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            Assert.True(fixture.Store.TournamentRoomAccess(fixture.Referee.Id,
                fixture.Tournament.Id, fixture.Match.Id, spectate: true).CanRefereeView);
        }
        finally
        {
            release.Set();
            if (fail)
                await Assert.ThrowsAsync<L12PlatformStorageUnavailableException>(async () => await writer);
            else fixture.Tournament = await writer;
            fixture.Store.StorageFailureInjector = null;
        }
        if (fail)
            Assert.True(fixture.Store.TournamentRoomAccess(fixture.Referee.Id,
                fixture.Tournament.Id, fixture.Match.Id, spectate: true).CanRefereeView);
        else
            Assert.Throws<L12TournamentScopeException>(() => fixture.Store.TournamentRoomAccess(
                fixture.Referee.Id, fixture.Tournament.Id, fixture.Match.Id, spectate: true));
    }

    [Fact]
    public async Task OwningTransactionSeesStagedRevocationWhileExternalReaderKeepsOldGeneration()
    {
        using var fixture = new Fixture();
        using var staged = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("synthetic staged barrier");
        };
        var writer = Task.Factory.StartNew(() => fixture.Store.ExecuteAdminTransaction(() =>
        {
            RefereeIds(TournamentRow(fixture.Store, fixture.Tournament.Id)).Clear();
            Assert.Throws<L12TournamentScopeException>(() => fixture.Store.TournamentRoomAccess(
                fixture.Referee.Id, fixture.Tournament.Id, fixture.Match.Id, spectate: true));
            staged.Set();
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(staged.Wait(TimeSpan.FromSeconds(3)));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            Assert.True(fixture.Store.TournamentRoomAccess(fixture.Referee.Id,
                fixture.Tournament.Id, fixture.Match.Id, spectate: true).CanRefereeView);
        }
        finally
        {
            release.Set();
            Assert.True(await writer);
            fixture.Store.StorageFailureInjector = null;
        }
        Assert.Throws<L12TournamentScopeException>(() => fixture.Store.TournamentRoomAccess(
            fixture.Referee.Id, fixture.Tournament.Id, fixture.Match.Id, spectate: true));
    }

    [Theory]
    [InlineData("role")]
    [InlineData("disabled")]
    [InlineData("deleted")]
    public void AccountRoleAndLifecycleChangesRevokeGlobalTournamentAccess(string change)
    {
        using var fixture = new Fixture();
        var registration = fixture.Store.Register(change switch
        {
            "disabled" => "ctrdisable",
            "deleted" => "ctrdelete",
            _ => "ctrrole",
        }, "password-123");
        Assert.True(registration.Success, registration.Message);
        var candidate = Assert.IsType<L12AccountView>(registration.Account);
        Assert.True(fixture.Store.SetRole(fixture.Admin, candidate.Id, "admin", Context("grant-global")));
        Assert.True(fixture.Store.TournamentRoomAccess(candidate.Id, fixture.Tournament.Id,
            fixture.Match.Id, spectate: true).CanRefereeView);

        switch (change)
        {
            case "role":
                Assert.True(fixture.Store.SetRole(fixture.Admin, candidate.Id, "player", Context("revoke-role")));
                break;
            case "disabled":
                fixture.Store.SetAccountDisabled(fixture.Admin, candidate.Id, true, "synthetic disable",
                    Context("disable"), true);
                break;
            case "deleted":
                fixture.Store.DeleteAccountPersonalData(fixture.Admin, candidate.Id, "synthetic delete",
                    Context("delete"), true);
                break;
        }

        Assert.Throws<L12TournamentScopeException>(() => fixture.Store.TournamentRoomAccess(candidate.Id,
            fixture.Tournament.Id, fixture.Match.Id, spectate: true));
    }

    [Fact]
    public void ReturnedAssignmentsDefensivelyMaterializeDecksAndPolicy()
    {
        using var fixture = new Fixture();
        var first = fixture.Store.TournamentRoomAssignment(fixture.Organizer.Id,
            fixture.Tournament.Id, fixture.Match.Id, spectate: false);
        var expectedCard = Assert.Single(first.PlayerA.Deck.CardIds.Take(1));
        var expectedDisaster = Assert.Single(first.OperationsPolicy.DisasterCardIds.Take(1));
        first.PlayerA.Deck.CardIds.Clear();
        first.PlayerA.Deck.MoraleIds.Clear();
        first.PlayerA.Deck.AlternateArtSelections["synthetic"] = "pollution";
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)first.OperationsPolicy.DisasterCardIds).Clear());

        var second = fixture.Store.TournamentRoomAssignment(fixture.Organizer.Id,
            fixture.Tournament.Id, fixture.Match.Id, spectate: false);
        Assert.NotSame(first.PlayerA.Deck, second.PlayerA.Deck);
        Assert.NotSame(first.OperationsPolicy, second.OperationsPolicy);
        Assert.Equal(expectedCard, second.PlayerA.Deck.CardIds[0]);
        Assert.Equal(expectedDisaster, second.OperationsPolicy.DisasterCardIds[0]);
        Assert.DoesNotContain("synthetic", second.PlayerA.Deck.AlternateArtSelections.Keys);
    }

    [Fact]
    public void RepeatedEligibilityChecksNeverMaterializeMutableDecks()
    {
        using var fixture = new Fixture();
        var expansions = 0;
        fixture.Store.DeckPayloadExpansionObserver = _ => expansions++;
        for (var index = 0; index < 100; index++)
        {
            Assert.True(fixture.Store.TournamentRoomAccess(fixture.Organizer.Id,
                fixture.Tournament.Id, fixture.Match.Id, spectate: false).CanPlay);
            Assert.True(fixture.Store.TournamentRoomAccess(fixture.Referee.Id,
                fixture.Tournament.Id, fixture.Match.Id, spectate: true).CanRefereeView);
        }
        Assert.Equal(0, expansions);
        _ = fixture.Store.TournamentRoomAssignment(fixture.Organizer.Id,
            fixture.Tournament.Id, fixture.Match.Id, spectate: false);
        Assert.Equal(2, expansions);
        for (var index = 0; index < 100; index++)
            _ = fixture.Store.TournamentRoomAccess(fixture.Organizer.Id,
                fixture.Tournament.Id, fixture.Match.Id, spectate: false);
        Assert.Equal(2, expansions);
    }

    [Fact]
    public void PrivateOutsiderSeesScopeFailureBeforeBrokenDeckDetails()
    {
        using var fixture = new Fixture();
        var outsider = fixture.Register("ctrout");
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            MatchRow(TournamentRow(fixture.Store, fixture.Tournament.Id), fixture.Match.Id).GetType()
                .GetProperty("PlayerADeckHash")!.SetValue(
                    MatchRow(TournamentRow(fixture.Store, fixture.Tournament.Id), fixture.Match.Id),
                    "synthetic-broken-hash");
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });

        Assert.Throws<L12TournamentScopeException>(() => fixture.Store.TournamentRoomAccess(outsider.Id,
            fixture.Tournament.Id, fixture.Match.Id, spectate: true));
        Assert.Throws<L12TournamentVersionConflictException>(() => fixture.Store.TournamentRoomAccess(
            fixture.Organizer.Id, fixture.Tournament.Id, fixture.Match.Id, spectate: false));
    }

    [Fact]
    public void RemovedParticipantSeesScopeFailureBeforeBrokenDeckDetails()
    {
        using var fixture = new Fixture();
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            ParticipantRow(TournamentRow(fixture.Store, fixture.Tournament.Id), fixture.Player.Id).GetType()
                .GetProperty("Removed")!.SetValue(
                    ParticipantRow(TournamentRow(fixture.Store, fixture.Tournament.Id), fixture.Player.Id), true);
            MatchRow(TournamentRow(fixture.Store, fixture.Tournament.Id), fixture.Match.Id).GetType()
                .GetProperty("PlayerADeckHash")!.SetValue(
                    MatchRow(TournamentRow(fixture.Store, fixture.Tournament.Id), fixture.Match.Id),
                    "synthetic-broken-hash");
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });

        Assert.Throws<L12TournamentScopeException>(() => fixture.Store.TournamentRoomAccess(fixture.Player.Id,
            fixture.Tournament.Id, fixture.Match.Id, spectate: false));
        Assert.Throws<L12TournamentVersionConflictException>(() => fixture.Store.TournamentRoomAccess(
            fixture.Organizer.Id, fixture.Tournament.Id, fixture.Match.Id, spectate: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingHistoricalRoomCodeDoesNotPoisonGenerationAndRunningAccessFailsClosed(
        string? roomCode)
    {
        using var fixture = new Fixture();
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            MatchRow(TournamentRow(fixture.Store, fixture.Tournament.Id), fixture.Match.Id).GetType()
                .GetProperty("RoomCode")!.SetValue(
                    MatchRow(TournamentRow(fixture.Store, fixture.Tournament.Id), fixture.Match.Id), roomCode);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });

        Assert.Throws<L12TournamentVersionConflictException>(() => fixture.Store.TournamentRoomAccess(
            fixture.Organizer.Id, fixture.Tournament.Id, fixture.Match.Id, spectate: false));
        Assert.Throws<KeyNotFoundException>(() =>
            fixture.Store.TournamentRoomAssignmentByRoom(fixture.Match.RoomCode));
    }

    [Theory]
    [InlineData("CardIds")]
    [InlineData("MoraleIds")]
    public void EmptyRequiredDeckZoneRejectsAuthorizedHotAccess(string emptyProperty)
    {
        using var fixture = new Fixture();
        var assignment = fixture.Store.TournamentRoomAssignment(fixture.Organizer.Id,
            fixture.Tournament.Id, fixture.Match.Id, spectate: false);
        var player = assignment.PlayerA.AccountId == fixture.Organizer.Id
            ? assignment.PlayerA : assignment.PlayerB;
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            var participant = ParticipantRow(TournamentRow(fixture.Store, fixture.Tournament.Id),
                fixture.Organizer.Id);
            var deck = participant.GetType().GetProperty("Deck")!.GetValue(participant)!;
            deck.GetType().GetProperty("PayloadHash")!.SetValue(deck, string.Empty);
            deck.GetType().GetProperty("CardIds")!.SetValue(deck, player.Deck.CardIds.ToList());
            deck.GetType().GetProperty("MoraleIds")!.SetValue(deck, player.Deck.MoraleIds.ToList());
            deck.GetType().GetProperty("SpecialIds")!.SetValue(deck, player.Deck.SpecialIds.ToList());
            ((IList)deck.GetType().GetProperty(emptyProperty)!.GetValue(deck)!).Clear();
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });

        Assert.Throws<L12TournamentVersionConflictException>(() => fixture.Store.TournamentRoomAccess(
            fixture.Organizer.Id, fixture.Tournament.Id, fixture.Match.Id, spectate: false));
    }

    [Fact]
    public void CaseDistinctHistoricalAlternateArtKeysDoNotPoisonCommittedGeneration()
    {
        using var fixture = new Fixture();
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            var participant = ParticipantRow(TournamentRow(fixture.Store, fixture.Tournament.Id),
                fixture.Organizer.Id);
            var deck = participant.GetType().GetProperty("Deck")!.GetValue(participant)!;
            deck.GetType().GetProperty("AlternateArtSelections")!.SetValue(deck,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["synthetic"] = "first",
                    ["SYNTHETIC"] = "second",
                });
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });

        Assert.True(fixture.Store.TournamentRoomAccess(fixture.Organizer.Id,
            fixture.Tournament.Id, fixture.Match.Id, spectate: false).CanPlay);
        Assert.Throws<ArgumentException>(() => fixture.Store.TournamentRoomAssignment(fixture.Organizer.Id,
            fixture.Tournament.Id, fixture.Match.Id, spectate: false));
    }

    [Fact]
    public void RepeatedMatchesReuseParticipantPayloadReadsWithinOneCommittedPrepare()
    {
        using var fixture = new Fixture();
        var payloadReads = new List<string>();
        fixture.Store.TournamentDeckPayloadReadObserver = payloadReads.Add;
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            var tournament = TournamentRow(fixture.Store, fixture.Tournament.Id);
            var duplicate = CloneRow(MatchRow(tournament, fixture.Match.Id));
            duplicate.GetType().GetProperty("Id")!.SetValue(duplicate, $"{fixture.Match.Id}-repeat");
            duplicate.GetType().GetProperty("RoomCode")!.SetValue(duplicate,
                $"{fixture.Match.RoomCode}-repeat");
            MatchRows(tournament).Add(duplicate);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });

        Assert.Equal(2, payloadReads.Count);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("complete")]
    [InlineData("remove")]
    public void EndCancellationAndParticipantRemovalRevokeRunningAccess(string change)
    {
        using var fixture = new Fixture();
        switch (change)
        {
            case "cancel":
                fixture.Tournament = fixture.Store.CancelTournament(fixture.Organizer, fixture.Tournament.Id,
                    new L12TournamentCancelPayload("synthetic cancel"), fixture.Tournament.Version,
                    Context("cancel"), true);
                break;
            case "complete":
                fixture.Tournament = fixture.Store.ApplyTournamentRuling(fixture.Referee,
                    fixture.Tournament.Id, fixture.Match.Id,
                    new L12TournamentRulingPayload("result", null, "player-a", "synthetic result"),
                    fixture.Tournament.Version, Context("result"), true);
                fixture.Tournament = fixture.Store.CompleteTournament(fixture.Organizer,
                    fixture.Tournament.Id, fixture.Tournament.Version, Context("complete"), true);
                break;
            case "remove":
                fixture.Tournament = fixture.Store.RemoveTournamentParticipant(fixture.Admin,
                    fixture.Tournament.Id,
                    new L12TournamentRemoveParticipantPayload(fixture.Player.Id, false, "synthetic removal"),
                    fixture.Tournament.Version, Context("remove"), true);
                break;
        }
        Assert.Throws<L12TournamentVersionConflictException>(() => fixture.Store.TournamentRoomAccess(
            fixture.Player.Id, fixture.Tournament.Id, fixture.Match.Id, spectate: false));
    }

    [Fact]
    public void CancelledHistoricalRoomKeepsRecoveryBindingAndFrozenMatchSettings()
    {
        using var fixture = new Fixture();
        fixture.Tournament = fixture.Store.ExtendTournamentMatch(fixture.Organizer,
            fixture.Tournament.Id, fixture.Match.Id,
            new L12TournamentTimeExtensionPayload(1, "synthetic extension"), fixture.Tournament.Version,
            Context("extend"), true);
        fixture.Tournament = fixture.Store.PauseTournamentMatch(fixture.Referee,
            fixture.Tournament.Id, fixture.Match.Id,
            new L12TournamentMatchPausePayload(true, "synthetic pause"), fixture.Tournament.Version,
            Context("pause"), true);
        var syntheticDeadline = new DateTimeOffset(2031, 2, 3, 4, 5, 6, TimeSpan.Zero);
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            MatchRow(TournamentRow(fixture.Store, fixture.Tournament.Id), fixture.Match.Id).GetType()
                .GetProperty("Deadline")!.SetValue(
                    MatchRow(TournamentRow(fixture.Store, fixture.Tournament.Id), fixture.Match.Id),
                    syntheticDeadline);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        var active = fixture.Store.TournamentRoomAssignment(fixture.Organizer.Id,
            fixture.Tournament.Id, fixture.Match.Id, spectate: false);
        Assert.True(active.Paused);
        Assert.Equal("synthetic pause", active.PauseReason);
        Assert.Equal(960, active.TimeControl.TotalTimeSeconds);
        Assert.Equal(syntheticDeadline, active.Deadline);

        fixture.Tournament = fixture.Store.CancelTournament(fixture.Organizer, fixture.Tournament.Id,
            new L12TournamentCancelPayload("synthetic cancel"), fixture.Tournament.Version,
            Context("cancel"), true);
        Assert.Throws<L12TournamentVersionConflictException>(() => fixture.Store.TournamentRoomAssignment(
            fixture.Organizer.Id, fixture.Tournament.Id, fixture.Match.Id, spectate: false));
        var restored = fixture.Store.TournamentRoomAssignmentByRoom(fixture.Match.RoomCode);
        Assert.Equal(fixture.Tournament.Id, restored.TournamentId);
        Assert.Equal(fixture.Match.Id, restored.MatchId);
        Assert.Equal(active.RulesHash, restored.RulesHash);
        Assert.Equal(active.Deadline, restored.Deadline);
        Assert.Equal(960, restored.TimeControl.TotalTimeSeconds);
        Assert.False(restored.CanPlay);
        Assert.False(restored.CanSpectate);
        Assert.Contains(fixture.Store.PendingTournamentRoomCommands(fixture.Tournament.Id),
            command => command.Kind == "pause" && command.MatchId == fixture.Match.Id);
        Assert.Contains(fixture.Store.PendingTournamentRoomCommands(fixture.Tournament.Id),
            command => command.Kind == "cancel" && command.MatchId == fixture.Match.Id);
    }

    [Fact]
    public void DuplicateRoomCodesKeepTheFirstTournamentAndRoundBinding()
    {
        using var fixture = new Fixture();
        var secondOrganizer = fixture.Register("ctrorgtwo");
        var secondPlayer = fixture.Register("ctrplytwo");
        var second = fixture.CreateRunningTournament(secondOrganizer, secondPlayer, "second tournament", []);
        var secondMatch = Assert.Single(second.Rounds[0].Matches);
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            MatchRow(TournamentRow(fixture.Store, second.Id), secondMatch.Id).GetType()
                .GetProperty("RoomCode")!.SetValue(MatchRow(TournamentRow(fixture.Store, second.Id),
                    secondMatch.Id), fixture.Match.RoomCode);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });

        var restored = fixture.Store.TournamentRoomAssignmentByRoom(fixture.Match.RoomCode);
        Assert.Equal(fixture.Tournament.Id, restored.TournamentId);
        Assert.Equal(fixture.Match.Id, restored.MatchId);
    }

    private static object Data(L12PlatformStore store)
        => typeof(L12PlatformStore).GetProperty("_data", Private)!.GetValue(store)!;

    private static object TournamentRow(L12PlatformStore store, string tournamentId)
        => ((IEnumerable)Data(store).GetType().GetProperty("Tournaments")!.GetValue(Data(store))!)
            .Cast<object>().First(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == tournamentId);

    private static object MatchRow(object tournament, string matchId)
        => ((IEnumerable)tournament.GetType().GetProperty("Rounds")!.GetValue(tournament)!)
            .Cast<object>().SelectMany(round => ((IEnumerable)round.GetType().GetProperty("Matches")!
                .GetValue(round)!).Cast<object>())
            .First(match => (string)match.GetType().GetProperty("Id")!.GetValue(match)! == matchId);

    private static object ParticipantRow(object tournament, string accountId)
        => ((IEnumerable)tournament.GetType().GetProperty("Participants")!.GetValue(tournament)!)
            .Cast<object>().First(participant =>
                (string)participant.GetType().GetProperty("AccountId")!.GetValue(participant)! == accountId);

    private static IList MatchRows(object tournament)
    {
        var round = ((IEnumerable)tournament.GetType().GetProperty("Rounds")!.GetValue(tournament)!)
            .Cast<object>().First();
        return (IList)round.GetType().GetProperty("Matches")!.GetValue(round)!;
    }

    private static object CloneRow(object source)
    {
        var clone = Activator.CreateInstance(source.GetType(), nonPublic: true)!;
        foreach (var property in source.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.CanRead && property.CanWrite))
            property.SetValue(clone, property.GetValue(source));
        return clone;
    }

    private static IList RefereeIds(object tournament)
        => (IList)tournament.GetType().GetProperty("RefereeAccountIds")!.GetValue(tournament)!;

    private static L12AdminAuditContext Context(string correlationId)
        => new(correlationId, "tournaments.manage", RequestMethod: "TEST",
            RequestPath: "/test/committed-tournaments");

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "l12-committed-tournaments", Guid.NewGuid().ToString("N"));
        internal L12Catalog Catalog { get; }
        internal L12PlatformStore Store { get; }
        internal L12AccountView Admin { get; }
        internal L12AccountView Organizer { get; }
        internal L12AccountView Player { get; }
        internal L12AccountView Referee { get; }
        internal L12TournamentView Tournament { get; set; }
        internal L12TournamentMatchView Match { get; }

        internal Fixture()
        {
            Directory.CreateDirectory(_root);
            Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            Store = new L12PlatformStore(Path.Combine(_root, "platform.json"), Catalog.PresetDecks,
                officialCards: Catalog.Cards);
            Admin = Store.Login("Admin", "L12master").Account!;
            Organizer = Register("ctrhost");
            Player = Register("ctrplayer");
            Referee = Register("ctrreferee");
            Assert.True(Store.SendFriendRequest(Organizer.Id, Referee.Id).Success);
            Assert.True(Store.ResolveFriendRequest(Referee.Id, Organizer.Id, true).Success);
            Tournament = CreateRunningTournament(Organizer, Player, "committed tournament", [Referee.Id]);
            Match = Assert.Single(Tournament.Rounds[0].Matches);
        }

        internal L12TournamentView CreateRunningTournament(L12AccountView organizer,
            L12AccountView player, string name, IReadOnlyList<string> referees)
        {
            var tournament = Store.CreateTournament(organizer,
                new L12TournamentCreatePayload(name, "single", "code", 8,
                    DateTimeOffset.UtcNow.AddHours(1), "S01/S02", "committed projection",
                    "private", "season", string.Empty, 50, 5, referees,
                    TimeControl: new L12RankedTimeControlConfig(900, 90, 120, 30, 45)),
                Context($"create-{name}"), true);
            tournament = SelectDeck(organizer, tournament, update: true);
            tournament = SelectDeck(player, tournament, update: false);
            tournament = Store.StartTournament(organizer, tournament.Id, tournament.Version,
                Context($"start-{name}"), true);
            tournament = Store.CheckInTournament(organizer, tournament.Id, 1,
                new L12TournamentCheckInPayload(organizer.Id, true), tournament.Version,
                Context($"ready-a-{name}"), true);
            tournament = Store.CheckInTournament(player, tournament.Id, 1,
                new L12TournamentCheckInPayload(null, true), tournament.Version,
                Context($"ready-b-{name}"), true);
            return Store.StartTournamentRound(organizer, tournament.Id, 1, tournament.Version,
                Context($"round-{name}"), true);
        }

        internal L12AccountView Register(string username)
        {
            var result = Store.Register(username, "password-123");
            Assert.True(result.Success, result.Message);
            return Assert.IsType<L12AccountView>(result.Account);
        }

        private L12TournamentView SelectDeck(L12AccountView account, L12TournamentView tournament,
            bool update)
        {
            var deck = Store.Decks(account.Id)[0];
            tournament = update
                ? Store.UpdateTournamentRegistration(account, tournament.Id,
                    new L12TournamentRegistrationPayload(), tournament.Version,
                    Context($"registration-{account.Id}"), true)
                : Store.RegisterTournament(account, tournament.Id,
                    new L12TournamentRegistrationPayload(), tournament.Version,
                    Context($"registration-{account.Id}"), true);
            return Store.PreCheckInTournament(account, tournament.Id,
                new L12TournamentPreCheckInPayload(deck.Name, string.Empty), tournament.Version,
                Context($"precheck-{account.Id}"), true);
        }

        public void Dispose()
        {
            Store.StorageFailureInjector = null;
            Store.DeckPayloadExpansionObserver = null;
            Store.TournamentDeckPayloadReadObserver = null;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
