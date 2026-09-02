using Microsoft.Data.Sqlite;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MatchRecorderTests
{
    private static L12CardInstance Card(L12Catalog catalog, string id, string instanceId)
    {
        var definition = catalog.Cards[id];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = id, Name = definition.NameZh, CardType = definition.CardType,
            Faction = definition.Faction, ImageUrl = definition.ImageUrl, Cost = definition.Cost ?? 0,
            EffectText = definition.Effect, BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
        };
    }

    [Fact]
    public async Task PersistsAcceptedAndRejectedCommandsWithStateSnapshots()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var game = new L12GameEngine(catalog, "record-test", "REC001", 77, ["甲", "乙"], [0, 1], skipPreparation: true);
        await using (var recorder = new MatchRecorder(path))
        {
            await recorder.InitializeAsync();
            await recorder.StartAsync(game.State, "ranked", "account-a", "account-b");
            var accepted = game.Handle(0, new L12Command("mulligan", CardInstanceIds: []));
            await recorder.AppendAsync(game, 1, 0, "{\"type\":\"mulligan\"}", accepted);
            var rejectedPlayer = 1 - game.State.ActivePlayer;
            var rejected = game.Handle(rejectedPlayer, new L12Command("endTurn"));
            await recorder.AppendAsync(game, 2, rejectedPlayer, "{\"type\":\"endTurn\"}", rejected);
            await recorder.CompleteAsync(game);

            var matches = await recorder.ListMatchesAsync();
            var detail = await recorder.GetMatchAsync("record-test");
            var ownMatches = await recorder.ListMatchesForPlayerAsync("甲");
            var unrelatedMatches = await recorder.ListMatchesForPlayerAsync("丙");
            var ownDetail = await recorder.GetMatchForPlayerAsync("record-test", "乙");
            var forbiddenDetail = await recorder.GetMatchForPlayerAsync("record-test", "丙");
            var rankings = await recorder.ListRankingMatchesAsync();
            Assert.Single(matches);
            Assert.Single(ownMatches);
            Assert.Empty(unrelatedMatches);
            Assert.NotNull(detail);
            Assert.NotNull(ownDetail);
            Assert.Null(forbiddenDetail);
            Assert.Single(rankings);
            Assert.Equal("甲", rankings[0].Player0);
            Assert.False(string.IsNullOrWhiteSpace(rankings[0].Master0));
            Assert.Equal(2, detail.Commands.Count);
            Assert.True(detail.Commands[0].Accepted);
            Assert.False(detail.Commands[1].Accepted);
            Assert.Equal("mulligan", detail.Commands[0].Command.GetProperty("type").GetString());
        }

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), SUM(accepted), MIN(LENGTH(state_json)), MIN(LENGTH(state_hash)) FROM match_events";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.True(reader.GetInt32(2) > 1000);
        Assert.Equal(64, reader.GetInt32(3));
    }

    [Fact]
    public async Task PlayerReplayRedactsOpponentPrivateZonesAndUsesCoveredCardOwnershipKnowledge()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var game = new L12GameEngine(catalog, "private-replay", "PRIVATE", 78,
            ["甲", "乙"], [0, 0], skipPreparation: true);
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear();
            player.Library.Clear();
        }
        var firstSecret = Card(catalog, "S01-0103", "first-private-hand");
        var secondSecret = Card(catalog, "S01-0205", "second-private-hand");
        game.State.Players[0].Hand.Add(firstSecret);
        game.State.Players[1].Hand.Add(secondSecret);
        var trojan = Card(catalog, "S02-0523", "private-trojan");
        trojan.OwnerIndex = 1;
        trojan.Hidden = true;
        game.State.Players[0].Field[1][1] = trojan;

        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        await recorder.StartAsync(game.State);
        await recorder.AppendAsync(game, 1, 0, "{\"type\":\"noop\",\"cardInstanceId\":\"first-private-hand\"}", CommandResult.Ok());

        var firstView = Assert.IsType<L12MatchDetail>(await recorder.GetMatchForPlayerAsync("private-replay", "甲"));
        var secondView = Assert.IsType<L12MatchDetail>(await recorder.GetMatchForPlayerAsync("private-replay", "乙"));
        var firstJson = JsonSerializer.Serialize(firstView);
        var secondJson = JsonSerializer.Serialize(secondView);

        Assert.Equal(0, firstView.ViewerPlayerIndex);
        Assert.Equal(1, secondView.ViewerPlayerIndex);
        Assert.Contains(firstSecret.CardId, firstJson);
        Assert.DoesNotContain(secondSecret.CardId, firstJson);
        Assert.DoesNotContain(trojan.CardId, firstJson);
        Assert.Contains("hidden-card", firstJson);
        Assert.Contains(secondSecret.CardId, secondJson);
        Assert.DoesNotContain(firstSecret.CardId, secondJson);
        Assert.Contains(trojan.CardId, secondJson);
        Assert.Contains("\"IdentityKnown\":true", secondJson);
    }
}
