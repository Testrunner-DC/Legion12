namespace TwelveLegions.Server;

public sealed record L12DeckCardQuantity(string CardId, long Quantity);
public sealed record L12DeckCountSubmission(string? Name, string? MasterId,
    IReadOnlyList<L12DeckCardQuantity>? Main, IReadOnlyList<L12DeckCardQuantity>? Morale,
    IReadOnlyList<L12DeckCardQuantity>? Special, IReadOnlyList<L12DeckCardQuantity>? Bench = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>?>? AlternateArtCopies = null);

public static class L12DeckValidator
{
    private static readonly HashSet<string> MainDeckTypes =
        ["legion", "tactic", "artifact"];

    public static bool TryValidatePreset(
        L12Catalog catalog,
        L12PresetDeckDefinition preset,
        out string error,
        IReadOnlyList<L12CardRestrictionConfig>? cardRestrictions = null)
        => TryValidate(catalog, new L12CustomDeckSubmission
        {
            Name = preset.Name,
            MasterId = preset.MasterId,
            CardIds = preset.CardIds.ToList(),
            MoraleIds = preset.MoraleIds.ToList(),
            SpecialIds = preset.SpecialIds.ToList(),
            BenchIds = preset.BenchIds.ToList(),
            AlternateArtSelections = new Dictionary<string, string>(preset.AlternateArtSelections, StringComparer.OrdinalIgnoreCase),
            AlternateArtCopies = preset.AlternateArtCopies.ToDictionary(item => item.Key,
                item => item.Value.ToList(), StringComparer.OrdinalIgnoreCase),
        }, out _, out error, cardRestrictions);

    public static bool TryValidate(
        L12Catalog catalog,
        L12CustomDeckSubmission submission,
        out L12PresetDeckDefinition deck,
        out string error,
        IReadOnlyList<L12CardRestrictionConfig>? cardRestrictions = null)
    {
        deck = null!;
        if (!TryValidateCounts(catalog, new L12DeckCountSubmission(submission.Name, submission.MasterId,
                CountCards(submission.CardIds), CountCards(submission.MoraleIds), CountCards(submission.SpecialIds),
                CountCards(submission.BenchIds), (submission.AlternateArtCopies ?? []).ToDictionary(item => item.Key,
                    item => (IReadOnlyList<string>?)item.Value)),
                out error, cardRestrictions)) return false;
        deck = new L12PresetDeckDefinition
        {
            PublicationId = submission.PublicationId, PublicationVersion = submission.PublicationVersion,
            Name = submission.Name.Trim(), MasterId = catalog.Cards[submission.MasterId].Id,
            CardIds = submission.CardIds.ToList(),
            MoraleIds = submission.MoraleIds.Select(catalog.MoraleIdentities.CanonicalDeckCardId).ToList(),
            SpecialIds = submission.SpecialIds.ToList(), BenchIds = (submission.BenchIds ?? []).Select(id => id.Trim()).ToList(),
            AlternateArtSelections = (submission.AlternateArtSelections ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                .Take(128).ToDictionary(item => item.Key.Trim(), item => item.Value.Trim(), StringComparer.OrdinalIgnoreCase),
            AlternateArtCopies = (submission.AlternateArtCopies ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Key) && item.Value is not null)
                .Take(128).ToDictionary(item => item.Key.Trim(), item => item.Value.Take(50)
                    .Select(value => value?.Trim() ?? string.Empty).ToList(), StringComparer.OrdinalIgnoreCase),
        };
        return true;
    }

    public static IReadOnlyList<L12DeckCardQuantity> CountCards(IEnumerable<string>? cards)
        => (cards ?? []).GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new L12DeckCardQuantity(group.Key, group.LongCount())).ToArray();

    public static long CountCards(IEnumerable<L12DeckCardQuantity> cards)
    {
        long total = 0;
        foreach (var card in cards) total = checked(total + card.Quantity);
        return total;
    }

    private static IReadOnlyList<L12DeckCardQuantity> MergeCounts(IEnumerable<L12DeckCardQuantity> cards)
        => cards.GroupBy(card => card.CardId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new L12DeckCardQuantity(group.Key, CountCards(group))).ToArray();

    public static bool TryValidateCounts(L12Catalog catalog, L12DeckCountSubmission submission,
        out string error, IReadOnlyList<L12CardRestrictionConfig>? cardRestrictions = null)
    {
        if (submission is null) { error = "牌库计数字段无效"; return false; }
        try { return ValidateCountCore(catalog, submission, out error, cardRestrictions); }
        catch (OverflowException) { error = "牌库数量超出可处理范围"; return false; }
    }

    private static bool ValidateCountCore(L12Catalog catalog, L12DeckCountSubmission submission,
        out string error, IReadOnlyList<L12CardRestrictionConfig>? cardRestrictions)
    {
        var name = submission.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 24)
        {
            error = "牌库名称须为 1–24 个字符";
            return false;
        }
        if (submission.MasterId is null || !catalog.Cards.TryGetValue(submission.MasterId, out var master)
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
        if (submission.Main is null || submission.Morale is null || submission.Special is null
            || new[] { submission.Main, submission.Morale, submission.Special, submission.Bench ?? [] }
                .SelectMany(region => region).Any(card => card is null || card.CardId is null || card.Quantity <= 0))
        { error = "牌库计数字段无效"; return false; }
        var main = MergeCounts(submission.Main);
        var morale = MergeCounts(submission.Morale.Select(card => new L12DeckCardQuantity(
            catalog.MoraleIdentities.CanonicalDeckCardId(card.CardId), card.Quantity)));
        var special = MergeCounts(submission.Special);
        var bench = MergeCounts(submission.Bench ?? []);
        var restrictions = (cardRestrictions ?? []).ToArray();
        L12CardRestrictionConfig? ResolveRestriction(string cardId)
            => restrictions.FirstOrDefault(item => string.Equals(item.CardId, cardId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.MasterId, master.Id, StringComparison.OrdinalIgnoreCase))
               ?? restrictions.FirstOrDefault(item => string.Equals(item.CardId, cardId, StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(item.MasterId));
        var restricted = MergeCounts(main.Concat(morale).Concat(special).Append(new(master.Id, 1)))
            .Select(card => new { Id = card.CardId, Count = card.Quantity, Rule = ResolveRestriction(card.CardId) })
            .FirstOrDefault(item => item.Rule is not null && item.Count > item.Rule.MaxCopies);
        if (restricted is not null)
        {
            var reason = string.IsNullOrWhiteSpace(restricted.Rule!.Reason)
                ? string.Empty
                : $"（{restricted.Rule.Reason}）";
            error = restricted.Rule.MaxCopies == 0
                ? $"当前赛季禁用卡牌：{restricted.Id}{reason}"
                : $"当前赛季同编号卡牌最多 {restricted.Rule.MaxCopies} 张：{restricted.Id}{reason}";
            return false;
        }
        var countedMainDeckSize = CountCards(main.Where(item => !catalog.Cards.TryGetValue(item.CardId, out var card)
            || !L12SpecialDeckRules.DoesNotCountTowardMainDeck(card)));
        if (countedMainDeckSize is < 40 or > 50)
        {
            error = $"主牌库须为 40–50 张（规则标明不计入构筑的卡牌除外，当前 {countedMainDeckSize} 张）";
            return false;
        }
        var excessive = main.Select(item => new
            {
                Id = item.CardId,
                Count = item.Quantity,
                Limit = Math.Min(catalog.Cards.TryGetValue(item.CardId, out var card) ? card.DeckLimit : 3,
                    ResolveRestriction(item.CardId)?.MaxCopies ?? int.MaxValue),
            })
            .FirstOrDefault(group => group.Count > group.Limit);
        if (excessive is not null)
        {
            error = $"同编号卡牌最多 {excessive.Limit} 张：{excessive.Id}";
            return false;
        }
        foreach (var cardId in main.Select(item => item.CardId))
        {
            if (!catalog.Cards.TryGetValue(cardId, out var card))
            {
                error = $"牌库包含未知卡牌：{cardId}";
                return false;
            }
            if (L12SpecialDeckRules.IsDerivedSpecialCard(card))
            {
                error = $"{card.NameZh} 为 Limit {L12StructuredCardSemantics.DerivedSpecialCardLimit(card.Id)} 的衍生卡，不能放入主牌库";
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
        var submittedCardCounts = main.ToDictionary(item => item.CardId, item => item.Quantity, StringComparer.OrdinalIgnoreCase);
        foreach (var appearance in submission.AlternateArtCopies ?? new Dictionary<string, IReadOnlyList<string>?>())
        {
            var cardId = appearance.Key?.Trim() ?? string.Empty;
            if (!submittedCardCounts.TryGetValue(cardId, out var copies))
            {
                error = $"异画副本没有对应的主牌：{cardId}";
                return false;
            }
            if (appearance.Value is null || appearance.Value.Count > copies)
            {
                error = $"{catalog.Cards[cardId].NameZh} 的原画与异画总数超过同编号投入数量";
                return false;
            }
        }

        var requiredMorale = master.Faction == "taiyangcheng" ? 6 : 8;
        if (CountCards(morale) != requiredMorale)
        {
            error = $"{master.NameZh} 的士气牌库须为 {requiredMorale} 张";
            return false;
        }
        foreach (var moraleId in morale.Select(item => item.CardId))
        {
            if (!catalog.Cards.TryGetValue(moraleId, out var moraleCard)
                || moraleCard.CardType != "rune" || moraleCard.Faction != master.Faction
                || !catalog.MoraleIdentities.IsVersionForFaction(moraleId, master.Faction))
            {
                error = $"无效的士气卡：{moraleId}";
                return false;
            }
        }
        var trialCapacity = L12SpecialDeckRules.TrialCapacity(master);
        var specialCount = CountCards(special);
        if (specialCount != trialCapacity)
        {
            error = trialCapacity == 0
                ? $"{master.NameZh} 不能携带试炼"
                : $"{master.NameZh} 的试炼区须为 {trialCapacity} 张（当前 {specialCount} 张）";
            return false;
        }
        if (special.Any(item => item.Quantity > 1))
        {
            error = "试炼区不能放入重复卡牌";
            return false;
        }
        foreach (var specialId in special.Select(item => item.CardId))
        {
            if (!catalog.Cards.TryGetValue(specialId, out var specialCard)
                || specialCard.CardType != "trial" || specialCard.Faction != master.Faction)
            {
                error = $"无效的特殊区卡牌：{specialId}";
                return false;
            }
        }

        if (CountCards(bench) > 200)
        {
            error = "备选区最多保存 200 张卡牌";
            return false;
        }
        foreach (var group in bench)
        {
            if (!catalog.Cards.TryGetValue(group.CardId, out var card))
            {
                error = $"备选区包含未知卡牌：{group.CardId}";
                return false;
            }
            if (L12SpecialDeckRules.IsDerivedSpecialCard(card) || !MainDeckTypes.Contains(card.CardType))
            {
                error = $"{card.NameZh} 不能放入备选区";
                return false;
            }
            if (card.Faction != "universal" && card.Faction != master.Faction)
            {
                error = $"备选区中的 {card.NameZh} 与主宰阵营不符";
                return false;
            }
            if (group.Quantity > Math.Min(card.DeckLimit, 50))
            {
                error = $"备选区同编号卡牌最多 {card.DeckLimit} 张：{card.Id}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}
