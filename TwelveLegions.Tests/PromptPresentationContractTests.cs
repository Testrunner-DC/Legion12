using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PromptPresentationContractTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [Trait("L12Evidence", "prompt-presentation:authoritative-owner-contract")]
    public void PromptCreationPublishesTheCompleteOwnerPresentationContract()
    {
        var (game, _) = BeginPrivateDecision();

        var owner = Serialize(game.SnapshotFor(0));
        var prompt = Assert.Single(owner.GetProperty("prompts").EnumerateArray());
        var presentation = prompt.GetProperty("presentation");

        Assert.False(string.IsNullOrWhiteSpace(presentation.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(presentation.GetProperty("situation").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(presentation.GetProperty("instruction").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(presentation.GetProperty("waitingSummary").GetString()));
        var labels = prompt.GetProperty("choiceLabels");
        Assert.Equal("不发动", labels.GetProperty("mode:none").GetString());
        Assert.Equal("发动", labels.GetProperty("mode:use").GetString());
        var consequences = presentation.GetProperty("choiceConsequences");
        Assert.Empty(consequences.EnumerateObject());
    }

    [Fact]
    [Trait("L12Evidence", "prompt-presentation:recipient-privacy-matrix")]
    public void OnlyAuthorizedOperatorsReceiveDetailsWhileOtherRecipientsReceiveASafeWaitingSummary()
    {
        const string privateDetail = "仅操作者可见：弃置哪张秘密手牌并抽2张牌";
        var (game, _) = BeginPrivateDecision(privateDetail);

        var owner = Serialize(game.SnapshotFor(0));
        var opponent = Serialize(game.SnapshotFor(1));
        var spectator = Serialize(game.SnapshotForSpectator());
        var referee = Serialize(game.SnapshotForReferee());
        var gm = Serialize(game.SnapshotForGm(1));

        Assert.Equal(privateDetail, Assert.Single(owner.GetProperty("prompts").EnumerateArray())
            .GetProperty("text").GetString());
        Assert.Equal(privateDetail, Assert.Single(gm.GetProperty("prompts").EnumerateArray())
            .GetProperty("text").GetString());
        Assert.Empty(opponent.GetProperty("prompts").EnumerateArray());
        Assert.Empty(spectator.GetProperty("prompts").EnumerateArray());
        Assert.Empty(referee.GetProperty("prompts").EnumerateArray());

        AssertSafeWaitingSummary(opponent);
        AssertSafeWaitingSummary(spectator);
        AssertSafeWaitingSummary(referee);
        Assert.Equal(JsonValueKind.Null, gm.GetProperty("waitingPrompt").ValueKind);
    }

    [Fact]
    [Trait("L12Evidence", "prompt-presentation:checkpoint-compatible")]
    public void PresentationSurvivesCheckpointRestoreWithoutChangingItsRecipientBoundary()
    {
        const string privateDetail = "仅操作者可见：选择一张秘密手牌";
        var (game, _) = BeginPrivateDecision(privateDetail);
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);

        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), random,
            game.CardFactSignalSequence, game.AutoPassEmptyResponses, game.ConcealHiddenResponseAvailability);

        var ownerPrompt = Assert.Single(Serialize(restored.SnapshotFor(0))
            .GetProperty("prompts").EnumerateArray());
        Assert.True(ownerPrompt.TryGetProperty("presentation", out var presentation));
        Assert.Equal("仅操作者可见", presentation.GetProperty("title").GetString());
        Assert.Contains("选择一张秘密手牌", presentation.GetProperty("situation").GetString(),
            StringComparison.Ordinal);
        AssertSafeWaitingSummary(Serialize(restored.SnapshotFor(1)));
        AssertSafeWaitingSummary(Serialize(restored.SnapshotForSpectator()));
    }

    [Fact]
    [Trait("L12Evidence", "prompt-presentation:legacy-checkpoint-fallback")]
    public void LegacyCheckpointWithoutPresentationStillRestoresForClientSideNaturalLanguageFallback()
    {
        var (game, _) = BeginPrivateDecision();
        var checkpoint = JsonNode.Parse(game.SerializeFullState())!.AsObject();
        var legacyPrompt = checkpoint["PendingPrompts"]!.AsArray()[0]!.AsObject();
        Assert.True(legacyPrompt.Remove("Presentation"));
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);

        var restored = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint.ToJsonString(), random,
            game.CardFactSignalSequence, game.AutoPassEmptyResponses, game.ConcealHiddenResponseAvailability);

        Assert.Null(Assert.Single(restored.State.PendingPrompts).Presentation);
        var projected = Assert.Single(Serialize(restored.SnapshotFor(0))
            .GetProperty("prompts").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, projected.GetProperty("presentation").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(projected.GetProperty("text").GetString()));
    }

    [Fact]
    [Trait("L12Evidence", "prompt-presentation:disaster-public-trial-owned")]
    public void NumericNarrativeKeepsDisasterPublicAndTrialProgressPlayerOwned()
    {
        var (_, disasterPrompt) = BeginPrivateDecision("天灾结算：天灾值 3➡️4");
        var (_, trialPrompt) = BeginPrivateDecision("试炼推进：我方试炼进度 0➡️2");

        Assert.Equal("天灾值 3➡️4", Assert.IsType<L12PromptPresentation>(disasterPrompt.Presentation).Situation);
        Assert.DoesNotContain("我方天灾", disasterPrompt.Presentation!.Situation, StringComparison.Ordinal);
        Assert.DoesNotContain("对方天灾", disasterPrompt.Presentation.Situation, StringComparison.Ordinal);
        Assert.Equal("我方试炼进度 0➡️2",
            Assert.IsType<L12PromptPresentation>(trialPrompt.Presentation).Situation);
    }

    private static void AssertSafeWaitingSummary(JsonElement snapshot)
    {
        var waiting = snapshot.GetProperty("waitingPrompt");
        Assert.Equal(0, waiting.GetProperty("playerIndex").GetInt32());
        var summary = waiting.GetProperty("waitingSummary").GetString();
        Assert.False(string.IsNullOrWhiteSpace(summary));
        Assert.DoesNotContain("甲", summary!, StringComparison.Ordinal);
        Assert.StartsWith("对手正在", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("秘密手牌", summary, StringComparison.Ordinal);
        Assert.False(waiting.TryGetProperty("title", out _));
        Assert.False(waiting.TryGetProperty("situation", out _));
        Assert.False(waiting.TryGetProperty("instruction", out _));
        Assert.False(waiting.TryGetProperty("choiceConsequences", out _));
        Assert.False(waiting.TryGetProperty("validChoices", out _));
    }

    private static (L12GameEngine Game, L12Prompt Prompt) BeginPrivateDecision(
        string text = "密令：是否弃置1张秘密手牌并抽2张牌？")
    {
        var game = new L12GameEngine(Catalog, "prompt-presentation", "PROMPT-A", 20260927,
            ["甲", "乙"], [0, 0], skipPreparation: true, stateFormatVersion: 2);
        var method = typeof(L12GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "CreatePrompt"
                && candidate.GetParameters().Length == 10);
        var prompt = Assert.IsType<L12Prompt>(method.Invoke(game,
            [0, "option", text, new[] { "mode:none", "mode:use" }, 1, 1,
                "prompt-presentation", null, true, null]));
        return (game, prompt);
    }

    private static JsonElement Serialize(object value) => JsonSerializer.SerializeToElement(value, WireJson);
}
