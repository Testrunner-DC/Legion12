using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private const string ResponsePresentationTargetIdsKey = "responsePresentationTargetIds";
    private const string ResponsePublicTargetSnapshotKey = "responsePublicTargetSnapshotV1";
    private sealed record ResponsePublicTargetSnapshot(string Id, int Owner, string Zone,
        int Row, int Slot, string? PublicName, int? CurrentCost, bool Tapped);
    private L12PendingActivation? _committingResponsePresentationActivation;

    private CommandResult CommitWithResponsePresentation(L12PendingActivation activation,
        Func<CommandResult> commit)
    {
        var previous = _committingResponsePresentationActivation;
        activation.IsCommittingResponsePresentation = true;
        _committingResponsePresentationActivation = activation;
        try
        {
            return commit();
        }
        finally
        {
            _committingResponsePresentationActivation = previous;
            activation.IsCommittingResponsePresentation = false;
        }
    }

    private static void SetResponsePresentationTargets(Dictionary<string, string> data,
        IEnumerable<string> targetIds)
    {
        var targets = targetIds.Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (targets.Length == 0) data.Remove(ResponsePresentationTargetIdsKey);
        else data[ResponsePresentationTargetIdsKey] = string.Join('|', targets);
    }

    private void CaptureResponsePresentationTargets(L12PendingActivation activation,
        L12ActivationSelectionStep step, IEnumerable<string> selected)
    {
        if (!step.IsResponsePresentationTarget || step.IsCostSelection) return;
        foreach (var id in selected)
        {
            if (!State.Players.Any(player => FindOnField(player, id, out _, out _) is not null
                    || player.Morale.Any(card => card.InstanceId == id))
                || activation.ResponsePresentationTargetIds.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
            activation.ResponsePresentationTargetIds.Add(id);
        }
    }

    private void CaptureResponsePublicTargetSnapshot(Dictionary<string, string> data,
        IEnumerable<string> selectedIds)
    {
        var facts = new List<ResponsePublicTargetSnapshot>();
        foreach (var id in selectedIds.Distinct(StringComparer.OrdinalIgnoreCase))
        foreach (var player in State.Players)
        {
            var field = FindOnField(player, id, out var row, out var slot);
            if (field is not null)
            {
                // A covered card has no public face or cost to snapshot.
                facts.Add(new(id, player.PlayerIndex, "field", row, slot,
                    field.Hidden ? null : field.Name, field.Hidden ? null : field.CurrentCost, false));
                break;
            }
            if (player.Morale.FirstOrDefault(card => card.InstanceId == id) is { } morale)
            {
                // Only the public resource face/activity state is needed here. Its CardId is
                // already public, but need not be repeated in response metadata.
                facts.Add(new(id, player.PlayerIndex, "morale", -1, -1, null, null, morale.Tapped));
                break;
            }
        }
        if (facts.Count > 0) data[ResponsePublicTargetSnapshotKey] = JsonSerializer.Serialize(facts);
    }

    private static IReadOnlyList<ResponsePublicTargetSnapshot> ReadResponsePublicTargetSnapshot(
        Dictionary<string, string> data)
    {
        if (!data.TryGetValue(ResponsePublicTargetSnapshotKey, out var encoded)) return [];
        try { return JsonSerializer.Deserialize<List<ResponsePublicTargetSnapshot>>(encoded) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string ResponseTargetSideLabel(int viewer, int owner)
        => viewer < 0 ? $"玩家{owner + 1}的" : owner == viewer ? "你的" : "对手的";

    private static string ResponseBattlefieldSlotLabel(int viewer, int owner, int row, int slot)
        => $"{ResponseTargetSideLabel(viewer, owner)}{(row == 0 ? "前排" : "后排")}{new[] { "左格", "中格", "右格" }[slot]}";

    private bool IsCurrentPublicResponseTarget(string id)
        => State.Players.Any(player => FindOnField(player, id, out _, out _) is not null
            || player.Morale.Any(card => card.InstanceId == id));

    private string DeclaredPublicTargetLabel(L12StackItem item, string id)
    {
        var label = PublicResponseTargets(item, item.Controller)
            .FirstOrDefault(target => string.Equals(target.Id, id, StringComparison.OrdinalIgnoreCase)).Label;
        return label is null ? "原目标" : $"原目标{label}";
    }

    // Display only already-public identities. Never resolve a target through private-zone lookup.
    private IEnumerable<(string Id, string Label, bool OnField)> PublicResponseTargets(L12StackItem item, int viewer)
    {
        if (item.Data.GetValueOrDefault("eventType") == "effect-hand-add") yield break;
        var frozen = ReadResponsePublicTargetSnapshot(item.Data);
        var frozenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fact in frozen)
        {
            if (fact.Owner is < 0 or > 1 || !frozenIds.Add(fact.Id)) continue;
            var side = ResponseTargetSideLabel(viewer, fact.Owner);
            if (fact.Zone == "field" && fact.Row is >= 0 and < 2 && fact.Slot is >= 0 and < 3)
            {
                var name = fact.PublicName is null ? "盖伏卡牌" : $"〈{fact.PublicName}〉";
                var cost = fact.CurrentCost is { } value ? $"；声明时当前费用{value}" : "";
                yield return (fact.Id,
                    $"{ResponseBattlefieldSlotLabel(viewer, fact.Owner, fact.Row, fact.Slot)}{name}{cost}", true);
            }
            else if (fact.Zone == "morale")
                yield return (fact.Id, $"{side}士气区的{(fact.Tapped ? "休整" : "活跃")}士气", true);
        }
        var authoritativeTargets = item.Targets.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var presentationTargets = (item.Data.GetValueOrDefault(ResponsePresentationTargetIdsKey) ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => !authoritativeTargets.Contains(id, StringComparer.OrdinalIgnoreCase) && !frozenIds.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => (Id: id, FieldOnly: true));
        foreach (var candidate in authoritativeTargets.Select(id => (Id: id, FieldOnly: false)).Concat(presentationTargets))
        {
            var id = candidate.Id;
            if (frozenIds.Contains(id)) continue;
            var found = false;
            foreach (var player in State.Players)
            {
                var side = ResponseTargetSideLabel(viewer, player.PlayerIndex);
                for (var row = 0; row < 2; row++)
                for (var slot = 0; slot < 3; slot++)
                {
                    var card = player.Field[row][slot];
                    if (card is null || !string.Equals(card.InstanceId, id, StringComparison.OrdinalIgnoreCase)) continue;
                    var name = card.Hidden ? "盖伏卡牌" : $"〈{card.Name}〉";
                    yield return (id, $"{ResponseBattlefieldSlotLabel(viewer, player.PlayerIndex, row, slot)}{name}", true);
                    found = true;
                }
                if (candidate.FieldOnly) continue;
                var grave = player.Graveyard.FirstOrDefault(card => card.InstanceId == id && !card.Hidden);
                if (grave is not null)
                {
                    yield return (id, $"{side}墓地〈{grave.Name}〉", false);
                    found = true;
                }
            }
            if (found) continue;
            if (candidate.FieldOnly) continue;
            var targetEffect = State.EffectStack.FirstOrDefault(effect => effect.StackItemId == id);
            if (targetEffect is not null)
                yield return (id, targetEffect.Data.GetValueOrDefault("eventType") == "effect-hand-add"
                    ? "因效果加入手牌的事件"
                    : $"{(targetEffect.Controller == viewer ? "你" : "对手")}的〈{targetEffect.SourceName}〉效果：{targetEffect.Text}", false);
        }
    }

    private string ResponseTargetIds(IEnumerable<L12StackItem> items, int viewer)
        => JsonSerializer.Serialize(items.SelectMany(item => PublicResponseTargets(item, viewer))
            .Where(target => target.OnField).Select(target => target.Id).Distinct(StringComparer.OrdinalIgnoreCase));

    private string DescribeResponse(L12StackItem item, int viewer)
    {
        if (item.Data.GetValueOrDefault("eventType") == "effect-hand-add")
            return $"{(item.Controller == viewer ? "你" : "对手")}因效果将卡牌加入手牌。是否响应？";
        var side = item.Controller == viewer ? "你" : "对手";
        var targets = PublicResponseTargets(item, viewer).Select(target => target.Label).ToArray();
        return $"{side}使用{BuildResponsePromptText(item)}"
            + (targets.Length == 0 ? "" : $"\n已选目标：{string.Join("；", targets)}")
            + "\n是否响应？";
    }

    private void AddBoundResponsePresentation(int viewer, L12StackItem target, Dictionary<string, string> data)
    {
        data["responseTargetIds"] = ResponseTargetIds([target], viewer);
        data["responseContext"] = DescribeResponse(target, viewer);
        AddPaidCostResponseData(target, data);
    }
}
