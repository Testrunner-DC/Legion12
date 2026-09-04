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
    IReadOnlyList<L12RankedIntegritySignalView> Signals,
    bool ReviewRecommended,
    string Enforcement,
    DateTimeOffset CreatedAt);

internal sealed record L12RankedIntegrityContext(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int MeaningfulCommandCount,
    string ConclusionKind,
    string? FirstNetworkFingerprint,
    string? SecondNetworkFingerprint);

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

    private sealed class RankedIntegrityAuditRow
    {
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
        public List<string> Signals { get; set; } = [];
        public bool ReviewRecommended { get; set; }
        public string Enforcement { get; set; } = "none";
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
            if (reviewOnly) rows = rows.Where(row => row.ReviewRecommended);
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
        if (first is null || second is null || first.Won != (winner == 0) || second.Won != (winner == 1))
            throw new InvalidOperationException("排位对局重放参数与已结算结果冲突");
        var addedAudit = EnsureRankedIntegrityAuditLocked(matchId, firstAccountId, secondAccountId, winner,
            firstMasterId, secondMasterId, context);
        if (addedAudit) Save(false);
        pair = new L12RankedSettlementPair(ToView(first), ToView(second), []);
        return true;
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
            if (existing.FirstAccountId != firstAccountId || existing.SecondAccountId != secondAccountId
                || existing.Winner != winner
                || (!string.IsNullOrWhiteSpace(existing.FirstMasterId)
                    && !string.Equals(existing.FirstMasterId, normalizedFirstMaster, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(existing.SecondMasterId)
                    && !string.Equals(existing.SecondMasterId, normalizedSecondMaster, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("排位对局重放签名与完整性账本冲突");
            return false;
        }

        var signals = new List<string>();
        var durationMs = 0L;
        var meaningful = 0;
        var conclusion = "unknown";
        var firstNetwork = string.Empty;
        var secondNetwork = string.Empty;
        if (context is not null)
        {
            durationMs = Math.Max(0L, (long)(context.EndedAt - context.StartedAt).TotalMilliseconds);
            meaningful = Math.Max(0, context.MeaningfulCommandCount);
            conclusion = string.IsNullOrWhiteSpace(context.ConclusionKind)
                ? "unknown" : context.ConclusionKind.Trim().ToLowerInvariant();
            firstNetwork = NormalizeNetworkFingerprint(context.FirstNetworkFingerprint);
            secondNetwork = NormalizeNetworkFingerprint(context.SecondNetworkFingerprint);
            if (!string.IsNullOrEmpty(firstNetwork) && FixedNetworkEquals(firstNetwork, secondNetwork))
                signals.Add("linked-network");
            if (durationMs <= (long)RankedVeryShortMatch.TotalMilliseconds)
                signals.Add("very-short-match");
            if (meaningful == 0) signals.Add("no-meaningful-actions");
            if (conclusion == "surrender") signals.Add("abnormal-surrender");
            else if (conclusion.Contains("timeout", StringComparison.Ordinal))
                signals.Add("abnormal-timeout");
        }
        var review = signals.Count >= 2 && signals.Any(code => code != "linked-network");
        _data.RankedIntegrityAudits.Add(new RankedIntegrityAuditRow
        {
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
            Signals = signals,
            ReviewRecommended = review,
            // 信号只进入人工审计；绝不在这里扣分、封禁或改变匹配分。
            Enforcement = "none",
        });
        return true;
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
        => !string.IsNullOrEmpty(right) && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private L12RankedIntegrityAuditView RankedIntegrityView(RankedIntegrityAuditRow row)
    {
        var linked = !string.IsNullOrEmpty(row.FirstNetworkFingerprint)
            && FixedNetworkEquals(row.FirstNetworkFingerprint, row.SecondNetworkFingerprint);
        var reference = linked ? row.FirstNetworkFingerprint[..Math.Min(23, row.FirstNetworkFingerprint.Length)] : null;
        return new L12RankedIntegrityAuditView(row.Id, row.MatchId, row.SeasonId,
            row.FirstAccountId, AccountName(row.FirstAccountId), row.SecondAccountId,
            AccountName(row.SecondAccountId), row.Winner, row.DurationMs, row.MeaningfulCommandCount,
            row.ConclusionKind, linked, reference, row.Signals.Select(code =>
                new L12RankedIntegritySignalView(code, IntegritySignalLabel(code))).ToArray(),
            row.ReviewRecommended, row.Enforcement, row.CreatedAt);
    }

    private static string IntegritySignalLabel(string code) => code switch
    {
        "linked-network" => "双方网络关联（仅一项证据）",
        "very-short-match" => "异常极短对局",
        "no-meaningful-actions" => "没有有效规则操作",
        "abnormal-surrender" => "极短或无操作投降",
        "abnormal-timeout" => "异常超时结束",
        _ => code,
    };
}
