using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MengPoSelectedTargetsCurrentFeedbackTests
{
    private static readonly L12Catalog Catalog =
        L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RealMengPoSelectionIsVisibleToEveryRecipientAndSurvivesV2Restore(int actor)
    {
        var game = Create(actor);
        var opponent = 1 - actor;
        var target = Card("S01-0204", $"mengpo-target-{actor}", opponent);
        var nonTarget = Card("S01-0004", $"mengpo-non-target-{actor}", opponent);
        game.State.Players[opponent].Field[0][0] = target;
        game.State.Players[opponent].Field[1][2] = nonTarget;

        var begin = game.Handle(actor, new L12Command("activateAbility", $"master-{actor}",
            Ability: "mengpoSilence"));
        Assert.True(begin.Accepted, begin.Error);
        var selection = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", selection.Continuation);
        Assert.Contains(target.InstanceId, selection.ValidChoices);
        Assert.Contains(nonTarget.InstanceId, selection.ValidChoices);

        var choose = game.Handle(actor, new L12Command("resolvePrompt",
            PromptId: selection.PromptId, Choice: target.InstanceId));
        Assert.True(choose.Accepted, choose.Error);
        ReachResponseWindow(game, actor);

        AssertResponseTargetFeedback(game, actor, target, nonTarget);
        var responseId = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Kind == "response").PromptId;

        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

        Assert.Equal(responseId, Assert.Single(restored.State.PendingPrompts,
            prompt => prompt.Kind == "response").PromptId);
        AssertResponseTargetFeedback(restored, actor, target, nonTarget);
    }

    private static L12GameEngine Create(int actor)
    {
        var baseDeck = Catalog.DeckAt(0);
        var mengPoDeck = new L12PresetDeckDefinition
        {
            Name = "孟婆公开目标当前反馈回归",
            MasterId = "S01-01M2",
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var decks = actor == 0
            ? new[] { mengPoDeck, baseDeck }
            : new[] { baseDeck, mengPoDeck };
        var game = new L12GameEngine(Catalog, "mengpo-selected-target-feedback",
            "MENGPO-TARGET", 102001 + actor, ["甲", "乙"], decks,
            skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = actor;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.UsedAbilities.Clear();
        }
        var owner = game.State.Players[actor];
        owner.MasterTapped = false;
        owner.Morale.Clear();
        owner.Morale.Add(new L12MoraleCard
        {
            InstanceId = $"mengpo-payment-{actor}",
            CardId = "S01-01C1",
        });
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
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
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }

    private static void ReachResponseWindow(L12GameEngine game, int actor)
    {
        for (var step = 0; step < 4; step++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.Kind == "response") return;
            var payment = game.State.Players[actor].Morale
                .Single(card => prompt.ValidChoices.Contains(card.InstanceId));
            var result = game.Handle(actor, new L12Command("resolvePrompt",
                PromptId: prompt.PromptId, Choice: payment.InstanceId));
            Assert.True(result.Accepted, result.Error);
        }
        throw new Xunit.Sdk.XunitException("孟婆声明未在有界费用步骤后进入响应窗");
    }

    private static void AssertResponseTargetFeedback(L12GameEngine game, int actor,
        L12CardInstance target, L12CardInstance nonTarget)
    {
        var response = Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Contains(target.Name, response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(nonTarget.Name, response.Text, StringComparison.Ordinal);

        var views = new[]
        {
            game.SnapshotFor(actor),
            game.SnapshotFor(1 - actor),
            game.SnapshotForSpectator(),
            game.SnapshotForReferee(),
        };
        foreach (var view in views)
        {
            var selected = Assert.Single(view.RecentEvents,
                entry => entry.Type == "target-selected"
                    && entry.PlayerSelectedTargets?.SourceInstanceId == $"master-{actor}");
            var fact = Assert.Single(selected.PlayerSelectedTargets!.Facts);
            Assert.Equal(target.InstanceId, fact.Id);
            Assert.Equal(1 - actor, fact.Owner);
            Assert.Equal((0, 0), (fact.Row, fact.Slot));
            Assert.Equal(target.Name, fact.PublicName);
            Assert.DoesNotContain(selected.PlayerSelectedTargets.Facts,
                candidate => candidate.Id == nonTarget.InstanceId);

            using var document = JsonDocument.Parse(JsonSerializer.Serialize(view));
            var stackItem = Assert.Single(document.RootElement.GetProperty("EffectStack")
                .EnumerateArray().ToArray());
            var labels = stackItem.GetProperty("publicTargetLabels").EnumerateArray()
                .Select(element => element.GetString()).ToArray();
            var label = Assert.Single(labels);
            Assert.Contains(target.Name, label, StringComparison.Ordinal);
            Assert.DoesNotContain(nonTarget.Name, label, StringComparison.Ordinal);
        }
    }
}
