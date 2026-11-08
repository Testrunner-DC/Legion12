using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class WorldUpheavalYangJianRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, string returnPlacement)
    {
        var baseDeck = Catalog.DeckAt(0);
        var firstDeck = new L12PresetDeckDefinition
        {
            Name = "杨戬天地异变组合回归牌库",
            MasterId = "S01-01M1",
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var secondDeck = new L12PresetDeckDefinition
        {
            Name = "对照牌库",
            MasterId = baseDeck.MasterId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "world-upheaval-yangjian", $"WU-YJ-{returnPlacement}", seed,
            ["甲", "乙"], [firstDeck, secondDeck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
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
            TrialValue = definition.TrialValue ?? 0,
        };
    }

    private static L12CardInstance[] SnapshotHand(L12GameEngine game)
    {
        var view = game.SnapshotFor(0).Players[0];
        return Assert.IsType<L12CardInstance[]>(view.GetType().GetProperty("hand")!.GetValue(view));
    }

    private static void AddReadyMorale(L12PlayerState player, int count)
    {
        while (player.Morale.Count(card => !card.Tapped) < count)
        {
            var morale = player.MoraleDeck[0];
            player.MoraleDeck.RemoveAt(0);
            morale.Tapped = false;
            player.Morale.Add(morale);
        }
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 80 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            Resolve(game, "pass");
        }
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassUntilFlow(L12GameEngine game, string flow)
    {
        for (var safety = 0; safety < 80; safety++)
        {
            if (game.State.EffectStack.Any(item => item.Data.GetValueOrDefault("atomicFlow") == flow)) return;
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", prompt.Kind);
            Resolve(game, "pass");
        }
        Assert.Contains(game.State.EffectStack, item => item.Data.GetValueOrDefault("atomicFlow") == flow);
    }

    [Theory]
    [InlineData("top", "S02-0610", "S02-0611")]
    [InlineData("bottom", "S02-0611", "S02-0610")]
    public void WorldUpheavalRecomputesMatchingProfessionAfterYangJianReturn(
        string returnPlacement, string drawnCardId, string remainingTopCardId)
    {
        var game = Create(returnPlacement == "top" ? 84201 : 84202, returnPlacement);
        var player = game.State.Players[0];
        var sameProfession = Card("S02-0604", $"wu-yj-same-{returnPlacement}");
        var differentProfession = Card("S02-0602", $"wu-yj-different-{returnPlacement}");
        var drawn = Card(drawnCardId, $"wu-yj-drawn-{returnPlacement}");
        var remainingTop = Card(remainingTopCardId, $"wu-yj-remaining-top-{returnPlacement}");
        player.Hand.Clear();
        player.Hand.AddRange([sameProfession, differentProfession]);
        player.Library.Clear();
        player.Library.AddRange([drawn, remainingTop]);
        player.Morale.Clear();
        AddReadyMorale(player, 1);
        game.State.ActiveDisaster = Card("S02-DS01", $"wu-yj-disaster-{returnPlacement}");

        var beforeSame = Assert.Single(SnapshotHand(game), card => card.InstanceId == sameProfession.InstanceId);
        if (returnPlacement == "bottom") Assert.Null(beforeSame.PlayBlockedReason);

        var activation = game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "drawCycle"));
        Assert.True(activation.Accepted, activation.Error);
        PassUntilFlow(game, "yangjian-return");
        PassResponses(game);

        var cardPrompt = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "yangjian-return-card");
        Assert.Contains(drawn.InstanceId, cardPrompt.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: cardPrompt.PromptId,
            Choice: drawn.InstanceId)).Accepted);
        var placePrompt = Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "yangjian-return-place");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: placePrompt.PromptId,
            Choice: returnPlacement)).Accepted);
        PassResponses(game);

        Assert.Equal(remainingTop.Profession == sameProfession.Profession
            ? remainingTop.CardId
            : drawn.CardId, player.Library.First().CardId);
        var afterSame = Assert.Single(SnapshotHand(game), card => card.InstanceId == sameProfession.InstanceId);
        var afterDifferent = Assert.Single(SnapshotHand(game), card => card.InstanceId == differentProfession.InstanceId);
        Assert.Contains("天地异变", afterSame.PlayBlockedReason);
        Assert.Null(afterDifferent.PlayBlockedReason);

        var samePlay = game.Handle(0, new L12Command("playCard", sameProfession.InstanceId, Row: 0, Slot: 0));
        Assert.False(samePlay.Accepted);
        Assert.Contains("天地异变", samePlay.Error);
    }
}
