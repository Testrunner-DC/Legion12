using System.Collections.Concurrent;
using TwelveLegions.Server;
using Xunit;
using static TwelveLegions.Platform.Tests.ChatOutboundPriorityTests;
using Barrier = TwelveLegions.Platform.Tests.ChatOutboundPriorityTests.Barrier;

namespace TwelveLegions.Platform.Tests;

// Synthetic kickoff barriers only. These tests are not HTTP, WebSocket or real transport evidence.
public sealed class ChatSendStartFenceTests
{
    private static readonly TimeSpan Timeout = ChatOutboundPriorityTests.Timeout;
    [Fact]
    public async Task F01_QueuedSnapshotBeforePrecloseCannotStartAfterCommittedGeneration()
    {
        await using var f = new Fixture(); await f.HoldAsync();
        var facts = true; f.Fresh = (_, _) => facts;
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue());
        using var change = f.Registry.TryBeginMutation("b", "a")!;
        Assert.Equal(L12ChatQueueResult.Paused, f.Enqueue());
        // Authority mutation is simulated only after pre-close has returned.
        facts = false; change.Complete(L12ChatMutationOutcome.Committed);
        f.LegacyRelease.TrySetResult(); f.Out.WakeChatLane();
        await Eventually(() => f.Out.ReadChatLaneSnapshot().RetainedItems == 0);
        Assert.Equal(0, f.Out.ReadChatLaneSnapshot().Started);
        Assert.Equal(1, f.Out.ReadChatLaneSnapshot().Suppressed);
        Assert.Equal((0, 0L), f.Budget.Snapshot);
        Assert.Equal((0, f.Registry.IndexBytes, 0, 0L), f.Registry.Snapshot);
        Assert.Equal(0, f.Faults);
    }

    [Fact]
    public async Task F02_BorrowedBeforePrecloseIsStillChargedAndCannotStartAfterCommit()
    {
        await using var f = new Fixture(); using var borrowed = new Barrier();
        f.Borrowed = borrowed.Block;
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(backingSize: 1024));
        try
        {
            await borrowed.WaitEntered();
            Assert.Equal(0, f.Out.ReadChatLaneSnapshot().Queued);
            using var mutation = f.Registry.TryBeginMutation("a", "b")!;
            mutation.Complete(L12ChatMutationOutcome.Committed);
            Assert.Equal((1, 1536L), f.Budget.Snapshot);
            Assert.Equal(1, f.Registry.Snapshot.Entries);
            borrowed.Open();
            await Eventually(() => f.Out.ReadChatLaneSnapshot().RetainedItems == 0);
            Assert.Equal(0, f.Out.ReadChatLaneSnapshot().Started);
            Assert.Equal((0, 0L), f.Budget.Snapshot);
            Assert.Equal(0, f.Registry.Snapshot.Entries);
        }
        finally { borrowed.Open(); }
    }

    [Fact]
    public async Task F03_FreshCheckAndSynchronousKickoffStayInsideTheSameMutationGate()
    {
        await using var f = new Fixture(); using var fresh = new Barrier();
        var order = new ConcurrentQueue<string>(); var attempted = NewSignal();
        f.Fresh = (_, _) => { order.Enqueue("check"); fresh.Block(); return true; };
        f.Kickoff = (_, _) => { order.Enqueue("kickoff"); return ValueTask.CompletedTask; };
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue());
        Task? mutationTask = null;
        try
        {
            await fresh.WaitEntered();
            mutationTask = Task.Run(() =>
            {
                attempted.TrySetResult();
                using var mutation = f.Registry.TryBeginMutation("a", "b")!;
                order.Enqueue("preclose-returned");
                mutation.Complete(L12ChatMutationOutcome.Committed);
            });
            await attempted.Task.WaitAsync(Timeout);
            await Task.Delay(30);
            Assert.False(mutationTask.IsCompleted); // A snapshot-only check would let begin return here.
            Assert.Equal(new[] { "check" }, order.ToArray());
            // Critical enqueue has no start-gate dependency even while the guard is deliberately stalled.
            Assert.True(await Task.Run(() => f.Out.TryEnqueue("prompt")).WaitAsync(Timeout));
            fresh.Open(); await mutationTask.WaitAsync(Timeout);
            await Eventually(() => f.Out.ReadChatLaneSnapshot().Sent == 1);
            Assert.Equal(new[] { "check", "kickoff", "preclose-returned" }, order.ToArray());
            Assert.Equal(0, f.Faults);
        }
        finally { fresh.Open(); if (mutationTask is not null) await mutationTask.WaitAsync(Timeout); }
    }

    [Fact]
    public async Task F03_PrecloseWinsBeforeFinalCheckSoBorrowedItemPausesWithoutKickoff()
    {
        await using var f = new Fixture(); using var borrowed = new Barrier();
        f.Borrowed = borrowed.Block; var checkedFresh = 0;
        f.Fresh = (_, _) => { Interlocked.Increment(ref checkedFresh); return true; };
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue());
        try
        {
            await borrowed.WaitEntered();
            using var mutation = f.Registry.TryBeginMutation("a", "b")!;
            borrowed.Open();
            await Eventually(() => f.Out.ReadChatLaneSnapshot() is { Paused: 1, Queued: 1 });
            Assert.Equal(0, checkedFresh); Assert.Equal(0, f.Out.ReadChatLaneSnapshot().Started);
            Assert.Equal((1, 513L), f.Budget.Snapshot);
            mutation.Complete(L12ChatMutationOutcome.Committed);
            Assert.True(f.Out.WakeChatLane());
            await Eventually(() => f.Out.ReadChatLaneSnapshot().RetainedItems == 0);
            Assert.Equal(0, f.Out.ReadChatLaneSnapshot().Sent);
        }
        finally { borrowed.Open(); }
    }

    [Fact]
    public async Task F05_NestedMutationDepthAndUnblockGenerationNeverResurrectOldPackets()
    {
        await using var f = new Fixture(); await f.HoldAsync();
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(value: 1));
        using var first = f.Registry.TryBeginMutation("a", "b")!;
        using var second = f.Registry.TryBeginMutation("b", "a")!;
        Assert.Equal(2, f.Registry.Snapshot.Pending);
        first.Complete(L12ChatMutationOutcome.KnownRollback);
        Assert.Equal(1, f.Registry.Snapshot.Pending);
        Assert.Equal(L12ChatQueueResult.Paused, f.Enqueue(value: 2));
        second.Complete(L12ChatMutationOutcome.Committed);
        using (var unblock = f.Registry.TryBeginMutation("a", "b")!)
            unblock.Complete(L12ChatMutationOutcome.Committed);
        // Synthetic fresh facts now allow; success generation still invalidates the old message.
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(value: 3));
        f.LegacyRelease.TrySetResult(); f.Out.WakeChatLane();
        await Eventually(() => f.Out.ReadChatLaneSnapshot().RetainedItems == 0);
        Assert.Equal(new[] { "hold", "chat:3" }, f.Events.ToArray());
        Assert.Equal(1, f.Out.ReadChatLaneSnapshot().Suppressed);
        Assert.Equal(1, f.Out.ReadChatLaneSnapshot().Sent);
        Assert.Equal((0, f.Registry.IndexBytes, 0, 0L), f.Registry.Snapshot);
    }

    [Fact]
    public async Task F05_KnownRollbackResumesButUnknownOutcomeStaysClosedUntilExplicitFreshRecovery()
    {
        await using var f = new Fixture(); await f.HoldAsync();
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue());
        var unknown = f.Registry.TryBeginMutation("a", "b")!;
        using var known = f.Registry.TryBeginMutation("b", "a")!;
        unknown.Dispose(); unknown.Complete(L12ChatMutationOutcome.KnownRollback); // Does not reopen twice.
        known.Complete(L12ChatMutationOutcome.KnownRollback);
        f.LegacyRelease.TrySetResult(); f.Out.WakeChatLane();
        await f.Out.EnqueueAndWaitAsync("prompt", CancellationToken.None).WaitAsync(Timeout);
        Assert.Equal(L12ChatQueueResult.Paused, f.Enqueue());
        Assert.False(f.Registry.TryResolveUnknown("a", "b", (_, _) => false));
        Assert.False(f.Registry.TryResolveUnknown("a", "b", (_, _) => throw new Exception("unknown")));
        Assert.Equal(0, f.Out.ReadChatLaneSnapshot().Started);
        Assert.Equal(1, f.Registry.Snapshot.Entries);
        Assert.True(f.Registry.TryResolveUnknown("a", "b", (_, _) => true));
        Assert.True(f.Out.WakeChatLane());
        await Eventually(() => f.Out.ReadChatLaneSnapshot() is { Sent: 1, RetainedItems: 0 });
        Assert.Equal((0, 0L), f.Budget.Snapshot);
        Assert.Equal((0, f.Registry.IndexBytes, 0, 0L), f.Registry.Snapshot);
    }

    [Fact]
    public void F05_UnknownEntriesAndPendingMutationsRemainBoundedWithoutEvictingActiveFence()
    {
        var registry = new L12ChatPairRegistry(1, 460, 1, 128);
        using (var unknown = registry.TryBeginMutation("a", "b")!) unknown.Dispose();
        Assert.Equal((1, 460L, 0, 0L), registry.Snapshot);
        Assert.Null(registry.TryBeginMutation("a", "c"));
        using var held = registry.TryBeginMutation("a", "b")!;
        Assert.Null(registry.TryBeginMutation("b", "a"));
        Assert.False(registry.TryResolveUnknown("a", "b", (_, _) => true));
        held.Complete(L12ChatMutationOutcome.KnownRollback);
        Assert.True(registry.TryResolveUnknown("a", "b", (_, _) => true));
        Assert.Equal((0, registry.IndexBytes, 0, 0L), registry.Snapshot);
    }

    [Fact]
    public async Task F07_ClosedRecipientEpochSuppressesBorrowedAndCannotBeReopenedByPairRollback()
    {
        await using var f = new Fixture(); using var borrowed = new Barrier();
        f.Borrowed = borrowed.Block;
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue());
        try
        {
            await borrowed.WaitEntered();
            f.Out.CloseChatStarts(); f.Out.CloseChatStarts();
            using var mutation = f.Registry.TryBeginMutation("a", "b")!;
            mutation.Complete(L12ChatMutationOutcome.KnownRollback);
            Assert.True(f.Out.TryEnqueue("critical"));
            Assert.Equal(L12ChatQueueResult.Suppressed, f.Enqueue());
            Assert.Equal((1, 513L), f.Budget.Snapshot);
            borrowed.Open();
            await Eventually(() => f.Out.ReadChatLaneSnapshot().RetainedItems == 0);
            Assert.Equal(0, f.Out.ReadChatLaneSnapshot().Started);
            Assert.Equal(0, f.Faults);
        }
        finally { borrowed.Open(); }
    }

    [Fact]
    public async Task F07_NewRecipientConnectionSharesPendingPairAndOldCloseWaitsForKickoffOnly()
    {
        var registry = new L12ChatPairRegistry(8, 8192, 8, 1024);
        await using var old = new Fixture(registry: registry);
        await using var replacement = new Fixture(registry: registry);
        using var kickoff = new Barrier(); var wire = NewSignal();
        old.Kickoff = (_, ct) => { kickoff.Block(); return new ValueTask(wire.Task.WaitAsync(ct)); };
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, old.Enqueue());
        Task? closeTask = null;
        try
        {
            await kickoff.WaitEntered();
            var closeAttempted = NewSignal();
            closeTask = Task.Run(() => { closeAttempted.TrySetResult(); old.Out.CloseChatStarts(); });
            await closeAttempted.Task.WaitAsync(Timeout);
            await Task.Delay(30); Assert.False(closeTask.IsCompleted);
            Assert.True(await Task.Run(() => old.Out.TryEnqueue("critical")).WaitAsync(Timeout));
            kickoff.Open(); await closeTask.WaitAsync(Timeout);
            // The epoch close did not wait for or cancel the already started wire operation.
            Assert.Equal(1, old.Out.ReadChatLaneSnapshot().RetainedItems);
            using var change = registry.TryBeginMutation("a", "b")!;
            Assert.Equal(L12ChatQueueResult.Paused, replacement.Enqueue());
            change.Complete(L12ChatMutationOutcome.KnownRollback);
            Assert.Equal(L12ChatQueueResult.AcceptedQueue, replacement.Enqueue());
            await Eventually(() => replacement.Out.ReadChatLaneSnapshot() is { Sent: 1, RetainedItems: 0 });
            wire.TrySetResult(); await Eventually(() => old.Out.ReadChatLaneSnapshot() is { Sent: 1, RetainedItems: 0 });
            Assert.Equal(0, old.Faults); Assert.Equal(0, replacement.Faults);
            Assert.Equal((0, registry.IndexBytes, 0, 0L), registry.Snapshot);
        }
        finally { kickoff.Open(); wire.TrySetResult(); if (closeTask is not null) await closeTask.WaitAsync(Timeout); }
    }

    [Theory]
    [InlineData("terminal")]
    [InlineData("drain")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    public async Task F07_ShutdownWinsBeforeFinalStartAndReleasesQueuedThenBorrowed(string kind)
    {
        await using var f = new Fixture(); await f.HoldAsync(); using var borrowed = new Barrier();
        f.Borrowed = borrowed.Block;
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(value: 1));
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(value: 2));
        f.LegacyRelease.TrySetResult();
        Task? shutdown = null;
        try
        {
            await borrowed.WaitEntered();
            shutdown = Task.Run(() => Shutdown(f.Out, kind));
            await Eventually(() => f.Out.ChatStartsClosed);
            Assert.Equal(0, f.Out.ReadChatLaneSnapshot().Queued);
            Assert.Equal(1, f.Out.ReadChatLaneSnapshot().RetainedItems);
            Assert.Equal((1, 513L), f.Budget.Snapshot);
            Assert.Equal(0, f.Out.ReadChatLaneSnapshot().Started);
            Assert.False(shutdown.IsCompleted);
            Assert.False(f.Out.TryEnqueue("late-critical"));
            borrowed.Open(); await shutdown.WaitAsync(Timeout);
            Assert.Equal(0, f.Out.ReadChatLaneSnapshot().Started);
            Assert.Equal(0, f.Out.ReadChatLaneSnapshot().Sent);
            Assert.Equal((0, 0L), f.Budget.Snapshot);
            Assert.Equal((0, f.Registry.IndexBytes, 0, 0L), f.Registry.Snapshot);
            Assert.Equal(kind == "terminal" ? new[] { "hold", "terminal-notice" } : new[] { "hold" }, f.Events.ToArray());
            Assert.Equal(0, f.Faults);
        }
        finally { borrowed.Open(); if (shutdown is not null) await shutdown.WaitAsync(Timeout); }
    }

    [Theory]
    [InlineData("terminal")]
    [InlineData("drain")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    public async Task F07_FinalCheckWinsShutdownMustCrossKickoffGateButNeverWaitWireToClose(string kind)
    {
        await using var f = new Fixture(); await f.HoldAsync(); using var fresh = new Barrier();
        var wire = NewSignal(); var calls = 0;
        f.Fresh = (_, _) => { fresh.Block(); return true; };
        f.Kickoff = (bytes, _) =>
        {
            Interlocked.Increment(ref calls); f.Events.Enqueue($"chat:{bytes.Array![bytes.Offset]}");
            // Deliberately ignore connection cancellation to distinguish short gate close from wire await.
            return new ValueTask(wire.Task);
        };
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(value: 1));
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(value: 2));
        f.LegacyRelease.TrySetResult();
        Task? shutdown = null;
        try
        {
            await fresh.WaitEntered();
            shutdown = Task.Run(() => Shutdown(f.Out, kind));
            // Queued second item is removed at admission close; the control call then waits the start gate.
            await Eventually(() => f.Out.ReadChatLaneSnapshot().Queued == 0);
            Assert.False(f.Out.ChatStartsClosed);
            Assert.False(shutdown.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref calls));
            fresh.Open();
            await Eventually(() => f.Out.ChatStartsClosed);
            Assert.Equal(1, Volatile.Read(ref calls));
            Assert.False(wire.Task.IsCompleted);
            Assert.False(shutdown.IsCompleted); // Lifecycle waits the owner, but closing the gate did not.
            Assert.Equal((1, 513L), f.Budget.Snapshot);
            Assert.Equal(L12ChatQueueResult.Suppressed, f.Enqueue(value: 3));
            wire.TrySetResult(); await shutdown.WaitAsync(Timeout);
            Assert.Equal(1, f.Out.ReadChatLaneSnapshot().Started);
            Assert.Equal(1, f.Out.ReadChatLaneSnapshot().Sent);
            Assert.Equal((0, 0L), f.Budget.Snapshot);
            Assert.Equal((0, f.Registry.IndexBytes, 0, 0L), f.Registry.Snapshot);
            Assert.Equal(kind == "terminal" ? new[] { "hold", "chat:1", "terminal-notice" } : new[] { "hold", "chat:1" }, f.Events.ToArray());
            Assert.Equal(0, f.Faults);
        }
        finally { fresh.Open(); wire.TrySetResult(); if (shutdown is not null) await shutdown.WaitAsync(Timeout); }
    }

    [Fact]
    public async Task F07_SynchronousKickoffFaultClosesStartsAndReleasesAllRetainedOwners()
    {
        await using var f = new Fixture(); await f.HoldAsync(); var calls = 0;
        f.Kickoff = (_, _) => { Interlocked.Increment(ref calls); throw new InvalidOperationException("synthetic kickoff fault"); };
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue());
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(value: 2));
        f.LegacyRelease.TrySetResult();
        await f.Fault.Task.WaitAsync(Timeout); await f.Out.CompleteAsync().WaitAsync(Timeout);
        Assert.True(f.Out.ChatStartsClosed);
        Assert.Equal(1, calls); Assert.Equal(1, f.Faults);
        Assert.Equal(1, f.Out.ReadChatLaneSnapshot().TransportFaults);
        Assert.Equal((0, 0L), f.Budget.Snapshot);
        Assert.Equal((0, f.Registry.IndexBytes, 0, 0L), f.Registry.Snapshot);
    }

    private static Task Shutdown(L12OutboundConnection outbound, string kind) => kind switch
    {
        "terminal" => outbound.EnqueueTerminalAndCompleteAsync("terminal-notice", CancellationToken.None),
        "drain" => outbound.CompleteAsync(),
        "cancel" => outbound.CompleteAsync(false),
        "dispose" => outbound.DisposeAsync().AsTask(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
