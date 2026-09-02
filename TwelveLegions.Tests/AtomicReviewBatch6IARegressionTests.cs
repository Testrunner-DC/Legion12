using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6IARegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static readonly (string CardId, string Trigger)[] ReviewedOptionalPrograms =
    [
        ("S01-0413", "enter"),
        ("S01-0405", "attack"),
        ("S01-0409", "after-attack"),
        ("S01-0115", "enter"),
        ("S01-0301", "death"),
        ("S01-0304", "enter"),
        ("S01-0309", "death"),
        ("S02-0104", "enter"),
        ("S02-0203", "death"),
        ("S02-0402", "death"),
        ("S02-0512", "death"),
        ("S02-0507", "enter"),
        ("S02-0507", "promotion-enter"),
        ("S02-0616", "enter"),
        ("ST01-02", "after-attack"),
        ("ST02-04", "enter"),
        ("ST06-03", "enter"),
        ("ST06-05", "enter"),
        ("ST06-05", "attack"),
        ("ST06-06", "enter"),
        ("ST06-08", "enter"),
    ];

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "atomic-review-batch6ia", "ATOMIC6IA", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
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
        }
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            SummonRound = -1,
            OwnerIndex = 0,
        };
    }

    private static object? Invoke(L12GameEngine game, string methodName, params object?[] arguments)
    {
        var method = typeof(L12GameEngine).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(game, arguments);
    }

    private static L12Prompt ResolveOnlyPrompt(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            ResolveOnlyPrompt(game, "pass");
    }

    private static L12CardInstance QueueOptionalProgram(L12GameEngine game, string cardId, string trigger,
        bool conditionSatisfied = true)
    {
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var source = Card(cardId, $"batch6ia-{cardId}-{trigger}-{conditionSatisfied}");
        if (source.CardType == "artifact") player.Relic = source;
        else if (trigger == "death") player.Resolving.Add(source);
        else player.Field[0][0] = source;
        player.Library.Add(Card("S01-0001", $"batch6ia-draw-{cardId}-{trigger}"));

        player.Hp = 6;
        opponent.Hp = 8;
        if (conditionSatisfied)
        {
            if (cardId == "S01-0405") opponent.Hand.Add(Card("S01-0002", "batch6ia-miyamoto-opponent-hand"));
        }
        else
        {
            switch ((cardId, trigger))
            {
                case ("S01-0413", "enter"):
                    for (var index = 0; index < 6; index++)
                        player.Hand.Add(Card("S01-0001", $"batch6ia-hiromasa-hand-{index}"));
                    break;
                case ("S01-0405", "attack"):
                    player.Hand.Add(Card("S01-0001", "batch6ia-miyamoto-hand"));
                    break;
                case ("S01-0409", "after-attack"):
                    break;
                case ("S01-0115", "enter"):
                    for (var index = 0; index < 8; index++)
                        player.Morale.Add(new L12MoraleCard
                        {
                            CardId = "S01-01C1", InstanceId = $"batch6ia-jingke-morale-{index}",
                        });
                    break;
                case ("S01-0304", "enter"):
                    player.Hp = opponent.Hp;
                    break;
                case ("S01-0309", "death"):
                    player.Hp = 9;
                    opponent.Hp = 8;
                    break;
            }
        }

        var data = trigger == "after-attack"
            ? new Dictionary<string, string> { ["killed"] = conditionSatisfied ? "true" : "false" }
            : null;
        Invoke(game, "QueueOrPushTriggeredEffect", 0, source, trigger, "6I-A可选原子测试", null, data);
        return source;
    }

    public static IEnumerable<object[]> ReviewedPrograms()
        => ReviewedOptionalPrograms.Select(program => new object[] { program.CardId, program.Trigger });

    public static IEnumerable<object[]> ConditionalPrograms()
    {
        yield return ["S01-0413", "enter"];
        yield return ["S01-0405", "attack"];
        yield return ["S01-0409", "after-attack"];
        yield return ["S01-0115", "enter"];
        yield return ["S01-0304", "enter"];
        yield return ["S01-0309", "death"];
    }

    [Fact]
    [Trait("L12Evidence", "entry:batch6ia-complete-verified-optional-inventory")]
    public void ReviewedInventoryEqualsEveryVerifiedAtomicOptionalProgram()
    {
        var actual = L12VerifiedAtomicPrograms.All
            .Where(program => program.Atoms.Any(atom => atom.Kind == L12AtomKinds.Optional))
            .Select(program => (program.CardId, program.Trigger))
            .OrderBy(program => program.CardId, StringComparer.Ordinal)
            .ThenBy(program => program.Trigger, StringComparer.Ordinal)
            .ToArray();
        var expected = ReviewedOptionalPrograms
            .OrderBy(program => program.CardId, StringComparer.Ordinal)
            .ThenBy(program => program.Trigger, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(ReviewedPrograms))]
    [Trait("L12Evidence", "entry:batch6ia-prestack-optional-declaration")]
    public void EveryVerifiedAtomicOptionalDeclaresBeforeAnyStackItem(string cardId, string trigger)
    {
        var game = Create(9400 + Array.FindIndex(ReviewedOptionalPrograms,
            program => program == (cardId, trigger)));

        QueueOptionalProgram(game, cardId, trigger);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains("mode:none", declaration.ValidChoices);
        Assert.Contains("mode:use", declaration.ValidChoices);
        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "verified-atomic-optional");
    }

    [Theory]
    [MemberData(nameof(ReviewedPrograms))]
    [Trait("L12Evidence", "entry:batch6ia-decline-no-empty-stack")]
    public void DecliningADeclaredVerifiedOptionalCreatesNoEmptyResponseStack(string cardId, string trigger)
    {
        var game = Create(9450 + Array.FindIndex(ReviewedOptionalPrograms,
            program => program == (cardId, trigger)));
        QueueOptionalProgram(game, cardId, trigger);

        ResolveOnlyPrompt(game, "mode:none");

        Assert.Empty(game.State.EffectStack);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }

    [Theory]
    [MemberData(nameof(ReviewedPrograms))]
    [Trait("L12Evidence", "entry:batch6ia-resolution-immutable-mode")]
    public void CommittedVerifiedOptionalReadsItsDeclaredModeWithoutAResolutionPrompt(string cardId, string trigger)
    {
        var game = Create(9500 + Array.FindIndex(ReviewedOptionalPrograms,
            program => program == (cardId, trigger)));
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        QueueOptionalProgram(game, cardId, trigger);
        var hpBefore = opponent.Hp;

        ResolveOnlyPrompt(game, "mode:use");

        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal("mode:use", item.Data.GetValueOrDefault("declared:mode"));
        PassResponses(game);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "verified-atomic-optional");
        var program = Assert.IsType<L12VerifiedAtomicProgram>(L12VerifiedAtomicPrograms.Find(cardId, trigger));
        if (program.Atoms.Any(atom => atom.Kind == L12AtomKinds.DamageMaster))
            Assert.Equal(hpBefore - 1, opponent.Hp);
        else if (program.Atoms.Any(atom => atom.Kind == L12AtomKinds.GainRune))
            Assert.Equal(1, player.SpecialZones.Runes);
        else if (program.Atoms.Any(atom => atom.Kind == L12AtomKinds.AddMorale))
            Assert.Single(player.Morale);
        else
            Assert.Single(player.Hand);
    }

    [Theory]
    [MemberData(nameof(ConditionalPrograms))]
    [Trait("L12Evidence", "entry:batch6ia-false-condition-no-candidate")]
    public void FalsePreconditionCreatesNeitherDeclarationNorResponseStack(string cardId, string trigger)
    {
        var game = Create(9600 + cardId[^1] + trigger.Length);

        QueueOptionalProgram(game, cardId, trigger, conditionSatisfied: false);

        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0304")]
    [Trait("L12Evidence", "entry:batch6ia-trigger-condition-snapshot")]
    public void ConditionAcceptedAtTriggerTimeIsNotRecheckedAfterResponses()
    {
        var game = Create(9650);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        QueueOptionalProgram(game, "S01-0304", "enter");
        ResolveOnlyPrompt(game, "mode:use");
        var hpBefore = opponent.Hp;
        player.Hp = opponent.Hp;

        PassResponses(game);

        Assert.Equal(hpBefore - 1, opponent.Hp);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0507")]
    [Trait("L12Evidence", "entry:batch6ia-atalanta-two-independent-entry-draws")]
    public void AtalantaNormalAndPromotionEntryOptionsRemainIndependentCandidatesAndStackItems()
    {
        var game = Create(9651);
        var source = Card("S02-0507", "batch6ia-atalanta-two-entry-options");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].Library.AddRange([
            Card("S01-0001", "batch6ia-atalanta-draw-one"),
            Card("S01-0002", "batch6ia-atalanta-draw-two"),
        ]);
        var create = typeof(L12GameEngine).GetMethod("CreateTriggerCandidate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(create);
        var promotion = Assert.IsType<L12TriggerCandidate>(create.Invoke(game,
            [0, source, "promotion-enter", "【晋升登场】效果", null, source]));
        var enter = Assert.IsType<L12TriggerCandidate>(create.Invoke(game,
            [0, source, "enter", "【登场时】效果", null, source]));
        Invoke(game, "QueueTriggerCandidates", (object)new[] { promotion, enter });
        var order = Assert.Single(game.State.PendingPrompts);
        var ordered = game.Handle(0, new L12Command("resolvePrompt", PromptId: order.PromptId,
            CardInstanceIds: [promotion.CandidateId, enter.CandidateId]));
        Assert.True(ordered.Accepted, ordered.Error);

        var first = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", first.Continuation);
        ResolveOnlyPrompt(game, "mode:use");
        var second = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", second.Continuation);
        ResolveOnlyPrompt(game, "mode:use");

        Assert.Equal(2, game.State.EffectStack.Count);
        Assert.Contains(game.State.EffectStack, item => item.Trigger == "enter"
            && item.Data.GetValueOrDefault("declared:mode") == "mode:use");
        Assert.Contains(game.State.EffectStack, item => item.Trigger == "promotion-enter"
            && item.Data.GetValueOrDefault("declared:mode") == "mode:use");
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0405")]
    [Trait("L12Evidence", "entry:batch6ia-attack-plan-composition")]
    public void MiyamotoUsesTheExistingAttackPlanWithoutDuplicateCandidatesOrPrompts()
    {
        var game = Create(9652);
        var miyamoto = Card("S01-0405", "batch6ia-miyamoto-real-attack");
        miyamoto.SummonRound = -1;
        game.State.Players[0].Field[0][0] = miyamoto;
        game.State.Players[0].Library.Add(Card("S01-0001", "batch6ia-miyamoto-real-draw"));
        game.State.Players[1].Hand.Add(Card("S01-0002", "batch6ia-miyamoto-real-opponent-hand"));

        var result = game.Handle(0, new L12Command("attack", miyamoto.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.True(result.Accepted, result.Error);
        Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", game.State.PendingPrompts[0].Continuation);
        Assert.Single(game.State.PendingActivations);
        Assert.Single(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
    }
}
