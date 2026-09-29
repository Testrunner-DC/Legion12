using System.Text.Json;
using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BattlePlayerResultFlowTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, int deckIndex = 0)
    {
        var game = new L12GameEngine(Catalog, "player-result-flow", "PLAYER-RESULT", seed,
            ["甲", "乙"], [Catalog.DeckAt(deckIndex), Catalog.DeckAt(deckIndex)], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 1;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Morale.Clear();
        }
        return game;
    }

    [Fact]
    [Trait("L12Evidence", "player-result:single-segment-no-public-object")]
    public void PrometheusWithNoCardToShowExplainsItsSkippedSingleSegment()
    {
        var game = Create(94802, 3);
        game.State.ActivePlayer = 0;
        var player = game.State.Players[0];
        var source = Card("S02-05M2", "result-prometheus");
        player.Field[0][0] = source;
        var power = new L12MoraleCard
        {
            InstanceId = "result-prometheus-power", CardId = "S02-05C1", IsGodPower = true,
        };
        player.Morale.Add(power);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "prometheusTopThree")).Accepted);
        PassResponses(game);

        Assert.True(power.Tapped);
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == source.InstanceId));
        Assert.Equal("skipped", result.EffectResultStatus);
        Assert.Contains("原因：牌库为空，没有可展示的牌库顶卡牌",
            result.PlayerLogSemantic?.OutcomeLabel);
        Assert.Null(result.PlayerLogSemantic?.TargetInstanceId);
        Assert.Contains("已支付费用：消耗1神力", result.PlayerLogSemantic?.OutcomeLabel);
        foreach (var events in new[] { game.SnapshotFor(0).RecentEvents,
                     game.SnapshotFor(1).RecentEvents, game.SnapshotForSpectator().RecentEvents })
            Assert.Contains(events, entry => entry.Sequence == result.Sequence
                && entry.PlayerLogSemantic?.OutcomeLabel.Contains("没有可展示的牌库顶卡牌",
                    StringComparison.Ordinal) == true);
    }

    private static L12CardInstance Card(string id, string instanceId)
    {
        var definition = Catalog.Cards[id];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = id, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction,
            ImageUrl = definition.ImageUrl, Cost = definition.Cost ?? 0,
            EffectText = definition.Effect, Traits = [.. definition.Traits],
            Profession = definition.Profession, BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0, DisasterLevel = definition.DisasterLevel ?? 0,
            SummonRound = -1,
        };
    }

    private static void Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(prompt.ValidChoices.Contains(choice),
            $"Prompt {prompt.Kind}/{prompt.Continuation}: requested {choice}; valid {string.Join(",", prompt.ValidChoices)}");
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var guard = 0; guard < 30 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; guard++)
            Resolve(game, "pass");
    }

    [Fact]
    [Trait("L12Evidence", "player-result:cosmos-yin-empty-library")]
    public void CosmosYinEmptyLibraryPublishesOnlyItsOwnFailedSegmentReason()
    {
        var game = Create(94801);
        var owner = game.State.Players[0];
        var actor = game.State.Players[1];
        var counter = Card("S02-0106", "result-yin-counter");
        counter.Hidden = true;
        counter.SetRound = 2;
        owner.Field[1][0] = counter;
        owner.Library.Clear();
        var baseTactic = Card("S01-0219", "result-yin-base");
        actor.Hand.Add(baseTactic);
        for (var index = 0; index < baseTactic.CurrentCost; index++)
            actor.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-02C1", InstanceId = $"result-yin-morale-{index}", Tapped = false,
            });

        Assert.True(game.Handle(1, new L12Command("playCard", baseTactic.InstanceId)).Accepted);
        Resolve(game, "pass");
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(counter.InstanceId, response.ValidChoices);
        Resolve(game, counter.InstanceId);
        PassResponses(game);

        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == counter.InstanceId));
        Assert.Equal("failed", result.EffectResultStatus);
        Assert.Contains("原因：牌库为空，无法展示牌库顶部卡牌",
            result.PlayerLogSemantic?.OutcomeLabel);
        Assert.Null(result.PlayerLogSemantic?.TargetInstanceId);
        foreach (var events in new[] { game.SnapshotFor(0).RecentEvents,
                     game.SnapshotFor(1).RecentEvents, game.SnapshotForSpectator().RecentEvents })
        {
            var visible = Assert.Single(events, entry => entry.Sequence == result.Sequence);
            Assert.Contains("原因：牌库为空，无法展示牌库顶部卡牌",
                visible.PlayerLogSemantic?.OutcomeLabel);
            Assert.DoesNotContain("已支付费用：", visible.PlayerLogSemantic?.OutcomeLabel ?? "");
        }
        Assert.DoesNotContain("result-yin-counter",
            JsonSerializer.Serialize(game.SnapshotForSpectator().RecentEvents
                .Where(entry => entry.Type == "effect-result"
                    && entry.Sequence != result.Sequence)));
    }

    [Fact]
    public void ImmortalGiftDeclinedEntryDoesNotFailItsCompletedDrawSegment()
    {
        var game = Create(94803);
        var source = Card("S01-0223", "result-gift");
        source.Hidden = true;
        game.State.Players[0].Field[1][0] = source;
        game.State.Players[0].Graveyard.Add(Card("S01-0212", "result-gift-guard"));
        game.State.Players[0].Library.Add(Card("S01-0201", "result-gift-draw"));
        var candidate = new L12TriggerCandidate
        {
            CandidateId = "result-gift-trigger", Controller = 0,
            SourceInstanceId = source.InstanceId, SourceCardId = source.CardId,
            SourceName = source.Name, SourceSnapshot = source.Clone(),
            Trigger = "reaction", Text = "【对方进攻后】反击战术",
        };
        var queue = typeof(L12GameEngine).GetMethod("QueueTriggerCandidates",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(queue);
        queue.Invoke(game, [(object)new[] { candidate }]);
        Resolve(game, "mode:use");
        Resolve(game, "mode:none");
        PassResponses(game);

        var results = game.State.Events.Where(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == source.InstanceId))
            .OrderBy(entry => entry.EffectSegmentIndex).ToArray();
        Assert.Equal(["resolved", "declined"], results.Select(entry => entry.EffectResultStatus));
        Assert.DoesNotContain(results, entry => entry.EffectResultStatus == "failed");
    }

    [Fact]
    public void WisdomCodexOpponentAbandonDoesNotBecomeWholeEffectFailure()
    {
        var game = Create(94804);
        var owner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var wisdom = Card("S01-0224", "result-wisdom");
        wisdom.Hidden = true;
        wisdom.SetRound = 0;
        owner.Field[1][0] = wisdom;
        var tactic = Card("S01-0015", "result-wisdom-tactic");
        tactic.OwnerIndex = 1;
        opponent.Hand.Add(tactic);
        for (var index = 0; index < tactic.CurrentCost; index++)
            opponent.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-00C1", InstanceId = $"result-wisdom-morale-{index}",
            });

        Assert.True(game.Handle(1, new L12Command("playCard", tactic.InstanceId)).Accepted);
        while (!Assert.Single(game.State.PendingPrompts).ValidChoices.Contains(wisdom.InstanceId))
            Resolve(game, "pass");
        Resolve(game, wisdom.InstanceId);
        PassResponses(game);
        Resolve(game, "abandon");

        Assert.Contains(game.State.Events, entry => entry.Type == "effect-abandoned");
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "failed"
            && entry.Cards.Any(card => card.InstanceId == wisdom.InstanceId));
    }
}
