using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class PublicDeckStatisticsConsistencyTests
{
    [Fact]
    public async Task SharedFilterMatchesLegacyAcrossWindowAndExcludedFactBoundaries()
    {
        await using var fixture = await PublicDeckStatisticsFixture.Start();
        var from = fixture.Now.AddDays(-90);
        fixture.SeedGroup(1, "M-FROM", "O-FROM", 3, "at-from", from);
        fixture.SeedGroup(2, "M-TO", "O-TO", 3, "at-to", fixture.Now);
        fixture.SeedGroup(3, "M-OLD", "O-OLD", 3, "too-old", from.AddTicks(-1));
        for (var index = 0; index < 3; index++)
            fixture.SeedMatch(4, "M-OPEN", "O-OPEN", "open-" + index, null);
        fixture.SeedGroup(5, "M-ERROR", "O-ERROR", 3, "error", fixture.Now.AddMinutes(-2), error: "failed");
        fixture.SeedGroup(6, "M-SANDBOX", "O-SANDBOX", 3, "sandbox", fixture.Now.AddMinutes(-2), mode: "sandbox");
        fixture.SeedGroup(7, "M-HELD", "O-HELD", 3, "held", fixture.Now.AddMinutes(-2));
        fixture.SeedGroup(8, "M-VOID", "O-VOID", 3, "void", fixture.Now.AddMinutes(-2));
        fixture.SeedGroup(9, "M-DISABLED", "O-DISABLED", 3, "disabled", fixture.Now.AddMinutes(-2),
            account1: "disabled-account");
        fixture.SeedGroup(10, "M-DELETED", "O-DELETED", 3, "deleted", fixture.Now.AddMinutes(-2),
            account0: "deleted-account");
        fixture.SeedGroup(11, "M-TWO", "O-TWO", 2, "minimum-two", fixture.Now.AddMinutes(-2));
        fixture.SeedGroup(12, "M-THREE", "O-THREE", 3, "minimum-three", fixture.Now.AddMinutes(-2));
        fixture.SeedGroup(13, "M-MISSING", "O-MISSING", 3, "missing-participant", fixture.Now.AddMinutes(-2));
        fixture.MatchSql("DELETE FROM match_participants WHERE match_id LIKE 'missing-participant-%' AND player_index=1;");
        fixture.SeedGroup(14, "M-EMPTY", "O-EMPTY", 3, "empty-master", fixture.Now.AddMinutes(-2));
        fixture.MatchSql("UPDATE match_participants SET master_id='' WHERE match_id LIKE 'empty-master-%' AND player_index=1;");
        fixture.MatchSql("UPDATE matches SET ended_utc='2026-10-06T07:bad-but-error+00:00' WHERE match_id LIKE 'error-%';");
        fixture.MatchSql("UPDATE matches SET ended_utc='2026-10-06T07:bad-but-sandbox+00:00' WHERE match_id LIKE 'sandbox-%';");
        fixture.MatchSql("UPDATE matches SET ended_utc='1900-bad-outside-window' WHERE match_id LIKE 'too-old-%';");
        var excludedMatches = Enumerable.Range(0, 3).Select(index => "held-" + index)
            .Concat(Enumerable.Range(0, 3).Select(index => "void-" + index)).ToArray();
        var excludedAccounts = new[] { "disabled-account", "deleted-account" };

        var legacy = await fixture.Recorder.PublicDeckVersionStatisticsAsync(fixture.Published.Id,
            excludedMatches, excludedAccounts);
        var paged = await fixture.Recorder.PublicDeckVersionStatisticsPageAsync(fixture.Published.Id,
            1, 100, excludedMatches, excludedAccounts);
        Assert.Equal(legacy.From, paged.From);
        Assert.Equal(legacy.To, paged.To);
        Assert.Equal((90, 9, "available"), (paged.RecentDays, paged.Games, paged.SampleStatus));
        Assert.Equal(3, paged.Total);
        Assert.Equal(new[] { 12, 2, 1 }, paged.Groups.Select(group => group.Version));
        Assert.Equal(legacy.Groups.ToArray(), paged.Groups.ToArray());
        fixture.Complete();
    }

    [Fact]
    public async Task EmptyAndMinimumTwoRemainExplicitlyNonAvailable()
    {
        await using var fixture = await PublicDeckStatisticsFixture.Start();
        var empty = await fixture.Recorder.PublicDeckVersionStatisticsPageAsync(fixture.Published.Id, 1, 30);
        Assert.Equal((0, 0, "empty"), (empty.Games, empty.Total, empty.SampleStatus));
        Assert.Empty(empty.Groups);
        fixture.SeedGroup(1, "M-TWO", "O-TWO", 2, "only-two", fixture.Now.AddMinutes(-1));
        var insufficient = await fixture.Recorder.PublicDeckVersionStatisticsPageAsync(fixture.Published.Id, 1, 30);
        var legacy = await fixture.Recorder.PublicDeckVersionStatisticsAsync(fixture.Published.Id);
        Assert.Equal((0, 0, "insufficient"),
            (insufficient.Games, insufficient.Total, insufficient.SampleStatus));
        Assert.Empty(insufficient.Groups);
        Assert.Equal(legacy.Games, insufficient.Games);
        Assert.Equal(legacy.SampleStatus, insufficient.SampleStatus);
        fixture.Complete();
    }

    [Fact]
    public async Task MoreThanOneHundredGroupsPageWithoutTruncationAndMatchLegacyExactly()
    {
        await using var fixture = await PublicDeckStatisticsFixture.Start();
        for (var group = 1; group <= 105; group++)
            fixture.SeedGroup(group, $"M-{group:000}", $"O-{group:000}", 3,
                $"many-{group:000}", fixture.Now.AddMinutes(-group));
        var legacy = await fixture.Recorder.PublicDeckVersionStatisticsAsync(fixture.Published.Id);
        var first = await fixture.Recorder.PublicDeckVersionStatisticsPageAsync(fixture.Published.Id, 1, 100);
        var second = await fixture.Recorder.PublicDeckVersionStatisticsPageAsync(fixture.Published.Id, 2, 100);
        var third = await fixture.Recorder.PublicDeckVersionStatisticsPageAsync(fixture.Published.Id, 3, 100);
        Assert.Equal((105, 315, 100), (first.Total, first.Games, first.Groups.Count));
        Assert.Equal((105, 315, 5), (second.Total, second.Games, second.Groups.Count));
        Assert.Equal((105, 315, 0), (third.Total, third.Games, third.Groups.Count));
        Assert.Equal(legacy.Groups.ToArray(), first.Groups.Concat(second.Groups).ToArray());
        Assert.Equal(legacy.Games, first.Games);
        Assert.Equal(legacy.SampleStatus, first.SampleStatus);
        fixture.Complete();
    }

    [Fact]
    public async Task CountTotalAndPageShareOneWalSnapshotWhileConcurrentCommitAppearsNextReadAndRestart()
    {
        await using var fixture = await PublicDeckStatisticsFixture.Start();
        fixture.SeedGroup(1, "M-ONE", "O-ONE", 3, "snapshot-one", fixture.Now.AddMinutes(-2));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Recorder.PublicDeckStatisticsReadPauseHook = async token =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        var pending = fixture.Recorder.PublicDeckVersionStatisticsPageAsync(fixture.Published.Id, 1, 100);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.SeedGroup(2, "M-TWO", "O-TWO", 3, "snapshot-two", fixture.Now.AddMinutes(-1));
        release.TrySetResult();
        var first = await pending;
        fixture.Recorder.PublicDeckStatisticsReadPauseHook = null;
        Assert.Equal((1, 3), (first.Total, first.Games));
        Assert.Equal(new[] { 1 }, first.Groups.Select(group => group.Version));
        var next = await fixture.Recorder.PublicDeckVersionStatisticsPageAsync(fixture.Published.Id, 1, 100);
        Assert.Equal((2, 6), (next.Total, next.Games));
        Assert.Equal(new[] { 2, 1 }, next.Groups.Select(group => group.Version));
        await using var restarted = new MatchRecorder(fixture.MatchesPath, () => fixture.Now);
        await restarted.InitializeAsync();
        var afterRestart = await restarted.PublicDeckVersionStatisticsPageAsync(fixture.Published.Id, 1, 100);
        Assert.Equal((next.From, next.To, next.RecentDays, next.Games, next.SampleStatus, next.Total, next.Page, next.PageSize),
            (afterRestart.From, afterRestart.To, afterRestart.RecentDays, afterRestart.Games,
                afterRestart.SampleStatus, afterRestart.Total, afterRestart.Page, afterRestart.PageSize));
        Assert.Equal(next.Groups.ToArray(), afterRestart.Groups.ToArray());
        fixture.Complete();
    }

    [Fact]
    public void LegacyAndPagedQueriesShareOneAuthoritativeFilterFragment()
    {
        var source = File.ReadAllText(RepositoryFile("服务端WebSocket", "TwelveLegions",
            "MatchRecorder.PublicDecks.cs"));
        Assert.Equal(2, Occurrences(source, "AND COALESCE(m.mode_id,'legacy')<>'sandbox'\n"));
        Assert.Equal(1, Occurrences(source, "AND NOT EXISTS(SELECT 1 FROM json_each($accounts)"));
        Assert.True(Occurrences(source, "PublicDeckStatisticsGroupedSql") >= 4);
    }

    private static int Occurrences(string source, string value)
        => source.Split(value, StringSplitOptions.None).Length - 1;

    private static string RepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("无法定位仓库源文件", string.Join('/', segments));
    }
}
