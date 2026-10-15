using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwelveLegions.Server;

internal static class L12ChatStartup
{
    internal const string EnvironmentKey = "L12_CHAT_ENABLED";

    internal static bool Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim();
        if (string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)) return false;
        throw new InvalidOperationException($"{EnvironmentKey} 仅允许 true 或 false；修改后需重启服务。");
    }
}

internal sealed record L12ChatTextResult(string Text, bool Moderated, string PolicyVersion);

internal static class L12ChatTextPolicy
{
    internal const int MaximumGraphemes = 100;
    internal const int MaximumUtf8Bytes = 512;
    internal const string Version = "chat-v1-20261016";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static bool TryApply(string? source, out L12ChatTextResult result, out string error)
    {
        result = null!;
        error = string.Empty;
        if (source is null)
        {
            error = "消息正文不能为空";
            return false;
        }
        try
        {
            if (StrictUtf8.GetByteCount(source) > MaximumUtf8Bytes)
            {
                error = "消息最多512字节";
                return false;
            }
        }
        catch (EncoderFallbackException)
        {
            error = "消息包含无效字符";
            return false;
        }
        if (StringInfo.ParseCombiningCharacters(source).Length > MaximumGraphemes)
        {
            error = "消息最多100字";
            return false;
        }

        var moderated = false;
        var builder = new StringBuilder(source.Length);
        foreach (var character in source.Normalize(NormalizationForm.FormKC))
        {
            if (character is '\u200B' or '\u200C' or '\u200D' or '\u2060' or '\uFEFF')
            {
                moderated = true;
                continue;
            }
            if (char.IsControl(character) && character is not ('\r' or '\n' or '\t'))
            {
                error = "消息包含不允许的控制字符";
                return false;
            }
            if (character == '\r')
            {
                moderated = true;
                if (builder.Length == 0 || builder[^1] != '\n') builder.Append('\n');
                continue;
            }
            if (character == '\t')
            {
                moderated = true;
                builder.Append(' ');
                continue;
            }
            builder.Append(character);
        }
        var normalized = builder.ToString().Trim();
        moderated |= !string.Equals(normalized, source, StringComparison.Ordinal);
        if (normalized.Length == 0)
        {
            error = "消息正文不能为空";
            return false;
        }
        if (StringInfo.ParseCombiningCharacters(normalized).Length > MaximumGraphemes
            || StrictUtf8.GetByteCount(normalized) > MaximumUtf8Bytes)
        {
            error = "消息最多100字且不超过512字节";
            return false;
        }
        var compact = CompactModerationText(normalized, out var sourceIndices);
        if (new[] { "儿童色情", "出售外挂", "售卖外挂", "买卖毒品" }
            .Any(phrase => compact.Contains(phrase, StringComparison.Ordinal)))
        {
            error = "消息包含不允许的伤害或交易内容";
            return false;
        }
        var spans = new List<(int Start, int Length)>();
        foreach (var phrase in new[] { "傻逼", "傻比", "操你妈" })
            AddModerationSpans(normalized, compact, sourceIndices, phrase, latinBoundary: false, spans);
        AddModerationSpans(normalized, compact, sourceIndices, "fuck", latinBoundary: true, spans);
        if (spans.Count != 0)
        {
            var masked = new StringBuilder(normalized);
            foreach (var span in spans.OrderByDescending(item => item.Start))
                masked.Remove(span.Start, span.Length).Insert(span.Start, "****");
            normalized = masked.ToString();
            moderated = true;
        }
        result = new(normalized, moderated, Version);
        return true;
    }

    private static string CompactModerationText(string text, out int[] sourceIndices)
    {
        var compact = new StringBuilder(text.Length);
        var indices = new List<int>(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = char.ToLowerInvariant(text[index]);
            if (!char.IsLetterOrDigit(character)) continue;
            compact.Append(character);
            indices.Add(index);
        }
        sourceIndices = indices.ToArray();
        return compact.ToString();
    }

    private static void AddModerationSpans(string source, string compact, int[] sourceIndices,
        string phrase, bool latinBoundary, List<(int Start, int Length)> spans)
    {
        var offset = 0;
        while (offset <= compact.Length - phrase.Length)
        {
            var found = compact.IndexOf(phrase, offset, StringComparison.Ordinal);
            if (found < 0) return;
            var sourceStart = sourceIndices[found];
            var sourceEnd = sourceIndices[found + phrase.Length - 1] + 1;
            // Separators may appear inside the obfuscated token, but they are also real word
            // boundaries outside it. Inspect the normalized source rather than the compacted
            // string so `f.u.c.k` and `fuck-you` match while `unfucking` does not.
            var boundary = !latinBoundary
                           || (sourceStart == 0 || !IsAsciiWord(source[sourceStart - 1]))
                           && (sourceEnd == source.Length || !IsAsciiWord(source[sourceEnd]));
            if (boundary)
            {
                if (!spans.Any(span => sourceStart < span.Start + span.Length
                                       && span.Start < sourceEnd))
                    spans.Add((sourceStart, sourceEnd - sourceStart));
            }
            offset = found + phrase.Length;
        }
    }

    private static bool IsAsciiWord(char value)
        => value is >= 'a' and <= 'z' or >= '0' and <= '9' or '_';

    internal static string Fingerprint(string scope, string peerAccountId, string? matchId, string source)
    {
        var payload = StrictUtf8.GetBytes(string.Concat(scope, "\n", peerAccountId, "\n",
            matchId ?? string.Empty, "\n", source));
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }
}

internal readonly record struct L12ChatRequestIdentity(Guid SessionId, string PlatformSessionId,
    string AccountId, long ConnectionGeneration);

internal enum L12ChatDeliveryScope { Legacy, Control, Friend, Opponent }

internal sealed record L12ChatDeliveryMetadata(
    string PairFirstId,
    string PairSecondId,
    L12ChatDeliveryScope Scope,
    string SenderAccountId,
    string RecipientAccountId,
    string? MatchId,
    long ScopeGeneration,
    L12ChatRequestIdentity Transport,
    L12ChatRequestIdentity Origin)
{
    internal static L12ChatDeliveryMetadata Legacy(string senderId, string recipientId)
        => new(senderId, recipientId, L12ChatDeliveryScope.Legacy, senderId, recipientId,
            null, 0, default, default);
}

internal sealed record L12ChatFrame(L12ChatRequestIdentity Transport,
    L12ChatDeliveryMetadata Metadata, object Payload);

internal sealed class L12ChatRuntime : IAsyncDisposable
{
    private const int MaximumQueuedWorkItems = 256;
    private const long MaximumQueuedWorkBytes = 1024 * 1024;
    private const int WorkMetadataBytes = 768;
    private const int MaximumQueuedWorkItemsPerAccount = 8;
    private const long MaximumQueuedWorkBytesPerAccount = 64 * 1024;
    private const int MaximumHistoryWireBytes = 64 * 1024;
    private const int MaximumOpponentConversations = 256;
    private const int MaximumOpponentEntriesPerConversation = 100;
    private const int MaximumOpponentRetainedItems = 4_096;
    private const long MaximumOpponentRetainedBytes = 4L * 1024 * 1024;
    private const int MaximumOpponentReceiptsPerConversation = 512;
    private const int MaximumOpponentReceipts = 8_192;
    private const long MaximumOpponentReceiptBytes = 4L * 1024 * 1024;
    private static readonly JsonSerializerOptions ChatWireJsonOptions = new(JsonSerializerDefaults.Web);

    private enum WorkKind { Send, Sync, Read }
    private sealed record Work(WorkKind Kind, L12ChatRequestIdentity Identity, string RequestId,
        string Scope, string PeerAccountId, string? MatchId,
        L12ChatTextResult? Text, string? Fingerprint, long? BeforeSeq, long ReadSeq,
        DateTimeOffset RequestIssuedAt, long Charge);
    private sealed class AccountWorkUsage
    {
        internal int Items;
        internal long Bytes;
    }
    private sealed record HistoryPayload(string Type, string RequestId, string Scope,
        string PeerAccountId, string? MatchId, IReadOnlyList<L12ChatEntry> Entries, bool HasMore);
    private sealed record OpponentItem(L12ChatEntry Entry, long Charge);
    private sealed record OpponentReceipt(string Fingerprint, string EntryId, long Sequence,
        DateTimeOffset ExpiresAt, long Charge);
    private sealed class OpponentConversation(long generation)
    {
        internal long Generation = generation;
        internal long NextSequence = 1;
        internal readonly LinkedList<OpponentItem> Entries = [];
        internal readonly Dictionary<string, OpponentReceipt> Requests = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, long> ReadCursors = new(StringComparer.Ordinal);
    }

    private readonly L12PlatformStore _platform;
    private readonly L12RoomManager _rooms;
    private readonly Func<L12ChatRequestIdentity, bool> _requestFresh;
    private readonly Func<string, L12ChatRequestIdentity?> _resolveRecipient;
    private readonly Func<L12ChatFrame, bool> _emit;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _workGate = new();
    private readonly Queue<Work> _work = [];
    private readonly Dictionary<string, AccountWorkUsage> _accountWork = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _workSignal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private readonly object _opponentGate = new();
    private readonly Dictionary<string, OpponentConversation> _opponent = new(StringComparer.Ordinal);
    private int _queuedWorkItems;
    private long _queuedWorkBytes;
    private int _opponentItems;
    private long _opponentBytes;
    private int _opponentReceipts;
    private long _opponentReceiptBytes;
    private bool _completed;

    internal L12ChatRuntime(L12PlatformStore platform, L12RoomManager rooms,
        Func<L12ChatRequestIdentity, bool> requestFresh,
        Func<string, L12ChatRequestIdentity?> resolveRecipient,
        Func<L12ChatFrame, bool> emit,
        Func<DateTimeOffset>? utcNow = null)
    {
        _platform = platform;
        _rooms = rooms;
        _requestFresh = requestFresh;
        _resolveRecipient = resolveRecipient;
        _emit = emit;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _rooms.OpponentChatScopeChanged += OnOpponentChatScopeChanged;
        _worker = Task.Run(RunAsync);
    }

    internal void QueueSend(L12ChatRequestIdentity identity, string? requestId, string? scope,
        string? peerAccountId, string? matchId, string? sourceText)
    {
        var normalizedScope = NormalizeScope(scope);
        if (!TryValidateEnvelope(identity, requestId, normalizedScope, peerAccountId, matchId,
                out var validatedRequest, out var validatedPeer, out var failure))
        {
            EmitSendFailure(identity, requestId, normalizedScope, peerAccountId, matchId,
                "invalidRequest", failure);
            return;
        }
        if (!L12ChatTextPolicy.TryApply(sourceText, out var text, out failure))
        {
            EmitSendFailure(identity, validatedRequest, normalizedScope, validatedPeer, matchId,
                "invalidText", failure);
            return;
        }
        if (!TryParseRequestIssuedAt(validatedRequest, _utcNow(), out var issuedAt, out failure))
        {
            EmitSendFailure(identity, validatedRequest, normalizedScope, validatedPeer, matchId,
                "invalidRequest", failure);
            return;
        }
        var fingerprint = L12ChatTextPolicy.Fingerprint(normalizedScope, validatedPeer,
            normalizedScope == "opponent" ? matchId : null, sourceText!);
        var work = new Work(WorkKind.Send, identity, validatedRequest, normalizedScope,
            validatedPeer, normalizedScope == "opponent" ? matchId : null, text,
            fingerprint, null, 0, issuedAt, WorkCharge(validatedRequest, normalizedScope, validatedPeer,
                matchId, text.Text, fingerprint));
        if (!TryQueue(work)) EmitSendFailure(identity, validatedRequest, normalizedScope,
            validatedPeer, matchId, "busy", "聊天队列正忙，请使用原 requestId 重试");
    }

    internal void QueueSync(L12ChatRequestIdentity identity, string? requestId, string? scope,
        string? peerAccountId, string? matchId, long? beforeSeq)
    {
        var normalizedScope = NormalizeScope(scope);
        if (!TryValidateEnvelope(identity, requestId, normalizedScope, peerAccountId, matchId,
                out var validatedRequest, out var validatedPeer, out var failure))
        {
            EmitHistoryFailure(identity, requestId, normalizedScope, peerAccountId, matchId,
                "invalidRequest", failure);
            return;
        }
        if (beforeSeq is <= 0 or > L12PlatformStore.ChatMaximumSafeSequence)
        {
            EmitHistoryFailure(identity, validatedRequest, normalizedScope, validatedPeer, matchId,
                "invalidRequest", "历史游标无效");
            return;
        }
        if (!TryParseRequestIssuedAt(validatedRequest, _utcNow(), out _, out failure))
        {
            EmitHistoryFailure(identity, validatedRequest, normalizedScope, validatedPeer, matchId,
                "invalidRequest", failure);
            return;
        }
        var work = new Work(WorkKind.Sync, identity, validatedRequest, normalizedScope,
            validatedPeer, normalizedScope == "opponent" ? matchId : null, null, null,
            beforeSeq, 0, default, WorkCharge(validatedRequest, normalizedScope, validatedPeer, matchId));
        if (!TryQueue(work)) EmitHistoryFailure(identity, validatedRequest, normalizedScope,
            validatedPeer, matchId, "busy", "聊天队列正忙，请稍后重试");
    }

    internal void QueueRead(L12ChatRequestIdentity identity, string? requestId, string? scope,
        string? peerAccountId, string? matchId, long seq)
    {
        var normalizedScope = NormalizeScope(scope);
        if (!TryValidateEnvelope(identity, requestId, normalizedScope, peerAccountId, matchId,
                out var validatedRequest, out var validatedPeer, out var failure))
        {
            EmitReadFailure(identity, requestId, normalizedScope, peerAccountId, matchId,
                "invalidRequest", failure);
            return;
        }
        if (seq is <= 0 or > L12PlatformStore.ChatMaximumSafeSequence)
        {
            EmitReadFailure(identity, validatedRequest, normalizedScope, validatedPeer, matchId,
                "invalidRequest", "已读游标无效");
            return;
        }
        if (!TryParseRequestIssuedAt(validatedRequest, _utcNow(), out _, out failure))
        {
            EmitReadFailure(identity, validatedRequest, normalizedScope, validatedPeer, matchId,
                "invalidRequest", failure);
            return;
        }
        var work = new Work(WorkKind.Read, identity, validatedRequest, normalizedScope,
            validatedPeer, normalizedScope == "opponent" ? matchId : null, null, null,
            null, seq, default, WorkCharge(validatedRequest, normalizedScope, validatedPeer, matchId));
        if (!TryQueue(work)) EmitReadFailure(identity, validatedRequest, normalizedScope,
            validatedPeer, matchId, "busy", "聊天队列正忙，请稍后重试");
    }

    private bool TryQueue(Work work)
    {
        lock (_workGate)
        {
            if (!_accountWork.TryGetValue(work.Identity.AccountId, out var account))
                account = new AccountWorkUsage();
            if (_completed || _queuedWorkItems >= MaximumQueuedWorkItems
                || work.Charge > MaximumQueuedWorkBytes - _queuedWorkBytes
                || account.Items >= MaximumQueuedWorkItemsPerAccount
                || work.Charge > MaximumQueuedWorkBytesPerAccount - account.Bytes) return false;
            _work.Enqueue(work);
            _queuedWorkItems++;
            _queuedWorkBytes += work.Charge;
            account.Items++;
            account.Bytes += work.Charge;
            _accountWork[work.Identity.AccountId] = account;
            _workSignal.Release();
            return true;
        }
    }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                await _workSignal.WaitAsync(_stop.Token);
                Work? item = null;
                lock (_workGate)
                {
                    if (_work.Count != 0)
                    {
                        item = _work.Dequeue();
                    }
                    else if (_completed) return;
                }
                if (item is null) continue;
                try { Process(item); }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"聊天低优先级工作隔离：{error.GetType().Name}");
                    EmitFailure(item, "unavailable", "聊天服务当前不可用");
                }
                finally { ReleaseWork(item); }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private void ReleaseWork(Work work)
    {
        lock (_workGate)
        {
            ReleaseWorkLocked(work);
        }
    }

    private void ReleaseWorkLocked(Work work)
    {
        _queuedWorkItems--;
        _queuedWorkBytes -= work.Charge;
        if (!_accountWork.TryGetValue(work.Identity.AccountId, out var account)) return;
        account.Items--;
        account.Bytes -= work.Charge;
        if (account.Items == 0) _accountWork.Remove(work.Identity.AccountId);
    }

    private void Process(Work work)
    {
        if (!_requestFresh(work.Identity)) return;
        try
        {
            switch (work.Kind)
            {
                case WorkKind.Send: ProcessSend(work); break;
                case WorkKind.Sync: ProcessSync(work); break;
                case WorkKind.Read: ProcessRead(work); break;
            }
        }
        catch (L12ChatOperationException error)
        {
            EmitFailure(work, error.Code, error.Message);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private void ProcessSend(Work work)
    {
        var now = _utcNow();
        if (work.RequestIssuedAt > now.AddMinutes(L12PlatformStore.ChatRequestFutureSkewMinutes)
            || work.RequestIssuedAt.AddHours(L12PlatformStore.FriendChatRetentionHours) <= now)
            throw new L12ChatOperationException("invalidRequest", "requestId 已过期或时间无效");
        L12StoredChatWrite stored;
        long scopeGeneration = 0;
        if (work.Scope == "friend")
        {
            stored = _platform.AppendCommittedFriendChat(work.Identity.AccountId, work.PeerAccountId,
                work.RequestId, work.Fingerprint!, work.Text!, _utcNow(),
                work.RequestIssuedAt, () => _requestFresh(work.Identity), _stop.Token);
        }
        else
        {
            stored = AppendOpponent(work, out scopeGeneration);
        }
        if (!_requestFresh(work.Identity)) return;
        EmitScoped(work.Identity, work.Identity, work.Scope, work.Identity.AccountId, work.PeerAccountId,
            work.MatchId, scopeGeneration, new
            {
                type = "chatAck", requestId = work.RequestId, success = true,
                stored.Duplicate, messageId = stored.MessageId, seq = stored.Sequence,
                entry = stored.Entry,
            });
        if (stored.Duplicate || stored.Entry is null) return;
        var recipient = _resolveRecipient(work.PeerAccountId);
        if (recipient is not { } target) return;
        EmitScoped(target, work.Identity, work.Scope, work.Identity.AccountId, work.PeerAccountId,
            work.MatchId, scopeGeneration, new { type = "chatMessage", entry = stored.Entry });
    }

    private void ProcessSync(Work work)
    {
        L12ChatHistoryPage page;
        long scopeGeneration = 0;
        if (work.Scope == "friend")
            page = _platform.ReadCommittedFriendChatHistory(work.Identity.AccountId,
                work.PeerAccountId, work.BeforeSeq, _utcNow(), _stop.Token);
        else
            page = ReadOpponentHistory(work, out scopeGeneration);
        if (!_requestFresh(work.Identity)) return;
        var payload = FitHistoryPayload(work, page);
        EmitScoped(work.Identity, work.Identity, work.Scope, work.Identity.AccountId,
            work.PeerAccountId, work.MatchId, scopeGeneration, payload);
    }

    private void ProcessRead(Work work)
    {
        long accepted;
        long scopeGeneration = 0;
        if (work.Scope == "friend")
            accepted = _platform.WriteCommittedFriendChatRead(work.Identity.AccountId,
                work.PeerAccountId, work.ReadSeq, _utcNow(), () => _requestFresh(work.Identity),
                _stop.Token);
        else
            accepted = WriteOpponentRead(work, out scopeGeneration);
        if (!_requestFresh(work.Identity)) return;
        EmitScoped(work.Identity, work.Identity, work.Scope, work.Identity.AccountId, work.PeerAccountId,
            work.MatchId, scopeGeneration, new
            {
                type = "chatReadAck", requestId = work.RequestId, success = true, seq = accepted,
            });
    }

    private static HistoryPayload FitHistoryPayload(Work work, L12ChatHistoryPage page)
    {
        var empty = Array.Empty<L12ChatEntry>();
        var trueBase = JsonSerializer.SerializeToUtf8Bytes(new HistoryPayload("chatHistory",
            work.RequestId, work.Scope, work.PeerAccountId, work.MatchId, empty, true),
            ChatWireJsonOptions).Length - 2; // Replace the serialized [] with exact entry bytes.
        var falseBase = JsonSerializer.SerializeToUtf8Bytes(new HistoryPayload("chatHistory",
            work.RequestId, work.Scope, work.PeerAccountId, work.MatchId, empty, false),
            ChatWireJsonOptions).Length - 2;
        if (page.Entries.Count == 0)
            return new("chatHistory", work.RequestId, work.Scope, work.PeerAccountId,
                work.MatchId, empty, page.HasMore);

        var serializedEntryBytes = page.Entries.Select(entry =>
            JsonSerializer.SerializeToUtf8Bytes(entry, ChatWireJsonOptions).Length).ToArray();
        var fullEntryBytes = serializedEntryBytes.Sum(value => (long)value)
                             + Math.Max(0, serializedEntryBytes.Length - 1);
        if (!page.HasMore && falseBase + fullEntryBytes <= MaximumHistoryWireBytes)
            return new("chatHistory", work.RequestId, work.Scope, work.PeerAccountId,
                work.MatchId, page.Entries, false);

        var newestFirst = new List<L12ChatEntry>(page.Entries.Count);
        long retainedBytes = 0;
        for (var index = page.Entries.Count - 1; index >= 0; index--)
        {
            var entryBytes = serializedEntryBytes[index];
            var addition = entryBytes + (newestFirst.Count == 0 ? 0 : 1);
            if (trueBase + retainedBytes + addition > MaximumHistoryWireBytes) break;
            newestFirst.Add(page.Entries[index]);
            retainedBytes += addition;
        }
        if (newestFirst.Count == 0)
            throw new L12ChatOperationException("quota", "单条聊天历史超过发送上限");

        newestFirst.Reverse();
        return new("chatHistory", work.RequestId, work.Scope, work.PeerAccountId,
            work.MatchId, newestFirst, true);
    }

    private L12StoredChatWrite AppendOpponent(Work work, out long generation)
    {
        var pair = RequireOpponentAdmission(work, out var scope);
        generation = scope.ScopeGeneration;
        var requestKey = string.Concat(work.Identity.AccountId, "\0", work.RequestId);
        lock (_opponentGate)
        {
            var currentScope = _rooms.ReadCommittedOpponentChatScope(work.Identity.AccountId,
                work.PeerAccountId, work.MatchId);
            if (!ReferenceEquals(currentScope, scope))
                throw new L12ChatOperationException("scopeUnavailable", "本局聊天范围已变化");
            if (!_opponent.TryGetValue(scope.MatchId, out var conversation))
            {
                if (_opponent.Count >= MaximumOpponentConversations)
                    throw new L12ChatOperationException("quota", "本局聊天缓存额度暂时不足", true);
                conversation = new(scope.ScopeGeneration);
                _opponent.Add(scope.MatchId, conversation);
            }
            if (conversation.Generation != scope.ScopeGeneration)
            {
                RemoveOpponentConversationLocked(scope.MatchId, conversation);
                conversation = new(scope.ScopeGeneration);
                _opponent.Add(scope.MatchId, conversation);
            }
            var now = _utcNow();
            PruneExpiredOpponentReceiptsLocked(conversation, now);
            if (conversation.Requests.TryGetValue(requestKey, out var duplicate))
            {
                if (!string.Equals(duplicate.Fingerprint, work.Fingerprint, StringComparison.Ordinal))
                    throw new L12ChatOperationException("requestConflict",
                        "相同 requestId 已用于不同的聊天请求");
                var retained = conversation.Entries.FirstOrDefault(item =>
                    string.Equals(item.Entry.Id, duplicate.EntryId, StringComparison.Ordinal))?.Entry;
                return new(retained, true, duplicate.EntryId, duplicate.Sequence);
            }
            var receiptCharge = WorkMetadataBytes + Encoding.UTF8.GetByteCount(requestKey)
                                + Encoding.UTF8.GetByteCount(work.Fingerprint!);
            if (_opponentReceipts >= MaximumOpponentReceipts
                || receiptCharge > MaximumOpponentReceiptBytes - _opponentReceiptBytes)
                PruneExpiredOpponentReceiptsAcrossConversationsLocked(now, 128);
            if (conversation.Requests.Count >= MaximumOpponentReceiptsPerConversation
                || _opponentReceipts >= MaximumOpponentReceipts
                || receiptCharge > MaximumOpponentReceiptBytes - _opponentReceiptBytes)
                throw new L12ChatOperationException("quota", "本局聊天请求回执额度暂时不足", true);
            var sequence = conversation.NextSequence;
            if (sequence is <= 0 or > L12PlatformStore.ChatMaximumSafeSequence)
                throw new L12ChatOperationException("unavailable", "本局聊天序号不可用", true);
            var entry = new L12ChatEntry(Guid.NewGuid().ToString("N"), sequence, "opponent",
                scope.MatchId, work.Identity.AccountId, pair.SenderName, work.PeerAccountId,
                work.Text!.Text, _utcNow().ToUniversalTime(), work.Text.Moderated,
                work.Text.PolicyVersion);
            var charge = WorkMetadataBytes + Encoding.UTF8.GetByteCount(entry.Text)
                         + Encoding.UTF8.GetByteCount(entry.SenderName) + entry.Id.Length * 2L;
            var victim = conversation.Entries.Count >= MaximumOpponentEntriesPerConversation
                ? conversation.Entries.First?.Value : null;
            var prospectiveItems = _opponentItems - (victim is null ? 0 : 1);
            var prospectiveBytes = _opponentBytes - (victim?.Charge ?? 0);
            if (prospectiveItems >= MaximumOpponentRetainedItems
                || charge > MaximumOpponentRetainedBytes - prospectiveBytes)
                throw new L12ChatOperationException("quota", "本局聊天缓存额度暂时不足", true);
            if (victim is not null) RemoveOldestOpponentLocked(conversation);
            var item = new OpponentItem(entry, charge);
            var receipt = new OpponentReceipt(work.Fingerprint!, entry.Id, sequence,
                work.RequestIssuedAt.AddHours(L12PlatformStore.FriendChatRetentionHours),
                receiptCharge);
            conversation.NextSequence++;
            conversation.Entries.AddLast(item);
            conversation.Requests.Add(requestKey, receipt);
            _opponentItems++;
            _opponentBytes += charge;
            _opponentReceipts++;
            _opponentReceiptBytes += receiptCharge;
            return new(entry, false, entry.Id, entry.Seq);
        }
    }

    private L12ChatHistoryPage ReadOpponentHistory(Work work, out long generation)
    {
        _ = RequireOpponentAdmission(work, out var scope);
        generation = scope.ScopeGeneration;
        lock (_opponentGate)
        {
            if (!_opponent.TryGetValue(scope.MatchId, out var conversation)
                || conversation.Generation != scope.ScopeGeneration) return new([], false);
            var eligible = conversation.Entries.Select(item => item.Entry)
                .Where(entry => work.BeforeSeq is null || entry.Seq < work.BeforeSeq.Value)
                .OrderByDescending(entry => entry.Seq)
                .Take(L12PlatformStore.FriendChatMaximumHistoryPage + 1).ToArray();
            var hasMore = eligible.Length > L12PlatformStore.FriendChatMaximumHistoryPage;
            return new(eligible.Take(L12PlatformStore.FriendChatMaximumHistoryPage)
                .Reverse().ToArray(), hasMore);
        }
    }

    private long WriteOpponentRead(Work work, out long generation)
    {
        _ = RequireOpponentAdmission(work, out var scope);
        generation = scope.ScopeGeneration;
        lock (_opponentGate)
        {
            if (!_opponent.TryGetValue(scope.MatchId, out var conversation)
                || conversation.Generation != scope.ScopeGeneration) return 0;
            var maximum = conversation.Entries.Last?.Value.Entry.Seq ?? 0;
            var accepted = Math.Min(work.ReadSeq, maximum);
            if (accepted > 0)
                conversation.ReadCursors[work.Identity.AccountId] = Math.Max(
                    conversation.ReadCursors.GetValueOrDefault(work.Identity.AccountId), accepted);
            return accepted;
        }
    }

    private L12CommittedChatPairView RequireOpponentAdmission(Work work,
        out L12CommittedOpponentChatScope scope)
    {
        var pair = _platform.ReadCommittedChatPair(work.Identity.AccountId, work.PeerAccountId);
        if (!pair.Available)
            throw new L12ChatOperationException("unavailable", "平台已提交账号状态当前不可用", true);
        if (!pair.BothAccountsActive || !pair.Unblocked)
            throw new L12ChatOperationException("scopeUnavailable", "本局聊天当前不可用");
        scope = _rooms.ReadCommittedOpponentChatScope(work.Identity.AccountId,
                    work.PeerAccountId, work.MatchId)
                ?? throw new L12ChatOperationException("scopeUnavailable", "当前不在同一有效对局");
        if (!scope.ContainsConnection(work.Identity.AccountId, work.Identity.SessionId,
                work.Identity.ConnectionGeneration))
            throw new L12ChatOperationException("staleSession", "原发送连接已不再占用本局席位");
        return pair;
    }

    private void OnOpponentChatScopeChanged(string matchId, long generation, bool available)
    {
        lock (_opponentGate)
            if (_opponent.TryGetValue(matchId, out var conversation)
                && (!available || conversation.Generation != generation))
                RemoveOpponentConversationLocked(matchId, conversation);
    }

    private void RemoveOpponentConversationLocked(string matchId, OpponentConversation conversation)
    {
        if (!_opponent.Remove(matchId, out var removed) || !ReferenceEquals(removed, conversation)) return;
        foreach (var item in conversation.Entries)
        {
            _opponentItems--;
            _opponentBytes -= item.Charge;
        }
        foreach (var receipt in conversation.Requests.Values)
        {
            _opponentReceipts--;
            _opponentReceiptBytes -= receipt.Charge;
        }
        conversation.Entries.Clear();
        conversation.Requests.Clear();
        conversation.ReadCursors.Clear();
    }

    private void RemoveOldestOpponentLocked(OpponentConversation conversation)
    {
        if (conversation.Entries.First is not { } first) return;
        conversation.Entries.RemoveFirst();
        _opponentItems--;
        _opponentBytes -= first.Value.Charge;
    }

    private void PruneExpiredOpponentReceiptsLocked(OpponentConversation conversation,
        DateTimeOffset now)
    {
        foreach (var request in conversation.Requests
                     .Where(item => item.Value.ExpiresAt <= now).Take(32).ToArray())
        {
            if (!conversation.Requests.Remove(request.Key, out var removed)) continue;
            _opponentReceipts--;
            _opponentReceiptBytes -= removed.Charge;
        }
    }

    private void PruneExpiredOpponentReceiptsAcrossConversationsLocked(DateTimeOffset now,
        int maximum)
    {
        var remaining = maximum;
        foreach (var conversation in _opponent.Values)
        {
            if (remaining == 0) return;
            var expired = conversation.Requests.Where(item => item.Value.ExpiresAt <= now)
                .Take(remaining).Select(item => item.Key).ToArray();
            foreach (var key in expired)
            {
                if (!conversation.Requests.Remove(key, out var removed)) continue;
                _opponentReceipts--;
                _opponentReceiptBytes -= removed.Charge;
                remaining--;
            }
        }
    }

    private void EmitFailure(Work work, string code, string message)
    {
        if (!_requestFresh(work.Identity)) return;
        switch (work.Kind)
        {
            case WorkKind.Send:
                EmitSendFailure(work.Identity, work.RequestId, work.Scope, work.PeerAccountId,
                    work.MatchId, code, message);
                break;
            case WorkKind.Sync:
                EmitHistoryFailure(work.Identity, work.RequestId, work.Scope, work.PeerAccountId,
                    work.MatchId, code, message);
                break;
            case WorkKind.Read:
                EmitReadFailure(work.Identity, work.RequestId, work.Scope, work.PeerAccountId,
                    work.MatchId, code, message);
                break;
        }
    }

    private void EmitSendFailure(L12ChatRequestIdentity identity, string? requestId, string? scope,
        string? peerAccountId, string? matchId, string code, string message)
        => EmitControl(identity, peerAccountId, new
        {
            type = "chatAck", requestId, success = false, code, message,
        });

    private void EmitHistoryFailure(L12ChatRequestIdentity identity, string? requestId, string? scope,
        string? peerAccountId, string? matchId, string code, string message)
        => EmitControl(identity, peerAccountId, new
        {
            type = "chatHistory", requestId, scope, peerAccountId, matchId,
            success = false, code, message, entries = Array.Empty<L12ChatEntry>(), hasMore = false,
        });

    private void EmitReadFailure(L12ChatRequestIdentity identity, string? requestId, string? scope,
        string? peerAccountId, string? matchId, string code, string message)
        => EmitControl(identity, peerAccountId, new
        {
            type = "chatReadAck", requestId, success = false, code, message, seq = 0,
        });

    private void EmitControl(L12ChatRequestIdentity identity, string? peerAccountId, object payload)
    {
        if (!_requestFresh(identity)) return;
        var second = string.IsNullOrWhiteSpace(peerAccountId) || peerAccountId == identity.AccountId
            ? string.Concat(identity.AccountId, "\0chat-control") : peerAccountId;
        _emit(new(identity, new(identity.AccountId, second, L12ChatDeliveryScope.Control,
            identity.AccountId, second, null, 0, identity, identity), payload));
    }

    private void EmitScoped(L12ChatRequestIdentity transport, L12ChatRequestIdentity origin,
        string scope, string senderId,
        string recipientId, string? matchId, long generation, object payload)
    {
        var kind = scope == "friend" ? L12ChatDeliveryScope.Friend : L12ChatDeliveryScope.Opponent;
        _emit(new(transport, new(senderId, recipientId, kind, senderId, recipientId,
            matchId, generation, transport, origin), payload));
    }

    private static bool TryValidateEnvelope(L12ChatRequestIdentity identity, string? requestId,
        string scope, string? peerAccountId, string? matchId, out string validRequest,
        out string validPeer, out string failure)
    {
        validRequest = (requestId ?? string.Empty).Trim();
        validPeer = (peerAccountId ?? string.Empty).Trim();
        failure = string.Empty;
        if (validRequest.Length is < 1 or > 128 || validRequest.Any(char.IsControl))
            failure = "requestId 无效";
        else if (scope is not ("friend" or "opponent")) failure = "聊天范围无效";
        else if (validPeer.Length is < 1 or > 128 || validPeer.Any(char.IsControl)
                 || validPeer == identity.AccountId) failure = "聊天对象无效";
        else if (scope == "opponent" && (string.IsNullOrWhiteSpace(matchId)
                 || matchId.Length > 128 || matchId.Any(char.IsControl))) failure = "对局标识无效";
        else return true;
        return false;
    }

    private static string NormalizeScope(string? scope) => (scope ?? string.Empty).Trim().ToLowerInvariant();

    private static bool TryParseRequestIssuedAt(string requestId, DateTimeOffset now,
        out DateTimeOffset issuedAt, out string failure)
    {
        issuedAt = default;
        failure = "requestId 必须为 c1.<unixMillis>.<uuid>";
        var parts = requestId.Split('.', 3, StringSplitOptions.None);
        if (parts.Length != 3 || parts[0] != "c1"
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            || !Guid.TryParseExact(parts[2], "D", out _)) return false;
        try { issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds); }
        catch (ArgumentOutOfRangeException) { return false; }
        if (issuedAt > now.AddMinutes(L12PlatformStore.ChatRequestFutureSkewMinutes))
        {
            failure = "requestId 时间超出服务器允许偏差";
            return false;
        }
        if (issuedAt.AddHours(L12PlatformStore.FriendChatRetentionHours) <= now)
        {
            failure = "requestId 已过期";
            return false;
        }
        failure = string.Empty;
        return true;
    }

    private static long WorkCharge(params string?[] values)
        => WorkMetadataBytes + values.Where(value => value is not null)
            .Sum(value => 24L + 2L * value!.Length);

    public async ValueTask DisposeAsync()
    {
        _rooms.OpponentChatScopeChanged -= OnOpponentChatScopeChanged;
        lock (_workGate)
        {
            if (!_completed)
            {
                _completed = true;
                while (_work.TryDequeue(out var queued)) ReleaseWorkLocked(queued);
                _workSignal.Release();
            }
        }
        _stop.Cancel();
        try { await _worker; }
        catch (OperationCanceledException) { }
        lock (_opponentGate)
        {
            _opponent.Clear();
            _opponentItems = 0;
            _opponentBytes = 0;
            _opponentReceipts = 0;
            _opponentReceiptBytes = 0;
        }
        _workSignal.Dispose();
        _stop.Dispose();
    }
}

/// <summary>
/// Bounded account lifecycle fence. The gate protects only generation flags and the synchronous
/// transport kickoff call; no SQLite/platform lock or await is allowed while it is held.
/// </summary>
internal sealed class L12ChatAccountRegistry
{
    internal sealed class AccountEntry(string accountId, long charge)
    {
        internal readonly string AccountId = accountId;
        internal readonly long Charge = charge;
        internal long Generation;
        internal int Pending;
        internal int References;
        internal bool RecoveryClosed;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, AccountEntry> _entries = new(StringComparer.Ordinal);
    private readonly int _maximumEntries;
    private readonly long _maximumBytes;
    private long _bytes;

    internal L12ChatAccountRegistry(int maximumEntries, long maximumBytes)
    {
        if (maximumEntries <= 0 || maximumBytes < 256) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        _maximumEntries = maximumEntries;
        _maximumBytes = maximumBytes;
    }

    internal AccountPairLease? TryAcquire(string firstId, string secondId)
    {
        if (string.IsNullOrWhiteSpace(firstId) || string.IsNullOrWhiteSpace(secondId)
            || firstId == secondId) return null;
        if (string.CompareOrdinal(firstId, secondId) > 0) (firstId, secondId) = (secondId, firstId);
        lock (_gate)
        {
            var first = TryGetOrCreateLocked(firstId);
            if (first is null) return null;
            first.References++;
            var second = TryGetOrCreateLocked(secondId);
            if (second is null)
            {
                ReleaseLocked(first);
                return null;
            }
            second.References++;
            return new(this, first, second, first.Generation, second.Generation);
        }
    }

    internal MutationLease? TryBeginMutation(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return null;
        lock (_gate)
        {
            var entry = TryGetOrCreateLocked(accountId);
            if (entry is null || entry.Pending == int.MaxValue) return null;
            entry.References++;
            entry.Pending++;
            return new(this, entry);
        }
    }

    internal bool Ready(AccountPairLease lease)
    {
        lock (_gate)
        {
            if (lease.StaleLocked()) return true;
            return lease.First.Pending == 0 && lease.Second.Pending == 0
                && !lease.First.RecoveryClosed && !lease.Second.RecoveryClosed;
        }
    }

    internal L12ChatQueueResult Start(AccountPairLease lease, Func<L12ChatQueueResult> kickoff)
    {
        lock (_gate)
        {
            if (lease.StaleLocked()) return L12ChatQueueResult.Suppressed;
            if (lease.First.Pending != 0 || lease.Second.Pending != 0
                || lease.First.RecoveryClosed || lease.Second.RecoveryClosed)
                return L12ChatQueueResult.Paused;
            return kickoff();
        }
    }

    private AccountEntry? TryGetOrCreateLocked(string accountId)
    {
        if (_entries.TryGetValue(accountId, out var existing)) return existing;
        var charge = 192L + 2L * accountId.Length;
        if (_entries.Count >= _maximumEntries || charge > _maximumBytes - _bytes) return null;
        var entry = new AccountEntry(accountId, charge);
        _entries.Add(accountId, entry);
        _bytes += charge;
        return entry;
    }

    private void Release(AccountEntry entry)
    {
        lock (_gate) ReleaseLocked(entry);
    }

    private void ReleaseLocked(AccountEntry entry)
    {
        entry.References--;
        if (entry.References != 0 || entry.Pending != 0 || entry.RecoveryClosed) return;
        if (_entries.Remove(entry.AccountId, out var removed) && ReferenceEquals(removed, entry))
            _bytes -= entry.Charge;
    }

    internal sealed class AccountPairLease(L12ChatAccountRegistry owner, AccountEntry first,
        AccountEntry second, long firstGeneration, long secondGeneration) : IDisposable
    {
        private L12ChatAccountRegistry? _owner = owner;
        internal AccountEntry First { get; } = first;
        internal AccountEntry Second { get; } = second;
        internal bool StaleLocked()
            => First.Generation != firstGeneration || Second.Generation != secondGeneration;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current is null) return;
            current.Release(First);
            current.Release(Second);
        }
    }

    internal sealed class MutationLease(L12ChatAccountRegistry owner, AccountEntry entry) : IDisposable
    {
        private L12ChatAccountRegistry? _owner = owner;
        internal void Complete(L12ChatMutationOutcome outcome)
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current is null) return;
            lock (current._gate)
            {
                if (outcome == L12ChatMutationOutcome.Committed)
                {
                    if (entry.Generation == long.MaxValue) entry.RecoveryClosed = true;
                    else entry.Generation++;
                }
                else if (outcome != L12ChatMutationOutcome.KnownRollback) entry.RecoveryClosed = true;
                entry.Pending--;
                current.ReleaseLocked(entry);
            }
        }
        public void Dispose() => Complete(L12ChatMutationOutcome.Unknown);
    }
}
