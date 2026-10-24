using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TwelveLegions.Server;

internal enum L12SyntheticTransportStatus
{
    Pending = 0,
    Sent = 1,
    QueueRejected = 2,
    Discarded = 3,
    SendFault = 4,
    Deadline = 5,
    MissingReply = 6,
}

internal readonly record struct L12SyntheticRecorderSnapshot(int Count, int Valid,
    long WaitStartedRelativeMicroseconds, long AcquiredRelativeMicroseconds,
    long HoldEndedRelativeMicroseconds);

internal readonly record struct L12SyntheticTransportSnapshot(
    int Slot,
    int Status,
    long InboundFifoMicroseconds,
    int InboundAcceptedDepth,
    long InboundAcceptedRetainedBytes,
    int InboundRemainingDepth,
    long PreGateMicroseconds,
    long GateWaitMicroseconds,
    long GateHoldMicroseconds,
    long SerializeMicroseconds,
    int OutboundEnqueueDepth,
    int OutboundDequeueDepth,
    long OutboundQueueMicroseconds,
    long PrepareMicroseconds,
    int WireBytes,
    int WireDelta,
    long SendMicroseconds,
    long CommitMicroseconds,
    long ServerTotalMicroseconds,
    long ReceiveToEnqueueMicroseconds,
    int ReceiveTimelineValid,
    int LeaseTimelineValid,
    long LeaseStartedRelativeMicroseconds,
    long LeaseCompletedRelativeMicroseconds,
    long EnqueuedRelativeMicroseconds,
    long SendStartedRelativeMicroseconds,
    long SendCompletedRelativeMicroseconds,
    long CommitCompletedRelativeMicroseconds,
    long ReceiptReceivedRelativeMicroseconds,
    int ReceiptValid,
    int ReceiptAfterSendValid,
    long RoomGateAcquiredRelativeMicroseconds,
    long OutboundEnqueuedRelativeMicroseconds,
    long OutboundDequeuedRelativeMicroseconds,
    L12SyntheticRecorderSnapshot RecorderAppend,
    L12SyntheticRecorderSnapshot RecorderComplete,
    int RecipientRole = 0);

/// <summary>
/// A one-shot numeric probe for an explicitly isolated test-run profile. It deliberately does not
/// feed the persisted 39-stage performance archive and never retains a request id or payload.
/// </summary>
internal sealed class L12SyntheticTransportDiagnostics : IDisposable
{
    internal const string EnvironmentKey = "L12_TESTRUN_TRANSPORT_DIAGNOSTICS";
    internal const string Mode = "terminal-burst-v1";
    internal const string BoundsMode = "terminal-bounds-v2";
    internal const string RecorderMode = "terminal-recorder-v3";
    internal const string RecipientsMode = "terminal-recipients-v4";
    internal const int MaximumSamples = 32;
    internal const int MaximumBoundsSamples = 6;
    private static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private readonly string _nonce;
    private readonly string _requestPrefix;
    private readonly L12SyntheticTransportSample?[] _samples;
    private readonly L12SyntheticTransportSample?[]? _peerSamples;
    private readonly Action<string> _sink;
    private readonly TimeSpan _deadline;
    private readonly TimeSpan _quietPeriod;
    private readonly bool _boundsMode;
    private readonly bool _recorderMode;
    private readonly bool _recipientsMode;
    private readonly Timer _timer;
    private int _accepted;
    private int _completed;
    private int _duplicates;
    private int _invalid;
    private int _receipts;
    private int _receiptDuplicates;
    private long _deadlineStartedAt;
    private bool _started;
    private bool _flushed;

    private L12SyntheticTransportDiagnostics(int expected, string nonce, Action<string> sink,
        TimeSpan deadline, TimeSpan quietPeriod, bool boundsMode, bool recorderMode, bool recipientsMode)
    {
        ExpectedSamples = expected;
        _nonce = nonce;
        _requestPrefix = $"l12diag:{nonce}:";
        _samples = new L12SyntheticTransportSample?[expected];
        _peerSamples = recipientsMode ? new L12SyntheticTransportSample?[expected] : null;
        _sink = sink;
        _deadline = deadline;
        _quietPeriod = quietPeriod;
        _boundsMode = boundsMode || recorderMode || recipientsMode;
        _recorderMode = recorderMode || recipientsMode;
        _recipientsMode = recipientsMode;
        _timer = new Timer(static state => ((L12SyntheticTransportDiagnostics)state!).Flush(),
            this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    internal int ExpectedSamples { get; }
    internal int AcceptedSamples { get { lock (_gate) return _accepted; } }
    internal int CompletedSamples { get { lock (_gate) return _completed; } }
    internal bool IsBoundsMode => _boundsMode;
    internal bool IsRecorderMode => _recorderMode;
    internal bool IsRecipientsMode => _recipientsMode;

    internal static L12SyntheticTransportDiagnostics? Create(bool ephemeralTestMatches,
        string? setting, Action<string>? sink = null, TimeSpan? deadline = null,
        TimeSpan? quietPeriod = null)
    {
        if (setting is null) return null;
        if (!ephemeralTestMatches)
            throw new InvalidOperationException(
                "Synthetic transport diagnostics require the isolated ephemeral test-run profile.");

        var parts = setting.Split(':', StringSplitOptions.None);
        var legacyMode = parts.Length == 3 && string.Equals(parts[0], Mode, StringComparison.Ordinal);
        var boundsMode = parts.Length == 3 && string.Equals(parts[0], BoundsMode, StringComparison.Ordinal);
        var recorderMode = parts.Length == 3 && string.Equals(parts[0], RecorderMode, StringComparison.Ordinal);
        var recipientsMode = parts.Length == 3 && string.Equals(parts[0], RecipientsMode, StringComparison.Ordinal);
        if (parts.Length != 3
            || !legacyMode && !boundsMode && !recorderMode && !recipientsMode
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expected)
            || expected < 1
            || legacyMode && expected > MaximumSamples
            || recipientsMode && expected > MaximumSamples
            || (boundsMode || recorderMode) && expected > MaximumBoundsSamples
            || parts[2].Length != 32
            || parts[2].Any(character => character is not (>= '0' and <= '9')
                                               and not (>= 'a' and <= 'f')))
            throw new InvalidOperationException("Synthetic transport diagnostics configuration is invalid.");

        var effectiveDeadline = deadline ?? DefaultDeadline;
        var effectiveQuietPeriod = quietPeriod ?? DefaultQuietPeriod;
        if (effectiveDeadline <= TimeSpan.Zero || effectiveQuietPeriod <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(deadline),
                "Synthetic transport diagnostic timers must be positive.");
        return new L12SyntheticTransportDiagnostics(expected, parts[2], sink ?? Console.Error.WriteLine,
            effectiveDeadline, effectiveQuietPeriod, boundsMode, recorderMode, recipientsMode);
    }

    internal bool AcceptsCommand(string? commandType) => _boundsMode && !_recipientsMode
        ? commandType is "surrender" or "playCard" or "endTurn"
        : string.Equals(commandType, "surrender", StringComparison.Ordinal);

    internal L12SyntheticTransportSample? TryBegin(string? requestId, L12InboundQueueTiming inbound,
        Guid transportId = default)
    {
        try
        {
            if (requestId is null || requestId.Length > 128
                || !requestId.StartsWith(_requestPrefix, StringComparison.Ordinal))
                return null;
            var remainder = requestId.AsSpan(_requestPrefix.Length);
            var separator = remainder.IndexOf(':');
            if (separator <= 0 || remainder[(separator + 1)..].IndexOf(':') >= 0
                || !int.TryParse(remainder[..separator], NumberStyles.None, CultureInfo.InvariantCulture,
                    out var slot)
                || slot < 0 || slot >= ExpectedSamples
                || !remainder[..separator].SequenceEqual(slot.ToString(CultureInfo.InvariantCulture))
                || !Guid.TryParseExact(remainder[(separator + 1)..], "N", out _))
            {
                lock (_gate) _invalid++;
                return null;
            }

            lock (_gate)
            {
                if (_flushed) return null;
                if (_boundsMode && transportId == default)
                {
                    _invalid++;
                    return null;
                }
                if (_samples[slot] is not null)
                {
                    _duplicates++;
                    return null;
                }
                var sample = new L12SyntheticTransportSample(this, slot, inbound, transportId);
                _samples[slot] = sample;
                _accepted++;
                if (!_started)
                {
                    _started = true;
                    _deadlineStartedAt = Stopwatch.GetTimestamp();
                    _timer.Change(_deadline, Timeout.InfiniteTimeSpan);
                }
                return sample;
            }
        }
        catch
        {
            // Diagnostics are observational. A malformed marker or callback race must not affect play.
            return null;
        }
    }

    internal void Complete(L12SyntheticTransportSample sample, L12SyntheticTransportStatus status,
        long observedAt)
    {
        try
        {
            lock (_gate)
            {
                if (_flushed || !OwnsSampleLocked(sample)) return;
                if (RejectBoundsEventAtOrBeyondDeadlineLocked(observedAt)) return;
                if (!sample.TryComplete(status)) return;
                _completed++;
                if (!_boundsMode && _completed >= ExpectedSamples)
                    _timer.Change(_quietPeriod, Timeout.InfiniteTimeSpan);
                else if (_boundsMode)
                    ScheduleBoundsFlushIfResolvedLocked();
            }
        }
        catch { }
    }

    private bool OwnsSampleLocked(L12SyntheticTransportSample sample)
        => sample.Slot >= 0 && sample.Slot < ExpectedSamples
            && (ReferenceEquals(_samples[sample.Slot], sample)
                || _peerSamples is not null && ReferenceEquals(_peerSamples[sample.Slot], sample));

    internal void BindRecipients(L12SyntheticTransportSample command, IReadOnlyList<Guid> players)
    {
        if (!_recipientsMode) return;
        try
        {
            lock (_gate)
            {
                if (_flushed || !ReferenceEquals(_samples[command.Slot], command)
                    || RejectBoundsEventAtOrBeyondDeadlineLocked(Stopwatch.GetTimestamp())) return;
                if (players.Count != 2 || players[0] == default || players[1] == default
                    || players[0] == players[1] || !players.Contains(command.TransportId)
                    || _peerSamples![command.Slot] is not null)
                { _invalid++; return; }
                var peer = players[0] == command.TransportId ? players[1] : players[0];
                _peerSamples![command.Slot] = command.CreatePeer(peer);
            }
        }
        catch { } // Observation must never change command delivery or settlement.
    }

    internal L12SyntheticTransportSample? TakeRecipient(L12SyntheticTransportSample command,
        Guid transportId, bool isGameState)
    {
        if (!_recipientsMode || !isGameState) return null;
        try
        {
            lock (_gate)
            {
                if (_flushed || !ReferenceEquals(_samples[command.Slot], command)
                    || RejectBoundsEventAtOrBeyondDeadlineLocked(Stopwatch.GetTimestamp())) return null;
                var recipient = transportId == command.TransportId ? command
                    : _peerSamples![command.Slot] is { } peer && peer.TransportId == transportId ? peer : null;
                if (recipient is null) return null; // Spectators/other transports are not the peer.
                if (!recipient.TryAssignOutbound()) { _invalid++; return null; }
                return recipient;
            }
        }
        catch { return null; }
    }

    internal void FinishRecipients(L12SyntheticTransportSample command)
    {
        if (!_recipientsMode) return;
        try
        {
            lock (_gate)
            {
                if (_flushed || !ReferenceEquals(_samples[command.Slot], command)) return;
                if (!command.OutboundAssigned) command.MissingReply();
                if (_peerSamples![command.Slot] is { OutboundAssigned: false } peer) peer.MissingReply();
            }
        }
        catch { }
    }

    internal void RecordRecorderBoundary(L12SyntheticTransportSample sample, int operation,
        int boundary, long timestamp)
    {
        try
        {
            lock (_gate)
            {
                if (!_recorderMode || _flushed || !ReferenceEquals(_samples[sample.Slot], sample)
                    || RejectBoundsEventAtOrBeyondDeadlineLocked(timestamp)) return;
                sample.RecordRecorderBoundaryCore(operation, boundary, timestamp);
            }
        }
        catch { }
    }

    internal bool TryAcceptAuthenticatedPingReceipt(Guid transportId, string json, long receivedAt)
    {
        if (!_boundsMode || transportId == default || receivedAt <= 0) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), "ping", StringComparison.Ordinal)
                || !root.TryGetProperty("diagnosticNonce", out var nonce)
                || nonce.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("diagnosticSlot", out var slotElement)
                || slotElement.ValueKind != JsonValueKind.Number)
                return false;
            var rawSlot = slotElement.GetRawText();
            if (!int.TryParse(rawSlot, NumberStyles.None, CultureInfo.InvariantCulture, out var slot)
                || !string.Equals(rawSlot, slot.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal))
                return false;
            var recipientRole = 0;
            if (_recipientsMode)
            {
                if (!root.TryGetProperty("diagnosticRecipient", out var recipient)
                    || recipient.ValueKind != JsonValueKind.Number
                    || recipient.GetRawText() is not ("0" or "1")) return false;
                recipientRole = recipient.GetInt32();
            }
            return TryAcceptReceipt(transportId, nonce.GetString(), slot, receivedAt, recipientRole);
        }
        catch (JsonException) { return false; }
        catch { return false; }
    }

    private bool TryAcceptReceipt(Guid transportId, string? nonce, int slot, long receivedAt, int recipientRole = 0)
    {
        try
        {
            lock (_gate)
            {
                if (_flushed) return false;
                if (!string.Equals(nonce, _nonce, StringComparison.Ordinal)
                    || slot < 0 || slot >= ExpectedSamples
                    || (recipientRole == 0 ? _samples[slot] : _peerSamples?[slot]) is not { } sample
                    || sample.TransportId != transportId)
                {
                    _invalid++;
                    return false;
                }
                if (RejectBoundsEventAtOrBeyondDeadlineLocked(receivedAt)) return false;
                if (!sample.TryRecordReceipt(receivedAt))
                {
                    _receiptDuplicates++;
                    return false;
                }
                _receipts++;
                ScheduleBoundsFlushIfResolvedLocked();
                return true;
            }
        }
        catch { return false; }
    }

    private bool RejectBoundsEventAtOrBeyondDeadlineLocked(long observedAt)
    {
        if (!_boundsMode || !_started) return false;
        if (observedAt <= 0) observedAt = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_deadlineStartedAt, observedAt) < _deadline) return false;
        // The nominal timer may be waiting for a ThreadPool thread or this owner gate. Do not let
        // that scheduling delay extend the measurement window; wake it as soon as the gate opens.
        _timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        return true;
    }

    private void ScheduleBoundsFlushIfResolvedLocked()
    {
        if (!_boundsMode || !_started || _accepted < ExpectedSamples) return;
        foreach (var sample in _samples)
            if (sample is null || !sample.IsBoundsResolved) return;
        if (_peerSamples is not null)
            foreach (var sample in _peerSamples)
                if (sample is null || !sample.IsBoundsResolved) return;
        var remaining = _deadline - Stopwatch.GetElapsedTime(_deadlineStartedAt);
        var due = remaining <= TimeSpan.Zero
            ? TimeSpan.Zero
            : remaining < _quietPeriod ? remaining : _quietPeriod;
        _timer.Change(due, Timeout.InfiniteTimeSpan);
    }

    internal IReadOnlyList<L12SyntheticTransportSnapshot> SnapshotForTests()
    {
        lock (_gate)
            return AllSamplesLocked().Where(sample => sample is not null)
                .Select(sample => sample!.Snapshot()).ToArray();
    }

    private IEnumerable<L12SyntheticTransportSample?> AllSamplesLocked()
        => _peerSamples is null ? _samples : _samples.Concat(_peerSamples);

    private void Flush()
    {
        L12SyntheticTransportSnapshot[] rows;
        int accepted;
        int completed;
        int duplicates;
        int invalid;
        int receipts;
        int receiptDuplicates;
        lock (_gate)
        {
            if (_flushed) return;
            _flushed = true;
            if (!_started) return;
            foreach (var sample in AllSamplesLocked())
                sample?.SealAtDeadline();
            rows = AllSamplesLocked().Where(sample => sample is not null)
                .Select(sample => sample!.Snapshot()).ToArray();
            accepted = _accepted;
            completed = rows.Count(row => row.Status != (int)L12SyntheticTransportStatus.Pending
                                          && row.Status != (int)L12SyntheticTransportStatus.Deadline);
            duplicates = _duplicates;
            invalid = _invalid;
            receipts = _receipts;
            receiptDuplicates = _receiptDuplicates;
        }

        if (!_boundsMode)
        {
            foreach (var row in rows)
            {
                var line = string.Create(CultureInfo.InvariantCulture,
                    $"{{\"schema\":1,\"kind\":1,\"slot\":{row.Slot},\"status\":{row.Status},\"in_fifo_us\":{row.InboundFifoMicroseconds},\"in_depth\":{row.InboundAcceptedDepth},\"in_bytes\":{row.InboundAcceptedRetainedBytes},\"in_remaining\":{row.InboundRemainingDepth},\"pre_gate_us\":{row.PreGateMicroseconds},\"gate_wait_us\":{row.GateWaitMicroseconds},\"gate_hold_us\":{row.GateHoldMicroseconds},\"serialize_us\":{row.SerializeMicroseconds},\"out_enq_depth\":{row.OutboundEnqueueDepth},\"out_deq_depth\":{row.OutboundDequeueDepth},\"out_queue_us\":{row.OutboundQueueMicroseconds},\"prepare_us\":{row.PrepareMicroseconds},\"wire_bytes\":{row.WireBytes},\"wire_delta\":{row.WireDelta},\"send_us\":{row.SendMicroseconds},\"commit_us\":{row.CommitMicroseconds},\"server_total_us\":{row.ServerTotalMicroseconds}}}");
                try { _sink(line); }
                catch { }
            }
            var summary = string.Create(CultureInfo.InvariantCulture,
                $"{{\"schema\":1,\"kind\":2,\"expected\":{ExpectedSamples},\"accepted\":{accepted},\"completed\":{completed},\"missing\":{ExpectedSamples - accepted},\"deadline\":{accepted - completed},\"duplicate\":{duplicates},\"invalid\":{invalid}}}");
            try { _sink(summary); }
            catch { }
            return;
        }

        if (_recorderMode)
        {
            foreach (var row in rows)
            {
                // A separate schema keeps both legacy strict numeric contracts unchanged.
                var line = JsonSerializer.Serialize(new
                {
                    schema = _recipientsMode ? 4 : 3, kind = 1, slot = row.Slot, status = row.Status,
                    in_fifo_us = row.InboundFifoMicroseconds, in_depth = row.InboundAcceptedDepth,
                    in_bytes = row.InboundAcceptedRetainedBytes, in_remaining = row.InboundRemainingDepth,
                    pre_gate_us = row.PreGateMicroseconds, gate_wait_us = row.GateWaitMicroseconds,
                    gate_hold_us = row.GateHoldMicroseconds, serialize_us = row.SerializeMicroseconds,
                    out_enq_depth = row.OutboundEnqueueDepth, out_deq_depth = row.OutboundDequeueDepth,
                    out_queue_us = row.OutboundQueueMicroseconds, prepare_us = row.PrepareMicroseconds,
                    wire_bytes = row.WireBytes, wire_delta = row.WireDelta, send_us = row.SendMicroseconds,
                    commit_us = row.CommitMicroseconds, server_total_us = row.ServerTotalMicroseconds,
                    receive_to_enqueue_us = row.ReceiveToEnqueueMicroseconds,
                    receive_valid = row.ReceiveTimelineValid, lease_valid = row.LeaseTimelineValid,
                    lease_start_rel_us = row.LeaseStartedRelativeMicroseconds,
                    lease_end_rel_us = row.LeaseCompletedRelativeMicroseconds,
                    enqueue_rel_us = row.EnqueuedRelativeMicroseconds,
                    send_start_rel_us = row.SendStartedRelativeMicroseconds,
                    send_end_rel_us = row.SendCompletedRelativeMicroseconds,
                    commit_rel_us = row.CommitCompletedRelativeMicroseconds,
                    receipt_rel_us = row.ReceiptReceivedRelativeMicroseconds,
                    receipt_valid = row.ReceiptValid, receipt_after_send_valid = row.ReceiptAfterSendValid,
                    room_acquired_rel_us = row.RoomGateAcquiredRelativeMicroseconds,
                    out_enqueued_rel_us = row.OutboundEnqueuedRelativeMicroseconds,
                    out_dequeued_rel_us = row.OutboundDequeuedRelativeMicroseconds,
                    append_count = row.RecorderAppend.Count, append_valid = row.RecorderAppend.Valid,
                    append_wait_rel_us = row.RecorderAppend.WaitStartedRelativeMicroseconds,
                    append_acquired_rel_us = row.RecorderAppend.AcquiredRelativeMicroseconds,
                    append_hold_end_rel_us = row.RecorderAppend.HoldEndedRelativeMicroseconds,
                    complete_count = row.RecorderComplete.Count, complete_valid = row.RecorderComplete.Valid,
                    complete_wait_rel_us = row.RecorderComplete.WaitStartedRelativeMicroseconds,
                    complete_acquired_rel_us = row.RecorderComplete.AcquiredRelativeMicroseconds,
                    complete_hold_end_rel_us = row.RecorderComplete.HoldEndedRelativeMicroseconds,
                });
                if (_recipientsMode)
                    line = "{\"recipient\":" + row.RecipientRole.ToString(CultureInfo.InvariantCulture)
                        + "," + line[1..];
                try { _sink(line); }
                catch { }
            }
            if (_recipientsMode)
            {
                var recipientSummary = JsonSerializer.Serialize(new
                {
                    schema = 4, kind = 2, expected_commands = ExpectedSamples, accepted_commands = accepted,
                    expected_recipients = ExpectedSamples * 2, observed_recipients = rows.Length, completed,
                    missing = ExpectedSamples * 2 - rows.Length,
                    deadline = rows.Count(row => row.Status == (int)L12SyntheticTransportStatus.Deadline),
                    duplicate = duplicates, invalid, receipts,
                    receipt_missing = rows.Count(row => row.ReceiptValid == 0
                        && row.Status is (int)L12SyntheticTransportStatus.Sent or (int)L12SyntheticTransportStatus.Deadline),
                    receipt_duplicate = receiptDuplicates,
                });
                try { _sink(recipientSummary); }
                catch { }
                return;
            }
            var summary = JsonSerializer.Serialize(new
            {
                schema = 3, kind = 2, expected = ExpectedSamples, accepted, completed,
                missing = ExpectedSamples - accepted, deadline = accepted - completed,
                duplicate = duplicates, invalid, receipts,
                receipt_missing = rows.Count(row => row.ReceiptValid == 0
                    && row.Status is (int)L12SyntheticTransportStatus.Sent or (int)L12SyntheticTransportStatus.Deadline),
                receipt_duplicate = receiptDuplicates,
            });
            try { _sink(summary); }
            catch { }
            return;
        }

        foreach (var row in rows)
        {
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{{\"schema\":2,\"kind\":1,\"slot\":{row.Slot},\"status\":{row.Status},\"in_fifo_us\":{row.InboundFifoMicroseconds},\"in_depth\":{row.InboundAcceptedDepth},\"in_bytes\":{row.InboundAcceptedRetainedBytes},\"in_remaining\":{row.InboundRemainingDepth},\"pre_gate_us\":{row.PreGateMicroseconds},\"gate_wait_us\":{row.GateWaitMicroseconds},\"gate_hold_us\":{row.GateHoldMicroseconds},\"serialize_us\":{row.SerializeMicroseconds},\"out_enq_depth\":{row.OutboundEnqueueDepth},\"out_deq_depth\":{row.OutboundDequeueDepth},\"out_queue_us\":{row.OutboundQueueMicroseconds},\"prepare_us\":{row.PrepareMicroseconds},\"wire_bytes\":{row.WireBytes},\"wire_delta\":{row.WireDelta},\"send_us\":{row.SendMicroseconds},\"commit_us\":{row.CommitMicroseconds},\"server_total_us\":{row.ServerTotalMicroseconds},\"receive_to_enqueue_us\":{row.ReceiveToEnqueueMicroseconds},\"receive_valid\":{row.ReceiveTimelineValid},\"lease_valid\":{row.LeaseTimelineValid},\"lease_start_rel_us\":{row.LeaseStartedRelativeMicroseconds},\"lease_end_rel_us\":{row.LeaseCompletedRelativeMicroseconds},\"enqueue_rel_us\":{row.EnqueuedRelativeMicroseconds},\"send_start_rel_us\":{row.SendStartedRelativeMicroseconds},\"send_end_rel_us\":{row.SendCompletedRelativeMicroseconds},\"commit_rel_us\":{row.CommitCompletedRelativeMicroseconds},\"receipt_rel_us\":{row.ReceiptReceivedRelativeMicroseconds},\"receipt_valid\":{row.ReceiptValid},\"receipt_after_send_valid\":{row.ReceiptAfterSendValid}}}");
            try { _sink(line); }
            catch { }
        }
        var receiptMissing = rows.Count(row => row.ReceiptValid == 0
                                               && row.Status is (int)L12SyntheticTransportStatus.Sent
                                                   or (int)L12SyntheticTransportStatus.Deadline);
        var boundsSummary = string.Create(CultureInfo.InvariantCulture,
            $"{{\"schema\":2,\"kind\":2,\"expected\":{ExpectedSamples},\"accepted\":{accepted},\"completed\":{completed},\"missing\":{ExpectedSamples - accepted},\"deadline\":{accepted - completed},\"duplicate\":{duplicates},\"invalid\":{invalid},\"receipts\":{receipts},\"receipt_missing\":{receiptMissing},\"receipt_duplicate\":{receiptDuplicates}}}");
        try { _sink(boundsSummary); }
        catch { }
    }

    public void Dispose()
    {
        Flush();
        _timer.Dispose();
    }
}

internal sealed class L12SyntheticTransportSample
{
    private readonly L12SyntheticTransportDiagnostics _owner;
    private readonly L12InboundQueueTiming _inbound;
    private readonly L12SyntheticTransportSample? _commandSample;
    private int _outboundAssigned;
    private readonly long _inboundEnqueuedAt;
    private readonly long _inboundDequeuedAt;
    private readonly long _receiveCompletedAt;
    private readonly long _leaseStartedAt;
    private readonly long _leaseCompletedAt;
    private readonly int _inboundAcceptedDepth;
    private readonly long _inboundAcceptedRetainedBytes;
    private readonly int _inboundRemainingDepth;
    private long _gateWaitStartedAt;
    private long _gateAcquiredAt;
    private long _gateReleasedAt;
    private long _serializeStartedAt;
    private long _serializeCompletedAt;
    private long _outboundEnqueuedAt;
    private long _outboundDequeuedAt;
    private int _outboundEnqueueDepth;
    private int _outboundDequeueDepth;
    private long _prepareStartedAt;
    private long _prepareCompletedAt;
    private int _wireBytes;
    private int _wireDelta;
    private long _sendStartedAt;
    private long _sendCompletedAt;
    private long _commitCompletedAt;
    private long _receiptReceivedAt;
    private int _status;
    private readonly Action<int, int, long>? _recorderObserver;
    private RecorderInterval _recorderAppend;
    private RecorderInterval _recorderComplete;

    internal L12SyntheticTransportSample(L12SyntheticTransportDiagnostics owner, int slot,
        L12InboundQueueTiming inbound, Guid transportId, L12SyntheticTransportSample? commandSample = null)
    {
        _owner = owner;
        _inbound = inbound;
        _commandSample = commandSample;
        Slot = slot;
        TransportId = transportId;
        _inboundEnqueuedAt = inbound.EnqueuedAt;
        _inboundDequeuedAt = inbound.DequeuedAt;
        _receiveCompletedAt = inbound.ReceiveCompletedAt;
        _leaseStartedAt = inbound.LeaseStartedAt;
        _leaseCompletedAt = inbound.LeaseCompletedAt;
        _inboundAcceptedDepth = inbound.AcceptedDepth;
        _inboundAcceptedRetainedBytes = inbound.AcceptedRetainedBytes;
        _inboundRemainingDepth = inbound.RemainingQueuedMessages;
        if (owner.IsRecorderMode && commandSample is null)
            _recorderObserver = (operation, boundary, timestamp) =>
                owner.RecordRecorderBoundary(this, operation, boundary, timestamp);
    }

    internal int Slot { get; }
    internal Guid TransportId { get; }
    internal Action<int, int, long>? RecorderObserver => _recorderObserver;
    internal bool IsRecipientsMode => _owner.IsRecipientsMode;
    internal bool OutboundAssigned => Volatile.Read(ref _outboundAssigned) != 0;
    internal bool TryAssignOutbound() => Interlocked.CompareExchange(ref _outboundAssigned, 1, 0) == 0;
    internal L12SyntheticTransportSample CreatePeer(Guid transportId)
        => new(_owner, Slot, _inbound, transportId, this);
    internal void BindRecipients(IReadOnlyList<Guid> players) => _owner.BindRecipients(this, players);
    internal L12SyntheticTransportSample? TakeRecipient(Guid transportId, bool isGameState)
        => _owner.TakeRecipient(this, transportId, isGameState);
    internal void FinishRecipients() => _owner.FinishRecipients(this);

    // Accessed only under the diagnostics owner gate, also used by SnapshotForTests/Flush.
    private struct RecorderInterval
    {
        internal int Count, Phase;
        internal long WaitStartedAt, AcquiredAt, HoldEndedAt;
        internal void Record(int boundary, long timestamp)
        {
            if (boundary == L12SyntheticRecorderObservation.WaitStarted)
            {
                Count = Math.Min(2, Count + 1);
                if (Count == 1 && Phase == 0) { WaitStartedAt = timestamp; Phase = 1; }
                else Phase = -1;
            }
            else if (boundary == L12SyntheticRecorderObservation.Acquired && Phase == 1
                     && timestamp >= WaitStartedAt) { AcquiredAt = timestamp; Phase = 2; }
            else if (boundary == L12SyntheticRecorderObservation.HoldEnded && Phase == 2
                     && timestamp >= AcquiredAt) { HoldEndedAt = timestamp; Phase = 3; }
            else Phase = -1;
        }
        internal readonly L12SyntheticRecorderSnapshot Snapshot(long origin, long roomAcquiredAt, bool timedOut)
            => new(Count, !timedOut && Count == 1 && Phase == 3 && origin > 0
                && roomAcquiredAt >= origin && WaitStartedAt >= roomAcquiredAt ? 1 : 0,
                RelativeMicroseconds(origin, WaitStartedAt), RelativeMicroseconds(origin, AcquiredAt),
                RelativeMicroseconds(origin, HoldEndedAt));
    }

    internal void RecordRecorderBoundaryCore(int operation, int boundary, long timestamp)
    {
        if (!IsPending) return;
        if (operation == L12SyntheticRecorderObservation.Append) _recorderAppend.Record(boundary, timestamp);
        else if (operation == L12SyntheticRecorderObservation.Complete) _recorderComplete.Record(boundary, timestamp);
    }
    internal bool IsBoundsResolved
    {
        get
        {
            var status = Volatile.Read(ref _status);
            return status != (int)L12SyntheticTransportStatus.Pending
                   && (status != (int)L12SyntheticTransportStatus.Sent
                       || Interlocked.Read(ref _receiptReceivedAt) > 0);
        }
    }

    internal void GateWaitStarted(long timestamp)
    {
        if (IsPending) Volatile.Write(ref _gateWaitStartedAt, timestamp);
    }
    internal void GateAcquired(long timestamp)
    {
        if (IsPending) Volatile.Write(ref _gateAcquiredAt, timestamp);
    }
    internal void GateReleased(long timestamp)
    {
        if (IsPending) Volatile.Write(ref _gateReleasedAt, timestamp);
    }
    internal void Serialization(long startedAt, long completedAt)
    {
        if (!IsPending) return;
        Volatile.Write(ref _serializeStartedAt, startedAt);
        Volatile.Write(ref _serializeCompletedAt, completedAt);
    }
    internal void OutboundEnqueued(long timestamp, int depth)
    {
        if (!IsPending) return;
        Volatile.Write(ref _outboundEnqueuedAt, timestamp);
        Volatile.Write(ref _outboundEnqueueDepth, depth);
    }
    internal void OutboundDequeued(long timestamp, int depth)
    {
        if (!IsPending) return;
        Volatile.Write(ref _outboundDequeuedAt, timestamp);
        Volatile.Write(ref _outboundDequeueDepth, depth);
    }
    internal void Prepared(long startedAt, long completedAt, int wireBytes, bool isDelta)
    {
        if (!IsPending) return;
        Volatile.Write(ref _prepareStartedAt, startedAt);
        Volatile.Write(ref _prepareCompletedAt, completedAt);
        Volatile.Write(ref _wireBytes, wireBytes);
        Volatile.Write(ref _wireDelta, isDelta ? 1 : 0);
    }
    internal void Sent(long startedAt, long completedAt)
    {
        if (!IsPending) return;
        Volatile.Write(ref _sendStartedAt, startedAt);
        Volatile.Write(ref _sendCompletedAt, completedAt);
    }
    internal void Committed(long timestamp)
    {
        if (!IsPending) return;
        Volatile.Write(ref _commitCompletedAt, timestamp);
        Complete(L12SyntheticTransportStatus.Sent, timestamp);
    }
    internal void QueueRejected() => Complete(L12SyntheticTransportStatus.QueueRejected,
        Stopwatch.GetTimestamp());
    internal void Discarded() => Complete(L12SyntheticTransportStatus.Discarded,
        Stopwatch.GetTimestamp());
    internal void SendFault() => Complete(L12SyntheticTransportStatus.SendFault,
        Stopwatch.GetTimestamp());
    internal void MissingReply() => Complete(L12SyntheticTransportStatus.MissingReply,
        Stopwatch.GetTimestamp());

    internal bool TryRecordReceipt(long timestamp) => timestamp > 0
        && Interlocked.CompareExchange(ref _receiptReceivedAt, timestamp, 0) == 0;

    private bool IsPending => Volatile.Read(ref _status) == (int)L12SyntheticTransportStatus.Pending;

    private void Complete(L12SyntheticTransportStatus status, long observedAt)
    {
        try { _owner.Complete(this, status, observedAt); }
        catch { }
    }

    internal bool TryComplete(L12SyntheticTransportStatus status)
        => Interlocked.CompareExchange(ref _status, (int)status,
            (int)L12SyntheticTransportStatus.Pending) == (int)L12SyntheticTransportStatus.Pending;

    internal void SealAtDeadline()
        => Interlocked.CompareExchange(ref _status, (int)L12SyntheticTransportStatus.Deadline,
            (int)L12SyntheticTransportStatus.Pending);

    internal L12SyntheticTransportSnapshot Snapshot()
    {
        var sendStartedAt = Volatile.Read(ref _sendStartedAt);
        var sendCompletedAt = Volatile.Read(ref _sendCompletedAt);
        var commitCompletedAt = Volatile.Read(ref _commitCompletedAt);
        var receiptReceivedAt = Interlocked.Read(ref _receiptReceivedAt);
        var receiveTimelineValid = _receiveCompletedAt > 0
                                   && _inboundEnqueuedAt >= _receiveCompletedAt;
        var leaseTimelineValid = receiveTimelineValid
                                 && _leaseStartedAt >= _receiveCompletedAt
                                 && _leaseCompletedAt >= _leaseStartedAt
                                 && _inboundEnqueuedAt >= _leaseCompletedAt;
        var receiptValid = receiveTimelineValid && receiptReceivedAt >= _receiveCompletedAt;
        var receiptAfterSendValid = receiptValid && sendCompletedAt > 0
                                   && receiptReceivedAt >= sendCompletedAt;
        var snapshot = new L12SyntheticTransportSnapshot(
            Slot,
            Volatile.Read(ref _status),
            ElapsedMicroseconds(_inboundEnqueuedAt, _inboundDequeuedAt),
            _inboundAcceptedDepth,
            _inboundAcceptedRetainedBytes,
            _inboundRemainingDepth,
            ElapsedMicroseconds(_inboundDequeuedAt, Volatile.Read(ref _gateWaitStartedAt)),
            ElapsedMicroseconds(Volatile.Read(ref _gateWaitStartedAt), Volatile.Read(ref _gateAcquiredAt)),
            ElapsedMicroseconds(Volatile.Read(ref _gateAcquiredAt), Volatile.Read(ref _gateReleasedAt)),
            ElapsedMicroseconds(Volatile.Read(ref _serializeStartedAt), Volatile.Read(ref _serializeCompletedAt)),
            Volatile.Read(ref _outboundEnqueueDepth),
            Volatile.Read(ref _outboundDequeueDepth),
            ElapsedMicroseconds(Volatile.Read(ref _outboundEnqueuedAt), Volatile.Read(ref _outboundDequeuedAt)),
            ElapsedMicroseconds(Volatile.Read(ref _prepareStartedAt), Volatile.Read(ref _prepareCompletedAt)),
            Volatile.Read(ref _wireBytes),
            Volatile.Read(ref _wireDelta),
            ElapsedMicroseconds(sendStartedAt, sendCompletedAt),
            ElapsedMicroseconds(sendCompletedAt, commitCompletedAt),
            ElapsedMicroseconds(_inboundEnqueuedAt, commitCompletedAt),
            receiveTimelineValid ? ElapsedMicroseconds(_receiveCompletedAt, _inboundEnqueuedAt) : 0,
            receiveTimelineValid ? 1 : 0,
            leaseTimelineValid ? 1 : 0,
            leaseTimelineValid ? ElapsedMicroseconds(_receiveCompletedAt, _leaseStartedAt) : 0,
            leaseTimelineValid ? ElapsedMicroseconds(_receiveCompletedAt, _leaseCompletedAt) : 0,
            receiveTimelineValid ? ElapsedMicroseconds(_receiveCompletedAt, _inboundEnqueuedAt) : 0,
            RelativeMicroseconds(_receiveCompletedAt, sendStartedAt),
            RelativeMicroseconds(_receiveCompletedAt, sendCompletedAt),
            RelativeMicroseconds(_receiveCompletedAt, commitCompletedAt),
            receiptValid ? ElapsedMicroseconds(_receiveCompletedAt, receiptReceivedAt) : 0,
            receiptValid ? 1 : 0,
            receiptAfterSendValid ? 1 : 0,
            RelativeMicroseconds(_receiveCompletedAt, Volatile.Read(ref _gateAcquiredAt)),
            RelativeMicroseconds(_receiveCompletedAt, Volatile.Read(ref _outboundEnqueuedAt)),
            RelativeMicroseconds(_receiveCompletedAt, Volatile.Read(ref _outboundDequeuedAt)),
            _recorderAppend.Snapshot(_receiveCompletedAt, Volatile.Read(ref _gateAcquiredAt),
                Volatile.Read(ref _status) == (int)L12SyntheticTransportStatus.Deadline),
            _recorderComplete.Snapshot(_receiveCompletedAt, Volatile.Read(ref _gateAcquiredAt),
                Volatile.Read(ref _status) == (int)L12SyntheticTransportStatus.Deadline),
            _commandSample is null ? 0 : 1);
        if (_commandSample is null) return snapshot;
        var command = _commandSample.Snapshot();
        return snapshot with
        {
            PreGateMicroseconds = command.PreGateMicroseconds,
            GateWaitMicroseconds = command.GateWaitMicroseconds,
            GateHoldMicroseconds = command.GateHoldMicroseconds,
            RoomGateAcquiredRelativeMicroseconds = command.RoomGateAcquiredRelativeMicroseconds,
            RecorderAppend = command.RecorderAppend,
            RecorderComplete = command.RecorderComplete,
        };
    }

    private static long ElapsedMicroseconds(long startedAt, long completedAt)
    {
        if (startedAt <= 0 || completedAt < startedAt) return 0;
        return (long)(Stopwatch.GetElapsedTime(startedAt, completedAt).TotalMilliseconds * 1000);
    }

    private static long RelativeMicroseconds(long origin, long timestamp)
        => origin > 0 && timestamp >= origin ? ElapsedMicroseconds(origin, timestamp) : 0;
}

public sealed partial class L12WebSocketServer : IAsyncDisposable
{
    private const string OperationsCrossSectionReplaceIntent = "replace-all-operations-sections";
    private static readonly JsonSerializerOptions OutgoingJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan SocketSendTimeout = TimeSpan.FromSeconds(5);

    private sealed record SocketPlatformBinding(string PlatformSessionId, string AccountId,
        long ConnectionGeneration);
    private sealed record ProtocolCapabilities(bool RequestIds, bool DeltaGameState,
        bool EventWindowDeltaV1 = false, bool PresenceDeltaV1 = false, bool ChatV1 = false,
        bool GameStateCardFrameV1 = false, bool HandArrayDeltaV1 = false,
        bool PresenceOrderReuseV1 = false, bool GameStatePropertyFrameV1 = false);
    private sealed record TelemetryPageViewRequest(string? Path);

    private static readonly JsonSerializerOptions CommandJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly L12RoomManager _rooms;
    private readonly MatchRecorder _recorder;
    private readonly L12PlatformStore _platform;
    private readonly L12AdminCommandBus _adminCommands;
    private readonly L12ModianImportService _modianImports;
    private readonly IL12ReleaseControlAdapter _releaseControl;
    private readonly L12Catalog _catalog;
    private readonly int _cardCount;
    private readonly L12HttpPerformanceMonitor _httpPerformance = new();
    private readonly Func<L12ServerStorageView> _storageSnapshot;
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();
    private readonly ConcurrentDictionary<Guid, L12OutboundConnection> _outboundConnections = new();
    // Explicit test-only wiring; no production chat lane or mutation registry is installed.
    private L12ChatPairRegistry? _chatMutationRegistry = null;
    private readonly ConcurrentDictionary<Guid, L12InboundConnection> _inboundConnections = new();
    private readonly ConcurrentDictionary<Guid, byte> _establishedInboundConnections = new();
    private readonly ConcurrentDictionary<Guid, L12SnapshotWireCodec> _snapshotCodecs = new();
    private readonly ConcurrentDictionary<Guid, L12PresenceSnapshotWireCodec> _presenceSnapshotCodecs = new();
    private readonly ConcurrentDictionary<Guid, SocketPlatformBinding> _socketPlatformSessions = new();
    private readonly ConcurrentDictionary<Guid, ProtocolCapabilities> _socketCapabilities = new();
    private readonly ConcurrentDictionary<string, Guid> _activeAccountSockets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _socketClaimGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, string> _socketRankedNetworkFingerprints = new();
    private readonly string? _rankedIntegrityHmacKey;
    private readonly L12RankedDevicePrivacy _rankedDevicePrivacy;
    private readonly ConcurrentDictionary<Guid, string> _socketRankedDevices = new();
    private readonly TimeSpan _rankedClockWatchdogInterval;
    private CancellationTokenSource? _rankedClockWatchdogCancellation;
    private Task? _rankedClockWatchdogTask;
    private readonly TimeSpan _sandboxReplayMaintenanceInterval;
    private readonly Func<DateTimeOffset> _seasonActivationUtcNow;
    private readonly L12SyntheticTransportDiagnostics? _transportDiagnostics;
    private readonly L12PresenceRefreshCoalescer _presenceRefreshCoalescer;
    internal Action<long>? PresenceHelloRevisionCapturedForTests { get; set; }
    private CancellationTokenSource? _sandboxReplayMaintenanceCancellation;
    private Task? _sandboxReplayMaintenanceTask;
    private WebApplication? _app;
    private IReadOnlyList<string> _addresses = [];

    public IReadOnlyList<string> Addresses => _addresses;

    public L12WebSocketServer(L12RoomManager rooms, MatchRecorder recorder, L12PlatformStore platform,
        L12Catalog catalog, IL12ReleaseControlAdapter? releaseControl = null,
        string? rankedIntegrityHmacKey = null, TimeSpan? rankedClockWatchdogInterval = null,
        IL12ModianImportClient? modianImportClient = null,
        TimeSpan? sandboxReplayMaintenanceInterval = null,
        Func<L12ServerStorageView>? storageSnapshot = null,
        Func<DateTimeOffset>? seasonActivationUtcNow = null,
        bool chatEnabled = false)
        : this(rooms, recorder, platform, catalog, releaseControl, rankedIntegrityHmacKey,
            rankedClockWatchdogInterval, modianImportClient, sandboxReplayMaintenanceInterval,
            storageSnapshot, seasonActivationUtcNow, null, chatEnabled)
    {
    }

    internal L12WebSocketServer(L12RoomManager rooms, MatchRecorder recorder, L12PlatformStore platform,
        L12Catalog catalog, IL12ReleaseControlAdapter? releaseControl,
        string? rankedIntegrityHmacKey, TimeSpan? rankedClockWatchdogInterval,
        IL12ModianImportClient? modianImportClient,
        TimeSpan? sandboxReplayMaintenanceInterval,
        Func<L12ServerStorageView>? storageSnapshot,
        Func<DateTimeOffset>? seasonActivationUtcNow,
        L12SyntheticTransportDiagnostics? transportDiagnostics,
        bool chatEnabled = false)
    {
        _rooms = rooms;
        _recorder = recorder;
        _platform = platform;
        _deploymentController = rooms.DeploymentDrain is { } coordinator
            ? new L12DeploymentDrainController(rooms, recorder, platform, coordinator) : null;
        _adminCommands = new L12AdminCommandBus(platform);
        _modianImports = new L12ModianImportService(platform, modianImportClient);
        _releaseControl = releaseControl ?? new L12DisabledReleaseControlAdapter();
        _rankedIntegrityHmacKey = rankedIntegrityHmacKey
            ?? Environment.GetEnvironmentVariable(L12RankedNetworkPrivacy.EnvironmentKey);
        _rankedDevicePrivacy = new L12RankedDevicePrivacy(_rankedIntegrityHmacKey);
        _rankedClockWatchdogInterval = rankedClockWatchdogInterval is { } interval && interval > TimeSpan.Zero
            ? interval : TimeSpan.FromSeconds(1);
        _sandboxReplayMaintenanceInterval = sandboxReplayMaintenanceInterval is { } maintenanceInterval
                                            && maintenanceInterval > TimeSpan.Zero
            ? maintenanceInterval : TimeSpan.FromMinutes(5);
        _storageSnapshot = storageSnapshot ?? (() => L12ServerStorageMonitor.Read());
        _seasonActivationUtcNow = seasonActivationUtcNow ?? (() => DateTimeOffset.UtcNow);
        _transportDiagnostics = transportDiagnostics;
        _presenceRefreshCoalescer = new L12PresenceRefreshCoalescer(
            FlushPresenceRefreshAsync,
            onEmitFailure: error => Console.Error.WriteLine($"在线状态合并刷新失败：{error.GetType().Name}"));
        _catalog = catalog;
        _cardCount = catalog.Cards.Count;
        _platform.SessionsRevoked += HandlePlatformSessionsRevoked;
        InitializeChatRuntime(chatEnabled);
    }

    private async Task StartCoreAsync(int port)
    {
        if (!_presenceRefreshCoalescer.Start())
            throw new InvalidOperationException("在线状态刷新器正在停止或已释放，不能启动服务");
        var host = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        if (string.IsNullOrWhiteSpace(host)) host = "0.0.0.0";
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://{host}:{port}");
        builder.Services.AddRouting();
        _app = builder.Build();
        var trafficGuard = new L12HttpTrafficGuard();
        MapRankedIntegrityEndpoints();
        MapAccountRankedRestrictionEndpoints();
        _app.Use((context, next) => _httpPerformance.InvokeAsync(context, next));
        _app.Use((context, next) => L12HttpExceptionBoundary.InvokeAsync(context, next));
        _app.UseWebSockets();
        _app.Use((context, next) => InvokeRequestLifetimeAsync(context, next));
        _app.Use(async (context, next) =>
        {
            // Authentication, mail throttles and privacy-preserving ranked network keys
            // must use the same verified client address, not the local reverse proxy.
            context.Connection.RemoteIpAddress = L12TrustedClientAddress.Resolve(context);
            var correlationId = L12CorrelationIds.AcceptOrCreate(context.Request.Headers[L12CorrelationIds.HeaderName]);
            context.Items[L12CorrelationIds.ContextItemName] = correlationId;
            var originalPath = context.Request.Path.Value ?? string.Empty;
            context.Items[L12CorrelationIds.OriginalPathItemName] = originalPath;
            if (context.Request.Path.StartsWithSegments("/api/admin/v1", out var remaining))
                context.Request.Path = "/api/admin" + remaining;
            context.Response.Headers[L12CorrelationIds.HeaderName] = correlationId;
            var origin = context.Request.Headers.Origin.ToString();
            if (IsAllowedOrigin(origin)) context.Response.Headers.AccessControlAllowOrigin = origin;
            context.Response.Headers.Vary = "Origin";
            context.Response.Headers.AccessControlAllowHeaders =
                $"Content-Type, Authorization, {L12CorrelationIds.HeaderName}, Idempotency-Key, If-Match, X-Admin-Reason, {L12DeckCoverCapability.HeaderName}";
            context.Response.Headers.AccessControlAllowMethods = "GET, POST, PUT, PATCH, DELETE, OPTIONS";
            context.Response.Headers.AccessControlExposeHeaders =
                $"{L12CorrelationIds.HeaderName}, X-Command-ID, X-Idempotent-Replay, ETag, Retry-After, RateLimit-Limit, RateLimit-Remaining, Server-Timing";
            if (HttpMethods.IsOptions(context.Request.Method)) { context.Response.StatusCode = StatusCodes.Status204NoContent; return; }
            if (!TryAcquireHttpDeploymentGuard(context.Request, out var deploymentGuard))
            {
                context.Items[L12HttpPerformanceMonitor.ExpectedUnavailableItemName] = true;
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsJsonAsync(new L12ApiError("deploymentDrainActive",
                    "服务器正在更新，请稍后再试", correlationId));
                return;
            }
            using var deploymentRequestGuard = deploymentGuard;
            var anonymousPublicTournamentRead = HttpMethods.IsGet(context.Request.Method)
                && context.Request.Path.StartsWithSegments("/api/public/tournaments");
            if (anonymousPublicTournamentRead || HttpMethods.IsGet(context.Request.Method)
                    && context.Request.Path.StartsWithSegments("/api/rankings"))
                context.Response.Headers.CacheControl = "no-store";
            var restrictedAccount = !anonymousPublicTournamentRead
                && context.Request.Path.StartsWithSegments("/api")
                ? _platform.Authenticate(context.Request.Headers.Authorization) : null;
            var rate = trafficGuard.Acquire(context.Request,
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", restrictedAccount?.Id);
            if (rate.Limit != int.MaxValue)
            {
                context.Response.Headers["RateLimit-Limit"] = rate.Limit.ToString();
                context.Response.Headers["RateLimit-Remaining"] = rate.Remaining.ToString();
            }
            if (!rate.Allowed)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = rate.RetryAfterSeconds.ToString();
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsJsonAsync(new L12ApiError("rate_limited",
                    "请求过于频繁，请稍后重试", correlationId));
                return;
            }
            var accountRecoveryPathAllowed = context.Request.Path == "/api/auth/me"
                || context.Request.Path == "/api/auth/change-password"
                || context.Request.Path == "/api/auth/change-username"
                || context.Request.Path == "/api/auth/mfa/capability"
                || context.Request.Path.StartsWithSegments("/api/auth/sessions")
                || anonymousPublicTournamentRead;
            if (restrictedAccount is { MustChangeUsername: true } && !accountRecoveryPathAllowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new L12ApiError("username_change_required",
                    "必须先修改不合规的用户名", correlationId));
                return;
            }
            if (restrictedAccount is { MustChangePassword: true } && !accountRecoveryPathAllowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new L12ApiError("password_change_required",
                    "必须先修改管理员设置的临时密码", correlationId));
                return;
            }
            var featureId = context.Request.Path.StartsWithSegments("/api/public/tournaments") ? "tournaments"
                : context.Request.Path.StartsWithSegments("/api/public-decks") ? "publicDecks"
                : context.Request.Path.StartsWithSegments("/api/tournaments") ? "tournaments"
                : null;
            if (featureId is not null && !_platform.CaptureOperationsPolicy().IsFeatureEnabled(featureId))
            {
                context.Items[L12HttpPerformanceMonitor.ExpectedUnavailableItemName] = true;
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "feature_disabled",
                    message = "该功能当前未开放",
                    correlationId,
                });
                return;
            }
            await next();
        });
        _app.UseRouting();
        MapDeploymentDrainEndpoints();
        MapDisasterAnalyticsEndpoints();
        _app.MapGet("/health", () =>
        {
            var build = L12RuntimeBuildVersion.Capture();
            var maintenance = _platform.EffectiveOperationsPolicy().Maintenance;
            return Results.Ok(new
            {
                service = "twelve-legions", cards = _cardCount,
                status = maintenance.Active ? "maintenance" : "ok",
                maintenance = maintenance.Active,
                serverVersion = build.ServerRelease,
                engineVersion = build.EngineVersion,
            });
        });
        _app.MapGet("/api/operations/effective-policy", (HttpRequest request) =>
        {
            var policy = _platform.EffectiveOperationsPolicy();
            request.HttpContext.Response.Headers.ETag = $"\"{policy.Version}\"";
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            return Results.Ok(policy);
        });
        _app.MapGet("/api/matches", async (HttpRequest request, int? limit) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized()
                : Results.Ok(await _recorder.ListMatchesForAccountAsync(account.Id, account.Username, limit ?? 30));
        });
        _app.MapGet("/api/matches/{matchId}", async (HttpRequest request, string matchId) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            try
            {
                var match = await _recorder.GetMatchForAccountAsync(matchId, account.Id, account.Username);
                if (match is not null && match.Commands.Count == 0)
                    return ApiError(request, "replay_payload_expired",
                        "回放载荷已清理或不可用；对局摘要与结算结果仍保留。",
                        StatusCodes.Status410Gone);
                return match is null ? Results.NotFound() : Results.Ok(match);
            }
            catch (InvalidDataException error) when (MatchRecorder.TryGetReplayIncompatibility(error, out _))
            {
                _ = MatchRecorder.TryGetReplayIncompatibility(error, out var evidence);
                Console.Error.WriteLine($"Replay reconstruction rejected (player-detail; correlation={CorrelationId(request)}): reason={evidence!.Reason}; sequence={evidence.Sequence?.ToString() ?? "none"}; type={evidence.CommandType ?? "none"}; dimension={evidence.Dimension}");
                return ApiError(request, "replay_incompatible",
                    "这场回放暂时无法还原，仍可查看对局结果。",
                    StatusCodes.Status409Conflict);
            }
        });
        _app.MapGet("/api/admin/matches", async (HttpRequest request) =>
        {
            const L12Permission permission = L12Permission.AdminMatchesRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            try
            {
                var page = await _recorder.ListAdminMatchesAsync(AdminMatchQuery(request));
                _platform.RecordAdminRead(authenticated.Account, permission, "match", "read-list", "matches",
                    AuditContext(request, permission), request.HttpContext.RequestAborted);
                return Results.Ok(page);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_match_query", error.Message, StatusCodes.Status400BadRequest);
            }
        });
        _app.MapGet("/api/admin/matches/{matchId}", async (HttpRequest request, string matchId) =>
        {
            const L12Permission permission = L12Permission.AdminMatchesRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            var includeReplay = string.Equals(request.Query["includeReplay"], "true",
                StringComparison.OrdinalIgnoreCase);
            try
            {
                if (includeReplay && await _recorder.IsPlayerReplayPayloadExpiredAsync(matchId,
                        request.HttpContext.RequestAborted))
                {
                    _platform.RecordAdminRead(authenticated.Account, permission, "match",
                        "read-replay-payload-expired", matchId, AuditContext(request, permission), request.HttpContext.RequestAborted);
                    return ApiError(request, "replay_payload_expired",
                        "该休闲／好友房录像已按保留规则清理；对局摘要与结算结果仍保留。",
                        StatusCodes.Status410Gone);
                }
                var match = await _recorder.GetAdminMatchAsync(matchId, includeReplay,
                    request.HttpContext.RequestAborted);
                var expired = match is null && await _recorder.IsSandboxReplayExpiredAsync(matchId,
                    request.HttpContext.RequestAborted);
                var replayPayloadExpired = includeReplay && match is not null
                    && match.Summary.CommandCount == 0
                    && (match.Summary.EndedUtc is not null
                        || string.Equals(match.Summary.ModeId, "sandbox", StringComparison.Ordinal));
                _platform.RecordAdminRead(authenticated.Account, permission, "match",
                    expired ? "read-sandbox-replay-expired"
                        : replayPayloadExpired ? "read-replay-payload-expired"
                        : includeReplay ? "read-replay" : "read-detail", matchId,
                    AuditContext(request, permission), request.HttpContext.RequestAborted);
                if (expired)
                    return ApiError(request, "sandbox_replay_expired", "沙盒录像已过期，Bug 报告仍已保留。",
                        StatusCodes.Status410Gone);
                if (replayPayloadExpired)
                    return ApiError(request, "replay_payload_expired",
                        "回放载荷已清理或不可用；对局摘要与结算结果仍保留。",
                        StatusCodes.Status410Gone);
                return match is null ? Results.NotFound() : Results.Ok(match);
            }
            catch (InvalidDataException error) when (MatchRecorder.TryGetReplayIncompatibility(error, out _))
            {
                _ = MatchRecorder.TryGetReplayIncompatibility(error, out var evidence);
                Console.Error.WriteLine($"Replay reconstruction rejected (admin-detail; correlation={CorrelationId(request)}): reason={evidence!.Reason}; sequence={evidence.Sequence?.ToString() ?? "none"}; type={evidence.CommandType ?? "none"}; dimension={evidence.Dimension}");
                return ApiError(request, "replay_incompatible",
                    "这场回放暂时无法还原，对局档案仍可查看。",
                    StatusCodes.Status409Conflict);
            }
            catch (L12ReplayPayloadTooLargeException error)
            {
                _platform.RecordAdminRead(authenticated.Account, permission, "match", "read-replay-rejected",
                    matchId, AuditContext(request, permission) with { Outcome = "rejected", Reason = "replay-too-large" }, request.HttpContext.RequestAborted);
                return ApiError(request, "replay_too_large",
                    $"回放超过在线读取上限（最多 {error.MaximumCommands} 条命令或 {error.MaximumBytes / 1024 / 1024} MiB），已在反序列化前拒绝以保护服务。",
                    StatusCodes.Status413PayloadTooLarge);
            }
        });
        _app.MapGet("/api/admin/matches/{matchId}/replay", async (HttpRequest request,
            string matchId, int? limit) =>
        {
            const L12Permission permission = L12Permission.AdminMatchesRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            try
            {
                if (await _recorder.IsPlayerReplayPayloadExpiredAsync(matchId,
                        request.HttpContext.RequestAborted))
                {
                    _platform.RecordAdminRead(authenticated.Account, permission, "match",
                        "read-replay-payload-expired", matchId, AuditContext(request, permission), request.HttpContext.RequestAborted);
                    return ApiError(request, "replay_payload_expired",
                        "该休闲／好友房录像已按保留规则清理；对局摘要与结算结果仍保留。",
                        StatusCodes.Status410Gone);
                }
                var page = await _recorder.GetAdminReplayPageAsync(matchId, QueryValue(request, "cursor"),
                    limit ?? 50, request.HttpContext.RequestAborted);
                var expired = page is null && await _recorder.IsSandboxReplayExpiredAsync(matchId,
                    request.HttpContext.RequestAborted);
                var replayPayloadExpired = page is { TotalCommands: 0 };
                _platform.RecordAdminRead(authenticated.Account, permission, "match",
                    expired ? "read-sandbox-replay-expired"
                        : replayPayloadExpired ? "read-replay-payload-expired" : "read-replay-page",
                    matchId, AuditContext(request, permission), request.HttpContext.RequestAborted);
                if (expired)
                    return ApiError(request, "sandbox_replay_expired", "沙盒录像已过期，Bug 报告仍已保留。",
                        StatusCodes.Status410Gone);
                if (replayPayloadExpired)
                    return ApiError(request, "replay_payload_expired",
                        "回放载荷已清理或不可用；对局摘要与结算结果仍保留。",
                        StatusCodes.Status410Gone);
                return page is null ? Results.NotFound() : Results.Ok(page);
            }
            catch (InvalidDataException error) when (MatchRecorder.TryGetReplayIncompatibility(error, out _))
            {
                _ = MatchRecorder.TryGetReplayIncompatibility(error, out var evidence);
                Console.Error.WriteLine($"Replay reconstruction rejected (admin-page; correlation={CorrelationId(request)}): reason={evidence!.Reason}; sequence={evidence.Sequence?.ToString() ?? "none"}; type={evidence.CommandType ?? "none"}; dimension={evidence.Dimension}");
                return ApiError(request, "replay_incompatible",
                    "这场回放暂时无法还原，对局档案仍可查看。",
                    StatusCodes.Status409Conflict);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_replay_cursor", error.Message,
                    StatusCodes.Status400BadRequest);
            }
            catch (L12ReplayPayloadTooLargeException error)
            {
                _platform.RecordAdminRead(authenticated.Account, permission, "match", "read-replay-page-rejected",
                    matchId, AuditContext(request, permission) with { Outcome = "rejected", Reason = "replay-row-too-large" }, request.HttpContext.RequestAborted);
                return ApiError(request, "replay_page_too_large",
                    $"单条回放状态超过分页上限（{error.MaximumBytes / 1024 / 1024} MiB），已在 JSON 反序列化前拒绝。",
                    StatusCodes.Status413PayloadTooLarge);
            }
        });
        _app.MapGet("/api/admin/players/{accountId}/matches", async (HttpRequest request, string accountId) =>
        {
            const L12Permission permission = L12Permission.AdminMatchesRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            try
            {
                var page = await _recorder.ListAdminMatchesForAccountAsync(accountId, AdminMatchQuery(request));
                _platform.RecordAdminRead(authenticated.Account, permission, "match", "read-player-matches",
                    accountId, AuditContext(request, permission), request.HttpContext.RequestAborted);
                return Results.Ok(page);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_match_query", error.Message, StatusCodes.Status400BadRequest);
            }
        });
        _app.MapGet("/api/admin/analytics/cards", async (HttpRequest request) =>
        {
            const L12Permission permission = L12Permission.AdminAnalyticsRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            var timing = new CardAnalyticsRequestTiming();
            using var cancellation = MatchRecorder.CreateCardAnalyticsRequestCancellation(
                request.HttpContext.RequestAborted, _app.Lifetime.ApplicationStopping);
            try
            {
                var page = await _recorder.ListCardAnalyticsAsync(CardAnalyticsQuery(request),
                    cancellation.Token, timing);
                _platform.RecordAdminRead(authenticated.Account, permission, "analytics", "read-card-list",
                    "cards", AuditContext(request, permission), cancellation.Token);
                return Results.Ok(page);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_analytics_query", error.Message, StatusCodes.Status400BadRequest);
            }
            catch (CardAnalyticsUnavailableException error)
            {
                return ApiError(request, error.Code, error.Message, error.StatusCode);
            }
            catch (OperationCanceledException) when (request.HttpContext.RequestAborted.IsCancellationRequested)
            {
                return Results.StatusCode(499);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            finally { request.HttpContext.Response.Headers["Server-Timing"] = timing.ToServerTiming(); }
        });
        _app.MapGet("/api/admin/analytics/cards/{cardId}", async (HttpRequest request, string cardId) =>
        {
            const L12Permission permission = L12Permission.AdminAnalyticsRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            var timing = new CardAnalyticsRequestTiming();
            using var cancellation = MatchRecorder.CreateCardAnalyticsRequestCancellation(
                request.HttpContext.RequestAborted, _app.Lifetime.ApplicationStopping);
            try
            {
                var includeRecentMatches = L12Authorization.HasPermission(authenticated.Account,
                    L12Permission.AdminMatchesRead);
                var detail = await _recorder.GetCardAnalyticsAsync(cardId, CardAnalyticsQuery(request),
                    includeRecentMatches, cancellation.Token, timing);
                _platform.RecordAdminRead(authenticated.Account, permission, "analytics", "read-card-detail",
                    cardId, AuditContext(request, permission), cancellation.Token);
                return detail is null ? Results.NotFound() : Results.Ok(detail);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_analytics_query", error.Message, StatusCodes.Status400BadRequest);
            }
            catch (CardAnalyticsUnavailableException error)
            {
                return ApiError(request, error.Code, error.Message, error.StatusCode);
            }
            catch (OperationCanceledException) when (request.HttpContext.RequestAborted.IsCancellationRequested)
            {
                return Results.StatusCode(499);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            finally { request.HttpContext.Response.Headers["Server-Timing"] = timing.ToServerTiming(); }
        });
        _app.MapGet("/api/admin/analytics/masters", async (HttpRequest request) =>
        {
            const L12Permission permission = L12Permission.AdminAnalyticsRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            var timing = new CardAnalyticsRequestTiming();
            using var cancellation = MatchRecorder.CreateCardAnalyticsRequestCancellation(
                request.HttpContext.RequestAborted, _app.Lifetime.ApplicationStopping);
            try
            {
                var query = CardAnalyticsQuery(request) with
                {
                    DisasterMode = QueryValue(request, "disasterMode"),
                    DisasterPoolId = QueryValue(request, "disasterPoolId"),
                    DisasterOrigin = QueryValue(request, "disasterOrigin"),
                };
                var report = await _recorder.ReadMasterAnalyticsAsync(query, cancellation.Token, timing);
                if (!L12Authorization.HasPermission(authenticated.Account, L12Permission.AdminMatchesRead)
                    && report.Disasters is { } disasters)
                {
                    // Even grouped single-match pick opportunities can disclose a private candidate set.
                    // Project only this response; never mutate the permission-independent cached DTO.
                    report = report with { Disasters = disasters with
                    {
                        Items = disasters.Items.Where(item => item.Action != "pick").ToArray(),
                        PrivatePickGroupsHidden = true,
                    } };
                }
                _platform.RecordAdminRead(authenticated.Account, permission, "analytics", "read-master-report",
                    report.SelectedMasterId ?? "masters", AuditContext(request, permission), request.HttpContext.RequestAborted);
                return Results.Ok(report);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_analytics_query", error.Message, StatusCodes.Status400BadRequest);
            }
            catch (CardAnalyticsUnavailableException error)
            {
                return ApiError(request, error.Code, error.Message, error.StatusCode);
            }
            catch (InvalidDataException)
            {
                return ApiError(request, "analytics_facts_unavailable", "禁选统计事实暂不可用，请缩小范围或稍后重试。",
                    StatusCodes.Status503ServiceUnavailable);
            }
            catch (OperationCanceledException) when (request.HttpContext.RequestAborted.IsCancellationRequested)
            { return Results.StatusCode(499); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
            finally { request.HttpContext.Response.Headers["Server-Timing"] = timing.ToServerTiming(); }
        });
        _app.MapGet("/api/admin/analytics/global", async (HttpRequest request) =>
        {
            const L12Permission permission = L12Permission.AdminAnalyticsRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            try
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var from = QueryGlobalAnalyticsDate(request, "fromDate", "fromUtc", "from") ?? today.AddDays(-29);
                var to = QueryGlobalAnalyticsDate(request, "toDate", "toUtc", "to") ?? today;
                var report = await _recorder.ReadGlobalAnalyticsAsync(from, to, _platform.Accounts(),
                    request.HttpContext.RequestAborted);
                _platform.RecordAdminRead(authenticated.Account, permission, "analytics", "read-global-report",
                    $"{from:yyyy-MM-dd}:{to:yyyy-MM-dd}", AuditContext(request, permission), request.HttpContext.RequestAborted);
                return Results.Ok(report);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_analytics_query", error.Message, StatusCodes.Status400BadRequest);
            }
        });
        _app.MapGet("/api/rankings", async (HttpRequest request, string? faction, string? range) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            var recordedMatches = await _recorder.ListRankedAnalyticsMatchesAsync(20_000);
            var matches = recordedMatches
                .Concat(_platform.TestRunAcceptanceRankedMatches())
                .DistinctBy(item => item.MatchId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var rolling = range is "7d" or "30d";
            var observedAt = DateTimeOffset.UtcNow;
            // A capped page is complete for this window only when its oldest row is strictly
            // earlier than the boundary; rows tied at the boundary may still be omitted.
            var windowStart = observedAt.AddDays(range == "7d" ? -7 : -30);
            var oldestRecordedStart = recordedMatches.LastOrDefault()?.StartedUtc;
            var cappedWindowComplete = DateTimeOffset.TryParse(oldestRecordedStart, out var oldestStarted)
                && oldestStarted < windowStart;
            var rawWindowComplete = !rolling || await _recorder.IsRankedAnalyticsWindowCompleteAsync(windowStart);
            var rangeLimited = rolling && (!rawWindowComplete
                || recordedMatches.Count >= 20_000 && !cappedWindowComplete);
            var players = rangeLimited ? Array.Empty<L12RankedLeaderboardEntry>()
                : rolling ? _platform.RankedIntervalLeaderboard(matches, range!, faction, 50, account?.Id, observedAt)
                : _platform.RankedLeaderboard(faction, 50, account?.Id);
            var analytics = _platform.RankedAnalytics(rangeLimited ? [] : matches, range, observedAt);
            return Results.Ok(new { players, analytics, rangeLimited });
        });
        _app.MapGet("/api/rankings/history", (int? limit) =>
            Results.Ok(_platform.RankedSeasonHistory(limit ?? 500)));
        _app.MapGet("/api/ranked/me", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(_platform.RankedOverview(account.Id));
        });
        _app.MapPost("/api/ranked/faction", (HttpRequest request, RankedFactionRequest body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            if (!_rooms.CanChangeRankedFaction(account.Id))
                return Results.Conflict(new { message = "匹配、房间、观战或对局期间不能更换派系" });
            try { return Results.Ok(_platform.SelectRankedFaction(account.Id, body.Faction ?? string.Empty)); }
            catch (ArgumentException error) { return Results.BadRequest(new { message = error.Message }); }
        });
        _app.MapPost("/api/ranked/title", (HttpRequest request, RankedTitleRequest body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            try { return Results.Ok(_platform.SelectRankedMasterTitle(account.Id, body.Title)); }
            catch (ArgumentException error) { return Results.BadRequest(new { message = error.Message }); }
        });
        _app.MapGet("/api/ranked/broadcasts", (int? limit) => Results.Ok(_platform.RankedBroadcasts(limit ?? 30)));
        _app.MapGet("/api/ranked/broadcasts/settings", () => Results.Ok(_platform.RankedBroadcastSettings()));
        _app.MapPost("/api/ranked/broadcasts/claim", (HttpRequest request,
            DateTimeOffset? subscriptionStartedAt) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(
                _platform.ClaimRankedBroadcast(account.Id, subscriptionStartedAt));
        });
        _app.MapPost("/api/ranked/broadcasts/{id}/complete", (HttpRequest request, string id,
            RankedBroadcastCompleteRequest body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            return _platform.CompleteRankedBroadcast(account.Id, id, body.ClaimToken ?? string.Empty)
                ? Results.Ok(new { completed = true })
                : Results.Conflict(new { message = "广播领取不存在或确认凭据无效" });
        });
        _app.MapGet("/api/admin/storage/private-deck-persistence", (HttpRequest request) =>
        {
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            if (!TryAuthorize(request, L12Permission.AdminOperationsRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.PrivateDeckPersistenceStatus(authenticated.Account));
        });
        _app.MapGet("/api/admin/ranked/config", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminOperationsRead, out var authenticated, out var failure)) return failure;
            return Results.Ok(_platform.RankedConfig(authenticated.Account));
        });
        _app.MapGet("/api/admin/seasons", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminOperationsRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.SeasonCatalog(authenticated.Account, _seasonActivationUtcNow()));
        });
        _app.MapGet("/api/admin/seasons/archives", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminOperationsRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.SeasonArchives(authenticated.Account));
        });
        _app.MapGet("/api/admin/seasons/archives/{seasonId}", (HttpRequest request, string seasonId) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminOperationsRead, out var authenticated, out var failure))
                return failure;
            try { return Results.Ok(_platform.SeasonArchive(authenticated.Account, seasonId)); }
            catch (L12OperationsConfigException error)
            {
                return SeasonManagementError(request, error);
            }
        });
        _app.MapGet("/api/admin/seasons/{definitionId}", (HttpRequest request, string definitionId) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminOperationsRead, out var authenticated, out var failure))
                return failure;
            try { return Results.Ok(_platform.SeasonDefinition(authenticated.Account, definitionId)); }
            catch (L12OperationsConfigException error)
            {
                return SeasonManagementError(request, error);
            }
        });
        _app.MapPost("/api/admin/seasons/draft", (HttpRequest request, SeasonDraftCreateRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var expectedVersion = body.ExpectedVersion ?? _platform.OperationsConfigVersion();
            var idempotencyKey = ResolveSeasonCommandKey(request, body.IdempotencyKey);
            var payload = new L12SeasonDraftCreateCommandPayload(body.ExpectedCurrentRevision);
            var command = CommandEnvelope(request, authenticated.Account, permission,
                "operations.config.season-draft-create", "operations:config", payload,
                idempotencyKey, expectedVersion, false, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteOperationsConfig(() => _platform.CreateSeasonDraft(current.Actor,
                    current.Payload.ExpectedCurrentRevision, current.Reason ?? string.Empty,
                    current.AuditContext)));
            var response = AdminCommandResponse(request, command, outcome);
            request.HttpContext.Response.Headers.ETag = $"\"{_platform.OperationsConfigVersion()}\"";
            return response;
        });
        _app.MapPut("/api/admin/seasons/draft/{definitionId}", (HttpRequest request,
            string definitionId, SeasonDraftUpdateRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var expectedVersion = body.ExpectedVersion ?? _platform.OperationsConfigVersion();
            try
            {
                var idempotencyKey = ResolveSeasonCommandKey(request, body.IdempotencyKey);
                var payload = new L12SeasonDefinitionApplyCommandPayload(definitionId, body.Draft,
                    body.ExpectedRevision, body.PreviewToken ?? string.Empty);
                var command = CommandEnvelope(request, authenticated.Account, permission,
                    "operations.config.season-draft-update", "operations:config", payload,
                    idempotencyKey, expectedVersion, false, body.Reason);
                var outcome = _adminCommands.Execute(command, permission,
                    current => ExecuteOperationsConfig(() =>
                    {
                        var token = current.Payload.PreviewToken;
                        if (string.IsNullOrWhiteSpace(token))
                            token = _platform.PreviewNextSeasonDefinition(current.Actor,
                                current.Payload.DefinitionId, current.Payload.Draft,
                                current.Payload.ExpectedRevision, expectedVersion,
                                current.AuditContext).PreviewToken;
                        return _platform.ApplyNextSeasonDefinition(current.Actor,
                            current.Payload.DefinitionId, current.Payload.Draft,
                            current.Payload.ExpectedRevision, expectedVersion, token,
                            current.Reason ?? string.Empty, current.AuditContext);
                    }));
                var response = AdminCommandResponse(request, command, outcome);
                request.HttpContext.Response.Headers.ETag = $"\"{_platform.OperationsConfigVersion()}\"";
                return response;
            }
            catch (L12OperationsConfigException error)
            {
                return SeasonManagementError(request, error);
            }
        });
        _app.MapDelete("/api/admin/seasons/draft/{definitionId}", (HttpRequest request,
            string definitionId, [Microsoft.AspNetCore.Mvc.FromBody] SeasonDraftDeleteRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var expectedVersion = body.ExpectedVersion ?? _platform.OperationsConfigVersion();
            var idempotencyKey = ResolveSeasonCommandKey(request, body.IdempotencyKey);
            var payload = new L12SeasonDraftDeleteCommandPayload(definitionId, body.ExpectedRevision);
            var command = CommandEnvelope(request, authenticated.Account, permission,
                "operations.config.season-draft-delete", "operations:config", payload,
                idempotencyKey, expectedVersion, false, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteOperationsConfig(() => _platform.DeleteSeasonDraft(current.Actor,
                    current.Payload.DefinitionId, current.Payload.ExpectedRevision,
                    current.Reason ?? string.Empty, current.AuditContext)));
            request.HttpContext.Response.Headers["X-Command-ID"] = outcome.Command?.Id ?? command.CommandId;
            request.HttpContext.Response.Headers.ETag = $"\"{_platform.OperationsConfigVersion()}\"";
            if (outcome.Replayed) request.HttpContext.Response.Headers["X-Idempotent-Replay"] = "true";
            return outcome.Success ? Results.NoContent()
                : ApiError(request, outcome.Code, outcome.Message, outcome.StatusCode);
        });
        _app.MapPost("/api/admin/seasons/{definitionId}/preview", (HttpRequest request,
            string definitionId, SeasonDefinitionPreviewRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var expectedVersion = body.ExpectedVersion
                ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault());
            if (expectedVersion is null)
                return ApiError(request, "expected_version_required",
                    "赛季配置预览必须提供 expectedVersion/If-Match",
                    StatusCodes.Status428PreconditionRequired);
            try
            {
                return Results.Ok(_platform.PreviewSeasonDefinition(authenticated.Account, definitionId,
                    body.Draft, body.ExpectedRevision, expectedVersion.Value,
                    AuditContext(request, permission)));
            }
            catch (L12OperationsConfigException error)
            {
                return SeasonManagementError(request, error);
            }
        });
        _app.MapPut("/api/admin/seasons/{definitionId}", (HttpRequest request,
            string definitionId, SeasonDefinitionApplyRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryOperationsCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12SeasonDefinitionApplyCommandPayload(definitionId, body.Draft,
                body.ExpectedRevision, body.PreviewToken ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission,
                "operations.config.season-definition-apply", "operations:config", payload,
                key, expected, false, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteOperationsConfig(() => _platform.ApplySeasonDefinition(current.Actor,
                    current.Payload.DefinitionId, current.Payload.Draft, current.Payload.ExpectedRevision,
                    expected, current.Payload.PreviewToken, current.Reason ?? string.Empty,
                    current.AuditContext)));
            if (outcome.Success && outcome.Value?.Slot == "current") NotifyOperationsPolicyChanged();
            var response = AdminCommandResponse(request, command, outcome);
            request.HttpContext.Response.Headers.ETag = $"\"{_platform.OperationsConfigVersion()}\"";
            return response;
        });
        _app.MapPost("/api/admin/seasons/draft/{definitionId}/activate", async (HttpRequest request,
            string definitionId, SeasonActivationRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryOperationsCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12SeasonActivationCommandPayload(definitionId,
                body.ExpectedCurrentRevision, body.ExpectedDraftRevision);
            var command = CommandEnvelope(request, authenticated.Account, permission,
                "operations.config.season-activate", "operations:config", payload, key, expected,
                false, body.Reason);
            var outcome = await _rooms.ExecuteRankedSeasonCutoverAsync(readiness =>
                _adminCommands.Execute(command, permission,
                    current => ExecuteOperationsConfig(() => _platform.ActivateSeason(current.Actor,
                        current.Payload.DefinitionId, current.Payload.ExpectedCurrentRevision,
                        current.Payload.ExpectedDraftRevision, current.Reason ?? string.Empty,
                        readiness, current.AuditContext)), risk: L12AdminCommandRisk.High));
            if (outcome.Success)
            {
                NotifyOperationsPolicyChanged();
                if (outcome.Value is not null)
                    NotifySeasonSummaryNotificationsChanged(
                        _platform.SeasonSummaryRecipients(outcome.Value.Archive.SeasonId));
            }
            var response = AdminCommandResponse(request, command, outcome);
            request.HttpContext.Response.Headers.ETag = $"\"{_platform.OperationsConfigVersion()}\"";
            return response;
        });
        _app.MapPost("/api/admin/seasons/draft/{definitionId}/activation-preview", async (
            HttpRequest request, string definitionId, SeasonActivationPreviewRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var expectedVersion = body.ExpectedVersion
                ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault());
            if (expectedVersion is null)
                return ApiError(request, "expected_version_required",
                    "切季影响预览必须提供 expectedVersion/If-Match",
                    StatusCodes.Status428PreconditionRequired);
            try
            {
                var catalog = _platform.SeasonCatalog(authenticated.Account,
                    _seasonActivationUtcNow());
                var preview = await _rooms.InspectRankedSeasonCutoverSnapshotAsync(
                    catalog.Current.SeasonId, readiness =>
                    {
                        var observedAt = _seasonActivationUtcNow();
                        return _platform.PreviewSeasonActivation(authenticated.Account, definitionId,
                            body.ExpectedCurrentRevision, body.ExpectedDraftRevision,
                            expectedVersion.Value, readiness, observedAt);
                    });
                return Results.Ok(preview);
            }
            catch (L12OperationsConfigException error)
            {
                return SeasonManagementError(request, error);
            }
        });
        _app.MapPost("/api/admin/ranked/season-reset-repair/preview", async (HttpRequest request,
            RankedSeasonResetRepairPreviewRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var expectedVersion = body.ExpectedVersion
                ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault());
            if (expectedVersion is null)
                return ApiError(request, "expected_version_required",
                    "T01 修复预览必须提供 expectedVersion/If-Match",
                    StatusCodes.Status428PreconditionRequired);
            try
            {
                var preview = await _rooms.InspectRankedSeasonCutoverSnapshotAsync("T01", readiness =>
                    _platform.PreviewT01RankedSeasonReset(authenticated.Account,
                        body.SeasonId ?? string.Empty, expectedVersion.Value, readiness,
                        _seasonActivationUtcNow(), body.CompetitiveStartAt));
                return Results.Ok(preview);
            }
            catch (L12OperationsConfigException error)
            {
                return SeasonManagementError(request, error);
            }
        });
        _app.MapPost("/api/admin/ranked/season-reset-repair", async (HttpRequest request,
            RankedSeasonResetRepairRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryOperationsCommandOptions(request, authenticated.Account, permission,
                    body.IdempotencyKey, body.ExpectedVersion, out var key, out var expected,
                    out failure)) return failure;
            var payload = new L12RankedSeasonResetRepairCommandPayload(body.SeasonId ?? string.Empty,
                body.ExpectedEvidenceFingerprint ?? string.Empty, body.CompetitiveStartAt);
            var command = CommandEnvelope(request, authenticated.Account, permission,
                "operations.config.ranked-season-reset-repair", "operations:config", payload,
                key, expected, false, body.Reason);
            var outcome = await _rooms.ExecuteRankedSeasonCutoverAsync(readiness =>
                _adminCommands.Execute(command, permission,
                    current => ExecuteOperationsConfig(() =>
                        _platform.RepairT01RankedSeasonReset(current.Actor,
                            current.Payload.SeasonId, current.Reason ?? string.Empty, expected,
                            readiness, current.AuditContext, current.Payload.ExpectedEvidenceFingerprint,
                            _seasonActivationUtcNow(), current.Payload.CompetitiveStartAt)),
                    risk: L12AdminCommandRisk.High));
            if (outcome.Success) NotifyOperationsPolicyChanged();
            var response = AdminCommandResponse(request, command, outcome);
            request.HttpContext.Response.Headers.ETag =
                $"\"{_platform.OperationsConfigVersion()}\"";
            return response;
        });
        _app.MapPost("/api/admin/seasons/identity-normalization/preview", async (HttpRequest request,
            SeasonIdentityMigrationPreviewRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var expectedVersion = body.ExpectedVersion
                ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault());
            if (expectedVersion is null)
                return ApiError(request, "expected_version_required",
                    "赛季编号迁移预览必须提供 expectedVersion/If-Match",
                    StatusCodes.Status428PreconditionRequired);
            if (expectedVersion.Value != _platform.OperationsConfigVersion())
                return ApiError(request, "operations_version_conflict",
                    "运营配置已变化，请刷新后重试", StatusCodes.Status409Conflict);
            try
            {
                return Results.Ok(await _rooms.PreviewSeasonIdentityNormalizationAsync(
                    authenticated.Account, _seasonActivationUtcNow()));
            }
            catch (L12OperationsConfigException error)
            {
                return SeasonManagementError(request, error);
            }
            catch (L12SeasonIdentityMigrationException error)
            {
                return ApiError(request, error.Code, error.Message, StatusCodes.Status409Conflict);
            }
        });
        _app.MapPost("/api/admin/seasons/identity-normalization", async (HttpRequest request,
            SeasonIdentityMigrationRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var expectedVersion = body.ExpectedVersion
                ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault());
            if (expectedVersion is null)
                return ApiError(request, "expected_version_required",
                    "赛季编号迁移必须提供 expectedVersion/If-Match",
                    StatusCodes.Status428PreconditionRequired);
            if (expectedVersion.Value != _platform.OperationsConfigVersion())
                return ApiError(request, "operations_version_conflict",
                    "运营配置已变化，请刷新后重试", StatusCodes.Status409Conflict);
            var idempotencyKey = body.IdempotencyKey?.Trim() ?? string.Empty;
            if (idempotencyKey.Length is < 8 or > 80)
                return ApiError(request, "idempotency_key_required",
                    "赛季编号迁移需要 8-80 位幂等键", StatusCodes.Status400BadRequest);
            try
            {
                var owner = $"{_seasonActivationWorkerId}:season-identity:{idempotencyKey}";
                if (owner.Length > 120) owner = owner[..120];
                var result = await _rooms.ExecuteSeasonIdentityNormalizationAsync(
                    authenticated.Account, body.ExpectedPlatformFingerprint ?? string.Empty,
                    body.ExpectedRecorderFingerprint ?? string.Empty, owner,
                    body.Reason ?? string.Empty, _seasonActivationUtcNow(),
                    RequestAuditContext(request, permission) with { IdempotencyKey = idempotencyKey,
                        ExpectedVersion = expectedVersion, Reason = body.Reason });
                NotifyOperationsPolicyChanged();
                request.HttpContext.Response.Headers.ETag =
                    $"\"{_platform.OperationsConfigVersion()}\"";
                return Results.Ok(result);
            }
            catch (L12OperationsConfigException error)
            {
                return SeasonManagementError(request, error);
            }
            catch (L12SeasonIdentityMigrationException error)
            {
                return ApiError(request, error.Code, error.Message, StatusCodes.Status409Conflict);
            }
        });
        _app.MapPost("/api/admin/seasons/draft/{definitionId}/arm", async (HttpRequest request,
            string definitionId, SeasonActivationArmRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryOperationsCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12SeasonActivationArmCommandPayload(definitionId,
                body.ExpectedCurrentRevision, body.ExpectedDraftRevision,
                body.ImpactPreviewToken ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission,
                "operations.config.season-activation-arm", "operations:config", payload, key,
                expected, false, body.Reason);
            var catalog = _platform.SeasonCatalog(authenticated.Account,
                _seasonActivationUtcNow());
            var outcome = await _rooms.InspectRankedSeasonCutoverSnapshotAsync(
                catalog.Current.SeasonId, readiness =>
                {
                    var observedAt = _seasonActivationUtcNow();
                    return _adminCommands.Execute(command, permission,
                        current => ExecuteOperationsConfig(() => _platform.ArmSeasonActivation(
                            current.Actor, current.Payload.DefinitionId,
                            current.Payload.ExpectedCurrentRevision,
                            current.Payload.ExpectedDraftRevision, expected,
                            current.Payload.ImpactPreviewToken, readiness,
                            current.Reason ?? string.Empty, observedAt, current.AuditContext)),
                        risk: L12AdminCommandRisk.High);
                });
            if (outcome.Success) ScheduleSeasonActivationEvaluation();
            var response = AdminCommandResponse(request, command, outcome);
            request.HttpContext.Response.Headers.ETag = $"\"{_platform.OperationsConfigVersion()}\"";
            return response;
        });
        _app.MapPost("/api/admin/seasons/draft/{definitionId}/disarm", (HttpRequest request,
            string definitionId, SeasonActivationDisarmRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryOperationsCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12SeasonActivationDisarmCommandPayload(definitionId,
                body.ExpectedDraftRevision, body.ExpectedPlanGeneration,
                body.DisarmGuardToken ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission,
                "operations.config.season-activation-disarm", "operations:config", payload, key,
                expected, false, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteOperationsConfig(() => _platform.DisarmSeasonActivation(current.Actor,
                    current.Payload.DefinitionId, current.Payload.ExpectedDraftRevision,
                    current.Payload.ExpectedPlanGeneration, current.Payload.DisarmGuardToken,
                    current.Reason ?? string.Empty, _seasonActivationUtcNow(), current.AuditContext)),
                risk: L12AdminCommandRisk.High);
            if (outcome.Success) ScheduleSeasonActivationEvaluation();
            var response = AdminCommandResponse(request, command, outcome);
            request.HttpContext.Response.Headers.ETag = $"\"{_platform.OperationsConfigVersion()}\"";
            return response;
        });
        _app.MapPut("/api/admin/ranked/config", (HttpRequest request, RankedConfigRequest body) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminOperationsWrite, out var authenticated, out var failure)) return failure;
            try
            {
                return Results.Ok(_platform.UpdateRankedConfig(authenticated.Account, body.Config,
                    body.Reason ?? string.Empty, new L12AdminAuditContext(CorrelationId(request),
                        Permission: L12Authorization.Key(L12Permission.AdminOperationsWrite),
                        Reason: body.Reason, RequestMethod: "PUT",
                        RequestPath: request.Path.Value ?? "/api/admin/ranked/config")));
            }
            catch (L12OperationsConfigException error) { return Results.BadRequest(new { code = error.Code, message = error.Message }); }
        });
        _app.MapGet("/api/admin/ranked/integrity-audits", (HttpRequest request, string? accountId,
            string? matchId, bool? reviewOnly, int? limit) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminAuditRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.RankedIntegrityAudits(authenticated.Account, accountId, matchId,
                reviewOnly ?? false, limit ?? 200));
        });
        _app.MapDelete("/api/admin/ranked/broadcasts/{id}", (HttpRequest request, string id) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminOperationsWrite, out var authenticated, out var failure)) return failure;
            return _platform.DeleteRankedBroadcast(authenticated.Account, id,
                new L12AdminAuditContext(CorrelationId(request),
                    Permission: L12Authorization.Key(L12Permission.AdminOperationsWrite),
                    RequestMethod: "DELETE", RequestPath: request.Path.Value ?? string.Empty))
                ? Results.NoContent() : Results.NotFound();
        });
        _app.MapPost("/api/auth/register", (AuthRequest request) =>
        {
            var result = _platform.Register(request.Username ?? string.Empty, request.Password ?? string.Empty);
            return result.Success ? Results.Ok(new { result.Message, result.Account, result.Token }) : Results.BadRequest(new { result.Message });
        });
        _app.MapPost("/api/auth/login", (HttpRequest request, AuthRequest body) =>
        {
            var clientKey = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
            var result = _platform.Login(body.Username ?? string.Empty, body.Password ?? string.Empty,
                new L12LoginAttemptContext(CorrelationId(request), clientKey,
                    request.HttpContext.Items.TryGetValue(L12CorrelationIds.OriginalPathItemName, out var path)
                        ? path as string ?? request.Path.Value ?? "/api/auth/login"
                        : request.Path.Value ?? "/api/auth/login"));
            if (result.Success)
            {
                _rankedDevicePrivacy.EnsureCookie(request.HttpContext);
                return Results.Ok(new { result.Message, result.Account, result.Token });
            }
            if (result.RetryAfterSeconds > 0)
                request.HttpContext.Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString();
            return ApiError(request, result.Code, result.Message,
                result.Code == "login_rate_limited"
                    ? StatusCodes.Status429TooManyRequests
                    : StatusCodes.Status401Unauthorized);
        });
        _app.MapGet("/api/auth/mfa/capability", () => Results.Ok(_platform.MfaCapability()));
        _app.MapGet("/api/auth/email/capability", () => Results.Ok(_platform.EmailCapability()));
        _app.MapGet("/api/auth/me", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is not null) _rankedDevicePrivacy.EnsureCookie(request.HttpContext);
            return account is null ? Results.Unauthorized() : Results.Ok(account);
        });
        _app.MapPost("/api/auth/change-password", (HttpRequest request, ChangePasswordRequest body) =>
        {
            var authenticated = _platform.AuthenticateSession(request.Headers.Authorization);
            if (authenticated is null)
                return ApiError(request, "authentication_required", "请先登录账号", StatusCodes.Status401Unauthorized);
            var result = _platform.ChangePassword(authenticated.Account.Id, body.CurrentPassword ?? string.Empty,
                body.NewPassword ?? string.Empty, authenticated.SessionId);
            return result.Success ? Results.Ok(new { result.Message }) : Results.BadRequest(new { result.Message });
        });
        _app.MapPost("/api/auth/change-username", (HttpRequest request, ChangeUsernameRequest body) =>
        {
            var authenticated = _platform.AuthenticateSession(request.Headers.Authorization);
            if (authenticated is null)
                return ApiError(request, "authentication_required", "请先登录账号", StatusCodes.Status401Unauthorized);
            var result = _platform.ChangeUsername(authenticated.Account.Id, body.CurrentPassword ?? string.Empty,
                body.NewUsername ?? string.Empty, authenticated.SessionId);
            return result.Success ? Results.Ok(new { result.Message, result.Account })
                : Results.BadRequest(new { result.Message });
        });
        _app.MapGet("/api/me/statistics", async (HttpRequest request, string? range) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            try
            {
                var recorded = await _recorder.PlayerStatisticsAsync(account.Id, account.Username,
                    _platform.RankedIntegrityExcludedMatchIds(), _platform.StatisticsExcludedAccountIds(),
                    range ?? "season", _platform.CaptureOperationsPolicy().Season,
                    request.HttpContext.RequestAborted);
                return Results.Ok(_platform.MergeTestRunAcceptanceStatistics(account.Id, recorded));
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_statistics_range", error.Message,
                    StatusCodes.Status400BadRequest);
            }
        });
        _app.MapGet("/api/auth/username-change-status", (HttpRequest request) =>
        {
            var authenticated = _platform.AuthenticateSession(request.Headers.Authorization);
            return authenticated is null ? ApiError(request, "authentication_required", "请先登录账号", StatusCodes.Status401Unauthorized)
                : Results.Ok(_platform.UsernameChangeStatus(authenticated.Account.Id));
        });
        _app.MapPost("/api/auth/username-change", (HttpRequest request, ChangeUsernameRequest body) =>
        {
            var authenticated = _platform.AuthenticateSession(request.Headers.Authorization);
            if (authenticated is null)
                return ApiError(request, "authentication_required", "请先登录账号", StatusCodes.Status401Unauthorized);
            var result = _platform.SelfServiceChangeUsername(authenticated.Account.Id, body.CurrentPassword ?? string.Empty,
                body.NewUsername ?? string.Empty, authenticated.SessionId);
            return result.Success ? Results.Ok(new { result.Message, result.Account }) : Results.BadRequest(new { result.Message });
        });
        _app.MapPost("/api/auth/username-change-requests", (HttpRequest request, UsernameChangeApplicationRequest body) =>
        {
            var authenticated = _platform.AuthenticateSession(request.Headers.Authorization);
            if (authenticated is null)
                return ApiError(request, "authentication_required", "请先登录账号", StatusCodes.Status401Unauthorized);
            try { return Results.Ok(_platform.SubmitUsernameChangeRequest(authenticated.Account.Id,
                body.NewUsername ?? string.Empty, body.Reason ?? string.Empty)); }
            catch (KeyNotFoundException error) { return ApiError(request, "account_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (InvalidOperationException error) { return ApiError(request, "username_change_unavailable", error.Message, StatusCodes.Status409Conflict); }
            catch (ArgumentException error) { return ApiError(request, "username_change_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        _app.MapPut("/api/auth/audio-preferences", (HttpRequest request, L12AudioPreferencesView body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized()
                : Results.Ok(_platform.UpdateAudioPreferences(account.Id, body));
        });
        _app.MapGet("/api/auth/email", (HttpRequest request) =>
        {
            var authenticated = _platform.AuthenticateSession(request.Headers.Authorization);
            return authenticated is null
                ? ApiError(request, "authentication_required", "请先登录账号", StatusCodes.Status401Unauthorized)
                : Results.Ok(_platform.EmailStatus(authenticated.Account.Id));
        });
        _app.MapPost("/api/auth/email/bind", (HttpRequest request, EmailBindingRequest body) =>
        {
            var authenticated = _platform.AuthenticateSession(request.Headers.Authorization);
            if (authenticated is null)
                return ApiError(request, "authentication_required", "请先登录账号", StatusCodes.Status401Unauthorized);
            try
            {
                var result = _platform.RequestEmailBinding(authenticated.Account.Id,
                    body.CurrentPassword ?? string.Empty, body.Email ?? string.Empty, ClientKey(request));
                if (result.RetryAfterSeconds > 0) request.HttpContext.Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString();
                return result.Success ? Results.Json(new { result.Code, result.Message }, statusCode: StatusCodes.Status202Accepted)
                    : ApiError(request, result.Code, result.Message,
                        result.RetryAfterSeconds > 0 ? StatusCodes.Status429TooManyRequests
                            : result.Code is "mail_unavailable" or "email_feature_disabled" ? StatusCodes.Status503ServiceUnavailable
                            : StatusCodes.Status400BadRequest);
            }
            catch
            {
                return ApiError(request, "email_binding_failed", "邮箱绑定请求处理失败",
                    StatusCodes.Status500InternalServerError);
            }
        });
        _app.MapPost("/api/auth/email/unbind", (HttpRequest request, CurrentPasswordRequest body) =>
        {
            var authenticated = _platform.AuthenticateSession(request.Headers.Authorization);
            if (authenticated is null)
                return ApiError(request, "authentication_required", "请先登录账号", StatusCodes.Status401Unauthorized);
            var result = _platform.UnbindEmail(authenticated.Account.Id, body.CurrentPassword ?? string.Empty);
            return result.Success ? Results.Ok(new { result.Code, result.Message })
                : ApiError(request, result.Code, result.Message, StatusCodes.Status400BadRequest);
        });
        _app.MapPost("/api/auth/email/verify", (HttpRequest request, TokenRequest body) =>
        {
            var result = _platform.VerifyEmail(body.Token ?? string.Empty, ClientKey(request));
            if (result.RetryAfterSeconds > 0) request.HttpContext.Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString();
            return result.Success ? Results.Ok(new { result.Code, result.Message })
                : ApiError(request, result.Code, result.Message,
                    result.RetryAfterSeconds > 0 ? StatusCodes.Status429TooManyRequests
                        : result.Code == "email_feature_disabled" ? StatusCodes.Status503ServiceUnavailable
                        : StatusCodes.Status400BadRequest);
        });
        _app.MapPost("/api/auth/password/forgot", (HttpRequest request, ForgotPasswordRequest body) =>
        {
            var result = _platform.RequestPasswordReset(body.Email ?? string.Empty, ClientKey(request));
            if (result.RetryAfterSeconds > 0) request.HttpContext.Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString();
            return result.RetryAfterSeconds > 0
                ? ApiError(request, result.Code, result.Message, StatusCodes.Status429TooManyRequests)
                : !result.Success
                    ? ApiError(request, result.Code, result.Message,
                        result.Code == "email_feature_disabled" ? StatusCodes.Status503ServiceUnavailable
                            : StatusCodes.Status400BadRequest)
                : Results.Json(new { result.Code, result.Message }, statusCode: StatusCodes.Status202Accepted);
        });
        _app.MapPost("/api/auth/password/reset", (HttpRequest request, PasswordResetRequest body) =>
        {
            var result = _platform.ResetPassword(body.Token ?? string.Empty, body.NewPassword ?? string.Empty,
                ClientKey(request));
            if (result.RetryAfterSeconds > 0) request.HttpContext.Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString();
            return result.Success ? Results.Ok(new { result.Code, result.Message })
                : ApiError(request, result.Code, result.Message,
                    result.RetryAfterSeconds > 0 ? StatusCodes.Status429TooManyRequests
                        : result.Code == "email_feature_disabled" ? StatusCodes.Status503ServiceUnavailable
                        : StatusCodes.Status400BadRequest);
        });
        _app.MapGet("/api/auth/sessions", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.SessionsReadOwn, out var authenticated, out var failure)) return failure;
            return Results.Ok(_platform.Sessions(authenticated.Account.Id, authenticated.SessionId));
        });
        _app.MapDelete("/api/auth/sessions/current", async (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.SessionsRevokeOwn, out var authenticated, out var failure)) return failure;
            return await OwnedSessionRevocationResponse(request, () => _platform.RevokeOwnSessionAsync(
                authenticated, authenticated.SessionId, AuditContext(request, L12Permission.SessionsRevokeOwn),
                request.HttpContext.RequestAborted));
        });
        _app.MapDelete("/api/auth/sessions/{sessionId}", async (HttpRequest request, string sessionId) =>
        {
            if (!TryAuthorize(request, L12Permission.SessionsRevokeOwn, out var authenticated, out var failure)) return failure;
            return await OwnedSessionRevocationResponse(request, () => _platform.RevokeOwnSessionAsync(
                authenticated, sessionId, AuditContext(request, L12Permission.SessionsRevokeOwn),
                request.HttpContext.RequestAborted));
        });
        _app.MapDelete("/api/auth/sessions", async (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.SessionsRevokeOwn, out var authenticated, out var failure)) return failure;
            return await OwnedSessionRevocationResponse(request, () => _platform.RevokeOwnSessionsAsync(authenticated,
                context: AuditContext(request, L12Permission.SessionsRevokeOwn),
                cancellationToken: request.HttpContext.RequestAborted));
        });
        _app.MapDelete("/api/auth/sessions/others", async (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.SessionsRevokeOwn, out var authenticated, out var failure)) return failure;
            return await OwnedSessionRevocationResponse(request, () => _platform.RevokeOwnSessionsAsync(authenticated, others: true,
                context: AuditContext(request, L12Permission.SessionsRevokeOwn),
                cancellationToken: request.HttpContext.RequestAborted));
        });
        _app.MapGet("/api/players", (HttpRequest request, string? search) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(_platform.FindPlayers(account.Id, search)
                .Select(player => new { player.AccountId, player.Username, player.Status, player.Direction,
                    player.CreatedAt, online = _rooms.IsAccountOnline(player.AccountId) }));
        });
        _app.MapGet("/api/presence", async (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            await _recorder.RecordSiteActivityAsync(account.Id, null, _rooms.RuntimeStats().OnlineAccountCount,
                request.HttpContext.RequestAborted);
            return Results.Ok(PresenceFor(account.Id));
        });
        _app.MapPost("/api/telemetry/page-view", async (HttpRequest request, TelemetryPageViewRequest body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            await _recorder.RecordSiteActivityAsync(account?.Id, body.Path,
                _rooms.RuntimeStats().OnlineAccountCount, request.HttpContext.RequestAborted);
            return Results.NoContent();
        });
        _app.MapGet("/api/friends", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(_platform.Friends(account.Id)
                .Select(player => new { player.AccountId, player.Username, player.Status, player.Direction,
                    player.CreatedAt, online = _rooms.IsAccountOnline(player.AccountId) }));
        });
        _app.MapGet("/api/friends/requests", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(_platform.FriendRequests(account.Id));
        });
        _app.MapGet("/api/friends/overview", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(new
            {
                friends = _platform.Friends(account.Id)
                    .Select(player => new { player.AccountId, player.Username, player.Status, player.Direction,
                        player.CreatedAt, online = _rooms.IsAccountOnline(player.AccountId) }),
                requests = _platform.FriendRequests(account.Id),
                blocked = _platform.BlockedAccounts(account.Id),
            });
        });
        _app.MapPost("/api/friends/requests", (HttpRequest request, FriendRequest body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            var result = _platform.SendFriendRequest(account.Id, body.AccountId ?? string.Empty);
            if (result.Success) NotifyFriendsChanged(account.Id, body.AccountId ?? string.Empty);
            return result.Success ? Results.Ok(new { result.Message }) : Results.BadRequest(new { result.Message });
        });
        _app.MapPost("/api/friends/requests/{requesterId}/resolve", (HttpRequest request, string requesterId, FriendResolveRequest body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            var result = _platform.ResolveFriendRequest(account.Id, requesterId, body.Accept);
            if (result.Success) NotifyFriendsChanged(account.Id, requesterId);
            return result.Success ? Results.Ok(new { result.Message }) : Results.BadRequest(new { result.Message });
        });
        _app.MapDelete("/api/friends/{friendId}", (HttpRequest request, string friendId) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            if (!TryBeginChatRelationshipMutation(account.Id, friendId, out var mutation))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            try
            {
                var removed = _platform.RemoveFriend(account.Id, friendId);
                mutation?.Complete(removed ? L12ChatMutationOutcome.Committed : L12ChatMutationOutcome.KnownRollback);
                if (removed) NotifyFriendsChanged(account.Id, friendId);
                return removed ? Results.Ok() : Results.NotFound();
            }
            finally { FinishChatRelationshipMutation(mutation, account.Id, friendId); }
        });
        _app.MapGet("/api/friends/blocked", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(_platform.BlockedAccounts(account.Id));
        });
        _app.MapPost("/api/friends/blocked", (HttpRequest request, FriendRequest body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            var targetId = body.AccountId ?? string.Empty;
            if (!TryBeginChatRelationshipMutation(account.Id, targetId, out var mutation))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            try
            {
                var result = _platform.BlockAccount(account.Id, targetId);
                mutation?.Complete(result.Success ? L12ChatMutationOutcome.Committed : L12ChatMutationOutcome.KnownRollback);
                if (result.Success) NotifyFriendsChanged(account.Id, targetId);
                return result.Success ? Results.Ok(new { result.Message }) : Results.BadRequest(new { result.Message });
            }
            finally { FinishChatRelationshipMutation(mutation, account.Id, targetId); }
        });
        _app.MapDelete("/api/friends/blocked/{accountId}", (HttpRequest request, string accountId) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            if (!TryBeginChatRelationshipMutation(account.Id, accountId, out var mutation))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            try
            {
                var removed = _platform.UnblockAccount(account.Id, accountId);
                mutation?.Complete(removed ? L12ChatMutationOutcome.Committed : L12ChatMutationOutcome.KnownRollback);
                if (removed) NotifyFriendsChanged(account.Id, accountId);
                return removed ? Results.Ok() : Results.NotFound();
            }
            finally { FinishChatRelationshipMutation(mutation, account.Id, accountId); }
        });
        _app.MapGet("/api/decks", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(_platform.Decks(account.Id));
        });
        _app.MapGet("/api/decks/summaries", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            if (!L12PrivateDeckQuery.TryParse(request.Query, out var query))
                return Results.BadRequest(new { message = "牌库摘要查询参数无效" });
            var page = _platform.PrivateDeckSummaries(account, _catalog, query,
                includeCover: L12DeckCoverCapability.IsEnabled(request.Headers));
            return page is null ? Results.Unauthorized() : Results.Ok(page);
        });
        _app.MapGet("/api/decks/by-id/{id}", (HttpRequest request, string id) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            if (!L12PrivateDeckQuery.TryParseRevision(request.Query, out var revision))
                return Results.BadRequest(new { message = "expectedRevision 必须为正整数" });
            var result = _platform.ReadPrivateDeck(account, id, revision);
            return result.Status switch
            {
                "ok" => Results.Ok(result.Deck),
                "unauthorized" => Results.Unauthorized(),
                "revision_conflict" => Results.Conflict(new
                {
                    code = "deck_revision_conflict",
                    message = "牌库已被其他操作更新，请刷新后重试",
                    currentRevision = result.CurrentRevision,
                }),
                _ => Results.NotFound(),
            };
        });
        _app.MapPost("/api/decks", (HttpRequest request, L12CustomDeckSubmission submission) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            if (!L12DeckValidator.TryValidate(_catalog, submission, out var deck, out var error))
                return Results.BadRequest(new { message = error });
            var result = _platform.CreateDeck(account.Id, deck);
            return result.Status switch
            {
                "ok" => Results.Created($"/api/decks/by-id/{result.Deck!.Id}", result.Deck),
                "name_conflict" => Results.Conflict(new
                {
                    code = "deck_name_conflict",
                    message = "已存在同名牌库",
                }),
                "storage_conflict" => StorageConflictResponse(request),
                _ => Results.BadRequest(new { message = "无法创建牌库" }),
            };
        });
        _app.MapPut("/api/decks/by-id/{id}", (HttpRequest request, string id, AccountDeckUpdateRequest body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            if (body.ExpectedRevision < 1)
                return Results.BadRequest(new { message = "expectedRevision 必须为正整数" });
            if (body.Deck is null) return Results.BadRequest(new { message = "牌库数据为空" });
            if (!L12DeckValidator.TryValidate(_catalog, body.Deck, out var deck, out var error))
                return Results.BadRequest(new { message = error });
            var result = _platform.UpdateDeck(account.Id, id, body.ExpectedRevision, deck);
            return result.Status switch
            {
                "ok" => Results.Ok(result.Deck),
                "not_found" => Results.NotFound(),
                "revision_conflict" => Results.Conflict(new
                {
                    code = "deck_revision_conflict",
                    message = "牌库已被其他操作更新，请刷新后重试",
                    currentRevision = result.CurrentRevision,
                }),
                "name_conflict" => Results.Conflict(new
                {
                    code = "deck_name_conflict",
                    message = "已存在同名牌库",
                }),
                "storage_conflict" => StorageConflictResponse(request),
                _ => Results.BadRequest(new { message = "无法更新牌库" }),
            };
        });
        _app.MapDelete("/api/decks/by-id/{id}", (HttpRequest request, string id, long? expectedRevision) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            if (expectedRevision is null or < 1)
                return Results.BadRequest(new { message = "expectedRevision 必须为正整数" });
            var result = _platform.DeleteDeck(account.Id, id, expectedRevision.Value);
            return result.Status switch
            {
                "ok" => Results.NoContent(),
                "not_found" => Results.NotFound(),
                "revision_conflict" => Results.Conflict(new
                {
                    code = "deck_revision_conflict",
                    message = "牌库已被其他操作更新，请刷新后重试",
                    currentRevision = result.CurrentRevision,
                }),
                "storage_conflict" => StorageConflictResponse(request),
                _ => Results.BadRequest(new { message = "无法删除牌库" }),
            };
        });
        _app.MapPut("/api/decks", (HttpRequest request, L12CustomDeckSubmission submission) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            if (!L12DeckValidator.TryValidate(_catalog, submission, out var deck, out var error))
                return Results.BadRequest(new { message = error });
            return Results.Ok(_platform.UpsertDeck(account.Id, deck));
        });
        _app.MapDelete("/api/decks/{name}", (HttpRequest request, string name) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            return _platform.DeleteDeck(account.Id, name) ? Results.Ok() : Results.NotFound();
        });
        bool PublicDeckReadUnavailable(Exception error) => error is InvalidDataException or JsonException or FormatException
            or Microsoft.Data.Sqlite.SqliteException or L12PlatformStorageUnavailableException or OverflowException or ArgumentException;
        IResult PublicDeckReadFailure() => Results.Json(new { code = "public_deck_read_unavailable",
            message = "公开牌库正文或引用当前无法安全读取" }, statusCode: 503);
        IResult PublicDeckQueryFailure() => Results.Json(new { code = "storage_unavailable",
            message = "公开牌库已提交读取代当前不可用" }, statusCode: 503);
        IResult PublicDeckStatisticsFailure() => Results.Json(new { code = "storage_unavailable",
            message = "牌库统计暂时无法读取，请稍后重试" }, statusCode: 503);
        bool TryPublicDeckContentWriteToken(IQueryCollection query, out string token)
        {
            token = "";
            if (query.Count != 1 || !query.TryGetValue("expectedReadToken", out var raw)
                || raw.Count != 1 || raw[0] is not { } value) return false;
            token = value;
            return token.Length == 64
                && token.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
        }
        IResult PublicDeckContentWriteFailure() => Results.Json(new { code = "storage_unavailable",
            message = "牌库内容暂时无法保存，请稍后重试" }, statusCode: 503);
        IResult PublicDeckContentWriteStatus(L12PublicDeckContentHeadWriteResult result) => result.Status switch
        {
            "ok" => Results.Ok(result.Head),
            "unauthorized" => Results.Unauthorized(),
            "forbidden" => Results.StatusCode(StatusCodes.Status403Forbidden),
            "not_found" => Results.NotFound(),
            "read_conflict" => Results.Json(new { code = "public_deck_read_conflict", refreshRequired = true }, statusCode: 409),
            "feature_disabled" => Results.Json(new { code = "feature_disabled", message = "公开牌库当前未开放" }, statusCode: 503),
            _ => Results.BadRequest(new { message = "牌库内容写入参数无效" }),
        };
        _app.MapGet("/api/deck-library/summaries", (HttpRequest request) =>
        {
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            var viewer = _platform.AuthenticateSession(request.Headers.Authorization);
            if (request.Headers.Authorization.Count > 0 && viewer is null) return Results.Unauthorized();
            if (!L12PublicDeckSummaryQuery.TryParse(request.Query, out var query))
                return Results.BadRequest(new { message = "公开牌库摘要查询参数无效" });
            try
            {
                var result = _platform.DeckLibrarySummaries(_catalog, query, viewer,
                    includeCover: L12DeckCoverCapability.IsEnabled(request.Headers));
                return result.Status switch
                {
                    "ok" => Results.Ok(result.Page),
                    "unauthorized" => Results.Unauthorized(),
                    "feature_disabled" => Results.Json(new { code = "feature_disabled", message = "公开牌库当前未开放" }, statusCode: 503),
                    _ => Results.BadRequest(),
                };
            }
            catch (Exception error) when (PublicDeckReadUnavailable(error)) { return PublicDeckQueryFailure(); }
        });
        _app.MapGet("/api/me/public-deck-references", (HttpRequest request) =>
        {
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            var viewer = _platform.AuthenticateSession(request.Headers.Authorization);
            if (viewer is null) return Results.Unauthorized();
            if (!L12PublicDeckReferenceQuery.TryParse(request.Query, out var query))
                return Results.BadRequest(new { message = "公开牌库引用查询参数无效" });
            try
            {
                var result = _platform.PublicDeckReferences(_catalog, query.PublicationIds, viewer);
                return result.Status switch
                {
                    "available" => Results.Ok(result),
                    "unauthorized" => Results.Unauthorized(),
                    "feature_disabled" => Results.Json(new { code = "feature_disabled", message = "公开牌库当前未开放" }, statusCode: 503),
                    _ => Results.BadRequest(new { message = "公开牌库引用查询参数无效" }),
                };
            }
            catch (Exception error) when (PublicDeckReadUnavailable(error)) { return PublicDeckQueryFailure(); }
        });
        IResult PublicDeckReadResponse(string status, object? value) => status switch
        {
            "ok" => Results.Ok(value),
            "unauthorized" => Results.Unauthorized(),
            "not_found" => Results.NotFound(),
            "read_conflict" => Results.Json(new { code = "public_deck_read_conflict", refreshRequired = true }, statusCode: 409),
            "feature_disabled" => Results.Json(new { code = "feature_disabled", message = "公开牌库当前未开放" }, statusCode: 503),
            _ => Results.BadRequest(new { message = "公开牌库读取参数无效" }),
        };
        _app.MapGet("/api/public-decks/{reference}/current", (HttpRequest request, string reference) =>
        {
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            var viewer = _platform.AuthenticateSession(request.Headers.Authorization);
            if (request.Headers.Authorization.Count > 0 && viewer is null) return Results.Unauthorized();
            if (!L12PublicDeckReadQuery.TryParse(request.Query, false, out var query)) return Results.BadRequest();
            try
            {
                var result = _platform.ReadPublicDeckCurrent(_catalog, reference, query.ExpectedReadToken, viewer);
                return PublicDeckReadResponse(result.Status, result.Detail);
            }
            catch (Exception error) when (PublicDeckReadUnavailable(error)) { return PublicDeckReadFailure(); }
        });
        _app.MapGet("/api/public-decks/{reference}/versions", (HttpRequest request, string reference) =>
        {
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            var viewer = _platform.AuthenticateSession(request.Headers.Authorization);
            if (request.Headers.Authorization.Count > 0 && viewer is null) return Results.Unauthorized();
            if (!L12PublicDeckReadQuery.TryParse(request.Query, true, out var query)) return Results.BadRequest();
            try
            {
                var result = _platform.ReadPublicDeckVersionPage(_catalog, reference, query, viewer);
                return PublicDeckReadResponse(result.Status, result.Page);
            }
            catch (Exception error) when (PublicDeckReadUnavailable(error)) { return PublicDeckReadFailure(); }
        });
        _app.MapGet("/api/public-decks/{reference}/versions/{version}", (HttpRequest request, string reference, string version) =>
        {
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            var viewer = _platform.AuthenticateSession(request.Headers.Authorization);
            if (request.Headers.Authorization.Count > 0 && viewer is null) return Results.Unauthorized();
            if (!L12PublicDeckReadQuery.TryParse(request.Query, false, out var query)
                || !int.TryParse(version, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                    out var number) || number < 1) return Results.BadRequest();
            try
            {
                var result = _platform.ReadPublicDeckVersion(_catalog, reference, number, query.ExpectedReadToken, viewer);
                return PublicDeckReadResponse(result.Status, result.Detail);
            }
            catch (Exception error) when (PublicDeckReadUnavailable(error)) { return PublicDeckReadFailure(); }
        });
        _app.MapGet("/api/public-decks/{reference}/statistics", async Task<IResult> (HttpRequest request, string reference) =>
        {
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            var viewer = _platform.AuthenticateSession(request.Headers.Authorization);
            if (request.Headers.Authorization.Count > 0 && viewer is null) return Results.Unauthorized();
            if (!L12PublicDeckReadQuery.TryParse(request.Query, true, out var query))
                return Results.BadRequest(new { message = "牌库统计查询参数无效" });
            try
            {
                var captured = _platform.CapturePublicDeckStatisticsRead(_catalog, reference,
                    query.ExpectedReadToken, viewer);
                if (captured.Status != "available") return PublicDeckStatisticsStatus(captured.Status);
                var lease = captured.Lease!;
                var statistics = await _recorder.PublicDeckVersionStatisticsPageAsync(lease.Id,
                    query.Page, query.PageSize, lease.ExcludedMatchIds, lease.ExcludedAccountIds,
                    request.HttpContext.RequestAborted);
                var status = _platform.RevalidatePublicDeckStatisticsRead(_catalog, lease, viewer);
                if (status != "available") return PublicDeckStatisticsStatus(status);
                return Results.Ok(new L12PublicDeckStatisticsPage(lease.Id, lease.PublicCode, lease.ReadToken,
                    lease.CatalogVersion, lease.PolicyVersion, statistics.From, statistics.To,
                    statistics.RecentDays, statistics.Games, statistics.SampleStatus, statistics.Groups,
                    statistics.Total, statistics.Page, statistics.PageSize));
            }
            catch (Exception error) when (error is not OperationCanceledException
                && (PublicDeckReadUnavailable(error) || error is TimeoutException))
            {
                return PublicDeckStatisticsFailure();
            }
        });
        _app.MapGet("/api/public-decks", (HttpRequest request, string? sort, bool? seasonCompliant) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            var policy = _platform.EffectiveOperationsPolicy();
            var decks = _platform.PublishedDecks(account?.Id).Select(item =>
            {
                var preset = new L12PresetDeckDefinition
                {
                    Name = item.Deck.Name,
                    MasterId = item.Deck.MasterId,
                    CardIds = item.Deck.CardIds.ToList(),
                    MoraleIds = item.Deck.MoraleIds.ToList(),
                    SpecialIds = item.Deck.SpecialIds.ToList(),
                };
                var valid = L12DeckValidator.TryValidatePreset(_catalog, preset, out var error,
                    policy.CardRestrictions);
                return item with
                {
                    SeasonCompliant = valid,
                    SeasonComplianceReason = valid ? null : error,
                };
            });
            if (seasonCompliant == true) decks = decks.Where(item => item.SeasonCompliant);
            decks = (sort ?? "copies").Trim().ToLowerInvariant() switch
            {
                "likes" => decks.OrderByDescending(item => item.Likes)
                    .ThenByDescending(item => item.CreatedAt).ThenBy(item => item.Id, StringComparer.Ordinal),
                "views" => decks.OrderByDescending(item => item.Views)
                    .ThenByDescending(item => item.CreatedAt).ThenBy(item => item.Id, StringComparer.Ordinal),
                "latest" => decks.OrderByDescending(item => item.CreatedAt)
                    .ThenBy(item => item.Id, StringComparer.Ordinal),
                _ => decks.OrderByDescending(item => item.Copies)
                    .ThenByDescending(item => item.CreatedAt).ThenBy(item => item.Id, StringComparer.Ordinal),
            };
            return Results.Ok(decks.ToArray());
        });
        _app.MapGet("/api/public-decks/{id}", async (HttpRequest request, string id) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            var item = _platform.PublishedDeckByPublicCode(id, account?.Id);
            if (item is null) return Results.NotFound();
            var policy = _platform.EffectiveOperationsPolicy();
            var preset = new L12PresetDeckDefinition
            {
                Name = item.Deck.Name,
                MasterId = item.Deck.MasterId,
                CardIds = item.Deck.CardIds.ToList(),
                MoraleIds = item.Deck.MoraleIds.ToList(),
                SpecialIds = item.Deck.SpecialIds.ToList(),
            };
            var valid = L12DeckValidator.TryValidatePreset(_catalog, preset, out var error,
                policy.CardRestrictions);
            return Results.Ok(item with
            {
                SeasonCompliant = valid,
                SeasonComplianceReason = valid ? null : error,
                Details = await PublicDeckDetailsWithStatisticsAsync(item.Id),
            });
        });
        _app.MapPost("/api/public-decks", (HttpRequest request, PublishedDeckRequest body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            if (body.Deck is null) return Results.BadRequest(new { message = "牌库数据为空" });
            if (!L12DeckValidator.TryValidate(_catalog, body.Deck, out var deck, out var error))
                return Results.BadRequest(new { message = error });
            var published = _platform.PublishDeck(account.Id, deck, body.PublicationId);
            return published is null ? Results.NotFound() : Results.Ok(published);
        });
        _app.MapPut("/api/public-decks/{id}/content", async (HttpRequest request, string id,
            L12PublicDeckContentInput body) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            try
            {
                var published = _platform.PublishedDeckByPublicCode(id, account.Id);
                if (published is null) return Results.NotFound();
                var details = _platform.UpdatePublicDeckContent(account.Id, published.Id, body);
                return details is null ? Results.NotFound() : Results.Ok(await PublicDeckDetailsWithStatisticsAsync(published.Id));
            }
            catch (UnauthorizedAccessException error)
            {
                return Results.Json(new { message = error.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { message = error.Message });
            }
        });
        _app.MapPut("/api/public-decks/{reference}/content/current", (HttpRequest request, string reference,
            L12PublicDeckContentInput body) =>
        {
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            var actor = _platform.AuthenticateSession(request.Headers.Authorization);
            if (actor is null) return Results.Unauthorized();
            if (!TryPublicDeckContentWriteToken(request.Query, out var expectedReadToken))
                return Results.BadRequest(new { message = "牌库内容写入参数无效" });
            try
            {
                return PublicDeckContentWriteStatus(_platform.WritePublicDeckContentCurrent(_catalog, reference,
                    expectedReadToken, actor, body));
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { message = error.Message });
            }
            catch (Exception error) when (PublicDeckReadUnavailable(error)
                || error is L12DeploymentBarrierClosedException)
            {
                return PublicDeckContentWriteFailure();
            }
        });
        _app.MapDelete("/api/public-decks/{id}", (HttpRequest request, string id) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            var published = _platform.PublishedDeckByPublicCode(id, account.Id);
            return published is not null && _platform.DeletePublishedDeck(account.Id, published.Id)
                ? Results.Ok() : Results.NotFound();
        });
        _app.MapPost("/api/public-decks/{reference}/counters/{kind}", (HttpRequest request, string reference, string kind) =>
        {
            var viewer = _platform.AuthenticateSession(request.Headers.Authorization);
            if (request.Headers.Authorization.Count > 0 && viewer is null) return Results.Unauthorized();
            if (request.Query.Count > 0) return Results.BadRequest(new { message = "计数请求参数无效" });
            var result = _platform.UpdatePublicDeckCounter(reference, kind, viewer);
            return result.Status switch
            {
                "ok" => Results.Ok(result.Counters),
                "unauthorized" => Results.Unauthorized(),
                "not_found" => Results.NotFound(),
                "feature_disabled" => Results.Json(new { code = "feature_disabled", message = "公开牌库当前未开放" }, statusCode: 503),
                _ => Results.BadRequest(new { message = "计数请求无效" }),
            };
        });
        _app.MapPost("/api/public-decks/{id}/like", (HttpRequest request, string id) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            var target = _platform.PublishedDeckByPublicCode(id, account.Id);
            var published = target is null ? null : _platform.TogglePublishedDeckLike(account.Id, target.Id);
            return published is null ? Results.NotFound() : Results.Ok(published);
        });
        _app.MapPost("/api/public-decks/{id}/copy", (HttpRequest request, string id) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            var target = _platform.PublishedDeckByPublicCode(id, account?.Id);
            var published = target is null ? null : _platform.RecordPublishedDeckCopy(target.Id, account?.Id);
            return published is null ? Results.NotFound() : Results.Ok(published);
        });
        _app.MapPost("/api/public-decks/{id}/view", (HttpRequest request, string id) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            var target = _platform.PublishedDeckByPublicCode(id, account?.Id);
            var published = target is null ? null : _platform.RecordPublishedDeckView(target.Id, account?.Id);
            return published is null ? Results.NotFound() : Results.Ok(published);
        });
        _app.MapGet("/api/public/tournaments/summaries", (HttpRequest request, string? section, string? format,
            string? search, int? page, int? pageSize, DateTimeOffset? startFrom, DateTimeOffset? startTo) =>
        {
            try
            {
                return Results.Ok(_platform.PublicTournamentSummaries(section, format, search,
                    page ?? 1, pageSize ?? 24, startFrom, startTo));
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_public_tournament_query", error.Message,
                    StatusCodes.Status400BadRequest);
            }
        });
        _app.MapGet("/api/public/tournaments/code/{code}", (HttpRequest request, string code) =>
        {
            var result = _platform.PublicTournamentByCode(code);
            return result is null
                ? ApiError(request, "tournament_not_found", "赛事不存在", StatusCodes.Status404NotFound)
                : Results.Ok(result);
        });
        _app.MapGet("/api/tournaments", (HttpRequest request, string? status, string? search, bool? mine) =>
        {
            if (!TryAuthorize(request, L12Permission.TournamentsRead, out var authenticated, out var failure))
                return failure;
            var result = _platform.Tournaments(authenticated.Account, status, search, mine ?? false);
            request.HttpContext.Response.Headers.ETag = $"\"{result.PlatformVersion}\"";
            return Results.Ok(result);
        });
        _app.MapGet("/api/tournaments/summaries", (HttpRequest request, string? section, string? format,
            string? search, int? page, int? pageSize, DateTimeOffset? startFrom, DateTimeOffset? startTo) =>
        {
            if (!TryAuthorize(request, L12Permission.TournamentsRead, out var authenticated, out var failure))
                return failure;
            var result = _platform.TournamentSummaries(authenticated.Account, section, format, search,
                page ?? 1, pageSize ?? 24, startFrom, startTo);
            request.HttpContext.Response.Headers.ETag = $"\"{result.PlatformVersion}\"";
            return Results.Ok(result);
        });
        _app.MapGet("/api/tournaments/career", (HttpRequest request, int? page, int? pageSize) =>
        {
            if (!TryAuthorize(request, L12Permission.TournamentsRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.TournamentCareer(authenticated.Account, page: page ?? 1,
                pageSize: pageSize ?? 20));
        });
        _app.MapGet("/api/tournaments/code/{code}", (HttpRequest request, string code) =>
        {
            if (!TryAuthorize(request, L12Permission.TournamentsRead, out var authenticated, out var failure))
                return failure;
            var result = _platform.TournamentByCode(authenticated.Account, code);
            if (result is null) return ApiError(request, "tournament_not_found", "赛事不存在",
                StatusCodes.Status404NotFound);
            request.HttpContext.Response.Headers.ETag = $"\"{result.Version}\"";
            return Results.Ok(result);
        });
        _app.MapGet("/api/tournaments/{id}", (HttpRequest request, string id) =>
        {
            if (!TryAuthorize(request, L12Permission.TournamentsRead, out var authenticated, out var failure))
                return failure;
            var result = _platform.Tournament(authenticated.Account, id);
            if (result is null) return ApiError(request, "tournament_not_found", "赛事不存在",
                StatusCodes.Status404NotFound);
            request.HttpContext.Response.Headers.ETag = $"\"{result.Version}\"";
            return Results.Ok(result);
        });
        _app.MapGet("/api/tournaments/{id}/start-check", (HttpRequest request, string id) =>
        {
            if (!TryAuthenticate(request, L12Permission.TournamentsManage, out var authenticated, out var failure))
                return failure;
            try { return Results.Ok(_platform.TournamentStartCheck(authenticated.Account, id)); }
            catch (L12TournamentScopeException error)
            {
                return ApiError(request, "scope_denied", error.Message, StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException error)
            {
                return ApiError(request, "tournament_resource_not_found", error.Message,
                    StatusCodes.Status404NotFound);
            }
        });
        _app.MapGet("/api/tournaments/{id}/export.csv", (HttpRequest request, string id) =>
        {
            if (!TryAuthorize(request, L12Permission.TournamentsRead, out var authenticated, out var failure))
                return failure;
            try
            {
                var csv = _platform.TournamentCsv(authenticated.Account, id);
                return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(),
                    "text/csv; charset=utf-8", $"tournament-{id}.csv");
            }
            catch (KeyNotFoundException error)
            {
                return ApiError(request, "tournament_not_found", error.Message, StatusCodes.Status404NotFound);
            }
        });
        _app.MapPost("/api/tournaments", (HttpRequest request, TournamentCreateRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsCreate;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.create",
                "tournaments", body.Tournament, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.CreateTournament(current.Actor, current.Payload,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome);
        });
        _app.MapPost("/api/tournaments/import-legacy", (HttpRequest request, TournamentLegacyImportRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentImportLegacy;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentLegacyImportPayload(body.Tournaments ?? [], body.PreviewHash);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.import-legacy",
                "tournaments", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.ImportLegacyTournaments(current.Actor, current.Payload,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome);
        });
        _app.MapPost("/api/tournaments/{id}/registrations", (HttpRequest request, string id,
            TournamentRegistrationRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsRegister;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentRegistrationPayload(body.DeckName ?? string.Empty,
                body.DeckCode ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.register",
                $"tournament:{id}/registration:{authenticated.Account.Id}", payload, key, expected,
                body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.RegisterTournament(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPut("/api/tournaments/{id}/registration", (HttpRequest request, string id,
            TournamentRegistrationRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsRegister;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentRegistrationPayload(body.DeckName ?? string.Empty,
                body.DeckCode ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.registration.update",
                $"tournament:{id}/registration:{authenticated.Account.Id}", payload, key, expected,
                body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.UpdateTournamentRegistration(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/pre-check-in", (HttpRequest request, string id,
            TournamentPreCheckInRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsRegister;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentPreCheckInPayload(body.DeckName ?? string.Empty,
                body.DeckCode ?? string.Empty, body.DeckId);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.pre-check-in",
                $"tournament:{id}/registration:{authenticated.Account.Id}", payload, key, expected,
                body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.PreCheckInTournament(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapDelete("/api/tournaments/{id}/registration", (HttpRequest request, string id,
            [Microsoft.AspNetCore.Mvc.FromBody] TournamentActionRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsRegister;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.drop",
                $"tournament:{id}/registration:{authenticated.Account.Id}", new TournamentEmptyPayload(), key,
                expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.DropTournament(current.Actor, id, expected,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPut("/api/tournaments/{id}/staff", (HttpRequest request, string id,
            TournamentStaffRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new TournamentStaffCommandPayload(id,
                new L12TournamentStaffPayload(body.RefereeAccountIds ?? []));
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.staff.set",
                $"tournament:{id}/staff", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.SetTournamentStaff(current.Actor, current.Payload.TournamentId,
                    current.Payload.Staff, expected,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPut("/api/tournaments/{id}/visibility", (HttpRequest request, string id,
            TournamentVisibilityRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentVisibilityPayload(body.Visibility ?? string.Empty,
                body.RegistrationVisibility ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission,
                "tournament.visibility.set", $"tournament:{id}/visibility", payload, key, expected,
                body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.SetTournamentVisibility(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/participants/remove", (HttpRequest request, string id,
            TournamentRemoveParticipantRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentRemoveParticipantPayload(body.AccountId ?? string.Empty,
                body.BanRegistration, body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.participant.remove",
                $"tournament:{id}/participant:{body.AccountId}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.RemoveTournamentParticipant(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/registration-ban", (HttpRequest request, string id,
            TournamentRegistrationBanRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentRegistrationBanPayload(body.AccountId ?? string.Empty,
                body.Banned, body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.registration-ban",
                $"tournament:{id}/registration-ban:{body.AccountId}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.SetTournamentRegistrationBan(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/organizer-transfer", (HttpRequest request, string id,
            TournamentOrganizerTransferRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentOrganizerTransferRequestPayload(body.AccountId ?? string.Empty,
                body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.organizer-transfer.request",
                $"tournament:{id}/organizer-transfer", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.RequestTournamentOrganizerTransfer(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/organizer-transfer/decision", (HttpRequest request, string id,
            TournamentOrganizerTransferDecisionRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsRegister;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentOrganizerTransferDecisionPayload(body.RequestId ?? string.Empty,
                body.Accept);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.organizer-transfer.decision",
                $"tournament:{id}/organizer-transfer", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.DecideTournamentOrganizerTransfer(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/start", (HttpRequest request, string id, TournamentActionRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.start",
                $"tournament:{id}", new TournamentTargetCommandPayload(id), key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.StartTournament(current.Actor, current.Payload.TournamentId, expected,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/phase", (HttpRequest request, string id,
            TournamentPhaseRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentPhasePayload(body.Phase ?? string.Empty, body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.phase",
                $"tournament:{id}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.SetTournamentPhase(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/postpone", (HttpRequest request, string id,
            TournamentPostponeRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentPostponePayload(body.NewStartAt, body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.postpone",
                $"tournament:{id}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.PostponeTournament(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/cancel", async (HttpRequest request, string id,
            TournamentCancelRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentCancelPayload(body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.cancel",
                $"tournament:{id}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.CancelTournament(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            if (outcome.Success && !body.DryRun)
            {
                var roomCommands = await _rooms.DrainTournamentRoomCommandsAsync(id);
                await SendManyAsync(roomCommands.Messages, CancellationToken.None);
            }
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/rounds", (HttpRequest request, string id, TournamentActionRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.round.create",
                $"tournament:{id}/rounds", new TournamentTargetCommandPayload(id), key, expected,
                body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.CreateNextRound(current.Actor, current.Payload.TournamentId, expected,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/rounds/{roundNumber:int}/check-in",
            (HttpRequest request, string id, int roundNumber, TournamentCheckInRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsRegister;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentCheckInPayload(body.AccountId, body.Ready);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.check-in",
                $"tournament:{id}/round:{roundNumber}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.CheckInTournament(current.Actor, id, roundNumber, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/rounds/{roundNumber:int}/start",
            (HttpRequest request, string id, int roundNumber, TournamentActionRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.round.start",
                $"tournament:{id}/round:{roundNumber}", new TournamentRoundCommandPayload(id, roundNumber), key, expected,
                body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.StartTournamentRound(current.Actor, current.Payload.TournamentId,
                    current.Payload.RoundNumber, expected,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/rounds/{roundNumber:int}/pause",
            (HttpRequest request, string id, int roundNumber, TournamentPauseRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentPausePayload(body.Paused, body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.round.pause",
                $"tournament:{id}/round:{roundNumber}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.PauseTournamentRound(current.Actor, id, roundNumber,
                    current.Payload, expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/matches/{matchId}/time-extension",
            async (HttpRequest request, string id, string matchId, TournamentTimeExtensionRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentTimeExtensionPayload(body.Minutes, body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.match.extend",
                $"tournament:{id}/match:{matchId}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.ExtendTournamentMatch(current.Actor, id, matchId, current.Payload,
                    expected, current.AuditContext, apply));
            if (outcome.Success && !body.DryRun)
            {
                var roomCommands = await _rooms.DrainTournamentRoomCommandsAsync(id);
                await SendManyAsync(roomCommands.Messages, CancellationToken.None);
            }
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/matches/{matchId}/pause",
            async (HttpRequest request, string id, string matchId, TournamentMatchPauseRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentRulingsWrite;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentMatchPausePayload(body.Paused, body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.match.pause",
                $"tournament:{id}/match:{matchId}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.PauseTournamentMatch(current.Actor, id, matchId,
                    current.Payload, expected, current.AuditContext, apply));
            if (outcome.Success && !body.DryRun)
            {
                var roomCommands = await _rooms.DrainTournamentRoomCommandsAsync(id);
                await SendManyAsync(roomCommands.Messages, CancellationToken.None);
            }
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/judge-cases", (HttpRequest request, string id,
            TournamentJudgeCaseCreateRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsRegister;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentJudgeCaseCreatePayload(body.MatchId ?? string.Empty,
                body.Category ?? string.Empty, body.Urgency ?? string.Empty, body.Message ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.judge-case.create",
                $"tournament:{id}/judge-cases", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.CreateTournamentJudgeCase(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/judge-cases/assign", (HttpRequest request, string id,
            TournamentJudgeCaseAssignRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentRulingsWrite;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentJudgeCaseAssignPayload(body.CaseId ?? string.Empty,
                body.AssigneeAccountId ?? string.Empty, body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.judge-case.assign",
                $"tournament:{id}/judge-cases", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.AssignTournamentJudgeCase(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/judge-cases/resolve", (HttpRequest request, string id,
            TournamentJudgeCaseResolveRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentRulingsWrite;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentJudgeCaseResolvePayload(body.CaseId ?? string.Empty,
                body.Status ?? string.Empty, body.Resolution ?? string.Empty, body.StaffNote ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.judge-case.resolve",
                $"tournament:{id}/judge-cases", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.ResolveTournamentJudgeCase(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/judge-cases/appeal", (HttpRequest request, string id,
            TournamentJudgeCaseAppealRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsRegister;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12TournamentJudgeCaseAppealPayload(body.CaseId ?? string.Empty,
                body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.judge-case.appeal",
                $"tournament:{id}/judge-cases", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.AppealTournamentJudgeCase(current.Actor, id, current.Payload,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/matches/{matchId}/rulings",
            (HttpRequest request, string id, string matchId, TournamentRulingRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentRulingsWrite;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new TournamentRulingCommandPayload(id, matchId,
                new L12TournamentRulingPayload(body.Kind ?? string.Empty, body.TargetAccountId,
                    body.Decision ?? string.Empty, body.Reason ?? string.Empty));
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.ruling.apply",
                $"tournament:{id}/match:{matchId}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.ApplyTournamentRuling(current.Actor, current.Payload.TournamentId,
                    current.Payload.MatchId, current.Payload.Ruling,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/matches/{matchId}/rematch",
            (HttpRequest request, string id, string matchId, TournamentRematchRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentRulingsWrite;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new TournamentRematchCommandPayload(id, matchId,
                new L12TournamentRematchPayload(body.Reason ?? string.Empty));
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.rematch",
                $"tournament:{id}/match:{matchId}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.RequestTournamentRematch(current.Actor,
                    current.Payload.TournamentId, current.Payload.MatchId, current.Payload.Rematch,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPut("/api/tournaments/{id}/matches/{matchId}/reference",
            async (HttpRequest request, string id, string matchId, TournamentMatchReferenceRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentRulingsWrite;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var reference = body.RecordedMatchId?.Trim() ?? string.Empty;
            var recorded = string.IsNullOrWhiteSpace(reference) ? null : await _recorder.GetMatchAsync(reference);
            var tournament = _platform.Tournament(authenticated.Account, id);
            var table = tournament?.Rounds.SelectMany(round => round.Matches).FirstOrDefault(item => item.Id == matchId);
            if (recorded is null || recorded.Match.EndedUtc is null || table is null
                || !new HashSet<string>([recorded.Match.Player0, recorded.Match.Player1], StringComparer.OrdinalIgnoreCase)
                    .SetEquals(new[] { table.PlayerAName, table.PlayerBName }))
                return ApiError(request, "invalid_match_reference", "仅可绑定已结束且参赛账号一致的对局记录",
                    StatusCodes.Status409Conflict);
            var payload = new TournamentReferenceCommandPayload(id, matchId,
                new L12TournamentMatchReferencePayload(reference));
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.match.reference",
                $"tournament:{id}/match:{matchId}", payload, key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.LinkTournamentMatch(current.Actor, current.Payload.TournamentId,
                    current.Payload.MatchId, current.Payload.Reference,
                    expected, current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapPost("/api/tournaments/{id}/complete", (HttpRequest request, string id,
            TournamentActionRequest body) =>
        {
            const L12Permission permission = L12Permission.TournamentsManage;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryTournamentCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "tournament.complete",
                $"tournament:{id}", new TournamentTargetCommandPayload(id), key, expected, body.DryRun, body.Reason);
            var outcome = ExecuteTournamentCommand(command, permission,
                (current, apply) => _platform.CompleteTournament(current.Actor, current.Payload.TournamentId, expected,
                    current.AuditContext, apply));
            return TournamentCommandResponse(request, command, outcome, id);
        });
        _app.MapGet("/api/admin/releases/artifacts", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.ReleasesRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.ReleaseArtifacts(authenticated.Account, _releaseControl));
        });
        _app.MapGet("/api/admin/releases/environments", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.ReleaseRuntimeRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.ReleaseEnvironments(authenticated.Account, _releaseControl));
        });
        _app.MapGet("/api/admin/releases/runs", (HttpRequest request, string? environment, string? status,
            int? limit) =>
        {
            if (!TryAuthorize(request, L12Permission.ReleasesRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.ReleaseRuns(authenticated.Account, environment, status, limit ?? 100));
        });
        _app.MapGet("/api/admin/releases/runs/{id}", (HttpRequest request, string id) =>
        {
            if (!TryAuthorize(request, L12Permission.ReleasesRead, out var authenticated, out var failure))
                return failure;
            var run = _platform.ReleaseRun(authenticated.Account, id);
            return run is null
                ? ApiError(request, "release_run_not_found", "发布记录不存在", StatusCodes.Status404NotFound)
                : Results.Ok(run);
        });
        _app.MapPost("/api/admin/releases/deploy", (HttpRequest request, ReleaseDeployRequest body) =>
        {
            const L12Permission permission = L12Permission.ReleasesExecute;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryReleaseCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            if (TryReplayReleaseCommand(request, authenticated.Account, permission, "release.deploy", key,
                    expected, body.DryRun, body.Reason,
                    payload => payload.Action == "deploy"
                               && payload.Artifact.Id == body.ArtifactId?.Trim()
                               && payload.Environment == body.Environment?.Trim().ToLowerInvariant(),
                    out var replay)) return replay;
            try
            {
                var payload = _platform.CaptureReleaseDeploy(authenticated.Account,
                    body.ArtifactId ?? string.Empty, body.Environment ?? string.Empty, _releaseControl);
                var validation = ExecuteReleaseCommand(authenticated.Account, payload, expected,
                    AuditContext(request, permission), false);
                if (!validation.Success) return ApiError(request, validation.Code, validation.Message,
                    validation.StatusCode);
                var command = CommandEnvelope(request, authenticated.Account, permission, "release.deploy",
                    $"release:{payload.Environment}", payload, key, expected, body.DryRun, body.Reason);
                var outcome = _adminCommands.Execute(command, permission,
                    current => ExecuteReleaseCommand(current.Actor, current.Payload, expected,
                        current.AuditContext, true),
                    current => ExecuteReleaseCommand(current.Actor, current.Payload, expected,
                        current.AuditContext, false), L12AdminCommandRisk.High);
                return ReleaseCommandResponse(request, command, outcome, payload.Environment);
            }
            catch (Exception error) when (IsReleaseRequestError(error))
            {
                return ReleaseRequestError(request, authenticated.Account, permission, error);
            }
        });
        _app.MapPost("/api/admin/releases/rollback", (HttpRequest request, ReleaseRollbackRequest body) =>
        {
            const L12Permission permission = L12Permission.ReleasesExecute;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryReleaseCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            if (TryReplayReleaseCommand(request, authenticated.Account, permission, "release.rollback", key,
                    expected, body.DryRun, body.Reason,
                    payload => payload.Action == "rollback"
                               && payload.RollbackTargetRunId == body.TargetRunId?.Trim(),
                    out var replay)) return replay;
            try
            {
                var payload = _platform.CaptureReleaseRollback(authenticated.Account,
                    body.TargetRunId ?? string.Empty, _releaseControl);
                var validation = ExecuteReleaseCommand(authenticated.Account, payload, expected,
                    AuditContext(request, permission), false);
                if (!validation.Success) return ApiError(request, validation.Code, validation.Message,
                    validation.StatusCode);
                var command = CommandEnvelope(request, authenticated.Account, permission, "release.rollback",
                    $"release:{payload.Environment}", payload, key, expected, body.DryRun, body.Reason);
                var outcome = _adminCommands.Execute(command, permission,
                    current => ExecuteReleaseCommand(current.Actor, current.Payload, expected,
                        current.AuditContext, true),
                    current => ExecuteReleaseCommand(current.Actor, current.Payload, expected,
                        current.AuditContext, false), L12AdminCommandRisk.High);
                return ReleaseCommandResponse(request, command, outcome, payload.Environment);
            }
            catch (Exception error) when (IsReleaseRequestError(error))
            {
                return ReleaseRequestError(request, authenticated.Account, permission, error);
            }
        });
        _app.MapPost("/api/bugs", async (HttpRequest request, BugRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Description)) return Results.BadRequest(new { message = "请填写问题描述" });
            var account = _platform.Authenticate(request.Headers.Authorization);
            var diagnostic = await _rooms.CaptureBugDiagnosticAsync(body.MatchId, body.RoomCode);
            return Results.Ok(_platform.AddBug(account, body.Title ?? string.Empty, body.Description, body.Page ?? string.Empty,
                body.RoomCode, body.MatchId, body.Version ?? string.Empty, diagnostic, body.ClientDiagnostic,
                _rooms.CaptureConnectionClaimDiagnostic(account?.Id)));
        });
        _app.MapGet("/api/admin/accounts", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminAccountsRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.Accounts());
        });
        _app.MapGet("/api/admin/accounts/query", (HttpRequest request, int? page, int? pageSize,
            string? search, bool? deleted) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminAccountsRead, out _, out var failure)) return failure;
            try { return Results.Ok(_platform.QueryAccounts(page ?? 1, pageSize ?? 10, search,
                deleted ?? false, request.HttpContext.RequestAborted)); }
            catch (ArgumentException)
            { return ApiError(request, "account_query_invalid", "账号搜索最多200个字符", StatusCodes.Status400BadRequest); }
        });
        _app.MapGet("/api/admin/accounts/{accountId}", (HttpRequest request, string accountId) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminAccountsRead, out _, out var failure)) return failure;
            var account = _platform.Account(accountId);
            return account is null
                ? ApiError(request, "account_not_found", "账号不存在", StatusCodes.Status404NotFound)
                : Results.Ok(account);
        });
        _app.MapGet("/api/admin/username-change-requests", (HttpRequest request, string? status) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminAccountsRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.UsernameChangeRequests(status));
        });
        _app.MapPost("/api/admin/username-change-requests/{id}/review", (HttpRequest request, string id,
            UsernameChangeReviewRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminAccountStatusWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.ReviewUsernameChangeRequest(authenticated.Account, id, body.Approve,
                body.Note, RequestAuditContext(request, permission))); }
            catch (KeyNotFoundException error) { return ApiError(request, "username_change_request_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (InvalidOperationException error) { return ApiError(request, "username_change_request_conflict", error.Message, StatusCodes.Status409Conflict); }
            catch (ArgumentException error) { return ApiError(request, "username_change_request_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        _app.MapPut("/api/admin/accounts/{id}/role", (HttpRequest request, string id, RoleRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminAccountRolesWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TrySecurityCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var idempotencyKey, out var expectedVersion, out failure))
                return failure;
            var payload = new RoleCommandPayload(id, body.Role?.Trim().ToLowerInvariant() ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "account.role.set", $"account:{id}", payload,
                idempotencyKey, expectedVersion, body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current =>
                {
                    var before = _platform.Account(current.Payload.AccountId);
                    if (!_platform.SetRole(current.Actor, current.Payload.AccountId, current.Payload.Role,
                            current.AuditContext))
                        return L12AdminCommandResult<RoleCommandResult>.Fail("invalid_role_change", "账号或角色无效",
                            StatusCodes.Status400BadRequest);
                    var updated = _platform.Account(current.Payload.AccountId)!;
                    var changed = before is not null
                        && !string.Equals(before.Role, updated.Role, StringComparison.OrdinalIgnoreCase);
                    return L12AdminCommandResult<RoleCommandResult>.Ok(
                        new RoleCommandResult(current.Payload.AccountId, updated.Role, changed),
                        changed ? "账号角色已更新" : "账号角色没有变化");
                },
                current =>
                {
                    if (!_platform.CanSetRole(current.Payload.AccountId, current.Payload.Role))
                        return L12AdminCommandResult<RoleCommandResult>.Fail("invalid_role_change", "账号或角色无效",
                            StatusCodes.Status400BadRequest);
                    return L12AdminCommandResult<RoleCommandResult>.Ok(
                        new RoleCommandResult(current.Payload.AccountId, current.Payload.Role, false), "干运行验证通过");
                });
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapPut("/api/admin/accounts/{id}/status",
            (HttpRequest request, string id, AccountStatusRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminAccountStatusWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TrySecurityCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var idempotencyKey, out var expectedVersion, out failure))
                return failure;
            var payload = new L12AccountStatusCommandPayload(id, body.Disabled, body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "account.status.set",
                $"account:{id}", payload, idempotencyKey, expectedVersion, body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteAccountStatus(current.Actor, current.Payload, current.AuditContext, true),
                current => ExecuteAccountStatus(current.Actor, current.Payload, current.AuditContext, false));
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapPost("/api/admin/accounts/{id}/reset-password",
            (HttpRequest request, string id, AccountSecurityActionRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminAccountStatusWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TrySecurityCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var idempotencyKey, out var expectedVersion, out failure))
                return failure;
            var payload = new AccountPasswordResetCommandPayload(id, body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission,
                "account.password-admin-reset", $"account:{id}", payload, idempotencyKey, expectedVersion,
                body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteAdminPasswordReset(current.Actor, current.Payload, current.AuditContext, true),
                current => ExecuteAdminPasswordReset(current.Actor, current.Payload, current.AuditContext, false));
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapPost("/api/admin/accounts/{id}/delete",
            (HttpRequest request, string id, AccountSecurityActionRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminAccountStatusWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TrySecurityCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var idempotencyKey, out var expectedVersion, out failure))
                return failure;
            var payload = new AccountDeletionCommandPayload(id, body.Reason ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission,
                "account.logical-delete", $"account:{id}", payload, idempotencyKey, expectedVersion,
                body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteAccountDeletion(current.Actor, current.Payload, current.AuditContext, true),
                current => ExecuteAccountDeletion(current.Actor, current.Payload, current.AuditContext, false));
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapGet("/api/admin/operations/config", (HttpRequest request) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var result = _platform.OperationsConfig(authenticated.Account);
            request.HttpContext.Response.Headers.ETag = $"\"{result.Version}\"";
            return Results.Ok(result);
        });
        _app.MapGet("/api/admin/operations/config/history", (HttpRequest request, int? limit) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            return Results.Ok(_platform.OperationsConfigHistory(authenticated.Account, limit ?? 50));
        });
        _app.MapGet("/api/admin/operations/config/sections/{section}",
            (HttpRequest request, string section) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var result = _platform.OperationsConfigSection(authenticated.Account, section);
                request.HttpContext.Response.Headers.ETag = $"\"{result.Revision}\"";
                return Results.Ok(result);
            }
            catch (L12OperationsConfigException error)
            {
                return OperationsConfigError(request, error);
            }
        });
        _app.MapGet("/api/admin/operations/config/sections/{section}/history",
            (HttpRequest request, string section, int? limit) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                return Results.Ok(_platform.OperationsConfigSectionHistory(authenticated.Account,
                    section, limit ?? 50));
            }
            catch (L12OperationsConfigException error)
            {
                return OperationsConfigError(request, error);
            }
        });
        _app.MapPost("/api/admin/operations/config/sections/{section}/preview",
            (HttpRequest request, string section, OperationsConfigSectionPreviewRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var result = _platform.PreviewOperationsConfigSection(authenticated.Account, section,
                    body.Config, body.ExpectedRevision ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault()),
                    body.ExpectedFieldRevisions,
                    AuditContext(request, permission) with { DryRun = true, Outcome = "dry-run" });
                request.HttpContext.Response.Headers.ETag = $"\"{result.CurrentRevision}\"";
                return Results.Ok(result);
            }
            catch (L12OperationsConfigException error)
            {
                return OperationsConfigError(request, error);
            }
        });
        _app.MapPut("/api/admin/operations/config/sections/{section}",
            (HttpRequest request, string section, OperationsConfigSectionApplyRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryOperationsCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedRevision, out var key, out var expected, out failure)) return failure;
            var payload = new L12OperationsSectionApplyCommandPayload(section, body.Config, expected,
                body.ExpectedFieldRevisions ?? new Dictionary<string, long>());
            var command = CommandEnvelope(request, authenticated.Account, permission,
                "operations.config.section.apply", $"operations:config:{section}", payload, key,
                null, false, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteOperationsConfig(() => _platform.ApplyOperationsConfigSection(
                    current.Actor, current.Payload.Section, current.Payload.Config,
                    current.Payload.ExpectedRevision, current.Payload.ExpectedFieldRevisions,
                    current.Reason, current.AuditContext)));
            if (outcome.Success && outcome.Value?.Applied == true) NotifyOperationsPolicyChanged();
            var response = AdminCommandResponse(request, command, outcome);
            if (outcome.Value?.Current is { } currentSection)
                request.HttpContext.Response.Headers.ETag = $"\"{currentSection.Revision}\"";
            return response;
        });
        _app.MapPost("/api/admin/operations/config/sections/{section}/rollback",
            (HttpRequest request, string section, OperationsConfigSectionRollbackRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryOperationsCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedRevision, out var key, out var expected, out failure)) return failure;
            var payload = new L12OperationsSectionRollbackCommandPayload(section,
                body.VersionId?.Trim() ?? string.Empty, expected);
            var command = CommandEnvelope(request, authenticated.Account, permission,
                "operations.config.section.rollback", $"operations:config:{section}", payload, key,
                null, false, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteOperationsConfig(() => _platform.RollbackOperationsConfigSection(
                    current.Actor, current.Payload.Section, current.Payload.VersionId,
                    current.Payload.ExpectedRevision, current.Reason, current.AuditContext)));
            if (outcome.Success && outcome.Value?.Applied == true) NotifyOperationsPolicyChanged();
            var response = AdminCommandResponse(request, command, outcome);
            if (outcome.Value?.Current is { } currentSection)
                request.HttpContext.Response.Headers.ETag = $"\"{currentSection.Revision}\"";
            return response;
        });
        _app.MapPost("/api/admin/operations/config/preview",
            (HttpRequest request, OperationsConfigPreviewRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var result = _platform.PreviewOperationsConfig(authenticated.Account, body.Config,
                    body.ExpectedVersion ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault()),
                    AuditContext(request, permission) with { DryRun = true, Outcome = "dry-run" });
                request.HttpContext.Response.Headers.ETag = $"\"{result.CurrentVersion}\"";
                return Results.Ok(result);
            }
            catch (L12OperationsConfigException error)
            {
                return OperationsConfigError(request, error);
            }
        });
        _app.MapPut("/api/admin/operations/config", (HttpRequest request, OperationsConfigApplyRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!string.Equals(body.CrossSectionReplaceIntent, OperationsCrossSectionReplaceIntent,
                    StringComparison.Ordinal))
                return ApiError(request, "operations_cross_section_intent_required",
                    "全量运营配置替换必须显式确认跨分区覆盖意图",
                    StatusCodes.Status428PreconditionRequired);
            if (!TryOperationsCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "operations.config.apply",
                "operations:config", body.Config, key, expected, false, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteOperationsConfig(() => _platform.ApplyOperationsConfig(current.Actor,
                    current.Payload, expected, current.Reason, current.AuditContext)));
            if (outcome.Success) NotifyOperationsPolicyChanged();
            var response = AdminCommandResponse(request, command, outcome);
            request.HttpContext.Response.Headers.ETag = $"\"{_platform.OperationsConfigVersion()}\"";
            return response;
        });
        _app.MapPost("/api/admin/operations/config/rollback",
            (HttpRequest request, OperationsConfigRollbackRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!string.Equals(body.CrossSectionReplaceIntent, OperationsCrossSectionReplaceIntent,
                    StringComparison.Ordinal))
                return ApiError(request, "operations_cross_section_intent_required",
                    "全量运营配置回滚必须显式确认跨分区覆盖意图",
                    StatusCodes.Status428PreconditionRequired);
            if (!TryOperationsCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12OperationsRollbackCommandPayload(body.VersionId?.Trim() ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "operations.config.rollback",
                "operations:config", payload, key, expected, false, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteOperationsConfig(() => _platform.RollbackOperationsConfig(current.Actor,
                    current.Payload.VersionId, expected, current.Reason, current.AuditContext)));
            if (outcome.Success) NotifyOperationsPolicyChanged();
            var response = AdminCommandResponse(request, command, outcome);
            request.HttpContext.Response.Headers.ETag = $"\"{_platform.OperationsConfigVersion()}\"";
            return response;
        });
        _app.MapPost("/api/admin/operations/server/start",
            (HttpRequest request, OperationsServerStartRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryOperationsCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            // The generic command bus rejects stale versions before invoking the operation. Server start is
            // state-idempotent: after a successful response is lost, a retry with a new key and the old version
            // must reach StartServer so it can observe that the server is already open. Keep the supplied
            // precondition in the signed payload and let the store enforce it only for maintenance -> open.
            var payload = new L12ServerStartCommandPayload(expected);
            var command = CommandEnvelope(request, authenticated.Account, permission, "operations.server.start",
                "operations:server", payload, key, null, false, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteOperationsConfig(() => _platform.StartServer(current.Actor,
                    current.Payload.ExpectedVersion,
                    current.Reason, current.AuditContext)));
            if (outcome.Success) NotifyOperationsPolicyChanged();
            var response = AdminCommandResponse(request, command, outcome);
            request.HttpContext.Response.Headers.ETag = $"\"{_platform.OperationsConfigVersion()}\"";
            return response;
        });
        _app.MapPost("/api/admin/operations/server/maintenance",
            (HttpRequest request, OperationsImmediateMaintenanceRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryOperationsCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            // Like explicit server start, immediate maintenance is state-idempotent. A retry after a lost
            // successful response must reach the store even when it carries the preceding version.
            var payload = new L12ImmediateMaintenanceStartCommandPayload(body.ExpectedDurationHours, expected);
            var command = CommandEnvelope(request, authenticated.Account, permission,
                "operations.server.maintenance.start", "operations:server", payload, key, null, false, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteOperationsConfig(() => _platform.BeginImmediateMaintenance(current.Actor,
                    current.Payload.ExpectedDurationHours, current.Payload.ExpectedVersion,
                    current.Reason, current.AuditContext)));
            if (outcome.Success) NotifyOperationsPolicyChanged();
            var response = AdminCommandResponse(request, command, outcome);
            request.HttpContext.Response.Headers.ETag = $"\"{_platform.OperationsConfigVersion()}\"";
            return response;
        });
        _app.MapPost("/api/admin/operations/server/maintenance/end",
            (HttpRequest request, OperationsImmediateMaintenanceEndRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminOperationsWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TryOperationsCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var key, out var expected, out failure)) return failure;
            var payload = new L12ImmediateMaintenanceEndCommandPayload(expected);
            var command = CommandEnvelope(request, authenticated.Account, permission,
                "operations.server.maintenance.end", "operations:server", payload, key, null, false, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteOperationsConfig(() => _platform.EndImmediateMaintenance(current.Actor,
                    current.Payload.ExpectedVersion, current.Reason, current.AuditContext)));
            if (outcome.Success) NotifyOperationsPolicyChanged();
            var response = AdminCommandResponse(request, command, outcome);
            request.HttpContext.Response.Headers.ETag = $"\"{_platform.OperationsConfigVersion()}\"";
            return response;
        });
        _app.MapGet("/api/admin/workbench/summary", (HttpRequest request) =>
        {
            var timing = new L12WorkbenchRequestTiming();
            if (!TryAuthenticate(request, L12Permission.AdminRuntimeRead, out var authenticated, out var failure))
                return failure;
            var account = authenticated.Account;
            var adminReads = new[]
            {
                L12Permission.AdminBugsRead, L12Permission.AdminAccountsRead,
                L12Permission.AdminRuntimeRead, L12Permission.AdminSecurityRead,
                L12Permission.AdminAuditRead,
            };
            if (!adminReads.Any(permission => L12Authorization.HasPermission(account, permission)))
                return ApiError(request, "permission_denied", "当前账号没有读取工作台摘要的权限",
                    StatusCodes.Status403Forbidden);

            timing.EndAuthentication();
            var sampledAt = DateTimeOffset.UtcNow;
            var pending = new List<L12AdminWorkbenchItemView>();
            var anomalies = new List<L12AdminWorkbenchItemView>();
            var recent = new List<L12AdminWorkbenchItemView>();
            var unavailable = new List<string>();
            void Capture(L12WorkbenchStage stage, string id, string kind, string label, string path,
                List<L12AdminWorkbenchItemView> target, Action read)
            {
                using var measured = timing.Measure(stage);
                try { read(); }
                catch (Exception error)
                {
                    unavailable.Add(id);
                    LogAdminReadFailure(request, $"workbench.{id}", error);
                    target.Add(new($"{id}-unavailable", kind, label,
                        $"该摘要暂时不可用；关联 ID：{CorrelationId(request)}", path, "unavailable"));
                }
            }
            if (L12Authorization.HasPermission(account, L12Permission.AdminBugsRead))
            {
                Capture(L12WorkbenchStage.Bugs, "bugs", "bug", "Bug 闭环", "/admin/users/bugs", pending, () =>
                {
                    var openBugs = _platform.OpenBugCount();
                    pending.Add(new("bugs", "bug", "Bug 闭环", openBugs == 0 ? "当前没有待处理 Bug" :
                        $"{openBugs} 条需要确认、处理或验证", "/admin/users/bugs",
                        openBugs == 0 ? "ok" : "attention", openBugs));
                });
            }
            if (L12Authorization.HasPermission(account, L12Permission.AdminAccountsRead))
            {
                Capture(L12WorkbenchStage.Renames, "renames", "account", "改名审核", "/admin/users/renames", pending, () =>
                {
                    var renameCount = _platform.UsernameChangeRequests("pending").Count;
                    pending.Add(new("renames", "account", "改名审核", renameCount == 0 ? "当前没有待审核申请" :
                        $"{renameCount} 条等待处理", "/admin/users/renames",
                        renameCount == 0 ? "ok" : "attention", renameCount));
                });
            }
            if (L12Authorization.HasPermission(account, L12Permission.AdminRuntimeRead))
            {
                Capture(L12WorkbenchStage.Runtime, "runtime", "runtime", "服务运行状态", "/admin/system/releases", anomalies, () =>
                {
                    var performance = _httpPerformance.Snapshot();
                    anomalies.Add(new("runtime", "runtime", "服务运行状态",
                        performance.WithinBudget == false ? "请求性能超出预算，请检查运行状态" :
                        $"{_rooms.RuntimeStats().ActiveGameCount} 场进行中",
                        "/admin/system/releases", performance.WithinBudget == false ? "warning" : "ok"));
                });
            }
            if (L12Authorization.HasPermission(account, L12Permission.AdminSecurityRead))
            {
                Capture(L12WorkbenchStage.Storage, "storage", "storage", "存储容量", "/admin/system/storage", anomalies, () =>
                {
                    var storage = _storageSnapshot();
                    anomalies.Add(new("storage", "storage", "存储容量", storage.Conclusion,
                        "/admin/system/storage", storage.Health));
                });
                Capture(L12WorkbenchStage.Security, "security", "security", "安全告警", "/admin/system/security", anomalies, () =>
                {
                    var security = _platform.SecurityStatus(account);
                    var alertCount = security.Alerts.Sum(item => (long)item.Count);
                    if (alertCount > 0)
                        anomalies.Add(new("security", "security", "安全告警", $"{alertCount} 项需要检查",
                            "/admin/system/security", "warning", (int)Math.Min(int.MaxValue, alertCount)));
                });
            }
            if (L12Authorization.HasPermission(account, L12Permission.AdminAuditRead))
            {
                Capture(L12WorkbenchStage.Audit, "audit", "audit", "最近活动", "/admin/system/audit", recent, () =>
                    recent.AddRange(_platform.AdminAudit(limit: 6).Select(item => new L12AdminWorkbenchItemView(
                        item.Id, item.Category, item.Action, $"{item.ActorName} · {item.Target}",
                        "/admin/system/audit", item.Outcome is "failed" or "denied" ? "warning" : "neutral",
                        OccurredAt: item.CreatedAt))));
            }
            request.HttpContext.Response.Headers["Server-Timing"] = timing.ToServerTiming();
            return Results.Ok(new L12AdminWorkbenchSummaryView(sampledAt, pending, anomalies, recent,
                unavailable.Count > 0, unavailable));
        });
        _app.MapGet("/api/admin/runtime/status", (HttpRequest request) =>
        {
            const L12Permission permission = L12Permission.AdminRuntimeRead;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var observedAt = DateTimeOffset.UtcNow;
            var build = L12RuntimeBuildVersion.Capture();
            var rooms = _rooms.RuntimeStats();
            var releases = _platform.ReleaseEnvironments(authenticated.Account, _releaseControl);
            var status = new L12RuntimeStatusView(observedAt,
                build.ServerRelease,
                _cardCount, rooms.OnlineAccountCount, _sockets.Count, rooms.RoomCount,
                rooms.ActiveGameCount, releases,
                new L12RuntimeDependencyView("cdn", false, "unavailable",
                    "no-authoritative-source", observedAt),
                _httpPerformance.Snapshot());
            return Results.Ok(status);
        });
        _app.MapGet("/api/admin/accounts/{accountId}/sessions", (HttpRequest request, string accountId) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminSessionsRead, out var authenticated, out var failure)) return failure;
            if (!_platform.AccountExists(accountId)) return ApiError(request, "account_not_found", "账号不存在", StatusCodes.Status404NotFound);
            return Results.Ok(_platform.Sessions(accountId, authenticated.SessionId));
        });
        _app.MapDelete("/api/admin/accounts/{accountId}/sessions/{sessionId}",
            (HttpRequest request, string accountId, string sessionId) =>
        {
            const L12Permission permission = L12Permission.AdminSessionsRevoke;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            var payload = new SessionCommandPayload(accountId, sessionId);
            var command = CommandEnvelope(request, authenticated.Account, permission, "account.session.revoke",
                $"account:{accountId}/session:{sessionId}", payload);
            var outcome = _adminCommands.Execute(command, permission,
                current => SessionCommandResult(_platform.RevokeAccountSession(current.Actor,
                    current.Payload.AccountId, current.Payload.SessionId!, current.AuditContext)),
                current => SessionCommandResult(_platform.RevokeAccountSession(current.Actor,
                    current.Payload.AccountId, current.Payload.SessionId!, current.AuditContext, true)));
            return AdminSessionCommandResponse(request, command, outcome);
        });
        _app.MapDelete("/api/admin/accounts/{accountId}/sessions", (HttpRequest request, string accountId) =>
        {
            const L12Permission permission = L12Permission.AdminSessionsRevoke;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            var payload = new SessionCommandPayload(accountId, null);
            var command = CommandEnvelope(request, authenticated.Account, permission, "account.sessions.revoke-all",
                $"account:{accountId}/sessions", payload);
            var outcome = _adminCommands.Execute(command, permission,
                current => SessionCommandResult(_platform.RevokeAccountSessions(current.Actor,
                    current.Payload.AccountId, current.AuditContext)),
                current => SessionCommandResult(_platform.RevokeAccountSessions(current.Actor,
                    current.Payload.AccountId, current.AuditContext, true)));
            return AdminSessionCommandResponse(request, command, outcome);
        });
        _app.MapGet("/api/admin/bugs", (HttpRequest request, string? status, string? priority, string? assignee, string? search) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminBugsRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.Bugs(status, priority, assignee, search));
        });
        _app.MapGet("/api/admin/bugs/query", (HttpRequest request, int? page, int? pageSize,
            string? status, string? priority, string? assignee, string? search) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminBugsRead, out _, out var failure)) return failure;
            try
            {
                return Results.Ok(_platform.QueryBugs(page ?? 1, pageSize ?? 10, status, priority,
                    assignee, search, request.HttpContext.RequestAborted));
            }
            catch (ArgumentException)
            {
                return ApiError(request, "invalid_bug_query", "Bug搜索最多200个字符",
                    StatusCodes.Status400BadRequest);
            }
        });
        _app.MapPatch("/api/admin/bugs/{id}", (HttpRequest request, string id, BugUpdateRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminBugsWrite;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            if (!string.IsNullOrWhiteSpace(body.VerifiedBy) || body.VerifiedAt is not null)
                return ApiError(request, "bug_verification_identity_client_forbidden",
                    "复测人与复测时间由服务端根据当前管理员自动记录，无需填写。",
                    StatusCodes.Status400BadRequest);
            var payload = new BugUpdateCommandPayload(id, body.Status, body.Priority, body.Assignee,
                body.AdminNotes, body.Comment, body.FixCommit, body.RegressionTest, body.DeployedVersion,
                body.DuplicateOf, body.ClosureDisposition);
            var command = CommandEnvelope(request, authenticated.Account, permission, "bug.update", $"bug:{id}",
                payload, body.IdempotencyKey, body.ExpectedVersion, body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission, current =>
            {
                try
                {
                    var updated = _platform.UpdateBug(current.Actor, current.Payload.Id, current.Payload.Status,
                        current.Payload.Priority, current.Payload.Assignee, current.Payload.AdminNotes,
                        current.Payload.Comment, current.AuditContext, current.Payload.FixCommit,
                        current.Payload.RegressionTest, current.Payload.DeployedVersion,
                        current.Payload.DuplicateOf, current.Payload.ClosureDisposition);
                    return updated is null
                        ? L12AdminCommandResult<L12BugReportView>.Fail("bug_not_found", "Bug 不存在",
                            StatusCodes.Status404NotFound)
                        : L12AdminCommandResult<L12BugReportView>.Ok(updated, "Bug 已更新");
                }
                catch (L12BugClosureValidationException error)
                {
                    return L12AdminCommandResult<L12BugReportView>.Fail(error.Code, error.Message,
                        StatusCodes.Status400BadRequest);
                }
            }, current =>
            {
                try
                {
                    var existing = _platform.UpdateBug(current.Actor, current.Payload.Id, current.Payload.Status,
                        current.Payload.Priority, current.Payload.Assignee, current.Payload.AdminNotes,
                        current.Payload.Comment, current.AuditContext, current.Payload.FixCommit,
                        current.Payload.RegressionTest, current.Payload.DeployedVersion,
                        current.Payload.DuplicateOf, current.Payload.ClosureDisposition, validateOnly: true);
                    return existing is null
                        ? L12AdminCommandResult<L12BugReportView>.Fail("bug_not_found", "Bug 不存在",
                            StatusCodes.Status404NotFound)
                        : L12AdminCommandResult<L12BugReportView>.Ok(existing, "干运行验证通过");
                }
                catch (L12BugClosureValidationException error)
                {
                    return L12AdminCommandResult<L12BugReportView>.Fail(error.Code, error.Message,
                        StatusCodes.Status400BadRequest);
                }
            });
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapGet("/api/admin/match-governance/draw-requests",
            (HttpRequest request, string? status, string? search) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminMatchGovernanceRead,
                    out var authenticated, out var failure)) return failure;
            return Results.Ok(_platform.MatchDrawRequests(authenticated.Account, status, search));
        });
        _app.MapGet("/api/admin/match-governance/player-reports",
            (HttpRequest request, string? status, string? search) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminMatchGovernanceRead,
                    out var authenticated, out var failure)) return failure;
            return Results.Ok(_platform.PlayerMatchReports(authenticated.Account, status, search));
        });
        _app.MapPatch("/api/admin/match-governance/draw-requests/{id}",
            (HttpRequest request, string id, L12MatchGovernanceUpdate body) =>
        {
            const L12Permission permission = L12Permission.AdminMatchGovernanceWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                return Results.Ok(_platform.UpdateMatchDrawRequest(authenticated.Account, id, body,
                    RequestAuditContext(request, permission)));
            }
            catch (KeyNotFoundException error)
            {
                return ApiError(request, "draw_request_not_found", error.Message,
                    StatusCodes.Status404NotFound);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_draw_governance_update", error.Message,
                    StatusCodes.Status400BadRequest);
            }
        });
        _app.MapPatch("/api/admin/match-governance/player-reports/{id}",
            (HttpRequest request, string id, L12MatchGovernanceUpdate body) =>
        {
            const L12Permission permission = L12Permission.AdminMatchGovernanceWrite;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                return Results.Ok(_platform.UpdatePlayerMatchReport(authenticated.Account, id, body,
                    RequestAuditContext(request, permission)));
            }
            catch (KeyNotFoundException error)
            {
                return ApiError(request, "player_report_not_found", error.Message,
                    StatusCodes.Status404NotFound);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_player_report_update", error.Message,
                    StatusCodes.Status400BadRequest);
            }
        });
        _app.MapGet("/api/content", (HttpRequest request) =>
        {
            var keys = request.Query["key"].Where(key => _platform.IsContentKeyAllowed(key)).Distinct(StringComparer.OrdinalIgnoreCase);
            return Results.Ok(_platform.PublicContents(keys!));
        });
        _app.MapGet("/api/content/{key}", (string key) =>
        {
            var content = _platform.PublicContents([key]);
            return Results.Ok(new
            {
                key,
                value = content.Values.GetValueOrDefault(key, string.Empty),
                content.ObservedAt,
                content.NextRuleTransitionAt,
            });
        });
        _app.MapGet("/api/site/home", () => Results.Ok(_platform.PublicSiteHome()));
        _app.MapGet("/api/site/categories", (string? kind) => Results.Ok(_platform.PublicSiteCategories(kind)));
        _app.MapGet(L12SharePage.Endpoint, (HttpRequest request) =>
        {
            var path = request.Headers[L12SharePage.OriginalPathHeader].FirstOrDefault()
                ?? request.Query["path"].FirstOrDefault() ?? "/";
            try
            {
                request.HttpContext.Response.Headers.CacheControl = "public,max-age=60,must-revalidate";
                request.HttpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
                return Results.Content(L12SharePage.RenderDeploymentPage(_platform, path),
                    "text/html; charset=utf-8", Encoding.UTF8);
            }
            catch (IOException)
            {
                return Results.Problem("站点页面入口暂时不可用", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
        _app.MapGet("/api/site/media/{id}/{variant}/{fileName}", (HttpRequest request, string id,
            string variant, string fileName) =>
        {
            var file = _platform.ResolveSiteMediaFile(id, variant, fileName);
            if (file is null) return Results.NotFound();
            request.HttpContext.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
            request.HttpContext.Response.Headers.ETag = $"\"{file.Hash}\"";
            request.HttpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(file.Path, file.ContentType, enableRangeProcessing: false);
        });
        _app.MapGet("/api/articles", (string? category, string? search, int? limit, string? kind) =>
            Results.Ok(_platform.PublicArticles(category, search, limit ?? 100, kind ?? "news")));
        _app.MapGet("/api/admin/articles", (HttpRequest request, string? status, string? category,
            string? search, int? limit, string? kind) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AdminArticles(status, category, search, limit ?? 300, kind ?? "news"));
        });
        _app.MapGet("/api/admin/articles/modian/preview", async (HttpRequest request,
            CancellationToken cancellationToken) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out _, out var failure)) return failure;
            try { return Results.Ok(await _modianImports.PreviewAsync(cancellationToken)); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Results.Empty;
            }
            catch (Exception error) when (error is HttpRequestException or TimeoutException
                or InvalidDataException or JsonException)
            {
                return ApiError(request, "modian_preview_failed", "固定项目更新暂时无法检查，请稍后重试",
                    StatusCodes.Status502BadGateway);
            }
        });
        _app.MapPost("/api/admin/articles/modian/import", async (HttpRequest request,
            L12ModianImportRequest body, CancellationToken cancellationToken) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var rawKey = request.Headers["Idempotency-Key"].FirstOrDefault() ?? body.IdempotencyKey;
                var idempotencyKey = rawKey?.Trim();
                if (idempotencyKey?.Length > 128) throw new ArgumentException("幂等键不能超过 128 字符");
                var audit = RequestAuditContext(request, permission) with { IdempotencyKey = idempotencyKey };
                return Results.Ok(await _modianImports.ImportAsync(authenticated.Account, body, audit,
                    cancellationToken));
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_modian_import", error.Message, StatusCodes.Status400BadRequest);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Results.Empty;
            }
            catch (Exception error) when (error is HttpRequestException or TimeoutException
                or InvalidDataException or JsonException)
            {
                return ApiError(request, "modian_import_unavailable", "固定项目更新暂时无法导入，请稍后重试",
                    StatusCodes.Status502BadGateway);
            }
        });
        _app.MapGet("/api/admin/articles/{id}", (HttpRequest request, string id) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            var article = _platform.AdminArticle(id);
            return article is null
                ? ApiError(request, "article_not_found", "稿件不存在", StatusCodes.Status404NotFound)
                : Results.Ok(article);
        });
        _app.MapPost("/api/admin/articles", (HttpRequest request, ArticleDraftRequest body) =>
            SaveArticleDraftResponse(request, null, body));
        _app.MapPut("/api/admin/articles/{id}", (HttpRequest request, string id, ArticleDraftRequest body) =>
            SaveArticleDraftResponse(request, id, body));
        _app.MapPost("/api/admin/articles/{id}/publish", (HttpRequest request, string id) =>
            ArticleMutationResponse(request, L12Permission.AdminContentPublish, id, "publish",
                (actor, context) => _platform.PublishArticle(actor, id, context)));
        _app.MapPost("/api/admin/articles/{id}/withdraw", (HttpRequest request, string id) =>
            ArticleMutationResponse(request, L12Permission.AdminContentPublish, id, "withdraw",
                (actor, context) => _platform.ChangeArticleStatus(actor, id, "withdraw", context)));
        _app.MapPost("/api/admin/articles/{id}/archive", (HttpRequest request, string id) =>
            ArticleMutationResponse(request, L12Permission.AdminContentDraft, id, "archive",
                (actor, context) => _platform.ChangeArticleStatus(actor, id, "archive", context)));
        _app.MapPost("/api/admin/articles/{id}/restore", (HttpRequest request, string id) =>
            ArticleMutationResponse(request, L12Permission.AdminContentDraft, id, "restore",
                (actor, context) => _platform.ChangeArticleStatus(actor, id, "restore", context)));
        _app.MapGet("/api/admin/articles/{id}/revisions", (HttpRequest request, string id) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            try { return Results.Ok(_platform.ArticleRevisions(id)); }
            catch (KeyNotFoundException error)
            {
                return ApiError(request, "article_not_found", error.Message, StatusCodes.Status404NotFound);
            }
        });
        _app.MapPost("/api/admin/articles/{id}/revisions/{revision:long}/restore",
            (HttpRequest request, string id, long revision) =>
                ArticleMutationResponse(request, L12Permission.AdminContentDraft, id, "restore-revision",
                    (actor, context) => _platform.RestoreArticleRevision(actor, id, revision, context)));
        _app.MapGet("/api/admin/site/media/policies", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(L12PlatformStore.SiteMediaPolicies());
        });
        _app.MapGet("/api/admin/site/media", (HttpRequest request, string? kind, int? limit) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AdminSiteMedia(kind, limit ?? 500));
        });
        _app.MapGet("/api/admin/site/media/search", (HttpRequest request, string? kind, string? search, int? page, int? pageSize, string? id, string? ids) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            try { return Results.Ok(_platform.SearchSiteMedia(kind, search, page ?? 1, pageSize ?? 24, id, ids?.Split(','))); }
            catch (ArgumentException error)
            { return ApiError(request, "media_search_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        _app.MapPost("/api/admin/site/media", async (HttpRequest request) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var bodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySize is { IsReadOnly: false })
                bodySize.MaxRequestBodySize = L12PlatformStore.SiteMediaRequestMaxBytes;
            if (!request.HasFormContentType)
                return ApiError(request, "media_form_required", "素材上传必须使用 multipart/form-data",
                    StatusCodes.Status415UnsupportedMediaType);
            if (request.ContentLength is > L12PlatformStore.SiteMediaRequestMaxBytes)
                return ApiError(request, "media_upload_too_large", "图片上传总量不能超过 32MB，请压缩原图后重试",
                    StatusCodes.Status413PayloadTooLarge);
            try
            {
                var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
                var original = await ReadRequiredFormFile(form, "original", L12PlatformStore.SiteMediaOriginalMaxBytes,
                    request.HttpContext.RequestAborted);
                var desktop = await ReadRequiredFormFile(form, "desktop", L12PlatformStore.SiteMediaDesktopMaxBytes,
                    request.HttpContext.RequestAborted);
                var mobile = await ReadRequiredFormFile(form, "mobile", L12PlatformStore.SiteMediaMobileMaxBytes,
                    request.HttpContext.RequestAborted);
                var thumbnail = await ReadRequiredFormFile(form, "thumbnail", L12PlatformStore.SiteMediaThumbnailMaxBytes,
                    request.HttpContext.RequestAborted);
                var focalX = ParseFiniteDouble(form["focalX"], .5);
                var focalY = ParseFiniteDouble(form["focalY"], .5);
                var result = _platform.UploadSiteMedia(authenticated.Account, new L12SiteMediaUpload(
                    form["kind"].ToString(), original.FileName, original.ContentType, original.Bytes,
                    desktop.Bytes, mobile.Bytes, thumbnail.Bytes, form["altText"].ToString(), focalX, focalY,
                    form["desktopAltText"].ToString(), form["mobileAltText"].ToString(),
                    form["thumbnailAltText"].ToString(),
                    bool.TryParse(form["independentVariants"].ToString(), out var independentVariants) &&
                    independentVariants),
                    RequestAuditContext(request, permission));
                return Results.Ok(result);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "media_upload_invalid", error.Message, StatusCodes.Status400BadRequest);
            }
            catch (SiteMediaUploadTooLargeException error)
            {
                return ApiError(request, "media_upload_too_large", error.Message,
                    StatusCodes.Status413PayloadTooLarge);
            }
            catch (InvalidDataException error)
            {
                return ApiError(request, "media_upload_invalid", error.Message, StatusCodes.Status400BadRequest);
            }
            catch (BadHttpRequestException error) when (error.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return ApiError(request, "media_upload_too_large", "图片上传总量不能超过 32MB，请压缩原图后重试",
                    StatusCodes.Status413PayloadTooLarge);
            }
        });
        _app.MapDelete("/api/admin/site/media/{id}", (HttpRequest request, string id) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                _platform.DeleteSiteMedia(authenticated.Account, id, RequestAuditContext(request, permission));
                return Results.NoContent();
            }
            catch (KeyNotFoundException error)
            {
                return ApiError(request, "media_not_found", error.Message, StatusCodes.Status404NotFound);
            }
            catch (L12SiteContentConflictException error)
            {
                return ApiError(request, "media_referenced", error.Message, StatusCodes.Status409Conflict);
            }
        });
        _app.MapGet("/api/admin/site/categories", (HttpRequest request, string? kind) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AdminSiteCategories(kind));
        });
        _app.MapPost("/api/admin/site/categories", (HttpRequest request, SiteCategoryRequest body) =>
            SaveSiteCategoryResponse(request, null, body));
        _app.MapPut("/api/admin/site/categories/{id}", (HttpRequest request, string id, SiteCategoryRequest body) =>
            SaveSiteCategoryResponse(request, id, body));
        _app.MapPut("/api/admin/site/categories/order/{kind}", (HttpRequest request, string kind,
            SiteCategoryOrderRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                return Results.Ok(_platform.ReorderSiteCategories(authenticated.Account, kind, body.Ids ?? [],
                    RequestAuditContext(request, permission)));
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "category_order_invalid", error.Message, StatusCodes.Status400BadRequest);
            }
        });
        _app.MapDelete("/api/admin/site/categories/{id}", (HttpRequest request, string id, string? migrateTo) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                _platform.DeleteSiteCategory(authenticated.Account, id, migrateTo,
                    RequestAuditContext(request, permission));
                return Results.NoContent();
            }
            catch (KeyNotFoundException error)
            {
                return ApiError(request, "category_not_found", error.Message, StatusCodes.Status404NotFound);
            }
            catch (L12SiteContentConflictException error)
            {
                return ApiError(request, "category_not_empty", error.Message, StatusCodes.Status409Conflict);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "category_delete_invalid", error.Message, StatusCodes.Status400BadRequest);
            }
        });
        _app.MapGet("/api/admin/content/{key}", (HttpRequest request, string key) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            try { return Results.Ok(_platform.GetContentEntry(key)); }
            catch (ArgumentException error)
            {
                return ApiError(request, "content_key_not_allowed", error.Message, StatusCodes.Status400BadRequest);
            }
        });
        _app.MapPut("/api/admin/content/{key}/draft", (HttpRequest request, string key, ContentRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            var payload = new ContentDraftCommandPayload(key, body.Value ?? string.Empty);
            var command = CommandEnvelope(request, authenticated.Account, permission, "content.draft.save",
                $"content:{key}", payload, body.IdempotencyKey, body.ExpectedVersion, body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission, current =>
            {
                if (!_platform.IsContentKeyAllowed(current.Payload.Key))
                    return L12AdminCommandResult<L12ContentEntryView>.Fail("content_key_not_allowed",
                        "内容键不在白名单中", StatusCodes.Status400BadRequest);
                return L12AdminCommandResult<L12ContentEntryView>.Ok(_platform.SaveContentDraft(current.Actor,
                    current.Payload.Key, current.Payload.Value, current.AuditContext,
                    current.Payload.Key is "rules.center" or "rules.rulings" ? current.ExpectedVersion : null), "草稿已保存");
            }, current =>
            {
                if (!_platform.IsContentKeyAllowed(current.Payload.Key))
                    return L12AdminCommandResult<L12ContentEntryView>.Fail("content_key_not_allowed",
                        "内容键不在白名单中", StatusCodes.Status400BadRequest);
                var entry = _platform.GetContentEntry(current.Payload.Key);
                return L12AdminCommandResult<L12ContentEntryView>.Ok(entry with
                {
                    DraftValue = current.Payload.Value,
                    Status = current.Payload.Value == entry.PublishedValue ? "published" : "draft",
                }, "干运行验证通过");
            });
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapPost("/api/admin/content/{key}/publish", (HttpRequest request, string key) =>
        {
            const L12Permission permission = L12Permission.AdminContentPublish;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var payload = _platform.CaptureContentPublish([key]);
                var command = CommandEnvelope(request, authenticated.Account, permission, "content.publish.batch",
                    $"content:{key}", payload);
                var outcome = ExecuteContentPublish(command, permission);
                return AdminCommandResponse(request, command, outcome);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "content_key_not_allowed", error.Message, StatusCodes.Status400BadRequest);
            }
        });
        _app.MapGet("/api/admin/content/keys", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.ContentKeys());
        });
        _app.MapGet("/api/admin/content/batches", (HttpRequest request, int? limit) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.ContentBatches(limit ?? 100));
        });
        _app.MapPost("/api/admin/content/preview", (HttpRequest request, ContentBatchRequest body) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            try { return Results.Ok(_platform.PreviewContentPublish(_platform.CaptureContentPublish(body.Keys ?? []))); }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_content_batch", error.Message, StatusCodes.Status400BadRequest);
            }
            catch (L12ContentStateConflictException error)
            {
                return ApiError(request, "content_version_conflict", error.Message, StatusCodes.Status409Conflict);
            }
        });
        _app.MapPost("/api/admin/content/publish", (HttpRequest request, ContentBatchRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminContentPublish;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var payload = _platform.CaptureContentPublish(body.Keys ?? []);
                var command = CommandEnvelope(request, authenticated.Account, permission, "content.publish.batch",
                    $"content-batch:{string.Join(',', payload.Items.Select(item => item.Key))}", payload,
                    body.IdempotencyKey, body.ExpectedVersion, body.DryRun, body.Reason);
                var outcome = ExecuteContentPublish(command, permission);
                return AdminCommandResponse(request, command, outcome);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_content_batch", error.Message, StatusCodes.Status400BadRequest);
            }
        });
        _app.MapPost("/api/admin/content/rollback", (HttpRequest request, ContentRollbackRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminContentRollback;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var payload = CaptureContentRollbackForCommand(request, authenticated.Account, body);
                var command = CommandEnvelope(request, authenticated.Account, permission, "content.rollback.batch",
                    $"content-batch:{payload.BatchId}", payload, body.IdempotencyKey, body.ExpectedVersion,
                    body.DryRun, body.Reason);
                var outcome = ExecuteContentRollback(command, permission);
                return AdminCommandResponse(request, command, outcome);
            }
            catch (KeyNotFoundException error)
            {
                return ApiError(request, "content_batch_not_found", error.Message, StatusCodes.Status404NotFound);
            }
            catch (L12ContentStateConflictException error)
            {
                return ApiError(request, "content_version_conflict", error.Message, StatusCodes.Status409Conflict);
            }
        });
        _app.MapGet("/api/admin/effect-atoms", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminEffectsRead, out _, out var failure)) return failure;
            return Results.Ok(L12EffectAtomRegistry.All);
        });
        _app.MapGet("/api/admin/effects/coverage", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminEffectsRead, out _, out var failure)) return failure;
            return Results.Ok(_catalog.AtomicEffects.Coverage());
        });
        _app.MapGet("/api/admin/effects", (HttpRequest request, string? search, string? status, string? product,
            string? atomKind, int? page, int? pageSize) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminEffectsRead, out _, out var failure)) return failure;
            var reviewed = _platform.ApplyEffectReviews(
                _catalog.AtomicEffects.Query(search, status, product, atomKind, page ?? 1, pageSize ?? 50));
            return Results.Ok(_platform.ApplyEffectPresentationOverrides(reviewed));
        });
        _app.MapGet("/api/admin/effects/{cardId}", (HttpRequest request, string cardId) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminEffectsRead, out _, out var failure)) return failure;
            var effect = _catalog.AtomicEffects.Find(cardId);
            if (effect is null) return Results.NotFound();
            return Results.Ok(_platform.ApplyEffectPresentationOverrides(_platform.ApplyEffectReviews(effect)));
        });
        _app.MapGet("/api/admin/effect-workbench/styles", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminEffectsRead, out _, out var failure)) return failure;
            return Results.Ok(L12PlatformStore.EffectPresentationStyles);
        });
        _app.MapGet("/api/admin/effects/{cardId}/workbench", (HttpRequest request, string cardId) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminEffectsRead, out _, out var failure)) return failure;
            var effect = _catalog.AtomicEffects.Find(cardId);
            if (effect is null) return ApiError(request, "effect_not_found", "卡牌效果不存在",
                StatusCodes.Status404NotFound);
            return Results.Ok(_platform.EffectWorkbench(
                _platform.ApplyEffectPresentationOverrides(_platform.ApplyEffectReviews(effect))));
        });
        _app.MapPut("/api/admin/effects/{cardId}/workbench/draft",
            (HttpRequest request, string cardId, L12EffectWorkbenchSaveRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminEffectsReview;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var effect = _catalog.AtomicEffects.Find(cardId);
            if (effect is null) return ApiError(request, "effect_not_found", "卡牌效果不存在",
                StatusCodes.Status404NotFound);
            try
            {
                return Results.Ok(_platform.SaveEffectWorkbenchDraft(authenticated.Account,
                    _platform.ApplyEffectPresentationOverrides(_platform.ApplyEffectReviews(effect)), body,
                    RequestAuditContext(request, permission)));
            }
            catch (InvalidOperationException error)
            {
                return ApiError(request, "effect_workbench_conflict", error.Message, StatusCodes.Status409Conflict);
            }
            catch (ArgumentException error)
            {
                return ApiError(request, "invalid_effect_workbench", error.Message, StatusCodes.Status400BadRequest);
            }
        });
        _app.MapPost("/api/admin/effects/{cardId}/workbench/validate",
            (HttpRequest request, string cardId, L12EffectWorkbenchActionRequest body) =>
            EffectWorkbenchAction(request, cardId, body, "validate"));
        _app.MapPost("/api/admin/effects/{cardId}/workbench/review",
            (HttpRequest request, string cardId, L12EffectWorkbenchActionRequest body) =>
            EffectWorkbenchAction(request, cardId, body, "review"));
        _app.MapPost("/api/admin/effects/{cardId}/workbench/publish",
            (HttpRequest request, string cardId, L12EffectWorkbenchActionRequest body) =>
            EffectWorkbenchAction(request, cardId, body, "publish"));
        _app.MapPost("/api/admin/effects/{cardId}/workbench/rollback",
            (HttpRequest request, string cardId, L12EffectWorkbenchActionRequest body) =>
            EffectWorkbenchAction(request, cardId, body, "rollback"));
        _app.MapPut("/api/admin/effects/{cardId}/review", (HttpRequest request, string cardId, EffectReviewRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminEffectsReview;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            var payload = new EffectReviewCommandPayload(cardId, body.AbilityId, body.Status ?? "unreviewed", body.Note);
            var command = CommandEnvelope(request, authenticated.Account, permission, "effect.review.save",
                body.AbilityId is null ? $"effect:{cardId}" : $"effect:{cardId}/{body.AbilityId}", payload,
                body.IdempotencyKey, body.ExpectedVersion, body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission, ExecuteEffectReview,
                current => ValidateEffectReview(current, false));
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapPost("/api/admin/rule-items/publish", (HttpRequest request, L12RuleItemPublishRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminContentPublish;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "rule-item.publish",
                $"content:{body.Key}/{body.Collection}/{body.ItemId}", body, body.IdempotencyKey, null,
                body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission, current =>
            {
                try
                {
                    var entry = _platform.PublishRuleItem(current.Actor, current.Payload, current.AuditContext);
                    NotifyRulesContentChanged();
                    return L12AdminCommandResult<L12ContentEntryView>.Ok(entry, "规则条目已发布");
                }
                catch (KeyNotFoundException error)
                {
                    return L12AdminCommandResult<L12ContentEntryView>.Fail("rule_item_not_found", error.Message,
                        StatusCodes.Status404NotFound);
                }
                catch (L12ContentStateConflictException error)
                {
                    return L12AdminCommandResult<L12ContentEntryView>.Fail("content_version_conflict", error.Message,
                        StatusCodes.Status409Conflict);
                }
                catch (ArgumentException error)
                {
                    return L12AdminCommandResult<L12ContentEntryView>.Fail("invalid_rule_item", error.Message,
                        StatusCodes.Status400BadRequest);
                }
            });
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapPost("/api/admin/rule-items/create", (HttpRequest request, L12RuleItemCreateRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "rule-item.create",
                $"content:{body.Key}/{body.Collection}", body, body.IdempotencyKey, body.ExpectedVersion,
                body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission, current =>
            {
                try
                {
                    var entry = _platform.CreateRuleItem(current.Actor, current.Payload, current.AuditContext);
                    return L12AdminCommandResult<L12ContentEntryView>.Ok(entry, "规则资料子板块已新建");
                }
                catch (L12ContentStateConflictException error)
                {
                    return L12AdminCommandResult<L12ContentEntryView>.Fail("content_version_conflict", error.Message,
                        StatusCodes.Status409Conflict);
                }
                catch (ArgumentException error)
                {
                    return L12AdminCommandResult<L12ContentEntryView>.Fail("invalid_rule_item", error.Message,
                        StatusCodes.Status400BadRequest);
                }
            });
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapPost("/api/admin/rule-items/delete", (HttpRequest request, L12RuleItemDeleteRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            var command = CommandEnvelope(request, authenticated.Account, permission, "rule-item.delete",
                $"content:{body.Key}/{body.Collection}/{body.ItemId}", body, body.IdempotencyKey,
                body.ExpectedVersion, body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission, current =>
            {
                try
                {
                    var entry = _platform.DeleteRuleItem(current.Actor, current.Payload, current.AuditContext);
                    NotifyRulesContentChanged();
                    return L12AdminCommandResult<L12ContentEntryView>.Ok(entry, "规则资料子板块已删除");
                }
                catch (KeyNotFoundException error)
                {
                    return L12AdminCommandResult<L12ContentEntryView>.Fail("rule_item_not_found", error.Message,
                        StatusCodes.Status404NotFound);
                }
                catch (UnauthorizedAccessException error)
                {
                    return L12AdminCommandResult<L12ContentEntryView>.Fail("permission_denied", error.Message,
                        StatusCodes.Status403Forbidden);
                }
                catch (L12ContentStateConflictException error)
                {
                    return L12AdminCommandResult<L12ContentEntryView>.Fail("content_version_conflict", error.Message,
                        StatusCodes.Status409Conflict);
                }
                catch (ArgumentException error)
                {
                    return L12AdminCommandResult<L12ContentEntryView>.Fail("invalid_rule_item", error.Message,
                        StatusCodes.Status400BadRequest);
                }
            });
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapGet("/api/me/alternate-arts", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(_platform.OwnedAlternateArts(account.Id));
        });
        _app.MapGet("/api/me/alternate-art-grant-notifications", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized() : Results.Ok(_platform.PendingAlternateArtGrantNotifications(account.Id));
        });
        _app.MapGet("/api/me/season-summary-notifications", (HttpRequest request) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            return account is null ? Results.Unauthorized()
                : Results.Ok(_platform.PendingSeasonSummaryNotifications(account.Id));
        });
        _app.MapPost("/api/me/season-summary-notifications/{id}/acknowledge",
            (HttpRequest request, string id) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            try
            {
                _platform.AcknowledgeSeasonSummaryNotification(account.Id, id);
                return Results.NoContent();
            }
            catch (KeyNotFoundException error)
            {
                return ApiError(request, "season_summary_notification_missing", error.Message,
                    StatusCodes.Status404NotFound);
            }
        });
        _app.MapPost("/api/me/alternate-art-grant-notifications/{id}/acknowledge", (HttpRequest request, string id) =>
        {
            var account = _platform.Authenticate(request.Headers.Authorization);
            if (account is null) return Results.Unauthorized();
            try { _platform.AcknowledgeAlternateArtGrantNotification(account.Id, id); return Results.NoContent(); }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_grant_notification_missing", error.Message, StatusCodes.Status404NotFound); }
        });
        // 画廊是公开展示；权益只在构筑选用和开局二次校验时生效。
        _app.MapGet("/api/alternate-arts", () => Results.Ok(_platform.AlternateArts()));
        _app.MapGet("/api/gallery/presentation", () => Results.Ok(_platform.GalleryPresentationCatalog()));
        _app.MapGet("/api/admin/alternate-art-products", (HttpRequest request, bool? includeInactive) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AlternateArtProducts(includeInactive == true));
        });
        _app.MapGet("/api/admin/alternate-art-rarities", (HttpRequest request, bool? includeInactive) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AlternateArtRarities(includeInactive == true));
        });
        _app.MapPut("/api/admin/alternate-art-rarities", (HttpRequest request, L12AlternateArtRarityDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveAlternateArtRarity(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_rarity_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        _app.MapPut("/api/admin/alternate-art-products", (HttpRequest request, L12AlternateArtProductDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveAlternateArtProduct(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_product_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        _app.MapPost("/api/admin/alternate-art-products/merge", (HttpRequest request, L12AlternateArtProductMergeDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.MergeAlternateArtProducts(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_product_merge_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        _app.MapPut("/api/admin/alternate-art-products/filter-visibility",
            (HttpRequest request, L12AlternateArtProductFilterVisibilityDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveGalleryProductFilterVisibility(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (ArgumentException error) { return ApiError(request, "gallery_product_settings_invalid", error.Message, StatusCodes.Status400BadRequest); }
            catch (KeyNotFoundException error) { return ApiError(request, "gallery_product_missing", error.Message, StatusCodes.Status404NotFound); }
        });
        _app.MapGet("/api/admin/alternate-arts", (HttpRequest request, bool? includeInactive) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AdminAlternateArts(includeInactive == true));
        });
        _app.MapGet("/api/admin/alternate-arts/search", (HttpRequest request, string? name, string? artCode,
            string? baseCard, int? page, int? pageSize, bool? includeInactive) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.SearchAlternateArts(name, artCode, baseCard, page ?? 1, pageSize ?? 20,
                includeInactive != false));
        });
        _app.MapGet("/api/admin/gallery/regular-cards/search", (HttpRequest request, string? name,
            string? cardCode, string? productId, int? page, int? pageSize) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.SearchRegularGalleryCards(name, cardCode, productId,
                page ?? 1, pageSize ?? 20));
        });
        _app.MapPut("/api/admin/gallery/regular-cards/metadata",
            (HttpRequest request, L12RegularGalleryCardMetadataDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveRegularGalleryCardMetadata(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (KeyNotFoundException error) { return ApiError(request, "gallery_regular_card_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (ArgumentException error) { return ApiError(request, "gallery_regular_card_metadata_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        _app.MapPut("/api/admin/gallery/regular-cards/image",
            (HttpRequest request, L12RegularGalleryCardImageDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveRegularGalleryCardImage(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (KeyNotFoundException error) { return ApiError(request, "gallery_regular_card_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (ArgumentException error) { return ApiError(request, "gallery_regular_card_image_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        _app.MapGet("/api/admin/server-storage", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminSecurityRead, out _, out var failure)) return failure;
            try { return Results.Ok(_storageSnapshot()); }
            catch (Exception error)
            {
                LogAdminReadFailure(request, "server-storage", error);
                return ApiError(request, "server_storage_unavailable",
                    "服务器存储采样暂时不可用，请使用关联 ID 查询日志", StatusCodes.Status503ServiceUnavailable);
            }
        });
        _app.MapPut("/api/admin/alternate-arts", (HttpRequest request, L12AlternateArtDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveAlternateArt(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        _app.MapPut("/api/admin/alternate-arts/metadata", (HttpRequest request, L12AlternateArtMetadataDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveAlternateArtMetadata(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_metadata_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        _app.MapPut("/api/admin/alternate-arts/image", (HttpRequest request, L12AlternateArtImageDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveAlternateArtImage(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_image_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        _app.MapGet("/api/admin/alternate-art-grants", (HttpRequest request, string? username) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AlternateArtGrants(username));
        });
        _app.MapPost("/api/admin/alternate-art-grants", (HttpRequest request, L12AlternateArtGrantDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var grant = _platform.GrantAlternateArt(authenticated.Account, draft, RequestAuditContext(request, permission));
                NotifyAlternateArtNotificationsChanged([grant.AccountId]);
                return Results.Ok(grant);
            }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_target_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_grant_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        _app.MapDelete("/api/admin/alternate-art-grants/{id}", (HttpRequest request, string id) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { _platform.RevokeAlternateArtGrant(authenticated.Account, id, RequestAuditContext(request, permission)); return Results.NoContent(); }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_grant_missing", error.Message, StatusCodes.Status404NotFound); }
        });
        _app.MapGet("/api/admin/alternate-art-award-rules", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AlternateArtAwardRules());
        });
        _app.MapPut("/api/admin/alternate-art-award-rules", (HttpRequest request, L12AlternateArtAwardRuleDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try { return Results.Ok(_platform.SaveAlternateArtAwardRule(authenticated.Account, draft, RequestAuditContext(request, permission))); }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_award_rule_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        _app.MapPost("/api/admin/alternate-art-award-rules/event-dispatch", (HttpRequest request, L12AlternateArtEventDispatchDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var grants = _platform.DispatchAlternateArtEvent(authenticated.Account, draft, RequestAuditContext(request, permission));
                NotifyAlternateArtNotificationsChanged(grants.Select(grant => grant.AccountId));
                return Results.Ok(grants);
            }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_event_target_missing", error.Message, StatusCodes.Status404NotFound); }
            catch (ArgumentException error) { return ApiError(request, "alternate_art_event_invalid", error.Message, StatusCodes.Status400BadRequest); }
        });
        _app.MapPost("/api/admin/alternate-art-grants/ranked-participants/preview", (HttpRequest request, L12AlternateArtRankedParticipantDispatchDraft draft) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminContentRead, out _, out var failure)) return failure;
            try { return Results.Ok(_platform.PreviewRankedParticipantAlternateArtDispatch(draft)); }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_missing", error.Message, StatusCodes.Status404NotFound); }
        });
        _app.MapPost("/api/admin/alternate-art-grants/ranked-participants/dispatch", (HttpRequest request, L12AlternateArtRankedParticipantDispatchDraft draft) =>
        {
            const L12Permission permission = L12Permission.AdminContentDraft;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            try
            {
                var grants = _platform.DispatchRankedParticipantAlternateArt(authenticated.Account, draft, RequestAuditContext(request, permission));
                NotifyAlternateArtNotificationsChanged(grants.Select(grant => grant.AccountId));
                return Results.Ok(grants);
            }
            catch (KeyNotFoundException error) { return ApiError(request, "alternate_art_missing", error.Message, StatusCodes.Status404NotFound); }
        });
        _app.MapPut("/api/admin/effects/{cardId}/presentations/{sceneId}",
            (HttpRequest request, string cardId, string sceneId, EffectPresentationRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminEffectsReview;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            var payload = new EffectPresentationCommandPayload(cardId, sceneId, body.Text, false);
            var command = CommandEnvelope(request, authenticated.Account, permission, "effect.presentation.save",
                $"effect-presentation:{sceneId}", payload, body.IdempotencyKey, body.ExpectedVersion,
                body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission, ExecuteEffectPresentation,
                current => ValidateEffectPresentation(current, false));
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapPost("/api/admin/effects/{cardId}/presentations/{sceneId}/restore",
            (HttpRequest request, string cardId, string sceneId, EffectPresentationRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminEffectsReview;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            var payload = new EffectPresentationCommandPayload(cardId, sceneId, null, true);
            var command = CommandEnvelope(request, authenticated.Account, permission, "effect.presentation.restore",
                $"effect-presentation:{sceneId}", payload, body.IdempotencyKey, body.ExpectedVersion,
                body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission, ExecuteEffectPresentation,
                current => ValidateEffectPresentation(current, false));
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapGet("/api/admin/commands", (HttpRequest request, string? status, string? type,
            string? actorId, int? limit) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminCommandsRead, out var authenticated, out var failure))
                return failure;
            var canReview = L12Authorization.HasPermission(authenticated.Account, L12Permission.AdminApprovalsRead);
            return Results.Ok(_platform.AdminCommands(status, type,
                canReview ? actorId : authenticated.Account.Id, limit ?? 200));
        });
        _app.MapGet("/api/admin/commands/query", (HttpRequest request, int? page, int? pageSize,
            string? status, string? type, string? actorId) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminCommandsRead, out var authenticated, out var failure))
                return failure;
            var canReview = L12Authorization.HasPermission(authenticated.Account, L12Permission.AdminApprovalsRead);
            return Results.Ok(_platform.QueryAdminCommands(page ?? 1, pageSize ?? 10, status, type,
                canReview ? actorId : authenticated.Account.Id, request.HttpContext.RequestAborted));
        });
        _app.MapGet("/api/admin/commands/{id}", (HttpRequest request, string id) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminCommandsRead, out var authenticated, out var failure))
                return failure;
            var command = _platform.AdminCommand(id);
            if (command is null || command.ActorId != authenticated.Account.Id
                && !L12Authorization.HasPermission(authenticated.Account, L12Permission.AdminApprovalsRead))
                return ApiError(request, "command_not_found", "命令不存在", StatusCodes.Status404NotFound);
            return Results.Ok(command);
        });
        _app.MapGet("/api/admin/approvals", (HttpRequest request, string? status, int? limit) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminApprovalsRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AdminApprovals(status ?? "requested", limit ?? 200));
        });
        _app.MapPost("/api/admin/approvals/{commandId}",
            (HttpRequest request, string commandId, L12AdminApprovalDecision _) =>
        {
            var stored = _platform.AdminCommand(commandId);
            var permission = stored?.Type.StartsWith("release.", StringComparison.Ordinal) == true
                    ? L12Permission.ReleaseApprovalsReview
                    : L12Permission.AdminApprovalsReview;
            if (!TryAuthenticate(request, permission, out var authenticated, out var failure)) return failure;
            Func<L12AdminCommandView, L12AccountView, bool>? scopeValidator = permission switch
            {
                L12Permission.ReleaseApprovalsReview => L12PlatformStore.CanReviewReleaseCommand,
                _ => null,
            };
            var outcome = _adminCommands.Review(commandId, authenticated.Account,
                AuditContext(request, permission) with { CommandId = commandId }, permission, scopeValidator);
            return AdminReviewResponse(request, outcome);
        });
        _app.MapGet("/api/admin/security/status", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminSecurityRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.SecurityStatus(authenticated.Account));
        });
        _app.MapGet("/api/admin/security/audit-archives", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminSecurityRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.AuditArchiveSegments(authenticated.Account));
        });
        _app.MapPost("/api/admin/security/audit-archives",
            (HttpRequest request, AuditArchiveRequest body) =>
        {
            const L12Permission permission = L12Permission.AdminAuditArchive;
            if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
            if (!TrySecurityCommandOptions(request, authenticated.Account, permission, body.IdempotencyKey,
                    body.ExpectedVersion, out var idempotencyKey, out var expectedVersion, out failure))
                return failure;
            var payload = CaptureAuditArchiveForCommand(authenticated.Account, idempotencyKey,
                body.RetentionDays);
            var command = CommandEnvelope(request, authenticated.Account, permission, "security.audit.archive",
                "security:audit", payload, idempotencyKey, expectedVersion, body.DryRun, body.Reason);
            var outcome = _adminCommands.Execute(command, permission,
                current => ExecuteAuditArchive(current.Actor, current.Payload, current.AuditContext, true),
                current => ExecuteAuditArchive(current.Actor, current.Payload, current.AuditContext, false),
                L12AdminCommandRisk.High);
            return AdminCommandResponse(request, command, outcome);
        });
        _app.MapPost("/api/admin/security/audit-recovery-rehearsal", (HttpRequest request) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminSecurityRead, out var authenticated, out var failure))
                return failure;
            return Results.Ok(_platform.RehearseAuditArchiveRecovery(authenticated.Account));
        });
        _app.MapGet("/api/admin/audit", (HttpRequest request, string? category, string? outcome,
            string? actorId, string? commandId, string? correlationId, int? limit) =>
        {
            if (!TryAuthorize(request, L12Permission.AdminAuditRead, out _, out var failure)) return failure;
            return Results.Ok(_platform.AdminAudit(category, limit ?? 200, outcome, actorId, commandId,
                correlationId));
        });
        _app.Map("/ws", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                return;
            }
            await HandleConnectionAsync(context, context.RequestAborted);
        });
        await _app.StartAsync();
        _addresses = _app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()?.Addresses.ToArray() ?? _app.Urls.ToArray();
        _rankedClockWatchdogCancellation = new CancellationTokenSource();
        _rankedClockWatchdogTask = RunRankedClockWatchdogAsync(_rankedClockWatchdogCancellation.Token);
        _sandboxReplayMaintenanceCancellation = new CancellationTokenSource();
        _sandboxReplayMaintenanceTask = RunSandboxReplayMaintenanceAsync(
            _sandboxReplayMaintenanceCancellation.Token);
        StartSeasonActivationCoordinator();
        ScheduleNextOperationsTransition();
        ScheduleNextRulesContentTransition();
        Console.WriteLine($"HTTP: http://{host}:{port}  WebSocket: /ws");
    }

    private async Task<L12PublicDeckDetailsView?> PublicDeckDetailsWithStatisticsAsync(string id)
    {
        var published = _platform.PublishedDeck(id, null);
        if (published is null) return null;
        var details = _platform.PublicDeckDetails(published.Id);
        if (details is null) return null;
        var statistics = await _recorder.PublicDeckVersionStatisticsAsync(published.Id,
            _platform.RankedIntegrityExcludedMatchIds(), _platform.StatisticsExcludedAccountIds());
        return details with
        {
            MatchStatistics = statistics,
            MatchBindingStatus = statistics.SampleStatus,
            MatchBindingMessage = statistics.SampleStatus == "available"
                ? $"过去 {statistics.RecentDays} 天，仅展示开局时已绑定公开版本且同组至少 3 场的匿名聚合统计。"
                : statistics.SampleStatus == "insufficient"
                    ? $"样本不足：过去 {statistics.RecentDays} 天各主宰组合均不足 3 场，暂不展示胜率。"
                    : $"过去 {statistics.RecentDays} 天暂无可核验的公开版本对局统计。",
        };
    }

    private async Task HandleConnectionAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        WebSocket? socket = null;
        var lifetime = (ServerRequestLifetime)context.Items[RequestLifetimeKey]!;
        try
        {
            socket = await context.WebSockets.AcceptWebSocketAsync();
            lock (_lifecycleGate) lifetime.Socket = socket;
            if (BeforeSocketRegistrationForTests is { } beforeRegistration) await beforeRegistration();
            L12InboundConnection inbound;
            lock (_lifecycleGate)
            {
                if (_stopTask is not null)
                {
                    lifetime.TransportReady.TrySetResult();
                    socket.Abort();
                    return;
                }
                _sockets[sessionId] = socket;
                var acceptedSocket = socket;
                var snapshotCodec = new L12SnapshotWireCodec();
                _snapshotCodecs[sessionId] = snapshotCodec;
                var presenceSnapshotCodec = new L12PresenceSnapshotWireCodec();
                _presenceSnapshotCodecs[sessionId] = presenceSnapshotCodec;
                void OutboundFault(Exception error)
                {
                    Console.Error.WriteLine($"WebSocket {sessionId} 发送隔离：{error.Message}");
                    acceptedSocket.Abort();
                }
                var outbound = _transportDiagnostics is null
                    ? new L12OutboundConnection(
                        (payload, token) => SendDirectAsync(acceptedSocket, snapshotCodec, presenceSnapshotCodec,
                            payload, null, token),
                        OutboundFault, SocketSendTimeout)
                    : new L12OutboundConnection(
                        (payload, diagnostic, token) => SendDirectAsync(acceptedSocket, snapshotCodec,
                            presenceSnapshotCodec, payload, diagnostic, token),
                        OutboundFault, SocketSendTimeout);
                ConfigureChatOutbound(sessionId, acceptedSocket, outbound);
                _outboundConnections[sessionId] = outbound;
                var deploymentDrain = _rooms.DeploymentDrain;
                void InboundFault(Exception error)
                {
                    Console.Error.WriteLine($"WebSocket {sessionId} 入站隔离：{error.Message}");
                    acceptedSocket.Abort();
                }
                Func<string, L12DeploymentDrainLease?>? leaseAcquirer = deploymentDrain is null
                    ? null
                    : json => TryAcquireQueuedDeploymentLease(deploymentDrain, json);
                inbound = _transportDiagnostics is null
                    ? new L12InboundConnection(
                        json => DispatchAsync(sessionId, json, CancellationToken.None),
                        InboundFault, leaseAcquirer)
                    : new L12InboundConnection(
                        (json, timing) => DispatchAsync(sessionId, json, CancellationToken.None, timing),
                        InboundFault, leaseAcquirer);
                _inboundConnections[sessionId] = inbound;
                lifetime.Inbound = inbound;
                lifetime.Outbound = outbound;
                lifetime.TransportReady.TrySetResult();
            }
            var businessPathEstablished = false;
            var rankedNetworkFingerprint = L12RankedNetworkPrivacy.Fingerprint(
                context.Connection.RemoteIpAddress, _rankedIntegrityHmacKey);
            if (rankedNetworkFingerprint is not null)
                _socketRankedNetworkFingerprints[sessionId] = rankedNetworkFingerprint;
            var browser = _rankedDevicePrivacy.Read(context.Request.Cookies[L12RankedDevicePrivacy.CookieName]);
            if (browser is not null) _socketRankedDevices[sessionId] = browser;
            var buffer = new byte[32 * 1024];
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var message = await ReceiveTextAsync(socket, buffer, cancellationToken);
                var receiveCompletedAt = _transportDiagnostics is null ? 0 : Stopwatch.GetTimestamp();
                if (message is null) break;
                lock (_lifecycleGate)
                    if (_stopTask is not null) break;
                // The initial handshake is sequential. Once authentication/recovery has established
                // this connection, it can never fall back to concurrent direct dispatch even if a
                // revocation removes the current binding while old FIFO work is still running.
                if (!businessPathEstablished)
                {
                    await DispatchAsync(sessionId, message, cancellationToken);
                    businessPathEstablished = _establishedInboundConnections.ContainsKey(sessionId);
                    continue;
                }
                if (TryReadMessageType(message, out var messageType)
                    && messageType is "ping" or "deploymentProbe")
                {
                    if (await ValidateAuthenticatedConnectionAsync(sessionId, cancellationToken))
                    {
                        if (messageType == "ping")
                        {
                            _transportDiagnostics?.TryAcceptAuthenticatedPingReceipt(
                                sessionId, message, receiveCompletedAt);
                            await SendAsync(sessionId,
                                new { type = "pong", utc = DateTimeOffset.UtcNow }, cancellationToken);
                        }
                        else
                            await DispatchAsync(sessionId, message, cancellationToken);
                    }
                    continue;
                }

                var enqueue = inbound.TryEnqueue(message, receiveCompletedAt);
                if (enqueue == L12InboundEnqueueResult.Accepted) continue;
                if (enqueue == L12InboundEnqueueResult.Completed) break;
                if (enqueue == L12InboundEnqueueResult.DeploymentDraining)
                {
                    await SendAsync(sessionId, CreateDeploymentDrainActivePayload(message),
                        cancellationToken);
                    continue;
                }

                // The overflowing frame was received but never accepted. Drain every previously
                // accepted FIFO item before sending its explicit rejection; never fabricate an ack.
                var rejection = CreateInboundOverloadPayload(message, enqueue);
                message = null; // Do not retain an unaccepted maximum-size frame while FIFO drains.
                await inbound.CompleteAsync(drain: true);
                await RejectInboundOverloadAsync(sessionId, rejection, cancellationToken);
                if (socket.State == WebSocketState.Open)
                    await socket.CloseOutputAsync((WebSocketCloseStatus)1013,
                        "inbound queue capacity exceeded", cancellationToken);
                break;
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException exception) { Console.WriteLine($"WebSocket {sessionId}: {exception.Message}"); }
        finally
        {
            CloseChatStartsForTransport(sessionId);
            _sockets.TryRemove(sessionId, out _);
            _establishedInboundConnections.TryRemove(sessionId, out _);
            if (_inboundConnections.TryRemove(sessionId, out var inbound))
            {
                inbound.StopAcceptingAndCancelPending();
                await inbound.CompleteAsync(drain: false);
            }
            string? disconnectedAccountId = null;
            if (_socketPlatformSessions.TryRemove(sessionId, out var binding)
                && TryReleaseActiveAccountSocket(_activeAccountSockets, binding.AccountId, sessionId))
            {
                disconnectedAccountId = binding.AccountId;
            }
            await SendManyAsync(DisconnectForTransportClose(sessionId), CancellationToken.None);
            if (disconnectedAccountId is not null) NotifyPresenceChanged();
            _socketRankedNetworkFingerprints.TryRemove(sessionId, out _);
            _socketRankedDevices.TryRemove(sessionId, out _);
            _socketCapabilities.TryRemove(sessionId, out _);
            _snapshotCodecs.TryRemove(sessionId, out _);
            _presenceSnapshotCodecs.TryRemove(sessionId, out _);
            if (_outboundConnections.TryRemove(sessionId, out var outbound))
                await outbound.DisposeAsync();
            await DisposeRequestSocketAsync(lifetime);
        }
    }

    private async Task DispatchAsync(Guid sessionId, string json, CancellationToken cancellationToken,
        L12InboundQueueTiming inboundTiming = default)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            await SendAsync(sessionId, new { type = "error", message = "消息不是有效 JSON" }, cancellationToken);
            return;
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
            {
                await SendAsync(sessionId,
                    new { type = "error", message = "消息缺少有效的 type" }, cancellationToken);
                return;
            }
            var messageType = typeElement.GetString();
            if (messageType is not ("hello" or "deploymentProbe"))
            {
                // This runs at dequeue, so revocation and connection-generation changes after
                // receipt still fence the queued command before it reaches any room mutation.
                if (!await ValidateAuthenticatedConnectionAsync(sessionId, cancellationToken)) return;
            }
            L12SyntheticTransportSample? transportDiagnostic = null;
            if (_transportDiagnostics is not null
                && messageType == "gameAction"
                && root.TryGetProperty("command", out var diagnosticCommand)
                && diagnosticCommand.ValueKind == JsonValueKind.Object
                && diagnosticCommand.TryGetProperty("type", out var diagnosticCommandType)
                && diagnosticCommandType.ValueKind == JsonValueKind.String
                && _transportDiagnostics.AcceptsCommand(diagnosticCommandType.GetString()))
                transportDiagnostic = _transportDiagnostics.TryBegin(
                    GetString(root, "requestId"), inboundTiming, sessionId);
            try
            {
                IReadOnlyList<OutgoingMessage> outgoing = messageType switch
                {
                    "hello" => await AuthenticateSessionAsync(sessionId, root),
                    "createRoom" => CreateRoom(sessionId, root),
                    "updateRoomOptions" => UpdateRoomOptions(sessionId, root),
                    "createSandbox" => await CreateSandboxAsync(sessionId, root),
                    "createTutorial" => await CreateTutorialAsync(sessionId, root),
                    "joinMatchmaking" => await JoinMatchmakingAsync(sessionId, root),
                    "pollMatchmaking" => await _rooms.PollMatchmakingAsync(sessionId),
                    "syncState" => await SynchronizeStateAsync(sessionId, root),
                    "syncPresence" => SyncPresence(sessionId),
                    "cancelMatchmaking" => _rooms.CancelMatchmaking(sessionId),
                    "joinRoom" => _rooms.JoinRoom(sessionId, GetString(root, "roomCode")),
                    "enterTournamentMatch" => await _rooms.EnterTournamentMatchAsync(sessionId,
                        GetString(root, "tournamentId"), GetString(root, "matchId")),
                    "inviteFriend" => _rooms.InviteFriend(sessionId, GetString(root, "accountId")),
                    "cancelFriendInvitation" => _rooms.CancelFriendInvitation(sessionId, GetString(root, "invitationId")),
                    "resolveFriendInvitation" => _rooms.ResolveFriendInvitation(sessionId,
                        GetString(root, "invitationId"), GetBool(root, "accept", false)),
                    "spectateRoom" => _rooms.SpectateRoom(sessionId, GetString(root, "roomCode")),
                    "spectateTournamentMatch" => _rooms.SpectateTournamentMatch(sessionId,
                        GetString(root, "tournamentId"), GetString(root, "matchId")),
                    "leaveRoom" => _rooms.LeaveRoom(sessionId),
                    "selectDeck" => _rooms.SelectDeck(sessionId, GetInt(root, "deckIndex")),
                    "selectCustomDeck" when root.TryGetProperty("deck", out var deckElement)
                        => SelectCustomDeck(sessionId, deckElement),
                    "ready" => await _rooms.SetReadyAsync(sessionId, GetBool(root, "ready", true)),
                    "gameAction" when root.TryGetProperty("command", out var command)
                        => await _rooms.HandleActionAsync(sessionId, command, GetString(root, "requestId"),
                            transportDiagnostic),
                    "getResponsePreference" => await _rooms.GetResponsePreferenceAsync(sessionId),
                    "setResponsePreference" => await _rooms.SetResponsePreferenceAsync(sessionId,
                        GetString(root, "mode"), GetString(root, "requestId")),
                    "requestMatchDraw" => await _rooms.RequestMatchDrawAsync(sessionId,
                        GetString(root, "requestId"), GetString(root, "reason")),
                    "resolveMatchDraw" => await _rooms.ResolveMatchDrawAsync(sessionId,
                        GetString(root, "requestId"), GetBool(root, "accept", false)),
                    "reportOpponent" => await _rooms.ReportOpponentAsync(sessionId,
                        GetString(root, "reportId"), GetString(root, "description")),
                    "sandboxAction" when root.TryGetProperty("command", out var sandboxCommand)
                        => await _rooms.HandleSandboxActionAsync(sessionId,
                            GetInt(root, "actingPlayerIndex", -1), sandboxCommand,
                            GetString(root, "requestId")),
                    "gmAction" when root.TryGetProperty("command", out var gmCommand)
                        => await _rooms.HandleGmActionAsync(sessionId, gmCommand,
                            GetString(root, "requestId")),
                    "chatSend" => QueueChatSend(sessionId, root),
                    "chatSync" => QueueChatSync(sessionId, root),
                    "chatRead" => QueueChatRead(sessionId, root),
                    "getEffectiveOperationsPolicy" => [EffectiveOperationsPolicyMessage(sessionId)],
                    "ping" => [new OutgoingMessage(sessionId, new { type = "pong", utc = DateTimeOffset.UtcNow })],
                    "deploymentProbe" => [new OutgoingMessage(sessionId, new
                    {
                        type = "deploymentProbe",
                        service = "twelve-legions",
                        protocolVersion = 1,
                        authentication = "token",
                    })],
                    _ => [new OutgoingMessage(sessionId, new { type = "error", message = "未知消息类型" })],
                };
                await SendManyAsync(outgoing, cancellationToken, transportDiagnostic, sessionId);
                if (messageType is "hello" or "createRoom" or "createSandbox" or "createTutorial" or "joinMatchmaking"
                    or "cancelMatchmaking" or "joinRoom" or "enterTournamentMatch" or "spectateRoom"
                    or "spectateTournamentMatch" or "leaveRoom" or "ready" or "resolveFriendInvitation")
                    NotifyPresenceChanged();
            }
            catch (L12DeploymentBarrierClosedException)
            {
                await SendManyAsync(DeploymentDrainActive(sessionId, root), cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                var correlationId = $"ws-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..34];
                var commandType = root.TryGetProperty("command", out var failedCommand)
                    && failedCommand.TryGetProperty("type", out var failedCommandType)
                    ? failedCommandType.GetString() : null;
                Console.Error.WriteLine($"[{correlationId}] WebSocket command failed: session={sessionId}, message={messageType}, command={commandType ?? "-"}\n{error}");
                await SendAsync(sessionId, new
                {
                    type = "error",
                    code = "commandFailed",
                    message = "本次操作处理失败，已保留连接并重新同步对局状态",
                    correlationId,
                    requestId = GetString(root, "requestId"),
                }, cancellationToken);
                try
                {
                    await SendManyAsync(await _rooms.RecoveryStateAsync(sessionId), cancellationToken);
                }
                catch (Exception recoveryError) when (recoveryError is not OperationCanceledException)
                {
                    Console.Error.WriteLine($"[{correlationId}] WebSocket recovery snapshot failed: {recoveryError}");
                }
            }
        }
    }

    private async Task<bool> ValidateAuthenticatedConnectionAsync(Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (!_socketPlatformSessions.TryGetValue(sessionId, out var binding))
        {
            await SendAsync(sessionId, new
            {
                type = "authenticationRequired", reason = "authentication-required",
                message = "请先登录账号",
            }, cancellationToken);
            return false;
        }

        while (!_platform.IsSessionActive(binding.PlatformSessionId))
        {
            L12OutboundConnection? terminalOutbound = null;
            WebSocket? terminalSocket = null;
            Task<bool>? terminalDelivery = null;
            CancellationTokenSource? terminalTimeout = null;
            var ownerReleased = false;
            var retired = false;
            await _socketClaimGate.WaitAsync(cancellationToken);
            try
            {
                if (!_socketPlatformSessions.TryGetValue(sessionId, out var currentBinding))
                    return false;
                if (currentBinding != binding)
                {
                    binding = currentBinding;
                    continue;
                }
                // A concurrent committed activity may have refreshed the same session while this
                // validator waited for a hello claim to finish. Recheck inside the claim fence.
                if (_platform.IsSessionActive(currentBinding.PlatformSessionId))
                {
                    binding = currentBinding;
                    break;
                }
                CloseChatStartsForTransport(sessionId);
                if (!TryReleaseSocketPlatformBinding(_socketPlatformSessions, sessionId,
                        currentBinding))
                    return false;

                retired = true;
                _rooms.RecordConnectionClaimRejection(currentBinding.AccountId,
                    "platform-session-revoked");
                ownerReleased = TryReleaseActiveAccountSocket(_activeAccountSockets,
                    currentBinding.AccountId, sessionId);
                _establishedInboundConnections.TryRemove(sessionId, out _);
                if (_inboundConnections.TryGetValue(sessionId, out var inbound))
                    inbound.StopAcceptingAndCancelPending();
                if (_sockets.TryGetValue(sessionId, out var socket)
                    && _sockets.TryRemove(new KeyValuePair<Guid, WebSocket>(sessionId, socket)))
                    terminalSocket = socket;
                if (_outboundConnections.TryGetValue(sessionId, out var outbound)
                    && _outboundConnections.TryRemove(
                        new KeyValuePair<Guid, L12OutboundConnection>(sessionId, outbound)))
                {
                    terminalOutbound = outbound;
                    var payload = new
                    {
                        type = "authenticationRequired", reason = "platform-session-revoked",
                        message = "登录会话已撤销",
                    };
                    var serializationStartedAt = L12PerformanceMetrics.Start();
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, OutgoingJsonOptions);
                    L12PerformanceMetrics.Duration("websocket.serialize", serializationStartedAt);
                    terminalTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                    terminalTimeout.CancelAfter(SocketSendTimeout);
                    // The async method establishes its queue fence synchronously before returning
                    // this task, while the transport is still inside the claim fence.
                    terminalDelivery = outbound.EnqueueTerminalAndCompleteAsync(
                        new L12QueuedPayload(bytes, false, false), terminalTimeout.Token);
                }
            }
            finally { _socketClaimGate.Release(); }

            if (retired)
            {
                await RetireInactiveTransportAsync(sessionId, terminalOutbound, terminalSocket,
                    terminalDelivery, terminalTimeout, ownerReleased, cancellationToken);
                return false;
            }
        }
        if (_activeAccountSockets.TryGetValue(binding.AccountId, out var currentSocket)
            && currentSocket == sessionId
            && _rooms.IsCurrentConnection(sessionId, binding.AccountId, binding.ConnectionGeneration))
            return true;

        await SupersedeSocketAsync(sessionId, binding.AccountId, cancellationToken);
        return false;
    }

    private async Task RetireInactiveTransportAsync(Guid sessionId, L12OutboundConnection? outbound,
        WebSocket? socket, Task<bool>? terminalDelivery,
        CancellationTokenSource? terminalTimeout, bool ownerReleased,
        CancellationToken cancellationToken)
    {
        var terminalDelivered = false;
        try
        {
            await SendManyAsync(DisconnectForTransportClose(sessionId), CancellationToken.None);
            if (ownerReleased) NotifyPresenceChanged();
            if (terminalDelivery is not null) terminalDelivered = await terminalDelivery;

            if (!terminalDelivered || socket is null || socket.State != WebSocketState.Open)
            {
                socket?.Abort();
                return;
            }
            using var closeTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            closeTimeout.CancelAfter(SocketSendTimeout);
            await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation,
                "platform session inactive", closeTimeout.Token);
        }
        catch (Exception error)
        {
            socket?.Abort();
            Console.Error.WriteLine($"WebSocket {sessionId} 失效连接收尾隔离：{error.Message}");
        }
        finally
        {
            if (terminalDelivery is not null)
            {
                try { await terminalDelivery; }
                catch { }
            }
            terminalTimeout?.Dispose();
            if (outbound is not null)
            {
                try { await outbound.DisposeAsync(); }
                catch (Exception error)
                {
                    socket?.Abort();
                    Console.Error.WriteLine($"WebSocket {sessionId} 终止发送队列释放失败：{error.Message}");
                }
            }
        }
    }

    private static bool TryReadMessageType(string json, out string? messageType)
    {
        messageType = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String) return false;
            messageType = type.GetString();
            return messageType is not null;
        }
        catch (JsonException) { return false; }
    }

    private static object CreateInboundOverloadPayload(string json,
        L12InboundEnqueueResult enqueue)
    {
        string? requestId = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var candidate = document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("requestId", out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString() : null;
            // An overload response must not keep a hostile megabyte-sized requestId alive.
            if (candidate is { Length: <= 128 }) requestId = candidate;
        }
        catch (JsonException) { }
        return new
        {
            type = "error",
            code = "inboundQueueCapacityExceeded",
            reason = enqueue == L12InboundEnqueueResult.MessageLimit
                ? "message-limit" : "retained-byte-limit",
            message = "待处理请求过多，请重新连接后确认操作结果",
            requestId,
            retryWithSameRequestId = requestId is not null,
        };
    }

    private async Task RejectInboundOverloadAsync(Guid sessionId, object payload,
        CancellationToken cancellationToken)
    {
        if (!_outboundConnections.TryGetValue(sessionId, out var outbound)) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SocketSendTimeout);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, OutgoingJsonOptions);
        if (!await outbound.EnqueueAndWaitAsync(new L12QueuedPayload(bytes, false, false), timeout.Token)
            && _sockets.TryGetValue(sessionId, out var socket)) socket.Abort();
    }

    private async Task<IReadOnlyList<OutgoingMessage>> AuthenticateSessionAsync(Guid sessionId, JsonElement root)
    {
        if (_socketPlatformSessions.TryGetValue(sessionId, out var currentBinding))
        {
            _rooms.RecordConnectionClaimRejection(currentBinding.AccountId, "duplicate-hello");
            return [new OutgoingMessage(sessionId, new
            {
                type = "connectionRejected", reason = "duplicate-hello", message = "连接已经完成身份认证",
            })];
        }
        var authenticated = _platform.AuthenticateTokenSession(GetString(root, "authToken"));
        if (authenticated is null)
            return [new OutgoingMessage(sessionId, new
            {
                type = "authenticationRequired", reason = "authentication-required", message = "请先登录账号",
            })];
        if (authenticated.Account.MustChangePassword)
        {
            _rooms.RecordConnectionClaimRejection(authenticated.Account.Id, "password-change-required");
            return [new OutgoingMessage(sessionId, new
            {
                type = "passwordChangeRequired", reason = "password-change-required",
                message = "必须先修改临时密码",
            })];
        }
        if (authenticated.Account.MustChangeUsername)
        {
            _rooms.RecordConnectionClaimRejection(authenticated.Account.Id, "username-change-required");
            return [new OutgoingMessage(sessionId, new
            {
                type = "usernameChangeRequired", reason = "username-change-required",
                message = "必须先修改不合规的用户名",
            })];
        }

        if (!TryAcquireHelloDeploymentGuard(out var deploymentGuard))
            return DeploymentDrainActive(sessionId, root);
        using var deployment = deploymentGuard;
        try
        {
            L12SessionClaimResult claim;
            var capabilities = ReadProtocolCapabilities(root);
            Guid? previousSocket;
            IReadOnlyList<OutgoingMessage> recovery;
            await _socketClaimGate.WaitAsync();
            try
            {
                // Natural expiry permanently retires this transport before it releases the claim
                // fence. A hello already read from that transport must not reclaim the same Guid.
                if (!_sockets.ContainsKey(sessionId)
                    || !_outboundConnections.ContainsKey(sessionId))
                {
                    _rooms.RecordConnectionClaimRejection(authenticated.Account.Id,
                        "platform-session-revoked");
                    return [new OutgoingMessage(sessionId, new
                    {
                        type = "authenticationRequired", reason = "platform-session-revoked",
                        message = "当前连接已失效，请重新连接",
                    })];
                }
                _socketCapabilities[sessionId] = capabilities;
                if (_snapshotCodecs.TryGetValue(sessionId, out var snapshotCodec))
                {
                    snapshotCodec.SetDeltaEnabled(capabilities.DeltaGameState, capabilities.EventWindowDeltaV1,
                        capabilities.HandArrayDeltaV1);
                    snapshotCodec.SetCardSnapshotFrameEnabled(capabilities.GameStateCardFrameV1);
                    snapshotCodec.SetPropertyFrameEnabled(capabilities.GameStatePropertyFrameV1);
                }
                if (_presenceSnapshotCodecs.TryGetValue(sessionId, out var presenceSnapshotCodec))
                    presenceSnapshotCodec.SetDeltaEnabled(capabilities.PresenceDeltaV1,
                        capabilities.PresenceOrderReuseV1);
                previousSocket = _activeAccountSockets.GetValueOrDefault(authenticated.Account.Id);
                // ConnectAsync advances the room connection generation before it returns. Fence
                // the old transport first; a failed/unknown claim never reopens that old lane.
                if (previousSocket is { } previousId && previousId != Guid.Empty && previousId != sessionId)
                    CloseChatStartsForTransport(previousId);
                claim = await _rooms.ConnectAsync(sessionId, authenticated.Account.Id,
                    authenticated.Account.Username,
                    _socketRankedNetworkFingerprints.GetValueOrDefault(sessionId),
                    _socketRankedDevices.GetValueOrDefault(sessionId));
                _socketPlatformSessions[sessionId] = new SocketPlatformBinding(authenticated.SessionId,
                    authenticated.Account.Id, claim.ConnectionGeneration);
                _activeAccountSockets[authenticated.Account.Id] = sessionId;
                recovery = await _rooms.RecoveryStateWithAckAsync(sessionId, claim.Recovered);
                _establishedInboundConnections[sessionId] = 0;
            }
            finally { _socketClaimGate.Release(); }

            var replaced = claim.ReplacedSessionId ?? previousSocket;
            if (replaced is { } oldSessionId && oldSessionId != sessionId)
                await SupersedeSocketAsync(oldSessionId, authenticated.Account.Id, CancellationToken.None);

            var session = new OutgoingMessage(sessionId, new
            {
                type = "session", sessionId, accountId = authenticated.Account.Id, name = claim.Name,
                claim.Recovered, claim.RoomCode,
                claim.ConnectionGeneration, claim.ClaimDecision, claim.PreviousConnectionGeneration,
                claim.RecoveryRevision,
                protocolVersion = 2,
                tutorialAvailable = _rooms.TutorialAvailable,
                chatServerUtcMs = _chatEnabled && capabilities.ChatV1
                    ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : (long?)null,
                capabilities = new
                {
                    requestIds = capabilities.RequestIds,
                    deltaGameState = capabilities.DeltaGameState,
                    eventWindowDeltaV1 = capabilities.DeltaGameState && capabilities.EventWindowDeltaV1,
                    handArrayDeltaV1 = capabilities.DeltaGameState && capabilities.HandArrayDeltaV1,
                    presenceDeltaV1 = capabilities.PresenceDeltaV1,
                    presenceOrderReuseV1 = capabilities.PresenceDeltaV1 && capabilities.PresenceOrderReuseV1,
                    chatV1 = _chatEnabled && capabilities.ChatV1,
                    gameStateCardFrameV1 = capabilities.GameStateCardFrameV1,
                    gameStatePropertyFrameV1 = capabilities.GameStatePropertyFrameV1,
                },
            });
            var presenceRevision = Interlocked.Read(ref _presenceResourceRevision);
            PresenceHelloRevisionCapturedForTests?.Invoke(presenceRevision);
            return new[]
            {
                session,
                EffectiveOperationsPolicyMessage(sessionId),
                new OutgoingMessage(sessionId, ResourceVersionsPayload(authenticated.Account.Id, presenceRevision)),
                PresenceSnapshotMessage(sessionId, authenticated.Account.Id, presenceRevision)
                    with { ForceFullPresence = true },
            }.Concat(recovery).ToArray();
        }
        catch (L12DeploymentBarrierClosedException)
        {
            CleanupRejectedHello(sessionId, authenticated.Account.Id);
            return DeploymentDrainActive(sessionId, root);
        }
    }

    private async Task SupersedeSocketAsync(Guid sessionId, string accountId,
        CancellationToken cancellationToken)
    {
        CloseChatStartsForTransport(sessionId);
        if (_inboundConnections.TryGetValue(sessionId, out var inbound))
            inbound.StopAcceptingAndCancelPending();
        var payload = new
        {
            type = "sessionSuperseded", reason = "newer-connection-generation",
            message = "此账号已由更新的连接接管",
        };
        if (_outboundConnections.TryGetValue(sessionId, out var outbound))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SocketSendTimeout);
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, OutgoingJsonOptions);
                await outbound.EnqueueAndWaitAsync(new L12QueuedPayload(bytes, false, false), timeout.Token);
            }
            catch (Exception error) when (error is OperationCanceledException or WebSocketException
                or ObjectDisposedException)
            {
                // 旧连接已被逻辑栅栏隔离；通知发送失败不影响新连接接管。
            }
        }
        _rooms.RecordConnectionClaimRejection(accountId, "older-connection-fenced");
        if (!_sockets.TryGetValue(sessionId, out var socket) || socket.State != WebSocketState.Open) return;
        try
        {
            await socket.CloseOutputAsync((WebSocketCloseStatus)4002, "connection superseded",
                cancellationToken);
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException
            or ObjectDisposedException)
        {
            socket.Abort();
        }
    }

    private OutgoingMessage EffectiveOperationsPolicyMessage(Guid sessionId)
        => new(sessionId, new
        {
            type = "effectiveOperationsPolicy",
            resource = OperationsPolicyResource,
            epoch = _resourceEpoch,
            revision = Interlocked.Read(ref _operationsResourceRevision),
            policy = _platform.EffectiveOperationsPolicy(),
        });

    private static int GetInt(JsonElement root, string propertyName, int fallback = 0)
        => root.TryGetProperty(propertyName, out var element) && element.TryGetInt32(out var value) ? value : fallback;

    private static ProtocolCapabilities ReadProtocolCapabilities(JsonElement root)
    {
        if (!root.TryGetProperty("capabilities", out var value)) return new(false, false);
        if (value.ValueKind == JsonValueKind.Array)
        {
            var requested = value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new(requested.Contains("requestIds"), requested.Contains("deltaGameState"),
                requested.Contains("eventWindowDeltaV1"), requested.Contains("presenceDeltaV1"),
                requested.Contains("chatV1"), requested.Contains(L12CardSnapshotFrame.Capability),
                requested.Contains(L12HandArrayDelta.Capability),
                requested.Contains(L12PresenceSnapshotWireCodec.OrderReuseCapability),
                requested.Contains(L12PropertySnapshotFrame.Capability));
        }
        if (value.ValueKind != JsonValueKind.Object) return new(false, false);
        return new(GetBool(value, "requestIds", false), GetBool(value, "deltaGameState", false),
            GetBool(value, "eventWindowDeltaV1", false), GetBool(value, "presenceDeltaV1", false),
            GetBool(value, "chatV1", false), GetBool(value, L12CardSnapshotFrame.Capability, false),
            GetBool(value, L12HandArrayDelta.Capability, false),
            GetBool(value, L12PresenceSnapshotWireCodec.OrderReuseCapability, false),
            GetBool(value, L12PropertySnapshotFrame.Capability, false));
    }

    private async Task<IReadOnlyList<OutgoingMessage>> SynchronizeStateAsync(Guid sessionId, JsonElement root)
    {
        if (GetBool(root, "plainFull", false))
        {
            // A decoder fault permanently retires this optional encoding for the current transport.
            // Already queued game states are still original full bytes and will therefore also use
            // the plain path when the per-connection sender prepares them.
            if (_socketCapabilities.TryGetValue(sessionId, out var capabilities))
                _socketCapabilities.TryUpdate(sessionId,
                    capabilities with { GameStateCardFrameV1 = false, GameStatePropertyFrameV1 = false }, capabilities);
            if (_snapshotCodecs.TryGetValue(sessionId, out var snapshotCodec))
            {
                snapshotCodec.SetCardSnapshotFrameEnabled(false);
                snapshotCodec.SetPropertyFrameEnabled(false);
            }
        }
        return await _rooms.RecoveryStateWithAckAsync(sessionId, recovered: true);
    }

    private IReadOnlyList<OutgoingMessage> SelectCustomDeck(Guid sessionId, JsonElement deckElement)
    {
        try
        {
            var deck = deckElement.Deserialize<L12CustomDeckSubmission>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return deck is null
                ? [new OutgoingMessage(sessionId, new { type = "deckRejected", message = "牌库数据为空" })]
                : _rooms.SelectCustomDeck(sessionId, deck);
        }
        catch (JsonException)
        {
            return [new OutgoingMessage(sessionId, new { type = "deckRejected", message = "牌库数据格式错误" })];
        }
    }

    private IReadOnlyList<OutgoingMessage> CreateRoom(Guid sessionId, JsonElement root)
    {
        if (!root.TryGetProperty("options", out var optionsElement)) return _rooms.CreateRoom(sessionId);
        try
        {
            var options = optionsElement.Deserialize<L12RoomOptions>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return _rooms.CreateRoom(sessionId, options);
        }
        catch (JsonException)
        {
            return [new OutgoingMessage(sessionId, new { type = "error", message = "房间设置格式错误" })];
        }
    }

    private async Task<IReadOnlyList<OutgoingMessage>> CreateSandboxAsync(Guid sessionId, JsonElement root)
    {
        try
        {
            var request = root.TryGetProperty("request", out var requestElement)
                ? requestElement.Deserialize<L12SandboxRequest>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : new L12SandboxRequest();
            return await _rooms.CreateSandboxAsync(sessionId, request);
        }
        catch (JsonException)
        {
            return [new OutgoingMessage(sessionId, new { type = "error", message = "沙盒设置格式错误" })];
        }
    }

    private Task SendManyAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken,
        L12SyntheticTransportSample? transportDiagnostic = null, Guid diagnosticSessionId = default)
    {
        var serializedByPayload = new Dictionary<object, byte[]>(ReferenceEqualityComparer.Instance);
        var diagnosticAssigned = false;
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_outboundConnections.TryGetValue(message.SessionId, out var outbound)) continue;
            var messageDiagnostic = transportDiagnostic is { IsRecipientsMode: true }
                ? transportDiagnostic.TakeRecipient(message.SessionId, message.IsGameState)
                : !diagnosticAssigned && transportDiagnostic is not null
                                    && message.SessionId == diagnosticSessionId
                ? transportDiagnostic : null;
            if (messageDiagnostic is not null) diagnosticAssigned = true;
            if (!serializedByPayload.TryGetValue(message.Payload, out var bytes))
            {
                var serializationStartedAt = L12PerformanceMetrics.Start();
                bytes = message.IsGameState || message.IsPresenceSnapshot
                    ? L12SnapshotJson.SerializeToUtf8Bytes(message.Payload)
                    : JsonSerializer.SerializeToUtf8Bytes(message.Payload, OutgoingJsonOptions);
                var serializationCompletedAt = messageDiagnostic is null ? 0 : Stopwatch.GetTimestamp();
                L12PerformanceMetrics.Duration("websocket.serialize", serializationStartedAt);
                messageDiagnostic?.Serialization(serializationStartedAt, serializationCompletedAt);
                serializedByPayload[message.Payload] = bytes;
                L12PerformanceMetrics.Bytes("websocket.full-payload", bytes.Length);
            }
            var queued = new L12QueuedPayload(bytes, message.IsGameState, message.ForceFullGameState,
                message.IsPresenceSnapshot, message.ForceFullPresence);
            if (outbound.TryEnqueue(queued, message.ReplaceableGameState, messageDiagnostic))
            {
                L12PerformanceMetrics.Value("websocket.queue-depth", outbound.QueuedCount);
                continue;
            }
            if (message.ReplaceableGameState) continue;
            // 队列中全是不可丢关键消息时，隔离此慢连接，绝不反压房间动作链。
            if (_sockets.TryGetValue(message.SessionId, out var slowSocket)) slowSocket.Abort();
        }
        if (transportDiagnostic is { IsRecipientsMode: true })
            transportDiagnostic.FinishRecipients();
        else if (transportDiagnostic is not null && !diagnosticAssigned)
            transportDiagnostic.MissingReply();
        return Task.CompletedTask;
    }

    private Task SendAsync(Guid sessionId, object payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_outboundConnections.TryGetValue(sessionId, out var outbound)) return Task.CompletedTask;
        var serializationStartedAt = L12PerformanceMetrics.Start();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, OutgoingJsonOptions);
        L12PerformanceMetrics.Duration("websocket.serialize", serializationStartedAt);
        if (!outbound.TryEnqueue(new L12QueuedPayload(bytes, false, false))
            && _sockets.TryGetValue(sessionId, out var slowSocket))
            slowSocket.Abort();
        return Task.CompletedTask;
    }

    private void CloseChatStartsForTransport(Guid sessionId)
    {
        if (_outboundConnections.TryGetValue(sessionId, out var outbound))
            outbound.CloseChatStarts();
    }

    // Only the three direct top-level HTTP mutations above use this optional boundary.
    // A null registry preserves the disabled path; these are not general transaction receipts.
    private bool TryBeginChatRelationshipMutation(string firstId, string secondId,
        out L12ChatPairRegistry.MutationLease? mutation)
    {
        mutation = null;
        var registry = Volatile.Read(ref _chatMutationRegistry);
        if (registry is null || string.IsNullOrWhiteSpace(firstId) || string.IsNullOrWhiteSpace(secondId)
            || firstId == secondId) return true; // No valid chat pair can exist for these IDs.
        // Capacity failure returns 503 before authority; never bypass the fence or Abort a socket.
        mutation = registry.TryBeginMutation(firstId, secondId);
        return mutation is not null;
    }

    private void FinishChatRelationshipMutation(L12ChatPairRegistry.MutationLease? mutation,
        string firstId, string secondId)
    {
        if (mutation is null) return;
        // An exceptional authority outcome stays Unknown. No catch/finally infers rollback from
        // a visible relationship, a storage exception, or an inner Save return.
        mutation.Dispose();
        // Pair gates are released before these bounded owner lookups and sender wake hints.
        WakeChatRelationshipAccount(firstId);
        if (secondId != firstId) WakeChatRelationshipAccount(secondId);
    }

    private void WakeChatRelationshipAccount(string accountId)
    {
        if (_activeAccountSockets.TryGetValue(accountId, out var sessionId)
            && _outboundConnections.TryGetValue(sessionId, out var outbound))
            outbound.WakeChatLane();
    }

    private static async Task SendDirectAsync(WebSocket socket, L12SnapshotWireCodec snapshotCodec,
        L12PresenceSnapshotWireCodec presenceSnapshotCodec, object payload,
        L12SyntheticTransportSample? transportDiagnostic,
        CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            transportDiagnostic?.SendFault();
            return;
        }
        var prepareStartedAt = transportDiagnostic is null ? 0 : Stopwatch.GetTimestamp();
        var queued = (L12QueuedPayload)payload;
        var prepared = queued.IsPresenceSnapshot
            ? presenceSnapshotCodec.Prepare(queued)
            : snapshotCodec.Prepare(queued);
        transportDiagnostic?.Prepared(prepareStartedAt, Stopwatch.GetTimestamp(), prepared.Payload.Length,
            prepared.IsDelta);
        L12PerformanceMetrics.Bytes(prepared.IsDelta
            ? "websocket.delta-payload" : "websocket.sent-payload", prepared.Payload.Length);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SocketSendTimeout);
        var sendStartedAt = L12PerformanceMetrics.Start();
        await StartPreparedTextSend(socket, prepared.Payload.AsMemory(), timeout.Token);
        var sendCompletedAt = transportDiagnostic is null ? 0 : Stopwatch.GetTimestamp();
        L12PerformanceMetrics.Duration("websocket.send", sendStartedAt);
        transportDiagnostic?.Sent(sendStartedAt, sendCompletedAt);
        prepared.Commit();
        transportDiagnostic?.Committed(Stopwatch.GetTimestamp());
    }

    internal static ValueTask StartPreparedTextSend(WebSocket socket, ReadOnlyMemory<byte> preparedPayload,
        CancellationToken cancellationToken)
        => socket.SendAsync(preparedPayload, WebSocketMessageType.Text, true, cancellationToken);

    private async Task RunRankedClockWatchdogAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_rankedClockWatchdogInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await SendManyAsync(await _rooms.TickRankedClocksAsync(), cancellationToken);
                    await SendManyAsync(await _rooms.TickResponseWindowsAsync(), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Ranked clock watchdog: {error.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task StopRankedClockWatchdogAsync()
    {
        var cancellation = _rankedClockWatchdogCancellation;
        var task = _rankedClockWatchdogTask;
        _rankedClockWatchdogCancellation = null;
        _rankedClockWatchdogTask = null;
        if (cancellation is null) return;
        cancellation.Cancel();
        if (task is not null)
        {
            try { await task; }
            catch (OperationCanceledException) { }
        }
        cancellation.Dispose();
    }

    private async Task RunSandboxReplayMaintenanceAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_sandboxReplayMaintenanceInterval);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _rooms.RunSandboxReplayMaintenanceAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    // 租约会在失败后释放，下一个独立维护周期重试；不占用排位时钟循环。
                    Console.Error.WriteLine($"Sandbox replay maintenance: {error.Message}");
                }
                if (!await timer.WaitForNextTickAsync(cancellationToken)) break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task StopSandboxReplayMaintenanceAsync()
    {
        var cancellation = _sandboxReplayMaintenanceCancellation;
        var task = _sandboxReplayMaintenanceTask;
        _sandboxReplayMaintenanceCancellation = null;
        _sandboxReplayMaintenanceTask = null;
        if (cancellation is null) return;
        cancellation.Cancel();
        if (task is not null)
        {
            try { await task; }
            catch (OperationCanceledException) { }
        }
        cancellation.Dispose();
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) continue;
            stream.Write(buffer, 0, result.Count);
            if (stream.Length > 1024 * 1024) throw new InvalidDataException("消息过大");
            if (result.EndOfMessage) return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static bool GetBool(JsonElement root, string name, bool fallback)
        => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : fallback;

    private static bool IsAllowedOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return false;
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Host is "localhost" or "127.0.0.1" or "::1") return true;
        var configured = Environment.GetEnvironmentVariable("L12_ALLOWED_ORIGINS") ?? string.Empty;
        return configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    private bool TryAuthenticate(HttpRequest request, L12Permission permission,
        out L12AuthenticatedSession authenticated, out IResult failure)
    {
        var current = _platform.AuthenticateSession(request.Headers.Authorization);
        if (current is not null)
        {
            authenticated = current;
            failure = Results.Empty;
            return true;
        }

        var context = AuditContext(request, permission) with { Outcome = "denied", Reason = "authentication-required" };
        _platform.RecordAuthorizationDenied(null, context, L12Authorization.Key(permission), "authentication-required");
        authenticated = null!;
        failure = ApiError(request, "authentication_required", "请先登录账号", StatusCodes.Status401Unauthorized);
        return false;
    }

    private async Task<IReadOnlyList<OutgoingMessage>> JoinMatchmakingAsync(Guid sessionId, JsonElement root)
    {
        try
        {
            var deck = root.TryGetProperty("deck", out var deckElement)
                ? deckElement.Deserialize<L12CustomDeckSubmission>(CommandJsonOptions) : null;
            return await _rooms.JoinMatchmakingAsync(sessionId, GetString(root, "mode"), deck);
        }
        catch (JsonException)
        {
            return [new OutgoingMessage(sessionId, new { type = "matchmakingRejected", message = "匹配牌库格式错误" })];
        }
    }

    private IReadOnlyList<OutgoingMessage> UpdateRoomOptions(Guid sessionId, JsonElement root)
    {
        if (!root.TryGetProperty("options", out var optionsElement))
            return [new OutgoingMessage(sessionId, new { type = "error", message = "缺少房间设置" })];
        try
        {
            var options = optionsElement.Deserialize<L12RoomOptions>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return _rooms.UpdateRoomOptions(sessionId, options);
        }
        catch (JsonException)
        {
            return [new OutgoingMessage(sessionId, new { type = "error", message = "房间设置格式错误" })];
        }
    }

    private bool TryAuthorize(HttpRequest request, L12Permission permission,
        out L12AuthenticatedSession authenticated, out IResult failure)
    {
        if (!TryAuthenticate(request, permission, out authenticated, out failure)) return false;
        if (L12Authorization.HasPermission(authenticated.Account, permission)) return true;

        var context = AuditContext(request, permission) with { Outcome = "denied", Reason = "permission-denied" };
        _platform.RecordAuthorizationDenied(authenticated.Account, context, L12Authorization.Key(permission),
            "permission-denied");
        failure = ApiError(request, "permission_denied", "当前账号没有执行此操作的权限",
            StatusCodes.Status403Forbidden);
        return false;
    }

    private static L12AdminAuditContext AuditContext(HttpRequest request, L12Permission permission)
        => new(CorrelationId(request), L12Authorization.Key(permission), RequestMethod: request.Method,
            RequestPath: request.HttpContext.Items.TryGetValue(L12CorrelationIds.OriginalPathItemName, out var path)
                ? path as string
                : request.Path.Value);

    private static L12AdminMatchQuery AdminMatchQuery(HttpRequest request)
        => new(
            Cursor: QueryValue(request, "cursor"),
            Limit: QueryInt(request, 50, "limit"),
            ModeId: QueryValue(request, "modeId", "mode"),
            Status: QueryValue(request, "status"),
            Player: QueryValue(request, "player"),
            MasterId: QueryValue(request, "masterId"),
            Winner: QueryNullableInt(request, "winner", "result"),
            FromUtc: QueryDate(request, "fromUtc", "from"),
            ToUtc: QueryInclusiveEndDate(request, "toUtc", "to"),
            RulesVersion: QueryValue(request, "rulesVersion"),
            SeasonId: QueryValue(request, "seasonId"));

    private L12CardAnalyticsQuery CardAnalyticsQuery(HttpRequest request)
    {
        var exclusions = _platform.ReadCommittedStatisticsExclusions();
        var search = QueryValue(request, "search");
        var candidates = search is null ? null : _catalog.Cards.Values
            .Where(card => card.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
                           || card.Number.Contains(search, StringComparison.OrdinalIgnoreCase)
                           || card.NameZh.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(card => card.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new L12CardAnalyticsQuery(
            Cursor: QueryValue(request, "cursor"),
            Limit: QueryInt(request, 50, "limit"),
            Page: QueryInt(request, 1, "page"),
            Sort: QueryValue(request, "sort") ?? "sample-size",
            Direction: QueryValue(request, "direction") ?? "desc",
            MinimumSampleSize: QueryInt(request, 5, "minimumSampleSize", "minimumSample"),
            Search: search,
            CandidateCardIds: candidates,
            ModeId: "ranked",
            MasterId: QueryValue(request, "masterId"),
            FromUtc: QueryDate(request, "fromUtc", "from"),
            ToUtc: QueryInclusiveEndDate(request, "toUtc", "to"),
            OpponentMasterId: QueryValue(request, "opponentMasterId"),
            Initiative: QueryValue(request, "initiative"),
            RulesVersion: QueryValue(request, "rulesVersion"),
            SeasonId: QueryValue(request, "seasonId"),
            EffectVersion: _recorder.ResolveAnalyticsEffectVersion(QueryValue(request, "effectVersion")),
            ExcludedMatchIds: exclusions.MatchIds.ToArray(),
            ExcludedAccountIds: exclusions.AccountIds.ToArray());
    }

    private static string? QueryValue(HttpRequest request, params string[] names)
    {
        foreach (var name in names)
        {
            var value = request.Query[name].FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static int QueryInt(HttpRequest request, int fallback, params string[] names)
    {
        var value = QueryValue(request, names);
        if (value is null) return fallback;
        if (!int.TryParse(value, out var parsed)) throw new ArgumentException($"查询参数 {names[0]} 必须是整数");
        return parsed;
    }

    private static int? QueryNullableInt(HttpRequest request, params string[] names)
    {
        var value = QueryValue(request, names);
        if (value is null) return null;
        if (!int.TryParse(value, out var parsed)) throw new ArgumentException($"查询参数 {names[0]} 必须是整数");
        return parsed;
    }

    private static DateOnly? QueryGlobalAnalyticsDate(HttpRequest request, string dateName,
        string legacyTimestampName, string legacyAlias)
    {
        var value = QueryValue(request, dateName);
        if (value is not null)
        {
            if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date))
                throw new ArgumentException($"查询参数 {dateName} 必须是有效的 YYYY-MM-DD 自然日");
            return date;
        }
        // Preserve explicit legacy timestamp semantics; calendar dates must not
        // pass through the server's local time zone and shift a selected day.
        if (QueryValue(request, legacyTimestampName) is not null)
            return DateOnly.FromDateTime(QueryDate(request, legacyTimestampName)!.Value.UtcDateTime);
        value = QueryValue(request, legacyAlias);
        if (value is null) return null;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var calendarDate)) return calendarDate;
        return DateOnly.FromDateTime(QueryDate(request, legacyAlias)!.Value.UtcDateTime);
    }

    private static DateTimeOffset? QueryDate(HttpRequest request, params string[] names)
    {
        var value = QueryValue(request, names);
        if (value is null) return null;
        if (!DateTimeOffset.TryParse(value, out var parsed))
            throw new ArgumentException($"查询参数 {names[0]} 必须是 ISO-8601 时间");
        return parsed;
    }

    private static DateTimeOffset? QueryInclusiveEndDate(HttpRequest request, params string[] names)
    {
        var value = QueryValue(request, names);
        if (value is null) return null;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
            return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(1);
        if (!DateTimeOffset.TryParse(value, out var parsed))
            throw new ArgumentException($"查询参数 {names[0]} 必须是 ISO-8601 时间");
        return parsed;
    }

    private static string CorrelationId(HttpRequest request)
        => request.HttpContext.Items.TryGetValue(L12CorrelationIds.ContextItemName, out var value)
           && value is string correlationId
            ? correlationId
            : L12CorrelationIds.AcceptOrCreate(request.Headers[L12CorrelationIds.HeaderName]);

    private static string ClientKey(HttpRequest request)
        => request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";

    private static IResult StorageConflictResponse(HttpRequest request)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return ApiError(request, "storage_conflict", "数据已更新，请刷新后重试", StatusCodes.Status409Conflict);
    }

    private static IResult ApiError(HttpRequest request, string code, string message, int statusCode)
    {
        if (statusCode == StatusCodes.Status503ServiceUnavailable
            && code is "feature_disabled" or "email_feature_disabled")
            request.HttpContext.Items[L12HttpPerformanceMonitor.ExpectedUnavailableItemName] = true;
        return Results.Json(new L12ApiError(code, message, CorrelationId(request)), statusCode: statusCode);
    }

    private static IResult SeasonManagementError(HttpRequest request, L12OperationsConfigException error)
    {
        var status = error.Code switch
        {
            "season_definition_not_found" or "season_archive_not_found" => StatusCodes.Status404NotFound,
            "season_definition_revision_conflict" or "season_draft_exists" or "duplicate_season_id"
                or "operations_version_conflict" or "season_preview_stale"
                or "season_activation_preview_stale" or "season_activation_plan_conflict"
                or "season_cutover_snapshot_conflict"
                => StatusCodes.Status409Conflict,
            "permission_denied" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };
        return ApiError(request, error.Code, error.Message, status);
    }

    private static void LogAdminReadFailure(HttpRequest request, string component, Exception error)
        => Console.Error.WriteLine($"[{CorrelationId(request)}] admin read failed: component={component}, "
            + $"errorType={error.GetType().FullName}");

    private static async Task<IResult> OwnedSessionRevocationResponse(HttpRequest request,
        Func<Task<L12SessionRevocationResult>> operation)
    {
        try { return SessionRevocationResponse(request, await operation()); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    }

    private static IResult SessionRevocationResponse(HttpRequest request, L12SessionRevocationResult result)
        => result.Found
            ? Results.Ok(new
            {
                result.SessionId,
                result.RevokedCount,
                result.AlreadyRevoked,
            })
            : ApiError(request, "session_not_found", "会话不存在", StatusCodes.Status404NotFound);

    private L12AdminCommandEnvelope<TPayload> CommandEnvelope<TPayload>(HttpRequest request,
        L12AccountView actor, L12Permission permission, string type, string scope, TPayload payload,
        string? bodyIdempotencyKey = null, long? bodyExpectedVersion = null, bool bodyDryRun = false,
        string? bodyReason = null)
    {
        var idempotencyKey = string.IsNullOrWhiteSpace(bodyIdempotencyKey)
            ? request.Headers["Idempotency-Key"].FirstOrDefault()
            : bodyIdempotencyKey;
        var expectedVersion = bodyExpectedVersion ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault());
        var dryRun = bodyDryRun || bool.TryParse(request.Query["dryRun"].FirstOrDefault(), out var requestedDryRun)
            && requestedDryRun;
        var reason = string.IsNullOrWhiteSpace(bodyReason)
            ? request.Headers["X-Admin-Reason"].FirstOrDefault()
            : bodyReason;
        var commandId = Guid.NewGuid().ToString("N");
        var audit = AuditContext(request, permission) with
        {
            CommandId = commandId,
            IdempotencyKey = idempotencyKey,
            ExpectedVersion = expectedVersion,
            DryRun = dryRun,
            Reason = reason,
            Outcome = dryRun ? "dry-run" : "succeeded",
        };
        return new L12AdminCommandEnvelope<TPayload>(commandId, idempotencyKey, type, actor,
            DateTimeOffset.UtcNow, scope, reason, dryRun, expectedVersion, payload, audit);
    }

    private bool TrySecurityCommandOptions(HttpRequest request, L12AccountView actor,
        L12Permission permission, string? bodyIdempotencyKey, long? bodyExpectedVersion,
        out string idempotencyKey, out long expectedVersion, out IResult failure)
    {
        idempotencyKey = string.IsNullOrWhiteSpace(bodyIdempotencyKey)
            ? request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim() ?? string.Empty
            : bodyIdempotencyKey.Trim();
        var expected = bodyExpectedVersion ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault());
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "idempotency-key-required" },
                "security.precondition", request.Path, "idempotency-key-required");
            expectedVersion = 0;
            failure = ApiError(request, "idempotency_key_required", "安全治理写操作必须提供幂等键",
                StatusCodes.Status400BadRequest);
            return false;
        }
        if (expected is null)
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "expected-version-required" },
                "security.precondition", request.Path, "expected-version-required");
            expectedVersion = 0;
            failure = ApiError(request, "expected_version_required",
                "安全治理写操作必须提供 expectedVersion/If-Match", StatusCodes.Status428PreconditionRequired);
            return false;
        }
        expectedVersion = expected.Value;
        failure = Results.Empty;
        return true;
    }

    private bool TryOperationsCommandOptions(HttpRequest request, L12AccountView actor,
        L12Permission permission, string? bodyIdempotencyKey, long? bodyExpectedVersion,
        out string idempotencyKey, out long expectedVersion, out IResult failure)
    {
        idempotencyKey = string.IsNullOrWhiteSpace(bodyIdempotencyKey)
            ? request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim() ?? string.Empty
            : bodyIdempotencyKey.Trim();
        var expected = bodyExpectedVersion ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault());
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "idempotency-key-required" },
                "operations.precondition", request.Path, "idempotency-key-required");
            expectedVersion = 0;
            failure = ApiError(request, "idempotency_key_required", "运营配置写操作必须提供幂等键",
                StatusCodes.Status400BadRequest);
            return false;
        }
        if (expected is null)
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "expected-version-required" },
                "operations.precondition", request.Path, "expected-version-required");
            expectedVersion = 0;
            failure = ApiError(request, "expected_version_required",
                "运营配置写操作必须提供 expectedVersion/If-Match", StatusCodes.Status428PreconditionRequired);
            return false;
        }
        expectedVersion = expected.Value;
        failure = Results.Empty;
        return true;
    }

    private static string ResolveSeasonCommandKey(HttpRequest request, string? bodyIdempotencyKey)
    {
        var supplied = string.IsNullOrWhiteSpace(bodyIdempotencyKey)
            ? request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim()
            : bodyIdempotencyKey.Trim();
        if (!string.IsNullOrWhiteSpace(supplied)) return supplied;
        return $"season-compat-{Guid.NewGuid():N}";
    }

    private static L12AdminCommandResult<T> ExecuteOperationsConfig<T>(Func<T> operation)
    {
        try
        {
            return L12AdminCommandResult<T>.Ok(operation());
        }
        catch (L12OperationsConfigException error)
        {
            var status = error.Code switch
            {
                "operations_version_conflict" => StatusCodes.Status409Conflict,
                "operations_version_not_found" => StatusCodes.Status404NotFound,
                "operations_section_conflict" or "operations_field_conflict"
                    => StatusCodes.Status409Conflict,
                "operations_section_not_found" or "operations_section_version_not_found"
                    => StatusCodes.Status404NotFound,
                "season_definition_not_found" => StatusCodes.Status404NotFound,
                "season_definition_revision_conflict" or "season_link_conflict"
                    or "season_cutover_not_ready" or "season_runtime_conflict"
                    or "season_archive_conflict" or "season_preview_stale"
                    or "season_activation_preview_stale" or "season_activation_plan_conflict"
                    or "season_cutover_snapshot_conflict"
                    or "season_draft_exists" or "duplicate_season_id" => StatusCodes.Status409Conflict,
                "permission_denied" => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest,
            };
            return L12AdminCommandResult<T>.Fail(error.Code,
                error.Message, status);
        }
    }

    private static IResult OperationsConfigError(HttpRequest request, L12OperationsConfigException error)
    {
        var status = error.Code switch
        {
            "operations_version_conflict" => StatusCodes.Status409Conflict,
            "operations_version_not_found" => StatusCodes.Status404NotFound,
            "operations_section_conflict" or "operations_field_conflict"
                => StatusCodes.Status409Conflict,
            "operations_section_not_found" or "operations_section_version_not_found"
                => StatusCodes.Status404NotFound,
            "permission_denied" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };
        return ApiError(request, error.Code, error.Message, status);
    }

    private L12AuditArchiveCommandPayload CaptureAuditArchiveForCommand(L12AccountView actor,
        string idempotencyKey, int? retentionDays)
    {
        var captured = _platform.CaptureAuditArchive(retentionDays);
        var prior = _platform.FindAdminCommand(actor.Id, idempotencyKey);
        if (prior?.View.Type == "security.audit.archive"
            && prior.View.Payload.Deserialize<L12AuditArchiveCommandPayload>(CommandJsonOptions) is { } stored
            && stored.RetentionDays == captured.RetentionDays)
            return stored;
        return captured;
    }

    private bool TryTournamentCommandOptions(HttpRequest request, L12AccountView actor, L12Permission permission,
        string? bodyIdempotencyKey, long? bodyExpectedVersion, out string idempotencyKey,
        out long expectedVersion, out IResult failure)
    {
        idempotencyKey = string.IsNullOrWhiteSpace(bodyIdempotencyKey)
            ? request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim() ?? string.Empty
            : bodyIdempotencyKey.Trim();
        var expected = bodyExpectedVersion ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault());
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "idempotency-key-required" },
                "tournament.precondition", request.Path, "idempotency-key-required");
            expectedVersion = 0;
            failure = ApiError(request, "idempotency_key_required", "赛事写操作必须提供幂等键",
                StatusCodes.Status400BadRequest);
            return false;
        }
        if (expected is null)
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "expected-version-required" },
                "tournament.precondition", request.Path, "expected-version-required");
            expectedVersion = 0;
            failure = ApiError(request, "expected_version_required", "赛事写操作必须提供 expectedVersion/If-Match",
                StatusCodes.Status428PreconditionRequired);
            return false;
        }
        expectedVersion = expected.Value;
        failure = Results.Empty;
        return true;
    }

    private L12AdminCommandResult<T> ExecuteTournamentCommand<TPayload, T>(
        L12AdminCommandEnvelope<TPayload> command,
        L12Permission permission,
        Func<L12AdminCommandEnvelope<TPayload>, bool, T> operation,
        L12AdminCommandRisk risk = L12AdminCommandRisk.Low)
    {
        Func<L12AccountView, bool>? scopedAuthorization = permission is L12Permission.TournamentsManage
            or L12Permission.TournamentRulingsWrite
            ? static (L12AccountView _) => true
            : null;
        if (risk == L12AdminCommandRisk.High && !command.DryRun)
        {
            var validation = TournamentOperation(command.Actor, command.AuditContext, permission,
                () => operation(command, false));
            if (!validation.Success) return validation;
        }
        return _adminCommands.Execute(command, permission,
            current => TournamentOperation(current.Actor, current.AuditContext, permission,
                () => operation(current, true)),
            current => TournamentOperation(current.Actor, current.AuditContext, permission,
                () => operation(current, false)), risk, scopedAuthorization);
    }

    private L12AdminCommandResult<T> TournamentOperation<T>(L12AccountView actor, L12AdminAuditContext audit,
        L12Permission permission, Func<T> operation)
    {
        try { return L12AdminCommandResult<T>.Ok(operation()); }
        catch (L12TournamentScopeException error)
        {
            _platform.RecordAuthorizationDenied(actor,
                audit with { Outcome = "denied", Reason = "scope-denied" },
                L12Authorization.Key(permission), "scope-denied");
            return L12AdminCommandResult<T>.Fail("scope_denied", error.Message, StatusCodes.Status403Forbidden);
        }
        catch (L12TournamentVersionConflictException error)
        {
            return L12AdminCommandResult<T>.Fail("tournament_version_conflict", error.Message,
                StatusCodes.Status409Conflict);
        }
        catch (L12TournamentPairingException error)
        {
            return L12AdminCommandResult<T>.Fail("tournament_pairing_unavailable", error.Message,
                StatusCodes.Status409Conflict);
        }
        catch (KeyNotFoundException error)
        {
            return L12AdminCommandResult<T>.Fail("tournament_resource_not_found", error.Message,
                StatusCodes.Status404NotFound);
        }
        catch (ArgumentException error)
        {
            return L12AdminCommandResult<T>.Fail("invalid_tournament_request", error.Message,
                StatusCodes.Status400BadRequest);
        }
    }

    private IResult TournamentCommandResponse<TPayload, T>(HttpRequest request,
        L12AdminCommandEnvelope<TPayload> command, L12AdminCommandResult<T> outcome, string? tournamentId = null)
    {
        var result = AdminCommandResponse(request, command, outcome);
        if (outcome.Success && !outcome.Replayed && !command.DryRun) NotifyTournamentsChanged();
        var version = tournamentId is null
            ? _platform.Version
            : _platform.AdminCommandResourceVersion("tournament.read", $"tournament:{tournamentId}")
              ?? _platform.Version;
        request.HttpContext.Response.Headers.ETag = $"\"{version}\"";
        return result;
    }

    private bool TryReleaseCommandOptions(HttpRequest request, L12AccountView actor, L12Permission permission,
        string? bodyIdempotencyKey, long? bodyExpectedVersion, out string idempotencyKey,
        out long expectedVersion, out IResult failure)
    {
        idempotencyKey = string.IsNullOrWhiteSpace(bodyIdempotencyKey)
            ? request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim() ?? string.Empty
            : bodyIdempotencyKey.Trim();
        var expected = bodyExpectedVersion ?? ParseExpectedVersion(request.Headers.IfMatch.FirstOrDefault());
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "idempotency-key-required" },
                "release.precondition", request.Path, "idempotency-key-required");
            expectedVersion = 0;
            failure = ApiError(request, "idempotency_key_required", "发布写操作必须提供幂等键",
                StatusCodes.Status400BadRequest);
            return false;
        }
        if (expected is null)
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with { Outcome = "rejected", Reason = "expected-version-required" },
                "release.precondition", request.Path, "expected-version-required");
            expectedVersion = 0;
            failure = ApiError(request, "expected_version_required", "发布写操作必须提供 expectedVersion/If-Match",
                StatusCodes.Status428PreconditionRequired);
            return false;
        }
        expectedVersion = expected.Value;
        failure = Results.Empty;
        return true;
    }

    private bool TryReplayReleaseCommand(HttpRequest request, L12AccountView actor, L12Permission permission,
        string type, string idempotencyKey, long expectedVersion, bool dryRun, string? reason,
        Func<L12ReleaseCommandPayload, bool> matchesRequest, out IResult response)
    {
        var prior = _platform.FindAdminCommand(actor.Id, idempotencyKey);
        if (prior is null)
        {
            response = Results.Empty;
            return false;
        }

        var payload = prior.View.Payload.Deserialize<L12ReleaseCommandPayload>(CommandJsonOptions);
        if (prior.View.Type != type || prior.View.ExpectedVersion != expectedVersion
            || prior.View.DryRun != dryRun || payload is null || !matchesRequest(payload))
        {
            _platform.RecordCommandOutcome(actor,
                AuditContext(request, permission) with
                {
                    Outcome = "rejected",
                    IdempotencyKey = idempotencyKey,
                    ExpectedVersion = expectedVersion,
                    DryRun = dryRun,
                    Reason = "idempotency-key-reused",
                }, type, request.Path, "idempotency-key-reused");
            response = ApiError(request, "idempotency_conflict", "同一幂等键已用于不同命令",
                StatusCodes.Status409Conflict);
            return true;
        }

        var command = CommandEnvelope(request, actor, permission, type, prior.View.Scope, payload,
            idempotencyKey, expectedVersion, dryRun, reason);
        var outcome = _adminCommands.Execute(command, permission,
            current => ExecuteReleaseCommand(current.Actor, current.Payload, expectedVersion,
                current.AuditContext, true),
            current => ExecuteReleaseCommand(current.Actor, current.Payload, expectedVersion,
                current.AuditContext, false), L12AdminCommandRisk.High);
        response = ReleaseCommandResponse(request, command, outcome, payload.Environment);
        return true;
    }

    private L12AdminCommandResult<L12ReleaseOperationView> ExecuteReleaseCommand(L12AccountView actor,
        L12ReleaseCommandPayload payload, long expectedVersion, L12AdminAuditContext audit, bool apply)
    {
        try
        {
            var operation = apply
                ? _platform.ExecuteRelease(actor, payload, expectedVersion, _releaseControl, audit)
                : _platform.PlanRelease(actor, payload, expectedVersion, _releaseControl);
            if (apply && operation.Run?.Status != "succeeded")
                return L12AdminCommandResult<L12ReleaseOperationView>.Fail("release_validation_failed",
                    "发布验证失败，运行记录已保存并按适配器结果执行回滚", StatusCodes.Status502BadGateway);
            return L12AdminCommandResult<L12ReleaseOperationView>.Ok(operation,
                apply ? "发布执行与验证完成" : "发布干运行验证通过");
        }
        catch (L12ReleaseVersionConflictException error)
        {
            return L12AdminCommandResult<L12ReleaseOperationView>.Fail("release_version_conflict", error.Message,
                StatusCodes.Status409Conflict);
        }
        catch (L12ReleaseArtifactException error)
        {
            return L12AdminCommandResult<L12ReleaseOperationView>.Fail(error.Code.Replace('-', '_'), error.Message,
                StatusCodes.Status409Conflict);
        }
        catch (L12ReleaseScopeException error)
        {
            return L12AdminCommandResult<L12ReleaseOperationView>.Fail("release_scope_denied", error.Message,
                StatusCodes.Status403Forbidden);
        }
        catch (KeyNotFoundException error)
        {
            return L12AdminCommandResult<L12ReleaseOperationView>.Fail("release_resource_not_found", error.Message,
                StatusCodes.Status404NotFound);
        }
    }

    private IResult ReleaseCommandResponse<TPayload>(HttpRequest request,
        L12AdminCommandEnvelope<TPayload> command, L12AdminCommandResult<L12ReleaseOperationView> outcome,
        string environment)
    {
        var result = AdminCommandResponse(request, command, outcome);
        request.HttpContext.Response.Headers.ETag = $"\"{_platform.ReleaseEnvironmentVersion(environment)}\"";
        return result;
    }

    private static bool IsReleaseRequestError(Exception error)
        => error is L12ReleaseScopeException or L12ReleaseVersionConflictException
            or L12ReleaseArtifactException or KeyNotFoundException or ArgumentException;

    private IResult ReleaseRequestError(HttpRequest request, L12AccountView actor, L12Permission permission,
        Exception error)
    {
        var audit = AuditContext(request, permission) with { Outcome = "rejected", Reason = error.GetType().Name };
        if (error is L12ReleaseScopeException)
        {
            _platform.RecordAuthorizationDenied(actor, audit with { Outcome = "denied", Reason = "scope-denied" },
                L12Authorization.Key(permission), "scope-denied");
            return ApiError(request, "release_scope_denied", error.Message, StatusCodes.Status403Forbidden);
        }
        _platform.RecordCommandOutcome(actor, audit, "release.prevalidation", request.Path,
            error is L12ReleaseArtifactException artifactError ? artifactError.Code : error.GetType().Name);
        return error switch
        {
            L12ReleaseVersionConflictException => ApiError(request, "release_version_conflict", error.Message,
                StatusCodes.Status409Conflict),
            L12ReleaseArtifactException artifact => ApiError(request, artifact.Code.Replace('-', '_'),
                artifact.Message, StatusCodes.Status409Conflict),
            KeyNotFoundException => ApiError(request, "release_resource_not_found", error.Message,
                StatusCodes.Status404NotFound),
            _ => ApiError(request, "invalid_release_request", error.Message, StatusCodes.Status400BadRequest),
        };
    }

    private L12ContentRollbackCommandPayload CaptureContentRollbackForCommand(HttpRequest request,
        L12AccountView actor, ContentRollbackRequest body)
    {
        var batchId = body.BatchId?.Trim() ?? string.Empty;
        var rawIdempotencyKey = string.IsNullOrWhiteSpace(body.IdempotencyKey)
            ? request.Headers["Idempotency-Key"].FirstOrDefault()
            : body.IdempotencyKey;
        var idempotencyKey = rawIdempotencyKey?.Trim();
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var prior = _platform.FindAdminCommand(actor.Id, idempotencyKey);
            if (prior is not null)
            {
                if (string.Equals(prior.View.Type, "content.rollback.batch", StringComparison.Ordinal)
                    && prior.View.Payload.Deserialize<L12ContentRollbackCommandPayload>(CommandJsonOptions) is { } stored
                    && string.Equals(stored.BatchId, batchId, StringComparison.Ordinal))
                    return stored;

                // 让命令总线统一返回幂等键冲突；无需在进入总线前读取或验证另一批次。
                return new L12ContentRollbackCommandPayload(batchId, []);
            }
        }

        return _platform.CaptureContentRollback(batchId);
    }

    private IResult SaveArticleDraftResponse(HttpRequest request, string? id, ArticleDraftRequest body)
    {
        const L12Permission permission = L12Permission.AdminContentDraft;
        if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
        try
        {
            var draft = new L12ArticleDraft(id, body.Title ?? string.Empty, body.Summary ?? string.Empty,
                body.Body ?? string.Empty, body.Category ?? string.Empty, body.CoverUrl ?? string.Empty,
                body.Link ?? string.Empty, body.Slug ?? string.Empty, body.Pinned, body.PublishAt,
                body.ExpectedRevision, body.Kind ?? "news", body.CategoryId, body.MediaAssetId,
                body.SortOrder, body.VideoAuthorName ?? string.Empty);
            return Results.Ok(_platform.SaveArticleDraft(authenticated.Account, draft,
                RequestAuditContext(request, permission)));
        }
        catch (InvalidOperationException error)
        {
            return ApiError(request, "article_version_conflict", error.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException error)
        {
            return ApiError(request, "invalid_article", error.Message, StatusCodes.Status400BadRequest);
        }
    }

    private IResult ArticleMutationResponse(HttpRequest request, L12Permission permission, string id,
        string action, Func<L12AccountView, L12AdminAuditContext, L12ArticleView> mutate)
    {
        if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
        try { return Results.Ok(mutate(authenticated.Account, RequestAuditContext(request, permission))); }
        catch (KeyNotFoundException error)
        {
            return ApiError(request, "article_not_found", error.Message, StatusCodes.Status404NotFound);
        }
        catch (ArgumentException error)
        {
            return ApiError(request, $"article_{action}_invalid", error.Message, StatusCodes.Status400BadRequest);
        }
    }

    private IResult SaveSiteCategoryResponse(HttpRequest request, string? id, SiteCategoryRequest body)
    {
        const L12Permission permission = L12Permission.AdminContentDraft;
        if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
        try
        {
            var result = _platform.SaveSiteCategory(authenticated.Account, new L12SiteCategoryDraft(id,
                body.Kind ?? string.Empty, body.Name ?? string.Empty, body.Slug ?? string.Empty,
                body.SortOrder, body.Active, body.ExpectedVersion), RequestAuditContext(request, permission));
            return Results.Ok(result);
        }
        catch (L12SiteContentConflictException error)
        {
            return ApiError(request, "category_version_conflict", error.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException error)
        {
            return ApiError(request, "category_invalid", error.Message, StatusCodes.Status400BadRequest);
        }
    }

    private sealed record UploadedFormFile(string FileName, string ContentType, byte[] Bytes);
    private sealed class SiteMediaUploadTooLargeException(string message) : Exception(message);

    private static async Task<UploadedFormFile> ReadRequiredFormFile(IFormCollection form, string name,
        int maxBytes, CancellationToken cancellationToken)
    {
        var file = form.Files.GetFile(name) ?? throw new InvalidDataException($"缺少素材文件：{name}");
        if (file.Length <= 0) throw new InvalidDataException($"素材文件 {name} 不能为空");
        if (file.Length > maxBytes)
            throw new SiteMediaUploadTooLargeException($"素材文件 {name} 超过 {maxBytes / 1024 / 1024}MB 限制");
        await using var stream = new MemoryStream((int)file.Length);
        await file.CopyToAsync(stream, cancellationToken);
        if (stream.Length > maxBytes) throw new SiteMediaUploadTooLargeException($"素材文件 {name} 超过大小限制");
        return new UploadedFormFile(Path.GetFileName(file.FileName), file.ContentType, stream.ToArray());
    }

    private static double ParseFiniteDouble(string? value, double fallback)
        => double.TryParse(value, System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed)
            ? parsed : fallback;

    private L12AdminAuditContext RequestAuditContext(HttpRequest request, L12Permission permission)
        => new(request.HttpContext.Items[L12CorrelationIds.ContextItemName]?.ToString() ?? Guid.NewGuid().ToString("N"),
            L12Authorization.Key(permission), RequestMethod: request.Method, RequestPath: request.Path);

    private IResult AdminCommandResponse<TPayload, T>(HttpRequest request,
        L12AdminCommandEnvelope<TPayload> command, L12AdminCommandResult<T> outcome)
    {
        request.HttpContext.Response.Headers["X-Command-ID"] = outcome.Command?.Id ?? command.CommandId;
        request.HttpContext.Response.Headers.ETag = $"\"{_platform.Version}\"";
        if (outcome.Replayed) request.HttpContext.Response.Headers["X-Idempotent-Replay"] = "true";
        return outcome.Success
            ? Results.Ok(outcome.Value)
            : ApiError(request, outcome.Code, outcome.Message, outcome.StatusCode);
    }

    private IResult AdminReviewResponse(HttpRequest request,
        L12AdminCommandResult<L12AdminCommandView> outcome)
    {
        if (outcome.Command is not null) request.HttpContext.Response.Headers["X-Command-ID"] = outcome.Command.Id;
        request.HttpContext.Response.Headers.ETag = $"\"{_platform.Version}\"";
        if (outcome.Replayed) request.HttpContext.Response.Headers["X-Idempotent-Replay"] = "true";
        return outcome.Success
            ? Results.Ok(outcome.Value)
            : ApiError(request, outcome.Code, outcome.Message, outcome.StatusCode);
    }

    private L12AdminCommandResult<L12ContentBatchOperationView> ExecuteContentPublish(
        L12AdminCommandEnvelope<L12ContentPublishCommandPayload> command, L12Permission permission)
        => _adminCommands.Execute(command, permission, current =>
        {
            try
            {
                var batch = _platform.PublishContentBatch(current.Actor, current.Payload, current.AuditContext);
                if (batch.Items.Any(item => item.Key is "rules.center" or "rules.rulings"))
                    NotifyRulesContentChanged();
                return L12AdminCommandResult<L12ContentBatchOperationView>.Ok(
                    new L12ContentBatchOperationView(true, batch, null), "内容批次已发布");
            }
            catch (L12ContentStateConflictException error)
            {
                return L12AdminCommandResult<L12ContentBatchOperationView>.Fail("content_version_conflict",
                    error.Message, StatusCodes.Status409Conflict);
            }
            catch (ArgumentException error)
            {
                return L12AdminCommandResult<L12ContentBatchOperationView>.Fail("content_validation_failed",
                    error.Message, StatusCodes.Status400BadRequest);
            }
        }, current =>
        {
            try
            {
                var preview = _platform.PreviewContentPublish(current.Payload);
                return L12AdminCommandResult<L12ContentBatchOperationView>.Ok(
                    new L12ContentBatchOperationView(false, null, preview), "干运行验证通过");
            }
            catch (L12ContentStateConflictException error)
            {
                return L12AdminCommandResult<L12ContentBatchOperationView>.Fail("content_version_conflict",
                    error.Message, StatusCodes.Status409Conflict);
            }
            catch (ArgumentException error)
            {
                return L12AdminCommandResult<L12ContentBatchOperationView>.Fail("content_validation_failed",
                    error.Message, StatusCodes.Status400BadRequest);
            }
        }, L12AdminCommandRisk.High);

    private L12AdminCommandResult<L12ContentBatchOperationView> ExecuteContentRollback(
        L12AdminCommandEnvelope<L12ContentRollbackCommandPayload> command, L12Permission permission)
        => _adminCommands.Execute(command, permission, current =>
        {
            try
            {
                var batch = _platform.RollbackContentBatch(current.Actor, current.Payload, current.AuditContext);
                if (batch.Items.Any(item => item.Key is "rules.center" or "rules.rulings"))
                    NotifyRulesContentChanged();
                return L12AdminCommandResult<L12ContentBatchOperationView>.Ok(
                    new L12ContentBatchOperationView(true, batch, null), "内容批次已回滚");
            }
            catch (L12ContentStateConflictException error)
            {
                return L12AdminCommandResult<L12ContentBatchOperationView>.Fail("content_version_conflict",
                    error.Message, StatusCodes.Status409Conflict);
            }
            catch (KeyNotFoundException error)
            {
                return L12AdminCommandResult<L12ContentBatchOperationView>.Fail("content_batch_not_found",
                    error.Message, StatusCodes.Status404NotFound);
            }
            catch (ArgumentException error)
            {
                return L12AdminCommandResult<L12ContentBatchOperationView>.Fail("content_validation_failed",
                    error.Message, StatusCodes.Status400BadRequest);
            }
        }, current =>
        {
            try
            {
                var preview = _platform.PreviewContentRollback(current.Payload);
                return L12AdminCommandResult<L12ContentBatchOperationView>.Ok(
                    new L12ContentBatchOperationView(false, null, preview), "干运行验证通过");
            }
            catch (L12ContentStateConflictException error)
            {
                return L12AdminCommandResult<L12ContentBatchOperationView>.Fail("content_version_conflict",
                    error.Message, StatusCodes.Status409Conflict);
            }
            catch (ArgumentException error)
            {
                return L12AdminCommandResult<L12ContentBatchOperationView>.Fail("content_validation_failed",
                    error.Message, StatusCodes.Status400BadRequest);
            }
        }, L12AdminCommandRisk.High);

    private L12AdminCommandResult<L12EffectReviewView> ExecuteEffectReview(
        L12AdminCommandEnvelope<EffectReviewCommandPayload> command)
        => ValidateEffectReview(command, true);

    private IResult EffectWorkbenchAction(HttpRequest request, string cardId,
        L12EffectWorkbenchActionRequest body, string action)
    {
        const L12Permission permission = L12Permission.AdminEffectsReview;
        if (!TryAuthorize(request, permission, out var authenticated, out var failure)) return failure;
        var source = _catalog.AtomicEffects.Find(cardId);
        if (source is null) return ApiError(request, "effect_not_found", "卡牌效果不存在",
            StatusCodes.Status404NotFound);
        var effect = _platform.ApplyEffectPresentationOverrides(_platform.ApplyEffectReviews(source));
        try
        {
            var context = RequestAuditContext(request, permission);
            var result = action switch
            {
                "validate" => _platform.ValidateEffectWorkbenchDraft(authenticated.Account, effect, body, context),
                "review" => _platform.ReviewEffectWorkbenchDraft(authenticated.Account, effect, body, context),
                "publish" => _platform.PublishEffectWorkbenchDraft(authenticated.Account, effect, body, context),
                "rollback" => _platform.RollbackEffectWorkbench(authenticated.Account, effect, body, context),
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            };
            return Results.Ok(result);
        }
        catch (KeyNotFoundException error)
        {
            return ApiError(request, "effect_workbench_not_found", error.Message, StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException error)
        {
            return ApiError(request, "effect_workbench_conflict", error.Message, StatusCodes.Status409Conflict);
        }
        catch (ArgumentException error)
        {
            return ApiError(request, "invalid_effect_workbench", error.Message, StatusCodes.Status400BadRequest);
        }
    }

    private L12AdminCommandResult<L12EffectReviewView> ValidateEffectReview(
        L12AdminCommandEnvelope<EffectReviewCommandPayload> command, bool apply)
    {
        var effect = _catalog.AtomicEffects.Find(command.Payload.CardId);
        if (effect is null)
            return L12AdminCommandResult<L12EffectReviewView>.Fail("effect_not_found", "卡牌效果不存在",
                StatusCodes.Status404NotFound);
        var ability = command.Payload.AbilityId is null ? null : effect.Abilities
            .FirstOrDefault(item => string.Equals(item.AbilityId, command.Payload.AbilityId,
                StringComparison.OrdinalIgnoreCase));
        if (command.Payload.AbilityId is not null && ability is null)
            return L12AdminCommandResult<L12EffectReviewView>.Fail("stale_ability_id",
                "能力标识已过期，请刷新后重新审查", StatusCodes.Status409Conflict);
        if (command.Payload.Status is not ("unreviewed" or "human-assisted" or "confirmed" or "rejected"))
            return L12AdminCommandResult<L12EffectReviewView>.Fail("invalid_review_status", "无效的审查状态",
                StatusCodes.Status400BadRequest);
        if (!apply)
            return L12AdminCommandResult<L12EffectReviewView>.Ok(new L12EffectReviewView(command.Payload.CardId,
                command.Payload.AbilityId, command.Payload.Status, command.Payload.Note?.Trim() ?? string.Empty,
                command.Actor.Username, DateTimeOffset.UtcNow, ability?.StructureHash ?? string.Empty), "干运行验证通过");
        return L12AdminCommandResult<L12EffectReviewView>.Ok(_platform.SaveEffectReview(command.Actor,
            command.Payload.CardId, command.Payload.AbilityId, command.Payload.Status, command.Payload.Note,
            ability?.StructureHash, command.AuditContext), "审查记录已保存");
    }

    private L12AdminCommandResult<L12EffectPresentationScene> ExecuteEffectPresentation(
        L12AdminCommandEnvelope<EffectPresentationCommandPayload> command)
        => ValidateEffectPresentation(command, true);

    private L12AdminCommandResult<L12EffectPresentationScene> ValidateEffectPresentation(
        L12AdminCommandEnvelope<EffectPresentationCommandPayload> command, bool apply)
    {
        var effect = _catalog.AtomicEffects.Find(command.Payload.CardId);
        if (effect is null)
            return L12AdminCommandResult<L12EffectPresentationScene>.Fail("effect_not_found",
                "卡牌效果不存在", StatusCodes.Status404NotFound);
        var scene = effect.Abilities.SelectMany(ability => ability.Presentations)
            .FirstOrDefault(item => string.Equals(item.SceneId, command.Payload.SceneId,
                StringComparison.Ordinal));
        if (scene is null)
            return L12AdminCommandResult<L12EffectPresentationScene>.Fail("stale_presentation_scene",
                "动效场景标识已过期，请刷新后重试", StatusCodes.Status409Conflict);
        try
        {
            if (command.Payload.Restore)
            {
                if (apply) _platform.RestoreEffectPresentationDefault(command.Actor, scene, command.AuditContext);
                return L12AdminCommandResult<L12EffectPresentationScene>.Ok(
                    scene with { OverrideText = null }, apply ? "已恢复默认动效文案" : "干运行验证通过");
            }
            var normalized = L12EffectPresentationText.Validate(command.Payload.Text, scene.Placeholders);
            if (apply) _platform.SaveEffectPresentationOverride(command.Actor, scene, normalized, command.AuditContext);
            return L12AdminCommandResult<L12EffectPresentationScene>.Ok(
                scene with { OverrideText = normalized }, apply ? "动效文案已保存" : "干运行验证通过");
        }
        catch (ArgumentException error)
        {
            return L12AdminCommandResult<L12EffectPresentationScene>.Fail("invalid_presentation_text",
                error.Message, StatusCodes.Status400BadRequest);
        }
    }

    private L12AdminCommandResult<L12AccountStatusOperationView> ExecuteAccountStatus(L12AccountView actor,
        L12AccountStatusCommandPayload payload, L12AdminAuditContext audit, bool apply)
    {
        L12ChatAccountRegistry.MutationLease? mutation = null;
        try
        {
            if (apply && !TryBeginChatAccountMutation(payload.AccountId, out mutation))
                return L12AdminCommandResult<L12AccountStatusOperationView>.Fail("chat_fence_busy",
                    "账号聊天撤权栅栏正忙，请稍后重试", StatusCodes.Status503ServiceUnavailable);
            var operation = _platform.SetAccountDisabled(actor, payload.AccountId, payload.Disabled,
                payload.Reason, audit, apply);
            mutation?.Complete(operation.Applied
                ? L12ChatMutationOutcome.Committed : L12ChatMutationOutcome.KnownRollback);
            if (operation.Applied) _recorder.InvalidateAnalyticsCache();
            return L12AdminCommandResult<L12AccountStatusOperationView>.Ok(operation,
                apply ? "账号状态已更新" : "干运行验证通过");
        }
        catch (L12SecurityPolicyException error)
        {
            mutation?.Complete(L12ChatMutationOutcome.KnownRollback);
            var status = error.Code == "security_reason_required"
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status403Forbidden;
            return L12AdminCommandResult<L12AccountStatusOperationView>.Fail(error.Code, error.Message, status);
        }
        catch (KeyNotFoundException error)
        {
            mutation?.Complete(L12ChatMutationOutcome.KnownRollback);
            return L12AdminCommandResult<L12AccountStatusOperationView>.Fail("account_not_found", error.Message,
                StatusCodes.Status404NotFound);
        }
        finally { FinishChatAccountMutation(mutation); }
    }

    private L12AdminCommandResult<L12AdminPasswordResetView> ExecuteAdminPasswordReset(L12AccountView actor,
        AccountPasswordResetCommandPayload payload, L12AdminAuditContext audit, bool apply)
    {
        try
        {
            var operation = _platform.AdminResetPassword(actor, payload.AccountId, payload.Reason, audit, apply);
            return L12AdminCommandResult<L12AdminPasswordResetView>.Ok(operation,
                apply ? "账号密码已重置" : "干运行验证通过");
        }
        catch (L12SecurityPolicyException error)
        {
            return L12AdminCommandResult<L12AdminPasswordResetView>.Fail(error.Code, error.Message,
                error.Code == "security_reason_required" ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status403Forbidden);
        }
        catch (KeyNotFoundException error)
        {
            return L12AdminCommandResult<L12AdminPasswordResetView>.Fail("account_not_found", error.Message,
                StatusCodes.Status404NotFound);
        }
    }

    private L12AdminCommandResult<L12AccountDeletionView> ExecuteAccountDeletion(L12AccountView actor,
        AccountDeletionCommandPayload payload, L12AdminAuditContext audit, bool apply)
    {
        L12ChatAccountRegistry.MutationLease? mutation = null;
        try
        {
            var preview = _platform.DeleteAccountPersonalData(actor, payload.AccountId, payload.Reason,
                audit, false);
            if (!apply)
                return L12AdminCommandResult<L12AccountDeletionView>.Ok(preview, "干运行验证通过");
            if (!TryBeginChatAccountMutation(payload.AccountId, out mutation))
                return L12AdminCommandResult<L12AccountDeletionView>.Fail("chat_fence_busy",
                    "账号聊天撤权栅栏正忙，请稍后重试", StatusCodes.Status503ServiceUnavailable);
            var prior = _platform.Account(payload.AccountId)!;
            var cleanedMatches = _recorder.AnonymizeAccountAsync(prior.Id, prior.Username,
                L12PlatformStore.DeletedAccountName(prior.Id)).GetAwaiter().GetResult();
            var operation = _platform.DeleteAccountPersonalData(actor, payload.AccountId, payload.Reason,
                audit, true) with { CleanedMatchRecords = cleanedMatches };
            mutation?.Complete(L12ChatMutationOutcome.Committed);
            _recorder.InvalidateAnalyticsCache();
            return L12AdminCommandResult<L12AccountDeletionView>.Ok(operation, "账号已逻辑删除并清理个人数据");
        }
        catch (L12SecurityPolicyException error)
        {
            mutation?.Complete(L12ChatMutationOutcome.KnownRollback);
            return L12AdminCommandResult<L12AccountDeletionView>.Fail(error.Code, error.Message,
                error.Code == "security_reason_required" ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status403Forbidden);
        }
        catch (KeyNotFoundException error)
        {
            mutation?.Complete(L12ChatMutationOutcome.KnownRollback);
            return L12AdminCommandResult<L12AccountDeletionView>.Fail("account_not_found", error.Message,
                StatusCodes.Status404NotFound);
        }
        finally { FinishChatAccountMutation(mutation); }
    }

    private L12AdminCommandResult<L12AuditArchiveOperationView> ExecuteAuditArchive(L12AccountView actor,
        L12AuditArchiveCommandPayload payload, L12AdminAuditContext audit, bool apply)
    {
        try
        {
            var operation = _platform.ArchiveAudit(actor, payload, audit, apply);
            return L12AdminCommandResult<L12AuditArchiveOperationView>.Ok(operation,
                apply ? "审计归档段已生成，独立事件源保持不变" : "干运行验证通过");
        }
        catch (L12SecurityPolicyException error)
        {
            return L12AdminCommandResult<L12AuditArchiveOperationView>.Fail(error.Code, error.Message,
                error.Code == "audit_unavailable"
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status400BadRequest);
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            return L12AdminCommandResult<L12AuditArchiveOperationView>.Fail("audit_archive_failed",
                error.Message, StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static L12AdminCommandResult<L12SessionRevocationResult> SessionCommandResult(
        L12SessionRevocationResult result)
        => result.Found
            ? L12AdminCommandResult<L12SessionRevocationResult>.Ok(result)
            : L12AdminCommandResult<L12SessionRevocationResult>.Fail("session_not_found", "账号或会话不存在",
                StatusCodes.Status404NotFound);

    private IResult AdminSessionCommandResponse(HttpRequest request,
        L12AdminCommandEnvelope<SessionCommandPayload> command,
        L12AdminCommandResult<L12SessionRevocationResult> outcome)
    {
        if (!outcome.Success) return AdminCommandResponse(request, command, outcome);
        request.HttpContext.Response.Headers["X-Command-ID"] = command.CommandId;
        request.HttpContext.Response.Headers.ETag = $"\"{_platform.Version}\"";
        if (outcome.Replayed) request.HttpContext.Response.Headers["X-Idempotent-Replay"] = "true";
        return SessionRevocationResponse(request, outcome.Value!);
    }

    private static long? ParseExpectedVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) normalized = normalized[2..].Trim();
        normalized = normalized.Trim('"');
        return long.TryParse(normalized, out var parsed) && parsed >= 0 ? parsed : null;
    }

    private static bool TryReleaseActiveAccountSocket(
        ConcurrentDictionary<string, Guid> owners, string accountId, Guid expectedSessionId)
        => owners.TryRemove(new KeyValuePair<string, Guid>(accountId, expectedSessionId));

    private static bool TryReleaseSocketPlatformBinding(
        ConcurrentDictionary<Guid, SocketPlatformBinding> bindings, Guid sessionId,
        SocketPlatformBinding expectedBinding)
        => bindings.TryRemove(new KeyValuePair<Guid, SocketPlatformBinding>(sessionId,
            expectedBinding));

    private void HandlePlatformSessionsRevoked(IReadOnlyList<string> sessionIds)
    {
        var revoked = sessionIds.ToHashSet(StringComparer.Ordinal);
        foreach (var mapping in _socketPlatformSessions.Where(item => revoked.Contains(item.Value.PlatformSessionId)).ToArray())
        {
            if (!_socketPlatformSessions.TryGetValue(mapping.Key, out var currentBinding)
                || currentBinding != mapping.Value) continue;
            CloseChatStartsForTransport(mapping.Key);
            if (_inboundConnections.TryGetValue(mapping.Key, out var inbound))
                inbound.StopAcceptingAndCancelPending();
            if (!TryReleaseSocketPlatformBinding(_socketPlatformSessions, mapping.Key, mapping.Value))
                continue;
            TryReleaseActiveAccountSocket(_activeAccountSockets, mapping.Value.AccountId, mapping.Key);
            _rooms.RecordConnectionClaimRejection(mapping.Value.AccountId, "platform-session-revoked");
            if (_sockets.TryGetValue(mapping.Key, out var socket)) socket.Abort();
        }
    }

}

public sealed record AuthRequest(string? Username, string? Password);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record ChangeUsernameRequest(string? CurrentPassword, string? NewUsername);
public sealed record UsernameChangeApplicationRequest(string? NewUsername, string? Reason);
public sealed record UsernameChangeReviewRequest(bool Approve, string? Note);
public sealed record CurrentPasswordRequest(string? CurrentPassword);
public sealed record EmailBindingRequest(string? Email, string? CurrentPassword);
public sealed record ForgotPasswordRequest(string? Email);
public sealed record TokenRequest(string? Token);
public sealed record PasswordResetRequest(string? Token, string? NewPassword);
public sealed record FriendRequest(string? AccountId);
public sealed record FriendResolveRequest(bool Accept);
public sealed record RankedFactionRequest(string? Faction);
public sealed record RankedTitleRequest(string? Title);
public sealed record RankedConfigRequest(L12RankedConfigView Config, string? Reason);
public sealed record SeasonDraftCreateRequest(long ExpectedCurrentRevision, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record SeasonDraftUpdateRequest(L12SeasonDefinitionDraft Draft, long ExpectedRevision,
    string? Reason, string? PreviewToken = null, string? IdempotencyKey = null,
    long? ExpectedVersion = null);
public sealed record SeasonDraftDeleteRequest(long ExpectedRevision, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record SeasonDefinitionPreviewRequest(L12SeasonDefinitionDraft Draft,
    long ExpectedRevision, long? ExpectedVersion = null);
public sealed record SeasonDefinitionApplyRequest(L12SeasonDefinitionDraft Draft,
    long ExpectedRevision, string? PreviewToken, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record L12SeasonDraftCreateCommandPayload(long ExpectedCurrentRevision);
public sealed record L12SeasonDraftDeleteCommandPayload(string DefinitionId, long ExpectedRevision);
public sealed record L12SeasonDefinitionApplyCommandPayload(string DefinitionId,
    L12SeasonDefinitionDraft Draft, long ExpectedRevision, string PreviewToken);
public sealed record RankedBroadcastCompleteRequest(string? ClaimToken);
public sealed record RoleRequest(string? Role, string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record RoleCommandPayload(string AccountId, string Role);
public sealed record RoleCommandResult(string AccountId, string Role, bool Changed);
public sealed record AccountStatusRequest(bool Disabled, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record AccountSecurityActionRequest(string? Reason, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false);
public sealed record AccountPasswordResetCommandPayload(string AccountId, string Reason);
public sealed record AccountDeletionCommandPayload(string AccountId, string Reason);
public sealed record OperationsConfigPreviewRequest(L12OperationsConfigPayload Config,
    long? ExpectedVersion = null);
public sealed record OperationsConfigApplyRequest(L12OperationsConfigPayload Config, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null, string? CrossSectionReplaceIntent = null);
public sealed record OperationsConfigRollbackRequest(string? VersionId, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null, string? CrossSectionReplaceIntent = null);
public sealed record L12OperationsRollbackCommandPayload(string VersionId);
public sealed record OperationsConfigSectionPreviewRequest(L12OperationsSectionPayload Config,
    IReadOnlyDictionary<string, long>? ExpectedFieldRevisions = null,
    long? ExpectedRevision = null);
public sealed record OperationsConfigSectionApplyRequest(L12OperationsSectionPayload Config,
    IReadOnlyDictionary<string, long>? ExpectedFieldRevisions = null, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedRevision = null);
public sealed record OperationsConfigSectionRollbackRequest(string? VersionId, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedRevision = null);
public sealed record L12OperationsSectionApplyCommandPayload(string Section,
    L12OperationsSectionPayload Config, long ExpectedRevision,
    IReadOnlyDictionary<string, long> ExpectedFieldRevisions);
public sealed record L12OperationsSectionRollbackCommandPayload(string Section, string VersionId,
    long ExpectedRevision);
public sealed record SeasonActivationRequest(long ExpectedCurrentRevision, long ExpectedDraftRevision,
    string? Reason = null, string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record L12SeasonActivationCommandPayload(string DefinitionId,
    long ExpectedCurrentRevision, long ExpectedDraftRevision);
public sealed record RankedSeasonResetRepairPreviewRequest(string? SeasonId,
    long? ExpectedVersion = null, DateTimeOffset? CompetitiveStartAt = null);
public sealed record RankedSeasonResetRepairRequest(string? SeasonId, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null,
    string? ExpectedEvidenceFingerprint = null, DateTimeOffset? CompetitiveStartAt = null);
public sealed record SeasonIdentityMigrationPreviewRequest(long? ExpectedVersion = null);
public sealed record SeasonIdentityMigrationRequest(string? ExpectedPlatformFingerprint,
    string? ExpectedRecorderFingerprint, string? Reason = null, string? IdempotencyKey = null,
    long? ExpectedVersion = null);
public sealed record L12RankedSeasonResetRepairCommandPayload(string SeasonId,
    string ExpectedEvidenceFingerprint, DateTimeOffset? CompetitiveStartAt = null);
public sealed record SeasonActivationPreviewRequest(long ExpectedCurrentRevision,
    long ExpectedDraftRevision, long? ExpectedVersion = null);
public sealed record SeasonActivationArmRequest(long ExpectedCurrentRevision, long ExpectedDraftRevision,
    string? Reason = null, string? IdempotencyKey = null, long? ExpectedVersion = null,
    string? ImpactPreviewToken = null);
public sealed record L12SeasonActivationArmCommandPayload(string DefinitionId,
    long ExpectedCurrentRevision, long ExpectedDraftRevision, string ImpactPreviewToken);
public sealed record SeasonActivationDisarmRequest(long ExpectedDraftRevision,
    long ExpectedPlanGeneration, string? DisarmGuardToken = null, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record L12SeasonActivationDisarmCommandPayload(string DefinitionId,
    long ExpectedDraftRevision, long ExpectedPlanGeneration, string DisarmGuardToken);
public sealed record OperationsServerStartRequest(string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record L12ServerStartCommandPayload(long ExpectedVersion, string RequestedState = "open");
public sealed record OperationsImmediateMaintenanceRequest(int ExpectedDurationHours, string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record OperationsImmediateMaintenanceEndRequest(string? Reason = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null);
public sealed record L12ImmediateMaintenanceStartCommandPayload(int ExpectedDurationHours,
    long ExpectedVersion, string RequestedState = "maintenance");
public sealed record L12ImmediateMaintenanceEndCommandPayload(long ExpectedVersion,
    string RequestedState = "open");
public sealed record AuditArchiveRequest(int? RetentionDays = null, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record SessionCommandPayload(string AccountId, string? SessionId);
public sealed record ContentRequest(string? Value, string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record ArticleDraftRequest(string? Title, string? Summary, string? Body, string? Category,
    string? CoverUrl, string? Link, string? Slug, bool Pinned, DateTimeOffset? PublishAt,
    long? ExpectedRevision = null, string? Kind = null, string? CategoryId = null,
    string? MediaAssetId = null, int SortOrder = 0, string? VideoAuthorName = null);
public sealed record SiteCategoryRequest(string? Kind, string? Name, string? Slug, int SortOrder,
    bool Active = true, long? ExpectedVersion = null);
public sealed record SiteCategoryOrderRequest(IReadOnlyList<string>? Ids);
public sealed record ContentDraftCommandPayload(string Key, string Value);
public sealed record ContentBatchRequest(IReadOnlyList<string>? Keys, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record ContentRollbackRequest(string? BatchId, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record EffectReviewRequest(string? AbilityId, string? Status, string? Note,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record EffectReviewCommandPayload(string CardId, string? AbilityId, string Status, string? Note);
public sealed record EffectPresentationRequest(string? Text = null, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record EffectPresentationCommandPayload(string CardId, string SceneId, string? Text, bool Restore);
public sealed record BugRequest(string? Title, string Description, string? Page, string? RoomCode,
    string? MatchId, string? Version, L12ClientConnectionDiagnosticView? ClientDiagnostic = null);
public sealed record BugUpdateRequest(string? Status, string? Priority, string? Assignee, string? AdminNotes,
    string? Comment, string? FixCommit = null, string? RegressionTest = null, string? DeployedVersion = null,
    string? VerifiedBy = null, DateTimeOffset? VerifiedAt = null, string? DuplicateOf = null,
    string? ClosureDisposition = null, string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record BugUpdateCommandPayload(string Id, string? Status, string? Priority, string? Assignee,
    string? AdminNotes, string? Comment, string? FixCommit = null, string? RegressionTest = null,
    string? DeployedVersion = null, string? DuplicateOf = null, string? ClosureDisposition = null);
public sealed record PublishedDeckRequest(string? PublicationId, L12CustomDeckSubmission? Deck);
public sealed record AccountDeckUpdateRequest(L12CustomDeckSubmission? Deck, long ExpectedRevision);
public sealed record TournamentCreateRequest(L12TournamentCreatePayload Tournament, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentLegacyImportRequest(IReadOnlyList<L12LegacyTournamentInput>? Tournaments,
    string? PreviewHash = null, string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record TournamentRegistrationRequest(string? DeckName, string? DeckCode,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentPreCheckInRequest(string? DeckName, string? DeckCode, string? DeckId = null,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentRemoveParticipantRequest(string? AccountId, bool BanRegistration, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentRegistrationBanRequest(string? AccountId, bool Banned, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentOrganizerTransferRequest(string? AccountId, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentOrganizerTransferDecisionRequest(string? RequestId, bool Accept, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentActionRequest(string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record TournamentStaffRequest(IReadOnlyList<string>? RefereeAccountIds,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentVisibilityRequest(string? Visibility, string? RegistrationVisibility,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentCheckInRequest(string? AccountId, bool Ready,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record TournamentPauseRequest(bool Paused, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentTimeExtensionRequest(int Minutes, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentPhaseRequest(string? Phase, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentPostponeRequest(DateTimeOffset NewStartAt, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentCancelRequest(string? Reason, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentMatchPauseRequest(bool Paused, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentJudgeCaseCreateRequest(string? MatchId, string? Category, string? Urgency,
    string? Message, string? IdempotencyKey = null, long? ExpectedVersion = null,
    bool DryRun = false, string? Reason = null);
public sealed record TournamentJudgeCaseAssignRequest(string? CaseId, string? AssigneeAccountId, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentJudgeCaseResolveRequest(string? CaseId, string? Status, string? Resolution,
    string? StaffNote, string? Reason, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentJudgeCaseAppealRequest(string? CaseId, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentRulingRequest(string? Kind, string? TargetAccountId, string? Decision,
    string? Reason, string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentRematchRequest(string? Reason, string? IdempotencyKey = null,
    long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentMatchReferenceRequest(string? RecordedMatchId, string? Reason,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false);
public sealed record TournamentEmptyPayload;
public sealed record TournamentTargetCommandPayload(string TournamentId);
public sealed record TournamentRoundCommandPayload(string TournamentId, int RoundNumber);
public sealed record TournamentStaffCommandPayload(string TournamentId, L12TournamentStaffPayload Staff);
public sealed record TournamentRulingCommandPayload(string TournamentId, string MatchId,
    L12TournamentRulingPayload Ruling);
public sealed record TournamentRematchCommandPayload(string TournamentId, string MatchId,
    L12TournamentRematchPayload Rematch);
public sealed record TournamentReferenceCommandPayload(string TournamentId, string MatchId,
    L12TournamentMatchReferencePayload Reference);
public sealed record ReleaseDeployRequest(string? ArtifactId, string? Environment,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
public sealed record ReleaseRollbackRequest(string? TargetRunId,
    string? IdempotencyKey = null, long? ExpectedVersion = null, bool DryRun = false, string? Reason = null);
