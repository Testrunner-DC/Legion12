using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class LeaveReactionCauseBoundaryRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-17T03:00:00Z");
    private const string BloodId = "leave-boundary-blood";
    private const string FallenId = "leave-boundary-fallen";

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [Trait("L12Evidence", "bug:BUG-20260911-0389692e")]
    public void AlvidaSelfDiscardNeverCreatesADeathCounterOpportunity(int owner, bool restore)
    {
        var game = Create(owner);
        game.State.Players[owner].Field[0][0] = Card("S01-0307", FallenId, owner);
        game.State.Players[owner].Hand.Add(Card("S01-0303", "leave-boundary-summon", owner));
        Submit(ref game, owner, new("activateAbility", FallenId, Ability: "alvidaSummon"), restore);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("leave-boundary-summon", prompt.ValidChoices);
        Submit(ref game, owner, new("resolvePrompt", PromptId: prompt.PromptId,
            Choice: "leave-boundary-summon"), restore);
        prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("0:1", prompt.ValidChoices);
        Submit(ref game, owner, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "0:1"), restore);
        AssertNoBlood(game);
        PassResponses(ref game, restore);
        AssertNoBlood(game);
        Assert.Single(game.State.Players[owner].Graveyard, card => card.InstanceId == FallenId);
        Assert.Equal("leave-boundary-summon", game.State.Players[owner].Field[0][1]?.InstanceId);
        Assert.True(game.State.Players[owner].Field[1][0]!.Hidden);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void RealReturnToHandDoesNotCreateADeathCounterOpportunity(int owner, bool restore)
    {
        var game = Create(owner);
        Gm(ref game, new("returnCardToHand", owner, CardInstanceId: FallenId), restore);
        AssertNoBlood(game);
        Assert.Single(game.State.Players[owner].Hand, card => card.InstanceId == FallenId);
        Assert.True(game.State.Players[owner].Field[1][0]!.Hidden);
        Assert.Empty(game.State.PendingPrompts);
    }

    [Theory]
    [InlineData(0, false, "library-top")]
    [InlineData(0, true, "library-top")]
    [InlineData(1, false, "library-top")]
    [InlineData(1, true, "library-top")]
    [InlineData(0, false, "graveyard")]
    [InlineData(0, true, "graveyard")]
    [InlineData(1, false, "graveyard")]
    [InlineData(1, true, "graveyard")]
    public void SharedNonDefeatZoneMovesDoNotCreateDeathCandidates(int owner, bool restore, string destination)
    {
        var game = Create(owner);
        if (restore) game = Restore(game);
        var player = game.State.Players[owner];
        var fallen = player.Field[0][0]!;
        var moved = typeof(L12GameEngine).GetMethod("MoveFieldCardToZone", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, [player, fallen, destination, "合成非阵亡移区", true]);
        Assert.True(Assert.IsType<bool>(moved));
        if (restore) game = Restore(game);
        AssertNoBlood(game);
        var zone = destination == "library-top" ? game.State.Players[owner].Library : game.State.Players[owner].Graveyard;
        Assert.Single(zone, card => card.InstanceId == FallenId);
    }

    [Theory]
    [InlineData(0, false, "destroy")]
    [InlineData(0, true, "destroy")]
    [InlineData(1, false, "destroy")]
    [InlineData(1, true, "destroy")]
    [InlineData(0, false, "zero")]
    [InlineData(0, true, "zero")]
    [InlineData(1, false, "zero")]
    [InlineData(1, true, "zero")]
    [InlineData(0, false, "combat")]
    [InlineData(0, true, "combat")]
    [InlineData(1, false, "combat")]
    [InlineData(1, true, "combat")]
    public void RealDefeatAndZeroTroopsStillOfferBloodEagleExactlyOnce(int owner, bool restore, string cause)
    {
        var game = Create(owner);
        if (cause == "combat")
        {
            game.State.ActivePlayer = 1 - owner;
            Submit(ref game, 1 - owner, new("attack", "leave-boundary-enemy",
                Target: new L12AttackTarget("legion", FallenId)), restore);
            for (var step = 0; !HasBloodPrompt(game); step++)
            {
                Assert.True(step < 16);
                if (game.State.PendingPrompts.Count > 0)
                {
                    var response = Assert.Single(game.State.PendingPrompts);
                    Assert.Equal("response", response.Kind);
                    Submit(ref game, response.PlayerIndex,
                        new("resolvePrompt", PromptId: response.PromptId, Choice: "pass"), restore);
                }
                else
                {
                    Assert.NotNull(game.State.PendingDefense);
                    Submit(ref game, owner, new("resolveDefense", CardInstanceIds: []), restore);
                }
            }
        }
        else Gm(ref game, new(cause == "zero" ? "setTroops" : "destroyCard", owner,
            CardInstanceId: FallenId, Value: cause == "zero" ? 0 : null), restore);

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("S01-0320", prompt.SourceCardId);
        Assert.Equal(BloodId, prompt.SourceInstanceId);
        Assert.Single(game.State.PendingActivations, item => item.SourceCardId == "S01-0320");
        Submit(ref game, owner, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "mode:none"), restore);
        PassResponses(ref game, restore);
        AssertNoBlood(game);
        Assert.Single(game.State.Players[owner].Graveyard, card => card.InstanceId == FallenId);
        Assert.True(game.State.Players[owner].Field[1][0]!.Hidden);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void NonDefeatReturnPreservesIndependentLeaveReaction(int owner, bool restore)
    {
        var game = Create(owner);
        game.State.ActivePlayer = 1 - owner;
        game.State.Players[owner].Field[0][0] = Card("S01-0001", FallenId, owner);
        var gift = Card("S01-0223", "leave-boundary-gift", owner);
        gift.Hidden = true; gift.SetRound = 0;
        game.State.Players[owner].Field[1][1] = gift;
        game.State.Players[owner].Library.Add(Card("S01-0003", "leave-boundary-draw", owner));
        Gm(ref game, new("returnCardToHand", owner, CardInstanceId: FallenId), restore);
        AssertNoBlood(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("S01-0223", prompt.SourceCardId);
        Submit(ref game, owner, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "mode:use"), restore);
        Assert.Contains(game.State.EffectStack, item => item.SourceCardId == "S01-0223");
        PassResponses(ref game, restore);
        Assert.Contains(game.State.Players[owner].Hand, card => card.InstanceId == "leave-boundary-draw");
        AssertNoBlood(game);
    }

    private static bool HasBloodPrompt(L12GameEngine game)
        => game.State.PendingPrompts.Any(prompt => prompt.SourceCardId == "S01-0320");

    private static void AssertNoBlood(L12GameEngine game)
    {
        Assert.False(HasBloodPrompt(game));
        Assert.DoesNotContain(game.State.PendingActivations, item => item.SourceCardId == "S01-0320");
        Assert.DoesNotContain(game.State.PendingTriggerStackCandidates, item => item.SourceCardId == "S01-0320");
        Assert.DoesNotContain(game.State.PendingTriggerBatches.SelectMany(batch => batch.Candidates),
            item => item.SourceCardId == "S01-0320");
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceCardId == "S01-0320");
    }

    private static void PassResponses(ref L12GameEngine game, bool restore)
    {
        for (var step = 0; game.State.PendingPrompts.Count > 0; step++)
        {
            Assert.True(step < 24);
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", prompt.Kind);
            Submit(ref game, prompt.PlayerIndex, new("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"), restore);
        }
    }

    private static void Submit(ref L12GameEngine game, int actor, L12Command command, bool restore)
    {
        if (restore) game = Restore(game);
        var result = game.Handle(actor, command);
        Assert.True(result.Accepted, result.Error);
        if (restore) game = Restore(game);
    }

    private static void Gm(ref L12GameEngine game, L12GmCommand command, bool restore)
    {
        if (restore) game = Restore(game);
        var result = game.HandleGm(command);
        Assert.True(result.Accepted, result.Error);
        if (restore) game = Restore(game);
    }

    private static L12GameEngine Restore(L12GameEngine game)
    {
        var raw = game.SerializeFullState(); var hash = game.ComputeStateHash();
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, raw, game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, utcNow: () => Now);
        Assert.Equal(raw, restored.SerializeFullState()); Assert.Equal(hash, restored.ComputeStateHash());
        return restored;
    }

    private static L12GameEngine Create(int owner)
    {
        var game = new L12GameEngine(Catalog, "leave-cause-boundary", "LOCAL", 202610170 + owner,
            ["合成甲", "合成乙"], [0, 0], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2, utcNow: () => Now);
        game.State.ActivePlayer = owner; game.State.FirstPlayer = owner;
        game.State.Round = 3; game.State.TurnSerial = 5; game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            foreach (var row in player.Field) Array.Clear(row);
            player.Hand.Clear(); player.Library.Clear(); player.Graveyard.Clear(); player.Morale.Clear();
            player.Resolving.Clear(); player.Removed.Clear(); player.Relic = null; player.ExtraRelics.Clear();
            player.UsedAbilities.Clear();
        }
        game.State.Players[owner].Field[0][0] = Card("S01-0002", FallenId, owner);
        var blood = Card("S01-0320", BloodId, owner); blood.Hidden = true; blood.SetRound = 0;
        game.State.Players[owner].Field[1][0] = blood;
        var enemy = Card("S01-0001", "leave-boundary-enemy", 1 - owner);
        enemy.Troops = 8000; enemy.SummonRound = 0;
        game.State.Players[1 - owner].Field[0][0] = enemy;
        return game;
    }

    private static L12CardInstance Card(string id, string instanceId, int owner)
    {
        var d = Catalog.Cards[id];
        return new() { InstanceId = instanceId, CardId = id, Name = d.NameZh, CardType = d.CardType,
            IsCounterTactic = d.IsCounterTactic, Faction = d.Faction, ImageUrl = d.ImageUrl,
            Cost = d.Cost ?? 0, HasPrintedCost = d.Cost.HasValue, EffectText = d.Effect,
            Traits = [.. d.Traits], Profession = d.Profession, BaseTroops = d.Troops ?? 0,
            Troops = d.Troops ?? 0, DisasterLevel = d.DisasterLevel ?? 0,
            OwnerIndex = owner, SummonRound = -1 };
    }
}
