using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class SusanooAttackBuffPlayerLogRegressionTests
{
    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void SusanooRealAttackPublishesSourceTargetAndPlusTwoThousandForEveryViewer(
        int controller, bool restoreAfterDeclaration)
    {
        var game = Create(controller, "S01-04M2", 26101710 + controller * 10 + (restoreAfterDeclaration ? 1 : 0));
        var player = game.State.Players[controller];
        var sword = Card("S01-0417", $"susano-log-sword-{controller}-{restoreAfterDeclaration}");
        L12DerivedStats.SetUntilTurnEnd(sword, 5000, int.MaxValue);
        sword.SummonRound = 0;
        player.Field[0][0] = sword;
        AddMorale(player, 1);

        Assert.True(game.Handle(controller,
            new L12Command("activateAbility", $"master-{controller}", Ability: "frontBuff")).Accepted);
        var target = Assert.Single(game.State.PendingPrompts);
        Assert.Contains(sword.InstanceId, target.ValidChoices);
        var declaration = game.Handle(controller,
            new L12Command("resolvePrompt", PromptId: target.PromptId, Choice: sword.InstanceId));
        Assert.True(declaration.Accepted, declaration.Error);

        if (restoreAfterDeclaration)
            game = RestoreExactV2(game);
        PassResponses(game);
        sword = Assert.IsType<L12CardInstance>(game.State.Players[controller].Field[0][0]);

        var attack = game.Handle(controller, new L12Command("attack", sword.InstanceId,
            Target: new L12AttackTarget("master", $"master-{1 - controller}")));
        Assert.True(attack.Accepted, attack.Error);
        Assert.Equal(7000, sword.Troops);

        var entry = Assert.Single(game.State.Events, item =>
            item.PlayerLogSemantic?.SourceInstanceId == $"master-{controller}"
            && item.PlayerLogSemantic.TargetInstanceId == sword.InstanceId
            && item.PlayerLogSemantic.OutcomeLabel.Contains("+2000", StringComparison.Ordinal));
        Assert.Equal("须佐之男", entry.PlayerLogSemantic?.SourceName);
        Assert.Equal("草薙剑", entry.PlayerLogSemantic?.TargetName);
        Assert.Equal("触发 进攻加兵力", entry.PlayerLogSemantic?.ActionLabel);
        Assert.Equal("〈草薙剑〉本次前排进攻兵力+2000", entry.PlayerLogSemantic?.OutcomeLabel);
        Assert.Equal(2, entry.Cards.Count());
        Assert.Contains(entry.Cards, card => card.InstanceId == $"master-{controller}" && !card.Hidden);
        Assert.Contains(entry.Cards, card => card.InstanceId == sword.InstanceId && !card.Hidden);

        AssertPublicSemantic(game.SnapshotFor(controller), entry.Sequence, entry.PlayerLogSemantic!);
        AssertPublicSemantic(game.SnapshotFor(1 - controller), entry.Sequence, entry.PlayerLogSemantic!);
        var spectator = game.SnapshotForSpectator();
        AssertPublicSemantic(spectator, entry.Sequence, entry.PlayerLogSemantic!);
        Assert.DoesNotContain("susano-private", System.Text.Json.JsonSerializer.Serialize(spectator),
            StringComparison.Ordinal);

        var countBeforeRejectedDuplicate = game.State.Events.Count(item => item.Sequence == entry.Sequence);
        var rejectedDuplicate = game.Handle(controller, new L12Command("attack", sword.InstanceId,
            Target: new L12AttackTarget("master", $"master-{1 - controller}")));
        Assert.False(rejectedDuplicate.Accepted);
        Assert.Equal(countBeforeRejectedDuplicate,
            game.State.Events.Count(item => item.Sequence == entry.Sequence));
    }

    [Fact]
    public void LegacyV2CheckpointWithoutAttackBuffProtocolKeepsTheOriginalRawEventShape()
    {
        var current = Create(0, "S01-04M2", 26101719);
        var player = current.State.Players[0];
        var sword = Card("S01-0417", "susano-log-legacy-sword");
        L12DerivedStats.SetUntilTurnEnd(sword, 5000, int.MaxValue);
        sword.SummonRound = 0;
        player.Field[0][0] = sword;
        AddMorale(player, 1);

        Assert.True(current.Handle(0,
            new L12Command("activateAbility", "master-0", Ability: "frontBuff")).Accepted);
        var target = Assert.Single(current.State.PendingPrompts);
        Assert.True(current.Handle(0, new L12Command("resolvePrompt", PromptId: target.PromptId,
            Choice: sword.InstanceId)).Accepted);
        PassResponses(current);

        var legacy = RestoreWithoutAttackBuffProtocol(current);

        sword = Assert.IsType<L12CardInstance>(legacy.State.Players[0].Field[0][0]);
        var attack = AttackMaster(legacy, 0, sword);
        Assert.True(attack.Accepted, attack.Error);
        Assert.Equal(7000, sword.Troops);
        var raw = Assert.Single(legacy.State.Events, item =>
            item.Text == "须佐之男使 草薙剑 本次前排进攻兵力 +2000");
        Assert.Null(raw.PlayerLogSemantic);
        var onlyAttacker = Assert.Single(raw.Cards);
        Assert.Equal(sword.InstanceId, onlyAttacker.InstanceId);
        Assert.DoesNotContain(raw.Cards, card => card.InstanceId == "master-0");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SaladinAdjacentSourcesAddExactlyOnceAndPublishTheFirstPublicSource(int adjacentSourceCount)
    {
        var game = Create(0, "S01-04M2", 26101730 + adjacentSourceCount);
        var (attacker, firstSaladin) = ArrangeSaladinLine(game, adjacentSourceCount);

        Assert.True(AttackMaster(game, 0, attacker).Accepted);

        AssertAttackBonusSemantic(game, "萨拉丁", firstSaladin.InstanceId, attacker, 1000,
            expectedTroops: 6000, expectedOutcome: "〈图坦卡蒙〉本次进攻兵力+1000");
        Assert.Single(game.State.Events, item => item.PlayerLogSemantic?.SourceName == "萨拉丁"
            && item.PlayerLogSemantic.TargetInstanceId == attacker.InstanceId);
    }

    [Fact]
    public void NonAdjacentSaladinDoesNotAddTroopsOrPublishAnAttackBuffEvent()
    {
        var game = Create(0, "S01-04M2", 26101733);
        var attacker = Card("S01-0207", "saladin-log-non-adjacent-attacker");
        L12DerivedStats.SetUntilTurnEnd(attacker, 5000, int.MaxValue);
        attacker.SummonRound = 0;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[0].Field[0][2] = Card("S01-0206", "saladin-log-non-adjacent-source");

        Assert.True(AttackMaster(game, 0, attacker).Accepted);

        Assert.Equal(5000, attacker.Troops);
        Assert.DoesNotContain(game.State.Events, item => item.PlayerLogSemantic?.SourceName == "萨拉丁");
        Assert.DoesNotContain(game.State.Events,
            item => item.Text == "萨拉丁使相邻太阳城军团本次进攻兵力+1000");
    }

    [Fact]
    public void LegacyV2SaladinAttackKeepsOneAttackerRawEventWithoutSemanticSource()
    {
        var current = Create(0, "S01-04M2", 26101734);
        var (attacker, saladin) = ArrangeSaladinLine(current, adjacentSourceCount: 1);
        var legacy = RestoreWithoutAttackBuffProtocol(current);
        attacker = Assert.IsType<L12CardInstance>(legacy.State.Players[0].Field[0][1]);

        Assert.True(AttackMaster(legacy, 0, attacker).Accepted);

        Assert.Equal(6000, attacker.Troops);
        var raw = Assert.Single(legacy.State.Events,
            item => item.Text == "萨拉丁使相邻太阳城军团本次进攻兵力+1000");
        Assert.Null(raw.PlayerLogSemantic);
        var onlyAttacker = Assert.Single(raw.Cards);
        Assert.Equal(attacker.InstanceId, onlyAttacker.InstanceId);
        Assert.DoesNotContain(raw.Cards, card => card.InstanceId == saladin.InstanceId);
    }

    [Fact]
    public void TenkaFubuAttackBonusPublishesNamedSourceTargetAndPlusOneThousand()
    {
        var game = Create(0, "S01-04M2", 26101720);
        var sword = ArrangeFrontSword(game, 0, "tenka-log-sword");
        game.State.Players[0].UsedAbilities.Add($"s2-tenka-front-attack:{game.State.TurnSerial}");

        Assert.True(AttackMaster(game, 0, sword).Accepted);

        AssertAttackBonusSemantic(game, "天下布武", null, sword, 1000, expectedTroops: 6000,
            expectedOutcome: "〈草薙剑〉本次前排进攻兵力+1000");
    }

    [Fact]
    public void TsukuyomiAttackBonusPublishesMasterSourceTargetAndAccumulatedValue()
    {
        var game = Create(0, "S02-04M1", 26101721);
        var sword = ArrangeFrontSword(game, 0, "tsukuyomi-log-sword");
        sword.TsukuyomiFrontMoveBonusCount = 2;
        sword.TsukuyomiFrontMoveBonusTurn = game.State.TurnSerial;

        Assert.True(AttackMaster(game, 0, sword).Accepted);

        AssertAttackBonusSemantic(game, "月读", "master-0", sword, 2000, expectedTroops: 7000,
            expectedOutcome: "〈草薙剑〉本次进攻兵力+2000");
    }

    [Fact]
    public void CardAttackOnlyBonusPublishesCardSourceTargetAndSignedValue()
    {
        var game = Create(0, "S01-04M2", 26101722);
        var sword = ArrangeFrontSword(game, 0, "card-bonus-log-sword");
        sword.AttackOnlyTroopsBonus = 1500;
        sword.AttackOnlyTroopsBonusUntilTurn = game.State.TurnSerial;

        Assert.True(AttackMaster(game, 0, sword).Accepted);

        AssertAttackBonusSemantic(game, "草薙剑", sword.InstanceId, sword, 1500, expectedTroops: 6500,
            expectedOutcome: "〈草薙剑〉本次进攻兵力+1500");
    }

    [Fact]
    public void NegativeCardAttackOnlyModifierUsesADecreaseLabelAndCanonicalMinusSign()
    {
        var game = Create(0, "S01-04M2", 26101723);
        var sword = ArrangeFrontSword(game, 0, "card-penalty-log-sword");
        sword.AttackOnlyTroopsBonus = -1000;
        sword.AttackOnlyTroopsBonusUntilTurn = game.State.TurnSerial;

        Assert.True(AttackMaster(game, 0, sword).Accepted);

        Assert.Equal(4000, sword.Troops);
        var entry = Assert.Single(game.State.Events, item =>
            item.PlayerLogSemantic?.SourceInstanceId == sword.InstanceId
            && item.PlayerLogSemantic.TargetInstanceId == sword.InstanceId
            && item.PlayerLogSemantic.OutcomeLabel.Contains("-1000", StringComparison.Ordinal));
        Assert.Equal("触发 进攻兵力修正", entry.PlayerLogSemantic?.ActionLabel);
        Assert.Equal("〈草薙剑〉本次进攻兵力-1000", entry.PlayerLogSemantic?.OutcomeLabel);
        Assert.DoesNotContain("+-1000", entry.PlayerLogSemantic?.OutcomeLabel, StringComparison.Ordinal);
    }

    private static void AssertAttackBonusSemantic(L12GameEngine game, string sourceName,
        string? sourceInstanceId, L12CardInstance target, int bonus, int expectedTroops, string expectedOutcome)
    {
        Assert.Equal(expectedTroops, target.Troops);
        var entry = Assert.Single(game.State.Events, item => item.PlayerLogSemantic?.SourceName == sourceName
            && item.PlayerLogSemantic.TargetInstanceId == target.InstanceId
            && item.PlayerLogSemantic.OutcomeLabel.Contains($"+{bonus}", StringComparison.Ordinal));
        Assert.Equal(sourceInstanceId, entry.PlayerLogSemantic?.SourceInstanceId);
        Assert.Equal(target.Name, entry.PlayerLogSemantic?.TargetName);
        Assert.Equal("触发 进攻加兵力", entry.PlayerLogSemantic?.ActionLabel);
        Assert.Equal(expectedOutcome, entry.PlayerLogSemantic?.OutcomeLabel);
    }

    private static void AssertPublicSemantic(L12GameSnapshot snapshot, long sequence,
        L12PlayerLogSemantic expected)
    {
        var entry = Assert.Single(snapshot.RecentEvents, item => item.Sequence == sequence);
        Assert.Equal(expected, entry.PlayerLogSemantic);
    }

    private static L12GameEngine Create(int controller, string masterId, int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var controllerDeck = new L12PresetDeckDefinition
        {
            Name = $"{masterId}日志测试牌库",
            MasterId = masterId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var decks = controller == 0
            ? new[] { controllerDeck, baseDeck }
            : new[] { baseDeck, controllerDeck };
        var game = new L12GameEngine(Catalog, "susano-log", "SUSANOLOG", seed,
            ["甲", "乙"], decks, skipPreparation: true, autoPassEmptyResponses: false,
            stateFormatVersion: 2, utcNow: () => FixedUtcNow);
        Assert.Equal(2, game.State.StateFormatVersion);
        game.State.Round = 2;
        game.State.ActivePlayer = controller;
        game.State.Phase = L12Phase.Main;
        game.State.Players[1 - controller].Hand.Add(Card("S01-0001", "susano-private"));
        return game;
    }

    private static L12CardInstance ArrangeFrontSword(L12GameEngine game, int controller, string instanceId)
    {
        var sword = Card("S01-0417", instanceId);
        L12DerivedStats.SetUntilTurnEnd(sword, 5000, int.MaxValue);
        sword.SummonRound = 0;
        game.State.Players[controller].Field[0][0] = sword;
        return sword;
    }

    private static (L12CardInstance Attacker, L12CardInstance FirstSaladin) ArrangeSaladinLine(
        L12GameEngine game, int adjacentSourceCount)
    {
        Assert.InRange(adjacentSourceCount, 1, 2);
        var attacker = Card("S01-0207", $"saladin-log-attacker-{adjacentSourceCount}");
        L12DerivedStats.SetUntilTurnEnd(attacker, 5000, int.MaxValue);
        attacker.SummonRound = 0;
        var firstSaladin = Card("S01-0206", $"saladin-log-left-{adjacentSourceCount}");
        game.State.Players[0].Field[0][0] = firstSaladin;
        game.State.Players[0].Field[0][1] = attacker;
        if (adjacentSourceCount == 2)
            game.State.Players[0].Field[0][2] = Card("S01-0206", "saladin-log-right-2");
        return (attacker, firstSaladin);
    }

    private static CommandResult AttackMaster(L12GameEngine game, int controller, L12CardInstance attacker)
        => game.Handle(controller, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master", $"master-{1 - controller}")));

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
            TrialValue = definition.TrialValue ?? 0,
        };
    }

    private static void AddMorale(L12PlayerState player, int count)
    {
        while (player.Morale.Count < count)
        {
            var morale = player.MoraleDeck[0];
            player.MoraleDeck.RemoveAt(0);
            morale.Tapped = false;
            player.Morale.Add(morale);
        }
    }

    private static void PassResponses(L12GameEngine game)
    {
        while (game.State.PendingPrompts.FirstOrDefault()?.Kind == "response")
        {
            var prompt = game.State.PendingPrompts[0];
            var result = game.Handle(prompt.PlayerIndex,
                new L12Command("resolvePrompt", PromptId: prompt.PromptId, Choice: "pass"));
            Assert.True(result.Accepted, result.Error);
        }
    }

    private static L12GameEngine RestoreExactV2(L12GameEngine game)
    {
        Assert.True(game.RandomState.HasValue);
        var randomState = game.RandomState.Value;
        var revision = game.State.Revision;
        var factSequence = game.CardFactSignalSequence;
        var stateHash = game.ComputeStateHash();
        var rawState = game.SerializeFullState();

        var restored = L12GameEngine.RestoreCheckpoint(Catalog, rawState, randomState, factSequence,
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
            utcNow: () => FixedUtcNow);

        Assert.Equal(2, restored.State.StateFormatVersion);
        Assert.Equal(rawState, restored.SerializeFullState());
        Assert.Equal(stateHash, restored.ComputeStateHash());
        Assert.Equal(randomState, restored.RandomState);
        Assert.Equal(revision, restored.State.Revision);
        Assert.Equal(factSequence, restored.CardFactSignalSequence);
        return restored;
    }

    private static L12GameEngine RestoreWithoutAttackBuffProtocol(L12GameEngine game)
    {
        Assert.True(game.State.AttackBuffLogProtocolEnabled);
        Assert.True(game.RandomState.HasValue);
        var checkpoint = JsonNode.Parse(game.SerializeFullState())!.AsObject();
        Assert.True(checkpoint.Remove(nameof(L12GameState.AttackBuffLogProtocolEnabled)));
        var legacyRawState = checkpoint.ToJsonString();
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, legacyRawState, game.RandomState.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, utcNow: () => FixedUtcNow);
        Assert.False(restored.State.AttackBuffLogProtocolEnabled);
        Assert.Equal(legacyRawState, restored.SerializeFullState());
        return restored;
    }
}
