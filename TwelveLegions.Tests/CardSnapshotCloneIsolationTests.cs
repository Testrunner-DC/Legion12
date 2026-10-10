using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CardSnapshotCloneIsolationTests
{
    [Fact]
    [Trait("L12Evidence", "invariant:card-snapshot-deep-clone")]
    public void CardSnapshotCloneDoesNotShareMutableCollectionsOrElements()
    {
        var originalTargets = new List<string> { "target-a" };
        var attached = Card("attached", "S02-06S2");
        attached.LastKnownAttachedCardIds.Add("nested-last-known");
        attached.TimedModifiers.Add(new L12TimedModifier
        {
            TroopsDelta = 500,
            ConsumedTroopsBonus = 100,
            ExpiresAfterTurn = 7,
            Source = "叠放卡",
        });

        var original = Card("original", "S02-0601");
        original.ContinuousTroopsBonusLayers["layer"] = new L12TroopsBonusLayer
        {
            Granted = 2000,
            Consumed = 500,
        };
        original.Traits.Add("圆桌骑士");
        original.LastKnownAttachedCardIds.Add("last-known");
        original.ActiveKeywords.Add("强攻");
        original.StatusIcons.Add("buff");
        original.StatusEffects.Add(new L12StatusEffectView("troops", "兵力+2000", "测试"));
        original.Abilities.Add(new L12AbilityView("ability", "测试能力"));
        original.RuleActions.Add(new L12RuleActionView("action", "测试动作", "测试动作说明",
            TargetKeys: originalTargets));
        original.TimedModifiers.Add(new L12TimedModifier
        {
            TroopsDelta = 2000,
            ConsumedTroopsBonus = 750,
            CostDelta = -1,
            ExpiresAfterTurn = 9,
            Source = "测试来源",
        });
        original.AttachedCards.Add(attached);

        var snapshot = original.Clone();

        Assert.NotSame(original.ContinuousTroopsBonusLayers, snapshot.ContinuousTroopsBonusLayers);
        Assert.NotSame(original.ContinuousTroopsBonusLayers["layer"], snapshot.ContinuousTroopsBonusLayers["layer"]);
        Assert.NotSame(original.Traits, snapshot.Traits);
        Assert.NotSame(original.LastKnownAttachedCardIds, snapshot.LastKnownAttachedCardIds);
        Assert.NotSame(original.ActiveKeywords, snapshot.ActiveKeywords);
        Assert.NotSame(original.StatusIcons, snapshot.StatusIcons);
        Assert.NotSame(original.StatusEffects, snapshot.StatusEffects);
        Assert.NotSame(original.Abilities, snapshot.Abilities);
        Assert.NotSame(original.RuleActions, snapshot.RuleActions);
        Assert.NotSame(originalTargets, Assert.Single(snapshot.RuleActions).TargetKeys);
        Assert.NotSame(original.TimedModifiers, snapshot.TimedModifiers);
        Assert.NotSame(original.TimedModifiers[0], snapshot.TimedModifiers[0]);
        Assert.NotSame(original.AttachedCards, snapshot.AttachedCards);
        Assert.NotSame(original.AttachedCards[0], snapshot.AttachedCards[0]);
        Assert.NotSame(original.AttachedCards[0].TimedModifiers, snapshot.AttachedCards[0].TimedModifiers);
        Assert.NotSame(original.AttachedCards[0].TimedModifiers[0], snapshot.AttachedCards[0].TimedModifiers[0]);

        original.ContinuousTroopsBonusLayers["layer"].Granted = 3000;
        original.Traits.Add("原实例特性");
        original.LastKnownAttachedCardIds.Add("original-last-known");
        original.ActiveKeywords.Add("必中");
        original.StatusIcons.Add("original-icon");
        original.StatusEffects.Add(new L12StatusEffectView("original", "原实例状态"));
        original.Abilities.Add(new L12AbilityView("original", "原实例能力"));
        originalTargets.Add("target-b");
        original.TimedModifiers[0].ConsumedTroopsBonus = 1000;
        original.AttachedCards[0].LastKnownAttachedCardIds.Add("original-nested");
        original.AttachedCards[0].TimedModifiers[0].ConsumedTroopsBonus = 200;

        Assert.Equal(2000, snapshot.ContinuousTroopsBonusLayers["layer"].Granted);
        Assert.DoesNotContain("原实例特性", snapshot.Traits);
        Assert.DoesNotContain("original-last-known", snapshot.LastKnownAttachedCardIds);
        Assert.DoesNotContain("必中", snapshot.ActiveKeywords);
        Assert.DoesNotContain("original-icon", snapshot.StatusIcons);
        Assert.DoesNotContain(snapshot.StatusEffects, item => item.Kind == "original");
        Assert.DoesNotContain(snapshot.Abilities, item => item.Id == "original");
        Assert.DoesNotContain("target-b", Assert.Single(snapshot.RuleActions).TargetKeys!);
        Assert.Equal(750, snapshot.TimedModifiers[0].ConsumedTroopsBonus);
        Assert.DoesNotContain("original-nested", snapshot.AttachedCards[0].LastKnownAttachedCardIds);
        Assert.Equal(100, snapshot.AttachedCards[0].TimedModifiers[0].ConsumedTroopsBonus);

        snapshot.ContinuousTroopsBonusLayers["layer"].Consumed = 900;
        snapshot.Traits.Add("快照特性");
        snapshot.LastKnownAttachedCardIds.Add("snapshot-last-known");
        snapshot.ActiveKeywords.Add("震击");
        snapshot.StatusIcons.Add("snapshot-icon");
        snapshot.StatusEffects.Add(new L12StatusEffectView("snapshot", "快照状态"));
        snapshot.Abilities.Add(new L12AbilityView("snapshot", "快照能力"));
        snapshot.RuleActions.Add(new L12RuleActionView("snapshot", "快照动作", "快照动作说明"));
        Assert.IsType<string[]>(snapshot.RuleActions[0].TargetKeys)[0] = "snapshot-target";
        snapshot.TimedModifiers[0].ConsumedTroopsBonus = 1250;
        snapshot.AttachedCards[0].LastKnownAttachedCardIds.Add("snapshot-nested");

        Assert.Equal(500, original.ContinuousTroopsBonusLayers["layer"].Consumed);
        Assert.DoesNotContain("快照特性", original.Traits);
        Assert.DoesNotContain("snapshot-last-known", original.LastKnownAttachedCardIds);
        Assert.DoesNotContain("震击", original.ActiveKeywords);
        Assert.DoesNotContain("snapshot-icon", original.StatusIcons);
        Assert.DoesNotContain(original.StatusEffects, item => item.Kind == "snapshot");
        Assert.DoesNotContain(original.Abilities, item => item.Id == "snapshot");
        Assert.DoesNotContain(original.RuleActions, item => item.Id == "snapshot");
        Assert.Equal("target-a", originalTargets[0]);
        Assert.Equal(1000, original.TimedModifiers[0].ConsumedTroopsBonus);
        Assert.DoesNotContain("snapshot-nested", original.AttachedCards[0].LastKnownAttachedCardIds);
    }

    private static L12CardInstance Card(string instanceId, string cardId)
        => new()
        {
            InstanceId = instanceId,
            CardId = cardId,
            Name = cardId,
            CardType = "legion",
            Faction = "otherworld",
        };
}
