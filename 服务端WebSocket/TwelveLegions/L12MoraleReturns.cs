namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private void CreateReturnMoralePrompt(int playerIndex, int count, string continuation, string? stackItemId,
        Dictionary<string, string> data, bool requireActive = false)
    {
        var player = State.Players[playerIndex];
        var choices = player.Morale.Where(card => !requireActive || !card.Tapped).Select(card => card.InstanceId).ToArray();
        data["count"] = count.ToString();
        data["requireActive"] = requireActive.ToString();
        data["choiceMode"] = "resource-return";
        foreach (var morale in player.Morale.Where(card => choices.Contains(card.InstanceId)))
        {
            data[$"{morale.InstanceId}:resourceType"] = morale.CardId == "S02-0010"
                ? "black-lotus"
                : morale.IsGodPower ? "god-power" : morale.Tapped ? "rested-morale" : "active-morale";
        }
        CreatePrompt(playerIndex, "resource-return", "请选择返还的士气", choices, count, count,
            continuation, stackItemId, isPrivate: true, data: data);
    }

    private int GetActiveAbilityReturnMoraleCost(L12PlayerState player, L12CardInstance source, string ability, string? target)
        => ability switch
        {
            "nonLethal" when source.CardId == "S01-01M1" => 4,
            "searchBrothers" when source.CardId == "S01-0105" => 1,
            "artifactDraw" when source.CardId == "S01-0117" => 1,
            "extendedRange" when source.CardId == "S01-0113" => 1,
            "xishiExchange" when source.CardId == "S01-0116" => 1,
            "mengpoSilence" when source.CardId == "S01-01M2" => 1,
            "shennongReset" when source.CardId == "S02-0104" => 1,
            "palaceExchange" when source.CardId == "S01-01D1"
                => DeclaredEnemyTarget(player.PlayerIndex, target)?.CurrentCost ?? 0,
            _ => 0,
        };

    private static bool ActiveReturnRequiresActiveMorale(L12CardInstance source, string ability)
        => source.CardId == "S01-0117" && ability == "artifactDraw";

    private string? ValidateActiveReturnPrepayment(int playerIndex, L12CardInstance source, string ability, string? target)
    {
        var player = State.Players[playerIndex];
        return ability switch
        {
            "searchBrothers" when source.CardId == "S01-0105" && source.Tapped
                => "刘备必须为活跃状态",
            "artifactDraw" when source.CardId == "S01-0117" && source.Tapped
                => "山河社稷图必须为活跃状态",
            "extendedRange" when source.CardId == "S01-0113"
                && (FindOnField(player, source.InstanceId, out var row, out _) is null || row != 1)
                => "该效果只能在后排发动",
            "xishiExchange" when source.CardId == "S01-0116" && !IsValidXishiDeclaration(player, target)
                => "声明的手牌目标或位置不再合法",
            "palaceExchange" when source.CardId == "S01-01D1" && source.Tapped
                => "凌霄宝殿必须为活跃状态",
            "palaceExchange" when source.CardId == "S01-01D1" && DeclaredEnemyTarget(playerIndex, target) is null
                => "目标不再合法",
            "mengpoSilence" when source.CardId == "S01-01M2" && !string.IsNullOrWhiteSpace(target)
                && DeclaredEnemyTarget(playerIndex, target) is null
                => "目标不再合法",
            "shennongReset" when source.CardId == "S02-0104" && source.Tapped
                => "神农鼎必须为活跃状态",
            "shennongReset" when source.CardId == "S02-0104"
                && (string.IsNullOrWhiteSpace(target)
                    || !player.UsedAbilities.Contains($"active:master-{playerIndex}:{target}"))
                => "所选主宰效果已不再处于使用过的状态",
            _ => null,
        };
    }

    private static bool IsValidXishiDeclaration(L12PlayerState player, string? target)
    {
        var declared = (target ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (declared.Length == 0) return true;
        if (declared.Length != 2) return false;
        var handCard = player.Hand.FirstOrDefault(card => card.InstanceId == declared[0]
            && card.CardType == "legion" && card.CardId != "S01-0116" && card.Troops <= 2000);
        var (row, slot) = ParseSlot(declared[1]);
        return handCard is not null && row is >= 0 and <= 1 && slot is >= 0 and <= 2
            && player.Field[row][slot] is null;
    }

    private bool BeginEffectMoraleReturn(L12StackItem item, int count, string afterReturn,
        Dictionary<string, string>? extra = null, bool requireActive = false)
    {
        var player = State.Players[item.Controller];
        var eligible = player.Morale.Count(card => !requireActive || !card.Tapped);
        if (count <= 0 || eligible < count) return false;
        if (!NeedsManualReturnMoraleSelection(player, count, requireActive))
        {
            var selected = player.Morale.Where(card => !requireActive || !card.Tapped)
                .OrderByDescending(card => card.Tapped).Take(count).ToArray();
            if (!ReturnSelectedMorale(player, selected, requireActive)) return false;
            CompleteEffectMoraleReturn(item, afterReturn, extra ?? []);
            return true;
        }

        var data = new Dictionary<string, string>
        {
            ["action"] = "effect-morale-return",
            ["afterReturn"] = afterReturn,
        };
        if (extra is not null) foreach (var pair in extra) data[$"return:{pair.Key}"] = pair.Value;
        CreateReturnMoralePrompt(item.Controller, count, "card-effect", item.StackItemId, data, requireActive);
        return true;
    }

    private void ContinueEffectMoraleReturn(L12StackItem item, L12Prompt prompt, IReadOnlyCollection<string> selectedIds)
    {
        var count = int.TryParse(prompt.Data.GetValueOrDefault("count"), out var parsed) ? parsed : 0;
        var requireActive = bool.TryParse(prompt.Data.GetValueOrDefault("requireActive"), out var active) && active;
        if (!ReturnSelectedMoraleById(State.Players[item.Controller], selectedIds, count, requireActive))
        {
            FinishStackItem(item);
            return;
        }
        var extra = prompt.Data.Where(pair => pair.Key.StartsWith("return:", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key[7..], pair => pair.Value);
        CompleteEffectMoraleReturn(item, prompt.Data.GetValueOrDefault("afterReturn") ?? string.Empty, extra);
    }

    private void CompleteEffectMoraleReturn(L12StackItem item, string afterReturn, IReadOnlyDictionary<string, string> data)
    {
        var player = State.Players[item.Controller];
        var source = FindSource(item);
        switch (afterReturn)
        {
            case "lubu-kill":
            case "march-kill":
            case "jingke-kill":
                if (data.GetValueOrDefault("target") is { Length: > 0 } killTarget)
                {
                    var reason = afterReturn switch
                    {
                        "jingke-kill" => "被荆轲击杀",
                        "march-kill" => "被神妙行军击杀",
                        _ => "被吕布效果击杀",
                    };
                    KillTarget(killTarget, reason);
                }
                FinishStackItem(item);
                break;
            case "mulan-charge":
                if (source is not null) source.HasCharge = true;
                FinishStackItem(item);
                break;
            case "wuzetian-lock":
                foreach (var id in (data.GetValueOrDefault("targets") ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries))
                {
                    var lockTarget = FindOnField(State.Players[1 - item.Controller], id, out _, out _);
                    if (lockTarget is not null) lockTarget.CannotUntapUntilRound = State.Round + 1;
                }
                FinishStackItem(item);
                break;
            case "hanxin-attack":
                if (source is not null)
                {
                    source.Troops += 1000;
                    GrantStrongAttack(source);
                }
                FinishStackItem(item);
                break;
            case "guanyu-attack":
                if (source is not null)
                {
                    source.Troops += 1000;
                    source.HasSureHit = true;
                    if (State.PendingDefense is not null) State.PendingDefense.SureHit = true;
                }
                FinishStackItem(item);
                break;
            case "mozi-immortal":
                foreach (var id in (data.GetValueOrDefault("targets") ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries))
                {
                    var target = FindOnField(player, id, out _, out _);
                    if (target is null) continue;
                    target.ImmortalUses = 1;
                    target.ImmortalUntilTurn = ExpiryAtNextOwnStart(item.Controller);
                }
                FinishStackItem(item);
                break;
            case "zhuge-peek":
            {
                if (player.Library.Count == 0) { FinishStackItem(item); break; }
                var top = player.Library[0];
                player.Library.RemoveAt(0);
                AddEvent("reveal", item.Controller, $"诸葛亮展示 {top.Name}", top);
                if (top.CardType == "artifact")
                {
                    player.Resolving.Add(top);
                    item.Data["zhuge-card"] = top.InstanceId;
                    CreatePrompt(item.Controller, "option", "将展示的圣物活跃登场，或加入手牌？", ["play", "hand"], 1, 1,
                        "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "zhuge-artifact" });
                }
                else
                {
                    AddCardToHandByEffect(player, top, "library", $"诸葛亮将{top.Name}加入手牌");
                    FinishStackItem(item);
                }
                break;
            }
            case "empty-city-block":
            {
                var targetStack = State.EffectStack.FirstOrDefault(stack => stack.StackItemId == item.Targets.FirstOrDefault());
                if (targetStack is not null) targetStack.Negated = true;
                if (!player.Field[0].Any(card => card is not null && IsFieldLegion(card))) Draw(player, 1);
                FinishStackItem(item);
                break;
            }
            case "lijing-recruit":
            {
                if (data.GetValueOrDefault("card") is not { Length: > 0 } recruit) { FinishStackItem(item); break; }
                item.Data["revealed"] = recruit;
                var promptData = new Dictionary<string, string>
                {
                    ["action"] = "lijing-slot", ["previewCardId"] = recruit,
                };
                var recruitCard = player.Library.FirstOrDefault(card => card.InstanceId == recruit);
                if (recruitCard is not null) AddPromptCardData(promptData, recruitCard);
                CreatePrompt(item.Controller, "slot", "请直接点击战场上的高亮空位，使展示的军团活跃登场", EmptySlots(player), 1, 1,
                    "card-effect", item.StackItemId, data: promptData);
                break;
            }
            case "liubei-summon":
            {
                if (data.GetValueOrDefault("card") is not { Length: > 0 } summon) { FinishStackItem(item); break; }
                item.Data["summon"] = summon;
                CreatePrompt(item.Controller, "slot", "选择活跃登场位置", EmptySlots(player), 1, 1,
                    "card-effect", item.StackItemId, data: new Dictionary<string, string> { ["action"] = "liubei-slot" });
                break;
            }
            case "palace-kill":
            {
                if (data.GetValueOrDefault("target") is not { Length: > 0 } palaceTarget) { FinishStackItem(item); break; }
                item.Data["target"] = palaceTarget;
                item.Data["paid"] = data.GetValueOrDefault("paid") ?? "0";
                ResolveDeclaredPalaceExchange(item);
                break;
            }
            case "lubu-ready":
                if (source is not null) ReadyCardByEffect(item.Controller, source, source, $"{source.Name}因效果转为活跃");
                FinishStackItem(item);
                break;
            case "free-tactic":
                player.FreeTacticCount++;
                FinishStackItem(item);
                break;
            case "qianyang-draw":
                if (!Draw(player, 1)) SetWinner(1 - item.Controller, "〈乾坤 阳〉抽牌时牌库为空");
                FinishStackItem(item);
                break;
            default:
                FinishStackItem(item);
                break;
        }
    }
}
