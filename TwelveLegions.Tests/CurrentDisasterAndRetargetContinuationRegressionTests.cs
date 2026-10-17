using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CurrentDisasterAndRetargetContinuationRegressionTests
{
    private static readonly L12Catalog Catalog =
        L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-17T00:00:00Z");

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "bug:BUG-20260917-e932f9ca")]
    public void GramRevalidatesTheDeclaredTroopsLimitAfterAnAmbushResponse(
        int actor, bool restoreBetweenCommands)
    {
        var opponent = 1 - actor;
        var basis = Catalog.DeckAt(3);
        Assert.Equal("asgard", Catalog.Cards[basis.MasterId].Faction);
        var game = new L12GameEngine(Catalog, "gram-retarget-current", "SYNTHETIC",
            932000 + actor, ["P0", "P1"], [basis, basis], skipPreparation: true,
            disasterMode: "none", autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2, utcNow: () => Now);
        game.State.ActivePlayer = actor;
        game.State.FirstPlayer = actor;
        game.State.Round = 3;
        game.State.TurnSerial = 25;
        game.State.Phase = L12Phase.Main;
        game.State.DisasterValue = 0;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Library.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
        }

        var owner = game.State.Players[actor];
        var defender = game.State.Players[opponent];
        owner.Hand.Add(Card("S01-0317", "gram", actor));
        owner.Library.Add(Card("S01-0001", "gram-mill-0", actor));
        owner.Library.Add(Card("S01-0002", "gram-mill-1", actor));
        owner.Library.Add(Card("S01-0003", "gram-library-remains", actor));
        for (var index = 0; index < 4; index++)
            owner.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-03C1", InstanceId = $"gram-morale-{index}", Tapped = false,
            });

        // The printed 2000-troop Heavy Soldier is 3000 while it is in the front row
        // during its opponent's turn. Ambush then adds the printed +2000, so the same
        // public instance is 5000 when Gram settles.
        var target = Card("S02-0007", "declared-target", opponent);
        Assert.Equal(2000, target.BaseTroops);
        defender.Field[0][0] = target;
        var ambush = Card("S01-0019", "ambush", opponent);
        ambush.Hidden = true;
        ambush.SetRound = 0;
        defender.Field[1][2] = ambush;

        var acceptedCommands = 0;
        var restores = 0;
        void RestoreExactV2()
        {
            var raw = game.SerializeFullState();
            var hash = game.ComputeStateHash();
            var revision = game.State.Revision;
            var factSequence = game.CardFactSignalSequence;
            game = L12GameEngine.RestoreCheckpoint(Catalog, raw, game.RandomState!.Value,
                factSequence, autoPassEmptyResponses: false,
                concealHiddenResponseAvailability: false, utcNow: () => Now);
            Assert.Equal(raw, game.SerializeFullState());
            Assert.Equal(hash, game.ComputeStateHash());
            Assert.Equal(revision, game.State.Revision);
            Assert.Equal(factSequence, game.CardFactSignalSequence);
            restores++;
        }

        void Send(int controller, L12Command command)
        {
            if (restoreBetweenCommands) RestoreExactV2();
            var result = game.Handle(controller, command);
            Assert.True(result.Accepted, result.Error);
            acceptedCommands++;
        }

        static L12Command Resolve(L12Prompt prompt, string? choice = null,
            List<string>? cardInstanceIds = null) => new("resolvePrompt",
            PromptId: prompt.PromptId, Choice: choice, CardInstanceIds: cardInstanceIds,
            ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
            SourceCardId: prompt.SourceCardId, Step: prompt.Step,
            CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller);

        Send(actor, new L12Command("playCard", "gram"));
        var modePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", modePrompt.Continuation);
        Assert.Equal("gram", modePrompt.SourceInstanceId);
        Assert.Contains("mode:use", modePrompt.ValidChoices);
        Send(actor, Resolve(modePrompt, "mode:use"));

        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", targetPrompt.Continuation);
        Assert.Contains("declared-target", targetPrompt.ValidChoices);
        Assert.Equal(3000, game.State.Players[opponent].Field[0][0]!.CurrentTroops);
        var targetDeclaration = Resolve(targetPrompt, "declared-target");
        Send(actor, targetDeclaration);

        var rawBeforeDuplicate = game.SerializeFullState();
        var hashBeforeDuplicate = game.ComputeStateHash();
        var revisionBeforeDuplicate = game.State.Revision;
        var factsBeforeDuplicate = game.CardFactSignalSequence;
        Assert.False(game.Handle(actor, targetDeclaration).Accepted);
        Assert.Equal(rawBeforeDuplicate, game.SerializeFullState());
        Assert.Equal(hashBeforeDuplicate, game.ComputeStateHash());
        Assert.Equal(revisionBeforeDuplicate, game.State.Revision);
        Assert.Equal(factsBeforeDuplicate, game.CardFactSignalSequence);

        var ambushCommitted = false;
        for (var safety = 0; safety < 80; safety++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is not { } prompt)
            {
                if (game.State.EffectStack.Count == 0) break;
                throw new Xunit.Sdk.XunitException("The real Gram/Ambush stack stalled without a prompt.");
            }
            if (prompt.Kind != "response")
                throw new Xunit.Sdk.XunitException(
                    $"Unexpected prompt {prompt.Kind}/{prompt.Continuation}/{prompt.SourceCardId}: "
                    + string.Join(',', prompt.ValidChoices));

            if (!ambushCommitted && prompt.PlayerIndex == opponent
                && prompt.ValidChoices.Contains("ambush"))
            {
                Send(opponent, Resolve(prompt, "ambush"));
                var ambushTarget = Assert.Single(game.State.PendingPrompts);
                Assert.Equal("pending-activation", ambushTarget.Continuation);
                Assert.Equal("ambush", ambushTarget.SourceInstanceId);
                Assert.Contains("declared-target", ambushTarget.ValidChoices);
                Send(opponent, Resolve(ambushTarget, "declared-target"));
                var gramStack = Assert.Single(game.State.EffectStack,
                    item => item.SourceInstanceId == "gram");
                var committedAmbush = Assert.Single(game.State.EffectStack,
                    item => item.SourceInstanceId == "ambush");
                Assert.Equal("declared-target", committedAmbush.Data["target"]);
                Assert.Equal(gramStack.StackItemId, Assert.Single(committedAmbush.Targets));
                ambushCommitted = true;
                continue;
            }

            Send(prompt.PlayerIndex, Resolve(prompt, "pass"));
        }

        Assert.True(ambushCommitted);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Null(game.State.ResponseWindow);
        Assert.Null(game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Equal(actor, game.State.ActivePlayer);
        Assert.Equal(restoreBetweenCommands ? acceptedCommands : 0, restores);

        owner = game.State.Players[actor];
        defender = game.State.Players[opponent];
        var survivingTarget = Assert.Single(defender.Field[0],
            card => card?.InstanceId == "declared-target")!;
        Assert.Equal(target.InstanceId, survivingTarget.InstanceId);
        Assert.Equal(5000, survivingTarget.CurrentTroops);
        Assert.DoesNotContain(defender.Library, card => card.InstanceId == "declared-target");
        Assert.DoesNotContain(defender.Graveyard, card => card.InstanceId == "declared-target");
        Assert.Single(defender.Graveyard, card => card.InstanceId == "ambush");
        Assert.Single(owner.Library, card => card.InstanceId == "gram-library-remains");
        Assert.Equal(2, owner.Graveyard.Count(card =>
            card.InstanceId.StartsWith("gram-mill-", StringComparison.Ordinal)));
        Assert.Equal(4, owner.Morale.Count(card => card.Tapped));
        Assert.Equal("gram", owner.Relic?.InstanceId);
        var ambushModifier = Assert.Single(game.State.Events, entry => entry.Type == "troops-modifier"
            && entry.PlayerIndex == opponent
            && entry.PlayerTroopsModifier?.TargetInstanceId == "declared-target"
            && entry.PlayerTroopsModifier.TroopsDelta == 2000);
        var gramFailure = Assert.Single(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.PlayerIndex == actor
            && entry.Text.Contains("当前兵力不高于3000", StringComparison.Ordinal)
            && entry.Cards.Any(card => card.InstanceId == "gram"));
        Assert.True(ambushModifier.Sequence < gramFailure.Sequence,
            "The authoritative Ambush modifier must resolve before Gram revalidates its target.");
    }

    [Fact]
    [Trait("L12Evidence", "bug:BUG-20260916-bbd8d4f9")]
    public void RagnarokNaturalTurnGrowthResetsTheValueAndPublishesBothPlayersDraws()
    {
        var game = new L12GameEngine(Catalog, "ragnarok-natural-growth", "SYNTHETIC",
            88602, ["P0", "P1"], [0, 1], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2, utcNow: () => Now);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(Card("S01-DS09", "turn-start-ragnarok", 0));
        game.State.DisasterValue = 9;
        var nextPlayerHandBefore = game.State.Players[1].Hand.Count;
        var opponentHandBefore = game.State.Players[0].Hand.Count;

        var result = game.Handle(0, new L12Command("endTurn"));
        Assert.True(result.Accepted, result.Error);

        Assert.Equal(1, game.State.ActivePlayer);
        Assert.Equal(3, game.State.Round);
        Assert.Equal(8, game.State.TurnSerial);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Equal(0, game.State.DisasterValue);
        Assert.Equal(-1, game.State.ExtraTurnsForPlayer);
        Assert.Equal(nextPlayerHandBefore + 3, game.State.Players[1].Hand.Count);
        Assert.Equal(opponentHandBefore + 2, game.State.Players[0].Hand.Count);
        Assert.Contains(game.State.Events, entry => entry.Type == "disaster-trigger-source"
            && entry.Text.Contains("开场触发", StringComparison.Ordinal));

        foreach (var events in new[]
                 {
                     game.SnapshotFor(0).RecentEvents,
                     game.SnapshotFor(1).RecentEvents,
                     game.SnapshotForSpectator().RecentEvents,
                 })
        {
            for (var player = 0; player < 2; player++)
            {
                var draw = Assert.Single(events, entry => entry.Type == "draw"
                    && entry.PlayerIndex == player
                    && entry.Text.Contains("诸神黄昏", StringComparison.Ordinal)
                    && entry.Text.Contains("2 张牌", StringComparison.Ordinal));
                Assert.DoesNotContain(draw.Cards, card =>
                    game.State.Players[player].Hand.Any(hand => hand.InstanceId == card.InstanceId));
            }
        }
    }

    private static L12CardInstance Card(string id, string instanceId, int owner)
    {
        var definition = Catalog.Cards[id];
        return new L12CardInstance
        {
            CardId = id, InstanceId = instanceId, OwnerIndex = owner, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction,
            IsCounterTactic = definition.IsCounterTactic, ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0, HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect, Traits = [.. definition.Traits],
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            Profession = definition.Profession, EffectiveProfession = definition.Profession,
            DisasterLevel = definition.DisasterLevel ?? 0, TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }
}
