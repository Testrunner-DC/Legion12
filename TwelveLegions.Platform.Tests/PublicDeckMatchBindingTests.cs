using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using System.Text.Json;
using System.Net.Http.Json;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckMatchBindingTests
{
    [Fact]
    public async Task ExplicitVersionsSurviveUpdateRestartAndReplayCleanupWithoutChangingStatistics()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-deck-binding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks, officialCards: catalog.Cards);
        var owner = store.Register("bindowner", "password-123").Account!;
        var other = store.Register("bindother", "password-123").Account!;
        var deck = catalog.PresetDecks[0];
        var publication = store.PublishDeck(owner.Id, deck, null)!;
        Assert.Equal(publication.Id, publication.Deck.PublicationId);
        Assert.Equal(1, publication.Deck.PublicationVersion);
        var second = store.PublishDeck(other.Id, deck, null)!;
        second = store.PublishDeck(other.Id, catalog.PresetDecks[1], second.Id)!;
        Assert.Equal(2, second.Deck.PublicationVersion);
        var binding = store.ResolvePublicDeckBinding(owner.Id, deck, publication.Id, 1);
        Assert.NotNull(binding);
        Assert.Null(store.ResolvePublicDeckBinding(owner.Id, deck, null, null));
        Assert.Null(store.ResolvePublicDeckBinding(other.Id, deck, publication.Id, 1));
        Assert.Null(store.ResolvePublicDeckBinding(owner.Id, catalog.PresetDecks[1], publication.Id, 1));
        Assert.Null(store.ResolvePublicDeckBinding(owner.Id, deck, publication.Id, 999));
        var now = DateTimeOffset.UtcNow;
        var path = Path.Combine(root, "matches.db");
        await using (var recorder = new MatchRecorder(path, () => now))
        {
            await recorder.InitializeAsync();
            var engine = new L12GameEngine(catalog, "bound-match", "BIND", 42, ["甲", "乙"],
                [deck, catalog.PresetDecks[1]], skipPreparation: true);
            await recorder.StartAsync(engine, "friendly", owner.Id, other.Id, [deck, catalog.PresetDecks[1]],
                [binding, store.ResolvePublicDeckBinding(other.Id, catalog.PresetDecks[1], second.Id, 2)]);
            store.PublishDeck(owner.Id, catalog.PresetDecks[1], publication.Id);
            var beforeCompletion = await recorder.PublicDeckVersionStatisticsAsync(publication.Id);
            Assert.Equal("empty", beforeCompletion.SampleStatus);
            Assert.Equal(0, beforeCompletion.Games);
            engine.State.Phase = L12Phase.GameOver;
            engine.State.Winner = 0;
            await recorder.CompleteAsync(engine);
            var smallSample = await recorder.PublicDeckVersionStatisticsAsync(publication.Id);
            Assert.Equal("insufficient", smallSample.SampleStatus);
            Assert.Equal(0, smallSample.Games);
            Assert.Empty(smallSample.Groups);
            for (var index = 2; index <= 3; index++)
            {
                var additional = new L12GameEngine(catalog, $"bound-match-{index}", "BIND", 42, ["甲", "乙"],
                    [deck, catalog.PresetDecks[1]], skipPreparation: true);
                await recorder.StartAsync(additional, "friendly", owner.Id, other.Id,
                    [deck, catalog.PresetDecks[1]],
                    [binding, store.ResolvePublicDeckBinding(other.Id, catalog.PresetDecks[1], second.Id, 2)]);
                additional.State.Phase = L12Phase.GameOver;
                additional.State.Winner = 0;
                await recorder.CompleteAsync(additional);
            }
            var statistics = await recorder.PublicDeckVersionStatisticsAsync(publication.Id);
            Assert.Equal("available", statistics.SampleStatus);
            var match = Assert.Single(statistics.Groups);
            Assert.Equal(1, match.Version);
            Assert.Equal(deck.MasterId, match.MasterId);
            Assert.Equal(catalog.PresetDecks[1].MasterId, match.OpponentMasterId);
            Assert.Equal((3, 3, 0, 0, 1d),
                (match.Games, match.Wins, match.Losses, match.Draws, match.WinRate));
            var opponent = Assert.Single((await recorder.PublicDeckVersionStatisticsAsync(second.Id)).Groups);
            Assert.Equal(2, opponent.Version);
            Assert.Equal((3, 0, 3, 0, 0d),
                (opponent.Games, opponent.Wins, opponent.Losses, opponent.Draws, opponent.WinRate));
            Assert.Equal(0, (await recorder.PublicDeckVersionStatisticsAsync(publication.Id,
                ["bound-match"])).Games);
            Assert.Equal(0, (await recorder.PublicDeckVersionStatisticsAsync(publication.Id,
                excludedAccountIds: [other.Id])).Games);
            Assert.Equal(3, (await recorder.PlayerStatisticsAsync(owner.Id, owner.Username)).Overall.Games);
            now = now.AddDays(9);
            await recorder.RunPlayerReplayCleanupIfDueAsync(utcNow: now);
        }
        await using (var restored = new MatchRecorder(path, () => now))
        {
            await restored.InitializeAsync();
            var match = Assert.Single((await restored.PublicDeckVersionStatisticsAsync(publication.Id)).Groups);
            Assert.Equal(1, match.Version);
            Assert.Equal(3, (await restored.PlayerStatisticsAsync(owner.Id, owner.Username)).Overall.Games);
            now = now.AddDays(82);
            var expired = await restored.PublicDeckVersionStatisticsAsync(publication.Id);
            Assert.Equal(90, expired.RecentDays);
            Assert.Equal("empty", expired.SampleStatus);
            Assert.Equal(0, expired.Games);
            Assert.Empty(expired.Groups);
        }
    }

    [Theory]
    [InlineData("friendly")]
    [InlineData("casual")]
    [InlineData("ranked")]
    public async Task RealRoomStartLocksExplicitVersionEvenWhenPublicationChangesWhileQueued(string mode)
    {
        var (root, catalog, store) = Setup();
        var first = store.Register("bindfirst", "password-123").Account!;
        var second = store.Register("bindsecond", "password-123").Account!;
        store.SelectRankedFaction(first.Id, "order");
        store.SelectRankedFaction(second.Id, "chaos");
        var deck = catalog.PresetDecks[0];
        var published = store.PublishDeck(first.Id, deck, null)!;
        var submission = new L12CustomDeckSubmission { Name = deck.Name, MasterId = deck.MasterId,
            CardIds = deck.CardIds, MoraleIds = deck.MoraleIds, SpecialIds = deck.SpecialIds,
            PublicationId = published.Id, PublicationVersion = 1 };
        await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, store);
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        await manager.ConnectAsync(a, first.Id, first.Username);
        await manager.ConnectAsync(b, second.Id, second.Username);
        IReadOnlyList<OutgoingMessage> started;
        if (mode == "friendly")
        {
            var room = JsonSerializer.SerializeToElement(manager.CreateRoom(a)[0].Payload)
                .GetProperty("roomCode").GetString();
            manager.JoinRoom(b, room);
            manager.SelectCustomDeck(a, submission);
            store.PublishDeck(first.Id, catalog.PresetDecks[1], published.Id);
            await manager.SetReadyAsync(a, true);
            started = await manager.SetReadyAsync(b, true);
        }
        else
        {
            await manager.JoinMatchmakingAsync(a, mode, submission);
            store.PublishDeck(first.Id, catalog.PresetDecks[1], published.Id);
            started = await manager.JoinMatchmakingAsync(b, mode, null);
        }
        Assert.Contains(started, item => JsonSerializer.SerializeToElement(item.Payload).GetProperty("type").GetString() == "gameState");
        using var connection = new SqliteConnection($"Data Source={Path.Combine(root, "matches.db")}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM match_public_deck_bindings;";
        Assert.Equal(1L, command.ExecuteScalar());
        command.CommandText = "SELECT count(*) FROM match_public_deck_bindings;";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public void AccountProvenancePersistsButCopiedUnpublishedAndChangedDecksDoNotAcquireIt()
    {
        var (root, catalog, store) = Setup();
        var owner = store.Register("bindsave", "password-123").Account!;
        var deck = catalog.PresetDecks[0];
        var publication = store.PublishDeck(owner.Id, deck, null)!;
        var explicitDeck = WithBinding(deck, publication.Id, 1);
        Assert.Equal(publication.Id, store.UpsertDeck(owner.Id, explicitDeck).PublicationId);
        var restored = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks, officialCards: catalog.Cards);
        Assert.Equal(1, Assert.Single(restored.Decks(owner.Id), item => item.PublicationId == publication.Id).PublicationVersion);
        // No metadata means no provenance, even for identical contents, owner, and name.
        Assert.Null(restored.UpsertDeck(owner.Id, deck).PublicationId);
        Assert.Null(restored.UpsertDeck(owner.Id, WithBinding(catalog.PresetDecks[1], publication.Id, 1)).PublicationId);
        Assert.Null(restored.ResolvePublicDeckBinding(owner.Id, deck, null, null));
    }

    [Fact]
    public async Task LegacyLaterPublishedAndTournamentSnapshotsRemainUnboundAndErrorsAreExcluded()
    {
        var (root, catalog, store) = Setup();
        var owner = store.Register("bindold", "password-123").Account!;
        var deck = catalog.PresetDecks[0];
        await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
        await recorder.InitializeAsync();
        foreach (var mode in new[] { "friendly", "tournament" })
        {
            var game = new L12GameEngine(catalog, mode, "OLD", 42, ["甲", "乙"], [deck, deck], skipPreparation: true);
            await recorder.StartAsync(game, mode, owner.Id, "opponent", [deck, deck]);
            game.State.Phase = L12Phase.GameOver;
            await recorder.CompleteAsync(game);
        }
        var publication = store.PublishDeck(owner.Id, deck, null)!;
        // Simulate the old database without the new table: additive initialization never backfills guesses.
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(root, "matches.db")}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE match_public_deck_bindings;"; command.ExecuteNonQuery();
        }
        await recorder.InitializeAsync();
        Assert.Equal(0, (await recorder.PublicDeckVersionStatisticsAsync(publication.Id)).Games);
        var invalid = new L12GameEngine(catalog, "invalid", "ERR", 42, ["甲", "乙"], [deck, deck], skipPreparation: true);
        await recorder.StartAsync(invalid, "friendly", owner.Id, "opponent", [deck, deck],
            [store.ResolvePublicDeckBinding(owner.Id, deck, publication.Id, 1), null]);
        invalid.State.Phase = L12Phase.GameOver;
        await recorder.CompleteAsync(invalid);
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(root, "matches.db")}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE matches SET error='invalidated' WHERE match_id='invalid';"; command.ExecuteNonQuery();
        }
        Assert.Equal(0, (await recorder.PublicDeckVersionStatisticsAsync(publication.Id)).Games);
        Assert.Equal(2, (await recorder.PlayerStatisticsAsync(owner.Id, owner.Username)).Overall.Games);
    }

    [Fact]
    public async Task DetailEndpointUsesLiveExclusionsAndContentSaveKeepsAnonymousStatistics()
    {
        var (root, catalog, store) = Setup();
        var owner = store.Register("bindapi", "password-123");
        var opponent = store.Register("bindopp", "password-123").Account!;
        var deck = catalog.PresetDecks[0];
        var published = store.PublishDeck(owner.Account!.Id, deck, null)!;
        await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
        await recorder.InitializeAsync();
        for (var index = 0; index < 3; index++)
        {
            var game = new L12GameEngine(catalog, $"api-bound-{index}", "API", 42,
                [owner.Account.Username, opponent.Username], [deck, deck], skipPreparation: true);
            await recorder.StartAsync(game, "friendly", owner.Account.Id, opponent.Id, [deck, deck],
                [store.ResolvePublicDeckBinding(owner.Account.Id, deck, published.Id, 1), null]);
            game.State.Phase = L12Phase.GameOver;
            game.State.Winner = index == 2 ? 1 : 0;
            await recorder.CompleteAsync(game);
        }
        var host = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
        await using var server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
        try
        {
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            var anonymousJson = await client.GetStringAsync($"/api/public-decks/{published.Id}");
            var anonymous = JsonSerializer.Deserialize<L12PublishedDeckView>(anonymousJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(3, anonymous!.Details!.MatchStatistics.Games);
            using (var document = JsonDocument.Parse(anonymousJson))
                AssertPublicStatisticsAreAnonymous(document.RootElement.GetProperty("details")
                    .GetProperty("matchStatistics").GetRawText());
            client.DefaultRequestHeaders.Authorization = new("Bearer", owner.Token);
            using var update = await client.PutAsJsonAsync($"/api/public-decks/{published.Id}/content",
                new L12PublicDeckContentInput(new("指南", "", "", "", ""), []));
            update.EnsureSuccessStatusCode();
            var updatedJson = await update.Content.ReadAsStringAsync();
            Assert.Equal(3, JsonSerializer.Deserialize<L12PublicDeckDetailsView>(updatedJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!.MatchStatistics.Games);
            using (var document = JsonDocument.Parse(updatedJson))
                AssertPublicStatisticsAreAnonymous(document.RootElement.GetProperty("matchStatistics").GetRawText());
            var admin = store.Login("Admin", "L12master").Account!;
            store.SetAccountDisabled(admin, opponent.Id, true, "测试统计排除", new("disable-bind"), true);
            Assert.Equal(0, (await client.GetFromJsonAsync<L12PublishedDeckView>(
                $"/api/public-decks/{published.Id}"))!.Details!.MatchStatistics.Games);
            store.SetAccountDisabled(admin, opponent.Id, false, "测试恢复统计", new("restore-bind"), true);
            Assert.Equal(3, (await client.GetFromJsonAsync<L12PublishedDeckView>(
                $"/api/public-decks/{published.Id}"))!.Details!.MatchStatistics.Games);
        }
        finally { await server.StopAsync(); Environment.SetEnvironmentVariable("L12_LISTEN_HOST", host); }
    }

    private static (string Root, L12Catalog Catalog, L12PlatformStore Store) Setup()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-deck-binding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        return (root, catalog, new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks, officialCards: catalog.Cards));
    }

    [Fact]
    public async Task StartFailureRollsBackBindingsWithMatchAndCheckpoint()
    {
        var (root, catalog, store) = Setup();
        var owner = store.Register("bindatomic", "password-123").Account!;
        var deck = catalog.PresetDecks[0];
        var publication = store.PublishDeck(owner.Id, deck, null)!;
        var path = Path.Combine(root, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        var game = new L12GameEngine(catalog, "atomic", "TX", 42, ["甲", "乙"], [deck, deck],
            skipPreparation: true, stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion);
        recorder.StorageFailureInjector = _ => throw new IOException("test-before-start-commit");
        await Assert.ThrowsAsync<IOException>(() => recorder.StartAsync(game, "friendly", owner.Id, "opponent", [deck, deck],
            [store.ResolvePublicDeckBinding(owner.Id, deck, publication.Id, 1), null]));
        using var connection = new SqliteConnection($"Data Source={path}"); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT count(*) FROM matches)+(SELECT count(*) FROM match_public_deck_bindings);";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public async Task RankedIntegrityHoldUsesTheSameAuthoritativeExclusionSet()
    {
        var (root, catalog, store) = Setup();
        var owner = store.Register("bindholda", "password-123").Account!;
        var opponent = store.Register("bindholdb", "password-123").Account!;
        store.SelectRankedFaction(owner.Id, "order"); store.SelectRankedFaction(opponent.Id, "chaos");
        var deck = catalog.PresetDecks[0];
        var publication = store.PublishDeck(owner.Id, deck, null)!;
        await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
        await recorder.InitializeAsync();
        for (var i = 0; i < 3; i++)
        {
            var id = $"held-bind-{i}";
            var game = new L12GameEngine(catalog, id, "HELD", 42, [owner.Username, opponent.Username], [deck, deck], skipPreparation: true);
            await recorder.StartAsync(game, "ranked", owner.Id, opponent.Id, [deck, deck],
                [store.ResolvePublicDeckBinding(owner.Id, deck, publication.Id, 1), null]);
            game.State.Phase = L12Phase.GameOver; game.State.Winner = 0;
            await recorder.CompleteAsync(game);
            var ended = DateTimeOffset.UtcNow.AddMinutes(-3 + i);
            store.SettleRankedMatch(id, owner.Id, opponent.Id, 0, integrity: new L12RankedIntegrityContext(
                ended.AddSeconds(-45), ended, 0, "surrender", null, null, 1));
        }
        var excluded = store.RankedIntegrityExcludedMatchIds();
        Assert.Contains("held-bind-2", excluded);
        Assert.Equal(3, (await recorder.PublicDeckVersionStatisticsAsync(publication.Id)).Games);
        var statistics = await recorder.PublicDeckVersionStatisticsAsync(publication.Id, excluded,
            store.StatisticsExcludedAccountIds());
        Assert.Equal("insufficient", statistics.SampleStatus);
        Assert.Equal(0, statistics.Games);
        Assert.Empty(statistics.Groups);
        Assert.Equal(2, (await recorder.PlayerStatisticsAsync(owner.Id, owner.Username, excluded)).Overall.Games);
    }

    private static L12PresetDeckDefinition WithBinding(L12PresetDeckDefinition deck, string id, int version)
        => new() { Name = deck.Name, MasterId = deck.MasterId, CardIds = deck.CardIds, MoraleIds = deck.MoraleIds,
            SpecialIds = deck.SpecialIds, PublicationId = id, PublicationVersion = version };

    private static void AssertPublicStatisticsAreAnonymous(string json)
    {
        using var document = JsonDocument.Parse(json);
        var names = new List<string>();
        Collect(document.RootElement);
        foreach (var forbidden in new[] { "matchId", "playedAt", "replayPath", "accountId", "username",
                     "displayName", "playerId", "playerName", "ownerId", "author" })
            Assert.DoesNotContain(names, name => string.Equals(name, forbidden, StringComparison.OrdinalIgnoreCase));

        void Collect(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject())
                {
                    names.Add(property.Name);
                    Collect(property.Value);
                }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) Collect(item);
        }
    }

    [Fact]
    public async Task StatisticsSeparateMultipleOpponentMastersWithoutExposingSmallGroups()
    {
        var (root, catalog, store) = Setup();
        var owner = store.Register("bindmulti", "password-123").Account!;
        var opponent = store.Register("bindmopp", "password-123").Account!;
        var deck = catalog.PresetDecks[0];
        var opponentMasterIds = catalog.Cards.Values.Where(card => card.CardType == "master"
                && card.Id != deck.MasterId).Select(card => card.Id).Take(2).ToArray();
        Assert.Equal(2, opponentMasterIds.Length);
        var publication = store.PublishDeck(owner.Id, deck, null)!;
        await using var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
        await recorder.InitializeAsync();
        for (var group = 0; group < opponentMasterIds.Length; group++)
            for (var index = 0; index < 3; index++)
            {
                var game = new L12GameEngine(catalog, $"multi-{group}-{index}", "MULTI", 42,
                    [owner.Username, opponent.Username], [deck, deck], skipPreparation: true);
                await recorder.StartAsync(game, "friendly", owner.Id, opponent.Id, [deck, deck],
                    [store.ResolvePublicDeckBinding(owner.Id, deck, publication.Id, 1), null]);
                game.State.Phase = L12Phase.GameOver;
                game.State.Winner = index <= group ? 0 : 1;
                await recorder.CompleteAsync(game);
                using var connection = new SqliteConnection($"Data Source={Path.Combine(root, "matches.db")}");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE match_participants SET master_id=$master WHERE match_id=$match AND player_index=1;";
                command.Parameters.AddWithValue("$master", opponentMasterIds[group]);
                command.Parameters.AddWithValue("$match", game.State.MatchId);
                command.ExecuteNonQuery();
            }
        var statistics = await recorder.PublicDeckVersionStatisticsAsync(publication.Id);
        Assert.Equal("available", statistics.SampleStatus);
        Assert.Equal(6, statistics.Games);
        Assert.Equal(2, statistics.Groups.Count);
        Assert.Equal(opponentMasterIds.OrderBy(id => id),
            statistics.Groups.Select(item => item.OpponentMasterId).OrderBy(id => id));
        Assert.All(statistics.Groups, group => Assert.Equal(deck.MasterId, group.MasterId));
        Assert.Contains(statistics.Groups, group => group.Wins == 1 && group.Losses == 2
            && group.WinRate == 0.3333);
        Assert.Contains(statistics.Groups, group => group.Wins == 2 && group.Losses == 1
            && group.WinRate == 0.6667);
    }
}
