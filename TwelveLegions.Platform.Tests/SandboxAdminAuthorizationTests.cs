using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;
using static TwelveLegions.Platform.Tests.TutorialRoomLifecycleTests;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class SandboxAdminAuthorizationTests
{
    [Fact]
    public async Task PublicSandboxRejectsPlayerAndMissingPlatformButExplicitPureFixtureUsesSameCore()
    {
        await using var f = await Fixture.Create();
        Assert.DoesNotContain(await f.Manager.CreateSandboxAsync(f.Student, null), IsGame);
        var pure = new L12RoomManager(f.Catalog, f.Recorder, maintenanceSandboxFenceActive: () => false);
        var owner = Guid.NewGuid();
        pure.Connect(owner, "trusted-fixture", "fixture");
        Assert.DoesNotContain(await pure.CreateSandboxAsync(owner, null), IsGame);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Manager.CreateSandboxForTestsAsync(f.Student, null));
        var created = Message(await pure.CreateSandboxForTestsAsync(owner, new(DisasterMode: "none")), "gameState");
        Assert.True(created.GetProperty("gmEnabled").GetBoolean());
        Assert.True(created.GetProperty("state").GetProperty("players")[1].TryGetProperty("hand", out _));
        foreach (var source in Directory.EnumerateFiles(Path.Combine(SourceRoot(), "服务端WebSocket", "TwelveLegions"), "L12WebSocketServer*.cs"))
            Assert.DoesNotContain("CreateSandboxForTestsAsync", File.ReadAllText(source));
    }

    [Fact]
    public async Task CurrentAdminCanCreateAndBothGmAndDualActorWorkThenDowngradeFencesAllMutationAndPrivateView()
    {
        await using var f = await Fixture.Create();
        var root = f.Platform.Login("Admin", "L12master").Account!;
        Assert.True(f.Platform.SetRole(root, f.AccountId, "admin"));
        var initial = Message(await f.Manager.CreateSandboxAsync(f.Student, new(DisasterMode: "none")), "gameState");
        Assert.True(initial.GetProperty("gmEnabled").GetBoolean());
        var gm = await f.Manager.HandleGmActionAsync(f.Student,
            JsonSerializer.SerializeToElement(new { type = "addMorale", targetPlayer = 0, value = 1 }), "gm-ok");
        Assert.Contains(gm, IsGame);
        var current = Message(gm, "gameState").GetProperty("state").GetProperty("activePlayer").GetInt32();
        var dual = await f.Manager.HandleSandboxActionAsync(f.Student, current,
            JsonSerializer.SerializeToElement(new { type = "endTurn" }), "dual-ok");
        Assert.Contains(dual, IsGame);
        Assert.True(f.Platform.SetRole(root, f.AccountId, "player"));
        Assert.DoesNotContain(await f.Manager.HandleGmActionAsync(f.Student,
            JsonSerializer.SerializeToElement(new { type = "addMorale", targetPlayer = 0, value = 1 }), "gm-no"), IsGame);
        Assert.DoesNotContain(await f.Manager.HandleSandboxActionAsync(f.Student, 1,
            JsonSerializer.SerializeToElement(new { type = "surrender" }), "dual-no"), IsGame);
        Assert.DoesNotContain(await f.Act(new("surrender"), "ordinary-no"), IsGame);
        var recovery = Message(await f.Manager.RecoveryStateAsync(f.Student), "gameState");
        Assert.False(recovery.GetProperty("gmEnabled").GetBoolean());
        Assert.False(recovery.GetProperty("state").GetProperty("players")[1].TryGetProperty("hand", out _));
        Assert.NotEqual(nameof(L12Phase.GameOver), recovery.GetProperty("state").GetProperty("phase").GetString());
    }

    [Theory]
    [InlineData("gm")]
    [InlineData("dual")]
    [InlineData("ordinary")]
    [InlineData("response")]
    public async Task DowngradeWhileWaitingForRoomGateIsRecheckedBeforeMutation(string lane)
    {
        await using var f = await Fixture.Create();
        var root = f.Platform.Login("Admin", "L12master").Account!;
        Assert.True(f.Platform.SetRole(root, f.AccountId, "admin"));
        await f.Manager.CreateSandboxAsync(f.Student, new(DisasterMode: "none"));
        var dictionary = (System.Collections.IEnumerable)typeof(L12RoomManager)
            .GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Manager)!;
        var entry = dictionary.Cast<object>().Single();
        var room = entry.GetType().GetProperty("Value")!.GetValue(entry)!;
        var gate = (SemaphoreSlim)room.GetType().GetProperty("Gate")!.GetValue(room)!;
        await gate.WaitAsync();
        Task<IReadOnlyList<OutgoingMessage>>? command = null;
        try
        {
            command = lane == "gm" ? f.Manager.HandleGmActionAsync(f.Student,
                JsonSerializer.SerializeToElement(new { type = "addMorale", value = 1 }), "waiting")
                : lane == "dual" ? f.Manager.HandleSandboxActionAsync(f.Student, 1,
                    JsonSerializer.SerializeToElement(new { type = "surrender" }), "waiting")
                : lane == "response" ? f.Manager.SetResponsePreferenceAsync(f.Student,
                    L12GameEngine.ValidOnlyResponseMode, "waiting")
                : f.Act(new("surrender"), "waiting");
            Assert.False(command.IsCompleted);
            Assert.True(f.Platform.SetRole(root, f.AccountId, "player"));
        }
        finally { gate.Release(); }
        Assert.DoesNotContain(await command!, IsGame);
        if (lane == "response")
        {
            Assert.Equal(L12GameEngine.DefaultResponseMode, f.Platform.ResponsePreference(f.AccountId));
            Assert.Null(await f.Recorder.LatestPendingResponsePreferenceAsync(f.AccountId));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedOldResponsePreferenceCannotReadOrWriteAfterSeatTakeover(bool write)
    {
        await using var f = await Fixture.Create();
        var root = f.Platform.Login("Admin", "L12master").Account!;
        Assert.True(f.Platform.SetRole(root, f.AccountId, "admin"));
        var initial = Message(await f.Manager.CreateSandboxAsync(f.Student, new(DisasterMode: "none")), "gameState");
        var match = initial.GetProperty("state").GetProperty("matchId").GetString()!;
        var before = (await f.Recorder.LoadJournalEngineAsync(match))!.Engine.ComputeStateHash();
        var gate = f.RoomGate();
        Task<L12SessionClaimResult>? claim = null;
        Task<IReadOnlyList<OutgoingMessage>>? stale = null;
        await gate.WaitAsync();
        try
        {
            claim = f.Manager.ConnectAsync(Guid.NewGuid(), f.AccountId, "learner");
            Assert.False(claim.IsCompleted);
            stale = write ? f.Manager.SetResponsePreferenceAsync(f.Student, L12GameEngine.ValidOnlyResponseMode, "stale-pref")
                : f.Manager.GetResponsePreferenceAsync(f.Student);
            Assert.False(stale.IsCompleted);
        }
        finally
        {
            gate.Release();
            await Task.WhenAll(new Task?[] { claim, stale }.OfType<Task>());
        }
        Assert.DoesNotContain(await stale!, message => JsonSerializer.SerializeToElement(message.Payload)
            .GetProperty("type").GetString() is "gameState" or "responsePreferenceResult" or "responsePreferenceState");
        Assert.Equal(before, (await f.Recorder.LoadJournalEngineAsync(match))!.Engine.ComputeStateHash());
        Assert.Equal(L12GameEngine.DefaultResponseMode, f.Platform.ResponsePreference(f.AccountId));
        Assert.Null(await f.Recorder.LatestPendingResponsePreferenceAsync(f.AccountId));
    }

    private static bool IsGame(OutgoingMessage message)
        => JsonSerializer.SerializeToElement(message.Payload).GetProperty("type").GetString() == "gameState";

    private static string SourceRoot()
        => PlatformTestSource.Root();
}
