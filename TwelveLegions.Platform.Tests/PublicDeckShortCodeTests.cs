using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckShortCodeTests
{
    [Fact]
    public void ExistingPublishedRowsReceiveOneStableShortCodeDuringSchemaUpgrade()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-public-code-upgrade-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var ownerLogin = store.Register("tcodeup1", "password-123");
            Assert.True(ownerLogin.Success, ownerLogin.Message);
            var owner = ownerLogin.Account!;
            var published = store.PublishDeck(owner.Id, new L12PresetDeckDefinition
            {
                Name = "待迁移公开牌库", MasterId = "S01-01M1", CardIds = ["S01-0101"],
                MoraleIds = ["S01-01C1"], SpecialIds = [],
            }, null)!;
            using (var connection = new SqliteConnection($"Data Source={store.TransactionalStoragePath};Mode=ReadWrite;Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DROP INDEX ux_published_decks_public_code; UPDATE published_decks SET public_code=NULL;";
                command.ExecuteNonQuery();
            }

            var upgraded = new L12PlatformStore(path);
            var migrated = upgraded.PublishedDeck(published.Id, null)!;
            Assert.Matches("^[23456789ABCDEFGHJKMNPQRSTVWXYZ]{12}$", migrated.PublicCode);
            Assert.Equal(migrated.PublicCode, new L12PlatformStore(path).PublishedDeck(published.Id, null)!.PublicCode);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FrontendL12D2FixtureDecodesOnServerAndRetiredL12D1DoesNot()
    {
        const string code = "L12D2-B2N7M-ZM2AW-YRPWG-MNDZS-23KMT-KP73X-24KMZ-E6PWG-F8B33-38MT7-ERQFT-V";
        Assert.True(L12DeckCodeCodec.TryDecode(code.ToLowerInvariant(), out var decoded));
        Assert.Equal("违规赛事牌库", decoded!.Name);
        Assert.Equal("S01-01M1", decoded.MasterId);
        Assert.Equal(["S01-0001", "S01-0001"], decoded.CardIds);
        Assert.False(L12DeckCodeCodec.TryDecode(code[..^1] + "2", out _));
        Assert.False(L12DeckCodeCodec.TryDecode("L12D1.eyJ2IjoxfQ", out _));
    }

    [Fact]
    public void PublicCodesAreOpaqueUniquePersistentAndResolveAlongsideLegacyIds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"l12-public-code-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var owner = store.Register("tcodeowner", "password-123").Account!;
            var reader = store.Register("tcodereader", "password-123").Account!;
            var published = Enumerable.Range(0, 128).Select(index => store.PublishDeck(owner.Id,
                new L12PresetDeckDefinition
                {
                    Name = $"短码牌库 {index}", MasterId = "S02-05M2", CardIds = [$"S02-{index:D4}"],
                    MoraleIds = ["S02-05C1"], SpecialIds = [],
                }, null)!).ToArray();

            Assert.Equal(128, published.Select(item => item.PublicCode).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(published, item => Assert.Matches(new Regex("^[23456789ABCDEFGHJKMNPQRSTVWXYZ]{12}$"), item.PublicCode));
            var target = published[37];
            Assert.Equal(target.Id, store.PublishedDeck(target.Id, null)!.Id);
            Assert.Null(store.PublishedDeckByPublicCode(target.Id, null));
            Assert.Equal(target.Id, store.PublishedDeck(target.PublicCode.ToLowerInvariant(), null)!.Id);
            var grouped = string.Join('-', target.PublicCode.Chunk(4).Select(chars => new string(chars)));
            Assert.Equal(target.Id, store.PublishedDeck(grouped, null)!.Id);
            Assert.True(store.TogglePublishedDeckLike(reader.Id, target.PublicCode)!.Liked);
            Assert.Equal(1, store.RecordPublishedDeckView(target.PublicCode.ToLowerInvariant(), reader.Id)!.Views);
            Assert.Equal(1, store.RecordPublishedDeckCopy(grouped, reader.Id)!.Copies);
            Assert.NotNull(store.UpdatePublicDeckContent(owner.Id, target.PublicCode,
                new L12PublicDeckContentInput(new("短码指南", "", "", "", ""), [])));

            var restored = new L12PlatformStore(path);
            var byLegacyId = restored.PublishedDeck(target.Id, reader.Id)!;
            var byShortCode = restored.PublishedDeck(target.PublicCode, reader.Id)!;
            Assert.Equal(target.PublicCode, byLegacyId.PublicCode);
            Assert.Equal(byLegacyId.Id, byShortCode.Id);
            Assert.Equal(byLegacyId.Deck.CardIds, byShortCode.Deck.CardIds);
            Assert.Equal(1, byShortCode.Views);
            Assert.Equal(1, byShortCode.Copies);
            Assert.True(byShortCode.Liked);
            Assert.Equal("短码指南", restored.PublicDeckDetails(target.PublicCode)!.Guide.BuildIdea);
            Assert.False(restored.DeletePublishedDeck(reader.Id, target.PublicCode));
            Assert.True(restored.DeletePublishedDeck(owner.Id, target.PublicCode));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
