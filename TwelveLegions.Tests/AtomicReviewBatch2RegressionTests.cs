using TwelveLegions.Server;
using System.Text.Json;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch2RegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
        => new(Catalog, "atomic-review-batch2", "ATOMIC2", seed, ["甲", "乙"], [0, 0], skipPreparation: true);

    private static L12GameEngine CreateWithFirstMaster(string masterId, int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = $"{masterId}原子审查牌库",
            MasterId = masterId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        return new L12GameEngine(Catalog, "atomic-review-batch2", "ATOMIC2", seed,
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

    private static void AddReadyMorale(L12PlayerState player, int count)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"atomic2-morale-{player.PlayerIndex}-{index}",
                CardId = "S01-01C1",
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

    private static void HoldOpponentResponseWindow(L12GameEngine game, int actingPlayer = 0)
    {
        var opponent = game.State.Players[1 - actingPlayer];
        var counter = Card("S01-0019", $"atomic2-response-{actingPlayer}-{game.State.StackSequence}");
        counter.Hidden = true;
        counter.SetRound = 0;
        opponent.Field[1][2] = counter;
        opponent.Field[0][2] ??= Card("S01-0004", $"atomic2-response-target-{actingPlayer}-{game.State.StackSequence}");
    }

    private static (L12PlayerState Player, L12CardInstance Trial) PrepareCrusade(L12GameEngine game, int runes)
    {
        var player = game.State.Players[0];
        var trial = Card("S02-06S6", $"crusade-trial-{game.State.TurnSerial}-{runes}");
        trial.TrialCompleted = true;
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.Trials.Add(trial);
        player.SpecialZones.Runes = runes;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        return (player, trial);
    }

    [Fact]
    [Trait("L12Evidence", "ability:cleopatraGuard")]
    public void CleopatraDeclaresGuardAndSlotBeforePaymentAndCancellationIsFree()
    {
        var game = Create(6801);
        var player = game.State.Players[0];
        var cleopatra = Card("S01-0214", "cleopatra-public-entry");
        var guard = Card("S01-0212", "cleopatra-grave-guard");
        player.Morale.Clear();
        player.Field[0][0] = cleopatra;
        player.Graveyard.Add(guard);
        AddReadyMorale(player, 1);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", cleopatra.InstanceId,
            Ability: "cleopatraGuard")).Accepted);
        var guardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.False(cleopatra.Tapped);
        Assert.False(Assert.Single(player.Morale).Tapped);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: guardPrompt.PromptId,
            Choice: guard.InstanceId)).Accepted);
        var cancelledSlot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("slot", cancelledSlot.Kind);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: cancelledSlot.PromptId,
            Choice: "skip")).Accepted);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(guard, player.Graveyard);
        Assert.False(cleopatra.Tapped);
        Assert.False(Assert.Single(player.Morale).Tapped);

        Assert.True(game.Handle(0, new L12Command("activateAbility", cleopatra.InstanceId,
            Ability: "cleopatraGuard")).Accepted);
        guardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: guardPrompt.PromptId,
            Choice: guard.InstanceId)).Accepted);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId,
            Choice: "1:2")).Accepted);

        Assert.True(cleopatra.Tapped);
        Assert.True(Assert.Single(player.Morale).Tapped);
        PassResponses(game);
        Assert.Same(guard, player.Field[1][2]);
        Assert.DoesNotContain(guard, player.Graveyard);
    }

    [Fact]
    [Trait("L12Evidence", "ability:palaceExchange")]
    public void PalaceDeclaresReviveSlotAndEnemyBeforeReturningMorale()
    {
        var game = CreateWithFirstMaster("S01-01D1", 6802);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var revive = Card("S01-0114", "palace-revive");
        var enemyTarget = Card("S01-0003", "palace-enemy");
        player.Graveyard.Clear();
        player.Morale.Clear();
        player.Graveyard.Add(revive);
        enemy.Field[0][1] = enemyTarget;
        AddReadyMorale(player, enemyTarget.CurrentCost);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "palaceExchange")).Accepted);
        var revivePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: revivePrompt.PromptId,
            Choice: revive.InstanceId)).Accepted);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("slot", slotPrompt.Kind);
        Assert.False(player.MasterTapped);
        Assert.Equal(enemyTarget.CurrentCost, player.Morale.Count);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId,
            Choice: "1:2")).Accepted);
        var enemyPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(enemyTarget.InstanceId, enemyPrompt.ValidChoices);
        Assert.False(player.MasterTapped);
        Assert.Equal(enemyTarget.CurrentCost, player.Morale.Count);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: enemyPrompt.PromptId,
            Choice: enemyTarget.InstanceId)).Accepted);
        PassResponses(game);

        Assert.True(player.MasterTapped);
        Assert.Equal(enemyTarget.CurrentCost, player.ReturnedMoraleThisTurn);
        Assert.True(Assert.Single(player.Morale).Tapped);
        Assert.Contains(enemyTarget, enemy.Graveyard);
        Assert.Same(revive, player.Field[1][2]);
        Assert.DoesNotContain(revive, player.Graveyard);
    }

    [Fact]
    [Trait("L12Evidence", "ability:yomiSweep")]
    public void YomiDeclaresBothTargetsBeforePaymentAndUsesFourResponseSegments()
    {
        var game = CreateWithFirstMaster("S01-04D1", 6803);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var killThree = Card("S01-0003", "yomi-cost-three");
        var killOneAfterDebuff = Card("S01-0114", "yomi-cost-two");
        var response = Card("S01-0019", "yomi-response-window");
        response.Hidden = true;
        response.SetRound = 0;
        player.Morale.Clear();
        AddReadyMorale(player, 2);
        enemy.Field[0][0] = killThree;
        enemy.Field[0][1] = killOneAfterDebuff;
        enemy.Field[1][0] = response;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "yomiSweep")).Accepted);
        var firstTarget = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(killThree.InstanceId, firstTarget.ValidChoices);
        Assert.Equal(2, player.Morale.Count(card => !card.Tapped));
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: firstTarget.PromptId,
            Choice: killThree.InstanceId)).Accepted);
        var secondTarget = Assert.Single(game.State.PendingPrompts);
        Assert.DoesNotContain(killThree.InstanceId, secondTarget.ValidChoices);
        Assert.Contains(killOneAfterDebuff.InstanceId, secondTarget.ValidChoices);
        Assert.Equal(2, player.Morale.Count(card => !card.Tapped));
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: secondTarget.PromptId,
            Choice: killOneAfterDebuff.InstanceId)).Accepted);
        Assert.Equal(0, player.Morale.Count(card => !card.Tapped));

        var flows = new List<string>();
        for (var safety = 0; safety < 20 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var flow = game.State.EffectStack[^1].Data.GetValueOrDefault("atomicFlow", string.Empty);
            if (flows.Count == 0 || flows[^1] != flow) flows.Add(flow);
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }

        Assert.Equal(["yomi-draw", "yomi-cost-debuff", "yomi-kill3", "yomi-kill1"], flows);
        Assert.Contains(killThree, enemy.Graveyard);
        Assert.Contains(killOneAfterDebuff, enemy.Graveyard);
    }

    [Fact]
    [Trait("L12Evidence", "ability:yomiSweep")]
    public void YomiInvalidKillThreeSegmentDoesNotBlockKillOneSegment()
    {
        var game = CreateWithFirstMaster("S01-04D1", 6804);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var invalidated = Card("S01-0003", "yomi-invalidated-three");
        var survivingTarget = Card("S01-0114", "yomi-surviving-one");
        var response = Card("S01-0019", "yomi-independent-response");
        response.Hidden = true;
        response.SetRound = 0;
        player.Morale.Clear();
        AddReadyMorale(player, 2);
        enemy.Field[0][0] = invalidated;
        enemy.Field[0][1] = survivingTarget;
        enemy.Field[1][0] = response;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "yomiSweep")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: invalidated.InstanceId)).Accepted);
        prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: survivingTarget.InstanceId)).Accepted);

        enemy.Field[0][0] = null;
        enemy.Graveyard.Add(invalidated);
        var flows = new List<string>();
        for (var safety = 0; safety < 20 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var flow = game.State.EffectStack[^1].Data.GetValueOrDefault("atomicFlow", string.Empty);
            if (flows.Count == 0 || flows[^1] != flow) flows.Add(flow);
            prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }

        Assert.Equal(["yomi-draw", "yomi-cost-debuff", "yomi-kill1"], flows);
        Assert.Contains(survivingTarget, enemy.Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("费用不高于3", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:yomiRecover")]
    public void YomiRecoverCancellationAndInvalidationAreFreeAndSuccessUsesAuthorityEvent()
    {
        var cancelGame = CreateWithFirstMaster("S01-04D1", 6805);
        var cancelPlayer = cancelGame.State.Players[0];
        var cancelCard = Card("S01-0401", "yomi-cancel-recover");
        cancelPlayer.Graveyard.Add(cancelCard);
        cancelGame.State.ActivePlayer = 0;
        cancelGame.State.Phase = L12Phase.Main;
        Assert.True(cancelGame.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "yomiRecover")).Accepted);
        var prompt = Assert.Single(cancelGame.State.PendingPrompts);
        Assert.True(cancelGame.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "skip")).Accepted);
        Assert.False(cancelPlayer.MasterTapped);
        Assert.Contains(cancelCard, cancelPlayer.Graveyard);
        Assert.Empty(cancelGame.State.EffectStack);

        var invalidGame = CreateWithFirstMaster("S01-04D1", 6806);
        var invalidPlayer = invalidGame.State.Players[0];
        var invalidCard = Card("S01-0401", "yomi-invalid-recover");
        invalidPlayer.Graveyard.Add(invalidCard);
        invalidGame.State.ActivePlayer = 0;
        invalidGame.State.Phase = L12Phase.Main;
        Assert.True(invalidGame.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "yomiRecover")).Accepted);
        prompt = Assert.Single(invalidGame.State.PendingPrompts);
        invalidPlayer.Graveyard.Remove(invalidCard);
        Assert.True(invalidGame.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: invalidCard.InstanceId)).Accepted);
        Assert.False(invalidPlayer.MasterTapped);
        Assert.Empty(invalidGame.State.EffectStack);
        Assert.Contains(invalidGame.State.Events, entry => entry.Type == "ability-rejected");

        var successGame = CreateWithFirstMaster("S01-04D1", 6807);
        var successPlayer = successGame.State.Players[0];
        var successCard = Card("S01-0401", "yomi-authority-recover");
        successPlayer.Graveyard.Add(successCard);
        successGame.State.ActivePlayer = 0;
        successGame.State.Phase = L12Phase.Main;
        Assert.True(successGame.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "yomiRecover")).Accepted);
        prompt = Assert.Single(successGame.State.PendingPrompts);
        Assert.True(successGame.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: successCard.InstanceId)).Accepted);
        PassResponses(successGame);
        Assert.Contains(successCard, successPlayer.Hand);
        Assert.Contains(successGame.State.AuthorityEvents, entry => entry.Type == "effect-hand-add"
            && entry.TargetInstanceId == successCard.InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "ability:crusadeRecover")]
    [Trait("L12Evidence", "ability:crusadeRichardPiercing")]
    public void CrusadeCancellationInvalidTargetAndInsufficientRunesNeverPartiallyPay()
    {
        var cancelGame = Create(6810);
        var (cancelPlayer, cancelTrial) = PrepareCrusade(cancelGame, 2);
        var discard = Card("S01-0003", "crusade-cancel-discard");
        var recover = Card("S02-0601", "crusade-cancel-recover");
        cancelPlayer.Hand.Clear();
        cancelPlayer.Graveyard.Clear();
        cancelPlayer.Hand.Add(discard);
        cancelPlayer.Graveyard.Add(recover);
        Assert.True(cancelGame.Handle(0, new L12Command("activateAbility", cancelTrial.InstanceId,
            Ability: "crusadeRecover")).Accepted);
        var prompt = Assert.Single(cancelGame.State.PendingPrompts);
        Assert.True(cancelGame.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "skip")).Accepted);
        Assert.Equal(2, cancelPlayer.SpecialZones.Runes);
        Assert.Contains(discard, cancelPlayer.Hand);
        Assert.Contains(recover, cancelPlayer.Graveyard);
        Assert.Empty(cancelGame.State.EffectStack);

        var invalidGame = Create(6811);
        var (invalidPlayer, invalidTrial) = PrepareCrusade(invalidGame, 2);
        var richard = Card("S02-0608", "crusade-invalid-richard");
        invalidPlayer.Field[0][0] = richard;
        Assert.True(invalidGame.Handle(0, new L12Command("activateAbility", invalidTrial.InstanceId,
            Ability: "crusadeRichardPiercing")).Accepted);
        prompt = Assert.Single(invalidGame.State.PendingPrompts);
        invalidPlayer.Field[0][0] = null;
        Assert.True(invalidGame.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: richard.InstanceId)).Accepted);
        Assert.Equal(2, invalidPlayer.SpecialZones.Runes);
        Assert.Empty(invalidGame.State.EffectStack);
        Assert.Contains(invalidGame.State.Events, entry => entry.Type == "ability-rejected");

        var runeGame = Create(6812);
        var (runePlayer, runeTrial) = PrepareCrusade(runeGame, 1);
        runePlayer.Field[0][0] = Card("S02-0608", "crusade-rune-richard");
        var rejected = runeGame.Handle(0, new L12Command("activateAbility", runeTrial.InstanceId,
            Ability: "crusadeRichardPiercing"));
        Assert.False(rejected.Accepted);
        Assert.Equal(1, runePlayer.SpecialZones.Runes);
        Assert.Empty(runeGame.State.PendingPrompts);
        Assert.Empty(runeGame.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "ability:crusadeRichardPiercing")]
    public void CrusadePiercingGrantIsLimitedToTheDeclaredTurn()
    {
        var game = Create(6813);
        var (player, trial) = PrepareCrusade(game, 2);
        var richard = Card("S02-0608", "crusade-duration-richard");
        player.Field[0][0] = richard;

        Assert.True(game.Handle(0, new L12Command("activateAbility", trial.InstanceId,
            Ability: "crusadeRichardPiercing")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: richard.InstanceId)).Accepted);
        PassResponses(game);
        Assert.Contains($"crusade-piercing:{richard.InstanceId}:{game.State.TurnSerial}", player.UsedAbilities);
        string[] Keywords() => JsonSerializer.SerializeToElement(game.SnapshotFor(0),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .GetProperty("players")[0].GetProperty("field")[0][0]
            .GetProperty("activeKeywords").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Contains("贯穿", Keywords());

        game.State.TurnSerial++;
        Assert.DoesNotContain("贯穿", Keywords());
    }

    [Fact]
    [Trait("L12Evidence", "ability:crusadeRecover")]
    public void CrusadeRecoverCommitsCostsAndUsesAuthoritativeHandAdd()
    {
        var game = Create(6814);
        var (player, trial) = PrepareCrusade(game, 2);
        var discard = Card("S01-0003", "crusade-paid-discard");
        var recover = Card("S02-0601", "crusade-paid-recover");
        player.Hand.Clear();
        player.Graveyard.Clear();
        player.Hand.Add(discard);
        player.Graveyard.Add(recover);

        Assert.True(game.Handle(0, new L12Command("activateAbility", trial.InstanceId,
            Ability: "crusadeRecover")).Accepted);
        var discardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(2, player.SpecialZones.Runes);
        Assert.Contains(discard, player.Hand);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: discardPrompt.PromptId,
            Choice: discard.InstanceId)).Accepted);
        var recoverPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(2, player.SpecialZones.Runes);
        Assert.Contains(discard, player.Hand);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: recoverPrompt.PromptId,
            Choice: recover.InstanceId)).Accepted);
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Contains(discard, player.Graveyard);
        PassResponses(game);

        Assert.Contains(recover, player.Hand);
        Assert.DoesNotContain(recover, player.Graveyard);
        Assert.Contains(game.State.AuthorityEvents, entry => entry.Type == "effect-hand-add"
            && entry.TargetInstanceId == recover.InstanceId && entry.OriginZone == "graveyard"
            && entry.DestinationZone == "hand");
    }

    [Fact]
    [Trait("L12Evidence", "ability:divinityFlipMorale")]
    public void DivinityFlipChoosesMoraleOnlyWhileItsEffectResolves()
    {
        var invalidGame = CreateWithFirstMaster("S02-05D1", 6815);
        var invalidPlayer = invalidGame.State.Players[0];
        invalidPlayer.Morale.Clear();
        var invalidTarget = new L12MoraleCard
        {
            CardId = "S02-05C1A", InstanceId = "divinity-invalid-flip", Tapped = true,
        };
        invalidPlayer.Morale.Add(invalidTarget);
        HoldOpponentResponseWindow(invalidGame);
        invalidGame.State.ActivePlayer = 0;
        invalidGame.State.Phase = L12Phase.Main;

        Assert.True(invalidGame.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "divinityFlipMorale")).Accepted);
        Assert.DoesNotContain(invalidGame.State.PendingPrompts, candidate => candidate.Continuation == "pending-activation");
        Assert.Single(invalidGame.State.EffectStack);
        invalidTarget.IsGodPower = true;
        PassResponses(invalidGame);
        Assert.Contains(invalidPlayer.UsedAbilities, key => key.Contains("divinityFlipMorale", StringComparison.Ordinal));
        Assert.Empty(invalidGame.State.EffectStack);
        Assert.Empty(invalidGame.State.PendingPrompts);

        var successGame = CreateWithFirstMaster("S02-05D1", 6816);
        var successPlayer = successGame.State.Players[0];
        successPlayer.Morale.Clear();
        var target = new L12MoraleCard
        {
            CardId = "S02-05C1A", InstanceId = "divinity-success-flip", Tapped = true,
        };
        successPlayer.Morale.Add(target);
        HoldOpponentResponseWindow(successGame);
        successGame.State.ActivePlayer = 0;
        successGame.State.Phase = L12Phase.Main;

        Assert.True(successGame.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "divinityFlipMorale")).Accepted);
        Assert.DoesNotContain(successGame.State.PendingPrompts, candidate => candidate.Continuation == "pending-activation");
        PassResponses(successGame);
        var prompt = Assert.Single(successGame.State.PendingPrompts);
        Assert.Equal("s2-flip-morale", prompt.Data["action"]);
        Assert.True(successGame.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: target.InstanceId)).Accepted);
        Assert.True(target.IsGodPower);
        Assert.True(target.Tapped);
        Assert.Contains($"active:master-0:divinityFlipMorale", successPlayer.UsedAbilities);
    }

    [Fact]
    [Trait("L12Evidence", "ability:divinityPower")]
    public void DivinityDamageDeclaresAllSixAllocationsBeforeGodPowerPayment()
    {
        var game = CreateWithFirstMaster("S02-05D1", 6817);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        player.Morale.Clear();
        player.Morale.AddRange([
            new L12MoraleCard { CardId = "S02-05C1", InstanceId = "divinity-declare-power-a", IsGodPower = true },
            new L12MoraleCard { CardId = "S02-05C1", InstanceId = "divinity-declare-power-b", IsGodPower = true },
        ]);
        var target = Card("S02-0004", "divinity-declared-damage-target");
        enemy.Field[0][0] = target;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "divinityPower")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "mode:damage")).Accepted);
        for (var index = 0; index < 6; index++)
        {
            prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("pending-activation", prompt.Continuation);
            Assert.Contains(target.InstanceId, prompt.ValidChoices);
            Assert.All(player.Morale, morale => Assert.False(morale.Tapped));
            Assert.Empty(game.State.EffectStack);
            Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                Choice: target.InstanceId)).Accepted);
        }
        Assert.All(player.Morale, morale => Assert.True(morale.Tapped));
        PassResponses(game);
        Assert.Contains(target, enemy.Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains(target.Name, StringComparison.Ordinal)
            && entry.Text.Contains("兵力不高于0阵亡", StringComparison.Ordinal));
    }
}
