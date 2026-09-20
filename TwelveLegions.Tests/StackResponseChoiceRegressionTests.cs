using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed partial class StackResponseChoiceRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create()
    {
        var deck = Catalog.DeckAt(0);
        var game = new L12GameEngine(Catalog, "stack-choice", "STACK", 30501,
            ["甲", "乙"], [deck, deck], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        game.State.PendingPrompts.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string id, string instance, int owner,
        string? cardType = null, int? troops = null)
    {
        var definition = Catalog.Cards[id];
        return new L12CardInstance
        {
            InstanceId = instance, CardId = id, OwnerIndex = owner, Name = definition.NameZh,
            CardType = cardType ?? definition.CardType, Faction = definition.Faction,
            EffectText = definition.Effect, ImageUrl = definition.ImageUrl,
            Troops = troops ?? definition.Troops ?? 0, BaseTroops = troops ?? definition.Troops ?? 0,
            Cost = definition.Cost ?? 0, SummonRound = -1,
        };
    }

    private static L12StackItem AddEffect(L12GameEngine game, string id, string trigger = "enter", int owner = 0,
        string sourceCardId = "S01-0103")
    {
        var source = Card(sourceCardId, $"source-{id}", owner);
        if (trigger == "enter")
        {
            var slot = Array.FindIndex(game.State.Players[owner].Field[0], card => card is null);
            Assert.True(slot >= 0, "登场效果测试夹具必须为来源保留真实场上位置");
            game.State.Players[owner].Field[0][slot] = source;
        }
        var item = new L12StackItem
        {
            StackItemId = id, Controller = owner, SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId, SourceName = source.Name, SourceSnapshot = source,
            Trigger = trigger, Text = $"{id}：独立效果段", Negated = true,
        };
        game.State.EffectStack.Add(item);
        game.State.StackSequence++;
        return item;
    }

    private static L12CardInstance Counter(L12GameEngine game, int slot, string id = "S01-0018")
    {
        var card = Card(id, $"counter-{slot}", 1);
        card.Hidden = true;
        card.SetRound = 0;
        game.State.Players[1].Field[1][slot] = card;
        return card;
    }

    private static void Offer(L12GameEngine game, int priority = 1)
    {
        game.State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = priority };
        typeof(L12GameEngine).GetMethod("OfferResponse", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(game, null);
    }

    private static IReadOnlyList<string> LegalResponses(L12GameEngine game, int playerIndex, L12StackItem target)
        => Assert.IsAssignableFrom<IReadOnlyList<string>>(typeof(L12GameEngine)
            .GetMethod("LegalResponseSources", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [playerIndex, target]));

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, $"{result.Error}; {prompt.Continuation}: {string.Join(',', prompt.ValidChoices)}");
        return prompt;
    }

    [Theory]
    [InlineData("stack-1")]
    [InlineData("stack-2")]
    public void ResponseChoosesEitherLegalStackItemAndRetainsPriority(string selected)
    {
        var game = Create();
        AddEffect(game, "stack-1");
        AddEffect(game, "stack-2");
        var first = Counter(game, 0);
        var second = Counter(game, 1);
        Offer(game);
        Resolve(game, first.InstanceId);
        var selection = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("stack-response-target", selection.Continuation);
        Assert.Equal(new[] { "stack-1", "stack-2", "cancel" }, selection.ValidChoices);
        Assert.Contains("stack-1：独立效果段", selection.Data["stack-1"]);
        Assert.Equal("stack-1：独立效果段", selection.Data["stack-1:effect"]);
        Assert.True(first.Hidden);
        Resolve(game, selected);
        Assert.Equal(selected, Assert.Single(game.State.EffectStack[^1].Targets));
        Assert.Equal(1, game.State.ResponseWindow!.PriorityPlayer);
        Assert.Equal(0, game.State.ResponseWindow.ConsecutivePasses);
        Assert.Contains(second.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);
        Resolve(game, second.InstanceId);
        Resolve(game, selected);
        Assert.Equal(4, game.State.EffectStack.Count);
        Assert.Equal(1, game.State.ResponseWindow!.PriorityPlayer);
        Resolve(game, "pass");
        Assert.Equal(0, game.State.ResponseWindow!.PriorityPlayer);
        Assert.Equal(1, game.State.ResponseWindow.ConsecutivePasses);
        Resolve(game, "pass");
        Assert.Empty(game.State.EffectStack);
    }

    [Fact]
    public void ResponseShowsFullEffectAndExactSameNameTargetBeforeAndDuringPayment()
    {
        var game = Create();
        var victim = Card("S01-0103", "selected-victim", 1);
        game.State.Players[1].Field[0][0] = Card("S01-0103", "same-name-other", 1);
        game.State.Players[1].Field[0][1] = victim;
        var effect = new L12StackItem
        {
            StackItemId = "punishment", Controller = 0, Trigger = "play",
            SourceInstanceId = "punishment-card", SourceCardId = "S01-0418", SourceName = "天诛",
            Text = "击杀对方1张费用不高于7的军团。",
        };
        game.State.EffectStack.Add(effect);
        effect.Targets.Add(victim.InstanceId);
        var counter = Counter(game, 0, "S01-0016");
        game.State.Players[1].Hand.Add(Card("S01-0003", "payment", 1));
        Offer(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("对方使用〈天诛〉", prompt.Text);
        Assert.Contains("费用不高于7", prompt.Text);
        Assert.Contains($"我方〈{victim.Name}〉（前排第2格）", prompt.Text);
        Assert.Contains("是否响应？", prompt.Text);
        Assert.Equal(new[] { victim.InstanceId }, JsonSerializer.Deserialize<string[]>(prompt.Data["responseTargetIds"]));
        Resolve(game, counter.InstanceId);
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("stack-response-discard", payment.Continuation);
        Assert.Equal(prompt.Data["responseTargetIds"], payment.Data["responseTargetIds"]);
        Assert.Contains("对方使用〈天诛〉", payment.Text);
        game = Restore(game);
        Assert.Equal(payment.Data["responseTargetIds"], Assert.Single(game.State.PendingPrompts).Data["responseTargetIds"]);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0016,S02-0006")]
    [Trait("L12Evidence", "auxiliary-report:absolute-defense-faith-zealot")]
    public void AbsoluteDefenseDiscardCostDoesNotTriggerFaithZealot()
    {
        var game = Create();
        game.State.ActivePlayer = 1;
        var target = AddEffect(game, "faith-cost-target", owner: 0);
        target.Negated = false;
        var absoluteDefense = Counter(game, 0, "S01-0016");
        var zealot = Card("S02-0006", "faith-as-absolute-defense-cost", 1);
        game.State.Players[1].Hand.Add(zealot);

        Offer(game, 1);
        Resolve(game, absoluteDefense.InstanceId);
        var payment = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("stack-response-discard", payment.Continuation);
        Resolve(game, zealot.InstanceId);

        Assert.Contains(zealot, game.State.Players[1].Graveyard);
        Assert.DoesNotContain(game.State.PendingTriggerStackCandidates,
            candidate => candidate.SourceCardId == "S02-0006");
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-faith-zealot");
        Assert.DoesNotContain(game.State.Players[1].UsedAbilities,
            key => key.Contains("faith-zealot", StringComparison.OrdinalIgnoreCase)
                || key == "card-name:S02-0006");
    }

    [Fact]
    public void ResponseHighlightsOnlyPublicFieldInstancesAndKeepsHiddenIdentityPrivate()
    {
        var game = Create();
        var covered = Card("S01-0018", "covered-position", 0);
        covered.Hidden = true;
        game.State.Players[0].Field[1][1] = covered;
        var secret = Card("S01-0103", "private-hand-id", 0);
        game.State.Players[0].Hand.Add(secret);
        var effect = AddEffect(game, "public-target");
        effect.Targets.AddRange([covered.InstanceId, secret.InstanceId]);
        Offer(game);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("对方盖伏卡牌（后排第2格）", prompt.Text);
        Assert.DoesNotContain(covered.Name, prompt.Text);
        Assert.DoesNotContain(secret.InstanceId, JsonSerializer.Serialize(prompt));
        Assert.Equal(new[] { covered.InstanceId }, JsonSerializer.Deserialize<string[]>(prompt.Data["responseTargetIds"]));
    }

    [Fact]
    public void EachResponseOptionKeepsItsOwnTargetInstanceInsteadOfSameNameUnion()
    {
        var game = Create();
        for (var slot = 0; slot < 2; slot++)
        {
            var victim = Card("S01-0103", $"victim-{slot}", 1);
            game.State.Players[1].Field[0][slot] = victim;
            AddEffect(game, $"choice-{slot}").Targets.Add(victim.InstanceId);
        }
        var response = Counter(game, 0);
        Offer(game);
        Resolve(game, response.InstanceId);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(new[] { "victim-0" }, JsonSerializer.Deserialize<string[]>(prompt.Data["choice-0:responseTargetIds"]));
        Assert.Equal(new[] { "victim-1" }, JsonSerializer.Deserialize<string[]>(prompt.Data["choice-1:responseTargetIds"]));
    }

    [Fact]
    public void UniqueLegalLowerTargetAutoBindsWithoutBorrowingItsTimingForTheTop()
    {
        var game = Create();
        AddEffect(game, "stack-1");
        AddEffect(game, "stack-2", "active");
        var response = Counter(game, 0);
        Offer(game);
        Resolve(game, response.InstanceId);
        Assert.Equal("stack-1", Assert.Single(game.State.EffectStack[^1].Targets));
        Assert.Equal(3, game.State.EffectStack.Count);
    }

    [Fact]
    public void CancelRestoresSamePriorityWithoutRevealPaymentOrPassing()
    {
        var game = Create();
        AddEffect(game, "stack-1");
        AddEffect(game, "stack-2");
        var response = Counter(game, 0, "S01-0016");
        var discard = Card("S01-0003", "cost", 1);
        game.State.Players[1].Hand.Add(discard);
        Offer(game);
        Resolve(game, response.InstanceId);
        Resolve(game, "cancel");
        Assert.Equal(2, game.State.EffectStack.Count);
        Assert.True(response.Hidden);
        Assert.Contains(discard, game.State.Players[1].Hand);
        Assert.Equal(1, game.State.ResponseWindow!.PriorityPlayer);
        Assert.Equal(0, game.State.ResponseWindow.ConsecutivePasses);
    }

    [Fact]
    public void TargetChoiceAndPaidDeclarationRestoreAndDuplicateSubmissionCannotPayTwice()
    {
        var game = Create();
        AddEffect(game, "stack-1");
        AddEffect(game, "stack-2");
        var response = Counter(game, 0, "S01-0016");
        game.State.Players[1].Hand.Add(Card("S01-0003", "cost", 1));
        Offer(game);
        Resolve(game, response.InstanceId);
        game = Restore(game);
        Assert.NotNull(game.State.Players[1].Field[1][0]);
        Resolve(game, "stack-1");
        game = Restore(game);
        var payment = Resolve(game, "cost");
        Assert.Equal("stack-1", Assert.Single(game.State.EffectStack[^1].Targets));
        Assert.Single(game.State.Players[1].Graveyard, card => card.InstanceId == "cost");
        Assert.False(game.Handle(1, new L12Command("resolvePrompt", PromptId: payment.PromptId, Choice: "cost")).Accepted);
        Assert.Equal(3, game.State.EffectStack.Count);
        Assert.Single(game.State.Players[1].Graveyard, card => card.InstanceId == "cost");
    }

    [Fact]
    public void RemovedChosenTargetDoesNotRetargetOrSpendDeclaredCost()
    {
        var game = Create();
        var first = AddEffect(game, "stack-1");
        AddEffect(game, "stack-2");
        var response = Counter(game, 0, "S01-0016");
        game.State.Players[1].Hand.Add(Card("S01-0003", "cost", 1));
        Offer(game);
        Resolve(game, response.InstanceId);
        Resolve(game, "stack-1");
        game.State.EffectStack.Remove(first);
        Resolve(game, "cost");
        Assert.Single(game.State.EffectStack);
        Assert.Single(game.State.Players[1].Hand);
        Assert.True(response.Hidden);
        Assert.Equal(1, game.State.ResponseWindow!.PriorityPlayer);
    }

    private static L12GameEngine Restore(L12GameEngine game) => L12GameEngine.RestoreCheckpoint(Catalog,
        game.SerializeFullState(), game.RandomState!.Value, game.CardFactSignalSequence,
        autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    [Fact]
    public void NegationActuallyAffectsOnlyTheChosenLowerEffect()
    {
        var game = Create();
        var a = AddEffect(game, "stack-1", "active");
        var b = AddEffect(game, "stack-2", "active");
        a.Negated = false;
        b.Negated = false;
        var response = Counter(game, 0, "S01-0016");
        game.State.Players[1].Hand.Add(Card("S01-0003", "cost", 1));
        Offer(game);
        Resolve(game, response.InstanceId);
        Resolve(game, a.StackItemId);
        Resolve(game, "cost");
        Resolve(game, "pass");
        Resolve(game, "pass");
        Assert.True(a.Negated);
        Assert.False(b.Negated);
        Assert.Single(game.State.Events, item => item.Type == "effect-negated");
    }

    [Fact]
    public void PrivateHandAddTargetsNeverExposeTheirCardMetadata()
    {
        var game = Create();
        foreach (var id in new[] { "stack-1", "stack-2" })
        {
            var effect = AddEffect(game, id, "authority-event");
            effect.Data["eventType"] = "effect-hand-add";
        }
        var response = Counter(game, 0, "S02-0017");
        game.State.Players[0].Hand.Add(Card("S01-0003", "secret-hand", 0));
        Offer(game);
        var initial = Assert.Single(game.State.PendingPrompts);
        Assert.False(initial.Data.ContainsKey("sourceCardId"));
        Resolve(game, response.InstanceId);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("stack-response-target", prompt.Continuation);
        Assert.DoesNotContain(prompt.Data.Keys, key => key.EndsWith(":cardId") || key.EndsWith(":image"));
        Assert.DoesNotContain("secret-hand", string.Join('|', prompt.Data.Values));
    }

    [Fact]
    public void BothPlayersChooseActivationOrderAndTheResultResolvesInReverseOrder()
    {
        var game = Create();
        L12TriggerCandidate Candidate(int owner, string id) => new()
        {
            CandidateId = id, Controller = owner, SourceInstanceId = id, SourceCardId = "S01-0103",
            SourceName = id, Trigger = "active", Text = id,
            Data = new Dictionary<string, string> { ["declaration-complete"] = "true" },
        };
        var candidates = new[] { Candidate(1, "C"), Candidate(0, "A"), Candidate(1, "D"), Candidate(0, "B") };
        typeof(L12GameEngine).GetMethod("QueueTriggerCandidates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, [candidates]);
        var first = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(0, first.PlayerIndex);
        Assert.Contains("发动先后", first.Text);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: first.PromptId,
            CardInstanceIds: ["B", "A"])).Accepted);
        var second = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, second.PlayerIndex);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: second.PromptId,
            CardInstanceIds: ["C", "D"])).Accepted);
        Assert.Equal(new[] { "B", "A", "C", "D" }, game.State.EffectStack.Select(item => item.SourceName));
        // Use negated effects to observe the shared pop order without invoking unrelated card resolvers.
        foreach (var item in game.State.EffectStack) item.Negated = true;
        Resolve(game, "pass");
        Resolve(game, "pass");
        var resolved = game.State.Events.Where(item => item.Type == "stack-resolve").Select(item => item.Text).ToArray();
        Assert.Equal(4, resolved.Length);
        foreach (var pair in new[] { "D", "C", "A", "B" }.Select((id, index) => (id, index)))
            Assert.Contains($"〈{pair.id}〉", resolved[pair.index]);
    }

    [Fact]
    public void CheckpointRestoresEveryBoardSlotAndCoveredIdentityForBothPlayers()
    {
        var game = Create();
        for (var owner = 0; owner < 2; owner++)
            for (var row = 0; row < 2; row++)
                for (var slot = 0; slot < 3; slot++)
                {
                    var card = Card(row == 1 ? "S01-0018" : "S01-0103", $"board-{owner}-{row}-{slot}", owner);
                    card.Hidden = row == 1;
                    card.Tapped = slot == 1;
                    game.State.Players[owner].Field[row][slot] = card;
                }
        var restored = Restore(game);
        for (var owner = 0; owner < 2; owner++)
            for (var row = 0; row < 2; row++)
                for (var slot = 0; slot < 3; slot++)
                {
                    var card = Assert.IsType<L12CardInstance>(restored.State.Players[owner].Field[row][slot]);
                    Assert.Equal($"board-{owner}-{row}-{slot}", card.InstanceId);
                    Assert.Equal(owner, card.OwnerIndex);
                    Assert.Equal(row == 1, card.Hidden);
                    Assert.Equal(slot == 1, card.Tapped);
                }
    }

    [Fact]
    public void ACommittedPuppetCannotBeDeclaredAgainWhileStillInHand()
    {
        var game = Create();
        var attack = AddEffect(game, "stack-1", "opponent-attack");
        game.State.PendingDefense = new L12PendingDefense
        {
            AttackerPlayer = 0, AttackerInstanceId = attack.SourceInstanceId,
            Target = new L12AttackTarget("master"),
        };
        var puppet = Card("S02-0005", "pending-puppet", 1);
        game.State.Players[1].Hand.Add(puppet);
        Offer(game);
        Resolve(game, puppet.InstanceId);
        Resolve(game, "0:1");
        Assert.Contains(puppet, game.State.Players[1].Hand);
        Assert.Single(game.State.EffectStack, item => item.SourceInstanceId == puppet.InstanceId);
        Assert.DoesNotContain(puppet.InstanceId, Assert.Single(game.State.PendingPrompts).ValidChoices);
        Assert.Equal(1, game.State.ResponseWindow!.PriorityPlayer);
    }

    [Fact]
    public void PublicResponseAvailabilityUsesTheSameDeclarationCandidatesAsSubmission()
    {
        var attack = Create();
        var attackRoot = AddEffect(attack, "evaluation-attack", "opponent-attack");
        attack.State.PendingDefense = new L12PendingDefense
        {
            AttackerPlayer = 0, AttackerInstanceId = attackRoot.SourceInstanceId,
            Target = new L12AttackTarget("master"),
        };
        var emptyCity = Counter(attack, 0, "S01-0120");
        var dawn = Counter(attack, 1, "S01-0020");
        Offer(attack);
        var attackPrompt = Assert.Single(attack.State.PendingPrompts);
        Assert.DoesNotContain(emptyCity.InstanceId, attackPrompt.ValidChoices);
        Assert.Contains(dawn.InstanceId, attackPrompt.ValidChoices);

        foreach (var (cardId, eventType) in new[]
                 {
                     ("S02-0016", "non-hand-entry"),
                     ("S02-0017", "effect-hand-add"),
                 })
        {
            var game = Create();
            var root = AddEffect(game, $"evaluation-{cardId}", "authority-event");
            root.Data["eventType"] = eventType;
            var response = Counter(game, 0, cardId);
            Offer(game);
            Assert.DoesNotContain(response.InstanceId,
                Assert.Single(game.State.PendingPrompts).ValidChoices);
        }
    }

    [Theory]
    [InlineData("S01-0020")]
    [InlineData("S01-0120")]
    [InlineData("S02-0016")]
    [InlineData("S02-0017")]
    [L12AbilityEvidence("S01-0020:ability:reaction:f099e096c2d7437b", "commit-declaration", "reconnect-declaration", "duplicate-declaration", "presentation-declaration")]
    [L12AbilityEvidence("S01-0120:ability:reaction:0865f062354681b2", "commit-declaration", "reconnect-declaration", "duplicate-declaration", "presentation-declaration")]
    [L12AbilityEvidence("S02-0016:ability:s2-reaction:37e38b08d365f0bb", "commit-declaration", "reconnect-declaration", "duplicate-declaration", "presentation-declaration")]
    [L12AbilityEvidence("S02-0017:ability:s2-reaction:0e0643c2b48ae93e", "commit-declaration", "reconnect-declaration", "duplicate-declaration", "presentation-declaration")]
    public void PublicResponseDeclarationsRestoreAndRejectDuplicateFinalSubmission(string cardId)
    {
        var game = Create();
        var trigger = cardId.StartsWith("S01-", StringComparison.Ordinal)
            ? "opponent-attack" : "authority-event";
        var root = AddEffect(game, $"public-plan-{cardId}", trigger);
        root.Negated = false;
        if (trigger == "opponent-attack")
            game.State.PendingDefense = new L12PendingDefense
            {
                AttackerPlayer = 0, AttackerInstanceId = root.SourceInstanceId,
                Target = new L12AttackTarget("master"),
            };
        if (cardId == "S01-0120")
            game.State.Players[1].Morale.Add(new L12MoraleCard
            {
                CardId = "S01-01C1", InstanceId = "public-plan-morale",
            });
        if (cardId == "S01-0020")
            for (var index = 0; index < 5; index++)
                game.State.Players[1].Graveyard.Add(Card("S01-0003", $"public-plan-grave-{index}", 1));
        if (cardId == "S02-0016")
        {
            root.Data["eventType"] = "non-hand-entry";
            game.State.Players[0].Field[0][0] = root.SourceSnapshot;
        }
        if (cardId == "S02-0017")
        {
            root.Data["eventType"] = "effect-hand-add";
            game.State.Players[0].Hand.Add(Card("S01-0003", "public-plan-hidden", 0));
        }
        var response = Counter(game, 0, cardId);
        Offer(game);
        Resolve(game, response.InstanceId);

        string? finalPromptId = null;
        string? finalChoice = null;
        while (game.State.PendingPrompts.SingleOrDefault()?.Continuation == "pending-activation")
        {
            game = Restore(game);
            var prompt = Assert.Single(game.State.PendingPrompts);
            var choice = prompt.Kind switch
            {
                "resource-return" => prompt.ValidChoices.Single(candidate => candidate != "skip"),
                "opponent-hand-card" => prompt.ValidChoices.Single(candidate => candidate != "skip"),
                _ when cardId == "S02-0016" => "mode:suppress",
                _ => "mode:none",
            };
            finalPromptId = prompt.PromptId;
            finalChoice = choice;
            Assert.True(game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice)).Accepted);
        }

        Assert.NotNull(finalPromptId);
        Assert.NotNull(finalChoice);
        Assert.Single(game.State.EffectStack, item => item.SourceInstanceId == response.InstanceId);
        var responseEvent = Assert.Single(game.State.Events, entry => entry.Type is "effect-response" or "effect-announced"
            && entry.Cards.Any(card => card.InstanceId == response.InstanceId));
        var ability = Assert.Single(Catalog.AtomicEffects.Find(cardId)!.Abilities,
            candidate => candidate.Trigger == (cardId.StartsWith("S01-", StringComparison.Ordinal)
                ? "reaction" : "s2-reaction"));
        Assert.Equal(ability.AbilityId, responseEvent.EffectAbilityId);
        Assert.Contains(ability.Presentations, scene => scene.SceneId == responseEvent.EffectSceneId);
        Assert.False(game.Handle(1,
            new L12Command("resolvePrompt", PromptId: finalPromptId, Choice: finalChoice)).Accepted);
        Assert.Single(game.State.EffectStack, item => item.SourceInstanceId == response.InstanceId);
    }

    [Fact]
    public void BattleUntilDawnRevalidatesDrawModeAfterRestore()
    {
        var game = Create();
        var root = AddEffect(game, "stale-dawn-root", "opponent-attack");
        game.State.PendingDefense = new L12PendingDefense
        {
            AttackerPlayer = 0, AttackerInstanceId = root.SourceInstanceId,
            Target = new L12AttackTarget("master"),
        };
        for (var index = 0; index < 5; index++)
            game.State.Players[1].Graveyard.Add(Card("S01-0003", $"stale-dawn-{index}", 1));
        var response = Counter(game, 0, "S01-0020");
        Offer(game);
        Resolve(game, response.InstanceId);
        game = Restore(game);
        game.State.Players[1].Graveyard.Clear();
        Resolve(game, "mode:draw");
        Assert.True(game.State.Players[1].Field[1][0] is { Hidden: true });
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == response.InstanceId);
        Assert.Contains(game.State.Events, item => item.Type == "ability-rejected"
            && item.Text.Contains("战斗至黎明", StringComparison.Ordinal));
    }

    [Fact]
    public void SureHitExcludesOnlyResponsesThatWouldBlockTheAttack()
    {
        var game = Create();
        var attack = AddEffect(game, "sure-hit-attack", "opponent-attack");
        game.State.PendingDefense = new L12PendingDefense
        {
            AttackerPlayer = 0,
            AttackerInstanceId = attack.SourceInstanceId,
            Target = new L12AttackTarget("legion", "sure-hit-legion-target"),
            SureHit = true,
        };
        var absoluteDefense = Counter(game, 0, "S01-0016");
        var emptyCity = Counter(game, 1, "S01-0120");
        var battleUntilDawn = Counter(game, 2, "S01-0020");
        var mercenaries = Card("S01-0002", "sure-hit-mercenaries", 1);
        game.State.Players[1].Hand.Add(mercenaries);
        game.State.Players[1].Hand.Add(Card("S01-0003", "sure-hit-discard", 1));
        game.State.Players[1].Morale.Add(new L12MoraleCard
        {
            CardId = "S01-01C1",
            InstanceId = "sure-hit-returnable-morale",
        });

        var responses = LegalResponses(game, 1, attack);

        Assert.DoesNotContain(absoluteDefense.InstanceId, responses);
        Assert.DoesNotContain(emptyCity.InstanceId, responses);
        Assert.DoesNotContain(mercenaries.InstanceId, responses);
        Assert.Contains(battleUntilDawn.InstanceId, responses);

        game = Restore(game);
        attack = Assert.Single(game.State.EffectStack, item => item.StackItemId == "sure-hit-attack");
        responses = LegalResponses(game, 1, attack);
        Assert.DoesNotContain(absoluteDefense.InstanceId, responses);
        Assert.DoesNotContain(emptyCity.InstanceId, responses);
        Assert.DoesNotContain(mercenaries.InstanceId, responses);
        Assert.Contains(battleUntilDawn.InstanceId, responses);
    }

    [Fact]
    public void SureHitStillAllowsAbsoluteDefenseToNegateAnIndependentAttackEffect()
    {
        var game = Create();
        var attack = AddEffect(game, "sure-hit-root", "opponent-attack");
        var attackEffect = AddEffect(game, "sure-hit-triggered-effect", "attack");
        game.State.PendingDefense = new L12PendingDefense
        {
            AttackerPlayer = 0,
            AttackerInstanceId = attack.SourceInstanceId,
            Target = new L12AttackTarget("legion", "sure-hit-target"),
            SureHit = true,
        };
        var absoluteDefense = Counter(game, 0, "S01-0016");
        game.State.Players[1].Hand.Add(Card("S01-0003", "sure-hit-negate-discard", 1));

        Assert.DoesNotContain(absoluteDefense.InstanceId, LegalResponses(game, 1, attack));
        Assert.Contains(absoluteDefense.InstanceId, LegalResponses(game, 1, attackEffect));

        game = Restore(game);
        attack = Assert.Single(game.State.EffectStack, item => item.StackItemId == "sure-hit-root");
        attackEffect = Assert.Single(game.State.EffectStack, item => item.StackItemId == "sure-hit-triggered-effect");
        Assert.DoesNotContain(absoluteDefense.InstanceId, LegalResponses(game, 1, attack));
        Assert.Contains(absoluteDefense.InstanceId, LegalResponses(game, 1, attackEffect));
    }

    [Fact]
    public void AttackBlockingResponseIdentityMatchesTheCurrentCardPoolAndAtomicCatalog()
    {
        var expected = new[] { "S01-0002", "S01-0016", "S01-0120" };
        var actual = Catalog.Cards.Values
            .Where(L12CounterTacticRules.BlocksAttack)
            .Select(card => card.Id)
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
        Assert.All(expected, cardId => Assert.True(Catalog.AtomicEffects.Find(cardId)!.BlocksAttack));
        Assert.All(Catalog.AtomicEffects.All.Where(card => !expected.Contains(card.CardId, StringComparer.Ordinal)),
            card => Assert.False(card.BlocksAttack));
        Assert.Equal(new[] { "S01-0016", "S01-0120" }, Catalog.Cards.Values
            .Where(card => L12CounterTacticRules.IsCounterTactic(card) && card.BlocksAttack)
            .Select(card => card.Id)
            .OrderBy(cardId => cardId, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("S02-0016", "discard", "normal")]
    [InlineData("S02-0016", "discard", "moved")]
    [InlineData("S02-0016", "discard", "negated")]
    [InlineData("S02-0016", "suppress", "normal")]
    [InlineData("S02-0016", "suppress", "moved")]
    [InlineData("S02-0016", "suppress", "non-legion")]
    [InlineData("S02-0016", "suppress", "negated")]
    [InlineData("S02-0016", "suppress", "protected")]
    [InlineData("S02-0017", "return", "normal")]
    [InlineData("S02-0017", "return", "moved")]
    [InlineData("S02-0017", "return", "negated")]
    [InlineData("S02-0018", "negate-ready", "normal")]
    [InlineData("S02-0018", "negate-ready", "missing-event")]
    [InlineData("S02-0018", "negate-ready", "negated")]
    [L12AbilityEvidence("S02-0016:ability:s2-reaction:37e38b08d365f0bb", "normal-settlement", "negated-settlement", "target-invalidated-settlement", "reconnect-settlement", "duplicate-rejected")]
    [L12AbilityEvidence("S02-0017:ability:s2-reaction:0e0643c2b48ae93e", "normal-settlement", "negated-settlement", "target-invalidated-settlement", "reconnect-settlement", "duplicate-rejected", "success-dependency")]
    [L12AbilityEvidence("S02-0018:ability:s2-reaction:e0e92d0479a94844", "normal-settlement", "negated-settlement", "target-invalidated-settlement", "reconnect-settlement", "duplicate-rejected", "success-dependency")]
    public void ResponseSettlementRevalidatesObjectsAndSuccessDependenciesAfterRecovery(
        string cardId, string branch, string outcome)
    {
        var game = Create();
        var root = AddEffect(game, "settlement-root", "authority-event", sourceCardId:
            outcome == "protected" ? "ST02-01" : "S01-0103");
        root.Data["eventType"] = cardId switch
        {
            "S02-0016" => "non-hand-entry",
            "S02-0017" => "effect-hand-add",
            _ => "effect-ready",
        };
        var entered = Card(outcome == "protected" ? "ST02-01" : root.SourceCardId,
            root.SourceInstanceId, 0, troops: 6000);
        if (outcome == "protected")
            entered.SummonRound = game.State.Round;
        game.State.Players[0].Field[0][0] = entered;
        var chosen = Card("S01-0003", "settlement-chosen", 0);
        game.State.Players[0].Hand.Add(chosen);
        game.State.Players[1].Library.Clear();
        game.State.Players[1].Library.Add(Card("S01-0003", "settlement-draw", 1));
        var response = Counter(game, 0, cardId);
        Offer(game);
        var submitted = Resolve(game, response.InstanceId);
        if (cardId == "S02-0016") submitted = Resolve(game, $"mode:{branch}");
        if (branch is "discard" or "return")
        {
            var selection = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("opponent-hand-card", selection.Kind);
            // Even a sole private object requires a real player choice.
            submitted = Resolve(game, selection.ValidChoices.Single(choice => choice != "skip"));
        }
        var responseItem = Assert.Single(game.State.EffectStack, item => item.SourceInstanceId == response.InstanceId);
        if (outcome == "negated") responseItem.Negated = true;
        if (outcome == "moved")
        {
            if (branch == "suppress") game.State.Players[0].Field[0][0] = null;
            else game.State.Players[0].Hand.Remove(chosen);
            game.State.Players[0].Graveyard.Add(branch == "suppress" ? entered : chosen);
        }
        if (outcome == "non-legion") game.State.Players[0].Field[0][0] =
            Card(entered.CardId, entered.InstanceId, 0, cardType: "artifact", troops: 6000);
        if (outcome == "missing-event") game.State.EffectStack.Remove(root);
        game = Restore(game);
        var restoredAuthority = game.State.EffectStack.FirstOrDefault(item => item.StackItemId == root.StackItemId);

        for (var step = 0; step < 32 && game.State.PendingPrompts.Count > 0; step++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.Kind == "response") Resolve(game, "pass");
            else if (cardId == "S02-0018" && outcome == "normal" && prompt.Kind == "hand-card")
                Resolve(game, chosen.InstanceId);
            else break;
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        var results = game.State.Events.Where(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == cardId)).OrderBy(entry => entry.EffectSegmentIndex).ToArray();
        Assert.NotEmpty(results);
        Assert.Equal(outcome == "normal" ? "resolved" : outcome == "negated" ? "negated" : "failed",
            results[0].EffectResultStatus);
        if (cardId is "S02-0017" or "S02-0018")
        {
            Assert.Equal(2, results.Length);
            Assert.Equal(outcome == "normal" ? "resolved" : "skipped", results[1].EffectResultStatus);
        }
        if (cardId == "S02-0017")
        {
            Assert.Equal(outcome == "normal", game.State.Players[1].Hand.Any(card => card.InstanceId == "settlement-draw"));
            Assert.Equal(outcome == "normal", game.State.Players[0].Library.Any(card => card.InstanceId == chosen.InstanceId));
        }
        if (cardId == "S02-0018")
            Assert.Equal(outcome == "normal", game.State.Players[0].Graveyard.Any(card => card.InstanceId == chosen.InstanceId));
        if (cardId == "S02-0016" && branch == "discard")
            Assert.Equal(outcome != "negated", game.State.Players[0].Graveyard.Any(card => card.InstanceId == chosen.InstanceId));
        if (branch == "suppress" && outcome != "moved")
            Assert.Equal(outcome == "normal" ? 3000 : 6000, game.State.Players[0].Field[0][0]!.Troops);
        if (branch == "suppress")
            Assert.Equal(outcome == "normal", restoredAuthority?.Data.GetValueOrDefault("suppressEnter") == "true");
        Assert.Single(game.State.Players.SelectMany(player => player.Hand.Concat(player.Graveyard).Concat(player.Library)),
            card => card.InstanceId == chosen.InstanceId);
        Assert.False(game.Handle(submitted.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: submitted.PromptId, Choice: "pass")).Accepted);
        Assert.Equal(results.Length, game.State.Events.Count(entry => entry.Type == "effect-result"
            && entry.Cards.Any(card => card.CardId == cardId)));
    }

    [Fact]
    public void EmptyCityRevalidatesReservedCostAfterRestoreWithoutPaying()
    {
        var game = Create();
        var root = AddEffect(game, "stale-empty-root", "opponent-attack");
        game.State.PendingDefense = new L12PendingDefense
        {
            AttackerPlayer = 0, AttackerInstanceId = root.SourceInstanceId,
            Target = new L12AttackTarget("master"),
        };
        var morale = new L12MoraleCard { CardId = "S01-01C1", InstanceId = "stale-empty-cost" };
        game.State.Players[1].Morale.Add(morale);
        var response = Counter(game, 0, "S01-0120");
        Offer(game);
        Resolve(game, response.InstanceId);
        Resolve(game, morale.InstanceId);
        game = Restore(game);
        game.State.Players[1].Morale.Clear();
        Resolve(game, "mode:none");
        Assert.True(game.State.Players[1].Field[1][0] is { Hidden: true });
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == response.InstanceId);
        Assert.DoesNotContain(game.State.Events, item => item.Type == "cost"
            && item.Text.Contains("空城计", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("S02-0016", "non-hand-entry")]
    [InlineData("S02-0017", "effect-hand-add")]
    public void AnonymousHandResponseRevalidatesTheTargetAfterRestore(string cardId, string eventType)
    {
        var game = Create();
        var root = AddEffect(game, $"stale-anonymous-{cardId}", "authority-event");
        root.Data["eventType"] = eventType;
        var hidden = Card("S01-0003", $"stale-hidden-{cardId}", 0);
        game.State.Players[0].Hand.Add(hidden);
        var response = Counter(game, 0, cardId);
        Offer(game);
        Resolve(game, response.InstanceId);
        if (cardId == "S02-0016") Resolve(game, "mode:discard");
        game = Restore(game);
        var declaration = Assert.Single(game.State.PendingPrompts);
        var anonymous = declaration.ValidChoices.Single(choice => choice != "skip");
        game.State.Players[0].Hand.Clear();
        Resolve(game, anonymous);
        Assert.True(game.State.Players[1].Field[1][0] is { Hidden: true });
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == response.InstanceId);
        Assert.DoesNotContain(game.State.Players[0].Graveyard, card => card.InstanceId == hidden.InstanceId);
        Assert.Contains(game.State.Events, item => item.Type == "ability-rejected");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[null,[null,null,null]]")]
    [InlineData("[[null,null],[null,null,null]]")]
    [InlineData("[[null,null,null],[null,null,null],[null,null,null]]")]
    public void CheckpointRejectsMissingNullOrMalformedBoardInsteadOfRestoringAnEmptyBoard(string field)
    {
        var game = Create();
        var document = JsonNode.Parse(game.SerializeFullState())!;
        var player = document["Players"]![0]!.AsObject();
        if (field == "missing") player.Remove("Field");
        else player["Field"] = JsonNode.Parse(field);
        var error = Record.Exception(() => L12GameEngine.RestoreCheckpoint(Catalog, document.ToJsonString(),
            game.RandomState!.Value, game.CardFactSignalSequence));
        Assert.True(error is JsonException or InvalidDataException, error?.ToString() ?? "损坏的战场没有被拒绝");
    }
}
