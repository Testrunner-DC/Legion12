using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed record L12PublicDeckCurrentReadView(L12PublicDeckSummaryView Summary, int Version,
    L12AccountDeckView Deck, L12PublicDeckGuideView Guide, IReadOnlyList<L12PublicDeckMatchupView> Matchups,
    int ContentRevision, DateTimeOffset? ContentUpdatedAt, string ReadToken, string CatalogVersion, long PolicyVersion);
public sealed record L12PublicDeckCurrentReadResult(string Status, L12PublicDeckCurrentReadView? Detail = null);

public sealed partial class L12PlatformStore
{
    private sealed record PublicDeckReadHead(string Id, string Name, int Version, string Hash,
        int ContentRevision, string? ContentHash, DateTimeOffset? ContentUpdatedAt, string Token,
        string CatalogVersion, L12OperationsPolicySnapshot Policy);

    private bool IsCurrentPublicDeckReader(L12AuthenticatedSession? viewer)
        => viewer is null || IsCurrentDeckReader(viewer.Account) && _data.Sessions.Any(session =>
            session.Id == viewer.SessionId && session.AccountId == viewer.Account.Id && session.RevokedAt is null
            && session.ExpiresAt > DateTimeOffset.UtcNow && session.PermissionVersion == viewer.Account.PermissionVersion);

    private static bool IsPublicDeckReadReference(string reference)
        => !string.IsNullOrWhiteSpace(reference) && reference.Length <= 64 && !reference.Any(char.IsControl);

    // The gate pins cache, permissions and policy. The first SQL read pins a single
    // WAL snapshot for the head, content and selected immutable version facts.
    private PublicDeckReadHead CapturePublicDeckReadHead(SqliteConnection connection, SqliteTransaction transaction,
        PublishedDeckRow row, L12Catalog catalog, L12OperationsPolicySnapshot policy, L12AuthenticatedSession? viewer,
        string? frozenCatalogVersion = null)
    {
        using (var state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText = """
                SELECT schema_version,storage_revision,(SELECT value FROM storage_meta WHERE key='schema_version')
                FROM platform_state WHERE singleton_id=1;
                """;
            using var stored = state.ExecuteReader();
            if (!stored.Read() || stored.GetInt32(0) != PlatformStorageSchemaVersion || stored.GetInt64(1) != _data.Version
                || stored.IsDBNull(2) || stored.GetString(2) != PlatformStorageSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidDataException("公开牌库读取存储格式或已提交代际不一致");
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT deck.name,deck.current_version,deck.current_payload_hash,deck.owner_id,deck.public_code,
                   deck.updated_utc,content.revision,content.content_hash,content.updated_utc,
                   EXISTS(SELECT 1 FROM published_deck_content_revisions WHERE publication_id=deck.publication_id)
            FROM published_decks deck
            LEFT JOIN published_deck_content_heads content ON content.publication_id=deck.publication_id
            WHERE deck.publication_id=$id AND deck.is_deleted=0;
            """;
        command.Parameters.AddWithValue("$id", row.Id);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || row.Version < 1 || reader.GetString(0) != row.Name || reader.GetInt32(1) != row.Version
            || reader.GetString(2) != row.PayloadHash || reader.GetString(3) != row.OwnerId
            || reader.GetString(4) != row.PublicCode || DateTimeOffset.Parse(reader.GetString(5)) != row.UpdatedAt)
            throw new InvalidDataException("公开牌库头与已提交代际不一致");
        var revision = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);
        var hash = reader.IsDBNull(7) ? null : reader.GetString(7);
        var updated = reader.IsDBNull(8) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(8));
        if (revision < 0 || revision == 0 && (hash is not null || updated is not null || reader.GetBoolean(9))
            || revision > 0 && (hash is null || updated is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit)))
            throw new InvalidDataException("公开牌库指南头无效");
        var catalogVersion = frozenCatalogVersion ?? LibraryCatalogVersion(catalog);
        var account = _data.Accounts.FirstOrDefault(item => item.Id == row.OwnerId);
        // Counters are intentionally excluded: opening a deck must not invalidate
        // its own pin. Viewer identity is consistency context, never authorization.
        var token = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            row.Id, row.PublicCode, row.Name, HeadVersion = row.Version, row.PayloadHash, row.OwnerId, row.UpdatedAt,
            Author = account is null ? "已注销玩家" : PublicUsername(account),
            ContentRevision = revision, ContentHash = hash, ContentUpdatedAt = updated,
            catalogVersion, PolicyVersion = policy.Version, policy.VersionId,
            Viewer = viewer?.Account.Id, Permission = viewer?.Account.PermissionVersion, Session = viewer?.SessionId,
        }))).ToLowerInvariant();
        return new(row.Id, row.Name, row.Version, row.PayloadHash, revision, hash, updated, token, catalogVersion, policy);
    }

    private StoredPublicDeckContent ReadPinnedPublicDeckContent(SqliteConnection connection,
        SqliteTransaction transaction, PublicDeckReadHead head)
    {
        if (head.ContentRevision == 0) return new(EmptyGuide(), [], 0, null);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT length(guide_json),length(matchups_json),guide_json,matchups_json FROM published_deck_content_payloads WHERE content_hash=$hash;";
        command.Parameters.AddWithValue("$hash", head.ContentHash!);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidDataException("公开牌库指南正文引用缺失");
        if (reader.GetInt64(0) > 64_000 || reader.GetInt64(1) > 1_000_000)
            throw new InvalidDataException("公开牌库指南正文资源预算超限");
        var guideJson = reader.GetString(2);
        var matchupsJson = reader.GetString(3);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{{\"guide\":{guideJson},\"matchups\":{matchupsJson}}}"))).ToLowerInvariant();
        if (hash != head.ContentHash) throw new InvalidDataException("公开牌库指南正文哈希不匹配");
        var guide = JsonSerializer.Deserialize<L12PublicDeckGuideView>(guideJson)
            ?? throw new InvalidDataException("公开牌库指南正文为空");
        var matchups = JsonSerializer.Deserialize<List<L12PublicDeckMatchupView>>(matchupsJson)
            ?? throw new InvalidDataException("公开牌库对局建议正文为空");
        if (matchups.Any(item => item is null)) throw new InvalidDataException("公开牌库对局建议包含空项");
        if (JsonSerializer.Serialize(NormalizeGuide(guide)) != guideJson
            || JsonSerializer.Serialize(NormalizeMatchups(matchups)) != matchupsJson)
            throw new InvalidDataException("公开牌库指南正文不是规范有界内容");
        return new(guide, matchups, head.ContentRevision, head.ContentUpdatedAt);
    }

    public L12PublicDeckCurrentReadResult ReadPublicDeckCurrent(L12Catalog catalog, string reference,
        string? expectedReadToken = null, L12AuthenticatedSession? viewer = null)
    {
        if (!IsPublicDeckReadReference(reference) || !L12PublicDeckReadQuery.IsValidToken(expectedReadToken))
            return new("invalid_request");
        lock (_gate)
        {
            if (!IsCurrentPublicDeckReader(viewer)) return new("unauthorized");
            var policy = CaptureOperationsPolicy();
            if (!policy.IsFeatureEnabled("publicDecks")) return new("feature_disabled");
            var row = FindPublishedDeck(reference);
            if (row is null) return new("not_found");
            using var connection = OpenDatabase(_databasePath, readOnly: true);
            using var transaction = connection.BeginTransaction(deferred: true);
            var head = CapturePublicDeckReadHead(connection, transaction, row, catalog, policy, viewer);
            if (expectedReadToken is not null && expectedReadToken != head.Token) return new("read_conflict");
            var selected = ReadPinnedPublicDeckVersion(connection, transaction, head, head.Version)
                ?? throw new InvalidDataException("公开牌库当前不可变版本缺失");
            if (selected.Payload.Hash != head.Hash || selected.Name != head.Name)
                throw new InvalidDataException("公开牌库当前版本与头引用不一致");
            var runtime = ReadReferencedDeckPayload(_data, row.PayloadHash);
            if (runtime.MasterId != selected.Payload.MasterId || runtime.MainJson != selected.Payload.MainJson
                || runtime.MoraleJson != selected.Payload.MoraleJson || runtime.SpecialJson != selected.Payload.SpecialJson)
                throw new InvalidDataException("公开牌库正文与已提交代际不一致");
            var content = ReadPinnedPublicDeckContent(connection, transaction, head);
            var summary = PublishedLibraryEntry(_data, row, catalog, policy, viewer?.Account.Id).View;
            PreflightOnlineDeckProjection([selected.Payload]);
            transaction.Commit();
            var payload = ExpandRuntimeDeckPayload(selected.Payload);
            var owner = viewer?.Account.Id == row.OwnerId;
            var deck = new L12AccountDeckView(row.Name, payload.MasterId, payload.MainCards, payload.MoraleCards,
                payload.SpecialCards, row.UpdatedAt, PublicationId: owner ? row.Id : null,
                PublicationVersion: owner ? row.Version : null);
            return new("ok", new(summary, head.Version, deck, content.Guide, content.Matchups, content.Revision,
                content.UpdatedAt, head.Token, head.CatalogVersion, policy.Version));
        }
    }
}
