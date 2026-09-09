using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SettlementRoomRetentionTests
{
    private static string Type(OutgoingMessage message) => JsonSerializer.SerializeToElement(message.Payload).GetProperty("type").GetString()!;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FirstLeaveKeepsOtherPlayerSettlementAndSecondCloses(bool hostFirst)
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-settlement", Guid.NewGuid().ToString("N"));
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var manager = new L12RoomManager(L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data")), recorder, utcNow: () => now);
        var host = Guid.NewGuid(); var guest = Guid.NewGuid();
        manager.Connect(host, "甲"); manager.Connect(guest, "乙");
        var code = JsonSerializer.SerializeToElement(manager.CreateRoom(host)[0].Payload).GetProperty("roomCode").GetString();
        manager.JoinRoom(guest, code);
        await manager.SetReadyAsync(host, true); await manager.SetReadyAsync(guest, true);
        await manager.HandleActionAsync(host, JsonSerializer.SerializeToElement(new { type = "surrender" }));
        var first = hostFirst ? host : guest; var second = hostFirst ? guest : host;
        Assert.DoesNotContain(manager.LeaveRoom(first), item => item.SessionId == second);
        Assert.Contains(await manager.RecoveryStateAsync(second), item => Type(item) == "gameState");
        var newRoom = Assert.Single(manager.CreateRoom(first));
        Assert.Equal("roomState", Type(newRoom));
        Assert.NotEqual(code, JsonSerializer.SerializeToElement(newRoom.Payload).GetProperty("roomCode").GetString());
        var newGuest = Guid.NewGuid(); manager.Connect(newGuest, "丙");
        manager.JoinRoom(newGuest, JsonSerializer.SerializeToElement(newRoom.Payload).GetProperty("roomCode").GetString());
        await manager.SetReadyAsync(first, true);
        Assert.Contains(await manager.SetReadyAsync(newGuest, true), item => Type(item) == "gameState");
        manager.LeaveRoom(second);
        Assert.Empty(await manager.RecoveryStateAsync(second));
        Assert.Contains(await manager.RecoveryStateAsync(first), item => Type(item) == "gameState");
        Assert.DoesNotContain(await manager.TickRankedClocksAsync(now.AddMinutes(31)), item => item.SessionId == first);
    }

    [Fact]
    public async Task SettlementSurvivesUntilThirtyMinutesWithoutExtendingOnRecovery()
    {
        var directory = Path.Combine(Path.GetTempPath(), "l12-settlement", Guid.NewGuid().ToString("N"));
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var manager = new L12RoomManager(L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data")), recorder, utcNow: () => now);
        var host = Guid.NewGuid(); var guest = Guid.NewGuid();
        manager.Connect(host, "甲"); manager.Connect(guest, "乙");
        var code = JsonSerializer.SerializeToElement(manager.CreateRoom(host)[0].Payload).GetProperty("roomCode").GetString();
        manager.JoinRoom(guest, code);
        await manager.SetReadyAsync(host, true); await manager.SetReadyAsync(guest, true);
        await manager.HandleActionAsync(host, JsonSerializer.SerializeToElement(new { type = "surrender" }));
        now = now.AddMinutes(29);
        Assert.DoesNotContain(await manager.TickRankedClocksAsync(), item => Type(item) == "roomClosed");
        Assert.Contains(await manager.RecoveryStateAsync(guest), item => Type(item) == "gameState");
        now = now.AddMinutes(1);
        Assert.Equal(2, (await manager.TickRankedClocksAsync()).Count(item => Type(item) == "roomClosed"));
        Assert.Empty(await manager.RecoveryStateAsync(host));
    }
}
