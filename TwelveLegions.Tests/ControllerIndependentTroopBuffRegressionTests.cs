using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ControllerIndependentTroopBuffRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly (string CardId, int Amount)[] Buffs =
    [
        ("S01-0301", 2000), ("S01-0311", 2000), ("S02-0509", 1000),
        ("S02-0517", 2000), ("S02-0519", 2000), ("S02-0606", 2000),
        ("ST05-09", 1000),
    ];

    public static IEnumerable<object[]> BothControllers()
        => Buffs.SelectMany(buff => new[] { 0, 1 }
            .Select(controller => new object[] { buff.CardId, buff.Amount, controller }));

    private static L12GameEngine Create(int controller, bool autoPass = false)
    {
        var game = new L12GameEngine(Catalog, "controller-independent-buff", "COST-R1", 93001 + controller,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: autoPass, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = controller;
        game.State.FirstPlayer = controller;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Library.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.Hp = 8;
        }
        return game;
    }

    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var definition = Catalog.Cards[id];
        return new L12CardInstance
        {
            InstanceId = instance, CardId = id, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction, ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0, HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect, Traits = [.. definition.Traits],
            Profession = definition.Profession, EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0, TrialValue = definition.TrialValue ?? 0,
            OwnerIndex = owner, SummonRound = -1,
        };
    }

    private static void Queue(L12GameEngine game, int controller, L12CardInstance source)
        => typeof(L12GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method => method.Name == "QueueOrPushTriggeredEffect" && method.GetParameters().Length == 6)
            .Invoke(game, [controller, source, "attack", "控制者独立兵力回归", null, new Dictionary<string, string>()]);

    private static L12Command Choose(L12GameEngine game, string? choice = null, params string[] cards)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var command = new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice,
            CardInstanceIds: cards.Length == 0 ? null : cards.ToList());
        var result = game.Handle(prompt.PlayerIndex, command);
        Assert.True(result.Accepted, result.Error);
        return command;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Choose(game, "pass");
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    private sealed record Payment(int Hp, string Hand, string Grave, string Library, string Morale);
    private static Payment PaidState(L12PlayerState player) => new(player.Hp,
        string.Join('|', player.Hand.Select(card => card.InstanceId)),
        string.Join('|', player.Graveyard.Select(card => card.InstanceId)),
        string.Join('|', player.Library.Select(card => card.InstanceId)),
        string.Join('|', player.Morale.Select(card => $"{card.InstanceId}:{card.Tapped}:{card.IsGodPower}")));

    private sealed record Fixture(L12GameEngine Game, L12CardInstance Source, int Controller,
        L12SimpleSelfTroopBuffTriggerSpec? Spec, L12CardInstance[] CostCards, L12MoraleCard? Power);

    private static Fixture Prepare(string cardId, int controller, bool preserveExistingCost = false)
    {
        var game = Create(controller);
        var source = Card(cardId, $"source-{controller}-{cardId}", controller);
        var player = game.State.Players[controller];
        player.Field[0][0] = source;
        if (preserveExistingCost)
        {
            source.TimedModifiers.Add(new L12TimedModifier
            { TroopsDelta = 0, CostDelta = 2, ExpiresAfterTurn = 17, Source = "existing-independent-cost" });
            source.CostModifier = 2;
        }
        var spec = L12SimpleSelfTroopBuffTriggerEffects.Find(cardId, "attack");
        L12CardInstance[] costCards = [];
        L12MoraleCard? power = null;
        switch (spec?.CostKind)
        {
            case "grave-bottom-two":
                costCards = [Card("S01-0001", "grave-a", controller), Card("S01-0002", "grave-b", controller)];
                player.Graveyard.AddRange(costCards);
                break;
            case "show-hand-tactic":
                costCards = [Card("S01-0005", "shown-tactic", controller)];
                player.Hand.AddRange(costCards);
                break;
            case "discard-hand":
                costCards = [Card("S01-0001", "discard-cost", controller)];
                player.Hand.AddRange(costCards);
                break;
            case "god-power":
                power = new L12MoraleCard { CardId = "S02-05C1", InstanceId = "power-cost", IsGodPower = true };
                player.Morale.Add(power);
                break;
        }
        Queue(game, controller, source);
        return new Fixture(game, source, controller, spec, costCards, power);
    }

    private static L12Command? Pay(Fixture fixture)
    {
        if (fixture.Spec is null) return null; // Medusa has no optional paid declaration.
        var game = fixture.Game;
        var command = Choose(game, "mode:use");
        if (fixture.CostCards.Length > 0)
            command = Choose(game, null, fixture.CostCards.Select(card => card.InstanceId).ToArray());
        else if (fixture.Power is not null)
            command = Choose(game, null, fixture.Power.InstanceId);

        var player = game.State.Players[fixture.Controller];
        switch (fixture.Spec.CostKind)
        {
            case "master-damage": Assert.Equal(7, player.Hp); break;
            case "grave-bottom-two":
                Assert.Empty(player.Graveyard);
                Assert.Equal(fixture.CostCards.Select(card => card.InstanceId), player.Library.Select(card => card.InstanceId));
                break;
            case "show-hand-tactic": Assert.Contains(fixture.CostCards[0], player.Hand); break;
            case "discard-hand":
                Assert.Empty(player.Hand);
                Assert.Contains(fixture.CostCards[0], player.Graveyard);
                break;
            case "god-power":
                Assert.True(fixture.Power!.Tapped);
                Assert.False(fixture.Power.IsGodPower);
                break;
        }
        return command;
    }

    [Fact]
    public void SevenRegisteredVerifiedTroopAddProgramsAreCoveredWithoutCardSpecificRuntimeFixes()
    {
        var actual = L12VerifiedAtomicPrograms.All
            .Where(program => program.Atoms.Any(atom => atom.Kind == L12AtomKinds.ModifyTroops
                && atom.Parameters.GetValueOrDefault("operation") == "add"))
            .Select(program => (program.CardId, program.Trigger)).OrderBy(item => item.CardId).ToArray();
        Assert.Equal(Buffs.Select(buff => (buff.CardId, "attack")).OrderBy(item => item.CardId).ToArray(), actual);
    }

    [Theory]
    [MemberData(nameof(BothControllers))]
    public void SevenTroopBuffsPreserveCostAndExpireForEitherController(string cardId, int amount, int controller)
    {
        var fixture = Prepare(cardId, controller, preserveExistingCost: true);
        var source = fixture.Source;
        var beforeCost = source.CurrentCost;
        var beforeCostModifier = source.CostModifier;
        var paymentCommand = Pay(fixture);
        var paid = PaidState(fixture.Game.State.Players[controller]);
        if (paymentCommand is not null)
        {
            Assert.False(fixture.Game.Handle(controller, paymentCommand).Accepted);
            Assert.Equal(paid, PaidState(fixture.Game.State.Players[controller]));
        }
        PassResponses(fixture.Game);

        var added = Assert.Single(source.TimedModifiers, modifier => modifier.Source == source.Name);
        Assert.Equal(amount, added.TroopsDelta);
        Assert.Equal(0, added.CostDelta);
        Assert.Equal(fixture.Game.State.TurnSerial, added.ExpiresAfterTurn);
        Assert.Equal(source.BaseTroops + amount, source.Troops);
        Assert.Equal(beforeCostModifier, source.CostModifier);
        Assert.Equal(beforeCost, source.CurrentCost);
        Assert.Equal(paid, PaidState(fixture.Game.State.Players[controller]));
        if (cardId == "ST05-09") Assert.True(source.HasShock);

        L12DerivedStats.ResetForCompletedTurn(source, fixture.Game.State.TurnSerial);
        Assert.DoesNotContain(source.TimedModifiers, modifier => modifier.Source == source.Name);
        Assert.Single(source.TimedModifiers, modifier => modifier.Source == "existing-independent-cost");
        Assert.Equal(source.BaseTroops, source.Troops);
        Assert.Equal(beforeCostModifier, source.CostModifier);
        Assert.Equal(beforeCost, source.CurrentCost);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CancellingUnpaidPercivalCostLeavesResourcesAndBothModifiersUnchanged(int controller)
    {
        var fixture = Prepare("S02-0606", controller);
        var before = PaidState(fixture.Game.State.Players[controller]);
        Choose(fixture.Game, "mode:use");
        Choose(fixture.Game, "skip");
        Assert.Equal(before, PaidState(fixture.Game.State.Players[controller]));
        Assert.Empty(fixture.Game.State.EffectStack);
        Assert.Empty(fixture.Source.TimedModifiers);
        Assert.Equal(0, fixture.Source.CostModifier);
        Assert.Equal(fixture.Source.Cost, fixture.Source.CurrentCost);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void NegationOrSourceDepartureKeepsPrepaidCostWithoutAddingAnyModifier(int controller, bool sourceLeaves)
    {
        var fixture = Prepare(sourceLeaves ? "S02-0606" : "S02-0519", controller);
        Pay(fixture);
        var player = fixture.Game.State.Players[controller];
        if (sourceLeaves)
        {
            player.Field[0][0] = null;
            player.Graveyard.Add(fixture.Source);
        }
        else Assert.Single(fixture.Game.State.EffectStack).Negated = true;
        var paid = PaidState(player);
        PassResponses(fixture.Game);
        Assert.Equal(paid, PaidState(player));
        Assert.Empty(fixture.Source.TimedModifiers);
        Assert.Equal(0, fixture.Source.CostModifier);
        Assert.Equal(fixture.Source.Cost, fixture.Source.CurrentCost);
        Assert.Contains(fixture.Game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == (sourceLeaves ? "failed" : "negated"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MedusaRealAttackKeepsShockDamageAndCombatCleanupWithoutChangingCost(int controller)
    {
        var game = Create(controller, autoPass: true);
        var source = Card("ST05-09", "medusa", controller);
        var enemy = game.State.Players[1 - controller];
        var left = Card("ST01-01", "left", 1 - controller);
        var primary = Card("ST01-01", "primary", 1 - controller);
        var right = Card("ST01-01", "right", 1 - controller);
        primary.Troops = 9000;
        game.State.Players[controller].Field[0][0] = source;
        enemy.Field[0] = [left, primary, right];
        var cost = source.CurrentCost;
        var result = game.Handle(controller, new L12Command("attack", source.InstanceId,
            Target: new L12AttackTarget("legion", primary.InstanceId)));
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(left.BaseTroops - 2000, left.Troops);
        Assert.Equal(right.BaseTroops - 2000, right.Troops);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("震击使进攻目标左右相邻军团", StringComparison.Ordinal));
        Assert.False(source.HasShock);
        Assert.Null(game.State.PendingDefense);
        Assert.Equal(0, source.CostModifier);
        Assert.Equal(cost, source.CurrentCost);
        Assert.All(source.TimedModifiers, modifier => Assert.Equal(0, modifier.CostDelta));
    }
}
