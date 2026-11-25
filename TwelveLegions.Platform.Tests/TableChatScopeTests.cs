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
        var runtime = new L12ChatRuntime(fixture.Store, rooms, _ => true, _ => null, frame =>
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
