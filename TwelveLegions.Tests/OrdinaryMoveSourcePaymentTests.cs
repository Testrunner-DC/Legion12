using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class OrdinaryMoveSourcePaymentTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int actor)
    {
        var game = new L12GameEngine(Catalog, "move-source-payment", "MOVE-COST", 107182,
            ["甲", "乙"], [0, 0], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = actor;
        game.State.Phase = L12Phase.Main;
        game.State.Round = 3;
        game.State.TurnSerial = 5;
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear(); player.Morale.Clear(); player.TemporaryMorale = 0;
            foreach (var row in player.Field) Array.Clear(row);
        }
        game.State.Players[actor].Field[0][0] = Guard("moving-guard");
        return game;
    }

    private static L12CardInstance Guard(string instance)
    {
        var definition = Catalog.Cards["S01-0212"];
        return new L12CardInstance
        {
            CardId = definition.Id, InstanceId = instance, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction,
            Cost = definition.Cost ?? 0, BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0, Traits = [.. definition.Traits],
            EffectText = definition.Effect, SummonRound = -1,
        };
    }

    private static L12GameEngine Restore(L12GameEngine game) => L12GameEngine.RestoreCheckpoint(
        Catalog, game.SerializeFullState(), game.RandomState!.Value, game.CardFactSignalSequence,
        autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)]
    [InlineData(0, true)] [InlineData(1, true)]
    public void MovingGuardCannotPayWithItselfAndNothingIsConsumed(int actor, bool restore)
    {
        var game = Create(actor);
        game.State.Players[actor].Morale.Add(new L12MoraleCard { InstanceId = "ordinary", CardId = "S01-00C1" });
        if (restore) game = Restore(game);
        var player = game.State.Players[actor];
        var before = game.SerializeFullState();
        var result = game.Handle(actor, new L12Command("move", "moving-guard", Row: 0, Slot: 1,
            CardInstanceIds: ["moving-guard"]));
        Assert.False(result.Accepted);
        Assert.Equal(before, game.SerializeFullState());
        Assert.False(player.Field[0][0]!.Tapped);
        Assert.False(Assert.Single(player.Morale).Tapped);
        Assert.Null(player.Field[0][1]);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)]
    [InlineData(0, true)] [InlineData(1, true)]
    public void AutomaticMoveCannotConsumeItsOnlyMovingGuard(int actor, bool restore)
    {
        var game = Create(actor);
        if (restore) game = Restore(game);
        var before = game.SerializeFullState();
        Assert.False(game.Handle(actor, new L12Command("move", "moving-guard", Row: 0, Slot: 1)).Accepted);
        Assert.Equal(before, game.SerializeFullState());
        Assert.Empty(game.State.PendingPrompts);
    }

    [Theory]
    [InlineData(0, "morale", false)] [InlineData(1, "morale", false)]
    [InlineData(0, "guard", false)] [InlineData(1, "guard", false)]
    [InlineData(0, "temporary", false)] [InlineData(1, "temporary", false)]
    [InlineData(0, "god-power", false)] [InlineData(1, "god-power", false)]
    [InlineData(0, "morale", true)] [InlineData(1, "morale", true)]
    [InlineData(0, "guard", true)] [InlineData(1, "guard", true)]
    [InlineData(0, "temporary", true)] [InlineData(1, "temporary", true)]
    [InlineData(0, "god-power", true)] [InlineData(1, "god-power", true)]
    public void OtherResourcesStillPayAndMovingGuardStaysActive(int actor, string resource, bool explicitPayment)
    {
        var game = Create(actor);
        var player = game.State.Players[actor];
        var paymentId = "other-resource";
        if (resource == "guard") player.Field[1][2] = Guard(paymentId);
        else if (resource == "temporary") { player.TemporaryMorale = 1; paymentId = "temporary-morale:1"; }
        else player.Morale.Add(new L12MoraleCard { InstanceId = paymentId, CardId = "S01-00C1", IsGodPower = resource == "god-power" });
        game = Restore(game);
        player = game.State.Players[actor];
        var result = game.Handle(actor, new L12Command("move", "moving-guard", Row: 0, Slot: 1,
            CardInstanceIds: explicitPayment ? [paymentId] : null));
        Assert.True(result.Accepted, result.Error);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Null(player.Field[0][0]);
        Assert.False(player.Field[0][1]!.Tapped);
        Assert.Equal("moving-guard", player.Field[0][1]!.InstanceId);
        if (resource == "guard") Assert.True(player.Field[1][2]!.Tapped);
        else if (resource == "temporary") Assert.Equal(0, player.TemporaryMorale);
        else Assert.True(Assert.Single(player.Morale).Tapped);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void MovePaymentPromptExcludesSelfAndRestoredSubmissionRejectsForgedSelf(int actor)
    {
        var game = Create(actor); var player = game.State.Players[actor];
        player.Field[1][2] = Guard("other-guard");
        player.Morale.Add(new L12MoraleCard { InstanceId = "ordinary", CardId = "S01-00C1" });
        Assert.True(game.Handle(actor, new L12Command("move", "moving-guard", Row: 0, Slot: 1)).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-payment", prompt.Kind);
        Assert.DoesNotContain("moving-guard", prompt.ValidChoices);
        Assert.Contains("other-guard", prompt.ValidChoices);
        Assert.Contains("ordinary", prompt.ValidChoices);
        game = Restore(game); player = game.State.Players[actor];
        prompt = Assert.Single(game.State.PendingPrompts);
        var before = game.SerializeFullState();
        Assert.False(game.Handle(actor, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: ["moving-guard"])).Accepted);
        Assert.Equal(before, game.SerializeFullState());
        Assert.True(game.Handle(actor, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: ["other-guard"])).Accepted);
        Assert.Empty(game.State.PendingPrompts);
        Assert.False(player.Field[0][1]!.Tapped);
        Assert.True(player.Field[1][2]!.Tapped);
        Assert.False(Assert.Single(player.Morale).Tapped);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void LegacyRestoredSelfChoiceRejectsWithoutConsumingTheMovePrompt(int actor)
    {
        var game = Create(actor); var player = game.State.Players[actor];
        player.Field[1][2] = Guard("other-guard");
        player.Morale.Add(new L12MoraleCard { InstanceId = "ordinary", CardId = "S01-00C1" });
        Assert.True(game.Handle(actor, new L12Command("move", "moving-guard", Row: 0, Slot: 1)).Accepted);
        // A checkpoint recorded by the old version may still list the moving guard.
        var oldPrompt = Assert.Single(game.State.PendingPrompts);
        oldPrompt.ValidChoices.Add("moving-guard");
        oldPrompt.ChoiceLabels["moving-guard"] = "陵墓守卫 · 我方前排左格";
        game = Restore(game); player = game.State.Players[actor];
        var prompt = Assert.Single(game.State.PendingPrompts);
        var before = game.SerializeFullState();
        Assert.False(game.Handle(actor, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: ["moving-guard"])).Accepted);
        Assert.Equal(before, game.SerializeFullState());
        Assert.True(game.Handle(actor, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: ["ordinary"])).Accepted);
        Assert.Empty(game.State.PendingPrompts);
        Assert.False(player.Field[0][1]!.Tapped);
        Assert.True(Assert.Single(player.Morale).Tapped);
        Assert.False(player.Field[1][2]!.Tapped);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void FreeOrdinaryMoveNeedsNoResourceAndDoesNotTapItsGuard(int actor)
    {
        var game = Create(actor);
        game.State.Players[actor].UsedAbilities.Add($"s2-tenka-free-move:moving-guard:{game.State.TurnSerial}");
        game = Restore(game);
        Assert.True(game.Handle(actor, new L12Command("move", "moving-guard", Row: 0, Slot: 1)).Accepted);
        Assert.False(game.State.Players[actor].Field[0][1]!.Tapped);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void ExistingAutomaticWaiverDoesNotPayByRestingTheSource(int actor)
    {
        var game = Create(actor);
        game.State.Players[actor].MasterMoraleWaiverCredit = 1;
        Assert.True(game.Handle(actor, new L12Command("move", "moving-guard", Row: 0, Slot: 1)).Accepted);
        Assert.False(game.State.Players[actor].Field[0][1]!.Tapped);
        Assert.Equal(0, game.State.Players[actor].MasterMoraleWaiverCredit);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)]
    [InlineData(0, true)] [InlineData(1, true)]
    public void InactiveMovingSourceStillRejectsBeforePayment(int actor, bool hidden)
    {
        var game = Create(actor); var player = game.State.Players[actor];
        player.Field[0][0]!.Tapped = !hidden; player.Field[0][0]!.Hidden = hidden;
        player.Morale.Add(new L12MoraleCard { InstanceId = "ordinary", CardId = "S01-00C1" });
        var before = game.SerializeFullState();
        Assert.False(game.Handle(actor, new L12Command("move", "moving-guard", Row: 0, Slot: 1,
            CardInstanceIds: ["ordinary"])).Accepted);
        Assert.Equal(before, game.SerializeFullState());
    }
}
