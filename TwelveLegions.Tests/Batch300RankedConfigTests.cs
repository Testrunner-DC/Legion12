using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class Batch300RankedConfigTests
{
    [Fact]
    public void SharedTierValuesPersistWithoutSortingNamesAndUseDynamicPlacementLimit()
    {
        var path = Path.Combine(Path.GetTempPath(), "l12-batch300", Guid.NewGuid().ToString("N"), "platform.json");
        var store = new L12PlatformStore(path);
        var admin = store.Login("Admin", "L12master").Account!;
        var original = store.RankedConfig(admin);
        int[] thresholds = [0, 16000, 40000, 70000, 110000];
        var changed = original with { PlacementMaximum = 39999, Factions = original.Factions.Select(f => f with
        {
            Tiers = f.Tiers.Select((t, i) => t with { Minimum = thresholds[i], BaseDelta = 101 + i,
                WinStreakCap = 21 + i, LossProtectionCap = 11 + i, RatingGapCap = 31 + i }).ToArray(),
        }).ToArray() };
        store.UpdateRankedConfig(admin, changed, "调整共同规则", new L12AdminAuditContext("batch300"));
        var loaded = new L12PlatformStore(path).RankedConfig(admin);
        Assert.Equal(39999, loaded.PlacementMaximum);
        for (var f = 0; f < 3; f++) Assert.Equal(changed.Factions[f].Tiers, loaded.Factions[f].Tiers);
        foreach (var bad in new[]
        {
            changed with { PlacementMaximum = 40000 },
            changed with { Factions = changed.Factions.Select(f => f with { Tiers = f.Tiers.Reverse().ToArray() }).ToArray() },
            changed with { Factions = changed.Factions.Select(f => f with { Tiers = f.Tiers.Select((t,i) => i == 1 ? t with { Minimum = 0 } : t).ToArray() }).ToArray() },
            changed with { Factions = changed.Factions.Select(f => f with { Tiers = f.Tiers.Select((t,i) => i == 4 ? t with { Minimum = int.MaxValue } : t).ToArray() }).ToArray() },
        })
        {
            Assert.Throws<L12OperationsConfigException>(() => store.UpdateRankedConfig(admin, bad,
                "拒绝非法规则", new L12AdminAuditContext("batch300-invalid")));
            Assert.Equal(changed.Factions[0].Tiers, store.RankedConfig(admin).Factions[0].Tiers);
        }
    }

    [Fact]
    public void TerminationRewardFollowsConfiguredTierNotOldScoreThreshold()
    {
        var store = new L12PlatformStore(Path.Combine(Path.GetTempPath(), "l12-batch300", Guid.NewGuid().ToString("N"), "platform.json"));
        var admin = store.Login("Admin", "L12master").Account!;
        var original = store.RankedConfig(admin);
        store.UpdateRankedConfig(admin, original with { PlacementMaximum = 19, Factions = original.Factions.Select(f => f with
        { Tiers = f.Tiers.Select((t,i) => t with { Minimum = i * 10 }).ToArray() }).ToArray() },
            "验证动态门槛", new L12AdminAuditContext("batch300-reward"));
        var reward = typeof(L12PlatformStore).GetMethod("StreakTerminationReward", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Assert.Equal(400, reward.Invoke(store, [20]));
        Assert.Equal(200, reward.Invoke(store, [19]));
        Assert.Equal(1250, reward.Invoke(store, [40]));
    }
}
