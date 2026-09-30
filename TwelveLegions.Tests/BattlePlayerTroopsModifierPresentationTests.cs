using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BattlePlayerTroopsModifierPresentationTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly string[] Routes = ["ankh", "egil", "inahime", "attack-ay", "attack-inahime",
        "ankh-enter", "egil-debuff", "inaihime-buff", "ambush-buff", "reaction-ambush", "lightSwordActive"];

    public static IEnumerable<object[]> SuccessfulRoutes() => Routes.SelectMany(route =>
        new[] { new object[] { route, 0 }, new object[] { route, 1 } });
    public static IEnumerable<object[]> InvalidRoutes() => Routes.SelectMany(route =>
        new[] { new object[] { route, false }, new object[] { route, true } });

    private static object? Invoke(L12GameEngine game, string method, params object?[] arguments) =>
        typeof(L12GameEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, arguments);

    private static L12CardInstance Card(string id, string cardId = "S01-0212", string faction = "gaotianyuan") => new()
    {
        InstanceId = id, CardId = cardId, Name = "同名公开军团", CardType = "legion",
        Faction = faction, Cost = 2, BaseTroops = 1000, Troops = 1000, OwnerIndex = 0,
    };

    private static (L12GameEngine Game, L12CardInstance Target, L12StackItem Item, L12Prompt Prompt)
        Setup(string route, int controller)
    {
        var game = new L12GameEngine(Catalog, "troops-log", "TROOPS", 93112,
            ["甲", "乙"], [0, 0], skipPreparation: true, stateFormatVersion: 2);
        game.State.Phase = L12Phase.Main;
        game.State.ActivePlayer = controller;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.PendingPrompts.Clear();
        game.State.EffectStack.Clear();
        foreach (var player in game.State.Players)
        foreach (var row in player.Field) Array.Clear(row);
        var target = Card("target", faction: route == "lightSwordActive" ? "otherworld" : "gaotianyuan");
        var owner = route.StartsWith("egil", StringComparison.Ordinal) ? 1 - controller : controller;
        target.OwnerIndex = owner;
        game.State.Players[owner].Field[0][0] = target;
        // Source is deliberately absent: the settled target fact must not rediscover it on today's board.
        var sourceId = route switch
        {
            "egil" or "egil-debuff" => "S01-0316",
            "inahime" or "attack-inahime" or "inaihime-buff" => "S01-0416",
            "attack-ay" => "S01-0208",
            "lightSwordActive" => "ST06-09",
            "ambush-buff" or "reaction-ambush" => Catalog.Cards.Values.Single(card => card.NameZh == "伏击").Id,
            _ => "S01-0215",
        };
        var definition = Catalog.Cards[sourceId];
        var source = new L12CardInstance
        {
            InstanceId = "departed-source", CardId = definition.Id, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction, Cost = definition.Cost ?? 0,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            EffectText = definition.Effect, Traits = [.. definition.Traits], Profession = definition.Profession,
        };
        var item = new L12StackItem
        {
            StackItemId = "modifier-stack", Controller = controller, SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId, SourceName = source.Name, SourceSnapshot = source,
            Trigger = route.StartsWith("attack-", StringComparison.Ordinal) ? "attack" : "enter", Text = "公开效果",
        };
        item.Data["declared:target"] = target.InstanceId;
        item.Data["target"] = route == "lightSwordActive" ? $"mode:buff|paid-card|{target.InstanceId}" : target.InstanceId;
        if (route.StartsWith("attack-", StringComparison.Ordinal)) item.Data["attackPlan"] = route[7..];
        if (route == "reaction-ambush") item.Data["atomicFlow"] = "伏击";
        game.State.EffectStack.Add(item);
        var prompt = new L12Prompt
        {
            PromptId = "modifier-prompt", PlayerIndex = controller, Kind = "target", Text = "选择公开对象",
            ValidChoices = [target.InstanceId], MinChoose = 1, MaxChoose = 1,
            Continuation = "card-effect", StackItemId = item.StackItemId,
            Data = new() { ["action"] = route },
        };
        return (game, target, item, prompt);
    }

    private static void Settle(string route, L12GameEngine game, L12StackItem item, L12Prompt prompt)
    {
        var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "target");
        switch (route)
        {
            case "ankh": case "egil": case "inahime":
                Assert.True((bool)Invoke(game, "ResolveBatch6JAEnterEffect", item, item.SourceSnapshot, route)!); break;
            case "attack-ay": case "attack-inahime":
                Assert.True((bool)Invoke(game, "TryResolveAttackPublicTriggerEffect", item, item.SourceSnapshot)!); break;
            case "ankh-enter": case "egil-debuff":
                Assert.True((bool)Invoke(game, "TryContinueS1Faction", item, prompt, new List<string> { "target" }, command)!); break;
            case "ambush-buff":
                Assert.True((bool)Invoke(game, "TryContinueS1Extended", item, prompt, new List<string> { "target" }, command)!); break;
            case "inaihime-buff":
                Invoke(game, "ContinueCardEffect", prompt, new List<string> { "target" }, command); break;
            case "reaction-ambush": Invoke(game, "ResolveS1ReactionEffect", item); break;
            case "lightSwordActive":
                Assert.True((bool)Invoke(game, "TryResolveStarterRemainingActiveEffect", item, null, route)!); break;
            default: throw new InvalidOperationException(route);
        }
    }

    [Theory]
    [MemberData(nameof(SuccessfulRoutes))]
    public void ElevenRealSettlementRoutesFreezeTargetDeltaAndDurationForBothControllers(string route, int controller)
    {
        var (game, target, item, prompt) = Setup(route, controller);
        var twin = Card("same-name-twin");
        game.State.Players[target.OwnerIndex!.Value].Field[0][1] = twin;
        var beforeCost = target.CostModifier;
        Settle(route, game, item, prompt);
        var delta = route.StartsWith("egil", StringComparison.Ordinal) ? -2000
            : route.Contains("inahime", StringComparison.Ordinal) || route == "inaihime-buff" ? 1000 : 2000;
        Assert.Equal(1000 + delta, target.Troops);
        Assert.Equal(beforeCost, target.CostModifier);
        Assert.Empty(twin.TimedModifiers);
        var modifier = Assert.Single(target.TimedModifiers);
        Assert.Equal(delta, modifier.TroopsDelta);
        Assert.Equal(0, modifier.CostDelta);
        Assert.Equal(7, modifier.ExpiresAfterTurn);
        var original = Assert.Single(game.State.Events, action => action.PlayerTroopsModifier is not null);
        var expected = new L12PlayerTroopsModifier(target.InstanceId, target.OwnerIndex, delta, "this-turn");
        Assert.Equal(expected, original.PlayerTroopsModifier);
        Assert.Equal("troops-modifier", original.Type);
        Assert.Equal(Math.Max(0, 1000 + delta), Assert.Single(original.Cards).Troops);
        Assert.DoesNotContain("伤害", original.Text);
        Assert.DoesNotContain("击杀", original.Text);
        Assert.DoesNotContain("departed-source", JsonSerializer.Serialize(original));
        Assert.DoesNotContain(game.State.Events, action => action.Type == "effect"
            && action.Text.StartsWith("光之剑使", StringComparison.Ordinal));
        // The event survives later board changes; recipient projection is event-snapshot based.
        game.State.Players[target.OwnerIndex!.Value].Field[0][0] = null;
        target.Hidden = true;
        foreach (var snapshot in new[] { game.SnapshotFor(0), game.SnapshotFor(1), game.SnapshotForSpectator(),
            game.SnapshotForReferee(), game.SnapshotForGm(0) })
            Assert.Equal(expected, Assert.Single(snapshot.RecentEvents, action => action.Sequence == original.Sequence)
                .PlayerTroopsModifier);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence);
        Assert.Equal(expected, Assert.Single(restored.SnapshotFor(1).RecentEvents,
            action => action.Sequence == original.Sequence).PlayerTroopsModifier);
    }

    [Theory]
    [MemberData(nameof(InvalidRoutes))]
    public void MissingOrCoveredTargetNeverPublishesSuccessfulTroopsFact(string route, bool hidden)
    {
        var (game, target, item, prompt) = Setup(route, 0);
        if (hidden) target.Hidden = true;
        else game.State.Players[target.OwnerIndex!.Value].Field[0][0] = null;
        Settle(route, game, item, prompt);
        Assert.DoesNotContain(game.State.Events, action => action.PlayerTroopsModifier is not null);
    }

    [Fact]
    public void CrossControlledTargetUsesSettledControllerNotCardOwner()
    {
        var (game, target, item, prompt) = Setup("ankh", 1);
        target.OwnerIndex = 0;
        Settle("ankh", game, item, prompt);
        var action = Assert.Single(game.State.Events, action => action.PlayerTroopsModifier is not null);
        Assert.Equal(1, action.PlayerTroopsModifier!.TargetControllerPlayerIndex);
        Assert.Equal(0, Assert.Single(action.Cards).OwnerIndex);
    }

    [Fact]
    public void RepeatedRealPromptCommandDoesNotPublishOrApplyTwice()
    {
        var (game, target, _, prompt) = Setup("inaihime-buff", 1);
        game.State.PendingPrompts.Add(prompt);
        var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: target.InstanceId);
        var first = game.Handle(1, command);
        Assert.True(first.Accepted, first.Error);
        Assert.False(game.Handle(1, command).Accepted);
        Assert.Single(target.TimedModifiers);
        Assert.Single(game.State.Events, action => action.PlayerTroopsModifier is not null);
    }

    [Fact]
    public void InvalidFactsAreOmittedForEveryRecipientAndLegacyTextCannotManufacturePayload()
    {
        var (game, target, _, _) = Setup("ankh", 0);
        var fact = new L12PlayerTroopsModifier("target", 0, 0, "this-turn");
        var raw = new L12ActionEvent(1, "troops-modifier", 0, "兵力+999999秘密来源", [target])
            { PlayerTroopsModifier = fact };
        var hidden = target.Clone(); hidden.Hidden = true;
        foreach (var viewer in new[] { -1, 0, 1 }) foreach (var revealAll in new[] { false, true })
        {
            Assert.Equal(fact, L12RecipientVisibility.ProjectActionEvent(game.State, raw, viewer, revealAll)
                .PlayerTroopsModifier);
            foreach (var invalid in new[] { raw with { Type = "effect" }, raw with { PlayerIndex = 1 },
                raw with { Cards = [] }, raw with { Cards = [hidden] }, raw with { Cards = [target, target] },
                raw with { PlayerTroopsModifier = fact with { TargetInstanceId = "private-instance" } },
                raw with { PlayerTroopsModifier = fact with { TargetControllerPlayerIndex = 2 } },
                raw with { PlayerTroopsModifier = fact with { TroopsDelta = null } },
                raw with { PlayerTroopsModifier = fact with { DurationCode = "internal-private-reason" } } })
                Assert.Null(L12RecipientVisibility.ProjectActionEvent(game.State, invalid, viewer, revealAll)
                    .PlayerTroopsModifier);
        }
        Invoke(game, "AddEvent", "effect", 0, "同名公开军团本回合兵力+2000", new[] { target });
        Assert.Null(game.State.Events.Last().PlayerTroopsModifier);
        Assert.DoesNotContain("PlayerTroopsModifier", JsonSerializer.Serialize(game.State.Events.Last()));
    }
}
