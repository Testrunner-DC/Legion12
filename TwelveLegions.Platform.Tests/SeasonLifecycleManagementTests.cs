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
    public void CurrentAndNextDefinitionsSharePreviewApplyButKeepRuntimeAndDraftIndependentAcrossRestart()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var initial = store.SeasonCatalog(admin);
            Assert.Equal(store.OperationsConfig(admin).Version, initial.OperationsVersion);
            var originalCurrentName = initial.Current.Name;
            var changedNextRanked = WithFirstTierBaseDelta(initial.Next!.Configuration.Ranked, 3821);
            var nextDraft = Draft(initial.Next) with
            {
                SeasonId = "S02",
                Name = "第二赛季独立草稿",
                Configuration = initial.Next.Configuration with { Ranked = changedNextRanked },
            };
            var nextPreview = store.PreviewSeasonDefinition(admin, initial.Next.DefinitionId,
                nextDraft, initial.Next.Revision, initial.OperationsVersion,
                Context("next-preview", initial.OperationsVersion));

            Assert.Equal("next", nextPreview.Slot);
            Assert.Contains("next-season-draft-only", nextPreview.Warnings);
            var nextApplied = store.ApplySeasonDefinition(admin, initial.Next.DefinitionId,
                nextDraft, initial.Next.Revision, initial.OperationsVersion, nextPreview.PreviewToken,
                "save next slot", Context("next-apply", initial.OperationsVersion));
            Assert.Equal(initial.OperationsVersion, nextApplied.OperationsVersion);
            Assert.Equal(originalCurrentName, store.SeasonCatalog(admin).Current.Name);
            Assert.Equal(3821, nextApplied.Definition.Configuration.Ranked.Factions[0].Tiers[0].BaseDelta);
            Assert.Equal(store.RankedConfig(admin).Factions[0].Tiers[0].BaseDelta,
                initial.Current.Configuration.Ranked.Factions[0].Tiers[0].BaseDelta);

            var afterNext = store.SeasonCatalog(admin);
            var currentDraft = Draft(afterNext.Current) with
            {
                Name = "当前赛季安全修订",
                EndsAt = DateTimeOffset.UtcNow.AddDays(30),
                Configuration = afterNext.Current.Configuration with
                {
                    Ranked = WithFirstTierBaseDelta(afterNext.Current.Configuration.Ranked, 3911),
                },
            };
            var currentPreview = store.PreviewSeasonDefinition(admin, afterNext.Current.DefinitionId,
                currentDraft, afterNext.Current.Revision, afterNext.OperationsVersion,
                Context("current-preview", afterNext.OperationsVersion));
            Assert.Equal("current", currentPreview.Slot);
            Assert.Contains("current-season-changes-apply-immediately", currentPreview.Warnings);
            Assert.Contains("current-ranked-config-affects-subsequent-settlements", currentPreview.Warnings);
            var currentApplied = store.ApplySeasonDefinition(admin, afterNext.Current.DefinitionId,
                currentDraft, afterNext.Current.Revision, afterNext.OperationsVersion,
                currentPreview.PreviewToken, "save current slot",
                Context("current-apply", afterNext.OperationsVersion));

            Assert.Equal(afterNext.OperationsVersion + 1, currentApplied.OperationsVersion);
            Assert.Equal("当前赛季安全修订", store.OperationsConfig(admin).Config.Season.Name);
            Assert.Equal(3911, store.RankedConfig(admin).Factions[0].Tiers[0].BaseDelta);
            Assert.Equal("第二赛季独立草稿", store.SeasonCatalog(admin).Next!.Name);
            Assert.Equal(3821, store.SeasonCatalog(admin).Next!.Configuration.Ranked
                .Factions[0].Tiers[0].BaseDelta);
            Assert.Contains(store.OperationsConfigHistory(admin),
                item => item.Action == "season-current-apply");

            var reopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var reopenedAdmin = reopened.Login("Admin", "L12master").Account!;
            var reopenedCatalog = reopened.SeasonCatalog(reopenedAdmin);
            Assert.Equal(currentApplied.OperationsVersion, reopenedCatalog.OperationsVersion);
            Assert.Equal("当前赛季安全修订", reopenedCatalog.Current.Name);
            Assert.Equal("第二赛季独立草稿", reopenedCatalog.Next!.Name);
            Assert.Equal(3911, reopened.RankedConfig(reopenedAdmin).Factions[0].Tiers[0].BaseDelta);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CurrentDefinitionGuardsIdentityVersionsPreviewBindingAndAtomicRollback()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var initial = store.SeasonCatalog(admin);
            var currentDraft = Draft(initial.Current) with { Name = "允许修改的名称" };

            var identity = Assert.Throws<L12OperationsConfigException>(() =>
                store.PreviewSeasonDefinition(admin, initial.Current.DefinitionId,
                    currentDraft with { SeasonId = "S99" }, initial.Current.Revision,
                    initial.OperationsVersion, Context("identity", initial.OperationsVersion)));
            Assert.Equal("season_identity_read_only", identity.Code);
            var start = Assert.Throws<L12OperationsConfigException>(() =>
                store.PreviewSeasonDefinition(admin, initial.Current.DefinitionId,
                    currentDraft with { StartsAt = DateTimeOffset.UtcNow }, initial.Current.Revision,
                    initial.OperationsVersion, Context("start", initial.OperationsVersion)));
            Assert.Equal("season_start_read_only", start.Code);

            var staleRevision = Assert.Throws<L12OperationsConfigException>(() =>
                store.PreviewSeasonDefinition(admin, initial.Current.DefinitionId, currentDraft,
                    initial.Current.Revision - 1, initial.OperationsVersion,
                    Context("stale-revision", initial.OperationsVersion)));
            Assert.Equal("season_definition_revision_conflict", staleRevision.Code);
            var staleOperations = Assert.Throws<L12OperationsConfigException>(() =>
                store.PreviewSeasonDefinition(admin, initial.Current.DefinitionId, currentDraft,
                    initial.Current.Revision, initial.OperationsVersion - 1,
                    Context("stale-operations", initial.OperationsVersion - 1)));
            Assert.Equal("operations_version_conflict", staleOperations.Code);

            var preview = store.PreviewSeasonDefinition(admin, initial.Current.DefinitionId,
                currentDraft, initial.Current.Revision, initial.OperationsVersion,
                Context("preview-binding", initial.OperationsVersion));
            var stalePreview = Assert.Throws<L12OperationsConfigException>(() =>
                store.ApplySeasonDefinition(admin, initial.Current.DefinitionId,
                    currentDraft with { Name = "预览后又改名" }, initial.Current.Revision,
                    initial.OperationsVersion, preview.PreviewToken, "reject stale preview",
                    Context("preview-stale", initial.OperationsVersion)));
            Assert.Equal("season_preview_stale", stalePreview.Code);

            var next = initial.Next!;
            store.UpdateSeasonDraft(admin, next.DefinitionId,
                Draft(next) with { SeasonId = "S02", Name = "改变衔接状态" }, next.Revision,
                "change linked draft", Context("change-link", next.Revision));
            var stateChangedAfterPreview = Assert.Throws<L12OperationsConfigException>(() =>
                store.ApplySeasonDefinition(admin, initial.Current.DefinitionId, currentDraft,
                    initial.Current.Revision, initial.OperationsVersion, preview.PreviewToken,
                    "reject changed state", Context("state-changed", initial.OperationsVersion)));
            Assert.Equal("season_preview_stale", stateChangedAfterPreview.Code);

            var afterLinkChange = store.SeasonCatalog(admin);
            var sameIdPreview = store.PreviewSeasonDefinition(admin,
                afterLinkChange.Current.DefinitionId, currentDraft,
                afterLinkChange.Current.Revision, afterLinkChange.OperationsVersion,
                Context("same-id-preview", afterLinkChange.OperationsVersion));
            var linkedNext = afterLinkChange.Next!;
            store.UpdateSeasonDraft(admin, linkedNext.DefinitionId,
                Draft(linkedNext) with { Name = "相同赛季 ID 的配置修改" }, linkedNext.Revision,
                "same id draft update", Context("same-id-update", linkedNext.Revision));
            var sameIdStateChanged = Assert.Throws<L12OperationsConfigException>(() =>
                store.ApplySeasonDefinition(admin, afterLinkChange.Current.DefinitionId,
                    currentDraft, afterLinkChange.Current.Revision, afterLinkChange.OperationsVersion,
                    sameIdPreview.PreviewToken, "reject same-id state change",
                    Context("same-id-stale", afterLinkChange.OperationsVersion)));
            Assert.Equal("season_preview_stale", sameIdStateChanged.Code);

            var refreshed = store.SeasonCatalog(admin);
            var rollbackPreview = store.PreviewSeasonDefinition(admin,
                refreshed.Current.DefinitionId, currentDraft, refreshed.Current.Revision,
                refreshed.OperationsVersion, Context("rollback-preview", refreshed.OperationsVersion));

            var beforeOperations = store.OperationsConfig(admin);
            var beforeRanked = store.RankedConfig(admin);
            var beforeCurrent = store.SeasonCatalog(admin).Current;
            var beforeAuditCount = store.AdminAudit().Count;
            store.StorageFailureInjector = stage =>
            {
                if (stage == "before-commit") throw new IOException("injected current season failure");
            };
            Assert.Throws<L12PlatformStorageUnavailableException>(() =>
                store.ApplySeasonDefinition(admin, initial.Current.DefinitionId, currentDraft,
                    refreshed.Current.Revision, refreshed.OperationsVersion,
                    rollbackPreview.PreviewToken,
                    "atomic rollback", Context("rollback", initial.OperationsVersion)));
            store.StorageFailureInjector = null;

            var afterFailureOperations = store.OperationsConfig(admin);
            Assert.Equal(beforeOperations.Version, afterFailureOperations.Version);
            Assert.Equal(beforeOperations.VersionId, afterFailureOperations.VersionId);
            Assert.Equal(beforeOperations.Config.Season, afterFailureOperations.Config.Season);
            Assert.Equal(beforeRanked.Factions[0].Tiers[0].BaseDelta,
                store.RankedConfig(admin).Factions[0].Tiers[0].BaseDelta);
            var afterFailureCurrent = store.SeasonCatalog(admin).Current;
            Assert.Equal(beforeCurrent.DefinitionId, afterFailureCurrent.DefinitionId);
            Assert.Equal(beforeCurrent.Revision, afterFailureCurrent.Revision);
            Assert.Equal(beforeCurrent.Name, afterFailureCurrent.Name);
            Assert.Equal(beforeAuditCount, store.AdminAudit().Count);
            var reopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var reopenedAdmin = reopened.Login("Admin", "L12master").Account!;
            var reopenedOperations = reopened.OperationsConfig(reopenedAdmin);
            Assert.Equal(beforeOperations.Version, reopenedOperations.Version);
            Assert.Equal(beforeOperations.VersionId, reopenedOperations.VersionId);
            var reopenedCurrent = reopened.SeasonCatalog(reopenedAdmin).Current;
            Assert.Equal(beforeCurrent.DefinitionId, reopenedCurrent.DefinitionId);
            Assert.Equal(beforeCurrent.Revision, reopenedCurrent.Revision);
            Assert.Equal(beforeCurrent.Name, reopenedCurrent.Name);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PreviewTokenBindsBothSlotsAcrossCurrentChangesDeletionAndRecreation()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = Catalog();
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var initial = store.SeasonCatalog(admin);
            var preparedNext = store.UpdateSeasonDraft(admin, initial.Next!.DefinitionId,
                Draft(initial.Next) with { SeasonId = "S02", Name = "第二赛季" },
                initial.Next.Revision, "prepare next", Context("prepare-next", initial.Next.Revision));
            var prepared = store.SeasonCatalog(admin);
            var nextDraft = Draft(preparedNext) with { Name = "下一赛季预览候选" };
            var nextPreview = store.PreviewSeasonDefinition(admin, preparedNext.DefinitionId,
                nextDraft, preparedNext.Revision, prepared.OperationsVersion,
                Context("next-before-current", prepared.OperationsVersion));

            var currentDraft = Draft(prepared.Current) with { Name = "当前赛季先行修改" };
            var currentPreview = store.PreviewSeasonDefinition(admin, prepared.Current.DefinitionId,
                currentDraft, prepared.Current.Revision, prepared.OperationsVersion,
                Context("current-change-preview", prepared.OperationsVersion));
            store.ApplySeasonDefinition(admin, prepared.Current.DefinitionId, currentDraft,
                prepared.Current.Revision, prepared.OperationsVersion, currentPreview.PreviewToken,
                "apply current first", Context("current-change-apply", prepared.OperationsVersion));

            var currentChanged = Assert.Throws<L12OperationsConfigException>(() =>
                store.ApplySeasonDefinition(admin, preparedNext.DefinitionId, nextDraft,
                    preparedNext.Revision, prepared.OperationsVersion, nextPreview.PreviewToken,
                    "reject current-changed preview", Context("next-stale", prepared.OperationsVersion)));
            Assert.Equal("season_preview_stale", currentChanged.Code);

            var beforeDelete = store.SeasonCatalog(admin);
            var deletePreview = store.PreviewSeasonDefinition(admin, beforeDelete.Next!.DefinitionId,
                Draft(beforeDelete.Next), beforeDelete.Next.Revision, beforeDelete.OperationsVersion,
                Context("before-delete", beforeDelete.OperationsVersion));
            Assert.True(store.DeleteSeasonDraft(admin, beforeDelete.Next.DefinitionId,
                beforeDelete.Next.Revision, "delete previewed draft",
                Context("delete-previewed", beforeDelete.Next.Revision)));
            var deleted = store.SeasonCatalog(admin);
            var recreated = store.CreateSeasonDraft(admin, deleted.Current.Revision,
                "recreate draft", Context("recreate", deleted.Current.Revision));
            Assert.NotEqual(beforeDelete.Next.DefinitionId, recreated.DefinitionId);

            var recreatedState = Assert.Throws<L12OperationsConfigException>(() =>
                store.ApplySeasonDefinition(admin, beforeDelete.Next.DefinitionId,
                    Draft(beforeDelete.Next), beforeDelete.Next.Revision,
                    beforeDelete.OperationsVersion, deletePreview.PreviewToken,
                    "reject deleted preview", Context("deleted-stale", beforeDelete.OperationsVersion)));
            Assert.Equal("season_preview_stale", recreatedState.Code);
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

            var beforeActiveLegacyOperations = store.OperationsConfig(admin.Account!);
            var beforeActiveLegacyRanked = store.RankedConfig(admin.Account!);
            var beforeActiveLegacyBusinessAudit = store.AdminAudit("operations").Count(item =>
                item.Action is "season-definition-preview" or "season-definition-apply");
            using (var activeLegacyRequest = Authorized(HttpMethod.Put,
                $"/api/admin/seasons/draft/{seasons.Current.DefinitionId}", admin.Token!,
                new SeasonDraftUpdateRequest(Draft(seasons.Current) with { Name = "不得经旧路由修改" },
                    seasons.Current.Revision, "reject active legacy route")))
            using (var activeLegacyResponse = await client.SendAsync(activeLegacyRequest))
                Assert.Equal(HttpStatusCode.BadRequest, activeLegacyResponse.StatusCode);
            var afterActiveLegacy = store.SeasonCatalog(admin.Account!);
            Assert.Equal(seasons.Current.Revision, afterActiveLegacy.Current.Revision);
            Assert.Equal(seasons.Current.Name, afterActiveLegacy.Current.Name);
            Assert.Equal(beforeActiveLegacyOperations.Version, store.OperationsConfig(admin.Account!).Version);
            Assert.Equal(beforeActiveLegacyOperations.VersionId,
                store.OperationsConfig(admin.Account!).VersionId);
            Assert.Equal(beforeActiveLegacyRanked.Factions[0].Tiers[0].BaseDelta,
                store.RankedConfig(admin.Account!).Factions[0].Tiers[0].BaseDelta);
            Assert.Equal(beforeActiveLegacyBusinessAudit, store.AdminAudit("operations").Count(item =>
                item.Action is "season-definition-preview" or "season-definition-apply"));

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

            L12SeasonDefinitionPreviewView preview;
            using (var previewRequest = Authorized(HttpMethod.Post,
                $"/api/admin/seasons/{seasons.Next.DefinitionId}/preview", admin.Token!,
                new SeasonDefinitionPreviewRequest(update, seasons.Next.Revision,
                    seasons.OperationsVersion)))
            using (var previewResponse = await client.SendAsync(previewRequest))
            {
                Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
                preview = (await previewResponse.Content
                    .ReadFromJsonAsync<L12SeasonDefinitionPreviewView>())!;
            }
            var applyBody = new SeasonDefinitionApplyRequest(update, seasons.Next.Revision,
                preview.PreviewToken, "apply through common contract", "season-api-same-key",
                seasons.OperationsVersion);
            using (var missingKeyRequest = Authorized(HttpMethod.Put,
                $"/api/admin/seasons/{seasons.Next.DefinitionId}", admin.Token!,
                applyBody with { IdempotencyKey = null }))
            using (var missingKeyResponse = await client.SendAsync(missingKeyRequest))
                Assert.Equal(HttpStatusCode.BadRequest, missingKeyResponse.StatusCode);
            using (var applyRequest = Authorized(HttpMethod.Put,
                $"/api/admin/seasons/{seasons.Next.DefinitionId}", admin.Token!, applyBody))
            using (var applyResponse = await client.SendAsync(applyRequest))
            {
                Assert.Equal(HttpStatusCode.OK, applyResponse.StatusCode);
                var applied = (await applyResponse.Content
                    .ReadFromJsonAsync<L12SeasonDefinitionOperationView>())!;
                Assert.Equal("next", applied.Slot);
                Assert.Equal(seasons.Next.Revision + 1, applied.Definition.Revision);
            }
            var operationsBeforeReplay = store.OperationsConfig(admin.Account!);
            store.ApplyOperationsConfig(admin.Account!, operationsBeforeReplay.Config,
                operationsBeforeReplay.Version, "change state before explicit replay",
                Context("state-before-replay", operationsBeforeReplay.Version));
            using (var replayRequest = Authorized(HttpMethod.Put,
                $"/api/admin/seasons/{seasons.Next.DefinitionId}", admin.Token!, applyBody))
            using (var replayResponse = await client.SendAsync(replayRequest))
            {
                Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
                Assert.Equal("true", replayResponse.Headers.GetValues("X-Idempotent-Replay").Single());
            }
            using (var crossRouteRequest = Authorized(HttpMethod.Put,
                $"/api/admin/seasons/draft/{seasons.Next.DefinitionId}", admin.Token!,
                new SeasonDraftUpdateRequest(update, seasons.Next.Revision,
                    "cross route key conflict", IdempotencyKey: "season-api-same-key",
                    ExpectedVersion: seasons.OperationsVersion)))
            using (var crossRouteResponse = await client.SendAsync(crossRouteRequest))
            {
                Assert.Equal(HttpStatusCode.Conflict, crossRouteResponse.StatusCode);
                var crossRouteBody = JsonNode.Parse(await crossRouteResponse.Content
                    .ReadAsStringAsync())!.AsObject();
                Assert.Equal("idempotency_conflict", crossRouteBody["code"]!.GetValue<string>());
            }
            using (var conflictRequest = Authorized(HttpMethod.Put,
                $"/api/admin/seasons/{seasons.Next.DefinitionId}", admin.Token!,
                applyBody with { Draft = update with { Name = "同键不同载荷" } }))
            using (var conflictResponse = await client.SendAsync(conflictRequest))
            {
                Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
                var conflictBody = JsonNode.Parse(await conflictResponse.Content.ReadAsStringAsync())!.AsObject();
                Assert.Equal("idempotency_conflict", conflictBody["code"]!.GetValue<string>());
            }

            var afterCommon = store.SeasonCatalog(admin.Account!);
            var legacyUpdate = Draft(afterCommon.Next!) with { Name = "旧路由兼容更新" };
            var legacyBody = new SeasonDraftUpdateRequest(legacyUpdate,
                afterCommon.Next!.Revision, "legacy compatibility", IdempotencyKey: "legacy-update-success");
            using (var legacyRequest = Authorized(HttpMethod.Put,
                $"/api/admin/seasons/draft/{afterCommon.Next.DefinitionId}", admin.Token!, legacyBody))
            using (var legacyResponse = await client.SendAsync(legacyRequest))
            {
                Assert.Equal(HttpStatusCode.OK, legacyResponse.StatusCode);
                var legacyView = (await legacyResponse.Content
                    .ReadFromJsonAsync<L12SeasonDefinitionView>())!;
                Assert.Equal("draft", legacyView.LifecycleStatus);
                Assert.Equal("旧路由兼容更新", legacyView.Name);
            }
            using (var legacyReplayRequest = Authorized(HttpMethod.Put,
                $"/api/admin/seasons/draft/{afterCommon.Next.DefinitionId}", admin.Token!, legacyBody))
            using (var legacyReplayResponse = await client.SendAsync(legacyReplayRequest))
            {
                Assert.Equal(HttpStatusCode.OK, legacyReplayResponse.StatusCode);
                Assert.Equal("true", legacyReplayResponse.Headers
                    .GetValues("X-Idempotent-Replay").Single());
            }
            Assert.Equal("旧路由兼容更新", store.SeasonCatalog(admin.Account!).Next!.Name);

            using var archivesRequest = Authorized(HttpMethod.Get, "/api/admin/seasons/archives", admin.Token!);
            using var archivesResponse = await client.SendAsync(archivesRequest);
            Assert.Equal(HttpStatusCode.OK, archivesResponse.StatusCode);
            Assert.Empty((await archivesResponse.Content.ReadFromJsonAsync<L12SeasonArchiveView[]>())!);

            var beforeDelete = store.SeasonCatalog(admin.Account!);
            var deleteRequestBody = new SeasonDraftDeleteRequest(beforeDelete.Next!.Revision,
                "delete idempotently", "season-delete-key", beforeDelete.OperationsVersion);
            using (var deleteRequest = Authorized(HttpMethod.Delete,
                $"/api/admin/seasons/draft/{beforeDelete.Next.DefinitionId}", admin.Token!,
                deleteRequestBody))
            using (var deleteResponse = await client.SendAsync(deleteRequest))
                Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
            using (var deleteReplayRequest = Authorized(HttpMethod.Delete,
                $"/api/admin/seasons/draft/{beforeDelete.Next.DefinitionId}", admin.Token!,
                deleteRequestBody))
            using (var deleteReplayResponse = await client.SendAsync(deleteReplayRequest))
            {
                Assert.Equal(HttpStatusCode.NoContent, deleteReplayResponse.StatusCode);
                Assert.Equal("true", deleteReplayResponse.Headers
                    .GetValues("X-Idempotent-Replay").Single());
            }
            using (var updateReplayAfterDeleteRequest = Authorized(HttpMethod.Put,
                $"/api/admin/seasons/draft/{beforeDelete.Next.DefinitionId}", admin.Token!, legacyBody))
            using (var updateReplayAfterDeleteResponse = await client.SendAsync(updateReplayAfterDeleteRequest))
            {
                Assert.Equal(HttpStatusCode.OK, updateReplayAfterDeleteResponse.StatusCode);
                Assert.Equal("true", updateReplayAfterDeleteResponse.Headers
                    .GetValues("X-Idempotent-Replay").Single());
                var replayedView = (await updateReplayAfterDeleteResponse.Content
                    .ReadFromJsonAsync<L12SeasonDefinitionView>())!;
                Assert.Equal("旧路由兼容更新", replayedView.Name);
            }

            var beforeCreate = store.SeasonCatalog(admin.Account!);
            var createRequestBody = new SeasonDraftCreateRequest(beforeCreate.Current.Revision,
                "create idempotently");
            using (var createRequest = Authorized(HttpMethod.Post, "/api/admin/seasons/draft",
                admin.Token!, createRequestBody))
            using (var createResponse = await client.SendAsync(createRequest))
                Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
            using (var createReplayRequest = Authorized(HttpMethod.Post, "/api/admin/seasons/draft",
                admin.Token!, createRequestBody))
            using (var createReplayResponse = await client.SendAsync(createReplayRequest))
            {
                Assert.Equal(HttpStatusCode.Conflict, createReplayResponse.StatusCode);
                Assert.False(createReplayResponse.Headers.Contains("X-Idempotent-Replay"));
            }
            var created = store.SeasonCatalog(admin.Account!);
            Assert.NotNull(created.Next);

            using (var secondDeleteRequest = Authorized(HttpMethod.Delete,
                $"/api/admin/seasons/draft/{created.Next!.DefinitionId}", admin.Token!,
                new SeasonDraftDeleteRequest(created.Next.Revision, "delete before legal recreate",
                    "season-delete-before-recreate", created.OperationsVersion)))
            using (var secondDeleteResponse = await client.SendAsync(secondDeleteRequest))
                Assert.Equal(HttpStatusCode.NoContent, secondDeleteResponse.StatusCode);
            var beforeRecreate = store.SeasonCatalog(admin.Account!);
            Assert.True(beforeRecreate.Current.Revision > beforeCreate.Current.Revision);
            using (var recreateRequest = Authorized(HttpMethod.Post, "/api/admin/seasons/draft",
                admin.Token!, new SeasonDraftCreateRequest(beforeRecreate.Current.Revision,
                    "create idempotently")))
            using (var recreateResponse = await client.SendAsync(recreateRequest))
            {
                Assert.Equal(HttpStatusCode.OK, recreateResponse.StatusCode);
                Assert.False(recreateResponse.Headers.Contains("X-Idempotent-Replay"));
            }
            Assert.NotNull(store.SeasonCatalog(admin.Account!).Next);

            var noKeyUpdateCatalog = store.SeasonCatalog(admin.Account!);
            var noKeyUpdateBody = new SeasonDraftUpdateRequest(
                Draft(noKeyUpdateCatalog.Next!) with { SeasonId = "S03", Name = "无键成功后状态保护" },
                noKeyUpdateCatalog.Next!.Revision, "no key success state protection");
            using (var noKeyUpdateRequest = Authorized(HttpMethod.Put,
                $"/api/admin/seasons/draft/{noKeyUpdateCatalog.Next.DefinitionId}", admin.Token!,
                noKeyUpdateBody))
            using (var noKeyUpdateResponse = await client.SendAsync(noKeyUpdateRequest))
                Assert.Equal(HttpStatusCode.OK, noKeyUpdateResponse.StatusCode);
            using (var noKeyUpdateRepeatRequest = Authorized(HttpMethod.Put,
                $"/api/admin/seasons/draft/{noKeyUpdateCatalog.Next.DefinitionId}", admin.Token!,
                noKeyUpdateBody))
            using (var noKeyUpdateRepeatResponse = await client.SendAsync(noKeyUpdateRepeatRequest))
            {
                Assert.Equal(HttpStatusCode.Conflict, noKeyUpdateRepeatResponse.StatusCode);
                Assert.False(noKeyUpdateRepeatResponse.Headers.Contains("X-Idempotent-Replay"));
            }

            var versionBindingCatalog = store.SeasonCatalog(admin.Account!);
            var versionBindingBody = new SeasonDraftUpdateRequest(
                Draft(versionBindingCatalog.Next!) with { Name = "版本绑定校验" },
                versionBindingCatalog.Next!.Revision - 1, "same legacy payload across versions");
            using (var beforeVersionChangeRequest = Authorized(HttpMethod.Put,
                $"/api/admin/seasons/draft/{versionBindingCatalog.Next.DefinitionId}", admin.Token!,
                versionBindingBody))
            using (var beforeVersionChangeResponse = await client.SendAsync(beforeVersionChangeRequest))
            {
                Assert.Equal(HttpStatusCode.Conflict, beforeVersionChangeResponse.StatusCode);
                Assert.False(beforeVersionChangeResponse.Headers.Contains("X-Idempotent-Replay"));
            }
            var operationsBeforeVersionChange = store.OperationsConfig(admin.Account!);
            store.ApplyOperationsConfig(admin.Account!, operationsBeforeVersionChange.Config,
                operationsBeforeVersionChange.Version, "advance operations version",
                Context("advance-operations", operationsBeforeVersionChange.Version));
            using (var afterVersionChangeRequest = Authorized(HttpMethod.Put,
                $"/api/admin/seasons/draft/{versionBindingCatalog.Next.DefinitionId}", admin.Token!,
                versionBindingBody))
            using (var afterVersionChangeResponse = await client.SendAsync(afterVersionChangeRequest))
            {
                Assert.Equal(HttpStatusCode.Conflict, afterVersionChangeResponse.StatusCode);
                Assert.False(afterVersionChangeResponse.Headers.Contains("X-Idempotent-Replay"));
            }

            var noKeyDeleteCatalog = store.SeasonCatalog(admin.Account!);
            var noKeyDeleteBody = new SeasonDraftDeleteRequest(noKeyDeleteCatalog.Next!.Revision,
                "no key delete state protection");
            using (var noKeyDeleteRequest = Authorized(HttpMethod.Delete,
                $"/api/admin/seasons/draft/{noKeyDeleteCatalog.Next.DefinitionId}", admin.Token!,
                noKeyDeleteBody))
            using (var noKeyDeleteResponse = await client.SendAsync(noKeyDeleteRequest))
                Assert.Equal(HttpStatusCode.NoContent, noKeyDeleteResponse.StatusCode);
            using (var noKeyDeleteRepeatRequest = Authorized(HttpMethod.Delete,
                $"/api/admin/seasons/draft/{noKeyDeleteCatalog.Next.DefinitionId}", admin.Token!,
                noKeyDeleteBody))
            using (var noKeyDeleteRepeatResponse = await client.SendAsync(noKeyDeleteRepeatRequest))
            {
                Assert.Equal(HttpStatusCode.NotFound, noKeyDeleteRepeatResponse.StatusCode);
                Assert.False(noKeyDeleteRepeatResponse.Headers.Contains("X-Idempotent-Replay"));
            }
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

    [Fact]
    public async Task LegacyRequestsWithoutKeysRetryRecoveredDependenciesWhileExplicitFailuresReplayAcrossRestart()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = Catalog();
            var initial = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var initialAdmin = initial.Login("Admin", "L12master").Account!;
            var initialSeasons = initial.SeasonCatalog(initialAdmin);
            var restrictedCard = catalog.Cards.Keys.First(cardId =>
                !initialSeasons.Next!.Configuration.CardRestrictions.Any(item =>
                    item.CardId.Equals(cardId, StringComparison.OrdinalIgnoreCase)));
            var prepared = initial.UpdateSeasonDraft(initialAdmin, initialSeasons.Next!.DefinitionId,
                Draft(initialSeasons.Next) with
                {
                    SeasonId = "S02",
                    Name = "恢复依赖测试草稿",
                    Configuration = initialSeasons.Next.Configuration with
                    {
                        CardRestrictions = initialSeasons.Next.Configuration.CardRestrictions
                            .Append(new L12CardRestrictionConfig(restrictedCard, 0, "dependency recovery"))
                            .ToArray(),
                    },
                }, initialSeasons.Next.Revision, "prepare dependency recovery",
                Context("prepare-recovery", initialSeasons.Next.Revision));
            var candidate = Draft(prepared) with { Name = "依赖恢复后可保存" };
            var noKeyBody = new SeasonDraftUpdateRequest(candidate, prepared.Revision,
                "recoverable no-key request");
            var explicitBody = new SeasonDraftUpdateRequest(candidate, prepared.Revision,
                "recoverable explicit request", IdempotencyKey: "recoverable-explicit-failure");
            var reducedCards = catalog.Cards.Where(pair => !pair.Key.Equals(restrictedCard,
                    StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            var reducedStore = new L12PlatformStore(path, catalog.PresetDecks, officialCards: reducedCards);
            var reducedLogin = reducedStore.Login("Admin", "L12master");
            await using (var reducedRecorder = new MatchRecorder(Path.Combine(root, "reduced-matches.db")))
            {
                await reducedRecorder.InitializeAsync();
                var reducedRooms = new L12RoomManager(catalog, reducedRecorder, reducedStore);
                await using var reducedServer = new L12WebSocketServer(reducedRooms, reducedRecorder,
                    reducedStore, catalog);
                await reducedServer.StartAsync(0);
                using var client = new HttpClient
                {
                    BaseAddress = new Uri(Assert.Single(reducedServer.Addresses)),
                };
                using (var noKeyRequest = Authorized(HttpMethod.Put,
                    $"/api/admin/seasons/draft/{prepared.DefinitionId}", reducedLogin.Token!, noKeyBody))
                using (var noKeyResponse = await client.SendAsync(noKeyRequest))
                {
                    Assert.Equal(HttpStatusCode.BadRequest, noKeyResponse.StatusCode);
                    Assert.False(noKeyResponse.Headers.Contains("X-Idempotent-Replay"));
                }
                using (var explicitRequest = Authorized(HttpMethod.Put,
                    $"/api/admin/seasons/draft/{prepared.DefinitionId}", reducedLogin.Token!, explicitBody))
                using (var explicitResponse = await client.SendAsync(explicitRequest))
                {
                    Assert.Equal(HttpStatusCode.BadRequest, explicitResponse.StatusCode);
                    Assert.False(explicitResponse.Headers.Contains("X-Idempotent-Replay"));
                    var failure = JsonNode.Parse(await explicitResponse.Content.ReadAsStringAsync())!.AsObject();
                    Assert.Equal("unknown_restricted_card", failure["code"]!.GetValue<string>());
                }
                await reducedServer.StopAsync();
            }

            var fullStore = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var fullLogin = fullStore.Login("Admin", "L12master");
            var recoveredCatalog = fullStore.SeasonCatalog(fullLogin.Account!);
            var directPreview = fullStore.PreviewNextSeasonDefinition(fullLogin.Account!,
                prepared.DefinitionId, candidate, prepared.Revision, recoveredCatalog.OperationsVersion,
                Context("recovered-preview", recoveredCatalog.OperationsVersion));
            Assert.True(directPreview.Valid);
            await using (var fullRecorder = new MatchRecorder(Path.Combine(root, "full-matches.db")))
            {
                await fullRecorder.InitializeAsync();
                var fullRooms = new L12RoomManager(catalog, fullRecorder, fullStore);
                await using var fullServer = new L12WebSocketServer(fullRooms, fullRecorder, fullStore, catalog);
                await fullServer.StartAsync(0);
                using var client = new HttpClient
                {
                    BaseAddress = new Uri(Assert.Single(fullServer.Addresses)),
                };
                using (var recoveredRequest = Authorized(HttpMethod.Put,
                    $"/api/admin/seasons/draft/{prepared.DefinitionId}", fullLogin.Token!, noKeyBody))
                using (var recoveredResponse = await client.SendAsync(recoveredRequest))
                {
                    Assert.Equal(HttpStatusCode.OK, recoveredResponse.StatusCode);
                    Assert.False(recoveredResponse.Headers.Contains("X-Idempotent-Replay"));
                }
                using (var explicitReplayRequest = Authorized(HttpMethod.Put,
                    $"/api/admin/seasons/draft/{prepared.DefinitionId}", fullLogin.Token!, explicitBody))
                using (var explicitReplayResponse = await client.SendAsync(explicitReplayRequest))
                {
                    Assert.Equal(HttpStatusCode.BadRequest, explicitReplayResponse.StatusCode);
                    Assert.Equal("true", explicitReplayResponse.Headers
                        .GetValues("X-Idempotent-Replay").Single());
                    var replayedFailure = JsonNode.Parse(await explicitReplayResponse.Content
                        .ReadAsStringAsync())!.AsObject();
                    Assert.Equal("unknown_restricted_card", replayedFailure["code"]!.GetValue<string>());
                }
                using (var explicitConflictRequest = Authorized(HttpMethod.Put,
                    $"/api/admin/seasons/draft/{prepared.DefinitionId}", fullLogin.Token!,
                    explicitBody with { Draft = candidate with { Name = "同键异载荷" } }))
                using (var explicitConflictResponse = await client.SendAsync(explicitConflictRequest))
                {
                    Assert.Equal(HttpStatusCode.Conflict, explicitConflictResponse.StatusCode);
                    var conflict = JsonNode.Parse(await explicitConflictResponse.Content
                        .ReadAsStringAsync())!.AsObject();
                    Assert.Equal("idempotency_conflict", conflict["code"]!.GetValue<string>());
                }
                await fullServer.StopAsync();
            }
        }
        finally
        {
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

    private static L12SeasonDefinitionDraft Draft(L12SeasonDefinitionView source)
        => new(source.SeasonId, source.Name, source.StartsAt, source.EndsAt, source.Configuration);

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
