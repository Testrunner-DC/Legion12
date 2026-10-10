using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TriggerResourceOrderingRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int controller, string master = "S02-04M1")
    {
        var original = Catalog.DeckAt(0);
        var custom = new L12PresetDeckDefinition
        {
            Name = "Trigger resource ordering", MasterId = master,
            CardIds = [.. original.CardIds], MoraleIds = [.. original.MoraleIds],
            SpecialIds = [.. original.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "trigger-resource-order", "TRIGGERORDER", 90301,
            ["甲", "乙"], controller == 0 ? [custom, original] : [original, custom],
            skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = controller;
        game.State.FirstPlayer = controller;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear();
            player.Resolving.Clear(); player.Morale.Clear(); player.UsedAbilities.Clear();
            player.SpecialZones.Trials.Clear(); player.SpecialZones.Runes = 0;
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = cardId, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction,
            ImageUrl = definition.ImageUrl, Cost = definition.Cost ?? 0,
            EffectText = definition.Effect, Traits = [.. definition.Traits],
            Profession = definition.Profession, BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0, SummonRound = -1, OwnerIndex = owner,
        };
    }

    private static L12Prompt Choose(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(choice, prompt.ValidChoices);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var count = 0; count < 30 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; count++)
            Choose(game, "pass");
        Assert.NotEqual("response", game.State.PendingPrompts.FirstOrDefault()?.Kind);
    }

    private static void Invoke(L12GameEngine game, string method, params object?[] arguments)
        => typeof(L12GameEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, arguments);

    private static L12GameEngine MoveLastResource(int controller, int targetController)
    {
        var game = Create(controller);
        var player = game.State.Players[controller];
        var moved = Card("S02-0401", "moved", controller);
        player.Field[0][0] = moved;
        game.State.Players[targetController].Field[0][2] = Card("S02-0402", "target", targetController);
        player.Morale.Add(new L12MoraleCard { CardId = "S02-04C1", InstanceId = "last-resource" });
        var result = game.Handle(controller, new L12Command("move", moved.InstanceId, Row: 1, Slot: 0));
        Assert.True(result.Accepted, result.Error);
        Assert.True(Assert.Single(player.Morale).Tapped);
        Assert.Same(moved, player.Field[1][0]);
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-order", order.Kind);
        Assert.Equal(controller, order.PlayerIndex);
        Assert.Equal(2, order.ValidChoices.Count);
        Assert.Equal(["friendly-front-to-back", "friendly-legion-moves"],
            order.ValidChoices.Select(id => order.Data[$"trigger:{id}"]).OrderBy(value => value));
        return game;
    }

    private static void Order(L12GameEngine game, bool readyFirst)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var ready = Assert.Single(prompt.ValidChoices, id => prompt.Data[$"trigger:{id}"] == "friendly-front-to-back");
        var follow = Assert.Single(prompt.ValidChoices, id => prompt.Data[$"trigger:{id}"] == "friendly-legion-moves");
        // Existing contract: activation order is the reverse of resolution order.
        var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: readyFirst ? [follow, ready] : [ready, follow]);
        var result = game.Handle(prompt.PlayerIndex, command);
        Assert.True(result.Accepted, result.Error);
        Assert.False(game.Handle(prompt.PlayerIndex, command).Accepted);
    }

    private static void Ready(L12GameEngine game)
    {
        Choose(game, "last-resource");
        PassResponses(game);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, true)]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, false)]
    public void LastResourceMovementAllowsReadyBeforePaidFollowForEitherControllerAndTarget(
        int controller, int targetController, bool restore)
    {
        var game = MoveLastResource(controller, targetController);
        Order(game, readyFirst: true);
        if (restore)
            game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
                game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Ready(game);
        var player = game.State.Players[controller];
        Assert.False(Assert.Single(player.Morale).Tapped);
        Choose(game, "mode:use");
        Choose(game, "target");
        Assert.Equal(targetController.ToString(), Assert.Single(game.State.PendingPrompts).Data["targetPlayerIndex"]);
        var slot = Choose(game, "0:1");
        Assert.False(game.Handle(controller, new L12Command("resolvePrompt", PromptId: slot.PromptId, Choice: "0:1")).Accepted);
        PassResponses(game);
        Assert.True(Assert.Single(player.Morale).Tapped);
        var target = game.State.Players[targetController].Field[0][1];
        Assert.Equal("target", target?.InstanceId);
        Assert.Equal(-1, target!.CostModifier);
        Assert.Null(game.State.Players[targetController].Field[0][2]);
        Assert.Contains($"active:master-{controller}:tsukuyomiFollowMove", player.UsedAbilities);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingTriggerBatches);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void InsufficientFollowFirstDoesNotConsumeOrSwallowTheReadyTrigger(int controller)
    {
        var game = MoveLastResource(controller, controller);
        Order(game, readyFirst: false);
        Ready(game);
        Assert.False(Assert.Single(game.State.Players[controller].Morale).Tapped);
        Assert.Equal("target", game.State.Players[controller].Field[0][2]?.InstanceId);
        Assert.DoesNotContain($"active:master-{controller}:tsukuyomiFollowMove", game.State.Players[controller].UsedAbilities);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingTriggerBatches);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DecliningFollowAfterReadyPreservesResourceAndOnceUsage(int controller)
    {
        var game = MoveLastResource(controller, controller);
        Order(game, readyFirst: true);
        Ready(game);
        var decision = Choose(game, "mode:none");
        Assert.False(game.Handle(controller, new L12Command("resolvePrompt", PromptId: decision.PromptId, Choice: "mode:use")).Accepted);
        Assert.False(Assert.Single(game.State.Players[controller].Morale).Tapped);
        Assert.DoesNotContain($"active:master-{controller}:tsukuyomiFollowMove", game.State.Players[controller].UsedAbilities);
        Assert.Equal("target", game.State.Players[controller].Field[0][2]?.InstanceId);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TargetRemovedOrHiddenDuringEarlierResponseIsRecheckedBeforeFollow(bool hidden)
    {
        var game = MoveLastResource(0, 1);
        Order(game, readyFirst: true);
        Choose(game, "last-resource");
        Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
        var opponent = game.State.Players[1];
        if (hidden) opponent.Field[0][2]!.Hidden = true;
        else opponent.Field[0][2] = null;
        PassResponses(game);
        Assert.False(Assert.Single(game.State.Players[0].Morale).Tapped);
        Assert.DoesNotContain("active:master-0:tsukuyomiFollowMove", game.State.Players[0].UsedAbilities);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Null(opponent.Field[0][1]);
    }

    [Fact]
    public void CancellingFollowPaymentAfterReadyDoesNotChargeOrConsumeOnce()
    {
        var game = MoveLastResource(0, 1);
        Order(game, readyFirst: true);
        Choose(game, "last-resource");
        // A response may add a non-equivalent payment option before the next declaration.
        game.State.Players[0].Morale.Add(new L12MoraleCard { CardId = "S02-03C1", InstanceId = "new-resource" });
        PassResponses(game);
        Choose(game, "mode:use");
        Assert.Equal("resource-payment", Assert.Single(game.State.PendingPrompts).Kind);
        Choose(game, "skip");
        Assert.All(game.State.Players[0].Morale, card => Assert.False(card.Tapped));
        Assert.DoesNotContain("active:master-0:tsukuyomiFollowMove", game.State.Players[0].UsedAbilities);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal("target", game.State.Players[1].Field[0][2]?.InstanceId);
    }

    [Fact]
    public void ReadyTargetBecomingActiveBeforeDeclarationSkipsOnlyReadyAndStillOffersFollow()
    {
        var game = MoveLastResource(0, 1);
        // A preceding resolving effect can make the mandatory ready target unavailable.
        Assert.Single(game.State.Players[0].Morale).Tapped = false;
        Order(game, readyFirst: true);
        Assert.Contains("mode:use", Assert.Single(game.State.PendingPrompts).ValidChoices);
        Choose(game, "mode:none");
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    public void AQueuedSingleTriggerWaitsForActualDeclarationToReadResources()
    {
        var game = Create(0, "S02-06M1");
        var player = game.State.Players[0];
        var lancelot = Card("S02-0602", "delayed-lancelot", 0);
        player.Field[0][0] = lancelot;
        game.State.IsResolvingStack = true;
        Invoke(game, "QueueOrPushTriggeredEffect", 0, lancelot, "enter", "兰斯洛特登场", null, null);
        Assert.Single(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingPrompts);
        player.SpecialZones.Runes = 1;
        game.State.IsResolvingStack = false;
        Invoke(game, "AdvanceTriggerBatches");
        Choose(game, "mode:use");
        Assert.Equal(0, player.SpecialZones.Runes);
        PassResponses(game);
        Assert.True(lancelot.HasCharge);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SingleScathachTriggerIsNotPreparedAndDiscardedWhilePreviousEffectStillResolves(int controller)
    {
        var game = Create(controller, "S02-06M1");
        var player = game.State.Players[controller];
        var scathach = Card("S02-0612", "delayed-scathach", controller);
        player.Field[0][0] = scathach;
        game.State.PendingDefense = new L12PendingDefense
        {
            AttackerPlayer = controller, AttackerInstanceId = scathach.InstanceId,
            Target = new L12AttackTarget("master"), StageEffectsQueued = true,
        };
        game.State.IsResolvingStack = true;
        Invoke(game, "QueueOrPushTriggeredEffect", controller, scathach, "attack", "斯卡哈进攻时", null, null);
        Assert.Single(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingPrompts);
        player.SpecialZones.Runes = 1;
        game.State.IsResolvingStack = false;
        Invoke(game, "AdvanceTriggerBatches");
        Choose(game, "mode:use");
        Assert.Equal(0, player.SpecialZones.Runes);
        PassResponses(game);
        Assert.Equal(scathach.BaseTroops + 2000, scathach.Troops);
        Assert.True(scathach.AttackNoLossUntilTurn >= game.State.TurnSerial);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void KagutsuchiCanPayWithCardDrawnByEarlierSimultaneousAttackTrigger(int controller)
    {
        var game = Create(controller, "ST04-M1");
        var player = game.State.Players[controller];
        var hawk = Card("ST06-05", "drawing-attacker", controller);
        player.Field[0][0] = hawk;
        player.Library.Add(Card("ST01-02", "drawn-payment", controller));
        var result = game.Handle(controller, new L12Command("attack", hawk.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(result.Accepted, result.Error);
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-order", order.Kind);
        Assert.Equal(2, order.ValidChoices.Count);
        var master = Assert.Single(order.ValidChoices, id => order.Data[$"sourceInstance:{id}"] == $"master-{controller}");
        var draw = Assert.Single(order.ValidChoices, id => id != master);
        Assert.True(game.Handle(controller, new L12Command("resolvePrompt", PromptId: order.PromptId,
            CardInstanceIds: [master, draw])).Accepted);
        Choose(game, "mode:use");
        PassResponses(game);
        Assert.Equal("drawn-payment", Assert.Single(player.Hand).InstanceId);
        Choose(game, "mode:discard");
        Choose(game, "drawn-payment");
        Assert.Empty(player.Hand);
        PassResponses(game);
        Assert.Equal(hawk.BaseTroops + 2000, hawk.Troops);
        Assert.Contains(player.Graveyard, card => card.InstanceId == "drawn-payment");
    }

    [Theory]
    [InlineData(0, "morale")]
    [InlineData(1, "morale")]
    [InlineData(0, "hand")]
    [InlineData(1, "hand")]
    [InlineData(0, "slot")]
    [InlineData(1, "slot")]
    public void HiddenPassCollectsWithItsPostAttackGroupAndReadsLaterMoraleHandOrSlot(int controller, string unavailable)
    {
        var game = Create(controller, "ST01-M1");
        game.State.ActivePlayer = 1 - controller;
        var player = game.State.Players[controller];
        var hiddenPass = Card("ST01-10", "hidden-pass", controller);
        hiddenPass.Hidden = true;
        player.Field[1][2] = hiddenPass;
        player.Hand.Add(Card("S01-0213", "kaba", controller));
        var entrant = Card("ST01-05", "entrant", controller);
        var morale = new L12MoraleCard { CardId = "ST01-C1", InstanceId = "return-cost" };
        if (unavailable != "morale") player.Morale.Add(morale);
        if (unavailable != "hand") player.Hand.Add(entrant);
        if (unavailable == "slot")
            for (var row = 0; row < 2; row++)
            for (var slot = 0; slot < 3; slot++)
                player.Field[row][slot] ??= Card("ST01-04", $"occupant-{row}-{slot}", controller);

        // The post-attack timing is real. The parent is still resolving, so declaration must wait.
        game.State.IsResolvingStack = true;
        Invoke(game, "QueueS1PostAttackReactions", 1 - controller);
        var batch = Assert.Single(game.State.PendingTriggerBatches);
        Assert.Equal(2, batch.Candidates.Count);
        Assert.Contains(batch.Candidates, candidate => candidate.SourceInstanceId == hiddenPass.InstanceId);
        Assert.True(hiddenPass.Hidden);
        Assert.Empty(game.State.PendingPrompts);

        if (unavailable == "morale") player.Morale.Add(morale);
        if (unavailable == "hand") player.Hand.Add(entrant);
        if (unavailable == "slot") player.Field[0][0] = null;
        game.State.IsResolvingStack = false;
        Invoke(game, "AdvanceTriggerBatches");
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-order", order.Kind);
        var hidden = Assert.Single(order.ValidChoices, id => order.Data[$"sourceInstance:{id}"] == "hidden-pass");
        var kaba = Assert.Single(order.ValidChoices, id => order.Data[$"sourceInstance:{id}"] == "kaba");
        Assert.True(game.Handle(controller, new L12Command("resolvePrompt", PromptId: order.PromptId,
            CardInstanceIds: [hidden, kaba])).Accepted);
        Choose(game, "mode:none");
        Choose(game, "mode:use");
        Choose(game, "return-cost");
        Choose(game, "entrant");
        Choose(game, "0:0");
        Assert.Empty(player.Morale);
        PassResponses(game);
        Assert.Equal("entrant", player.Field[0][0]?.InstanceId);
        Assert.DoesNotContain(player.Hand, card => card.InstanceId == "entrant");
    }
}
