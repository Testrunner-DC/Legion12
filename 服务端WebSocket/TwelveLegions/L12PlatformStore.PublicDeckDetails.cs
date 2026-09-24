using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    private const int PublicDeckGuideSectionLimit = 1200;
    private const int PublicDeckMatchupSectionLimit = 800;
    private const int PublicDeckMatchupLimit = 64;

    private sealed record StoredPublicDeckContent(L12PublicDeckGuideView Guide,
        IReadOnlyList<L12PublicDeckMatchupView> Matchups, int Revision, DateTimeOffset? UpdatedAt);

    public L12PublicDeckDetailsView? PublicDeckDetails(string publicationId)
    {
        lock (_gate)
        {
            var published = _data.PublishedDecks.FirstOrDefault(item => item.Id == publicationId);
            if (published is null) return null;
            using var connection = OpenDatabase(_databasePath, readOnly: true);
            var content = ReadPublicDeckContent(connection, publicationId);
            return new(content.Guide, content.Matchups, content.Revision, content.UpdatedAt,
                ReadPublicDeckVersions(connection, published), [], "unavailable",
                "尚无可证明绑定到该公开牌库版本的对局记录；不会用作者总战绩替代。" );
        }
    }

    public L12PublicDeckDetailsView? UpdatePublicDeckContent(string accountId, string publicationId,
        L12PublicDeckContentInput input)
    {
        lock (_gate)
        {
            var published = _data.PublishedDecks.FirstOrDefault(item => item.Id == publicationId);
            if (published is null) return null;
            if (!string.Equals(published.OwnerId, accountId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("只有公开牌库作者可以编辑指南和对局建议");

            var guide = NormalizeGuide(input.Guide);
            var matchups = NormalizeMatchups(input.Matchups);
            var guideJson = JsonSerializer.Serialize(guide);
            var matchupJson = JsonSerializer.Serialize(matchups);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{{\"guide\":{guideJson},\"matchups\":{matchupJson}}}"))).ToLowerInvariant();
            var now = DateTimeOffset.UtcNow;
            using var connection = OpenDatabase(_databasePath, readOnly: false);
            using var transaction = connection.BeginTransaction();
            var current = ReadPublicDeckContent(connection, publicationId, transaction);
            string? currentHash = null;
            using (var query = connection.CreateCommand())
            {
                query.Transaction = transaction;
                query.CommandText = "SELECT content_hash FROM published_deck_content_heads WHERE publication_id=$id;";
                query.Parameters.AddWithValue("$id", publicationId);
                currentHash = Convert.ToString(query.ExecuteScalar());
            }
            if (string.Equals(currentHash, hash, StringComparison.Ordinal))
            {
                transaction.Commit();
                return new(current.Guide, current.Matchups, current.Revision, current.UpdatedAt,
                    ReadPublicDeckVersions(connection, published), [], "unavailable",
                    "尚无可证明绑定到该公开牌库版本的对局记录；不会用作者总战绩替代。");
            }
            using (var payload = connection.CreateCommand())
            {
                payload.Transaction = transaction;
                payload.CommandText = """
                    INSERT OR IGNORE INTO published_deck_content_payloads(
                        content_hash,guide_json,matchups_json,created_utc)
                    VALUES($hash,$guide,$matchups,$created);
                    """;
                payload.Parameters.AddWithValue("$hash", hash);
                payload.Parameters.AddWithValue("$guide", guideJson);
                payload.Parameters.AddWithValue("$matchups", matchupJson);
                payload.Parameters.AddWithValue("$created", now.ToString("O"));
                payload.ExecuteNonQuery();
            }
            var revision = current.Revision + 1;
            using (var revisionCommand = connection.CreateCommand())
            {
                revisionCommand.Transaction = transaction;
                revisionCommand.CommandText = """
                    INSERT INTO published_deck_content_revisions(
                        publication_id,revision,content_hash,author_id,created_utc)
                    VALUES($id,$revision,$hash,$author,$created);
                    INSERT INTO published_deck_content_heads(publication_id,revision,content_hash,updated_utc)
                    VALUES($id,$revision,$hash,$created)
                    ON CONFLICT(publication_id) DO UPDATE SET
                        revision=excluded.revision,content_hash=excluded.content_hash,updated_utc=excluded.updated_utc;
                    """;
                revisionCommand.Parameters.AddWithValue("$id", publicationId);
                revisionCommand.Parameters.AddWithValue("$revision", revision);
                revisionCommand.Parameters.AddWithValue("$hash", hash);
                revisionCommand.Parameters.AddWithValue("$author", accountId);
                revisionCommand.Parameters.AddWithValue("$created", now.ToString("O"));
                revisionCommand.ExecuteNonQuery();
            }
            transaction.Commit();
            return new(guide, matchups, revision, now, ReadPublicDeckVersions(connection, published), [],
                "unavailable", "尚无可证明绑定到该公开牌库版本的对局记录；不会用作者总战绩替代。");
        }
    }

    private static L12PublicDeckGuideView NormalizeGuide(L12PublicDeckGuideView? input)
        => new(NormalizePublicDeckText(input?.BuildIdea, PublicDeckGuideSectionLimit, "构筑思路"),
            NormalizePublicDeckText(input?.Opening, PublicDeckGuideSectionLimit, "起手建议"),
            NormalizePublicDeckText(input?.KeyCards, PublicDeckGuideSectionLimit, "关键牌与配合"),
            NormalizePublicDeckText(input?.CommonSequence, PublicDeckGuideSectionLimit, "常见展开"),
            NormalizePublicDeckText(input?.Substitutions, PublicDeckGuideSectionLimit, "替换建议"));

    private static IReadOnlyList<L12PublicDeckMatchupView> NormalizeMatchups(
        IReadOnlyList<L12PublicDeckMatchupView>? input)
    {
        var submitted = input ?? [];
        foreach (var item in submitted.Where(item => string.IsNullOrWhiteSpace(item.OpponentMasterId)))
            if (!string.IsNullOrWhiteSpace(item.Notes) || !string.IsNullOrWhiteSpace(item.KeyCards)
                || !string.IsNullOrWhiteSpace(item.SuggestedSwaps))
                throw new ArgumentException("填写对局建议时必须选择敌方主宰");
        var rows = submitted.Where(item => !string.IsNullOrWhiteSpace(item.OpponentMasterId)).ToArray();
        if (rows.Length > PublicDeckMatchupLimit) throw new ArgumentException($"对局建议最多 {PublicDeckMatchupLimit} 个主宰");
        var result = new List<L12PublicDeckMatchupView>(rows.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var masterId = row.OpponentMasterId.Trim().ToUpperInvariant();
            if (masterId.Length > 64 || !seen.Add(masterId)) throw new ArgumentException("对局建议中的敌方主宰无效或重复");
            result.Add(new(masterId,
                NormalizePublicDeckText(row.Notes, PublicDeckMatchupSectionLimit, "对局思路"),
                NormalizePublicDeckText(row.KeyCards, PublicDeckMatchupSectionLimit, "对局关键牌"),
                NormalizePublicDeckText(row.SuggestedSwaps, PublicDeckMatchupSectionLimit, "换牌建议")));
        }
        return result.OrderBy(item => item.OpponentMasterId, StringComparer.Ordinal).ToArray();
    }

    private static string NormalizePublicDeckText(string? value, int limit, string label)
    {
        var normalized = (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Trim();
        if (normalized.Length > limit) throw new ArgumentException($"{label}最多 {limit} 个字符");
        if (normalized.Any(character => char.IsControl(character) && character is not '\n' and not '\t'))
            throw new ArgumentException($"{label}包含不支持的控制字符");
        if (normalized.Contains('<') || normalized.Contains('>')
            || normalized.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{label}只能填写纯文本");
        return normalized;
    }

    private static StoredPublicDeckContent ReadPublicDeckContent(SqliteConnection connection, string publicationId,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT payload.guide_json,payload.matchups_json,head.revision,head.updated_utc
            FROM published_deck_content_heads head
            JOIN published_deck_content_payloads payload ON payload.content_hash=head.content_hash
            WHERE head.publication_id=$id;
            """;
        command.Parameters.AddWithValue("$id", publicationId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new(EmptyGuide(), [], 0, null);
        return new(JsonSerializer.Deserialize<L12PublicDeckGuideView>(reader.GetString(0)) ?? EmptyGuide(),
            JsonSerializer.Deserialize<List<L12PublicDeckMatchupView>>(reader.GetString(1)) ?? [],
            reader.GetInt32(2), DateTimeOffset.Parse(reader.GetString(3)));
    }

    private static L12PublicDeckGuideView EmptyGuide() => new("", "", "", "", "");

    private static IReadOnlyList<L12PublicDeckVersionView> ReadPublicDeckVersions(SqliteConnection connection,
        PublishedDeckRow published)
    {
        var stored = new List<(int Version, string Name, NormalizedDeckPayload Payload, DateTimeOffset Created)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT versions.version,versions.name,versions.payload_hash,payload.master_id,
                       payload.main_cards_json,payload.morale_cards_json,payload.special_cards_json,
                       versions.created_utc
                FROM published_deck_versions versions
                JOIN deck_payloads payload ON payload.payload_hash=versions.payload_hash
                WHERE versions.publication_id=$id ORDER BY versions.version;
                """;
            command.Parameters.AddWithValue("$id", published.Id);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var payload = new NormalizedDeckPayload(reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    reader.GetString(5), reader.GetString(6), ExpandCards(reader.GetString(4)),
                    ExpandCards(reader.GetString(5)), ExpandCards(reader.GetString(6)));
                stored.Add((reader.GetInt32(0), string.IsNullOrWhiteSpace(reader.GetString(1)) ? published.Name : reader.GetString(1),
                    payload, DateTimeOffset.Parse(reader.GetString(7))));
            }
        }
        var views = new List<L12PublicDeckVersionView>(stored.Count);
        for (var index = 0; index < stored.Count; index++)
        {
            var current = stored[index];
            var previous = index == 0 ? null : stored[index - 1].Payload;
            var deck = new L12AccountDeckView(current.Name, current.Payload.MasterId, current.Payload.MainCards,
                current.Payload.MoraleCards, current.Payload.SpecialCards, current.Created);
            views.Add(new(current.Version, current.Name, deck, current.Created,
                previous is null ? [] : DiffPublicDeckPayloads(previous, current.Payload)));
        }
        views.Reverse();
        return views;
    }

    private static IReadOnlyList<L12PublicDeckVersionChangeView> DiffPublicDeckPayloads(
        NormalizedDeckPayload previous, NormalizedDeckPayload current)
    {
        var result = new List<L12PublicDeckVersionChangeView>();
        Add("main", previous.MainCards, current.MainCards);
        Add("morale", previous.MoraleCards, current.MoraleCards);
        Add("special", previous.SpecialCards, current.SpecialCards);
        if (!string.Equals(previous.MasterId, current.MasterId, StringComparison.Ordinal))
            result.Insert(0, new("master", current.MasterId, 0, 1));
        return result;

        void Add(string section, IReadOnlyList<string> before, IReadOnlyList<string> after)
        {
            var left = before.GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var right = after.GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            foreach (var cardId in left.Keys.Concat(right.Keys).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
            {
                var from = left.GetValueOrDefault(cardId);
                var to = right.GetValueOrDefault(cardId);
                if (from != to) result.Add(new(section, cardId, from, to));
            }
        }
    }
}
