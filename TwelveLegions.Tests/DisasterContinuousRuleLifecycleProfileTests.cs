using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class DisasterContinuousRuleLifecycleProfileTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Theory]
    [InlineData("S01-DS01")]
    [InlineData("S01-DS02")]
    [InlineData("S01-DS03")]
    [InlineData("S01-DS04")]
    [InlineData("S01-DS08")]
    [InlineData("S01-DS10")]
    [InlineData("S02-DS01")]
    [InlineData("S02-DS02")]
    [InlineData("S02-DS03")]
    [InlineData("S02-DS04")]
    [InlineData("S02-DS05")]
    [InlineData("S02-DS06")]
    [L12AbilityEvidence("S01-DS01:ability:static:9b5681c438931452", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-DS02:ability:static:4408d437a8ab5e5a", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-DS03:ability:static:70004a014a03d2a0", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-DS04:ability:static:017c7359962a2512", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-DS04:ability:attack:68f2ff0b600a41e8", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-DS08:ability:static:3e7cd5724f09420c", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S01-DS10:ability:static:33501d2503c08b73", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-DS01:ability:static:31558cb4f3e2c3da", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-DS02:ability:static:01aeea1f7fc7e317", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-DS03:ability:continuous:fed4f60f2af1523c", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-DS04:ability:static:00575cc9fcb1aaaa", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-DS05:ability:static:335d304b639c5d3f", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-DS05:ability:attack:4326fa5eef9e6e3a", "normal", "reconnect", "presentation-consumers")]
    [L12AbilityEvidence("S02-DS06:ability:static:c1632b7b22b87c4f", "normal", "reconnect", "presentation-consumers")]
    public void CurrentRuleAndPublicDisasterProjectionSurviveCheckpoint(string cardId)
    {
        var game = Create(70300 + cardId[^1]);
        game.State.ActiveDisaster = Card(cardId, $"active-{cardId}");

        AssertRegisteredRule(cardId);
        Assert.Contains(cardId, JsonSerializer.Serialize(game.SnapshotFor(0)), StringComparison.Ordinal);
        Assert.Contains(cardId, JsonSerializer.Serialize(game.SnapshotForSpectator()), StringComparison.Ordinal);

        game = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: true, concealHiddenResponseAvailability: false);

        Assert.Equal(cardId, game.State.ActiveDisaster?.CardId);
        AssertRegisteredRule(cardId);
        Assert.Contains(cardId, JsonSerializer.Serialize(game.SnapshotFor(1)), StringComparison.Ordinal);
    }

    private static void AssertRegisteredRule(string cardId)
    {
        Assert.True(L12ActiveDisasterRules.HasRegisteredContinuousRule(cardId));
        switch (cardId)
        {
            case "S01-DS01":
                Assert.Equal(L12ActiveDisasterRules.DarkMorningStarCardId, cardId);
                break;
            case "S01-DS02":
                Assert.True(L12ActiveDisasterRules.DisasterLegionMasterDamageBonus(cardId));
                break;
            case "S01-DS03":
                Assert.True(L12ActiveDisasterRules.ForbidsBackRowLegionPlacement(cardId));
                Assert.True(L12ActiveDisasterRules.CounterTacticsAreFree(cardId));
                break;
            case "S01-DS04":
                Assert.True(L12ActiveDisasterRules.HighTroopsAttackRollsDice(cardId));
                break;
            case "S01-DS08":
                Assert.True(L12ActiveDisasterRules.RelicEffectUseDamagesMaster(cardId));
                break;
            case "S01-DS10":
                Assert.True(L12ActiveDisasterRules.DisasterValueLocked(cardId));
                break;
            case "S02-DS01":
                Assert.True(L12ActiveDisasterRules.LibraryFlipped(cardId));
                Assert.True(L12ActiveDisasterRules.HandLegionBlockedMatchingLibraryTop(cardId));
                break;
            case "S02-DS02":
                Assert.True(L12ActiveDisasterRules.ActiveFrontRowUnattackable(cardId));
                Assert.True(L12ActiveDisasterRules.MasterUnattackableByTroopsAtMost2000(cardId));
                Assert.True(L12ActiveDisasterRules.TauntSuppressed(cardId));
                break;
            case "S02-DS03":
                Assert.True(L12ActiveDisasterRules.ActiveRestUseDamagesMaster(cardId));
                break;
            case "S02-DS04":
                Assert.True(L12ActiveDisasterRules.RangedLegionsCannotRangedAttack(cardId));
                break;
            case "S02-DS05":
                Assert.True(L12ActiveDisasterRules.MustAttackLegionBeforeMaster(cardId));
                break;
            case "S02-DS06":
                Assert.True(L12ActiveDisasterRules.MasterEffectCostsExtraMorale(cardId));
                Assert.True(L12ActiveDisasterRules.HandLegionEntryCostsExtra(cardId));
                break;
            default:
                throw new Xunit.Sdk.XunitException($"未审计的持续天灾：{cardId}");
        }
    }

    private static L12GameEngine Create(int seed)
        => new(Catalog, "disaster-continuous-rule", "DISASTER-RULE", seed,
            ["甲", "乙"], [0, 0], skipPreparation: true, stateFormatVersion: 2);

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
            EffectText = definition.Effect,
            DisasterLevel = definition.DisasterLevel ?? 0,
        };
    }
}
