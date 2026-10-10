using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PrometheusPrivatePreviewRegressionTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, int controller = 0)
    {
        var game = new L12GameEngine(Catalog, "prometheus-private-preview", "PROMETHEUS-PRIVATE-PREVIEW",
            seed, ["甲", "乙"], [3, 3], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = controller;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            foreach (var row in player.Field) Array.Clear(row);
        }
        return game;
    }

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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
        };
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static void HoldResponse(L12GameEngine game, int controller)
    {
        var opponent = game.State.Players[1 - controller];
        var counter = Card("S01-0019", $"prometheus-response-{controller}");
        counter.Hidden = true;
        counter.SetRound = 0;
        opponent.Field[1][2] = counter;
        opponent.Field[0][2] = Card("S01-0004", $"prometheus-response-target-{controller}");
    }

    private static L12CardInstance StartPrometheus(L12GameEngine game, int controller,
        params L12CardInstance[] top)
    {
        var player = game.State.Players[controller];
        var source = Card("S02-05M2", $"prometheus-source-{game.State.MatchId}-{controller}");
        player.Field[0][0] = source;
        player.Morale.Add(new L12MoraleCard
        {
            InstanceId = $"prometheus-power-{game.State.MatchId}-{controller}",
            CardId = "S02-05C1",
            IsGodPower = true,
        });
        player.Library.AddRange(top);
        var activation = game.Handle(controller, new L12Command("activateAbility", source.InstanceId,
            Ability: "prometheusTopThree"));
        Assert.True(activation.Accepted, activation.Error);
        PassResponses(game);
        return source;
    }

    private static JsonElement ControllerPrompt(L12GameEngine game, int controller)
    {
        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(controller), WebJson);
        return Assert.Single(snapshot.GetProperty("prompts").EnumerateArray().ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("L12Evidence", "ability:prometheusTopThree;privacy:controller-preview")]
    public void PrometheusControllerSeesAllThreeInspectedCardsButCanChooseOnlyOlympus(int controller)
    {
        var game = Create(97501 + controller, controller);
        var player = game.State.Players[controller];
        var eligible = Card("S02-0502", $"prometheus-eligible-{controller}");
        var otherA = Card("S02-0003", $"prometheus-other-a-{controller}");
        var otherB = Card("S02-0402", $"prometheus-other-b-{controller}");
        StartPrometheus(game, controller, eligible, otherA, otherB);

        var authoritative = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-prometheus-pick", authoritative.Data["action"]);
        Assert.Equal([eligible.InstanceId], authoritative.ValidChoices);
        Assert.DoesNotContain("skip", authoritative.ValidChoices);

        var prompt = ControllerPrompt(game, controller);
        var data = prompt.GetProperty("data");
        Assert.Equal(string.Join('|', eligible.InstanceId, otherA.InstanceId, otherB.InstanceId),
            data.GetProperty("displayCardIds").GetString());
        Assert.Equal([eligible.InstanceId], prompt.GetProperty("validChoices").EnumerateArray()
            .Select(value => value.GetString()!).ToArray());
        foreach (var card in new[] { eligible, otherA, otherB })
        {
            Assert.Equal(card.CardId, data.GetProperty($"{card.InstanceId}:cardId").GetString());
            Assert.Equal("牌库", data.GetProperty($"{card.InstanceId}:zone").GetString());
        }

        var opponentSnapshot = JsonSerializer.Serialize(game.SnapshotFor(1 - controller), WebJson);
        Assert.DoesNotContain(eligible.InstanceId, opponentSnapshot, StringComparison.Ordinal);
        Assert.DoesNotContain(otherA.InstanceId, opponentSnapshot, StringComparison.Ordinal);
        Assert.DoesNotContain(otherB.InstanceId, opponentSnapshot, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("L12Evidence", "ability:prometheusTopThree;boundary:fewer-than-three;eligible:multiple")]
    public void PrometheusDisplaysFewerThanThreeCardsAndKeepsMultipleEligibleCardsSelectable()
    {
        var game = Create(97503);
        var first = Card("S02-0502", "prometheus-short-first");
        var second = Card("S02-0503", "prometheus-short-second");
        StartPrometheus(game, 0, first, second);

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal([first.InstanceId, second.InstanceId], prompt.ValidChoices);
        Assert.Equal(string.Join('|', first.InstanceId, second.InstanceId), prompt.Data["displayCardIds"]);
        Assert.DoesNotContain("skip", prompt.ValidChoices);
        Assert.DoesNotContain(first.InstanceId, JsonSerializer.Serialize(game.SnapshotFor(1), WebJson),
            StringComparison.Ordinal);
        Assert.DoesNotContain(second.InstanceId, JsonSerializer.Serialize(game.SnapshotFor(1), WebJson),
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("L12Evidence", "ability:prometheusTopThree;boundary:zero-eligible;boundary:fewer-than-three")]
    public void PrometheusWithNoEligibleCardShowsTheInspectedCardsOnlyInRequiredPrivateReorder()
    {
        var game = Create(97504);
        var first = Card("S02-0401", "prometheus-zero-first");
        var second = Card("S02-0402", "prometheus-zero-second");
        StartPrometheus(game, 0, first, second);

        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("reorder-order", order.Data["action"]);
        Assert.Equal([first.InstanceId, second.InstanceId], order.ValidChoices);
        Assert.Equal(string.Join('|', first.InstanceId, second.InstanceId), order.Data["displayCardIds"]);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-prometheus-pick");
        var opponent = JsonSerializer.Serialize(game.SnapshotFor(1), WebJson);
        Assert.DoesNotContain(first.InstanceId, opponent, StringComparison.Ordinal);
        Assert.DoesNotContain(second.InstanceId, opponent, StringComparison.Ordinal);

        var resolved = game.Handle(0, new L12Command("resolvePrompt", PromptId: order.PromptId,
            BottomCardInstanceIds: [second.InstanceId, first.InstanceId]));
        Assert.True(resolved.Accepted, resolved.Error);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal([second.InstanceId, first.InstanceId], game.State.Players[0].Library
            .Select(card => card.InstanceId).ToArray());
    }

    [Fact]
    [Trait("L12Evidence", "cards:S02-05M2,S02-0008;faction:ring-universal")]
    public void PrometheusTreatsInspectedCardsAsOlympusWhileTheUniversalRingIsEffective()
    {
        var game = Create(97505);
        var player = game.State.Players[0];
        player.Relic = Card("S02-0008", "prometheus-universal-ring");
        var neutral = Card("S02-0003", "prometheus-ring-neutral");
        var gaotianyuan = Card("S02-0402", "prometheus-ring-gaotianyuan");
        StartPrometheus(game, 0, neutral, gaotianyuan);

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal([neutral.InstanceId, gaotianyuan.InstanceId], prompt.ValidChoices);
        Assert.Equal(string.Join('|', neutral.InstanceId, gaotianyuan.InstanceId),
            prompt.Data["displayCardIds"]);
        Assert.DoesNotContain("skip", prompt.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "ability:prometheusTopThree;invalid-choice;public-selected-only")]
    public void PrometheusRejectsSkipCancelAndIneligibleChoicesThenRevealsOnlyTheAcceptedCard()
    {
        var game = Create(97506);
        var player = game.State.Players[0];
        var eligible = Card("S02-0502", "prometheus-choice-eligible");
        var otherA = Card("S02-0003", "prometheus-choice-other-a");
        var otherB = Card("S02-0402", "prometheus-choice-other-b");
        StartPrometheus(game, 0, eligible, otherA, otherB);
        var prompt = Assert.Single(game.State.PendingPrompts);
        var initialControllerPrompt = ControllerPrompt(game, 0).GetRawText();
        var initialLibrary = player.Library.Select(card => card.InstanceId).ToArray();

        foreach (var illegalChoice in new[] { "skip", "cancel", otherA.InstanceId })
        {
            var rejected = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                Choice: illegalChoice));
            Assert.False(rejected.Accepted);
            Assert.Same(prompt, Assert.Single(game.State.PendingPrompts));
            Assert.Equal(initialLibrary, player.Library.Select(card => card.InstanceId).ToArray());
            Assert.Empty(player.Hand);
            Assert.Equal(initialControllerPrompt, ControllerPrompt(game, 0).GetRawText());
        }

        var accepted = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: eligible.InstanceId));
        Assert.True(accepted.Accepted, accepted.Error);
        Assert.Contains(player.Hand, card => card.InstanceId == eligible.InstanceId);
        var opponentAfterPick = JsonSerializer.Serialize(game.SnapshotFor(1), WebJson);
        Assert.Contains(eligible.InstanceId, opponentAfterPick, StringComparison.Ordinal);
        Assert.DoesNotContain(otherA.InstanceId, opponentAfterPick, StringComparison.Ordinal);
        Assert.DoesNotContain(otherB.InstanceId, opponentAfterPick, StringComparison.Ordinal);

        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal([otherA.InstanceId, otherB.InstanceId], order.ValidChoices);
        Assert.Equal(string.Join('|', otherA.InstanceId, otherB.InstanceId), order.Data["displayCardIds"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: order.PromptId,
            BottomCardInstanceIds: [otherB.InstanceId, otherA.InstanceId])).Accepted);
        var opponentAfterOrder = JsonSerializer.Serialize(game.SnapshotFor(1), WebJson);
        Assert.Contains(eligible.InstanceId, opponentAfterOrder, StringComparison.Ordinal);
        Assert.DoesNotContain(otherA.InstanceId, opponentAfterOrder, StringComparison.Ordinal);
        Assert.DoesNotContain(otherB.InstanceId, opponentAfterOrder, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("L12Evidence", "ability:prometheusTopThree;checkpoint:v2;read-idempotence")]
    public void PrometheusPrivatePreviewSurvivesV2RestoreAndRepeatedReadsWithoutMutation()
    {
        var game = Create(97507);
        var eligible = Card("S02-0502", "prometheus-restore-eligible");
        var otherA = Card("S02-0003", "prometheus-restore-other-a");
        var otherB = Card("S02-0402", "prometheus-restore-other-b");
        StartPrometheus(game, 0, eligible, otherA, otherB);

        var stateBeforeReads = game.SerializeFullState();
        var firstRead = ControllerPrompt(game, 0).GetRawText();
        var secondRead = ControllerPrompt(game, 0).GetRawText();
        Assert.Equal(firstRead, secondRead);
        Assert.Equal(stateBeforeReads, game.SerializeFullState());

        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        var restoredPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal([eligible.InstanceId], restoredPrompt.ValidChoices);
        Assert.Equal(string.Join('|', eligible.InstanceId, otherA.InstanceId, otherB.InstanceId),
            restoredPrompt.Data["displayCardIds"]);
        var restoredStateBeforeReads = game.SerializeFullState();
        Assert.Equal(ControllerPrompt(game, 0).GetRawText(), ControllerPrompt(game, 0).GetRawText());
        Assert.Equal(restoredStateBeforeReads, game.SerializeFullState());
        var opponent = JsonSerializer.Serialize(game.SnapshotFor(1), WebJson);
        Assert.DoesNotContain(eligible.InstanceId, opponent, StringComparison.Ordinal);
        Assert.DoesNotContain(otherA.InstanceId, opponent, StringComparison.Ordinal);
        Assert.DoesNotContain(otherB.InstanceId, opponent, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("L12Evidence", "ability:prometheusTopThree;activation-rejected;negated")]
    public void RejectedOrNegatedPrometheusNeverCreatesThePrivatePreview()
    {
        var rejectedGame = Create(97508);
        var rejectedPlayer = rejectedGame.State.Players[0];
        var rejectedSource = Card("S02-05M2", "prometheus-rejected-source");
        var rejectedTop = Card("S02-0502", "prometheus-rejected-top");
        rejectedPlayer.Field[0][0] = rejectedSource;
        rejectedPlayer.Library.Add(rejectedTop);
        var rejected = rejectedGame.Handle(0, new L12Command("activateAbility", rejectedSource.InstanceId,
            Ability: "prometheusTopThree"));
        Assert.False(rejected.Accepted);
        Assert.Empty(rejectedGame.State.PendingPrompts);
        Assert.Empty(rejectedGame.State.EffectStack);
        Assert.DoesNotContain(rejectedTop.InstanceId,
            JsonSerializer.Serialize(rejectedGame.SnapshotFor(1), WebJson), StringComparison.Ordinal);

        var negatedGame = Create(97509);
        var negatedPlayer = negatedGame.State.Players[0];
        var negatedTop = Card("S02-0502", "prometheus-negated-top");
        HoldResponse(negatedGame, 0);
        var source = Card("S02-05M2", "prometheus-negated-source");
        negatedPlayer.Field[0][0] = source;
        negatedPlayer.Morale.Add(new L12MoraleCard
        {
            InstanceId = "prometheus-negated-power", CardId = "S02-05C1", IsGodPower = true,
        });
        negatedPlayer.Library.Add(negatedTop);
        var activation = negatedGame.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "prometheusTopThree"));
        Assert.True(activation.Accepted, activation.Error);
        Assert.Single(negatedGame.State.EffectStack).Negated = true;
        PassResponses(negatedGame);
        Assert.Empty(negatedGame.State.PendingPrompts);
        Assert.Single(negatedPlayer.Library);
        Assert.Empty(negatedPlayer.Hand);
        Assert.DoesNotContain(negatedTop.InstanceId,
            JsonSerializer.Serialize(negatedGame.SnapshotFor(1), WebJson), StringComparison.Ordinal);
    }
}
