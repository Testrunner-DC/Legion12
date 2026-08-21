using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class NewSystemsTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(bool preparation = false, int seed = 4421)
        => new(Catalog, "new-systems", "RULE12", seed, ["甲", "乙"], [0, 1], skipPreparation: !preparation);

    private static L12CardInstance TakeCard(L12GameEngine game, int playerIndex, string cardId)
    {
        var player = game.State.Players[playerIndex];
        var card = player.Hand.Concat(player.Library).First(item => item.CardId == cardId);
        player.Hand.Remove(card);
        player.Library.Remove(card);
        return card;
    }

    private static void AddAllMorale(L12PlayerState player)
    {
        foreach (var morale in player.MoraleDeck.ToArray())
        {
            player.MoraleDeck.Remove(morale);
            morale.Tapped = false;
            player.Morale.Add(morale);
        }
    }

    private static void AddActiveMorale(L12PlayerState player, int count)
    {
        while (player.Morale.Count < count)
        {
            var morale = player.MoraleDeck[0];
            player.MoraleDeck.RemoveAt(0);
            morale.Tapped = false;
            player.Morale.Add(morale);
        }
    }

    private static L12CardInstance CreateInstance(string cardId, string instanceId)
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
        };
    }

    [Fact]
    public void CatalogLoadsTheS2ArchiveAlongsideS1()
    {
        Assert.Equal(248, Catalog.Cards.Count);
        Assert.Equal("始皇帝 嬴政", Catalog.Cards["S02-0101"].NameZh);
        Assert.Equal("destruction", Catalog.Cards["S02-DS01"].CardType);
        Assert.Equal("otherworld", Catalog.Cards["S02-06M1"].Faction);
    }

    [Fact]
    public void PrideDisasterAddsOneToEveryLegionPlayCost()
    {
        var game = Create(seed: 5531);
        var player = game.State.Players[0];
        var legion = CreateInstance("S02-0004", "pride-legion");
        player.Hand.Clear();
        player.Hand.Add(legion);
        AddActiveMorale(player, legion.Cost + 1);
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = CreateInstance("S02-DS06", "pride-disaster");

        var result = game.Handle(0, new L12Command("playCard", legion.InstanceId, Row: 0, Slot: 0));

        Assert.True(result.Accepted, result.Error);
        Assert.Equal(legion.Cost + 1, player.Morale.Count(card => card.Tapped));
    }

    [Fact]
    public void SleeplessNightDamagesMasterWhenAnyActiveRestAbilityIsUsed()
    {
        var game = Create(seed: 5532);
        var player = game.State.Players[0];
        var baiQi = CreateInstance("S01-0109", "sleepless-baiqi");
        player.Field[0][0] = baiQi;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = CreateInstance("S02-DS03", "sleepless-disaster");
        var hpBefore = player.Hp;

        var result = game.Handle(0, new L12Command("activateAbility", baiQi.InstanceId, Ability: "addMorale"));

        Assert.True(result.Accepted, result.Error);
        Assert.Equal(hpBefore - 1, player.Hp);
    }

    [Fact]
    public void PrideDisasterAddsOneMoraleToMasterEffectCost()
    {
        var game = Create(seed: 5533);
        var owner = Enumerable.Range(0, game.State.Players.Length)
            .Single(index => game.State.Players[index].MasterId == "S01-01M1");
        var player = game.State.Players[owner];
        AddAllMorale(player);
        foreach (var morale in player.Morale) morale.Tapped = true;
        foreach (var morale in player.Morale.Take(2)) morale.Tapped = false;
        game.State.ActivePlayer = owner;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = CreateInstance("S02-DS06", "pride-master-disaster");

        var result = game.Handle(owner,
            new L12Command("activateAbility", $"master-{owner}", Ability: "drawCycle"));

        Assert.True(result.Accepted, result.Error);
        Assert.DoesNotContain(player.Morale, card => !card.Tapped);
    }

    [Fact]
    public void DivineBalanceLetsBothPlayersDiscardSimultaneously()
    {
        var game = Create(seed: 5528);
        game.State.Phase = L12Phase.Main;
        game.State.DisasterValue = 9;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(CreateInstance("S01-DS06", "test-divine-balance"));

        Assert.True(game.Handle(game.State.ActivePlayer, new L12Command("endTurn")).Accepted);
        var confirmations = game.State.PendingPrompts.Where(prompt => prompt.Continuation == "disaster-trigger-confirm").ToArray();
        Assert.Equal(2, confirmations.Length);
        foreach (var confirmation in confirmations)
            Assert.True(game.Handle(confirmation.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: confirmation.PromptId)).Accepted);

        while (game.State.PendingPrompts.Count == 1 && game.State.PendingPrompts[0].Kind == "response")
        {
            var response = game.State.PendingPrompts[0];
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        }

        var discardPrompts = game.State.PendingPrompts
            .Where(prompt => prompt.Data.GetValueOrDefault("action") == "disaster-discard").ToArray();
        Assert.Equal(2, discardPrompts.Length);
        Assert.Equal([0, 1], discardPrompts.Select(prompt => prompt.PlayerIndex).Order().ToArray());
        Assert.All(discardPrompts, prompt => Assert.Equal("true", prompt.Data["simultaneous"]));

        var first = discardPrompts[0];
        var firstHandBefore = game.State.Players[first.PlayerIndex].Hand.Select(card => card.InstanceId).ToArray();
        Assert.True(game.Handle(first.PlayerIndex, new L12Command("resolvePrompt", PromptId: first.PromptId,
            Choice: first.ValidChoices[0])).Accepted);
        Assert.Single(game.State.PendingPrompts, prompt => prompt.Data.GetValueOrDefault("action") == "disaster-discard");
        Assert.Equal(firstHandBefore, game.State.Players[first.PlayerIndex].Hand.Select(card => card.InstanceId));

        var second = game.State.PendingPrompts.Single(prompt => prompt.Data.GetValueOrDefault("action") == "disaster-discard");
        Assert.True(game.Handle(second.PlayerIndex, new L12Command("resolvePrompt", PromptId: second.PromptId,
            Choice: second.ValidChoices[0])).Accepted);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Data.GetValueOrDefault("action") == "disaster-discard");
    }

    [Fact]
    public void DragonDescentLetsBothPlayersOrderTheirGraveyardsSimultaneously()
    {
        var game = Create(seed: 5530);
        game.State.Phase = L12Phase.Main;
        game.State.DisasterValue = 9;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(CreateInstance("S01-DS05", "test-dragon-descent"));
        game.State.Players[0].Graveyard.Add(CreateInstance("S01-0001", "grave-a"));
        game.State.Players[1].Graveyard.Add(CreateInstance("S01-0002", "grave-b"));

        Assert.True(game.Handle(game.State.ActivePlayer, new L12Command("endTurn")).Accepted);
        foreach (var confirmation in game.State.PendingPrompts
                     .Where(prompt => prompt.Continuation == "disaster-trigger-confirm").ToArray())
            Assert.True(game.Handle(confirmation.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: confirmation.PromptId)).Accepted);

        while (game.State.PendingPrompts.Count == 1 && game.State.PendingPrompts[0].Kind == "response")
        {
            var response = game.State.PendingPrompts[0];
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        }

        var orderPrompts = game.State.PendingPrompts
            .Where(prompt => prompt.Data.GetValueOrDefault("action") == "disaster-grave-bottom").ToArray();
        Assert.Equal(2, orderPrompts.Length);
        Assert.Equal([0, 1], orderPrompts.Select(prompt => prompt.PlayerIndex).Order().ToArray());
        Assert.All(orderPrompts, prompt => Assert.Equal("true", prompt.Data["simultaneous"]));

        var first = orderPrompts[0];
        Assert.True(game.Handle(first.PlayerIndex, new L12Command("resolvePrompt", PromptId: first.PromptId,
            CardInstanceIds: first.ValidChoices.ToList())).Accepted);
        Assert.Single(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "disaster-grave-bottom");
    }

    [Fact]
    public void PreparationUsesOrderedImmediatePublicDisasterChoices()
    {
        var game = Create(preparation: true);
        Assert.Equal(L12Phase.Initiative, game.State.Phase);
        Assert.Single(game.State.PendingPrompts);
        var initiative = game.State.PendingPrompts.Single();
        Assert.Equal(game.State.DiceWinner, initiative.PlayerIndex);
        Assert.NotNull(game.SnapshotFor(1 - initiative.PlayerIndex).WaitingPrompt);
        Assert.True(game.Handle(initiative.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: initiative.PromptId, Choice: "first")).Accepted);

        var sawRandomReveal = false;
        string[]? firstPlayerCandidates = null;
        while (game.State.Phase == L12Phase.DisasterPreparation)
        {
            if (game.State.PendingPrompts.Count == 2)
            {
                Assert.All(game.State.PendingPrompts, prompt => Assert.Equal("disaster-reveal", prompt.Kind));
                var sharedPreview = game.State.PendingPrompts[0].Data["previewCardId"];
                var revealPrompts = game.State.PendingPrompts.ToArray();
                for (var revealIndex = 0; revealIndex < revealPrompts.Length; revealIndex++)
                {
                    var reveal = revealPrompts[revealIndex];
                    Assert.Equal(sharedPreview, reveal.Data["previewCardId"]);
                    Assert.True(reveal.Data.ContainsKey($"{sharedPreview}:image"));
                    Assert.True(game.Handle(reveal.PlayerIndex,
                        new L12Command("resolvePrompt", PromptId: reveal.PromptId)).Accepted);
                    if (revealIndex == 0) Assert.NotNull(game.SnapshotFor(reveal.PlayerIndex).WaitingPrompt);
                }
                sawRandomReveal = true;
                continue;
            }
            var prompt = Assert.Single(game.State.PendingPrompts);
            var beforeBans = game.State.BannedDisasters.Count;
            var choice = prompt.ValidChoices.FirstOrDefault();
            if (prompt.Kind == "disaster-reveal")
            {
                sawRandomReveal = true;
                Assert.Empty(prompt.ValidChoices);
                Assert.Equal(0, prompt.MinChoose);
                Assert.True(prompt.Data.TryGetValue("previewCardId", out var previewId));
                Assert.True(prompt.Data.ContainsKey($"{previewId}:image"));
                Assert.True(prompt.Data.ContainsKey($"{previewId}:effect"));
            }
            if (prompt.Continuation == "setup-first-pick")
                firstPlayerCandidates = prompt.ValidChoices.ToArray();
            if (prompt.Continuation == "setup-second-pick" && firstPlayerCandidates is not null)
                Assert.DoesNotContain(prompt.ValidChoices, id => firstPlayerCandidates.Contains(id));
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                Choice: choice)).Accepted);
            if (prompt.Kind == "disaster-ban")
            {
                Assert.True(prompt.Data.ContainsKey($"{choice}:image"));
                Assert.True(prompt.Data.ContainsKey($"{choice}:effect"));
                Assert.Equal(beforeBans + 1, game.State.BannedDisasters.Count);
                Assert.Contains(game.State.Events, item => item.Type == "disaster-banned"
                    && item.Cards.Any(card => card.InstanceId == choice));
            }
        }

        Assert.True(sawRandomReveal);
        Assert.Equal(L12Phase.Mulligan, game.State.Phase);
        Assert.Equal(3, game.State.BannedDisasters.Count);
        Assert.Single(game.State.RevealedDisasters);
        Assert.Equal(2, game.State.ChosenDisasters.Count);
        var spectator = game.SnapshotForSpectator();
        Assert.Empty(spectator.Prompts);
        Assert.Equal(3, spectator.BannedDisasters.Length);
        Assert.Single(spectator.RevealedDisasters);
        Assert.Equal(2, spectator.ChosenDisasters.Length);
        Assert.Equal(4, spectator.SessionDisasters.Length);
        var spectatorJson = JsonSerializer.Serialize(spectator);
        Assert.Contains("\"hidden\":true", spectatorJson);
        Assert.DoesNotContain(spectator.ChosenDisasters, item => item is L12CardInstance);
        Assert.IsType<L12CardInstance>(spectator.SessionDisasters[0]);
        Assert.All(spectator.SessionDisasters.Skip(1).Take(2), item => Assert.IsNotType<L12CardInstance>(item));
        Assert.Equal("S01-DS10", Assert.IsType<L12CardInstance>(spectator.SessionDisasters[3]).CardId);
        Assert.Equal(game.State.RevealedDisasters[0].InstanceId,
            Assert.IsType<L12CardInstance>(spectator.SessionDisasters[0]).InstanceId);

        for (var viewer = 0; viewer < 2; viewer++)
        {
            var playerSnapshot = game.SnapshotFor(viewer);
            var ownChoice = game.State.ChosenDisasters.Single(card =>
                game.State.ChosenDisasterOwners[card.InstanceId] == viewer);
            var opponentChoice = game.State.ChosenDisasters.Single(card =>
                game.State.ChosenDisasterOwners[card.InstanceId] != viewer);
            Assert.Contains(playerSnapshot.ChosenDisasters, item => item is L12CardInstance card
                && card.InstanceId == ownChoice.InstanceId);
            Assert.DoesNotContain(playerSnapshot.ChosenDisasters, item => item is L12CardInstance card
                && card.InstanceId == opponentChoice.InstanceId);
            var privateEvent = game.State.Events.Single(item => item.Type == "disaster-selected"
                && item.Cards.Any(card => card.InstanceId == opponentChoice.InstanceId));
            var filteredEvent = playerSnapshot.RecentEvents.Single(item => item.Sequence == privateEvent.Sequence);
            Assert.Empty(filteredEvent.Cards);
            Assert.DoesNotContain(opponentChoice.Name, filteredEvent.Text);
            Assert.All(playerSnapshot.SessionDisasters.Take(2), item => Assert.IsType<L12CardInstance>(item));
            Assert.IsNotType<L12CardInstance>(playerSnapshot.SessionDisasters[2]);
            Assert.Equal("S01-DS10", Assert.IsType<L12CardInstance>(playerSnapshot.SessionDisasters[3]).CardId);
            var visibleSessionIds = playerSnapshot.SessionDisasters.Take(2)
                .Cast<L12CardInstance>().Select(card => card.InstanceId).ToArray();
            Assert.Equal(game.State.RevealedDisasters[0].InstanceId, visibleSessionIds[0]);
            Assert.Equal(ownChoice.InstanceId, visibleSessionIds[1]);
            Assert.DoesNotContain(opponentChoice.InstanceId, visibleSessionIds);
        }

        var referee = game.SnapshotForReferee();
        Assert.All(game.State.ChosenDisasters, card => Assert.Contains(referee.ChosenDisasters,
            item => item is L12CardInstance visible && visible.InstanceId == card.InstanceId));
        Assert.All(referee.SessionDisasters, item => Assert.IsType<L12CardInstance>(item));
        Assert.Equal(4, game.State.DisasterDeck.Count);
        Assert.Equal("S01-DS10", game.State.DisasterDeck[^1].CardId);
        Assert.All(game.State.Players, player => Assert.Equal(6, player.Hand.Count));
    }

    [Fact]
    public void YangJianDrawCycleReturnsTheSelectedHandCardInTheSamePrompt()
    {
        var game = Create(seed: 5519);
        var owner = Enumerable.Range(0, game.State.Players.Length)
            .Single(index => game.State.Players[index].MasterId == "S01-01M1");
        var player = game.State.Players[owner];
        AddAllMorale(player);
        game.State.ActivePlayer = owner;
        game.State.Phase = L12Phase.Main;
        var libraryBefore = player.Library.Count;

        Assert.True(game.Handle(owner, new L12Command("activateAbility", $"master-{owner}", Ability: "drawCycle")).Accepted);
        while (game.State.PendingPrompts.Count > 0 && game.State.PendingPrompts[0].Kind == "response")
        {
            var response = game.State.PendingPrompts[0];
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        }

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("single-top-bottom", prompt.Data["placementMode"]);
        Assert.Equal(player.Hand.Count, prompt.ValidChoices.Count);
        var returned = prompt.ValidChoices[0];
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            CardInstanceIds: [returned], Destination: "bottom")).Accepted);

        Assert.Empty(game.State.PendingPrompts);
        Assert.Equal(libraryBefore, player.Library.Count);
        Assert.Equal(returned, player.Library[^1].InstanceId);
        Assert.Contains(game.State.Events, item => item.Type == "return" && item.Cards.Any(card => card.InstanceId == returned));
    }

    [Fact]
    public void ObservingStarsOrdersAllFiveCardsToOneDestinationInOnePrompt()
    {
        var game = Create(seed: 5520);
        var owner = Enumerable.Range(0, game.State.Players.Length)
            .Single(index => game.State.Players[index].Library.Concat(game.State.Players[index].Hand)
                .Any(card => card.CardId == "S01-0119"));
        var player = game.State.Players[owner];
        var observingStars = TakeCard(game, owner, "S01-0119");
        player.Hand.Add(observingStars);
        AddAllMorale(player);
        game.State.ActivePlayer = owner;
        game.State.Phase = L12Phase.Main;
        var originalTop = player.Library.Take(5).Select(card => card.InstanceId).ToArray();

        Assert.True(game.Handle(owner, new L12Command("playCard", observingStars.InstanceId)).Accepted);
        while (game.State.PendingPrompts.Count > 0 && game.State.PendingPrompts[0].Kind == "response")
        {
            var response = game.State.PendingPrompts[0];
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        }

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("all-top-bottom", prompt.Data["placementMode"]);
        Assert.Equal(originalTop, prompt.ValidChoices);
        var bottom = originalTop.Reverse().ToList();
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            TopCardInstanceIds: [], BottomCardInstanceIds: bottom)).Accepted);

        var moralePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(owner,
            new L12Command("resolvePrompt", PromptId: moralePrompt.PromptId, Choice: "no")).Accepted);
        Assert.Equal(bottom, player.Library.TakeLast(bottom.Count).Select(card => card.InstanceId));
        Assert.Contains(observingStars, player.Graveyard);
    }

    [Fact]
    public void OiranGiftLetsThePlayerOrderEveryUnchosenCardBeforeReturningThemToBottom()
    {
        var game = Create(seed: 5521);
        var owner = Enumerable.Range(0, game.State.Players.Length)
            .Single(index => game.State.Players[index].Library.Concat(game.State.Players[index].Hand)
                .Any(card => card.CardId == "S01-0419"));
        var player = game.State.Players[owner];
        var gift = TakeCard(game, owner, "S01-0419");
        player.Hand.Add(gift);
        AddAllMorale(player);
        game.State.ActivePlayer = owner;
        game.State.Phase = L12Phase.Main;

        var revealed = player.Library.Take(3).ToArray();
        var eligible = revealed.FirstOrDefault(card => card.CardId != "S01-0419" && card.Faction == "gaotianyuan");
        if (eligible is null)
        {
            eligible = player.Library.Skip(3).First(card => card.CardId != "S01-0419" && card.Faction == "gaotianyuan");
            player.Library.Remove(eligible);
            player.Library.Insert(0, eligible);
            revealed = player.Library.Take(3).ToArray();
        }

        Assert.True(game.Handle(owner, new L12Command("playCard", gift.InstanceId)).Accepted);
        while (game.State.PendingPrompts.Count > 0 && game.State.PendingPrompts[0].Kind == "response")
        {
            var response = game.State.PendingPrompts[0];
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        }

        var pickPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("optional-add", pickPrompt.Data["choiceMode"]);
        Assert.Contains(eligible.InstanceId, pickPrompt.ValidChoices);
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: pickPrompt.PromptId,
            Choice: eligible.InstanceId)).Accepted);

        var orderPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("all-bottom", orderPrompt.Data["placementMode"]);
        var bottomOrder = orderPrompt.ValidChoices.AsEnumerable().Reverse().ToList();
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: orderPrompt.PromptId,
            TopCardInstanceIds: [], BottomCardInstanceIds: bottomOrder)).Accepted);

        var moralePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(owner,
            new L12Command("resolvePrompt", PromptId: moralePrompt.PromptId, Choice: "no")).Accepted);
        Assert.Equal(bottomOrder, player.Library.TakeLast(bottomOrder.Count).Select(card => card.InstanceId));
        Assert.Contains(eligible, player.Hand);
        Assert.Contains(gift, player.Graveyard);
    }

    [Fact]
    public void WildCampLetsThePlayerOrderEveryUnchosenCardBeforeReturningThemToBottom()
    {
        var game = Create(seed: 5528);
        const int owner = 0;
        var player = game.State.Players[owner];
        var camp = CreateInstance("S01-0007", "wild-camp-order");
        player.Hand.Add(camp);
        AddAllMorale(player);
        game.State.ActivePlayer = owner;
        game.State.Phase = L12Phase.Main;
        var revealed = player.Library.Take(3).Select(card => card.InstanceId).ToArray();

        Assert.True(game.Handle(owner, new L12Command("playCard", camp.InstanceId)).Accepted);
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var response = game.State.PendingPrompts[0];
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        }

        var pick = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(owner,
            new L12Command("resolvePrompt", PromptId: pick.PromptId, Choice: "skip")).Accepted);
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("all-bottom", order.Data["placementMode"]);
        Assert.Equal(revealed, order.ValidChoices);
        var bottom = revealed.Reverse().ToList();
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: order.PromptId,
            TopCardInstanceIds: [], BottomCardInstanceIds: bottom)).Accepted);
        var option = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(owner,
            new L12Command("resolvePrompt", PromptId: option.PromptId, Choice: "skip")).Accepted);

        Assert.Equal(bottom, player.Library.TakeLast(bottom.Count).Select(card => card.InstanceId));
    }

    [Fact]
    public void ApocalypseLetsBothPlayersPrivatelyOrderTheirWholeHandsBeforeDrawingFour()
    {
        var game = Create(seed: 5529);
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(CreateInstance("S01-DS07", "fixed-apocalypse"));
        game.State.DisasterValue = 9;
        var originalHands = game.State.Players
            .Select(player => player.Hand.Select(card => card.InstanceId).ToArray()).ToArray();

        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        foreach (var prompt in game.State.PendingPrompts.Where(prompt => prompt.Kind == "disaster-trigger").ToArray())
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId)).Accepted);

        var orders = game.State.PendingPrompts
            .Where(prompt => prompt.Data.GetValueOrDefault("action") == "disaster-apocalypse-hand-order")
            .OrderBy(prompt => prompt.PlayerIndex).ToArray();
        Assert.Equal(2, orders.Length);
        Assert.All(orders, prompt =>
        {
            Assert.True(prompt.IsPrivate);
            Assert.Equal("all-bottom", prompt.Data["placementMode"]);
        });
        var expectedBottom = new List<string>[2];
        foreach (var prompt in orders)
        {
            expectedBottom[prompt.PlayerIndex] = prompt.ValidChoices.AsEnumerable().Reverse().ToList();
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                TopCardInstanceIds: [], BottomCardInstanceIds: expectedBottom[prompt.PlayerIndex])).Accepted);
        }

        for (var index = 0; index < 2; index++)
        {
            Assert.Equal(expectedBottom[index], game.State.Players[index].Library
                .TakeLast(originalHands[index].Length).Select(card => card.InstanceId));
            Assert.Equal(4, game.State.Players[index].Hand.Count);
        }
    }

    [Fact]
    public void NegatingAnEnterEffectDoesNotUndoTheLegionEntryOrItsDisasterValue()
    {
        var game = Create(seed: 5522);
        var owner = Enumerable.Range(0, game.State.Players.Length)
            .Single(index => game.State.Players[index].Library.Concat(game.State.Players[index].Hand)
                .Any(card => card.CardId == "S01-0105"));
        var opponent = 1 - owner;
        var legion = TakeCard(game, owner, "S01-0105");
        var negate = TakeCard(game, owner, "S01-0018");
        var unusedNegate = TakeCard(game, owner, "S01-0018");
        var player = game.State.Players[owner];
        player.Hand.Add(legion);
        AddAllMorale(player);
        negate.Hidden = true;
        negate.SetRound = 0;
        unusedNegate.Hidden = true;
        unusedNegate.SetRound = 0;
        game.State.Players[opponent].Field[1][0] = negate;
        game.State.Players[opponent].Field[1][1] = unusedNegate;
        game.State.ActivePlayer = owner;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;
        var disasterBefore = game.State.DisasterValue;

        Assert.True(game.Handle(owner, new L12Command("playCard", legion.InstanceId, Row: 0, Slot: 0)).Accepted);
        Assert.Same(legion, player.Field[0][0]);
        Assert.Equal(disasterBefore + legion.DisasterLevel, game.State.DisasterValue);

        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(opponent, response.PlayerIndex);
        Assert.Contains(negate.InstanceId, response.ValidChoices);
        Assert.Equal("instant", response.Data["choiceMode"]);
        Assert.True(game.Handle(opponent, new L12Command("resolvePrompt", PromptId: response.PromptId,
            Choice: negate.InstanceId)).Accepted);

        Assert.Same(legion, player.Field[0][0]);
        Assert.Equal(disasterBefore + legion.DisasterLevel, game.State.DisasterValue);
        Assert.Contains(game.State.Events, item => item.Type == "effect-negated");
        Assert.Empty(game.State.PendingPrompts);
        Assert.Same(unusedNegate, game.State.Players[opponent].Field[1][1]);
    }

    [Fact]
    public void TriggeredDisasterIsShownToBothPlayersAndEachConfirmationDismissesIndependently()
    {
        var game = Create(preparation: true, seed: 5523);
        var initiative = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(initiative.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: initiative.PromptId, Choice: "first")).Accepted);
        while (game.State.Phase == L12Phase.DisasterPreparation)
        {
            foreach (var prompt in game.State.PendingPrompts.ToArray())
            {
                var choice = prompt.ValidChoices.FirstOrDefault();
                Assert.True(game.Handle(prompt.PlayerIndex,
                    new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
            }
        }

        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        game.State.DisasterValue = 9;
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        var triggers = game.State.PendingPrompts.Where(prompt => prompt.Kind == "disaster-trigger").ToArray();
        Assert.Equal(2, triggers.Length);
        var preview = triggers[0].Data["previewCardId"];
        Assert.All(triggers, prompt =>
        {
            Assert.Equal(preview, prompt.Data["previewCardId"]);
            Assert.True(prompt.Data.ContainsKey($"{preview}:image"));
            Assert.True(prompt.Data.ContainsKey($"{preview}:effect"));
        });

        Assert.True(game.Handle(triggers[0].PlayerIndex,
            new L12Command("resolvePrompt", PromptId: triggers[0].PromptId)).Accepted);
        Assert.Empty(game.SnapshotFor(triggers[0].PlayerIndex).Prompts);
        Assert.NotNull(game.SnapshotFor(triggers[0].PlayerIndex).WaitingPrompt);
        Assert.Contains(game.State.PendingPrompts, prompt => prompt.PromptId == triggers[1].PromptId);
        Assert.True(game.Handle(triggers[1].PlayerIndex,
            new L12Command("resolvePrompt", PromptId: triggers[1].PromptId)).Accepted);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "stack-response");
    }

    [Fact]
    public void DisasterRemovalDoesNotQueueDeathTriggersAndCannotBeRespondedTo()
    {
        var game = Create(seed: 8860);
        var deathLegion = CreateInstance("S01-0102", "disaster-death-legion");
        game.State.Players[0].Field[0][0] = deathLegion;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(CreateInstance("S01-DS09", "fixed-ragnarok"));
        game.State.DisasterValue = 9;

        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        foreach (var prompt in game.State.PendingPrompts.Where(prompt => prompt.Continuation == "disaster-trigger-confirm").ToArray())
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId)).Accepted);

        Assert.Contains(deathLegion, game.State.Players[0].Graveyard);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "stack-response");
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == deathLegion.InstanceId);
    }

    [Fact]
    public void FinalDisasterLocksDisasterValueAtZeroAcrossTurnEnd()
    {
        var game = Create(seed: 8864);
        game.State.ActiveDisaster = CreateInstance("S01-DS10", "final-disaster");
        game.State.DisasterDeck.Clear();
        game.State.DisasterValue = 7;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("endTurn")).Accepted);
        Assert.Equal(0, game.State.DisasterValue);
    }

    [Fact]
    public void HiddenLegionUsesCardBackForOpponentSnapshot()
    {
        var game = Create(seed: 8865);
        var hidden = CreateInstance("S01-0415", "hidden-hanzo");
        hidden.Hidden = true;
        game.State.Players[0].Field[0][0] = hidden;

        var json = JsonSerializer.Serialize(game.SnapshotFor(1));
        Assert.Contains("hidden-card", json);
        Assert.DoesNotContain("hidden-hanzo\",\"cardId\":\"S01-0415", json);
    }

    [Fact]
    public void HattoriHanzoCanRevealFromHiddenStateDuringOwnersMainPhase()
    {
        var game = Create(seed: 8866);
        var owner = Enumerable.Range(0, game.State.Players.Length)
            .Single(index => game.State.Players[index].Library.Concat(game.State.Players[index].Hand)
                .Any(card => card.CardId == "S01-0415"));
        var player = game.State.Players[owner];
        var hanzo = TakeCard(game, owner, "S01-0415");
        hanzo.Hidden = true;
        hanzo.Tapped = false;
        player.Field[0][0] = hanzo;
        game.State.ActivePlayer = owner;
        game.State.Phase = L12Phase.Main;

        var ownSnapshot = JsonSerializer.Serialize(game.SnapshotFor(owner));
        var opponentSnapshot = JsonSerializer.Serialize(game.SnapshotFor(1 - owner));
        Assert.Contains("S01-0415", ownSnapshot);
        Assert.Contains("revealHidden", ownSnapshot);
        Assert.Contains("hidden-card", opponentSnapshot);

        Assert.True(game.Handle(owner,
            new L12Command("activateAbility", hanzo.InstanceId, Ability: "revealHidden")).Accepted);
        while (game.State.PendingPrompts.FirstOrDefault(prompt => prompt.Continuation == "stack-response") is { } response)
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);

        Assert.False(hanzo.Hidden);
        Assert.Contains(game.State.Events,
            item => item.Text.Contains("主动翻回正面", StringComparison.Ordinal));
    }

    [Fact]
    public void ShanheShejituReusesObservingStarsAllTopBottomOrdering()
    {
        var game = Create(seed: 8867);
        var owner = Enumerable.Range(0, game.State.Players.Length)
            .Single(index => game.State.Players[index].Library.Concat(game.State.Players[index].Hand)
                .Any(card => card.CardId == "S01-0117"));
        var player = game.State.Players[owner];
        var shanhe = TakeCard(game, owner, "S01-0117");
        shanhe.Tapped = false;
        player.Relic = shanhe;
        game.State.ActivePlayer = owner;
        game.State.Phase = L12Phase.Main;

        var eligible = player.Library.First(card => card.Faction == "tianting");
        player.Library.Remove(eligible);
        player.Library.Insert(0, eligible);
        var discard = player.Hand.First();

        Assert.True(game.Handle(owner,
            new L12Command("activateAbility", shanhe.InstanceId, Ability: "artifactSearch")).Accepted);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: costPrompt.PromptId,
            Choice: discard.InstanceId)).Accepted);
        while (game.State.PendingPrompts.FirstOrDefault(prompt => prompt.Continuation == "stack-response") is { } response)
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);

        var searchPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(eligible.InstanceId, searchPrompt.ValidChoices);
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: searchPrompt.PromptId,
            Choice: eligible.InstanceId)).Accepted);

        var orderPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("all-top-bottom", orderPrompt.Data["placementMode"]);
        Assert.Equal(2, orderPrompt.ValidChoices.Count);
        var bottom = orderPrompt.ValidChoices.AsEnumerable().Reverse().ToList();
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: orderPrompt.PromptId,
            TopCardInstanceIds: [], BottomCardInstanceIds: bottom)).Accepted);
        Assert.Equal(bottom, player.Library.TakeLast(2).Select(card => card.InstanceId));
    }

    [Fact]
    public void TriggeredEffectStillResolvesAfterItsSourceLeavesTheField()
    {
        var game = Create(seed: 5524);
        const int owner = 0;
        const int opponent = 1;
        var legion = TakeCard(game, owner, "S01-0105");
        var guanYu = TakeCard(game, owner, "S01-0106");
        var responseCard = TakeCard(game, owner, "S01-0018");
        var player = game.State.Players[owner];
        player.Hand.Add(legion);
        player.Hand.Add(guanYu);
        AddAllMorale(player);
        responseCard.Hidden = true;
        responseCard.SetRound = 0;
        game.State.Players[opponent].Field[1][0] = responseCard;
        game.State.ActivePlayer = owner;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(owner, new L12Command("playCard", legion.InstanceId, Row: 0, Slot: 0)).Accepted);
        var response = Assert.Single(game.State.PendingPrompts);
        player.Field[0][0] = null;
        player.Graveyard.Add(legion);
        Assert.True(game.Handle(opponent,
            new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);

        Assert.Contains(legion, player.Graveyard);
        Assert.Contains(game.State.PendingPrompts, prompt => prompt.Data.GetValueOrDefault("action") == "liubei-card");
    }

    [Fact]
    public void AutomaticTurnStartRecordsEveryPhaseAndItsAction()
    {
        var game = Create(seed: 6642);
        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);

        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Contains(game.State.Events, item => item.Type == "phase" && item.Text == "执行触发天灾");
        Assert.Contains(game.State.Events, item => item.Type == "phase-detail" && item.Text.Contains("未达到触发条件"));
        Assert.Contains(game.State.Events, item => item.Type == "phase" && item.Text == "执行重置阶段");
        Assert.Contains(game.State.Events, item => item.Type == "phase-detail" && item.Text.Contains("转为活跃"));
        Assert.Contains(game.State.Events, item => item.Type == "phase" && item.Text == "执行抽牌阶段");
        Assert.Contains(game.State.Events, item => (item.Type is "phase-detail" or "draw-skipped")
            && (item.Text.Contains("抽取 1 张牌") || item.Text.Contains("首回合不抽牌")));
        Assert.Contains(game.State.Events, item => item.Type == "phase" && item.Text == "执行士气阶段");
        Assert.Contains(game.State.Events, item => item.Type == "phase-detail" && item.Text.Contains("追加"));
        Assert.Contains(game.State.Events, item => item.Type == "phase" && item.Text == "进入主要阶段");
    }

    [Fact]
    public void BackRowRangedLegionCanAttackFrontButNotMasterAndTakesNoRangedLoss()
    {
        var game = Create();
        var attackerPlayer = game.State.ActivePlayer;
        var defenderPlayer = 1 - attackerPlayer;
        var attacker = TakeCard(game, attackerPlayer, attackerPlayer == 1 ? "S01-0410" : "S01-0109");
        if (attacker.CardId != "S01-0410")
        {
            attackerPlayer = 1;
            defenderPlayer = 0;
            game.State.ActivePlayer = 1;
            attacker = TakeCard(game, 1, "S01-0410");
        }
        var target = game.State.Players[defenderPlayer].Hand.First(card => card.CardType == "legion");
        game.State.Players[defenderPlayer].Hand.Remove(target);
        attacker.SummonRound = 0;
        attacker.Troops = 2000;
        target.Troops = 1000;
        game.State.Players[attackerPlayer].Field[1][0] = attacker;
        game.State.Players[defenderPlayer].Field[0][0] = target;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.False(game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        Assert.True(game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        foreach (var prompt in game.State.PendingPrompts.ToArray())
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        Assert.Equal(2000, attacker.Troops);
        Assert.Contains(target, game.State.Players[defenderPlayer].Graveyard);
    }

    [Fact]
    public void FrontRangedCanAttackBackButDefaultMeleeCannot()
    {
        var game = Create();
        game.State.ActivePlayer = 1;
        game.State.Phase = L12Phase.Main;
        game.State.Round = 2;
        var ranged = TakeCard(game, 1, "S01-0410");
        var backTarget = game.State.Players[0].Hand.First(card => card.CardType == "legion");
        game.State.Players[0].Hand.Remove(backTarget);
        ranged.SummonRound = 0;
        game.State.Players[1].Field[0][0] = ranged;
        game.State.Players[0].Field[1][0] = backTarget;
        Assert.True(game.Handle(1, new L12Command("attack", ranged.InstanceId,
            Target: new L12AttackTarget("legion", backTarget.InstanceId))).Accepted);

        var meleeGame = Create(seed: 992);
        var active = meleeGame.State.ActivePlayer;
        var other = 1 - active;
        var melee = meleeGame.State.Players[active].Hand.First(card => card.CardType == "legion" && !card.HasRangeBonus);
        var target = meleeGame.State.Players[other].Hand.First(card => card.CardType == "legion");
        meleeGame.State.Players[active].Hand.Remove(melee);
        meleeGame.State.Players[other].Hand.Remove(target);
        melee.SummonRound = 0;
        meleeGame.State.Players[active].Field[0][0] = melee;
        meleeGame.State.Players[other].Field[1][0] = target;
        meleeGame.State.Round = 2;
        meleeGame.State.Phase = L12Phase.Main;
        Assert.False(meleeGame.Handle(active, new L12Command("attack", melee.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
    }

    [Fact]
    public void AbsoluteDefenseRespondsOnStackAndKeepsPaidCostsWhenNegatingAttack()
    {
        var game = Create(seed: 1512);
        var attackerPlayer = game.State.ActivePlayer;
        var defenderPlayer = 1 - attackerPlayer;
        var attackerOwner = game.State.Players[attackerPlayer];
        var defender = game.State.Players[defenderPlayer];
        var attacker = attackerOwner.Hand.First(card => card.CardType == "legion");
        attackerOwner.Hand.Remove(attacker);
        attackerOwner.Hand.Clear();
        attacker.SummonRound = 0;
        attackerOwner.Field[0][0] = attacker;
        var response = defender.Hand.Concat(defender.Library).First(card => card.CardId == "S01-0016");
        defender.Hand.Remove(response); defender.Library.Remove(response); defender.Hand.Add(response);
        var discard = defender.Hand.First(card => card.InstanceId != response.InstanceId);
        AddAllMorale(defender);
        game.State.ActivePlayer = defenderPlayer;
        game.State.Phase = L12Phase.Main;
        Assert.True(game.Handle(defenderPlayer, new L12Command("playCard", response.InstanceId, Row: 1, Slot: 0)).Accepted);
        game.State.ActivePlayer = attackerPlayer;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(attackerPlayer, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        var responsePrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(response.InstanceId, responsePrompt.ValidChoices);
        Assert.True(game.Handle(defenderPlayer, new L12Command("resolvePrompt", PromptId: responsePrompt.PromptId,
            Choice: response.InstanceId)).Accepted);
        var discardPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(defenderPlayer, new L12Command("resolvePrompt", PromptId: discardPrompt.PromptId,
            Choice: discard.InstanceId)).Accepted);

        Assert.Null(game.State.PendingDefense);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Contains(response, defender.Graveyard);
        Assert.Contains(discard, defender.Graveyard);
        Assert.Equal(2, defender.Morale.Count(card => card.Tapped));
        Assert.Contains(game.State.Events, item => item.Type == "effect-negated");
    }

    [Fact]
    public void KusanagiOnFrontRowIsTreatedAsALegionForMovement()
    {
        var game = Create(seed: 7788);
        var owner = 1;
        var player = game.State.Players[owner];
        var sword = TakeCard(game, owner, "S01-0417");
        sword.SummonRound = 0;
        sword.Troops = 5000;
        player.Field[0][0] = sword;
        AddAllMorale(player);
        game.State.ActivePlayer = owner;
        game.State.Phase = L12Phase.Main;
        game.State.Round = 2;

        Assert.True(game.Handle(owner, new L12Command("move", sword.InstanceId, Row: 0, Slot: 1)).Accepted);
        Assert.Null(player.Field[0][0]);
        Assert.Same(sword, player.Field[0][1]);
    }

    [Fact]
    public void RangedLegionsAreIdentifiedByTheCanonicalEffectSentence()
    {
        var ranged = Catalog.Cards.Values
            .Where(card => card.Effect?.Contains("进攻距离+1，远程进攻无损。", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(ranged);
        foreach (var definition in ranged)
        {
            var instance = new L12CardInstance
            {
                InstanceId = $"test-{definition.Id}",
                CardId = definition.Id,
                Name = definition.NameZh,
                CardType = definition.CardType,
                Faction = definition.Faction,
                EffectText = definition.Effect,
            };
            Assert.True(instance.HasRangeBonus);
            Assert.True(instance.HasRangedNoLoss);
        }
    }

    [Fact]
    public void TiantingFactionEffectsUseTheMoraleCardRules()
    {
        var game = Create(seed: 8861);
        var player = game.State.Players[0];
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        foreach (var morale in player.MoraleDeck.Take(2).ToArray())
        {
            player.MoraleDeck.Remove(morale);
            player.Morale.Add(morale);
        }

        Assert.True(game.Handle(0, new L12Command("activateAbility", "faction-0", Ability: "factionAddActive")).Accepted);
        foreach (var prompt in game.State.PendingPrompts.ToArray())
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass")).Accepted);
        Assert.Equal(3, player.Morale.Count);
        Assert.Equal(2, player.Morale.Count(card => card.Tapped));
        Assert.Single(player.Morale, card => !card.Tapped);

        var recovery = Create(seed: 8862);
        recovery.State.ActivePlayer = 0;
        recovery.State.Phase = L12Phase.Main;
        var recoveryPlayer = recovery.State.Players[0];
        foreach (var morale in recoveryPlayer.MoraleDeck.Take(4).ToArray())
        {
            recoveryPlayer.MoraleDeck.Remove(morale);
            recoveryPlayer.Morale.Add(morale);
        }
        Assert.False(recovery.Handle(0, new L12Command("activateAbility", "faction-0", Ability: "factionZeroRecovery")).Accepted);
        Assert.True(recovery.Handle(0, new L12Command("activateAbility", "master-0", Ability: "nonLethal")).Accepted);
        while (recovery.State.PendingPrompts.FirstOrDefault(prompt => prompt.Continuation == "stack-response") is { } response)
            Assert.True(recovery.Handle(response.PlayerIndex, new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        var trigger = Assert.Single(recovery.State.PendingPrompts, prompt => prompt.Continuation == "faction-zero-recovery");
        Assert.True(recovery.Handle(0, new L12Command("resolvePrompt", PromptId: trigger.PromptId, Choice: "yes")).Accepted);
        Assert.Equal(2, recoveryPlayer.Morale.Count);
        Assert.All(recoveryPlayer.Morale, card => Assert.True(card.Tapped));
    }

    [Fact]
    public void GaotianyuanFactionEffectDrawsAndCanMoveOneActiveLegionOneSpace()
    {
        var game = Create(seed: 8863);
        var player = game.State.Players[1];
        game.State.ActivePlayer = 1;
        game.State.Phase = L12Phase.Main;
        foreach (var morale in player.MoraleDeck.Take(2).ToArray())
        {
            player.MoraleDeck.Remove(morale);
            player.Morale.Add(morale);
        }
        var legion = player.Hand.First(card => card.CardType == "legion");
        player.Hand.Remove(legion);
        legion.SummonRound = 0;
        player.Field[0][0] = legion;
        var handBefore = player.Hand.Count;

        Assert.True(game.Handle(1, new L12Command("activateAbility", "faction-1", Ability: "factionDrawMove")).Accepted);
        while (game.State.PendingPrompts.Count > 0)
        {
            var prompt = game.State.PendingPrompts[0];
            var choice = prompt.ValidChoices.Contains("pass") ? "pass"
                : prompt.ValidChoices.Contains(legion.InstanceId) ? legion.InstanceId
                : prompt.ValidChoices.Contains("0:1") ? "0:1"
                : prompt.ValidChoices[0];
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        }

        Assert.Equal(handBefore + 1, player.Hand.Count);
        Assert.Null(player.Field[0][0]);
        Assert.Same(legion, player.Field[0][1]);
    }
}
