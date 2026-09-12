using System.Reflection;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class Batch299WukongTombEvidenceTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(string masterId = "S01-04M1", int seed = 29901)
    {
        var basis = Catalog.DeckAt(0);
        var firstDeck = new L12PresetDeckDefinition
        {
            Name = $"{masterId}证据牌库",
            MasterId = masterId,
            CardIds = [.. basis.CardIds],
            MoraleIds = [.. basis.MoraleIds],
            SpecialIds = [.. basis.SpecialIds],
        };
        var game = new L12GameEngine(Catalog, "batch299-wukong-tomb", "B299WT", seed,
            ["甲", "乙"], [firstDeck, basis], skipPreparation: true,
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
            player.Resolving.Clear();
            player.Removed.Clear();
            player.Morale.Clear();
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
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
            OwnerIndex = owner,
        };
    }

    private static object? Call(L12GameEngine game, string methodName, params object?[] args)
        => typeof(L12GameEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == methodName && method.GetParameters().Length == args.Length)
            .Invoke(game, args);

    private static void AssertOnlyInMasterZone(L12GameEngine game, int ownerIndex, string instanceId)
    {
        var owner = game.State.Players[ownerIndex];
        Assert.DoesNotContain(owner.Field.SelectMany(row => row), card => card?.InstanceId == instanceId);
        Assert.DoesNotContain(owner.Hand, card => card.InstanceId == instanceId);
        Assert.DoesNotContain(owner.Library, card => card.InstanceId == instanceId);
        Assert.DoesNotContain(owner.Graveyard, card => card.InstanceId == instanceId);
        Assert.DoesNotContain(owner.Removed, card => card.InstanceId == instanceId);
        Assert.DoesNotContain(owner.Resolving, card => card.InstanceId == instanceId);
    }

    private static L12CardInstance PlainLegion(string instanceId, int troops, int owner)
        => new()
        {
            InstanceId = instanceId,
            CardId = $"test-{instanceId}",
            Name = instanceId,
            CardType = "legion",
            Faction = "universal",
            Cost = 1,
            BaseTroops = troops,
            Troops = troops,
            SummonRound = -1,
            OwnerIndex = owner,
        };

    private static L12CardInstance WukongLegion(int troops)
    {
        var wukong = Card("S02-01M1", "batch299-wukong");
        wukong.IsMasterLegion = true;
        wukong.HasCharge = true;
        wukong.SetTroopsValue = troops;
        wukong.Troops = troops;
        return wukong;
    }

    private static void ResolveChoice(L12GameEngine game, string choice)
    {
        var prompt = Assert.Single(game.State.PendingPrompts);
        var result = game.Handle(prompt.PlayerIndex,
            new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: choice));
        Assert.True(result.Accepted, result.Error);
    }

    private static void PassResponses(L12GameEngine game)
    {
        for (var safety = 0; safety < 80 && game.State.PendingPrompts.FirstOrDefault()?.Kind == "response"; safety++)
            ResolveChoice(game, "pass");
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Kind == "response");
    }

    private static L12Prompt? AdvanceCombatToPublicDecisionOrCompletion(L12GameEngine game)
    {
        for (var safety = 0; safety < 120; safety++)
        {
            if (game.State.PendingPrompts.FirstOrDefault() is { } prompt)
            {
                if (prompt.Kind != "response") return prompt;
                ResolveChoice(game, "pass");
                continue;
            }
            if (game.State.PendingDefense is { Stage: L12CombatStage.DefenseChoice } pending)
            {
                var result = game.Handle(1 - pending.AttackerPlayer,
                    new L12Command("resolveDefense", CardInstanceIds: []));
                Assert.True(result.Accepted, result.Error);
                continue;
            }
            if (game.State.PendingDefense is null) return null;
        }
        throw new Xunit.Sdk.XunitException("战斗时间线未在安全上限内结束或进入公开选择");
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-01M1")]
    [Trait("L12Evidence", "timing:surviving-after-attack-return")]
    public void WukongSurvivingAttackReturnsAndOffersOptionalRestedMorale()
    {
        var game = Create("S02-01M1", 29901);
        var owner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var wukong = WukongLegion(4000);
        var target = PlainLegion("batch299-survival-target", 1000, 1);
        owner.Field[0][0] = wukong;
        opponent.Field[0][0] = target;
        opponent.Morale.Add(new L12MoraleCard { InstanceId = "batch299-enemy-morale", CardId = "S01-01C1" });

        var attack = game.Handle(0, new L12Command("attack", wukong.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);

        var optional = Assert.IsType<L12Prompt>(AdvanceCombatToPublicDecisionOrCompletion(game));
        Assert.Equal("pending-activation", optional.Continuation);
        Assert.Contains("mode:none", optional.ValidChoices);
        Assert.Contains("mode:use", optional.ValidChoices);
        Assert.DoesNotContain(owner.Field.SelectMany(row => row), card => card?.IsMasterLegion == true);

        ResolveChoice(game, "mode:use");
        PassResponses(game);
        var morale = Assert.Single(owner.Morale);
        Assert.True(morale.Tapped);
    }

    [Fact]
    [Trait("L12Evidence", "card:S02-01M1")]
    [Trait("L12Evidence", "boundary:attacker-dies-before-after-attack")]
    public void WukongKilledDuringAttackReturnsToMasterZoneAndOffersMoraleReward()
    {
        var game = Create("S02-01M1", 29902);
        var owner = game.State.Players[0];
        var opponent = game.State.Players[1];
        var wukong = WukongLegion(1000);
        var target = PlainLegion("batch299-lethal-target", 4000, 1);
        owner.Field[0][0] = wukong;
        opponent.Field[0][0] = target;
        opponent.Morale.Add(new L12MoraleCard { InstanceId = "batch299-more-morale", CardId = "S01-01C1" });

        var attack = game.Handle(0, new L12Command("attack", wukong.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);

        var optional = Assert.IsType<L12Prompt>(AdvanceCombatToPublicDecisionOrCompletion(game));
        Assert.Equal("pending-activation", optional.Continuation);
        AssertOnlyInMasterZone(game, 0, wukong.InstanceId);
        ResolveChoice(game, "mode:use");
        PassResponses(game);
        var morale = Assert.Single(owner.Morale);
        Assert.True(morale.Tapped);
    }

    [Theory]
    [InlineData("hand")]
    [InlineData("library-top")]
    [InlineData("library-bottom")]
    [InlineData("removed")]
    [InlineData("graveyard")]
    [InlineData("effect-defeat")]
    [Trait("L12Evidence", "card:S02-01M1")]
    [Trait("L12Evidence", "replacement:any-leave-returns-master-zone")]
    public void WukongLegionIgnoresRequestedOrdinaryDestinationAndReturnsOnlyToMasterZone(string destination)
    {
        var game = Create("S02-01M1", 29920 + destination.Length);
        var owner = game.State.Players[0];
        var wukong = WukongLegion(3000);
        owner.Field[0][0] = wukong;
        game.State.Players[1].Morale.Add(new L12MoraleCard
        {
            InstanceId = $"batch299-opponent-{destination}", CardId = "S01-01C1"
        });

        var moved = destination == "effect-defeat"
            ? game.HandleGm(new L12GmCommand("destroyCard", 0, CardInstanceId: wukong.InstanceId)).Accepted
            : Assert.IsType<bool>(Call(game, "MoveFieldCardToZone",
                owner, wukong, destination, "证据矩阵离场", false));

        Assert.True(moved);
        AssertOnlyInMasterZone(game, 0, wukong.InstanceId);
        var optional = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", optional.Continuation);
        ResolveChoice(game, "mode:use");
        PassResponses(game);
        Assert.True(Assert.Single(owner.Morale).Tapped);
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0204")]
    [Trait("L12Evidence", "timing:mandatory-death-precedes-leave")]
    public void TombConstructTwoGuardsUseOneMandatoryDeathPathWithoutLeaveChoice()
    {
        var game = Create(seed: 29903);
        var owner = game.State.Players[0];
        var construct = Card("S01-0204", "batch299-construct-success");
        var guards = new[]
        {
            Card("S01-0212", "batch299-guard-success-a"),
            Card("S01-0212", "batch299-guard-success-b"),
        };
        construct.AttachedCards.AddRange(guards);
        owner.Field[0][0] = construct;

        var destroyed = game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: construct.InstanceId));
        Assert.True(destroyed.Accepted, destroyed.Error);
        Assert.Equal("pending-activation", Assert.Single(game.State.PendingPrompts).Continuation);
        Assert.DoesNotContain(game.State.PendingPrompts, prompt => prompt.Continuation == "trigger-batch-order");

        ResolveChoice(game, "0:0");
        ResolveChoice(game, "0:1");
        var death = Assert.Single(game.State.EffectStack, item => item.SourceCardId == "S01-0204");
        Assert.Equal("death", death.Trigger);
        PassResponses(game);

        Assert.All(guards, guard =>
        {
            Assert.True(guard.Tapped);
            Assert.Contains(owner.Field.SelectMany(row => row), card => ReferenceEquals(card, guard));
            Assert.DoesNotContain(owner.Graveyard, card => card.InstanceId == guard.InstanceId);
        });
        Assert.DoesNotContain(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.Text.Contains("离场", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("L12Evidence", "card:S01-0204")]
    [Trait("L12Evidence", "repro:BUG-20260908-74b09916-combat-three-guards")]
    public void TombConstructCombatDeathWithThreeAttachedGuardsDeclaresBeforeStackAndSummonsAll(bool negateDeath)
    {
        var game = Create(seed: 29905);
        var attackerPlayer = game.State.Players[0];
        var constructPlayer = game.State.Players[1];
        var attacker = PlainLegion("batch299-tomb-attacker", 10000, 0);
        var construct = Card("S01-0204", "batch299-combat-construct", 1);
        var guards = new[]
        {
            Card("S01-0212", "batch299-combat-guard-a", 1),
            Card("S01-0212", "batch299-combat-guard-b", 1),
            Card("S01-0212", "batch299-combat-guard-c", 1),
        };
        construct.AttachedCards.AddRange(guards);
        attackerPlayer.Field[0][0] = attacker;
        constructPlayer.Field[0][0] = construct;

        var attack = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", construct.InstanceId)));
        Assert.True(attack.Accepted, attack.Error);

        var declaration = Assert.IsType<L12Prompt>(AdvanceCombatToPublicDecisionOrCompletion(game));
        Assert.Equal("pending-activation", declaration.Continuation);
        Assert.Empty(game.State.EffectStack);
        ResolveChoice(game, "0:0");
        ResolveChoice(game, "0:1");
        ResolveChoice(game, "0:2");
        var death = Assert.Single(game.State.EffectStack, item => item.SourceCardId == "S01-0204");
        Assert.Equal("death", death.Trigger);
        Assert.False(death.Negated);
        death.Negated = negateDeath;

        PassResponses(game);
        if (negateDeath)
        {
            Assert.Equal("pending-activation", Assert.Single(game.State.PendingPrompts).Continuation);
            ResolveChoice(game, "0:0");
            ResolveChoice(game, "0:1");
            ResolveChoice(game, "0:2");
            Assert.Equal("leave", Assert.Single(game.State.EffectStack).Trigger);
            PassResponses(game);
        }

        Assert.All(guards, guard =>
        {
            Assert.True(guard.Tapped);
            Assert.Equal(1, constructPlayer.Field.SelectMany(row => row)
                .Count(card => card?.InstanceId == guard.InstanceId));
            Assert.DoesNotContain(constructPlayer.Graveyard, card => card.InstanceId == guard.InstanceId);
        });
        Assert.Contains(constructPlayer.Graveyard, card => card.InstanceId == construct.InstanceId);
        Assert.Equal(negateDeath, game.State.Events.Any(entry => entry.Type == "effect-trigger"
            && entry.Text.Contains("离场", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("L12Evidence", "card:S01-0204")]
    [Trait("L12Evidence", "timing:negated-death-leave-fallback")]
    public void TombConstructTwoGuardsUseLeaveFallbackOnlyAfterDeathIsNegated()
    {
        var game = Create(seed: 29904);
        var owner = game.State.Players[0];
        var construct = Card("S01-0204", "batch299-construct-negated");
        var guards = new[]
        {
            Card("S01-0212", "batch299-guard-negated-a"),
            Card("S01-0212", "batch299-guard-negated-b"),
        };
        construct.AttachedCards.AddRange(guards);
        owner.Field[0][0] = construct;

        var destroyed = game.HandleGm(new L12GmCommand("destroyCard", 0,
            CardInstanceId: construct.InstanceId));
        Assert.True(destroyed.Accepted, destroyed.Error);
        ResolveChoice(game, "0:0");
        ResolveChoice(game, "0:1");
        var death = Assert.Single(game.State.EffectStack, item => item.SourceCardId == "S01-0204");
        Assert.Equal("death", death.Trigger);
        death.Negated = true;
        PassResponses(game);

        var fallback = Assert.Single(game.State.PendingPrompts);
        Assert.Equal("pending-activation", fallback.Continuation);
        ResolveChoice(game, "0:1");
        ResolveChoice(game, "0:2");
        var leave = Assert.Single(game.State.EffectStack, item => item.SourceCardId == "S01-0204");
        Assert.Equal("leave", leave.Trigger);
        PassResponses(game);

        Assert.All(guards, guard =>
        {
            Assert.True(guard.Tapped);
            Assert.Equal(1, owner.Field.SelectMany(row => row).Count(card => card?.InstanceId == guard.InstanceId));
            Assert.DoesNotContain(owner.Graveyard, card => card.InstanceId == guard.InstanceId);
        });
        Assert.Contains(game.State.Events, entry => entry.Type == "effect-trigger"
            && entry.Text.Contains("离场", StringComparison.Ordinal));
    }
}
