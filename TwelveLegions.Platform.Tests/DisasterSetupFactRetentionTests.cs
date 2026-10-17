using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DisasterSetupFactRetentionTests
{
    [Fact]
    public async Task RealPlayerReplayCleanupPreservesFactsWhileDeletingReplayPayload()
    {
        await using var f = await DisasterFactFixture.Create();
        await f.Prepare();
        await f.End();
        var before = JsonSerializer.Serialize(await f.Read());
        var beforeCounts = await f.Scalar("SELECT COUNT(*) FROM match_action_events;");
        Assert.True(beforeCounts > 0);
        var result = await f.Recorder.RunPlayerReplayCleanupIfDueAsync(activeMatchIds: [],
            evidenceProvider: () => new([], []), utcNow: f.Now.AddDays(9));
        Assert.True(result.Ran);
        Assert.Equal(1, result.PurgedMatches);
        Assert.Equal(0, await f.Scalar("SELECT COUNT(*) FROM match_state_checkpoints;"));
        Assert.Equal(0, await f.Scalar("SELECT COUNT(*) FROM match_action_events;"));
        Assert.Equal(before, JsonSerializer.Serialize(await f.Read()));
        Assert.True(await f.Recorder.IsPlayerReplayPayloadExpiredAsync(f.Game.State.MatchId));
    }

    [Fact]
    public async Task RealIsolatedTestRunCleanupPreservesCaptureAfterFourteenDays()
    {
        await using var f = await DisasterFactFixture.Create(testrun: true);
        await f.Prepare();
        await f.End();
        var before = JsonSerializer.Serialize(await f.Read());
        var result = await f.Recorder.RunTestRunRetentionCoreAsync("testrun", "https://legion-12.com/testrun",
            f.Root, Path.Combine(Path.GetDirectoryName(f.Root)!, "legion12-runtime"), [], () => new([], []),
            f.Now.AddDays(16), TimeSpan.FromSeconds(3), CancellationToken.None, enforceInstalledPaths: false);
        Assert.True(result.Ran);
        Assert.Equal(1, result.PurgedMatches);
        Assert.Equal(0, await f.Scalar("SELECT COUNT(*) FROM match_state_checkpoints;"));
        Assert.Equal(before, JsonSerializer.Serialize(await f.Read()));
    }

    [Fact]
    public async Task BoundedCandidatesUseTheirIndexAndDoNotGrowAfterPreparation()
    {
        await using var f = await DisasterFactFixture.Create();
        await f.Prepare();
        var rows = await f.Scalar("SELECT COUNT(*) FROM match_disaster_candidates;");
        Assert.Equal(3 * f.Game.DisasterCaptureSeed!.Pool.Length + 2, rows);
        for (var index = 0; index < 16; index++)
            await f.Record(0, new("resolvePrompt", PromptId: "expired-prompt", Choice: "missing"), accepted: false);
        Assert.Equal(rows, await f.Scalar("SELECT COUNT(*) FROM match_disaster_candidates;"));
        Assert.Empty(f.Game.DisasterSetupObservations);
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={f.Database};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN SELECT match_id,step FROM match_disaster_candidates WHERE card_id='S01-DS01';";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Contains("ix_match_disaster_candidates_card", reader.GetString(3));
    }
}
