using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

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
