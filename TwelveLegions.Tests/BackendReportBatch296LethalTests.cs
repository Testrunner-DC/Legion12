using System.Text.Json;
using System.Text.Json.Serialization;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BackendReportBatch296LethalTests
{
    private const string HoremhebId = "S01-0205";
    private const string HelenId = "S02-0515";
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private sealed record CombatFixture(
        L12GameEngine Game,
        int AttackerIndex,
        int DefenderIndex,
        L12CardInstance Attacker,
        L12CardInstance ProtectedCard,
        L12CardInstance? Substitute,
        L12MoraleCard OrdinaryMorale,
        L12MoraleCard GodPower);

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, $"backend-report-batch296-p4-{seed}", "BATCH296P4", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
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
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner, int? troops = null)
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
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }

    private static L12MoraleCard Morale(string instanceId, bool godPower)
        => new()
        {
            InstanceId = instanceId,
            CardId = godPower ? "S02-05C1" : "S01-01C1",
            IsGodPower = godPower,
            Tapped = false,
        };

    private static CombatFixture Arrange(string protectedCardId, int defenderIndex, int seed,
        bool withSubstitute = true)
    {
        var game = Create(seed);
        var attackerIndex = 1 - defenderIndex;
        game.State.ActivePlayer = attackerIndex;
        game.State.FirstPlayer = attackerIndex;
        var attacker = Card("S01-0105", $"batch296-attacker-{seed}", attackerIndex, troops: 6000);
        var protectedCard = Card(protectedCardId, $"batch296-protected-{seed}", defenderIndex, troops: 4000);
        var ordinaryMorale = Morale($"batch296-morale-{seed}", godPower: false);
        var godPower = Morale($"batch296-god-power-{seed}", godPower: true);
        var attackerPlayer = game.State.Players[attackerIndex];
        var defender = game.State.Players[defenderIndex];
        attackerPlayer.Field[0][0] = attacker;
        defender.Field[0][0] = protectedCard;
        defender.Morale.AddRange([ordinaryMorale, godPower]);

        L12CardInstance? substitute = null;
        if (withSubstitute && protectedCardId == HoremhebId)
        {
            substitute = Card("S01-0212", $"batch296-guard-{seed}", defenderIndex);
            // A front-row guard remains a legal Horemheb substitute without creating a support choice.
            defender.Field[0][1] = substitute;
        }
        else if (withSubstitute && protectedCardId == HelenId)
        {
            substitute = Card("S02-0502", $"batch296-hand-legion-{seed}", defenderIndex);
            defender.Hand.Add(substitute);
        }

        return new CombatFixture(game, attackerIndex, defenderIndex, attacker, protectedCard,
            substitute, ordinaryMorale, godPower);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 40; safety++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault();
            if (prompt?.Kind != "response") return;
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.Fail("响应窗口未在安全上限内完成");
    }

    private static void BeginLethalCombat(CombatFixture fixture)
    {
        var attack = fixture.Game.Handle(fixture.AttackerIndex,
            new L12Command("attack", fixture.Attacker.InstanceId,
                Target: new L12AttackTarget("legion", fixture.ProtectedCard.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);
        PassResponses(fixture.Game);
        if (fixture.Game.State.PendingDefense?.Stage == L12CombatStage.DefenseChoice
            && fixture.Game.State.PendingPrompts.Count == 0)
        {
            var defense = fixture.Game.Handle(fixture.DefenderIndex,
                new L12Command("resolveDefense", CardInstanceIds: []));
            Assert.True(defense.Accepted, defense.Error);
            PassResponses(fixture.Game);
        }
    }

    private static L12Prompt ReplacementPrompt(CombatFixture fixture)
    {
        BeginLethalCombat(fixture);
        var prompt = Assert.Single(fixture.Game.State.PendingPrompts);
        Assert.Equal("combat-lethal-replacement", prompt.Continuation);
        Assert.Equal(fixture.DefenderIndex, prompt.PlayerIndex);
        Assert.Equal(fixture.ProtectedCard.InstanceId, prompt.Data.GetValueOrDefault("cardInstanceId"));
        Assert.Contains("decline", prompt.ValidChoices);
        return prompt;
    }

    private static CommandResult Resolve(L12GameEngine game, L12Prompt prompt, string choice)
        => game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));

    [Theory]
    [InlineData(HoremhebId, 0)]
    [InlineData(HoremhebId, 1)]
    [InlineData(HelenId, 0)]
    [InlineData(HelenId, 1)]
    [Trait("L12Evidence", "bug:BUG-20260908-54b108d2")]
    [Trait("L12Evidence", "bug:BUG-20260908-7c126af5")]
    public void CombatLethalSubstitutionMayBeAcceptedOnceWithoutChargingMorale(
        string protectedCardId, int defenderIndex)
    {
        var fixture = Arrange(protectedCardId, defenderIndex, 29610 + defenderIndex
            + (protectedCardId == HelenId ? 10 : 0));
        var prompt = ReplacementPrompt(fixture);
        var substitute = Assert.IsType<L12CardInstance>(fixture.Substitute);

        var accepted = Resolve(fixture.Game, prompt, substitute.InstanceId);

        Assert.True(accepted.Accepted, accepted.Error);
        PassResponses(fixture.Game);
        Assert.Null(fixture.Game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, fixture.Game.State.Phase);
        Assert.Empty(fixture.Game.State.PendingPrompts);
        Assert.Same(fixture.ProtectedCard,
            fixture.Game.State.Players[defenderIndex].Field[0][0]);
        Assert.False(fixture.OrdinaryMorale.Tapped);
        Assert.False(fixture.GodPower.Tapped);
        Assert.Equal(2, fixture.Game.State.Players[defenderIndex].Morale.Count);
        Assert.Contains(substitute, fixture.Game.State.Players[defenderIndex].Graveyard);
        if (protectedCardId == HoremhebId)
            Assert.Contains(fixture.Game.State.Events, entry => entry.Type == "leave"
                && entry.Cards.Any(card => card.InstanceId == substitute.InstanceId));
        else
        {
            Assert.Contains(fixture.Game.State.Events, entry => entry.Type == "discard"
                && entry.Cards.Any(card => card.InstanceId == substitute.InstanceId));
            Assert.DoesNotContain(fixture.Game.State.Events, entry => entry.Type == "leave"
                && entry.Cards.Any(card => card.InstanceId == substitute.InstanceId));
        }

        var repeated = Resolve(fixture.Game, prompt, substitute.InstanceId);
        Assert.False(repeated.Accepted);
        Assert.Empty(fixture.Game.State.PendingPrompts);
        Assert.Same(fixture.ProtectedCard,
            fixture.Game.State.Players[defenderIndex].Field[0][0]);
    }

    [Theory]
    [InlineData(HoremhebId, 0)]
    [InlineData(HoremhebId, 1)]
    [InlineData(HelenId, 0)]
    [InlineData(HelenId, 1)]
    [Trait("L12Evidence", "bug:BUG-20260908-54b108d2")]
    [Trait("L12Evidence", "bug:BUG-20260908-7c126af5")]
    public void CombatLethalSubstitutionDeclineIsAskedOnceAndParentCombatFinishes(
        string protectedCardId, int defenderIndex)
    {
        var fixture = Arrange(protectedCardId, defenderIndex, 29630 + defenderIndex
            + (protectedCardId == HelenId ? 10 : 0));
        var prompt = ReplacementPrompt(fixture);
        var substitute = Assert.IsType<L12CardInstance>(fixture.Substitute);

        var declined = Resolve(fixture.Game, prompt, "decline");

        Assert.True(declined.Accepted, declined.Error);
        PassResponses(fixture.Game);
        Assert.Null(fixture.Game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, fixture.Game.State.Phase);
        Assert.Empty(fixture.Game.State.PendingPrompts);
        Assert.Contains(fixture.ProtectedCard, fixture.Game.State.Players[defenderIndex].Graveyard);
        Assert.False(fixture.OrdinaryMorale.Tapped);
        Assert.False(fixture.GodPower.Tapped);
        Assert.Equal(2, fixture.Game.State.Players[defenderIndex].Morale.Count);
        if (protectedCardId == HoremhebId)
            Assert.Same(substitute, fixture.Game.State.Players[defenderIndex].Field[0][1]);
        else
            Assert.Contains(substitute, fixture.Game.State.Players[defenderIndex].Hand);

        var repeated = Resolve(fixture.Game, prompt, "decline");
        Assert.False(repeated.Accepted);
        Assert.Empty(fixture.Game.State.PendingPrompts);
    }

    [Theory]
    [InlineData(HoremhebId, 0)]
    [InlineData(HelenId, 1)]
    [Trait("L12Evidence", "entry:no-legal-lethal-substitute")]
    public void NoLegalSubstituteCreatesNoEmptyPromptAndNormalCombatDeathStillApplies(
        string protectedCardId, int defenderIndex)
    {
        var fixture = Arrange(protectedCardId, defenderIndex, 29650 + defenderIndex,
            withSubstitute: false);

        BeginLethalCombat(fixture);

        PassResponses(fixture.Game);
        Assert.Empty(fixture.Game.State.PendingPrompts);
        Assert.Null(fixture.Game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, fixture.Game.State.Phase);
        Assert.Contains(fixture.ProtectedCard, fixture.Game.State.Players[defenderIndex].Graveyard);
        Assert.Contains(fixture.Game.State.Events, entry => entry.Type == "leave"
            && entry.Cards.Any(card => card.InstanceId == fixture.ProtectedCard.InstanceId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "bug:BUG-20260908-7c126af5")]
    public void AttackingHoremhebCanDeclineReplacementOnceAndFinishItsLethalAttack(int attackerIndex)
    {
        var game = Create(29680 + attackerIndex);
        var defenderIndex = 1 - attackerIndex;
        game.State.ActivePlayer = attackerIndex;
        game.State.FirstPlayer = attackerIndex;
        var horemheb = Card(HoremhebId, "batch296-attacking-horemheb", attackerIndex, troops: 4000);
        var guard = Card("S01-0212", "batch296-attacking-horemheb-guard", attackerIndex);
        var defender = Card("S01-0105", "batch296-horemheb-strong-defender", defenderIndex, troops: 6000);
        var morale = Morale("batch296-attacking-horemheb-morale", false);
        var godPower = Morale("batch296-attacking-horemheb-god", true);
        game.State.Players[attackerIndex].Field[0][0] = horemheb;
        game.State.Players[attackerIndex].Field[0][1] = guard;
        game.State.Players[attackerIndex].Morale.AddRange([morale, godPower]);
        game.State.Players[defenderIndex].Field[0][0] = defender;
        BeginLethalCombat(new CombatFixture(game, attackerIndex, defenderIndex, horemheb,
            defender, guard, morale, godPower));

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("combat-lethal-replacement", prompt.Continuation);
        Assert.Equal(attackerIndex, prompt.PlayerIndex);
        Assert.Equal(horemheb.InstanceId, prompt.Data.GetValueOrDefault("cardInstanceId"));
        Assert.Contains(guard.InstanceId, prompt.ValidChoices);
        var declined = Resolve(game, prompt, "decline");
        Assert.True(declined.Accepted, declined.Error);
        PassResponses(game);

        Assert.Empty(game.State.PendingPrompts);
        Assert.Null(game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Contains(horemheb, game.State.Players[attackerIndex].Graveyard);
        Assert.Same(guard, game.State.Players[attackerIndex].Field[0][1]);
        Assert.Same(defender, game.State.Players[defenderIndex].Field[0][0]);
        Assert.False(morale.Tapped);
        Assert.False(godPower.Tapped);
        Assert.False(Resolve(game, prompt, "decline").Accepted);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Fact]
    [Trait("L12Evidence", "entry:simultaneous-combat-lethals")]
    public void DecliningHelenDoesNotBlockTheOtherSeatHoremhebDecisionInTheSameCombat()
    {
        var game = Create(29660);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        var horemheb = Card(HoremhebId, "batch296-simultaneous-horemheb", 0, troops: 4000);
        var guard = Card("S01-0212", "batch296-simultaneous-guard", 0);
        var helen = Card(HelenId, "batch296-simultaneous-helen", 1, troops: 4000);
        var handLegion = Card("S02-0502", "batch296-simultaneous-hand", 1);
        game.State.Players[0].Field[0][0] = horemheb;
        game.State.Players[0].Field[0][1] = guard;
        game.State.Players[1].Field[0][0] = helen;
        game.State.Players[1].Hand.Add(handLegion);
        var fixture = new CombatFixture(game, 0, 1, horemheb, helen, handLegion,
            Morale("unused-ordinary", false), Morale("unused-god", true));

        var helenPrompt = ReplacementPrompt(fixture);
        Assert.True(Resolve(game, helenPrompt, "decline").Accepted);

        var horemhebPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("combat-lethal-replacement", horemhebPrompt.Continuation);
        Assert.Equal(0, horemhebPrompt.PlayerIndex);
        Assert.Equal(horemheb.InstanceId, horemhebPrompt.Data.GetValueOrDefault("cardInstanceId"));
        Assert.Contains(guard.InstanceId, horemhebPrompt.ValidChoices);
        Assert.True(Resolve(game, horemhebPrompt, guard.InstanceId).Accepted);
        PassResponses(game);

        Assert.Null(game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Contains(helen, game.State.Players[1].Graveyard);
        Assert.Same(horemheb, game.State.Players[0].Field[0][0]);
        Assert.Contains(guard, game.State.Players[0].Graveyard);
        Assert.Contains(handLegion, game.State.Players[1].Hand);
    }

    [Theory]
    [InlineData(HoremhebId)]
    [InlineData(HelenId)]
    [Trait("L12Evidence", "entry:new-independent-lethal-event")]
    public void ANewTurnCombatCreatesANewLethalDecisionInsteadOfPermanentProtection(string protectedCardId)
    {
        var fixture = Arrange(protectedCardId, defenderIndex: 1,
            seed: 29670 + (protectedCardId == HelenId ? 1 : 0));
        var firstPrompt = ReplacementPrompt(fixture);
        var firstSubstitute = Assert.IsType<L12CardInstance>(fixture.Substitute);
        Assert.True(Resolve(fixture.Game, firstPrompt, firstSubstitute.InstanceId).Accepted);
        PassResponses(fixture.Game);
        Assert.Null(fixture.Game.State.PendingDefense);

        fixture.Game.State.TurnSerial++;
        fixture.Game.State.Round++;
        fixture.Game.State.ActivePlayer = fixture.AttackerIndex;
        fixture.Game.State.Phase = L12Phase.Main;
        fixture.Attacker.AttacksThisTurn = 0;
        fixture.Attacker.Troops = 6000;
        fixture.Attacker.Tapped = false;
        L12CardInstance secondSubstitute;
        if (protectedCardId == HoremhebId)
        {
            secondSubstitute = Card("S01-0212", "batch296-second-guard", fixture.DefenderIndex);
            fixture.Game.State.Players[fixture.DefenderIndex].Field[0][1] = secondSubstitute;
        }
        else
        {
            secondSubstitute = Card("S02-0502", "batch296-second-hand", fixture.DefenderIndex);
            fixture.Game.State.Players[fixture.DefenderIndex].Hand.Add(secondSubstitute);
        }

        var secondPrompt = ReplacementPrompt(fixture);

        Assert.NotEqual(firstPrompt.PromptId, secondPrompt.PromptId);
        Assert.Contains(secondSubstitute.InstanceId, secondPrompt.ValidChoices);
        Assert.True(Resolve(fixture.Game, secondPrompt, "decline").Accepted);
        PassResponses(fixture.Game);
        Assert.Null(fixture.Game.State.PendingDefense);
        Assert.Contains(fixture.ProtectedCard,
            fixture.Game.State.Players[fixture.DefenderIndex].Graveyard);
    }

    [Theory]
    [InlineData(HoremhebId)]
    [InlineData(HelenId)]
    [Trait("L12Evidence", "entry:recorded-decline-snapshot-recovery")]
    public void RecordedDeclineSurvivesPendingCombatRoundTripAndAStalePromptCannotReverseIt(
        string protectedCardId)
    {
        var fixture = Arrange(protectedCardId, defenderIndex: 1,
            seed: 29680 + (protectedCardId == HelenId ? 1 : 0));
        var stalePrompt = ReplacementPrompt(fixture);
        var pending = Assert.IsType<L12PendingDefense>(fixture.Game.State.PendingDefense);
        pending.LethalReplacementDecisions[fixture.ProtectedCard.InstanceId] = false;

        var pendingJson = JsonSerializer.Serialize(pending);
        fixture.Game.State.PendingDefense = JsonSerializer.Deserialize<L12PendingDefense>(pendingJson,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
            });
        var restoredPending = Assert.IsType<L12PendingDefense>(fixture.Game.State.PendingDefense);
        Assert.False(restoredPending.LethalReplacementDecisions[fixture.ProtectedCard.InstanceId]);
        var reconnectSnapshot = fixture.Game.SnapshotFor(fixture.DefenderIndex);
        var snapshotCombat = Assert.IsType<L12PendingDefense>(reconnectSnapshot.PendingDefense);
        Assert.False(snapshotCombat.LethalReplacementDecisions[fixture.ProtectedCard.InstanceId]);
        var restoredPrompt = Assert.Single(fixture.Game.State.PendingPrompts);
        var restoredProtected = fixture.ProtectedCard;
        var restoredSubstitute = Assert.IsType<L12CardInstance>(fixture.Substitute);

        // A recovered stale/duplicated prompt cannot change the already recorded "decline" to acceptance.
        var staleSubmit = Resolve(fixture.Game, restoredPrompt, restoredSubstitute.InstanceId);

        Assert.True(staleSubmit.Accepted, staleSubmit.Error);
        PassResponses(fixture.Game);
        Assert.Null(fixture.Game.State.PendingDefense);
        Assert.Empty(fixture.Game.State.PendingPrompts);
        Assert.Contains(restoredProtected, fixture.Game.State.Players[fixture.DefenderIndex].Graveyard);
        if (protectedCardId == HoremhebId)
            Assert.Same(restoredSubstitute, fixture.Game.State.Players[fixture.DefenderIndex].Field[0][1]);
        else
            Assert.Contains(restoredSubstitute, fixture.Game.State.Players[fixture.DefenderIndex].Hand);
        Assert.DoesNotContain(fixture.Game.State.Players[fixture.DefenderIndex].UsedAbilities,
            key => key.StartsWith("pending:lethal-substitution:", StringComparison.Ordinal));

        var recoverySnapshot = fixture.Game.SnapshotFor(fixture.DefenderIndex);
        Assert.Null(recoverySnapshot.PendingDefense);
        Assert.Empty(recoverySnapshot.Prompts);
        Assert.Equal(L12Phase.Main, fixture.Game.State.Phase);
        var repeated = fixture.Game.Handle(fixture.DefenderIndex,
            new L12Command("resolvePrompt", PromptId: stalePrompt.PromptId,
                Choice: restoredSubstitute.InstanceId));
        Assert.False(repeated.Accepted);
        Assert.Empty(fixture.Game.State.PendingPrompts);
    }
}
