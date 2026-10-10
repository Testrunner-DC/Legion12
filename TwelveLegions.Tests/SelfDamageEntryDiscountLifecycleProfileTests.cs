using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SelfDamageEntryDiscountLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence("S01-0303:ability:hand-play:5e06807975eda2b7", "optional-choice", "last-health-terminal", "reconnect-payment")]
    [L12AbilityEvidence("S01-0304:ability:hand-play:5e06807975eda2b7", "optional-choice", "last-health-terminal", "reconnect-payment")]
    [L12AbilityEvidence("S01-0308:ability:hand-play:5e06807975eda2b7", "optional-choice", "last-health-terminal", "reconnect-payment")]
    [L12AbilityEvidence("S01-0310:ability:hand-play:5e06807975eda2b7", "optional-choice", "last-health-terminal", "reconnect-payment")]
    [L12AbilityEvidence("S01-0314:ability:hand-play:5e06807975eda2b7", "optional-choice", "last-health-terminal", "reconnect-payment")]
    [L12AbilityEvidence("S02-0303:ability:hand-play:5e06807975eda2b7", "optional-choice", "last-health-terminal", "reconnect-payment")]
    public void EveryPrintedSelfDamageDiscountUsesOneHandPlayCostProtocol()
    {
        var abilities = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "hand-play"
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.DamageMaster && atom.Stage == "cost"
                    && atom.Parameters.GetValueOrDefault("semantic") == "self-damage-entry-discount-cost"))
            .ToArray();
        Assert.Equal(6, abilities.Length);
        Assert.Equal(EffectLifecycleProfiles.SelfDamageEntryDiscountAbilityIds.Order(),
            abilities.Select(ability => ability.AbilityId).Order());
        Assert.All(abilities, ability =>
        {
            Assert.Equal("可对我方主宰造成1点伤害", ability.CostText);
            Assert.Equal("此军团登场费用-1。", ability.ResolutionText);
            var rule = Assert.IsType<L12SelfDamageEntryDiscountRule>(
                L12StructuredCardRules.SelfDamageEntryDiscount(ability.CardId));
            Assert.Equal(1, rule.DamageAmount);
            Assert.Equal(-1, rule.CostAdjustment);
        });
    }

    [Theory]
    [InlineData("S01-0303")]
    [InlineData("S01-0304")]
    [InlineData("S01-0308")]
    [InlineData("S01-0310")]
    [InlineData("S01-0314")]
    [InlineData("S02-0303")]
    [L12AbilityEvidence("S01-0303:ability:hand-play:5e06807975eda2b7", "normal", "duplicate-submit", "presentation-consumers")]
    [L12AbilityEvidence("S01-0304:ability:hand-play:5e06807975eda2b7", "normal", "duplicate-submit", "presentation-consumers")]
    [L12AbilityEvidence("S01-0308:ability:hand-play:5e06807975eda2b7", "normal", "duplicate-submit", "presentation-consumers")]
    [L12AbilityEvidence("S01-0310:ability:hand-play:5e06807975eda2b7", "normal", "duplicate-submit", "presentation-consumers")]
    [L12AbilityEvidence("S01-0314:ability:hand-play:5e06807975eda2b7", "normal", "duplicate-submit", "presentation-consumers")]
    [L12AbilityEvidence("S02-0303:ability:hand-play:5e06807975eda2b7", "normal", "duplicate-submit", "presentation-consumers")]
    public void AcceptedSelfDamageChoiceUsesDisplayedDiscountAndCannotBeSubmittedTwice(string cardId)
    {
        var game = Create(74600 + cardId[^1]);
        var player = game.State.Players[0];
        var card = Card(cardId, $"normal-{cardId}");
        player.Hand.Add(card);
        for (var index = 0; index < card.Cost - 1; index++)
            player.Morale.Add(Morale($"normal-payment-{index}"));

        Assert.Equal(card.Cost, SnapshotPlayCost(game, card.InstanceId));
        Assert.True(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)).Accepted);
        var choice = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("play-cost-choice", choice.Continuation);
        Assert.Equal(card.Cost.ToString(), choice.Data["normalCost"]);
        Assert.Equal((card.Cost - 1).ToString(), choice.Data["discountedCost"]);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: choice.PromptId, Choice: "yes")).Accepted);
        Assert.Equal(9, player.Hp);
        Assert.Equal(card.InstanceId, player.Field[0][0]?.InstanceId);
        Assert.DoesNotContain(player.Hand, candidate => candidate.InstanceId == card.InstanceId);
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: choice.PromptId, Choice: "yes")).Accepted);
    }

    [Theory]
    [InlineData("S01-0303")]
    [InlineData("S01-0304")]
    [InlineData("S01-0308")]
    [InlineData("S01-0310")]
    [InlineData("S01-0314")]
    [InlineData("S02-0303")]
    [L12AbilityEvidence("S01-0303:ability:hand-play:5e06807975eda2b7", "payment-cancel", "reconnect-payment")]
    [L12AbilityEvidence("S01-0304:ability:hand-play:5e06807975eda2b7", "payment-cancel", "reconnect-payment")]
    [L12AbilityEvidence("S01-0308:ability:hand-play:5e06807975eda2b7", "payment-cancel", "reconnect-payment")]
    [L12AbilityEvidence("S01-0310:ability:hand-play:5e06807975eda2b7", "payment-cancel", "reconnect-payment")]
    [L12AbilityEvidence("S01-0314:ability:hand-play:5e06807975eda2b7", "payment-cancel", "reconnect-payment")]
    [L12AbilityEvidence("S02-0303:ability:hand-play:5e06807975eda2b7", "payment-cancel", "reconnect-payment")]
    public void RecoveredSelfDamagePaymentCanCancelWithoutPayingEitherCost(string cardId)
    {
        var game = Create(74700 + cardId[^1]);
        var player = game.State.Players[0];
        var card = Card(cardId, $"cancel-{cardId}");
        player.Hand.Add(card);
        player.Field[1][2] = Card("S01-0212", $"cancel-guard-{cardId}");
        for (var index = 0; index < card.Cost - 1; index++)
            player.Morale.Add(Morale($"cancel-payment-{index}"));

        Assert.True(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)).Accepted);
        var choiceId = Assert.Single(game.State.PendingPrompts).PromptId;
        game = Restore(game);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: choiceId, Choice: "yes")).Accepted);
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("play-morale-choice", payment.Continuation);
        Assert.Contains("cancel", payment.ValidChoices);
        game = Restore(game);
        player = game.State.Players[0];

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId, Choice: "cancel")).Accepted);
        Assert.Equal(10, player.Hp);
        Assert.Contains(player.Hand, candidate => candidate.InstanceId == card.InstanceId);
        Assert.All(player.Morale, morale => Assert.False(morale.Tapped));
        Assert.False(player.Field[1][2]!.Tapped);
        Assert.Empty(game.State.PendingPrompts);
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId, Choice: "cancel")).Accepted);
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "self-damage-entry-discount", "SELF-DAMAGE", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: true,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.ActiveDisaster = null;
        foreach (var owner in game.State.Players)
        {
            owner.Hand.Clear();
            owner.Morale.Clear();
            foreach (var row in owner.Field) Array.Clear(row);
        }
        game.State.Players[0].Hp = 10;
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: true, concealHiddenResponseAvailability: false);

    private static int SnapshotPlayCost(L12GameEngine game, string instanceId)
    {
        var player = game.SnapshotFor(0).Players[0];
        var hand = Assert.IsType<L12CardInstance[]>(player.GetType().GetProperty("hand")!.GetValue(player));
        return Assert.Single(hand, card => card.InstanceId == instanceId).PlayCost!.Value;
    }

    private static L12MoraleCard Morale(string id)
        => new() { InstanceId = id, CardId = "S01-03C1" };

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
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }
}
