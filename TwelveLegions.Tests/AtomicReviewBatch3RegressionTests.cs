using TwelveLegions.Server;
using System.Text.Json;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch3RegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
        => new(Catalog, "atomic-review-batch3", "ATOMIC3", seed, ["甲", "乙"], [0, 0], skipPreparation: true);

    private static L12GameEngine CreateWithFirstMaster(string masterId, int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = $"{masterId}第三批原子审查牌库",
            MasterId = masterId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        return new L12GameEngine(Catalog, "atomic-review-batch3", "ATOMIC3", seed,
            ["甲", "乙"], [deck, baseDeck], skipPreparation: true);
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
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
        };
    }

    private static void AddMorale(L12PlayerState player, int count, string cardId = "S01-01C1", bool tapped = false)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"atomic3-morale-{player.PlayerIndex}-{player.Morale.Count}",
                CardId = cardId,
                Tapped = tapped,
            });
    }

    private static void PrepareMain(L12GameEngine game, int activePlayer = 0)
    {
        game.State.ActivePlayer = activePlayer;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
    }

    private static void HoldOpponentResponseWindow(L12GameEngine game, int actingPlayer = 0)
    {
        var opponent = game.State.Players[1 - actingPlayer];
        var counter = Card("S01-0019", $"atomic3-response-{actingPlayer}-{game.State.StackSequence}");
        counter.Hidden = true;
        counter.SetRound = 0;
        opponent.Field[1][2] = counter;
        opponent.Field[0][2] ??= Card("S01-0004", $"atomic3-response-target-{actingPlayer}-{game.State.StackSequence}");
    }

    private static L12Prompt ResolveSinglePrompt(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 50 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    [Fact]
    [Trait("L12Evidence", "ability:asgardDraw")]
    public void AsgardDrawDeclaresExtraHealBeforePayingAnyMorale()
    {
        var game = CreateWithFirstMaster("S01-03M2", 6901);
        var player = game.State.Players[0];
        player.Morale.Clear();
        player.Library.Clear();
        AddMorale(player, 3, "S01-03C1");
        var drawn = Card("S01-0301", "asgard-declared-draw");
        player.Library.Add(drawn);
        player.Hp = 5;
        HoldOpponentResponseWindow(game);
        PrepareMain(game);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "faction-0", Ability: "asgardDraw")).Accepted);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:heal", mode.ValidChoices);
        Assert.Empty(game.State.EffectStack);
        Assert.Equal(3, player.Morale.Count(card => !card.Tapped));
        Assert.DoesNotContain(drawn, player.Hand);

        ResolveSinglePrompt(game, "mode:heal");
        Assert.Equal(0, player.Morale.Count(card => !card.Tapped));
        Assert.Single(game.State.EffectStack);
        PassResponses(game);

        Assert.Contains(drawn, player.Hand);
        Assert.Equal(6, player.Hp);
    }

    [Fact]
    [Trait("L12Evidence", "ability:factionDrawMove")]
    public void GaotianyuanDrawMoveDeclaresModeLegionAndSlotBeforePayment()
    {
        var game = CreateWithFirstMaster("S01-04M2", 6902);
        var player = game.State.Players[0];
        player.Morale.Clear();
        player.Library.Clear();
        AddMorale(player, 2, "S01-04C1");
        var drawn = Card("S01-0401", "gaotianyuan-declared-draw");
        var mover = Card("S01-0402", "gaotianyuan-declared-mover");
        player.Library.Add(drawn);
        player.Field[0][0] = mover;
        HoldOpponentResponseWindow(game);
        PrepareMain(game);

        var activation = game.Handle(0, new L12Command("activateAbility", "faction-0", Ability: "factionDrawMove"));
        Assert.True(activation.Accepted, activation.Error);
        ResolveSinglePrompt(game, "mode:move");
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(mover.InstanceId, target.ValidChoices);
        Assert.Equal(2, player.Morale.Count(card => !card.Tapped));
        Assert.DoesNotContain(drawn, player.Hand);
        ResolveSinglePrompt(game, mover.InstanceId);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("0:1", slot.ValidChoices);
        Assert.Equal(2, player.Morale.Count(card => !card.Tapped));
        ResolveSinglePrompt(game, "0:1");

        Assert.Equal(0, player.Morale.Count(card => !card.Tapped));
        player.Field[0][0] = null;
        player.Graveyard.Add(mover);
        PassResponses(game);

        Assert.Contains(drawn, player.Hand);
        Assert.Contains(mover, player.Graveyard);
        Assert.Null(player.Field[0][1]);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("位移", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:sunTopThree")]
    public void SunTopThreeKeepsHiddenPickDelayedButPredeclaresPublicGraveRecovery()
    {
        var game = CreateWithFirstMaster("S01-02D1", 6903);
        var player = game.State.Players[0];
        player.Morale.Clear();
        player.Hand.Clear();
        player.Library.Clear();
        player.Graveyard.Clear();
        AddMorale(player, 2, "S01-02C1");
        var hiddenTop = Card("S01-0201", "sun-hidden-top");
        var recover = Card("S01-0202", "sun-declared-recover");
        player.Library.Add(hiddenTop);
        player.Graveyard.Add(recover);
        PrepareMain(game);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "sunTopThree")).Accepted);
        var graveTarget = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(recover.InstanceId, graveTarget.ValidChoices);
        Assert.DoesNotContain(hiddenTop.InstanceId, graveTarget.ValidChoices);
        Assert.Equal(2, player.Morale.Count(card => !card.Tapped));
        ResolveSinglePrompt(game, recover.InstanceId);
        Assert.Equal(0, player.Morale.Count(card => !card.Tapped));

        PassResponses(game);
        var hiddenPick = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("faction-search-pick", hiddenPick.Data["action"]);
        Assert.Contains(hiddenTop.InstanceId, hiddenPick.ValidChoices);
        ResolveSinglePrompt(game, hiddenTop.InstanceId);

        Assert.Contains(hiddenTop, player.Hand);
        Assert.Contains(recover, player.Hand);
        Assert.Contains(game.State.AuthorityEvents, entry => entry.Type == "effect-hand-add"
            && entry.TargetInstanceId == recover.InstanceId && entry.OriginZone == "graveyard");
    }

    [Fact]
    [Trait("L12Evidence", "ability:isisCanopic")]
    public void IsisDeclaresChosenGuardsCanopicAndRewardBeforeDiscardingExactlyThree()
    {
        var game = CreateWithFirstMaster("S01-02M1", 6904);
        var player = game.State.Players[0];
        player.Graveyard.Clear();
        var guards = Enumerable.Range(0, 4).Select(index => Card("S01-0212", $"isis-declared-guard-{index}")).ToArray();
        player.Field[0][0] = guards[0];
        player.Field[0][1] = guards[1];
        player.Field[0][2] = guards[2];
        player.Field[1][0] = guards[3];
        var canopic = Card("S01-0216", "isis-declared-canopic");
        player.Graveyard.Add(canopic);
        HoldOpponentResponseWindow(game);
        PrepareMain(game);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "isisCanopic")).Accepted);
        var guardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(4, guardPrompt.ValidChoices.Count(choice => choice != "skip"));
        var selected = guards.Take(3).Select(card => card.InstanceId).ToList();
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: guardPrompt.PromptId,
            CardInstanceIds: selected)).Accepted);
        ResolveSinglePrompt(game, canopic.InstanceId);
        var reward = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:draw", reward.ValidChoices);
        var rewardSnapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0))
            .GetProperty("Prompts")[0];
        var rewardLabels = rewardSnapshot.GetProperty("ChoiceLabels");
        Assert.False(rewardSnapshot.TryGetProperty("Continuation", out _));
        Assert.False(rewardSnapshot.TryGetProperty("StackItemId", out _));
        Assert.Equal("抽取1张牌", rewardLabels.GetProperty("mode:draw").GetString());
        Assert.Equal("我方主宰增加1点血量", rewardLabels.GetProperty("mode:heal").GetString());
        Assert.All(reward.ValidChoices, choice =>
            Assert.True(rewardLabels.TryGetProperty(choice, out _), $"缺少玩家可见标签：{choice}"));
        Assert.DoesNotContain(rewardLabels.EnumerateObject(), entry =>
            entry.Value.GetString()?.Contains("mode:", StringComparison.OrdinalIgnoreCase) == true);
        Assert.All(guards, guard => Assert.DoesNotContain(guard, player.Graveyard));
        ResolveSinglePrompt(game, "mode:draw");

        Assert.All(guards.Take(3), guard => Assert.Contains(guard, player.Graveyard));
        Assert.Same(guards[3], player.Field[1][0]);
        Assert.Single(game.State.EffectStack);
        PassResponses(game);
        Assert.Contains(canopic, player.SpecialZones.CanopicProgress);
    }

    [Fact]
    [Trait("L12Evidence", "ability:mengpoMorale")]
    public void MengpoPrivateDiscardCostIsDeclaredAndPaidBeforeStack()
    {
        var game = CreateWithFirstMaster("S01-01M2", 6905);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        player.Morale.Clear();
        opponent.Morale.Clear();
        AddMorale(opponent, 1);
        player.Hand.Clear();
        var discard = Card("S01-0101", "mengpo-declared-discard");
        player.Hand.Add(discard);
        HoldOpponentResponseWindow(game);
        PrepareMain(game);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "mengpoMorale")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(discard.InstanceId, prompt.ValidChoices);
        Assert.Contains(discard, player.Hand);
        Assert.Empty(game.State.EffectStack);
        ResolveSinglePrompt(game, discard.InstanceId);

        Assert.DoesNotContain(discard, player.Hand);
        Assert.Contains(discard, player.Graveyard);
        Assert.Single(game.State.EffectStack);
        PassResponses(game);
        Assert.Single(player.Morale);
    }

    [Fact]
    [Trait("L12Evidence", "ability:amaterasuReady")]
    public void AmaterasuPrivateDiscardCostIsDeclaredAndPaidBeforeStack()
    {
        var game = CreateWithFirstMaster("S01-04M1", 6906);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Morale.Clear();
        AddMorale(player, 2, tapped: true);
        var discard = Card("S01-0401", "amaterasu-declared-discard");
        player.Hand.Add(discard);
        HoldOpponentResponseWindow(game);
        PrepareMain(game);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "amaterasuReady")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(discard.InstanceId, prompt.ValidChoices);
        Assert.All(player.Morale, morale => Assert.True(morale.Tapped));
        ResolveSinglePrompt(game, discard.InstanceId);
        var moralePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: moralePrompt.PromptId,
            CardInstanceIds: [.. player.Morale.Select(morale => morale.InstanceId)])).Accepted);

        Assert.Contains(discard, player.Graveyard);
        Assert.All(player.Morale, morale => Assert.True(morale.Tapped));
        Assert.Single(game.State.EffectStack);
        PassResponses(game);
        Assert.All(player.Morale, morale => Assert.False(morale.Tapped));
    }

    [Fact]
    [Trait("L12Evidence", "entry:play")]
    public void HelaDeclaresTargetBeforeBasePaymentAndMillsItsColonCostBeforeStack()
    {
        var game = Create(6907);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        player.Hand.Clear();
        player.Library.Clear();
        player.Morale.Clear();
        var tactic = Card("S02-0307", "hela-declared-source");
        var milled = Card("S02-0301", "hela-prestack-mill");
        var target = Card("S02-0004", "hela-declared-target");
        player.Hand.Add(tactic);
        player.Library.Add(milled);
        enemy.Field[0][0] = target;
        HoldOpponentResponseWindow(game);
        AddMorale(player, tactic.CurrentCost);
        PrepareMain(game);

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(target.InstanceId, targetPrompt.ValidChoices);
        Assert.Contains(milled, player.Library);
        Assert.Equal(tactic.CurrentCost, player.Morale.Count(card => !card.Tapped));
        Assert.Empty(game.State.EffectStack);
        ResolveSinglePrompt(game, target.InstanceId);

        Assert.Contains(milled, player.Graveyard);
        Assert.Equal(0, player.Morale.Count(card => !card.Tapped));
        Assert.Single(game.State.EffectStack);
        enemy.Field[0][0] = null;
        enemy.Graveyard.Add(target);
        PassResponses(game);
        Assert.Empty(target.TimedModifiers);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("海拉", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:play")]
    public void HelaRejectsBeforeBasePaymentWhenItsColonCostCannotBePaid()
    {
        var game = Create(6910);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        player.Hand.Clear();
        player.Library.Clear();
        player.Morale.Clear();
        var tactic = Card("S02-0307", "hela-empty-library-source");
        var target = Card("S02-0004", "hela-empty-library-target");
        player.Hand.Add(tactic);
        enemy.Field[0][0] = target;
        AddMorale(player, tactic.CurrentCost);
        PrepareMain(game);

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        ResolveSinglePrompt(game, target.InstanceId);

        Assert.Contains(tactic, player.Hand);
        Assert.Equal(tactic.CurrentCost, player.Morale.Count(card => !card.Tapped));
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(game.State.Events, entry => entry.Type == "ability-rejected");
    }

    [Fact]
    [Trait("L12Evidence", "entry:play")]
    public void FearlessAssassinationDeclaresPublicTargetBeforePaying()
    {
        var game = Create(6908);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Morale.Clear();
        var tactic = Card("S02-0206", "fearless-declared-source");
        var target = Card("S01-0201", "fearless-declared-target");
        player.Hand.Add(tactic);
        player.Field[0][0] = target;
        HoldOpponentResponseWindow(game);
        AddMorale(player, tactic.CurrentCost);
        PrepareMain(game);

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(target.InstanceId, prompt.ValidChoices);
        Assert.Equal(tactic.CurrentCost, player.Morale.Count(card => !card.Tapped));
        Assert.Empty(game.State.EffectStack);
        ResolveSinglePrompt(game, target.InstanceId);

        Assert.Equal(0, player.Morale.Count(card => !card.Tapped));
        player.Field[0][0] = null;
        player.Graveyard.Add(target);
        PassResponses(game);
        Assert.Empty(target.TimedModifiers);
    }

    [Fact]
    [Trait("L12Evidence", "entry:play")]
    public void TenkaFubuDeclaresModeAndRowBeforePaying()
    {
        var game = Create(6909);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        player.Hand.Clear();
        player.Morale.Clear();
        var tactic = Card("S02-0406", "tenka-declared-source");
        var target = Card("S02-0004", "tenka-declared-row-target");
        player.Hand.Add(tactic);
        enemy.Field[1][0] = target;
        HoldOpponentResponseWindow(game);
        AddMorale(player, tactic.CurrentCost);
        PrepareMain(game);

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        ResolveSinglePrompt(game, "mode:row-cost");
        var row = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("row:1", row.ValidChoices);
        Assert.Equal(tactic.CurrentCost, player.Morale.Count(card => !card.Tapped));
        Assert.Empty(game.State.EffectStack);
        ResolveSinglePrompt(game, "row:1");

        Assert.Equal(0, player.Morale.Count(card => !card.Tapped));
        Assert.Single(game.State.EffectStack);
        PassResponses(game);
        Assert.Equal(-2, target.CostModifier);
    }
}
