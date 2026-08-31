using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch4RegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, string? firstMaster = null)
    {
        var first = Catalog.DeckAt(0);
        if (firstMaster is not null)
            first = new L12PresetDeckDefinition
            {
                Name = $"{firstMaster}第四批原子审查牌库",
                MasterId = firstMaster,
                CardIds = [.. first.CardIds],
                MoraleIds = [.. first.MoraleIds],
                SpecialIds = [],
            };
        var game = new L12GameEngine(Catalog, "atomic-review-batch4", "ATOMIC4", seed,
            ["甲", "乙"], [first, Catalog.DeckAt(0)], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 50 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        return prompt;
    }

    [Fact]
    [Trait("L12Evidence", "trigger:tsukuyomiFollowMove")]
    public void TsukuyomiDeclaresCostTargetAndSlotBeforeStackAndDoesNotRefundOnTargetLoss()
    {
        var game = Create(7001, "S02-04M1");
        var player = game.State.Players[0];
        var moved = Card("S02-0401", "atomic4-tsukuyomi-moved");
        var target = Card("S02-0402", "atomic4-tsukuyomi-target");
        player.Field[1][0] = moved;
        player.Field[0][2] = target;
        var movementMorale = new L12MoraleCard
        {
            CardId = "S02-04C1", InstanceId = "atomic4-tsukuyomi-move-cost", Tapped = false,
        };
        var morale = new L12MoraleCard
        {
            CardId = "S02-04C1", InstanceId = "atomic4-tsukuyomi-cost", Tapped = false,
        };
        player.Morale.AddRange([movementMorale, morale]);

        Assert.True(game.Handle(0, new L12Command("move", moved.InstanceId, Row: 0, Slot: 0)).Accepted);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", mode.Continuation);
        Assert.Empty(game.State.EffectStack);
        Resolve(game, "mode:use");
        var cost = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(morale.InstanceId, cost.ValidChoices);
        Assert.False(morale.Tapped);
        Resolve(game, morale.InstanceId);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(target.InstanceId, targetPrompt.ValidChoices);
        Resolve(game, target.InstanceId);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("0:1", slot.ValidChoices);
        Assert.False(morale.Tapped);
        Resolve(game, "0:1");

        Assert.True(morale.Tapped);
        Assert.Contains("active:master-0:tsukuyomiFollowMove", player.UsedAbilities);
        Assert.Single(game.State.EffectStack);
        player.Field[0][2] = null;
        player.Graveyard.Add(target);
        PassResponses(game);

        Assert.True(morale.Tapped);
        Assert.Contains(target, player.Graveyard);
        Assert.Null(player.Field[0][1]);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("不回滚", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0106")]
    public void CosmosYinKeepsTopCardHiddenUntilResolutionThenDeclaresPublicTarget()
    {
        var game = Create(7002);
        var owner = game.State.Players[0];
        var actor = game.State.Players[1];
        var counter = Card("S02-0106", "atomic4-cosmos-counter");
        counter.Hidden = true;
        counter.SetRound = 2;
        owner.Field[1][0] = counter;
        var target = Card("S02-0402", "atomic4-cosmos-target");
        owner.Field[0][0] = target;
        owner.Library.Clear();
        var hiddenTop = Card("S01-0109", "atomic4-cosmos-hidden-top");
        owner.Library.Add(hiddenTop);
        var baseTactic = Card("S01-0219", "atomic4-cosmos-base");
        actor.Hand.Add(baseTactic);
        for (var index = 0; index < baseTactic.CurrentCost; index++)
            actor.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-02C1", InstanceId = $"atomic4-cosmos-base-cost-{index}", Tapped = false,
            });
        game.State.ActivePlayer = 1;

        Assert.True(game.Handle(1, new L12Command("playCard", baseTactic.InstanceId)).Accepted);
        var firstPriority = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, firstPriority.PlayerIndex);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: firstPriority.PromptId,
            Choice: "pass")).Accepted);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, response.PlayerIndex);
        Assert.Equal("response", response.Kind);
        Assert.Contains(counter.InstanceId, response.ValidChoices);
        Assert.DoesNotContain(hiddenTop.InstanceId, response.ValidChoices);
        Assert.DoesNotContain(hiddenTop.InstanceId, response.Data.Values);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: response.PromptId,
            Choice: counter.InstanceId)).Accepted);

        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.ValidChoices.Contains(hiddenTop.InstanceId)
                || prompt.Data.Values.Contains(hiddenTop.InstanceId));
        PassResponses(game);

        Assert.True(game.State.PendingPrompts.Count == 1,
            string.Join(" | ", game.State.Events.TakeLast(12).Select(entry => $"{entry.Type}:{entry.Text}")));
        var delayed = game.State.PendingPrompts[0];
        Assert.Equal("post-hidden-reveal", delayed.Data["declarationTiming"]);
        Assert.Contains(target.InstanceId, delayed.ValidChoices);
        Assert.Contains(hiddenTop, owner.Graveyard);
        owner.Field[0][0] = null;
        owner.Graveyard.Add(target);
        Resolve(game, target.InstanceId);

        Assert.Contains(hiddenTop, owner.Graveyard);
        Assert.DoesNotContain(hiddenTop, owner.Library);
        Assert.Equal(hiddenTop.BaseTroops, hiddenTop.Troops);
    }
}
