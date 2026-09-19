using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ReplayEvidenceProtectionTests
{
    [Fact]
    public void OnlyTriagedRecentExplicitBugBindingsProtectReplayPayload()
    {
        var path = Path.Combine(Path.GetTempPath(), "l12-replay-evidence",
            Guid.NewGuid().ToString("N"), "platform.json");
        var now = DateTimeOffset.UtcNow;
        var store = new L12PlatformStore(path);
        var report = store.AddBug(null, "合成报告", "验证回放证据保护", "/battle", "PRIMARY", "primary",
            "fixture", new L12BugDiagnosticView(now, "diagnostic", "DIAGNOSTIC", null,
                null, null, null, null, null, [], [], []),
            new L12ClientConnectionDiagnosticView(now, "/battle", "ok", 200, "ok", 200,
                "open", null, null, null, null, 0, "CLIENT", "client", null, "ready", "ok", "none"),
            new L12ConnectionClaimDiagnosticView(now, "ready", 1, null, "CLAIM", "claim", null, null));
        Assert.Empty(store.ReplayEvidenceAt(now).MatchIds); // untriaged reports do not hold payloads
        var admin = new L12AccountView("fixture-admin", "fixture", "admin", now, false);
        foreach (var status in new[] { "confirmed", "in-progress" })
        {
            store.UpdateBug(admin, report.Id, status, null, null, null);
            var evidence = store.ReplayEvidenceAt(now);
            Assert.Equal(["primary"], evidence.MatchIds);
            Assert.Equal(["PRIMARY"], evidence.RoomCodes);
            Assert.DoesNotContain("diagnostic", evidence.MatchIds);
            Assert.DoesNotContain("claim", evidence.MatchIds);
        }
        store.UpdateBug(admin, report.Id, "resolved", null, null, null);
        Assert.Empty(store.ReplayEvidenceAt(now).MatchIds);
        store.UpdateBug(admin, report.Id, "new", null, null, null);
        Assert.Empty(new L12PlatformStore(path).ReplayEvidenceAt(now).MatchIds);
        store.UpdateBug(admin, report.Id, "confirmed", null, null, null);
        Assert.Empty(store.ReplayEvidenceAt(
            report.CreatedAt.Add(L12PlatformStore.BugReplayEvidenceMaximumAge).AddTicks(1)).MatchIds);
        store.UpdateBug(admin, report.Id, "closed", null, null, null);
        Assert.Empty(store.ReplayEvidenceAt(now).RoomCodes);
        Assert.Single(store.Bugs(null)); // Cleanup protection never deletes the report itself.
    }
}
