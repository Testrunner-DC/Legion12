using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PlayerReplayDeckNamePrivacyTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private const string FirstPrivateName = "PRIVATE_DECK_ALPHA_DO_NOT_PUBLISH";
    private const string SecondPrivateName = "PRIVATE_DECK_BETA_DO_NOT_PUBLISH";

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    public async Task PlayerListsAndEveryReplayFrameHideDeckNamesWithoutChangingRawArchive(
        int stateFormatVersion, int viewer)
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-replay-deck-name-privacy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "matches.db");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var game = new L12GameEngine(catalog, "private-deck-labels", "PRVLABEL", 810208,
                ["甲", "乙"],
                [NamedDeck(catalog.DeckAt(0), FirstPrivateName), NamedDeck(catalog.DeckAt(1), SecondPrivateName)],
                skipPreparation: true, stateFormatVersion: stateFormatVersion);
            await using (var recorder = new MatchRecorder(path))
            {
                recorder.AttachCatalog(catalog);
                await recorder.InitializeAsync();
                if (stateFormatVersion == 2)
                    await recorder.StartAsync(game, "friendly", "account-a", "account-b");
                else
                    await recorder.StartAsync(game.State, "friendly", "account-a", "account-b");
                for (var player = 0; player < 2; player++)
                {
                    var command = new L12Command("mulligan", CardInstanceIds: []);
                    var result = game.Handle(player, command);
                    Assert.True(result.Accepted, result.Error);
                    await recorder.AppendAsync(game, player + 1, player, JsonSerializer.Serialize(command, WireJson), result);
                }
                game.ConcludeByAuthority(0, "合成回放隐私测试结束");
                Assert.True(await recorder.CompleteAsync(game));

                var original = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(game.State.MatchId));
                var originalJson = JsonSerializer.Serialize(original, WireJson);
                Assert.Contains(FirstPrivateName, originalJson, StringComparison.Ordinal);
                Assert.Contains(SecondPrivateName, originalJson, StringComparison.Ordinal);
                Assert.Equal(FirstPrivateName, original.Match.Deck0);
                Assert.Equal(SecondPrivateName, original.Match.Deck1);
                Assert.Equal(2, original.Commands.Count);

                var account = viewer == 0 ? "account-a" : "account-b";
                var name = viewer == 0 ? "甲" : "乙";
                var playerList = Assert.Single(await recorder.ListMatchesForAccountAsync(account, name));
                AssertNoPrivateNames(JsonSerializer.Serialize(playerList, WireJson));
                Assert.Equal(string.Empty, playerList.Deck0);
                Assert.Equal(string.Empty, playerList.Deck1);
                var legacyList = Assert.Single(await recorder.ListMatchesForPlayerAsync(name));
                AssertNoPrivateNames(JsonSerializer.Serialize(legacyList, WireJson));

                var playerDetail = Assert.IsType<L12MatchDetail>(
                    await recorder.GetMatchForAccountAsync(game.State.MatchId, account, name));
                var legacyDetail = Assert.IsType<L12MatchDetail>(
                    await recorder.GetMatchForPlayerAsync(game.State.MatchId, name));
                foreach (var detail in new[] { playerDetail, legacyDetail })
                {
                    Assert.Equal(viewer, detail.ViewerPlayerIndex);
                    AssertNoPrivateNames(JsonSerializer.Serialize(detail, WireJson));
                    Assert.Equal(original.Commands.Select(command => command.StateHash),
                        detail.Commands.Select(command => command.StateHash));
                    foreach (var frame in detail.Commands)
                        foreach (var player in frame.State.GetProperty("Players").EnumerateArray())
                        {
                            Assert.False(player.TryGetProperty("DeckName", out _));
                            Assert.False(player.TryGetProperty("deckName", out _));
                        }
                }
                Assert.Null(await recorder.GetMatchForAccountAsync(game.State.MatchId, "stranger", "甲"));
                Assert.Null(await recorder.GetMatchForPlayerAsync(game.State.MatchId, "第三方"));

                // Private player projection is read-only. Raw administrator/archive
                // metadata, state bytes and original state hashes must remain intact.
                Assert.Equal(originalJson, JsonSerializer.Serialize(
                    await recorder.GetMatchAsync(game.State.MatchId), WireJson));
                var internalList = Assert.Single(await recorder.ListMatchesAsync());
                Assert.Equal(FirstPrivateName, internalList.Deck0);
                Assert.Equal(SecondPrivateName, internalList.Deck1);
                var fixtureOutput = Environment.GetEnvironmentVariable("L12_REPLAY_SYNTHETIC_FIXTURE_OUTPUT");
                if (!string.IsNullOrWhiteSpace(fixtureOutput))
                {
                    Directory.CreateDirectory(fixtureOutput);
                    File.WriteAllText(Path.Combine(fixtureOutput, $"replay-v{stateFormatVersion}-viewer{viewer}.json"),
                        JsonSerializer.Serialize(new
                        {
                            format = "legion12-replay", version = 1, compatibilityVersion = 1,
                            exportedAt = DateTimeOffset.UtcNow,
                            detail = playerDetail,
                        }, WireJson));
                }
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static L12PresetDeckDefinition NamedDeck(L12PresetDeckDefinition source, string name) => new()
    {
        Name = name,
        MasterId = source.MasterId,
        CardIds = [.. source.CardIds],
        MoraleIds = [.. source.MoraleIds],
        SpecialIds = [.. source.SpecialIds],
    };

    private static void AssertNoPrivateNames(string json)
    {
        Assert.DoesNotContain(FirstPrivateName, json, StringComparison.Ordinal);
        Assert.DoesNotContain(SecondPrivateName, json, StringComparison.Ordinal);
    }
}
