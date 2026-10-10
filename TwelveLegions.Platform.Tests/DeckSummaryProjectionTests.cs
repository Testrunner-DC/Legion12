using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DeckSummaryProjectionTests
{
    [Fact]
    public void PagingFiltersAndFacetsShareOneOwnedGenerationWithStableOrdering()
    {
        using var fixture = new Fixture();
        var source = fixture.Catalog.PresetDecks[0];
        foreach (var name in new[] { "ALPHA", "alpha 二", "甲", "乙", "丙", "最后" }) fixture.Create(name, source);
        fixture.Store.CreateDeck(fixture.Other.Id, Named(source, "ALPHA other"));
        fixture.Observe();
        var all = fixture.Store.PrivateDeckSummaries(fixture.Owner, fixture.Catalog, new(Sort: "name"))!;
        var first = fixture.Store.PrivateDeckSummaries(fixture.Owner, fixture.Catalog, new(PageSize: 2, Sort: "name"))!;
        var second = fixture.Store.PrivateDeckSummaries(fixture.Owner, fixture.Catalog, new(Page: 2, PageSize: 2, Sort: "name"))!;
        Assert.Equal(6, first.Total);
        Assert.Equal(all.Items.Take(2).Select(item => item.Id), first.Items.Select(item => item.Id));
        Assert.Equal(all.Items.Skip(2).Take(2).Select(item => item.Id), second.Items.Select(item => item.Id));
        Assert.Equal(first.Generation, second.Generation);
        Assert.Equal(first.CatalogVersion, second.CatalogVersion);
        Assert.Equal(first.PolicyVersion, second.PolicyVersion);
        Assert.Equal(first.Total, first.Facets.Masters.Sum(item => item.Count));
        Assert.Equal(first.Total, first.Facets.Legal + first.Facets.Illegal);
        var filtered = fixture.Store.PrivateDeckSummaries(fixture.Owner, fixture.Catalog,
            new(Keyword: "alpha", MasterId: source.MasterId, Legal: true))!;
        Assert.Equal(2, filtered.Total);
        Assert.Equal(2, filtered.Facets.Legal);
        Assert.Equal(2, Assert.Single(filtered.Facets.Masters).Count);
        Assert.Empty(fixture.Store.PrivateDeckSummaries(fixture.Owner, fixture.Catalog, new(Page: int.MaxValue, PageSize: 100))!.Items);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public void SummaryUsesSameSeasonRuleAsFullValidationAndNeverContainsPrivateBodies()
    {
        using var fixture = new Fixture();
        var source = fixture.Catalog.PresetDecks[0];
        fixture.Create("season", source);
        var admin = fixture.Store.Login("Admin", "L12master").Account!;
        var config = fixture.Store.OperationsConfig(admin);
        var banned = source.CardIds[0];
        fixture.Store.ApplyOperationsConfig(admin, config.Config with { CardRestrictions = [new(banned, 0, "CQ1 synthetic")] },
            config.Version, "CQ1 synthetic restriction", new L12AdminAuditContext("cq1-season",
                Permission: L12Authorization.Key(L12Permission.AdminOperationsWrite), Reason: "CQ1 synthetic",
                RequestMethod: "PUT", RequestPath: "/api/admin/operations/config"));
        fixture.Observe();
        var page = fixture.Store.PrivateDeckSummaries(fixture.Owner, fixture.Catalog)!;
        var item = Assert.Single(page.Items);
        Assert.False(item.Legal);
        Assert.False(L12DeckValidator.TryValidatePreset(fixture.Catalog, source, out var error,
            fixture.Store.EffectiveOperationsPolicy().CardRestrictions));
        Assert.Equal(error, item.LegalityReason);
        Assert.Equal(1, page.Facets.Illegal);
        var serialized = JsonSerializer.Serialize(page, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var key in new[] { "cardIds", "moraleIds", "specialIds", "benchIds", "alternateArtCopies", "alternateArtSelections",
            "payloadHash", "code", "environment" }) Assert.DoesNotContain('"' + key + '"', serialized);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public void EqualSortValuesUseStableIdsAndSummaryFailureNeverFallsBackToFullList()
    {
        using var fixture = new Fixture();
        var source = fixture.Catalog.PresetDecks[0];
        fixture.Create("one", source);
        fixture.Create("two", source);
        var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Store)!;
        var rows = ((IEnumerable)data.GetType().GetProperty("Decks")!.GetValue(data)!).Cast<object>().ToArray();
        var same = DateTimeOffset.Parse("2026-10-07T00:00:00Z", CultureInfo.InvariantCulture);
        foreach (var row in rows)
        {
            row.GetType().GetProperty("UpdatedAt")!.SetValue(row, same);
            row.GetType().GetProperty("Name")!.SetValue(row, "same");
        }
        fixture.Observe();
        foreach (var sort in new[] { "latest", "name" })
        {
            var page = fixture.Store.PrivateDeckSummaries(fixture.Owner, fixture.Catalog, new(Sort: sort))!;
            Assert.Equal(page.Items.Select(item => item.Id).Order(StringComparer.Ordinal), page.Items.Select(item => item.Id));
        }
        rows[0].GetType().GetProperty("PayloadHash")!.SetValue(rows[0], "missing-reference");
        Assert.Throws<InvalidDataException>(() => fixture.Store.PrivateDeckSummaries(fixture.Owner, fixture.Catalog));
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public void ReaderPermissionGenerationIsRecheckedBeforeReadingOrExpanding()
    {
        using var fixture = new Fixture();
        var created = fixture.Create("permissions", fixture.Catalog.PresetDecks[0]);
        fixture.Observe();
        var stale = fixture.Owner with { PermissionVersion = fixture.Owner.PermissionVersion + 1 };
        Assert.Null(fixture.Store.PrivateDeckSummaries(stale, fixture.Catalog));
        Assert.Equal("unauthorized", fixture.Store.ReadPrivateDeck(stale, created.Id).Status);
        Assert.Equal("not_found", fixture.Store.ReadPrivateDeck(fixture.Other, created.Id).Status);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public void AnotherOwnersBrokenReferenceIsNeverReadBySummaryOrDetail()
    {
        using var fixture = new Fixture();
        var source = fixture.Catalog.PresetDecks[0];
        fixture.Create("owned", source);
        var foreign = fixture.Store.CreateDeck(fixture.Other.Id, Named(source, "foreign")).Deck!;
        var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Store)!;
        var rows = ((IEnumerable)data.GetType().GetProperty("Decks")!.GetValue(data)!).Cast<object>();
        var row = rows.Single(item => (string)item.GetType().GetProperty("Id")!.GetValue(item)! == foreign.Id);
        row.GetType().GetProperty("PayloadHash")!.SetValue(row, "foreign-broken-reference");
        fixture.Observe();
        Assert.Equal("owned", Assert.Single(fixture.Store.PrivateDeckSummaries(fixture.Owner, fixture.Catalog)!.Items).Name);
        Assert.Equal("not_found", fixture.Store.ReadPrivateDeck(fixture.Owner, foreign.Id).Status);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    [Fact]
    public void HugeCompactFactsCanBeSummarizedButStaleReadsNeverExpandThem()
    {
        using var fixture = new Fixture();
        var source = fixture.Catalog.PresetDecks[0];
        var created = fixture.Create("huge", source);
        // Prepare the existing one-time identity backup before injecting facts
        // that intentionally exceed the explicit full-export budget.
        _ = new L12PlatformStore(fixture.Path, officialCards: fixture.Catalog.Cards);
        var main = new[] { new { CardId = source.CardIds[0].ToUpperInvariant(), Quantity = int.MaxValue } };
        var empty = main.Take(0).ToArray();
        var canonical = JsonSerializer.Serialize(new { schema = 1, master = source.MasterId, main, morale = empty, special = empty });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var payload = JsonSerializer.Serialize(new object[]
        {
            new object[] { new object[] { main[0].CardId, int.MaxValue } }, Array.Empty<object>(), Array.Empty<object>(),
        });
        using (var connection = new SqliteConnection(fixture.Store.TransactionalStoragePath is { } path
            ? new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString() : throw new InvalidOperationException()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO deck_payloads(payload_hash,master_id,payload_format,payload_json,created_utc)
                VALUES($hash,$master,1,$payload,'2026-10-07T00:00:00Z');
                UPDATE account_decks SET payload_hash=$hash WHERE deck_id=$id AND is_deleted=0;
                """;
            command.Parameters.AddWithValue("$hash", hash);
            command.Parameters.AddWithValue("$master", source.MasterId);
            command.Parameters.AddWithValue("$payload", payload);
            command.Parameters.AddWithValue("$id", created.Id);
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        var restarted = new L12PlatformStore(fixture.Path, officialCards: fixture.Catalog.Cards);
        restarted.DeckPayloadExpansionObserver = fixture.Expansions.Add;
        var page = restarted.PrivateDeckSummaries(fixture.Owner, fixture.Catalog)!;
        Assert.Equal(int.MaxValue, Assert.Single(page.Items).Counts.Main);
        Assert.False(page.Items[0].Legal);
        Assert.Equal("revision_conflict", restarted.ReadPrivateDeck(fixture.Owner, created.Id, created.Revision + 1).Status);
        Assert.Equal("not_found", restarted.ReadPrivateDeck(fixture.Other, created.Id).Status);
        Assert.Empty(fixture.Expansions);
        fixture.Complete();
    }

    private static L12PresetDeckDefinition Named(L12PresetDeckDefinition source, string name) => new()
    {
        Name = name, MasterId = source.MasterId, CardIds = [.. source.CardIds], MoraleIds = [.. source.MoraleIds],
        SpecialIds = [.. source.SpecialIds], BenchIds = [.. source.BenchIds],
    };

    private sealed class Fixture : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "l12-cq1-summary-" + Guid.NewGuid().ToString("N"));
        private bool passed;
        public L12Catalog Catalog { get; } = L12Catalog.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        public string Path => System.IO.Path.Combine(root, "platform.json");
        public L12PlatformStore Store { get; }
        public L12AccountView Owner { get; }
        public L12AccountView Other { get; }
        public List<string> Expansions { get; } = [];
        public Fixture()
        {
            Directory.CreateDirectory(root);
            Store = new(Path, officialCards: Catalog.Cards);
            var owner = Store.Register("cq1sumown", "password-123");
            var other = Store.Register("cq1sumoth", "password-123");
            Assert.True(owner.Success, owner.Message);
            Assert.True(other.Success, other.Message);
            Owner = owner.Account!;
            Other = other.Account!;
        }
        public L12AccountDeckView Create(string name, L12PresetDeckDefinition source)
        {
            var result = Store.CreateDeck(Owner.Id, Named(source, name));
            Assert.True(result.Success);
            return result.Deck!;
        }
        public void Observe() => Store.DeckPayloadExpansionObserver = Expansions.Add;
        public void Complete() => passed = true;
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (passed) Directory.Delete(root, true);
            else Console.WriteLine("Failed synthetic CQ1 summary fixture retained: " + root);
        }
    }
}
