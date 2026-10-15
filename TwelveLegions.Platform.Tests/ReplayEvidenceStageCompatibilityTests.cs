using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class ReplayEvidenceStageCompatibilityTests
{
    [Theory]
    [InlineData("confirmed")]
    [InlineData("in-progress")]
    [InlineData("decision")]
    [InlineData("implementation")]
    [InlineData("retest")]
    [InlineData("deploy")]
    public void ActiveWorkflowStagesProtectOnlyExactTopLevelBindings(string status)
    {
        using var fixture = new RankedIdentityFixture();
        var now = DateTimeOffset.UtcNow;
        var matchId = $"Match-{status}";
        var roomCode = $"ROOM-{status}";
        var report = AddBoundBug(fixture, now, roomCode, matchId);

        var untriaged = fixture.Store.ReplayEvidenceAt(now);
        Assert.Empty(untriaged.MatchIds);
        Assert.Empty(untriaged.RoomCodes);

        var updated = fixture.Store.UpdateBug(fixture.Admin, report.Id, status,
            null, null, null);
        Assert.NotNull(updated);
        Assert.Equal(status, updated.Status);

        var evidence = fixture.Store.ReplayEvidenceAt(now);
        Assert.Equal([matchId], evidence.MatchIds);
        Assert.Equal([roomCode], evidence.RoomCodes);
        Assert.DoesNotContain(matchId.ToLowerInvariant(), evidence.MatchIds);
        Assert.DoesNotContain(roomCode.ToLowerInvariant(), evidence.RoomCodes);
        Assert.DoesNotContain("diagnostic-match", evidence.MatchIds);
        Assert.DoesNotContain("DIAGNOSTIC-ROOM", evidence.RoomCodes);
        Assert.DoesNotContain("client-match", evidence.MatchIds);
        Assert.DoesNotContain("CLIENT-ROOM", evidence.RoomCodes);
        Assert.DoesNotContain("claim-match", evidence.MatchIds);
        Assert.DoesNotContain("CLAIM-ROOM", evidence.RoomCodes);
    }

    [Fact]
    public void TerminalStatusesReleaseAndRealUpdateCanReopenProtection()
    {
        using var fixture = new RankedIdentityFixture();
        var now = DateTimeOffset.UtcNow;
        var report = AddBoundBug(fixture, now, "ROOM-reopen", "match-reopen");

        Assert.Equal("decision", fixture.Store.UpdateBug(fixture.Admin, report.Id,
            "decision", null, null, null)!.Status);
        Assert.Equal(["match-reopen"], fixture.Store.ReplayEvidenceAt(now).MatchIds);

        Assert.Equal("closed", fixture.Store.UpdateBug(fixture.Admin, report.Id,
            "closed", null, null, null, "reviewed terminal evidence",
            closureDisposition: "rejected")!.Status);
        Assert.Empty(fixture.Store.ReplayEvidenceAt(now).MatchIds);

        var implementation = fixture.Store.UpdateBug(fixture.Admin, report.Id,
            "implementation", null, null, null)!;
        Assert.Equal("implementation", implementation.Status);
        Assert.Null(implementation.ClosureDisposition);
        Assert.Equal(["match-reopen"], fixture.Store.ReplayEvidenceAt(now).MatchIds);

        Assert.Equal("resolved", fixture.Store.UpdateBug(fixture.Admin, report.Id,
            "resolved", null, null, null, "reviewed resolved evidence",
            closureDisposition: "rejected")!.Status);
        Assert.Empty(fixture.Store.ReplayEvidenceAt(now).RoomCodes);

        var deploy = fixture.Store.UpdateBug(fixture.Admin, report.Id,
            "deploy", null, null, null)!;
        Assert.Equal("deploy", deploy.Status);
        Assert.Null(deploy.ClosureDisposition);
        var reopened = fixture.Store.ReplayEvidenceAt(now);
        Assert.Equal(["match-reopen"], reopened.MatchIds);
        Assert.Equal(["ROOM-reopen"], reopened.RoomCodes);
    }

    [Fact]
    public void ExactSevenDayCutoffIsInclusiveAndOneTickOlderIsReleased()
    {
        using var fixture = new RankedIdentityFixture();
        var report = AddBoundBug(fixture, DateTimeOffset.UtcNow,
            "ROOM-cutoff", "match-cutoff");
        fixture.Store.UpdateBug(fixture.Admin, report.Id, "retest", null, null, null);

        var exactCutoff = report.CreatedAt.Add(L12PlatformStore.BugReplayEvidenceMaximumAge);
        var exact = fixture.Store.ReplayEvidenceAt(exactCutoff);
        Assert.Equal(["match-cutoff"], exact.MatchIds);
        Assert.Equal(["ROOM-cutoff"], exact.RoomCodes);

        var expired = fixture.Store.ReplayEvidenceAt(exactCutoff.AddTicks(1));
        Assert.Empty(expired.MatchIds);
        Assert.Empty(expired.RoomCodes);
    }

    [Fact]
    public void DiagnosticOnlyOrBlankBindingsNeverProtectAndOutputHasOnlyIdentifierFields()
    {
        using var fixture = new RankedIdentityFixture();
        var now = DateTimeOffset.UtcNow;
        var exact = AddBoundBug(fixture, now, "  ROOM-public  ", "  match-public  ");
        var diagnosticOnly = AddBoundBug(fixture, now, null, null);
        var blank = AddBoundBug(fixture, now, "   ", "\t");
        foreach (var report in new[] { exact, diagnosticOnly, blank })
            fixture.Store.UpdateBug(fixture.Admin, report.Id, "confirmed", null, null, null);

        var evidence = fixture.Store.ReplayEvidenceAt(now);
        Assert.Equal(["match-public"], evidence.MatchIds);
        Assert.Equal(["ROOM-public"], evidence.RoomCodes);
        Assert.DoesNotContain("diagnostic-match", evidence.MatchIds);
        Assert.DoesNotContain("DIAGNOSTIC-ROOM", evidence.RoomCodes);
        Assert.DoesNotContain("client-match", evidence.MatchIds);
        Assert.DoesNotContain("CLIENT-ROOM", evidence.RoomCodes);
        Assert.DoesNotContain("claim-match", evidence.MatchIds);
        Assert.DoesNotContain("CLAIM-ROOM", evidence.RoomCodes);

        var properties = typeof(L12ReplayEvidenceReferences)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);
        Assert.Equal(["MatchIds", "RoomCodes"],
            properties.Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.All(properties, property =>
            Assert.Equal(typeof(IReadOnlyCollection<string>), property.PropertyType));

        var serialized = JsonSerializer.Serialize(evidence);
        Assert.DoesNotContain("PRIVATE-TITLE-SENTINEL", serialized);
        Assert.DoesNotContain("PRIVATE-DESCRIPTION-SENTINEL", serialized);
        Assert.DoesNotContain(fixture.Admin.Id, serialized);
        Assert.DoesNotContain(exact.Id, serialized);
        Assert.DoesNotContain("diagnostic-match", serialized);
        Assert.DoesNotContain("client-match", serialized);
        Assert.DoesNotContain("claim-match", serialized);
    }

    private static L12BugReportView AddBoundBug(RankedIdentityFixture fixture,
        DateTimeOffset now, string? roomCode, string? matchId)
        => fixture.Store.AddBug(fixture.Admin, "PRIVATE-TITLE-SENTINEL",
            "PRIVATE-DESCRIPTION-SENTINEL", "/private-replay-evidence", roomCode, matchId,
            "fixture-version", new L12BugDiagnosticView(now, "diagnostic-match",
                "DIAGNOSTIC-ROOM", null, null, null, null, null, null, [], [], []),
            new L12ClientConnectionDiagnosticView(now, "/private-replay-evidence", "ok", 200,
                "ok", 200, "open", null, null, null, null, 0, "CLIENT-ROOM", "client-match",
                null, "ready", "ok", "none"),
            new L12ConnectionClaimDiagnosticView(now, "ready", 1, null,
                "CLAIM-ROOM", "claim-match", null, null));
}
