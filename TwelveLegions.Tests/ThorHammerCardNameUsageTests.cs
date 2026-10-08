using TwelveLegions.Server;
using Xunit;
using static TwelveLegions.Tests.ThorHammerCardNameUsageFixture;

namespace TwelveLegions.Tests;

public sealed class ThorHammerCardNameUsageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommittedUseLocksEveryCopyAndSurvivesEntryAndV2(bool restoreBeforeResolution)
    {
        var game = Create(26100811 + (restoreBeforeResolution ? 1 : 0));
        var (first, second, costs) = SeedPair(game, prefix: "shared-success");
        var ordered = new[] { costs[2], costs[0], costs[1] };

        Commit(game, 0, first, ordered, "0:0");

        var player = game.State.Players[0];
        Assert.Contains(SharedUsageKey, player.UsedAbilities);
        Assert.Equal(ordered.Select(card => card.InstanceId),
            player.Library.TakeLast(3).Select(card => card.InstanceId));
        if (restoreBeforeResolution) game = Restore(game);
        PassResponses(game);

        player = game.State.Players[0];
        first = Assert.Single(player.Field.SelectMany(row => row),
            card => card?.InstanceId == first.InstanceId)!;
        second = Assert.Single(player.Graveyard, card => card.InstanceId == second.InstanceId);
        Assert.Contains(SharedUsageKey, player.UsedAbilities);
        AssertHammerBlocked(game, 0, second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelBeforeCommitDoesNotConsumeSharedUse(bool restoreBeforeCancel)
    {
        var game = Create(26100821 + (restoreBeforeCancel ? 1 : 0));
        var (first, second, _) = SeedPair(game, prefix: "shared-cancel");
        var prompt = Begin(game, 0, first);
        if (restoreBeforeCancel)
        {
            game = Restore(game);
            prompt = Assert.Single(game.State.PendingPrompts);
            second = Assert.Single(game.State.Players[0].Graveyard,
                card => card.InstanceId == second.InstanceId);
        }

        Cancel(game, prompt);

        Assert.DoesNotContain(SharedUsageKey, game.State.Players[0].UsedAbilities);
        AssertHammerCanBegin(game, 0, second);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("slot")]
    public void InvalidDeclarationBeforeCommitDoesNotConsumeSharedUse(string invalidated)
    {
        var game = Create(26100831 + (invalidated == "slot" ? 1 : 0));
        var (first, second, costs) = SeedPair(game, prefix: $"shared-invalid-{invalidated}");
        var order = Begin(game, 0, first);
        ResolveMany(game, order, costs[0].InstanceId, costs[1].InstanceId, costs[2].InstanceId);
        var slot = Assert.Single(game.State.PendingPrompts);
        var selectedSlot = slot.ValidChoices[0];
        if (invalidated == "source")
        {
            game.State.Players[0].Graveyard.Remove(first);
            game.State.Players[0].Hand.Add(first);
        }
        else
        {
            var parts = selectedSlot.Split(':').Select(int.Parse).ToArray();
            game.State.Players[0].Field[parts[0]][parts[1]] = Card("S02-0002", "invalid-slot-blocker");
        }

        ResolveChoice(game, slot, selectedSlot);

        var player = game.State.Players[0];
        Assert.DoesNotContain(SharedUsageKey, player.UsedAbilities);
        Assert.All(costs.Take(3), cost => Assert.Contains(cost, player.Graveyard));
        AssertHammerCanBegin(game, 0, second);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("slot")]
    public void CommittedUseRemainsConsumedWhenSettlementBecomesInvalid(string invalidated)
    {
        var game = Create(26100841 + (invalidated == "slot" ? 1 : 0));
        var (first, second, costs) = SeedPair(game, prefix: $"shared-settlement-{invalidated}");
        var committed = Commit(game, 0, first, costs.Take(3).ToArray(), "0:0");
        var player = game.State.Players[0];
        if (invalidated == "source")
        {
            player.Graveyard.Remove(first);
            player.Hand.Add(first);
        }
        else
        {
            var parts = committed.Slot.Split(':').Select(int.Parse).ToArray();
            player.Field[parts[0]][parts[1]] = Card("S02-0002", "settlement-slot-blocker");
        }

        PassResponses(game);

        Assert.Contains(SharedUsageKey, player.UsedAbilities);
        Assert.DoesNotContain(player.Field.SelectMany(row => row), card => card?.InstanceId == first.InstanceId);
        AssertHammerBlocked(game, 0, second);
    }

    [Theory]
    [InlineData("hand")]
    [InlineData("library-bottom")]
    [InlineData("graveyard")]
    public void AuthoritativePrivateZoneMoveDoesNotRefreshNameLimit(string destination)
    {
        var game = Create(26100851 + destination.Length);
        var (first, second, costs) = SeedPair(game, prefix: $"shared-zone-{destination}");
        Commit(game, 0, first, costs.Take(3).ToArray(), "0:0");
        PassResponses(game);
        first = Assert.Single(game.State.Players[0].Field.SelectMany(row => row),
            card => card?.InstanceId == first.InstanceId)!;

        MoveFieldCardToPrivateZone(game, 0, first, destination);

        Assert.Contains(SharedUsageKey, game.State.Players[0].UsedAbilities);
        AssertHammerBlocked(game, 0, second);
    }

    [Fact]
    public void RealDesertDominionDiscardDoesNotRefreshNameLimit()
    {
        var game = Create(26100861);
        var (first, second, costs) = SeedPair(game, prefix: "shared-real-discard");
        Commit(game, 0, first, costs.Take(3).ToArray(), "0:0");
        PassResponses(game);
        first = Assert.Single(game.State.Players[0].Field.SelectMany(row => row),
            card => card?.InstanceId == first.InstanceId)!;

        DiscardHammerThroughRealDesertDominion(game, first);

        Assert.Contains(first, game.State.Players[0].Graveyard);
        Assert.Contains(SharedUsageKey, game.State.Players[0].UsedAbilities);
        AssertHammerBlocked(game, 0, second);
    }

    [Fact]
    public void SharedUseIsScopedToController()
    {
        var game = Create(26100871);
        game.State.Players[0].UsedAbilities.Add(SharedUsageKey);
        var (_, secondPlayerHammer, _) = SeedPair(game, 1, "other-controller");
        game.State.ActivePlayer = 1;

        AssertHammerCanBegin(game, 1, secondPlayerHammer);
        Assert.Contains(SharedUsageKey, game.State.Players[0].UsedAbilities);
        Assert.DoesNotContain(SharedUsageKey, game.State.Players[1].UsedAbilities);
    }

    [Fact]
    public void NextOwnTurnClearsSharedUseAndAllowsOneCopyAgain()
    {
        var game = Create(26100881);
        var (first, second, costs) = SeedPair(game, prefix: "shared-next-turn");
        Commit(game, 0, first, costs.Take(3).ToArray(), "0:0");
        PassResponses(game);
        first = Assert.Single(game.State.Players[0].Field.SelectMany(row => row),
            card => card?.InstanceId == first.InstanceId)!;
        MoveFieldCardToPrivateZone(game, 0, first, "graveyard");
        AssertHammerBlocked(game, 0, second);

        var end = game.Handle(0, new L12Command("endTurn"));
        Assert.True(end.Accepted, end.Error);
        Assert.Equal(1, game.State.ActivePlayer);
        end = game.Handle(1, new L12Command("endTurn"));
        Assert.True(end.Accepted, end.Error);
        Assert.Equal(0, game.State.ActivePlayer);

        second = Assert.Single(game.State.Players[0].Graveyard,
            card => card.InstanceId == second.InstanceId);
        Assert.DoesNotContain(SharedUsageKey, game.State.Players[0].UsedAbilities);
        AssertHammerCanBegin(game, 0, second);
    }

    [Fact]
    public void V2OrderAndCommitRecoveryPreserveOnePaymentAndRejectOldPromptReplay()
    {
        var game = Create(26100891);
        var (first, second, costs) = SeedPair(game, prefix: "shared-v2");
        var ordered = new[] { costs[1], costs[2], costs[0] };
        var orderPrompt = Begin(game, 0, first);
        ResolveMany(game, orderPrompt, ordered.Select(card => card.InstanceId).ToArray());
        game = Restore(game);
        var slotPrompt = Assert.Single(game.State.PendingPrompts);
        ResolveChoice(game, slotPrompt, slotPrompt.ValidChoices[0]);
        Assert.Contains(SharedUsageKey, game.State.Players[0].UsedAbilities);
        Assert.Equal(ordered.Select(card => card.InstanceId),
            game.State.Players[0].Library.TakeLast(3).Select(card => card.InstanceId));

        var replay = game.Handle(0, new L12Command("resolvePrompt",
            PromptId: orderPrompt.PromptId, CardInstanceIds: ordered.Select(card => card.InstanceId).ToList()));
        Assert.False(replay.Accepted);
        game = Restore(game);
        PassResponses(game);

        second = Assert.Single(game.State.Players[0].Graveyard,
            card => card.InstanceId == second.InstanceId);
        Assert.Equal(3, game.State.Players[0].Library.Count(card =>
            ordered.Any(cost => cost.InstanceId == card.InstanceId)));
        AssertHammerBlocked(game, 0, second);
    }

    [Fact]
    public void LegacyInstanceMarkerBlocksEveryCopyAfterV2Recovery()
    {
        var game = Create(26100901);
        var (first, second, _) = SeedPair(game, prefix: "shared-legacy");
        game.State.Players[0].UsedAbilities.Add($"active:{first.InstanceId}:{HammerAbility}");
        game = Restore(game);
        second = Assert.Single(game.State.Players[0].Graveyard,
            card => card.InstanceId == second.InstanceId);

        AssertHammerBlocked(game, 0, second);
    }

    [Fact]
    public void OwnTurnAndThorMasterGatesRemainAuthoritative()
    {
        var wrongMaster = Create(26100911, firstMaster: "S01-02M1");
        var (wrongHammer, _, _) = SeedPair(wrongMaster, prefix: "shared-wrong-master");
        var wrong = wrongMaster.Handle(0,
            new L12Command("activateAbility", wrongHammer.InstanceId, Ability: HammerAbility));
        Assert.False(wrong.Accepted);
        Assert.Equal("仅〈雷神索尔〉可发动墓地中〈雷神之锤〉的效果", wrong.Error);

        var opponentTurn = Create(26100912);
        var (hammer, _, _) = SeedPair(opponentTurn, prefix: "shared-opponent-turn");
        opponentTurn.State.ActivePlayer = 1;
        var offTurn = opponentTurn.Handle(0,
            new L12Command("activateAbility", hammer.InstanceId, Ability: HammerAbility));
        Assert.False(offTurn.Accepted);
        Assert.Equal("只能在自己的主要阶段发动主动效果", offTurn.Error);
    }
}
