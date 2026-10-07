using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckSummaryGenerationTests
{
    [Fact]
    public void RealLocalViewCopyAndLikeWritesChangeGenerationWithoutDataVersion()
    {
        using var fixture = new PublicDeckSummaryFixture();
        var published = fixture.Publish("counters");
        var query = new L12PublicDeckSummaryQuery(Source: "public");
        var before = fixture.Page(query, fixture.Viewer);
        var dataVersion = fixture.Store.Version;
        fixture.Store.RecordPublishedDeckView(published.Id, null);
        Assert.Equal(dataVersion, fixture.Store.Version);
        var viewed = fixture.Page(query, fixture.Viewer);
        Assert.NotEqual(before.Generation, viewed.Generation);
        Assert.Equal(1, Assert.Single(viewed.Items).Views);
        fixture.Store.RecordPublishedDeckCopy(published.Id, null);
        Assert.Equal(dataVersion, fixture.Store.Version);
        var copied = fixture.Page(query, fixture.Viewer);
        Assert.NotEqual(viewed.Generation, copied.Generation);
        Assert.Equal(1, Assert.Single(copied.Items).Copies);
        fixture.Store.TogglePublishedDeckLike(fixture.Owner.Id, published.Id);
        Assert.Equal(dataVersion, fixture.Store.Version);
        var liked = fixture.Page(query, fixture.Viewer);
        Assert.NotEqual(copied.Generation, liked.Generation);
        Assert.True(Assert.Single(liked.Items).ViewerLiked);
        Assert.Equal(1, liked.Items[0].Likes);
        fixture.Observe();
        Assert.Equal(liked.Generation, fixture.Page(query, fixture.Viewer).Generation);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Theory]
    [InlineData("revoke")]
    [InlineData("role")]
    [InlineData("disable")]
    [InlineData("delete")]
    public void CommittedViewerInvalidationNeverReturnsOwnerOperationFlags(string kind)
    {
        using var fixture = new PublicDeckSummaryFixture();
        fixture.Publish("viewer");
        var query = new L12PublicDeckSummaryQuery(Source: "public");
        var owned = Assert.Single(fixture.Page(query, fixture.Viewer).Items);
        Assert.True(owned.CanEdit);
        Assert.NotNull(owned.PublicationVersion);
        var admin = fixture.Store.Login("Admin", "L12master").Account!;
        var context = new L12AdminAuditContext("cq2-viewer", Reason: "CQ2 synthetic", RequestMethod: "POST", RequestPath: "/synthetic");
        switch (kind)
        {
            case "revoke": Assert.Equal(1, fixture.Store.RevokeOwnSession(fixture.Viewer, fixture.Viewer.SessionId).RevokedCount); break;
            case "role": Assert.True(fixture.Store.SetRole(admin, fixture.Owner.Id, "admin")); break;
            case "disable": fixture.Store.SetAccountDisabled(admin, fixture.Owner.Id, true, "CQ2 synthetic", context, true); break;
            case "delete": fixture.Store.DeleteAccountPersonalData(admin, fixture.Owner.Id, "CQ2 synthetic", context, true); break;
        }
        fixture.Observe();
        Assert.Equal("unauthorized", fixture.Store.DeckLibrarySummaries(fixture.Catalog, query, fixture.Viewer).Status);
        var anonymous = fixture.Page(query);
        Assert.All(anonymous.Items, item => { Assert.False(item.CanEdit); Assert.False(item.ViewerLiked); Assert.Null(item.PublicationVersion); });
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public void PublicOwnerNamePolicyAndSameSourcePoolMetadataChangeCapturedGeneration()
    {
        using var fixture = new PublicDeckSummaryFixture();
        fixture.Publish("generation");
        var query = new L12PublicDeckSummaryQuery(Source: "public");
        var before = fixture.Page(query);
        var rename = fixture.Store.SelfServiceChangeUsername(fixture.Owner.Id, "password-123", "cq2renamed", fixture.Viewer.SessionId);
        Assert.True(rename.Success, rename.Message);
        var renamed = fixture.Page(query);
        Assert.NotEqual(before.Generation, renamed.Generation);
        Assert.Equal("cq2renamed", Assert.Single(renamed.Items).Author);
        fixture.Configure(config => config with { CardRestrictions = [new(fixture.Source.CardIds[0], 0)] });
        var policyChanged = fixture.Page(query);
        Assert.NotEqual(renamed.Generation, policyChanged.Generation);
        Assert.NotEqual(renamed.PolicyVersion, policyChanged.PolicyVersion);
        var pools = Assert.IsAssignableFrom<IDictionary<string, string?>>(fixture.Catalog.CardPools);
        pools[fixture.Source.MasterId] = "S03";
        fixture.Observe();
        var metadataChanged = fixture.Page(query);
        Assert.NotEqual(policyChanged.CatalogVersion, metadataChanged.CatalogVersion);
        Assert.NotEqual(policyChanged.Generation, metadataChanged.Generation);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public void IntMaxCompactPublishedFactsAreSummarizedWithoutMaterialization()
    {
        using var fixture = new PublicDeckSummaryFixture();
        var published = fixture.Publish("huge");
        _ = new L12PlatformStore(fixture.PathName, officialCards: fixture.Catalog.Cards);
        var id = fixture.Source.CardIds.First(cardId => !L12SpecialDeckRules.DoesNotCountTowardMainDeck(fixture.Catalog.Cards[cardId]));
        var main = new[] { new { CardId = id, Quantity = int.MaxValue } };
        var empty = main.Take(0).ToArray();
        var canonical = JsonSerializer.Serialize(new { schema = 1, master = fixture.Source.MasterId, main, morale = empty, special = empty });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var payload = JsonSerializer.Serialize(new object[]
        {
            new object[] { new object[] { id, int.MaxValue } }, Array.Empty<object>(), Array.Empty<object>(),
        });
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = fixture.Store.TransactionalStoragePath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO deck_payloads(payload_hash,master_id,payload_format,payload_json,created_utc)
                VALUES($hash,$master,1,$payload,'2026-10-07T00:00:00Z');
                UPDATE published_decks SET current_payload_hash=$hash WHERE publication_id=$id AND is_deleted=0;
                UPDATE published_deck_versions SET payload_hash=$hash WHERE publication_id=$id;
                """;
            command.Parameters.AddWithValue("$hash", hash);
            command.Parameters.AddWithValue("$master", fixture.Source.MasterId);
            command.Parameters.AddWithValue("$payload", payload);
            command.Parameters.AddWithValue("$id", published.Id);
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        var restarted = new L12PlatformStore(fixture.PathName, officialCards: fixture.Catalog.Cards);
        restarted.DeckPayloadExpansionObserver = fixture.Expansions.Add;
        var page = restarted.DeckLibrarySummaries(fixture.Catalog, new(Source: "public")).Page!;
        Assert.Equal(int.MaxValue, Assert.Single(page.Items).Counts.Main);
        Assert.False(page.Items[0].Legal);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }
}
