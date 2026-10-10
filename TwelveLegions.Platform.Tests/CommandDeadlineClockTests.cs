using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class CommandDeadlineClockTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 10)]
    [InlineData(1, 10)]
    [InlineData(0, 1000)]
    [InlineData(1, 1000)]
    public async Task PublicManualResponseUsesExecutionTimeForReplayDespiteLateReceipt(int first, int delayMs)
    {
        await using var fixture = await CommandDeadlineClockFixture.CreateAsync(first, delayMs);
        var executionTime = fixture.Now;
        var command = fixture.PassCommand();
        var result = fixture.Engine.Handle(first, command);
        Assert.True(result.Accepted, result.Error);
        var lease = Assert.IsType<L12ResponseAutoCloseLease>(fixture.Engine.CaptureResponseAutoCloseLease());
        Assert.Equal(1 - first, lease.PriorityPlayer);
        Assert.Equal(executionTime.AddSeconds(5), lease.DeadlineUtc);
        await fixture.AppendAsync(1, command, result);

        var detail = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(fixture.Engine.State.MatchId));
        var replayed = Assert.Single(detail.Commands);
        Assert.Equal(fixture.Engine.ComputeStateHash(), replayed.StateHash);
        Assert.Equal(lease.DeadlineUtc,
            replayed.State.GetProperty("ResponseWindow").GetProperty("AutoCloseDeadlineUtc").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task JournalTailReplaysRecordedTimeAndResumesLiveClock(int first)
    {
        await using var fixture = await CommandDeadlineClockFixture.CreateAsync(first, 10);
        var command = fixture.PassCommand();
        var result = fixture.Engine.Handle(first, command);
        Assert.True(result.Accepted, result.Error);
        await fixture.AppendAsync(1, command, result);
        var originalHash = fixture.Engine.ComputeStateHash();
        fixture.Now = fixture.Now.AddMinutes(10);
        var recovery = Assert.IsType<L12JournalRecoveryState>(
            await fixture.Recorder.LoadJournalEngineAsync(fixture.Engine.State.MatchId));
        Assert.Equal(originalHash, recovery.Engine.ComputeStateHash());
        Assert.Equal(fixture.RecorderNow, SnapshotClock(recovery.Engine, first));
        CommandDeadlineClockFixture.SeedFreshWindow(recovery.Engine, first);
        var next = recovery.Engine.Handle(first, fixture.PassCommand(recovery.Engine));
        Assert.True(next.Accepted, next.Error);
        Assert.Equal(fixture.RecorderNow.AddSeconds(5),
            recovery.Engine.CaptureResponseAutoCloseLease()!.DeadlineUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RankedRecoveryReturnsLiveClockForSubsequentOrdinaryCommands(int first)
    {
        await using var fixture = await CommandDeadlineClockFixture.CreateAsync(first, 0, ranked: true);
        var command = fixture.PassCommand();
        var result = fixture.Engine.Handle(first, command);
        Assert.True(result.Accepted, result.Error);
        await fixture.AppendAsync(1, command, result, ranked: true);
        var manager = new L12RoomManager(fixture.Catalog, fixture.Recorder, utcNow: () => fixture.Now);
        var restored = await manager.RestoreRankedRoomsAsync();
        Assert.Equal(1, restored.Restored);
        Assert.Equal(0, restored.Failed);
        Assert.Equal(0, restored.Invalidated);
        var engine = CommandDeadlineClockFixture.RestoredEngine(manager);
        Assert.Equal(fixture.Engine.ComputeStateHash(), engine.ComputeStateHash());
        fixture.Now = fixture.Now.AddMinutes(10);
        Assert.Equal(fixture.Now, SnapshotClock(engine, first));
        CommandDeadlineClockFixture.SeedFreshWindow(engine, first);
        var next = engine.Handle(first, fixture.PassCommand(engine));
        Assert.True(next.Accepted, next.Error);
        Assert.Equal(fixture.Now.AddSeconds(5), engine.CaptureResponseAutoCloseLease()!.DeadlineUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task DuplicateAppendIsIdempotentButCannotReuseConsumedCommandTime(int first)
    {
        await using var fixture = await CommandDeadlineClockFixture.CreateAsync(first, 10);
        var command = fixture.PassCommand();
        var result = fixture.Engine.Handle(first, command);
        Assert.True(result.Accepted, result.Error);
        await fixture.AppendAsync(1, command, result);
        await fixture.AppendAsync(1, command, result);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.AppendAsync(2, command, result));
        Assert.Equal("v2 对局命令缺少权威执行时刻，拒绝持久化", error.Message);
        var detail = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(fixture.Engine.State.MatchId));
        Assert.Single(detail.Commands);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RejectedCommandDoesNotSupplyLaterSuccessfulCommandsDeadline(int first)
    {
        await using var fixture = await CommandDeadlineClockFixture.CreateAsync(first, 10);
        var rejected = fixture.PassCommand() with { Choice = "not-a-legal-choice" };
        var denial = fixture.Engine.Handle(first, rejected);
        Assert.False(denial.Accepted);
        await fixture.AppendAsync(1, rejected, denial);
        fixture.Now = fixture.Now.AddSeconds(20);
        var valid = fixture.PassCommand();
        var result = fixture.Engine.Handle(first, valid);
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(fixture.Now.AddSeconds(5), fixture.Engine.CaptureResponseAutoCloseLease()!.DeadlineUtc);
        await fixture.AppendAsync(2, valid, result);
        var detail = Assert.IsType<L12MatchDetail>(await fixture.Recorder.GetMatchAsync(fixture.Engine.State.MatchId));
        Assert.Equal(2, detail.Commands.Count);
        Assert.False(detail.Commands[0].Accepted);
        Assert.True(detail.Commands[1].Accepted);
    }

    [Fact]
    public async Task FailedReplayScopeLeavesNoTimingTicketOrHistoricalClockInLiveEngine()
    {
        await using var fixture = await CommandDeadlineClockFixture.CreateAsync(0, 10);
        var earlier = fixture.Now.AddYears(-1);
        var error = Assert.Throws<InvalidOperationException>(() => fixture.Engine.ReplayRecordedCommand(
            earlier, () => throw new InvalidOperationException("synthetic scope failure")));
        Assert.Equal("synthetic scope failure", error.Message);
        Assert.Null(fixture.Engine.TakeRecordedCommandTiming(CommandResult.Reject("synthetic"), validateResult: true));
        var command = fixture.PassCommand();
        var result = fixture.Engine.Handle(0, command);
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(fixture.Now.AddSeconds(5), fixture.Engine.CaptureResponseAutoCloseLease()!.DeadlineUtc);
        await fixture.AppendAsync(1, command, result);
        Assert.Single((await fixture.Recorder.GetMatchAsync(fixture.Engine.State.MatchId))!.Commands);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task GmCommandPersistsItsExecutionTimeNotLateRecorderTime(int player)
    {
        await using var fixture = await CommandDeadlineClockFixture.CreateAsync(player, 1000);
        var command = new L12GmCommand("addMorale", TargetPlayer: player, Value: 1);
        var result = fixture.Engine.HandleGm(command);
        Assert.True(result.Accepted, result.Error);
        await fixture.Recorder.AppendAsync(fixture.Engine, 1, -1,
            JsonSerializer.Serialize(command, CommandDeadlineClockFixture.WireJson), result);
        var recorded = Assert.Single((await fixture.Recorder.GetMatchAsync(fixture.Engine.State.MatchId))!.Commands);
        Assert.Equal(fixture.Now, DateTimeOffset.Parse(recorded.ReceivedUtc));
        Assert.Equal(fixture.Engine.ComputeStateHash(), recorded.StateHash);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task PreferenceCommandPersistsItsExecutionTimeNotLateRecorderTime(int player)
    {
        await using var fixture = await CommandDeadlineClockFixture.CreateAsync(player, 1000);
        var mode = L12GameEngine.InvalidFiveSecondsResponseMode;
        var result = fixture.Engine.ApplyResponsePreference(player, mode);
        Assert.True(result.Accepted, result.Error);
        await fixture.Recorder.AppendAsync(fixture.Engine, 1, player,
            JsonSerializer.Serialize(new { type = "setResponsePreference", responseMode = mode }), result);
        var recorded = Assert.Single((await fixture.Recorder.GetMatchAsync(fixture.Engine.State.MatchId))!.Commands);
        Assert.Equal(fixture.Now, DateTimeOffset.Parse(recorded.ReceivedUtc));
        Assert.Equal(fixture.Engine.ComputeStateHash(), recorded.StateHash);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task AutoCloseAndItsSafeNoOpPersistObservedTimeNotLateRecorderTime(int first, bool apply)
    {
        await using var fixture = await CommandDeadlineClockFixture.CreateAsync(first, 1000);
        var pass = fixture.PassCommand();
        var result = fixture.Engine.Handle(first, pass);
        Assert.True(result.Accepted, result.Error);
        await fixture.AppendAsync(1, pass, result);
        var lease = fixture.Engine.CaptureResponseAutoCloseLease()!;
        fixture.Now = lease.DeadlineUtc.AddMilliseconds(apply ? 1 : -1);
        Assert.Equal(apply, fixture.Engine.TryExpireResponseAutoClose(lease.PromptId,
            lease.StackItemId, lease.PriorityPlayer, lease.DeadlineUtc, fixture.Now));
        await fixture.Recorder.AppendAsync(fixture.Engine, 2, -1,
            JsonSerializer.Serialize(new
            {
                type = "responseAutoClose", promptId = lease.PromptId, stackItemId = lease.StackItemId,
                priorityPlayer = lease.PriorityPlayer, deadlineUtc = lease.DeadlineUtc,
                observedAtUtc = fixture.Now,
            }), CommandResult.Ok());
        var detail = (await fixture.Recorder.GetMatchAsync(fixture.Engine.State.MatchId))!;
        Assert.Equal(2, detail.Commands.Count);
        Assert.Equal(fixture.Now, DateTimeOffset.Parse(detail.Commands[1].ReceivedUtc));
        Assert.Equal(fixture.Engine.ComputeStateHash(), detail.Commands[1].StateHash);
    }

    private static DateTimeOffset SnapshotClock(L12GameEngine engine, int viewer)
    {
        var projected = JsonSerializer.SerializeToElement(engine.SnapshotForGm(viewer),
            CommandDeadlineClockFixture.WireJson);
        return projected.GetProperty("prompts")[0].GetProperty("autoClose")
            .GetProperty("serverNowUtc").GetDateTimeOffset();
    }
}
