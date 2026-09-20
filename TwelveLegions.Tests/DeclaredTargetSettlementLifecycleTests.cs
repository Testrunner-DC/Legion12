using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

/// <summary>
/// 同一批“声明后目标失效”的结算语义：全部失效必须失败、部分失效保留其余对象。
/// 这些测试直接保留响应窗口，模拟逆结算期间对象发生变化，而非只验证发动时筛选。
/// </summary>
public sealed class DeclaredTargetSettlementLifecycleTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "declared-target-settlement", "L12TARGET", seed,
            ["甲", "乙"], [0, 1], skipPreparation: true, autoPassEmptyResponses: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
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
            player.SpecialZones.Runes = 0;
            player.UsedAbilities.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int? troops = null)
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
            SummonRound = -1,
        };
    }

    private static void Push(L12GameEngine game, L12CardInstance source, string trigger,
        Dictionary<string, string> data)
    {
        var method = typeof(L12GameEngine).GetMethod("PushEffect", BindingFlags.Instance | BindingFlags.NonPublic)!;
        _ = method.Invoke(game, [0, source, trigger, source.Name, null, data]);
    }

    private static void PassResponses(L12GameEngine game, int maximum = 12)
    {
        var passed = 0;
        while (game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt && passed++ < maximum)
        {
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.True(passed < maximum, "响应窗口未在限定次数内结束");
    }

    [Fact]
    [Trait("L12Evidence", "type:multi-enemy-target")]
    public void ChaoticArrowsKeepsValidTargetsAndRecordsPartiallyInvalidDeclarations()
    {
        var game = Create(94101);
        var source = Card("S02-0011", "chaotic-arrows-source");
        var invalid = Card("S02-0003", "chaotic-arrows-invalid");
        var valid = Card("S02-0003", "chaotic-arrows-valid");
        game.State.Players[0].Resolving.Add(source);
        game.State.Players[1].Field[0][0] = invalid;
        game.State.Players[1].Field[0][1] = valid;

        Push(game, source, "play", new()
        {
            ["atomicFlow"] = "chaotic-arrows-effect",
            ["atomicContinuation"] = "true",
            ["declared:killTargets"] = $"{invalid.InstanceId}|{valid.InstanceId}",
        });
        game.State.Players[1].Field[0][0] = null;
        game.State.Players[1].Graveyard.Add(invalid);
        PassResponses(game);

        Assert.Contains(valid, game.State.Players[1].Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("1个已声明对象", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "type:multi-enemy-target-lethal-replacement")]
    public void ChaoticArrowsWaitsForLethalReplacementBeforeKillingItsNextTarget()
    {
        var game = Create(94109);
        var source = Card("S02-0011", "chaotic-arrows-replacement-source");
        var horemheb = Card("S01-0205", "chaotic-arrows-horemheb", 2000);
        var guard = Card("S01-0212", "chaotic-arrows-guard");
        var next = Card("S02-0003", "chaotic-arrows-next", 2000);
        game.State.Players[0].Resolving.Add(source);
        game.State.Players[1].Field[0][0] = horemheb;
        game.State.Players[1].Field[0][1] = guard;
        game.State.Players[1].Field[0][2] = next;

        Push(game, source, "play", new()
        {
            ["atomicFlow"] = "chaotic-arrows-effect",
            ["atomicContinuation"] = "true",
            ["declared:killTargets"] = $"{horemheb.InstanceId}|{next.InstanceId}",
        });
        PassResponses(game);

        var replacement = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("effect-lethal-replacement", replacement.Continuation);
        Assert.Same(horemheb, game.State.Players[1].Field[0][0]);
        Assert.Same(guard, game.State.Players[1].Field[0][1]);
        Assert.Same(next, game.State.Players[1].Field[0][2]);

        var result = game.Handle(1, new L12Command("resolvePrompt", PromptId: replacement.PromptId,
            Choice: guard.InstanceId));
        Assert.True(result.Accepted, result.Error);

        Assert.Same(horemheb, game.State.Players[1].Field[0][0]);
        Assert.Null(game.State.Players[1].Field[0][1]);
        Assert.Null(game.State.Players[1].Field[0][2]);
        Assert.Contains(guard, game.State.Players[1].Graveyard);
        Assert.Contains(next, game.State.Players[1].Graveyard);
        Assert.DoesNotContain(game.State.EffectStack,
            item => item.SourceInstanceId == source.InstanceId && item.Data.GetValueOrDefault("atomicFlow") == "chaotic-arrows-effect");
    }

    [Fact]
    [Trait("L12Evidence", "type:multi-enemy-target")]
    public void StarterMultiTargetKillFailsWhenEveryDeclaredTargetLeavesBeforeSettlement()
    {
        var game = Create(94102);
        var source = Card("ST04-02", "kojiro-source");
        var target = Card("S02-0003", "kojiro-invalid");
        game.State.Players[0].Graveyard.Add(source);
        game.State.Players[1].Field[0][0] = target;

        Push(game, source, "death", new()
        {
            ["atomicFlow"] = "kojiro-death-kill",
            ["declared:enemyTargets"] = target.InstanceId,
        });
        game.State.Players[1].Field[0][0] = null;
        game.State.Players[1].Graveyard.Add(target);
        PassResponses(game);

        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("佐佐木小次郎", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "type:single-enemy-target")]
    public void QianKunYangFailsWhenItsDeclaredTargetLeavesBeforeSettlement()
    {
        var game = Create(94103);
        var source = Card("S02-0105", "qianyang-source");
        var target = Card("S02-0003", "qianyang-invalid");
        game.State.Players[0].Resolving.Add(source);
        game.State.Players[1].Field[0][0] = target;

        Push(game, source, "play", new()
        {
            ["atomicFlow"] = "qianyang-kill",
            ["atomicContinuation"] = "true",
            ["declared:killTarget"] = target.InstanceId,
        });
        game.State.Players[1].Field[0][0] = null;
        game.State.Players[1].Graveyard.Add(target);
        PassResponses(game);

        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("乾坤 阳", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "type:independent-target-pair")]
    public void HannibalKeepsTheRemainingDeclaredTargetWhenTheOtherOneLeaves()
    {
        var game = Create(94104);
        var source = Card("S02-0516", "hannibal-source");
        var invalidOwn = Card("S02-0004", "hannibal-invalid-own");
        var validEnemy = Card("S02-0004", "hannibal-valid-enemy");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].Field[0][1] = invalidOwn;
        game.State.Players[1].Field[0][0] = validEnemy;
        game.State.Players[0].Morale.Add(new L12MoraleCard
        {
            InstanceId = "hannibal-target-settlement-power",
            CardId = "S02-05C1",
            IsGodPower = true,
        });

        Assert.True(game.Handle(0, new L12Command("attack", source.InstanceId,
            Target: new L12AttackTarget("legion", validEnemy.InstanceId))).Accepted);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: mode.PromptId,
            Choice: "mode:use")).Accepted);
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId,
            Choice: "hannibal-target-settlement-power")).Accepted);
        var own = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: own.PromptId,
            Choice: invalidOwn.InstanceId)).Accepted);
        var enemy = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: enemy.PromptId,
            Choice: validEnemy.InstanceId)).Accepted);
        game.State.Players[0].Field[0][1] = null;
        game.State.Players[0].Graveyard.Add(invalidOwn);
        PassResponses(game);

        Assert.Contains(validEnemy.TimedModifiers, modifier => modifier.TroopsDelta == -2000);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("1个已声明对象", StringComparison.Ordinal));
    }
}
