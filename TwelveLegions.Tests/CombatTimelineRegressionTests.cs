using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CombatTimelineRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 82801, bool autoPass = true)
        => new(Catalog, "combat-timeline", "COMBAT", seed, ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: autoPass, concealHiddenResponseAvailability: false);

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
            SummonRound = -1,
        };
    }

    private static L12CardInstance PlainLegion(string instanceId, int troops)
        => new()
        {
            InstanceId = instanceId,
            CardId = $"test-{instanceId}",
            Name = instanceId,
            CardType = "legion",
            Faction = "universal",
            Cost = 1,
            BaseTroops = troops,
            Troops = troops,
            SummonRound = -1,
        };

    private static void ReadyForCombat(L12GameEngine game)
    {
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
        }
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;
    }

    private static void PassCurrentResponse(L12GameEngine game)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", prompt.Kind);
        Assert.True(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
    }

    [Fact]
    public void AttackerAttackTimingFullySettlesBeforeDefenderOpponentAttackTiming()
    {
        var game = Create();
        ReadyForCombat(game);
        var attacker = Card("S01-0104", "timing-attacker");
        var target = PlainLegion("timing-target", 3000);
        var ambush = Card("S01-0019", "timing-ambush");
        ambush.Hidden = true;
        ambush.SetRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[1].Field[1][1] = ambush;
        game.State.Players[0].Morale.Add(new L12MoraleCard
        {
            InstanceId = "timing-morale", CardId = "S01-01C1", Tapped = false,
        });

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);

        var attackerPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, attackerPrompt.PlayerIndex);
        Assert.Equal("card-effect", attackerPrompt.Continuation);
        Assert.Equal(L12CombatStage.AttackerAttackTiming, game.State.PendingDefense?.Stage);
        Assert.DoesNotContain(ambush.InstanceId, attackerPrompt.ValidChoices);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: attackerPrompt.PromptId, Choice: "no")).Accepted);

        var defenderPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, defenderPrompt.PlayerIndex);
        Assert.Equal("response", defenderPrompt.Kind);
        Assert.Contains(ambush.InstanceId, defenderPrompt.ValidChoices);
        Assert.Equal(L12CombatStage.DefenderAttackTiming, game.State.PendingDefense?.Stage);
    }

    [Fact]
    public void AbsoluteDefenseAtDefenderTimingBlocksAttackWithoutRewindingAttackerTiming()
    {
        var game = Create(82809);
        ReadyForCombat(game);
        var attacker = PlainLegion("absolute-attacker", 3000);
        var absoluteDefense = Card("S01-0016", "absolute-defense");
        var discard = PlainLegion("absolute-discard", 1000);
        absoluteDefense.Hidden = true;
        absoluteDefense.SetRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[1][0] = absoluteDefense;
        game.State.Players[1].Hand.Add(discard);

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);

        var responsePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, responsePrompt.PlayerIndex);
        Assert.Contains(absoluteDefense.InstanceId, responsePrompt.ValidChoices);
        Assert.Equal(L12CombatStage.DefenderAttackTiming, game.State.PendingDefense?.Stage);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: responsePrompt.PromptId,
            Choice: absoluteDefense.InstanceId)).Accepted);
        var discardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: discardPrompt.PromptId,
            Choice: discard.InstanceId)).Accepted);

        Assert.Null(game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Contains(absoluteDefense, game.State.Players[1].Graveyard);
        Assert.Contains(discard, game.State.Players[1].Graveyard);
        Assert.Equal(10, game.State.Players[1].Hp);
        Assert.Contains(game.State.Events, entry => entry.Type == "combat-stage"
            && entry.Text.Contains("绝对防御", StringComparison.Ordinal));
    }

    [Fact]
    public void AttackerLeavingDuringAttackTimingAutomaticallyAbortsAtSafeBoundary()
    {
        var game = Create(82802);
        ReadyForCombat(game);
        var attacker = Card("S01-0104", "leaving-attacker");
        var target = PlainLegion("leaving-target", 3000);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[0].Morale.Add(new L12MoraleCard
        {
            InstanceId = "leaving-morale", CardId = "S01-01C1", Tapped = false,
        });

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        game.State.Players[0].Field[0][0] = null;
        game.State.Players[0].Graveyard.Add(attacker);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "no")).Accepted);

        Assert.Null(game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Contains(game.State.Events, entry => entry.Type == "attack-aborted"
            && entry.Text.Contains("进攻军团已离场", StringComparison.Ordinal));
        Assert.Same(target, game.State.Players[1].Field[0][0]);
        Assert.Equal(3000, target.Troops);
    }

    [Fact]
    public void TargetLeavingDuringAttackTimingAutomaticallyAbortsAtSafeBoundary()
    {
        var game = Create(82807);
        ReadyForCombat(game);
        var attacker = Card("S01-0104", "target-leave-attacker");
        var target = PlainLegion("target-leave-target", 3000);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[0].Morale.Add(new L12MoraleCard
        {
            InstanceId = "target-leave-morale", CardId = "S01-01C1", Tapped = false,
        });

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        game.State.Players[1].Field[0][0] = null;
        game.State.Players[1].Hand.Add(target);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "no")).Accepted);

        Assert.Null(game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Contains(game.State.Events, entry => entry.Type == "attack-aborted"
            && entry.Text.Contains("被进攻军团已离场", StringComparison.Ordinal));
        Assert.Same(attacker, game.State.Players[0].Field[0][0]);
        Assert.Equal(3000, target.Troops);
    }

    [Fact]
    public void DefenseUsesFrozenAttackValueAndKeepsSameColumnSupportSemantics()
    {
        var game = Create(82803);
        ReadyForCombat(game);
        var attacker = PlainLegion("freeze-attacker", 3000);
        var target = PlainLegion("freeze-target", 2000);
        var support = PlainLegion("freeze-support", 1000);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][1] = target;
        game.State.Players[1].Field[1][1] = support;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        Assert.Equal(L12CombatStage.DefenseChoice, game.State.PendingDefense?.Stage);
        Assert.Equal(3000, game.State.PendingDefense?.AttackValue);
        var reconnectSnapshot = game.SnapshotFor(1);
        var reconnectCombat = Assert.IsType<L12PendingDefense>(reconnectSnapshot.PendingDefense);
        Assert.Equal(L12CombatStage.DefenseChoice, reconnectCombat.Stage);
        Assert.Equal(3000, reconnectCombat.AttackValue);
        var reconnectJson = JsonSerializer.SerializeToElement(reconnectSnapshot,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var serializedCombat = reconnectJson.GetProperty("pendingDefense");
        Assert.Equal("DefenseChoice", serializedCombat.GetProperty("stage").GetString());
        Assert.Equal(3000, serializedCombat.GetProperty("attackValue").GetInt32());

        attacker.Troops = 5000;
        Assert.True(game.Handle(1, new L12Command("resolveDefense", SupportInstanceId: support.InstanceId)).Accepted);

        Assert.Same(attacker, game.State.Players[0].Field[0][0]);
        Assert.Same(target, game.State.Players[1].Field[0][1]);
        Assert.Equal(5000, attacker.Troops);
        Assert.Equal(2000, target.Troops);
        Assert.Contains(support, game.State.Players[1].Graveyard);
        Assert.DoesNotContain(game.State.Events, entry => entry.Text.Contains("【击杀时】", StringComparison.Ordinal));
    }

    [Fact]
    public void DefenseAuthorityEventRevalidatesBeforeExtraSettlement()
    {
        var game = Create(82804, autoPass: false);
        ReadyForCombat(game);
        var attacker = PlainLegion("revalidate-attacker", 3000);
        var blocker = PlainLegion("revalidate-blocker", 3000);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Hand.Add(blocker);
        var hpBefore = game.State.Players[1].Hp;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        PassCurrentResponse(game);
        PassCurrentResponse(game);
        Assert.Equal(L12CombatStage.DefenseChoice, game.State.PendingDefense?.Stage);

        Assert.True(game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: [blocker.InstanceId])).Accepted);
        game.State.Players[1].Hand.Remove(blocker);
        game.State.Players[1].Graveyard.Add(blocker);
        PassCurrentResponse(game);
        PassCurrentResponse(game);

        Assert.Equal(hpBefore - 1, game.State.Players[1].Hp);
        Assert.Contains(game.State.Events, entry => entry.Type == "defense-invalid");
        Assert.Null(game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, game.State.Phase);
    }

    [Fact]
    public void AttackerLeavingDuringDefenseAuthorityEventSkipsRichardExtraDiscardAndAborts()
    {
        var game = Create(82809, autoPass: false);
        ReadyForCombat(game);
        var attacker = Card("S02-0608", "authority-leave-richard");
        var blocker = PlainLegion("authority-leave-blocker", 3000);
        var spare = PlainLegion("authority-leave-spare", 1000);
        attacker.Troops = 3000;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Hand.AddRange([blocker, spare]);

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        for (var step = 0; step < 16
             && game.State.PendingDefense?.Stage != L12CombatStage.DefenseChoice; step++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            var choice = prompt.Kind == "response" ? "pass"
                : prompt.ValidChoices.Contains("no") ? "no"
                : prompt.ValidChoices.Contains("skip") ? "skip"
                : prompt.ValidChoices[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        }
        Assert.Equal(L12CombatStage.DefenseChoice, game.State.PendingDefense?.Stage);

        Assert.True(game.Handle(1, new L12Command("resolveDefense",
            CardInstanceIds: [blocker.InstanceId])).Accepted);
        game.State.Players[0].Field[0][0] = null;
        game.State.Players[0].Graveyard.Add(attacker);
        for (var step = 0; step < 6 && game.State.PendingDefense is not null; step++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", prompt.Kind);
            Assert.NotEqual("s2-richard-defense-extra-discard", prompt.Data.GetValueOrDefault("action"));
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }

        Assert.Null(game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Contains(blocker, game.State.Players[1].Hand);
        Assert.Contains(spare, game.State.Players[1].Hand);
        Assert.Contains(game.State.Events, entry => entry.Type == "defense-invalid"
            && entry.Text.Contains("不再支付额外费用", StringComparison.Ordinal));
        Assert.Contains(game.State.Events, entry => entry.Type == "attack-aborted"
            && entry.Text.Contains("进攻军团已离场", StringComparison.Ordinal));
    }

    [Fact]
    public void KillTriggersBeforeDefenderDeathAndCardEntersGraveOnlyAfterBoth()
    {
        var game = Create(82805);
        ReadyForCombat(game);
        var attacker = Card("S01-0409", "kill-attacker");
        var defender = Card("S01-0102", "kill-defender");
        attacker.Troops = 2000;
        defender.Troops = 1000;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = defender;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", defender.InstanceId))).Accepted);

        for (var step = 0; step < 4
             && game.State.PendingDefense?.Stage == L12CombatStage.DefenderAttackTiming; step++)
            PassCurrentResponse(game);

        Assert.Equal(L12CombatStage.KillTriggers, game.State.PendingDefense?.Stage);
        Assert.Contains(defender, game.State.Players[1].Resolving);
        Assert.DoesNotContain(defender, game.State.Players[1].Graveyard);
        var killPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(killPrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: killPrompt.PromptId, Choice: "no")).Accepted);

        var triggerEvents = game.State.Events.Where(entry => entry.Type == "effect-trigger").ToList();
        var killIndex = triggerEvents.FindIndex(entry => entry.Cards.Any(card => card.InstanceId == attacker.InstanceId));
        var deathIndex = triggerEvents.FindIndex(entry => entry.Cards.Any(card => card.InstanceId == defender.InstanceId));
        Assert.True(killIndex >= 0 && deathIndex > killIndex);
        Assert.Contains(defender, game.State.Players[1].Graveyard);
        Assert.DoesNotContain(defender, game.State.Players[1].Resolving);
    }

    [Fact]
    public void MutualDefeatSkipsKillAndOrdersAttackerDeathBeforeDefenderDeath()
    {
        var game = Create(82806);
        ReadyForCombat(game);
        var attacker = Card("S01-0102", "mutual-attacker");
        var defender = Card("S01-0102", "mutual-defender");
        attacker.Troops = defender.Troops = 1000;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = defender;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", defender.InstanceId))).Accepted);

        var triggers = game.State.Events.Where(entry => entry.Type == "effect-trigger").ToList();
        var attackerDeath = triggers.FindIndex(entry => entry.Cards.Any(card => card.InstanceId == attacker.InstanceId));
        var defenderDeath = triggers.FindIndex(entry => entry.Cards.Any(card => card.InstanceId == defender.InstanceId));
        Assert.True(attackerDeath >= 0 && defenderDeath > attackerDeath);
        Assert.DoesNotContain(game.State.Events, entry => entry.Text.Contains("【击杀时】", StringComparison.Ordinal));
        Assert.Contains(attacker, game.State.Players[0].Graveyard);
        Assert.Contains(defender, game.State.Players[1].Graveyard);
        var firstGrave = game.State.Events.FindIndex(entry => entry.Type == "grave");
        var lastDeathTrigger = game.State.Events.FindLastIndex(entry => entry.Type == "effect-trigger"
            && entry.Cards.Any(card => card.InstanceId is "mutual-attacker" or "mutual-defender"));
        Assert.True(firstGrave > lastDeathTrigger);
    }

    [Fact]
    public void AttackerAfterAttackFinishesBeforeDefenderAfterAttackAndMainWaitsForBoth()
    {
        var game = Create(82808);
        ReadyForCombat(game);
        var attacker = Card("S01-0101", "after-attacker");
        var target = PlainLegion("after-target", 1000);
        var defenderAfter = Card("S01-0017", "after-defender-reaction");
        defenderAfter.Hidden = true;
        defenderAfter.SetRound = 0;
        attacker.Troops = 5000;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[1].Field[1][1] = defenderAfter;
        for (var index = 0; index < 4; index++)
            game.State.Players[0].Morale.Add(new L12MoraleCard
            {
                InstanceId = $"after-morale-{index}", CardId = "S01-01C1", Tapped = false,
            });

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);

        var attackerAfterPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, attackerAfterPrompt.PlayerIndex);
        Assert.Equal(L12CombatStage.AttackerAfterAttack, game.State.PendingDefense?.Stage);
        Assert.Equal(L12Phase.Defense, game.State.Phase);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: attackerAfterPrompt.PromptId,
            Choice: "no")).Accepted);

        var defenderAfterPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, defenderAfterPrompt.PlayerIndex);
        Assert.Equal(L12CombatStage.DefenderAfterAttack, game.State.PendingDefense?.Stage);
        Assert.Equal(L12Phase.Defense, game.State.Phase);

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

        Assert.Null(game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        var stages = game.State.Events.Where(entry => entry.Type == "effect-trigger").ToList();
        var attackerAfterIndex = stages.FindIndex(entry => entry.Cards.Any(card => card.InstanceId == attacker.InstanceId));
        var defenderAfterIndex = stages.FindIndex(entry => entry.Cards.Any(card => card.InstanceId == defenderAfter.InstanceId));
        Assert.True(attackerAfterIndex >= 0 && defenderAfterIndex > attackerAfterIndex);
    }

    [Fact]
    public void PrintedCombatTimingRegistryMatchesTheWholeCardPool()
    {
        var cards = Catalog.Cards.Values.ToArray();
        var kill = cards.Where(card => card.CardType == "legion"
                && (card.Effect?.Contains("击杀时", StringComparison.Ordinal) == true
                    || card.Effect?.Contains("击杀对方军团后", StringComparison.Ordinal) == true))
            .Select(card => card.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var attackerAfter = cards.Where(card => card.CardType == "legion"
                && card.Effect?.Contains("此军团进攻后", StringComparison.Ordinal) == true)
            .Select(card => card.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defenderAfter = cards.Where(card => card.Effect?.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Contains("对方进攻后", StringComparison.Ordinal) == true)
            .Select(card => card.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var attackedAfter = cards.Where(card => card.Effect?.Contains("被进攻后", StringComparison.Ordinal) == true)
            .Select(card => card.Id).ToArray();

        Assert.True(kill.SetEquals(L12GameEngine.NativeCombatKillCards));
        Assert.True(attackerAfter.SetEquals(L12GameEngine.AttackerAfterAttackCards));
        Assert.True(defenderAfter.SetEquals(L12GameEngine.DefenderAfterAttackCards));
        Assert.Empty(attackedAfter);
    }
}
