using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed partial class StackResponseChoiceRegressionTests
{
    [Theory]
    [InlineData("S02-0017", 0)]
    [InlineData("S02-0017", 1)]
    [InlineData("S02-0017", null)]
    [InlineData("S02-0017", -1)]
    [L12AbilityEvidence("S02-0017:ability:s2-reaction:0e0643c2b48ae93e", "owner-destination", "private-return", "reconnect-settlement")]
    public void AnonymousReturnUsesOwnerLibraryBeforeFollowingDraw(string cardId, int? owner)
    {
        var game = Create();
        var root = AddEffect(game, "owner-return-root", "authority-event");
        root.Data["eventType"] = "effect-hand-add";
        var selected = Card("S01-0003", "owner-return-private-card", 0);
        selected.OwnerIndex = owner;
        game.State.Players[0].Hand.Add(selected);
        game.State.Players[1].Library.Clear();
        game.State.Players[1].Library.Add(Card("S01-0003", "owner-return-original-top", 1));
        var response = Counter(game, 0, cardId);
        Offer(game);
        Resolve(game, response.InstanceId);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("opponent-hand-card", prompt.Kind);
        Assert.DoesNotContain(selected.InstanceId, prompt.ValidChoices);
        var submitted = Resolve(game, prompt.ValidChoices.Single(choice => choice != "skip"));
        var eventStart = game.State.Events.Count;
        game = Restore(game);
        for (var step = 0; step < 32 && game.State.PendingPrompts.Count > 0; step++)
            Resolve(game, "pass");
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.Players[0].Hand);
        var actualOwner = owner == 1 ? 1 : 0;
        Assert.Equal(actualOwner == 1, game.State.Players[1].Hand.Any(card => card.InstanceId == selected.InstanceId));
        Assert.Equal(actualOwner == 0, game.State.Players[0].Library[0].InstanceId == selected.InstanceId);
        Assert.Equal(actualOwner == 0, game.State.Players[1].Hand.Any(card => card.InstanceId == "owner-return-original-top"));
        Assert.Single(game.State.Players.SelectMany(player => player.Hand.Concat(player.Library).Concat(player.Graveyard)),
            card => card.InstanceId == selected.InstanceId);
        var results = game.State.Events.Skip(eventStart).Where(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == cardId)).ToArray();
        Assert.Equal(2, results.Length);
        Assert.All(results, result => Assert.Equal("resolved", result.EffectResultStatus));
        Assert.All(game.State.Events.Skip(eventStart).Where(entry => entry.Type == "return"), entry =>
        {
            Assert.DoesNotContain(entry.Cards, card => card.InstanceId == selected.InstanceId);
            Assert.DoesNotContain(selected.Name, entry.Text);
        });
        Assert.False(game.Handle(submitted.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: submitted.PromptId, Choice: "pass")).Accepted);
        Assert.Equal(2, game.State.Events.Count(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == cardId)));
    }

    [Theory]
    [InlineData("S01-0020", "normal")]
    [InlineData("S01-0020", "condition-invalid")]
    [InlineData("S01-0020", "empty-library")]
    [InlineData("S01-0020", "negated-draw")]
    [InlineData("S01-0020", "declined")]
    [InlineData("S01-0020", "no-buff-target")]
    [InlineData("S01-0120", "normal")]
    [InlineData("S01-0120", "condition-invalid")]
    [InlineData("S01-0120", "empty-library")]
    [InlineData("S01-0120", "negated-draw")]
    [InlineData("S01-0120", "declined")]
    [InlineData("S01-0120", "missing-attack")]
    [InlineData("S01-0120", "wrong-attack-kind")]
    [L12AbilityEvidence("S01-0020:ability:reaction:f099e096c2d7437b", "conditional-settlement", "declined-branch", "empty-library", "negated-settlement", "reconnect-settlement", "no-target")]
    [L12AbilityEvidence("S01-0120:ability:reaction:0865f062354681b2", "conditional-settlement", "declined-branch", "empty-library", "negated-settlement", "reconnect-settlement", "target-invalidated-settlement")]
    public void ConditionalResponseSegmentsReportActualOutcomesAfterRecovery(string cardId, string outcome)
    {
        var game = Create();
        var root = AddEffect(game, "condition-root", "opponent-attack");
        game.State.PendingDefense = new L12PendingDefense
        {
            AttackerPlayer = 0, AttackerInstanceId = root.SourceInstanceId,
            Target = new L12AttackTarget("master"),
        };
        var player = game.State.Players[1];
        player.Morale.Clear();
        player.Morale.Add(new L12MoraleCard { CardId = "S01-01C1", InstanceId = "condition-cost" });
        player.Library.Clear();
        if (outcome != "empty-library") player.Library.Add(Card("S01-0003", "condition-draw", 1));
        if (cardId == "S01-0020")
        {
            for (var index = 0; index < 5; index++) player.Graveyard.Add(Card("S01-0003", $"condition-grave-{index}", 1));
            if (outcome != "no-buff-target") player.Field[0][0] = Card("S01-0003", "condition-buff", 1, troops: 4000);
        }
        var response = Counter(game, 0, cardId);
        Offer(game);
        Resolve(game, response.InstanceId);
        if (cardId == "S01-0120") Resolve(game, "condition-cost");
        var submitted = Resolve(game, outcome == "declined" ? "mode:none" : "mode:draw");
        if (outcome == "condition-invalid")
        {
            if (cardId == "S01-0020") player.Graveyard.Clear();
            else player.Field[0][0] = Card("S01-0003", "condition-new-front", 1);
        }
        if (outcome == "missing-attack") game.State.EffectStack.Remove(root);
        if (outcome == "wrong-attack-kind")
        {
            game.State.EffectStack.Remove(root);
            game.State.EffectStack.Insert(0, new L12StackItem
            {
                StackItemId = root.StackItemId, Controller = root.Controller,
                SourceInstanceId = root.SourceInstanceId, SourceCardId = root.SourceCardId,
                SourceName = root.SourceName, SourceSnapshot = root.SourceSnapshot,
                Trigger = "enter", Text = root.Text, Negated = true,
            });
        }
        game = Restore(game);
        for (var step = 0; step < 32 && game.State.PendingPrompts.Count > 0; step++)
        {
            var current = game.State.EffectStack.LastOrDefault();
            if (outcome == "negated-draw" && current?.SourceInstanceId == response.InstanceId
                && current.Data.GetValueOrDefault("compositeSegment") == "1") current.Negated = true;
            Resolve(game, "pass");
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        var results = game.State.Events.Where(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == cardId)).OrderBy(entry => entry.EffectSegmentIndex).ToArray();
        var expectedResults = outcome == "declined" ? 1 : 2;
        Assert.Equal(expectedResults, results.Length);
        Assert.Equal(outcome is "missing-attack" or "wrong-attack-kind" ? "failed"
            : outcome == "no-buff-target" ? "skipped" : "resolved", results[0].EffectResultStatus);
        // Declining before activation creates no draw segment or response window.
        if (outcome != "declined")
            Assert.Equal(outcome is "condition-invalid" or "empty-library" ? "failed"
                : outcome == "negated-draw" ? "negated" : "resolved", results[1].EffectResultStatus);
        Assert.Equal(outcome is "normal" or "no-buff-target" or "missing-attack" or "wrong-attack-kind",
            game.State.Players[1].Hand.Any(card => card.InstanceId == "condition-draw"));
        Assert.Equal(outcome == "empty-library" ? 0 : (int?)null, game.State.Winner);
        if (cardId == "S01-0120") Assert.Empty(game.State.Players[1].Morale);
        else if (outcome != "no-buff-target") Assert.Equal(5000, game.State.Players[1].Field[0][0]!.Troops);
        Assert.False(game.Handle(submitted.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: submitted.PromptId, Choice: "pass")).Accepted);
        Assert.Equal(expectedResults, game.State.Events.Count(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == cardId)));
    }

    [Fact]
    public void EveryConditionalDrawCounterUsesTheReviewedPair()
    {
        var matches = Catalog.Cards.Values.Where(card => card.IsCounterTactic
            && System.Text.RegularExpressions.Regex.IsMatch(card.Effect ?? string.Empty, "若.*可抽取1张牌"))
            .Select(card => card.Id).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "S01-0020", "S01-0120" }, matches);
    }

    [Theory]
    [InlineData("S01-0120", "normal")]
    [InlineData("S01-0120", "wrapper-removed")]
    [InlineData("S01-0120", "root-removed")]
    [InlineData("S01-0120", "legacy")]
    [InlineData("S02-0016", "normal")]
    [InlineData("S02-0016", "wrapper-removed")]
    [InlineData("S02-0016", "root-removed")]
    [InlineData("S02-0016", "legacy")]
    [InlineData("S02-0018", "normal")]
    [InlineData("S02-0018", "wrapper-removed")]
    [InlineData("S02-0018", "root-removed")]
    [InlineData("S02-0018", "legacy")]
    [L12AbilityEvidence("S01-0120:ability:reaction:0865f062354681b2", "nested-authority", "reconnect-settlement")]
    [L12AbilityEvidence("S02-0016:ability:s2-reaction:37e38b08d365f0bb", "nested-authority", "reconnect-settlement")]
    [L12AbilityEvidence("S02-0018:ability:s2-reaction:e0e92d0479a94844", "nested-authority", "reconnect-settlement")]
    public void NestedResponseKeepsItsDeclaredRootWhenIntermediateStackChanges(string cardId, string outcome)
    {
        var game = Create();
        var root = AddEffect(game, "nested-root", cardId == "S01-0120" ? "opponent-attack" : "authority-event");
        root.Data["eventType"] = cardId == "S02-0016" ? "non-hand-entry" : "effect-ready";
        if (cardId == "S01-0120")
        {
            root.Negated = false;
            game.State.PendingDefense = new L12PendingDefense
            {
                AttackerPlayer = 0, AttackerInstanceId = root.SourceInstanceId,
                Target = new L12AttackTarget("master"),
            };
            game.State.Players[1].Morale.Clear();
            game.State.Players[1].Morale.Add(new L12MoraleCard { CardId = "S01-01C1", InstanceId = "nested-cost" });
        }
        game.State.Players[0].Field[0][0] = Card(root.SourceCardId, root.SourceInstanceId, 0, troops: 6000);
        var discard = Card("S01-0003", "nested-discard", 0);
        game.State.Players[0].Hand.Add(discard);
        // A minimal response-chain fixture, not a claim about a particular card's nested combo.
        var wrapper = AddEffect(game, "nested-wrapper", "response-negate");
        wrapper.Targets.Add(root.StackItemId);
        var response = Counter(game, 0, cardId);
        Offer(game);
        Resolve(game, response.InstanceId);
        Assert.Equal("response-target", Assert.Single(game.State.PendingPrompts).Kind);
        Resolve(game, wrapper.StackItemId);
        if (cardId == "S01-0120")
        {
            Resolve(game, "nested-cost");
            Resolve(game, "mode:none");
        }
        if (cardId == "S02-0016") Resolve(game, "mode:suppress");
        var responseItem = Assert.Single(game.State.EffectStack, item => item.SourceInstanceId == response.InstanceId);
        Assert.Equal(root.StackItemId, responseItem.Data.GetValueOrDefault("authorityTarget"));
        if (outcome == "legacy") responseItem.Data.Remove("authorityTarget");
        if (outcome == "wrapper-removed") game.State.EffectStack.Remove(wrapper);
        if (outcome == "root-removed") game.State.EffectStack.Remove(root);
        game = Restore(game);
        var restoredRoot = game.State.EffectStack.FirstOrDefault(item => item.StackItemId == root.StackItemId);
        for (var step = 0; step < 32 && game.State.PendingPrompts.Count > 0; step++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            Resolve(game, prompt.Kind == "hand-card" ? discard.InstanceId : "pass");
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        var results = game.State.Events.Where(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == cardId)).OrderBy(entry => entry.EffectSegmentIndex).ToArray();
        Assert.NotEmpty(results);
        Assert.Equal(outcome == "root-removed" ? "failed" : "resolved", results[0].EffectResultStatus);
        if (cardId == "S01-0120" && restoredRoot is not null) Assert.True(restoredRoot.Negated);
        if (cardId == "S02-0016")
        {
            Assert.Equal(outcome == "root-removed" ? 6000 : 3000, game.State.Players[0].Field[0][0]!.Troops);
            Assert.Equal(outcome != "root-removed", restoredRoot?.Data.GetValueOrDefault("suppressEnter") == "true");
        }
        if (cardId == "S02-0018")
            Assert.Equal(outcome != "root-removed", game.State.Players[0].Graveyard.Any(card => card.InstanceId == discard.InstanceId));
    }
}
