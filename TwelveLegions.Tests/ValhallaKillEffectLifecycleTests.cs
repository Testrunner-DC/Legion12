using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ValhallaKillEffectLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "valhalla-kill-lifecycle", "VALHALLA-KILL-LIFECYCLE",
            seed, ["甲", "乙"], [3, 3], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
            foreach (var row in player.Field)
                Array.Clear(row);
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int? troops = null)
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
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
        };
    }

    private static (L12CardInstance Source, L12CardInstance CostA, L12CardInstance CostB,
        L12CardInstance Low, L12CardInstance Broad) Prepare(L12GameEngine game, bool targets = true,
        bool holdResponse = true)
    {
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        player.Graveyard.Clear();
        var source = Card("S01-03D1", "valhalla-source");
        var costA = Card("S01-0309", "valhalla-cost-a");
        var costB = Card("S01-0311", "valhalla-cost-b");
        var low = Card("S01-0004", "valhalla-low", 1000);
        var broad = Card("S01-0003", "valhalla-broad", 4000);
        player.Relic = source;
        player.Graveyard.AddRange([costA, costB]);
        if (targets)
        {
            enemy.Field[0][0] = low;
            enemy.Field[0][1] = broad;
        }
        if (holdResponse)
        {
            var response = Card("S01-0019", "valhalla-response", 6000);
            response.Hidden = true;
            response.SetRound = 0;
            enemy.Field[1][2] = response;
        }
        return (source, costA, costB, low, broad);
    }

    private static (string CostPromptId, string LowPromptId, string BroadPromptId) Declare(
        L12GameEngine game, L12CardInstance source, L12CardInstance costA, L12CardInstance costB,
        string lowTarget, string broadTarget)
    {
        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "valhallaKill")).Accepted);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: costPrompt.PromptId,
            CardInstanceIds: [costA.InstanceId, costB.InstanceId])).Accepted);
        var lowPrompt = Assert.Single(game.State.PendingPrompts);
        var lowResult = game.Handle(0, new L12Command("resolvePrompt", PromptId: lowPrompt.PromptId,
            Choice: lowTarget));
        Assert.True(lowResult.Accepted, lowResult.Error);
        var broadPrompt = Assert.Single(game.State.PendingPrompts);
        var broadResult = game.Handle(0, new L12Command("resolvePrompt", PromptId: broadPrompt.PromptId,
            Choice: broadTarget));
        Assert.True(broadResult.Accepted, broadResult.Error);
        return (costPrompt.PromptId, lowPrompt.PromptId, broadPrompt.PromptId);
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

    private static L12ActionEvent[] Results(L12GameEngine game)
        => game.State.Events.Where(entry => entry.Type == "effect-result"
                && entry.Cards.Any(card => card.InstanceId == "valhalla-source"))
            .OrderBy(entry => entry.EffectSegmentIndex).ToArray();

    [Fact]
    [Trait("L12Evidence", "ability:valhallaKill")]
    public void CostAndBothKillSegmentsResolveAfterCheckpoint()
    {
        var game = Create(91351);
        var fixture = Prepare(game);
        var prompts = Declare(game, fixture.Source, fixture.CostA, fixture.CostB,
            fixture.Low.InstanceId, fixture.Broad.InstanceId);
        Assert.True(fixture.Source.Tapped);
        Assert.Contains(fixture.CostA, game.State.Players[0].Library);
        Assert.Contains(fixture.CostB, game.State.Players[0].Library);
        var paidCost = Assert.Single(game.State.EffectStack).Data["paidCostSummary"];
        Assert.Contains($"休整〈{fixture.Source.Name}〉", paidCost, StringComparison.Ordinal);
        Assert.Contains($"将墓地中的〈{fixture.CostA.Name}〉置于牌库底部", paidCost,
            StringComparison.Ordinal);
        Assert.Contains($"将墓地中的〈{fixture.CostB.Name}〉置于牌库底部", paidCost,
            StringComparison.Ordinal);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.Equal(paidCost, Assert.Single(game.State.EffectStack).Data["paidCostSummary"]);
        Assert.Equal(paidCost, Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Kind == "response").Data["responsePaidCostSummary"]);

        PassResponses(game);

        Assert.Null(game.State.Players[1].Field[0][0]);
        Assert.Null(game.State.Players[1].Field[0][1]);
        var results = Results(game);
        Assert.Equal(["resolved", "resolved"], results.Select(entry => entry.EffectResultStatus));
        Assert.Equal([1, 2], results.Select(entry => entry.EffectSegmentIndex));
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompts.LowPromptId,
            Choice: fixture.Low.InstanceId)).Accepted);
    }

    [Fact]
    [Trait("L12Evidence", "ability:valhallaKill")]
    public void LowTargetAboveThresholdFailsButBroadTargetStillResolves()
    {
        var game = Create(91352);
        var fixture = Prepare(game);
        Declare(game, fixture.Source, fixture.CostA, fixture.CostB,
            fixture.Low.InstanceId, fixture.Broad.InstanceId);
        fixture.Low.Troops = 2000;

        PassResponses(game);

        Assert.Same(fixture.Low, game.State.Players[1].Field[0][0]);
        Assert.Null(game.State.Players[1].Field[0][1]);
        Assert.Equal(["resolved", "failed"], Results(game).Select(entry => entry.EffectResultStatus));
    }

    [Fact]
    [Trait("L12Evidence", "ability:valhallaKill")]
    public void BroadTargetAboveThresholdFailsAfterLowTargetResolved()
    {
        var game = Create(91353);
        var fixture = Prepare(game);
        Declare(game, fixture.Source, fixture.CostA, fixture.CostB,
            fixture.Low.InstanceId, fixture.Broad.InstanceId);
        fixture.Broad.Troops = 6000;

        PassResponses(game);

        Assert.Null(game.State.Players[1].Field[0][0]);
        Assert.Same(fixture.Broad, game.State.Players[1].Field[0][1]);
        Assert.Equal(["failed", "resolved"], Results(game).Select(entry => entry.EffectResultStatus));
    }

    [Fact]
    [Trait("L12Evidence", "ability:valhallaKill")]
    public void BothTargetsChangingThresholdProduceTwoFailuresWithoutRefundingCost()
    {
        var game = Create(91354);
        var fixture = Prepare(game);
        Declare(game, fixture.Source, fixture.CostA, fixture.CostB,
            fixture.Low.InstanceId, fixture.Broad.InstanceId);
        fixture.Low.Troops = 2000;
        fixture.Broad.Troops = 6000;

        PassResponses(game);

        Assert.Equal(["failed", "failed"], Results(game).Select(entry => entry.EffectResultStatus));
        Assert.True(fixture.Source.Tapped);
        Assert.Contains(fixture.CostA, game.State.Players[0].Library);
        Assert.Contains(fixture.CostB, game.State.Players[0].Library);
    }

    [Fact]
    [Trait("L12Evidence", "ability:valhallaKill")]
    public void NoTargetsStillPaysCostAndPublishesTwoSkippedSegments()
    {
        var game = Create(91355);
        var fixture = Prepare(game, targets: false, holdResponse: false);
        Assert.True(game.Handle(0, new L12Command("activateAbility", fixture.Source.InstanceId,
            Ability: "valhallaKill")).Accepted);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: costPrompt.PromptId,
            CardInstanceIds: [fixture.CostA.InstanceId, fixture.CostB.InstanceId])).Accepted);
        PassResponses(game);

        Assert.True(fixture.Source.Tapped);
        Assert.Contains(fixture.CostA, game.State.Players[0].Library);
        Assert.Contains(fixture.CostB, game.State.Players[0].Library);
        Assert.Equal(["skipped", "skipped"], Results(game).Select(entry => entry.EffectResultStatus));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "ability:valhallaKill")]
    public void OnlyBroadTargetSkipsLowSegmentAndStillResolvesBroadKill()
    {
        var game = Create(91357);
        var fixture = Prepare(game, targets: false, holdResponse: false);
        game.State.Players[1].Field[0][1] = fixture.Broad;
        Assert.True(game.Handle(0, new L12Command("activateAbility", fixture.Source.InstanceId,
            Ability: "valhallaKill")).Accepted);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: costPrompt.PromptId,
            CardInstanceIds: [fixture.CostA.InstanceId, fixture.CostB.InstanceId])).Accepted);
        var broadPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(fixture.Broad.InstanceId, broadPrompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: broadPrompt.PromptId,
            Choice: fixture.Broad.InstanceId)).Accepted);

        PassResponses(game);

        Assert.Null(game.State.Players[1].Field[0][1]);
        Assert.Equal(["resolved", "skipped"], Results(game).Select(entry => entry.EffectResultStatus));
    }

    [Fact]
    [Trait("L12Evidence", "ability:valhallaKill")]
    public void OnlyLowTargetResolvesLowKillAndSkipsDistinctBroadSegment()
    {
        var game = Create(91358);
        var fixture = Prepare(game, targets: false, holdResponse: false);
        game.State.Players[1].Field[0][0] = fixture.Low;
        Declare(game, fixture.Source, fixture.CostA, fixture.CostB,
            fixture.Low.InstanceId, "mode:none");

        PassResponses(game);

        Assert.Null(game.State.Players[1].Field[0][0]);
        Assert.Equal(["skipped", "resolved"], Results(game).Select(entry => entry.EffectResultStatus));
    }

    [Fact]
    [Trait("L12Evidence", "ability:valhallaKill")]
    public void NegationStopsBothKillsAndDoesNotRefundTheGraveCost()
    {
        var game = Create(91356);
        var fixture = Prepare(game);
        Declare(game, fixture.Source, fixture.CostA, fixture.CostB,
            fixture.Low.InstanceId, fixture.Broad.InstanceId);
        Assert.Single(game.State.EffectStack).Negated = true;

        PassResponses(game);

        Assert.Same(fixture.Low, game.State.Players[1].Field[0][0]);
        Assert.Same(fixture.Broad, game.State.Players[1].Field[0][1]);
        Assert.True(fixture.Source.Tapped);
        Assert.Contains(fixture.CostA, game.State.Players[0].Library);
        Assert.Contains(fixture.CostB, game.State.Players[0].Library);
        Assert.Equal("negated", Assert.Single(Results(game)).EffectResultStatus);
    }
}
