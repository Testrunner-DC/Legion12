using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CourtMagicianActiveRestLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, bool autoPassEmptyResponses = true)
        => new(Catalog, "court-magician-rest", "CMREST", seed, ["甲", "乙"], [4, 4],
            skipPreparation: true, autoPassEmptyResponses: autoPassEmptyResponses,
            concealHiddenResponseAvailability: false);

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            IsCounterTactic = definition.IsCounterTactic,
            Faction = definition.Faction,
            ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = owner,
            SummonRound = 0,
        };
    }

    private static void PrepareMain(L12GameEngine game, L12CardInstance magician)
    {
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;
        game.State.Players[0].Field[0][0] = magician;
    }

    private static void Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 20
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0003")]
    [Trait("L12Evidence", "ability:disableCounters")]
    [Trait("L12Evidence", "family:active-rest-lifecycle")]
    public void ActiveRestIsPaidBeforeResponseAndNegationDoesNotStartTheSeal()
    {
        var game = Create(92001, autoPassEmptyResponses: false);
        var magician = Card("S02-0003", "court-magician-negated");
        var defense = Card("S01-0016", "court-magician-defense", owner: 1);
        var discard = Card("S01-0001", "court-magician-defense-cost", owner: 1);
        defense.Hidden = true;
        defense.SetRound = 0;
        PrepareMain(game, magician);
        game.State.Players[1].Field[1][0] = defense;
        game.State.Players[1].Hand.Add(discard);

        var activation = game.Handle(0,
            new L12Command("activateAbility", magician.InstanceId, Ability: "disableCounters"));

        Assert.True(activation.Accepted, activation.Error);
        Assert.True(magician.Tapped);
        Assert.Equal(-1, game.State.CounterTacticsDisabledUntilTurnSerial);
        if (Assert.Single(game.State.PendingPrompts).PlayerIndex == 0)
            Resolve(game, "pass");
        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", response.Kind);
        Assert.Contains(defense.InstanceId, response.ValidChoices);
        Assert.Contains("Cost（已支付）", response.Text, StringComparison.Ordinal);
        Assert.Contains($"休整〈{magician.Name}〉", response.Data["responsePaidCostSummary"],
            StringComparison.Ordinal);

        Resolve(game, defense.InstanceId);
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("stack-response-discard", payment.Continuation);
        Resolve(game, discard.InstanceId);
        PassResponses(game);

        Assert.True(magician.Tapped);
        Assert.Equal(-1, game.State.CounterTacticsDisabledUntilTurnSerial);
        Assert.Equal(-1, game.State.CounterTacticsDisabledExpiresAtPlayerTurnStart);
    }

    [Fact]
    [Trait("L12Evidence", "family:active-rest-common-stack-cost")]
    public void CommonActiveStackBoundaryCommitsStructuredRestAndNegationCannotRefundIt()
    {
        var game = Create(920011, autoPassEmptyResponses: false);
        var magician = Card("S02-0003", "court-magician-common-boundary");
        PrepareMain(game, magician);
        var push = typeof(L12GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method => method.Name == "PushEffect" && method.GetParameters().Length == 6);

        push.Invoke(game, [0, magician, "active", "主动效果", null,
            new Dictionary<string, string> { ["ability"] = "disableCounters" }]);

        Assert.True(magician.Tapped);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);
        Assert.True(magician.Tapped);
        Assert.Equal(-1, game.State.CounterTacticsDisabledUntilTurnSerial);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0003")]
    [Trait("L12Evidence", "family:active-rest-duration-recovery")]
    public void SealSurvivesCheckpointAndOpponentTurnThenExpiresAtTheOwnersNextTurnStart()
    {
        var game = Create(92002);
        var magician = Card("S02-0003", "court-magician-duration");
        PrepareMain(game, magician);

        var activation = game.Handle(0,
            new L12Command("activateAbility", magician.InstanceId, Ability: "disableCounters"));
        Assert.True(activation.Accepted, activation.Error);
        Assert.True(game.State.TurnSerial < game.State.CounterTacticsDisabledUntilTurnSerial);
        Assert.Equal(0, game.State.CounterTacticsDisabledExpiresAtPlayerTurnStart);

        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: true, concealHiddenResponseAvailability: false);
        Assert.True(game.State.TurnSerial < game.State.CounterTacticsDisabledUntilTurnSerial);
        Assert.Equal(0, game.State.CounterTacticsDisabledExpiresAtPlayerTurnStart);

        var ownerEnd = game.Handle(0, new L12Command("endTurn"));
        Assert.True(ownerEnd.Accepted, ownerEnd.Error);
        Assert.Equal(1, game.State.ActivePlayer);
        Assert.True(game.State.TurnSerial < game.State.CounterTacticsDisabledUntilTurnSerial);

        var opponentEnd = game.Handle(1, new L12Command("endTurn"));
        Assert.True(opponentEnd.Accepted, opponentEnd.Error);
        Assert.Equal(0, game.State.ActivePlayer);
        Assert.Equal(-1, game.State.CounterTacticsDisabledUntilTurnSerial);
        Assert.Equal(-1, game.State.CounterTacticsDisabledExpiresAtPlayerTurnStart);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-0003")]
    [Trait("L12Evidence", "family:active-rest-repeatability")]
    public void ReadiedCourtMagicianMayPayActiveRestAgainWithoutAnInventedOncePerTurnLimit()
    {
        var game = Create(92003);
        var magician = Card("S02-0003", "court-magician-repeat");
        PrepareMain(game, magician);

        var first = game.Handle(0,
            new L12Command("activateAbility", magician.InstanceId, Ability: "disableCounters"));
        Assert.True(first.Accepted, first.Error);
        Assert.True(magician.Tapped);

        magician.Tapped = false;
        var second = game.Handle(0,
            new L12Command("activateAbility", magician.InstanceId, Ability: "disableCounters"));

        Assert.True(second.Accepted, second.Error);
        Assert.True(magician.Tapped);
        Assert.DoesNotContain(game.State.Players[0].UsedAbilities,
            key => key.Contains("disableCounters", StringComparison.Ordinal));
    }
}
