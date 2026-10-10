using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BattlePlayerSelectedTargetsPresentationTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = cardId, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction,
            Cost = definition.Cost ?? 0, BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0, EffectText = definition.Effect,
        };
    }

    private static L12GameEngine Create()
    {
        var game = new L12GameEngine(Catalog, "selected-targets", "SELECTED-TARGETS", 24001,
            ["真实玩家姓名甲", "真实玩家姓名乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        game.State.Phase = L12Phase.Main;
        game.State.ActivePlayer = 0;
        foreach (var player in game.State.Players)
        foreach (var row in player.Field) Array.Clear(row);
        return game;
    }

    private static L12StackItem Push(L12GameEngine game, L12CardInstance source,
        IEnumerable<string> targetIds, Dictionary<string, string>? data = null)
    {
        var push = typeof(L12GameEngine).GetMethod("PushEffect", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return Assert.IsType<L12StackItem>(push.Invoke(game,
            [0, source, "active", "主动效果", targetIds, data ?? new Dictionary<string, string>()]));
    }

    private static L12StackItem QueueReady(L12GameEngine game, L12CardInstance source,
        L12CardInstance target, bool publicSource = true)
    {
        var queue = typeof(L12GameEngine).GetMethod("QueueAuthorityEvent",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return Assert.IsType<L12StackItem>(queue.Invoke(game,
            ["effect-ready", 0, source, "公开准备事件", null, target.InstanceId,
                null, null, false, null, publicSource]));
    }

    [Fact]
    public void GenericPushRecordsPublicTargetBeforeOfferingResponse()
    {
        var game = Create();
        var source = Card("S01-0004", "selected-source");
        var target = Card("S01-0004", "selected-target");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[1][2] = target;
        Push(game, source, [target.InstanceId]);

        var selected = Assert.Single(game.State.Events, entry => entry.Type == "target-selected");
        Assert.Contains(game.UnpersistedEvents, entry => entry.Sequence == selected.Sequence
            && entry.PlayerSelectedTargets is not null);
        Assert.True(selected.Sequence < Assert.Single(game.State.Events,
            entry => entry.Type == "effect-activation" && entry.Cards.Any(card => card.InstanceId == source.InstanceId)).Sequence);
        Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
        Assert.Contains("已选目标", game.State.PendingPrompts[0].Text);
        Assert.Contains("后排右格", game.State.PendingPrompts[0].Text);
    }

    [Fact]
    public void PublicAuthorityReadyFreezesTargetBeforeItsOwnResponseWindow()
    {
        var game = Create();
        var source = Card("S01-0004", "authority-source");
        var target = Card("S01-0004", "authority-target");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[1][2] = target;
        var item = QueueReady(game, source, target);

        var selected = Assert.Single(game.State.Events, entry => entry.Type == "target-selected");
        Assert.True(selected.Sequence < Assert.Single(game.State.Events,
            entry => entry.Type == "authority-event").Sequence);
        Assert.Equal(target.InstanceId, Assert.Single(selected.PlayerSelectedTargets!.Facts).Id);
        Assert.Contains($"后排右格〈{target.Name}〉", Assert.Single(game.State.PendingPrompts).Text);
        game.State.Players[1].Field[1][2] = null;
        var describe = typeof(L12GameEngine).GetMethod("DescribeResponse",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Contains($"后排右格〈{target.Name}〉",
            Assert.IsType<string>(describe.Invoke(game, [item, 1])));
        Assert.Single(game.SnapshotForSpectator().RecentEvents,
            entry => entry.Type == "target-selected");
    }

    [Fact]
    public void PrivateAuthorityEntryDoesNotPublishATargetSelection()
    {
        var game = Create();
        var source = Card("S01-0004", "private-authority-source");
        var target = Card("S01-0004", "private-authority-target");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][1] = target;
        QueueReady(game, source, target, publicSource: false);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "target-selected");
        Assert.DoesNotContain(game.SnapshotForSpectator().RecentEvents,
            entry => entry.Type == "target-selected");
    }

    [Fact]
    public void PublicSelectionKeepsTheOriginalTargetAcrossMovementRecoveryAndAllRecipients()
    {
        var game = Create();
        var source = Card("S01-0004", "selection-source");
        var target = Card("S01-0004", "selection-target");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[1][2] = target;
        var item = Push(game, source, [target.InstanceId]);
        var original = Assert.Single(game.State.Events, entry => entry.Type == "target-selected");
        var fact = Assert.Single(Assert.IsType<L12PlayerSelectedTargets>(original.PlayerSelectedTargets).Facts);
        Assert.Equal(target.InstanceId, fact.Id);
        Assert.Equal(1, fact.Owner);
        Assert.Equal((1, 2), (fact.Row, fact.Slot));
        Assert.Equal(target.Name, fact.PublicName);
        Assert.Equal(target.CurrentCost, fact.CurrentCost);
        foreach (var view in new[] { game.SnapshotFor(0), game.SnapshotFor(1),
                     game.SnapshotForSpectator(), game.SnapshotForReferee(), game.SnapshotForGm(0) })
        {
            var publicEvent = Assert.Single(view.RecentEvents, entry => entry.Type == "target-selected");
            Assert.Equal(fact, Assert.Single(publicEvent.PlayerSelectedTargets!.Facts));
            Assert.DoesNotContain("真实玩家姓名", JsonSerializer.Serialize(publicEvent));
        }

        target.Hidden = true;
        game.State.Players[1].Field[1][2] = null;
        game.State.Players[1].Graveyard.Add(target);
        var describe = typeof(L12GameEngine).GetMethod("DescribeResponse", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var after = Assert.IsType<string>(describe.Invoke(game, [item, 1]));
        Assert.Contains($"你的后排右格〈{fact.PublicName}〉", after);
        Assert.DoesNotContain("盖伏卡牌", after);
        Assert.Equal(fact, Assert.Single(game.SnapshotForSpectator().RecentEvents
            .Single(entry => entry.Type == "target-selected").PlayerSelectedTargets!.Facts));

        var restored = L12GameEngine.RestoreCheckpoint(Catalog,
            game.SerializeFullState(), game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        var restoredFact = Assert.Single(restored.SnapshotFor(1).RecentEvents
            .Single(entry => entry.Type == "target-selected").PlayerSelectedTargets!.Facts);
        Assert.Equal(fact, restoredFact);
    }

    [Fact]
    public void CoveredAndPrivateTargetsNeverPublishIdentityOrPrivateBrowseFacts()
    {
        var game = Create();
        var source = Card("S01-0004", "covered-source");
        var covered = Card("S01-0019", "covered-target");
        covered.Hidden = true;
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[1][0] = covered;
        Push(game, source, [covered.InstanceId]);
        var selected = Assert.Single(game.SnapshotFor(0).RecentEvents,
            entry => entry.Type == "target-selected");
        var fact = Assert.Single(selected.PlayerSelectedTargets!.Facts);
        Assert.Null(fact.PublicName);
        Assert.Null(fact.CurrentCost);
        Assert.DoesNotContain(covered.Name, JsonSerializer.Serialize(selected));

        var privateGame = Create();
        var privateSource = Card("S01-0004", "private-source");
        var handTarget = Card("S01-0004", "private-hand-target");
        privateGame.State.Players[0].Field[0][0] = privateSource;
        privateGame.State.Players[1].Hand.Add(handTarget);
        Push(privateGame, privateSource, [handTarget.InstanceId]);
        Assert.DoesNotContain(privateGame.State.Events, entry => entry.Type == "target-selected");
        Assert.DoesNotContain(privateGame.SnapshotForSpectator().RecentEvents,
            entry => entry.Type == "target-selected");
    }

    [Fact]
    public void PreviouslyFrozenAndMultipleTargetsUseOneSelectionEvent()
    {
        var game = Create();
        var source = Card("S01-0004", "multiple-source");
        var first = Card("S01-0004", "multiple-first");
        var second = Card("S01-0004", "multiple-second");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][1] = first;
        game.State.Players[1].Field[1][2] = second;
        var data = new Dictionary<string, string>();
        var capture = typeof(L12GameEngine).GetMethod("CaptureResponsePublicTargetSnapshot",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        capture.Invoke(game, [data, new[] { first.InstanceId, second.InstanceId }]);

        Push(game, source, [first.InstanceId, second.InstanceId], data);
        var selected = Assert.Single(game.State.Events, entry => entry.Type == "target-selected");
        Assert.Equal([first.InstanceId, second.InstanceId],
            selected.PlayerSelectedTargets!.Facts.Select(fact => fact.Id));
        Assert.Contains("前排中格", Assert.Single(game.State.PendingPrompts).Text);
        Assert.Contains("后排右格", game.State.PendingPrompts[0].Text);
    }

    [Fact]
    public void RealMerlinDeclarationPublishesChoiceBeforeTheFirstResponse()
    {
        var game = Create();
        var source = Card("S02-0603", "merlin-selected-source");
        var target = Card("S01-0004", "merlin-selected-target");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].SpecialZones.Runes = 1;
        game.State.Players[1].Field[1][2] = target;
        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "merlinRune")).Accepted);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: mode.PromptId,
            Choice: "mode:debuff")).Accepted);
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: targetPrompt.PromptId,
            Choice: target.InstanceId)).Accepted);

        var selected = Assert.Single(game.State.Events, entry => entry.Type == "target-selected"
            && entry.PlayerSelectedTargets?.SourceInstanceId == source.InstanceId);
        Assert.Equal(target.InstanceId, Assert.Single(selected.PlayerSelectedTargets!.Facts).Id);
        Assert.True(selected.Sequence < game.State.Events.Single(entry => entry.Type == "effect-activation"
            && entry.Cards.Any(card => card.InstanceId == source.InstanceId)).Sequence);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", response.Kind);
        Assert.Contains($"后排右格〈{target.Name}〉", response.Text);
    }

    [Fact]
    public void RecipientProjectionRejectsPrivateOrMismatchedTargetClaims()
    {
        var game = Create();
        var source = Card("S01-0004", "sanitized-source");
        var selected = new L12ActionEvent(99, "target-selected", 0, "公开目标选择", [source])
        {
            PlayerSelectedTargets = new(source.InstanceId,
            [new("public", 1, "field", 0, 0, "公开对象", 2, false),
             new("private", 1, "hand", -1, -1, "私密手牌", null, false)]),
        };
        foreach (var viewer in new[] { 0, 1, -1 })
        {
            var projected = L12RecipientVisibility.ProjectActionEvent(game.State, selected,
                viewer, revealAllDisasters: false);
            Assert.Equal("public", Assert.Single(projected.PlayerSelectedTargets!.Facts).Id);
            Assert.DoesNotContain("私密手牌", JsonSerializer.Serialize(projected));
        }
        var hiddenCard = source.Clone();
        hiddenCard.Hidden = true;
        var hiddenSource = selected with { Cards = [hiddenCard] };
        Assert.Null(L12RecipientVisibility.ProjectActionEvent(game.State, hiddenSource,
            1, revealAllDisasters: false).PlayerSelectedTargets);
        var wrongType = selected with { Type = "response" };
        Assert.Null(L12RecipientVisibility.ProjectActionEvent(game.State, wrongType,
            1, revealAllDisasters: false).PlayerSelectedTargets);
    }
}
