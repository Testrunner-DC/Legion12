using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class DisasterPreparationWaitingSummaryRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Fact]
    public void RealDisasterPreparationPublishesStageSpecificWaitingSummary()
    {
        var game = new L12GameEngine(Catalog, "waiting-summary", "WAITING", 102525,
            ["甲", "乙"], [0, 1], skipPreparation: false, stateFormatVersion: 2);
        var initiative = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(initiative.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: initiative.PromptId, Choice: "first")).Accepted);

        var oldCheckpoint = JsonNode.Parse(game.SerializeFullState())!.AsObject();
        oldCheckpoint["PendingPrompts"]!.AsArray()[0]!["Presentation"]!["WaitingSummary"] =
            "对手正在决定是否执行开局效果";
        var restoredOld = L12GameEngine.RestoreCheckpoint(Catalog, oldCheckpoint.ToJsonString(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            game.AutoPassEmptyResponses, game.ConcealHiddenResponseAvailability);
        Assert.Equal("disaster-ban", Assert.Single(restoredOld.State.PendingPrompts).Kind);
        Assert.Equal("对手正在决定是否执行开局效果", JsonSerializer.SerializeToElement(
            restoredOld.SnapshotFor(1 - restoredOld.State.PendingPrompts[0].PlayerIndex), Wire)
            .GetProperty("waitingPrompt").GetProperty("waitingSummary").GetString());

        var expected = new Dictionary<string, string>
        {
            ["disaster-ban"] = "对手正在禁用天灾",
            ["disaster-pick"] = "对手正在选择天灾",
            ["disaster-reveal"] = "对手正在确认随机公开天灾",
        };
        var seen = new HashSet<string>();
        for (var step = 0; step < 12 && game.State.Phase != L12Phase.Mulligan; step++)
        {
            var prompt = game.State.PendingPrompts[0];
            if (expected.TryGetValue(prompt.Kind, out var summary))
            {
                seen.Add(prompt.Kind);
                Assert.Equal(summary, prompt.Presentation?.WaitingSummary);
                var viewer = 1 - prompt.PlayerIndex;
                var receiver = JsonSerializer.SerializeToElement(game.SnapshotFor(viewer), Wire);
                var waiting = receiver.GetProperty("waitingPrompt");
                if (waiting.ValueKind == JsonValueKind.Null)
                {
                    // The public reveal initially asks both players to confirm. The other
                    // player has an owner prompt until their own acknowledgement is sent.
                    Assert.Equal("disaster-reveal", prompt.Kind);
                    Assert.Contains(receiver.GetProperty("prompts").EnumerateArray(), item =>
                        item.GetProperty("kind").GetString() == "disaster-reveal");
                }
                else
                {
                    Assert.Equal(summary, waiting.GetProperty("waitingSummary").GetString());
                    Assert.False(waiting.TryGetProperty("validChoices", out _));
                }
                Assert.Equal(summary, JsonSerializer.SerializeToElement(game.SnapshotForSpectator(), Wire)
                    .GetProperty("waitingPrompt").GetProperty("waitingSummary").GetString());
            }
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt",
                PromptId: prompt.PromptId, Choice: prompt.ValidChoices.FirstOrDefault())).Accepted);
        }
        Assert.True(expected.Keys.ToHashSet().SetEquals(seen));
    }

    [Fact]
    public void TrialOrderingAndRealOptionalOpeningEffectKeepSeparateMeaningsAfterRestore()
    {
        var baseTrialDeck = Catalog.DeckAt(0);
        var trialDeck = new L12PresetDeckDefinition
        {
            Name = "试炼顺序等待测试", MasterId = "S02-06M1",
            CardIds = [.. baseTrialDeck.CardIds], MoraleIds = [.. baseTrialDeck.MoraleIds],
            SpecialIds = [.. Catalog.Cards.Values.Where(card => card.CardType == "trial")
                .Take(2).Select(card => card.Id)],
        };
        var game = new L12GameEngine(Catalog, "trial-waiting", "TRIALWAIT", 102526,
            ["甲", "乙"], [trialDeck, Catalog.DeckAt(0)], skipPreparation: false,
            disasterMode: "none", stateFormatVersion: 2);
        var initiative = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(initiative.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: initiative.PromptId, Choice: "first")).Accepted);
        var trial = Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "trial-order");
        Assert.Equal("对手正在安排试炼顺序", trial.Presentation?.WaitingSummary);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            game.AutoPassEmptyResponses, game.ConcealHiddenResponseAvailability);
        Assert.Equal("对手正在安排试炼顺序", JsonSerializer.SerializeToElement(
            restored.SnapshotForSpectator(), Wire).GetProperty("waitingPrompt")
            .GetProperty("waitingSummary").GetString());

        var baseDeck = Catalog.DeckAt(0);
        var optionalDeck = new L12PresetDeckDefinition
        {
            Name = "真实开局效果对照", MasterId = "S02-03M1",
            CardIds = ["S02-0305", "S02-0301", .. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds], SpecialIds = [.. baseDeck.SpecialIds],
        };
        var optionalGame = new L12GameEngine(Catalog, "optional-waiting", "OPTIONWAIT", 102527,
            ["甲", "乙"], [optionalDeck, baseDeck], skipPreparation: false,
            disasterMode: "none", stateFormatVersion: 2);
        var optionalInitiative = Assert.Single(optionalGame.State.PendingPrompts);
        Assert.True(optionalGame.Handle(optionalInitiative.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: optionalInitiative.PromptId, Choice: "first")).Accepted);
        var ring = Assert.Single(optionalGame.State.PendingPrompts,
            prompt => prompt.Continuation == "setup-s2-ring");
        var hammer = Assert.Single(optionalGame.State.PendingPrompts,
            prompt => prompt.Continuation == "setup-s2-thor-hammer");
        Assert.Equal("对手正在决定是否执行开局效果", ring.Presentation?.WaitingSummary);
        Assert.Equal("对手正在决定是否执行开局效果", hammer.Presentation?.WaitingSummary);
        Assert.Equal("对手正在决定是否执行开局效果", JsonSerializer.SerializeToElement(
            optionalGame.SnapshotFor(1), Wire).GetProperty("waitingPrompt")
            .GetProperty("waitingSummary").GetString());
    }
}
