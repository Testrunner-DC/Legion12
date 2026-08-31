using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6ARegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "atomic-review-batch6a", "ATOMIC6A", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
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
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Resolving.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int troops = 0)
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
            BaseTroops = troops > 0 ? troops : definition.Troops ?? 0,
            Troops = troops > 0 ? troops : definition.Troops ?? 0,
            SummonRound = -1,
        };
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    private static void ArrangeLegalHandPlayState(L12GameEngine game, string cardId)
    {
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        player.FreeTacticCount = 1;
        player.Hp = 6;
        player.Hand.Add(Card(cardId, $"atomic6a-source-{cardId}"));
        player.Hand.Add(Card("S01-0002", $"atomic6a-discard-{cardId}", 3000));
        player.Hand.Add(Card("S01-0016", $"atomic6a-counter-a-{cardId}"));
        player.Hand.Add(Card("S01-0019", $"atomic6a-counter-b-{cardId}"));
        player.Field[0][0] = Card("S01-0103", $"atomic6a-own-a-{cardId}", 3000);
        player.Field[0][1] = Card("S01-0104", $"atomic6a-own-b-{cardId}", 3000);
        var enemyFront = Card("S01-0201", $"atomic6a-enemy-a-{cardId}", 2000);
        enemy.Field[0][0] = enemyFront;
        var enemyBack = Card("S01-0202", $"atomic6a-enemy-b-{cardId}", 2000);
        enemy.Field[1][1] = enemyBack;
        enemy.Relic = Card("S01-0117", $"atomic6a-relic-{cardId}");
        player.Graveyard.Add(Card("S01-0309", $"atomic6a-asgard-{cardId}", 3000));
        player.Graveyard.Add(Card("S01-0201", $"atomic6a-sun-{cardId}", 3000));
        player.Graveyard.Add(Card("S01-0001", $"atomic6a-grave-a-{cardId}"));
        player.Graveyard.Add(Card("S01-0002", $"atomic6a-grave-b-{cardId}"));
        player.Graveyard.Add(Card("S01-0003", $"atomic6a-grave-c-{cardId}"));
        for (var index = 0; index < 6; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-01C1",
                InstanceId = $"atomic6a-morale-{cardId}-{index}",
                Tapped = false,
            });
    }

    [Theory]
    [InlineData("S01-0005")]
    [InlineData("S01-0006")]
    [InlineData("S01-0009")]
    [InlineData("S01-0010")]
    [InlineData("S01-0011")]
    [InlineData("S01-0118")]
    [InlineData("S01-0221")]
    [InlineData("S01-0318")]
    [InlineData("S01-0319")]
    [InlineData("S02-0009")]
    [InlineData("S02-0010")]
    [InlineData("S02-0011")]
    [InlineData("S02-0013")]
    [Trait("L12Evidence", "entry:hand-play-public-declaration")]
    public void ReviewedHandTacticsDeclareBeforeTheyLeaveTheHandOrEnterTheStack(string cardId)
    {
        var game = Create(7201);
        ArrangeLegalHandPlayState(game, cardId);
        var source = Assert.Single(game.State.Players[0].Hand, card => card.CardId == cardId);

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Same(source, Assert.Single(game.State.Players[0].Hand, card => card.InstanceId == source.InstanceId));
        Assert.DoesNotContain(source, game.State.Players[0].Resolving);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == source.InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0006")]
    [Trait("L12Evidence", "entry:pre-stack-colon-cost")]
    public void EvilRitualPaysItsDeclaredDiscardBeforeStackEntryAndNegationDoesNotRefundIt()
    {
        var game = Create(7203);
        ArrangeLegalHandPlayState(game, "S01-0006");
        var player = game.State.Players[0];
        var source = Assert.Single(player.Hand, card => card.CardId == "S01-0006");
        var discard = Assert.Single(player.Hand, card => card.InstanceId.StartsWith("atomic6a-discard-", StringComparison.Ordinal));

        Assert.True(game.Handle(0, new L12Command("playCard", source.InstanceId)).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            Choice: discard.InstanceId)).Accepted);

        Assert.Contains(discard, player.Graveyard);
        Assert.DoesNotContain(discard, player.Hand);
        var stackItem = Assert.Single(game.State.EffectStack, item => item.SourceInstanceId == source.InstanceId);
        stackItem.Negated = true;
        PassResponses(game);

        Assert.Contains(discard, player.Graveyard);
    }

    [Fact]
    [Trait("L12Evidence", "entry:free-tactic-common-declaration")]
    [Trait("L12Evidence", "card:S01-0005")]
    public void LiMuFreePlayUsesTheSameHandTacticDeclarationPlanner()
    {
        var game = Create(7204);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var liMu = Card("S02-0102", "atomic6a-limu");
        var volley = Card("S01-0005", "atomic6a-limu-volley");
        player.Hand.Add(liMu);
        player.Library.Clear();
        player.Library.AddRange([volley, Card("S01-0001", "atomic6a-limu-filler")]);
        enemy.Field[0][0] = Card("S01-0103", "atomic6a-limu-target");
        for (var index = 0; index < liMu.Cost; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-01C1",
                InstanceId = $"atomic6a-limu-morale-{index}",
                Tapped = false,
            });

        Assert.True(game.Handle(0, new L12Command("playCard", liMu.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        var reveal = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-limu-reveal", reveal.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: reveal.PromptId,
            Choice: "yes")).Accepted);
        var freePlay = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-limu-tactic", freePlay.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: freePlay.PromptId,
            Choice: "play")).Accepted);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains(volley, player.Resolving);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == volley.InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0320")]
    [Trait("L12Evidence", "entry:independent-target-failure")]
    public void BloodEagleStillMovesTheSurvivingDeclaredTargetWhenItsPeerLeavesTheGraveyard()
    {
        var game = Create(7202);
        var player = game.State.Players[0];
        var counter = Card("S01-0320", "atomic6a-blood-eagle-counter");
        counter.Hidden = true;
        counter.SetRound = 0;
        var fallen = Card("S01-0309", "atomic6a-blood-eagle-fallen");
        var handTarget = Card("S01-0311", "atomic6a-blood-eagle-hand");
        var bottomTarget = Card("S01-0312", "atomic6a-blood-eagle-bottom");
        player.Field[1][0] = counter;
        player.Field[0][0] = fallen;
        player.Graveyard.AddRange([handTarget, bottomTarget]);

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: fallen.InstanceId)).Accepted);
        var triggerOrder = Assert.Single(game.State.PendingPrompts);
        var bloodTrigger = triggerOrder.ValidChoices.Single(id =>
            triggerOrder.Data.GetValueOrDefault(id)?.Contains("复仇血鹰", StringComparison.Ordinal) == true);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: triggerOrder.PromptId,
            CardInstanceIds: [bloodTrigger, .. triggerOrder.ValidChoices
                .Where(id => id != "skip" && id != bloodTrigger)])).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            CardInstanceIds: [handTarget.InstanceId, bottomTarget.InstanceId])).Accepted);

        while (!game.State.EffectStack.Any(item => item.SourceInstanceId == counter.InstanceId
                   && item.Data.GetValueOrDefault("atomicFlow") == "blood-eagle-recover"))
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            var choice = prompt.ValidChoices.Contains("no") ? "no" : "pass";
            Assert.Contains(choice, prompt.ValidChoices);
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                Choice: choice)).Accepted);
        }
        Assert.True(player.Graveyard.Remove(handTarget));
        player.Library.Insert(0, handTarget);
        PassResponses(game);

        Assert.Same(bottomTarget, player.Library.Last());
        Assert.DoesNotContain(bottomTarget, player.Graveyard);
    }
}
