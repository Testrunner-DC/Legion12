using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TournamentReputationProductionFactsTests
{
    [Fact]
    public void ActualWithdrawalChargesThePlayerWithoutAccountAdminPermission()
    {
        using var f = new ReputationProductionFixture();
        var t = f.Create();
        t = f.Register(t, f.Players[0]);
        t = f.Open(t);
        var version = f.Store.Version;
        t = f.Store.DropTournament(f.Players[0], t.Id, t.Version, f.Audit, true);
        Assert.Equal(90, f.Score(f.Players[0]));
        Assert.Equal(version + 1, f.Store.Version);
        var repeated = f.Store.DropTournament(f.Players[0], t.Id, t.Version, f.Audit, true);
        Assert.Equal(t.Version, repeated.Version);
        Assert.Equal(90, f.Score(f.Players[0]));
        Assert.Throws<L12TournamentScopeException>(() => f.Store.UpdateTournamentRegistration(f.Players[0],
            t.Id, new(), t.Version, f.Audit, true));
    }

    [Fact]
    public void ActualLowReputationBlocksNewRegistrationButProtectsExistingSeat()
    {
        using var f = new ReputationProductionFixture();
        var protectedTournament = f.Register(f.Create(), f.Players[0]);
        for (var index = 0; index < 3; index++)
        {
            var prior = f.Open(f.Register(f.Create(), f.Players[0]));
            var now = DateTimeOffset.UtcNow;
            var revision = f.Store.TournamentReputationProjection(f.Admin, f.Players[0].Id, now).Revision;
            f.Store.RecordTournamentReputationFact(f.Admin,
                new($"synthetic-qualification-{index}", prior.Id, f.Players[0].Id, "no-show", now),
                prior.Version, revision, f.Audit, true, now);
        }
        var fresh = f.Create();
        Assert.Equal(40, f.Score(f.Players[0]));
        Assert.Throws<L12TournamentScopeException>(() => f.Register(fresh, f.Players[0]));
        protectedTournament = f.Lock(protectedTournament, f.Players[0]);
        Assert.NotNull(protectedTournament.Participants.Single(item => item.AccountId == f.Players[0].Id).Deck?.LockedAt);
    }

    [Fact]
    public void ActualManualGraceNoShowPenalizesOnlyTheAbsentSeat()
    {
        using var f = new ReputationProductionFixture();
        var t = f.Create(lateGrace: 0);
        foreach (var player in f.Players.Take(2)) t = f.Lock(f.Register(t, player), player);
        t = f.Store.StartTournament(f.Host, t.Id, t.Version, f.Audit, true);
        var match = Assert.Single(Assert.Single(t.Rounds).Matches);
        var present = f.Players.Single(item => item.Id == match.PlayerAAccountId);
        var absent = f.Players.Single(item => item.Id == match.PlayerBAccountId);
        t = f.Store.CheckInTournament(present, t.Id, 1, new(null, true), t.Version, f.Audit, true);
        t = f.Store.StartTournamentRound(f.Host, t.Id, 1, t.Version, f.Audit, true);
        t = f.Store.ApplyTournamentRuling(f.Host, t.Id, match.Id,
            new("no-show", absent.Id, "no-show-b", "synthetic existing grace ruling"), t.Version, f.Audit, true);
        Assert.Equal(80, f.Score(absent));
        Assert.Equal(100, f.Score(present));
    }

    [Fact]
    public void PostponedWithdrawalIsExemptAndCancellationReprojectsEarlierWithdrawal()
    {
        using var f = new ReputationProductionFixture();
        var t = f.Open(f.Register(f.Create(), f.Players[0]));
        var opening = f.Store.TournamentCheckInFacts(f.Host, t.Id).Start;
        t = f.Store.PostponeTournament(f.Host, t.Id, new(DateTimeOffset.UtcNow.AddHours(4), "synthetic organizer postponement"),
            t.Version, f.Audit, true);
        t = f.Store.DropTournament(f.Players[0], t.Id, t.Version, f.Audit, true);
        Assert.Equal(100, f.Score(f.Players[0]));
        Assert.Equal(opening, f.Store.TournamentCheckInFacts(f.Host, t.Id).Start);
        var other = f.Open(f.Register(f.Create(), f.Players[1]));
        other = f.Store.DropTournament(f.Players[1], other.Id, other.Version, f.Audit, true);
        Assert.Equal(90, f.Score(f.Players[1]));
        other = f.Store.CancelTournament(f.Host, other.Id, new("synthetic organizer cancellation"), other.Version, f.Audit, true);
        Assert.Equal(100, f.Score(f.Players[1]));
        Assert.Equal(100, f.Restart().TournamentReputationProjection(f.Players[1], f.Players[1].Id, DateTimeOffset.UtcNow).Score);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticGraceUsesAuthoritativeAbsenceWithoutInventingAWinner(bool bothAbsent)
    {
        using var f = new ReputationProductionFixture();
        var start = DateTimeOffset.UtcNow.AddHours(2);
        var t = f.Store.CreateTournament(f.Admin, new("自动信誉合成", "single", "public", 2, start,
            "现行规则", "", "after", "none", "", 50, 5, OperationMode: "automated",
            IntermissionMinutes: 0, LateGraceMinutes: 0), f.Audit, true);
        foreach (var player in f.Players.Take(2)) t = f.Lock(f.Register(t, player), player);
        Assert.Equal(1, f.Store.AdvanceAutomaticTournaments(start.AddMinutes(-5)).Changed);
        Assert.Equal(1, f.Store.AdvanceAutomaticTournaments(start).Changed);
        t = f.Store.Tournament(f.Admin, t.Id)!;
        var match = Assert.Single(Assert.Single(t.Rounds).Matches);
        var present = f.Players.Single(item => item.Id == match.PlayerAAccountId);
        var absent = f.Players.Single(item => item.Id == match.PlayerBAccountId);
        if (!bothAbsent) t = f.Store.CheckInTournament(present, t.Id, 1, new(null, true), t.Version, f.Audit, true);
        Assert.Equal(1, f.Store.AdvanceAutomaticTournaments(start).Changed);
        var version = f.Store.Version;
        Assert.Equal(1, f.Store.AdvanceAutomaticTournaments(start).Changed);
        Assert.Equal(version + 1, f.Store.Version);
        Assert.Equal(80, f.Store.TournamentReputationProjection(absent, absent.Id, start).Score);
        Assert.Equal(bothAbsent ? 80 : 100, f.Store.TournamentReputationProjection(present, present.Id, start).Score);
        t = f.Store.Tournament(f.Admin, t.Id)!;
        match = Assert.Single(Assert.Single(t.Rounds).Matches);
        if (bothAbsent) { Assert.Null(match.Result); Assert.Equal("attention-required", t.AutomationStatus); }
        else
        {
            var beforeExplicitDrop = t.Version;
            t = f.Store.DropTournament(absent, t.Id, t.Version, f.Audit, true);
            Assert.Equal(beforeExplicitDrop + 1, t.Version);
            Assert.Equal(1, f.Store.TournamentReputationProjection(absent, absent.Id, start).Revision);
            Assert.Throws<L12TournamentVersionConflictException>(() => f.Store.RequestTournamentRematch(f.Admin,
                t.Id, match.Id, new("synthetic stale rematch"), beforeExplicitDrop, f.Audit, true));
            Assert.Throws<L12TournamentVersionConflictException>(() => f.Store.RequestTournamentRematch(f.Admin,
                t.Id, match.Id, new("synthetic explicit withdrawal cannot be undone"), t.Version, f.Audit, true));
        }
        f.Store.AdvanceAutomaticTournaments(start.AddSeconds(1));
        Assert.Equal(1, f.Store.TournamentReputationProjection(absent, absent.Id, start.AddSeconds(1)).Revision);
        Assert.Equal(80, f.Restart().TournamentReputationProjection(absent, absent.Id, start.AddSeconds(1)).Score);
    }
}

internal sealed class ReputationProductionFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "l12-rpp-" + Guid.NewGuid().ToString("N"));
    public string MirrorPath => Path.Combine(Root, "platform.json");
    public L12PlatformStore Store { get; }
    public L12AccountView Host { get; }
    public L12AccountView Admin { get; }
    public L12AccountView[] Players { get; }
    public L12Catalog Catalog { get; }
    public L12AdminAuditContext Audit { get; } = new("reputation-production-test", "tournaments.manage",
        RequestMethod: "TEST", RequestPath: "/test/reputation-production");
    public ReputationProductionFixture()
    {
        Directory.CreateDirectory(Root);
        Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        Store = new(MirrorPath, Catalog.PresetDecks, officialCards: Catalog.Cards);
        Host = Store.Register("rpphost", "password-123").Account!;
        var adminResult = Store.Register("rppstaff", "password-123");
        Assert.True(adminResult.Success, adminResult.Message);
        var admin = adminResult.Account!;
        Assert.True(Store.SetRole(admin.Id, "admin"));
        Admin = Store.Accounts().Single(item => item.Id == admin.Id);
        Players = Enumerable.Range(0, 4).Select(index => Store.Register("rppplayer" + index, "password-123").Account!).ToArray();
    }
    public L12TournamentView Create(int maxPlayers = 2, int lateGrace = 5)
        => Store.CreateTournament(Host, new("信誉生产合成", "single", "public", maxPlayers,
            DateTimeOffset.UtcNow.AddHours(2), "现行规则", "", "after", "none", "", 50, 5,
            LateGraceMinutes: lateGrace), Audit, true);
    public L12TournamentView Register(L12TournamentView t, L12AccountView player)
        => Store.RegisterTournament(player, t.Id, new(), t.Version, Audit, true);
    public L12TournamentView Open(L12TournamentView t)
        => Store.SetTournamentPhase(Host, t.Id, new("pre-check-in", "synthetic opening"), t.Version, Audit, true);
    public L12TournamentView Lock(L12TournamentView t, L12AccountView player)
    {
        var deck = Store.Decks(player.Id)[0];
        return Store.PreCheckInTournament(player, t.Id, new(deck.Name, "", deck.Id), t.Version, Audit, true);
    }
    public int Score(L12AccountView player) => Store.TournamentReputationProjection(player, player.Id, DateTimeOffset.UtcNow).Score;
    public L12PlatformStore Restart() => new(MirrorPath, Catalog.PresetDecks, officialCards: Catalog.Cards);
    public void Dispose() => Directory.Delete(Root, true);
}
