using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6IBRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static readonly (string CardId, string Trigger)[] ReviewedTriggers =
    [
        ("S01-0001", "death"), ("S01-0112", "death"), ("S01-0115", "death"),
        ("S01-0207", "death"), ("S01-0210", "death"), ("S01-0303", "death"),
        ("S01-0304", "death"), ("S01-0306", "death"), ("S01-0307", "death"), ("S01-0313", "death"),
        ("S01-0403", "death"), ("S01-0407", "death"), ("S02-0002", "after-kill"),
        ("S02-01S1", "death"), ("S02-0301", "death"), ("S02-0508", "death"),
        ("S02-0518", "death"), ("S02-0601", "death"), ("S02-0615", "death"),
    ];

    private static readonly string[] ResolutionModeCards =
    [
        "S01-0001", "S01-0303", "S01-0306", "S02-0002", "S02-01S1", "S02-0301", "S02-0601",
    ];

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "atomic-review-batch6ib", "ATOMIC6IB", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Library.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int? troops = null, int? cost = null)
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
            Cost = cost ?? definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            SummonRound = -1,
            OwnerIndex = 0,
        };
    }

    private static object? Invoke(object target, string method, params object?[] args)
    {
        var candidate = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(info => info.Name == method && info.GetParameters().Length == args.Length);
        return candidate.Invoke(target, args);
    }

    private static L12Prompt OnlyPrompt(L12GameEngine game) => Assert.Single(game.State.PendingPrompts);

    private static void ResolveChoice(L12GameEngine game, string choice)
    {
        var prompt = OnlyPrompt(game);
        Assert.True(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
    }

    private static void ResolveCards(L12GameEngine game, params string[] choices)
    {
        var prompt = OnlyPrompt(game);
        Assert.True(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: choices.ToList())).Accepted);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            ResolveChoice(game, "pass");
    }

    private static (L12CardInstance Source, Dictionary<string, L12CardInstance> Cards,
        Dictionary<string, L12MoraleCard> Morale) QueueReviewedTrigger(L12GameEngine game,
        string cardId, string trigger, bool withLegalChoices = true, string cause = "effect")
    {
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var source = Card(cardId, $"batch6ib-source-{cardId}-{trigger}");
        var cards = new Dictionary<string, L12CardInstance>();
        var morale = new Dictionary<string, L12MoraleCard>();
        if (trigger is "after-attack" or "after-kill")
        {
            if (cardId == "S02-0002") source.Tapped = true;
            player.Field[0][0] = source;
        }
        else
            player.Resolving.Add(source);

        player.Library.AddRange([
            Card("S01-0002", $"batch6ib-draw-a-{cardId}"),
            Card("S01-0003", $"batch6ib-draw-b-{cardId}"),
            Card("S01-0004", $"batch6ib-draw-c-{cardId}"),
        ]);

        if (withLegalChoices)
        {
            switch (cardId)
            {
                case "S01-0112":
                    cards["target"] = Card("S01-0005", "batch6ib-sunwu-target", cost: 2);
                    player.Graveyard.Add(cards["target"]);
                    break;
                case "S01-0115":
                    morale["cost"] = new L12MoraleCard
                    {
                        InstanceId = "batch6ib-jingke-cost", CardId = "S01-01C1", Tapped = false,
                    };
                    player.Morale.Add(morale["cost"]);
                    cards["target"] = Card("S01-0201", "batch6ib-jingke-target", troops: 2000);
                    enemy.Field[0][0] = cards["target"];
                    break;
                case "S01-0207":
                    cards["target"] = Card("S01-0201", "batch6ib-tut-target", cost: 2);
                    player.Graveyard.Add(cards["target"]);
                    break;
                case "S01-0210":
                    cards["target"] = Card("S01-0202", "batch6ib-nitocris-target", cost: 2);
                    player.Graveyard.Add(cards["target"]);
                    break;
                case "S01-0304":
                    cards["target"] = Card("S01-0201", "batch6ib-harald-target", troops: 2000);
                    enemy.Field[0][0] = cards["target"];
                    break;
                case "S01-0307":
                    cards["target"] = Card("S01-0303", "batch6ib-alvida-target", cost: 2);
                    player.Graveyard.Add(cards["target"]);
                    break;
                case "S01-0313":
                    cards["target"] = Card("S01-0201", "batch6ib-oddr-target", troops: 2000);
                    enemy.Field[0][0] = cards["target"];
                    break;
                case "S01-0403":
                    cards["private"] = Card("S01-0016", "batch6ib-uesugi-private");
                    player.Hand.Add(cards["private"]);
                    break;
                case "S01-0407":
                    cards["private"] = Card("S01-0402", "batch6ib-ryoma-private", cost: 3);
                    player.Hand.Add(cards["private"]);
                    break;
                case "S02-01S1":
                    player.MoraleDeck.Add(new L12MoraleCard
                    {
                        InstanceId = "batch6ib-xiaotian-deck", CardId = "S02-01C1", Tapped = false,
                    });
                    break;
                case "S02-0508":
                    morale["target"] = new L12MoraleCard
                    {
                        InstanceId = "batch6ib-atalanta-morale", CardId = "S02-05C1", Tapped = false,
                    };
                    player.Morale.Add(morale["target"]);
                    break;
                case "S02-0518":
                    cards["target"] = Card("S02-0507", "batch6ib-theseus-promotion");
                    player.Graveyard.Add(cards["target"]);
                    break;
                case "S02-0601":
                    cards["private"] = Card("S02-0606", "batch6ib-arthur-private", cost: 4);
                    player.Hand.Add(cards["private"]);
                    break;
            }
        }

        var data = trigger is "after-attack" or "after-kill"
            ? new Dictionary<string, string>
            {
                ["killed"] = "true", ["combatKillConfirmed"] = "true",
                ["defeatedInstanceId"] = "batch6ib-defeated", ["combatTiming"] = "kill",
            }
            : new Dictionary<string, string> { ["cause"] = cause };
        Invoke(game, "QueueOrPushTriggeredEffect", 0, source, trigger, "6I-B公开声明测试", null, data);
        return (source, cards, morale);
    }

    public static IEnumerable<object[]> ReviewedTriggerRows()
        => ReviewedTriggers.Select(trigger => new object[] { trigger.CardId, trigger.Trigger });

    public static IEnumerable<object[]> ModeRows()
        => ResolutionModeCards.Select(cardId => new object[] { cardId, cardId == "S02-0002" ? "after-kill" : "death" });

    public static IEnumerable<object[]> NoLegalChoiceRows()
    {
        yield return ["S01-0112", "death"];
        yield return ["S01-0115", "death"];
        yield return ["S01-0207", "death"];
        yield return ["S01-0210", "death"];
        yield return ["S01-0304", "death"];
        yield return ["S01-0307", "death"];
        yield return ["S01-0313", "death"];
        yield return ["S01-0403", "death"];
        yield return ["S01-0407", "death"];
        yield return ["S02-01S1", "death"];
        yield return ["S02-0508", "death"];
        yield return ["S02-0518", "death"];
        yield return ["S02-0601", "death"];
    }

    [Theory]
    [MemberData(nameof(ReviewedTriggerRows))]
    [Trait("L12Evidence", "entry:batch6ib-prestack-death-declaration")]
    public void EveryReviewedLegacyTriggerDeclaresBeforeAnyStackItem(string cardId, string trigger)
    {
        var game = Create(9700 + Array.FindIndex(ReviewedTriggers, item => item == (cardId, trigger)));
        QueueReviewedTrigger(game, cardId, trigger);

        var declaration = OnlyPrompt(game);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Empty(game.State.EffectStack);
    }

    [Theory]
    [MemberData(nameof(ModeRows))]
    [Trait("L12Evidence", "entry:batch6ib-decline-no-empty-stack")]
    public void DecliningLegacyOptionalModeCreatesNoEmptyResponseStack(string cardId, string trigger)
    {
        var game = Create(9750 + cardId[^1]);
        QueueReviewedTrigger(game, cardId, trigger);

        Assert.Contains("mode:none", OnlyPrompt(game).ValidChoices);
        ResolveChoice(game, "mode:none");

        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }

    [Theory]
    [MemberData(nameof(NoLegalChoiceRows))]
    [Trait("L12Evidence", "entry:batch6ib-no-legal-choice-no-stack")]
    public void TriggerWithNoLegalChoiceCreatesNeitherDeclarationNorEmptyStack(string cardId, string trigger)
    {
        var game = Create(9800 + cardId[^1]);
        QueueReviewedTrigger(game, cardId, trigger, withLegalChoices: false);

        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    public static IEnumerable<object[]> GraveToHandRows()
    {
        yield return ["S01-0112", true];
        yield return ["S01-0307", false];
        yield return ["S02-0518", true];
    }

    public static IEnumerable<object[]> DeathHandSummonRows()
    {
        yield return ["S01-0407", false, true];
        yield return ["S02-0601", true, false];
    }

    public static IEnumerable<object[]> DeathHandSummonIdentityRows()
    {
        yield return ["S01-0407", false];
        yield return ["S02-0601", true];
    }

    [Theory]
    [MemberData(nameof(DeathHandSummonRows))]
    [Trait("L12Evidence", "entry:death-hand-summon-shared-lifecycle")]
    public void DeathHandSummonsShareSelectionResponseAndSettlementProtocol(
        string cardId, bool optional, bool tapped)
    {
        var game = Create(9805 + cardId[^1]);
        var player = game.State.Players[0];
        var fixture = QueueReviewedTrigger(game, cardId, "death");

        if (optional)
        {
            var mode = OnlyPrompt(game);
            Assert.Equal(["mode:none", "mode:use"], mode.ValidChoices);
            ResolveChoice(game, "mode:use");
        }

        var hand = OnlyPrompt(game);
        Assert.Equal("hand-card", hand.Kind);
        Assert.Equal(optional ? 1 : 0, hand.MinChoose);
        Assert.Equal(1, hand.MaxChoose);
        Assert.Equal(optional, hand.ValidChoices.Contains("skip"));
        if (optional) Assert.Equal("取消整次发动", hand.ChoiceLabels["skip"]);
        Assert.Contains(fixture.Cards["private"].InstanceId, hand.ValidChoices);
        Assert.Contains(fixture.Cards["private"].InstanceId,
            JsonSerializer.Serialize(game.SnapshotFor(0)), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Cards["private"].InstanceId,
            JsonSerializer.Serialize(game.SnapshotFor(1)), StringComparison.Ordinal);

        ResolveCards(game, fixture.Cards["private"].InstanceId);
        var slotPrompt = OnlyPrompt(game);
        Assert.Equal("slot", slotPrompt.Kind);
        Assert.Equal(optional, slotPrompt.ValidChoices.Contains("skip"));
        if (optional) Assert.Equal("取消整次发动", slotPrompt.ChoiceLabels["skip"]);
        var slot = slotPrompt.ValidChoices.First(choice => choice != "skip");
        ResolveChoice(game, slot);

        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal(fixture.Cards["private"].InstanceId,
            item.Data.GetValueOrDefault("declared:entryCard"));
        Assert.False(string.IsNullOrWhiteSpace(item.Data.GetValueOrDefault("presentationSceneId")));
        PassResponses(game);

        Assert.DoesNotContain(fixture.Cards["private"], player.Hand);
        Assert.Contains(fixture.Cards["private"], player.Field.SelectMany(row => row).OfType<L12CardInstance>());
        Assert.Equal(tapped, fixture.Cards["private"].Tapped);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == cardId));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0407")]
    [Trait("L12Evidence", "entry:ryoma-mandatory-zero-selection")]
    public void RyomaDeathIsMandatoryButMayChooseZeroCardsInsideTheEffect()
    {
        var game = Create(9807);
        var player = game.State.Players[0];
        var fixture = QueueReviewedTrigger(game, "S01-0407", "death");

        var hand = OnlyPrompt(game);
        Assert.Equal("hand-card", hand.Kind);
        Assert.Equal(0, hand.MinChoose);
        Assert.DoesNotContain("mode:none", hand.ValidChoices);
        Assert.DoesNotContain("skip", hand.ValidChoices);
        ResolveCards(game);

        Assert.Single(game.State.EffectStack);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "slot");
        PassResponses(game);

        Assert.Contains(fixture.Cards["private"], player.Hand);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "ability-cancelled"
            && entry.Cards.Any(card => card.CardId == "S01-0407"));
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == "S01-0407"));
    }

    [Theory]
    [MemberData(nameof(DeathHandSummonIdentityRows))]
    [Trait("L12Evidence", "entry:death-hand-summon-revalidate-zone")]
    public void DeathHandSummonFailsWhenDeclaredCardLeavesHandDuringResponse(
        string cardId, bool optional)
    {
        var game = Create(9809 + cardId[^1]);
        var player = game.State.Players[0];
        var fixture = QueueReviewedTrigger(game, cardId, "death");
        if (optional) ResolveChoice(game, "mode:use");
        ResolveCards(game, fixture.Cards["private"].InstanceId);
        var slot = OnlyPrompt(game).ValidChoices.First(choice => choice != "skip");
        ResolveChoice(game, slot);

        Assert.True(player.Hand.Remove(fixture.Cards["private"]));
        player.Graveyard.Add(fixture.Cards["private"]);
        PassResponses(game);

        Assert.Contains(fixture.Cards["private"], player.Graveyard);
        Assert.DoesNotContain(fixture.Cards["private"], player.Field.SelectMany(row => row).OfType<L12CardInstance>());
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "failed"
            && entry.Cards.Any(card => card.CardId == cardId));
    }

    [Theory]
    [MemberData(nameof(DeathHandSummonIdentityRows))]
    [Trait("L12Evidence", "entry:death-hand-summon-revalidate-cost-slot")]
    public void DeathHandSummonRevalidatesCurrentCostAndReservedSlot(
        string cardId, bool optional)
    {
        foreach (var invalidateSlot in new[] { false, true })
        {
            var game = Create(9811 + cardId[^1] + (invalidateSlot ? 20 : 0));
            var player = game.State.Players[0];
            var fixture = QueueReviewedTrigger(game, cardId, "death");
            if (optional) ResolveChoice(game, "mode:use");
            ResolveCards(game, fixture.Cards["private"].InstanceId);
            var slotPrompt = OnlyPrompt(game);
            var slot = slotPrompt.ValidChoices.First(choice => choice != "skip");
            ResolveChoice(game, slot);

            if (invalidateSlot)
            {
                var parts = slot.Split(':');
                var row = int.Parse(parts[0]);
                var index = int.Parse(parts[1]);
                player.Field[row][index] = Card("S01-0002", $"occupied-{cardId}");
            }
            else
            {
                fixture.Cards["private"].CostModifier += 1;
            }

            PassResponses(game);

            Assert.Contains(fixture.Cards["private"], player.Hand);
            Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
                && entry.EffectResultStatus == "failed"
                && entry.Cards.Any(card => card.CardId == cardId));
        }
    }

    [Theory]
    [MemberData(nameof(DeathHandSummonIdentityRows))]
    [Trait("L12Evidence", "entry:death-hand-summon-negated")]
    public void DeathHandSummonStopsWhenNegatedWithoutMovingTheCard(string cardId, bool optional)
    {
        var game = Create(9815 + cardId[^1]);
        var player = game.State.Players[0];
        var fixture = QueueReviewedTrigger(game, cardId, "death");
        if (optional) ResolveChoice(game, "mode:use");
        ResolveCards(game, fixture.Cards["private"].InstanceId);
        var slot = OnlyPrompt(game).ValidChoices.First(choice => choice != "skip");
        ResolveChoice(game, slot);
        Assert.Single(game.State.EffectStack).Negated = true;

        PassResponses(game);

        Assert.Contains(fixture.Cards["private"], player.Hand);
        Assert.DoesNotContain(fixture.Cards["private"], player.Field.SelectMany(row => row).OfType<L12CardInstance>());
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "negated"
            && entry.Cards.Any(card => card.CardId == cardId));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0601")]
    [Trait("L12Evidence", "entry:arthur-cancel-duplicate")]
    public void ArthurMayCancelBeforeCommitAndDuplicateSubmissionIsRejected()
    {
        var game = Create(9818);
        var fixture = QueueReviewedTrigger(game, "S02-0601", "death");
        ResolveChoice(game, "mode:use");
        var prompt = OnlyPrompt(game);
        Assert.Equal("取消整次发动", prompt.ChoiceLabels["skip"]);

        ResolveCards(game, "skip");

        Assert.Empty(game.State.EffectStack);
        Assert.Contains(fixture.Cards["private"], game.State.Players[0].Hand);
        Assert.False(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: [fixture.Cards["private"].InstanceId])).Accepted);
    }

    [Theory]
    [MemberData(nameof(GraveToHandRows))]
    [Trait("L12Evidence", "entry:grave-to-hand-shared-lifecycle")]
    public void GraveToHandDeathEffectsUseOneExplicitSelectionAndSettlementProtocol(string cardId, bool optional)
    {
        var game = Create(9810 + cardId[^1]);
        var player = game.State.Players[0];
        var invalid = Card("S01-0002", $"grave-to-hand-invalid-{cardId}");
        player.Graveyard.Add(invalid);
        var fixture = QueueReviewedTrigger(game, cardId, "death");

        if (optional) ResolveChoice(game, "mode:use");
        var targetPrompt = OnlyPrompt(game);
        Assert.Equal("grave-card", targetPrompt.Kind);
        Assert.Equal(1, targetPrompt.MinChoose);
        Assert.Equal(1, targetPrompt.MaxChoose);
        Assert.Equal(optional, targetPrompt.ValidChoices.Contains("skip"));
        if (optional) Assert.Equal("取消整次发动", targetPrompt.ChoiceLabels["skip"]);
        Assert.Equal([fixture.Cards["target"].InstanceId],
            targetPrompt.ValidChoices.Where(choice => choice != "skip"));
        var displayed = targetPrompt.Data["displayCardIds"].Split('|');
        Assert.Contains(fixture.Cards["target"].InstanceId, displayed);
        Assert.Contains(invalid.InstanceId, displayed);
        var reconnectPrompt = JsonSerializer.Serialize(game.SnapshotFor(0));
        Assert.Contains(targetPrompt.PromptId, reconnectPrompt, StringComparison.Ordinal);
        Assert.Contains(fixture.Cards["target"].InstanceId, reconnectPrompt, StringComparison.Ordinal);
        Assert.Contains(invalid.InstanceId, reconnectPrompt, StringComparison.Ordinal);

        ResolveCards(game, fixture.Cards["target"].InstanceId);
        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal(fixture.Cards["target"].InstanceId,
            item.Data.GetValueOrDefault("declared:recoverTarget"));
        Assert.Contains(fixture.Cards["target"].InstanceId,
            JsonSerializer.Serialize(game.SnapshotFor(0)), StringComparison.Ordinal);
        PassResponses(game);

        Assert.Contains(fixture.Cards["target"], player.Hand);
        Assert.DoesNotContain(fixture.Cards["target"], player.Graveyard);
        Assert.Contains(game.State.Events, entry => entry.Type == "reveal"
            && entry.Cards.Any(card => card.InstanceId == fixture.Cards["target"].InstanceId));
        Assert.Contains(game.State.AuthorityEvents, entry => entry.Type == "effect-hand-add"
            && entry.TargetInstanceId == fixture.Cards["target"].InstanceId);
        var declaration = Assert.Single(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.Cards.Any(card => card.CardId == cardId));
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == cardId));
        Assert.Equal("resolved", result.EffectResultStatus);
        Assert.Equal(declaration.EffectSceneId, result.EffectSceneId);
    }

    [Theory]
    [MemberData(nameof(GraveToHandRows))]
    [Trait("L12Evidence", "entry:grave-to-hand-revalidate-after-response")]
    public void GraveToHandDeathEffectsFailWhenDeclaredTargetLeavesGraveDuringStack(string cardId, bool optional)
    {
        var game = Create(9820 + cardId[^1]);
        var player = game.State.Players[0];
        var fixture = QueueReviewedTrigger(game, cardId, "death");
        if (optional) ResolveChoice(game, "mode:use");
        ResolveCards(game, fixture.Cards["target"].InstanceId);

        Assert.True(player.Graveyard.Remove(fixture.Cards["target"]));
        player.Library.Add(fixture.Cards["target"]);
        PassResponses(game);

        Assert.DoesNotContain(fixture.Cards["target"], player.Hand);
        Assert.Contains(fixture.Cards["target"], player.Library);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("逆结算后不再符合条件", StringComparison.Ordinal));
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "failed"
            && entry.Cards.Any(card => card.CardId == cardId));
    }

    [Theory]
    [MemberData(nameof(GraveToHandRows))]
    [Trait("L12Evidence", "entry:grave-to-hand-negated")]
    public void GraveToHandDeathEffectsStopWhenNegatedWithoutMovingTarget(string cardId, bool optional)
    {
        var game = Create(9830 + cardId[^1]);
        var player = game.State.Players[0];
        var fixture = QueueReviewedTrigger(game, cardId, "death");
        if (optional) ResolveChoice(game, "mode:use");
        ResolveCards(game, fixture.Cards["target"].InstanceId);
        Assert.Single(game.State.EffectStack).Negated = true;

        PassResponses(game);

        Assert.Contains(fixture.Cards["target"], player.Graveyard);
        Assert.DoesNotContain(fixture.Cards["target"], player.Hand);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "negated"
            && entry.Cards.Any(card => card.CardId == cardId));
    }

    [Theory]
    [InlineData("S01-0112")]
    [InlineData("S02-0518")]
    [Trait("L12Evidence", "entry:grave-to-hand-decline-duplicate")]
    public void GraveToHandDeathEffectsDeclineWithoutStackAndRejectDuplicateSubmission(string cardId)
    {
        var game = Create(9840 + cardId[^1]);
        QueueReviewedTrigger(game, cardId, "death");
        var prompt = OnlyPrompt(game);

        ResolveChoice(game, "mode:none");

        Assert.Empty(game.State.EffectStack);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-declined"
            && entry.EffectResultStatus == "declined"
            && entry.Cards.Any(card => card.CardId == cardId));
        Assert.False(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "mode:use")).Accepted);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0112")]
    [Trait("L12Evidence", "entry:grave-to-hand-trigger-condition-locked")]
    public void SunWuLocksDisasterTriggerConditionButStillRevalidatesTheDeclaredCard()
    {
        var game = Create(9849);
        game.State.DisasterValue = 4;
        var fixture = QueueReviewedTrigger(game, "S01-0112", "death");
        ResolveChoice(game, "mode:use");
        ResolveCards(game, fixture.Cards["target"].InstanceId);

        game.State.DisasterValue = 5;
        PassResponses(game);

        Assert.Contains(fixture.Cards["target"], game.State.Players[0].Hand);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == "S01-0112"));
    }

    [Theory]
    [InlineData("S01-0304")]
    [InlineData("S01-0313")]
    [InlineData("S02-01S1")]
    [InlineData("S02-0508")]
    [Trait("L12Evidence", "entry:batch6ib-single-death-result-scene")]
    public void AuditedSingleDeathEffectsPublishOneResolvedSettlementSegment(string cardId)
    {
        var game = Create(9830 + cardId[^1]);
        var fixture = QueueReviewedTrigger(game, cardId, "death");
        switch (cardId)
        {
            case "S01-0304":
                ResolveCards(game, fixture.Cards["target"].InstanceId);
                break;
            case "S01-0313":
                ResolveChoice(game, "mode:use");
                ResolveCards(game, fixture.Cards["target"].InstanceId);
                break;
            case "S02-01S1":
                ResolveChoice(game, "mode:use");
                break;
            case "S02-0508":
                ResolveCards(game, fixture.Morale["target"].InstanceId);
                break;
        }

        var declaration = Assert.Single(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.Cards.Any(card => card.CardId == cardId));
        Assert.NotNull(declaration.EffectSceneId);
        Assert.Equal(1, declaration.EffectSegmentIndex);
        Assert.Equal(1, declaration.EffectSegmentCount);
        PassResponses(game);

        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == cardId));
        Assert.Equal("resolved", result.EffectResultStatus);
        Assert.Equal(declaration.EffectSceneId, result.EffectSceneId);
        switch (cardId)
        {
            case "S01-0304":
                Assert.DoesNotContain(fixture.Cards["target"], game.State.Players[1].Field.SelectMany(row => row));
                break;
            case "S01-0313":
                Assert.True(fixture.Cards["target"].Tapped);
                break;
            case "S02-01S1":
                Assert.True(Assert.Single(game.State.Players[0].Morale).Tapped);
                break;
            case "S02-0508":
                Assert.True(fixture.Morale["target"].IsGodPower);
                break;
        }
    }

    [Theory]
    [InlineData("S01-0313")]
    [InlineData("S02-01S1")]
    [Trait("L12Evidence", "entry:batch6ib-single-death-decline")]
    public void AuditedOptionalSingleDeathDeclinesAreDistinctAndRejectDuplicates(string cardId)
    {
        var game = Create(9840 + cardId[^1]);
        QueueReviewedTrigger(game, cardId, "death");
        var prompt = OnlyPrompt(game);

        ResolveChoice(game, "mode:none");

        Assert.Empty(game.State.EffectStack);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-declined"
            && entry.EffectResultStatus == "declined"
            && entry.Cards.Any(card => card.CardId == cardId));
        Assert.False(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "mode:use")).Accepted);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0115")]
    [Trait("L12Evidence", "entry:batch6ib-jingke-prepaid-zero-target")]
    public void JingKeMayDeclareZeroTargetsButMustPrepayExactMoraleBeforeStack()
    {
        var game = Create(9850);
        var fixture = QueueReviewedTrigger(game, "S01-0115", "death");

        ResolveChoice(game, "mode:use");
        ResolveCards(game, fixture.Morale["cost"].InstanceId);
        ResolveCards(game);

        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal("true", item.Data.GetValueOrDefault("return-morale-prepaid"));
        Assert.DoesNotContain(fixture.Morale["cost"], game.State.Players[0].Morale);
        item.Negated = true;
        PassResponses(game);
        Assert.DoesNotContain(fixture.Morale["cost"], game.State.Players[0].Morale);
        Assert.Same(fixture.Cards["target"], game.State.Players[1].Field[0][0]);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0115")]
    [Trait("L12Evidence", "entry:batch6ib-jingke-cancel-distinct-from-zero-target")]
    public void JingKeTargetStepDistinguishesCancellationFromChoosingZeroTargets()
    {
        var game = Create(98501);
        var fixture = QueueReviewedTrigger(game, "S01-0115", "death");

        ResolveChoice(game, "mode:use");
        ResolveCards(game, fixture.Morale["cost"].InstanceId);
        var targetPrompt = OnlyPrompt(game);
        Assert.Contains(fixture.Cards["target"].InstanceId, targetPrompt.ValidChoices);
        Assert.Contains("skip", targetPrompt.ValidChoices);
        Assert.Equal("取消整次发动", targetPrompt.ChoiceLabels["skip"]);
        ResolveCards(game, "skip");
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(fixture.Morale["cost"], game.State.Players[0].Morale);
    }

    [Theory]
    [InlineData("S01-0403")]
    [InlineData("S01-0407")]
    [InlineData("S02-0601")]
    [Trait("L12Evidence", "entry:batch6ib-private-hand-declaration")]
    public void PrivateHandIdentityNeverAppearsInOpponentDeclarationSnapshot(string cardId)
    {
        var game = Create(9851 + cardId[^1]);
        var fixture = QueueReviewedTrigger(game, cardId, "death");
        if (cardId != "S01-0407") ResolveChoice(game, "mode:use");
        var privatePrompt = OnlyPrompt(game);
        Assert.Contains(fixture.Cards["private"].InstanceId, privatePrompt.ValidChoices);

        var ownerSnapshot = JsonSerializer.Serialize(game.SnapshotFor(0));
        var opponentSnapshot = JsonSerializer.Serialize(game.SnapshotFor(1));
        Assert.Contains(fixture.Cards["private"].InstanceId, ownerSnapshot, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Cards["private"].InstanceId, opponentSnapshot, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0403")]
    [Trait("L12Evidence", "entry:uesugi-counter-deployment-revalidation")]
    [L12AbilityEvidence("S01-0403:ability:death:c3e5fc27d01fe269", "target-invalidated")]
    public void UesugiCounterDeploymentDoesNotReplaceItsDeclaredHandCounterAfterResponse()
    {
        var game = Create(98513);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        opponent.Field[0][0] = Card("S01-0003", "batch6ib-uesugi-opponent-legion");
        var fixture = QueueReviewedTrigger(game, "S01-0403", "death");

        ResolveChoice(game, "mode:use");
        ResolveCards(game, fixture.Cards["private"].InstanceId);
        var slot = OnlyPrompt(game);
        Assert.Contains("1:0", slot.ValidChoices);
        ResolveChoice(game, "1:0");

        Assert.True(player.Hand.Remove(fixture.Cards["private"]));
        player.Graveyard.Add(fixture.Cards["private"]);
        PassResponses(game);

        Assert.Contains(fixture.Cards["private"], player.Graveyard);
        Assert.DoesNotContain(fixture.Cards["private"], player.Field.SelectMany(row => row).OfType<L12CardInstance>());
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("已声明的对象", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0403")]
    [Trait("L12Evidence", "entry:uesugi-counter-deployment-normal")]
    [L12AbilityEvidence("S01-0403:ability:death:c3e5fc27d01fe269", "normal")]
    public void UesugiCounterDeploymentSetsItsDeclaredHandCounterAfterResponses()
    {
        var game = Create(98514);
        var player = game.State.Players[0];
        game.State.Players[1].Field[0][0] = Card("S01-0003", "batch6ib-uesugi-normal-opponent-legion");
        var fixture = QueueReviewedTrigger(game, "S01-0403", "death");

        ResolveChoice(game, "mode:use");
        ResolveCards(game, fixture.Cards["private"].InstanceId);
        ResolveChoice(game, "1:0");
        PassResponses(game);

        Assert.Same(fixture.Cards["private"], player.Field[1][0]);
        Assert.True(fixture.Cards["private"].Hidden);
        Assert.Equal(game.State.Round, fixture.Cards["private"].SummonRound);
    }

    [Theory]
    [InlineData("S01-0001", 2)]
    [InlineData("S01-0303", 1)]
    [InlineData("S01-0306", 2)]
    [InlineData("S02-0301", 1)]
    [Trait("L12Evidence", "entry:batch6ib-delayed-hidden-discard")]
    public void DrawCycleDeclaresOnlyModeAndDelaysExactDiscardUntilAfterDraw(string cardId, int drawCount)
    {
        var game = Create(9860 + cardId[^1]);
        var player = game.State.Players[0];
        var before = player.Hand.Count;
        QueueReviewedTrigger(game, cardId, "death");
        ResolveChoice(game, "mode:use");

        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal("mode:use", item.Data.GetValueOrDefault("declared:mode"));
        Assert.DoesNotContain(player.Hand.Select(card => card.InstanceId), id => item.Data.Values.Contains(id));
        PassResponses(game);

        var discard = OnlyPrompt(game);
        Assert.Equal("pending-activation", discard.Continuation);
        Assert.Equal("hand-card", discard.Kind);
        Assert.DoesNotContain("skip", discard.ValidChoices);
        Assert.False(discard.Data.ContainsKey("action"));
        Assert.Equal(before + drawCount, player.Hand.Count);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0002")]
    [Trait("L12Evidence", "entry:batch6ib-alice-once-reservation")]
    public void AliceDeclineReleasesPendingOnceAndCommitFinalizesItBeforeStack()
    {
        var game = Create(9870);
        var fixture = QueueReviewedTrigger(game, "S02-0002", "after-kill");
        var onceKey = $"alice-ready:{fixture.Source.InstanceId}:{game.State.TurnSerial}";
        Assert.Contains($"{onceKey}:pending", game.State.Players[0].UsedAbilities);
        var declinePrompt = OnlyPrompt(game);
        ResolveChoice(game, "mode:none");
        Assert.DoesNotContain($"{onceKey}:pending", game.State.Players[0].UsedAbilities);
        Assert.DoesNotContain(onceKey, game.State.Players[0].UsedAbilities);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-declined"
            && entry.EffectResultStatus == "declined"
            && entry.Cards.Any(card => card.CardId == "S02-0002"));
        Assert.False(game.Handle(declinePrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: declinePrompt.PromptId, Choice: "mode:use")).Accepted);

        var accepted = QueueReviewedTrigger(game, "S02-0002", "after-kill");
        accepted.Source.Tapped = true;
        var acceptedOnceKey = $"alice-ready:{accepted.Source.InstanceId}:{game.State.TurnSerial}";
        ResolveChoice(game, "mode:use");
        Assert.Contains(acceptedOnceKey, game.State.Players[0].UsedAbilities);
        Assert.DoesNotContain($"{acceptedOnceKey}:pending", game.State.Players[0].UsedAbilities);
        Assert.Single(game.State.EffectStack);
        var declaration = Assert.Single(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.Cards.Any(card => card.InstanceId == accepted.Source.InstanceId));
        Assert.NotNull(declaration.EffectSceneId);
        PassResponses(game);
        Assert.False(accepted.Source.Tapped);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.EffectSceneId == declaration.EffectSceneId);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0518")]
    [Trait("L12Evidence", "entry:batch6ib-theseus-reveal-hand-add")]
    public void TheseusRevealsDeclaredPromotionAndPublishesHandAddAuthorityEvent()
    {
        var game = Create(9871);
        var fixture = QueueReviewedTrigger(game, "S02-0518", "death");
        ResolveChoice(game, "mode:use");
        ResolveCards(game, fixture.Cards["target"].InstanceId);
        PassResponses(game);

        Assert.Contains(game.State.Events, entry => entry.Type == "reveal"
            && entry.Cards.Any(card => card.InstanceId == fixture.Cards["target"].InstanceId));
        Assert.Contains(game.State.AuthorityEvents, entry => entry.Type == "effect-hand-add"
            && entry.TargetInstanceId == fixture.Cards["target"].InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0615")]
    [Trait("L12Evidence", "entry:batch6ib-gwen-effect-only-mode")]
    public void GwenOnlyTriggersForEffectDeathAndUsesHerOwnCorrectPublicModeLog()
    {
        var combat = Create(9872);
        QueueReviewedTrigger(combat, "S02-0615", "death", cause: "combat");
        Assert.Empty(combat.State.PendingPrompts);
        Assert.Empty(combat.State.EffectStack);

        var effect = Create(9873);
        effect.State.Players[0].Hp = effect.State.Players[0].MaxHp - 1;
        var hpBefore = effect.State.Players[0].Hp;
        QueueReviewedTrigger(effect, "S02-0615", "death", cause: "effect");
        Assert.Equal(["mode:heal", "mode:draw"], OnlyPrompt(effect).ValidChoices);
        ResolveChoice(effect, "mode:heal");
        PassResponses(effect);
        Assert.Equal(hpBefore + 1, effect.State.Players[0].Hp);
        Assert.DoesNotContain(effect.State.Events, entry => entry.Text.Contains("兰马洛克", StringComparison.Ordinal));
        Assert.Contains(effect.State.Events, entry => entry.Text.Contains("格温莉安", StringComparison.Ordinal));
    }
}
