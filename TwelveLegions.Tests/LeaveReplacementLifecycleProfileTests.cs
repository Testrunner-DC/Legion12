using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class LeaveReplacementLifecycleProfileTests
{
    private const string MasterReturnAbilityId = "S02-01M1:ability:leave:cf42cfffe1b9b9bc";
    private const string AttachedDiscardAbilityId = "S02-0013:ability:host-leaves-artifact:b2720c3b205be910";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence(MasterReturnAbilityId, "normal", "reconnect", "presentation-consumers")]
    public void WukongLeaveReplacementAndOptionalMoraleSurviveRestoreWithoutOrdinaryDestination()
    {
        var game = Create(72520);
        var owner = game.State.Players[0];
        var wukong = Card("S02-01M1", "leave-profile-wukong", 0);
        wukong.IsMasterLegion = true;
        owner.Field[0][0] = wukong;
        owner.MoraleDeck.Add(new L12MoraleCard { InstanceId = "leave-profile-morale", CardId = "S02-01C1" });
        game.State.Players[1].Morale.Add(new L12MoraleCard { InstanceId = "leave-profile-opponent", CardId = "S02-01C1" });

        var result = game.HandleGm(new L12GmCommand("destroyCard", 0, CardInstanceId: wukong.InstanceId));
        Assert.True(result.Accepted, result.Error);
        Assert.DoesNotContain(owner.Graveyard, card => card.InstanceId == wukong.InstanceId);
        Assert.Single(game.State.Events, entry => entry.Type == "return"
            && entry.Cards.Any(card => card.InstanceId == wukong.InstanceId));
        var activation = Assert.Single(game.State.PendingPrompts);

        game = Restore(game);
        owner = game.State.Players[0];
        activation = Assert.Single(game.State.PendingPrompts, prompt => prompt.PromptId == activation.PromptId);
        result = game.Handle(activation.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: activation.PromptId, Choice: "mode:use"));
        Assert.True(result.Accepted, result.Error);
        PassResponses(game);

        Assert.DoesNotContain(owner.Graveyard, card => card.InstanceId == wukong.InstanceId);
        Assert.True(Assert.Single(owner.Morale).Tapped);
        Assert.Single(game.State.Events, entry => entry.Type == "return"
            && entry.Cards.Any(card => card.InstanceId == wukong.InstanceId));
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved");
    }

    [Fact]
    [L12AbilityEvidence(AttachedDiscardAbilityId, "normal", "reconnect", "presentation-consumers")]
    public void AttachedHolyLockFollowsRelicOwnerToGraveyardAcrossRestore()
    {
        var game = Create(72521);
        var lockOwner = game.State.Players[0];
        var relicController = game.State.Players[1];
        var host = Card("S01-0117", "leave-profile-relic", 1);
        var holyLock = Card("S02-0013", "leave-profile-lock", 0);
        host.AttachedCards.Add(holyLock);
        relicController.Relic = host;

        game = Restore(game);
        lockOwner = game.State.Players[0];
        relicController = game.State.Players[1];
        host = Assert.IsType<L12CardInstance>(relicController.Relic);
        holyLock = Assert.Single(host.AttachedCards);
        var result = game.HandleGm(new L12GmCommand("returnCardToHand", 1,
            CardInstanceId: host.InstanceId));
        Assert.True(result.Accepted, result.Error);

        Assert.Null(relicController.Relic);
        Assert.Empty(host.AttachedCards);
        Assert.Contains(relicController.Hand, card => card.InstanceId == host.InstanceId);
        Assert.Contains(lockOwner.Graveyard, card => card.InstanceId == holyLock.InstanceId);
        Assert.Single(game.State.Events, entry => entry.Type == "grave"
            && entry.Cards.Any(card => card.InstanceId == holyLock.InstanceId)
            && entry.Text.Contains("离开圣物区", StringComparison.Ordinal));
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var count = 0; count < 50 && game.State.PendingPrompts.FirstOrDefault() is { } prompt; count++)
        {
            var choice = prompt.Kind == "response" ? "pass"
                : prompt.ValidChoices.Contains("yes") ? "yes"
                : prompt.ValidChoices.Contains("skip") ? "skip"
                : prompt.ValidChoices.First();
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "leave-profile", "LEAVE-PROFILE", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int ownerIndex)
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
            HasPrintedCost = definition.Cost is not null,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = ownerIndex,
        };
    }
}
