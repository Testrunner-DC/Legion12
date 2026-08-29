using TwelveLegions.Server;
using System.Text.Json;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class S2FactionRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 6301)
        => new(Catalog, "s2-faction", "S2FACTION", seed, ["甲", "乙"], [0, 0], skipPreparation: true);

    private static L12GameEngine CreateWithFirstMaster(string masterId, int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var firstDeck = new L12PresetDeckDefinition
        {
            Name = $"{masterId}测试牌库",
            MasterId = masterId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "s2-faction", "S2FACTION", seed,
            ["甲", "乙"], [firstDeck, baseDeck], skipPreparation: true);
        game.State.ActivePlayer = 0;
        return game;
    }

    private static L12CardInstance Card(string id, string instanceId)
    {
        var definition = Catalog.Cards[id];
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
            CannotAttack = definition.Id is "S02-0005" or "S02-0007" or "S02-0201" or "S02-0603",
        };
    }

    private static void AddMorale(L12PlayerState player, int count)
    {
        while (player.Morale.Count < count)
        {
            var morale = player.MoraleDeck[0];
            player.MoraleDeck.RemoveAt(0);
            morale.Tapped = false;
            player.Morale.Add(morale);
        }
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

    [Fact]
    public void ArthurDeathDeclaresHandCardAndBattlefieldSlotBeforeEnteringStack()
    {
        var game = Create(63000);
        var player = game.State.Players[0];
        player.Hand.Clear();
        var knight = Card("S02-0604", "arthur-declared-knight");
        player.Hand.Add(knight);
        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, "S02-0601", Row: 0, Slot: 0,
            TriggerEffects: false)).Accepted);
        var arthur = Assert.IsType<L12CardInstance>(player.Field[0][0]);

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: arthur.InstanceId)).Accepted);
        PassResponses(game);
        var cardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", cardPrompt.Continuation);
        Assert.Contains(knight.InstanceId, cardPrompt.ValidChoices);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceCardId == "S02-0601");

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: cardPrompt.PromptId,
            Choice: knight.InstanceId)).Accepted);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("slot", slotPrompt.Kind);
        var slot = slotPrompt.ValidChoices[0];
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId,
            Choice: slot)).Accepted);
        PassResponses(game);

        Assert.DoesNotContain(knight, player.Hand);
        Assert.Contains(player.Field.SelectMany(row => row), card => card?.InstanceId == knight.InstanceId && !card.Tapped);
    }

    [Fact]
    public void TheseusDeathDeclaresPromotionRecoveryAndRevealsItBeforeResolution()
    {
        var game = Create(63001);
        var player = game.State.Players[0];
        var promotion = Card("S02-0501", "theseus-declared-promotion");
        player.Graveyard.Add(promotion);
        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, "S02-0518", Row: 0, Slot: 0,
            TriggerEffects: false)).Accepted);
        var theseus = Assert.IsType<L12CardInstance>(player.Field[0][0]);

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: theseus.InstanceId)).Accepted);
        PassResponses(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", prompt.Continuation);
        Assert.Contains(promotion.InstanceId, prompt.ValidChoices);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: promotion.InstanceId)).Accepted);
        PassResponses(game);

        Assert.Contains(promotion, player.Hand);
        Assert.DoesNotContain(promotion, player.Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Type == "return"
            && entry.Cards.Any(card => card.InstanceId == promotion.InstanceId));
    }

    [Fact]
    public void HeraclesMayDeclineItsDrawTwoDiscardOneEnterEffect()
    {
        var game = Create(63010);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Library.Clear();
        player.Library.AddRange([Card("S02-0503", "heracles-draw-one"), Card("S02-0504", "heracles-draw-two")]);

        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, "S02-0502", Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-heracles-draw-discard-choice", prompt.Data["action"]);
        Assert.Equal(2, player.Library.Count);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "no")).Accepted);
        Assert.Empty(player.Hand);
        Assert.Equal(2, player.Library.Count);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Fact]
    public void GoldenScarabEnterEffectSummonsStartingGraveyardBeetleToChosenSlot()
    {
        var game = Create(63011);
        var player = game.State.Players[0];
        var beetle = Card("S02-0201", "golden-scarab-beetle");
        beetle.OwnerIndex = 0;
        player.Graveyard.Add(beetle);

        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, "S02-0205")).Accepted);
        PassResponses(game);

        var chooseSlot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-scarab-enter-slot", chooseSlot.Data["action"]);
        Assert.Equal(beetle.InstanceId, chooseSlot.Data["previewCardId"]);
        var destination = Assert.Single(chooseSlot.ValidChoices.Take(1));
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: chooseSlot.PromptId,
            Choice: destination)).Accepted);

        Assert.DoesNotContain(beetle, player.Graveyard);
        Assert.Contains(player.Field.SelectMany(row => row), card => card?.InstanceId == beetle.InstanceId && !card.Tapped);
    }

    [Fact]
    public void GoldScarabBlocksOtherArtifactsButAllowsAnotherGoldScarab()
    {
        var game = Create(630111);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Relic = Card("S02-0205", "artifact-blocker-scarab");
        var differentArtifact = Card("S02-0520", "blocked-different-scarab");
        var sameArtifact = Card("S02-0205", "allowed-same-scarab");
        player.Hand.AddRange([differentArtifact, sameArtifact]);
        AddMorale(player, sameArtifact.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var hand = snapshot.GetProperty("players")[0].GetProperty("hand");
        var blockedView = hand.EnumerateArray().Single(card =>
            card.GetProperty("instanceId").GetString() == differentArtifact.InstanceId);
        var allowedView = hand.EnumerateArray().Single(card =>
            card.GetProperty("instanceId").GetString() == sameArtifact.InstanceId);
        Assert.StartsWith("〈黄金圣甲虫〉", blockedView.GetProperty("playBlockedReason").GetString());
        Assert.Equal(JsonValueKind.Null, allowedView.GetProperty("playBlockedReason").ValueKind);

        var differentResult = game.Handle(0, new L12Command("playCard", differentArtifact.InstanceId));
        var sameResult = game.Handle(0, new L12Command("playCard", sameArtifact.InstanceId));
        Assert.False(differentResult.Accepted);
        Assert.True(sameResult.Accepted, sameResult.Error);
        Assert.StartsWith("〈黄金圣甲虫〉", differentResult.Error);
        Assert.Contains(differentArtifact, player.Hand);
        Assert.DoesNotContain(sameArtifact, player.Hand);
    }

    [Fact]
    public void GoldenScarabDebuffDoesNotRestTheArtifactUsesItsOwnTurnKeyAndKillsAtZero()
    {
        var game = Create(630113);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var scarab = Card("S02-0205", "scarab-debuff-source");
        var discard = Card("S02-0003", "scarab-debuff-discard");
        var target = Card("S02-0005", "scarab-debuff-target");
        player.Relic = scarab;
        player.Hand.Clear();
        player.Hand.Add(discard);
        opponent.Field[0][0] = target;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", scarab.InstanceId,
            Ability: "scarabDebuff")).Accepted);
        var discardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: discardPrompt.PromptId,
            Choice: discard.InstanceId)).Accepted);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            CardInstanceIds: [target.InstanceId])).Accepted);
        PassResponses(game);

        Assert.False(scarab.Tapped);
        Assert.Contains(discard, player.Graveyard);
        Assert.Contains(target, opponent.Graveyard);
        Assert.False(game.Handle(0, new L12Command("activateAbility", scarab.InstanceId,
            Ability: "scarabDebuff")).Accepted);
        Assert.DoesNotContain($"active:{scarab.InstanceId}:scarabSummon", player.UsedAbilities);
    }

    [Fact]
    public void MedjedDeclaresStrongModeGuardAndTargetBeforePayingAtomically()
    {
        var game = CreateWithFirstMaster("S01-02M3", 630114);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        AddMorale(player, 1);
        var guard = Card("S01-0212", "medjed-strong-guard");
        var target = Card("S02-0601", "medjed-debuff-target");
        player.Field[0][0] = guard;
        opponent.Field[0][0] = target;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "medjedDebuff")).Accepted);
        var modePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(["mode:normal", "mode:strong", "skip"], modePrompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: modePrompt.PromptId,
            Choice: "mode:strong")).Accepted);
        var guardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(guard.InstanceId, guardPrompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: guardPrompt.PromptId,
            Choice: guard.InstanceId)).Accepted);
        Assert.False(guard.Tapped);
        Assert.All(player.Morale, morale => Assert.False(morale.Tapped));

        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            Choice: target.InstanceId)).Accepted);
        PassResponses(game);

        Assert.True(guard.Tapped);
        Assert.Single(player.Morale, morale => morale.Tapped);
        Assert.Equal(2000, target.Troops);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") is "medjed-extra-choice" or "medjed-extra-guard");
    }

    [Fact]
    public void MedjedNormalModeMayUseATombGuardAsItsOrdinaryOneResourcePayment()
    {
        var game = CreateWithFirstMaster("S01-02M3", 630115);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        player.Morale.Clear();
        var guard = Card("S01-0212", "medjed-normal-resource");
        var target = Card("S02-0601", "medjed-normal-target");
        player.Field[0][0] = guard;
        opponent.Field[0][0] = target;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "medjedDebuff")).Accepted);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.DoesNotContain("mode:strong", mode.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: mode.PromptId,
            Choice: "mode:normal")).Accepted);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            Choice: target.InstanceId)).Accepted);
        PassResponses(game);

        Assert.True(guard.Tapped);
        Assert.Equal(4000, target.Troops);
    }

    [Fact]
    public void AndvaranautBlocksEveryArtifactFromHandAndReachesTheSnapshot()
    {
        var game = Create(630112);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Relic = Card("S02-0305", "artifact-blocker-ring");
        var differentArtifact = Card("S02-0520", "blocked-different-ring");
        var sameArtifact = Card("S02-0305", "blocked-same-ring");
        player.Hand.AddRange([differentArtifact, sameArtifact]);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var hand = snapshot.GetProperty("players")[0].GetProperty("hand");
        Assert.All(hand.EnumerateArray(), card =>
            Assert.StartsWith("〈安德华拉诺特〉", card.GetProperty("playBlockedReason").GetString()));

        foreach (var card in player.Hand.ToArray())
        {
            var result = game.Handle(0, new L12Command("playCard", card.InstanceId));
            Assert.False(result.Accepted);
            Assert.StartsWith("〈安德华拉诺特〉", result.Error);
            Assert.Contains(card, player.Hand);
        }
    }

    [Theory]
    [InlineData("S01-0301", "verified-atomic-optional")]
    [InlineData("S01-0309", "verified-atomic-optional")]
    [InlineData("S02-0203", "verified-atomic-optional")]
    [InlineData("S02-0301", "s2-optional-death-draw")]
    [InlineData("S02-0402", "verified-atomic-optional")]
    [InlineData("S02-0512", "verified-atomic-optional")]
    public void OptionalDeathDrawClusterMayDeclineWithoutDrawingOrDiscarding(string cardId, string expectedAction)
    {
        var game = Create(63012);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Library.Clear();
        player.Library.Add(Card("S02-0504", $"optional-death-draw-{cardId}"));
        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, cardId, Row: 0, Slot: 0,
            TriggerEffects: false)).Accepted);
        var source = Assert.IsType<L12CardInstance>(player.Field[0][0]);

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0, CardInstanceId: source.InstanceId)).Accepted);
        PassResponses(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(expectedAction, prompt.Data["action"]);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "no")).Accepted);
        Assert.Empty(player.Hand);
        Assert.Single(player.Library);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Theory]
    [InlineData("S01-0301")]
    [InlineData("S01-0309")]
    [InlineData("S02-0203")]
    [InlineData("S02-0402")]
    [InlineData("S02-0512")]
    public void VerifiedOptionalDeathDrawResumesAfterYesAndDrawsExactlyOnce(string cardId)
    {
        var game = Create(63013);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Library.Clear();
        var drawn = Card("S02-0504", $"verified-optional-draw-{cardId}");
        player.Library.Add(drawn);
        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, cardId, Row: 0, Slot: 0,
            TriggerEffects: false)).Accepted);
        var source = Assert.IsType<L12CardInstance>(player.Field[0][0]);

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0, CardInstanceId: source.InstanceId)).Accepted);
        PassResponses(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("verified-atomic-optional", prompt.Data["action"]);
        Assert.Empty(player.Hand);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "yes")).Accepted);
        Assert.Same(drawn, Assert.Single(player.Hand));
        Assert.Empty(player.Library);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Theory]
    [InlineData("S02-0509", 0, 1)]
    [InlineData("S02-0512", 0, 1)]
    [InlineData("S02-0518", 0, 1)]
    [InlineData("S02-0510", 5, 3)]
    public void StructuredHandConditionsDriveDisplayedPlayCost(string cardId, int godPowerCount, int discount)
    {
        var game = Create(63011 + godPowerCount);
        var player = game.State.Players[0];
        var card = Card(cardId, $"structured-cost-{cardId}");
        player.Hand.Clear();
        player.Hand.Add(card);
        AddMorale(player, Math.Max(1, godPowerCount));
        foreach (var morale in player.Morale.Take(godPowerCount)) morale.IsGodPower = true;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var view = game.SnapshotFor(0).Players[0];
        var hand = Assert.IsType<L12CardInstance[]>(view.GetType().GetProperty("hand")!.GetValue(view));
        Assert.Equal(card.Cost - discount, Assert.Single(hand).PlayCost);
    }

    [Fact]
    public void OdysseusZeroGodPowerDiscountPaysTheStructuredCost()
    {
        var game = Create(63016);
        var player = game.State.Players[0];
        var card = Card("S02-0509", "structured-cost-odysseus");
        player.Hand.Clear();
        player.Hand.Add(card);
        AddMorale(player, card.Cost - 1);
        foreach (var morale in player.Morale)
        {
            morale.IsGodPower = false;
            morale.Tapped = false;
        }
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var result = game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0));

        Assert.True(result.Accepted, result.Error);
        Assert.Equal(card.Cost - 1, player.Morale.Count(morale => morale.Tapped));
    }

    [Theory]
    [InlineData("S01-0107")]
    [InlineData("S01-0204")]
    [InlineData("S01-0312")]
    [InlineData("S02-0004")]
    [InlineData("S02-0007")]
    [InlineData("S02-0302")]
    [InlineData("S02-0512")]
    [InlineData("S02-0615")]
    public void EveryPrintedFrontRowTauntUsesTheSharedLegalTargetRule(string cardId)
    {
        var game = Create(63017);
        var attacker = Card("S02-0003", $"taunt-attacker-{cardId}");
        var taunt = Card(cardId, $"taunt-target-{cardId}");
        attacker.SummonRound = -1;
        game.State.Players[1].Field[0][0] = attacker;
        game.State.Players[0].Field[0][1] = taunt;
        game.State.ActivePlayer = 1;
        game.State.Phase = L12Phase.Main;

        var legalTargets = game.SnapshotFor(1).LegalAttackTargets[attacker.InstanceId];

        Assert.Contains(taunt.InstanceId, legalTargets);
        Assert.DoesNotContain("master", legalTargets);
    }

    [Fact]
    public void PrintedFrontRowTauntStopsApplyingAfterMovingToTheBackRow()
    {
        var game = Create(63018);
        var attacker = Card("S02-0003", "back-taunt-attacker");
        var aeneas = Card("S02-0512", "back-row-aeneas");
        attacker.SummonRound = -1;
        game.State.Players[1].Field[0][0] = attacker;
        game.State.Players[0].Field[1][1] = aeneas;
        game.State.ActivePlayer = 1;
        game.State.Phase = L12Phase.Main;

        var legalTargets = game.SnapshotFor(1).LegalAttackTargets[attacker.InstanceId];

        Assert.False(L12StructuredCardRules.HasTaunt(aeneas, 1));
        Assert.Contains("master", legalTargets);
    }
    [Fact]
    public void FaithZealotDiscardedByEffectTriggersFreeMasterAbilityWithoutUsingItsTurnCount()
    {
        var game = Create(6310);
        var player = game.State.Players[0];
        player.Hand.Clear();
        var heracles = Card("S02-0502", "faith-heracles");
        var zealot = Card("S02-0006", "faith-zealot-effect-discard");
        player.Hand.AddRange([heracles, zealot]);
        AddMorale(player, heracles.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", heracles.InstanceId, Row: 0, Slot: 0)).Accepted);
        var optional = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-heracles-draw-discard-choice", optional.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: optional.PromptId, Choice: "yes")).Accepted);
        var discard = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-olympus-draw-discard", discard.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: discard.PromptId, Choice: zealot.InstanceId)).Accepted);
        PassResponses(game);

        var faith = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-faith-zealot", faith.Data["action"]);
        Assert.Contains("drawCycle", faith.ValidChoices);
        var moraleBefore = player.Morale.Select(card => (card.InstanceId, card.Tapped)).ToArray();
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: faith.PromptId, Choice: "drawCycle")).Accepted);
        PassResponses(game);

        Assert.Equal(moraleBefore, player.Morale.Select(card => (card.InstanceId, card.Tapped)).ToArray());
        Assert.DoesNotContain("active:master-0:drawCycle", player.UsedAbilities);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("无视全部消耗触发") && entry.Text.Contains("杨戬"));
        Assert.Contains(game.State.PendingPrompts, prompt => prompt.Data.GetValueOrDefault("action") == "yangjian-return-card");
    }

    [Fact]
    public void FaithZealotDiscardedAsAColonCostDoesNotTrigger()
    {
        var game = Create(6311);
        var player = game.State.Players[0];
        player.Hand.Clear();
        var naotora = Card("S02-0402", "faith-cost-naotora");
        var zealot = Card("S02-0006", "faith-zealot-cost-discard");
        var target = Card("S02-0401", "faith-cost-ready-target");
        target.Tapped = true;
        player.Hand.AddRange([naotora, zealot]);
        player.Field[0][1] = target;
        AddMorale(player, naotora.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", naotora.InstanceId, Row: 0, Slot: 0)).Accepted);
        var discard = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-gaotianyuan-ready-discard", discard.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: discard.PromptId, Choice: zealot.InstanceId)).Accepted);
        var ready = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-gaotianyuan-ready-target", ready.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: ready.PromptId, Choice: target.InstanceId)).Accepted);
        PassResponses(game);

        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Data.GetValueOrDefault("action") == "s2-faith-zealot");
        Assert.DoesNotContain(player.UsedAbilities, key => key.Contains("faith-zealot", StringComparison.OrdinalIgnoreCase));
        Assert.False(target.Tapped);
    }

    [Fact]
    public void FaithZealotOncePerTurnUsageIsTrackedPerPhysicalInstance()
    {
        var game = Create(63111);
        var player = game.State.Players[0];
        var beowulf = Card("S01-0301", "faith-double-mill-source");
        var first = Card("S02-0006", "faith-double-first");
        var second = Card("S02-0006", "faith-double-second");
        player.Hand.Clear();
        player.Library.Clear();
        player.Hand.Add(beowulf);
        player.Library.AddRange([first, second, Card("S01-0001", "faith-double-draw")]);
        AddMorale(player, beowulf.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", beowulf.InstanceId,
            Row: 0, Slot: 0)).Accepted);

        var triggeredInstances = new HashSet<string>();
        while (triggeredInstances.Count < 2)
        {
            PassResponses(game);
            var prompt = Assert.Single(game.State.PendingPrompts,
                candidate => candidate.Data.GetValueOrDefault("action") == "s2-faith-zealot");
            triggeredInstances.Add(game.State.EffectStack[^1].SourceInstanceId);
            Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                Choice: "skip")).Accepted);
        }

        Assert.Equal(new[] { first.InstanceId, second.InstanceId }.Order(), triggeredInstances.Order());
        Assert.Contains($"trigger:faith-zealot:{first.InstanceId}", player.UsedAbilities);
        Assert.Contains($"trigger:faith-zealot:{second.InstanceId}", player.UsedAbilities);
    }

    [Fact]
    public void MistletoeCharmDeclaresRunesBeforePayingAndThenDebuffsTarget()
    {
        var game = Create(6312);
        var player = game.State.Players[0];
        var tactic = Card("S02-0622", "mistletoe-charm");
        var target = Card("S02-0004", "mistletoe-target");
        player.Hand.Clear();
        player.Hand.Add(tactic);
        game.State.Players[1].Field[0][0] = target;
        player.SpecialZones.Runes = 2;
        AddMorale(player, 1);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", targetPrompt.Continuation);
        Assert.Contains(target.InstanceId, targetPrompt.ValidChoices);
        Assert.Contains(tactic, player.Hand);
        Assert.Equal(2, player.SpecialZones.Runes);
        Assert.Equal(1, player.Morale.Count(card => !card.Tapped));
        Assert.Empty(game.State.EffectStack);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            Choice: target.InstanceId)).Accepted);
        var runePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-mistletoe-rune-cost", runePrompt.Continuation);
        Assert.Equal("resource-payment", runePrompt.Kind);
        Assert.Equal(["rune:1", "rune:2"], runePrompt.ValidChoices);
        var runePayment = game.Handle(0, new L12Command("resolvePrompt", PromptId: runePrompt.PromptId,
            CardInstanceIds: ["rune:1", "rune:2"]));
        Assert.True(runePayment.Accepted, runePayment.Error);
        if (game.State.EffectStack.Count > 0)
            Assert.Equal(target.InstanceId, game.State.EffectStack[^1].Data["target"]);
        PassResponses(game);

        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Single(player.Morale, card => card.Tapped);
        Assert.Null(game.State.Players[1].Field[0][0]);
        Assert.Contains(target, game.State.Players[1].Graveyard);
        Assert.Equal(target.BaseTroops, target.Troops);
    }

    [Fact]
    public void GawainConsumesTheRunesClickedOnTheBoard()
    {
        var game = Create(63121);
        var player = game.State.Players[0];
        var gawain = Card("S02-0607", "gawain-rune-payment");
        gawain.SummonRound = 0;
        player.Field[0][0] = gawain;
        player.SpecialZones.Runes = 3;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", gawain.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        var runePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-gawain-runes", runePrompt.Data["action"]);
        Assert.Equal("resource-payment", runePrompt.Kind);
        Assert.Equal(["rune:1", "rune:2", "rune:3"], runePrompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: runePrompt.PromptId,
            CardInstanceIds: ["rune:1", "rune:2"])).Accepted);

        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.Equal(gawain.BaseTroops + 2000, gawain.Troops);
        Assert.Equal(3, game.State.PendingDefense?.MasterDamage);
    }

    [Fact]
    public void PromotionSelectsAFoundationPaysGodPowerAndBatchesBothEntryTriggers()
    {
        var game = Create(6313);
        var player = game.State.Players[0];
        var foundation = Card("S02-0502", "promotion-foundation");
        var promoted = Card("S02-0501", "promotion-card");
        foundation.SummonRound = 2;
        player.Field[0][1] = foundation;
        player.Hand.Clear();
        player.Hand.Add(promoted);
        for (var index = 0; index < 2; index++)
        {
            var morale = new L12MoraleCard
            {
                InstanceId = $"promotion-power-{index}", CardId = "S02-05C1", Tapped = false, IsGodPower = true,
            };
            player.Morale.Add(morale);
        }
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", promoted.InstanceId)).Accepted);
        var foundationPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-promotion-foundation", foundationPrompt.Continuation);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: foundationPrompt.PromptId,
            Choice: foundation.InstanceId)).Accepted);

        Assert.Same(promoted, player.Field[0][1]);
        Assert.DoesNotContain(promoted, player.Hand);
        Assert.Contains(foundation, promoted.AttachedCards);
        Assert.Equal(foundation.SummonRound, promoted.SummonRound);
        Assert.All(player.Morale.Where(card => card.CardId == "S02-05C1"), card =>
        {
            Assert.True(card.Tapped);
            Assert.False(card.IsGodPower);
        });
        var triggerOrder = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-batch-order", triggerOrder.Continuation);
        Assert.Equal(2, triggerOrder.ValidChoices.Count);

        var promotionTrigger = triggerOrder.ValidChoices.Single(id =>
            triggerOrder.Data.GetValueOrDefault($"trigger:{id}") == "promotion-enter");
        var enterTrigger = triggerOrder.ValidChoices.Single(id =>
            triggerOrder.Data.GetValueOrDefault($"trigger:{id}") == "enter");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: triggerOrder.PromptId,
            CardInstanceIds: [enterTrigger, promotionTrigger])).Accepted);

        // 玩家提交的是实际结算顺序。堆叠虽然后进先出，顶部仍必须是玩家排在第一位的触发。
        Assert.Equal("enter", game.State.EffectStack[^1].Trigger);
        Assert.Equal("promotion-enter", game.State.EffectStack[^2].Trigger);
    }

    [Fact]
    public void HeraclesPromotionRevealIsPublicAndDoesNotBlockOnOpponentConfirmation()
    {
        var game = Create(63132);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var foundation = Card("S02-0502", "heracles-reveal-foundation");
        var promoted = Card("S02-0501", "heracles-reveal-promotion");
        var shown = Card("S02-0503", "heracles-revealed-legion");
        var target = Card("S02-0504", "heracles-reveal-target");
        player.Field[0][0] = foundation;
        opponent.Field[0][0] = target;
        player.Hand.Clear();
        player.Hand.AddRange([promoted, shown]);
        for (var index = 0; index < 2; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"heracles-reveal-power-{index}", CardId = "S02-05C1", IsGodPower = true,
            });
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", promoted.InstanceId)).Accepted);
        var foundationPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: foundationPrompt.PromptId,
            Choice: foundation.InstanceId)).Accepted);
        var triggerOrder = Assert.Single(game.State.PendingPrompts);
        var promotionTrigger = triggerOrder.ValidChoices.Single(id =>
            triggerOrder.Data.GetValueOrDefault($"trigger:{id}") == "promotion-enter");
        var enterTrigger = triggerOrder.ValidChoices.Single(id =>
            triggerOrder.Data.GetValueOrDefault($"trigger:{id}") == "enter");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: triggerOrder.PromptId,
            CardInstanceIds: [promotionTrigger, enterTrigger])).Accepted);
        PassResponses(game);

        var revealChoice = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-heracles-promotion-show", revealChoice.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: revealChoice.PromptId,
            Choice: shown.InstanceId)).Accepted);

        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, targetPrompt.PlayerIndex);
        Assert.Equal("s2-heracles-promotion-kill", targetPrompt.Data["action"]);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "information-confirm");
        var revealEvent = Assert.Single(game.State.Events, actionEvent => actionEvent.Type == "reveal"
            && actionEvent.Cards.Any(card => card.InstanceId == shown.InstanceId));
        Assert.Contains("赫拉克勒斯·晋升展示手牌中的", revealEvent.Text);
        Assert.Contains("〈阿喀琉斯·晋升〉", revealEvent.Text);
    }

    [Fact]
    public void AchillesMayAttackLegionsOnSummonOnlyWhenItEnteredByPromotion()
    {
        var ordinaryGame = Create(63130);
        var ordinaryPlayer = ordinaryGame.State.Players[0];
        ordinaryPlayer.Hand.Clear();
        var ordinary = Card("S02-0503", "achilles-ordinary-entry");
        ordinaryPlayer.Hand.Add(ordinary);
        AddMorale(ordinaryPlayer, ordinary.Cost);
        ordinaryGame.State.ActivePlayer = 0;
        ordinaryGame.State.Phase = L12Phase.Main;

        Assert.True(ordinaryGame.Handle(0, new L12Command("playCard", ordinary.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(ordinaryGame);
        Assert.Equal(-1, ordinary.CanAttackLegionsOnSummonUntilTurn);

        var promotionGame = Create(63131);
        var promotionPlayer = promotionGame.State.Players[0];
        var foundation = Card("S02-0504", "achilles-promotion-foundation");
        var promoted = Card("S02-0503", "achilles-promotion-entry");
        var enemy = Card("S02-0604", "achilles-promotion-target");
        foundation.SummonRound = -1;
        promotionPlayer.Field[0][0] = foundation;
        promotionGame.State.Players[1].Field[0][0] = enemy;
        promotionPlayer.Hand.Clear();
        promotionPlayer.Hand.Add(promoted);
        for (var index = 0; index < 2; index++)
            promotionPlayer.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"achilles-god-power-{index}", CardId = "S02-05C1", IsGodPower = true,
            });
        promotionGame.State.ActivePlayer = 0;
        promotionGame.State.Phase = L12Phase.Main;

        Assert.True(promotionGame.Handle(0, new L12Command("playCard", promoted.InstanceId)).Accepted);
        var chooseFoundation = Assert.Single(promotionGame.State.PendingPrompts);
        Assert.True(promotionGame.Handle(0, new L12Command("resolvePrompt", PromptId: chooseFoundation.PromptId,
            Choice: foundation.InstanceId)).Accepted);
        PassResponses(promotionGame);

        Assert.Equal(promotionGame.State.TurnSerial, promoted.CanAttackLegionsOnSummonUntilTurn);
        Assert.Contains(enemy.InstanceId,
            promotionGame.SnapshotFor(0).LegalAttackTargets[promoted.InstanceId]);
    }

    [Fact]
    public void AchillesReducesFinalRangedCombatDamageByOneThousand()
    {
        var game = Create(63132);
        var attacker = Card("S02-0003", "achilles-ranged-attacker");
        var achilles = Card("S02-0503", "achilles-ranged-target");
        attacker.Troops = 7000;
        attacker.SummonRound = -1;
        achilles.SummonRound = -1;
        game.State.Players[0].Field[1][0] = attacker;
        game.State.Players[1].Field[0][0] = achilles;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", achilles.InstanceId))).Accepted);
        PassResponses(game);

        Assert.Same(achilles, game.State.Players[1].Field[0][0]);
        Assert.Equal(2000, achilles.Troops);
        Assert.DoesNotContain(achilles, game.State.Players[1].Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("受到远程进攻")
            && entry.Text.Contains("最终战斗伤害由 7000 降为 6000"));

        var supportedGame = Create(63133);
        var supportedAttacker = Card("S02-0003", "achilles-supported-attacker");
        var supportedAchilles = Card("S02-0503", "achilles-supported-target");
        var supporter = Card("S02-0004", "achilles-supporter");
        supportedAttacker.Troops = 7000;
        supportedAttacker.SummonRound = -1;
        supportedAchilles.SummonRound = -1;
        supporter.SummonRound = -1;
        supportedGame.State.Players[0].Field[1][0] = supportedAttacker;
        supportedGame.State.Players[1].Field[0][0] = supportedAchilles;
        supportedGame.State.Players[1].Field[1][0] = supporter;
        supportedGame.State.ActivePlayer = 0;
        supportedGame.State.Phase = L12Phase.Main;

        Assert.True(supportedGame.Handle(0, new L12Command("attack", supportedAttacker.InstanceId,
            Target: new L12AttackTarget("legion", supportedAchilles.InstanceId))).Accepted);
        PassResponses(supportedGame);
        Assert.Equal(L12Phase.Defense, supportedGame.State.Phase);
        Assert.Equal(8000, supportedAchilles.Troops);
        Assert.True(supportedGame.Handle(1, new L12Command("resolveDefense",
            SupportInstanceId: supporter.InstanceId)).Accepted);

        Assert.Equal(supportedAchilles.BaseTroops, supportedAchilles.Troops);
        Assert.Same(supportedAchilles, supportedGame.State.Players[1].Field[0][0]);
    }

    [Fact]
    public void AchillesGainsTemporaryFrontRowTauntAfterKillingALegion()
    {
        var game = Create(63134);
        var achilles = Card("S02-0503", "achilles-kill-attacker");
        var victim = Card("S02-0005", "achilles-kill-victim");
        achilles.SummonRound = -1;
        victim.SummonRound = -1;
        game.State.Players[0].Field[0][0] = achilles;
        game.State.Players[1].Field[0][0] = victim;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", achilles.InstanceId,
            Target: new L12AttackTarget("legion", victim.InstanceId))).Accepted);
        PassResponses(game);

        Assert.Contains(victim, game.State.Players[1].Graveyard);
        Assert.True(achilles.TauntUntilTurn > game.State.TurnSerial);

        var enemyAttacker = Card("S02-0003", "achilles-taunt-enemy");
        enemyAttacker.SummonRound = -1;
        game.State.Players[1].Field[0][1] = enemyAttacker;
        game.State.ActivePlayer = 1;
        game.State.Phase = L12Phase.Main;
        var attackMaster = game.Handle(1, new L12Command("attack", enemyAttacker.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.False(attackMaster.Accepted);
        Assert.Contains("挑衅", attackMaster.Error);
    }

    [Fact]
    public void OdysseusMayRevealATacticFromHandToGainOneThousandOnAttack()
    {
        var game = Create(63135);
        var player = game.State.Players[0];
        var odysseus = Card("S02-0509", "odysseus-attack");
        var tactic = Card("S02-0522", "odysseus-shown-tactic");
        odysseus.SummonRound = -1;
        player.Field[0][0] = odysseus;
        player.Hand.Clear();
        player.Hand.Add(tactic);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", odysseus.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        PassResponses(game);
        var show = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-odysseus-show-tactic", show.Data["action"]);
        Assert.Contains(tactic.InstanceId, show.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: show.PromptId,
            Choice: tactic.InstanceId)).Accepted);
        PassResponses(game);

        Assert.Contains(tactic, player.Hand);
        Assert.Equal(odysseus.BaseTroops + 1000, odysseus.Troops);
        Assert.Contains(game.State.Events, entry => entry.Type == "reveal" && entry.Cards.Any(card => card.CardId == tactic.CardId));
    }

    [Fact]
    public void ForgeDiscountIsAppliedToAndConsumedByTheNextPromotion()
    {
        var game = Create(6314);
        var player = game.State.Players[0];
        var forge = Card("S02-0520", "forge-discount-source");
        var foundation = Card("S02-0506", "forge-promotion-foundation");
        var promoted = Card("S02-0505", "forge-promotion-card");
        player.Relic = forge;
        player.Field[0][0] = foundation;
        player.Hand.Clear();
        player.Hand.Add(promoted);
        AddMorale(player, 1);
        player.Morale[0].IsGodPower = true;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", forge.InstanceId,
            Ability: "forgePromotionDiscount")).Accepted);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "resource-payment");
        PassResponses(game);
        Assert.Equal(1, player.NextS2PromotionGodPowerDiscount);

        Assert.True(game.Handle(0, new L12Command("playCard", promoted.InstanceId)).Accepted);
        var chooseFoundation = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: chooseFoundation.PromptId,
            Choice: foundation.InstanceId)).Accepted);

        Assert.Same(promoted, player.Field[0][0]);
        Assert.Equal(0, player.NextS2PromotionGodPowerDiscount);
        Assert.True(player.Morale[0].Tapped);
        Assert.True(player.Morale[0].IsGodPower);
    }

    [Fact]
    public void ForgeDeclaresItsReadyAfterKillTargetBeforePayingAndEnteringTheStack()
    {
        var game = Create(6315);
        var player = game.State.Players[0];
        var forge = Card("S02-0520", "forge-ready-source");
        var target = Card("S02-0502", "forge-ready-target");
        player.Relic = forge;
        player.Field[0][0] = target;
        AddMorale(player, 1);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", forge.InstanceId,
            Ability: "forgeReadyOnKill")).Accepted);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", targetPrompt.Continuation);
        Assert.False(forge.Tapped);
        Assert.DoesNotContain(player.Morale, morale => morale.Tapped);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            Choice: target.InstanceId)).Accepted);
        Assert.True(forge.Tapped);
        Assert.Single(player.Morale, morale => morale.Tapped);
        PassResponses(game);
        Assert.Equal(game.State.TurnSerial, target.ReadyAfterNextKillUntilTurn);
    }

    [Theory]
    [InlineData("S02-0101")]
    [InlineData("S02-0504")]
    public void FrontRowPrintedProtectionRejectsLowTroopMasterAttack(string protectorId)
    {
        var game = Create(6316);
        var attacker = Card("S02-0003", "low-troop-attacker");
        var protector = Card(protectorId, $"protector-{protectorId}");
        attacker.SummonRound = -1;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = protector;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var attack = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.False(attack.Accepted);
        Assert.Contains("兵力不高于2000", attack.Error);
    }

    [Fact]
    public void HelenWithGodPowerMakesOpponentChooseAndDiscardOneCard()
    {
        var game = Create(6317);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var helen = Card("S02-0515", "helen-entry");
        var discarded = Card("S02-0001", "helen-discard-target");
        player.Hand.Clear();
        player.Hand.Add(helen);
        opponent.Hand.Clear();
        opponent.Hand.Add(discarded);
        AddMorale(player, helen.Cost);
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = "helen-god-power", CardId = "S02-05C1", Tapped = false, IsGodPower = true,
        });
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", helen.InstanceId, Row: 0, Slot: 0)).Accepted);
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-payment", payment.Kind);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId,
            CardInstanceIds: player.Morale.Where(card => !card.IsGodPower && !card.Tapped)
                .Take(helen.Cost).Select(card => card.InstanceId).ToList())).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, prompt.PlayerIndex);
        Assert.Equal("s2-helen-entry-discard", prompt.Data["action"]);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: discarded.InstanceId)).Accepted);

        Assert.DoesNotContain(discarded, opponent.Hand);
        Assert.Contains(discarded, opponent.Graveyard);
    }

    [Fact]
    public void CanuteSelectsUpToTwoDifferentlyNamedAsgardLegionsAndTriggersTheirDeathEffects()
    {
        var game = Create(6318);
        var player = game.State.Players[0];
        var canute = Card("S02-0303", "canute-entry");
        var beowulf = Card("S02-0301", "canute-beowulf");
        var duplicate = Card("S02-0301", "canute-beowulf-duplicate");
        player.Hand.Clear();
        player.Hand.Add(canute);
        player.Library.Insert(0, Card("S02-0302", "canute-death-draw"));
        player.Graveyard.AddRange([beowulf, duplicate]);
        AddMorale(player, canute.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", canute.InstanceId, Row: 0, Slot: 0,
            Choice: "normal-cost")).Accepted);
        var choose = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-canute-trigger-deaths", choose.Data["action"]);

        var invalid = game.Handle(0, new L12Command("resolvePrompt", PromptId: choose.PromptId,
            CardInstanceIds: [beowulf.InstanceId, duplicate.InstanceId]));
        Assert.False(invalid.Accepted);
        Assert.Contains("非同名", invalid.Error);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: choose.PromptId,
            CardInstanceIds: [beowulf.InstanceId])).Accepted);
        PassResponses(game);

        var optional = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-optional-death-draw", optional.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: optional.PromptId, Choice: "yes")).Accepted);
        Assert.Contains(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-asgard-death-discard");
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("卡纽特大帝触发了1张军团"));
    }

    [Fact]
    public void OkitaEntersWithChargeAndTemporaryTroopsWhenGrassSwordIsInFront()
    {
        var game = Create(6319);
        var player = game.State.Players[0];
        var sword = Card("S01-0417", "okita-grass-sword");
        var okita = Card("S02-0403", "okita-entry");
        player.Field[0][0] = sword;
        player.Hand.Clear();
        player.Hand.Add(okita);
        AddMorale(player, okita.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", okita.InstanceId, Row: 0, Slot: 1)).Accepted);

        Assert.True(okita.HasCharge);
        Assert.Equal(okita.BaseTroops + 1000, okita.Troops);
        Assert.Contains(okita.TimedModifiers, modifier => modifier.Source == "冲田总司" && modifier.TroopsDelta == 1000);
    }

    [Fact]
    public void OkitaAttackMayPlayEligibleTopGaotianyuanLegionForFree()
    {
        var game = Create(63191);
        var player = game.State.Players[0];
        var okita = Card("S02-0403", "okita-attack-play");
        var tomoe = Card("S01-0410", "okita-top-tomoe");
        okita.SummonRound = -1;
        player.Field[0][0] = okita;
        player.Library.Clear();
        player.Library.Add(tomoe);
        var moraleBefore = player.Morale.Count(card => !card.Tapped);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", okita.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        PassResponses(game);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-okita-top", mode.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: mode.PromptId,
            Choice: "play")).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-okita-slot", slot.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: slot.ValidChoices[0])).Accepted);
        PassResponses(game);

        Assert.DoesNotContain(tomoe, player.Library);
        Assert.Contains(player.Field.SelectMany(row => row), card => card?.InstanceId == tomoe.InstanceId);
        Assert.True(tomoe.HasCharge);
        Assert.Equal(moraleBefore, player.Morale.Count(card => !card.Tapped));
        Assert.Contains(game.State.Events, entry => entry.Type == "reveal" && entry.Cards.Any(card => card.CardId == tomoe.CardId));
        Assert.Contains(game.State.Events, entry => entry.Type == "play" && entry.Cards.Any(card => card.CardId == tomoe.CardId));
    }

    [Fact]
    public void OkitaAttackAddsIneligibleTopCardToHand()
    {
        var game = Create(63192);
        var player = game.State.Players[0];
        var okita = Card("S02-0403", "okita-attack-hand");
        var squire = Card("S02-0609", "okita-top-squire");
        okita.SummonRound = -1;
        player.Field[0][0] = okita;
        player.Library.Clear();
        player.Library.Add(squire);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", okita.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        PassResponses(game);

        Assert.DoesNotContain(squire, player.Library);
        Assert.Contains(squire, player.Hand);
        Assert.Contains(game.State.Events, entry => entry.Type == "reveal" && entry.Cards.Any(card => card.CardId == squire.CardId));
        Assert.Contains(game.State.AuthorityEvents, entry => entry.Type == "effect-hand-add"
            && entry.TargetInstanceId == squire.InstanceId);
    }

    [Fact]
    public void MercenaryResponseDoesNotSwallowOkitasAttackTrigger()
    {
        var game = Create(63193);
        var attackerPlayer = game.State.Players[0];
        var defender = game.State.Players[1];
        var okita = Card("S02-0403", "okita-mercenary-attacker");
        var target = Card("S01-0102", "okita-mercenary-target");
        var mercenary = Card("S01-0002", "okita-mercenary-response");
        var squire = Card("S02-0609", "okita-mercenary-top");
        okita.SummonRound = target.SummonRound = 0;
        attackerPlayer.Field[0][0] = okita;
        attackerPlayer.Library.Clear();
        attackerPlayer.Library.Add(squire);
        defender.Field[0][0] = target;
        defender.Hand.Clear();
        defender.Hand.Add(mercenary);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", okita.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);

        L12Prompt? mercenaryWindow = null;
        for (var step = 0; step < 20 && mercenaryWindow is null; step++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is null) continue;
            if (prompt.Kind == "response" && prompt.ValidChoices.Contains(mercenary.InstanceId))
            {
                mercenaryWindow = prompt;
                break;
            }
            var choice = prompt.Kind == "response" ? "pass"
                : prompt.ValidChoices.Contains("skip") ? "skip"
                : prompt.ValidChoices.Contains("no") ? "no"
                : prompt.ValidChoices[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        }

        Assert.NotNull(mercenaryWindow);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: mercenaryWindow!.PromptId,
            Choice: mercenary.InstanceId)).Accepted);
        for (var step = 0; step < 20 && !attackerPlayer.Hand.Contains(squire); step++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt is null) continue;
            var choice = prompt.Kind == "response" ? "pass"
                : prompt.ValidChoices.Contains("skip") ? "skip"
                : prompt.ValidChoices.Contains("no") ? "no"
                : prompt.ValidChoices[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        }

        Assert.Contains(squire, attackerPlayer.Hand);
        Assert.Contains(game.State.AuthorityEvents, entry => entry.Type == "effect-hand-add"
            && entry.TargetInstanceId == squire.InstanceId);
        Assert.Contains(mercenary, defender.Graveyard);
    }

    [Fact]
    public void AsgardEntryThatCanAttackMasterDoesNotGainGeneralCharge()
    {
        var game = Create();
        var player = game.State.Players[0];
        var card = Card("S02-0301", "s2-asgard-master-attack");
        player.Hand.Add(card);
        AddMorale(player, card.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var play = game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        Assert.False(card.HasCharge);
        Assert.Equal(game.State.TurnSerial, card.CanAttackMasterOnSummonUntilTurn);
        Assert.True(game.Handle(0, new L12Command("attack", card.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
    }

    [Fact]
    public void RolloOffersTheFullEligibleGraveyardAndDiscountsByTheOrderedReturnCount()
    {
        var game = Create(630211);
        var player = game.State.Players[0];
        var rollo = Card("S02-0302", "rollo-discount-entry");
        var graveCards = Enumerable.Range(0, 10)
            .Select(index => Card("S01-0301", $"rollo-grave-{index}"))
            .ToArray();
        player.Hand.Clear();
        player.Graveyard.Clear();
        player.Hand.Add(rollo);
        player.Graveyard.AddRange(graveCards);
        AddMorale(player, 4);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", rollo.InstanceId, Row: 0, Slot: 0)).Accepted);
        var gravePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-rollo-grave-cost", gravePrompt.Continuation);
        Assert.Equal(10, gravePrompt.ValidChoices.Count);
        Assert.Equal(8, gravePrompt.MaxChoose);
        var ordered = graveCards.Take(8).Reverse().Select(card => card.InstanceId).ToList();
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: gravePrompt.PromptId,
            CardInstanceIds: ordered)).Accepted);

        Assert.Same(rollo, player.Field[0][0]);
        Assert.All(player.Morale, morale => Assert.True(morale.Tapped));
        Assert.Equal(ordered, player.Library.TakeLast(8).Select(card => card.InstanceId));
        Assert.Equal(graveCards.Skip(8), player.Graveyard);
    }

    [Fact]
    public void ThorHammerGraveyardAbilityConfirmsBeforeCostThenSelectsSlotOnBoard()
    {
        var game = CreateWithFirstMaster("S02-03M1", 63021);
        var player = game.State.Players[0];
        player.Graveyard.Clear();
        var hammer = Card("S02-0301", "thor-hammer-grave");
        var first = Card("S02-0001", "thor-hammer-cost-1");
        var second = Card("S02-0002", "thor-hammer-cost-2");
        var third = Card("S02-0003", "thor-hammer-cost-3");
        player.Graveyard.AddRange([hammer, first, second, third]);
        game.State.Phase = L12Phase.Main;

        var snapshotJson = JsonSerializer.Serialize(game.SnapshotFor(0));
        Assert.Contains("thorHammerRevive", snapshotJson);

        var begin = game.Handle(0, new L12Command("activateAbility", hammer.InstanceId,
            Ability: "thorHammerRevive"));
        Assert.True(begin.Accepted, begin.Error);
        var confirmation = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("graveyard-active-confirm", confirmation.Continuation);
        Assert.Equal(["yes", "no"], confirmation.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: confirmation.PromptId,
            Choice: "no")).Accepted);
        Assert.Equal([hammer, first, second, third], player.Graveyard);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);

        Assert.True(game.Handle(0, new L12Command("activateAbility", hammer.InstanceId,
            Ability: "thorHammerRevive")).Accepted);
        confirmation = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: confirmation.PromptId,
            Choice: "yes")).Accepted);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("grave-card", costPrompt.Kind);
        Assert.DoesNotContain(hammer.InstanceId, costPrompt.ValidChoices);
        Assert.Empty(game.State.EffectStack);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: costPrompt.PromptId,
            CardInstanceIds: [second.InstanceId, first.InstanceId, third.InstanceId])).Accepted);
        Assert.Equal(new[] { second.InstanceId, first.InstanceId, third.InstanceId },
            player.Library.TakeLast(3).Select(card => card.InstanceId).ToArray());
        Assert.Contains(hammer, player.Graveyard);
        PassResponses(game);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("slot", slotPrompt.Kind);
        Assert.Equal("s2-thor-hammer-slot", slotPrompt.Data["action"]);
        Assert.Equal(hammer.InstanceId, slotPrompt.Data["previewCardId"]);
        var slot = slotPrompt.ValidChoices[0];
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slotPrompt.PromptId,
            Choice: slot)).Accepted);

        Assert.DoesNotContain(hammer, player.Graveyard);
        Assert.Contains(player.Field.SelectMany(row => row), card => card?.InstanceId == hammer.InstanceId && !card.Tapped);
        Assert.Equal(new[] { second.InstanceId, first.InstanceId, third.InstanceId },
            player.Library.TakeLast(3).Select(card => card.InstanceId).ToArray());
        Assert.Equal(game.State.TurnSerial, hammer.CanAttackMasterOnSummonUntilTurn);
    }

    [Fact]
    public void AsgardEntryHealsMaster()
    {
        var game = Create(6302);
        var player = game.State.Players[0];
        player.Hp = player.MaxHp - 2;
        var card = Card("S02-0302", "s2-asgard-heal");
        player.Hand.Add(card);
        AddMorale(player, card.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var play = game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        Assert.Equal(player.MaxHp - 1, player.Hp);
    }

    [Fact]
    public void GoldenHaraldDeathHealsMasterThroughTheSharedAtomicRuntime()
    {
        var game = Create(63022);
        var player = game.State.Players[0];
        player.Hp = player.MaxHp - 2;
        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, "S01-0302", Row: 0, Slot: 0,
            TriggerEffects: false)).Accepted);
        var harald = Assert.IsType<L12CardInstance>(player.Field[0][0]);

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: harald.InstanceId)).Accepted);
        PassResponses(game);

        Assert.Equal(player.MaxHp - 1, player.Hp);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("金发哈拉尔", StringComparison.Ordinal));
    }

    [Fact]
    public void SquireDeathAdvancesTheCurrentTrialThroughTheSharedAtomicRuntime()
    {
        var game = Create(63023);
        var player = game.State.Players[0];
        var trial = Card("S02-06S4", "squire-death-trial");
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.Trials.Add(trial);
        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, "S02-0609", Row: 0, Slot: 0,
            TriggerEffects: false)).Accepted);
        var squire = Assert.IsType<L12CardInstance>(player.Field[0][0]);

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: squire.InstanceId)).Accepted);
        PassResponses(game);

        Assert.Equal(1, trial.TrialProgress);
        Assert.Equal(1, player.SpecialZones.TrialLevel);
        Assert.Contains(game.State.Events, entry => entry.Type == "trial" && entry.Text.Contains("0 → 1", StringComparison.Ordinal));
    }

    [Fact]
    public void JoanDeathHealsBothMastersThroughTheSharedAtomicRuntime()
    {
        var game = Create(63024);
        game.State.Players[0].Hp = game.State.Players[0].MaxHp - 2;
        game.State.Players[1].Hp = game.State.Players[1].MaxHp - 2;
        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, "S02-0613", Row: 0, Slot: 0,
            TriggerEffects: false)).Accepted);
        var joan = Assert.IsType<L12CardInstance>(game.State.Players[0].Field[0][0]);

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: joan.InstanceId)).Accepted);
        PassResponses(game);

        Assert.Equal(game.State.Players[0].MaxHp - 1, game.State.Players[0].Hp);
        Assert.Equal(game.State.Players[1].MaxHp - 1, game.State.Players[1].Hp);
    }

    [Fact]
    public void RagnarEntryGainsChargeThroughTheSharedAtomicRuntimeWhenMasterIsLow()
    {
        var game = Create(63025);
        var player = game.State.Players[0];
        player.Hp = 7;
        var ragnar = Card("S01-0303", "ragnar-atomic-entry");
        player.Hand.Add(ragnar);
        AddMorale(player, ragnar.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var play = game.Handle(0, new L12Command("playCard", ragnar.InstanceId, Row: 0, Slot: 0,
            Choice: "normal-cost"));

        Assert.True(play.Accepted, play.Error);
        PassResponses(game);
        Assert.True(ragnar.HasCharge);
    }

    [Fact]
    public void RuthlessHaraldEntryDamageIsOptionalAndUsesTheSharedAtomicRuntime()
    {
        var game = Create(63026);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        player.Hp = 5;
        opponent.Hp = 8;
        var harald = Card("S01-0304", "ruthless-harald-atomic-entry");
        player.Hand.Add(harald);
        AddMorale(player, harald.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var play = game.Handle(0, new L12Command("playCard", harald.InstanceId, Row: 0, Slot: 0,
            Choice: "normal-cost"));
        Assert.True(play.Accepted, play.Error);
        PassResponses(game);
        var optional = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("verified-atomic-optional", optional.Data["action"]);
        Assert.Equal(8, opponent.Hp);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: optional.PromptId, Choice: "yes")).Accepted);
        PassResponses(game);

        Assert.Equal(7, opponent.Hp);
    }

    [Fact]
    public void GoldenHaraldAttackGainsStrongAttackThroughTheSharedAtomicRuntime()
    {
        var game = Create(63027);
        var player = game.State.Players[0];
        player.Hp = 6;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, "S01-0302", Row: 0, Slot: 0,
            TriggerEffects: false)).Accepted);
        var harald = Assert.IsType<L12CardInstance>(player.Field[0][0]);
        harald.SummonRound = game.State.Round - 1;
        harald.Tapped = false;

        var attack = game.Handle(0, new L12Command("attack", harald.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.True(attack.Accepted, attack.Error);
        Assert.True(harald.HasStrongAttack);
        Assert.Equal(2, game.State.PendingDefense?.MasterDamage);
    }

    [Theory]
    [InlineData("S01-0303", 8)]
    [InlineData("S01-0304", 8)]
    public void AsgardConditionalEntryAtomsDoNothingWhenTheirConditionIsFalse(string cardId, int opponentHp)
    {
        var game = Create(cardId == "S01-0303" ? 63028 : 63029);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        player.Hp = 8;
        opponent.Hp = opponentHp;
        var card = Card(cardId, $"{cardId}-false-condition");
        player.Hand.Add(card);
        AddMorale(player, card.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var play = game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0,
            Choice: "normal-cost"));

        Assert.True(play.Accepted, play.Error);
        PassResponses(game);
        Assert.False(card.HasCharge);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal(opponentHp, opponent.Hp);
    }

    [Fact]
    public void MiyamotoEntryGainsChargeOnlyWhenNoOtherFrontLegionExists()
    {
        var emptyFront = Create(63030);
        var emptyPlayer = emptyFront.State.Players[0];
        emptyPlayer.Hp = 8;
        var miyamoto = Card("S01-0405", "miyamoto-atomic-entry");
        emptyPlayer.Hand.Add(miyamoto);
        AddMorale(emptyPlayer, miyamoto.Cost);
        emptyFront.State.ActivePlayer = 0;
        emptyFront.State.Phase = L12Phase.Main;

        var accepted = emptyFront.Handle(0, new L12Command("playCard", miyamoto.InstanceId, Row: 0, Slot: 0));
        Assert.True(accepted.Accepted, accepted.Error);
        PassResponses(emptyFront);
        Assert.True(miyamoto.HasCharge);

        var occupiedFront = Create(63031);
        var occupiedPlayer = occupiedFront.State.Players[0];
        Assert.True(occupiedFront.HandleGm(new L12GmCommand("placeCard", 0, "S01-0302", Row: 0, Slot: 1,
            TriggerEffects: false)).Accepted);
        var blockedMiyamoto = Card("S01-0405", "miyamoto-blocked-entry");
        occupiedPlayer.Hand.Add(blockedMiyamoto);
        AddMorale(occupiedPlayer, blockedMiyamoto.Cost);
        occupiedFront.State.ActivePlayer = 0;
        occupiedFront.State.Phase = L12Phase.Main;

        accepted = occupiedFront.Handle(0, new L12Command("playCard", blockedMiyamoto.InstanceId, Row: 0, Slot: 0));
        Assert.True(accepted.Accepted, accepted.Error);
        PassResponses(occupiedFront);
        Assert.False(blockedMiyamoto.HasCharge);
    }

    [Fact]
    public void AsgardTacticMillsBeforeItDebuffsTheChosenEnemy()
    {
        var game = Create(6303);
        var player = game.State.Players[0];
        var tactic = Card("S02-0307", "s2-asgard-curse");
        var milled = Card("S02-0007", "s2-asgard-milled");
        var target = Card("S02-0004", "s2-asgard-target");
        player.Hand.Add(tactic);
        player.Library.Insert(0, milled);
        game.State.Players[1].Field[0][0] = target;
        AddMorale(player, tactic.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        Assert.Contains(milled, player.Graveyard);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: target.InstanceId)).Accepted);
        // The target is a front-row defender, so its existing opposing-turn +1000
        // continuous modifier remains alongside this tactic's -3000 modifier.
        Assert.Equal(target.BaseTroops - 2000, target.Troops);
    }

    [Fact]
    public void OtherworldMerlinEntryAddsARuneAndCannotAttack()
    {
        var game = Create(6304);
        var player = game.State.Players[0];
        var card = Card("S02-0603", "s2-otherworld-merlin-entry");
        player.Hand.Add(card);
        AddMorale(player, card.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)).Accepted);
        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.Same(card, player.Field[0][0]);
        Assert.True(card.CannotAttack);
    }

    [Theory]
    [InlineData("S02-0603")]
    [InlineData("S02-0606")]
    [InlineData("S02-0607")]
    [InlineData("S02-0618")]
    public void OtherworldMandatoryEntryRuneCardsShareTheVerifiedRuntime(string cardId)
    {
        var game = Create(63040 + cardId[^1]);
        var player = game.State.Players[0];
        var card = Card(cardId, $"verified-rune-entry-{cardId}");
        player.Hand.Clear();
        player.Hand.Add(card);
        AddMorale(player, card.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var result = game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0));
        PassResponses(game);

        Assert.True(result.Accepted, result.Error);
        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.Same(card, player.Field[0][0]);
        Assert.Contains(game.State.Events, entry => entry.Type == "runes" && entry.Cards.Any(eventCard => eventCard.CardId == cardId)
            && entry.Text.Contains("获得1符文", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("yes", 1)]
    [InlineData("no", 0)]
    public void AmakineOptionalEntryRuneUsesTheVerifiedOptionalContinuation(string choice, int expectedRunes)
    {
        var game = Create(63047 + expectedRunes);
        var player = game.State.Players[0];
        var amakine = Card("S02-0616", $"verified-amakine-{choice}");
        player.Hand.Clear();
        player.Hand.Add(amakine);
        AddMorale(player, amakine.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var play = game.Handle(0, new L12Command("playCard", amakine.InstanceId, Row: 1, Slot: 0));
        PassResponses(game);

        Assert.True(play.Accepted, play.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("verified-atomic-optional", prompt.Data["action"]);
        Assert.Equal("获得1符文", prompt.Data["yes"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        Assert.Equal(expectedRunes, player.SpecialZones.Runes);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Theory]
    [InlineData("rune", 1, false)]
    [InlineData("trial", 0, true)]
    [InlineData("skip", 0, false)]
    public void ConstanceEntryChoosesExactlyOneRuneOrTrialMode(string choice, int expectedRunes, bool expectedTapped)
    {
        var game = Create(63048 + expectedRunes);
        var player = game.State.Players[0];
        var constance = Card("S02-0614", $"constance-entry-{choice}");
        var trial = Card("S02-06S5", $"constance-trial-{choice}");
        player.Hand.Clear();
        player.Hand.Add(constance);
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.Trials.Add(trial);
        AddMorale(player, constance.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", constance.InstanceId,
            Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-constance-entry", prompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: choice)).Accepted);

        Assert.Equal(expectedRunes, player.SpecialZones.Runes);
        Assert.Equal(expectedTapped, constance.Tapped);
        Assert.Equal(choice == "trial" ? constance.TrialValue : 0, trial.TrialProgress);
    }

    [Fact]
    public void ScathachEntryChargeUsesTheVerifiedKeywordRuntime()
    {
        var game = Create(63049);
        var player = game.State.Players[0];
        var scathach = Card("S02-0612", "verified-scathach-charge");
        player.Hand.Clear();
        player.Hand.Add(scathach);
        AddMorale(player, scathach.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var play = game.Handle(0, new L12Command("playCard", scathach.InstanceId, Row: 0, Slot: 0));
        PassResponses(game);

        Assert.True(play.Accepted, play.Error);
        Assert.True(scathach.HasCharge);
        Assert.Same(scathach, player.Field[0][0]);
        Assert.Contains(game.State.Events, entry => entry.Cards.Any(card => card.CardId == "S02-0612")
            && entry.Text.Contains("获得冲锋", StringComparison.Ordinal));
    }

    [Fact]
    public void MerlinDeclaresModeAndTargetBeforeSpendingRune()
    {
        var game = Create(6308);
        var player = game.State.Players[0];
        var merlin = Card("S02-0603", "s2-otherworld-merlin-active");
        var targetDefinition = Catalog.Cards.Values.First(card => card.CardType == "legion"
            && card.Troops >= 6000 && card.Id is not "S02-0519");
        var target = Card(targetDefinition.Id, "s2-otherworld-merlin-target");
        player.Field[0][0] = merlin;
        player.SpecialZones.Runes = 1;
        game.State.Players[1].Field[0][0] = target;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", merlin.InstanceId, Ability: "merlinRune")).Accepted);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: mode.PromptId, Choice: "mode:debuff")).Accepted);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId, Choice: target.InstanceId)).Accepted);
        Assert.True(merlin.Tapped);
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Same(target, game.State.Players[1].Field[0][0]);
        Assert.Equal(target.BaseTroops - 3000, target.Troops);
    }

    [Fact]
    public void OtherworldRuneOptionsUseEffectTextAndCanBeCancelledBeforeAutomaticPayment()
    {
        var game = CreateWithFirstMaster("S02-06M1", 63081);
        var player = game.State.Players[0];
        player.Morale.Clear();
        player.MoraleDeck.Clear();
        player.MoraleDeck.Add(new L12MoraleCard { InstanceId = "otherworld-faction", CardId = "S02-06C1" });
        player.SpecialZones.Runes = 1;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var activation = game.Handle(0, new L12Command("activateAbility", "faction-0", Ability: "runeUse"));
        Assert.True(activation.Accepted, activation.Error);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("消耗1符文：当前试炼进度+1", mode.Data["mode:trial"]);
        Assert.Equal("消耗1符文：抽取1张牌", mode.Data["mode:draw"]);
        Assert.Contains("skip", mode.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: mode.PromptId,
            Choice: "skip")).Accepted);

        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.Empty(game.State.PendingActivations);
        Assert.DoesNotContain(player.UsedAbilities, key => key.Contains("runeUse", StringComparison.Ordinal));
    }

    [Fact]
    public void OlympusFlipEntryLetsThePlayerChooseOneMoraleToFlip()
    {
        var game = Create(6305);
        var player = game.State.Players[0];
        var card = Card("S02-0513", "s2-olympus-flip-entry");
        player.Hand.Add(card);
        player.Morale.Clear();
        for (var index = 0; index < card.Cost + 1; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"olympus-flip-morale-{index}", CardId = "S02-05C1A", Tapped = false,
            });
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-flip-morale", prompt.Data["action"]);
        var morale = player.Morale.First(candidate => !candidate.Tapped && prompt.ValidChoices.Contains(candidate.InstanceId));
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: morale.InstanceId)).Accepted);
        Assert.False(morale.Tapped);
        Assert.True(morale.IsGodPower);
    }

    [Fact]
    public void GloryRoadFlipsUpToThreeChosenMoraleThenPaysTwoChosenGodPowerAndSearchesOlympus()
    {
        var game = Create(6361);
        var player = game.State.Players[0];
        var road = Card("S02-0521", "glory-road");
        var olympus = Card("S02-0519", "glory-road-olympus");
        var other = Card("S02-0301", "glory-road-other");
        player.Hand.Clear();
        player.Library.Clear();
        player.Morale.Clear();
        player.Hand.Add(road);
        player.Library.AddRange([other, olympus]);
        for (var index = 0; index < 7; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"glory-morale-{index}", CardId = "S02-05C1A", Tapped = false,
            });
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", road.InstanceId)).Accepted);
        PassResponses(game);
        var flip = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-glory-flip", flip.Data["action"]);
        Assert.Equal(3, flip.MaxChoose);
        var flipIds = player.Morale.Where(card => !card.Tapped && flip.ValidChoices.Contains(card.InstanceId))
            .Take(3).Select(card => card.InstanceId).ToArray();
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: flip.PromptId,
            CardInstanceIds: flipIds.ToList())).Accepted);
        Assert.All(player.Morale.Where(card => flipIds.Contains(card.InstanceId)), card => Assert.True(card.IsGodPower));

        var usePower = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-glory-use-power", usePower.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: usePower.PromptId, Choice: "yes")).Accepted);
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-glory-pay-power", payment.Data["action"]);
        var paidIds = payment.ValidChoices.Take(2).ToArray();
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId,
            CardInstanceIds: paidIds.ToList())).Accepted);

        var search = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-glory-search", search.Data["action"]);
        Assert.Equal([olympus.InstanceId], search.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: search.PromptId,
            Choice: olympus.InstanceId)).Accepted);

        Assert.Contains(olympus, player.Hand);
        Assert.Contains(other, player.Library);
        Assert.All(player.Morale.Where(card => paidIds.Contains(card.InstanceId)), card =>
        {
            Assert.True(card.Tapped);
            Assert.False(card.IsGodPower);
        });
        Assert.Single(player.Morale, card => flipIds.Contains(card.InstanceId) && card.IsGodPower && !card.Tapped);
    }

    [Fact]
    public void RichardEntryAdvancesTrialAttachesUpToThreeSquiresFromAllZonesAndGainsImmortality()
    {
        var game = Create(6362);
        var player = game.State.Players[0];
        var richard = Card("S02-0608", "richard-entry");
        var fieldSquire = Card("S02-0609", "richard-field-squire");
        var handSquire = Card("S02-0609", "richard-hand-squire");
        var graveSquire = Card("S02-0609", "richard-grave-squire");
        var librarySquire = Card("S02-0609", "richard-library-squire");
        var trial = Card("S02-06S4", "richard-trial");
        player.Hand.Clear();
        player.Library.Clear();
        player.Graveyard.Clear();
        player.Morale.Clear();
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.Trials.Add(trial);
        player.Hand.AddRange([richard, handSquire]);
        player.Library.Add(librarySquire);
        player.Graveyard.Add(graveSquire);
        player.Field[0][1] = fieldSquire;
        for (var index = 0; index < richard.Cost; index++)
            player.Morale.Add(new L12MoraleCard { InstanceId = $"richard-cost-{index}", CardId = "S02-06C1" });
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", richard.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        var attach = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-richard-entry-attach", attach.Data["action"]);
        Assert.Equal("战场", attach.Data[$"{fieldSquire.InstanceId}:zone"]);
        Assert.Equal("手牌", attach.Data[$"{handSquire.InstanceId}:zone"]);
        Assert.Equal("墓地", attach.Data[$"{graveSquire.InstanceId}:zone"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: attach.PromptId,
            CardInstanceIds: [fieldSquire.InstanceId, handSquire.InstanceId, graveSquire.InstanceId])).Accepted);

        Assert.Equal(2, trial.TrialProgress);
        Assert.Equal(1, richard.ImmortalUses);
        Assert.True(richard.ImmortalUntilTurn > game.State.TurnSerial);
        Assert.Equal(3, richard.AttachedCards.Count);
        Assert.Null(player.Field[0][1]);
        Assert.DoesNotContain(handSquire, player.Hand);
        Assert.DoesNotContain(graveSquire, player.Graveyard);
        Assert.Contains(librarySquire, player.Library);
    }

    [Fact]
    public void RichardAttackDiscardsChosenAttachedSquiresAndDefensePaysAnExtraChosenHandCard()
    {
        var game = Create(6363);
        var attackerPlayer = game.State.Players[0];
        var defender = game.State.Players[1];
        var richard = Card("S02-0608", "richard-attack");
        richard.SummonRound = 0;
        var firstSquire = Card("S02-0609", "richard-attack-squire-a");
        var secondSquire = Card("S02-0609", "richard-attack-squire-b");
        richard.AttachedCards.AddRange([firstSquire, secondSquire]);
        attackerPlayer.Field[0][0] = richard;
        var blocker = Card("S02-0608", "richard-blocker");
        blocker.Troops = 12000;
        var extra = Card("S02-0001", "richard-extra-discard");
        defender.Hand.Clear();
        defender.Hand.AddRange([blocker, extra]);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        var hpBefore = defender.Hp;

        Assert.True(game.Handle(0, new L12Command("attack", richard.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        PassResponses(game);
        var attackEffect = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-richard-attack-squires", attackEffect.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: attackEffect.PromptId,
            CardInstanceIds: [firstSquire.InstanceId, secondSquire.InstanceId])).Accepted);
        Assert.Equal(richard.BaseTroops + 2000, richard.Troops);
        Assert.Empty(richard.AttachedCards);
        Assert.Contains(firstSquire, attackerPlayer.Graveyard);
        Assert.Contains(secondSquire, attackerPlayer.Graveyard);

        Assert.True(game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: [blocker.InstanceId])).Accepted);
        PassResponses(game);
        var extraCost = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-richard-defense-extra-discard", extraCost.Data["action"]);
        Assert.DoesNotContain(blocker.InstanceId, extraCost.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: extraCost.PromptId,
            Choice: extra.InstanceId)).Accepted);

        Assert.Equal(hpBefore, defender.Hp);
        Assert.Contains(blocker, defender.Graveyard);
        Assert.Contains(extra, defender.Graveyard);
    }

    [Fact]
    public void RichardMakesDefenseInvalidWhenNoSeparateHandCardCanPayTheExtraCost()
    {
        var game = Create(6364);
        var attackerPlayer = game.State.Players[0];
        var defender = game.State.Players[1];
        var richard = Card("S02-0608", "richard-unpaid");
        richard.SummonRound = 0;
        attackerPlayer.Field[0][0] = richard;
        var blocker = Card("S02-0608", "richard-only-blocker");
        blocker.Troops = 9000;
        defender.Hand.Clear();
        defender.Hand.Add(blocker);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        var hpBefore = defender.Hp;

        Assert.True(game.Handle(0, new L12Command("attack", richard.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        PassResponses(game);
        Assert.True(game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: [blocker.InstanceId])).Accepted);
        PassResponses(game);

        Assert.Equal(hpBefore - 1, defender.Hp);
        Assert.Contains(blocker, defender.Hand);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("额外弃牌费用", StringComparison.Ordinal));
    }

    [Fact]
    public void AristotleDiscountIsConsumedByTheNextOlympusLegion()
    {
        var game = Create(6306);
        var player = game.State.Players[0];
        var aristotle = Card("S02-0513", "s2-aristotle");
        var next = Card("S02-0518", "s2-theseus");
        player.Field[0][0] = aristotle;
        player.Hand.Add(next);
        AddMorale(player, Math.Max(1, next.Cost - 1));
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", aristotle.InstanceId, Ability: "aristotleDiscount")).Accepted);
        Assert.Equal(1, player.NextS2OlympusLegionDiscount);
        Assert.True(game.Handle(0, new L12Command("playCard", next.InstanceId, Row: 0, Slot: 1)).Accepted);
        Assert.Equal(0, player.NextS2OlympusLegionDiscount);
        Assert.Same(next, player.Field[0][1]);
    }

    [Fact]
    public void OtherworldArthurEntryHasChargeAndPairDiscountApplies()
    {
        var game = Create(6307);
        var player = game.State.Players[0];
        var percival = Card("S02-0611", "s2-percival");
        var arthur = Card("S02-0612", "s2-arthur");
        player.Field[0][0] = percival;
        player.Hand.Add(arthur);
        AddMorale(player, Math.Max(0, arthur.Cost - 2));
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", arthur.InstanceId, Row: 0, Slot: 1)).Accepted);
        Assert.True(arthur.HasCharge);
    }

    [Fact]
    public void ArthurEntryUsesOneRuneInsteadOfMoraleForKingsSword()
    {
        var game = Create(6308);
        var player = game.State.Players[0];
        var arthur = Card("S02-0601", "s2-arthur-rune-cost");
        player.Hand.Add(arthur);
        AddMorale(player, arthur.Cost);
        player.SpecialZones.Runes = 1;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var play = game.Handle(0, new L12Command("playCard", arthur.InstanceId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-arthur-sword", prompt.Data["action"]);
        Assert.Contains("消耗1符文", prompt.Data["yes"]);

        var moraleAfterPlayingArthur = player.Morale.Count(card => !card.Tapped);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "yes")).Accepted);

        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Equal(moraleAfterPlayingArthur, player.Morale.Count(card => !card.Tapped));
        Assert.Contains(arthur.AttachedCards, card => card.CardId == "S02-06S2");
    }

    [Fact]
    public void RestedAmakineProtectsOnlyActiveTrialLegionsFromAttack()
    {
        var game = Create(6309);
        var attackerPlayer = game.State.ActivePlayer;
        var defenderPlayer = 1 - attackerPlayer;
        var attacker = Card("S02-0004", "amakine-test-attacker");
        attacker.SummonRound = 0;
        game.State.Players[attackerPlayer].Field[0][0] = attacker;
        var amakine = Card("S02-0616", "amakine-protector");
        amakine.Tapped = true;
        var galahad = Card("S02-0604", "amakine-trial-legion");
        game.State.Players[defenderPlayer].Field[0][0] = galahad;
        game.State.Players[defenderPlayer].Field[1][0] = amakine;
        game.State.Phase = L12Phase.Main;

        var protectedAttack = game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", galahad.InstanceId)));
        Assert.False(protectedAttack.Accepted);
        Assert.Contains("阿麦金", protectedAttack.Error);

        galahad.Tapped = true;
        var restedTargetAttack = game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", galahad.InstanceId)));
        Assert.True(restedTargetAttack.Accepted, restedTargetAttack.Error);
    }

    [Fact]
    public void AmakineShowsTheTopOtherworldCardAndCanAddItToHand()
    {
        var game = Create(6310);
        var playerIndex = game.State.ActivePlayer;
        var player = game.State.Players[playerIndex];
        var amakine = Card("S02-0616", "amakine-top-source");
        var top = Card("S02-0619", "amakine-top-card");
        player.Field[0][0] = amakine;
        player.Library.Insert(0, top);
        game.State.Phase = L12Phase.Main;

        var activation = game.Handle(playerIndex, new L12Command("activateAbility", amakine.InstanceId, Ability: "amakineTop"));
        Assert.True(activation.Accepted, activation.Error);
        Assert.True(amakine.Tapped);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-amakine-top-place", prompt.Data["action"]);
        Assert.Equal(top.InstanceId, prompt.Data["previewCardId"]);
        Assert.Contains("hand", prompt.ValidChoices);
        Assert.True(game.Handle(playerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "hand")).Accepted);
        Assert.Contains(top, player.Hand);
        Assert.DoesNotContain(top, player.Library);
    }

    [Fact]
    public void GalahadCanPayItselfAfterTheGrailTrialToDrawAndHeal()
    {
        var game = Create(6311);
        var playerIndex = game.State.ActivePlayer;
        var player = game.State.Players[playerIndex];
        var galahad = Card("S02-0604", "galahad-grail-source");
        var grail = Card("S02-06S4", "completed-grail");
        grail.TrialCompleted = true;
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.Trials.Add(grail);
        player.Field[0][0] = galahad;
        player.Hp = player.MaxHp - 1;
        var handBefore = player.Hand.Count;
        game.State.Phase = L12Phase.Main;

        var activation = game.Handle(playerIndex, new L12Command("activateAbility", galahad.InstanceId, Ability: "galahadGrailReward"));
        Assert.True(activation.Accepted, activation.Error);
        Assert.Contains(galahad, player.Graveyard);
        Assert.Null(player.Field[0][0]);
        Assert.Equal(handBefore + 1, player.Hand.Count);
        Assert.Equal(player.MaxHp, player.Hp);
    }

    [Fact]
    public void GalahadGrailRewardAbilityAppearsOnlyAfterGrailTrialCompleted()
    {
        var game = Create(63111);
        var playerIndex = game.State.ActivePlayer;
        var player = game.State.Players[playerIndex];
        var galahad = Card("S02-0604", "galahad-grail-ability-view");
        player.Field[0][0] = galahad;
        game.State.Phase = L12Phase.Main;

        static JsonElement Ability(JsonElement snapshot, int playerIndex)
            => snapshot.GetProperty("players")[playerIndex].GetProperty("field")[0][0]
                .GetProperty("abilities").EnumerateArray()
                .Single(item => item.GetProperty("id").GetString() == "galahadGrailReward");

        var before = Ability(JsonSerializer.SerializeToElement(game.SnapshotFor(playerIndex),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)), playerIndex);
        Assert.False(before.GetProperty("enabled").GetBoolean());
        Assert.Contains("尚未完成", before.GetProperty("disabledReason").GetString());

        var grail = Card("S02-06S4", "completed-grail-for-ability-view");
        grail.TrialCompleted = true;
        player.SpecialZones.Trials.Add(grail);
        var after = Ability(JsonSerializer.SerializeToElement(game.SnapshotFor(playerIndex),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)), playerIndex);
        Assert.True(after.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void FortuneSearchesArtifactAndUesugiOrdersTheRestAndEmpowersTheNextUesugi()
    {
        var game = Create(6312);
        var playerIndex = game.State.ActivePlayer;
        var player = game.State.Players[playerIndex];
        player.Hand.Clear();
        player.Library.Clear();
        var tactic = Card("S02-0405", "fortune-source");
        var artifact = Card("S02-0404", "fortune-artifact");
        var uesugi = Card("S01-0403", "fortune-uesugi");
        var first = Card("S02-0402", "fortune-first");
        var second = Card("S02-0403", "fortune-second");
        var third = Card("S02-0406", "fortune-third");
        player.Hand.Add(tactic);
        player.Library.AddRange([artifact, first, uesugi, second, third]);
        AddMorale(player, tactic.Cost + Math.Max(0, uesugi.Cost - 2));
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(playerIndex, new L12Command("playCard", tactic.InstanceId)).Accepted);
        PassResponses(game);
        var artifactPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-fortune-artifact", artifactPrompt.Data["action"]);
        Assert.True(game.Handle(playerIndex, new L12Command("resolvePrompt", PromptId: artifactPrompt.PromptId,
            Choice: artifact.InstanceId)).Accepted);

        var uesugiPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-fortune-uesugi", uesugiPrompt.Data["action"]);
        Assert.True(game.Handle(playerIndex, new L12Command("resolvePrompt", PromptId: uesugiPrompt.PromptId,
            Choice: uesugi.InstanceId)).Accepted);

        var orderPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("all-bottom", orderPrompt.Data["placementMode"]);
        var bottomOrder = new List<string> { third.InstanceId, first.InstanceId, second.InstanceId };
        Assert.True(game.Handle(playerIndex, new L12Command("resolvePrompt", PromptId: orderPrompt.PromptId,
            BottomCardInstanceIds: bottomOrder)).Accepted);

        Assert.Contains(artifact, player.Hand);
        Assert.Contains(uesugi, player.Hand);
        Assert.Equal(bottomOrder, player.Library.Select(card => card.InstanceId));
        Assert.Contains("s2-fortune-next-uesugi", player.UsedAbilities);

        Assert.True(game.Handle(playerIndex, new L12Command("playCard", uesugi.InstanceId, Row: 0, Slot: 0)).Accepted);
        Assert.True(uesugi.HasCharge);
        Assert.DoesNotContain("s2-fortune-next-uesugi", player.UsedAbilities);
    }

    [Fact]
    public void MimirSpringRequiresTwoDamageThenHealsDrawsAndCanMillTwo()
    {
        var game = Create(6313);
        var playerIndex = game.State.ActivePlayer;
        var player = game.State.Players[playerIndex];
        player.Hand.Clear();
        player.Library.Clear();
        var spring = Card("S02-0306", "mimir-source");
        var draw = Card("S02-0301", "mimir-draw");
        var millOne = Card("S02-0302", "mimir-mill-one");
        var millTwo = Card("S02-0303", "mimir-mill-two");
        player.Hand.Add(spring);
        player.Library.AddRange([draw, millOne, millTwo]);
        AddMorale(player, spring.Cost);
        player.Hp = player.MaxHp - 2;
        player.MasterDamageTakenThisTurn = 1;
        game.State.Phase = L12Phase.Main;

        var tooEarly = game.Handle(playerIndex, new L12Command("playCard", spring.InstanceId));
        Assert.False(tooEarly.Accepted);
        Assert.Contains(spring, player.Hand);

        player.MasterDamageTakenThisTurn = 2;
        Assert.True(game.Handle(playerIndex, new L12Command("playCard", spring.InstanceId)).Accepted);
        PassResponses(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-mimir-mill", prompt.Data["action"]);
        Assert.True(game.Handle(playerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "yes")).Accepted);

        Assert.Equal(player.MaxHp - 1, player.Hp);
        Assert.Contains(draw, player.Hand);
        Assert.Contains(millOne, player.Graveyard);
        Assert.Contains(millTwo, player.Graveyard);
        Assert.Contains("s2-mimir-used", player.UsedAbilities);
    }

    [Fact]
    public void OlympusMoraleConsumesOneMoraleThenLetsThePlayerFlipOneMorale()
    {
        var game = Create(6314);
        var playerIndex = game.State.ActivePlayer;
        var player = game.State.Players[playerIndex];
        player.Morale.Clear();
        player.MoraleDeck.Clear();
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = "olympus-morale-active", CardId = "S02-05C1A", Tapped = false,
        });
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = "olympus-morale-rested", CardId = "S02-05C1A", Tapped = true,
        });
        game.State.Phase = L12Phase.Main;

        var activation = game.Handle(playerIndex, new L12Command("activateAbility", $"faction-{playerIndex}",
            Ability: "olympusMoraleFlip"));
        Assert.True(activation.Accepted, activation.Error);
        PassResponses(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-flip-morale", prompt.Data["action"]);
        Assert.True(game.Handle(playerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "olympus-morale-rested")).Accepted);

        var flipped = player.Morale.Single(card => card.InstanceId == "olympus-morale-rested");
        Assert.True(flipped.Tapped);
        Assert.True(flipped.IsGodPower);
        Assert.True(player.Morale.Single(card => card.InstanceId == "olympus-morale-active").Tapped);
        Assert.Contains($"active:faction-{playerIndex}:olympusMoraleFlip", player.UsedAbilities);
    }

    [Fact]
    public void AttackEffectOrdinaryCostCanBePaidWithAChosenGodPower()
    {
        var game = Create(6319);
        var player = game.State.Players[0];
        var bors = Card("S02-0605", "bors-god-power-payment");
        bors.SummonRound = 0;
        player.Field[0][0] = bors;
        player.Morale.Clear();
        var ordinary = new L12MoraleCard
        {
            InstanceId = "bors-ordinary-morale", CardId = "S02-05C1A", Tapped = false,
        };
        var godPower = new L12MoraleCard
        {
            InstanceId = "bors-god-power", CardId = "S02-05C1", Tapped = false, IsGodPower = true,
        };
        player.Morale.AddRange([ordinary, godPower]);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", bors.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        PassResponses(game);
        var activate = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-bors-strong", activate.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: activate.PromptId,
            Choice: "yes")).Accepted);

        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-payment", payment.Kind);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId,
            CardInstanceIds: [godPower.InstanceId])).Accepted);

        Assert.True(bors.HasStrongAttack);
        Assert.False(ordinary.Tapped);
        Assert.True(godPower.Tapped);
        Assert.True(godPower.IsGodPower);
    }

    [Fact]
    public void PrometheusTakesOneOlympusCardThenReturnsAllRemainingCardsTogether()
    {
        var game = Create(6315);
        var playerIndex = game.State.ActivePlayer;
        var player = game.State.Players[playerIndex];
        var prometheus = Card("S02-05M2", "prometheus-master-source");
        player.Field[0][0] = prometheus;
        player.Hand.Clear();
        player.Library.Clear();
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = "prometheus-power", CardId = "S02-05C1", Tapped = false, IsGodPower = true,
        });
        var olympus = Card("S02-0502", "prometheus-olympus");
        var first = Card("S02-0003", "prometheus-first");
        var second = Card("S02-0402", "prometheus-second");
        player.Library.AddRange([olympus, first, second]);
        game.State.Phase = L12Phase.Main;

        var activation = game.Handle(playerIndex, new L12Command("activateAbility", prometheus.InstanceId,
            Ability: "prometheusTopThree"));
        Assert.True(activation.Accepted, activation.Error);
        PassResponses(game);

        var pick = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-prometheus-pick", pick.Data["action"]);
        Assert.Contains(olympus.InstanceId, pick.ValidChoices);
        Assert.True(game.Handle(playerIndex, new L12Command("resolvePrompt", PromptId: pick.PromptId,
            Choice: olympus.InstanceId)).Accepted);

        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("all-top-bottom", order.Data["placementMode"]);
        Assert.True(game.Handle(playerIndex, new L12Command("resolvePrompt", PromptId: order.PromptId,
            BottomCardInstanceIds: [second.InstanceId, first.InstanceId])).Accepted);

        Assert.Contains(olympus, player.Hand);
        Assert.Equal([second.InstanceId, first.InstanceId], player.Library.Select(card => card.InstanceId));
        Assert.True(player.Morale.Single(card => card.InstanceId == "prometheus-power").Tapped);
        Assert.True(player.Morale.Single(card => card.InstanceId == "prometheus-power").IsGodPower);
    }

    [Fact]
    public void PrometheusMasterSnapshotAndRealMasterActivationConsumeButDoNotFlipGodPower()
    {
        var game = CreateWithFirstMaster("S02-05M2", 63150);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Library.Clear();
        player.Morale.Clear();
        var godPower = new L12MoraleCard
        {
            InstanceId = "prometheus-real-power", CardId = "S02-05C1", Tapped = false, IsGodPower = true,
        };
        player.Morale.Add(godPower);
        player.Library.AddRange([
            Card("S02-0502", "prometheus-real-olympus"),
            Card("S02-0003", "prometheus-real-second"),
            Card("S02-0402", "prometheus-real-third"),
        ]);
        game.State.Phase = L12Phase.Main;

        var snapshot = JsonSerializer.Serialize(game.SnapshotFor(0));
        Assert.Contains("prometheusTopThree", snapshot);
        var activation = game.Handle(0, new L12Command("activateAbility", player.MasterId,
            Ability: "prometheusTopThree"));
        Assert.True(activation.Accepted, activation.Error);
        Assert.True(godPower.Tapped);
        Assert.True(godPower.IsGodPower);
        PassResponses(game);
        Assert.Contains(game.State.PendingPrompts, prompt => prompt.Data.GetValueOrDefault("action") == "s2-prometheus-pick");
    }

    [Fact]
    public void HannibalConsumesGodPowerWithoutFlippingItBackToMorale()
    {
        var game = Create(63151);
        var player = game.State.Players[0];
        var enemyPlayer = game.State.Players[1];
        var hannibal = Card("S02-0516", "hannibal-attacker");
        var ownTarget = Card("S02-0004", "hannibal-own-target");
        var enemyTarget = Card("S02-0004", "hannibal-enemy-target");
        hannibal.SummonRound = -1;
        ownTarget.SummonRound = -1;
        enemyTarget.SummonRound = -1;
        player.Field[0][0] = hannibal;
        player.Field[0][1] = ownTarget;
        enemyPlayer.Field[0][0] = enemyTarget;
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = "hannibal-power", CardId = "S02-05C1", Tapped = false, IsGodPower = true,
        });
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", hannibal.InstanceId,
            Target: new L12AttackTarget("legion", enemyTarget.InstanceId))).Accepted);
        PassResponses(game);
        var pay = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-hannibal-pay", pay.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: pay.PromptId,
            Choice: "yes")).Accepted);

        var own = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-hannibal-own", own.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: own.PromptId,
            Choice: ownTarget.InstanceId)).Accepted);

        var enemy = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-hannibal-enemy", enemy.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: enemy.PromptId,
            Choice: enemyTarget.InstanceId)).Accepted);

        var power = Assert.Single(player.Morale, card => card.InstanceId == "hannibal-power");
        Assert.True(power.Tapped);
        Assert.True(power.IsGodPower);
        Assert.True(ownTarget.Troops < ownTarget.BaseTroops);
        Assert.True(enemyTarget.Troops < enemyTarget.BaseTroops);
    }

    [Fact]
    public void KusanagiPlacedAsALegionKeepsFiveThousandTroopsAfterAttackingAMaster()
    {
        var game = CreateWithFirstMaster("S01-04M2", 63152);
        var player = game.State.Players[0];
        var defender = game.State.Players[1];
        var sword = Card("S01-0417", "kusanagi-legion");
        sword.SummonRound = 0;
        player.Relic = sword;
        defender.Field[0] = new L12CardInstance?[3];
        defender.Field[1] = new L12CardInstance?[3];
        AddMorale(player, 2);
        game.State.Round = 2;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "S01-04M2",
            Ability: "kusanagi")).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: "0:0")).Accepted);
        PassResponses(game);

        Assert.Same(sword, player.Field[0][0]);
        Assert.Null(player.Relic);
        Assert.Equal(5000, sword.Troops);
        Assert.Equal(5000, sword.SetTroopsValue);
        Assert.Equal(int.MaxValue, sword.SetTroopsUntilTurn);

        Assert.True(game.Handle(0, new L12Command("attack", sword.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        PassResponses(game);
        Assert.True(game.Handle(1, new L12Command("resolveDefense")).Accepted);

        Assert.Same(sword, player.Field[0][0]);
        Assert.DoesNotContain(sword, player.Graveyard);
        Assert.Equal(5000, sword.Troops);
    }

    [Fact]
    public void KusanagiKeepsItsOriginalArtifactEntryTurnWhenPlacedAsALegion()
    {
        var game = CreateWithFirstMaster("S01-04M2", 63155);
        var player = game.State.Players[0];
        var sword = Card("S01-0417", "kusanagi-same-turn-legion");
        sword.SummonRound = game.State.Round;
        player.Relic = sword;
        AddMorale(player, 2);
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "S01-04M2",
            Ability: "kusanagi")).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: "0:0")).Accepted);
        PassResponses(game);

        Assert.Same(sword, player.Field[0][0]);
        Assert.Equal(game.State.Round, sword.SummonRound);
        Assert.DoesNotContain(sword.InstanceId, game.SnapshotFor(0).LegalAttackTargets.Keys);

        game.State.Round++;
        Assert.Contains("master", game.SnapshotFor(0).LegalAttackTargets[sword.InstanceId]);
    }

    [Fact]
    public void KusanagiRealHandPlayKeepsSummonSicknessOnlyForItsArtifactEntryRound()
    {
        var game = CreateWithFirstMaster("S01-04M2", 631551);
        var player = game.State.Players[0];
        var sword = Card("S01-0417", "kusanagi-real-play");
        player.Hand.Clear();
        player.Hand.Add(sword);
        AddMorale(player, 6);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", sword.InstanceId)).Accepted);
        PassResponses(game);
        Assert.Same(sword, player.Relic);
        Assert.Equal(game.State.Round, sword.SummonRound);

        foreach (var morale in player.Morale) morale.Tapped = false;
        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "kusanagi")).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId, Choice: "0:0")).Accepted);
        PassResponses(game);
        Assert.DoesNotContain(sword.InstanceId, game.SnapshotFor(0).LegalAttackTargets.Keys);

        sword.HasCharge = true;
        Assert.Contains("master", game.SnapshotFor(0).LegalAttackTargets[sword.InstanceId]);
        sword.HasCharge = false;

        game.State.Round++;
        Assert.Contains("master", game.SnapshotFor(0).LegalAttackTargets[sword.InstanceId]);
    }

    [Fact]
    public void KusanagiPlayedAsArtifactOnAPriorRoundCanAttackImmediatelyAfterLaterTransformation()
    {
        var game = CreateWithFirstMaster("S01-04M2", 631552);
        var player = game.State.Players[0];
        var sword = Card("S01-0417", "kusanagi-prior-round-play");
        player.Hand.Clear();
        player.Hand.Add(sword);
        AddMorale(player, 6);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", sword.InstanceId)).Accepted);
        PassResponses(game);
        var artifactEntryRound = sword.SummonRound;
        game.State.Round = artifactEntryRound + 1;
        foreach (var morale in player.Morale) morale.Tapped = false;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "kusanagi")).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId, Choice: "0:0")).Accepted);
        PassResponses(game);

        Assert.Equal(artifactEntryRound, sword.SummonRound);
        Assert.Contains("master", game.SnapshotFor(0).LegalAttackTargets[sword.InstanceId]);
    }

    [Fact]
    public void PlayingANewArtifactDoesNotReplaceBattlefieldKusanagiLegion()
    {
        var game = CreateWithFirstMaster("S01-04M2", 63153);
        var player = game.State.Players[0];
        var sword = Card("S01-0417", "kusanagi-replaced");
        L12DerivedStats.SetUntilTurnEnd(sword, 5000, int.MaxValue);
        player.Field[0][0] = sword;
        var newArtifact = Card("S01-0215", "replacement-artifact");
        player.Hand.Clear();
        player.Hand.Add(newArtifact);
        AddMorale(player, newArtifact.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", newArtifact.InstanceId)).Accepted);

        Assert.Same(sword, player.Field[0][0]);
        Assert.Contains(newArtifact, player.ExtraRelics.Prepend(player.Relic).Where(card => card is not null));
        Assert.DoesNotContain(sword, player.Graveyard);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == sword.InstanceId);
    }

    [Fact]
    public void SusanooFrontBuffAppliesToKusanagiLegionAttack()
    {
        var game = CreateWithFirstMaster("S01-04M2", 631531);
        var player = game.State.Players[0];
        var sword = Card("S01-0417", "kusanagi-susano-buff");
        L12DerivedStats.SetUntilTurnEnd(sword, 5000, int.MaxValue);
        sword.SummonRound = 0;
        player.Field[0][0] = sword;
        AddMorale(player, 1);
        game.State.Round = 2;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "frontBuff")).Accepted);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(sword.InstanceId, target.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: target.PromptId,
            Choice: sword.InstanceId)).Accepted);
        PassResponses(game);

        Assert.True(game.Handle(0, new L12Command("attack", sword.InstanceId,
            Target: new L12AttackTarget("master", "master-1"))).Accepted);
        Assert.Equal(7000, sword.Troops);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("须佐之男", StringComparison.Ordinal)
            && entry.Text.Contains("+2000", StringComparison.Ordinal));
    }

    [Fact]
    public void SusanooMayReturnKusanagiFromTheArtifactZoneToTheLibraryTopWhenItLeaves()
    {
        var game = CreateWithFirstMaster("S01-04M2", 63154);
        var player = game.State.Players[0];
        var sword = Card("S01-0417", "kusanagi-relic-leaves");
        player.Relic = sword;

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: sword.InstanceId)).Accepted);
        PassResponses(game);
        var returnTop = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("kusanagi-return-top", returnTop.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: returnTop.PromptId,
            Choice: "yes")).Accepted);

        Assert.Same(sword, player.Library[0]);
        Assert.DoesNotContain(sword, player.Graveyard);
        Assert.Equal(sword.BaseTroops, sword.Troops);
        Assert.Null(sword.SetTroopsValue);
    }

    [Fact]
    public void TenkaFubuFrontAttackBonusExistsOnlyDuringThatAttack()
    {
        var game = Create(6316);
        var playerIndex = game.State.ActivePlayer;
        var defenderIndex = 1 - playerIndex;
        var player = game.State.Players[playerIndex];
        player.Hand.Clear();
        var tactic = Card("S02-0406", "tenka-front-source");
        var attacker = Card("S02-0402", "tenka-front-attacker");
        attacker.SummonRound = 0;
        player.Hand.Add(tactic);
        player.Field[0][0] = attacker;
        AddMorale(player, tactic.Cost);
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(playerIndex, new L12Command("playCard", tactic.InstanceId)).Accepted);
        PassResponses(game);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(playerIndex, new L12Command("resolvePrompt", PromptId: mode.PromptId,
            Choice: "front-attack")).Accepted);

        var printedTroops = attacker.Troops;
        Assert.True(game.Handle(playerIndex, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        Assert.Equal(printedTroops + 1000, attacker.Troops);
        PassResponses(game);
        Assert.True(game.Handle(defenderIndex, new L12Command("resolveDefense")).Accepted);
        Assert.Equal(printedTroops, attacker.Troops);
    }

    [Fact]
    public void TenkaFubuCanDebuffOneRowOrGrantEachCurrentActiveLegionOneFreeMove()
    {
        var rowGame = Create(6317);
        var rowPlayerIndex = rowGame.State.ActivePlayer;
        var rowPlayer = rowGame.State.Players[rowPlayerIndex];
        var enemy = rowGame.State.Players[1 - rowPlayerIndex];
        rowPlayer.Hand.Clear();
        var rowTactic = Card("S02-0406", "tenka-row-source");
        var frontEnemy = Card("S02-0004", "tenka-row-front");
        var backEnemy = Card("S02-0007", "tenka-row-back");
        rowPlayer.Hand.Add(rowTactic);
        enemy.Field[0][0] = frontEnemy;
        enemy.Field[1][0] = backEnemy;
        AddMorale(rowPlayer, rowTactic.Cost);
        rowGame.State.Phase = L12Phase.Main;

        Assert.True(rowGame.Handle(rowPlayerIndex, new L12Command("playCard", rowTactic.InstanceId)).Accepted);
        PassResponses(rowGame);
        var rowMode = Assert.Single(rowGame.State.PendingPrompts);
        Assert.True(rowGame.Handle(rowPlayerIndex, new L12Command("resolvePrompt", PromptId: rowMode.PromptId,
            Choice: "row-cost")).Accepted);
        var rowChoice = Assert.Single(rowGame.State.PendingPrompts);
        Assert.True(rowGame.Handle(rowPlayerIndex, new L12Command("resolvePrompt", PromptId: rowChoice.PromptId,
            Choice: "row:0")).Accepted);
        Assert.Equal(-2, frontEnemy.CostModifier);
        Assert.Equal(0, backEnemy.CostModifier);

        var moveGame = Create(6318);
        var movePlayerIndex = moveGame.State.ActivePlayer;
        var movePlayer = moveGame.State.Players[movePlayerIndex];
        movePlayer.Hand.Clear();
        var moveTactic = Card("S02-0406", "tenka-move-source");
        var mover = Card("S02-0402", "tenka-mover");
        movePlayer.Hand.Add(moveTactic);
        movePlayer.Field[0][0] = mover;
        AddMorale(movePlayer, moveTactic.Cost);
        moveGame.State.Phase = L12Phase.Main;

        Assert.True(moveGame.Handle(movePlayerIndex, new L12Command("playCard", moveTactic.InstanceId)).Accepted);
        PassResponses(moveGame);
        var moveMode = Assert.Single(moveGame.State.PendingPrompts);
        Assert.True(moveGame.Handle(movePlayerIndex, new L12Command("resolvePrompt", PromptId: moveMode.PromptId,
            Choice: "free-move")).Accepted);
        Assert.True(moveGame.Handle(movePlayerIndex, new L12Command("move", mover.InstanceId, Row: 0, Slot: 1)).Accepted);
        var secondMove = moveGame.Handle(movePlayerIndex, new L12Command("move", mover.InstanceId, Row: 0, Slot: 2));
        Assert.False(secondMove.Accepted);
    }

    [Fact]
    public void TakedaSearchesThenSummonsSanadaAndReadiesOneMorale()
    {
        var game = Create(6319);
        var playerIndex = game.State.ActivePlayer;
        var player = game.State.Players[playerIndex];
        player.Hand.Clear();
        player.Library.Clear();
        var takeda = Card("S02-0401", "takeda-source");
        var sanada = Card("S01-0404", "takeda-sanada");
        var searched = Card("S02-0402", "takeda-searched");
        var ineligible = Card("S02-0401", "takeda-ineligible");
        player.Hand.AddRange([takeda, sanada]);
        player.Library.AddRange([searched, ineligible]);
        AddMorale(player, takeda.Cost);
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(playerIndex,
            new L12Command("playCard", takeda.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);

        var search = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-takeda-search", search.Data["action"]);
        Assert.Contains(searched.InstanceId, search.ValidChoices);
        Assert.DoesNotContain(ineligible.InstanceId, search.ValidChoices);
        Assert.True(game.Handle(playerIndex, new L12Command("resolvePrompt", PromptId: search.PromptId,
            Choice: searched.InstanceId)).Accepted);

        var sanadaChoice = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-takeda-sanada", sanadaChoice.Data["action"]);
        Assert.True(game.Handle(playerIndex, new L12Command("resolvePrompt", PromptId: sanadaChoice.PromptId,
            Choice: sanada.InstanceId)).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-takeda-sanada-slot", slot.Data["action"]);
        Assert.True(game.Handle(playerIndex, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: "1:1")).Accepted);

        var morale = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-takeda-ready-morale", morale.Data["action"]);
        var moraleId = morale.ValidChoices[0];
        Assert.True(game.Handle(playerIndex, new L12Command("resolvePrompt", PromptId: morale.PromptId,
            Choice: moraleId)).Accepted);

        Assert.Contains(searched, player.Hand);
        Assert.Same(sanada, player.Field[1][1]);
        Assert.False(sanada.Tapped);
        Assert.False(player.Morale.Single(card => card.InstanceId == moraleId).Tapped);
    }

    [Fact]
    public void TakedaPreventsMoraleFromBeingReadiedByAMasterEffect()
    {
        var game = CreateWithFirstMaster("S01-04M1", 6320);
        var playerIndex = 0;
        var player = game.State.Players[playerIndex];
        player.Hand.Clear();
        player.Hand.Add(Card("S02-0003", "amaterasu-discard"));
        player.Field[0][0] = Card("S02-0401", "takeda-static");
        AddMorale(player, 2);
        foreach (var morale in player.Morale) morale.Tapped = true;
        game.State.Phase = L12Phase.Main;

        var activation = game.Handle(playerIndex, new L12Command("activateAbility", $"master-{playerIndex}",
            Ability: "amaterasuReady"));
        Assert.True(activation.Accepted, activation.Error);
        PassResponses(game);
        var discard = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("amaterasu-discard", discard.Data["action"]);
        Assert.True(game.Handle(playerIndex, new L12Command("resolvePrompt", PromptId: discard.PromptId,
            Choice: "amaterasu-discard")).Accepted);

        Assert.All(player.Morale, morale => Assert.True(morale.Tapped));
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-prevented");
    }

    [Fact]
    public void PoisonNegatesEveryMoraleReadyEventFromOneAmaterasuEffect()
    {
        var game = CreateWithFirstMaster("S01-04M1", 63201);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        player.Hand.Clear();
        player.Hand.Add(Card("S02-0003", "amaterasu-poison-discard"));
        player.Hand.Add(Card("S02-0007", "poison-forced-discard"));
        AddMorale(player, 2);
        foreach (var morale in player.Morale) morale.Tapped = true;
        var poison = Card("S02-0018", "amaterasu-poison-counter");
        poison.Hidden = true;
        poison.SetRound = 0;
        opponent.Field[1][0] = poison;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        var activation = game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "amaterasuReady"));
        Assert.True(activation.Accepted, activation.Error);
        PassResponses(game);
        var discard = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: discard.PromptId,
            Choice: "amaterasu-poison-discard")).Accepted);

        var poisonResponse = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, poisonResponse.PlayerIndex);
        Assert.Contains(poison.InstanceId, poisonResponse.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: poisonResponse.PromptId,
            Choice: poison.InstanceId)).Accepted);
        PassResponses(game);
        var forcedDiscard = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-poison-discard", forcedDiscard.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: forcedDiscard.PromptId,
            Choice: forcedDiscard.ValidChoices[0])).Accepted);
        PassResponses(game);

        Assert.All(player.Morale, morale => Assert.True(morale.Tapped));
    }

    [Fact]
    public void MorriganGainsOneRuneOnEnemyDeathAndCanMarkAnOtherworldLegionToReadyAfterItsNextKill()
    {
        var game = CreateWithFirstMaster("S02-06M1", 6321);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var attacker = Card("S02-0615", "morrigan-attacker");
        attacker.SummonRound = 0;
        var firstTarget = Card("S02-0005", "morrigan-first-target");
        player.Field[0][0] = attacker;
        enemy.Field[0][0] = firstTarget;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", firstTarget.InstanceId))).Accepted);
        PassResponses(game);

        var gainRune = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-morrigan-enemy-death", gainRune.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: gainRune.PromptId,
            Choice: "yes")).Accepted);
        Assert.Equal(1, player.SpecialZones.Runes);

        player.SpecialZones.Runes = 2;
        var activation = game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "morriganReadyOnKill"));
        Assert.True(activation.Accepted, activation.Error);
        var choose = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", choose.Continuation);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: choose.PromptId,
            Choice: attacker.InstanceId)).Accepted);
        PassResponses(game);

        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Equal(game.State.TurnSerial, attacker.ReadyAfterNextKillUntilTurn);
        Assert.Equal("莫瑞甘", attacker.ReadyAfterNextKillSourceName);

        attacker.Tapped = false;
        var secondTarget = Card("S02-0005", "morrigan-second-target");
        enemy.Field[0][0] = secondTarget;
        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", secondTarget.InstanceId))).Accepted);
        PassResponses(game);

        Assert.False(attacker.Tapped);
        Assert.Equal(-1, attacker.ReadyAfterNextKillUntilTurn);
        Assert.Null(attacker.ReadyAfterNextKillSourceName);
        Assert.Equal(0, player.SpecialZones.Runes);
    }

    [Fact]
    public void AvalonAdvancesTrialAndGainsRuneAtTurnStart()
    {
        var game = CreateWithFirstMaster("S02-06D1", 6322);
        var player = game.State.Players[0];
        player.SpecialZones.Trials.Clear();
        var trial = Card("S02-06S4", "avalon-trial");
        player.SpecialZones.Trials.Add(trial);

        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);

        Assert.Equal(1, trial.TrialProgress);
        Assert.Equal(1, player.SpecialZones.Runes);
    }

    [Fact]
    public void AvalonRecoversLegionAndTacticThenGrantsTheNextTacticFree()
    {
        var game = CreateWithFirstMaster("S02-06D1", 6323);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Graveyard.Clear();
        var legion = Card("S02-0602", "avalon-legion");
        var tactic = Card("S02-0620", "avalon-tactic");
        player.Graveyard.AddRange([legion, tactic]);
        player.SpecialZones.Runes = 2;
        game.State.Phase = L12Phase.Main;

        var result = game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "avalonRecover"));
        Assert.True(result.Accepted, result.Error);
        var chooseLegion = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: chooseLegion.PromptId, Choice: legion.InstanceId)).Accepted);
        var chooseTactic = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: chooseTactic.PromptId, Choice: tactic.InstanceId)).Accepted);
        PassResponses(game);

        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Contains(legion, player.Hand);
        Assert.Contains(tactic, player.Hand);
        Assert.Equal(1, player.FreeTacticCount);
    }

    [Fact]
    public void AvalonRestsPersistentlyAndDebuffsOnlyTheDeclaredEnemyLegion()
    {
        var game = CreateWithFirstMaster("S02-06D1", 6324);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var target = Card("S02-0005", "avalon-target");
        var other = Card("S02-0005", "avalon-other");
        enemy.Field[0][0] = target;
        enemy.Field[0][1] = other;
        game.State.Phase = L12Phase.Main;

        var result = game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "avalonDebuff"));
        Assert.True(result.Accepted, result.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: target.InstanceId)).Accepted);
        PassResponses(game);

        Assert.True(player.MasterTapped);
        Assert.Null(enemy.Field[0][0]);
        Assert.Contains(target, enemy.Graveyard);
        Assert.Equal(target.BaseTroops, target.Troops);
        Assert.Equal(other.BaseTroops, other.Troops);
        var second = game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "avalonDebuff"));
        Assert.False(second.Accepted);
    }

    [Fact]
    public void NephthysPreventsOwnTombGuardFromAttackingAMaster()
    {
        var game = CreateWithFirstMaster("S02-02M1", 6325);
        var guard = Card("S01-0212", "nephthys-tomb-guard");
        guard.SummonRound = -1;
        game.State.Players[0].Field[0][0] = guard;
        game.State.Phase = L12Phase.Main;

        var result = game.Handle(0, new L12Command("attack", guard.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.False(result.Accepted);
        Assert.Contains("奈芙蒂斯", result.Error);
        Assert.False(guard.Tapped);
    }

    [Fact]
    public void NephthysSacrificesAnyNumberOfLegionsAndDiscountsOnlyTheNextSunDisasterLegion()
    {
        var game = CreateWithFirstMaster("S02-02M1", 6326);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Graveyard.Clear();
        var first = Card("S02-0005", "nephthys-sacrifice-first");
        var second = Card("S02-0007", "nephthys-sacrifice-second");
        player.Field[0][0] = first;
        player.Field[1][1] = second;
        game.State.Phase = L12Phase.Main;

        var activation = game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "nephthysSacrifice"));
        Assert.True(activation.Accepted, activation.Error);
        var choose = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: choose.PromptId,
            CardInstanceIds: [first.InstanceId, second.InstanceId])).Accepted);
        PassResponses(game);

        Assert.Contains(first, player.Graveyard);
        Assert.Contains(second, player.Graveyard);
        Assert.Equal(2, player.NextS2SunDisasterLegionDiscount);

        var discounted = Card("S01-0203", "nephthys-discounted-legion");
        player.Hand.Add(discounted);
        AddMorale(player, discounted.Cost - 2);
        Assert.True(game.Handle(0, new L12Command("playCard", discounted.InstanceId, Row: 0, Slot: 0)).Accepted);
        Assert.Equal(0, player.NextS2SunDisasterLegionDiscount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NephthysAndImhotepDiscountsAccumulateInEitherOrder(bool imhotepFirst)
    {
        var game = CreateWithFirstMaster("S02-02M1", imhotepFirst ? 63261 : 63262);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Graveyard.Clear();
        var first = Card("S02-0005", "stacked-discount-first");
        var second = Card("S02-0007", "stacked-discount-second");
        var imhotep = Card("S02-0204", "stacked-discount-imhotep");
        player.Field[0][0] = first;
        player.Field[1][1] = second;
        player.Field[1][2] = imhotep;
        game.State.Phase = L12Phase.Main;

        void ResolveImhotep()
        {
            var result = game.Handle(0, new L12Command("activateAbility", imhotep.InstanceId,
                Ability: "imhotepDiscount"));
            Assert.True(result.Accepted, result.Error);
            PassResponses(game);
        }

        void ResolveNephthys()
        {
            var result = game.Handle(0, new L12Command("activateAbility", "master-0",
                Ability: "nephthysSacrifice"));
            Assert.True(result.Accepted, result.Error);
            var choose = Assert.Single(game.State.PendingPrompts);
            Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: choose.PromptId,
                CardInstanceIds: [first.InstanceId, second.InstanceId])).Accepted);
            PassResponses(game);
        }

        if (imhotepFirst)
        {
            ResolveImhotep();
            ResolveNephthys();
        }
        else
        {
            ResolveNephthys();
            ResolveImhotep();
        }

        Assert.Equal(3, player.NextS2SunDisasterLegionDiscount);
        var discounted = Card("S01-0203", "stacked-discount-target");
        player.Hand.Add(discounted);
        AddMorale(player, discounted.Cost - 3);
        var play = game.Handle(0, new L12Command("playCard", discounted.InstanceId, Row: 0, Slot: 1));
        Assert.True(play.Accepted, play.Error);
        Assert.Equal(0, player.NextS2SunDisasterLegionDiscount);
    }

    [Fact]
    public void NephthysSummonsAScarabIntoAChosenSlotAfterOwnSunLegionDiesOnOpponentTurn()
    {
        var game = CreateWithFirstMaster("S02-02M1", 6327);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        player.Graveyard.Clear();
        var scarab = Card("S02-0201", "nephthys-grave-scarab");
        player.Graveyard.Add(scarab);
        var victim = Card("S01-0206", "nephthys-sun-victim");
        player.Field[0][0] = victim;
        var attacker = Card("S01-0101", "nephthys-enemy-attacker");
        attacker.SummonRound = -1;
        enemy.Field[0][0] = attacker;
        game.State.ActivePlayer = 1;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(1, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", victim.InstanceId))).Accepted);
        PassResponses(game);

        var trigger = Assert.Single(game.State.PendingPrompts);
        if (trigger.Kind == "trigger-order")
        {
            var ordered = trigger.ValidChoices
                .OrderByDescending(id => trigger.Data.GetValueOrDefault(id)?.Contains("奈芙蒂斯") == true)
                .ToList();
            Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: trigger.PromptId,
                CardInstanceIds: ordered)).Accepted);
            PassResponses(game);
            trigger = Assert.Single(game.State.PendingPrompts);
        }
        Assert.Equal("s2-nephthys-own-death", trigger.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: trigger.PromptId, Choice: "yes")).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-nephthys-scarab-slot", slot.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId, Choice: "1:2")).Accepted);

        Assert.Same(scarab, player.Field[1][2]);
        Assert.False(scarab.Tapped);
        Assert.Contains($"s2-nephthys-scarab:{game.State.TurnSerial}", player.UsedAbilities);
    }

    [Fact]
    public void LiMuMayPlayAnEligibleTopActiveTacticForFreeBeforeChoosingWhetherToDraw()
    {
        var game = Create(6328);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Library.Clear();
        var liMu = Card("S02-0102", "limu-enter");
        var runePower = Card("S02-0620", "limu-free-tactic");
        var filler = Card("S02-0609", "limu-filler");
        player.Hand.Add(liMu);
        player.Library.AddRange([runePower, filler]);
        AddMorale(player, liMu.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", liMu.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        var reveal = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-limu-reveal", reveal.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: reveal.PromptId, Choice: "yes")).Accepted);

        var revealEvent = Assert.Single(game.State.Events,
            entry => entry.Type == "reveal" && entry.Text.StartsWith("李牧登场时", StringComparison.Ordinal));
        Assert.Equal("李牧登场时，展示牌库顶的1张牌。", revealEvent.Text);
        Assert.Collection(revealEvent.Cards, card => Assert.Equal(runePower.CardId, card.CardId));

        var tactic = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-limu-tactic", tactic.Data["action"]);
        Assert.Equal(runePower.InstanceId, tactic.Data["previewCardId"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: tactic.PromptId, Choice: "play")).Accepted);

        var draw = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-limu-draw", draw.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: draw.PromptId, Choice: "no")).Accepted);
        PassResponses(game);

        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.Contains(runePower, player.Graveyard);
        Assert.DoesNotContain(runePower, player.Library);
    }

    [Fact]
    public void LiMuReturnsAnIneligibleRevealedCardToBottomThenMayDraw()
    {
        var game = Create(6329);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Library.Clear();
        var liMu = Card("S02-0102", "limu-ineligible-enter");
        var ineligible = Card("S02-0609", "limu-ineligible-top");
        var drawn = Card("S02-0604", "limu-drawn-card");
        player.Hand.Add(liMu);
        player.Library.AddRange([ineligible, drawn]);
        AddMorale(player, liMu.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", liMu.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        var reveal = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: reveal.PromptId, Choice: "yes")).Accepted);

        var draw = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-limu-draw", draw.Data["action"]);
        Assert.Equal(drawn, player.Library[0]);
        Assert.Equal(ineligible, player.Library[^1]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: draw.PromptId, Choice: "yes")).Accepted);

        Assert.Contains(drawn, player.Hand);
        Assert.Equal(ineligible, Assert.Single(player.Library));
    }

    [Fact]
    public void LiMuTriggersOnlyWhenFourMoraleAreReturnedByAMasterEffect()
    {
        var game = CreateWithFirstMaster("S01-01M1", 6330);
        var player = game.State.Players[0];
        player.Field[0][0] = Card("S02-0102", "limu-morale-trigger");
        player.Field[0][1] = Card("S02-0609", "limu-front-fill-one");
        player.Field[0][2] = Card("S02-0609", "limu-front-fill-two");
        AddMorale(player, 4);
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "nonLethal")).Accepted);
        PassResponses(game);

        var prompt = Assert.Single(game.State.PendingPrompts,
            candidate => candidate.Data.GetValueOrDefault("action") == "s2-limu-morale");
        Assert.Equal("s2-limu-morale", prompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "yes")).Accepted);

        var morale = Assert.Single(player.Morale);
        Assert.True(morale.Tapped);
        Assert.Contains(player.UsedAbilities, key => key.StartsWith("trigger:limu-morale:", StringComparison.Ordinal));
    }

    [Fact]
    public void YangJianMaySummonXiaotianAfterReturningFourMorale()
    {
        var game = CreateWithFirstMaster("S01-01M1", 6331);
        var player = game.State.Players[0];
        AddMorale(player, 4);
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "nonLethal")).Accepted);
        PassResponses(game);

        var prompt = Assert.Single(game.State.PendingPrompts,
            candidate => candidate.Data.GetValueOrDefault("action") == "s2-xiaotian-morale");
        Assert.Equal("s2-xiaotian-morale", prompt.Data["action"]);
        Assert.Equal("false", prompt.Data[$"{prompt.Data["previewCardId"]}:hasPrintedCost"]);
        Assert.DoesNotContain($"{prompt.Data["previewCardId"]}:cost", prompt.Data.Keys);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "yes")).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts,
            candidate => candidate.Data.GetValueOrDefault("action") == "s2-xiaotian-slot");
        Assert.Equal("s2-xiaotian-slot", slot.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId, Choice: "0:1")).Accepted);

        var xiaotian = Assert.IsType<L12CardInstance>(player.Field[0][1]);
        Assert.Equal("S02-01S1", xiaotian.CardId);
        Assert.False(xiaotian.HasPrintedCost);
        Assert.False(xiaotian.Tapped);
        Assert.Equal(2000, xiaotian.Troops);
    }

    [Fact]
    public void DerivedCardsVanishOnEveryFieldExitInsteadOfEnteringOrdinaryZones()
    {
        var game = Create(6332);
        var player = game.State.Players[0];

        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, "S02-01S1", Row: 0, Slot: 0,
            TriggerEffects: false)).Accepted);
        var returned = Assert.IsType<L12CardInstance>(player.Field[0][0]);
        Assert.False(returned.HasPrintedCost);
        Assert.True(game.HandleGm(new L12GmCommand("returnCardToHand", 0,
            CardInstanceId: returned.InstanceId)).Accepted);
        AssertDerivedCardIsInNoOrdinaryZone(player, returned.InstanceId);

        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, "S02-01S1", Row: 0, Slot: 0,
            TriggerEffects: false)).Accepted);
        var defeated = Assert.IsType<L12CardInstance>(player.Field[0][0]);
        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: defeated.InstanceId)).Accepted);
        AssertDerivedCardIsInNoOrdinaryZone(player, defeated.InstanceId);
        Assert.Contains(game.State.Events, entry => entry.Type == "derived-vanished"
            && entry.Text.Contains("离场时消灭", StringComparison.Ordinal));

        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, "S02-0504", Row: 0, Slot: 1,
            TriggerEffects: false)).Accepted);
        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, "S02-01S1", Row: 0, Slot: 2,
            TriggerEffects: false)).Accepted);
        var host = Assert.IsType<L12CardInstance>(player.Field[0][1]);
        var attached = Assert.IsType<L12CardInstance>(player.Field[0][2]);
        player.Field[0][2] = null;
        host.AttachedCards.Add(attached);
        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: host.InstanceId)).Accepted);
        AssertDerivedCardIsInNoOrdinaryZone(player, attached.InstanceId);
    }

    private static void AssertDerivedCardIsInNoOrdinaryZone(L12PlayerState player, string instanceId)
    {
        Assert.DoesNotContain(player.Hand, card => card.InstanceId == instanceId);
        Assert.DoesNotContain(player.Library, card => card.InstanceId == instanceId);
        Assert.DoesNotContain(player.Graveyard, card => card.InstanceId == instanceId);
        Assert.DoesNotContain(player.Removed, card => card.InstanceId == instanceId);
        Assert.DoesNotContain(player.Field.SelectMany(row => row), card => card?.InstanceId == instanceId);
    }

    [Fact]
    public void CatalogLoadsTraitsAndProfessionFromSupplementalMetadata()
    {
        var arthur = Catalog.Cards["S02-0601"];
        Assert.Equal(["彼界", "圆桌骑士"], arthur.Traits);
        Assert.Equal("领军", arthur.Profession);

        var promoted = Catalog.Cards["S02-0501"];
        Assert.Equal(["奥林匹斯", "晋升者"], promoted.Traits);
        Assert.Equal("斗士", promoted.Profession);

        Assert.Contains("杨戬专属", Catalog.Cards["S02-01S1"].Traits);
    }

    [Fact]
    public void RoundTableDomainSearchAndBuffUseRoundTableTrait()
    {
        var game = Create(6332);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Library.Clear();
        var tactic = Card("S02-0621", "round-table-domain");
        var searchedKnight = Card("S02-0601", "round-table-searched");
        var nonKnight = Card("S02-0612", "round-table-non-knight");
        var fieldKnight = Card("S02-0605", "round-table-field");
        player.Hand.Add(tactic);
        player.Library.AddRange([searchedKnight, nonKnight]);
        player.Field[0][0] = fieldKnight;
        AddMorale(player, 3);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        PassResponses(game);

        var search = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-round-table-search", search.Data["action"]);
        Assert.Equal([searchedKnight.InstanceId], search.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: search.PromptId, Choice: searchedKnight.InstanceId)).Accepted);

        var buff = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-round-table-buff", buff.Data["action"]);
        Assert.Contains(fieldKnight.InstanceId, buff.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: buff.PromptId, Choice: fieldKnight.InstanceId)).Accepted);

        Assert.Contains(searchedKnight, player.Hand);
        Assert.Equal(nonKnight, Assert.Single(player.Library));
        Assert.Equal(fieldKnight.BaseTroops + 2000, fieldKnight.Troops);
        Assert.Equal(3, player.Morale.Count(card => card.Tapped));
    }

    [Fact]
    public void MagatamaSearchesOnlyHighHeavenCavalryUsingProfessionMetadata()
    {
        var game = Create(6333);
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Library.Clear();
        var magatama = Card("S02-0404", "magatama-search-source");
        var cavalry = Card("S01-0409", "magatama-cavalry");
        var wrongProfession = Card("S02-0401", "magatama-wrong-profession");
        var wrongFaction = Card("S02-0505", "magatama-wrong-faction");
        player.Hand.Add(magatama);
        player.Library.AddRange([cavalry, wrongProfession, wrongFaction]);
        AddMorale(player, magatama.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", magatama.InstanceId)).Accepted);
        PassResponses(game);

        var search = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-magatama-search", search.Data["action"]);
        Assert.Contains(cavalry.InstanceId, search.ValidChoices);
        Assert.DoesNotContain(wrongProfession.InstanceId, search.ValidChoices);
        Assert.DoesNotContain(wrongFaction.InstanceId, search.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: search.PromptId,
            Choice: cavalry.InstanceId)).Accepted);

        Assert.Contains(cavalry, player.Hand);
        Assert.DoesNotContain(cavalry, player.Library);
    }

    [Fact]
    public void MagatamaMoveUsesCavalryMovementOnEitherBattlefieldAndAllowsRestedLegions()
    {
        var game = Create(6334);
        var player = game.State.Players[0];
        var magatama = Card("S02-0404", "magatama-move-source");
        var legion = Card("S02-0401", "magatama-moving-legion");
        legion.Tapped = true;
        player.Relic = magatama;
        game.State.Players[1].Field[0][1] = legion;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var activationResult = game.Handle(0, new L12Command("activateAbility", magatama.InstanceId,
            Ability: "magatamaMove"));
        Assert.True(activationResult.Accepted, activationResult.Error);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", target.Continuation);
        Assert.Contains(legion.InstanceId, target.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: target.PromptId,
            Choice: legion.InstanceId)).Accepted);

        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("slot", slot.Kind);
        Assert.Equal("1", slot.Data["targetPlayerIndex"]);
        Assert.Contains("1:2", slot.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: "1:2")).Accepted);
        Assert.True(magatama.Tapped);
        PassResponses(game);

        Assert.Null(game.State.Players[1].Field[0][1]);
        Assert.Same(legion, game.State.Players[1].Field[1][2]);
        Assert.True(legion.Tapped);
        Assert.Equal(game.State.TurnSerial, legion.LastMovedTurn);
        Assert.Equal(game.State.TurnSerial, legion.LastCavalryMoveTurn);
    }

    [Fact]
    public void MagatamaBackToFrontMoveEnablesTsukuyomiAttackBonus()
    {
        var game = CreateWithFirstMaster("S02-04M1", 63341);
        var player = game.State.Players[0];
        var magatama = Card("S02-0404", "magatama-tsukuyomi-source");
        var legion = Card("S02-0401", "magatama-tsukuyomi-legion");
        legion.SummonRound = 0;
        player.Relic = magatama;
        player.Field[1][0] = legion;
        game.State.Round = 2;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var activationResult = game.Handle(0, new L12Command("activateAbility", magatama.InstanceId,
            Ability: "magatamaMove"));
        Assert.True(activationResult.Accepted, activationResult.Error);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: target.PromptId,
            Choice: legion.InstanceId)).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: "0:2")).Accepted);
        PassResponses(game);

        var baseTroops = legion.Troops;
        Assert.True(game.Handle(0, new L12Command("attack", legion.InstanceId,
            Target: new L12AttackTarget("master", "master-1"))).Accepted);
        Assert.Equal(baseTroops + 1000, legion.Troops);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("月读", StringComparison.Ordinal)
            && entry.Text.Contains("+1000", StringComparison.Ordinal));
    }

    [Fact]
    public void MagatamaCannotGiveTheSameLegionASecondCavalryMoveThisTurn()
    {
        var game = Create(63342);
        var player = game.State.Players[0];
        var magatama = Card("S02-0404", "magatama-repeat-source");
        var legion = Card("S02-0401", "magatama-already-moved-legion");
        legion.Tapped = true;
        legion.LastCavalryMoveTurn = game.State.TurnSerial;
        player.Relic = magatama;
        player.Field[0][0] = legion;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var result = game.Handle(0, new L12Command("activateAbility", magatama.InstanceId,
            Ability: "magatamaMove"));

        Assert.False(result.Accepted);
        Assert.Contains("没有可进行骑兵位移", result.Error);
        Assert.False(magatama.Tapped);
    }

    [Fact]
    public void MagatamaImmortalTargetsOnlyALegionMovedThisTurn()
    {
        var game = Create(6335);
        var player = game.State.Players[0];
        var magatama = Card("S02-0404", "magatama-immortal-source");
        var moved = Card("S02-0401", "magatama-moved-legion");
        var unmoved = Card("S02-0402", "magatama-unmoved-legion");
        moved.LastMovedTurn = game.State.TurnSerial;
        player.Relic = magatama;
        player.Field[0][0] = moved;
        player.Field[0][1] = unmoved;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", magatama.InstanceId,
            Ability: "magatamaImmortal")).Accepted);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(moved.InstanceId, target.ValidChoices);
        Assert.DoesNotContain(unmoved.InstanceId, target.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: target.PromptId,
            Choice: moved.InstanceId)).Accepted);
        PassResponses(game);

        Assert.True(magatama.Tapped);
        Assert.Equal(1, moved.ImmortalUses);
        Assert.True(moved.ImmortalUntilTurn >= game.State.TurnSerial);
    }

    [Fact]
    public void ArtemisMayFlipARestedMoraleWhenAnOwnRangedLegionIsDefeated()
    {
        var game = CreateWithFirstMaster("S02-05M1", 6336);
        var defender = game.State.Players[0];
        var attackerPlayer = game.State.Players[1];
        defender.Hand.Clear();
        attackerPlayer.Hand.Clear();
        var ranged = Card("S02-0513", "artemis-ranged-defeated");
        var attacker = Card("S02-0004", "artemis-enemy-attacker");
        ranged.SummonRound = attacker.SummonRound = 0;
        defender.Field[0][0] = ranged;
        attackerPlayer.Field[0][0] = attacker;
        defender.Morale.Clear();
        defender.Morale.Add(new L12MoraleCard
        {
            CardId = "S02-05C1", InstanceId = "artemis-rested-morale", Tapped = true,
        });
        game.State.ActivePlayer = 1;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(1, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", ranged.InstanceId))).Accepted);
        PassResponses(game);

        var flip = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-flip-morale");
        Assert.Contains(defender.Morale[0].InstanceId, flip.ValidChoices);
        Assert.Contains("skip", flip.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: flip.PromptId,
            Choice: defender.Morale[0].InstanceId)).Accepted);
        Assert.True(defender.Morale[0].IsGodPower);
        Assert.True(defender.Morale[0].Tapped);
    }

    [Fact]
    public void TrojanHorseMayEnterEnemyFieldThenLeavesAndDrawsAtOwnersNextEnd()
    {
        var game = Create(6337);
        var attackerPlayer = game.State.Players[0];
        var owner = game.State.Players[1];
        attackerPlayer.Hand.Clear();
        owner.Hand.Clear();
        var attacker = Card("S02-0004", "trojan-attacker");
        var horse = Card("S02-0523", "trojan-horse");
        attacker.SummonRound = 0;
        attackerPlayer.Field[0][0] = attacker;
        owner.Hand.Add(horse);
        var libraryBefore = owner.Library.Count;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        PassResponses(game);
        Assert.True(game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: [])).Accepted);
        PassResponses(game);
        var confirm = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-trojan-confirm");
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: confirm.PromptId, Choice: "yes")).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-trojan-slot");
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: slot.PromptId, Choice: "1:1")).Accepted);
        Assert.Same(horse, attackerPlayer.Field[1][1]);
        Assert.True(horse.Hidden);

        game.State.ActivePlayer = 1;
        game.State.Phase = L12Phase.Main;
        game.State.TurnSerial = horse.DiscardAtEndOfTurnUntilTurn;
        Assert.True(game.Handle(1, new L12Command("endTurn")).Accepted);

        Assert.Null(attackerPlayer.Field[1][1]);
        Assert.Contains(horse, owner.Graveyard);
        Assert.Equal(libraryBefore - 1, owner.Library.Count);
    }

    [Fact]
    public void DivinityRecoveryFirstReturnsAnyOlympusCardThenMaySummonFromHandOrGrave()
    {
        var game = CreateWithFirstMaster("S02-05D1", 6338);
        var player = game.State.Players[0];
        player.Graveyard.Clear();
        var recoveredTactic = Card("S02-0522", "divinity-recovered-tactic");
        var summonedLegion = Card("S02-0502", "divinity-summoned-legion");
        player.Graveyard.AddRange([recoveredTactic, summonedLegion]);
        player.Morale.Clear();
        player.Morale.AddRange([
            new L12MoraleCard { CardId = "S02-05C1", InstanceId = "divinity-power-a", IsGodPower = true },
            new L12MoraleCard { CardId = "S02-05C1", InstanceId = "divinity-power-b", IsGodPower = true },
        ]);
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "divinityPower")).Accepted);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: mode.PromptId, Choice: "mode:recover")).Accepted);
        PassResponses(game);
        var recover = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-divinity-recover");
        Assert.Contains(recoveredTactic.InstanceId, recover.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: recover.PromptId,
            Choice: recoveredTactic.InstanceId)).Accepted);
        var summon = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-divinity-hand");
        Assert.Contains(summonedLegion.InstanceId, summon.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: summon.PromptId,
            Choice: summonedLegion.InstanceId)).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-divinity-hand-slot");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId, Choice: "0:0")).Accepted);

        Assert.Contains(recoveredTactic, player.Hand);
        Assert.Same(summonedLegion, player.Field[0][0]);
    }

    [Fact]
    public void TsukuyomiMovementBuildsOneOwnerOrderedBatchForSameTimingTriggers()
    {
        var game = CreateWithFirstMaster("S02-04M1", 6339);
        var player = game.State.Players[0];
        var moved = Card("S02-0401", "tsukuyomi-moved");
        var other = Card("S02-0402", "tsukuyomi-other");
        player.Field[0][0] = moved;
        player.Field[0][2] = other;
        player.Morale.Clear();
        player.Morale.AddRange([
            new L12MoraleCard { CardId = "S02-04C1", InstanceId = "tsukuyomi-active-a" },
            new L12MoraleCard { CardId = "S02-04C1", InstanceId = "tsukuyomi-active-b" },
        ]);
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("move", moved.InstanceId, Row: 1, Slot: 0)).Accepted);

        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-order", order.Kind);
        Assert.Equal(2, order.ValidChoices.Count);
        Assert.Contains(order.ValidChoices, id => order.Data[$"trigger:{id}"] == "active");
        Assert.Contains(order.ValidChoices, id => order.Data[$"sourceInstance:{id}"] == "master-0");
    }

    [Fact]
    public void RestedHippolytaMakesFrontBackMovementFree()
    {
        var game = Create(6340);
        var player = game.State.Players[0];
        var hippolyta = Card("S02-0510", "hippolyta-rested");
        var mover = Card("S02-0502", "hippolyta-mover");
        hippolyta.Tapped = true;
        player.Field[0][0] = mover;
        player.Field[0][2] = hippolyta;
        player.Morale.Clear();
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("move", mover.InstanceId, Row: 1, Slot: 0)).Accepted);
        Assert.Same(mover, player.Field[1][0]);
        Assert.Empty(player.Morale);
    }

    [Fact]
    public void WukongReturnsChosenMoraleAndEntersAsMasterLegionWithMatchingTroops()
    {
        var game = CreateWithFirstMaster("S02-01M1", 6341);
        var player = game.State.Players[0];
        AddMorale(player, 3);
        game.State.Phase = L12Phase.Main;
        var returned = player.Morale.Take(3).Select(card => card.InstanceId).ToArray();

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "wukongTransform")).Accepted);
        var choice = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-selection", choice.Data.GetValueOrDefault("choiceMode"));
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: choice.PromptId,
            CardInstanceIds: [.. returned])).Accepted);
        PassResponses(game);

        var wukong = Assert.Single(player.Field[0], card => card?.IsMasterLegion == true)!;
        Assert.Equal("S02-01M1", wukong.CardId);
        Assert.Equal(3000, wukong.Troops);
        Assert.True(wukong.HasCharge);
        Assert.DoesNotContain(player.Morale, morale => returned.Contains(morale.InstanceId));

        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(game.SnapshotFor(0)));
        var master = snapshot.RootElement.GetProperty("Players")[0].GetProperty("master");
        Assert.True(master.GetProperty("deployedAsLegion").GetBoolean());
        Assert.Equal(JsonValueKind.Null, master.GetProperty("masterImageUrl").ValueKind);
        Assert.Equal("孙悟空", master.GetProperty("MasterName").GetString());
        Assert.Equal(8, master.GetProperty("Hp").GetInt32());
    }

    [Theory]
    [InlineData("S02-0511")]
    [InlineData("S02-0517")]
    public void EnterTurnAttackLegionPermissionUsesVerifiedAtomicRuntime(string cardId)
    {
        var game = Create(63001 + cardId[^1]);
        var player = game.State.Players[0];
        var card = Card(cardId, $"atomic-enter-{cardId}");
        var enemy = Card("S02-0604", $"atomic-enter-target-{cardId}");
        player.Hand.Clear();
        player.Hand.Add(card);
        game.State.Players[1].Field[0][0] = enemy;
        AddMorale(player, card.Cost);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var result = game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0));
        PassResponses(game);

        Assert.True(result.Accepted, result.Error);
        Assert.Equal(game.State.TurnSerial, card.CanAttackLegionsOnSummonUntilTurn);
        Assert.Contains(enemy.InstanceId, game.SnapshotFor(0).LegalAttackTargets[card.InstanceId]);
    }

    [Fact]
    public void WukongUsesFourHumanAssistedStructuredAbilities()
    {
        Assert.True(L12StructuredCardRules.TryGetStructuredAbilities("S02-01M1", out var abilities));
        Assert.Equal(4, abilities.Count);
        Assert.All(abilities, ability => Assert.Equal("human-assisted", ability.ReviewStatus));
        Assert.Contains(abilities, ability => ability.ExecutionModel == "active"
            && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.ReturnMorale));
        Assert.Contains(abilities, ability => ability.Trigger == "leave"
            && ability.Atoms.Any(atom => atom.Parameters.GetValueOrDefault("operation") == "replace-leave-with-return-master-zone"));
    }

    [Fact]
    public void WukongReturnAtEndAsksBeforeAddingRestedMorale()
    {
        var game = CreateWithFirstMaster("S02-01M1", 6342);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        AddMorale(player, 2);
        AddMorale(opponent, 3);
        game.State.Phase = L12Phase.Main;
        var returned = player.Morale.Take(2).Select(card => card.InstanceId).ToArray();

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "wukongTransform")).Accepted);
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId,
            CardInstanceIds: [.. returned])).Accepted);
        PassResponses(game);
        var zeroMoraleTrigger = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "faction-zero-recovery");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: zeroMoraleTrigger.PromptId,
            Choice: "no")).Accepted);

        Assert.True(game.Handle(0, new L12Command("endTurn")).Accepted);
        Assert.DoesNotContain(player.Field.SelectMany(row => row), card => card?.IsMasterLegion == true);
        var optional = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "s2-wukong-return-morale");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: optional.PromptId, Choice: "yes")).Accepted);
        Assert.Single(player.Morale);
        Assert.True(player.Morale[0].Tapped);
        Assert.Equal(1, game.State.ActivePlayer);
    }

    [Fact]
    public void RealPreparationAsksOptionalRingAndThorEffectsBeforeDrawingStartingHand()
    {
        var baseDeck = Catalog.DeckAt(0);
        var setupDeck = new L12PresetDeckDefinition
        {
            Name = "开局可选效果测试牌库",
            MasterId = "S02-03M1",
            CardIds = ["S02-0305", "S02-0301", .. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "s2-setup", "S2SETUP", 6343,
            ["甲", "乙"], [setupDeck, baseDeck], skipPreparation: false, disasterMode: "none");
        var initiative = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(initiative.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: initiative.PromptId, Choice: "first")).Accepted);

        var owner = game.State.Players[0];
        var setupPrompts = game.State.PendingPrompts.Where(prompt => prompt.PlayerIndex == 0
            && prompt.Continuation.StartsWith("setup-s2-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, setupPrompts.Length);
        Assert.Empty(owner.Hand);

        var ring = setupPrompts.Single(prompt => prompt.Continuation == "setup-s2-ring");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: ring.PromptId, Choice: "no")).Accepted);
        var hammer = game.State.PendingPrompts.Single(prompt => prompt.Continuation == "setup-s2-thor-hammer");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: hammer.PromptId, Choice: "yes")).Accepted);

        Assert.Null(owner.Relic);
        Assert.Equal(6, owner.Hand.Count);
        Assert.Contains(owner.Hand, card => card.CardId == "S02-0301");
        Assert.Contains(game.State.Events, entry => entry.Type == "reveal"
            && entry.Cards.Any(card => card.CardId == "S02-0301"));
        Assert.Equal(L12Phase.Mulligan, game.State.Phase);
    }

    [Fact]
    public void MargaretEntryMillIsOptionalInsteadOfSilentlyDiscardingTheTopCard()
    {
        var declineGame = Create(6344);
        var declinePlayer = declineGame.State.Players[0];
        var declineMargaret = Card("S02-0304", "margaret-decline");
        declinePlayer.Hand.Clear();
        declinePlayer.Hand.Add(declineMargaret);
        AddMorale(declinePlayer, declineMargaret.Cost);
        declineGame.State.Phase = L12Phase.Main;
        var declineLibrary = declinePlayer.Library.Count;

        Assert.True(declineGame.Handle(0,
            new L12Command("playCard", declineMargaret.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(declineGame);
        var decline = Assert.Single(declineGame.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-margaret-entry-mill");
        Assert.Equal(declineLibrary, declinePlayer.Library.Count);
        Assert.True(declineGame.Handle(0,
            new L12Command("resolvePrompt", PromptId: decline.PromptId, Choice: "no")).Accepted);
        Assert.Equal(declineLibrary, declinePlayer.Library.Count);

        var acceptGame = Create(6345);
        var acceptPlayer = acceptGame.State.Players[0];
        var acceptMargaret = Card("S02-0304", "margaret-accept");
        acceptPlayer.Hand.Clear();
        acceptPlayer.Hand.Add(acceptMargaret);
        AddMorale(acceptPlayer, acceptMargaret.Cost);
        acceptGame.State.Phase = L12Phase.Main;
        var acceptLibrary = acceptPlayer.Library.Count;

        Assert.True(acceptGame.Handle(0,
            new L12Command("playCard", acceptMargaret.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(acceptGame);
        var accept = Assert.Single(acceptGame.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-margaret-entry-mill");
        Assert.True(acceptGame.Handle(0,
            new L12Command("resolvePrompt", PromptId: accept.PromptId, Choice: "yes")).Accepted);
        Assert.Equal(acceptLibrary - 1, acceptPlayer.Library.Count);
        Assert.Contains(acceptPlayer.Graveyard, card => card.Name != acceptMargaret.Name);
    }

    [Fact]
    public void MargaretReactsOnlyToEffectDamageAndThenPreventsFurtherLegionHealingThisTurn()
    {
        var game = Create(6346);
        var player = game.State.Players[0];
        var margaret = Card("S02-0304", "margaret-field");
        var oddr = Card("S01-0313", "margaret-damage-source");
        var harald = Card("S02-0302", "margaret-heal-source");
        player.Field[0][0] = margaret;
        player.Hand.Clear();
        player.Hand.AddRange([oddr, harald]);
        AddMorale(player, 8);
        player.Hp = player.MaxHp - 2;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", oddr.InstanceId, Row: 0, Slot: 1)).Accepted);
        PassResponses(game);
        var oddrDamage = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "oddr-draw");
        Assert.True(game.Handle(0,
            new L12Command("resolvePrompt", PromptId: oddrDamage.PromptId, Choice: "yes")).Accepted);
        PassResponses(game);

        var reaction = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-margaret-master-damage");
        Assert.True(game.Handle(0,
            new L12Command("resolvePrompt", PromptId: reaction.PromptId, Choice: "yes")).Accepted);
        Assert.True(margaret.Tapped);
        Assert.Equal(player.MaxHp - 2, player.Hp);
        Assert.Equal(game.State.TurnSerial, player.LegionEffectHealForbiddenUntilTurn);

        player.Hp = player.MaxHp - 3;
        foreach (var morale in player.Morale) morale.Tapped = false;
        Assert.True(game.Handle(0, new L12Command("playCard", harald.InstanceId, Row: 0, Slot: 2)).Accepted);
        PassResponses(game);

        Assert.Equal(player.MaxHp - 3, player.Hp);
        Assert.Contains(game.State.Events, entry => entry.Type == "heal-prevented"
            && entry.Text.Contains("无法因军团效果", StringComparison.Ordinal));
    }

    [Fact]
    public void MargaretAndAnderstorpRingShareOneOwnerOrderedDamageTriggerBatch()
    {
        var game = Create(6347);
        var player = game.State.Players[0];
        var margaret = Card("S02-0304", "margaret-batch");
        var ring = Card("S02-0305", "ring-batch");
        var oddr = Card("S01-0313", "batch-damage-source");
        player.Field[0][0] = margaret;
        player.Relic = ring;
        player.Hand.Clear();
        player.Hand.Add(oddr);
        AddMorale(player, oddr.Cost);
        player.Hp = player.MaxHp - 2;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", oddr.InstanceId, Row: 0, Slot: 1)).Accepted);
        PassResponses(game);
        var damage = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "oddr-draw");
        Assert.True(game.Handle(0,
            new L12Command("resolvePrompt", PromptId: damage.PromptId, Choice: "yes")).Accepted);

        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-order", order.Kind);
        Assert.Equal(2, order.ValidChoices.Count);
        Assert.Contains(order.ValidChoices,
            id => order.Data[$"sourceInstance:{id}"] == margaret.InstanceId);
        Assert.Contains(order.ValidChoices,
            id => order.Data[$"sourceInstance:{id}"] == ring.InstanceId);
    }

    [Fact]
    public void PingyangMakesOnlyTheNextMasterDamageToTheOpponentBecomeTwoThisTurn()
    {
        var game = CreateWithFirstMaster("S01-01M1", 6352);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var pingyang = Card("S02-0103", "pingyang-entry");
        player.Hand.Clear();
        player.Hand.Add(pingyang);
        AddMorale(player, 8);
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0,
            new L12Command("playCard", pingyang.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        Assert.Equal(game.State.TurnSerial, player.NextMasterDamageToOpponentBecomesTwoUntilTurn);

        var mercenary = Card("S01-0002", "pingyang-non-master-damage");
        player.Field[0][1] = mercenary;
        opponent.Hp = Math.Max(3, opponent.Hp);
        var beforeLegionDamage = opponent.Hp;
        // 同一玩家的军团进攻不会错误消耗“主宰造成的下一次伤害”。
        Assert.True(game.Handle(0,
            new L12Command("attack", mercenary.InstanceId, Target: new L12AttackTarget("master"))).Accepted);
        PassResponses(game);
        if (game.State.PendingDefense is not null)
            Assert.True(game.Handle(1, new L12Command("resolveDefense")).Accepted);
        Assert.Equal(beforeLegionDamage - 1, opponent.Hp);
        Assert.Equal(game.State.TurnSerial, player.NextMasterDamageToOpponentBecomesTwoUntilTurn);

        foreach (var morale in player.Morale) morale.Tapped = false;
        var beforeMasterDamage = opponent.Hp;
        Assert.True(game.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "nonLethal")).Accepted);
        PassResponses(game);
        while (game.State.PendingPrompts.Count > 0)
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "no")).Accepted);
            PassResponses(game);
        }

        Assert.Equal(beforeMasterDamage - 2, opponent.Hp);
        Assert.Equal(-1, player.NextMasterDamageToOpponentBecomesTwoUntilTurn);
    }

    [Fact]
    public void PromotedAtalantaUsesThreeThousandTroopsAndRangeOnlyInTheBackRow()
    {
        var game = Create(6353);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var atalanta = Card("S02-0507", "atalanta-back-row");
        var target = Card("S01-0002", "atalanta-front-target");
        target.Troops = 3500;
        player.Field[1][0] = atalanta;
        opponent.Field[0][0] = target;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0,
            new L12Command("attack", atalanta.InstanceId,
                Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        PassResponses(game);

        Assert.Equal(500, target.Troops);
        Assert.Equal(4000, atalanta.Troops);
        Assert.Equal("弓手", atalanta.EffectiveProfession);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("兵力视为3000", StringComparison.Ordinal));

        var frontGame = Create(6354);
        var frontPlayer = frontGame.State.Players[0];
        var frontOpponent = frontGame.State.Players[1];
        var frontAtalanta = Card("S02-0507", "atalanta-front-row");
        var backTarget = Card("S01-0002", "atalanta-back-target");
        frontPlayer.Field[0][0] = frontAtalanta;
        frontOpponent.Field[1][0] = backTarget;
        frontGame.State.ActivePlayer = 0;
        frontGame.State.Phase = L12Phase.Main;

        var invalid = frontGame.Handle(0,
            new L12Command("attack", frontAtalanta.InstanceId,
                Target: new L12AttackTarget("legion", backTarget.InstanceId)));
        Assert.False(invalid.Accepted);
        Assert.Contains("近战军团无法进攻对方后排", invalid.Error);
        Assert.Equal(frontAtalanta.Profession, L12StructuredCardRules.EffectiveProfession(frontAtalanta, 0));
    }

    [Fact]
    public void YoshitsuneUsesSharedBackRowRangeAndAttackTroopsWithoutPersistingTheSetValue()
    {
        var game = Create(6355);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var yoshitsune = Card("S01-0409", "yoshitsune-back-row");
        var target = Card("S01-0002", "yoshitsune-front-target");
        target.Troops = 2500;
        player.Field[1][0] = yoshitsune;
        opponent.Field[0][0] = target;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", yoshitsune.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        PassResponses(game);

        Assert.Equal(500, target.Troops);
        Assert.Equal(yoshitsune.BaseTroops, yoshitsune.Troops);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("兵力视为2000", StringComparison.Ordinal));
    }
}
