using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ReplayOrphanInventoryTests
{
    [Fact]
    public async Task PagesThroughHealthyIdsAndFlagsProtectedOrphansWithoutWriting()
    {
        var path = Path.Combine(Path.GetTempPath(), "l12-orphan-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE matches(match_id TEXT PRIMARY KEY);
                    CREATE TABLE match_action_events(match_id TEXT NOT NULL,event_sequence INTEGER,PRIMARY KEY(match_id,event_sequence));
                    CREATE TABLE ranked_settlement_outbox(match_id TEXT PRIMARY KEY);
                    INSERT INTO matches VALUES('a');
                    INSERT INTO match_action_events VALUES('a',1),('a',2),('b',1),('c',1);
                    INSERT INTO ranked_settlement_outbox VALUES('c');
                    """;
                await command.ExecuteNonQueryAsync();
            }
            var before = await File.ReadAllBytesAsync(path);
            var first = await L12ReplayOrphanInventory.ReadPageAsync(path, "match_action_events", pageSize: 1);
            Assert.Empty(first.Findings);
            Assert.False(first.Complete);
            Assert.Equal("a", first.NextAfterMatchId);
            var second = await L12ReplayOrphanInventory.ReadPageAsync(path, "match_action_events", first.NextAfterMatchId, 2);
            Assert.Collection(second.Findings, item => { Assert.Equal("b", item.MatchId); Assert.False(item.HasRecoveryOrSettlementReference); },
                item => { Assert.Equal("c", item.MatchId); Assert.True(item.HasRecoveryOrSettlementReference); });
            var last = await L12ReplayOrphanInventory.ReadPageAsync(path, "match_action_events", second.NextAfterMatchId);
            Assert.True(last.Complete);
            Assert.Equal(0, last.ScannedMatches);
            Assert.True((await L12ReplayOrphanInventory.ReadPageAsync(path, "match_card_facts")).Complete);
            await Assert.ThrowsAsync<ArgumentException>(() => L12ReplayOrphanInventory.ReadPageAsync(path, "matches; DELETE FROM matches"));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => L12ReplayOrphanInventory.ReadPageAsync(path, "match_events", pageSize: 251));
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MissingDatabaseIsNotCreated()
    {
        var path = Path.Combine(Path.GetTempPath(), "l12-missing-" + Guid.NewGuid().ToString("N") + ".db");
        await Assert.ThrowsAsync<FileNotFoundException>(() => L12ReplayOrphanInventory.ReadPageAsync(path, "match_events"));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task CountsAreBoundedAndExternalHoldsAreExplicitWithoutReadingPayloads()
    {
        var path = Path.Combine(Path.GetTempPath(), "l12-orphan-evidence-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE matches(match_id TEXT PRIMARY KEY);
                    CREATE TABLE match_action_events(match_id TEXT,event_sequence INTEGER,PRIMARY KEY(match_id,event_sequence));
                    WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<1200)
                    INSERT INTO match_action_events SELECT 'large',x FROM n;
                    INSERT INTO match_action_events VALUES('active',1),('bug',1),('unknown',1);
                    """;
                await command.ExecuteNonQueryAsync();
            }
            var before = await File.ReadAllBytesAsync(path);
            var unverified = await L12ReplayOrphanInventory.ReadPageAsync(path, "match_action_events");
            Assert.All(unverified.Findings, row => Assert.Equal("external-references-unchecked", row.Disposition));
            var page = await L12ReplayOrphanInventory.ReadPageAsync(path, "match_action_events",
                activeMatchIds: new[] { "active" }, evidence: new L12ReplayEvidenceReferences(new[] { "bug" }, new[] { "unmapped-room" }));
            var large = Assert.Single(page.Findings, row => row.MatchId == "large");
            Assert.Equal(1001, large.ObservedRows);
            Assert.True(large.RowCountCapped);
            Assert.Equal("protected-reference", Assert.Single(page.Findings, row => row.MatchId == "active").Disposition);
            Assert.Equal("protected-reference", Assert.Single(page.Findings, row => row.MatchId == "bug").Disposition);
            Assert.Equal("requires-room-and-evidence-review", Assert.Single(page.Findings, row => row.MatchId == "unknown").Disposition);
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }
}
