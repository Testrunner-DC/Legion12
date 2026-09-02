using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class StarterBatch3ARegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "starter-3a", "STARTER3A", seed, ["甲", "乙"], [0, 1],
            skipPreparation: true);
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
            SummonRound = -1,
        };
    }

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == args.Length);
        return method.Invoke(target, args);
    }

    private static void Queue(L12GameEngine game, L12CardInstance source, string trigger)
        => Invoke(game, "QueueOrPushTriggeredEffect", 0, source, trigger, $"【{trigger}】效果", null,
            new Dictionary<string, string>());

    [Fact]
    public void Batch3AAbilitiesAndProgramsUseTheSameStructuredBoundaries()
    {
        var expected = new (string CardId, string Trigger)[]
        {
            ("ST01-06", "enter"), ("ST03-02", "continuous"), ("ST03-04", "after-damage"),
        };
        foreach (var (cardId, trigger) in expected)
        {
            Assert.NotNull(L12VerifiedAtomicPrograms.Find(cardId, trigger));
            Assert.Contains(Catalog.AtomicEffects.Find(cardId)!.Abilities,
                ability => ability.Trigger == trigger && ability.MigrationStatus == "verified"
                    && !ability.HasLegacyFallback);
        }
    }

    [Fact]
    public void LiuJiDiscountsOnlyTheNextLegionAndExpiresAtTurnEnd()
    {
        var game = Create(20301);
        var player = game.State.Players[0];
        var liuJi = Card("ST01-06", "liu-ji");
        player.Field[0][0] = liuJi;

        Queue(game, liuJi, "enter");
        Assert.Equal(1, player.NextLegionEntryDiscount);

        var legion = Card("ST01-04", "discounted-legion");
        player.Hand.Add(legion);
        for (var index = 0; index < 8; index++)
            player.Morale.Add(new L12MoraleCard { CardId = "ST01-C", InstanceId = $"morale-{index}" });
        Assert.Equal(legion.Cost - 1, (int)Invoke(game, "GetPlayCost", 0, legion, false, 0, 0)!);
        Assert.True(game.Handle(0, new L12Command("playCard", legion.InstanceId, Row: 0, Slot: 1)).Accepted);
        Assert.Equal(0, player.NextLegionEntryDiscount);

        Queue(game, liuJi, "enter");
        Assert.Equal(1, player.NextLegionEntryDiscount);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        Invoke(game, "CompleteEndTurn", 0);
        Assert.Equal(0, player.NextLegionEntryDiscount);
    }

    [Fact]
    public void VikingBerserkerDiscountRequiresHandAndHpAtMostSeven()
    {
        var game = Create(20302);
        var player = game.State.Players[0];
        var berserker = Card("ST03-02", "berserker");
        player.Hand.Add(berserker);

        player.Hp = 8;
        Assert.Equal(0, L12StructuredCardRules.HandPlayCostModifier(player, berserker));
        player.Hp = 7;
        Assert.Equal(-1, L12StructuredCardRules.HandPlayCostModifier(player, berserker));
        player.Hand.Remove(berserker);
        player.Field[0][0] = berserker;
        Assert.Equal(0, L12StructuredCardRules.HandPlayCostModifier(player, berserker));
    }

    [Fact]
    public void VikingRaiderDrawsTwoAfterItDamagesTheOpponentMaster()
    {
        var game = Create(20303);
        var player = game.State.Players[0];
        var raider = Card("ST03-04", "raider");
        player.Field[0][0] = raider;
        player.Library.Clear();
        player.Library.Add(Card("ST01-04", "draw-one"));
        player.Library.Add(Card("ST01-05", "draw-two"));

        Queue(game, raider, "after-damage");

        Assert.Equal(2, player.Hand.Count);
        Assert.Empty(player.Library);
        Assert.Contains(game.State.Events, item => item.Text.Contains("维京掠夺者对主宰造成伤害后抽取2张牌"));
    }
}
