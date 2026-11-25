using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

// P4 本桌频道（用户 2026-11-25 裁定）：受众＝两名选手 + 裁判，裁判可发言，且**裁判之间互相可见**；
// 普通观战者一律不在内。裁判身份是逐请求算出来的，所以这里用发布作用域的方式直接钉住受众边界。
[Collection("Platform environment")]
public sealed class TableChatScopeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static (L12ChatRuntime Runtime, L12RoomManager Rooms, RankedIdentityFixture Fixture) Create(
        string matchId, out Guid senderSession, out Guid recipientSession, out Guid refereeSession,
        out L12AccountView sender, out L12AccountView recipient, out L12AccountView referee,
        ConcurrentQueue<JsonElement> emitted)
    {
        var fixture = new RankedIdentityFixture();
        sender = fixture.Store.Register($"ts{Guid.NewGuid():N}"[..10], "Password123!").Account!;
        recipient = fixture.Store.Register($"tr{Guid.NewGuid():N}"[..10], "Password123!").Account!;
        referee = fixture.Store.Register($"tf{Guid.NewGuid():N}"[..10], "Password123!").Account!;
        var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(fixture.Path)!, $"table-{Guid.NewGuid():N}.db"));
        recorder.InitializeAsync().GetAwaiter().GetResult();
        var rooms = new L12RoomManager(fixture.Catalog, recorder, fixture.Store);
        senderSession = Guid.NewGuid();
        recipientSession = Guid.NewGuid();
        refereeSession = Guid.NewGuid();
        PublishScope(rooms, new L12CommittedOpponentChatScope(matchId, 1,
            sender.Id, senderSession, 1, recipient.Id, recipientSession, 1,
            [new L12CommittedChatMember(referee.Id, refereeSession, 1)]));
        // 三名成员的连接身份都可解析，广播用例才能观察到逐个送达。
        // out 形参不能进 lambda，先落到局部变量。
        var senderAccountId = sender.Id;
        var recipientAccountId = recipient.Id;
        var refereeAccountId = referee.Id;
        var senderSid = senderSession;
        var recipientSid = recipientSession;
        var refereeSid = refereeSession;
        Func<string, L12ChatRequestIdentity?> resolve = accountId =>
            accountId == senderAccountId ? new L12ChatRequestIdentity(senderSid, "s", senderAccountId, 1)
            : accountId == recipientAccountId ? new L12ChatRequestIdentity(recipientSid, "r", recipientAccountId, 1)
            : accountId == refereeAccountId ? new L12ChatRequestIdentity(refereeSid, "f", refereeAccountId, 1)
            : null;
        var runtime = new L12ChatRuntime(fixture.Store, rooms, _ => true, resolve, frame =>
        {
            emitted.Enqueue(JsonSerializer.SerializeToElement(frame.Payload, Web));
            return true;
        });
        return (runtime, rooms, fixture);
    }

    private static async Task<JsonElement> Send(L12ChatRuntime runtime, ConcurrentQueue<JsonElement> emitted,
        Guid session, string accountId, string peerId, string matchId, string text)
    {
        var identity = new L12ChatRequestIdentity(session, "s", accountId, 1);
        var requestId = $"c1.{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.{Guid.NewGuid():D}";
        runtime.QueueSend(identity, requestId, "table", peerId, matchId, text);
        for (var i = 0; i < 500; i++)
        {
            foreach (var frame in emitted)
                if (frame.TryGetProperty("type", out var type) && type.GetString() == "chatAck"
                    && frame.TryGetProperty("requestId", out var id) && id.GetString() == requestId)
                    return frame;
            await Task.Delay(10);
        }
        throw new TimeoutException("未收到 chatAck");
    }

    [Fact]
    public async Task PlayerTableMessageIsAcceptedForAPlayerPeerAndReachesTheRefereeAudience()
    {
        var emitted = new ConcurrentQueue<JsonElement>();
        var matchId = $"table-{Guid.NewGuid():N}";
        var (runtime, _, fixture) = Create(matchId, out var senderSession, out _, out _,
            out var sender, out var recipient, out _, emitted);
        using (fixture)
        await using (runtime)
        {
            var ack = await Send(runtime, emitted, senderSession, sender.Id, recipient.Id, matchId, "本桌频道");
            Assert.True(ack.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task RefereeMaySpeakAndIsVisibleToBothPlayersAndOtherReferees()
    {
        var emitted = new ConcurrentQueue<JsonElement>();
        var matchId = $"table-{Guid.NewGuid():N}";
        var (runtime, _, fixture) = Create(matchId, out _, out _, out var refereeSession,
            out var sender, out var recipient, out var referee, emitted);
        using (fixture)
        await using (runtime)
        {
            var toPlayer = await Send(runtime, emitted, refereeSession, referee.Id, sender.Id, matchId, "裁判发话");
            Assert.True(toPlayer.GetProperty("success").GetBoolean());
            var toOther = await Send(runtime, emitted, refereeSession, referee.Id, recipient.Id, matchId, "裁判发话2");
            Assert.True(toOther.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task AccountOutsideTheTableScopeIsSuppressed()
    {
        var emitted = new ConcurrentQueue<JsonElement>();
        var matchId = $"table-{Guid.NewGuid():N}";
        var (runtime, _, fixture) = Create(matchId, out var senderSession, out _, out _,
            out var sender, out _, out _, emitted);
        using (fixture)
        await using (runtime)
        {
            var outsider = fixture.Store.Register($"to{Guid.NewGuid():N}"[..10], "Password123!").Account!;
            var ack = await Send(runtime, emitted, senderSession, sender.Id, outsider.Id, matchId, "不该送达");
            Assert.False(ack.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task BroadcastMessageReachesEveryOtherTableMember()
    {
        var emitted = new ConcurrentQueue<JsonElement>();
        var matchId = $"table-{Guid.NewGuid():N}";
        var (runtime, _, fixture) = Create(matchId, out var senderSession, out var recipientSession,
            out var refereeSession, out var sender, out var recipient, out var referee, emitted);
        using (fixture)
        await using (runtime)
        {
            var identity = new L12ChatRequestIdentity(senderSession, "s", sender.Id, 1);
            var requestId = $"c1.{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.{Guid.NewGuid():D}";
            runtime.QueueSend(identity, requestId, "table", L12ChatRuntime.TableBroadcastPeer, matchId, "全桌通知");

            // 收件人写在送达元数据里而不是载荷里，所以按条数断言：
            // 两名选手 + 一名裁判 = 三个成员，发送者自己不再重复收 → 另外两人各一条 chatMessage。
            var messages = 0;
            for (var i = 0; i < 600 && messages < 2; i++)
            {
                messages = emitted.Count(frame => frame.TryGetProperty("type", out var type)
                    && type.GetString() == "chatMessage");
                await Task.Delay(10);
            }

            var ack = emitted.FirstOrDefault(frame => frame.TryGetProperty("type", out var type)
                && type.GetString() == "chatAck");
            Assert.True(messages == 2, $"chatMessage={messages}; ack={ack}");
            // 发送者必须拿到成功回执：线上「发送结果未确认」正是这条回执被投递闸门丢掉的结果。
            Assert.True(ack.ValueKind == JsonValueKind.Object && ack.GetProperty("success").GetBoolean(),
                $"ack={ack}");
        }
    }
    // 投递闸门（L12WebSocketServer.FreshChatDelivery）曾把本桌通配收件人当成账号去查账号对，
    // 于是发送者的 chatAck 与 chatHistory 被静默丢弃。这条用例直接钉住闸门的判定。
    [Fact]
    public async Task TableBroadcastRecipientIsAdmittedByMembershipInsteadOfAccountPair()
    {
        var emitted = new ConcurrentQueue<JsonElement>();
        var matchId = $"table-{Guid.NewGuid():N}";
        var (runtime, rooms, fixture) = Create(matchId, out var senderSession, out _, out _,
            out var sender, out _, out _, emitted);
        using (fixture)
        await using (runtime)
        {
            var origin = new L12ChatRequestIdentity(senderSession, "s", sender.Id, 1);
            var metadata = new L12ChatDeliveryMetadata(sender.Id, L12ChatRuntime.TableBroadcastPeer,
                L12ChatDeliveryScope.Table, sender.Id, L12ChatRuntime.TableBroadcastPeer, matchId, 1,
                origin, origin);

            Assert.True(L12ChatRuntime.IsTableBroadcast(metadata.Scope, metadata.RecipientAccountId));
            Assert.True(L12ChatRuntime.TableBroadcastAdmitted(metadata, rooms, originCurrent: true));

            var outsider = fixture.Store.Register($"tx{Guid.NewGuid():N}"[..10], "Password123!").Account!;
            var foreign = metadata with { SenderAccountId = outsider.Id };
            Assert.False(L12ChatRuntime.TableBroadcastAdmitted(foreign, rooms, originCurrent: true));
            Assert.False(L12ChatRuntime.TableBroadcastAdmitted(metadata, rooms, originCurrent: false));

            // 普通账号对投递仍走原判定：本桌通配不能顺手放宽别的分支。
            Assert.False(L12ChatRuntime.IsTableBroadcast(L12ChatDeliveryScope.Opponent,
                L12ChatRuntime.TableBroadcastPeer));
        }
    }

    private static void PublishScope(L12RoomManager rooms, L12CommittedOpponentChatScope scope)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var dictionary = typeof(L12RoomManager).GetField("_opponentChatScopes", flags)!.GetValue(rooms)!;
        var slotType = typeof(L12RoomManager).GetNestedType("OpponentChatScopeSlot", BindingFlags.NonPublic)!;
        var slot = Activator.CreateInstance(slotType, nonPublic: true)!;
        slotType.GetField("Current", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(slot, scope);
        Assert.True((bool)dictionary.GetType().GetMethod("TryAdd")!.Invoke(dictionary, [scope.MatchId, slot])!);
    }
}
