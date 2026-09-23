using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class GrantedCombatKeywordLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence("S02-0206:ability:granted:1aba3f5bd15a426d", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    public void GrantedMustHitUsesOneCurrentStateForProjectionDefenseAndTurnExpiryAcrossRestore()
    {
        Assert.True(L12StructuredCardRules.HasPrintedKeywordReference("S02-0206", "must-hit"));
        var game = Create(72570);
        var attacker = Card("S01-0003", "granted-must-hit-attacker", 0);
        var defender = Card("S01-0003", "granted-must-hit-defender", 1);
        var blocker = Card("S01-0003", "granted-must-hit-blocker", 1);
        attacker.SummonRound = defender.SummonRound = blocker.SummonRound = 0;
        attacker.SureHitAgainstLegionsUntilTurn = game.State.TurnSerial;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = defender;
        game.State.Players[1].Field[1][0] = blocker;

        Assert.Contains("必中", ActiveKeywords(game, 0, 0));
        game = Restore(game);
        attacker = game.State.Players[0].Field[0][0]!;
        defender = game.State.Players[1].Field[0][0]!;
        blocker = game.State.Players[1].Field[1][0]!;
        Assert.Contains("必中", ActiveKeywords(game, 0, 0));

        var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", defender.InstanceId)));
        Assert.True(result.Accepted, result.Error);
        Assert.True(game.State.PendingDefense?.SureHit);
        PassResponses(game);
        Assert.False(blocker.Tapped);

        game = Create(72571);
        attacker = Card("S01-0003", "granted-must-hit-expiry", 0);
        attacker.SureHitAgainstLegionsUntilTurn = game.State.TurnSerial;
        game.State.Players[0].Field[0][0] = attacker;
        Assert.True(game.Handle(0, new L12Command("endTurn")).Accepted);
        Assert.DoesNotContain("必中", ActiveKeywords(game, 0, 0));
    }

    [Fact]
    [L12AbilityEvidence("S02-0404:ability:granted:e3ff02735b6b18f4", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    public void GrantedDeathImmunityUsesOneCurrentStateForProjectionLethalReplacementAndLeaveReset()
    {
        Assert.True(L12StructuredCardRules.HasPrintedKeywordReference("S02-0404", "death-immunity"));
        var game = Create(72572);
        var target = Card("S01-0002", "granted-death-immunity", 0);
        target.ImmortalUses = 1;
        target.ImmortalUntilTurn = game.State.TurnSerial;
        game.State.Players[0].Field[0][0] = target;
        Assert.Contains("免死", ActiveKeywords(game, 0, 0));

        game = Restore(game);
        target = game.State.Players[0].Field[0][0]!;
        Assert.Contains("免死", ActiveKeywords(game, 0, 0));
        var result = game.HandleGm(new L12GmCommand("destroyCard", 0, CardInstanceId: target.InstanceId));
        Assert.False(result.Accepted);
        Assert.Same(target, game.State.Players[0].Field[0][0]);
        Assert.Equal(0, target.ImmortalUses);
        Assert.DoesNotContain("免死", ActiveKeywords(game, 0, 0));

        game = Create(72573);
        target = Card("S01-0002", "granted-death-immunity-leave", 0);
        target.ImmortalUses = 1;
        target.ImmortalUntilTurn = game.State.TurnSerial;
        game.State.Players[0].Field[0][0] = target;
        result = game.HandleGm(new L12GmCommand("returnCardToHand", 0, CardInstanceId: target.InstanceId));
        Assert.True(result.Accepted, result.Error);
        target = Assert.Single(game.State.Players[0].Hand, card => card.InstanceId == target.InstanceId);
        Assert.Equal(0, target.ImmortalUses);
        Assert.Equal(-1, target.ImmortalUntilTurn);
    }

    [Fact]
    [L12AbilityEvidence("ST01-01:ability:granted:6ec4b634ed12b206", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    public void GrantedPiercingRestoresPaidStackAndGeneratesOneTriggerSuppressedMasterAttack()
    {
        Assert.True(L12StructuredCardRules.HasPrintedKeywordReference("ST01-01", "piercing"));
        var game = Create(72574);
        var player = game.State.Players[0];
        var zhaoyun = Card("ST01-01", "granted-piercing-zhaoyun", 0);
        player.Field[0][0] = zhaoyun;
        player.Morale.Add(new L12MoraleCard { CardId = "ST01-C1", InstanceId = "granted-piercing-morale" });
        QueueTrigger(game, zhaoyun, "after-attack", new Dictionary<string, string>
        {
            ["killed"] = "true",
            ["combatKillConfirmed"] = "true",
            ["sourceWasAttackingLegion"] = "true",
        });
        Choose(game, "mode:use");
        Choose(game, "granted-piercing-morale");
        Assert.Empty(player.Morale);
        Assert.Single(game.State.EffectStack);

        game = Restore(game);
        PassResponses(game);
        var pending = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal("master", pending.Target.Type);
        Assert.True(pending.SuppressAttackTriggers);
        Assert.Equal(zhaoyun.InstanceId, pending.AttackerInstanceId);
        Assert.Single(game.State.Events, entry => entry.Type == "piercing"
            && entry.Text.Contains("剩余兵力", StringComparison.Ordinal)
            && entry.Text.Contains("不触发【进攻时】效果", StringComparison.Ordinal));
        Assert.Contains($"starter-piercing:{zhaoyun.InstanceId}:{game.State.TurnSerial}",
            game.State.Players[0].UsedAbilities);
    }

    private static string[] ActiveKeywords(L12GameEngine game, int playerIndex, int slot)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(playerIndex),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return snapshot.GetProperty("players")[playerIndex].GetProperty("field")[0][slot]
            .GetProperty("activeKeywords").EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static void QueueTrigger(L12GameEngine game, L12CardInstance source, string trigger,
        Dictionary<string, string> data)
    {
        var method = typeof(L12GameEngine).GetMethod("QueueOrPushTriggeredEffect",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(game, [0, source, trigger, $"〈{source.Name}〉{trigger}效果", null, data]);
    }

    private static void Choose(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var guard = 0; guard < 20; guard++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault(candidate => candidate.Kind == "response");
            if (prompt is null) return;
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        throw new InvalidOperationException("响应窗口未在限定次数内结束");
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "granted-combat-keywords", "GRANTED-KEYWORDS", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Morale.Clear();
        }
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static L12CardInstance Card(string cardId, string instanceId, int ownerIndex)
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = ownerIndex,
            SummonRound = 0,
        };
    }
}
