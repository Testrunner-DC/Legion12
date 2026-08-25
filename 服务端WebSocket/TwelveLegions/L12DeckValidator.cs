namespace TwelveLegions.Server;

public static class L12DeckValidator
{
    private static readonly HashSet<string> MainDeckTypes =
        ["legion", "tactic", "counter-tactic", "artifact"];

    public static bool TryValidate(
        L12Catalog catalog,
        L12CustomDeckSubmission submission,
        out L12PresetDeckDefinition deck,
        out string error)
    {
        deck = null!;
        var name = submission.Name.Trim();
        if (name.Length is < 1 or > 24)
        {
            error = "牌库名称须为 1–24 个字符";
            return false;
        }
        if (!catalog.Cards.TryGetValue(submission.MasterId, out var master)
            || master.CardType is not ("master" or "divinity"))
        {
            error = "请选择有效的主宰";
            return false;
        }
        if (master.Id == "S01-02M2")
        {
            error = "复苏的奥西里斯不能被选择为主宰；选择伊西斯时会自动置入额外区并在开局进入墓地";
            return false;
        }
        var countedMainDeckSize = submission.CardIds.Count(id => !catalog.Cards.TryGetValue(id, out var card)
            || !L12SpecialDeckRules.DoesNotCountTowardMainDeck(card));
        if (countedMainDeckSize is < 40 or > 50)
        {
            error = $"主牌库须为 40–50 张（规则标明不计入构筑的卡牌除外，当前 {countedMainDeckSize} 张）";
            return false;
        }
        var excessive = submission.CardIds.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Id = group.Key,
                Count = group.Count(),
                Limit = catalog.Cards.TryGetValue(group.Key, out var card) ? card.DeckLimit : 3,
            })
            .FirstOrDefault(group => group.Count > group.Limit);
        if (excessive is not null)
        {
            error = $"同编号卡牌最多 {excessive.Limit} 张：{excessive.Id}";
            return false;
        }
        foreach (var cardId in submission.CardIds)
        {
            if (!catalog.Cards.TryGetValue(cardId, out var card))
            {
                error = $"牌库包含未知卡牌：{cardId}";
                return false;
            }
            if (!MainDeckTypes.Contains(card.CardType))
            {
                error = $"{card.NameZh} 不能放入主牌库";
                return false;
            }
            if (card.Faction != "universal" && card.Faction != master.Faction)
            {
                error = $"{card.NameZh} 与主宰阵营不符";
                return false;
            }
        }

        var requiredMorale = master.Faction == "taiyangcheng" ? 6 : 8;
        if (submission.MoraleIds.Count != requiredMorale)
        {
            error = $"{master.NameZh} 的士气牌库须为 {requiredMorale} 张";
            return false;
        }
        foreach (var moraleId in submission.MoraleIds)
        {
            if (!catalog.Cards.TryGetValue(moraleId, out var morale)
                || morale.CardType is not ("rune" or "divinity") || morale.Faction != master.Faction)
            {
                error = $"无效的士气卡：{moraleId}";
                return false;
            }
        }
        var trialCapacity = L12SpecialDeckRules.TrialCapacity(master);
        if (submission.SpecialIds.Count != trialCapacity)
        {
            error = trialCapacity == 0
                ? $"{master.NameZh} 不能携带试炼"
                : $"{master.NameZh} 的试炼区须为 {trialCapacity} 张（当前 {submission.SpecialIds.Count} 张）";
            return false;
        }
        if (submission.SpecialIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != submission.SpecialIds.Count)
        {
            error = "试炼区不能放入重复卡牌";
            return false;
        }
        foreach (var specialId in submission.SpecialIds)
        {
            if (!catalog.Cards.TryGetValue(specialId, out var special)
                || special.CardType != "trial" || special.Faction != master.Faction)
            {
                error = $"无效的特殊区卡牌：{specialId}";
                return false;
            }
        }

        deck = new L12PresetDeckDefinition
        {
            Name = name,
            MasterId = master.Id,
            CardIds = submission.CardIds.ToList(),
            MoraleIds = submission.MoraleIds.ToList(),
            SpecialIds = submission.SpecialIds.ToList(),
        };
        error = string.Empty;
        return true;
    }
}
