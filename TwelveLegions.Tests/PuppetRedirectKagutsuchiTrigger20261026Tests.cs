using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PuppetRedirectKagutsuchiTrigger20261026Tests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(
        Path.Combine(AppContext.BaseDirectory, "Data"));

    private sealed record RedirectResult(L12GameEngine Game, int AttackerKagutsuchiPrompts,
        int DefenderKagutsuchiPrompts, string PuppetId, string DefenderMoraleId);

    private static L12GameEngine Create(int attackerIndex, int seed, bool attackerUsesKagutsuchi = false)
    {
        var basis = Catalog.DeckAt(0);
        L12PresetDeckDefinition Deck(string masterId) => new()
        {
            Name = "傀儡改向后迦具土时点回归",
            MasterId = masterId,
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var defenderIndex = 1 - attackerIndex;
        var decks = new[] { Deck("S01-01M1"), Deck("S01-01M1") };
        decks[defenderIndex] = Deck("ST04-M1");
        if (attackerUsesKagutsuchi) decks[attackerIndex] = Deck("ST04-M1");
        var game = new L12GameEngine(Catalog, "puppet-redirect-kagutsuchi", "PUPPET-KAGUTSUCHI",
            seed, ["甲", "乙"], decks, skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        game.State.ActivePlayer = attackerIndex;
        game.State.FirstPlayer = attackerIndex;
        game.State.Round = 2;
        game.State.TurnSerial = 9;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        game.State.DisasterDeck.Clear();
        foreach (var player in game.State.Players)
        {
            player.Hp = 10;
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner, int? troops = null)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            CardId = definition.Id,
            InstanceId = instanceId,
            OwnerIndex = owner,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static L12MoraleCard Morale(string instanceId)
        => new() { CardId = "ST04-C1", InstanceId = instanceId };

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

    private static void Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static bool IsKagutsuchiModePrompt(L12Prompt prompt)
        => prompt.Continuation == "pending-activation"
            && prompt.ValidChoices.Contains("mode:none")
            && (prompt.ValidChoices.Contains("mode:morale")
                || prompt.ValidChoices.Contains("mode:discard"));

    private static L12GameEngine BeginRedirectWithFollowup(int attackerIndex, bool restore,
        bool support, bool mercenary, bool counter = false)
    {
        var defenderIndex = 1 - attackerIndex;
        var game = Create(attackerIndex, 281001 + attackerIndex);
        game.State.Players[attackerIndex].Field[0][0] = Card("S01-0003", "flow-attacker", attackerIndex);
        game.State.Players[defenderIndex].Hand.Add(Card("S02-0005", "flow-puppet", defenderIndex));
        if (mercenary)
            game.State.Players[defenderIndex].Hand.Add(Card("S01-0002", "flow-mercenary", defenderIndex));
        if (counter)
        {
            var response = Card("S01-0020", "flow-counter", defenderIndex);
            response.Hidden = true;
            game.State.Players[defenderIndex].Field[1][2] = response;
        }
        if (support)
        {
            var supporter = Card("S01-0002", "flow-supporter", defenderIndex, troops: 9000);
            game.State.Players[defenderIndex].Field[1][0] = supporter;
        }
        var attack = game.Handle(attackerIndex, new L12Command("attack", "flow-attacker",
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        for (var guard = 0; guard < 30; guard++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.ValidChoices.Contains("flow-puppet"))
            {
                Resolve(game, "flow-puppet");
                Assert.Equal("stack-response-puppet-slot", Assert.Single(game.State.PendingPrompts).Continuation);
                Resolve(game, "0:0");
                return restore ? Restore(game) : game;
            }
            Resolve(game, IsKagutsuchiModePrompt(prompt) ? "mode:none" : "pass");
        }
        throw new Xunit.Sdk.XunitException("未到达傀儡改向声明");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "ruling:puppet-redirect-keeps-opponent-attack-responses")]
    public void RedirectedAttackOffersNewlyLegalMercenaryBeforeDefense(int attackerIndex, bool restore)
    {
        var defenderIndex = 1 - attackerIndex;
        var game = BeginRedirectWithFollowup(attackerIndex, restore, support: false, mercenary: true);
        for (var guard = 0; guard < 60 && game.State.PendingPrompts.Count > 0; guard++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (game.State.PendingDefense?.Target.InstanceId == "flow-puppet"
                && prompt.ValidChoices.Contains("flow-mercenary"))
            {
                Assert.Equal(defenderIndex, prompt.PlayerIndex);
                Assert.Contains(game.State.EffectStack, item => item.Trigger == "opponent-attack");
                if (restore)
                {
                    game = Restore(game);
                    Assert.Contains("flow-mercenary", Assert.Single(game.State.PendingPrompts).ValidChoices);
                }
                Resolve(game, "flow-mercenary");
                for (var finish = 0; finish < 60 && game.State.PendingPrompts.Count > 0; finish++)
                    Resolve(game, IsKagutsuchiModePrompt(game.State.PendingPrompts[0]) ? "mode:none" : "pass");
                Assert.Null(game.State.PendingDefense);
                Assert.Contains(game.State.Players[defenderIndex].Graveyard,
                    card => card.InstanceId == "flow-mercenary");
                Assert.Contains(game.State.Players[defenderIndex].Field[0],
                    card => card?.InstanceId == "flow-puppet");
                Assert.Equal(10, game.State.Players[defenderIndex].Hp);
                return;
            }
            Resolve(game, IsKagutsuchiModePrompt(prompt) ? "mode:none" : "pass");
        }
        Assert.Fail("傀儡改向后跳过了新合法的佣兵响应机会");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "ruling:puppet-redirect-keeps-support-choice")]
    public void RedirectedAttackKeepsLegalSupportAndSettlesOnlyOnce(int attackerIndex, bool restore)
    {
        var defenderIndex = 1 - attackerIndex;
        var game = BeginRedirectWithFollowup(attackerIndex, restore, support: true, mercenary: false);
        for (var guard = 0; guard < 60 && game.State.PendingPrompts.Count > 0; guard++)
            Resolve(game, IsKagutsuchiModePrompt(game.State.PendingPrompts[0]) ? "mode:none" : "pass");
        Assert.Equal(L12CombatStage.DefenseChoice, game.State.PendingDefense?.Stage);
        Assert.Equal("flow-puppet", game.State.PendingDefense?.Target.InstanceId);
        var combatId = game.State.PendingDefense!.CombatId;
        var result = game.Handle(defenderIndex, new L12Command("resolveDefense",
            CardInstanceIds: ["flow-supporter"]));
        Assert.True(result.Accepted, result.Error);
        for (var guard = 0; guard < 60 && game.State.PendingPrompts.Count > 0; guard++)
            Resolve(game, IsKagutsuchiModePrompt(game.State.PendingPrompts[0]) ? "mode:none" : "pass");
        Assert.Null(game.State.PendingDefense);
        Assert.Contains(game.State.Players[defenderIndex].Field[0], card => card?.InstanceId == "flow-puppet");
        Assert.Contains(game.State.Players[defenderIndex].Graveyard, card => card.InstanceId == "flow-supporter");
        Assert.Equal(10, game.State.Players[defenderIndex].Hp);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "support-skipped");
        Assert.Single(game.State.Events, entry => entry.PlayerCombat is { EventKind: "attack", OutcomeCode: "declared" } combat
            && combat.CombatId == combatId);
        Assert.Single(game.State.Events, entry => entry.PlayerCombat is { EventKind: "support", OutcomeCode: "supported" } combat
            && combat.CombatId == combatId);
        Assert.False(game.Handle(defenderIndex, new L12Command("resolveDefense",
            CardInstanceIds: ["flow-supporter"])).Accepted);
        Assert.False(string.IsNullOrEmpty(combatId));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "ruling:puppet-redirect-keeps-other-attack-effects-and-support")]
    public void RedirectedAttackAllowsCounterEffectThenSupport(int attackerIndex, bool restore)
    {
        var defenderIndex = 1 - attackerIndex;
        var game = BeginRedirectWithFollowup(attackerIndex, restore,
            support: true, mercenary: false, counter: true);
        var activated = false;
        for (var guard = 0; guard < 80 && game.State.PendingPrompts.Count > 0; guard++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (!activated && game.State.PendingDefense?.Target.InstanceId == "flow-puppet"
                && prompt.PlayerIndex == defenderIndex && prompt.ValidChoices.Contains("flow-counter"))
            {
                Resolve(game, "flow-counter");
                activated = true;
            }
            else Resolve(game, IsKagutsuchiModePrompt(prompt) ? "mode:none" : "pass");
        }
        Assert.True(activated, "改向后漏掉其他【对方进攻时】效果");
        Assert.Equal(2000, game.State.Players[defenderIndex].Field[0][0]?.Troops);
        Assert.Equal(L12CombatStage.DefenseChoice, game.State.PendingDefense?.Stage);
        Assert.True(game.Handle(defenderIndex, new L12Command("resolveDefense",
            CardInstanceIds: ["flow-supporter"])).Accepted);
        for (var guard = 0; guard < 60 && game.State.PendingPrompts.Count > 0; guard++)
            Resolve(game, IsKagutsuchiModePrompt(game.State.PendingPrompts[0]) ? "mode:none" : "pass");
        Assert.Null(game.State.PendingDefense);
        Assert.Contains(game.State.Players[defenderIndex].Field[0], card => card?.InstanceId == "flow-puppet");
        Assert.Single(game.State.Players[defenderIndex].Graveyard, card => card.InstanceId == "flow-counter");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "boundary:puppet-redirect-without-support-finishes-once")]
    public void RedirectedAttackWithoutLegalSupportFinishesOnce(int attackerIndex, bool restore)
    {
        var defenderIndex = 1 - attackerIndex;
        var game = BeginRedirectWithFollowup(attackerIndex, restore, support: false, mercenary: false);
        for (var guard = 0; guard < 60 && game.State.PendingPrompts.Count > 0; guard++)
            Resolve(game, IsKagutsuchiModePrompt(game.State.PendingPrompts[0]) ? "mode:none" : "pass");
        Assert.Null(game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Single(game.State.Players[defenderIndex].Graveyard, card => card.InstanceId == "flow-puppet");
        Assert.Single(game.State.Events, entry => entry.Type == "support-skipped");
        Assert.Single(game.State.Events, entry => entry.PlayerCombat?.EventKind == "attack"
            && entry.PlayerCombat.OutcomeCode == "declared");
        Assert.Equal(10, game.State.Players[defenderIndex].Hp);
    }

    private static RedirectResult RunPuppetRedirect(int attackerIndex, bool restoreAfterPuppetCommit,
        bool attackerUsesKagutsuchi)
    {
        var defenderIndex = 1 - attackerIndex;
        var game = Create(attackerIndex,
            26102640 + attackerIndex * 10 + (restoreAfterPuppetCommit ? 1 : 0)
            + (attackerUsesKagutsuchi ? 2 : 0), attackerUsesKagutsuchi);
        var attacker = Card("S01-0003", $"redirect-attacker-{attackerIndex}", attackerIndex);
        var puppet = Card("S02-0005", $"redirect-puppet-{defenderIndex}", defenderIndex);
        var attackerMorale = Morale($"redirect-attacker-morale-{attackerIndex}");
        var defenderMorale = Morale($"redirect-defender-morale-{defenderIndex}");
        game.State.Players[attackerIndex].Field[0][0] = attacker;
        game.State.Players[attackerIndex].Morale.Add(attackerMorale);
        game.State.Players[defenderIndex].Hand.Add(puppet);
        game.State.Players[defenderIndex].Morale.Add(defenderMorale);

        var attack = game.Handle(attackerIndex, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);

        var promptCounts = new int[2];
        var foundPuppetWindow = false;
        for (var safety = 0; safety < 80; safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (IsKagutsuchiModePrompt(prompt))
            {
                promptCounts[prompt.PlayerIndex]++;
                Assert.Equal(attackerIndex, prompt.PlayerIndex);
                Resolve(game, "mode:none");
                continue;
            }
            Assert.Equal("response", prompt.Kind);
            if (prompt.ValidChoices.Contains(puppet.InstanceId))
            {
                Assert.Equal(defenderIndex, prompt.PlayerIndex);
                Resolve(game, puppet.InstanceId);
                foundPuppetWindow = true;
                break;
            }
            Resolve(game, "pass");
        }
        Assert.True(foundPuppetWindow, "合法主宰进攻未到达傀儡响应窗口");

        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("stack-response-puppet-slot", slotPrompt.Continuation);
        Assert.Contains("0:0", slotPrompt.ValidChoices);
        Resolve(game, "0:0");
        Assert.Equal("response-retarget-master", game.State.EffectStack[^1].Trigger);
        if (restoreAfterPuppetCommit) game = Restore(game);

        var defenderCommittedKagutsuchi = false;
        for (var safety = 0; safety < 160; safety++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                if (IsKagutsuchiModePrompt(prompt))
                {
                    promptCounts[prompt.PlayerIndex]++;
                    if (prompt.PlayerIndex == defenderIndex && !defenderCommittedKagutsuchi)
                    {
                        defenderCommittedKagutsuchi = true;
                        Resolve(game, "mode:morale");
                    }
                    else
                        Resolve(game, "mode:none");
                    continue;
                }
                if (prompt.ValidChoices.Contains(defenderMorale.InstanceId))
                {
                    Assert.Equal(defenderIndex, prompt.PlayerIndex);
                    Resolve(game, defenderMorale.InstanceId);
                    continue;
                }
                Assert.Equal("response", prompt.Kind);
                Resolve(game, "pass");
                continue;
            }
            if (game.State.PendingDefense is null
                || game.State.PendingDefense.Stage == L12CombatStage.DefenseChoice) break;
            Assert.Fail("傀儡改向后的战斗未到达迦具土机会或防守选择安全边界");
        }

        return new(game, promptCounts[attackerIndex], promptCounts[defenderIndex],
            puppet.InstanceId, defenderMorale.InstanceId);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "bug:BUG-20261024-646ce6f1")]
    [Trait("L12Evidence", "ruling:puppet-redirect-opens-kagutsuchi-defended-legion-timing")]
    [Trait("L12Evidence", "reconnect:v2-puppet-redirect-kagutsuchi")]
    public void PuppetRedirectToLegionOffersDefendersKagutsuchiTiming(
        int attackerIndex, bool restoreAfterPuppetCommit)
    {
        var defenderIndex = 1 - attackerIndex;
        var result = RunPuppetRedirect(attackerIndex, restoreAfterPuppetCommit,
            attackerUsesKagutsuchi: false);

        Assert.Equal(0, result.AttackerKagutsuchiPrompts);
        Assert.Equal(1, result.DefenderKagutsuchiPrompts);
        Assert.Contains(result.Game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("成为本次进攻目标", StringComparison.Ordinal)
            && entry.Cards.Any(card => card.InstanceId == result.PuppetId));
        Assert.Contains(result.Game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("本回合兵力+2000", StringComparison.Ordinal)
            && entry.Cards.Any(card => card.InstanceId == result.PuppetId));
        Assert.True(result.Game.State.Players[defenderIndex].Morale
            .Single(card => card.InstanceId == result.DefenderMoraleId).Tapped);
        Assert.Contains($"trigger:starter-kagutsuchi:{result.Game.State.TurnSerial}",
            result.Game.State.Players[defenderIndex].UsedAbilities);
    }

    [Fact]
    [Trait("L12Evidence", "control:master-attack-without-puppet-does-not-trigger-defender-kagutsuchi")]
    public void MasterAttackWithoutPuppetDoesNotOfferDefendersKagutsuchiTiming()
    {
        const int attackerIndex = 0;
        const int defenderIndex = 1;
        var game = Create(attackerIndex, 26102660);
        var attacker = Card("S01-0003", "no-puppet-attacker", attackerIndex);
        game.State.Players[attackerIndex].Field[0][0] = attacker;
        game.State.Players[defenderIndex].Morale.Add(Morale("no-puppet-defender-morale"));

        var attack = game.Handle(attackerIndex, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        var kagutsuchiPrompts = 0;
        for (var safety = 0; safety < 80 && game.State.PendingDefense is not null; safety++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                if (IsKagutsuchiModePrompt(prompt))
                {
                    kagutsuchiPrompts++;
                    Resolve(game, "mode:none");
                }
                else
                {
                    Assert.Equal("response", prompt.Kind);
                    Resolve(game, "pass");
                }
                continue;
            }
            if (game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice })
            {
                var defense = game.Handle(defenderIndex,
                    new L12Command("resolveDefense", CardInstanceIds: []));
                Assert.True(defense.Accepted, defense.Error);
                continue;
            }
            Assert.Fail("无傀儡主宰进攻停在不可推进状态");
        }

        Assert.Equal(0, kagutsuchiPrompts);
        Assert.DoesNotContain($"trigger:starter-kagutsuchi:{game.State.TurnSerial}",
            game.State.Players[defenderIndex].UsedAbilities);
    }

    [Fact]
    [Trait("L12Evidence", "regression:kagutsuchi-unpayable-keeps-ordinary-defender-response")]
    public void OrdinaryLegionAttackKeepsDefenderResponseWindowWhenKagutsuchiCannotPay()
    {
        const int attackerIndex = 0;
        const int defenderIndex = 1;
        var game = Create(attackerIndex, 26102661);
        var attacker = Card("S01-0003", "unpayable-kagutsuchi-attacker", attackerIndex);
        var target = Card("ST04-03", "unpayable-kagutsuchi-target", defenderIndex);
        game.State.Players[attackerIndex].Field[0][0] = attacker;
        game.State.Players[defenderIndex].Field[0][0] = target;

        Assert.Empty(game.State.Players[defenderIndex].Hand);
        Assert.Empty(game.State.Players[defenderIndex].Morale);
        var attack = game.Handle(attackerIndex, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);

        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", response.Kind);
        Assert.Equal(attackerIndex, response.PlayerIndex);
        Assert.Contains("pass", response.ValidChoices);
        var ordinaryTiming = Assert.Single(game.State.EffectStack);
        Assert.Equal("opponent-attack", ordinaryTiming.Trigger);
        Assert.Equal("defender-attack", ordinaryTiming.Data["combatTiming"]);
        Assert.Equal(L12CombatStage.DefenderAttackTiming, game.State.PendingDefense?.Stage);
        Assert.Contains(game.State.Events, entry => entry.Type == "combat-stage"
            && entry.Text.Contains("防守方取得【对方进攻时】响应时点", StringComparison.Ordinal));

        Resolve(game, "pass");
        response = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", response.Kind);
        Assert.Equal(defenderIndex, response.PlayerIndex);
        Assert.Contains("pass", response.ValidChoices);
        Assert.Same(ordinaryTiming, Assert.Single(game.State.EffectStack));

        Resolve(game, "pass");
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.DoesNotContain(game.State.EffectStack, item => item.StackItemId == ordinaryTiming.StackItemId);
    }

    [Fact]
    [Trait("L12Evidence", "boundary:attacking-kagutsuchi-does-not-repeat-after-puppet-redirect")]
    public void PuppetRedirectAddsDefenderTimingWithoutRepeatingAttackersKagutsuchiTiming()
    {
        const int attackerIndex = 0;
        const int defenderIndex = 1;
        var result = RunPuppetRedirect(attackerIndex, restoreAfterPuppetCommit: false,
            attackerUsesKagutsuchi: true);

        Assert.Equal(1, result.AttackerKagutsuchiPrompts);
        Assert.Equal(1, result.DefenderKagutsuchiPrompts);
        Assert.Contains(result.Game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("成为本次进攻目标", StringComparison.Ordinal)
            && entry.Cards.Any(card => card.InstanceId == result.PuppetId));
        Assert.Contains($"trigger:starter-kagutsuchi:{result.Game.State.TurnSerial}",
            result.Game.State.Players[defenderIndex].UsedAbilities);
    }
}
