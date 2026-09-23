using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class GameSetupLifecycleProfileTests
{
    private const string RingAbilityId = "S02-0305:ability:game-setup:cf14affeb486a9f7";
    private const string ThorAbilityId = "S02-03M1:ability:game-setup:46b2a85c54cecc56";
    private const string TiantingSetupId = "S01-01D1:ability:setup:281db2829152b981";
    private const string SunSetupId = "S01-02D1:ability:setup:281db2829152b981";
    private const string AsgardSetupId = "S01-03D1:ability:setup:281db2829152b981";
    private const string TakamagaharaSetupId = "S01-04D1:ability:setup:281db2829152b981";
    private const string OlympusSetupId = "S02-05D1:ability:setup:281db2829152b981";
    private const string OtherworldSetupId = "S02-06D1:ability:setup:281db2829152b981";

    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

    [Fact]
    public void AllDivinitySetupSegmentsAreIndependent()
    {
        var actual = Catalog.AtomicEffects.All
            .Where(card => card.CardType == "divinity")
            .Select(card => Assert.Single(card.Abilities, ability => ability.Trigger == "setup").AbilityId)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            TiantingSetupId, SunSetupId, AsgardSetupId, TakamagaharaSetupId, OlympusSetupId, OtherworldSetupId,
        }.Order(StringComparer.Ordinal), actual);
    }

    [Fact]
    [L12AbilityEvidence(RingAbilityId, "normal", "reconnect", "duplicate-submit", "presentation-consumers")]
    [L12AbilityEvidence(ThorAbilityId, "normal", "reconnect", "duplicate-submit", "presentation-consumers")]
    public void OptionalSetupPromptsRestoreAndApplyBeforeTheStartingHandIsDrawn()
    {
        var baseDeck = Catalog.DeckAt(0);
        var setupDeck = new L12PresetDeckDefinition
        {
            Name = "开局生命周期测试牌库",
            MasterId = "S02-03M1",
            CardIds = ["S02-0305", "S02-0301", .. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "setup-profile", "SETUP", 93301,
            ["甲", "乙"], [setupDeck, baseDeck], skipPreparation: false, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        var initiative = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(initiative.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: initiative.PromptId, Choice: "first")).Accepted);

        var beforeRestore = game.State.PendingPrompts.Where(prompt => prompt.PlayerIndex == 0
            && prompt.Continuation.StartsWith("setup-s2-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, beforeRestore.Length);
        Assert.Empty(game.State.Players[0].Hand);
        Assert.Equal(2, game.SnapshotFor(0).Prompts.Length);

        game = Restore(game);
        var ring = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "setup-s2-ring");
        var ringResolved = game.Handle(0,
            new L12Command("resolvePrompt", PromptId: ring.PromptId, Choice: "yes"));
        Assert.True(ringResolved.Accepted, ringResolved.Error);
        Assert.Equal("S02-0305", game.State.Players[0].Relic?.CardId);
        Assert.Empty(game.State.Players[0].Hand);
        Assert.False(game.Handle(0,
            new L12Command("resolvePrompt", PromptId: ring.PromptId, Choice: "yes")).Accepted);

        var hammer = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "setup-s2-thor-hammer");
        var hammerResolved = game.Handle(0,
            new L12Command("resolvePrompt", PromptId: hammer.PromptId, Choice: "yes"));
        Assert.True(hammerResolved.Accepted, hammerResolved.Error);
        Assert.False(game.Handle(0,
            new L12Command("resolvePrompt", PromptId: hammer.PromptId, Choice: "yes")).Accepted);

        var owner = game.State.Players[0];
        Assert.Equal(4, owner.Hand.Count);
        Assert.Contains(owner.Hand, card => card.CardId == "S02-0301");
        Assert.Equal(L12Phase.Mulligan, game.State.Phase);
        Assert.Contains(game.State.Events, entry => entry.Type == "setup"
            && entry.Cards.Any(card => card.CardId == "S02-0305"));
        Assert.Contains(game.State.Events, entry => entry.Type == "reveal"
            && entry.Cards.Any(card => card.CardId == "S02-0301"));
        using var publicProjection = JsonDocument.Parse(JsonSerializer.Serialize(game.SnapshotFor(0)));
        var projectedOwner = publicProjection.RootElement.GetProperty("Players")[0];
        Assert.Equal("S02-0305", projectedOwner.GetProperty("Relic").GetProperty("CardId").GetString());
        Assert.Contains(projectedOwner.GetProperty("hand").EnumerateArray(),
            card => card.GetProperty("CardId").GetString() == "S02-0301");
    }

    [Theory]
    [InlineData("S01-01D1", TiantingSetupId)]
    [InlineData("S01-02D1", SunSetupId)]
    [InlineData("S01-03D1", AsgardSetupId)]
    [InlineData("S01-04D1", TakamagaharaSetupId)]
    [InlineData("S02-05D1", OlympusSetupId)]
    [InlineData("S02-06D1", OtherworldSetupId)]
    [L12AbilityEvidence(TiantingSetupId, "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence(SunSetupId, "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence(AsgardSetupId, "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence(TakamagaharaSetupId, "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence(OlympusSetupId, "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence(OtherworldSetupId, "normal", "reconnect", "presentation-consumers")]
    public void EveryDivinitySetupMoraleIsAppliedOnceAndRestoredAsCurrentResourceState(
        string divinityId, string abilityId)
    {
        Assert.Contains(divinityId, abilityId, StringComparison.Ordinal);
        var baseDeck = Catalog.DeckAt(0);
        var olympusDeck = new L12PresetDeckDefinition
        {
            Name = "诸神巅开局生命周期测试",
            MasterId = divinityId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "setup-olympus-profile", "SETUPO", 93302,
            ["甲", "乙"], [olympusDeck, baseDeck], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);

        Assert.Equal(2, game.State.Players[0].Morale.Count);
        Assert.All(game.State.Players[0].Morale, morale => Assert.False(morale.Tapped));
        game = Restore(game);
        Assert.Equal(2, game.State.Players[0].Morale.Count);
        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(game.SnapshotFor(0)));
        Assert.Equal(2, snapshot.RootElement.GetProperty("Players")[0]
            .GetProperty("morale").GetArrayLength());
    }
}
