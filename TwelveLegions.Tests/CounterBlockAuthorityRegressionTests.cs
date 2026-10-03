using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CounterBlockAuthorityRegressionTests
{
    private const string MercenaryAbilityId = "S01-0002:ability:reaction:a472c4e7c34abf4b";
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    [L12AbilityEvidence(MercenaryAbilityId, "richard-extra-discard-unpaid", "paid-cost-not-refunded")]
    public void RichardUnpaidExtraDiscardInvalidatesMercenaryBlockAndAttackContinues()
    {
        var game = Create(72540);
        var richard = Card("S02-0608", "effect-block-richard", 0, 9000);
        var target = Card("S01-0102", "effect-block-richard-target", 1, 1000);
        var mercenary = Card("S01-0002", "effect-block-richard-mercenary", 1);
        richard.SummonRound = target.SummonRound = -1;
        game.State.Players[0].Field[0][0] = richard;
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[1].Hand.Add(mercenary);

        var result = game.Handle(0, new L12Command("attack", richard.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId)));
        Assert.True(result.Accepted, result.Error);
        ResolveRichardAttackOrder(game);
        var mercenaryPrompt = FindPrompt(game,
            prompt => prompt.Kind == "response" && prompt.ValidChoices.Contains(mercenary.InstanceId),
            "理查进攻未进入佣兵部队响应窗口");
        result = game.Handle(1, new L12Command("resolvePrompt", PromptId: mercenaryPrompt.PromptId,
            Choice: mercenary.InstanceId));
        Assert.True(result.Accepted, result.Error);
        PassAll(game);

        Assert.Null(FindField(game.State.Players[1], target.InstanceId));
        Assert.Single(game.State.Players[1].Graveyard,
            card => card.InstanceId == mercenary.InstanceId);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("额外弃牌费用", StringComparison.Ordinal));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "defense"
            && entry.PlayerCombat?.OutcomeCode == "blocked"
            && entry.Cards.Any(card => card.InstanceId == mercenary.InstanceId));
    }

    [Fact]
    [L12AbilityEvidence(MercenaryAbilityId, "richard-extra-discard-paid", "paid-cost-not-refunded")]
    public void RichardPaidExtraDiscardKeepsMercenaryBlockValid()
    {
        var game = Create(72541);
        var richard = Card("S02-0608", "effect-block-richard-paid", 0, 9000);
        var target = Card("S01-0102", "effect-block-richard-paid-target", 1, 1000);
        var mercenary = Card("S01-0002", "effect-block-richard-paid-mercenary", 1);
        var extra = Card("S01-0001", "effect-block-richard-paid-extra", 1);
        richard.SummonRound = target.SummonRound = -1;
        game.State.Players[0].Field[0][0] = richard;
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[1].Hand.AddRange([mercenary, extra]);

        Assert.True(game.Handle(0, new L12Command("attack", richard.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        ResolveRichardAttackOrder(game);
        var mercenaryPrompt = FindPrompt(game,
            prompt => prompt.Kind == "response" && prompt.ValidChoices.Contains(mercenary.InstanceId),
            "理查进攻未进入佣兵部队响应窗口");
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: mercenaryPrompt.PromptId,
            Choice: mercenary.InstanceId)).Accepted);
        var extraPrompt = FindPrompt(game,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-richard-defense-extra-discard",
            "佣兵部队抵挡未进入理查额外弃牌结算");
        Assert.Contains(extra.InstanceId, extraPrompt.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: extraPrompt.PromptId,
            Choice: extra.InstanceId)).Accepted);
        PassAll(game);

        Assert.NotNull(FindField(game.State.Players[1], target.InstanceId));
        Assert.Single(game.State.Players[1].Graveyard, card => card.InstanceId == mercenary.InstanceId);
        Assert.Single(game.State.Players[1].Graveyard, card => card.InstanceId == extra.InstanceId);
        Assert.Contains(game.State.Events, entry => entry.Type == "defense"
            && entry.PlayerCombat?.OutcomeCode == "blocked"
            && entry.Cards.Any(card => card.InstanceId == mercenary.InstanceId));
    }

    [Fact]
    [L12AbilityEvidence(MercenaryAbilityId, "reconnect", "stale-combat-binding")]
    public void RestoredEffectBlockAuthorityDoesNotBindAChangedCombatIdentity()
    {
        var game = Create(72542);
        var attacker = Card("S01-0001", "effect-block-stale-attacker", 0, 9000);
        var target = Card("S01-0102", "effect-block-stale-target", 1, 1000);
        var mercenary = Card("S01-0002", "effect-block-stale-mercenary", 1);
        attacker.SummonRound = target.SummonRound = -1;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[1].Hand.Add(mercenary);

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        var mercenaryPrompt = FindPrompt(game,
            prompt => prompt.Kind == "response" && prompt.ValidChoices.Contains(mercenary.InstanceId),
            "未进入佣兵部队响应窗口");
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: mercenaryPrompt.PromptId,
            Choice: mercenary.InstanceId)).Accepted);
        _ = FindPrompt(game, prompt => game.State.EffectStack.Any(item => item.StackItemId == prompt.StackItemId
                && item.Trigger == "authority-event" && item.Data.GetValueOrDefault("effectBlock") == "true"),
            "佣兵部队抵挡未建立效果抵挡权威事件");
        var authority = Assert.Single(game.State.EffectStack,
            item => item.Trigger == "authority-event" && item.Data.GetValueOrDefault("effectBlock") == "true");
        Assert.False(string.IsNullOrWhiteSpace(authority.Data["effectBlockCombatId"]));
        Assert.False(string.IsNullOrWhiteSpace(authority.Data["effectBlockAttackStackItemId"]));

        game = Restore(game);
        Assert.NotNull(game.State.PendingDefense);
        game.State.PendingDefense!.CombatId = "combat-replaced-after-restore";
        PassAll(game);

        Assert.Null(FindField(game.State.Players[1], target.InstanceId));
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "defense"
            && entry.PlayerCombat?.OutcomeCode == "blocked"
            && entry.Cards.Any(card => card.InstanceId == mercenary.InstanceId));
    }

    [Fact]
    [L12AbilityEvidence(MercenaryAbilityId, "attacker-left-before-authority", "context-invalidated")]
    public void EffectBlockAuthorityDoesNotCommitAfterItsBoundAttackerLeavesTheBattlefield()
    {
        var game = Create(72545);
        var attacker = Card("S01-0001", "effect-block-leaving-attacker", 0, 9000);
        var target = Card("S01-0102", "effect-block-leaving-target", 1, 1000);
        var mercenary = Card("S01-0002", "effect-block-leaving-mercenary", 1);
        attacker.SummonRound = target.SummonRound = -1;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[1].Hand.Add(mercenary);

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        var mercenaryPrompt = FindPrompt(game,
            prompt => prompt.Kind == "response" && prompt.ValidChoices.Contains(mercenary.InstanceId),
            "未进入佣兵部队响应窗口");
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: mercenaryPrompt.PromptId,
            Choice: mercenary.InstanceId)).Accepted);
        _ = FindPrompt(game, prompt => game.State.EffectStack.Any(item => item.StackItemId == prompt.StackItemId
                && item.Trigger == "authority-event" && item.Data.GetValueOrDefault("effectBlock") == "true"),
            "佣兵部队抵挡未建立效果抵挡权威事件");

        game.State.Players[0].Field[0][0] = null;
        PassAll(game);

        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "defense"
            && entry.PlayerCombat?.OutcomeCode == "blocked"
            && entry.Cards.Any(card => card.InstanceId == mercenary.InstanceId));
        Assert.Contains(game.State.Events, entry => entry.Type is "defense-invalid" or "attack-aborted");
    }

    [Fact]
    public void LandlordsCoercionSeesAbsoluteDefenseAsARealBlock()
    {
        var game = Create(72543);
        var attacker = Card("S01-0001", "absolute-landlord-attacker", 0, 3000);
        var landlord = Covered("S02-0015", "absolute-landlord", 0);
        var absolute = Covered("S01-0016", "absolute-defense-source", 1);
        var absoluteCost = Card("S01-0001", "absolute-defense-cost", 1);
        attacker.SummonRound = -1;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[0].Field[1][0] = landlord;
        game.State.Players[1].Field[1][0] = absolute;
        game.State.Players[1].Hand.Add(absoluteCost);
        var hpBefore = game.State.Players[1].Hp;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        var absolutePrompt = FindPrompt(game,
            prompt => prompt.Kind == "response" && prompt.ValidChoices.Contains(absolute.InstanceId),
            "绝对防御未取得对方进攻响应窗口");
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: absolutePrompt.PromptId,
            Choice: absolute.InstanceId)).Accepted);
        var costPrompt = FindPrompt(game,
            prompt => prompt.ValidChoices.Contains(absoluteCost.InstanceId), "绝对防御未要求弃置手牌费用");
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: costPrompt.PromptId,
            Choice: absoluteCost.InstanceId)).Accepted);
        var landlordPrompt = FindPrompt(game,
            prompt => prompt.Kind == "response" && prompt.ValidChoices.Contains(landlord.InstanceId),
            "绝对防御抵挡未建立地主响应时点");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: landlordPrompt.PromptId,
            Choice: landlord.InstanceId)).Accepted);
        var discardPrompt = FindPrompt(game,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-landlord-extra-discard",
            "地主未进入额外弃牌结算");
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: discardPrompt.PromptId,
            Choice: "decline")).Accepted);
        PassAll(game);

        Assert.Equal(hpBefore - 1, game.State.Players[1].Hp);
        Assert.Contains(absolute, game.State.Players[1].Graveyard);
        Assert.Contains(absoluteCost, game.State.Players[1].Graveyard);
    }

    [Fact]
    public void LandlordsCoercionSeesEmptyCityAsARealBlock()
    {
        var game = Create(72544);
        var attacker = Card("S01-0001", "empty-city-landlord-attacker", 0, 3000);
        var landlord = Covered("S02-0015", "empty-city-landlord", 0);
        var emptyCity = Covered("S01-0120", "empty-city-source", 1);
        attacker.SummonRound = -1;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[0].Field[1][0] = landlord;
        game.State.Players[1].Field[1][0] = emptyCity;
        var morale = AddMorale(game.State.Players[1]);
        var hpBefore = game.State.Players[1].Hp;

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        var emptyCityPrompt = FindPrompt(game,
            prompt => prompt.Kind == "response" && prompt.ValidChoices.Contains(emptyCity.InstanceId),
            "空城计未取得对方进攻响应窗口");
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: emptyCityPrompt.PromptId,
            Choice: emptyCity.InstanceId)).Accepted);
        var costPrompt = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(morale.InstanceId, costPrompt.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: costPrompt.PromptId,
            Choice: morale.InstanceId)).Accepted);
        var drawMode = Assert.Single(game.State.PendingPrompts);
        Assert.Contains("mode:none", drawMode.ValidChoices);
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: drawMode.PromptId,
            Choice: "mode:none")).Accepted);
        var landlordPrompt = FindPrompt(game,
            prompt => prompt.Kind == "response" && prompt.ValidChoices.Contains(landlord.InstanceId),
            "空城计抵挡未建立地主响应时点");
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: landlordPrompt.PromptId,
            Choice: landlord.InstanceId)).Accepted);
        var discardPrompt = FindPrompt(game,
            prompt => prompt.Data.GetValueOrDefault("action") == "s2-landlord-extra-discard",
            "地主未进入额外弃牌结算");
        Assert.True(game.Handle(1, new L12Command("resolvePrompt", PromptId: discardPrompt.PromptId,
            Choice: "decline")).Accepted);
        PassAll(game);

        Assert.Equal(hpBefore - 1, game.State.Players[1].Hp);
        Assert.Contains(emptyCity, game.State.Players[1].Graveyard);
    }

    private static void ResolveRichardAttackOrder(L12GameEngine game)
    {
        var order = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("trigger-order", order.Kind);
        var defense = order.ValidChoices.Single(id => order.Data[id].Contains("抵挡", StringComparison.Ordinal));
        var remaining = order.ValidChoices.Where(id => id != defense).ToArray();
        var result = game.Handle(order.PlayerIndex, new L12Command("resolvePrompt", PromptId: order.PromptId,
            CardInstanceIds: [defense, .. remaining]));
        Assert.True(result.Accepted, result.Error);
    }

    private static L12Prompt FindPrompt(L12GameEngine game, Func<L12Prompt, bool> predicate, string failure)
    {
        for (var count = 0; count < 150; count++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is not { } prompt) break;
            if (predicate(prompt)) return prompt;
            ResolveDefault(game, prompt);
        }
        throw new Xunit.Sdk.XunitException(failure);
    }

    private static void PassAll(L12GameEngine game)
    {
        for (var count = 0; count < 200; count++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                ResolveDefault(game, prompt);
                continue;
            }
            if (game.State.PendingDefense is not null
                && game.State.PendingDefense.Stage == L12CombatStage.DefenseChoice)
            {
                var result = game.Handle(1, new L12Command("resolveDefense", CardInstanceIds: []));
                Assert.True(result.Accepted, result.Error);
                continue;
            }
            if (game.State.EffectStack.Count == 0) break;
        }
        Assert.Empty(game.State.PendingPrompts);
        Assert.Empty(game.State.EffectStack);
        Assert.Empty(game.State.DeferredEffectStack);
        Assert.Empty(game.State.PendingActivations);
        Assert.Empty(game.State.PendingTriggerBatches);
        Assert.Empty(game.State.PendingTriggerStackCandidates);
        Assert.Null(game.State.PendingDefense);
    }

    private static void ResolveDefault(L12GameEngine game, L12Prompt prompt)
    {
        var choice = prompt.Kind == "response" ? "pass"
            : prompt.ValidChoices.Contains("no") ? "no"
            : prompt.ValidChoices.Contains("skip") ? "skip"
            : prompt.ValidChoices.Contains("mode:none") ? "mode:none"
            : prompt.ValidChoices.First();
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "counter-block-authority", "COUNTER-BLOCK", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2);
        game.State.ActiveDisaster = null;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
        }
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.Phase = L12Phase.Main;
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(),
            game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0), game.CardFactSignalSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);

    private static L12MoraleCard AddMorale(L12PlayerState player)
    {
        var morale = player.MoraleDeck[0];
        player.MoraleDeck.RemoveAt(0);
        morale.Tapped = false;
        player.Morale.Add(morale);
        return morale;
    }

    private static L12CardInstance Covered(string cardId, string instanceId, int ownerIndex)
    {
        var card = Card(cardId, instanceId, ownerIndex);
        card.Hidden = true;
        card.SetRound = 0;
        return card;
    }

    private static L12CardInstance? FindField(L12PlayerState player, string instanceId)
        => player.Field.SelectMany(row => row).FirstOrDefault(card => card?.InstanceId == instanceId);

    private static L12CardInstance Card(string cardId, string instanceId, int ownerIndex, int? troops = null)
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
            HasPrintedCost = definition.Cost is not null,
            EffectText = definition.Effect,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            BaseTroops = troops ?? definition.Troops ?? 0,
            Troops = troops ?? definition.Troops ?? 0,
            OwnerIndex = ownerIndex,
        };
    }
}
