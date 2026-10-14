using System.Collections.Concurrent;
using System.Reflection;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

public sealed class ChatOutboundPriorityTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Q01_NoLowLaneAndConfiguredIdleLaneKeepLegacySequenceAndAllocation()
    {
        await using var plain = new Fixture(configure: false);
        await using var idle = new Fixture();
        Assert.False(plain.Out.HasChatLane);
        Assert.True(idle.Out.HasChatLane);
        var pending = typeof(L12OutboundConnection).GetNestedType("PendingMessage", BindingFlags.NonPublic)!;
        Assert.Equal(new[] { "Delivered", "Diagnostic", "Payload", "ReplaceableGameState" },
            pending.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic).Select(x => x.Name).Order().ToArray());
        Assert.Equal(4, pending.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Length);
        await plain.HoldAsync();
        await idle.HoldAsync();
        string[] values = ["state-1", "state-2", "prompt", "state-3", "state-4", "terminal-like"];
        // Warm the same call sites; this measures synchronous admission, not end-to-end latency.
        foreach (var f in new[] { plain, idle })
            foreach (var value in values) Assert.True(f.Out.TryEnqueue(value, value.StartsWith("state")));
        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var value in values) Assert.True(plain.Out.TryEnqueue(value, value.StartsWith("state")));
        var plainBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var value in values) Assert.True(idle.Out.TryEnqueue(value, value.StartsWith("state")));
        var idleBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"No-low synchronous admission: plain={plainBytes}B idle-configured={idleBytes}B");
        Assert.Equal(plainBytes, idleBytes);
        Assert.Equal(plain.Out.QueuedCount, idle.Out.QueuedCount);
        Assert.Equal(plain.Out.MergedCount, idle.Out.MergedCount);
        Assert.Equal(default, idle.Out.ReadChatLaneSnapshot());
        plain.LegacyRelease.TrySetResult(); idle.LegacyRelease.TrySetResult();
        await plain.Out.CompleteAsync(); await idle.Out.CompleteAsync();
        Assert.Equal(plain.Events.ToArray(), idle.Events.ToArray());
        Assert.Equal(new[] { "hold", "state-2", "prompt", "state-4", "terminal-like",
            "state-2", "prompt", "state-4", "terminal-like" }, plain.Events.ToArray());
    }

    [Fact]
    public async Task Q01_Legacy32RejectsOnlyItsOwnQueueAndLowDoesNotCutMergeWindow()
    {
        await using var f = new Fixture(); await f.HoldAsync();
        Assert.True(f.Out.TryEnqueue("state-1", true));
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue());
        Assert.True(f.Out.TryEnqueue("state-2", true));
        Assert.Equal(1, f.Out.QueuedCount);
        Assert.Equal(1, f.Out.MergedCount);
        for (var i = 0; i < 31; i++) Assert.True(f.Out.TryEnqueue("critical"));
        Assert.Equal(32, f.Out.QueuedCount);
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(value: 2));
        Assert.True(f.Out.TryEnqueue("critical-replaces-state"));
        Assert.False(f.Out.TryEnqueue("critical-full"));
        Assert.Equal(32, f.Out.QueuedCount);
        Assert.Equal(2, f.Out.ReadChatLaneSnapshot().RetainedItems);
        Assert.Equal(2, f.Out.DroppedCount);
    }

    [Fact]
    public async Task Q02_CountAndFullBackingBytesStayChargedQueuedBorrowedAndInflight()
    {
        var limits = new L12ChatLaneLimits(2, 2048, 64, 512);
        await using var f = new Fixture(limits: limits); await f.HoldAsync();
        using var borrowed = new Barrier();
        f.Borrowed = borrowed.Block;
        var wireEntered = NewSignal(); var wireRelease = NewSignal();
        f.Kickoff = (_, ct) => { wireEntered.TrySetResult(); return new ValueTask(wireRelease.Task.WaitAsync(ct)); };
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(backingSize: 512));
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(backingSize: 512, value: 2));
        Assert.Equal((2, 2048L), f.Budget.Snapshot);
        Assert.Equal(L12ChatQueueResult.QuotaRejected, f.Enqueue());
        f.LegacyRelease.TrySetResult();
        try
        {
            await borrowed.WaitEntered();
            Assert.Equal(1, f.Out.ReadChatLaneSnapshot().Queued);
            Assert.Equal(2048, f.Out.ReadChatLaneSnapshot().RetainedBytes);
            Assert.Equal(L12ChatQueueResult.QuotaRejected, f.Enqueue());
            borrowed.Open(); await wireEntered.Task.WaitAsync(Timeout);
            Assert.Equal(2, f.Out.ReadChatLaneSnapshot().RetainedItems);
            Assert.Equal((2, 2048L), f.Budget.Snapshot);
            Assert.Equal(L12ChatQueueResult.QuotaRejected, f.Enqueue());
            wireRelease.TrySetResult();
            await Eventually(() => f.Out.ReadChatLaneSnapshot() is { Sent: 2, RetainedItems: 0 });
            Assert.Equal((0, 0L), f.Budget.Snapshot);
            Assert.Equal(0, f.Registry.Snapshot.Entries);
        }
        finally { borrowed.Open(); wireRelease.TrySetResult(); }
    }

    [Fact]
    public async Task Q02_BytePacketAndGlobalLimitsRejectWithoutLegacyFaultOrEviction()
    {
        var global = new L12ChatGlobalBudget(2, 1536);
        await using var a = new Fixture(budget: global, limits: new(4, 2048, 16, 512));
        await using var b = new Fixture(budget: global, limits: new(4, 2048, 16, 512));
        await a.HoldAsync(); await b.HoldAsync();
        Assert.Equal(L12ChatQueueResult.QuotaRejected, a.Enqueue(backingSize: 1800));
        Assert.Equal(L12ChatQueueResult.QuotaRejected, a.Out.TryEnqueueChat("a", "b", new byte[17], 100));
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, a.Enqueue(backingSize: 256));
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, b.Enqueue(backingSize: 256));
        Assert.Equal(L12ChatQueueResult.QuotaRejected, b.Enqueue());
        Assert.Equal((2, 1536L), global.Snapshot);
        Assert.True(a.Out.TryEnqueue("prompt")); Assert.True(b.Out.TryEnqueue("prompt"));
        Assert.Equal(0, a.Faults); Assert.Equal(0, b.Faults);
        await a.Out.CompleteAsync(false); await b.Out.CompleteAsync(false);
        Assert.Equal((0, 0L), global.Snapshot);
    }

    [Fact]
    public async Task Q02_ChargedMetadataCoversWarmDescriptorAndLeaseAllocation()
    {
        await using var f = new Fixture(); await f.HoldAsync();
        using var pin = f.Registry.TryAcquire("a", "b")!;
        var buffer = new byte[1];
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Out.TryEnqueueChat("a", "b", buffer, 100));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = f.Out.TryEnqueueChat("a", "b", buffer, 100);
        var descriptorBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Warm chat descriptor/node/leases allocation={descriptorBytes}B metadataCharge=512B");
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, result);
        // Include the worst small-array header/alignment padding, in addition to full buffer length.
        Assert.InRange(descriptorBytes + 31, 1, L12ChatLaneLimits.MinimumMetadataBytes);
        Assert.Equal(2 * 513, f.Out.ReadChatLaneSnapshot().RetainedBytes);
    }

    [Fact]
    public void Q02_RegistryFixedIndexAndNewEntryAllocationHaveConservativeCharges()
    {
        _ = new L12ChatPairRegistry(1, 1024, 1, 128); // Warm constructor call site.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var registry = new L12ChatPairRegistry(1, 1024, 1, 128);
        var indexAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(indexAllocation, 1, registry.IndexBytes);
        using var warm = registry.TryAcquire("warm", "pair")!;
        warm.Dispose();
        before = GC.GetAllocatedBytesForCurrentThread();
        using var pair = registry.TryAcquire("a", "b")!;
        var entryAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(entryAllocation, 1, 260);
        output.WriteLine($"Registry index allocation={indexAllocation}B charge={registry.IndexBytes}B; entry+pin={entryAllocation}B charge=260B");
        pair.Dispose();
        // Fixed bucket memory is retained, so its charge is deliberately not refunded on removal.
        Assert.Equal((0, registry.IndexBytes, 0, 0L), registry.Snapshot);
    }

    [Fact]
    public async Task Q02_WarmInflightB1AsyncOwnerAndDescriptorFitConservative512Charge()
    {
        await using var f = new Fixture(); await f.HoldAsync();
        using var pin = f.Registry.TryAcquire("a", "b")!;
        var buffer = new byte[1]; var wire = NewSignal(); var probed = NewSignal();
        long beforeOwner = 0, ownerBytes = 0;
        int ownerThread = 0, probeThread = 0;
        f.Borrowed = () =>
        {
            ownerThread = Environment.CurrentManagedThreadId;
            beforeOwner = GC.GetAllocatedBytesForCurrentThread();
        };
        f.Kickoff = (_, _) => new ValueTask(wire.Task); // Pre-created fake task; B2 transport excluded.
        f.OwnerProbe = () =>
        {
            ownerBytes = GC.GetAllocatedBytesForCurrentThread() - beforeOwner;
            probeThread = Environment.CurrentManagedThreadId;
            probed.TrySetResult();
        };
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Out.TryEnqueueChat("a", "b", buffer, 100));
        f.LegacyRelease.TrySetResult();
        try
        {
            await probed.Task.WaitAsync(Timeout); wire.TrySetResult();
            await Eventually(() => f.Out.ReadChatLaneSnapshot().RetainedItems == 0);
            // Warm the suspended async-owner path before the measured second packet.
            wire = NewSignal(); probed = NewSignal();
            var beforeDescriptor = GC.GetAllocatedBytesForCurrentThread();
            var result = f.Out.TryEnqueueChat("a", "b", buffer, 100);
            var descriptorBytes = GC.GetAllocatedBytesForCurrentThread() - beforeDescriptor;
            Assert.Equal(L12ChatQueueResult.AcceptedQueue, result);
            await probed.Task.WaitAsync(Timeout);
            Assert.Equal(ownerThread, probeThread);
            Assert.Equal(1, f.Out.ReadChatLaneSnapshot().RetainedItems);
            Assert.Equal((1, 513L), f.Budget.Snapshot);
            Assert.InRange(descriptorBytes + ownerBytes + 31, 1, L12ChatLaneLimits.MinimumMetadataBytes);
            output.WriteLine($"B1-only warm allocation: descriptor={descriptorBytes}B suspended-owner={ownerBytes}B array-padding<=31B charge=512B; precreated callback task/codec/real transport excluded");
            wire.TrySetResult();
            await Eventually(() => f.Out.ReadChatLaneSnapshot() is { Sent: 2, RetainedItems: 0 });
            Assert.Equal((0, 0L), f.Budget.Snapshot);
        }
        finally { wire.TrySetResult(); }
    }

    [Fact]
    public void Q02_AllBudgetsAreExplicitPositiveAndOverflowSafe()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new L12ChatLaneLimits(0, 1024, 1, 512));
        Assert.Throws<ArgumentOutOfRangeException>(() => new L12ChatLaneLimits(1, 1024, 1, 511));
        Assert.Throws<ArgumentOutOfRangeException>(() => new L12ChatLaneLimits(1, 256, 1, 512));
        Assert.Throws<ArgumentOutOfRangeException>(() => new L12ChatGlobalBudget(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new L12ChatPairRegistry(1, 255, 1, 128));
        Assert.Throws<ArgumentOutOfRangeException>(() => new L12ChatPairRegistry(1, 1024, 1, 127));
        var budget = new L12ChatGlobalBudget(int.MaxValue, long.MaxValue);
        using var lease = budget.TryReserve(long.MaxValue)!;
        Assert.Null(budget.TryReserve(1));
        Assert.Equal((1, long.MaxValue), budget.Snapshot);
        lease.Dispose(); lease.Dispose();
        Assert.Equal((0, 0L), budget.Snapshot);
        var countOnly = new L12ChatGlobalBudget(1, long.MaxValue);
        using var one = countOnly.TryReserve(1)!;
        Assert.Null(countOnly.TryReserve(1));
        one.Dispose();
        Assert.Equal((0, 0L), countOnly.Snapshot);
        var registry = new L12ChatPairRegistry(1, 460, 1, 128);
        using var pair = registry.TryAcquire("a", "b")!;
        Assert.Null(registry.TryAcquire("a", "c"));
        Assert.Null(registry.TryAcquire(new string('x', 1000), "b"));
        using var mutation = registry.TryBeginMutation("b", "a")!;
        Assert.Null(registry.TryBeginMutation("a", "b"));
        Assert.Equal((1, 460L, 1, 128L), registry.Snapshot);
        mutation.Complete(L12ChatMutationOutcome.KnownRollback); mutation.Dispose();
        Assert.Equal((1, 460L, 0, 0L), registry.Snapshot);
        pair.Dispose(); pair.Dispose();
        Assert.Equal((0, registry.IndexBytes, 0, 0L), registry.Snapshot);
    }

    [Fact]
    public async Task Q03_CriticalAfterBorrowReturnsSameLowNodeAndUsesOneSender()
    {
        await using var f = new Fixture(); await f.HoldAsync();
        using var borrowed = new Barrier(); f.Borrowed = borrowed.Block;
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue());
        f.LegacyRelease.TrySetResult();
        try
        {
            await borrowed.WaitEntered();
            Assert.True(f.Out.TryEnqueue("prompt"));
            Assert.Equal(1, f.Out.ReadChatLaneSnapshot().RetainedItems);
            borrowed.Open();
            await Eventually(() => f.Out.ReadChatLaneSnapshot() is { Sent: 1, RetainedItems: 0 });
            Assert.Equal(new[] { "hold", "prompt", "chat:1" }, f.Events.ToArray());
            Assert.Equal(1, f.MaximumConcurrent);
            Assert.Equal((0, 0L), f.Budget.Snapshot);
        }
        finally { borrowed.Open(); }
    }

    [Fact]
    public async Task Q03_CriticalEnqueueNeverWaitsForChatStartGateOrInflightWire()
    {
        await using var f = new Fixture(); using var start = new Barrier();
        var wire = NewSignal();
        f.Kickoff = (_, ct) => { start.Block(); return new ValueTask(wire.Task.WaitAsync(ct)); };
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue());
        try
        {
            await start.WaitEntered();
            Assert.True(await Task.Run(() => f.Out.TryEnqueue("prompt")).WaitAsync(Timeout));
            Assert.Equal(1, f.Out.QueuedCount);
            Assert.Equal(1, f.Out.ReadChatLaneSnapshot().RetainedItems);
            start.Open();
            await Eventually(() => f.Out.ReadChatLaneSnapshot().Started == 1);
            Assert.Equal(1, f.Out.QueuedCount); // Already started low frame is non-preemptible.
            wire.TrySetResult();
            await f.Out.EnqueueAndWaitAsync("marker", CancellationToken.None).WaitAsync(Timeout);
            Assert.Equal(1, f.Out.ReadChatLaneSnapshot().Sent);
        }
        finally { start.Open(); wire.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Q04_CompletionOrTerminalReleasesQueuedButNeverBorrowedEarly(bool terminal)
    {
        await using var f = new Fixture(); await f.HoldAsync();
        using var borrowed = new Barrier(); f.Borrowed = borrowed.Block;
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue());
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(value: 2));
        f.LegacyRelease.TrySetResult();
        try
        {
            await borrowed.WaitEntered();
            Task done = terminal ? f.Out.EnqueueTerminalAndCompleteAsync("terminal", CancellationToken.None)
                : f.Out.CompleteAsync();
            Assert.Equal(0, f.Out.ReadChatLaneSnapshot().Queued);
            Assert.Equal((1, 513L), f.Budget.Snapshot);
            Assert.Equal(1, f.Out.ReadChatLaneSnapshot().RetainedItems);
            borrowed.Open(); await done.WaitAsync(Timeout);
            Assert.Equal((0, 0L), f.Budget.Snapshot);
            Assert.Equal(0, f.Registry.Snapshot.Entries);
            Assert.Equal(0, f.Out.ReadChatLaneSnapshot().Started);
            Assert.Equal(0, f.Faults);
            Assert.Equal(L12ChatQueueResult.Suppressed, f.Enqueue());
        }
        finally { borrowed.Open(); }
    }

    [Fact]
    public async Task Q04_LegacyFaultReleasesQueuedLowAndStartedLowFaultReleasesAllOnce()
    {
        await using (var legacy = new Fixture())
        {
            await legacy.HoldAsync();
            Assert.Equal(L12ChatQueueResult.AcceptedQueue, legacy.Enqueue());
            legacy.LegacyRelease.TrySetException(new InvalidOperationException("synthetic legacy fault"));
            await legacy.Fault.Task.WaitAsync(Timeout);
            await legacy.Out.CompleteAsync();
            Assert.True(legacy.Out.ChatStartsClosed);
            Assert.Equal((0, 0L), legacy.Budget.Snapshot);
            Assert.Equal(0, legacy.Registry.Snapshot.Entries);
        }
        await using var f = new Fixture(); await f.HoldAsync();
        f.Kickoff = (_, _) => ValueTask.FromException(new InvalidOperationException("synthetic wire fault"));
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue());
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(value: 2));
        f.LegacyRelease.TrySetResult(); await f.Fault.Task.WaitAsync(Timeout);
        await f.Out.CompleteAsync();
        Assert.Equal(1, f.Faults);
        Assert.Equal(1, f.Out.ReadChatLaneSnapshot().TransportFaults);
        Assert.True(f.Out.ChatStartsClosed);
        Assert.Equal((0, 0L), f.Budget.Snapshot);
        Assert.Equal(0, f.Registry.Snapshot.Entries);
    }

    [Fact]
    public async Task Q05_AllPausedSleepWithoutPollingAndLegacyContinuesUntilExplicitWake()
    {
        await using var f = new Fixture(); await f.HoldAsync();
        var borrowed = 0; var checkedFresh = 0;
        f.Borrowed = () => Interlocked.Increment(ref borrowed);
        f.Fresh = (_, _) => { Interlocked.Increment(ref checkedFresh); return true; };
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue());
        using var mutation = f.Registry.TryBeginMutation("a", "b")!;
        Assert.Equal(L12ChatQueueResult.Paused, f.Enqueue());
        f.LegacyRelease.TrySetResult();
        await f.Out.EnqueueAndWaitAsync("prompt", CancellationToken.None).WaitAsync(Timeout);
        await Task.Delay(50); // Observation only; production has no timer or per-item wait task.
        Assert.Equal(0, borrowed); Assert.Equal(0, checkedFresh);
        Assert.Equal(1, f.Out.ReadChatLaneSnapshot().Queued);
        var clockReads = Volatile.Read(ref f.ClockReads);
        await Task.Delay(50);
        Assert.Equal(clockReads, Volatile.Read(ref f.ClockReads)); // Also detect a paused scan/signal busy loop.
        mutation.Complete(L12ChatMutationOutcome.KnownRollback);
        Assert.True(f.Out.WakeChatLane());
        await Eventually(() => f.Out.ReadChatLaneSnapshot().Sent == 1);
        Assert.Equal(1, borrowed); Assert.Equal(1, checkedFresh);
        Assert.Equal(new[] { "hold", "prompt", "chat:1" }, f.Events.ToArray());
    }

    [Theory]
    [InlineData("false")]
    [InlineData("throw")]
    [InlineData("expired")]
    public async Task Q06_NormalSuppressionExpiryAndUnreadableGuardNeverFaultOrReportSent(string mode)
    {
        await using var f = new Fixture(); await f.HoldAsync();
        f.Fresh = (_, _) => mode == "throw" ? throw new InvalidOperationException("unreadable") : false;
        Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(expiresAt: 10));
        if (mode == "expired") f.Now = 10;
        f.LegacyRelease.TrySetResult();
        await Eventually(() => f.Out.ReadChatLaneSnapshot().RetainedItems == 0);
        var s = f.Out.ReadChatLaneSnapshot();
        Assert.Equal(0, s.Started); Assert.Equal(0, s.Sent); Assert.Equal(0, s.TransportFaults);
        Assert.Equal(mode == "expired" ? 1 : 0, s.Expired);
        Assert.Equal(mode == "expired" ? 0 : 1, s.Suppressed);
        Assert.Equal(0, f.Faults); Assert.True(f.Out.TryEnqueue("prompt"));
        Assert.Equal((0, 0L), f.Budget.Snapshot);
        Assert.Equal(0, f.Registry.Snapshot.Entries);
    }

    // Named RED regression against blocked-original-256/B82F: its one wake sends only one low
    // item, leaving ready nodes charged forever. The deadline fails this test instead of hanging.
    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    public async Task Q05_MultiplePausedPacketsDrainOnOneWakeWithReadyLegacyFirst(int count)
    {
        await using var f = new Fixture(); await f.HoldAsync();
        for (byte i = 1; i <= count; i++) Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(value: i));
        using var mutation = f.Registry.TryBeginMutation("a", "b")!;
        f.LegacyRelease.TrySetResult();
        // Exactly count admission clock reads + count paused scans exhaust all original permits.
        await Eventually(() => Volatile.Read(ref f.ClockReads) >= 2 * count);
        Assert.Equal(count, f.Out.ReadChatLaneSnapshot().Queued);
        Assert.Equal(0, f.Out.ReadChatLaneSnapshot().Started);
        mutation.Complete(L12ChatMutationOutcome.KnownRollback);
        // Test-only queue barrier makes legacy ready and the single explicit wake visible together.
        // No pair/start gate is taken while this barrier is held.
        lock (f.QueueGate)
        {
            Assert.True(f.Out.TryEnqueue("prompt-after-resume"));
            Assert.True(f.Out.WakeChatLane());
        }
        try
        {
            await Eventually(() => f.Out.ReadChatLaneSnapshot().Sent == count &&
                f.Out.ReadChatLaneSnapshot().RetainedItems == 0);
        }
        catch
        {
            output.WriteLine($"Single-wake drain failed before deadline: {f.Out.ReadChatLaneSnapshot()}");
            throw;
        }
        Assert.Equal(new[] { "hold", "prompt-after-resume" }
            .Concat(Enumerable.Range(1, count).Select(i => $"chat:{i}")), f.Events.ToArray());
        Assert.Equal((0, 0L), f.Budget.Snapshot);
        Assert.Equal((0, f.Registry.IndexBytes, 0, 0L), f.Registry.Snapshot);
        Assert.Equal(0, f.Faults);
    }

    [Fact]
    public async Task Q05_ResumedDrainSleepsAgainOnNewPrecloseAndContinuesAfterOneMoreWake()
    {
        await using var f = new Fixture(); await f.HoldAsync();
        for (byte i = 1; i <= 4; i++) Assert.Equal(L12ChatQueueResult.AcceptedQueue, f.Enqueue(value: i));
        using var first = f.Registry.TryBeginMutation("a", "b")!;
        f.LegacyRelease.TrySetResult();
        await Eventually(() => Volatile.Read(ref f.ClockReads) >= 8);
        using var secondBorrowed = new Barrier(); var borrows = 0;
        f.Borrowed = () => { if (Interlocked.Increment(ref borrows) == 2) secondBorrowed.Block(); };
        first.Complete(L12ChatMutationOutcome.KnownRollback);
        Assert.True(f.Out.WakeChatLane());
        try
        {
            await secondBorrowed.WaitEntered();
            using var again = f.Registry.TryBeginMutation("a", "b")!;
            secondBorrowed.Open();
            await Eventually(() => f.Out.ReadChatLaneSnapshot() is { Sent: 1, Paused: 1, Queued: 3 });
            await Task.Delay(50);
            var reads = Volatile.Read(ref f.ClockReads);
            await Task.Delay(50);
            Assert.Equal(reads, Volatile.Read(ref f.ClockReads));
            Assert.Equal((3, 1539L), f.Budget.Snapshot);
            await f.Out.EnqueueAndWaitAsync("prompt-while-paused", CancellationToken.None).WaitAsync(Timeout);
            again.Complete(L12ChatMutationOutcome.KnownRollback);
            Assert.True(f.Out.WakeChatLane());
            await Eventually(() => f.Out.ReadChatLaneSnapshot() is { Sent: 4, RetainedItems: 0 });
            Assert.Equal(new[] { "hold", "chat:1", "prompt-while-paused", "chat:2", "chat:3", "chat:4" }, f.Events.ToArray());
            Assert.Equal((0, 0L), f.Budget.Snapshot);
            Assert.Equal(0, f.Faults);
        }
        finally { secondBorrowed.Open(); }
    }

    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    internal static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static async Task Eventually(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!condition()) await Task.Delay(1, timeout.Token);
    }
    internal sealed class Barrier : IDisposable
    {
        private readonly TaskCompletionSource _entered = NewSignal();
        private readonly ManualResetEventSlim _release = new();
        internal void Block()
        {
            _entered.TrySetResult();
            if (!_release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Synthetic barrier not released.");
        }
        internal Task WaitEntered() => _entered.Task.WaitAsync(Timeout);
        internal void Open() => _release.Set();
        public void Dispose() { _release.Set(); _release.Dispose(); }
    }
    internal sealed class Fixture : IAsyncDisposable
    {
        internal readonly L12OutboundConnection Out;
        internal readonly L12ChatGlobalBudget Budget;
        internal readonly L12ChatPairRegistry Registry;
        internal object QueueGate => typeof(L12OutboundConnection)
            .GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Out)!;
        internal readonly ConcurrentQueue<string> Events = new();
        internal readonly TaskCompletionSource LegacyEntered = NewSignal(), LegacyRelease = NewSignal();
        internal readonly TaskCompletionSource<Exception> Fault = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal long Now = 1;
        internal int Faults, MaximumConcurrent, ClockReads;
        private int _concurrent;
        internal Action? Borrowed, OwnerProbe;
        internal Func<string, string, bool> Fresh = (_, _) => true;
        internal Func<ArraySegment<byte>, CancellationToken, ValueTask> Kickoff;
        internal Fixture(bool configure = true, L12ChatLaneLimits? limits = null,
            L12ChatGlobalBudget? budget = null, L12ChatPairRegistry? registry = null)
        {
            Budget = budget ?? new(64, 65536); Registry = registry ?? new(64, 65536, 64, 8192);
            Kickoff = (bytes, _) =>
            {
                Enter(); Events.Enqueue($"chat:{bytes.Array![bytes.Offset]}"); Exit(); return ValueTask.CompletedTask;
            };
            Out = new(async (payload, ct) =>
            {
                Enter();
                try
                {
                    var value = Assert.IsType<string>(payload); Events.Enqueue(value);
                    if (value == "hold") { LegacyEntered.TrySetResult(); await LegacyRelease.Task.WaitAsync(ct); }
                }
                finally { Exit(); }
            }, e => { Interlocked.Increment(ref Faults); Fault.TrySetResult(e); });
            if (configure) Out.ConfigureChatLane(limits ?? new(8, 8192, 64, 512), Budget, Registry,
                () => { Interlocked.Increment(ref ClockReads); return Volatile.Read(ref Now); },
                (a, b) => Fresh(a, b), (bytes, ct) => Kickoff(bytes, ct),
                () => Borrowed?.Invoke(), () => OwnerProbe?.Invoke());
        }
        private void Enter() { var n = Interlocked.Increment(ref _concurrent); MaximumConcurrent = Math.Max(MaximumConcurrent, n); }
        private void Exit() => Interlocked.Decrement(ref _concurrent);
        internal async Task HoldAsync()
        {
            Assert.True(Out.TryEnqueue("hold")); await LegacyEntered.Task.WaitAsync(Timeout);
        }
        internal L12ChatQueueResult Enqueue(int backingSize = 1, byte value = 1,
            long expiresAt = 100, string first = "a", string second = "b")
        {
            var buffer = new byte[backingSize]; buffer[^1] = value;
            return Out.TryEnqueueChat(first, second, new ArraySegment<byte>(buffer, backingSize - 1, 1), expiresAt);
        }
        public async ValueTask DisposeAsync() { LegacyRelease.TrySetResult(); await Out.DisposeAsync(); }
    }
}
