using TwelveLegions.Server;
using Xunit;
using System.Text.RegularExpressions;

namespace TwelveLegions.Tests;

public sealed class RuleKernelTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12PlayerState Player() => new()
    {
        PlayerIndex = 0,
        Name = "测试者",
        DeckName = "测试牌库",
        Faction = "tianting",
        MasterId = "S01-01M1",
        MasterName = "杨戬",
        Hp = 8,
        MaxHp = 8,
    };

    private static L12CardInstance Card(string id, int troops = 2000, string? name = null, string faction = "tianting", string[]? traits = null) => new()
    {
        InstanceId = id,
        CardId = id,
        Name = name ?? id,
        CardType = "legion",
        Faction = faction,
        Cost = 1,
        Traits = traits is null ? [] : [.. traits],
        BaseTroops = troops,
        Troops = troops,
    };

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
        return new L12GameEngine(Catalog, "rule-kernel", "RULEKERNEL", seed,
            ["甲", "乙"], [firstDeck, baseDeck], skipPreparation: true);
    }

    [Fact]
    public void TemporaryAndContinuousTroopsCardPoolScanIsPinned()
    {
        var scanned = Catalog.Cards.Values
            .Where(card => Regex.IsMatch(card.Effect ?? string.Empty,
                @"兵力[^\d\r\n]{0,8}(?:\+|＋|-|－|−)\s*\d+|兵力[^\r\n]{0,8}(?:视为|变为)|兵力额外"))
            .Select(card => card.Id)
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[]
        {
            "S01-0005", "S01-0008", "S01-0009", "S01-0017", "S01-0019", "S01-0020",
            "S01-0104", "S01-0106", "S01-0107", "S01-0110", "S01-0118", "S01-0201",
            "S01-0203", "S01-0204", "S01-0206", "S01-0208", "S01-0212", "S01-0215",
            "S01-0217", "S01-0220", "S01-02D1", "S01-02M2", "S01-02M3", "S01-0301",
            "S01-0310", "S01-0311", "S01-0312", "S01-0314", "S01-0316", "S01-0320",
            "S01-0409", "S01-0411", "S01-0416", "S01-04M1", "S01-04M2", "S02-0004",
            "S02-0007", "S02-0016", "S02-0103", "S02-0205", "S02-0206", "S02-0307",
            "S02-0403", "S02-0406", "S02-04M1", "S02-0503", "S02-0507", "S02-0509",
            "S02-0511", "S02-0516", "S02-0517", "S02-0519", "S02-0522", "S02-0523",
            "S02-0603", "S02-0606", "S02-0607", "S02-0608", "S02-0612", "S02-0615",
            "S02-0619", "S02-0621", "S02-0622", "S02-06D1", "S02-06S2", "S02-06S5",
        }, scanned);
    }

    [Fact]
    public void DrawIsAtomicWhenLibraryIsTooSmall()
    {
        var player = Player(); player.Library.Add(Card("a"));
        var result = L12LibraryOps.Draw(player, 2);
        Assert.False(result.Success); Assert.Single(player.Library); Assert.Empty(player.Hand);
    }

    [Fact]
    public void DrawMovesExactlyTheRequestedTopCards()
    {
        var player = Player(); player.Library.AddRange([Card("a"), Card("b"), Card("c")]);
        var result = L12LibraryOps.Draw(player, 2);
        Assert.True(result.Success); Assert.Equal(["a", "b"], player.Hand.Select(card => card.InstanceId)); Assert.Equal("c", Assert.Single(player.Library).InstanceId);
    }

    [Fact]
    public void ViewTopDoesNotMutateTheLibrary()
    {
        var player = Player(); player.Library.AddRange([Card("a"), Card("b")]);
        var result = L12LibraryOps.ViewTop(player, 2);
        Assert.True(result.Success); Assert.Equal(2, player.Library.Count); Assert.Empty(player.Hand); Assert.Empty(player.Graveyard);
    }

    [Fact]
    public void MillIsAtomicAndMovesCardsToGraveyard()
    {
        var player = Player(); player.Library.AddRange([Card("a"), Card("b")]);
        Assert.False(L12LibraryOps.Mill(player, 3).Success); Assert.Equal(2, player.Library.Count);
        Assert.True(L12LibraryOps.Mill(player, 2).Success); Assert.Empty(player.Library); Assert.Equal(2, player.Graveyard.Count);
    }

    [Fact]
    public void SearchAndTopBottomOperationsPreserveDeclaredOrder()
    {
        var player = Player(); var a = Card("a"); var b = Card("b"); var c = Card("c");
        player.Library.AddRange([a, b, c]);
        Assert.Equal(["b"], L12LibraryOps.Search(player, card => card.InstanceId == "b").Select(card => card.InstanceId));
        Assert.True(L12LibraryOps.PutOnBottom(player, [a])); Assert.Equal(["b", "c", "a"], player.Library.Select(card => card.InstanceId));
        Assert.True(L12LibraryOps.PutOnTop(player, [a, c])); Assert.Equal(["a", "c", "b"], player.Library.Select(card => card.InstanceId));
    }

    [Fact]
    public void ReorderTopRequiresExactlyTheCurrentTopSet()
    {
        var player = Player(); player.Library.AddRange([Card("a"), Card("b"), Card("c")]);
        Assert.False(L12LibraryOps.ReorderTop(player, ["a", "c"]));
        Assert.True(L12LibraryOps.ReorderTop(player, ["b", "a"]));
        Assert.Equal(["b", "a", "c"], player.Library.Select(card => card.InstanceId));
    }

    [Fact]
    public void ImmortalSetValueIsFollowedByContinuousModifiers()
    {
        var card = Card("legion", 5000);
        L12DerivedStats.ApplyContinuousModifier(card, -2000, 4);
        L12DerivedStats.SetUntilTurnEnd(card, 1000, 4);
        Assert.Equal(-1000, card.Troops);
        L12DerivedStats.ResetForCompletedTurn(card, 4);
        Assert.Equal(3000, card.Troops);
    }

    [Fact]
    public void ConsumedContinuousBonusIsNotSubtractedAgainWhenConditionEnds()
    {
        var card = Card("continuous-layer", 5000);
        L12DerivedStats.ApplyContinuousModifier(card, 1000, 4);
        L12DerivedStats.ApplyTroopsDamage(card, 5000);

        L12DerivedStats.ApplyContinuousModifier(card, 0, 4);

        Assert.Equal(1000, card.Troops);
    }

    [Fact]
    public void ContinuousBonusConditionRemovesOnlyItsUnconsumedRemainder()
    {
        var card = Card("partial-continuous-layer", 5000);
        L12DerivedStats.ApplyContinuousModifier(card, 2000, 4);
        L12DerivedStats.ApplyTroopsDamage(card, 1000);

        L12DerivedStats.ApplyContinuousModifier(card, 0, 4);

        Assert.Equal(5000, card.Troops);
    }

    [Fact]
    public void CompletedTurnRemovesExpiredBonusAndClearsDamageItAbsorbed()
    {
        var card = Card("timed-layer", 5000);
        var modifier = new L12TimedModifier
        {
            TroopsDelta = 2000,
            ExpiresAfterTurn = 4,
            Source = "本回合兵力层",
        };
        card.TimedModifiers.Add(modifier);
        card.Troops += modifier.TroopsDelta;

        L12DerivedStats.ApplyTroopsDamage(card, 6000);

        Assert.Equal(1000, card.Troops);
        Assert.Equal(2000, modifier.ConsumedTroopsBonus);

        L12DerivedStats.ResetForCompletedTurn(card, 4);

        Assert.Equal(5000, card.Troops);
        Assert.Empty(card.TimedModifiers);
    }

    [Fact]
    public void CompletedTurnClearsDamageWhileKeepingUnexpiredAndContinuousModifiers()
    {
        var card = Card("surviving-derived-layers", 5000);
        var modifier = new L12TimedModifier
        {
            TroopsDelta = 2000,
            ExpiresAfterTurn = 5,
            Source = "跨回合兵力层",
        };
        card.TimedModifiers.Add(modifier);
        card.Troops += modifier.TroopsDelta;
        L12DerivedStats.ApplyContinuousModifiers(card,
            new Dictionary<string, int> { ["persistent-source"] = 1000 }, -1000, 4);
        L12DerivedStats.ApplyTroopsDamage(card, 2500);

        L12DerivedStats.ResetForCompletedTurn(card, 4);

        Assert.Equal(7000, card.Troops);
        Assert.Single(card.TimedModifiers);
        Assert.Equal(0, modifier.ConsumedTroopsBonus);
        Assert.Equal(1000, card.ContinuousTroopsBonusGranted);
        Assert.Equal(0, card.ContinuousTroopsBonusConsumed);
        Assert.Equal(-1000, card.ContinuousTroopsPenalty);
        Assert.Equal(0, card.ContinuousTroopsModifier);
    }

    [Fact]
    public void RepeatedContinuousRecalculationDoesNotRestoreConsumedBonus()
    {
        var card = Card("recalculated-continuous-layer", 5000);
        L12DerivedStats.ApplyContinuousModifier(card, 1000, 4);
        L12DerivedStats.ApplyTroopsDamage(card, 1500);

        L12DerivedStats.ApplyContinuousModifier(card, 1000, 4);
        L12DerivedStats.ApplyContinuousModifier(card, 1000, 4);

        Assert.Equal(4500, card.Troops);
        Assert.Equal(1000, card.ContinuousTroopsBonusConsumed);
    }

    [Fact]
    public void ContinuousBonusIsGrantedFreshOnlyAfterConditionEndsAndReturns()
    {
        var card = Card("renewed-continuous-layer", 5000);
        L12DerivedStats.ApplyContinuousModifier(card, 1000, 4);
        L12DerivedStats.ApplyTroopsDamage(card, 1000);

        L12DerivedStats.ApplyContinuousModifier(card, 0, 4);
        Assert.Equal(5000, card.Troops);

        L12DerivedStats.ApplyContinuousModifier(card, 1000, 4);
        Assert.Equal(6000, card.Troops);
        Assert.Equal(0, card.ContinuousTroopsBonusConsumed);
    }

    [Fact]
    public void ContinuousBonusAndPenaltyRemainSeparateWhenOneSourceEnds()
    {
        var card = Card("mixed-continuous-layer", 5000);
        L12DerivedStats.ApplyContinuousModifiers(card, 1000, -1000, 4);
        L12DerivedStats.ApplyTroopsDamage(card, 500);

        L12DerivedStats.ApplyContinuousModifiers(card, 0, -1000, 4);

        Assert.Equal(4000, card.Troops);
    }

    [Fact]
    public void EachContinuousSourceRemovesOnlyItsOwnUnconsumedLayer()
    {
        var card = Card("multi-source-continuous-layer", 5000);
        L12DerivedStats.ApplyContinuousModifiers(card,
            new Dictionary<string, int> { ["a"] = 1000, ["b"] = 1000 }, 0, 4);
        L12DerivedStats.ApplyTroopsDamage(card, 1500);

        L12DerivedStats.ApplyContinuousModifiers(card,
            new Dictionary<string, int> { ["a"] = 1000 }, 0, 4);

        Assert.Equal(5000, card.Troops);
        Assert.Equal(1000, card.ContinuousTroopsBonusConsumed);
    }

    [Fact]
    public void TriggerPlannerBuildsActivePlayerBatchBeforeOpponentBatch()
    {
        static L12TriggerCandidate Trigger(string id, int controller) => new()
        {
            CandidateId = id, Controller = controller, SourceInstanceId = id, SourceCardId = id,
            SourceName = id, Trigger = "death", Text = "阵亡时",
        };
        var plan = L12TriggerBatchPlanner.Plan([Trigger("enemy", 1), Trigger("mine-a", 0), Trigger("mine-b", 0)], 0);
        Assert.Equal(2, plan.Count); Assert.All(plan[0], trigger => Assert.Equal(0, trigger.Controller)); Assert.Equal(1, plan[1][0].Controller);
    }

    [Fact]
    public void MutualCombatDefeatResolvesAttackerDeathBeforeDefenderDeath()
    {
        var game = new L12GameEngine(Catalog, "trigger-integration", "TRIGGER", 8131,
            ["甲", "乙"], [0, 0], skipPreparation: true);
        static L12CardInstance Take(L12PlayerState player, string cardId)
        {
            var card = player.Hand.Concat(player.Library).First(candidate => candidate.CardId == cardId);
            player.Hand.Remove(card); player.Library.Remove(card); return card;
        }
        var attacker = Take(game.State.Players[0], "S01-0102");
        var defender = Take(game.State.Players[1], "S01-0102");
        attacker.Troops = defender.Troops = 1000;
        attacker.SummonRound = defender.SummonRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = defender;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", defender.InstanceId))).Accepted);
        for (var step = 0; step < 30 && game.State.PendingDefense is not null; step++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is null) break;
            var choice = prompt.Kind == "response" ? "pass"
                : prompt.ValidChoices.Contains("no") ? "no"
                : prompt.ValidChoices.Contains("skip") ? "skip"
                : prompt.ValidChoices[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        }

        var triggerEvents = game.State.Events.Where(entry => entry.Type == "effect-trigger").ToList();
        var attackerDeath = triggerEvents.FindIndex(entry => entry.Cards.Any(card => card.InstanceId == attacker.InstanceId));
        var defenderDeath = triggerEvents.FindIndex(entry => entry.Cards.Any(card => card.InstanceId == defender.InstanceId));
        Assert.True(attackerDeath >= 0 && defenderDeath > attackerDeath);
        Assert.Contains(attacker, game.State.Players[0].Graveyard);
        Assert.Contains(defender, game.State.Players[1].Graveyard);
    }

    [Fact]
    public void PendingActivationDeclaresAndValidatesTargetBeforePaying()
    {
        var game = new L12GameEngine(Catalog, "pending", "PENDING", 8128, ["甲", "乙"], [1, 1], skipPreparation: true);
        var player = game.State.Players[0];
        while (player.Morale.Count < 2) { var morale = player.MoraleDeck[0]; player.MoraleDeck.RemoveAt(0); player.Morale.Add(morale); }
        var target = player.Hand.Concat(player.Library).First(card => card.CardType == "legion" && card.Faction == "gaotianyuan");
        player.Hand.Remove(target); player.Library.Remove(target); player.Field[0][0] = target;
        game.State.ActivePlayer = 0; game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "S01-04M2", Ability: "frontBuff")).Accepted);
        Assert.Single(game.State.PendingActivations);
        Assert.Equal(2, player.Morale.Count(card => !card.Tapped));
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: target.InstanceId)).Accepted);
        Assert.Empty(game.State.PendingActivations);
        Assert.Equal(1, player.Morale.Count(card => !card.Tapped));
    }

    [Fact]
    public void InvalidatedPendingTargetDoesNotPayCost()
    {
        var game = new L12GameEngine(Catalog, "pending-invalid", "PENDINV", 8129, ["甲", "乙"], [1, 1], skipPreparation: true);
        var player = game.State.Players[0];
        while (player.Morale.Count < 2) { var morale = player.MoraleDeck[0]; player.MoraleDeck.RemoveAt(0); player.Morale.Add(morale); }
        var target = player.Hand.Concat(player.Library).First(card => card.CardType == "legion" && card.Faction == "gaotianyuan");
        player.Hand.Remove(target); player.Library.Remove(target); player.Field[0][0] = target;
        game.State.ActivePlayer = 0; game.State.Phase = L12Phase.Main;
        Assert.True(game.Handle(0, new L12Command("activateAbility", "S01-04M2", Ability: "frontBuff")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts); player.Field[0][0] = null;
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: target.InstanceId)).Accepted);
        Assert.Equal(2, player.Morale.Count(card => !card.Tapped));
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "type:token")]
    public void S2RuneAndGodPowerCostsAreAtomic()
    {
        var player = Player();
        L12S2ZoneOps.GainRunes(player, 2);
        Assert.False(L12S2ZoneOps.SpendRunes(player, 3)); Assert.Equal(2, player.SpecialZones.Runes);
        Assert.True(L12S2ZoneOps.SpendRunes(player, 2)); Assert.Equal(0, player.SpecialZones.Runes);
        player.Morale.AddRange([
            new L12MoraleCard { InstanceId = "power-a", CardId = "S02-05C1", IsGodPower = true },
            new L12MoraleCard { InstanceId = "power-b", CardId = "S02-05C1", IsGodPower = true },
        ]);
        Assert.False(L12S2ZoneOps.ConsumeAndFlipGodPower(player, 3));
        Assert.All(player.Morale, card => { Assert.False(card.Tapped); Assert.True(card.IsGodPower); });
        Assert.True(L12S2ZoneOps.ConsumeAndFlipGodPower(player, 2));
        Assert.All(player.Morale, card => { Assert.True(card.Tapped); Assert.False(card.IsGodPower); });

        var retainedPower = new L12MoraleCard
        {
            InstanceId = "power-c", CardId = "S02-05C1", IsGodPower = true,
        };
        player.Morale.Add(retainedPower);
        Assert.True(L12S2ZoneOps.ConsumeGodPower(player, 1));
        Assert.True(retainedPower.Tapped);
        Assert.True(retainedPower.IsGodPower);
    }

    [Fact]
    public void S2PromotionKeepsSharedStateAndAttachesFoundation()
    {
        var player = Player();
        var foundation = Card("foundation", 4000, "赫拉克勒斯", "olympus"); foundation.Tapped = true; foundation.HasStrongAttack = true;
        foundation.ImmortalUses = 1; foundation.ImmortalUntilTurn = 42; foundation.ImmortalExpiresAtPlayerTurnStart = 0;
        var promoted = Card("promoted", 6000, "赫拉克勒斯·晋升", "olympus", ["奥林匹斯", "晋升者"]);
        player.Field[0][1] = foundation; player.Hand.Add(promoted);
        player.Morale.AddRange([
            new L12MoraleCard { InstanceId = "power-a", CardId = "S02-05C1", IsGodPower = true },
            new L12MoraleCard { InstanceId = "power-b", CardId = "S02-05C1", IsGodPower = true },
        ]);
        Assert.True(L12S2ZoneOps.Promote(player, foundation, promoted, 2));
        Assert.Same(promoted, player.Field[0][1]); Assert.True(promoted.Tapped); Assert.True(promoted.HasStrongAttack);
        Assert.Equal(1, promoted.ImmortalUses); Assert.Equal(42, promoted.ImmortalUntilTurn);
        Assert.Equal(0, promoted.ImmortalExpiresAtPlayerTurnStart);
        Assert.Same(foundation, Assert.Single(promoted.AttachedCards)); Assert.Empty(player.Hand);
    }

    [Fact]
    public void MultiStepActivationDeclaresCardAndPositionBeforeReturningMorale()
    {
        var game = new L12GameEngine(Catalog, "pending-multi", "PENDMULT", 8130, ["甲", "乙"], [0, 0], skipPreparation: true);
        var player = game.State.Players[0];
        while (player.Morale.Count < 2) { var morale = player.MoraleDeck[0]; player.MoraleDeck.RemoveAt(0); player.Morale.Add(morale); }
        var xishi = Card("S01-0116", 1000, "西施");
        var summon = Card("summon", 2000, "测试军团");
        player.Field[0][0] = xishi; player.Hand.Add(summon);
        game.State.ActivePlayer = 0; game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", xishi.InstanceId, Ability: "xishiExchange")).Accepted);
        var cardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: cardPrompt.PromptId, Choice: summon.InstanceId)).Accepted);
        Assert.Equal(2, player.Morale.Count); Assert.Single(game.State.PendingActivations);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId, Choice: "0:1")).Accepted);
        Assert.Single(player.Morale); Assert.Same(summon, player.Field[0][1]); Assert.Contains(xishi, player.Graveyard);
    }

    [Fact]
    public void XiShiMayChooseNoLegionAndStillPaysCostAndDraws()
    {
        var game = new L12GameEngine(Catalog, "xishi-zero", "XISHIZERO", 8131, ["甲", "乙"], [0, 0], skipPreparation: true);
        var player = game.State.Players[0];
        while (player.Morale.Count < 1) { var morale = player.MoraleDeck[0]; player.MoraleDeck.RemoveAt(0); player.Morale.Add(morale); }
        var xishi = Card("S01-0116", 1000, "西施");
        player.Field[0][0] = xishi;
        var handBefore = player.Hand.Count;
        game.State.ActivePlayer = 0; game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", xishi.InstanceId, Ability: "xishiExchange")).Accepted);
        var cardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, cardPrompt.MinChoose);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: cardPrompt.PromptId)).Accepted);

        var zeroMorale = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: zeroMorale.PromptId,
            Choice: "mode:none")).Accepted);

        Assert.Empty(game.State.PendingActivations);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        Assert.Empty(player.Morale);
        Assert.Contains(xishi, player.Graveyard);
        Assert.Equal(handBefore + 1, player.Hand.Count);
        var drawEvent = Assert.Single(game.State.Events, entry => entry.Type == "draw");
        Assert.Contains("〈西施〉", drawEvent.Text);
        Assert.Contains("抽取 1 张牌", drawEvent.Text);
        Assert.DoesNotContain(player.Hand[^1].Name, drawEvent.Text);
    }

    [Fact]
    public void MengPoMayChooseNoEnemyAndStillReturnsMoraleAndDraws()
    {
        var game = CreateWithFirstMaster("S01-01M2", 8132);
        var player = game.State.Players[0];
        while (player.Morale.Count < 1) { var morale = player.MoraleDeck[0]; player.MoraleDeck.RemoveAt(0); player.Morale.Add(morale); }
        while (player.Hand.Count > 5) player.Hand.RemoveAt(player.Hand.Count - 1);
        var enemy = Card("enemy", 2000, "对方军团", "asgard");
        game.State.Players[1].Field[0][0] = enemy;
        var suppressBefore = enemy.SuppressDeathUntilTurn;
        var handBefore = player.Hand.Count;
        game.State.ActivePlayer = 0; game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "mengpoSilence")).Accepted);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, targetPrompt.MinChoose);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId)).Accepted);

        Assert.Empty(player.Morale);
        Assert.Equal(suppressBefore, enemy.SuppressDeathUntilTurn);
        Assert.Equal(handBefore + 1, player.Hand.Count);
    }
}
