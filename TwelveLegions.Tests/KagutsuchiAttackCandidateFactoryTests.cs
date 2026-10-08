using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class KagutsuchiAttackCandidateFactoryTests
{
    private static readonly (string CardId, string[] Plans)[] AttackPlanCases =
    [
        ("S01-0401", ["honda"]), ("S01-0104", ["hanxin"]), ("S01-0106", ["guanyu"]),
        ("S01-0203", ["menes"]), ("S01-0208", ["ay"]), ("S01-0306", ["olaf"]),
        ("S01-0402", ["nobunaga"]), ("S01-0405", ["miyamoto"]),
        ("S01-0406", ["hijikata"]), ("S01-0408", ["takasugi"]),
        ("S01-0413", ["hiromasa"]), ("S01-0416", ["inahime"]),
        ("S02-0103", ["pingyang"]), ("S02-0511", ["perot"]),
        ("S02-0605", ["bors"]), ("S02-0607", ["gawain"]),
        ("S02-0608", ["richard-defense", "richard-squires"]),
        ("S02-0612", ["scathach"]), ("S02-0617", ["robin-rune", "robin-draw"]),
        ("S01-0301", ["beowulf"]), ("S01-0311", ["gustav"]),
        ("S02-0509", ["odysseus"]), ("S02-0517", ["penthesilea"]),
        ("S02-0519", ["spartan"]), ("S02-0606", ["percival"]),
    ];

    public static IEnumerable<object[]> EveryAttackPlan()
        => AttackPlanCases.Select(item => new object[] { item.CardId, item.Plans });

    [Theory]
    [MemberData(nameof(EveryAttackPlan))]
    [Trait("L12Evidence", "entry:kagutsuchi-shared-attack-candidate-factory")]
    public void EveryAttackPublicPlanKeepsItsPlanMetadataWhenKagutsuchiAlsoTriggers(
        string cardId, string[] expectedPlans)
    {
        var game = KagutsuchiAttackCandidateFactoryFixture.Create(0, 202610810 + cardId[^1]);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var attacker = KagutsuchiAttackCandidateFactoryFixture.Card(cardId,
            $"factory-{cardId}", 0);
        player.Field[0][0] = attacker;
        opponent.Field[0][0] = KagutsuchiAttackCandidateFactoryFixture.Card("S01-0003",
            $"factory-target-{cardId}", 1);
        if (cardId == "S02-0608")
            attacker.AttachedCards.Add(KagutsuchiAttackCandidateFactoryFixture.Card("S02-0609",
                "factory-richard-squire", 0));
        if (cardId == "S02-0617")
            player.Field[0][1] = KagutsuchiAttackCandidateFactoryFixture.Card("S02-0608",
                "factory-robin-richard", 0);

        var attack = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", opponent.Field[0][0]!.InstanceId)));

        Assert.True(attack.Accepted, attack.Error);
        Assert.Equal("trigger-order", KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game).Kind);
        var candidates = KagutsuchiAttackCandidateFactoryFixture.CurrentCandidates(game)
            .Where(candidate => candidate.SourceInstanceId == attacker.InstanceId).ToArray();
        Assert.Equal(expectedPlans.Order(), candidates.Select(candidate => candidate.Data["attackPlan"]).Order());
        Assert.All(candidates, candidate =>
        {
            Assert.Equal("attack", candidate.Trigger);
            Assert.Equal(attacker.CardId, candidate.SourceSnapshot?.CardId);
            Assert.Equal(attacker.InstanceId, candidate.SourceSnapshot?.InstanceId);
        });
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [Trait("L12Bug", "BUG-20261007-1bada65d")]
    [Trait("L12Bug", "BUG-20261007-5d676498")]
    [Trait("L12Bug", "BUG-20261007-961ab290")]
    public void HondaAndKagutsuchiResolveCompletelyInEitherDeclaredOrder(int controller,
        bool kagutsuchiFirst)
    {
        var game = SetupHonda(controller, 202610840 + controller * 2 + (kagutsuchiFirst ? 0 : 1),
            out var hondaId, out var zeroId, out var positiveId, out var moraleId);
        var order = KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game);
        var hondaCandidate = Assert.Single(KagutsuchiAttackCandidateFactoryFixture.CurrentCandidates(game),
            candidate => candidate.SourceInstanceId == hondaId);
        Assert.Equal("honda", hondaCandidate.Data["attackPlan"]);
        var choices = KagutsuchiAttackCandidateFactoryFixture.TriggerChoiceIds(game, order, hondaId);
        KagutsuchiAttackCandidateFactoryFixture.ResolveMany(game, kagutsuchiFirst
            ? [choices.Printed, choices.Master]
            : [choices.Master, choices.Printed]);

        var sawHondaDebuffStack = ResolveHondaAndKagutsuchi(game, hondaId, zeroId, moraleId);
        var player = game.State.Players[controller];
        var opponent = game.State.Players[1 - controller];
        var honda = FindField(player, hondaId);
        var remainsPositive = FindField(opponent, positiveId);
        Assert.True(sawHondaDebuffStack);
        Assert.True(player.Morale.Single(card => card.InstanceId == moraleId).Tapped);
        Assert.Equal(honda.BaseTroops + 2000, honda.Troops);
        Assert.DoesNotContain(opponent.Field.SelectMany(row => row), card => card?.InstanceId == zeroId);
        Assert.Contains(opponent.Graveyard, card => card.InstanceId == zeroId);
        Assert.Equal(2, remainsPositive.CurrentCost);
    }

    [Fact]
    [Trait("L12Evidence", "entry:honda-no-late-target-empty-prompt")]
    public void HondaWithNoZeroCostTargetFinishesWithoutAnEmptyLateDeclaration()
    {
        var game = KagutsuchiAttackCandidateFactoryFixture.Create(0, 202610850);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var honda = KagutsuchiAttackCandidateFactoryFixture.Card("S01-0401", "no-target-honda", 0);
        var expensive = KagutsuchiAttackCandidateFactoryFixture.Card("S01-0003", "no-target-cost-three", 1,
            cost: 3);
        var morale = KagutsuchiAttackCandidateFactoryFixture.Morale("no-target-morale");
        player.Field[0][0] = honda;
        player.Morale.Add(morale);
        opponent.Field[0][0] = expensive;
        Assert.True(game.Handle(0, new L12Command("attack", honda.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        var order = KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game);
        var choices = KagutsuchiAttackCandidateFactoryFixture.TriggerChoiceIds(game, order, honda.InstanceId);
        KagutsuchiAttackCandidateFactoryFixture.ResolveMany(game, [choices.Master, choices.Printed]);

        for (var safety = 0; safety < 80 && game.State.PendingPrompts.Count > 0; safety++)
        {
            var prompt = KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game);
            if (prompt.Kind != "response") Assert.NotEmpty(prompt.ValidChoices);
            KagutsuchiAttackCandidateFactoryFixture.Resolve(game,
                prompt.Kind == "response" ? "pass"
                : prompt.ValidChoices.Contains("mode:morale") ? "mode:morale"
                : prompt.ValidChoices.Contains(morale.InstanceId) ? morale.InstanceId
                : prompt.ValidChoices.First());
        }

        Assert.Equal(2, expensive.CurrentCost);
        Assert.Empty(game.State.PendingActivations);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation" && prompt.ValidChoices.Count == 0);
    }

    [Fact]
    [Trait("L12Evidence", "entry:kagutsuchi-honda-order-v2")]
    public void HondaCandidateAndOrderRestoreFromV2BeforeEitherEffectDeclares()
    {
        var game = SetupHonda(0, 202610851, out var hondaId, out var zeroId, out _, out var moraleId);
        game = KagutsuchiAttackCandidateFactoryFixture.RestoreV2(game);
        var order = KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game);
        var candidate = Assert.Single(KagutsuchiAttackCandidateFactoryFixture.CurrentCandidates(game),
            item => item.SourceInstanceId == hondaId);
        Assert.Equal("honda", candidate.Data["attackPlan"]);
        var choices = KagutsuchiAttackCandidateFactoryFixture.TriggerChoiceIds(game, order, hondaId);
        KagutsuchiAttackCandidateFactoryFixture.ResolveMany(game, [choices.Master, choices.Printed]);

        Assert.True(ResolveHondaAndKagutsuchi(game, hondaId, zeroId, moraleId));
        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == zeroId);
    }

    [Fact]
    [Trait("L12Evidence", "entry:kagutsuchi-honda-composite-v2")]
    public void HondaLateTargetDeclarationRestoresFromV2BetweenItsTwoSegments()
    {
        var game = SetupHonda(0, 202610852, out var hondaId, out var zeroId, out _, out _);
        var order = KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game);
        var choices = KagutsuchiAttackCandidateFactoryFixture.TriggerChoiceIds(game, order, hondaId);
        KagutsuchiAttackCandidateFactoryFixture.ResolveMany(game, [choices.Master, choices.Printed]);
        while (game.State.Players[1].Field[0][0]!.CostModifier == 0)
        {
            var prompt = KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game);
            Assert.Equal("response", prompt.Kind);
            KagutsuchiAttackCandidateFactoryFixture.Resolve(game, "pass");
        }
        var lateTarget = KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game);
        Assert.Contains(zeroId, lateTarget.ValidChoices);

        game = KagutsuchiAttackCandidateFactoryFixture.RestoreV2(game);
        var restoredTarget = KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game);
        Assert.Equal(lateTarget.PromptId, restoredTarget.PromptId);
        Assert.Contains(zeroId, restoredTarget.ValidChoices);
        KagutsuchiAttackCandidateFactoryFixture.Resolve(game, zeroId);
        PassAllResponses(game);

        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == zeroId);
    }

    [Fact]
    [Trait("L12Evidence", "entry:kagutsuchi-richard-split-and-cost")]
    public void RichardKeepsBothCandidatesAndPrepaysTheSelectedSquireWhenKagutsuchiAlsoTriggers()
    {
        var game = KagutsuchiAttackCandidateFactoryFixture.Create(0, 202610853);
        var richard = KagutsuchiAttackCandidateFactoryFixture.Card("S02-0608", "factory-richard", 0);
        var squire = KagutsuchiAttackCandidateFactoryFixture.Card("S02-0609", "factory-squire", 0);
        richard.AttachedCards.Add(squire);
        game.State.Players[0].Field[0][0] = richard;
        Assert.True(game.Handle(0, new L12Command("attack", richard.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        var order = KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game);
        var candidates = KagutsuchiAttackCandidateFactoryFixture.CurrentCandidates(game)
            .Where(item => item.SourceInstanceId == richard.InstanceId).ToArray();
        Assert.Equal(["richard-defense", "richard-squires"],
            candidates.Select(item => item.Data["attackPlan"]).Order());
        var squires = Assert.Single(candidates, item => item.Data["attackPlan"] == "richard-squires");
        KagutsuchiAttackCandidateFactoryFixture.ResolveMany(game,
            order.ValidChoices.Where(id => id != squires.CandidateId).Append(squires.CandidateId));
        var mode = KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game);
        Assert.Equal(["mode:none", "mode:use"], mode.ValidChoices);
        KagutsuchiAttackCandidateFactoryFixture.Resolve(game, "mode:use");
        Assert.Contains(squire.InstanceId, KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game).ValidChoices);
        KagutsuchiAttackCandidateFactoryFixture.Resolve(game, squire.InstanceId);

        Assert.DoesNotContain(richard.AttachedCards, card => card.InstanceId == squire.InstanceId);
        Assert.Contains(game.State.Players[0].Graveyard, card => card.InstanceId == squire.InstanceId);
        Assert.Equal("richard-squires", Assert.Single(game.State.EffectStack).Data["attackPlan"]);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    [Trait("L12Evidence", "entry:kagutsuchi-robin-split")]
    public void RobinKeepsItsConditionalDrawCandidateAndChoiceWhenKagutsuchiAlsoTriggers(
        bool withRichard, int expectedOrderChoices)
    {
        var game = KagutsuchiAttackCandidateFactoryFixture.Create(0, 202610854 + (withRichard ? 1 : 0));
        var player = game.State.Players[0];
        var robin = KagutsuchiAttackCandidateFactoryFixture.Card("S02-0617", "factory-robin", 0);
        player.Field[0][0] = robin;
        player.Library.Add(KagutsuchiAttackCandidateFactoryFixture.Card("S01-0003", "factory-robin-draw", 0));
        if (withRichard)
            player.Field[0][1] = KagutsuchiAttackCandidateFactoryFixture.Card("S02-0608",
                "factory-robin-richard", 0);
        Assert.True(game.Handle(0, new L12Command("attack", robin.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        var order = KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game);
        Assert.Equal(expectedOrderChoices, order.ValidChoices.Count);
        var plans = KagutsuchiAttackCandidateFactoryFixture.CurrentCandidates(game)
            .Where(item => item.SourceInstanceId == robin.InstanceId).ToArray();
        Assert.Contains(plans, item => item.Data["attackPlan"] == "robin-rune"
            && item.Data["declaration-complete"] == "true");
        if (!withRichard)
        {
            Assert.DoesNotContain(plans, item => item.Data["attackPlan"] == "robin-draw");
            return;
        }
        var draw = Assert.Single(plans, item => item.Data["attackPlan"] == "robin-draw");
        KagutsuchiAttackCandidateFactoryFixture.ResolveMany(game,
            order.ValidChoices.Where(id => id != draw.CandidateId).Append(draw.CandidateId));
        Assert.Equal(["mode:none", "mode:use"],
            KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game).ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "entry:kagutsuchi-traditional-attack-fallback")]
    public void UnregisteredTraditionalAttackTriggerKeepsTheExistingFallbackCandidate()
    {
        var game = KagutsuchiAttackCandidateFactoryFixture.Create(0, 202610856);
        var hannibal = KagutsuchiAttackCandidateFactoryFixture.Card("S02-0516", "factory-hannibal", 0);
        game.State.Players[0].Field[0][0] = hannibal;
        Assert.True(game.Handle(0, new L12Command("attack", hannibal.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);

        var order = KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game);
        Assert.Equal("trigger-order", order.Kind);
        var candidate = Assert.Single(KagutsuchiAttackCandidateFactoryFixture.CurrentCandidates(game),
            item => item.SourceInstanceId == hannibal.InstanceId);
        Assert.False(candidate.Data.ContainsKey("attackPlan"));
        Assert.Equal(hannibal.CardId, candidate.SourceSnapshot?.CardId);
    }

    private static L12GameEngine SetupHonda(int controller, int seed, out string hondaId,
        out string zeroId, out string positiveId, out string moraleId)
    {
        var game = KagutsuchiAttackCandidateFactoryFixture.Create(controller, seed);
        var player = game.State.Players[controller];
        var opponent = game.State.Players[1 - controller];
        hondaId = $"factory-honda-{controller}";
        zeroId = $"factory-cost-one-{controller}";
        positiveId = $"factory-cost-three-{controller}";
        moraleId = $"factory-morale-{controller}";
        player.Field[0][0] = KagutsuchiAttackCandidateFactoryFixture.Card("S01-0401", hondaId, controller);
        opponent.Field[0][0] = KagutsuchiAttackCandidateFactoryFixture.Card("S01-0003", zeroId,
            1 - controller, cost: 1);
        opponent.Field[0][1] = KagutsuchiAttackCandidateFactoryFixture.Card("S01-0003", positiveId,
            1 - controller, cost: 3);
        player.Morale.Add(KagutsuchiAttackCandidateFactoryFixture.Morale(moraleId));
        var attack = game.Handle(controller, new L12Command("attack", hondaId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        Assert.Equal("trigger-order", KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game).Kind);
        return game;
    }

    private static bool ResolveHondaAndKagutsuchi(L12GameEngine game, string hondaId,
        string zeroId, string moraleId)
    {
        var sawHondaDebuff = false;
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.Count > 0; safety++)
        {
            var prompt = KagutsuchiAttackCandidateFactoryFixture.OnlyPrompt(game);
            var top = game.State.EffectStack.LastOrDefault();
            if (prompt.Kind == "response")
            {
                if (top?.SourceInstanceId == hondaId && !sawHondaDebuff)
                {
                    Assert.Equal("honda", top.Data["attackPlan"]);
                    Assert.Equal("honda-debuff", top.Data["atomicFlow"]);
                    sawHondaDebuff = true;
                }
                KagutsuchiAttackCandidateFactoryFixture.Resolve(game, "pass");
                continue;
            }
            if (prompt.ValidChoices.Contains("mode:morale"))
                KagutsuchiAttackCandidateFactoryFixture.Resolve(game, "mode:morale");
            else if (prompt.ValidChoices.Contains(moraleId))
                KagutsuchiAttackCandidateFactoryFixture.Resolve(game, moraleId);
            else if (prompt.ValidChoices.Contains(zeroId))
                KagutsuchiAttackCandidateFactoryFixture.Resolve(game, zeroId);
            else
                Assert.Fail($"未识别的迭具土/本多提示：{prompt.Kind}/{prompt.Continuation}/"
                    + string.Join(',', prompt.ValidChoices));
        }
        return sawHondaDebuff;
    }

    private static void PassAllResponses(L12GameEngine game)
    {
        for (var safety = 0;
             safety < 20 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response";
             safety++)
            KagutsuchiAttackCandidateFactoryFixture.Resolve(game, "pass");
    }

    private static L12CardInstance FindField(L12PlayerState player, string instanceId)
        => Assert.Single(player.Field.SelectMany(row => row), card => card?.InstanceId == instanceId)!;
}
