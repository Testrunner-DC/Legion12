namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    internal const string PaidCostSummaryDataKey = "paidCostSummary";

    private sealed record PaidCostCardSnapshot(string Name, string CardId, bool Tapped);
    private sealed record PaidCostMoraleSnapshot(string CardId, bool Tapped, bool IsGodPower);

    private sealed record ActivePaidCostSnapshot(
        int Controller,
        string SourceInstanceId,
        string SourceName,
        bool SourceTapped,
        bool MasterTapped,
        int Hp,
        int Runes,
        int TemporaryMorale,
        IReadOnlyDictionary<string, PaidCostCardSnapshot> Hand,
        IReadOnlyDictionary<string, PaidCostCardSnapshot> Graveyard,
        IReadOnlyDictionary<string, PaidCostCardSnapshot> Library,
        IReadOnlyDictionary<string, PaidCostMoraleSnapshot> Morale,
        IReadOnlyDictionary<string, PaidCostCardSnapshot> Field);

    private ActivePaidCostSnapshot? _activePaidCostSnapshot;

    /// <summary>
    /// 只登记已经完成支付的公开 Cost。响应说明不得从卡文猜测是否支付，也不得把尚未
    /// 执行的后续分支写成既成事实；调用方必须在权威状态变更成功后才写入此元数据。
    /// </summary>
    private static void RecordPaidCostPresentation(IDictionary<string, string> data,
        params string?[] summaries)
    {
        var existing = (data.TryGetValue(PaidCostSummaryDataKey, out var current) ? current : null)
            ?.Split('；', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        var normalized = existing.Concat(summaries)
            .Where(summary => !string.IsNullOrWhiteSpace(summary))
            .Select(summary => summary!.Trim().TrimEnd('。'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0) return;
        data[PaidCostSummaryDataKey] = string.Join("；", normalized);
    }

    private static IReadOnlyDictionary<string, PaidCostCardSnapshot> SnapshotCostCards(
        IEnumerable<L12CardInstance> cards)
        => cards.ToDictionary(card => card.InstanceId,
            card => new PaidCostCardSnapshot(card.Name, card.CardId, card.Tapped),
            StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, PaidCostMoraleSnapshot> SnapshotCostMorale(
        IEnumerable<L12MoraleCard> cards)
        => cards.ToDictionary(card => card.InstanceId,
            card => new PaidCostMoraleSnapshot(card.CardId, card.Tapped, card.IsGodPower),
            StringComparer.OrdinalIgnoreCase);

    private ActivePaidCostSnapshot CaptureActivePaidCostSnapshot(int controller, L12CardInstance source)
    {
        var player = State.Players[controller];
        var usesMasterZone = source.CardType is "master" or "divinity"
            || source.InstanceId.Equals($"master-{controller}", StringComparison.OrdinalIgnoreCase);
        return new ActivePaidCostSnapshot(
            controller,
            source.InstanceId,
            source.Name,
            usesMasterZone ? player.MasterTapped : source.Tapped,
            player.MasterTapped,
            player.Hp,
            player.SpecialZones.Runes,
            player.TemporaryMorale,
            SnapshotCostCards(player.Hand),
            SnapshotCostCards(player.Graveyard),
            SnapshotCostCards(player.Library),
            SnapshotCostMorale(player.Morale),
            SnapshotCostCards(player.Field.SelectMany(row => row).Where(card => card is not null).Select(card => card!)));
    }

    /// <summary>
    /// 主动能力的提交入口先保存支付前快照；只有同一次提交真正抵达 active 入栈边界时，
    /// 才按权威区域/资源差异生成回执。这样费用减免、陵墓守卫代付、私密弃牌、符文/神力
    /// 和主动休整都描述实际发生的支付，而不是重复解析卡文。
    /// </summary>
    private void AddActivePaidCostPresentation(int controller, L12CardInstance source,
        Dictionary<string, string> data)
    {
        if (data.ContainsKey(PaidCostSummaryDataKey)) return;
        var before = _activePaidCostSnapshot;
        if (before is null || before.Controller != controller
            || !before.SourceInstanceId.Equals(source.InstanceId, StringComparison.OrdinalIgnoreCase)) return;

        var player = State.Players[controller];
        var runtimeAbility = data.GetValueOrDefault("ability") ?? string.Empty;
        var sourceRestIsCost = L12StructuredCardRules.IsActiveRestAbility(source.CardId, runtimeAbility);
        var currentHand = player.Hand.Select(card => card.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentGrave = player.Graveyard.ToDictionary(card => card.InstanceId, StringComparer.OrdinalIgnoreCase);
        var currentLibrary = player.Library.Select(card => card.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentMorale = player.Morale.ToDictionary(card => card.InstanceId, StringComparer.OrdinalIgnoreCase);
        var currentField = player.Field.SelectMany(row => row).Where(card => card is not null).Select(card => card!)
            .ToDictionary(card => card.InstanceId, StringComparer.OrdinalIgnoreCase);
        var summaries = new List<string>();

        var damage = Math.Max(0, before.Hp - player.Hp);
        if (damage > 0) summaries.Add($"对我方主宰造成{damage}点伤害");

        var returnedMorale = before.Morale.Keys.Count(id => !currentMorale.ContainsKey(id));
        if (returnedMorale > 0) summaries.Add($"返还{returnedMorale}士气");
        var tappedMorale = before.Morale
            .Where(pair => !pair.Value.Tapped && currentMorale.GetValueOrDefault(pair.Key) is { Tapped: true })
            .Select(pair => pair.Value).ToArray();
        var flippedGodPower = tappedMorale.Count(card => card.IsGodPower);
        var consumedMorale = tappedMorale.Length - flippedGodPower;
        if (consumedMorale > 0) summaries.Add($"消耗{consumedMorale}士气");
        if (flippedGodPower > 0) summaries.Add($"消耗并翻转{flippedGodPower}神力");
        var temporaryMorale = Math.Max(0, before.TemporaryMorale - player.TemporaryMorale);
        if (temporaryMorale > 0) summaries.Add($"消耗{temporaryMorale}临时士气");

        var spentRunes = Math.Max(0, before.Runes - player.SpecialZones.Runes);
        if (spentRunes > 0) summaries.Add($"消耗{spentRunes}符文");

        foreach (var pair in before.Hand.Where(pair => !currentHand.Contains(pair.Key)
                     && currentGrave.ContainsKey(pair.Key)))
            summaries.Add($"弃置手牌中的〈{pair.Value.Name}〉");
        foreach (var pair in before.Field.Where(pair => !currentField.ContainsKey(pair.Key)
                     && currentGrave.ContainsKey(pair.Key)))
            summaries.Add($"弃置战场上的〈{pair.Value.Name}〉");
        foreach (var pair in before.Library.Where(pair => !currentLibrary.Contains(pair.Key)
                     && currentGrave.ContainsKey(pair.Key)))
            summaries.Add($"弃置牌库顶部的〈{pair.Value.Name}〉");
        foreach (var pair in before.Graveyard.Where(pair => !currentGrave.ContainsKey(pair.Key)
                     && currentLibrary.Contains(pair.Key)))
            summaries.Add($"将墓地中的〈{pair.Value.Name}〉置于牌库底部");

        var masterNowTapped = player.MasterTapped;
        var usesMasterZone = source.CardType is "master" or "divinity"
            || source.InstanceId.Equals($"master-{controller}", StringComparison.OrdinalIgnoreCase);
        if (sourceRestIsCost && !before.MasterTapped && masterNowTapped && usesMasterZone)
            summaries.Add($"休整〈{before.SourceName}〉");
        var sourceNowTapped = usesMasterZone ? masterNowTapped
            : currentField.GetValueOrDefault(source.InstanceId)?.Tapped
              ?? (player.Relic?.InstanceId == source.InstanceId ? player.Relic.Tapped
                  : player.ExtraRelics.FirstOrDefault(card => card.InstanceId == source.InstanceId)?.Tapped)
              ?? source.Tapped;
        if (sourceRestIsCost && !before.SourceTapped && sourceNowTapped && !usesMasterZone)
            summaries.Add($"休整〈{before.SourceName}〉");
        foreach (var pair in before.Field.Where(pair => pair.Key != source.InstanceId && !pair.Value.Tapped
                     && currentField.GetValueOrDefault(pair.Key) is { Tapped: true }))
            summaries.Add(pair.Value.CardId is "S01-0202" or "ST02-T1"
                ? $"以休整〈{pair.Value.Name}〉支付1士气"
                : $"休整〈{pair.Value.Name}〉");

        RecordPaidCostPresentation(data, summaries.ToArray());
    }

    private static string PaidCostResponseLine(L12StackItem item)
    {
        var summary = item.Data.GetValueOrDefault(PaidCostSummaryDataKey)?.Trim();
        if (string.IsNullOrWhiteSpace(summary)) return string.Empty;
        return $"\nCost（已支付）：{summary}";
    }

    private static void AddPaidCostResponseData(L12StackItem item, Dictionary<string, string> data,
        string? keyPrefix = null)
    {
        var summary = item.Data.GetValueOrDefault(PaidCostSummaryDataKey);
        if (string.IsNullOrWhiteSpace(summary)) return;
        var prefix = string.IsNullOrWhiteSpace(keyPrefix) ? string.Empty : keyPrefix + ":";
        data[$"{prefix}responsePaidCostSummary"] = summary;
    }

    private static string ResolveCompositeResponseEffectText(L12StackItem item, string fallback)
    {
        var planId = item.Data.GetValueOrDefault("compositePlan");
        if (string.IsNullOrWhiteSpace(planId)) return fallback;
        var segments = L12CompositeEffectPlans.Segments(planId!);
        if (L12CompositeEffectPlans.UsesSingleResponseEffect(planId))
        {
            var enabled = segments
                .Where(segment => CompositeSegmentEnabled(segment, item))
                .Select(segment => segment.Text.Trim().TrimEnd('。'))
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();
            return enabled.Length == 0 ? fallback : string.Join("；随后，", enabled) + "。";
        }

        if (!int.TryParse(item.Data.GetValueOrDefault("compositeSegment"), out var index)
            || index < 0 || index >= segments.Count) return fallback;
        var current = segments[index];
        return CompositeSegmentEnabled(current, item)
            ? current.Text.Trim().TrimEnd('。') + "。"
            : fallback;
    }

    /// <summary>
    /// 将公共复合协议中已经在首个响应窗口前支付的费用转换为统一展示元数据。
    /// 这里只描述计划确实提交的 Cost；普通打牌费用和效果正文中的结算动作不在此列。
    /// </summary>
    private static void RecordCompositePreResponseCosts(string cardId,
        IReadOnlyDictionary<string, List<string>> declared, Dictionary<string, string> data)
    {
        var segments = L12CompositeEffectPlans.Segments(cardId);
        var enabled = segments.Where(segment => CompositeSegmentEnabled(segment, declared)).ToArray();
        var paidSegments = enabled.Where(segment => segment.PreStackCost).ToArray();
        if (paidSegments.Length == 0 && enabled.FirstOrDefault() is { Cost: > 0 } first)
            paidSegments = [first];

        var summaries = new List<string>();
        if (cardId == "S02-0307") summaries.Add("弃置我方牌库顶部1张牌");
        if (cardId == "S02-0207")
        {
            var count = declared.GetValueOrDefault("discardTargets", []).Count;
            if (count > 0) summaries.Add($"弃置我方战场{count}张军团");
        }
        foreach (var segment in paidSegments)
        {
            var summary = segment.CostKind switch
            {
                "god-power-flip" => $"消耗并翻转{segment.Cost}神力",
                "morale-return" => $"返还{segment.Cost}士气",
                "ordinary-payment" => $"消耗{segment.Cost}士气",
                "discard-hand" => $"弃置{segment.Cost}张手牌",
                "conditional-master-damage" => $"处理对我方主宰造成{segment.Cost}点伤害的Cost",
                "grave-bottom" => $"将墓地卡牌按效果合计{segment.Cost}张置于牌库底部",
                _ => null,
            };
            if (summary is not null) summaries.Add(summary);
        }
        RecordPaidCostPresentation(data, summaries.ToArray());
    }
}
