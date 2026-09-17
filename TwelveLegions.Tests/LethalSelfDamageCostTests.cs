using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class LethalSelfDamageCostTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> DiscountCases() => Catalog.Cards.Values
        .Where(card => L12StructuredCardRules.SelfDamageEntryDiscount(card.Id) is not null)
        .SelectMany(card => new[] { new object[] { card.Id, false }, new object[] { card.Id, true } });

    [Theory]
    [MemberData(nameof(DiscountCases))]
    public void LastHealthCanPayPrintedEntryDiscountButDoesNotPlayTheLegion(string cardId, bool restore)
    {
        var game = Create();
        var card = Card(cardId, "lethal-play");
        game.State.Players[0].Hand.Add(card);
        Accept(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)));
        Assert.Contains("yes", Assert.Single(game.State.PendingPrompts).ValidChoices);
        if (restore) game = Restore(game);
        Choose(game, "yes");
        AssertTerminal(game);
        Assert.Contains(game.State.Players[0].Hand, entry => entry.InstanceId == card.InstanceId);
        Assert.All(game.State.Players[0].Field.SelectMany(row => row), Assert.Null);
    }

    [Theory]
    [InlineData("S01-03M1", "valkyrieRecover", false)]
    [InlineData("S01-03M1", "valkyrieRecover", true)]
    [InlineData("S01-03D1", "valhallaDiscount", false)]
    [InlineData("S01-03D1", "valhallaDiscount", true)]
    public void LastHealthCanPayMasterCostWithoutStackingItsEffect(string master, string ability, bool restore)
    {
        var game = Create(master);
        game.State.Players[0].Graveyard.AddRange([Card("S01-0311", "lethal-hand"), Card("S01-0312", "lethal-bottom")]);
        if (restore) game = Restore(game);
        Accept(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: ability)));
        if (ability == "valkyrieRecover")
        {
            Choose(game, ids: ["lethal-hand", "lethal-bottom"]);
            if (restore) game = Restore(game);
            Choose(game, "lethal-hand");
        }
        AssertTerminal(game);
        Assert.Equal(2, game.State.Players[0].Graveyard.Count);
        Assert.Empty(game.State.Players[0].Hand);
    }

    [Theory]
    [InlineData("S01-0313", false)]
    [InlineData("S01-0313", true)]
    [InlineData("S01-0316", false)]
    [InlineData("S01-0316", true)]
    public void LethalEntryCostDoesNotDrawMillOrLeaveDeclarations(string cardId, bool restore)
    {
        var game = Create();
        var player = game.State.Players[0];
        var card = Card(cardId, "lethal-enter");
        player.Hand.Add(card);
        var library = player.Library.Select(entry => entry.InstanceId).ToArray();
        Accept(game.Handle(0, new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)));
        Assert.Contains("mode:use", Assert.Single(game.State.PendingPrompts).ValidChoices);
        if (restore) game = Restore(game);
        Choose(game, "mode:use");
        AssertTerminal(game);
        Assert.Equal(library, game.State.Players[0].Library.Select(entry => entry.InstanceId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LastHealthCanPayBeowulfAttackCostWithoutContinuingCombat(bool restore)
    {
        var game = Create();
        var attacker = Card("S01-0301", "lethal-attacker");
        game.State.Players[0].Field[0][0] = attacker;
        var enemyHp = game.State.Players[1].Hp;
        Accept(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))));
        Assert.Contains("mode:use", Assert.Single(game.State.PendingPrompts).ValidChoices);
        if (restore) game = Restore(game);
        Choose(game, "mode:use");
        AssertTerminal(game);
        Assert.Equal(enemyHp, game.State.Players[1].Hp);
        Assert.Equal(attacker.BaseTroops, game.State.Players[0].Field[0][0]!.CurrentTroops);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LethalBjornCostStopsBeforeMovingGraveCardsOrReviving(bool restore)
    {
        var game = Create();
        var player = game.State.Players[0];
        var source = Card("S01-0305", "lethal-bjorn");
        player.Field[0][0] = source;
        var costs = Enumerable.Range(1, 4).Select(index => Card("S01-0001", $"lethal-grave-{index}")).ToArray();
        player.Graveyard.AddRange(costs);
        Accept(game.HandleGm(new L12GmCommand("destroyCard", 0, CardInstanceId: source.InstanceId)));
        Choose(game, ids: costs.Select(card => card.InstanceId).ToList());
        if (restore) game = Restore(game);
        Choose(game, "0:1");
        AssertTerminal(game);
        Assert.All(costs, cost => Assert.Contains(game.State.Players[0].Graveyard, card => card.InstanceId == cost.InstanceId));
        Assert.All(game.State.Players[0].Field.SelectMany(row => row), Assert.Null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChristinaLethalReplacementCostDoesNotPlayOrResolveTheTactic(bool restore)
    {
        var game = Create();
        var player = game.State.Players[0];
        var source = Card("ST03-05", "lethal-christina");
        var tactic = Card("S01-0008", "lethal-tactic");
        player.Field[0][0] = source;
        player.Hand.Add(tactic);
        Accept(game.Handle(0, new L12Command("activateAbility", source.InstanceId, Ability: "christinaFreeTactic")));
        Pass(game);
        if (restore) game = Restore(game);
        var library = game.State.Players[0].Library.Select(card => card.InstanceId).ToArray();
        Accept(game.Handle(0, new L12Command("playCard", tactic.InstanceId)));
        AssertTerminal(game);
        Assert.Contains(game.State.Players[0].Hand, card => card.InstanceId == tactic.InstanceId);
        Assert.Equal(library, game.State.Players[0].Library.Select(card => card.InstanceId));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BrunhildeAllowsLethalCostWithOrWithoutASigurd(bool withTarget, bool restore)
    {
        var game = Create();
        var player = game.State.Players[0];
        player.Hand.Add(Card("S01-0309", "lethal-brunhilde"));
        if (withTarget) player.Graveyard.Add(Card("S01-0310", "lethal-sigurd"));
        Accept(game.Handle(0, new L12Command("playCard", "lethal-brunhilde", Row: 0, Slot: 0)));
        Assert.Contains("mode:use", Assert.Single(game.State.PendingPrompts).ValidChoices);
        if (restore) game = Restore(game);
        Choose(game, "mode:use");
        if (withTarget)
        {
            Choose(game, "lethal-sigurd");
            if (restore) game = Restore(game);
            Choose(game, "0:1");
        }
        AssertTerminal(game);
        if (withTarget) Assert.Contains(game.State.Players[0].Graveyard, card => card.InstanceId == "lethal-sigurd");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PostColonLethalDamageAlsoStopsAlvidaSummonWithoutBecomingACost(bool restore)
    {
        var game = Create();
        var player = game.State.Players[0];
        player.Field[0][0] = Card("S01-0307", "lethal-alvida");
        player.Hand.Add(Card("S01-0303", "lethal-alvida-target"));
        Accept(game.Handle(0, new L12Command("activateAbility", "lethal-alvida", Ability: "alvidaSummon")));
        Choose(game, "lethal-alvida-target");
        Choose(game, "0:1");
        Assert.Equal(1, player.Hp); // 冒号后伤害，响应前不得提前支付。
        if (restore) game = Restore(game);
        Pass(game);
        AssertTerminal(game);
        Assert.Contains(game.State.Players[0].Hand, card => card.InstanceId == "lethal-alvida-target");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ValkyrieCallExplicitLowHealthExemptionDoesNotBecomeALethalCost(int hp)
    {
        var game = Create();
        var player = game.State.Players[0];
        player.Hp = hp;
        player.Hand.Add(Card("S01-0318", "cost-exempt-call"));
        player.Graveyard.Add(Card("S01-0311", "cost-exempt-target"));
        Accept(game.Handle(0, new L12Command("playCard", "cost-exempt-call")));
        Choose(game, "cost-exempt-target");
        Choose(game, "0:1");
        Pass(game);
        Assert.Null(game.State.Winner);
        Assert.Equal(hp, player.Hp);
        Assert.Equal("cost-exempt-target", player.Field[0][1]?.InstanceId);
    }

    [Theory]
    [InlineData("S01-03M1", "valkyrieRecover")]
    [InlineData("S01-03D1", "valhallaDiscount")]
    public void TwoHealthMasterCostStillStacksNormallyAndNegationDoesNotRefund(string master, string ability)
    {
        var game = Create(master);
        var player = game.State.Players[0];
        player.Hp = 2;
        player.Graveyard.AddRange([Card("S01-0311", "cost-hand"), Card("S01-0312", "cost-bottom")]);
        Accept(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: ability)));
        if (ability == "valkyrieRecover") { Choose(game, ids: ["cost-hand", "cost-bottom"]); Choose(game, "cost-hand"); }
        Assert.Null(game.State.Winner);
        Assert.Equal(1, player.Hp);
        Assert.Single(game.State.EffectStack).Negated = true;
        Pass(game);
        Assert.Equal(1, player.Hp);
        Assert.Equal(2, player.Graveyard.Count);
        Assert.Empty(game.State.PendingPrompts);
    }

    private static void AssertTerminal(L12GameEngine game)
    {
        Assert.Equal(0, game.State.Players[0].Hp);
        Assert.Equal(1, game.State.Winner);
        Assert.Equal(L12Phase.GameOver, game.State.Phase);
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Null(game.State.PendingDefense);
        Assert.Null(game.State.ResponseWindow);
        Assert.Single(game.State.Events, entry => entry.Type == "game-over");
        Assert.False(game.Handle(0, new L12Command("endTurn")).Accepted);
        var restored = Restore(game);
        Assert.Equal(L12Phase.GameOver, restored.State.Phase);
        Assert.Empty(restored.State.PendingPrompts);
        Assert.Empty(restored.State.EffectStack);
    }

    private static L12GameEngine Create(string master = "S01-03M1")
    {
        var baseDeck = Catalog.DeckAt(3);
        var deck = new L12PresetDeckDefinition { Name = "lethal-cost", MasterId = master,
            CardIds = [.. baseDeck.CardIds], MoraleIds = [.. baseDeck.MoraleIds], SpecialIds = [.. baseDeck.SpecialIds] };
        var game = new L12GameEngine(Catalog, "lethal-cost", "LETHAL", 91841, ["甲", "乙"], [deck, baseDeck],
            skipPreparation: true, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0; game.State.FirstPlayer = 0; game.State.Round = 2; game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3]; player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Graveyard.Clear(); player.Morale.Clear(); player.Resolving.Clear();
            player.ExtraRelics.Clear(); player.Relic = null;
        }
        game.State.Players[0].Hp = 1;
        game.State.Players[0].Morale.AddRange(Enumerable.Range(0, 10).Select(index =>
            new L12MoraleCard { CardId = "S01-03C1", InstanceId = $"lethal-morale-{index}" }));
        return game;
    }

    private static void Pass(L12GameEngine game)
    {
        for (var count = 0; game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; count++)
        {
            Assert.True(count < 20);
            Choose(game, "pass");
        }
    }

    private static void Choose(L12GameEngine game, string? choice = null, List<string>? ids = null)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        Accept(game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: choice, CardInstanceIds: ids)));
    }

    private static void Accept(CommandResult result) => Assert.True(result.Accepted, result.Error);
    private static L12GameEngine Restore(L12GameEngine game) => L12GameEngine.RestoreCheckpoint(Catalog,
        game.SerializeFullState(), game.RandomState!.Value, game.CardFactSignalSequence,
        autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
    private static L12CardInstance Card(string cardId, string id)
    {
        var card = Catalog.Cards[cardId];
        return new L12CardInstance { InstanceId = id, CardId = card.Id, Name = card.NameZh,
            CardType = card.CardType, Faction = card.Faction, Cost = card.Cost ?? 0,
            EffectText = card.Effect, Traits = [.. card.Traits], ImageUrl = card.ImageUrl,
            BaseTroops = card.Troops ?? 0, Troops = card.Troops ?? 0, SummonRound = -1,
            DisasterLevel = card.DisasterLevel ?? 0 };
    }
}
