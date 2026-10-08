using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

internal sealed record KondoLethalScenario(
    L12GameEngine Game,
    string OkitaId,
    string KondoId,
    string ProbeAttackerId,
    string IndependentAttackerId);

internal static class KondoPersistentLethalReplacementFixture
{
    private static L12Catalog Catalog { get; } =
        L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    internal static KondoLethalScenario BeginMerlinLethal(bool splitLayersForOrderProbe = false)
    {
        var game = Create(202610087);
        var merlin = Card("S02-0603", "lethal-merlin", 0);
        var probeAttacker = Card("S01-0001", "post-replacement-probe-attacker", 0);
        var independentAttacker = Card("S01-0002", "later-independent-attacker", 0, troops: 4000);
        var okita = Card("S02-0403", "protected-okita", 1);
        var kondo = Card("ST04-05", "replacement-kondo", 1);
        game.State.Players[0].Field[0][0] = merlin;
        game.State.Players[0].Field[0][1] = independentAttacker;
        game.State.Players[0].Field[0][2] = probeAttacker;
        game.State.Players[0].SpecialZones.Runes = 1;
        game.State.Players[1].Field[0][0] = okita;
        game.State.Players[1].Field[0][1] = kondo;

        var activation = game.Handle(0,
            new L12Command("activateAbility", merlin.InstanceId, Ability: "merlinRune"));
        Assert.True(activation.Accepted, activation.Error);
        Resolve(game, "mode:debuff");
        Resolve(game, okita.InstanceId);
        PassResponses(game);
        var replacement = Prompt(game);
        Assert.Equal("effect-lethal-replacement", replacement.Continuation);
        Assert.Contains(kondo.InstanceId, replacement.ValidChoices);
        Assert.Equal(0, okita.Troops);

        // The default keeps the real Merlin effect untouched.  A separate
        // adversarial variant splits its exact total only to test canonical
        // collection ordering; it must not replace the reported real path.
        if (splitLayersForOrderProbe)
        {
            var merlinModifier = Assert.Single(okita.TimedModifiers);
            Assert.Equal(-3000, merlinModifier.TroopsDelta);
            okita.TimedModifiers.Clear();
            okita.TimedModifiers.Add(new L12TimedModifier
            {
                TroopsDelta = -2000,
                CostDelta = 0,
                ConsumedTroopsBonus = 0,
                ExpiresAfterTurn = merlinModifier.ExpiresAfterTurn,
                Source = "merlin-layer-b",
            });
            okita.TimedModifiers.Add(new L12TimedModifier
            {
                TroopsDelta = -1000,
                CostDelta = 0,
                ConsumedTroopsBonus = 0,
                ExpiresAfterTurn = merlinModifier.ExpiresAfterTurn,
                Source = "merlin-layer-a",
            });
        }
        return new(game, okita.InstanceId, kondo.InstanceId,
            probeAttacker.InstanceId, independentAttacker.InstanceId);
    }

    internal static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0),
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);

    internal static L12CardInstance FieldCard(L12GameEngine game, int playerIndex, string instanceId)
        => Assert.Single(game.State.Players[playerIndex].Field.SelectMany(row => row),
            card => card?.InstanceId == instanceId)!;

    internal static L12Prompt Prompt(L12GameEngine game)
        => Assert.Single(game.State.PendingPrompts);

    internal static void Resolve(L12GameEngine game, string choice)
    {
        var prompt = Prompt(game);
        Assert.Contains(choice, prompt.ValidChoices);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    internal static void PassResponses(L12GameEngine game, int limit = 80)
    {
        for (var count = 0; count < limit
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; count++)
            Resolve(game, "pass");
        Assert.NotEqual("response", game.State.PendingPrompts.FirstOrDefault()?.Kind);
    }

    internal static void CompleteAttack(L12GameEngine game)
    {
        PassResponses(game);
        if (game.State.PendingDefense is not { Stage: L12CombatStage.DefenseChoice } pending) return;
        var defender = 1 - pending.AttackerPlayer;
        var result = game.Handle(defender,
            new L12Command("resolveDefense", CardInstanceIds: []));
        Assert.True(result.Accepted, result.Error);
        PassResponses(game);
    }

    internal static bool MoveToHand(L12GameEngine game, int playerIndex, L12CardInstance card)
    {
        var method = typeof(L12GameEngine).GetMethod("MoveFieldCardToZone",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method.Invoke(game,
            [game.State.Players[playerIndex], card, "hand", "测试移入私有区域", false]));
    }

    internal static string[] ProtectionKeys(L12GameEngine game, int playerIndex, string instanceId)
        => game.State.Players[playerIndex].UsedAbilities
            .Where(key => key.StartsWith($"lethal-event-protected:{instanceId}:",
                StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

    private static L12GameEngine Create(int seed)
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
        var game = new L12GameEngine(Catalog, "kondo-lethal-replacement", "KONDO", seed,
            ["甲", "乙"], [Deck("甲测试牌库", "S02-06M1"), Deck("乙测试牌库", "S02-04M1")],
            skipPreparation: true, autoPassEmptyResponses: false,
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

    private static L12CardInstance Card(string cardId, string instanceId, int owner, int? troops = null)
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
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            OwnerIndex = owner,
            SummonRound = -1,
        };
    }
}
