using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BugDiagnosticPersistenceTests
{
    [Fact]
    public void BugReportPersistsServerVersionAndRedactedRuntimeDiagnostic()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-bug-diagnostic", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "platform.json");
        var diagnostic = new L12BugDiagnosticView(DateTimeOffset.UtcNow, "match-1", "ROOM01", "Main", 2, 8,
            0, 17, 4, ["attack:S01-0408:attack"], ["option:card-effect:attack-trigger"], ["attack"]);
        var clientDiagnostic = new L12ClientConnectionDiagnosticView(DateTimeOffset.UtcNow, "/me", "ok", 200,
            "authenticated", 200, "open", 1006, "network interrupted", DateTimeOffset.UtcNow.AddSeconds(-25),
            DateTimeOffset.UtcNow.AddSeconds(-24), 2, "ROOM01", "match-1", 7, "snapshot-received",
            "authenticated", "inactive");
        var claimDiagnostic = new L12ConnectionClaimDiagnosticView(DateTimeOffset.UtcNow,
            "reclaimed-disconnected-room", 7, 6, "ROOM01", "match-1", 17, null);
        var store = new L12PlatformStore(path);

        var created = store.AddBug(null, "进攻异常", "点击宫本武藏进攻后中断", "/battle", "ROOM01",
            "match-1", "client-release-20260906", diagnostic, clientDiagnostic, claimDiagnostic);
        Assert.Equal("client-release-20260906", created.ClientVersion);
        Assert.NotEqual(created.ClientVersion, created.ServerVersion);
        Assert.False(string.IsNullOrWhiteSpace(created.EngineVersion));
        Assert.DoesNotMatch(@"^l12-engine/1\.0\.0(?:\.0)?$", created.EngineVersion);
        Assert.Equal("client-release-20260906", created.Version);
        Assert.Equal(17, created.Diagnostic?.Revision);
        Assert.Equal("snapshot-received", created.ClientDiagnostic?.RecoveryPhase);
        Assert.Equal(7, created.ConnectionDiagnostic?.ConnectionGeneration);

        var reloaded = new L12PlatformStore(path);
        var persisted = Assert.Single(reloaded.Bugs("new"));
        Assert.Equal("match-1", persisted.Diagnostic?.MatchId);
        Assert.Equal("attack:S01-0408:attack", Assert.Single(persisted.Diagnostic!.Stack));
        Assert.DoesNotContain("hand", string.Join('|', persisted.Diagnostic.Prompts), StringComparison.OrdinalIgnoreCase);
        var serializedClientDiagnostic = System.Text.Json.JsonSerializer.Serialize(persisted.ClientDiagnostic);
        Assert.DoesNotContain("token", serializedClientDiagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ip", serializedClientDiagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hand", serializedClientDiagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("reclaimed-disconnected-room", persisted.ConnectionDiagnostic?.Decision);
    }

    [Fact]
    public void LegacyBugRowsRemainReadableWithoutTreatingMissingClientVersionAsServerVersion()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-bug-version-legacy", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "platform.json");
        var store = new L12PlatformStore(path);

        var created = store.AddBug(null, "旧客户端", "没有版本字段", "/me", null, null, string.Empty);

        Assert.Equal("unknown-client", created.Version);
        Assert.Equal("unknown-client", created.ClientVersion);
        Assert.False(string.IsNullOrWhiteSpace(created.ServerVersion));
        Assert.False(string.IsNullOrWhiteSpace(created.EngineVersion));
        Assert.DoesNotMatch(@"^l12-engine/1\.0\.0(?:\.0)?$", created.EngineVersion);
    }
}
