namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private void BeginEffectMoralePayment(L12StackItem item, int cost, string afterPayment, Dictionary<string, string>? extra = null)
    {
        var player = State.Players[item.Controller];
        if (ActiveResourceCount(player) < cost) { FinishStackItem(item); return; }
        var guards = PublicLegions(player).Count(card => card.CardId == "S01-0212" && !card.Tapped && State.ActivePlayer == item.Controller);
        if (guards == 0)
        {
            if (TryConsumeMorale(player, cost)) CompleteEffectMoralePayment(item, afterPayment, extra ?? []);
            else FinishStackItem(item);
            return;
        }
        var canPayWithoutGuards = ActiveMoraleCountWithoutTombGuards(player) >= cost;
        var data = new Dictionary<string, string>
        {
            ["action"] = "effect-morale-payment", ["afterPayment"] = afterPayment, ["cost"] = cost.ToString(),
            ["canPayWithoutGuards"] = canPayWithoutGuards.ToString(), ["choiceMode"] = "instant",
            ["yes"] = $"使用陵墓守卫优先支付（费用 {cost}）",
            ["no"] = canPayWithoutGuards ? $"仅使用士气支付（费用 {cost}）" : "不使用并取消发动",
        };
        if (extra is not null) foreach (var pair in extra) data[$"payment:{pair.Key}"] = pair.Value;
        CreatePrompt(item.Controller, "optional", "是否使用活跃的陵墓守卫支付本次效果费用？", ["yes", "no"], 1, 1,
            "card-effect", item.StackItemId, data: data);
    }

    private void ContinueEffectMoralePayment(L12StackItem item, L12Prompt prompt, string choice)
    {
        var cost = int.TryParse(prompt.Data.GetValueOrDefault("cost"), out var parsedCost) ? parsedCost : 0;
        var canPayWithoutGuards = bool.TryParse(prompt.Data.GetValueOrDefault("canPayWithoutGuards"), out var parsedCanPay) && parsedCanPay;
        if (choice == "no" && !canPayWithoutGuards) { FinishStackItem(item); return; }
        var player = State.Players[item.Controller];
        var paid = choice == "yes"
            ? TryConsumeMorale(player, cost, preferTombGuards: true, allowTombGuards: true)
            : TryConsumeMorale(player, cost, preferTombGuards: false, allowTombGuards: false);
        if (!paid) { FinishStackItem(item); return; }
        var extra = prompt.Data.Where(pair => pair.Key.StartsWith("payment:", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key[8..], pair => pair.Value);
        CompleteEffectMoralePayment(item, prompt.Data.GetValueOrDefault("afterPayment") ?? string.Empty, extra);
    }

    private void CompleteEffectMoralePayment(L12StackItem item, string afterPayment, IReadOnlyDictionary<string, string> data)
    {
        var player = State.Players[item.Controller]; var enemy = State.Players[1 - item.Controller]; var source = FindSource(item);
        switch (afterPayment)
        {
            case "nobunaga-debuff":
                foreach (var target in PublicLegions(enemy)) target.CostModifier--;
                AddEvent("effect", item.Controller, "织田信长：对方所有军团本回合费用-1", source is null ? [] : [source]);
                FinishStackItem(item); break;
            case "hijikata-kill":
                PromptEnemyLegion(item, "hijikata-attack-kill", "土方岁三：击杀对方1张费用不高于1的军团", target => target.CurrentCost <= 1, true); break;
            case "takasugi-debuff":
                PromptEnemyLegion(item, "takasugi-debuff", "高杉晋作：选择对方1张军团，本回合费用-2", _ => true, false); break;
            case "scout-shuffle":
                if (enemy.Hand.Count == 0) { FinishStackItem(item); break; }
                CreatePrompt(1 - item.Controller, "card", "前线侦查：选择1张手牌洗回牌库", enemy.Hand.Select(card => card.InstanceId), 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "scout-shuffle" }); break;
            case "ay-buff":
                PromptOwnLegion(item, "ay-buff", "阿伊：选择我方前排1张兵力不高于2000的军团，本回合兵力+2000",
                    target => target.Troops <= 2000 && FindOnField(player, target.InstanceId, out var row, out _) is not null && row == 0, false); break;
            case "asgard-heal": HealMaster(item.Controller, 1, "阿斯加德阵营效果"); FinishStackItem(item); break;
            case "camp-mode":
                if (data.GetValueOrDefault("mode") == "heal") HealMaster(item.Controller, 1, "野外扎营");
                else if (data.GetValueOrDefault("mode") == "draw") Draw(player, 1);
                FinishStackItem(item); break;
            case "s2-prayer-private": BeginPrayerPrivatePreview(item); break;
            case "s2-black-lotus-morale":
                if (source is not null && player.Resolving.Remove(source))
                {
                    player.Morale.Add(new L12MoraleCard
                    {
                        InstanceId = source.InstanceId,
                        CardId = source.CardId,
                        Tapped = true,
                    });
                    AddEvent("morale", item.Controller, "〈黑色莲花〉休整置入士气区，视为1张士气", source);
                }
                FinishStackItem(item); break;
            default: FinishStackItem(item); break;
        }
    }
}
