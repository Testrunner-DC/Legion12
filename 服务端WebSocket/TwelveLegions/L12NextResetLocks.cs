namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private static void LockUntilNextReset(L12CardInstance card)
        => card.CannotUntapAtNextReset = true;

    private static void LockUntilNextReset(L12MoraleCard card)
        => card.CannotUntapAtNextReset = true;

    // The one-shot state writers above stay unchanged. Rule handlers publish
    // their public result through this shared exit at the same successful write.
    private void LockUntilNextResetByEffect(L12StackItem item, L12CardInstance target)
    {
        LockUntilNextReset(target);
        var publicTarget = !target.Hidden && !string.IsNullOrWhiteSpace(target.Name);
        RecordNextResetLock(item,
            $"{(publicTarget ? $"〈{target.Name}〉" : "所选盖伏卡")}下个重置阶段无法转为活跃",
            publicTarget ? [target] : []);
    }

    private void LockUntilNextResetByEffect(L12StackItem item, L12MoraleCard target)
        => LockMoraleUntilNextResetByEffect(item, [target]);

    private void LockMoraleUntilNextResetByEffect(L12StackItem item, IEnumerable<L12MoraleCard> targets, int unmatched = 0)
    {
        var actual = targets.DistinctBy(card => card.InstanceId, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var target in actual) LockUntilNextReset(target);
        if (actual.Length > 0)
            RecordNextResetLock(item, $"所选{actual.Length}张士气下个重置阶段无法转为活跃"
                + (unmatched > 0 ? $"；另有{unmatched}张所选士气不再休整，未受到限制；其余对象继续结算" : string.Empty), []);
    }

    private void RecordNextResetLock(L12StackItem item, string outcome, L12CardInstance[] publicTargets)
    {
        // Use an actual source snapshot, never the display name/card number as
        // a way of reconstructing a missing or hidden source's public identity.
        var source = FindSource(item) ?? item.SourceSnapshot;
        if (source is null || IsPrivateTriggerSource(source)
            || State.Players.Any(player => player.Library.Any(card => card.InstanceId == source.InstanceId))
            || string.IsNullOrWhiteSpace(source.Name)
            || source.InstanceId != item.SourceInstanceId || source.CardId != item.SourceCardId)
        {
            AddEvent("effect", item.Controller, "所选对象下个重置阶段无法转为活跃");
            return;
        }
        var action = item.Trigger switch
        {
            "enter" => "发动登场时效果",
            "promotion-enter" => "发动晋升登场效果",
            "death" => "发动阵亡时效果",
            _ => "发动效果",
        };
        AddSemanticPlayerLogEvent("effect", item.Controller, $"〈{source.Name}〉{action}，{outcome}",
            new(action, outcome, source.InstanceId, source.Name,
                publicTargets.Length == 1 ? publicTargets[0].InstanceId : null,
                publicTargets.Length == 1 ? publicTargets[0].Name : null),
            new[] { source }.Concat(publicTargets).DistinctBy(card => card.InstanceId).ToArray());
    }

    private bool CanUntapDuringReset(L12CardInstance card)
    {
        var blockedAtThisReset = card.CannotUntapAtNextReset;
        card.CannotUntapAtNextReset = false;
        return !blockedAtThisReset && card.CannotUntapUntilRound < State.Round;
    }

    private bool CanUntapDuringReset(L12MoraleCard card)
    {
        var blockedAtThisReset = card.CannotUntapAtNextReset;
        card.CannotUntapAtNextReset = false;
        return !blockedAtThisReset && card.CannotUntapUntilRound < State.Round;
    }
}
