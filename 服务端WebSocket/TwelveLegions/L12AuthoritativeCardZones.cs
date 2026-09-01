namespace TwelveLegions.Server;

/// <summary>
/// 服务端内部的权威卡牌实例查询与区域事务。这里可以读取全部真实区域，但绝不用于
/// Prompt/快照投影；客户端可见性仍只由 FindPromptCard 与 SnapshotFor 决定。
/// SourceSnapshot 只是最后已知信息，永远不属于任何权威区域，也不得被插入区域。
/// </summary>
public sealed partial class L12GameEngine
{
    private sealed record L12AuthoritativeCardLocation(
        L12PlayerState Host, L12CardInstance Card, string Zone, int Row = -1, int Slot = -1);

    private static readonly IReadOnlyDictionary<string, (string Trigger, string Text)> LibraryTopTriggerPlans =
        new Dictionary<string, (string Trigger, string Text)>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0414"] = ("return-library-top", "【返回牌库顶部时】效果"),
        };

    private static L12CardInstance CaptureLastKnownSourceSnapshot(L12CardInstance card)
    {
        var snapshot = card.Clone();
        snapshot.LastKnownAttachedCardIds = card.LastKnownAttachedCardIds.Count > 0
            ? [.. card.LastKnownAttachedCardIds]
            : card.AttachedCards.Select(attached => attached.InstanceId).ToList();
        return snapshot;
    }

    private List<L12AuthoritativeCardLocation> AuthoritativeCardLocations(string instanceId)
    {
        var locations = new List<L12AuthoritativeCardLocation>();
        foreach (var player in State.Players)
        {
            for (var row = 0; row < player.Field.Length; row++)
            for (var slot = 0; slot < player.Field[row].Length; slot++)
                if (player.Field[row][slot] is { } fieldCard && fieldCard.InstanceId == instanceId)
                    locations.Add(new(player, fieldCard, "field", row, slot));

            if (player.Relic is { } relic && relic.InstanceId == instanceId)
                locations.Add(new(player, relic, "relic"));
            AddCards(player, player.ExtraRelics, "extra");
            AddCards(player, player.SpecialZones.GodPower, "god-power");
            AddCards(player, player.SpecialZones.Trials, "trial");
            AddCards(player, player.SpecialZones.CanopicProgress, "canopic");
            AddCards(player, player.Resolving, "resolving");
            AddCards(player, player.Hand, "hand");
            AddCards(player, player.Library, "library");
            AddCards(player, player.Graveyard, "graveyard");
            AddCards(player, player.Removed, "removed");

            void AddCards(L12PlayerState host, IEnumerable<L12CardInstance> cards, string zone)
            {
                foreach (var card in cards.Where(card => card.InstanceId == instanceId))
                    locations.Add(new(host, card, zone));
            }
        }
        return locations;
    }

    private L12CardInstance? FindAuthoritativeCard(string instanceId)
    {
        var card = AuthoritativeCardLocations(instanceId).FirstOrDefault()?.Card;
        if (card is not null) return card;
        if (State.ActiveDisaster?.InstanceId == instanceId) return State.ActiveDisaster;
        return State.DisasterPool.Concat(State.DisasterDeck).Concat(State.BannedDisasters)
            .Concat(State.RemovedDisasters).Concat(State.SelectedDisasters)
            .Concat(State.RevealedDisasters).Concat(State.ChosenDisasters)
            .FirstOrDefault(candidate => candidate.InstanceId == instanceId);
    }

    private bool RemoveAuthoritativeLocation(L12AuthoritativeCardLocation location)
        => location.Zone switch
        {
            "field" when ReferenceEquals(location.Host.Field[location.Row][location.Slot], location.Card)
                => RemoveFieldReference(location),
            "relic" when ReferenceEquals(location.Host.Relic, location.Card)
                => RemoveRelicReference(location),
            "extra" => location.Host.ExtraRelics.Remove(location.Card),
            "god-power" => location.Host.SpecialZones.GodPower.Remove(location.Card),
            "trial" => location.Host.SpecialZones.Trials.Remove(location.Card),
            "canopic" => location.Host.SpecialZones.CanopicProgress.Remove(location.Card),
            "resolving" => location.Host.Resolving.Remove(location.Card),
            "hand" => location.Host.Hand.Remove(location.Card),
            "library" => location.Host.Library.Remove(location.Card),
            "graveyard" => location.Host.Graveyard.Remove(location.Card),
            "removed" => location.Host.Removed.Remove(location.Card),
            _ => false,
        };

    private static bool RemoveFieldReference(L12AuthoritativeCardLocation location)
    {
        location.Host.Field[location.Row][location.Slot] = null;
        return true;
    }

    private static bool RemoveRelicReference(L12AuthoritativeCardLocation location)
    {
        location.Host.Relic = null;
        return true;
    }

    private bool TryMoveAuthoritativeCardToOwnerLibraryTop(int fallbackOwner, string instanceId, string reason,
        out L12CardInstance? moved, out string? error)
    {
        moved = null;
        error = null;
        var locations = AuthoritativeCardLocations(instanceId);
        if (locations.Count == 0)
        {
            error = "权威区域中不存在该真实实例";
            return false;
        }
        if (locations.Count != 1)
        {
            error = "权威区域中存在重复实例，区域事务已取消";
            return false;
        }

        var location = locations[0];
        var card = location.Card;
        var owner = CardOwner(card, State.Players[fallbackOwner]);
        if (location.Zone == "field")
        {
            if (!MoveFieldCardToZone(location.Host, card, "library-top", reason))
            {
                error = "战场实例已失效";
                return false;
            }
            moved = card;
            AddEvent("return", owner.PlayerIndex, $"{card.Name}返回所有者牌库顶部", card);
            return true;
        }

        var sourceSnapshot = CaptureLastKnownSourceSnapshot(card);
        if (!RemoveAuthoritativeLocation(location))
        {
            error = "真实实例的来源区域已失效";
            return false;
        }
        if (location.Zone is "relic" or "extra" && card.AttachedCards.Count > 0)
            DiscardAttachedCards(card, $"{card.Name}离开圣物区");
        ResetCardAfterLeavingField(card);
        owner.Library.Insert(0, card);
        if (location.Zone is "relic" or "extra")
        {
            AddEvent("leave", location.Host.PlayerIndex, $"{card.Name}{reason}", card);
            QueueTriggerCandidates(BuildS1LeaveReactionCandidates(location.Host.PlayerIndex, sourceSnapshot));
        }
        QueueReturnedToLibraryTopTrigger(fallbackOwner, sourceSnapshot);
        AddEvent("return", owner.PlayerIndex, $"{card.Name}返回所有者牌库顶部", card);
        moved = card;
        return true;
    }

    private void QueueReturnedToLibraryTopTrigger(int controller, L12CardInstance sourceSnapshot)
    {
        if (!LibraryTopTriggerPlans.TryGetValue(sourceSnapshot.CardId, out var plan)) return;
        QueueTriggerCandidates([
            CreateTriggerCandidate(controller, sourceSnapshot, plan.Trigger, plan.Text,
                sourceSnapshot: sourceSnapshot),
        ]);
    }
}
