using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class StackResponseChoiceRegressionTests
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

    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var definition = Catalog.Cards[id];
        return new L12CardInstance
        {
            InstanceId = instance, CardId = id, OwnerIndex = owner, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction,
            EffectText = definition.Effect, ImageUrl = definition.ImageUrl,
            Troops = definition.Troops ?? 0, BaseTroops = definition.Troops ?? 0,
            Cost = definition.Cost ?? 0, SummonRound = -1,
        };
    }

    private static L12StackItem AddEffect(L12GameEngine game, string id, string trigger = "enter", int owner = 0)
    {
        var source = Card("S01-0103", $"source-{id}", owner);
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
