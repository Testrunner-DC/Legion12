using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class B2TournamentDeckIdentityTests
{
    [Fact]
    public async Task HttpPreCheckInUsesDeckIdAfterRenameAndOldNameReuse()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var login = store.Register("b2tourh01", "password-123");
            Assert.True(login.Success, login.Message);
            var actor = login.Account!;
            var originalPreset = catalog.PresetDecks[0];
            var replacementPreset = catalog.PresetDecks[Math.Min(1, catalog.PresetDecks.Count - 1)];
            var original = store.CreateDeck(actor.Id, Deck(originalPreset, "旧名称"));
            Assert.True(original.Success);
            var renamed = store.UpdateDeck(actor.Id, original.Deck!.Id, original.Deck.Revision,
                Deck(originalPreset, "已改名"));
            Assert.True(renamed.Success);
            Assert.True(store.CreateDeck(actor.Id, Deck(replacementPreset, "旧名称")).Success);
            var tournament = store.CreateTournament(actor, TournamentPayload(), Context("create"), true);

            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            using var request = Authorized(HttpMethod.Post, $"/api/tournaments/{tournament.Id}/pre-check-in",
                login.Token!, new TournamentPreCheckInRequest("旧名称", string.Empty, original.Deck.Id,
                    "b2-deck-id-pre-check-in", tournament.Version));
            using var response = await client.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {responseText}");

            var locked = store.Tournament(actor, tournament.Id)!.Participants
                .Single(item => item.AccountId == actor.Id).Deck!;
            Assert.Equal("已改名", locked.Name);
            Assert.Equal(original.Deck.MasterId, locked.MasterId);
            Assert.Equal(original.Deck.CardIds, locked.CardIds);
            Assert.Equal(original.Deck.MoraleIds, locked.MoraleIds);
            Assert.Equal(original.Deck.SpecialIds, locked.SpecialIds);
        }
        finally
        {
            if (server is not null) { await server.StopAsync(); await server.DisposeAsync(); }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DeletedDeckIdDoesNotFallBackToReusedName()
    {
        var root = TempRoot();
        try
        {
            var catalog = LoadCatalog();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var actor = store.Register("b2tourd01", "password-123").Account!;
            var original = store.CreateDeck(actor.Id, Deck(catalog.PresetDecks[0], "重用名称"));
            Assert.True(store.DeleteDeck(actor.Id, original.Deck!.Id, original.Deck.Revision).Success);
            var replacement = store.CreateDeck(actor.Id,
                Deck(catalog.PresetDecks[Math.Min(1, catalog.PresetDecks.Count - 1)], "重用名称"));
            Assert.True(replacement.Success);
            var tournament = store.CreateTournament(actor, TournamentPayload(), Context("create"), true);

            Assert.Throws<KeyNotFoundException>(() => store.PreCheckInTournament(actor, tournament.Id,
                new L12TournamentPreCheckInPayload("重用名称", string.Empty, original.Deck.Id),
                tournament.Version, Context("deleted-id"), true));

            var participant = store.Tournament(actor, tournament.Id)!.Participants
                .Single(item => item.AccountId == actor.Id);
            Assert.Null(participant.Deck);
            Assert.Null(participant.TournamentCheckedInAt);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CrossAccountDeckIdFailsWithoutFallingBackToOwnedName()
    {
        var root = TempRoot();
        try
        {
            var catalog = LoadCatalog();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var actor = store.Register("b2touro01", "password-123").Account!;
            var other = store.Register("b2tourx01", "password-123").Account!;
            Assert.True(store.CreateDeck(actor.Id, Deck(catalog.PresetDecks[0], "同名牌库")).Success);
            var foreign = store.CreateDeck(other.Id,
                Deck(catalog.PresetDecks[Math.Min(1, catalog.PresetDecks.Count - 1)], "同名牌库"));
            var tournament = store.CreateTournament(actor, TournamentPayload(), Context("create"), true);

            Assert.Throws<KeyNotFoundException>(() => store.PreCheckInTournament(actor, tournament.Id,
                new L12TournamentPreCheckInPayload("同名牌库", string.Empty, foreign.Deck!.Id),
                tournament.Version, Context("cross-account-id"), true));

            Assert.Null(store.Tournament(actor, tournament.Id)!.Participants
                .Single(item => item.AccountId == actor.Id).Deck);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LockedSnapshotSurvivesPrivateRenameDeleteRestartAndIdempotentRetry()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = LoadCatalog();
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var actor = store.Register("b2tours01", "password-123").Account!;
            var preset = catalog.PresetDecks[0];
            var personal = store.CreateDeck(actor.Id, Deck(preset, "锁定前"));
            var tournament = store.CreateTournament(actor, TournamentPayload(), Context("create"), true);
            tournament = store.PreCheckInTournament(actor, tournament.Id,
                new L12TournamentPreCheckInPayload("故意不同的名称", string.Empty, personal.Deck!.Id),
                tournament.Version, Context("lock"), true);
            var before = tournament.Participants.Single(item => item.AccountId == actor.Id).Deck!;

            var renamed = store.UpdateDeck(actor.Id, personal.Deck.Id, personal.Deck.Revision,
                Deck(preset, "锁定后改名"));
            Assert.True(renamed.Success);
            Assert.True(store.DeleteDeck(actor.Id, renamed.Deck!.Id, renamed.Deck.Revision).Success);

            var retry = store.PreCheckInTournament(actor, tournament.Id,
                new L12TournamentPreCheckInPayload("再次故意不同", "IGNORED", personal.Deck.Id),
                tournament.Version, Context("idempotent-retry"), true);
            Assert.Equal(tournament.Version, retry.Version);
            AssertSnapshotUnchanged(before,
                retry.Participants.Single(item => item.AccountId == actor.Id).Deck!);

            var restarted = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var afterRestart = restarted.Tournament(actor, tournament.Id)!.Participants
                .Single(item => item.AccountId == actor.Id).Deck!;
            AssertSnapshotUnchanged(before, afterRestart);
            Assert.DoesNotContain(restarted.Decks(actor.Id), item => item.Id == personal.Deck.Id);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void AssertSnapshotUnchanged(L12TournamentDeckSnapshotView expected,
        L12TournamentDeckSnapshotView actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Hash, actual.Hash);
        Assert.Equal(expected.MasterId, actual.MasterId);
        Assert.Equal(expected.CardIds.Order(StringComparer.Ordinal), actual.CardIds.Order(StringComparer.Ordinal));
        Assert.Equal(expected.MoraleIds.Order(StringComparer.Ordinal), actual.MoraleIds.Order(StringComparer.Ordinal));
        Assert.Equal(expected.SpecialIds.Order(StringComparer.Ordinal), actual.SpecialIds.Order(StringComparer.Ordinal));
        Assert.Equal(expected.SubmittedAt, actual.SubmittedAt);
        Assert.Equal(expected.LockedAt, actual.LockedAt);
    }

    private static L12PresetDeckDefinition Deck(L12PresetDeckDefinition source, string name) => new()
    {
        Name = name,
        MasterId = source.MasterId,
        CardIds = [.. source.CardIds],
        MoraleIds = [.. source.MoraleIds],
        SpecialIds = [.. source.SpecialIds],
        BenchIds = [.. source.BenchIds],
    };

    private static L12Catalog LoadCatalog()
        => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));

    private static L12TournamentCreatePayload TournamentPayload()
        => new("牌库 ID 锁定回归", "single", "public", 8, DateTimeOffset.UtcNow.AddHours(1),
            "S01/S02", "b2 tournament identity", "after", "season", string.Empty, 50, 5);

    private static L12AdminAuditContext Context(string correlationId)
        => new(correlationId, "tournaments.manage", RequestMethod: "TEST", RequestPath: "/test/tournaments");

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-b2-tournament-deck-id-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
