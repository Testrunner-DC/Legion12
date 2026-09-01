using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6GARegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, string? masterId = null)
    {
        var baseDeck = Catalog.DeckAt(0);
        var decks = string.IsNullOrWhiteSpace(masterId)
            ? new[] { baseDeck, baseDeck }
            : new[]
            {
                new L12PresetDeckDefinition
                {
                    Name = "Batch6GA",
                    MasterId = masterId,
                    CardIds = [.. baseDeck.CardIds],
                    MoraleIds = [.. baseDeck.MoraleIds],
                    SpecialIds = [.. baseDeck.SpecialIds],
                },
                baseDeck,
            };
        var game = new L12GameEngine(Catalog, "atomic-review-batch6ga", "ATOMIC6GA", seed,
            ["甲", "乙"], decks, skipPreparation: true,
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
            player.Resolving.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.UsedAbilities.Clear();
            player.SpecialZones.Trials.Clear();
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
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
            OwnerIndex = owner,
        };
    }

    private static void InvokeVoid(L12GameEngine game, string methodName, params object?[] arguments)
    {
        var method = typeof(L12GameEngine).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(game, arguments);
    }

    private static L12TriggerCandidate InvokeCandidate(L12GameEngine game, string methodName, params object?[] arguments)
    {
        var method = typeof(L12GameEngine).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<L12TriggerCandidate>(method.Invoke(game, arguments));
    }

    private static void QueueCandidate(L12GameEngine game, L12TriggerCandidate candidate)
        => InvokeVoid(game, "QueueTriggerCandidates", (object)new[] { candidate });

    private static void QueueDirectTrigger(L12GameEngine game, L12CardInstance source, string trigger,
        Dictionary<string, string>? data = null)
        => InvokeVoid(game, "QueueOrPushTriggeredEffect", 0, source, trigger, "Batch6GA测试触发", null, data);

    private static void AddRestedOrdinaryMorale(L12PlayerState player, string instanceId)
        => player.Morale.Add(new L12MoraleCard
        {
            CardId = "S02-05C1",
            InstanceId = instanceId,
            Tapped = true,
            IsGodPower = false,
        });

    private static void AddMoraleDeckCard(L12PlayerState player, string instanceId)
        => player.MoraleDeck.Add(new L12MoraleCard
        {
            CardId = "S02-05C1",
            InstanceId = instanceId,
        });

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

    private static void QueueFocusTrigger(L12GameEngine game, string plan)
    {
        var player = game.State.Players[0];
        switch (plan)
        {
            case "margaret-entry":
            {
                var source = Card("S02-0304", "batch6ga-margaret-entry");
                player.Field[0][0] = source;
                player.Library.Add(Card("S02-0004", "batch6ga-margaret-top"));
                QueueDirectTrigger(game, source, "enter");
                break;
            }
            case "margaret-damage":
            {
                var source = Card("S02-0304", "batch6ga-margaret-damage");
                player.Field[0][0] = source;
                QueueDirectTrigger(game, source, "active",
                    new Dictionary<string, string> { ["ability"] = "margaretMasterDamage" });
                break;
            }
            case "anderstorp":
            {
                player.Relic = Card("S02-0305", "batch6ga-ring");
                QueueCandidate(game, InvokeCandidate(game, "BuildAnderstorpRingDrawCandidate", 0));
                break;
            }
            case "artemis":
            {
                AddRestedOrdinaryMorale(player, "batch6ga-artemis-morale");
                var defeated = Card("S02-0513", "batch6ga-artemis-ranged");
                defeated.LastKnownWasRanged = true;
                QueueCandidate(game, InvokeCandidate(game, "BuildArtemisRangedDeathCandidate", 0, defeated));
                break;
            }
            case "morrigan":
                QueueCandidate(game, InvokeCandidate(game, "BuildMorriganEnemyDeathCandidate", 1));
                break;
            case "limu-morale":
            {
                var liMu = Card("S02-0102", "batch6ga-limu");
                player.Field[0][0] = liMu;
                player.Field[0][1] = Card("S02-0609", "batch6ga-limu-fill-one");
                player.Field[0][2] = Card("S02-0609", "batch6ga-limu-fill-two");
                AddMoraleDeckCard(player, "batch6ga-limu-morale");
                InvokeVoid(game, "QueueS2MasterMoraleReturnTriggers", 0,
                    Card(game.State.Players[0].MasterId, "master-0"), 4);
                break;
            }
            case "grail-round-table":
            {
                var trial = Card("S02-06S4", "batch6ga-grail");
                trial.TrialCompleted = true;
                player.SpecialZones.Trials.Add(trial);
                var entered = Card("S02-0601", "batch6ga-round-table");
                player.Field[0][0] = entered;
                InvokeVoid(game, "QueueS2GrailRoundTableEntry", 0, entered);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(plan), plan, null);
        }
    }

    public static IEnumerable<object[]> FocusPlans()
    {
        yield return ["margaret-entry", ""];
        yield return ["margaret-damage", ""];
        yield return ["anderstorp", "S02-0305"];
        yield return ["artemis", "S02-05M1"];
        yield return ["morrigan", "S02-06M1"];
        yield return ["limu-morale", ""];
        yield return ["grail-round-table", ""];
    }

    [Theory]
    [MemberData(nameof(FocusPlans))]
    [Trait("L12Evidence", "entry:batch6ga-public-trigger-declaration")]
    public void EveryBatch6GATriggerDeclaresItsPublicModeBeforeAnyStackItem(string plan, string? masterId)
    {
        var game = Create(9000 + plan.Length, masterId);

        QueueFocusTrigger(game, plan);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains("mode:none", declaration.ValidChoices);
        Assert.Contains("mode:use", declaration.ValidChoices);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "card-effect");
    }

    public static IEnumerable<object[]> OptionalOncePlans()
    {
        yield return ["anderstorp", "S02-0305", "trigger:anderstorp-draw:3"];
        yield return ["artemis", "S02-05M1", "trigger:artemis-ranged-death:3"];
        yield return ["morrigan", "S02-06M1", "s2-morrigan-rune:3"];
        yield return ["limu-morale", "", "trigger:limu-morale:batch6ga-limu:3"];
        yield return ["grail-round-table", "", "trigger:grail-round-table:3"];
    }

    [Theory]
    [MemberData(nameof(OptionalOncePlans))]
    [Trait("L12Evidence", "entry:batch6ga-optional-once-reservation")]
    public void OptionalOnceUsesPendingReservationUntilThePlayerCommits(
        string plan, string? masterId, string finalKey)
    {
        var game = Create(9100 + plan.Length, masterId);
        var player = game.State.Players[0];

        QueueFocusTrigger(game, plan);

        Assert.DoesNotContain(finalKey, player.UsedAbilities);
        Assert.Contains($"{finalKey}:pending", player.UsedAbilities);
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0304")]
    [Trait("L12Evidence", "entry:margaret-prepaid-cost-independent-lock")]
    public void MargaretPrepaysRestAndQueuesTheHealLockAsAnIndependentSegment()
    {
        var game = Create(9201);
        var player = game.State.Players[0];
        player.Hp = player.MaxHp - 2;
        QueueFocusTrigger(game, "margaret-damage");

        ResolveOnlyPrompt(game, "mode:use");

        var margaret = Assert.IsType<L12CardInstance>(player.Field[0][0]);
        Assert.True(margaret.Tapped);
        var heal = Assert.Single(game.State.EffectStack);
        Assert.Equal("margaret-heal", heal.Data["atomicFlow"]);
        heal.Negated = true;
        for (var safety = 0; safety < 100
             && game.State.EffectStack.LastOrDefault()?.Data.GetValueOrDefault("atomicFlow") == "margaret-heal"
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            ResolveOnlyPrompt(game, "pass");

        Assert.True(margaret.Tapped);
        Assert.Equal(player.MaxHp - 2, player.Hp);
        var lockSegment = Assert.Single(game.State.EffectStack);
        Assert.Equal("margaret-heal-lock", lockSegment.Data["atomicFlow"]);
        Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-05M1")]
    [Trait("L12Evidence", "entry:artemis-public-target-invalid-no-once-refund")]
    public void ArtemisDeclaresTheExactMoraleAndTargetFailureDoesNotRefundOnce()
    {
        var game = Create(9202, "S02-05M1");
        var player = game.State.Players[0];
        QueueFocusTrigger(game, "artemis");
        ResolveOnlyPrompt(game, "mode:use");

        var targetPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", targetPrompt.Continuation);
        Assert.Contains("batch6ga-artemis-morale", targetPrompt.ValidChoices);
        ResolveOnlyPrompt(game, "batch6ga-artemis-morale");

        var morale = Assert.Single(player.Morale);
        morale.Tapped = false;
        PassResponses(game);

        Assert.False(morale.IsGodPower);
        Assert.Contains("trigger:artemis-ranged-death:3", player.UsedAbilities);
    }
}
