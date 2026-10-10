using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

internal static class PharaohFestivalRingFactionFixture
{
    internal static L12Catalog Catalog { get; } =
        L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    internal static L12GameEngine Create(int seed, string firstMaster = "ST04-M1",
        string secondMaster = "S01-02M3", bool autoPassEmptyResponses = false)
    {
        var basis = Catalog.DeckAt(0);
        L12PresetDeckDefinition Deck(string name, string master) => new()
        {
            Name = name,
            MasterId = master,
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [.. basis.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "bug-effect-reproduction", "BUGEFFECT", seed,
            ["甲", "乙"], [Deck("甲测试牌库", firstMaster), Deck("乙测试牌库", secondMaster)],
            skipPreparation: true, autoPassEmptyResponses: autoPassEmptyResponses,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
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
            player.Resolving.Clear();
            player.Morale.Clear();
            player.UsedAbilities.Clear();
            player.SpecialZones.Runes = 0;
            player.SpecialZones.Trials.Clear();
        }
        return game;
    }

    internal static L12CardInstance Card(string cardId, string instanceId, int owner,
        int? troops = null, int? cost = null)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            IsCounterTactic = definition.IsCounterTactic,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = cost ?? definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }

    internal static L12MoraleCard Morale(string instanceId)
        => new() { CardId = "S01-01C1", InstanceId = instanceId };

    internal static L12Prompt Prompt(L12GameEngine game)
        => Assert.Single(game.State.PendingPrompts);

    internal static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Prompt(game);
        Assert.Contains(choice, prompt.ValidChoices);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    internal static L12Prompt ResolveMany(L12GameEngine game, params string[] choices)
    {
        var prompt = Prompt(game);
        foreach (var choice in choices) Assert.Contains(choice, prompt.ValidChoices);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: [.. choices]));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    internal static void PassResponses(L12GameEngine game, int limit = 80)
    {
        for (var count = 0; count < limit
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; count++)
            Resolve(game, "pass");
        Assert.NotEqual("response", game.State.PendingPrompts.FirstOrDefault()?.Kind);
    }

    internal static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
}
