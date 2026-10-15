using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;

namespace TwelveLegions.Server;

public sealed partial class L12WebSocketServer
{
    private const int ChatPerConnectionItems = 64;
    private const long ChatPerConnectionBytes = 512 * 1024;
    private const int ChatMaximumPacketBytes = 64 * 1024;
    private const int ChatMetadataBytes = 768;
    private static readonly long ChatFrameLifetimeTicks = 10 * Stopwatch.Frequency;

    private bool _chatEnabled;
    private L12ChatRuntime? _chatRuntime;
    private L12ChatAccountRegistry? _chatAccountRegistry;
    private L12ChatGlobalBudget? _chatGlobalBudget;

    private void InitializeChatRuntime(bool enabled)
    {
        _chatEnabled = enabled;
        if (!enabled) return;
        _chatMutationRegistry = new L12ChatPairRegistry(4_096, 4L * 1024 * 1024,
            1_024, 512L * 1024);
        _chatAccountRegistry = new L12ChatAccountRegistry(8_192, 4L * 1024 * 1024);
        _chatGlobalBudget = new L12ChatGlobalBudget(4_096, 16L * 1024 * 1024);
        _chatRuntime = new L12ChatRuntime(_platform, _rooms, IsChatRequestFresh,
            ResolveChatRecipient, TryEmitChatFrame);
    }

    private void ConfigureChatOutbound(Guid sessionId, WebSocket socket,
        L12OutboundConnection outbound)
    {
        if (!_chatEnabled || _chatMutationRegistry is null || _chatAccountRegistry is null
            || _chatGlobalBudget is null) return;
        outbound.ConfigureChatLane(
            new L12ChatLaneLimits(ChatPerConnectionItems, ChatPerConnectionBytes,
                ChatMaximumPacketBytes, ChatMetadataBytes),
            _chatGlobalBudget, _chatMutationRegistry, Stopwatch.GetTimestamp,
            FreshChatDelivery,
            (bytes, token) => StartPreparedTextSend(socket,
                bytes.Array!.AsMemory(bytes.Offset, bytes.Count), token),
            _chatAccountRegistry, _rooms.StartCommittedOpponentChatScope);
    }

    private IReadOnlyList<OutgoingMessage> QueueChatSend(Guid sessionId, JsonElement root)
    {
        if (!TryCaptureChatIdentity(sessionId, out var identity)) return [];
        _chatRuntime!.QueueSend(identity, GetString(root, "requestId"), GetString(root, "scope"),
            GetString(root, "peerAccountId"), GetString(root, "matchId"), GetString(root, "text"));
        return [];
    }

    private IReadOnlyList<OutgoingMessage> QueueChatSync(Guid sessionId, JsonElement root)
    {
        if (!TryCaptureChatIdentity(sessionId, out var identity)) return [];
        long? beforeSequence = null;
        if (root.TryGetProperty("beforeSeq", out var before))
            beforeSequence = before.ValueKind == JsonValueKind.Number && before.TryGetInt64(out var parsed)
                ? parsed : -1;
        _chatRuntime!.QueueSync(identity, GetString(root, "requestId"), GetString(root, "scope"),
            GetString(root, "peerAccountId"), GetString(root, "matchId"), beforeSequence);
        return [];
    }

    private IReadOnlyList<OutgoingMessage> QueueChatRead(Guid sessionId, JsonElement root)
    {
        if (!TryCaptureChatIdentity(sessionId, out var identity)) return [];
        var sequence = root.TryGetProperty("seq", out var value) && value.TryGetInt64(out var parsed)
            ? parsed : -1;
        _chatRuntime!.QueueRead(identity, GetString(root, "requestId"), GetString(root, "scope"),
            GetString(root, "peerAccountId"), GetString(root, "matchId"), sequence);
        return [];
    }

    private bool TryCaptureChatIdentity(Guid sessionId, out L12ChatRequestIdentity identity)
    {
        identity = default;
        if (!_chatEnabled || _chatRuntime is null
            || !_socketCapabilities.TryGetValue(sessionId, out var capabilities)
            || !capabilities.ChatV1
            || !_socketPlatformSessions.TryGetValue(sessionId, out var binding)) return false;
        identity = new(sessionId, binding.PlatformSessionId, binding.AccountId,
            binding.ConnectionGeneration);
        return IsChatRequestFresh(identity);
    }

    private bool IsChatRequestFresh(L12ChatRequestIdentity identity)
        => _socketPlatformSessions.TryGetValue(identity.SessionId, out var binding)
           && string.Equals(binding.PlatformSessionId, identity.PlatformSessionId,
               StringComparison.Ordinal)
           && string.Equals(binding.AccountId, identity.AccountId, StringComparison.Ordinal)
           && binding.ConnectionGeneration == identity.ConnectionGeneration
           && _activeAccountSockets.TryGetValue(identity.AccountId, out var active)
           && active == identity.SessionId
           && _platform.IsSessionActive(identity.PlatformSessionId)
           && _rooms.IsCurrentConnection(identity.SessionId, identity.AccountId,
               identity.ConnectionGeneration);

    private L12ChatRequestIdentity? ResolveChatRecipient(string accountId)
    {
        if (!_activeAccountSockets.TryGetValue(accountId, out var sessionId)
            || !_socketPlatformSessions.TryGetValue(sessionId, out var binding)) return null;
        var identity = new L12ChatRequestIdentity(sessionId, binding.PlatformSessionId,
            binding.AccountId, binding.ConnectionGeneration);
        return IsChatRequestFresh(identity) ? identity : null;
    }

    private bool TryEmitChatFrame(L12ChatFrame frame)
    {
        if (!_chatEnabled || !IsChatRequestFresh(frame.Transport)
            || !_outboundConnections.TryGetValue(frame.Transport.SessionId, out var outbound)) return false;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame.Payload, OutgoingJsonOptions);
        var expiresAt = Stopwatch.GetTimestamp() + ChatFrameLifetimeTicks;
        return outbound.TryEnqueueChat(frame.Metadata, new ArraySegment<byte>(bytes), expiresAt)
               == L12ChatQueueResult.AcceptedQueue;
    }

    private bool FreshChatDelivery(L12ChatDeliveryMetadata metadata)
    {
        if (!IsChatRequestFresh(metadata.Transport)) return false;
        if (metadata.Scope == L12ChatDeliveryScope.Control) return true;
        var pair = _platform.ReadCommittedChatPair(metadata.SenderAccountId,
            metadata.RecipientAccountId);
        if (!pair.Available || !pair.BothAccountsActive || !pair.Unblocked) return false;
        if (metadata.Scope == L12ChatDeliveryScope.Friend)
            return pair.AcceptedAndUnblocked;
        if (metadata.Scope != L12ChatDeliveryScope.Opponent
            || !IsChatRequestFresh(metadata.Origin)) return false;
        var scope = _rooms.ReadCommittedOpponentChatScope(metadata.SenderAccountId,
            metadata.RecipientAccountId, metadata.MatchId);
        return scope is not null && scope.ScopeGeneration == metadata.ScopeGeneration
               && scope.ContainsConnection(metadata.Origin.AccountId, metadata.Origin.SessionId,
                   metadata.Origin.ConnectionGeneration)
               && scope.ContainsConnection(metadata.Transport.AccountId, metadata.Transport.SessionId,
                   metadata.Transport.ConnectionGeneration);
    }

    private bool TryBeginChatAccountMutation(string accountId,
        out L12ChatAccountRegistry.MutationLease? mutation)
    {
        mutation = null;
        if (_chatAccountRegistry is null || string.IsNullOrWhiteSpace(accountId)) return true;
        mutation = _chatAccountRegistry.TryBeginMutation(accountId);
        return mutation is not null;
    }

    private void FinishChatAccountMutation(L12ChatAccountRegistry.MutationLease? mutation)
    {
        if (mutation is null) return;
        mutation.Dispose();
        foreach (var outbound in _outboundConnections.Values) outbound.WakeChatLane();
    }
}
