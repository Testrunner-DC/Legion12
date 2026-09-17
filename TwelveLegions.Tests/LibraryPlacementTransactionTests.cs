using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

// Saved mid-resolution protocol fixtures exercise every existing placement adapter.
public sealed class LibraryPlacementTransactionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    public static IEnumerable<object[]> Orders()
    {
        yield return ["reorder-order", "all-top-bottom"];
        foreach (var action in new[] { "oiran-order", "festival-bottom-order", "faction-search-order",
            "camp-order", "s2-fortune-bottom-order", "s2-rune-power-bottom-order" })
            yield return [action, "all-bottom"];
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void RealTopDeckAbilityHandlesZeroSingleAndMultipleRemaindersAcrossRestore(int count, bool hit)
    {
        var game = Create("reorder-order", "all-top-bottom");
        game.State.PendingPrompts.Clear(); game.State.EffectStack.Clear(); game.State.IsResolvingStack = false;
        var player = game.State.Players[0];
        player.Library.Clear(); player.Relic = null; player.Morale.Clear();
        var source = Card("S02-05M2", "prometheus");
        player.Field[0][0] = source;
        player.Morale.Add(new L12MoraleCard { InstanceId = "god", CardId = "S02-05C1", IsGodPower = true });
        for (var index = 0; index < count; index++)
            player.Library.Add(Card(hit && index == 0 ? "S02-0502" : "S01-0001", $"view-{index}"));
        Accept(game.Handle(0, new L12Command("activateAbility", source.InstanceId, Ability: "prometheusTopThree")));
        for (var limit = 0; game.State.PendingPrompts.FirstOrDefault()?.Kind == "response" && limit < 10; limit++)
        {
            var response = game.State.PendingPrompts[0];
            Accept(game.Handle(response.PlayerIndex, new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")));
        }
        game = Restore(game);
        if (hit)
        {
            var pick = Assert.Single(game.State.PendingPrompts);
            Assert.Equal(["view-0"], pick.ValidChoices); // even a unique card requires the player's choice
            Accept(game.Handle(0, new L12Command("resolvePrompt", PromptId: pick.PromptId, Choice: "view-0")));
            game = Restore(game);
            Assert.Contains(game.State.Players[0].Hand, card => card.InstanceId == "view-0");
        }
        var remainder = count - (hit ? 1 : 0);
        if (remainder > 0)
        {
            var order = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("all-top-bottom", order.Data["placementMode"]);
            Assert.True(order.IsPrivate);
            Accept(game.Handle(0, new L12Command("resolvePrompt", PromptId: order.PromptId,
                BottomCardInstanceIds: order.ValidChoices.AsEnumerable().Reverse().ToList())));
        }
        // Final ruling: preserve the existing post-add response, including across reconnect.
        if (hit)
        {
            Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
            var added = Assert.Single(game.State.EffectStack);
            Assert.Equal("authority-event", added.Trigger);
            Assert.Equal("effect-hand-add", added.Data["eventType"]);
            game = Restore(game);
            Assert.Single(game.State.Players[0].Hand, card => card.InstanceId == "view-0");
            Assert.DoesNotContain(game.State.Players[0].Library, card => card.InstanceId == "view-0");
            Assert.Equal(added.StackItemId, Assert.Single(game.State.EffectStack).StackItemId);
        }
        for (var limit = 0; game.State.PendingPrompts.FirstOrDefault()?.Kind == "response" && limit < 10; limit++)
        {
            Assert.All(game.State.EffectStack, item => Assert.Equal("authority-event", item.Trigger));
            var response = game.State.PendingPrompts[0];
            Accept(game.Handle(response.PlayerIndex, new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")));
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.False(game.State.IsResolvingStack);
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public void RestoredOrderCompletesExactlyOnceAndDoesNotRevealCards(string action, string mode)
    {
        var game = Restore(Create(action, mode));
        Accept(game.Handle(0, new L12Command("resolvePrompt", PromptId: "placement", BottomCardInstanceIds: ["b", "a"])));
        Assert.Equal(["tail", "b", "a"], game.State.Players[0].Library.Select(card => card.InstanceId));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.False(game.State.IsResolvingStack);
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: "placement", BottomCardInstanceIds: ["b", "a"])).Accepted);
        Assert.DoesNotContain(game.State.Events, entry => entry.Cards.Any(card => card.InstanceId is "a" or "b"));
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public void InvalidPlacementKeepsPromptAndAllowsRetry(string action, string mode)
    {
        var game = Create(action, mode);
        foreach (var invalid in new[] {
            new L12Command("resolvePrompt", PromptId: "placement", TopCardInstanceIds: ["a"], BottomCardInstanceIds: ["b"]),
            new L12Command("resolvePrompt", PromptId: "placement", BottomCardInstanceIds: ["a", "a"]),
            new L12Command("resolvePrompt", PromptId: "placement", BottomCardInstanceIds: ["a"]),
            new L12Command("resolvePrompt", PromptId: "placement", BottomCardInstanceIds: ["a", "tail"]) })
        {
            Assert.False(game.Handle(0, invalid).Accepted);
            Assert.Single(game.State.PendingPrompts);
            Assert.Equal(["a", "b", "tail"], game.State.Players[0].Library.Select(card => card.InstanceId));
        }
        if (mode == "all-bottom")
            Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: "placement", TopCardInstanceIds: ["a", "b"])).Accepted);
        Accept(game.Handle(0, new L12Command("resolvePrompt", PromptId: "placement", BottomCardInstanceIds: ["b", "a"])));
        Assert.Empty(game.State.PendingPrompts);
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public void MissingObjectAfterRestoreFailsSafelyWithoutMovingOtherCards(string action, string mode)
    {
        var game = Create(action, mode);
        var missing = game.State.Players[0].Library[0];
        game.State.Players[0].Library.Remove(missing);
        game.State.Players[0].Graveyard.Add(missing);
        game = Restore(game);
        game.Handle(0, new L12Command("resolvePrompt", PromptId: "placement", BottomCardInstanceIds: ["b", "a"]));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.False(game.State.IsResolvingStack);
        Assert.Equal(["b", "tail"], game.State.Players[0].Library.Select(card => card.InstanceId));
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed");
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public void OrphanedOrderCannotLeavePermanentWaitingState(string action, string mode)
    {
        var game = Create(action, mode);
        game.State.EffectStack.Clear();
        game = Restore(game);
        game.Handle(0, new L12Command("resolvePrompt", PromptId: "placement", BottomCardInstanceIds: ["b", "a"]));
        Assert.Empty(game.State.PendingPrompts);
        Assert.False(game.State.IsResolvingStack);
        Assert.Equal(["a", "b", "tail"], game.State.Players[0].Library.Select(card => card.InstanceId));
    }

    [Theory]
    [InlineData("oiran-pick")]
    [InlineData("festival-hand")]
    [InlineData("festival-grave")]
    [InlineData("faction-search-pick")]
    [InlineData("camp-pick")]
    [InlineData("s2-fortune-artifact")]
    [InlineData("s2-fortune-uesugi")]
    [InlineData("s2-rune-power-pick")]
    [InlineData("s2-prometheus-pick")]
    [InlineData("shanhe-search-pick")]
    [InlineData("starter-telemachus-pick")]
    public void MissingInspectedChoiceCannotThrowOrLeaveAWaitingStack(string action)
    {
        var game = Create("reorder-order", "all-top-bottom");
        game.State.PendingPrompts.Clear();
        game.State.PendingPrompts.Add(new L12Prompt { PromptId = "pick", PlayerIndex = 0, Kind = "card", Text = "选择查看的卡",
            ValidChoices = ["a"], MinChoose = 1, MaxChoose = 1, IsPrivate = true, Continuation = "card-effect",
            StackItemId = "stack", Data = new() { ["action"] = action } });
        game.State.Players[0].Library.RemoveAt(0);
        game = Restore(game);
        Accept(game.Handle(0, new L12Command("resolvePrompt", PromptId: "pick", Choice: "a")));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.False(game.State.IsResolvingStack);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed");
    }

    [Fact]
    public void DuplicateSavedOrderPromptsAreSafelyReconciled()
    {
        var game = Create("reorder-order", "all-top-bottom");
        game.State.PendingPrompts.Add(game.State.PendingPrompts[0]);
        game = Restore(game);
        game.Handle(0, new L12Command("resolvePrompt", PromptId: "placement", BottomCardInstanceIds: ["b", "a"]));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.False(game.State.IsResolvingStack);
        Assert.Equal(["a", "b", "tail"], game.State.Players[0].Library.Select(card => card.InstanceId));
    }

    private static L12GameEngine Create(string action, string mode)
    {
        var game = new L12GameEngine(Catalog, "placement", "PLACEMENT", 919, ["甲", "乙"], [3, 3],
            skipPreparation: true, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.Phase = L12Phase.Main;
        game.State.ActivePlayer = 0;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Library.Clear();
            player.Hand.Clear(); player.Graveyard.Clear(); player.Morale.Clear();
            player.Relic = null; player.ExtraRelics.Clear();
        }
        var owner = game.State.Players[0];
        owner.Relic = Card("S01-0117", "source");
        owner.Library.AddRange(new[] { "a", "b", "tail" }.Select(id => Card("S01-0001", id)));
        var item = new L12StackItem { StackItemId = "stack", Controller = 0, SourceCardId = owner.Relic.CardId,
            SourceInstanceId = "source", SourceName = owner.Relic.Name, Trigger = "active", Text = "牌库顺序" };
        foreach (var key in new[] { "reorder-cards", "oiran-cards", "festival-cards", "faction-search-top", "camp-top", "s2-fortune-cards", "rune-power-top" })
            item.Data[key] = "a|b";
        game.State.EffectStack.Add(item);
        game.State.IsResolvingStack = true;
        game.State.PendingPrompts.Add(new L12Prompt { PromptId = "placement", PlayerIndex = 0, Kind = "order",
            Text = "排列查看的牌", ValidChoices = ["a", "b"], MinChoose = 2, MaxChoose = 2, IsPrivate = true,
            Continuation = "card-effect", StackItemId = "stack", Data = new() { ["action"] = action, ["placementMode"] = mode } });
        return game;
    }
    private static L12CardInstance Card(string cardId, string id)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance { InstanceId = id, CardId = cardId, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction, EffectText = definition.Effect,
            Traits = [.. definition.Traits], Profession = definition.Profession, Cost = definition.Cost ?? 0,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0 };
    }
    private static void Accept(CommandResult result) => Assert.True(result.Accepted, result.Error);
    private static L12GameEngine Restore(L12GameEngine game) => L12GameEngine.RestoreCheckpoint(Catalog,
        game.SerializeFullState(), game.RandomState!.Value, game.CardFactSignalSequence,
        autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
}
