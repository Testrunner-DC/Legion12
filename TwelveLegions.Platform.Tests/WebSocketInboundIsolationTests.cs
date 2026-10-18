using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class WebSocketInboundIsolationTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TerminalRecorderV3StaysOffIsEphemeralCappedAndEmitsOnlySixNumericSlots()
    {
        const string nonce = "0123456789abcdef0123456789abcdef";
        Assert.Null(L12SyntheticTransportDiagnostics.Create(false, null));
        Assert.Throws<InvalidOperationException>(() => L12SyntheticTransportDiagnostics.Create(false,
            $"{L12SyntheticTransportDiagnostics.RecorderMode}:6:{nonce}"));
        Assert.Throws<InvalidOperationException>(() => L12SyntheticTransportDiagnostics.Create(true,
            $"{L12SyntheticTransportDiagnostics.RecorderMode}:7:{nonce}"));
        var lines = new ConcurrentQueue<string>();
        using var diagnostics = L12SyntheticTransportDiagnostics.Create(true,
            $"{L12SyntheticTransportDiagnostics.RecorderMode}:6:{nonce}", lines.Enqueue)!;
        Assert.True(diagnostics.IsBoundsMode);
        Assert.True(diagnostics.IsRecorderMode);
        Assert.True(diagnostics.AcceptsCommand("surrender"));
        Assert.True(diagnostics.AcceptsCommand("playCard"));
        Assert.True(diagnostics.AcceptsCommand("endTurn"));
        Assert.False(diagnostics.AcceptsCommand("attack"));
        Assert.Null(diagnostics.TryBegin($"l12diag:{nonce}:0:{Guid.NewGuid():N}", default));
        var transport = Guid.NewGuid();
        for (var slot = 0; slot < 6; slot++)
        {
            var sample = Assert.IsType<L12SyntheticTransportSample>(diagnostics.TryBegin(
                $"l12diag:{nonce}:{slot}:{Guid.NewGuid():N}", default, transport));
            Assert.NotNull(sample.RecorderObserver);
        }
        Assert.Null(diagnostics.TryBegin($"l12diag:{nonce}:6:{Guid.NewGuid():N}", default, transport));
        Assert.Null(diagnostics.TryBegin($"l12diag:{nonce}:0:{Guid.NewGuid():N}", default, transport));
        diagnostics.Dispose();
        Assert.Equal(7, lines.Count);
        Assert.All(lines, line =>
        {
            Assert.DoesNotContain(nonce, line, StringComparison.Ordinal);
            Assert.DoesNotContain(transport.ToString("N"), line, StringComparison.OrdinalIgnoreCase);
            using var document = JsonDocument.Parse(line);
            Assert.Equal(3, document.RootElement.GetProperty("schema").GetInt32());
            Assert.All(document.RootElement.EnumerateObject(), property =>
                Assert.Equal(JsonValueKind.Number, property.Value.ValueKind));
        });
        Assert.Equal(Enumerable.Range(0, 6), diagnostics.SnapshotForTests().Select(row => row.Slot));
    }

    [Theory]
    [InlineData("ordered", 1, 1)]
    [InlineData("missing", 1, 0)]
    [InlineData("duplicate", 2, 0)]
    [InlineData("reversed", 1, 0)]
    public void TerminalRecorderV3RequiresOneCompleteOrderedInterval(string arm, int count, int valid)
    {
        const string nonce = "102132435465768798a9bacbdcedfe0f";
        using var diagnostics = L12SyntheticTransportDiagnostics.Create(true,
            $"{L12SyntheticTransportDiagnostics.RecorderMode}:1:{nonce}", _ => { })!;
        var origin = Stopwatch.GetTimestamp();
        var sample = Assert.IsType<L12SyntheticTransportSample>(diagnostics.TryBegin(
            $"l12diag:{nonce}:0:{Guid.NewGuid():N}",
            new L12InboundQueueTiming(origin, origin, 1, 128, 0, origin), Guid.NewGuid()));
        sample.GateAcquired(origin + 1);
        var observer = sample.RecorderObserver!;
        observer(L12SyntheticRecorderObservation.Append, L12SyntheticRecorderObservation.WaitStarted, origin + 2);
        if (arm != "missing")
        {
            observer(L12SyntheticRecorderObservation.Append, L12SyntheticRecorderObservation.Acquired,
                arm == "reversed" ? origin + 1 : origin + 3);
            observer(L12SyntheticRecorderObservation.Append, L12SyntheticRecorderObservation.HoldEnded, origin + 4);
        }
        if (arm == "duplicate")
            observer(L12SyntheticRecorderObservation.Append, L12SyntheticRecorderObservation.WaitStarted, origin + 5);
        var row = Assert.Single(diagnostics.SnapshotForTests());
        Assert.Equal(count, row.RecorderAppend.Count);
        Assert.Equal(valid, row.RecorderAppend.Valid);
        Assert.Equal(0, row.RecorderComplete.Count);
        Assert.Equal(0, row.RecorderComplete.Valid);
    }

    [Fact]
    public async Task TerminalRecorderV3ScopeSeparatesParallelRequestsRestoresExceptionsAndFencesInheritedWork()
    {
        var observations = new ConcurrentQueue<(int Owner, int Boundary)>();
        await Task.WhenAll(Enumerable.Range(0, 2).Select(owner => Task.Run(async () =>
        {
            using var scope = L12SyntheticRecorderObservation.Begin((_, boundary, _) =>
                observations.Enqueue((owner, boundary)));
            L12SyntheticRecorderObservation.Record(1, 1);
            await Task.Yield();
            L12SyntheticRecorderObservation.Record(1, 2);
        })));
        Assert.Equal(4, observations.Count);
        foreach (var owner in Enumerable.Range(0, 2))
            Assert.Equal(new[] { 1, 2 }, observations.Where(item => item.Owner == owner).Select(item => item.Boundary));
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var scope = L12SyntheticRecorderObservation.Begin((_, boundary, _) =>
                observations.Enqueue((2, boundary)));
            L12SyntheticRecorderObservation.Record(1, 1);
            throw new InvalidOperationException("synthetic scope failure");
        }));
        L12SyntheticRecorderObservation.Record(1, 2);
        Assert.Equal(new[] { 1 }, observations.Where(item => item.Owner == 2).Select(item => item.Boundary));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task inherited;
        using (L12SyntheticRecorderObservation.Begin((_, boundary, _) => observations.Enqueue((3, boundary))))
        {
            using (L12SyntheticRecorderObservation.Begin(null)) L12SyntheticRecorderObservation.Record(1, 1);
            L12SyntheticRecorderObservation.Record(1, 2);
            inherited = Task.Run(async () => { await release.Task; L12SyntheticRecorderObservation.Record(1, 3); });
        }
        release.SetResult();
        await inherited;
        Assert.Equal(new[] { 2 }, observations.Where(item => item.Owner == 3).Select(item => item.Boundary));
        using (L12SyntheticRecorderObservation.Begin((_, _, _) => throw new InvalidOperationException("observer")))
            Assert.Null(Record.Exception(() => L12SyntheticRecorderObservation.Record(1, 1)));
    }

    [Fact]
    public async Task TerminalRecorderV3DeadlineInvalidatesIntervalsAndLateCallbacksCannotMutateSealedRows()
    {
        const string nonce = "2031425364758697a8b9cadbecfd0e1f";
        var lines = new ConcurrentQueue<string>();
        using var diagnostics = L12SyntheticTransportDiagnostics.Create(true,
            $"{L12SyntheticTransportDiagnostics.RecorderMode}:1:{nonce}", lines.Enqueue,
            TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(10))!;
        var origin = Stopwatch.GetTimestamp();
        var sample = Assert.IsType<L12SyntheticTransportSample>(diagnostics.TryBegin(
            $"l12diag:{nonce}:0:{Guid.NewGuid():N}",
            new L12InboundQueueTiming(origin, origin, 1, 128, 0, origin), Guid.NewGuid()));
        sample.GateAcquired(origin);
        using (L12SyntheticRecorderObservation.Begin(sample.RecorderObserver))
        {
            L12SyntheticRecorderObservation.Record(1, 1);
            L12SyntheticRecorderObservation.Record(1, 2);
            L12SyntheticRecorderObservation.Record(1, 3);
        }
        Assert.Equal(1, Assert.Single(diagnostics.SnapshotForTests()).RecorderAppend.Valid);
        Assert.True(SpinWait.SpinUntil(() => lines.Count == 2, TimeSpan.FromSeconds(2)));
        var sealedRow = Assert.Single(diagnostics.SnapshotForTests());
        Assert.Equal((int)L12SyntheticTransportStatus.Deadline, sealedRow.Status);
        Assert.Equal(0, sealedRow.RecorderAppend.Valid);
        sample.RecorderObserver!(2, 1, Stopwatch.GetTimestamp());
        sample.RecorderObserver!(1, 1, origin); // Backdating cannot reopen a sealed owner.
        await Task.Yield();
        Assert.Equal(sealedRow, Assert.Single(diagnostics.SnapshotForTests()));
        Assert.Equal(2, lines.Count);
    }

    [Theory]
    [InlineData("terminal-burst-v1", 1)]
    [InlineData("terminal-bounds-v2", 2)]
    public void TerminalRecorderV3DoesNotChangeLegacySchemaOutput(string mode, int schema)
    {
        const string nonce = "30415263748596a7b8c9daebfc0d1e2f";
        var lines = new ConcurrentQueue<string>();
        using var diagnostics = L12SyntheticTransportDiagnostics.Create(true, $"{mode}:1:{nonce}", lines.Enqueue)!;
        var sample = Assert.IsType<L12SyntheticTransportSample>(diagnostics.TryBegin(
            $"l12diag:{nonce}:0:{Guid.NewGuid():N}", default, Guid.NewGuid()));
        Assert.Null(sample.RecorderObserver);
        sample.MissingReply();
        diagnostics.Dispose();
        var expected = "{\"schema\":" + schema + ",\"kind\":1,\"slot\":0,\"status\":6,\"in_fifo_us\":0,\"in_depth\":0,\"in_bytes\":0,\"in_remaining\":0,\"pre_gate_us\":0,\"gate_wait_us\":0,\"gate_hold_us\":0,\"serialize_us\":0,\"out_enq_depth\":0,\"out_deq_depth\":0,\"out_queue_us\":0,\"prepare_us\":0,\"wire_bytes\":0,\"wire_delta\":0,\"send_us\":0,\"commit_us\":0,\"server_total_us\":0"
            + (schema == 1 ? "}" : ",\"receive_to_enqueue_us\":0,\"receive_valid\":0,\"lease_valid\":0,\"lease_start_rel_us\":0,\"lease_end_rel_us\":0,\"enqueue_rel_us\":0,\"send_start_rel_us\":0,\"send_end_rel_us\":0,\"commit_rel_us\":0,\"receipt_rel_us\":0,\"receipt_valid\":0,\"receipt_after_send_valid\":0}");
        Assert.Equal(expected, lines.First());
        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.DoesNotContain("append_", line, StringComparison.Ordinal));
    }

    [Fact]
    public void TerminalBurstDiagnosticsStayOffAndFailClosedOutsideTheIsolatedProfile()
    {
        const string nonce = "0123456789abcdef0123456789abcdef";
        Assert.Null(L12SyntheticTransportDiagnostics.Create(false, null));
        Assert.Throws<InvalidOperationException>(() =>
            L12SyntheticTransportDiagnostics.Create(true, "   "));
        Assert.Throws<InvalidOperationException>(() =>
            L12SyntheticTransportDiagnostics.Create(false, string.Empty));

        var error = Assert.Throws<InvalidOperationException>(() =>
            L12SyntheticTransportDiagnostics.Create(false,
                $"{L12SyntheticTransportDiagnostics.Mode}:1:{nonce}"));
        Assert.DoesNotContain(nonce, error.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() =>
            L12SyntheticTransportDiagnostics.Create(true,
                $"{L12SyntheticTransportDiagnostics.Mode}:1:{nonce.ToUpperInvariant()}"));

        var disposed = L12SyntheticTransportDiagnostics.Create(true,
            $"{L12SyntheticTransportDiagnostics.Mode}:1:{nonce}")!;
        disposed.Dispose();
        Assert.Null(disposed.TryBegin($"l12diag:{nonce}:0:{Guid.NewGuid():N}", default));

        var callbackFault = L12SyntheticTransportDiagnostics.Create(true,
            $"{L12SyntheticTransportDiagnostics.Mode}:1:{nonce}",
            _ => throw new InvalidOperationException("synthetic sink failure"))!;
        Assert.NotNull(callbackFault.TryBegin($"l12diag:{nonce}:0:{Guid.NewGuid():N}",
            new L12InboundQueueTiming(1, 2, 1, 100, 0)));
        Assert.Null(Record.Exception(callbackFault.Dispose));
    }

    [Fact]
    public void TerminalBurstDiagnosticsRequireExactNonceUniqueNumericSlotAndUuid()
    {
        const string nonce = "0123456789abcdef0123456789abcdef";
        var lines = new ConcurrentQueue<string>();
        using var diagnostics = L12SyntheticTransportDiagnostics.Create(true,
            $"{L12SyntheticTransportDiagnostics.Mode}:2:{nonce}", lines.Enqueue,
            TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(10))!;
        var timing = new L12InboundQueueTiming(1, 2, 3, 400, 1);

        Assert.Null(diagnostics.TryBegin(
            $"l12diag:1123456789abcdef0123456789abcdef:0:{Guid.NewGuid():N}", timing));
        Assert.Null(diagnostics.TryBegin($"l12diag:{nonce}:2:{Guid.NewGuid():N}", timing));
        Assert.Null(diagnostics.TryBegin($"l12diag:{nonce}:00:{Guid.NewGuid():N}", timing));
        Assert.Null(diagnostics.TryBegin($"l12diag:{nonce}:0:{Guid.NewGuid()}", timing));

        var first = Assert.IsType<L12SyntheticTransportSample>(
            diagnostics.TryBegin($"l12diag:{nonce}:0:{Guid.NewGuid():N}", timing));
        Assert.Null(diagnostics.TryBegin($"l12diag:{nonce}:0:{Guid.NewGuid():N}", timing));
        var second = Assert.IsType<L12SyntheticTransportSample>(
            diagnostics.TryBegin($"l12diag:{nonce}:1:{Guid.NewGuid():N}", timing));
        first.MissingReply();
        second.MissingReply();

        Assert.True(SpinWait.SpinUntil(() => lines.Count == 3, TimeSpan.FromSeconds(2)));
        Assert.Equal(2, diagnostics.AcceptedSamples);
        Assert.Equal(2, diagnostics.CompletedSamples);
        Assert.All(lines, line =>
        {
            Assert.DoesNotContain(nonce, line, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(line);
            Assert.All(document.RootElement.EnumerateObject(), property =>
                Assert.Equal(JsonValueKind.Number, property.Value.ValueKind));
        });
        using var legacyRow = lines.Select(line => JsonDocument.Parse(line)).First(document =>
            document.RootElement.GetProperty("kind").GetInt32() == 1);
        Assert.Equal(new[]
        {
            "schema", "kind", "slot", "status", "in_fifo_us", "in_depth", "in_bytes",
            "in_remaining", "pre_gate_us", "gate_wait_us", "gate_hold_us", "serialize_us",
            "out_enq_depth", "out_deq_depth", "out_queue_us", "prepare_us", "wire_bytes",
            "wire_delta", "send_us", "commit_us", "server_total_us",
        }, legacyRow.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void TerminalBurstDiagnosticsAreCappedAtThirtyTwoAndDoNotFlushOnAllocationAlone()
    {
        const string nonce = "fedcba9876543210fedcba9876543210";
        var lines = new ConcurrentQueue<string>();
        var diagnostics = L12SyntheticTransportDiagnostics.Create(true,
            $"{L12SyntheticTransportDiagnostics.Mode}:32:{nonce}", lines.Enqueue,
            TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10))!;
        var timing = new L12InboundQueueTiming(1, 2, 1, 100, 0);

        for (var slot = 0; slot < L12SyntheticTransportDiagnostics.MaximumSamples; slot++)
            Assert.NotNull(diagnostics.TryBegin(
                $"l12diag:{nonce}:{slot}:{Guid.NewGuid():N}", timing));
        Assert.Null(diagnostics.TryBegin($"l12diag:{nonce}:32:{Guid.NewGuid():N}", timing));
        Assert.Empty(lines);

        diagnostics.Dispose();
        Assert.Equal(33, lines.Count);
        Assert.Equal(32, lines.Count(line => JsonDocument.Parse(line).RootElement
            .GetProperty("kind").GetInt32() == 1));
    }

    [Fact]
    public async Task TerminalBurstDeadlineSealsIncompleteRowsAndLateCallbacksCannotDoubleOutput()
    {
        const string nonce = "00112233445566778899aabbccddeeff";
        var lines = new ConcurrentQueue<string>();
        var diagnostics = L12SyntheticTransportDiagnostics.Create(true,
            $"{L12SyntheticTransportDiagnostics.Mode}:2:{nonce}", lines.Enqueue,
            TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(10))!;
        var sample = Assert.IsType<L12SyntheticTransportSample>(diagnostics.TryBegin(
            $"l12diag:{nonce}:1:{Guid.NewGuid():N}", new L12InboundQueueTiming(1, 2, 1, 100, 0)));

        Assert.True(SpinWait.SpinUntil(() => lines.Count == 2, TimeSpan.FromSeconds(2)));
        var row = lines.Select(line => JsonDocument.Parse(line)).Single(document =>
            document.RootElement.GetProperty("kind").GetInt32() == 1);
        using (row)
        {
            Assert.Equal(1, row.RootElement.GetProperty("slot").GetInt32());
            Assert.Equal((int)L12SyntheticTransportStatus.Deadline,
                row.RootElement.GetProperty("status").GetInt32());
        }
        using var summary = lines.Select(line => JsonDocument.Parse(line)).Single(document =>
            document.RootElement.GetProperty("kind").GetInt32() == 2);
        Assert.Equal(2, summary.RootElement.GetProperty("expected").GetInt32());
        Assert.Equal(1, summary.RootElement.GetProperty("accepted").GetInt32());
        Assert.Equal(0, summary.RootElement.GetProperty("completed").GetInt32());
        Assert.Equal(1, summary.RootElement.GetProperty("missing").GetInt32());
        Assert.Equal(1, summary.RootElement.GetProperty("deadline").GetInt32());
        var countAtDeadline = lines.Count;
        var sealedSnapshot = Assert.Single(diagnostics.SnapshotForTests());
        sample.Committed(100);
        diagnostics.Dispose();
        await Task.Delay(60);
        Assert.Equal(countAtDeadline, lines.Count);
        Assert.Equal(sealedSnapshot, Assert.Single(diagnostics.SnapshotForTests()));
        Assert.Equal((int)L12SyntheticTransportStatus.Deadline, sealedSnapshot.Status);
    }

    [Fact]
    public async Task TerminalBoundsV2IsCappedBoundToTransportAndPreservesEarlyReceiptOrdering()
    {
        const string nonce = "102132435465768798a9bacbdcedfe0f";
        Assert.Throws<InvalidOperationException>(() => L12SyntheticTransportDiagnostics.Create(false,
            $"{L12SyntheticTransportDiagnostics.BoundsMode}:1:{nonce}"));
        Assert.Throws<InvalidOperationException>(() => L12SyntheticTransportDiagnostics.Create(true,
            $"{L12SyntheticTransportDiagnostics.BoundsMode}:7:{nonce}"));

        var lines = new ConcurrentQueue<string>();
        var transport = Guid.NewGuid();
        using var diagnostics = L12SyntheticTransportDiagnostics.Create(true,
            $"{L12SyntheticTransportDiagnostics.BoundsMode}:1:{nonce}", lines.Enqueue,
            TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(20))!;
        Assert.True(diagnostics.IsBoundsMode);
        Assert.True(diagnostics.AcceptsCommand("playCard"));
        Assert.True(diagnostics.AcceptsCommand("endTurn"));
        Assert.True(diagnostics.AcceptsCommand("surrender"));
        Assert.False(diagnostics.AcceptsCommand("passResponse"));

        var receiveCompletedAt = Stopwatch.GetTimestamp();
        var leaseStartedAt = Stopwatch.GetTimestamp();
        var leaseCompletedAt = Stopwatch.GetTimestamp();
        var enqueuedAt = Stopwatch.GetTimestamp();
        var dequeuedAt = Stopwatch.GetTimestamp();
        var sample = Assert.IsType<L12SyntheticTransportSample>(diagnostics.TryBegin(
            $"l12diag:{nonce}:0:{Guid.NewGuid():N}",
            new L12InboundQueueTiming(enqueuedAt, dequeuedAt, 1, 128, 0,
                receiveCompletedAt, leaseStartedAt, leaseCompletedAt), transport));

        Assert.False(diagnostics.TryAcceptAuthenticatedPingReceipt(Guid.NewGuid(),
            $"{{\"type\":\"ping\",\"diagnosticNonce\":\"{nonce}\",\"diagnosticSlot\":0}}",
            Stopwatch.GetTimestamp()));
        Assert.False(diagnostics.TryAcceptAuthenticatedPingReceipt(transport,
            $"{{\"type\":\"ping\",\"diagnosticNonce\":\"{nonce}\",\"diagnosticSlot\":0.0}}",
            Stopwatch.GetTimestamp()));
        Assert.False(diagnostics.TryAcceptAuthenticatedPingReceipt(transport,
            "{\"type\":\"ping\",\"diagnosticNonce\":\"ffffffffffffffffffffffffffffffff\",\"diagnosticSlot\":0}",
            Stopwatch.GetTimestamp()));
        Assert.True(diagnostics.TryAcceptAuthenticatedPingReceipt(transport,
            $"{{\"type\":\"ping\",\"diagnosticNonce\":\"{nonce}\",\"diagnosticSlot\":0}}",
            Stopwatch.GetTimestamp()));
        Assert.False(diagnostics.TryAcceptAuthenticatedPingReceipt(transport,
            $"{{\"type\":\"ping\",\"diagnosticNonce\":\"{nonce}\",\"diagnosticSlot\":0}}",
            Stopwatch.GetTimestamp()));
        await Task.Delay(40);
        Assert.Empty(lines); // A receipt alone cannot finish a sample before its send commits.

        var sendStartedAt = Stopwatch.GetTimestamp();
        var sendCompletedAt = Stopwatch.GetTimestamp();
        sample.Sent(sendStartedAt, sendCompletedAt);
        sample.Committed(Stopwatch.GetTimestamp());

        Assert.True(SpinWait.SpinUntil(() => lines.Count == 2, TimeSpan.FromSeconds(2)));
        var rowLine = lines.Single(line => JsonDocument.Parse(line).RootElement
            .GetProperty("kind").GetInt32() == 1);
        Assert.DoesNotContain(nonce, rowLine, StringComparison.Ordinal);
        Assert.DoesNotContain(transport.ToString("N"), rowLine, StringComparison.OrdinalIgnoreCase);
        using var row = JsonDocument.Parse(rowLine);
        Assert.Equal(2, row.RootElement.GetProperty("schema").GetInt32());
        Assert.Equal(1, row.RootElement.GetProperty("receive_valid").GetInt32());
        Assert.Equal(1, row.RootElement.GetProperty("lease_valid").GetInt32());
        Assert.Equal(1, row.RootElement.GetProperty("receipt_valid").GetInt32());
        Assert.Equal(0, row.RootElement.GetProperty("receipt_after_send_valid").GetInt32());
        Assert.True(row.RootElement.GetProperty("receipt_rel_us").GetInt64() >= 0);
        Assert.All(row.RootElement.EnumerateObject(), property =>
            Assert.Equal(JsonValueKind.Number, property.Value.ValueKind));
    }

    [Fact]
    public async Task TerminalBoundsV2WaitsPastQuietPeriodAndHardDeadlineFlushesOnlyOnce()
    {
        const string nonce = "ffeeddccbbaa99887766554433221100";
        var lines = new ConcurrentQueue<string>();
        var transport = Guid.NewGuid();
        var diagnostics = L12SyntheticTransportDiagnostics.Create(true,
            $"{L12SyntheticTransportDiagnostics.BoundsMode}:1:{nonce}", lines.Enqueue,
            TimeSpan.FromMilliseconds(140), TimeSpan.FromMilliseconds(20))!;
        var receiveCompletedAt = Stopwatch.GetTimestamp();
        var enqueuedAt = Stopwatch.GetTimestamp();
        var sample = Assert.IsType<L12SyntheticTransportSample>(diagnostics.TryBegin(
            $"l12diag:{nonce}:0:{Guid.NewGuid():N}",
            new L12InboundQueueTiming(enqueuedAt, Stopwatch.GetTimestamp(), 1, 128, 0,
                receiveCompletedAt), transport));
        var sendStartedAt = Stopwatch.GetTimestamp();
        sample.Sent(sendStartedAt, Stopwatch.GetTimestamp());
        sample.Committed(Stopwatch.GetTimestamp());

        await Task.Delay(60);
        Assert.Empty(lines); // v1's quiet-period flush must not truncate a v2 receipt window.
        Assert.True(SpinWait.SpinUntil(() => lines.Count == 2, TimeSpan.FromSeconds(2)));
        var rowLine = lines.Single(line => JsonDocument.Parse(line).RootElement
            .GetProperty("kind").GetInt32() == 1);
        using (var row = JsonDocument.Parse(rowLine))
        {
            Assert.Equal((int)L12SyntheticTransportStatus.Sent,
                row.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(0, row.RootElement.GetProperty("receipt_valid").GetInt32());
        }
        using (var summary = JsonDocument.Parse(lines.Single(line => JsonDocument.Parse(line).RootElement
                   .GetProperty("kind").GetInt32() == 2)))
            Assert.Equal(1, summary.RootElement.GetProperty("receipt_missing").GetInt32());

        var countAtDeadline = lines.Count;
        Assert.False(diagnostics.TryAcceptAuthenticatedPingReceipt(transport,
            $"{{\"type\":\"ping\",\"diagnosticNonce\":\"{nonce}\",\"diagnosticSlot\":0}}",
            Stopwatch.GetTimestamp()));
        diagnostics.Dispose();
        await Task.Delay(50);
        Assert.Equal(countAtDeadline, lines.Count);
    }

    [Fact]
    public async Task TerminalBoundsV2RejectsLateCompletionAndReceiptWhenTimerCallbackIsGateDelayed()
    {
        const string nonce = "01230123012301230123012301230123";
        var lines = new ConcurrentQueue<string>();
        var transport = Guid.NewGuid();
        var diagnostics = L12SyntheticTransportDiagnostics.Create(true,
            $"{L12SyntheticTransportDiagnostics.BoundsMode}:1:{nonce}", lines.Enqueue,
            TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(10))!;
        var ownerGate = typeof(L12SyntheticTransportDiagnostics)
            .GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(diagnostics)!;

        lock (ownerGate)
        {
            var receiveCompletedAt = Stopwatch.GetTimestamp();
            var sample = Assert.IsType<L12SyntheticTransportSample>(diagnostics.TryBegin(
                $"l12diag:{nonce}:0:{Guid.NewGuid():N}",
                new L12InboundQueueTiming(Stopwatch.GetTimestamp(), Stopwatch.GetTimestamp(),
                    1, 128, 0, receiveCompletedAt), transport));
            Thread.Sleep(80); // Keep the timer callback behind the owner gate past the absolute cutoff.
            var lateAt = Stopwatch.GetTimestamp();
            sample.Sent(lateAt, lateAt);
            sample.Committed(lateAt);
            Assert.False(diagnostics.TryAcceptAuthenticatedPingReceipt(transport,
                $"{{\"type\":\"ping\",\"diagnosticNonce\":\"{nonce}\",\"diagnosticSlot\":0}}",
                lateAt));
            Assert.Empty(lines);
        }

        Assert.True(SpinWait.SpinUntil(() => lines.Count == 2, TimeSpan.FromSeconds(2)));
        using (var row = JsonDocument.Parse(lines.Single(line => JsonDocument.Parse(line).RootElement
                   .GetProperty("kind").GetInt32() == 1)))
        {
            Assert.Equal((int)L12SyntheticTransportStatus.Deadline,
                row.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(0, row.RootElement.GetProperty("receipt_valid").GetInt32());
        }
        using (var summary = JsonDocument.Parse(lines.Single(line => JsonDocument.Parse(line).RootElement
                   .GetProperty("kind").GetInt32() == 2)))
        {
            Assert.Equal(0, summary.RootElement.GetProperty("completed").GetInt32());
            Assert.Equal(1, summary.RootElement.GetProperty("deadline").GetInt32());
            Assert.Equal(1, summary.RootElement.GetProperty("receipt_missing").GetInt32());
        }
        var flushedCount = lines.Count;
        diagnostics.Dispose();
        await Task.Delay(60);
        Assert.Equal(flushedCount, lines.Count);
    }

    [Fact]
    public async Task TerminalBoundsV2AcceptsPreDeadlineReceiptTimestampProcessedAfterCutoff()
    {
        const string nonce = "32103210321032103210321032103210";
        var lines = new ConcurrentQueue<string>();
        var transport = Guid.NewGuid();
        var diagnostics = L12SyntheticTransportDiagnostics.Create(true,
            $"{L12SyntheticTransportDiagnostics.BoundsMode}:1:{nonce}", lines.Enqueue,
            TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(10))!;
        var ownerGate = typeof(L12SyntheticTransportDiagnostics)
            .GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(diagnostics)!;

        lock (ownerGate)
        {
            var receiveCompletedAt = Stopwatch.GetTimestamp();
            var sample = Assert.IsType<L12SyntheticTransportSample>(diagnostics.TryBegin(
                $"l12diag:{nonce}:0:{Guid.NewGuid():N}",
                new L12InboundQueueTiming(Stopwatch.GetTimestamp(), Stopwatch.GetTimestamp(),
                    1, 128, 0, receiveCompletedAt), transport));
            var sentAt = Stopwatch.GetTimestamp();
            sample.Sent(sentAt, sentAt);
            sample.Committed(sentAt);
            var receivedBeforeDeadline = Stopwatch.GetTimestamp();
            Thread.Sleep(80); // Processing is late, but the server captured receipt time before cutoff.
            Assert.True(diagnostics.TryAcceptAuthenticatedPingReceipt(transport,
                $"{{\"type\":\"ping\",\"diagnosticNonce\":\"{nonce}\",\"diagnosticSlot\":0}}",
                receivedBeforeDeadline));
            Assert.Empty(lines);
        }

        Assert.True(SpinWait.SpinUntil(() => lines.Count == 2, TimeSpan.FromSeconds(2)));
        using (var row = JsonDocument.Parse(lines.Single(line => JsonDocument.Parse(line).RootElement
                   .GetProperty("kind").GetInt32() == 1)))
        {
            Assert.Equal((int)L12SyntheticTransportStatus.Sent,
                row.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(1, row.RootElement.GetProperty("receipt_valid").GetInt32());
            Assert.Equal(1, row.RootElement.GetProperty("receipt_after_send_valid").GetInt32());
        }
        var flushedCount = lines.Count;
        diagnostics.Dispose();
        await Task.Delay(60);
        Assert.Equal(flushedCount, lines.Count);
    }

    [Fact]
    public async Task BusinessQueueIsSingleConsumerFifo()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<string>();
        var active = 0;
        var maximumActive = 0;
        var inbound = new L12InboundConnection(async json =>
        {
            var current = Interlocked.Increment(ref active);
            maximumActive = Math.Max(maximumActive, current);
            observed.Add(json);
            if (json == "first")
            {
                entered.TrySetResult();
                await release.Task;
            }
            Interlocked.Decrement(ref active);
        });

        try
        {
            try
            {
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("first"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("second"));
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("third"));
            }
            finally { release.TrySetResult(); }
            await inbound.CompleteAsync(drain: true).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(["first", "second", "third"], observed);
            Assert.Equal(1, maximumActive);
            Assert.Equal(0, inbound.RetainedMessages);
            Assert.Equal(0, inbound.RetainedBytes);
        }
        finally
        {
            release.TrySetResult();
            inbound.StopAcceptingAndCancelPending();
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task TimedBusinessQueuePreservesFifoAndReportsAcceptedCapacity()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<(string Json, L12InboundQueueTiming Timing)>();
        await using var inbound = new L12InboundConnection(async (json, timing) =>
        {
            observed.Add((json, timing));
            if (json != "first") return;
            entered.TrySetResult();
            await release.Task;
        });

        Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("first"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("second"));
        Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("third"));
        release.TrySetResult();
        await inbound.CompleteAsync(drain: true).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["first", "second", "third"], observed.Select(item => item.Json));
        Assert.Equal([1, 2, 3], observed.Select(item => item.Timing.AcceptedDepth));
        Assert.All(observed, item => Assert.True(item.Timing.DequeuedAt >= item.Timing.EnqueuedAt));
        Assert.All(observed, item => Assert.Equal(0, item.Timing.ReceiveCompletedAt));
        Assert.All(observed, item => Assert.Equal(0, item.Timing.LeaseStartedAt));
        Assert.All(observed, item => Assert.Equal(0, item.Timing.LeaseCompletedAt));
        Assert.Equal(0, inbound.RetainedMessages);
        Assert.True(inbound.MaximumObservedMessages <= L12InboundConnection.MaximumRetainedMessages);
    }

    [Fact]
    public async Task TimedBusinessQueueReportsReceiveLeaseAndEnqueueBoundariesInOrder()
    {
        var coordinator = new L12DeploymentDrainCoordinator(Guid.NewGuid().ToString("N"),
            new string('a', 40), new OpenFenceStore());
        var observed = new TaskCompletionSource<L12InboundQueueTiming>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var inbound = new L12InboundConnection((_, timing) =>
        {
            observed.TrySetResult(timing);
            return Task.CompletedTask;
        }, leaseAcquirer: _ => coordinator.TryAcquireAdmission(out var lease) ? lease : null);

        var receiveCompletedAt = Stopwatch.GetTimestamp();
        Assert.Equal(L12InboundEnqueueResult.Accepted,
            inbound.TryEnqueue("timed", receiveCompletedAt));
        var timing = await observed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await inbound.CompleteAsync(drain: true).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(receiveCompletedAt, timing.ReceiveCompletedAt);
        Assert.True(timing.LeaseStartedAt >= timing.ReceiveCompletedAt);
        Assert.True(timing.LeaseCompletedAt >= timing.LeaseStartedAt);
        Assert.True(timing.EnqueuedAt >= timing.LeaseCompletedAt);
        Assert.True(timing.DequeuedAt >= timing.EnqueuedAt);
        Assert.Equal(0, coordinator.Snapshot().AdmissionLeases);
        Assert.Equal(0, inbound.RetainedMessages);
    }

    [Fact]
    public async Task TimedBusinessQueueKeepsCapacityAndPendingCancellationSemantics()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<string>();
        var inbound = new L12InboundConnection(async (json, _) =>
        {
            observed.Add(json);
            entered.TrySetResult();
            await release.Task;
        });
        try
        {
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("executing"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            for (var index = 1; index < L12InboundConnection.MaximumRetainedMessages; index++)
                Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue($"pending-{index}"));
            Assert.Equal(L12InboundEnqueueResult.MessageLimit, inbound.TryEnqueue("overflow"));
            Assert.Equal(L12InboundConnection.MaximumRetainedMessages, inbound.RetainedMessages);

            inbound.StopAcceptingAndCancelPending();
            Assert.Equal(1, inbound.RetainedMessages);
            Assert.Equal(L12InboundEnqueueResult.Completed, inbound.TryEnqueue("late"));
            release.TrySetResult();
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(["executing"], observed);
            Assert.Equal(0, inbound.RetainedMessages);
            Assert.Equal(0, inbound.RetainedBytes);
        }
        finally
        {
            release.TrySetResult();
            inbound.StopAcceptingAndCancelPending();
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task RetainedBudgetIncludesExecutingLegacyMaximumFrame()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handled = 0;
        var inbound = new L12InboundConnection(async _ =>
        {
            Interlocked.Increment(ref handled);
            entered.TrySetResult();
            await release.Task;
        });
        var legacyMaximumAsciiFrame = new string('a', 1024 * 1024);

        try
        {
            try
            {
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue(legacyMaximumAsciiFrame));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(1, inbound.RetainedMessages);
            Assert.Equal(96L + 2L * 1024 * 1024, inbound.RetainedBytes);
            Assert.Equal(L12InboundEnqueueResult.RetainedByteLimit,
                inbound.TryEnqueue(legacyMaximumAsciiFrame));
            Assert.True(inbound.MaximumObservedBytes <= L12InboundConnection.MaximumRetainedBytes);
            }
            finally { release.TrySetResult(); }
            await inbound.CompleteAsync(drain: true).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, handled);
        }
        finally
        {
            release.TrySetResult();
            inbound.StopAcceptingAndCancelPending();
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task MessageCountIncludesExecutingItemAndFailsClosed()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inbound = new L12InboundConnection(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        });

        try
        {
            try
            {
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("gate"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            for (var index = 1; index < L12InboundConnection.MaximumRetainedMessages; index++)
                Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue($"queued-{index}"));
            Assert.Equal(L12InboundEnqueueResult.MessageLimit, inbound.TryEnqueue("overflow"));
            Assert.Equal(L12InboundConnection.MaximumRetainedMessages, inbound.RetainedMessages);
            inbound.StopAcceptingAndCancelPending();
            }
            finally { release.TrySetResult(); }
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            release.TrySetResult();
            inbound.StopAcceptingAndCancelPending();
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task ClosingConnectionCancelsOnlyNotStartedMessages()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<string>();
        var inbound = new L12InboundConnection(async json =>
        {
            observed.Add(json);
            entered.TrySetResult();
            await release.Task;
        });

        try
        {
            try
            {
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("started"));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(L12InboundEnqueueResult.Accepted, inbound.TryEnqueue("old-generation-pending"));
            inbound.StopAcceptingAndCancelPending();
            Assert.Equal(L12InboundEnqueueResult.Completed, inbound.TryEnqueue("late"));
            }
            finally { release.TrySetResult(); }
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(["started"], observed);
            Assert.Equal(0, inbound.RetainedMessages);
        }
        finally
        {
            release.TrySetResult();
            inbound.StopAcceptingAndCancelPending();
            await inbound.CompleteAsync(drain: false).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task AuthenticatedPingBypassesBlockedSyncStateOnSameSocket()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-ping", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var identity = Guid.NewGuid().ToString("N")[..8];
            var account = platform.Register($"iq{identity}", "Password123!").Account!;
            var token = platform.Login(account.Username, "Password123!").Token!;
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var server = new L12WebSocketServer(manager, recorder, platform, catalog);
            await server.StartAsync(0);
            using var socket = new ClientWebSocket();
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                await socket.ConnectAsync(endpoint, CancellationToken.None);
                await SendAsync(socket, new { type = "hello", authToken = token });
                _ = await ReceiveTypeAsync(socket, "session", TimeSpan.FromSeconds(10));
                _ = await ReceiveTypeAsync(socket, "recoveryComplete", TimeSpan.FromSeconds(10));
                await SendAsync(socket, new { type = "createRoom" });
                var room = await ReceiveTypeAsync(socket, "roomState", TimeSpan.FromSeconds(10));
                var gate = RuntimeGate(manager, room.GetProperty("roomCode").GetString()!);
                var holder = HoldGateAsync(gate);
                try
                {
                    await holder.Entered.WaitAsync(TimeSpan.FromSeconds(2));
                    await SendAsync(socket, new { type = "syncState" });
                    await SendAsync(socket, new { type = "ping" });
                    var pong = await ReceiveTypeAsync(socket, "pong", TimeSpan.FromSeconds(1));
                    Assert.Equal("pong", pong.GetProperty("type").GetString());
                }
                finally
                {
                    holder.Release.TrySetResult();
                    await holder.Completion.WaitAsync(TimeSpan.FromSeconds(2));
                }
                _ = await ReceiveTypeAsync(socket, "recoveryComplete", TimeSpan.FromSeconds(10));
                await SendAsync(socket, new { type = "ping" });
                _ = await ReceiveTypeAsync(socket, "pong", TimeSpan.FromSeconds(1));
            }
            finally
            {
                socket.Abort();
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally
        {
            DeleteIsolatedTestDirectory(directory, "l12-inbound-ping");
        }
    }

    [Fact]
    public async Task UnauthenticatedPingStillRequiresHello()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-unauth", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, platform),
                recorder, platform, catalog);
            await server.StartAsync(0);
            using var socket = new ClientWebSocket();
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                await socket.ConnectAsync(new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri,
                    CancellationToken.None);
                await SendAsync(socket, new { type = "ping" });
                var rejected = await ReceiveTypeAsync(socket, "authenticationRequired", TimeSpan.FromSeconds(1));
                Assert.Equal("authentication-required", rejected.GetProperty("reason").GetString());
            }
            finally
            {
                socket.Abort();
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally
        {
            DeleteIsolatedTestDirectory(directory, "l12-inbound-unauth");
        }
    }

    [Fact]
    public async Task NonPingJsonShapesNeverEnterAuthenticatedControlLane()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-shape", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, platform),
                recorder, platform, catalog);
            await server.StartAsync(0);
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                var identity = Guid.NewGuid().ToString("N")[..8];
                var first = platform.Register($"im{identity}", "Password123!");
                using (var malformed = await ConnectAuthenticatedAsync(endpoint, first.Token!))
                {
                    await SendRawAsync(malformed, "{");
                    var error = await ReceiveTypeAsync(malformed, "error", TimeSpan.FromSeconds(2));
                    Assert.Equal("消息不是有效 JSON", error.GetProperty("message").GetString());
                    await SendAsync(malformed, new { type = "ping" });
                    _ = await ReceiveTypeAsync(malformed, "pong", TimeSpan.FromSeconds(2));
                    malformed.Abort();
                }

                var invalidPayloads = new[] { "[]", "42", "{\"type\":42}" };
                for (var index = 0; index < invalidPayloads.Length; index++)
                {
                    var account = platform.Register($"is{identity}{index}", "Password123!");
                    using var invalid = await ConnectAuthenticatedAsync(endpoint, account.Token!);
                    await SendRawAsync(invalid, invalidPayloads[index]);
                    await AssertRejectedOrClosedWithoutPongAsync(invalid, TimeSpan.FromSeconds(2));
                }
            }
            finally
            {
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally { DeleteIsolatedTestDirectory(directory, "l12-inbound-shape"); }
    }

    [Fact]
    public async Task QueuedBusinessRevalidatesCommittedSessionRevocationBeforeRoomMutation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-revoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var identity = Guid.NewGuid().ToString("N")[..8];
            var account = platform.Register($"ir{identity}", "Password123!").Account!;
            var token = platform.Login(account.Username, "Password123!").Token!;
            var actor = platform.AuthenticateTokenSession(token)!;
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var server = new L12WebSocketServer(manager, recorder, platform, catalog);
            await server.StartAsync(0);
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                using var socket = await ConnectAuthenticatedAsync(endpoint, token);
                await SendAsync(socket, new { type = "createRoom" });
                var room = await ReceiveTypeAsync(socket, "roomState", TimeSpan.FromSeconds(10));
                var roomCode = room.GetProperty("roomCode").GetString()!;
                var holder = HoldGateAsync(RuntimeGate(manager, roomCode));
                try
                {
                    await holder.Entered.WaitAsync(TimeSpan.FromSeconds(2));
                    var inbound = RuntimeInbound(server);
                    await SendAsync(socket, new { type = "syncState" });
                    await SendAsync(socket, new { type = "leaveRoom" });
                    Assert.True(SpinWait.SpinUntil(
                        () => HasOneExecutingAndOnePending(inbound), TimeSpan.FromSeconds(2)),
                        "one executing and one pending message were not retained behind the room gate");
                    var revoked = platform.RevokeOwnSession(actor, actor.SessionId);
                    Assert.Equal(1, revoked.RevokedCount);
                    Assert.False(inbound.IsAccepting);
                    Assert.Equal(1, inbound.RetainedMessages);
                }
                finally
                {
                    holder.Release.TrySetResult();
                    await holder.Completion.WaitAsync(TimeSpan.FromSeconds(2));
                }

                await AssertRejectedOrClosedWithoutPongAsync(socket, TimeSpan.FromSeconds(2));
                var replacement = platform.Login(account.Username, "Password123!").Token!;
                using var recovered = await ConnectAuthenticatedAsync(endpoint, replacement);
                await SendAsync(recovered, new { type = "syncState" });
                var recovery = await ReceiveTypeAsync(recovered, "recoveryComplete", TimeSpan.FromSeconds(10));
                Assert.Equal(roomCode, recovery.GetProperty("roomCode").GetString());
                recovered.Abort();
            }
            finally
            {
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally { DeleteIsolatedTestDirectory(directory, "l12-inbound-revoke"); }
    }

    [Fact]
    public async Task ReplacementGenerationCancelsPendingOldSocketBusiness()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-generation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var identity = Guid.NewGuid().ToString("N")[..8];
            var account = platform.Register($"ig{identity}", "Password123!").Account!;
            var token = platform.Login(account.Username, "Password123!").Token!;
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var server = new L12WebSocketServer(manager, recorder, platform, catalog);
            await server.StartAsync(0);
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                using var oldSocket = new ClientWebSocket();
                await oldSocket.ConnectAsync(endpoint, CancellationToken.None);
                await SendAsync(oldSocket, new { type = "hello", authToken = token });
                var oldSession = await ReceiveTypeAsync(oldSocket, "session", TimeSpan.FromSeconds(10));
                _ = await ReceiveTypeAsync(oldSocket, "recoveryComplete", TimeSpan.FromSeconds(10));
                var oldSessionId = oldSession.GetProperty("sessionId").GetGuid();
                var oldGeneration = oldSession.GetProperty("connectionGeneration").GetInt64();
                await SendAsync(oldSocket, new { type = "createRoom" });
                var room = await ReceiveTypeAsync(oldSocket, "roomState", TimeSpan.FromSeconds(10));
                var roomCode = room.GetProperty("roomCode").GetString()!;
                var holder = HoldGateAsync(RuntimeGate(manager, roomCode));
                Task<ClientWebSocket>? replacementTask = null;
                var claimReserved = false;
                try
                {
                    await holder.Entered.WaitAsync(TimeSpan.FromSeconds(2));
                    await SendAsync(oldSocket, new { type = "syncState" });
                    await SendAsync(oldSocket, new { type = "leaveRoom" });
                    Assert.True(SpinWait.SpinUntil(
                        () => RuntimeInbound(server).RetainedMessages >= 2, TimeSpan.FromSeconds(2)));
                    replacementTask = ConnectAuthenticatedAsync(endpoint, token);
                    // A claim reserves its generation before waiting for the room gate, but it
                    // becomes authoritative only after recovery/its durable checkpoint succeeds.
                    // Do not require the old connection to become invalid before that boundary.
                    claimReserved = SpinWait.SpinUntil(
                        () => ReservedGeneration(manager, account.Id) > oldGeneration,
                        TimeSpan.FromSeconds(2));
                }
                finally
                {
                    holder.Release.TrySetResult();
                    await holder.Completion.WaitAsync(TimeSpan.FromSeconds(2));
                }

                Assert.NotNull(replacementTask);
                using var replacement = await replacementTask!.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(claimReserved, "replacement did not reach the original recovery claim boundary");
                Assert.False(manager.IsCurrentConnection(oldSessionId, account.Id, oldGeneration));
                await SendAsync(replacement, new { type = "syncState" });
                var recovery = await ReceiveTypeAsync(replacement, "recoveryComplete", TimeSpan.FromSeconds(10));
                Assert.Equal(roomCode, recovery.GetProperty("roomCode").GetString());
                replacement.Abort();
                oldSocket.Abort();
            }
            finally
            {
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally { DeleteIsolatedTestDirectory(directory, "l12-inbound-generation"); }
    }

    [Fact]
    public async Task InboundMessageLimitDrainsAcceptedFifoThenRejectsOverflowWithoutBusinessAck()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-overload", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var identity = Guid.NewGuid().ToString("N")[..8];
            var account = platform.Register($"io{identity}", "Password123!").Account!;
            var token = platform.Login(account.Username, "Password123!").Token!;
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var server = new L12WebSocketServer(manager, recorder, platform, catalog);
            await server.StartAsync(0);
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                using var socket = await ConnectAuthenticatedAsync(endpoint, token);
                await SendAsync(socket, new { type = "createRoom" });
                var room = await ReceiveTypeAsync(socket, "roomState", TimeSpan.FromSeconds(10));
                var roomCode = room.GetProperty("roomCode").GetString()!;
                var holder = HoldGateAsync(RuntimeGate(manager, roomCode));
                try
                {
                    await holder.Entered.WaitAsync(TimeSpan.FromSeconds(2));
                    var inbound = RuntimeInbound(server);
                    for (var index = 0; index < L12InboundConnection.MaximumRetainedMessages; index++)
                        await SendAsync(socket, new { type = "syncState", requestId = $"accepted-{index}" });
                    Assert.True(SpinWait.SpinUntil(
                        () => inbound.RetainedMessages == L12InboundConnection.MaximumRetainedMessages,
                        TimeSpan.FromSeconds(2)));
                    await SendAsync(socket, new { type = "leaveRoom", requestId = "overflow-must-not-ack" });
                    Assert.True(SpinWait.SpinUntil(() => !inbound.IsAccepting,
                        TimeSpan.FromSeconds(2)), "overflow was not received before releasing the FIFO barrier");
                }
                finally
                {
                    holder.Release.TrySetResult();
                    await holder.Completion.WaitAsync(TimeSpan.FromSeconds(2));
                }

                var recoveryCount = 0;
                JsonElement rejection = default;
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    while (true)
                    {
                        var message = await ReceiveOneAsync(socket, timeout.Token);
                        var type = message.GetProperty("type").GetString();
                        if (type == "recoveryComplete") { recoveryCount++; continue; }
                        if (type == "error" && message.TryGetProperty("code", out var code)
                            && code.GetString() == "inboundQueueCapacityExceeded")
                        {
                            rejection = message;
                            break;
                        }
                    }
                }
                Assert.Equal(L12InboundConnection.MaximumRetainedMessages, recoveryCount);
                Assert.Equal("overflow-must-not-ack", rejection.GetProperty("requestId").GetString());
                Assert.True(rejection.GetProperty("retryWithSameRequestId").GetBoolean());

                var replacementToken = platform.Login(account.Username, "Password123!").Token!;
                using var replacement = await ConnectAuthenticatedAsync(endpoint, replacementToken);
                await SendAsync(replacement, new { type = "syncState" });
                var recovery = await ReceiveTypeAsync(replacement, "recoveryComplete", TimeSpan.FromSeconds(10));
                Assert.Equal(roomCode, recovery.GetProperty("roomCode").GetString());
                replacement.Abort();
                socket.Abort();
            }
            finally
            {
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally { DeleteIsolatedTestDirectory(directory, "l12-inbound-overload"); }
    }

    public static IEnumerable<object[]> InboundByteReconnectCases()
        => Enumerable.Range(0, 20).Select(index => new object[] { index, index % 2 == 1 });

    [Theory]
    [MemberData(nameof(InboundByteReconnectCases))]
    public async Task InboundByteLimitRejectsLongRequestIdWithoutApplyingUnacceptedMutation(
        int attempt, bool renewPlatformSession)
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-bytes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var account = platform.Register($"ib{Guid.NewGuid():N}"[..10], "Password123!").Account!;
            var token = platform.Login(account.Username, "Password123!").Token!;
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var server = new L12WebSocketServer(manager, recorder, platform, catalog);
            await server.StartAsync(0);
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                using var socket = await ConnectAuthenticatedAsync(endpoint, token);
                await SendAsync(socket, new { type = "createRoom" });
                var roomCode = (await ReceiveTypeAsync(socket, "roomState", TimeSpan.FromSeconds(10)))
                    .GetProperty("roomCode").GetString()!;
                var holder = HoldGateAsync(RuntimeGate(manager, roomCode));
                try
                {
                    await holder.Entered.WaitAsync(TimeSpan.FromSeconds(2));
                    // A valid legacy ASCII frame just below the original 1 MiB wire limit is
                    // accepted. A second 128 KiB frame exceeds retained bytes, not item count.
                    await SendAsync(socket, new { type = "syncState", padding = new string('a', 1024 * 1024 - 128) });
                    var inbound = RuntimeInbound(server);
                    Assert.True(SpinWait.SpinUntil(() => inbound.RetainedMessages == 1
                        && inbound.RetainedBytes > 2_000_000, TimeSpan.FromSeconds(2)));
                    await SendAsync(socket, new { type = "leaveRoom", requestId = new string('x', 128 * 1024) });
                    Assert.True(SpinWait.SpinUntil(() => !inbound.IsAccepting, TimeSpan.FromSeconds(2)));
                    Assert.Equal(1, inbound.RetainedMessages);
                    Assert.True(inbound.MaximumObservedBytes <= L12InboundConnection.MaximumRetainedBytes);
                }
                finally
                {
                    holder.Release.TrySetResult();
                    await holder.Completion.WaitAsync(TimeSpan.FromSeconds(2));
                }
                _ = await ReceiveTypeAsync(socket, "recoveryComplete", TimeSpan.FromSeconds(10));
                var rejection = await ReceiveTypeAsync(socket, "error", TimeSpan.FromSeconds(10));
                Assert.Equal("inboundQueueCapacityExceeded", rejection.GetProperty("code").GetString());
                Assert.Equal("retained-byte-limit", rejection.GetProperty("reason").GetString());
                Assert.Equal(JsonValueKind.Null, rejection.GetProperty("requestId").ValueKind);
                Assert.False(rejection.GetProperty("retryWithSameRequestId").GetBoolean());
                var replacementToken = renewPlatformSession
                    ? platform.Login(account.Username, "Password123!").Token! : token;
                using var replacement = await ConnectAuthenticatedAsync(endpoint, replacementToken);
                var owners = Assert.IsType<ConcurrentDictionary<string, Guid>>(typeof(L12WebSocketServer)
                    .GetField("_activeAccountSockets", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(server));
                Assert.True(owners.TryGetValue(account.Id, out var replacementOwner),
                    $"replacement lost its claim before sync in attempt {attempt}");
                await SendAsync(replacement, new { type = "syncState" });
                Assert.Equal(roomCode, (await ReceiveTypeAsync(replacement, "recoveryComplete",
                    TimeSpan.FromSeconds(10))).GetProperty("roomCode").GetString());
                Assert.True(owners.TryGetValue(account.Id, out var ownerAfterSync));
                Assert.Equal(replacementOwner, ownerAfterSync);
                replacement.Abort();
                socket.Abort();
            }
            finally
            {
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally { DeleteIsolatedTestDirectory(directory, "l12-inbound-bytes"); }
    }

    [Fact]
    public async Task SocketClosePreservesExecutingDurableCommandAndCancelsQueuedMutation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-inbound-durable-close", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var platform = new L12PlatformStore(Path.Combine(directory, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var account = platform.Register($"ic{Guid.NewGuid():N}"[..10], "Password123!").Account!;
            Assert.True(platform.SetRole(platform.Login("Admin", "L12master").Account!, account.Id, "admin"));
            var token = platform.Login(account.Username, "Password123!").Token!;
            await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(catalog, recorder, platform);
            var server = new L12WebSocketServer(manager, recorder, platform, catalog);
            await server.StartAsync(0);
            try
            {
                var http = new UriBuilder(Assert.Single(server.Addresses)) { Host = "127.0.0.1" };
                var endpoint = new UriBuilder(http.Uri) { Scheme = "ws", Path = "/ws" }.Uri;
                using var socket = await ConnectAuthenticatedAsync(endpoint, token);
                await SendAsync(socket, new { type = "createSandbox" });
                var roomCode = (await ReceiveTypeAsync(socket, "roomState", TimeSpan.FromSeconds(10)))
                    .GetProperty("roomCode").GetString()!;
                var rooms = (IEnumerable)typeof(L12RoomManager)
                    .GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
                var runtimeRoom = rooms.Cast<object>().Select(entry => entry.GetType().GetProperty("Value")!
                    .GetValue(entry)!).Single(room => (string)room.GetType().GetProperty("Code")!.GetValue(room)! == roomCode);
                var game = (L12GameEngine)runtimeRoom.GetType().GetProperty("Game")!.GetValue(runtimeRoom)!;
                var matchId = game.State.MatchId;
                recorder.StorageFailureInjector = phase =>
                {
                    if (phase != "before-match-command-commit") return;
                    entered.TrySetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("synthetic durable command hold");
                };
                try
                {
                    await SendAsync(socket, new { type = "setResponsePreference", mode = "valid-only", requestId = "durable-first" });
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    var inbound = RuntimeInbound(server);
                    await SendAsync(socket, new { type = "setResponsePreference", mode = "invalid-five-seconds", requestId = "cancel-pending" });
                    Assert.True(SpinWait.SpinUntil(() => inbound.RetainedMessages == 2, TimeSpan.FromSeconds(2)));
                    socket.Abort();
                    Assert.True(SpinWait.SpinUntil(() => !inbound.IsAccepting && inbound.RetainedMessages == 1,
                        TimeSpan.FromSeconds(3)), "socket teardown did not cancel pending work before durable completion");
                }
                finally { release.Set(); }
                Assert.True(SpinWait.SpinUntil(() => platform.ResponsePreference(account.Id) == "valid-only",
                    TimeSpan.FromSeconds(5)), "executing durable command was lost on disconnect");
                await using var reopened = new MatchRecorder(Path.Combine(directory, "matches.db"));
                await reopened.InitializeAsync();
                reopened.AttachCatalog(catalog);
                var recorded = (await reopened.GetMatchAsync(matchId))!;
                var preferences = recorded.Commands.Where(command => command.Command.GetProperty("type")
                    .GetString() == "setResponsePreference").ToArray();
                Assert.Single(preferences);
                Assert.True(preferences[0].Accepted);
                Assert.Equal("valid-only", preferences[0].Command.GetProperty("responseMode").GetString());
                Assert.Equal("valid-only", (await reopened.LoadJournalEngineAsync(matchId))!.Engine.ResponseModeFor(0));
            }
            finally
            {
                release.Set();
                recorder.StorageFailureInjector = null;
                try { await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
        }
        finally { release.Set(); DeleteIsolatedTestDirectory(directory, "l12-inbound-durable-close"); }
    }

    private static async Task SendAsync(ClientWebSocket socket, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, WireJson);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task SendRawAsync(ClientWebSocket socket, string payload)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<ClientWebSocket> ConnectAuthenticatedAsync(Uri endpoint, string token)
    {
        var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(endpoint, CancellationToken.None);
            await SendAsync(socket, new { type = "hello", authToken = token });
            _ = await ReceiveTypeAsync(socket, "session", TimeSpan.FromSeconds(10));
            _ = await ReceiveTypeAsync(socket, "recoveryComplete", TimeSpan.FromSeconds(10));
            return socket;
        }
        catch
        {
            socket.Abort();
            socket.Dispose();
            throw;
        }
    }

    private static async Task AssertRejectedOrClosedWithoutPongAsync(ClientWebSocket socket, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            var buffer = new byte[1024];
            var result = await socket.ReceiveAsync(buffer, cancellation.Token);
            if (result.MessageType == WebSocketMessageType.Close) return;
            using var document = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
            var root = document.RootElement;
            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            Assert.True(root.TryGetProperty("type", out var type));
            Assert.Equal(JsonValueKind.String, type.ValueKind);
            Assert.NotEqual("pong", type.GetString());
        }
        catch (WebSocketException)
        {
            // The current Dispatch error boundary may abort malformed object shapes. The contract
            // here only freezes that these shapes cannot be mistaken for control-lane ping.
        }
    }

    private static async Task<JsonElement> ReceiveTypeAsync(ClientWebSocket socket, string expected,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var buffer = new byte[16 * 1024];
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("socket closed before expected message");
                stream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            using var document = JsonDocument.Parse(stream.ToArray());
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == expected) return root.Clone();
        }
        throw new InvalidDataException($"missing WebSocket message {expected}");
    }

    private static async Task<JsonElement> ReceiveOneAsync(ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("socket closed before expected message");
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static SemaphoreSlim RuntimeGate(L12RoomManager manager, string roomCode)
    {
        var rooms = (IEnumerable)typeof(L12RoomManager)
            .GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        foreach (var entry in rooms)
        {
            var room = entry!.GetType().GetProperty("Value")!.GetValue(entry)!;
            if (!string.Equals((string)room.GetType().GetProperty("Code")!.GetValue(room)!, roomCode,
                    StringComparison.OrdinalIgnoreCase)) continue;
            return (SemaphoreSlim)room.GetType().GetProperty("Gate")!.GetValue(room)!;
        }
        throw new InvalidOperationException($"missing runtime room {roomCode}");
    }

    private static bool HasOneExecutingAndOnePending(L12InboundConnection inbound)
    {
        var gate = typeof(L12InboundConnection).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inbound)!;
        lock (gate)
        {
            var pending = (ICollection)typeof(L12InboundConnection)
                .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inbound)!;
            return inbound.IsAccepting && inbound.RetainedMessages == 2 && pending.Count == 1;
        }
    }

    private static L12InboundConnection RuntimeInbound(L12WebSocketServer server)
    {
        var connections = (IEnumerable)typeof(L12WebSocketServer)
            .GetField("_inboundConnections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;
        foreach (var entry in connections)
            return (L12InboundConnection)entry!.GetType().GetProperty("Value")!.GetValue(entry)!;
        throw new InvalidOperationException("missing runtime inbound connection");
    }

    private static long ReservedGeneration(L12RoomManager manager, string accountId)
        => ((System.Collections.Concurrent.ConcurrentDictionary<string, long>)typeof(L12RoomManager)
            .GetField("_accountConnectionGenerations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager)!).GetValueOrDefault(accountId);

    private static GateHolder HoldGateAsync(SemaphoreSlim gate)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = Task.Run(async () =>
        {
            await gate.WaitAsync();
            entered.TrySetResult();
            try { await release.Task; }
            finally { gate.Release(); }
        });
        return new GateHolder(entered.Task, release, completion);
    }

    private static void DeleteIsolatedTestDirectory(string directory, string expectedParentName)
    {
        var target = new DirectoryInfo(Path.GetFullPath(directory));
        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), expectedParentName));
        if (target.Parent is null
            || !string.Equals(target.Parent.FullName.TrimEnd(Path.DirectorySeparatorChar),
                expectedParent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || target.Name.Length != 32
            || !Guid.TryParseExact(target.Name, "N", out _))
            throw new InvalidOperationException($"refusing to delete unexpected test directory: {target.FullName}");

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (target.Exists) target.Delete(recursive: true);
    }

    private sealed class OpenFenceStore : IL12DeploymentDrainFenceStore
    {
        public L12DeploymentDrainFenceLoadResult Load() => L12DeploymentDrainFenceLoadResult.Missing;
        public bool TryWrite(L12DeploymentPersistedFence fence) => true;
        public bool TryClear(string operationId, string targetCommit) => true;
    }

    private sealed record GateHolder(Task Entered, TaskCompletionSource Release, Task Completion);
}
