using Microsoft.Data.Sqlite;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PharaohFestivalMovementOriginTests
{
    private const string ProtocolFlag = nameof(L12GameState.MovementOriginProtocolEnabled);
    private static readonly L12Catalog Catalog =
        L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void RealFestivalPublishesOneLibraryOriginWithoutLeakingItsPrivateChoice(
        int controller, bool restoreAtGravePrompt)
    {
        var scenario = ArrangeFestival(
            $"festival-origin-{controller}-{restoreAtGravePrompt}",
            26101670 + controller * 10 + (restoreAtGravePrompt ? 1 : 0), controller);
        var game = scenario.Game;
        var player = game.State.Players[controller];
        var libraryCountBefore = player.Library.Count;

        var play = game.Handle(controller,
            new L12Command("playCard", scenario.FestivalInstanceId));
        Assert.True(play.Accepted, play.Error);
        PassResponses(game);

        var handPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("festival-hand", handPrompt.Data["action"]);
        Assert.True(handPrompt.IsPrivate);
        Assert.Contains(scenario.HandChoiceInstanceId, handPrompt.ValidChoices);
        Resolve(game, handPrompt, scenario.HandChoiceInstanceId);

        var gravePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("festival-grave", gravePrompt.Data["action"]);
        Assert.True(gravePrompt.IsPrivate);
        Assert.Contains(scenario.GraveChoiceInstanceId, gravePrompt.ValidChoices);
        Assert.Equal("牌库", gravePrompt.Data[$"{scenario.GraveChoiceInstanceId}:zone"]);
        AssertPrivateLibraryChoice(game, controller, scenario.GraveChoiceInstanceId);
        Assert.Single(game.State.Players[controller].Hand,
            card => card.InstanceId == scenario.HandChoiceInstanceId);

        if (restoreAtGravePrompt)
        {
            game = RestoreExact(game);
            gravePrompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("festival-grave", gravePrompt.Data["action"]);
            Assert.Contains(scenario.GraveChoiceInstanceId, gravePrompt.ValidChoices);
            Assert.Equal("牌库", gravePrompt.Data[$"{scenario.GraveChoiceInstanceId}:zone"]);
        }

        var graveCommand = new L12Command("resolvePrompt", PromptId: gravePrompt.PromptId,
            Choice: scenario.GraveChoiceInstanceId);
        var graveResult = game.Handle(gravePrompt.PlayerIndex, graveCommand);
        Assert.True(graveResult.Accepted, graveResult.Error);

        var discard = Assert.Single(game.State.Events, item => item.Type == "discard"
            && item.Cards.Any(card => card.InstanceId == scenario.GraveChoiceInstanceId));
        Assert.Equal("library", discard.OriginZone);
        Assert.False(Assert.Single(discard.Cards).Hidden);
        Assert.Equal("library", Assert.Single(game.UnpersistedEvents,
            item => item.Sequence == discard.Sequence).OriginZone);
        AssertPublicProjection(game, discard.Sequence, scenario.GraveChoiceInstanceId);

        var beforeDuplicate = StrictState(game);
        var duplicate = game.Handle(gravePrompt.PlayerIndex, graveCommand);
        Assert.False(duplicate.Accepted);
        var afterDuplicate = StrictState(game);
        Assert.Equal(beforeDuplicate.Raw, afterDuplicate.Raw);
        Assert.Equal(beforeDuplicate.Hash, afterDuplicate.Hash);
        Assert.Single(game.State.Players[controller].Graveyard,
            card => card.InstanceId == scenario.GraveChoiceInstanceId);
        Assert.Single(game.State.Events, item => item.Type == "discard"
            && item.Cards.Any(card => card.InstanceId == scenario.GraveChoiceInstanceId));

        var orderPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("festival-bottom-order", orderPrompt.Data["action"]);
        var order = orderPrompt.ValidChoices.AsEnumerable().Reverse().ToArray();
        var orderResult = game.Handle(orderPrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: orderPrompt.PromptId,
                BottomCardInstanceIds: [.. order]));
        Assert.True(orderResult.Accepted, orderResult.Error);
        PassResponses(game);

        player = game.State.Players[controller];
        Assert.Equal(libraryCountBefore - 2, player.Library.Count);
        Assert.Single(player.Hand, card => card.InstanceId == scenario.HandChoiceInstanceId);
        Assert.Single(player.Graveyard, card => card.InstanceId == scenario.GraveChoiceInstanceId);
        Assert.DoesNotContain(player.Library, card => card.InstanceId is var id
            && (id == scenario.HandChoiceInstanceId || id == scenario.GraveChoiceInstanceId));
        Assert.Equal(order, player.Library.TakeLast(order.Length)
            .Select(card => card.InstanceId).ToArray());
        AssertPublicProjection(game, discard.Sequence, scenario.GraveChoiceInstanceId);
        _ = RestoreExact(game);
    }

    [Theory]
    [InlineData(true, "library")]
    [InlineData(false, null)]
    public async Task JournalTailReplaysFestivalOriginAndExactFinalState(
        bool protocolEnabled, string? expectedOrigin)
    {
        var scenario = ArrangeFestival($"festival-journal-{protocolEnabled}",
            protocolEnabled ? 26101691 : 26101692, controller: 0);
        var game = scenario.Game;
        if (!protocolEnabled)
        {
            var state = JsonNode.Parse(game.SerializeFullState())!.AsObject();
            Assert.True(state.Remove(ProtocolFlag));
            game = L12GameEngine.RestoreCheckpoint(Catalog, state.ToJsonString(),
                Assert.IsType<L12RandomState>(game.RandomState), game.CardFactSignalSequence,
                autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
            Assert.False(game.State.MovementOriginProtocolEnabled);
            Assert.DoesNotContain(ProtocolFlag, game.SerializeFullState(), StringComparison.Ordinal);
        }

        var directory = Path.Combine(Path.GetTempPath(), "l12-festival-origin-journal",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        recorder.AttachCatalog(Catalog);
        await recorder.StartAsync(game, "sandbox");
        long commandSequence = 0;

        async Task RecordAsync(int playerIndex, L12Command command)
        {
            var result = game.Handle(playerIndex, command);
            Assert.True(result.Accepted, result.Error);
            commandSequence++;
            await recorder.AppendAsync(game, commandSequence, playerIndex,
                JsonSerializer.Serialize(command), result,
                $"festival-origin-{protocolEnabled}-{commandSequence}");
        }

        async Task PassResponsesAsync()
        {
            for (var safety = 0; safety < 80
                 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            {
                var response = Assert.Single(game.State.PendingPrompts);
                await RecordAsync(response.PlayerIndex,
                    new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass"));
            }
            Assert.NotEqual("response", game.State.PendingPrompts.FirstOrDefault()?.Kind);
        }

        await RecordAsync(0, new L12Command("playCard", scenario.FestivalInstanceId));
        await PassResponsesAsync();
        var handPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("festival-hand", handPrompt.Data["action"]);
        await RecordAsync(handPrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: handPrompt.PromptId,
                Choice: scenario.HandChoiceInstanceId));
        var gravePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("festival-grave", gravePrompt.Data["action"]);
        await RecordAsync(gravePrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: gravePrompt.PromptId,
                Choice: scenario.GraveChoiceInstanceId));
        var orderPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("festival-bottom-order", orderPrompt.Data["action"]);
        await RecordAsync(orderPrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: orderPrompt.PromptId,
                BottomCardInstanceIds: [.. orderPrompt.ValidChoices.AsEnumerable().Reverse()]));
        await PassResponsesAsync();

        var discard = Assert.Single(game.State.Events, item => item.Type == "discard"
            && item.Cards.Any(card => card.InstanceId == scenario.GraveChoiceInstanceId));
        Assert.Equal(expectedOrigin, discard.OriginZone);
        var expected = StrictState(game);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                SELECT event_json FROM match_action_events
                WHERE match_id=$match AND event_sequence=$sequence;
                """;
            command.Parameters.AddWithValue("$match", game.State.MatchId);
            command.Parameters.AddWithValue("$sequence", discard.Sequence);
            var journalEvent = JsonNode.Parse(
                Assert.IsType<string>(await command.ExecuteScalarAsync()))!.AsObject();
            Assert.Equal(expectedOrigin, journalEvent["OriginZone"]?.GetValue<string>());
        }

        var recovered = Assert.IsType<L12JournalRecoveryState>(
            await recorder.LoadJournalEngineAsync(game.State.MatchId));
        Assert.Equal(commandSequence, recovered.CommandSequence);
        Assert.Equal(expected.Raw, recovered.Engine.SerializeFullState());
        Assert.Equal(expected.Hash, recovered.Engine.ComputeStateHash());
        Assert.Equal(protocolEnabled, recovered.Engine.State.MovementOriginProtocolEnabled);
        Assert.Equal(expectedOrigin, Assert.Single(recovered.Engine.State.Events,
            item => item.Sequence == discard.Sequence).OriginZone);
        if (!protocolEnabled)
            Assert.DoesNotContain(ProtocolFlag, recovered.Engine.SerializeFullState(),
                StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("library", false, "library")]
    [InlineData("library", true, null)]
    [InlineData("field", false, null)]
    public void SharedOriginGuardRequiresSupportedAndActuallyVacatedSource(
        string claimedOrigin, bool leaveDuplicateInClaimedSource, string? expectedOrigin)
    {
        var game = CreateGame($"festival-origin-guard-{claimedOrigin}-{leaveDuplicateInClaimedSource}",
            26101695 + (leaveDuplicateInClaimedSource ? 1 : 0), controller: 0);
        var card = Card("S01-0204", $"festival-origin-guard-card-{claimedOrigin}-{leaveDuplicateInClaimedSource}", 0);
        game.State.Players[0].Graveyard.Add(card);
        if (leaveDuplicateInClaimedSource)
            game.State.Players[0].Library.Add(card.Clone());
        AddSyntheticEvent(game, card);

        var method = typeof(L12GameEngine).GetMethod("AttachPublicDiscardOriginToLastEvent",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(game, [0, card, claimedOrigin]);

        var lastAction = Assert.IsType<L12ActionEvent>(game.State.LastAction);
        Assert.Equal(expectedOrigin, lastAction.OriginZone);
        Assert.Equal(expectedOrigin, Assert.Single(game.State.Events,
            item => item.Sequence == lastAction.Sequence).OriginZone);
        Assert.Equal(expectedOrigin, Assert.Single(game.UnpersistedEvents,
            item => item.Sequence == lastAction.Sequence).OriginZone);
    }

    private static FestivalScenario ArrangeFestival(string matchId, int seed, int controller)
    {
        var game = CreateGame(matchId, seed, controller);
        var suffix = $"{controller}-{seed}";
        var festival = Card("S01-0222", $"festival-source-{suffix}", controller);
        var handChoice = Card("S01-0203", $"festival-hand-{suffix}", controller);
        var graveChoice = Card("S01-0204", $"festival-grave-{suffix}", controller);
        var fillerOne = Card("S01-0101", $"festival-filler-one-{suffix}", controller);
        var fillerTwo = Card("S01-0102", $"festival-filler-two-{suffix}", controller);
        var excludedFestival = Card("S01-0222", $"festival-excluded-{suffix}", controller);
        var player = game.State.Players[controller];
        player.Hand.Add(festival);
        player.Library.AddRange([handChoice, graveChoice, fillerOne, fillerTwo, excludedFestival]);
        player.Morale.Add(new L12MoraleCard
        {
            CardId = "S01-01C1",
            InstanceId = $"festival-cost-{suffix}",
        });
        return new FestivalScenario(game, festival.InstanceId,
            handChoice.InstanceId, graveChoice.InstanceId);
    }

    private static L12GameEngine CreateGame(string matchId, int seed, int controller)
    {
        var basis = Catalog.DeckAt(0);
        L12PresetDeckDefinition Deck(string name) => new()
        {
            Name = name,
            MasterId = "S01-02M3",
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [.. basis.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, matchId, "FESTIVAL", seed,
            ["甲", "乙"], [Deck("甲测试牌库"), Deck("乙测试牌库")],
            skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = controller;
        game.State.FirstPlayer = controller;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        game.State.PendingPrompts.Clear();
        game.State.EffectStack.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
            player.SpecialZones.Trials.Clear();
            player.SpecialZones.Runes = 0;
            player.UsedAbilities.Clear();
        }
        Assert.True(game.State.MovementOriginProtocolEnabled);
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            OwnerIndex = owner,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            IsCounterTactic = definition.IsCounterTactic,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static void Resolve(L12GameEngine game, L12Prompt prompt, string choice)
    {
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 80
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            Resolve(game, prompt, "pass");
        }
        Assert.NotEqual("response", game.State.PendingPrompts.FirstOrDefault()?.Kind);
    }

    private static void AssertPrivateLibraryChoice(
        L12GameEngine game, int controller, string instanceId)
    {
        Assert.Contains(instanceId,
            JsonSerializer.Serialize(game.SnapshotFor(controller), WireJson),
            StringComparison.Ordinal);
        foreach (var snapshot in new[]
                 {
                     game.SnapshotFor(1 - controller),
                     game.SnapshotForSpectator(),
                     game.SnapshotForReferee(),
                 })
            Assert.DoesNotContain(instanceId, JsonSerializer.Serialize(snapshot, WireJson),
                StringComparison.Ordinal);
    }

    private static void AssertPublicProjection(
        L12GameEngine game, long sequence, string instanceId)
    {
        foreach (var snapshot in new[]
                 {
                     game.SnapshotFor(0),
                     game.SnapshotFor(1),
                     game.SnapshotForSpectator(),
                     game.SnapshotForReferee(),
                 })
        {
            var projected = Assert.Single(snapshot.RecentEvents,
                item => item.Sequence == sequence && item.Type == "discard"
                    && item.Cards.Any(card => card.InstanceId == instanceId));
            Assert.Equal("library", projected.OriginZone);
            Assert.False(Assert.Single(projected.Cards).Hidden);
        }
    }

    private static L12GameEngine RestoreExact(L12GameEngine game)
    {
        var before = StrictState(game);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, before.Raw,
            Assert.IsType<L12RandomState>(game.RandomState), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        var after = StrictState(restored);
        Assert.Equal(before.Raw, after.Raw);
        Assert.Equal(before.Hash, after.Hash);
        return restored;
    }

    private static StateEvidence StrictState(L12GameEngine game)
    {
        var raw = game.SerializeFullState();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))
            .ToLowerInvariant();
        Assert.Equal(hash, game.ComputeStateHash());
        return new StateEvidence(raw, hash);
    }

    private static void AddSyntheticEvent(L12GameEngine game, L12CardInstance card)
    {
        var method = typeof(L12GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "AddEvent"
                && candidate.GetParameters().Length == 4);
        method.Invoke(game, ["discard", 0, "festival-origin-guard", new[] { card }]);
    }

    private sealed record FestivalScenario(
        L12GameEngine Game,
        string FestivalInstanceId,
        string HandChoiceInstanceId,
        string GraveChoiceInstanceId);

    private sealed record StateEvidence(string Raw, string Hash);
}
