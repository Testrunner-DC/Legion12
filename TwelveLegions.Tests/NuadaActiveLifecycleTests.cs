using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class NuadaActiveLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, bool withTrial = true)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "努阿达主动生命周期",
            MasterId = "ST06-M1",
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "nuada-active", "NUADA-ACTIVE", seed,
            ["甲", "乙"], [deck, baseDeck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        var player = game.State.Players[0];
        player.Hand.Clear();
        player.Library.Clear();
        player.Graveyard.Clear();
        player.Morale.Clear();
        player.SpecialZones.Trials.Clear();
        player.SpecialZones.Runes = 2;
        if (withTrial)
        {
            var definition = Catalog.Cards["ST06-S1"];
            player.SpecialZones.Trials.Add(new L12CardInstance
            {
                InstanceId = "nuada-trial", CardId = definition.Id, Name = definition.NameZh,
                CardType = definition.CardType, Faction = definition.Faction,
                ImageUrl = definition.ImageUrl, EffectText = definition.Effect, OwnerIndex = 0,
            });
        }
        return game;
    }

    private static L12MoraleCard Rested(string id) => new()
    {
        CardId = "ST06-C1", InstanceId = id, Tapped = true,
    };

    private static L12Prompt Begin(L12GameEngine game, params L12MoraleCard[] morale)
    {
        game.State.Players[0].Morale.AddRange(morale);
        var result = game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "nuadaReadyMorale"));
        Assert.True(result.Accepted, result.Error);
        return Assert.Single(game.State.PendingPrompts);
    }

    private static void Choose(L12GameEngine game, L12Prompt prompt, params string[] ids)
    {
        var result = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [.. ids]));
        Assert.True(result.Accepted, result.Error);
    }

    private static void DrainPrompts(L12GameEngine game)
    {
        var count = 0;
        while (game.State.PendingPrompts.FirstOrDefault() is { } prompt && count++ < 32)
        {
            L12Command command;
            if (prompt.Kind == "response")
                command = new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass");
            else if (prompt.Kind == "trigger-order")
                command = new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                    CardInstanceIds: prompt.ValidChoices.ToList());
            else if (prompt.ValidChoices.Contains("mode:none"))
                command = new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "mode:none");
            else break;
            var result = game.Handle(prompt.PlayerIndex, command);
            Assert.True(result.Accepted, result.Error);
        }
        Assert.True(count < 32, "努阿达相关Prompt未在限定次数内结束");
    }

    [Fact]
    public void PrintedSecondAbilityUsesTwoStructuredSettlementScenes()
    {
        var ability = Catalog.AtomicEffects.Find("ST06-M1")!.Abilities
            .Single(candidate => candidate.Sequence == 2);
        var scenes = ability.Presentations.Where(scene => scene.Flow is
            "nuada-ready-morale" or "nuada-trial-advance").ToArray();
        Assert.Equal(2, scenes.Length);
        Assert.Equal([1, 2], scenes.OrderBy(scene => scene.SegmentIndex).Select(scene => scene.SegmentIndex));
        Assert.All(scenes, scene => Assert.Equal(2, scene.SegmentCount));
    }

    [Fact]
    [Trait("L12Evidence", "ability:nuadaReadyMorale")]
    public void CostIsPaidBeforeResponsesAndNegationStopsBothEffectSegments()
    {
        var game = Create(91801);
        var morale = Rested("nuada-negated-morale");
        var prompt = Begin(game, morale);
        Choose(game, prompt, morale.InstanceId);

        Assert.Equal(0, game.State.Players[0].SpecialZones.Runes);
        Assert.True(morale.Tapped);
        Assert.Equal(0, game.State.Players[0].SpecialZones.Trials[0].TrialProgress);
        var stackItem = Assert.Single(game.State.EffectStack);
        Assert.Equal("消耗2符文", stackItem.Data["paidCostSummary"]);
        stackItem.Negated = true;
        DrainPrompts(game);

        Assert.True(morale.Tapped);
        Assert.Equal(0, game.State.Players[0].SpecialZones.Trials[0].TrialProgress);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "negated");
    }

    [Fact]
    [Trait("L12Evidence", "ability:nuadaReadyMorale")]
    public void ZeroMoraleTargetsSkipsOnlyTheFirstSegmentAndStillAdvancesTrial()
    {
        var game = Create(91802);
        var response = Begin(game);
        Assert.Equal("response", response.Kind);
        DrainPrompts(game);

        Assert.Equal(0, game.State.Players[0].SpecialZones.Runes);
        Assert.Equal(2, game.State.Players[0].SpecialZones.Trials[0].TrialProgress);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "skipped"
            && entry.EffectSegmentIndex == 1 && entry.EffectSegmentCount == 2);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.EffectSegmentIndex == 2 && entry.EffectSegmentCount == 2);
    }

    [Fact]
    [Trait("L12Evidence", "ability:nuadaReadyMorale")]
    public void ReverseSettlementReadiesOnlyStillRestedMoraleAndContinuesTrialSegment()
    {
        var game = Create(91803);
        var first = Rested("nuada-first");
        var second = Rested("nuada-second");
        var prompt = Begin(game, first, second);
        Choose(game, prompt, first.InstanceId, second.InstanceId);
        second.Tapped = false;
        DrainPrompts(game);

        Assert.False(first.Tapped);
        Assert.False(second.Tapped);
        Assert.Equal(2, game.State.Players[0].SpecialZones.Trials[0].TrialProgress);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("1张已声明士气", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:nuadaReadyMorale")]
    public void AllMoraleTargetsInvalidFailsOnlyTheFirstSegment()
    {
        var game = Create(91804);
        var morale = Rested("nuada-invalid");
        var prompt = Begin(game, morale);
        Choose(game, prompt, morale.InstanceId);
        morale.Tapped = false;
        DrainPrompts(game);

        Assert.Equal(2, game.State.Players[0].SpecialZones.Trials[0].TrialProgress);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "failed"
            && entry.EffectSegmentIndex == 1 && entry.EffectSegmentCount == 2);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.EffectSegmentIndex == 2 && entry.EffectSegmentCount == 2);
    }

    [Fact]
    [Trait("L12Evidence", "ability:nuadaReadyMorale")]
    public void MissingTrialSkipsTheSecondSegmentWithoutBlockingMoraleResolution()
    {
        var game = Create(91805, withTrial: false);
        var morale = Rested("nuada-no-trial");
        var prompt = Begin(game, morale);
        Choose(game, prompt, morale.InstanceId);
        DrainPrompts(game);

        Assert.False(morale.Tapped);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.EffectSegmentIndex == 1 && entry.EffectSegmentCount == 2);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "skipped"
            && entry.EffectSegmentIndex == 2 && entry.EffectSegmentCount == 2);
    }

    [Fact]
    [Trait("L12Evidence", "ability:nuadaReadyMorale")]
    public void V2RestoreCompletesBothFrozenSegmentsOnceAndRejectsOldPrompt()
    {
        var game = Create(91806);
        var morale = Rested("nuada-restored");
        var selection = Begin(game, morale);
        Choose(game, selection, morale.InstanceId);
        var oldResponse = Assert.Single(game.State.PendingPrompts);
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        Assert.Equal("消耗2符文", Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Kind == "response").Data["responsePaidCostSummary"]);
        DrainPrompts(game);

        Assert.False(game.State.Players[0].Morale.Single().Tapped);
        Assert.Equal(2, game.State.Players[0].SpecialZones.Trials[0].TrialProgress);
        Assert.False(game.Handle(oldResponse.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: oldResponse.PromptId, Choice: "pass")).Accepted);
        Assert.Equal(2, game.State.Events.Count(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == "ST06-M1")));
    }
}
