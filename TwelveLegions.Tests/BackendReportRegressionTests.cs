using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BackendReportRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, string? firstMaster = null, string disasterMode = "none")
    {
        var baseDeck = Catalog.DeckAt(0);
        var firstDeck = firstMaster is null ? baseDeck : new L12PresetDeckDefinition
        {
            Name = $"{firstMaster} 后台报告回归牌库",
            MasterId = firstMaster,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "backend-report-regressions", "REPORTS", seed,
            ["甲", "乙"], [firstDeck, baseDeck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            disasterMode: disasterMode);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Resolving.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int? disasterLevel = null)
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = disasterLevel ?? definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
        };
    }

    private static L12Prompt Prompt(L12GameEngine game) => Assert.Single(game.State.PendingPrompts);

    private static L12Prompt Choose(L12GameEngine game, params string[] choices)
    {
        var prompt = Prompt(game);
        var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: choices.Length == 1 ? choices[0] : null,
            CardInstanceIds: choices.Length == 1 ? null : [.. choices]));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var guard = 0; guard < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; guard++)
            Choose(game, "pass");
    }

    private static L12Prompt ResponseFor(L12GameEngine game, int playerIndex)
    {
        for (var guard = 0; guard < 12; guard++)
        {
            var prompt = Prompt(game);
            Assert.Equal("response", prompt.Kind);
            if (prompt.PlayerIndex == playerIndex) return prompt;
            Choose(game, "pass");
        }
        throw new Xunit.Sdk.XunitException("响应优先权未到达指定玩家");
    }

    [Fact]
    [Trait("L12Evidence", "BUG-292c4feb")]
    public void ThorChargeAppliesToAnAsgardLegionSummonedFromTheGraveyard()
    {
        var game = Create(292401, "S02-03M1");
        var player = game.State.Players[0];
        var call = Card("S01-0318", "thor-valkyrie-call");
        var gustav = Card("S01-0311", "thor-revived-gustav");
        player.Hand.Add(call);
        player.Graveyard.Add(gustav);
        player.TemporaryMorale = 20;
        player.UsedAbilities.Add($"s2-thor-charge:{game.State.TurnSerial}");

        var play = game.Handle(0, new L12Command("playCard", call.InstanceId));
        Assert.True(play.Accepted, play.Error);
        Choose(game, gustav.InstanceId);
        Choose(game, "0:0");
        PassResponses(game);

        Assert.Same(gustav, player.Field[0][0]);
        Assert.True(gustav.HasCharge);
        Assert.Single(game.State.Events,
            entry => entry.Text.Contains("雷神索尔赋予的冲锋", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "BUG-6d0f505d")]
    [Trait("L12Evidence", "BUG-11adbd08")]
    public void AbsoluteDefenseNegatesBlackbeardsWholeEnterEffectIncludingTheDrawClause()
    {
        var game = Create(605501);
        var actor = game.State.Players[0];
        var defender = game.State.Players[1];
        var blackbeard = Card("S01-0001", "negated-blackbeard");
        var defense = Card("S01-0016", "blackbeard-absolute-defense");
        var defenseCost = Card("S01-0002", "blackbeard-defense-cost");
        var ownDrawOne = Card("S01-0003", "blackbeard-own-draw-1");
        var ownDrawTwo = Card("S01-0004", "blackbeard-own-draw-2");
        var enemyDraw = Card("S01-0005", "blackbeard-enemy-draw");
        defense.Hidden = true;
        defense.SetRound = 0;
        actor.Hand.Add(blackbeard);
        actor.Library.AddRange([ownDrawOne, ownDrawTwo]);
        actor.TemporaryMorale = 20;
        defender.Field[1][0] = defense;
        defender.Hand.Add(defenseCost);
        defender.Library.Add(enemyDraw);

        var play = game.Handle(0, new L12Command("playCard", blackbeard.InstanceId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        var response = ResponseFor(game, 1);
        Assert.Contains(defense.InstanceId, response.ValidChoices);
        Choose(game, defense.InstanceId);
        var discard = Prompt(game);
        Assert.Equal("stack-response-discard", discard.Continuation);
        Choose(game, defenseCost.InstanceId);
        PassResponses(game);

        Assert.Empty(actor.Hand);
        Assert.Equal([ownDrawOne, ownDrawTwo], actor.Library);
        Assert.Empty(defender.Hand);
        Assert.Equal([enemyDraw], defender.Library);
        Assert.DoesNotContain(game.State.EffectStack,
            item => item.Data.GetValueOrDefault("atomicFlow") == "teach-enter-draw");
    }

    [Fact]
    [Trait("L12Evidence", "BUG-6d0f505d-normal-control")]
    public void BlackbeardStillDiscardsAndDrawsNormallyWhenItsEnterEffectResolves()
    {
        var game = Create(605502);
        var actor = game.State.Players[0];
        var opponent = game.State.Players[1];
        var blackbeard = Card("S01-0001", "normal-blackbeard");
        var actorDiscardOne = Card("S01-0002", "normal-own-discard-1");
        var actorDiscardTwo = Card("S01-0003", "normal-own-discard-2");
        var enemyDiscardOne = Card("S01-0004", "normal-enemy-discard-1");
        var enemyDiscardTwo = Card("S01-0005", "normal-enemy-discard-2");
        var ownDrawOne = Card("S01-0006", "normal-own-draw-1");
        var ownDrawTwo = Card("S01-0007", "normal-own-draw-2");
        var enemyDraw = Card("S01-0008", "normal-enemy-draw");
        actor.Hand.AddRange([blackbeard, actorDiscardOne, actorDiscardTwo]);
        opponent.Hand.AddRange([enemyDiscardOne, enemyDiscardTwo]);
        actor.Library.AddRange([ownDrawOne, ownDrawTwo]);
        opponent.Library.Add(enemyDraw);
        actor.TemporaryMorale = 20;

        var play = game.Handle(0, new L12Command("playCard", blackbeard.InstanceId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        PassResponses(game);
        var discards = game.State.PendingPrompts
            .Where(prompt => prompt.Data.GetValueOrDefault("action") == "teach-discard").ToArray();
        Assert.Equal(2, discards.Length);
        foreach (var prompt in discards)
        {
            var ids = prompt.PlayerIndex == 0
                ? new[] { actorDiscardOne.InstanceId, actorDiscardTwo.InstanceId }
                : new[] { enemyDiscardOne.InstanceId, enemyDiscardTwo.InstanceId };
            var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: [.. ids]));
            Assert.True(result.Accepted, result.Error);
        }
        PassResponses(game);

        Assert.Equal([ownDrawOne, ownDrawTwo], actor.Hand);
        Assert.Equal([enemyDraw], opponent.Hand);
        Assert.Contains(actorDiscardOne, actor.Graveyard);
        Assert.Contains(enemyDiscardOne, opponent.Graveyard);
    }

    [Fact]
    [Trait("L12Evidence", "BUG-7e27c502")]
    public void QianYangCannotSelectKusanagiWhoseLegionFormHasOriginalTroopsFiveThousand()
    {
        var game = Create(727501);
        var actor = game.State.Players[0];
        var opponent = game.State.Players[1];
        var qianYang = Card("S02-0105", "qian-yang");
        var kusanagi = Card("S01-0417", "kusanagi-legion-form");
        var ordinaryLowTarget = Card("S01-0003", "qian-yang-low-target");
        kusanagi.SetTroopsValue = 5000;
        kusanagi.Troops = 5000;
        actor.Hand.Add(qianYang);
        actor.TemporaryMorale = 20;
        opponent.Field[0][0] = kusanagi;
        opponent.Field[0][1] = ordinaryLowTarget;

        var play = game.Handle(0, new L12Command("playCard", qianYang.InstanceId));
        Assert.True(play.Accepted, play.Error);
        Choose(game, "mode:none");
        var target = Prompt(game);

        Assert.Contains(ordinaryLowTarget.InstanceId, target.ValidChoices);
        Assert.DoesNotContain(kusanagi.InstanceId, target.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "BUG-b130b777")]
    public void RolloTreatsUniversalGraveCardsAsAsgardWhileTheRingIsActive()
    {
        var game = Create(130777, "S02-03M1");
        var player = game.State.Players[0];
        var ring = Card("S02-0008", "rollo-ring");
        var rollo = Card("S02-0302", "rollo");
        var universal = Card("S01-0002", "rollo-universal-cost");
        var printedAsgard = Card("S01-0301", "rollo-printed-asgard-cost");
        player.Relic = ring;
        player.Hand.Add(rollo);
        player.Graveyard.AddRange([universal, printedAsgard]);
        player.TemporaryMorale = 20;

        var play = game.Handle(0, new L12Command("playCard", rollo.InstanceId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        var cost = Prompt(game);

        Assert.Equal("s2-rollo-grave-cost", cost.Continuation);
        Assert.Contains(universal.InstanceId, cost.ValidChoices);
        Assert.Contains(printedAsgard.InstanceId, cost.ValidChoices);
        Choose(game, universal.InstanceId);
        Assert.Equal("response", Prompt(game).Kind);
        Assert.DoesNotContain(universal, player.Graveyard);
        Assert.Same(universal, player.Library[^1]);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "ability-rejected");
    }

    [Fact]
    [Trait("L12Evidence", "BUG-927ce3ae")]
    public void DisasterWaitsForCanutesWholeEnterEffectDeclarationAndResolution()
    {
        var game = Create(927531, disasterMode: "all");
        var player = game.State.Players[0];
        var oldDisaster = Card("S01-DS09", "canute-old-disaster");
        var nextDisaster = Card("S01-DS02", "canute-next-disaster");
        var alvida = Card("S01-0307", "canute-alvida-source");
        var canute = Card("S02-0303", "canute-effect-entry");
        game.State.ActiveDisaster = oldDisaster;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(nextDisaster);
        game.State.DisasterValue = 7;
        player.Field[0][0] = alvida;
        for (var row = 0; row < 2; row++)
            for (var slot = 0; slot < 3; slot++)
                if (row != 0 || slot != 0)
                    player.Field[row][slot] = Card("S01-0002", $"canute-blocker-{row}-{slot}");
        player.Hand.Add(canute);

        var begin = game.Handle(0, new L12Command("activateAbility", alvida.InstanceId, Ability: "alvidaSummon"));
        Assert.True(begin.Accepted, begin.Error);
        Choose(game, canute.InstanceId);
        Choose(game, "0:0");
        PassResponses(game);

        var canuteMode = Prompt(game);
        Assert.Equal("pending-activation", canuteMode.Continuation);
        Assert.Equal(9, game.State.DisasterValue);
        Assert.Same(oldDisaster, game.State.ActiveDisaster);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "disaster-removed");

        Choose(game, "mode:use");
        var canuteTargets = Prompt(game);
        Assert.Equal("pending-activation", canuteTargets.Continuation);
        Assert.Equal("cards", canuteTargets.Kind);
        Assert.Contains(alvida.InstanceId, canuteTargets.ValidChoices);
        Assert.Same(oldDisaster, game.State.ActiveDisaster);
        Choose(game, alvida.InstanceId);
        PassResponses(game);

        Assert.Same(nextDisaster, game.State.ActiveDisaster);
        Assert.Contains(game.State.Events, entry => entry.Type == "disaster-removed"
            && entry.Cards.Any(card => card.InstanceId == oldDisaster.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "BUG-927ce3ae-decline")]
    public void DisasterResumesAfterCanutesEnterEffectIsDeclined()
    {
        var game = Create(927532, disasterMode: "all");
        var player = game.State.Players[0];
        var oldDisaster = Card("S01-DS09", "canute-decline-old-disaster");
        var nextDisaster = Card("S01-DS02", "canute-decline-next-disaster");
        var alvida = Card("S01-0307", "canute-decline-alvida");
        var canute = Card("S02-0303", "canute-decline-entry");
        game.State.ActiveDisaster = oldDisaster;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(nextDisaster);
        game.State.DisasterValue = 7;
        player.Field[0][0] = alvida;
        player.Hand.Add(canute);

        var begin = game.Handle(0, new L12Command("activateAbility", alvida.InstanceId, Ability: "alvidaSummon"));
        Assert.True(begin.Accepted, begin.Error);
        Choose(game, canute.InstanceId);
        Choose(game, "0:0");
        PassResponses(game);

        var canuteMode = Prompt(game);
        Assert.Equal("pending-activation", canuteMode.Continuation);
        Assert.Same(oldDisaster, game.State.ActiveDisaster);
        Choose(game, "mode:none");

        Assert.Empty(game.State.PendingActivations);
        Assert.Same(nextDisaster, game.State.ActiveDisaster);
        Assert.Contains(game.State.Events, entry => entry.Type == "disaster-removed"
            && entry.Cards.Any(card => card.InstanceId == oldDisaster.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "BUG-82e6141a")]
    public void AlvidaCanChooseHerOwnCostVacatedSlotOnAnOtherwiseFullBattlefield()
    {
        var game = Create(826141);
        var player = game.State.Players[0];
        var alvida = Card("S01-0307", "alvida-full-board");
        var entry = Card("S01-0303", "alvida-entry");
        player.Field[0][0] = alvida;
        for (var row = 0; row < 2; row++)
            for (var slot = 0; slot < 3; slot++)
                if (row != 0 || slot != 0)
                    player.Field[row][slot] = Card("S01-0002", $"alvida-blocker-{row}-{slot}");
        player.Hand.Add(entry);
        var hpBefore = player.Hp;

        var begin = game.Handle(0, new L12Command("activateAbility", alvida.InstanceId, Ability: "alvidaSummon"));
        Assert.True(begin.Accepted, begin.Error);
        Choose(game, entry.InstanceId);
        var slotPrompt = Prompt(game);
        Assert.Contains("0:0", slotPrompt.ValidChoices);
        Choose(game, "0:0");
        PassResponses(game);

        Assert.Contains(alvida, player.Graveyard);
        Assert.Same(entry, player.Field[0][0]);
        Assert.Equal(hpBefore - 1, player.Hp);
    }

    [Fact]
    [Trait("L12Evidence", "BUG-82e6141a-cancellation")]
    public void CancellingAlvidasProspectiveSlotReleasesTheWholeUncommittedDeclaration()
    {
        var game = Create(826142);
        var player = game.State.Players[0];
        var alvida = Card("S01-0307", "alvida-cancel-source");
        var entry = Card("S01-0303", "alvida-cancel-entry");
        player.Field[0][0] = alvida;
        for (var row = 0; row < 2; row++)
            for (var slot = 0; slot < 3; slot++)
                if (row != 0 || slot != 0)
                    player.Field[row][slot] = Card("S01-0002", $"alvida-cancel-blocker-{row}-{slot}");
        player.Hand.Add(entry);
        var hpBefore = player.Hp;

        var begin = game.Handle(0, new L12Command("activateAbility", alvida.InstanceId, Ability: "alvidaSummon"));
        Assert.True(begin.Accepted, begin.Error);
        Choose(game, entry.InstanceId);
        Assert.Contains("0:0", Prompt(game).ValidChoices);
        Choose(game, "skip");

        Assert.Same(alvida, player.Field[0][0]);
        Assert.Contains(entry, player.Hand);
        Assert.Equal(hpBefore, player.Hp);
        Assert.Empty(game.State.PendingActivations);
        Assert.Contains(game.State.Events, entryEvent => entryEvent.Type == "ability-cancelled");
    }

    [Fact]
    [Trait("L12Evidence", "BUG-82e6141a-corrupt-land")]
    public void CorruptLandExcludesBackRowFromProspectiveEntrySlots()
    {
        var game = Create(826144);
        var player = game.State.Players[0];
        var alvida = Card("S01-0307", "alvida-corrupt-land-source");
        var entry = Card("S01-0303", "alvida-corrupt-land-entry");
        game.State.ActiveDisaster = Card("S01-DS03", "corrupt-land");
        player.Field[0][0] = alvida;
        player.Field[0][1] = Card("S01-0002", "corrupt-land-front-blocker-1");
        player.Field[0][2] = Card("S01-0002", "corrupt-land-front-blocker-2");
        player.Hand.Add(entry);

        var begin = game.Handle(0, new L12Command("activateAbility", alvida.InstanceId, Ability: "alvidaSummon"));
        Assert.True(begin.Accepted, begin.Error);
        Choose(game, entry.InstanceId);
        var slotPrompt = Prompt(game);

        Assert.Contains("0:0", slotPrompt.ValidChoices);
        Assert.DoesNotContain(slotPrompt.ValidChoices,
            choice => choice.StartsWith("1:", StringComparison.Ordinal));
        Choose(game, "skip");
    }

    [Fact]
    [Trait("L12Evidence", "BUG-82e6141a-battlefield-boundary")]
    public void ProspectiveSourceSlotDoesNotLeakIntoTheOpponentsBattlefield()
    {
        var game = Create(826145);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var alvida = Card("S01-0307", "alvida-cross-field-source");
        var infiltrator = Card("S01-0004", "alvida-cross-field-entry", disasterLevel: 2);
        // Exercise the shared battlefield-routing rule through Alvida's real declaration protocol.
        // Only the eligibility value is varied; S01-0004 remains the card that may enter either battlefield.
        player.Field[0][0] = alvida;
        for (var row = 0; row < 2; row++)
            for (var slot = 0; slot < 3; slot++)
            {
                if (row != 0 || slot != 0)
                    player.Field[row][slot] = Card("S01-0002", $"cross-field-own-blocker-{row}-{slot}");
                if (row != 0 || slot != 2)
                    opponent.Field[row][slot] = Card("S01-0002", $"cross-field-enemy-blocker-{row}-{slot}");
            }
        player.Hand.Add(infiltrator);

        var begin = game.Handle(0, new L12Command("activateAbility", alvida.InstanceId, Ability: "alvidaSummon"));
        Assert.True(begin.Accepted, begin.Error);
        Choose(game, infiltrator.InstanceId);
        var battlefieldPrompt = Prompt(game);
        Assert.Contains("battlefield:0", battlefieldPrompt.ValidChoices);
        Assert.Contains("battlefield:1", battlefieldPrompt.ValidChoices);
        Choose(game, "battlefield:1");
        var slotPrompt = Prompt(game);

        Assert.Equal(["0:2"], slotPrompt.ValidChoices.Where(choice => choice != "skip"));
        Assert.DoesNotContain("0:0", slotPrompt.ValidChoices);
        Choose(game, "skip");
    }

    [Fact]
    [Trait("L12Evidence", "BUG-82e6141a-post-payment-change")]
    public void AlvidaDoesNotOverwriteAProspectiveSlotThatChangesAfterPayment()
    {
        var game = Create(826146);
        var player = game.State.Players[0];
        var alvida = Card("S01-0307", "alvida-post-payment-source");
        var entry = Card("S01-0303", "alvida-post-payment-entry");
        player.Field[0][0] = alvida;
        for (var row = 0; row < 2; row++)
            for (var slot = 0; slot < 3; slot++)
                if (row != 0 || slot != 0)
                    player.Field[row][slot] = Card("S01-0002", $"post-payment-blocker-{row}-{slot}");
        player.Hand.Add(entry);
        var hpBefore = player.Hp;

        var begin = game.Handle(0, new L12Command("activateAbility", alvida.InstanceId, Ability: "alvidaSummon"));
        Assert.True(begin.Accepted, begin.Error);
        Choose(game, entry.InstanceId);
        Choose(game, "0:0");
        Assert.Contains(alvida, player.Graveyard);
        Assert.Null(player.Field[0][0]);

        var lateBlocker = Card("S01-0002", "alvida-late-blocker");
        player.Field[0][0] = lateBlocker;
        PassResponses(game);

        Assert.Same(lateBlocker, player.Field[0][0]);
        Assert.Contains(entry, player.Hand);
        Assert.Contains(alvida, player.Graveyard);
        Assert.Equal(hpBefore - 1, player.Hp);
        Assert.Contains(game.State.Events, entryEvent => entryEvent.Type == "effect-cancelled"
            && entryEvent.Text.Contains("不覆盖", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "BUG-82e6141a-horus")]
    public void HorusCanChooseADeclaredCostSlotOnAnOtherwiseFullBattlefield()
    {
        var game = Create(826143, "ST02-M1");
        var player = game.State.Players[0];
        var revive = Card("ST02-07", "horus-full-board-revive");
        var secondCost = Card("S01-0002", "horus-full-board-second-cost");
        player.Morale.Add(new L12MoraleCard
        {
            CardId = "S01-01C1",
            InstanceId = "horus-full-board-morale",
        });
        player.Morale.Add(new L12MoraleCard
        {
            CardId = "S01-01C1",
            InstanceId = "horus-full-board-morale-untouched",
        });
        player.Field[0][0] = revive;
        player.Field[0][1] = secondCost;
        for (var row = 0; row < 2; row++)
            for (var slot = 0; slot < 3; slot++)
                if (player.Field[row][slot] is null)
                    player.Field[row][slot] = Card("S01-0002", $"horus-full-board-blocker-{row}-{slot}");

        var begin = game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "horusRevive"));
        Assert.True(begin.Accepted, begin.Error);
        Choose(game, "mode:morale-legions");
        Choose(game, "horus-full-board-morale");
        Choose(game, revive.InstanceId, secondCost.InstanceId);
        Choose(game, revive.InstanceId);
        var slotPrompt = Prompt(game);
        Assert.Contains("0:0", slotPrompt.ValidChoices);
        Assert.Contains("0:1", slotPrompt.ValidChoices);
        Choose(game, "0:0");
        PassResponses(game);

        Assert.Same(revive, player.Field[0][0]);
        Assert.True(revive.Tapped);
        Assert.Contains(secondCost, player.Graveyard);
    }

    [Fact]
    [Trait("L12Evidence", "BUG-17333d1f")]
    public void VanishedXiaotianStillAddsTappedMoraleFromItsDeathSnapshot()
    {
        var game = Create(173331);
        var player = game.State.Players[0];
        var xiaotian = Card("S02-01S1", "report-xiaotian");
        var mover = Card("S01-0002", "report-xiaotian-state-check");
        xiaotian.Troops = 0;
        xiaotian.SummonRound = 0;
        mover.SummonRound = 0;
        player.Field[0][0] = xiaotian;
        player.Field[0][2] = mover;
        player.TemporaryMorale = 1;
        player.MoraleDeck.Add(new L12MoraleCard
        {
            CardId = "S01-01C1",
            InstanceId = "report-xiaotian-morale",
        });
        var moraleBefore = player.Morale.Count;

        var moved = game.Handle(0, new L12Command("move", mover.InstanceId, Row: 1, Slot: 2));
        Assert.True(moved.Accepted, moved.Error);
        var trigger = Prompt(game);
        Assert.Equal("pending-activation", trigger.Continuation);
        Choose(game, "mode:use");
        PassResponses(game);

        Assert.True(player.Morale.Count == moraleBefore + 1,
            string.Join(" | ", game.State.Events.Select(entry => $"{entry.Type}:{entry.Text}"))
            + $"; prompts={string.Join(',', game.State.PendingPrompts.Select(prompt => $"{prompt.Kind}:{prompt.Continuation}:{prompt.Text}"))}"
            + $"; stack={string.Join(',', game.State.EffectStack.Select(item => $"{item.Trigger}:{item.Data.GetValueOrDefault("atomicFlow")}"))}");
        Assert.True(player.Morale[^1].Tapped);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "ability-rejected");
    }
}
