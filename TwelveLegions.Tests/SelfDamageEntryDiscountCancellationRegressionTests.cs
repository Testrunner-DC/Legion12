using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SelfDamageEntryDiscountCancellationRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> CardAndRestoreCases()
        => Catalog.Cards.Values
            .Where(card => L12StructuredCardRules.SelfDamageEntryDiscount(card.Id) is not null)
            .OrderBy(card => card.Id, StringComparer.Ordinal)
            .SelectMany(card => new[]
            {
                new object[] { card.Id, false },
                new object[] { card.Id, true },
            });

    [Theory]
    [MemberData(nameof(CardAndRestoreCases))]
    public void CostDecisionCanExplicitlyCancelBeforeEitherPayment(string cardId, bool restore)
    {
        var game = Create(88100 + cardId[^1]);
        var card = PutCardAndDiscountOnlyMorale(game, cardId, $"cancel-{cardId}");

        Accept(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)));
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("play-cost-choice", prompt.Continuation);
        Assert.Equal(["yes", "no", "cancel"], prompt.ValidChoices);
        Assert.Equal("true", prompt.Data["allowCancel"]);
        Assert.Equal("取消打出", prompt.Data["cancel"]);
        if (restore) game = Restore(game);
        var player = game.State.Players[0];

        Accept(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "cancel")));

        Assert.Equal(10, player.Hp);
        Assert.Contains(player.Hand, candidate => candidate.InstanceId == card.InstanceId);
        Assert.All(player.Morale, morale => Assert.False(morale.Tapped));
        Assert.All(player.Field.SelectMany(row => row), Assert.Null);
        AssertCleared(game);
        Assert.False(game.Handle(0,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "cancel")).Accepted);
    }

    [Theory]
    [MemberData(nameof(CardAndRestoreCases))]
    public void UnaffordableNormalCostReissuesTheDecisionInsteadOfConsumingIt(string cardId, bool restore)
    {
        var game = Create(88200 + cardId[^1]);
        var card = PutCardAndDiscountOnlyMorale(game, cardId, $"retry-{cardId}");

        Accept(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)));
        var first = Assert.Single(game.State.PendingPrompts);
        if (restore) game = Restore(game);
        var player = game.State.Players[0];

        Accept(game.Handle(0, new L12Command("resolvePrompt", PromptId: first.PromptId, Choice: "no")));

        var retry = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("play-cost-choice", retry.Continuation);
        Assert.NotEqual(first.PromptId, retry.PromptId);
        Assert.Contains("士气不足", retry.Data["retryReason"]);
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(retry.PromptId,
            Assert.Single(snapshot.GetProperty("prompts").EnumerateArray()).GetProperty("promptId").GetString());
        Assert.Equal(10, player.Hp);
        Assert.Contains(player.Hand, candidate => candidate.InstanceId == card.InstanceId);
        Assert.All(player.Morale, morale => Assert.False(morale.Tapped));
        Assert.All(player.Field.SelectMany(row => row), Assert.Null);

        Accept(game.Handle(0, new L12Command("resolvePrompt", PromptId: retry.PromptId, Choice: "cancel")));
        AssertCleared(game);
    }

    [Theory]
    [MemberData(nameof(CardAndRestoreCases))]
    public void AffordableNormalCostStillPlaysWithoutSelfDamage(string cardId, bool restore)
    {
        var game = Create(88300 + cardId[^1]);
        var player = game.State.Players[0];
        var card = Card(cardId, $"normal-{cardId}");
        player.Hand.Add(card);
        AddMorale(player, card.Cost, $"normal-{cardId}");

        Accept(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)));
        var prompt = Assert.Single(game.State.PendingPrompts);
        if (restore) game = Restore(game);
        player = game.State.Players[0];

        Accept(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "no")));

        Assert.Equal(10, player.Hp);
        Assert.Equal(card.InstanceId, player.Field[0][0]?.InstanceId);
        Assert.DoesNotContain(player.Hand, candidate => candidate.InstanceId == card.InstanceId);
        Assert.Equal(card.Cost, player.Morale.Count(morale => morale.Tapped));
        Assert.DoesNotContain(game.State.PendingPrompts,
            candidate => candidate.Continuation == "play-cost-choice");
    }

    [Theory]
    [InlineData("card-left-hand", false)]
    [InlineData("card-left-hand", true)]
    [InlineData("slot-occupied", false)]
    [InlineData("slot-occupied", true)]
    [InlineData("discount-became-unaffordable", false)]
    [InlineData("discount-became-unaffordable", true)]
    public void InvalidatedRetryClosesTheObsoletePromptWithoutRejectedMutation(
        string invalidation, bool restore)
    {
        var game = Create(88400 + invalidation.Length);
        var card = PutCardAndDiscountOnlyMorale(game, "S02-0303", $"invalid-{invalidation}");
        Accept(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)));
        var prompt = Assert.Single(game.State.PendingPrompts);
        if (restore) game = Restore(game);
        var player = game.State.Players[0];
        switch (invalidation)
        {
            case "card-left-hand":
                var source = Assert.Single(player.Hand, candidate => candidate.InstanceId == card.InstanceId);
                player.Hand.Remove(source);
                player.Graveyard.Add(source);
                break;
            case "slot-occupied":
                player.Field[0][0] = Card("S01-0001", $"occupied-{restore}");
                break;
            case "discount-became-unaffordable":
                player.Morale[0].Tapped = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidation));
        }

        Accept(game.Handle(0,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "no")));

        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal(10, player.Hp);
        Assert.DoesNotContain(player.Field.SelectMany(row => row),
            candidate => candidate?.InstanceId == card.InstanceId);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
    }

    [Fact]
    public void LegacyPendingDecisionRestoresByteForByteAndUnaffordableNoRefreshesToCancelablePrompt()
    {
        var game = Create(88500);
        var card = PutCardAndDiscountOnlyMorale(game, "S02-0303", "legacy-pending-canute");
        Accept(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)));
        var legacy = Assert.Single(game.State.PendingPrompts);
        Assert.True(legacy.ValidChoices.Remove("cancel"));
        legacy.Data.Remove("allowCancel");
        legacy.Data.Remove("cancel");
        legacy.ChoiceLabels.Remove("cancel");
        legacy.Presentation?.ChoiceConsequences.Remove("cancel");
        var legacyState = game.SerializeFullState();
        var random = game.RandomState;
        var facts = game.CardFactSignalSequence;

        game = L12GameEngine.RestoreCheckpoint(Catalog, legacyState, random!.Value, facts,
            autoPassEmptyResponses: true, concealHiddenResponseAvailability: false);

        Assert.Equal(legacyState, game.SerializeFullState());
        Assert.Equal(random, game.RandomState);
        Assert.Equal(facts, game.CardFactSignalSequence);
        var restoredLegacy = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(["yes", "no"], restoredLegacy.ValidChoices);
        Assert.DoesNotContain("allowCancel", restoredLegacy.Data.Keys);

        Accept(game.Handle(0,
            new L12Command("resolvePrompt", PromptId: restoredLegacy.PromptId, Choice: "no")));

        var refreshed = Assert.Single(game.State.PendingPrompts);
        Assert.NotEqual(restoredLegacy.PromptId, refreshed.PromptId);
        Assert.Equal(["yes", "no", "cancel"], refreshed.ValidChoices);
        Assert.Equal("true", refreshed.Data["allowCancel"]);
        Assert.Contains("士气不足", refreshed.Data["retryReason"]);
    }

    [Fact]
    public void InvalidMixedCancelWrongPlayerAndConsumedPromptIdRemainPureRejections()
    {
        var game = Create(88600);
        var card = PutCardAndDiscountOnlyMorale(game, "S02-0303", "rejection-guards-canute");
        Accept(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)));
        var first = Assert.Single(game.State.PendingPrompts);

        AssertPureRejection(game, 0, new L12Command("resolvePrompt", PromptId: first.PromptId,
            Choice: "cancel", CardInstanceIds: ["yes"]));
        AssertPureRejection(game, 1,
            new L12Command("resolvePrompt", PromptId: first.PromptId, Choice: "cancel"));

        Accept(game.Handle(0,
            new L12Command("resolvePrompt", PromptId: first.PromptId, Choice: "no")));
        var retry = Assert.Single(game.State.PendingPrompts);
        Assert.NotEqual(first.PromptId, retry.PromptId);
        AssertPureRejection(game, 0,
            new L12Command("resolvePrompt", PromptId: first.PromptId, Choice: "cancel"));
        Assert.Equal(retry.PromptId, Assert.Single(game.State.PendingPrompts).PromptId);
    }

    private static L12CardInstance PutCardAndDiscountOnlyMorale(L12GameEngine game, string cardId, string instanceId)
    {
        var player = game.State.Players[0];
        var card = Card(cardId, instanceId);
        player.Hand.Add(card);
        AddMorale(player, card.Cost - 1, instanceId);
        return card;
    }

    private static void AddMorale(L12PlayerState player, int count, string prefix)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard { InstanceId = $"{prefix}-morale-{index}", CardId = "S01-03C1" });
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "self-damage-cancel", "SELF-DAMAGE-CANCEL", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: true,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Phase = L12Phase.Main;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.ActiveDisaster = null;
        game.State.PendingPrompts.Clear();
        foreach (var owner in game.State.Players)
        {
            owner.Hand.Clear();
            owner.Library.Clear();
            owner.Graveyard.Clear();
            owner.Morale.Clear();
            foreach (var row in owner.Field) Array.Clear(row);
        }
        game.State.Players[0].Hp = 10;
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
    {
        var state = game.SerializeFullState();
        var random = game.RandomState;
        var facts = game.CardFactSignalSequence;
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, state, random!.Value, facts,
            autoPassEmptyResponses: true, concealHiddenResponseAvailability: false);
        Assert.Equal(state, restored.SerializeFullState());
        Assert.Equal(random, restored.RandomState);
        Assert.Equal(facts, restored.CardFactSignalSequence);
        return restored;
    }

    private static void AssertCleared(L12GameEngine game)
    {
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Null(game.State.PendingDefense);
        Assert.Null(game.State.ResponseWindow);
    }

    private static void AssertPureRejection(L12GameEngine game, int playerIndex, L12Command command)
    {
        var state = game.SerializeFullState();
        var random = game.RandomState;
        var facts = game.CardFactSignalSequence;
        var result = game.Handle(playerIndex, command);
        Assert.False(result.Accepted);
        Assert.Equal(state, game.SerializeFullState());
        Assert.Equal(random, game.RandomState);
        Assert.Equal(facts, game.CardFactSignalSequence);
    }

    private static void Accept(CommandResult result) => Assert.True(result.Accepted, result.Error);

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
            HasPrintedCost = definition.Cost.HasValue,
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
