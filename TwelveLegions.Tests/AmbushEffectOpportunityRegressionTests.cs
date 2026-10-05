using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AmbushEffectOpportunityRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12CardInstance Card(string id, string instance, int owner)
    {
        var definition = Catalog.Cards[id];
        return new L12CardInstance
        {
            CardId = id, InstanceId = instance, OwnerIndex = owner, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction, ImageUrl = definition.ImageUrl,
            Cost = definition.Cost ?? 0, HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect, Traits = [.. definition.Traits],
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0, SummonRound = -1,
        };
    }

    private static L12GameEngine Create(int attacker, int? kagutsuchiOwner = null, bool conceal = false,
        bool autoPass = false)
    {
        var basis = Catalog.DeckAt(0);
        L12PresetDeckDefinition Deck(string master) => new()
        {
            Name = "合成伏击时点", MasterId = master,
            CardIds = [.. basis.CardIds], MoraleIds = [.. basis.MoraleIds], SpecialIds = [],
        };
        var decks = new[] { Deck("S01-01M1"), Deck("S01-01M1") };
        if (kagutsuchiOwner is { } owner) decks[owner] = Deck("ST04-M1");
        var game = new L12GameEngine(Catalog, "synthetic-ambush-opportunity", "LOCAL", 1400000 + attacker,
            ["合成甲", "合成乙"], decks, skipPreparation: true, autoPassEmptyResponses: autoPass,
            concealHiddenResponseAvailability: conceal, stateFormatVersion: 2);
        game.State.ActivePlayer = attacker;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        game.State.DisasterDeck.Clear();
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Graveyard.Clear(); player.Resolving.Clear();
            player.Morale.Clear(); player.MoraleDeck.Clear(); player.UsedAbilities.Clear();
            player.Relic = null; player.ExtraRelics.Clear();
            for (var i = 0; i < 2; i++) player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"morale-{player.PlayerIndex}-{i}", CardId = "S01-04C1",
            });
            player.Field[0][0] = Card("S01-0002", $"legion-{player.PlayerIndex}", player.PlayerIndex);
        }
        game.State.Revision++;
        return game;
    }

    private static void SetAmbush(L12GameEngine game, int owner)
    {
        var card = Card("S01-0019", "ambush", owner);
        card.Hidden = true;
        game.State.Players[owner].Field[1][2] = card;
    }

    private static void Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt",
            PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static L12GameEngine Recover(L12GameEngine game, bool recover) => !recover ? game
        : L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    public static IEnumerable<object[]> CombatCases()
    {
        foreach (var attacker in new[] { 0, 1 })
        foreach (var defensive in new[] { false, true })
        foreach (var recover in new[] { false, true })
        foreach (var payment in new[] { "mode:morale", "mode:discard" })
            yield return [attacker, defensive, recover, payment];
    }

    [Theory]
    [MemberData(nameof(CombatCases))]
    [Trait("L12Evidence", "ruling:ambush-actual-effect-20261005")]
    public void AmbushRespondsToPaidKagutsuchiEffectOnEitherSide(int attacker, bool defensive,
        bool recover, string payment)
    {
        var effectOwner = defensive ? 1 - attacker : attacker;
        var responseOwner = 1 - effectOwner;
        var game = Create(attacker, effectOwner);
        SetAmbush(game, responseOwner);
        game.State.Players[effectOwner].Hand.Add(Card("S01-0003", "discard", effectOwner));
        game = Recover(game, recover);
        Assert.True(game.Handle(attacker, new L12Command("attack", $"legion-{attacker}",
            Target: new L12AttackTarget("legion", $"legion-{1 - attacker}"))).Accepted);
        for (var step = 0; step < 30; step++)
        {
            game = Recover(game, recover);
            var prompt = Assert.Single(game.State.PendingPrompts);
            var top = game.State.EffectStack.LastOrDefault();
            if (prompt.Kind == "response" && top?.SourceCardId == "ST04-M1")
            {
                Assert.Equal(attacker, prompt.PlayerIndex);
                if (prompt.PlayerIndex != responseOwner) Resolve(game, "pass");
                var response = Assert.Single(game.State.PendingPrompts);
                Assert.Contains("ambush", response.ValidChoices);
                Assert.Equal(top.StackItemId, response.StackItemId);
                Resolve(game, "ambush");
                Resolve(game, $"legion-{responseOwner}");
                var committed = game.State.EffectStack[^1];
                Assert.Equal("S01-0019", committed.SourceCardId);
                Assert.Equal(top.StackItemId, Assert.Single(committed.Targets));
                Assert.False(game.State.Players[responseOwner].Field[1][2]?.Hidden ?? false);
                return;
            }
            Resolve(game, prompt.Kind == "response" ? "pass"
                : prompt.ValidChoices.Contains(payment) ? payment
                : prompt.ValidChoices.Contains("discard") ? "discard"
                : prompt.ValidChoices.First(id => id is not ("skip" or "mode:none")));
        }
        Assert.Fail("未到达迦具土效果的独立响应窗口");
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [Trait("L12Evidence", "ruling:ambush-direct-opponent-attack-20261005")]
    public void AmbushCanStartAtOpponentAttackWithoutAnEarlierEffectAndRequiresBothPasses(int attacker,
        bool recover)
    {
        var game = Create(attacker);
        SetAmbush(game, 1 - attacker);
        game = Recover(game, recover);
        Assert.True(game.Handle(attacker, new L12Command("attack", $"legion-{attacker}",
            Target: new L12AttackTarget("legion", $"legion-{1 - attacker}"))).Accepted);
        var first = Assert.Single(game.State.PendingPrompts);
        var attack = Assert.Single(game.State.EffectStack);
        Assert.Equal("opponent-attack", attack.Trigger);
        Assert.Equal(attacker, first.PlayerIndex);
        Resolve(game, "pass");
        game = Recover(game, recover);
        Assert.Equal(1, game.State.ResponseWindow!.ConsecutivePasses);
        Assert.Equal(attack.StackItemId, Assert.Single(game.State.EffectStack).StackItemId);
        var second = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(1 - attacker, second.PlayerIndex);
        Assert.Contains("ambush", second.ValidChoices);
        Resolve(game, "ambush");
        Resolve(game, $"legion-{1 - attacker}");
        Assert.Equal(0, game.State.ResponseWindow!.ConsecutivePasses);
        Assert.Equal(attack.StackItemId, Assert.Single(game.State.EffectStack[^1].Targets));
    }

    public static IEnumerable<object[]> EffectTimings()
    {
        foreach (var trigger in new[] { "enter", "promotion-enter", "active", "play", "attack",
            "legion-attack-timing", "after-attack", "death", "leave", "turn-start", "turn-end",
            "trial-advance", "trial-complete", "discard-trigger", "master-morale-return",
            "morrigan-enemy-death", "medjed-master-damage", "rune-spent", "return-library-top",
            "reaction", "s2-reaction", "response-negate", "future-card-trigger" })
        foreach (var owner in new[] { 0, 1 })
        foreach (var cardId in new[] { "S01-0019", "S01-0016", "S02-0106" })
            yield return [trigger, owner, cardId];
    }

    [Theory]
    [MemberData(nameof(EffectTimings))]
    [Trait("L12Evidence", "family:opponent-actual-effect-response")]
    public void ActualAndAnonymousEligibilityShareTheSameEffectFamily(string trigger, int owner, string cardId)
    {
        var game = Create(0, conceal: true);
        var responseCard = Card(cardId, "family-response", owner);
        responseCard.Hidden = true;
        game.State.Players[owner].Field[1][2] = responseCard;
        game.State.Players[owner].Hand.Add(Card("S01-0003", "absolute-defense-cost", owner));
        var source = game.State.Players[1 - owner].Field[0][0]!;
        var item = new L12StackItem
        {
            StackItemId = "synthetic-effect", Controller = 1 - owner,
            SourceInstanceId = source.InstanceId, SourceCardId = source.CardId,
            SourceName = source.Name, Trigger = trigger, Text = "合成已发动效果",
        };
        bool Invoke(string method, params object[] arguments) => (bool)typeof(L12GameEngine)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, arguments)!;
        var method = cardId == "S01-0016" ? "CanAbsoluteDefenseRespondTo"
            : cardId == "S02-0106" ? "CanUseS2CounterAtStack" : "CanUseS1ResponseAtCurrentEffect";
        bool Eligible(L12StackItem target) => cardId == "S01-0016"
            ? Invoke(method, owner, target) : Invoke(method, cardId, owner, target);
        Assert.True(Eligible(item));
        Assert.True(Invoke("IsPoolCounterResponseAtTiming", cardId, owner, item));
        var stateBefore = game.SerializeFullState();
        Assert.True(Invoke("CanMasterCardPoolRespondAtTiming", owner, item, false));
        Assert.Equal(stateBefore, game.SerializeFullState());
        var legal = (IReadOnlyList<string>)typeof(L12GameEngine).GetMethod("LegalResponseSources",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, [owner, item])!;
        Assert.Contains("family-response", legal);
        var ownEffect = new L12StackItem
        {
            StackItemId = "own-effect", Controller = owner, SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId, SourceName = source.Name, Trigger = trigger, Text = item.Text,
        };
        Assert.False(Eligible(ownEffect));
    }

    [Theory]
    [InlineData("authority-event")] [InlineData("composite-continuation")]
    [InlineData("disaster")] [InlineData("authority-disaster")]
    public void RuleCarriersAndDisastersNeverMasqueradeAsCardEffectActivations(string trigger)
    {
        var game = Create(0);
        SetAmbush(game, 1);
        game.State.Players[1].Hand.Add(Card("S01-0003", "discard", 1));
        var source = game.State.Players[0].Field[0][0]!;
        var carrier = new L12StackItem
        {
            StackItemId = "carrier", Controller = 0, SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId, SourceName = source.Name, Trigger = trigger, Text = "规则载体",
        };
        Assert.False((bool)typeof(L12GameEngine).GetMethod("IsRespondableCardEffectActivation",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, [carrier])!);
        foreach (var cardId in new[] { "S01-0019", "S01-0016", "S02-0106" })
            Assert.False((bool)typeof(L12GameEngine).GetMethod("IsPoolCounterResponseAtTiming",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, [cardId, 1, carrier])!);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    [Trait("L12Evidence", "ruling:no-empty-response-window")]
    public void NoPossibleResponseSkipsTheWindowWithoutAnyPlayerOperation(int attacker)
    {
        var game = Create(attacker, conceal: true, autoPass: true);
        Assert.True(game.Handle(attacker, new L12Command("attack", $"legion-{attacker}",
            Target: new L12AttackTarget("legion", $"legion-{1 - attacker}"))).Accepted);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
        Assert.Null(game.State.ResponseWindow);
    }

    [Theory]
    [InlineData("S01-0016")] [InlineData("S01-0019")] [InlineData("S02-0106")]
    public void NestedDisastersAndUnrespondableClausesNeverCreateGenericEffectEligibility(string cardId)
    {
        var game = Create(0);
        SetAmbush(game, 1);
        game.State.Players[1].Hand.Add(Card("S01-0003", "discard", 1));
        var source = game.State.Players[0].Field[0][0]!;
        var disaster = new L12StackItem
        {
            StackItemId = "disaster-root", Controller = 0, SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId, SourceName = source.Name, Trigger = "disaster", Text = "天灾",
        };
        var wrapper = new L12StackItem
        {
            StackItemId = "wrapper", Controller = 0, SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId, SourceName = source.Name, Trigger = "reaction", Text = "嵌套载体",
        };
        wrapper.Targets.Add(disaster.StackItemId);
        game.State.EffectStack.AddRange([disaster, wrapper]);
        bool Eligible(L12StackItem target) => (bool)typeof(L12GameEngine)
            .GetMethod("IsPoolCounterResponseAtTiming", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(game, [cardId, 1, target])!;
        Assert.False(Eligible(wrapper));
        wrapper.Targets.Clear();
        wrapper.Data["unrespondable"] = "true";
        Assert.False(Eligible(wrapper));
    }

    [Theory]
    [InlineData(0, "S01-0016")] [InlineData(1, "S01-0016")]
    [InlineData(0, "S01-0019")] [InlineData(1, "S01-0019")]
    [InlineData(0, "S02-0106")] [InlineData(1, "S02-0106")]
    public void AllThreeAttackOrEffectCardsKeepTheIndependentOpponentAttackEntrance(int attacker, string cardId)
    {
        var game = Create(attacker);
        var card = Card(cardId, "raw-response", 1 - attacker);
        card.Hidden = true;
        game.State.Players[1 - attacker].Field[1][2] = card;
        game.State.Players[1 - attacker].Hand.Add(Card("S01-0003", "raw-discard", 1 - attacker));
        Assert.True(game.Handle(attacker, new L12Command("attack", $"legion-{attacker}",
            Target: new L12AttackTarget("legion", $"legion-{1 - attacker}"))).Accepted);
        Resolve(game, "pass");
        Assert.Contains("raw-response", Assert.Single(game.State.PendingPrompts).ValidChoices);
        var attack = Assert.Single(game.State.EffectStack);
        Assert.True((bool)typeof(L12GameEngine).GetMethod("IsPoolCounterResponseAtTiming",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, [cardId, 1 - attacker, attack])!);
    }
}
