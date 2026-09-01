using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6LARegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static readonly IReadOnlyDictionary<string, int> AuditedAbilityCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["S02-0001"] = 2, ["S02-0002"] = 2, ["S02-0003"] = 3, ["S02-0004"] = 2,
            ["S02-0005"] = 2, ["S02-0006"] = 2, ["S02-0007"] = 3, ["S02-0008"] = 2,
            ["S02-0009"] = 1, ["S02-0010"] = 2, ["S02-0011"] = 1, ["S02-0012"] = 3,
            ["S02-0013"] = 3, ["S02-0014"] = 1, ["S02-0015"] = 1, ["S02-0016"] = 3,
            ["S02-0017"] = 1, ["S02-0018"] = 1,
            ["S02-0101"] = 2, ["S02-0102"] = 2, ["S02-0103"] = 2, ["S02-0104"] = 2,
            ["S02-0105"] = 1, ["S02-0106"] = 1, ["S02-01M1"] = 4, ["S02-01S1"] = 2,
        };

    private static L12GameEngine Create(int seed = 8401, string firstMaster = "S01-01M1")
    {
        var baseDeck = Catalog.DeckAt(0);
        var firstDeck = new L12PresetDeckDefinition
        {
            Name = $"{firstMaster}第六批6L-A审查牌库",
            MasterId = firstMaster,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "atomic-review-batch6la", "ATOMIC6LA", seed,
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

    private static void AddMorale(L12PlayerState player, int count, string prefix)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-01C1",
                InstanceId = $"{prefix}-{index}",
            });
    }

    private static void AddMoraleDeck(L12PlayerState player, int count, string prefix)
    {
        for (var index = 0; index < count; index++)
            player.MoraleDeck.Add(new L12MoraleCard
            {
                CardId = "S01-01C1",
                InstanceId = $"{prefix}-{index}",
            });
    }

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

    private static L12StackItem PassUntilFlow(L12GameEngine game, string flow)
    {
        for (var safety = 0; safety < 80; safety++)
        {
            var item = game.State.EffectStack.SingleOrDefault(candidate =>
                candidate.Data.GetValueOrDefault("atomicFlow") == flow);
            if (item is not null) return item;
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.Continuation == "pending-activation")
            {
                Assert.Contains("mode:none", prompt.ValidChoices);
                Resolve(game, "mode:none");
                continue;
            }
            Assert.Equal("stack-response", prompt.Continuation);
            Resolve(game, "pass");
        }
        throw new Xunit.Sdk.XunitException($"未进入预期效果段 {flow}");
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 120 && game.State.PendingPrompts.Count > 0; safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.Continuation == "pending-activation")
            {
                Assert.Contains("mode:none", prompt.ValidChoices);
                Resolve(game, "mode:none");
                continue;
            }
            Assert.Equal("stack-response", prompt.Continuation);
            Resolve(game, "pass");
        }
        Assert.Empty(game.State.PendingPrompts);
    }

    private static (L12GameEngine Game, L12PlayerState Player, L12CardInstance Yingzheng, L12CardInstance Cost)
        BeginYingzheng(int seed)
    {
        var game = Create(seed);
        var player = game.State.Players[0];
        var yingzheng = Card("S02-0101", $"batch6la-yingzheng-{seed}");
        var cost = Card("S02-0101", $"batch6la-yingzheng-cost-{seed}");
        player.Hand.Add(yingzheng);
        player.Hand.Add(cost);
        AddMorale(player, 8, $"batch6la-yingzheng-morale-{seed}");

        var play = game.Handle(0, new L12Command("playCard", yingzheng.InstanceId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-yingzheng-enter-cost", costPrompt.Continuation);
        Resolve(game, cost.InstanceId);
        return (game, player, yingzheng, cost);
    }

    [Fact]
    [Trait("L12Evidence", "batch:6L-A-inventory")]
    public void S2UniversalAndTiantingAuditFreezesEveryCardAndAbility()
    {
        Assert.Equal(26, AuditedAbilityCounts.Count);
        Assert.Equal(51, AuditedAbilityCounts.Values.Sum());
        Assert.All(AuditedAbilityCounts, pair =>
        {
            var card = Assert.Contains(pair.Key, Catalog.Cards);
            Assert.Contains(card.Faction, new[] { "universal", "tianting" });
            Assert.False(string.IsNullOrWhiteSpace(card.Effect));
            Assert.True(pair.Value > 0);
        });
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-01M1")]
    public void WukongDeclaresFrontSlotBeforeReturningMoraleAndEnteringStack()
    {
        var game = Create(8402, "S02-01M1");
        var player = game.State.Players[0];
        player.Field[0][0] = Card("S02-0004", "batch6la-wukong-occupied");
        AddMorale(player, 3, "batch6la-wukong-morale");
        var paymentIds = player.Morale.Select(card => card.InstanceId).ToArray();

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "wukongTransform")).Accepted);
        Resolve(game, paymentIds);

        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", slot.Continuation);
        Assert.Equal("slot", slot.Kind);
        Assert.DoesNotContain("0:0", slot.ValidChoices);
        Assert.Contains("0:1", slot.ValidChoices);
        Assert.Contains("0:2", slot.ValidChoices);
        Assert.Equal(3, player.Morale.Count);
        Assert.Empty(game.State.EffectStack);

        Resolve(game, "0:2");
        Assert.Empty(player.Morale);
        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal("0:2", item.Data["slot"]);
        Assert.Equal("3", item.Data["count"]);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-01M1")]
    public void WukongOccupiedDeclaredSlotCancelsEntryWithoutRefundOrOverwrite()
    {
        var game = Create(8403, "S02-01M1");
        var player = game.State.Players[0];
        AddMorale(player, 2, "batch6la-wukong-conflict-morale");
        var returned = player.Morale.Select(card => card.InstanceId).ToArray();

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "wukongTransform")).Accepted);
        Resolve(game, returned);
        Resolve(game, "0:1");
        var blocker = Card("S02-0004", "batch6la-wukong-blocker");
        player.Field[0][1] = blocker;

        PassResponses(game);

        Assert.Same(blocker, player.Field[0][1]);
        Assert.DoesNotContain(player.Field.SelectMany(row => row), card => card?.IsMasterLegion == true);
        Assert.Empty(player.Morale);
        Assert.Contains("active:master-0:wukongTransform", player.UsedAbilities);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("孙悟空", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0101")]
    public void YingzhengKillAndSubsequentMoraleReturnAreIndependentResponseItems()
    {
        var (game, player, _, cost) = BeginYingzheng(8404);
        var other = Card("S02-0004", "batch6la-yingzheng-other");
        player.Field[0][1] = other;

        var kill = PassUntilFlow(game, "yingzheng-kill");
        Assert.Contains(cost, player.Graveyard);
        Assert.Equal(8, player.Morale.Count);
        kill.Negated = true;

        var followup = PassUntilFlow(game, "yingzheng-return");
        Assert.Same(other, player.Field[0][1]);
        Assert.Equal(8, player.Morale.Count);
        Assert.NotEqual(kill.StackItemId, followup.StackItemId);

        PassResponses(game);
        Assert.Empty(player.Morale);
        Assert.Equal(game.State.TurnSerial, player.FactionMoraleAdditionForbiddenUntilTurn);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0101")]
    public void YingzhengReturnSegmentCanBeNegatedWithoutUndoingKillOrPaidCost()
    {
        var (game, player, _, cost) = BeginYingzheng(8405);
        var other = Card("S02-0004", "batch6la-yingzheng-killed");
        player.Field[0][1] = other;

        _ = PassUntilFlow(game, "yingzheng-kill");
        var followup = PassUntilFlow(game, "yingzheng-return");
        followup.Negated = true;
        PassResponses(game);

        Assert.DoesNotContain(other, player.Field.SelectMany(row => row));
        Assert.Contains(other, player.Graveyard);
        Assert.Contains(cost, player.Graveyard);
        Assert.Equal(8, player.Morale.Count);
        Assert.NotEqual(game.State.TurnSerial, player.FactionMoraleAdditionForbiddenUntilTurn);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0101")]
    public void YingzhengBlocksNonFactionMoraleButAllowsTiantingFactionRecovery()
    {
        var (game, player, _, _) = BeginYingzheng(8406);
        PassResponses(game);
        Assert.Empty(player.Morale);
        AddMoraleDeck(player, 2, "batch6la-yingzheng-deck");

        var blocked = Assert.IsType<int>(Invoke(game, "AddMorale", player, 1, true, false));
        var faction = Assert.IsType<int>(Invoke(game, "AddMorale", player, 1, true, true));

        Assert.Equal(0, blocked);
        Assert.Equal(1, faction);
        Assert.Single(player.Morale);
        Assert.True(player.Morale[0].Tapped);
    }
}
