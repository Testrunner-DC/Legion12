using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PromptCardPresentationSnapshotTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static L12GameEngine Create()
    {
        var game = new L12GameEngine(Catalog, "prompt-card-presentation", "PROMPT-CARDS", 20903,
            ["甲", "乙"], [0, 0], skipPreparation: true);
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Removed.Clear();
            player.Resolving.Clear();
            player.SpecialZones.Trials.Clear();
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, string? imageUrl = "catalog")
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = imageUrl == "catalog" ? definition.ImageUrl : imageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
        };
    }

    private static L12Prompt CreatePrompt(L12GameEngine game, string kind, IEnumerable<string> choices,
        Dictionary<string, string>? data = null, int min = 1, int max = 1)
    {
        var method = typeof(L12GameEngine).GetMethod("CreatePrompt", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return Assert.IsType<L12Prompt>(method.Invoke(game,
            [0, kind, "选择卡牌", choices, min, max, "snapshot-test", null, true, data]));
    }

    private static JsonElement SnapshotPrompt(L12GameEngine game)
        => JsonSerializer.SerializeToElement(game.SnapshotFor(0), JsonOptions)
            .GetProperty("prompts")[0];

    private static void AssertCardMetadata(JsonElement prompt, L12CardInstance card, string zone)
    {
        var data = prompt.GetProperty("data");
        Assert.Equal(card.CardId, data.GetProperty($"{card.InstanceId}:cardId").GetString());
        Assert.Equal(card.Name, data.GetProperty($"{card.InstanceId}:name").GetString());
        Assert.Equal(card.CardType, data.GetProperty($"{card.InstanceId}:cardType").GetString());
        Assert.Equal(zone, data.GetProperty($"{card.InstanceId}:zone").GetString());
    }

    [Fact]
    [Trait("L12Evidence", "prompt-card:top-three-all-visible-invalid-disabled")]
    public void TopThreeSnapshotDisplaysEveryRevealedCardAndEnrichesTheIllegalEntry()
    {
        var game = Create();
        var player = game.State.Players[0];
        var first = Card("ST05-03", "top-three-first");
        var illegal = Card("ST05-02", "top-three-illegal");
        var third = Card("ST05-10", "top-three-third");
        player.Library.AddRange([first, illegal, third]);
        var data = new Dictionary<string, string>
        {
            ["displayCardIds"] = string.Join('|', first.InstanceId, illegal.InstanceId),
        };

        CreatePrompt(game, "card", [first.InstanceId, third.InstanceId], data);
        var prompt = SnapshotPrompt(game);

        Assert.Equal("single-row", prompt.GetProperty("data").GetProperty("layout").GetString());
        Assert.Equal("true", prompt.GetProperty("data").GetProperty("cardSelection").GetString());
        Assert.Equal(string.Join('|', first.InstanceId, illegal.InstanceId, third.InstanceId),
            prompt.GetProperty("data").GetProperty("displayCardIds").GetString());
        Assert.DoesNotContain(illegal.InstanceId,
            prompt.GetProperty("validChoices").EnumerateArray().Select(item => item.GetString()));
        AssertCardMetadata(prompt, first, "牌库");
        AssertCardMetadata(prompt, illegal, "牌库");
        AssertCardMetadata(prompt, third, "牌库");
    }

    [Theory]
    [InlineData("discard", "hand", "手牌")]
    [InlineData("grave-card", "graveyard", "墓地")]
    [InlineData("library-search", "library", "牌库")]
    [InlineData("order", "graveyard", "墓地")]
    [InlineData("optional-cards", "hand", "手牌")]
    [Trait("L12Evidence", "prompt-card:all-card-choice-kinds-share-snapshot-metadata")]
    public void CardChoiceKindsShareOneLayoutAndCompleteMetadata(string kind, string zone, string expectedZone)
    {
        var game = Create();
        var player = game.State.Players[0];
        var card = Card("S01-0104", $"{kind}-card", imageUrl: null);
        switch (zone)
        {
            case "hand": player.Hand.Add(card); break;
            case "library": player.Library.Add(card); break;
            case "graveyard": player.Graveyard.Add(card); break;
        }
        var data = kind == "order"
            ? new Dictionary<string, string> { ["placementMode"] = "all-top-bottom" }
            : null;

        CreatePrompt(game, kind, [card.InstanceId], data);
        var prompt = SnapshotPrompt(game);

        Assert.Equal("single-row", prompt.GetProperty("data").GetProperty("layout").GetString());
        Assert.Equal(card.InstanceId, prompt.GetProperty("data").GetProperty("displayCardIds").GetString());
        AssertCardMetadata(prompt, card, expectedZone);
        Assert.False(prompt.GetProperty("data").TryGetProperty($"{card.InstanceId}:image", out _));
    }

    [Fact]
    [Trait("L12Evidence", "prompt-card:trial-order-is-card-order-trigger-order-is-not")]
    public void OrderingKindsUseCardEvidenceInsteadOfBlindKindMatching()
    {
        var trialGame = Create();
        var trial = Card("S02-06S1", "trial-order-card");
        trialGame.State.Players[0].SpecialZones.Trials.Add(trial);
        CreatePrompt(trialGame, "trial-order", [trial.InstanceId]);
        var trialPrompt = SnapshotPrompt(trialGame);
        Assert.Equal(trial.InstanceId, trialPrompt.GetProperty("data").GetProperty("displayCardIds").GetString());
        AssertCardMetadata(trialPrompt, trial, "试炼区");

        var triggerGame = Create();
        CreatePrompt(triggerGame, "trigger-order", ["trigger-candidate-1", "trigger-candidate-2"],
            new Dictionary<string, string>
            {
                ["trigger-candidate-1"] = "触发效果一",
                ["trigger-candidate-2"] = "触发效果二",
            }, 2, 2);
        var triggerData = SnapshotPrompt(triggerGame).GetProperty("data");
        Assert.False(triggerData.TryGetProperty("displayCardIds", out _));
        Assert.False(triggerData.TryGetProperty("layout", out _));
    }

    [Fact]
    [Trait("L12Evidence", "prompt-card:preview-card-is-enriched-outside-valid-choices")]
    public void PreviewCardOutsideValidChoicesStillHasEnoughMetadataToRenderFromCardId()
    {
        var game = Create();
        var preview = Card("S01-0104", "preview-only-card", imageUrl: null);
        game.State.Players[0].Graveyard.Add(preview);

        CreatePrompt(game, "option", ["mode:use", "mode:none"], new Dictionary<string, string>
        {
            ["previewCardId"] = preview.InstanceId,
        });
        var prompt = SnapshotPrompt(game);

        Assert.DoesNotContain(preview.InstanceId,
            prompt.GetProperty("validChoices").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(preview.InstanceId, prompt.GetProperty("data").GetProperty("previewCardId").GetString());
        AssertCardMetadata(prompt, preview, "墓地");
        Assert.False(prompt.GetProperty("data").TryGetProperty($"{preview.InstanceId}:image", out _));
    }

    [Fact]
    [Trait("L12Evidence", "prompt-card:hidden-hand-remains-anonymous-card-backs")]
    public void HiddenOpponentHandSnapshotUsesAnonymousCardBacksWithoutIdentityMetadata()
    {
        var game = Create();
        var hidden = Card("S01-0104", "secret-opponent-card");
        hidden.OwnerIndex = 1;
        game.State.Players[1].Hand.Add(hidden);
        var method = typeof(L12GameEngine).GetMethod("CreateAnonymousHandChoicePrompt",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var prompt = Assert.IsType<L12Prompt>(method.Invoke(game,
            [0, new[] { hidden }, "opponent-hand-card", "盲选对方1张手牌", 1, 1,
                "hidden-snapshot-test", null, null]));

        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0), JsonOptions);
        var projectedPrompt = snapshot.GetProperty("prompts")[0];
        var slot = Assert.Single(projectedPrompt.GetProperty("validChoices").EnumerateArray()).GetString()!;
        var data = projectedPrompt.GetProperty("data");
        Assert.StartsWith("hidden-hand-slot-", slot, StringComparison.Ordinal);
        Assert.Equal("/assets/l12/card-back-official.png", data.GetProperty($"{slot}:image").GetString());
        Assert.Equal("对方手牌 1", projectedPrompt.GetProperty("choiceLabels").GetProperty(slot).GetString());
        Assert.False(data.TryGetProperty($"{slot}:cardId", out _));
        Assert.False(data.TryGetProperty($"{slot}:name", out _));
        Assert.False(data.TryGetProperty($"{slot}:zone", out _));
        Assert.DoesNotContain(hidden.InstanceId, snapshot.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(hidden.CardId, snapshot.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(hidden.Name, snapshot.ToString(), StringComparison.Ordinal);
        Assert.Single(prompt.HiddenChoiceMap);
    }
}
