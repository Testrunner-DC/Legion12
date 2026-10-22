using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class DeepResponseUiFixture20261023Tests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string RootId = "deep-sortie";

    [Fact]
    public void ActualAlternatingAbsoluteDefensesReachSixLayersAndCloseAfterV2RestoreBeforeFive()
    {
        var game = Create();
        var frames = new List<object>();
        var sequence = 0;
        var restoredBeforeFive = false;

        void Send(int actor, L12Command command)
        {
            Assert.True(++sequence <= 64, "Deep response fixture exceeded its command budget.");
            var depth = game.State.EffectStack.Count;
            var prompt = game.State.PendingPrompts.SingleOrDefault();
            var before = new[] { Capture(game, 0), Capture(game, 1) };
            var result = game.Handle(actor, command);
            Assert.True(result.Accepted, $"{sequence} {command.Type}: {result.Error}");
            if (depth is >= 4 and <= 6 && prompt is not null)
                frames.Add(new
                {
                    sequence, actor, depth, promptKind = prompt.Kind, promptId = prompt.PromptId,
                    command, before, restoredBeforeFive,
                    afterDepth = game.State.EffectStack.Count, afterRevision = game.State.Revision,
                });
        }

        void Choose(string choice)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Contains(choice, prompt.ValidChoices);
            Send(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: [choice], ActivationId: prompt.ActivationId,
                SourceInstanceId: prompt.SourceInstanceId, SourceCardId: prompt.SourceCardId,
                Step: prompt.Step, CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller));
        }

        Send(0, new L12Command("playCard", RootId));
        Assert.Equal(1, game.State.EffectStack.Count);
        Assert.Equal("S01-0012", game.State.EffectStack[0].SourceCardId);
        Assert.Equal(0, game.State.ResponseWindow!.PriorityPlayer);
        Choose("pass");

        for (var responseIndex = 0; responseIndex < 5; responseIndex++)
        {
            var actor = responseIndex % 2 == 0 ? 1 : 0;
            var ownIndex = responseIndex / 2;
            var expectedDepth = responseIndex + 1;
            Assert.Equal(expectedDepth, game.State.EffectStack.Count);
            if (expectedDepth == 4)
            {
                var original = game.SerializeFullState();
                var hash = game.ComputeStateHash();
                var rng = game.RandomState!.Value;
                game = L12GameEngine.RestoreCheckpoint(Catalog, original, rng, game.CardFactSignalSequence,
                    autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
                Assert.Equal(original, game.SerializeFullState());
                Assert.Equal(hash, game.ComputeStateHash());
                Assert.Equal(rng, game.RandomState);
                restoredBeforeFive = true;
            }

            Assert.Equal(actor, game.State.ResponseWindow!.PriorityPlayer);
            var topId = game.State.EffectStack[^1].StackItemId;
            var responseId = $"deep-counter-{actor}-{ownIndex}";
            var response = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", response.Kind);
            Assert.Contains("pass", response.ValidChoices);
            Assert.Contains(responseId, response.ValidChoices);
            Choose(responseId);
            if (game.State.PendingPrompts.Single().Kind == "response-target") Choose(topId);
            var payment = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("stack-response-discard", payment.Continuation);
            var discardedId = $"deep-discard-{actor}-{ownIndex}";
            Choose(discardedId);
            Assert.Contains(game.State.Players[actor].Graveyard, card => card.InstanceId == discardedId);
            Assert.Equal(expectedDepth + 1, game.State.EffectStack.Count);
            var committed = game.State.EffectStack[^1];
            Assert.Equal(responseId, committed.SourceInstanceId);
            Assert.Equal("S01-0016", committed.SourceCardId);
            Assert.Equal(topId, Assert.Single(committed.Targets));
            Assert.Equal(actor, game.State.ResponseWindow!.PriorityPlayer);
            if (responseIndex < 4) Choose("pass");
        }

        Assert.True(restoredBeforeFive);
        Assert.Equal(6, game.State.EffectStack.Count);
        // Both sides really receive priority at layer six; player zero still has
        // an unused legal counter. Passing closes the already declared chain.
        Assert.Equal(1, game.State.ResponseWindow!.PriorityPlayer);
        Choose("pass");
        Assert.Equal(0, game.State.ResponseWindow!.PriorityPlayer);
        Assert.Contains("deep-counter-0-2", Assert.Single(game.State.PendingPrompts).ValidChoices);
        Choose("pass");
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Null(game.State.ResponseWindow);
        Assert.Null(game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Contains(game.State.Players[0].Graveyard, card => card.InstanceId == RootId);
        Assert.Null(game.State.Players[0].NextLegionChargeMaxCost);
        Assert.Equal(5, game.State.Players.Sum(player => player.Graveyard.Count(card => card.CardId == "S01-0016")));
        Assert.Equal(5, game.State.Players.Sum(player => player.Graveyard.Count(card => card.InstanceId.StartsWith("deep-discard-", StringComparison.Ordinal))));
        Assert.All(new[] { 4, 5, 6 }, depth => Assert.Contains(frames,
            frame => JsonSerializer.SerializeToElement(frame, Wire).GetProperty("depth").GetInt32() == depth));

        var output = Environment.GetEnvironmentVariable("L12_DEEP_RESPONSE_FIXTURE_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var fullOutput = Path.GetFullPath(output);
        var allowed = Path.GetFullPath(@"D:\GPT\Legion12\artifacts\bug-closure-continuation-20261011\next-authority-batch-20261016\closeout\intake-current-20261023-card-effect\deep-response-ui");
        Assert.True(fullOutput.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith(".json", fullOutput, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(fullOutput), "Refusing to overwrite prior fixture evidence.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
        File.WriteAllText(fullOutput, JsonSerializer.Serialize(new
        {
            schema = 1, status = "ACTUAL_HANDLE_CHAIN_PASSED", rootCardId = "S01-0012",
            responseCardId = "S01-0016", maximumDepth = 6, commandCount = sequence,
            restoredBeforeFive, realConnectionTakeover = false,
            boundary = "Synthetic catalog-based initial board with previously set counters. All stack creation, target declarations, discard payments and closure use real Handle commands. Engine V2 recovery only, not a socket takeover or original player replay.",
            frames, final = new[] { Capture(game, 0), Capture(game, 1) },
        }, Wire));
    }

    private static JsonElement Capture(L12GameEngine game, int viewer)
        => JsonSerializer.SerializeToElement(game.SnapshotFor(viewer), Wire);

    private static L12GameEngine Create()
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "Synthetic deep response", MasterId = "S02-05M2",
            CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "deep-response-ui", "DEEP-RESPONSE", 102390,
            ["Synthetic A", "Synthetic B"], [deck, deck], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2,
            utcNow: () => DateTimeOffset.Parse("2026-10-23T04:00:00+08:00"));
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Resolving.Clear();
            player.Removed.Clear(); player.Morale.Clear(); player.MoraleDeck.Clear(); player.UsedAbilities.Clear();
            player.Relic = null; player.ExtraRelics.Clear(); player.Hp = 8;
            for (var slot = 0; slot < 3; slot++)
            {
                var counter = Card("S01-0016", $"deep-counter-{player.PlayerIndex}-{slot}", player.PlayerIndex);
                counter.Hidden = true;
                counter.SetRound = 0;
                player.Field[1][slot] = counter;
                player.Hand.Add(Card("S01-0003", $"deep-discard-{player.PlayerIndex}-{slot}", player.PlayerIndex));
            }
            for (var index = 0; index < 12; index++)
                player.Library.Add(Card("S01-0003", $"deep-library-{player.PlayerIndex}-{index}", player.PlayerIndex));
            for (var index = 0; index < 8; index++)
                player.Morale.Add(new L12MoraleCard
                {
                    CardId = "S02-05C1", InstanceId = $"deep-morale-{player.PlayerIndex}-{index}",
                });
        }
        game.State.Players[0].Hand.Add(Card("S01-0012", RootId, 0));
        game.State.ActivePlayer = 0; game.State.FirstPlayer = 0;
        game.State.Round = 3; game.State.TurnSerial = 6; game.State.Phase = L12Phase.Main;
        game.State.PendingPrompts.Clear(); game.State.PendingActivations.Clear();
        game.State.EffectStack.Clear(); game.State.DeferredEffectStack.Clear();
        game.State.PendingTriggerBatches.Clear(); game.State.PendingTriggerStackCandidates.Clear();
        game.State.SuspendedCombatContexts.Clear(); game.State.Events.Clear(); game.State.AuthorityEvents.Clear();
        game.State.Log.Clear(); game.State.LastAction = null; game.State.ResponseWindow = null;
        game.State.PendingDefense = null; game.State.ActiveDisaster = null;
        game.State.Revision++;
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = definition.Id, OwnerIndex = owner,
            Name = definition.NameZh, CardType = definition.CardType, Faction = definition.Faction,
            ImageUrl = definition.ImageUrl, Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue, IsCounterTactic = definition.IsCounterTactic,
            EffectText = definition.Effect, Traits = [.. definition.Traits], Profession = definition.Profession,
            EffectiveProfession = definition.Profession, BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0, SummonRound = -1,
        };
    }
}
