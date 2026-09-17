using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SimpleSelfTroopBuffTriggerConsistencyTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> Specs()
        => L12SimpleSelfTroopBuffTriggerEffects.All.Select(spec => new object[] { spec.CardId });

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "simple-self-troop-buff", "SELF-BUFF", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
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
        }
        game.State.Players[0].Hp = 8;
        game.State.Players[1].Hp = 8;
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
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            SummonRound = -1,
            OwnerIndex = 0,
        };
    }

    private static L12MoraleCard GodPower(string instanceId)
        => new()
        {
            CardId = "S02-05C1",
            InstanceId = instanceId,
            IsGodPower = true,
            Tapped = false,
        };

    private static object? Invoke(object target, string method, params object?[] args)
    {
        var candidate = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(info => info.Name == method && info.GetParameters().Length == args.Length);
        return candidate.Invoke(target, args);
    }

    private static void Queue(L12GameEngine game, L12CardInstance source)
        => Invoke(game, "QueueOrPushTriggeredEffect", 0, source, "attack",
            "旧的逐卡进攻时文本", null, new Dictionary<string, string>());

    private static L12Prompt Choose(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static L12Prompt ChooseCards(L12GameEngine game, params string[] instanceIds)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: instanceIds.ToList()));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var prompt = game.State.PendingPrompts[0];
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private sealed record Fixture(
        L12GameEngine Game,
        L12CardInstance Source,
        L12SimpleSelfTroopBuffTriggerSpec Spec,
        L12Prompt ModePrompt,
        L12CardInstance[] CostCards,
        L12MoraleCard? GodPower);

    private static Fixture DeclareAndPay(string cardId, int seed)
    {
        var game = Create(seed);
        var spec = Assert.Single(L12SimpleSelfTroopBuffTriggerEffects.All,
            item => item.CardId == cardId);
        var player = game.State.Players[0];
        var source = Card(cardId, $"source-{cardId}");
        player.Field[0][0] = source;
        L12CardInstance[] costCards = [];
        L12MoraleCard? power = null;

        switch (spec.CostKind)
        {
            case "grave-bottom-two":
                costCards =
                [
                    Card("S01-0001", $"grave-a-{cardId}"),
                    Card("S01-0002", $"grave-b-{cardId}"),
                ];
                player.Graveyard.AddRange(costCards);
                break;
            case "show-hand-tactic":
                costCards = [Card("S01-0005", $"tactic-{cardId}")];
                player.Hand.Add(costCards[0]);
                break;
            case "god-power":
                power = GodPower($"power-{cardId}");
                player.Morale.Add(power);
                break;
            case "discard-hand":
                costCards = [Card("S01-0001", $"hand-{cardId}")];
                player.Hand.Add(costCards[0]);
                break;
        }

        Queue(game, source);
        var modePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(spec.PromptText, modePrompt.Text, StringComparison.Ordinal);
        Choose(game, "mode:use");

        switch (spec.CostKind)
        {
            case "grave-bottom-two":
                ChooseCards(game, costCards.Select(card => card.InstanceId).ToArray());
                break;
            case "show-hand-tactic":
            case "discard-hand":
                ChooseCards(game, costCards[0].InstanceId);
                break;
            case "god-power":
                ChooseCards(game, power!.InstanceId);
                break;
        }

        return new Fixture(game, source, spec, modePrompt, costCards, power);
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-self-troop-buff-trigger-spec")]
    public void ExactAttackBuffsOwnOneDefinitionProgramCostAndSettlementScene()
    {
        var expected = new[]
        {
            ("S01-0301", 3, "attack", "master-damage", 2000),
            ("S01-0311", 1, "attack", "grave-bottom-two", 2000),
            ("S02-0509", 3, "attack", "show-hand-tactic", 1000),
            ("S02-0517", 3, "attack", "god-power", 2000),
            ("S02-0519", 1, "attack", "god-power", 2000),
            ("S02-0606", 3, "attack", "discard-hand", 2000),
        };
        Assert.Equal(expected, L12SimpleSelfTroopBuffTriggerEffects.All.Select(spec =>
            (spec.CardId, spec.AbilitySequence, spec.Trigger, spec.CostKind, spec.Amount)));

        foreach (var spec in L12SimpleSelfTroopBuffTriggerEffects.All)
        {
            var ability = Catalog.AtomicEffects.Find(spec.CardId)!.Abilities
                .Single(item => item.Sequence == spec.AbilitySequence);
            var scene = Assert.Single(ability.Presentations,
                item => item.Flow == L12SingleSegmentTriggeredEffectPresentations.Flow);
            Assert.Equal(spec.SettlementText, scene.DefaultText);

            var program = Assert.IsType<L12VerifiedAtomicProgram>(
                L12VerifiedAtomicPrograms.Find(spec.CardId, spec.Trigger));
            Assert.Single(program.Atoms, atom => atom.Kind == L12AtomKinds.Optional);
            var cost = Assert.Single(program.Atoms, atom => atom.Stage == "cost");
            Assert.Equal("true", cost.Parameters.GetValueOrDefault("prepaid"));
            var modifier = Assert.Single(program.Atoms,
                atom => atom.Kind == L12AtomKinds.ModifyTroops);
            Assert.Equal(spec.Amount.ToString(), modifier.Parameters["value"]);
            Assert.Equal("source", modifier.Parameters["target"]);
            Assert.Single(program.Atoms, atom => atom.Kind == L12AtomKinds.Duration);
            Assert.Equal(spec.SettlementText, L12GameEngine.ResolveTriggeredEffectDisplayText(
                Card(spec.CardId, $"display-{spec.CardId}"), spec.Trigger, "旧文本"));
        }
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-self-troop-buff-full-pool")]
    public void FullPoolHasNoOtherPaidAttackWhoseOnlyResolutionIsASourceTroopIncrease()
    {
        var discovered = Catalog.AtomicEffects.All
            .SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "attack"
                && ability.Atoms.Any(atom => atom.Stage == "cost")
                && ability.Atoms.Count(atom => atom.Kind == L12AtomKinds.ModifyTroops
                    && atom.Parameters.GetValueOrDefault("operation") == "add") == 1
                && ability.Atoms.Where(atom => atom.Stage == "resolution")
                    .All(atom => atom.Kind == L12AtomKinds.ModifyTroops))
            .Select(ability => ability.CardId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(cardId => cardId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var registered = L12SimpleSelfTroopBuffTriggerEffects.All
            .Select(spec => spec.CardId)
            .OrderBy(cardId => cardId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(registered, discovered);
    }

    [Theory]
    [MemberData(nameof(Specs))]
    [Trait("L12Evidence", "entry:simple-self-troop-buff-positive")]
    public void EveryCostKindPaysOnceThenUsesTheSameSourceBuffSettlement(string cardId)
    {
        var fixture = DeclareAndPay(cardId, 11200 + cardId[^1]);
        var player = fixture.Game.State.Players[0];
        var hpAfterPayment = player.Hp;
        var handAfterPayment = player.Hand.Count;
        var graveAfterPayment = player.Graveyard.Count;
        var libraryAfterPayment = player.Library.Count;

        var response = Assert.Single(fixture.Game.State.PendingPrompts,
            prompt => prompt.Kind == "response");
        Assert.Contains("Cost（已支付）", response.Text, StringComparison.Ordinal);
        Assert.Contains("效果：", response.Text, StringComparison.Ordinal);
        Assert.Contains(fixture.Spec.SettlementText.Split('：')[^1].TrimEnd('。'),
            response.Text, StringComparison.Ordinal);
        PassResponses(fixture.Game);

        Assert.Equal(fixture.Spec.Amount,
            Assert.Single(fixture.Source.TimedModifiers,
                modifier => modifier.Source == fixture.Spec.Name).TroopsDelta);
        Assert.Equal(fixture.Source.BaseTroops + fixture.Spec.Amount, fixture.Source.Troops);
        Assert.Equal(hpAfterPayment, player.Hp);
        Assert.Equal(handAfterPayment, player.Hand.Count);
        Assert.Equal(graveAfterPayment, player.Graveyard.Count);
        Assert.Equal(libraryAfterPayment, player.Library.Count);
        if (fixture.GodPower is not null)
        {
            Assert.True(fixture.GodPower.Tapped);
            Assert.False(fixture.GodPower.IsGodPower);
        }
        Assert.Single(fixture.Game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == cardId));
    }

    [Theory]
    [MemberData(nameof(Specs))]
    [Trait("L12Evidence", "entry:simple-self-troop-buff-cannot-pay")]
    public void MissingPrintedCostNeverCreatesAnActivationOrStack(string cardId)
    {
        var game = Create(11300 + cardId[^1]);
        var source = Card(cardId, $"cannot-pay-{cardId}");
        game.State.Players[0].Field[0][0] = source;
        // 只检查费用候选，0血表示没有可支付的伤害额度；1血已不属于费用不足。
        // 真实1血发动并立即判败由LethalSelfDamageCostTests覆盖。
        game.State.Players[0].Hp = cardId == "S01-0301" ? 0 : 1;

        Queue(game, source);

        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(source.TimedModifiers);
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-self-troop-buff-no-source")]
    public void MissingBattlefieldSourceSilentlySkipsBeforeDeclaration()
    {
        var game = Create(11310);
        var source = Card("S02-0519", "missing-source");
        game.State.Players[0].Morale.Add(GodPower("unused-power"));

        Queue(game, source);

        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-self-troop-buff-cost-cancel")]
    public void InvalidCostChoiceKeepsACancellablePromptAndCancelLeavesEverythingUnpaid()
    {
        var game = Create(11311);
        var player = game.State.Players[0];
        var source = Card("S02-0606", "percival-cancel");
        var hand = Card("S01-0001", "percival-valid-cost");
        player.Field[0][0] = source;
        player.Hand.Add(hand);
        Queue(game, source);
        Choose(game, "mode:use");
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("取消整次发动", costPrompt.ChoiceLabels["skip"]);

        var invalid = game.Handle(0, new L12Command("resolvePrompt",
            PromptId: costPrompt.PromptId, CardInstanceIds: ["missing-card"]));
        Assert.False(invalid.Accepted);
        Assert.Equal(costPrompt.PromptId, Assert.Single(game.State.PendingPrompts).PromptId);
        Assert.Contains(hand, player.Hand);

        var cancel = game.Handle(0, new L12Command("resolvePrompt",
            PromptId: costPrompt.PromptId, Choice: "skip"));
        Assert.True(cancel.Accepted, cancel.Error);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains(hand, player.Hand);
        Assert.Empty(source.TimedModifiers);
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-self-troop-buff-negated")]
    public void NegationKeepsThePrepaidCostButDoesNotApplyTheBuff()
    {
        var fixture = DeclareAndPay("S02-0519", 11401);
        var power = Assert.IsType<L12MoraleCard>(fixture.GodPower);
        Assert.Single(fixture.Game.State.EffectStack).Negated = true;

        PassResponses(fixture.Game);

        Assert.True(power.Tapped);
        Assert.False(power.IsGodPower);
        Assert.Empty(fixture.Source.TimedModifiers);
        Assert.Single(fixture.Game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "negated");
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-self-troop-buff-source-revalidate")]
    public void SourceLeavingAfterPaymentFailsWithoutBuffingTheFrozenSnapshot()
    {
        var fixture = DeclareAndPay("S02-0606", 11402);
        var player = fixture.Game.State.Players[0];
        player.Field[0][0] = null;
        player.Graveyard.Add(fixture.Source);

        PassResponses(fixture.Game);

        Assert.Empty(fixture.Source.TimedModifiers);
        Assert.DoesNotContain(fixture.CostCards[0], player.Hand);
        Assert.Contains(fixture.CostCards[0], player.Graveyard);
        Assert.Single(fixture.Game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "failed");
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-self-troop-buff-duplicate")]
    public void DuplicateDeclarationSubmitCannotPayMasterDamageTwice()
    {
        var fixture = DeclareAndPay("S01-0301", 11403);
        Assert.Equal(7, fixture.Game.State.Players[0].Hp);

        var duplicate = fixture.Game.Handle(fixture.ModePrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: fixture.ModePrompt.PromptId, Choice: "mode:use"));
        Assert.False(duplicate.Accepted);
        Assert.Equal(7, fixture.Game.State.Players[0].Hp);
        PassResponses(fixture.Game);

        Assert.Single(fixture.Source.TimedModifiers);
        Assert.Equal(7, fixture.Game.State.Players[0].Hp);
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-self-troop-buff-reconnect")]
    public void PaidOrderedGraveCostAndBuffResumeExactlyOnceAfterV2Reconnect()
    {
        var fixture = DeclareAndPay("S01-0311", 11404);
        var response = Assert.Single(fixture.Game.State.PendingPrompts,
            prompt => prompt.Kind == "response");
        var checkpoint = fixture.Game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        var random = fixture.Game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);

        var restored = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            fixture.Game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        PassResponses(restored);

        var source = Assert.IsType<L12CardInstance>(restored.State.Players[0].Field[0][0]);
        Assert.Single(source.TimedModifiers,
            modifier => modifier.Source == "古斯塔夫一世" && modifier.TroopsDelta == 2000);
        Assert.Equal(2, restored.State.Players[0].Library.Count);
        Assert.Empty(restored.State.Players[0].Graveyard);
        Assert.False(restored.Handle(response.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
    }
}
