using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class HelenLethalEffectCancellation20261027Tests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private sealed record PendingHelenReplacement(
        L12GameEngine Game,
        int EgilController,
        int HelenController,
        string HelenId,
        string SubstituteId,
        L12Prompt Prompt);

    private sealed record DirectTimedReplacementScenario(
        L12GameEngine Game,
        string Kind,
        int TargetController,
        string ProtectedId,
        string ReplacementId,
        string Choice,
        int TroopsBeforeEffect,
        L12Prompt? Prompt);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("Bug", "BUG-20261026-209394c1:red")]
    [Trait("L12Evidence", "card:S01-0316")]
    [Trait("L12Evidence", "card:S02-0515")]
    [Trait("L12Evidence", "reconnect:v2-effect-lethal-cancellation")]
    public void HelenSubstitutionCancelsTheLethalEgilModifierAndRestoresPreEffectTroops(int egilController)
    {
        const string egilId = "helen-cancellation-egil";
        const string helenId = "helen-cancellation-target";
        const string substituteId = "helen-cancellation-substitute";
        var helenController = 1 - egilController;
        var game = Create(egilController);
        var egilPlayer = game.State.Players[egilController];
        var helenPlayer = game.State.Players[helenController];
        var egil = Card("S01-0316", egilId, egilController);
        var helen = Card("S02-0515", helenId, helenController);
        var substitute = Card("S01-0002", substituteId, helenController);
        egilPlayer.Hand.Add(egil);
        helenPlayer.Field[0][0] = helen;
        helenPlayer.Hand.Add(substitute);
        egilPlayer.Library.Add(Card("S01-0003", "helen-cancellation-mill-a", egilController));
        egilPlayer.Library.Add(Card("S01-0003", "helen-cancellation-mill-b", egilController));
        for (var index = 0; index < egil.Cost; index++)
            egilPlayer.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-03C1",
                InstanceId = $"helen-cancellation-morale-{egilController}-{index}",
            });
        game.State.Revision++;
        game = Restore(game);

        Assert.Equal(2000, FindFieldCard(game, helenController, helenId).CurrentTroops);
        var payment = game.State.Players[egilController].Morale
            .Where(card => !card.Tapped)
            .Take(egil.Cost)
            .Select(card => card.InstanceId)
            .ToList();
        game = Send(game, egilController, new L12Command("playCard", egilId,
            CardInstanceIds: payment, Row: 0, Slot: 0));

        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", mode.Continuation);
        Assert.Contains("mode:use", mode.ValidChoices);
        game = Resolve(game, mode, choice: "mode:use");

        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", target.Continuation);
        Assert.Contains(helenId, target.ValidChoices);
        game = Resolve(game, target, choice: helenId);
        game = PassResponseWindows(game);

        var replacement = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(helenController, replacement.PlayerIndex);
        Assert.Equal("effect-lethal-replacement", replacement.Continuation);
        Assert.Equal("helen-hand", replacement.Data["replacementKind"]);
        Assert.Contains(substituteId, replacement.ValidChoices);
        Assert.Equal(0, FindFieldCard(game, helenController, helenId).CurrentTroops);

        game = Resolve(game, replacement, choice: substituteId);

        var survived = FindFieldCard(game, helenController, helenId);
        Assert.Contains(game.State.Players[helenController].Graveyard,
            card => card.InstanceId == substituteId);
        Assert.Contains(game.State.Events, entry => entry.Type == "replacement"
            && entry.Cards.Any(card => card.InstanceId == helenId));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "leave"
            && entry.Cards.Any(card => card.InstanceId == helenId));
        Assert.Equal(2000, survived.CurrentTroops);
    }

    [Fact]
    [Trait("Bug", "BUG-20261026-209394c1:unrelated-state")]
    public void CancellationRemovesOnlyEgilContributionAndKeepsPriorLayersDamageCostAndRest()
    {
        var pending = BeginHelenReplacement(egilController: 0, "unrelated-state", helen =>
        {
            helen.Tapped = true;
            helen.TimedModifiers.Add(new L12TimedModifier
            {
                TroopsDelta = 2000,
                ConsumedTroopsBonus = 1000,
                CostDelta = 0,
                ExpiresAfterTurn = 7,
                Source = "prior-positive-layer",
            });
            helen.TimedModifiers.Add(new L12TimedModifier
            {
                TroopsDelta = -1500,
                ConsumedTroopsBonus = 0,
                CostDelta = 0,
                ExpiresAfterTurn = 7,
                Source = "prior-negative-layer",
            });
            helen.TimedModifiers.Add(new L12TimedModifier
            {
                TroopsDelta = 0,
                ConsumedTroopsBonus = 0,
                CostDelta = -1,
                ExpiresAfterTurn = 7,
                Source = "prior-cost-layer",
            });
            helen.Troops = 1500;
            helen.CostModifier = -1;
        });

        var game = Resolve(pending.Game, pending.Prompt, choice: pending.SubstituteId);
        var survived = FindFieldCard(game, pending.HelenController, pending.HelenId);

        Assert.Equal(1500, survived.CurrentTroops);
        Assert.True(survived.Tapped);
        Assert.Equal(-1, survived.CostModifier);
        Assert.Equal(3, survived.TimedModifiers.Count);
        Assert.Contains(survived.TimedModifiers, modifier => modifier.Source == "prior-positive-layer"
            && modifier.TroopsDelta == 2000 && modifier.ConsumedTroopsBonus == 1000);
        Assert.Contains(survived.TimedModifiers, modifier => modifier.Source == "prior-negative-layer"
            && modifier.TroopsDelta == -1500);
        Assert.Contains(survived.TimedModifiers, modifier => modifier.Source == "prior-cost-layer"
            && modifier.CostDelta == -1 && modifier.TroopsDelta == 0);
        Assert.DoesNotContain(survived.TimedModifiers,
            modifier => modifier.Source == "夺命诗人埃吉尔");
    }

    [Theory]
    [InlineData("fingerprint-mismatch")]
    [InlineData("missing-index")]
    [Trait("Bug", "BUG-20261026-209394c1:corrupt-recipe")]
    public void CorruptCancellationRecipeFailsBeforePromptOrStateIsConsumed(string corruption)
    {
        var pending = BeginHelenReplacement(egilController: 0, "corrupt-recipe");
        var game = pending.Game;
        var prompt = pending.Prompt;
        if (corruption == "fingerprint-mismatch")
            prompt.Data["lethalTroopsCancellation:fingerprint"] = new string('0', 64);
        else
            Assert.True(prompt.Data.Remove("lethalTroopsCancellation:modifierIndex"));
        game.State.Revision++;
        game = Restore(game);
        prompt = Assert.Single(game.State.PendingPrompts);
        var stateBefore = game.SerializeFullState();
        var hashBefore = game.ComputeStateHash();
        var randomBefore = game.RandomState;
        var handBefore = game.State.Players[pending.HelenController].Hand
            .Select(card => card.InstanceId).ToArray();

        Assert.Throws<InvalidDataException>(() => game.Handle(prompt.PlayerIndex,
            PromptCommand(prompt, pending.SubstituteId)));

        Assert.Equal(stateBefore, game.SerializeFullState());
        Assert.Equal(hashBefore, game.ComputeStateHash());
        Assert.Equal(randomBefore, game.RandomState);
        Assert.Equal(handBefore, game.State.Players[pending.HelenController].Hand
            .Select(card => card.InstanceId).ToArray());
        Assert.Same(prompt, Assert.Single(game.State.PendingPrompts));
        Assert.Equal(0, FindFieldCard(game, pending.HelenController, pending.HelenId).CurrentTroops);
    }

    [Fact]
    [Trait("Bug", "BUG-20261026-209394c1:legacy-prompt")]
    public void LegacyPromptWithoutRecipeKeepsExistingConsumedLethalStateProtection()
    {
        var pending = BeginHelenReplacement(egilController: 0, "legacy-prompt", legacyProtocol: true);
        foreach (var key in pending.Prompt.Data.Keys
                     .Where(key => key.StartsWith("lethalTroopsCancellation:", StringComparison.Ordinal))
                     .ToArray())
            pending.Prompt.Data.Remove(key);
        pending.Game.State.Revision++;
        var game = Restore(pending.Game);
        var prompt = Assert.Single(game.State.PendingPrompts);

        var resolved = game.Handle(prompt.PlayerIndex, PromptCommand(prompt, pending.SubstituteId));
        Assert.True(resolved.Accepted, resolved.Error);

        var protectedCard = FindFieldCard(game, pending.HelenController, pending.HelenId);
        Assert.Equal(0, protectedCard.CurrentTroops);
        Assert.Contains(game.State.Players[pending.HelenController].UsedAbilities,
            key => key.StartsWith($"lethal-event-protected:{pending.HelenId}:",
                StringComparison.Ordinal));
        Assert.Contains(game.State.Players[pending.HelenController].Graveyard,
            card => card.InstanceId == pending.SubstituteId);
    }

    [Fact]
    [Trait("Bug", "BUG-20261026-209394c1:decline")]
    public void DecliningReplacementKeepsEgilModifierAndAppliesOriginalLethalResult()
    {
        var pending = BeginHelenReplacement(egilController: 0, "decline");
        var game = Resolve(pending.Game, pending.Prompt, choice: "decline");

        Assert.DoesNotContain(game.State.Players[pending.HelenController].Field.SelectMany(row => row),
            card => card?.InstanceId == pending.HelenId);
        Assert.Contains(game.State.Players[pending.HelenController].Graveyard,
            card => card.InstanceId == pending.HelenId);
        Assert.Contains(game.State.Players[pending.HelenController].Hand,
            card => card.InstanceId == pending.SubstituteId);
    }

    [Fact]
    [Trait("Bug", "BUG-20261026-209394c1:new-lethal-event")]
    public void LaterIndependentEgilModifierIsNotCoveredByConsumedReplacement()
    {
        var pending = BeginHelenReplacement(egilController: 0, "new-lethal-event");
        var game = Resolve(pending.Game, pending.Prompt, choice: pending.SubstituteId);
        Assert.Equal(2000, FindFieldCard(game, pending.HelenController, pending.HelenId).CurrentTroops);

        const string secondEgilId = "helen-cancellation-second-egil";
        var egilPlayer = game.State.Players[pending.EgilController];
        var secondEgil = Card("S01-0316", secondEgilId, pending.EgilController);
        egilPlayer.Hand.Add(secondEgil);
        egilPlayer.Library.Add(Card("S01-0003", "second-egil-mill-a", pending.EgilController));
        egilPlayer.Library.Add(Card("S01-0003", "second-egil-mill-b", pending.EgilController));
        for (var index = 0; index < secondEgil.Cost; index++)
            egilPlayer.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-03C1",
                InstanceId = $"second-egil-morale-{index}",
            });
        game.State.Revision++;
        game = Restore(game);
        var payment = game.State.Players[pending.EgilController].Morale
            .Where(card => !card.Tapped)
            .Take(secondEgil.Cost)
            .Select(card => card.InstanceId)
            .ToList();
        game = Send(game, pending.EgilController, new L12Command("playCard", secondEgilId,
            CardInstanceIds: payment, Row: 0, Slot: 1));
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), choice: "mode:use");
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), choice: pending.HelenId);
        game = PassResponseWindows(game);

        Assert.DoesNotContain(game.State.Players[pending.HelenController].Field.SelectMany(row => row),
            card => card?.InstanceId == pending.HelenId);
        Assert.Contains(game.State.Players[pending.HelenController].Graveyard,
            card => card.InstanceId == pending.HelenId);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation == "effect-lethal-replacement");
    }

    [Fact]
    [Trait("Bug", "BUG-20261008-CONSUMED-LETHAL-STATE:new-negative-layer")]
    public void ProtectedLegacyRawZeroCanCancelANewNegativeLayerWithoutGuessingTheOldLayer()
    {
        var first = BeginHelenReplacement(egilController: 0, "legacy-raw-zero", legacyProtocol: true);
        foreach (var key in first.Prompt.Data.Keys
                     .Where(key => key.StartsWith("lethalTroopsCancellation:", StringComparison.Ordinal))
                     .ToArray())
            first.Prompt.Data.Remove(key);
        var helen = FindFieldCard(first.Game, first.HelenController, first.HelenId);
        var oldLayer = Assert.Single(helen.TimedModifiers,
            modifier => modifier.Source == "夺命诗人埃吉尔");
        helen.TimedModifiers[0] = new L12TimedModifier
        {
            TroopsDelta = oldLayer.TroopsDelta,
            ConsumedTroopsBonus = oldLayer.ConsumedTroopsBonus,
            CostDelta = oldLayer.CostDelta,
            ExpiresAfterTurn = first.Game.State.TurnSerial + 1,
            OwnerTurnEndExpiry = oldLayer.OwnerTurnEndExpiry,
            Source = oldLayer.Source,
        };
        first.Game.State.Revision++;
        var game = Restore(first.Game);
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), first.SubstituteId);
        helen = FindFieldCard(game, first.HelenController, first.HelenId);
        Assert.Equal(0, helen.Troops);
        var originalProtection = Assert.Single(game.State.Players[first.HelenController].UsedAbilities,
            key => key.StartsWith($"lethal-event-protected:{first.HelenId}:",
                StringComparison.Ordinal));

        const string secondEgilId = "legacy-raw-zero-second-egil";
        const string secondSubstituteId = "legacy-raw-zero-second-substitute";
        game.State.TurnSerial++;
        var egilPlayer = game.State.Players[first.EgilController];
        var helenPlayer = game.State.Players[first.HelenController];
        var secondEgil = Card("S01-0316", secondEgilId, first.EgilController);
        egilPlayer.Hand.Add(secondEgil);
        helenPlayer.Hand.Add(Card("S01-0002", secondSubstituteId, first.HelenController));
        egilPlayer.Library.Add(Card("S01-0003", "legacy-raw-zero-mill-a", first.EgilController));
        egilPlayer.Library.Add(Card("S01-0003", "legacy-raw-zero-mill-b", first.EgilController));
        for (var index = 0; index < secondEgil.Cost; index++)
            egilPlayer.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-03C1",
                InstanceId = $"legacy-raw-zero-morale-{index}",
            });
        game.State.Revision++;
        game = Restore(game);
        var payment = game.State.Players[first.EgilController].Morale
            .Where(card => !card.Tapped)
            .Take(secondEgil.Cost)
            .Select(card => card.InstanceId)
            .ToList();
        game = Send(game, first.EgilController, new L12Command("playCard", secondEgilId,
            CardInstanceIds: payment, Row: 0, Slot: 1));
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), choice: "mode:use");
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), choice: first.HelenId);
        game = PassResponseWindows(game);
        var secondReplacement = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "effect-lethal-replacement");
        game = Resolve(game, secondReplacement, choice: secondSubstituteId);

        var survived = FindFieldCard(game, first.HelenController, first.HelenId);
        Assert.Equal(0, survived.Troops);
        Assert.Single(survived.TimedModifiers,
            modifier => modifier.Source == "夺命诗人埃吉尔" && modifier.TroopsDelta == -2000);
        Assert.Equal(originalProtection,
            Assert.Single(game.State.Players[first.HelenController].UsedAbilities,
                key => key.StartsWith($"lethal-event-protected:{first.HelenId}:",
                    StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Bug", "BUG-20261026-209394c1:shared-timed-modifier-red")]
    [Trait("L12Evidence", "card:S02-0603")]
    [Trait("L12Evidence", "card:ST04-05")]
    public void KondoReplacementCancelsTheRealMerlinTimedModifierAcrossV2Restore()
    {
        var setup = KondoPersistentLethalReplacementFixture.BeginMerlinLethal();
        var game = KondoPersistentLethalReplacementFixture.Restore(setup.Game);

        KondoPersistentLethalReplacementFixture.Resolve(game, setup.KondoId);

        var survived = KondoPersistentLethalReplacementFixture.FieldCard(game, 1, setup.OkitaId);
        Assert.Contains(game.State.Players[1].Graveyard,
            card => card.InstanceId == setup.KondoId);
        Assert.Equal(survived.BaseTroops, survived.CurrentTroops);
    }

    [Theory]
    [InlineData("helen")]
    [InlineData("horemheb")]
    [InlineData("kondo")]
    [InlineData("achilles")]
    [Trait("Bug", "BUG-20261026-209394c1:shared-replacement-family")]
    public void RealMerlinTimedModifierUsesEveryPromptReplacementTransaction(string kind)
    {
        var pending = BeginDirectMerlinReplacement(kind, $"family-{kind}");
        var game = Resolve(pending.Game, Assert.IsType<L12Prompt>(pending.Prompt), pending.Choice);
        var survived = FindFieldCard(game, pending.TargetController, pending.ProtectedId);

        Assert.Equal(pending.TroopsBeforeEffect, survived.CurrentTroops);
        Assert.DoesNotContain(survived.TimedModifiers, modifier => modifier.Source == "梅林");
        Assert.Contains(game.State.Events, entry => entry.Type == "replacement"
            && entry.Cards.Any(card => card.InstanceId == pending.ProtectedId));
        if (kind == "achilles")
        {
            var paid = Assert.Single(game.State.Players[pending.TargetController].Morale,
                morale => morale.InstanceId == pending.ReplacementId);
            Assert.True(paid.Tapped);
            Assert.False(paid.IsGodPower);
        }
        else
        {
            Assert.Contains(game.State.Players[pending.TargetController].Graveyard,
                card => card.InstanceId == pending.ReplacementId);
            Assert.DoesNotContain(game.State.Players[pending.TargetController].Hand,
                card => card.InstanceId == pending.ReplacementId);
            Assert.DoesNotContain(game.State.Players[pending.TargetController].Field.SelectMany(row => row),
                card => card?.InstanceId == pending.ReplacementId);
        }
    }

    [Fact]
    [Trait("Bug", "BUG-20261026-209394c1:lake-lady-auto")]
    public void RealMerlinTimedModifierUsesLakeLadyAutomaticSwordPayment()
    {
        var resolved = BeginDirectMerlinReplacement("lake-lady", "lake-lady-auto");
        var survived = FindFieldCard(resolved.Game, resolved.TargetController, resolved.ProtectedId);

        Assert.Null(resolved.Prompt);
        Assert.Equal(resolved.TroopsBeforeEffect, survived.CurrentTroops);
        Assert.DoesNotContain(survived.TimedModifiers, modifier => modifier.Source == "梅林");
        Assert.Empty(survived.AttachedCards);
        Assert.Contains(resolved.Game.State.Players[resolved.TargetController].Graveyard,
            card => card.InstanceId == resolved.ReplacementId);
        Assert.Contains(resolved.Game.State.Events, entry => entry.Type == "replacement"
            && entry.Text.Contains("王者之剑", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Bug", "BUG-20261026-209394c1:multi-target-causality")]
    public void HannibalSimultaneousLethalTargetsEachKeepTheirOwnCancellationRecipe()
    {
        const string hannibalId = "shared-hannibal-source";
        const string ownHelenId = "shared-hannibal-own-helen";
        const string enemyHelenId = "shared-hannibal-enemy-helen";
        const string ownSubstituteId = "shared-hannibal-own-substitute";
        const string enemySubstituteId = "shared-hannibal-enemy-substitute";
        const string powerId = "shared-hannibal-power";
        var game = Create(0);
        var actor = game.State.Players[0];
        var opponent = game.State.Players[1];
        actor.Field[0][0] = Card("S02-0516", hannibalId, 0);
        actor.Field[0][2] = Card("S02-0515", ownHelenId, 0);
        opponent.Field[0][0] = Card("S02-0515", enemyHelenId, 1);
        actor.Hand.Add(Card("S01-0002", ownSubstituteId, 0));
        opponent.Hand.Add(Card("S01-0002", enemySubstituteId, 1));
        actor.Morale.Add(new L12MoraleCard
        {
            InstanceId = powerId,
            CardId = "S02-05C1",
            IsGodPower = true,
            Tapped = false,
        });
        game.State.Revision++;
        game = Restore(game);

        game = Send(game, 0, new L12Command("attack", hannibalId,
            Target: new L12AttackTarget("master")));
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), choice: "mode:use");
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), choice: powerId);
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), choice: ownHelenId);
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), choice: enemyHelenId);
        game = PassResponseWindows(game);

        var prompts = game.State.PendingPrompts
            .Where(prompt => prompt.Continuation == "effect-lethal-replacement")
            .OrderBy(prompt => prompt.Data["cardInstanceId"], StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, prompts.Length);
        Assert.Equal(new[] { enemyHelenId, ownHelenId },
            prompts.Select(prompt => prompt.Data["cardInstanceId"]).ToArray());
        Assert.All(prompts, prompt =>
        {
            Assert.Equal("2", prompt.Data["lethalTroopsCancellation:version"]);
            Assert.Equal("0", prompt.Data["lethalTroopsCancellation:modifierIndex"]);
            Assert.Equal(64, prompt.Data["lethalTroopsCancellation:fingerprint"].Length);
        });
        Assert.NotEqual(prompts[0].Data["lethalTroopsCancellation:fingerprint"],
            prompts[1].Data["lethalTroopsCancellation:fingerprint"]);

        var ownPrompt = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("cardInstanceId") == ownHelenId);
        game = Resolve(game, ownPrompt, choice: ownSubstituteId);
        var enemyPrompt = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("cardInstanceId") == enemyHelenId);
        game = Resolve(game, enemyPrompt, choice: enemySubstituteId);

        Assert.Equal(2000, FindFieldCard(game, 0, ownHelenId).CurrentTroops);
        Assert.Equal(2000, FindFieldCard(game, 1, enemyHelenId).CurrentTroops);
        Assert.Contains(game.State.Players[0].Graveyard, card => card.InstanceId == ownSubstituteId);
        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == enemySubstituteId);
    }

    [Fact]
    [Trait("Bug", "BUG-20261026-209394c1:identical-negative-layers")]
    public void ExactDuplicateOldNegativeLayerIsKeptWhileTheCrossingLayerIndexIsCancelled()
    {
        var pending = BeginDirectMerlinReplacement("helen", "identical-layer",
            (game, target) =>
            {
                target.TimedModifiers.Add(new L12TimedModifier
                {
                    TroopsDelta = 4000,
                    ConsumedTroopsBonus = 0,
                    CostDelta = 0,
                    ExpiresAfterTurn = game.State.TurnSerial,
                    Source = "pre-existing-buffer",
                });
                target.TimedModifiers.Add(new L12TimedModifier
                {
                    TroopsDelta = -3000,
                    ConsumedTroopsBonus = 0,
                    CostDelta = 0,
                    ExpiresAfterTurn = game.State.TurnSerial,
                    Source = "梅林",
                });
                target.Troops = 3000;
            });
        var prompt = Assert.IsType<L12Prompt>(pending.Prompt);
        Assert.Equal("2", prompt.Data["lethalTroopsCancellation:modifierIndex"]);

        var game = Resolve(pending.Game, prompt, pending.Choice);
        var survived = FindFieldCard(game, pending.TargetController, pending.ProtectedId);

        Assert.Equal(3000, survived.CurrentTroops);
        Assert.Equal(2, survived.TimedModifiers.Count);
        Assert.Single(survived.TimedModifiers, modifier => modifier.Source == "梅林"
            && modifier.TroopsDelta == -3000);
        Assert.Single(survived.TimedModifiers, modifier => modifier.Source == "pre-existing-buffer"
            && modifier.TroopsDelta == 4000);
    }

    [Fact]
    [Trait("Bug", "BUG-20261026-209394c1:cancelled-expiry")]
    public void CancelledMerlinLayerDoesNotReturnAgainWhenItsExpiryIsProcessed()
    {
        var pending = BeginDirectMerlinReplacement("achilles", "expiry");
        var game = Resolve(pending.Game, Assert.IsType<L12Prompt>(pending.Prompt), pending.Choice);
        var survived = FindFieldCard(game, pending.TargetController, pending.ProtectedId);
        Assert.Equal(3000, survived.CurrentTroops);

        L12DerivedStats.ResetForCompletedTurn(survived, game.State.TurnSerial);

        Assert.Equal(survived.BaseTroops, survived.CurrentTroops);
        Assert.DoesNotContain(survived.TimedModifiers, modifier => modifier.Source == "梅林");
    }

    [Fact]
    [Trait("Bug", "BUG-20261026-209394c1:shared-corrupt-v2")]
    public void CorruptRealMerlinCancellationRecipeIsRejectedAtomicallyAfterV2Restore()
    {
        var pending = BeginDirectMerlinReplacement("helen", "corrupt-v2");
        var game = pending.Game;
        var prompt = Assert.IsType<L12Prompt>(pending.Prompt);
        prompt.Data["lethalTroopsCancellation:fingerprint"] = new string('F', 64);
        game.State.Revision++;
        game = Restore(game);
        prompt = Assert.Single(game.State.PendingPrompts);
        var stateBefore = game.SerializeFullState();
        var hashBefore = game.ComputeStateHash();
        var randomBefore = game.RandomState;
        var handBefore = game.State.Players[pending.TargetController].Hand
            .Select(card => card.InstanceId).ToArray();

        Assert.Throws<InvalidDataException>(() => game.Handle(prompt.PlayerIndex,
            PromptCommand(prompt, pending.Choice)));

        Assert.Equal(stateBefore, game.SerializeFullState());
        Assert.Equal(hashBefore, game.ComputeStateHash());
        Assert.Equal(randomBefore, game.RandomState);
        Assert.Equal(handBefore, game.State.Players[pending.TargetController].Hand
            .Select(card => card.InstanceId).ToArray());
        Assert.Same(prompt, Assert.Single(game.State.PendingPrompts));
    }

    [Fact]
    [Trait("Bug", "BUG-20261008-CONSUMED-LETHAL-STATE:unprotected-raw-zero")]
    public void UnprotectedRawZeroDoesNotInventARecipeForALaterNegativeLayer()
    {
        const int sourceController = 0;
        const int targetController = 1;
        const string merlinId = "shared-merlin-unprotected-raw-zero";
        const string protectedId = "shared-protected-unprotected-raw-zero";
        const string replacementId = "shared-replacement-unprotected-raw-zero";
        var game = Create(sourceController);
        game.State.Players[sourceController].Field[0][0] =
            Card("S02-0603", merlinId, sourceController);
        game.State.Players[sourceController].SpecialZones.Runes = 1;
        game.State.Players[targetController].Field[0][0] =
            Card("S02-0515", protectedId, targetController);
        game.State.Players[targetController].Hand.Add(
            Card("S01-0002", replacementId, targetController));
        game.State.Revision++;
        game = Restore(game);

        game = Send(game, sourceController,
            new L12Command("activateAbility", merlinId, Ability: "merlinRune"));
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), choice: "mode:debuff");
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        var target = FindFieldCard(game, targetController, protectedId);
        target.Troops = 0;
        Assert.DoesNotContain(game.State.Players[targetController].UsedAbilities,
            key => key.StartsWith($"lethal-event-protected:{protectedId}:",
                StringComparison.Ordinal));

        // Inject the raw-zero boundary after target selection opens, so the state-based
        // check cannot consume it before the shared timed-modifier writer runs.
        var targetResult = game.Handle(targetPrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
                Choice: protectedId, ActivationId: targetPrompt.ActivationId,
                SourceInstanceId: targetPrompt.SourceInstanceId,
                SourceCardId: targetPrompt.SourceCardId, Step: targetPrompt.Step,
                CreatedRevision: targetPrompt.CreatedRevision,
                Controller: targetPrompt.Controller));
        Assert.True(targetResult.Accepted, targetResult.Error);
        game = Restore(game);
        game = PassResponseWindows(game);
        var prompt = Assert.Single(game.State.PendingPrompts,
            candidate => candidate.Continuation == "effect-lethal-replacement"
                && candidate.Data.GetValueOrDefault("cardInstanceId") == protectedId);

        Assert.DoesNotContain(prompt.Data.Keys,
            key => key.StartsWith("lethalTroopsCancellation:", StringComparison.Ordinal));
        Assert.Equal(-3000,
            FindFieldCard(game, targetController, protectedId).Troops);
    }

    private static DirectTimedReplacementScenario BeginDirectMerlinReplacement(
        string kind, string suffix, Action<L12GameEngine, L12CardInstance>? configureTarget = null,
        bool expectRecipe = true)
    {
        const int sourceController = 0;
        const int targetController = 1;
        var game = Create(sourceController);
        var sourcePlayer = game.State.Players[sourceController];
        var targetPlayer = game.State.Players[targetController];
        var merlinId = $"shared-merlin-{suffix}";
        var protectedId = $"shared-protected-{suffix}";
        var replacementId = $"shared-replacement-{suffix}";
        var protectedCardId = kind switch
        {
            "horemheb" => "S01-0205",
            "kondo" => "S02-0403",
            "achilles" => "S02-0504",
            "lake-lady" => "S02-0601",
            _ => "S02-0515",
        };
        var protectedCard = Card(protectedCardId, protectedId, targetController);
        if (kind == "lake-lady") protectedCard.Troops = 2000;
        else if (protectedCard.Troops > 3000) protectedCard.Troops = 3000;
        configureTarget?.Invoke(game, protectedCard);
        var troopsBeforeEffect = protectedCard.Troops;
        sourcePlayer.Field[0][0] = Card("S02-0603", merlinId, sourceController);
        sourcePlayer.SpecialZones.Runes = 1;
        targetPlayer.Field[0][0] = protectedCard;
        targetPlayer.SpecialZones.Trials.Clear();
        string choice;
        switch (kind)
        {
            case "horemheb":
                targetPlayer.Field[1][0] = Card("S01-0212", replacementId, targetController);
                choice = replacementId;
                break;
            case "kondo":
                targetPlayer.Field[0][1] = Card("ST04-05", replacementId, targetController);
                choice = replacementId;
                break;
            case "achilles":
                targetPlayer.Morale.Add(new L12MoraleCard
                {
                    InstanceId = replacementId,
                    CardId = "S02-05C1",
                    IsGodPower = true,
                    Tapped = false,
                });
                choice = "yes";
                break;
            case "lake-lady":
            {
                protectedCard.AttachedCards.Add(Card("S02-06S2", replacementId, targetController));
                var trial = Card("S02-06S3", $"shared-lake-trial-{suffix}", targetController);
                trial.TrialCompleted = true;
                targetPlayer.SpecialZones.Trials.Add(trial);
                choice = string.Empty;
                break;
            }
            default:
                targetPlayer.Hand.Add(Card("S01-0002", replacementId, targetController));
                choice = replacementId;
                break;
        }
        game.State.Revision++;
        game = Restore(game);
        game = Send(game, sourceController,
            new L12Command("activateAbility", merlinId, Ability: "merlinRune"));
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), choice: "mode:debuff");
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), choice: protectedId);
        game = PassResponseWindows(game);
        if (kind == "lake-lady")
            return new(game, kind, targetController, protectedId, replacementId, choice,
                troopsBeforeEffect, null);
        var prompt = Assert.Single(game.State.PendingPrompts,
            candidate => candidate.Continuation == "effect-lethal-replacement"
                && candidate.Data.GetValueOrDefault("cardInstanceId") == protectedId);
        if (expectRecipe)
            Assert.Equal("2", prompt.Data["lethalTroopsCancellation:version"]);
        else
            Assert.DoesNotContain(prompt.Data.Keys,
                key => key.StartsWith("lethalTroopsCancellation:", StringComparison.Ordinal));
        Assert.Equal(0, FindFieldCard(game, targetController, protectedId).CurrentTroops);
        return new(game, kind, targetController, protectedId, replacementId, choice,
            troopsBeforeEffect, prompt);
    }

    private static PendingHelenReplacement BeginHelenReplacement(int egilController, string suffix,
        Action<L12CardInstance>? configureHelen = null, bool legacyProtocol = false)
    {
        var helenController = 1 - egilController;
        var egilId = $"helen-cancellation-egil-{suffix}";
        var helenId = $"helen-cancellation-target-{suffix}";
        var substituteId = $"helen-cancellation-substitute-{suffix}";
        var game = Create(egilController, legacyProtocol);
        var egilPlayer = game.State.Players[egilController];
        var helenPlayer = game.State.Players[helenController];
        var egil = Card("S01-0316", egilId, egilController);
        var helen = Card("S02-0515", helenId, helenController);
        configureHelen?.Invoke(helen);
        egilPlayer.Hand.Add(egil);
        helenPlayer.Field[0][0] = helen;
        helenPlayer.Hand.Add(Card("S01-0002", substituteId, helenController));
        egilPlayer.Library.Add(Card("S01-0003", $"helen-cancellation-mill-a-{suffix}", egilController));
        egilPlayer.Library.Add(Card("S01-0003", $"helen-cancellation-mill-b-{suffix}", egilController));
        for (var index = 0; index < egil.Cost; index++)
            egilPlayer.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-03C1",
                InstanceId = $"helen-cancellation-morale-{suffix}-{index}",
            });
        game.State.Revision++;
        game = Restore(game);
        var payment = game.State.Players[egilController].Morale
            .Where(card => !card.Tapped)
            .Take(egil.Cost)
            .Select(card => card.InstanceId)
            .ToList();
        game = Send(game, egilController, new L12Command("playCard", egilId,
            CardInstanceIds: payment, Row: 0, Slot: 0));
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), choice: "mode:use");
        game = Resolve(game, Assert.Single(game.State.PendingPrompts), choice: helenId);
        game = PassResponseWindows(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("effect-lethal-replacement", prompt.Continuation);
        Assert.Equal(legacyProtocol ? "1" : "2", prompt.Data["lethalTroopsCancellation:version"]);
        Assert.Equal(0, FindFieldCard(game, helenController, helenId).CurrentTroops);
        return new(game, egilController, helenController, helenId, substituteId, prompt);
    }

    private static L12Command PromptCommand(L12Prompt prompt, string choice)
        => new("resolvePrompt", PromptId: prompt.PromptId, Choice: choice,
            ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
            SourceCardId: prompt.SourceCardId, Step: prompt.Step,
            CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller);

    private static L12GameEngine Create(int egilController, bool legacyProtocol = false)
    {
        var deck = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "helen-lethal-effect-cancellation-20261027",
            "HELEN-CANCEL", 202610280 + egilController, ["Synthetic A", "Synthetic B"], [deck, deck],
            skipPreparation: true, disasterMode: "none", autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2,
            utcNow: () => DateTimeOffset.Parse("2026-10-27T10:00:00+08:00"));
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
        game.State.ActivePlayer = egilController;
        game.State.FirstPlayer = egilController;
        game.State.Round = 3;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
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
        game.State.PendingDefense = null;
        game.State.ActiveDisaster = null;
        game.State.Revision++;
        if (!legacyProtocol) return game;
        // Only the explicitly named historical recipe scenarios freeze v1.
        var checkpoint = JsonNode.Parse(game.SerializeFullState())!.AsObject();
        checkpoint.Remove(nameof(L12GameState.TimedTroopsReductionAbsorptionProtocolEnabled));
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint.ToJsonString(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            utcNow: () => DateTimeOffset.Parse("2026-10-27T10:00:00+08:00"));
        return game;
    }

    private static L12GameEngine PassResponseWindows(L12GameEngine game)
    {
        for (var guard = 0;; guard++)
        {
            var response = game.State.PendingPrompts.FirstOrDefault(prompt => prompt.Kind == "response");
            if (response is null) break;
            Assert.True(guard < 8, "埃吉尔结算超过限定响应步数");
            game = Resolve(game, response, choice: "pass");
        }
        return game;
    }

    private static L12CardInstance FindFieldCard(L12GameEngine game, int controller, string instanceId)
        => Assert.Single(game.State.Players[controller].Field.SelectMany(row => row),
            card => card?.InstanceId == instanceId)!;

    private static L12GameEngine Send(L12GameEngine game, int actor, L12Command command)
    {
        game = Restore(game);
        var result = game.Handle(actor, command);
        Assert.True(result.Accepted, $"{command.Type}: {result.Error}");
        return Restore(game);
    }

    private static L12GameEngine Resolve(L12GameEngine game, L12Prompt prompt,
        string? choice = null, List<string>? cards = null)
        => Send(game, prompt.PlayerIndex, new L12Command("resolvePrompt",
            PromptId: prompt.PromptId, Choice: choice, CardInstanceIds: cards,
            ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
            SourceCardId: prompt.SourceCardId, Step: prompt.Step,
            CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller));

    private static L12GameEngine Restore(L12GameEngine game)
    {
        var state = game.SerializeFullState();
        var hash = game.ComputeStateHash();
        var random = game.RandomState!.Value;
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, state, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        Assert.Equal(state, restored.SerializeFullState());
        Assert.Equal(hash, restored.ComputeStateHash());
        Assert.Equal(random, restored.RandomState);
        return restored;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            OwnerIndex = owner,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            IsCounterTactic = definition.IsCounterTactic,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }
}
