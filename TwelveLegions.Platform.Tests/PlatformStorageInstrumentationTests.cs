using System.Security.Cryptography;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

public sealed class PlatformStorageInstrumentationTests
{
    [Fact]
    public void SyntheticScopeReportsAggregatesAndPreservesDeckRecovery()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var owner = store.Register("measowner", "password-123").Account!;
            var deck = Deck("测量牌库", "C1");

            L12SyntheticStorageMeasurement measurement;
            using (var scope = L12PlatformStore.BeginSyntheticStorageMeasurement())
            {
                store.UpsertDeck(owner.Id, deck);
                measurement = scope.Complete();
            }

            Assert.Equal(1, measurement.Connections);
            Assert.True(measurement.SelectStatements > 0);
            Assert.True(measurement.InsertStatements > 0);
            Assert.True(measurement.UpdateStatements > 0);
            Assert.True(measurement.DeckDomainStatements > 0);
            Assert.True(measurement.InclusiveAffectedRows > 0);
            Assert.True(measurement.TransactionNanoseconds > 0);
            Assert.True(measurement.ProfiledSqlNanoseconds >= 0);
            Assert.True(measurement.SqlitePageWrites > 0);
            Assert.True(measurement.SqlitePagePayloadBytes >= measurement.SqlitePageWrites * 512);
            Assert.True(measurement.MirrorBytes > 0);
            Assert.Equal(0, measurement.InstrumentationErrors);

            var reloaded = new L12PlatformStore(path);
            var restored = Assert.Single(reloaded.Decks(owner.Id));
            Assert.Equal(deck.Name, restored.Name);
            Assert.Equal(deck.CardIds, restored.CardIds);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DirectLikeReportsPointWriteWithoutMirrorOrSnapshotMutation()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var owner = store.Register("measpub", "password-123").Account!;
            var reader = store.Register("measreader", "password-123").Account!;
            var published = store.PublishDeck(owner.Id, Deck("公开测量牌库", "C2"), null)!;
            var mirrorBefore = SHA256.HashData(File.ReadAllBytes(path));

            L12SyntheticStorageMeasurement measurement;
            using (var scope = L12PlatformStore.BeginSyntheticStorageMeasurement())
            {
                Assert.True(store.TogglePublishedDeckLike(reader.Id, published.Id)!.Liked);
                measurement = scope.Complete();
            }

            Assert.Equal(1, measurement.Connections);
            Assert.Equal(0, measurement.MirrorBytes);
            Assert.Equal(0, measurement.PlatformDomainStatements);
            Assert.True(measurement.DeckDomainStatements >= 2);
            Assert.Equal(1, measurement.InclusiveAffectedRows);
            Assert.Equal(0, measurement.InstrumentationErrors);
            Assert.Equal(mirrorBefore, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.True(new L12PlatformStore(path).PublishedDecks(reader.Id).Single().Liked);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SyntheticScopesRejectNestingAndReleaseAmbientState()
    {
        using (var scope = L12PlatformStore.BeginSyntheticStorageMeasurement())
        {
            Assert.Throws<InvalidOperationException>(() => L12PlatformStore.BeginSyntheticStorageMeasurement());
        }
        using var next = L12PlatformStore.BeginSyntheticStorageMeasurement();
        var measurement = next.Complete();
        Assert.Equal(0, measurement.Connections);
    }

    private static L12PresetDeckDefinition Deck(string name, string cardId) => new()
    {
        Name = name,
        MasterId = "M1",
        CardIds = [cardId, cardId],
        MoraleIds = ["R1"],
        SpecialIds = [],
    };

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-platform-measurement-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
