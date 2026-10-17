using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DisasterSetupFactCoverageAndPrivacyTests
{
    [Theory]
    [InlineData("none", "not-applicable", 0)] [InlineData("random", "exact", 4)]
    public async Task RandomAndNoneHaveNoSelfSelectedPreferenceSamples(string mode, string coverage, int composition)
    {
        await using var f = await DisasterFactFixture.Create(disaster: mode);
        await f.Prepare();
        await f.End();
        var facts = await f.Read();
        Assert.Equal(coverage, facts.Coverage);
        Assert.Empty(facts.Decisions);
        Assert.Equal(composition, facts.Composition.Length);
        Assert.Equal(new L12DisasterSetupCoverage(1, 0, 0, 0, 1),
            await f.Recorder.ReadDisasterSetupCoverageAsync(new([], [])));
    }

    [Fact]
    public async Task OldSkippedAndLateRecordingRemainUnknownAndInterruptedIsNotARejection()
    {
        await using var skipped = await DisasterFactFixture.Create(skip: true);
        await skipped.End();
        Assert.Equal("unknown", (await skipped.Read()).Coverage);
        Assert.Equal(new L12DisasterSetupCoverage(1, 0, 1, 0, 0),
            await skipped.Recorder.ReadDisasterSetupCoverageAsync(new([], [])));
        await using var late = new DisasterFactFixture();
        await late.Recorder.InitializeAsync();
        var prompt = late.Game.State.PendingPrompts[0];
        Assert.True(late.Game.Handle(prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "first")).Accepted);
        await late.Start();
        await late.Prepare();
        await late.End();
        Assert.Equal("unknown", (await late.Read()).Coverage);
        await using var interrupted = await DisasterFactFixture.Create();
        await interrupted.Step();
        await interrupted.End();
        var pending = await interrupted.Read();
        Assert.Equal("partial", pending.Coverage);
        Assert.Equal("interrupted", pending.Status);
        Assert.Null(Assert.Single(pending.Decisions).ChosenCardId);
    }

    [Fact]
    public async Task ActivePrivateFactsAreNotReturnedEvenToStaffAndPublicRolesAreDeniedAfterCompletion()
    {
        await using var f = await DisasterFactFixture.Create();
        while (f.Game.State.DisasterPreparationStep < 4) await f.Step();
        var active = await f.Read();
        Assert.Equal("private", active.Coverage);
        Assert.Empty(active.Decisions);
        Assert.Empty(active.Pool);
        Assert.Null(active.PublicCardId);
        foreach (var role in new[] { "player", "spectator", "referee", "analyst" })
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Recorder.ReadDisasterSetupFactsAsync(
                f.Game.State.MatchId, DisasterFactFixture.Staff with { Role = role }));
        await f.Prepare();
        await f.End();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Recorder.ReadDisasterSetupFactsAsync(
            f.Game.State.MatchId, DisasterFactFixture.Staff with { Role = "player" }));
        Assert.Equal(5, (await f.Read()).Decisions.Length);
        Assert.DoesNotContain("synthetic-account", JsonSerializer.Serialize(await f.Read()));
    }

    [Fact]
    public async Task CorruptCandidatesFailClosedInBothDetailAndAggregateReads()
    {
        await using var f = await DisasterFactFixture.Create();
        await f.Prepare();
        await f.End();
        await f.Execute("DELETE FROM match_disaster_candidates WHERE step=0 AND card_id=(SELECT MIN(card_id) FROM match_disaster_candidates WHERE step=0);");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Read());
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Recorder.ReadDisasterSetupCoverageAsync(new([], [])));
    }

    [Fact]
    public async Task CommittedExclusionsAreRequiredAndAppliedToCoverage()
    {
        await using var f = await DisasterFactFixture.Create();
        await f.Prepare();
        await f.End();
        Assert.Equal(new L12DisasterSetupCoverage(1, 1, 0, 0, 0), await f.Recorder.ReadDisasterSetupCoverageAsync(new([], [])));
        Assert.Equal(0, (await f.Recorder.ReadDisasterSetupCoverageAsync(new([f.Game.State.MatchId], []))).Total);
        Assert.Equal(0, (await f.Recorder.ReadDisasterSetupCoverageAsync(new([], ["synthetic-account-b"]))).Total);
        await Assert.ThrowsAsync<L12PlatformStorageUnavailableException>(() => f.Recorder.ReadDisasterSetupCoverageAsync(
            new([], []) { Available = false }));
        await f.Execute("UPDATE matches SET error='synthetic-error';");
        Assert.Equal(0, (await f.Recorder.ReadDisasterSetupCoverageAsync(new([], []))).Total);
    }
}
