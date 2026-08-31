using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6HRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static readonly string[] ReviewedAttackCards =
    [
        "S01-0104", "S01-0106", "S01-0203", "S01-0208", "S01-0301", "S01-0306", "S01-0311",
        "S01-0402", "S01-0406", "S01-0408", "S02-0509", "S02-0511", "S02-0517", "S02-0519",
        "S02-0605", "S02-0606", "S02-0607", "S02-0608", "S02-0612", "S01-0405", "S01-0413",
        "S01-0416", "S02-0103", "S02-0617",
    ];

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "atomic-review-batch6h", "ATOMIC6H", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
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
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Resolving.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int troops = 0)
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
            BaseTroops = troops > 0 ? troops : definition.Troops ?? 0,
            Troops = troops > 0 ? troops : definition.Troops ?? 0,
            SummonRound = -1,
        };
    }

    private static L12MoraleCard Morale(string id, bool godPower = false)
        => new() { CardId = godPower ? "S02-05C1" : "S01-01C1", InstanceId = id, IsGodPower = godPower };

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static L12Prompt ResolveMany(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: choices.ToList()));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 80 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var prompt = game.State.PendingPrompts[0];
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static void AttackMaster(L12GameEngine game, L12CardInstance attacker)
    {
        game.State.Players[0].Field[0][0] = attacker;
        var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(result.Accepted, result.Error);
    }

    public static IEnumerable<object[]> ReviewedCards()
        => ReviewedAttackCards.Select(cardId => new object[] { cardId });

    [Theory]
    [MemberData(nameof(ReviewedCards))]
    [Trait("L12Evidence", "entry:attack-public-trigger-plan")]
    public void EveryReviewedAttackCardIsRegisteredInThePublicTriggerPlanner(string cardId)
    {
        var method = typeof(L12GameEngine).GetMethod("HasPublicTriggerDeclarationPlan",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = method.Invoke(null, [cardId, "attack", null]);
        Assert.True(Assert.IsType<bool>(result));
    }

    [Theory]
    [MemberData(nameof(ReviewedCards))]
    [Trait("L12Evidence", "entry:attack-runtime-public-declaration")]
    public void EveryReviewedAttackCardUsesTheRuntimeDeclarationRoute(string cardId)
    {
        var game = Create(7650 + Array.IndexOf(ReviewedAttackCards, cardId));
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var attacker = Card(cardId, $"batch6h-runtime-{cardId}");
        var helper = Card("S01-0401", $"batch6h-helper-{cardId}", 1000);
        var enemy = Card("S02-0609", $"batch6h-enemy-{cardId}", 1000);
        var counter = Card("S01-0019", $"batch6h-counter-{cardId}");
        counter.Hidden = true;
        player.Field[0][1] = helper;
        opponent.Field[0][0] = enemy;
        opponent.Field[1][1] = counter;
        player.Morale.Add(Morale($"batch6h-morale-{cardId}"));
        player.Morale.Add(Morale($"batch6h-power-{cardId}", godPower: true));
        player.SpecialZones.Runes = 3;
        player.Hand.Add(Card("S01-0001", $"batch6h-hand-{cardId}"));
        player.Hand.Add(Card("S01-0005", $"batch6h-tactic-{cardId}"));
        opponent.Hand.Add(Card("S01-0001", $"batch6h-opponent-hand-a-{cardId}"));
        opponent.Hand.Add(Card("S01-0002", $"batch6h-opponent-hand-b-{cardId}"));
        player.Graveyard.Add(Card("S01-0001", $"batch6h-grave-a-{cardId}"));
        player.Graveyard.Add(Card("S01-0002", $"batch6h-grave-b-{cardId}"));
        player.Library.Clear();
        player.Library.Add(Card("S01-0101", $"batch6h-library-{cardId}"));
        if (cardId == "S02-0608")
            attacker.AttachedCards.Add(Card("S02-0609", $"batch6h-squire-{cardId}"));
        if (cardId == "S02-0617")
            player.Field[1][0] = Card("S02-0608", $"batch6h-richard-{cardId}");

        AttackMaster(game, attacker);

        var prompt = Assert.Single(game.State.PendingPrompts);
        if (cardId is "S02-0608" or "S02-0617")
            Assert.Equal("trigger-order", prompt.Kind);
        else
            Assert.Equal("pending-activation", prompt.Continuation);
        Assert.DoesNotContain(game.State.PendingPrompts, pending => pending.Continuation == "card-effect");
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0104")]
    [Trait("L12Evidence", "entry:attack-colon-cost-pre-stack")]
    public void HanXinDeclaresAndReturnsSpecificMoraleBeforeItsAttackEffectEntersTheStack()
    {
        var game = Create(7601);
        var player = game.State.Players[0];
        var hanXin = Card("S01-0104", "batch6h-hanxin");
        var morale = Morale("batch6h-hanxin-morale");
        player.Morale.Add(morale);

        AttackMaster(game, hanXin);

        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", mode.Continuation);
        Assert.Empty(game.State.EffectStack);
        Resolve(game, "mode:use");
        var cost = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(morale.InstanceId, cost.ValidChoices);
        Resolve(game, morale.InstanceId);

        Assert.DoesNotContain(morale, player.Morale);
        Assert.Single(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0203")]
    [Trait("L12Evidence", "entry:attack-self-discard-colon-cost")]
    public void MenesMayDiscardItselfAsItsPrepaidCostWithoutDeathOrSourceBoundBuff()
    {
        var game = Create(7610);
        var player = game.State.Players[0];
        var menes = Card("S01-0203", "batch6h-menes-self-cost");

        AttackMaster(game, menes);

        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:use", mode.ValidChoices);
        Resolve(game, "mode:use");
        var cost = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(menes.InstanceId, cost.ValidChoices);
        Resolve(game, menes.InstanceId);

        Assert.DoesNotContain(menes, player.Field.SelectMany(row => row).OfType<L12CardInstance>());
        Assert.Contains(menes, player.Graveyard);
        Assert.Single(game.State.EffectStack);
        Assert.DoesNotContain(game.State.Events, entry => entry.Text.Contains("【阵亡时】", StringComparison.Ordinal));

        PassResponses(game);

        Assert.Null(game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Contains(menes, player.Graveyard);
        Assert.DoesNotContain(menes, player.Field.SelectMany(row => row).OfType<L12CardInstance>());
        Assert.Equal(menes.BaseTroops, menes.Troops);
        Assert.Empty(menes.TimedModifiers);
        Assert.False(menes.HasStrongAttack);
        Assert.Contains(game.State.Events, entry => entry.Type == "leave"
            && entry.Text.Contains("作为进攻效果费用弃置", StringComparison.Ordinal));
        Assert.Contains(game.State.Events, entry => entry.Type == "attack-aborted"
            && entry.Text.Contains("进攻军团已离场", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Text.Contains("【阵亡时】", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0408")]
    [Trait("L12Evidence", "entry:attack-target-invalid-no-refund")]
    public void TakasugiKeepsItsPrepaidCostWhenTheDeclaredTargetLeavesBeforeResolution()
    {
        var game = Create(7602);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var takasugi = Card("S01-0408", "batch6h-takasugi");
        var target = Card("S01-0201", "batch6h-takasugi-target");
        var morale = Morale("batch6h-takasugi-morale");
        player.Morale.Add(morale);
        enemy.Field[0][0] = target;

        AttackMaster(game, takasugi);
        Resolve(game, "mode:use");
        Resolve(game, morale.InstanceId);
        Resolve(game, target.InstanceId);
        Assert.True(morale.Tapped);

        enemy.Field[0][0] = null;
        enemy.Graveyard.Add(target);
        PassResponses(game);

        Assert.True(morale.Tapped);
        Assert.Equal(0, target.CostModifier);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-cancelled");
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0402")]
    [Trait("L12Evidence", "entry:attack-source-snapshot")]
    public void NobunagaGlobalEffectStillResolvesAfterItsSourceLeavesTheField()
    {
        var game = Create(7603);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var nobunaga = Card("S01-0402", "batch6h-nobunaga");
        var target = Card("S01-0201", "batch6h-nobunaga-target");
        var morale = Morale("batch6h-nobunaga-morale");
        player.Morale.Add(morale);
        enemy.Field[0][0] = target;

        AttackMaster(game, nobunaga);
        Resolve(game, "mode:use");
        Resolve(game, morale.InstanceId);
        player.Field[0][0] = null;
        player.Graveyard.Add(nobunaga);
        PassResponses(game);

        Assert.True(morale.Tapped);
        Assert.Equal(-1, target.CostModifier);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0103")]
    [Trait("L12Evidence", "entry:post-stack-hidden-reveal")]
    public void PingyangOptInIsDeclaredBeforeStackWithoutLeakingTheLibraryTop()
    {
        var game = Create(7604);
        var player = game.State.Players[0];
        var pingyang = Card("S02-0103", "batch6h-pingyang");
        var hiddenTop = Card("S01-0104", "batch6h-pingyang-top");
        player.Library.Clear();
        player.Library.Add(hiddenTop);

        AttackMaster(game, pingyang);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(hiddenTop.InstanceId, declaration.ValidChoices);
        Assert.DoesNotContain(hiddenTop.InstanceId, declaration.Data.Values);
        Assert.DoesNotContain(game.State.Events, entry => entry.Cards.Any(card => card.InstanceId == hiddenTop.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0607")]
    [Trait("L12Evidence", "entry:independent-attack-segments")]
    public void GawainSpendsAtLeastOneRuneThenQueuesASeparateRespondableBuffSegment()
    {
        var game = Create(7605);
        var player = game.State.Players[0];
        var gawain = Card("S02-0607", "batch6h-gawain");
        player.SpecialZones.Runes = 3;

        AttackMaster(game, gawain);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.DoesNotContain("rune-count:0", declaration.ValidChoices);
        Resolve(game, "rune-count:2");
        var spend = Assert.Single(game.State.EffectStack);
        Assert.Equal(3, player.SpecialZones.Runes);
        PassResponses(game);

        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.Equal(gawain.BaseTroops + 2000, gawain.Troops);
        Assert.Equal(3, game.State.PendingDefense?.MasterDamage);
        Assert.True(game.State.Events.Count(entry => entry.Type == "stack-push"
            && entry.Cards.Any(card => card.InstanceId == gawain.InstanceId)) >= 2);
        Assert.DoesNotContain(spend, game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0608")]
    [Trait("L12Evidence", "entry:same-time-independent-attack-candidates")]
    public void RichardCreatesSortableDefenseAndSquireCandidatesAndNegatingDefenseDisablesTheTax()
    {
        var game = Create(7606);
        var player = game.State.Players[0];
        var defender = game.State.Players[1];
        var richard = Card("S02-0608", "batch6h-richard");
        richard.AttachedCards.Add(Card("S02-0609", "batch6h-richard-squire"));
        var blocker = Card("S01-0104", "batch6h-richard-blocker", 9000);
        var extra = Card("S01-0001", "batch6h-richard-extra");
        defender.Hand.AddRange([blocker, extra]);

        AttackMaster(game, richard);
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-order", order.Kind);
        Assert.Equal(2, order.ValidChoices.Count);
        var defenseId = order.ValidChoices.Single(id => order.Data[id].Contains("抵挡", StringComparison.Ordinal));
        var squireId = order.ValidChoices.Single(id => id != defenseId);
        ResolveMany(game, defenseId, squireId);
        Resolve(game, "mode:none");

        var defenseStack = Assert.Single(game.State.EffectStack);
        defenseStack.Negated = true;
        PassResponses(game);
        Assert.True(game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: [blocker.InstanceId])).Accepted);
        PassResponses(game);

        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-richard-defense-extra-discard");
        Assert.Contains(extra, defender.Hand);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0617")]
    [Trait("L12Evidence", "entry:robin-independent-attack-segments")]
    public void RobinCreatesARequiredRuneCandidateAndASeparateOptionalDrawCandidate()
    {
        var game = Create(7607);
        var player = game.State.Players[0];
        var robin = Card("S02-0617", "batch6h-robin");
        player.Field[0][1] = Card("S02-0608", "batch6h-robin-richard");
        player.Library.Clear();
        player.Library.Add(Card("S01-0001", "batch6h-robin-draw"));

        AttackMaster(game, robin);

        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-order", order.Kind);
        Assert.Equal(2, order.ValidChoices.Count);
        Assert.Contains(order.ValidChoices, id => order.Data[id].Contains("符文", StringComparison.Ordinal));
        Assert.Contains(order.ValidChoices, id => order.Data[id].Contains("抽牌", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0403")]
    [Trait("L12Evidence", "entry:attack-hidden-control")]
    public void OkitaRemainsAResolutionTimeHiddenRevealControl()
    {
        var game = Create(7608);
        var player = game.State.Players[0];
        var okita = Card("S02-0403", "batch6h-okita-control");
        var hiddenTop = Card("S01-0401", "batch6h-okita-top");
        player.Library.Clear();
        player.Library.Add(hiddenTop);

        AttackMaster(game, okita);

        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        Assert.DoesNotContain(game.State.Events, entry => entry.Cards.Any(card => card.InstanceId == hiddenTop.InstanceId));
        Assert.Single(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0519")]
    public void SpartanWarriorUsesItsOwnModifierLabel()
    {
        var game = Create(7609);
        var player = game.State.Players[0];
        var spartan = Card("S02-0519", "batch6h-spartan");
        var power = Morale("batch6h-spartan-power", godPower: true);
        player.Morale.Add(power);

        AttackMaster(game, spartan);
        Resolve(game, "mode:use");
        Resolve(game, power.InstanceId);
        PassResponses(game);

        Assert.Contains(spartan.TimedModifiers, modifier => modifier.Source == "斯巴达勇士");
        Assert.DoesNotContain(spartan.TimedModifiers, modifier => modifier.Source == "阿喀琉斯");
    }
}
