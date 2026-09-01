using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RulingClosureRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 69001, int firstDeck = 0)
    {
        var game = new L12GameEngine(Catalog, "ruling-closure", "RULING", seed,
            ["甲", "乙"], [firstDeck, firstDeck], skipPreparation: true,
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
            player.Resolving.Clear();
            player.Morale.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int? troops = null, int? owner = null)
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
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }

    private static void AddReadyMorale(L12PlayerState player, int count)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"ruling-morale-{player.PlayerIndex}-{index}",
                CardId = "S01-01C1",
                Tapped = false,
            });
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

    private static object? InvokePrivate(object target, string method, params object?[] args)
        => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, args);

    [Fact]
    public void HoremhebSubstituteReceivesTheActualLethalDestinationWithOwnerAndDeathEvents()
    {
        var game = Create();
        var controller = game.State.Players[0];
        var owner = game.State.Players[1];
        var horemheb = Card("S01-0205", "ruling-horemheb", troops: 4000, owner: 0);
        var guard = Card("S01-0212", "ruling-controlled-guard", owner: 1);
        var killer = Card("S01-0105", "ruling-horemheb-killer", owner: 1);
        horemheb.Tapped = true;
        controller.Field[0][0] = horemheb;
        controller.Field[1][0] = guard;
        owner.Field[0][0] = killer;

        var killItem = new L12StackItem
        {
            StackItemId = "ruling-horemheb-kill-stack",
            Controller = 1,
            SourceInstanceId = killer.InstanceId,
            SourceCardId = killer.CardId,
            SourceName = killer.Name,
            Trigger = "active",
            Text = "测试致命效果",
            SourceSnapshot = killer,
        };
        _ = InvokePrivate(game, "KillTarget", killItem, horemheb.InstanceId, "被测试致命效果击杀");
        var replacement = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "effect-lethal-replacement");
        Assert.Contains(guard.InstanceId, replacement.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: replacement.PromptId,
            Choice: guard.InstanceId)).Accepted);

        Assert.Same(horemheb, controller.Field[0][0]);
        Assert.Equal(4000, horemheb.Troops);
        Assert.True(horemheb.Tapped);
        Assert.Null(controller.Field[1][0]);
        Assert.DoesNotContain(guard, controller.Graveyard);
        Assert.Contains(guard, owner.Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Type == "leave"
            && entry.Cards.Any(card => card.InstanceId == guard.InstanceId));
        Assert.Contains(game.State.Events, entry => entry.Type == "replacement"
            && entry.Cards.Any(card => card.InstanceId == guard.InstanceId));
        Assert.Contains(game.State.Events, entry => entry.Type == "kill-source"
            && entry.Cards.Any(card => card.InstanceId == killer.InstanceId));
    }

    [Fact]
    public void HelenSubstituteIsRevalidatedAndInvalidSelectionDoesNotProtectHer()
    {
        var game = Create(firstDeck: 4);
        var player = game.State.Players[0];
        var helen = Card("S02-0515", "ruling-helen", owner: 0);
        var substitute = Card("S02-0502", "ruling-helen-substitute", owner: 0);
        player.Field[0][0] = helen;
        player.Hand.Add(substitute);

        _ = InvokePrivate(game, "RemoveFromField", player, helen, true, "被测试致命效果击杀",
            true, L12FieldLeaveKind.Defeat, false, false);
        var replacement = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "effect-lethal-replacement");
        Assert.Contains(substitute.InstanceId, replacement.ValidChoices);
        player.Hand.Remove(substitute);
        player.Library.Add(substitute);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: replacement.PromptId,
            Choice: substitute.InstanceId)).Accepted);

        Assert.Null(player.Field[0][0]);
        Assert.Contains(helen, player.Graveyard);
        Assert.Contains(substitute, player.Library);
        Assert.DoesNotContain(substitute, player.Graveyard);
    }

    [Fact]
    public void HelenUsesARealEffectDiscardRatherThanAFieldDeathTransaction()
    {
        var game = Create(firstDeck: 4);
        var player = game.State.Players[0];
        var helen = Card("S02-0515", "ruling-helen-discard", owner: 0);
        var substitute = Card("S02-0006", "ruling-helen-faith", owner: 0);
        player.Field[0][0] = helen;
        player.Hand.Add(substitute);

        _ = InvokePrivate(game, "RemoveFromField", player, helen, true, "被测试致命效果击杀",
            true, L12FieldLeaveKind.Defeat, false, false);
        var replacement = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "effect-lethal-replacement");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: replacement.PromptId,
            Choice: substitute.InstanceId)).Accepted);

        Assert.Same(helen, player.Field[0][0]);
        Assert.Contains(substitute, player.Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Type == "discard"
            && entry.Cards.Any(card => card.InstanceId == substitute.InstanceId));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "leave"
            && entry.Cards.Any(card => card.InstanceId == substitute.InstanceId));
        Assert.Contains(player.UsedAbilities,
            key => key == $"trigger:faith-zealot:{substitute.InstanceId}");
    }

    [Fact]
    public void HoremhebCombatSubstituteReceivesTheCombatDeathAndOriginalCardKeepsItsState()
    {
        var game = Create();
        var attackerPlayer = game.State.Players[0];
        var defender = game.State.Players[1];
        var attacker = Card("S01-0105", "ruling-horemheb-attacker", troops: 6000, owner: 0);
        var horemheb = Card("S01-0205", "ruling-horemheb-combat", troops: 4000, owner: 1);
        var guard = Card("S01-0212", "ruling-horemheb-combat-guard", owner: 1);
        attacker.SummonRound = 0;
        horemheb.SummonRound = 0;
        horemheb.Tapped = false;
        attackerPlayer.Field[0][0] = attacker;
        defender.Field[0][0] = horemheb;
        defender.Field[1][0] = guard;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", horemheb.InstanceId))).Accepted);
        PassResponses(game);
        Assert.True(game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: [])).Accepted);
        PassResponses(game);
        var replacement = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "combat-lethal-replacement");
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: replacement.PromptId,
            Choice: guard.InstanceId)).Accepted);
        PassResponses(game);

        Assert.Same(horemheb, defender.Field[0][0]);
        Assert.Equal(4000, horemheb.Troops);
        Assert.False(horemheb.Tapped);
        Assert.Contains(guard, defender.Graveyard);
        Assert.DoesNotContain(horemheb, defender.Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Type == "leave"
            && entry.Cards.Any(card => card.InstanceId == guard.InstanceId));
    }

    [Fact]
    public void PtolemyRepeatsColonEffectWithoutTheOriginalCostOrVirtualCardMovement()
    {
        var game = Create();
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var ptolemy = Card("S01-0211", "ruling-ptolemy");
        player.Hand.Add(ptolemy);
        AddReadyMorale(player, ptolemy.Cost);
        player.LastActiveTacticCardId = "S01-0006";
        var hpBefore = opponent.Hp;

        Assert.True(game.Handle(0, new L12Command("playCard", ptolemy.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        PassResponses(game);

        Assert.True(opponent.Hp == hpBefore - 1,
            string.Join("\n", game.State.Events.Select(entry => $"{entry.Type}:{entry.Text}")));
        Assert.Empty(player.Hand);
        Assert.DoesNotContain(player.Graveyard, card => card.CardId == "S01-0006");
        Assert.Equal(ptolemy.Cost, player.Morale.Count(card => card.Tapped));
        Assert.Contains(game.State.Events, entry => entry.Type == "stack-push"
            && entry.Text.Contains("再次发动", StringComparison.Ordinal));
    }

    [Fact]
    public void PtolemyDeclaresRepeatedModesAndTargetsBeforeTheRepeatedEffectStackItem()
    {
        var game = Create();
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var ptolemy = Card("S01-0211", "ruling-ptolemy-declaration");
        opponent.Field[0][0] = Card("S01-0105", "ruling-volley-front");
        opponent.Field[1][0] = Card("S01-0106", "ruling-volley-back");
        player.Hand.Add(ptolemy);
        AddReadyMorale(player, ptolemy.Cost);
        player.LastActiveTacticCardId = "S01-0005";

        Assert.True(game.Handle(0, new L12Command("playCard", ptolemy.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);

        var declaration = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.Contains("mode:front", declaration.ValidChoices);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceCardId == "S01-0005");
    }

    [Fact]
    public void PtolemyCancelsWhenARepeatedEffectsDeclaredTargetBecomesInvalid()
    {
        var game = Create();
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var ptolemy = Card("S01-0211", "ruling-ptolemy-invalid-target");
        var target = Card("S01-0105", "ruling-ptolemy-stale-target");
        opponent.Field[0][0] = target;
        player.Hand.Add(ptolemy);
        AddReadyMorale(player, ptolemy.Cost);
        player.LastActiveTacticCardId = "S02-0622";

        Assert.True(game.Handle(0, new L12Command("playCard", ptolemy.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        var declaration = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        opponent.Field[0][0] = null;
        opponent.Graveyard.Add(target);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            Choice: target.InstanceId)).Accepted);

        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceCardId == "S02-0622");
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("目标失效", StringComparison.Ordinal));
    }

    [Fact]
    public void PtolemyDeclaresAColonCostDependentCountWithoutPayingItAgain()
    {
        var game = Create(firstDeck: 2);
        var player = game.State.Players[0];
        var ptolemy = Card("S01-0211", "ruling-ptolemy-desert");
        var summonDefinition = Catalog.Cards.Values.First(card => card.CardType == "legion"
            && card.Faction == "taiyangcheng" && card.DisasterLevel == 2);
        var summon = Card(summonDefinition.Id, "ruling-ptolemy-desert-summon");
        player.Hand.AddRange([ptolemy, summon]);
        AddReadyMorale(player, ptolemy.Cost);
        player.LastActiveTacticCardId = "S02-0207";

        Assert.True(game.Handle(0, new L12Command("playCard", ptolemy.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        var count = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.Contains("count:2", count.ValidChoices);
        var fieldIds = player.Field.SelectMany(row => row).Where(card => card is not null)
            .Select(card => card!.InstanceId).ToHashSet();
        Assert.DoesNotContain(count.ValidChoices, fieldIds.Contains);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: count.PromptId,
            Choice: "count:2")).Accepted);
        var hand = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(summon.InstanceId, hand.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: hand.PromptId,
            Choice: summon.InstanceId)).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: "0:1")).Accepted);
        PassResponses(game);

        Assert.Same(summon, player.Field[0][1]);
        Assert.Same(ptolemy, player.Field[0][0]);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "cost"
            && entry.Text.Contains("沙漠君临", StringComparison.Ordinal));
    }

    [Fact]
    public void FaithZealotMasterChoiceAppearsOnlyAfterZealotLeavesTheStack()
    {
        var game = Create(firstDeck: 0);
        var player = game.State.Players[0];
        var zealot = Card("S02-0006", "ruling-faith-zealot");
        player.Graveyard.Add(zealot);
        InvokePrivate(game, "NotifyCardDiscarded", player, zealot, "library", true);
        PassResponses(game);

        var choice = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-faith-zealot");
        Assert.DoesNotContain(game.State.EffectStack,
            item => item.SourceInstanceId == zealot.InstanceId);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: choice.PromptId,
            Choice: "drawCycle")).Accepted);
        Assert.DoesNotContain(game.State.EffectStack,
            item => item.SourceInstanceId == zealot.InstanceId);
        var generated = Assert.Single(game.State.EffectStack,
            item => item.SourceCardId == player.MasterId
                && item.Data.GetValueOrDefault("freeMasterActivation") == "true");
        Assert.Contains(game.State.PendingPrompts,
            prompt => prompt.Kind == "response" && prompt.StackItemId == generated.StackItemId);
        Assert.Empty(player.Morale);
        Assert.DoesNotContain(player.UsedAbilities,
            key => key == $"active:master-0:drawCycle");
    }

    [Fact]
    public void NegatedLiJingRevealDoesNotReadOrProcessTheHiddenTopCard()
    {
        var game = Create();
        var player = game.State.Players[0];
        var liJing = Card("S01-0103", "ruling-li-jing");
        var hiddenTop = Card("S01-0105", "ruling-hidden-top");
        player.Hand.Add(liJing);
        player.Library.Add(hiddenTop);
        AddReadyMorale(player, liJing.Cost);

        Assert.True(game.Handle(0, new L12Command("playCard", liJing.InstanceId, Row: 0, Slot: 0)).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            Choice: "mode:use")).Accepted);
        var liJingItem = Assert.Single(game.State.EffectStack, item => item.SourceInstanceId == liJing.InstanceId);
        liJingItem.Negated = true;
        PassResponses(game);

        Assert.Same(hiddenTop, player.Library[0]);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "reveal"
            && entry.Cards.Any(card => card.InstanceId == hiddenTop.InstanceId));
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") is "lijing-choice" or "lijing-slot");
    }

    [Fact]
    public void LiJingRevealAndDependentChoiceStayInsideOneUninterruptedStackItem()
    {
        var game = Create();
        var player = game.State.Players[0];
        var liJing = Card("S01-0103", "ruling-li-jing-transaction");
        var hiddenTop = Card("S01-0105", "ruling-li-jing-transaction-top");
        player.Hand.Add(liJing);
        player.Library.Add(hiddenTop);
        AddReadyMorale(player, liJing.Cost);

        Assert.True(game.Handle(0, new L12Command("playCard", liJing.InstanceId, Row: 0, Slot: 0)).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            Choice: "mode:use")).Accepted);
        PassResponses(game);

        var dependent = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "lijing-choice");
        Assert.Equal(game.State.EffectStack.Single().StackItemId, dependent.StackItemId);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Contains(game.State.Events, entry => entry.Type == "reveal"
            && entry.Cards.Any(card => card.InstanceId == hiddenTop.InstanceId));
    }

    [Fact]
    public void ThunderWrathTieProcessesActivePlayerThenOtherPlayerAndRecordsTheSequence()
    {
        var tied = Create();
        tied.State.ActivePlayer = 1;
        tied.State.Players[0].Field[0][0] = Card("S01-0105", "ruling-thunder-0");
        tied.State.Players[1].Field[0][0] = Card("S01-0201", "ruling-thunder-1");
        var disaster = Card("S01-DS04", "ruling-thunder");
        var item = new L12StackItem
        {
            StackItemId = "ruling-thunder-stack",
            Controller = 1,
            SourceInstanceId = disaster.InstanceId,
            SourceCardId = disaster.CardId,
            SourceName = disaster.Name,
            Trigger = "disaster",
            Text = "天灾触发效果",
            SourceSnapshot = disaster,
        };
        tied.State.EffectStack.Add(item);
        tied.State.IsResolvingStack = true;
        InvokePrivate(tied, "ResolveThunderWrathRolls", item, 4, 4);

        var first = Assert.Single(tied.State.PendingPrompts);
        Assert.Equal(1, first.PlayerIndex);
        var firstCard = tied.State.Players[1].Field.SelectMany(row => row).Single(card => card is not null)!;
        Assert.True(tied.Handle(1, new L12Command("resolvePrompt", PromptId: first.PromptId,
            Choice: firstCard.InstanceId)).Accepted);
        var second = Assert.Single(tied.State.PendingPrompts);
        Assert.Equal(0, second.PlayerIndex);
        var secondCard = tied.State.Players[0].Field.SelectMany(row => row).Single(card => card is not null)!;
        Assert.True(tied.Handle(0, new L12Command("resolvePrompt", PromptId: second.PromptId,
            Choice: secondCard.InstanceId)).Accepted);

        Assert.Contains(firstCard, tied.State.Players[1].Hand);
        Assert.Contains(secondCard, tied.State.Players[0].Hand);
        Assert.Contains(tied.State.Events, entry => entry.Type == "dice"
            && entry.Text.Contains("当前回合玩家", StringComparison.Ordinal));
    }

    [Fact]
    public void ThunderWrathTieSkipsAPlayerWithoutLegionsAndStillProcessesTheOther()
    {
        var tied = Create();
        tied.State.ActivePlayer = 0;
        tied.State.Players[1].Field[0][0] = Card("S01-0201", "ruling-thunder-skip");
        var disaster = Card("S01-DS04", "ruling-thunder-skip-disaster");
        var item = new L12StackItem
        {
            StackItemId = "ruling-thunder-skip-stack",
            Controller = 0,
            SourceInstanceId = disaster.InstanceId,
            SourceCardId = disaster.CardId,
            SourceName = disaster.Name,
            Trigger = "disaster",
            Text = "天灾触发效果",
            SourceSnapshot = disaster,
        };
        tied.State.EffectStack.Add(item);
        tied.State.IsResolvingStack = true;
        InvokePrivate(tied, "ResolveThunderWrathRolls", item, 2, 2);

        var onlyPrompt = Assert.Single(tied.State.PendingPrompts);
        Assert.Equal(1, onlyPrompt.PlayerIndex);
        Assert.Contains(tied.State.Events, entry => entry.Type == "effect-skip"
            && entry.Text.Contains("没有军团", StringComparison.Ordinal));
    }
}
