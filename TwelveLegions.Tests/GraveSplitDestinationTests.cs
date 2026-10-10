using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class GraveSplitDestinationTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    public static IEnumerable<object[]> ZoneCases()
    {
        foreach (var blood in new[] { false, true })
        foreach (var missing in new[] { 0, 1, 2, 3 })
        foreach (var restore in new[] { false, true })
            yield return [blood, missing, restore];
    }

    [Theory]
    [MemberData(nameof(ZoneCases))]
    public void EachDeclaredDestinationRechecksItsOwnGraveObject(bool blood, int missing, bool restore)
    {
        var game = Declare(blood);
        var player = game.State.Players[0];
        foreach (var (id, bit) in new[] { ("split-hand", 1), ("split-bottom", 2) })
        {
            if ((missing & bit) == 0) continue;
            var card = player.Graveyard.Single(card => card.InstanceId == id);
            player.Graveyard.Remove(card);
            player.Removed.Add(card);
        }
        if (restore) game = Restore(game);
        SettleAndCheckReplay(game);
        AssertDestinations(game, missing, invalidInGrave: false);
        if (!blood) AssertPaid(game);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void BloodEagleRechecksEachFactionAfterRingLeavesDuringItsOwnResponse(int invalid, bool restore)
    {
        var game = Declare(blood: true, universalMask: invalid);
        game.State.Players[0].ExtraRelics.Clear();
        if (restore) game = Restore(game);
        SettleAndCheckReplay(game);
        AssertDestinations(game, invalid, invalidInGrave: true);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NegationMovesNeitherObjectAndDoesNotRefundPaidCost(bool blood, bool restore)
    {
        var game = Declare(blood);
        if (restore) game = Restore(game);
        var item = game.State.EffectStack.Single(item => blood
            ? item.Data.GetValueOrDefault("atomicFlow") == "blood-eagle-recover"
            : item.SourceCardId == "S01-03M1");
        item.Negated = true;
        SettleAndCheckReplay(game);
        AssertDestinations(game, 3, invalidInGrave: true, negated: true);
        if (!blood) AssertPaid(game);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void CurrentSpecialZoneRestrictionIsRecheckedPerDestination(bool blood, int invalid)
    {
        var game = Declare(blood);
        var target = game.State.Players[0].Graveyard.Single(card =>
            card.InstanceId == (invalid == 1 ? "split-hand" : "split-bottom"));
        // 权威状态替身：所选对象在响应后具有禁止进入手牌/牌库的专属特征。
        // 验证区域规则复验，不冒充存在某张实际赋予此特征的卡。
        target.Traits.Add("杨戬专属");
        game = Restore(game);
        SettleAndCheckReplay(game);
        AssertDestinations(game, invalid, invalidInGrave: true);
        if (!blood) AssertPaid(game);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValkyrieCanCancelEitherDeclarationStepWithoutPaying(bool afterPair)
    {
        var game = Create();
        AddTargets(game, 0);
        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "valkyrieRecover")).Accepted);
        if (afterPair) Resolve(game, ids: ["split-hand", "split-bottom"]);
        game = Restore(game);
        Resolve(game, choice: "skip");
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        Assert.Equal(6, game.State.Players[0].Hp);
        Assert.False(Assert.Single(game.State.Players[0].Morale).Tapped);
        Assert.Equal(3, game.State.Players[0].Graveyard.Count);
        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "valkyrieRecover")).Accepted);
    }

    [Fact]
    public void ValkyrieRestoresBothSelectionStepsAndOnlyPaysAfterDestinationDeclaration()
    {
        var game = Create();
        AddTargets(game, 0);
        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "valkyrieRecover")).Accepted);
        game = Restore(game);
        Resolve(game, ids: ["split-hand", "split-bottom"]);
        Assert.Equal(6, game.State.Players[0].Hp);
        Assert.False(Assert.Single(game.State.Players[0].Morale).Tapped);
        game = Restore(game);
        var destination = Assert.Single(game.State.PendingPrompts);
        Assert.Equal(new[] { "split-bottom", "split-hand" }, destination.ValidChoices.Where(id => id != "skip").Order());
        Resolve(game, choice: "split-hand");
        SettleAndCheckReplay(game);
        AssertDestinations(game, 0, invalidInGrave: false);
        AssertPaid(game);
    }

    [Fact]
    public void FullPoolGravePairSplitInventoryIsExplicit()
    {
        var cards = Catalog.Cards.Values.Where(card => card.Effect is not null && card.Effect.Contains("墓地2张", StringComparison.Ordinal)
            && card.Effect.Contains("其中1张", StringComparison.Ordinal)
            && card.Effect.Contains("另1张", StringComparison.Ordinal)).Select(card => card.Id).Order();
        Assert.Equal(new[] { "S01-0320", "S01-03M1" }, cards);
    }

    private static L12GameEngine Declare(bool blood, int universalMask = 0)
    {
        var game = Create();
        var player = game.State.Players[0];
        AddTargets(game, universalMask);
        if (!blood)
        {
            Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "valkyrieRecover")).Accepted);
            Resolve(game, ids: ["split-hand", "split-bottom"]);
            Resolve(game, choice: "split-hand");
            AssertPaid(game);
            return game;
        }
        var counter = Card("S01-0320", "split-counter");
        counter.Hidden = true;
        counter.SetRound = 0;
        player.Field[1][0] = counter;
        var fallen = Card("S01-0309", "split-fallen");
        player.Field[0][0] = fallen;
        Assert.True(game.HandleGm(new L12GmCommand("destroyCard", 0, CardInstanceId: fallen.InstanceId)).Accepted);
        var order = Assert.Single(game.State.PendingPrompts);
        var bloodTrigger = order.ValidChoices.Single(id =>
            order.Data.GetValueOrDefault(id)?.Contains("复仇血鹰", StringComparison.Ordinal) == true);
        Resolve(game, ids: [.. order.ValidChoices.Where(id => id != "skip" && id != bloodTrigger), bloodTrigger]);
        for (var count = 0; game.State.PendingPrompts.Single().ValidChoices.Contains("mode:none"); count++)
        {
            Assert.True(count < 10);
            Resolve(game, choice: "mode:none");
        }
        Resolve(game, ids: ["split-hand", "split-bottom"]);
        for (var count = 0; !game.State.EffectStack.Any(item => item.Data.GetValueOrDefault("atomicFlow") == "blood-eagle-recover"); count++)
        {
            Assert.True(count < 20);
            Resolve(game, choice: game.State.PendingPrompts.Single().ValidChoices.Contains("no") ? "no" : "pass");
        }
        Assert.Equal("response", Assert.Single(game.State.PendingPrompts).Kind);
        return game;
    }

    private static void AddTargets(L12GameEngine game, int universalMask)
    {
        var player = game.State.Players[0];
        if (universalMask != 0) player.ExtraRelics.Add(Card("S02-0008", "split-ring"));
        player.Graveyard.AddRange([
            Card((universalMask & 1) != 0 ? "S01-0001" : "S01-0311", "split-hand"),
            Card((universalMask & 2) != 0 ? "S01-0002" : "S01-0312", "split-bottom"),
            Card("S01-0309", "split-unselected"),
        ]);
    }

    private static void SettleAndCheckReplay(L12GameEngine game)
    {
        var response = Assert.Single(game.State.PendingPrompts);
        var stale = new L12Command("resolvePrompt", PromptId: response.PromptId, Choice: "pass");
        for (var count = 0; game.State.PendingPrompts.Count > 0; count++)
        {
            Assert.True(count < 40);
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.Kind == "response")
                Resolve(game, choice: "pass");
            else if (prompt.Continuation == "pending-activation" && prompt.ValidChoices.Contains("mode:none"))
                Resolve(game, choice: "mode:none");
            else
                Assert.Fail($"未预期的后续弹框：{prompt.Kind}/{prompt.Continuation}");
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.EffectStack);
        var before = game.State.Players[0];
        var hand = before.Hand.Select(card => card.InstanceId).ToArray();
        var library = before.Library.Select(card => card.InstanceId).ToArray();
        var hp = before.Hp;
        game.Handle(response.PlayerIndex, stale);
        Assert.Equal(hand, before.Hand.Select(card => card.InstanceId));
        Assert.Equal(library, before.Library.Select(card => card.InstanceId));
        Assert.Equal(hp, before.Hp);
    }

    private static void AssertDestinations(L12GameEngine game, int invalid, bool invalidInGrave, bool negated = false)
    {
        var player = game.State.Players[0];
        Assert.Equal((invalid & 1) == 0, player.Hand.Any(card => card.InstanceId == "split-hand"));
        Assert.Equal((invalid & 2) == 0, player.Library.Last().InstanceId == "split-bottom");
        foreach (var (id, bit) in new[] { ("split-hand", 1), ("split-bottom", 2) })
        {
            if ((invalid & bit) != 0)
                Assert.Contains(invalidInGrave ? player.Graveyard : player.Removed, card => card.InstanceId == id);
            Assert.Single(player.Hand.Concat(player.Library).Concat(player.Graveyard).Concat(player.Removed), card => card.InstanceId == id);
        }
        Assert.Contains(player.Graveyard, card => card.InstanceId == "split-unselected");
        if (!negated && invalid == 3) Assert.Contains(game.State.Events, entry => entry.Type == "effect-failed");
        if (!negated && invalid is 1 or 2)
            Assert.Contains(game.State.Events, entry => entry.Text.Contains("其余对象继续结算", StringComparison.Ordinal));
    }

    private static void AssertPaid(L12GameEngine game)
    {
        Assert.Equal(5, game.State.Players[0].Hp);
        Assert.True(Assert.Single(game.State.Players[0].Morale).Tapped);
    }

    private static L12GameEngine Create()
    {
        var baseDeck = Catalog.DeckAt(3);
        var deck = new L12PresetDeckDefinition { Name = "split", MasterId = "S01-03M1",
            CardIds = [.. baseDeck.CardIds], MoraleIds = [.. baseDeck.MoraleIds], SpecialIds = [.. baseDeck.SpecialIds] };
        var game = new L12GameEngine(Catalog, "grave-split", "SPLIT", 91831, ["甲", "乙"], [deck, baseDeck],
            skipPreparation: true, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3]; player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear(); player.Graveyard.Clear(); player.Morale.Clear(); player.Resolving.Clear();
            player.ExtraRelics.Clear(); player.Relic = null; player.Hp = 6;
        }
        game.State.Players[0].Morale.Add(new L12MoraleCard { CardId = "S01-03C1", InstanceId = "split-morale" });
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game) => L12GameEngine.RestoreCheckpoint(Catalog,
        game.SerializeFullState(), game.RandomState!.Value, game.CardFactSignalSequence,
        autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static void Resolve(L12GameEngine game, string? choice = null, List<string>? ids = null)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: choice, CardInstanceIds: ids));
        Assert.True(result.Accepted);
    }

    private static L12CardInstance Card(string cardId, string id)
    {
        var card = Catalog.Cards[cardId];
        return new L12CardInstance { InstanceId = id, CardId = card.Id, Name = card.NameZh,
            CardType = card.CardType, Faction = card.Faction, Cost = card.Cost ?? 0,
            EffectText = card.Effect, Traits = [.. card.Traits], ImageUrl = card.ImageUrl,
            BaseTroops = card.Troops ?? 0, Troops = card.Troops ?? 0, SummonRound = -1 };
    }
}
