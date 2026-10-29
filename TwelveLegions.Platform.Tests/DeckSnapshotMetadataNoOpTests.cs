using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DeckSnapshotMetadataNoOpTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

    [Fact]
    public void UnchangedMixedSnapshotDoesNotRewriteReferenceRows()
    {
        using var f = new Fixture();
        var before = f.Row("account_decks", "name_key='ONE'");
        var measured = f.Persist();
        Assert.Equal(0, measured.InstrumentationErrors);
        Assert.InRange(measured.DeckDomainStatements, 2, 32);
        Assert.Equal(0, measured.UpdateRows);
        Assert.Equal(0, measured.InsertRows);
        Assert.Equal(0, measured.DeleteRows);
        Assert.Equal(before, f.Row("account_decks", "name_key='ONE'"));
    }

    [Theory]
    [InlineData("name", "changed")]
    [InlineData("deck_id", "changed")]
    [InlineData("revision", 99L)]
    [InlineData("payload_hash", "other-payload")]
    [InlineData("alternate_art_selections_json", "{\"C1\":\"art\"}")]
    [InlineData("alternate_art_copies_json", "{\"C1\":[\"art\"]}")]
    [InlineData("bench_cards_json", "[{\"CardId\":\"C1\",\"Quantity\":1}]")]
    [InlineData("representative_card_ids_json", "[\"C1\"]")]
    [InlineData("publication_id", "linked")]
    [InlineData("publication_version", 3L)]
    [InlineData("updated_utc", "2026-01-01T08:00:00.0000000+08:00")]
    [InlineData("is_deleted", 1L)]
    public void EveryPrivatePersistedFieldStillRepairsAChangedRow(string column, object value)
        => AssertRepair("account_decks", "name_key='ONE'", column, value);

    [Theory]
    [InlineData("public_code", "changed-code")]
    [InlineData("owner_id", "changed-owner")]
    [InlineData("alternate_art_selections_json", "{\"C1\":\"art\"}")]
    [InlineData("alternate_art_copies_json", "{\"C1\":[\"art\"]}")]
    [InlineData("views", 13L)]
    [InlineData("copies", 13L)]
    [InlineData("updated_utc", "2026-01-01T08:00:00.0000000+08:00")]
    [InlineData("is_deleted", 1L)]
    public void PublicHeadMetadataAndTombstoneAreNotSkipped(string column, object value)
        => AssertRepair("published_decks", "publication_id='publication'", column, value);

    [Theory]
    [InlineData("payload_hash", "other-payload")]
    [InlineData("submitted_utc", "2026-01-01T08:00:00.0000000+08:00")]
    [InlineData("locked_utc", "2026-01-01T00:00:00.0000000+00:00")]
    public void EveryTournamentReferenceFieldIsComparedIncludingNull(string column, object value)
        => AssertRepair("tournament_deck_refs", "tournament_id='tournament'", column, value);

    [Fact]
    public void MatchingPublicHeadStillRecreatesMissingHistory()
    {
        using var f = new Fixture();
        var before = f.Row("published_decks", "publication_id='publication'");
        f.Execute("DELETE FROM published_deck_versions;");
        var measured = f.Persist();
        Assert.Equal(1, measured.InsertRows);
        Assert.Equal(before, f.Row("published_decks", "publication_id='publication'"));
        Assert.NotEmpty(f.Row("published_deck_versions", "publication_id='publication'"));
    }

    [Fact]
    public void RetainedTombstonesAreNotRepeatedlyUpdatedOrDeleted()
    {
        using var f = new Fixture();
        ((IList)f.Data.GetType().GetProperty("Decks")!.GetValue(f.Data)!).Clear();
        var first = f.Persist();
        Assert.Equal(2, first.UpdateRows);
        var retained = f.Row("account_decks", "name_key='ONE'");
        var second = f.Persist();
        Assert.Equal(0, second.UpdateRows);
        Assert.Equal(0, second.DeleteRows);
        Assert.Equal(retained, f.Row("account_decks", "name_key='ONE'"));
        Assert.Equal(2L, f.Scalar("SELECT COUNT(*) FROM account_decks WHERE is_deleted=1;"));
    }

    [Fact]
    public void RetainedPublicTombstoneKeepsHistoryAndLikesWithoutRepeatedWrites()
    {
        using var f = new Fixture();
        var history = f.Row("published_deck_versions", "publication_id='publication'");
        var like = f.Row("published_deck_likes", "publication_id='publication'");
        ((IList)f.Data.GetType().GetProperty("PublishedDecks")!.GetValue(f.Data)!).Clear();
        Assert.Equal(1, f.Persist().UpdateRows);
        var retained = f.Row("published_decks", "publication_id='publication'");
        var second = f.Persist();
        Assert.Equal(0, second.UpdateRows);
        Assert.Equal(0, second.DeleteRows);
        Assert.Equal(retained, f.Row("published_decks", "publication_id='publication'"));
        Assert.Equal(history, f.Row("published_deck_versions", "publication_id='publication'"));
        Assert.Equal(like, f.Row("published_deck_likes", "publication_id='publication'"));
        Assert.Equal(1L, f.Scalar("SELECT COUNT(*) FROM published_decks WHERE is_deleted=1;"));
    }

    [Fact]
    public void RepeatedTournamentKeyObservesEarlierWritesWithinTheSameSnapshot()
    {
        using var f = new Fixture();
        var expected = f.Row("tournament_deck_refs", "tournament_id='tournament'");
        var tournaments = (IList)f.Data.GetType().GetProperty("Tournaments")!.GetValue(f.Data)!;
        var rows = (IList)tournaments[0]!.GetType().GetProperty("Participants")!.GetValue(tournaments[0])!;
        var original = rows[0]!;
        var json = JsonSerializer.Serialize(original, original.GetType());
        var changed = JsonSerializer.Deserialize(json, original.GetType())!;
        var deck = changed.GetType().GetProperty("Deck")!.GetValue(changed)!;
        deck.GetType().GetProperty("LockedAt")!.SetValue(deck, DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
        rows.Add(changed);
        rows.Add(JsonSerializer.Deserialize(json, original.GetType())!);
        var measured = f.Persist();
        // The existing measurement groups UPSERT changes by its INSERT verb.
        Assert.Equal(2, measured.InsertRows);
        Assert.Equal(0, measured.UpdateRows);
        Assert.Equal(expected, f.Row("tournament_deck_refs", "tournament_id='tournament'"));
    }

    [Fact]
    public void ChangedHeadKeepsFirstHistoryLikeAndPublicationCreationTimes()
    {
        using var f = new Fixture();
        var history = f.Row("published_deck_versions", "publication_id='publication'");
        var like = f.Row("published_deck_likes", "publication_id='publication'");
        var created = f.Scalar("SELECT created_utc FROM published_decks WHERE publication_id='publication';");
        var rows = (IList)f.Data.GetType().GetProperty("PublishedDecks")!.GetValue(f.Data)!;
        var row = rows[0]!;
        row.GetType().GetProperty("CreatedAt")!.SetValue(row, DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
        row.GetType().GetProperty("UpdatedAt")!.SetValue(row, DateTimeOffset.Parse("2026-02-02T00:00:00Z"));
        f.Persist();
        Assert.Equal(created, f.Scalar("SELECT created_utc FROM published_decks WHERE publication_id='publication';"));
        Assert.Equal(history, f.Row("published_deck_versions", "publication_id='publication'"));
        Assert.Equal(like, f.Row("published_deck_likes", "publication_id='publication'"));
    }

    private static void AssertRepair(string table, string where, string column, object value)
    {
        using var f = new Fixture();
        var expected = f.Row(table, where);
        if (Equals(value, "other-payload"))
            value = f.Scalar("SELECT payload_hash FROM account_decks WHERE name_key='TWO';");
        using (var db = f.Open())
        using (var command = db.CreateCommand())
        {
            command.CommandText = $"UPDATE {table} SET {column}=$value WHERE {where};";
            command.Parameters.AddWithValue("$value", value);
            Assert.Equal(1, command.ExecuteNonQuery());
        }
        Assert.NotEqual(expected, f.Row(table, where));
        f.Persist();
        Assert.Equal(expected, f.Row(table, where));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "l12-deck-metadata-" + Guid.NewGuid().ToString("N"));
        internal L12PlatformStore Store { get; }
        internal object Data => typeof(L12PlatformStore).GetProperty("_data", Instance)!.GetValue(Store)!;
        internal Fixture()
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "platform.json");
            File.WriteAllText(path, """
                {"Decks":[
                 {"AccountId":"owner","Name":"one","MasterId":"M1","CardIds":["C1"],"UpdatedAt":"2026-01-01T00:00:00Z"},
                 {"AccountId":"owner","Name":"two","MasterId":"M1","CardIds":["C2"],"UpdatedAt":"2026-01-01T00:00:00Z"}],
                 "PublishedDecks":[{"Id":"publication","PublicCode":"PUB","OwnerId":"owner","Name":"public","MasterId":"M1","CardIds":["C1"],"LikedByAccountIds":["liker"],"CreatedAt":"2025-01-01T00:00:00Z","UpdatedAt":"2026-01-01T00:00:00Z"}],
                 "Tournaments":[{"Id":"tournament","Code":"SYNTHETIC","Participants":[{"AccountId":"owner","Deck":{"MasterId":"M1","CardIds":["C1"],"SubmittedAt":"2026-01-01T00:00:00Z"}}]}]}
                """);
            Store = new L12PlatformStore(path);
        }
        internal SqliteConnection Open()
        {
            var db = new SqliteConnection($"Data Source={Store.TransactionalStoragePath};Pooling=False");
            db.Open(); return db;
        }
        internal L12SyntheticStorageMeasurement Persist()
        {
            using var db = Open();
            using var scope = L12PlatformStore.BeginSyntheticStorageMeasurement();
            typeof(L12PlatformStore).GetMethod("AttachSyntheticStorageMeasurement", Static)!.Invoke(null, [db]);
            using var transaction = db.BeginTransaction();
            typeof(L12PlatformStore).GetMethod("PersistDeckDomainSnapshot", Static)!.Invoke(null, [db, transaction, Data]);
            transaction.Commit();
            return scope.Complete();
        }
        internal object Scalar(string sql)
        {
            using var db = Open(); using var command = db.CreateCommand();
            command.CommandText = sql; return command.ExecuteScalar()!;
        }
        internal void Execute(string sql)
        {
            using var db = Open(); using var command = db.CreateCommand();
            command.CommandText = sql; command.ExecuteNonQuery();
        }
        internal string Row(string table, string where)
        {
            using var db = Open(); using var command = db.CreateCommand();
            command.CommandText = $"SELECT * FROM {table} WHERE {where};";
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return "";
            return JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount)
                .Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)).ToArray());
        }
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
}
