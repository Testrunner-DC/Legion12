using System.Reflection;
using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CardInstanceZoneResetTests
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(string masterId = "S01-04M2", int seed = 92801)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = "实例跨区重置",
            MasterId = masterId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        var game = new L12GameEngine(Catalog, "zone-reset", "ZONE-RESET", seed,
            ["甲", "乙"], [deck, baseDeck], skipPreparation: true,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            stateFormatVersion: 2);
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        foreach (var player in game.State.Players)
        {
            player.Hand.Clear();
            player.Library.Clear();
            player.Graveyard.Clear();
            player.Morale.Clear();
            player.Relic = null;
            player.ExtraRelics.Clear();
            foreach (var row in player.Field) Array.Clear(row);
        }
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId, CardId = cardId, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction,
            ImageUrl = definition.ImageUrl, Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue, EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0,
            Traits = [.. definition.Traits], Profession = definition.Profession, OwnerIndex = 0,
        };
    }

    private static object? Invoke(L12GameEngine game, string method, params object?[] args)
        => typeof(L12GameEngine).GetMethod(method, PrivateInstance)!.Invoke(game, args);

    private static void AddMorale(L12PlayerState player, int count)
    {
        for (var index = 0; index < count; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"zone-morale-{player.PlayerIndex}-{player.Morale.Count}",
                CardId = "S01-01C1", Tapped = false,
            });
    }

    private static void Resolve(L12GameEngine game, params string[] choices)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex, new L12Command("resolvePrompt",
            PromptId: prompt.PromptId, Choice: choices.Length == 1 ? choices[0] : null,
            CardInstanceIds: choices.Length == 1 ? null : [.. choices]));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
            Resolve(game, "pass");
    }

    private static void Dirty(L12CardInstance card)
    {
        card.CostModifier = -1;
        card.Troops = 123;
        card.Tapped = true;
        card.HasStrongAttack = true;
        card.ImmortalUses = 1;
        card.LastKnownWasRanged = true;
        card.LastKnownEffectiveProfession = "弓手";
        card.LastKnownAttachedCardIds.Add("old-attachment");
        card.MinimumPlayCost = 1;
        card.PlayBlockedReason = "old";
        card.SpendableResourceType = "old";
        card.ActiveKeywords.Add("旧关键词");
        card.StatusIcons.Add("old");
        card.StatusEffects.Add(new("old", "old"));
        card.RuleActions.Add(new("old", "old", "old", true));
        card.TimedModifiers.Add(new() { TroopsDelta = 1, CostDelta = 1, ExpiresAfterTurn = 99, Source = "old" });
    }

    private static void AssertReset(L12CardInstance card)
    {
        Assert.Equal(0, card.CostModifier);
        Assert.Equal(card.BaseTroops, card.Troops);
        Assert.False(card.Tapped);
        Assert.False(card.HasStrongAttack);
        Assert.Equal(0, card.ImmortalUses);
        Assert.Null(card.MinimumPlayCost);
        Assert.Null(card.PlayBlockedReason);
        Assert.Null(card.SpendableResourceType);
        Assert.Empty(card.ActiveKeywords);
        Assert.Empty(card.StatusIcons);
        Assert.Empty(card.StatusEffects);
        Assert.Empty(card.RuleActions);
        Assert.Empty(card.TimedModifiers);
    }

    public static IEnumerable<object[]> LimitedInstanceUsageRules()
        => L12ActiveUsageRules.All.Select(rule => new object[] { rule.CardId, rule.Ability });

    [Theory]
    [MemberData(nameof(LimitedInstanceUsageRules))]
    public void EveryRegisteredLimitedActiveUsageKeyObeysItsPrivateZoneScope(
        string cardId, string ability)
    {
        var game = Create();
        var player = game.State.Players[0];
        var card = Card(cardId, $"limited-{cardId}-{ability}");
        var key = L12ActiveUsageRules.UsageKey(card.InstanceId, cardId, ability);
        var instanceKey = $"active:{card.InstanceId}:{ability}";
        player.UsedAbilities.Add(key);
        player.UsedAbilities.Add(instanceKey);

        Invoke(game, "ResetCardForPrivateZone", card);

        Assert.DoesNotContain(instanceKey, player.UsedAbilities);
        if (L12CardNameUsageRules.Keys.ContainsKey(cardId))
            Assert.Contains(key, player.UsedAbilities);
        else
            Assert.DoesNotContain(key, player.UsedAbilities);
    }

    [Fact]
    public void PrivateBoundaryClearsOnlyKeysContainingTheExactInstanceToken()
    {
        var game = Create();
        var player = game.State.Players[0];
        var card = Card("S01-0410", "instance-token");
        var removed = new[]
        {
            "active:instance-token:choice",
            "simple-data-prefix:instance-token:4",
            "pending:kind:instance-token:turn",
            "instance-token",
        };
        var preserved = new[]
        {
            "card-name:S01-0410",
            "master-trigger:0:4",
            "global:instance-token-extra",
            "active:xinstance-token:choice",
            "active:instance-token-extra:choice",
        };
        player.UsedAbilities.UnionWith(removed.Concat(preserved));

        Invoke(game, "ResetCardForPrivateZone", card);

        Assert.DoesNotContain(player.UsedAbilities, key => removed.Contains(key));
        Assert.All(preserved, key => Assert.Contains(key, player.UsedAbilities));
    }

    [Fact]
    public void BattlefieldToLibraryClearsKusanagiInstanceUsageAndRuntimeState()
    {
        var game = Create();
        var player = game.State.Players[0];
        var sword = Card("S01-0417", "zone-kusanagi");
        sword.Abilities.Add(new L12AbilityView("printed", "印刷能力"));
        Dirty(sword);
        player.Field[0][0] = sword;
        player.UsedAbilities.UnionWith([
            "active:zone-kusanagi:choice",
            "susano-buff:zone-kusanagi",
            $"trial-card-lock:zone-kusanagi:{game.State.TurnSerial}",
            "card-name:S02-0006",
        ]);

        Assert.True((bool)Invoke(game, "MoveFieldCardToZone", player, sword,
            "library-top", "测试返回", false)!);

        Assert.Same(sword, player.Library[0]);
        AssertReset(sword);
        Assert.Contains(sword.Abilities, ability => ability.Id == "printed");
        Assert.DoesNotContain(player.UsedAbilities, key => key.Contains("zone-kusanagi", StringComparison.Ordinal));
        Assert.Contains("card-name:S02-0006", player.UsedAbilities);
    }

    [Fact]
    public void PrivateZoneToBattlefieldEntersWithResetState()
    {
        var game = Create();
        var player = game.State.Players[0];
        var legion = Card("S01-0410", "dirty-private-legion");
        Dirty(legion);
        player.Library.Add(legion);
        player.UsedAbilities.Add("active:dirty-private-legion:old");

        var owner = new L12StackItem
        {
            StackItemId = "zone-reset-summon-owner",
            Controller = player.PlayerIndex,
            SourceInstanceId = legion.InstanceId,
            SourceCardId = legion.CardId,
            SourceName = legion.Name,
            Trigger = "active",
            Text = string.Empty,
        };
        var summon = typeof(L12GameEngine).GetMethod("TrySummonFromAnyPrivateZone", PrivateInstance,
            null, [typeof(L12PlayerState), typeof(int), typeof(string), typeof(string), typeof(bool),
                typeof(L12StackItem)], null);
        Assert.NotNull(summon);
        Assert.True((bool)summon.Invoke(game, [player, 0, legion.InstanceId, "0:0", false, owner])!);
        Assert.Equal([Assert.Single(game.State.Events, entry => entry.Type == "put"
            && entry.Cards.Any(card => card.InstanceId == legion.InstanceId)).Sequence],
            owner.PresentationFactSequences);

        Assert.Same(legion, player.Field[0][0]);
        AssertReset(legion);
        Assert.False(legion.LastKnownWasRanged);
        Assert.Null(legion.LastKnownEffectiveProfession);
        Assert.Empty(legion.LastKnownAttachedCardIds);
        Assert.DoesNotContain(player.UsedAbilities,
            key => key.Contains(legion.InstanceId, StringComparison.Ordinal));
    }

    [Fact]
    public void OriginalHandSummonAndRelicPlacementEntrypointsResetBeforeEntryState()
    {
        var game = Create();
        var player = game.State.Players[0];
        var legion = Card("S01-0410", "entry-hand-legion");
        Dirty(legion);
        player.Hand.Add(legion);

        Assert.True((bool)Invoke(game, "TrySummonFromHand", player, legion.InstanceId,
            "0:0", true, null, null)!);
        Assert.Same(legion, player.Field[0][0]);
        Assert.Equal(0, legion.CostModifier);
        Assert.Equal(legion.BaseTroops, legion.Troops);
        Assert.False(legion.HasStrongAttack);
        Assert.Empty(legion.TimedModifiers);
        Assert.True(legion.Tapped);
        Assert.Equal(game.State.Round, legion.SummonRound);

        var relic = Card("S01-0417", "entry-library-relic");
        Dirty(relic);
        Invoke(game, "PlaceArtifactInRelicZone", 0, relic);
        Assert.Same(relic, player.Relic);
        AssertReset(relic);
        Assert.Equal(game.State.Round, relic.SummonRound);
    }

    [Fact]
    public void RealPlayCardResetsDirtyLegionBeforeApplyingItsEntryState()
    {
        var game = Create(seed: 92811);
        var player = game.State.Players[0];
        var legion = Card("S01-0410", "real-play-dirty-legion");
        Dirty(legion);
        player.Hand.Add(legion);
        AddMorale(player, legion.CurrentCost);

        var played = game.Handle(0, new L12Command("playCard", legion.InstanceId, Row: 0, Slot: 0));

        Assert.True(played.Accepted, played.Error);
        Assert.Same(legion, player.Field[0][0]);
        Assert.DoesNotContain(legion, player.Hand);
        AssertReset(legion);
        Assert.Equal(game.State.Round, legion.SummonRound);
        Assert.False(legion.LastKnownWasRanged);
        Assert.Null(legion.LastKnownEffectiveProfession);
        Assert.Empty(legion.LastKnownAttachedCardIds);
    }

    [Fact]
    public void RealPlayCardResetsDirtyArtifactBeforeRelicEntryState()
    {
        var game = Create(seed: 92812);
        var player = game.State.Players[0];
        var relic = Card("S01-0417", "real-play-dirty-relic");
        Dirty(relic);
        player.Hand.Add(relic);
        AddMorale(player, relic.CurrentCost);

        var played = game.Handle(0, new L12Command("playCard", relic.InstanceId));

        Assert.True(played.Accepted, played.Error);
        Assert.Same(relic, player.Relic);
        Assert.DoesNotContain(relic, player.Hand);
        AssertReset(relic);
        Assert.Equal(game.State.Round, relic.SummonRound);
        Assert.False(relic.LastKnownWasRanged);
        Assert.Null(relic.LastKnownEffectiveProfession);
        Assert.Empty(relic.LastKnownAttachedCardIds);
    }

    [Fact]
    public void RealPromotionSubmissionWithInvalidatedGodPowerDoesNotResetOrMoveDirtyCard()
    {
        var game = Create(seed: 92813);
        var player = game.State.Players[0];
        var foundation = Card("S02-0502", "promotion-failure-foundation");
        var promoted = Card("S02-0501", "promotion-failure-card");
        Dirty(promoted);
        var retainedAttachment = Card("S02-0609", "promotion-failure-retained-attachment");
        promoted.AttachedCards.Add(retainedAttachment);
        player.Field[0][0] = foundation;
        player.Hand.Add(promoted);
        for (var index = 0; index < 2; index++)
            player.Morale.Add(new L12MoraleCard
            {
                InstanceId = $"promotion-failure-power-{index}",
                CardId = "S02-05C1",
                IsGodPower = true,
                Tapped = false,
            });
        var before = JsonSerializer.Serialize(promoted);

        var begin = game.Handle(0, new L12Command("playCard", promoted.InstanceId));
        Assert.True(begin.Accepted, begin.Error);
        var prompt = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("s2-promotion-foundation", prompt.Continuation);
        Assert.Contains(foundation.InstanceId, prompt.ValidChoices);
        foreach (var power in player.Morale) power.Tapped = true;

        var failed = game.Handle(0, new L12Command("resolvePrompt", PromptId: prompt.PromptId,
            Choice: foundation.InstanceId));

        Assert.False(failed.Accepted);
        Assert.Contains(promoted, player.Hand);
        Assert.Same(foundation, player.Field[0][0]);
        Assert.Equal(before, JsonSerializer.Serialize(promoted));
        Assert.Same(retainedAttachment, Assert.Single(promoted.AttachedCards));
        Assert.DoesNotContain(foundation, promoted.AttachedCards);
    }

    [Fact]
    public void RichardSquireResetsFromPrivateZonesAndPreservesFieldRuntime()
    {
        var game = Create();
        var player = game.State.Players[0];
        var privateSquire = Card("S02-0609", "private-squire");
        Dirty(privateSquire);
        player.Hand.Add(privateSquire);

        var takenPrivate = Assert.IsType<L12CardInstance>(
            Invoke(game, "TakeS2RichardSquire", player, privateSquire.InstanceId));
        Assert.Same(privateSquire, takenPrivate);
        AssertReset(takenPrivate);

        var fieldSquire = Card("S02-0609", "field-squire");
        Dirty(fieldSquire);
        player.Field[0][0] = fieldSquire;
        var takenField = Assert.IsType<L12CardInstance>(
            Invoke(game, "TakeS2RichardSquire", player, fieldSquire.InstanceId));
        Assert.Same(fieldSquire, takenField);
        Assert.Null(player.Field[0][0]);
        Assert.True(takenField.HasStrongAttack);
        Assert.True(takenField.Tapped);
        Assert.Equal(-1, takenField.CostModifier);
    }

    [Fact]
    public void HandGraveLibraryHandTransitionsResetAtEveryBoundary()
    {
        var game = Create();
        var player = game.State.Players[0];
        var card = Card("S01-0410", "private-cycle");
        player.Hand.Add(card);

        Dirty(card);
        Assert.True((bool)Invoke(game, "MoveHandToGrave", player, card.InstanceId, true, null)!);
        AssertReset(card);

        Dirty(card);
        Invoke(game, "MoveGraveToLibraryTop", player, card.InstanceId);
        Assert.Same(card, player.Library[0]);
        AssertReset(card);

        Dirty(card);
        Assert.True((bool)Invoke(game, "MoveLibraryCardToHandByEffect", player,
            card.InstanceId, "测试入手")!);
        Assert.Contains(card, player.Hand);
        AssertReset(card);
    }

    [Fact]
    public void KusanagiReturnedBySusanoAndPlayedFromTopByOkitaCanUseItsChoiceAgain()
    {
        var game = Create(seed: 92803);
        var player = game.State.Players[0];
        var sword = Card("S01-0417", "reported-kusanagi");
        var okita = Card("S02-0403", "reported-okita");
        okita.SummonRound = -1;
        player.Relic = sword;
        player.Field[0][1] = okita;
        AddMorale(player, 6);

        Assert.True(game.Handle(0, new L12Command("activateAbility", sword.InstanceId,
            Ability: "kusanagiStrong")).Accepted);
        Resolve(game, okita.InstanceId);
        PassResponses(game);
        Assert.Contains("active:reported-kusanagi:choice", player.UsedAbilities);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "kusanagi")).Accepted);
        Resolve(game, "0:0");
        PassResponses(game);
        Assert.Same(sword, player.Field[0][0]);
        Assert.Contains("active:reported-kusanagi:choice", player.UsedAbilities);

        Assert.True((bool)Invoke(game, "MoveFieldCardToZone", player, sword,
            "library-top", "因离场效果返回牌库顶部", false)!);
        Assert.DoesNotContain("active:reported-kusanagi:choice", player.UsedAbilities);

        Assert.True(game.Handle(0, new L12Command("attack", okita.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
        PassResponses(game);
        Resolve(game, "play");
        var slot = Assert.Single(game.State.PendingPrompts);
        Assert.True(game.Handle(0, new L12Command("resolvePrompt", PromptId: slot.PromptId,
            Choice: slot.ValidChoices[0])).Accepted);
        PassResponses(game);
        if (game.State.PendingDefense is not null)
            Assert.True(game.Handle(1, new L12Command("resolveDefense")).Accepted);

        Assert.Same(sword, player.Relic);
        var secondUse = game.Handle(0, new L12Command("activateAbility", sword.InstanceId,
            Ability: "kusanagiStrong"));
        Assert.True(secondUse.Accepted, secondUse.Error);
    }

    [Fact]
    public void WukongBattlefieldMasterRoundTripKeepsTheSameRuntimeInstanceAcrossCheckpoint()
    {
        var game = Create("S02-01M1", 92802);
        var player = game.State.Players[0];
        var wukong = Card("S02-01M1", "persistent-wukong");
        wukong.IsMasterLegion = true;
        wukong.HasStrongAttack = true;
        wukong.Troops = 4000;
        player.Field[0][0] = wukong;

        Assert.True((bool)Invoke(game, "ReturnWukongMasterLegions", player, "测试", false)!);
        Assert.Same(wukong, player.MasterLegionState);
        Assert.True(wukong.HasStrongAttack);
        Assert.Equal(4000, wukong.Troops);

        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false);
        Assert.Equal("persistent-wukong", game.State.Players[0].MasterLegionState?.InstanceId);
        Assert.True(game.State.Players[0].MasterLegionState?.HasStrongAttack);
        Assert.Equal(4000, game.State.Players[0].MasterLegionState?.Troops);

        player = game.State.Players[0];
        AddMorale(player, 2);
        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "wukongTransform")).Accepted);
        Resolve(game, player.Morale.Select(card => card.InstanceId).ToArray());
        Resolve(game, "0:1");
        PassResponses(game);
        var returned = Assert.Single(player.Field[0], card => card?.IsMasterLegion == true)!;
        Assert.Equal("persistent-wukong", returned.InstanceId);
        Assert.True(returned.HasStrongAttack);
    }

    [Fact]
    public void WukongRealTransformAttackNextOwnTurnAndTransformAgainKeepsIdentityAndReadies()
    {
        var game = Create("S02-01M1", 92804);
        var player = game.State.Players[0];
        player.Library.Add(Card("S01-0410", "p0-draw-1"));
        game.State.Players[1].Library.Add(Card("S01-0410", "p1-draw-1"));
        AddMorale(player, 2);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "wukongTransform")).Accepted);
        Resolve(game, player.Morale.Select(card => card.InstanceId).ToArray());
        Resolve(game, "0:0");
        PassResponses(game);
        if (game.State.PendingPrompts.FirstOrDefault()?.ValidChoices.Contains("mode:none") == true)
            Resolve(game, "mode:none");
        var first = Assert.Single(player.Field[0], card => card?.IsMasterLegion == true)!;
        var instanceId = first.InstanceId;

        var attack = game.Handle(0, new L12Command("attack", first.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted,
            $"{attack.Error}; phase={game.State.Phase}; prompts={string.Join(';', game.State.PendingPrompts.Select(p => $"{p.Kind}[{string.Join(',', p.ValidChoices)}]/{p.Continuation}"))}; stack={game.State.EffectStack.Count}");
        PassResponses(game);
        if (game.State.PendingDefense is not null)
            Assert.True(game.Handle(1, new L12Command("resolveDefense")).Accepted);
        PassResponses(game);
        Assert.Same(first, player.MasterLegionState);
        Assert.True(first.Tapped);

        Assert.True(game.Handle(0, new L12Command("endTurn")).Accepted);
        PassResponses(game);
        Assert.True(game.Handle(1, new L12Command("endTurn")).Accepted);
        PassResponses(game);
        Assert.Equal(0, game.State.ActivePlayer);
        Assert.False(first.Tapped);
        Assert.Equal(0, first.AttacksThisTurn);

        AddMorale(player, 2);
        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "wukongTransform")).Accepted);
        Resolve(game, player.Morale.Select(card => card.InstanceId).ToArray());
        Resolve(game, "0:1");
        PassResponses(game);
        if (game.State.PendingPrompts.FirstOrDefault()?.ValidChoices.Contains("mode:none") == true)
            Resolve(game, "mode:none");
        var second = Assert.Single(player.Field[0], card => card?.IsMasterLegion == true)!;
        Assert.Same(first, second);
        Assert.Equal(instanceId, second.InstanceId);
        Assert.False(second.Tapped);
        Assert.True(game.Handle(0, new L12Command("attack", second.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
    }

    [Fact]
    public void ShennongResetAllowsSameTurnWukongRetransformAsTheSameReadyInstance()
    {
        var game = Create("S02-01M1", 92809);
        var player = game.State.Players[0];
        var shennong = Card("S02-0104", "same-turn-shennong");
        player.Relic = shennong;
        AddMorale(player, 5);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "wukongTransform")).Accepted);
        Resolve(game, player.Morale.Take(2).Select(card => card.InstanceId).ToArray());
        Resolve(game, "0:0");
        PassResponses(game);
        if (game.State.PendingPrompts.FirstOrDefault()?.ValidChoices.Contains("mode:none") == true)
            Resolve(game, "mode:none");
        var first = Assert.Single(player.Field[0], card => card?.IsMasterLegion == true)!;
        var instanceId = first.InstanceId;

        var attack = game.Handle(0, new L12Command("attack", first.InstanceId,
            Target: new L12AttackTarget("master")));
        Assert.True(attack.Accepted, attack.Error);
        PassResponses(game);
        if (game.State.PendingDefense is not null)
            Assert.True(game.Handle(1, new L12Command("resolveDefense")).Accepted);
        PassResponses(game);
        Assert.Same(first, player.MasterLegionState);
        Assert.True(first.Tapped);

        var usageKey = L12ActiveUsageRules.UsageKey("master-0", "S02-01M1", "wukongTransform");
        Assert.Contains(usageKey, player.UsedAbilities);
        var reset = game.Handle(0, new L12Command("activateAbility", shennong.InstanceId,
            Ability: "shennongReset"));
        Assert.True(reset.Accepted, reset.Error);
        Resolve(game, "wukongTransform");
        if (game.State.PendingPrompts.FirstOrDefault()?.Kind == "resource-return")
            Resolve(game, player.Morale[0].InstanceId);
        PassResponses(game);
        Assert.DoesNotContain(usageKey, player.UsedAbilities);

        Assert.True(game.Handle(0, new L12Command("activateAbility", "master-0",
            Ability: "wukongTransform")).Accepted);
        Resolve(game, player.Morale.Take(2).Select(card => card.InstanceId).ToArray());
        Resolve(game, "0:1");
        PassResponses(game);
        if (game.State.PendingPrompts.FirstOrDefault()?.ValidChoices.Contains("mode:none") == true)
            Resolve(game, "mode:none");
        var second = Assert.Single(player.Field[0], card => card?.IsMasterLegion == true)!;
        Assert.Same(first, second);
        Assert.Equal(instanceId, second.InstanceId);
        Assert.False(second.Tapped);
        Assert.True(game.Handle(0, new L12Command("attack", second.InstanceId,
            Target: new L12AttackTarget("master"))).Accepted);
    }

    [Fact]
    public void WukongReturnDoesNotOverwriteAnExistingStoredInstance()
    {
        var game = Create("S02-01M1", 92805);
        var player = game.State.Players[0];
        var stored = Card("S02-01M1", "stored-wukong");
        stored.IsMasterLegion = true;
        var field = Card("S02-01M1", "second-wukong");
        field.IsMasterLegion = true;
        player.MasterLegionState = stored;
        player.Field[0][0] = field;

        Assert.False((bool)Invoke(game, "ReturnWukongMasterLegions", player, "测试", false)!);
        Assert.Same(stored, player.MasterLegionState);
        Assert.Same(field, player.Field[0][0]);
        Assert.Contains(game.State.Events, item => item.Type == "invalid-state");
    }

    [Theory]
    [InlineData("field")]
    [InlineData("hand")]
    public void RestoreCheckpointRejectsMasterInstanceDuplicatedInAnotherZone(string duplicateZone)
    {
        var game = Create("S02-01M1", 92806);
        var player = game.State.Players[0];
        var stored = Card("S02-01M1", "duplicate-wukong");
        stored.IsMasterLegion = true;
        player.MasterLegionState = stored;
        if (duplicateZone == "field") player.Field[0][0] = stored;
        else player.Hand.Add(stored);
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);

        var error = Assert.Throws<TargetInvocationException>(() => InvokeStaticRestore(
            game.SerializeFullState(), random, game.CardFactSignalSequence));
        Assert.IsType<InvalidDataException>(error.InnerException);
    }

    [Fact]
    public void LegacyCheckpointWithoutStoredMasterFieldRestoresAndSnapshotsDoNotLeakStoredState()
    {
        var game = Create("S02-01M1", 92807);
        var player = game.State.Players[0];
        var stored = Card("S02-01M1", "private-stored-wukong");
        stored.IsMasterLegion = true;
        player.MasterLegionState = stored;

        foreach (var snapshot in new object[]
                 { game.SnapshotFor(0), game.SnapshotFor(1), game.SnapshotForSpectator() })
        {
            var projected = JsonSerializer.Serialize(snapshot);
            Assert.DoesNotContain("MasterLegionState", projected, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(stored.InstanceId, projected, StringComparison.Ordinal);
        }

        var json = JsonNode.Parse(game.SerializeFullState())!.AsObject();
        foreach (var node in json["Players"]!.AsArray())
            node!.AsObject().Remove("MasterLegionState");
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, json.ToJsonString(), random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        Assert.All(restored.State.Players, restoredPlayer => Assert.Null(restoredPlayer.MasterLegionState));
    }

    [Fact]
    public void StoredWukongRestStateIsPublicInEveryProjectionAndSurvivesCheckpoint()
    {
        var game = Create("S02-01M1", 92810);
        var stored = Card("S02-01M1", "projection-stored-wukong");
        stored.IsMasterLegion = true;
        stored.Tapped = true;
        game.State.Players[0].MasterLegionState = stored;

        AssertProjectedMasterTapped(game, stored.InstanceId);
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        Assert.True(game.State.Players[0].MasterLegionState?.Tapped);
        AssertProjectedMasterTapped(game, stored.InstanceId);
    }

    private static void AssertProjectedMasterTapped(L12GameEngine game, string privateInstanceId)
    {
        foreach (var snapshot in new object[]
                 { game.SnapshotFor(0), game.SnapshotFor(1), game.SnapshotForSpectator() })
        {
            var json = JsonSerializer.SerializeToElement(snapshot);
            var master = json.GetProperty("Players")[0].GetProperty("master");
            Assert.True(master.GetProperty("tapped").GetBoolean());
            Assert.DoesNotContain(privateInstanceId, json.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("MasterLegionState", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void MatchRecorderTracksStoredMasterAsAnAuthoritativeMasterZoneCard()
    {
        var game = Create("S02-01M1", 92808);
        var stored = Card("S02-01M1", "facts-stored-wukong");
        stored.IsMasterLegion = true;
        game.State.Players[0].MasterLegionState = stored;

        var recorderType = typeof(MatchRecorder);
        var capture = recorderType.GetMethod("CaptureLocations", BindingFlags.Static | BindingFlags.NonPublic,
            null, [typeof(L12GameState)], null)!;
        var locations = Assert.IsAssignableFrom<IDictionary>(capture.Invoke(null, [game.State]));
        var location = locations[stored.InstanceId]!;
        Assert.Equal("master", location.GetType().GetProperty("Zone")!.GetValue(location));

        var enumerate = recorderType.GetMethod("EnumeratePlayerCards", BindingFlags.Static | BindingFlags.NonPublic)!;
        var cards = Assert.IsAssignableFrom<IEnumerable>(enumerate.Invoke(null, [game.State.Players[0]]))
            .Cast<L12CardInstance>();
        Assert.Contains(stored, cards);
    }

    private static object? InvokeStaticRestore(string stateJson, L12RandomState random, long signalSequence)
        => typeof(L12GameEngine).GetMethod("RestoreCheckpoint", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [Catalog, stateJson, random, signalSequence, false, false, null]);
}
