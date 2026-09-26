using Xunit;

namespace TwelveLegions.Tests;

public sealed class LongChainAllCardCoverageTests
{
    public static IEnumerable<object[]> Cases()
    {
        var cardFilter = Environment.GetEnvironmentVariable("L12_LONG_CHAIN_CARD");
        if (!string.IsNullOrWhiteSpace(cardFilter)
            && !LongChainAllCardHarness.Catalog.Cards.ContainsKey(cardFilter))
            throw new InvalidOperationException($"L12_LONG_CHAIN_CARD 不是权威卡牌 ID：{cardFilter}");
        var shardText = Environment.GetEnvironmentVariable("L12_LONG_CHAIN_SHARD");
        int? shard = string.IsNullOrWhiteSpace(shardText) ? null
            : int.TryParse(shardText, out var parsed) && parsed is >= 0 and < LongChainAllCardHarness.ShardCount
                ? parsed : throw new InvalidOperationException("L12_LONG_CHAIN_SHARD 必须是 0..7");
        foreach (var testCase in LongChainAllCardHarness.Inventory()
                     .Where(item => string.IsNullOrWhiteSpace(cardFilter) || item.CardId == cardFilter)
                     .Where(item => shard is null || item.Shard == shard))
            yield return [testCase];
    }

    [Fact]
    [Trait("L12Evidence", "lc02-all-card-inventory")]
    public void AuthoritativeInventoryAndStableShardsAreExact()
    {
        var inventory = LongChainAllCardHarness.Inventory();
        Assert.Equal(324, inventory.Count);
        Assert.Equal(324, inventory.Select(item => item.CardId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(LongChainAllCardHarness.Catalog.Cards.Keys.Order(StringComparer.Ordinal),
            inventory.Select(item => item.CardId));
        Assert.All(inventory, item =>
        {
            Assert.Equal(LongChainAllCardHarness.StableSeed(item.CardId), item.Seed);
            Assert.Equal(LongChainAllCardHarness.StableShard(item.CardId), item.Shard);
            Assert.InRange(item.Shard, 0, 7);
        });
        var shards = Enumerable.Range(0, 8)
            .Select(index => inventory.Where(item => item.Shard == index).Select(item => item.CardId).ToHashSet(StringComparer.Ordinal))
            .ToArray();
        Assert.Equal(324, shards.SelectMany(item => item).Distinct(StringComparer.Ordinal).Count());
        for (var left = 0; left < shards.Length; left++)
        for (var right = left + 1; right < shards.Length; right++)
            Assert.Empty(shards[left].Intersect(shards[right], StringComparer.Ordinal));
        for (var index = 0; index < 8; index++)
        {
            var ordered = inventory.Where(item => item.Shard == index).Select(item => item.CardId).ToArray();
            Assert.Equal(ordered.Order(StringComparer.Ordinal), ordered);
        }
    }

    [Theory]
    [MemberData(nameof(Cases), DisableDiscoveryEnumeration = true)]
    [Trait("L12Evidence", "lc02-all-card-checkpoint-replay")]
    public async Task EveryCardSurvivesTwoCheckpointCutpoints(LongChainAllCardCase testCase)
    {
        var evidence = await LongChainAllCardHarness.RunAsync(testCase);
        Assert.True(evidence.AcceptedSteps >= 6);
        Assert.True(evidence.RejectedSteps >= 3);
        Assert.True(evidence.Cutpoints.Length >= 3);
        Assert.False(string.IsNullOrWhiteSpace(evidence.FocusEvidence));
    }
}
