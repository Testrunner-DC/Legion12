using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class RecordedInternalCommandOriginTests
{
    [Theory]
    [InlineData(0, "authorityConclusion", false)]
    [InlineData(1, "authorityConclusion", false)]
    [InlineData(0, "setResponsePreference", false)]
    [InlineData(1, "setResponsePreference", false)]
    [InlineData(0, "responseAutoClose", false)]
    [InlineData(1, "responseAutoClose", false)]
    [InlineData(0, "authorityConclusion", true)]
    [InlineData(1, "authorityConclusion", true)]
    [InlineData(0, "setResponsePreference", true)]
    [InlineData(1, "setResponsePreference", true)]
    [InlineData(0, "responseAutoClose", true)]
    [InlineData(1, "responseAutoClose", true)]
    public async Task RejectedPlayerActionCannotBecomeInternalCommandDuringReplay(
        int player, string type, bool ranked)
    {
        await using var fixture = await CommandDeadlineClockFixture.CreateAsync(player, 10, ranked);
        var command = new L12Command(type);
        var result = fixture.Engine.Handle(player, command);
        Assert.False(result.Accepted);
        var rejectedHash = fixture.Engine.ComputeStateHash();
        await fixture.AppendAsync(1, command, result, ranked);
        await AssertRejectedReplayAsync(fixture, rejectedHash, ranked);
    }

    [Theory]
    [InlineData("authorityConclusion")]
    [InlineData("setResponsePreference")]
    [InlineData("responseAutoClose")]
    public async Task RejectedGmActionCannotBecomeInternalCommandDuringReplay(string type)
    {
        await using var fixture = await CommandDeadlineClockFixture.CreateAsync(0, 10);
        var command = new L12GmCommand(type);
        var result = fixture.Engine.HandleGm(command);
        Assert.False(result.Accepted);
        var rejectedHash = fixture.Engine.ComputeStateHash();
        await fixture.Recorder.AppendAsync(fixture.Engine, 1, -1,
            JsonSerializer.Serialize(command, CommandDeadlineClockFixture.WireJson), result);
        await AssertRejectedReplayAsync(fixture, rejectedHash, ranked: false);
    }

    private static async Task AssertRejectedReplayAsync(CommandDeadlineClockFixture fixture,
        string expectedHash, bool ranked)
    {
        var detail = Assert.IsType<L12MatchDetail>(
            await fixture.Recorder.GetMatchAsync(fixture.Engine.State.MatchId));
        var recorded = Assert.Single(detail.Commands);
        Assert.False(recorded.Accepted);
        Assert.Equal(expectedHash, recorded.StateHash);
        Assert.Equal(fixture.Now, DateTimeOffset.Parse(recorded.ReceivedUtc));
        Assert.Equal(expectedHash, fixture.Engine.ComputeStateHash());
        Assert.NotEqual(L12Phase.GameOver, fixture.Engine.State.Phase);

        // Move past the write without exhausting the synthetic 120-second turn.
        fixture.Now = fixture.Now.AddSeconds(20);
        var journal = Assert.IsType<L12JournalRecoveryState>(
            await fixture.Recorder.LoadJournalEngineAsync(fixture.Engine.State.MatchId));
        Assert.Equal(expectedHash, journal.Engine.ComputeStateHash());
        if (!ranked) return;
        var manager = new L12RoomManager(fixture.Catalog, fixture.Recorder, utcNow: () => fixture.Now);
        var restored = await manager.RestoreRankedRoomsAsync();
        Assert.Equal(1, restored.Restored);
        Assert.Equal(0, restored.Failed);
        Assert.Equal(0, restored.Invalidated);
        Assert.Equal(expectedHash, CommandDeadlineClockFixture.RestoredEngine(manager).ComputeStateHash());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RejectedInternalTypeCannotForgeAgreedDrawInAdminSummary(int player)
    {
        await using var fixture = await CommandDeadlineClockFixture.CreateAsync(player, 10);
        var result = fixture.Engine.Handle(player, new L12Command("authorityConclusion"));
        Assert.False(result.Accepted);
        await fixture.Recorder.AppendAsync(fixture.Engine, 1, player,
            JsonSerializer.Serialize(new { type = "authorityConclusion", agreedDraw = true }), result);
        fixture.Engine.ConcludeByAuthority(null, "synthetic invalid conclusion");
        Assert.False(fixture.Engine.State.EndedByAgreedDraw);
        await fixture.Recorder.AppendAuthorityAsync(fixture.Engine, 2, "synthetic invalid conclusion");
        Assert.True(await fixture.Recorder.CompleteAsync(fixture.Engine));
        var summary = Assert.IsType<L12AdminMatchDetail>(
            await fixture.Recorder.GetAdminMatchAsync(fixture.Engine.State.MatchId));
        Assert.Equal("invalid", summary.Summary.Status);
        Assert.All(summary.Summary.Players, entry => Assert.Equal("invalid", entry.Result));
        Assert.Empty((await fixture.Recorder.ListAdminMatchesAsync(new L12AdminMatchQuery(Status: "completed"))).Items);
        Assert.Single((await fixture.Recorder.ListAdminMatchesAsync(new L12AdminMatchQuery(Status: "invalid"))).Items);
        Assert.Equal(2, (await fixture.Recorder.GetMatchAsync(fixture.Engine.State.MatchId))!.Commands.Count);
    }
}
