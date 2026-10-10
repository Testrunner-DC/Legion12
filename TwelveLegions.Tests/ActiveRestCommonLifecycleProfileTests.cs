using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ActiveRestCommonLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence("S01-0105:ability:active:0e81cd47a6221fd8", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-0109:ability:active:88c64e7a7e50fb25", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-0117:ability:active:ba48403c4da1e24c", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-01D1:ability:active:32505e4556bad1b8", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-0214:ability:active:30e47404439f2371", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-0215:ability:active:6984859bdd4fa8b1", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-0317:ability:active:90c21e26f3d58b69", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-03D1:ability:active:4260db0837113c77", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S01-04D1:ability:active:1dcb5503b8cd59a8", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0003:ability:active:484fb98a6af8df3f", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0104:ability:active:1687d445c6acc308", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0204:ability:active:4257a82eec559a94", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0205:ability:active:8023ed21f8771697", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0404:ability:active:b30de444d37a3b6e", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0510:ability:active:2ee4c7f29b568e48", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0513:ability:active:0b4d5245336709f8", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0520:ability:active:e4e320d416a9c103", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-05D1:ability:active:f160e84288ecb28c", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0603:ability:active:8768d3f1fcb44728", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-0616:ability:active:3616b237df312569", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("S02-06D1:ability:active:30a9d18991dc8481", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("ST02-05:ability:active:80aa98cc24ef764e", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("ST03-05:ability:active:87d142bd0e12a218", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("ST03-07:ability:active:0d4ebc1a2ab8b128", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("ST04-06:ability:active:8f6b1b9dfc246e36", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("ST05-06:ability:active:cc5d71f55d3a253f", "active-rest-cost", "runtime-branch-mapping")]
    [L12AbilityEvidence("ST06-09:ability:active:e533dbf15f08cea0", "active-rest-cost", "runtime-branch-mapping")]
    public void EveryPrintedActiveRestSegmentUsesTheSharedCostBoundary()
    {
        var activeRest = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "active"
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.RestSource && atom.Stage == "cost"))
            .ToArray();
        Assert.Equal(27, activeRest.Length);
        Assert.Equal(EffectLifecycleProfiles.ActiveRestAbilityIds.Order(),
            activeRest.Select(ability => ability.AbilityId).Order());
        Assert.All(activeRest, ability =>
        {
            Assert.Contains("主动休整", ability.Text);
            Assert.Contains(ability.Atoms,
                atom => atom.Kind == L12AtomKinds.RestSource && atom.Stage == "cost");
        });
    }

    [Fact]
    [L12AbilityEvidence("S01-0105:ability:active:0e81cd47a6221fd8", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S01-0109:ability:active:88c64e7a7e50fb25", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S01-0117:ability:active:ba48403c4da1e24c", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S01-01D1:ability:active:32505e4556bad1b8", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S01-0214:ability:active:30e47404439f2371", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S01-0215:ability:active:6984859bdd4fa8b1", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S01-0317:ability:active:90c21e26f3d58b69", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S01-03D1:ability:active:4260db0837113c77", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S01-04D1:ability:active:1dcb5503b8cd59a8", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S02-0003:ability:active:484fb98a6af8df3f", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S02-0104:ability:active:1687d445c6acc308", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S02-0204:ability:active:4257a82eec559a94", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S02-0205:ability:active:8023ed21f8771697", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S02-0404:ability:active:b30de444d37a3b6e", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S02-0510:ability:active:2ee4c7f29b568e48", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S02-0513:ability:active:0b4d5245336709f8", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S02-0520:ability:active:e4e320d416a9c103", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S02-05D1:ability:active:f160e84288ecb28c", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S02-0603:ability:active:8768d3f1fcb44728", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S02-0616:ability:active:3616b237df312569", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("S02-06D1:ability:active:30a9d18991dc8481", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("ST02-05:ability:active:80aa98cc24ef764e", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("ST03-05:ability:active:87d142bd0e12a218", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("ST03-07:ability:active:0d4ebc1a2ab8b128", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("ST04-06:ability:active:8f6b1b9dfc246e36", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("ST05-06:ability:active:cc5d71f55d3a253f", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    [L12AbilityEvidence("ST06-09:ability:active:e533dbf15f08cea0", "normal", "negated", "duplicate-submit", "reconnect", "presentation-consumers", "paid-cost-preserved", "readied-source-reuse")]
    public void EveryRuntimeBranchCommitsOneSharedRestCostAndKeepsItAcrossNegationRestoreAndRetryGate()
    {
        var runtimeKeys = Assert.IsAssignableFrom<IEnumerable<string>>(
                typeof(L12StructuredCardRules).GetField("ActiveRestAbilities",
                    BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(31, runtimeKeys.Length);

        foreach (var runtimeKey in runtimeKeys)
        {
            var split = runtimeKey.Split('|');
            var cardId = split[0];
            var ability = split[1];
            Assert.True(L12StructuredCardRules.IsActiveRestAbility(cardId, ability));
            var game = Create(72700 + Array.IndexOf(runtimeKeys, runtimeKey));
            var source = Card(cardId, Catalog.Cards[cardId].CardType == "divinity"
                ? "master-0" : $"active-rest-{cardId}-{ability}");
            PlaceSource(game.State.Players[0], source);
            HoldResponse(game);

            var snapshot = Invoke(game, "CaptureActivePaidCostSnapshot", 0, source);
            typeof(L12GameEngine).GetField("_activePaidCostSnapshot",
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(game, snapshot);
            var item = Assert.IsType<L12StackItem>(Invoke(game, "PushEffect", 0, source,
                "active", "主动休整效果", null,
                new Dictionary<string, string> { ["ability"] = ability }));

            Assert.True(source.CardType == "divinity"
                ? game.State.Players[0].MasterTapped
                : source.Tapped, runtimeKey);
            Assert.Equal($"休整〈{source.Name}〉", item.Data["paidCostSummary"]);
            item.Negated = true;
            game = Restore(game);
            var restoredItem = Assert.Single(game.State.EffectStack);
            Assert.True(restoredItem.Negated);
            Assert.Equal($"休整〈{source.Name}〉", restoredItem.Data["paidCostSummary"]);

            var player = game.State.Players[0];
            var restoredSource = source.CardType switch
            {
                "legion" => player.Field.SelectMany(row => row).Single(card => card is not null)!,
                "artifact" => Assert.IsType<L12CardInstance>(player.Relic),
                _ => Card(cardId, "master-0"),
            };
            var stackCount = game.State.EffectStack.Count;
            var rejected = Assert.IsType<CommandResult>(Invoke(game, "BeginActiveAbilityWithSource",
                0, player, restoredSource, ability,
                new L12Command("activateAbility", restoredSource.InstanceId, Ability: ability)));
            Assert.False(rejected.Accepted, runtimeKey);
            Assert.Contains("必须为活跃状态", rejected.Error, StringComparison.Ordinal);
            Assert.Equal(stackCount, game.State.EffectStack.Count);
        }
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "active-rest-common", "ACTIVE-REST", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
        }
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        return game;
    }

    private static void PlaceSource(L12PlayerState player, L12CardInstance source)
    {
        if (source.CardType == "legion") player.Field[0][0] = source;
        else if (source.CardType == "artifact") player.Relic = source;
    }

    private static void HoldResponse(L12GameEngine game)
    {
        var response = Card("S01-0019", "active-rest-response");
        response.Hidden = true;
        response.SetRound = 0;
        game.State.Players[1].Field[1][2] = response;
    }

    private static object? Invoke(L12GameEngine game, string methodName, params object?[] args)
        => typeof(L12GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method => method.Name == methodName && method.GetParameters().Length == args.Length)
            .Invoke(game, args);

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

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
            HasPrintedCost = definition.Cost is not null,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = 0,
        };
    }
}
