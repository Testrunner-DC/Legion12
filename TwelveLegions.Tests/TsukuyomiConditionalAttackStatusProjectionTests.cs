using System.Reflection;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TsukuyomiConditionalAttackStatusProjectionTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-18T05:00:00Z");

    public static IEnumerable<object[]> DisplayCases()
    {
        foreach (var owner in new[] { 0, 1 })
        foreach (var count in new[] { 1, 2 })
        foreach (var restore in new[] { false, true })
            yield return [owner, count, restore];
    }

    [Theory]
    [MemberData(nameof(DisplayCases))]
    public void EveryAllowedViewShowsCurrentAccumulationWithoutChangingTroopsOrAuthority(
        int owner, int count, bool restore)
    {
        var game = Create(owner);
        var card = Arrange(game, owner, count, game.State.TurnSerial);
        if (restore) game = Restore(game);
        card = Assert.IsType<L12CardInstance>(game.State.Players[owner].Field[0][0]);
        var before = game.SerializeFullState();
        var hash = game.ComputeStateHash();
        var random = game.RandomState;
        var facts = game.CardFactSignalSequence;
        for (var repetition = 0; repetition < 2; repetition++)
        foreach (var snapshot in Views(game, owner))
        {
            var projected = ProjectedCard(snapshot, owner);
            var status = Assert.Single(Statuses(projected), item => item.Kind == "conditional-attack");
            Assert.Equal($"本回合每次进攻兵力＋{count * 1000}", status.Label);
            Assert.Equal("月读", status.Source);
            Assert.Equal(card.BaseTroops, projected.GetProperty("baseTroops").GetInt32());
            Assert.Equal(card.Troops, projected.GetProperty("troops").GetInt32());
            Assert.Equal(hash, snapshot.StateHash);
        }
        Assert.Equal(before, game.SerializeFullState());
        Assert.Equal(hash, game.ComputeStateHash());
        Assert.Equal(random, game.RandomState);
        Assert.Equal(facts, game.CardFactSignalSequence);
        Assert.Empty(card.StatusEffects);
        Assert.Equal(card.BaseTroops, card.Troops);
        Assert.Equal(count, card.TsukuyomiFrontMoveBonusCount);
    }

    [Theory]
    [InlineData(0, 0, 10, false)] [InlineData(1, 0, 10, false)]
    [InlineData(0, 2, 9, false)] [InlineData(1, 2, 9, false)]
    [InlineData(0, 2, 11, false)] [InlineData(1, 2, 11, false)]
    [InlineData(0, 2, 10, true)] [InlineData(1, 2, 10, true)]
    public void ZeroExpiredFutureAndHiddenBonusesNeverBecomeAVisibleCurrentStatus(
        int owner, int count, int bonusTurn, bool hidden)
    {
        var game = Create(owner);
        Arrange(game, owner, count, bonusTurn).Hidden = hidden;
        foreach (var snapshot in Views(game, owner))
            Assert.DoesNotContain(Statuses(ProjectedCard(snapshot, owner)),
                item => item.Kind == "conditional-attack" || item.Source == "月读");
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void ExistingLeaveFieldResetRemovesTheAccumulationInsteadOfLeavingAStaleDisplay(int owner)
    {
        var game = Create(owner);
        var card = Arrange(game, owner, 2, game.State.TurnSerial);
        Assert.Contains(Statuses(ProjectedCard(game.SnapshotFor(owner), owner)), item => item.Kind == "conditional-attack");
        typeof(L12GameEngine).GetMethod("ResetCardAfterLeavingField", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [card]);
        Assert.Equal(0, card.TsukuyomiFrontMoveBonusCount);
        Assert.Equal(-1, card.TsukuyomiFrontMoveBonusTurn);
        Assert.DoesNotContain(Statuses(ProjectedCard(game.SnapshotFor(owner), owner)), item => item.Kind == "conditional-attack");
    }

    [Fact]
    public void OtherAttackOnlySourcesAreNotRelabelledAsTsukuyomiOrAddedToStandingTroops()
    {
        var game = Create(0);
        var card = Arrange(game, 0, 0, -1);
        card.AttackOnlyTroopsBonus = 2000;
        card.AttackOnlyTroopsBonusUntilTurn = game.State.TurnSerial;
        game.State.Players[0].UsedAbilities.Add($"susano-buff:{card.InstanceId}");
        game.State.Players[0].UsedAbilities.Add($"s2-tenka-front-attack:{game.State.TurnSerial}");
        var projected = ProjectedCard(game.SnapshotFor(0), 0);
        Assert.DoesNotContain(Statuses(projected), item => item.Source == "月读");
        Assert.Equal(card.BaseTroops, projected.GetProperty("troops").GetInt32());
    }

    private static L12GameSnapshot[] Views(L12GameEngine game, int owner)
        => [game.SnapshotFor(owner), game.SnapshotFor(1 - owner), game.SnapshotForSpectator(), game.SnapshotForReferee()];

    private static JsonElement ProjectedCard(L12GameSnapshot snapshot, int owner)
        => JsonSerializer.SerializeToElement(snapshot, WireJson).GetProperty("players")[owner].GetProperty("field")[0][0];

    private static L12StatusEffectView[] Statuses(JsonElement card)
        => card.TryGetProperty("statusEffects", out var effects)
            ? JsonSerializer.Deserialize<L12StatusEffectView[]>(effects, WireJson)! : [];

    private static L12CardInstance Arrange(L12GameEngine game, int owner, int count, int turn)
    {
        var definition = Catalog.Cards["S02-0401"];
        var card = new L12CardInstance
        {
            InstanceId = $"moon-status-{owner}", CardId = definition.Id, Name = definition.NameZh,
            CardType = definition.CardType, Faction = definition.Faction, Cost = definition.Cost ?? 0,
            BaseTroops = definition.Troops ?? 0, Troops = definition.Troops ?? 0, EffectText = definition.Effect,
            Traits = [.. definition.Traits], Profession = definition.Profession, OwnerIndex = owner,
            TsukuyomiFrontMoveBonusCount = count, TsukuyomiFrontMoveBonusTurn = turn,
        };
        game.State.Players[owner].Field[0][0] = card;
        return card;
    }

    private static L12GameEngine Create(int owner)
    {
        var baseline = Catalog.DeckAt(0);
        var moon = new L12PresetDeckDefinition { Name = "月读状态投影", MasterId = "S02-04M1",
            CardIds = [.. baseline.CardIds], MoraleIds = [.. baseline.MoraleIds], SpecialIds = [] };
        var game = new L12GameEngine(Catalog, "moon-status", "MOONSTATUS", 26101815, ["甲", "乙"],
            owner == 0 ? [moon, baseline] : [baseline, moon], skipPreparation: true, disasterMode: "none",
            autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, stateFormatVersion: 2, utcNow: () => Now);
        game.State.ActivePlayer = owner;
        game.State.FirstPlayer = owner;
        game.State.Round = 3;
        game.State.TurnSerial = 10;
        game.State.Phase = L12Phase.Main;
        return game;
    }

    private static L12GameEngine Restore(L12GameEngine game)
        => L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), game.RandomState!.Value,
            game.CardFactSignalSequence, autoPassEmptyResponses: false, concealHiddenResponseAvailability: false, utcNow: () => Now);
}
