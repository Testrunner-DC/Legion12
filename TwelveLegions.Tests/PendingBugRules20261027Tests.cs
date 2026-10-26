using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PendingBugRules20261027Tests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("Bug", "BUG-20261024-d1e22f0b:current-rule")]
    [Trait("L12Evidence", "ability:amaterasuReady")]
    public void AmaterasuMayReadyRestedGodPowerAsMoraleAcrossV2Restore(int actor)
    {
        const string discardId = "pending-rule-amaterasu-discard";
        const string godPowerId = "pending-rule-amaterasu-god-power";
        const string ordinaryMoraleId = "pending-rule-amaterasu-ordinary-morale";
        var game = Create(actor);
        var player = game.State.Players[actor];
        player.Hand.Add(Card("S01-0401", discardId, actor));
        player.Morale.Add(new L12MoraleCard
        {
            CardId = "S02-05C1",
            InstanceId = godPowerId,
            IsGodPower = true,
            Tapped = true,
        });
        player.Morale.Add(new L12MoraleCard
        {
            CardId = "S01-04C1",
            InstanceId = ordinaryMoraleId,
            Tapped = true,
        });
        game.State.Revision++;
        game = Restore(game);

        game = Send(game, actor, new L12Command("activateAbility", $"master-{actor}",
            Ability: "amaterasuReady"));
        var discardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(discardId, discardPrompt.ValidChoices);
        game = Resolve(game, discardPrompt, choice: discardId);

        var moralePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(godPowerId, moralePrompt.ValidChoices);
        Assert.Contains(ordinaryMoraleId, moralePrompt.ValidChoices);
        Assert.Equal(0, moralePrompt.MinChoose);
        Assert.Equal(2, moralePrompt.MaxChoose);
        game = Resolve(game, moralePrompt, cards: [godPowerId, ordinaryMoraleId]);

        for (var guard = 0; game.State.PendingPrompts.Count > 0; guard++)
        {
            Assert.True(guard < 8, "天照大神回费链超过限定响应步数");
            var response = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", response.Kind);
            game = Resolve(game, response, choice: "pass");
        }

        player = game.State.Players[actor];
        var godPower = Assert.Single(player.Morale, morale => morale.InstanceId == godPowerId);
        var ordinaryMorale = Assert.Single(player.Morale, morale => morale.InstanceId == ordinaryMoraleId);
        Assert.True(godPower.IsGodPower);
        Assert.False(godPower.Tapped);
        Assert.False(ordinaryMorale.Tapped);
        Assert.Contains(player.Graveyard, card => card.InstanceId == discardId);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Null(game.State.ResponseWindow);
        Assert.Equal(L12Phase.Main, game.State.Phase);
    }

    private static L12GameEngine Create(int actor)
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "Pending bug rules",
            MasterId = "S01-04M1",
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "pending-bug-rules-20261027", "PENDING-RULES", 202610270 + actor,
            ["Synthetic A", "Synthetic B"], [deck, deck], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2,
            utcNow: () => DateTimeOffset.Parse("2026-10-27T03:00:00+08:00"));
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Removed.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
            player.Hp = 8;
        }
        game.State.ActivePlayer = actor;
        game.State.FirstPlayer = actor;
        game.State.Round = 3;
        game.State.TurnSerial = 6;
        game.State.Phase = L12Phase.Main;
        game.State.PendingPrompts.Clear();
        game.State.PendingActivations.Clear();
        game.State.EffectStack.Clear();
        game.State.DeferredEffectStack.Clear();
        game.State.PendingTriggerBatches.Clear();
        game.State.PendingTriggerStackCandidates.Clear();
        game.State.SuspendedCombatContexts.Clear();
        game.State.Events.Clear();
        game.State.AuthorityEvents.Clear();
        game.State.Log.Clear();
        game.State.LastAction = null;
        game.State.ResponseWindow = null;
        game.State.PendingDefense = null;
        game.State.ActiveDisaster = null;
        game.State.Revision++;
        return game;
    }

    private static L12GameEngine Send(L12GameEngine game, int actor, L12Command command)
    {
        game = Restore(game);
        var result = game.Handle(actor, command);
        Assert.True(result.Accepted, $"{command.Type}: {result.Error}");
        return Restore(game);
    }

    private static L12GameEngine Resolve(L12GameEngine game, L12Prompt prompt,
        string? choice = null, List<string>? cards = null)
        => Send(game, prompt.PlayerIndex, new L12Command("resolvePrompt",
            PromptId: prompt.PromptId, Choice: choice, CardInstanceIds: cards,
            ActivationId: prompt.ActivationId, SourceInstanceId: prompt.SourceInstanceId,
            SourceCardId: prompt.SourceCardId, Step: prompt.Step,
            CreatedRevision: prompt.CreatedRevision, Controller: prompt.Controller));

    private static L12GameEngine Restore(L12GameEngine game)
    {
        var state = game.SerializeFullState();
        var hash = game.ComputeStateHash();
        var random = game.RandomState!.Value;
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, state, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        Assert.Equal(state, restored.SerializeFullState());
        Assert.Equal(hash, restored.ComputeStateHash());
        Assert.Equal(random, restored.RandomState);
        return restored;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            OwnerIndex = owner,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            IsCounterTactic = definition.IsCounterTactic,
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
}
