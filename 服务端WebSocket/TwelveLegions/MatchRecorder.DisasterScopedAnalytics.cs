using System.Globalization;

namespace TwelveLegions.Server;

public sealed partial class MatchRecorder
{
    public Task<L12DisasterAnalyticsReport> ReadScopedDisasterAnalyticsAsync(L12CardAnalyticsQuery query,
        string? includedCardId, string? deckSignature, CancellationToken cancellationToken = default)
        => ReadScopedDisasterAnalyticsAsync(query, includedCardId, deckSignature, cancellationToken,
            new CardAnalyticsRequestTiming());

    internal async Task<L12DisasterAnalyticsReport> ReadScopedDisasterAnalyticsAsync(L12CardAnalyticsQuery query,
        string? includedCardId, string? deckSignature, CancellationToken cancellationToken,
        CardAnalyticsRequestTiming timing)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CardAnalyticsHardBudget);
        var epoch = AnalyticsCacheEpoch;
        try
        {
            var normalized = NormalizeAnalyticsQuery(query);
            var card = string.IsNullOrWhiteSpace(includedCardId) ? null : includedCardId.Trim();
            var signature = string.IsNullOrWhiteSpace(deckSignature) ? null : deckSignature.Trim();
            if ((card is null) == (signature is null))
                throw new ArgumentException("请选择且仅选择一种卡牌或构筑范围");
            if (card is not null && !IsScopedAnalyticsCardId(card))
                throw new ArgumentException("卡牌范围无效", nameof(includedCardId));
            if (signature is not null) ValidateScopedDeckSignature(signature);
            // Kind and target are separate JSON fields; targets cannot collide through concatenation.
            var key = AnalyticsResultCacheKey(card is null ? "disaster-deck" : "disaster-included-card",
                normalized, card ?? signature);
            return (await ExecuteCardAnalyticsAsync(key, epoch, budget.Token, timing,
                execution => ComputeScopedDisasterAnalytics(execution, normalized, card, signature)))!;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (AnalyticsCacheEpoch != epoch) throw CardAnalyticsUnavailableException.Changed();
            throw CardAnalyticsUnavailableException.TimedOut();
        }
        finally { timing.SetTotal(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
    }

    private static bool IsScopedAnalyticsCardId(string value)
        => value.Length is > 0 and <= 128
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    // This is the existing popular-deck signature, not SQL or a permissively parsed deck submission.
    // Require exactly its section/card BINARY order and positive canonical decimal quantities.
    private static void ValidateScopedDeckSignature(string signature)
    {
        if (signature.Length > 16_384)
            throw new ArgumentException("构筑范围过长", nameof(signature));
        var parts = signature.Split('|');
        if (parts.Length is < 1 or > 256)
            throw new ArgumentException("构筑范围条目过多", nameof(signature));
        string? previousSection = null;
        string? previousCard = null;
        foreach (var part in parts)
        {
            var colon = part.IndexOf(':');
            var quantityMarker = part.LastIndexOf('x');
            if (colon <= 0 || quantityMarker <= colon + 1)
                throw new ArgumentException("构筑范围结构无效", nameof(signature));
            var section = part[..colon];
            var card = part[(colon + 1)..quantityMarker];
            var quantityText = part[(quantityMarker + 1)..];
            if (section is not ("main" or "morale" or "special") || !IsScopedAnalyticsCardId(card)
                || !int.TryParse(quantityText, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity)
                || quantity is < 1 or > 1000
                || quantityText != quantity.ToString(CultureInfo.InvariantCulture)
                || previousSection is not null && (string.CompareOrdinal(previousSection, section) > 0
                    || previousSection == section && string.CompareOrdinal(previousCard, card) >= 0))
                throw new ArgumentException("构筑范围须使用规范排序和数量", nameof(signature));
            previousSection = section;
            previousCard = card;
        }
    }

    private static L12DisasterAnalyticsReport ComputeScopedDisasterAnalytics(CardAnalyticsExecution execution,
        L12CardAnalyticsQuery query, string? card, string? signature)
    {
        var connection = execution.Connection;
        using var transaction = connection.BeginTransaction(deferred: true);
        execution.Measure(CardAnalyticsStage.ScopeTotal, () =>
        {
            PrepareAnalyticsScopeAsync(connection, query, includeFacts: false, transaction: transaction)
                .GetAwaiter().GetResult();
            execution.Check();
            // Restrict participants, never matches: the opposing seat is not implicitly included.
            // Only the disaster calculator consumes this narrowed eligible table; existing card/master
            // reports and their denominators have independent scopes and are not modified.
            using var restrict = card is not null
                ? DisasterCommand(connection, transaction, """
                    DELETE FROM temp.l12_analytics_eligible AS e WHERE NOT EXISTS(
                      SELECT 1 FROM match_deck_cards d WHERE d.match_id=e.match_id
                        AND d.player_index=e.player_index AND d.card_id=$card AND d.quantity>0);
                    """, ("$card", card))
                : DisasterCommand(connection, transaction, """
                    WITH ordered AS (
                      SELECT d.match_id,d.player_index,d.section,d.card_id,d.quantity
                      FROM match_deck_cards d JOIN temp.l12_analytics_eligible e
                        ON e.match_id=d.match_id AND e.player_index=d.player_index
                      ORDER BY d.match_id,d.player_index,d.section,d.card_id),
                    signatures AS (
                      SELECT match_id,player_index,group_concat(section||':'||card_id||'x'||quantity,'|') signature
                      FROM ordered GROUP BY match_id,player_index)
                    DELETE FROM temp.l12_analytics_eligible AS e WHERE NOT EXISTS(
                      SELECT 1 FROM signatures s WHERE s.match_id=e.match_id
                        AND s.player_index=e.player_index AND s.signature=$signature);
                    """, ("$signature", signature));
            restrict.ExecuteNonQuery();
        });
        var report = execution.Measure(CardAnalyticsStage.Coverage, () =>
            ComputeDisasterAnalytics(execution, transaction, query, allowUnselectedMaster: true));
        execution.Check();
        transaction.Commit();
        return report;
    }
}
