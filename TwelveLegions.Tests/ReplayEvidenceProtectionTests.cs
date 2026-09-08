using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ReplayEvidenceProtectionTests
{
    [Fact]
    public void UnresolvedReportsProtectAllDiagnosticReferencesAndReleaseOnlyAfterClosure()
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
        // The existing report normalizer binds client diagnostics to the authoritative top-level ID.
        var expected = new[] { "primary", "diagnostic", "claim" }.Order().ToArray();
        Assert.Equal(expected, store.UnresolvedReplayEvidence().MatchIds.Order().ToArray());
        Assert.Equal(3, store.UnresolvedReplayEvidence().RoomCodes.Count);
        var admin = new L12AccountView("fixture-admin", "fixture", "admin", now, false);
        foreach (var status in new[] { "confirmed", "in-progress" })
        {
            store.UpdateBug(admin, report.Id, status, null, null, null);
            Assert.Equal(expected, store.UnresolvedReplayEvidence().MatchIds.Order().ToArray());
        }
        store.UpdateBug(admin, report.Id, "resolved", null, null, null);
        Assert.Empty(store.UnresolvedReplayEvidence().MatchIds);
        store.UpdateBug(admin, report.Id, "new", null, null, null);
        Assert.Equal(3, new L12PlatformStore(path).UnresolvedReplayEvidence().MatchIds.Count);
        store.UpdateBug(admin, report.Id, "closed", null, null, null);
        Assert.Empty(store.UnresolvedReplayEvidence().RoomCodes);
        Assert.Single(store.Bugs(null)); // Cleanup protection never deletes the report itself.
    }
}
