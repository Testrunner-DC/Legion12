using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class RecipientPrivacyMatrixAdversarialTests
{
    private const int Seed = 2026092801;
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [Trait("L12Evidence", "lc05a:live-checkpoint-four-recipient-matrix")]
    public void LiveAndCheckpointViewsKeepEveryPrivateDomainInsideItsRecipientBoundary()
    {
        var fixture = CreateFixture("lc05a-live-checkpoint");
        var before = CaptureViews(fixture.Game);

        AssertLivePrivateMatrix(before, fixture);

        var restored = L12GameEngine.RestoreCheckpoint(Catalog, fixture.Game.SerializeFullState(),
            fixture.Game.RandomState ?? new L12RandomState(Seed, 1, 2, 3, 4, 0),
            fixture.Game.CardFactSignalSequence, fixture.Game.AutoPassEmptyResponses,
            fixture.Game.ConcealHiddenResponseAvailability);
        var after = CaptureViews(restored);

        AssertJsonEqual(before.Player0, after.Player0);
        AssertJsonEqual(before.Player1, after.Player1);
        AssertJsonEqual(before.Spectator, after.Spectator);
        AssertJsonEqual(before.Referee, after.Referee);
        AssertLivePrivateMatrix(after, fixture);
    }

    [Fact]
    [Trait("L12Evidence", "referee:display-identities-without-live-actions")]
    public void RefereePublicCardZonesKeepStaticFacesButNeverCarryExecutableActionState()
    {
        var fixture = CreateFixture("referee-display-only-card-zones");
        var player = fixture.Game.State.Players[0];
        var graveyard = Card("S01-0103", "referee-graveyard", 0);
        var relic = Card("S01-0103", "referee-relic", 0);
        var extraRelic = Card("S01-0103", "referee-extra-relic", 0);
        var resolving = Card("S01-0103", "referee-resolving", 0);
        var canopic = Card("S01-0103", "referee-canopic", 0);
        var libraryTop = Card("S01-0103", "referee-library-top", 0);
        foreach (var card in new[] { fixture.Player0Hand, fixture.Player0Covered, fixture.Player0Trial,
                     graveyard, relic, extraRelic, resolving, canopic, libraryTop })
        {
            card.Abilities.Add(new L12AbilityView("referee-live-ability", "实时能力", false, "私密禁用原因"));
            card.RuleActions.Add(new L12RuleActionView("referee-live-rule", "规则动作", "实时目标", false,
                "私密规则原因", TargetKeys: ["private-target"]));
            card.PlayCost = 7;
            card.MinimumPlayCost = 1;
            card.PlayBlockedReason = "私密出牌阻断";
            card.SpendableResourceType = "morale";
        }
        relic.AttachedCards.Add(Card("S01-0103", "referee-attached", 0));
        relic.AttachedCards[0].RuleActions.Add(new L12RuleActionView("attached-live-rule", "叠放动作", "实时"));
        player.Graveyard.Add(graveyard);
        player.Relic = relic;
        player.ExtraRelics.Add(extraRelic);
        player.Resolving.Add(resolving);
        player.SpecialZones.CanopicProgress.Add(canopic);
        player.Library.Add(libraryTop);
        fixture.Player0Disaster.RuleActions.Add(new L12RuleActionView("disaster-live-rule", "天灾动作", "实时"));
        fixture.Game.State.ActiveDisaster = fixture.Player0Disaster;
        fixture.Game.State.BannedDisasters.Add(fixture.Player0Disaster);
        fixture.Game.State.RemovedDisasters.Add(fixture.Player0Disaster);
        fixture.Game.State.RevealedDisasters.Add(fixture.Player0Disaster);

        var refereeSnapshot = Serialize(fixture.Game.SnapshotForReferee());
        var referee = refereeSnapshot.GetProperty("players")[0];
        var gm = Serialize(fixture.Game.SnapshotForGm(0)).GetProperty("players")[0];
        var normal = Serialize(fixture.Game.SnapshotFor(0)).GetProperty("players")[0];
        Assert.Empty(referee.GetProperty("master").GetProperty("abilities").EnumerateArray());
        Assert.Empty(referee.GetProperty("factionEffect").GetProperty("abilities").EnumerateArray());
        Assert.Equal(normal.GetProperty("master").GetProperty("abilities").GetRawText(),
            gm.GetProperty("master").GetProperty("abilities").GetRawText());
        Assert.Equal(normal.GetProperty("factionEffect").GetProperty("abilities").GetRawText(),
            gm.GetProperty("factionEffect").GetProperty("abilities").GetRawText());

        foreach (var card in new[]
        {
            referee.GetProperty("hand")[0], referee.GetProperty("field")[1][0],
            referee.GetProperty("graveyard")[0], referee.GetProperty("specialZones").GetProperty("trials")[0],
            referee.GetProperty("relic"), referee.GetProperty("extraRelics")[0],
            referee.GetProperty("resolving")[0], referee.GetProperty("specialZones").GetProperty("canopicProgress")[0],
        })
            AssertDisplayOnlyCard(card);
        AssertDisplayOnlyCard(referee.GetProperty("relic").GetProperty("attachedCards")[0]);
        Assert.Equal("referee-graveyard", referee.GetProperty("graveyard")[0].GetProperty("instanceId").GetString());
        Assert.Equal("referee-relic", referee.GetProperty("relic").GetProperty("instanceId").GetString());
        Assert.True(referee.GetProperty("field")[1][0].GetProperty("hidden").GetBoolean());
        Assert.True(referee.GetProperty("field")[1][0].GetProperty("identityKnown").GetBoolean());
        Assert.False(referee.TryGetProperty("spendableResourceCount", out _));
        Assert.True(gm.GetProperty("relic").GetProperty("ruleActions").GetArrayLength() > 0);
        Assert.True(normal.GetProperty("extraRelics")[0].GetProperty("ruleActions").GetArrayLength() > 0);
        Assert.True(gm.GetProperty("specialZones").GetProperty("trials")[0]
            .GetProperty("ruleActions").GetArrayLength() > 0);
        foreach (var zone in new[] { "activeDisaster", "bannedDisasters", "removedDisasters",
                     "revealedDisasters", "chosenDisasters", "sessionDisasters" })
        {
            var value = refereeSnapshot.GetProperty(zone);
            IEnumerable<JsonElement> cards = value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().ToArray() : [value];
            foreach (var card in cards.Where(card => card.ValueKind == JsonValueKind.Object
                         && card.TryGetProperty("cardId", out _)))
                AssertDisplayOnlyCard(card);
        }
    }

    private static void AssertDisplayOnlyCard(JsonElement card)
    {
        Assert.Empty(card.GetProperty("abilities").EnumerateArray());
        Assert.Empty(card.GetProperty("ruleActions").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, card.GetProperty("playCost").ValueKind);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("minimumPlayCost").ValueKind);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("playBlockedReason").ValueKind);
        Assert.False(card.TryGetProperty("spendableResourceType", out _));
    }

    [Fact]
    [Trait("L12Evidence", "lc05a:persisted-history-player-replay-matrix")]
    public async Task LaterDisclosureDoesNotRewriteHistoricalFramesOrLeakAcrossPlayerReplay()
    {
        var fixture = CreateFixture("lc05a-history-replay");
        var initialViews = CaptureViews(fixture.Game);
        var savedSnapshots = new[]
        {
            fixture.Game.SnapshotFor(0), fixture.Game.SnapshotFor(1),
            fixture.Game.SnapshotForSpectator(), fixture.Game.SnapshotForReferee(),
        };
        var savedSnapshotBytes = savedSnapshots
            .Select(snapshot => JsonSerializer.SerializeToUtf8Bytes(snapshot))
            .ToArray();
        var directory = Path.Combine(Path.GetTempPath(), "l12-lc05a-recipient-privacy",
            Guid.NewGuid().ToString("N"));
        await using var recorder = new MatchRecorder(Path.Combine(directory, "matches.db"));
        await recorder.InitializeAsync();
        await recorder.StartAsync(fixture.Game);
        var blockedCommand = new L12Command("endTurn");
        var blocked = fixture.Game.Handle(0, blockedCommand);
        Assert.False(blocked.Accepted);
        await recorder.AppendAsync(fixture.Game, 1, 0,
            JsonSerializer.Serialize(blockedCommand), blocked);

        var firstStoredBefore = Assert.IsType<L12MatchDetail>(
            await recorder.GetMatchAsync(fixture.Game.State.MatchId)).Commands[0].State.GetRawText();

        PublishPreviouslyPrivateFacts(fixture);
        fixture.Game.ConcludeByAuthority(0, "LC-05A 合成回放结束");
        Assert.True(await recorder.CompleteAsync(fixture.Game));

        for (var index = 0; index < savedSnapshots.Length; index++)
            Assert.Equal(savedSnapshotBytes[index], JsonSerializer.SerializeToUtf8Bytes(savedSnapshots[index]));

        var firstStoredAfter = Assert.IsType<L12MatchDetail>(
            await recorder.GetMatchAsync(fixture.Game.State.MatchId)).Commands[0].State.GetRawText();
        Assert.Equal(firstStoredBefore, firstStoredAfter);

        AssertPublicFactsAreVisibleOnlyInNewSnapshots(CaptureViews(fixture.Game), fixture);

        var player0Replay = Assert.IsType<L12MatchDetail>(
            await recorder.GetMatchForPlayerAsync(fixture.Game.State.MatchId, "甲"));
        var player1Replay = Assert.IsType<L12MatchDetail>(
            await recorder.GetMatchForPlayerAsync(fixture.Game.State.MatchId, "乙"));
        Assert.Equal(0, player0Replay.ViewerPlayerIndex);
        Assert.Equal(1, player1Replay.ViewerPlayerIndex);
        var player0Leaks = AssertReplayPrivateMatrix(player0Replay, fixture, viewer: 0,
            initialViews.Player0);
        var player1Leaks = AssertReplayPrivateMatrix(player1Replay, fixture, viewer: 1,
            initialViews.Player1);
        Assert.True(player0Leaks.Length == 0 && player1Leaks.Length == 0,
            $"玩家0历史首帧泄露：{string.Join(", ", player0Leaks)}；"
            + $"玩家1历史首帧泄露：{string.Join(", ", player1Leaks)}");
    }

    private static PrivacyFixture CreateFixture(string matchId)
    {
        var game = new L12GameEngine(Catalog, matchId, "LC05A", Seed, ["甲", "乙"], [0, 0],
            skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.PendingPrompts.Clear();
        game.State.EffectStack.Clear();
        game.State.Events.Clear();
        game.State.Log.Clear();
        game.State.LastAction = null;
        game.State.EventSequence = 0;
        game.State.ChosenDisasters.Clear();
        game.State.RevealedDisasters.Clear();
        game.State.ChosenDisasterOwners.Clear();
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Removed.Clear();
            player.Resolving.Clear();
            player.SpecialZones.Trials.Clear();
            foreach (var row in player.Field) Array.Clear(row);
        }

        var player0Hand = Card("S01-0103", "lc05a-p0-hand", 0);
        var player1Hand = Card("S01-0205", "lc05a-p1-hand", 1);
        game.State.Players[0].Hand.Add(player0Hand);
        game.State.Players[1].Hand.Add(player1Hand);

        var player0Covered = Card("S02-0523", "lc05a-p0-covered", 0);
        var player1Covered = Card("S02-0522", "lc05a-p1-covered", 1);
        player0Covered.Hidden = true;
        player1Covered.Hidden = true;
        game.State.Players[0].Field[1][0] = player0Covered;
        game.State.Players[1].Field[1][0] = player1Covered;

        var player0Trial = Card("S02-06S4", "lc05a-p0-trial", 0);
        var player1Trial = Card("S02-06S5", "lc05a-p1-trial", 1);
        player0Trial.TrialProgress = 2;
        player1Trial.TrialProgress = 3;
        game.State.Players[0].SpecialZones.Trials.Add(player0Trial);
        game.State.Players[1].SpecialZones.Trials.Add(player1Trial);

        var player0Disaster = Card("S01-DS01", "lc05a-p0-disaster", 0);
        var player1Disaster = Card("S01-DS02", "lc05a-p1-disaster", 1);
        game.State.ChosenDisasters.AddRange([player0Disaster, player1Disaster]);
        game.State.ChosenDisasterOwners[player0Disaster.InstanceId] = 0;
        game.State.ChosenDisasterOwners[player1Disaster.InstanceId] = 1;

        var prompt = new L12Prompt
        {
            PromptId = "lc05a-owner-prompt",
            PlayerIndex = 0,
            Kind = "hand-card",
            Text = "lc05a-owner-prompt-text",
            ValidChoices = [player0Hand.InstanceId],
            MinChoose = 1,
            MaxChoose = 1,
            IsPrivate = true,
            Continuation = "lc05a-private-continuation",
            Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [player0Hand.InstanceId] = "lc05a-owner-data-secret",
            },
            ChoiceLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [player0Hand.InstanceId] = "lc05a-owner-label-secret",
            },
            Presentation = new L12PromptPresentation
            {
                Title = "lc05a-owner-title-secret",
                Situation = "lc05a-owner-situation-secret",
                Instruction = "lc05a-owner-instruction-secret",
                WaitingSummary = "对手正在选择卡牌",
                ChoiceConsequences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [player0Hand.InstanceId] = "lc05a-owner-consequence-secret",
                },
            },
        };
        game.State.PendingPrompts.Add(prompt);
        AddEvent(game, "private-return", 0, "lc05a-p0-private-return-secret", player0Hand);
        AddEvent(game, "private-return", 1, "lc05a-p1-private-return-secret", player1Hand);
        AddEvent(game, "private-disaster-reveal", 0, "lc05a-p0-disaster-view-secret", player0Disaster);
        AddEvent(game, "private-disaster-reveal", 1, "lc05a-p1-disaster-view-secret", player1Disaster);
        AddEvent(game, "disaster-selected", 0, "lc05a-p0-disaster-selected-secret", player0Disaster);
        AddEvent(game, "disaster-selected", 1, "lc05a-p1-disaster-selected-secret", player1Disaster);

        return new(game, prompt, player0Hand, player1Hand, player0Covered, player1Covered,
            player0Trial, player1Trial, player0Disaster, player1Disaster);
    }

    private static void PublishPreviouslyPrivateFacts(PrivacyFixture fixture)
    {
        typeof(L12GameEngine).GetMethod("AddResolvedPromptLog",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(fixture.Game, [fixture.Prompt, new[] { fixture.Player0Hand.InstanceId }]);
        var audit = Assert.Single(fixture.Game.State.Events, item => item.Type == "prompt-resolved");
        Assert.Equal("甲 已完成非公开选择", audit.Text);
        Assert.Empty(audit.Cards);
        Assert.DoesNotContain("lc05a-owner", audit.Text, StringComparison.Ordinal);
        fixture.Game.State.PendingPrompts.Clear();

        fixture.Player0Covered.Hidden = false;
        fixture.Player1Covered.Hidden = false;
        fixture.Player0Trial.TrialCompleted = true;
        fixture.Player1Trial.TrialCompleted = true;
        fixture.Game.State.RevealedDisasters.AddRange(
            [fixture.Player0Disaster, fixture.Player1Disaster]);
    }

    private static void AssertLivePrivateMatrix(RecipientViews views, PrivacyFixture fixture)
    {
        AssertPlayerOwnsOnlyTheirHandAndCoveredIdentity(views.Player0, fixture, 0);
        AssertPlayerOwnsOnlyTheirHandAndCoveredIdentity(views.Player1, fixture, 1);
        AssertPublicViewerHasNoHandOrCoveredIdentity(views.Spectator, fixture);
        AssertRefereeSeesBothHandsAndCoveredIdentityWithoutActions(views.Referee, fixture);

        AssertTrialVisibility(views.Player0, fixture.Player0Trial, visible: true);
        AssertTrialVisibility(views.Player0, fixture.Player1Trial, visible: false);
        AssertTrialVisibility(views.Player1, fixture.Player0Trial, visible: false);
        AssertTrialVisibility(views.Player1, fixture.Player1Trial, visible: true);
        AssertTrialVisibility(views.Spectator, fixture.Player0Trial, visible: false);
        AssertTrialVisibility(views.Spectator, fixture.Player1Trial, visible: false);
        // SnapshotForReferee is the current internal authority view: both unfinished trials are intentional.
        AssertTrialVisibility(views.Referee, fixture.Player0Trial, visible: true);
        AssertTrialVisibility(views.Referee, fixture.Player1Trial, visible: true);

        AssertDisasterVisibility(views.Player0, fixture.Player0Disaster, visible: true);
        AssertDisasterVisibility(views.Player0, fixture.Player1Disaster, visible: false);
        AssertDisasterVisibility(views.Player1, fixture.Player0Disaster, visible: false);
        AssertDisasterVisibility(views.Player1, fixture.Player1Disaster, visible: true);
        AssertDisasterVisibility(views.Spectator, fixture.Player0Disaster, visible: false);
        AssertDisasterVisibility(views.Spectator, fixture.Player1Disaster, visible: false);
        AssertDisasterVisibility(views.Referee, fixture.Player0Disaster, visible: true);
        AssertDisasterVisibility(views.Referee, fixture.Player1Disaster, visible: true);

        var ownerPrompt = Assert.Single(views.Player0.GetProperty("prompts").EnumerateArray());
        Assert.Equal("lc05a-owner-label-secret", ownerPrompt.GetProperty("choiceLabels")
            .GetProperty(fixture.Player0Hand.InstanceId).GetString());
        Assert.Equal("lc05a-owner-consequence-secret", ownerPrompt.GetProperty("presentation")
            .GetProperty("choiceConsequences").GetProperty(fixture.Player0Hand.InstanceId).GetString());
        Assert.NotEqual(ownerPrompt.GetProperty("choiceLabels").GetRawText(),
            ownerPrompt.GetProperty("presentation").GetProperty("choiceConsequences").GetRawText());
        Assert.Equal(JsonValueKind.Null, views.Player0.GetProperty("waitingPrompt").ValueKind);

        foreach (var waitingView in new[] { views.Player1, views.Spectator, views.Referee })
            AssertWaitingPromptIsExactSafeWhitelist(waitingView);
        foreach (var hiddenView in new[] { views.Player1, views.Spectator, views.Referee })
        {
            Assert.Empty(hiddenView.GetProperty("prompts").EnumerateArray());
            var json = hiddenView.GetRawText();
            foreach (var secret in OwnerPromptSecrets(fixture))
            {
                if (hiddenView.Equals(views.Referee) && secret == fixture.Player0Hand.InstanceId)
                    continue; // The card is visible, but its private choice and prompt remain hidden.
                Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
            }
        }
    }

    private static void AssertPublicFactsAreVisibleOnlyInNewSnapshots(RecipientViews views,
        PrivacyFixture fixture)
    {
        foreach (var view in new[] { views.Player0, views.Player1, views.Spectator, views.Referee })
        {
            AssertCoveredVisibility(view, fixture.Player0Covered, visible: true);
            AssertCoveredVisibility(view, fixture.Player1Covered, visible: true);
            AssertTrialVisibility(view, fixture.Player0Trial, visible: true);
            AssertTrialVisibility(view, fixture.Player1Trial, visible: true);
            AssertDisasterVisibility(view, fixture.Player0Disaster, visible: true);
            AssertDisasterVisibility(view, fixture.Player1Disaster, visible: true);
            Assert.Empty(view.GetProperty("prompts").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, view.GetProperty("waitingPrompt").ValueKind);
        }
        AssertPlayerOwnsOnlyTheirHandAndCoveredIdentity(views.Player0, fixture, 0,
            coveredNowPublic: true);
        AssertPlayerOwnsOnlyTheirHandAndCoveredIdentity(views.Player1, fixture, 1,
            coveredNowPublic: true);
        AssertPublicViewerHasNoHandOrCoveredIdentity(views.Spectator, fixture,
            coveredNowPublic: true);
        AssertRefereeSeesBothHandsAndCoveredIdentityWithoutActions(views.Referee, fixture,
            coveredNowPublic: true);
    }

    private static string[] AssertReplayPrivateMatrix(L12MatchDetail replay, PrivacyFixture fixture,
        int viewer, JsonElement initialLiveView)
    {
        var firstState = Assert.Single(replay.Commands, command => command.Sequence == 1).State;
        var json = firstState.GetRawText();
        var own = viewer == 0
            ? new[] { fixture.Player0Hand.InstanceId, fixture.Player0Covered.InstanceId,
                fixture.Player0Trial.InstanceId, fixture.Player0Disaster.InstanceId }
            : new[] { fixture.Player1Hand.InstanceId, fixture.Player1Covered.InstanceId,
                fixture.Player1Trial.InstanceId, fixture.Player1Disaster.InstanceId };
        var forbidden = viewer == 0
            ? new[] { fixture.Player1Hand.InstanceId, fixture.Player1Covered.CardId,
                fixture.Player1Trial.CardId, fixture.Player1Trial.InstanceId,
                fixture.Player1Disaster.CardId,
                "lc05a-p1-private-return-secret", "lc05a-p1-disaster-view-secret",
                "lc05a-p1-disaster-selected-secret" }
            : new[] { fixture.Player0Hand.InstanceId, fixture.Player0Covered.CardId,
                fixture.Player0Trial.CardId, fixture.Player0Trial.InstanceId,
                fixture.Player0Disaster.CardId,
                "lc05a-p0-private-return-secret", "lc05a-p0-disaster-view-secret",
                "lc05a-p0-disaster-selected-secret" }
                .Concat(OwnerPromptSecrets(fixture)).ToArray();

        foreach (var visible in own) Assert.Contains(visible, json, StringComparison.Ordinal);
        Assert.Empty(firstState.GetProperty("PendingPrompts").EnumerateArray());
        Assert.Empty(firstState.GetProperty("PendingActivations").EnumerateArray());
        AssertReplayMatchesLiveRecipient(initialLiveView, firstState);
        return forbidden.Distinct(StringComparer.Ordinal)
            .Where(secret => json.Contains(secret, StringComparison.Ordinal)).ToArray();
    }

    private static void AssertReplayMatchesLiveRecipient(JsonElement live, JsonElement replay)
    {
        Assert.Equal(
            live.GetProperty("chosenDisasters").EnumerateArray().Select(DisasterProjection).ToArray(),
            replay.GetProperty("ChosenDisasters").EnumerateArray().Select(DisasterProjection).ToArray());
        Assert.Equal(
            live.GetProperty("recentEvents").EnumerateArray().Select(EventProjection).ToArray(),
            replay.GetProperty("Events").EnumerateArray().Select(EventProjection).ToArray());
    }

    private static string DisasterProjection(JsonElement disaster)
        => $"{Property(disaster, "InstanceId", "instanceId").GetString()}|"
           + $"{OptionalString(disaster, "CardId", "cardId")}|"
           + $"{OptionalBoolean(disaster, "Hidden", "hidden")}";

    private static string EventProjection(JsonElement actionEvent)
    {
        var cards = Property(actionEvent, "Cards", "cards").EnumerateArray()
            .Select(card => $"{Property(card, "InstanceId", "instanceId").GetString()}|"
                            + OptionalString(card, "CardId", "cardId"));
        return $"{Property(actionEvent, "Sequence", "sequence").GetInt64()}|"
               + $"{Property(actionEvent, "Type", "type").GetString()}|"
               + $"{OptionalInt(actionEvent, "PlayerIndex", "playerIndex")}|"
               + $"{Property(actionEvent, "Text", "text").GetString()}|{string.Join(",", cards)}";
    }

    private static JsonElement Property(JsonElement value, string pascal, string camel)
        => value.TryGetProperty(pascal, out var result) ? result : value.GetProperty(camel);

    private static string OptionalString(JsonElement value, string pascal, string camel)
        => value.TryGetProperty(pascal, out var result) || value.TryGetProperty(camel, out result)
            ? result.GetString() ?? string.Empty
            : string.Empty;

    private static bool OptionalBoolean(JsonElement value, string pascal, string camel)
        => (value.TryGetProperty(pascal, out var result) || value.TryGetProperty(camel, out result))
            && result.GetBoolean();

    private static int? OptionalInt(JsonElement value, string pascal, string camel)
        => value.TryGetProperty(pascal, out var result) || value.TryGetProperty(camel, out result)
            ? result.ValueKind == JsonValueKind.Null ? null : result.GetInt32()
            : null;

    private static void AssertPlayerOwnsOnlyTheirHandAndCoveredIdentity(JsonElement snapshot,
        PrivacyFixture fixture, int viewer, bool coveredNowPublic = false)
    {
        var ownHand = viewer == 0 ? fixture.Player0Hand : fixture.Player1Hand;
        var otherHand = viewer == 0 ? fixture.Player1Hand : fixture.Player0Hand;
        var players = snapshot.GetProperty("players");
        Assert.Contains(players[viewer].GetProperty("hand").EnumerateArray(),
            card => card.GetProperty("instanceId").GetString() == ownHand.InstanceId);
        Assert.False(players[1 - viewer].TryGetProperty("hand", out _));
        Assert.DoesNotContain(otherHand.InstanceId, snapshot.GetRawText(), StringComparison.Ordinal);
        AssertCoveredVisibility(snapshot, viewer == 0 ? fixture.Player0Covered : fixture.Player1Covered,
            visible: true);
        AssertCoveredVisibility(snapshot, viewer == 0 ? fixture.Player1Covered : fixture.Player0Covered,
            visible: coveredNowPublic);
    }

    private static void AssertPublicViewerHasNoHandOrCoveredIdentity(JsonElement snapshot,
        PrivacyFixture fixture, bool coveredNowPublic = false)
    {
        foreach (var player in snapshot.GetProperty("players").EnumerateArray())
            Assert.False(player.TryGetProperty("hand", out _));
        Assert.DoesNotContain(fixture.Player0Hand.InstanceId, snapshot.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Player1Hand.InstanceId, snapshot.GetRawText(), StringComparison.Ordinal);
        AssertCoveredVisibility(snapshot, fixture.Player0Covered, visible: coveredNowPublic);
        AssertCoveredVisibility(snapshot, fixture.Player1Covered, visible: coveredNowPublic);
    }

    private static void AssertRefereeSeesBothHandsAndCoveredIdentityWithoutActions(JsonElement snapshot,
        PrivacyFixture fixture, bool coveredNowPublic = false)
    {
        var players = snapshot.GetProperty("players");
        Assert.Contains(players[0].GetProperty("hand").EnumerateArray(),
            card => card.GetProperty("instanceId").GetString() == fixture.Player0Hand.InstanceId);
        Assert.Contains(players[1].GetProperty("hand").EnumerateArray(),
            card => card.GetProperty("instanceId").GetString() == fixture.Player1Hand.InstanceId);
        Assert.All(players.EnumerateArray(), player =>
        {
            Assert.False(player.TryGetProperty("moraleDeck", out _));
            Assert.False(player.TryGetProperty("promotionOptions", out _));
            Assert.All(player.GetProperty("hand").EnumerateArray(), card =>
            {
                Assert.Equal(JsonValueKind.Null, card.GetProperty("playCost").ValueKind);
                Assert.Empty(card.GetProperty("abilities").EnumerateArray());
                Assert.Empty(card.GetProperty("ruleActions").EnumerateArray());
            });
        });
        AssertCoveredVisibility(snapshot, fixture.Player0Covered, visible: true);
        AssertCoveredVisibility(snapshot, fixture.Player1Covered, visible: true);
        Assert.Empty(snapshot.GetProperty("legalAttackTargets").EnumerateObject());
        Assert.Empty(snapshot.GetProperty("prompts").EnumerateArray());
        foreach (var actionEvent in snapshot.GetProperty("recentEvents").EnumerateArray())
            if (actionEvent.GetProperty("type").GetString() == "return")
                Assert.Empty(actionEvent.GetProperty("cards").EnumerateArray());
        if (!coveredNowPublic)
        {
            var covered = players.EnumerateArray().SelectMany(player => player.GetProperty("field").EnumerateArray())
                .SelectMany(row => row.EnumerateArray()).Where(card => card.ValueKind == JsonValueKind.Object
                    && card.GetProperty("hidden").GetBoolean()).ToArray();
            Assert.All(covered, card => Assert.True(card.GetProperty("identityKnown").GetBoolean()));
        }
    }

    private static void AssertCoveredVisibility(JsonElement snapshot, L12CardInstance card, bool visible)
    {
        var matches = snapshot.GetProperty("players").EnumerateArray()
            .SelectMany(player => player.GetProperty("field").EnumerateArray())
            .SelectMany(row => row.EnumerateArray())
            .Where(item => item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("instanceId", out var id)
                && id.GetString() == card.InstanceId)
            .ToArray();
        var projected = Assert.Single(matches);
        Assert.Equal(visible ? card.CardId : "hidden-card",
            projected.GetProperty("cardId").GetString());
        Assert.Equal(card.Hidden, projected.GetProperty("hidden").GetBoolean());
        Assert.Equal(visible && card.Hidden, projected.GetProperty("identityKnown").GetBoolean());
    }

    private static void AssertTrialVisibility(JsonElement snapshot, L12CardInstance card, bool visible)
    {
        var matches = snapshot.GetProperty("players").EnumerateArray()
            .SelectMany(player => player.GetProperty("specialZones").GetProperty("trials").EnumerateArray())
            .Where(item => item.GetProperty("instanceId").GetString() == card.InstanceId)
            .ToArray();
        var projected = Assert.Single(matches);
        Assert.Equal(visible ? card.CardId : "hidden-trial", projected.GetProperty("cardId").GetString());
    }

    private static void AssertDisasterVisibility(JsonElement snapshot, L12CardInstance card, bool visible)
    {
        var projected = Assert.Single(snapshot.GetProperty("chosenDisasters").EnumerateArray(),
            item => item.GetProperty("instanceId").GetString() == card.InstanceId);
        Assert.Equal(visible, projected.TryGetProperty("cardId", out var cardId)
            && cardId.GetString() == card.CardId);
    }

    private static void AssertWaitingPromptIsExactSafeWhitelist(JsonElement snapshot)
    {
        var waiting = snapshot.GetProperty("waitingPrompt");
        Assert.Equal(new[] { "kind", "playerIndex", "playerName", "waitingSummary" },
            waiting.EnumerateObject().Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal(0, waiting.GetProperty("playerIndex").GetInt32());
        Assert.Equal("甲", waiting.GetProperty("playerName").GetString());
        Assert.Equal("hand-card", waiting.GetProperty("kind").GetString());
        Assert.Equal("对手正在选择卡牌", waiting.GetProperty("waitingSummary").GetString());
    }

    private static RecipientViews CaptureViews(L12GameEngine game) => new(
        Serialize(game.SnapshotFor(0)), Serialize(game.SnapshotFor(1)),
        Serialize(game.SnapshotForSpectator()), Serialize(game.SnapshotForReferee()));

    private static JsonElement Serialize(object value)
        => JsonSerializer.SerializeToElement(value, WireJson);

    private static void AssertJsonEqual(JsonElement expected, JsonElement actual)
        => Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetRawText()),
                JsonNode.Parse(actual.GetRawText())),
            "检查点恢复前后的接收者投影不一致");

    private static IEnumerable<string> OwnerPromptSecrets(PrivacyFixture fixture) =>
    [
        fixture.Player0Hand.InstanceId,
        "lc05a-owner-prompt-text",
        "lc05a-owner-data-secret",
        "lc05a-owner-label-secret",
        "lc05a-owner-title-secret",
        "lc05a-owner-situation-secret",
        "lc05a-owner-instruction-secret",
        "lc05a-owner-consequence-secret",
        "lc05a-private-continuation",
    ];

    private static void AddEvent(L12GameEngine game, string type, int playerIndex, string text,
        L12CardInstance card)
    {
        var action = new L12ActionEvent(++game.State.EventSequence, type, playerIndex, text,
            [card.Clone()]);
        game.State.Events.Add(action);
        game.State.LastAction = action;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = cardId,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }

    private sealed record RecipientViews(JsonElement Player0, JsonElement Player1,
        JsonElement Spectator, JsonElement Referee);

    private sealed record PrivacyFixture(
        L12GameEngine Game,
        L12Prompt Prompt,
        L12CardInstance Player0Hand,
        L12CardInstance Player1Hand,
        L12CardInstance Player0Covered,
        L12CardInstance Player1Covered,
        L12CardInstance Player0Trial,
        L12CardInstance Player1Trial,
        L12CardInstance Player0Disaster,
        L12CardInstance Player1Disaster);
}
