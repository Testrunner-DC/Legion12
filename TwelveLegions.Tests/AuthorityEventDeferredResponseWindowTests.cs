using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AuthorityEventDeferredResponseWindowTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly string[] Producers =
        ["effect-block", "defense-choice-factory", "non-hand-entry", "ready-card", "ready-morale", "hand-add"];
    private sealed class Clock { public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-11T01:00:00Z"); }

    private static L12CardInstance Card(string id, string iid, int actor)
    {
        var d = Catalog.Cards[id];
        return new()
        {
            CardId = id, InstanceId = iid, OwnerIndex = actor, Name = d.NameZh, CardType = d.CardType,
            IsCounterTactic = d.IsCounterTactic, Faction = d.Faction, ImageUrl = d.ImageUrl,
            Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue, EffectText = d.Effect,
            Traits = [.. d.Traits], Profession = d.Profession,
            BaseTroops = d.Troops ?? 0, Troops = d.Troops ?? 0,
            DisasterLevel = d.DisasterLevel ?? 0, TrialValue = d.TrialValue ?? 0, SummonRound = -1,
        };
    }

    private static L12GameEngine Create(int actor, Clock clock, string mode = L12GameEngine.DefaultResponseMode)
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "合成权威时点延后", MasterId = "S01-02M1",
            CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "synthetic-authority-deferred", "LOCAL", 191001 + actor,
            ["合成甲", "合成乙"], [deck, deck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2,
            utcNow: () => clock.Now, responseModes: [mode, mode]);
        game.State.PendingPrompts.Clear();
        game.State.ActivePlayer = actor; game.State.FirstPlayer = 0;
        game.State.Round = 2; game.State.TurnSerial = 3; game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null; game.State.DisasterDeck.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3]; player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Resolving.Clear();
            player.ExtraRelics.Clear(); player.Relic = null; player.Morale.Clear(); player.UsedAbilities.Clear();
        }
        var owner = game.State.Players[actor];
        owner.Relic = Card("S01-0219", "factory-source", actor);
        owner.Field[0][0] = Card("S01-0003", "ready-target", actor);
        owner.Field[0][0]!.Tapped = true;
        owner.Field[0][1] = Card("S01-0003", "entry-target", actor);
        owner.Morale.Add(new L12MoraleCard { InstanceId = "ready-morale", CardId = "S01-02C1", Tapped = true });
        owner.Hand.Add(Card("S01-0003", "unrelated-private-hand", actor));
        owner.Library.Add(Card("S01-0003", "unrelated-private-library", actor));
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game, bool restore, Clock clock)
        => !restore ? game : L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, utcNow: () => clock.Now);

    private static object? Invoke(L12GameEngine game, string name, params object?[] args)
        => typeof(L12GameEngine).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, args);

    private static void Handle(L12GameEngine game, int actor, L12Command command)
    {
        var result = game.Handle(actor, command);
        Assert.True(result.Accepted, result.Error);
    }

    private static void Pass(L12GameEngine game, string stackId)
    {
        Assert.Equal(stackId, game.State.EffectStack[^1].StackItemId);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", prompt.Kind);
        Assert.Equal("stack-response", prompt.Continuation);
        Assert.Equal(stackId, prompt.StackItemId);
        Assert.Equal(game.State.ResponseWindow!.PriorityPlayer, prompt.PlayerIndex);
        Handle(game, prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
    }

    private static string StartParent(L12GameEngine game, int actor)
    {
        game.State.Players[actor].Hand.Add(Card("S01-0219", "deferred-parent", actor));
        Handle(game, actor, new L12Command("playCard", "deferred-parent"));
        var parent = Assert.Single(game.State.EffectStack);
        Assert.Equal("single-effect", parent.Data["compositeResponseScope"]);
        return parent.StackItemId;
    }

    private static L12StackItem Queue(L12GameEngine game, int actor, string producer, string suffix = "")
    {
        var owner = game.State.Players[actor];
        var source = owner.Relic!;
        switch (producer)
        {
            case "effect-block":
                var response = new L12StackItem
                {
                    StackItemId = "effect-block-producer" + suffix, Controller = actor,
                    SourceInstanceId = source.InstanceId, SourceCardId = source.CardId, SourceName = source.Name,
                    Trigger = "response-block", Text = "合成效果抵挡生产者",
                };
                foreach (var pair in new Dictionary<string, string>
                {
                    ["effectBlock"] = "true", ["effectBlockCombatId"] = "synthetic-combat",
                    ["effectBlockAttackStackItemId"] = "synthetic-attack", ["effectBlockAttackerPlayer"] = (1 - actor).ToString(),
                    ["effectBlockAttackerInstanceId"] = "entry-target", ["effectBlockTargetType"] = "master",
                    ["effectBlockTargetInstanceId"] = "",
                }) response.Data[pair.Key] = pair.Value;
                Invoke(game, "QueueEffectBlockAuthorityEvent", response, source);
                break;
            case "defense-choice-factory":
                // ResolveDefense rejects while a response/stack exists. This is its shared factory shape,
                // not a claim that the public command may bypass that normal interaction guard.
                Invoke(game, "QueueAuthorityEvent", "defense", actor, source, "合成抵挡选择生产者",
                    actor, null, null, null, false, new Dictionary<string, string>
                    { ["action"] = "block", ["blockIds"] = "", ["supportIds"] = "" }, true);
                break;
            case "non-hand-entry":
                Invoke(game, "QueueNonHandEntry", actor, owner.Field[0][1]!, "graveyard");
                break;
            case "ready-card":
                Invoke(game, "ReadyCardByEffect", actor, source, owner.Field[0][0]!, "合成转活跃", null);
                break;
            case "ready-morale":
                Invoke(game, "ReadyMoraleByEffect", actor, source, owner.Morale.Single(), "合成士气转活跃");
                break;
            case "hand-add":
                Invoke(game, "AddCardToHandByEffect", owner,
                    Card("S02-0620", "private-deferred-subject" + suffix, actor), "library", "合成私密入手");
                break;
            default: throw new InvalidOperationException("Unknown synthetic producer.");
        }
        return Assert.Single(game.State.EffectStack.Concat(game.State.DeferredEffectStack),
            item => item.Trigger == "authority-event" && item.Data["eventId"] == game.State.AuthorityEvents[^1].EventId);
    }

    private static void AssertPrivacy(L12GameEngine game, int actor, params string[] privateIds)
    {
        var live = JsonSerializer.Serialize(game.SnapshotFor(1 - actor));
        var raw = JsonSerializer.SerializeToElement(game.State);
        var recorded = (JsonElement)typeof(MatchRecorder).GetMethod("SanitizeRecordedState",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [raw, 1 - actor])!;
        foreach (var id in privateIds.Concat(new[] { "unrelated-private-hand", "unrelated-private-library" }))
        {
            Assert.DoesNotContain(id, live, StringComparison.Ordinal);
            Assert.DoesNotContain(id, recorded.GetRawText(), StringComparison.Ordinal);
        }
    }

    private static void AssertClean(L12GameEngine game)
    {
        Assert.Empty(game.State.PendingPrompts); Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates); Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack); Assert.Null(game.State.ResponseWindow);
    }

    public static IEnumerable<object[]> ActiveCases()
    {
        foreach (var producer in Producers)
        foreach (var actor in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
        foreach (var alreadyPassed in new[] { 0, 1 })
            yield return [producer, actor, restore, alreadyPassed];
    }

    [Theory]
    [MemberData(nameof(ActiveCases))]
    [Trait("L12Evidence", "ruling:authority-current-window-deferred-20261011")]
    public void SixProducerShapesPreserveTheCurrentWindowAndResolveOnlyAfterItsWholeParent(
        string producer, int actor, bool restore, int alreadyPassed)
    {
        var clock = new Clock();
        var game = Create(actor, clock);
        var parentId = StartParent(game, actor);
        if (alreadyPassed == 1) Pass(game, parentId);
        game = Restore(game, restore, clock);
        var window = game.State.ResponseWindow!;
        var beforeWindow = JsonSerializer.Serialize(window);
        var beforePrompt = JsonSerializer.Serialize(Assert.Single(game.State.PendingPrompts));
        var queued = Queue(game, actor, producer);
        Assert.Same(window, game.State.ResponseWindow);
        Assert.Equal(beforeWindow, JsonSerializer.Serialize(game.State.ResponseWindow));
        Assert.Equal(beforePrompt, JsonSerializer.Serialize(Assert.Single(game.State.PendingPrompts)));
        Assert.Equal(parentId, Assert.Single(game.State.EffectStack).StackItemId);
        Assert.Equal(queued.StackItemId, Assert.Single(game.State.DeferredEffectStack).StackItemId);
        Assert.False(game.State.AuthorityEvents.Single(item => item.EventId == queued.Data["eventId"]).Resolved);
        var authorityId = queued.StackItemId;
        var eventId = queued.Data["eventId"];
        if (producer == "hand-add") AssertPrivacy(game, actor, "private-deferred-subject");
        game = Restore(game, restore, clock);
        Assert.Equal(beforeWindow, JsonSerializer.Serialize(game.State.ResponseWindow));
        Assert.Equal(beforePrompt, JsonSerializer.Serialize(Assert.Single(game.State.PendingPrompts)));
        for (var n = alreadyPassed; n < 2; n++) { Pass(game, parentId); game = Restore(game, restore, clock); }
        Assert.Equal(authorityId, Assert.Single(game.State.EffectStack).StackItemId);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Single(game.State.Players[actor].Graveyard, card => card.InstanceId == "deferred-parent");
        Assert.Equal(2, game.State.Players[actor].TemporaryMorale);
        if (producer == "hand-add") AssertPrivacy(game, actor, "private-deferred-subject");
        for (var n = 0; n < 2; n++) { Pass(game, authorityId); game = Restore(game, restore, clock); }
        Assert.True(game.State.AuthorityEvents.Single(item => item.EventId == eventId).Resolved);
        if (producer == "ready-card") Assert.False(game.State.Players[actor].Field[0][0]!.Tapped);
        if (producer == "ready-morale") Assert.False(game.State.Players[actor].Morale.Single().Tapped);
        AssertClean(game);
    }

    public static IEnumerable<object[]> ProducerControlCases()
    {
        foreach (var producer in Producers)
        foreach (var actor in new[] { 0, 1 })
        foreach (var restore in new[] { false, true })
            yield return [producer, actor, restore];
    }

    [Theory]
    [MemberData(nameof(ProducerControlCases))]
    [Trait("L12Evidence", "authority-idle-immediate-control")]
    public void SixProducerShapesWithoutAnExistingWindowStillOpenTheirOwnWindow(
        string producer, int actor, bool restore)
    {
        var clock = new Clock(); var game = Create(actor, clock);
        var item = Queue(game, actor, producer);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Equal(item.StackItemId, Assert.Single(game.State.EffectStack).StackItemId);
        Assert.Equal(item.StackItemId, Assert.Single(game.State.PendingPrompts).StackItemId);
        Assert.NotNull(game.State.ResponseWindow);
        game = Restore(game, restore, clock);
        for (var pass = 0; pass < 2; pass++) { Pass(game, item.StackItemId); game = Restore(game, restore, clock); }
        AssertClean(game);
    }

    [Theory]
    [MemberData(nameof(ProducerControlCases))]
    [Trait("L12Evidence", "authority-settling-deferred-control")]
    public void SixProducerShapesAlreadyResolvingStillUseTheExistingDeferredQueue(
        string producer, int actor, bool restore)
    {
        var clock = new Clock(); var game = Create(actor, clock);
        var source = game.State.Players[actor].Relic!;
        game.State.IsResolvingStack = true;
        game.State.EffectStack.Add(new L12StackItem
        {
            StackItemId = "settling-control-parent", Controller = actor, Trigger = "active",
            SourceCardId = source.CardId, SourceInstanceId = source.InstanceId, SourceName = source.Name,
            Text = "合成结算中控制组",
        });
        var item = Queue(game, actor, producer);
        Assert.Equal(item.StackItemId, Assert.Single(game.State.DeferredEffectStack).StackItemId);
        Assert.Equal("settling-control-parent", Assert.Single(game.State.EffectStack).StackItemId);
        Assert.Empty(game.State.PendingPrompts); Assert.Null(game.State.ResponseWindow);
        game = Restore(game, restore, clock);
        Assert.Equal(item.StackItemId, Assert.Single(game.State.DeferredEffectStack).StackItemId);
        Assert.Equal("settling-control-parent", Assert.Single(game.State.EffectStack).StackItemId);
        Assert.Empty(game.State.PendingPrompts); Assert.Null(game.State.ResponseWindow);
        if (producer == "hand-add") AssertPrivacy(game, actor, "private-deferred-subject");
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)] [InlineData(1, false)] [InlineData(1, true)]
    [Trait("L12Evidence", "authority-multiple-deferred-distinct-window-order")]
    public void TwoPrivateAuthorityEventsKeepTheirExistingSingleItemLifoOrderAcrossRecovery(int actor, bool restore)
    {
        var clock = new Clock(); var game = Create(actor, clock);
        var parentId = StartParent(game, actor);
        var first = Queue(game, actor, "hand-add", "-first");
        var second = Queue(game, actor, "hand-add", "-second");
        var firstId = first.StackItemId; var secondId = second.StackItemId;
        Assert.Equal(new[] { firstId, secondId }, game.State.DeferredEffectStack.Select(item => item.StackItemId));
        Assert.Equal(parentId, Assert.Single(game.State.EffectStack).StackItemId);
        AssertPrivacy(game, actor, "private-deferred-subject-first", "private-deferred-subject-second");
        game = Restore(game, restore, clock);
        Pass(game, parentId); game = Restore(game, restore, clock); Pass(game, parentId);
        game = Restore(game, restore, clock);
        foreach (var expected in new[] { secondId, firstId })
        {
            Assert.Equal(expected, Assert.Single(game.State.EffectStack).StackItemId);
            for (var pass = 0; pass < 2; pass++) { Pass(game, expected); game = Restore(game, restore, clock); }
        }
        Assert.Equal(2, game.State.AuthorityEvents.Count(item => item.Type == "effect-hand-add" && item.Resolved));
        Assert.Equal(2, game.State.Players[actor].Hand.Count(card => card.InstanceId.StartsWith("private-deferred-subject", StringComparison.Ordinal)));
        AssertPrivacy(game, actor, "private-deferred-subject-first", "private-deferred-subject-second");
        AssertClean(game);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)] [InlineData(1, false)] [InlineData(1, true)]
    [Trait("L12Evidence", "authority-deferred-preserves-five-second-deadline")]
    public void PrivateAuthorityEnqueueDoesNotResetOrRecreateTheOriginalFiveSecondLease(int actor, bool restore)
    {
        var clock = new Clock(); var game = Create(actor, clock, L12GameEngine.InvalidFiveSecondsResponseMode);
        var parentId = StartParent(game, actor);
        game = Restore(game, restore, clock);
        var first = game.CaptureResponseAutoCloseLease()!;
        Assert.NotNull(first);
        var originalWindow = game.State.ResponseWindow!;
        var serialized = JsonSerializer.Serialize(originalWindow);
        clock.Now = clock.Now.AddSeconds(1);
        var queued = Queue(game, actor, "hand-add");
        Assert.Same(originalWindow, game.State.ResponseWindow);
        Assert.Equal(serialized, JsonSerializer.Serialize(game.State.ResponseWindow));
        Assert.Equal(first, game.CaptureResponseAutoCloseLease());
        game = Restore(game, restore, clock);
        Assert.Equal(first, game.CaptureResponseAutoCloseLease());
        Assert.False(game.TryExpireResponseAutoClose(first.PromptId, first.StackItemId, first.PriorityPlayer,
            first.DeadlineUtc, first.DeadlineUtc.AddMilliseconds(-1)));
        Assert.Equal(parentId, Assert.Single(game.State.EffectStack).StackItemId);
        clock.Now = first.DeadlineUtc;
        Assert.True(game.TryExpireResponseAutoClose(first.PromptId, first.StackItemId, first.PriorityPlayer,
            first.DeadlineUtc, clock.Now));
        Assert.False(game.TryExpireResponseAutoClose(first.PromptId, first.StackItemId, first.PriorityPlayer,
            first.DeadlineUtc, clock.Now));
        Assert.Equal(parentId, Assert.Single(game.State.EffectStack).StackItemId);
        Assert.Equal(queued.StackItemId, Assert.Single(game.State.DeferredEffectStack).StackItemId);
        var second = game.CaptureResponseAutoCloseLease()!;
        Assert.Equal(first.DeadlineUtc.AddSeconds(5), second.DeadlineUtc);
        game = Restore(game, restore, clock);
        clock.Now = second.DeadlineUtc;
        Assert.True(game.TryExpireResponseAutoClose(second.PromptId, second.StackItemId, second.PriorityPlayer,
            second.DeadlineUtc, clock.Now));
        Assert.Equal(queued.StackItemId, Assert.Single(game.State.EffectStack).StackItemId);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Single(game.State.Players[actor].Graveyard, card => card.InstanceId == "deferred-parent");
        var fresh = game.CaptureResponseAutoCloseLease()!;
        Assert.Equal(second.DeadlineUtc.AddSeconds(5), fresh.DeadlineUtc);
        Assert.NotEqual(parentId, fresh.StackItemId);
        AssertPrivacy(game, actor, "private-deferred-subject");
        game = Restore(game, restore, clock);
        for (var pass = 0; pass < 2; pass++) { Pass(game, queued.StackItemId); game = Restore(game, restore, clock); }
        AssertClean(game);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)] [InlineData(1, false)] [InlineData(1, true)]
    [Trait("L12Evidence", "public-defense-choice-cannot-bypass-current-window")]
    public void ActualDefenseCommandCannotQueueWhileAttackResponsesAreStillPending(int actor, bool restore)
    {
        var clock = new Clock(); var game = Create(actor, clock);
        game.State.Players[actor].Field[0][0]!.Tapped = false;
        Handle(game, actor, new L12Command("attack", "ready-target", Target: new L12AttackTarget("master")));
        game = Restore(game, restore, clock);
        var window = JsonSerializer.Serialize(game.State.ResponseWindow);
        var prompts = JsonSerializer.Serialize(game.State.PendingPrompts);
        var result = game.Handle(1 - actor, new L12Command("resolveDefense", CardInstanceIds: []));
        Assert.False(result.Accepted);
        Assert.Equal(window, JsonSerializer.Serialize(game.State.ResponseWindow));
        Assert.Equal(prompts, JsonSerializer.Serialize(game.State.PendingPrompts));
        Assert.Empty(game.State.AuthorityEvents);
        Assert.Empty(game.State.DeferredEffectStack);
    }
}
