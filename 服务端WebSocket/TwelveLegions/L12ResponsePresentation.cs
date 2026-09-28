using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private const string ResponsePresentationTargetIdsKey = "responsePresentationTargetIds";
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
            if (!State.Players.Any(player => FindOnField(player, id, out _, out _) is not null)
                || activation.ResponsePresentationTargetIds.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
            activation.ResponsePresentationTargetIds.Add(id);
        }
    }

    // Display only already-public identities. Never resolve a target through private-zone lookup.
    private IEnumerable<(string Id, string Label, bool OnField)> PublicResponseTargets(L12StackItem item, int viewer)
    {
        if (item.Data.GetValueOrDefault("eventType") == "effect-hand-add") yield break;
        var authoritativeTargets = item.Targets.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var presentationTargets = (item.Data.GetValueOrDefault(ResponsePresentationTargetIdsKey) ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => !authoritativeTargets.Contains(id, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => (Id: id, FieldOnly: true));
        foreach (var candidate in authoritativeTargets.Select(id => (Id: id, FieldOnly: false)).Concat(presentationTargets))
        {
            var id = candidate.Id;
            var found = false;
            foreach (var player in State.Players)
            {
                var side = player.PlayerIndex == viewer ? "我方" : "对方";
                for (var row = 0; row < 2; row++)
                for (var slot = 0; slot < 3; slot++)
                {
                    var card = player.Field[row][slot];
                    if (card is null || !string.Equals(card.InstanceId, id, StringComparison.OrdinalIgnoreCase)) continue;
                    var name = card.Hidden ? "盖伏卡牌" : $"〈{card.Name}〉";
                    yield return (id, $"{side}{name}（{(row == 0 ? "前排" : "后排")}第{slot + 1}格）", true);
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
                    : $"{(targetEffect.Controller == viewer ? "我方" : "对方")}〈{targetEffect.SourceName}〉的效果：{targetEffect.Text}", false);
        }
    }

    private string ResponseTargetIds(IEnumerable<L12StackItem> items, int viewer)
        => JsonSerializer.Serialize(items.SelectMany(item => PublicResponseTargets(item, viewer))
            .Where(target => target.OnField).Select(target => target.Id).Distinct(StringComparer.OrdinalIgnoreCase));

    private string DescribeResponse(L12StackItem item, int viewer)
    {
        if (item.Data.GetValueOrDefault("eventType") == "effect-hand-add")
            return $"{(item.Controller == viewer ? "我方" : "对方")}因效果将卡牌加入手牌。是否响应？";
        var side = item.Controller == viewer ? "我方" : "对方";
        var targets = PublicResponseTargets(item, viewer).Select(target => target.Label).ToArray();
        return $"{side}使用{BuildResponsePromptText(item)}"
            + "\n（效果原文中的我方／对方以发动者为准）"
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
