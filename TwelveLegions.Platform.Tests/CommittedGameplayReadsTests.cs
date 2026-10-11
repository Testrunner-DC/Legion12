using System.Diagnostics;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class CommittedGameplayReadsTests
{
    [Theory]
    [InlineData("capture-policy", false)]
    [InlineData("effective-policy", false)]
    [InlineData("draw", false)]
    [InlineData("settlement", false)]
    [InlineData("capture-policy", true)]
    [InlineData("effective-policy", true)]
    [InlineData("draw", true)]
    [InlineData("settlement", true)]
    [InlineData("response-preference", false)]
    [InlineData("response-preference", true)]
    public async Task CriticalReadDoesNotWaitForAnUnrelatedCommit(string read, bool fail)
    {
        using var fixture = new RankedIdentityFixture();
        var first = fixture.CreateAccount("crfirst");
        var second = fixture.CreateAccount("crsecond");
        var now = DateTimeOffset.UtcNow;
        fixture.CommitProfile(first.Id, "order", 1000, 5);
        fixture.CommitProfile(second.Id, "chaos", 1000, 5);
        fixture.Store.SettleRankedMatch("projection-match", first.Id, second.Id, 0);
        var draw = fixture.Store.CreateMatchDrawRequest("projection-draw", "projection-match",
            "PROJ01", "ranked", first, second, "synthetic rule issue", now);
        object? Query() => read switch
        {
            "capture-policy" => fixture.Store.CaptureOperationsPolicy(),
            "effective-policy" => fixture.Store.EffectiveOperationsPolicy(now),
            "draw" => fixture.Store.MatchDrawRequestForClient(draw.MatchId, second.Id, now),
            "settlement" => fixture.Store.RankedSettlement(draw.MatchId, second.Id),
            "response-preference" => fixture.Store.ResponsePreference(second.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(read)),
        };
        var expected = JsonSerializer.Serialize(Query());
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("synthetic unrelated platform commit barrier");
            if (fail) throw new IOException("synthetic unrelated platform commit failure");
        };
        var writer = Task.Factory.StartNew(() =>
        {
            try { fixture.Store.Register("crwriter", "Password123!"); return false; }
            catch (L12PlatformStorageUnavailableException) { return true; }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<(string Value, TimeSpan Duration)>? reader = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            reader = Task.Factory.StartNew(() =>
            {
                var watch = Stopwatch.StartNew();
                var value = JsonSerializer.Serialize(Query());
                return (value, watch.Elapsed);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var observed = await reader.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(expected, observed.Value);
            Assert.True(observed.Duration < TimeSpan.FromMilliseconds(250));
        }
        finally
        {
            release.Set();
            Assert.Equal(fail, await writer.WaitAsync(TimeSpan.FromSeconds(5)));
            fixture.Store.StorageFailureInjector = null;
            if (reader is not null) await reader.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(expected, JsonSerializer.Serialize(Query()));
    }

    [Fact]
    public void ExpiredDrawProjectionDoesNotWriteOrInvalidateTheCommittedGeneration()
    {
        using var fixture = new RankedIdentityFixture();
        var first = fixture.CreateAccount("exfirst");
        var second = fixture.CreateAccount("exsecond");
        var now = DateTimeOffset.UtcNow;
        var draw = fixture.Store.CreateMatchDrawRequest("expiry-read", "expiry-read-match", "EXP001",
            "ranked", first, second, "synthetic rule issue", now);
        var version = fixture.Store.Version;
        var observed = fixture.Store.MatchDrawRequestForClient(draw.MatchId, second.Id,
            draw.ExpiresAt);
        Assert.NotNull(observed);
        Assert.Equal("expired", observed.Status);
        Assert.False(observed.ViewerCanRespond);
        Assert.Equal(version, fixture.Store.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WholeBroadcastProjectionStaysOnOneDurableGeneration(bool fail)
    {
        using var fixture = new RankedIdentityFixture();
        var first = fixture.CreateAccount("genfirst");
        var second = fixture.CreateAccount("gensecond");
        fixture.CommitProfile(first.Id, "order", 1000, 5);
        fixture.CommitProfile(second.Id, "chaos", 1000, 5);
        var now = DateTimeOffset.UtcNow;
        var request = fixture.Store.CreateMatchDrawRequest("generation-draw", "generation-match",
            "GEN001", "friendly", first, second, "synthetic private reason", now);
        fixture.Store.SettleRankedMatch(request.MatchId, first.Id, second.Id, 0);
        var viewers = new[] { (first.Id, 0), (second.Id, 1) };
        var before = fixture.Store.ReadCommittedGamePlatformView(request.MatchId, viewers, now, true);
        var beforeJson = JsonSerializer.Serialize(before);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("synthetic generation barrier");
            if (fail) throw new IOException("synthetic generation failure");
        };
        var writer = Task.Factory.StartNew(() =>
        {
            try
            {
                fixture.Store.ExecuteAdminTransaction(() =>
                {
                    RankedIdentityTestState.SetProfile(fixture.Store, first.Id, fixture.CurrentSeasonId,
                        "fate", 200_000, 5);
                    fixture.Store.ResolveMatchDrawRequest(request.MatchId, request.Id, second, false, now);
                    return true;
                });
                return false;
            }
            catch (L12PlatformStorageUnavailableException) { return true; }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            for (var index = 0; index < 100; index++)
                Assert.Equal(beforeJson, JsonSerializer.Serialize(fixture.Store.ReadCommittedGamePlatformView(
                    request.MatchId, viewers, now, true)));
        }
        finally
        {
            release.Set();
            Assert.Equal(fail, await writer.WaitAsync(TimeSpan.FromSeconds(5)));
            fixture.Store.StorageFailureInjector = null;
        }
        var after = fixture.Store.ReadCommittedGamePlatformView(request.MatchId, viewers, now, true);
        Assert.Equal(beforeJson, JsonSerializer.Serialize(before)); // Retained view is immutable.
        Assert.Equal(fail ? "pending" : "rejected", after.DrawRequests[first.Id]!.Status);
        Assert.Equal(fail, before.Revision == after.Revision);
        Assert.Equal(fixture.Store.Version, after.Revision);
        Assert.Equal(new[] { 0, 1 }, after.Badges.OrderBy(row => row.PlayerIndex).Select(row => row.PlayerIndex));
        Assert.All(viewers, viewer => Assert.Equal(JsonSerializer.Serialize(fixture.Store.RankedSettlement(
            request.MatchId, viewer.Item1)), JsonSerializer.Serialize(after.Settlements[viewer.Item1])));
        Assert.Null(fixture.Store.MatchDrawRequestForClient(request.MatchId, "unrelated-viewer", now));
    }

    [Theory]
    [InlineData("before-gameplay-read-index-build")]
    [InlineData("after-gameplay-read-index-build")]
    public void PreparationFailureNeverPublishesStagedPolicy(string failureStage)
    {
        using var fixture = new RankedIdentityFixture();
        var policy = fixture.Store.CaptureOperationsPolicy();
        var projection = RankedIdentityTestState.CommittedProjection(fixture.Store);
        var config = fixture.Store.OperationsConfig(fixture.Admin);
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage == failureStage) throw new IOException("synthetic gameplay view preparation failure");
        };
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.ApplyOperationsConfig(
            fixture.Admin, config.Config with
            {
                Maintenance = config.Config.Maintenance with { Enabled = true, Message = "synthetic staged maintenance" },
            }, config.Version, "synthetic failed policy", fixture.Context));
        fixture.Store.StorageFailureInjector = null;
        Assert.Same(projection, RankedIdentityTestState.CommittedProjection(fixture.Store));
        Assert.Equal(policy, fixture.Store.CaptureOperationsPolicy());
        var reopened = new L12PlatformStore(fixture.Path, fixture.Catalog.PresetDecks,
            officialCards: fixture.Catalog.Cards);
        Assert.Equal(JsonSerializer.Serialize(policy), JsonSerializer.Serialize(reopened.CaptureOperationsPolicy()));
    }

    [Fact]
    public void PublishedPolicyAndSettlementCollectionsAreImmutable()
    {
        using var fixture = new RankedIdentityFixture();
        var policy = fixture.Store.CaptureOperationsPolicy();
        Assert.IsNotType<string[]>(policy.DisasterCardIds);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)policy.DisasterCardIds)[0] = "changed");
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, bool>)policy.FeatureFlags).Clear());
        var first = fixture.CreateAccount("immutfirst");
        var second = fixture.CreateAccount("immutsecond");
        fixture.CommitProfile(first.Id, "order", 1000, 5);
        fixture.CommitProfile(second.Id, "chaos", 1000, 5);
        fixture.Store.SettleRankedMatch("immutable-settlement", first.Id, second.Id, 0);
        var settlement = fixture.Store.RankedSettlement("immutable-settlement", first.Id)!;
        Assert.Throws<NotSupportedException>(() => ((IList<L12RankedSettlementComponent>)settlement.Components).Clear());
    }

    [Fact]
    public void MissingCommittedProjectionFailsClosedWithoutAuthorityFallback()
    {
        using var fixture = new RankedIdentityFixture();
        var field = typeof(L12PlatformStore).GetField("_committedSessionActivity",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var original = field.GetValue(fixture.Store)!;
        try
        {
            var unavailable = typeof(L12PlatformStore).GetField("UnavailableSessionActivity",
                BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            field.SetValue(fixture.Store, unavailable);
            Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.CaptureOperationsPolicy());
            Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.EffectiveOperationsPolicy());
            Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.ResponsePreference("unknown"));
            Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.Store.RankedSettlement("match", "unknown"));
            var view = fixture.Store.ReadCommittedGamePlatformView("match", new[] { ("unknown", 0) },
                DateTimeOffset.UtcNow, true);
            Assert.False(view.Available);
            Assert.Empty(view.Badges);
            Assert.Empty(view.DrawRequests);
            Assert.Empty(view.Settlements);
        }
        finally { field.SetValue(fixture.Store, original); }
    }
}
