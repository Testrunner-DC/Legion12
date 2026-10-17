using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class DisasterSetupFactProducerTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-17T00:00:00Z");

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void RealFirstAndSecondPlayersProduceExactOpportunitiesWithoutChangingStateRandomOrOldFacts(int first)
    {
        var game = new L12GameEngine(Catalog, "disaster-producer", "DSC001", 4071,
            ["synthetic-a", "synthetic-b"], [0, 0], stateFormatVersion: 2, utcNow: () => Now);
        Assert.NotNull(game.DisasterCaptureSeed);
        var observations = new List<L12DisasterSetupObservation>();
        for (var index = 0; index < 12 && game.State.Phase is L12Phase.Initiative or L12Phase.DisasterPreparation; index++)
        {
            var twin = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
                game.CardFactSignalSequence, utcNow: () => Now);
            var prompt = game.State.PendingPrompts[0];
            var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: prompt.Kind == "initiative"
                    ? [first == game.State.DiceWinner ? "first" : "second"]
                    : prompt.MinChoose == 0 ? [] : [prompt.ValidChoices[0]]);
            Assert.True(game.Handle(prompt.PlayerIndex, command).Accepted);
            Assert.True(twin.Handle(prompt.PlayerIndex, command).Accepted);
            Assert.Equal(twin.SerializeFullState(), game.SerializeFullState());
            Assert.Equal(twin.ComputeStateHash(), game.ComputeStateHash());
            Assert.Equal(twin.RandomState, game.RandomState);
            Assert.Equal(twin.CardFactSignalSequence, game.CardFactSignalSequence);
            observations.AddRange(game.DisasterSetupObservations);
            game.ClearDisasterSetupObservations();
        }
        Assert.Equal(first, game.State.FirstPlayer);
        var offers = observations.Where(item => item.Kind == "offer").ToArray();
        Assert.Equal(new[] { 0, 1, 2, 4, 5 }, offers.Select(item => item.Step));
        Assert.Equal(new[] { first, 1 - first, first, first, 1 - first }, offers.Select(item => item.Actor!.Value));
        var pool = game.DisasterCaptureSeed!.Pool;
        Assert.Equal(new[] { pool.Length, pool.Length - 1, pool.Length - 2, 3, 2 },
            offers.Select(item => item.Candidates!.Length));
        Assert.All(offers, item => Assert.DoesNotContain("S01-DS10", item.Candidates!));
        Assert.Empty(offers[3].Candidates!.Intersect(offers[4].Candidates!));
        Assert.Equal(5, observations.Count(item => item.Kind == "decision"));
        Assert.Single(observations, item => item.Kind == "public");
        var composition = Assert.Single(observations, item => item.Kind == "complete").Composition!;
        Assert.Equal(4, composition.Length);
        Assert.Equal(2, composition.Count(item => item.Source == "player-pick"));
        Assert.Single(composition, item => item.Source == "fixed");
        Assert.Single(composition, item => item.Source == "random-public");
        Assert.DoesNotContain("DisasterSetupObservations", JsonSerializer.Serialize(game.State));
    }

    [Theory]
    [InlineData("none", 0)] [InlineData("random", 4)]
    public void NonDraftModesHaveCompositionButNoChoiceOpportunities(string mode, int count)
    {
        var game = new L12GameEngine(Catalog, "disaster-mode", "DSC002", 4072,
            ["synthetic-a", "synthetic-b"], [0, 0], disasterMode: mode, stateFormatVersion: 2, utcNow: () => Now);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "first")).Accepted);
        var complete = Assert.Single(game.DisasterSetupObservations);
        Assert.Equal("complete", complete.Kind);
        Assert.Equal(count, complete.Composition!.Length);
    }

    [Fact]
    public void SeasonUsesActualBuiltPoolAndFreezesItsCardIdentities()
    {
        var ids = Enumerable.Range(1, 9).Select(index => $"S01-DS{index:00}")
            .Append("S01-DS10").Append("not-in-catalog").ToArray();
        var policy = L12OperationsPolicyDefaults.FromCatalog(Catalog) with { DisasterCardIds = ids };
        var game = new L12GameEngine(Catalog, "season", "DSC005", 4075, ["a", "b"], [0, 0],
            disasterMode: "season", operationsPolicy: policy, stateFormatVersion: 2);
        var expected = game.State.DisasterPool.Select(card => card.CardId).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(9, expected.Length);
        ids[0] = "S02-DS01";
        Assert.Equal(expected, game.DisasterCaptureSeed!.Pool);
        Assert.DoesNotContain("S01-DS10", game.DisasterCaptureSeed.Pool);
        Assert.DoesNotContain("not-in-catalog", game.DisasterCaptureSeed.Pool);
    }

    [Fact]
    public void SkippedPreparationAndOldStateDoNotClaimNativeCapture()
    {
        var skipped = new L12GameEngine(Catalog, "skip", "DSC003", 4073, ["a", "b"], [0, 0],
            skipPreparation: true, stateFormatVersion: 2);
        Assert.Null(skipped.DisasterCaptureSeed);
        var old = new L12GameEngine(Catalog, "old", "DSC004", 4074, ["a", "b"], [0, 0]);
        Assert.Null(old.DisasterCaptureSeed);
    }
}
