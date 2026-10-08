using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class TerminalOutboundFenceTests
{
    [Fact]
    public async Task TerminalAuthenticationNoticeDiscardsQueuedPrivateFrames()
    {
        var sent = new List<string>();
        var senderEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var outbound = new L12OutboundConnection(async (payload, _) =>
        {
            var value = Assert.IsType<string>(payload);
            sent.Add(value);
            if (value != "in-flight") return;
            senderEntered.TrySetResult();
            await releaseSender.Task;
        });

        Assert.True(outbound.TryEnqueue("in-flight"));
        await senderEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var queuedPrivate = outbound.EnqueueAndWaitAsync("queued-private-frame",
            CancellationToken.None);

        var terminal = outbound.EnqueueTerminalAndCompleteAsync("authenticationRequired",
            CancellationToken.None);
        Assert.False(outbound.TryEnqueue("late-private-frame"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await queuedPrivate);
        releaseSender.TrySetResult();
        Assert.True(await terminal.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.Equal(["in-flight", "authenticationRequired"], sent);
    }

    [Fact]
    public async Task OnlyFirstTerminalNoticeCanOwnCompletedConnection()
    {
        var sent = new List<string>();
        await using var outbound = new L12OutboundConnection((payload, _) =>
        {
            sent.Add(Assert.IsType<string>(payload));
            return Task.CompletedTask;
        });

        Assert.True(await outbound.EnqueueTerminalAndCompleteAsync("first",
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(await outbound.EnqueueTerminalAndCompleteAsync("second",
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(outbound.TryEnqueue("late"));
        Assert.Equal(["first"], sent);
    }

    [Fact]
    public async Task TerminalSendTimeoutReportsFaultAndKeepsFutureFramesFenced()
    {
        var fault = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var outbound = new L12OutboundConnection(
            async (_, cancellationToken) =>
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken),
            error => fault.TrySetResult(error), TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await outbound.EnqueueTerminalAndCompleteAsync("authenticationRequired",
                CancellationToken.None));
        Assert.IsAssignableFrom<OperationCanceledException>(
            await fault.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(outbound.TryEnqueue("after-terminal-fault"));
        Assert.Equal(0, outbound.QueuedCount);
    }
}
