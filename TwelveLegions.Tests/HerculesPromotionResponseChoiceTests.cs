using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class HerculesPromotionResponseChoiceTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(0, "promotion-enter")]
    [InlineData(0, "enter")]
    [InlineData(1, "promotion-enter")]
    [InlineData(1, "enter")]
    [Trait("L12Evidence", "bug:BUG-20260912-ebe1a62d")]
    [Trait("L12Evidence", "card:S01-0016,S02-0501,S02-0502")]
    public void AbsoluteDefenseSeparatesBothPromotedHeraclesEntryEffectsAcrossRestore(
        int controller, string selectedTrigger)
    {
        var responder = 1 - controller;
        var game = Create(controller, 202609120 + controller);
        var owner = game.State.Players[controller];
        var opponent = game.State.Players[responder];
        var foundation = Card("S02-0502", $"heracles-foundation-{controller}", controller);
        var promotion = Card("S02-0501", $"heracles-promotion-{controller}", controller);
        var shown = Card("S02-0504", $"heracles-shown-{controller}", controller);
        var target = Card("S01-0002", $"heracles-target-{controller}", responder);
        var absoluteDefense = Card("S01-0016", $"absolute-defense-{responder}", responder);
        var discardCost = Card("S01-0003", $"absolute-defense-cost-{responder}", responder);
        foundation.SummonRound = game.State.Round - 1;
        owner.Field[0][1] = foundation;
        owner.Hand.AddRange([promotion, shown]);
        opponent.Field[0][0] = target;
        absoluteDefense.Hidden = true;
        absoluteDefense.SetRound = game.State.Round - 1;
        opponent.Field[1][0] = absoluteDefense;
        opponent.Hand.Add(discardCost);
        for (var index = 0; index < 2; index++)
            owner.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"heracles-power-{controller}-{index}",
                CardId = "S02-05C1",
                IsGodPower = true,
            });

        AssertAccepted(game.Handle(controller, new L12Command("playCard", promotion.InstanceId)));
        Resolve(game, foundation.InstanceId);
        var triggerOrder = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-batch-order", triggerOrder.Continuation);
        var promotionTrigger = triggerOrder.ValidChoices.Single(id =>
            triggerOrder.Data.GetValueOrDefault($"trigger:{id}") == "promotion-enter");
        var enterTrigger = triggerOrder.ValidChoices.Single(id =>
            triggerOrder.Data.GetValueOrDefault($"trigger:{id}") == "enter");
        AssertAccepted(game.Handle(controller, new L12Command("resolvePrompt",
            PromptId: triggerOrder.PromptId, CardInstanceIds: [enterTrigger, promotionTrigger])));

        while (game.State.PendingPrompts.FirstOrDefault()?.Continuation == "pending-activation")
        {
            var declaration = Assert.Single(game.State.PendingPrompts);
            var choice = declaration.ValidChoices.Contains("mode:use") ? "mode:use"
                : declaration.ValidChoices.Contains(shown.InstanceId) ? shown.InstanceId
                : declaration.ValidChoices.Contains(target.InstanceId) ? target.InstanceId
                : throw new Xunit.Sdk.XunitException(
                    $"未识别的赫拉克勒斯声明步骤：{string.Join(',', declaration.ValidChoices)}");
            Resolve(game, choice);
        }

        var observedWindows = new List<(string Trigger, string StackItemId)>();
        string? selectedText = null;
        while (game.State.PendingPrompts.Count > 0)
        {
            var responsePrompt = Assert.Single(game.State.PendingPrompts);
            if (responsePrompt.Continuation == "pending-activation")
            {
                var declarationChoice = responsePrompt.ValidChoices.Contains("mode:use") ? "mode:use"
                    : responsePrompt.ValidChoices.Contains(shown.InstanceId) ? shown.InstanceId
                    : responsePrompt.ValidChoices.Contains(target.InstanceId) ? target.InstanceId
                    : throw new Xunit.Sdk.XunitException(
                        $"未识别的后续赫拉克勒斯声明步骤：{string.Join(',', responsePrompt.ValidChoices)}");
                Resolve(game, declarationChoice);
                continue;
            }
            Assert.Equal(responder, responsePrompt.PlayerIndex);
            Assert.Equal("response", responsePrompt.Kind);
            Assert.Contains(absoluteDefense.InstanceId, responsePrompt.ValidChoices);
            var current = Assert.Single(game.State.EffectStack);
            Assert.Equal(responsePrompt.StackItemId, current.StackItemId);
            Assert.Equal("S02-0501", current.SourceCardId);
            Assert.Equal(promotion.InstanceId, current.SourceInstanceId);
            Assert.Contains(current.Trigger switch
            {
                "promotion-enter" => "时点：晋升登场",
                "enter" => "时点：登场时",
                _ => throw new Xunit.Sdk.XunitException($"意外的赫拉克勒斯响应时点：{current.Trigger}"),
            }, responsePrompt.Text);
            Assert.DoesNotContain(observedWindows, item => item.Trigger == current.Trigger);
            observedWindows.Add((current.Trigger, current.StackItemId));
            Assert.True(observedWindows.Count <= 2,
                $"赫拉克勒斯的{current.Trigger}响应窗口重复出现");

            var promptId = responsePrompt.PromptId;
            var promptText = responsePrompt.Text;
            var stackItemId = current.StackItemId;
            game = Restore(game);
            responsePrompt = Assert.Single(game.State.PendingPrompts);
            current = Assert.Single(game.State.EffectStack);
            Assert.Equal(promptId, responsePrompt.PromptId);
            Assert.Equal(promptText, responsePrompt.Text);
            Assert.Equal(stackItemId, responsePrompt.StackItemId);
            Assert.Equal(stackItemId, current.StackItemId);
            WriteConsumerFixture(game, responder, controller, selectedTrigger, current.Trigger);

            if (current.Trigger != selectedTrigger)
            {
                Resolve(game, "pass");
                continue;
            }

            selectedText = current.Text;
            Resolve(game, absoluteDefense.InstanceId);
            var payment = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("discard-cost", payment.Kind);
            Assert.Equal("stack-response-discard", payment.Continuation);
            Assert.Equal(current.StackItemId, payment.StackItemId);
            Resolve(game, discardCost.InstanceId);
            continue;
        }

        Assert.NotNull(selectedText);
        Assert.Contains(observedWindows, item => item.Trigger == selectedTrigger);
        if (selectedTrigger == "enter")
        {
            Assert.Equal(["promotion-enter", "enter"],
                observedWindows.Select(item => item.Trigger).ToArray());
            Assert.NotEqual(observedWindows[0].StackItemId, observedWindows[1].StackItemId);
        }
        else
        {
            // 绝对防御在首个窗口用掉后，后续没有合法响应会按现有偏好自动跳过；
            // 不为验收强造空窗口，后续登场时效果由最终双方血量证明已经正常结算。
            Assert.Equal(["promotion-enter"],
                observedWindows.Select(item => item.Trigger).ToArray());
        }

        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        owner = game.State.Players[controller];
        opponent = game.State.Players[responder];
        Assert.Contains(discardCost.InstanceId, opponent.Graveyard.Select(card => card.InstanceId));
        Assert.Equal(shown.InstanceId, owner.Library[0].InstanceId);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-negated"
            && entry.Text.Contains(selectedText, StringComparison.Ordinal));
        if (selectedTrigger == "promotion-enter")
        {
            Assert.Contains(target.InstanceId,
                opponent.Field.SelectMany(row => row).Where(card => card is not null)
                    .Select(card => card!.InstanceId));
            Assert.DoesNotContain(target.InstanceId, opponent.Graveyard.Select(card => card.InstanceId));
            Assert.All(game.State.Players, player => Assert.Equal(player.MaxHp - 1, player.Hp));
        }
        else
        {
            Assert.Contains(target.InstanceId, opponent.Graveyard.Select(card => card.InstanceId));
            Assert.All(game.State.Players, player => Assert.Equal(player.MaxHp, player.Hp));
        }
    }

    private static L12GameEngine Create(int activePlayer, int seed)
    {
        var deck = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "heracles-response-choice", "HERCULES", seed,
            ["甲", "乙"], [deck, deck], skipPreparation: true, autoPassEmptyResponses: true,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = activePlayer;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        game.State.PendingPrompts.Clear();
        game.State.EffectStack.Clear();
        game.State.PendingActivations.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.Hp = player.MaxHp;
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            OwnerIndex = owner,
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

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        AssertAccepted(result);
        return prompt;
    }

    private static void AssertAccepted(CommandResult result)
        => Assert.True(result.Accepted, result.Error);

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: true, concealHiddenResponseAvailability: false);

    private static void WriteConsumerFixture(L12GameEngine game, int responder,
        int controller, string selectedTrigger, string trigger)
    {
        var root = Environment.GetEnvironmentVariable("L12_HERCULES_RESPONSE_SNAPSHOT_ROOT");
        // Only the controller-0 case that passes the first window and responds in the second
        // may publish the paired consumer fixtures. Both files therefore come from one
        // deterministic game and are consecutive recipient projections rather than a mix of theories.
        if (string.IsNullOrWhiteSpace(root) || controller != 0 || selectedTrigger != "enter") return;
        var fullPath = Path.GetFullPath(Path.Combine(root, $"recipient-{trigger}.json"));
        var allowedRoot = Path.GetFullPath(Path.Combine("D:\\GPT\\Legion12\\artifacts",
            "bug-closure-continuation-20261011", "hercules-response-current-20261018"));
        Assert.StartsWith(allowedRoot + Path.DirectorySeparatorChar, fullPath, StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(game.SnapshotFor(responder),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }
}
