using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class StructuredAttackRuleConsumerTests
{
    private static readonly string[] CannotAttackCardIds =
        ["S01-0004", "S02-0005", "S02-0007", "S02-0201", "S02-0603"];
    private static readonly string[] CannotSupportCardIds = ["S01-0004", "S02-0201"];

    public static TheoryData<string> CannotAttackCards => new(CannotAttackCardIds);

    public static TheoryData<string> CannotSupportCards => new(CannotSupportCardIds);

    private static L12Catalog Catalog => L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    private static L12GameEngine Create(int seed = 69201)
        => new(Catalog, "structured-attack-rule", "ATTACK-RULE", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true);

    private static L12GameEngine CreateWithFirstMaster(string masterId, int seed)
    {
        var baseDeck = Catalog.DeckAt(0);
        var deck = new L12PresetDeckDefinition
        {
            Name = $"{masterId}结构规则测试牌库",
            MasterId = masterId,
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [],
        };
        return new L12GameEngine(Catalog, "structured-attack-rule", "ATTACK-RULE", seed,
            ["甲", "乙"], [deck, baseDeck], skipPreparation: true);
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
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = 0,
        };
    }

    private static L12CardInstance PrepareAttacker(L12GameEngine game, string cardId)
    {
        foreach (var player in game.State.Players)
            for (var row = 0; row < player.Field.Length; row++)
                Array.Fill(player.Field[row], null);
        var attacker = Card(cardId, $"attacker-{cardId}");
        game.State.Players[0].Field[0][0] = attacker;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        return attacker;
    }

    [Fact]
    public void SquireCannotAttackMasterButCanStillAttackALegion()
    {
        var masterGame = Create();
        var squire = PrepareAttacker(masterGame, "S02-0609");
        Assert.True(L12StructuredCardRules.CombatProfile(squire, 0).CannotAttackMaster);
        Assert.False(masterGame.SnapshotFor(0).LegalAttackTargets.ContainsKey(squire.InstanceId));

        var masterAttack = masterGame.Handle(0, new L12Command("attack", squire.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.False(masterAttack.Accepted);
        Assert.Contains("无法进攻主宰", masterAttack.Error);
        Assert.False(squire.Tapped);
        Assert.Equal(0, squire.AttacksThisTurn);
        Assert.Equal(L12Phase.Main, masterGame.State.Phase);

        var legionGame = Create(69202);
        squire = PrepareAttacker(legionGame, "S02-0609");
        var target = Card("S02-0004", "squire-legion-target");
        legionGame.State.Players[1].Field[0][0] = target;
        Assert.Contains(target.InstanceId, legionGame.SnapshotFor(0).LegalAttackTargets[squire.InstanceId]);
        Assert.DoesNotContain("master", legionGame.SnapshotFor(0).LegalAttackTargets[squire.InstanceId]);
        var legionAttack = legionGame.Handle(0, new L12Command("attack", squire.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId)));
        Assert.True(legionAttack.Accepted, legionAttack.Error);
    }

    [Fact]
    public void AnotherTrialLegionWithoutTheRuleCanAttackMaster()
    {
        var game = Create(69203);
        var percival = PrepareAttacker(game, "S02-0606");

        Assert.False(L12StructuredCardRules.CombatProfile(percival, 0).CannotAttackMaster);
        Assert.Contains("master", game.SnapshotFor(0).LegalAttackTargets[percival.InstanceId]);
        var result = game.Handle(0, new L12Command("attack", percival.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.True(result.Accepted, result.Error);
    }

    [Fact]
    public void NephthysTombGuardRestrictionRemainsIndependentOfTheAttackersOwnRule()
    {
        var game = CreateWithFirstMaster("S02-02M1", 69204);
        var guard = PrepareAttacker(game, "S01-0212");
        Assert.False(L12StructuredCardRules.CombatProfile(guard, 0).CannotAttackMaster);

        var result = game.Handle(0, new L12Command("attack", guard.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.False(result.Accepted);
        Assert.Contains("奈芙蒂斯", result.Error);
    }

    [Fact]
    public void OwnCannotAttackMasterRuleStillAppliesAfterV2Recovery()
    {
        var game = Create(69205);
        var squire = PrepareAttacker(game, "S02-0609");
        var random = game.RandomState ?? new L12RandomState(1, 1, 2, 3, 4, 0);
        game = L12GameEngine.RestoreCheckpoint(Catalog,
            game.SerializeFullState().Insert(1, "\"StateFormatVersion\":2,"), random,
            game.CardFactSignalSequence, autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false);
        squire = game.State.Players[0].Field[0][0]!;

        var result = game.Handle(0, new L12Command("attack", squire.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.False(result.Accepted);
        Assert.Contains("无法进攻主宰", result.Error);
        Assert.False(squire.Tapped);
    }

    [Fact]
    public void UnconditionalAttackRestrictionInventoryMatchesTheCurrentCardPool()
    {
        string[] CardsWith(string parameter) => Catalog.AtomicEffects.All
            .Where(card => card.Abilities.Any(ability => ability.ExecutionModel == "continuous"
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.AttackRule
                    && atom.Parameters.GetValueOrDefault(parameter) == "true")))
            .Select(card => card.CardId)
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(CannotAttackCardIds.OrderBy(cardId => cardId), CardsWith("cannotAttack"));
        Assert.Equal(CannotSupportCardIds.OrderBy(cardId => cardId), CardsWith("cannotSupport"));
    }

    [Theory]
    [MemberData(nameof(CannotAttackCards))]
    public void PrintedCannotAttackRuleUsesTheCommonAttackGate(string cardId)
    {
        var game = Create(69210 + cardId[^1]);
        var attacker = PrepareAttacker(game, cardId);

        var result = game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("master")));

        Assert.False(result.Accepted);
        Assert.Contains("不能进攻", result.Error);
        Assert.False(attacker.Tapped);
    }

    [Fact]
    public void PrintedCannotSupportRuleUsesTheCommonDefenseGate()
    {
        const string cardId = "S02-0201";
        var game = Create(69220 + cardId[^1]);
        foreach (var player in game.State.Players)
            for (var row = 0; row < player.Field.Length; row++)
                Array.Fill(player.Field[row], null);
        var attacker = Card("S02-0004", $"support-attacker-{cardId}");
        attacker.Troops = 3000;
        var target = Card("S02-0004", $"support-target-{cardId}");
        target.Troops = 1000;
        var supporter = Card(cardId, $"supporter-{cardId}");
        supporter.Troops = 5000;
        var cooperative = Card("ST04-07", $"cooperative-support-{cardId}");
        cooperative.Troops = 5000;
        game.State.Players[0].Field[0][0] = attacker;
        game.State.Players[1].Field[0][0] = target;
        game.State.Players[1].Field[1][0] = supporter;
        game.State.Players[1].Field[1][2] = cooperative;
        game.State.ActivePlayer = 0;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        Assert.True(L12StructuredCardRules.CannotSupport(supporter, 1));
        Assert.True(L12StructuredCardRules.HasCooperativeSupport(cooperative, 1));
        Assert.False(L12StructuredCardRules.CannotSupport(cooperative, 1));

        Assert.True(game.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", target.InstanceId))).Accepted);
        Assert.Equal(L12CombatStage.DefenseChoice, game.State.PendingDefense?.Stage);
        var result = game.Handle(1, new L12Command("resolveDefense",
            SupportInstanceId: supporter.InstanceId));

        Assert.False(result.Accepted);
        Assert.Contains("无法支援", result.Error);
    }

    [Fact]
    public void HannibalAttackTargetRestrictionReadsItsReadyCondition()
    {
        var activeGame = Create(69230);
        var attacker = PrepareAttacker(activeGame, "S02-0004");
        var activeHannibal = Card("S02-0516", "active-hannibal-target");
        activeGame.State.Players[1].Field[0][0] = activeHannibal;
        Assert.True(L12StructuredCardRules.CannotBeAttacked(activeHannibal, 0));

        var activeResult = activeGame.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", activeHannibal.InstanceId)));
        Assert.False(activeResult.Accepted);
        Assert.Contains("无法被进攻", activeResult.Error);

        var restedGame = Create(69231);
        attacker = PrepareAttacker(restedGame, "S02-0004");
        var restedHannibal = Card("S02-0516", "rested-hannibal-target");
        restedHannibal.Tapped = true;
        restedGame.State.Players[1].Field[0][0] = restedHannibal;
        Assert.False(L12StructuredCardRules.CannotBeAttacked(restedHannibal, 0));

        var restedResult = restedGame.Handle(0, new L12Command("attack", attacker.InstanceId,
            Target: new L12AttackTarget("legion", restedHannibal.InstanceId)));
        Assert.True(restedResult.Accepted, restedResult.Error);
    }
}
