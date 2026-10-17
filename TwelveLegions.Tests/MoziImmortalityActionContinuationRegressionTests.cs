using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MoziImmortalityActionContinuationRegressionTests
{
    private static readonly L12Catalog Catalog =
        L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-17T00:00:00Z");

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "bug:BUG-20260910-8f8199b2")]
    public void RealMoziImmortalitySurvivorCanStillAttackTheMasterAfterYingzhengLethal(
        int actor, bool restoreBetweenCommands)
    {
        var basis = Catalog.DeckAt(0);
        Assert.Equal("tianting", Catalog.Cards[basis.MasterId].Faction);
        var game = new L12GameEngine(Catalog, "mozi-immortal-action", "SYNTHETIC",
            981900 + actor, ["P0", "P1"], [basis, basis], skipPreparation: true,
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
        owner.Hand.Add(Card("S01-0110", "mozi", actor));
        owner.Hand.Add(Card("S02-0101", "yingzheng", actor));
        owner.Hand.Add(Card("S02-0101", "entry-cost", actor));
        owner.Field[0][1] = Card("S01-0114", "protected-tianting", actor);
        owner.Library.Add(Card("S01-0002", "draw-after-mozi-death-0", actor));
        owner.Library.Add(Card("S01-0002", "draw-after-mozi-death-1", actor));
        for (var i = 0; i < 12; i++)
            owner.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-01C1", InstanceId = $"morale-{i}", Tapped = false,
            });

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
        }

        void Send(int controller, L12Command command)
        {
            if (restoreBetweenCommands) RestoreExactV2();
            var result = game.Handle(controller, command);
            Assert.True(result.Accepted, result.Error);
        }

        static L12Command Resolve(L12Prompt prompt, string? choice = null,
            List<string>? cardInstanceIds = null) => new("resolvePrompt",
            PromptId: prompt.PromptId, Choice: choice, CardInstanceIds: cardInstanceIds,
            ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
            SourceCardId: prompt.SourceCardId, Step: prompt.Step,
            CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller);

        void AssertOperableMain()
        {
            Assert.Null(game.State.PendingDefense);
            Assert.Null(game.State.ResponseWindow);
            Assert.Empty(game.State.PendingPrompts);
            Assert.Empty(game.State.PendingActivations);
            Assert.Empty(game.State.PendingTriggerBatches);
            Assert.Empty(game.State.PendingTriggerStackCandidates);
            Assert.Empty(game.State.EffectStack);
            Assert.Empty(game.State.DeferredEffectStack);
            Assert.Equal(L12Phase.Main, game.State.Phase);
            Assert.Equal(actor, game.State.ActivePlayer);
        }

        int DrainToMain(bool resolveDefense = false)
        {
            var responsePasses = 0;
            for (var safety = 0; safety < 160; safety++)
            {
                if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
                {
                    if (prompt.Kind == "response")
                    {
                        responsePasses++;
                        Send(prompt.PlayerIndex, Resolve(prompt, "pass"));
                        continue;
                    }
                    if (prompt.Continuation == "trigger-batch-order")
                    {
                        Send(prompt.PlayerIndex, Resolve(prompt,
                            cardInstanceIds: [.. prompt.ValidChoices]));
                        continue;
                    }
                    if (prompt.Continuation == "pending-activation"
                        && prompt.ValidChoices.Contains("mode:none"))
                    {
                        Send(prompt.PlayerIndex, Resolve(prompt, "mode:none"));
                        continue;
                    }
                    throw new Xunit.Sdk.XunitException(
                        $"Unexpected prompt {prompt.Kind}/{prompt.Continuation}/{prompt.SourceCardId}: "
                        + string.Join(',', prompt.ValidChoices));
                }
                if (resolveDefense
                    && game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice } defense)
                {
                    Send(1 - defense.AttackerPlayer,
                        new L12Command("resolveDefense", CardInstanceIds: []));
                    continue;
                }
                AssertOperableMain();
                return responsePasses;
            }
            throw new Xunit.Sdk.XunitException(
                "The real Mozi/Yingzheng authority chain did not return to an operable main phase.");
        }

        Send(actor, new L12Command("playCard", "mozi", Row: 0, Slot: 0));
        var modePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", modePrompt.Continuation);
        Assert.Equal("mozi", modePrompt.SourceInstanceId);
        Assert.Contains("mode:use", modePrompt.ValidChoices);
        Send(actor, Resolve(modePrompt, "mode:use"));

        var moralePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", moralePrompt.Continuation);
        Assert.Contains("morale-11", moralePrompt.ValidChoices);
        Send(actor, Resolve(moralePrompt, "morale-11"));

        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", targetPrompt.Continuation);
        Assert.Contains("protected-tianting", targetPrompt.ValidChoices);
        Assert.Contains("mozi", targetPrompt.ValidChoices);
        var targetCommand = Resolve(targetPrompt,
            cardInstanceIds: ["protected-tianting"]);
        Send(actor, targetCommand);

        var rawBeforeDuplicate = game.SerializeFullState();
        var hashBeforeDuplicate = game.ComputeStateHash();
        var revisionBeforeDuplicate = game.State.Revision;
        var factSequenceBeforeDuplicate = game.CardFactSignalSequence;
        Assert.False(game.Handle(actor, targetCommand).Accepted);
        Assert.Equal(rawBeforeDuplicate, game.SerializeFullState());
        Assert.Equal(hashBeforeDuplicate, game.ComputeStateHash());
        Assert.Equal(revisionBeforeDuplicate, game.State.Revision);
        Assert.Equal(factSequenceBeforeDuplicate, game.CardFactSignalSequence);

        Assert.True(DrainToMain() > 0);
        owner = game.State.Players[actor];
        var protectedTarget = Assert.Single(owner.Field[0],
            card => card?.InstanceId == "protected-tianting")!;
        Assert.Equal(1, protectedTarget.ImmortalUses);
        Assert.Equal(int.MaxValue, protectedTarget.ImmortalUntilTurn);
        Assert.Equal(actor, protectedTarget.ImmortalExpiresAtPlayerTurnStart);
        Assert.False(protectedTarget.Tapped);
        Assert.Equal(-1, protectedTarget.SummonRound);
        Assert.Equal(11, owner.Morale.Count);
        Assert.Equal(3, owner.Morale.Count(card => card.Tapped));
        Assert.DoesNotContain(owner.Morale, card => card.InstanceId == "morale-11");

        Send(actor, new L12Command("playCard", "yingzheng", Row: 0, Slot: 2));
        var entryCostPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-yingzheng-enter-cost", entryCostPrompt.Continuation);
        Assert.True(entryCostPrompt.IsPrivate);
        Assert.Contains("entry-cost", entryCostPrompt.ValidChoices);
        Send(actor, Resolve(entryCostPrompt, "entry-cost"));
        Assert.True(DrainToMain() > 0);

        owner = game.State.Players[actor];
        var survived = Assert.Single(owner.Field[0],
            card => card?.InstanceId == "protected-tianting")!;
        Assert.Equal(protectedTarget.InstanceId, survived.InstanceId);
        Assert.False(survived.Tapped);
        Assert.Equal(1000, survived.CurrentTroops);
        Assert.Equal(0, survived.ImmortalUses);
        Assert.Single(owner.Graveyard, card => card.InstanceId == "mozi");
        Assert.Single(owner.Graveyard, card => card.InstanceId == "entry-cost");
        Assert.DoesNotContain(owner.Graveyard,
            card => card.InstanceId == "protected-tianting");
        Assert.Single(owner.Hand, card => card.InstanceId.StartsWith("draw-after-mozi-death-",
            StringComparison.Ordinal));
        Assert.Empty(owner.Morale);
        Assert.Single(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("免死生效", StringComparison.Ordinal)
            && entry.Cards.Any(card => card.InstanceId == survived.InstanceId));

        Send(actor, new L12Command("attack", survived.InstanceId,
            Target: new L12AttackTarget("master")));
        var attack = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal(actor, attack.AttackerPlayer);
        Assert.Equal(survived.InstanceId, attack.AttackerInstanceId);
        Assert.Equal("master", attack.Target.Type);
        owner = game.State.Players[actor];
        var attacking = Assert.Single(owner.Field[0],
            card => card?.InstanceId == "protected-tianting")!;
        Assert.True(attacking.Tapped);
        Assert.Equal(1, attacking.AttacksThisTurn);

        Assert.True(DrainToMain(resolveDefense: true) > 0);
        owner = game.State.Players[actor];
        var completed = Assert.Single(owner.Field[0],
            card => card?.InstanceId == "protected-tianting")!;
        Assert.Equal(survived.InstanceId, completed.InstanceId);
        Assert.True(completed.Tapped);
        Assert.Equal(1, completed.AttacksThisTurn);
        Assert.Equal(0, completed.ImmortalUses);
        AssertOperableMain();
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
