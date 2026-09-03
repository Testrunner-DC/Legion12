namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private void RollInitiative()
    {
        int first;
        int second;
        do
        {
            first = _random.Next(1, 7);
            second = _random.Next(1, 7);
        } while (first == second);
        State.InitiativeRolls = [first, second];
        State.DiceWinner = first > second ? 0 : 1;
    }

    private void BuildDisasterPool()
    {
        if (State.DisasterMode == "season"
            || string.Equals(State.OperationsPolicy.DefaultRoomConfig.MatchModeId, "tournament",
                StringComparison.OrdinalIgnoreCase))
        {
            var index = 0;
            foreach (var id in State.OperationsPolicy.DisasterCardIds.Where(id =>
                         !string.Equals(id, L12PlatformStore.AnnihilationCardId,
                             StringComparison.OrdinalIgnoreCase)))
            {
                if (_catalog.Cards.TryGetValue(id, out var card)
                    && string.Equals(card.CardType, "destruction", StringComparison.OrdinalIgnoreCase))
                    State.DisasterPool.Add(CreateCard(id, $"disaster-season-{++index:00}"));
            }
            return;
        }

        for (var number = 1; number <= 9; number++)
        {
            var id = $"S01-DS{number:00}";
            if (_catalog.Cards.ContainsKey(id))
                State.DisasterPool.Add(CreateCard(id, $"disaster-{number:00}"));
        }
        for (var number = 1; number <= 6; number++)
        {
            var id = $"S02-DS{number:00}";
            if (_catalog.Cards.ContainsKey(id))
                State.DisasterPool.Add(CreateCard(id, $"disaster-s2-{number:00}"));
        }
    }

    private void PrepareLibrariesAndHands(bool applyOptionalSetupDefaults = false)
    {
        foreach (var player in State.Players)
        {
            var startingHandSize = 6;
            // skipPreparation 是服务端测试捷径，无人回答开局 Prompt，所以可显式请求默认使用。
            // 真实对局在 BeginOptionalS2Setup 中由玩家回答“可”效果，这里只根据已建立的区域状态计算起始手牌。
            if (applyOptionalSetupDefaults && player.Library.FirstOrDefault(card => card.CardId == "S02-0305") is { } ring)
            {
                player.Library.Remove(ring);
                player.Relic = ring;
                AddEvent("setup", player.PlayerIndex, "将〈安德华拉诺特〉从牌库置入圣物区，起始手牌改为4张", ring);
            }
            if (applyOptionalSetupDefaults && player.MasterId == "S02-03M1"
                && player.Library.FirstOrDefault(card => card.CardId == "S02-0301") is { } hammer)
            {
                player.Library.Remove(hammer);
                player.Hand.Add(hammer);
                AddEvent("setup", player.PlayerIndex, "〈雷神索尔〉将1张〈雷神之锤〉加入起始手牌", hammer);
            }
            if (player.Relic?.CardId == "S02-0305") startingHandSize = 4;
            if (player.MasterId == "S02-03M1" && player.Hand.Any(card => card.CardId == "S02-0301")) startingHandSize--;
            ShuffleLibrary(player, "对局准备");
            Draw(player, startingHandSize);
            if (player.MasterId is "S01-02D1" or "S01-03D1" or "S01-04D1" or "S02-05D1") AddMorale(player, 2);
        }
    }

    private void BeginOptionalS2Setup()
    {
        foreach (var player in State.Players)
        {
            if (player.Library.Any(card => card.CardId == "S02-0305"))
                CreatePrompt(player.PlayerIndex, "optional", "游戏开始时，是否将〈安德华拉诺特〉从牌库置入圣物区？",
                    ["yes", "no"], 1, 1, "setup-s2-ring", isPrivate: true,
                    data: new Dictionary<string, string> { ["yes"] = "置入圣物区，起始手牌为4张", ["no"] = "不发动" });
            if (player.MasterId == "S02-03M1" && player.Library.Any(card => card.CardId == "S02-0301"))
                CreatePrompt(player.PlayerIndex, "optional", "游戏开始时，是否将牌库1张〈雷神之锤〉加入起始手牌？",
                    ["yes", "no"], 1, 1, "setup-s2-thor-hammer", isPrivate: true,
                    data: new Dictionary<string, string> { ["yes"] = "加入起始手牌", ["no"] = "不发动" });
        }
        if (!State.PendingPrompts.Any(prompt => prompt.Continuation.StartsWith("setup-s2-", StringComparison.Ordinal)))
            FinishOptionalS2Setup();
    }

    private void FinishOptionalS2Setup()
    {
        PrepareLibrariesAndHands();
        BeginTrialOrderingOrMulligan();
    }

    private void BeginTrialOrderingOrMulligan()
    {
        foreach (var player in State.Players.Where(candidate => candidate.SpecialZones.Trials.Count > 1 && !candidate.TrialOrderDone))
        {
            var data = new Dictionary<string, string> { ["layout"] = "single-row", ["sourceZone"] = "trial" };
            foreach (var trial in player.SpecialZones.Trials) AddPromptCardData(data, trial);
            CreatePrompt(player.PlayerIndex, "trial-order", "按本局进行顺序依次选择全部试炼",
                player.SpecialZones.Trials.Select(card => card.InstanceId), player.SpecialZones.Trials.Count,
                player.SpecialZones.Trials.Count, "setup-trial-order", isPrivate: true, data: data);
        }
        if (!State.PendingPrompts.Any(item => item.Continuation == "setup-trial-order")) StartMulliganAfterPreparation();
    }

    private L12Prompt CreatePrompt(
        int playerIndex,
        string kind,
        string text,
        IEnumerable<string> choices,
        int min,
        int max,
        string continuation,
        string? stackItemId = null,
        bool isPrivate = true,
        Dictionary<string, string>? data = null)
    {
        var playerText = L12PlayerFacingText.Naturalize(text);
        var validChoices = choices.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        data ??= [];
        var explicitlyDisplayedIds = data.GetValueOrDefault("displayCardIds")?
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        string[] previewCardIds = string.IsNullOrWhiteSpace(data.GetValueOrDefault("previewCardId"))
            ? []
            : [data["previewCardId"]];
        EnrichPromptCardData(playerIndex, validChoices.Concat(explicitlyDisplayedIds).Concat(previewCardIds), data);
        ApplySharedPromptPresentation(playerIndex, kind, playerText, validChoices, stackItemId, data);
        ApplyDirectBoardChoiceMode(validChoices, data);
        var choiceLabels = BuildPlayerChoiceLabels(playerIndex, kind, playerText, validChoices, data);
        var prompt = new L12Prompt
        {
            PromptId = $"prompt-{++State.PromptSequence}",
            PlayerIndex = playerIndex,
            Kind = kind,
            Text = playerText,
            ValidChoices = validChoices,
            MinChoose = min,
            MaxChoose = max,
            Continuation = continuation,
            StackItemId = stackItemId,
            IsPrivate = isPrivate,
            Data = data,
            ChoiceLabels = choiceLabels,
        };
        State.PendingPrompts.Add(prompt);
        AddEvent("prompt", playerIndex, $"等待 {State.Players[playerIndex].Name}：{playerText}");
        return prompt;
    }

    /// <summary>
    /// 所有卡效选择只从这里取得界面语义。卡牌实现只声明“可见对象/合法对象/费用”，
    /// 不再各自决定卡图排列、发动按钮或把内部协议文本交给前端猜测。
    /// </summary>
    private void ApplySharedPromptPresentation(int playerIndex, string kind, string playerText,
        IReadOnlyList<string> validChoices, string? stackItemId, Dictionary<string, string> data)
    {
        var choiceSet = validChoices.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var isDecision = (choiceSet.SetEquals(["yes", "no"])
                || choiceSet.SetEquals(["mode:use", "mode:none"]))
            && kind is "optional" or "option";
        if (isDecision)
        {
            data["uiPattern"] = "effect-decision";
            data["yes"] = "发动";
            data["mode:use"] = "发动";
            data["no"] = "不发动";
            data["mode:none"] = "不发动";

            var stackItem = stackItemId is null ? null : State.EffectStack.Concat(State.DeferredEffectStack)
                .FirstOrDefault(item => item.StackItemId == stackItemId);
            if (stackItem is not null)
            {
                data.TryAdd("sourceName", stackItem.SourceName);
                data.TryAdd("effectText", stackItem.Text);
            }
            else
            {
                var separator = playerText.IndexOf('：');
                if (separator > 0)
                {
                    data.TryAdd("sourceName", playerText[..separator]);
                    data.TryAdd("effectText", playerText[(separator + 1)..]);
                }
            }
        }

        var cardChoiceKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "card", "cards", "hand-card", "hand-cards", "discard", "discard-cost",
            "discard-or-decline", "optional-card", "search", "library-search", "grave-card",
            "opponent-hand-card", "optional-cards", "order", "trial-order", "disaster-ban",
            "disaster-pick",
        };
        var explicitDisplayIds = data.GetValueOrDefault("displayCardIds")?
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var metadataIds = data.Keys
            .Where(key => key.EndsWith(":cardId", StringComparison.OrdinalIgnoreCase))
            .Select(key => key[..^":cardId".Length])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var choiceCardIds = validChoices
            .Where(choice => metadataIds.Contains(choice, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var anonymousCardBackIds = validChoices
            .Where(choice => data.ContainsKey($"{choice}:image")
                && !data.ContainsKey($"{choice}:cardId"))
            .ToArray();
        var isCardChoice = explicitDisplayIds.Length > 0
            || choiceCardIds.Length > 0
            || (cardChoiceKinds.Contains(kind) && anonymousCardBackIds.Length > 0);
        if (!isCardChoice) return;

        data.TryAdd("cardSelection", "true");
        data.TryAdd("layout", "single-row");
        if (kind.Contains("hand", StringComparison.OrdinalIgnoreCase) || kind.StartsWith("discard", StringComparison.OrdinalIgnoreCase))
            data.TryAdd("sourceZone", "hand");

        var displayed = explicitDisplayIds.Concat(choiceCardIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (displayed.Length == 0) displayed = anonymousCardBackIds;
        if (displayed.Length > 0) data["displayCardIds"] = string.Join('|', displayed);
    }

    private static readonly IReadOnlyDictionary<string, string> CommonPlayerChoiceLabels
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["first"] = "选择先攻", ["second"] = "选择后攻",
            ["yes"] = "是", ["no"] = "否", ["agree"] = "同意", ["refuse"] = "不同意",
            ["pass"] = "不响应", ["skip"] = "不发动", ["confirm"] = "确认信息", ["cancel"] = "取消",
            ["top"] = "牌库顶部", ["bottom"] = "牌库底部", ["recruit"] = "活跃登场",
            ["discard"] = "弃置", ["suppress"] = "使其失去效果", ["front"] = "前排", ["back"] = "后排",
            ["single"] = "选择1张", ["all"] = "全部", ["field"] = "战场", ["hand"] = "手牌",
            ["draw"] = "抽取1张牌", ["heal"] = "我方主宰增加1点血量",
            ["normal"] = "普通登场", ["extra"] = "支付额外费用", ["promotion"] = "晋升登场",
            ["rune"] = "获得1枚符文", ["trial"] = "试炼进度+1", ["kill"] = "击杀军团",
            ["recover"] = "回收卡牌", ["free-tactic"] = "主动战术无需消耗费用",
            ["back-master"] = "后排远程军团可进攻主宰",
            ["mode:none"] = "不发动", ["mode:draw"] = "抽取1张牌",
            ["mode:heal"] = "我方主宰增加1点血量", ["mode:move"] = "位移军团",
            ["mode:search"] = "检索牌库", ["mode:recover"] = "回收卡牌",
            ["mode:damage"] = "分配兵力伤害", ["mode:debuff"] = "削弱军团",
            ["mode:buff"] = "消耗1士气，使所选【圆桌骑士】军团本回合兵力+2000", ["mode:trial"] = "试炼进度+1",
            ["mode:rune"] = "获得1枚符文", ["mode:charge"] = "获得冲锋",
            ["mode:mandatory"] = "试炼进度+1",
            ["mode:normal"] = "目标本回合兵力-1000", ["mode:strong"] = "额外休整1张活跃〈陵墓守卫〉，目标本回合兵力-3000",
            ["mode:second"] = "消耗并翻转1神力：选择对方1张军团，本回合兵力-2000", ["mode:use"] = "发动",
            ["mode:all"] = "对全部目标生效",
            ["mode:front"] = "选择前排",
            ["mode:back"] = "选择后排",
            ["mode:single"] = "选择1张军团",
            ["mode:kill"] = "击杀目标军团",
            ["mode:morale"] = "追加休整士气",
            ["mode:mill"] = "弃置牌库顶部2张牌",
            ["mode:discard"] = "盲选并弃置对方1张手牌",
            ["mode:suppress"] = "令目标效果无效并削弱目标军团",
            ["mode:row-cost"] = "选择对方1排并降低费用",
            ["mode:front-attack"] = "本回合我方前排所有【高天原】军团进攻时兵力+1000",
            ["mode:free-move"] = "获得免费前后位移",
            ["mode:grave"] = "从墓地选择公开卡牌",
            ["mode:library"] = "结算时查看牌库并选择",
            ["mode:entry"] = "使军团登场",
            ["mode:revive"] = "从墓地使军团登场",
            ["mode:summon"] = "使已声明军团登场",
            ["mode:shock"] = "所选军团本回合震击伤害+2000",
            ["mode:ranged"] = "所选远程军团本回合进攻时兵力+2000",
            ["row:0"] = "选择前排", ["row:1"] = "选择后排",
            ["pay:god-power"] = "支付神力", ["buff:strong"] = "获得强攻", ["buff:shock"] = "获得震击",
        };

    private Dictionary<string, string> BuildPlayerChoiceLabels(
        int playerIndex,
        string promptKind,
        string promptText,
        IReadOnlyList<string> choices,
        IReadOnlyDictionary<string, string> data)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < choices.Count; index++)
        {
            var choice = choices[index];
            if (data.TryGetValue(choice, out var supplied) && IsNaturalLanguageChoiceLabel(choice, supplied))
            {
                labels[choice] = L12PlayerFacingText.Naturalize(supplied.Trim());
                continue;
            }
            if (CommonPlayerChoiceLabels.TryGetValue(choice, out var common))
            {
                labels[choice] = L12PlayerFacingText.Naturalize(common);
                continue;
            }
            var contextual = ContextualPlayerChoiceLabel(promptKind, promptText, choice);
            if (contextual is not null)
            {
                labels[choice] = L12PlayerFacingText.Naturalize(contextual);
                continue;
            }
            var card = FindPromptCard(playerIndex, choice);
            if (card is not null)
            {
                labels[choice] = L12PlayerFacingText.Naturalize(card.Name);
                continue;
            }
            labels[choice] = L12PlayerFacingText.Naturalize(
                StructuredPlayerChoiceLabel(playerIndex, choice) ?? $"效果选项 {index + 1}");
        }
        return labels;
    }

    private static string? ContextualPlayerChoiceLabel(string promptKind, string promptText, string choice)
    {
        if (choice.Equals("play", StringComparison.OrdinalIgnoreCase))
            return promptText.Contains("登场", StringComparison.Ordinal) ? "活跃登场" : "打出";
        if ((promptKind.Equals("option", StringComparison.OrdinalIgnoreCase)
                || promptKind.Equals("disaster-value", StringComparison.OrdinalIgnoreCase))
            && promptText.Contains("天灾值", StringComparison.Ordinal)
            && int.TryParse(choice, out var delta) && delta is >= -2 and <= 2)
            return delta switch
            {
                < 0 => $"天灾值减少{-delta}点",
                > 0 => $"天灾值增加{delta}点",
                _ => "天灾值不变",
            };
        return null;
    }

    private string? StructuredPlayerChoiceLabel(int playerIndex, string choice)
    {
        if (choice.StartsWith("rune:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(choice.AsSpan(5), out var runeIndex))
            return $"第{runeIndex}枚符文";
        if (choice.StartsWith("battlefield:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(choice.AsSpan("battlefield:".Length), out var battlefield)
            && battlefield >= 0 && battlefield < State.Players.Length)
            return $"{State.Players[battlefield].Name}的战场";
        if (choice.StartsWith("discard:", StringComparison.OrdinalIgnoreCase))
        {
            var card = State.Players[playerIndex].Hand
                .FirstOrDefault(candidate => candidate.InstanceId.Equals(choice["discard:".Length..], StringComparison.OrdinalIgnoreCase));
            return card is null ? "弃置所选手牌" : $"弃置〈{card.Name}〉";
        }
        var slot = choice.Split(':');
        if (slot.Length == 2 && int.TryParse(slot[0], out var row) && int.TryParse(slot[1], out var column)
            && row is >= 0 and < 2 && column is >= 0 and < 3)
            return $"{(row == 0 ? "前排" : "后排")}第{column + 1}格";
        return null;
    }

    private static bool IsNaturalLanguageChoiceLabel(string choice, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals(choice, StringComparison.OrdinalIgnoreCase)) return false;
        var token = value.Trim();
        return token.Any(character => character is >= '\u3400' and <= '\u9fff')
            && !token.StartsWith("mode:", StringComparison.OrdinalIgnoreCase)
            && !token.StartsWith("continuation:", StringComparison.OrdinalIgnoreCase)
            && !token.StartsWith("action:", StringComparison.OrdinalIgnoreCase)
            && !token.StartsWith("prompt-", StringComparison.OrdinalIgnoreCase)
            && !token.StartsWith("activation-", StringComparison.OrdinalIgnoreCase)
            && !token.StartsWith("stack-", StringComparison.OrdinalIgnoreCase);
    }

    private L12Prompt CreateAnonymousHandChoicePrompt(
        int playerIndex,
        IReadOnlyCollection<L12CardInstance> hiddenCards,
        string kind,
        string text,
        int min,
        int max,
        string continuation,
        string? stackItemId = null,
        Dictionary<string, string>? data = null)
    {
        var randomized = hiddenCards.ToList();
        Shuffle(randomized);
        var promptSequence = State.PromptSequence + 1;
        var slots = Enumerable.Range(1, randomized.Count)
            .Select(index => $"hidden-hand-slot-{promptSequence}-{index}")
            .ToArray();
        data ??= [];
        for (var index = 0; index < slots.Length; index++)
        {
            data[slots[index]] = $"对方手牌 {index + 1}";
            data[$"{slots[index]}:image"] = "/assets/l12/card-back-official.png";
        }
        var prompt = CreatePrompt(playerIndex, kind, text, slots, min, max, continuation,
            stackItemId, isPrivate: true, data: data);
        for (var index = 0; index < slots.Length; index++)
            prompt.HiddenChoiceMap[slots[index]] = randomized[index].InstanceId;
        return prompt;
    }

    private static string ResolveHiddenPromptChoice(L12Prompt prompt, string choice)
        => prompt.HiddenChoiceMap.GetValueOrDefault(choice, choice);

    /// <summary>
    /// 所有公开场面与同质资源的选择都在棋盘上直接完成。此规则放在 Prompt 公共入口，
    /// 避免卡效分支分别设置 choiceMode 后再次出现编号弹框或同类效果交互不一致。
    /// 私有区域（手牌、牌库、墓地）仍使用单行卡图选择器。
    /// </summary>
    private void ApplyDirectBoardChoiceMode(IReadOnlyCollection<string> choices, Dictionary<string, string> data)
    {
        if (data.ContainsKey("choiceMode")) return;
        var materialChoices = choices.Where(choice => choice is not ("skip" or "yes" or "no" or "pass")
            && !choice.StartsWith("mode:", StringComparison.OrdinalIgnoreCase)
            && !choice.StartsWith("discard:", StringComparison.OrdinalIgnoreCase)
            && !choice.StartsWith("pay:", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (materialChoices.Length == 0) return;

        var fieldIds = State.Players.SelectMany(player => player.Field.SelectMany(row => row))
            .Where(card => card is not null && !card.Hidden).Select(card => card!.InstanceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var moraleIds = State.Players.SelectMany(player => player.Morale).Select(card => card.InstanceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        static bool IsRune(string choice) => choice.StartsWith("rune:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(choice.AsSpan(5), out var index) && index is >= 1 and <= 3;
        static bool IsSlot(string choice)
        {
            var parts = choice.Split(':');
            return parts.Length == 2 && int.TryParse(parts[0], out var row) && int.TryParse(parts[1], out var slot)
                && row is >= 0 and < 2 && slot is >= 0 and < 3;
        }

        if (materialChoices.All(fieldIds.Contains)) data["choiceMode"] = "board-target";
        else if (materialChoices.All(choice => moraleIds.Contains(choice) || IsRune(choice)))
            data["choiceMode"] = "resource-selection";
        else if (materialChoices.All(choice => fieldIds.Contains(choice) || moraleIds.Contains(choice) || IsRune(choice)))
            data["choiceMode"] = "board-selection";
        else if (materialChoices.All(IsSlot)) data["choiceMode"] = "board-slot";
    }

    private void EnrichPromptCardData(int viewer, IEnumerable<string> choices, Dictionary<string, string> data)
    {
        foreach (var id in choices.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var card = FindPromptCard(viewer, id);
            if (card is null) continue;
            AddPromptCardData(data, card);
        }
    }

    private void AddPromptCardData(Dictionary<string, string> data, L12CardInstance card)
    {
        var id = card.InstanceId;
        data.TryAdd(id, card.Name);
        data.TryAdd($"{id}:name", card.Name);
        if (!string.IsNullOrWhiteSpace(card.ImageUrl)) data.TryAdd($"{id}:image", card.ImageUrl);
        if (!string.IsNullOrWhiteSpace(card.EffectText)) data.TryAdd($"{id}:effect", card.EffectText);
        data.TryAdd($"{id}:cardId", card.CardId);
        data.TryAdd($"{id}:cardType", card.CardType);
        data.TryAdd($"{id}:faction", card.Faction);
        if (PromptCardZone(card) is { } zone) data.TryAdd($"{id}:zone", zone);
        if (card.Traits.Count > 0) data.TryAdd($"{id}:traits", string.Join('|', card.Traits));
        if (!string.IsNullOrWhiteSpace(card.Profession)) data.TryAdd($"{id}:profession", card.Profession);
        data.TryAdd($"{id}:hasPrintedCost", card.HasPrintedCost ? "true" : "false");
        if (card.HasPrintedCost) data.TryAdd($"{id}:cost", card.CurrentCost.ToString());
        data.TryAdd($"{id}:troops", card.Troops.ToString());
        data.TryAdd($"{id}:baseTroops", card.BaseTroops.ToString());
        data.TryAdd($"{id}:disasterLevel", card.DisasterLevel.ToString());
    }

    private string? PromptCardZone(L12CardInstance card)
    {
        foreach (var player in State.Players)
        {
            if (player.Hand.Contains(card)) return "手牌";
            if (player.Library.Contains(card)) return "牌库";
            if (player.Graveyard.Contains(card)) return "墓地";
            if (player.Removed.Contains(card)) return "移除区";
            if (player.Resolving.Contains(card)) return "结算区";
            if (player.Field.SelectMany(row => row).Any(candidate => ReferenceEquals(candidate, card))) return "战场";
            if (ReferenceEquals(player.Relic, card) || player.ExtraRelics.Contains(card)) return "圣物区";
            if (player.SpecialZones.GodPower.Contains(card)) return "神力区";
            if (player.SpecialZones.Trials.Contains(card)) return "试炼区";
            if (player.SpecialZones.CanopicProgress.Contains(card)) return "卡诺匹斯区";
        }
        if (ReferenceEquals(State.ActiveDisaster, card)
            || State.DisasterPool.Contains(card)
            || State.DisasterDeck.Contains(card)
            || State.BannedDisasters.Contains(card)
            || State.RemovedDisasters.Contains(card)
            || State.SelectedDisasters.Contains(card)
            || State.RevealedDisasters.Contains(card)
            || State.ChosenDisasters.Contains(card))
            return "天灾区";
        return null;
    }

    private L12CardInstance? FindPromptCard(int viewer, string instanceId)
    {
        var mine = State.Players[viewer];
        var card = mine.Hand.Concat(mine.Library).Concat(mine.Graveyard).Concat(mine.Removed)
            .Concat(mine.Resolving).Concat(mine.ExtraRelics).Concat(mine.SpecialZones.GodPower)
            .Concat(mine.SpecialZones.Trials).Concat(mine.SpecialZones.CanopicProgress)
            .FirstOrDefault(item => item.InstanceId == instanceId)
            ?? mine.Field.SelectMany(row => row).FirstOrDefault(item => item?.InstanceId == instanceId)
            ?? (mine.Relic?.InstanceId == instanceId ? mine.Relic : null);
        if (card is not null) return card;

        var opponent = State.Players[1 - viewer];
        card = opponent.Field.SelectMany(row => row).FirstOrDefault(item => item?.InstanceId == instanceId)
            ?? opponent.Graveyard.Concat(opponent.Removed).Concat(opponent.Resolving).Concat(opponent.ExtraRelics)
                .FirstOrDefault(item => item.InstanceId == instanceId)
            ?? (opponent.Relic?.InstanceId == instanceId ? opponent.Relic : null);
        if (card is not null && !card.Hidden) return card;

        return State.DisasterPool.Concat(State.DisasterDeck).Concat(State.BannedDisasters)
            .Concat(State.RemovedDisasters).Concat(State.SelectedDisasters).Concat(State.RevealedDisasters)
            .Concat(State.ChosenDisasters)
            .Append(State.ActiveDisaster).FirstOrDefault(item => item?.InstanceId == instanceId);
    }

    private CommandResult ResolvePrompt(int playerIndex, L12Command command)
    {
        if (string.IsNullOrWhiteSpace(command.PromptId)) return CommandResult.Reject("缺少 promptId");
        var prompt = State.PendingPrompts.FirstOrDefault(item => item.PromptId == command.PromptId);
        if (prompt is null) return CommandResult.Reject("选择请求不存在或已结算");
        if (prompt.PlayerIndex != playerIndex) return CommandResult.Reject("不能替其他玩家作出选择");
        var chosen = new List<string>();
        if (prompt.Data.GetValueOrDefault("placementMode") is "split-top-bottom" or "all-top-bottom" or "all-bottom")
        {
            chosen.AddRange(command.TopCardInstanceIds ?? []);
            chosen.AddRange(command.BottomCardInstanceIds ?? []);
            if (chosen.Count != chosen.Distinct().Count()) return CommandResult.Reject("同一张牌不能同时靠顶和靠底");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(command.Choice)) chosen.Add(command.Choice);
            if (command.CardInstanceIds is not null) chosen.AddRange(command.CardInstanceIds);
            chosen = chosen.Distinct().ToList();
        }
        if (chosen.Count < prompt.MinChoose || chosen.Count > prompt.MaxChoose)
            return CommandResult.Reject($"必须选择 {prompt.MinChoose} 至 {prompt.MaxChoose} 项");
        if (chosen.Any(item => !prompt.ValidChoices.Contains(item)))
            return CommandResult.Reject("包含无效选项");
        if (prompt.Data.GetValueOrDefault("selectionConstraint") == "distinct-card-names")
        {
            var selectedCards = chosen.Select(id => FindPromptCard(playerIndex, id)).ToArray();
            if (selectedCards.Any(card => card is null)
                || selectedCards.Select(card => card!.Name).Distinct(StringComparer.Ordinal).Count() != selectedCards.Length)
                return CommandResult.Reject("选择的卡牌必须为非同名卡牌");
        }

        State.PendingPrompts.Remove(prompt);
        AddEvent("prompt-resolved", playerIndex, $"{State.Players[playerIndex].Name} 已完成选择");

        switch (prompt.Continuation)
        {
            case "setup-initiative":
                ResolveInitiativeChoice(playerIndex, chosen[0]);
                break;
            case "setup-ban":
                ResolveDisasterBan(playerIndex, chosen[0]);
                break;
            case "setup-public-confirm":
                if (!State.PendingPrompts.Any(item => item.Continuation == "setup-public-confirm"))
                {
                    State.DisasterPreparationStep++;
                    ContinueDisasterPreparation();
                }
                break;
            case "setup-first-pick":
            case "setup-second-pick":
                ResolveDisasterPick(playerIndex, chosen[0], prompt);
                break;
            case "setup-trial-order":
            {
                var player = State.Players[playerIndex];
                var ordered = chosen.Select(id => player.SpecialZones.Trials.FirstOrDefault(card => card.InstanceId == id)).ToArray();
                if (ordered.Any(card => card is null) || ordered.Length != player.SpecialZones.Trials.Count)
                    return CommandResult.Reject("必须为全部试炼确定进行顺序");
                player.SpecialZones.Trials.Clear();
                player.SpecialZones.Trials.AddRange(ordered!);
                player.TrialOrderDone = true;
                AddEvent("trial-order", playerIndex, $"{player.Name} 已确定试炼顺序");
                if (!State.PendingPrompts.Any(item => item.Continuation == "setup-trial-order")) StartMulliganAfterPreparation();
                break;
            }
            case "setup-s2-ring":
            {
                var player = State.Players[playerIndex];
                if (chosen[0] == "yes" && player.Library.FirstOrDefault(card => card.CardId == "S02-0305") is { } ring)
                {
                    player.Library.Remove(ring);
                    player.Relic = ring;
                    AddEvent("setup", playerIndex, "将〈安德华拉诺特〉从牌库置入圣物区，起始手牌改为4张", ring);
                }
                if (!State.PendingPrompts.Any(item => item.Continuation.StartsWith("setup-s2-", StringComparison.Ordinal)))
                    FinishOptionalS2Setup();
                break;
            }
            case "setup-s2-thor-hammer":
            {
                var player = State.Players[playerIndex];
                if (chosen[0] == "yes" && player.Library.FirstOrDefault(card => card.CardId == "S02-0301") is { } hammer)
                {
                    player.Library.Remove(hammer);
                    player.Hand.Add(hammer);
                    AddEvent("reveal", playerIndex, $"{player.Name}展示卡牌〈雷神之锤〉", hammer);
                    AddEvent("setup", playerIndex, "〈雷神索尔〉将1张〈雷神之锤〉加入起始手牌", hammer);
                }
                if (!State.PendingPrompts.Any(item => item.Continuation.StartsWith("setup-s2-", StringComparison.Ordinal)))
                    FinishOptionalS2Setup();
                break;
            }
            case "s2-ring-end-discard":
            {
                var player = State.Players[playerIndex];
                foreach (var id in chosen) MoveHandToGrave(player, id, causedByEffect: false);
                State.Phase = L12Phase.End;
                AddEvent("phase", playerIndex, "执行结束阶段");
                if (DisastersEnabled) ResolveEndPhaseDisasterEffect(playerIndex);
                if (State.PendingPrompts.Count == 0 && State.EffectStack.Count == 0) CompleteEndTurn(playerIndex);
                break;
            }
            case "disaster-trigger-confirm":
                if (!State.PendingPrompts.Any(item => item.Continuation == "disaster-trigger-confirm"))
                {
                    var disaster = State.ActiveDisaster;
                    if (disaster is not null)
                        PushEffect(State.ActivePlayer, disaster, "disaster", "天灾触发效果",
                            data: new Dictionary<string, string>
                            {
                                ["opening"] = prompt.Data.GetValueOrDefault("opening", "false")
                            });
                }
                break;
            case "s2-prayer-public-confirm":
                if (!State.PendingPrompts.Any(candidate => candidate.Continuation == "s2-prayer-public-confirm"
                    && candidate.StackItemId == prompt.StackItemId))
                {
                    var prayerItem = State.EffectStack.FirstOrDefault(candidate => candidate.StackItemId == prompt.StackItemId);
                    if (prayerItem is not null) FinishStackItem(prayerItem);
                }
                break;
            case "stack-response":
                ResolveStackResponse(playerIndex, prompt, chosen[0]);
                break;
            case "stack-response-discard":
                ResolveAbsoluteDefenseDiscard(playerIndex, prompt, chosen[0]);
                break;
            case "stack-response-puppet-slot":
                ResolvePuppetResponseSlot(playerIndex, prompt, chosen[0]);
                break;
            case "combat-lethal-replacement":
                ResolveCombatLethalReplacement(playerIndex, prompt, chosen[0]);
                break;
            case "effect-lethal-replacement":
                ResolveEffectLethalReplacement(playerIndex, prompt, chosen[0]);
                break;
            case "faith-zealot-post-resolution":
                ResolveFaithZealotPostResolutionChoice(prompt, chosen[0]);
                break;
            case "card-effect":
            case "disaster-effect":
            case "active-ability":
                ContinueCardEffect(prompt, chosen, command);
                break;
            case "pending-activation":
                ResolvePendingActivation(prompt, chosen);
                break;
            case "play-cost-choice":
            {
                var result = ResolvePlayCostChoice(prompt, chosen[0]);
                if (!result.Accepted) return result;
                break;
            }
            case "play-morale-choice":
            {
                var result = ResolveTombGuardPlayPaymentChoice(prompt, chosen);
                if (!result.Accepted) return result;
                break;
            }
            case "active-morale-choice":
            {
                var result = ResolveTombGuardActivePaymentChoice(prompt, chosen);
                if (!result.Accepted) return result;
                break;
            }
            case "active-return-choice":
            {
                var result = ResolveActiveReturnMoraleChoice(prompt, chosen);
                if (!result.Accepted) return result;
                break;
            }
            case "move-morale-choice":
            {
                var result = ResolveMoveResourcePayment(prompt, chosen);
                if (!result.Accepted) return result;
                break;
            }
            case "s2-mistletoe-rune-cost":
            {
                var result = PlayCard(prompt.PlayerIndex, new L12Command(
                    "playCard", CardInstanceId: prompt.Data.GetValueOrDefault("cardInstanceId"), Choice: $"runes:{chosen.Count}",
                    Target: new L12AttackTarget("legion", prompt.Data.GetValueOrDefault("targetInstanceId"))));
                if (!result.Accepted) return result;
                break;
            }
            case "s2-rollo-grave-cost":
            {
                int? row = int.TryParse(prompt.Data.GetValueOrDefault("row"), out var parsedRow) ? parsedRow : null;
                int? slot = int.TryParse(prompt.Data.GetValueOrDefault("slot"), out var parsedSlot) ? parsedSlot : null;
                var result = PlayCard(prompt.PlayerIndex, new L12Command("playCard",
                    CardInstanceId: prompt.Data.GetValueOrDefault("cardInstanceId"), Row: row, Slot: slot,
                    Choice: $"rollo:{string.Join(',', chosen)}",
                    TargetPlayerIndex: int.TryParse(prompt.Data.GetValueOrDefault("targetPlayerIndex"), out var targetPlayerIndex)
                        ? targetPlayerIndex : null));
                if (!result.Accepted) return result;
                break;
            }
            case "s2-yingzheng-enter-cost":
            {
                var result = ResolveYingzhengEnterCost(prompt, chosen[0]);
                if (!result.Accepted) return result;
                break;
            }
            case "s2-promotion-foundation":
            {
                var result = PlayCard(prompt.PlayerIndex, new L12Command(
                    "playCard", CardInstanceId: prompt.Data.GetValueOrDefault("cardInstanceId"),
                    Choice: $"promotion:{chosen[0]}"));
                if (!result.Accepted) return result;
                break;
            }
            case "s2-promotion-mode":
            {
                if (chosen[0] == "cancel") break;
                int? row = int.TryParse(prompt.Data.GetValueOrDefault("row"), out var parsedRow) ? parsedRow : null;
                int? slot = int.TryParse(prompt.Data.GetValueOrDefault("slot"), out var parsedSlot) ? parsedSlot : null;
                var result = PlayCard(prompt.PlayerIndex, new L12Command(
                    "playCard", CardInstanceId: prompt.Data.GetValueOrDefault("cardInstanceId"), Row: row, Slot: slot,
                    Choice: chosen[0] == "normal" ? "normal-entry" : "promotion-mode"));
                if (!result.Accepted) return result;
                break;
            }
            case "trigger-batch-order":
                ResolveTriggerBatchOrder(prompt, chosen);
                break;
            case "end-disaster-hand":
                ContinueEndDisasterHand(prompt, chosen);
                break;
            default:
                return CommandResult.Reject("未知选择续接点");
        }
        AdvanceCombatTimelineIfIdle();
        return CommandResult.Ok();
    }

    private CommandResult ResolvePlayCostChoice(L12Prompt prompt, string choice)
    {
        if (!int.TryParse(prompt.Data.GetValueOrDefault("row"), out var row)
            || !int.TryParse(prompt.Data.GetValueOrDefault("slot"), out var slot))
            return CommandResult.Reject("登场位置数据无效");
        return PlayCard(prompt.PlayerIndex, new L12Command(
            "playCard",
            CardInstanceId: prompt.Data.GetValueOrDefault("cardInstanceId"),
            Row: row,
            Slot: slot,
            Choice: choice == "yes" ? "self-damage-cost" : "normal-cost",
            TargetPlayerIndex: int.TryParse(prompt.Data.GetValueOrDefault("targetPlayerIndex"), out var targetPlayerIndex) ? targetPlayerIndex : null));
    }

    private CommandResult ResolveTombGuardPlayPaymentChoice(L12Prompt prompt, List<string> chosen)
    {
        int? row = int.TryParse(prompt.Data.GetValueOrDefault("row"), out var parsedRow) ? parsedRow : null;
        int? slot = int.TryParse(prompt.Data.GetValueOrDefault("slot"), out var parsedSlot) ? parsedSlot : null;
        var baseChoice = prompt.Data.GetValueOrDefault("baseChoice", "normal-cost");
        return PlayCard(prompt.PlayerIndex, new L12Command(
            "playCard",
            CardInstanceId: prompt.Data.GetValueOrDefault("cardInstanceId"),
            Row: row,
            Slot: slot,
            Choice: baseChoice,
            CardInstanceIds: chosen,
            Target: string.IsNullOrWhiteSpace(prompt.Data.GetValueOrDefault("targetInstanceId"))
                ? null : new L12AttackTarget("legion", prompt.Data.GetValueOrDefault("targetInstanceId")),
            TargetPlayerIndex: int.TryParse(prompt.Data.GetValueOrDefault("targetPlayerIndex"), out var targetPlayerIndex) ? targetPlayerIndex : null));
    }

    private CommandResult ResolveMoveResourcePayment(L12Prompt prompt, List<string> chosen)
    {
        if (!int.TryParse(prompt.Data.GetValueOrDefault("row"), out var row)
            || !int.TryParse(prompt.Data.GetValueOrDefault("slot"), out var slot))
            return CommandResult.Reject("位移位置数据无效");
        return Move(prompt.PlayerIndex, new L12Command("move",
            CardInstanceId: prompt.Data.GetValueOrDefault("cardInstanceId"), Row: row, Slot: slot,
            CardInstanceIds: chosen));
    }

    private CommandResult ResolveTombGuardActivePaymentChoice(L12Prompt prompt, List<string> chosen)
    {
        var player = State.Players[prompt.PlayerIndex];
        var sourceId = prompt.Data.GetValueOrDefault("sourceId") ?? string.Empty;
        var source = FindOnField(player, sourceId, out _, out _)
            ?? (player.Relic?.InstanceId == sourceId ? player.Relic : null)
            ?? player.ExtraRelics.FirstOrDefault(card => card.InstanceId == sourceId)
            ?? (prompt.Data.GetValueOrDefault("sourceCardId") == player.MasterId ? CreateActiveMasterSource(player, sourceId) : null)
            ?? (sourceId == $"faction-{prompt.PlayerIndex}" ? CreateCard(prompt.Data.GetValueOrDefault("sourceCardId") ?? string.Empty, sourceId) : null);
        if (source is null) return CommandResult.Reject("主动效果来源已不在合法区域");
        var returnIds = (prompt.Data.GetValueOrDefault("returnIds") ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries);
        return CommitActiveAbility(prompt.PlayerIndex, source, prompt.Data.GetValueOrDefault("ability") ?? string.Empty,
            prompt.Data.GetValueOrDefault("target"), selectedResourceIds: chosen,
            selectedReturnIds: returnIds.Length == 0 ? null : returnIds);
    }

    private CommandResult ResolveActiveReturnMoraleChoice(L12Prompt prompt, List<string> chosen)
    {
        var player = State.Players[prompt.PlayerIndex];
        var sourceId = prompt.Data.GetValueOrDefault("sourceId") ?? string.Empty;
        var source = FindOnField(player, sourceId, out _, out _)
            ?? (player.Relic?.InstanceId == sourceId ? player.Relic : null)
            ?? player.ExtraRelics.FirstOrDefault(card => card.InstanceId == sourceId)
            ?? (prompt.Data.GetValueOrDefault("sourceCardId") == player.MasterId ? CreateActiveMasterSource(player, sourceId) : null)
            ?? (sourceId == $"faction-{prompt.PlayerIndex}" ? CreateCard(prompt.Data.GetValueOrDefault("sourceCardId") ?? string.Empty, sourceId) : null);
        if (source is null) return CommandResult.Reject("主动效果来源已不在合法区域");
        return CommitActiveAbility(prompt.PlayerIndex, source, prompt.Data.GetValueOrDefault("ability") ?? string.Empty,
            prompt.Data.GetValueOrDefault("target"), selectedReturnIds: chosen);
    }

    private void ResolveInitiativeChoice(int playerIndex, string choice)
    {
        State.FirstPlayer = choice == "first" ? playerIndex : 1 - playerIndex;
        State.ActivePlayer = State.FirstPlayer;
        AddEvent("initiative-choice", playerIndex,
            $"{State.Players[playerIndex].Name} 选择{(choice == "first" ? "先攻" : "后攻")}；{State.Players[State.FirstPlayer].Name} 为先攻玩家");
        if (State.DisasterMode == "none")
        {
            State.Phase = L12Phase.DisasterPreparation;
            SetDisasterValue(0);
            PrepareAfterDisasterSelection("本局不使用天灾，天灾值始终为 0");
            return;
        }
        if (State.DisasterMode == "random")
        {
            State.Phase = L12Phase.DisasterPreparation;
            BuildRandomDisasterDeck();
            PrepareAfterDisasterSelection("已随机建立本局天灾牌库；〈堙灭〉固定置于最底部");
            return;
        }
        State.Phase = L12Phase.DisasterPreparation;
        State.DisasterPreparationStep = 0;
        ContinueDisasterPreparation();
    }

    private void BuildRandomDisasterDeck()
    {
        var normal = State.DisasterPool.Where(card => card.CardId != "S01-DS10")
            .OrderBy(_ => _random.Next()).Take(3).ToList();
        Shuffle(normal);
        State.DisasterDeck.Clear();
        State.DisasterDeck.AddRange(normal);
        State.DisasterDeck.Add(CreateCard("S01-DS10", "disaster-final"));
        AddEvent("shuffle", null, "随机模式洗切天灾牌库，〈堙灭〉固定置于最底部");
        State.DisasterPool.Clear();
        SetDisasterValue(0);
    }

    private void ContinueDisasterPreparation()
    {
        var first = State.FirstPlayer;
        var second = 1 - first;
        switch (State.DisasterPreparationStep)
        {
            case 0:
                PromptDisasterBan(first, "先攻玩家禁用第 1 张天灾");
                break;
            case 1:
                PromptDisasterBan(second, "后攻玩家禁用 1 张天灾");
                break;
            case 2:
                PromptDisasterBan(first, "先攻玩家禁用第 2 张天灾");
                break;
            case 3:
            {
                var available = State.DisasterPool.Where(card => !State.BannedDisasters.Contains(card)).ToList();
                var publicCard = available[_random.Next(available.Count)];
                State.DisasterPool.Remove(publicCard);
                State.SelectedDisasters.Add(publicCard);
                State.RevealedDisasters.Add(publicCard);
                AddEvent("disaster-public", null, $"随机公开天灾〈{publicCard.Name}〉", publicCard);
                var data = new Dictionary<string, string>
                {
                    ["previewCardId"] = publicCard.InstanceId,
                    ["previewPresentation"] = "information-card",
                };
                AddPromptCardData(data, publicCard);
                CreatePrompt(first, "disaster-reveal", $"随机公开天灾〈{publicCard.Name}〉", [], 0, 0,
                    "setup-public-confirm", isPrivate: false, data: new Dictionary<string, string>(data));
                CreatePrompt(second, "disaster-reveal", $"随机公开天灾〈{publicCard.Name}〉", [], 0, 0,
                    "setup-public-confirm", isPrivate: false, data: new Dictionary<string, string>(data));
                break;
            }
            case 4:
                PromptDisasterPick(first, 3, "先攻玩家从 3 张候选天灾中选择 1 张", "setup-first-pick");
                break;
            case 5:
                PromptDisasterPick(second, 2, "后攻玩家从 2 张候选天灾中选择 1 张", "setup-second-pick");
                break;
            default:
                FinishDisasterPreparation();
                break;
        }
    }

    private void PromptDisasterBan(int playerIndex, string text)
    {
        var data = new Dictionary<string, string>();
        foreach (var card in State.DisasterPool)
        {
            data[card.InstanceId] = card.Name;
            if (!string.IsNullOrWhiteSpace(card.ImageUrl)) data[$"{card.InstanceId}:image"] = card.ImageUrl;
        }
        CreatePrompt(playerIndex, "disaster-ban", text,
            State.DisasterPool.Select(card => card.InstanceId), 1, 1, "setup-ban", isPrivate: false,
            data: data);
    }

    private void ResolveDisasterBan(int playerIndex, string instanceId)
    {
        var card = State.DisasterPool.First(card => card.InstanceId == instanceId);
        State.DisasterPool.Remove(card);
        State.BannedDisasters.Add(card);
        AddEvent("disaster-banned", playerIndex, $"{State.Players[playerIndex].Name} 禁用〈{card.Name}〉", card);
        State.DisasterPreparationStep++;
        ContinueDisasterPreparation();
    }

    private void PromptDisasterPick(int playerIndex, int count, string text, string continuation)
    {
        var candidates = State.DisasterPool.OrderBy(_ => _random.Next()).Take(count).ToArray();
        var data = new Dictionary<string, string>();
        foreach (var card in candidates)
        {
            data[card.InstanceId] = card.Name;
            if (!string.IsNullOrWhiteSpace(card.ImageUrl)) data[$"{card.InstanceId}:image"] = card.ImageUrl;
        }
        CreatePrompt(playerIndex, "disaster-pick", text, candidates.Select(card => card.InstanceId),
            1, 1, continuation, isPrivate: true,
            data: data);
    }

    private void ResolveDisasterPick(int playerIndex, string instanceId, L12Prompt prompt)
    {
        var card = State.DisasterPool.First(item => item.InstanceId == instanceId);
        // 本步骤中随机抽到的候选牌全部离开后续候选池；未选择的牌也不能被下一位玩家抽到。
        State.DisasterPool.RemoveAll(item => prompt.ValidChoices.Contains(item.InstanceId));
        State.SelectedDisasters.Add(card);
        State.ChosenDisasters.Add(card);
        State.ChosenDisasterOwners[card.InstanceId] = playerIndex;
        card.OwnerIndex = playerIndex;
        AddEvent("disaster-selected", playerIndex, $"{State.Players[playerIndex].Name} 选择〈{card.Name}〉", card);
        State.DisasterPreparationStep++;
        ContinueDisasterPreparation();
    }

    private void FinishDisasterPreparation()
    {
        Shuffle(State.SelectedDisasters);
        State.DisasterDeck.AddRange(State.SelectedDisasters);
        State.DisasterDeck.Add(CreateCard("S01-DS10", "disaster-final"));
        AddEvent("shuffle", null, "洗切双方选定的天灾，〈堙灭〉固定置于最底部");
        State.DisasterPool.Clear();
        PrepareAfterDisasterSelection("本局 4 张天灾牌库已组成，〈堙灭〉位于牌库底部");
    }

    private void PrepareAfterDisasterSelection(string eventText)
    {
        SetDisasterValue(0);
        AddEvent("disaster-deck-ready", null, eventText);
        BeginOptionalS2Setup();
    }

    private void StartMulliganAfterPreparation()
    {
        State.Phase = L12Phase.Mulligan;
        AddEvent("mulligan-start", null, "双方同时进行调度");
    }

    private L12StackItem PushEffect(int controller, L12CardInstance source, string trigger, string text,
        IEnumerable<string>? targets = null, Dictionary<string, string>? data = null)
    {
        var sourceAbilities = GetAbilities(source.CardId);
        if (trigger == "active" && State.ActiveDisaster?.CardId == "S02-DS03"
            && sourceAbilities.Any(ability => ability.Id == data?.GetValueOrDefault("ability")
                && (ability.Label.Contains("主动休整", StringComparison.Ordinal)
                    || sourceAbilities.Count == 1
                    && L12StructuredCardRules.HasActiveRestAbility(source.CardId))))
        {
            DamageMasterNonLethal(controller, 1, "〈无眠之夜〉的持续效果", neutralSource: true);
        }
        // 〈虚构的圣杯〉监听“圣物效果发动”这一公共事件，而不是只挂在主动效果按钮上。
        // PushEffect 是登场时、主动、触发式圣物效果共同经过的唯一入口；持续效果不会入栈，
        // 因而不会在这里被误计为一次发动。
        if (State.ActiveDisaster?.CardId == "S01-DS08" && source.CardType == "artifact"
            && trigger is not "disaster")
        {
            DamageMasterNonLethal(controller, 1, "〈虚构的圣杯〉：发动圣物效果", neutralSource: true);
        }
        var item = new L12StackItem
        {
            StackItemId = $"stack-{++State.StackSequence}",
            Controller = controller,
            SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId,
            SourceName = source.Name,
            Trigger = trigger,
            Text = text,
            // Repeated effect-only copies deliberately have no authoritative zone card.
            // Preserve their last-known data without exposing the internal zone lookup
            // through this prompt/stack boundary or moving a virtual card into a zone.
            SourceSnapshot = data?.GetValueOrDefault("repeatedEffectOnly") == "true"
                ? CaptureLastKnownSourceSnapshot(source)
                : null,
        };
        if (targets is not null) item.Targets.AddRange(targets);
        if (data is not null)
            foreach (var pair in data) item.Data[pair.Key] = pair.Value;
        if (trigger == "disaster") item.Data["unrespondable"] = "true";
        if (trigger is "active" or "play")
            PublishEffectPresentation("effect-activation", controller, source, trigger, text, item.Data);
        else if (IsDirectTriggeredEffect(trigger, source, text))
            PublishEffectPresentation("effect-trigger", controller, source, trigger, text, item.Data);
        if (State.IsResolvingStack)
        {
            State.DeferredEffectStack.Add(item);
            AddEvent("stack-deferred", controller, $"〈{source.Name}〉的{text}将在当前堆叠关闭后开启新堆叠", source);
        }
        else
        {
            State.EffectStack.Add(item);
            AddEvent("stack-push", controller, $"〈{source.Name}〉的{text}进入堆叠", source);
            BeginStackItem(item);
        }
        return item;
    }

    private void BeginStackItem(L12StackItem item)
    {
        if (item.Data.GetValueOrDefault("unrespondable") == "true")
        {
            State.ResponseWindow = null;
            State.IsResolvingStack = true;
            ResolveTopStack();
            return;
        }
        BeginResponseWindow(item);
    }

    private void BeginResponseWindow(L12StackItem item)
    {
        State.ResponseWindow = new L12ResponseWindow
        {
            PriorityPlayer = State.ActivePlayer,
            ConsecutivePasses = 0,
        };
        OfferResponse();
    }

    private void OfferResponse()
    {
        if (State.ResponseWindow is null || State.EffectStack.Count == 0) return;
        var playerIndex = State.ResponseWindow.PriorityPlayer;
        var top = State.EffectStack[^1];
        var timing = ResponseTimingContext(top);
        var player = State.Players[playerIndex];
        var choices = new List<string>();
        var protectedFromCounters = top.Controller != playerIndex && IsProtectedFromCounterTactics(top);
        var defenderAttackTimingRoot = top.Trigger == "opponent-attack";
        var defendingPlayer = State.PendingDefense is null ? -1 : 1 - State.PendingDefense.AttackerPlayer;
        var responseCards = player.Field[1].Where(card => card is { CardType: "tactic" }
            && card.CannotRespondUntilRound < State.Round).Cast<L12CardInstance>().ToArray();
        if (State.TurnSerial < State.CounterTacticsDisabledUntilTurnSerial || protectedFromCounters) responseCards = [];
        foreach (var card in responseCards)
        {
            if (card.CardId == "S01-0016" && top.Controller != playerIndex && top.Trigger != "authority-event"
                && player.Hand.Count > 0 && (!defenderAttackTimingRoot || playerIndex == defendingPlayer))
                choices.Add(card.InstanceId);
            // 〈落穴陷阱〉只响应“军团登场时”。军团与圣物共用 enter 堆叠时点，
            // 因此不能只判断触发名；否则圣物的【登场时】效果也会错误开放响应。
            if (!defenderAttackTimingRoot && card.CardId == "S01-0018" && top.Controller != playerIndex && timing.Trigger == "enter"
                && FindSource(timing) is { } enteredCard && IsFieldLegion(enteredCard))
                choices.Add(card.InstanceId);
            if (CanUseS1ReactionAtStack(card.CardId, playerIndex, top)) choices.Add(card.InstanceId);
            if (!defenderAttackTimingRoot && CanUseS2CounterAtStack(card.CardId, playerIndex, top)) choices.Add(card.InstanceId);
        }
        if (!protectedFromCounters && top.Trigger == "opponent-attack" && State.PendingDefense?.Target.Type == "legion"
            && State.PendingDefense.SureHit != true && playerIndex == defendingPlayer)
            choices.AddRange(player.Hand.Where(card => card.CardId == "S01-0002").Select(card => card.InstanceId));
        if (!protectedFromCounters && top.Trigger == "opponent-attack" && State.PendingDefense?.Target.Type == "master"
            && playerIndex == defendingPlayer
            && Enumerable.Range(0, 3).Any(slot => player.Field[0][slot] is null))
            choices.AddRange(player.Hand.Where(card => card.CardId == "S02-0005").Select(card => card.InstanceId));
        var hasAnonymousPoolResponse = _concealHiddenResponseAvailability
            && CanMasterCardPoolRespondAtTiming(playerIndex, top, protectedFromCounters);
        if (_autoPassEmptyResponses && choices.Count == 0 && !hasAnonymousPoolResponse)
        {
            PassPriority(playerIndex);
            return;
        }
        choices.Add("pass");
        var responseData = choices.Where(choice => choice != "pass")
            .Select(id => responseCards.FirstOrDefault(card => card.InstanceId == id)
                ?? player.Hand.First(card => card.InstanceId == id))
            .ToDictionary(card => card.InstanceId, card => card.Name);
        responseData["choiceMode"] = "instant";
        CreatePrompt(playerIndex, "response", $"是否响应堆叠顶部：{top.SourceName}－{top.Text}", choices,
            1, 1, "stack-response", top.StackItemId, isPrivate: true, data: responseData);
    }

    /// <summary>
    /// 只使用主宰构筑可用卡池和公开场面判断“是否存在理论响应”。
    /// 隐藏手牌/盖牌的真实身份只能影响响应窗口中的实际选项，不能影响窗口是否出现。
    /// </summary>
    private bool CanMasterCardPoolRespondAtTiming(int playerIndex, L12StackItem top, bool protectedFromCounters)
    {
        if (top.Controller == playerIndex || protectedFromCounters) return false;
        var player = State.Players[playerIndex];
        var timing = ResponseTimingContext(top);
        var pool = _catalog.Cards.Values.Where(card =>
            card.Faction == "universal" || card.Faction == player.Faction);

        // 盖伏区数量和禁用状态均为公开场面信息；牌的真实身份不是。同回合盖伏可以立即响应。
        var hasEligibleCoveredCard = State.TurnSerial >= State.CounterTacticsDisabledUntilTurnSerial
            && player.Field[1].Any(card => card is { Hidden: true, CardType: "tactic" }
                && card.CannotRespondUntilRound < State.Round);
        if (hasEligibleCoveredCard && pool.Any(card => IsPoolCounterResponseAtTiming(card.Id, playerIndex, top, timing)))
            return true;

        var defendingPlayer = State.PendingDefense is null ? -1 : 1 - State.PendingDefense.AttackerPlayer;
        if (playerIndex != defendingPlayer || player.Hand.Count == 0 || top.Trigger != "opponent-attack") return false;
        if (State.PendingDefense?.Target.Type == "legion" && State.PendingDefense.SureHit != true
            && pool.Any(card => card.Id == "S01-0002"))
            return true;
        return State.PendingDefense?.Target.Type == "master"
            && Enumerable.Range(0, 3).Any(slot => player.Field[0][slot] is null)
            && pool.Any(card => card.Id == "S02-0005");
    }

    private bool IsPoolCounterResponseAtTiming(
        string cardId,
        int playerIndex,
        L12StackItem top,
        L12StackItem timing)
    {
        if (!IsCounterTactic(cardId)) return false;
        if (top.Trigger == "opponent-attack")
        {
            var defendingPlayer = State.PendingDefense is null ? -1 : 1 - State.PendingDefense.AttackerPlayer;
            if (cardId == "S01-0016")
                return playerIndex == defendingPlayer && State.Players[playerIndex].Hand.Count > 0;
            return CanUseS1ReactionAtStack(cardId, playerIndex, top);
        }
        if (cardId == "S01-0016")
            return top.Trigger != "authority-event" && State.Players[playerIndex].Hand.Count > 0;
        if (cardId == "S01-0018")
            return timing.Trigger == "enter" && FindSource(timing) is { } enteredCard && IsFieldLegion(enteredCard);
        return CanUseS1ReactionAtStack(cardId, playerIndex, top)
            || CanUseS2CounterAtStack(cardId, playerIndex, top);
    }

    private L12StackItem ResponseTimingContext(L12StackItem top)
    {
        var current = top;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (current.Trigger is "reaction" or "s2-reaction" or "response-negate" or "response-block" or "response-retarget-master")
        {
            if (!visited.Add(current.StackItemId)) break;
            var targetId = current.Targets.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(targetId)) break;
            var target = State.EffectStack.FirstOrDefault(candidate => candidate.StackItemId == targetId);
            if (target is null) break;
            current = target;
        }
        return current;
    }

    private bool IsProtectedFromCounterTactics(L12StackItem top)
    {
        if (top.Trigger == "disaster"
            || top.Data.GetValueOrDefault("inheritedCounterTacticProtection") == "true") return true;
        var source = FindSource(top);
        return source is not null
            && L12StructuredCardRules.HasSummonTurnCounterTacticProtection(source, State.Round);
    }

    private void ResolveStackResponse(int playerIndex, L12Prompt prompt, string choice)
    {
        if (choice == "pass")
        {
            PassPriority(playerIndex);
            return;
        }
        var player = State.Players[playerIndex];
        var response = FindOnField(player, choice, out _, out _)
            ?? player.Hand.FirstOrDefault(card => card.InstanceId == choice);
        if (response is null) { PassPriority(playerIndex); return; }
        if (response.CardId == "S01-0002")
        {
            CommitMercenaryResponse(playerIndex, response, prompt.StackItemId!);
            return;
        }
        if (response.CardId == "S02-0005")
        {
            var frontSlots = Enumerable.Range(0, 3)
                .Where(slot => player.Field[0][slot] is null)
                .Select(slot => $"0:{slot}")
                .ToArray();
            if (frontSlots.Length == 0) { PassPriority(playerIndex); return; }
            var choices = frontSlots.Append("cancel").ToArray();
            CreatePrompt(playerIndex, "slot", $"{response.Name}：预先选择休整登场的前排位置", choices,
                1, 1, "stack-response-puppet-slot", prompt.StackItemId, isPrivate: true,
                data: new Dictionary<string, string>
                {
                    ["responseId"] = response.InstanceId,
                    ["choiceMode"] = "board-slot",
                    ["cancel"] = "取消发动",
                });
            return;
        }
        if (response.CardId == "S01-0016")
        {
            var discards = player.Hand.Select(card => card.InstanceId).ToArray();
            CreatePrompt(playerIndex, "discard-cost", "弃置 1 张手牌作为〈绝对防御〉的费用", discards,
                1, 1, "stack-response-discard", prompt.StackItemId, isPrivate: true,
                data: new Dictionary<string, string> { ["responseId"] = response.InstanceId });
            return;
        }
        if (L12StructuredCardRules.RequiresOwnLegionResponseTarget(response.CardId))
        {
            var targets = PublicLegions(player).Select(card => card.InstanceId).ToArray();
            if (targets.Length == 0) { PassPriority(playerIndex); return; }
            BeginPendingResponseActivation(playerIndex, response, prompt.StackItemId!, targets,
                "伏击：预先选择我方1张军团，本回合兵力+2000");
            return;
        }
        if (TryBeginPublicResponseDeclaration(playerIndex, response, prompt.StackItemId!))
            return;
        if (response.CardId == "S01-0224")
        {
            var target = State.EffectStack.First(item => item.StackItemId == prompt.StackItemId);
            CommitS1ReactionResponse(playerIndex, response, prompt.StackItemId!);
            return;
        }
        if (response.CardId is "S02-0015" or "S02-0018" or "S02-0106")
        {
            var target = State.EffectStack.First(item => item.StackItemId == prompt.StackItemId);
            var data = response.CardId == "S02-0018" ? DirectPublicResponseData(response, target) : null;
            CommitS2CounterResponse(playerIndex, response, prompt.StackItemId!, data);
            return;
        }
        CommitNegateResponse(playerIndex, response, prompt.StackItemId!);
    }

    private void ResolveAbsoluteDefenseDiscard(int playerIndex, L12Prompt prompt, string discardId)
    {
        var player = State.Players[playerIndex];
        var responseId = prompt.Data["responseId"];
        var response = FindOnField(player, responseId, out _, out _)!;
        var discard = player.Hand.First(card => card.InstanceId == discardId);
        player.Hand.Remove(discard);
        player.Graveyard.Add(discard);
        AddEvent("cost", playerIndex, $"{player.Name} 弃置 {discard.Name} 支付〈绝对防御〉费用", discard);
        CommitNegateResponse(playerIndex, response, prompt.StackItemId!);
    }

    private void ResolvePuppetResponseSlot(int playerIndex, L12Prompt prompt, string slotChoice)
    {
        if (slotChoice == "cancel")
        {
            State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = playerIndex };
            OfferResponse();
            return;
        }
        var player = State.Players[playerIndex];
        var response = player.Hand.FirstOrDefault(card => card.InstanceId == prompt.Data.GetValueOrDefault("responseId")
            && card.CardId == "S02-0005");
        if (response is null || prompt.StackItemId is null)
        {
            PassPriority(playerIndex);
            return;
        }
        CommitPuppetResponse(playerIndex, response, prompt.StackItemId, slotChoice);
    }

    private void CommitPuppetResponse(int playerIndex, L12CardInstance response, string targetStackId, string slotChoice)
    {
        var item = new L12StackItem
        {
            StackItemId = $"stack-{++State.StackSequence}",
            Controller = playerIndex,
            SourceInstanceId = response.InstanceId,
            SourceCardId = response.CardId,
            SourceName = response.Name,
            Trigger = "response-retarget-master",
            Text = "从手牌休整登场于前排，并将本次进攻目标改为此军团",
        };
        item.Targets.Add(targetStackId);
        item.Data["slot"] = slotChoice;
        State.EffectStack.Add(item);
        AddEvent("response", playerIndex, $"{State.Players[playerIndex].Name} 发动〈{response.Name}〉响应主宰进攻", response);
        PublishEffectPresentation("effect-response", playerIndex, response, item.Trigger, item.Text, item.Data);
        State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = 1 - playerIndex };
        OfferResponse();
    }

    private void CommitNegateResponse(int playerIndex, L12CardInstance response, string targetStackId)
    {
        var player = State.Players[playerIndex];
        if (FindOnField(player, response.InstanceId, out var row, out var slot) is not null) player.Field[row][slot] = null;
        response.Hidden = false;
        player.Resolving.Add(response);
        var item = new L12StackItem
        {
            StackItemId = $"stack-{++State.StackSequence}",
            Controller = playerIndex,
            SourceInstanceId = response.InstanceId,
            SourceCardId = response.CardId,
            SourceName = response.Name,
            Trigger = "response-negate",
            Text = "无效堆叠中的效果",
        };
        item.Targets.Add(targetStackId);
        State.EffectStack.Add(item);
        AddEvent("response", playerIndex, $"{player.Name} 打出〈{response.Name}〉响应", response);
        PublishEffectPresentation("effect-response", playerIndex, response, item.Trigger, item.Text, item.Data);
        State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = 1 - playerIndex };
        OfferResponse();
    }

    private void CommitMercenaryResponse(int playerIndex, L12CardInstance response, string targetStackId)
    {
        var item = new L12StackItem
        {
            StackItemId = $"stack-{++State.StackSequence}",
            Controller = playerIndex,
            SourceInstanceId = response.InstanceId,
            SourceCardId = response.CardId,
            SourceName = response.Name,
            Trigger = "response-block",
            Text = "弃置此军团，抵挡本次进攻",
        };
        item.Targets.Add(targetStackId);
        State.EffectStack.Add(item);
        AddEvent("response", playerIndex, $"{playerIndex + 1} 号玩家发动〈佣兵部队〉抵挡进攻", response);
        PublishEffectPresentation("effect-response", playerIndex, response, item.Trigger, item.Text, item.Data);
        State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = 1 - playerIndex };
        OfferResponse();
    }

    private void PassPriority(int playerIndex)
    {
        var window = State.ResponseWindow;
        if (window is null) return;
        window.ConsecutivePasses++;
        AddEvent("priority-pass", playerIndex, $"{State.Players[playerIndex].Name} 不响应");
        if (window.ConsecutivePasses >= 2)
        {
            State.ResponseWindow = null;
            State.IsResolvingStack = true;
            ResolveTopStack();
            return;
        }
        window.PriorityPlayer = 1 - playerIndex;
        OfferResponse();
    }

    private void ResolveTopStack()
    {
        if (State.EffectStack.Count == 0)
        {
            AfterStackSettled();
            return;
        }
        var item = State.EffectStack[^1];
        // 无效状态必须先于响应类型分派处理。否则已被〈绝对防御〉无效的
        // 〈落穴陷阱〉仍会进入 response-negate 分支，继续无效原登场效果。
        if (item.Negated)
        {
            State.PendingPrompts.RemoveAll(prompt => prompt.StackItemId == item.StackItemId);
            AddEvent("stack-resolve", item.Controller, $"〈{item.SourceName}〉的{item.Text}未产生效果");
            if (item.Trigger == "attack")
            {
                AddEvent("combat-stage", item.Controller,
                    "进攻方【进攻时】效果被无效；进攻宣言仍然成立，继续后续时序");
            }
            else if (item.Trigger == "opponent-attack" && State.PendingDefense is not null)
            {
                State.PendingDefense.BlockedByResponse = true;
                AddEvent("combat-stage", 1 - State.PendingDefense.AttackerPlayer,
                    "〈绝对防御〉抵挡本次进攻；已结算的进攻时效果不回退");
            }
            FinishStackItem(item);
            return;
        }
        if (item.Trigger == "response-negate")
        {
            var target = State.EffectStack.FirstOrDefault(candidate => candidate.StackItemId == item.Targets.FirstOrDefault());
            if (target is not null) target.Negated = true;
            AddEvent("effect-negated", item.Controller,
                target is null ? "响应目标已经离开堆叠" : $"〈{target.SourceName}〉的{target.Text}被无效");
            FinishStackItem(item);
            return;
        }
        if (item.Trigger == "response-block")
        {
            var target = State.EffectStack.FirstOrDefault(candidate => candidate.StackItemId == item.Targets.FirstOrDefault());
            // 抵挡只终止交战，不无效已经发动的【进攻时】效果。
            if (State.PendingDefense is not null) State.PendingDefense.BlockedByResponse = true;
            var player = State.Players[item.Controller];
            var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == item.SourceInstanceId);
            if (card is not null) { player.Hand.Remove(card); player.Graveyard.Add(card); }
            AddEvent("defense", item.Controller, "佣兵部队抵挡本次进攻", card is null ? [] : [card]);
            FinishStackItem(item);
            return;
        }
        if (item.Trigger == "response-retarget-master")
        {
            ResolvePuppetResponse(item);
            return;
        }
        ResolveCardEffect(item);
    }

    private void ResolvePuppetResponse(L12StackItem item)
    {
        var player = State.Players[item.Controller];
        var card = player.Hand.FirstOrDefault(candidate => candidate.InstanceId == item.SourceInstanceId
            && candidate.CardId == "S02-0005");
        var attackItem = State.EffectStack.FirstOrDefault(candidate => candidate.StackItemId == item.Targets.FirstOrDefault()
            && candidate.Trigger == "opponent-attack" && !candidate.Negated);
        var slotParts = item.Data.GetValueOrDefault("slot")?.Split(':');
        var slot = -1;
        var validSlot = slotParts is { Length: 2 }
            && int.TryParse(slotParts[0], out var row) && row == 0
            && int.TryParse(slotParts[1], out slot) && slot is >= 0 and <= 2
            && player.Field[0][slot] is null;
        if (card is null || attackItem is null || State.PendingDefense?.Target.Type != "master" || !validSlot)
        {
            AddEvent("effect-failed", item.Controller, $"〈{item.SourceName}〉未能在预先选择的位置登场，进攻目标不变");
            FinishStackItem(item);
            return;
        }

        player.Hand.Remove(card);
        card.Tapped = true;
        card.SummonRound = State.Round;
        player.Field[0][slot] = card;
        ApplyDisasterLevelOnEntry(item.Controller, card, deferTriggerUntilStackSettles: true);
        State.PendingDefense.Target = new L12AttackTarget("legion", card.InstanceId);
        AddEvent("enter", item.Controller, $"{card.Name} 从手牌休整登场于前排，并成为本次进攻目标", card);
        if (HasImmediateEffect(card, "enter"))
            QueueOrPushTriggeredEffect(item.Controller, card, "enter", "【登场时】效果");
        QueueS2GrailRoundTableEntry(item.Controller, card);
        FinishStackItem(item);
    }

    private void FinishStackItem(L12StackItem item)
    {
        QueueNextTrialCompletionSegment(item);
        if (!item.Negated && item.Data.GetValueOrDefault("wisdomRewards") is { Length: > 0 } rewards)
        {
            foreach (var marker in rewards.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = marker.Split('|', 2);
                if (parts.Length != 2 || !int.TryParse(parts[0], out var controller) || controller is < 0 or > 1) continue;
                var wisdom = State.Players[controller].Graveyard.LastOrDefault(card => card.InstanceId == parts[1])
                    ?? State.Players[controller].Resolving.LastOrDefault(card => card.InstanceId == parts[1]);
                if (wisdom is not null)
                    QueueTriggerCandidates([CreateTriggerCandidate(controller, wisdom,
                        "wisdom-reward", "对方效果成功完成结算后的效果")]);
            }
        }
        if (item.Trigger == "authority-event" && FindAuthorityEvent(item) is { } authorityEvent)
            authorityEvent.Resolved = true;
        var completedSource = FindSource(item);
        var queuedCompositeContinuation = QueueNextCompositeSegment(item, completedSource);
        var queueBatch6JATakeda = !queuedCompositeContinuation
            && item.Data.GetValueOrDefault("batch6JAFollowup") == "takeda" && completedSource is not null;
        var queueAngusTrial = !queuedCompositeContinuation && !item.Negated && completedSource?.CardType == "tactic"
            && item.Trigger is "play" or "reaction" or "s2-reaction";
        var queueExorcistReturn = !queuedCompositeContinuation && !item.Negated
            && completedSource?.CardType == "tactic"
            && item.Trigger is "play" or "reaction";
        State.EffectStack.Remove(item);
        if (queueBatch6JATakeda) QueueBatch6JATakedaFollowup(item.Controller, completedSource!);
        var owner = State.Players[item.Controller];
        var resolving = owner.Resolving.FirstOrDefault(card => card.InstanceId == item.SourceInstanceId);
        if (resolving is not null && !queuedCompositeContinuation && !IsPendingCombatDeath(resolving.InstanceId)
            && State.EffectStack.All(other => other.SourceInstanceId != resolving.InstanceId))
        {
            owner.Resolving.Remove(resolving);
            ResetCardAfterLeavingField(resolving);
            owner.Graveyard.Add(resolving);
        }
        if (!queuedCompositeContinuation && item.Data.ContainsKey("postResolutionGenerated"))
        {
            State.IsResolvingStack = false;
            if (TryBeginPostResolutionGeneratedInteraction(item)) return;
        }
        if (queueExorcistReturn) QueueS2ExorcistReturns(item.Controller, completedSource!);
        if (queueAngusTrial) QueueS2AngusTacticTrial(item.Controller, completedSource!);
        if (State.EffectStack.Count > 0)
        {
            if (State.IsResolvingStack) ResolveTopStack();
            else BeginStackItem(State.EffectStack[^1]);
            return;
        }
        State.IsResolvingStack = false;
        if (State.PendingTriggerBatches.Count > 0)
        {
            AdvanceTriggerBatches();
            if (State.PendingPrompts.Any(prompt => prompt.Continuation == "trigger-batch-order")
                || State.EffectStack.Count > 0) return;
        }
        if (State.DeferredEffectStack.Count > 0)
        {
            // Deferred siblings were created while another item was resolving. Each is a distinct
            // StackItem and therefore needs its own response window; bulk-merging them would let only
            // the last item receive responses and auto-resolve every remaining sibling underneath it.
            var next = State.DeferredEffectStack[^1];
            State.DeferredEffectStack.RemoveAt(State.DeferredEffectStack.Count - 1);
            State.EffectStack.Add(next);
            AddEvent("stack-open", null, "当前堆叠关闭，处理下一项结算中产生的额外触发式效果");
            BeginStackItem(next);
            return;
        }
        AfterStackSettled();
    }

    private void TrySettleScheduledDisasterIfIdle()
    {
        if (!State.CheckDisasterAfterStack
            || State.IsResolvingStack
            || State.EffectStack.Count > 0
            || State.DeferredEffectStack.Count > 0
            || State.PendingTriggerBatches.Count > 0
            || State.PendingTriggerStackCandidates.Count > 0
            || State.PendingActivations.Count > 0
            || State.PendingPrompts.Count > 0
            || State.ResponseWindow is not null)
            return;

        AfterStackSettled();
    }

    private void AfterStackSettled()
    {
        State.ResponseWindow = null;
        var pendingFactionPlayer = State.Players.FirstOrDefault(player => player.UsedAbilities.Contains("pending:factionZeroRecovery"));
        if (pendingFactionPlayer is not null)
        {
            const string queuedFactionKey = "queued:factionZeroRecovery";
            if (pendingFactionPlayer.UsedAbilities.Contains(queuedFactionKey)) return;
            var alreadyQueued = State.PendingActivations.Any(activation =>
                    activation.Controller == pendingFactionPlayer.PlayerIndex
                    && activation.SourceCardId == "S01-01C1")
                || State.PendingTriggerStackCandidates.Any(candidate =>
                    candidate.Controller == pendingFactionPlayer.PlayerIndex
                    && candidate.SourceCardId == "S01-01C1"
                    && candidate.Data.GetValueOrDefault("ability") == "factionZeroRecovery");
            if (alreadyQueued) return;
            pendingFactionPlayer.UsedAbilities.Remove("pending:factionZeroRecovery");
            pendingFactionPlayer.UsedAbilities.Add(queuedFactionKey);
            var faction = CreateCard("S01-01C1", $"faction-{pendingFactionPlayer.PlayerIndex}");
            QueueTriggerCandidates([
                CreateTriggerCandidate(pendingFactionPlayer.PlayerIndex, faction, "active",
                    "我方士气为0张时的天廷阵营效果",
                    new Dictionary<string, string> { ["ability"] = "factionZeroRecovery" }, faction)
            ]);
            return;
        }
        if (State.PendingDefense is not null)
        {
            AdvanceCombatTimelineIfIdle();
            if (State.PendingDefense is not null || !CombatTimelineIsIdle()) return;
        }
        if (State.CheckDisasterAfterStack)
        {
            State.CheckDisasterAfterStack = false;
            if (State.DisasterValue > 8)
            {
                BeginDisasterTrigger(opening: false);
                if (State.EffectStack.Count > 0 || State.PendingPrompts.Count > 0) return;
            }
        }
        if (State.ResumeTurnStartAfterStack)
        {
            State.ResumeTurnStartAfterStack = false;
            // A turn-start disaster such as Ragnarok can end the just-started
            // turn.  Do not resume Reset/Draw/Morale from that interrupted turn;
            // close it first so the granted extra turn starts as a fresh turn.
            if (State.Phase == L12Phase.End)
            {
                CompleteEndTurn(State.ActivePlayer);
                return;
            }
            ContinueAutomaticTurnStart();
            return;
        }
        if (State.ResumeGmResetAfterStack)
        {
            State.ResumeGmResetAfterStack = false;
            CompleteGmResetPhase(State.ActivePlayer);
            AddEvent("gm", State.ActivePlayer, $"[GM] 推进至{GmPhaseLabel(State.Phase)}");
            return;
        }
        if (State.Phase == L12Phase.End && State.PendingPrompts.Count == 0)
        {
            CompleteEndTurn(State.ActivePlayer);
            return;
        }
    }
}
