using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class StarterBatch3BRegressionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed)
    {
        var game = new L12GameEngine(Catalog, "starter-3b", "STARTER3B", seed, ["甲", "乙"], [0, 1],
            skipPreparation: true);
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
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.MoraleDeck.Clear();
            player.SpecialZones.Trials.Clear();
            player.SpecialZones.Runes = 0;
            player.UsedAbilities.Clear();
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId)
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
            EffectiveProfession = definition.Profession,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == args.Length);
        return method.Invoke(target, args);
    }

    private static void SetMaster(L12PlayerState player, string cardId)
    {
        typeof(L12PlayerState).GetProperty(nameof(L12PlayerState.MasterId))!.SetValue(player, cardId);
        typeof(L12PlayerState).GetProperty(nameof(L12PlayerState.MasterName))!
            .SetValue(player, Catalog.Cards[cardId].NameZh);
    }

    private static L12Prompt Prompt(L12GameEngine game) => Assert.Single(game.State.PendingPrompts);

    private static void Choose(L12GameEngine game, string choice)
    {
        var prompt = Prompt(game);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void ChooseMany(L12GameEngine game, params string[] choices)
    {
        var prompt = Prompt(game);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, CardInstanceIds: choices.ToList()));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game, int maximum = 24)
    {
        var count = 0;
        while (game.State.PendingPrompts.FirstOrDefault() is { Kind: "response" } prompt && count++ < maximum)
        {
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
        Assert.True(count < maximum, "响应窗口未在限定次数内结束");
    }

    private static void OrderPendingTriggers(L12GameEngine game)
    {
        if (game.State.PendingPrompts.FirstOrDefault() is not { Kind: "trigger-order" } prompt) return;
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                CardInstanceIds: prompt.ValidChoices.ToList()));
        Assert.True(result.Accepted, result.Error);
    }

    private static void DeclinePendingOptionalTriggers(L12GameEngine game, int maximum = 24)
    {
        var count = 0;
        while (game.State.PendingPrompts.FirstOrDefault() is { } prompt && count++ < maximum)
        {
            if (prompt.Kind == "response")
            {
                var pass = game.Handle(prompt.PlayerIndex,
                    new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
                Assert.True(pass.Accepted, pass.Error);
            }
            else if (prompt.Kind == "trigger-order")
            {
                var order = game.Handle(prompt.PlayerIndex,
                    new L12Command("resolvePrompt", PromptId: prompt.PromptId,
                        CardInstanceIds: prompt.ValidChoices.ToList()));
                Assert.True(order.Accepted, order.Error);
            }
            else if (prompt.ValidChoices.Contains("mode:none"))
            {
                var decline = game.Handle(prompt.PlayerIndex,
                    new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "mode:none"));
                Assert.True(decline.Accepted, decline.Error);
            }
            else break;
        }
        Assert.True(count < maximum, "可选触发未在限定次数内结束");
    }

    private static void Queue(L12GameEngine game, int controller, L12CardInstance source, string trigger)
        => Invoke(game, "QueueOrPushTriggeredEffect", controller, source, trigger, $"【{trigger}】效果", null,
            new Dictionary<string, string>());

    private static void GiveMorale(L12PlayerState player, int count, string prefix = "morale")
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard { CardId = "S01-01C1", InstanceId = $"{prefix}-{index}" });
    }

    [Theory]
    [InlineData("ST01-C1", "S01-01C1")]
    [InlineData("ST02-C1", "S01-02C1")]
    [InlineData("ST03-C1", "S01-03C1")]
    [InlineData("ST04-C1", "S01-04C1")]
    [InlineData("ST05-C1", "S02-05C1A")]
    [InlineData("ST06-C1", "S02-06C1")]
    public void StarterMoraleCardsReuseTheCanonicalFactionAbilitySet(string starterId, string canonicalId)
    {
        var game = Create(20390);
        var method = typeof(L12GameEngine).GetMethod("GetAbilities",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var starter = Assert.IsAssignableFrom<IEnumerable<L12AbilityView>>(method.Invoke(game, [starterId]));
        var canonical = Assert.IsAssignableFrom<IEnumerable<L12AbilityView>>(method.Invoke(game, [canonicalId]));

        Assert.Equal(canonical.Select(ability => ability.Id), starter.Select(ability => ability.Id));
        Assert.NotEmpty(starter);
    }

    [Fact]
    public void RemainingStarterCardsShareStructuredAndVerifiedRuntimeBoundaries()
    {
        var expected = new (string CardId, string Trigger)[]
        {
            ("ST-DS02", "continuous"), ("ST01-M1", "morale-return"),
            ("ST02-07", "opponent-back-to-front"), ("ST02-10", "play"), ("ST02-M1", "active"),
            ("ST03-08", "continuous"), ("ST03-10", "play"), ("ST03-M1", "active"),
            ("ST04-02", "attack"), ("ST04-02", "death"), ("ST04-04", "enter"),
            ("ST04-05", "opponent-turn-lethal"), ("ST04-10", "continuous"), ("ST04-10", "play"),
            ("ST04-M1", "legion-attack-timing"), ("ST05-01", "promotion-enter"),
            ("ST05-10", "play"), ("ST05-M1", "active"),
            ("ST06-M1", "rune-spent"), ("ST06-M1", "active"),
            ("ST06-S1", "trial-complete"), ("ST06-S1", "active"),
        };

        foreach (var (cardId, trigger) in expected)
        {
            Assert.NotNull(L12VerifiedAtomicPrograms.Find(cardId, trigger));
            Assert.Contains(Catalog.AtomicEffects.Find(cardId)!.Abilities,
                ability => ability.Trigger == trigger && ability.MigrationStatus == "verified"
                    && !ability.HasLegacyFallback);
        }

        var nuada = Catalog.Cards["ST06-M1"];
        Assert.Equal(
            "我方消耗符文时，每消耗1符文，可选择我方1张【彼界】军团，本回合兵力+1000。\n" +
            "我方 回合1次 可消耗2符文：将我方最多2张士气转为活跃，试炼+2。",
            nuada.Effect);
        Assert.True(L12StructuredCardRules.TryGetStructuredAbilities("ST06-M1", out var nuadaAbilities));
        var nuadaActive = Assert.Single(nuadaAbilities, ability => ability.Trigger == "active");
        Assert.Contains(nuadaActive.Atoms, atom => atom.Kind == L12AtomKinds.AdvanceTrial
            && atom.Parameters.GetValueOrDefault("amount") == "2");
    }

    [Fact]
    public void LustDisasterAddsTroopsAndPrepaysAttackDiscard()
    {
        var game = Create(20401);
        var player = game.State.Players[0];
        var attacker = Card("ST01-01", "lust-attacker");
        player.Field[0][0] = attacker;
        game.State.ActiveDisaster = Card("ST-DS02", "lust-disaster");
        var cost = Card("ST01-03", "lust-cost");
        player.Hand.Add(cost);

        Invoke(game, "RecalculateContinuousTroops");
        Assert.Equal(attacker.BaseTroops + 1000, attacker.Troops);

        var start = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(start.Accepted, start.Error);
        Assert.False(attacker.Tapped);
        Assert.Null(game.State.PendingDefense);
        Assert.Equal("hand-card", Prompt(game).Kind);

        Choose(game, cost.InstanceId);
        Assert.Contains(cost, player.Graveyard);
        Assert.True(attacker.Tapped);
        Assert.NotNull(game.State.PendingDefense);
        Assert.True(game.State.Events.FindIndex(entry => entry.Type == "cost" && entry.Text.Contains("色欲之罪"))
            < game.State.Events.FindIndex(entry => entry.Type == "attack"));
    }

    [Fact]
    public void ChangeAndTombDefenderUseAuthoritativeResourceAndMovementEvents()
    {
        var changeGame = Create(20402);
        var changePlayer = changeGame.State.Players[0];
        SetMaster(changePlayer, "ST01-M1");
        changePlayer.MoraleDeck.Add(new L12MoraleCard { CardId = "S01-01C1", InstanceId = "change-added" });
        Invoke(changeGame, "RegisterReturnedMorale", changePlayer, 1);
        Invoke(changeGame, "FlushStarterResourceTriggerBatches");
        Choose(changeGame, "mode:use");
        PassResponses(changeGame);
        var added = Assert.Single(changePlayer.Morale);
        Assert.True(added.Tapped);

        var moveGame = Create(20403);
        var movedPlayer = moveGame.State.Players[0];
        var watcher = moveGame.State.Players[1];
        var moved = Card("ST01-01", "moved-front");
        var defender = Card("ST02-07", "tomb-defender");
        movedPlayer.Field[0][0] = moved;
        watcher.Field[0][0] = defender;
        Invoke(moveGame, "RecordLegionMovement", 0, moved, 1, 0);
        Assert.Equal(1, Prompt(moveGame).PlayerIndex);
        Choose(moveGame, "mode:use");
        PassResponses(moveGame);
        Assert.Equal(moved.BaseTroops - 3000, moved.Troops);
    }

    [Fact]
    public void RemainingTacticsUseDeclaredTargetsAndPersistentStatuses()
    {
        var reviveGame = Create(20404);
        var revivePlayer = reviveGame.State.Players[0];
        revivePlayer.TemporaryMorale = 20;
        var revive = Card("ST02-10", "desert-burial");
        var guard = Card("S01-0212", "revived-guard");
        revivePlayer.Hand.Add(revive);
        revivePlayer.Graveyard.Add(guard);
        Assert.True(reviveGame.Handle(0, new L12Command("playCard", revive.InstanceId)).Accepted);
        Choose(reviveGame, guard.InstanceId);
        Choose(reviveGame, "0:0");
        PassResponses(reviveGame);
        Assert.Same(guard, revivePlayer.Field[0][0]);
        Assert.False(guard.Tapped);
        Assert.Equal(reviveGame.State.TurnSerial, guard.DiscardAtEndOfTurnUntilTurn);
        Invoke(reviveGame, "ResolveS2DelayedEndTurnCards", 0);
        Assert.Contains(guard, revivePlayer.Graveyard);

        var bloodGame = Create(20405);
        var bloodPlayer = bloodGame.State.Players[0];
        bloodPlayer.TemporaryMorale = 20;
        var bloodline = Card("ST03-10", "bloodline");
        var asgard = Card("ST03-01", "blood-target");
        bloodPlayer.Hand.Add(bloodline);
        bloodPlayer.Field[0][0] = asgard;
        bloodPlayer.Graveyard.Add(Card("ST03-08", "triple-asgard"));
        Assert.True(bloodGame.Handle(0, new L12Command("playCard", bloodline.InstanceId)).Accepted);
        Choose(bloodGame, asgard.InstanceId);
        PassResponses(bloodGame);
        Assert.Equal(asgard.BaseTroops + 3000, asgard.Troops);
        Assert.Equal(2, bloodGame.State.Events.Count(entry =>
            (entry.Type is "stack-push" or "stack-deferred")
            && entry.Cards.Any(card => card.InstanceId == bloodline.InstanceId)));

        var fireGame = Create(20406);
        var firePlayer = fireGame.State.Players[0];
        firePlayer.TemporaryMorale = 20;
        var fire = Card("ST04-10", "invasion-fire");
        var takeda = Card("S01-0403", "takeda");
        var host = Card("ST04-03", "fire-host");
        firePlayer.Hand.Add(fire);
        firePlayer.Field[0][0] = takeda;
        firePlayer.Field[0][1] = host;
        Assert.Equal(-1, L12StructuredCardRules.HandPlayCostModifier(firePlayer, fire));
        Assert.True(fireGame.Handle(0, new L12Command("playCard", fire.InstanceId)).Accepted);
        Choose(fireGame, host.InstanceId);
        PassResponses(fireGame);
        Assert.Contains(fire, host.AttachedCards);

        var snapshot = JsonSerializer.SerializeToElement(fireGame.SnapshotFor(0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var hostView = snapshot.GetProperty("players")[0].GetProperty("field")[0]
            .EnumerateArray().Single(card => card.ValueKind != JsonValueKind.Null
                && card.GetProperty("instanceId").GetString() == host.InstanceId);
        Assert.Equal("强攻", Assert.Single(hostView.GetProperty("activeKeywords").EnumerateArray())
            .GetString());
        Assert.Contains(hostView.GetProperty("attachedCards").EnumerateArray(),
            card => card.GetProperty("cardId").GetString() == "ST04-10");

        var giftGame = Create(20407);
        var giftPlayer = giftGame.State.Players[0];
        giftPlayer.TemporaryMorale = 20;
        var gift = Card("ST05-10", "hunter-gift");
        var olympus = Card("ST05-01", "gift-target");
        giftPlayer.Hand.Add(gift);
        giftPlayer.Field[0][0] = olympus;
        Assert.True(giftGame.Handle(0, new L12Command("playCard", gift.InstanceId)).Accepted);
        Choose(giftGame, "mode:shock");
        Choose(giftGame, olympus.InstanceId);
        PassResponses(giftGame);
        Assert.Equal(2000, olympus.ShockDamageBonus);
        Assert.Equal(giftGame.State.TurnSerial, olympus.ShockDamageBonusUntilTurn);
    }

    [Fact]
    public void KaiWaivesPrintedMasterMoraleButHorusStillPrepaysFieldCosts()
    {
        var game = Create(20408);
        var player = game.State.Players[0];
        SetMaster(player, "ST02-M1");
        var kai = Card("ST04-04", "kai");
        player.Field[0][0] = kai;
        Queue(game, 0, kai, "enter");
        PassResponses(game);
        Assert.Equal(game.State.TurnSerial, player.MasterMoraleWaiverUntilTurn);

        var firstCost = Card("ST01-01", "horus-cost-1");
        var secondCost = Card("ST01-02", "horus-cost-2");
        var revive = Card("ST02-07", "horus-revive");
        player.Field[0][1] = firstCost;
        player.Field[0][2] = secondCost;
        player.Graveyard.Add(revive);

        var start = game.Handle(0, new L12Command("activateAbility", "master-0", Ability: "horusRevive"));
        Assert.True(start.Accepted, start.Error);
        ChooseMany(game, firstCost.InstanceId, secondCost.InstanceId);
        Choose(game, revive.InstanceId);
        Choose(game, "0:1");
        PassResponses(game);

        Assert.Empty(player.Morale);
        Assert.True(player.Graveyard.Contains(firstCost),
            $"grave={string.Join(',', player.Graveyard.Select(card => card.InstanceId))}; resolving={string.Join(',', player.Resolving.Select(card => card.InstanceId))}");
        Assert.True(player.Graveyard.Contains(secondCost),
            $"grave={string.Join(',', player.Graveyard.Select(card => card.InstanceId))}; resolving={string.Join(',', player.Resolving.Select(card => card.InstanceId))}");
        Assert.Same(revive, player.Field[0][1]);
        Assert.True(revive.Tapped);

        var triggerGame = Create(20423);
        var triggerPlayer = triggerGame.State.Players[0];
        SetMaster(triggerPlayer, "ST04-M1");
        var triggerKai = Card("ST04-04", "trigger-kai");
        var triggerAttacker = Card("ST04-03", "waived-kagutsuchi-attacker");
        triggerPlayer.Field[0][0] = triggerKai;
        triggerPlayer.Field[0][1] = triggerAttacker;
        Queue(triggerGame, 0, triggerKai, "enter");
        PassResponses(triggerGame);
        Assert.True(triggerGame.Handle(0, new L12Command("attack", triggerAttacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        Choose(triggerGame, "mode:morale");
        PassResponses(triggerGame);
        Assert.Empty(triggerPlayer.Morale);
        Assert.Equal(triggerAttacker.BaseTroops + 2000, triggerAttacker.Troops);
    }

    [Fact]
    public void KojiroDelaysOpponentHandIdentityAndKondoReplacesLethalEffect()
    {
        var game = Create(20409);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var kojiro = Card("ST04-02", "kojiro");
        var secret = Card("ST01-03", "kojiro-secret");
        player.Field[0][0] = kojiro;
        opponent.Hand.Add(secret);

        var attack = game.Handle(0, new L12Command("attack", kojiro.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        PassResponses(game);
        var discardPrompt = Prompt(game);
        Assert.Equal(1, discardPrompt.PlayerIndex);
        Assert.True(discardPrompt.IsPrivate);
        Assert.DoesNotContain(secret.InstanceId, JsonSerializer.Serialize(game.SnapshotFor(0)),
            StringComparison.Ordinal);
        Choose(game, secret.InstanceId);
        Assert.Contains(secret, opponent.Graveyard);

        var replacement = Create(20410);
        replacement.State.ActivePlayer = 1;
        var owner = replacement.State.Players[0];
        var protectedCard = Card("ST04-03", "protected-gaotianyuan");
        var kondo = Card("ST04-05", "kondo");
        owner.Field[0][0] = protectedCard;
        owner.Field[0][1] = kondo;
        var removed = (bool)Invoke(replacement, "RemoveFromField", owner, protectedCard, true,
            "被效果击杀", true, L12FieldLeaveKind.Defeat, false, false)!;
        Assert.False(removed);
        Choose(replacement, kondo.InstanceId);
        Assert.Same(protectedCard, owner.Field[0][0]);
        Assert.Null(owner.Field[0][1]);
        Assert.Contains(kondo, owner.Graveyard);
    }

    [Fact]
    public void KojiroDeathUsesPrintedTroopsAndMayKillTwoTargets()
    {
        var game = Create(20415);
        var player = game.State.Players[0];
        var opponent = game.State.Players[1];
        var kojiro = Card("ST04-02", "kojiro-death");
        var first = Card("ST02-07", "kojiro-low-1");
        var second = Card("ST02-07", "kojiro-low-2");
        first.Troops = 5000;
        second.Troops = 4000;
        player.Graveyard.Add(kojiro);
        opponent.Field[0][0] = first;
        opponent.Field[0][1] = second;

        Queue(game, 0, kojiro, "death");
        ChooseMany(game, first.InstanceId, second.InstanceId);
        PassResponses(game);

        Assert.Null(opponent.Field[0][0]);
        Assert.Null(opponent.Field[0][1]);
        Assert.Contains(first, opponent.Graveyard);
        Assert.Contains(second, opponent.Graveyard);
    }

    [Fact]
    public void KagutsuchiPaysBeforeStackAndBuffsTheAttackingLegion()
    {
        var game = Create(20411);
        var player = game.State.Players[0];
        SetMaster(player, "ST04-M1");
        var attacker = Card("ST04-03", "kagutsuchi-attacker");
        player.Field[0][0] = attacker;
        GiveMorale(player, 2, "kagutsuchi");
        player.Hand.Add(Card("ST01-02", "kagutsuchi-discard"));

        var attack = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        var choicePrompt = Prompt(game);
        Assert.Equal(["mode:morale", "mode:discard", "mode:none"], choicePrompt.ValidChoices);
        Assert.Equal("消耗2士气", choicePrompt.ChoiceLabels["mode:morale"]);
        Assert.Equal("弃置1张手牌", choicePrompt.ChoiceLabels["mode:discard"]);
        Assert.Equal("不发动", choicePrompt.ChoiceLabels["mode:none"]);
        Choose(game, "mode:morale");
        PassResponses(game);

        Assert.All(player.Morale, morale => Assert.True(morale.Tapped));
        Assert.Equal(attacker.BaseTroops + 2000, attacker.Troops);
        Assert.True(game.State.Events.FindIndex(entry => entry.Type == "cost" && entry.Text.Contains("火之迦具土"))
            < game.State.Events.FindIndex(entry => entry.Type == "stack-push"
                && entry.Cards.Any(card => card.CardId == "ST04-M1")));

        var defenseGame = Create(20416);
        var defender = defenseGame.State.Players[1];
        SetMaster(defender, "ST04-M1");
        var attackingLegion = Card("ST01-01", "defense-timing-attacker");
        attackingLegion.Troops = 7000;
        var defendedLegion = Card("ST04-03", "defense-timing-target");
        var cooperativeSupport = Card("ST04-07", "defense-timing-cooperative-support");
        defenseGame.State.Players[0].Field[0][0] = attackingLegion;
        defender.Field[0][0] = defendedLegion;
        defender.Field[1][2] = cooperativeSupport;
        GiveMorale(defender, 2, "defense-kagutsuchi");
        var defenseAttack = defenseGame.Handle(0, new L12Command("attack", attackingLegion.InstanceId,
            Target: new L12AttackTarget("legion", defendedLegion.InstanceId)));
        Assert.True(defenseAttack.Accepted, defenseAttack.Error);
        Assert.Equal(1, Prompt(defenseGame).PlayerIndex);
        Choose(defenseGame, "mode:morale");
        PassResponses(defenseGame);
        Assert.Equal(L12CombatStage.DefenseChoice, defenseGame.State.PendingDefense?.Stage);
        var supportResult = defenseGame.Handle(1, new L12Command("resolveDefense",
            CardInstanceIds: [cooperativeSupport.InstanceId]));
        Assert.True(supportResult.Accepted, supportResult.Error);
        PassResponses(defenseGame);
        Assert.Equal(defendedLegion.BaseTroops + 2000, defendedLegion.Troops);
        Assert.Contains(cooperativeSupport, defender.Graveyard);
    }

    [Fact]
    public void StrongAttackFromSelfTemporaryAndEveryAttachmentSourceAddsOnlyOneMasterDamage()
    {
        var game = Create(204061);
        var player = game.State.Players[0];
        var attacker = Card("ST01-01", "multi-source-strong-attacker");
        attacker.AttachedCards.Add(Card("ST04-10", "attached-invasion-fire"));
        attacker.AttachedCards.Add(Card("S02-06S2", "attached-kings-sword"));
        player.Field[0][0] = attacker;

        var attack = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        Assert.Equal(2, game.State.PendingDefense?.MasterDamage);

        Invoke(game, "GrantStrongAttack", attacker);
        Assert.True(attacker.HasStrongAttack);
        Assert.Equal(2, game.State.PendingDefense?.MasterDamage);
    }

    [Fact]
    public void RemainingMasterActivesPayCostsAndApplyTheirDeclaredResults()
    {
        var sifGame = Create(20417);
        var sifPlayer = sifGame.State.Players[0];
        SetMaster(sifPlayer, "ST03-M1");
        var oldTop = Card("ST01-01", "sif-old-top");
        var first = Card("ST03-01", "sif-first");
        var second = Card("ST03-02", "sif-second");
        var third = Card("ST03-03", "sif-third");
        sifPlayer.Library.Add(oldTop);
        sifPlayer.Graveyard.AddRange([first, second, third]);
        Assert.True(sifGame.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "sifCycle")).Accepted);
        ChooseMany(sifGame, third.InstanceId, first.InstanceId, second.InstanceId);
        PassResponses(sifGame);
        Assert.Contains(oldTop, sifPlayer.Hand);
        Assert.Equal([third.InstanceId, first.InstanceId, second.InstanceId],
            sifPlayer.Library.Select(card => card.InstanceId));

        var athenaGame = Create(20418);
        var athenaPlayer = athenaGame.State.Players[0];
        SetMaster(athenaPlayer, "ST05-M1");
        var discard = Card("ST01-01", "athena-discard");
        var olympusOne = Card("ST05-02", "athena-one");
        var olympusTwo = Card("ST05-03", "athena-two");
        var morale = new L12MoraleCard { CardId = "ST05-C1", InstanceId = "athena-morale" };
        athenaPlayer.Hand.Add(discard);
        athenaPlayer.Morale.Add(morale);
        athenaPlayer.Field[0][0] = olympusOne;
        athenaPlayer.Field[0][1] = olympusTwo;
        Assert.True(athenaGame.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "athenaFrontBuff")).Accepted);
        Choose(athenaGame, discard.InstanceId);
        Choose(athenaGame, morale.InstanceId);
        ChooseMany(athenaGame, olympusOne.InstanceId, olympusTwo.InstanceId);
        PassResponses(athenaGame);
        Assert.Contains(discard, athenaPlayer.Graveyard);
        Assert.True(morale.IsGodPower);
        Assert.Equal(olympusOne.BaseTroops + 1000, olympusOne.Troops);
        Assert.Equal(1, olympusOne.MasterAttackDamageBonus);
        Assert.Equal(olympusTwo.BaseTroops + 1000, olympusTwo.Troops);
        Assert.Equal(2, athenaGame.State.Events.Count(entry =>
            (entry.Type is "stack-push" or "stack-deferred")
            && entry.Cards.Any(card => card.CardId == "ST05-M1")));

        var nuadaGame = Create(20419);
        var nuadaPlayer = nuadaGame.State.Players[0];
        SetMaster(nuadaPlayer, "ST06-M1");
        nuadaPlayer.SpecialZones.Runes = 2;
        var nuadaTrial = Card("ST06-S1", "nuada-trial");
        nuadaPlayer.SpecialZones.Trials.Add(nuadaTrial);
        var restedOne = new L12MoraleCard { CardId = "ST06-C1", InstanceId = "nuada-rested-1", Tapped = true };
        var restedTwo = new L12MoraleCard { CardId = "ST06-C1", InstanceId = "nuada-rested-2", Tapped = true };
        nuadaPlayer.Morale.AddRange([restedOne, restedTwo]);
        Assert.True(nuadaGame.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "nuadaReadyMorale")).Accepted);
        ChooseMany(nuadaGame, restedOne.InstanceId, restedTwo.InstanceId);
        PassResponses(nuadaGame);
        DeclinePendingOptionalTriggers(nuadaGame);
        Assert.Equal(0, nuadaPlayer.SpecialZones.Runes);
        Assert.False(restedOne.Tapped);
        Assert.False(restedTwo.Tapped);
        Assert.Equal(2, nuadaTrial.TrialProgress);

        var skyGame = Create(20420);
        var skyPlayer = skyGame.State.Players[0];
        var sky = Card("ST06-S1", "sky-active");
        sky.TrialCompleted = true;
        skyPlayer.SpecialZones.Trials.Add(sky);
        Assert.True(skyGame.Handle(0,
            new L12Command("activateAbility", sky.InstanceId, Ability: "skyCityDiscount")).Accepted);
        PassResponses(skyGame);
        Assert.Equal(1, skyPlayer.NextOtherworldLegionEntryDiscount);
        var otherworld = Card("ST06-02", "sky-discounted-legion");
        skyPlayer.Hand.Add(otherworld);
        Assert.Equal(Math.Max(0, otherworld.Cost - 1),
            (int)Invoke(skyGame, "GetPlayCost", 0, otherworld, false, 0, 0)!);
    }

    [Fact]
    public void AeneasKeepsLibraryIdentityPrivateUntilResolutionAndThenShuffles()
    {
        var game = Create(20412);
        var player = game.State.Players[0];
        var aeneas = Card("ST05-01", "aeneas");
        var ranged = Card("ST05-03", "secret-ranged");
        var ordinary = Card("ST01-01", "secret-ordinary");
        player.Field[0][0] = aeneas;
        player.Library.AddRange([ranged, ordinary]);

        Queue(game, 0, aeneas, "promotion-enter");
        var declaration = Prompt(game);
        Assert.DoesNotContain(ranged.InstanceId, declaration.ValidChoices);
        Choose(game, "mode:use");
        PassResponses(game);
        var search = Prompt(game);
        Assert.True(search.IsPrivate);
        Assert.Contains(ranged.InstanceId, search.ValidChoices);
        Assert.DoesNotContain(ranged.InstanceId, JsonSerializer.Serialize(game.SnapshotFor(1)),
            StringComparison.Ordinal);
        ChooseMany(game, ranged.InstanceId);
        Choose(game, "0:1");

        Assert.Same(ranged, player.Field[0][1]);
        Assert.DoesNotContain(ranged, player.Library);
        Assert.Contains(game.State.Events, entry => entry.Text.Contains("埃涅阿斯晋升登场检索结算"));
        Assert.Equal(2, game.State.Events.Count(entry =>
            (entry.Type is "stack-push" or "stack-deferred")
            && entry.Cards.Any(card => card.InstanceId == aeneas.InstanceId)));
    }

    [Fact]
    public void NuadaMayPayForZeroTargetsBecauseThePrintedMaximumIncludesZero()
    {
        var game = Create(20421);
        var player = game.State.Players[0];
        SetMaster(player, "ST06-M1");
        player.SpecialZones.Runes = 2;
        var trial = Card("ST06-S1", "nuada-zero-target-trial");
        player.SpecialZones.Trials.Add(trial);

        var result = game.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "nuadaReadyMorale"));
        Assert.True(result.Accepted, result.Error);
        PassResponses(game);
        DeclinePendingOptionalTriggers(game);

        Assert.Equal(0, player.SpecialZones.Runes);
        Assert.Equal(2, trial.TrialProgress);
        Assert.Contains(game.State.Events, entry => entry.Type == "effect"
            && entry.Text.Contains("0张士气"));
    }

    [Fact]
    public void GraveWarriorMayRepresentThreeAsgardLegionsForExistingCardPoolCosts()
    {
        var game = Create(20422);
        var player = game.State.Players[0];
        var gram = Card("S01-0317", "gram");
        var warrior = Card("ST03-08", "grave-warrior");
        var companion = Card("ST03-01", "grave-companion");
        player.Relic = gram;
        player.Graveyard.AddRange([warrior, companion]);
        var opponentHp = game.State.Players[1].Hp;

        var result = game.Handle(0,
            new L12Command("activateAbility", gram.InstanceId, Ability: "gramDamage"));
        Assert.True(result.Accepted, result.Error);
        ChooseMany(game, warrior.InstanceId, companion.InstanceId);
        PassResponses(game);

        Assert.True(gram.Tapped);
        Assert.Equal([warrior.InstanceId, companion.InstanceId],
            player.Library.Select(card => card.InstanceId));
        Assert.Equal(opponentHp - 1, game.State.Players[1].Hp);
    }

    [Fact]
    public void NuadaRuneSpendAndSkyCityCompletionUseIndependentDeclaredEffects()
    {
        var nuadaGame = Create(20413);
        var nuadaPlayer = nuadaGame.State.Players[1];
        SetMaster(nuadaPlayer, "ST06-M1");
        var target = Card("ST06-02", "nuada-target");
        nuadaPlayer.Field[0][0] = target;
        nuadaPlayer.SpecialZones.Runes = 2;
        Assert.Equal(0, nuadaGame.State.ActivePlayer);
        Assert.True(L12S2ZoneOps.SpendRunes(nuadaPlayer, 2));
        Invoke(nuadaGame, "FlushStarterResourceTriggerBatches");
        OrderPendingTriggers(nuadaGame);
        for (var index = 0; index < 2; index++)
        {
            Choose(nuadaGame, "mode:use");
            Choose(nuadaGame, target.InstanceId);
            PassResponses(nuadaGame);
        }
        // 两次符文消费触发各 +1000；目标同时位于对方回合的前排，持续获得 +1000。
        Assert.Equal(target.BaseTroops + 3000, target.Troops);

        var skyGame = Create(20414);
        var skyPlayer = skyGame.State.Players[0];
        var sky = Card("ST06-S1", "sky-city");
        sky.TrialCompleted = true;
        skyPlayer.SpecialZones.Trials.Add(sky);
        skyPlayer.Hp = Math.Max(1, skyPlayer.MaxHp - 2);
        skyPlayer.Library.Add(Card("ST01-01", "sky-draw"));
        Invoke(skyGame, "QueueCompletedTrialTriggerBatch", 0, sky);
        Choose(skyGame, "mode:use");
        Choose(skyGame, "mode:use");
        Choose(skyGame, "mode:use");
        PassResponses(skyGame);

        Assert.Equal(2, skyPlayer.SpecialZones.Runes);
        Assert.Equal(skyPlayer.MaxHp, skyPlayer.Hp);
        Assert.Single(skyPlayer.Hand);
        Assert.Equal(3, skyGame.State.Events.Count(entry => entry.Type is "stack-push" or "stack-deferred"
            && entry.Cards.Any(card => card.InstanceId == sky.InstanceId)));
    }
}
