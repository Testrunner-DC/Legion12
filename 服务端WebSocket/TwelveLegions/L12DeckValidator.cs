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
        if (!catalog.Cards.TryGetValue(submission.MasterId, out var master) || master.CardType != "master")
        {
            error = "请选择有效的主宰";
            return false;
        }
        var countedMainDeckSize = submission.CardIds.Count(id => id is not ("S01-0212" or "S02-0201"));
        if (countedMainDeckSize is < 40 or > 50)
        {
            error = $"主牌库须为 40–50 张（陵墓守卫、增殖的甲虫不计入，当前 {countedMainDeckSize} 张）";
            return false;
        }
        var excessive = submission.CardIds.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 3);
        if (excessive is not null)
        {
            error = $"同编号卡牌最多 3 张：{excessive.Key}";
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
                || morale.CardType != "rune" || morale.Faction != master.Faction)
            {
                error = $"无效的士气卡：{moraleId}";
                return false;
            }
        }

        deck = new L12PresetDeckDefinition
        {
            Name = name,
            MasterId = master.Id,
            CardIds = submission.CardIds.ToList(),
            MoraleIds = submission.MoraleIds.ToList(),
        };
        error = string.Empty;
        return true;
    }
}
