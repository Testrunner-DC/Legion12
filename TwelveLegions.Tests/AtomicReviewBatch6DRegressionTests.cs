using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6DRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "atomic-review-batch6d", "ATOMIC6D", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Prepare(game);
        return game;
    }

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
        var game = new L12GameEngine(Catalog, "atomic-review-batch6d", "ATOMIC6D", seed,
            ["甲", "乙"], [firstDeck, baseDeck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Prepare(game);
        return game;
    }

    private static void Prepare(L12GameEngine game)
    {
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
            player.Removed.Clear();
            player.Resolving.Clear();
            player.ExtraRelics.Clear();
            player.SpecialZones.GodPower.Clear();
            player.SpecialZones.Trials.Clear();
            player.SpecialZones.CanopicProgress.Clear();
            player.Relic = null;
            player.Morale.Clear();
        }
    }

    private static L12CardInstance Card(string cardId, string instanceId, int? owner = null)
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
            OwnerIndex = owner,
        };
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static L12Prompt ResolveMany(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [.. choices]));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 80 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
    }

    private static L12Prompt AdvanceCombatToChoice(L12GameEngine game)
    {
        for (var safety = 0; safety < 120; safety++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                if (prompt.Kind != "response") return prompt;
                Resolve(game, "pass");
                continue;
            }
            if (game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice } pending)
            {
                var result = game.Handle(1 - pending.AttackerPlayer,
                    new L12Command("resolveDefense", CardInstanceIds: []));
                Assert.True(result.Accepted, result.Error);
                continue;
            }
        }
        throw new Xunit.Sdk.XunitException("战斗时间线未进入预期公开选择");
    }

    private static int CountAuthoritativeInstances(L12GameEngine game, string instanceId)
        => game.State.Players.Sum(player =>
            player.Field.SelectMany(row => row).Count(card => card?.InstanceId == instanceId)
            + (player.Relic?.InstanceId == instanceId ? 1 : 0)
            + player.ExtraRelics.Count(card => card.InstanceId == instanceId)
            + player.Resolving.Count(card => card.InstanceId == instanceId)
            + player.Hand.Count(card => card.InstanceId == instanceId)
            + player.Library.Count(card => card.InstanceId == instanceId)
            + player.Graveyard.Count(card => card.InstanceId == instanceId)
            + player.Removed.Count(card => card.InstanceId == instanceId)
            + player.SpecialZones.GodPower.Count(card => card.InstanceId == instanceId)
            + player.SpecialZones.Trials.Count(card => card.InstanceId == instanceId)
            + player.SpecialZones.CanopicProgress.Count(card => card.InstanceId == instanceId));

    private static void PutInZone(L12PlayerState player, L12CardInstance card, string zone)
    {
        switch (zone)
        {
            case "field": player.Field[0][0] = card; break;
            case "relic": player.Relic = card; break;
            case "extra": player.ExtraRelics.Add(card); break;
            case "trial": player.SpecialZones.Trials.Add(card); break;
            case "canopic": player.SpecialZones.CanopicProgress.Add(card); break;
            case "resolving": player.Resolving.Add(card); break;
            case "hand": player.Hand.Add(card); break;
            case "library": player.Library.Add(card); break;
            case "graveyard": player.Graveyard.Add(card); break;
            case "removed": player.Removed.Add(card); break;
            default: throw new ArgumentOutOfRangeException(nameof(zone), zone, null);
        }
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0414")]
    [Trait("L12Evidence", "entry:last-known-source-independent-trigger")]
    public void KatsuraReturnCreatesAnIndependentMoraleTriggerWhoseNegationDoesNotUndoTheReturn()
    {
        var game = Create(7801);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var katsura = Card("S01-0414", "batch6d-katsura", 0);
        var target = Card("S01-0003", "batch6d-katsura-target", 1);
        target.Troops = 1000;
        player.Field[0][0] = katsura;
        opponent.Field[0][0] = target;
        var firstMorale = new L12MoraleCard { CardId = "S01-04C1", InstanceId = "batch6d-katsura-morale-1", Tapped = true };
        var secondMorale = new L12MoraleCard { CardId = "S01-04C1", InstanceId = "batch6d-katsura-morale-2", Tapped = true };
        player.Morale.AddRange([firstMorale, secondMorale]);

        Assert.True(game.Handle(0, new L12Command("attack", katsura.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);

        var returnDeclaration = AdvanceCombatToChoice(game);
        Assert.Equal("pending-activation", returnDeclaration.Continuation);
        Assert.Contains("mode:use", returnDeclaration.ValidChoices);
        Assert.Contains(katsura, player.Field[0]);
        Resolve(game, "mode:use");
        PassResponses(game);

        Assert.Same(katsura, player.Library[0]);
        Assert.Equal(1, CountAuthoritativeInstances(game, katsura.InstanceId));
        var moraleDeclaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", moraleDeclaration.Continuation);
        Assert.Contains(firstMorale.InstanceId, moraleDeclaration.ValidChoices);
        Assert.Contains(secondMorale.InstanceId, moraleDeclaration.ValidChoices);
        Assert.DoesNotContain(game.State.EffectStack,
            item => item.SourceCardId == "S01-0414" && item.Trigger == "return-library-top");

        ResolveMany(game, secondMorale.InstanceId);
        var moraleEffect = Assert.Single(game.State.EffectStack,
            item => item.SourceCardId == "S01-0414" && item.Trigger == "return-library-top");
        moraleEffect.Negated = true;
        PassResponses(game);

        Assert.True(firstMorale.Tapped);
        Assert.True(secondMorale.Tapped);
        Assert.Same(katsura, player.Library[0]);
        Assert.Equal(1, CountAuthoritativeInstances(game, katsura.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0414")]
    [Trait("L12Evidence", "entry:independent-target-invalidation")]
    public void KatsuraMoraleTargetsInvalidateIndependentlyAfterDeclaration()
    {
        var game = Create(7805);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var katsura = Card("S01-0414", "batch6d-katsura-partial", 0);
        var target = Card("S01-0003", "batch6d-katsura-partial-target", 1);
        player.Field[0][0] = katsura;
        opponent.Field[0][0] = target;
        var invalidated = new L12MoraleCard
        {
            CardId = "S01-04C1", InstanceId = "batch6d-katsura-invalidated", Tapped = true,
        };
        var stillLegal = new L12MoraleCard
        {
            CardId = "S01-04C1", InstanceId = "batch6d-katsura-still-legal", Tapped = true,
        };
        player.Morale.AddRange([invalidated, stillLegal]);

        Assert.True(game.Handle(0, new L12Command("attack", katsura.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        var returnDeclaration = AdvanceCombatToChoice(game);
        Assert.Equal("pending-activation", returnDeclaration.Continuation);
        Resolve(game, "mode:use");
        PassResponses(game);

        var moraleDeclaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", moraleDeclaration.Continuation);
        ResolveMany(game, invalidated.InstanceId, stillLegal.InstanceId);
        invalidated.Tapped = false;
        PassResponses(game);

        Assert.False(invalidated.Tapped);
        Assert.False(stillLegal.Tapped);
        Assert.Same(katsura, player.Library[0]);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("仅取消该目标", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0204")]
    [Trait("L12Evidence", "entry:cross-control-owner-snapshot")]
    public void TombConstructIndependentDeathAndLeaveTriggersUseTheOwnerSnapshotWithoutDuplicatingGuards()
    {
        var game = Create(7802);
        var owner = game.State.Players[0];
        var controller = game.State.Players[1];
        var construct = Card("S01-0204", "batch6d-construct", 0);
        var guard = Card("S01-0212", "batch6d-guard", 0);
        guard.OwnerIndex = null;
        construct.AttachedCards.Add(guard);
        controller.Field[0][0] = construct;

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 1,
            CardInstanceId: construct.InstanceId)).Accepted);
        var order = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Continuation == "trigger-batch-order");
        Assert.Equal(2, order.ValidChoices.Count);
        Assert.Contains(construct, owner.Graveyard);
        Assert.Contains(guard, owner.Graveyard);

        owner.Graveyard.Remove(construct);
        owner.Library.Add(construct);
        for (var row = 0; row < 2; row++)
        for (var slot = 0; slot < 3; slot++)
            controller.Field[row][slot] = Card("S01-0102", $"batch6d-block-{row}-{slot}", 1);

        ResolveMany(game, [.. order.ValidChoices]);
        var firstSlot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", firstSlot.Continuation);
        Assert.Contains("0:0", firstSlot.ValidChoices);
        Resolve(game, "0:0");
        var firstEffect = Assert.Single(game.State.EffectStack,
            item => item.SourceCardId == "S01-0204");
        Assert.NotNull(firstEffect.SourceSnapshot);
        firstEffect.Negated = true;
        PassResponses(game);

        var secondSlot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", secondSlot.Continuation);
        Assert.Contains("0:1", secondSlot.ValidChoices);
        Resolve(game, "0:1");
        PassResponses(game);

        Assert.Same(guard, owner.Field[0][1]);
        Assert.DoesNotContain(guard, owner.Graveyard);
        Assert.DoesNotContain(controller.Field.SelectMany(row => row), card => card?.InstanceId == guard.InstanceId);
        Assert.Equal(1, CountAuthoritativeInstances(game, guard.InstanceId));
        Assert.Same(construct, owner.Library[0]);
        Assert.Equal(1, CountAuthoritativeInstances(game, construct.InstanceId));
    }

    [Theory]
    [InlineData("field")]
    [InlineData("relic")]
    [InlineData("extra")]
    [InlineData("trial")]
    [InlineData("canopic")]
    [InlineData("resolving")]
    [InlineData("hand")]
    [InlineData("library")]
    [InlineData("graveyard")]
    [InlineData("removed")]
    [Trait("L12Evidence", "card:S01-0417")]
    [Trait("L12Evidence", "entry:authoritative-owner-instance-transfer")]
    public void KusanagiReturnMovesTheRealInstanceFromEveryAuthoritativeZoneWithoutDuplicates(string zone)
    {
        var game = CreateWithFirstMaster("S01-04M2", 7803);
        var player = game.State.Players[0];
        var sword = Card("S01-0417", $"batch6d-kusanagi-{zone}", 0);
        player.Relic = sword;

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: sword.InstanceId)).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains("mode:use", declaration.ValidChoices);
        Assert.True(player.Graveyard.Remove(sword));
        PutInZone(player, sword, zone);

        Resolve(game, "mode:use");
        var effect = Assert.Single(game.State.EffectStack, item => item.SourceCardId == "S01-0417");
        Assert.NotNull(effect.SourceSnapshot);
        PassResponses(game);

        Assert.Same(sword, player.Library[0]);
        Assert.Null(player.Relic);
        Assert.Equal(1, CountAuthoritativeInstances(game, sword.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "entry:snapshot-is-not-a-zone-card")]
    public void MissingRealKusanagiSourceNeverInsertsTheLastKnownSnapshotIntoTheLibrary()
    {
        var game = CreateWithFirstMaster("S01-04M2", 7804);
        var player = game.State.Players[0];
        var sword = Card("S01-0417", "batch6d-kusanagi-missing", 0);
        player.Relic = sword;

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: sword.InstanceId)).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.True(player.Graveyard.Remove(sword));
        Assert.Equal(0, CountAuthoritativeInstances(game, sword.InstanceId));

        Resolve(game, "mode:use");
        PassResponses(game);

        Assert.DoesNotContain(player.Library, card => card.InstanceId == sword.InstanceId);
        Assert.Equal(0, CountAuthoritativeInstances(game, sword.InstanceId));
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("真实实例", StringComparison.Ordinal));
    }
}
