using TwelveLegions.Server;
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
        var runePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-mistletoe-rune-cost", runePrompt.Continuation);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: runePrompt.PromptId, Choice: "runes:2")).Accepted);
        PassResponses(game);

        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-mistletoe-debuff", targetPrompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId, Choice: target.InstanceId)).Accepted);

        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Single(player.Morale, card => card.Tapped);
        Assert.Null(game.State.Players[1].Field[0][0]);
        Assert.Contains(target, game.State.Players[1].Graveyard);
        Assert.Equal(target.BaseTroops, target.Troops);
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
        var forgePayment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-payment", forgePayment.Kind);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: forgePayment.PromptId,
            CardInstanceIds: [player.Morale[0].InstanceId])).Accepted);
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
        Assert.False(player.Morale.Single(card => card.InstanceId == "prometheus-power").IsGodPower);
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
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "yes")).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts,
            candidate => candidate.Data.GetValueOrDefault("action") == "s2-xiaotian-slot");
        Assert.Equal("s2-xiaotian-slot", slot.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId, Choice: "0:1")).Accepted);

        var xiaotian = Assert.IsType<L12CardInstance>(player.Field[0][1]);
        Assert.Equal("S02-01S1", xiaotian.CardId);
        Assert.False(xiaotian.Tapped);
        Assert.Equal(2000, xiaotian.Troops);
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
    public void MagatamaMoveUsesDynamicAdjacentSlotsAndRecordsMovementTurn()
    {
        var game = Create(6334);
        var player = game.State.Players[0];
        var magatama = Card("S02-0404", "magatama-move-source");
        var legion = Card("S02-0401", "magatama-moving-legion");
        player.Relic = magatama;
        player.Field[0][1] = legion;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("activateAbility", magatama.InstanceId,
            Ability: "magatamaMove")).Accepted);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", target.Continuation);
        Assert.Equal([legion.InstanceId], target.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: target.PromptId,
            Choice: legion.InstanceId)).Accepted);

        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("slot", slot.Kind);
        Assert.Equal(["0:0", "0:2", "1:1"], slot.ValidChoices.OrderBy(value => value));
        Assert.DoesNotContain("1:0", slot.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: "1:1")).Accepted);
        Assert.True(magatama.Tapped);
        PassResponses(game);

        Assert.Null(player.Field[0][1]);
        Assert.Same(legion, player.Field[1][1]);
        Assert.Equal(game.State.TurnSerial, legion.LastMovedTurn);
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
}
