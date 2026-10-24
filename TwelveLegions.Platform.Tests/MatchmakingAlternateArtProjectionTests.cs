using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class MatchmakingAlternateArtProjectionTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task InitialQueuedPrivateDeckProjectsOwnedMasterArtToBothPlayerViews()
    {
        await using var fixture = await Fixture.CreateAsync();
        var owner = fixture.RegisterAndConnect("artown1");
        var opponent = fixture.RegisterAndConnect("artpeer1");
        var (art, preset) = GrantEligibleMasterArt(fixture, owner.Account);
        var selected = fixture.Store.CreateDeck(owner.Account.Id,
            CopyDeck(preset, "首次匹配异画", art.Id));
        Assert.True(selected.Success, selected.Status);
        Assert.Equal(art.Id, selected.Deck!.AlternateArtSelections![art.BaseCardId]);

        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(owner.SessionId, "casual",
            Submission(selected.Deck)));
        var opponentDeck = fixture.Store.CreateDeck(opponent.Account.Id,
            CopyDeck(preset, "首次匹配原画", alternateArtId: null));
        Assert.True(opponentDeck.Success, opponentDeck.Status);
        var started = await fixture.Manager.JoinMatchmakingAsync(opponent.SessionId, "casual",
            Submission(opponentDeck.Deck!));
        AssertPlayerBaselinesStarted(started, owner, opponent);

        AssertMasterProjectionForBothViews(fixture, started, owner, opponent, art);
        fixture.AcceptCleanup();
    }

    [Fact]
    public async Task ReplacedAndPolledQueuedDeckUsesOwnedMasterArtAndRejectsUnownedSelection()
    {
        await using var fixture = await Fixture.CreateAsync();
        var owner = fixture.RegisterAndConnect("artown2");
        var opponent = fixture.RegisterAndConnect("artpeer2");
        var (art, preset) = GrantEligibleMasterArt(fixture, owner.Account);

        var original = fixture.Store.CreateDeck(owner.Account.Id,
            CopyDeck(preset, "公开匹配主宰外观", alternateArtId: null));
        Assert.True(original.Success, original.Status);
        Assert.Empty(original.Deck!.AlternateArtSelections!);
        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(owner.SessionId, "casual",
            Submission(original.Deck)));

        var selected = fixture.Store.UpdateDeck(owner.Account.Id, original.Deck.Id, original.Deck.Revision,
            CopyDeck(preset, original.Deck.Name, art.Id));
        Assert.True(selected.Success, selected.Status);
        Assert.Equal(art.Id, selected.Deck!.AlternateArtSelections![art.BaseCardId]);

        // This is the real public-match replacement and polling path. The queue captures the revised
        // private deck while the connected session still has its prior preset/custom-deck projection.
        AssertQueued(await fixture.Manager.JoinMatchmakingAsync(owner.SessionId, "casual",
            Submission(selected.Deck)));
        AssertQueued(await fixture.Manager.PollMatchmakingAsync(owner.SessionId));

        var opponentDeck = fixture.Store.CreateDeck(opponent.Account.Id,
            CopyDeck(preset, "公开匹配未授权外观", alternateArtId: null));
        Assert.True(opponentDeck.Success, opponentDeck.Status);
        var forgedOpponent = Submission(opponentDeck.Deck!, art.Id);
        var started = await fixture.Manager.JoinMatchmakingAsync(opponent.SessionId, "casual", forgedOpponent);
        AssertPlayerBaselinesStarted(started, owner, opponent);

        AssertMasterProjectionForBothViews(fixture, started, owner, opponent, art);
        fixture.AcceptCleanup();
    }

    private static (L12AlternateArtView Art, L12PresetDeckDefinition Preset) GrantEligibleMasterArt(
        Fixture fixture, L12AccountView owner)
    {
        var adminLogin = fixture.Store.Login("Admin", "L12master");
        Assert.True(adminLogin.Success, adminLogin.Message);
        var eligible = fixture.Store.AdminAlternateArts()
            .Where(item => item.Active && item.BuiltIn && !string.IsNullOrWhiteSpace(item.CardImageId))
            .Select(item => new
            {
                Art = item,
                Preset = fixture.Catalog.PresetDecks.FirstOrDefault(deck => deck.MasterId == item.BaseCardId),
            })
            .Where(item => item.Preset is not null)
            .ToArray();
        Assert.NotEmpty(eligible);
        var selected = eligible[0];
        Assert.Equal("master", fixture.Catalog.Cards[selected.Art.BaseCardId].CardType);
        var grant = fixture.Store.GrantAlternateArt(adminLogin.Account!,
            new L12AlternateArtGrantDraft(selected.Art.Id, owner.Id, "manual",
                "matchmaking-projection-regression"));
        Assert.Equal(owner.Id, grant.AccountId);
        Assert.Equal(selected.Art.Id, grant.AlternateArtId);
        Assert.Contains(fixture.Store.OwnedAlternateArts(owner.Id), item => item.Id == selected.Art.Id);
        return (selected.Art, selected.Preset!);
    }

    private static void AssertPlayerBaselinesStarted(IReadOnlyList<OutgoingMessage> started,
        Peer owner, Peer opponent)
    {
        foreach (var viewer in new[] { owner, opponent })
        {
            Assert.Contains(started, message => message.SessionId == viewer.SessionId
                && Payload(message).GetProperty("type").GetString() == "matchmakingFound");
            _ = PayloadFor(started, viewer.SessionId, "gameState");
        }
    }

    private static void AssertMasterProjectionForBothViews(Fixture fixture,
        IReadOnlyList<OutgoingMessage> started, Peer owner, Peer opponent, L12AlternateArtView art)
    {
        var expectedOwnedUrl = $"l12-card-id:{art.CardImageId}";
        var expectedOriginalUrl = fixture.Catalog.Cards[art.BaseCardId].ImageUrl;
        foreach (var viewer in new[] { owner, opponent })
        {
            var state = PayloadFor(started, viewer.SessionId, "gameState").GetProperty("state");
            var ownerMaster = MasterFor(state, owner.Account.Username);
            Assert.Equal(art.BaseCardId, ownerMaster.GetProperty("masterId").GetString());
            Assert.Equal(expectedOwnedUrl, ownerMaster.GetProperty("masterImageUrl").GetString());

            var opponentMaster = MasterFor(state, opponent.Account.Username);
            Assert.Equal(art.BaseCardId, opponentMaster.GetProperty("masterId").GetString());
            Assert.Equal(expectedOriginalUrl, opponentMaster.GetProperty("masterImageUrl").GetString());
        }
    }

    private static L12PresetDeckDefinition CopyDeck(L12PresetDeckDefinition source, string name,
        string? alternateArtId) => new()
    {
        Name = name,
        MasterId = source.MasterId,
        CardIds = [.. source.CardIds],
        MoraleIds = [.. source.MoraleIds],
        SpecialIds = [.. source.SpecialIds],
        BenchIds = [.. source.BenchIds],
        AlternateArtSelections = string.IsNullOrWhiteSpace(alternateArtId)
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.OrdinalIgnoreCase) { [source.MasterId] = alternateArtId },
    };

    private static L12CustomDeckSubmission Submission(L12AccountDeckView deck,
        string? forcedMasterArtId = null) => new()
    {
        Name = deck.Name,
        MasterId = deck.MasterId,
        CardIds = [.. deck.CardIds],
        MoraleIds = [.. deck.MoraleIds],
        SpecialIds = [.. deck.SpecialIds],
        BenchIds = [.. deck.BenchIds ?? []],
        AlternateArtSelections = forcedMasterArtId is null
            ? (deck.AlternateArtSelections ?? new Dictionary<string, string>())
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.OrdinalIgnoreCase) { [deck.MasterId] = forcedMasterArtId },
        AlternateArtCopies = (deck.AlternateArtCopies ?? new Dictionary<string, IReadOnlyList<string>>())
            .ToDictionary(item => item.Key, item => item.Value.ToList(), StringComparer.OrdinalIgnoreCase),
    };

    private static JsonElement MasterFor(JsonElement state, string username)
        => state.GetProperty("players").EnumerateArray()
            .Single(player => player.GetProperty("name").GetString() == username)
            .GetProperty("master");

    private static void AssertQueued(IEnumerable<OutgoingMessage> messages)
    {
        var payload = Assert.Single(messages.Select(Payload)
            .Where(item => item.GetProperty("type").GetString() == "matchmakingState"));
        Assert.True(payload.GetProperty("queued").GetBoolean());
    }

    private static JsonElement PayloadFor(IEnumerable<OutgoingMessage> messages, Guid sessionId, string type)
        => Payload(Assert.Single(messages.Where(message => message.SessionId == sessionId
            && Payload(message).GetProperty("type").GetString() == type)));

    private static JsonElement Payload(OutgoingMessage message)
        => JsonSerializer.SerializeToElement(message.Payload, WireJson);

    private sealed record Peer(L12AccountView Account, Guid SessionId);

    private sealed class Fixture : IAsyncDisposable
    {
        private bool _cleanupAccepted;

        private Fixture(string root, L12Catalog catalog, L12PlatformStore store,
            MatchRecorder recorder, L12RoomManager manager)
        {
            Root = root;
            Catalog = catalog;
            Store = store;
            Recorder = recorder;
            Manager = manager;
        }

        public string Root { get; }
        public L12Catalog Catalog { get; }
        public L12PlatformStore Store { get; }
        public MatchRecorder Recorder { get; }
        public L12RoomManager Manager { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(),
                "l12-matchmaking-master-art-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards, officialAlternateArts: catalog.OfficialAlternateArts,
                officialCardProducts: catalog.CardProducts);
            var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            return new Fixture(root, catalog, store, recorder,
                new L12RoomManager(catalog, recorder, store));
        }

        public Peer RegisterAndConnect(string username)
        {
            var registration = Store.Register(username, "password-123");
            Assert.True(registration.Success, registration.Message);
            var peer = new Peer(registration.Account!, Guid.NewGuid());
            Manager.Connect(peer.SessionId, peer.Account.Id, peer.Account.Username);
            return peer;
        }

        public void AcceptCleanup() => _cleanupAccepted = true;

        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            using var pool = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = Path.Combine(Root, "matches.db") }.ToString());
            SqliteConnection.ClearPool(pool);
            if (_cleanupAccepted && Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
