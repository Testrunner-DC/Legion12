using Microsoft.Data.Sqlite;
using System.Net;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RankReachedRuleBackfillTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(System.IO.Path.Combine(
        AppContext.BaseDirectory, "TwelveLegions", "Data"));

    [Fact]
    public void SavingActiveRuleBackfillsEveryQualifiedPlayerBeyondTheLeaderboardLimit()
    {
        using var f = new Fixture();
        var players = Enumerable.Range(0, 55).Select(_ => f.Player(placement: 10)).ToArray();
        Assert.Equal(50, f.Store.RankedLeaderboard().Count);
        f.Store.SaveAlternateArtAwardRule(f.Admin, f.Draft());
        var grants = f.Store.AlternateArtGrants();
        Assert.Equal(players.Length, grants.Count);
        Assert.Equal(players.Select(row => row.Id).Order(), grants.Select(row => row.AccountId).Order());
        Assert.All(grants, row => Assert.Equal(f.Season, row.SourceReference));
        Assert.All(players, player => Assert.Single(f.Store.PendingAlternateArtGrantNotifications(player.Id)));
        Assert.Equal(players.Length, f.Reopen().AlternateArtGrants().Count);
    }

    [Fact]
    public void FirstSettledGameQualifiesWithoutWaitingForPlacementCompletion()
    {
        using var f = new Fixture();
        var first = f.Player(placement: 0);
        var second = f.Player(placement: 0);
        f.Store.SettleRankedMatch("backfill-before-rule", first.Id, second.Id, 0);
        Assert.Empty(f.Store.RankedLeaderboard());
        f.Store.SaveAlternateArtAwardRule(f.Admin, f.Draft());
        Assert.Equal(2, f.Store.AlternateArtGrants().Count);
        Assert.Single(f.Store.PendingAlternateArtGrantNotifications(first.Id));
        Assert.Single(f.Store.PendingAlternateArtGrantNotifications(second.Id));
    }

    [Fact]
    public void SelectingFactionAloneIsNotACompletedRankedParticipationFact()
    {
        using var f = new Fixture();
        var first = f.Player(placement: 0);
        var second = f.Player(placement: 0);
        f.Store.SaveAlternateArtAwardRule(f.Admin, f.Draft());
        Assert.Empty(f.Store.AlternateArtGrants());
        f.Store.SettleRankedMatch("backfill-future-game", first.Id, second.Id, 0);
        Assert.Equal(2, f.Store.AlternateArtGrants().Count);
    }

    [Fact]
    public void RepeatedSavesReactivationAndFutureSettlementKeepTheCanonicalGrantKeys()
    {
        using var f = new Fixture();
        var first = f.Player();
        var second = f.Player();
        var draft = f.Draft(season: "  " + f.Season.ToLowerInvariant() + "  ");
        var rule = f.Store.SaveAlternateArtAwardRule(f.Admin, draft);
        var ids = f.Store.AlternateArtGrants().Select(row => row.Id).Order().ToArray();
        Assert.Equal(2, ids.Length);
        f.Store.SaveAlternateArtAwardRule(f.Admin, draft with { Id = rule.Id });
        f.Store.SaveAlternateArtAwardRule(f.Admin, draft with { Id = rule.Id, Active = false });
        f.Store.SaveAlternateArtAwardRule(f.Admin, draft with { Id = rule.Id, Active = true });
        f.Store.SaveAlternateArtAwardRule(f.Admin, draft with { Id = null });
        f.Store.SettleRankedMatch("backfill-after-save", first.Id, second.Id, 0);
        Assert.Equal(ids, f.Store.AlternateArtGrants().Select(row => row.Id).Order());
        Assert.All(f.Store.AlternateArtGrants(), row =>
        {
            Assert.Equal("rank-reached", row.SourceKind);
            Assert.Equal(f.Season, row.SourceReference);
        });
        Assert.Single(f.Store.PendingAlternateArtGrantNotifications(first.Id));
        Assert.Single(f.Store.PendingAlternateArtGrantNotifications(second.Id));
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("below-tier")]
    [InlineData("wrong-season")]
    [InlineData("event")]
    [InlineData("season-final")]
    [InlineData("master-champion-season-final")]
    public void SavingOtherConditionsDoesNotExpandImmediateAwards(string condition)
    {
        using var f = new Fixture();
        var player = f.Player();
        var draft = condition switch
        {
            "inactive" => f.Draft(active: false),
            "below-tier" => f.Draft(minimum: 4),
            "wrong-season" => f.Draft(season: "OTHER-SEASON"),
            _ => f.Draft(kind: condition),
        };
        var saved = f.Store.SaveAlternateArtAwardRule(f.Admin, draft);
        Assert.Empty(f.Store.AlternateArtGrants());
        Assert.Empty(f.Store.PendingAlternateArtGrantNotifications(player.Id));
        if (condition == "inactive")
        {
            f.Store.SaveAlternateArtAwardRule(f.Admin, draft with { Id = saved.Id, Active = true });
            Assert.Single(f.Store.AlternateArtGrants());
        }
    }

    [Fact]
    public void DisabledDeletedWrongSeasonAndZeroParticipationProfilesAreNotRecipients()
    {
        using var f = new Fixture();
        var eligible = f.Player();
        var disabled = f.Player();
        var deleted = f.Player();
        var oldSeason = f.Player(season: "OTHER-SEASON");
        var notPlayed = f.Player(placement: 0);
        f.Store.SetAccountDisabled(f.Admin, disabled.Id, true, "synthetic backfill", new("backfill-disable"), true);
        f.Store.DeleteAccountPersonalData(f.Admin, deleted.Id, "synthetic backfill", new("backfill-delete"), true);
        f.Store.SaveAlternateArtAwardRule(f.Admin, f.Draft());
        Assert.Equal(eligible.Id, Assert.Single(f.Store.AlternateArtGrants()).AccountId);
        foreach (var player in new[] { disabled, deleted, oldSeason, notPlayed })
            Assert.Empty(f.Store.PendingAlternateArtGrantNotifications(player.Id));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PartialGrantAndCommitFailuresRollBackCreationOrUpdateCompletely(bool update, bool commitFailure)
    {
        using var f = new Fixture();
        var players = Enumerable.Range(0, 3).Select(_ => f.Player()).ToArray();
        var old = update ? f.Store.SaveAlternateArtAwardRule(f.Admin, f.Draft(minimum: 4)) : null;
        var rules = f.Store.AlternateArtAwardRules().ToArray();
        var auditIds = f.Store.AdminAudit().Select(row => row.Id).Order().ToArray();
        var granted = 0;
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage == "after-rank-reached-rule-backfill-grant")
            {
                granted++;
                if (!commitFailure && granted == 2) throw new IOException("synthetic partial backfill");
            }
            if (commitFailure && stage == "before-commit") throw new IOException("synthetic commit failure");
        };
        var error = Record.Exception(() => f.Store.SaveAlternateArtAwardRule(f.Admin, f.Draft(id: old?.Id)));
        if (commitFailure) Assert.IsType<L12PlatformStorageUnavailableException>(error);
        else Assert.IsType<IOException>(error);
        Assert.Equal(commitFailure ? 3 : 2, granted);
        Assert.Equal(rules, f.Store.AlternateArtAwardRules());
        Assert.Empty(f.Store.AlternateArtGrants());
        Assert.All(players, player => Assert.Empty(f.Store.PendingAlternateArtGrantNotifications(player.Id)));
        Assert.Equal(auditIds, f.Store.AdminAudit().Select(row => row.Id).Order());
        f.Store.StorageFailureInjector = null;
        f.Store.UpdateAudioPreferences(f.Admin.Id, new());
        var reopened = f.Reopen();
        Assert.Equal(rules, reopened.AlternateArtAwardRules());
        Assert.Empty(reopened.AlternateArtGrants());
        Assert.All(players, player => Assert.Empty(reopened.PendingAlternateArtGrantNotifications(player.Id)));
        Assert.Equal(auditIds, reopened.AdminAudit().Select(row => row.Id).Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictingStoreCommitSurvivesWithoutAnyPartialBackfill(bool refreshFailure)
    {
        using var f = new Fixture();
        var player = f.Player();
        var other = f.Reopen();
        var advanced = false;
        f.Store.StorageFailureInjector = stage =>
        {
            if (stage == "after-rank-reached-rule-backfill-grant" && !advanced)
            {
                advanced = true;
                other.UpdateAudioPreferences(f.Admin.Id, new(MusicEnabled: false));
            }
            if (stage == "before-conflict-refresh" && refreshFailure)
                throw new IOException("synthetic conflict refresh failure");
        };
        var error = Record.Exception(() => f.Store.SaveAlternateArtAwardRule(f.Admin, f.Draft()));
        if (refreshFailure) Assert.IsType<L12PlatformStorageRefreshException>(error);
        else Assert.IsType<L12PlatformStorageConflictException>(error);
        Assert.True(advanced);
        var latest = f.Reopen();
        Assert.False(latest.Account(f.Admin.Id)!.AudioPreferences!.MusicEnabled);
        Assert.Empty(latest.AlternateArtAwardRules());
        Assert.Empty(latest.AlternateArtGrants());
        Assert.Empty(latest.PendingAlternateArtGrantNotifications(player.Id));
        Assert.Empty(latest.AdminAudit(category: "alternate-art-award-rule"));
        if (!refreshFailure)
        {
            Assert.False(f.Store.Account(f.Admin.Id)!.AudioPreferences!.MusicEnabled);
            Assert.Empty(f.Store.AlternateArtAwardRules());
            Assert.Empty(f.Store.AlternateArtGrants());
            Assert.Empty(f.Store.PendingAlternateArtGrantNotifications(player.Id));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedDefinitionsAreReadOnceAndUnavailabilityCannotProducePartialAwards(bool unavailable)
    {
        using var f = new Fixture();
        var players = Enumerable.Range(0, 3).Select(_ => f.Player()).ToArray();
        var art = new L12AlternateArtView("shared-backfill", "SHARED-BACKFILL", Catalog.DeckAt(0).CardIds[0], "共享异画",
            "media", "/api/site/media/media/desktop/" + new string('a', 64) + ".webp",
            "/api/site/media/media/thumbnail/" + new string('b', 64) + ".webp", true,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var transport = new GalleryTransport(art);
        var clock = new CountingClock();
        await using var source = new L12SharedGalleryCatalog(transport, clock);
        Assert.True(await source.RefreshAsync());
        var store = f.Reopen(source);
        if (unavailable)
        {
            transport.Available = false;
            Assert.False(await source.RefreshAsync());
        }
        clock.Reads = 0;
        var draft = f.Draft() with { AlternateArtId = art.Id };
        if (unavailable)
        {
            Assert.Throws<L12PlatformStorageUnavailableException>(() => store.SaveAlternateArtAwardRule(f.Admin, draft));
            Assert.Empty(store.AlternateArtAwardRules());
            Assert.Empty(store.AlternateArtGrants());
            Assert.Empty(store.AdminAudit(category: "alternate-art-award-rule"));
        }
        else
        {
            store.SaveAlternateArtAwardRule(f.Admin, draft);
            Assert.Equal(1, clock.Reads);
            Assert.Equal(players.Length, store.AlternateArtGrants().Count);
        }
        Assert.Equal(unavailable ? 2 : 1, transport.Requests);
    }

    private sealed class CountingClock : TimeProvider
    {
        internal int Reads;
        public override long GetTimestamp() { Reads++; return DateTimeOffset.UtcNow.UtcTicks; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private sealed class GalleryTransport(L12AlternateArtView art) : HttpMessageHandler
    {
        internal bool Available = true;
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(!Available ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    JsonSerializer.Serialize(new[] { art }, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ab-" + Guid.NewGuid().ToString("N"));
        private int _next;
        internal string Path => System.IO.Path.Combine(_root, "platform.json");
        internal L12PlatformStore Store { get; }
        internal L12AccountView Admin { get; }
        internal string Season { get; }
        internal string ArtId { get; }
        internal Fixture()
        {
            Store = Reopen();
            Admin = Store.Login("Admin", "L12master").Account!;
            Season = Store.OperationsConfig(Admin).Config.Season.Id;
            ArtId = Store.AlternateArts().First(row => row.Active && row.Kind == "alternate-art").Id;
        }
        internal L12PlatformStore Reopen(L12SharedGalleryCatalog? source = null) => new(Path, Catalog.PresetDecks,
            officialCards: Catalog.Cards, officialAlternateArts: Catalog.OfficialAlternateArts, sharedGallery: source);
        internal L12AccountView Player(int placement = 1, int seven = 0, string? season = null)
        {
            var player = Store.Register("back" + ++_next, "Password123!").Account!;
            Store.SelectRankedFaction(player.Id, "order");
            Store.ExecuteAdminTransaction(() =>
            {
                RankedIdentityTestState.SetProfile(Store, player.Id, season ?? Season, "order", seven, placement);
                RankedIdentityTestState.RequestSave(Store);
                return true;
            });
            return player;
        }
        internal L12AlternateArtAwardRuleDraft Draft(bool active = true, int minimum = 0,
            string? id = null, string kind = "rank-reached", string? season = null)
            => new(id, ArtId, kind, season ?? Season, kind == "event" ? "test-event" : "", minimum, active);
        public void Dispose()
        {
            Store.StorageFailureInjector = null;
            PlatformTestDirectoryCleanup.Retry(() => Directory.Delete(_root, true),
                OperatingSystem.IsWindows(), Thread.Sleep, SqliteConnection.ClearAllPools);
        }
    }
}
