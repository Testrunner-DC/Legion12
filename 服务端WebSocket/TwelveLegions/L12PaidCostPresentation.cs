namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    internal const string PaidCostSummaryDataKey = "paidCostSummary";

    /// <summary>
    /// 只登记已经完成支付的公开 Cost。响应说明不得从卡文猜测是否支付，也不得把尚未
    /// 执行的后续分支写成既成事实；调用方必须在权威状态变更成功后才写入此元数据。
    /// </summary>
    private static void RecordPaidCostPresentation(IDictionary<string, string> data,
        params string?[] summaries)
    {
        var normalized = summaries
            .Where(summary => !string.IsNullOrWhiteSpace(summary))
            .Select(summary => summary!.Trim().TrimEnd('。'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0) return;
        data[PaidCostSummaryDataKey] = string.Join("；", normalized);
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

    private static string CompleteSingleResponseEffectText(L12StackItem item, string fallback)
    {
        var planId = item.Data.GetValueOrDefault("compositePlan");
        if (!L12CompositeEffectPlans.UsesSingleResponseEffect(planId)) return fallback;
        var segments = L12CompositeEffectPlans.Segments(planId!)
            .Where(segment => CompositeSegmentEnabled(segment, item))
            .Select(segment => segment.Text.Trim().TrimEnd('。'))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        return segments.Length == 0 ? fallback : string.Join("；随后，", segments) + "。";
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
