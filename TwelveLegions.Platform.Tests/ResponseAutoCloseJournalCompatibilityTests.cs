using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class ResponseAutoCloseJournalCompatibilityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task JournalRebuildAcceptsCurrentAndLegacyResponseAutoCloseFieldNames(
        bool legacy, bool bothInvalidFiveSeconds)
    {
        await using var fixture = await Fixture.CreateAsync(bothInvalidFiveSeconds);
        await fixture.StartFriendlyAsync();
        fixture.Now = fixture.OriginalLease.DeadlineUtc;
        var command = fixture.ExpireAndBuildCommand(legacy);
        fixture.Now = fixture.Now.AddMilliseconds(1);
        await fixture.Recorder.AppendAsync(fixture.Engine, 1, -1, command, CommandResult.Ok());
        var sequence = 1;
        if (bothInvalidFiveSeconds)
        {
            var next = Assert.IsType<L12ResponseAutoCloseLease>(
                fixture.Engine.CaptureResponseAutoCloseLease());
            Assert.Equal(fixture.OriginalLease.DeadlineUtc.AddSeconds(5), next.DeadlineUtc);
            fixture.Now = next.DeadlineUtc;
            var second = fixture.ExpireCurrentAndBuildCommand(legacy);
            fixture.Now = fixture.Now.AddMilliseconds(1);
            await fixture.Recorder.AppendAsync(fixture.Engine, 2, -1, second, CommandResult.Ok());
            sequence = 2;
        }

        var recovered = Assert.IsType<L12JournalRecoveryState>(
            await fixture.Recorder.LoadJournalEngineAsync(fixture.Engine.State.MatchId));

        Assert.Equal(sequence, recovered.CommandSequence);
        Assert.Equal(fixture.Engine.ComputeStateHash(), recovered.Engine.ComputeStateHash());
        Assert.Equal(fixture.Engine.CaptureResponseAutoCloseLease(),
            recovered.Engine.CaptureResponseAutoCloseLease());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RankedRecoveryAcceptsCurrentAndLegacyResponseAutoCloseFieldNames(
        bool legacy, bool bothInvalidFiveSeconds)
    {
        await using var fixture = await Fixture.CreateAsync(bothInvalidFiveSeconds);
        await fixture.StartRankedAsync();
        fixture.Now = fixture.OriginalLease.DeadlineUtc;
        var command = fixture.ExpireAndBuildCommand(legacy);
        fixture.Now = fixture.Now.AddMilliseconds(1);
        await fixture.Recorder.AppendRankedAsync(fixture.Engine, 1, -1, command, CommandResult.Ok(),
            fixture.Runtime(1, 2), settlement: null);
        var sequence = 1;
        if (bothInvalidFiveSeconds)
        {
            var next = Assert.IsType<L12ResponseAutoCloseLease>(
                fixture.Engine.CaptureResponseAutoCloseLease());
            Assert.Equal(fixture.OriginalLease.DeadlineUtc.AddSeconds(5), next.DeadlineUtc);
            fixture.Now = next.DeadlineUtc;
            var second = fixture.ExpireCurrentAndBuildCommand(legacy);
            fixture.Now = fixture.Now.AddMilliseconds(1);
            await fixture.Recorder.AppendRankedAsync(fixture.Engine, 2, -1, second, CommandResult.Ok(),
                fixture.Runtime(2, 3), settlement: null);
            sequence = 2;
        }

        var manager = new L12RoomManager(fixture.Catalog, fixture.Recorder, utcNow: () => fixture.Now);
        var summary = await manager.RestoreRankedRoomsAsync();

        Assert.Equal(1, summary.Restored);
        Assert.Equal(0, summary.Invalidated);
        Assert.Equal(0, summary.Failed);
        var restored = Assert.Single(Rooms(manager));
        var restoredEngine = Game(restored);
        Assert.Equal(sequence, CommandSequence(restored));
        Assert.Equal(fixture.Engine.ComputeStateHash(), restoredEngine.ComputeStateHash());
        Assert.Equal(fixture.Engine.CaptureResponseAutoCloseLease(),
            restoredEngine.CaptureResponseAutoCloseLease());
    }

    [Fact]
    public async Task RecordedObservedTimeScopesNewDeadlinesAndDoesNotLeakIntoLaterWindows()
    {
        await using var fixture = await Fixture.CreateAsync(bothInvalidFiveSeconds: true);
        await fixture.StartFriendlyAsync();
        var observedAt = fixture.OriginalLease.DeadlineUtc;
        fixture.Now = observedAt.AddMinutes(1);

        var command = fixture.ExpireAndBuildCommand(legacy: false, observedAtUtc: observedAt);
        var next = Assert.IsType<L12ResponseAutoCloseLease>(
            fixture.Engine.CaptureResponseAutoCloseLease());
        Assert.Equal(observedAt.AddSeconds(5), next.DeadlineUtc);
        Assert.NotEqual(fixture.Now.AddSeconds(5), next.DeadlineUtc);
        await fixture.Recorder.AppendAsync(fixture.Engine, 1, -1, command, CommandResult.Ok());

        var recovered = Assert.IsType<L12JournalRecoveryState>(
            await fixture.Recorder.LoadJournalEngineAsync(fixture.Engine.State.MatchId));
        Assert.Equal(fixture.Engine.ComputeStateHash(), recovered.Engine.ComputeStateHash());
        Assert.Equal(next, recovered.Engine.CaptureResponseAutoCloseLease());

        var later = fixture.BeginFreshEmptyResponseWindow();
        Assert.Equal(fixture.Now.AddSeconds(5), later.DeadlineUtc);
    }

    [Fact]
    public async Task RecordedObservedTimeScopeIsRestoredWhenSynchronousDeadlineCreationThrows()
    {
        await using var fixture = await Fixture.CreateAsync(bothInvalidFiveSeconds: true);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            fixture.Engine.TryExpireResponseAutoClose(fixture.OriginalLease.PromptId,
                fixture.OriginalLease.StackItemId, fixture.OriginalLease.PriorityPlayer,
                fixture.OriginalLease.DeadlineUtc, DateTimeOffset.MaxValue));

        var later = fixture.BeginFreshEmptyResponseWindow();
        Assert.Equal(fixture.Now.AddSeconds(5), later.DeadlineUtc);
    }

    [Fact]
    public async Task JournalRebuildRejectsLegacyFactWhoseNextDeadlineWasNotRecorded()
    {
        await using var fixture = await Fixture.CreateAsync(bothInvalidFiveSeconds: true);
        await fixture.StartFriendlyAsync();
        var observedAt = fixture.OriginalLease.DeadlineUtc;
        fixture.Now = observedAt.AddMinutes(1);
        var command = fixture.ExpireAndBuildCommand(legacy: true, observedAtUtc: observedAt);
        var deterministic = Assert.IsType<L12ResponseAutoCloseLease>(
            fixture.Engine.CaptureResponseAutoCloseLease());
        Assert.Equal(observedAt.AddSeconds(5), deterministic.DeadlineUtc);

        // Historical producers did not record the new deadline created synchronously
        // by the automatic pass. Model that unavailable fact without weakening replay hashes.
        fixture.Engine.State.ResponseWindow!.AutoCloseDeadlineUtc = fixture.Now.AddSeconds(5);
        await fixture.Recorder.AppendAsync(fixture.Engine, 1, -1, command, CommandResult.Ok());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Recorder.LoadJournalEngineAsync(fixture.Engine.State.MatchId));
    }

    [Theory]
    [InlineData("missing-prompt")]
    [InlineData("null-stack")]
    [InlineData("wrong-priority-type")]
    [InlineData("uppercase-deadline")]
    [InlineData("wrong-observed-type")]
    [InlineData("conflicting-alias")]
    public async Task JournalRebuildRejectsDamagedResponseAutoCloseRecords(string damage)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartFriendlyAsync();
        var command = fixture.ExpireAndBuildDamagedCommand(damage);
        await fixture.Recorder.AppendAsync(fixture.Engine, 1, -1, command, CommandResult.Ok());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Recorder.LoadJournalEngineAsync(fixture.Engine.State.MatchId));
    }

    [Fact]
    public async Task RankedRecoveryFailsClosedForDamagedResponseAutoCloseRecord()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartRankedAsync();
        var command = fixture.ExpireAndBuildDamagedCommand("wrong-priority-type");
        await fixture.Recorder.AppendRankedAsync(fixture.Engine, 1, -1, command, CommandResult.Ok(),
            fixture.Runtime(1, 2), settlement: null);

        var manager = new L12RoomManager(fixture.Catalog, fixture.Recorder, utcNow: () => fixture.Now);
        var summary = await manager.RestoreRankedRoomsAsync();

        Assert.Equal(0, summary.Restored);
        Assert.Equal(1, summary.Invalidated);
        Assert.Equal(0, summary.Failed);
        Assert.Empty(Rooms(manager));
    }

    [Fact]
    public async Task RepeatedResponseAutoCloseFactChangesAuthoritativeStateOnlyOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartFriendlyAsync();
        var command = fixture.ExpireAndBuildCommand(legacy: true);
        await fixture.Recorder.AppendAsync(fixture.Engine, 1, -1, command, CommandResult.Ok());
        var revisionAfterFirst = fixture.Engine.State.Revision;
        var eventsAfterFirst = fixture.Engine.State.EventSequence;
        var hashAfterFirst = fixture.Engine.ComputeStateHash();

        Assert.False(fixture.Engine.TryExpireResponseAutoClose(fixture.OriginalLease.PromptId,
            fixture.OriginalLease.StackItemId, fixture.OriginalLease.PriorityPlayer,
            fixture.OriginalLease.DeadlineUtc, fixture.OriginalLease.DeadlineUtc));
        Assert.Equal(revisionAfterFirst, fixture.Engine.State.Revision);
        Assert.Equal(eventsAfterFirst, fixture.Engine.State.EventSequence);
        Assert.Equal(hashAfterFirst, fixture.Engine.ComputeStateHash());
        await fixture.Recorder.AppendAsync(fixture.Engine, 2, -1, command, CommandResult.Ok());

        var recovered = Assert.IsType<L12JournalRecoveryState>(
            await fixture.Recorder.LoadJournalEngineAsync(fixture.Engine.State.MatchId));
        Assert.Equal(2, recovered.CommandSequence);
        Assert.Equal(revisionAfterFirst, recovered.Engine.State.Revision);
        Assert.Equal(eventsAfterFirst, recovered.Engine.State.EventSequence);
        Assert.Equal(hashAfterFirst, recovered.Engine.ComputeStateHash());
    }

    private static IEnumerable<object> Rooms(L12RoomManager manager)
    {
        var entries = (IEnumerable)typeof(L12RoomManager).GetField("_rooms",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        foreach (var entry in entries)
            yield return entry.GetType().GetProperty("Value")!.GetValue(entry)!;
    }

    private static L12GameEngine Game(object room)
        => (L12GameEngine)room.GetType().GetProperty("Game")!.GetValue(room)!;

    private static long CommandSequence(object room)
        => (long)room.GetType().GetProperty("CommandSequence")!.GetValue(room)!;

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly L12PresetDeckDefinition _deck;
        private readonly MutableClock _clock;

        private Fixture(string directory, L12Catalog catalog, MatchRecorder recorder,
            L12GameEngine engine, L12PresetDeckDefinition deck, MutableClock clock,
            L12ResponseAutoCloseLease originalLease)
        {
            _directory = directory;
            Catalog = catalog;
            Recorder = recorder;
            Engine = engine;
            _deck = deck;
            _clock = clock;
            OriginalLease = originalLease;
        }

        internal L12Catalog Catalog { get; }
        internal MatchRecorder Recorder { get; }
        internal L12GameEngine Engine { get; }
        internal DateTimeOffset Now { get => _clock.Now; set => _clock.Now = value; }
        internal L12ResponseAutoCloseLease OriginalLease { get; }

        internal static async Task<Fixture> CreateAsync(bool bothInvalidFiveSeconds = false)
        {
            var directory = Path.Combine(Path.GetTempPath(),
                "l12-response-autoclose-compat-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var clock = new MutableClock(
                new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero));
            var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"), () => clock.Now);
            await recorder.InitializeAsync();
            recorder.AttachCatalog(catalog);
            var original = catalog.DeckAt(0);
            var deck = new L12PresetDeckDefinition
            {
                Name = "response auto-close compatibility",
                MasterId = "S02-03M1",
                CardIds = [.. original.CardIds],
                MoraleIds = [.. original.MoraleIds],
                SpecialIds = [],
            };
            var engine = new L12GameEngine(catalog,
                "response-autoclose-" + Guid.NewGuid().ToString("N"),
                "RA" + Guid.NewGuid().ToString("N")[..6], 120912,
                ["first", "second"], [deck, deck], skipPreparation: true,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
                stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion,
                utcNow: () => clock.Now,
                responseModes:
                [
                    L12GameEngine.InvalidFiveSecondsResponseMode,
                    bothInvalidFiveSeconds
                        ? L12GameEngine.InvalidFiveSecondsResponseMode
                        : L12GameEngine.DefaultResponseMode,
                ]);
            engine.State.PendingPrompts.Clear();
            foreach (var player in engine.State.Players)
            {
                // Ranked recovery reconstructs the exact deck multiset from the
                // initial Library/Graveyard. Keep every physical instance when
                // arranging an empty public response fixture; do not delete it.
                player.Library.AddRange(player.Hand);
                player.Hand.Clear();
                foreach (var row in player.Field)
                    player.Library.AddRange(row.OfType<L12CardInstance>());
                player.Field[0] = new L12CardInstance?[3];
                player.Field[1] = new L12CardInstance?[3];
            }
            var stack = new L12StackItem
            {
                StackItemId = "response-stack-" + Guid.NewGuid().ToString("N"),
                Controller = 1,
                SourceInstanceId = "source",
                SourceCardId = "S01-0103",
                SourceName = "response source",
                Trigger = "reaction",
                Text = "response auto-close compatibility",
            };
            engine.State.ActivePlayer = 0;
            engine.State.EffectStack.Add(stack);
            typeof(L12GameEngine).GetMethod("BeginResponseWindow",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(engine, [stack]);
            var lease = Assert.IsType<L12ResponseAutoCloseLease>(engine.CaptureResponseAutoCloseLease());
            return new Fixture(directory, catalog, recorder, engine, deck, clock, lease);
        }

        internal Task StartFriendlyAsync()
            => Recorder.StartAsync(Engine, decks: [_deck, _deck]);

        internal Task StartRankedAsync()
            => Recorder.StartRankedAsync(Engine, "account-first", "account-second",
                [_deck, _deck], Runtime(0, 1));

        internal L12RankedRuntimeCheckpoint Runtime(long sequence, long generation)
            => new(1, Engine.State.MatchId, Engine.State.RoomCode, "active", generation,
                sequence, Engine.State.Revision, Engine.ComputeStateHash(), Now, 0,
                [900_000, 900_000], [120_000, 120_000], [false, false], Now,
                null, false, [true, true], [null, null], ["integrity-first", "integrity-second"],
                [1, 1], Now, L12PlatformStore.DefaultRankedTimeControl(),
                ["browser-first", "browser-second"]);

        internal string ExpireAndBuildCommand(bool legacy, DateTimeOffset? observedAtUtc = null)
            => ExpireAndBuildCommand(OriginalLease, legacy,
                observedAtUtc ?? OriginalLease.DeadlineUtc);

        internal string ExpireCurrentAndBuildCommand(bool legacy)
        {
            var lease = Assert.IsType<L12ResponseAutoCloseLease>(Engine.CaptureResponseAutoCloseLease());
            return ExpireAndBuildCommand(lease, legacy, lease.DeadlineUtc);
        }

        internal string ExpireAndBuildDamagedCommand(string damage)
        {
            Assert.True(Engine.TryExpireResponseAutoClose(OriginalLease.PromptId,
                OriginalLease.StackItemId, OriginalLease.PriorityPlayer,
                OriginalLease.DeadlineUtc, OriginalLease.DeadlineUtc));
            var fields = CurrentFields(OriginalLease, OriginalLease.DeadlineUtc);
            switch (damage)
            {
                case "missing-prompt":
                    fields.Remove("promptId");
                    break;
                case "null-stack":
                    fields["stackItemId"] = null;
                    break;
                case "wrong-priority-type":
                    fields["priorityPlayer"] = OriginalLease.PriorityPlayer.ToString();
                    break;
                case "uppercase-deadline":
                    fields["DeadlineUtc"] = fields["deadlineUtc"];
                    fields.Remove("deadlineUtc");
                    break;
                case "wrong-observed-type":
                    fields["observedAtUtc"] = 20261006;
                    break;
                case "conflicting-alias":
                    fields["PromptId"] = OriginalLease.PromptId + "-different";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(damage));
            }
            return JsonSerializer.Serialize(fields);
        }

        internal L12ResponseAutoCloseLease BeginFreshEmptyResponseWindow()
        {
            Engine.State.PendingPrompts.Clear();
            Engine.State.EffectStack.Clear();
            Engine.State.ResponseWindow = null;
            var stack = new L12StackItem
            {
                StackItemId = "scope-stack-" + Guid.NewGuid().ToString("N"),
                Controller = 1,
                SourceInstanceId = "scope-source",
                SourceCardId = "S01-0103",
                SourceName = "scope source",
                Trigger = "reaction",
                Text = "response auto-close scope",
            };
            Engine.State.ActivePlayer = 0;
            Engine.State.EffectStack.Add(stack);
            typeof(L12GameEngine).GetMethod("BeginResponseWindow",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Engine, [stack]);
            return Assert.IsType<L12ResponseAutoCloseLease>(Engine.CaptureResponseAutoCloseLease());
        }

        private string ExpireAndBuildCommand(L12ResponseAutoCloseLease lease, bool legacy,
            DateTimeOffset observedAtUtc)
        {
            Assert.True(Engine.TryExpireResponseAutoClose(lease.PromptId,
                lease.StackItemId, lease.PriorityPlayer, lease.DeadlineUtc, observedAtUtc));
            var fields = CurrentFields(lease, observedAtUtc);
            if (legacy)
            {
                fields["PromptId"] = fields["promptId"];
                fields["StackItemId"] = fields["stackItemId"];
                fields["PriorityPlayer"] = fields["priorityPlayer"];
                fields.Remove("promptId");
                fields.Remove("stackItemId");
                fields.Remove("priorityPlayer");
            }
            return JsonSerializer.Serialize(fields);
        }

        private static Dictionary<string, object?> CurrentFields(
            L12ResponseAutoCloseLease lease, DateTimeOffset observedAtUtc) => new()
        {
            ["type"] = "responseAutoClose",
            ["promptId"] = lease.PromptId,
            ["stackItemId"] = lease.StackItemId,
            ["priorityPlayer"] = lease.PriorityPlayer,
            ["deadlineUtc"] = lease.DeadlineUtc,
            ["observedAtUtc"] = observedAtUtc,
        };

        private sealed class MutableClock(DateTimeOffset now)
        {
            internal DateTimeOffset Now { get; set; } = now;
        }

        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            var target = Path.GetFullPath(_directory);
            var parent = Path.GetFullPath(Path.GetTempPath())
                             .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(target).StartsWith("l12-response-autoclose-compat-",
                    StringComparison.Ordinal))
                throw new InvalidOperationException("response auto-close fixture cleanup scope mismatch");
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try { Directory.Delete(target, true); return; }
                catch (IOException) when (attempt < 4) { await Task.Delay(40 * (attempt + 1)); }
            }
        }
    }
}
