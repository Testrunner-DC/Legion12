using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6LBRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static readonly IReadOnlyDictionary<string, int> AuditedAbilityCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["S02-0201"] = 2, ["S02-0202"] = 2, ["S02-0203"] = 3, ["S02-0204"] = 3,
            ["S02-0205"] = 4, ["S02-0206"] = 3, ["S02-0207"] = 1, ["S02-02M1"] = 3,
            ["S02-0301"] = 4, ["S02-0302"] = 4, ["S02-0303"] = 2, ["S02-0304"] = 3,
            ["S02-0305"] = 4, ["S02-0306"] = 2, ["S02-0307"] = 1, ["S02-03M1"] = 3,
        };

    private static L12GameEngine Create(int seed = 8501, string firstMaster = "S01-02M1")
    {
        var baseDeck = Catalog.DeckAt(0);
        var firstDeck = new L12PresetDeckDefinition
        {
            Name = $"{firstMaster}第六批6L-B审查牌库",
            MasterId = firstMaster,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "atomic-review-batch6lb", "ATOMIC6LB", seed,
            ["甲", "乙"], [firstDeck, baseDeck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
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
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.Resolving.Clear();
            player.ExtraRelics.Clear();
            player.Relic = null;
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

    private static L12CardInstance PlainLegion(string instanceId, string name = "审查军团",
        string faction = "taiyangcheng", int disasterLevel = 0)
        => new()
        {
            InstanceId = instanceId,
            CardId = $"TEST-{instanceId}",
            Name = name,
            CardType = "legion",
            Faction = faction,
            Cost = 1,
            BaseTroops = 1000,
            Troops = 1000,
            DisasterLevel = disasterLevel,
            SummonRound = -1,
        };

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    private static void Resolve(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var command = choices.Length == 1
            ? new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choices[0])
            : new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [.. choices]);
        var result = game.Handle(prompt.PlayerIndex, command);
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 120 && game.State.PendingPrompts.Count > 0; safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("stack-response", prompt.Continuation);
            Resolve(game, "pass");
        }
        Assert.Empty(game.State.PendingPrompts);
    }

    private static void AddMorale(L12PlayerState player, int count, string prefix)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard { CardId = "S01-02C1", InstanceId = $"{prefix}-{index}" });
    }

    [Fact]
    [Trait("L12Evidence", "batch:6L-B-inventory")]
    public void S2SunCityAndAsgardAuditFreezesEveryCardAndAbility()
    {
        Assert.Equal(16, AuditedAbilityCounts.Count);
        Assert.Equal(44, AuditedAbilityCounts.Values.Sum());
        Assert.All(AuditedAbilityCounts, pair =>
        {
            var card = Assert.Contains(pair.Key, Catalog.Cards);
            Assert.Contains(card.Faction, new[] { "taiyangcheng", "asgard" });
            Assert.False(string.IsNullOrWhiteSpace(card.Effect));
            Assert.True(pair.Value > 0);
        });
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0202")]
    public void TombPaladinCountsEveryOwnTurnTombNamedDepartureForControllerAndResetsAtTurnEnd()
    {
        var game = Create(8502);
        var controller = game.State.Players[0];
        var owner = game.State.Players[1];
        var paladin = Card("S02-0202", "batch6lb-paladin-hand");
        controller.Hand.Add(paladin);
        var construct = Card("S01-0204", "batch6lb-controlled-construct");
        construct.OwnerIndex = 1;
        controller.Field[0][0] = construct;

        var removed = (bool)Invoke(game, "RemoveFromField", controller, construct, true,
            "因审查效果弃置", false, L12FieldLeaveKind.Discard, true, false)!;

        Assert.True(removed);
        Assert.Equal(1, controller.TombNamedLegionsLeftThisTurn);
        Assert.Equal(0, owner.TombNamedLegionsLeftThisTurn);
        Assert.Contains(construct, owner.Graveyard);
        Assert.Equal(Math.Max(0, paladin.Cost - 1),
            (int)Invoke(game, "GetPlayCost", 0, paladin, false, 0, 0)!);

        Assert.False((bool)Invoke(game, "RemoveFromField", controller, construct, true,
            "不得重复登记", false, L12FieldLeaveKind.Discard, true, false)!);
        Assert.Equal(1, controller.TombNamedLegionsLeftThisTurn);

        game.State.Phase = L12Phase.End;
        Invoke(game, "CompleteEndTurn", 0);
        Assert.Equal(0, controller.TombNamedLegionsLeftThisTurn);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0202")]
    public void TombPaladinDoesNotCountControllerDepartureOutsideThatControllersOwnTurn()
    {
        var game = Create(8503);
        var player = game.State.Players[0];
        var construct = Card("S01-0204", "batch6lb-opponent-turn-construct");
        player.Field[0][0] = construct;
        game.State.ActivePlayer = 1;

        Assert.True((bool)Invoke(game, "MoveFieldCardToZone", player, construct, "hand",
            "因审查效果返回手牌", false)!);

        Assert.Equal(0, player.TombNamedLegionsLeftThisTurn);
        Assert.Contains(construct, player.Hand);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0207")]
    public void DesertRulePrepaysDiscardCostBeforeResponseAndOccupiedSlotDoesNotRefundOrOverwrite()
    {
        var game = Create(8504);
        var player = game.State.Players[0];
        var firstCost = PlainLegion("batch6lb-desert-cost-1");
        var secondCost = PlainLegion("batch6lb-desert-cost-2");
        player.Field[0][0] = firstCost;
        player.Field[0][1] = secondCost;
        var summon = PlainLegion("batch6lb-desert-summon", disasterLevel: 2);
        player.Hand.Add(summon);
        var tactic = Card("S02-0207", "batch6lb-desert-tactic");
        player.Hand.Add(tactic);
        AddMorale(player, 4, "batch6lb-desert-morale");

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        Resolve(game, firstCost.InstanceId, secondCost.InstanceId);
        Resolve(game, summon.InstanceId);
        Resolve(game, "0:0");

        Assert.Contains(firstCost, player.Graveyard);
        Assert.Contains(secondCost, player.Graveyard);
        Assert.Null(player.Field[0][0]);
        Assert.Null(player.Field[0][1]);
        Assert.Contains(summon, player.Hand);
        Assert.Single(game.State.EffectStack);

        var blocker = PlainLegion("batch6lb-desert-blocker");
        player.Field[0][0] = blocker;
        PassResponses(game);

        Assert.Same(blocker, player.Field[0][0]);
        Assert.Contains(summon, player.Hand);
        Assert.Contains(firstCost, player.Graveyard);
        Assert.Contains(secondCost, player.Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("已弃置费用不恢复", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0301")]
    public void ThorHammerGraveyardActiveButtonStartsCostAndSlotDeclarationWithoutDuplicateConfirmation()
    {
        var game = Create(8505, "S02-03M1");
        var player = game.State.Players[0];
        var hammer = Card("S02-0301", "batch6lb-hammer");
        var costs = Enumerable.Range(0, 3).Select(index => Card("S02-0001", $"batch6lb-hammer-cost-{index}"))
            .ToArray();
        player.Graveyard.Add(hammer);
        player.Graveyard.AddRange(costs);

        var begin = game.Handle(0, new L12Command("activateAbility", hammer.InstanceId,
            Ability: "thorHammerRevive"));

        Assert.True(begin.Accepted, begin.Error);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", costPrompt.Continuation);
        Assert.Equal("grave-card", costPrompt.Kind);
        Assert.DoesNotContain(hammer.InstanceId, costPrompt.ValidChoices);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "graveyard-active-confirm");
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-03M1")]
    public void ThorChargePublicPaymentOffersGodPowerAndTombGuardResources()
    {
        var game = Create(8506, "S02-03M1");
        var player = game.State.Players[0];
        player.Hp = 3;
        var guard = Card("S01-0212", "batch6lb-thor-guard");
        player.Field[0][0] = guard;
        var godPower = new L12MoraleCard
        {
            CardId = "S02-05C1",
            InstanceId = "batch6lb-thor-god-power",
            IsGodPower = true,
        };
        player.Morale.Add(godPower);
        player.Morale.Add(new L12MoraleCard
        {
            CardId = "S01-03C1",
            InstanceId = "batch6lb-thor-ordinary-morale",
        });

        var begin = game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "thorCharge"));

        Assert.True(begin.Accepted, begin.Error);
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-payment", payment.Kind);
        Assert.Contains(guard.InstanceId, payment.ValidChoices);
        Assert.Contains(godPower.InstanceId, payment.ValidChoices);
    }
}
