using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SimpleDeathTrialAdvanceTriggerConsistencyTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "simple-death-trial", "TRIAL-DEATH", seed,
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
            player.SpecialZones.Trials.Clear();
            player.SpecialZones.TrialLevel = 0;
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

    private static object? Invoke(object target, string method, params object?[] args)
    {
        var candidate = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(info => info.Name == method && info.GetParameters().Length == args.Length);
        return candidate.Invoke(target, args);
    }

    private static void QueueDeath(L12GameEngine game, string cardId, int controller = 0, int owner = 0)
    {
        var source = Card(cardId, $"trial-source-{cardId}-{controller}-{owner}", owner);
        game.State.Players[controller].Resolving.Add(source);
        Invoke(game, "QueueOrPushTriggeredEffect", controller, source, "death",
            "单试炼推进一致性测试", null, new Dictionary<string, string> { ["cause"] = "effect" });
    }

    private static L12Prompt OnlyResponse(L12GameEngine game)
        => Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");

    private static void ResolvePrompt(L12GameEngine game, L12Prompt prompt, string choice)
    {
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            ResolvePrompt(game, OnlyResponse(game), "pass");
    }

    private static L12CardInstance AddTrial(L12GameEngine game, int playerIndex, string instanceId,
        int progress = 0, bool completed = false)
    {
        var trial = Card("S02-06S4", instanceId, playerIndex);
        trial.TrialProgress = progress;
        trial.TrialCompleted = completed;
        game.State.Players[playerIndex].SpecialZones.Trials.Add(trial);
        game.State.Players[playerIndex].SpecialZones.TrialLevel = progress;
        return trial;
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-death-trial-advance-spec")]
    public void TwoExactSingleTrialAdvanceDeathEffectsUseOneParameterizedProgramShape()
    {
        var expectedCardIds = new[] { "S02-0609", "ST06-06" };
        Assert.Equal(expectedCardIds, L12SimpleTrialAdvanceTriggerEffects.All.Select(spec => spec.CardId));

        var nonSettlementKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            L12AtomKinds.Trigger,
            L12AtomKinds.Condition,
            L12AtomKinds.Optional,
            L12AtomKinds.Legacy,
            L12AtomKinds.CompositeFlow,
        };
        var catalogMatches = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .Where(ability => ability.Trigger == "death")
            .Where(ability =>
            {
                var settlement = ability.Atoms.Where(atom => !nonSettlementKinds.Contains(atom.Kind)).ToArray();
                return settlement.Length == 1 && settlement[0].Kind == L12AtomKinds.AdvanceTrial;
            })
            .Select(ability => ability.CardId)
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedCardIds, catalogMatches);

        foreach (var spec in L12SimpleTrialAdvanceTriggerEffects.All)
        {
            var ability = Catalog.AtomicEffects.Find(spec.CardId)!.Abilities
                .Single(item => item.Sequence == spec.AbilitySequence);
            Assert.Equal("death", ability.Trigger);
            Assert.Equal(spec.SettlementText,
                Assert.Single(ability.Presentations, scene =>
                    scene.Flow == L12SingleSegmentTriggeredEffectPresentations.Flow).DefaultText);

            var program = Assert.IsType<L12VerifiedAtomicProgram>(
                L12VerifiedAtomicPrograms.Find(spec.CardId, spec.Trigger));
            Assert.Single(program.Atoms, atom => atom.Kind == L12AtomKinds.Trigger);
            var advance = Assert.Single(program.Atoms, atom => atom.Kind == L12AtomKinds.AdvanceTrial);
            Assert.DoesNotContain(program.Atoms,
                atom => atom.Kind is not L12AtomKinds.Trigger and not L12AtomKinds.AdvanceTrial);
            Assert.Equal(spec.Amount.ToString(), advance.Parameters["amount"]);
            Assert.Equal(spec.SettlementText,
                L12GameEngine.ResolveTriggeredEffectDisplayText(
                    Card(spec.CardId, $"display-{spec.CardId}"), spec.Trigger, "【阵亡时】效果"));
        }
    }

    [Theory]
    [InlineData("S02-0609", 1)]
    [InlineData("ST06-06", 2)]
    [Trait("L12Evidence", "entry:simple-death-trial-advance-amount")]
    public void MandatoryDeathEffectAdvancesTheControllersCurrentTrialByItsDeclaredAmount(
        string cardId, int amount)
    {
        var game = Create(10200 + amount);
        var trial = AddTrial(game, 1, $"amount-trial-{cardId}", progress: 2);
        QueueDeath(game, cardId, controller: 1, owner: 0);

        Assert.Empty(game.State.PendingActivations);
        var trigger = Assert.Single(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.Cards.Any(card => card.CardId == cardId));
        PassResponses(game);

        Assert.Equal(2 + amount, trial.TrialProgress);
        Assert.Equal(2 + amount, game.State.Players[1].SpecialZones.TrialLevel);
        Assert.Equal(0, game.State.Players[0].SpecialZones.TrialLevel);
        var result = Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == cardId));
        Assert.Equal("resolved", result.EffectResultStatus);
        Assert.Equal(trigger.EffectSceneId, result.EffectSceneId);
        Assert.Equal(trigger.EffectText, result.EffectText);
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-death-trial-advance-empty")]
    public void NoIncompleteTrialSilentlyResolvesWithoutCreatingAChoiceOrFalseProgressLog()
    {
        var game = Create(10210);
        AddTrial(game, 0, "completed-trial", progress: 8, completed: true);
        QueueDeath(game, "S02-0609");
        Assert.Empty(game.State.PendingActivations);
        PassResponses(game);

        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "trial");
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == "S02-0609"));
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-death-trial-advance-current")]
    public void CompletedTrialIsSkippedAndOnlyTheFirstIncompleteTrialAdvancesWithCapEight()
    {
        var game = Create(10211);
        var completed = AddTrial(game, 0, "completed-first", progress: 8, completed: true);
        var current = AddTrial(game, 0, "current-second", progress: 7);
        var later = AddTrial(game, 0, "later-third", progress: 3);
        QueueDeath(game, "ST06-06");
        PassResponses(game);

        Assert.Equal(8, completed.TrialProgress);
        Assert.Equal(8, current.TrialProgress);
        Assert.Equal(3, later.TrialProgress);
        Assert.Equal(8, game.State.Players[0].SpecialZones.TrialLevel);
        Assert.Contains(game.State.Events, entry => entry.Type == "trial"
            && entry.Text.Contains("7 → 8", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-death-trial-advance-negated")]
    public void NegatedMandatoryTrialAdvanceDoesNotChangeProgress()
    {
        var game = Create(10212);
        var trial = AddTrial(game, 0, "negated-trial", progress: 2);
        QueueDeath(game, "ST06-06");
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.Equal(2, trial.TrialProgress);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "negated"
            && entry.Cards.Any(card => card.CardId == "ST06-06"));
    }

    [Fact]
    [Trait("L12Evidence", "entry:simple-death-trial-advance-reconnect")]
    public void MandatoryTrialAdvanceRestoresDuringResponseAndRejectsTheCompletedPrompt()
    {
        var game = Create(10213);
        AddTrial(game, 0, "restored-trial", progress: 2);
        QueueDeath(game, "ST06-06");
        var oldResponse = OnlyResponse(game);
        Assert.Contains(oldResponse.PromptId, JsonSerializer.Serialize(game.SnapshotFor(oldResponse.PlayerIndex)),
            StringComparison.Ordinal);
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");

        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        PassResponses(game);

        Assert.Equal(4, Assert.Single(game.State.Players[0].SpecialZones.Trials).TrialProgress);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == "ST06-06"));
        Assert.False(game.Handle(oldResponse.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: oldResponse.PromptId, Choice: "pass")).Accepted);
    }
}
