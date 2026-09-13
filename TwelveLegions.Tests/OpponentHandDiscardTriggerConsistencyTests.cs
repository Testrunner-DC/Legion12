using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class OpponentHandDiscardTriggerConsistencyTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "opponent-discard-one", "DISCARD-ONE", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 7;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Resolving.Clear();
            player.Library.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner = 0)
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
            SummonRound = -1,
            OwnerIndex = owner,
        };
    }

    private static object? Invoke(object target, string method, params object?[] args)
    {
        var candidate = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(info => info.Name == method && info.GetParameters().Length == args.Length);
        return candidate.Invoke(target, args);
    }

    private static L12CardInstance Queue(L12GameEngine game, string cardId, string trigger)
    {
        var source = Card(cardId, $"discard-source-{cardId}-{trigger}");
        game.State.Players[0].Resolving.Add(source);
        Invoke(game, "QueueOrPushTriggeredEffect", 0, source, trigger,
            "令对方弃置1张手牌一致性测试", null, null);
        return source;
    }

    private static void ResolvePrompt(L12GameEngine game, L12Prompt prompt, string choice)
    {
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 100 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
        {
            var response = Assert.Single(game.State.PendingPrompts, prompt => prompt.Kind == "response");
            ResolvePrompt(game, response, "pass");
        }
    }

    private static L12CardInstance AddOpponentHand(L12GameEngine game, string instanceId)
    {
        var card = Card("S01-0001", instanceId, owner: 1);
        game.State.Players[1].Hand.Add(card);
        return card;
    }

    private static void SatisfyCondition(L12GameEngine game, string cardId)
    {
        if (cardId == "S01-0209")
            for (var index = game.State.Players[1].Hand.Count; index < 6; index++)
                AddOpponentHand(game, $"nefertiti-extra-{index}");
        else if (cardId == "S02-0515")
            game.State.Players[0].Morale.Add(new L12MoraleCard
            {
                InstanceId = "helen-god-power", CardId = "S02-05C1", IsGodPower = true,
            });
    }

    [Fact]
    [Trait("L12Evidence", "entry:opponent-hand-discard-one-spec")]
    public void FiveExactSingleOpponentDiscardTriggersUseOneParameterizedProtocol()
    {
        var expected = new[]
        {
            "S01-0209|enter", "S01-0308|after-damage", "S02-0515|enter",
            "S02-0605|death", "ST04-02|attack",
        };
        Assert.Equal(expected, L12OpponentHandDiscardTriggerEffects.All
            .Select(spec => $"{spec.CardId}|{spec.Trigger}"));

        var textMatches = Catalog.Cards.Values
            .Where(card => (card.Effect ?? string.Empty).Contains("对方弃置1张手牌", StringComparison.Ordinal))
            .Select(card => card.Id).OrderBy(cardId => cardId, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "S01-0209", "S01-0308", "S02-0018", "S02-0515", "S02-0605", "ST04-02" },
            textMatches);
        Assert.Null(L12OpponentHandDiscardTriggerEffects.Find("S02-0018", "s2-reaction"));

        foreach (var spec in L12OpponentHandDiscardTriggerEffects.All)
        {
            var ability = Catalog.AtomicEffects.Find(spec.CardId)!.Abilities
                .Single(item => item.Sequence == spec.AbilitySequence);
            Assert.Equal(spec.SettlementText.TrimEnd('。'), ability.Text.TrimEnd('。'));
            Assert.Equal(spec.SettlementText,
                Assert.Single(ability.Presentations, scene =>
                    scene.Flow == L12SingleSegmentTriggeredEffectPresentations.Flow).DefaultText);

            var program = Assert.IsType<L12VerifiedAtomicProgram>(
                L12VerifiedAtomicPrograms.Find(spec.CardId, spec.Trigger));
            var flow = Assert.Single(program.Atoms, atom => atom.Kind == L12AtomKinds.CompositeFlow);
            Assert.Equal(L12OpponentHandDiscardTriggerEffects.Flow, flow.Parameters["flow"]);
            Assert.Equal("opponent", flow.Parameters["chooser"]);
            Assert.Equal("1", flow.Parameters["amount"]);
            Assert.Equal(spec.SettlementText,
                L12GameEngine.ResolveTriggeredEffectDisplayText(
                    Card(spec.CardId, $"display-{spec.CardId}"), spec.Trigger, "触发效果"));
        }
    }

    [Theory]
    [InlineData("S01-0209", "enter")]
    [InlineData("S01-0308", "after-damage")]
    [InlineData("S02-0515", "enter")]
    [InlineData("S02-0605", "death")]
    [InlineData("ST04-02", "attack")]
    [Trait("L12Evidence", "entry:opponent-hand-discard-one-private-choice")]
    public void AffectedOpponentMustPrivatelyChooseEvenTheOnlyHandCard(string cardId, string trigger)
    {
        var game = Create(10300 + cardId.Length + trigger.Length);
        var selected = AddOpponentHand(game, $"only-{cardId}");
        SatisfyCondition(game, cardId);
        Queue(game, cardId, trigger);
        PassResponses(game);

        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1, prompt.PlayerIndex);
        Assert.True(prompt.IsPrivate);
        Assert.Equal(L12OpponentHandDiscardTriggerEffects.Continuation, prompt.Data["action"]);
        Assert.Contains(selected.InstanceId, prompt.ValidChoices);
        Assert.DoesNotContain(selected.InstanceId, JsonSerializer.Serialize(game.SnapshotFor(0)),
            StringComparison.Ordinal);

        ResolvePrompt(game, prompt, selected.InstanceId);
        Assert.DoesNotContain(game.State.Players[1].Hand, card => card.InstanceId == selected.InstanceId);
        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == selected.InstanceId);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "resolved"
            && entry.Cards.Any(card => card.CardId == cardId));
    }

    [Fact]
    [Trait("L12Evidence", "entry:bors-real-death-route")]
    public void BorsIsRecognizedByTheAuthoritativeDeathTriggerGate()
    {
        var game = Create(10320);
        var bors = Card("S02-0605", "bors-death-gate");

        Assert.True(Assert.IsType<bool>(Invoke(game, "HasDeathTrigger", bors)));
    }

    [Theory]
    [InlineData("S01-0209", "enter")]
    [InlineData("S01-0308", "after-damage")]
    [InlineData("S02-0515", "enter")]
    [InlineData("S02-0605", "death")]
    [InlineData("ST04-02", "attack")]
    [Trait("L12Evidence", "entry:opponent-hand-discard-one-ineligible")]
    public void UnmetConditionOrEmptyOpponentHandSilentlySkipsBeforeStack(string cardId, string trigger)
    {
        var game = Create(10330 + cardId.Length);
        if (cardId == "ST04-02")
        {
            game.State.Players[0].Hand.Add(Card("S01-0001", "kojiro-own-1"));
            game.State.Players[0].Hand.Add(Card("S01-0001", "kojiro-own-2"));
            AddOpponentHand(game, "kojiro-opponent-1");
        }
        Queue(game, cardId, trigger);

        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.PendingPrompts);
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.Cards.Any(card => card.CardId == cardId));
    }

    [Fact]
    [Trait("L12Evidence", "entry:opponent-hand-discard-one-response-revalidation")]
    public void OpponentHandBecomingEmptyDuringResponseMakesTheStackedEffectFail()
    {
        var game = Create(10340);
        var target = AddOpponentHand(game, "bors-response-target");
        Queue(game, "S02-0605", "death");
        game.State.Players[1].Hand.Remove(target);
        game.State.Players[1].Graveyard.Add(target);
        PassResponses(game);

        Assert.Empty(game.State.PendingPrompts);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "failed"
            && entry.Cards.Any(card => card.CardId == "S02-0605"));
    }

    [Fact]
    [Trait("L12Evidence", "entry:opponent-hand-discard-one-negated")]
    public void NegatedEffectDoesNotRevealOrDiscardTheOpponentsHand()
    {
        var game = Create(10341);
        var target = AddOpponentHand(game, "bors-negated-target");
        Queue(game, "S02-0605", "death");
        Assert.Single(game.State.EffectStack).Negated = true;
        PassResponses(game);

        Assert.Contains(game.State.Players[1].Hand, card => card.InstanceId == target.InstanceId);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Single(game.State.Events, entry => entry.Type == "effect-result"
            && entry.EffectResultStatus == "negated"
            && entry.Cards.Any(card => card.CardId == "S02-0605"));
    }

    [Fact]
    [Trait("L12Evidence", "entry:opponent-hand-discard-one-reconnect")]
    public void PrivateDiscardChoiceRestoresAndRejectsDuplicateSubmission()
    {
        var game = Create(10342);
        var target = AddOpponentHand(game, "bors-restored-target");
        Queue(game, "S02-0605", "death");
        PassResponses(game);
        var oldPrompt = Assert.Single(game.State.PendingPrompts);
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var checkpoint = game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,");

        game = L12GameEngine.RestoreCheckpoint(Catalog, checkpoint, random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        var restored = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(oldPrompt.PromptId, restored.PromptId);
        Assert.Contains(target.InstanceId, restored.ValidChoices);
        ResolvePrompt(game, restored, target.InstanceId);

        Assert.Contains(game.State.Players[1].Graveyard, card => card.InstanceId == target.InstanceId);
        Assert.False(game.Handle(1, new L12Command("resolvePrompt",
            PromptId: restored.PromptId, Choice: target.InstanceId)).Accepted);
    }
}
