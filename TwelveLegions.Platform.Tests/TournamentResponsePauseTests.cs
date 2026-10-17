using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

public sealed class TournamentResponsePauseTests
{
    [Theory]
    [InlineData(0, 20, 25)]
    [InlineData(2, 20, 23)]
    [InlineData(7, 20, 18)]
    public void ResumePreservesOnlyUnconsumedResponseTimeAndReplayHash(int pauseSeconds, int resumeSeconds, int deadlineSeconds)
    {
        var now = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
        var game = Create(now);
        var original = game.SerializeFullState();
        var randomState = game.RandomState!.Value;
        var factSequence = game.CardFactSignalSequence;
        var result = game.ResumeTournamentResponseClock(now.AddSeconds(pauseSeconds), now.AddSeconds(resumeSeconds));
        Assert.True(result.Accepted);
        Assert.Equal(now.AddSeconds(deadlineSeconds), game.State.ResponseWindow!.AutoCloseDeadlineUtc);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, original, randomState,
            factSequence, false, false);
        Assert.True(restored.ReplayRecordedCommand(now.AddSeconds(resumeSeconds),
            () => restored.ResumeTournamentResponseClock(now.AddSeconds(pauseSeconds), now.AddSeconds(resumeSeconds))).Accepted);
        Assert.Equal(game.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void NewlyCreatedPausedWindowDoesNotReceiveEntireEarlierPause()
    {
        var now = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
        var game = Create(now.AddSeconds(10));
        Assert.True(game.ResumeTournamentResponseClock(now, now.AddSeconds(20)).Accepted);
        Assert.Equal(now.AddSeconds(25), game.State.ResponseWindow!.AutoCloseDeadlineUtc);
    }

    [Fact]
    public void OnlyAcceptedAuthorityCanReplayResumeAndReverseTimeIsRejected()
    {
        Assert.True(L12RecordedCommandOrigin.AllowsInternalReplay("tournamentResumeResponse", -1, true));
        Assert.False(L12RecordedCommandOrigin.AllowsInternalReplay("tournamentResumeResponse", 0, true));
        Assert.False(L12RecordedCommandOrigin.AllowsInternalReplay("tournamentResumeResponse", -1, false));
        var now = DateTimeOffset.UtcNow;
        var game = Create(now);
        var before = game.ComputeStateHash();
        Assert.False(game.ResumeTournamentResponseClock(now.AddSeconds(1), now).Accepted);
        Assert.Equal(before, game.ComputeStateHash());
    }

    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
    private static L12GameEngine Create(DateTimeOffset now)
    {
        var catalog = Catalog;
        var deck = catalog.DeckAt(0);
        var game = new L12GameEngine(catalog, "pause-response", "PTEST", 1234,
            ["first", "second"], [deck, deck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: L12PersistenceContract.CurrentStateFormatVersion, utcNow: () => now,
            responseModes: [L12GameEngine.InvalidFiveSecondsResponseMode, L12GameEngine.InvalidFiveSecondsResponseMode]);
        game.State.PendingPrompts.Clear();
        foreach (var player in game.State.Players)
        {
            player.Library.AddRange(player.Hand); player.Hand.Clear();
            foreach (var row in player.Field) player.Library.AddRange(row.OfType<L12CardInstance>());
            player.Field[0] = new L12CardInstance?[3]; player.Field[1] = new L12CardInstance?[3];
        }
        var stack = new L12StackItem { StackItemId = "pause-stack", Controller = 1,
            SourceInstanceId = "source", SourceCardId = "S01-0103", SourceName = "synthetic source",
            Trigger = "reaction", Text = "synthetic response" };
        game.State.ActivePlayer = 0; game.State.EffectStack.Add(stack);
        typeof(L12GameEngine).GetMethod("BeginResponseWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, [stack]);
        Assert.NotNull(game.CaptureResponseAutoCloseLease());
        return game;
    }
}
