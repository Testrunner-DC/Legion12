using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TournamentExplicitRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreatorRemainsStaffUntilTheyExplicitlyRegister(bool administratorCreates)
    {
        using var f = new Fixture();
        var actor = administratorCreates ? f.Admin : f.Host;
        var preview = f.Store.CreateTournament(actor, Payload(), Context("preview"), false);
        Assert.Empty(preview.Participants);
        Assert.Equal(0, preview.Counts.Registered);
        Assert.Null(f.Store.Tournament(actor, preview.Id));
        var tournament = f.Store.CreateTournament(actor, Payload(), Context("create"), true);
        Assert.Equal(actor.Id, tournament.OrganizerAccountId);
        Assert.Empty(tournament.Participants);
        Assert.Equal(0, tournament.Counts.PendingCheckIn);
        Assert.Equal(0, tournament.Counts.CheckedIn);
        Assert.Equal(0, f.Store.PublicTournamentByCode(tournament.Code)!.Counts.Registered);
        var before = tournament.Version;
        var proposed = f.Store.RegisterTournament(actor, tournament.Id, new(), before,
            Context("register-preview"), false);
        Assert.Single(proposed.Participants);
        Assert.Empty(f.Store.Tournament(actor, tournament.Id)!.Participants);
        tournament = f.Store.RegisterTournament(actor, tournament.Id, new(), before,
            Context("register"), true);
        Assert.Equal(actor.Id, Assert.Single(tournament.Participants).AccountId);
        Assert.Equal(1, tournament.Counts.Registered);
        Assert.Equal(1, tournament.Counts.PendingCheckIn);
        Assert.Equal(0, tournament.Counts.CheckedIn);
        Assert.Equal(0, tournament.Counts.Active);
        Assert.Equal(1, f.Store.PublicTournamentByCode(tournament.Code)!.Counts.Registered);
        Assert.Equal(tournament.Counts, Assert.Single(f.Store.TournamentSummaries(actor,
            "host", null, null).Items).Counts);
    }

    [Fact]
    public void WithdrawnRegistrationRemainsRecordedButDoesNotOccupyTheRegisteredCount()
    {
        using var f = new Fixture();
        var tournament = f.Store.CreateTournament(f.Host, Payload(), Context("create"), true);
        // This fixture tests withdrawal independently of the creator policy above.
        if (!tournament.Participants.Any(row => row.AccountId == f.Host.Id))
            tournament = f.Store.RegisterTournament(f.Host, tournament.Id, new(), tournament.Version,
                Context("explicit-host"), true);
        tournament = f.Store.DropTournament(f.Host, tournament.Id, tournament.Version,
            Context("drop"), true);
        Assert.True(Assert.Single(tournament.Participants).Dropped);
        Assert.Equal(1, tournament.Counts.Dropped);
        Assert.Equal(0, tournament.Counts.Registered);
        Assert.Equal(0, tournament.Counts.PendingCheckIn);
        Assert.Equal(0, tournament.Counts.CheckedIn);
        Assert.Equal(0, f.Store.PublicTournamentByCode(tournament.Code)!.Counts.Registered);
        var reloaded = new L12PlatformStore(f.Path);
        var persisted = reloaded.Tournament(f.Host, tournament.Id)!;
        Assert.True(Assert.Single(persisted.Participants).Dropped);
        Assert.Equal(0, persisted.Counts.Registered);
    }

    [Fact]
    public void LegacyImportPreservesOnlyItsExplicitRosterWithoutAddingTheImportingAdministrator()
    {
        using var f = new Fixture();
        var input = new L12LegacyTournamentInput(
            Id: "synthetic-explicit-roster", Code: null, Name: "合成旧赛事", Organizer: null,
            Referees: [], Status: "registration", Format: "single", Visibility: "public",
            MaxPlayers: 16, StartAt: null, Ruleset: "现行规则", Description: "纯合成",
            DeckVisibility: "after", DisasterMode: "season", BanList: "", RoundMinutes: 50,
            CheckInMinutes: 5, Participants: [new(f.Host.Username)], Rounds: [],
            CreatedAt: null, UpdatedAt: null, CompletedAt: null);
        var preview = f.Store.ImportLegacyTournaments(f.Admin, new([input]), Context("import-preview"), false);
        Assert.Equal(f.Host.Id, Assert.Single(Assert.Single(preview.Tournaments).Participants).AccountId);
        var applied = f.Store.ImportLegacyTournaments(f.Admin, new([input], preview.PreviewHash),
            Context("import"), true);
        var tournament = Assert.Single(applied.Tournaments);
        Assert.Equal(f.Admin.Id, tournament.OrganizerAccountId);
        Assert.Equal(f.Host.Id, Assert.Single(tournament.Participants).AccountId);
        Assert.DoesNotContain(tournament.Participants, row => row.AccountId == f.Admin.Id);
        Assert.Equal(1, tournament.Counts.Registered);
    }

    private static L12TournamentCreatePayload Payload()
        => new("合成显式报名", "single", "public", 16, DateTimeOffset.UtcNow.AddHours(1),
            "现行规则", "synthetic", "after", "season", "", 50, 5,
            RegistrationVisibility: "public");

    private static L12AdminAuditContext Context(string id)
        => new("synthetic-explicit-" + id, "tournaments.manage", RequestMethod: "TEST",
            RequestPath: "/test/tournaments");

    private sealed class Fixture : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "l12-explicit-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(root, "platform.json");
        public L12PlatformStore Store { get; }
        public L12AccountView Admin { get; }
        public L12AccountView Host { get; }
        public Fixture()
        {
            Directory.CreateDirectory(root);
            Store = new(Path);
            Admin = Store.Login("Admin", "L12master").Account!;
            var registration = Store.Register("expHost", "password-123");
            Assert.True(registration.Success, registration.Message);
            Host = registration.Account!;
        }
        public void Dispose() => Directory.Delete(root, true);
    }
}
