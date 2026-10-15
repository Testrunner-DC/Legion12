using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Platform.Tests;

[Collection("Platform environment")]
public sealed class SelfDamageEntryDiscountProtocolRegressionTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static readonly L12Catalog Catalog = L12Catalog.Load(
        Path.Combine(AppContext.BaseDirectory, "TwelveLegions", "Data"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-12T14:00:00+08:00");

    public static IEnumerable<object[]> ProtocolCases()
        => Catalog.Cards.Values
            .Where(card => L12StructuredCardRules.SelfDamageEntryDiscount(card.Id) is not null)
            .OrderBy(card => card.Id, StringComparer.Ordinal)
            .SelectMany(card => new[] { 0, 1 }.SelectMany(actor => new[] { false, true }
                .Select(restore => new object[] { card.Id, actor, restore })));

    public static IEnumerable<object[]> ExactThorCanuteCases()
        => new[] { 0, 1 }.SelectMany(actor => new[] { false, true }.SelectMany(restore =>
            new[] { "yes", "no" }.Select(choice => new object[] { actor, restore, choice })));

    [Theory]
    [MemberData(nameof(ProtocolCases))]
    public async Task InsufficientNormalCostPublishesReplacementPromptThenExplicitCancelPublishesItsRemoval(
        string cardId, int actor, bool restore)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-self-damage-protocol",
            $"{cardId}-{actor}-{restore}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        MatchRecorder? recorder = null;
        try
        {
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(Catalog, recorder, utcNow: () => Now,
                maintenanceSandboxFenceActive: () => false);
            var host = Guid.NewGuid();
            var guest = Guid.NewGuid();
            manager.Connect(host, $"{cardId}-{actor}-account-a", "Synthetic A");
            manager.Connect(guest, $"{cardId}-{actor}-account-b", "Synthetic B");
            var created = manager.CreateRoom(host);
            var roomCode = Payload(created[0]).GetProperty("roomCode").GetString()!;
            manager.JoinRoom(guest, roomCode);
            await manager.SetReadyAsync(host, true);
            await manager.SetReadyAsync(guest, true);

            var room = RuntimeRoom(manager, roomCode);
            var gameProperty = room.GetType().GetProperty("Game",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var started = (L12GameEngine)gameProperty.GetValue(room)!;
            var game = CreateExact(cardId, actor, started.State.MatchId, roomCode,
                88400 + actor + cardId[^1]);
            var player = game.State.Players[actor];
            var card = Assert.Single(player.Hand);
            Accept(game.Handle(actor,
                new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)));
            var first = Assert.Single(game.State.PendingPrompts);
            if (restore)
            {
                var state = game.SerializeFullState();
                var random = game.RandomState;
                var facts = game.CardFactSignalSequence;
                game = L12GameEngine.RestoreCheckpoint(Catalog, state, random!.Value, facts,
                    autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
                    utcNow: () => Now);
                Assert.Equal(state, game.SerializeFullState());
                Assert.Equal(random, game.RandomState);
                Assert.Equal(facts, game.CardFactSignalSequence);
            }
            else
            {
                game.MarkEventsPersisted(game.State.EventSequence);
                game.MarkCardFactsPersisted(game.CardFactSignalSequence);
            }
            gameProperty.SetValue(room, game);
            player = game.State.Players[actor];
            var actorSession = actor == 0 ? host : guest;

            var beforeNo = game.State.Revision;
            var noMessages = await manager.HandleActionAsync(actorSession,
                JsonSerializer.SerializeToElement(new L12Command("resolvePrompt",
                    PromptId: first.PromptId, Choice: "no"), Wire),
                $"{cardId}-{actor}-{restore}-no");

            Assert.Equal(beforeNo + 1, game.State.Revision);
            Assert.DoesNotContain(noMessages, message => Type(message) == "actionRejected");
            Assert.Contains(noMessages, message => Type(message) == "gameState");
            var retry = Assert.Single(game.State.PendingPrompts);
            Assert.NotEqual(first.PromptId, retry.PromptId);
            Assert.Contains("士气不足", retry.Data["retryReason"]);
            AssertActorPromptCount(noMessages, actorSession, 1);
            Assert.Equal(10, player.Hp);
            Assert.Contains(player.Hand, candidate => candidate.InstanceId == card.InstanceId);
            Assert.All(player.Morale, morale => Assert.False(morale.Tapped));

            var beforeCancel = game.State.Revision;
            var cancelMessages = await manager.HandleActionAsync(actorSession,
                JsonSerializer.SerializeToElement(new L12Command("resolvePrompt",
                    PromptId: retry.PromptId, Choice: "cancel"), Wire),
                $"{cardId}-{actor}-{restore}-cancel");

            Assert.Equal(beforeCancel + 1, game.State.Revision);
            Assert.DoesNotContain(cancelMessages, message => Type(message) == "actionRejected");
            Assert.Contains(cancelMessages, message => Type(message) == "gameState");
            AssertActorPromptCount(cancelMessages, actorSession, 0);
            Assert.Empty(game.State.PendingPrompts);
            Assert.Equal(10, player.Hp);
            Assert.Contains(player.Hand, candidate => candidate.InstanceId == card.InstanceId);
            Assert.All(player.Morale, morale => Assert.False(morale.Tapped));
            Assert.All(player.Field.SelectMany(row => row), Assert.Null);
        }
        finally
        {
            await CleanupFixtureAsync(root, recorder);
        }
    }

    [Theory]
    [MemberData(nameof(ExactThorCanuteCases))]
    public async Task ExactThorLastHealthCanutePreservesLethalYesAndMakesUnaffordableNoRecoverable(
        int actor, bool restore, string choice)
    {
        var root = Path.Combine(Path.GetTempPath(), "l12-self-damage-thor-protocol",
            $"{actor}-{restore}-{choice}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        MatchRecorder? recorder = null;
        try
        {
            recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
            await recorder.InitializeAsync();
            var manager = new L12RoomManager(Catalog, recorder, utcNow: () => Now,
                maintenanceSandboxFenceActive: () => false);
            var host = Guid.NewGuid();
            var guest = Guid.NewGuid();
            manager.Connect(host, $"thor-{actor}-account-a", "Synthetic A");
            manager.Connect(guest, $"thor-{actor}-account-b", "Synthetic B");
            var created = manager.CreateRoom(host);
            var roomCode = Payload(created[0]).GetProperty("roomCode").GetString()!;
            manager.JoinRoom(guest, roomCode);
            await manager.SetReadyAsync(host, true);
            await manager.SetReadyAsync(guest, true);

            var room = RuntimeRoom(manager, roomCode);
            var gameProperty = room.GetType().GetProperty("Game",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var started = (L12GameEngine)gameProperty.GetValue(room)!;
            var game = CreateExact("S02-0303", actor, started.State.MatchId, roomCode,
                88500 + actor, hp: 1, moraleCount: 6);
            var card = Assert.Single(game.State.Players[actor].Hand);
            Accept(game.Handle(actor,
                new L12Command("playCard", card.InstanceId, Row: 0, Slot: 0)));
            var first = Assert.Single(game.State.PendingPrompts);
            if (restore)
            {
                var state = game.SerializeFullState();
                var random = game.RandomState;
                var facts = game.CardFactSignalSequence;
                game = L12GameEngine.RestoreCheckpoint(Catalog, state, random!.Value, facts,
                    autoPassEmptyResponses: false, concealHiddenResponseAvailability: false,
                    utcNow: () => Now);
                Assert.Equal(state, game.SerializeFullState());
                Assert.Equal(random, game.RandomState);
                Assert.Equal(facts, game.CardFactSignalSequence);
            }
            else
            {
                game.MarkEventsPersisted(game.State.EventSequence);
                game.MarkCardFactsPersisted(game.CardFactSignalSequence);
            }
            gameProperty.SetValue(room, game);
            var player = game.State.Players[actor];
            var actorSession = actor == 0 ? host : guest;
            var before = game.State.Revision;

            var messages = await manager.HandleActionAsync(actorSession,
                JsonSerializer.SerializeToElement(new L12Command("resolvePrompt",
                    PromptId: first.PromptId, Choice: choice), Wire),
                $"thor-{actor}-{restore}-{choice}");

            Assert.Equal(before + 1, game.State.Revision);
            Assert.DoesNotContain(messages, message => Type(message) == "actionRejected");
            Assert.Contains(messages, message => Type(message) == "gameState");
            if (choice == "yes")
            {
                AssertActorPromptCount(messages, actorSession, 0);
                Assert.Equal(0, player.Hp);
                Assert.Equal(1 - actor, game.State.Winner);
                Assert.Equal(L12Phase.GameOver, game.State.Phase);
                Assert.Contains(player.Hand, candidate => candidate.InstanceId == card.InstanceId);
                Assert.DoesNotContain(player.Field.SelectMany(row => row),
                    candidate => candidate?.InstanceId == card.InstanceId);
                Assert.Empty(game.State.PendingPrompts);
                Assert.Empty(game.State.PendingTriggerBatches);
                Assert.Empty(game.State.PendingTriggerStackCandidates);
                Assert.Empty(game.State.EffectStack);
                Assert.Empty(game.State.DeferredEffectStack);
            }
            else
            {
                AssertActorPromptCount(messages, actorSession, 1);
                var retry = Assert.Single(game.State.PendingPrompts);
                Assert.NotEqual(first.PromptId, retry.PromptId);
                Assert.Contains("士气不足", retry.Data["retryReason"]);
                Assert.Equal(1, player.Hp);
                Assert.Equal(6, player.Morale.Count(morale => !morale.Tapped));
                Assert.Contains(player.Hand, candidate => candidate.InstanceId == card.InstanceId);

                var cancelMessages = await manager.HandleActionAsync(actorSession,
                    JsonSerializer.SerializeToElement(new L12Command("resolvePrompt",
                        PromptId: retry.PromptId, Choice: "cancel"), Wire),
                    $"thor-{actor}-{restore}-cancel");
                Assert.DoesNotContain(cancelMessages, message => Type(message) == "actionRejected");
                AssertActorPromptCount(cancelMessages, actorSession, 0);
                Assert.Empty(game.State.PendingPrompts);
            }
        }
        finally
        {
            await CleanupFixtureAsync(root, recorder);
        }
    }

    [Fact]
    public async Task SupervisedFixtureHandsLockedDatabaseToPostExitCleanupWithoutDeletingItInHost()
    {
        var supervisedRoot = Environment.GetEnvironmentVariable("L12_TEST_TEMP_ROOT");
        if (string.IsNullOrEmpty(supervisedRoot)) return;
        var root = OwnedFixturePath("l12-self-damage-protocol");
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "matches.db");
        // The live test host deliberately owns this handle. The existing outer
        // supervisor may remove the owned tree only after this process exits
        // and its current complete TRX proves every test passed.
        using var held = new FileStream(database, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        held.WriteByte(1);

        await CleanupFixtureAsync(root, recorder: null);

        Assert.True(Directory.Exists(root));
        Assert.True(File.Exists(database));
        Assert.Equal(1, held.Length);
        // No in-host deletion here: successful outer-supervisor cleanup is part
        // of the acceptance, and failed test evidence must remain untouched.
    }

    [Theory]
    [InlineData("l12-self-damage-protocol")]
    [InlineData("l12-self-damage-thor-protocol")]
    public void FixtureHandoffAcceptsOnlyItsExactSupervisedTempAndOwnedParent(string parent)
    {
        var owned = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}");
        var root = Path.Combine(owned, parent, $"synthetic-{Guid.NewGuid():N}");
        var inspections = 0;
        Assert.True(UsesPostExitFixtureCleanup(root, owned, owned,
            _ => { inspections++; return FileAttributes.Directory; }));
        Assert.True(inspections >= 3);
    }

    [Theory]
    [InlineData("temp-mismatch")]
    [InlineData("root-name")]
    [InlineData("sibling-root")]
    [InlineData("similar-parent")]
    [InlineData("invalid-leaf")]
    [InlineData("linked-fixture")]
    [InlineData("linked-owner")]
    [InlineData("linked-ancestor")]
    [InlineData("not-directory")]
    public void FixtureHandoffRejectsMismatchAndLinksInsteadOfRelaxingCleanup(string variant)
    {
        var owned = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}");
        if (variant == "root-name") owned += "-unowned";
        var temp = variant == "temp-mismatch" ? owned + "-other" : owned;
        var parent = variant == "similar-parent" ? "l12-self-damage-protocol-unowned" : "l12-self-damage-protocol";
        var root = Path.Combine(variant == "sibling-root" ? owned + "-sibling" : owned,
            parent, variant == "invalid-leaf" ? "synthetic-invalid" : $"synthetic-{Guid.NewGuid():N}");
        var linked = variant switch
        {
            "linked-fixture" => root,
            "linked-owner" => owned,
            "linked-ancestor" => Directory.GetParent(owned)!.FullName,
            _ => string.Empty,
        };
        Assert.Throws<InvalidOperationException>(() => UsesPostExitFixtureCleanup(root, owned, temp,
            path => variant == "not-directory" ? FileAttributes.Normal
                : string.Equals(path, linked, StringComparison.OrdinalIgnoreCase)
                    ? FileAttributes.Directory | FileAttributes.ReparsePoint : FileAttributes.Directory));
    }

    [Fact]
    public async Task FixtureWithoutSupervisorRetainsStrictImmediateDeletion()
    {
        var root = OwnedFixturePath("l12-self-damage-protocol");
        var inspections = 0;
        Assert.False(UsesPostExitFixtureCleanup(root, null, Path.GetTempPath(),
            _ => { inspections++; throw new InvalidOperationException("must not inspect"); }));
        Assert.Equal(0, inspections);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "standalone.txt"), "synthetic");
        await DeleteOwnedFixtureDirectoryAsync(root);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task FixtureCleanupRetriesTransientIoFailureAndThenSucceeds()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        await DeleteOwnedFixtureDirectoryAsync(OwnedFixturePath("l12-self-damage-protocol"),
            _ => true,
            (_, _) =>
            {
                attempts++;
                if (attempts < 3) throw new IOException("synthetic transient handle");
            },
            delay => { delays.Add(delay); return Task.CompletedTask; });

        Assert.Equal(3, attempts);
        Assert.Equal([TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(80)], delays);
    }

    [Fact]
    public async Task FixtureCleanupRethrowsPersistentIoFailureOnFifthAttempt()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var error = await Assert.ThrowsAsync<IOException>(() =>
            DeleteOwnedFixtureDirectoryAsync(OwnedFixturePath("l12-self-damage-thor-protocol"),
                _ => true,
                (_, _) => { attempts++; throw new IOException($"synthetic persistent handle {attempts}"); },
                delay => { delays.Add(delay); return Task.CompletedTask; }));

        Assert.Equal("synthetic persistent handle 5", error.Message);
        Assert.Equal(5, attempts);
        Assert.Equal([TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(80),
            TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(160)], delays);
    }

    [Fact]
    public async Task FixtureCleanupDoesNotRetryOrSwallowNonIoFailure()
    {
        var attempts = 0;
        var delays = 0;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            DeleteOwnedFixtureDirectoryAsync(OwnedFixturePath("l12-self-damage-protocol"),
                _ => true,
                (_, _) => { attempts++; throw new UnauthorizedAccessException("synthetic authorization failure"); },
                _ => { delays++; return Task.CompletedTask; }));

        Assert.Equal(1, attempts);
        Assert.Equal(0, delays);
    }

    [Fact]
    public async Task FixtureCleanupRejectsOutOfScopePathBeforeInspectingOrDeletingIt()
    {
        var inspections = 0;
        var deletes = 0;
        var outside = Path.Combine(Path.GetTempPath(), "l12-self-damage-protocol-unowned",
            $"case-{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DeleteOwnedFixtureDirectoryAsync(outside,
                _ => { inspections++; return true; },
                (_, _) => deletes++,
                _ => Task.CompletedTask));

        Assert.Equal(0, inspections);
        Assert.Equal(0, deletes);
    }

    private static async Task CleanupFixtureAsync(string root, MatchRecorder? recorder)
    {
        var target = ValidateOwnedFixtureRoot(root);
        if (recorder is not null) await recorder.DisposeAsync();
        using var ownedPool = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(target, "matches.db"),
        }.ToString());
        SqliteConnection.ClearPool(ownedPool);
        if (UsesPostExitFixtureCleanup(target,
                Environment.GetEnvironmentVariable("L12_TEST_TEMP_ROOT"), Path.GetTempPath()))
            return;
        await DeleteOwnedFixtureDirectoryAsync(target);
    }

    private static bool UsesPostExitFixtureCleanup(string root, string? supervisedRoot, string tempRoot,
        Func<string, FileAttributes>? attributes = null)
    {
        if (string.IsNullOrEmpty(supervisedRoot)) return false;
        var owned = Path.GetFullPath(supervisedRoot).TrimEnd(Path.DirectorySeparatorChar);
        var actualTemp = Path.GetFullPath(tempRoot).TrimEnd(Path.DirectorySeparatorChar);
        var name = Path.GetFileName(owned);
        if (!string.Equals(owned, actualTemp, StringComparison.OrdinalIgnoreCase)
            || name.Length != 37 || !name.StartsWith("test-", StringComparison.Ordinal)
            || name.Skip(5).Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("supervised fixture cleanup ownership mismatch");

        var target = ValidateOwnedFixtureRoot(root, actualTemp);
        attributes ??= File.GetAttributes;
        for (DirectoryInfo? directory = new(target); directory is not null; directory = directory.Parent)
        {
            var value = attributes(directory.FullName);
            if ((value & FileAttributes.ReparsePoint) != 0 || (value & FileAttributes.Directory) == 0)
                throw new InvalidOperationException("supervised fixture cleanup linked path refused");
        }
        // Physical removal is NOT skipped: Invoke-L12TestRun must observe process
        // exit, a current all-pass TRX, a plain complete tree and actual deletion.
        // Any failure there retains the tree and fails the enclosing verification.
        return true;
    }

    private static async Task DeleteOwnedFixtureDirectoryAsync(string root,
        Func<string, bool>? directoryExists = null,
        Action<string, bool>? deleteDirectory = null,
        Func<TimeSpan, Task>? delayAsync = null)
    {
        var target = ValidateOwnedFixtureRoot(root);
        directoryExists ??= Directory.Exists;
        deleteDirectory ??= Directory.Delete;
        delayAsync ??= Task.Delay;
        if (!directoryExists(target)) return;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                deleteDirectory(target, true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                await delayAsync(TimeSpan.FromMilliseconds(40 * (attempt + 1)));
            }
        }
    }

    private static string ValidateOwnedFixtureRoot(string root, string? tempRoot = null)
    {
        var target = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var parent = Directory.GetParent(target)?.FullName;
        var temp = Path.GetFullPath(tempRoot ?? Path.GetTempPath());
        var ownedParents = new[]
        {
            Path.GetFullPath(Path.Combine(temp, "l12-self-damage-protocol")),
            Path.GetFullPath(Path.Combine(temp, "l12-self-damage-thor-protocol")),
        };
        var leaf = Path.GetFileName(target);
        var separator = leaf.LastIndexOf('-');
        var suffix = separator >= 0 ? leaf[(separator + 1)..] : string.Empty;
        if (parent is null
            || !ownedParents.Contains(parent, StringComparer.OrdinalIgnoreCase)
            || suffix.Length != 32
            || suffix.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("self-damage protocol fixture cleanup scope mismatch");
        return target;
    }

    private static string OwnedFixturePath(string parent)
        => Path.Combine(Path.GetTempPath(), parent, $"synthetic-{Guid.NewGuid():N}");

    private static void AssertActorPromptCount(IReadOnlyList<OutgoingMessage> messages,
        Guid actorSession, int expected)
    {
        var gameState = messages.Single(message => message.SessionId == actorSession
            && Type(message) == "gameState");
        Assert.Equal(expected, Payload(gameState).GetProperty("state").GetProperty("prompts").GetArrayLength());
    }

    private static string Type(OutgoingMessage message)
        => Payload(message).GetProperty("type").GetString()!;

    private static JsonElement Payload(OutgoingMessage message)
        => JsonSerializer.SerializeToElement(message.Payload, Wire);

    private static object RuntimeRoom(L12RoomManager manager, string roomCode)
    {
        var rooms = (IEnumerable)typeof(L12RoomManager)
            .GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        foreach (var entry in rooms)
        {
            var type = entry!.GetType();
            if (string.Equals((string?)type.GetProperty("Key")!.GetValue(entry), roomCode,
                    StringComparison.OrdinalIgnoreCase))
                return type.GetProperty("Value")!.GetValue(entry)!;
        }
        throw new InvalidOperationException("runtime room not found");
    }

    private static L12GameEngine CreateExact(string cardId, int actor, string matchId,
        string roomCode, int seed, int hp = 10, int? moraleCount = null)
    {
        var baseDeck = Catalog.DeckAt(0);
        var thor = new L12PresetDeckDefinition
        {
            Name = "protocol-explicit-thor",
            MasterId = "S02-03M1",
            CardIds = [.. baseDeck.CardIds],
            MoraleIds = [.. baseDeck.MoraleIds],
            SpecialIds = [.. baseDeck.SpecialIds],
        };
        var decks = actor == 0 ? new[] { thor, baseDeck } : new[] { baseDeck, thor };
        var game = new L12GameEngine(Catalog, matchId, roomCode, seed,
            ["Synthetic A", "Synthetic B"], decks, skipPreparation: true,
            disasterMode: "none", autoPassEmptyResponses: false,
            concealHiddenResponseAvailability: false, stateFormatVersion: 2,
            utcNow: () => Now);
        game.State.PendingPrompts.Clear();
        game.State.ActivePlayer = actor;
        game.State.FirstPlayer = actor;
        game.State.Round = 2;
        game.State.TurnSerial = 4;
        game.State.Phase = L12Phase.Main;
        game.State.ActiveDisaster = null;
        game.State.DisasterDeck.Clear();
        foreach (var owner in game.State.Players)
        {
            owner.Field[0] = new L12CardInstance?[3];
            owner.Field[1] = new L12CardInstance?[3];
            owner.Hand.Clear();
            owner.Library.Clear();
            owner.Graveyard.Clear();
            owner.Removed.Clear();
            owner.Resolving.Clear();
            owner.Relic = null;
            owner.ExtraRelics.Clear();
            owner.Morale.Clear();
            owner.MoraleDeck.Clear();
            owner.UsedAbilities.Clear();
            owner.SpecialZones.CanopicProgress.Clear();
            owner.SpecialZones.GodPower.Clear();
            owner.SpecialZones.Trials.Clear();
            owner.TemporaryMorale = 0;
            owner.FreeTacticCount = 0;
        }
        var player = game.State.Players[actor];
        player.Hp = hp;
        var card = Card(cardId, $"protocol-{cardId}-seat{actor}", actor);
        for (var index = 0; index < (moraleCount ?? card.Cost - 1); index++)
            player.Morale.Add(new L12MoraleCard
            {
                CardId = "S02-03C1",
                InstanceId = $"protocol-{cardId}-seat{actor}-morale-{index}",
            });
        player.Hand.Add(card);
        return game;
    }

    private static L12CardInstance Card(string cardId, string instanceId, int owner)
    {
        var definition = Catalog.Cards[cardId];
        return new L12CardInstance
        {
            InstanceId = instanceId,
            CardId = definition.Id,
            Name = definition.NameZh,
            OwnerIndex = owner,
            CardType = definition.CardType,
            Faction = definition.Faction,
            Cost = definition.Cost ?? 0,
            HasPrintedCost = definition.Cost.HasValue,
            EffectText = definition.Effect,
            BaseTroops = definition.Troops ?? 0,
            Troops = definition.Troops ?? 0,
            Traits = [.. definition.Traits],
            Profession = definition.Profession,
            ImageUrl = definition.ImageUrl,
            DisasterLevel = definition.DisasterLevel ?? 0,
            TrialValue = definition.TrialValue ?? 0,
            SummonRound = -1,
        };
    }

    private static void Accept(CommandResult result) => Assert.True(result.Accepted, result.Error);
}
