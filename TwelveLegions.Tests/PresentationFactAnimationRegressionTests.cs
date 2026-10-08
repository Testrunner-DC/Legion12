using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PresentationFactAnimationRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(string matchId, int seed, int stateFormatVersion = 2)
    {
        var basis = Catalog.DeckAt(0);
        var thorDeck = new L12PresetDeckDefinition
        {
            Name = "表现事实回归",
            MasterId = "S02-03M1",
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, matchId, "FACT01", seed,
            ["甲", "乙"], [thorDeck, basis], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: stateFormatVersion);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            OwnerIndex = owner,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
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

    private static void ResolvePrompt(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void ResolvePrompt(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [.. choices]));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 40
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            ResolvePrompt(game, "pass");
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }

    private static string BeginHammer(L12GameEngine game, L12CardInstance hammer,
        IReadOnlyList<L12CardInstance> costs, string? slot = null)
    {
        var player = game.State.Players[0];
        if (!player.Graveyard.Any(card => card.InstanceId == hammer.InstanceId))
            player.Graveyard.Add(hammer);
        player.Graveyard.AddRange(costs);
        var begin = game.Handle(0,
            new L12Command("activateAbility", hammer.InstanceId, Ability: "thorHammerRevive"));
        Assert.True(begin.Accepted, begin.Error);
        ResolvePrompt(game, costs.Select(card => card.InstanceId).ToArray());
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        var selectedSlot = slot ?? slotPrompt.ValidChoices[0];
        Assert.Contains(selectedSlot, slotPrompt.ValidChoices);
        ResolvePrompt(game, selectedSlot);
        return selectedSlot;
    }

    private static L12ActionEvent HammerResult(L12GameEngine game, string instanceId)
        => game.State.Events.Last(item => item.Type == "effect-result"
            && item.Cards.Any(card => card.InstanceId == instanceId));

    [Fact]
    public void ResolvedHammerReferencesItsExactPutFactAndASecondLegalReviveGetsANewIdentity()
    {
        var game = Create("presentation-fact-twice", 26100401);
        var hammer = Card("S02-0301", "presentation-hammer-twice");
        var firstCosts = Enumerable.Range(0, 3)
            .Select(index => Card("S02-0001", $"presentation-first-cost-{index}")).ToArray();
        BeginHammer(game, hammer, firstCosts, "0:0");
        PassResponses(game);

        var firstPut = Assert.Single(game.State.Events, item => item.Type == "put"
            && item.Cards.Any(card => card.InstanceId == hammer.InstanceId));
        var firstResult = HammerResult(game, hammer.InstanceId);
        Assert.Equal([firstPut.Sequence], Assert.IsType<long[]>(firstResult.PlayerPresentationFactSequences));
        foreach (var snapshot in new[]
                 {
                     game.SnapshotFor(0), game.SnapshotFor(1),
                     game.SnapshotForSpectator(), game.SnapshotForReferee(),
                 })
        {
            var projectedResult = Assert.Single(snapshot.RecentEvents, item => item.Sequence == firstResult.Sequence);
            Assert.Equal([firstPut.Sequence],
                Assert.IsType<long[]>(projectedResult.PlayerPresentationFactSequences));
            Assert.Contains(snapshot.RecentEvents, item => item.Sequence == firstPut.Sequence && item.Type == "put");
        }

        // Establish the later legal private-zone state without adding a second
        // presentation producer, then advance through both real end-turn commands.
        // The card-name usage limit must only reopen on the next own turn.
        game.State.Players[0].Field[0][0] = null;
        game.State.Players[0].Graveyard.Add(hammer);
        game.State.Players[0].Field[1][0] = Card("S02-0002", "presentation-owner-anchor");
        game.State.Players[1].Field[1][0] = Card("S02-0002", "presentation-rival-anchor", owner: 1);
        game.State.Players[0].Library.Add(Card("S02-0002", "presentation-owner-draw"));
        game.State.Players[1].Library.Add(Card("S02-0002", "presentation-rival-draw", owner: 1));
        var firstEnd = game.Handle(0, new L12Command("endTurn"));
        Assert.True(firstEnd.Accepted, firstEnd.Error);
        var secondEnd = game.Handle(1, new L12Command("endTurn"));
        Assert.True(secondEnd.Accepted, secondEnd.Error);
        Assert.Equal(0, game.State.ActivePlayer);
        var restoredHammer = Assert.Single(game.State.Players[0].Graveyard,
            card => card.InstanceId == hammer.InstanceId);
        var secondCosts = Enumerable.Range(0, 3)
            .Select(index => Card("S02-0002", $"presentation-second-cost-{index}")).ToArray();
        BeginHammer(game, restoredHammer, secondCosts, "0:1");
        PassResponses(game);

        var puts = game.State.Events.Where(item => item.Type == "put"
            && item.Cards.Any(card => card.InstanceId == hammer.InstanceId)).ToArray();
        Assert.Equal(2, puts.Length);
        Assert.True(puts[1].Sequence > puts[0].Sequence);
        var results = game.State.Events.Where(item => item.Type == "effect-result"
            && item.Cards.Any(card => card.InstanceId == hammer.InstanceId)).ToArray();
        Assert.Equal(2, results.Length);
        Assert.Equal([puts[0].Sequence], Assert.IsType<long[]>(results[0].PlayerPresentationFactSequences));
        Assert.Equal([puts[1].Sequence], Assert.IsType<long[]>(results[1].PlayerPresentationFactSequences));
    }

    [Fact]
    public void PresentationReferencesAreKeptOnlyWhenTheRecipientReceivesTheReferencedFact()
    {
        var game = Create("presentation-fact-recipient-projection", 26100405);
        var card = Card("S02-0001", "presentation-private-state");
        game.State.Players[0].Field[0][0] = card;
        var factSequence = game.State.EventSequence + 1;
        var resultSequence = factSequence + 1;
        var privateSnapshot = card.Clone();
        privateSnapshot.Tapped = true;
        var privateFact = new L12ActionEvent(factSequence, "private-trigger-state", 0,
            "仅当前接收者可见的状态事实", [privateSnapshot])
        {
            PlayerCardStateTransition = new(card.InstanceId, false, true),
        };
        var result = new L12ActionEvent(resultSequence, "effect-result", 0,
            "公开结果不得借用未投影给接收者的事实", [card.Clone()])
        {
            PlayerPresentationFactSequences = [factSequence],
        };
        game.State.EventSequence = resultSequence;
        game.State.Events.Add(privateFact);
        game.State.Events.Add(result);
        game.State.LastAction = result;

        var owner = game.SnapshotFor(0);
        Assert.Contains(owner.RecentEvents, item => item.Sequence == factSequence && item.Type == "state");
        Assert.Equal([factSequence], Assert.IsType<long[]>(Assert.Single(owner.RecentEvents,
            item => item.Sequence == resultSequence).PlayerPresentationFactSequences));

        foreach (var snapshot in new[]
                 {
                     game.SnapshotFor(1), game.SnapshotForSpectator(), game.SnapshotForReferee(),
                 })
        {
            Assert.DoesNotContain(snapshot.RecentEvents, item => item.Sequence == factSequence);
            Assert.Null(Assert.Single(snapshot.RecentEvents,
                item => item.Sequence == resultSequence).PlayerPresentationFactSequences);
            Assert.Null(snapshot.LastAction?.PlayerPresentationFactSequences);
        }

        var malformedMovement = new L12ActionEvent(resultSequence + 1, "move", 0,
            "畸形位移事实", [card.Clone()])
        {
            PlayerBattlefieldMovement = new([
                new(card.InstanceId, 0, 0, 0, 0, 9),
            ]),
        };
        var malformedResult = result with
        {
            Sequence = resultSequence + 2,
            PlayerPresentationFactSequences = [malformedMovement.Sequence],
        };
        var recipientEvents = new Dictionary<long, L12ActionEvent>
        {
            [malformedMovement.Sequence] = malformedMovement,
            [malformedResult.Sequence] = malformedResult,
        };
        Assert.Null(L12RecipientVisibility.ProjectPresentationFactReferences(
            malformedResult, recipientEvents).PlayerPresentationFactSequences);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("attack")]
    [InlineData("attack-ended")]
    public void PublicStateFactsAreProjectedOnlyForSupportedEventsWithOneVisibleIdentity(string eventType)
    {
        var game = Create($"presentation-public-state-{eventType}", 26100406);
        var card = Card("S02-0001", $"presentation-public-state-{eventType}");
        card.Tapped = true;
        game.State.Players[0].Field[0][0] = card;
        var actionEvent = new L12ActionEvent(1, eventType, 0, "公开横置事实", [card.Clone()])
        {
            PlayerCardStateTransition = new(card.InstanceId, false, true),
        };

        var projected = L12RecipientVisibility.ProjectActionEvent(game.State, actionEvent, 1, false);

        Assert.Equal(actionEvent.PlayerCardStateTransition, projected.PlayerCardStateTransition);
    }

    [Fact]
    public void HiddenDuplicateMalformedOrUnsupportedStateFactsAreRemovedBeforeRecipientDelivery()
    {
        var game = Create("presentation-state-recipient-filter", 26100407);
        var card = Card("S02-0001", "presentation-state-recipient-filter");
        card.Tapped = true;
        game.State.Players[0].Field[0][0] = card;
        var valid = new L12ActionEvent(1, "attack", 0, "公开横置事实", [card.Clone()])
        {
            PlayerCardStateTransition = new(card.InstanceId, false, true),
        };
        var hidden = card.Clone();
        hidden.Hidden = true;
        var cases = new[]
        {
            valid with { Cards = [hidden] },
            valid with { Cards = [card.Clone(), card.Clone()] },
            valid with { PlayerCardStateTransition = new("other-instance", false, true) },
            valid with { PlayerCardStateTransition = new(card.InstanceId, true, true) },
            valid with { Type = "dice" },
        };

        Assert.All(cases, actionEvent => Assert.Null(L12RecipientVisibility.ProjectActionEvent(
            game.State, actionEvent, 1, false).PlayerCardStateTransition));
    }

    [Fact]
    public void InvalidatedOrNegatedHammerDoesNotInventAPresentationFact()
    {
        var invalidated = Create("presentation-fact-invalidated", 26100402);
        var invalidatedHammer = Card("S02-0301", "presentation-hammer-invalidated");
        var invalidatedCosts = Enumerable.Range(0, 3)
            .Select(index => Card("S02-0001", $"presentation-invalidated-cost-{index}")).ToArray();
        var slot = BeginHammer(invalidated, invalidatedHammer, invalidatedCosts, "0:0");
        var (row, column) = slot.Split(':') is [var rowText, var columnText]
            ? (int.Parse(rowText), int.Parse(columnText)) : (-1, -1);
        invalidated.State.Players[0].Field[row][column] = Card("S02-0002", "presentation-slot-blocker");
        PassResponses(invalidated);
        Assert.DoesNotContain(invalidated.State.Events, item => item.Type == "put"
            && item.Cards.Any(card => card.InstanceId == invalidatedHammer.InstanceId));
        Assert.Null(HammerResult(invalidated, invalidatedHammer.InstanceId).PlayerPresentationFactSequences);

        var negated = Create("presentation-fact-negated", 26100403);
        var negatedHammer = Card("S02-0301", "presentation-hammer-negated");
        var negatedCosts = Enumerable.Range(0, 3)
            .Select(index => Card("S02-0001", $"presentation-negated-cost-{index}")).ToArray();
        var counter = Card("S01-0016", "presentation-hammer-counter", owner: 1);
        counter.Hidden = true;
        counter.SetRound = 0;
        negated.State.Players[1].Field[1][0] = counter;
        var discard = Card("S01-0003", "presentation-hammer-counter-cost", owner: 1);
        negated.State.Players[1].Hand.Add(discard);
        BeginHammer(negated, negatedHammer, negatedCosts, "0:0");
        ResolvePrompt(negated, "pass");
        Assert.Contains(counter.InstanceId, Assert.Single(negated.State.PendingPrompts).ValidChoices);
        ResolvePrompt(negated, counter.InstanceId);
        Assert.Contains(discard.InstanceId, Assert.Single(negated.State.PendingPrompts).ValidChoices);
        ResolvePrompt(negated, discard.InstanceId);
        PassResponses(negated);
        Assert.DoesNotContain(negated.State.Events, item => item.Type == "put"
            && item.Cards.Any(card => card.InstanceId == negatedHammer.InstanceId));
        var negatedResult = HammerResult(negated, negatedHammer.InstanceId);
        Assert.Equal("negated", negatedResult.EffectResultStatus);
        Assert.Null(negatedResult.PlayerPresentationFactSequences);
    }

    [Fact]
    public async Task LegacyV2JournalWithoutProtocolFlagReplaysRealCommandToRecordedStateHash()
    {
        Assert.Equal(2, L12PersistenceContract.MinimumCheckpointRecoveryVersion);
        var created = Create("presentation-fact-legacy-journal", 26100404);
        var currentState = JsonNode.Parse(created.SerializeFullState())!.AsObject();
        var legacyState = currentState.DeepClone().AsObject();
        Assert.True(legacyState.Remove(nameof(L12GameState.PresentationFactProtocolEnabled)));
        var legacy = L12GameEngine.RestoreCheckpoint(Catalog, legacyState.ToJsonString(),
            created.RandomState!.Value, created.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.False(legacy.State.PresentationFactProtocolEnabled);
        var restoredLegacyState = JsonNode.Parse(legacy.SerializeFullState())!.AsObject();
        Assert.Equal(legacyState.ToJsonString(), restoredLegacyState.ToJsonString());
        var currentWithoutProtocolFlag = currentState.DeepClone().AsObject();
        Assert.True(currentWithoutProtocolFlag.Remove(nameof(L12GameState.PresentationFactProtocolEnabled)));
        Assert.Equal(restoredLegacyState.ToJsonString(), currentWithoutProtocolFlag.ToJsonString());

        var wireJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var currentSnapshot = JsonNode.Parse(JsonSerializer.Serialize(created.SnapshotFor(0), wireJson))!.AsObject();
        var legacySnapshot = JsonNode.Parse(JsonSerializer.Serialize(legacy.SnapshotFor(0), wireJson))!.AsObject();
        var currentStateHash = currentSnapshot["stateHash"]?.GetValue<string>();
        var legacyStateHash = legacySnapshot["stateHash"]?.GetValue<string>();
        Assert.NotEqual(currentStateHash, legacyStateHash);
        Assert.True(currentSnapshot.Remove("stateHash"));
        Assert.True(legacySnapshot.Remove("stateHash"));
        Assert.Equal(currentSnapshot.ToJsonString(), legacySnapshot.ToJsonString());

        var directory = Path.Combine(Path.GetTempPath(), "l12-presentation-fact-journal",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        await using var recorder = new MatchRecorder(path);
        await recorder.InitializeAsync();
        recorder.AttachCatalog(Catalog);
        await recorder.StartAsync(legacy, "sandbox");
        var command = new L12GmCommand("setLife", 0, Value: 17);
        var result = legacy.HandleGm(command);
        Assert.True(result.Accepted, result.Error);
        await recorder.AppendAsync(legacy, 1, -1, JsonSerializer.Serialize(command), result,
            "presentation-fact-legacy-command");
        var expectedHash = legacy.ComputeStateHash();

        var recovered = Assert.IsType<L12JournalRecoveryState>(
            await recorder.LoadJournalEngineAsync(legacy.State.MatchId));
        Assert.Equal(1, recovered.CommandSequence);
        Assert.False(recovered.Engine.State.PresentationFactProtocolEnabled);
        Assert.Equal(expectedHash, recovered.Engine.ComputeStateHash());
        Assert.DoesNotContain(nameof(L12GameState.PresentationFactProtocolEnabled),
            recovered.Engine.SerializeFullState(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("enter")]
    [InlineData("discard")]
    public void PublicSingleCardZoneFactsAreRecipientValidatedWithoutLeakingHiddenOrDuplicateCards(string factType)
    {
        var card = Card("S02-0005", $"presentation-public-{factType}", owner: 1);
        var fact = new L12ActionEvent(1, factType, 1, "公开单卡区域事实", [card.Clone()]);
        var result = new L12ActionEvent(2, "effect-result", 1, "效果完成", [card.Clone()])
        {
            PlayerPresentationFactSequences = [fact.Sequence],
        };
        var delivered = new Dictionary<long, L12ActionEvent>
        {
            [fact.Sequence] = fact,
            [result.Sequence] = result,
        };
        Assert.Equal([fact.Sequence], Assert.IsType<long[]>(L12RecipientVisibility
            .ProjectPresentationFactReferences(result, delivered).PlayerPresentationFactSequences));

        var hidden = card.Clone();
        hidden.Hidden = true;
        delivered[fact.Sequence] = fact with { Cards = [hidden] };
        Assert.Null(L12RecipientVisibility.ProjectPresentationFactReferences(result, delivered)
            .PlayerPresentationFactSequences);
        delivered[fact.Sequence] = fact with { Cards = [card.Clone(), card.Clone()] };
        Assert.Null(L12RecipientVisibility.ProjectPresentationFactReferences(result, delivered)
            .PlayerPresentationFactSequences);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public void MercenaryResultReferencesOnlyItsCommittedDiscardFactWhenProtocolIsEnabled(
        int stateFormatVersion, bool expectedReference)
    {
        var game = Create($"presentation-mercenary-{stateFormatVersion}", 26100408, stateFormatVersion);
        var attacker = Card("S01-0001", "presentation-mercenary-attacker");
        var target = Card("S01-0102", "presentation-mercenary-target", owner: 1);
        var mercenary = Card("S01-0002", "presentation-mercenary-source", owner: 1);
        attacker.Troops = 9000;
        target.Troops = 1000;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[1].Hand.Add(mercenary);

        var attack = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);
        for (var safety = 0; safety < 20; safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.ValidChoices.Contains(mercenary.InstanceId))
            {
                ResolvePrompt(game, mercenary.InstanceId);
                break;
            }
            ResolvePrompt(game, "pass");
        }
        PassResponses(game);

        var discard = Assert.Single(game.State.Events, item => item.Type == "discard"
            && item.Cards.Any(card => card.InstanceId == mercenary.InstanceId));
        var result = Assert.Single(game.State.Events, item => item.Type == "effect-result"
            && item.Cards.Any(card => card.InstanceId == mercenary.InstanceId));
        if (expectedReference)
            Assert.Equal([discard.Sequence], Assert.IsType<long[]>(result.PlayerPresentationFactSequences));
        else
            Assert.Null(result.PlayerPresentationFactSequences);
        foreach (var snapshot in new[]
                 {
                     game.SnapshotFor(0), game.SnapshotFor(1),
                     game.SnapshotForSpectator(), game.SnapshotForReferee(),
                 })
        {
            var projected = Assert.Single(snapshot.RecentEvents, item => item.Sequence == result.Sequence);
            if (expectedReference)
                Assert.Equal([discard.Sequence], Assert.IsType<long[]>(projected.PlayerPresentationFactSequences));
            else
                Assert.Null(projected.PlayerPresentationFactSequences);
        }
    }

    [Fact]
    public void PuppetResultReferencesItsExactCommittedEnterFact()
    {
        var game = Create("presentation-puppet-entry", 26100409);
        var attacker = Card("S02-0003", "presentation-puppet-attacker");
        var puppet = Card("S02-0005", "presentation-puppet-source", owner: 1);
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Hand.Add(puppet);

        var attack = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        for (var safety = 0; safety < 20; safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.ValidChoices.Contains(puppet.InstanceId))
            {
                ResolvePrompt(game, puppet.InstanceId);
                break;
            }
            ResolvePrompt(game, "pass");
        }
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("0:1", slot.ValidChoices);
        ResolvePrompt(game, "0:1");
        PassResponses(game);

        var enter = Assert.Single(game.State.Events, item => item.Type == "enter"
            && item.Cards.Any(card => card.InstanceId == puppet.InstanceId));
        var result = Assert.Single(game.State.Events, item => item.Type == "effect-result"
            && item.Cards.Any(card => card.InstanceId == puppet.InstanceId));
        Assert.Equal([enter.Sequence], Assert.IsType<long[]>(result.PlayerPresentationFactSequences));
        foreach (var snapshot in new[]
                 {
                     game.SnapshotFor(0), game.SnapshotFor(1),
                     game.SnapshotForSpectator(), game.SnapshotForReferee(),
                 })
        {
            var projected = Assert.Single(snapshot.RecentEvents, item => item.Sequence == result.Sequence);
            Assert.Equal([enter.Sequence], Assert.IsType<long[]>(projected.PlayerPresentationFactSequences));
        }
    }
}
