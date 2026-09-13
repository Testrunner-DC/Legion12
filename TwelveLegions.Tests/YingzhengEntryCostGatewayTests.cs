using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class YingzhengEntryCostGatewayTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void GenericEntryGatewayWithoutCurrentEightCostOnlyRevealsHand()
    {
        var game = Create(91331);
        var player = game.State.Players[0];
        var enemy = Card("S01-0003", "yingzheng-gateway-enemy", owner: 1);
        var yingzheng = Card("S02-0101", "yingzheng-gateway-source");
        player.Field[0][0] = yingzheng;
        player.Hand.Add(Card("S01-0003", "yingzheng-gateway-not-eight"));
        game.State.Players[1].Field[0][0] = enemy;

        Invoke(game, "QueueOrPushTriggeredEffect", 0, yingzheng, "enter", "【登场时】效果", null, null);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", response.Kind);
        Assert.False(response.Data.ContainsKey("responsePaidCostSummary"));
        Assert.DoesNotContain("Cost（已支付）", response.Text, StringComparison.Ordinal);
        PassResponses(game);

        Assert.Same(enemy, game.State.Players[1].Field[0][0]);
        Assert.Contains(game.State.Events, entry => entry.Type == "reveal"
            && entry.Text.Contains("未满足发动条件", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events,
            entry => entry.Type == "effect"
                && entry.Text.Contains("击杀除此军团以外", StringComparison.Ordinal));
    }

    [Fact]
    public void DiscountedPrintedEightCostDoesNotPayACurrentCostEightRequirement()
    {
        var game = Create(91332);
        var player = game.State.Players[0];
        var yingzheng = Card("S02-0101", "yingzheng-current-cost-source");
        var discounted = Card("S02-0101", "yingzheng-current-cost-seven");
        var enemy = Card("S01-0003", "yingzheng-current-cost-enemy", owner: 1);
        discounted.CostModifier = -1;
        player.Hand.AddRange([yingzheng, discounted]);
        AddMorale(player, yingzheng.CurrentCost);
        game.State.Players[1].Field[0][0] = enemy;

        var play = game.Handle(0, new L12Command("playCard", yingzheng.InstanceId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation == "s2-yingzheng-enter-cost");
        PassResponses(game);

        Assert.Contains(discounted, player.Hand);
        Assert.Same(enemy, game.State.Players[1].Field[0][0]);
        Assert.Contains(game.State.Events, entry => entry.Type == "reveal"
            && entry.Cards.Any(card => card.InstanceId == discounted.InstanceId));
    }

    [Fact]
    public void InvalidatedEightCostFallsBackWithoutClosingTheMatchFlow()
    {
        var game = Create(91333, stateFormatVersion: 2);
        var player = game.State.Players[0];
        var yingzheng = Card("S02-0101", "yingzheng-invalidated-source");
        var cost = Card("S02-0101", "yingzheng-invalidated-cost");
        var costId = cost.InstanceId;
        var enemy = Card("S01-0003", "yingzheng-invalidated-enemy", owner: 1);
        player.Hand.AddRange([yingzheng, cost]);
        AddMorale(player, yingzheng.CurrentCost);
        game.State.Players[1].Field[0][0] = enemy;

        var play = game.Handle(0, new L12Command("playCard", yingzheng.InstanceId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-yingzheng-enter-cost", prompt.Continuation);
        game = Restore(game);
        player = game.State.Players[0];
        cost = Assert.Single(player.Hand, card => card.InstanceId == costId);
        cost.CostModifier = -1;

        var result = game.Handle(0, new L12Command("resolvePrompt",
            PromptId: Assert.Single(game.State.PendingPrompts).PromptId, Choice: cost.InstanceId));
        Assert.True(result.Accepted, result.Error);
        PassResponses(game);

        Assert.Contains(player.Hand, card => card.InstanceId == cost.InstanceId);
        Assert.Equal(enemy.InstanceId, game.State.Players[1].Field[0][0]?.InstanceId);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(game.State.Events, entry => entry.Type == "reveal"
            && entry.Text.Contains("未满足发动条件", StringComparison.Ordinal));
    }

    [Fact]
    public void GenericEntryGatewayWithCurrentEightCostRequiresPaymentBeforeKilling()
    {
        var game = Create(91334);
        var player = game.State.Players[0];
        var yingzheng = Card("S02-0101", "yingzheng-gateway-paid-source");
        var cost = Card("S02-0101", "yingzheng-gateway-paid-cost");
        var enemy = Card("S01-0003", "yingzheng-gateway-paid-enemy", owner: 1);
        player.Field[0][0] = yingzheng;
        player.Hand.Add(cost);
        game.State.Players[1].Field[0][0] = enemy;

        Invoke(game, "QueueOrPushTriggeredEffect", 0, yingzheng, "enter", "【登场时】效果", null, null);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-yingzheng-enter-cost", prompt.Continuation);
        Assert.Same(enemy, game.State.Players[1].Field[0][0]);

        var payment = game.Handle(0, new L12Command("resolvePrompt",
            PromptId: prompt.PromptId, Choice: cost.InstanceId));
        Assert.True(payment.Accepted, payment.Error);
        Assert.Contains(cost, player.Graveyard);
        PassResponses(game);

        Assert.Null(game.State.Players[1].Field[0][0]);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("击杀除此军团以外", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidatedSelectionKeepsPromptWhenAnotherCurrentEightCostRemains()
    {
        var game = Create(91335);
        var player = game.State.Players[0];
        var yingzheng = Card("S02-0101", "yingzheng-reselect-source");
        var stale = Card("S02-0101", "yingzheng-reselect-stale");
        var valid = Card("S02-0101", "yingzheng-reselect-valid");
        player.Field[0][0] = yingzheng;
        player.Hand.AddRange([stale, valid]);

        Invoke(game, "QueueOrPushTriggeredEffect", 0, yingzheng, "enter", "【登场时】效果", null, null);
        var prompt = Assert.Single(game.State.PendingPrompts);
        stale.CostModifier = -1;

        var rejected = game.Handle(0, new L12Command("resolvePrompt",
            PromptId: prompt.PromptId, Choice: stale.InstanceId));
        Assert.False(rejected.Accepted);
        Assert.Contains("重新选择", rejected.Error);
        var retryPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.NotEqual(prompt.PromptId, retryPrompt.PromptId);
        Assert.DoesNotContain(stale.InstanceId, retryPrompt.ValidChoices);
        Assert.Contains(valid.InstanceId, retryPrompt.ValidChoices);
        Assert.Contains(stale, player.Hand);
        Assert.Contains(valid, player.Hand);

        var duplicateOldSubmit = game.Handle(0, new L12Command("resolvePrompt",
            PromptId: prompt.PromptId, Choice: valid.InstanceId));
        Assert.False(duplicateOldSubmit.Accepted);
        Assert.Contains(valid, player.Hand);

        var accepted = game.Handle(0, new L12Command("resolvePrompt",
            PromptId: retryPrompt.PromptId, Choice: valid.InstanceId));
        Assert.True(accepted.Accepted, accepted.Error);
        Assert.Contains(stale, player.Hand);
        Assert.Contains(valid, player.Graveyard);
    }

    [Fact]
    public void SourceLeavingDuringEntryPaymentClosesThePromptWithoutDiscarding()
    {
        var game = Create(91336, stateFormatVersion: 2);
        var player = game.State.Players[0];
        var yingzheng = Card("S02-0101", "yingzheng-left-source");
        var cost = Card("S02-0101", "yingzheng-left-cost");
        player.Field[0][0] = yingzheng;
        player.Hand.Add(cost);

        Invoke(game, "QueueOrPushTriggeredEffect", 0, yingzheng, "enter", "【登场时】效果", null, null);
        var prompt = Assert.Single(game.State.PendingPrompts);
        player.Field[0][0] = null;
        player.Graveyard.Add(yingzheng);

        var result = game.Handle(0, new L12Command("resolvePrompt",
            PromptId: prompt.PromptId, Choice: cost.InstanceId));
        Assert.True(result.Accepted, result.Error);
        Assert.Contains(cost, player.Hand);
        Assert.DoesNotContain(cost, player.Graveyard);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    public void RepeatingTheSamePaidPromptCannotDiscardASecondCard()
    {
        var game = Create(91337);
        var player = game.State.Players[0];
        var yingzheng = Card("S02-0101", "yingzheng-repeat-source");
        var first = Card("S02-0101", "yingzheng-repeat-first");
        var second = Card("S02-0101", "yingzheng-repeat-second");
        player.Field[0][0] = yingzheng;
        player.Hand.AddRange([first, second]);

        Invoke(game, "QueueOrPushTriggeredEffect", 0, yingzheng, "enter", "【登场时】效果", null, null);
        var prompt = Assert.Single(game.State.PendingPrompts);
        var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: first.InstanceId);
        Assert.True(game.Handle(0, command).Accepted);

        var repeated = game.Handle(0, command);
        Assert.False(repeated.Accepted);
        Assert.Contains(first, player.Graveyard);
        Assert.Contains(second, player.Hand);
        Assert.Single(player.Graveyard, card => card.InstanceId == first.InstanceId);
    }

    [Fact]
    public void PaidCostAndCompleteEffectRemainVisibleAfterResponseReconnect()
    {
        var game = Create(91338, stateFormatVersion: 2);
        var player = game.State.Players[0];
        var yingzheng = Card("S02-0101", "yingzheng-response-restore-source");
        var cost = Card("S02-0101", "yingzheng-response-restore-cost");
        player.Field[0][0] = yingzheng;
        player.Hand.Add(cost);

        Invoke(game, "QueueOrPushTriggeredEffect", 0, yingzheng, "enter", "【登场时】效果", null, null);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt",
            PromptId: costPrompt.PromptId, Choice: cost.InstanceId)).Accepted);

        var restored = Restore(game);
        var response = Assert.Single(restored.State.PendingPrompts);
        Assert.Equal("response", response.Kind);
        Assert.Equal($"弃置手牌中的〈{cost.Name}〉（当前费用8）",
            response.Data["responsePaidCostSummary"]);
        Assert.Contains("Cost（已支付）", response.Text, StringComparison.Ordinal);
        Assert.Contains("击杀除此军团以外的所有军团", response.Text, StringComparison.Ordinal);
        Assert.Contains("返还所有士气并限制本回合追加士气", response.Text,
            StringComparison.Ordinal);
        Assert.Contains(restored.State.Players[0].Graveyard,
            card => card.InstanceId == cost.InstanceId);
    }

    private static L12GameEngine Create(int seed, int stateFormatVersion = 0)
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "嬴政登场费用网关回归",
            MasterId = basis.MasterId,
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "yingzheng-cost-gateway", "YINGZHENG", seed,
            ["甲", "乙"], [deck, basis], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: stateFormatVersion);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var state in game.State.Players)
        {
            state.Field[0] = new L12CardInstance?[3];
            state.Field[1] = new L12CardInstance?[3];
            state.Hand.Clear();
            state.Library.Clear();
            state.Graveyard.Clear();
            state.Morale.Clear();
            state.Resolving.Clear();
        }
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static void AddMorale(L12PlayerState player, int count)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-01C1",
                InstanceId = $"yingzheng-cost-morale-{index}",
            });
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 40
             && game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt; safety++)
        {
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }

    private static object? Invoke(object target, string name, params object?[] args)
        => target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method => method.Name == name && method.GetParameters().Length == args.Length)
            .Invoke(target, args);

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }
}
