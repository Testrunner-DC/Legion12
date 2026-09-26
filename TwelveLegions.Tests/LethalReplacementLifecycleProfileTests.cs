using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class LethalReplacementLifecycleProfileTests
{
    private const string HoremhebAbilityId = "S01-0205:ability:death:7016351513168cdb";
    private const string AchillesAbilityId = "S02-0504:ability:lethal-replacement:3fb565d50830f260";
    private const string HelenAbilityId = "S02-0515:ability:lethal-replacement:654c3040d6da8b4d";

    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "lethal-profile", "LETHAL", seed,
            ["甲", "乙"], [4, 4], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 1;
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
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
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
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }

    private static object? InvokePrivate(object target, string method, params object?[] args)
        => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, args);

    private static (L12CardInstance Protected, string Choice) ArrangeReplacement(
        L12GameEngine game, string cardId, string suffix)
    {
        var player = game.State.Players[0];
        var protectedCard = Card(cardId, $"protected-{suffix}");
        player.Field[0][0] = protectedCard;
        if (cardId == "S01-0205")
        {
            var guard = Card("S01-0212", $"guard-{suffix}");
            player.Field[1][0] = guard;
            return (protectedCard, guard.InstanceId);
        }
        if (cardId == "S02-0504")
        {
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"god-power-{suffix}",
                CardId = "S02-05C1",
                IsGodPower = true,
                Tapped = false,
            });
            return (protectedCard, "yes");
        }

        var substitute = Card("S02-0502", $"hand-substitute-{suffix}");
        player.Hand.Add(substitute);
        return (protectedCard, substitute.InstanceId);
    }

    private static L12Prompt BeginEffectLethalReplacement(L12GameEngine game, L12CardInstance protectedCard)
    {
        _ = InvokePrivate(game, "RemoveFromField", game.State.Players[0], protectedCard, true,
            "被生命周期测试效果击杀", true, L12FieldLeaveKind.Defeat, false, false);
        return Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "effect-lethal-replacement");
    }

    [Fact]
    public void HelenLethalEventIdIsStableWhenTheSameCommandRunsAfterCheckpointRestore()
    {
        var original = Create(92951);
        var (protectedCard, _) = ArrangeReplacement(original, "S02-0515", "stable-event");
        var checkpoint = original.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            original.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            original.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

        var originalPrompt = BeginEffectLethalReplacement(original, protectedCard);
        var restoredProtected = restored.State.Players[0].Field[0][0]!;
        var restoredPrompt = BeginEffectLethalReplacement(restored, restoredProtected);

        Assert.Equal(originalPrompt.PromptId, restoredPrompt.PromptId);
        Assert.Equal(originalPrompt.Data["lethalEventId"], restoredPrompt.Data["lethalEventId"]);
        Assert.Equal($"lethal-event:{originalPrompt.PromptId}:{protectedCard.InstanceId}",
            originalPrompt.Data["lethalEventId"]);
    }

    [Theory]
    [InlineData("S01-0205", HoremhebAbilityId)]
    [InlineData("S02-0504", AchillesAbilityId)]
    [InlineData("S02-0515", HelenAbilityId)]
    [L12AbilityEvidence(HoremhebAbilityId, "normal", "reconnect", "duplicate-submit", "presentation-consumers")]
    [L12AbilityEvidence(AchillesAbilityId, "normal", "reconnect", "duplicate-submit", "presentation-consumers")]
    [L12AbilityEvidence(HelenAbilityId, "normal", "reconnect", "duplicate-submit", "single-candidate-choice", "presentation-consumers")]
    public void EveryEffectLethalReplacementRestoresTheSamePromptAndConsumesItOnce(
        string cardId, string abilityId)
    {
        Assert.Contains(cardId, abilityId, StringComparison.Ordinal);
        var game = Create(93001 + cardId[^1]);
        var (protectedCard, choice) = ArrangeReplacement(game, cardId, cardId);
        var prompt = BeginEffectLethalReplacement(game, game.State.Players[0].Field[0][0]!);

        Assert.Contains("即将阵亡", prompt.Text, StringComparison.Ordinal);
        Assert.Equal(cardId, game.State.Players[0].Field[0][0]!.CardId);
        Assert.Single(game.SnapshotFor(0).Prompts);

        game = L12GameEngine.RestoreCheckpoint(Catalog,
            game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,"),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        var restoredPrompt = Assert.Single(game.State.PendingPrompts,
            candidate => candidate.PromptId == prompt.PromptId);
        Assert.Contains(choice, restoredPrompt.ValidChoices);

        var resolved = game.Handle(0,
            new L12Command("resolvePrompt", PromptId: restoredPrompt.PromptId, Choice: choice));
        Assert.True(resolved.Accepted, resolved.Error);
        Assert.Equal(cardId, game.State.Players[0].Field[0][0]!.CardId);
        Assert.Empty(game.State.PendingPrompts);
        var playerLog = Assert.Single(game.State.Events, entry => entry.Type == "replacement");
        Assert.Equal("触发 致命代替", playerLog.PlayerLogSemantic?.ActionLabel);
        Assert.Contains("未阵亡", playerLog.PlayerLogSemantic?.OutcomeLabel, StringComparison.Ordinal);
        Assert.Equal(protectedCard.InstanceId, playerLog.PlayerLogSemantic?.SourceInstanceId);
        Assert.Equal(protectedCard.InstanceId, playerLog.PlayerLogSemantic?.TargetInstanceId);

        var duplicate = game.Handle(0,
            new L12Command("resolvePrompt", PromptId: restoredPrompt.PromptId, Choice: choice));
        Assert.False(duplicate.Accepted);
    }

    [Fact]
    [Trait("L12Evidence", "bug:BUG-20260926-334ff582")]
    [Trait("L12Evidence", "prompt:battlefield-position-label")]
    public void HoremhebSameNameSubstitutesHaveStableBattlefieldPositionLabels()
    {
        var game = Create(93015);
        var player = game.State.Players[0];
        var horemheb = Card("S01-0205", "horemheb-position-label");
        var frontGuard = Card("S01-0212", "guard-front-position");
        var backGuard = Card("S01-0212", "guard-back-position");
        player.Field[0][0] = horemheb;
        player.Field[0][1] = frontGuard;
        player.Field[1][2] = backGuard;

        var prompt = BeginEffectLethalReplacement(game, horemheb);

        Assert.Equal("陵墓守卫 · 我方前排第2格", prompt.ChoiceLabels[frontGuard.InstanceId]);
        Assert.Equal("陵墓守卫 · 我方后排第3格", prompt.ChoiceLabels[backGuard.InstanceId]);
    }

    [Theory]
    [InlineData("S01-0205", HoremhebAbilityId)]
    [InlineData("S02-0515", HelenAbilityId)]
    [L12AbilityEvidence(HoremhebAbilityId, "target-invalidated")]
    [L12AbilityEvidence(HelenAbilityId, "target-invalidated", "payment-cancel")]
    public void DeclaredSubstituteThatLeavesItsRequiredZoneDoesNotProtectOrSubstituteAnotherCard(
        string cardId, string abilityId)
    {
        Assert.Contains(cardId, abilityId, StringComparison.Ordinal);
        var game = Create(93101 + cardId[^1]);
        var (protectedCard, choice) = ArrangeReplacement(game, cardId, $"invalid-{cardId}");
        var prompt = BeginEffectLethalReplacement(game, protectedCard);
        var player = game.State.Players[0];

        if (cardId == "S01-0205")
        {
            var guard = Assert.Single(player.Field.SelectMany(row => row),
                card => card?.InstanceId == choice)!;
            player.Field[1][0] = null;
            player.Graveyard.Add(guard);
        }
        else
        {
            var substitute = Assert.Single(player.Hand, card => card.InstanceId == choice);
            player.Hand.Remove(substitute);
            player.Library.Add(substitute);
        }

        var resolved = game.Handle(0,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(resolved.Accepted, resolved.Error);
        Assert.Null(player.Field[0][0]);
        Assert.Contains(player.Graveyard, card => card.InstanceId == protectedCard.InstanceId);
        Assert.Empty(game.State.PendingPrompts);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "replacement");
    }

    [Fact]
    [L12AbilityEvidence(AchillesAbilityId, "payment-cancel")]
    public void AchillesPaymentFailureEndsThePromptAndDoesNotPreserveTheCard()
    {
        var game = Create(93201);
        var (achilles, _) = ArrangeReplacement(game, "S02-0504", "payment-failure");
        var prompt = BeginEffectLethalReplacement(game, achilles);
        game.State.Players[0].Morale.Clear();

        var resolved = game.Handle(0,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "yes"));
        Assert.True(resolved.Accepted, resolved.Error);
        Assert.Null(game.State.Players[0].Field[0][0]);
        Assert.Contains(game.State.Players[0].Graveyard, card => card.InstanceId == achilles.InstanceId);
        Assert.Empty(game.State.PendingPrompts);
        Assert.DoesNotContain(game.State.Players[0].UsedAbilities,
            key => key.StartsWith("pending:s2-achilles-lethal-replacement:", StringComparison.Ordinal));
    }
}
