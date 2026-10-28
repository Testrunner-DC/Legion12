using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TournamentReputationProductionStartTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticNoChangeNeverSerializesWholePlatform(bool scheduledFutureTournament)
    {
        using var f = new ReputationProductionFixture();
        var now = DateTimeOffset.UtcNow;
        if (scheduledFutureTournament)
            f.Store.CreateTournament(f.Admin, new("尚未到期自动赛事", "single", "public", 2, now.AddHours(2),
                "现行规则", "", "after", "none", "", 50, 5, OperationMode: "automated"), f.Audit, true);
        else f.Create(); // No automated candidates at all.
        var version = f.Store.Version;
        var mirror = File.ReadAllText(f.MirrorPath);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var before = SnapshotSerializationMetric();
            Assert.Equal(0, f.Store.AdvanceAutomaticTournaments(now).Changed);
            var after = SnapshotSerializationMetric();
            // The existing metrics window rotates every minute. Retry only a
            // crossed telemetry epoch, never a measured nonzero regression.
            if (before.StartedAt != after.StartedAt) continue;
            Assert.Equal(0, after.Count - before.Count);
            Assert.Equal(version, f.Store.Version);
            Assert.Equal(mirror, File.ReadAllText(f.MirrorPath));
            return;
        }
        Assert.Fail("metric window changed during all bounded no-op measurements");
    }

    private static PerformanceMetricSummary SnapshotSerializationMetric()
    {
        Assert.True(L12PerformanceMetrics.TrySnapshot(out var batch));
        return Assert.Single(batch!.Series.Where(item => item.Stage == "duration." + L12PlatformOperationMetrics.SnapshotSerializeStage
            && item.Unit == "ms"));
    }

    [Fact]
    public void ActualStartAtomicallySubstitutesLockedWaitingPlayerAndChargesOriginalMissingSeat()
    {
        using var f = new ReputationProductionFixture();
        var t = f.Create();
        t = f.Register(t, f.Players[0]);
        t = f.Register(t, f.Players[1]);
        t = f.Register(t, f.Players[2]);
        t = f.Lock(t, f.Players[0]);
        t = f.Lock(t, f.Players[2]);
        var waitingHash = t.Participants.Single(item => item.AccountId == f.Players[2].Id).Deck!.Hash;
        var before = f.Store.Version;
        var check = f.Store.TournamentStartCheck(f.Host, t.Id);
        Assert.True(check.CanStart, string.Join(";", check.Blockers));
        Assert.Equal(before, f.Store.Version);
        t = f.Store.StartTournament(f.Host, t.Id, t.Version, f.Audit, true);
        Assert.Equal(before + 1, f.Store.Version);
        Assert.True(t.Participants.Single(item => item.AccountId == f.Players[1].Id).Dropped);
        var promoted = t.Participants.Single(item => item.AccountId == f.Players[2].Id);
        Assert.False(promoted.Waitlisted); Assert.Equal(waitingHash, promoted.Deck!.Hash);
        Assert.Equal(80, f.Score(f.Players[1])); Assert.Equal(100, f.Score(f.Players[2]));
        var match = Assert.Single(Assert.Single(t.Rounds).Matches);
        Assert.Contains(f.Players[2].Id, new[] { match.PlayerAAccountId, match.PlayerBAccountId });
        Assert.DoesNotContain(f.Players[1].Id, new[] { match.PlayerAAccountId, match.PlayerBAccountId });
    }

    [Theory]
    [InlineData("before-commit")]
    [InlineData("before-audit-append")]
    public void StartPreviewAndFailedCommitCannotPublishSeatsPenaltyOrPairings(string failureStage)
    {
        using var f = new ReputationProductionFixture();
        var t = Roster(f);
        var version = f.Store.Version;
        var mirror = File.ReadAllText(f.MirrorPath);
        var preview = f.Store.StartTournament(f.Host, t.Id, t.Version, f.Audit, false);
        Assert.Equal("running", preview.Status);
        Assert.Equal(version, f.Store.Version); Assert.Equal(mirror, File.ReadAllText(f.MirrorPath));
        Assert.Equal(100, f.Score(f.Players[1]));
        Assert.True(f.Store.Tournament(f.Host, t.Id)!.Participants.Single(item => item.AccountId == f.Players[2].Id).Waitlisted);
        f.Store.StorageFailureInjector = stage => { if (stage == failureStage) throw new IOException("synthetic atomic start failure"); };
        Assert.Throws<L12PlatformStorageUnavailableException>(() => f.Store.StartTournament(f.Host, t.Id, t.Version, f.Audit, true));
        f.Store.StorageFailureInjector = null;
        Assert.Equal(version, f.Store.Version); Assert.Equal(mirror, File.ReadAllText(f.MirrorPath));
        var restored = f.Restart().Tournament(f.Host, t.Id)!;
        Assert.Equal("registration", restored.Status); Assert.Empty(restored.Rounds);
        Assert.False(restored.Participants.Single(item => item.AccountId == f.Players[1].Id).Dropped);
        Assert.True(restored.Participants.Single(item => item.AccountId == f.Players[2].Id).Waitlisted);
        Assert.Equal(100, f.Score(f.Players[1]));
        t = f.Store.StartTournament(f.Host, t.Id, t.Version, f.Audit, true);
        Assert.Equal(80, f.Score(f.Players[1]));
        Assert.Equal(version + 1, f.Store.Version);
    }

    [Fact]
    public void StartCasFailureRefreshesWithoutPublishingStalePenaltyOrSeatPlan()
    {
        using var f = new ReputationProductionFixture();
        var t = Roster(f);
        var other = f.Restart();
        var newer = other.SetTournamentAnnouncement(f.Host, t.Id, "synthetic newer announcement", t.Version, f.Audit, true);
        Assert.Throws<L12PlatformStorageConflictException>(() => f.Store.StartTournament(f.Host, t.Id, t.Version, f.Audit, true));
        var actual = f.Store.Tournament(f.Host, t.Id)!;
        Assert.Equal(newer.Version, actual.Version); Assert.Equal("registration", actual.Status); Assert.Empty(actual.Rounds);
        Assert.True(actual.Participants.Single(item => item.AccountId == f.Players[2].Id).Waitlisted);
        Assert.Equal(100, f.Score(f.Players[1]));
    }

    [Fact]
    public void StartSkipsAnEarlierUnLockedCandidateAndPreservesOriginalSourceSnapshot()
    {
        using var f = new ReputationProductionFixture();
        var t = f.Create();
        foreach (var player in f.Players) t = f.Register(t, player);
        t = f.Lock(t, f.Players[0]); t = f.Lock(t, f.Players[3]);
        var check = f.Store.TournamentStartCheck(f.Host, t.Id);
        Assert.Equal(1, check.EligiblePlayers); Assert.Equal(2, check.ProjectedEligiblePlayers);
        Assert.Equal(1, check.PromotedWaitlistedPlayers);
        t = f.Store.StartTournament(f.Host, t.Id, t.Version, f.Audit, true);
        Assert.True(t.Participants.Single(item => item.AccountId == f.Players[2].Id).Waitlisted);
        Assert.False(t.Participants.Single(item => item.AccountId == f.Players[3].Id).Waitlisted);
        Assert.Equal(100, f.Score(f.Players[2])); Assert.Equal(100, f.Score(f.Players[3]));
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(f.MirrorPath))!;
        var account = json["Accounts"]!.AsArray().Single(item => item!["Id"]!.GetValue<string>() == f.Players[1].Id)!;
        var entry = Assert.Single(account["TournamentReputation"]!["Events"]!.AsArray())!;
        Assert.True(entry["FormalSeat"]!.GetValue<bool>());
        Assert.Equal("registration-no-show", entry["Source"]!["Kind"]!.GetValue<string>());
        Assert.Contains(t.Id, entry["Source"]!["RootId"]!.GetValue<string>());
        Assert.Equal(80, f.Restart().TournamentReputationProjection(f.Players[1], f.Players[1].Id, DateTimeOffset.UtcNow).Score);
    }

    private static L12TournamentView Roster(ReputationProductionFixture f)
    {
        var t = f.Create();
        foreach (var player in f.Players.Take(3)) t = f.Register(t, player);
        t = f.Lock(t, f.Players[0]); return f.Lock(t, f.Players[2]);
    }
}
