using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6JBRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
        => new(Catalog, "atomic-review-batch6jb", "ATOMIC6JB", seed, ["甲", "乙"], [0, 1],
            skipPreparation: true);

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
        };
    }

    private static void AddMorale(L12PlayerState player, int count, string prefix, bool tapped = false)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-01C1",
                InstanceId = $"{prefix}-{index}",
                Tapped = tapped,
            });
    }

    private static void HoldOpponentResponseWindow(L12GameEngine game, int controller = 0)
    {
        var opponent = game.State.Players[1 - controller];
        var counter = Card("S01-0016", $"batch6jb-response-{controller}-{Guid.NewGuid():N}");
        counter.Hidden = true;
        counter.SetRound = 0;
        opponent.Field[1][0] = counter;
        opponent.Hand.Add(Card("S01-0002", $"batch6jb-response-hand-{controller}-{Guid.NewGuid():N}"));
    }

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    private static L12Prompt OnlyPrompt(L12GameEngine game) => Assert.Single(game.State.PendingPrompts);

    private static void Resolve(L12GameEngine game, string? choice = null, List<string>? choices = null)
    {
        var prompt = OnlyPrompt(game);
        Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: choice, CardInstanceIds: choices)).Accepted);
    }

    private static void PassResponses(L12GameEngine game, int maximum = 12)
    {
        var guard = 0;
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response" && guard++ < maximum)
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    private static void PassCurrentResponseWindow(L12GameEngine game)
    {
        var stackItemId = game.State.EffectStack[^1].StackItemId;
        while (game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt
            && prompt.StackItemId == stackItemId)
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
    }

    [Theory]
    [InlineData("S01-0007")]
    [InlineData("S01-0013")]
    [Trait("L12Evidence", "hand-play:batch6jb-public-followup-declaration")]
    public void RemainingCompositeTacticsDeclareTheirPublicFollowupBeforeAnyStack(string cardId)
    {
        var game = Create(9980 + cardId[^1]);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        player.Hand.Clear();
        var source = Card(cardId, $"batch6jb-{cardId}");
        player.Hand.Add(source);
        AddMorale(player, 12, $"batch6jb-{cardId}-morale");
        opponent.Hand.Add(Card("S01-0002", $"batch6jb-{cardId}-enemy-hand"));
        player.Library.Insert(0, Card("S01-0102", $"batch6jb-{cardId}-hidden-top"));
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);

        Assert.Equal("pending-activation", OnlyPrompt(game).Continuation);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(game.State.PendingActivations, activation => activation.SourceCardId == cardId);
    }

    [Fact]
    [Trait("L12Evidence", "entry:optional-followup-eligibility")]
    public void CompositePlanSkipsAFollowupDecisionWhenOnlyDeclineRemains()
    {
        var game = Create(9988);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        player.Hand.Clear();
        player.Morale.Clear();
        for (var row = 0; row < opponent.Field.Length; row++)
            for (var slot = 0; slot < opponent.Field[row].Length; slot++)
                opponent.Field[row][slot] = null;
        var march = Card("S01-0118", "batch6jb-no-kill-followup");
        var buffTarget = Card("S01-0109", "batch6jb-only-buff-target");
        player.Hand.Add(march);
        player.Field[0][0] = buffTarget;
        AddMorale(player, Math.Max(3, march.CurrentCost), "batch6jb-no-kill-morale");
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var play = game.Handle(0, new L12Command("playCard", march.InstanceId));

        Assert.True(play.Accepted, play.Error);
        var targetPrompt = OnlyPrompt(game);
        Assert.Equal("pending-activation", targetPrompt.Continuation);
        Assert.Contains(buffTarget.InstanceId, targetPrompt.ValidChoices);
        Assert.DoesNotContain("mode:none", targetPrompt.ValidChoices);
        Assert.DoesNotContain(game.State.PendingActivations, activation =>
            activation.DeclaredValues.ContainsKey("mode"));
    }

    [Theory]
    [InlineData("S01-0101", "after-attack")]
    [InlineData("S01-0311", "after-attack")]
    [InlineData("S01-0108", "death")]
    [InlineData("S02-0001", "s2-after-opponent-tactic")]
    [Trait("L12Evidence", "trigger:batch6jb-public-declaration")]
    public void RemainingPublicTriggersDeclareBeforeAnyStack(string cardId, string trigger)
    {
        var game = Create(9990 + cardId[^1]);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var source = Card(cardId, $"batch6jb-source-{cardId}");
        player.Field[0][0] = source;
        AddMorale(player, 6, $"batch6jb-{cardId}-morale");
        player.Graveyard.Add(Card("S01-0002", $"batch6jb-{cardId}-grave-a"));
        player.Graveyard.Add(Card("S01-0003", $"batch6jb-{cardId}-grave-b"));
        AddMorale(opponent, 1, $"batch6jb-{cardId}-enemy-morale", tapped: true);
        game.State.ActivePlayer = trigger == "death" ? 1 : 0;

        Invoke(game, "QueueOrPushTriggeredEffect", 0, source, trigger, "6J-B公开声明回归", null,
            new Dictionary<string, string>());

        Assert.Equal("pending-activation", OnlyPrompt(game).Continuation);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "trigger:batch6jb-wukong-return-declaration")]
    public void WukongReturnUsesPublicTriggerDeclarationInsteadOfDirectOptionalPrompt()
    {
        var game = Create(9995);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var wukong = Card("S02-01M1", "batch6jb-wukong");
        wukong.IsMasterLegion = true;
        player.Field[0][0] = wukong;
        AddMorale(opponent, 1, "batch6jb-wukong-enemy");

        Assert.True((bool)Invoke(game, "ReturnWukongMasterLegions", player, "测试返回", false)!);

        Assert.Equal("pending-activation", OnlyPrompt(game).Continuation);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "trigger:batch6jb-faction-zero-once-declaration")]
    public void TiantingZeroMoraleRecoveryUsesPendingActivationAndKeepsOncePending()
    {
        var game = Create(9996);
        var player = game.State.Players[0];
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        player.UsedAbilities.Add("pending:factionZeroRecovery");

        Invoke(game, "AfterStackSettled");

        Assert.Equal("pending-activation", OnlyPrompt(game).Continuation);
        Assert.Contains("pending:factionZeroRecovery", player.UsedAbilities);
        Assert.DoesNotContain("trigger:factionZeroRecovery", player.UsedAbilities);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "trigger:batch6jb-prayer-refusal-independent-declaration")]
    public void PrayerRefusalStartsAnIndependentPrepaidDeclarationWithoutRevealingTheDisaster()
    {
        var game = Create(9997);
        var player = game.State.Players[0];
        var prayer = Card("S02-0012", "batch6jb-prayer");
        player.Hand.Clear();
        player.Hand.Add(prayer);
        AddMorale(player, 4, "batch6jb-prayer-morale");
        var disaster = Card("S01-DS01", "batch6jb-prayer-hidden-disaster");
        game.State.DisasterDeck.Insert(0, disaster);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", prayer.InstanceId)).Accepted);
        PassResponses(game);
        var consent = OnlyPrompt(game);
        Assert.Equal(1, consent.PlayerIndex);
        Assert.True(game.Handle(1,
            new L12Command("resolvePrompt", PromptId: consent.PromptId, Choice: "refuse")).Accepted);

        var declaration = OnlyPrompt(game);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.DoesNotContain(disaster.InstanceId, declaration.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "hand-play:batch6jb-prepaid-independent-segments")]
    public void WildCampPrepaysTheDeclaredFollowupAndStillQueuesItWhenTheSearchIsNegated()
    {
        var game = new L12GameEngine(Catalog, "atomic-review-batch6jb", "ATOMIC6JB", 9998,
            ["甲", "乙"], [0, 1], skipPreparation: true, autoPassEmptyResponses: false);
        var player = game.State.Players[0];
        player.Hand.Clear();
        var camp = Card("S01-0007", "batch6jb-camp-prepaid");
        player.Hand.Add(camp);
        AddMorale(player, 12, "batch6jb-camp-prepaid-morale");
        player.Library.Insert(0, Card("S01-0102", "batch6jb-camp-top-a"));
        player.Library.Insert(1, Card("S01-0002", "batch6jb-camp-top-b"));
        player.Library.Insert(2, Card("S01-0003", "batch6jb-camp-top-c"));
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", camp.InstanceId)).Accepted);
        Resolve(game, "mode:draw");
        var cost = OnlyPrompt(game);
        var paidId = cost.ValidChoices[0];
        Resolve(game, paidId);

        Assert.Contains(player.Morale, morale => morale.InstanceId == paidId && morale.Tapped);
        Assert.True(game.State.EffectStack.Count > 0,
            $"prompts={string.Join(';', game.State.PendingPrompts.Select(prompt => $"{prompt.Kind}:{prompt.Continuation}:{prompt.Data.GetValueOrDefault("activationStep")}"))}; "
            + $"activations={string.Join(';', game.State.PendingActivations.Select(activation => $"{activation.Ability}:{activation.CurrentStep}"))}; "
            + $"events={string.Join(';', game.State.Events.TakeLast(8).Select(entry => $"{entry.Type}:{entry.Text}"))}");
        Assert.Equal("camp-search", game.State.EffectStack[^1].Data["atomicFlow"]);
        game.State.EffectStack[^1].Negated = true;
        PassCurrentResponseWindow(game);

        Assert.Equal("camp-draw", game.State.EffectStack[^1].Data["atomicFlow"]);
        Assert.Contains(player.Morale, morale => morale.InstanceId == paidId && morale.Tapped);
    }

    [Fact]
    [Trait("L12Evidence", "hand-play:batch6jb-affected-player-resolution-choice")]
    public void FrontlineReconPrepaysItsIndependentSecondSegmentButLetsTheAffectedPlayerChooseTheirHandAtResolution()
    {
        var game = new L12GameEngine(Catalog, "atomic-review-batch6jb", "ATOMIC6JB", 10005,
            ["甲", "乙"], [0, 1], skipPreparation: true, autoPassEmptyResponses: false);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        player.Hand.Clear();
        opponent.Hand.Clear();
        var scout = Card("S01-0013", "batch6jb-scout-prepaid");
        var hiddenHand = Card("S01-0002", "batch6jb-scout-affected-hand");
        player.Hand.Add(scout);
        opponent.Hand.Add(hiddenHand);
        AddMorale(player, 12, "batch6jb-scout-prepaid-morale");
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", scout.InstanceId)).Accepted);
        Resolve(game, "mode:use");
        var cost = OnlyPrompt(game);
        var paidId = cost.ValidChoices[0];
        Resolve(game, paidId);

        Assert.Equal("scout-reveal", game.State.EffectStack[^1].Data["atomicFlow"]);
        Assert.Contains(player.Morale, morale => morale.InstanceId == paidId && morale.Tapped);
        PassCurrentResponseWindow(game);
        Assert.Equal("scout-shuffle-effect", game.State.EffectStack[^1].Data["atomicFlow"]);
        PassCurrentResponseWindow(game);

        var affectedChoice = OnlyPrompt(game);
        Assert.Equal(1, affectedChoice.PlayerIndex);
        Assert.Contains(hiddenHand.InstanceId, affectedChoice.ValidChoices);
        Assert.Contains(player.Morale, morale => morale.InstanceId == paidId && morale.Tapped);
    }

    [Fact]
    [Trait("L12Evidence", "trigger:batch6jb-lubu-prepaid-no-refund")]
    public void LuBuReturnsFourDeclaredMoraleBeforeStackAndNegationDoesNotRefundThem()
    {
        var game = Create(9999);
        var player = game.State.Players[0];
        var lubu = Card("S01-0101", "batch6jb-lubu-prepaid");
        lubu.Tapped = true;
        player.Field[0][0] = lubu;
        AddMorale(player, 6, "batch6jb-lubu-prepaid-morale");
        HoldOpponentResponseWindow(game);
        var returned = player.Morale.Take(4).Select(card => card.InstanceId).ToList();

        Invoke(game, "QueueOrPushTriggeredEffect", 0, lubu, "after-attack", "吕布进攻后", null,
            new Dictionary<string, string>());
        var mode = OnlyPrompt(game);
        Assert.Equal(["mode:none", "mode:use"], mode.ValidChoices);
        Assert.DoesNotContain("skip", mode.ValidChoices);
        Assert.Equal(mode.ValidChoices.Count,
            mode.ValidChoices.Select(choice => mode.ChoiceLabels[choice]).Distinct(StringComparer.Ordinal).Count());
        Resolve(game, "mode:use");
        Resolve(game, choices: returned);

        Assert.Equal(2, player.Morale.Count);
        Assert.DoesNotContain(player.Morale, card => returned.Contains(card.InstanceId));
        game.State.EffectStack[^1].Negated = true;
        PassResponses(game);

        Assert.True(lubu.Tapped);
        Assert.Equal(2, player.Morale.Count);
    }

    [Fact]
    [Trait("L12Evidence", "trigger:batch6jb-gustav-prepaid-once")]
    public void GustavPrepaysOrderedGraveCostFinalizesOnceAndNegationDoesNotUndoEither()
    {
        var game = Create(10000);
        var player = game.State.Players[0];
        var gustav = Card("S01-0311", "batch6jb-gustav-prepaid");
        gustav.Tapped = true;
        player.Field[0][0] = gustav;
        var first = Card("S01-0002", "batch6jb-gustav-cost-a");
        var second = Card("S01-0003", "batch6jb-gustav-cost-b");
        player.Graveyard.AddRange([first, second]);
        HoldOpponentResponseWindow(game);

        Invoke(game, "QueueOrPushTriggeredEffect", 0, gustav, "after-attack", "古斯塔夫进攻后", null,
            new Dictionary<string, string>());
        Resolve(game, "mode:use");
        Resolve(game, choices: [second.InstanceId, first.InstanceId]);

        Assert.Empty(player.Graveyard);
        Assert.Equal([second.InstanceId, first.InstanceId], player.Library.TakeLast(2).Select(card => card.InstanceId));
        Assert.Contains($"gustav-ready:{gustav.InstanceId}:{game.State.TurnSerial}", player.UsedAbilities);
        game.State.EffectStack[^1].Negated = true;
        PassResponses(game);

        Assert.True(gustav.Tapped);
        Assert.Empty(player.Graveyard);
    }

    [Fact]
    [Trait("L12Evidence", "trigger:batch6jb-target-invalid-only-cancels-segment")]
    public void MulanTargetInvalidationOnlyCancelsHerDeclaredSegment()
    {
        var game = Create(10001);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var mulan = Card("S01-0108", "batch6jb-mulan-invalid");
        player.Field[0][0] = mulan;
        AddMorale(opponent, 1, "batch6jb-mulan-target", tapped: true);
        HoldOpponentResponseWindow(game);
        var target = opponent.Morale[0];
        game.State.ActivePlayer = 1;

        Invoke(game, "QueueOrPushTriggeredEffect", 0, mulan, "death", "花木兰阵亡", null,
            new Dictionary<string, string>());
        Resolve(game, target.InstanceId);
        opponent.Morale.Remove(target);
        PassResponses(game);

        Assert.Equal(0, target.CannotUntapUntilRound);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("目标", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "trigger:batch6jb-prayer-prepaid-hidden-no-refund")]
    public void PrayerPrivatePreviewPaysBeforeStackAndNegationKeepsTheCostWithoutRevealingTheTopCard()
    {
        var game = Create(10002);
        var player = game.State.Players[0];
        var prayer = Card("S02-0012", "batch6jb-prayer-prepaid");
        player.Hand.Clear();
        player.Hand.Add(prayer);
        AddMorale(player, 4, "batch6jb-prayer-prepaid-morale");
        var disaster = Card("S01-DS01", "batch6jb-prayer-secret");
        game.State.DisasterDeck.Insert(0, disaster);
        HoldOpponentResponseWindow(game);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("playCard", prayer.InstanceId)).Accepted);
        PassResponses(game);
        var consent = OnlyPrompt(game);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: consent.PromptId, Choice: "refuse")).Accepted);
        Resolve(game, "mode:use");
        var cost = OnlyPrompt(game);
        var paidId = cost.ValidChoices[0];
        Resolve(game, paidId);

        Assert.Contains(player.Morale, morale => morale.InstanceId == paidId && morale.Tapped);
        Assert.DoesNotContain(disaster.InstanceId, System.Text.Json.JsonSerializer.Serialize(game.SnapshotFor(1)));
        game.State.EffectStack[^1].Negated = true;
        PassResponses(game);

        Assert.Contains(player.Morale, morale => morale.InstanceId == paidId && morale.Tapped);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Data.GetValueOrDefault("previewCardId") == disaster.InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "trigger:batch6jb-faction-once-decline-release")]
    public void TiantingDeclineCreatesNoEmptyStackAndAcceptFinalizesOnceBeforeResponse()
    {
        var decline = Create(10003);
        var declinePlayer = decline.State.Players[0];
        declinePlayer.UsedAbilities.Add("pending:factionZeroRecovery");
        Invoke(decline, "AfterStackSettled");
        Resolve(decline, "mode:none");
        Assert.Empty(decline.State.EffectStack);
        Assert.DoesNotContain("pending:factionZeroRecovery", declinePlayer.UsedAbilities);
        Assert.DoesNotContain("trigger:factionZeroRecovery", declinePlayer.UsedAbilities);

        var accept = Create(10004);
        var acceptPlayer = accept.State.Players[0];
        HoldOpponentResponseWindow(accept);
        acceptPlayer.UsedAbilities.Add("pending:factionZeroRecovery");
        Invoke(accept, "AfterStackSettled");
        Resolve(accept, "mode:use");
        Assert.Contains("trigger:factionZeroRecovery", acceptPlayer.UsedAbilities);
        Assert.DoesNotContain("pending:factionZeroRecovery", acceptPlayer.UsedAbilities);
        Assert.NotEmpty(accept.State.EffectStack);
    }
}
