using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;
using Xunit.Sdk;

namespace TwelveLegions.Tests;

[CollectionDefinition(JournalFailureClosureCollection.Name, DisableParallelization = true)]
public sealed class JournalFailureClosureCollection : ICollectionFixture<JournalFailureTemplateFixture>
{
    public const string Name = "LC-03B journal failure closure";
}

[Collection(JournalFailureClosureCollection.Name)]
public sealed class JournalFailureClosureAdversarialTests
{
    private readonly JournalFailureTemplateFixture _fixture;

    public JournalFailureClosureAdversarialTests(JournalFailureTemplateFixture fixture)
        => _fixture = fixture;

    [Theory]
    [InlineData("CP-01", "truncate-one")]
    [InlineData("CP-02", "truncate-half")]
    [InlineData("CP-05", "state-hash")]
    [InlineData("CP-06", "revision")]
    public async Task LatestDamagedCheckpointFallsBackOnlyThroughACompleteVerifiedTail(
        string matrixId, string mutation)
    {
        var path = _fixture.Copy(matrixId);
        await MutateLatestCheckpointAsync(path, mutation);
        try
        {
            await using var recorder = await OpenRecorderAsync(path);
            var recovered = Assert.IsType<L12JournalRecoveryState>(
                await recorder.LoadJournalEngineAsync(JournalFailureTemplateFixture.TargetMatchId));
            Assert.Equal(_fixture.TargetCommandSequence, recovered.CommandSequence);
            Assert.Equal(_fixture.TargetStateHash, recovered.Engine.ComputeStateHash());
        }
        catch (Exception error) when (IsCorruptionFailure(error))
        {
            throw new XunitException(
                $"{matrixId} red: latest checkpoint is damaged but the complete verified tail was not recovered; "
                + $"copy={path}; error={error.GetType().Name}: {error.Message}");
        }
    }

    [Theory]
    [InlineData("CP-03", "bytes-smaller")]
    [InlineData("CP-04", "bytes-larger")]
    [InlineData("CP-07", "random-truncate")]
    public async Task CheckpointMetadataAndRandomCorruptionNeverProducesAnUnverifiedState(
        string matrixId, string mutation)
    {
        var path = _fixture.Copy(matrixId);
        await MutateLatestCheckpointAsync(path, mutation);
        await AssertFailsClosedOrFullyRecoversAsync(matrixId, path);
    }

    [Fact]
    public async Task UnsupportedRandomStateVersionIsNotTreatedAsRecoverableDamage()
    {
        const string matrixId = "CP-08";
        var path = _fixture.Copy(matrixId);
        await MutateLatestCheckpointAsync(path, "random-version");
        await AssertJournalDataFailureAsync(matrixId, path, "随机状态版本不受支持",
            recorder => recorder.LoadJournalEngineAsync(JournalFailureTemplateFixture.TargetMatchId));
    }

    [Theory]
    [InlineData("CMD-01", "json-half", "v2 恢复命令 JSON 损坏")]
    [InlineData("CMD-02", "type-missing", "v2 恢复命令缺少类型")]
    [InlineData("CMD-03", "type-invalid", "v2 恢复尾部校验失败")]
    [InlineData("CMD-04", "accepted", "v2 恢复尾部校验失败")]
    [InlineData("CMD-05", "revision", "v2 恢复尾部校验失败")]
    [InlineData("CMD-06", "state-hash", "v2 恢复尾部校验失败")]
    public async Task CorruptTailCommandFailsClosed(string matrixId, string mutation,
        string expectedError)
    {
        var path = _fixture.Copy(matrixId);
        await MutateTailCommandAsync(path, mutation);
        await AssertJournalDataFailureAsync(matrixId, path, expectedError,
            recorder => recorder.LoadJournalEngineAsync(JournalFailureTemplateFixture.TargetMatchId));
    }

    [Fact]
    public async Task MissingCommittedTailCommandCannotSilentlyRecoverAnOlderSequence()
    {
        const string matrixId = "CMD-07";
        var path = _fixture.Copy(matrixId);
        await ExecuteAsync(path, "DELETE FROM match_events WHERE match_id=$match AND sequence=65;",
            ("$match", JournalFailureTemplateFixture.TargetMatchId));
        await AssertJournalDataFailureAsync(matrixId, path,
            "v2 日志缺少耐久已提交边界对应的命令记录",
            recorder => recorder.LoadJournalEngineAsync(JournalFailureTemplateFixture.TargetMatchId));
    }

    [Fact]
    public async Task SchemaRejectsPhysicalDuplicateSequence()
    {
        const string matrixId = "DUP-01";
        var path = _fixture.Copy(matrixId);
        await using var connection = OpenConnection(path);
        var duplicate = connection.CreateCommand();
        duplicate.CommandText = """
            INSERT INTO match_events(
                match_id,sequence,received_utc,player_index,command_json,accepted,error,
                revision,state_hash,state_json,request_id)
            SELECT match_id,sequence,received_utc,player_index,command_json,accepted,error,
                   revision,state_hash,state_json,request_id
            FROM match_events WHERE match_id=$match AND sequence=65;
            """;
        duplicate.Parameters.AddWithValue("$match", JournalFailureTemplateFixture.TargetMatchId);
        var error = await Assert.ThrowsAsync<SqliteException>(() => duplicate.ExecuteNonQueryAsync());
        Assert.Equal(19, error.SqliteErrorCode);
    }

    [Fact]
    public async Task LogicalDuplicateRequestRollsBackTheSecondSequenceAndRecoversTheFirstOnly()
    {
        const string matrixId = "DUP-02";
        var path = _fixture.Copy(matrixId);
        await using var recorder = await OpenRecorderAsync(path);
        var recovered = Assert.IsType<L12JournalRecoveryState>(
            await recorder.LoadJournalEngineAsync(JournalFailureTemplateFixture.TargetMatchId));
        var command = new L12GmCommand("setLife", 0, Value: 77);
        var result = recovered.Engine.HandleGm(command);
        Assert.True(result.Accepted);
        await Assert.ThrowsAsync<SqliteException>(() => recorder.AppendAsync(recovered.Engine,
            recovered.CommandSequence + 1, -1, JsonSerializer.Serialize(command), result, "target-65"));

        await using var verify = await OpenRecorderAsync(path);
        var after = Assert.IsType<L12JournalRecoveryState>(
            await verify.LoadJournalEngineAsync(JournalFailureTemplateFixture.TargetMatchId));
        Assert.Equal(_fixture.TargetCommandSequence, after.CommandSequence);
        Assert.Equal(_fixture.TargetStateHash, after.Engine.ComputeStateHash());
        Assert.Single(after.ProcessedRequests, request => request.RequestId == "target-65");
    }

    [Fact]
    public async Task RepeatedHealthyRecoveryKeepsStateRandomSequenceAndDedupBaselineIdentical()
    {
        const string matrixId = "REC-01";
        var path = _fixture.Copy(matrixId);
        await using var firstRecorder = await OpenRecorderAsync(path);
        var first = Assert.IsType<L12JournalRecoveryState>(
            await firstRecorder.LoadJournalEngineAsync(JournalFailureTemplateFixture.TargetMatchId));
        await using var secondRecorder = await OpenRecorderAsync(path);
        var second = Assert.IsType<L12JournalRecoveryState>(
            await secondRecorder.LoadJournalEngineAsync(JournalFailureTemplateFixture.TargetMatchId));

        Assert.Equal(first.CommandSequence, second.CommandSequence);
        Assert.Equal(first.Engine.SerializeFullState(), second.Engine.SerializeFullState());
        Assert.Equal(first.Engine.ComputeStateHash(), second.Engine.ComputeStateHash());
        Assert.Equal(first.Engine.RandomState, second.Engine.RandomState);
        Assert.Equal(first.Engine.RandomDrawCount, second.Engine.RandomDrawCount);
        Assert.Equal(first.ProcessedRequests, second.ProcessedRequests);
    }

    [Fact]
    public async Task RecoveredAppendUsesHigherSequenceWithoutOverwritingOlderDamagedEvidence()
    {
        const string matrixId = "REC-02";
        var path = _fixture.Copy(matrixId);
        const string damagedHash = "damaged-checkpoint-evidence";
        await ExecuteAsync(path, """
            UPDATE match_state_checkpoints SET state_hash=$hash
            WHERE match_id=$match AND sequence=32;
            """, ("$hash", damagedHash), ("$match", JournalFailureTemplateFixture.TargetMatchId));

        await using var recorder = await OpenRecorderAsync(path);
        var recovered = Assert.IsType<L12JournalRecoveryState>(
            await recorder.LoadJournalEngineAsync(JournalFailureTemplateFixture.TargetMatchId));
        Assert.Equal(_fixture.TargetCommandSequence, recovered.CommandSequence);
        var command = new L12GmCommand("setLife", 0, Value: 91);
        var result = recovered.Engine.HandleGm(command);
        Assert.True(result.Accepted);
        await recorder.AppendAsync(recovered.Engine, recovered.CommandSequence + 1, -1,
            JsonSerializer.Serialize(command), result, "target-66");

        await using var connection = OpenConnection(path);
        var inspect = connection.CreateCommand();
        inspect.CommandText = """
            SELECT state_hash,
                   (SELECT MAX(sequence) FROM match_events WHERE match_id=$match),
                   (SELECT COUNT(*) FROM match_events WHERE match_id=$match AND sequence=66)
            FROM match_state_checkpoints WHERE match_id=$match AND sequence=32;
            """;
        inspect.Parameters.AddWithValue("$match", JournalFailureTemplateFixture.TargetMatchId);
        await using var reader = await inspect.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(damagedHash, reader.GetString(0));
        Assert.Equal(66, reader.GetInt64(1));
        Assert.Equal(1, reader.GetInt32(2));
    }

    [Fact]
    public async Task CorruptMatchDoesNotBlockHealthyMatchInTheSameReadableDatabase()
    {
        const string matrixId = "ISO-01";
        var path = _fixture.Copy(matrixId);
        await MutateTailCommandAsync(path, "state-hash");
        await AssertJournalDataFailureAsync(matrixId, path, "v2 恢复尾部校验失败",
            recorder => recorder.LoadJournalEngineAsync(JournalFailureTemplateFixture.TargetMatchId));

        await using var recorder = await OpenRecorderAsync(path);
        var healthy = Assert.IsType<L12JournalRecoveryState>(
            await recorder.LoadJournalEngineAsync(JournalFailureTemplateFixture.HealthyMatchId));
        Assert.Equal(_fixture.HealthyCommandSequence, healthy.CommandSequence);
        Assert.Equal(_fixture.HealthyStateHash, healthy.Engine.ComputeStateHash());
    }

    [Fact]
    public async Task TruncatedSqliteFileFailsClosedAsAWholeDatabase()
    {
        const string matrixId = "DB-01";
        var path = _fixture.Copy(matrixId);
        SqliteConnection.ClearAllPools();
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            stream.SetLength(Math.Max(4096, stream.Length / 2));

        await AssertWholeDatabaseFailureAsync(matrixId, path,
            recorder => recorder.LoadJournalEngineAsync(JournalFailureTemplateFixture.TargetMatchId));
    }

    private async Task AssertFailsClosedOrFullyRecoversAsync(string matrixId, string path)
    {
        try
        {
            await using var recorder = await OpenRecorderAsync(path);
            var recovered = Assert.IsType<L12JournalRecoveryState>(
                await recorder.LoadJournalEngineAsync(JournalFailureTemplateFixture.TargetMatchId));
            Assert.Equal(_fixture.TargetCommandSequence, recovered.CommandSequence);
            Assert.Equal(_fixture.TargetStateHash, recovered.Engine.ComputeStateHash());
        }
        catch (InvalidDataException)
        {
            // A verified fallback and an explicit failure closure are both safe for this metadata attack.
        }
    }

    private static async Task AssertJournalDataFailureAsync(string matrixId, string path,
        string expectedError,
        Func<MatchRecorder, Task<L12JournalRecoveryState?>> action)
    {
        try
        {
            await using var recorder = await OpenRecorderAsync(path);
            await action(recorder);
        }
        catch (InvalidDataException error)
        {
            Assert.Contains(expectedError, error.Message, StringComparison.Ordinal);
            return;
        }
        throw new XunitException($"{matrixId} red: corrupted journal was accepted; copy={path}");
    }

    private static async Task AssertWholeDatabaseFailureAsync(string matrixId, string path,
        Func<MatchRecorder, Task<L12JournalRecoveryState?>> action)
    {
        try
        {
            await using var recorder = await OpenRecorderAsync(path);
            await action(recorder);
        }
        catch (Exception error) when (error is SqliteException or IOException)
        {
            return;
        }
        throw new XunitException($"{matrixId} red: truncated SQLite database was accepted; copy={path}");
    }

    private static bool IsCorruptionFailure(Exception error)
        => error is InvalidDataException or JsonException;

    private static async Task<MatchRecorder> OpenRecorderAsync(string path)
    {
        var recorder = new MatchRecorder(path);
        recorder.AttachCatalog(JournalFailureTemplateFixture.Catalog);
        await recorder.InitializeAsync();
        return recorder;
    }

    private static SqliteConnection OpenConnection(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static async Task MutateLatestCheckpointAsync(string path, string mutation)
    {
        await using var connection = OpenConnection(path);
        if (mutation is "truncate-one" or "truncate-half" or "random-truncate")
        {
            var column = mutation == "random-truncate" ? "random_state_blob" : "state_blob";
            var read = connection.CreateCommand();
            read.CommandText = $"""
                SELECT {column} FROM match_state_checkpoints
                WHERE match_id=$match AND sequence=64;
                """;
            read.Parameters.AddWithValue("$match", JournalFailureTemplateFixture.TargetMatchId);
            var original = Assert.IsType<byte[]>(await read.ExecuteScalarAsync());
            var length = mutation == "truncate-one" ? original.Length - 1 : original.Length / 2;
            var damaged = original[..Math.Max(1, length)];
            var updateBlob = connection.CreateCommand();
            updateBlob.CommandText = $"""
                UPDATE match_state_checkpoints SET {column}=$value
                WHERE match_id=$match AND sequence=64;
                """;
            updateBlob.Parameters.AddWithValue("$value", damaged);
            updateBlob.Parameters.AddWithValue("$match", JournalFailureTemplateFixture.TargetMatchId);
            Assert.Equal(1, await updateBlob.ExecuteNonQueryAsync());
            return;
        }

        var update = connection.CreateCommand();
        update.CommandText = mutation switch
        {
            "bytes-smaller" => """
                UPDATE match_state_checkpoints SET uncompressed_bytes=uncompressed_bytes-1
                WHERE match_id=$match AND sequence=64;
                """,
            "bytes-larger" => """
                UPDATE match_state_checkpoints SET uncompressed_bytes=uncompressed_bytes+1
                WHERE match_id=$match AND sequence=64;
                """,
            "state-hash" => """
                UPDATE match_state_checkpoints SET state_hash='bad-checkpoint-hash'
                WHERE match_id=$match AND sequence=64;
                """,
            "revision" => """
                UPDATE match_state_checkpoints SET revision=revision+1
                WHERE match_id=$match AND sequence=64;
                """,
            "random-version" => """
                UPDATE match_state_checkpoints SET random_state_version=999
                WHERE match_id=$match AND sequence=64;
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null),
        };
        update.Parameters.AddWithValue("$match", JournalFailureTemplateFixture.TargetMatchId);
        Assert.Equal(1, await update.ExecuteNonQueryAsync());
    }

    private static async Task MutateTailCommandAsync(string path, string mutation)
    {
        var sql = mutation switch
        {
            "json-half" => """
                UPDATE match_events SET command_json=substr(command_json,1,length(command_json)/2)
                WHERE match_id=$match AND sequence=65;
                """,
            "type-missing" => """
                UPDATE match_events SET command_json='{"targetPlayer":0,"value":99}'
                WHERE match_id=$match AND sequence=65;
                """,
            "type-invalid" => """
                UPDATE match_events SET command_json='{"type":"not-a-command"}'
                WHERE match_id=$match AND sequence=65;
                """,
            "accepted" => """
                UPDATE match_events SET accepted=CASE accepted WHEN 1 THEN 0 ELSE 1 END
                WHERE match_id=$match AND sequence=65;
                """,
            "revision" => """
                UPDATE match_events SET revision=revision+1
                WHERE match_id=$match AND sequence=65;
                """,
            "state-hash" => """
                UPDATE match_events SET state_hash='bad-command-hash'
                WHERE match_id=$match AND sequence=65;
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null),
        };
        await ExecuteAsync(path, sql, ("$match", JournalFailureTemplateFixture.TargetMatchId));
    }

    private static async Task ExecuteAsync(string path, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = OpenConnection(path);
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }
}

public sealed class JournalFailureTemplateFixture : IDisposable
{
    internal const string TargetMatchId = "lc03b-target";
    internal const string HealthyMatchId = "lc03b-healthy";
    internal static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "l12-lc03b-journal", Guid.NewGuid().ToString("N"));
    private readonly string _templatePath;

    public JournalFailureTemplateFixture()
    {
        Directory.CreateDirectory(_root);
        _templatePath = Path.Combine(_root, "template.db");
        CreateTemplateAsync().GetAwaiter().GetResult();
    }

    internal long TargetCommandSequence { get; private set; }
    internal string TargetStateHash { get; private set; } = string.Empty;
    internal long HealthyCommandSequence { get; private set; }
    internal string HealthyStateHash { get; private set; } = string.Empty;

    internal string Copy(string matrixId)
    {
        var path = Path.Combine(_root, $"{matrixId.ToLowerInvariant()}.db");
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _templatePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
        return path;
    }

    private async Task CreateTemplateAsync()
    {
        await using var recorder = new MatchRecorder(_templatePath);
        await recorder.InitializeAsync();
        recorder.AttachCatalog(Catalog);
        var target = new L12GameEngine(Catalog, TargetMatchId, "LC03BT", 2026092601,
            ["合成甲", "合成乙"], [0, 1], skipPreparation: true, stateFormatVersion: 2);
        await recorder.StartAsync(target);
        for (var sequence = 1; sequence <= 65; sequence++)
        {
            var command = new L12GmCommand("setLife", 0, Value: 10 + sequence % 7);
            var result = target.HandleGm(command);
            Assert.True(result.Accepted);
            await recorder.AppendAsync(target, sequence, -1, JsonSerializer.Serialize(command), result,
                $"target-{sequence}");
        }
        TargetCommandSequence = 65;
        TargetStateHash = target.ComputeStateHash();

        var healthy = new L12GameEngine(Catalog, HealthyMatchId, "LC03BH", 2026092602,
            ["健康甲", "健康乙"], [0, 1], skipPreparation: true, stateFormatVersion: 2);
        await recorder.StartAsync(healthy);
        for (var sequence = 1; sequence <= 5; sequence++)
        {
            var command = new L12GmCommand("setLife", 1, Value: 15 + sequence);
            var result = healthy.HandleGm(command);
            Assert.True(result.Accepted);
            await recorder.AppendAsync(healthy, sequence, -1, JsonSerializer.Serialize(command), result,
                $"healthy-{sequence}");
        }
        HealthyCommandSequence = 5;
        HealthyStateHash = healthy.ComputeStateHash();
        SqliteConnection.ClearAllPools();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
