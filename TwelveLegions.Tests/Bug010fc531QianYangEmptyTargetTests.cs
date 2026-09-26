using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class Bug010fc531QianYangEmptyTargetTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("L12Evidence", "BUG-010fc531")]
    public void QianYangWithoutAKillTargetStillOffersItsIndependentDrawSegment(bool draw)
    {
        var game = Create(105310 + (draw ? 1 : 0));
        var player = game.State.Players[0];
        var tactic = Card("S02-0105", $"qianyang-empty-{draw}");
        player.Hand.Add(tactic);
        AddMorale(player, 3);
        var initialHandCount = player.Hand.Count;

        var play = game.Handle(0, new L12Command("playCard", tactic.InstanceId));

        Assert.True(play.Accepted, play.Error);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", mode.Continuation);
        Assert.Contains("mode:none", mode.ValidChoices);
        Assert.Contains("mode:draw", mode.ValidChoices);
        Resolve(game, draw ? "mode:draw" : "mode:none");
        if (draw)
        {
            var payment = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("resource-return", payment.Kind);
            Resolve(game, payment.ValidChoices[0]);
        }

        Assert.Contains(tactic, player.Graveyard);
        Assert.Equal(initialHandCount - 1 + (draw ? 1 : 0), player.Hand.Count);
        Assert.Equal(draw ? 2 : 3, player.Morale.Count);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-noop"
            && entry.Text.Contains("没有合法", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("L12Evidence", "BUG-010fc531")]
    public void QianYangWithAKillTargetMayIndependentlyDeclineOrPayForTheDraw(bool draw)
    {
        var game = Create(105320 + (draw ? 1 : 0));
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var tactic = Card("S02-0105", $"qianyang-targeted-{draw}");
        var target = Card("S02-0003", $"qianyang-target-{draw}");
        player.Hand.Add(tactic);
        opponent.Field[0][0] = target;
        AddMorale(player, 3);
        var initialHandCount = player.Hand.Count;

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        Resolve(game, target.InstanceId);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:none", mode.ValidChoices);
        Assert.Contains("mode:draw", mode.ValidChoices);
        Resolve(game, draw ? "mode:draw" : "mode:none");
        if (draw)
        {
            var payment = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("resource-return", payment.Kind);
            Resolve(game, payment.ValidChoices[0]);
        }

        Assert.Contains(target, opponent.Graveyard);
        Assert.Contains(tactic, player.Graveyard);
        Assert.Equal(initialHandCount - 1 + (draw ? 1 : 0), player.Hand.Count);
        Assert.Equal(draw ? 2 : 3, player.Morale.Count);
    }

    [Fact]
    [Trait("L12Evidence", "BUG-010fc531")]
    public void QianYangStillOffersTheIndependentDrawWhenItsDeclaredKillTargetLeaves()
    {
        var game = Create(105330, autoPassEmptyResponses: false);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var tactic = Card("S02-0105", "qianyang-invalid-source");
        var target = Card("S02-0003", "qianyang-invalid-target");
        player.Hand.Add(tactic);
        opponent.Field[0][0] = target;
        AddMorale(player, 3);

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);
        Resolve(game, target.InstanceId);
        opponent.Field[0][0] = null;
        opponent.Graveyard.Add(target);
        PassResponses(game);

        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:none", mode.ValidChoices);
        Assert.Contains("mode:draw", mode.ValidChoices);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("乾坤 阳", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "type:independent-later-segment-after-empty-or-failed-first-effect")]
    public void SameFamilyPlansDoNotRequireTheIndependentLaterSegmentToInheritFirstSegmentSuccess()
    {
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0118"] = "march-kill-segment",
            ["S02-0105"] = "qianyang-draw",
            ["S02-0522"] = "nyx-secondary",
        };
        var scanned = L12CompositeEffectPlans.AllPresentationPlans()
            .Where(plan => plan.Segments.Count > 1
                && plan.Segments[0].PublicTargetKeys is { Length: > 0 }
                && plan.Segments.Skip(1).Any(segment => segment.DeclareAtSegmentStart
                    && !segment.RequiresPreviousSuccess))
            .Select(plan => plan.PlanId)
            .OrderBy(planId => planId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expected.Keys.OrderBy(planId => planId, StringComparer.OrdinalIgnoreCase), scanned);
        var emptyTargetFallthrough = L12CompositeEffectPlans.AllPresentationPlans()
            .Where(plan => plan.Segments.Any(segment => segment.SkipWhenNoLegalTargets))
            .Select(plan => plan.PlanId)
            .OrderBy(planId => planId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(["S02-0105"], emptyTargetFallthrough);

        foreach (var (planId, laterFlow) in expected)
        {
            var segments = L12CompositeEffectPlans.Segments(planId);
            Assert.True(segments.Count >= 2, planId);
            Assert.NotEmpty(segments[0].PublicTargetKeys ?? []);
            var later = Assert.Single(segments, segment => segment.Flow == laterFlow);
            Assert.True(later.DeclareAtSegmentStart, planId);
            Assert.False(later.RequiresPreviousSuccess, planId);
        }
    }

    private static L12GameEngine Create(int seed, bool autoPassEmptyResponses = true)
    {
        var game = new L12GameEngine(Catalog, "bug-010fc531", "BUG010FC531", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: autoPassEmptyResponses, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
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
            SummonRound = -1,
        };
    }

    private static void AddMorale(L12PlayerState player, int count)
    {
        while (player.Morale.Count < count)
        {
            var morale = player.MoraleDeck[0];
            player.MoraleDeck.RemoveAt(0);
            morale.Tapped = false;
            player.Morale.Add(morale);
        }
    }

    private static void Resolve(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var command = choices.Length == 1
            ? new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choices[0])
            : new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [.. choices]);
        var result = game.Handle(prompt.PlayerIndex, command);
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        var safety = 0;
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            Assert.True(safety++ < 20);
            Resolve(game, "pass");
        }
    }
}
