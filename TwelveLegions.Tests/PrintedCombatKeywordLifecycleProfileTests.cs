using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PrintedCombatKeywordLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence("S02-0505:ability:keyword-definition:cf232142ca7d10f9", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("S02-0602:ability:keyword-definition:beff9037e2c10a9d", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("S02-0612:ability:keyword-definition:beff9037e2c10a9d", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    public void PrintedChargeDefinitionUsesTheSharedFlagForProjectionAttackAndLeaveReset()
    {
        foreach (var cardId in new[] { "S02-0505", "S02-0602", "S02-0612" })
        {
            Assert.True(L12StructuredCardRules.HasKeywordDefinition(cardId, "charge"));
            var game = Create(72600 + cardId.Length);
            var card = Card(cardId, $"printed-charge-{cardId}", 0);
            card.HasCharge = true;
            card.SummonRound = game.State.Round;
            game.State.Players[0].Field[0][0] = card;
            Assert.Contains("冲锋", ActiveKeywords(game, 0));

            game = Restore(game);
            card = game.State.Players[0].Field[0][0]!;
            var result = game.Handle(0, new L12Command("attack", card.InstanceId,
                Target: new L12AttackTarget("master")));
            Assert.True(result.Accepted, result.Error);
        }

        var leaveGame = Create(72604);
        var leaving = Card("S02-0505", "printed-charge-leave", 0);
        leaving.HasCharge = true;
        leaveGame.State.Players[0].Field[0][0] = leaving;
        Assert.True(leaveGame.HandleGm(new L12GmCommand("returnCardToHand", 0,
            CardInstanceId: leaving.InstanceId)).Accepted);
        Assert.False(Assert.Single(leaveGame.State.Players[0].Hand).HasCharge);
    }

    [Fact]
    [L12AbilityEvidence("S02-0511:ability:keyword-definition:96aa4e9504b12339", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("S02-05M1:ability:keyword-definition:41657ed47ef085ae", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    public void PrintedShockDefinitionUsesTheSharedFlagForProjectionCollateralAndTurnExpiry()
    {
        foreach (var cardId in new[] { "S02-0511", "S02-05M1" })
        {
            Assert.True(L12StructuredCardRules.HasKeywordDefinition(cardId, "shock"));
            var game = Create(72610 + cardId.Length);
            // The definition is consumed by the shared instance flag. Use a neutral
            // legion so the defining cards' own attack abilities cannot delay shock
            // behind a separate optional declaration prompt.
            var attacker = Card("S01-0002", $"printed-shock-{cardId}", 0);
            var primary = Card("S01-0002", $"printed-shock-primary-{cardId}", 1);
            var adjacent = Card("S01-0002", $"printed-shock-adjacent-{cardId}", 1);
            attacker.HasShock = true;
            attacker.SummonRound = primary.SummonRound = adjacent.SummonRound = 0;
            adjacent.SetTroopsValue = 5000;
            adjacent.Troops = 5000;
            game.State.Players[0].Field[0][0] = attacker;
            game.State.Players[1].Field[0][1] = primary;
            game.State.Players[1].Field[0][0] = adjacent;
            Assert.Contains("震击", ActiveKeywords(game, 0));

            game = Restore(game);
            attacker = game.State.Players[0].Field[0][0]!;
            primary = game.State.Players[1].Field[0][1]!;
            adjacent = game.State.Players[1].Field[0][0]!;
            Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
                Target: new L12AttackTarget("legion", primary.InstanceId))).Accepted);
            PassResponses(game);
            Assert.Equal(3000, adjacent.CurrentTroops);
        }

        var expiry = Create(72614);
        var expiring = Card("S02-0511", "printed-shock-expiry", 0);
        expiring.HasShock = true;
        expiry.State.Players[0].Field[0][0] = expiring;
        Assert.True(expiry.Handle(0, new L12Command("endTurn")).Accepted);
        Assert.False(expiring.HasShock);
    }

    [Fact]
    [L12AbilityEvidence("S02-05M1:ability:keyword-definition:995c52041c470ca4", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("S02-0605:ability:keyword-definition:60bccaeb6d982ea8", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    public void PrintedStrongAttackDefinitionUsesTheSharedSemanticForProjectionDamageAndExpiry()
    {
        foreach (var cardId in new[] { "S02-05M1", "S02-0605" })
        {
            Assert.True(L12StructuredCardRules.HasKeywordDefinition(cardId, "strong-attack"));
            var game = Create(72620 + cardId.Length);
            var attacker = Card(cardId, $"printed-strong-{cardId}", 0);
            attacker.HasStrongAttack = true;
            attacker.SummonRound = 0;
            game.State.Players[0].Field[0][0] = attacker;
            Assert.Contains("强攻", ActiveKeywords(game, 0));

            game = Restore(game);
            attacker = game.State.Players[0].Field[0][0]!;
            Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
                Target: new L12AttackTarget("master"))).Accepted);
            Assert.Equal(2, game.State.PendingDefense?.MasterDamage);
        }

        var expiry = Create(72624);
        var expiring = Card("S02-0605", "printed-strong-expiry", 0);
        expiring.HasStrongAttack = true;
        expiry.State.Players[0].Field[0][0] = expiring;
        Assert.True(expiry.Handle(0, new L12Command("endTurn")).Accepted);
        Assert.False(expiring.HasStrongAttack);
    }

    [Fact]
    [L12AbilityEvidence("S02-0608:ability:keyword-definition:4d1e472a814a1e0b", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("S02-0611:ability:keyword-definition:4d1e472a814a1e0b", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    public void PrintedDeathImmunityDefinitionUsesTheSharedReplacementAndTurnStartExpiry()
    {
        foreach (var cardId in new[] { "S02-0608", "S02-0611" })
        {
            Assert.True(L12StructuredCardRules.HasKeywordDefinition(cardId, "death-immunity"));
            var game = Create(72630 + cardId.Length);
            var target = Card(cardId, $"printed-immortal-{cardId}", 0);
            target.ImmortalUses = 1;
            target.ImmortalUntilTurn = int.MaxValue;
            target.ImmortalExpiresAtPlayerTurnStart = 0;
            game.State.Players[0].Field[0][0] = target;
            Assert.Contains("免死", ActiveKeywords(game, 0));

            game = Restore(game);
            target = game.State.Players[0].Field[0][0]!;
            var result = game.HandleGm(new L12GmCommand("destroyCard", 0,
                CardInstanceId: target.InstanceId));
            Assert.False(result.Accepted);
            Assert.Equal(0, target.ImmortalUses);
        }

        var expiry = Create(72634);
        var expiring = Card("S02-0611", "printed-immortal-expiry", 0);
        expiring.ImmortalUses = 1;
        expiring.ImmortalUntilTurn = int.MaxValue;
        expiring.ImmortalExpiresAtPlayerTurnStart = 0;
        expiry.State.Players[0].Field[0][0] = expiring;
        Invoke(expiry, "ExpireEffectsAtPlayerTurnStart", 0);
        Assert.Equal(0, expiring.ImmortalUses);
        Assert.Equal(-1, expiring.ImmortalUntilTurn);
    }

    [Fact]
    [L12AbilityEvidence("S02-0302:ability:keyword-definition:eaba79729a9d7a65", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("S02-0503:ability:keyword-definition:6692b63a59c971d0", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("S02-0512:ability:keyword-definition:6692b63a59c971d0", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("S02-0615:ability:keyword-definition:8a4c9aff096f6526", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("ST01-04:ability:keyword-definition:c24a6b9d8de8435a", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("ST02-02:ability:keyword-definition:c24a6b9d8de8435a", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("ST04-01:ability:keyword-definition:c24a6b9d8de8435a", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    [L12AbilityEvidence("ST06-02:ability:keyword-definition:c24a6b9d8de8435a", "normal", "reconnect", "presentation-consumers", "leave-or-turn-expiry", "reconnect-state")]
    public void PrintedTauntDefinitionsUseOneCurrentRowStateForProjectionAndAttackRestriction()
    {
        var cardIds = new[] { "S02-0302", "S02-0503", "S02-0512", "S02-0615", "ST01-04", "ST02-02", "ST04-01", "ST06-02" };
        foreach (var cardId in cardIds)
        {
            Assert.True(L12StructuredCardRules.HasKeywordDefinition(cardId, "taunt"));
            var game = Create(72640 + cardId.Length);
            var taunt = Card(cardId, $"printed-taunt-{cardId}", 0);
            if (cardId == "S02-0503") taunt.TauntUntilTurn = game.State.TurnSerial;
            var attacker = Card("S01-0002", $"printed-taunt-attacker-{cardId}", 1);
            attacker.SummonRound = 0;
            game.State.Players[0].Field[0][0] = taunt;
            game.State.Players[1].Field[0][0] = attacker;
            game.State.ActivePlayer = 1;
            Assert.Contains("挑衅", ActiveKeywords(game, 0));

            game = Restore(game);
            taunt = game.State.Players[0].Field[0][0]!;
            attacker = game.State.Players[1].Field[0][0]!;
            Assert.True(L12StructuredCardRules.HasTaunt(taunt, 0));
            var result = game.Handle(1, new L12Command("attack", attacker.InstanceId,
                Target: new L12AttackTarget("master")));
            Assert.False(result.Accepted);
        }
    }

    private static string[] ActiveKeywords(L12GameEngine game, int playerIndex)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(playerIndex),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return snapshot.GetProperty("players")[playerIndex].GetProperty("field")[0][0]
            .GetProperty("activeKeywords").EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var guard = 0; guard < 20; guard++)
        {
            var prompt = game.State.PendingPrompts.FirstOrDefault(candidate => candidate.Kind == "response");
            if (prompt is null) return;
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        throw new InvalidOperationException("响应窗口未在限定次数内结束");
    }

    private static object? Invoke(L12GameEngine game, string methodName, params object?[] args)
        => typeof(L12GameEngine).GetMethod(methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(game, args);

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "printed-combat-keywords", "PRINTED-KEYWORDS", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
        }
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        return game;
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
            HasPrintedCost = definition.Cost is not null,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = ownerIndex,
            SummonRound = 0,
        };
    }
}
