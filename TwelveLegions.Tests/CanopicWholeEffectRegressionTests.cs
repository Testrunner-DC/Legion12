using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CanopicWholeEffectRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly string[] Canopics = ["S01-0216", "S01-0217", "S01-0218", "S01-0219", "S01-0220"];

    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var d = Catalog.Cards[id];
        return new L12CardInstance
        {
            InstanceId = instance, CardId = id, OwnerIndex = owner, Name = d.NameZh,
            CardType = d.CardType, IsCounterTactic = d.IsCounterTactic, Faction = d.Faction, ImageUrl = d.ImageUrl,
            Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue, EffectText = d.Effect,
            Traits = [.. d.Traits], Profession = d.Profession,
            BaseTroops = d.Troops ?? 0, Troops = d.Troops ?? 0,
            DisasterLevel = d.DisasterLevel ?? 0, TrialValue = d.TrialValue ?? 0, SummonRound = -1,
        };
    }

    private static L12GameEngine Create(string master, int actor)
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "合成卡诺匹斯整项效果", MasterId = master,
            CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "synthetic-canopic-whole-effect", "LOCAL", 107001 + actor,
            ["合成甲", "合成乙"], [deck, deck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.PendingPrompts.Clear();
        game.State.ActivePlayer = actor;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        game.State.DisasterDeck.Clear();
        foreach (var p in game.State.Players)
        {
            p.Field[0] = new L12CardInstance?[3];
            p.Field[1] = new L12CardInstance?[3];
            p.Hand.Clear(); p.Graveyard.Clear(); p.Library.Clear(); p.Resolving.Clear();
            p.ExtraRelics.Clear(); p.Relic = null; p.SpecialZones.CanopicProgress.Clear();
            p.Morale.Clear(); p.UsedAbilities.Clear();
            for (var i = 0; i < 8; i++) p.Library.Add(Card("S01-0003", $"filler-{p.PlayerIndex}-{i}", p.PlayerIndex));
        }
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game, bool restore)
        => !restore ? game : L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static void Handle(L12GameEngine game, int owner, L12Command command)
    {
        var result = game.Handle(owner, command);
        Assert.True(result.Accepted, result.Error);
    }

    private static void Resolve(L12GameEngine game, string choice)
    {
        var p = Assert.Single(game.State.PendingPrompts);
        Handle(game, p.PlayerIndex, new L12Command("resolvePrompt", PromptId: p.PromptId, Choice: choice));
    }

    private static void Pass(L12GameEngine game)
    {
        for (var n = 0; n < 60 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; n++)
            Resolve(game, "pass");
        Assert.NotEqual("response", game.State.PendingPrompts.FirstOrDefault()?.Kind);
    }

    private static void PassOneWindow(L12GameEngine game)
    {
        var stack = Assert.Single(game.State.PendingPrompts).StackItemId;
        for (var n = 0; n < 2 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; n++)
        {
            Assert.Equal(stack, game.State.PendingPrompts[0].StackItemId);
            Resolve(game, "pass");
        }
        Assert.DoesNotContain(game.State.PendingPrompts, p => p.Kind == "response");
    }

    private static void PassOnlyPostSearchHandAddTiming(L12GameEngine game)
    {
        // A real "card added by an effect" timing is independent of negating a clause
        // inside Canopic Box. Never hide that timing merely to assert one entry window.
        for (var n = 0; n < 4 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; n++)
        {
            var top = Assert.Single(game.State.EffectStack);
            Assert.Equal("authority-event", top.Trigger);
            Assert.Equal("effect-hand-add", top.Data.GetValueOrDefault("eventType"));
            Resolve(game, "pass");
        }
        Assert.Empty(game.State.PendingPrompts);
    }

    private static void SetAbsoluteDefense(L12GameEngine game, int owner)
    {
        var counter = Card("S01-0016", "absolute-defense", owner);
        counter.Hidden = true;
        counter.SetRound = 0;
        game.State.Players[owner].Field[1][2] = counter;
        game.State.Players[owner].Hand.Add(Card("S01-0003", "counter-discard", owner));
    }

    private static void DeclareUntilResponse(L12GameEngine game)
    {
        for (var n = 0; n < 20 && game.State.PendingPrompts.FirstOrDefault()?.Kind != "response"; n++)
        {
            var p = Assert.Single(game.State.PendingPrompts);
            Assert.Contains("canopic-legion-target", p.ValidChoices);
            Resolve(game, "canopic-legion-target");
        }
    }

    private static L12GameEngine Negate(L12GameEngine game, int actor, bool recover)
    {
        if (Assert.Single(game.State.PendingPrompts).PlayerIndex == actor) Resolve(game, "pass");
        Assert.Contains("absolute-defense", Assert.Single(game.State.PendingPrompts).ValidChoices);
        Resolve(game, "absolute-defense");
        game = Restore(game, recover);
        var payment = Assert.Single(game.State.PendingPrompts);
        var command = new L12Command("resolvePrompt", PromptId: payment.PromptId, Choice: "counter-discard");
        Handle(game, payment.PlayerIndex, command);
        Assert.False(game.Handle(payment.PlayerIndex, command).Accepted);
        Assert.Equal("response-negate", game.State.EffectStack[^1].Trigger);
        game = Restore(game, recover);
        Pass(game);
        Assert.Single(game.State.Players[1 - actor].Graveyard, c => c.InstanceId == "counter-discard");
        return game;
    }

    public static IEnumerable<object[]> CanopicFourZeroCases()
    {
        foreach (var actor in new[] { 0, 1 })
        foreach (var recover in new[] { false, true })
        foreach (var negate in new[] { false, true })
        foreach (var extraRelic in new[] { false, true })
            yield return [actor, recover, negate, extraRelic];
    }

    [Theory]
    [MemberData(nameof(CanopicFourZeroCases))]
    [Trait("L12Evidence", "ruling:canopic-four-zero-target-20261011")]
    public void CanopicFourZeroTargetsSkipsOnlyTheTargetClauseAndPreservesWholeEffectNegation(
        int actor, bool recover, bool negate, bool extraRelic)
    {
        var game = Create("S01-02M1", actor);
        var player = game.State.Players[actor];
        if (extraRelic) player.Relic = Card("S01-0117", "existing-relic", actor);
        player.Field[0][0] = Card("S01-0203", "zero-unused-target", actor);
        player.Hand.Add(Card("S01-0220", "zero-canopic", actor));
        player.Hand.Add(Card("S01-0003", "private-zero-hand", actor));
        player.Library.Add(Card("S01-0003", "private-zero-library", actor));
        SetAbsoluteDefense(game, 1 - actor);
        Handle(game, actor, new L12Command("playCard", "zero-canopic"));
        game = Restore(game, recover);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Equal(0, declaration.MinChoose);
        Assert.Equal(1, declaration.MaxChoose); // actual public candidate count clamps printed 2
        Assert.Contains("zero-unused-target", declaration.ValidChoices);
        Assert.DoesNotContain("skip", declaration.ValidChoices);
        Assert.DoesNotContain("mode:none", declaration.ValidChoices);
        var privateView = JsonSerializer.Serialize(game.SnapshotFor(1 - actor));
        Assert.DoesNotContain("private-zero-hand", privateView, StringComparison.Ordinal);
        Assert.DoesNotContain("private-zero-library", privateView, StringComparison.Ordinal);
        var zero = new L12Command("resolvePrompt", PromptId: declaration.PromptId, CardInstanceIds: []);
        Handle(game, actor, zero);
        Assert.False(game.Handle(actor, zero).Accepted);
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("trigger:S01-0220:enter", first.Data["compositePlan"]);
        Assert.Equal("single-effect", first.Data["compositeResponseScope"]);
        Assert.Equal("1", first.Data["compositeSegment"]);
        Assert.Equal("canopic-four-discard", first.Data["atomicFlow"]);
        Assert.Equal(string.Empty, first.Data.GetValueOrDefault("declared:targets"));
        Assert.DoesNotContain(game.State.Events, e => e.Type == "effect-trigger"
            && e.EffectSegmentIndex == 1 && e.Cards.Any(c => c.InstanceId == "zero-canopic"));
        game = Restore(game, recover);
        if (negate) game = Negate(game, actor, recover);
        else PassOneWindow(game);
        player = game.State.Players[actor];
        Assert.Equal(0, player.Field[0][0]!.ImmortalUses);
        Assert.DoesNotContain(game.State.Events, e => e.EffectResultStatus == "failed"
            && e.Cards.Any(c => c.InstanceId == "zero-canopic"));
        Assert.Equal(extraRelic, player.Relic?.InstanceId == "existing-relic");
        var relicCount = player.ExtraRelics.Count(c => c.InstanceId == "zero-canopic")
            + (player.Relic?.InstanceId == "zero-canopic" ? 1 : 0);
        Assert.Equal(negate ? 1 : 0, relicCount);
        Assert.Equal(negate ? 0 : 1, player.Graveyard.Count(c => c.InstanceId == "zero-canopic"));
        Assert.Equal(1, player.Hand.Concat(player.Library).Concat(player.Graveyard)
            .Concat(player.ExtraRelics).Count(c => c.InstanceId == "zero-canopic")
            + (player.Relic?.InstanceId == "zero-canopic" ? 1 : 0));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
        Assert.Null(game.State.ResponseWindow);
        _ = Restore(game, true);
    }

    public static IEnumerable<object[]> CanopicFourZeroSourceLeftCases()
    {
        foreach (var actor in new[] { 0, 1 })
        foreach (var recover in new[] { false, true })
        foreach (var extraRelic in new[] { false, true })
        foreach (var destination in new[] { "hand", "library-bottom" })
            yield return [actor, recover, extraRelic, destination];
    }

    [Theory]
    [MemberData(nameof(CanopicFourZeroSourceLeftCases))]
    [Trait("L12Evidence", "canopic-four-zero-source-zone")]
    public void CanopicFourZeroTargetSourceReturnedBeforeSettlementIsNotDiscardedAgain(
        int actor, bool recover, bool extraRelic, string destination)
    {
        var game = Create("S01-02M1", actor);
        var player = game.State.Players[actor];
        if (extraRelic) player.Relic = Card("S01-0117", "existing-relic", actor);
        player.Field[0][0] = Card("S01-0203", "zero-unused-target", actor);
        player.Hand.Add(Card("S01-0220", "zero-returned-canopic", actor));
        Handle(game, actor, new L12Command("playCard", "zero-returned-canopic"));
        game = Restore(game, recover);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Handle(game, actor, new L12Command("resolvePrompt", PromptId: declaration.PromptId, CardInstanceIds: []));
        Assert.Equal("canopic-four-discard", Assert.Single(game.State.EffectStack).Data["atomicFlow"]);
        var returned = game.HandleGm(new L12GmCommand("returnCardToHand", actor,
            CardInstanceId: "zero-returned-canopic"));
        Assert.True(returned.Accepted, returned.Error);
        if (destination == "library-bottom")
        {
            var moved = game.HandleGm(new L12GmCommand("moveHandCard", actor,
                CardInstanceId: "zero-returned-canopic", Destination: destination));
            Assert.True(moved.Accepted, moved.Error);
        }
        game = Restore(game, recover);
        PassOneWindow(game);
        player = game.State.Players[actor];
        Assert.DoesNotContain(player.Graveyard, c => c.InstanceId == "zero-returned-canopic");
        Assert.DoesNotContain(player.ExtraRelics, c => c.InstanceId == "zero-returned-canopic");
        Assert.NotEqual("zero-returned-canopic", player.Relic?.InstanceId);
        Assert.Equal(extraRelic, player.Relic?.InstanceId == "existing-relic");
        var remaining = destination == "hand" ? player.Hand : player.Library;
        Assert.Single(remaining, c => c.InstanceId == "zero-returned-canopic");
        Assert.Equal(1, player.Hand.Concat(player.Library).Concat(player.Graveyard)
            .Concat(player.ExtraRelics).Count(c => c.InstanceId == "zero-returned-canopic")
            + (player.Relic?.InstanceId == "zero-returned-canopic" ? 1 : 0));
        Assert.Equal(0, player.Field[0][0]!.ImmortalUses);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
        Assert.Null(game.State.ResponseWindow);
        _ = Restore(game, true);
    }

    public static IEnumerable<object[]> CanopicFourRejectedChoiceCases()
    {
        foreach (var actor in new[] { 0, 1 })
        foreach (var recover in new[] { false, true })
        foreach (var method in new[] { "skip", "mode:none", "over-limit", "foreign-target" })
            yield return [actor, recover, method];
    }

    [Theory]
    [MemberData(nameof(CanopicFourRejectedChoiceCases))]
    [Trait("L12Evidence", "canopic-four-zero-declaration-binding")]
    public void CanopicFourRejectsRemovedDeclineAndOutOfRangeChoicesWithoutCancellingDeclaration(
        int actor, bool recover, string method)
    {
        var game = Create("S01-02M1", actor);
        var player = game.State.Players[actor];
        for (var slot = 0; slot < 3; slot++)
            player.Field[0][slot] = Card("S01-0203", $"zero-target-{slot}", actor);
        game.State.Players[1 - actor].Field[0][0] = Card("S01-0203", "foreign-target", 1 - actor);
        player.Hand.Add(Card("S01-0220", "zero-choice-canopic", actor));
        Handle(game, actor, new L12Command("playCard", "zero-choice-canopic"));
        game = Restore(game, recover);
        var declaration = Assert.Single(game.State.PendingPrompts);
        var activationId = Assert.Single(game.State.PendingActivations).ActivationId;
        Assert.Equal(0, declaration.MinChoose);
        Assert.Equal(2, declaration.MaxChoose);
        var invalid = method == "over-limit"
            ? new L12Command("resolvePrompt", PromptId: declaration.PromptId,
                CardInstanceIds: ["zero-target-0", "zero-target-1", "zero-target-2"])
            : new L12Command("resolvePrompt", PromptId: declaration.PromptId, Choice: method);
        Assert.False(game.Handle(actor, invalid).Accepted);
        Assert.Equal(declaration.PromptId, Assert.Single(game.State.PendingPrompts).PromptId);
        Assert.Equal(activationId, Assert.Single(game.State.PendingActivations).ActivationId);
        Assert.Empty(game.State.EffectStack);
        Assert.Equal(1, game.State.Players[actor].ExtraRelics.Count(c => c.InstanceId == "zero-choice-canopic")
            + (game.State.Players[actor].Relic?.InstanceId == "zero-choice-canopic" ? 1 : 0));
        game = Restore(game, recover);
        declaration = Assert.Single(game.State.PendingPrompts);
        Handle(game, actor, new L12Command("resolvePrompt", PromptId: declaration.PromptId, CardInstanceIds: []));
        PassOneWindow(game);
        Assert.Single(game.State.Players[actor].Graveyard, c => c.InstanceId == "zero-choice-canopic");
        Assert.All(game.State.Players[actor].Field[0], card => Assert.Equal(0, card!.ImmortalUses));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
    }

    public static IEnumerable<object[]> CanopicOneMandatoryTargetCases()
    {
        foreach (var actor in new[] { 0, 1 })
        foreach (var recover in new[] { false, true })
        foreach (var negate in new[] { false, true })
            yield return [actor, recover, negate];
    }

    [Theory]
    [MemberData(nameof(CanopicOneMandatoryTargetCases))]
    [Trait("L12Evidence", "ruling:canopic-one-mandatory-single-target-20261011")]
    public void CanopicOneRequiresSingleTargetAndRejectsWholeAbilityDecline(
        int actor, bool recover, bool negate)
    {
        var game = Create("S01-02M1", actor);
        var player = game.State.Players[actor];
        player.Field[0][0] = Card("S01-0203", "canopic-legion-target", actor);
        player.Hand.Add(Card("S01-0217", "mandatory-canopic-one", actor));
        SetAbsoluteDefense(game, 1 - actor);
        Handle(game, actor, new L12Command("playCard", "mandatory-canopic-one"));
        game = Restore(game, recover);
        var declaration = Assert.Single(game.State.PendingPrompts);
        var activationId = Assert.Single(game.State.PendingActivations).ActivationId;
        Assert.Equal(1, declaration.MinChoose);
        Assert.Equal(1, declaration.MaxChoose);
        Assert.DoesNotContain("skip", declaration.ValidChoices);
        Assert.DoesNotContain("mode:none", declaration.ValidChoices);
        Assert.False(game.Handle(actor, new L12Command("resolvePrompt",
            PromptId: declaration.PromptId, Choice: "skip")).Accepted);
        Assert.Equal(declaration.PromptId, Assert.Single(game.State.PendingPrompts).PromptId);
        Assert.Equal(activationId, Assert.Single(game.State.PendingActivations).ActivationId);
        Assert.False(game.Handle(actor, new L12Command("resolvePrompt",
            PromptId: declaration.PromptId, CardInstanceIds: [])).Accepted);
        Assert.Equal(declaration.PromptId, Assert.Single(game.State.PendingPrompts).PromptId);
        Assert.Equal(activationId, Assert.Single(game.State.PendingActivations).ActivationId);
        Assert.Empty(game.State.EffectStack);
        Resolve(game, "canopic-legion-target");
        game = Restore(game, recover);
        if (negate) game = Negate(game, actor, recover);
        else PassOneWindow(game);
        player = game.State.Players[actor];
        Assert.Equal(!negate, player.Field[0][0]!.HasStrongAttack);
        Assert.Equal(negate ? 1 : 0, player.ExtraRelics.Count(c => c.InstanceId == "mandatory-canopic-one")
            + (player.Relic?.InstanceId == "mandatory-canopic-one" ? 1 : 0));
        Assert.Equal(negate ? 0 : 1, player.Graveyard.Count(c => c.InstanceId == "mandatory-canopic-one"));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Null(game.State.ResponseWindow);
    }

    public static IEnumerable<object[]> CanopicFourTwoTargetCases()
    {
        foreach (var actor in new[] { 0, 1 })
        foreach (var recover in new[] { false, true })
        foreach (var negate in new[] { false, true })
            yield return [actor, recover, negate];
    }

    [Theory]
    [MemberData(nameof(CanopicFourTwoTargetCases))]
    [Trait("L12Evidence", "canopic-four-two-target-control")]
    public void CanopicFourTwoTargetsStillShareTheWholeEffectResponse(
        int actor, bool recover, bool negate)
    {
        var game = Create("S01-02M1", actor);
        var player = game.State.Players[actor];
        player.Field[0][0] = Card("S01-0203", "two-target-0", actor);
        player.Field[0][1] = Card("S01-0203", "two-target-1", actor);
        player.Hand.Add(Card("S01-0220", "two-target-canopic", actor));
        SetAbsoluteDefense(game, 1 - actor);
        Handle(game, actor, new L12Command("playCard", "two-target-canopic"));
        game = Restore(game, recover);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, declaration.MinChoose);
        Assert.Equal(2, declaration.MaxChoose);
        Handle(game, actor, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            CardInstanceIds: ["two-target-0", "two-target-1"]));
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("single-effect", first.Data["compositeResponseScope"]);
        Assert.Equal("0", first.Data["compositeSegment"]);
        Assert.Equal("canopic-four", first.Data["atomicFlow"]);
        game = Restore(game, recover);
        if (negate) game = Negate(game, actor, recover);
        else PassOneWindow(game);
        player = game.State.Players[actor];
        Assert.Equal(negate ? 0 : 1, player.Field[0][0]!.ImmortalUses);
        Assert.Equal(negate ? 0 : 1, player.Field[0][1]!.ImmortalUses);
        Assert.Equal(negate ? 1 : 0, player.ExtraRelics.Count(c => c.InstanceId == "two-target-canopic")
            + (player.Relic?.InstanceId == "two-target-canopic" ? 1 : 0));
        Assert.Equal(negate ? 0 : 1, player.Graveyard.Count(c => c.InstanceId == "two-target-canopic"));
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Null(game.State.ResponseWindow);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [Trait("L12Evidence", "canopic-four-settlement-target-revalidation")]
    public void CanopicFourTargetLeavingBeforeSettlementDoesNotGainImmortalityOrBlockDiscard(
        int actor, bool recover)
    {
        var game = Create("S01-02M1", actor);
        var player = game.State.Players[actor];
        player.Field[0][0] = Card("S01-0203", "left-target", actor);
        player.Field[0][1] = Card("S01-0203", "retained-target", actor);
        player.Hand.Add(Card("S01-0220", "target-leave-canopic", actor));
        SetAbsoluteDefense(game, 1 - actor);
        Handle(game, actor, new L12Command("playCard", "target-leave-canopic"));
        game = Restore(game, recover);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Handle(game, actor, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            CardInstanceIds: ["left-target", "retained-target"]));
        var originalWindow = JsonSerializer.Serialize(game.State.ResponseWindow);
        var originalPrompt = JsonSerializer.Serialize(Assert.Single(game.State.PendingPrompts));
        var returned = game.HandleGm(new L12GmCommand("returnCardToHand", actor,
            CardInstanceId: "left-target"));
        Assert.True(returned.Accepted, returned.Error);
        game = Restore(game, recover);
        var canopicStack = Assert.Single(game.State.EffectStack, item => item.SourceInstanceId == "target-leave-canopic");
        var handAddStack = Assert.Single(game.State.DeferredEffectStack, item => item.Trigger == "authority-event");
        Assert.Equal("effect-hand-add", handAddStack.Data["eventType"]);
        Assert.Equal("left-target", handAddStack.Data["targetInstanceId"]);
        Assert.Equal("field", handAddStack.Data["originZone"]);
        Assert.Equal("hand", handAddStack.Data["destinationZone"]);
        var canopicStackId = canopicStack.StackItemId;
        var handAddStackId = handAddStack.StackItemId;
        Assert.Equal(originalWindow, JsonSerializer.Serialize(game.State.ResponseWindow));
        Assert.Equal(originalPrompt, JsonSerializer.Serialize(Assert.Single(game.State.PendingPrompts)));
        Assert.All(game.State.PendingPrompts, prompt =>
        {
            Assert.Equal("response", prompt.Kind);
            Assert.Equal("stack-response", prompt.Continuation);
            Assert.Equal(canopicStackId, prompt.StackItemId);
        });
        // Finish the whole jar entry before the deferred field-to-hand authority window opens.
        // Each window remains distinct; do not blindly pass unknown prompts.
        foreach (var expectedStackId in new[] { canopicStackId, handAddStackId })
        {
            for (var pass = 0; pass < 2; pass++)
            {
                Assert.Equal(expectedStackId, game.State.EffectStack[^1].StackItemId);
                Assert.All(game.State.PendingPrompts, prompt =>
                {
                    Assert.Equal("response", prompt.Kind);
                    Assert.Equal("stack-response", prompt.Continuation);
                    Assert.Contains(prompt.StackItemId, new[] { canopicStackId, handAddStackId });
                });
                var priority = game.State.ResponseWindow!.PriorityPlayer;
                var response = game.State.PendingPrompts.Last(prompt =>
                    prompt.StackItemId == expectedStackId && prompt.PlayerIndex == priority);
                Assert.Contains("pass", response.ValidChoices);
                Handle(game, priority, new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass"));
                game = Restore(game, recover);
            }
            Assert.DoesNotContain(game.State.EffectStack, item => item.StackItemId == expectedStackId);
            if (expectedStackId == canopicStackId)
            {
                Assert.Equal(handAddStackId, Assert.Single(game.State.EffectStack).StackItemId);
                Assert.Empty(game.State.DeferredEffectStack);
                Assert.Equal(1, game.State.Players[actor].Field[0][1]!.ImmortalUses);
            }
        }
        player = game.State.Players[actor];
        Assert.Null(player.Field[0][0]);
        Assert.Equal(0, Assert.Single(player.Hand, c => c.InstanceId == "left-target").ImmortalUses);
        Assert.Equal(1, player.Field[0][1]!.ImmortalUses);
        Assert.Single(player.Graveyard, c => c.InstanceId == "target-leave-canopic");
        Assert.Null(player.Relic);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Null(game.State.ResponseWindow);
    }

    public static IEnumerable<object[]> CanopicCases()
    {
        foreach (var card in Canopics)
        foreach (var owner in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
        foreach (var negate in new[] { false, true })
            yield return [card, owner, restore, negate];
    }

    [Theory]
    [MemberData(nameof(CanopicCases))]
    [Trait("L12Evidence", "ruling:canopic-whole-effect-negation-20261007")]
    public void ActualCanopicPlayAndAbsoluteDefensePreserveTheWholeEntryBoundary(
        string card, int actor, bool recover, bool negate)
    {
        var game = Create("S01-02M1", actor);
        var p = game.State.Players[actor];
        p.Hp = 5;
        p.Relic = Card("S01-0117", "existing-relic", actor);
        p.Field[0][0] = Card("S01-0203", "canopic-legion-target", actor);
        var baseTroops = p.Field[0][0]!.Troops;
        p.Library.Insert(0, Card("S01-0217", "hidden-canopic-search-result", actor));
        p.Hand.Add(Card(card, "played-canopic", actor));
        SetAbsoluteDefense(game, 1 - actor);
        Handle(game, actor, new L12Command("playCard", "played-canopic"));
        DeclareUntilResponse(game);
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("single-effect", first.Data.GetValueOrDefault("compositeResponseScope"));
        Assert.Contains(p.ExtraRelics, c => c.InstanceId == "played-canopic");
        Assert.DoesNotContain("hidden-canopic-search-result", JsonSerializer.Serialize(game.SnapshotFor(1 - actor)),
            StringComparison.Ordinal);
        if (recover) first.Data.Remove("compositeResponseScope"); // old canonical checkpoint relies on the registry
        game = Restore(game, recover);
        if (negate) game = Negate(game, actor, recover);
        else
        {
            PassOneWindow(game);
            if (card == "S01-0216")
            {
                var search = Assert.Single(game.State.PendingPrompts);
                Assert.Contains("hidden-canopic-search-result", search.ValidChoices);
                Assert.DoesNotContain("hidden-canopic-search-result", JsonSerializer.Serialize(game.SnapshotFor(1 - actor)),
                    StringComparison.Ordinal);
                game = Restore(game, recover);
                Assert.DoesNotContain("hidden-canopic-search-result", JsonSerializer.Serialize(game.SnapshotFor(1 - actor)),
                    StringComparison.Ordinal);
                Resolve(game, "hidden-canopic-search-result");
                PassOnlyPostSearchHandAddTiming(game);
            }
        }
        p = game.State.Players[actor];
        Assert.Equal("existing-relic", p.Relic!.InstanceId);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Null(game.State.ResponseWindow);
        if (negate)
        {
            Assert.DoesNotContain(game.State.Events, e => e.Type == "effect-trigger"
                && e.EffectSegmentIndex == 2 && e.Cards.Any(c => c.InstanceId == "played-canopic"));
            Assert.Single(p.ExtraRelics, c => c.InstanceId == "played-canopic");
            Assert.DoesNotContain(p.Graveyard, c => c.InstanceId == "played-canopic");
            Assert.Equal(5, p.Hp);
            Assert.Equal(0, p.TemporaryMorale);
            Assert.Equal(0, p.FreeTacticCount);
            Assert.Equal(baseTroops, p.Field[0][0]!.Troops);
            Assert.False(p.Field[0][0]!.HasStrongAttack);
            Assert.Equal(0, p.Field[0][0]!.ImmortalUses);
            Assert.DoesNotContain(p.Hand, c => c.InstanceId == "hidden-canopic-search-result");
        }
        else
        {
            Assert.Contains(game.State.Events, e => e.Type == "effect-trigger"
                && e.EffectSegmentIndex == 2 && e.Cards.Any(c => c.InstanceId == "played-canopic"));
            Assert.DoesNotContain(p.ExtraRelics, c => c.InstanceId == "played-canopic");
            Assert.Single(p.Graveyard, c => c.InstanceId == "played-canopic");
            if (card == "S01-0216")
            {
                Assert.Equal(6, p.Hp);
                Assert.Single(p.Hand, c => c.InstanceId == "hidden-canopic-search-result");
            }
            if (card == "S01-0217")
            {
                Assert.Equal(baseTroops + 2000, p.Field[0][0]!.Troops);
                Assert.True(p.Field[0][0]!.HasStrongAttack);
            }
            if (card == "S01-0218") Assert.Equal(1, p.FreeTacticCount);
            if (card == "S01-0219") Assert.Equal(2, p.TemporaryMorale);
            if (card == "S01-0220") Assert.Equal(1, p.Field[0][0]!.ImmortalUses);
        }
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [Trait("L12Evidence", "ability:isisCanopic")]
    public void AbsoluteDefenseDoesNotRefundTheThreeGuardsPaidByIsis(int actor, bool recover)
    {
        var game = Create("S01-02M1", actor);
        var p = game.State.Players[actor];
        for (var s = 0; s < 3; s++) p.Field[0][s] = Card("S01-0212", $"guard-{s}", actor);
        p.Graveyard.Add(Card("S01-0219", "isis-canopic", actor));
        SetAbsoluteDefense(game, 1 - actor);
        Handle(game, actor, new L12Command("activateAbility", $"master-{actor}", Ability: "isisCanopic"));
        Resolve(game, "isis-canopic");
        Resolve(game, "mode:draw");
        Assert.All(p.Field[0], c => Assert.Null(c));
        Assert.Equal(3, p.Graveyard.Count(c => c.CardId == "S01-0212"));
        game = Negate(Restore(game, recover), actor, recover);
        p = game.State.Players[actor];
        Assert.Equal(3, p.Graveyard.Count(c => c.CardId == "S01-0212"));
        Assert.Single(p.Graveyard, c => c.InstanceId == "isis-canopic");
        Assert.Empty(p.SpecialZones.CanopicProgress);
        Assert.Empty(p.Hand);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingPrompts);
    }

    public static IEnumerable<object[]> NoTargetCases()
    {
        foreach (var card in new[] { "S01-0217", "S01-0220" })
        foreach (var owner in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
        foreach (var negate in new[] { false, true })
            yield return [card, owner, restore, negate];
    }

    [Theory]
    [MemberData(nameof(NoTargetCases))]
    [Trait("L12Evidence", "entry:canopic-whole-effect-no-target-discard")]
    public void CanopicWithoutTargetsUsesTheCanonicalSingleEffectPlan(
        string card, int actor, bool recover, bool negate)
    {
        var game = Create("S01-02M1", actor);
        game.State.Players[actor].Hand.Add(Card(card, "no-target-canopic", actor));
        SetAbsoluteDefense(game, 1 - actor);
        Handle(game, actor, new L12Command("playCard", "no-target-canopic"));
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal($"trigger:{card}:enter", first.Data["compositePlan"]);
        Assert.Equal("single-effect", first.Data["compositeResponseScope"]);
        game = Restore(game, recover);
        if (negate) game = Negate(game, actor, recover);
        else Pass(game);
        var player = game.State.Players[actor];
        Assert.Null(player.Relic);
        Assert.Equal(negate, player.ExtraRelics.Any(c => c.InstanceId == "no-target-canopic"));
        Assert.Equal(negate ? 1 : 0, player.ExtraRelics.Count(c => c.InstanceId == "no-target-canopic"));
        Assert.DoesNotContain(player.SpecialZones.CanopicProgress, c => c.InstanceId == "no-target-canopic");
        Assert.True(L12StructuredCardSemantics.IgnoresRelicZoneLimit(card));
        Assert.Equal(!negate, player.Graveyard.Any(c => c.InstanceId == "no-target-canopic"));
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingPrompts);
    }

    public static IEnumerable<object[]> SourceLeftCases()
    {
        foreach (var card in Canopics)
        foreach (var actor in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
        foreach (var destination in new[] { "hand", "library-bottom" })
            yield return [card, actor, restore, destination];
    }

    [Theory]
    [MemberData(nameof(SourceLeftCases))]
    [Trait("L12Evidence", "canopic-source-zone-and-lki")]
    public void SourceReturnedBeforeSettlementIsNotDiscardedAgainOrDuplicated(
        string card, int actor, bool recover, string destination)
    {
        var game = Create("S01-02M1", actor);
        var p = game.State.Players[actor];
        p.Hp = 5;
        p.Field[0][0] = Card("S01-0203", "canopic-legion-target", actor);
        p.Library.Insert(0, Card("S01-0217", "hidden-canopic-search-result", actor));
        p.Hand.Add(Card(card, "returned-canopic", actor));
        SetAbsoluteDefense(game, 1 - actor);
        Handle(game, actor, new L12Command("playCard", "returned-canopic"));
        DeclareUntilResponse(game);
        var returned = game.HandleGm(new L12GmCommand("returnCardToHand", actor, CardInstanceId: "returned-canopic"));
        Assert.True(returned.Accepted, returned.Error);
        if (destination == "library-bottom")
        {
            var moved = game.HandleGm(new L12GmCommand("moveHandCard", actor,
                CardInstanceId: "returned-canopic", Destination: destination));
            Assert.True(moved.Accepted, moved.Error);
        }
        game = Restore(game, recover);
        PassOneWindow(game);
        if (card == "S01-0216")
        {
            Assert.Contains("hidden-canopic-search-result", Assert.Single(game.State.PendingPrompts).ValidChoices);
            game = Restore(game, recover);
            Resolve(game, "hidden-canopic-search-result");
            PassOnlyPostSearchHandAddTiming(game);
        }
        p = game.State.Players[actor];
        Assert.True(game.State.PendingPrompts.Count == 0, JsonSerializer.Serialize(game.State.EffectStack));
        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(p.Graveyard, c => c.InstanceId == "returned-canopic");
        Assert.Null(p.Relic);
        Assert.Empty(p.ExtraRelics);
        var remaining = destination == "hand" ? p.Hand : p.Library;
        Assert.Single(remaining, c => c.InstanceId == "returned-canopic");
        if (card == "S01-0216") Assert.Equal(6, p.Hp);
        if (card == "S01-0217") Assert.True(p.Field[0][0]!.HasStrongAttack);
        if (card == "S01-0218") Assert.Equal(1, p.FreeTacticCount);
        if (card == "S01-0219") Assert.Equal(2, p.TemporaryMorale);
        if (card == "S01-0220") Assert.Equal(1, p.Field[0][0]!.ImmortalUses);
        _ = Restore(game, true); // duplicate-zone identities must not break checkpoint recovery
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [Trait("L12Evidence", "card:S01-0112")]
    public void SunWuActualEntryReturnAllowsAbsoluteDefenseToBeCoveredWithZeroActiveMorale(int actor, bool recover)
    {
        var game = Create("S01-01M1", actor);
        var p = game.State.Players[actor];
        p.Hand.Add(Card("S01-0112", "played-sunwu", actor));
        p.Hand.Add(Card("S01-0016", "free-cover-defense", actor));
        for (var i = 0; i < 3; i++) p.Morale.Add(new L12MoraleCard
        {
            CardId = "S01-01C1", InstanceId = $"sunwu-morale-{i}",
        });
        Handle(game, actor, new L12Command("playCard", "played-sunwu", Row: 0, Slot: 0));
        for (var n = 0; n < 30 && game.State.PendingPrompts.Count > 0; n++)
        {
            var q = Assert.Single(game.State.PendingPrompts);
            Resolve(game, q.Kind == "response" ? "pass" : q.ValidChoices.Contains("mode:use") ? "mode:use"
                : q.ValidChoices.First(c => c is not ("skip" or "mode:none" or "cancel")));
        }
        Assert.Equal(1, p.FreeTacticCount);
        Assert.All(p.Morale, m => Assert.True(m.Tapped));
        game = Restore(game, recover);
        p = game.State.Players[actor];
        var handSnapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(actor),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("players")[actor].GetProperty("hand");
        var cover = handSnapshot.EnumerateArray().Single(c => c.GetProperty("instanceId").GetString() == "free-cover-defense");
        Assert.True(cover.GetProperty("isCounterTactic").GetBoolean());
        Assert.Equal(0, cover.GetProperty("playCost").GetInt32());
        Handle(game, actor, new L12Command("playCard", "free-cover-defense", Row: 1, Slot: 0));
        Assert.Equal("free-cover-defense", p.Field[1][0]!.InstanceId);
        Assert.True(p.Field[1][0]!.Hidden);
        Assert.Equal(0, p.FreeTacticCount);
        Assert.All(p.Morale, m => Assert.True(m.Tapped));
    }
}
