using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class OiranTransferEffectLifecycleTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var deck = Catalog.DeckAt(0);
        return new L12GameEngine(Catalog, "oiran-transfer-lifecycle", "OIRAN-TRANSFER-LIFECYCLE",
            seed, ["甲", "乙"], [deck, deck], skipPreparation: true);
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
        };
    }

    private static (L12CardInstance Source, L12CardInstance Own, L12CardInstance Enemy) Prepare(
        L12GameEngine game, bool holdResponse = true)
    {
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
            foreach (var row in player.Field)
                Array.Clear(row);

        var source = Card("ST04-06", "oiran-source");
        var own = Card("S01-0004", "oiran-own");
        var enemy = Card("S01-0109", "oiran-enemy");
        game.State.Players[0].Field[0][0] = source;
        game.State.Players[0].Field[0][1] = own;
        game.State.Players[1].Field[0][0] = enemy;
        if (holdResponse)
        {
            var response = Card("S01-0019", "oiran-response");
            response.Hidden = true;
            response.SetRound = 0;
            game.State.Players[1].Field[1][2] = response;
        }
        return (source, own, enemy);
    }

    private static (string EnemyPromptId, string OwnPromptId) Declare(L12GameEngine game,
        L12CardInstance source, L12CardInstance enemy, L12CardInstance own)
    {
        Assert.True(game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "oiranTransfer")).Accepted);
        var enemyPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("enemy-legion", enemyPrompt.Kind);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: enemyPrompt.PromptId,
            Choice: enemy.InstanceId)).Accepted);
        var ownPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("field-legion", ownPrompt.Kind);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: ownPrompt.PromptId,
            Choice: own.InstanceId)).Accepted);
        return (enemyPrompt.PromptId, ownPrompt.PromptId);
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    private static L12ActionEvent[] Results(L12GameEngine game)
        => game.State.Events.Where(entry => entry.Type == "effect-result"
                && entry.Cards.Any(card => card.InstanceId == "oiran-source"))
            .OrderBy(entry => entry.EffectSegmentIndex).ToArray();

    [Fact]
    [Trait("L12Evidence", "card:ST04-06")]
    public void BothClausesResolveAsIndependentResultsAndSurviveCheckpoint()
    {
        var game = Create(91341);
        var (source, own, enemy) = Prepare(game);
        var oldPrompts = Declare(game, source, enemy, own);
        Assert.True(source.Tapped);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");
        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint,
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

        PassResponses(game);

        var restoredOwn = game.State.Players[0].Field[0][1]!;
        var restoredEnemy = game.State.Players[1].Field[0][0]!;
        Assert.Equal(own.BaseTroops + 1000, restoredOwn.Troops);
        Assert.Equal(enemy.BaseTroops - 1000, restoredEnemy.Troops);
        var results = Results(game);
        Assert.Equal(["resolved", "resolved"], results.Select(entry => entry.EffectResultStatus));
        Assert.Equal([1, 2], results.Select(entry => entry.EffectSegmentIndex));
        Assert.All(results, entry => Assert.Equal(2, entry.EffectSegmentCount));
        Assert.False(game.Handle(0, new L12Command("resolvePrompt", PromptId: oldPrompts.EnemyPromptId,
            Choice: enemy.InstanceId)).Accepted);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:ST04-06")]
    public void EnemyClauseCanFailWhileOwnClauseStillResolves()
    {
        var game = Create(91342);
        var (source, own, enemy) = Prepare(game);
        Declare(game, source, enemy, own);
        game.State.Players[1].Field[0][0] = null;

        PassResponses(game);

        Assert.Equal(own.BaseTroops + 1000, own.Troops);
        Assert.Equal(enemy.BaseTroops, enemy.Troops);
        Assert.Equal(["failed", "resolved"], Results(game).Select(entry => entry.EffectResultStatus));
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed"
            && entry.Text.Contains("对方军团", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:ST04-06")]
    public void OwnClauseCanFailAfterEnemyClauseResolved()
    {
        var game = Create(91343);
        var (source, own, enemy) = Prepare(game);
        Declare(game, source, enemy, own);
        game.State.Players[0].Field[0][1] = null;

        PassResponses(game);

        Assert.Equal(enemy.BaseTroops - 1000, enemy.Troops);
        Assert.Equal(own.BaseTroops, own.Troops);
        Assert.Equal(["resolved", "failed"], Results(game).Select(entry => entry.EffectResultStatus));
    }

    [Fact]
    [Trait("L12Evidence", "card:ST04-06")]
    public void BothInvalidTargetsProduceTwoFailuresWithoutAFalseSuccessLog()
    {
        var game = Create(91344);
        var (source, own, enemy) = Prepare(game);
        Declare(game, source, enemy, own);
        game.State.Players[0].Field[0][1] = null;
        game.State.Players[1].Field[0][0] = null;

        PassResponses(game);

        Assert.Equal(["failed", "failed"], Results(game).Select(entry => entry.EffectResultStatus));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("本回合兵力", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("L12Evidence", "card:ST04-06")]
    public void NegationStopsTheWholeAbilityAndDoesNotRefundActiveRestCost()
    {
        var game = Create(91345);
        var (source, own, enemy) = Prepare(game);
        Declare(game, source, enemy, own);
        Assert.Single(game.State.EffectStack).Negated = true;

        PassResponses(game);

        Assert.True(source.Tapped);
        Assert.Equal(own.BaseTroops, own.Troops);
        Assert.Equal(enemy.BaseTroops, enemy.Troops);
        var result = Assert.Single(Results(game));
        Assert.Equal("negated", result.EffectResultStatus);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:ST04-06")]
    public void MissingEnemyTargetRejectsBeforeTheActiveRestCost()
    {
        var game = Create(91346);
        var (source, _, _) = Prepare(game, holdResponse: false);
        game.State.Players[1].Field[0][0] = null;

        var result = game.Handle(0, new L12Command("activateAbility", source.InstanceId,
            Ability: "oiranTransfer"));

        Assert.False(result.Accepted);
        Assert.Contains("双方战场", result.Error ?? string.Empty, StringComparison.Ordinal);
        Assert.False(source.Tapped);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    [Trait("L12Evidence", "card:ST04-06")]
    public void LegacyInFlightStackWithoutCompositeIdentityStillSettlesItsFrozenTargets()
    {
        var game = Create(91347);
        var (source, own, enemy) = Prepare(game);
        Declare(game, source, enemy, own);
        var item = Assert.Single(game.State.EffectStack);
        foreach (var key in new[]
                 {
                     "compositePlan", "compositeSegment", "compositeResponseScope", "atomicFlow",
                     "atomicContinuation", "declared:enemyTarget", "declared:ownTarget",
                 })
            item.Data.Remove(key);

        PassResponses(game);

        Assert.Equal(own.BaseTroops + 1000, own.Troops);
        Assert.Equal(enemy.BaseTroops - 1000, enemy.Troops);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
    }
}
