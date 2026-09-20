using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ActiveRestDeferredStateLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed, bool autoPassEmptyResponses)
    {
        var game = new L12GameEngine(Catalog, "active-rest-deferred-state", "ACTIVE-REST-STATE", seed,
            ["甲", "乙"], [Catalog.DeckAt(0), Catalog.DeckAt(0)], skipPreparation: true,
            autoPassEmptyResponses: autoPassEmptyResponses, concealHiddenResponseAvailability: false);
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
            player.Removed.Clear();
            player.Resolving.Clear();
            player.ExtraRelics.Clear();
            player.Relic = null;
            player.Morale.Clear();
            player.UsedAbilities.Clear();
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
            OwnerIndex = 0,
            SummonRound = -1,
        };
    }

    private static (L12GameEngine Game, L12CardInstance Source, L12CardInstance? Ally) Setup(
        string ability, int seed, bool autoPassEmptyResponses)
    {
        var game = Create(seed, autoPassEmptyResponses);
        var player = game.State.Players[0];
        var (cardId, allyId) = ability switch
        {
            "imhotepDiscount" => ("S02-0204", (string?)null),
            "oasisDancerBuff" => ("ST02-05", "ST02-09"),
            "christinaFreeTactic" => ("ST03-05", (string?)null),
            _ => throw new ArgumentOutOfRangeException(nameof(ability)),
        };
        var source = Card(cardId, $"deferred-{ability}-source");
        player.Field[0][0] = source;
        L12CardInstance? ally = null;
        if (allyId is not null)
        {
            ally = Card(allyId, $"deferred-{ability}-ally");
            player.Field[0][1] = ally;
        }
        return (game, source, ally);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100
             && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static void AssertStateApplied(L12GameEngine game, string ability, L12CardInstance? ally,
        int expectedApplications)
    {
        var player = game.State.Players[0];
        switch (ability)
        {
            case "imhotepDiscount":
                Assert.Equal(expectedApplications, player.NextS2SunDisasterLegionDiscount);
                break;
            case "oasisDancerBuff":
                Assert.NotNull(ally);
                Assert.Equal(ally.BaseTroops + expectedApplications * 1000, ally.CurrentTroops);
                break;
            case "christinaFreeTactic":
                Assert.Equal(expectedApplications > 0,
                    player.UsedAbilities.Contains($"starter-christina-free-tactic:{game.State.TurnSerial}"));
                break;
        }
    }

    [Theory]
    [InlineData("imhotepDiscount")]
    [InlineData("oasisDancerBuff")]
    [InlineData("christinaFreeTactic")]
    [Trait("L12Evidence", "family:active-rest-deferred-state-negation")]
    public void NegatedDeferredStateKeepsActiveRestCostAndWritesNoState(string ability)
    {
        var (game, source, ally) = Setup(ability, 9301, autoPassEmptyResponses: false);

        var activation = game.Handle(0,
            new L12Command("activateAbility", source.InstanceId, Ability: ability));
        Assert.True(activation.Accepted, activation.Error);
        Assert.True(source.Tapped);
        AssertStateApplied(game, ability, ally, 0);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(source.Tapped);
        AssertStateApplied(game, ability, ally, 0);
        Assert.Equal("negated", Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == source.InstanceId)).EffectResultStatus);
    }

    [Theory]
    [InlineData("imhotepDiscount")]
    [InlineData("oasisDancerBuff")]
    [InlineData("christinaFreeTactic")]
    [Trait("L12Evidence", "family:active-rest-deferred-state-repeatability")]
    public void ReadiedSourceCanPayActiveRestAgainWithoutAnInventedUsageLimit(string ability)
    {
        var (game, source, ally) = Setup(ability, 9302, autoPassEmptyResponses: true);

        var first = game.Handle(0,
            new L12Command("activateAbility", source.InstanceId, Ability: ability));
        Assert.True(first.Accepted, first.Error);
        AssertStateApplied(game, ability, ally, 1);

        source.Tapped = false;
        var second = game.Handle(0,
            new L12Command("activateAbility", source.InstanceId, Ability: ability));
        Assert.True(second.Accepted, second.Error);
        Assert.True(source.Tapped);
        AssertStateApplied(game, ability, ally,
            ability == "christinaFreeTactic" ? 1 : 2);
        Assert.DoesNotContain(game.State.Players[0].UsedAbilities,
            key => key.Contains(ability, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("imhotepDiscount")]
    [InlineData("oasisDancerBuff")]
    [InlineData("christinaFreeTactic")]
    [Trait("L12Evidence", "family:active-rest-deferred-state-v2")]
    public void PendingDeferredStateRestoresFromV2AndResolvesExactlyOnce(string ability)
    {
        var (game, source, ally) = Setup(ability, 9303, autoPassEmptyResponses: false);
        var activation = game.Handle(0,
            new L12Command("activateAbility", source.InstanceId, Ability: ability));
        Assert.True(activation.Accepted, activation.Error);
        AssertStateApplied(game, ability, ally, 0);

        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        source = game.State.Players[0].Field[0][0]!;
        ally = game.State.Players[0].Field[0][1];
        PassResponses(game);

        Assert.True(source.Tapped);
        AssertStateApplied(game, ability, ally, 1);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.InstanceId == source.InstanceId));
    }

    [Fact]
    [Trait("L12Evidence", "ability:oasisDancerBuff")]
    public void OasisDancerBuffExpiresAtTheEndOfTheCurrentTurn()
    {
        var (game, _, ally) = Setup("oasisDancerBuff", 9304, autoPassEmptyResponses: true);
        Assert.NotNull(ally);
        var originalTroops = ally.BaseTroops;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "deferred-oasisDancerBuff-source",
            Ability: "oasisDancerBuff")).Accepted);
        Assert.Equal(originalTroops + 1000, ally.CurrentTroops);
        Assert.True(game.Handle(0, new L12Command("endTurn")).Accepted);

        Assert.Equal(originalTroops, ally.CurrentTroops);
    }
}
