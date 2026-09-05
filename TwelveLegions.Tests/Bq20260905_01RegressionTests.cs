using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class Bq20260905_01RegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "bq-20260905-01", "BQ0905", seed, ["甲", "乙"], [0, 1],
            skipPreparation: true);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
        }
        return game;
    }

    private static L12GameEngine CreateWithFirstMaster(string masterId, int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var firstDeck = new L12PresetDeckDefinition
        {
            Name = $"{masterId}测试牌库",
            MasterId = masterId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "bq-20260905-01", "BQ0905", seed, ["甲", "乙"],
            [firstDeck, baseDeck], skipPreparation: true);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
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
            DisasterLevel = definition.DisasterLevel ?? 0,
            SummonRound = -1,
        };
    }

    private static void AddMorale(L12PlayerState player, int count)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard { CardId = "S01-03C1", InstanceId = $"morale-{index}" });
    }

    private static void Choose(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void ChooseMany(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: choices.ToList()));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game, int maximum = 24)
    {
        var count = 0;
        while (game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt && count++ < maximum)
        {
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.True(count < maximum, "响应窗口未在限定次数内结束");
    }

    [Fact]
    public void RolloAsksHowManyCopiesASelectedGraveWarriorRepresentsAndUsesThatDiscount()
    {
        var game = Create(90501);
        var player = game.State.Players[0];
        var rollo = Card("S02-0302", "rollo");
        var warrior = Card("ST03-08", "rollo-warrior");
        player.Hand.Add(rollo);
        player.Graveyard.Add(warrior);
        AddMorale(player, 8);

        var begin = game.Handle(0, new L12Command("playCard", rollo.InstanceId, Row: 0, Slot: 0));
        Assert.True(begin.Accepted, begin.Error);
        ChooseMany(game, warrior.InstanceId);
        var countPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-rollo-grave-count", countPrompt.Continuation);
        Assert.Equal(3, countPrompt.ValidChoices.Count);
        var asThree = Assert.Single(countPrompt.ValidChoices,
            choice => countPrompt.ChoiceLabels[choice].Contains("视为3张", StringComparison.Ordinal));
        Choose(game, asThree);

        Assert.Same(rollo, player.Field[0][0]);
        Assert.Equal(7, player.Morale.Count(card => card.Tapped));
        Assert.Equal(warrior.InstanceId, Assert.Single(player.Library).InstanceId);
        Assert.DoesNotContain(warrior, player.Graveyard);
    }

    [Fact]
    public void HuntingMomentUsesOneGraveWarriorAndOnePhysicalCardAsItsFourCardCost()
    {
        var game = Create(90502);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var hunt = Card("S01-0319", "hunt");
        var warrior = Card("ST03-08", "hunt-warrior");
        var companion = Card("S01-0001", "hunt-companion");
        var target = Card("S01-0103", "hunt-target");
        player.Hand.Add(hunt);
        player.Graveyard.AddRange([warrior, companion]);
        opponent.Field[0][0] = target;
        AddMorale(player, 3);

        var begin = game.Handle(0, new L12Command("playCard", hunt.InstanceId));
        Assert.True(begin.Accepted, begin.Error);
        var gravePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(2, gravePrompt.MinChoose);
        Assert.Equal(2, gravePrompt.MaxChoose);
        ChooseMany(game, warrior.InstanceId, companion.InstanceId);
        var countPrompt = Assert.Single(game.State.PendingPrompts);
        var asThree = Assert.Single(countPrompt.ValidChoices,
            choice => countPrompt.ChoiceLabels[choice].Contains("视为3张", StringComparison.Ordinal));
        Choose(game, asThree);
        Choose(game, target.InstanceId);
        PassResponses(game);

        Assert.Equal([warrior.InstanceId, companion.InstanceId], player.Library.Select(card => card.InstanceId));
        Assert.DoesNotContain(warrior, player.Graveyard);
        Assert.DoesNotContain(companion, player.Graveyard);
        Assert.Contains(hunt, player.Graveyard);
        Assert.Contains(target, opponent.Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Type == "cost"
            && entry.Text.Contains("2张实体卡牌", StringComparison.Ordinal)
            && entry.Text.Contains("视为4张", StringComparison.Ordinal));
    }

    [Fact]
    public void DynamicTargetPromptShowsTheCurrentLegendaryBloodlineBonus()
    {
        var game = Create(90503);
        var player = game.State.Players[0];
        var bloodline = Card("ST03-10", "bloodline");
        var target = Card("ST03-01", "bloodline-target");
        player.Hand.Add(bloodline);
        player.Field[0][0] = target;
        player.Graveyard.Add(Card("ST03-08", "bloodline-warrior"));
        AddMorale(player, 1);

        var begin = game.Handle(0, new L12Command("playCard", bloodline.InstanceId));
        Assert.True(begin.Accepted, begin.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("当前加3000", prompt.ChoiceLabels[target.InstanceId]);
    }

    [Fact]
    public void GraveCostPublicEntryLetsOneWarriorPayThorHammersThreeCardCost()
    {
        var game = CreateWithFirstMaster("S02-03M1", 90504);
        var player = game.State.Players[0];
        var hammer = Card("S02-0301", "public-grave-hammer");
        var warrior = Card("ST03-08", "public-grave-warrior");
        player.Graveyard.AddRange([hammer, warrior]);

        var begin = game.Handle(0, new L12Command("activateAbility", hammer.InstanceId,
            Ability: "thorHammerRevive"));
        Assert.True(begin.Accepted, begin.Error);
        var gravePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("order", gravePrompt.Kind);
        Assert.Equal(1, gravePrompt.MinChoose);
        ChooseMany(game, warrior.InstanceId);

        var countPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("grave-faction-count", game.State.PendingActivations.Single()
            .SelectionSteps[game.State.PendingActivations.Single().CurrentStep].Kind);
        var asThree = Assert.Single(countPrompt.ValidChoices,
            choice => countPrompt.ChoiceLabels[choice].Contains("视为3张", StringComparison.Ordinal));
        Choose(game, asThree);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Choose(game, slotPrompt.ValidChoices[0]);
        PassResponses(game);

        Assert.DoesNotContain(warrior, player.Graveyard);
        Assert.Contains(warrior, player.Library);
        Assert.Contains(player.Field.SelectMany(row => row), card => card?.InstanceId == hammer.InstanceId);
    }
}
