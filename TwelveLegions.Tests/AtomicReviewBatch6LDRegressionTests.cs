using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6LDRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static readonly IReadOnlyDictionary<string, int> AuditedAbilityCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["S02-0601"] = 2, ["S02-0602"] = 5, ["S02-0603"] = 5, ["S02-0604"] = 3,
            ["S02-0605"] = 4, ["S02-0606"] = 5, ["S02-0607"] = 2, ["S02-0608"] = 4,
            ["S02-0609"] = 3, ["S02-0610"] = 3, ["S02-0611"] = 5, ["S02-0612"] = 4,
            ["S02-0613"] = 3, ["S02-0614"] = 5, ["S02-0615"] = 3, ["S02-0616"] = 3,
            ["S02-0617"] = 4, ["S02-0618"] = 3, ["S02-0619"] = 2, ["S02-0620"] = 2,
            ["S02-0621"] = 2, ["S02-0622"] = 2, ["S02-06C1"] = 2, ["S02-06D1"] = 4,
            ["S02-06M1"] = 3, ["S02-06M2"] = 1, ["S02-06S1"] = 1, ["S02-06S2"] = 1,
            ["S02-06S3"] = 3, ["S02-06S4"] = 3, ["S02-06S5"] = 2, ["S02-06S6"] = 1,
            ["S02-DS01"] = 1, ["S02-DS02"] = 2, ["S02-DS03"] = 3, ["S02-DS04"] = 2,
            ["S02-DS05"] = 3, ["S02-DS06"] = 2,
        };

    private static L12GameEngine Create(int seed = 8701, string firstMaster = "S02-06M1")
    {
        var baseDeck = Catalog.DeckAt(0);
        var firstDeck = new L12PresetDeckDefinition
        {
            Name = $"{firstMaster}第六批6L-D审查牌库",
            MasterId = firstMaster,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "atomic-review-batch6ld", "ATOMIC6LD", seed,
            ["甲", "乙"], [firstDeck, baseDeck], skipPreparation: true,
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
            player.MoraleDeck.Clear();
            player.Resolving.Clear();
            player.ExtraRelics.Clear();
            player.Relic = null;
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
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
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

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 120 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
    }

    private static void AddReadyMorale(L12PlayerState player, int count, string prefix)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S02-06C1", InstanceId = $"{prefix}-{index}",
            });
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
    [Trait("L12Evidence", "batch:6L-D-inventory")]
    public void S2OtherworldAndDisasterAuditFreezesEveryCardAndAbility()
    {
        Assert.Equal(38, AuditedAbilityCounts.Count);
        Assert.Equal(108, AuditedAbilityCounts.Values.Sum());
        Assert.All(AuditedAbilityCounts, pair =>
        {
            var card = Assert.Contains(pair.Key, Catalog.Cards);
            Assert.True(card.Faction == "otherworld" || card.Id.StartsWith("S02-DS", StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(card.Effect));
            Assert.Equal(pair.Value, Catalog.AtomicEffects.Find(pair.Key)?.Abilities.Count);
        });
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0603")]
    [Trait("L12Evidence", "entry:merlin-hidden-library-delay")]
    public void MerlinDeclaresOnlyPublicModeBeforeTheSearchEffectLegallyStarts()
    {
        var game = Create(8702);
        var player = game.State.Players[0];
        var merlin = Card("S02-0603", "batch6ld-merlin");
        var hiddenTactic = Card("S01-0005", "batch6ld-merlin-hidden-tactic");
        player.Field[0][0] = merlin;
        player.Library.Add(hiddenTactic);
        player.SpecialZones.Runes = 1;

        var begin = game.Handle(0, new L12Command("activateAbility", merlin.InstanceId, Ability: "merlinRune"));
        Assert.True(begin.Accepted, begin.Error);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:search", mode.ValidChoices);
        Assert.DoesNotContain(hiddenTactic.InstanceId, mode.ValidChoices);

        Resolve(game, "mode:search");

        Assert.Empty(game.State.PendingActivations);
        Assert.True(merlin.Tapped);
        Assert.Equal(0, player.SpecialZones.Runes);
        PassResponses(game);
        var hidden = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-merlin-search", hidden.Data["action"]);
        Assert.True(hidden.IsPrivate);
        Assert.Contains(hiddenTactic.InstanceId, hidden.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-06S4")]
    [Trait("L12Evidence", "entry:trial-completion-hidden-library-existence")]
    public void GrailCompletionOffersPublicUseModeWithoutPeekingForAHiddenMatch()
    {
        var game = Create(8703);
        game.State.Players[0].Library.Add(Card("S02-0401", "batch6ld-grail-no-match"));

        BeginCompletion(game, "S02-06S4", "batch6ld-grail");

        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:none", mode.ValidChoices);
        Assert.Contains("mode:use", mode.ValidChoices);
        Resolve(game, "mode:use");
        PassResponses(game);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("未命中", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "cards:S02-06S4,S02-0008")]
    public void GrailHiddenSearchRecognizesARingUniversalLegionOnlyAtResolution()
    {
        var game = Create(8710);
        var player = game.State.Players[0];
        var universal = Card("S02-0003", "batch6ld-grail-ring-universal");
        player.Relic = Card("S02-0008", "batch6ld-grail-ring");
        player.Library.Add(universal);

        BeginCompletion(game, "S02-06S4", "batch6ld-grail-ring-trial");
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.DoesNotContain(universal.InstanceId, mode.ValidChoices);
        Resolve(game, "mode:use");
        PassResponses(game);

        var hidden = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trial-completion-library-search", hidden.Data["action"]);
        Assert.Contains(universal.InstanceId, hidden.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "cards:S02-0605,S02-0008")]
    public void BorsDiscountCountsAUniversalLegionWhileTheRingIsActive()
    {
        var game = Create(8704);
        var player = game.State.Players[0];
        var bors = Card("S02-0605", "batch6ld-bors");
        player.Relic = Card("S02-0008", "batch6ld-bors-ring");
        player.Field[0][0] = Card("S02-0003", "batch6ld-bors-universal");
        player.Hand.Add(bors);
        AddReadyMorale(player, bors.Cost - 1, "batch6ld-bors-cost");

        var play = game.Handle(0, new L12Command("playCard", bors.InstanceId, Row: 0, Slot: 1));

        Assert.True(play.Accepted, play.Error);
    }

    [Fact]
    [Trait("L12Evidence", "cards:S02-06M1,S02-0008")]
    public void MorriganTargetIncludesAUniversalLegionWhileTheRingIsActive()
    {
        var game = Create(8705);
        var player = game.State.Players[0];
        var universal = Card("S02-0003", "batch6ld-morrigan-universal");
        player.Relic = Card("S02-0008", "batch6ld-morrigan-ring");
        player.Field[0][0] = universal;
        player.SpecialZones.Runes = 2;

        var begin = game.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "morriganReadyOnKill"));

        Assert.True(begin.Accepted, begin.Error);
        Assert.Contains(universal.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "cards:S02-06S5,S02-0008")]
    public void FenianReadyIncludesARingUniversalLegionWithPrintedTroopsAtMostFourThousand()
    {
        var game = Create(8706);
        var player = game.State.Players[0];
        var trial = Card("S02-06S5", "batch6ld-fenian-trial");
        var universal = Card("S02-0003", "batch6ld-fenian-universal");
        universal.Tapped = true;
        trial.TrialCompleted = true;
        player.SpecialZones.Trials.Add(trial);
        player.Relic = Card("S02-0008", "batch6ld-fenian-ring");
        player.Field[0][0] = universal;
        player.SpecialZones.Runes = 1;

        var begin = game.Handle(0,
            new L12Command("activateAbility", trial.InstanceId, Ability: "fenianReady"));

        Assert.True(begin.Accepted, begin.Error);
        Assert.Contains(universal.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "cards:S02-0620,S02-0008")]
    public void RunePowerHiddenTopSearchRecognizesARingUniversalCardAtResolution()
    {
        var game = Create(8707);
        var player = game.State.Players[0];
        var tactic = Card("S02-0620", "batch6ld-rune-power");
        var universal = Card("S02-0003", "batch6ld-rune-power-universal");
        player.Relic = Card("S02-0008", "batch6ld-rune-power-ring");
        player.Hand.Add(tactic);
        player.Library.AddRange([
            universal,
            Card("S02-0401", "batch6ld-rune-power-filler-a"),
            Card("S02-0402", "batch6ld-rune-power-filler-b"),
        ]);
        AddReadyMorale(player, tactic.Cost + 1, "batch6ld-rune-power-cost");

        var play = game.Handle(0, new L12Command("playCard", tactic.InstanceId));
        Assert.True(play.Accepted, play.Error);
        Resolve(game, "mode:search");
        var payment = Assert.Single(game.State.PendingPrompts);
        var paid = game.Handle(0, new L12Command("resolvePrompt", PromptId: payment.PromptId,
            CardInstanceIds: payment.ValidChoices.Take(1).ToList()));
        Assert.True(paid.Accepted, paid.Error);
        PassResponses(game);

        var hidden = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-rune-power-pick", hidden.Data["action"]);
        Assert.Contains(universal.InstanceId, hidden.ValidChoices);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-06S6")]
    [Trait("L12Evidence", "entry:crusade-shared-once")]
    public void CrusadeThreeModesShareOnePrintedOncePerTurnLimit()
    {
        var game = Create(8708);
        var player = game.State.Players[0];
        var trial = Card("S02-06S6", "batch6ld-crusade");
        var galahad = Card("S02-0604", "batch6ld-crusade-galahad");
        var richard = Card("S02-0608", "batch6ld-crusade-richard");
        trial.TrialCompleted = true;
        player.SpecialZones.Trials.Add(trial);
        player.Field[0][0] = galahad;
        player.Field[0][1] = richard;
        player.SpecialZones.Runes = 3;

        var first = game.Handle(0,
            new L12Command("activateAbility", trial.InstanceId, Ability: "crusadeTrialNoLoss"));
        Assert.True(first.Accepted, first.Error);
        Resolve(game, galahad.InstanceId);
        PassResponses(game);

        var second = game.Handle(0,
            new L12Command("activateAbility", trial.InstanceId, Ability: "crusadeRichardPiercing"));

        Assert.False(second.Accepted);
        Assert.Equal(2, player.SpecialZones.Runes);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Fact]
    [Trait("L12Evidence", "cards:S02-06S6,S02-0008")]
    public void CrusadeOnlyOtherworldRecoveryDoesNotTreatARingUniversalCardAsOnlyOtherworld()
    {
        var game = Create(8711);
        var player = game.State.Players[0];
        var trial = Card("S02-06S6", "batch6ld-crusade-only-trial");
        trial.TrialCompleted = true;
        player.SpecialZones.Trials.Add(trial);
        player.Relic = Card("S02-0008", "batch6ld-crusade-only-ring");
        player.Hand.Add(Card("S02-0401", "batch6ld-crusade-only-discard"));
        player.Graveyard.Add(Card("S02-0003", "batch6ld-crusade-only-universal"));
        player.SpecialZones.Runes = 2;

        var begin = game.Handle(0,
            new L12Command("activateAbility", trial.InstanceId, Ability: "crusadeRecover"));

        Assert.False(begin.Accepted);
        Assert.Contains("只有【彼界】特征", begin.Error);
        Assert.Equal(2, player.SpecialZones.Runes);
        Assert.Single(player.Hand);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0604")]
    [Trait("L12Evidence", "entry:galahad-prepaid-self-discard")]
    public void GalahadDeclaresOptionalHealAndPaysSelfDiscardBeforeItsStackItem()
    {
        var game = Create(8709);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var galahad = Card("S02-0604", "batch6ld-galahad");
        var trial = Card("S02-06S4", "batch6ld-galahad-trial");
        var counter = Card("S01-0019", "batch6ld-galahad-counter");
        trial.TrialCompleted = true;
        counter.Hidden = true;
        counter.SetRound = 0;
        player.SpecialZones.Trials.Add(trial);
        player.Field[0][0] = galahad;
        player.Library.Add(Card("S02-0401", "batch6ld-galahad-draw"));
        player.Hp = player.MaxHp - 1;
        opponent.Field[1][0] = counter;

        var begin = game.Handle(0,
            new L12Command("activateAbility", galahad.InstanceId, Ability: "galahadGrailReward"));

        Assert.True(begin.Accepted, begin.Error);
        Assert.Empty(game.State.EffectStack);
        Assert.Same(galahad, player.Field[0][0]);
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:none", mode.ValidChoices);
        Assert.Contains("mode:heal", mode.ValidChoices);

        Resolve(game, "mode:heal");
        Assert.Null(player.Field[0][0]);
        Assert.Contains(galahad, player.Graveyard);
        var stackItem = Assert.Single(game.State.EffectStack);
        stackItem.Negated = true;
        PassResponses(game);

        Assert.Contains(galahad, player.Graveyard);
        Assert.Equal(player.MaxHp - 1, player.Hp);
        Assert.Empty(player.Hand);
    }
}
