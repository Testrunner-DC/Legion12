using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RankedAdmissionTests
{
    [Fact]
    public void BrowserTokenIsSignedAndCannotBeReplacedByAnIpOrForgedToken()
    {
        var privacy = new L12RankedDevicePrivacy("ranked-browser-secret-at-least-32-characters");
        var token = privacy.Issue();
        Assert.StartsWith("browser-v1:", privacy.Read(token));
        Assert.Equal(privacy.Read(token), new L12RankedDevicePrivacy("ranked-browser-secret-at-least-32-characters").Read(token));
        Assert.Null(privacy.Read("198.51.100.1"));
        Assert.Null(privacy.Read(token[..128] + (token[^1] == '0' ? '1' : '0')));
        Assert.Null(new L12RankedDevicePrivacy("another-random-key-with-at-least-32-chars").Read(token));
        Assert.NotEqual(privacy.Read(token), privacy.Read(privacy.Issue()));
    }

    [Fact]
    public async Task SameBrowserDifferentAccountsCannotQueueConcurrentlyButCasualAndReconnectWork()
    {
        var fixture = await CreateAsync();
        var (manager, platform, _) = fixture;
        var first = platform.Register("并发甲", "Password123!").Account!;
        var second = platform.Register("并发乙", "Password123!").Account!;
        platform.SelectRankedFaction(first.Id, "order");
        platform.SelectRankedFaction(second.Id, "chaos");
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        await manager.ConnectAsync(a, first.Id, first.Username, "shared-network", "browser-v1:same");
        await manager.ConnectAsync(b, second.Id, second.Username, "shared-network", "browser-v1:same");
        var attempts = await Task.WhenAll(manager.JoinMatchmakingAsync(a, "ranked", null), manager.JoinMatchmakingAsync(b, "ranked", null));
        Assert.Single(attempts.SelectMany(x => x).Where(x => Type(x) == "matchmakingRejected"));
        Assert.Single(attempts.SelectMany(x => x).Where(x => Type(x) == "matchmakingState"));
        var replacement = Guid.NewGuid();
        await manager.ConnectAsync(replacement, first.Id, first.Username, "shared-network", "browser-v1:same");
        Assert.DoesNotContain(await manager.JoinMatchmakingAsync(replacement, "ranked", null), x => Type(x) == "matchmakingRejected");
        Assert.Contains(await manager.JoinMatchmakingAsync(b, "casual", null), x => Type(x) == "matchmakingState");
        manager.CancelMatchmaking(replacement);
        Assert.DoesNotContain(await manager.JoinMatchmakingAsync(b, "ranked", null), x => Type(x) == "matchmakingRejected");
    }

    [Fact]
    public async Task SharedNetworkAloneDoesNotPreventRankedAndParallelDuplicateAdmissionCreatesOnlyOneRoom()
    {
        var (manager, platform, _) = await CreateAsync();
        var first = platform.Register("同网甲", "Password123!").Account!;
        var second = platform.Register("同网乙", "Password123!").Account!;
        platform.SelectRankedFaction(first.Id, "order");
        platform.SelectRankedFaction(second.Id, "chaos");
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        await manager.ConnectAsync(a, first.Id, first.Username, "same-ip", "browser-v1:first");
        await manager.ConnectAsync(b, second.Id, second.Username, "same-ip", "browser-v1:second");
        await manager.JoinMatchmakingAsync(a, "ranked", null);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => manager.JoinMatchmakingAsync(b, "ranked", null)));
        // A successful match sends exactly two found messages, one to each player.
        Assert.Equal(2, outcomes.SelectMany(x => x).Count(x => Type(x) == "matchmakingFound"));
        var replacement = Guid.NewGuid();
        var claim = await manager.ConnectAsync(replacement, second.Id, second.Username, "same-ip", "browser-v1:second");
        Assert.True(claim.Recovered);
        Assert.Contains(await manager.JoinMatchmakingAsync(replacement, "ranked", null), x => Type(x) == "matchmakingRejected");
    }

    private static string? Type(OutgoingMessage message)
        => JsonSerializer.SerializeToElement(message.Payload).GetProperty("type").GetString();

    private static async Task<(L12RoomManager, L12PlatformStore, MatchRecorder)> CreateAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "l12-ranked-admission", Guid.NewGuid().ToString("N"));
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var platform = new L12PlatformStore(Path.Combine(path, "platform.json"), catalog.PresetDecks, officialCards: catalog.Cards);
        var recorder = new MatchRecorder(Path.Combine(path, "matches.db"));
        await recorder.InitializeAsync();
        return (new L12RoomManager(catalog, recorder, platform), platform, recorder);
    }
}
