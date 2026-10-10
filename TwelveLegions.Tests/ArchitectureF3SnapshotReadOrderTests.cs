using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

/// <summary>
/// F3 is evidence gathering, not a change to snapshot or persistence semantics.
/// Every recipient receives the same projection regardless of who read first.
/// </summary>
public sealed class ArchitectureF3SnapshotReadOrderTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset FrozenNow = new(2026, 10, 2, 3, 0, 0, TimeSpan.Zero);
    private static readonly string[] Readers = ["player0", "player1", "spectator", "referee"];

    [Theory]
    [InlineData("initiative-prompt")]
    [InlineData("mulligan")]
    [InlineData("active")]
    [InlineData("recovered-active")]
    public void NormalRecipientReadOrdersLeaveAuthorityAndEachProjectionUnchanged(string scenario)
    {
        var baseline = Capture(scenario, Readers);
        Assert.Equal(baseline.BeforeState, baseline.AfterState);
        Assert.Equal(baseline.BeforeRevision, baseline.AfterRevision);
        Assert.Equal(baseline.BeforeEvents, baseline.AfterEvents);
        Assert.Equal(baseline.BeforePrompts, baseline.AfterPrompts);

        foreach (var order in Permutations(Readers))
        {
            var actual = Capture(scenario, order);
            Assert.Equal(baseline, actual);
        }
    }

    [Fact]
    public void DeliberatelyOrphanedActivationPromptReconcilesOnceRegardlessOfFirstReader()
    {
        var baseline = Capture("orphaned-prompt", Readers);
        Assert.NotEqual(baseline.BeforeState, baseline.AfterState);
        Assert.Equal(baseline.BeforeRevision + 1, baseline.AfterRevision);
        Assert.NotEqual(baseline.BeforeEvents, baseline.AfterEvents);
        Assert.NotEqual(baseline.BeforePrompts, baseline.AfterPrompts);

        foreach (var order in Permutations(Readers))
            Assert.Equal(baseline, Capture("orphaned-prompt", order));
    }

    private static ReadManifest Capture(string scenario, IReadOnlyList<string> order)
    {
        var game = Create(scenario);
        var beforeState = Digest(game.SerializeFullState());
        var beforeRevision = game.State.Revision;
        var beforeEvents = Digest(JsonSerializer.Serialize(game.State.Events, WireJson));
        var beforePrompts = Digest(JsonSerializer.Serialize(game.State.PendingPrompts, WireJson));
        var views = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reader in order)
        {
            views[reader] = Digest(JsonSerializer.Serialize(Snapshot(game, reader), WireJson));
            // Repeated reads of the same recipient must not advance authority or change its view.
            Assert.Equal(views[reader], Digest(JsonSerializer.Serialize(Snapshot(game, reader), WireJson)));
        }
        var afterState = Digest(game.SerializeFullState());
        var afterRevision = game.State.Revision;
        var afterEvents = Digest(JsonSerializer.Serialize(game.State.Events, WireJson));
        var afterPrompts = Digest(JsonSerializer.Serialize(game.State.PendingPrompts, WireJson));
        // ComputeStateHash prepares a projection too. Call it only after the reader-order
        // observation, so a repair it performs cannot be misattributed to the first reader.
        var afterHash = game.ComputeStateHash();
        Assert.Equal(afterState, Digest(game.SerializeFullState()));
        return new ReadManifest(beforeState, afterState, afterHash,
            beforeRevision, afterRevision, beforeEvents,
            afterEvents, beforePrompts, afterPrompts,
            views["player0"], views["player1"], views["spectator"], views["referee"]);
    }

    private static object Snapshot(L12GameEngine game, string reader) => reader switch
    {
        "player0" => game.SnapshotFor(0),
        "player1" => game.SnapshotFor(1),
        "spectator" => game.SnapshotForSpectator(),
        "referee" => game.SnapshotForReferee(),
        _ => throw new ArgumentOutOfRangeException(nameof(reader)),
    };

    private static L12GameEngine Create(string scenario)
    {
        var skipPreparation = scenario != "initiative-prompt";
        var game = new L12GameEngine(Catalog, "architecture-f3-order", "F3ORDER", 20261002,
            ["甲", "乙"], [0, 1], skipPreparation: skipPreparation,
            disasterMode: "none", stateFormatVersion: 2, utcNow: () => FrozenNow);
        if (scenario is "active" or "recovered-active")
        {
            Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
            Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        }
        if (scenario == "recovered-active")
        {
            game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
                Assert.IsType<L12RandomState>(game.RandomState), game.CardFactSignalSequence,
                game.AutoPassEmptyResponses, game.ConcealHiddenResponseAvailability,
                utcNow: () => FrozenNow);
        }
        if (scenario == "orphaned-prompt")
        {
            game.State.PendingPrompts.Add(new L12Prompt
            {
                PromptId = "architecture-f3-orphan", PlayerIndex = 0, Kind = "card", Text = "孤立提示",
                ValidChoices = ["missing-card"], MinChoose = 1, MaxChoose = 1, IsPrivate = true,
                Continuation = "pending-activation", ActivationId = "missing-activation",
                SourceInstanceId = "missing-source", SourceCardId = "S01-0001", Step = 0,
                CreatedRevision = game.State.Revision, Controller = 0,
                Data = new Dictionary<string, string> { ["activationId"] = "missing-activation" },
            });
        }
        return game;
    }

    private static IEnumerable<string[]> Permutations(IReadOnlyList<string> items)
    {
        if (items.Count == 0) { yield return []; yield break; }
        for (var index = 0; index < items.Count; index++)
        {
            var first = items[index];
            var rest = items.Where((_, at) => at != index).ToArray();
            foreach (var tail in Permutations(rest)) yield return [first, .. tail];
        }
    }

    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record ReadManifest(string BeforeState, string AfterState, string AfterHash,
        long BeforeRevision, long AfterRevision, string BeforeEvents,
        string AfterEvents, string BeforePrompts, string AfterPrompts, string Player0,
        string Player1, string Spectator, string Referee);
}
