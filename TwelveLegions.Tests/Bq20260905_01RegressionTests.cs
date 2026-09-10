using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class Bq20260905_01RegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, bool checkpointReady = false)
    {
        var game = new L12GameEngine(Catalog, "bq-20260905-01", "BQ0905", seed, ["甲", "乙"], [0, 1],
            skipPreparation: true, stateFormatVersion: checkpointReady ? 2 : 0);
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
        Assert.Equal(4, countPrompt.ValidChoices.Count);
        Assert.Contains("cancel", countPrompt.ValidChoices);
        var asThree = Assert.Single(countPrompt.ValidChoices,
            choice => countPrompt.ChoiceLabels[choice].Contains("视为3张", StringComparison.Ordinal));
        Choose(game, asThree);

        Assert.Same(rollo, player.Field[0][0]);
        Assert.Equal(7, player.Morale.Count(card => card.Tapped));
        Assert.Equal(warrior.InstanceId, Assert.Single(player.Library).InstanceId);
        Assert.DoesNotContain(warrior, player.Graveyard);
    }

    [Fact]
    public void RolloOnlyOffersAffordableWarriorCountsAndCancellationSurvivesRestoreAndStaleSubmission()
    {
        var game = Create(905011, checkpointReady: true);
        var player = game.State.Players[0];
        var rollo = Card("S02-0302", "rollo-seven-morale");
        var warrior = Card("ST03-08", "rollo-seven-morale-warrior");
        player.Hand.Add(rollo);
        player.Graveyard.Add(warrior);
        AddMorale(player, 7);

        var begin = game.Handle(0, new L12Command("playCard", rollo.InstanceId, Row: 0, Slot: 0));
        Assert.True(begin.Accepted, begin.Error);
        var gravePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("cancel", gravePrompt.ValidChoices);
        Assert.Equal(1, gravePrompt.MinChoose);
        ChooseMany(game, warrior.InstanceId);

        var countPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-rollo-grave-count", countPrompt.Continuation);
        Assert.Contains("cancel", countPrompt.ValidChoices);
        Assert.DoesNotContain(countPrompt.ValidChoices,
            choice => countPrompt.ChoiceLabels[choice].Contains("视为1张", StringComparison.Ordinal));
        Assert.Contains(countPrompt.ValidChoices,
            choice => countPrompt.ChoiceLabels[choice].Contains("视为2张", StringComparison.Ordinal));

        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence);
        var restoredPrompt = Assert.Single(restored.State.PendingPrompts);
        var staleChoice = $"grave-copies:{warrior.InstanceId}=1";
        var stale = restored.Handle(0, new L12Command("resolvePrompt", PromptId: restoredPrompt.PromptId,
            Choice: staleChoice));
        Assert.False(stale.Accepted);
        Assert.Equal(restoredPrompt.PromptId, Assert.Single(restored.State.PendingPrompts).PromptId);

        var cancel = restored.Handle(0, new L12Command("resolvePrompt", PromptId: restoredPrompt.PromptId,
            Choice: "cancel"));
        Assert.True(cancel.Accepted, cancel.Error);
        Assert.Empty(restored.State.PendingPrompts);
        Assert.Contains(restored.State.Players[0].Hand, card => card.InstanceId == rollo.InstanceId);
        Assert.Contains(restored.State.Players[0].Graveyard, card => card.InstanceId == warrior.InstanceId);
        Assert.All(restored.State.Players[0].Morale, morale => Assert.False(morale.Tapped));
        Assert.Null(restored.State.Players[0].Field[0][0]);
    }

    [Fact]
    public void RolloRejectedGraveSelectionReturnsToACancellablePromptInsteadOfDeadlocking()
    {
        var game = Create(905012);
        var player = game.State.Players[0];
        var rollo = Card("S02-0302", "rollo-retry");
        var ordinary = Card("S01-0301", "rollo-insufficient-ordinary");
        var alternativeWarrior = Card("ST03-08", "rollo-affordable-alternative");
        player.Hand.Add(rollo);
        player.Graveyard.AddRange([ordinary, alternativeWarrior]);
        AddMorale(player, 7);

        Assert.True(game.Handle(0, new L12Command("playCard", rollo.InstanceId, Row: 0, Slot: 0)).Accepted);
        ChooseMany(game, ordinary.InstanceId);

        var retry = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-rollo-grave-cost", retry.Continuation);
        Assert.Contains("cancel", retry.ValidChoices);
        Assert.Contains("不足", retry.Data["retryReason"], StringComparison.Ordinal);
        Assert.Contains(rollo, player.Hand);
        Assert.Contains(ordinary, player.Graveyard);
        Assert.Contains(alternativeWarrior, player.Graveyard);
        Assert.All(player.Morale, morale => Assert.False(morale.Tapped));

        var cancel = game.Handle(0, new L12Command("resolvePrompt", PromptId: retry.PromptId, Choice: "cancel"));
        Assert.True(cancel.Accepted, cancel.Error);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Null(player.Field[0][0]);
    }

    [Fact]
    public void HuntingMomentReturnsOneGraveWarriorAndOnePhysicalCardAsFourCardsByEffect()
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
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("2张实体卡牌", StringComparison.Ordinal)
            && entry.Text.Contains("视为4张", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "cost"
            && entry.Text.Contains("墓地", StringComparison.Ordinal));
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

    [Fact]
    public void ThorHammerLetsTwoWarriorsRepresentItsThreeCardGraveCost()
    {
        var game = CreateWithFirstMaster("S02-03M1", 90505);
        var player = game.State.Players[0];
        var hammer = Card("S02-0301", "thor-hammer-two-warriors");
        var first = Card("ST03-08", "thor-hammer-warrior-first");
        var second = Card("ST03-08", "thor-hammer-warrior-second");
        player.Graveyard.AddRange([hammer, first, second]);

        var begin = game.Handle(0, new L12Command("activateAbility", hammer.InstanceId,
            Ability: "thorHammerRevive"));
        Assert.True(begin.Accepted, begin.Error);
        ChooseMany(game, first.InstanceId, second.InstanceId);

        var firstCount = Assert.Single(game.State.PendingPrompts);
        var firstAsOne = Assert.Single(firstCount.ValidChoices,
            choice => firstCount.ChoiceLabels[choice].Contains("视为1张", StringComparison.Ordinal));
        Choose(game, firstAsOne);

        var secondCount = Assert.Single(game.State.PendingPrompts);
        var secondAsTwo = Assert.Single(secondCount.ValidChoices,
            choice => secondCount.ChoiceLabels[choice].Contains("视为2张", StringComparison.Ordinal));
        Choose(game, secondAsTwo);

        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Choose(game, slotPrompt.ValidChoices[0]);
        PassResponses(game);

        Assert.DoesNotContain(first, player.Graveyard);
        Assert.DoesNotContain(second, player.Graveyard);
        Assert.Equal([first.InstanceId, second.InstanceId], player.Library.Select(card => card.InstanceId));
        Assert.Contains(player.Field.SelectMany(row => row), card => card?.InstanceId == hammer.InstanceId);
    }
}
