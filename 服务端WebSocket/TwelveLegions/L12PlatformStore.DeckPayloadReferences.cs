using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // One immutable compact body per hash belongs to the complete committed
    // generation. Resolving a body never reaches a potentially newer database.
    private sealed record CompactDeckPayloadFact(string MasterId, int Format, string PayloadJson);
    internal Action<string>? DeckPayloadExpansionObserver { get; set; }

    private static CompactDeckPayloadFact RequireDeckPayload(DataFile data, string hash, string master)
    {
        if (string.IsNullOrWhiteSpace(hash) || !data.DeckPayloads.TryGetValue(hash, out var fact)
            || fact is null || fact.MasterId != master || fact.Format != L12DeckPayloadCodec.CurrentFormat)
            throw new InvalidDataException("牌库正文引用不属于完整的已提交状态");
        return fact;
    }

    private static string CaptureDeckPayload(DataFile data, string master, string hash,
        IReadOnlyList<string> main, IReadOnlyList<string> morale, IReadOnlyList<string> special)
    {
        if (!string.IsNullOrWhiteSpace(hash) && main.Count == 0 && morale.Count == 0 && special.Count == 0)
        {
            if (data.DeckPayloads.ContainsKey(hash))
            {
                RequireDeckPayload(data, hash, master);
                return hash;
            }
            // A complete legacy export can legitimately contain an empty body.
            // A missing non-empty reference must never be converted to that body.
            if (NormalizeDeckPayload(master, main, morale, special).Hash != hash)
                throw new InvalidDataException("完整状态缺少引用的牌库正文");
        }
        var normalized = NormalizeDeckPayload(master, main, morale, special);
        if (!string.IsNullOrWhiteSpace(hash) && hash != normalized.Hash)
            throw new InvalidDataException("牌库正文与其已捕获引用不一致");
        var fact = new CompactDeckPayloadFact(normalized.MasterId, L12DeckPayloadCodec.CurrentFormat,
            L12DeckPayloadCodec.EncodeLegacyJson(normalized.MainJson, normalized.MoraleJson, normalized.SpecialJson));
        if (data.DeckPayloads.TryGetValue(normalized.Hash, out var existing))
        {
            var decoded = L12DeckPayloadCodec.Decode(existing.Format, existing.PayloadJson);
            L12DeckPayloadCodec.ValidateHash(normalized.Hash, existing.MasterId, decoded);
            if (existing.MasterId != fact.MasterId || decoded.MainJson != normalized.MainJson
                || decoded.MoraleJson != normalized.MoraleJson || decoded.SpecialJson != normalized.SpecialJson)
                throw new InvalidDataException("同一牌库正文哈希对应不同的紧凑事实");
        }
        else data.DeckPayloads.Add(normalized.Hash, fact);
        return normalized.Hash;
    }

    private static string CaptureDeckPayload(DataFile data, DeckRow row)
    {
        row.PayloadHash = CaptureDeckPayload(data, row.MasterId, row.PayloadHash,
            row.CardIds, row.MoraleIds, row.SpecialIds);
        row.MasterId = data.DeckPayloads[row.PayloadHash].MasterId;
        return row.PayloadHash;
    }
    private static string CaptureDeckPayload(DataFile data, PublishedDeckRow row)
    {
        row.PayloadHash = CaptureDeckPayload(data, row.MasterId, row.PayloadHash,
            row.CardIds, row.MoraleIds, row.SpecialIds);
        row.MasterId = data.DeckPayloads[row.PayloadHash].MasterId;
        return row.PayloadHash;
    }
    private static string CaptureDeckPayload(DataFile data, TournamentDeckSnapshotRow row)
    {
        row.PayloadHash = CaptureDeckPayload(data, row.MasterId, row.PayloadHash,
            row.CardIds, row.MoraleIds, row.SpecialIds);
        row.MasterId = data.DeckPayloads[row.PayloadHash].MasterId;
        return row.PayloadHash;
    }

    private static string DeckBenchJson(DeckRow row)
        => row.BenchIds.Count > 0 ? CompactDeckCardsJson(row.BenchIds) : row.BenchJson;

    private static void CompactRuntimeDeckDomain(DataFile data)
    {
        foreach (var row in data.Decks)
        {
            CaptureDeckPayload(data, row);
            row.BenchJson = DeckBenchJson(row);
            // Validate compact counts while preserving the recovery budget's
            // distinction between structure validation and actual expansion.
            _ = ReadRecoveryCardCounts(row.BenchJson);
            row.CardIds = []; row.MoraleIds = []; row.SpecialIds = []; row.BenchIds = [];
        }
        foreach (var row in data.PublishedDecks)
        {
            CaptureDeckPayload(data, row);
            row.CardIds = []; row.MoraleIds = []; row.SpecialIds = [];
        }
        foreach (var row in data.Tournaments.SelectMany(t => t.Participants).Select(p => p.Deck))
        {
            if (string.IsNullOrWhiteSpace(row.MasterId)) continue;
            CaptureDeckPayload(data, row);
            row.CardIds = []; row.MoraleIds = []; row.SpecialIds = [];
        }
        var referenced = data.Decks.Select(row => row.PayloadHash)
            .Concat(data.PublishedDecks.Select(row => row.PayloadHash))
            .Concat(data.Tournaments.SelectMany(t => t.Participants).Select(p => p.Deck.PayloadHash))
            .Where(hash => !string.IsNullOrWhiteSpace(hash)).ToHashSet(StringComparer.Ordinal);
        // Retention in SQLite is independent of the current runtime generation.
        // History/tombstones/orphans are never deleted from persistent storage.
        foreach (var hash in data.DeckPayloads.Keys.Where(hash => !referenced.Contains(hash)).ToArray())
            data.DeckPayloads.Remove(hash);
    }

    private static void ValidateCompactRuntimeDeckDomain(DataFile data)
    {
        foreach (var (hash, fact) in data.DeckPayloads)
        {
            if (fact is null) throw new InvalidDataException("完整回滚状态的紧凑正文为空");
            var decoded = L12DeckPayloadCodec.Decode(fact.Format, fact.PayloadJson);
            L12DeckPayloadCodec.ValidateHash(hash, fact.MasterId, decoded);
        }
        CompactRuntimeDeckDomain(data);
    }

    private static Dictionary<string, CompactDeckPayloadFact> ReadCompactRuntimeDeckPayloads(
        SqliteConnection connection, SqliteTransaction? transaction, DataFile data)
    {
        var referenced = data.Tournaments.SelectMany(t => t.Participants).Select(p => p.Deck.PayloadHash)
            .Where(hash => !string.IsNullOrWhiteSpace(hash)).ToHashSet(StringComparer.Ordinal);
        using (var refs = connection.CreateCommand())
        {
            refs.Transaction = transaction;
            refs.CommandText = """
                SELECT payload_hash FROM account_decks WHERE is_deleted=0
                UNION SELECT current_payload_hash FROM published_decks WHERE is_deleted=0
                UNION SELECT payload_hash FROM tournament_deck_refs;
                """;
            using var rows = refs.ExecuteReader();
            while (rows.Read()) referenced.Add(rows.GetString(0));
        }
        var payloads = new Dictionary<string, CompactDeckPayloadFact>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT payload_hash,master_id,payload_format,payload_json FROM deck_payloads;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var hash = reader.GetString(0);
            var fact = new CompactDeckPayloadFact(reader.GetString(1), reader.GetInt32(2), reader.GetString(3));
            var decoded = L12DeckPayloadCodec.Decode(fact.Format, fact.PayloadJson);
            L12DeckPayloadCodec.ValidateHash(hash, fact.MasterId, decoded);
            if (referenced.Contains(hash)) payloads.Add(hash, fact);
        }
        return payloads;
    }

    private static NormalizedDeckPayload ReadReferencedDeckPayload(DataFile data, string hash)
    {
        if (!data.DeckPayloads.TryGetValue(hash, out var fact))
            throw new InvalidDataException("完整状态缺少引用的牌库正文");
        var decoded = L12DeckPayloadCodec.Decode(fact.Format, fact.PayloadJson);
        L12DeckPayloadCodec.ValidateHash(hash, fact.MasterId, decoded);
        return new(hash, fact.MasterId, decoded.MainJson, decoded.MoraleJson, decoded.SpecialJson, [], [], []);
    }

    // Online materialization budgets, not limits on durable compact facts.
    // Check the actual consumer copies and bench before allocating any cards.
    private static void CheckOnlineDeckExpansionBudget(NormalizedDeckPayload payload,
        string? benchJson, ref long total)
    {
        var copies = checked(CountRecoveryCards(ReadRecoveryCardCounts(payload.MainJson))
            + CountRecoveryCards(ReadRecoveryCardCounts(payload.MoraleJson))
            + CountRecoveryCards(ReadRecoveryCardCounts(payload.SpecialJson))
            + (benchJson is null ? 0 : CountRecoveryCards(ReadRecoveryCardCounts(benchJson))));
        if (copies > RecoveryExpandedRowCardLimit || copies > RecoveryExpandedTotalCardLimit - total)
            throw new L12PlatformStorageUnavailableException(
                "在线牌库展开资源预算超限；紧凑正文和引用已保留，需要有资源的离线读取或导出");
        total = checked(total + copies);
    }

    private static void PreflightRuntimeDeckProjection(DataFile data,
        IEnumerable<(string Hash, string? BenchJson)> consumers)
    {
        var total = 0L;
        foreach (var (hash, bench) in consumers)
            CheckOnlineDeckExpansionBudget(ReadReferencedDeckPayload(data, hash), bench, ref total);
    }

    private static void PreflightOnlineDeckProjection(IEnumerable<NormalizedDeckPayload> consumers)
    {
        var total = 0L;
        foreach (var payload in consumers)
            CheckOnlineDeckExpansionBudget(payload, null, ref total);
    }

    private static NormalizedDeckPayload ReadExportDeckPayload(DataFile data, string master, string hash,
        IReadOnlyList<string> main, IReadOnlyList<string> morale, IReadOnlyList<string> special)
    {
        if (!string.IsNullOrWhiteSpace(hash) && main.Count == 0 && morale.Count == 0 && special.Count == 0)
        {
            RequireDeckPayload(data, hash, master);
            return ReadReferencedDeckPayload(data, hash);
        }
        var payload = NormalizeDeckPayload(master, main, morale, special);
        if (!string.IsNullOrWhiteSpace(hash) && hash != payload.Hash)
            throw new InvalidDataException("牌库正文与其已捕获引用不一致");
        return payload;
    }

    private static void PreflightFullDeckDomainExport(DataFile data)
    {
        var total = 0L;
        foreach (var row in data.Decks)
            CheckOnlineDeckExpansionBudget(ReadExportDeckPayload(data, row.MasterId, row.PayloadHash,
                row.CardIds, row.MoraleIds, row.SpecialIds), DeckBenchJson(row), ref total);
        foreach (var row in data.PublishedDecks)
            CheckOnlineDeckExpansionBudget(ReadExportDeckPayload(data, row.MasterId, row.PayloadHash,
                row.CardIds, row.MoraleIds, row.SpecialIds), null, ref total);
        foreach (var row in data.Tournaments.SelectMany(t => t.Participants).Select(p => p.Deck))
        {
            if (string.IsNullOrWhiteSpace(row.MasterId)) continue;
            CheckOnlineDeckExpansionBudget(ReadExportDeckPayload(data, row.MasterId, row.PayloadHash,
                row.CardIds, row.MoraleIds, row.SpecialIds), null, ref total);
        }
    }

    private NormalizedDeckPayload ExpandRuntimeDeckPayload(DataFile data, string hash, string? benchJson = null)
        => ExpandRuntimeDeckPayload(ReadReferencedDeckPayload(data, hash), benchJson);

    private NormalizedDeckPayload ExpandRuntimeDeckPayload(NormalizedDeckPayload payload, string? benchJson = null)
    {
        var total = 0L;
        CheckOnlineDeckExpansionBudget(payload, benchJson, ref total);
        DeckPayloadExpansionObserver?.Invoke(payload.Hash);
        return payload with
        {
            MainCards = ExpandCards(payload.MainJson), MoraleCards = ExpandCards(payload.MoraleJson),
            SpecialCards = ExpandCards(payload.SpecialJson),
        };
    }

    // Existing migration/rollback tooling receives its complete legacy JSON only
    // at the explicit export boundary. Expanded copies never enter live state.
    private static string SerializeFullDeckDomainBackup(DataFile data)
    {
        PreflightFullDeckDomainExport(data);
        CompactRuntimeDeckDomain(data);
        var copy = JsonSerializer.Deserialize<DataFile>(JsonSerializer.Serialize(data, PlatformMigrationJsonOptions),
            PlatformMigrationJsonOptions) ?? throw new InvalidDataException("完整牌库备份复制失败");
        foreach (var row in copy.Decks)
        {
            var payload = ReadReferencedDeckPayload(copy, row.PayloadHash);
            row.CardIds = ExpandCards(payload.MainJson).ToList(); row.MoraleIds = ExpandCards(payload.MoraleJson).ToList();
            row.SpecialIds = ExpandCards(payload.SpecialJson).ToList(); row.BenchIds = ExpandCards(row.BenchJson).ToList();
        }
        foreach (var row in copy.PublishedDecks)
        {
            var payload = ReadReferencedDeckPayload(copy, row.PayloadHash);
            row.CardIds = ExpandCards(payload.MainJson).ToList(); row.MoraleIds = ExpandCards(payload.MoraleJson).ToList();
            row.SpecialIds = ExpandCards(payload.SpecialJson).ToList();
        }
        foreach (var row in copy.Tournaments.SelectMany(t => t.Participants).Select(p => p.Deck))
        {
            if (string.IsNullOrWhiteSpace(row.PayloadHash)) continue;
            var payload = ReadReferencedDeckPayload(copy, row.PayloadHash);
            row.CardIds = ExpandCards(payload.MainJson).ToList(); row.MoraleIds = ExpandCards(payload.MoraleJson).ToList();
            row.SpecialIds = ExpandCards(payload.SpecialJson).ToList();
        }
        copy.DeckPayloads.Clear();
        return JsonSerializer.Serialize(copy, PlatformMigrationJsonOptions);
    }
}
