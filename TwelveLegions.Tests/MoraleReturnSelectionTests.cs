using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class MoraleReturnSelectionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine CreateGame()
    {
        var game = new L12GameEngine(Catalog, "return-morale-regression", "RETURN", 8617,
            ["甲", "乙"], [0, 0], skipPreparation: true);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;
        game.State.PendingPrompts.Clear();
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

    [Fact]
    public void MixedReadyAndRestedMoraleRequiresDirectBoardSelection()
    {
        var game = CreateGame();
        var player = game.State.Players[0];
        var liubei = Card("S01-0105", "return-liubei");
        player.Field[0][0] = liubei;
        player.Morale.Clear();
        player.Morale.Add(new L12MoraleCard { InstanceId = "ready", CardId = "S01-01C1", Tapped = false });
        player.Morale.Add(new L12MoraleCard { InstanceId = "rested", CardId = "S01-01C1", Tapped = true });

        Assert.True(game.Handle(0, new L12Command("activateAbility", liubei.InstanceId,
            Ability: "searchBrothers")).Accepted);

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-return", prompt.Kind);
        Assert.Equal("resource-return", prompt.Data["choiceMode"]);
        Assert.Equal("请选择返还的士气", prompt.Text);
        Assert.Equal(2, player.Morale.Count);

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: ["ready"])).Accepted);
        Assert.DoesNotContain(player.Morale, morale => morale.InstanceId == "ready");
        Assert.Contains(player.Morale, morale => morale.InstanceId == "rested" && morale.Tapped);
    }

    [Fact]
    public void EquivalentMoraleOutcomesReturnAutomatically()
    {
        var game = CreateGame();
        var player = game.State.Players[0];
        var liubei = Card("S01-0105", "auto-return-liubei");
        player.Field[0][0] = liubei;
        player.Morale.Clear();
        player.Morale.Add(new L12MoraleCard { InstanceId = "ordinary-a", CardId = "S01-01C1", Tapped = false });
        player.Morale.Add(new L12MoraleCard { InstanceId = "ordinary-b", CardId = "S01-01C1", Tapped = false });

        Assert.True(game.Handle(0, new L12Command("activateAbility", liubei.InstanceId,
            Ability: "searchBrothers")).Accepted);

        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "resource-return");
        Assert.Single(player.Morale);
    }

    [Fact]
    public void SelectedBlackLotusReturnsToGraveyardInsteadOfMoraleDeck()
    {
        var game = CreateGame();
        var player = game.State.Players[0];
        var liubei = Card("S01-0105", "lotus-return-liubei");
        player.Field[0][0] = liubei;
        player.Morale.Clear();
        player.Morale.Add(new L12MoraleCard { InstanceId = "ordinary", CardId = "S01-01C1", Tapped = false });
        player.Morale.Add(new L12MoraleCard { InstanceId = "lotus", CardId = "S02-0010", Tapped = false });

        Assert.True(game.Handle(0, new L12Command("activateAbility", liubei.InstanceId,
            Ability: "searchBrothers")).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("black-lotus", prompt.Data["lotus:resourceType"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: ["lotus"])).Accepted);

        Assert.DoesNotContain(player.Morale, morale => morale.InstanceId == "lotus");
        Assert.Contains(player.Graveyard, card => card.InstanceId == "lotus" && card.CardId == "S02-0010");
    }
}
