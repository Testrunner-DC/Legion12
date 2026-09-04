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
        var store = new L12PlatformStore(path);

        var created = store.AddBug(null, "进攻异常", "点击宫本武藏进攻后中断", "/battle", "ROOM01",
            "match-1", "abcdef123456", diagnostic);
        Assert.Equal("abcdef123456", created.Version);
        Assert.Equal(17, created.Diagnostic?.Revision);

        var reloaded = new L12PlatformStore(path);
        var persisted = Assert.Single(reloaded.Bugs("new"));
        Assert.Equal("match-1", persisted.Diagnostic?.MatchId);
        Assert.Equal("attack:S01-0408:attack", Assert.Single(persisted.Diagnostic!.Stack));
        Assert.DoesNotContain("hand", string.Join('|', persisted.Diagnostic.Prompts), StringComparison.OrdinalIgnoreCase);
    }
}
