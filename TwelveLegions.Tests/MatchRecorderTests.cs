using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MatchRecorderTests
{
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
            await recorder.StartAsync(game.State);
            var accepted = game.Handle(0, new L12Command("mulligan", CardInstanceIds: []));
            await recorder.AppendAsync(game, 1, 0, "{\"type\":\"mulligan\"}", accepted);
            var rejectedPlayer = 1 - game.State.ActivePlayer;
            var rejected = game.Handle(rejectedPlayer, new L12Command("endTurn"));
            await recorder.AppendAsync(game, 2, rejectedPlayer, "{\"type\":\"endTurn\"}", rejected);

            var matches = await recorder.ListMatchesAsync();
            var detail = await recorder.GetMatchAsync("record-test");
            Assert.Single(matches);
            Assert.NotNull(detail);
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
}
