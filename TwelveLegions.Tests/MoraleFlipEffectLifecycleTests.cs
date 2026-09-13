using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MoraleFlipEffectLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine CreateWithFirstMaster(string masterId, int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = $"{masterId}士气翻转生命周期回归",
            MasterId = masterId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        return new L12GameEngine(Catalog, "morale-flip-lifecycle", "MORALE-FLIP-LIFECYCLE", seed,
            ["甲", "乙"], [deck, baseDeck], skipPreparation: true);
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
        };
    }

    private static L12MoraleCard Morale(string instanceId, bool tapped = false) => new()
    {
        InstanceId = instanceId,
        CardId = "S02-05C1",
        Tapped = tapped,
    };

    private static void PrepareMain(L12GameEngine game)
    {
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
    }

    private static void HoldOpponentResponseWindow(L12GameEngine game)
    {
        var opponent = game.State.Players[1];
        var counter = Card("S01-0019", $"morale-flip-response-{game.State.StackSequence}");
        counter.Hidden = true;
        counter.SetRound = 0;
        opponent.Field[1][2] = counter;
        opponent.Field[0][2] ??= Card("S01-0004", $"morale-flip-target-{game.State.StackSequence}");
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    private static L12ActionEvent Result(L12GameEngine game, string sourceInstanceId)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == sourceInstanceId));

    private static void CommitOlympusFlip(L12GameEngine game, string paymentId)
    {
        Assert.True(game.Handle(0, new L12Command("activateAbility", "faction-0",
            Ability: "olympusMoraleFlip")).Accepted);
        var paymentPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("active-morale-choice", paymentPrompt.Continuation);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: paymentPrompt.PromptId,
            CardInstanceIds: [paymentId])).Accepted);
    }

    [Fact]
    [Trait("L12Evidence", "ability:olympusMoraleFlip")]
    public void OlympusMoraleFlipKeepsPaymentSeparateAndResolvesAfterCheckpoint()
    {
        var game = CreateWithFirstMaster("S02-05M1", 91331);
        var player = game.State.Players[0];
        PrepareMain(game);
        player.Morale.Clear();
        var payment = Morale("olympus-flip-payment");
        var target = Morale("olympus-flip-target");
        player.Morale.AddRange([payment, target]);
        HoldOpponentResponseWindow(game);

        CommitOlympusFlip(game, payment.InstanceId);
        Assert.True(payment.Tapped);
        Assert.False(target.IsGodPower);
        PassResponses(game);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-flip-morale", targetPrompt.Data["action"]);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            Choice: target.InstanceId)).Accepted);

        Assert.True(game.State.Players[0].Morale.Single(card => card.InstanceId == payment.InstanceId).Tapped);
        Assert.True(game.State.Players[0].Morale.Single(card => card.InstanceId == target.InstanceId).IsGodPower);
        var result = Result(game, "faction-0");
        Assert.Equal("resolved", result.EffectResultStatus);
        Assert.Equal(1, result.EffectSegmentIndex);
        Assert.Equal(1, result.EffectSegmentCount);
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            Choice: target.InstanceId)).Accepted);
    }

    [Fact]
    [Trait("L12Evidence", "ability:olympusMoraleFlip")]
    public void NegatedOlympusMoraleFlipKeepsPaidMoraleAndCreatesNoTargetPrompt()
    {
        var game = CreateWithFirstMaster("S02-05M1", 91332);
        var player = game.State.Players[0];
        PrepareMain(game);
        player.Morale.Clear();
        var payment = Morale("olympus-negated-payment");
        var target = Morale("olympus-negated-target");
        player.Morale.AddRange([payment, target]);
        HoldOpponentResponseWindow(game);

        CommitOlympusFlip(game, payment.InstanceId);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(payment.Tapped);
        Assert.False(target.IsGodPower);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Contains(player.UsedAbilities, key => key.Contains("olympusMoraleFlip", StringComparison.Ordinal));
        Assert.Equal("negated", Result(game, "faction-0").EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:olympusMoraleFlip")]
    public void OlympusMoraleFlipFailsWhenEverySettlementCandidateChangedFaceDuringResponses()
    {
        var game = CreateWithFirstMaster("S02-05M1", 91333);
        var player = game.State.Players[0];
        PrepareMain(game);
        player.Morale.Clear();
        var payment = Morale("olympus-no-target-payment");
        var target = Morale("olympus-no-target-target");
        player.Morale.AddRange([payment, target]);
        HoldOpponentResponseWindow(game);

        CommitOlympusFlip(game, payment.InstanceId);
        payment.IsGodPower = true;
        target.IsGodPower = true;
        PassResponses(game);

        Assert.True(payment.Tapped);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal("failed", Result(game, "faction-0").EffectResultStatus);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("响应逆结算", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:olympusMoraleFlip")]
    public void OlympusMoraleFlipRevalidatesTheFrozenSelectionBeforeChangingItsFace()
    {
        var game = CreateWithFirstMaster("S02-05M1", 91334);
        var player = game.State.Players[0];
        PrepareMain(game);
        player.Morale.Clear();
        var payment = Morale("olympus-stale-payment");
        var target = Morale("olympus-stale-target");
        player.Morale.AddRange([payment, target]);
        HoldOpponentResponseWindow(game);

        CommitOlympusFlip(game, payment.InstanceId);
        PassResponses(game);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        target.IsGodPower = true;

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            Choice: target.InstanceId)).Accepted);

        Assert.True(payment.Tapped);
        Assert.True(target.IsGodPower);
        Assert.Equal("failed", Result(game, "faction-0").EffectResultStatus);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("所选士气", StringComparison.Ordinal));
    }

    [Fact]
    public void OptionalEnterMoraleFlipPublishesDeclinedWhenThePlayerChoosesSkip()
    {
        var game = CreateWithFirstMaster("S02-05M1", 91335);
        var player = game.State.Players[0];
        PrepareMain(game);
        player.Hand.Clear();
        player.Morale.Clear();
        var aristotle = Card("S02-0513", "optional-flip-aristotle");
        player.Hand.Add(aristotle);
        player.Morale.AddRange([
            Morale("optional-flip-cost-1"), Morale("optional-flip-cost-2"),
            Morale("optional-flip-cost-3"), Morale("optional-flip-target")]);

        Assert.True(game.Handle(0, new L12Command("playCard", aristotle.InstanceId,
            Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("skip", prompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "skip")).Accepted);

        Assert.DoesNotContain(player.Morale, morale => morale.IsGodPower);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-declined"
            && entry.EffectResultStatus == "declined"
            && entry.Text.Contains("选择不发动", StringComparison.Ordinal));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }
}
