using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

/// <summary>
/// 用户 2026-11-26「显示名跟随改名」：房间与对局的展示名必须取账号当前名，
/// 开局的权威状态与对局历史仍保留入局时的名字。
/// </summary>
public sealed class PlayerDisplayNameFollowsRenameTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RenameDuringGameProjectsLiveNameWhileAuthoritativeStateKeepsJoinName()
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-display-name-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var platformPath = Path.Combine(root, "platform.json");
        var database = Path.Combine(root, "matches.db");
        var store = new L12PlatformStore(platformPath);
        var host = store.Register("显示名甲", "Secret123!");
        var peer = store.Register("显示名乙", "Secret123!");
        Assert.True(host.Success, host.Message);
        Assert.True(peer.Success, peer.Message);

        await using var recorder = new MatchRecorder(database);
        await recorder.InitializeAsync();
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        var manager = new L12RoomManager(catalog, recorder, store);
        store.UsernameChanged += manager.SyncAccountName;

        var hostSession = Guid.NewGuid();
        var peerSession = Guid.NewGuid();
        manager.Connect(hostSession, host.Account!.Id, host.Account.Username);
        manager.Connect(peerSession, peer.Account!.Id, peer.Account.Username);
        var created = manager.CreateRoom(hostSession, new L12RoomOptions { Spectating = "public", DisasterMode = "none" });
        var code = Payload(created[0]).GetProperty("roomCode").GetString()!;
        manager.JoinRoom(peerSession, code);
        await manager.SetReadyAsync(hostSession, true);
        var started = await manager.SetReadyAsync(peerSession, true);
        Assert.Equal("显示名甲", PlayerNames(started, hostSession)![0]);

        var renamed = store.SelfServiceChangeUsername(host.Account.Id, "Secret123!", "显示名丙");
        Assert.True(renamed.Success, renamed.Message);

        // 改名后任意一次投影（此处用观战者的房间/对局投影）都必须带新名。
        var observer = Guid.NewGuid();
        manager.Connect(observer, "observer-display-name", "观战者");
        var spectated = manager.SpectateRoom(observer, code);
        Assert.Equal("显示名丙", PlayerNames(spectated, observer, "gameState")![0]);
        Assert.Equal("显示名乙", PlayerNames(spectated, observer, "gameState")![1]);
        var roomNames = MessagePayload(spectated, observer, "roomState").GetProperty("players");
        Assert.Equal("显示名丙", roomNames[0].GetProperty("name").GetString());

        // 权威状态与对局历史仍保留入局名：改名只影响展示投影。
        var game = (L12GameEngine)OnlyRoom(manager).GetType().GetProperty("Game")!.GetValue(OnlyRoom(manager))!;
        Assert.Equal("显示名甲", game.State.Players[0].Name);

        await recorder.DisposeAsync();
        using var pool = new SqliteConnection($"Data Source={database}");
        SqliteConnection.ClearPool(pool);
    }

    private static JsonElement MessagePayload(IReadOnlyList<OutgoingMessage> messages, Guid sessionId, string type)
        => messages.Where(message => message.SessionId == sessionId)
            .Select(Payload)
            .Single(payload => payload.GetProperty("type").GetString() == type);

    private static string?[]? PlayerNames(IReadOnlyList<OutgoingMessage> messages, Guid sessionId, string type = "gameState")
    {
        var payload = MessagePayload(messages, sessionId, type);
        var state = type == "gameState" ? payload.GetProperty("state") : payload;
        return state.GetProperty("players").EnumerateArray()
            .Select(player => player.GetProperty("name").GetString()).ToArray();
    }

    private static object OnlyRoom(L12RoomManager manager)
    {
        var rooms = (System.Collections.IEnumerable)typeof(L12RoomManager)
            .GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        var entry = rooms.Cast<object>().Single();
        return entry.GetType().GetProperty("Value")!.GetValue(entry)!;
    }

    private static JsonElement Payload(OutgoingMessage message)
        => JsonSerializer.SerializeToElement(message.Payload, Json);
}
