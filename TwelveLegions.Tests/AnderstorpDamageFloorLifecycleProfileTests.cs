using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AnderstorpDamageFloorLifecycleProfileTests
{
    private const string AbilityId = "S02-0305:ability:master-damaged:4c8ce907eed1f778";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence(AbilityId, "normal", "reconnect", "presentation-consumers")]
    public void OpponentTurnFirstMasterDamageBecomesTwoAndLaterDamageDoesNotAcrossRestore()
    {
        var game = Create(72500);
        var defender = game.State.Players[0];
        var attackerPlayer = game.State.Players[1];
        defender.Relic = Card("S02-0305", "anderstorp-ring", ownerIndex: 0);
        var first = Card("S01-0001", "anderstorp-first-attacker", ownerIndex: 1);
        var second = Card("S01-0002", "anderstorp-second-attacker", ownerIndex: 1);
        first.SummonRound = second.SummonRound = -1;
        attackerPlayer.Field[0][0] = first;
        attackerPlayer.Field[0][1] = second;
        game.State.ActivePlayer = 1;
        game.State.FirstPlayer = 0;
        game.State.Round = 3;
        game.State.TurnSerial = 5;
        game.State.Phase = L12Phase.Main;
        var hpBefore = defender.Hp;

        game = Restore(game);
        AttackMasterAndResolve(game, "anderstorp-first-attacker");
        Assert.Equal(hpBefore - 2, game.State.Players[0].Hp);
        Assert.Equal(2, game.State.Players[0].MasterDamageTakenThisTurn);
        Assert.Contains(game.State.Events, entry => entry.Type == "damage"
            && entry.Text.Contains("失去 2 点血量", StringComparison.Ordinal));

        game.State.Phase = L12Phase.Main;
        AttackMasterAndResolve(game, "anderstorp-second-attacker");
        Assert.Equal(hpBefore - 3, game.State.Players[0].Hp);
        Assert.Equal(3, game.State.Players[0].MasterDamageTakenThisTurn);
        Assert.Contains(game.State.Events, entry => entry.Type == "damage"
            && entry.Text.Contains("失去 1 点血量", StringComparison.Ordinal));
    }

    [Fact]
    [L12AbilityEvidence("S02-0305:ability:continuous:26b824128ffced1a", "non-opening-entry", "end-turn-discard")]
    public void AnderstorpEndTurnRestrictionStartsWhenTheRelicIsPlayedFromHand()
    {
        var game = Create(72501);
        game.State.Phase = L12Phase.Main;
        game.State.ActivePlayer = 0;
        var player = game.State.Players[0];
        var ring = Card("S02-0305", "anderstorp-played-from-hand", 0);
        player.Hand.Add(ring);

        var play = game.Handle(0, new L12Command("playCard", ring.InstanceId));
        Assert.True(play.Accepted, play.Error);
        Assert.Same(ring, player.Relic);

        for (var index = 0; index < 7; index++)
            player.Hand.Add(Card("S01-0001", $"anderstorp-end-hand-{index}", 0));

        var endTurn = game.Handle(0, new L12Command("endTurn"));
        Assert.True(endTurn.Accepted, endTurn.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-ring-end-discard", prompt.Continuation);
        Assert.Equal(1, prompt.MinChoose);
        Assert.Equal(1, prompt.MaxChoose);

        var resolve = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [player.Hand[0].InstanceId]));
        Assert.True(resolve.Accepted, resolve.Error);
        Assert.Equal(6, player.Hand.Count);
    }

    [Fact]
    [L12AbilityEvidence("S02-0305:ability:continuous:26b824128ffced1a", "extra-relic-zone", "end-turn-discard")]
    public void AnderstorpEndTurnRestrictionAlsoObservesTheExtraRelicStorage()
    {
        var game = Create(72502);
        game.State.Phase = L12Phase.Main;
        game.State.ActivePlayer = 0;
        var player = game.State.Players[0];
        player.ExtraRelics.Add(Card("S02-0305", "anderstorp-extra-relic", 0));
        for (var index = 0; index < 7; index++)
            player.Hand.Add(Card("S01-0001", $"anderstorp-extra-hand-{index}", 0));

        var endTurn = game.Handle(0, new L12Command("endTurn"));
        Assert.True(endTurn.Accepted, endTurn.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-ring-end-discard", prompt.Continuation);
    }

    private static void AttackMasterAndResolve(L12GameEngine game, string attackerId)
    {
        var result = game.Handle(1, new L12Command("attack", attackerId,
            Target: new L12AttackTarget("master")));
        Assert.True(result.Accepted, result.Error);
        for (var count = 0; count < 50; count++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                var choice = prompt.Kind == "response" ? "pass"
                    : prompt.ValidChoices.Contains("no") ? "no"
                    : prompt.ValidChoices.Contains("skip") ? "skip"
                    : prompt.ValidChoices.FirstOrDefault() ?? "pass";
                result = game.Handle(prompt.PlayerIndex,
                    new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
                Assert.True(result.Accepted, result.Error);
                continue;
            }
            if (game.State.PendingDefense is not null)
            {
                result = game.Handle(0, new L12Command("resolveDefense", CardInstanceIds: []));
                Assert.True(result.Accepted, result.Error);
                continue;
            }
            break;
        }
        Assert.Null(game.State.PendingDefense);
        Assert.Empty(game.State.EffectStack);
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "anderstorp-damage-floor", "ANDERSTORP", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Morale.Clear();
        }
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static L12CardInstance Card(string cardId, string instanceId, int ownerIndex)
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
            HasPrintedCost = definition.Cost is not null,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = ownerIndex,
        };
    }
}
