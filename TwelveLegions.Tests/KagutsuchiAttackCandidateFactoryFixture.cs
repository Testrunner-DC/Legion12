using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

internal static class KagutsuchiAttackCandidateFactoryFixture
{
    internal static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    internal static L12GameEngine Create(int controller, int seed)
    {
        var basis = Catalog.DeckAt(0);
        L12PresetDeckDefinition Deck(string master) => new()
        {
            Name = "迭具土进攻候选合成牌组",
            MasterId = master,
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };
        var decks = new[] { Deck("S01-02M3"), Deck("S01-02M3") };
        decks[controller] = Deck("ST04-M1");
        var game = new L12GameEngine(Catalog, "kagutsuchi-attack-candidate-factory", "LOCAL", seed,
            ["合成甲", "合成乙"], decks, skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = controller;
        game.State.FirstPlayer = controller;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        game.State.DisasterDeck.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
        }
        game.State.Revision++;
        return game;
    }

    internal static L12CardInstance Card(string cardId, string instanceId, int owner, int? cost = null)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            CardId = definition.Id,
            InstanceId = instanceId,
            OwnerIndex = owner,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = cost ?? definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            SummonRound = -1,
        };
    }

    internal static L12MoraleCard Morale(string instanceId)
        => new() { CardId = "S01-04C1", InstanceId = instanceId };

    internal static L12Prompt OnlyPrompt(L12GameEngine game)
        => Assert.Single(game.State.PendingPrompts);

    internal static void Resolve(L12GameEngine game, string choice)
    {
        var prompt = OnlyPrompt(game);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    internal static void ResolveMany(L12GameEngine game, IEnumerable<string> choices)
    {
        var prompt = OnlyPrompt(game);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: choices.ToList()));
        Assert.True(result.Accepted, result.Error);
    }

    internal static L12GameEngine RestoreV2(L12GameEngine game)
    {
        Assert.Equal(2, game.State.StateFormatVersion);
        return L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
    }

    internal static IReadOnlyList<L12TriggerCandidate> CurrentCandidates(L12GameEngine game)
        => game.State.PendingTriggerBatches.SelectMany(batch => batch.Candidates)
            .Concat(game.State.PendingTriggerStackCandidates).ToArray();

    internal static (string Master, string Printed) TriggerChoiceIds(L12GameEngine game,
        L12Prompt order, string printedInstanceId)
    {
        var master = Assert.Single(order.ValidChoices,
            id => order.Data[$"sourceInstance:{id}"] == $"master-{order.PlayerIndex}");
        var printed = Assert.Single(order.ValidChoices,
            id => order.Data[$"sourceInstance:{id}"] == printedInstanceId);
        return (master, printed);
    }
}
