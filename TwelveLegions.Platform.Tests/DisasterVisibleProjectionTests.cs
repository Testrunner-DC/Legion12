using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DisasterVisibleProjectionTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public async Task BothPlayersSeeOnlyOwnPickCandidatesAndPublicBans(int first)
    {
        await using var f = await DisasterFactFixture.Create();
        await f.Prepare(first); await f.End();
        var facts = await f.Read();
        for (var player = 0; player < 2; player++)
        {
            var view = Assert.IsType<L12VisibleDisasterSetup>(await f.Recorder.ReadVisibleDisasterSetupAsync(
                f.Game.State.MatchId, player == 0 ? "synthetic-account-a" : "synthetic-account-b"));
            Assert.Equal("self", view.Visibility);
            Assert.Equal(first, view.FirstPlayer);
            Assert.Equal(4, view.Decisions.Count);
            Assert.All(view.Decisions.Where(d => d.Action == "ban"), d => Assert.Empty(d.OwnCandidateCardIds));
            var pick = Assert.Single(view.Decisions.Where(d => d.Action == "pick"));
            Assert.Equal(player, pick.ActorIndex);
            Assert.Equal(facts.Decisions.Single(d => d.Action == "pick" && d.ActorIndex == player).Candidates,
                pick.OwnCandidateCardIds);
            Assert.DoesNotContain(facts.Decisions.Single(d => d.Action == "pick" && d.ActorIndex != player).ChosenCardId!,
                view.VisibleCompositionCardIds);
            Assert.DoesNotContain("synthetic-account", JsonSerializer.Serialize(view));
            Assert.DoesNotContain("Deck", JsonSerializer.Serialize(view));
        }
        Assert.Null(await f.Recorder.ReadVisibleDisasterSetupAsync(f.Game.State.MatchId, "synthetic-outsider"));
        Assert.Null(await f.Recorder.ReadVisibleDisasterSetupAsync(f.Game.State.MatchId, null));
    }

    [Fact]
    public async Task PublicTournamentProjectionNeverIncludesPrivatePicksEvenForNonparticipantStaff()
    {
        await using var f = await DisasterFactFixture.Create("tournament");
        Assert.Null(await f.Recorder.ReadVisibleDisasterSetupAsync(f.Game.State.MatchId, null, true));
        await f.Prepare(); await f.End();
        foreach (var viewer in new string?[] { null, "synthetic-staff", "synthetic-outsider" })
        {
            var view = Assert.IsType<L12VisibleDisasterSetup>(await f.Recorder.ReadVisibleDisasterSetupAsync(
                f.Game.State.MatchId, viewer, true));
            Assert.Equal("public", view.Visibility);
            Assert.Equal(3, view.Decisions.Count);
            Assert.All(view.Decisions, d => { Assert.Equal("ban", d.Action); Assert.Empty(d.OwnCandidateCardIds); });
            Assert.Equal(2, view.VisibleCompositionCardIds.Count);
            Assert.DoesNotContain("CandidateCardIds", JsonSerializer.Serialize(view).Replace("OwnCandidateCardIds", ""));
        }
    }

    [Fact]
    public async Task ProjectionSurvivesReplayPurgeButCorruptFactsFailClosedAndWrongModeIsRejected()
    {
        await using var f = await DisasterFactFixture.Create();
        await f.Prepare(); await f.End();
        var before = await f.Recorder.ReadVisibleDisasterSetupAsync(f.Game.State.MatchId, "synthetic-account-a");
        await f.Recorder.RunPlayerReplayCleanupIfDueAsync(activeMatchIds: [],
            evidenceProvider: () => new([], []), utcNow: f.Now.AddDays(9));
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await f.Recorder.ReadVisibleDisasterSetupAsync(
            f.Game.State.MatchId, "synthetic-account-a")));
        Assert.Null(await f.Recorder.ReadVisibleDisasterSetupAsync(f.Game.State.MatchId, null, true));
        await f.Execute("DELETE FROM match_disaster_candidates WHERE step=4;");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Recorder.ReadVisibleDisasterSetupAsync(
            f.Game.State.MatchId, "synthetic-account-a"));
    }

    [Theory]
    [InlineData("random", false, "exact")]
    [InlineData("none", false, "not-applicable")]
    [InlineData("all", true, "unknown")]
    public async Task UnknownAndNonDraftRemainTruthful(string mode, bool skip, string coverage)
    {
        await using var f = await DisasterFactFixture.Create("friendly", mode, skip);
        if (!skip) await f.Prepare(); await f.End();
        var view = Assert.IsType<L12VisibleDisasterSetup>(await f.Recorder.ReadVisibleDisasterSetupAsync(
            f.Game.State.MatchId, "synthetic-account-a"));
        Assert.Equal(coverage, view.Coverage);
        Assert.Empty(view.Decisions);
    }

    [Fact]
    public async Task PublicAggregateRequiresThreeDistinctMatchesNotMirrorSeatsAndExclusionsStillApply()
    {
        await using var f = await DisasterFactFixture.Create("ranked");
        await f.Prepare(); await f.End();
        await f.Execute("INSERT INTO match_public_deck_bindings VALUES('synthetic-dsc',0,'synthetic-publication',1,'synthetic-hash');"
            + "INSERT INTO match_public_deck_bindings VALUES('synthetic-dsc',1,'synthetic-publication',1,'synthetic-hash');");
        var insufficient = await f.Recorder.ReadPublicDeckDisasterStatisticsAsync("synthetic-publication", new([], []));
        Assert.Equal("insufficient", insufficient.Status); Assert.Equal(0, insufficient.UsableMatches); Assert.Empty(insufficient.Items);
        await CloneSyntheticCompletedMatch(f, "synthetic-dsc-2");
        await CloneSyntheticCompletedMatch(f, "synthetic-dsc-3");
        f.Now = f.Now.AddSeconds(31); // Direct fixture SQL is outside the production invalidation path.
        var available = await f.Recorder.ReadPublicDeckDisasterStatisticsAsync("synthetic-publication", new([], []));
        Assert.Equal("available", available.Status); Assert.Equal(3, available.UsableMatches); Assert.NotEmpty(available.Items);
        Assert.Equal(9, available.Items.Sum(r => r.Bans));
        var serialized = JsonSerializer.Serialize(available);
        Assert.DoesNotContain("synthetic-dsc", serialized); Assert.DoesNotContain("synthetic-account", serialized);
        Assert.DoesNotContain("pick", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pool", serialized, StringComparison.OrdinalIgnoreCase);
        var excluded = await f.Recorder.ReadPublicDeckDisasterStatisticsAsync("synthetic-publication",
            new(["synthetic-dsc-2", "synthetic-dsc-3"], []));
        Assert.Equal("insufficient", excluded.Status); Assert.Empty(excluded.Items);
        Assert.Empty((await f.Recorder.ReadPublicDeckDisasterStatisticsAsync("synthetic-publication",
            new([], ["synthetic-account-b"]))).Items);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Recorder.ReadPublicDeckDisasterStatisticsAsync(
            "synthetic-publication", new([], []) { Available = false }));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Recorder.ReadPublicDeckDisasterStatisticsAsync(
            "synthetic-publication", new([], []), cancelled.Token));
    }

    private static async Task CloneSyntheticCompletedMatch(DisasterFactFixture f, string id)
    {
        // Derived synthetic native-fact fixtures, not a claim of three independent real games.
        await using var connection = new SqliteConnection($"Data Source={f.Database};Pooling=False");
        await connection.OpenAsync();
        foreach (var table in new[] { "matches", "match_participants", "match_deck_cards", "match_disaster_captures",
            "match_disaster_decisions", "match_disaster_candidates", "match_public_deck_bindings" })
        {
            var columns = new List<string>();
            using (var schema = connection.CreateCommand())
            {
                schema.CommandText = $"PRAGMA table_info({table});";
                await using var reader = await schema.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            }
            using var insert = connection.CreateCommand();
            insert.CommandText = $"INSERT INTO {table}({string.Join(',', columns)}) SELECT "
                + string.Join(',', columns.Select(c => c == "match_id" ? "$id" : c))
                + $" FROM {table} WHERE match_id='synthetic-dsc';";
            insert.Parameters.AddWithValue("$id", id);
            await insert.ExecuteNonQueryAsync();
        }
    }
}
