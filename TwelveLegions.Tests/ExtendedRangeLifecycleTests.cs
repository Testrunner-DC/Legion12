using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ExtendedRangeLifecycleTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private const string SourceId = "range-source";
    private static L12CardInstance Card(string id, string instance, string? type = null)
    {
        var definition = Catalog.Cards[id];
        return new() { InstanceId = instance, CardId = id, Name = definition.NameZh,
            CardType = type ?? definition.CardType, Faction = definition.Faction,
            Profession = definition.Profession, EffectText = definition.Effect, Cost = definition.Cost ?? 0,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0, SummonRound = -1 };
    }

    private static L12GameEngine Create(string id, int morale = 6)
    {
        var game = new L12GameEngine(Catalog, "extended-range", "RANGE", 91741, ["甲", "乙"], [0, 0],
            skipPreparation: true, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0; game.State.Round = 3; game.State.TurnSerial = 5; game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear(); player.Morale.Clear(); player.UsedAbilities.Clear();
        }
        game.State.Players[0].Field[1][0] = Card(id, SourceId);
        game.State.Players[1].Field[1][0] = Card("ST01-05", "back-target");
        for (var index = 0; index < morale; index++)
            game.State.Players[0].Morale.Add(new() { InstanceId = $"morale-{index}", CardId = "S01-01C1" });
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game) => L12GameEngine.RestoreCheckpoint(Catalog,
        game.SerializeFullState(), game.RandomState!.Value, game.CardFactSignalSequence,
        autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static void ActivateAndChoosePayment(L12GameEngine game)
    {
        var result = game.Handle(0, new L12Command("activateAbility", SourceId, Ability: "extendedRange"));
        Assert.True(result.Accepted, result.Error);
        if (game.State.PendingPrompts.FirstOrDefault() is { Kind: "resource-return" } payment)
        {
            result = game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId, Choice: payment.ValidChoices[0]));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.NotEmpty(game.State.EffectStack);
    }

    private static void Resolve(L12GameEngine game)
    {
        for (var count = 0; count < 20 && game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt; count++)
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Theory]
    [InlineData("S01-0003", true)]
    [InlineData("S01-0113", false)]
    [L12AbilityEvidence("S01-0003:ability:active:73c59f9367069790", "normal", "legal-targets", "authoritative-attack", "reconnect-settlement", "presentation-consumers")]
    [L12AbilityEvidence("S01-0113:ability:active:e1b5cdab435b4c1f", "normal", "legal-targets", "authoritative-attack", "reconnect-settlement", "presentation-consumers")]
    public void PaidRangeUsesOnlyItsPrintedTargetsAfterV2Recovery(string id, bool master)
    {
        var game = Create(id);
        ActivateAndChoosePayment(game);
        game = Restore(game);
        Resolve(game);
        var abilityId = id == "S01-0003"
            ? "S01-0003:ability:active:73c59f9367069790"
            : "S01-0113:ability:active:e1b5cdab435b4c1f";
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectAbilityId == abilityId && entry.EffectResultStatus == "resolved");
        Assert.False(string.IsNullOrWhiteSpace(result.EffectSceneId));
        Assert.False(string.IsNullOrWhiteSpace(result.EffectText));
        Assert.All(new[] { game.SnapshotFor(0), game.SnapshotFor(1) }, snapshot =>
            Assert.Contains(snapshot.RecentEvents, entry => entry.EffectAbilityId == abilityId
                && entry.EffectSceneId == result.EffectSceneId && entry.EffectText == result.EffectText));
        game = Restore(game);
        var targets = game.SnapshotFor(0).LegalAttackTargets[SourceId];
        Assert.Contains("back-target", targets);
        Assert.Equal(master, targets.Contains("master"));
        var masterAttempt = Restore(game).Handle(0, new L12Command("attack", SourceId, Target: new L12AttackTarget("master")));
        Assert.Equal(master, masterAttempt.Accepted);
        Assert.True(game.Handle(0, new L12Command("attack", SourceId, Target: new L12AttackTarget("legion", "back-target"))).Accepted);
    }

    [Theory]
    [InlineData("S01-0003", 4)]
    [InlineData("S01-0113", 2)]
    [L12AbilityEvidence("S01-0003:ability:active:73c59f9367069790", "repeat-activation", "payment-per-activation", "legacy-once-marker", "button-enabled")]
    [L12AbilityEvidence("S01-0113:ability:active:e1b5cdab435b4c1f", "repeat-activation", "payment-per-activation", "legacy-once-marker", "button-enabled")]
    public void ASecondActivationPaysAgainAndIgnoresLegacyOnceMarkers(string id, int spent)
    {
        var game = Create(id);
        ActivateAndChoosePayment(game); Resolve(game);
        game.State.Players[0].UsedAbilities.Add($"active:{SourceId}:extendedRange"); // Older checkpoints may contain this invalid restriction.
        game = Restore(game);
        ActivateAndChoosePayment(game); Resolve(game);
        var player = game.State.Players[0];
        Assert.Equal(spent, id == "S01-0003" ? player.Morale.Count(card => card.Tapped) : 6 - player.Morale.Count);
        var view = JsonSerializer.SerializeToElement(game.SnapshotFor(0), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .GetProperty("players")[0].GetProperty("field")[1][0].GetProperty("abilities").EnumerateArray()
            .Single(ability => ability.GetProperty("id").GetString() == "extendedRange");
        Assert.True(view.GetProperty("enabled").GetBoolean());
    }

    [Theory]
    [InlineData("S01-0003")]
    [InlineData("S01-0113")]
    [L12AbilityEvidence("S01-0003:ability:active:73c59f9367069790", "negated-settlement", "paid-cost-preserved", "reconnect-settlement")]
    [L12AbilityEvidence("S01-0113:ability:active:e1b5cdab435b4c1f", "negated-settlement", "paid-cost-preserved", "reconnect-settlement")]
    public void NegationPreservesPaymentButDoesNotGrantRange(string id)
    {
        var game = Create(id);
        ActivateAndChoosePayment(game);
        Assert.Single(game.State.EffectStack).Negated = true;
        game = Restore(game); Resolve(game);
        Assert.False(game.SnapshotFor(0).LegalAttackTargets.TryGetValue(SourceId, out var targets) && targets.Contains("back-target"));
        Assert.Equal(id == "S01-0003" ? 2 : 1,
            id == "S01-0003" ? game.State.Players[0].Morale.Count(card => card.Tapped) : 6 - game.State.Players[0].Morale.Count);
        Assert.Contains(game.State.Events, action => action.Type == "effect-result" && action.EffectResultStatus == "negated");
    }

    [Theory]
    [InlineData("S01-0003", false)]
    [InlineData("S01-0003", true)]
    [InlineData("S01-0113", false)]
    [InlineData("S01-0113", true)]
    [L12AbilityEvidence("S01-0003:ability:active:73c59f9367069790", "source-invalidated-settlement", "non-legion-state-fixture", "reconnect-settlement")]
    [L12AbilityEvidence("S01-0113:ability:active:e1b5cdab435b4c1f", "source-invalidated-settlement", "non-legion-state-fixture", "reconnect-settlement")]
    public void MissingOrNonLegionSourceFailsInsteadOfGrantingOutOfZone(string id, bool nonLegion)
    {
        var game = Create(id);
        ActivateAndChoosePayment(game);
        var player = game.State.Players[0];
        var source = player.Field[1][0]!;
        player.Field[1][0] = nonLegion ? Card(id, SourceId, "relic") : null;
        if (!nonLegion) player.Graveyard.Add(source);
        game = Restore(game); Resolve(game);
        Assert.Contains(game.State.Events, action => action.Type == "effect-result" && action.EffectResultStatus == "failed");
        var current = nonLegion ? game.State.Players[0].Field[1][0]! : game.State.Players[0].Graveyard.Single(card => card.InstanceId == SourceId);
        Assert.NotEqual(game.State.TurnSerial, current.CanAttackBackAndMasterUntilTurn);
    }

    [Theory]
    [InlineData("S01-0003", "消耗2士气")]
    [InlineData("S01-0113", "返还1士气")]
    [L12AbilityEvidence("S01-0003:ability:active:73c59f9367069790", "cost-scope", "presentation-identity")]
    [L12AbilityEvidence("S01-0113:ability:active:e1b5cdab435b4c1f", "cost-scope", "presentation-identity")]
    public void PaidRangeHasItsOwnActiveCostAndSingleResultScene(string id, string cost)
    {
        var ability = Assert.Single(Catalog.AtomicEffects.Find(id)!.Abilities, ability => ability.Trigger == "active");
        Assert.Contains(cost, ability.CostText);
        Assert.DoesNotContain("进攻距离", ability.CostText);
        Assert.Equal("single-active", Assert.Single(ability.Presentations).Flow);
    }

    [Theory]
    [InlineData("S01-0003")]
    [InlineData("S01-0113")]
    [L12AbilityEvidence("S01-0003:ability:active:73c59f9367069790", "no-enemy", "turn-end-expiry")]
    [L12AbilityEvidence("S01-0113:ability:active:e1b5cdab435b4c1f", "no-enemy", "turn-end-expiry")]
    public void NoEnemyIsNotAnActivationCostAndRangeExpiresAtTurnEnd(string id)
    {
        var game = Create(id);
        Array.Clear(game.State.Players[1].Field[1]);
        ActivateAndChoosePayment(game); Resolve(game);
        Assert.Equal(game.State.TurnSerial, game.State.Players[0].Field[1][0]!.CanAttackBackUntilTurn);
        var ended = game.Handle(0, new L12Command("endTurn"));
        Assert.True(ended.Accepted, ended.Error);
        Assert.Null(game.State.Players[0].Field[1][0]!.CanAttackBackUntilTurn);
        Assert.Equal(-1, game.State.Players[0].Field[1][0]!.CanAttackBackAndMasterUntilTurn);
    }

    [Theory]
    [InlineData("S01-0003")]
    [InlineData("S01-0113")]
    [L12AbilityEvidence("S01-0003:ability:active:73c59f9367069790", "activation-row", "insufficient-cost")]
    [L12AbilityEvidence("S01-0113:ability:active:e1b5cdab435b4c1f", "activation-row", "insufficient-cost")]
    public void SourceRowIsCheckedBeforePaymentAndInsufficientCostDoesNotLockTheGame(string id)
    {
        var game = Create(id, 0);
        Assert.False(game.Handle(0, new L12Command("activateAbility", SourceId, Ability: "extendedRange")).Accepted);
        Assert.Empty(game.State.PendingPrompts); Assert.Empty(game.State.EffectStack);
        game = Create(id);
        var player = game.State.Players[0];
        player.Field[0][0] = player.Field[1][0]; player.Field[1][0] = null;
        var rejected = game.Handle(0, new L12Command("activateAbility", SourceId, Ability: "extendedRange"));
        Assert.False(rejected.Accepted);
        Assert.Contains("后排", rejected.Error);
        Assert.Equal(6, player.Morale.Count); Assert.All(player.Morale, card => Assert.False(card.Tapped));
        Assert.Empty(game.State.PendingPrompts); Assert.Empty(game.State.EffectStack);
    }

    [Theory]
    [InlineData("S01-0003", "resource-payment")]
    [InlineData("S01-0113", "resource-return")]
    [L12AbilityEvidence("S01-0003:ability:active:73c59f9367069790", "invalid-payment", "payment-cancel", "duplicate-cancel", "duplicate-submit", "reconnect-payment")]
    [L12AbilityEvidence("S01-0113:ability:active:e1b5cdab435b4c1f", "invalid-payment", "payment-cancel", "duplicate-cancel", "duplicate-submit", "reconnect-payment")]
    public void InvalidPaymentChoiceCanBeCancelledAfterRecoveryWithoutPaymentOrDeadlock(string id, string kind)
    {
        var game = Create(id);
        game.State.Players[0].Morale[0].Tapped = true;
        game.State.Players[0].TemporaryMorale = 1;
        Assert.True(game.Handle(0, new L12Command("activateAbility", SourceId, Ability: "extendedRange")).Accepted);
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(kind, payment.Kind);
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId, Choice: "invalid-resource")).Accepted);
        game = Restore(game);
        payment = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("cancel", payment.ValidChoices);
        var cancel = new L12Command("resolvePrompt", PromptId: payment.PromptId, Choice: "cancel");
        Assert.True(game.Handle(0, cancel).Accepted);
        Assert.Equal(6, game.State.Players[0].Morale.Count);
        Assert.Equal(1, game.State.Players[0].TemporaryMorale);
        Assert.Empty(game.State.PendingPrompts); Assert.Empty(game.State.PendingActivations); Assert.Empty(game.State.EffectStack);
        var state = game.SerializeFullState();
        Assert.False(game.Handle(0, cancel).Accepted);
        Assert.Equal(state, game.SerializeFullState());
        Assert.True(game.Handle(0, new L12Command("endTurn")).Accepted);
    }

    [Fact]
    public void BackOnlyPermissionClonesAndPromotesWithoutChangingUnusedCheckpointShape()
    {
        var foundation = Card("S02-0508", "foundation");
        Assert.DoesNotContain("CanAttackBackUntilTurn", JsonSerializer.Serialize(foundation));
        foundation.CanAttackBackUntilTurn = 7;
        Assert.Equal(7, foundation.Clone().CanAttackBackUntilTurn);
        var promoted = Card("S02-0507", "promoted");
        L12S2ZoneOps.InheritPromotionState(foundation, promoted);
        Assert.Equal(7, promoted.CanAttackBackUntilTurn);
        Assert.Equal(-1, promoted.CanAttackBackAndMasterUntilTurn);
    }

    [Fact]
    public void EffectStageReturnDoesNotInheritTheActivationCostCancellation()
    {
        var game = Create("S01-0113");
        typeof(L12GameEngine).GetMethod("CreateReturnMoralePrompt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(game, [0, 1, "card-effect", null, new Dictionary<string, string>(), false]);
        var effectChoice = Assert.Single(game.State.PendingPrompts);
        Assert.DoesNotContain("cancel", effectChoice.ValidChoices);
        var before = game.SerializeFullState();
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: effectChoice.PromptId, Choice: "cancel")).Accepted);
        Assert.Equal(before, game.SerializeFullState());
    }

    [Fact]
    public void BackOnlyPermissionIsRemovedByTheSharedLeaveCleanup()
    {
        var source = Card("S01-0113", SourceId);
        source.CanAttackBackUntilTurn = 5;
        typeof(L12GameEngine).GetMethod("ResetCardAfterLeavingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [source]);
        Assert.Null(source.CanAttackBackUntilTurn);
    }
}
