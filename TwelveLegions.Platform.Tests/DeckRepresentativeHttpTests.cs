using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DeckRepresentativeHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAndReopenRetainOrderedManualRepresentatives(bool objectPersistence)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        if (objectPersistence) f.Store.ApplyPrivateDeckPersistenceStartup(true);
        var actor = f.Store.Accounts().Single(item => item.Username == "wbplayer");
        var sample = f.Store.Decks(actor.Id).First();
        var chosen = sample.CardIds.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        Assert.Equal(3, chosen.Length);
        var submission = JsonSerializer.SerializeToNode(new { name="代表测试", sample.MasterId, sample.CardIds, sample.MoraleIds, sample.SpecialIds }, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        submission["representativeCardIds"] = JsonSerializer.SerializeToNode(chosen);
        using var client = new HttpClient { BaseAddress = f.Client.BaseAddress };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", f.PlayerToken);
        client.DefaultRequestHeaders.Add("X-L12-Deck-Cover", "2");
        using var response = await client.PostAsJsonAsync("/api/decks", submission);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(chosen, json.RootElement.GetProperty("representativeCardIds").EnumerateArray().Select(x => x.GetString()).ToArray());
        var id = json.RootElement.GetProperty("id").GetString();
        using var reopened = await client.GetAsync($"/api/decks/by-id/{id}");
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        using var json2 = JsonDocument.Parse(await reopened.Content.ReadAsStringAsync());
        Assert.Equal(chosen, json2.RootElement.GetProperty("representativeCardIds").EnumerateArray().Select(x => x.GetString()).ToArray());
        var reloaded = new L12PlatformStore(f.MirrorPath);
        Assert.Equal(chosen, reloaded.Decks(actor.Id).Single(d => d.Id == id).RepresentativeCardIds);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyUpdatesClearAndPublishedVersionsKeepTheirOwnSelection(bool objectPersistence)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        if (objectPersistence) f.Store.ApplyPrivateDeckPersistenceStartup(true);
        var actor = f.Store.Accounts().Single(item => item.Username == "wbplayer");
        var sample = f.Store.Decks(actor.Id).First();
        var chosen = sample.CardIds.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        Assert.Equal(3, chosen.Length);
        JsonObject Make(string name) => JsonSerializer.SerializeToNode(new { name, sample.MasterId, sample.CardIds, sample.MoraleIds, sample.SpecialIds }, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        using var client = new HttpClient { BaseAddress = f.Client.BaseAddress };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", f.PlayerToken);
        client.DefaultRequestHeaders.Add("X-L12-Deck-Cover", "2");
        var body = Make("选择历史"); body["representativeCardIds"] = JsonSerializer.SerializeToNode(chosen);
        using var create = await client.PostAsJsonAsync("/api/decks", body);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;
        var revision = created.RootElement.GetProperty("revision").GetInt64();
        var legacy = Make("选择历史");
        using var update = await client.PutAsJsonAsync($"/api/decks/by-id/{id}", new { deck=legacy, expectedRevision=revision });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var updated = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
        Assert.Equal(chosen, updated.RootElement.GetProperty("representativeCardIds").EnumerateArray().Select(x=>x.GetString()).ToArray());
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/decks/summaries?exactName=%E9%80%89%E6%8B%A9%E5%8E%86%E5%8F%B2&pageSize=1"))
        {
            request.Headers.Add("X-L12-Deck-Cover", "1");
            using var list = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            var cover = listJson.RootElement.GetProperty("items")[0].GetProperty("coverCardIds");
            Assert.Equal(chosen, cover.EnumerateArray().Select(x=>x.GetString()).ToArray());
        }
        revision = updated.RootElement.GetProperty("revision").GetInt64();
        using (var stale = await client.PutAsJsonAsync($"/api/decks/by-id/{id}", new { deck=legacy, expectedRevision=revision-1 }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);        var bad = Make("选择历史"); bad["representativeCardIds"] = JsonSerializer.SerializeToNode(new[] {chosen[0], chosen[0]});
        using var rejected = await client.PutAsJsonAsync($"/api/decks/by-id/{id}", new { deck=bad, expectedRevision=revision });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        bad["representativeCardIds"] = JsonSerializer.SerializeToNode(new[] {"NOT-IN-MAIN"});
        using var outsider = await client.PutAsJsonAsync($"/api/decks/by-id/{id}", new { deck=bad, expectedRevision=revision });
        Assert.Equal(HttpStatusCode.BadRequest, outsider.StatusCode);
        bad["representativeCardIds"] = JsonSerializer.SerializeToNode(new[] {chosen[0],chosen[1],chosen[2],"NOT-IN-MAIN"});
        using var tooMany = await client.PutAsJsonAsync($"/api/decks/by-id/{id}", new { deck=bad, expectedRevision=revision });
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        if (objectPersistence)
        {
            var hit = false;
            f.Store.StorageFailureInjector = stage =>
            {
                if (stage != "before-private-deck-row") return;
                hit = true; f.Store.StorageFailureInjector = null;
                throw new IOException("synthetic representative save failure");
            };
            var failing = Make("选择历史"); failing["representativeCardIds"] = JsonSerializer.SerializeToNode(new[] {chosen[2]});
            using var failed = await client.PutAsJsonAsync($"/api/decks/by-id/{id}", new { deck=failing, expectedRevision=revision });
            f.Store.StorageFailureInjector = null;
            Assert.True(hit); Assert.False(failed.IsSuccessStatusCode);
            Assert.Equal(chosen, f.Store.Decks(actor.Id).Single(d=>d.Id==id).RepresentativeCardIds);
        }
        var clear = Make("选择历史"); clear["representativeCardIds"] = new JsonArray();
        using var cleared = await client.PutAsJsonAsync($"/api/decks/by-id/{id}", new { deck=clear, expectedRevision=revision });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        using var clearedJson = JsonDocument.Parse(await cleared.Content.ReadAsStringAsync());
        Assert.False(clearedJson.RootElement.TryGetProperty("representativeCardIds",out _));
        var pub1 = Make("公开代表"); pub1["representativeCardIds"] = JsonSerializer.SerializeToNode(new[] {chosen[0]});
        using var first = await client.PostAsJsonAsync("/api/public-decks", new { deck=pub1 });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var publicationId = firstJson.RootElement.GetProperty("id").GetString()!;
        var code = firstJson.RootElement.GetProperty("publicCode").GetString()!;
        var version1 = firstJson.RootElement.GetProperty("deck").GetProperty("publicationVersion").GetInt32();
        var pub2 = Make("公开代表"); pub2["representativeCardIds"] = JsonSerializer.SerializeToNode(new[] {chosen[1],chosen[0]});
        using var second = await client.PostAsJsonAsync("/api/public-decks", new { publicationId, deck=pub2 });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var version2 = secondJson.RootElement.GetProperty("deck").GetProperty("publicationVersion").GetInt32();
        Assert.Equal(version1+1, version2);
        using (var db = new SqliteConnection($"Data Source={f.Store.TransactionalStoragePath}"))
        {
            db.Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(DISTINCT payload_hash) FROM published_deck_versions WHERE publication_id=$id;";
            cmd.Parameters.AddWithValue("$id",publicationId);
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
        }
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/deck-library/summaries?source=public&pageSize=100"))
        {
            request.Headers.Add("X-L12-Deck-Cover", "1");
            using var list = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            var item = listJson.RootElement.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetString()==publicationId);
            Assert.Equal(new[] {chosen[1],chosen[0]}, item.GetProperty("coverCardIds").EnumerateArray().Select(x=>x.GetString()).ToArray());
        }
        using (var currentHead = await client.GetAsync($"/api/public-decks/{code}/current"))
        {
            Assert.Equal(HttpStatusCode.OK, currentHead.StatusCode);
            using var headJson = JsonDocument.Parse(await currentHead.Content.ReadAsStringAsync());
            Assert.Equal(new[] {chosen[1],chosen[0]}, headJson.RootElement.GetProperty("deck").GetProperty("representativeCardIds").EnumerateArray().Select(x=>x.GetString()).ToArray());
        }
        using var old = await client.GetAsync($"/api/public-decks/{code}/versions/{version1}");
        Assert.Equal(HttpStatusCode.OK, old.StatusCode);
        using var oldJson = JsonDocument.Parse(await old.Content.ReadAsStringAsync());
        Assert.Equal(new[] {chosen[0]}, oldJson.RootElement.GetProperty("deck").GetProperty("representativeCardIds").EnumerateArray().Select(x=>x.GetString()).ToArray());
        using var current = await client.GetAsync($"/api/public-decks/{code}/versions/{version2}");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        using var currentJson = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
        Assert.Equal(new[] {chosen[1],chosen[0]}, currentJson.RootElement.GetProperty("deck").GetProperty("representativeCardIds").EnumerateArray().Select(x=>x.GetString()).ToArray());
        var reopened = new L12PlatformStore(f.MirrorPath);
        var saved = reopened.Decks(actor.Id).Single(d=>d.Id==id);
        Assert.Null(saved.RepresentativeCardIds);
        Assert.NotNull(reopened.ResolvePublicDeckBinding(actor.Id, new L12PresetDeckDefinition {Name=sample.Name,MasterId=sample.MasterId,CardIds=sample.CardIds.ToList(),MoraleIds=sample.MoraleIds.ToList(),SpecialIds=sample.SpecialIds.ToList()}, publicationId, version1));
    }
    [Fact]
    public async Task SchemaElevenUpgradeRollsBackAllRepresentativeColumnsOnFailure()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        using var db = new SqliteConnection($"Data Source={f.Store.TransactionalStoragePath}"); db.Open();
        void Execute(string sql) { using var command=db.CreateCommand(); command.CommandText=sql; command.ExecuteNonQuery(); }
        long Scalar(string sql) { using var command=db.CreateCommand(); command.CommandText=sql; return Convert.ToInt64(command.ExecuteScalar()); }
        foreach (var table in new[] {"account_decks","published_decks","published_deck_versions"})
            Execute($"ALTER TABLE {table} DROP COLUMN representative_card_ids_json;");
        Execute("UPDATE platform_state SET schema_version=11; UPDATE storage_meta SET value='11' WHERE key='schema_version';");
        Execute("CREATE TRIGGER fail_representative_schema BEFORE UPDATE OF schema_version ON platform_state WHEN NEW.schema_version=12 BEGIN SELECT RAISE(ABORT,'synthetic-representative-migration-failure'); END;");
        Assert.Throws<L12PlatformStorageUnavailableException>(() => new L12PlatformStore(f.MirrorPath));
        Assert.Equal(11, Scalar("SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
        foreach (var table in new[] {"account_decks","published_decks","published_deck_versions"})
            Assert.Equal(0, Scalar($"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='representative_card_ids_json';"));
        Execute("DROP TRIGGER fail_representative_schema;");
        var restored = new L12PlatformStore(f.MirrorPath);
        Assert.Equal(PlatformSchemaTestContract.Current, restored.StorageStatus().SchemaVersion);
        foreach (var table in new[] {"account_decks","published_decks","published_deck_versions"})
            Assert.Equal(1, Scalar($"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='representative_card_ids_json';"));
    }
    [Fact]
    public async Task LegacyStoreUpdateDropsOnlyRepresentativesNoLongerInMainDeck()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var actor=f.Store.Accounts().Single(a=>a.Username=="wbplayer");
        var sample=f.Store.Decks(actor.Id).First();
        var selected=sample.CardIds.Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        Assert.Equal(2,selected.Length);
        var created=f.Store.CreateDeck(actor.Id,new L12PresetDeckDefinition { Name="旧版裁剪",MasterId=sample.MasterId,
            CardIds=sample.CardIds.ToList(),MoraleIds=sample.MoraleIds.ToList(),SpecialIds=sample.SpecialIds.ToList(),
            RepresentativeCardIds=selected.ToList() });
        Assert.True(created.Success);
        var changed=sample.CardIds.Where(id=>!string.Equals(id,selected[0],StringComparison.OrdinalIgnoreCase)).ToList();
        // This is a store-level compatibility test, not an assertion that the modified deck is legal for API saves.
        var update=f.Store.UpdateDeck(actor.Id,created.Deck!.Id,created.Deck.Revision,new L12PresetDeckDefinition {
            Name="旧版裁剪",MasterId=sample.MasterId,CardIds=changed,MoraleIds=sample.MoraleIds.ToList(),SpecialIds=sample.SpecialIds.ToList() });
        Assert.True(update.Success);
        Assert.Equal(new[] {selected[1]},update.Deck!.RepresentativeCardIds);
        var restarted=new L12PlatformStore(f.MirrorPath);
        Assert.Equal(new[] {selected[1]},restarted.Decks(actor.Id).Single(d=>d.Id==created.Deck.Id).RepresentativeCardIds);
    }
    [Fact]
    public async Task LegacyWireShapeRemainsUnchangedAcrossDeckRoutes()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var actor = f.Store.Accounts().Single(a=>a.Username=="wbplayer");
        var sample = f.Store.Decks(actor.Id).First();
        var selected = sample.CardIds.Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        var deck = JsonSerializer.SerializeToNode(new {name="旧端封面",sample.MasterId,sample.CardIds,sample.MoraleIds,sample.SpecialIds},new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        deck["representativeCardIds"] = JsonSerializer.SerializeToNode(selected);
        using var client = new HttpClient {BaseAddress=f.Client.BaseAddress};
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",f.PlayerToken);
        using var createRequest = new HttpRequestMessage(HttpMethod.Post,"/api/decks") { Content=JsonContent.Create(deck) };
        createRequest.Headers.Add("X-L12-Deck-Cover","2");
        using var create=await client.SendAsync(createRequest); Assert.Equal(HttpStatusCode.Created,create.StatusCode);
        using var created=JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id=created.RootElement.GetProperty("id").GetString(); var revision=created.RootElement.GetProperty("revision").GetInt64();
        using var publishRequest = new HttpRequestMessage(HttpMethod.Post,"/api/public-decks") { Content=JsonContent.Create(new {deck}) };
        publishRequest.Headers.Add("X-L12-Deck-Cover","2");
        using var publish=await client.SendAsync(publishRequest); Assert.Equal(HttpStatusCode.OK,publish.StatusCode);
        using var published=JsonDocument.Parse(await publish.Content.ReadAsStringAsync());
        var code=published.RootElement.GetProperty("publicCode").GetString(); var version=published.RootElement.GetProperty("deck").GetProperty("publicationVersion").GetInt32();
        foreach(var path in new[] {"/api/decks",$"/api/decks/by-id/{id}","/api/public-decks",$"/api/public-decks/{code}",$"/api/public-decks/{code}/current",$"/api/public-decks/{code}/versions/{version}"})
        foreach(var capability in new string?[] {null,"1","3"})
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,path);
            if(capability is not null)request.Headers.Add("X-L12-Deck-Cover",capability);
            using var response=await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            using var result=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.DoesNotContain("representativeCardIds", result.RootElement.GetRawText(), StringComparison.Ordinal);
        }
        var legacy=JsonSerializer.SerializeToNode(new {name="旧端封面",sample.MasterId,sample.CardIds,sample.MoraleIds,sample.SpecialIds},new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var saved=await client.PutAsJsonAsync($"/api/decks/by-id/{id}",new {deck=legacy,expectedRevision=revision});
        Assert.Equal(HttpStatusCode.OK,saved.StatusCode); Assert.DoesNotContain("representativeCardIds",await saved.Content.ReadAsStringAsync(),StringComparison.Ordinal);
        Assert.Equal(selected,f.Store.Decks(actor.Id).Single(d=>d.Id==id).RepresentativeCardIds);
    }
}