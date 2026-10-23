using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class TerminalRecipientDiagnosticsTests
{
    private const string Mode = "terminal-recipients-v4";
    private const string Nonce = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void ExplicitEphemeralOnlyModeIsCappedAt32CommandsAndAcceptsOnlySurrender()
    {
        Assert.Null(L12SyntheticTransportDiagnostics.Create(false, null));
        Assert.Throws<InvalidOperationException>(() => L12SyntheticTransportDiagnostics.Create(false, $"{Mode}:1:{Nonce}"));
        foreach (var count in new[] { 0, 33 })
            Assert.Throws<InvalidOperationException>(() => L12SyntheticTransportDiagnostics.Create(true, $"{Mode}:{count}:{Nonce}"));
        foreach (var count in new[] { 1, 30, 32 })
        {
            using var diagnostics = L12SyntheticTransportDiagnostics.Create(true, $"{Mode}:{count}:{Nonce}", _ => { })!;
            Assert.True(diagnostics.IsRecipientsMode);
            Assert.True(diagnostics.IsRecorderMode);
            Assert.True(diagnostics.AcceptsCommand("surrender"));
            foreach (var command in new[] { "attack", "playCard", "endTurn", "hello", "Surrender" })
                Assert.False(diagnostics.AcceptsCommand(command));
        }
    }

    [Fact]
    public void Full32CommandCohortEmitsExactly64NumericRecipientRowsWithoutTransportOrPayloadData()
    {
        var lines = new ConcurrentQueue<string>();
        using var diagnostics = L12SyntheticTransportDiagnostics.Create(true, $"{Mode}:32:{Nonce}", lines.Enqueue)!;
        var transports = new List<Guid>();
        for (var slot = 0; slot < 32; slot++)
        {
            var actorId = Guid.NewGuid();
            var peerId = Guid.NewGuid();
            transports.AddRange([actorId, peerId]);
            var origin = Stopwatch.GetTimestamp();
            var actor = Begin(diagnostics, slot, actorId, origin);
            actor.BindRecipients([actorId, peerId]);
            Assert.Same(actor, actor.TakeRecipient(actorId, true));
            var peer = Assert.IsType<L12SyntheticTransportSample>(actor.TakeRecipient(peerId, true));
            actor.Prepared(origin, origin, 200_000, false);
            peer.Prepared(origin, origin, 210_000, false);
            actor.Sent(origin, origin);
            peer.Sent(origin, origin);
            actor.Committed(origin);
            peer.Committed(origin);
            Assert.True(Receipt(diagnostics, actorId, slot, "0", origin + 1));
            Assert.True(Receipt(diagnostics, peerId, slot, "1", origin + 1));
            actor.FinishRecipients();
        }
        Assert.Null(diagnostics.TryBegin($"l12diag:{Nonce}:32:{Guid.NewGuid():N}", default, Guid.NewGuid()));
        Assert.Null(diagnostics.TryBegin($"l12diag:{Nonce}:0:{Guid.NewGuid():N}", default, transports[0]));
        diagnostics.Dispose();
        Assert.Equal(65, lines.Count);
        var rows = diagnostics.SnapshotForTests();
        Assert.Equal(64, rows.Count);
        Assert.Equal(32, rows.Count(row => row.RecipientRole == 0));
        Assert.Equal(32, rows.Count(row => row.RecipientRole == 1));
        foreach (var line in lines)
        {
            Assert.DoesNotContain(Nonce, line, StringComparison.Ordinal);
            Assert.All(transports, transport => Assert.DoesNotContain(transport.ToString("N"), line, StringComparison.OrdinalIgnoreCase));
            using var json = JsonDocument.Parse(line);
            Assert.Equal(4, json.RootElement.GetProperty("schema").GetInt32());
            Assert.All(json.RootElement.EnumerateObject(), property => Assert.Equal(JsonValueKind.Number, property.Value.ValueKind));
        }
        using var summary = JsonDocument.Parse(lines.Last());
        Assert.Equal(32, summary.RootElement.GetProperty("accepted_commands").GetInt32());
        Assert.Equal(64, summary.RootElement.GetProperty("observed_recipients").GetInt32());
        Assert.Equal(64, summary.RootElement.GetProperty("completed").GetInt32());
        Assert.Equal(64, summary.RootElement.GetProperty("receipts").GetInt32());
        Assert.Equal(0, summary.RootElement.GetProperty("missing").GetInt32());
        Assert.Equal(0, summary.RootElement.GetProperty("deadline").GetInt32());
    }

    [Fact]
    public void AuthorityBindingIgnoresSpectatorsNonStateFramesAndForeignSamples()
    {
        using var diagnostics = L12SyntheticTransportDiagnostics.Create(true, $"{Mode}:1:{Nonce}", _ => { })!;
        var actorId = Guid.NewGuid();
        var peerId = Guid.NewGuid();
        var spectatorId = Guid.NewGuid();
        var actor = Begin(diagnostics, 0, actorId, Stopwatch.GetTimestamp());
        new L12SyntheticTransportSample(diagnostics, 0, default, spectatorId).BindRecipients([spectatorId, peerId]);
        Assert.Single(diagnostics.SnapshotForTests());
        actor.BindRecipients([peerId, actorId]);
        Assert.Null(actor.TakeRecipient(spectatorId, true));
        Assert.Null(actor.TakeRecipient(actorId, false));
        Assert.Null(actor.TakeRecipient(peerId, false));
        Assert.Same(actor, actor.TakeRecipient(actorId, true));
        var peer = Assert.IsType<L12SyntheticTransportSample>(actor.TakeRecipient(peerId, true));
        Assert.NotSame(actor, peer);
        Assert.Null(peer.RecorderObserver);
        Assert.Null(actor.TakeRecipient(actorId, true));
        Assert.Null(actor.TakeRecipient(peerId, true));
        actor.FinishRecipients();
        Assert.All(diagnostics.SnapshotForTests(), row => Assert.Equal((int)L12SyntheticTransportStatus.Pending, row.Status));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("one")]
    [InlineData("three")]
    [InlineData("duplicate")]
    [InlineData("unrelated")]
    public void InvalidPlayerBindingDoesNotGuessThePeer(string arm)
    {
        var lines = new ConcurrentQueue<string>();
        using var diagnostics = L12SyntheticTransportDiagnostics.Create(true, $"{Mode}:1:{Nonce}", lines.Enqueue)!;
        var actorId = Guid.NewGuid();
        var peerId = Guid.NewGuid();
        var actor = Begin(diagnostics, 0, actorId, Stopwatch.GetTimestamp());
        actor.BindRecipients(arm switch
        {
            "empty" => [], "one" => [actorId], "three" => [actorId, peerId, Guid.NewGuid()],
            "duplicate" => [actorId, actorId], "unrelated" => [Guid.NewGuid(), peerId],
            _ => throw new ArgumentOutOfRangeException(nameof(arm)),
        });
        Assert.Null(actor.TakeRecipient(peerId, true));
        actor.FinishRecipients();
        Assert.Equal((int)L12SyntheticTransportStatus.MissingReply, Assert.Single(diagnostics.SnapshotForTests()).Status);
        diagnostics.Dispose();
        using var summary = JsonDocument.Parse(lines.Last());
        Assert.Equal(1, summary.RootElement.GetProperty("missing").GetInt32());
        Assert.Equal(1, summary.RootElement.GetProperty("invalid").GetInt32());
    }

    [Theory]
    [InlineData("-0")]
    [InlineData("0.0")]
    [InlineData("1.0")]
    [InlineData("1e0")]
    [InlineData("2")]
    [InlineData("\"1\"")]
    public void RecipientReceiptRequiresStrictRoleAndExactAuthenticatedTransport(string malformedRole)
    {
        using var diagnostics = L12SyntheticTransportDiagnostics.Create(true, $"{Mode}:1:{Nonce}", _ => { })!;
        var actorId = Guid.NewGuid();
        var peerId = Guid.NewGuid();
        var origin = Stopwatch.GetTimestamp();
        var actor = Begin(diagnostics, 0, actorId, origin);
        actor.BindRecipients([actorId, peerId]);
        Assert.False(Receipt(diagnostics, peerId, 0, malformedRole, origin + 1));
        Assert.False(Receipt(diagnostics, peerId, 0, "0", origin + 1));
        Assert.False(Receipt(diagnostics, actorId, 0, "1", origin + 1));
        Assert.False(Receipt(diagnostics, Guid.NewGuid(), 0, "1", origin + 1));
        Assert.False(diagnostics.TryAcceptAuthenticatedPingReceipt(peerId,
            $"{{\"type\":\"ping\",\"diagnosticNonce\":\"{Nonce}\",\"diagnosticSlot\":0}}", origin + 1));
        Assert.True(Receipt(diagnostics, peerId, 0, "1", origin + 2));
        Assert.False(Receipt(diagnostics, peerId, 0, "1", origin + 3));
        Assert.True(Receipt(diagnostics, actorId, 0, "0", origin + 2));
        Assert.All(diagnostics.SnapshotForTests(), row => Assert.Equal(1, row.ReceiptValid));
    }

    [Fact]
    public void RecipientsShareCommandBoundariesButKeepOutboundCostsAndReceiptsIndependent()
    {
        using var diagnostics = L12SyntheticTransportDiagnostics.Create(true, $"{Mode}:1:{Nonce}", _ => { })!;
        var actorId = Guid.NewGuid();
        var peerId = Guid.NewGuid();
        var origin = Stopwatch.GetTimestamp();
        long At(int microseconds) => origin + Stopwatch.Frequency * microseconds / 1_000_000;
        var actor = Begin(diagnostics, 0, actorId, origin);
        actor.GateWaitStarted(At(1));
        actor.GateAcquired(At(2));
        actor.BindRecipients([actorId, peerId]);
        actor.RecorderObserver!(L12SyntheticRecorderObservation.Append, L12SyntheticRecorderObservation.WaitStarted, At(3));
        actor.RecorderObserver!(L12SyntheticRecorderObservation.Append, L12SyntheticRecorderObservation.Acquired, At(4));
        actor.RecorderObserver!(L12SyntheticRecorderObservation.Append, L12SyntheticRecorderObservation.HoldEnded, At(5));
        actor.GateReleased(At(6));
        Assert.Same(actor, actor.TakeRecipient(actorId, true));
        var peer = Assert.IsType<L12SyntheticTransportSample>(actor.TakeRecipient(peerId, true));
        actor.OutboundEnqueued(At(10), 1); actor.OutboundDequeued(At(20), 0);
        peer.OutboundEnqueued(At(30), 3); peer.OutboundDequeued(At(130), 2);
        actor.Prepared(At(20), At(25), 200_000, false);
        peer.Prepared(At(130), At(180), 210_000, false);
        actor.Sent(At(25), At(26)); actor.Committed(At(27));
        peer.Sent(At(180), At(190)); peer.Committed(At(191));
        Assert.True(Receipt(diagnostics, actorId, 0, "0", At(200)));
        Assert.True(Receipt(diagnostics, peerId, 0, "1", At(300)));
        var rows = diagnostics.SnapshotForTests().OrderBy(row => row.RecipientRole).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(rows[0].GateWaitMicroseconds, rows[1].GateWaitMicroseconds);
        Assert.Equal(rows[0].GateHoldMicroseconds, rows[1].GateHoldMicroseconds);
        Assert.Equal(rows[0].RecorderAppend, rows[1].RecorderAppend);
        Assert.Equal(1, rows[1].RecorderAppend.Valid);
        Assert.True(rows[1].OutboundQueueMicroseconds > rows[0].OutboundQueueMicroseconds);
        Assert.True(rows[1].PrepareMicroseconds > rows[0].PrepareMicroseconds);
        Assert.True(rows[1].ReceiptReceivedRelativeMicroseconds > rows[0].ReceiptReceivedRelativeMicroseconds);
        Assert.Equal(200_000, rows[0].WireBytes);
        Assert.Equal(210_000, rows[1].WireBytes);
    }

    [Fact]
    public async Task AbsoluteDeadlineSealsBothRecipientsAndCannotBeReopenedByLateCallbacks()
    {
        var lines = new ConcurrentQueue<string>();
        using var diagnostics = L12SyntheticTransportDiagnostics.Create(true, $"{Mode}:1:{Nonce}", lines.Enqueue,
            TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(10))!;
        var actorId = Guid.NewGuid();
        var peerId = Guid.NewGuid();
        var actor = Begin(diagnostics, 0, actorId, Stopwatch.GetTimestamp());
        actor.BindRecipients([actorId, peerId]);
        Assert.True(SpinWait.SpinUntil(() => lines.Count == 3, TimeSpan.FromSeconds(2)));
        var before = diagnostics.SnapshotForTests().ToArray();
        Assert.All(before, row => Assert.Equal((int)L12SyntheticTransportStatus.Deadline, row.Status));
        Assert.Null(actor.TakeRecipient(peerId, true));
        actor.Committed(Stopwatch.GetTimestamp());
        Assert.False(Receipt(diagnostics, peerId, 0, "1", Stopwatch.GetTimestamp()));
        actor.BindRecipients([actorId, Guid.NewGuid()]);
        actor.FinishRecipients();
        await Task.Yield();
        Assert.Equal(before, diagnostics.SnapshotForTests());
        Assert.Equal(3, lines.Count);
    }

    private static L12SyntheticTransportSample Begin(L12SyntheticTransportDiagnostics diagnostics, int slot,
        Guid transport, long origin)
        => Assert.IsType<L12SyntheticTransportSample>(diagnostics.TryBegin($"l12diag:{Nonce}:{slot}:{Guid.NewGuid():N}",
            new L12InboundQueueTiming(origin, origin, 1, 128, 0, origin), transport));

    private static bool Receipt(L12SyntheticTransportDiagnostics diagnostics, Guid transport, int slot,
        string role, long timestamp)
        => diagnostics.TryAcceptAuthenticatedPingReceipt(transport,
            $"{{\"type\":\"ping\",\"diagnosticNonce\":\"{Nonce}\",\"diagnosticSlot\":{slot},\"diagnosticRecipient\":{role}}}", timestamp);
}
