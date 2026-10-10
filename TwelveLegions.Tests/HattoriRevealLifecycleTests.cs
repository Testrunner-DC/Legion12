using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class HattoriRevealLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var deck = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "hattori-reveal", "HATTORI-REVEAL", seed,
            ["甲", "乙"], [deck, deck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            foreach (var row in player.Field) Array.Clear(row);
        }
        return game;
    }

    private static L12CardInstance Hanzo(string instanceId = "hattori-hidden")
    {
        var definition = Catalog.Cards["S01-0415"];
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            OwnerIndex = 0,
            Hidden = true,
            SummonRound = 1,
        };
    }

    private static L12CardInstance Activate(L12GameEngine game)
    {
        var hanzo = Hanzo();
        game.State.Players[0].Field[0][0] = hanzo;
        var result = game.Handle(0, new L12Command("activateAbility", hanzo.InstanceId,
            Ability: "revealHidden"));
        Assert.True(result.Accepted, result.Error);
        Assert.Single(game.State.EffectStack);
        return hanzo;
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static L12ActionEvent Result(L12GameEngine game)
        => Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == "S01-0415"));

    [Fact]
    public void PrintedActiveAbilityUsesOneStructuredSettlementScene()
    {
        var ability = Catalog.AtomicEffects.Find("S01-0415")!.Abilities
            .Single(candidate => candidate.Sequence == 3);
        var scene = Assert.Single(ability.Presentations, candidate =>
            candidate.Flow == L12SingleSegmentEffectPresentations.Flow);

        Assert.Equal(1, scene.SegmentIndex);
        Assert.Equal(1, scene.SegmentCount);
    }

    [Fact]
    [Trait("L12Evidence", "ability:revealHidden")]
    public void ResolutionRevealsTheAuthoritativeFieldInstanceWithoutResettingSummonRound()
    {
        var game = Create(91701);
        var hanzo = Activate(game);
        PassResponses(game);

        Assert.False(hanzo.Hidden);
        Assert.Equal(1, hanzo.SummonRound);
        Assert.Equal("resolved", Result(game).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:revealHidden")]
    public void NegationKeepsTheFieldInstanceHidden()
    {
        var game = Create(91702);
        var hanzo = Activate(game);
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.True(hanzo.Hidden);
        Assert.Equal("negated", Result(game).EffectResultStatus);
    }

    [Fact]
    [Trait("L12Evidence", "ability:revealHidden")]
    public void LeavingTheFieldDuringResponsesFailsInsteadOfMutatingTheFrozenSnapshot()
    {
        var game = Create(91703);
        var hanzo = Activate(game);
        game.State.Players[0].Field[0][0] = null;
        game.State.Players[0].Graveyard.Add(hanzo);
        PassResponses(game);

        Assert.True(hanzo.Hidden);
        Assert.Equal("failed", Result(game).EffectResultStatus);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("已离场或不再为覆盖状态", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:revealHidden")]
    public void BecomingFaceUpBeforeResolutionFailsWithoutASecondStateChange()
    {
        var game = Create(91704);
        var hanzo = Activate(game);
        hanzo.Hidden = false;
        PassResponses(game);

        Assert.False(hanzo.Hidden);
        Assert.Equal("failed", Result(game).EffectResultStatus);
        Assert.DoesNotContain(game.State.Events, entry => entry.Text.Contains("主动翻回正面", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "ability:revealHidden")]
    public void V2RestoreResolvesOnceAndRejectsTheOldResponsePrompt()
    {
        var game = Create(91705);
        var hanzo = Activate(game);
        var oldPrompt = Assert.Single(game.State.PendingPrompts);
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        PassResponses(game);

        var restored = Assert.IsType<L12CardInstance>(game.State.Players[0].Field[0][0]);
        Assert.Equal(hanzo.InstanceId, restored.InstanceId);
        Assert.False(restored.Hidden);
        Assert.False(game.Handle(oldPrompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: oldPrompt.PromptId, Choice: "pass")).Accepted);
        Assert.Equal("resolved", Result(game).EffectResultStatus);
        Assert.Single(game.State.Events, entry => entry.Text.Contains("主动翻回正面", StringComparison.Ordinal));
    }
}
