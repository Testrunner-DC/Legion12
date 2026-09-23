using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MercenaryHandBlockLifecycleProfileTests
{
    private const string AbilityId = "S01-0002:ability:reaction:a472c4e7c34abf4b";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence(AbilityId, "normal", "reconnect", "duplicate-submit", "presentation-consumers")]
    public void PaidHandBlockSurvivesRestoreAndBlocksExactlyOnce()
    {
        var (game, attacker, target, mercenary) = Prepare(72530);
        var prompt = BeginAttackAndFindMercenaryPrompt(game, attacker, target, mercenary);
        var result = game.Handle(1, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: mercenary.InstanceId));
        Assert.True(result.Accepted, result.Error);
        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == mercenary.InstanceId);
        Assert.Contains(game.State.EffectStack, item => item.Trigger == "response-block");

        game = Restore(game);
        Assert.False(game.Handle(1, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: mercenary.InstanceId)).Accepted);
        PassAll(game);

        Assert.NotNull(FindField(game.State.Players[1], target.InstanceId));
        Assert.Single(game.State.Players[1].Graveyard, card => card.InstanceId == mercenary.InstanceId);
        Assert.Single(game.State.Events, entry => entry.Type == "defense"
            && entry.Cards.Any(card => card.InstanceId == mercenary.InstanceId));
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.InstanceId == mercenary.InstanceId));
    }

    [Fact]
    [L12AbilityEvidence(AbilityId, "payment-cancel")]
    public void PassingResponseWindowDoesNotPaySelfDiscardCost()
    {
        var (game, attacker, target, mercenary) = Prepare(72531);
        var prompt = BeginAttackAndFindMercenaryPrompt(game, attacker, target, mercenary);
        var result = game.Handle(1, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
        Assert.True(result.Accepted, result.Error);
        PassAll(game);

        Assert.Contains(game.State.Players[1].Hand, card => card.InstanceId == mercenary.InstanceId);
        Assert.DoesNotContain(game.State.Players[1].Graveyard, card => card.InstanceId == mercenary.InstanceId);
        Assert.Null(FindField(game.State.Players[1], target.InstanceId));
    }

    [Fact]
    [L12AbilityEvidence(AbilityId, "negated")]
    public void NegatedBlockKeepsPaidDiscardAndAttackContinues()
    {
        var (game, attacker, target, mercenary) = Prepare(72532);
        var prompt = BeginAttackAndFindMercenaryPrompt(game, attacker, target, mercenary);
        var result = game.Handle(1, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: mercenary.InstanceId));
        Assert.True(result.Accepted, result.Error);
        Assert.Single(game.State.EffectStack, item => item.Trigger == "response-block").Negated = true;
        PassAll(game);

        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == mercenary.InstanceId);
        Assert.Null(FindField(game.State.Players[1], target.InstanceId));
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "negated"
            && entry.Cards.Any(card => card.InstanceId == mercenary.InstanceId));
    }

    [Fact]
    [L12AbilityEvidence(AbilityId, "target-invalidated")]
    public void MissingAttackStackTargetFailsBlockWithoutBindingAnotherItem()
    {
        var (game, attacker, target, mercenary) = Prepare(72533);
        var prompt = BeginAttackAndFindMercenaryPrompt(game, attacker, target, mercenary);
        var result = game.Handle(1, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: mercenary.InstanceId));
        Assert.True(result.Accepted, result.Error);
        var block = Assert.Single(game.State.EffectStack, item => item.Trigger == "response-block");
        var declaredAttackId = Assert.Single(block.Targets);
        game.State.EffectStack.RemoveAll(item => item.StackItemId == declaredAttackId);
        PassAll(game);

        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == mercenary.InstanceId);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("响应目标已经离开堆叠", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "defense"
            && entry.Cards.Any(card => card.InstanceId == mercenary.InstanceId));
    }

    private static L12Prompt BeginAttackAndFindMercenaryPrompt(L12GameEngine game,
        L12CardInstance attacker, L12CardInstance target, L12CardInstance mercenary)
    {
        var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId)));
        Assert.True(result.Accepted, result.Error);
        for (var count = 0; count < 50; count++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.Kind == "response" && prompt.ValidChoices.Contains(mercenary.InstanceId)) return prompt;
            result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                Choice: prompt.Kind == "response" ? "pass"
                    : prompt.ValidChoices.Contains("skip") ? "skip"
                    : prompt.ValidChoices.Contains("no") ? "no" : prompt.ValidChoices.First()));
            Assert.True(result.Accepted, result.Error);
        }
        throw new Xunit.Sdk.XunitException("未进入佣兵部队手牌响应窗口");
    }

    private static void PassAll(L12GameEngine game)
    {
        for (var count = 0; count < 100; count++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                var choice = prompt.Kind == "response" ? "pass"
                    : prompt.ValidChoices.Contains("no") ? "no"
                    : prompt.ValidChoices.Contains("skip") ? "skip" : prompt.ValidChoices.First();
                var result = game.Handle(prompt.PlayerIndex,
                    new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
                Assert.True(result.Accepted, result.Error);
                continue;
            }
            if (game.State.PendingDefense is not null)
            {
                var result = game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: []));
                Assert.True(result.Accepted, result.Error);
                continue;
            }
            if (game.State.EffectStack.Count == 0) break;
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    private static (L12GameEngine Game, L12CardInstance Attacker, L12CardInstance Target,
        L12CardInstance Mercenary) Prepare(int seed)
    {
        var game = new L12GameEngine(Catalog, "mercenary-hand-block", "MERCENARY-BLOCK", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
        }
        var attacker = Card("S01-0001", "mercenary-profile-attacker", 0, 9000);
        var target = Card("S01-0102", "mercenary-profile-target", 1, 1000);
        var mercenary = Card("S01-0002", "mercenary-profile-response", 1);
        attacker.SummonRound = target.SummonRound = -1;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[1].Hand.Add(mercenary);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;
        return (game, attacker, target, mercenary);
    }

    private static L12CardInstance? FindField(L12PlayerState player, string instanceId)
        => player.Field.SelectMany(row => row).FirstOrDefault(card => card?.InstanceId == instanceId);

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static L12CardInstance Card(string cardId, string instanceId, int ownerIndex, int? troops = null)
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
            HasPrintedCost = definition.Cost is not null,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            OwnerIndex = ownerIndex,
        };
    }
}
