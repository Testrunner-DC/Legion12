using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch1RegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 83101, bool autoPassEmptyResponses = true)
    {
        var game = new L12GameEngine(Catalog, "atomic-review-batch1", "ATOMIC-B1", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: autoPassEmptyResponses, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
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

    private static L12CardInstance Card(string cardId, string instanceId, int? cost = null, int? troops = null)
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
            Cost = cost ?? definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static L12CardInstance PlainLegion(string instanceId, int troops, string faction = "universal",
        int disasterLevel = 0)
        => new()
        {
            InstanceId = instanceId,
            CardId = $"test-{instanceId}",
            Name = instanceId,
            CardType = "legion",
            Faction = faction,
            Cost = 1,
            BaseTroops = troops,
            Troops = troops,
            DisasterLevel = disasterLevel,
            SummonRound = -1,
        };

    private static L12MoraleCard Morale(string id, bool godPower = false)
        => new() { InstanceId = id, CardId = "S01-01C1", IsGodPower = godPower, Tapped = false };

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        return prompt;
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

    [Fact]
    public void NyxDeclaresBothPublicTargetsModeAndGodPowerBeforeBasePayment()
    {
        var game = Create();
        var player = game.State.Players[0];
        var first = PlainLegion("nyx-first", 5000);
        var second = PlainLegion("nyx-second", 5000);
        game.State.Players[1].Field[0][0] = first;
        game.State.Players[1].Field[0][1] = second;
        var power = Morale("nyx-power", godPower: true);
        player.Morale.Add(power);
        var nyx = Card("S02-0522", "nyx-plan", cost: 0);
        player.Hand.Add(nyx);

        Assert.True(game.Handle(0, new L12Command("playCard", nyx.InstanceId)).Accepted);

        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", mode.Continuation);
        Assert.Contains("mode:second", mode.ValidChoices);
        Assert.Contains(nyx, player.Hand);
        Assert.False(power.Tapped);

        Resolve(game, "mode:second");
        var primary = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(first.InstanceId, primary.ValidChoices);
        Resolve(game, first.InstanceId);
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(power.InstanceId, payment.ValidChoices);
        Resolve(game, power.InstanceId);
        var secondary = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(second.InstanceId, secondary.ValidChoices);
        Resolve(game, second.InstanceId);

        Assert.DoesNotContain(nyx, player.Hand);
        Assert.True(power.Tapped);
        Assert.False(power.IsGodPower);
        Assert.Equal(2000, first.Troops);
        Assert.Equal(3000, second.Troops);
        var segmentPushes = game.State.Events.Where(entry => entry.Type is "stack-push" or "stack-deferred"
            && entry.Cards.Any(card => card.InstanceId == nyx.InstanceId)).ToArray();
        Assert.Equal(2, segmentPushes.Length);
    }

    [Fact]
    public void DesertRuleDeclaresEveryDiscardSummonAndSlotBeforeMovingAnyCard()
    {
        var game = Create(83102);
        var player = game.State.Players[0];
        var firstCost = PlainLegion("desert-cost-1", 2000);
        var secondCost = PlainLegion("desert-cost-2", 2000);
        player.Field[0][0] = firstCost;
        player.Field[0][1] = secondCost;
        var summon = PlainLegion("desert-summon", 4000, "taiyangcheng", disasterLevel: 2);
        player.Hand.Add(summon);
        var tactic = Card("S02-0207", "desert-plan", cost: 0);
        player.Hand.Add(tactic);

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        var costs = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", costs.Continuation);
        Assert.Contains(firstCost.InstanceId, costs.ValidChoices);
        Assert.Contains(secondCost.InstanceId, costs.ValidChoices);
        Assert.Contains(tactic, player.Hand);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: costs.PromptId,
            CardInstanceIds: [firstCost.InstanceId, secondCost.InstanceId])).Accepted);
        var summonPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(summon.InstanceId, summonPrompt.ValidChoices);
        Assert.Same(firstCost, player.Field[0][0]);
        Assert.Same(secondCost, player.Field[0][1]);
        Resolve(game, summon.InstanceId);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("0:0", slot.ValidChoices);
        Resolve(game, "0:0");

        Assert.Contains(firstCost, player.Graveyard);
        Assert.Contains(secondCost, player.Graveyard);
        Assert.Same(summon, player.Field[0][0]);
    }

    [Fact]
    public void CancellingCompositeDeclarationNeverPaysOrMovesTheSource()
    {
        var game = Create(83103);
        var player = game.State.Players[0];
        player.Morale.Add(Morale("cancel-cost"));
        game.State.Players[1].Field[0][0] = PlainLegion("cancel-target", 3000);
        var qianyang = Card("S02-0105", "cancel-qianyang", cost: 1);
        player.Hand.Add(qianyang);

        Assert.True(game.Handle(0, new L12Command("playCard", qianyang.InstanceId)).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", prompt.Continuation);
        Resolve(game, "skip");

        Assert.Contains(qianyang, player.Hand);
        Assert.False(player.Morale[0].Tapped);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    public void InvalidLaterSegmentTargetCancelsThatSegmentBeforeItsDeclaredCostIsPaid()
    {
        var game = Create(83105, autoPassEmptyResponses: false);
        var player = game.State.Players[0];
        var first = PlainLegion("invalid-first", 5000);
        var second = PlainLegion("invalid-second", 5000);
        game.State.Players[1].Field[0][0] = first;
        game.State.Players[1].Field[0][1] = second;
        var power = Morale("invalid-power", godPower: true);
        player.Morale.Add(power);
        var nyx = Card("S02-0522", "invalid-nyx", cost: 0);
        player.Hand.Add(nyx);

        Assert.True(game.Handle(0, new L12Command("playCard", nyx.InstanceId)).Accepted);
        Resolve(game, "mode:second");
        Resolve(game, first.InstanceId);
        Resolve(game, power.InstanceId);
        Resolve(game, second.InstanceId);
        Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 1,
            CardInstanceId: second.InstanceId)).Accepted);
        PassResponses(game);

        Assert.False(power.Tapped);
        Assert.True(power.IsGodPower);
        Assert.Equal(2000, first.Troops);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("公开目标已失效", StringComparison.Ordinal)
            && entry.Text.Contains("其余独立段继续", StringComparison.Ordinal));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    public void ShockCollateralKillsAreTypedSourceKillsButNotPrintedKillTimingAndConsumeOnce()
    {
        var game = Create(83104);
        var attacker = Card("S02-0516", "shock-source", troops: 4000);
        attacker.HasShock = true;
        attacker.AttackNoLossUntilTurn = game.State.TurnSerial;
        attacker.ReadyAfterNextKillUntilTurn = game.State.TurnSerial;
        attacker.ReadyAfterNextKillSourceName = "匠神锻造炉";
        var primary = PlainLegion("shock-primary", 6000);
        var left = PlainLegion("shock-left", 1000);
        var right = PlainLegion("shock-right", 1000);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][1] = primary;
        game.State.Players[1].Field[0][0] = left;
        game.State.Players[1].Field[0][2] = right;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", primary.InstanceId))).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains("mode:none", declaration.ValidChoices);
        Resolve(game, "mode:none");

        Assert.Contains(left, game.State.Players[1].Graveyard);
        Assert.Contains(right, game.State.Players[1].Graveyard);
        Assert.Same(attacker, game.State.Players[0].Field[0][0]);
        Assert.False(attacker.Tapped);
        Assert.Equal(-1, attacker.ReadyAfterNextKillUntilTurn);
        var granted = game.State.Events.Where(entry => entry.Type == "effect-trigger"
            && entry.Text.Contains("匠神锻造炉", StringComparison.Ordinal)).ToArray();
        Assert.True(granted.Length == 1, string.Join(" | ", game.State.Events
            .Where(entry => entry.Type == "effect-trigger").Select(entry => entry.Text)));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.Text.Contains("【击杀时】", StringComparison.Ordinal));
    }
}
