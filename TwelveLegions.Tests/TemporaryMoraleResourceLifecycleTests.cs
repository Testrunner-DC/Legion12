using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TemporaryMoraleResourceLifecycleTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var basis = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "临时士气生命周期",
            MasterId = "S01-01M1",
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "temporary-morale-resource", "TMR", seed,
            ["甲", "乙"], [deck, basis], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.Round = 3;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear();
            player.Morale.Clear();
            player.UsedAbilities.Clear();
            player.TemporaryMorale = 0;
        }
        game.State.Players[0].Library.Clear();
        game.State.Players[0].Library.Add(Card("S01-0101", "temporary-morale-draw"));
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
        };
    }

    private static L12MoraleCard Morale(string instanceId) => new()
    {
        InstanceId = instanceId,
        CardId = "S01-01C1",
    };

    [Fact]
    public void StaleTemporaryMoraleSelectionReopensActivePaymentWithCancelFallback()
    {
        var game = Create(92701);
        var player = game.State.Players[0];
        player.TemporaryMorale = 2;
        var ordinary = Morale("temporary-retry-ordinary");
        player.Morale.Add(ordinary);

        var opened = game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "drawCycle"));

        Assert.True(opened.Accepted, opened.Error);
        var first = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("active-morale-choice", first.Continuation);
        Assert.Contains("temporary-morale:2", first.ValidChoices);
        player.TemporaryMorale = 1;

        var stale = game.Handle(0, new L12Command("resolvePrompt", PromptId: first.PromptId,
            CardInstanceIds: ["temporary-morale:2"]));

        Assert.True(stale.Accepted, stale.Error);
        var retry = Assert.Single(game.State.PendingPrompts);
        Assert.NotEqual(first.PromptId, retry.PromptId);
        Assert.Equal("active-morale-choice", retry.Continuation);
        Assert.Contains("temporary-morale:1", retry.ValidChoices);
        Assert.DoesNotContain("temporary-morale:2", retry.ValidChoices);
        Assert.Contains("cancel", retry.ValidChoices);
        Assert.Contains("已失效", retry.Data["retryReason"]);
        Assert.Equal(1, player.TemporaryMorale);
        Assert.False(ordinary.Tapped);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: retry.PromptId,
            Choice: "cancel")).Accepted);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal(1, player.TemporaryMorale);
        Assert.False(ordinary.Tapped);
    }

    [Fact]
    public void TemporaryMoralePaymentPromptSurvivesV2SnapshotAndCanOnlyPayOnce()
    {
        var game = Create(92702);
        var player = game.State.Players[0];
        player.TemporaryMorale = 2;
        player.Morale.Add(Morale("temporary-reconnect-ordinary"));
        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "drawCycle")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);

        var snapshot = JsonSerializer.Serialize(game.SnapshotFor(0));
        Assert.Contains(prompt.PromptId, snapshot, StringComparison.Ordinal);
        Assert.Contains("temporary-morale:1", snapshot, StringComparison.Ordinal);
        Assert.Contains("temporary-morale:2", snapshot, StringComparison.Ordinal);

        var paid = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: ["temporary-morale:1"]));
        Assert.True(paid.Accepted, paid.Error);
        Assert.Equal(1, player.TemporaryMorale);

        var replay = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: ["temporary-morale:1"]));
        Assert.False(replay.Accepted);
        Assert.Equal(1, player.TemporaryMorale);
    }

    [Fact]
    public void WaivedMasterBaseCostAndPrideSurchargeLeaveNoTemporaryVoucher()
    {
        var game = Create(92703);
        var player = game.State.Players[0];
        player.TemporaryMorale = 1;
        player.MasterMoraleWaiverUntilTurn = game.State.TurnSerial;
        game.State.ActiveDisaster = Card("S02-DS06", "temporary-pride-disaster");

        var result = game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "drawCycle"));

        Assert.True(result.Accepted, result.Error);
        Assert.Equal(0, player.TemporaryMorale);
        Assert.Contains(game.State.EffectStack, item => item.Data.GetValueOrDefault("ability") == "drawCycle");
    }
}
