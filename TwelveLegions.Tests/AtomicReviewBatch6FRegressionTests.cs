using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6FRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static readonly string[] TrialLegionIds =
    [
        "S02-0604", "S02-0606", "S02-0609", "S02-0610",
        "S02-0613", "S02-0614", "S02-0617", "S02-0618",
    ];

    private static L12GameEngine Create(int seed, string? masterId = null)
    {
        var baseDeck = Catalog.DeckAt(0);
        var decks = masterId is null
            ? new[] { baseDeck, baseDeck }
            : new[]
            {
                new L12PresetDeckDefinition
                {
                    Name = "Batch6F",
                    MasterId = masterId,
                    CardIds = [.. baseDeck.CardIds],
                    MoraleIds = [.. baseDeck.MoraleIds],
                    SpecialIds = [.. baseDeck.SpecialIds],
                },
                baseDeck,
            };
        var game = new L12GameEngine(Catalog, "atomic-review-batch6f", "ATOMIC6F", seed,
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
            player.UsedAbilities.Clear();
            player.SpecialZones.Trials.Clear();
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
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
            OwnerIndex = 0,
        };
    }

    private static L12CardInstance AddOpenTrial(L12GameEngine game, int progress = 0)
    {
        var trial = Card("S02-06S4", $"batch6f-trial-{game.State.TurnSerial}-{progress}");
        trial.TrialProgress = progress;
        game.State.Players[0].SpecialZones.Trials.Add(trial);
        return trial;
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
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
            Resolve(game, "pass");
    }

    private static void QueueTrigger(L12GameEngine game, L12CardInstance source, string trigger,
        Dictionary<string, string>? data = null)
    {
        var method = typeof(L12GameEngine).GetMethod("QueueOrPushTriggeredEffect",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(game, [0, source, trigger, "Batch6F测试触发", null, data]);
    }

    private static void QueueAngusTactic(L12GameEngine game, L12CardInstance tactic)
    {
        var method = typeof(L12GameEngine).GetMethod("QueueS2AngusTacticTrial",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(game, [0, tactic]);
    }

    public static IEnumerable<object[]> TrialLegions()
        => TrialLegionIds.Select(cardId => new object[] { cardId });

    [Theory]
    [MemberData(nameof(TrialLegions))]
    [Trait("L12Evidence", "entry:trial-advance-public-event")]
    public void EveryTrialValueLegionPaysRestBeforeARespondableAdvanceEffect(string cardId)
    {
        var game = Create(8000 + Array.IndexOf(TrialLegionIds, cardId));
        var player = game.State.Players[0];
        var legion = Card(cardId, $"batch6f-generic-{cardId}");
        var trial = AddOpenTrial(game);
        player.Field[0][0] = legion;

        var result = game.Handle(0, new L12Command("activateAbility", legion.InstanceId,
            Ability: "trialAdvance"));

        Assert.True(result.Accepted, result.Error);
        Assert.True(legion.Tapped);
        Assert.Equal(0, trial.TrialProgress);
        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal("true", item.Data["trialAdvanceEvent"]);
        Assert.Equal(legion.TrialValue.ToString(), item.Data["trialAdvanceCount"]);
        Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
    }

    [Fact]
    [Trait("L12Evidence", "entry:trial-advance-negated-cost-not-refunded")]
    public void NegatedTrialAdvanceKeepsItsRestCostAndDoesNotPublishFinnFollowUp()
    {
        var game = Create(8010);
        var player = game.State.Players[0];
        var finn = Card("S02-0610", "batch6f-negated-finn");
        var trial = AddOpenTrial(game);
        player.Field[0][0] = finn;
        player.SpecialZones.Runes = 1;

        Assert.True(game.Handle(0, new L12Command("activateAbility", finn.InstanceId,
            Ability: "trialAdvance")).Accepted);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(finn.Tapped);
        Assert.Equal(0, trial.TrialProgress);
        Assert.Equal(1, player.SpecialZones.Runes);
        Assert.Empty(game.State.PendingActivations);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Text.Contains("芬恩", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0610")]
    [Trait("L12Evidence", "entry:finn-independent-ready-trigger")]
    public void FinnReadyIsASeparateTriggerThatPrepaysRuneAndRechecksTheSource()
    {
        var game = Create(8011);
        var player = game.State.Players[0];
        var finn = Card("S02-0610", "batch6f-finn-follow-up");
        var trial = AddOpenTrial(game);
        player.Field[0][0] = finn;
        player.SpecialZones.Runes = 1;

        Assert.True(game.Handle(0, new L12Command("activateAbility", finn.InstanceId,
            Ability: "trialAdvance")).Accepted);
        PassResponses(game);
        Assert.Equal(1, trial.TrialProgress);
        Assert.True(finn.Tapped);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains("mode:use", declaration.ValidChoices);
        Resolve(game, "mode:use");

        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.True(finn.Tapped);
        Assert.Single(game.State.EffectStack);
        player.Field[0][0] = null;
        player.Graveyard.Add(finn);
        PassResponses(game);

        Assert.True(finn.Tapped);
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.DoesNotContain($"trial-card-lock:{finn.InstanceId}:{game.State.TurnSerial}", player.UsedAbilities);
    }

    public static IEnumerable<object[]> EntryPlans()
    {
        yield return ["S02-0602", new[] { "mode:none", "mode:use" }];
        yield return ["S02-0604", new[] { "mode:none", "mode:trial" }];
        yield return ["S02-0610", new[] { "mode:none", "mode:trial" }];
        yield return ["S02-0614", new[] { "mode:none", "mode:rune", "mode:trial" }];
    }

    [Theory]
    [MemberData(nameof(EntryPlans))]
    [Trait("L12Evidence", "entry:trial-entry-public-declaration")]
    public void TrialClusterEntryChoicesAreDeclaredBeforeAnyStackItem(string cardId, string[] modes)
    {
        var game = Create(8020 + cardId[^1]);
        var player = game.State.Players[0];
        var source = Card(cardId, $"batch6f-entry-{cardId}");
        player.Field[0][0] = source;
        player.SpecialZones.Runes = 1;
        AddOpenTrial(game);

        QueueTrigger(game, source, "enter");

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Empty(game.State.EffectStack);
        Assert.All(modes, mode => Assert.Contains(mode, declaration.ValidChoices));
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "card-effect");
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0604")]
    [Trait("L12Evidence", "entry:entry-trial-rest-cost-pre-stack")]
    public void GalahadEntryPrepaysRestAndNegationDoesNotRefundIt()
    {
        var game = Create(8030);
        var player = game.State.Players[0];
        var galahad = Card("S02-0604", "batch6f-galahad");
        var trial = AddOpenTrial(game);
        player.Field[0][0] = galahad;

        QueueTrigger(game, galahad, "enter");
        Resolve(game, "mode:trial");

        Assert.True(galahad.Tapped);
        Assert.Equal(0, trial.TrialProgress);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);
        Assert.True(galahad.Tapped);
        Assert.Equal(0, trial.TrialProgress);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0602")]
    [Trait("L12Evidence", "entry:lancelot-entry-rune-prepaid")]
    public void LancelotEntryPrepaysRuneAndSourceLossOnlyCancelsCharge()
    {
        var game = Create(8031);
        var player = game.State.Players[0];
        var lancelot = Card("S02-0602", "batch6f-lancelot-entry");
        player.Field[0][0] = lancelot;
        player.SpecialZones.Runes = 1;

        QueueTrigger(game, lancelot, "enter");
        Resolve(game, "mode:use");

        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Single(game.State.EffectStack);
        player.Field[0][0] = null;
        player.Graveyard.Add(lancelot);
        PassResponses(game);

        Assert.False(lancelot.HasCharge);
        Assert.Equal(0, player.SpecialZones.Runes);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0602")]
    [Trait("L12Evidence", "entry:lancelot-kill-mode-source-snapshot")]
    public void LancelotKillDeclaresModeBeforeStackAndGlobalChoiceSurvivesSourceLoss()
    {
        var game = Create(8032);
        var player = game.State.Players[0];
        var lancelot = Card("S02-0602", "batch6f-lancelot-kill");
        var trial = AddOpenTrial(game);
        player.Field[0][0] = lancelot;

        QueueTrigger(game, lancelot, "after-attack", new Dictionary<string, string>
        {
            ["killed"] = "true",
            ["combatKillConfirmed"] = "true",
        });
        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains("mode:trial", declaration.ValidChoices);
        Assert.Contains("mode:rune", declaration.ValidChoices);
        Resolve(game, "mode:trial");

        player.Field[0][0] = null;
        player.Graveyard.Add(lancelot);
        PassResponses(game);
        Assert.Equal(1, trial.TrialProgress);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-06M2")]
    [Trait("L12Evidence", "entry:angus-mandatory-once-trigger")]
    public void AngusTacticSuccessIsMandatoryAndNegationStillConsumesItsOnce()
    {
        var game = Create(8040, "S02-06M2");
        var player = game.State.Players[0];
        var trial = AddOpenTrial(game);
        var tactic = Card("S01-0005", "batch6f-angus-tactic");

        QueueAngusTactic(game, tactic);
        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal("true", item.Data["trialAdvanceEvent"]);
        Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
        item.Negated = true;
        PassResponses(game);

        Assert.Equal(0, trial.TrialProgress);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Text.Contains("是否使试炼", StringComparison.Ordinal));
        QueueAngusTactic(game, tactic);
        Assert.Empty(game.State.EffectStack);
        Assert.Contains($"trigger:angus-tactic:{game.State.TurnSerial}", player.UsedAbilities);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-06D1")]
    [Trait("L12Evidence", "entry:avalon-turn-start-respondable")]
    public void AvalonTurnStartQueuesOneRespondableCombinedEffectBeforeChangingState()
    {
        var game = Create(8041, "S02-06D1");
        var player = game.State.Players[0];
        var trial = AddOpenTrial(game);
        game.State.Phase = L12Phase.Mulligan;
        game.State.Players[0].MulliganDone = false;
        game.State.Players[1].MulliganDone = false;

        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);

        Assert.Equal(0, trial.TrialProgress);
        Assert.Equal(0, player.SpecialZones.Runes);
        var item = Assert.Single(game.State.EffectStack);
        Assert.Equal("turn-start", item.Trigger);
        Assert.Equal("true", item.Data["trialAdvanceEvent"]);
        item.Negated = true;
        PassResponses(game);
        Assert.Equal(0, trial.TrialProgress);
        Assert.Equal(0, player.SpecialZones.Runes);
    }

    [Fact]
    [Trait("L12Evidence", "entry:trial-progress-is-not-trial-completion")]
    public void ReachingEightByAdvanceDoesNotPublishTheBatch6BCompletionEvent()
    {
        var game = Create(8042, "S02-06M2");
        var player = game.State.Players[0];
        var galahad = Card("S02-0604", "batch6f-eight-galahad");
        var trial = AddOpenTrial(game, progress: 7);
        player.Field[0][0] = galahad;

        Assert.True(game.Handle(0, new L12Command("activateAbility", galahad.InstanceId,
            Ability: "trialAdvance")).Accepted);
        PassResponses(game);

        Assert.Equal(8, trial.TrialProgress);
        Assert.False(trial.TrialCompleted);
        Assert.DoesNotContain(game.State.PendingTriggerStackCandidates,
            candidate => candidate.Trigger == "trial-complete");
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Text.Contains("完成试炼", StringComparison.Ordinal));
    }
}
