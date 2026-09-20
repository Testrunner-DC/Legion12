using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ActiveRestResourceLibraryLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, bool autoPassEmptyResponses = false)
    {
        var game = new L12GameEngine(Catalog, "active-rest-resource-library", "ACTIVE-REST-RESOURCE", seed,
            ["甲", "乙"], [Catalog.DeckAt(0), Catalog.DeckAt(0)], skipPreparation: true,
            autoPassEmptyResponses: autoPassEmptyResponses, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Removed.Clear();
            player.Resolving.Clear();
            player.ExtraRelics.Clear();
            player.Relic = null;
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
        }
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
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = 0,
            SummonRound = -1,
        };
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static L12ActionEvent Result(L12GameEngine game)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result");

    [Fact]
    [Trait("L12Evidence", "ability:addMorale")]
    public void BaiQiEmptyMoraleDeckSkipsEffectButKeepsActiveRestCost()
    {
        var game = Create(9401);
        var player = game.State.Players[0];
        var source = Card("S01-0109", "resource-baiqi-empty");
        player.Field[0][0] = source;

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "addMorale")).Accepted);
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Empty(player.Morale);
        Assert.Equal("skipped", Result(game).EffectResultStatus);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-noop"
            && entry.Text.Contains("士气牌库为空", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:addMorale")]
    public void BaiQiBlockedMoraleAdditionFailsButKeepsActiveRestCost()
    {
        var game = Create(9402);
        var player = game.State.Players[0];
        var source = Card("S01-0109", "resource-baiqi-blocked");
        player.Field[0][0] = source;
        player.MoraleDeck.Add(new L12MoraleCard { InstanceId = "resource-baiqi-morale", CardId = "S01-01C1" });
        player.FactionMoraleAdditionForbiddenUntilTurn = game.State.TurnSerial;

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "addMorale")).Accepted);
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Empty(player.Morale);
        Assert.Single(player.MoraleDeck);
        Assert.Equal("failed", Result(game).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:addMorale")]
    public void NegatedBaiQiKeepsRestAndDoesNotTakeMoraleFromTheDeck()
    {
        var game = Create(9403);
        var player = game.State.Players[0];
        var source = Card("S01-0109", "resource-baiqi-negated");
        player.Field[0][0] = source;
        player.MoraleDeck.Add(new L12MoraleCard { InstanceId = "resource-baiqi-negated-morale", CardId = "S01-01C1" });

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "addMorale")).Accepted);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Empty(player.Morale);
        Assert.Single(player.MoraleDeck);
        Assert.Equal("negated", Result(game).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:artifactDraw")]
    public void NegatedShanheDrawKeepsReturnedMoraleAndRestWithoutDrawing()
    {
        var game = Create(9404);
        var player = game.State.Players[0];
        var source = Card("S01-0117", "resource-shanhe-negated");
        var top = Card("S01-0101", "resource-shanhe-top");
        player.Relic = source;
        player.Library.Add(top);
        player.Morale.Add(new L12MoraleCard { InstanceId = "resource-shanhe-cost", CardId = "S01-01C1" });

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "artifactDraw")).Accepted);
        var firstPrompt = Assert.Single(game.State.PendingPrompts);
        if (firstPrompt.Kind != "response")
            Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: firstPrompt.PromptId,
                CardInstanceIds: ["resource-shanhe-cost"])).Accepted);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Empty(player.Morale);
        Assert.Equal([top.InstanceId], player.Library.Select(card => card.InstanceId));
        Assert.Empty(player.Hand);
        Assert.Equal("negated", Result(game).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:kaneMillOne")]
    public void NegatedKaneKeepsRestAndDoesNotDiscardTheTopCard()
    {
        var game = Create(9405);
        var player = game.State.Players[0];
        var source = Card("ST03-07", "resource-kane-negated");
        var top = Card("ST01-05", "resource-kane-top");
        player.Field[0][0] = source;
        player.Library.Add(top);

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "kaneMillOne")).Accepted);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Equal([top.InstanceId], player.Library.Select(card => card.InstanceId));
        Assert.Empty(player.Graveyard);
        Assert.Equal("negated", Result(game).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:kaneMillOne")]
    public void KaneEmptyLibraryLosesAndDoesNotPublishFalseDiscardSuccess()
    {
        var game = Create(9406);
        var player = game.State.Players[0];
        var source = Card("ST03-07", "resource-kane-empty");
        player.Field[0][0] = source;

        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "kaneMillOne")).Accepted);
        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Equal(1, game.State.Winner);
        Assert.Equal("failed", Result(game).EffectResultStatus);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("弃置我方牌库顶部1张牌", StringComparison.Ordinal));
    }
}
