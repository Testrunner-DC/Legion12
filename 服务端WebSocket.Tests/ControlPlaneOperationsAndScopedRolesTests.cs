using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

[Collection("Platform environment")]
public sealed class ControlPlaneOperationsAndScopedRolesTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void DurableRolesArePlayerAndAdminAndLegacyRolesMigratePersistently()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var original = new L12PlatformStore(path);
            var account = original.Register("LegacyRoleUser", "password-123").Account!;
            var mirror = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var row = mirror["Accounts"]!.AsArray().OfType<JsonObject>()
                .Single(item => item["Id"]!.GetValue<string>() == account.Id);
            row["Role"] = "release-manager";
            File.WriteAllText(path, mirror.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            SqliteConnection.ClearAllPools();
            File.Delete(Path.Combine(root, "platform.db"));

            var migrated = new L12PlatformStore(path);

            Assert.Equal(new[] { "admin", "player" }, L12Authorization.Roles);
            Assert.Equal("player", migrated.Account(account.Id)!.Role);
            Assert.False(L12Authorization.IsKnownRole("support"));
            Assert.False(L12Authorization.IsKnownRole("editor"));
            Assert.False(L12Authorization.IsKnownRole("referee"));
            Assert.False(L12Authorization.IsKnownRole("organizer"));
            Assert.False(L12Authorization.IsKnownRole("release-manager"));
            Assert.Equal("player", JsonNode.Parse(File.ReadAllText(path))!["Accounts"]!.AsArray()
                .OfType<JsonObject>().Single(item => item["Id"]!.GetValue<string>() == account.Id)["Role"]!
                .GetValue<string>());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void TournamentCreatorAndFriendRefereeReceiveOnlyTournamentScopedAuthority()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var organizer = store.Register("ScopedOrganizer", "password-123").Account!;
            var referee = store.Register("ScopedReferee", "password-123").Account!;
            var outsider = store.Register("ScopedOutsider", "password-123").Account!;

            Assert.True(store.SendFriendRequest(organizer.Id, referee.Id).Success);
            Assert.True(store.ResolveFriendRequest(referee.Id, organizer.Id, true).Success);
            var tournament = store.CreateTournament(organizer, CreateTournamentPayload([referee.Id]),
                Context("scoped-create"), true);

            Assert.Equal(organizer.Id, tournament.OrganizerAccountId);
            Assert.Equal("player", store.Account(referee.Id)!.Role);
            Assert.False(L12Authorization.HasPermission(referee, L12Permission.TournamentRulingsWrite));
            Assert.False(L12Authorization.HasPermission(organizer, L12Permission.TournamentsManage));
            Assert.Equal(referee.Id, Assert.Single(tournament.Referees).AccountId);
            Assert.Throws<L12TournamentScopeException>(() => store.SetTournamentStaff(outsider,
                tournament.Id, new L12TournamentStaffPayload([]), tournament.Version,
                Context("outsider-staff"), true));

            var otherOrganizer = store.Register("OtherOrganizer", "password-123").Account!;
            Assert.Throws<ArgumentException>(() => store.CreateTournament(otherOrganizer,
                CreateTournamentPayload([referee.Id]), Context("not-friends"), true));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OperationsConfigPreviewApplyReplayHistoryRollbackAndRestartAreConsistent()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var store = new L12PlatformStore(path);
            var admin = store.Login("Admin", "L12master").Account!;
            var initial = store.OperationsConfig(admin);
            var changed = initial.Config with
            {
                Season = initial.Config.Season with { Id = "S02", Name = "S02" },
                CardRestrictions = [new L12CardRestrictionConfig("S01-0001", 0, "season ban")],
                Maintenance = new L12MaintenanceConfig(true, "计划维护", null, null),
            };

            var preview = store.PreviewOperationsConfig(admin, changed, initial.Version,
                Context("ops-preview") with { Permission = "admin.operations.write" });
            Assert.True(preview.Valid);
            Assert.Contains("season", preview.Changes);
            Assert.Equal(initial.Version, store.OperationsConfig(admin).Version);

            var commandId = Guid.NewGuid().ToString("N");
            var envelope = new L12AdminCommandEnvelope<L12OperationsConfigPayload>(commandId,
                "ops-apply-1", "operations.config.apply", admin, DateTimeOffset.UtcNow,
                "operations:config", "activate S02", false, initial.Version, changed,
                Context("ops-apply-1") with
                {
                    Permission = "admin.operations.write",
                    CommandId = commandId,
                    IdempotencyKey = "ops-apply-1",
                    ExpectedVersion = initial.Version,
                });
            var bus = new L12AdminCommandBus(store);
            L12AdminCommandResult<L12OperationsConfigOperationView> Execute()
                => bus.Execute(envelope, L12Permission.AdminOperationsWrite,
                    current => L12AdminCommandResult<L12OperationsConfigOperationView>.Ok(
                        store.ApplyOperationsConfig(current.Actor, current.Payload,
                            current.ExpectedVersion!.Value, current.Reason, current.AuditContext)));

            var applied = Execute();
            var replay = Execute();
            Assert.True(applied.Success);
            Assert.False(applied.Pending);
            Assert.True(replay.Success);
            Assert.True(replay.Replayed);
            Assert.Equal(initial.Version + 1, store.OperationsConfig(admin).Version);
            var appliedVersionId = applied.Value!.HistoryEntry.Id;

            var reloaded = new L12PlatformStore(path);
            var reloadedAdmin = reloaded.Login("Admin", "L12master").Account!;
            var persisted = reloaded.OperationsConfig(reloadedAdmin);
            Assert.Equal("S02", persisted.Config.Season.Id);
            Assert.True(persisted.Config.Maintenance.Enabled);
            Assert.Contains(reloaded.OperationsConfigHistory(reloadedAdmin), item => item.Id == appliedVersionId);

            var rollback = reloaded.RollbackOperationsConfig(reloadedAdmin, initial.VersionId,
                persisted.Version, "restore initial", Context("ops-rollback"));
            Assert.True(rollback.Applied);
            Assert.Equal("S01", rollback.Current.Config.Season.Id);
            Assert.Equal(persisted.Version + 1, rollback.Current.Version);
            Assert.StartsWith("rollback:", rollback.HistoryEntry.Action);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OperationsConfigRejectsUnlockingOrMovingAnnihilation()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var current = store.OperationsConfig(admin);

            var unlocked = current.Config with
            {
                DisasterPool = new L12SeasonDisasterPoolConfig(current.Config.DisasterPool.CardIds, false),
            };
            var moved = current.Config with
            {
                DisasterPool = new L12SeasonDisasterPoolConfig(
                    [L12PlatformStore.AnnihilationCardId, "S01-DS01"], true),
            };

            Assert.Equal("annihilation_locked", Assert.Throws<L12OperationsConfigException>(() =>
                store.PreviewOperationsConfig(admin, unlocked, current.Version, Context("unlock"))).Code);
            Assert.Equal("annihilation_locked", Assert.Throws<L12OperationsConfigException>(() =>
                store.PreviewOperationsConfig(admin, moved, current.Version, Context("move"))).Code);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OperationsConfigAcceptsNineDisastersIncludingFinalAnnihilation()
    {
        var root = TempRoot();
        try
        {
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
            var admin = store.Login("Admin", "L12master").Account!;
            var current = store.OperationsConfig(admin);
            var ordinary = current.Config.DisasterPool.CardIds
                .Where(id => !id.Equals(L12PlatformStore.AnnihilationCardId, StringComparison.OrdinalIgnoreCase))
                .Take(8).ToArray();
            Assert.Equal(8, ordinary.Length);
            var valid = current.Config with
            {
                DisasterPool = new L12SeasonDisasterPoolConfig(
                    ordinary.Append(L12PlatformStore.AnnihilationCardId).ToArray(), true),
            };

            var preview = store.PreviewOperationsConfig(admin, valid, current.Version, Context("nine-disasters"));
            Assert.Equal(9, preview.Normalized.DisasterPool.CardIds.Count);

            var invalid = valid with
            {
                DisasterPool = new L12SeasonDisasterPoolConfig(
                    ordinary.Take(7).Append(L12PlatformStore.AnnihilationCardId).ToArray(), true),
            };
            Assert.Equal("invalid_disaster_pool", Assert.Throws<L12OperationsConfigException>(() =>
                store.PreviewOperationsConfig(admin, invalid, current.Version, Context("eight-disasters"))).Code);
            store.ApplyOperationsConfig(admin, valid, current.Version, "activate nine disasters",
                Context("apply-nine-disasters"));
            Assert.True(store.CaptureOperationsPolicy().IsSeasonDisasterModeAvailable(DateTimeOffset.UtcNow));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OperationsPolicyBuildsAuthoritativeRankedCasualFriendlyAndSandboxScopes()
    {
        var root = TempRoot();
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var current = store.OperationsConfig(admin);
            store.ApplyOperationsConfig(admin, current.Config with
            {
                CardRestrictions = [new L12CardRestrictionConfig("S01-0001", 0, "scope regression")],
            }, current.Version, "scope regression", Context("scope-regression"));
            var policy = store.CaptureOperationsPolicy();

            var ranked = policy.ForRankedMatch();
            Assert.Equal("ranked", ranked.DefaultRoomConfig.MatchModeId);
            Assert.Equal("season", ranked.DefaultRoomConfig.DisasterMode);
            Assert.NotEmpty(ranked.DisasterCardIds);
            Assert.Single(ranked.CardRestrictions);

            var casual = policy.ForCasualMatch();
            Assert.Equal("casual", casual.DefaultRoomConfig.MatchModeId);
            Assert.Equal("all", casual.DefaultRoomConfig.DisasterMode);
            Assert.Empty(casual.DisasterCardIds);
            Assert.Empty(casual.CardRestrictions);

            var unrestrictedFriendly = policy.ForFriendlyRoom(false, "random");
            Assert.Equal("friendly", unrestrictedFriendly.DefaultRoomConfig.MatchModeId);
            Assert.Equal("random", unrestrictedFriendly.DefaultRoomConfig.DisasterMode);
            Assert.Empty(unrestrictedFriendly.DisasterCardIds);
            Assert.Empty(unrestrictedFriendly.CardRestrictions);
            var seasonFriendly = policy.ForFriendlyRoom(false, "season");
            Assert.Equal("season", seasonFriendly.DefaultRoomConfig.DisasterMode);
            Assert.Equal(policy.DisasterCardIds, seasonFriendly.DisasterCardIds);

            var restrictedFriendly = policy.ForFriendlyRoom(true, "none");
            Assert.Single(restrictedFriendly.CardRestrictions);
            Assert.Empty(restrictedFriendly.DisasterCardIds);

            var sandbox = policy.ForSandbox("custom");
            Assert.Equal("sandbox", sandbox.DefaultRoomConfig.MatchModeId);
            Assert.Equal("custom", sandbox.DefaultRoomConfig.DisasterMode);
            Assert.Empty(sandbox.DisasterCardIds);
            Assert.Empty(sandbox.CardRestrictions);
            Assert.Throws<ArgumentOutOfRangeException>(() => policy.ForSandbox("season"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DefaultRoomConfigMigratesAndDefaultPresetSelectionSeedsOnlyNewAccounts()
    {
        var root = TempRoot();
        var path = Path.Combine(root, "platform.json");
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var current = store.OperationsConfig(admin);
            var selectedMaster = catalog.PresetDecks[1].MasterId;
            var applied = store.ApplyOperationsConfig(admin, current.Config with
            {
                DefaultPresetDeckIds = [selectedMaster],
                DefaultRoomConfig = new L12DefaultRoomConfig("ranked", "friends", "public", "random"),
            }, current.Version, "default policy", Context("default-policy"));

            var account = store.Register("DefaultDeckUser", "password-123").Account!;
            var seeded = store.Decks(account.Id);
            Assert.NotEmpty(seeded);
            Assert.All(seeded, deck => Assert.Equal(selectedMaster, deck.MasterId));
            Assert.Equal("ranked", applied.Current.Config.DefaultRoomConfig.MatchModeId);

            var mirror = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            mirror["OperationsConfig"]!.AsObject().Remove("DefaultRoomConfig");
            foreach (var history in mirror["OperationsConfigHistory"]!.AsArray().OfType<JsonObject>())
                history["Config"]?.AsObject().Remove("DefaultRoomConfig");
            File.WriteAllText(path, mirror.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            SqliteConnection.ClearAllPools();
            File.Delete(Path.Combine(root, "platform.db"));

            var migrated = new L12PlatformStore(path, catalog.PresetDecks, officialCards: catalog.Cards);
            var migratedAdmin = migrated.Login("Admin", "L12master").Account!;
            var defaults = migrated.OperationsConfig(migratedAdmin).Config.DefaultRoomConfig;
            Assert.Equal(new L12DefaultRoomConfig("casual", "public", "request", "all"), defaults);
            Assert.NotNull(JsonNode.Parse(File.ReadAllText(path))!["OperationsConfig"]!["DefaultRoomConfig"]);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task OperationsRestrictionsOnlyApplyToOptInFriendlyRoomsWhileDeckStorageAndSandboxStayOpen()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var bannedIndex = Enumerable.Range(0, catalog.PresetDecks.Count).First(index =>
                catalog.PresetDecks[index].CardIds.Any(cardId => catalog.PresetDecks
                    .Where((_, otherIndex) => otherIndex != index)
                    .Any(other => !other.CardIds.Contains(cardId, StringComparer.OrdinalIgnoreCase))));
            var bannedPreset = catalog.PresetDecks[bannedIndex];
            var bannedCardId = bannedPreset.CardIds.First(cardId => catalog.PresetDecks
                .Where((_, index) => index != bannedIndex)
                .Any(other => !other.CardIds.Contains(cardId, StringComparer.OrdinalIgnoreCase)));
            Assert.Contains(catalog.PresetDecks, deck => !deck.CardIds.Contains(bannedCardId,
                StringComparer.OrdinalIgnoreCase));

            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var current = store.OperationsConfig(admin);
            store.ApplyOperationsConfig(admin, current.Config with
            {
                CardRestrictions = [new L12CardRestrictionConfig(bannedCardId, 0, "regression ban")],
            }, current.Version, "ban one card", Context("ban-card"));
            var player = store.Register("RestrictedDeckUser", "password-123");
            var opponent = store.Register("RestrictedOpponent", "password-123");

            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store);
            server = new L12WebSocketServer(rooms, recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };
            using (var request = Authorized(HttpMethod.Put, "/api/decks", player.Token!, "restricted-http",
                       Submission(bannedPreset)))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using (var request = Authorized(HttpMethod.Post, "/api/public-decks", player.Token!,
                       "restricted-publish", new { Deck = Submission(bannedPreset) }))
            using (var response = await client.SendAsync(request))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var hostSession = Guid.NewGuid();
            var joinSession = Guid.NewGuid();
            rooms.Connect(hostSession, player.Account!.Id, player.Account.Username);
            rooms.Connect(joinSession, opponent.Account!.Id, opponent.Account.Username);
            var created = rooms.CreateRoom(hostSession, new L12RoomOptions
            {
                MatchModeId = "ranked",
                DisasterMode = "season",
                UseCardRestrictions = false,
            });
            var createdPayload = Payload(created[0]);
            var roomCode = createdPayload["roomCode"]!.GetValue<string>();
            Assert.Equal("friendly", createdPayload["options"]!["matchModeId"]!.GetValue<string>());
            Assert.Equal("season", createdPayload["options"]!["disasterMode"]!.GetValue<string>());
            Assert.False(createdPayload["options"]!["useCardRestrictions"]!.GetValue<bool>());
            Assert.All(rooms.SelectDeck(hostSession, bannedIndex), message =>
                Assert.Equal("roomState", Payload(message)["type"]!.GetValue<string>()));
            Assert.All(rooms.JoinRoom(joinSession, roomCode), message =>
                Assert.Equal("roomState", Payload(message)["type"]!.GetValue<string>()));
            Assert.All(await rooms.SetReadyAsync(hostSession, true), message =>
                Assert.Equal("roomState", Payload(message)["type"]!.GetValue<string>()));
            Assert.All(await rooms.SetReadyAsync(joinSession, true), message =>
                Assert.Equal("gameState", Payload(message)["type"]!.GetValue<string>()));

            var sandboxSession = Guid.NewGuid();
            rooms.Connect(sandboxSession, "sandbox-restricted", "沙盒限制测试");
            var sandbox = await rooms.CreateSandboxAsync(sandboxSession,
                new L12SandboxRequest(Submission(bannedPreset)));
            Assert.Contains(sandbox, message => Payload(message)["type"]!.GetValue<string>() == "gameState");

            var seasonSandboxSession = Guid.NewGuid();
            rooms.Connect(seasonSandboxSession, "season-sandbox", "赛季沙盒伪造");
            var seasonSandbox = await rooms.CreateSandboxAsync(seasonSandboxSession,
                new L12SandboxRequest(DisasterMode: "season"));
            Assert.Equal("sandboxRejected", Payload(Assert.Single(seasonSandbox))["type"]!.GetValue<string>());

            var restrictedHost = Guid.NewGuid();
            rooms.Connect(restrictedHost, "restricted-friend-room", "禁限卡好友房");
            var restrictedRoom = rooms.CreateRoom(restrictedHost, new L12RoomOptions
            {
                DisasterMode = "random",
                UseCardRestrictions = true,
            });
            var restrictedPayload = Payload(restrictedRoom[0]);
            Assert.True(restrictedPayload["options"]!["useCardRestrictions"]!.GetValue<bool>());
            Assert.Equal("deckRejected", Payload(Assert.Single(rooms.SelectDeck(restrictedHost, bannedIndex)))["type"]!
                .GetValue<string>());

            var inviter = store.Register("InvitePolicyHost", "password-123").Account!;
            var invitee = store.Register("InvitePolicyGuest", "password-123").Account!;
            Assert.True(store.SendFriendRequest(inviter.Id, invitee.Id).Success);
            Assert.True(store.ResolveFriendRequest(invitee.Id, inviter.Id, true).Success);
            var inviterSession = Guid.NewGuid();
            var inviteeSession = Guid.NewGuid();
            rooms.Connect(inviterSession, inviter.Id, inviter.Username);
            rooms.Connect(inviteeSession, invitee.Id, invitee.Username);
            var invitation = rooms.InviteFriend(inviterSession, invitee.Id)
                .Select(Payload).Single(payload => payload["type"]!.GetValue<string>() == "friendInvitationSent");
            var resolved = rooms.ResolveFriendInvitation(inviteeSession,
                invitation["invitationId"]!.GetValue<string>(), true);
            var invitedRoom = resolved.Select(Payload)
                .First(payload => payload["type"]!.GetValue<string>() == "roomState");
            Assert.False(invitedRoom["options"]!["useCardRestrictions"]!.GetValue<bool>());
            Assert.Equal("all", invitedRoom["options"]!["disasterMode"]!.GetValue<string>());
            Assert.All(rooms.SelectDeck(inviterSession, bannedIndex), message =>
                Assert.Equal("roomState", Payload(message)["type"]!.GetValue<string>()));
        }
        finally
        {
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RoomAndGamePinPolicyWhileMaintenanceGatesNewFriendlyFlows()
    {
        var root = TempRoot();
        MatchRecorder? recorder = null;
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks,
                officialCards: catalog.Cards);
            var admin = store.Login("Admin", "L12master").Account!;
            var initialPolicy = store.CaptureOperationsPolicy();
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder, store);
            var host = store.Register("PolicyHost", "password-123").Account!;
            var guest = store.Register("PolicyGuest", "password-123").Account!;
            var hostSession = Guid.NewGuid();
            var guestSession = Guid.NewGuid();
            rooms.Connect(hostSession, host.Id, host.Username);
            rooms.Connect(guestSession, guest.Id, guest.Username);
            var created = rooms.CreateRoom(hostSession, new L12RoomOptions
            {
                MatchModeId = "ranked",
                DisasterMode = "random",
                UseCardRestrictions = true,
            });
            var createdPayload = Payload(created[0]);
            var roomCode = createdPayload["roomCode"]!.GetValue<string>();
            Assert.Equal(initialPolicy.Version, createdPayload["operationsPolicyVersion"]!.GetValue<long>());
            Assert.Equal("friendly", createdPayload["options"]!["matchModeId"]!.GetValue<string>());
            Assert.Equal("random", createdPayload["options"]!["disasterMode"]!.GetValue<string>());
            Assert.All(rooms.JoinRoom(guestSession, roomCode), message =>
                Assert.Equal("roomState", Payload(message)["type"]!.GetValue<string>()));

            var current = store.OperationsConfig(admin);
            var maintenance = store.ApplyOperationsConfig(admin, current.Config with
            {
                Maintenance = new L12MaintenanceConfig(true, "维护门禁回归", null, null),
            }, current.Version, "maintenance on", Context("maintenance-on")).Current;
            Assert.Equal("maintenance_active", Payload(Assert.Single(await rooms.SetReadyAsync(hostSession, true)))["code"]!
                .GetValue<string>());
            var blockedRoomSession = Guid.NewGuid();
            rooms.Connect(blockedRoomSession, "blocked-room", "维护建房");
            Assert.Equal("maintenance_active", Payload(Assert.Single(rooms.CreateRoom(blockedRoomSession)))["code"]!
                .GetValue<string>());
            var blockedSandboxSession = Guid.NewGuid();
            rooms.Connect(blockedSandboxSession, "blocked-sandbox", "维护沙盒");
            Assert.Equal("maintenance_active",
                Payload(Assert.Single(await rooms.CreateSandboxAsync(blockedSandboxSession, null)))["code"]!
                    .GetValue<string>());

            var mixedPool = Enumerable.Range(1, 6).Select(number => $"S02-DS{number:00}")
                .Concat(Enumerable.Range(1, 3).Select(number => $"S01-DS{number:00}"))
                .Append(L12PlatformStore.AnnihilationCardId).ToArray();
            var live = store.ApplyOperationsConfig(admin, maintenance.Config with
            {
                DisasterPool = new L12SeasonDisasterPoolConfig(mixedPool),
                MatchModes =
                [
                    new L12MatchModeConfig("casual", "休闲对战", false),
                    new L12MatchModeConfig("ranked", "排位对战", true),
                ],
                DefaultRoomConfig = maintenance.Config.DefaultRoomConfig with { MatchModeId = "ranked" },
                Maintenance = new L12MaintenanceConfig(false, string.Empty, null, null),
            }, maintenance.Version, "new policy", Context("new-policy")).Current;
            var currentPolicy = store.CaptureOperationsPolicy();
            Assert.NotEqual(initialPolicy.Version, currentPolicy.Version);

            var casualSession = Guid.NewGuid();
            rooms.Connect(casualSession, "friendly-independent", "好友房独立");
            var friendlyUnaffected = rooms.CreateRoom(casualSession,
                new L12RoomOptions { MatchModeId = "casual" });
            Assert.Equal("roomState", Payload(Assert.Single(friendlyUnaffected))["type"]!.GetValue<string>());
            Assert.Equal("friendly", Payload(friendlyUnaffected[0])["options"]!["matchModeId"]!.GetValue<string>());

            // 好友房固定创建时的作用域策略；公开匹配模式开关不能污染已存在的好友房。
            Assert.All(await rooms.SetReadyAsync(hostSession, true), message =>
                Assert.Equal("roomState", Payload(message)["type"]!.GetValue<string>()));
            var started = await rooms.SetReadyAsync(guestSession, true);
            Assert.All(started, message => Assert.Equal(initialPolicy.Version,
                Payload(message)["state"]!["operationsPolicyVersion"]!.GetValue<long>()));

            var oldEngine = new L12GameEngine(catalog, "old-policy", "OLD", 1, ["甲", "乙"], [0, 1],
                disasterMode: "season", operationsPolicy: initialPolicy);
            var newEngine = new L12GameEngine(catalog, "new-policy", "NEW", 1, ["甲", "乙"], [0, 1],
                disasterMode: "season", operationsPolicy: currentPolicy);
            Assert.All(oldEngine.State.DisasterPool,
                card => Assert.StartsWith("S01-DS", card.CardId));
            Assert.Contains(newEngine.State.DisasterPool, card => card.CardId.StartsWith("S02-DS", StringComparison.Ordinal));

            var waitingHost = store.Register("WaitingHost", "password-123").Account!;
            var waitingGuest = store.Register("WaitingGuest", "password-123").Account!;
            var waitingHostSession = Guid.NewGuid();
            var waitingGuestSession = Guid.NewGuid();
            rooms.Connect(waitingHostSession, waitingHost.Id, waitingHost.Username);
            rooms.Connect(waitingGuestSession, waitingGuest.Id, waitingGuest.Username);
            var waitingRoom = rooms.CreateRoom(waitingHostSession);
            var waitingCode = Payload(waitingRoom[0])["roomCode"]!.GetValue<string>();
            store.ApplyOperationsConfig(admin, live.Config with
            {
                Maintenance = new L12MaintenanceConfig(true, "再次维护",
                    DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(2)),
            }, live.Version, "maintenance again", Context("maintenance-again"));
            Assert.Equal("maintenance_active",
                Payload(Assert.Single(rooms.JoinRoom(waitingGuestSession, waitingCode)))["code"]!.GetValue<string>());

            var matchId = Payload(started[0])["state"]!["matchId"]!.GetValue<string>();
            var maintenanceMessages = await rooms.TickRankedClocksAsync(DateTimeOffset.UtcNow);
            var invalidated = maintenanceMessages.Select(Payload)
                .First(payload => payload["type"]!.GetValue<string>() == "gameState");
            Assert.Equal("GameOver", invalidated["state"]!["phase"]!.GetValue<string>());
            Assert.Null(invalidated["state"]!["winner"]);
            Assert.Contains("服务器维护", invalidated["state"]!["winnerReason"]!.GetValue<string>());
            var recorded = Assert.IsType<L12MatchDetail>(await recorder.GetMatchAsync(matchId));
            Assert.NotNull(recorded.Match.EndedUtc);
            Assert.Single(recorded.Commands, command =>
                command.Command.GetProperty("type").GetString() == "authorityConclusion");
            rooms.Disconnect(hostSession);
            var recoveredSession = Guid.NewGuid();
            var recovered = Payload(rooms.Connect(recoveredSession, host.Id, host.Username));
            Assert.True(recovered["recovered"]!.GetValue<bool>());
            Assert.NotEmpty(rooms.RecoveryState(recoveredSession));
        }
        finally
        {
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task HttpAccountChangesOperationsAndRuntimeAreDirectVersionedAndTruthful()
    {
        var root = TempRoot();
        var previousHost = Environment.GetEnvironmentVariable("L12_LISTEN_HOST");
        L12WebSocketServer? server = null;
        MatchRecorder? recorder = null;
        try
        {
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", "127.0.0.1");
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var store = new L12PlatformStore(Path.Combine(root, "platform.json"), catalog.PresetDecks);
            var admin = store.Login("Admin", "L12master");
            var target = store.Register("DirectRoleTarget", "password-123");
            var tournamentOrganizer = store.Register("HttpScopedOrganizer", "password-123");
            var tournamentReferee = store.Register("HttpScopedReferee", "password-123");
            Assert.True(store.SendFriendRequest(tournamentOrganizer.Account!.Id,
                tournamentReferee.Account!.Id).Success);
            Assert.True(store.ResolveFriendRequest(tournamentReferee.Account.Id,
                tournamentOrganizer.Account.Id, true).Success);
            var rooms = new L12RoomManager(catalog, recorder, store);
            server = new L12WebSocketServer(rooms, recorder, store, catalog);
            await server.StartAsync(0);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(server.Addresses)) };

            L12TournamentView createdTournament;
            using (var createTournament = Authorized(HttpMethod.Post, "/api/tournaments",
                       tournamentOrganizer.Token!, "direct-tournament-create",
                       new TournamentCreateRequest(CreateTournamentPayload(), "direct-tournament-create-1",
                           store.Version, false, "creator is organizer")))
            using (var response = await client.SendAsync(createTournament))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                createdTournament = (await response.Content.ReadFromJsonAsync<L12TournamentView>())!;
                Assert.Equal(tournamentOrganizer.Account.Id, createdTournament.OrganizerAccountId);
            }
            using (var setStaff = Authorized(HttpMethod.Put,
                       $"/api/tournaments/{createdTournament.Id}/staff", tournamentOrganizer.Token!,
                       "direct-tournament-staff", new TournamentStaffRequest([tournamentReferee.Account.Id],
                           "direct-tournament-staff-1", createdTournament.Version, false, "friend referee")))
            using (var response = await client.SendAsync(setStaff))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var updated = await response.Content.ReadFromJsonAsync<L12TournamentView>();
                Assert.Equal(tournamentReferee.Account.Id, Assert.Single(updated!.Referees).AccountId);
            }
            Assert.Empty(store.AdminApprovals());
            using (var removedTournamentApprovals = Authorized(HttpMethod.Get,
                       $"/api/tournaments/{createdTournament.Id}/approvals", tournamentOrganizer.Token!,
                       "removed-tournament-approvals"))
            using (var response = await client.SendAsync(removedTournamentApprovals))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            using (var roleRequest = Authorized(HttpMethod.Put,
                       $"/api/admin/accounts/{target.Account!.Id}/role", admin.Token!, "direct-role",
                       new RoleRequest("admin", "direct-role-1", target.Account.PermissionVersion, false, "promote")))
            using (var response = await client.SendAsync(roleRequest))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<RoleCommandResult>();
                Assert.Equal("admin", result!.Role);
                Assert.True(result.Changed);
            }
            using (var sameRole = Authorized(HttpMethod.Put,
                       $"/api/admin/accounts/{target.Account.Id}/role", admin.Token!, "same-role",
                       new RoleRequest("admin", "same-role-1", store.Account(target.Account.Id)!.PermissionVersion,
                           false, "no change")))
            using (var response = await client.SendAsync(sameRole))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<RoleCommandResult>();
                Assert.False(result!.Changed);
            }

            var legacyApprovalId = Guid.NewGuid().ToString("N");
            var legacyApproval = new L12AdminCommandEnvelope<RoleCommandPayload>(legacyApprovalId,
                "legacy-direct-role-approval", "account.role.set", admin.Account!, DateTimeOffset.UtcNow,
                $"account:{target.Account.Id}", "legacy approval must stay disabled", false,
                store.Account(target.Account.Id)!.PermissionVersion,
                new RoleCommandPayload(target.Account.Id, "player"),
                Context("legacy-direct-role-approval") with
                {
                    CommandId = legacyApprovalId,
                    IdempotencyKey = "legacy-direct-role-approval",
                    ExpectedVersion = store.Account(target.Account.Id)!.PermissionVersion,
                });
            var legacyPending = new L12AdminCommandBus(store).Execute(legacyApproval,
                L12Permission.AdminAccountRolesWrite,
                current => L12AdminCommandResult<RoleCommandResult>.Ok(
                    new RoleCommandResult(current.Payload.AccountId, current.Payload.Role, true)),
                risk: L12AdminCommandRisk.High);
            Assert.True(legacyPending.Pending);
            Assert.Empty(store.AdminApprovals());
            Assert.Equal(0, store.SecurityStatus(admin.Account!).PendingApprovals);
            using (var legacyReview = Authorized(HttpMethod.Post,
                       $"/api/admin/approvals/{legacyApprovalId}", admin.Token!, "legacy-role-review",
                       new { decision = "approve", reason = "must remain disabled" }))
            using (var response = await client.SendAsync(legacyReview))
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("admin", store.Account(target.Account.Id)!.Role);

            using (var rejectedLegacy = Authorized(HttpMethod.Put,
                       $"/api/admin/accounts/{target.Account.Id}/role", admin.Token!, "legacy-role",
                       new RoleRequest("organizer", "legacy-role-1", store.Account(target.Account.Id)!.PermissionVersion,
                           false, "legacy")))
            using (var response = await client.SendAsync(rejectedLegacy))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            using (var statusRequest = Authorized(HttpMethod.Put,
                       $"/api/admin/accounts/{target.Account.Id}/status", admin.Token!, "direct-status",
                       new AccountStatusRequest(true, "security response contract", "direct-status-1",
                           store.Account(target.Account.Id)!.PermissionVersion)))
            using (var response = await client.SendAsync(statusRequest))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<L12AccountStatusOperationView>();
                Assert.True(result!.Applied);
                Assert.True(result.Account.Disabled);
            }

            L12OperationsConfigView current;
            using (var getConfig = Authorized(HttpMethod.Get, "/api/admin/operations/config", admin.Token!, "ops-get"))
            using (var response = await client.SendAsync(getConfig))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                current = (await response.Content.ReadFromJsonAsync<L12OperationsConfigView>())!;
            }
            var next = current.Config with
            {
                FeatureFlags = new Dictionary<string, bool>(current.Config.FeatureFlags)
                {
                    ["publicDecks"] = false,
                    ["tournaments"] = false,
                },
            };
            using (var apply = Authorized(HttpMethod.Put, "/api/admin/operations/config", admin.Token!, "ops-http",
                       new OperationsConfigApplyRequest(next, "disable temporarily", "ops-http-1", current.Version)))
            using (var response = await client.SendAsync(apply))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<L12OperationsConfigOperationView>();
                Assert.False(result!.Current.Config.FeatureFlags["tournaments"]);
            }

            using (var effectiveResponse = await client.GetAsync("/api/operations/effective-policy"))
            {
                Assert.Equal(HttpStatusCode.OK, effectiveResponse.StatusCode);
                var effective = JsonNode.Parse(await effectiveResponse.Content.ReadAsStringAsync())!.AsObject();
                Assert.NotNull(effective["defaultRoomConfig"]);
                Assert.NotNull(effective["matchModes"]);
                Assert.NotNull(effective["maintenance"]);
                Assert.Null(effective["featureFlags"]);
                Assert.Null(effective["disasterPool"]);
            }
            Assert.Equal(HttpStatusCode.ServiceUnavailable,
                (await client.GetAsync("/api/public-decks")).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable,
                (await client.GetAsync("/api/tournaments")).StatusCode);

            using (var runtime = Authorized(HttpMethod.Get, "/api/admin/runtime/status", admin.Token!, "runtime"))
            using (var response = await client.SendAsync(runtime))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var status = await response.Content.ReadFromJsonAsync<L12RuntimeStatusView>();
                Assert.Equal(catalog.Cards.Count, status!.CardCount);
                Assert.Equal(0, status.WebSocketConnectionCount);
                Assert.False(status.Cdn.Configured);
                Assert.Equal("unavailable", status.Cdn.State);
                Assert.Equal("no-authoritative-source", status.Cdn.Detail);
                Assert.Equal(2, status.ReleaseEnvironments.Count);
            }
        }
        finally
        {
            if (server is not null)
            {
                await server.StopAsync();
                await server.DisposeAsync();
            }
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("L12_LISTEN_HOST", previousHost);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RuntimeStatsExcludeCompletedGames()
    {
        var root = TempRoot();
        MatchRecorder? recorder = null;
        try
        {
            var catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var rooms = new L12RoomManager(catalog, recorder);
            var sessionId = Guid.NewGuid();
            rooms.Connect(sessionId, "sandbox-account", "统计测试");

            await rooms.CreateSandboxAsync(sessionId, new L12SandboxRequest());
            Assert.Equal(1, rooms.RuntimeStats().ActiveGameCount);

            using var surrender = JsonDocument.Parse("{\"type\":\"surrender\"}");
            await rooms.HandleSandboxActionAsync(sessionId, 0, surrender.RootElement);

            Assert.Equal(0, rooms.RuntimeStats().ActiveGameCount);
        }
        finally
        {
            if (recorder is not null) await recorder.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private static L12TournamentCreatePayload CreateTournamentPayload(IReadOnlyList<string>? referees = null)
        => new("作用域赛事", "swiss", "public", 16, null, "现行规则", "scoped roles",
            "after", "season", string.Empty, 50, 5, referees);

    private static L12AdminAuditContext Context(string correlationId)
        => new(correlationId, RequestMethod: "TEST", RequestPath: "/test");

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token,
        string correlationId, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(L12CorrelationIds.HeaderName, correlationId);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static L12CustomDeckSubmission Submission(L12PresetDeckDefinition deck)
        => new()
        {
            Name = deck.Name,
            MasterId = deck.MasterId,
            CardIds = deck.CardIds.ToList(),
            MoraleIds = deck.MoraleIds.ToList(),
            SpecialIds = deck.SpecialIds.ToList(),
        };

    private static JsonObject Payload(OutgoingMessage message) => Payload(message.Payload);

    private static JsonObject Payload(object payload)
        => JsonSerializer.SerializeToNode(payload, JsonOptions)!.AsObject();

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"l12-operations-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
