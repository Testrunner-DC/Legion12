using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6BRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 7620, string? masterId = null)
    {
        var baseDeck = Catalog.DeckAt(0);
        var decks = masterId is null
            ? new[] { baseDeck, baseDeck }
            : new[]
            {
                new L12PresetDeckDefinition
                {
                    Name = "Batch6B",
                    MasterId = masterId,
                    CardIds = [.. baseDeck.CardIds],
                    MoraleIds = [.. baseDeck.MoraleIds],
                    SpecialIds = [],
                },
                baseDeck,
            };
        var game = new L12GameEngine(Catalog, "atomic-review-batch6b", "ATOMIC6B", seed,
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
            player.Morale.Clear();
            player.Resolving.Clear();
            player.SpecialZones.Trials.Clear();
            player.SpecialZones.Runes = 0;
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
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

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
            Resolve(game, "pass");
    }

    private static L12CardInstance BeginCompletion(L12GameEngine game, string trialCardId, string instanceId)
    {
        var trial = Card(trialCardId, instanceId);
        trial.TrialProgress = 8;
        game.State.Players[0].SpecialZones.Trials.Add(trial);
        var result = game.Handle(0,
            new L12Command("activateAbility", trial.InstanceId, Ability: "completeTrial"));
        Assert.True(result.Accepted, result.Error);
        PassResponses(game);
        Assert.True(trial.TrialCompleted);
        return trial;
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-06S4")]
    [Trait("L12Evidence", "entry:trial-completion-hidden-library-delay")]
    public void GrailTrialDeclaresOnlyPublicModeBeforeStackAndRevealsLibraryIdentityAtResolution()
    {
        var game = Create(7621);
        var player = game.State.Players[0];
        var otherworld = Card("S02-0604", "batch6b-hidden-otherworld");
        player.Library.Add(otherworld);

        BeginCompletion(game, "S02-06S4", "batch6b-grail");

        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", mode.Continuation);
        Assert.Contains("mode:none", mode.ValidChoices);
        Assert.Contains("mode:use", mode.ValidChoices);
        Assert.DoesNotContain(otherworld.InstanceId, mode.ValidChoices);
        Assert.DoesNotContain(game.State.Events, entry => entry.Text.Contains(otherworld.InstanceId, StringComparison.Ordinal));
        Resolve(game, "mode:use");
        Assert.Single(game.State.EffectStack);

        PassResponses(game);
        var hiddenChoice = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trial-completion-library-search", hiddenChoice.Data["action"]);
        Assert.Contains(otherworld.InstanceId, hiddenChoice.ValidChoices);
        Resolve(game, otherworld.InstanceId);

        Assert.Contains(otherworld, player.Hand);
        Assert.DoesNotContain(otherworld, player.Library);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-06S3")]
    [Trait("L12Evidence", "entry:trial-completion-independent-mandatory-segments")]
    public void LakeLadyDeclineStartsWithMandatoryShuffleAndItsNegationStillContinuesToDiscount()
    {
        var game = Create(7622);
        var player = game.State.Players[0];
        var arthur = Card("S02-0601", "batch6b-grave-arthur");
        player.Graveyard.Add(arthur);

        BeginCompletion(game, "S02-06S3", "batch6b-lake-lady");
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:none", mode.ValidChoices);
        Assert.Contains("mode:grave", mode.ValidChoices);
        Resolve(game, "mode:none");

        var mandatoryShuffle = Assert.Single(game.State.EffectStack);
        Assert.Equal("1", mandatoryShuffle.Data["trialSegment"]);
        Assert.Contains("返回牌库并重洗", mandatoryShuffle.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(game.State.EffectStack,
            item => item.Data.GetValueOrDefault("trialSegment") == "0");
        mandatoryShuffle.Negated = true;
        PassResponses(game);

        Assert.Contains(arthur, player.Graveyard);
        Assert.DoesNotContain(arthur, player.Library);
        Assert.Equal(game.State.TurnSerial, player.S2ArthurDiscountUntilTurn);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-06S5")]
    [Trait("L12Evidence", "entry:trial-completion-colon-cost-pre-stack")]
    public void FenianTrialPrepaysXRunesAndQueuesRepeatableTargetsAsIndependentSegments()
    {
        var game = Create(7623);
        var player = game.State.Players[0];
        var enemy = Card("S02-0302", "batch6b-repeat-target");
        player.SpecialZones.Runes = 3;
        game.State.Players[1].Field[0][0] = enemy;

        BeginCompletion(game, "S02-06S5", "batch6b-fenian");
        Resolve(game, "mode:use");
        var amount = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("rune-count:3", amount.ValidChoices);
        Resolve(game, "rune-count:3");
        Resolve(game, enemy.InstanceId);
        Resolve(game, enemy.InstanceId);
        Resolve(game, enemy.InstanceId);

        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Single(game.State.EffectStack);
        game.State.EffectStack[0].Negated = true;
        PassResponses(game);

        Assert.Equal(enemy.BaseTroops - 6000, enemy.Troops);
        Assert.Equal(0, player.SpecialZones.Runes);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-06S5")]
    [Trait("L12Evidence", "entry:trial-completion-source-snapshot-target-loss")]
    public void FenianTargetLossCancelsOnlyItsSegmentAndNeverRefundsPrepaidRunes()
    {
        var game = Create(76231);
        var player = game.State.Players[0];
        var first = Card("S02-0302", "batch6b-lost-target", 10000);
        var second = Card("S02-0303", "batch6b-valid-target", 10000);
        player.SpecialZones.Runes = 2;
        game.State.Players[1].Field[0][0] = first;
        game.State.Players[1].Field[0][1] = second;

        var trial = BeginCompletion(game, "S02-06S5", "batch6b-fenian-snapshot");
        Resolve(game, "mode:use");
        Resolve(game, "rune-count:2");
        Resolve(game, first.InstanceId);
        Resolve(game, second.InstanceId);
        Assert.Equal(0, player.SpecialZones.Runes);

        player.SpecialZones.Trials.Remove(trial);
        game.State.Players[1].Field[0][0] = null;
        game.State.Players[1].Graveyard.Add(first);
        PassResponses(game);

        Assert.Equal(first.BaseTroops, first.Troops);
        Assert.Equal(second.BaseTroops - 3000, second.Troops);
        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("已支付符文不返还", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-06S3")]
    [Trait("L12Evidence", "entry:trial-completion-library-identity-delay")]
    public void LakeLadyLibraryArthurIdentityIsChosenOnlyAfterItsDeclaredSegmentStarts()
    {
        var game = Create(76232);
        var player = game.State.Players[0];
        var arthur = Card("S02-0601", "batch6b-library-arthur");
        player.Library.Add(arthur);

        BeginCompletion(game, "S02-06S3", "batch6b-lake-library");
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:library", mode.ValidChoices);
        Assert.DoesNotContain(arthur.InstanceId, mode.ValidChoices);
        Resolve(game, "mode:library");
        PassResponses(game);

        var hidden = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trial-completion-library-arthur", hidden.Data["action"]);
        Assert.True(hidden.IsPrivate);
        Assert.Contains(arthur.InstanceId, hidden.ValidChoices);
        Resolve(game, arthur.InstanceId);
        PassResponses(game);

        Assert.Contains(arthur, player.Hand);
        Assert.Equal(game.State.TurnSerial, player.S2ArthurDiscountUntilTurn);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-06M2")]
    [Trait("L12Evidence", "entry:trial-completion-trigger-batch")]
    public void PrintedTrialAndAngusAreSeparateSameTimeCandidatesChosenByTheirOwner()
    {
        var game = Create(7624, "S02-06M2");

        BeginCompletion(game, "S02-06S4", "batch6b-angus-grail");

        Assert.Equal(0, game.State.Players[0].SpecialZones.Runes);
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-order", order.Kind);
        Assert.Equal(2, order.ValidChoices.Count);
        Assert.Contains(order.ValidChoices, id => order.Data[id].Contains("安格斯", StringComparison.Ordinal));
        Assert.Contains(order.ValidChoices, id => order.Data[id].Contains("寻找圣杯", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-06M2")]
    [Trait("L12Evidence", "entry:trial-completion-optional-master-trigger")]
    public void AngusRuneGainIsOptionalAndResolvesFromItsOwnStackItem()
    {
        var game = Create(7625, "S02-06M2");
        BeginCompletion(game, "S02-06S4", "batch6b-angus-separate");
        var order = Assert.Single(game.State.PendingPrompts);
        var angus = order.ValidChoices.Single(id => order.Data[id].Contains("安格斯", StringComparison.Ordinal));
        var trial = order.ValidChoices.Single(id => id != angus);
        ResolveMany(game, trial, angus);

        Resolve(game, "mode:use");
        Assert.Equal(0, game.State.Players[0].SpecialZones.Runes);
        Assert.Single(game.State.EffectStack);
        Resolve(game, "mode:none");
        PassResponses(game);
        Assert.Equal(1, game.State.Players[0].SpecialZones.Runes);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "control:S02-06S6")]
    [Trait("L12Evidence", "entry:trial-completion-full-pool-control")]
    public void CrusadeTrialWithoutPrintedCompletionTriggerOnlyMarksCompletion()
    {
        var game = Create(7626);
        var trial = BeginCompletion(game, "S02-06S6", "batch6b-crusade-control");

        Assert.True(trial.TrialCompleted);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }
}
