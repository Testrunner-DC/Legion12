using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class AtomicReviewBatch6KARegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static readonly IReadOnlyDictionary<string, int> AuditedAbilityCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0001"] = 2, ["S01-0002"] = 2, ["S01-0003"] = 2, ["S01-0004"] = 3,
            ["S01-0005"] = 1, ["S01-0006"] = 1, ["S01-0007"] = 1, ["S01-0008"] = 1,
            ["S01-0009"] = 1, ["S01-0010"] = 1, ["S01-0011"] = 1, ["S01-0012"] = 1,
            ["S01-0013"] = 1, ["S01-0014"] = 1, ["S01-0015"] = 1, ["S01-0016"] = 1,
            ["S01-0017"] = 1, ["S01-0018"] = 1, ["S01-0019"] = 1, ["S01-0020"] = 1,
            ["S01-0021"] = 1, ["S01-00C1"] = 1,
            ["S01-0101"] = 3, ["S01-0102"] = 2, ["S01-0103"] = 1, ["S01-0104"] = 2,
            ["S01-0105"] = 2, ["S01-0106"] = 2, ["S01-0107"] = 2, ["S01-0108"] = 2,
            ["S01-0109"] = 2, ["S01-0110"] = 3, ["S01-0111"] = 4, ["S01-0112"] = 3,
            ["S01-0113"] = 2, ["S01-0114"] = 3, ["S01-0115"] = 3, ["S01-0116"] = 2,
            ["S01-0117"] = 2, ["S01-0118"] = 1, ["S01-0119"] = 1, ["S01-0120"] = 1,
            ["S01-01C1"] = 2, ["S01-01D1"] = 2, ["S01-01M1"] = 4, ["S01-01M2"] = 1,
            ["S01-DS01"] = 1, ["S01-DS02"] = 3, ["S01-DS03"] = 3, ["S01-DS04"] = 2,
            ["S01-DS05"] = 1, ["S01-DS06"] = 1, ["S01-DS07"] = 1, ["S01-DS08"] = 1,
            ["S01-DS09"] = 1, ["S01-DS10"] = 1,
        };

    private static L12GameEngine Create(int seed, string? firstMaster = null, string? secondMaster = null)
    {
        var baseDeck = Catalog.DeckAt(0);
        var firstDeck = firstMaster is null ? baseDeck : new L12PresetDeckDefinition
        {
            Name = $"{firstMaster} 6K-A审计牌库",
            MasterId = firstMaster,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var secondDeck = secondMaster is null ? baseDeck : new L12PresetDeckDefinition
        {
            Name = $"{secondMaster} 6K-A审计牌库",
            MasterId = secondMaster,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "atomic-review-batch6ka", "ATOMIC6KA", seed,
            ["甲", "乙"], [firstDeck, secondDeck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        game.State.ActivePlayer = 0;
        game.State.FirstPlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 3;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Field[0] = new L12CardInstance?[3];
            player.Field[1] = new L12CardInstance?[3];
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Resolving.Clear();
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
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            DisasterLevel = definition.DisasterLevel ?? 0,
        };
    }

    private static void AddReadyMorale(L12PlayerState player, int count)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S01-01C1",
                InstanceId = $"batch6ka-morale-{player.PlayerIndex}-{index}",
            });
    }

    private static L12Prompt Resolve(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
        return prompt;
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 80 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            Resolve(game, "pass");
    }

    private static L12StackItem PassUntilFlow(L12GameEngine game, string flow)
    {
        for (var safety = 0; safety < 80; safety++)
        {
            var found = game.State.EffectStack.SingleOrDefault(item =>
                item.Data.GetValueOrDefault("atomicFlow") == flow);
            if (found is not null) return found;
            var prompt = Assert.Single(game.State.PendingPrompts);
            Assert.Equal("response", prompt.Kind);
            Resolve(game, "pass");
        }
        throw new Xunit.Sdk.XunitException($"未进入预期复合段 {flow}");
    }

    [Fact]
    [Trait("L12Evidence", "batch:6K-A-inventory")]
    public void UniversalAndHeavenAuditFreezesEveryCardAndAbility()
    {
        Assert.Equal(56, AuditedAbilityCounts.Count);
        Assert.Equal(94, AuditedAbilityCounts.Values.Sum());
        Assert.All(AuditedAbilityCounts, pair =>
        {
            var card = Assert.Contains(pair.Key, Catalog.Cards);
            Assert.False(string.IsNullOrWhiteSpace(card.NameZh));
            Assert.False(string.IsNullOrWhiteSpace(card.Effect));
            Assert.True(pair.Value > 0);
        });
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0001")]
    [Trait("L12Evidence", "entry:independent-enter-segments")]
    public void BlackbeardDiscardAndSubsequentDrawAreSeparateStackItems()
    {
        var game = Create(8101);
        var player = game.State.Players[0];
        var blackbeard = Card("S01-0001", "batch6ka-blackbeard");
        player.Hand.Add(blackbeard);
        AddReadyMorale(player, Math.Max(1, blackbeard.CurrentCost));
        player.Library.AddRange([
            Card("S01-0003", "batch6ka-blackbeard-own-a"),
            Card("S01-0003", "batch6ka-blackbeard-own-b"),
        ]);
        game.State.Players[1].Library.Add(Card("S01-0003", "batch6ka-blackbeard-enemy-a"));

        Assert.True(game.Handle(0, new L12Command("playCard", blackbeard.InstanceId, Row: 0, Slot: 0)).Accepted);

        var discard = Assert.Single(game.State.EffectStack, item => item.SourceInstanceId == blackbeard.InstanceId);
        Assert.Equal("teach-enter-discard", discard.Data["atomicFlow"]);
        Assert.Equal("trigger:S01-0001:enter", discard.Data["compositePlan"]);
        discard.Negated = true;
        var draw = PassUntilFlow(game, "teach-enter-draw");
        Assert.Equal("teach-enter-draw", draw.Data["atomicFlow"]);
        Assert.Equal("1", draw.Data["compositeSegment"]);
        PassResponses(game);
        Assert.Equal(2, player.Hand.Count);
        Assert.Single(game.State.Players[1].Hand);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0020")]
    public void BattleUntilDawnDeclaresItsPublicDrawModeBeforeEnteringTheStack()
    {
        var game = Create(8102);
        var attacker = Card("S01-0002", "batch6ka-dawn-attacker");
        var counter = Card("S01-0020", "batch6ka-dawn-counter");
        counter.Hidden = true;
        counter.SetRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[1][0] = counter;
        for (var index = 0; index < 5; index++)
            game.State.Players[1].Graveyard.Add(Card("S01-0003", $"batch6ka-dawn-grave-{index}"));

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        Resolve(game, "pass");
        Resolve(game, counter.InstanceId);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains("mode:draw", declaration.ValidChoices);
        Assert.Contains("mode:none", declaration.ValidChoices);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == counter.InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0120")]
    public void EmptyCityDeclaresItsPublicDrawModeAfterTheCostAndBeforeStacking()
    {
        var game = Create(8103);
        var attacker = Card("S01-0002", "batch6ka-empty-attacker");
        var counter = Card("S01-0120", "batch6ka-empty-counter");
        counter.Hidden = true;
        counter.SetRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[1][0] = counter;
        AddReadyMorale(game.State.Players[1], 1);

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        Resolve(game, "pass");
        Resolve(game, counter.InstanceId);
        Resolve(game, Assert.Single(game.State.Players[1].Morale).InstanceId);

        var declaration = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Contains("mode:draw", declaration.ValidChoices);
        Assert.Contains("mode:none", declaration.ValidChoices);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == counter.InstanceId);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0021")]
    [Trait("L12Evidence", "entry:private-hand-public-entry-declaration")]
    public void RegencyDeclaresItsPrivateHandCardAndPublicEntryPositionBeforeStacking()
    {
        var game = Create(8109, secondMaster: "S01-01M1");
        var defender = game.State.Players[0];
        var attackerOwner = game.State.Players[1];
        var counter = Card("S01-0021", "batch6ka-regency");
        counter.Hidden = true;
        counter.SetRound = 0;
        var summon = Card("S01-0003", "batch6ka-regency-summon");
        defender.Field[1][0] = counter;
        defender.Hand.Add(summon);
        game.State.ActivePlayer = 1;
        AddReadyMorale(attackerOwner, 4);

        var activation = game.Handle(1, new L12Command("activateAbility", "master-1", Ability: "nonLethal"));
        Assert.True(activation.Accepted, activation.Error);
        PassResponses(game);

        L12Prompt declaration = null!;
        for (var safety = 0; safety < 20; safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            var pending = prompt.Data.TryGetValue("activationId", out var activationId)
                ? game.State.PendingActivations.FirstOrDefault(item => item.ActivationId == activationId)
                : null;
            if (pending?.SourceCardId == "S01-0021")
            {
                declaration = prompt;
                break;
            }
            if (prompt.Kind == "response") Resolve(game, "pass");
            else if (prompt.ValidChoices.Contains("mode:none")) Resolve(game, "mode:none");
            else Resolve(game, "skip");
        }

        Assert.NotNull(declaration);
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.True(declaration.IsPrivate);
        Assert.Contains(summon.InstanceId, declaration.ValidChoices);
        Assert.DoesNotContain("mode:none", declaration.ValidChoices);
        Assert.DoesNotContain("skip", declaration.ValidChoices);
        Assert.DoesNotContain(game.State.EffectStack, item => item.SourceInstanceId == counter.InstanceId);
        Assert.Same(counter, defender.Field[1][0]);
        Assert.True(counter.Hidden);

        Resolve(game, summon.InstanceId);
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("slot", slot.Kind);
        Assert.Equal("0", slot.Data["targetPlayerIndex"]);
        Resolve(game, "0:1");

        Assert.Contains(game.State.EffectStack, item => item.SourceInstanceId == counter.InstanceId);
        Assert.Contains(summon, defender.Hand);
        Assert.Contains(counter, defender.Resolving);
        for (var safety = 0; safety < 40 && !defender.Field.SelectMany(row => row).Contains(summon); safety++)
        {
            var prompt = Assert.Single(game.State.PendingPrompts);
            if (prompt.Kind == "response") Resolve(game, "pass");
            else if (prompt.ValidChoices.Contains("mode:none")) Resolve(game, "mode:none");
            else if (prompt.Continuation == "trigger-batch-order")
            {
                var ordered = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt",
                    PromptId: prompt.PromptId, CardInstanceIds: [.. prompt.ValidChoices]));
                Assert.True(ordered.Accepted, ordered.Error);
            }
            else throw new Xunit.Sdk.XunitException($"未预期的摄政皇权续接：{prompt.Kind}/{prompt.Continuation}");
        }
        Assert.Contains(summon, defender.Field.SelectMany(row => row));
        Assert.Same(summon, defender.Field[0][1]);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0116")]
    public void XishiWithNoDeclaredSummonStartsAtTheIndependentDrawSegment()
    {
        var game = Create(8104);
        var player = game.State.Players[0];
        var xishi = Card("S01-0116", "batch6ka-xishi");
        player.Field[0][0] = xishi;
        AddReadyMorale(player, 1);
        player.Library.Add(Card("S01-0003", "batch6ka-xishi-draw"));

        Assert.True(game.Handle(0, new L12Command("activateAbility", xishi.InstanceId,
            Ability: "xishiExchange")).Accepted);
        var declaration = Assert.Single(game.State.PendingPrompts);
        var declaredNone = game.Handle(0, new L12Command("resolvePrompt", PromptId: declaration.PromptId,
            CardInstanceIds: []));
        Assert.True(declaredNone.Accepted, declaredNone.Error);

        var draw = Assert.Single(game.State.EffectStack);
        Assert.Equal("xishi-draw", draw.Data["atomicFlow"]);
        Assert.Equal("1", draw.Data["compositeSegment"]);
        Assert.Contains(xishi, player.Graveyard);
        PassResponses(game);
        Assert.Contains(player.Hand, card => card.InstanceId == "batch6ka-xishi-draw");
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0105")]
    public void LiuBeiSearchAndSubsequentShuffleUseIndependentSegments()
    {
        var game = Create(8105);
        var player = game.State.Players[0];
        var liuBei = Card("S01-0105", "batch6ka-liubei");
        player.Field[0][0] = liuBei;
        AddReadyMorale(player, 1);
        player.Library.Add(Card("S01-0106", "batch6ka-guanyu"));

        var result = game.Handle(0, new L12Command("activateAbility", liuBei.InstanceId,
            Ability: "searchBrothers"));
        Assert.True(result.Accepted, result.Error);

        var search = Assert.Single(game.State.EffectStack);
        Assert.Equal("liubei-search", search.Data["atomicFlow"]);
        Assert.Equal("active:S01-0105:searchBrothers", search.Data["compositePlan"]);
        search.Negated = true;
        var shuffle = PassUntilFlow(game, "liubei-shuffle");
        Assert.Equal("liubei-shuffle", shuffle.Data["atomicFlow"]);
        PassResponses(game);
        Assert.DoesNotContain(game.State.PendingPrompts,
            prompt => prompt.Data.GetValueOrDefault("action") == "liubei-search");
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-01M1")]
    public void YangJianDrawAndDelayedPrivateReturnUseIndependentSegments()
    {
        var game = Create(8106, "S01-01M1");
        var player = game.State.Players[0];
        AddReadyMorale(player, 1);
        player.Hand.Add(Card("S01-0003", "batch6ka-yangjian-existing-hand"));
        player.Library.Add(Card("S01-0003", "batch6ka-yangjian-draw"));

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "drawCycle")).Accepted);

        var draw = Assert.Single(game.State.EffectStack);
        Assert.Equal("yangjian-draw", draw.Data["atomicFlow"]);
        draw.Negated = true;
        var next = PassUntilFlow(game, "yangjian-return");
        Assert.Equal("yangjian-return", next.Data["atomicFlow"]);
        PassResponses(game);
        var returnCard = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("yangjian-return-card", returnCard.Data["action"]);
        Assert.True(returnCard.IsPrivate);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-01D1")]
    [Trait("L12Evidence", "ability:palaceReward")]
    public void PalaceRewardMoraleAndSubsequentDrawUseIndependentSegments()
    {
        var game = Create(8107, "S01-01D1");
        var player = game.State.Players[0];
        player.ReturnedMoraleThisTurn = 2;
        player.Library.Add(Card("S01-0003", "batch6ka-palace-reward-draw"));

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "palaceReward")).Accepted);

        var morale = Assert.Single(game.State.EffectStack);
        Assert.Equal("palace-reward-morale", morale.Data["atomicFlow"]);
        morale.Negated = true;
        var draw = PassUntilFlow(game, "palace-reward-draw");
        Assert.Equal("palace-reward-draw", draw.Data["atomicFlow"]);
        PassResponses(game);
        Assert.Contains(player.Hand, card => card.InstanceId == "batch6ka-palace-reward-draw");
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-01D1")]
    [Trait("L12Evidence", "ability:palaceExchange")]
    public void PalaceExchangeReviveSurvivesItsIndependentKillSegmentBeingNegated()
    {
        var game = Create(8108, "S01-01D1");
        var player = game.State.Players[0];
        var enemy = game.State.Players[1];
        var revive = Card("S01-0114", "batch6ka-palace-revive");
        var victim = Card("S01-0003", "batch6ka-palace-victim");
        player.Graveyard.Add(revive);
        enemy.Field[0][0] = victim;
        AddReadyMorale(player, victim.CurrentCost);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "palaceExchange")).Accepted);
        Resolve(game, revive.InstanceId);
        Resolve(game, "1:2");
        Resolve(game, victim.InstanceId);

        var kill = Assert.Single(game.State.EffectStack);
        Assert.Equal("palace-exchange-kill", kill.Data["atomicFlow"]);
        kill.Negated = true;
        var reviveSegment = PassUntilFlow(game, "palace-exchange-revive");
        Assert.Equal("palace-exchange-revive", reviveSegment.Data["atomicFlow"]);
        Assert.Contains(victim, enemy.Field.SelectMany(row => row));
        PassResponses(game);
        Assert.Same(revive, player.Field[1][2]);
        Assert.Contains(victim, enemy.Field.SelectMany(row => row));
    }
}
