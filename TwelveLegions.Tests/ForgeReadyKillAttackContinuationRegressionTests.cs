using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ForgeReadyKillAttackContinuationRegressionTests
{
    private static readonly L12Catalog Catalog =
        L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-17T00:00:00Z");

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "bug:BUG-20260908-c3b3ed30")]
    public void RealForgeGrantReadiesTheKillerAndAllowsThatSameLegionToAttackTheMaster(
        int actor, bool restoreBetweenCommands)
    {
        var basis = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "forge-ready-kill-attack", "SYNTHETIC",
            930800 + actor, ["P0", "P1"], [basis, basis], skipPreparation: true,
            disasterMode: "none", autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2, utcNow: () => Now);
        game.State.ActivePlayer = actor;
        game.State.FirstPlayer = actor;
        game.State.Round = 3;
        game.State.TurnSerial = 17;
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
        var opponent = game.State.Players[1 - actor];
        owner.Relic = Card("S02-0520", "forge", actor);
        owner.Field[0][0] = Card("S02-0502", "heracles", actor);
        owner.Morale.Add(new L12MoraleCard
        {
            CardId = "S02-05C1", InstanceId = "forge-cost", Tapped = false,
        });
        opponent.Field[0][0] = PlainLegion("ordinary-victim", 1 - actor, 2000);

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

        static L12Command Resolve(L12Prompt prompt, string choice) => new("resolvePrompt",
            PromptId: prompt.PromptId, Choice: choice, ActivationId: prompt.ActivationId,
            SourceInstanceId: prompt.SourceInstanceId, SourceCardId: prompt.SourceCardId,
            Step: prompt.Step, CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller);

        int DrainToMain()
        {
            var responsePasses = 0;
            for (var safety = 0; safety < 100; safety++)
            {
                if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
                {
                    if (prompt.Kind == "response") responsePasses++;
                    var choice = prompt.Kind == "response" ? "pass"
                        : prompt.ValidChoices.Contains("mode:use") ? "mode:use"
                        : prompt.ValidChoices.Contains("skip") ? "skip"
                        : prompt.ValidChoices.Contains("no") ? "no"
                        : throw new Xunit.Sdk.XunitException(
                            $"Unexpected prompt {prompt.Kind}/{prompt.Continuation}: {string.Join(',', prompt.ValidChoices)}");
                    Send(prompt.PlayerIndex, Resolve(prompt, choice));
                    continue;
                }
                if (game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice } defense)
                {
                    Send(1 - defense.AttackerPlayer, new L12Command("resolveDefense", CardInstanceIds: []));
                    continue;
                }
                Assert.Null(game.State.PendingDefense);
                Assert.Empty(game.State.PendingPrompts);
                Assert.Empty(game.State.PendingActivations);
                Assert.Empty(game.State.EffectStack);
                Assert.Empty(game.State.DeferredEffectStack);
                Assert.Equal(L12Phase.Main, game.State.Phase);
                return responsePasses;
            }
            throw new Xunit.Sdk.XunitException("The real authority chain did not return to an operable main phase.");
        }

        Send(actor, new L12Command("activateAbility", "forge", Ability: "forgeReadyOnKill"));
        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(actor, targetPrompt.PlayerIndex);
        Assert.Equal("pending-activation", targetPrompt.Continuation);
        Assert.Contains("heracles", targetPrompt.ValidChoices);
        Assert.False(game.State.Players[actor].Relic!.Tapped);
        Assert.False(Assert.Single(game.State.Players[actor].Morale).Tapped);

        var targetCommand = Resolve(targetPrompt, "heracles");
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

        owner = game.State.Players[actor];
        Assert.True(owner.Relic!.Tapped);
        Assert.True(Assert.Single(owner.Morale).Tapped);
        var forgeResponsePasses = DrainToMain();
        Assert.True(forgeResponsePasses > 0);
        owner = game.State.Players[actor];
        var granted = Assert.Single(owner.Field[0], card => card?.InstanceId == "heracles")!;
        Assert.Equal(game.State.TurnSerial, granted.ReadyAfterNextKillUntilTurn);
        Assert.Equal("匠神锻造炉", granted.ReadyAfterNextKillSourceName);
        Assert.Single(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("下一次击杀对方军团后转为活跃", StringComparison.Ordinal)
            && entry.Cards.Any(card => card.InstanceId == granted.InstanceId));

        Send(actor, new L12Command("attack", granted.InstanceId,
            Target: new L12AttackTarget("legion", "ordinary-victim")));
        _ = DrainToMain();

        owner = game.State.Players[actor];
        opponent = game.State.Players[1 - actor];
        var readied = Assert.Single(owner.Field[0], card => card?.InstanceId == "heracles")!;
        Assert.Single(opponent.Graveyard, card => card.InstanceId == "ordinary-victim");
        Assert.Equal(2000, readied.Troops);
        Assert.False(readied.Tapped);
        Assert.Equal(1, readied.AttacksThisTurn);
        Assert.Equal(-1, readied.ReadyAfterNextKillUntilTurn);
        Assert.Null(readied.ReadyAfterNextKillSourceName);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.Text.Contains("击杀后转为活跃", StringComparison.Ordinal)
            && entry.Cards.Any(card => card.InstanceId == readied.InstanceId));
        Assert.Single(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("因匠神锻造炉转为活跃", StringComparison.Ordinal)
            && entry.Cards.Any(card => card.InstanceId == readied.InstanceId));
        Assert.True(owner.Relic!.Tapped);
        Assert.True(Assert.Single(owner.Morale).Tapped);

        Send(actor, new L12Command("attack", readied.InstanceId,
            Target: new L12AttackTarget("master")));
        var secondAttack = Assert.IsType<L12PendingDefense>(game.State.PendingDefense);
        Assert.Equal(actor, secondAttack.AttackerPlayer);
        Assert.Equal(readied.InstanceId, secondAttack.AttackerInstanceId);
        Assert.Equal("master", secondAttack.Target.Type);
        Assert.True(Assert.Single(game.State.Players[actor].Field[0],
            card => card?.InstanceId == "heracles")!.Tapped);
        Assert.Equal(2, Assert.Single(game.State.Players[actor].Field[0],
            card => card?.InstanceId == "heracles")!.AttacksThisTurn);
    }

    private static L12CardInstance Card(string id, string instanceId, int owner)
    {
        var definition = Catalog.Cards[id];
        return new L12CardInstance
        {
            CardId = id, InstanceId = instanceId, OwnerIndex = owner, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction, ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0, HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect, Traits = [.. definition.Traits],
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            Profession = definition.Profession, EffectiveProfession = definition.Profession, SummonRound = -1,
        };
    }

    private static L12CardInstance PlainLegion(string instanceId, int owner, int troops) => new()
    {
        CardId = "TEST-ORDINARY-2000", InstanceId = instanceId, OwnerIndex = owner,
        Name = "普通2000军团", CardType = "legion", Faction = "universal", Cost = 1,
        BaseTroops = troops, Troops = troops, SummonRound = -1,
    };
}
