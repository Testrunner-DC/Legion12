namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    public L12PublicDeckBinding? ResolvePublicDeckBinding(string? accountId, L12PresetDeckDefinition deck,
        string? publicationId, int? version)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(publicationId) || version is not > 0)
            return null;
        lock (_gate)
        {
            using var connection = OpenDatabase(_databasePath, readOnly: true);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT v.payload_hash FROM published_deck_versions v
                JOIN published_decks p ON p.publication_id=v.publication_id
                WHERE v.publication_id=$id AND v.version=$version AND p.owner_id=$owner AND p.is_deleted=0;
                """;
            command.Parameters.AddWithValue("$id", publicationId);
            command.Parameters.AddWithValue("$version", version.Value);
            command.Parameters.AddWithValue("$owner", accountId);
            var hash = command.ExecuteScalar() as string;
            var actual = NormalizeDeckPayload(deck.MasterId, deck.CardIds, deck.MoraleIds, deck.SpecialIds).Hash;
            return hash == actual ? new(publicationId, version.Value, hash) : null;
        }
    }
}
