using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SimpleResourceTriggerConsistencyTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "simple-resource-trigger", "RESOURCE-TRIGGER", seed,
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
            player.SpecialZones.Runes = 0;
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            SummonRound = -1,
            OwnerIndex = owner,
        };
    }

    private static object? Invoke(object target, string method, params object?[] args)
    {
        var candidate = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(info => info.Name == method && info.GetParameters().Length == args.Length);
        return candidate.Invoke(target, args);
    }

    private static void Queue(L12GameEngine game, string cardId, string trigger,
        Dictionary<string, string>? data = null)
    {
        var source = Card(cardId, $"resource-source-{cardId}-{trigger}");
        game.State.Players[0].Resolving.Add(source);
        Invoke(game, "QueueOrPushTriggeredEffect", 0, source, trigger,
            "单段资源一致性测试", null, data ?? new Dictionary<string, string>());
    }

    private static void Resolve(L12GameEngine game, L12Prompt prompt, string choice)
    {
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var prompt = game.State.PendingPrompts.First(item => item.Kind == "response");
            Resolve(game, prompt, "pass");
        }
    }

    private static L12MoraleCard Morale(string id, bool tapped = true, bool godPower = false,
        string cardId = "S02-05C1")
        => new() { CardId = cardId, InstanceId = id, Tapped = tapped, IsGodPower = godPower };

    [Fact]
    [Trait("L12Evidence", "entry:simple-resource-trigger-spec")]
    public void ExactResourceTriggersOwnOneStructuredDefinitionAndOneSettlementScene()
    {
        var expected = new[]
        {
            ("S02-0603", 2, "enter", "gain-runes", 1, false),
            ("S02-0606", 2, "enter", "gain-runes", 1, false),
            ("S02-0607", 1, "enter", "gain-runes", 1, false),
            ("S02-0616", 2, "enter", "gain-runes", 1, true),
            ("S02-0618", 3, "enter", "gain-runes", 1, false),
            ("ST06-03", 1, "enter", "gain-runes", 1, true),
            ("ST06-08", 1, "enter", "gain-runes", 1, true),
            ("S02-01S1", 2, "death", "add-rested-morale", 1, true),
            ("S02-0508", 2, "death", "flip-morale-to-god-power", 1, false),
            ("S02-05M1", 1, "friendly-ranged-death", "flip-morale-to-god-power", 1, true),
            ("S02-06M1", 1, "morrigan-enemy-death", "gain-runes", 1, true),
            ("S02-0102", 1, "master-morale-return", "add-rested-morale", 1, true),
            ("S02-06S4", 2, "friendly-round-table-enter", "gain-runes", 1, true),
            ("S02-06M2", 2, "trial-advance", "gain-runes", 1, true),
            ("S02-01M1", 2, "master-legion-returned", "add-rested-morale", 1, true),
            ("S01-01C1", 2, "morale-returned-to-zero", "add-rested-morale", 2, true),
        };
        Assert.Equal(expected, L12SimpleResourceTriggerEffects.All.Select(spec =>
            (spec.CardId, spec.AbilitySequence, spec.Trigger, spec.Operation, spec.Amount, spec.Optional)));
        Assert.Equal(L12SimpleResourceTriggerEffects.All.Length,
            L12SimpleResourceTriggerEffects.All.Select(spec =>
                (spec.CardId, spec.Trigger, spec.DataAbility, spec.DataMode)).Distinct().Count());

        foreach (var spec in L12SimpleResourceTriggerEffects.All)
        {
            var ability = Catalog.AtomicEffects.Find(spec.CardId)!.Abilities
                .Single(item => item.Sequence == spec.AbilitySequence);
            Assert.DoesNotContain(ability.Trigger, new[] { "static", "continuous" });
            var scene = Assert.Single(ability.Presentations,
                item => item.Flow == L12SingleSegmentTriggeredEffectPresentations.Flow);
            Assert.Equal(spec.SettlementText, scene.DefaultText);

            var program = L12VerifiedAtomicPrograms.Find(spec.CardId, spec.Trigger);
            if (!spec.OwnsStandaloneAtomicAbility)
            {
                Assert.Null(program);
                Assert.Contains(ability.Atoms, atom => atom.Kind == L12AtomKinds.AddMorale);
                continue;
            }
            Assert.NotNull(program);
            Assert.Equal(spec.Optional, program.Atoms.Any(atom => atom.Kind == L12AtomKinds.Optional));
            Assert.Equal(spec.CandidateCondition is not null,
                program.Atoms.Any(atom => atom.Kind == L12AtomKinds.Condition));
            Assert.Equal(spec.TargetFilter is not null,
                program.Atoms.Any(atom => atom.Kind == L12AtomKinds.SelectTarget));
            var settlementKind = spec.Operation switch
            {
                L12SimpleResourceTriggerEffects.AddRestedMorale => L12AtomKinds.AddMorale,
                L12SimpleResourceTriggerEffects.GainRunes => L12AtomKinds.GainRune,
                L12SimpleResourceTriggerEffects.FlipMoraleToGodPower => L12AtomKinds.FlipMorale,
                _ => throw new InvalidOperationException(spec.Operation),
            };
            var settlement = Assert.Single(program.Atoms, atom => atom.Kind == settlementKind);
            Assert.Equal(spec.Amount.ToString(), settlement.Parameters["amount"]);
            Assert.Equal(spec.SettlementText, L12GameEngine.ResolveTriggeredEffectDisplayText(
                Card(spec.CardId, $"display-{spec.CardId}"), spec.Trigger, "旧文本",
                new Dictionary<string, string>
                {
                    ["ability"] = spec.DataAbility ?? string.Empty,
                    ["mode"] = spec.DataMode ?? string.Empty,
                }));
        }
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-resource-trigger-mandatory-no-selection")]
    public void MandatoryRuneEntryUsesResponseStackWithoutCreatingAnEmptyDeclaration()
    {
        var game = Create(11005);

        Queue(game, "S02-0603", "enter");

        Assert.Empty(game.State.PendingActivations);
        Assert.Single(game.State.EffectStack);
        Assert.Single(game.State.PendingPrompts, item => item.Kind == "response");
        PassResponses(game);

        Assert.Equal(1, game.State.Players[0].SpecialZones.Runes);
        Assert.Single(game.State.Events, entry => entry.Type == "runes"
            && entry.Text.Contains("梅林", StringComparison.Ordinal));
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == "S02-0603"));
    }

    [Theory]
    [InlineData("S02-0603", false)]
    [InlineData("S02-0606", false)]
    [InlineData("S02-0607", false)]
    [InlineData("S02-0616", true)]
    [InlineData("S02-0618", false)]
    [InlineData("ST06-03", true)]
    [InlineData("ST06-08", true)]
    [Trait("L12Evidence", "entry:simple-resource-trigger-rune-entry-pool")]
    public void EveryRuneEntryCardUsesTheSameDeclarationAndSettlementProtocol(string cardId, bool optional)
    {
        var game = Create(11007);

        Queue(game, cardId, "enter");
        if (optional)
        {
            var declaration = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("pending-activation", declaration.Continuation);
            Resolve(game, declaration, "mode:use");
        }

        Assert.Single(game.State.EffectStack);
        Assert.Single(game.State.PendingPrompts, item => item.Kind == "response");
        PassResponses(game);

        Assert.Equal(1, game.State.Players[0].SpecialZones.Runes);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == cardId));
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-resource-trigger-optional-decline")]
    public void OptionalRuneEntryCanBeDeclinedWithoutCreatingAStackItem()
    {
        var game = Create(11006);

        Queue(game, "ST06-03", "enter");

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Resolve(game, declaration, "mode:none");

        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Equal(0, game.State.Players[0].SpecialZones.Runes);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-declined"
            && entry.Cards.Any(card => card.CardId == "ST06-03"));
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-resource-trigger-no-candidate")]
    public void OptionalMoraleAdditionWithoutMoraleDeckSilentlySkipsBeforeDeclaration()
    {
        var game = Create(11001);

        Queue(game, "S02-01S1", "death");

        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-trigger");
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0508")]
    [Trait("L12Evidence", "entry:simple-resource-trigger-required-target")]
    public void MandatorySingleMoraleTargetStillRequiresAPlayerClickAndRevalidatesOnSettlement()
    {
        var game = Create(11002);
        var target = Morale("atalanta-only-morale", tapped: false);
        game.State.Players[0].Morale.Add(target);

        Queue(game, "S02-0508", "death");

        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", targetPrompt.Continuation);
        Assert.Equal([target.InstanceId], targetPrompt.ValidChoices);
        Assert.Empty(game.State.EffectStack);
        Resolve(game, targetPrompt, target.InstanceId);
        Assert.Single(game.State.EffectStack);

        target.IsGodPower = true;
        PassResponses(game);

        Assert.True(target.IsGodPower);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("目标在结算时失效", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("目标在结算时失效", StringComparison.Ordinal));
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "failed"
            && entry.Cards.Any(card => card.CardId == "S02-0508"));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-01S1")]
    [Trait("L12Evidence", "entry:simple-resource-trigger-reconnect")]
    public void AcceptedOptionalResourceEffectRestoresInResponseAndSettlesExactlyOnce()
    {
        var game = Create(11003);
        game.State.Players[0].MoraleDeck.Add(Morale("xiaotian-deck-morale"));
        Queue(game, "S02-01S1", "death");
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(declaration.PromptId, JsonSerializer.Serialize(game.SnapshotFor(0)),
            StringComparison.Ordinal);
        Resolve(game, declaration, "mode:use");
        var oldResponse = Assert.Single(game.State.PendingPrompts, item => item.Kind == "response");
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");

        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        PassResponses(game);

        Assert.Single(game.State.Players[0].Morale);
        Assert.Empty(game.State.Players[0].MoraleDeck);
        Assert.Single(game.State.Events, entry => entry.Type == "morale"
            && entry.Text.Contains("哮天犬·稚", StringComparison.Ordinal));
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == "S02-01S1"));
        Assert.False(game.Handle(oldResponse.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: oldResponse.PromptId, Choice: "pass")).Accepted);
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-resource-trigger-god-power-identity")]
    [L12AbilityEvidence("S02-0508:ability:death:9aea23b4138e399e",
        "candidate-generation", "black-lotus-excluded", "single-candidate-choice")]
    public void MoraleFlipTargetListUsesGodPowerIdentityAndExcludesBlackLotus()
    {
        var game = Create(110021);
        var target = Morale("atalanta-olympus-morale", tapped: false);
        var lotus = Morale("atalanta-black-lotus", tapped: false, cardId: "S02-0010");
        game.State.Players[0].Morale.AddRange([lotus, target]);

        Queue(game, "S02-0508", "death");

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal([target.InstanceId], prompt.ValidChoices);
        Assert.DoesNotContain(lotus.InstanceId, prompt.ValidChoices);
    }

    [Fact]
    [L12AbilityEvidence("S02-0508:ability:death:9aea23b4138e399e",
        "candidate-generation", "candidate-settlement-parity")]
    [L12AbilityEvidence("S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527",
        "candidate-generation", "black-lotus-excluded", "rested-only-filter")]
    public void EveryMoraleFlipFilterUsesTheSameGodPowerIdentityBoundary()
    {
        var game = Create(110022);
        var player = game.State.Players[0];
        var active = Morale("flip-filter-active", tapped: false);
        var rested = Morale("flip-filter-rested", tapped: true, cardId: "ST05-C1");
        var activeLotus = Morale("flip-filter-active-lotus", tapped: false, cardId: "S02-0010");
        var restedLotus = Morale("flip-filter-rested-lotus", tapped: true, cardId: "S02-0010");
        player.Morale.AddRange([activeLotus, restedLotus, active, rested]);
        var anySpec = L12SimpleResourceTriggerEffects.Find("S02-0508", "death")!;
        var restedSpec = L12SimpleResourceTriggerEffects.Find("S02-05M1", "friendly-ranged-death",
            new Dictionary<string, string> { ["ability"] = "artemisDeathFlip" })!;

        var any = Assert.IsAssignableFrom<IEnumerable<string>>(
            Invoke(game, "SimpleResourceMoraleTargets", player, anySpec)).ToArray();
        var onlyRested = Assert.IsAssignableFrom<IEnumerable<string>>(
            Invoke(game, "SimpleResourceMoraleTargets", player, restedSpec)).ToArray();

        Assert.Equal([active.InstanceId, rested.InstanceId], any);
        Assert.Equal([rested.InstanceId], onlyRested);
    }

    [Fact]
    [L12AbilityEvidence("S02-0508:ability:death:9aea23b4138e399e",
        "no-target", "black-lotus-excluded")]
    public void MandatoryMoraleFlipWithOnlyBlackLotusSilentlySkipsBeforeStacking()
    {
        var game = Create(110023);
        var lotus = Morale("atalanta-no-target-lotus", tapped: false, cardId: "S02-0010");
        game.State.Players[0].Morale.Add(lotus);

        Queue(game, "S02-0508", "death");

        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.False(lotus.IsGodPower);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-01M1")]
    [Trait("L12Evidence", "entry:simple-resource-trigger-condition-revalidate")]
    public void WukongResourceConditionFailureAfterDeclarationIsFailedNotCancelled()
    {
        var game = Create(110031);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        player.MoraleDeck.Add(Morale("wukong-deck-morale"));
        opponent.Morale.Add(Morale("wukong-opponent-morale"));
        Queue(game, "S02-01M1", "master-legion-returned",
            new Dictionary<string, string> { ["ability"] = "wukongReturnMorale" });
        Resolve(game, Assert.Single(game.State.PendingPrompts), "mode:use");
        Assert.Single(game.State.EffectStack);

        player.Morale.Add(Morale("wukong-current-morale"));
        PassResponses(game);

        Assert.Single(player.Morale);
        Assert.Single(player.MoraleDeck);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("资源条件在结算时失效", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-cancelled"
            && entry.Text.Contains("资源条件在结算时失效", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-resource-trigger-negated")]
    public void NegatedOptionalResourceEffectDoesNotMutateResources()
    {
        var game = Create(11004);
        game.State.Players[0].MoraleDeck.Add(Morale("negated-deck-morale"));
        Queue(game, "S02-01S1", "death");
        Resolve(game, Assert.Single(game.State.PendingPrompts), "mode:use");
        Assert.Single(game.State.EffectStack).Negated = true;

        PassResponses(game);

        Assert.Empty(game.State.Players[0].Morale);
        Assert.Single(game.State.Players[0].MoraleDeck);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "negated"
            && entry.Cards.Any(card => card.CardId == "S02-01S1"));
    }
}
