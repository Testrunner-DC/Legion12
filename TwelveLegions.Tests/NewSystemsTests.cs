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
    public void CatalogLoadsS2AndStarterProductsAlongsideS1()
    {
        Assert.Equal(324, Catalog.Cards.Count);
        Assert.Equal("始皇帝 嬴政", Catalog.Cards["S02-0101"].NameZh);
        Assert.Equal("destruction", Catalog.Cards["S02-DS01"].CardType);
        Assert.Equal("otherworld", Catalog.Cards["S02-06M1"].Faction);
        Assert.Equal("伊丽莎白一世", Catalog.Cards["ST06-01"].NameZh);
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
    public void PrideMasterEffectStagesReturnAndPaymentWithoutDoubleCharging()
    {
        var game = Create(seed: 5534);
        var owner = Enumerable.Range(0, game.State.Players.Length)
            .Single(index => game.State.Players[index].MasterId == "S01-01M1");
        var player = game.State.Players[owner];
        AddAllMorale(player);
        foreach (var morale in player.Morale) morale.Tapped = true;
        var readyOrdinaryA = player.Morale[0];
        var readyOrdinaryB = player.Morale[1];
        var readyGodPower = player.Morale[2];
        readyOrdinaryA.Tapped = false;
        readyOrdinaryB.Tapped = false;
        readyGodPower.Tapped = false;
        readyGodPower.IsGodPower = true;
        var selectedReturn = player.Morale.Where(card => card.Tapped).Take(3)
            .Append(readyOrdinaryA).Select(card => card.InstanceId).ToArray();
        var originalCount = player.Morale.Count;
        game.State.ActivePlayer = owner;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = CreateInstance("S02-DS06", "pride-return-disaster");

        Assert.True(game.Handle(owner,
            new L12Command("activateAbility", $"master-{owner}", Ability: "nonLethal")).Accepted);
        var returnPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-return", returnPrompt.Kind);
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: returnPrompt.PromptId,
            CardInstanceIds: [.. selectedReturn])).Accepted);

        // 返还选择只是声明；复合费用全部确认前不得改变资源。
        Assert.Equal(originalCount, player.Morale.Count);
        var paymentPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("resource-payment", paymentPrompt.Kind);
        Assert.DoesNotContain(readyOrdinaryA.InstanceId, paymentPrompt.ValidChoices);
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: paymentPrompt.PromptId,
            CardInstanceIds: [readyGodPower.InstanceId])).Accepted);

        Assert.Equal(originalCount - 4, player.Morale.Count);
        Assert.DoesNotContain(player.Morale, morale => selectedReturn.Contains(morale.InstanceId));
        Assert.True(readyGodPower.Tapped);
        Assert.Equal(4, player.ReturnedMoraleThisTurn);
    }

    [Fact]
    public void PrideMasterEffectInsufficientPostReturnPaymentDoesNotMutateMorale()
    {
        var game = Create(seed: 5535);
        var owner = Enumerable.Range(0, game.State.Players.Length)
            .Single(index => game.State.Players[index].MasterId == "S01-01M1");
        var player = game.State.Players[owner];
        AddAllMorale(player);
        while (player.Morale.Count > 4)
        {
            player.MoraleDeck.Add(player.Morale[^1]);
            player.Morale.RemoveAt(player.Morale.Count - 1);
        }
        foreach (var morale in player.Morale) morale.Tapped = false;
        var ids = player.Morale.Select(card => card.InstanceId).ToArray();
        game.State.ActivePlayer = owner;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = CreateInstance("S02-DS06", "pride-insufficient-disaster");

        var result = game.Handle(owner,
            new L12Command("activateAbility", $"master-{owner}", Ability: "nonLethal"));

        Assert.False(result.Accepted);
        Assert.Equal(ids, player.Morale.Select(card => card.InstanceId));
        Assert.Equal(0, player.ReturnedMoraleThisTurn);
    }

    [Fact]
    public void InvalidTriggerDeclarationRemovesItsCandidateAndDoesNotBlockLaterBatches()
    {
        var game = Create(seed: 5536);
        var source = CreateInstance("S01-0108", "invalid-trigger-source");
        var candidate = new L12TriggerCandidate
        {
            CandidateId = "invalid-trigger-candidate", Controller = 0,
            SourceInstanceId = source.InstanceId, SourceCardId = source.CardId,
            SourceName = source.Name, Trigger = "death", Text = "阵亡时效果",
        };
        var activation = new L12PendingActivation
        {
            ActivationId = "invalid-trigger-activation", Controller = 0,
            SourceInstanceId = source.InstanceId, SourceCardId = source.CardId,
            Ability = "trigger-declaration", Text = "选择目标", ValidChoices = ["still-legal"],
            SelectionSteps =
            [
                new L12ActivationSelectionStep
                {
                    Kind = "field-legion", Text = "选择目标", ValidChoices = ["already-invalid"],
                },
            ],
            TriggerCandidateId = candidate.CandidateId,
        };
        game.State.PendingTriggerStackCandidates.Add(candidate);
        game.State.PendingActivations.Add(activation);
        game.State.PendingPrompts.Add(new L12Prompt
        {
            PromptId = "invalid-trigger-prompt", PlayerIndex = 0, Kind = "field-legion", Text = "选择目标",
            ValidChoices = ["still-legal"], MinChoose = 1, MaxChoose = 1, IsPrivate = true,
            Continuation = "pending-activation",
            Data = new Dictionary<string, string> { ["activationId"] = activation.ActivationId },
        });

        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: "invalid-trigger-prompt",
            Choice: "still-legal")).Accepted);

        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Contains(game.State.Events, entry => entry.Type == "ability-rejected");
    }

    [Fact]
    public void LegalAttackPreviewAndFormalAttackShareHannibalRestriction()
    {
        var game = Create(seed: 5537);
        var attacker = CreateInstance("S02-0003", "preview-attacker");
        var hannibal = CreateInstance("S02-0516", "preview-hannibal");
        attacker.SummonRound = -1;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = hannibal;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        var legal = game.SnapshotFor(0).LegalAttackTargets[attacker.InstanceId];
        Assert.DoesNotContain(hannibal.InstanceId, legal);
        var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", hannibal.InstanceId)));
        Assert.False(result.Accepted);
        Assert.Contains("汉尼拔", result.Error);
    }

    [Fact]
    public void LegalAttackPreviewAndFormalAttackShareDisasterRangeRestriction()
    {
        var game = Create(seed: 5538);
        var attacker = CreateInstance("S02-0003", "preview-ranged-attacker");
        var target = CreateInstance("S02-0003", "preview-ranged-target");
        attacker.SummonRound = -1;
        game.State.Players[0].Field[1][0] = attacker;
        game.State.Players[1].Field[0][0] = target;
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = CreateInstance("S02-DS04", "preview-storm");

        var legal = game.SnapshotFor(0).LegalAttackTargets.GetValueOrDefault(attacker.InstanceId) ?? [];
        Assert.DoesNotContain(target.InstanceId, legal);
        var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId)));
        Assert.False(result.Accepted);
        Assert.Contains("风暴乱象", result.Error);
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
                    Assert.All(revealPrompts, reveal =>
                        Assert.Equal("information-card", reveal.Data["previewPresentation"]));
                for (var revealIndex = 0; revealIndex < revealPrompts.Length; revealIndex++)
                {
                    var reveal = revealPrompts[revealIndex];
                    Assert.Equal(sharedPreview, reveal.Data["previewCardId"]);
                    Assert.True(reveal.Data.ContainsKey($"{sharedPreview}:image"));
                    Assert.True(game.Handle(reveal.PlayerIndex,
                        new L12Command("resolvePrompt", PromptId: reveal.PromptId)).Accepted);
                    if (revealIndex == 0)
                    {
                        var confirmedSnapshot = game.SnapshotFor(reveal.PlayerIndex);
                        Assert.Empty(confirmedSnapshot.Prompts);
                        Assert.NotNull(confirmedSnapshot.WaitingPrompt);
                        var waitingJson = JsonSerializer.Serialize(confirmedSnapshot.WaitingPrompt);
                        Assert.Contains("disaster-reveal", waitingJson);
                        Assert.Contains((1 - reveal.PlayerIndex).ToString(), waitingJson);
                        Assert.Contains(game.State.PendingPrompts,
                            prompt => prompt.PromptId == revealPrompts[1].PromptId);

                        // A duplicated/stale acknowledgement must be rejected without consuming the
                        // opponent's independent public-information confirmation.
                        var stale = game.Handle(reveal.PlayerIndex,
                            new L12Command("resolvePrompt", PromptId: reveal.PromptId));
                        Assert.False(stale.Accepted);
                        Assert.Contains(game.State.PendingPrompts,
                            prompt => prompt.PromptId == revealPrompts[1].PromptId);
                    }
                }
                Assert.DoesNotContain(game.State.PendingPrompts,
                    prompt => prompt.Continuation == "setup-public-confirm");
                Assert.Equal(4, game.State.DisasterPreparationStep);
                Assert.Single(game.State.PendingPrompts,
                    prompt => prompt.Continuation == "setup-first-pick");
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
                Assert.Equal("information-card", prompt.Data["previewPresentation"]);
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
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
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
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Continuation == "pending-activation");
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

        Assert.Equal(bottomOrder, player.Library.TakeLast(bottomOrder.Count).Select(card => card.InstanceId));
        Assert.Contains(eligible, player.Hand);
        Assert.Contains(game.State.Events, entry => entry.Type == "search"
            && entry.Text == $"花魁的馈赠将〈{eligible.Name}〉加入手牌"
            && entry.Cards.Single().InstanceId == eligible.InstanceId);
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
        var followupDeclaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", followupDeclaration.Continuation);
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: followupDeclaration.PromptId,
            Choice: "mode:none")).Accepted);
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
        var guanYu = TakeCard(game, owner, "S01-0106");
        var negate = TakeCard(game, owner, "S01-0018");
        var unusedNegate = TakeCard(game, owner, "S01-0018");
        var player = game.State.Players[owner];
        player.Hand.Add(legion);
        player.Hand.Add(guanYu);
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

        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", mode.Continuation);
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: mode.PromptId,
            Choice: "mode:use")).Accepted);
        var morale = Assert.Single(game.State.PendingPrompts);
        var returnedMorale = morale.ValidChoices.First(choice => choice != "skip");
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: morale.PromptId,
            Choice: returnedMorale)).Accepted);
        var brother = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: brother.PromptId,
            Choice: guanYu.InstanceId)).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: slot.ValidChoices.First(choice => choice != "skip"))).Accepted);

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
        Assert.DoesNotContain(player.Morale, card => card.InstanceId == returnedMorale);
        Assert.Contains(guanYu, player.Hand);
        Assert.Same(unusedNegate, game.State.Players[opponent].Field[1][1]);
    }

    [Fact]
    public void ContinuousOnlyDisasterUsesPublicRevealAnimationWithoutConfirmation()
    {
        var game = Create(seed: 5523);
        game.State.Phase = L12Phase.Main;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(CreateInstance("S01-DS08", "continuous-disaster"));
        game.State.DisasterValue = 9;
        Assert.True(game.Handle(game.State.ActivePlayer, new L12Command("endTurn")).Accepted);

        var reveal = Assert.Single(game.State.Events, entry => entry.Type == "disaster-reveal");
        Assert.Contains(reveal.Cards, card => card.CardId == "S01-DS08");
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "disaster-trigger");
        Assert.Contains(game.State.Events, entry => entry.Type == "disaster-active" && entry.Cards.Any(card => card.CardId == "S01-DS08"));
    }

    [Fact]
    public void TriggeredDisasterStartsItsEffectWithoutExtraRevealConfirmation()
    {
        var game = Create(seed: 55231);
        game.State.Phase = L12Phase.Main;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(CreateInstance("S01-DS02", "triggered-disaster"));
        game.State.DisasterValue = 9;
        var hpBefore = game.State.Players.Select(player => player.Hp).ToArray();

        Assert.True(game.Handle(game.State.ActivePlayer, new L12Command("endTurn")).Accepted);

        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "disaster-trigger");
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "disaster-reveal");
        Assert.Equal(hpBefore[0] - 1, game.State.Players[0].Hp);
        Assert.Equal(hpBefore[1] - 1, game.State.Players[1].Hp);
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
        Assert.Equal(-1, game.State.ExtraTurnsForPlayer);
    }

    [Fact]
    public void RagnarokGmTriggerImmediatelyBeginsTheGrantedExtraTurn()
    {
        var game = Create(seed: 88601);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(CreateInstance("S01-DS09", "later-ragnarok"));
        var turnSerialBefore = game.State.TurnSerial;
        var inactiveHandBefore = game.State.Players[1].Hand.Count;

        Assert.True(game.HandleGm(new L12GmCommand("triggerDisaster")).Accepted);
        foreach (var confirmation in game.State.PendingPrompts
                     .Where(prompt => prompt.Continuation == "disaster-trigger-confirm").ToArray())
            Assert.True(game.Handle(confirmation.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: confirmation.PromptId)).Accepted);

        Assert.Equal(-1, game.State.ExtraTurnsForPlayer);
        Assert.Equal(0, game.State.ActivePlayer);
        Assert.Equal(turnSerialBefore + 1, game.State.TurnSerial);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Equal(inactiveHandBefore + 2, game.State.Players[1].Hand.Count);
    }

    [Fact]
    public void RagnarokTriggeredAtTurnStartSkipsTheInterruptedTurnsResetAndDraw()
    {
        var game = Create(seed: 88602);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(CreateInstance("S01-DS09", "turn-start-ragnarok"));
        game.State.DisasterValue = 9;
        var nextPlayerHandBefore = game.State.Players[1].Hand.Count;
        var opponentHandBefore = game.State.Players[0].Hand.Count;

        Assert.True(game.Handle(0, new L12Command("endTurn")).Accepted);

        Assert.Equal(1, game.State.ActivePlayer);
        Assert.Equal(4, game.State.Round);
        Assert.Equal(9, game.State.TurnSerial);
        Assert.Equal(L12Phase.Main, game.State.Phase);
        Assert.Equal(-1, game.State.ExtraTurnsForPlayer);
        Assert.Equal(nextPlayerHandBefore + 1, game.State.Players[1].Hand.Count);
        Assert.Equal(opponentHandBefore + 2, game.State.Players[0].Hand.Count);
        Assert.Single(game.State.Events, entry => entry.Type == "phase"
            && entry.PlayerIndex == 1 && entry.Text == "执行重置阶段");
        Assert.Single(game.State.Events, entry => entry.Type == "phase"
            && entry.PlayerIndex == 1 && entry.Text == "执行抽牌阶段");
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
    public void HattoriHanzoRevealedOnALaterTurnCanAttackWithoutNewSummonSickness()
    {
        var game = Create(seed: 88661);
        var owner = Enumerable.Range(0, game.State.Players.Length)
            .Single(index => game.State.Players[index].Library.Concat(game.State.Players[index].Hand)
                .Any(card => card.CardId == "S01-0415"));
        var player = game.State.Players[owner];
        var hanzo = TakeCard(game, owner, "S01-0415");
        hanzo.Hidden = true;
        hanzo.Tapped = false;
        hanzo.SummonRound = game.State.Round;
        player.Field[0][0] = hanzo;
        game.State.ActivePlayer = owner;
        game.State.Phase = L12Phase.Main;

        var enteredRound = hanzo.SummonRound;
        game.State.Round = enteredRound + 1;
        Assert.True(game.Handle(owner,
            new L12Command("activateAbility", hanzo.InstanceId, Ability: "revealHidden")).Accepted);
        while (game.State.PendingPrompts.FirstOrDefault(prompt => prompt.Continuation == "stack-response") is { } response)
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);

        Assert.False(hanzo.Hidden);
        Assert.Equal(enteredRound, hanzo.SummonRound);
        Assert.Contains(hanzo.InstanceId, game.SnapshotFor(owner).LegalAttackTargets.Keys);
        Assert.True(game.Handle(owner, new L12Command("attack", hanzo.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
    }

    [Fact]
    public void HiddenHattoriCannotSupportOrCreateATheoreticalCounterResponse()
    {
        var game = Create(seed: 88662);
        var attacker = CreateInstance("S01-0002", "hanzo-support-attacker");
        var target = CreateInstance("S01-0109", "hanzo-support-target");
        var hanzo = CreateInstance("S01-0415", "hanzo-hidden-support");
        attacker.SummonRound = target.SummonRound = hanzo.SummonRound = 0;
        attacker.Troops = 5000;
        target.Troops = 1000;
        hanzo.Hidden = true;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[1].Field[1][0] = hanzo;
        game.State.Players[0].Hand.Clear();
        game.State.Players[1].Hand.Clear();
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "stack-response");
        Assert.Contains(game.State.Events, entry => entry.Type == "support-skipped");
        Assert.Contains(target, game.State.Players[1].Graveyard);
        Assert.Same(hanzo, game.State.Players[1].Field[1][0]);
    }

    [Fact]
    public void TriggeredDisasterStillTreatsHiddenHattoriAsALegionAndPublishesToBothPlayers()
    {
        var game = Create(seed: 88663);
        var hanzo = CreateInstance("S01-0415", "hanzo-hidden-disaster");
        hanzo.Hidden = true;
        game.State.Players[0].Field[0][0] = hanzo;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;
        game.State.DisasterDeck.Clear();
        game.State.DisasterDeck.Add(CreateInstance("S02-DS04", "storm-animation"));

        Assert.True(game.HandleGm(new L12GmCommand("triggerDisaster")).Accepted);

        Assert.Null(game.State.Players[0].Field[0][0]);
        Assert.Same(hanzo, game.State.Players[0].Field[1][0]);
        var presentation = Assert.Single(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.Cards.Any(card => card.InstanceId == "storm-animation"));
        Assert.Null(presentation.PlayerIndex);
        Assert.StartsWith("触发 双方后排所有卡牌", presentation.Text);
        Assert.DoesNotContain("持续 军团无法发动远程进攻", presentation.Text);
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
    public void ShanheSearchPublishesTheSharedEffectHandAddAuthorityEvent()
    {
        var game = Create(seed: 88671);
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
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: searchPrompt.PromptId,
            Choice: eligible.InstanceId)).Accepted);

        Assert.Contains(game.State.AuthorityEvents, authorityEvent => authorityEvent.Type == "effect-hand-add"
            && authorityEvent.SourceInstanceId == eligible.InstanceId
            && authorityEvent.OriginZone == "library"
            && authorityEvent.DestinationZone == "hand");
    }

    [Fact]
    public void MarchSplitsItsIndependentParagraphsIntoSeparateStackItems()
    {
        var game = Create(seed: 88672);
        const int owner = 0;
        var player = game.State.Players[owner];
        var enemy = game.State.Players[1 - owner];
        var march = player.Hand.Concat(player.Library).First(card => card.CardId == "S01-0118");
        player.Hand.Remove(march);
        player.Library.Remove(march);
        player.Hand.Add(march);
        var friendly = CreateInstance("S01-0109", "march-friendly");
        friendly.SummonRound = 0;
        player.Field[0][0] = friendly;
        // 第二段只可击杀兵力不高于 6000 的军团；使用合法目标验证新堆叠，
        // 避免首段路由回归被无目标自动结算掩盖。
        var target = CreateInstance("S01-0402", "march-target");
        enemy.Field[0][0] = target;
        AddActiveMorale(player, 5);
        game.State.ActivePlayer = owner;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(owner, new L12Command("playCard", march.InstanceId)).Accepted);
        var buff = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", buff.Continuation);
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: buff.PromptId,
            Choice: friendly.InstanceId)).Accepted);
        var decision = Assert.Single(game.State.PendingPrompts);
        Assert.Single(game.State.EffectStack);
        Assert.Equal("effect-decision", decision.Data.GetValueOrDefault("uiPattern"));
        Assert.Equal(march.InstanceId, decision.Data.GetValueOrDefault("sourceInstanceId"));
        Assert.Equal(march.CardId, decision.Data.GetValueOrDefault("sourceCardId"));
        Assert.Equal(march.Name, decision.Data.GetValueOrDefault($"{march.InstanceId}:name"));
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: decision.PromptId,
            Choice: "mode:use")).Accepted);
        Assert.True(game.State.PendingPrompts.Count == 1,
            $"prompts={game.State.PendingPrompts.Count}; morale={player.Morale.Count}; tapped={player.Morale.Count(card => card.Tapped)}; stack={string.Join('|', game.State.EffectStack.Select(item => item.StackItemId + ':' + item.Data.GetValueOrDefault("atomicFlow")))}; deferred={string.Join('|', game.State.DeferredEffectStack.Select(item => item.StackItemId + ':' + item.Data.GetValueOrDefault("atomicFlow")))}; events={string.Join(" || ", game.State.Events.Select(entry => entry.Text))}");
        var cost = Assert.Single(game.State.PendingPrompts);
        var returnedMorale = cost.ValidChoices.Where(choice => choice != "skip").Take(2).ToList();
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: cost.PromptId,
            CardInstanceIds: returnedMorale)).Accepted);
        var kill = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(target.InstanceId, kill.ValidChoices);
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: kill.PromptId,
            Choice: target.InstanceId)).Accepted);
        Assert.DoesNotContain(returnedMorale, id => player.Morale.Any(resource => resource.InstanceId == id));
        Assert.Equal(friendly.BaseTroops + 2000, friendly.Troops);
        Assert.Contains(target, enemy.Graveyard);
        Assert.Contains(game.State.Events, gameEvent => gameEvent.Type == "stack-deferred"
            && gameEvent.Text.Contains("神妙行军", StringComparison.Ordinal));
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
        var mode = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", mode.Continuation);
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: mode.PromptId,
            Choice: "mode:use")).Accepted);
        var returnedMorale = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: returnedMorale.PromptId,
            Choice: returnedMorale.ValidChoices[0])).Accepted);
        var card = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(guanYu.InstanceId, card.ValidChoices);
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: card.PromptId,
            Choice: guanYu.InstanceId)).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(owner, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: slot.ValidChoices[0])).Accepted);

        var response = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("response", response.Kind);
        player.Field[0][0] = null;
        player.Graveyard.Add(legion);
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            response = game.State.PendingPrompts[0];
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        }

        Assert.Contains(legion, player.Graveyard);
        Assert.Contains(player.Field.SelectMany(row => row), fieldCard => fieldCard?.InstanceId == guanYu.InstanceId);
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
    public void ThunderWrathRollsForLegionsAboveTwoThousandAndMayEndTheAttack()
    {
        var sawEndedAttack = false;
        var sawContinuedAttack = false;
        for (var seed = 7800; seed < 7840 && (!sawEndedAttack || !sawContinuedAttack); seed++)
        {
            var game = Create(seed: seed);
            var attacker = CreateInstance("S02-0004", $"thunder-attacker-{seed}");
            attacker.SummonRound = -1;
            game.State.Players[0].Field[0][0] = attacker;
            game.State.Players[1].Field[0] = new L12CardInstance?[3];
            game.State.Players[1].Field[1] = new L12CardInstance?[3];
            game.State.ActiveDisaster = CreateInstance("S01-DS04", $"thunder-{seed}");
            game.State.ActivePlayer = 0;
            game.State.Phase = L12Phase.Main;
            var hpBefore = game.State.Players[1].Hp;

            Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
                Target: new L12AttackTarget("master"))).Accepted);
            var rollEvent = Assert.Single(game.State.Events, entry => entry.Type == "dice");
            var roll = int.Parse(rollEvent.Text[^1].ToString());
            if (roll <= 2)
            {
                sawEndedAttack = true;
                Assert.Null(game.State.PendingDefense);
                Assert.Equal(L12Phase.Main, game.State.Phase);
                Assert.True(attacker.Tapped);
                Assert.Equal(hpBefore, game.State.Players[1].Hp);
                Assert.Contains(game.State.Events, entry => entry.Type == "attack-ended"
                    && entry.Text.Contains("雷霆天怒"));
            }
            else
            {
                sawContinuedAttack = true;
                Assert.NotNull(game.State.PendingDefense);
                Assert.Equal(L12Phase.Defense, game.State.Phase);
            }
        }

        Assert.True(sawEndedAttack);
        Assert.True(sawContinuedAttack);
    }

    [Fact]
    public void ThunderWrathDoesNotRollForLegionsWithTwoThousandTroops()
    {
        var game = Create(seed: 7841);
        var attacker = CreateInstance("S02-0005", "thunder-low-attacker");
        attacker.SummonRound = -1;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0] = new L12CardInstance?[3];
        game.State.Players[1].Field[1] = new L12CardInstance?[3];
        game.State.ActiveDisaster = CreateInstance("S01-DS04", "thunder-low");
        game.State.ActivePlayer = 0;
        game.State.Phase = L12Phase.Main;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);

        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "dice");
        Assert.NotNull(game.State.PendingDefense);
    }

    [Fact]
    public void EveryPrintedOptionalSelfDamageEntryDiscountUsesStructuredSemantics()
    {
        var expected = Catalog.Cards.Values
            .Where(card => card.Effect?.Contains("可对我方主宰造成1点伤害：此军团登场费用-1", StringComparison.Ordinal) == true)
            .Select(card => card.Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(6, expected.Count);
        foreach (var definition in Catalog.Cards.Values)
            Assert.Equal(expected.Contains(definition.Id),
                L12StructuredCardRules.HasOptionalSelfDamageEntryDiscount(definition.Id));
    }

    [Fact]
    public void EveryTriggeredDisasterUsesStructuredPresentationSemantics()
    {
        var expected = Catalog.Cards.Values
            .Where(card => card.CardType == "destruction"
                && card.Effect?.Contains("触发", StringComparison.Ordinal) == true)
            .Select(card => card.Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(14, expected.Count);
        foreach (var definition in Catalog.Cards.Values.Where(card => card.CardType == "destruction"))
            Assert.Equal(expected.Contains(definition.Id),
                L12StructuredCardRules.HasTriggeredDisasterEffect(definition.Id));
    }

    [Fact]
    public void EveryPrintedActiveRestSourceUsesStructuredSemantics()
    {
        var expected = Catalog.Cards.Values
            .Where(card => card.CardType != "destruction"
                && card.Effect?.Contains("主动休整", StringComparison.Ordinal) == true)
            .Select(card => card.Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(27, expected.Count);
        foreach (var definition in Catalog.Cards.Values.Where(card => card.CardType != "destruction"))
            Assert.Equal(expected.Contains(definition.Id), L12StructuredCardRules.HasActiveRestAbility(definition.Id));
    }

    [Fact]
    public void CardInstanceCombatFlagsIgnoreMutableEffectText()
    {
        var archer = new L12CardInstance
        {
            InstanceId = "structured-archer",
            CardId = "S01-0110",
            Name = "结构化弓手",
            CardType = "legion",
            Faction = "tianting",
            EffectText = "不含任何战斗关键词",
        };
        var plain = new L12CardInstance
        {
            InstanceId = "fake-text-legion",
            CardId = "S02-0005",
            Name = "伪造文本军团",
            CardType = "legion",
            Faction = "universal",
            EffectText = "进攻距离+1，远程进攻无损。进攻无损。无法被远程进攻。",
        };
        var protectedLegion = new L12CardInstance
        {
            InstanceId = "structured-protected-legion",
            CardId = "S01-0101",
            Name = "结构化保护军团",
            CardType = "legion",
            Faction = "tianting",
            EffectText = string.Empty,
        };

        Assert.True(archer.HasRangeBonus);
        Assert.True(archer.HasRangedNoLoss);
        Assert.False(plain.HasRangeBonus);
        Assert.False(plain.HasRangedNoLoss);
        Assert.False(plain.HasAttackNoLoss);
        Assert.False(plain.CannotBeRanged);
        Assert.True(protectedLegion.HasAttackNoLoss);
        Assert.True(protectedLegion.CannotBeRanged);
    }

    [Fact]
    public void EveryPrintedRangedRuleUsesTheSharedConditionalCombatProfile()
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
            var front = L12StructuredCardRules.CombatProfile(instance, 0);
            var back = L12StructuredCardRules.CombatProfile(instance, 1);
            if (definition.Id is "S01-0409" or "S02-0507")
            {
                Assert.False(front.HasRangeBonus);
                Assert.False(front.HasRangedNoLoss);
                Assert.True(back.HasRangeBonus);
                Assert.True(back.HasRangedNoLoss);
            }
            else if (definition.Effect?.Contains("「位于前排」进攻距离+1", StringComparison.Ordinal) == true)
            {
                Assert.True(front.HasRangeBonus);
                Assert.True(front.HasRangedNoLoss);
                Assert.False(back.HasRangeBonus);
                Assert.False(back.HasRangedNoLoss);
            }
            else
            {
                Assert.True(front.HasRangeBonus);
                Assert.True(front.HasRangedNoLoss);
                Assert.True(back.HasRangeBonus);
                Assert.True(back.HasRangedNoLoss);
            }
        }
    }

    [Fact]
    public void EveryPrintedPermanentCombatRuleMatchesTheSharedCombatProfile()
    {
        var attackNoLoss = Catalog.Cards.Values
            .Where(card => card.Effect?.Split('\n')
                .Any(line => line.TrimStart().StartsWith("进攻无损", StringComparison.Ordinal)) == true)
            .Select(card => card.Id)
            .ToHashSet(StringComparer.Ordinal);
        var cannotBeRanged = Catalog.Cards.Values
            .Where(card => card.Effect?.Contains("无法被远程进攻", StringComparison.Ordinal) == true)
            .Select(card => card.Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("S01-0101", attackNoLoss);
        Assert.Contains("S02-0002", attackNoLoss);
        Assert.DoesNotContain("S01-0110", attackNoLoss);
        Assert.DoesNotContain("S02-0001", attackNoLoss);

        foreach (var definition in Catalog.Cards.Values)
        {
            var instance = new L12CardInstance
            {
                InstanceId = $"combat-{definition.Id}",
                CardId = definition.Id,
                Name = definition.NameZh,
                CardType = definition.CardType,
                Faction = definition.Faction,
                Profession = definition.Profession,
                EffectText = definition.Effect,
            };
            var profile = L12StructuredCardRules.CombatProfile(instance, 0);
            Assert.Equal(attackNoLoss.Contains(definition.Id), profile.HasAttackNoLoss);
            Assert.Equal(cannotBeRanged.Contains(definition.Id), profile.CannotBeRanged);
        }
    }

    [Theory]
    [InlineData("S01-0409", 2000, null)]
    [InlineData("S02-0507", 3000, "弓手")]
    public void BackRowConditionalCombatRulesSetAttackTroopsAndDerivedProfession(
        string cardId, int troops, string? profession)
    {
        var definition = Catalog.Cards[cardId];
        var instance = new L12CardInstance
        {
            InstanceId = $"test-{cardId}",
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            Profession = definition.Profession,
            EffectText = definition.Effect,
        };

        var front = L12StructuredCardRules.CombatProfile(instance, 0);
        var back = L12StructuredCardRules.CombatProfile(instance, 1);

        Assert.Null(front.AttackTroopsSetValue);
        Assert.Equal(troops, back.AttackTroopsSetValue);
        Assert.Equal(profession ?? definition.Profession, back.EffectiveProfession);
    }

    [Fact]
    public void DerivedArcherProfessionAlwaysGrantsTheCompleteProfessionCombatAbility()
    {
        var definition = Catalog.Cards["S02-0507"];
        var atalanta = new L12CardInstance
        {
            InstanceId = "derived-archer-profession",
            CardId = definition.Id,
            Name = definition.NameZh,
            CardType = definition.CardType,
            Faction = definition.Faction,
            Profession = definition.Profession,
            EffectText = definition.Effect,
        };

        var front = L12StructuredCardRules.CombatProfile(atalanta, 0);
        var back = L12StructuredCardRules.CombatProfile(atalanta, 1);

        Assert.NotEqual("弓手", front.EffectiveProfession);
        Assert.False(front.HasRangeBonus);
        Assert.False(front.HasRangedNoLoss);
        Assert.Equal("弓手", back.EffectiveProfession);
        Assert.True(back.HasRangeBonus);
        Assert.True(back.HasRangedNoLoss);
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
        var trigger = Assert.Single(recovery.State.PendingPrompts, prompt => prompt.Continuation == "pending-activation");
        Assert.True(recovery.Handle(0, new L12Command("resolvePrompt", PromptId: trigger.PromptId, Choice: "mode:use")).Accepted);
        while (recovery.State.PendingPrompts.FirstOrDefault()?.Continuation == "pending-activation")
        {
            var otherDeclaration = recovery.State.PendingPrompts[0];
            Assert.True(recovery.Handle(otherDeclaration.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: otherDeclaration.PromptId, Choice: "mode:none")).Accepted);
        }
        while (recovery.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var response = recovery.State.PendingPrompts[0];
            Assert.True(recovery.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        }
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
                : prompt.ValidChoices.Contains("mode:move") ? "mode:move"
                : prompt.ValidChoices.Contains(legion.InstanceId) ? legion.InstanceId
                : prompt.ValidChoices.Contains("0:1") ? "0:1"
                : prompt.ValidChoices[0];
            Assert.True(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        }

        Assert.Equal(handBefore + 1, player.Hand.Count);
        Assert.Null(player.Field[0][0]);
        Assert.Same(legion, player.Field[0][1]);
    }

    [Fact]
    public void EarthlyChangeReversesLibrariesExactlyOnceAndRestoresTheirCurrentOrder()
    {
        var game = Create(seed: 8864);
        var before = game.State.Players.Select(player => player.Library.Select(card => card.InstanceId).ToArray()).ToArray();
        var method = typeof(L12GameEngine).GetMethod("SetLibrariesReversedByDisaster",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        method.Invoke(game, [true]);
        Assert.True(game.State.LibrariesReversedByDisaster);
        for (var player = 0; player < 2; player++)
            Assert.Equal(before[player].Reverse(), game.State.Players[player].Library.Select(card => card.InstanceId));

        method.Invoke(game, [true]);
        for (var player = 0; player < 2; player++)
            Assert.Equal(before[player].Reverse(), game.State.Players[player].Library.Select(card => card.InstanceId));

        method.Invoke(game, [false]);
        Assert.False(game.State.LibrariesReversedByDisaster);
        for (var player = 0; player < 2; player++)
            Assert.Equal(before[player], game.State.Players[player].Library.Select(card => card.InstanceId));
    }

    [Fact]
    public void CorruptEarthRemovesRearRowFromEveryCommonPlacementAndMoveChoice()
    {
        var game = Create(seed: 8865);
        var player = game.State.Players[1];
        game.State.ActivePlayer = 1;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = CreateInstance("S01-DS03", "corrupt-earth-active");
        foreach (var morale in player.MoraleDeck.Take(2).ToArray())
        {
            player.MoraleDeck.Remove(morale);
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = morale.InstanceId,
                CardId = "ST04-C1",
                Tapped = morale.Tapped,
            });
        }
        var legion = player.Hand.First(card => card.CardType == "legion");
        player.Hand.Remove(legion);
        legion.SummonRound = 0;
        player.Field[0][0] = legion;

        Assert.True(game.Handle(1, new L12Command("activateAbility", "faction-1", Ability: "factionDrawMove")).Accepted);
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var response = game.State.PendingPrompts[0];
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        }
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("gaotianyuan-move-target", target.Data["action"]);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: target.PromptId, Choice: legion.InstanceId)).Accepted);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.DoesNotContain("1:0", slot.ValidChoices);
        Assert.Contains("0:1", slot.ValidChoices);
    }

    [Fact]
    public void SnapshotPublishesStructuredTemporaryStatusIcons()
    {
        var game = Create(seed: 8866);
        var player = game.State.Players[0];
        var legion = CreateInstance("S01-0108", "status-icon-legion");
        legion.TimedModifiers.Add(new L12TimedModifier { TroopsDelta = 2000, ExpiresAfterTurn = 5, Source = "须佐之男" });
        legion.TimedModifiers.Add(new L12TimedModifier { TroopsDelta = -1000, ExpiresAfterTurn = 5, Source = "测试减兵力" });
        legion.CannotAttack = true;
        legion.CannotSupport = true;
        legion.CannotReadyByEffectUntilTurn = 5;
        legion.ImmortalUses = 1;
        legion.ImmortalUntilTurn = 5;
        legion.DiscardAtEndOfTurnUntilTurn = 5;
        legion.ReadyAfterNextKillUntilTurn = 5;
        player.Field[0][0] = legion;
        game.State.TurnSerial = 1;

        var json = System.Text.Json.JsonSerializer.SerializeToElement(game.SnapshotFor(0));
        var fieldCard = json.GetProperty("Players")[0].GetProperty("field")[0][0];
        var kinds = fieldCard.GetProperty("StatusEffects").EnumerateArray()
            .Select(item => item.GetProperty("Kind").GetString()).ToArray();
        Assert.Contains("power-up", kinds);
        Assert.Contains("power-down", kinds);
        Assert.Contains("lock", kinds);
        Assert.Contains("disabled", kinds);
        Assert.Contains("shield", kinds);
        Assert.Contains("discard-end", kinds);
        Assert.Contains("extra-attack", kinds);
    }

    [Fact]
    public void MulanDeathResourceSelectionExplicitlyTargetsOpponentMoraleOnBoard()
    {
        var game = Create(seed: 8867);
        var mulanOwner = 0;
        var opponent = 1;
        var mulan = CreateInstance("S01-0108", "mulan-resource-target");
        game.State.Players[mulanOwner].Field[0][0] = mulan;
        var morale = game.State.Players[opponent].MoraleDeck[0];
        game.State.Players[opponent].MoraleDeck.RemoveAt(0);
        morale.Tapped = true;
        game.State.Players[opponent].Morale.Add(morale);
        game.State.ActivePlayer = opponent;

        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", mulanOwner,
            CardInstanceId: mulan.InstanceId)).Accepted);
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var response = game.State.PendingPrompts[0];
            Assert.True(game.Handle(response.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass")).Accepted);
        }
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("target-morale", prompt.Kind);
        Assert.Equal(opponent.ToString(), prompt.Data["targetPlayerIndex"]);
        Assert.Contains(morale.InstanceId, prompt.ValidChoices);
    }
}
