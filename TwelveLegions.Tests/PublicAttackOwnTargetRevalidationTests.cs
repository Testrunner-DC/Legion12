using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

/// <summary>
/// 【进攻时】预先声明的我方军团必须在响应后仍为同一张合法军团。
/// 目标失效不退还已支付费用，并统一记录为结算失败而非玩家取消。
/// </summary>
public sealed class PublicAttackOwnTargetRevalidationTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "attack-own-target-revalidation", "ATTACK-OWN-TARGET", seed,
            ["甲", "乙"], [0, 1], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Phase = L12Phase.Main;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, string? cardType = null,
        int? troops = null)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = cardType ?? definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = troops ?? definition.Troops ?? 1000,
            Troops = troops ?? definition.Troops ?? 1000,
            SummonRound = -1,
        };
    }

    private static L12MoraleCard Morale(string instanceId)
        => new() { CardId = "S01-01C1", InstanceId = instanceId };

    private static void AttackMaster(L12GameEngine game, L12CardInstance attacker)
    {
        game.State.Players[0].Field[0][0] = attacker;
        var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(result.Accepted, result.Error);
    }

    private static string Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt.PromptId;
    }

    private static void PassResponses(L12GameEngine game, int maximum = 16)
    {
        var passed = 0;
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response" && passed++ < maximum)
        {
            var prompt = game.State.PendingPrompts[0];
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.True(passed < maximum, "响应窗口未在限定次数内结束");
    }

    private static L12GameEngine RestoreAsV2(L12GameEngine game)
    {
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        return L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
    }

    private static void AssertFailedWithoutCancellation(L12GameEngine game, string sourceName)
    {
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains(sourceName, StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains(sourceName, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0208")]
    [Trait("L12Evidence", "entry:attack-own-target-current-type")]
    public void AyPaidCostButTargetBecomesArtifactDuringResponsesDoesNotBuffIt()
    {
        var game = Create(9301);
        var player = game.State.Players[0];
        var source = Card("S01-0208", "attack-own-ay-source");
        var target = Card("S01-0001", "attack-own-ay-target", troops: 2000);
        var morale = Morale("attack-own-ay-morale");
        player.Field[0][1] = target;
        player.Morale.Add(morale);

        AttackMaster(game, source);
        Resolve(game, "mode:use");
        Resolve(game, target.InstanceId);
        Assert.True(morale.Tapped);

        var transformed = Card(target.CardId, target.InstanceId, cardType: "artifact", troops: 2000);
        player.Field[0][1] = transformed;
        PassResponses(game);

        Assert.True(morale.Tapped);
        Assert.Empty(transformed.TimedModifiers);
        AssertFailedWithoutCancellation(game, source.Name);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0208")]
    [Trait("L12Evidence", "entry:attack-own-target-current-threshold")]
    public void AyPaidCostButTargetExceedsTroopThresholdDuringResponsesDoesNotBuffIt()
    {
        var game = Create(9302);
        var player = game.State.Players[0];
        var source = Card("S01-0208", "attack-own-ay-threshold-source");
        var target = Card("S01-0001", "attack-own-ay-threshold-target", troops: 2000);
        var morale = Morale("attack-own-ay-threshold-morale");
        player.Field[0][1] = target;
        player.Morale.Add(morale);

        AttackMaster(game, source);
        Resolve(game, "mode:use");
        Resolve(game, target.InstanceId);
        target.Troops = 3000;
        PassResponses(game);

        Assert.True(morale.Tapped);
        Assert.Empty(target.TimedModifiers);
        AssertFailedWithoutCancellation(game, source.Name);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0416")]
    [Trait("L12Evidence", "entry:attack-own-target-failure-result")]
    public void InahimeTargetExceedingThresholdIsFailedRatherThanCancelled()
    {
        var game = Create(9303);
        var player = game.State.Players[0];
        var source = Card("S01-0416", "attack-own-inahime-source");
        var target = Card("S01-0401", "attack-own-inahime-target", troops: 5000);
        player.Field[0][1] = target;

        AttackMaster(game, source);
        Resolve(game, target.InstanceId);
        target.Troops = 6000;
        PassResponses(game);

        Assert.Empty(target.TimedModifiers);
        AssertFailedWithoutCancellation(game, source.Name);
    }

    [Fact]
    [Trait("L12Evidence", "entry:attack-own-target-v2-recovery")]
    [Trait("L12Evidence", "entry:attack-own-target-repeat-submit")]
    public void RestoredAyResponseRevalidatesTargetAndRejectsStalePrompt()
    {
        var game = Create(9304);
        var player = game.State.Players[0];
        var source = Card("S01-0208", "attack-own-ay-restore-source");
        var target = Card("S01-0001", "attack-own-ay-restore-target", troops: 2000);
        var morale = Morale("attack-own-ay-restore-morale");
        player.Field[0][1] = target;
        player.Morale.Add(morale);

        AttackMaster(game, source);
        Resolve(game, "mode:use");
        Resolve(game, target.InstanceId);
        var responseId = Assert.Single(game.State.PendingPrompts).PromptId;

        game = RestoreAsV2(game);
        var restoredTarget = game.State.Players[0].Field[0][1]!;
        game.State.Players[0].Field[0][1] = Card(restoredTarget.CardId, restoredTarget.InstanceId,
            cardType: "artifact", troops: 2000);
        PassResponses(game);

        Assert.True(game.State.Players[0].Morale.Single(card => card.InstanceId == morale.InstanceId).Tapped);
        Assert.Empty(game.State.Players[0].Field[0][1]!.TimedModifiers);
        AssertFailedWithoutCancellation(game, source.Name);
        Assert.False(game.Handle(0,
            new L12Command("resolvePrompt", PromptId: responseId, Choice: "pass")).Accepted);
    }
}
