using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PromotionEntryLifecycleProfileTests
{
    private const string HeraclesId = "S02-0501:ability:promotion:3eb467465ef47272";
    private const string AchillesId = "S02-0503:ability:promotion:3eb467465ef47272";
    private const string PerseusId = "S02-0505:ability:promotion:e890e8664470e824";
    private const string AtalantaId = "S02-0507:ability:promotion:e890e8664470e824";

    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "promotion-profile", "PROMOTION", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = 0,
            SummonRound = -1,
        };
    }

    private static void AddGodPower(L12PlayerState player, int count, string suffix)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"god-power-{suffix}-{index}",
                CardId = "S02-05C1",
                IsGodPower = true,
                Tapped = false,
            });
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

    [Theory]
    [InlineData("S02-0501", "S02-0502", HeraclesId, 2)]
    [InlineData("S02-0503", "S02-0504", AchillesId, 2)]
    [InlineData("S02-0505", "S02-0506", PerseusId, 1)]
    [InlineData("S02-0507", "S02-0508", AtalantaId, 1)]
    [L12AbilityEvidence(HeraclesId, "normal", "reconnect", "duplicate-submit", "single-candidate-choice", "presentation-consumers")]
    [L12AbilityEvidence(AchillesId, "normal", "reconnect", "duplicate-submit", "single-candidate-choice", "presentation-consumers")]
    [L12AbilityEvidence(PerseusId, "normal", "reconnect", "duplicate-submit", "single-candidate-choice", "presentation-consumers")]
    [L12AbilityEvidence(AtalantaId, "normal", "reconnect", "duplicate-submit", "single-candidate-choice", "presentation-consumers")]
    public void EveryPromotionRestoresTheFoundationChoiceAndCommitsExactlyOnce(
        string promotedCardId, string foundationCardId, string abilityId, int cost)
    {
        Assert.Contains(promotedCardId, abilityId, StringComparison.Ordinal);
        var game = Create(93400 + promotedCardId[^1]);
        var player = game.State.Players[0];
        var foundation = Card(foundationCardId, $"foundation-{promotedCardId}");
        var promoted = Card(promotedCardId, $"promoted-{promotedCardId}");
        player.Field[0][1] = foundation;
        player.Hand.Add(promoted);
        AddGodPower(player, cost, promotedCardId);

        var begun = game.Handle(0, new L12Command("playCard", promoted.InstanceId));
        Assert.True(begun.Accepted, begun.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-promotion-foundation", prompt.Continuation);
        Assert.Equal([foundation.InstanceId, "cancel"], prompt.ValidChoices);
        Assert.Single(game.SnapshotFor(0).Prompts);

        game = Restore(game);
        var restoredPrompt = Assert.Single(game.State.PendingPrompts);
        var resolved = game.Handle(0, new L12Command("resolvePrompt", PromptId: restoredPrompt.PromptId,
            Choice: foundation.InstanceId));
        Assert.True(resolved.Accepted, resolved.Error);

        player = game.State.Players[0];
        var entered = Assert.IsType<L12CardInstance>(player.Field[0][1]);
        Assert.Equal(promotedCardId, entered.CardId);
        Assert.Contains(entered.AttachedCards, card => card.InstanceId == foundation.InstanceId);
        Assert.DoesNotContain(player.Hand, card => card.InstanceId == promoted.InstanceId);
        Assert.Equal(cost, player.Morale.Count(morale => morale.Tapped && !morale.IsGodPower));
        Assert.Contains(game.State.Events, entry => entry.Type == "promotion"
            && entry.Cards.Any(card => card.InstanceId == promoted.InstanceId));
        using var projection = JsonDocument.Parse(JsonSerializer.Serialize(game.SnapshotFor(0)));
        Assert.Contains(projection.RootElement.GetProperty("Players")[0].GetProperty("field")[0]
            .EnumerateArray(), slot => slot.ValueKind != JsonValueKind.Null
                && slot.GetProperty("CardId").GetString() == promotedCardId);

        var duplicate = game.Handle(0, new L12Command("resolvePrompt", PromptId: restoredPrompt.PromptId,
            Choice: foundation.InstanceId));
        Assert.False(duplicate.Accepted);
    }

    [Theory]
    [InlineData("S02-0501", "S02-0502", HeraclesId, 2)]
    [InlineData("S02-0503", "S02-0504", AchillesId, 2)]
    [InlineData("S02-0505", "S02-0506", PerseusId, 1)]
    [InlineData("S02-0507", "S02-0508", AtalantaId, 1)]
    [L12AbilityEvidence(HeraclesId, "no-target")]
    [L12AbilityEvidence(AchillesId, "no-target")]
    [L12AbilityEvidence(PerseusId, "no-target")]
    [L12AbilityEvidence(AtalantaId, "no-target")]
    public void PromotionWithoutAFoundationCannotStartOrSpendGodPower(
        string promotedCardId, string foundationCardId, string abilityId, int cost)
    {
        Assert.NotEqual(promotedCardId, foundationCardId);
        Assert.Contains(promotedCardId, abilityId, StringComparison.Ordinal);
        var game = Create(93500 + promotedCardId[^1]);
        var player = game.State.Players[0];
        var promoted = Card(promotedCardId, $"no-foundation-{promotedCardId}");
        player.Hand.Add(promoted);
        AddGodPower(player, cost, $"no-foundation-{promotedCardId}");

        var result = game.Handle(0, new L12Command("playCard", promoted.InstanceId));
        Assert.False(result.Accepted);
        Assert.Contains(promoted, player.Hand);
        Assert.Empty(game.State.PendingPrompts);
        Assert.All(player.Morale, morale =>
        {
            Assert.True(morale.IsGodPower);
            Assert.False(morale.Tapped);
        });
    }

    [Theory]
    [InlineData("S02-0501", "S02-0502", HeraclesId, 2)]
    [InlineData("S02-0503", "S02-0504", AchillesId, 2)]
    [InlineData("S02-0505", "S02-0506", PerseusId, 1)]
    [InlineData("S02-0507", "S02-0508", AtalantaId, 1)]
    [L12AbilityEvidence(HeraclesId, "target-invalidated")]
    [L12AbilityEvidence(AchillesId, "target-invalidated")]
    [L12AbilityEvidence(PerseusId, "target-invalidated")]
    [L12AbilityEvidence(AtalantaId, "target-invalidated")]
    public void FoundationThatLeavesBeforeSubmissionInvalidatesWithoutReplacementOrPayment(
        string promotedCardId, string foundationCardId, string abilityId, int cost)
    {
        Assert.Contains(promotedCardId, abilityId, StringComparison.Ordinal);
        var game = Create(93600 + promotedCardId[^1]);
        var player = game.State.Players[0];
        var foundation = Card(foundationCardId, $"stale-foundation-{promotedCardId}");
        var promoted = Card(promotedCardId, $"stale-promoted-{promotedCardId}");
        player.Field[0][0] = foundation;
        player.Hand.Add(promoted);
        AddGodPower(player, cost, $"stale-{promotedCardId}");
        Assert.True(game.Handle(0, new L12Command("playCard", promoted.InstanceId)).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);

        player.Field[0][0] = null;
        player.Graveyard.Add(foundation);
        var result = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: foundation.InstanceId));
        Assert.False(result.Accepted);
        Assert.Contains(promoted, player.Hand);
        Assert.Empty(game.State.PendingPrompts);
        Assert.All(player.Morale, morale =>
        {
            Assert.True(morale.IsGodPower);
            Assert.False(morale.Tapped);
        });
        Assert.False(game.Handle(0, new L12Command("playCard", promoted.InstanceId)).Accepted);
    }

    [Theory]
    [InlineData("S02-0501", "S02-0502", HeraclesId, 2)]
    [InlineData("S02-0503", "S02-0504", AchillesId, 2)]
    [InlineData("S02-0505", "S02-0506", PerseusId, 1)]
    [InlineData("S02-0507", "S02-0508", AtalantaId, 1)]
    [L12AbilityEvidence(HeraclesId, "payment-cancel")]
    [L12AbilityEvidence(AchillesId, "payment-cancel")]
    [L12AbilityEvidence(PerseusId, "payment-cancel")]
    [L12AbilityEvidence(AtalantaId, "payment-cancel")]
    public void PromotionFoundationPromptCanBeCancelledWithoutPayingOrChangingZones(
        string promotedCardId, string foundationCardId, string abilityId, int cost)
    {
        Assert.Contains(promotedCardId, abilityId, StringComparison.Ordinal);
        var game = Create(93700 + promotedCardId[^1]);
        var player = game.State.Players[0];
        var foundation = Card(foundationCardId, $"cancel-foundation-{promotedCardId}");
        var promoted = Card(promotedCardId, $"cancel-promoted-{promotedCardId}");
        player.Field[0][0] = foundation;
        player.Hand.Add(promoted);
        AddGodPower(player, cost, $"cancel-{promotedCardId}");
        Assert.True(game.Handle(0, new L12Command("playCard", promoted.InstanceId)).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);

        var cancelled = game.Handle(0,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "cancel"));
        Assert.True(cancelled.Accepted, cancelled.Error);
        Assert.Same(foundation, player.Field[0][0]);
        Assert.Contains(promoted, player.Hand);
        Assert.Empty(game.State.PendingPrompts);
        Assert.All(player.Morale, morale =>
        {
            Assert.True(morale.IsGodPower);
            Assert.False(morale.Tapped);
        });
    }
}
