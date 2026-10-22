using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PlayerReplaySummaryProjectionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SummaryReadsExistingPublicMastersWithoutChangingCountsOrPrivateDecks(
        bool missingParticipants, bool missingName)
    {
        var parent = Path.Combine(Path.GetTempPath(), "l12-player-summary-projection");
        var directory = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "matches.db");
        var now = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        var completed = false;
        try
        {
            await using var recorder = new MatchRecorder(path, () => now);
            await recorder.InitializeAsync();
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            var seed = connection.CreateCommand();
            seed.CommandText = """
                INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,
                                    started_utc,ended_utc,mode_id,account_0,account_1,first_player)
                VALUES('summary-owned','QA',1,'甲','乙','PRIVATE_ALPHA','PRIVATE_BETA',
                       $start,$end,'friendly','account-a','account-b',0);
                INSERT INTO match_events(match_id,sequence,received_utc,player_index,
                                         command_json,accepted,revision,state_hash,state_json)
                VALUES('summary-owned',1,$end,0,'{"type":"syntheticAccepted"}',1,1,'first','{}'),
                      ('summary-owned',2,$end,0,'{"type":"syntheticRejected"}',0,1,'first','{}'),
                      ('summary-owned',3,$end,-1,'{"type":"authorityProgress"}',1,1,'first','{}');
                """;
            seed.Parameters.AddWithValue("$start", now.AddMinutes(-5).ToString("O"));
            seed.Parameters.AddWithValue("$end", now.AddMinutes(-1).ToString("O"));
            await seed.ExecuteNonQueryAsync();
            if (!missingParticipants)
            {
                var participants = connection.CreateCommand();
                participants.CommandText = """
                    INSERT INTO match_participants(match_id,player_index,display_name,
                                                   master_id,master_name,deck_name)
                    VALUES('summary-owned',1,'乙','master-b','迦具土','PRIVATE_BETA'),
                          ('summary-owned',0,'甲','master-a',$name,'PRIVATE_ALPHA');
                    """;
                participants.Parameters.AddWithValue("$name", missingName ? "" : "嫦娥");
                await participants.ExecuteNonQueryAsync();
            }
            var first = Assert.Single(await recorder.ListRecentPlayerReplayMatchesAsync("account-a", "甲"));
            var second = Assert.Single(await recorder.ListMatchesForAccountAsync("account-b", "乙"));
            var legacy = Assert.Single(await recorder.ListMatchesForPlayerAsync("甲"));
            var internalSummary = Assert.Single(await recorder.ListMatchesAsync());
            var detail = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync("summary-owned"));
            foreach (var summary in new[] { first, second, legacy, internalSummary, detail.Match })
            {
                Assert.Equal(3, summary.CommandCount); // includes rejected and system records, not actions
                Assert.Equal(missingParticipants ? null : missingName ? "master-a" : "嫦娥", summary.Master0);
                Assert.Equal(missingParticipants ? null : "迦具土", summary.Master1);
                Assert.Null(summary.Winner); // the default first_player=0 is not a result or confirmed initiative
            }
            foreach (var summary in new[] { first, second, legacy })
            {
                Assert.Empty(summary.Deck0);
                Assert.Empty(summary.Deck1);
            }
            Assert.Equal("PRIVATE_ALPHA", internalSummary.Deck0);
            Assert.Equal(3, detail.Commands.Count);
            Assert.Empty(await recorder.ListRecentPlayerReplayMatchesAsync("stranger", "甲"));
            var unchanged = connection.CreateCommand();
            unchanged.CommandText = "SELECT COUNT(*) FROM match_events WHERE match_id='summary-owned';";
            Assert.Equal(3L, await unchanged.ExecuteScalarAsync());
            completed = true;
        }
        finally
        {
            if (completed)
            {
                var target = Path.GetFullPath(directory);
                if (Path.GetDirectoryName(target) != Path.GetFullPath(parent)
                    || !Guid.TryParseExact(Path.GetFileName(target), "N", out _))
                    throw new InvalidOperationException("Unowned summary fixture path");
                PlatformTestDirectoryCleanup.Delete(target);
            }
            else Console.WriteLine("Failed synthetic summary fixture retained: " + directory);
        }
    }
}
