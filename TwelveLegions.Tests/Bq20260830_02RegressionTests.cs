using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class Bq20260830_02RegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 68201)
        => new(Catalog, "bq-20260830-02", "BQ083002", seed, ["甲", "乙"], [0, 0], skipPreparation: true);

    private static L12GameEngine CreateWithFirstMaster(string masterId, int seed, string disasterMode = "none")
    {
        var baseDeck = Catalog.DeckAt(0);
        var firstDeck = new L12PresetDeckDefinition
        {
            Name = $"{masterId}批次回归牌库",
            MasterId = masterId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        return new L12GameEngine(Catalog, "bq-20260830-02", "BQ083002", seed,
            ["甲", "乙"], [firstDeck, baseDeck], skipPreparation: true, disasterMode: disasterMode);
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
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
        };
    }

    private static void AddReadyMorale(L12PlayerState player, int count)
    {
        while (player.Morale.Count(card => !card.Tapped) < count)
        {
            var morale = player.MoraleDeck[0];
            player.MoraleDeck.RemoveAt(0);
            morale.Tapped = false;
            player.Morale.Add(morale);
        }
    }

    private static L12Prompt ResponseFor(L12GameEngine game, int playerIndex)
    {
        for (var guard = 0; guard < 8; guard++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", prompt.Kind);
            if (prompt.PlayerIndex == playerIndex) return prompt;
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
        throw new InvalidOperationException("响应优先权未到达预期玩家");
    }

    [Fact]
    public void ValkyrieFirstPlayerFirstTurnStillMillsInsteadOfUsingTheNormalDrawSkip()
    {
        var game = CreateWithFirstMaster("S01-03M1", 68201);
        game.State.FirstPlayer = 0;
        game.State.ActivePlayer = 0;
        game.State.Round = 1;
        var player = game.State.Players[0];
        var handBefore = player.Hand.Count;
        var libraryBefore = player.Library.Count;

        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);

        Assert.Equal(handBefore, player.Hand.Count);
        Assert.Equal(libraryBefore - 2, player.Library.Count);
        Assert.Equal(2, player.Graveyard.Count);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("瓦尔基里将抽牌阶段改为弃置牌库顶部2张牌", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "draw-skipped" && entry.PlayerIndex == 0);
    }

    [Fact]
    public void VanishedDerivedLegionUsesItsDepartureSnapshotForStateBasedDeathTrigger()
    {
        var game = Create(68202);
        var player = game.State.Players[0];
        var xiaotian = Card("S02-01S1", "snapshot-xiaotian");
        var mover = Card("S01-0002", "snapshot-mover");
        xiaotian.Troops = 0;
        xiaotian.SummonRound = 0;
        mover.SummonRound = 0;
        player.Field[0][0] = xiaotian;
        player.Field[0][2] = mover;
        AddReadyMorale(player, 1);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        var moved = game.Handle(0, new L12Command("move", mover.InstanceId, Row: 1, Slot: 2));

        Assert.True(moved.Accepted, moved.Error);
        Assert.Null(player.Field[0][0]);
        Assert.DoesNotContain(player.Graveyard, card => card.InstanceId == xiaotian.InstanceId);
        Assert.DoesNotContain(player.Removed, card => card.InstanceId == xiaotian.InstanceId);
        var death = Assert.Single(game.State.PendingPrompts, prompt =>
        {
            if (prompt.Continuation != "pending-activation"
                || !prompt.Data.TryGetValue("activationId", out var activationId)) return false;
            return game.State.PendingActivations.Single(candidate => candidate.ActivationId == activationId)
                .SourceCardId == xiaotian.CardId;
        });
        Assert.Equal(0, death.PlayerIndex);
        Assert.Equal("effect-decision", death.Data["uiPattern"]);
        Assert.False(death.Data.ContainsKey("previewCardId"));
        Assert.Contains(game.State.Events, entry => entry.Type == "derived-vanished"
            && entry.Cards.Any(card => card.InstanceId == xiaotian.InstanceId));
    }

    [Fact]
    public void SameRoundAbsoluteDefenseCanBeNestedUnderSameRoundWisdomCodex()
    {
        var game = Create(68203);
        var defender = game.State.Players[0];
        var actor = game.State.Players[1];
        var absoluteDefense = Card("S01-0016", "same-round-absolute-defense");
        var wisdom = Card("S01-0224", "same-round-wisdom");
        var discard = Card("S01-0001", "same-round-defense-discard");
        var tactic = Card("S01-0219", "same-round-base-artifact");
        absoluteDefense.Hidden = true;
        absoluteDefense.SetRound = 2;
        wisdom.Hidden = true;
        wisdom.SetRound = 2;
        defender.Field[1][0] = absoluteDefense;
        defender.Hand.Clear();
        defender.Hand.Add(discard);
        actor.Field[1][0] = wisdom;
        actor.Hand.Clear();
        actor.Hand.Add(tactic);
        AddReadyMorale(actor, tactic.Cost);
        game.State.ActivePlayer = 1;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(1, new L12Command("playCard", tactic.InstanceId)).Accepted);
        var defenseWindow = ResponseFor(game, 0);
        Assert.Contains(absoluteDefense.InstanceId, defenseWindow.ValidChoices);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: defenseWindow.PromptId,
            Choice: absoluteDefense.InstanceId)).Accepted);
        var discardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("stack-response-discard", discardPrompt.Continuation);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: discardPrompt.PromptId,
            Choice: discard.InstanceId)).Accepted);

        var wisdomWindow = ResponseFor(game, 1);
        Assert.Contains(wisdom.InstanceId, wisdomWindow.ValidChoices);
        Assert.Equal("S01-0016", game.State.EffectStack[^1].SourceCardId);
    }

    [Fact]
    public void SameRoundSeasonTwoCounterCanRespondImmediately()
    {
        var game = Create(68204);
        var counterOwner = game.State.Players[0];
        var actor = game.State.Players[1];
        var counter = Card("S02-0106", "same-round-s2-counter");
        var tactic = Card("S01-0219", "same-round-s2-base-artifact");
        counter.Hidden = true;
        counter.SetRound = 2;
        counterOwner.Field[1][0] = counter;
        actor.Hand.Clear();
        actor.Hand.Add(tactic);
        AddReadyMorale(actor, tactic.Cost);
        game.State.ActivePlayer = 1;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(1, new L12Command("playCard", tactic.InstanceId)).Accepted);
        var response = ResponseFor(game, 0);
        Assert.Contains(counter.InstanceId, response.ValidChoices);
    }

    [Fact]
    public void TsukuyomiBackToFrontMovesAccumulatePerMovementWithoutConsumingCavalryMove()
    {
        var game = CreateWithFirstMaster("S02-04M1", 68205);
        var player = game.State.Players[0];
        var mover = Card("S01-0409", "tsukuyomi-stacking-mover");
        var hippolyta = Card("S02-0510", "tsukuyomi-free-move-source");
        mover.SummonRound = 0;
        hippolyta.SummonRound = 0;
        hippolyta.Tapped = true;
        player.Field[1][0] = mover;
        player.Field[0][2] = hippolyta;
        player.Morale.Clear();
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("move", mover.InstanceId, Row: 0, Slot: 0)).Accepted);
        Assert.True(game.Handle(0, new L12Command("move", mover.InstanceId, Row: 1, Slot: 0)).Accepted);
        Assert.True(game.Handle(0, new L12Command("move", mover.InstanceId, Row: 0, Slot: 0)).Accepted);
        Assert.Equal(-1, mover.LastCavalryMoveTurn);

        var attack = game.Handle(0, new L12Command("attack", mover.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        Assert.Equal(2000, game.State.PendingDefense?.TemporaryAttackerTroopsBonus);
        Assert.Equal(-1, mover.LastCavalryMoveTurn);
    }

    public static TheoryData<string> TrialLegionIds => new()
    {
        "S02-0618", "S02-0609", "S02-0604", "S02-0613",
        "S02-0606", "S02-0614", "S02-0617", "S02-0610",
    };

    [Theory]
    [MemberData(nameof(TrialLegionIds))]
    [Trait("L12Evidence", "entry:trial")]
    public void EveryTrialLegionPublishesAnIndependentTrialAbility(string cardId)
    {
        var game = Create(68206);
        Assert.True(game.HandleGm(new L12GmCommand("placeCard", 0, cardId, Row: 0, Slot: 0,
            TriggerEffects: false)).Accepted);

        var card = Assert.IsType<L12CardInstance>(game.State.Players[0].Field[0][0]);
        Assert.Contains(card.Abilities, ability => ability.Id == "trialAdvance");
        Assert.All(card.Abilities.Where(ability => ability.Id != "trialAdvance"),
            ability => Assert.NotEqual("trialAdvance", ability.Id));

        var snapshot = JsonSerializer.SerializeToElement(game.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var battlefieldAbilities = snapshot.GetProperty("players")[0].GetProperty("field")[0][0]
            .GetProperty("abilities").EnumerateArray().ToArray();
        Assert.Contains(battlefieldAbilities,
            ability => ability.GetProperty("id").GetString() == "trialAdvance");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewlyFlippedDestructionExecutesTurnStartEffectOnNormalAndExtraTurns(bool extraTurn)
    {
        var game = CreateWithFirstMaster("S01-03M1", extraTurn ? 68208 : 68207, disasterMode: "all");
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 3;
        game.State.TurnSerial = 2;
        game.State.Phase = L12Phase.End;
        game.State.ExtraTurnsForPlayer = extraTurn ? 0 : -1;
        game.State.ActiveDisaster = null;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(Card("S01-DS10", $"turn-start-destruction-{extraTurn}"));
        game.State.DisasterValue = 9;
        var hpBefore = game.State.Players.Select(player => player.Hp).ToArray();

        Assert.True(game.HandleGm(new L12GmCommand("nextPhase")).Accepted);

        Assert.Equal("S01-DS10", game.State.ActiveDisaster?.CardId);
        Assert.Equal(hpBefore[0] - 1, game.State.Players[0].Hp);
        Assert.Equal(hpBefore[1] - 1, game.State.Players[1].Hp);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Equal(extraTurn ? 0 : 1, game.State.ActivePlayer);
    }

    [Fact]
    public void NewlyFlippedOpeningDestructionExecutesTurnStartEffectExactlyOnceAndCannotBeRespondedTo()
    {
        var game = CreateWithFirstMaster("S01-03M1", 68209, disasterMode: "all");
        game.State.FirstPlayer = 0;
        game.State.ActivePlayer = 0;
        game.State.Round = 1;
        game.State.ActiveDisaster = null;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(Card("S01-DS10", "opening-destruction"));
        game.State.DisasterValue = 9;
        var hpBefore = game.State.Players.Select(player => player.Hp).ToArray();

        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);

        Assert.Equal(hpBefore[0] - 1, game.State.Players[0].Hp);
        Assert.Equal(hpBefore[1] - 1, game.State.Players[1].Hp);
        Assert.Equal(2, game.State.Events.Count(entry => entry.Type == "damage"
            && entry.Text.Contains("〈堙灭〉", StringComparison.Ordinal)));
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }
}
