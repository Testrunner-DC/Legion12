using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class TestRunReplayRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 20, 0, 0, TimeSpan.Zero);
    private const string Url = "https://testrun.legion-12.com";

    [Theory]
    [InlineData("production", Url)]
    [InlineData("", Url)]
    [InlineData("testrun", "https://legion-12.com")]
    [InlineData("testrun", "https://testrun.legion-12.com.evil.test")]
    [InlineData("testrun", "http://testrun.legion-12.com")]
    [InlineData("testrun", "https://testrun.legion-12.com:8084")]
    public async Task WrongEnvironmentOrUrlNeverWrites(string marker, string url)
    {
        await using var fixture = await Fixture.Create();
        await fixture.Seed("old", Now.AddDays(-15));
        var result = await fixture.Recorder.RunTestRunReplayRetentionAsync(marker, url,
            fixture.Runtime, fixture.Production, evidenceProvider: () => new([], []), utcNow: Now);
        Assert.False(result.Ran);
        Assert.False(MatchRecorder.IsTestRunRetentionIsolatedCore(marker,url,fixture.Runtime,fixture.Production,fixture.Database,false));
        Assert.Equal(0, await fixture.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name='testrun_replay_retention_cursor'"));
        Assert.Equal(1, await fixture.Scalar("SELECT COUNT(*) FROM matches WHERE initial_state_json IS NOT NULL"));
    }

    [Fact]
    public async Task EvidenceAddedAfterCandidateReadAndCancellationPreservePayload()
    {
        await using var f = await Fixture.Create();
        await f.Seed("late", Now.AddDays(-15));
        var reads = 0;
        var result = await f.Run(evidence: () => ++reads == 1 ? new([], []) : new(["late"], []));
        Assert.Equal(0, result.PurgedMatches);
        Assert.Equal(1, await f.Scalar("SELECT COUNT(*) FROM match_state_checkpoints"));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Recorder.RunTestRunRetentionCoreAsync(
            "testrun", Url, f.Runtime, f.Production, [], () => new([], []),
            Now, null, cancel.Token, enforceInstalledPaths: false));
        Assert.Equal(1, await f.Scalar("SELECT COUNT(*) FROM match_card_facts"));
    }

    [Fact]
    public async Task PhysicalDatabaseOutsideRuntimeAndMissingEvidenceFailClosed()
    {
        await using var fixture = await Fixture.Create();
        Assert.False(MatchRecorder.IsTestRunRetentionIsolated("testrun", Url,
            fixture.Runtime, fixture.Production, Path.Combine(fixture.Production, "matches.db")));
        Assert.False((await fixture.Recorder.RunTestRunReplayRetentionAsync("testrun", Url,
            fixture.Runtime, fixture.Production, utcNow: Now)).Ran);
        Assert.False(MatchRecorder.IsTestRunRetentionIsolated("testrun", Url,
            fixture.Runtime, fixture.Runtime, fixture.Database));
    }

    [Fact]
    public async Task StrictFourteenDayBoundaryProtectsEvidenceAndPreservesArchivesAndAppliedOutbox()
    {
        await using var f = await Fixture.Create();
        foreach (var id in new[] { "old", "bug", "room", "active", "pending", "legacy", "ranked", "quarantine", "recovering", "corrupt" })
            await f.Seed(id, Now.AddDays(-15));
        await f.Seed("boundary", Now.AddDays(-14));
        await f.Seed("recent", Now.AddDays(-14).AddTicks(1));
        await f.Seed("unfinished", null);
        await f.Outbox("pending", 3, "pending");
        await f.Outbox("legacy", 0, "applied");
        await f.Outbox("ranked", 3, "applied");
        await f.Outbox("corrupt", 3, "applied");
        await f.Execute("UPDATE ranked_settlement_outbox SET payload_hash='bad' WHERE match_id='corrupt'; INSERT INTO ranked_recovery_quarantine VALUES('quarantine','test','2026-08-01'); INSERT INTO ranked_match_runtime(match_id,room_code,status,checkpoint_json,checkpoint_hash,updated_utc) VALUES('recovering','R','active','{}','hash','2026-08-01');");
        var reads = 0;
        var result = await f.Run(["active"], () => { reads++; return new(["bug"], ["R-room"]); });
        Assert.True(result.Ran);
        Assert.Equal(2, result.PurgedMatches);
        Assert.True(reads > 2);
        Assert.Equal(13, await f.Scalar("SELECT COUNT(*) FROM matches"));
        Assert.Equal(26, await f.Scalar("SELECT COUNT(*) FROM match_participants"));
        Assert.Equal(13, await f.Scalar("SELECT COUNT(*) FROM match_deck_cards"));
        Assert.Equal(13, await f.Scalar("SELECT COUNT(*) FROM match_events"));
        Assert.Equal(11, await f.Scalar("SELECT COUNT(*) FROM match_card_facts"));
        Assert.Equal(4, await f.Scalar("SELECT COUNT(*) FROM ranked_settlement_outbox"));
        Assert.Equal(2, await f.Scalar("SELECT COUNT(*) FROM player_replay_payload_expirations WHERE match_id IN ('old','ranked')"));
        Assert.Equal(11, await f.Scalar("SELECT COUNT(*) FROM match_state_checkpoints"));
        Assert.Equal(11, await f.Scalar("SELECT COUNT(*) FROM match_action_requests"));
        Assert.Equal(11, await f.Scalar("SELECT COUNT(*) FROM match_action_events"));
        Assert.Equal(2, await f.Scalar("SELECT COUNT(*) FROM match_events WHERE state_json='{}'"));
        Assert.Equal(0, (await f.Run(["active"], () => new(["bug"], ["R-room"]))).PurgedMatches);
    }

    [Fact]
    public async Task FailureRollsBackPayloadFactsTombstoneAndCursorThenRetryIsSafe()
    {
        await using var f = await Fixture.Create();
        await f.Seed("old", Now.AddDays(-15));
        f.Recorder.StorageFailureInjector = point =>
        {
            if (point == "before-testrun-replay-purge-commit") throw new IOException("injected");
        };
        await Assert.ThrowsAsync<IOException>(() => f.Run());
        Assert.Equal(1, await f.Scalar("SELECT COUNT(*) FROM matches WHERE initial_state_json IS NOT NULL"));
        Assert.Equal(1, await f.Scalar("SELECT COUNT(*) FROM match_card_facts"));
        Assert.Equal(1, await f.Scalar("SELECT COUNT(*) FROM match_state_checkpoints"));
        Assert.Equal(0, await f.Scalar("SELECT COUNT(*) FROM player_replay_payload_expirations"));
        Assert.Equal(0, await f.Scalar("SELECT last_rowid FROM testrun_replay_retention_cursor"));
        f.Recorder.StorageFailureInjector = null;
        Assert.Equal(1, (await f.Run()).PurgedMatches);
    }

    [Fact]
    public async Task BoundedCursorCrossesProtectedPrefixAndZeroBudgetDoesNotScan()
    {
        await using var f = await Fixture.Create();
        await f.Execute("""
            WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<505)
            INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,started_utc,ended_utc,mode_id)
            SELECT 'recent-'||x,'R',1,'a','b','a','b','2026-09-09','2026-09-09','casual' FROM n;
            """);
        await f.Seed("old-tail", Now.AddDays(-15));
        Assert.False((await f.Run(budget: TimeSpan.Zero)).Ran);
        Assert.Equal(0, await f.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name='testrun_replay_retention_cursor'"));
        var first = await f.Run();
        Assert.Equal(0, first.PurgedMatches);
        Assert.True(first.HasMoreEligibleMatches);
        var cursor = await f.Scalar("SELECT last_rowid FROM testrun_replay_retention_cursor");
        Assert.InRange(cursor, 1, 500);
        var total = 0;
        await f.Restart();
        Assert.Equal(cursor, await f.Scalar("SELECT last_rowid FROM testrun_replay_retention_cursor"));
        for (var attempt = 0; attempt < 10 && total == 0; attempt++) total += (await f.Run()).PurgedMatches;
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task ReleaseRuntimeAliasResolvesIntoTestStorageButNeverProduction()
    {
        await using var f=await Fixture.Create();
        var alias=Path.Combine(f.Root,"release-runtime");
        void Link(string target)
        {
            if(OperatingSystem.IsWindows())
            {
                var info=new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute=false,CreateNoWindow=true };
                foreach(var arg in new[]{"/c","mklink","/J",alias,target})info.ArgumentList.Add(arg);
                using var process=System.Diagnostics.Process.Start(info)!;process.WaitForExit();Assert.Equal(0,process.ExitCode);
            }
            else Directory.CreateSymbolicLink(alias,target);
        }
        try
        {
            Link(f.Runtime);
            Assert.True(MatchRecorder.IsTestRunRetentionIsolatedCore("testrun",Url,f.Runtime,f.Production,
                Path.Combine(alias,"matches.db"),false));
            Directory.Delete(alias);
            File.Copy(f.Database,Path.Combine(f.Production,"matches.db"));
            Link(f.Production);
            Assert.False(MatchRecorder.IsTestRunRetentionIsolatedCore("testrun",Url,f.Runtime,f.Production,
                Path.Combine(alias,"matches.db"),false));
            if(!OperatingSystem.IsWindows())
                Assert.False(MatchRecorder.IsTestRunRetentionIsolated("testrun",Url,f.Runtime,f.Production,f.Database));
        }
        finally { if(Directory.Exists(alias))Directory.Delete(alias); }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "l12-test-retention-" + Guid.NewGuid().ToString("N"));
        public string Runtime => Path.Combine(Root, "legion12-testrun-runtime");
        public string Production => Path.Combine(Root, "legion12-runtime");
        public string Database => Path.Combine(Runtime, "matches.db");
        public MatchRecorder Recorder { get; private set; } = null!;
        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            Directory.CreateDirectory(f.Runtime);
            Directory.CreateDirectory(f.Production);
            f.Recorder = new MatchRecorder(f.Database, () => Now);
            await f.Recorder.InitializeAsync();
            return f;
        }
        public Task<L12PlayerReplayCleanupResult> Run(IReadOnlyCollection<string>? active = null,
            Func<L12ReplayEvidenceReferences>? evidence = null, TimeSpan? budget = null)
            => Recorder.RunTestRunRetentionCoreAsync("testrun", Url, Runtime, Production, active ?? [],
                evidence ?? (() => new([], [])), Now, budget, CancellationToken.None, enforceInstalledPaths: false);
        public async Task Restart()
        {
            await Recorder.DisposeAsync();
            Recorder = new MatchRecorder(Database, () => Now);
            await Recorder.InitializeAsync();
        }
        public async Task Execute(string sql, params (string Key, object Value)[] parameters)
        {
            await using var c = new SqliteConnection($"Data Source={Database};Pooling=False");
            await c.OpenAsync();
            using var command = c.CreateCommand();
            command.CommandText = sql;
            foreach (var p in parameters) command.Parameters.AddWithValue(p.Key, p.Value);
            await command.ExecuteNonQueryAsync();
        }
        public async Task<long> Scalar(string sql)
        {
            await using var c = new SqliteConnection($"Data Source={Database};Pooling=False");
            await c.OpenAsync();
            using var command = c.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        public Task Seed(string id, DateTimeOffset? ended) => Execute("""
            INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,started_utc,ended_utc,mode_id,initial_state_json)
            VALUES($id,'R-'||$id,1,'a','b','a','b',$start,$end,'casual','{"initial":true}');
            INSERT INTO match_participants(match_id,player_index,display_name,master_id,master_name,deck_name,deck_snapshot_coverage)
            VALUES($id,0,'a','a','a','a','exact'),($id,1,'b','b','b','b','exact');
            INSERT INTO match_deck_cards(match_id,player_index,section,card_id,quantity) VALUES($id,0,'main','a',1);
            INSERT INTO match_events(match_id,sequence,received_utc,player_index,command_json,accepted,revision,state_hash,state_json)
            VALUES($id,1,$start,0,'{}',1,1,'hash','{"state":true}');
            INSERT INTO match_action_requests(match_id,player_index,request_id,command_sequence,accepted,revision,state_hash,created_utc)
            VALUES($id,0,'request',1,1,1,'hash',$start);
            INSERT INTO match_action_events(match_id,event_sequence,command_sequence,revision,event_json,created_utc)
            VALUES($id,1,1,1,'{"event":true}',$start);
            INSERT INTO match_card_facts(match_id,fact_key,command_sequence,revision,round,turn,phase,occurred_utc,kind,coverage,metadata_json)
            VALUES($id,'fact',1,1,1,1,'Main',$start,'played','exact','{}');
            INSERT INTO match_state_checkpoints(match_id,sequence,revision,state_hash,state_encoding,state_blob,uncompressed_bytes,random_draw_count,random_state_version,card_fact_signal_sequence,auto_pass_empty_responses,conceal_hidden_response_availability,created_utc)
            VALUES($id,1,1,'hash','test',X'0102',2,0,0,0,0,0,$start);
            """, ("$id", id), ("$start", Now.AddDays(-16).ToString("O")), ("$end", ended is null ? DBNull.Value : ended.Value.ToString("O")));
        public Task Outbox(string id, int round, string status)
        {
            var payload = new L12RankedSettlementEnvelope(1, id, "a", "b", "ma", "mb", 0,
                Now.AddDays(-16), Now.AddDays(-15), 5, "normal", "", "", round);
            var json = JsonSerializer.Serialize(payload);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            return Execute("UPDATE matches SET mode_id='ranked' WHERE match_id=$id; INSERT INTO ranked_settlement_outbox(match_id,payload_json,payload_hash,status,created_utc) VALUES($id,$json,$hash,$status,$utc);",
                ("$id", id), ("$json", json), ("$hash", hash), ("$status", status), ("$utc", Now.ToString("O")));
        }
        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            using var pool = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database }.ToString());
            SqliteConnection.ClearPool(pool);
            for (var attempt=0; ; attempt++)
            {
                try { Directory.Delete(Root, recursive: true); break; }
                catch (IOException) when (attempt<4) { await Task.Delay(50*(attempt+1)); }
            }
        }
    }
}
