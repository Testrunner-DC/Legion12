using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Abstractions;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class DeploymentReadinessRequestCoveringIndexTests(ITestOutputHelper output)
{
    private const string IndexName = "ix_match_action_requests_readiness_cover";
    private static readonly string OriginalSql = ((IReadOnlyDictionary<string, string>)typeof(MatchRecorder)
        .GetField("DeploymentQueries", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!)["journal_orphans"];

    [Fact]
    public async Task InitializationCoversTheOriginalFullQueryWithoutChangingCountsOrRecords()
    {
        await using var f = await Fixture.Create();
        f.Seed();
        await f.Recorder.InitializeAsync(); // Settle the recorder's existing legacy metadata backfill first.
        f.Sql($"DROP INDEX IF EXISTS {IndexName};"); // Synthetic original-schema control only.
        var facts = f.Facts();
        var version = f.Scalar("PRAGMA user_version;");
        var before = f.Scalar(OriginalSql);
        var oldPlan = f.Plan();
        Assert.Equal(7, before); // Four request rows (one has three faults), plus three other journal rows.
        Assert.Contains(oldPlan, detail => detail == "SCAN r");

        await f.Recorder.InitializeAsync();

        Assert.Equal(before, f.Scalar(OriginalSql));
        Assert.Equal(facts, f.Facts());
        Assert.Equal(version, f.Scalar("PRAGMA user_version;"));
        var readiness = await f.Recorder.DeploymentReadinessAsync();
        Assert.True(readiness.Verified);
        Assert.False(readiness.Clear);
        Assert.False(readiness.GrantsStopPermit);
        Assert.Equal("none", readiness.FailureCode);
        Assert.Equal(39, readiness.Queries);
        Assert.Equal(before, readiness.Blockers["journal_orphans"]);
        output.WriteLine(JsonSerializer.Serialize(new { syntheticOnly = true, originalFullSql = OriginalSql,
            oldPlan, initializedPlan = f.Plan(), originalCount = before, initializedCount = f.Scalar(OriginalSql),
            originalUserVersion = version, finalUserVersion = f.Scalar("PRAGMA user_version;"),
            grantsStopPermit = readiness.GrantsStopPermit }));
        Assert.Contains(f.Plan(), detail => detail.Contains($"SCAN r USING COVERING INDEX {IndexName}", StringComparison.Ordinal));
        f.Passed = true;
    }

    [Fact]
    public async Task RepeatedRealInitializationCreatesOneExactNonuniqueIndexAndPreservesFacts()
    {
        await using var f = await Fixture.Create();
        f.Seed();
        await f.Recorder.InitializeAsync();
        var facts = f.Facts();
        var version = f.Scalar("PRAGMA user_version;");
        await using (var reopened = new MatchRecorder(f.DatabasePath, Fixture.UtcNow))
        {
            await reopened.InitializeAsync();
            await reopened.InitializeAsync();
        }
        Assert.Equal(facts, f.Facts());
        Assert.Equal(version, f.Scalar("PRAGMA user_version;"));
        using var db = f.Open();
        using var indexes = db.CreateCommand();
        indexes.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type='index' AND name=$name;";
        indexes.Parameters.AddWithValue("$name", IndexName);
        Assert.Equal(1L, Convert.ToInt64(indexes.ExecuteScalar()));
        using var columns = db.CreateCommand();
        columns.CommandText = $"PRAGMA index_xinfo('{IndexName}');";
        using var reader = columns.ExecuteReader();
        var keys = new List<(string Name, long Descending, string Collation)>();
        while (reader.Read())
            if (reader.GetInt64(5) == 1) keys.Add((reader.GetString(2), reader.GetInt64(3), reader.GetString(4)));
        Assert.Equal(new[] { ("match_id", 0L, "BINARY"), ("command_sequence", 0L, "BINARY"), ("accepted", 0L, "BINARY") }, keys);
        reader.Close();
        using var uniqueness = db.CreateCommand();
        uniqueness.CommandText = $"SELECT \"unique\" FROM pragma_index_list('match_action_requests') WHERE name='{IndexName}';";
        Assert.Equal(0L, Convert.ToInt64(uniqueness.ExecuteScalar()));
        f.Passed = true;
    }

    [Theory]
    [InlineData("missing-table")]
    [InlineData("unknown-version")]
    public async Task ReadinessStillRefusesMissingOrUnknownSchemaWithoutRepairingIt(string damage)
    {
        await using var f = await Fixture.Create();
        f.Sql(damage == "missing-table" ? "DROP TABLE response_preference_outbox;" : "PRAGMA user_version=1;");
        var facts = f.Facts();
        var version = f.Scalar("PRAGMA user_version;");
        var readiness = await f.Recorder.DeploymentReadinessAsync();
        Assert.False(readiness.Verified);
        Assert.False(readiness.Clear);
        Assert.False(readiness.GrantsStopPermit);
        Assert.Equal("schema_invalid", readiness.FailureCode);
        Assert.Equal(facts, f.Facts());
        Assert.Equal(version, f.Scalar("PRAGMA user_version;"));
        f.Passed = true;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "readiness-cover-" + Guid.NewGuid().ToString("N"));
        internal string DatabasePath => Path.Combine(_root, "matches.db");
        internal MatchRecorder Recorder { get; private set; } = null!;
        internal bool Passed { get; set; }
        internal static DateTimeOffset UtcNow() => DateTimeOffset.Parse("2026-01-02T00:00:00Z");
        internal static async Task<Fixture> Create()
        {
            var f = new Fixture();
            f.Recorder = new MatchRecorder(f.DatabasePath, UtcNow);
            await f.Recorder.InitializeAsync();
            return f;
        }
        internal SqliteConnection Open()
        {
            var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
            db.Open(); return db;
        }
        internal void Sql(string sql)
        { using var db = Open(); using var command = db.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
        internal long Scalar(string sql)
        { using var db = Open(); using var command = db.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(command.ExecuteScalar()); }
        internal string[] Plan()
        {
            using var db = Open(); using var command = db.CreateCommand(); command.CommandText = "EXPLAIN QUERY PLAN " + OriginalSql;
            using var reader = command.ExecuteReader(); var plan = new List<string>();
            while (reader.Read()) plan.Add(reader.GetString(3));
            return plan.ToArray();
        }
        internal string Facts()
        {
            using var db = Open();
            var tables = new List<string>();
            using (var command = db.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
                using var reader = command.ExecuteReader(); while (reader.Read()) tables.Add(reader.GetString(0));
            }
            var facts = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var table in tables)
            {
                using var command = db.CreateCommand();
                // Existing InitializeAnalyticsSchemaAsync deliberately refreshes only this
                // schema heartbeat's migrated_utc on every open. Its component/version and
                // every field of all business tables remain part of the equality assertion.
                command.CommandText = table == "match_recorder_schema"
                    ? "SELECT component,version FROM match_recorder_schema;"
                    : "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\";";
                using var reader = command.ExecuteReader(); var rows = new List<string>();
                while (reader.Read())
                {
                    var values = new object[reader.FieldCount]; reader.GetValues(values);
                    rows.Add(JsonSerializer.Serialize(values.Select(value => value is DBNull ? null : value).ToArray()));
                }
                facts.Add(table, rows.Order(StringComparer.Ordinal).ToArray());
            }
            return JsonSerializer.Serialize(facts);
        }
        internal void Seed() => Sql("""
            -- Only this synthetic setup connection admits deliberately orphaned rows.
            -- Product initialization/readiness connections keep their normal constraints.
            PRAGMA foreign_keys=OFF;
            INSERT INTO matches(match_id,room_code,seed,player_0,player_1,deck_0,deck_1,started_utc,ended_utc,mode_id)
            VALUES('known','synthetic-room',1,'synthetic-a','synthetic-b','synthetic-d0','synthetic-d1',
              '2026-01-01T00:00:00Z','2026-01-01T01:00:00Z','friendly');
            INSERT INTO match_action_requests(match_id,player_index,request_id,command_sequence,accepted,revision,state_hash,created_utc)
            VALUES('known',0,'normal',1,1,1,'hash','2026-01-01T00:00:00Z'),
              ('known',0,'negative',-1,1,1,'hash','2026-01-01T00:00:00Z'),
              ('known',0,'invalid-accepted',2,2,1,'hash','2026-01-01T00:00:00Z'),
              ('orphan',0,'orphan',3,1,1,'hash','2026-01-01T00:00:00Z'),
              ('orphan',0,'multiple-faults',-2,7,1,'hash','2026-01-01T00:00:00Z'),
              ('known',1,'valid-rejection',4,0,1,'hash','2026-01-01T00:00:00Z');
            INSERT INTO match_state_checkpoints(match_id,sequence,revision,state_hash,state_encoding,state_blob,uncompressed_bytes,
              random_draw_count,card_fact_signal_sequence,auto_pass_empty_responses,conceal_hidden_response_availability,created_utc)
            VALUES('checkpoint-orphan',0,0,'hash','synthetic',X'00',1,0,0,0,0,'2026-01-01T00:00:00Z');
            INSERT INTO match_action_events(match_id,event_sequence,command_sequence,revision,event_json,created_utc)
            VALUES('known',1,-1,1,'{}','2026-01-01T00:00:00Z');
            INSERT INTO match_events(match_id,sequence,received_utc,command_json,accepted,revision,state_hash,state_json)
            VALUES('event-orphan',1,'2026-01-01T00:00:00Z','{}',0,0,'hash','{}');
            """);
        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync(); SqliteConnection.ClearAllPools();
            if (Passed && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
