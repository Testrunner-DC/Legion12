using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class Bq20260903_01RegressionTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 69031, string? firstMaster = null)
    {
        var baseDeck = Catalog.DeckAt(0);
        var firstDeck = firstMaster is null ? baseDeck : new L12PresetDeckDefinition
        {
            Name = $"{firstMaster} BQ-20260903-01",
            MasterId = firstMaster,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "bq-20260903-01", "BQ090301", seed,
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
            player.Resolving.Clear();
            player.Morale.Clear();
            player.UsedAbilities.Clear();
            player.SpecialZones.Runes = 0;
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
            OwnerIndex = 1,
        };
    }

    public static IEnumerable<object[]> OpponentTurnFrontBonusCards()
    {
        foreach (var cardId in new[]
        {
            "S01-0107", "S01-0212", "S01-0312", "S02-0004", "S02-0007", "S02-0615",
            "ST02-02", "ST04-01", "ST06-02",
        })
            yield return new object[] { cardId };
    }

    [Theory]
    [MemberData(nameof(OpponentTurnFrontBonusCards))]
    public void StructuredOpponentTurnFrontBonusCoversTheWholePrintedPool(string cardId)
    {
        var game = Create();
        var card = Card(cardId, $"opponent-turn-{cardId}");
        game.State.Players[1].Field[0][0] = card;

        game.SnapshotFor(0);
        Assert.Equal(card.BaseTroops + 1000, card.Troops);

        game.SnapshotFor(0);
        Assert.Equal(card.BaseTroops + 1000, card.Troops);

        game.State.Players[1].Field[0][0] = null;
        game.State.Players[1].Field[1][0] = card;
        game.SnapshotFor(0);
        Assert.Equal(card.BaseTroops, card.Troops);

        game.State.ActivePlayer = 1;
        game.State.Players[1].Field[1][0] = null;
        game.State.Players[1].Field[0][0] = card;
        game.SnapshotFor(1);
        Assert.Equal(card.BaseTroops, card.Troops);
    }

    [Theory]
    [InlineData("S01-0204")]
    [InlineData("ST01-04")]
    public void FrontTauntWithoutPrintedOpponentTurnTroopsDoesNotGainTheBonus(string cardId)
    {
        var game = Create();
        var card = Card(cardId, $"taunt-only-{cardId}");
        game.State.Players[1].Field[0][0] = card;

        game.SnapshotFor(0);

        Assert.Equal(card.BaseTroops, card.Troops);
        Assert.Equal(0, L12StructuredCardRules.OpponentTurnFrontTroopsBonus(cardId));
    }

    public static IEnumerable<object[]> PromotionPairs()
    {
        yield return new object[] { "S02-0501", "S02-0502" };
        yield return new object[] { "S02-0503", "S02-0504" };
        yield return new object[] { "S02-0505", "S02-0506" };
        yield return new object[] { "S02-0507", "S02-0508" };
        yield return new object[] { "ST05-01", "S02-0512" };
    }

    [Theory]
    [MemberData(nameof(PromotionPairs))]
    public void OwnSnapshotPublishesServerAuthoritativePromotionFoundationInstances(
        string promotionCardId, string foundationCardId)
    {
        var game = Create();
        var player = game.State.Players[0];
        var promoted = Card(promotionCardId, $"promoted-{promotionCardId}");
        var foundation = Card(foundationCardId, $"foundation-{foundationCardId}");
        promoted.OwnerIndex = 0;
        foundation.OwnerIndex = 0;
        player.Hand.Add(promoted);
        player.Field[0][0] = foundation;
        for (var index = 0; index < 4; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"promotion-power-{index}",
                CardId = "S02-05C1A",
                IsGodPower = true,
                Tapped = false,
            });

        var own = JsonSerializer.SerializeToElement(game.SnapshotFor(0));
        var options = own.GetProperty("Players")[0].GetProperty("promotionOptions");
        var candidates = options.GetProperty(promoted.InstanceId).EnumerateArray()
            .Select(element => element.GetString()!).ToArray();

        Assert.Equal([foundation.InstanceId], candidates);
        Assert.DoesNotContain(promoted.InstanceId, JsonSerializer.Serialize(game.SnapshotFor(1)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void PromotionSnapshotRejectsOrdinaryMoraleAndInsufficientGodPower()
    {
        var game = Create();
        var player = game.State.Players[0];
        var promoted = Card("ST05-01", "aeneas-promotion");
        var foundation = Card("S02-0512", "aeneas-foundation");
        player.Hand.Add(promoted);
        player.Field[0][0] = foundation;
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = "ordinary-morale",
            CardId = "S02-05C1A",
            IsGodPower = false,
            Tapped = false,
        });

        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0));

        Assert.False(snapshot.GetProperty("Players")[0].GetProperty("promotionOptions")
            .TryGetProperty(promoted.InstanceId, out _));
    }

    [Fact]
    public void LancelotEntryRuneCostUsesTheSharedSpendEventPath()
    {
        var game = Create();
        var player = game.State.Players[0];
        var lancelot = Card("S02-0602", "lancelot-shared-rune-spend");
        lancelot.OwnerIndex = 0;
        player.Field[0][0] = lancelot;
        player.SpecialZones.Runes = 1;

        var queue = typeof(L12GameEngine).GetMethod("QueueOrPushTriggeredEffect",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(queue);
        queue.Invoke(game, [0, lancelot, "enter", "兰斯洛特登场", null, null]);
        var declaration = Assert.Single(game.State.PendingPrompts);

        var result = game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            Choice: "mode:use"));

        Assert.True(result.Accepted, result.Error);
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Contains(game.State.Events, actionEvent => actionEvent.Type == "cost"
            && actionEvent.Cards.Any(card => card.InstanceId == lancelot.InstanceId));
    }

    public static IEnumerable<object[]> ActiveRestAbilities()
    {
        foreach (var pair in new (string CardId, string Ability)[]
        {
            ("S01-0105", "searchBrothers"), ("S01-0109", "addMorale"),
            ("S01-0117", "artifactDraw"), ("S01-0117", "artifactSearch"),
            ("S01-01D1", "palaceExchange"), ("S01-0214", "cleopatraGuard"),
            ("S01-0215", "ankhReady"), ("S01-0215", "ankhDraw"),
            ("S01-0317", "gramDamage"), ("S01-03D1", "valhallaKill"),
            ("S01-04D1", "yomiRecover"), ("S02-0003", "disableCounters"),
            ("S02-0104", "shennongReset"), ("S02-0204", "imhotepDiscount"),
            ("S02-0205", "scarabSummon"), ("S02-0404", "magatamaMove"),
            ("S02-0404", "magatamaImmortal"), ("S02-0510", "hippolytaRevive"),
            ("S02-0513", "aristotleDiscount"), ("S02-0520", "forgePromotionDiscount"),
            ("S02-0520", "forgeReadyOnKill"), ("S02-05D1", "divinityFreePromotion"),
            ("S02-0603", "merlinRune"), ("S02-0616", "amakineTop"),
            ("S02-06D1", "avalonDebuff"), ("ST02-05", "oasisDancerBuff"),
            ("ST03-05", "christinaFreeTactic"), ("ST03-07", "kaneMillOne"),
            ("ST04-06", "oiranTransfer"), ("ST05-06", "telemachusTopThree"),
            ("ST06-09", "lightSwordActive"),
        })
            yield return new object[] { pair.CardId, pair.Ability };
    }

    [Theory]
    [MemberData(nameof(ActiveRestAbilities))]
    public void ActiveRestAndPrintedOncePerTurnHaveIndependentStructuredIdentities(
        string cardId, string ability)
        => Assert.True(L12StructuredCardRules.IsActiveRestAbility(cardId, ability));

    [Theory]
    [InlineData("S01-0317", "gramReady")]
    [InlineData("S01-01D1", "palaceReward")]
    [InlineData("S02-0205", "scarabDebuff")]
    [InlineData("S02-05D1", "divinityPower")]
    [InlineData("S02-06D1", "avalonRecover")]
    public void OtherAbilitiesOnActiveRestCardsRemainPrintedOnceOrIndependent(
        string cardId, string ability)
        => Assert.False(L12StructuredCardRules.IsActiveRestAbility(cardId, ability));

    [Fact]
    public void MerlinMayActiveRestAgainAfterAnotherEffectReadiesHim()
    {
        var game = Create();
        var player = game.State.Players[0];
        var merlin = Card("S02-0603", "merlin-repeat-active-rest");
        merlin.OwnerIndex = 0;
        var enemy = Card("S02-0507", "merlin-repeat-target");
        player.Field[0][0] = merlin;
        game.State.Players[1].Field[0][0] = enemy;
        player.SpecialZones.Runes = 2;

        Assert.True(game.Handle(0, new L12Command("activateAbility", merlin.InstanceId,
            Ability: "merlinRune")).Accepted);
        ResolveOnlyPrompt(game, "mode:debuff");
        ResolveOnlyPrompt(game, enemy.InstanceId);
        PassResponses(game);

        Assert.True(merlin.Tapped);
        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.DoesNotContain($"active:{merlin.InstanceId}:merlinRune", player.UsedAbilities);

        merlin.Tapped = false;
        var second = game.Handle(0, new L12Command("activateAbility", merlin.InstanceId,
            Ability: "merlinRune"));

        Assert.True(second.Accepted, second.Error);
        Assert.Single(game.State.PendingPrompts);
    }

    [Fact]
    public void AbilityViewIgnoresOnceKeyOnlyForTheActiveRestAbilityOnTheSameCard()
    {
        var game = Create();
        var player = game.State.Players[0];
        var scarab = Card("S02-0205", "scarab-mixed-usage-semantics");
        scarab.OwnerIndex = 0;
        player.Field[0][0] = scarab;
        var summonTarget = Card("S02-0201", "scarab-mixed-usage-target");
        summonTarget.OwnerIndex = 0;
        player.Graveyard.Add(summonTarget);
        player.UsedAbilities.Add($"active:{scarab.InstanceId}:scarabSummon");
        player.UsedAbilities.Add($"active:{scarab.InstanceId}:scarabDebuff");

        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0), WebJson);
        var abilities = snapshot.GetProperty("players")[0].GetProperty("field")[0][0]
            .GetProperty("abilities").EnumerateArray().ToArray();
        var summon = abilities.Single(view => view.GetProperty("id").GetString() == "scarabSummon");
        var debuff = abilities.Single(view => view.GetProperty("id").GetString() == "scarabDebuff");

        Assert.True(summon.GetProperty("enabled").GetBoolean());
        Assert.False(debuff.GetProperty("enabled").GetBoolean());
        Assert.Contains("本回合已经发动", debuff.GetProperty("disabledReason").GetString());
    }

    [Fact]
    public void TsukuyomiFollowMoveMaySelectAndMoveAnOpponentLegion()
    {
        var game = Create(69032, "S02-04M1");
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var moved = Card("S02-0401", "tsukuyomi-own-moved");
        var opponentTarget = Card("S02-0402", "tsukuyomi-opponent-target");
        moved.OwnerIndex = 0;
        opponentTarget.OwnerIndex = 1;
        player.Field[1][0] = moved;
        opponent.Field[0][2] = opponentTarget;
        player.Morale.AddRange([
            new L12MoraleCard { CardId = "S02-04C1", InstanceId = "tsukuyomi-move-cost", Tapped = false },
            new L12MoraleCard { CardId = "S02-04C1", InstanceId = "tsukuyomi-follow-cost", Tapped = false },
        ]);

        var movement = game.Handle(0, new L12Command("move", moved.InstanceId, Row: 0, Slot: 0));
        Assert.True(movement.Accepted, movement.Error);
        var order = Assert.Single(game.State.PendingPrompts);
        var follow = Assert.Single(order.ValidChoices,
            id => order.Data[id].Contains("军团位移时效果", StringComparison.Ordinal));
        var front = Assert.Single(order.ValidChoices, id => id != follow);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: order.PromptId,
            CardInstanceIds: [follow, front])).Accepted);

        ResolveOnlyPrompt(game, "mode:use");
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(opponentTarget.InstanceId, targetPrompt.ValidChoices);
        ResolveOnlyPrompt(game, opponentTarget.InstanceId);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("1", slotPrompt.Data.GetValueOrDefault("targetPlayerIndex"));
        Assert.Contains("0:1", slotPrompt.ValidChoices);
        ResolveOnlyPrompt(game, "0:1");
        PassResponses(game);

        Assert.Null(opponent.Field[0][2]);
        Assert.Same(opponentTarget, opponent.Field[0][1]);
        Assert.Equal(-1, opponentTarget.CostModifier);
        Assert.Contains("active:master-0:tsukuyomiFollowMove", player.UsedAbilities);

        RecordMovement(game, 0, moved, 1, 1);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Fact]
    public void MagatamaCavalryMoveOnlyOffersReadyFriendlyLegions()
    {
        var game = Create(690321, "S02-04M1");
        var player = game.State.Players[0];
        var magatama = Card("S02-0404", "magatama-ready-filter");
        var ready = Card("S02-0401", "magatama-ready-legion");
        var rested = Card("S02-0402", "magatama-rested-legion");
        magatama.OwnerIndex = ready.OwnerIndex = rested.OwnerIndex = 0;
        rested.Tapped = true;
        player.Relic = magatama;
        player.Field[0][0] = ready;
        player.Field[0][1] = rested;

        var start = game.Handle(0, new L12Command("activateAbility", magatama.InstanceId,
            Ability: "magatamaMove"));

        Assert.True(start.Accepted, start.Error);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(ready.InstanceId, targetPrompt.ValidChoices);
        Assert.DoesNotContain(rested.InstanceId, targetPrompt.ValidChoices);
    }

    [Fact]
    public void DecliningTsukuyomiFollowMoveDoesNotConsumeItsTurnUsage()
    {
        var game = Create(69033, "S02-04M1");
        var player = game.State.Players[0];
        var moved = Card("S02-0401", "tsukuyomi-decline-moved");
        var target = Card("S02-0402", "tsukuyomi-decline-target");
        moved.OwnerIndex = target.OwnerIndex = 0;
        player.Field[0][0] = moved;
        player.Field[0][2] = target;
        player.Morale.Add(new L12MoraleCard
        {
            CardId = "S02-04C1", InstanceId = "tsukuyomi-decline-cost", Tapped = false,
        });

        RecordMovement(game, 0, moved, 1, 1);
        var first = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:none", first.ValidChoices);
        ResolveOnlyPrompt(game, "mode:none");

        Assert.DoesNotContain("active:master-0:tsukuyomiFollowMove", player.UsedAbilities);
        Assert.False(player.Morale[0].Tapped);

        RecordMovement(game, 0, moved, 1, 1);
        var second = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:use", second.ValidChoices);
    }

    [Fact]
    public void KondoLethalReplacementDoesNotRunBeforeAnAvailableSupportChoice()
    {
        var game = Create(69034);
        var attacker = Card("S02-0003", "kondo-support-attacker");
        var protectedCard = Card("ST04-03", "kondo-support-protected");
        var kondo = Card("ST04-05", "kondo-support-source");
        var supporter = Card("S02-0003", "kondo-supporter");
        attacker.OwnerIndex = 0;
        protectedCard.OwnerIndex = kondo.OwnerIndex = supporter.OwnerIndex = 1;
        attacker.Troops = 6000;
        protectedCard.Troops = 3000;
        supporter.Troops = 3000;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = protectedCard;
        game.State.Players[1].Field[0][1] = kondo;
        game.State.Players[1].Field[1][0] = supporter;

        var attack = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", protectedCard.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);
        PassResponses(game);

        Assert.Equal(L12Phase.Defense, game.State.Phase);
        Assert.Equal(L12CombatStage.DefenseChoice, game.State.PendingDefense?.Stage);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation == "combat-lethal-replacement");

        var support = game.Handle(1, new L12Command("resolveDefense",
            SupportInstanceId: supporter.InstanceId));
        Assert.True(support.Accepted, support.Error);
        PassResponses(game);

        Assert.Same(protectedCard, game.State.Players[1].Field[0][0]);
        Assert.Same(kondo, game.State.Players[1].Field[0][1]);
        Assert.Contains(supporter, game.State.Players[1].Graveyard);
        Assert.DoesNotContain(game.State.Events, actionEvent => actionEvent.Type == "replacement");
        Assert.DoesNotContain(game.State.Events, actionEvent => actionEvent.Type == "support-skipped");
    }

    [Fact]
    public void OiranGiftCompletesTheTopThreeChoiceBeforeReadyingMorale()
    {
        var game = Create(69035);
        var player = game.State.Players[0];
        var gift = Card("S01-0419", "oiran-order-source");
        var chosen = Card("S01-0401", "oiran-order-chosen");
        var second = Card("S01-0101", "oiran-order-second");
        var third = Card("S01-0201", "oiran-order-third");
        var morale = new L12MoraleCard
        {
            CardId = "S01-04C1", InstanceId = "oiran-order-morale", Tapped = true,
        };
        gift.OwnerIndex = chosen.OwnerIndex = second.OwnerIndex = third.OwnerIndex = 0;
        player.Hand.Add(gift);
        player.Library.InsertRange(0, [chosen, second, third]);
        player.Morale.Add(morale);
        player.TemporaryMorale = 1;

        var play = game.Handle(0, new L12Command("playCard", gift.InstanceId));
        Assert.True(play.Accepted, play.Error);
        ResolveOnlyPrompt(game, "mode:morale");
        ResolveOnlyPrompt(game, morale.InstanceId);
        PassResponses(game);

        var topThree = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("oiran-pick", topThree.Data.GetValueOrDefault("action"));
        Assert.True(morale.Tapped);
        ResolveOnlyPrompt(game, chosen.InstanceId);
        ResolveOnlyBottomOrder(game, second.InstanceId, third.InstanceId);

        Assert.True(morale.Tapped);
        PassResponses(game);

        Assert.Contains(chosen, player.Hand);
        Assert.False(morale.Tapped);
    }

    [Fact]
    public void TombConstructReturnsAllThreeAttachedGuardsWithoutDuplicatingThem()
    {
        var game = Create(69036);
        var player = game.State.Players[0];
        var construct = Card("S01-0204", "three-guard-construct");
        construct.OwnerIndex = 0;
        var guards = Enumerable.Range(0, 3)
            .Select(index => Card("S01-0212", $"three-guard-{index}"))
            .ToArray();
        foreach (var guard in guards)
        {
            guard.OwnerIndex = 0;
            construct.AttachedCards.Add(guard);
        }
        player.Field[0][0] = construct;

        var destroy = game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: construct.InstanceId));
        Assert.True(destroy.Accepted, destroy.Error);
        var order = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "trigger-batch-order");
        ResolveOnlyPromptMany(game, [.. order.ValidChoices]);
        ResolveOnlyPrompt(game, "0:0");
        ResolveOnlyPrompt(game, "0:1");
        ResolveOnlyPrompt(game, "0:2");
        PassResponses(game);
        for (var safety = 0; safety < 20 && game.State.PendingPrompts.Count > 0; safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.Kind == "response") ResolveOnlyPrompt(game, "pass");
            else ResolveOnlyPrompt(game, prompt.ValidChoices[0]);
        }

        Assert.All(guards, guard =>
        {
            Assert.Contains(player.Field.SelectMany(row => row), card => card == guard);
            Assert.DoesNotContain(guard, player.Graveyard);
            Assert.Equal(1, player.Field.SelectMany(row => row).Count(card => card == guard)
                + player.Graveyard.Count(card => card == guard));
        });
    }

    [Fact]
    public void AmakineUsesPrintedOtherworldFactionAndMayTakeARoundTableKnight()
    {
        var game = Create(69037);
        var player = game.State.Players[0];
        var amakine = Card("S02-0616", "amakine-round-table-source");
        var roundTableKnight = Card("S02-0602", "amakine-round-table-target");
        amakine.OwnerIndex = roundTableKnight.OwnerIndex = 0;
        player.Field[0][0] = amakine;
        player.Library.Insert(0, roundTableKnight);

        var activation = game.Handle(0, new L12Command("activateAbility", amakine.InstanceId,
            Ability: "amakineTop"));
        Assert.True(activation.Accepted, activation.Error);
        PassResponses(game);
        var choice = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("hand", choice.ValidChoices);
        ResolveOnlyPrompt(game, "hand");

        Assert.Contains(roundTableKnight, player.Hand);
        Assert.DoesNotContain(roundTableKnight, player.Library);
    }

    [Fact]
    public void TemporaryMoraleCanPayTheFactionEffectThatSummonsATombGuard()
    {
        var game = Create(69038, "S01-02M1");
        var player = game.State.Players[0];
        var guard = Card("S01-0212", "temporary-morale-tomb-guard");
        guard.OwnerIndex = 0;
        player.Graveyard.Add(guard);
        player.TemporaryMorale = 2;

        var activation = game.Handle(0, new L12Command("activateAbility", "faction-0",
            Ability: "sunGuard"));
        Assert.True(activation.Accepted, activation.Error);
        ResolveOnlyPrompt(game, guard.InstanceId);
        ResolveOnlyPrompt(game, "0:0");
        PassResponses(game);

        Assert.Equal(0, player.TemporaryMorale);
        Assert.Same(guard, player.Field[0][0]);
        Assert.False(guard.Tapped);
        Assert.DoesNotContain(guard, player.Graveyard);
    }

    private static void ResolveOnlyPrompt(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void ResolveOnlyPromptMany(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: choices.ToList()));
        Assert.True(result.Accepted, result.Error);
    }

    private static void ResolveOnlyBottomOrder(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                BottomCardInstanceIds: choices.ToList()));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var guard = 0; guard < 20 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; guard++)
            ResolveOnlyPrompt(game, "pass");
    }

    private static void RecordMovement(L12GameEngine game, int controller, L12CardInstance moved,
        int fromRow, int toRow)
    {
        var method = typeof(L12GameEngine).GetMethod("RecordLegionMovement",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(game, [controller, moved, fromRow, toRow]);
    }
}
