using System.Collections;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class CommittedGameplayReadLifecycleTests(ITestOutputHelper output)
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Fact]
    public void LegacyHistoricalSettlementWithoutRankedConfigStillReachesEnsureAndBecomesCommitted()
    {
        using var fixture = new RankedIdentityFixture();
        var first = fixture.CreateAccount("legacyfirst");
        var second = fixture.CreateAccount("legacyother");
        fixture.CommitProfile(first.Id, "order", 1000, 5);
        fixture.CommitProfile(second.Id, "chaos", 1000, 5);
        fixture.Store.SettleRankedMatch("legacy-config-match", first.Id, second.Id, 0);
        var expected = JsonSerializer.Serialize(fixture.Store.RankedSettlement("legacy-config-match", first.Id));
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            var data = Data(fixture.Store);
            data.GetType().GetProperty("RankedConfig")!.SetValue(data, null);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.CaptureOperationsPolicy());
        var reopened = new L12PlatformStore(fixture.Path, fixture.Catalog.PresetDecks,
            officialCards: fixture.Catalog.Cards);
        Assert.Equal(expected, JsonSerializer.Serialize(reopened.RankedSettlement("legacy-config-match", first.Id)));
        Assert.NotNull(reopened.CaptureOperationsPolicy());
    }

    [Theory]
    [InlineData("MasterTitles")]
    [InlineData("RankedSeasonResetRepairs")]
    [InlineData("TransitionMatchIds")]
    [InlineData("SeasonDefinitions")]
    [InlineData("SeasonArchives")]
    public void RepairableLegacyNullableRankedAndSeasonPartsCommitOnlyAfterEnsure(string property)
    {
        using var fixture = new RankedIdentityFixture();
        var first = fixture.CreateAccount("nullfirst");
        var second = fixture.CreateAccount("nullsecond");
        fixture.CommitProfile(first.Id, "order", 1000, 5);
        fixture.CommitProfile(second.Id, "chaos", 1000, 5);
        fixture.Store.SettleRankedMatch("nullable-history", first.Id, second.Id, 0);
        var expected = JsonSerializer.Serialize(fixture.Store.RankedSettlement("nullable-history", first.Id));
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            var data = Data(fixture.Store);
            object target = data;
            if (property == "MasterTitles")
                target = data.GetType().GetProperty("RankedConfig")!.GetValue(data)!;
            else if (property == "TransitionMatchIds")
            {
                var repairs = (IList)data.GetType().GetProperty("RankedSeasonResetRepairs")!.GetValue(data)!;
                var rowType = repairs.GetType().GetGenericArguments()[0];
                target = Activator.CreateInstance(rowType, nonPublic: true)!;
                rowType.GetProperty("SeasonId")!.SetValue(target, fixture.CurrentSeasonId);
                repairs.Add(target);
            }
            if (property.StartsWith("Season", StringComparison.Ordinal))
            {
                var operations = data.GetType().GetProperty("OperationsConfig")!.GetValue(data)!;
                var season = operations.GetType().GetProperty("Season")!.GetValue(operations)!;
                season.GetType().GetProperty("Name")!.SetValue(season, "S01");
            }
            target.GetType().GetProperty(property)!.SetValue(target, null);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.CaptureOperationsPolicy());
        var reopened = new L12PlatformStore(fixture.Path, fixture.Catalog.PresetDecks,
            officialCards: fixture.Catalog.Cards);
        Assert.NotNull(reopened.CaptureOperationsPolicy());
        Assert.Equal(expected, JsonSerializer.Serialize(reopened.RankedSettlement("nullable-history", first.Id)));
        var twice = new L12PlatformStore(fixture.Path, fixture.Catalog.PresetDecks,
            officialCards: fixture.Catalog.Cards);
        Assert.NotNull(twice.CaptureOperationsPolicy());
        Assert.Equal(expected, JsonSerializer.Serialize(twice.RankedSettlement("nullable-history", first.Id)));
    }

    [Fact]
    public void InvalidCommittedFactionMakesTheWholeGameBundleUnavailableWithoutThrowing()
    {
        using var fixture = new RankedIdentityFixture();
        var player = fixture.CreateAccount("badfaction");
        fixture.CommitProfile(player.Id, "invalid-synthetic-faction", 1000, 5);
        var view = fixture.Store.ReadCommittedGamePlatformView("match", new[] { (player.Id, 0) },
            DateTimeOffset.UtcNow, true);
        Assert.False(view.Available);
        Assert.Empty(view.Badges);
        Assert.Empty(view.DrawRequests);
        Assert.Empty(view.Settlements);
    }

    [Fact]
    public void PartiallyMissingBadgeProjectionMakesTheWholeGameBundleUnavailable()
    {
        using var fixture = new RankedIdentityFixture();
        var player = fixture.CreateAccount("partialread");
        var field = typeof(L12PlatformStore).GetField("_committedSessionActivity", Private)!;
        var previous = field.GetValue(fixture.Store)!;
        try
        {
            RankedIdentityTestState.RemoveRankedProjection(fixture.Store);
            var view = fixture.Store.ReadCommittedGamePlatformView("match", new[] { (player.Id, 0) },
                DateTimeOffset.UtcNow, true);
            Assert.False(view.Available);
            Assert.Empty(view.Badges);
            Assert.Empty(view.DrawRequests);
            Assert.Empty(view.Settlements);
        }
        finally { field.SetValue(fixture.Store, previous); }
    }

    [Theory]
    [InlineData("DefaultRoomConfig")]
    [InlineData("DisasterPool")]
    [InlineData("CardRestrictions")]
    [InlineData("DefaultPresetDeckIds")]
    [InlineData("MatchModes")]
    [InlineData("FeatureFlags")]
    [InlineData("Maintenance")]
    [InlineData("ImmediateMaintenance")]
    [InlineData("Announcements")]
    public void LegacyNullableOperationsPartsAreEnsuredBeforeTheirReadProjection(string property)
    {
        using var fixture = new RankedIdentityFixture();
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            var data = Data(fixture.Store);
            var operations = data.GetType().GetProperty("OperationsConfig")!.GetValue(data)!;
            operations.GetType().GetProperty(property)!.SetValue(operations, null);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.CaptureOperationsPolicy());
        var reopened = new L12PlatformStore(fixture.Path, fixture.Catalog.PresetDecks,
            officialCards: fixture.Catalog.Cards);
        Assert.NotNull(reopened.CaptureOperationsPolicy());
        Assert.NotNull(reopened.EffectiveOperationsPolicy());
    }

    [Theory]
    [InlineData("recovery")]
    [InlineData("resolve")]
    [InlineData("leave")]
    public async Task FriendlyDurableDrawFinalizationFailureRetriesFromExplicitRecoveryOrResolve(string mode)
    {
        using var fixture = new RankedIdentityFixture();
        var first = fixture.CreateAccount("retryfirst");
        var second = fixture.CreateAccount("retryother");
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(fixture.Path)!, "draw-retry.db"));
        await recorder.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var manager = new L12RoomManager(fixture.Catalog, recorder, fixture.Store, () => now);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        manager.Connect(a, first.Id, first.Username);
        manager.Connect(b, second.Id, second.Username);
        var code = JsonSerializer.SerializeToElement(manager.CreateRoom(a)[0].Payload, Wire)
            .GetProperty("roomCode").GetString()!;
        manager.JoinRoom(b, code);
        await manager.SetReadyAsync(a, true);
        var started = await manager.SetReadyAsync(b, true);
        var matchId = started.Select(row => JsonSerializer.SerializeToElement(row.Payload, Wire))
            .First(row => row.GetProperty("type").GetString() == "gameState")
            .GetProperty("state").GetProperty("matchId").GetString()!;
        const string requestId = "draw-finalization-retry";
        await manager.RequestMatchDrawAsync(a, requestId, "synthetic issue");
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage == "before-commit" && fixture.Store.MatchDrawRequestForClient(matchId, second.Id,
                    DateTimeOffset.UtcNow)?.Status == "accepted")
                throw new IOException("synthetic governance finalization failure");
        };
        var accepted = await manager.ResolveMatchDrawAsync(b, requestId, true);
        Assert.Contains(accepted, row => JsonSerializer.SerializeToElement(row.Payload, Wire)
            .GetProperty("type").GetString() == "matchGovernanceResult");
        Assert.Equal("accepting", fixture.Store.MatchDrawRequestForClient(matchId, second.Id,
            DateTimeOffset.UtcNow)!.Status);
        if (mode == "leave")
        {
            manager.LeaveRoom(a);
            manager.LeaveRoom(b);
            Assert.Equal(1, manager.RuntimeStats().RoomCount);
            await Tick();
            Assert.Equal(1, manager.RuntimeStats().RoomCount);
        }
        fixture.Store.StorageFailureInjector = null;
        now = now.AddMinutes(6); // Durable conclusion must survive auxiliary five-minute expiry.
        if (mode == "resolve") await manager.ResolveMatchDrawAsync(b, requestId, true);
        else if (mode == "recovery") await manager.RecoveryStateWithAckAsync(b, recovered: true);
        else { await Tick(); Assert.Equal(0, manager.RuntimeStats().RoomCount); }
        Assert.Equal("accepted", fixture.Store.MatchDrawRequestForClient(matchId, second.Id,
            DateTimeOffset.UtcNow)!.Status);
        if (mode != "leave")
        {
            await manager.RecoveryStateWithAckAsync(a, recovered: true);
            await manager.ResolveMatchDrawAsync(b, requestId, true);
        }
        var record = Assert.Single(fixture.Store.MatchDrawRequests(fixture.Admin, search: matchId));
        Assert.Single(record.History, row => row.Action == "settlement-finalized");
        var detail = (await recorder.GetMatchAsync(matchId))!;
        Assert.Single(detail.Commands, row => row.Command.GetProperty("type").GetString() == "authorityConclusion");

        async Task Tick() => await (Task)typeof(L12RoomManager).GetMethod("TickSettlementRoomsAsync", Private)!
            .Invoke(manager, new object[] { now, new List<OutgoingMessage>() })!;
    }

    [Fact]
    public void TenThousandHistoricalSettlementsReuseUnchangedViewsAndUpdateOnlyChangedFacts()
    {
        using var fixture = new RankedIdentityFixture();
        var first = fixture.CreateAccount("largefirst");
        var second = fixture.CreateAccount("largeother");
        fixture.CommitProfile(first.Id, "order", 1000, 5);
        fixture.CommitProfile(second.Id, "chaos", 1000, 5);
        fixture.Store.SettleRankedMatch("history-seed", first.Id, second.Id, 0);
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            var rows = Rows(fixture.Store);
            var type = rows.GetType().GetGenericArguments()[0];
            var templates = rows.Cast<object>().Select(row => JsonSerializer.Serialize(row, type)).ToArray();
            for (var index = 1; index < 5000; index++)
            foreach (var template in templates)
            {
                var row = JsonSerializer.Deserialize(template, type)!;
                type.GetProperty("MatchId")!.SetValue(row, $"history-{index:D5}");
                rows.Add(row);
            }
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        Assert.Equal(10000, Rows(fixture.Store).Count);
        var firstMap = SettlementMap(fixture.Store);
        var data = Data(fixture.Store);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var prepared = typeof(L12PlatformStore).GetMethod("PrepareCommittedSessionActivity", Private)!
            .Invoke(fixture.Store, new[] { data, RankedIdentityTestState.RollbackCache(fixture.Store) })!;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var newReads = prepared.GetType().GetProperty("GameplayReads")!.GetValue(prepared)!;
        Assert.Same(firstMap, newReads.GetType().GetProperty("Settlements")!.GetValue(newReads));
        output.WriteLine("historical_settlements=10000 unchanged_index_prepare_allocated_bytes={0}", allocated);
        Assert.True(allocated < 4 * 1024 * 1024);

        var original = fixture.Store.RankedSettlement("history-seed", first.Id)!;
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            var components = (IList)Rows(fixture.Store)[0]!.GetType().GetProperty("Components")!
                .GetValue(Rows(fixture.Store)[0])!;
            var old = (L12RankedSettlementComponent)components[0]!;
            components[0] = old with { Label = "synthetic changed label" };
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });
        var changed = fixture.Store.RankedSettlement("history-seed", first.Id)!;
        Assert.NotSame(original, changed);
        Assert.NotEqual(original.Components[0].Label, changed.Components[0].Label);
        var unrelated = fixture.Store.RankedSettlement("history-00001", first.Id)!;
        fixture.Store.Register("historynew", "Password123!");
        Assert.Same(unrelated, fixture.Store.RankedSettlement("history-00001", first.Id));
    }

    private static object Data(L12PlatformStore store)
        => typeof(L12PlatformStore).GetProperty("_data", Private)!.GetValue(store)!;
    private static IList Rows(L12PlatformStore store)
        => (IList)Data(store).GetType().GetProperty("RankedSettlements")!.GetValue(Data(store))!;
    private static object SettlementMap(L12PlatformStore store)
    {
        var projection = RankedIdentityTestState.CommittedProjection(store);
        var reads = projection.GetType().GetProperty("GameplayReads")!.GetValue(projection)!;
        return reads.GetType().GetProperty("Settlements")!.GetValue(reads)!;
    }
}
