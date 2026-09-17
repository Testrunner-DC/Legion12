using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed partial class CavalryMoveRuleActionTests
{
    private static L12GameEngine Restore(L12GameEngine game) => L12GameEngine.RestoreCheckpoint(
        Catalog, game.SerializeFullState(), game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
        game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static L12AtomicAbility NativeAbility(string cardId) => Catalog.AtomicEffects.Find(cardId)!.Abilities
        .Single(ability => ability.ExecutionModel == "rule-action");

    [Theory]
    [InlineData("S01-0310")]
    [InlineData("S01-0409")]
    [InlineData("S02-0505")]
    [InlineData("ST01-01")]
    [InlineData("ST06-04")]
    [L12AbilityEvidence("S01-0310:ability:active:0a0575206e996652", "normal", "presentation-event", "button-text", "no-resource-cost", "reconnect-before-command", "reconnect-after-command", "duplicate-submit")]
    [L12AbilityEvidence("S01-0409:ability:active:56a01edf47ee1225", "normal", "presentation-event", "button-text", "no-resource-cost", "reconnect-before-command", "reconnect-after-command", "duplicate-submit")]
    [L12AbilityEvidence("S02-0505:ability:active:bac4cb5d348f29f1", "normal", "presentation-event", "button-text", "no-resource-cost", "reconnect-before-command", "reconnect-after-command", "duplicate-submit")]
    [L12AbilityEvidence("ST01-01:ability:active:69626894e55e27e5", "normal", "presentation-event", "button-text", "no-resource-cost", "reconnect-before-command", "reconnect-after-command", "duplicate-submit")]
    [L12AbilityEvidence("ST06-04:ability:active:719cc1c7c1084fa0", "normal", "presentation-event", "button-text", "no-resource-cost", "reconnect-before-command", "reconnect-after-command", "duplicate-submit")]
    public void NativeMovementUsesExactAbilitySceneAndRejectsRepeatAfterV2Recovery(string cardId)
    {
        var game = Create(91721);
        var source = Card(cardId, "native-move");
        game.State.Players[0].Field[0][0] = source;
        var ability = NativeAbility(cardId);
        var scene = Assert.Single(ability.Presentations);
        var action = Assert.Single(FieldCard(game, 0, 0).GetProperty("ruleActions").EnumerateArray());
        Assert.Equal(ability.Text, action.GetProperty("text").GetString());
        Assert.True(action.GetProperty("enabled").GetBoolean());
        Assert.Contains("1:2", action.GetProperty("targetKeys").EnumerateArray().Select(value => value.GetString()));
        game = Restore(game); // Recovery before a destination is submitted is not a reservation.
        var moraleCount = game.State.Players[0].Morale.Count;
        var handCount = game.State.Players[0].Hand.Count;
        Assert.True(game.Handle(0, new L12Command("cavalryMove", source.InstanceId, Row: 1, Slot: 2)).Accepted);
        var move = Assert.Single(game.State.Events, entry => entry.Type == "move");
        Assert.Equal(scene.SceneId, move.EffectSceneId);
        Assert.Equal(ability.Text, move.EffectText);
        Assert.Equal(source.InstanceId, Assert.Single(move.Cards).InstanceId);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Equal(moraleCount, game.State.Players[0].Morale.Count);
        Assert.Equal(handCount, game.State.Players[0].Hand.Count);
        Assert.False(game.State.Players[0].Field[1][2]!.Tapped);
        game = Restore(game);
        var beforeDuplicate = game.SerializeFullState();
        Assert.False(game.Handle(0, new L12Command("cavalryMove", source.InstanceId, Row: 0, Slot: 1)).Accepted);
        Assert.Equal(beforeDuplicate, game.SerializeFullState());
        action = Assert.Single(FieldCard(game, 1, 2).GetProperty("ruleActions").EnumerateArray());
        Assert.False(action.GetProperty("enabled").GetBoolean());
        Assert.Contains("本回合已经进行过", action.GetProperty("disabledReason").GetString());
    }

    [Theory]
    [InlineData("S01-0310")]
    [InlineData("S01-0409")]
    [InlineData("S02-0505")]
    [InlineData("ST01-01")]
    [InlineData("ST06-04")]
    [L12AbilityEvidence("S01-0310:ability:active:0a0575206e996652", "source-invalidated", "destination-invalidated", "reconnect-before-command")]
    [L12AbilityEvidence("S01-0409:ability:active:56a01edf47ee1225", "source-invalidated", "destination-invalidated", "reconnect-before-command")]
    [L12AbilityEvidence("S02-0505:ability:active:bac4cb5d348f29f1", "source-invalidated", "destination-invalidated", "reconnect-before-command")]
    [L12AbilityEvidence("ST01-01:ability:active:69626894e55e27e5", "source-invalidated", "destination-invalidated", "reconnect-before-command")]
    [L12AbilityEvidence("ST06-04:ability:active:719cc1c7c1084fa0", "source-invalidated", "destination-invalidated", "reconnect-before-command")]
    public void NativeMovementRevalidatesPublishedDestinationAndSourceAfterRecovery(string cardId)
    {
        foreach (var invalidation in new[] { "occupied", "disaster", "tapped", "hidden", "profession", "left-field", "non-legion" })
        {
            var game = Create(91722);
            var source = Card(cardId, "stale-source");
            var player = game.State.Players[0];
            player.Field[0][0] = source;
            var action = Assert.Single(FieldCard(game, 0, 0).GetProperty("ruleActions").EnumerateArray());
            Assert.True(action.GetProperty("enabled").GetBoolean());
            Assert.Contains("1:2", action.GetProperty("targetKeys").EnumerateArray().Select(value => value.GetString()));
            switch (invalidation)
            {
                case "occupied": player.Field[1][2] = Card("S01-0001", "occupant"); break;
                case "disaster": game.State.ActiveDisaster = Card("S01-DS03", "corrupt-land"); break;
                case "tapped": source.Tapped = true; break;
                case "hidden": source.Hidden = true; break;
                // Minimal malformed/stale authority fixtures, not a claim of a real transformation chain.
                case "profession": player.Field[0][0] = Card(cardId, source.InstanceId, profession: "斗士"); break;
                case "left-field": player.Field[0][0] = null; player.Graveyard.Add(source); break;
                case "non-legion": player.Field[0][0] = Card(cardId, source.InstanceId, cardType: "artifact"); break;
            }
            game = Restore(game);
            var before = game.SerializeFullState();
            var result = game.Handle(0, new L12Command("cavalryMove", source.InstanceId, Row: 1, Slot: 2));
            Assert.False(result.Accepted, $"{cardId}/{invalidation} must reject the stale selection");
            Assert.Equal(before, game.SerializeFullState());
            Assert.Empty(game.State.PendingPrompts);
            Assert.Empty(game.State.EffectStack);
        }
    }

    [Theory]
    [InlineData("S01-0310")]
    [InlineData("S01-0409")]
    [InlineData("S02-0505")]
    [InlineData("ST01-01")]
    [InlineData("ST06-04")]
    [L12AbilityEvidence("S01-0310:ability:active:0a0575206e996652", "timing", "button-rejection-consistency")]
    [L12AbilityEvidence("S01-0409:ability:active:56a01edf47ee1225", "timing", "button-rejection-consistency")]
    [L12AbilityEvidence("S02-0505:ability:active:bac4cb5d348f29f1", "timing", "button-rejection-consistency")]
    [L12AbilityEvidence("ST01-01:ability:active:69626894e55e27e5", "timing", "button-rejection-consistency")]
    [L12AbilityEvidence("ST06-04:ability:active:719cc1c7c1084fa0", "timing", "button-rejection-consistency")]
    public void NativeMovementTimingRejectionMatchesTheDisabledButton(string cardId)
    {
        foreach (var wrongTiming in new[] { "opponent-turn", "wrong-phase" })
        {
            var game = Create(91723);
            var source = Card(cardId, "timing-source");
            game.State.Players[0].Field[0][0] = source;
            if (wrongTiming == "opponent-turn") game.State.ActivePlayer = 1;
            else game.State.Phase = L12Phase.Draw;
            var action = Assert.Single(FieldCard(game, 0, 0).GetProperty("ruleActions").EnumerateArray());
            Assert.False(action.GetProperty("enabled").GetBoolean());
            var before = game.SerializeFullState();
            var result = game.Handle(0, new L12Command("cavalryMove", source.InstanceId, Row: 1, Slot: 2));
            Assert.False(result.Accepted);
            Assert.Equal(action.GetProperty("disabledReason").GetString(), result.Error);
            Assert.Equal(before, game.SerializeFullState());
        }
    }

    [Theory]
    [InlineData("S01-0310")]
    [InlineData("S01-0409")]
    [InlineData("S02-0505")]
    [InlineData("ST01-01")]
    [InlineData("ST06-04")]
    [L12AbilityEvidence("S01-0310:ability:active:0a0575206e996652", "single-candidate-choice", "no-target", "missing-choice", "no-payment-before-choice")]
    [L12AbilityEvidence("S01-0409:ability:active:56a01edf47ee1225", "single-candidate-choice", "no-target", "missing-choice", "no-payment-before-choice")]
    [L12AbilityEvidence("S02-0505:ability:active:bac4cb5d348f29f1", "single-candidate-choice", "no-target", "missing-choice", "no-payment-before-choice")]
    [L12AbilityEvidence("ST01-01:ability:active:69626894e55e27e5", "single-candidate-choice", "no-target", "missing-choice", "no-payment-before-choice")]
    [L12AbilityEvidence("ST06-04:ability:active:719cc1c7c1084fa0", "single-candidate-choice", "no-target", "missing-choice", "no-payment-before-choice")]
    public void NativeMovementRequiresManualDestinationEvenWhenOnlyOneIsLegal(string cardId)
    {
        var game = Create(91724);
        var source = Card(cardId, "one-destination");
        var player = game.State.Players[0];
        player.Field[0][0] = source;
        for (var row = 0; row < 2; row++)
        for (var slot = 0; slot < 3; slot++)
            if ((row, slot) is not (0, 0) and not (1, 2))
                player.Field[row][slot] = Card("S01-0001", $"block-{row}-{slot}");
        var action = Assert.Single(FieldCard(game, 0, 0).GetProperty("ruleActions").EnumerateArray());
        Assert.Equal("1:2", Assert.Single(action.GetProperty("targetKeys").EnumerateArray()).GetString());
        Assert.True(action.GetProperty("enabled").GetBoolean());
        Assert.Same(source, player.Field[0][0]);
        var before = game.SerializeFullState();
        Assert.False(game.Handle(0, new L12Command("cavalryMove", source.InstanceId)).Accepted);
        Assert.Equal(before, game.SerializeFullState());
        player.Field[1][2] = Card("S01-0001", "last-slot-filled");
        action = Assert.Single(FieldCard(game, 0, 0).GetProperty("ruleActions").EnumerateArray());
        Assert.False(action.GetProperty("enabled").GetBoolean());
        Assert.Empty(action.GetProperty("targetKeys").EnumerateArray());
        Assert.False(game.Handle(0, new L12Command("cavalryMove", source.InstanceId, Row: 1, Slot: 2)).Accepted);
        Assert.Equal(-1, source.LastCavalryMoveTurn);
        player.Field[1][2] = null;
        Assert.True(game.Handle(0, new L12Command("cavalryMove", source.InstanceId, Row: 1, Slot: 2)).Accepted);
    }
}
