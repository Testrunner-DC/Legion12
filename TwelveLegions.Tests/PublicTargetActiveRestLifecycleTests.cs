using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PublicTargetActiveRestLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, string? firstMasterId = null)
    {
        var baseDeck = Catalog.DeckAt(0);
        var firstDeck = firstMasterId is null
            ? baseDeck
            : new L12PresetDeckDefinition
            {
                Name = $"{firstMasterId}公开目标测试牌库",
                MasterId = firstMasterId,
                CardIds = [.. baseDeck.CardIds],
                MoraleIds = [.. baseDeck.MoraleIds],
                SpecialIds = [.. baseDeck.SpecialIds],
            };
        var game = new L12GameEngine(Catalog, "public-target-active-rest", "PUBLIC-TARGET-ACTIVE-REST",
            seed, ["甲", "乙"], [firstDeck, baseDeck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
            foreach (var row in player.Field)
                Array.Clear(row);
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
        };
    }

    private static void HoldResponse(L12GameEngine game, string instanceId)
    {
        var response = Card("S01-0019", instanceId);
        response.Hidden = true;
        response.SetRound = 0;
        game.State.Players[1].Field[1][2] = response;
    }

    private static void AddReadyMorale(L12PlayerState player, int count)
    {
        while (player.Morale.Count < count)
        {
            var morale = player.MoraleDeck[0];
            player.MoraleDeck.RemoveAt(0);
            morale.Tapped = false;
            player.Morale.Add(morale);
        }
    }

    private static L12Prompt Choose(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    private static L12ActionEvent Result(L12GameEngine game, string sourceInstanceId)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == sourceInstanceId));

    [Fact]
    [Trait("L12Evidence", "ability:merlinRune")]
    public void MerlinTargetBecomingNonPublicFailsAndKeepsBothCosts()
    {
        var game = Create(91501);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var source = Card("S02-0603", "merlin-public-source");
        var target = Card("S01-0004", "merlin-public-target");
        player.Field[0][0] = source;
        player.SpecialZones.Runes = 1;
        enemy.Field[0][0] = target;
        HoldResponse(game, "merlin-public-response");

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "merlinRune")).Accepted);
        Choose(game, "mode:debuff");
        var oldTargetPrompt = Choose(game, target.InstanceId);
        target.Hidden = true;

        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Equal(target.BaseTroops, target.Troops);
        Assert.Equal("failed", Result(game, source.InstanceId).EffectResultStatus);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("兵力-3000", StringComparison.Ordinal));
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: oldTargetPrompt.PromptId,
            Choice: target.InstanceId)).Accepted);
    }

    [Fact]
    [Trait("L12Evidence", "ability:avalonDebuff")]
    public void AvalonRestCostAndFrozenTargetSurviveRestoreButHiddenTargetFails()
    {
        var game = Create(91502, "S02-06D1");
        var target = Card("S01-0004", "avalon-public-target");
        game.State.Players[1].Field[0][0] = target;
        HoldResponse(game, "avalon-public-response");

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "avalonDebuff")).Accepted);
        var oldTargetPrompt = Choose(game, target.InstanceId);
        target.Hidden = true;
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

        PassResponses(game);

        Assert.True(game.State.Players[0].MasterTapped);
        var restoredTarget = game.State.Players[1].Field[0][0]!;
        Assert.Equal(restoredTarget.BaseTroops, restoredTarget.Troops);
        Assert.Equal("failed", Result(game, "master-0").EffectResultStatus);
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: oldTargetPrompt.PromptId,
            Choice: target.InstanceId)).Accepted);
    }

    [Fact]
    [Trait("L12Evidence", "ability:lightSwordActive")]
    public void LightSwordTargetBecomingNonPublicFailsWithoutRefundingRestOrDiscard()
    {
        var game = Create(91503);
        var player = game.State.Players[0];
        var source = Card("ST06-09", "light-sword-public-source");
        var discard = Card("ST01-05", "light-sword-public-discard");
        var target = Card("ST06-02", "light-sword-public-target");
        player.Relic = source;
        player.Hand.Add(discard);
        player.Field[0][0] = target;
        HoldResponse(game, "light-sword-public-response");

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "lightSwordActive")).Accepted);
        Choose(game, "mode:buff");
        Choose(game, discard.InstanceId);
        Choose(game, target.InstanceId);
        target.Hidden = true;

        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Contains(discard, player.Graveyard);
        Assert.Equal(target.BaseTroops, target.Troops);
        Assert.Equal("failed", Result(game, source.InstanceId).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:palaceExchange")]
    public void PalaceHiddenKillTargetFailsWithoutRefundingRestOrReturnedMorale()
    {
        var game = Create(91504, "S01-01D1");
        var player = game.State.Players[0];
        var target = Card("S01-0003", "palace-public-target");
        game.State.Players[1].Field[0][0] = target;
        AddReadyMorale(player, target.CurrentCost);
        HoldResponse(game, "palace-public-response");

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "palaceExchange")).Accepted);
        if (Assert.Single(game.State.PendingPrompts).ValidChoices.Contains("mode:none"))
            Choose(game, "mode:none");
        Choose(game, target.InstanceId);
        target.Hidden = true;

        PassResponses(game);

        Assert.True(player.MasterTapped);
        Assert.Equal(target.CurrentCost, player.ReturnedMoraleThisTurn);
        Assert.Same(target, game.State.Players[1].Field[0][0]);
        Assert.Equal("failed", Result(game, "master-0").EffectResultStatus);
    }
}
