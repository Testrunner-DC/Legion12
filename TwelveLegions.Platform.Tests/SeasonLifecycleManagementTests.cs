using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class SeasonLifecycleManagementTests
{
    [Fact]
    public void ExistingRuntimeAndPendingGradientMigrateIntoPersistentDefinitionsWithoutInventingArchives()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            _ = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            RewriteAsLegacy(path, data =>
            {
                data["RankedPendingGradient"]!["Tiers"]![0]!["BaseDelta"] = 4321;
            });
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var runtime = store.OperationsConfig(admin);
            var ranked = store.RankedConfig(admin);

            var seasons = store.SeasonCatalog(admin);

            Assert.False(seasons.AutomaticActivationEnabled);
            Assert.Equal(runtime.Config.Season.Id, seasons.Current.SeasonId);
            Assert.Equal("active", seasons.Current.LifecycleStatus);
            Assert.NotNull(seasons.Next);
            Assert.Equal("draft", seasons.Next!.LifecycleStatus);
            Assert.Equal(seasons.Current.SeasonId, seasons.Next.PreviousSeasonId);
            Assert.Equal(4321, ranked.PendingGradient!.Tiers[0].BaseDelta);
            Assert.Equal(ranked.PendingGradient!.Tiers.Select(TierValues),
                seasons.Next.Configuration.Ranked.Factions[0].Tiers.Select(TierValues));
            Assert.Empty(seasons.Archives);

            var persisted = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.Equal(1, persisted["SeasonLifecycleMigrationVersion"]!.GetValue<int>());
            Assert.Equal(2, persisted["SeasonDefinitions"]!.AsArray().Count);
            Assert.Empty(persisted["SeasonArchives"]!.AsArray());
            var currentDefinitionId = seasons.Current.DefinitionId;
            var draftDefinitionId = seasons.Next.DefinitionId;

            var reopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var reopenedAdmin = reopened.Login("Admin", "L12master").Account!;
            var reopenedSeasons = reopened.SeasonCatalog(reopenedAdmin);
            Assert.Equal(currentDefinitionId, reopenedSeasons.Current.DefinitionId);
            Assert.Equal(draftDefinitionId, reopenedSeasons.Next!.DefinitionId);
            Assert.Empty(reopenedSeasons.Archives);

            SqliteConnection.ClearAllPools();
            File.Delete(Path.Combine(root, "platform.db"));
            var jsonReopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var jsonAdmin = jsonReopened.Login("Admin", "L12master").Account!;
            var jsonSeasons = jsonReopened.SeasonCatalog(jsonAdmin);
            Assert.Equal(currentDefinitionId, jsonSeasons.Current.DefinitionId);
            Assert.Equal(draftDefinitionId, jsonSeasons.Next!.DefinitionId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DeletedDraftDoesNotReturnAfterSqliteOrJsonRestart()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var draft = store.SeasonCatalog(admin).Next!;

            Assert.True(store.DeleteSeasonDraft(admin, draft.DefinitionId, draft.Revision,
                "cancel next season", Context("season-draft-delete", draft.Revision)));
            Assert.Null(store.SeasonCatalog(admin).Next);
            Assert.Null(store.RankedConfig(admin).PendingGradient);

            var sqliteReopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var sqliteAdmin = sqliteReopened.Login("Admin", "L12master").Account!;
            Assert.Null(sqliteReopened.SeasonCatalog(sqliteAdmin).Next);
            Assert.Null(sqliteReopened.RankedConfig(sqliteAdmin).PendingGradient);

            SqliteConnection.ClearAllPools();
            File.Delete(Path.Combine(root, "platform.db"));
            var jsonReopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var jsonAdmin = jsonReopened.Login("Admin", "L12master").Account!;
            Assert.Null(jsonReopened.SeasonCatalog(jsonAdmin).Next);
            Assert.Null(jsonReopened.RankedConfig(jsonAdmin).PendingGradient);
            var persisted = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.Equal(1, persisted["SeasonLifecycleMigrationVersion"]!.GetValue<int>());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DeletedDraftFromPreMarkerCandidateDoesNotReturnDuringCompatibilityUpgrade()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var draft = store.SeasonCatalog(admin).Next!;
            store.DeleteSeasonDraft(admin, draft.DefinitionId, draft.Revision,
                "simulate accepted candidate", Context("pre-marker-delete", draft.Revision));

            RewriteSnapshot(path, data => data.Remove("SeasonLifecycleMigrationVersion"));
            var reopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var reopenedAdmin = reopened.Login("Admin", "L12master").Account!;

            Assert.Null(reopened.SeasonCatalog(reopenedAdmin).Next);
            Assert.Null(reopened.RankedConfig(reopenedAdmin).PendingGradient);
            var persisted = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.Equal(1, persisted["SeasonLifecycleMigrationVersion"]!.GetValue<int>());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void LegacyStoreWithoutPendingGradientPreservesRankedCompatibilitySemantics(
        int gradientVersion, bool expectsDraft)
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            _ = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            RewriteAsLegacy(path, data =>
            {
                data["RankedGradientVersion"] = gradientVersion;
                data.Remove("RankedPendingGradient");
            });

            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var seasons = store.SeasonCatalog(admin);

            Assert.Equal(expectsDraft, seasons.Next is not null);
            Assert.Equal(expectsDraft, store.RankedConfig(admin).PendingGradient is not null);
            var persisted = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.Equal(1, persisted["SeasonLifecycleMigrationVersion"]!.GetValue<int>());

            var reopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var reopenedAdmin = reopened.Login("Admin", "L12master").Account!;
            Assert.Equal(expectsDraft, reopened.SeasonCatalog(reopenedAdmin).Next is not null);
            Assert.Equal(expectsDraft, reopened.RankedConfig(reopenedAdmin).PendingGradient is not null);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SeasonLifecycleMigrationIsIdempotentUnderRepeatedConcurrentCalls()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var expected = store.SeasonCatalog(admin);

            Parallel.For(0, 64, _ => store.EnsureSeasonLifecycleState());

            var actual = store.SeasonCatalog(admin);
            Assert.Equal(expected.Current.DefinitionId, actual.Current.DefinitionId);
            Assert.Equal(expected.Next!.DefinitionId, actual.Next!.DefinitionId);
            var persisted = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.Equal(1, persisted["SeasonLifecycleMigrationVersion"]!.GetValue<int>());
            Assert.Equal(2, persisted["SeasonDefinitions"]!.AsArray().Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DraftEditsUseOptimisticLockPersistNumericGradientAndLeaveCurrentRuntimeUntouched()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var beforeOperations = store.OperationsConfig(admin);
            var beforeRanked = store.RankedConfig(admin);
            var draft = store.SeasonCatalog(admin).Next!;
            var changedRanked = WithFirstTierBaseDelta(draft.Configuration.Ranked, 3811);
            var input = new L12SeasonDefinitionDraft("S02", "第二赛季",
                DateTimeOffset.UtcNow.AddDays(7), DateTimeOffset.UtcNow.AddDays(97),
                draft.Configuration with { Ranked = changedRanked });

            var updated = store.UpdateSeasonDraft(admin, draft.DefinitionId, input, draft.Revision,
                "prepare season two", Context("season-draft-update", draft.Revision));

            Assert.Equal(draft.Revision + 1, updated.Revision);
            Assert.Equal("S02", updated.SeasonId);
            Assert.Equal(3811, updated.Configuration.Ranked.Factions[0].Tiers[0].BaseDelta);
            Assert.All(updated.Configuration.Ranked.Factions,
                faction => Assert.Equal(3811, faction.Tiers[0].BaseDelta));
            var afterOperations = store.OperationsConfig(admin);
            Assert.Equal(beforeOperations.Version, afterOperations.Version);
            Assert.Equal(beforeOperations.VersionId, afterOperations.VersionId);
            Assert.Equal(beforeOperations.Config.Season, afterOperations.Config.Season);
            Assert.Equal(beforeOperations.Config.DisasterPool.CardIds,
                afterOperations.Config.DisasterPool.CardIds);
            Assert.Equal(beforeRanked.Factions[0].Tiers[0].BaseDelta,
                store.RankedConfig(admin).Factions[0].Tiers[0].BaseDelta);
            Assert.Equal(3811, store.RankedConfig(admin).PendingGradient!.Tiers[0].BaseDelta);
            var stale = Assert.Throws<L12OperationsConfigException>(() => store.UpdateSeasonDraft(admin,
                draft.DefinitionId, input, draft.Revision, "stale retry",
                Context("season-draft-stale", draft.Revision)));
            Assert.Equal("season_definition_revision_conflict", stale.Code);
            var audit = Assert.Single(store.AdminAudit("operations")
                .Where(item => item.Action == "season-draft-update"));
            Assert.Equal(draft.Revision, audit.ExpectedVersion);
            Assert.Equal("prepare season two", audit.Reason);

            SqliteConnection.ClearAllPools();
            File.Delete(Path.Combine(root, "platform.db"));
            var reopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var reopenedAdmin = reopened.Login("Admin", "L12master").Account!;
            Assert.Equal(3811, reopened.SeasonCatalog(reopenedAdmin).Next!
                .Configuration.Ranked.Factions[0].Tiers[0].BaseDelta);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DraftManagementApiEnforcesPermissionsConflictsAndKeepsAutomaticActivationDisabled()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = Catalog();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master");
            var player = store.Register("tseason29", "Password123!");
            Assert.True(player.Success);
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store);
            server = new L12WebSocketServer(rooms, recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };

            using (var anonymousResponse = await client.GetAsync("/api/admin/seasons"))
            {
                var anonymousBody = await anonymousResponse.Content.ReadAsStringAsync();
                Assert.True(anonymousResponse.StatusCode == HttpStatusCode.Unauthorized,
                    $"Expected 401, received {(int)anonymousResponse.StatusCode}: {anonymousBody}");
            }
            using (var playerRequest = Authorized(HttpMethod.Get, "/api/admin/seasons", player.Token!))
            using (var response = await client.SendAsync(playerRequest))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            L12SeasonCatalogView seasons;
            using (var adminRequest = Authorized(HttpMethod.Get, "/api/admin/seasons", admin.Token!))
            using (var response = await client.SendAsync(adminRequest))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                seasons = (await response.Content.ReadFromJsonAsync<L12SeasonCatalogView>())!;
            }
            Assert.False(seasons.AutomaticActivationEnabled);
            Assert.NotNull(seasons.Next);

            var update = new L12SeasonDefinitionDraft("S02", "第二赛季", null, null,
                seasons.Next!.Configuration);
            using var staleRequest = Authorized(HttpMethod.Put,
                $"/api/admin/seasons/draft/{seasons.Next.DefinitionId}", admin.Token!,
                new SeasonDraftUpdateRequest(update, seasons.Next.Revision - 1, "stale api update"));
            using var staleResponse = await client.SendAsync(staleRequest);
            Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
            var body = JsonNode.Parse(await staleResponse.Content.ReadAsStringAsync())!.AsObject();
            Assert.Equal("season_definition_revision_conflict", body["code"]!.GetValue<string>());

            using var staleDeleteRequest = Authorized(HttpMethod.Delete,
                $"/api/admin/seasons/draft/{seasons.Next.DefinitionId}", admin.Token!,
                new SeasonDraftDeleteRequest(seasons.Next.Revision - 1, "stale api delete"));
            using var staleDeleteResponse = await client.SendAsync(staleDeleteRequest);
            Assert.Equal(HttpStatusCode.Conflict, staleDeleteResponse.StatusCode);
            var deleteBody = JsonNode.Parse(await staleDeleteResponse.Content.ReadAsStringAsync())!.AsObject();
            Assert.Equal("season_definition_revision_conflict", deleteBody["code"]!.GetValue<string>());

            using var archivesRequest = Authorized(HttpMethod.Get, "/api/admin/seasons/archives", admin.Token!);
            using var archivesResponse = await client.SendAsync(archivesRequest);
            Assert.Equal(HttpStatusCode.OK, archivesResponse.StatusCode);
            Assert.Empty((await archivesResponse.Content.ReadFromJsonAsync<L12SeasonArchiveView[]>())!);
        }
        finally
        {
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Directory.Delete(root, true);
        }
    }

    private static L12RankedConfigView WithFirstTierBaseDelta(L12RankedConfigView source, int value)
        => source with
        {
            PendingGradient = null,
            Factions = source.Factions.Select(faction => faction with
            {
                Tiers = faction.Tiers.Select((tier, index) =>
                    index == 0 ? tier with { BaseDelta = value } : tier).ToArray(),
            }).ToArray(),
        };

    private static (string Name, int Minimum, int BaseDelta, int WinStreakCap,
        int LossProtectionCap, int RatingGapCap, int StreakTerminationReward) TierValues(
            L12RankedTierGradientConfig tier)
        => (tier.Name, tier.Minimum, tier.BaseDelta, tier.WinStreakCap, tier.LossProtectionCap,
            tier.RatingGapCap, tier.StreakTerminationReward);

    private static (string Name, int Minimum, int BaseDelta, int WinStreakCap,
        int LossProtectionCap, int RatingGapCap, int StreakTerminationReward) TierValues(
            L12RankedTierConfig tier)
        => (tier.Name, tier.Minimum, tier.BaseDelta, tier.WinStreakCap, tier.LossProtectionCap,
            tier.RatingGapCap, tier.StreakTerminationReward);

    private static L12Catalog Catalog()
        => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));

    private static void RewriteAsLegacy(string path, Action<JsonObject> mutate)
        => RewriteSnapshot(path, data =>
        {
            data.Remove("SeasonLifecycleMigrationVersion");
            data.Remove("SeasonDefinitions");
            data.Remove("SeasonArchives");
            mutate(data);
        });

    private static void RewriteSnapshot(string path, Action<JsonObject> mutate)
    {
        var data = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        mutate(data);
        SqliteConnection.ClearAllPools();
        File.Delete(Path.Combine(Path.GetDirectoryName(path)!, "platform.db"));
        File.WriteAllText(path, data.ToJsonString());
    }

    private static L12AdminAuditContext Context(string correlationId, long? expectedVersion = null)
        => new(correlationId, ExpectedVersion: expectedVersion, RequestMethod: "TEST", RequestPath: "/test");

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(L12CorrelationIds.HeaderName, Guid.NewGuid().ToString("N"));
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-season-lifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
