using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ExtendedCardEffectsTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int firstDeck, int secondDeck, int seed = 9012)
        => new(Catalog, "extended-effects", "EFFECT", seed, ["甲", "乙"], [firstDeck, secondDeck], skipPreparation: true);

    private static void ReadyMain(L12GameEngine game, int playerIndex)
    {
        game.State.ActivePlayer = playerIndex;
        game.State.Phase = L12Phase.Main;
        while (game.State.Players[playerIndex].MoraleDeck.Count > 0)
        {
            var morale = game.State.Players[playerIndex].MoraleDeck[0];
            game.State.Players[playerIndex].MoraleDeck.RemoveAt(0);
            game.State.Players[playerIndex].Morale.Add(morale);
        }
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        }
    }

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = definition.Id, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction, ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0, EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
        };
    }

    private static L12CardInstance TakeCard(L12PlayerState player, string cardId)
    {
        var card = player.Hand.Concat(player.Library).FirstOrDefault(candidate => candidate.CardId == cardId)
            ?? Card(cardId, $"test-{player.PlayerIndex}-{cardId}-{Guid.NewGuid():N}");
        player.Hand.Remove(card);
        player.Library.Remove(card);
        player.Hand.Add(card);
        return card;
    }

    [Fact]
    public void SolarCityPresetStartsWithThreeTombGuardsInGraveyard()
    {
        var game = Create(2, 3);
        var solar = game.State.Players[0];
        Assert.Equal("taiyangcheng", solar.Faction);
        Assert.Equal(3, solar.Graveyard.Count(card => card.CardId == "S01-0212"));
        Assert.DoesNotContain(solar.Library, card => card.CardId == "S01-0212");
    }

    [Fact]
    public void BeowulfEntryMillsExactlyTwoCards()
    {
        var game = Create(3, 2);
        var player = game.State.Players[0];
        var beowulf = TakeCard(player, "S01-0301");
        ReadyMain(game, 0);
        var before = player.Library.Count;
        Assert.True(game.Handle(0, new L12Command("playCard", beowulf.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        Assert.Equal(before - 2, player.Library.Count);
        Assert.Contains(game.State.Events, item => item.Type == "mill" && item.Text.Contains("贝奥武夫"));
    }

    [Fact]
    public void CanopicArtifactsDoNotReplaceThePrimaryRelic()
    {
        var game = Create(2, 3);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var ankh = player.Hand.Concat(player.Library).First(card => card.CardId == "S01-0215");
        player.Hand.Remove(ankh); player.Library.Remove(ankh); player.Hand.Add(ankh);
        Assert.True(game.Handle(0, new L12Command("playCard", ankh.InstanceId)).Accepted);
        PassResponses(game);
        if (game.State.PendingPrompts.Count > 0)
        {
            var prompt = game.State.PendingPrompts[0];
            var choice = prompt.ValidChoices.FirstOrDefault() ?? "skip";
            game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        }
        var canopic = Card("S01-0217", "test-canopic-one");
        player.Hand.Add(canopic);
        Assert.True(game.Handle(0, new L12Command("playCard", canopic.InstanceId)).Accepted);
        Assert.Equal(ankh.InstanceId, player.Relic?.InstanceId);
        Assert.Contains(player.ExtraRelics, card => card.InstanceId == canopic.InstanceId);
    }

    [Fact]
    public void AnkhSteleActiveEffectsPayTheirCostsAndResolve()
    {
        var game = Create(2, 3);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var ankh = Card("S01-0215", "test-ankh");
        player.Relic = ankh;
        var guard = player.Graveyard.First(card => card.CardId == "S01-0212");
        player.Graveyard.Remove(guard); guard.Tapped = true; player.Field[0][0] = guard;
        var discard = player.Library[0]; player.Library.RemoveAt(0); player.Hand.Add(discard);

        Assert.True(game.Handle(0, new L12Command("activateAbility", ankh.InstanceId, Ability: "ankhReady")).Accepted);
        var discardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: discardPrompt.PromptId, Choice: discard.InstanceId)).Accepted);
        PassResponses(game);
        var guardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("ankh-ready-target", guardPrompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: guardPrompt.PromptId, Choice: guard.InstanceId)).Accepted);

        Assert.True(ankh.Tapped);
        Assert.False(guard.Tapped);
        Assert.Contains(discard, player.Graveyard);

        ankh.Tapped = false; guard.Tapped = false;
        var handBefore = player.Hand.Count;
        Assert.True(game.Handle(0, new L12Command("activateAbility", ankh.InstanceId, Ability: "ankhDraw")).Accepted);
        var guardCostPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: guardCostPrompt.PromptId, Choice: guard.InstanceId)).Accepted);
        PassResponses(game);
        Assert.True(ankh.Tapped);
        Assert.True(guard.Tapped);
        Assert.Equal(handBefore + 1, player.Hand.Count);
    }

    [Fact]
    public void GramDamageActiveEffectReturnsFourAsgardLegionsInChosenOrder()
    {
        var game = Create(3, 2);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var gram = Card("S01-0317", "test-gram"); player.Relic = gram;
        var costs = player.Library.Where(card => card.CardType == "legion" && card.Faction == "asgard").Take(4).ToArray();
        foreach (var card in costs) { player.Library.Remove(card); player.Graveyard.Add(card); }
        var chosenOrder = costs.Reverse().Select(card => card.InstanceId).ToList();
        var enemyHp = game.State.Players[1].Hp;

        Assert.True(game.Handle(0, new L12Command("activateAbility", gram.InstanceId, Ability: "gramDamage")).Accepted);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: costPrompt.PromptId, CardInstanceIds: chosenOrder)).Accepted);
        PassResponses(game);

        Assert.True(gram.Tapped);
        Assert.Equal(chosenOrder, player.Library.TakeLast(4).Select(card => card.InstanceId));
        Assert.Equal(enemyHp - 1, game.State.Players[1].Hp);
    }

    [Fact]
    public void PharaohFestivalUsesHandThenGraveThenOrderedBottomStages()
    {
        var game = Create(2, 3);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var festival = player.Hand.Concat(player.Library).First(card => card.CardId == "S01-0222");
        player.Hand.Remove(festival); player.Library.Remove(festival); player.Hand.Add(festival);
        var eligible = player.Library.Where(card => card.Faction == "taiyangcheng" && card.CardId != "S01-0222").Take(2).ToArray();
        foreach (var card in eligible) player.Library.Remove(card);
        player.Library.InsertRange(0, eligible);

        Assert.True(game.Handle(0, new L12Command("playCard", festival.InstanceId)).Accepted);
        PassResponses(game);
        var handPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("festival-hand", handPrompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: handPrompt.PromptId, Choice: eligible[0].InstanceId)).Accepted);
        var gravePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("festival-grave", gravePrompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: gravePrompt.PromptId, Choice: eligible[1].InstanceId)).Accepted);
        var orderPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("all-bottom", orderPrompt.Data["placementMode"]);
        var order = orderPrompt.ValidChoices.AsEnumerable().Reverse().ToList();
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: orderPrompt.PromptId, BottomCardInstanceIds: order)).Accepted);

        Assert.Contains(eligible[0], player.Hand);
        Assert.Contains(eligible[1], player.Graveyard);
        Assert.Equal(order, player.Library.TakeLast(order.Count).Select(card => card.InstanceId));
    }

    [Fact]
    public void SolarCityPlayerChoosesWhetherTombGuardsPayAPlayedCardsCost()
    {
        var game = Create(2, 3);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var guard = player.Graveyard.First(card => card.CardId == "S01-0212");
        player.Graveyard.Remove(guard); player.Field[0][0] = guard;
        var legion = Card("S01-0205", "test-paid-legion"); player.Hand.Add(legion);

        Assert.True(game.Handle(0, new L12Command("playCard", legion.InstanceId, Row: 0, Slot: 1)).Accepted);
        var paymentPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("play-morale-choice", paymentPrompt.Continuation);
        var activeMoraleBefore = player.Morale.Count(card => !card.Tapped);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: paymentPrompt.PromptId, Choice: "yes")).Accepted);

        Assert.True(guard.Tapped);
        Assert.Equal(activeMoraleBefore - (legion.Cost - 1), player.Morale.Count(card => !card.Tapped));
        Assert.Equal(legion, player.Field[0][1]);
    }

    [Fact]
    public void SolarCityPlayerAlsoChoosesTombGuardPaymentForActiveAbilities()
    {
        var game = Create(2, 3);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        while (player.Hand.Count > 3) { var card = player.Hand[^1]; player.Hand.RemoveAt(player.Hand.Count - 1); player.Library.Add(card); }
        var guard = player.Graveyard.First(card => card.CardId == "S01-0212");
        player.Graveyard.Remove(guard); player.Field[0][0] = guard;

        Assert.True(game.Handle(0, new L12Command("activateAbility", "faction-0", Ability: "sunDraw")).Accepted);
        var paymentPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("active-morale-choice", paymentPrompt.Continuation);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: paymentPrompt.PromptId, Choice: "yes")).Accepted);

        Assert.True(guard.Tapped);
        PassResponses(game);
    }

    [Fact]
    public void ExtendedAbilityMetadataIsPublishedBySnapshots()
    {
        var game = Create(0, 1);
        var snapshot = game.SnapshotFor(0);
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
        Assert.Contains("drawCycle", json);
        Assert.Contains("factionAddActive", json);
    }

    [Fact]
    public void EgilDoesNotRepeatHisEntryEffectWhenAttacking()
    {
        var game = Create(3, 2);
        ReadyMain(game, 0);
        game.State.Round = 2;
        var egil = Card("S01-0316", "test-egil");
        var target = Card("S01-0212", "test-target");
        egil.SummonRound = 0;
        target.SummonRound = 0;
        game.State.Players[0].Field[0][0] = egil;
        game.State.Players[1].Field[0][0] = target;

        Assert.True(game.Handle(0, new L12Command("attack", egil.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        PassResponses(game);

        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Text.Contains("夺命诗人埃吉尔"));
        Assert.Empty(game.State.EffectStack);
        Assert.Equal(L12Phase.Defense, game.State.Phase);
    }

    [Fact]
    public void AyLetsThePlayerChooseWhereTheTombGuardEnters()
    {
        var game = Create(2, 3);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var ay = TakeCard(player, "S01-0208");

        Assert.True(game.Handle(0, new L12Command("playCard", ay.InstanceId, Row: 0, Slot: 0)).Accepted);
        PassResponses(game);
        var placement = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("queued-summon-slot", placement.Data["action"]);
        Assert.Contains("阿伊", placement.Text);
        var chosenSlot = placement.ValidChoices.Last();
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: placement.PromptId, Choice: chosenSlot)).Accepted);

        var parts = chosenSlot.Split(':');
        var row = int.Parse(parts[0]);
        var slot = int.Parse(parts[1]);
        Assert.Equal("S01-0212", player.Field[row][slot]?.CardId);
        Assert.True(player.Field[row][slot]!.Tapped);
    }

    [Fact]
    public void GramBottomReplacementAlwaysSendsTombGuardToItsOwnersGraveyard()
    {
        var game = Create(3, 2);
        var asgard = game.State.Players[0];
        var solar = game.State.Players[1];
        ReadyMain(game, 0);
        var guard = solar.Graveyard.First(card => card.CardId == "S01-0212");
        solar.Graveyard.Remove(guard);
        solar.Field[0][0] = guard;
        var gram = TakeCard(asgard, "S01-0317");

        Assert.True(game.Handle(0, new L12Command("playCard", gram.InstanceId)).Accepted);
        PassResponses(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("gram-bottom", prompt.Data["action"]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: guard.InstanceId)).Accepted);

        Assert.DoesNotContain(solar.Library, card => card.InstanceId == guard.InstanceId);
        Assert.Contains(solar.Graveyard, card => card.InstanceId == guard.InstanceId);
        Assert.Contains(game.State.Events, item => item.Type == "replacement" && item.Cards.Any(card => card.InstanceId == guard.InstanceId));
    }

    [Theory]
    [InlineData("S01-0303")]
    [InlineData("S01-0304")]
    [InlineData("S01-0308")]
    [InlineData("S01-0310")]
    [InlineData("S01-0314")]
    public void AsgardSelfDamageEntryDiscountIsAlwaysAnExplicitChoice(string cardId)
    {
        var game = Create(3, 2);
        var player = game.State.Players[0];
        ReadyMain(game, 0);
        var card = player.Hand.Concat(player.Library).FirstOrDefault(candidate => candidate.CardId == cardId) ?? Card(cardId, $"test-{cardId}");
        player.Hand.Remove(card); player.Library.Remove(card); player.Hand.Add(card);
        var hp = player.Hp;

        Assert.True(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)).Accepted);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("play-cost-choice", prompt.Continuation);
        Assert.Equal(["yes", "no"], prompt.ValidChoices);
        Assert.Null(player.Field[0][0]);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "yes")).Accepted);

        Assert.Equal(hp - 1, player.Hp);
        Assert.Equal(card.InstanceId, player.Field[0][0]?.InstanceId);
    }
}
