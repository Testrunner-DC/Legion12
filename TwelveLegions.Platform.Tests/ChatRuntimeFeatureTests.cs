using System.Collections.Concurrent;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class ChatRuntimeFeatureTests
{
    [Fact]
    public async Task ShutdownJoinsStartedChatCommitWithoutCancelingItAndDropsUnstartedWork()
    {
        using var fixture = new RankedIdentityFixture();
        var sender = fixture.Store.Register($"ss{Guid.NewGuid():N}"[..10], "Password123!").Account!;
        var recipient = fixture.Store.Register($"sr{Guid.NewGuid():N}"[..10], "Password123!").Account!;
        Assert.True(fixture.Store.SendFriendRequest(sender.Id, recipient.Id).Success);
        Assert.True(fixture.Store.ResolveFriendRequest(recipient.Id, sender.Id, true).Success);
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(fixture.Path)!, "shutdown-chat.db"));
        await recorder.InitializeAsync();
        var rooms = new L12RoomManager(fixture.Catalog, recorder, fixture.Store);
        var runtime = new L12ChatRuntime(fixture.Store, rooms, _ => true, _ => null, _ => true);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-chat-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("synthetic shutdown chat commit hold");
        };
        Task? stopping = null;
        try
        {
            Queue(runtime, sender.Id, recipient.Id, DateTimeOffset.UtcNow, 0);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            Queue(runtime, sender.Id, recipient.Id, DateTimeOffset.UtcNow, 1);
            stopping = runtime.DisposeAsync().AsTask();
            Assert.False(stopping.IsCompleted);
            release.Set();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            var history = fixture.Store.ReadCommittedFriendChatHistory(sender.Id, recipient.Id, null,
                DateTimeOffset.UtcNow, CancellationToken.None);
            var entry = Assert.Single(history.Entries);
            Assert.Equal("body-0", entry.Text);
            Assert.Equal(0, Field<int>(runtime, "_queuedWorkItems"));
            Assert.Equal(0, Field<long>(runtime, "_queuedWorkBytes"));
            Assert.Same(stopping, runtime.DisposeAsync().AsTask());
        }
        finally
        {
            release.Set();
            fixture.Store.StorageFailureInjector = null;
            if (stopping is null) await runtime.DisposeAsync();
            else await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData("管理员 GM 裁判 外挂是什么", "管理员 GM 裁判 外挂是什么", false)]
    [InlineData("你真傻\u200B逼", "你真****", true)]
    [InlineData("F.U.C.K-you", "****-you", true)]
    [InlineData("ｆ ｕ ｃ ｋ", "****", true)]
    [InlineData("unfucking", "unfucking", false)]
    public void IndependentTextPolicyNormalizesMasksAndKeepsEnglishBoundaries(string source,
        string expected, bool moderated)
    {
        Assert.True(L12ChatTextPolicy.TryApply(source, out var result, out var error), error);
        Assert.Equal(expected, result.Text);
        Assert.Equal(moderated, result.Moderated);
        Assert.Equal(L12ChatTextPolicy.Version, result.PolicyVersion);
    }

    [Theory]
    [InlineData("儿童色情")]
    [InlineData("出 售 外 挂")]
    [InlineData("售\u200B卖外挂")]
    [InlineData("买卖毒品")]
    public void IndependentTextPolicyRejectsFrozenHarmPhrasesAfterCompaction(string source)
    {
        Assert.False(L12ChatTextPolicy.TryApply(source, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void IndependentTextPolicyEnforcesBothGraphemeAndUtf8Limits()
    {
        Assert.False(L12ChatTextPolicy.TryApply(new string('a', 101), out _, out _));
        var byteHeavy = string.Concat(Enumerable.Repeat("a\u0301\u0301\u0301\u0301", 100));
        Assert.Equal(100, System.Globalization.StringInfo.ParseCombiningCharacters(byteHeavy).Length);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(byteHeavy) > 512);
        Assert.False(L12ChatTextPolicy.TryApply(byteHeavy, out _, out _));
    }

    [Fact]
    public async Task WorkQuotaIsSharedByAccountAcrossConnectionsAndHeldUntilProcessingFinally()
    {
        using var fixture = new RankedIdentityFixture();
        var sender = fixture.Store.Register($"qs{Guid.NewGuid():N}"[..10], "Password123!");
        var recipient = fixture.Store.Register($"qr{Guid.NewGuid():N}"[..10], "Password123!");
        Assert.True(sender.Success);
        Assert.True(recipient.Success);
        Assert.True(fixture.Store.SendFriendRequest(sender.Account!.Id, recipient.Account!.Id).Success);
        Assert.True(fixture.Store.ResolveFriendRequest(recipient.Account.Id, sender.Account.Id, true).Success);
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(fixture.Path)!,
            "chat-quota.db"));
        await recorder.InitializeAsync();
        var rooms = new L12RoomManager(fixture.Catalog, recorder, fixture.Store);
        var emitted = new ConcurrentQueue<JsonElement>();
        await using var runtime = new L12ChatRuntime(fixture.Store, rooms, _ => true, _ => null, frame =>
        {
            emitted.Enqueue(JsonSerializer.SerializeToElement(frame.Payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return true;
        });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        fixture.Store.StorageFailureInjector = stage =>
        {
            if (stage != "before-chat-commit") return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("quota hold");
        };
        try
        {
            var now = DateTimeOffset.UtcNow;
            Queue(runtime, sender.Account.Id, recipient.Account.Id, now, 0);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            for (var index = 1; index < 8; index++)
                Queue(runtime, sender.Account.Id, recipient.Account.Id, now.AddMilliseconds(index), index);

            Queue(runtime, sender.Account.Id, recipient.Account.Id, now.AddMilliseconds(8), 8);
            Assert.Contains(emitted, frame => String(frame, "type") == "chatAck"
                && String(frame, "code") == "busy");

            var beforeOther = emitted.Count;
            Queue(runtime, recipient.Account.Id, sender.Account.Id, now.AddMilliseconds(9), 9);
            Assert.Equal(beforeOther, emitted.Count);
        }
        finally
        {
            release.Set();
            fixture.Store.StorageFailureInjector = null;
        }
    }

    [Fact]
    public async Task SendSyncAndReadShareTheTimestampedRequestIdContract()
    {
        using var fixture = new RankedIdentityFixture();
        var sender = fixture.Store.Register($"is{Guid.NewGuid():N}"[..10], "Password123!").Account!;
        var recipient = fixture.Store.Register($"ir{Guid.NewGuid():N}"[..10], "Password123!").Account!;
        Assert.True(fixture.Store.SendFriendRequest(sender.Id, recipient.Id).Success);
        Assert.True(fixture.Store.ResolveFriendRequest(recipient.Id, sender.Id, true).Success);
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(fixture.Path)!,
            "chat-request-id.db"));
        await recorder.InitializeAsync();
        var rooms = new L12RoomManager(fixture.Catalog, recorder, fixture.Store);
        var emitted = new ConcurrentQueue<JsonElement>();
        await using var runtime = new L12ChatRuntime(fixture.Store, rooms, _ => true, _ => null, frame =>
        {
            emitted.Enqueue(JsonSerializer.SerializeToElement(frame.Payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return true;
        });
        var identity = new L12ChatRequestIdentity(Guid.NewGuid(), "session", sender.Id, 1);
        runtime.QueueSend(identity, "legacy-id", "friend", recipient.Id, null, "body");
        runtime.QueueSync(identity, "legacy-id", "friend", recipient.Id, null, null);
        runtime.QueueRead(identity, "legacy-id", "friend", recipient.Id, null, 1);
        Assert.Equal(3, emitted.Count(frame => String(frame, "code") == "invalidRequest"));
    }

    [Fact]
    public async Task FullOpponentConversationRejectsLargerItemAtomicallyUnderGlobalBytePressure()
    {
        using var fixture = new RankedIdentityFixture();
        var sender = fixture.Store.Register($"os{Guid.NewGuid():N}"[..10], "Password123!").Account!;
        var recipient = fixture.Store.Register($"or{Guid.NewGuid():N}"[..10], "Password123!").Account!;
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(fixture.Path)!,
            "chat-opponent-quota.db"));
        await recorder.InitializeAsync();
        var rooms = new L12RoomManager(fixture.Catalog, recorder, fixture.Store);
        const string matchId = "opponent-quota-match";
        var senderSession = Guid.NewGuid();
        var recipientSession = Guid.NewGuid();
        PublishScope(rooms, new L12CommittedOpponentChatScope(matchId, 1,
            sender.Id, senderSession, 1, recipient.Id, recipientSession, 1));
        var emitted = new ConcurrentQueue<JsonElement>();
        await using var runtime = new L12ChatRuntime(fixture.Store, rooms, _ => true, _ => null, frame =>
        {
            emitted.Enqueue(JsonSerializer.SerializeToElement(frame.Payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return true;
        });
        var identity = new L12ChatRequestIdentity(senderSession, "sender-session", sender.Id, 1);
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 100; index++)
        {
            var requestId = RequestId(now.AddMilliseconds(index));
            runtime.QueueSend(identity, requestId, "opponent", recipient.Id, matchId, "x");
            var ack = await TakeAsync(emitted, frame => String(frame, "type") == "chatAck"
                && String(frame, "requestId") == requestId);
            Assert.True(ack.GetProperty("success").GetBoolean());
        }

        var conversation = OpponentConversation(runtime, matchId);
        var entries = Entries(conversation);
        Assert.Equal(100, Count(entries));
        var firstBefore = FirstEntry(entries);
        var itemsBefore = Field<int>(runtime, "_opponentItems");
        const long pressuredBytes = 4L * 1024 * 1024 - 1;
        SetField(runtime, "_opponentBytes", pressuredBytes);

        var rejectedRequest = RequestId(now.AddSeconds(1));
        runtime.QueueSend(identity, rejectedRequest, "opponent", recipient.Id, matchId,
            new string('<', L12ChatTextPolicy.MaximumGraphemes));
        var rejected = await TakeAsync(emitted, frame => String(frame, "type") == "chatAck"
            && String(frame, "requestId") == rejectedRequest);
        Assert.False(rejected.GetProperty("success").GetBoolean());
        Assert.Equal("quota", String(rejected, "code"));
        Assert.Equal(100, Count(entries));
        Assert.Equal(firstBefore.Id, FirstEntry(entries).Id);
        Assert.Equal(firstBefore.Seq, FirstEntry(entries).Seq);
        Assert.Equal(itemsBefore, Field<int>(runtime, "_opponentItems"));
        Assert.Equal(pressuredBytes, Field<long>(runtime, "_opponentBytes"));

        var historyRequest = RequestId(now.AddSeconds(2));
        runtime.QueueSync(identity, historyRequest, "opponent", recipient.Id, matchId, null);
        var history = await TakeAsync(emitted, frame => String(frame, "type") == "chatHistory"
            && String(frame, "requestId") == historyRequest);
        var retained = history.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(100, retained.Length);
        Assert.False(history.GetProperty("hasMore").GetBoolean());
        Assert.Equal(firstBefore.Id, retained[0].GetProperty("id").GetString());
        Assert.Equal(firstBefore.Seq, retained[0].GetProperty("seq").GetInt64());
    }

    private static void Queue(L12ChatRuntime runtime, string senderId, string recipientId,
        DateTimeOffset issuedAt, int index)
    {
        var identity = new L12ChatRequestIdentity(Guid.NewGuid(), $"session-{index}", senderId, index + 1);
        runtime.QueueSend(identity, $"c1.{issuedAt.ToUnixTimeMilliseconds()}.{Guid.NewGuid():D}",
            "friend", recipientId, null, $"body-{index}");
    }

    private static string RequestId(DateTimeOffset issuedAt)
        => $"c1.{issuedAt.ToUnixTimeMilliseconds()}.{Guid.NewGuid():D}";

    private static void PublishScope(L12RoomManager rooms, L12CommittedOpponentChatScope scope)
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        var dictionary = typeof(L12RoomManager).GetField("_opponentChatScopes", privateInstance)!
            .GetValue(rooms)!;
        var slotType = typeof(L12RoomManager).GetNestedType("OpponentChatScopeSlot",
            BindingFlags.NonPublic)!;
        var slot = Activator.CreateInstance(slotType, nonPublic: true)!;
        slotType.GetField("Current", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(slot, scope);
        var added = (bool)dictionary.GetType().GetMethod("TryAdd")!
            .Invoke(dictionary, new[] { scope.MatchId, slot })!;
        Assert.True(added);
    }

    private static object OpponentConversation(L12ChatRuntime runtime, string matchId)
    {
        var dictionary = Assert.IsAssignableFrom<IDictionary>(typeof(L12ChatRuntime)
            .GetField("_opponent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime));
        var conversation = dictionary[matchId];
        Assert.NotNull(conversation);
        return conversation;
    }

    private static object Entries(object conversation)
        => conversation.GetType().GetField("Entries", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(conversation)!;

    private static int Count(object entries)
        => (int)entries.GetType().GetProperty("Count")!.GetValue(entries)!;

    private static L12ChatEntry FirstEntry(object entries)
    {
        var node = entries.GetType().GetProperty("First")!.GetValue(entries)!;
        var item = node.GetType().GetProperty("Value")!.GetValue(node)!;
        return (L12ChatEntry)item.GetType().GetProperty("Entry")!.GetValue(item)!;
    }

    private static T Field<T>(object owner, string name)
        => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(owner)!;

    private static void SetField(object owner, string name, object value)
        => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(owner, value);

    private static async Task<JsonElement> TakeAsync(ConcurrentQueue<JsonElement> queue,
        Func<JsonElement, bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            while (queue.TryDequeue(out var frame))
                if (predicate(frame)) return frame;
            await Task.Delay(5, deadline.Token);
        }
    }

    private static string? String(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}
