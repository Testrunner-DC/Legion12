using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class SeasonManagementObservabilityTests
{
    [Fact]
    public void ImpactPreviewIsReadOnlyRepeatableAndExplicitlyUnarmed()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var now = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
            var (store, admin, _, seasons, draft) = PrepareDraft(root, now.AddHours(2));
            var readiness = Ready(seasons.Current.SeasonId);
            var beforeJson = File.ReadAllBytes(path);
            var beforeAuditCount = store.AdminAudit("operations").Count;

            var first = store.PreviewSeasonActivation(admin, draft.DefinitionId,
                seasons.Current.Revision, draft.Revision, seasons.OperationsVersion,
                readiness, now);
            var second = store.PreviewSeasonActivation(admin, draft.DefinitionId,
                seasons.Current.Revision, draft.Revision, seasons.OperationsVersion,
                readiness, now.AddMinutes(1));

            Assert.True(first.Valid);
            Assert.Equal("unarmed", first.PlanStatus);
            Assert.Equal(0, first.PlanGeneration);
            Assert.Equal("free", first.LeaseState);
            Assert.Equal(first.PreviewToken, second.PreviewToken);
            Assert.Equal(beforeJson, File.ReadAllBytes(path));
            Assert.Equal(beforeAuditCount, store.AdminAudit("operations").Count);
            Assert.Null(store.SeasonCatalog(admin, now).Next!.ActivationPlan);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void PreviewCountsReuseFinalizationEligibilityAndDeduplicateAccounts()
    {
        var root = TempRoot();
        try
        {
            var now = new DateTimeOffset(2026, 10, 2, 1, 0, 0, TimeSpan.Zero);
            var (store, admin, _, seasons, draft) = PrepareDraft(root, now.AddHours(2));
            var first = store.Register("预览参与甲", "Password123!").Account!;
            var second = store.Register("预览参与乙", "Password123!").Account!;
            var idle = store.Register("预览未参赛", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            store.SelectRankedFaction(idle.Id, "fate");
            store.SettleRankedDrawMatch("impact-deduplicated-match", first.Id, second.Id);

            var preview = store.PreviewSeasonActivation(admin, draft.DefinitionId,
                seasons.Current.Revision, draft.Revision, seasons.OperationsVersion,
                Ready(seasons.Current.SeasonId), now);

            Assert.Equal(2, preview.SettlementParticipantCount);
            Assert.Equal(2, preview.HistoryRecordCount);
            Assert.Equal(2, preview.SummaryNotificationCount);
            Assert.DoesNotContain(preview.BlockingCodes, code => code == "season_cutover_not_ready");
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void ArmRejectsMissingTokenAndEveryStaleAuthorityBinding()
    {
        var root = TempRoot();
        try
        {
            var now = new DateTimeOffset(2026, 10, 2, 2, 0, 0, TimeSpan.Zero);
            var (store, admin, _, seasons, draft) = PrepareDraft(root, now.AddHours(2));
            var readiness = Ready(seasons.Current.SeasonId);
            var preview = store.PreviewSeasonActivation(admin, draft.DefinitionId,
                seasons.Current.Revision, draft.Revision, seasons.OperationsVersion,
                readiness, now);

            AssertCode("season_activation_preview_stale", () => store.ArmSeasonActivation(admin,
                draft.DefinitionId, seasons.Current.Revision, draft.Revision,
                seasons.OperationsVersion, string.Empty, readiness, "missing token", now,
                Context("missing", seasons.OperationsVersion)));
            AssertCode("season_definition_revision_conflict", () => store.ArmSeasonActivation(admin,
                draft.DefinitionId, seasons.Current.Revision + 1, draft.Revision,
                seasons.OperationsVersion, preview.PreviewToken, readiness, "stale current", now,
                Context("current", seasons.OperationsVersion)));
            AssertCode("season_definition_revision_conflict", () => store.ArmSeasonActivation(admin,
                draft.DefinitionId, seasons.Current.Revision, draft.Revision + 1,
                seasons.OperationsVersion, preview.PreviewToken, readiness, "stale draft", now,
                Context("draft", seasons.OperationsVersion)));
            AssertCode("operations_version_conflict", () => store.ArmSeasonActivation(admin,
                draft.DefinitionId, seasons.Current.Revision, draft.Revision,
                seasons.OperationsVersion + 1, preview.PreviewToken, readiness, "stale operations", now,
                Context("operations", seasons.OperationsVersion + 1)));

            Assert.True(store.DeleteSeasonDraft(admin, draft.DefinitionId, draft.Revision,
                "replace draft identity", Context("delete")));
            var afterDelete = store.SeasonCatalog(admin, now);
            var replacement = store.CreateSeasonDraft(admin, afterDelete.Current.Revision,
                "new draft identity", Context("create"));
            replacement = store.UpdateSeasonDraft(admin, replacement.DefinitionId,
                new L12SeasonDefinitionDraft("S-observe-replacement", "替换赛季",
                    now.AddHours(3), now.AddDays(90), replacement.Configuration),
                replacement.Revision, "complete replacement", Context("complete"));
            var replacementCatalog = store.SeasonCatalog(admin, now);
            var replacementReadiness = Ready(replacementCatalog.Current.SeasonId);
            AssertCode("season_activation_preview_stale", () => store.ArmSeasonActivation(admin,
                replacement.DefinitionId, replacementCatalog.Current.Revision, replacement.Revision,
                replacementCatalog.OperationsVersion, preview.PreviewToken, replacementReadiness,
                "stale definition", now, Context("definition", replacementCatalog.OperationsVersion)));
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void AuditFailureBlocksArmAndPlanShowsHeldThenExpiredLeaseWithoutWorkerIdentity()
    {
        var root = TempRoot();
        try
        {
            var now = new DateTimeOffset(2026, 10, 2, 3, 0, 0, TimeSpan.Zero);
            var scheduledAt = now.AddMinutes(5);
            var (store, admin, _, seasons, draft) = PrepareDraft(root, scheduledAt);
            var readiness = Ready(seasons.Current.SeasonId);
            store.AuditAvailabilityProbeOverride = () => false;
            var blocked = store.PreviewSeasonActivation(admin, draft.DefinitionId,
                seasons.Current.Revision, draft.Revision, seasons.OperationsVersion,
                readiness, now);
            Assert.False(blocked.Valid);
            Assert.Contains("audit_unavailable", blocked.BlockingCodes);
            AssertCode("audit_unavailable", () => store.ArmSeasonActivation(admin,
                draft.DefinitionId, seasons.Current.Revision, draft.Revision,
                seasons.OperationsVersion, blocked.PreviewToken, readiness, "blocked audit", now,
                Context("blocked", seasons.OperationsVersion)));

            store.AuditAvailabilityProbeOverride = () => true;
            var allowed = store.PreviewSeasonActivation(admin, draft.DefinitionId,
                seasons.Current.Revision, draft.Revision, seasons.OperationsVersion,
                readiness, now);
            var armed = store.ArmSeasonActivation(admin, draft.DefinitionId,
                seasons.Current.Revision, draft.Revision, seasons.OperationsVersion,
                allowed.PreviewToken, readiness, "arm observable plan", now,
                Context("arm", seasons.OperationsVersion));
            var claim = store.TryClaimDueSeasonActivation("private-worker-path", scheduledAt,
                TimeSpan.FromMinutes(2));
            Assert.NotNull(claim);
            var held = store.SeasonCatalog(admin, scheduledAt).Next!.ActivationPlan!;
            Assert.Equal("held", held.LeaseState);
            Assert.Equal("private-worker-path", held.LeaseOwner);
            Assert.NotEqual("private-worker-path", held.IntentMask);
            var expired = store.SeasonCatalog(admin, scheduledAt.AddMinutes(2)).Next!.ActivationPlan!;
            Assert.Equal("expired", expired.LeaseState);
            Assert.Equal(armed.ActivationPlan!.ArmedAt, held.ArmedAt);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void ArmedAtSurvivesRestartDisarmAndAutomaticCutoverKeepsA31TitlesConsistent()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var now = new DateTimeOffset(2026, 10, 2, 4, 0, 0, TimeSpan.Zero);
            var scheduledAt = now.AddMinutes(5);
            var (store, admin, catalog, seasons, draft) = PrepareDraft(root, scheduledAt);
            var first = store.Register("自动总结甲", "Password123!").Account!;
            var second = store.Register("自动总结乙", "Password123!").Account!;
            store.SelectRankedFaction(first.Id, "order");
            store.SelectRankedFaction(second.Id, "chaos");
            store.SettleRankedDrawMatch("automatic-summary-title", first.Id, second.Id);
            var readiness = Ready(seasons.Current.SeasonId);
            var preview = store.PreviewSeasonActivation(admin, draft.DefinitionId,
                seasons.Current.Revision, draft.Revision, seasons.OperationsVersion,
                readiness, now);
            var armed = store.ArmSeasonActivation(admin, draft.DefinitionId,
                seasons.Current.Revision, draft.Revision, seasons.OperationsVersion,
                preview.PreviewToken, readiness, "arm persistent plan", now,
                Context("arm", seasons.OperationsVersion));
            Assert.Equal(now, armed.ActivationPlan!.ArmedAt);

            var reopened = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var reopenedAdmin = reopened.Login("Admin", "L12master").Account!;
            Assert.Equal(now, reopened.SeasonCatalog(reopenedAdmin, now).Next!.ActivationPlan!.ArmedAt);
            AssertCode("season_activation_plan_conflict", () => reopened.DisarmSeasonActivation(
                reopenedAdmin, draft.DefinitionId, armed.Revision,
                armed.ActivationPlan.Generation + 1, armed.ActivationPlan.DisarmGuardToken,
                "reject replaced generation", now.AddMinutes(1), Context("stale-generation")));
            AssertCode("season_activation_plan_conflict", () => reopened.DisarmSeasonActivation(
                reopenedAdmin, draft.DefinitionId, armed.Revision,
                armed.ActivationPlan.Generation, armed.ActivationPlan.DisarmGuardToken + "-stale",
                "reject stale guard", now.AddMinutes(1), Context("stale-guard")));
            var disarmed = reopened.DisarmSeasonActivation(reopenedAdmin, draft.DefinitionId,
                armed.Revision, armed.ActivationPlan.Generation,
                armed.ActivationPlan.DisarmGuardToken, "verify disarm cleanup",
                now.AddMinutes(1), Context("disarm"));
            Assert.Equal(now, disarmed.ActivationPlan!.ArmedAt);
            Assert.Equal("free", disarmed.ActivationPlan.LeaseState);

            var afterDisarm = reopened.SeasonCatalog(reopenedAdmin, now.AddMinutes(1));
            var rePreview = reopened.PreviewSeasonActivation(reopenedAdmin, draft.DefinitionId,
                afterDisarm.Current.Revision, afterDisarm.Next!.Revision,
                afterDisarm.OperationsVersion, readiness, now.AddMinutes(1));
            var reArmed = reopened.ArmSeasonActivation(reopenedAdmin, draft.DefinitionId,
                afterDisarm.Current.Revision, afterDisarm.Next.Revision,
                afterDisarm.OperationsVersion, rePreview.PreviewToken, readiness,
                "rearm for automatic cutover", now.AddMinutes(1),
                Context("rearm", afterDisarm.OperationsVersion));
            var claim = reopened.TryClaimDueSeasonActivation("worker-title-check", scheduledAt,
                TimeSpan.FromMinutes(1))!;
            var activated = reopened.ActivateClaimedSeason(reopenedAdmin, claim,
                "automatic cutover title check", readiness, scheduledAt,
                Context("activate", claim.ExpectedOperationsVersion));

            Assert.Equal("completed", activated.Current.ActivationPlan!.Status);
            Assert.Equal(reArmed.ActivationPlan!.ArmedAt, activated.Current.ActivationPlan.ArmedAt);
            Assert.Equal("free", activated.Current.ActivationPlan.LeaseState);
            Assert.Equal(seasons.Current.Name, activated.Archive.Name);
            var history = Assert.Single(reopened.RankedOverview(first.Id).History);
            var summary = Assert.Single(reopened.PendingSeasonSummaryNotifications(first.Id));
            Assert.Equal(activated.Archive.SeasonId, history.SeasonId);
            Assert.Equal("历史赛季", history.SeasonName);
            Assert.Equal(history.SeasonName, summary.SeasonName);
            Assert.Equal(history.Titles.ToArray(), summary.Titles.ToArray());
            var completedCatalog = reopened.SeasonCatalog(reopenedAdmin, scheduledAt);
            Assert.Null(completedCatalog.Next);
            Assert.Equal("completed", completedCatalog.Current.ActivationPlan!.Status);
            Assert.Equal("no-action-required",
                completedCatalog.Current.ActivationPlan.SuggestedActionCode);
        }
        finally { Cleanup(root); }
    }

    private static (L12PlatformStore Store, L12AccountView Admin, L12Catalog Catalog,
        L12SeasonCatalogView Seasons, L12SeasonDefinitionView Draft) PrepareDraft(
        string root, DateTimeOffset scheduledAt)
    {
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
            officialCards: catalog.Cards);
        var admin = store.Login("Admin", "L12master").Account!;
        var seasons = store.SeasonCatalog(admin, scheduledAt.AddHours(-1));
        var draft = store.UpdateSeasonDraft(admin, seasons.Next!.DefinitionId,
            new L12SeasonDefinitionDraft("S-observe", "运营观察赛季", scheduledAt,
                scheduledAt.AddDays(90), seasons.Next.Configuration), seasons.Next.Revision,
            "prepare observable season", Context("prepare"));
        return (store, admin, catalog, store.SeasonCatalog(admin, scheduledAt.AddHours(-1)), draft);
    }

    private static L12RankedSeasonCutoverReadiness Ready(string seasonId)
        => new(seasonId, 0, 0, 0, 0);

    private static void AssertCode(string code, Action action)
        => Assert.Equal(code, Assert.Throws<L12OperationsConfigException>(action).Code);

    private static L12AdminAuditContext Context(string id, long? expected = null)
        => new(id, ExpectedVersion: expected, RequestMethod: "TEST", RequestPath: "/test");

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-season-observability-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string root)
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(root, true);
    }
}
