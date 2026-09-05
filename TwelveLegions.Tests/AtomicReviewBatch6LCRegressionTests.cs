using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6LCRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static readonly IReadOnlyDictionary<string, int> AuditedAbilityCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["S02-0401"] = 2, ["S02-0402"] = 2, ["S02-0403"] = 3, ["S02-0404"] = 5,
            ["S02-0405"] = 3, ["S02-0406"] = 4, ["S02-04M1"] = 3,
            ["S02-0501"] = 4, ["S02-0502"] = 1, ["S02-0503"] = 6, ["S02-0504"] = 2,
            ["S02-0505"] = 5, ["S02-0506"] = 1, ["S02-0507"] = 5, ["S02-0508"] = 2,
            ["S02-0509"] = 3, ["S02-0510"] = 3, ["S02-0511"] = 3, ["S02-0512"] = 4,
            ["S02-0513"] = 3, ["S02-0514"] = 2, ["S02-0515"] = 3, ["S02-0516"] = 3,
            ["S02-0517"] = 3, ["S02-0518"] = 3, ["S02-0519"] = 2, ["S02-0520"] = 4,
            ["S02-0521"] = 2, ["S02-0522"] = 2, ["S02-0523"] = 2, ["S02-05M1"] = 4,
            ["S02-05M2"] = 1, ["S02-05C1"] = 3, ["S02-05C1A"] = 3, ["S02-05D1"] = 3,
        };

    private static L12GameEngine Create(int seed = 8601, string firstMaster = "S02-05M1")
    {
        var baseDeck = Catalog.DeckAt(0);
        var firstDeck = new L12PresetDeckDefinition
        {
            Name = $"{firstMaster}第六批6L-C审查牌库",
            MasterId = firstMaster,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "atomic-review-batch6lc", "ATOMIC6LC", seed,
            ["甲", "乙"], [firstDeck, baseDeck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
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
            player.MoraleDeck.Clear();
            player.Resolving.Clear();
            player.ExtraRelics.Clear();
            player.Relic = null;
        }
        return game;
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
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 120 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
    }

    private static L12StackItem PassUntilFlow(L12GameEngine game, string flow)
    {
        for (var safety = 0; safety < 120; safety++)
        {
            var found = game.State.EffectStack.SingleOrDefault(item =>
                item.Data.GetValueOrDefault("atomicFlow") == flow);
            if (found is not null) return found;
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", prompt.Kind);
            Resolve(game, "pass");
        }
        throw new Xunit.Sdk.XunitException($"未进入预期复合段 {flow}");
    }

    private static void AddReadyMorale(L12PlayerState player, int count, string prefix = "batch6lc-morale")
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S02-05C1A", InstanceId = $"{prefix}-{index}",
            });
    }

    [Fact]
    [Trait("L12Evidence", "batch:6L-C-inventory")]
    public void S2TakamagaharaAndOlympusAuditFreezesEveryCardAndAbility()
    {
        Assert.Equal(35, AuditedAbilityCounts.Count);
        Assert.Equal(104, AuditedAbilityCounts.Values.Sum());
        Assert.All(AuditedAbilityCounts, pair =>
        {
            var card = Assert.Contains(pair.Key, Catalog.Cards);
            Assert.Contains(card.Faction, new[] { "gaotianyuan", "olympus" });
            Assert.False(string.IsNullOrWhiteSpace(card.Effect));
            Assert.Equal(pair.Value, Catalog.AtomicEffects.Find(pair.Key)?.Abilities.Count);
        });
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-04M1")]
    public void TsukuyomiBackToFrontBonusIsAnIndependentUnlimitedTriggerInsteadOfSynchronousMutation()
    {
        var game = Create(8602, "S02-04M1");
        var player = game.State.Players[0];
        var moved = Card("S02-0401", "batch6lc-tsukuyomi-moved");
        player.Field[0][0] = moved;

        Invoke(game, "RecordLegionMovement", 0, moved, 1, 0);

        Assert.Equal(0, moved.TsukuyomiFrontMoveBonusCount);
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("tsukuyomiFrontAttackBuff", first.Data["ability"]);
        first.Negated = true;
        PassResponses(game);
        Assert.Equal(0, moved.TsukuyomiFrontMoveBonusCount);

        for (var occurrence = 1; occurrence <= 2; occurrence++)
        {
            Invoke(game, "RecordLegionMovement", 0, moved, 1, 0);
            var effect = Assert.Single(game.State.EffectStack);
            Assert.Equal("tsukuyomiFrontAttackBuff", effect.Data["ability"]);
            PassResponses(game);
            Assert.Equal(occurrence, moved.TsukuyomiFrontMoveBonusCount);
        }
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0405")]
    public void FortuneNextUesugiSegmentSurvivesNegationOfItsHiddenSearchSegment()
    {
        var game = Create(8603, "S02-04M1");
        var player = game.State.Players[0];
        var tactic = Card("S02-0405", "batch6lc-fortune");
        player.Hand.Add(tactic);
        player.Library.AddRange([
            Card("S02-0404", "batch6lc-fortune-artifact"),
            Card("S01-0403", "batch6lc-fortune-uesugi"),
            Card("S02-0401", "batch6lc-fortune-a"),
            Card("S02-0402", "batch6lc-fortune-b"),
            Card("S02-0403", "batch6lc-fortune-c"),
        ]);
        AddReadyMorale(player, tactic.CurrentCost);

        var play = game.Handle(0, new L12Command("playCard", tactic.InstanceId));
        Assert.True(play.Accepted, play.Error);
        var search = Assert.Single(game.State.EffectStack);
        Assert.Equal("fortune-search", search.Data["atomicFlow"]);
        search.Negated = true;

        var followup = PassUntilFlow(game, "fortune-next-uesugi");
        Assert.DoesNotContain("s2-fortune-next-uesugi", player.UsedAbilities);
        PassResponses(game);

        Assert.DoesNotContain(player.Library, card => card.InstanceId == tactic.InstanceId);
        Assert.Contains("s2-fortune-next-uesugi", player.UsedAbilities);
        Assert.DoesNotContain(game.State.EffectStack, item => item.StackItemId == followup.StackItemId);
    }

    [Fact]
    [Trait("L12Evidence", "entry:olympus-promotion-shared-state")]
    public void PromotionInheritsTemporaryStateAndGrantedKeywordsFromItsFoundation()
    {
        var player = Create(8604).State.Players[0];
        var foundation = Card("S02-0502", "batch6lc-foundation");
        var promoted = Card("S02-0501", "batch6lc-promoted");
        foundation.Tapped = true;
        foundation.HasShock = true;
        foundation.HasStrongAttack = true;
        foundation.CannotReadyByEffectUntilTurn = 8;
        foundation.TauntUntilTurn = 7;
        foundation.TauntExpiresAtPlayerTurnStart = 6;
        foundation.LastMovedTurn = 3;
        foundation.LastCavalryMoveTurn = 3;
        foundation.TimedModifiers.Add(new L12TimedModifier
        {
            TroopsDelta = 2000, CostDelta = -1, ExpiresAfterTurn = 7,
            ConsumedTroopsBonus = 1000, Source = "6L-C共享状态",
        });
        foundation.Troops += 1000;
        player.Field[0][0] = foundation;
        player.Hand.Add(promoted);
        player.Morale.Add(new L12MoraleCard
        {
            CardId = "S02-05C1", InstanceId = "batch6lc-promotion-power", IsGodPower = true,
        });

        Assert.True(L12S2ZoneOps.Promote(player, foundation, promoted, 1));

        Assert.Same(promoted, player.Field[0][0]);
        Assert.True(promoted.Tapped);
        Assert.True(promoted.HasShock);
        Assert.True(promoted.HasStrongAttack);
        Assert.Equal(8, promoted.CannotReadyByEffectUntilTurn);
        Assert.Equal(7, promoted.TauntUntilTurn);
        Assert.Equal(6, promoted.TauntExpiresAtPlayerTurnStart);
        Assert.Equal(3, promoted.LastMovedTurn);
        Assert.Equal(3, promoted.LastCavalryMoveTurn);
        var inherited = Assert.Single(promoted.TimedModifiers);
        Assert.Equal(2000, inherited.TroopsDelta);
        Assert.Equal(1000, inherited.ConsumedTroopsBonus);
        Assert.Equal(promoted.BaseTroops + 1000, promoted.Troops);
    }

    [Fact]
    [Trait("L12Evidence", "cards:S02-05M1,S02-0008")]
    public void ArtemisPublicFactionTargetIncludesUniversalLegionWhileTheRingIsActive()
    {
        var game = Create(8605, "S02-05M1");
        var player = game.State.Players[0];
        var neutral = Card("S02-0003", "batch6lc-artemis-neutral");
        neutral.CostModifier = 3 - neutral.Cost;
        player.Field[0][0] = neutral;
        player.Relic = Card("S02-0008", "batch6lc-artemis-ring");
        player.Hand.Add(Card("S02-0001", "batch6lc-artemis-discard"));

        var begin = game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "artemisBuff"));
        Assert.True(begin.Accepted, begin.Error);
        Resolve(game, $"discard:{player.Hand[0].InstanceId}");
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(neutral.InstanceId, target.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "cards:S02-0510,S02-0008")]
    public void HippolytaReviveIncludesUniversalLegionInTheOwnersOlympusGraveyard()
    {
        var game = Create(8606);
        var player = game.State.Players[0];
        var source = Card("S02-0510", "batch6lc-hippolyta");
        var neutral = Card("S02-0003", "batch6lc-hippolyta-neutral");
        neutral.CostModifier = 4 - neutral.Cost;
        player.Field[0][0] = source;
        player.Relic = Card("S02-0008", "batch6lc-hippolyta-ring");
        player.Hand.Add(Card("S02-0001", "batch6lc-hippolyta-discard"));
        player.Graveyard.Add(neutral);
        AddReadyMorale(player, 3, "batch6lc-hippolyta-cost");

        var begin = game.Handle(0, new L12Command("activateAbility", source.InstanceId, Ability: "hippolytaRevive"));
        Assert.True(begin.Accepted, begin.Error);
        Resolve(game, player.Hand[0].InstanceId);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(neutral.InstanceId, target.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "cards:S02-0520,S02-0008")]
    public void ForgeTargetIncludesUniversalNonPromotionLegionWhileTheRingIsActive()
    {
        var game = Create(8607);
        var player = game.State.Players[0];
        var forge = Card("S02-0520", "batch6lc-forge");
        var neutral = Card("S02-0003", "batch6lc-forge-neutral");
        player.Relic = forge;
        player.ExtraRelics.Add(Card("S02-0008", "batch6lc-forge-ring"));
        player.Field[0][0] = neutral;
        AddReadyMorale(player, 1, "batch6lc-forge-cost");

        var begin = game.Handle(0, new L12Command("activateAbility", forge.InstanceId, Ability: "forgeReadyOnKill"));
        Assert.True(begin.Accepted, begin.Error);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(neutral.InstanceId, target.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "cards:S02-0513,S02-0008")]
    public void AristotleDiscountAppliesToAndIsConsumedByARingUniversalLegion()
    {
        var game = Create(8608);
        var player = game.State.Players[0];
        var aristotle = Card("S02-0513", "batch6lc-aristotle");
        var neutral = Card("S02-0003", "batch6lc-aristotle-neutral");
        player.Field[0][0] = aristotle;
        player.Relic = Card("S02-0008", "batch6lc-aristotle-ring");
        player.Hand.Add(neutral);
        AddReadyMorale(player, Math.Max(1, neutral.Cost), "batch6lc-aristotle-cost");

        var activation = game.Handle(0, new L12Command("activateAbility", aristotle.InstanceId,
            Ability: "aristotleDiscount"));
        Assert.True(activation.Accepted, activation.Error);
        PassResponses(game);
        Assert.Equal(1, player.NextS2OlympusLegionDiscount);
        Assert.Equal(Math.Max(0, neutral.Cost - 1),
            (int)Invoke(game, "GetPlayCost", 0, neutral, false, 0, 0)!);

        var play = game.Handle(0, new L12Command("playCard", neutral.InstanceId, Row: 0, Slot: 1));
        Assert.True(play.Accepted, play.Error);
        Assert.Equal(0, player.NextS2OlympusLegionDiscount);
    }

    [Fact]
    [Trait("L12Evidence", "cards:S02-0514,S02-0008")]
    public void PlatoHiddenTopSearchRecognizesRingUniversalCardOnlyAfterResolution()
    {
        var game = Create(8609);
        var player = game.State.Players[0];
        var plato = Card("S02-0514", "batch6lc-plato");
        var neutral = Card("S02-0003", "batch6lc-plato-neutral");
        player.Hand.Add(plato);
        player.Library.AddRange([
            neutral,
            Card("S02-0401", "batch6lc-plato-other-a"),
            Card("S02-0402", "batch6lc-plato-other-b"),
        ]);
        player.Relic = Card("S02-0008", "batch6lc-plato-ring");
        AddReadyMorale(player, plato.Cost, "batch6lc-plato-cost");

        var play = game.Handle(0, new L12Command("playCard", plato.InstanceId, Row: 0, Slot: 0));
        Assert.True(play.Accepted, play.Error);
        Assert.DoesNotContain(game.State.EffectStack, item => item.Targets.Contains(neutral.InstanceId));
        PassResponses(game);

        var search = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("faction-search-pick", search.Data["action"]);
        Assert.Contains(neutral.InstanceId, search.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "cards:S02-05M2,S02-0008")]
    public void PrometheusDelaysTopCardIdentityAndRecognizesRingUniversalCardAtResolution()
    {
        var game = Create(8610, "S02-05M2");
        var player = game.State.Players[0];
        var neutral = Card("S02-0003", "batch6lc-prometheus-neutral");
        player.Library.AddRange([
            neutral,
            Card("S02-0401", "batch6lc-prometheus-other-a"),
            Card("S02-0402", "batch6lc-prometheus-other-b"),
        ]);
        player.Relic = Card("S02-0008", "batch6lc-prometheus-ring");
        player.Morale.Add(new L12MoraleCard
        {
            CardId = "S02-05C1", InstanceId = "batch6lc-prometheus-power", IsGodPower = true,
        });

        var activation = game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "prometheusTopThree"));
        Assert.True(activation.Accepted, activation.Error);
        Assert.DoesNotContain(game.State.EffectStack, item => item.Targets.Contains(neutral.InstanceId));
        PassResponses(game);

        var pick = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-prometheus-pick", pick.Data["action"]);
        Assert.Contains(neutral.InstanceId, pick.ValidChoices);
        var power = Assert.Single(player.Morale);
        Assert.True(power.Tapped);
        Assert.True(power.IsGodPower);
    }

    [Fact]
    [Trait("L12Evidence", "cards:S02-05D1,S02-0008")]
    public void DivinityRecoveryDeclarationRecognizesRingUniversalCardsInPrivateZones()
    {
        var game = Create(8611, "S02-05D1");
        var player = game.State.Players[0];
        var neutral = Card("S02-0003", "batch6lc-divinity-neutral");
        player.Graveyard.Add(neutral);
        player.Relic = Card("S02-0008", "batch6lc-divinity-ring");
        for (var index = 0; index < 2; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S02-05C1", InstanceId = $"batch6lc-divinity-power-{index}", IsGodPower = true,
            });

        var activation = game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "divinityPower"));
        Assert.True(activation.Accepted, activation.Error);
        Resolve(game, "mode:recover");
        var recover = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(neutral.InstanceId, recover.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-05D1")]
    public void DivinityEntrySegmentSurvivesNegationOfItsIndependentRecoverySegment()
    {
        var game = Create(8612, "S02-05D1");
        var player = game.State.Players[0];
        var recover = Card("S02-0522", "batch6lc-divinity-recover");
        var entry = Card("S02-0502", "batch6lc-divinity-entry");
        player.Graveyard.AddRange([recover, entry]);
        for (var index = 0; index < 2; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S02-05C1", InstanceId = $"batch6lc-divinity-segment-power-{index}",
                IsGodPower = true,
            });

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "divinityPower")).Accepted);
        Resolve(game, "mode:recover");
        Resolve(game, recover.InstanceId);
        Resolve(game, entry.InstanceId);
        Resolve(game, "0:0");

        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("divinity-recover", first.Data["atomicFlow"]);
        first.Negated = true;
        var second = PassUntilFlow(game, "divinity-entry");
        Assert.Contains(recover, player.Graveyard);
        Assert.Contains(entry, player.Graveyard);
        PassResponses(game);

        Assert.Contains(recover, player.Graveyard);
        Assert.Same(entry, player.Field[0][0]);
        Assert.All(player.Morale, morale =>
        {
            Assert.True(morale.Tapped);
            Assert.False(morale.IsGodPower);
        });
        Assert.DoesNotContain(game.State.EffectStack, item => item.StackItemId == second.StackItemId);
    }

    [Fact]
    [Trait("L12Evidence", "entry:olympus-four-state-resource-model")]
    public void GodPowerPaymentPreservesTheFourIndependentFaceAndReadinessStates()
    {
        var player = Create(8613).State.Players[0];
        var activeMorale = new L12MoraleCard { CardId = "S02-05C1A", InstanceId = "batch6lc-active-morale" };
        var restedMorale = new L12MoraleCard { CardId = "S02-05C1A", InstanceId = "batch6lc-rested-morale", Tapped = true };
        var activePower = new L12MoraleCard { CardId = "S02-05C1", InstanceId = "batch6lc-active-power", IsGodPower = true };
        var restedPower = new L12MoraleCard { CardId = "S02-05C1", InstanceId = "batch6lc-rested-power", IsGodPower = true, Tapped = true };
        player.Morale.AddRange([activeMorale, restedMorale, activePower, restedPower]);

        Assert.True(L12S2ZoneOps.ConsumeGodPower(player, 1));
        Assert.True(activePower.IsGodPower);
        Assert.True(activePower.Tapped);
        Assert.False(activeMorale.IsGodPower);
        Assert.False(activeMorale.Tapped);
        Assert.False(restedMorale.IsGodPower);
        Assert.True(restedMorale.Tapped);
        Assert.True(restedPower.IsGodPower);
        Assert.True(restedPower.Tapped);
    }
}
