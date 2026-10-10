using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class PharaohFestivalRingFactionTests
{
    private sealed record FestivalCards(
        string Festival,
        string UniversalForHand,
        string UniversalForGrave,
        string PrintedSun,
        string OtherFaction,
        string FestivalInLibrary,
        string[] InitialTopOrder);

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(0, true, true)]
    [InlineData(1, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(1, true, true)]
    [Trait("L12Bug", "BUG-20260907-58aaeb7f")]
    public void WorldRingMakesBothFestivalSelectionsUseTheControllersEffectiveFaction(
        int controller, bool ringInExtraRelic, bool restoreAcrossStages)
    {
        var game = CreateGame(controller, 202609070 + controller * 100
            + (ringInExtraRelic ? 10 : 0) + (restoreAcrossStages ? 1 : 0));
        var cards = ArrangeFestivalTopFive(game, controller, includeSecondUniversal: true);
        var player = game.State.Players[controller];
        var ring = PharaohFestivalRingFactionFixture.Card("S02-0008", $"festival-ring-{controller}-{ringInExtraRelic}", controller);
        if (ringInExtraRelic) player.ExtraRelics.Add(ring);
        else player.Relic = ring;

        Assert.True(L12StructuredCardRules.HasFaction(player,
            player.Library.Single(card => card.InstanceId == cards.UniversalForHand), "taiyangcheng"));

        PlayFestival(game, controller, cards.Festival);
        AssertFestivalPrompt(game, "festival-hand", cards.InitialTopOrder,
            cards.UniversalForHand, cards.UniversalForGrave);

        if (restoreAcrossStages) game = PharaohFestivalRingFactionFixture.Restore(game);
        PharaohFestivalRingFactionFixture.Resolve(game, cards.UniversalForHand);

        AssertFestivalPrompt(game, "festival-grave",
            cards.InitialTopOrder.Where(id => id != cards.UniversalForHand), cards.UniversalForGrave);
        if (restoreAcrossStages) game = PharaohFestivalRingFactionFixture.Restore(game);
        PharaohFestivalRingFactionFixture.Resolve(game, cards.UniversalForGrave);

        if (restoreAcrossStages) game = PharaohFestivalRingFactionFixture.Restore(game);
        var orderPrompt = PharaohFestivalRingFactionFixture.Prompt(game);
        Assert.Equal("festival-bottom-order", orderPrompt.Data["action"]);
        Assert.Equal("all-bottom", orderPrompt.Data["placementMode"]);
        var bottomOrder = orderPrompt.ValidChoices.AsEnumerable().Reverse().ToArray();
        var orderResult = game.Handle(orderPrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: orderPrompt.PromptId,
                BottomCardInstanceIds: [.. bottomOrder]));
        Assert.True(orderResult.Accepted, orderResult.Error);

        player = game.State.Players[controller];
        Assert.Single(player.Hand, card => card.InstanceId == cards.UniversalForHand);
        Assert.Single(player.Graveyard, card => card.InstanceId == cards.UniversalForGrave);
        Assert.DoesNotContain(player.Library, card => card.InstanceId == cards.UniversalForHand
            || card.InstanceId == cards.UniversalForGrave);
        Assert.Equal(bottomOrder, player.Library.TakeLast(bottomOrder.Length).Select(card => card.InstanceId));
        Assert.Equal(1, CountPrivateZoneCopies(player, cards.UniversalForHand));
        Assert.Equal(1, CountPrivateZoneCopies(player, cards.UniversalForGrave));
        Assert.Single(game.State.Events, entry => entry.Type == "reveal"
            && entry.Cards.Any(card => card.InstanceId == cards.UniversalForHand));
        Assert.Single(game.State.Events, entry => entry.Type == "discard"
            && entry.Cards.Any(card => card.InstanceId == cards.UniversalForGrave));
    }

    [Fact]
    [Trait("L12Bug", "BUG-20260907-58aaeb7f")]
    public void UniversalCardsRemainIneligibleWhenNoWorldRingIsPresent()
    {
        var game = CreateGame(0, 202609071);
        var cards = ArrangeFestivalTopFiveWithTwoPrintedSunCards(game, 0);

        PlayFestival(game, 0, cards.Festival);
        var handPrompt = PharaohFestivalRingFactionFixture.Prompt(game);
        Assert.Equal("festival-hand", handPrompt.Data["action"]);
        Assert.DoesNotContain(cards.Universal, handPrompt.ValidChoices);
        Assert.Contains(cards.FirstPrintedSun, handPrompt.ValidChoices);
        Assert.Equal("只能选择【太阳城】卡牌，且不能选择〈法老王的庆典〉本身",
            handPrompt.Data[$"disabledChoice:{cards.Universal}"]);

        PharaohFestivalRingFactionFixture.Resolve(game, cards.FirstPrintedSun);
        var gravePrompt = PharaohFestivalRingFactionFixture.Prompt(game);
        Assert.Equal("festival-grave", gravePrompt.Data["action"]);
        Assert.DoesNotContain(cards.Universal, gravePrompt.ValidChoices);
        Assert.Contains(cards.SecondPrintedSun, gravePrompt.ValidChoices);
    }

    [Fact]
    [Trait("L12Bug", "BUG-20260907-58aaeb7f")]
    public void WorldRingOutsideRelicZonesDoesNotChangeFestivalFactionEligibility()
    {
        var game = CreateGame(0, 202609072);
        var cards = ArrangeFestivalTopFiveWithTwoPrintedSunCards(game, 0);
        var player = game.State.Players[0];
        var departedRing = PharaohFestivalRingFactionFixture.Card("S02-0008", "festival-departed-ring", 0);
        player.Graveyard.Add(departedRing);

        Assert.False(L12StructuredCardRules.HasFaction(player,
            player.Library.Single(card => card.InstanceId == cards.Universal), "taiyangcheng"));
        PlayFestival(game, 0, cards.Festival);

        var handPrompt = PharaohFestivalRingFactionFixture.Prompt(game);
        Assert.DoesNotContain(cards.Universal, handPrompt.ValidChoices);
        Assert.Contains(cards.FirstPrintedSun, handPrompt.ValidChoices);
    }

    [Fact]
    [Trait("L12Bug", "BUG-20260907-58aaeb7f")]
    public void ActiveWorldRingDoesNotMakeOtherFactionsOrFestivalItselfEligible()
    {
        var game = CreateGame(0, 202609073);
        var cards = ArrangeFestivalTopFive(game, 0, includeSecondUniversal: false);
        var player = game.State.Players[0];
        player.Relic = PharaohFestivalRingFactionFixture.Card("S02-0008", "festival-boundary-ring", 0);

        PlayFestival(game, 0, cards.Festival);

        var prompt = PharaohFestivalRingFactionFixture.Prompt(game);
        Assert.Contains(cards.UniversalForHand, prompt.ValidChoices);
        Assert.DoesNotContain(cards.OtherFaction, prompt.ValidChoices);
        Assert.DoesNotContain(cards.FestivalInLibrary, prompt.ValidChoices);
        Assert.Equal("只能选择【太阳城】卡牌，且不能选择〈法老王的庆典〉本身",
            prompt.Data[$"disabledChoice:{cards.OtherFaction}"]);
        Assert.Equal("只能选择【太阳城】卡牌，且不能选择〈法老王的庆典〉本身",
            prompt.Data[$"disabledChoice:{cards.FestivalInLibrary}"]);
    }

    private static L12GameEngine CreateGame(int controller, int seed)
    {
        var game = PharaohFestivalRingFactionFixture.Create(seed, "S01-02M3", "S01-02M3");
        game.State.ActivePlayer = controller;
        game.State.FirstPlayer = controller;
        foreach (var player in game.State.Players)
        {
            player.Relic = null;
            player.ExtraRelics.Clear();
        }
        return game;
    }

    private static FestivalCards ArrangeFestivalTopFive(L12GameEngine game, int controller,
        bool includeSecondUniversal)
    {
        var player = game.State.Players[controller];
        var suffix = $"{controller}-{includeSecondUniversal}";
        var festival = PharaohFestivalRingFactionFixture.Card("S01-0222", $"festival-source-{suffix}", controller);
        var universalForHand = PharaohFestivalRingFactionFixture.Card("S01-0003", $"festival-universal-hand-{suffix}", controller);
        var universalForGrave = includeSecondUniversal
            ? PharaohFestivalRingFactionFixture.Card("S01-0004", $"festival-universal-grave-{suffix}", controller)
            : PharaohFestivalRingFactionFixture.Card("S01-0204", $"festival-second-sun-{suffix}", controller);
        var printedSun = PharaohFestivalRingFactionFixture.Card("S01-0203", $"festival-printed-sun-{suffix}", controller);
        var otherFaction = PharaohFestivalRingFactionFixture.Card("S01-0101", $"festival-other-faction-{suffix}", controller);
        var festivalInLibrary = PharaohFestivalRingFactionFixture.Card("S01-0222", $"festival-self-excluded-{suffix}", controller);
        var top = new[] { universalForHand, universalForGrave, printedSun, otherFaction, festivalInLibrary };
        player.Hand.Add(festival);
        player.Library.AddRange(top);
        player.Morale.Add(PharaohFestivalRingFactionFixture.Morale($"festival-cost-{suffix}"));
        return new FestivalCards(festival.InstanceId, universalForHand.InstanceId,
            universalForGrave.InstanceId, printedSun.InstanceId, otherFaction.InstanceId,
            festivalInLibrary.InstanceId, top.Select(card => card.InstanceId).ToArray());
    }

    private static (string Festival, string Universal, string FirstPrintedSun, string SecondPrintedSun)
        ArrangeFestivalTopFiveWithTwoPrintedSunCards(L12GameEngine game, int controller)
    {
        var player = game.State.Players[controller];
        var festival = PharaohFestivalRingFactionFixture.Card("S01-0222", $"festival-no-ring-source-{controller}", controller);
        var universal = PharaohFestivalRingFactionFixture.Card("S01-0003", $"festival-no-ring-universal-{controller}", controller);
        var firstSun = PharaohFestivalRingFactionFixture.Card("S01-0203", $"festival-no-ring-sun-a-{controller}", controller);
        var secondSun = PharaohFestivalRingFactionFixture.Card("S01-0204", $"festival-no-ring-sun-b-{controller}", controller);
        var otherFaction = PharaohFestivalRingFactionFixture.Card("S01-0101", $"festival-no-ring-other-{controller}", controller);
        var excludedFestival = PharaohFestivalRingFactionFixture.Card("S01-0222", $"festival-no-ring-self-{controller}", controller);
        player.Hand.Add(festival);
        player.Library.AddRange([universal, firstSun, secondSun, otherFaction, excludedFestival]);
        player.Morale.Add(PharaohFestivalRingFactionFixture.Morale($"festival-no-ring-cost-{controller}"));
        return (festival.InstanceId, universal.InstanceId, firstSun.InstanceId, secondSun.InstanceId);
    }

    private static void PlayFestival(L12GameEngine game, int controller, string festivalInstanceId)
    {
        var result = game.Handle(controller, new L12Command("playCard", festivalInstanceId));
        Assert.True(result.Accepted, result.Error);
        PharaohFestivalRingFactionFixture.PassResponses(game);
    }

    private static void AssertFestivalPrompt(L12GameEngine game, string action,
        IEnumerable<string> displayed, params string[] expectedChoices)
    {
        var prompt = PharaohFestivalRingFactionFixture.Prompt(game);
        Assert.Equal(action, prompt.Data["action"]);
        Assert.Equal(string.Join('|', displayed), prompt.Data["displayCardIds"]);
        foreach (var expected in expectedChoices) Assert.Contains(expected, prompt.ValidChoices);
    }

    private static int CountPrivateZoneCopies(L12PlayerState player, string instanceId)
        => player.Hand.Count(card => card.InstanceId == instanceId)
            + player.Library.Count(card => card.InstanceId == instanceId)
            + player.Graveyard.Count(card => card.InstanceId == instanceId)
            + player.Resolving.Count(card => card.InstanceId == instanceId);
}
