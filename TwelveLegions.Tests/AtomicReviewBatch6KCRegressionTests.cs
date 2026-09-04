using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6KCRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static readonly IReadOnlyDictionary<string, int> AuditedAbilityCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0401"] = 2, ["S01-0402"] = 2, ["S01-0403"] = 4, ["S01-0404"] = 1,
            ["S01-0405"] = 2, ["S01-0406"] = 2, ["S01-0407"] = 2, ["S01-0408"] = 2,
            ["S01-0409"] = 4, ["S01-0410"] = 2, ["S01-0411"] = 3, ["S01-0412"] = 2,
            ["S01-0413"] = 4, ["S01-0414"] = 2, ["S01-0415"] = 3, ["S01-0416"] = 3,
            ["S01-0417"] = 2, ["S01-0418"] = 1, ["S01-0419"] = 1, ["S01-0420"] = 2,
            ["S01-04C1"] = 1, ["S01-04D1"] = 3, ["S01-04M1"] = 2, ["S01-04M2"] = 3,
        };

    private static L12GameEngine Create(string firstMaster = "S01-04M2", int seed = 8301)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = $"{firstMaster}第六批6K-C审查牌库",
            MasterId = firstMaster,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "atomic-review-batch6kc", "ATOMIC6KC", seed,
            ["甲", "乙"], [deck, baseDeck], skipPreparation: true,
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
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Resolving.Clear();
            player.ExtraRelics.Clear();
            player.Relic = null;
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int? troops = null, int? cost = null)
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
            Cost = cost ?? definition.Cost ?? 0,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            EffectiveProfession = definition.Profession,
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            SummonRound = -1,
        };
    }

    private static void AddMorale(L12PlayerState player, int count, bool tapped = false)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-04C1",
                InstanceId = $"batch6kc-morale-{player.PlayerIndex}-{index}",
                Tapped = tapped,
            });
    }

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    private static void QueueTrigger(L12GameEngine game, L12CardInstance source, string trigger)
        => Invoke(game, "QueueOrPushTriggeredEffect", 0, source, trigger, "6K-C逐能力审计", null,
            new Dictionary<string, string>());

    private static void Resolve(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var command = choices.Length == 1
            ? new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choices[0])
            : new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: [.. choices]);
        var result = game.Handle(prompt.PlayerIndex, command);
        Assert.True(result.Accepted, result.Error);
    }

    private static L12StackItem PassUntilFlow(L12GameEngine game, string flow)
    {
        for (var safety = 0; safety < 80; safety++)
        {
            var item = game.State.EffectStack.SingleOrDefault(candidate =>
                candidate.Data.GetValueOrDefault("atomicFlow") == flow);
            if (item is not null) return item;
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", prompt.Kind);
            Resolve(game, "pass");
        }
        throw new Xunit.Sdk.XunitException($"未进入预期效果段 {flow}");
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 80 && game.State.PendingPrompts.Count > 0; safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", prompt.Kind);
            Resolve(game, "pass");
        }
        Assert.Empty(game.State.PendingPrompts);
    }

    [Fact]
    [Trait("L12Evidence", "batch:6K-C-inventory")]
    public void TakamagaharaAuditFreezesEveryCardAndAbility()
    {
        Assert.Equal(24, AuditedAbilityCounts.Count);
        Assert.Equal(55, AuditedAbilityCounts.Values.Sum());
        Assert.All(AuditedAbilityCounts, pair =>
        {
            var card = Assert.Contains(pair.Key, Catalog.Cards);
            Assert.Equal("gaotianyuan", card.Faction);
            Assert.False(string.IsNullOrWhiteSpace(card.Effect));
            Assert.True(pair.Value > 0);
        });
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0401")]
    public void HondaDeclaresTheSubsequentKillBeforeEitherIndependentSegmentStacks()
    {
        var game = Create(seed: 8302);
        var source = Card("S01-0401", "batch6kc-honda");
        var eligibleAfterDebuff = Card("S01-0302", "batch6kc-honda-one", cost: 1);
        var tooExpensive = Card("S01-0202", "batch6kc-honda-two", cost: 2);
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[1].Field[0][0] = eligibleAfterDebuff;
        game.State.Players[1].Field[0][1] = tooExpensive;

        QueueTrigger(game, source, "attack");

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains(eligibleAfterDebuff.InstanceId, declaration.ValidChoices);
        Assert.DoesNotContain(tooExpensive.InstanceId, declaration.ValidChoices);
        Assert.Empty(game.State.EffectStack);
        Resolve(game, eligibleAfterDebuff.InstanceId);

        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("honda-debuff", first.Data["atomicFlow"]);
        first.Negated = true;
        // A cost-0 target remains legal even when the first segment is negated, so the second segment
        // must still receive its own response window rather than being swallowed by the first item.
        eligibleAfterDebuff.CostModifier = -1;
        var second = PassUntilFlow(game, "honda-kill");
        Assert.Equal(eligibleAfterDebuff.InstanceId, second.Data["declared:killTarget"]);
        Assert.Contains(eligibleAfterDebuff, game.State.Players[1].Field.SelectMany(row => row));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-04M1")]
    public void AmaterasuReadyAndFrontBuffShareOneResponseAndAreNegatedTogether()
    {
        var game = Create("S01-04M1", 8303);
        var player = game.State.Players[0];
        var discard = Card("S01-0401", "batch6kc-amaterasu-discard");
        var front = Card("S01-0404", "batch6kc-amaterasu-front");
        player.Hand.Add(discard);
        player.Field[0][0] = front;
        AddMorale(player, 3, tapped: true);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "amaterasuReady")).Accepted);
        Resolve(game, discard.InstanceId);

        var moraleDeclaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", moraleDeclaration.Continuation);
        Assert.All(player.Morale, morale => Assert.Contains(morale.InstanceId, moraleDeclaration.ValidChoices));
        Resolve(game, player.Morale[1].InstanceId, player.Morale[2].InstanceId);

        Assert.Contains(discard, player.Graveyard);
        Assert.All(player.Morale, morale => Assert.True(morale.Tapped));
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("amaterasu-ready", first.Data["atomicFlow"]);
        Assert.Equal("single-effect", first.Data["compositeResponseScope"]);
        first.Negated = true;

        PassResponses(game);
        Assert.All(player.Morale, morale => Assert.True(morale.Tapped));
        Assert.Same(front, game.State.Players[0].Field[0][0]);
        Assert.Equal(front.BaseTroops, front.Troops);
        Assert.DoesNotContain(game.State.EffectStack.Concat(game.State.DeferredEffectStack), item =>
            item.Data.GetValueOrDefault("atomicFlow") == "amaterasu-front-buff");
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-04M1")]
    public void AmaterasuCostDebuffAndSubsequentKillAreSeparateResponseItems()
    {
        var game = Create("S01-04M1", 8304);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        AddMorale(player, 1);
        var target = Card("S01-0302", "batch6kc-amaterasu-target", cost: 1);
        enemy.Field[0][0] = target;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "amaterasuKill")).Accepted);
        Resolve(game, target.InstanceId);
        Resolve(game, target.InstanceId);

        Assert.True(Assert.Single(player.Morale).Tapped);
        var first = Assert.Single(game.State.EffectStack);
        Assert.Equal("amaterasu-debuff", first.Data["atomicFlow"]);
        first.Negated = true;
        target.CostModifier = -1;

        var second = PassUntilFlow(game, "amaterasu-kill");
        Assert.Equal(target.InstanceId, second.Data["declared:killTarget"]);
        Assert.True(Assert.Single(player.Morale).Tapped);
        Assert.Contains(target, enemy.Field.SelectMany(row => row));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0418")]
    public void DivinePunishmentDeclaresItsPublicKillTargetBeforePaymentAndStack()
    {
        var game = Create(seed: 8309);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var tactic = Card("S01-0418", "batch6kc-divine-punishment");
        var legal = Card("S01-0302", "batch6kc-divine-legal", cost: 7);
        var tooExpensive = Card("S01-0202", "batch6kc-divine-expensive", cost: 8);
        player.Hand.Add(tactic);
        enemy.Field[0][0] = legal;
        enemy.Field[0][1] = tooExpensive;
        AddMorale(player, tactic.CurrentCost);

        Assert.True(game.Handle(0, new L12Command("playCard", tactic.InstanceId)).Accepted);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains(legal.InstanceId, declaration.ValidChoices);
        Assert.DoesNotContain(tooExpensive.InstanceId, declaration.ValidChoices);
        Assert.Contains(tactic, player.Hand);
        Assert.Empty(game.State.EffectStack);
        Resolve(game, legal.InstanceId);

        Assert.DoesNotContain(tactic, player.Hand);
        var effect = Assert.Single(game.State.EffectStack);
        Assert.Equal("divine-punishment-effect", effect.Data["atomicFlow"]);
        Assert.Equal(legal.InstanceId, effect.Data["declared:killTarget"]);
    }

    [Fact]
    [Trait("L12Evidence", "cards:S01-0415,S01-04M2")]
    public void SusanoPublicLegionTargetExcludesConcealedCardsAndIncludesRingNeutralLegions()
    {
        var game = Create(seed: 8305);
        var player = game.State.Players[0];
        var concealed = Card("S01-0415", "batch6kc-hidden-hattori");
        concealed.Hidden = true;
        var neutral = Card("S01-0001", "batch6kc-ring-neutral");
        var ring = Card("S02-0008", "batch6kc-ring");
        player.Field[0][0] = concealed;
        player.Field[0][1] = neutral;
        player.Relic = ring;
        AddMorale(player, 1);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "frontBuff")).Accepted);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.DoesNotContain(concealed.InstanceId, declaration.ValidChoices);
        Assert.Contains(neutral.InstanceId, declaration.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0417")]
    public void KusanagiTargetsOnlyPublicLegionsForBothModes()
    {
        var game = Create(seed: 8306);
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var sword = Card("S01-0417", "batch6kc-kusanagi", 5000);
        var concealed = Card("S01-0415", "batch6kc-kusanagi-hidden");
        concealed.Hidden = true;
        var neutral = Card("S01-0001", "batch6kc-kusanagi-neutral");
        player.Field[0][0] = sword;
        player.Field[0][1] = concealed;
        player.Field[0][2] = neutral;
        player.ExtraRelics.Add(Card("S02-0008", "batch6kc-extra-ring"));
        AddMorale(player, 2);

        Assert.True(game.Handle(0, new L12Command("activateAbility", sword.InstanceId,
            Ability: "kusanagiStrong")).Accepted);
        var strong = Assert.Single(game.State.PendingPrompts);
        Assert.DoesNotContain(concealed.InstanceId, strong.ValidChoices);
        Assert.Contains(neutral.InstanceId, strong.ValidChoices);
        Resolve(game, neutral.InstanceId);
        // Cancel the pending item so the second half of this selector test starts from a clean stack.
        Assert.Single(game.State.EffectStack).Negated = true;
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response") Resolve(game, "pass");

        var counter = Card("S01-0019", "batch6kc-covered-counter");
        counter.Hidden = false;
        enemy.Field[1][0] = counter;
        var enemyLegion = Card("S01-0302", "batch6kc-enemy-legion");
        enemy.Field[0][0] = enemyLegion;
        player.UsedAbilities.Remove($"active:{sword.InstanceId}:choice");
        Assert.True(game.Handle(0, new L12Command("activateAbility", sword.InstanceId,
            Ability: "kusanagiDebuff")).Accepted);
        var debuff = Assert.Single(game.State.PendingPrompts);
        Assert.DoesNotContain(counter.InstanceId, debuff.ValidChoices);
        Assert.Contains(enemyLegion.InstanceId, debuff.ValidChoices);
    }

    [Theory]
    [InlineData("enter")]
    [InlineData("attack")]
    [Trait("L12Evidence", "card:S01-0416")]
    public void InahimeUsesTheSamePublicFactionLegionQueryAtBothPrintedTimings(string trigger)
    {
        var game = Create(seed: trigger == "enter" ? 8307 : 8308);
        var player = game.State.Players[0];
        var source = Card("S01-0416", $"batch6kc-inahime-{trigger}");
        var concealed = Card("S01-0415", $"batch6kc-inahime-hidden-{trigger}", 1000);
        concealed.Hidden = true;
        var neutral = Card("S01-0001", $"batch6kc-inahime-neutral-{trigger}", 1000);
        player.Field[0][0] = source;
        player.Field[0][1] = concealed;
        player.Field[0][2] = neutral;
        player.ExtraRelics.Add(Card("S02-0008", $"batch6kc-inahime-ring-{trigger}"));

        QueueTrigger(game, source, trigger);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.DoesNotContain(concealed.InstanceId, declaration.ValidChoices);
        Assert.Contains(neutral.InstanceId, declaration.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "cards:S01-0405,S01-0415")]
    public void MiyamotoEnterConditionDoesNotCountAConcealedCardAsAnotherLegion()
    {
        var game = Create(seed: 8310);
        var player = game.State.Players[0];
        var source = Card("S01-0405", "batch6kc-miyamoto");
        var concealed = Card("S01-0415", "batch6kc-miyamoto-hidden");
        concealed.Hidden = true;
        player.Field[0][0] = source;
        player.Field[0][1] = concealed;

        QueueTrigger(game, source, "enter");
        Assert.Single(game.State.EffectStack);
        PassResponses(game);

        Assert.True(source.HasCharge);
        Assert.True(concealed.Hidden);
    }

    [Fact]
    [Trait("L12Evidence", "cards:S01-04D1,S02-0008")]
    public void YomiRecoveryTreatsUniversalGraveCardsAsTakamagaharaWhileTheRingIsActive()
    {
        var game = Create("S01-04D1", 8311);
        var player = game.State.Players[0];
        var neutral = Card("S01-0001", "batch6kc-yomi-ring-neutral");
        player.Graveyard.Add(neutral);
        player.ExtraRelics.Add(Card("S02-0008", "batch6kc-yomi-ring"));

        var result = game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "yomiRecover"));

        Assert.True(result.Accepted, result.Error);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains(neutral.InstanceId, declaration.ValidChoices);
        Assert.Empty(game.State.EffectStack);
    }
}
