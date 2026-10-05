using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ResponsePreferenceModeTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(string mode, ref DateTimeOffset now,
        bool autoPass = false, bool conceal = false)
    {
        var clock = now;
        var original = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "响应设置测试", MasterId = "S02-03M1", CardIds = [.. original.CardIds],
            MoraleIds = [.. original.MoraleIds], SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "response-preference-test", "RESP", 120912,
            ["甲", "乙"], [deck, deck], skipPreparation: true,
            autoPassEmptyResponses: autoPass, concealHiddenResponseAvailability: conceal,
            stateFormatVersion: 2, utcNow: () => clock,
            responseModes: [mode, mode]);
        game.State.PendingPrompts.Clear();
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear();
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
        }
        return game;
    }

    private static L12StackItem EmptyResponseStack(string id = "response-stack",
        string trigger = "reaction", string text = "测试响应时点") => new()
    {
        StackItemId = id,
        Controller = 1,
        SourceInstanceId = "source",
        SourceCardId = "S01-0103",
        SourceName = "测试来源",
        Trigger = trigger,
        Text = text,
    };

    private static void Offer(L12GameEngine game, L12StackItem stack, int priorityPlayer = 0)
    {
        game.State.ActivePlayer = priorityPlayer;
        game.State.EffectStack.Add(stack);
        typeof(L12GameEngine).GetMethod("BeginResponseWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, [stack]);
    }

    private static void Pass(L12GameEngine game, int playerIndex)
    {
        game.State.PendingPrompts.Clear();
        typeof(L12GameEngine).GetMethod("PassPriority", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, [playerIndex]);
    }

    private static void BeginNextWindow(L12GameEngine game, int priorityPlayer, string id)
    {
        game.State.PendingPrompts.Clear();
        game.State.EffectStack.Clear();
        game.State.ResponseWindow = null;
        Offer(game, EmptyResponseStack(id), priorityPlayer);
    }

    [Fact]
    public void DefaultModeKeepsLegacyEmptyWindowSwitch()
    {
        var now = DateTimeOffset.Parse("2026-09-27T00:00:00Z");
        var legacyWindow = Create(L12GameEngine.DefaultResponseMode, ref now, autoPass: false);
        Offer(legacyWindow, EmptyResponseStack());
        Assert.Equal(["pass"], Assert.Single(legacyWindow.State.PendingPrompts).ValidChoices);
        Assert.Null(legacyWindow.CaptureResponseAutoCloseLease());

        var legacyAutoPass = Create(L12GameEngine.DefaultResponseMode, ref now, autoPass: true);
        Offer(legacyAutoPass, EmptyResponseStack());
        Assert.Empty(legacyAutoPass.State.PendingPrompts);
    }

    [Fact]
    public void ValidOnlyIgnoresAnonymousPoolAvailability()
    {
        var now = DateTimeOffset.Parse("2026-09-27T00:00:00Z");
        var game = Create(L12GameEngine.ValidOnlyResponseMode, ref now, conceal: true);
        var stack = EmptyResponseStack(trigger: "promotion-enter", text: "晋升登场 可抽取1张牌。");
        var policy = typeof(L12GameEngine).GetMethod("ShouldAutoPassEmptyResponse",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.True((bool)policy.Invoke(null,
            [L12GameEngine.ValidOnlyResponseMode, 0, true, false, false])!);
        Assert.False((bool)policy.Invoke(null,
            [L12GameEngine.DefaultResponseMode, 0, true, true, false])!);

        Offer(game, stack, priorityPlayer: 1);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Null(game.CaptureResponseAutoCloseLease());
    }

    [Fact]
    public void InvalidFiveSecondsProjectsOnlyToOwnerAndGm()
    {
        var now = DateTimeOffset.Parse("2026-09-27T00:00:00Z");
        var game = Create(L12GameEngine.InvalidFiveSecondsResponseMode, ref now);
        Offer(game, EmptyResponseStack());

        var owner = JsonSerializer.Serialize(game.SnapshotFor(0));
        var opponent = JsonSerializer.Serialize(game.SnapshotFor(1));
        var spectator = JsonSerializer.Serialize(game.SnapshotForSpectator());
        var referee = JsonSerializer.Serialize(game.SnapshotForReferee());
        var gm = JsonSerializer.Serialize(game.SnapshotForGm(1));
        Assert.Contains("autoClose", owner, StringComparison.Ordinal);
        Assert.Contains("no-valid-response", owner, StringComparison.Ordinal);
        Assert.Contains("autoClose", gm, StringComparison.Ordinal);
        Assert.DoesNotContain("autoClose", opponent, StringComparison.Ordinal);
        Assert.DoesNotContain("autoClose", spectator, StringComparison.Ordinal);
        Assert.DoesNotContain("autoClose", referee, StringComparison.Ordinal);
        Assert.DoesNotContain("DeadlineUtc", opponent, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoCloseUsesInclusiveDeadlineAndRejectsStaleOrDuplicateLease()
    {
        var now = DateTimeOffset.Parse("2026-09-27T00:00:00Z");
        var game = Create(L12GameEngine.InvalidFiveSecondsResponseMode, ref now);
        Offer(game, EmptyResponseStack());
        var lease = Assert.IsType<L12ResponseAutoCloseLease>(game.CaptureResponseAutoCloseLease());

        Assert.False(game.TryExpireResponseAutoClose(lease.PromptId, lease.StackItemId,
            lease.PriorityPlayer, lease.DeadlineUtc, lease.DeadlineUtc.AddMilliseconds(-1)));
        Assert.False(game.TryExpireResponseAutoClose("old-prompt", lease.StackItemId,
            lease.PriorityPlayer, lease.DeadlineUtc, lease.DeadlineUtc));
        Assert.False(game.TryExpireResponseAutoClose(lease.PromptId, "old-stack",
            lease.PriorityPlayer, lease.DeadlineUtc, lease.DeadlineUtc));
        Assert.False(game.TryExpireResponseAutoClose(lease.PromptId, lease.StackItemId,
            1 - lease.PriorityPlayer, lease.DeadlineUtc, lease.DeadlineUtc));
        Assert.False(game.TryExpireResponseAutoClose(lease.PromptId, lease.StackItemId,
            lease.PriorityPlayer, lease.DeadlineUtc.AddSeconds(1), lease.DeadlineUtc.AddSeconds(1)));
        Assert.True(game.TryExpireResponseAutoClose(lease.PromptId, lease.StackItemId,
            lease.PriorityPlayer, lease.DeadlineUtc, lease.DeadlineUtc));
        Assert.False(game.TryExpireResponseAutoClose(lease.PromptId, lease.StackItemId,
            lease.PriorityPlayer, lease.DeadlineUtc, lease.DeadlineUtc));
    }

    [Theory]
    [InlineData(L12GameEngine.ValidOnlyResponseMode)]
    [InlineData(L12GameEngine.InvalidFiveSecondsResponseMode)]
    public void DefaultWindowIgnoresPreferenceChangesUntilTheNextWindow(string newMode)
    {
        var now = DateTimeOffset.Parse("2026-09-27T00:00:00Z");
        var game = Create(L12GameEngine.DefaultResponseMode, ref now);
        Offer(game, EmptyResponseStack(), priorityPlayer: 0);
        Assert.Null(game.State.ResponseWindow!.FrozenPlayerResponseModes);

        Assert.True(game.ApplyResponsePreference(1, newMode).Accepted);
        Pass(game, 0);

        Assert.Equal(1, game.State.ResponseWindow!.PriorityPlayer);
        Assert.Null(game.State.ResponseWindow.FrozenPlayerResponseModes);
        Assert.Equal(["pass"], Assert.Single(game.State.PendingPrompts).ValidChoices);
        Assert.Null(game.CaptureResponseAutoCloseLease());

        BeginNextWindow(game, 1, "next-response-stack");
        Assert.Equal(newMode, game.State.ResponseWindow!.FrozenPlayerResponseModes![1]);
        if (newMode == L12GameEngine.ValidOnlyResponseMode)
        {
            Assert.Equal(0, game.State.ResponseWindow.PriorityPlayer);
            Assert.Contains(game.State.Events, item => item.Type == "priority-pass" && item.PlayerIndex == 1);
            Assert.Null(game.CaptureResponseAutoCloseLease());
        }
        else
        {
            Assert.Equal(1, game.State.ResponseWindow.PriorityPlayer);
            Assert.NotNull(game.CaptureResponseAutoCloseLease());
        }
    }

    [Fact]
    public void FiveSecondWindowKeepsFrozenModeAfterChangingBackToDefault()
    {
        var now = DateTimeOffset.Parse("2026-09-27T00:00:00Z");
        var game = Create(L12GameEngine.InvalidFiveSecondsResponseMode, ref now);
        Offer(game, EmptyResponseStack(), priorityPlayer: 0);
        var firstLease = Assert.IsType<L12ResponseAutoCloseLease>(game.CaptureResponseAutoCloseLease());

        Assert.True(game.ApplyResponsePreference(1, L12GameEngine.DefaultResponseMode).Accepted);
        Assert.True(game.TryExpireResponseAutoClose(firstLease.PromptId, firstLease.StackItemId,
            firstLease.PriorityPlayer, firstLease.DeadlineUtc, firstLease.DeadlineUtc));

        Assert.Equal(L12GameEngine.InvalidFiveSecondsResponseMode,
            game.State.ResponseWindow!.FrozenPlayerResponseModes![1]);
        Assert.Equal(1, game.State.ResponseWindow.PriorityPlayer);
        var nextLease = Assert.IsType<L12ResponseAutoCloseLease>(game.CaptureResponseAutoCloseLease());
        Assert.Equal(firstLease.DeadlineUtc.AddSeconds(5), nextLease.DeadlineUtc);

        BeginNextWindow(game, 1, "default-next-response-stack");
        Assert.Equal(L12GameEngine.DefaultResponseMode,
            game.State.ResponseWindow!.FrozenPlayerResponseModes![1]);
        Assert.Equal(["pass"], Assert.Single(game.State.PendingPrompts).ValidChoices);
        Assert.Null(game.CaptureResponseAutoCloseLease());
    }

    [Fact]
    public void CheckpointRestoreKeepsFrozenModesDeadlineAndProjectionPrivacy()
    {
        var now = DateTimeOffset.Parse("2026-09-27T00:00:00Z");
        var game = Create(L12GameEngine.InvalidFiveSecondsResponseMode, ref now);
        Offer(game, EmptyResponseStack(), priorityPlayer: 0);
        var originalLease = Assert.IsType<L12ResponseAutoCloseLease>(game.CaptureResponseAutoCloseLease());
        Assert.True(game.ApplyResponsePreference(0, L12GameEngine.DefaultResponseMode).Accepted);

        var checkpoint = game.SerializeFullState();
        Assert.Contains("FrozenPlayerResponseModes", checkpoint, StringComparison.Ordinal);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            Assert.IsType<L12RandomState>(game.RandomState), game.CardFactSignalSequence,
            game.AutoPassEmptyResponses, game.ConcealHiddenResponseAvailability,
            utcNow: () => now.AddSeconds(2));
        var restoredLease = Assert.IsType<L12ResponseAutoCloseLease>(restored.CaptureResponseAutoCloseLease());

        Assert.Equal(game.State.ResponseWindow!.FrozenPlayerResponseModes,
            restored.State.ResponseWindow!.FrozenPlayerResponseModes);
        Assert.Equal(originalLease, restoredLease);
        foreach (var snapshot in new object[]
                 {
                     restored.SnapshotFor(0), restored.SnapshotFor(1), restored.SnapshotForSpectator(),
                     restored.SnapshotForReferee(), restored.SnapshotForGm(1),
                 })
        {
            var json = JsonSerializer.Serialize(snapshot);
            Assert.DoesNotContain("FrozenPlayerResponseModes", json, StringComparison.Ordinal);
            Assert.DoesNotContain(L12GameEngine.InvalidFiveSecondsResponseMode, json, StringComparison.Ordinal);
        }
        Assert.Contains("autoClose", JsonSerializer.Serialize(restored.SnapshotFor(0)), StringComparison.Ordinal);
        Assert.DoesNotContain("autoClose", JsonSerializer.Serialize(restored.SnapshotFor(1)), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingPlatformAccountReadsAsDefaultMode()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"l12-response-platform-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new L12PlatformStore(Path.Combine(directory, "platform.json"));
            Assert.Equal(L12GameEngine.DefaultResponseMode,
                store.ResponsePreference("compatibility-session-without-account"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

public sealed class ResponsePreferenceOutboxTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Game(string matchId) => new(Catalog, matchId, "OUTBOX", 92712,
        ["甲", "乙"], [0, 0], skipPreparation: true,
        stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion);

    [Fact]
    public async Task JournalAndPreferenceOutboxCommitAtomicallyAndAckIdempotently()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"l12-response-outbox-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        MatchRecorder? recorder = null;
        try
        {
            recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var game = Game("response-outbox-commit");
            await recorder.StartAsync(game, account0: "account-a", account1: "account-b");
            var result = game.ApplyResponsePreference(0, L12GameEngine.ValidOnlyResponseMode);
            var envelope = new L12ResponsePreferenceOutboxEnvelope("operation-a", game.State.MatchId,
                1, "account-a", 0, L12GameEngine.ValidOnlyResponseMode, DateTimeOffset.UtcNow);
            await recorder.AppendResponsePreferenceAsync(game, 1, 0,
                JsonSerializer.Serialize(new { type = "setResponsePreference", responseMode = envelope.TargetMode }),
                result, envelope, null, "request-a");

            var pending = Assert.Single(await recorder.ListPendingResponsePreferencesAsync("account-a"));
            Assert.Equal(L12GameEngine.ValidOnlyResponseMode, pending.Payload!.TargetMode);
            await recorder.MarkResponsePreferenceAppliedAsync(pending.Id, pending.PayloadHash);
            Assert.Empty(await recorder.ListPendingResponsePreferencesAsync("account-a"));
        }
        finally
        {
            if (recorder is not null) await recorder.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FailedJournalCommitLeavesNoPreferenceOutbox()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"l12-response-outbox-fail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        MatchRecorder? recorder = null;
        try
        {
            recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
            await recorder.InitializeAsync();
            var game = Game("response-outbox-failure");
            await recorder.StartAsync(game, account0: "account-a", account1: "account-b");
            var result = game.ApplyResponsePreference(0, L12GameEngine.InvalidFiveSecondsResponseMode);
            var envelope = new L12ResponsePreferenceOutboxEnvelope("operation-fail", game.State.MatchId,
                1, "account-a", 0, L12GameEngine.InvalidFiveSecondsResponseMode, DateTimeOffset.UtcNow);
            recorder.StorageFailureInjector = phase =>
            {
                if (phase == "before-match-command-commit") throw new IOException("injected");
            };
            await Assert.ThrowsAsync<IOException>(() => recorder.AppendResponsePreferenceAsync(game, 1, 0,
                JsonSerializer.Serialize(new { type = "setResponsePreference", responseMode = envelope.TargetMode }),
                result, envelope, null, "request-fail"));
            recorder.StorageFailureInjector = null;
            Assert.Empty(await recorder.ListPendingResponsePreferencesAsync("account-a"));
        }
        finally
        {
            if (recorder is not null) await recorder.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
