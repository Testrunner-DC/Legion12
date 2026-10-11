using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AuthorityEventSourcePresentationTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var definition = Catalog.Cards[id];
        return new L12CardInstance
        {
            CardId = id, InstanceId = instance, OwnerIndex = owner, Name = definition.NameZh,
            CardType = definition.CardType, IsCounterTactic = definition.IsCounterTactic,
            Faction = definition.Faction, ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0, HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect, Traits = [.. definition.Traits], Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0, TrialValue = definition.TrialValue ?? 0, SummonRound = -1,
        };
    }

    private static L12GameEngine Create(int actor, string master = "S01-01M1")
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "合成无来源权威事件", MasterId = master,
            CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "synthetic-authority-source-presentation", "LOCAL", 171001 + actor,
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
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Resolving.Clear();
            player.Relic = null; player.ExtraRelics.Clear(); player.Morale.Clear(); player.UsedAbilities.Clear();
        }
        game.State.Players[actor].Hand.Add(Card("S01-0003", "unrelated-private-hand", actor));
        game.State.Players[actor].Library.Add(Card("S01-0003", "unrelated-private-library", actor));
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game, bool recover)
        => !recover ? game : L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState!.Value, game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static object? Invoke(L12GameEngine game, string method, params object?[] arguments)
        => typeof(L12GameEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, arguments);

    private static void PublishResult(L12GameEngine game, L12StackItem item, string status)
        => Invoke(game, "AddEffectResultEvent", item, status);

    private static void AssertOtherPrivateCardsHidden(L12GameEngine game, int actor)
    {
        var view = JsonSerializer.Serialize(game.SnapshotFor(1 - actor));
        Assert.DoesNotContain("unrelated-private-hand", view, StringComparison.Ordinal);
        Assert.DoesNotContain("unrelated-private-library", view, StringComparison.Ordinal);
    }

    private static L12GameEngine PassExactAuthorityWindow(L12GameEngine game, string stackId, bool recover,
        string? parentStackId = null)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            var item = game.State.EffectStack[^1];
            Assert.Equal(stackId, item.StackItemId);
            Assert.Equal("authority-event", item.Trigger);
            Assert.Equal("effect-hand-add", item.Data["eventType"]);
            Assert.All(game.State.PendingPrompts, pending =>
            {
                Assert.Equal("response", pending.Kind);
                Assert.Equal("stack-response", pending.Continuation);
                Assert.True(pending.StackItemId == stackId || pending.StackItemId == parentStackId);
            });
            var prompt = game.State.PendingPrompts.Last(pending => pending.StackItemId == stackId
                && pending.PlayerIndex == game.State.ResponseWindow!.PriorityPlayer);
            Assert.Equal("response", prompt.Kind);
            Assert.Equal("stack-response", prompt.Continuation);
            Assert.Equal(stackId, prompt.StackItemId);
            Assert.Equal(game.State.ResponseWindow!.PriorityPlayer, prompt.PlayerIndex);
            var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass");
            var result = game.Handle(prompt.PlayerIndex, command);
            Assert.True(result.Accepted, result.Error);
            Assert.False(game.Handle(prompt.PlayerIndex, command).Accepted);
            game = Restore(game, recover);
        }
        Assert.DoesNotContain(game.State.EffectStack, item => item.StackItemId == stackId);
        if (parentStackId is not null)
        {
            Assert.Equal(parentStackId, Assert.Single(game.State.EffectStack).StackItemId);
            return game;
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
        Assert.Null(game.State.ResponseWindow);
        return game;
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [Trait("L12Evidence", "ruling:source-free-authority-result-20261011")]
    public void ActualGmFieldReturnKeepsTheAuthorityFactAndCompletesWithoutAFakeCardResult(int actor, bool recover)
    {
        var game = Create(actor, "S01-02M1");
        game.State.Players[actor].Field[0][0] = Card("S01-0203", "gm-returned-legion", actor);
        game.State.Players[actor].Field[0][1] = Card("S01-0203", "gm-retained-legion", actor);
        game.State.Players[actor].Hand.Add(Card("S01-0220", "gm-parent-canopic", actor));
        var play = game.Handle(actor, new L12Command("playCard", "gm-parent-canopic"));
        Assert.True(play.Accepted, play.Error);
        var declaration = Assert.Single(game.State.PendingPrompts);
        var declared = game.Handle(actor, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            CardInstanceIds: ["gm-returned-legion", "gm-retained-legion"]));
        Assert.True(declared.Accepted, declared.Error);
        var parentStackId = Assert.Single(game.State.EffectStack).StackItemId;
        var originalWindow = JsonSerializer.Serialize(game.State.ResponseWindow);
        var originalPrompt = JsonSerializer.Serialize(Assert.Single(game.State.PendingPrompts));
        var result = game.HandleGm(new L12GmCommand("returnCardToHand", actor, CardInstanceId: "gm-returned-legion"));
        Assert.True(result.Accepted, result.Error);
        var item = Assert.Single(game.State.DeferredEffectStack, pending => pending.Trigger == "authority-event");
        Assert.Equal("authority-event", item.Trigger);
        Assert.Equal(string.Empty, item.SourceCardId);
        Assert.Equal(string.Empty, item.SourceInstanceId);
        Assert.Null(item.SourceSnapshot);
        Assert.Equal("effect-hand-add", item.Data["eventType"]);
        Assert.Equal("field", item.Data["originZone"]);
        Assert.Equal("hand", item.Data["destinationZone"]);
        Assert.Equal("gm-returned-legion", item.Data["targetInstanceId"]);
        var stackId = item.StackItemId;
        var eventId = item.Data["eventId"];
        AssertOtherPrivateCardsHidden(game, actor);
        game = Restore(game, recover);
        Assert.Equal(originalWindow, JsonSerializer.Serialize(game.State.ResponseWindow));
        Assert.Equal(originalPrompt, JsonSerializer.Serialize(Assert.Single(game.State.PendingPrompts)));
        Assert.Equal(0, game.State.Players[actor].Field[0][1]!.ImmortalUses);
        Assert.True(game.State.ResponseWindow is not null, JsonSerializer.Serialize(new
        {
            prompts = game.State.PendingPrompts.Select(pending => new
            {
                pending.PromptId, pending.Kind, pending.Continuation, pending.StackItemId,
                pending.PlayerIndex, pending.ValidChoices,
            }),
            stack = game.State.EffectStack.Select(pending => new
            {
                pending.StackItemId, pending.SourceCardId, pending.SourceInstanceId, pending.Trigger,
            }),
            game.State.ResponseWindow, game.State.IsResolvingStack,
        }));
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.Equal(parentStackId, Assert.Single(game.State.EffectStack).StackItemId);
            Assert.All(game.State.PendingPrompts, pending =>
            {
                Assert.Equal("response", pending.Kind);
                Assert.Equal("stack-response", pending.Continuation);
                Assert.Equal(parentStackId, pending.StackItemId);
            });
            var priority = game.State.ResponseWindow!.PriorityPlayer;
            var response = game.State.PendingPrompts.Last(pending => pending.PlayerIndex == priority);
            var passed = game.Handle(priority, new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass"));
            Assert.True(passed.Accepted, passed.Error);
            game = Restore(game, recover);
        }
        Assert.Equal(stackId, Assert.Single(game.State.EffectStack).StackItemId);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Equal(1, game.State.Players[actor].Field[0][1]!.ImmortalUses);
        game = PassExactAuthorityWindow(game, stackId, recover);
        var player = game.State.Players[actor];
        Assert.Null(player.Field[0][0]);
        Assert.Single(player.Hand, card => card.InstanceId == "gm-returned-legion");
        Assert.Equal(1, player.Hand.Concat(player.Library).Concat(player.Graveyard)
            .Concat(player.Resolving).Count(card => card.InstanceId == "gm-returned-legion"));
        Assert.True(Assert.Single(game.State.AuthorityEvents, entry => entry.EventId == eventId).Resolved);
        Assert.Contains(game.State.Events, entry => entry.Type == "authority-event");
        Assert.Contains(game.State.Events, entry => entry.Type == "gm");
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == "gm-returned-legion"));
        Assert.Equal(0, Assert.Single(player.Hand, card => card.InstanceId == "gm-returned-legion").ImmortalUses);
        Assert.Equal(1, player.Field[0][1]!.ImmortalUses);
        Assert.Single(player.Graveyard, card => card.InstanceId == "gm-parent-canopic");
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Null(game.State.ResponseWindow);
        AssertOtherPrivateCardsHidden(game, actor);
        _ = Restore(game, true);
    }

    public static IEnumerable<object[]> SourceFreeStatusCases()
    {
        foreach (var actor in new[] { 0, 1 })
        foreach (var recover in new[] { false, true })
        foreach (var status in new[] { "resolved", "failed", "negated", "skipped", "declined" })
            yield return [actor, recover, status];
    }

    [Theory]
    [MemberData(nameof(SourceFreeStatusCases))]
    [Trait("L12Evidence", "source-free-authority-no-card-result-privacy")]
    public void PrivateSourceFreeHandAddNeverManufacturesACardResultForAnyTerminalStatus(
        int actor, bool recover, string status)
    {
        var game = Create(actor);
        var privateCard = Card("S02-0620", "private-hand-add-subject", actor);
        game.State.Players[actor].Hand.Add(privateCard);
        var item = (L12StackItem)Invoke(game, "QueueAuthorityEvent",
            "effect-hand-add", actor, privateCard, "合成私密入手权威事件",
            actor, privateCard.InstanceId, "library", "hand", true, null, false)!;
        Assert.Equal(string.Empty, item.SourceCardId);
        Assert.Equal(string.Empty, item.SourceInstanceId);
        Assert.Null(item.SourceSnapshot);
        var stackId = item.StackItemId;
        var eventId = item.Data["eventId"];
        game = Restore(game, recover);
        item = Assert.Single(game.State.EffectStack);
        PublishResult(game, item, status);
        PublishResult(game, item, status);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-result");
        Assert.Equal(1, game.State.AuthorityEvents.Count(entry => entry.EventId == eventId));
        Assert.False(Assert.Single(game.State.AuthorityEvents, entry => entry.EventId == eventId).Resolved);
        var view = JsonSerializer.Serialize(game.SnapshotFor(1 - actor));
        Assert.DoesNotContain("private-hand-add-subject", view, StringComparison.Ordinal);
        Assert.DoesNotContain(privateCard.Name, view, StringComparison.Ordinal);
        AssertOtherPrivateCardsHidden(game, actor);
        game = PassExactAuthorityWindow(game, stackId, recover);
        Assert.True(Assert.Single(game.State.AuthorityEvents, entry => entry.EventId == eventId).Resolved);
        Assert.Single(game.State.Players[actor].Hand, card => card.InstanceId == "private-hand-add-subject");
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-result");
        _ = Restore(game, true);
    }

    public static IEnumerable<object[]> SourceBearingCases()
    {
        foreach (var actor in new[] { 0, 1 })
        foreach (var recover in new[] { false, true })
        foreach (var hidden in new[] { false, true })
        foreach (var shape in new[] { "identifiers", "snapshot-only", "card-id-only" })
            yield return [actor, recover, hidden, shape];
    }

    [Theory]
    [MemberData(nameof(SourceBearingCases))]
    [Trait("L12Evidence", "authority-source-and-lki-results-remain-once")]
    public void AuthorityRecordsWithAnyRealSourceKeepTheirResultPresentationAndPublishedReceipt(
        int actor, bool recover, bool hidden, string shape)
    {
        var game = Create(actor);
        var source = Card("S01-0220", "valid-result-source", actor);
        source.Hidden = hidden;
        var item = new L12StackItem
        {
            StackItemId = "known-authority-result", Controller = actor, Trigger = "authority-event",
            SourceCardId = shape == "snapshot-only" ? string.Empty : source.CardId,
            SourceInstanceId = shape == "identifiers" ? source.InstanceId : string.Empty,
            SourceName = source.Name, SourceSnapshot = shape == "card-id-only" ? null : source,
            Text = "合成有来源结果",
        };
        game.State.EffectStack.Add(item);
        game = Restore(game, recover);
        item = Assert.Single(game.State.EffectStack);
        PublishResult(game, item, "failed");
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result");
        Assert.Equal("failed", result.EffectResultStatus);
        Assert.Equal(source.CardId, Assert.Single(result.Cards).CardId);
        Assert.Equal("true", item.Data["effectResultPublished"]);
        if (hidden && shape != "card-id-only") Assert.Null(result.PlayerLogSemantic);
        PublishResult(game, item, "failed");
        game = Restore(game, recover);
        item = Assert.Single(game.State.EffectStack);
        PublishResult(game, item, "failed");
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result");
        AssertOtherPrivateCardsHidden(game, actor);
    }

    public static IEnumerable<object[]> InvalidSourceCases()
    {
        foreach (var recover in new[] { false, true })
        foreach (var shape in new[] { "real-effect-empty", "enter-empty", "authority-unknown-card",
            "authority-only-instance", "authority-whitespace-card", "wrong-trigger-empty" })
            yield return [recover, shape];
    }

    [Theory]
    [MemberData(nameof(InvalidSourceCases))]
    [Trait("L12Evidence", "malformed-real-effect-sources-still-fail-strictly")]
    public void InvalidCardEffectAndPartialAuthoritySourceRecordsAreNotSilentlyExempted(bool recover, string shape)
    {
        var game = Create(0);
        var item = new L12StackItem
        {
            StackItemId = "invalid-result-source", Controller = 0,
            Trigger = shape == "real-effect-empty" ? "play" : shape == "enter-empty" ? "enter"
                : shape == "wrong-trigger-empty" ? "not-authority-event" : "authority-event",
            SourceCardId = shape == "authority-unknown-card" ? "UNKNOWN-CARD"
                : shape == "authority-whitespace-card" ? " " : string.Empty,
            SourceInstanceId = shape == "authority-only-instance" ? "missing-real-instance" : string.Empty,
            SourceName = "合成非法来源", Text = "合成非法来源",
        };
        game.State.EffectStack.Add(item);
        game = Restore(game, recover);
        item = Assert.Single(game.State.EffectStack);
        var exception = Assert.Throws<TargetInvocationException>(() => PublishResult(game, item, "failed"));
        Assert.IsType<KeyNotFoundException>(exception.InnerException);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-result");
        Assert.False(item.Data.ContainsKey("effectResultPublished"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("L12Evidence", "published-result-receipt-remains-first")]
    public void AnAlreadyPublishedResultStillReturnsBeforeSourceValidation(bool recover)
    {
        var game = Create(0);
        var item = new L12StackItem
        {
            StackItemId = "already-published", Controller = 0, Trigger = "play",
            SourceCardId = "UNKNOWN-CARD", SourceInstanceId = "missing-real-instance",
            SourceName = "合成已发布结果", Text = "合成已发布结果",
        };
        item.Data["effectResultPublished"] = "true";
        game.State.EffectStack.Add(item);
        game = Restore(game, recover);
        item = Assert.Single(game.State.EffectStack);
        PublishResult(game, item, "failed");
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-result");
        Assert.Equal("true", item.Data["effectResultPublished"]);
    }
}
