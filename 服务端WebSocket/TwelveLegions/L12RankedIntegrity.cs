using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace TwelveLegions.Server;

public sealed record L12RankedIntegritySignalView(string Code, string Label);

public sealed record L12RankedIntegrityAuditView(
    string Id,
    string MatchId,
    string SeasonId,
    string FirstAccountId,
    string FirstPlayer,
    string SecondAccountId,
    string SecondPlayer,
    int? Winner,
    long DurationMs,
    int MeaningfulCommandCount,
    string ConclusionKind,
    bool NetworkLinked,
    string? NetworkCorrelationId,
    bool BrowserLinked,
    string? BrowserCorrelationId,
    int FinalRound,
    IReadOnlyList<L12RankedIntegritySignalView> Signals,
    bool ReviewRecommended,
    string EffectiveDisposition,
    string Enforcement,
    DateTimeOffset CreatedAt);

internal sealed record L12RankedIntegrityContext(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int MeaningfulCommandCount,
    string ConclusionKind,
    string? FirstNetworkFingerprint,
    string? SecondNetworkFingerprint,
    int FinalRound = 0,
    string? FirstBrowserFingerprint = null,
    string? SecondBrowserFingerprint = null);

internal static class L12RankedNetworkPrivacy
{
    internal const string EnvironmentKey = "L12_RANKED_INTEGRITY_HMAC_KEY";

    internal static string? Fingerprint(IPAddress? address, string? secret)
    {
        if (address is null || string.IsNullOrWhiteSpace(secret)
            || Encoding.UTF8.GetByteCount(secret) < 32) return null;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var addressBytes = address.GetAddressBytes();
        var payload = new byte[addressBytes.Length + 1];
        payload[0] = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? (byte)4 : (byte)6;
        Buffer.BlockCopy(addressBytes, 0, payload, 1, addressBytes.Length);
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return "net-v1:" + Convert.ToHexString(digest).ToLowerInvariant();
    }
}

public sealed partial class L12PlatformStore
{
    private static readonly TimeSpan RankedVeryShortMatch = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RankedExtremeShortMatch = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RankedRepeatedPairWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RankedHighRiskCooldown = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RankedPatternWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan RankedClusterWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan RankedPaddedMinimum = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan RankedPaddedMaximum = TimeSpan.FromMinutes(12);

    private sealed class RankedIntegrityAuditRow
    {
        public int EvidenceVersion { get; set; }
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string MatchId { get; set; } = string.Empty;
        public string SeasonId { get; set; } = string.Empty;
        public string FirstAccountId { get; set; } = string.Empty;
        public string SecondAccountId { get; set; } = string.Empty;
        public int? Winner { get; set; }
        public string FirstMasterId { get; set; } = string.Empty;
        public string SecondMasterId { get; set; } = string.Empty;
        public long DurationMs { get; set; }
        public int MeaningfulCommandCount { get; set; }
        public string ConclusionKind { get; set; } = "unknown";
        public string FirstNetworkFingerprint { get; set; } = string.Empty;
        public string SecondNetworkFingerprint { get; set; } = string.Empty;
        public string FirstBrowserFingerprint { get; set; } = string.Empty;
        public string SecondBrowserFingerprint { get; set; } = string.Empty;
        public int FinalRound { get; set; }
        public List<string> Signals { get; set; } = [];
        public bool ReviewRecommended { get; set; }
        public bool RewardHeld { get; set; }
        public string Enforcement { get; set; } = "none";
        public DateTimeOffset EndedAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public IReadOnlyList<L12RankedIntegrityAuditView> RankedIntegrityAudits(L12AccountView actor,
        string? accountId = null, string? matchId = null, bool reviewOnly = false, int limit = 200)
    {
        EnsureOperationsPermission(actor, L12Permission.AdminAuditRead);
        lock (_gate)
        {
            var rows = _data.RankedIntegrityAudits.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(accountId))
                rows = rows.Where(row => row.FirstAccountId == accountId || row.SecondAccountId == accountId);
            if (!string.IsNullOrWhiteSpace(matchId))
                rows = rows.Where(row => string.Equals(row.MatchId, matchId.Trim(),
                    StringComparison.OrdinalIgnoreCase));
            if (reviewOnly) rows = rows.Where(row => row.ReviewRecommended
                && !IsTerminalRankedIntegrityDisposition(RankedIntegrityDispositionLocked(row.MatchId)));
            return rows.OrderByDescending(row => row.CreatedAt)
                .Take(Math.Clamp(limit, 1, 500)).Select(RankedIntegrityView).ToArray();
        }
    }

    internal void RecordInvalidRankedMatch(string matchId, string firstAccountId, string secondAccountId,
        string? firstMasterId, string? secondMasterId, L12RankedIntegrityContext? context)
    {
        lock (_gate)
        {
            ValidateRankedIdentity(matchId, firstAccountId, secondAccountId, winner: null);
            if (_data.RankedSettlements.Any(row => string.Equals(row.MatchId, matchId,
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("排位对局已存在计分结果，不能重放为无效局");
            if (EnsureRankedIntegrityAuditLocked(matchId, firstAccountId, secondAccountId, null,
                    firstMasterId, secondMasterId, context))
                Save(false);
        }
    }

    private bool TryGetRankedSettlementReplayLocked(string matchId, string firstAccountId,
        string secondAccountId, int winner, string? firstMasterId, string? secondMasterId,
        L12RankedIntegrityContext? context, out L12RankedSettlementPair pair)
    {
        var rows = _data.RankedSettlements.Where(row => string.Equals(row.MatchId, matchId,
            StringComparison.OrdinalIgnoreCase)).ToArray();
        if (rows.Length == 0)
        {
            if (_data.RankedIntegrityAudits.Any(row => string.Equals(row.MatchId, matchId,
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("排位对局已作为无效局记录，不能重放为计分结果");
            pair = null!;
            return false;
        }
        if (rows.Length != 2)
            throw new InvalidDataException("排位结算账本不完整，已拒绝重复结算");
        var first = rows.SingleOrDefault(row => row.AccountId == firstAccountId);
        var second = rows.SingleOrDefault(row => row.AccountId == secondAccountId);
        if (first is null || second is null || first.Won != (winner == 0) || second.Won != (winner == 1)
            || !string.Equals(first.Outcome, winner == 0 ? "win" : "loss", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(second.Outcome, winner == 1 ? "win" : "loss", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("排位对局重放参数与已结算结果冲突");
        var addedAudit = EnsureRankedIntegrityAuditLocked(matchId, firstAccountId, secondAccountId, winner,
            firstMasterId, secondMasterId, context);
        if (addedAudit) Save(false);
        pair = new L12RankedSettlementPair(ToView(first), ToView(second), []);
        return true;
    }

    internal void VerifyRankedSettlementApplied(L12RankedSettlementEnvelope payload)
    {
        lock (_gate)
        {
            var settlements = _data.RankedSettlements.Where(row => string.Equals(row.MatchId,
                payload.MatchId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (payload.Winner is { } winner)
            {
                if (settlements.Length != 2)
                    throw new InvalidDataException("排位结算账本必须恰有两席记录");
                var first = settlements.SingleOrDefault(row => row.AccountId == payload.FirstAccountId);
                var second = settlements.SingleOrDefault(row => row.AccountId == payload.SecondAccountId);
                if (first is null || second is null || first.Won != (winner == 0)
                    || second.Won != (winner == 1)
                    || !string.Equals(first.Outcome, winner == 0 ? "win" : "loss",
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(second.Outcome, winner == 1 ? "win" : "loss",
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("排位结算账本与 outbox 胜负载荷冲突");
            }
            else if (string.Equals(payload.ConclusionKind, L12GameEngine.AgreedDrawConclusionKind,
                         StringComparison.OrdinalIgnoreCase))
            {
                if (settlements.Length != 2)
                    throw new InvalidDataException("排位平局结算账本必须恰有两席记录");
                var first = settlements.SingleOrDefault(row => row.AccountId == payload.FirstAccountId);
                var second = settlements.SingleOrDefault(row => row.AccountId == payload.SecondAccountId);
                if (first is null || second is null
                    || !string.Equals(first.Outcome, "draw", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(second.Outcome, "draw", StringComparison.OrdinalIgnoreCase)
                    || first.Delta != 0 || second.Delta != 0
                    || first.Before != first.After || second.Before != second.After)
                    throw new InvalidOperationException("排位平局账本与 outbox 载荷冲突");
            }
            else if (settlements.Length != 0)
            {
                throw new InvalidOperationException("无效排位不得存在七曜结算记录");
            }

            var audits = _data.RankedIntegrityAudits.Where(row => string.Equals(row.MatchId,
                payload.MatchId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (audits.Length != 1)
                throw new InvalidDataException("排位完整性账本必须恰有一条对应记录");
            var audit = audits[0];
            var duration = Math.Max(0L, (long)(payload.EndedAt - payload.StartedAt).TotalMilliseconds);
            if (audit.FirstAccountId != payload.FirstAccountId
                || audit.SecondAccountId != payload.SecondAccountId
                || audit.Winner != payload.Winner
                || !string.Equals(audit.FirstMasterId, payload.FirstMasterId,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(audit.SecondMasterId, payload.SecondMasterId,
                    StringComparison.OrdinalIgnoreCase)
                || audit.DurationMs != duration
                || audit.MeaningfulCommandCount != payload.MeaningfulCommandCount
                || !string.Equals(audit.ConclusionKind, payload.ConclusionKind,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(audit.FirstNetworkFingerprint,
                    NormalizeNetworkFingerprint(payload.FirstNetworkFingerprint), StringComparison.Ordinal)
                || !string.Equals(audit.SecondNetworkFingerprint,
                    NormalizeNetworkFingerprint(payload.SecondNetworkFingerprint), StringComparison.Ordinal)
                || audit.EvidenceVersion >= 2 && (!string.Equals(audit.FirstBrowserFingerprint,
                        NormalizeBrowserFingerprint(payload.FirstBrowserFingerprint), StringComparison.Ordinal)
                    || !string.Equals(audit.SecondBrowserFingerprint,
                        NormalizeBrowserFingerprint(payload.SecondBrowserFingerprint), StringComparison.Ordinal)
                    || audit.FinalRound != payload.FinalRound))
                throw new InvalidOperationException("排位完整性账本与 outbox 幂等载荷冲突");
        }
    }

    internal bool CanReplayMissingRankedSettlement(L12RankedSettlementEnvelope payload,
        out string reason)
    {
        lock (_gate)
        {
            if (_data.RankedSettlements.Any(row => string.Equals(row.MatchId, payload.MatchId,
                    StringComparison.OrdinalIgnoreCase))
                || _data.RankedIntegrityAudits.Any(row => string.Equals(row.MatchId, payload.MatchId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                reason = "同一 matchId 已存在部分或冲突平台记录";
                return false;
            }
            if (payload.Winner is null)
            {
                // 无效局与同意平局均不改变七曜与主宰统计；缺失账本可在任意后续时点安全补写。
                reason = string.Empty;
                return true;
            }
            var hasLaterDependentSettlement = _data.RankedSettlements.Any(row =>
                (row.AccountId == payload.FirstAccountId || row.AccountId == payload.SecondAccountId)
                && row.SettledAt > payload.EndedAt);
            if (hasLaterDependentSettlement)
            {
                reason = "平台已存在依赖该玩家旧分值的更晚结算，拒绝乱序补账";
                return false;
            }
            reason = string.Empty;
            return true;
        }
    }

    private bool EnsureRankedIntegrityAuditLocked(string matchId, string firstAccountId,
        string secondAccountId, int? winner, string? firstMasterId, string? secondMasterId,
        L12RankedIntegrityContext? context)
    {
        var normalizedFirstMaster = firstMasterId?.Trim() ?? string.Empty;
        var normalizedSecondMaster = secondMasterId?.Trim() ?? string.Empty;
        var existing = _data.RankedIntegrityAudits.FirstOrDefault(row => string.Equals(row.MatchId, matchId,
            StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var expectedConclusion = string.IsNullOrWhiteSpace(context?.ConclusionKind)
                ? "unknown"
                : context.ConclusionKind.Trim().ToLowerInvariant();
            if (existing.FirstAccountId != firstAccountId || existing.SecondAccountId != secondAccountId
                || existing.Winner != winner
                || (!string.IsNullOrWhiteSpace(existing.FirstMasterId)
                    && !string.Equals(existing.FirstMasterId, normalizedFirstMaster, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(existing.SecondMasterId)
                    && !string.Equals(existing.SecondMasterId, normalizedSecondMaster, StringComparison.OrdinalIgnoreCase))
                || (context is not null && !string.Equals(existing.ConclusionKind, expectedConclusion,
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("排位对局重放签名与完整性账本冲突");
            return false;
        }

        var signals = new List<string>();
        var durationMs = 0L;
        var meaningful = 0;
        var conclusion = "unknown";
        var firstNetwork = string.Empty;
        var secondNetwork = string.Empty;
        var firstBrowser = string.Empty;
        var secondBrowser = string.Empty;
        var finalRound = 0;
        if (context is not null)
        {
            durationMs = Math.Max(0L, (long)(context.EndedAt - context.StartedAt).TotalMilliseconds);
            meaningful = Math.Max(0, context.MeaningfulCommandCount);
            conclusion = string.IsNullOrWhiteSpace(context.ConclusionKind)
                ? "unknown" : context.ConclusionKind.Trim().ToLowerInvariant();
            firstNetwork = NormalizeNetworkFingerprint(context.FirstNetworkFingerprint);
            secondNetwork = NormalizeNetworkFingerprint(context.SecondNetworkFingerprint);
            firstBrowser = NormalizeBrowserFingerprint(context.FirstBrowserFingerprint);
            secondBrowser = NormalizeBrowserFingerprint(context.SecondBrowserFingerprint);
            finalRound = Math.Max(0, context.FinalRound);
            if (!string.IsNullOrEmpty(firstNetwork) && FixedNetworkEquals(firstNetwork, secondNetwork))
                signals.Add("linked-network");
            if (!string.IsNullOrEmpty(firstBrowser) && FixedFingerprintEquals(firstBrowser, secondBrowser))
                signals.Add("linked-browser");
            if (durationMs <= (long)RankedVeryShortMatch.TotalMilliseconds)
                signals.Add("very-short-match");
            if (meaningful == 0) signals.Add("no-meaningful-actions");
            // 投降本身是正常规则结果；只有与极短时长或零有效操作复合时才是审计信号。
            if (conclusion == "surrender"
                && (durationMs <= (long)RankedVeryShortMatch.TotalMilliseconds || meaningful == 0))
                signals.Add("abnormal-surrender");
            else if (conclusion.Contains("timeout", StringComparison.Ordinal))
                signals.Add("abnormal-timeout");
        }
        var rewardHeld = context is not null && winner is 0 or 1
            && IsRepeatedUnilateralExtremePairLocked(firstAccountId, secondAccountId, winner.Value,
                durationMs, meaningful, context.EndedAt);
        if (rewardHeld) signals.Add("repeated-unilateral-extreme-pair");
        if (context is not null && winner is 0 or 1)
            AddAccountPatternSignalsLocked(signals, firstAccountId, secondAccountId, winner.Value,
                durationMs, meaningful, finalRound, firstNetwork, secondNetwork,
                firstBrowser, secondBrowser, context.EndedAt);
        var review = rewardHeld
            || signals.Contains("repeated-padded-transfer", StringComparer.Ordinal)
            || signals.Contains("linked-loser-cluster", StringComparer.Ordinal)
            || signals.Contains("unilateral-score-transfer", StringComparer.Ordinal)
                && signals.Contains("repeated-pair-day", StringComparer.Ordinal)
            || signals.Count(code => code is not ("linked-network" or "linked-browser")) >= 2;
        _data.RankedIntegrityAudits.Add(new RankedIntegrityAuditRow
        {
            EvidenceVersion = 2,
            MatchId = matchId,
            SeasonId = RequireOperationsConfig().Season.Id,
            FirstAccountId = firstAccountId,
            SecondAccountId = secondAccountId,
            Winner = winner,
            FirstMasterId = normalizedFirstMaster,
            SecondMasterId = normalizedSecondMaster,
            DurationMs = durationMs,
            MeaningfulCommandCount = meaningful,
            ConclusionKind = conclusion,
            FirstNetworkFingerprint = firstNetwork,
            SecondNetworkFingerprint = secondNetwork,
            FirstBrowserFingerprint = firstBrowser,
            SecondBrowserFingerprint = secondBrowser,
            FinalRound = finalRound,
            Signals = signals,
            ReviewRecommended = review,
            RewardHeld = rewardHeld,
            // 只有严格复合信号可暂扣当前新奖励；绝不自动封禁或追扣旧分。
            Enforcement = rewardHeld ? "reward-held" : "none",
            EndedAt = context?.EndedAt.ToUniversalTime() ?? DateTimeOffset.UtcNow,
        });
        return true;
    }

    private bool ShouldHoldRankedRewardLocked(string firstAccountId, string secondAccountId, int winner,
        L12RankedIntegrityContext? context)
    {
        if (context is null) return false;
        var durationMs = Math.Max(0L, (long)(context.EndedAt - context.StartedAt).TotalMilliseconds);
        return IsRepeatedUnilateralExtremePairLocked(firstAccountId, secondAccountId, winner,
            durationMs, Math.Max(0, context.MeaningfulCommandCount), context.EndedAt);
    }

    private bool IsRepeatedUnilateralExtremePairLocked(string firstAccountId, string secondAccountId,
        int winner, long durationMs, int meaningfulCommandCount, DateTimeOffset endedAt)
    {
        if (durationMs > (long)RankedExtremeShortMatch.TotalMilliseconds || meaningfulCommandCount != 0)
            return false;
        var winningAccountId = winner == 0 ? firstAccountId : secondAccountId;
        var cutoff = endedAt.ToUniversalTime() - RankedRepeatedPairWindow;
        var previous = _data.RankedIntegrityAudits.Count(row =>
        {
            var samePair = row.FirstAccountId.Equals(firstAccountId, StringComparison.OrdinalIgnoreCase)
                    && row.SecondAccountId.Equals(secondAccountId, StringComparison.OrdinalIgnoreCase)
                || row.FirstAccountId.Equals(secondAccountId, StringComparison.OrdinalIgnoreCase)
                    && row.SecondAccountId.Equals(firstAccountId, StringComparison.OrdinalIgnoreCase);
            if (!samePair || row.Winner is not (0 or 1)
                || row.DurationMs > (long)RankedExtremeShortMatch.TotalMilliseconds
                || row.MeaningfulCommandCount != 0) return false;
            var priorWinner = row.Winner == 0 ? row.FirstAccountId : row.SecondAccountId;
            var priorEndedAt = row.EndedAt == default ? row.CreatedAt : row.EndedAt;
            return priorWinner.Equals(winningAccountId, StringComparison.OrdinalIgnoreCase)
                && priorEndedAt.ToUniversalTime() >= cutoff
                && priorEndedAt.ToUniversalTime() <= endedAt.ToUniversalTime();
        });
        // 当前局是同一胜者在窗口内第三局时才暂扣；重复、网络关联或普通投降单独均不足以触发。
        return previous >= 2;
    }

    internal int RankedPairPriority(string accountId, string otherId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var cutoff = now.ToUniversalTime() - RankedRepeatedPairWindow;
            return _data.RankedIntegrityAudits.Count(row =>
            {
                var endedAt = row.EndedAt == default ? row.CreatedAt : row.EndedAt;
                return endedAt.ToUniversalTime() >= cutoff && endedAt.ToUniversalTime() <= now.ToUniversalTime()
                    && (row.FirstAccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase)
                            && row.SecondAccountId.Equals(otherId, StringComparison.OrdinalIgnoreCase)
                        || row.FirstAccountId.Equals(otherId, StringComparison.OrdinalIgnoreCase)
                            && row.SecondAccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase));
            });
        }
    }

    private static void ValidateRankedIdentity(string matchId, string firstAccountId,
        string secondAccountId, int? winner)
    {
        if (string.IsNullOrWhiteSpace(matchId)) throw new ArgumentException("排位对局ID不能为空");
        if (string.IsNullOrWhiteSpace(firstAccountId) || string.IsNullOrWhiteSpace(secondAccountId)
            || string.Equals(firstAccountId, secondAccountId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("排位结算必须包含两个不同账号");
        if (winner is not null and not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(winner));
    }

    private static string NormalizeNetworkFingerprint(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized.StartsWith("net-v1:", StringComparison.Ordinal)
            && normalized.Length == 71 && normalized[7..].All(Uri.IsHexDigit) ? normalized : string.Empty;
    }

    private static bool FixedNetworkEquals(string left, string right)
        => FixedFingerprintEquals(left, right);

    private static string NormalizeBrowserFingerprint(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized.StartsWith("browser-v1:", StringComparison.Ordinal)
            && normalized.Length == 75 && normalized[11..].All(Uri.IsHexDigit) ? normalized : string.Empty;
    }

    private static bool FixedFingerprintEquals(string left, string right)
        => !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right)
           && left.Length == right.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private void AddAccountPatternSignalsLocked(List<string> signals, string firstAccountId,
        string secondAccountId, int winner, long durationMs, int meaningful, int finalRound,
        string firstNetwork, string secondNetwork, string firstBrowser, string secondBrowser,
        DateTimeOffset endedAt)
    {
        var normalizedEndedAt = endedAt.ToUniversalTime();
        var winningAccountId = winner == 0 ? firstAccountId : secondAccountId;
        var losingAccountId = winner == 0 ? secondAccountId : firstAccountId;
        var pairCutoff = normalizedEndedAt - RankedPatternWindow;
        var pairHistory = _data.RankedIntegrityAudits.Where(row =>
        {
            var rowEndedAt = (row.EndedAt == default ? row.CreatedAt : row.EndedAt).ToUniversalTime();
            return rowEndedAt >= pairCutoff && rowEndedAt <= normalizedEndedAt
                && IsSameRankedPair(row, firstAccountId, secondAccountId) && row.Winner is 0 or 1;
        }).ToArray();
        var pairCount = pairHistory.Length + 1;
        var unilateralWins = pairHistory.Count(row => WinnerAccountId(row).Equals(winningAccountId,
            StringComparison.OrdinalIgnoreCase)) + 1;
        if (pairCount >= 3) signals.Add("repeated-pair-day");
        if (pairCount >= 3 && unilateralWins >= 3 && unilateralWins * 4 >= pairCount * 3)
            signals.Add("unilateral-score-transfer");

        if (IsLowProgressPaddedMatch(durationMs, meaningful, finalRound))
        {
            var priorPaddedWins = pairHistory.Count(row => WinnerAccountId(row).Equals(winningAccountId,
                    StringComparison.OrdinalIgnoreCase)
                && IsLowProgressPaddedMatch(row.DurationMs, row.MeaningfulCommandCount, row.FinalRound));
            if (priorPaddedWins >= 2) signals.Add("repeated-padded-transfer");
        }

        var losingNetwork = winner == 0 ? secondNetwork : firstNetwork;
        var losingBrowser = winner == 0 ? secondBrowser : firstBrowser;
        if (string.IsNullOrEmpty(losingNetwork) && string.IsNullOrEmpty(losingBrowser)) return;
        var clusterCutoff = normalizedEndedAt - RankedClusterWindow;
        var beneficiaryHistory = _data.RankedIntegrityAudits.Where(row =>
            {
                var rowEndedAt = (row.EndedAt == default ? row.CreatedAt : row.EndedAt).ToUniversalTime();
                return rowEndedAt >= clusterCutoff && rowEndedAt <= normalizedEndedAt
                    && row.Winner is 0 or 1
                    && WinnerAccountId(row).Equals(winningAccountId, StringComparison.OrdinalIgnoreCase);
            }).ToArray();
        var linkedLosingAccounts = beneficiaryHistory
            .Select(row => (AccountId: LoserAccountId(row),
                Network: FingerprintForAccount(row, LoserAccountId(row), browser: false),
                Browser: FingerprintForAccount(row, LoserAccountId(row), browser: true)))
            .Where(item => FingerprintMatches(item.Network, losingNetwork)
                || FingerprintMatches(item.Browser, losingBrowser))
            .Select(item => item.AccountId).Append(losingAccountId)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (linkedLosingAccounts.Length >= 2 && beneficiaryHistory.Length + 1 >= 3)
            signals.Add("linked-loser-cluster");
    }

    private static bool IsLowProgressPaddedMatch(long durationMs, int meaningful, int finalRound)
        => durationMs >= (long)RankedPaddedMinimum.TotalMilliseconds
           && durationMs <= (long)RankedPaddedMaximum.TotalMilliseconds
           && meaningful <= 6 && finalRound <= 3;

    private static bool IsSameRankedPair(RankedIntegrityAuditRow row, string firstAccountId,
        string secondAccountId)
        => row.FirstAccountId.Equals(firstAccountId, StringComparison.OrdinalIgnoreCase)
               && row.SecondAccountId.Equals(secondAccountId, StringComparison.OrdinalIgnoreCase)
           || row.FirstAccountId.Equals(secondAccountId, StringComparison.OrdinalIgnoreCase)
               && row.SecondAccountId.Equals(firstAccountId, StringComparison.OrdinalIgnoreCase);

    private static string WinnerAccountId(RankedIntegrityAuditRow row)
        => row.Winner == 0 ? row.FirstAccountId : row.SecondAccountId;

    private static string LoserAccountId(RankedIntegrityAuditRow row)
        => row.Winner == 0 ? row.SecondAccountId : row.FirstAccountId;

    private static string FingerprintForAccount(RankedIntegrityAuditRow row, string accountId,
        bool browser)
        => row.FirstAccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase)
            ? browser ? row.FirstBrowserFingerprint : row.FirstNetworkFingerprint
            : browser ? row.SecondBrowserFingerprint : row.SecondNetworkFingerprint;

    private static bool FingerprintMatches(string first, string second)
        => !string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(second)
            && FixedFingerprintEquals(first, second);

    private L12RankedIntegrityAuditView RankedIntegrityView(RankedIntegrityAuditRow row)
    {
        var linked = !string.IsNullOrEmpty(row.FirstNetworkFingerprint)
            && FixedNetworkEquals(row.FirstNetworkFingerprint, row.SecondNetworkFingerprint);
        var reference = linked ? row.FirstNetworkFingerprint[..Math.Min(23, row.FirstNetworkFingerprint.Length)] : null;
        var browserLinked = !string.IsNullOrEmpty(row.FirstBrowserFingerprint)
            && FixedFingerprintEquals(row.FirstBrowserFingerprint, row.SecondBrowserFingerprint);
        var browserReference = browserLinked
            ? row.FirstBrowserFingerprint[..Math.Min(27, row.FirstBrowserFingerprint.Length)] : null;
        return new L12RankedIntegrityAuditView(row.Id, row.MatchId, row.SeasonId,
            row.FirstAccountId, AccountName(row.FirstAccountId), row.SecondAccountId,
            AccountName(row.SecondAccountId), row.Winner, row.DurationMs, row.MeaningfulCommandCount,
            row.ConclusionKind, linked, reference, browserLinked, browserReference, row.FinalRound,
            row.Signals.Select(code =>
                new L12RankedIntegritySignalView(code, IntegritySignalLabel(code))).ToArray(),
            row.ReviewRecommended, RankedIntegrityDispositionLocked(row.MatchId), row.Enforcement, row.CreatedAt);
    }

    private string RankedIntegrityDispositionLocked(string matchId)
        => _data.RankedIntegrityDecisions
            .Where(row => row.Disposition != "revoked"
                && row.MatchIds.Contains(matchId, StringComparer.OrdinalIgnoreCase)
                && !IsDecisionRevokedLocked(row.Id))
            .OrderByDescending(row => row.Revision)
            .Select(row => row.Disposition)
            .FirstOrDefault() ?? "unreviewed";

    private static bool IsTerminalRankedIntegrityDisposition(string disposition)
        => disposition is "normal" or "insufficient" or "system-error" or "confirmed";

    private static string IntegritySignalLabel(string code) => code switch
    {
        "linked-network" => "双方网络关联（仅一项证据）",
        "linked-browser" => "同一浏览器标识（仅一项证据）",
        "very-short-match" => "异常极短对局",
        "no-meaningful-actions" => "没有有效规则操作",
        "abnormal-surrender" => "极短或无操作投降",
        "abnormal-timeout" => "异常超时结束",
        "repeated-unilateral-extreme-pair" => "短时窗口内同一方重复零操作获胜（奖励暂扣）",
        "repeated-pair-day" => "24小时内双方重复匹配",
        "unilateral-score-transfer" => "重复对局胜负长期单向",
        "repeated-padded-transfer" => "多局低操作对局疑似刻意拖延后单向结算",
        "linked-loser-cluster" => "同一获益账号关联多个身份相近的失败账号",
        _ => code,
    };
}
