using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RelicZoneLimitLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence("S01-0216:ability:static:bf632dc8776cd134", "exact-card-family", "primary-occupied")]
    [L12AbilityEvidence("S01-0217:ability:static:bf632dc8776cd134", "exact-card-family", "primary-occupied")]
    [L12AbilityEvidence("S01-0218:ability:static:bf632dc8776cd134", "exact-card-family", "primary-occupied")]
    [L12AbilityEvidence("S01-0219:ability:static:bf632dc8776cd134", "exact-card-family", "primary-occupied")]
    [L12AbilityEvidence("S01-0220:ability:static:bf632dc8776cd134", "exact-card-family", "primary-occupied")]
    public void RelicZoneLimitExemptDefinitionsMatchTheClosedFamily()
    {
        var actual = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.ExecutionModel == "continuous"
                && L12StructuredCardSemantics.IgnoresRelicZoneLimit(ability.CardId))
            .Select(ability => ability.AbilityId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(EffectLifecycleProfiles.RelicZoneLimitExemptAbilityIds
            .OrderBy(id => id, StringComparer.Ordinal), actual);
        Assert.All(EffectLifecycleProfiles.RelicZoneLimitExemptAbilityIds, id =>
        {
            var cardId = id[..id.IndexOf(":ability:", StringComparison.Ordinal)];
            Assert.Equal("artifact", Catalog.Cards[cardId].CardType);
        });
        Assert.False(L12StructuredCardSemantics.IgnoresRelicZoneLimit("S02-0305"));
    }

    [Theory]
    [InlineData("S01-0216")]
    [InlineData("S01-0217")]
    [InlineData("S01-0218")]
    [InlineData("S01-0219")]
    [InlineData("S01-0220")]
    [L12AbilityEvidence("S01-0216:ability:static:bf632dc8776cd134",
        "normal", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-0217:ability:static:bf632dc8776cd134",
        "normal", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-0218:ability:static:bf632dc8776cd134",
        "normal", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-0219:ability:static:bf632dc8776cd134",
        "normal", "duplicate-submit", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-0220:ability:static:bf632dc8776cd134",
        "normal", "duplicate-submit", "reconnect", "presentation-consumers")]
    public void HandPlayKeepsPrimaryRelicAndPlacesEveryExemptArtifactInExtraZone(string cardId)
    {
        var game = Create(71400, stateFormatVersion: 2);
        var player = game.State.Players[0];
        var primary = Card("S01-0215", "primary-relic");
        var canopic = Card(cardId, $"exempt-{cardId}", "名称已变化但身份不变");
        player.Relic = primary;
        player.Hand.Add(canopic);

        var result = game.Handle(0, new L12Command("playCard", canopic.InstanceId));

        Assert.True(result.Accepted, result.Error);
        Assert.Same(primary, player.Relic);
        Assert.Contains(canopic, player.ExtraRelics);
        Assert.DoesNotContain(primary, player.Graveyard);

        var snapshotJson = System.Text.Json.JsonSerializer.Serialize(game.SnapshotFor(0));
        Assert.Contains(canopic.InstanceId, snapshotJson, StringComparison.Ordinal);
        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        var restored = game.State.Players[0];
        Assert.Equal(primary.InstanceId, restored.Relic?.InstanceId);
        Assert.Contains(restored.ExtraRelics, card => card.InstanceId == canopic.InstanceId);

        var duplicate = game.Handle(0, new L12Command("playCard", canopic.InstanceId));
        Assert.False(duplicate.Accepted);
        Assert.Single(restored.ExtraRelics, card => card.InstanceId == canopic.InstanceId);
    }

    [Fact]
    [L12AbilityEvidence("S01-0216:ability:static:bf632dc8776cd134", "primary-empty")]
    public void EmptyPrimarySlotReceivesTheFirstExemptArtifact()
    {
        var game = Create(71401);
        var player = game.State.Players[0];
        var canopic = Card("S01-0216", "first-canopic");
        player.Hand.Add(canopic);

        var result = game.Handle(0, new L12Command("playCard", canopic.InstanceId));

        Assert.True(result.Accepted, result.Error);
        Assert.Same(canopic, player.Relic);
        Assert.Empty(player.ExtraRelics);
    }

    [Fact]
    [L12AbilityEvidence("S01-0216:ability:static:bf632dc8776cd134", "ordinary-artifact-replaces")]
    public void ANameContainingCanopicDoesNotExemptAnUnrelatedArtifact()
    {
        var game = Create(71402);
        var player = game.State.Players[0];
        var primary = Card("S01-0215", "ordinary-primary");
        var ordinary = Card("S02-0305", "spoofed-name-artifact", "卡诺匹斯伪物");
        player.Relic = primary;
        player.Hand.Add(ordinary);

        var result = game.Handle(0, new L12Command("playCard", ordinary.InstanceId));

        Assert.True(result.Accepted, result.Error);
        Assert.Same(ordinary, player.Relic);
        Assert.Contains(primary, player.Graveyard);
        Assert.DoesNotContain(ordinary, player.ExtraRelics);
    }

    [Fact]
    [L12AbilityEvidence("S01-0218:ability:static:bf632dc8776cd134", "effect-generated-play")]
    public void EffectGeneratedArtifactPlayUsesTheSameExemptionIdentity()
    {
        var game = Create(71403);
        var player = game.State.Players[0];
        var primary = Card("S01-0215", "generated-primary");
        var canopic = Card("S01-0218", "generated-canopic", "效果打出的改名圣物");
        player.Relic = primary;
        player.Library.Add(canopic);
        var parent = new L12StackItem
        {
            StackItemId = "generated-canopic-parent",
            Controller = 0,
            SourceInstanceId = "generated-source",
            SourceCardId = "S01-0001",
            SourceName = "测试来源",
            Trigger = "play",
            Text = "效果生成打出",
        };
        game.State.EffectStack.Add(parent);

        Invoke(game, "BeginEffectGeneratedFreePlay", 0, canopic, parent, "library", "测试效果");

        Assert.Same(primary, player.Relic);
        Assert.Contains(canopic, player.ExtraRelics);
        Assert.DoesNotContain(canopic, player.Library);
        Assert.DoesNotContain(primary, player.Graveyard);
    }

    [Fact]
    [L12AbilityEvidence("S01-0217:ability:static:bf632dc8776cd134", "zhuge-generated-play")]
    public void ZhugeGeneratedArtifactPlayUsesTheSharedPlacementKernel()
    {
        var game = Create(71404);
        var player = game.State.Players[0];
        var primary = Card("S01-0215", "zhuge-primary");
        var canopic = Card("S01-0217", "zhuge-canopic");
        player.Relic = primary;
        player.Resolving.Add(canopic);
        var item = new L12StackItem
        {
            StackItemId = "zhuge-canopic-parent",
            Controller = 0,
            SourceInstanceId = "zhuge-source",
            SourceCardId = "S01-0111",
            SourceName = "诸葛亮",
            Trigger = "attack",
            Text = "诸葛亮进攻时效果",
        };
        item.Data["zhuge-card"] = canopic.InstanceId;
        game.State.EffectStack.Add(item);
        var prompt = new L12Prompt
        {
            PromptId = "zhuge-canopic-prompt",
            PlayerIndex = 0,
            Kind = "choice",
            Text = "是否打出",
            ValidChoices = ["play", "hand"],
            MinChoose = 1,
            MaxChoose = 1,
            Continuation = "stack",
            StackItemId = item.StackItemId,
            Data = new Dictionary<string, string> { ["action"] = "zhuge-artifact" },
        };

        Invoke(game, "TryContinueS1Extended", item, prompt, new List<string> { "play" },
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "play"));

        Assert.Same(primary, player.Relic);
        Assert.Contains(canopic, player.ExtraRelics);
        Assert.DoesNotContain(canopic, player.Resolving);
        Assert.DoesNotContain(primary, player.Graveyard);
    }

    [Fact]
    [L12AbilityEvidence("S01-0219:ability:static:bf632dc8776cd134", "gm-play")]
    public void GmPlacementUsesTheSharedArtifactZoneRule()
    {
        var game = Create(71405);
        var player = game.State.Players[0];
        var primary = Card("S01-0215", "gm-primary");
        player.Relic = primary;

        var result = game.HandleGm(new L12GmCommand("placeCard", 0, "S01-0219", TriggerEffects: false));

        Assert.True(result.Accepted, result.Error);
        Assert.Same(primary, player.Relic);
        Assert.Single(player.ExtraRelics, card => card.CardId == "S01-0219");
        Assert.DoesNotContain(primary, player.Graveyard);
    }

    private static L12GameEngine Create(int seed, int stateFormatVersion = 0)
    {
        var game = new L12GameEngine(Catalog, "relic-zone-limit", "RELIC-LIMIT", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: stateFormatVersion);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.ExtraRelics.Clear();
            player.Relic = null;
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, string? name = null)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = name ?? definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = 0,
        };
    }

    private static void Invoke(L12GameEngine game, string methodName, params object?[] arguments)
    {
        var method = typeof(L12GameEngine).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(game, arguments);
    }
}
