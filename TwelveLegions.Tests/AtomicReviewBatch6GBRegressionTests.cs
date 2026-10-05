using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6GBRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var deck = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "atomic-review-batch6gb", "ATOMIC6GB", seed,
            ["甲", "乙"], [deck, deck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.UsedAbilities.Clear();
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
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
            OwnerIndex = 0,
        };
    }

    private static void InvokeVoid(L12GameEngine game, string methodName, params object?[] arguments)
    {
        var method = typeof(L12GameEngine).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(game, arguments);
    }

    private static L12CardInstance PrepareLiMu(L12GameEngine game, params L12CardInstance[] library)
    {
        var liMu = Card("S02-0102", $"batch6gb-limu-{game.State.TurnSerial}-{library.Length}");
        game.State.Players[0].Field[0][0] = liMu;
        game.State.Players[0].Library.AddRange(library);
        InvokeVoid(game, "QueueOrPushTriggeredEffect", 0, liMu, "enter", "【登场时】效果", null, null);
        return liMu;
    }

    private static L12Prompt ResolveOnlyPrompt(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void Declare(L12GameEngine game, string revealMode, string drawMode)
    {
        var reveal = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", reveal.Continuation);
        Assert.Contains("展示牌库顶部", reveal.Text, StringComparison.Ordinal);
        ResolveOnlyPrompt(game, revealMode);
        var draw = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", draw.Continuation);
        Assert.Contains("随后抽取1张牌", draw.Text, StringComparison.Ordinal);
        ResolveOnlyPrompt(game, drawMode);
    }

    private static void PassCurrentStackItem(L12GameEngine game)
    {
        var current = Assert.Single(game.State.EffectStack).StackItemId;
        for (var safety = 0; safety < 100
             && game.State.EffectStack.LastOrDefault()?.StackItemId == current
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            ResolveOnlyPrompt(game, "pass");
    }

    private static void PassOnlyPostSettlementAuthorityEvents(L12GameEngine game)
    {
        for (var safety = 0; safety < 20 && game.State.PendingPrompts.Count > 0; safety++)
        {
            Assert.All(game.State.EffectStack, item => Assert.Equal("authority-event", item.Trigger));
            Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
            ResolveOnlyPrompt(game, "pass");
        }
        Assert.Empty(game.State.EffectStack);
        Assert.Null(game.State.ResponseWindow);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6gb-hidden-top-public-modes")]
    public void LiMuDeclaresOnlyPublicRevealAndDrawModesBeforeAnyStackItem()
    {
        var game = Create(9301);
        var top = Card("S01-0005", "batch6gb-secret-top");

        PrepareLiMu(game, top, Card("S01-0001", "batch6gb-filler"));

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", prompt.Continuation);
        Assert.Contains("展示牌库顶部", prompt.Text, StringComparison.Ordinal);
        Assert.Contains("mode:none", prompt.ValidChoices);
        Assert.Contains("mode:use", prompt.ValidChoices);
        Assert.Empty(game.State.EffectStack);
        var declarationState = JsonSerializer.Serialize(new
        {
            game.State.PendingPrompts,
            game.State.PendingTriggerStackCandidates,
            game.State.Events,
        });
        Assert.DoesNotContain(top.InstanceId, declarationState, StringComparison.Ordinal);
        Assert.DoesNotContain(top.Name, declarationState, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6gb-skip-reveal-direct-draw")]
    public void SkippingRevealStartsWithTheRealDrawSegmentWithoutAnEmptyRevealStack()
    {
        var game = Create(9302);
        var top = Card("S01-0001", "batch6gb-direct-draw");
        PrepareLiMu(game, top);

        Declare(game, "mode:none", "mode:use");

        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("limu-draw", first.Data.GetValueOrDefault("atomicFlow"));
        Assert.Equal("1", first.Data.GetValueOrDefault("compositeSegment"));
        Assert.DoesNotContain(game.State.EffectStack,
            item => item.Data.GetValueOrDefault("atomicFlow") == "limu-reveal");
        Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
        Assert.Same(top, game.State.Players[0].Library[0]);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6gb-both-declined-no-stack")]
    public void DecliningBothOptionalClausesDoesNotCreateAnyEmptyStackItem()
    {
        var game = Create(9303);
        var top = Card("S01-0001", "batch6gb-decline-top");
        PrepareLiMu(game, top);

        Declare(game, "mode:none", "mode:none");

        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Same(top, game.State.Players[0].Library[0]);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "reveal");
    }

    [Fact]
    [Trait("L12Evidence", "ruling:limu-whole-effect-negation-20261005")]
    public void NegatingLiMuStopsBothRevealAndSubsequentDraw()
    {
        var game = Create(9304);
        var top = Card("S01-0001", "batch6gb-negated-top");
        PrepareLiMu(game, top);
        Declare(game, "mode:use", "mode:use");
        var reveal = Assert.Single(game.State.EffectStack);
        Assert.Equal("limu-reveal", reveal.Data.GetValueOrDefault("atomicFlow"));
        reveal.Negated = true;

        PassCurrentStackItem(game);

        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "reveal");
        Assert.Same(top, game.State.Players[0].Library[0]);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.Players[0].Hand);
        Assert.Null(game.State.ResponseWindow);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6gb-reveal-after-response")]
    public void EligibleTopIdentityAppearsOnlyAfterTheRevealSegmentLegallyStarts()
    {
        var game = Create(9305);
        var top = Card("S01-0005", "batch6gb-eligible-top");
        PrepareLiMu(game, top, Card("S01-0001", "batch6gb-after-top"));
        Declare(game, "mode:use", "mode:none");
        Assert.DoesNotContain(top.InstanceId, JsonSerializer.Serialize(game.State.Events), StringComparison.Ordinal);

        PassCurrentStackItem(game);

        var choice = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-limu-tactic", choice.Data.GetValueOrDefault("action"));
        Assert.Equal(top.InstanceId, choice.Data.GetValueOrDefault("previewCardId"));
        Assert.Contains(game.State.Events, entry => entry.Type == "reveal"
            && entry.Cards.Any(card => card.CardId == top.CardId));
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6gb-ineligible-bottom-then-draw")]
    public void IneligibleRevealedTopMovesToBottomThenDrawsWithoutASecondResponseWindow()
    {
        var game = Create(9306);
        var ineligible = Card("S01-0001", "batch6gb-ineligible-top");
        var drawn = Card("S01-0002", "batch6gb-next-card");
        PrepareLiMu(game, ineligible, drawn);
        Declare(game, "mode:use", "mode:use");

        PassCurrentStackItem(game);

        Assert.Contains(drawn, game.State.Players[0].Hand);
        Assert.Same(ineligible, game.State.Players[0].Library[^1]);
        PassOnlyPostSettlementAuthorityEvents(game);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-limu-draw");
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6gb-free-play-common-pending-activation")]
    public void EligibleFreeTacticStillUsesTheCommonHandPlayPendingActivation()
    {
        var game = Create(9307);
        var enemy = game.State.Players[1];
        enemy.Field[0][0] = Card("S01-0103", "batch6gb-volley-target");
        var volley = Card("S01-0005", "batch6gb-volley");
        PrepareLiMu(game, volley, Card("S01-0001", "batch6gb-volley-filler"));
        Declare(game, "mode:use", "mode:use");
        PassCurrentStackItem(game);
        var play = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-limu-tactic", play.Data.GetValueOrDefault("action"));

        ResolveOnlyPrompt(game, "play");

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains(volley, game.State.Players[0].Resolving);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == volley.InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6gb-source-snapshot-single-effect")]
    public void SourceLeavingAfterDeclarationDoesNotSwallowRevealOrDrawSegments()
    {
        var game = Create(9308);
        var top = Card("S01-0001", "batch6gb-source-left-top");
        var liMu = PrepareLiMu(game, top, Card("S01-0002", "batch6gb-source-left-draw"));
        Declare(game, "mode:use", "mode:use");
        game.State.Players[0].Field[0][0] = null;
        game.State.Players[0].Graveyard.Add(liMu);

        PassCurrentStackItem(game);

        Assert.Contains(game.State.Events, entry => entry.Type == "reveal");
        Assert.Contains(game.State.Players[0].Hand, card => card.InstanceId == "batch6gb-source-left-draw");
        PassOnlyPostSettlementAuthorityEvents(game);
    }

    public static IEnumerable<object[]> RealPlayCases()
    {
        foreach (var owner in new[] { 0, 1 })
        foreach (var row in new[] { 0, 1 })
        foreach (var recover in new[] { false, true })
        foreach (var outcome in new[] { "negated", "bottom", "free-play", "child-negated" })
            yield return [owner, row, recover, outcome];
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("L12Evidence", "checkpoint:limu-legacy-scope-absent")]
    public void OldCheckpointWithoutTheScopeMarkerStillUsesTheCurrentWholeEffectRule(bool oldIndependentMarker)
    {
        var game = Create(9330);
        PrepareLiMu(game, Card("S01-0005", "legacy-hidden-top"), Card("S01-0001", "legacy-draw"));
        Declare(game, "mode:use", "mode:use");
        var first = Assert.Single(game.State.EffectStack);
        first.Data.Remove("compositeResponseScope");
        if (oldIndependentMarker) first.Data["preserveIndependentStack"] = "true";
        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.False(Assert.Single(game.State.EffectStack).Data.ContainsKey("compositeResponseScope"));
        Assert.Single(game.State.EffectStack).Negated = true;
        PassCurrentStackItem(game);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.Players[0].Hand);
        Assert.Equal(new[] { "legacy-hidden-top", "legacy-draw" },
            game.State.Players[0].Library.Select(card => card.InstanceId));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "reveal");
    }

    [Theory]
    [MemberData(nameof(RealPlayCases))]
    [Trait("L12Evidence", "ruling:limu-real-play-single-effect-20261005")]
    public void RealLiMuPlayAndActualPitfallKeepTheWholeEffectBoundary(int owner, int row,
        bool recover, string outcome)
    {
        var game = Create(9310 + owner);
        game.State.ActivePlayer = owner;
        game.State.DisasterDeck.Clear();
        var liMu = Card("S02-0102", "real-limu");
        liMu.OwnerIndex = owner;
        var top = Card(outcome == "bottom" ? "S01-0001" : "S01-0005", "real-top");
        top.OwnerIndex = owner;
        var drawn = Card("S01-0002", "real-drawn");
        drawn.OwnerIndex = owner;
        game.State.Players[owner].Hand.Add(liMu);
        game.State.Players[owner].Library.AddRange([top, drawn]);
        for (var i = 0; i < liMu.Cost; i++) game.State.Players[owner].Morale.Add(new L12MoraleCard
        {
            InstanceId = $"real-morale-{i}", CardId = "S01-04C1",
        });
        var target = Card("S01-0103", "real-volley-target");
        target.OwnerIndex = 1 - owner;
        game.State.Players[1 - owner].Field[0][0] = target;
        if (outcome == "negated")
        {
            var pitfall = Card("S01-0018", "real-pitfall");
            pitfall.OwnerIndex = 1 - owner;
            pitfall.Hidden = true;
            game.State.Players[1 - owner].Field[1][0] = pitfall;
        }
        else if (outcome == "child-negated")
        {
            var defense = Card("S01-0016", "real-absolute-defense");
            defense.OwnerIndex = 1 - owner;
            defense.Hidden = true;
            game.State.Players[1 - owner].Field[1][0] = defense;
            var discard = Card("S01-0003", "real-defense-discard");
            discard.OwnerIndex = 1 - owner;
            game.State.Players[1 - owner].Hand.Add(discard);
        }
        L12GameEngine Restore(L12GameEngine current) => !recover ? current
            : L12GameEngine.RestoreCheckpoint(Catalog, current.SerializeFullState(), current.RandomState!.Value,
                current.CardFactSignalSequence, autoPassEmptyResponses: false,
                concealHiddenResponseAvailability: false);
        game = Restore(game);
        var played = game.Handle(owner, new L12Command("playCard", "real-limu", Row: row, Slot: 0));
        Assert.True(played.Accepted, played.Error);
        game = Restore(game);
        ResolveOnlyPrompt(game, "mode:use");
        game = Restore(game);
        ResolveOnlyPrompt(game, "mode:use");
        game = Restore(game);
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("single-effect", first.Data.GetValueOrDefault("compositeResponseScope"));
        Assert.Equal(owner, Assert.Single(game.State.PendingPrompts).PlayerIndex);
        if (outcome == "negated")
        {
            ResolveOnlyPrompt(game, "pass");
            game = Restore(game);
            Assert.Contains("real-pitfall", Assert.Single(game.State.PendingPrompts).ValidChoices);
            ResolveOnlyPrompt(game, "real-pitfall");
        }
        for (var safety = 0; safety < 80 && game.State.PendingPrompts.Count > 0; safety++)
        {
            game = Restore(game);
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.Kind == "response")
            {
                Assert.DoesNotContain(game.State.EffectStack, item =>
                    item.Data.GetValueOrDefault("atomicFlow") == "limu-draw");
                ResolveOnlyPrompt(game, outcome == "child-negated"
                    && game.State.EffectStack[^1].SourceInstanceId == "real-top"
                    && prompt.ValidChoices.Contains("real-absolute-defense") ? "real-absolute-defense" : "pass");
            }
            else if (prompt.Data.GetValueOrDefault("action") == "s2-limu-tactic")
                ResolveOnlyPrompt(game, "play");
            else
            {
                if (prompt.ValidChoices.Contains("real-defense-discard"))
                    ResolveOnlyPrompt(game, "real-defense-discard");
                else
                {
                    Assert.Equal("pending-activation", prompt.Continuation);
                    Assert.Contains("mode:front", prompt.ValidChoices);
                    ResolveOnlyPrompt(game, "mode:front");
                }
            }
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Contains(game.State.Players[owner].Field[row], card => card?.InstanceId == "real-limu");
        Assert.All(game.State.Players[owner].Morale, morale => Assert.True(morale.Tapped));
        if (outcome == "negated")
        {
            Assert.Empty(game.State.Players[owner].Hand);
            Assert.Equal(new[] { "real-top", "real-drawn" },
                game.State.Players[owner].Library.Select(card => card.InstanceId));
            Assert.DoesNotContain(game.State.Events, action => action.Type == "reveal"
                && action.Cards.Any(card => card.InstanceId == "real-top"));
            Assert.Contains(game.State.Players[1 - owner].Graveyard,
                card => card.InstanceId == "real-pitfall");
        }
        else
        {
            Assert.Contains(game.State.Players[owner].Hand, card => card.InstanceId == "real-drawn");
            if (outcome == "bottom")
                Assert.Equal("real-top", Assert.Single(game.State.Players[owner].Library).InstanceId);
            else
            {
                Assert.Contains(game.State.Players[owner].Graveyard, card => card.InstanceId == "real-top");
                var settled = Assert.Single(game.State.Events, action => action.Type == "effect-result"
                    && action.Cards.Any(card => card.InstanceId == "real-top"));
                var draw = Assert.Single(game.State.Events, action => action.Type == "draw");
                Assert.True(settled.Sequence < draw.Sequence, "免费战术必须完整结算后才随后抽牌");
                if (outcome == "child-negated")
                {
                    Assert.Contains(game.State.Players[1 - owner].Graveyard,
                        card => card.InstanceId == "real-absolute-defense");
                    Assert.Equal(target.BaseTroops, game.State.Players[1 - owner].Field[0][0]!.Troops);
                }
            }
        }
    }
}
