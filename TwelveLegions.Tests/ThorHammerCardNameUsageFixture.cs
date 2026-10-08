using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

internal static class ThorHammerCardNameUsageFixture
{
    internal const string HammerCardId = "S02-0301";
    internal const string HammerAbility = "thorHammerRevive";
    internal const string SharedUsageKey = "card-name:S02-0301";

    internal static readonly L12Catalog Catalog = L12Catalog.Load(
        Path.Combine(AppContext.BaseDirectory, "Data"));

    internal static L12GameEngine Create(int seed = 26100801,
        string firstMaster = "S02-03M1", string secondMaster = "S02-03M1")
    {
        var basis = Catalog.DeckAt(0);
        L12PresetDeckDefinition Deck(string name, string masterId) => new()
        {
            Name = name,
            MasterId = masterId,
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [],
        };

        var game = new L12GameEngine(Catalog, "thor-hammer-card-name", "HAMMER-NAME", seed,
            ["甲", "乙"], [Deck("甲方雷神之锤次数夹具", firstMaster), Deck("乙方雷神之锤次数夹具", secondMaster)],
            skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Resolving.Clear();
            player.ExtraRelics.Clear();
            player.Relic = null;
            player.UsedAbilities.Clear();
        }
        return game;
    }

    internal static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
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
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            SummonRound = -1,
            OwnerIndex = owner,
        };
    }

    internal static (L12CardInstance First, L12CardInstance Second, L12CardInstance[] Costs)
        SeedPair(L12GameEngine game, int playerIndex = 0, string prefix = "hammer", int costCount = 12)
    {
        var player = game.State.Players[playerIndex];
        var first = Card(HammerCardId, $"{prefix}-first", playerIndex);
        var second = Card(HammerCardId, $"{prefix}-second", playerIndex);
        var costs = Enumerable.Range(0, costCount)
            .Select(index => Card("S02-0001", $"{prefix}-cost-{index}", playerIndex)).ToArray();
        player.Graveyard.AddRange([first, second, .. costs]);
        return (first, second, costs);
    }

    internal static L12Prompt Begin(L12GameEngine game, int playerIndex, L12CardInstance hammer)
    {
        var result = game.Handle(playerIndex,
            new L12Command("activateAbility", hammer.InstanceId, Ability: HammerAbility));
        Assert.True(result.Accepted, result.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", prompt.Continuation);
        Assert.Equal("order", prompt.Kind);
        return prompt;
    }

    internal static (string Slot, string OrderPromptId, string SlotPromptId) Commit(
        L12GameEngine game, int playerIndex, L12CardInstance hammer,
        IReadOnlyList<L12CardInstance> orderedCosts, string? slot = null)
    {
        var orderPrompt = Begin(game, playerIndex, hammer);
        ResolveMany(game, orderPrompt, orderedCosts.Select(card => card.InstanceId).ToArray());
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("slot", slotPrompt.Kind);
        var selectedSlot = slot ?? slotPrompt.ValidChoices[0];
        Assert.Contains(selectedSlot, slotPrompt.ValidChoices);
        ResolveChoice(game, slotPrompt, selectedSlot);
        Assert.Single(game.State.EffectStack);
        return (selectedSlot, orderPrompt.PromptId, slotPrompt.PromptId);
    }

    internal static void ResolveChoice(L12GameEngine game, L12Prompt prompt, string choice)
    {
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    internal static void ResolveMany(L12GameEngine game, L12Prompt prompt, params string[] choices)
    {
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: choices.ToList()));
        Assert.True(result.Accepted, result.Error);
    }

    internal static void Cancel(L12GameEngine game, L12Prompt prompt)
        => ResolveChoice(game, prompt, "skip");

    internal static void PassResponses(L12GameEngine game)
    {
        for (var guard = 0; guard < 60 && game.State.PendingPrompts.FirstOrDefault() is { } prompt; guard++)
        {
            Assert.Equal("response", prompt.Kind);
            ResolveChoice(game, prompt, "pass");
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    internal static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

    internal static L12AbilityView HammerView(L12GameEngine game, int playerIndex, L12CardInstance hammer)
    {
        var views = (List<L12AbilityView>)Invoke(game, "BuildAbilityViews",
            game.State.Players[playerIndex], hammer.CardId, hammer.InstanceId)!;
        return Assert.Single(views, view => view.Id == HammerAbility);
    }

    internal static void AssertHammerBlocked(L12GameEngine game, int playerIndex, L12CardInstance hammer)
    {
        var view = HammerView(game, playerIndex, hammer);
        Assert.False(view.Enabled);
        Assert.Equal("该效果本回合已经发动", view.DisabledReason);
        var before = game.SerializeFullState();
        var result = game.Handle(playerIndex,
            new L12Command("activateAbility", hammer.InstanceId, Ability: HammerAbility));
        Assert.False(result.Accepted);
        Assert.Equal("该效果本回合已经发动", result.Error);
        Assert.Equal(before, game.SerializeFullState());
    }

    internal static void AssertHammerCanBegin(L12GameEngine game, int playerIndex, L12CardInstance hammer)
    {
        Assert.True(HammerView(game, playerIndex, hammer).Enabled);
        Cancel(game, Begin(game, playerIndex, hammer));
    }

    internal static void MoveFieldCardToPrivateZone(L12GameEngine game, int playerIndex,
        L12CardInstance card, string destination)
    {
        var moved = (bool)Invoke(game, "MoveFieldCardToZone", game.State.Players[playerIndex], card,
            destination, "雷神之锤卡名次数离区回归", false)!;
        Assert.True(moved);
    }

    internal static void AddMorale(L12PlayerState player, int count, string prefix)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"{prefix}-{index}",
                CardId = "S01-03C1",
                Tapped = false,
            });
    }

    internal static void DiscardHammerThroughRealDesertDominion(
        L12GameEngine game, L12CardInstance hammer)
    {
        var player = game.State.Players[0];
        var dummy = Card("S02-0002", "hammer-desert-dummy");
        var tactic = Card("S02-0207", "hammer-desert-tactic");
        var summon = Card("S02-0202", "hammer-desert-summon");
        var hammerSlot = FieldSlot(player, hammer);
        var dummySlot = hammerSlot == "0:0" ? "0:1" : "0:0";
        var (dummyRow, dummyColumn) = ParseSlot(dummySlot);
        player.Field[dummyRow][dummyColumn] = dummy;
        player.Hand.AddRange([tactic, summon]);
        AddMorale(player, 4, "hammer-desert-morale");

        var play = game.Handle(0, new L12Command("playCard", tactic.InstanceId));
        Assert.True(play.Accepted, play.Error);
        ResolveMany(game, Assert.Single(game.State.PendingPrompts), hammer.InstanceId, dummy.InstanceId);
        ResolveChoice(game, Assert.Single(game.State.PendingPrompts), summon.InstanceId);
        var slot = Assert.Single(game.State.PendingPrompts);
        ResolveChoice(game, slot, slot.ValidChoices[0]);
        Assert.Contains(hammer, player.Graveyard);
        PassResponses(game);
    }

    internal static string FieldSlot(L12PlayerState player, L12CardInstance card)
    {
        for (var row = 0; row < player.Field.Length; row++)
        for (var slot = 0; slot < player.Field[row].Length; slot++)
            if (player.Field[row][slot]?.InstanceId == card.InstanceId) return $"{row}:{slot}";
        throw new Xunit.Sdk.XunitException($"找不到场上实例 {card.InstanceId}");
    }

    private static (int Row, int Slot) ParseSlot(string value)
    {
        var parts = value.Split(':');
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }

    private static object? Invoke(L12GameEngine game, string method, params object?[] arguments)
        => typeof(L12GameEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, arguments);
}
