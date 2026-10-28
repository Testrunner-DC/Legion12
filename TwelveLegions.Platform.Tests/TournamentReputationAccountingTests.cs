using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TournamentReputationAccountingTests
{
    [Fact]
    public void NewEffectiveViolationAtSameTournamentCapRestartsRecoveryFromDayNine()
    {
        using var f = new TournamentReputationFoundationTests.Fixture();
        var t = f.RegisteredAndOpened();
        var dayZero = f.Store.TournamentCheckInFacts(f.Admin, t.Id).Start!.EffectiveAt.AddSeconds(1);
        f.Store.RecordTournamentReputationFact(f.Admin,
            new("day-zero-missing", t.Id, f.Player.Id, "no-show", dayZero),
            t.Version, 0, f.Audit, true, dayZero);
        var dayNine = dayZero.AddDays(9);
        var second = f.Store.RecordTournamentReputationFact(f.Admin,
            new("distinct-day-nine-missing", t.Id, f.Player.Id, "no-show", dayNine),
            t.Version, 1, f.Audit, true, dayNine);
        Assert.Equal(85, second.Projection.Score); // First full recovery period, no second deduction.
        Assert.Equal(dayNine, second.Projection.LastViolationAt);
        Assert.Equal(dayZero.AddDays(16), second.Projection.NextRecoveryAt);
        Assert.Equal(85, f.Store.TournamentReputationProjection(f.Player, f.Player.Id, dayZero.AddDays(16).AddTicks(-1)).Score);
        Assert.Equal(90, f.Store.TournamentReputationProjection(f.Player, f.Player.Id, dayZero.AddDays(16)).Score);
    }

    [Fact]
    public void PreviewReplayUpgradeAndSevenDayRecoveryKeepOneTournamentCap()
    {
        using var f = new TournamentReputationFoundationTests.Fixture();
        var t = f.RegisteredAndOpened();
        var at = f.Store.TournamentCheckInFacts(f.Admin, t.Id).Start!.EffectiveAt.AddSeconds(1);
        var fact = new L12TournamentReputationFact("synthetic-withdrawal", t.Id, f.Player.Id, "withdrawal", at);
        var version = f.Store.Version;
        var mirror = File.ReadAllText(f.Path);
        var preview = f.Store.RecordTournamentReputationFact(f.Admin, fact, t.Version, 0, f.Audit, false, at);
        Assert.Equal(90, preview.Projection.Score); Assert.False(preview.Applied);
        Assert.Equal(version, f.Store.Version); Assert.Equal(mirror, File.ReadAllText(f.Path));
        Assert.Equal(100, f.Store.TournamentReputationProjection(f.Player, f.Player.Id, at).Score);
        var applied = f.Store.RecordTournamentReputationFact(f.Admin, fact, t.Version, 0, f.Audit, true, at);
        Assert.True(applied.Applied); Assert.Equal(90, applied.Projection.Score);
        version = f.Store.Version;
        Assert.True(f.Store.RecordTournamentReputationFact(f.Admin, fact, t.Version, 0, f.Audit, true, at).AlreadyApplied);
        Assert.Equal(version, f.Store.Version);
        var upgraded = f.Store.RecordTournamentReputationFact(f.Admin,
            fact with { Id = "synthetic-no-show", Kind = "no-show" }, t.Version, 1, f.Audit, true, at);
        Assert.Equal(80, upgraded.Projection.Score);
        var repeatedSeverity = f.Store.RecordTournamentReputationFact(f.Admin,
            fact with { Id = "synthetic-same-severity", Kind = "no-show" },
            t.Version, 2, f.Audit, true, at.AddDays(1));
        Assert.Equal(80, repeatedSeverity.Projection.Score);
        Assert.Equal(at, repeatedSeverity.Projection.LastViolationAt);
        Assert.Equal(80, f.Store.TournamentReputationProjection(f.Player, f.Player.Id, at.AddDays(7).AddTicks(-1)).Score);
        Assert.Equal(85, f.Store.TournamentReputationProjection(f.Player, f.Player.Id, at.AddDays(7)).Score);
        Assert.Equal(100, f.Store.TournamentReputationProjection(f.Player, f.Player.Id, at.AddDays(35)).Score);
        Assert.Equal(3, f.Store.TournamentReputationProjection(f.Player, f.Player.Id, at.AddDays(35)).Revision);
        var restored = new L12PlatformStore(f.Path);
        Assert.Equal(85, restored.TournamentReputationProjection(f.Player, f.Player.Id, at.AddDays(7)).Score);
    }

    [Fact]
    public void RevocationReprojectsEffectiveFactsInsteadOfAddingTheOldDeduction()
    {
        using var f = new TournamentReputationFoundationTests.Fixture();
        var t = f.RegisteredAndOpened();
        var at = f.Store.TournamentCheckInFacts(f.Admin, t.Id).Start!.EffectiveAt.AddSeconds(1);
        var withdrawal = new L12TournamentReputationFact("withdrawal", t.Id, f.Player.Id, "withdrawal", at);
        f.Store.RecordTournamentReputationFact(f.Admin, withdrawal, t.Version, 0, f.Audit, true, at);
        f.Store.RecordTournamentReputationFact(f.Admin, withdrawal with { Id = "missing", Kind = "no-show" },
            t.Version, 1, f.Audit, true, at);
        var revoked = f.Store.RecordTournamentReputationFact(f.Admin,
            new("revoke-missing", t.Id, f.Player.Id, "revoke", at.AddDays(28), "missing"),
            t.Version, 2, f.Audit, true, at.AddDays(28));
        Assert.Equal(100, revoked.Projection.Score); // 90 plus four recovery periods, capped; not 110.
        Assert.True(f.Store.RecordTournamentReputationFact(f.Admin,
            new("repeat-revoke", t.Id, f.Player.Id, "revoke", at.AddDays(28), "missing"),
            t.Version, 3, f.Audit, true, at.AddDays(28)).AlreadyApplied);
        Assert.Equal(3, f.Store.TournamentReputationProjection(f.Player, f.Player.Id, at.AddDays(28)).Revision);
    }

    [Fact]
    public void UnpromotedWaitlistAndPreOpeningFactsCannotProduceAPenalty()
    {
        using var f = new TournamentReputationFoundationTests.Fixture();
        var t = f.Create(DateTimeOffset.UtcNow.AddHours(2));
        t = f.Store.RegisterTournament(f.Player, t.Id, new(), t.Version, f.Audit, true);
        var at = DateTimeOffset.UtcNow;
        var beforeOpen = f.Store.RecordTournamentReputationFact(f.Admin,
            new("before-open", t.Id, f.Player.Id, "withdrawal", at), t.Version, 0, f.Audit, true, at);
        Assert.Equal("before-check-in", beforeOpen.Exemption); Assert.Equal(100, beforeOpen.Projection.Score);
        t = f.Store.RegisterTournament(f.Admin, t.Id, new(), t.Version, f.Audit, true);
        var waiting = f.Store.Register("rpwaiting", "password-123").Account!;
        t = f.Store.RegisterTournament(waiting, t.Id, new(), t.Version, f.Audit, true);
        t = f.Store.SetTournamentPhase(f.Admin, t.Id, new("pre-check-in", "synthetic open"), t.Version, f.Audit, true);
        at = DateTimeOffset.UtcNow;
        var result = f.Store.RecordTournamentReputationFact(f.Admin,
            new("waitlist-no-show", t.Id, waiting.Id, "no-show", at), t.Version, 0, f.Audit, true, at);
        Assert.Equal("unpromoted-waitlist", result.Exemption); Assert.Equal(100, result.Projection.Score);
        Assert.Throws<L12TournamentScopeException>(() => f.Store.TournamentReputationProjection(f.Player, waiting.Id, at));
        t = f.Store.DropTournament(f.Player, t.Id, t.Version, f.Audit, true);
        Assert.False(t.Participants.Single(item => item.AccountId == waiting.Id).Waitlisted);
        Assert.Equal(100, new L12PlatformStore(f.Path).TournamentReputationProjection(waiting, waiting.Id, at).Score);
        Assert.Equal(1, f.Store.TournamentReputationProjection(waiting, waiting.Id, at).Revision);
        Assert.DoesNotContain("TournamentReputation", System.Text.Json.JsonSerializer.Serialize(f.Store.Account(f.Player.Id)));
    }

    [Theory]
    [InlineData("before-commit")]
    [InlineData("before-audit-append")]
    public void SaveFailureRollsBackAccountEventProjectionAndAudit(string stage)
    {
        using var f = new TournamentReputationFoundationTests.Fixture();
        var t = f.RegisteredAndOpened();
        var at = DateTimeOffset.UtcNow;
        var version = f.Store.Version;
        var mirror = File.ReadAllText(f.Path);
        f.Store.StorageFailureInjector = actual => { if (actual == stage) throw new IOException("synthetic reputation save failure"); };
        Assert.Throws<L12PlatformStorageUnavailableException>(() => f.Store.RecordTournamentReputationFact(f.Admin,
            new("failed-event", t.Id, f.Player.Id, "no-show", at), t.Version, 0, f.Audit, true, at));
        f.Store.StorageFailureInjector = null;
        Assert.Equal(version, f.Store.Version); Assert.Equal(mirror, File.ReadAllText(f.Path));
        Assert.Equal(0, f.Store.TournamentReputationProjection(f.Player, f.Player.Id, at).Revision);
        var restored = new L12PlatformStore(f.Path);
        Assert.Equal(100, restored.TournamentReputationProjection(f.Player, f.Player.Id, at).Score);
        Assert.True(f.Store.RecordTournamentReputationFact(f.Admin,
            new("failed-event", t.Id, f.Player.Id, "no-show", at), t.Version, 0, f.Audit, true, at).Applied);
    }

    [Fact]
    public void CrossStoreCasFailureCannotPublishAStaleEventOrDiscardTheOtherStore()
    {
        using var f = new TournamentReputationFoundationTests.Fixture();
        var t = f.RegisteredAndOpened();
        var other = new L12PlatformStore(f.Path);
        var newer = other.SetTournamentPhase(f.Admin, t.Id, new("registration-closed", "synthetic concurrent"), t.Version, f.Audit, true);
        var at = DateTimeOffset.UtcNow;
        Assert.Throws<L12PlatformStorageConflictException>(() => f.Store.RecordTournamentReputationFact(f.Admin,
            new("stale-event", t.Id, f.Player.Id, "no-show", at), t.Version, 0, f.Audit, true, at));
        Assert.Equal(newer.Version, f.Store.Tournament(f.Admin, t.Id)!.Version);
        Assert.Equal(0, f.Store.TournamentReputationProjection(f.Player, f.Player.Id, at).Revision);
        Assert.Equal(0, new L12PlatformStore(f.Path).TournamentReputationProjection(f.Player, f.Player.Id, at).Revision);
    }

    [Fact]
    public void RestoreRejectsAReputationProjectionThatDisagreesWithItsEvents()
    {
        using var f = new TournamentReputationFoundationTests.Fixture();
        var t = f.RegisteredAndOpened();
        var at = DateTimeOffset.UtcNow;
        f.Store.RecordTournamentReputationFact(f.Admin, new("valid", t.Id, f.Player.Id, "no-show", at),
            t.Version, 0, f.Audit, true, at);
        using var connection = new SqliteConnection($"Data Source={f.Store.TransactionalStoragePath};Pooling=False");
        connection.Open();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT snapshot_json FROM platform_state WHERE singleton_id=1";
        var json = JsonNode.Parse((string)read.ExecuteScalar()!)!;
        var account = json["Accounts"]!.AsArray().Single(item => item!["Id"]!.GetValue<string>() == f.Player.Id)!;
        account["TournamentReputation"]!["Score"] = 100;
        var body = json.ToJsonString();
        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE platform_state SET snapshot_json=$json,snapshot_sha256=$hash WHERE singleton_id=1";
        update.Parameters.AddWithValue("$json", body);
        update.Parameters.AddWithValue("$hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant());
        update.ExecuteNonQuery();
        Assert.Throws<L12PlatformStorageUnavailableException>(() => new L12PlatformStore(f.Path));
    }
}
