using System.Globalization;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class CardCombatProjectionAllocationTests
{
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void RepeatedFourFlagProjectionDoesNotRebuildRuleDeclarations()
    {
        var card = Card("S02-0002", "弓手");
        for (var index = 0; index < 256; index++) _ = Flags(card);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++) _ = Flags(card);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated <= 8192, $"Repeated flags allocated {allocated} bytes; expected <= 8192");
    }

    [Fact]
    public void AllCatalogFlagsMatchFreshProfilesAcrossKeysAndReadinessChanges()
    {
        foreach (var definition in Catalog.Cards.Values)
        foreach (var profession in new string?[] { null, definition.Profession, "弓手", "术师", "步兵", "弓手 " }.Distinct(StringComparer.Ordinal))
        {
            var card = Card(definition.Id, profession);
            foreach (var tapped in new[] { false, true, false })
            {
                card.Tapped = tapped;
                var expected = FreshFlags(card);
                Assert.Equal(expected, Flags(card));
                _ = Flags(Card("synthetic-unrecognized", "术师"));
                Assert.Equal(expected, Flags(card)); // A -> B -> A must not reuse B.
                card.Troops += 1;
                card.CostModifier -= 1;
                card.StatusIcons.Add("synthetic-status");
                Assert.Equal(expected, Flags(card));
            }
        }
        Assert.Equal(FreshFlags(Card("s02-0002", "弓手 ")), Flags(Card("s02-0002", "弓手 ")));
    }

    [Fact]
    public void ThreadsCannotBorrowAnotherThreadsSummary()
    {
        using var barrier = new Barrier(2);
        var failures = new Exception?[2];
        var threads = Enumerable.Range(0, 2).Select(worker => new Thread(() =>
        {
            try
            {
                var card = Card("synthetic-thread", worker == 0 ? "弓手" : "步兵");
                var expected = FreshFlags(card);
                for (var index = 0; index < 100; index++)
                {
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(5)));
                    Assert.Equal(expected, Flags(card));
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(5)));
                }
            }
            catch (Exception error) { failures[worker] = error; }
        }) { IsBackground = true }).ToArray();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.All(failures, failure => Assert.Null(failure));
    }

    [Fact]
    public void FullProfilesRemainFreshUnderMutableCultureAndPublicRuleResults()
    {
        var original = CultureInfo.CurrentCulture;
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        try
        {
            CultureInfo.CurrentCulture = culture;
            var card = Catalog.Cards.Values.Select(definition => Card(definition.Id, definition.Profession))
                .First(candidate => L12StructuredCardRules.CombatProfile(candidate, 0).IncomingRangedCombatDamageAdjustment == 1000);
            var expectedFlags = Flags(card);
            Assert.Equal(1000, L12StructuredCardRules.CombatProfile(card, 0).IncomingRangedCombatDamageAdjustment);
            culture.NumberFormat.PositiveSign = "1";
            Assert.Equal(0, L12StructuredCardRules.CombatProfile(card, 0).IncomingRangedCombatDamageAdjustment);
            Assert.Equal(expectedFlags, Flags(card));
            Assert.Equal(FreshFlags(card), Flags(card));
            // Public declaration parameters are caller-owned, never a shared cached graph.
            foreach (var atom in L12StructuredCardRules.GetCombatRuleAbilities(card.CardId).SelectMany(ability => ability.Atoms))
                atom.Parameters.Clear();
            Assert.Equal(expectedFlags, Flags(card));
            Assert.Equal(FreshFlags(card), Flags(card));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void SerializationKeepsItsOriginalFieldsAndNeverChangesEngineState()
    {
        var game = new L12GameEngine(Catalog, "synthetic-flags", "SYNTH", 90210,
            ["甲", "乙"], [0, 1], skipPreparation: true);
        var before = game.SerializeFullState();
        var random = game.RandomState;
        var facts = game.CardFactSignalSequence;
        for (var index = 0; index < 10; index++)
        {
            var snapshot = game.SnapshotFor(index % 2);
            _ = JsonSerializer.Serialize(snapshot);
            Assert.Equal(before, game.SerializeFullState());
        }
        Assert.Equal(random, game.RandomState);
        Assert.Equal(facts, game.CardFactSignalSequence);
        var card = Card("S02-0002", "弓手");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(card));
        var expected = FreshFlags(card);
        Assert.Equal(expected.Range, json.RootElement.GetProperty("HasRangeBonus").GetBoolean());
        Assert.Equal(expected.RangedNoLoss, json.RootElement.GetProperty("HasRangedNoLoss").GetBoolean());
        Assert.Equal(expected.AttackNoLoss, json.RootElement.GetProperty("HasAttackNoLoss").GetBoolean());
        Assert.Equal(expected.CannotBeRanged, json.RootElement.GetProperty("CannotBeRanged").GetBoolean());
    }

    private static L12CardInstance Card(string cardId, string? profession) => new()
    {
        InstanceId = $"synthetic-{cardId}", CardId = cardId, Name = "合成", CardType = "军团",
        Faction = "通用", Profession = profession, Troops = 1000,
    };

    private static (bool Range, bool RangedNoLoss, bool AttackNoLoss, bool CannotBeRanged) Flags(L12CardInstance card)
        => (card.HasRangeBonus, card.HasRangedNoLoss, card.HasAttackNoLoss, card.CannotBeRanged);

    private static (bool Range, bool RangedNoLoss, bool AttackNoLoss, bool CannotBeRanged) FreshFlags(L12CardInstance card)
    {
        var front = L12StructuredCardRules.CombatProfile(card, 0);
        var back = L12StructuredCardRules.CombatProfile(card, 1);
        return (front.HasRangeBonus || back.HasRangeBonus, front.HasRangedNoLoss || back.HasRangedNoLoss,
            front.HasAttackNoLoss || back.HasAttackNoLoss, front.CannotBeRanged || back.CannotBeRanged);
    }
}
