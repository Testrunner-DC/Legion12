using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class GramActiveLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "gram-active", "GRAM-ACTIVE", seed,
            ["甲", "乙"], [3, 3], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Relic = null;
            foreach (var row in player.Field) Array.Clear(row);
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
        };
    }

    private static void AddOrdinaryCosts(L12PlayerState player, int count)
    {
        for (var index = 0; index < count; index++)
            player.Graveyard.Add(Card("S01-0301", $"gram-cost-{index}"));
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static L12ActionEvent Result(L12GameEngine game, string sourceId)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == sourceId));

    [Fact]
    public void PrintedActiveRestAbilityUsesOneStructuredNonLethalDamageScene()
    {
        var ability = Catalog.AtomicEffects.Find("S01-0317")!.Abilities
            .Single(candidate => candidate.Sequence == 2);

        var scene = Assert.Single(ability.Presentations, candidate =>
            candidate.Flow == "gram-nonlethal-damage");
        Assert.Equal((1, 1), (scene.SegmentIndex, scene.SegmentCount));
    }

    [Fact]
    [Trait("L12Evidence", "ability:gramDamage")]
    public void InsufficientGraveCostRejectsBeforeCreatingASelectionPrompt()
    {
        var game = Create(91401);
        var player = game.State.Players[0];
        var source = Card("S01-0317", "gram-insufficient-source");
        player.Relic = source;
        AddOrdinaryCosts(player, 3);

        var result = game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "gramDamage"));

        Assert.False(result.Accepted);
        Assert.Contains("墓地", result.Error);
        Assert.Empty(game.State.PendingPrompts);
        Assert.False(source.Tapped);
        Assert.Equal(3, player.Graveyard.Count);
        Assert.Empty(player.Library);
    }

    [Fact]
    [Trait("L12Evidence", "ability:gramDamage")]
    public void AConvertedDerivedSpecialLegionCannotCountAsAPayableGraveBottomCost()
    {
        var game = Create(91406);
        var player = game.State.Players[0];
        var source = Card("S01-0317", "gram-special-source");
        player.Relic = source;
        player.ExtraRelics.Add(Card("S02-0008", "gram-special-ring"));
        AddOrdinaryCosts(player, 3);
        player.Graveyard.Add(Card("S02-01S1", "gram-special-xiaotian"));

        var result = game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "gramDamage"));

        Assert.False(result.Accepted);
        Assert.Contains("合法返回牌库底部", result.Error);
        Assert.Empty(game.State.PendingPrompts);
        Assert.False(source.Tapped);
        Assert.Equal(4, player.Graveyard.Count);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0306")]
    public void SameTypeOlafCostDoesNotOfferAnUnpayableDerivedSpecialGraveCard()
    {
        var game = Create(91407);
        var player = game.State.Players[0];
        var olaf = Card("S01-0306", "olaf-special-cost-source");
        var special = Card("S02-01S1", "olaf-special-cost-xiaotian");
        olaf.SummonRound = 0;
        player.Field[0][0] = olaf;
        player.Graveyard.Add(special);

        var result = game.Handle(0, new L12Command("attack", olaf.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.True(result.Accepted, result.Error);
        Assert.Empty(game.State.PendingActivations);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        Assert.Contains(special, player.Graveyard);
        Assert.Empty(player.Library);
    }

    [Fact]
    [Trait("L12Evidence", "ability:gramDamage")]
    public void CostSelectionCanCancelWithoutRestingTheSourceOrMovingCards()
    {
        var game = Create(91402);
        var player = game.State.Players[0];
        var source = Card("S01-0317", "gram-cancel-source");
        player.Relic = source;
        AddOrdinaryCosts(player, 4);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "gramDamage")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("skip", prompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "skip")).Accepted);

        Assert.False(source.Tapped);
        Assert.Equal(4, player.Graveyard.Count);
        Assert.Empty(player.Library);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "ability:gramDamage")]
    public void PaidCostUsesDeclaredBottomOrderAndPublishesTheActualDamageSegment()
    {
        var game = Create(91403);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var source = Card("S01-0317", "gram-resolved-source");
        player.Relic = source;
        AddOrdinaryCosts(player, 4);
        var order = player.Graveyard.AsEnumerable().Reverse().Select(card => card.InstanceId).ToArray();
        var hp = enemy.Hp;

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "gramDamage")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: order.ToList())).Accepted);
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Empty(player.Graveyard);
        Assert.Equal(order, player.Library.TakeLast(4).Select(card => card.InstanceId));
        Assert.Equal(hp - 1, enemy.Hp);
        var result = Result(game, source.InstanceId);
        Assert.Equal("resolved", result.EffectResultStatus);
        Assert.Equal((1, 1), (result.EffectSegmentIndex, result.EffectSegmentCount));
    }

    [Fact]
    [Trait("L12Evidence", "ability:gramDamage")]
    public void NegationKeepsTheRestAndGraveBottomCostsButPreventsDamage()
    {
        var game = Create(91404);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var source = Card("S01-0317", "gram-negated-source");
        player.Relic = source;
        AddOrdinaryCosts(player, 4);
        var costs = player.Graveyard.Select(card => card.InstanceId).ToArray();
        var hp = enemy.Hp;

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "gramDamage")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: costs.ToList())).Accepted);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Empty(player.Graveyard);
        Assert.Equal(costs, player.Library.TakeLast(4).Select(card => card.InstanceId));
        Assert.Equal(hp, enemy.Hp);
        Assert.Equal("negated", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:gramDamage")]
    public void GraveWarriorRepresentationAndV2RestoreCannotPayTheSamePromptTwice()
    {
        var game = Create(91405);
        var player = game.State.Players[0];
        var source = Card("S01-0317", "gram-restore-source");
        var warrior = Card("ST03-08", "gram-restore-warrior");
        var ordinary = Card("S01-0301", "gram-restore-ordinary");
        player.Relic = source;
        player.Graveyard.AddRange([warrior, ordinary]);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "gramDamage")).Accepted);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: costPrompt.PromptId,
            CardInstanceIds: [warrior.InstanceId, ordinary.InstanceId])).Accepted);
        var countPrompt = Assert.Single(game.State.PendingPrompts);
        var representation = Assert.Single(countPrompt.ValidChoices, choice =>
            choice.StartsWith("grave-copies:", StringComparison.OrdinalIgnoreCase));
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: countPrompt.PromptId,
            Choice: representation)).Accepted);

        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        PassResponses(game);

        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: costPrompt.PromptId,
            CardInstanceIds: [warrior.InstanceId, ordinary.InstanceId])).Accepted);
        var restored = game.State.Players[0];
        Assert.True(restored.Relic!.Tapped);
        Assert.Empty(restored.Graveyard);
        Assert.Equal([warrior.InstanceId, ordinary.InstanceId],
            restored.Library.TakeLast(2).Select(card => card.InstanceId));
        Assert.Equal("resolved", Result(game, source.InstanceId).EffectResultStatus);
    }
}
