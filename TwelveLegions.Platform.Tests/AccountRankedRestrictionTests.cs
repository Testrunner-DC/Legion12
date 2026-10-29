using SchemaContract = TwelveLegions.Platform.Tests.PlatformSchemaTestContract;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class AccountRankedRestrictionTests
{
    [Fact]
    public async Task StandaloneSevenDayBanDoesNotRequireMatchOrRevokeSession()
    {
        await using var fixture = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var store = fixture.Store;
        var target = store.Authenticate("Bearer " + fixture.PlayerToken)!;
        var admin = store.Authenticate("Bearer " + fixture.AdminToken)!;
        var sessionBefore = store.AuthenticateTokenSession(fixture.PlayerToken)!;
        var permissionVersion = target.PermissionVersion;
        Assert.Null(store.RankedEntryBlock(target.Id, DateTimeOffset.UtcNow));
        Assert.Empty(store.RankedIntegrityDecisions(admin).Items);
        Assert.Empty(store.RankedIntegrityNotifications(target).Items);

        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"/api/admin/accounts/{Uri.EscapeDataString(target.Id)}/ranked-restriction")
        {
            Content = JsonContent.Create(new
            {
                restrictionDays = 7,
                reason = "synthetic-account-only-seven-day-ban",
                expectedVersion = store.Version,
                idempotencyKey = "account-only-seven-day-baseline",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminToken);

        using var response = await fixture.Client.SendAsync(request);

        // This must fail on the existing HTTP server with a missing route, not
        // through a reference to a not-yet-implemented DTO or store method.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var current = store.AuthenticateTokenSession(fixture.PlayerToken);
        Assert.NotNull(current);
        Assert.Equal(sessionBefore.SessionId, current!.SessionId);
        Assert.Equal(permissionVersion, current.Account.PermissionVersion);
        Assert.False(current.Account.Disabled);
        Assert.NotNull(store.RankedEntryBlock(target.Id, DateTimeOffset.UtcNow));
        Assert.Null(store.RankedEntryBlock(target.Id, DateTimeOffset.UtcNow.AddDays(8)));
        Assert.Empty(store.RankedIntegrityDecisions(admin).Items);
        Assert.Empty(store.RankedIntegrityNotifications(target).Items);

        var reloaded = new L12PlatformStore(fixture.MirrorPath);
        Assert.NotNull(reloaded.RankedEntryBlock(target.Id, DateTimeOffset.UtcNow));
        Assert.NotNull(reloaded.AuthenticateTokenSession(fixture.PlayerToken));
        Assert.Empty(reloaded.RankedIntegrityDecisions(admin).Items);
    }

    [Fact]
    public async Task WholeDaysReplayAndClearPreserveTokensWithoutExtendingTheBan()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var target = f.Store.Authenticate("Bearer " + f.PlayerToken)!;
        var admin = f.Store.Authenticate("Bearer " + f.AdminToken)!;
        var beforeSession = f.Store.AuthenticateTokenSession(f.PlayerToken)!;
        var otherToken = f.Store.Login(target.Username, "password-123").Token!;
        var otherSession = f.Store.AuthenticateTokenSession(otherToken)!;
        var adminSession = f.Store.AuthenticateTokenSession(f.AdminToken)!;
        var command = new { restrictionDays = 7, reason = "synthetic-seven-days", expectedVersion = f.Store.Version,
            idempotencyKey = "manual-ban-replay" };
        using var first = await Mutate(f, target.Id, command);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstResult = (await first.Content.ReadFromJsonAsync<L12AccountRankedRestrictionOperation>())!;
        var until = firstResult.Restriction.RestrictionUntil!.Value;
        Assert.Equal(firstResult.Restriction.SetAt!.Value.AddDays(7), until);
        Assert.NotNull(f.Store.RankedEntryBlock(target.Id, until.AddTicks(-1)));
        Assert.Null(f.Store.RankedEntryBlock(target.Id, until));
        using var replay = await Mutate(f, target.Id, command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var repeated = (await replay.Content.ReadFromJsonAsync<L12AccountRankedRestrictionOperation>())!;
        Assert.Equal(until, repeated.Restriction.RestrictionUntil);
        Assert.Single(f.Store.AdminAudit(category: "account").Where(row => row.Action == "ranked-ban-set"));
        using var conflict = await Mutate(f, target.Id, new { restrictionDays = 8, command.reason,
            command.expectedVersion, command.idempotencyKey });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(until, f.Store.AccountRankedRestriction(admin, target.Id).RestrictionUntil);

        var clear = new { reason = "synthetic-clear-only-manual", expectedVersion = f.Store.Version,
            idempotencyKey = "manual-ban-clear-replay" };
        using var cleared = await Mutate(f, target.Id, clear, clear: true);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        using var clearReplay = await Mutate(f, target.Id, clear, clear: true);
        Assert.Equal(HttpStatusCode.OK, clearReplay.StatusCode);
        Assert.Null(f.Store.RankedEntryBlock(target.Id, DateTimeOffset.UtcNow));
        Assert.Single(f.Store.AdminAudit(category: "account").Where(row => row.Action == "ranked-ban-clear"));
        Assert.NotNull(f.Store.AccountRankedRestriction(admin, target.Id).ClearedAt);
        var current = f.Store.AuthenticateTokenSession(f.PlayerToken)!;
        Assert.Equal(beforeSession, current);
        Assert.Equal(otherSession, f.Store.AuthenticateTokenSession(otherToken));
        Assert.Equal(adminSession, f.Store.AuthenticateTokenSession(f.AdminToken));
        var reloaded = new L12PlatformStore(f.MirrorPath);
        Assert.Equal(beforeSession, reloaded.AuthenticateTokenSession(f.PlayerToken));
        Assert.Equal(otherSession, reloaded.AuthenticateTokenSession(otherToken));
        Assert.Null(reloaded.RankedEntryBlock(target.Id, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3651)]
    public async Task InvalidWholeDayDurationsNeverCreateARestriction(int? days)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var target = f.Store.Authenticate("Bearer " + f.PlayerToken)!;
        using var response = await Mutate(f, target.Id, new { restrictionDays = days, reason = "synthetic-invalid-days",
            expectedVersion = f.Store.Version, idempotencyKey = "invalid-days" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(f.Store.RankedEntryBlock(target.Id, DateTimeOffset.UtcNow));
        Assert.Equal(target.PermissionVersion, f.Store.Account(target.Id)!.PermissionVersion);
    }

    [Theory]
    [InlineData("fraction")]
    [InlineData("reason")]
    [InlineData("missing-key")]
    [InlineData("missing-version")]
    [InlineData("stale-version")]
    [InlineData("player")]
    public async Task PermissionAndInputPreconditionsCannotBeBypassed(string shape)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var target = f.Store.Authenticate("Bearer " + f.PlayerToken)!;
        var body = new Dictionary<string, object?> { ["restrictionDays"] = shape == "fraction" ? 1.5 : 7,
            ["reason"] = shape == "reason" ? "  " : "synthetic-precondition",
            ["expectedVersion"] = f.Store.Version, ["idempotencyKey"] = "precondition-" + shape };
        if (shape == "missing-key") body.Remove("idempotencyKey");
        if (shape == "missing-version") body.Remove("expectedVersion");
        if (shape == "stale-version") body["expectedVersion"] = -1;
        using var response = await Mutate(f, target.Id, body, token: shape == "player" ? f.PlayerToken : null);
        var expected = shape switch { "player" => HttpStatusCode.Forbidden,
            "missing-version" => HttpStatusCode.PreconditionRequired, "stale-version" => HttpStatusCode.Conflict,
            _ => HttpStatusCode.BadRequest };
        Assert.Equal(expected, response.StatusCode);
        Assert.Null(f.Store.RankedEntryBlock(target.Id, DateTimeOffset.UtcNow));
        Assert.NotNull(f.Store.AuthenticateTokenSession(f.PlayerToken));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(21)]
    public async Task ClearManualBanDoesNotRevokeTheIndependentIntegrityDecision(int manualDays)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var store = f.Store;
        var target = store.Authenticate("Bearer " + f.PlayerToken)!;
        var admin = store.Authenticate("Bearer " + f.AdminToken)!;
        var opponentRegistration = store.Register("mbother", "Password123!");
        Assert.True(opponentRegistration.Success, opponentRegistration.Message);
        var opponent = opponentRegistration.Account!;
        store.SelectRankedFaction(target.Id, "order"); store.SelectRankedFaction(opponent.Id, "chaos");
        for (var index = 0; index < 5; index++)
            store.SettleRankedMatch($"manual-ban-placement-{index}", target.Id, opponent.Id, index % 2);
        store.SettleRankedMatch("manual-ban-integrity", target.Id, opponent.Id, 0);
        var input = new L12RankedIntegrityActionInput("manual-ban-integrity", "confirmed", ["manual-ban-integrity"],
            [target.Id], 14, "synthetic-real-settlement", "synthetic-integrity-ban",
            Scope: "account", TargetAccountIds: [target.Id]);
        var preview = store.PreviewRankedIntegrityAction(admin, input);
        Assert.True(preview.CanConfirm, string.Join(";", preview.BlockingReasons));
        var decision = store.ConfirmRankedIntegrityAction(admin, input, preview.Revision, new("manual-ban-integrity"));
        var integrityUntil = Assert.Single(decision.AccountEffects).RestrictionUntil;
        var notifications = store.RankedIntegrityNotifications(target).Items.Count;
        using var set = await Mutate(f, target.Id, new { restrictionDays = manualDays, reason = "synthetic-manual-overlap",
            expectedVersion = store.Version, idempotencyKey = "manual-over-integrity" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var overlap = store.AccountRankedRestriction(admin, target.Id);
        Assert.Equal(manualDays > 14 ? overlap.RestrictionUntil : integrityUntil, overlap.EffectiveRestrictionUntil);
        using var clear = await Mutate(f, target.Id, new { reason = "synthetic-manual-clear",
            expectedVersion = store.Version, idempotencyKey = "manual-clear-not-integrity" }, clear: true);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.NotNull(store.RankedEntryBlock(target.Id, DateTimeOffset.UtcNow));
        Assert.Null(store.RankedEntryBlock(opponent.Id, DateTimeOffset.UtcNow));
        Assert.Equal(integrityUntil, store.AccountRankedRestriction(admin, target.Id).EffectiveRestrictionUntil);
        Assert.Single(store.RankedIntegrityDecisions(admin).Items);
        Assert.Equal(notifications, store.RankedIntegrityNotifications(target).Items.Count);
        Assert.Equal("applied", store.RankedSettlement("manual-ban-integrity", opponent.Id)!.RewardStatus);
    }

    [Fact]
    public async Task FailedCommitRollsBackRestrictionAuditAndCommandWithTheExistingTransaction()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var target = f.Store.Authenticate("Bearer " + f.PlayerToken)!;
        var beforeVersion = f.Store.Version;
        var beforeSession = f.Store.AuthenticateTokenSession(f.PlayerToken)!;
        f.Store.StorageFailureInjector = phase => { if (phase == "before-commit") throw new IOException("synthetic-account-ban-commit-failure"); };
        try
        {
            using var response = await Mutate(f, target.Id, new { restrictionDays = 7, reason = "synthetic-save-failure",
                expectedVersion = beforeVersion, idempotencyKey = "manual-ban-save-failure" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally { f.Store.StorageFailureInjector = null; }
        Assert.Equal(beforeVersion, f.Store.Version);
        Assert.Null(f.Store.RankedEntryBlock(target.Id, DateTimeOffset.UtcNow));
        Assert.Equal(beforeSession, f.Store.AuthenticateTokenSession(f.PlayerToken));
        Assert.DoesNotContain(f.Store.AdminCommands(), row => row.IdempotencyKey == "manual-ban-save-failure");
        Assert.DoesNotContain(f.Store.AdminAudit(category: "account"), row => row.Action == "ranked-ban-set");
        var reloaded = new L12PlatformStore(f.MirrorPath);
        Assert.Null(reloaded.RankedEntryBlock(target.Id, DateTimeOffset.UtcNow));
        Assert.Equal(beforeVersion, reloaded.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SchemaNineMigrationIsAtomicAndPreservesAnEmptyOptionalRestriction(bool failMigration)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var target = f.Store.Authenticate("Bearer " + f.PlayerToken)!;
        using var connection = new SqliteConnection($"Data Source={f.Store.TransactionalStoragePath};Mode=ReadWrite;Pooling=False");
        connection.Open();
        ExecuteSql(connection, "UPDATE platform_state SET schema_version=9; UPDATE storage_meta SET value='9' WHERE key='schema_version';");
        var legacy = JsonNode.Parse(Scalar(connection, "SELECT snapshot_json FROM platform_state WHERE singleton_id=1;")!)!.AsObject();
        var accounts = (legacy["Accounts"] ?? legacy["accounts"])!.AsArray();
        foreach (var account in accounts)
        {
            account!.AsObject().Remove("RankedRestriction");
            account.AsObject().Remove("rankedRestriction");
        }
        var legacyJson = legacy.ToJsonString();
        using (var prepareLegacy = connection.CreateCommand())
        {
            prepareLegacy.CommandText = "UPDATE platform_state SET snapshot_json=$json,snapshot_sha256=$hash WHERE singleton_id=1;";
            prepareLegacy.Parameters.AddWithValue("$json", legacyJson);
            prepareLegacy.Parameters.AddWithValue("$hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyJson))).ToLowerInvariant());
            prepareLegacy.ExecuteNonQuery();
        }
        var before = Scalar(connection, "SELECT snapshot_json FROM platform_state WHERE singleton_id=1;");
        var beforeHash = Scalar(connection, "SELECT snapshot_sha256 FROM platform_state WHERE singleton_id=1;");
        var generation = Scalar(connection, "SELECT value FROM storage_meta WHERE key='ranked_broadcast_generation';");
        if (failMigration)
        {
            ExecuteSql(connection, "CREATE TRIGGER fail_account_ban_schema BEFORE UPDATE OF schema_version ON platform_state WHEN NEW.schema_version=10 BEGIN SELECT RAISE(ABORT,'synthetic-account-ban-schema-failure'); END;");
            var failure = Assert.Throws<L12PlatformStorageUnavailableException>(() => new L12PlatformStore(f.MirrorPath));
            Assert.Contains("synthetic-account-ban-schema-failure", failure.ToString(), StringComparison.Ordinal);
            Assert.Equal("9", Scalar(connection, "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
            Assert.Equal("9", Scalar(connection, "SELECT value FROM storage_meta WHERE key='schema_version';"));
        }
        else
        {
            var migrated = new L12PlatformStore(f.MirrorPath);
            Assert.Null(migrated.RankedEntryBlock(target.Id, DateTimeOffset.UtcNow));
            Assert.NotNull(migrated.AuthenticateTokenSession(f.PlayerToken));
            Assert.Equal(SchemaContract.Current.ToString(), Scalar(connection, "SELECT schema_version FROM platform_state WHERE singleton_id=1;"));
            Assert.Equal(SchemaContract.Current.ToString(), Scalar(connection, "SELECT value FROM storage_meta WHERE key='schema_version';"));
        }
        Assert.Equal(before, Scalar(connection, "SELECT snapshot_json FROM platform_state WHERE singleton_id=1;"));
        Assert.Equal(beforeHash, Scalar(connection, "SELECT snapshot_sha256 FROM platform_state WHERE singleton_id=1;"));
        Assert.Equal(generation, Scalar(connection, "SELECT value FROM storage_meta WHERE key='ranked_broadcast_generation';"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BanOnlyBlocksNewRankedAdmissionAndDoesNotKickAnExistingRankedRoom(bool ongoingGame)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var store = f.Store;
        var target = store.Authenticate("Bearer " + f.PlayerToken)!;
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(f.MirrorPath)!, "account-ban-games.db"));
        await recorder.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var manager = new L12RoomManager(catalog, recorder, store, () => now);
        store.SelectRankedFaction(target.Id, "order");
        var session = Guid.NewGuid();
        manager.Connect(session, target.Id, target.Username);
        string? matchId = null;
        if (ongoingGame)
        {
            var otherRegistration = store.Register("mbactive", "Password123!");
            Assert.True(otherRegistration.Success, otherRegistration.Message);
            var other = otherRegistration.Account!;
            store.SelectRankedFaction(other.Id, "chaos");
            var otherSession = Guid.NewGuid(); manager.Connect(otherSession, other.Id, other.Username);
            await manager.JoinMatchmakingAsync(session, "ranked", null);
            var paired = await manager.JoinMatchmakingAsync(otherSession, "ranked", null);
            matchId = GameState(paired, session).GetProperty("state").GetProperty("matchId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(matchId));
        }
        using var response = await Mutate(f, target.Id, new { restrictionDays = 7, reason = "synthetic-admission-only",
            expectedVersion = store.Version, idempotencyKey = "admission-only-" + ongoingGame });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(store.RankedEntryBlock(target.Id, DateTimeOffset.UtcNow));
        Assert.NotNull(store.AuthenticateTokenSession(f.PlayerToken));
        if (ongoingGame)
        {
            var recovered = await manager.RecoveryStateAsync(session);
            Assert.Equal(matchId, GameState(recovered, session).GetProperty("state").GetProperty("matchId").GetString());
            Assert.Null(store.RankedSettlement(matchId!, target.Id));
        }
        else
        {
            var rejected = await manager.JoinMatchmakingAsync(session, "ranked", null);
            Assert.Contains(rejected, message => JsonSerializer.SerializeToElement(message.Payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)).TryGetProperty("message", out var messageText)
                && messageText.GetString()!.Contains("排位资格"));
            var casual = await manager.JoinMatchmakingAsync(session, "casual", null);
            Assert.Contains(casual, message => JsonSerializer.SerializeToElement(message.Payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)).TryGetProperty("queued", out var queued)
                && queued.ValueKind == JsonValueKind.True);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommittedBanDuringPairPreparationRejectsBothWithoutPublishingMatch(bool banTriggeringSeat)
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var first = f.Store.Authenticate("Bearer " + f.PlayerToken)!;
        var registered = f.Store.Register("racepair", "Password123!");
        Assert.True(registered.Success, registered.Message);
        var second = registered.Account!;
        f.Store.SelectRankedFaction(first.Id, "order");
        f.Store.SelectRankedFaction(second.Id, "chaos");
        var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
        await using var recorder = new MatchRecorder(Path.Combine(Path.GetDirectoryName(f.MirrorPath)!, "pair-race.db"));
        await recorder.InitializeAsync();
        var manager = new L12RoomManager(catalog, recorder, f.Store);
        var sessions = new[] { Guid.NewGuid(), Guid.NewGuid() };
        manager.Connect(sessions[0], first.Id, first.Username);
        manager.Connect(sessions[1], second.Id, second.Username);
        var reached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.RankedMatchmakingBeforeFinalAdmissionInjector = async () =>
        {
            reached.TrySetResult(true);
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
        };
        Task<IReadOnlyList<OutgoingMessage>>? pairing = null;
        try
        {
            await manager.JoinMatchmakingAsync(sessions[0], "ranked", null);
            pairing = manager.JoinMatchmakingAsync(sessions[1], "ranked", null);
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var banned = banTriggeringSeat ? second : first;
            using var response = await Mutate(f, banned.Id, new { restrictionDays = 7,
                reason = "synthetic-inflight-pair", expectedVersion = f.Store.Version,
                idempotencyKey = "inflight-pair-" + banTriggeringSeat });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); // real committed admin transaction
            release.TrySetResult(true);
            var messages = await pairing.WaitAsync(TimeSpan.FromSeconds(5));
            var wire = messages.Select(message => JsonSerializer.SerializeToElement(message.Payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))).ToArray();
            Assert.DoesNotContain(wire, row => row.GetProperty("type").GetString() is "matchmakingFound" or "gameState");
            foreach (var session in sessions)
                Assert.Contains(messages, message => message.SessionId == session
                    && JsonSerializer.SerializeToElement(message.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                        .GetProperty("type").GetString() == "matchmakingRejected");
            Assert.Contains(wire, row => row.TryGetProperty("message", out var text)
                && text.GetString()!.Contains("排位资格", StringComparison.Ordinal));
            Assert.Equal(0, manager.RuntimeStats().RoomCount);
            Assert.Equal(0, manager.RuntimeStats().ActiveGameCount);
            Assert.Empty(await recorder.ListMatchesAsync());
        }
        finally
        {
            release.TrySetResult(true);
            manager.RankedMatchmakingBeforeFinalAdmissionInjector = null;
            if (pairing is not null) await pairing.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ManualBanDatabaseCannotBeDowngradedByTheExistingOfflineRehearsal()
    {
        await using var f = await AdminWorkbenchTimingTests.Fixture.Create(false);
        var target = f.Store.Authenticate("Bearer " + f.PlayerToken)!;
        using var set = await Mutate(f, target.Id, new { restrictionDays = 7, reason = "synthetic-recovery-boundary",
            expectedVersion = f.Store.Version, idempotencyKey = "ban-no-downgrade" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Throws<InvalidDataException>(() => L12PlatformStore.RevertRankedBroadcastObjectStorageForRehearsal(
            f.Store.TransactionalStoragePath));
        var recovered = new L12PlatformStore(f.MirrorPath);
        Assert.Equal(SchemaContract.Current, recovered.StorageStatus().SchemaVersion);
        Assert.NotNull(recovered.RankedEntryBlock(target.Id, DateTimeOffset.UtcNow));
        // This exercises the actual supported SQLite recovery/rehearsal path;
        // it does not claim to run an old binary against a standalone JSON mirror.
    }

    private static JsonElement GameState(IReadOnlyList<OutgoingMessage> messages, Guid session)
        => messages.Where(message => message.SessionId == session)
            .Select(message => JsonSerializer.SerializeToElement(message.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
            .Single(payload => payload.TryGetProperty("type", out var type) && type.GetString() == "gameState");

    private static async Task<HttpResponseMessage> Mutate(AdminWorkbenchTimingTests.Fixture f, string accountId,
        object body, bool clear = false, string? token = null)
    {
        using var request = new HttpRequestMessage(clear ? HttpMethod.Post : HttpMethod.Put,
            $"/api/admin/accounts/{Uri.EscapeDataString(accountId)}/ranked-restriction" + (clear ? "/clear" : ""))
        { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? f.AdminToken);
        return await f.Client.SendAsync(request);
    }

    private static string? Scalar(SqliteConnection connection, string sql)
    { using var command = connection.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar()?.ToString(); }
    private static void ExecuteSql(SqliteConnection connection, string sql)
    { using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
}
