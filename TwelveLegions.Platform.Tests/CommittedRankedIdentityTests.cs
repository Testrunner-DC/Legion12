using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class CommittedRankedIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueryReturnsOldGenerationWithoutWaitingForCommit(bool fail)
    {
        using var fixture = new RankedIdentityFixture();
        var player = fixture.CreateAccount("rankbarrier");
        fixture.CommitProfile(player.Id, "order", sevenValue: 0, placementPlayed: 0);
        var oldIdentity = fixture.Store.RankedBattleIdentity(player.Id, 0);
        Assert.False(oldIdentity.HighestTier);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("synthetic ranked commit barrier");
            if (fail) throw new IOException("synthetic ranked commit failure");
        };
        var writer = Task.Factory.StartNew(() =>
        {
            try
            {
                fixture.CommitProfile(player.Id, "order", sevenValue: 100_000,
                    placementPlayed: 5);
                return false;
            }
            catch (L12PlatformStorageUnavailableException)
            {
                return true;
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<(L12RankedBattleIdentityView identity, TimeSpan Elapsed)>? query = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            query = Task.Factory.StartNew(() =>
            {
                var watch = Stopwatch.StartNew();
                var identity = fixture.Store.RankedBattleIdentity(player.Id, 0);
                return (identity, watch.Elapsed);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var observed = await query.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(oldIdentity, observed.identity);
            Assert.True(observed.Elapsed < TimeSpan.FromMilliseconds(250));
        }
        finally
        {
            release.Set();
            Assert.Equal(fail, await writer.WaitAsync(TimeSpan.FromSeconds(5)));
            fixture.Store.StorageFailureInjector = null;
            if (query is not null)
                await query.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var committed = fixture.Store.RankedBattleIdentity(player.Id, 0);
        Assert.Equal(!fail, committed.HighestTier);
        Assert.Equal(!fail, new L12PlatformStore(fixture.Path, fixture.Catalog.PresetDecks,
            officialCards: fixture.Catalog.Cards).RankedBattleIdentity(player.Id, 0).HighestTier);
    }

    [Theory]
    [InlineData("before-ranked-identity-index-build")]
    [InlineData("after-ranked-identity-index-build")]
    public void IndexBuildFailureKeepsDatabaseRollbackCacheAndIdentityOnOldGeneration(string fault)
    {
        using var fixture = new RankedIdentityFixture();
        var player = fixture.CreateAccount("rankbuild");
        fixture.CommitProfile(player.Id, "order", sevenValue: 0, placementPlayed: 0);
        var committed = RankedIdentityTestState.CommittedProjection(fixture.Store);
        var ranked = RankedIdentityTestState.RankedProjection(fixture.Store);
        var cache = RankedIdentityTestState.RollbackCache(fixture.Store);
        var version = fixture.Store.Version;
        var reached = false;
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != fault) return;
            reached = true;
            throw new IOException("synthetic ranked index failure");
        };
        Assert.Throws<L12PlatformStorageUnavailableException>(() => fixture.CommitProfile(player.Id,
            "order", sevenValue: 100_000, placementPlayed: 5));
        fixture.Store.StorageFailureInjector = null;

        Assert.True(reached);
        Assert.Equal(version, fixture.Store.Version);
        Assert.Same(committed, RankedIdentityTestState.CommittedProjection(fixture.Store));
        Assert.Same(ranked, RankedIdentityTestState.RankedProjection(fixture.Store));
        Assert.Same(cache, RankedIdentityTestState.RollbackCache(fixture.Store));
        Assert.False(fixture.Store.RankedBattleIdentity(player.Id, 0).HighestTier);
        Assert.False(new L12PlatformStore(fixture.Path, fixture.Catalog.PresetDecks,
            officialCards: fixture.Catalog.Cards).RankedBattleIdentity(player.Id, 0).HighestTier);

        fixture.CommitProfile(player.Id, "order", sevenValue: 100_000, placementPlayed: 5);
        Assert.True(fixture.Store.RankedBattleIdentity(player.Id, 0).HighestTier);
    }

    [Fact]
    public void ReadsDoNotCreateProfilesAdvanceVersionOrReplaceCommittedCache()
    {
        using var fixture = new RankedIdentityFixture();
        var player = fixture.CreateAccount("rankread");
        var version = fixture.Store.Version;
        var profileCount = RankedIdentityTestState.ProfileCount(fixture.Store);
        var committed = RankedIdentityTestState.CommittedProjection(fixture.Store);
        var ranked = RankedIdentityTestState.RankedProjection(fixture.Store);
        var cache = RankedIdentityTestState.RollbackCache(fixture.Store);

        var first = fixture.Store.RankedBattleIdentity(player.Id, 0);
        var second = fixture.Store.RankedBattleIdentity(player.Id, 1);

        Assert.Equal(string.Empty, first.Faction);
        Assert.Equal(string.Empty, first.Tier);
        Assert.Null(first.Rank);
        Assert.Equal(0, first.PlayerIndex);
        Assert.Equal(1, second.PlayerIndex);
        Assert.Equal(version, fixture.Store.Version);
        Assert.Equal(profileCount, RankedIdentityTestState.ProfileCount(fixture.Store));
        Assert.Same(committed, RankedIdentityTestState.CommittedProjection(fixture.Store));
        Assert.Same(ranked, RankedIdentityTestState.RankedProjection(fixture.Store));
        Assert.Same(cache, RankedIdentityTestState.RollbackCache(fixture.Store));
    }

    [Fact]
    public void EquivalentSeasonIsReadWithoutHealingAndOldSeasonProjectsZeroProgress()
    {
        using var fixture = new RankedIdentityFixture();
        var player = fixture.CreateAccount("rankseason");
        var equivalent = RankedIdentityTestState.CompatibilityVariant(fixture.CurrentSeasonId);
        fixture.CommitProfile(player.Id, "chaos", sevenValue: 100_001, placementPlayed: 5,
            seasonId: equivalent);
        var sameSeason = fixture.Store.RankedBattleIdentity(player.Id, 0);
        Assert.True(sameSeason.HighestTier);
        Assert.Equal(equivalent, RankedIdentityTestState.ProfileValue<string>(fixture.Store,
            player.Id, "SeasonId"));

        fixture.CommitProfile(player.Id, "chaos", sevenValue: 100_001, placementPlayed: 5,
            seasonId: "retired-season");
        var profileCount = RankedIdentityTestState.ProfileCount(fixture.Store);
        var oldSeason = fixture.Store.RankedBattleIdentity(player.Id, 0);
        Assert.Equal("混沌", oldSeason.Faction);
        Assert.Equal("定级 0/5", oldSeason.Tier);
        Assert.False(oldSeason.HighestTier);
        Assert.Null(oldSeason.Rank);
        Assert.Equal("retired-season", RankedIdentityTestState.ProfileValue<string>(fixture.Store,
            player.Id, "SeasonId"));
        Assert.Equal(100_001, RankedIdentityTestState.ProfileValue<int>(fixture.Store,
            player.Id, "SevenValue"));
        Assert.Equal(profileCount, RankedIdentityTestState.ProfileCount(fixture.Store));
    }

    [Fact]
    public void FullyTiedMaskedPublicNamesKeepRankedProfileSourceOrder()
    {
        using var fixture = new RankedIdentityFixture();
        var players = new[]
            {
                fixture.CreateAccount("rankorder-a"),
                fixture.CreateAccount("rankorder-b"),
                fixture.CreateAccount("rankorder-c"),
            }
            .OrderByDescending(row => row.Id, StringComparer.Ordinal)
            .ToArray();
        var legacyNames = new[] { "fuckx", "shitx", "httpx" };
        Assert.Equal("****x", Assert.Single(legacyNames.Select(L12UsernamePolicy.PublicName)
            .Distinct(StringComparer.Ordinal)));

        fixture.Store.ExecuteAdminTransaction(() =>
        {
            for (var index = 0; index < players.Length; index++)
            {
                RankedIdentityTestState.SetAccountUsername(fixture.Store, players[index].Id,
                    legacyNames[index]);
                RankedIdentityTestState.SetProfile(fixture.Store, players[index].Id,
                    fixture.CurrentSeasonId, "order", 100_000, 5, hiddenRating: 1500);
            }
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });

        var faction = fixture.Store.RankedConfig(fixture.Admin).Factions.Single(row =>
            row.Id == "order");
        for (var index = 0; index < players.Length; index++)
        {
            var expectedRank = index + 1;
            var live = RankedIdentityTestState.LiveRanks(fixture.Store, players[index].Id);
            var committed = RankedIdentityTestState.CommittedRanks(fixture.Store,
                players[index].Id);
            var identity = fixture.Store.RankedBattleIdentity(players[index].Id, index);
            Assert.Equal(expectedRank, live.Faction);
            Assert.Equal(expectedRank, live.Overall);
            Assert.Equal(expectedRank, committed.Faction);
            Assert.Equal(expectedRank, committed.Overall);
            Assert.Equal(index, committed.SourceOrder);
            Assert.Equal(expectedRank, identity.Rank);
            Assert.Equal(index, identity.PlayerIndex);
            Assert.Equal(index == 0 ? faction.FirstTitle : faction.TopFiveTitle,
                identity.PlacementTitle);
        }
    }

    [Fact]
    public void FullyTiedGhostProfilesKeepRankedProfileSourceOrder()
    {
        using var fixture = new RankedIdentityFixture();
        var accountIds = new[] { "ghost-order-c", "ghost-order-b", "ghost-order-a" };
        fixture.Store.ExecuteAdminTransaction(() =>
        {
            foreach (var accountId in accountIds)
                RankedIdentityTestState.SetProfile(fixture.Store, accountId,
                    fixture.CurrentSeasonId, "order", 100_000, 5, hiddenRating: 1500);
            RankedIdentityTestState.RequestSave(fixture.Store);
            return true;
        });

        for (var index = 0; index < accountIds.Length; index++)
        {
            var expectedRank = index + 1;
            var live = RankedIdentityTestState.LiveRanks(fixture.Store, accountIds[index]);
            var committed = RankedIdentityTestState.CommittedRanks(fixture.Store,
                accountIds[index]);
            Assert.Equal(expectedRank, live.Faction);
            Assert.Equal(expectedRank, live.Overall);
            Assert.Equal(expectedRank, committed.Faction);
            Assert.Equal(expectedRank, committed.Overall);
            Assert.Equal(index, committed.SourceOrder);
        }
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("delete")]
    public async Task AccountLifecycleChangeIsInvisibleUntilItsCommit(string action)
    {
        using var fixture = new RankedIdentityFixture();
        var player = fixture.CreateAccount("ranklife");
        fixture.CommitProfile(player.Id, "fate", sevenValue: 100_000, placementPlayed: 5);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("synthetic account lifecycle barrier");
        };
        var writer = Task.Factory.StartNew(() => fixture.Store.ExecuteAdminTransaction(() =>
        {
            if (action == "disable")
                fixture.Store.SetAccountDisabled(fixture.Admin, player.Id, true, "synthetic",
                    fixture.Context, true);
            else
                fixture.Store.DeleteAccountPersonalData(fixture.Admin, player.Id, "synthetic",
                    fixture.Context, true);
            return true;
        }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            Assert.True(fixture.Store.RankedBattleIdentity(player.Id, 0).HighestTier);
        }
        finally
        {
            release.Set();
            await writer.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Store.StorageFailureInjector = null;
        }
        Assert.Throws<KeyNotFoundException>(() => fixture.Store.RankedBattleIdentity(player.Id, 0));
    }

    [Fact]
    public async Task UnavailableRankedProjectionFailsClosedWithoutTakingGlobalGate()
    {
        using var fixture = new RankedIdentityFixture();
        var player = fixture.CreateAccount("rankclosed");
        RankedIdentityTestState.RemoveRankedProjection(fixture.Store);
        var gate = RankedIdentityTestState.Gate(fixture.Store);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Factory.StartNew(() =>
        {
            lock (gate)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("synthetic unavailable projection gate holder");
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<Exception>? query = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            query = Task.Factory.StartNew(() => Record.Exception(() =>
                    fixture.Store.RankedBattleIdentity(player.Id, 0)), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var error = await query.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.IsType<L12PlatformStorageUnavailableException>(error);
        }
        finally
        {
            release.Set();
            await holder.WaitAsync(TimeSpan.FromSeconds(5));
            if (query is not null)
                await query.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}

internal sealed class RankedIdentityFixture : IDisposable
{
    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
        "l12-committed-ranked", Guid.NewGuid().ToString("N"));
    internal string Path => System.IO.Path.Combine(_root, "platform.json");
    internal L12Catalog Catalog { get; }
    internal L12PlatformStore Store { get; }
    internal L12AccountView Admin { get; }
    internal string CurrentSeasonId => Store.OperationsConfig(Admin).Config.Season.Id;
    internal L12AdminAuditContext Context { get; } = new("committed-ranked-identity",
        "admin.accounts.status.write");

    internal RankedIdentityFixture()
    {
        Catalog = L12Catalog.Load(System.IO.Path.Combine(AppContext.BaseDirectory,
            "TwelveLegions", "Data"));
        Store = new L12PlatformStore(Path, Catalog.PresetDecks, officialCards: Catalog.Cards);
        Admin = Store.Login("Admin", "L12master").Account!;
    }

    internal L12AccountView CreateAccount(string username)
        => Store.Register(username, "Password123!").Account!;

    internal void CommitProfile(string accountId, string? faction, int sevenValue,
        int placementPlayed, double hiddenRating = 1500, string? selectedMasterTitle = null,
        string? seasonId = null)
        => Store.ExecuteAdminTransaction(() =>
        {
            RankedIdentityTestState.SetProfile(Store, accountId, seasonId ?? CurrentSeasonId,
                faction, sevenValue, placementPlayed, hiddenRating, selectedMasterTitle);
            RankedIdentityTestState.RequestSave(Store);
            return true;
        });

    public void Dispose()
    {
        Store.StorageFailureInjector = null;
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

internal static class RankedIdentityTestState
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    internal static object Gate(L12PlatformStore store)
        => typeof(L12PlatformStore).GetField("_gate", PrivateInstance)!.GetValue(store)!;

    internal static object CommittedProjection(L12PlatformStore store)
        => typeof(L12PlatformStore).GetField("_committedSessionActivity", PrivateInstance)!
            .GetValue(store)!;

    internal static object RankedProjection(L12PlatformStore store)
    {
        var committed = CommittedProjection(store);
        return committed.GetType().GetProperty("RankedIdentity")!.GetValue(committed)!;
    }

    internal static byte[] RollbackCache(L12PlatformStore store)
        => (byte[])typeof(L12PlatformStore).GetField("_lastCommittedSnapshot", PrivateInstance)!
            .GetValue(store)!;

    internal static int ProfileCount(L12PlatformStore store)
        => Profiles(store).Count;

    internal static T ProfileValue<T>(L12PlatformStore store, string accountId, string property)
        => (T)Profile(store, accountId).GetType().GetProperty(property)!
            .GetValue(Profile(store, accountId))!;

    internal static (int Faction, int Overall) LiveRanks(L12PlatformStore store,
        string accountId)
    {
        var profile = Profile(store, accountId);
        return (
            (int)typeof(L12PlatformStore).GetMethod("FactionRank", PrivateInstance)!
                .Invoke(store, new[] { profile })!,
            (int)typeof(L12PlatformStore).GetMethod("OverallRank", PrivateInstance)!
                .Invoke(store, new[] { profile })!);
    }

    internal static (int Faction, int Overall, int SourceOrder) CommittedRanks(
        L12PlatformStore store, string accountId)
    {
        var identity = RankedProjection(store);
        var profiles = identity.GetType().GetProperty("Profiles")!.GetValue(identity)!;
        var values = (IEnumerable)profiles.GetType().GetProperty("Values")!.GetValue(profiles)!;
        var profile = values.Cast<object>().Single(item =>
            (string)item.GetType().GetProperty("AccountId")!.GetValue(item)! == accountId);
        return (
            (int)typeof(L12PlatformStore).GetMethod("CommittedFactionRank", PrivateStatic)!
                .Invoke(null, new[] { identity, profile })!,
            (int)typeof(L12PlatformStore).GetMethod("CommittedOverallRank", PrivateStatic)!
                .Invoke(null, new[] { identity, profile })!,
            (int)profile.GetType().GetProperty("SourceOrder")!.GetValue(profile)!);
    }

    internal static void SetAccountUsername(L12PlatformStore store, string accountId,
        string username)
    {
        var account = Accounts(store).Cast<object>().Single(item =>
            (string)item.GetType().GetProperty("Id")!.GetValue(item)! == accountId);
        account.GetType().GetProperty("Username")!.SetValue(account, username);
        account.GetType().GetProperty("MustChangeUsername")!.SetValue(account, true);
    }

    internal static void SetProfile(L12PlatformStore store, string accountId, string seasonId,
        string? faction, int sevenValue, int placementPlayed, double hiddenRating = 1500,
        string? selectedMasterTitle = null)
    {
        var profiles = Profiles(store);
        var row = profiles.Cast<object>().FirstOrDefault(item =>
            (string)item.GetType().GetProperty("AccountId")!.GetValue(item)! == accountId);
        if (row is null)
        {
            row = Activator.CreateInstance(profiles.GetType().GetGenericArguments()[0], nonPublic: true)!;
            row.GetType().GetProperty("AccountId")!.SetValue(row, accountId);
            profiles.Add(row);
        }
        row.GetType().GetProperty("SeasonId")!.SetValue(row, seasonId);
        row.GetType().GetProperty("Faction")!.SetValue(row, faction);
        row.GetType().GetProperty("SevenValue")!.SetValue(row, sevenValue);
        row.GetType().GetProperty("PlacementPlayed")!.SetValue(row, placementPlayed);
        row.GetType().GetProperty("HiddenRating")!.SetValue(row, hiddenRating);
        row.GetType().GetProperty("SelectedMasterTitle")!.SetValue(row, selectedMasterTitle);
    }

    internal static void RequestSave(L12PlatformStore store)
        => typeof(L12PlatformStore).GetMethod("Save", PrivateInstance)!
            .Invoke(store, new object[] { true });

    internal static void RemoveRankedProjection(L12PlatformStore store)
    {
        var current = CommittedProjection(store);
        var type = current.GetType();
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic)
            .Single(item => item.GetParameters().Length == 6);
        var damaged = constructor.Invoke(new[]
        {
            type.GetProperty("Revision")!.GetValue(current),
            type.GetProperty("RollbackSnapshot")!.GetValue(current),
            type.GetProperty("Sessions")!.GetValue(current),
            type.GetProperty("Available")!.GetValue(current),
            type.GetProperty("DeploymentReadiness")!.GetValue(current),
            null,
        });
        typeof(L12PlatformStore).GetField("_committedSessionActivity", PrivateInstance)!
            .SetValue(store, damaged);
    }

    internal static string CompatibilityVariant(string value)
        => "  " + string.Concat(value.ToLowerInvariant().Select(character =>
            character is >= '!' and <= '~' ? (char)(character + 0xfee0) : character)) + "  ";

    private static IList Profiles(L12PlatformStore store)
    {
        var data = Data(store);
        return (IList)data.GetType().GetProperty("RankedProfiles")!.GetValue(data)!;
    }

    private static IList Accounts(L12PlatformStore store)
    {
        var data = Data(store);
        return (IList)data.GetType().GetProperty("Accounts")!.GetValue(data)!;
    }

    private static object Data(L12PlatformStore store)
        => typeof(L12PlatformStore).GetProperty("_data", PrivateInstance)!.GetValue(store)!;

    private static object Profile(L12PlatformStore store, string accountId)
        => Profiles(store).Cast<object>().Single(item =>
            (string)item.GetType().GetProperty("AccountId")!.GetValue(item)! == accountId);
}
