using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    // Display only already-public identities. Never resolve a target through private-zone lookup.
    private IEnumerable<(string Id, string Label, bool OnField)> PublicResponseTargets(L12StackItem item, int viewer)
    {
        if (item.Data.GetValueOrDefault("eventType") == "effect-hand-add") yield break;
        foreach (var id in item.Targets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var found = false;
            foreach (var player in State.Players)
            {
                var side = player.PlayerIndex == viewer ? "我方" : "对方";
                for (var row = 0; row < 2; row++)
                for (var slot = 0; slot < 3; slot++)
                {
                    var card = player.Field[row][slot];
                    if (card?.InstanceId != id) continue;
                    var name = card.Hidden ? "盖伏卡牌" : $"〈{card.Name}〉";
                    yield return (id, $"{side}{name}（{(row == 0 ? "前排" : "后排")}第{slot + 1}格）", true);
                    found = true;
                }
                var grave = player.Graveyard.FirstOrDefault(card => card.InstanceId == id && !card.Hidden);
                if (grave is not null)
                {
                    yield return (id, $"{side}墓地〈{grave.Name}〉", false);
                    found = true;
                }
            }
            if (found) continue;
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
    }
}
