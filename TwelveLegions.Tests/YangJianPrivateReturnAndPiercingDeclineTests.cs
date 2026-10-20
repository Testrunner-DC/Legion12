using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class YangJianPrivateReturnAndPiercingDeclineTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    public static IEnumerable<object[]> YangJianReturnCases()
    {
        foreach (var owner in new[] { 0, 1 })
        foreach (var direct in new[] { false, true })
        foreach (var destination in new[] { "top", "bottom" })
            yield return [owner, direct, destination];
    }

    [Theory]
    [MemberData(nameof(YangJianReturnCases))]
    public void YangJianPrivateReturnHidesIdentityAndChosenDestinationFromEveryPublicView(
        int ownerIndex, bool direct, string destination)
    {
        var game = CreateWithYangJian(ownerIndex, 202610200 + ownerIndex * 10 + (direct ? 2 : 0)
            + (destination == "bottom" ? 1 : 0));
        var owner = game.State.Players[ownerIndex];
        var opponentIndex = 1 - ownerIndex;
        var hidden = Card("S01-0003", $"yangjian-private-{ownerIndex}-{direct}-{destination}", ownerIndex);
        var drawn = Card("S01-0004", $"yangjian-drawn-{ownerIndex}-{direct}-{destination}", ownerIndex);
        var remaining = Card("S01-0005", $"yangjian-library-{ownerIndex}-{direct}-{destination}", ownerIndex);
        owner.Hand.Add(hidden);
        owner.Library.AddRange([drawn, remaining]);
        owner.Morale.Add(new L12MoraleCard
        {
            CardId = "S01-01C1",
            InstanceId = $"yangjian-morale-{ownerIndex}-{direct}-{destination}",
        });

        var activation = game.Handle(ownerIndex,
            new L12Command("activateAbility", $"master-{ownerIndex}", Ability: "drawCycle"));
        Assert.True(activation.Accepted, activation.Error);
        var cardPrompt = AdvanceToPrompt(game,
            prompt => prompt.Data.GetValueOrDefault("action") == "yangjian-return-card");
        Assert.True(cardPrompt.IsPrivate);
        Assert.Contains(hidden.InstanceId, cardPrompt.ValidChoices);
        AssertOwnerOnlyPrompt(game, ownerIndex, hidden);
        AssertNoPrivateTargetPreview(game, opponentIndex, hidden);

        if (direct)
        {
            var result = game.Handle(ownerIndex, new L12Command("resolvePrompt",
                PromptId: cardPrompt.PromptId, Choice: hidden.InstanceId, Destination: destination));
            Assert.True(result.Accepted, result.Error);
        }
        else
        {
            var select = game.Handle(ownerIndex, new L12Command("resolvePrompt",
                PromptId: cardPrompt.PromptId, Choice: hidden.InstanceId));
            Assert.True(select.Accepted, select.Error);
            var placePrompt = Assert.Single(game.State.PendingPrompts);
            Assert.True(placePrompt.IsPrivate);
            Assert.Equal("yangjian-return-place", placePrompt.Data.GetValueOrDefault("action"));
            Assert.Equal(["top", "bottom"], placePrompt.ValidChoices);

            // The selected hand instance is retained only in the authoritative continuation data.
            // Public stack/target previews must not expose that instance while placement is pending.
            AssertPrivatePromptHiddenFromPublic(game, ownerIndex, hidden);
            AssertNoPrivateTargetPreview(game, opponentIndex, hidden);

            var place = game.Handle(ownerIndex, new L12Command("resolvePrompt",
                PromptId: placePrompt.PromptId, Choice: destination));
            Assert.True(place.Accepted, place.Error);
        }

        Assert.DoesNotContain(hidden, owner.Hand);
        if (destination == "top")
        {
            Assert.Same(hidden, owner.Library[0]);
            Assert.Same(remaining, owner.Library[1]);
        }
        else
        {
            Assert.Same(remaining, owner.Library[0]);
            Assert.Same(hidden, owner.Library[^1]);
        }

        AssertYangJianViews(game, ownerIndex, hidden, destination);
        var restored = Restore(game);
        AssertYangJianViews(restored, ownerIndex, hidden, destination);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void DeclinedZhaoYunPiercingNeverOffersPuppetButRealPiercingDoes(
        int attackerIndex, bool activatePiercing)
    {
        var defenderIndex = 1 - attackerIndex;
        var game = CreateCombat(202610240 + attackerIndex * 2 + (activatePiercing ? 1 : 0), attackerIndex);
        var attacker = game.State.Players[attackerIndex];
        var defender = game.State.Players[defenderIndex];
        var zhaoYun = Card("ST01-01", $"zhaoyun-{attackerIndex}-{activatePiercing}", attackerIndex);
        var defeated = Card("ST01-05", $"zhaoyun-target-{attackerIndex}-{activatePiercing}", defenderIndex);
        var puppet = Card("S02-0005", $"zhaoyun-puppet-{attackerIndex}-{activatePiercing}", defenderIndex);
        var payment = new L12MoraleCard
        {
            CardId = "ST01-C1",
            InstanceId = $"zhaoyun-payment-{attackerIndex}-{activatePiercing}",
        };
        var spareMorale = new L12MoraleCard
        {
            CardId = "ST01-C1",
            InstanceId = $"zhaoyun-spare-{attackerIndex}-{activatePiercing}",
        };
        attacker.Field[0][0] = zhaoYun;
        attacker.Morale.AddRange([payment, spareMorale]);
        defender.Field[0][0] = defeated;
        defender.Hand.Add(puppet);

        var attack = game.Handle(attackerIndex, new L12Command("attack", zhaoYun.InstanceId,
            Target: new L12AttackTarget("legion", defeated.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);
        var decision = AdvanceToZhaoYunPiercingDecision(game, puppet.InstanceId);
        Assert.Equal(attackerIndex, decision.PlayerIndex);
        Assert.Equal(["mode:none", "mode:use"], decision.ValidChoices);
        Assert.Equal(1000, zhaoYun.Troops);
        Assert.Contains(defeated, defender.Resolving);
        Assert.Contains(puppet, defender.Hand);
        Assert.DoesNotContain(puppet, defender.Field.SelectMany(row => row));

        var mode = activatePiercing ? "mode:use" : "mode:none";
        var chooseMode = game.Handle(attackerIndex,
            new L12Command("resolvePrompt", PromptId: decision.PromptId, Choice: mode));
        Assert.True(chooseMode.Accepted, chooseMode.Error);

        if (!activatePiercing)
        {
            AdvanceDeclinedCombatToIdle(game, puppet.InstanceId);
            Assert.Contains(payment, attacker.Morale);
            Assert.DoesNotContain(payment, attacker.MoraleDeck);
            Assert.Contains(puppet, defender.Hand);
            Assert.DoesNotContain(puppet, defender.Field.SelectMany(row => row));
            Assert.Single(defender.Graveyard, card => card.InstanceId == defeated.InstanceId);
            Assert.DoesNotContain(game.State.Events, entry => entry.Type == "piercing");
            Assert.DoesNotContain(attacker.UsedAbilities,
                key => key.StartsWith($"starter-piercing:{zhaoYun.InstanceId}:", StringComparison.Ordinal));

            var restored = Restore(game);
            Assert.Null(restored.State.PendingDefense);
            Assert.Empty(restored.State.PendingPrompts);
            Assert.Contains(restored.State.Players[defenderIndex].Hand,
                card => card.InstanceId == puppet.InstanceId);
            Assert.DoesNotContain(restored.State.Events, entry => entry.Type == "piercing");
            return;
        }

        var paymentPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(payment.InstanceId, paymentPrompt.ValidChoices);
        var pay = game.Handle(attackerIndex, new L12Command("resolvePrompt",
            PromptId: paymentPrompt.PromptId, Choice: payment.InstanceId));
        Assert.True(pay.Accepted, pay.Error);

        var puppetWindow = AdvanceToPuppetWindow(game, puppet.InstanceId);
        Assert.Equal(defenderIndex, puppetWindow.PlayerIndex);
        Assert.Contains(puppet.InstanceId, puppetWindow.ValidChoices);
        var piercing = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal("master", piercing.Target.Type);
        Assert.True(piercing.SuppressAttackTriggers);
        Assert.Equal(zhaoYun.InstanceId, piercing.AttackerInstanceId);
        Assert.DoesNotContain(payment, attacker.Morale);
        Assert.Contains(payment, attacker.MoraleDeck);
        Assert.Contains(puppet, defender.Hand);
        Assert.DoesNotContain(puppet, defender.Field.SelectMany(row => row));
        Assert.Single(game.State.Events, entry => entry.Type == "piercing");
        Assert.Contains(attacker.UsedAbilities,
            key => key.StartsWith($"starter-piercing:{zhaoYun.InstanceId}:", StringComparison.Ordinal));

        var restoredAtRealWindow = Restore(game);
        var restoredPending = Assert.IsType<L12PendingDefense>(restoredAtRealWindow.State.PendingDefense);
        Assert.Equal("master", restoredPending.Target.Type);
        Assert.Contains(restoredAtRealWindow.State.PendingPrompts,
            prompt => prompt.Kind == "response" && prompt.ValidChoices.Contains(puppet.InstanceId));
        Assert.Contains(restoredAtRealWindow.State.Players[defenderIndex].Hand,
            card => card.InstanceId == puppet.InstanceId);
    }

    private static void AssertOwnerOnlyPrompt(L12GameEngine game, int ownerIndex, L12CardInstance hidden)
    {
        var ownerJson = JsonSerializer.Serialize(game.SnapshotFor(ownerIndex).Prompts, WireJson);
        Assert.Contains(hidden.InstanceId, ownerJson, StringComparison.Ordinal);
        AssertPrivatePromptHiddenFromPublic(game, ownerIndex, hidden);
    }

    private static void AssertPrivatePromptHiddenFromPublic(
        L12GameEngine game, int ownerIndex, L12CardInstance hidden)
    {
        foreach (var view in PublicViews(game, 1 - ownerIndex))
        {
            Assert.Empty(view.Prompts);
            var waiting = JsonSerializer.Serialize(view.WaitingPrompt, WireJson);
            Assert.DoesNotContain(hidden.InstanceId, waiting, StringComparison.Ordinal);
            Assert.DoesNotContain(hidden.CardId, waiting, StringComparison.Ordinal);
            Assert.DoesNotContain(hidden.Name, waiting, StringComparison.Ordinal);
        }
    }

    private static void AssertNoPrivateTargetPreview(
        L12GameEngine game, int opponentIndex, L12CardInstance hidden)
    {
        foreach (var view in PublicViews(game, opponentIndex))
        {
            var stackJson = JsonSerializer.Serialize(view.EffectStack, WireJson);
            Assert.DoesNotContain(hidden.InstanceId, stackJson, StringComparison.Ordinal);
            Assert.DoesNotContain(hidden.CardId, stackJson, StringComparison.Ordinal);
            Assert.DoesNotContain(hidden.Name, stackJson, StringComparison.Ordinal);
            foreach (var stackItem in JsonSerializer.SerializeToElement(view.EffectStack, WireJson).EnumerateArray())
            {
                Assert.Empty(stackItem.GetProperty("targets").EnumerateArray());
                Assert.Empty(stackItem.GetProperty("publicTargetIds").EnumerateArray());
            }
        }
    }

    private static void AssertYangJianViews(
        L12GameEngine game, int ownerIndex, L12CardInstance hidden, string destination)
    {
        var ownerEvent = Assert.Single(game.SnapshotFor(ownerIndex).RecentEvents,
            entry => entry.Type == "return" && entry.PlayerIndex == ownerIndex);
        Assert.Contains(hidden.Name, ownerEvent.Text, StringComparison.Ordinal);
        Assert.Contains(destination == "top" ? "顶部" : "底部", ownerEvent.Text, StringComparison.Ordinal);
        Assert.Single(ownerEvent.Cards, card => card.InstanceId == hidden.InstanceId);
        Assert.Null(ownerEvent.PlayerSelectedTargets);

        foreach (var view in PublicViews(game, 1 - ownerIndex))
        {
            var returned = Assert.Single(view.RecentEvents,
                entry => entry.Type == "return" && entry.PlayerIndex == ownerIndex);
            Assert.Equal("放回1张牌", returned.Text);
            Assert.Empty(returned.Cards);
            Assert.Null(returned.EffectText);
            Assert.Null(returned.PlayerSelectedTargets);
            var eventJson = JsonSerializer.Serialize(returned, WireJson);
            Assert.DoesNotContain(hidden.InstanceId, eventJson, StringComparison.Ordinal);
            Assert.DoesNotContain(hidden.CardId, eventJson, StringComparison.Ordinal);
            Assert.DoesNotContain(hidden.Name, eventJson, StringComparison.Ordinal);
            Assert.DoesNotContain("顶部", eventJson, StringComparison.Ordinal);
            Assert.DoesNotContain("底部", eventJson, StringComparison.Ordinal);
        }
        AssertNoPrivateTargetPreview(game, 1 - ownerIndex, hidden);
    }

    private static IEnumerable<L12GameSnapshot> PublicViews(L12GameEngine game, int opponentIndex)
    {
        yield return game.SnapshotFor(opponentIndex);
        yield return game.SnapshotForSpectator();
        yield return game.SnapshotForReferee();
    }

    private static L12Prompt AdvanceToPrompt(L12GameEngine game, Func<L12Prompt, bool> completed)
    {
        for (var guard = 0; guard < 80; guard++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (completed(prompt)) return prompt;
            Assert.Equal("response", prompt.Kind);
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        throw new Xunit.Sdk.XunitException("杨戬回牌提示未在限定步骤内出现");
    }

    private static L12Prompt AdvanceToZhaoYunPiercingDecision(L12GameEngine game, string puppetId)
    {
        for (var guard = 0; guard < 120; guard++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is not null)
            {
                Assert.DoesNotContain(puppetId, prompt.ValidChoices);
                if (prompt.SourceCardId == "ST01-01"
                    && prompt.ValidChoices.Contains("mode:none")
                    && prompt.ValidChoices.Contains("mode:use"))
                    return prompt;
                Assert.Equal("response", prompt.Kind);
                var pass = game.Handle(prompt.PlayerIndex,
                    new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
                Assert.True(pass.Accepted, pass.Error);
                continue;
            }
            if (game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice } pending)
            {
                var defense = game.Handle(1 - pending.AttackerPlayer,
                    new L12Command("resolveDefense", CardInstanceIds: []));
                Assert.True(defense.Accepted, defense.Error);
                continue;
            }
            throw new Xunit.Sdk.XunitException("真实赵云击杀未到达贯穿发动询问");
        }
        throw new Xunit.Sdk.XunitException("真实赵云击杀未在限定步骤内到达贯穿发动询问");
    }

    private static void AdvanceDeclinedCombatToIdle(L12GameEngine game, string puppetId)
    {
        for (var guard = 0; guard < 80; guard++)
        {
            Assert.False(game.State.PendingDefense?.Target.Type == "master",
                "选择不发动后不应建立贯穿主宰进攻");
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is not null)
            {
                Assert.DoesNotContain(puppetId, prompt.ValidChoices);
                Assert.Equal("response", prompt.Kind);
                var pass = game.Handle(prompt.PlayerIndex,
                    new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
                Assert.True(pass.Accepted, pass.Error);
                continue;
            }
            if (IsIdle(game)) return;
            throw new Xunit.Sdk.XunitException("选择不发动后战斗停在无合法推进入口的状态");
        }
        throw new Xunit.Sdk.XunitException("选择不发动后战斗未在限定步骤内清空");
    }

    private static L12Prompt AdvanceToPuppetWindow(L12GameEngine game, string puppetId)
    {
        for (var guard = 0; guard < 80; guard++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is not null)
            {
                if (prompt.Kind == "response" && prompt.ValidChoices.Contains(puppetId))
                {
                    Assert.Equal("master", game.State.PendingDefense?.Target.Type);
                    return prompt;
                }
                var choice = prompt.Kind == "response" ? "pass"
                    : throw new Xunit.Sdk.XunitException($"贯穿结算出现未预期提示：{prompt.Kind}/{prompt.Text}");
                var result = game.Handle(prompt.PlayerIndex,
                    new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
                Assert.True(result.Accepted, result.Error);
                continue;
            }
            throw new Xunit.Sdk.XunitException("真实贯穿未建立戏法师傀儡响应窗口");
        }
        throw new Xunit.Sdk.XunitException("真实贯穿未在限定步骤内建立戏法师傀儡响应窗口");
    }

    private static bool IsIdle(L12GameEngine game)
        => game.State.PendingDefense is null
            && game.State.PendingPrompts.Count == 0
            && game.State.PendingActivations.Count == 0
            && game.State.EffectStack.Count == 0
            && game.State.DeferredEffectStack.Count == 0
            && game.State.PendingTriggerBatches.Count == 0
            && game.State.PendingTriggerStackCandidates.Count == 0;

    private static L12GameEngine CreateWithYangJian(int ownerIndex, int seed)
    {
        var basis = Catalog.DeckAt(0);
        var decks = new[] { basis, basis };
        decks[ownerIndex] = new L12PresetDeckDefinition
        {
            Name = "杨戬私密回牌回归",
            MasterId = "S01-01M1",
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "yangjian-private-return", "PRIVATE", seed,
            ["甲", "乙"], decks, skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        ResetForScenario(game, ownerIndex);
        return game;
    }

    private static L12GameEngine CreateCombat(int seed, int attackerIndex)
    {
        var basis = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "zhaoyun-piercing-decline", "PIERCE", seed,
            ["甲", "乙"], [basis, basis], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        ResetForScenario(game, attackerIndex);
        return game;
    }

    private static void ResetForScenario(L12GameEngine game, int activePlayer)
    {
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Removed.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
            player.Hp = 8;
        }
        game.State.PendingPrompts.Clear();
        game.State.PendingActivations.Clear();
        game.State.EffectStack.Clear();
        game.State.DeferredEffectStack.Clear();
        game.State.PendingTriggerBatches.Clear();
        game.State.PendingTriggerStackCandidates.Clear();
        game.State.SuspendedCombatContexts.Clear();
        game.State.Events.Clear();
        game.State.AuthorityEvents.Clear();
        game.State.Log.Clear();
        game.State.LastAction = null;
        game.State.ResponseWindow = null;
        game.State.ActivePlayer = activePlayer;
        game.State.FirstPlayer = activePlayer;
        game.State.Round = 2;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        game.State.DisasterValue = 0;
        game.State.ActiveDisaster = null;
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
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
            OwnerIndex = ownerIndex,
        };
    }
}
