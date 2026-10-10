using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class BattlePlayerUnderstandingScenarioTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

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

    private static L12GameEngine Create(string suffix)
        => new(Catalog, $"stage5-{suffix}", "STAGE5", 25001,
            ["测试玩家甲", "测试玩家乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);

    private static void PrepareMain(L12GameEngine game)
    {
        game.State.Phase = L12Phase.Main;
        game.State.ActivePlayer = 0;
        foreach (var player in game.State.Players)
        foreach (var row in player.Field) Array.Clear(row);
    }

    private static void Push(L12GameEngine game, L12CardInstance source, string targetId)
    {
        var method = typeof(L12GameEngine).GetMethod("PushEffect",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.IsType<L12StackItem>(method.Invoke(game,
            [0, source, "active", "主动效果", new[] { targetId }, new Dictionary<string, string>()]));
    }

    [Fact]
    public void AuthorityTraceAndRecipientFixturesKeepTheFiveQuestionsAnswerableWithoutPrivateLeaks()
    {
        var mulligan = Create("mulligan");
        Assert.Equal(L12Phase.Mulligan, mulligan.State.Phase);
        Assert.False(mulligan.State.Players[0].MulliganDone);

        // A real command trajectory, including both declaration choices, owns the
        // response text and the public target-selection event.
        var game = Create("declaration");
        PrepareMain(game);
        var source = Card("S02-0603", "stage5-merlin");
        var target = Card("S01-0004", "stage5-public-target");
        var reaction = Card("S01-0016", "stage5-opponent-reaction");
        reaction.Hidden = true;
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[1][2] = target;
        game.State.Players[1].Field[1][0] = reaction;
        game.State.Players[0].SpecialZones.Runes = 1;
        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "merlinRune")).Accepted);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: mode.PromptId,
            Choice: "mode:debuff")).Accepted);
        var choice = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: choice.PromptId,
            Choice: target.InstanceId)).Accepted);
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", response.Kind);
        Assert.Contains($"后排右格〈{target.Name}〉", response.Text);
        var selection = Assert.Single(game.State.Events, item => item.Type == "target-selected");
        Assert.Equal(target.InstanceId,
            Assert.Single(selection.PlayerSelectedTargets!.Facts).Id);
        Assert.True(selection.Sequence < game.State.Events.Single(item => item.Type == "effect-activation"
            && item.Cards.Any(card => card.InstanceId == source.InstanceId)).Sequence);

        var firstPriorityView = game.SnapshotFor(0);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: response.PromptId,
            Choice: "pass")).Accepted);
        var opponentResponse = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, opponentResponse.PlayerIndex);
        Assert.Equal("response", opponentResponse.Kind);
        Assert.Contains($"后排右格〈{target.Name}〉", opponentResponse.Text);

        var actorView = game.SnapshotFor(0);
        var responderView = game.SnapshotFor(1);
        var spectatorView = game.SnapshotForSpectator();
        var refereeView = game.SnapshotForReferee();
        foreach (var view in new[] { actorView, responderView, spectatorView, refereeView })
            Assert.Single(view.RecentEvents, item => item.Type == "target-selected");

        var negated = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        negated.State.PendingPrompts.Clear();
        negated.State.ResponseWindow = null;
        negated.State.EffectStack[^1].Negated = true;
        var resolve = typeof(L12GameEngine).GetMethod("ResolveTopStack",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        resolve.Invoke(negated, null);
        var negatedResult = Assert.Single(negated.State.Events, item => item.Type == "effect-result"
            && item.EffectResultStatus == "negated" && item.Cards.Any(card => card.InstanceId == source.InstanceId));
        Assert.Contains("已支付费用", negatedResult.PlayerLogSemantic?.OutcomeLabel);
        var negatedActorView = negated.SnapshotFor(0);
        var negatedOpponentView = negated.SnapshotFor(1);

        // The already declared object remains the same after it leaves the board.
        game.State.Players[1].Field[1][2] = null;
        game.State.Players[1].Graveyard.Add(target);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        var recoveredView = restored.SnapshotFor(1);
        Assert.Contains($"后排右格〈{target.Name}〉",
            Assert.Single(restored.State.PendingPrompts).Text);
        Assert.Single(recoveredView.RecentEvents, item => item.Type == "target-selected"
            && item.PlayerSelectedTargets!.Facts.Single().Id == target.InstanceId);
        restored.State.PendingPrompts.Clear();
        restored.State.ResponseWindow = null;
        resolve.Invoke(restored, null);
        var invalidatedView = restored.SnapshotFor(1);
        Assert.Contains(restored.State.Events, item => item.Type == "effect-result"
            && item.Cards.Any(card => card.InstanceId == source.InstanceId)
            && item.EffectResultStatus is "failed" or "skipped");

        var coveredGame = Create("covered");
        PrepareMain(coveredGame);
        var coveredSource = Card("S01-0004", "stage5-covered-source");
        var coveredTarget = Card("S01-0019", "stage5-covered-target");
        coveredTarget.Hidden = true;
        coveredGame.State.Players[0].Field[0][0] = coveredSource;
        coveredGame.State.Players[1].Field[0][1] = coveredTarget;
        Push(coveredGame, coveredSource, coveredTarget.InstanceId);
        var coveredPublic = coveredGame.SnapshotForSpectator();
        var coveredFact = Assert.Single(coveredPublic.RecentEvents,
            item => item.Type == "target-selected").PlayerSelectedTargets!.Facts.Single();
        Assert.Null(coveredFact.PublicName);
        Assert.Null(coveredFact.CurrentCost);
        Assert.DoesNotContain(coveredTarget.Name, JsonSerializer.Serialize(coveredPublic, Wire));

        var privateGame = Create("private");
        PrepareMain(privateGame);
        var privateSource = Card("S01-0004", "stage5-private-source");
        var privateTarget = Card("S01-0004", "stage5-private-hand-target");
        privateGame.State.Players[0].Field[0][0] = privateSource;
        privateGame.State.Players[1].Hand.Add(privateTarget);
        Push(privateGame, privateSource, privateTarget.InstanceId);
        var privatePublic = privateGame.SnapshotForSpectator();
        Assert.DoesNotContain(privatePublic.RecentEvents,
            item => item.Type == "target-selected");
        Assert.DoesNotContain(privateTarget.InstanceId,
            JsonSerializer.Serialize(privatePublic.RecentEvents, Wire));

        // Written only for the browser matrix, using synthetic accounts and the
        // server's own projected snapshots. The normal xUnit run stays read-only.
        var output = Environment.GetEnvironmentVariable("L12_STAGE5_FIXTURE_PATH");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            schema = 1,
            sourceName = source.Name,
            targetName = target.Name,
            targetInstanceId = target.InstanceId,
            mulligan = mulligan.SnapshotFor(0),
            firstPriority = firstPriorityView,
            actor = actorView,
            responder = responderView,
            spectator = spectatorView,
            referee = refereeView,
            negatedActor = negatedActorView,
            negatedOpponent = negatedOpponentView,
            recovered = recoveredView,
            invalidated = invalidatedView,
            covered = coveredPublic,
        }, Wire));
    }
}
