using System.Text.Json;
using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class FieldMoraleResourceLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 92601, bool autoPassEmptyResponses = true)
    {
        var deck = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "field-morale-resource", "FMR", seed,
            ["甲", "乙"], [deck, deck], skipPreparation: true,
            autoPassEmptyResponses: autoPassEmptyResponses,
            concealHiddenResponseAvailability: false, disasterMode: "none");
        game.State.Phase = L12Phase.Main;
        game.State.ActivePlayer = 0;
        game.State.Round = 3;
        game.State.TurnSerial = 5;
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear();
            player.Morale.Clear();
            player.TemporaryMorale = 0;
            player.Resolving.Clear();
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = cardId,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
        };
    }

    private static int SpendableResources(L12GameEngine game, int playerIndex, int? viewerIndex = null)
        => JsonSerializer.SerializeToElement(game.SnapshotFor(viewerIndex ?? playerIndex),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .GetProperty("players")[playerIndex]
            .GetProperty("spendableResourceCount").GetInt32();

    private static object? Call(L12GameEngine game, string method, params object?[] args)
        => typeof(L12GameEngine).GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, args);

    [Fact]
    [Trait("L12Evidence", "card:S01-0212")]
    public void TombGuardOwnsTheOnlyStructuredFieldMoraleResourceRule()
    {
        var rule = Assert.IsType<L12FieldMoraleResourceRule>(
            L12StructuredCardSemantics.FieldMoraleResourceRule("S01-0212"));

        Assert.Equal("tomb-guard", rule.ResourceType);
        Assert.Equal("陵墓守卫", rule.DisplayName);
        Assert.True(rule.ControllerTurnOnly);
        Assert.True(rule.RequiresActive);
        Assert.Null(L12StructuredCardSemantics.FieldMoraleResourceRule("S01-0211"));
    }

    [Theory]
    [InlineData("S01-0212", 0)]
    [InlineData("S01-0212", 1)]
    [Trait("L12Evidence", "card:S01-0212")]
    public void ActivePublicTombGuardIsOneSpendableResourceInEitherRow(string _, int row)
    {
        var game = Create(92602 + row);
        var player = game.State.Players[0];
        var mover = Card("S01-0101", $"mover-{row}");
        var guard = Card("S01-0212", $"guard-{row}");
        player.Field[0][0] = mover;
        player.Field[row][2] = guard;

        Assert.Equal(1, SpendableResources(game, 0));
        var moved = game.Handle(0, new L12Command("move", CardInstanceId: mover.InstanceId,
            CardInstanceIds: [guard.InstanceId], Row: 0, Slot: 1));

        Assert.True(moved.Accepted, moved.Error);
        Assert.True(guard.Tapped);
        Assert.Equal(0, SpendableResources(game, 0));
    }

    [Theory]
    [InlineData("S01-0212", true, false, 0, 0)]
    [InlineData("S01-0212", false, true, 0, 0)]
    [InlineData("S01-0212", false, false, 1, 0)]
    [InlineData("S01-0212", false, false, 0, 1)]
    [Trait("L12Evidence", "card:S01-0212")]
    public void CurrentStateControlsResourceEligibility(string _, bool tapped, bool hidden,
        int activePlayer, int expected)
    {
        var game = Create(92610 + activePlayer);
        var guard = Card("S01-0212", "stateful-guard");
        guard.Tapped = tapped;
        guard.Hidden = hidden;
        game.State.Players[0].Field[0][0] = guard;
        game.State.ActivePlayer = activePlayer;

        Assert.Equal(expected, SpendableResources(game, 0));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0212")]
    public void CurrentControllerMaySpendAControlledTombGuardRegardlessOfPrintedOwner()
    {
        var game = Create(92620);
        game.State.ActivePlayer = 1;
        var controller = game.State.Players[1];
        var mover = Card("S01-0101", "controlled-mover");
        var guard = Card("S01-0212", "controlled-guard");
        guard.OwnerIndex = 0;
        controller.Field[0][0] = mover;
        controller.Field[1][2] = guard;

        Assert.Equal(1, SpendableResources(game, 1));
        var view = JsonSerializer.SerializeToElement(game.SnapshotFor(1),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("tomb-guard", view.GetProperty("players")[1].GetProperty("field")[1][2]
            .GetProperty("spendableResourceType").GetString());
        var moved = game.Handle(1, new L12Command("move", CardInstanceId: mover.InstanceId,
            CardInstanceIds: [guard.InstanceId], Row: 0, Slot: 1));

        Assert.True(moved.Accepted, moved.Error);
        Assert.True(guard.Tapped);
        Assert.Equal(0, SpendableResources(game, 1));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0212")]
    public void ManualPaymentProjectsStructuredTypeAndCommitsTheSelectedInstanceOnlyOnce()
    {
        var game = Create(92630);
        var player = game.State.Players[0];
        var guard = Card("S01-0212", "manual-guard");
        var ordinary = new L12MoraleCard { CardId = "S01-01C1", InstanceId = "manual-morale" };
        var legion = Card("S01-0116", "manual-legion");
        player.Field[1][2] = guard;
        player.Morale.Add(ordinary);
        player.Hand.Add(legion);

        var begin = game.Handle(0, new L12Command("playCard", CardInstanceId: legion.InstanceId, Row: 0, Slot: 0));
        Assert.True(begin.Accepted, begin.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-payment", prompt.Kind);
        Assert.Equal("tomb-guard", prompt.Data[$"{guard.InstanceId}:resourceType"]);
        Assert.Contains(guard.InstanceId, prompt.ValidChoices);
        Assert.Contains(ordinary.InstanceId, prompt.ValidChoices);

        var paid = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [guard.InstanceId]));
        Assert.True(paid.Accepted, paid.Error);
        Assert.True(guard.Tapped);
        Assert.False(ordinary.Tapped);
        Assert.Same(legion, player.Field[0][0]);

        var duplicate = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [guard.InstanceId]));
        Assert.False(duplicate.Accepted);
        Assert.True(guard.Tapped);
        Assert.False(ordinary.Tapped);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0212")]
    public void StaleSelectedFieldResourceFailsWithoutSubstitutingAnotherResourceAndCanRestart()
    {
        var game = Create(92640);
        var player = game.State.Players[0];
        var guard = Card("S01-0212", "stale-guard");
        var ordinary = new L12MoraleCard { CardId = "S01-01C1", InstanceId = "fallback-morale" };
        var legion = Card("S01-0116", "stale-payment-legion");
        player.Field[1][2] = guard;
        player.Morale.Add(ordinary);
        player.Hand.Add(legion);

        Assert.True(game.Handle(0, new L12Command("playCard", CardInstanceId: legion.InstanceId,
            Row: 0, Slot: 0)).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        guard.Tapped = true;

        var stale = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [guard.InstanceId]));
        Assert.False(stale.Accepted);
        Assert.False(ordinary.Tapped);
        Assert.Contains(legion, player.Hand);
        Assert.Null(player.Field[0][0]);

        var retry = game.Handle(0, new L12Command("playCard", CardInstanceId: legion.InstanceId,
            Row: 0, Slot: 0));
        Assert.True(retry.Accepted, retry.Error);
        Assert.True(ordinary.Tapped);
        Assert.Same(legion, player.Field[0][0]);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0212")]
    public void SnapshotRebuildsSpendableCountFromCurrentAuthoritativeState()
    {
        var game = Create(92650);
        var player = game.State.Players[0];
        var guard = Card("S01-0212", "snapshot-guard");
        player.Field[0][0] = guard;

        Assert.Equal(1, SpendableResources(game, 0));
        Assert.Equal(1, SpendableResources(game, 0, viewerIndex: 1));
        guard.Tapped = true;
        Assert.Equal(0, SpendableResources(game, 0));
        guard.Tapped = false;
        player.Field[0][0] = null;
        player.Graveyard.Add(guard);
        Assert.Equal(0, SpendableResources(game, 0));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0212")]
    public void EffectPaymentCanBeCancelledWithoutLeavingTheStackItemBlocked()
    {
        var game = Create(92660);
        var player = game.State.Players[0];
        var guard = Card("S01-0212", "effect-cancel-guard");
        var ordinary = new L12MoraleCard { CardId = "S01-01C1", InstanceId = "effect-cancel-morale" };
        player.Field[0][0] = guard;
        player.Morale.Add(ordinary);
        var item = new L12StackItem
        {
            StackItemId = "effect-payment-cancel",
            Controller = 0,
            SourceInstanceId = "effect-source",
            SourceCardId = "S01-0001",
            SourceName = "测试后续效果",
            Trigger = "play",
            Text = "可支付后续",
        };
        game.State.EffectStack.Add(item);

        Call(game, "BeginEffectMoralePayment", item, 1, "optional-paid-effect-operation",
            new Dictionary<string, string> { ["operation"] = "heal-master", ["amount"] = "1" });
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("cancel", prompt.ValidChoices);
        Assert.Equal("不发动后续效果", prompt.Data["cancel"]);

        var cancelled = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "cancel"));
        Assert.True(cancelled.Accepted, cancelled.Error);
        Assert.Empty(game.State.PendingPrompts);
        Assert.DoesNotContain(item, game.State.EffectStack);
        Assert.False(guard.Tapped);
        Assert.False(ordinary.Tapped);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0212")]
    public void StaleEffectPaymentSelectionReopensTheSamePaymentWithCancelFallback()
    {
        var game = Create(92661);
        var player = game.State.Players[0];
        var guard = Card("S01-0212", "effect-stale-guard");
        var ordinary = new L12MoraleCard { CardId = "S01-01C1", InstanceId = "effect-stale-morale" };
        player.Field[0][0] = guard;
        player.Morale.Add(ordinary);
        var item = new L12StackItem
        {
            StackItemId = "effect-payment-retry",
            Controller = 0,
            SourceInstanceId = "effect-source",
            SourceCardId = "S01-0001",
            SourceName = "测试后续效果",
            Trigger = "play",
            Text = "可支付后续",
        };
        game.State.EffectStack.Add(item);
        Call(game, "BeginEffectMoralePayment", item, 1, "optional-paid-effect-operation",
            new Dictionary<string, string> { ["operation"] = "heal-master", ["amount"] = "1" });
        var first = Assert.Single(game.State.PendingPrompts);
        guard.Tapped = true;

        var stale = game.Handle(0, new L12Command("resolvePrompt", PromptId: first.PromptId,
            CardInstanceIds: [guard.InstanceId]));
        Assert.True(stale.Accepted, stale.Error);
        var retry = Assert.Single(game.State.PendingPrompts);
        Assert.NotEqual(first.PromptId, retry.PromptId);
        Assert.Contains("已失效", retry.Data["retryReason"]);
        Assert.Contains("cancel", retry.ValidChoices);
        Assert.Contains(item, game.State.EffectStack);
        Assert.False(ordinary.Tapped);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: retry.PromptId,
            Choice: "cancel")).Accepted);
        Assert.DoesNotContain(item, game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0212")]
    public void RejectedActiveCommitRestoresSelectedResourcesAndTemporaryVoucher()
    {
        var game = Create(92662);
        var player = game.State.Players[0];
        var invalidSource = Card("S01-0101", "invalid-front-buff-source");
        var guard = Card("S01-0212", "rollback-guard");
        player.Field[0][0] = invalidSource;
        player.Field[1][2] = guard;

        var result = Assert.IsType<CommandResult>(Call(game, "CommitActiveAbility", 0, invalidSource,
            "frontBuff", null, null, new[] { guard.InstanceId }, null));

        Assert.False(result.Accepted);
        Assert.False(guard.Tapped);
        Assert.Equal(0, player.TemporaryMorale);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0212")]
    public void PaidCostSummaryUsesTheStructuredFieldResourceIdentity()
    {
        var game = Create(92663, autoPassEmptyResponses: false);
        var player = game.State.Players[0];
        var source = Card("S01-02C1", "faction-0");
        var guard = Card("S01-0212", "summary-guard");
        player.Field[0][0] = guard;

        var result = Assert.IsType<CommandResult>(Call(game, "CommitActiveAbility", 0, source,
            "sunDraw", null, null, new[] { guard.InstanceId }, null));

        Assert.True(result.Accepted, result.Error);
        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal("以休整〈陵墓守卫〉支付1士气", item.Data["paidCostSummary"]);
        Assert.True(guard.Tapped);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0212")]
    public void StaleFieldResourceSelectionReopensActivePaymentWithoutSubstitutingOrdinaryMorale()
    {
        var game = Create(92664);
        var player = game.State.Players[0];
        var source = Card("S01-02C1", "faction-0");
        var guard = Card("S01-0212", "active-retry-guard");
        var ordinary = new L12MoraleCard { CardId = "S01-02C1", InstanceId = "active-retry-morale" };
        player.Field[0][0] = guard;
        player.Morale.Add(ordinary);

        var opened = Assert.IsType<CommandResult>(Call(game, "CommitActiveAbility", 0, source,
            "sunDraw", null, null, null, null));
        Assert.True(opened.Accepted, opened.Error);
        var first = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(guard.InstanceId, first.ValidChoices);
        Assert.Contains(ordinary.InstanceId, first.ValidChoices);
        guard.Tapped = true;

        var stale = game.Handle(0, new L12Command("resolvePrompt", PromptId: first.PromptId,
            CardInstanceIds: [guard.InstanceId]));

        Assert.True(stale.Accepted, stale.Error);
        var retry = Assert.Single(game.State.PendingPrompts);
        Assert.NotEqual(first.PromptId, retry.PromptId);
        Assert.DoesNotContain(guard.InstanceId, retry.ValidChoices);
        Assert.Contains(ordinary.InstanceId, retry.ValidChoices);
        Assert.Contains("cancel", retry.ValidChoices);
        Assert.Contains("已失效", retry.Data["retryReason"]);
        Assert.False(ordinary.Tapped);
        Assert.Empty(game.State.EffectStack);
    }
}
