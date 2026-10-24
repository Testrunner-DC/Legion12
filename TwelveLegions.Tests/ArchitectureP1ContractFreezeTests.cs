using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveLegions.Server;
using Xunit;

namespace TwelveLegions.Tests;

public sealed class ArchitectureP1ContractFreezeTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions FixtureJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private static readonly L12Catalog Catalog = L12Catalog.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void FrozenContractManifestMatchesTheCurrentKernelBoundary()
    {
        // Preserve the committed pre-return manifest byte-for-byte. The new optional
        // event property and capability are frozen independently below.
        var first = CreateBeforeLibraryReturnEngine();
        var second = CreateBeforeLibraryReturnEngine();
        var actual = Capture(first);

        Assert.Equal(actual, Capture(second));
        var expected = JsonSerializer.Deserialize<ContractManifest>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "architecture-p1-contracts.json")),
            FixtureJson);
        expected = WithReviewedSusanooDepartureRoute(expected!);
        Assert.True(expected == actual,
            $"P1 内核契约发生变化；必须先审查等价性并显式更新黄金夹具。当前值：{Environment.NewLine}{JsonSerializer.Serialize(actual, FixtureJson)}");
    }

    [Fact]
    public void NextResetMoraleFlagIsTheOnlyAdditiveChangeToThePriorPostCommandProjection()
    {
        // Keep this prior projection byte guard on its original protocol era.
        // The separate capability guard below proves all newer state-hash changes.
        var game = CreatePriorProtocolEngine();
        CompleteMulligan(game);
        // Keep this era's byte guard before the separately reviewed placement-row projection.
        var snapshotJson = JsonSerializer.Serialize(game.SnapshotFor(0) with { LegionPlacementRows = null }, WireJson);
        using var snapshot = JsonDocument.Parse(snapshotJson);
        var morale = snapshot.RootElement.GetProperty("players").EnumerateArray()
            .SelectMany(player => player.GetProperty("morale").EnumerateArray()).ToArray();
        Assert.NotEmpty(morale);
        Assert.All(morale, token => Assert.False(token.GetProperty("cannotUntapAtNextReset").GetBoolean()));

        // This golden scenario has no applied lock. Prove the approved wire extension
        // leaves every prior byte intact; do not weaken the full manifest assertion.
        const string additiveFalseFlag = ",\"cannotUntapAtNextReset\":false";
        var priorProjection = snapshotJson.Replace(additiveFalseFlag, "", StringComparison.Ordinal);
        Assert.Equal(morale.Length, (snapshotJson.Length - priorProjection.Length) / additiveFalseFlag.Length);
        const string expectedPriorProjectionHash = "cdd35edbbc18fd73fa03dabf820ffe17881fda1ce5315bdbc450a20d8e02e0e8";
        var actualPriorProjectionHash = Sha256(priorProjection);
        Assert.True(string.Equals(expectedPriorProjectionHash, actualPriorProjectionHash,
                StringComparison.Ordinal),
            $"P1 prior projection hash changed: {actualPriorProjectionHash}");
    }

    [Fact]
    public void NewLifecycleCapabilitiesOnlyChangeStateFlagsAndTheProjectedStateHash()
    {
        var expectedPrior = JsonSerializer.Deserialize<ContractManifest>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures",
                "architecture-p1-contracts-prior-owner-protocols.json")), FixtureJson);
        Assert.Equal(WithReviewedSusanooDepartureRoute(expectedPrior!), Capture(CreatePriorProtocolEngine()));

        var current = CreateEngine();
        var prior = CreatePriorProtocolEngine();
        AssertProtocolEraDifferenceOnly(current, prior);
        CompleteMulligan(current);
        CompleteMulligan(prior);
        AssertProtocolEraDifferenceOnly(current, prior);
    }

    private static readonly string[] LifecycleCapabilityNames =
    [
        nameof(L12GameState.OwnerTurnEndModifierProtocolEnabled),
        nameof(L12GameState.AttackBuffLogProtocolEnabled),
        nameof(L12GameState.SourceVacatedEntrySlotProtocolEnabled),
        nameof(L12GameState.LibraryReturnFactsProtocolEnabled),
    ];

    [Fact]
    public void LibraryReturnAddsOnlyItsOptionalEventPropertyAndExplicitNewV2Capability()
    {
        var current = CreateEngine();
        Assert.True(current.State.LibraryReturnFactsProtocolEnabled);
        var legacy = CreateBeforeLibraryReturnEngine();
        Assert.False(legacy.State.LibraryReturnFactsProtocolEnabled);
        Assert.DoesNotContain(nameof(L12GameState.LibraryReturnFactsProtocolEnabled), legacy.SerializeFullState());
        var checkpoint = JsonNode.Parse(current.SerializeFullState())!.AsObject();
        Assert.True(checkpoint.Remove(nameof(L12GameState.LibraryReturnFactsProtocolEnabled)));
        Assert.Equal(legacy.SerializeFullState(), checkpoint.ToJsonString());
        var property = typeof(L12ActionEvent).GetProperty(nameof(L12ActionEvent.PlayerLibraryReturn))!;
        Assert.Equal(typeof(L12PlayerLibraryReturn), property.PropertyType);
        Assert.Equal(System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>()!.Condition);
        Assert.Equal(new[] { "BottomCount", "IdentitiesPublic", "LibraryOwner", "OperationId", "ShuffledCount", "TopCount" },
            typeof(L12PlayerLibraryReturn).GetProperties().Select(item => item.Name).Order(StringComparer.Ordinal));
        var oldEvent = new L12ActionEvent(1, "return", 0, "legacy", []);
        Assert.DoesNotContain("playerLibraryReturn", JsonSerializer.Serialize(oldEvent, WireJson));
        var currentViews = new[] { current.SnapshotFor(0), current.SnapshotFor(1), current.SnapshotForSpectator(), current.SnapshotForReferee() };
        var legacyViews = new[] { legacy.SnapshotFor(0), legacy.SnapshotFor(1), legacy.SnapshotForSpectator(), legacy.SnapshotForReferee() };
        for (var index = 0; index < currentViews.Length; index++)
            Assert.Equal(JsonSerializer.Serialize(legacyViews[index], WireJson),
                JsonSerializer.Serialize(currentViews[index] with { StateHash = legacyViews[index].StateHash }, WireJson));
    }

    private static L12GameEngine CreateBeforeLibraryReturnEngine()
    {
        var current = CreateEngine();
        var checkpoint = JsonNode.Parse(current.SerializeFullState())!.AsObject();
        Assert.True(checkpoint.Remove(nameof(L12GameState.LibraryReturnFactsProtocolEnabled)));
        return L12GameEngine.RestoreCheckpoint(Catalog, checkpoint.ToJsonString(), current.RandomState!.Value,
            current.CardFactSignalSequence, current.AutoPassEmptyResponses, current.ConcealHiddenResponseAvailability);
    }

    private static L12GameEngine CreatePriorProtocolEngine()
    {
        var current = CreateEngine();
        var checkpoint = JsonNode.Parse(current.SerializeFullState())!.AsObject();
        foreach (var name in LifecycleCapabilityNames)
        {
            Assert.True(checkpoint[name]!.GetValue<bool>());
            Assert.True(checkpoint.Remove(name));
        }
        return L12GameEngine.RestoreCheckpoint(Catalog, checkpoint.ToJsonString(),
            current.RandomState!.Value, current.CardFactSignalSequence,
            current.AutoPassEmptyResponses, current.ConcealHiddenResponseAvailability);
    }

    private static void AssertProtocolEraDifferenceOnly(L12GameEngine current, L12GameEngine prior)
    {
        var checkpoint = JsonNode.Parse(current.SerializeFullState())!.AsObject();
        var priorCheckpoint = JsonNode.Parse(prior.SerializeFullState())!.AsObject();
        foreach (var name in LifecycleCapabilityNames)
        {
            Assert.True(checkpoint[name]!.GetValue<bool>());
            Assert.True(checkpoint.Remove(name));
            Assert.False(priorCheckpoint.ContainsKey(name));
        }
        Assert.Equal(priorCheckpoint.ToJsonString(), checkpoint.ToJsonString());
        var currentViews = new[] { current.SnapshotFor(0), current.SnapshotFor(1),
            current.SnapshotForSpectator(), current.SnapshotForReferee() };
        var priorViews = new[] { prior.SnapshotFor(0), prior.SnapshotFor(1),
            prior.SnapshotForSpectator(), prior.SnapshotForReferee() };
        for (var index = 0; index < currentViews.Length; index++)
        {
            Assert.Equal(current.ComputeStateHash(), currentViews[index].StateHash);
            Assert.Equal(prior.ComputeStateHash(), priorViews[index].StateHash);
            Assert.NotEqual(priorViews[index].StateHash, currentViews[index].StateHash);
            Assert.Equal(JsonSerializer.Serialize(priorViews[index], WireJson),
                JsonSerializer.Serialize(currentViews[index] with { StateHash = priorViews[index].StateHash }, WireJson));
        }
    }

    [Fact]
    public void CheckpointRestoreKeepsTheSameStateEventsAndRecipientProjections()
    {
        var game = CreateEngine();
        CompleteMulligan(game);
        var randomState = Assert.IsType<L12RandomState>(game.RandomState);
        var restored = L12GameEngine.RestoreCheckpoint(Catalog, game.SerializeFullState(), randomState,
            game.CardFactSignalSequence, game.AutoPassEmptyResponses, game.ConcealHiddenResponseAvailability);

        Assert.Equal(Sha256(game.SerializeFullState()), Sha256(restored.SerializeFullState()));
        Assert.Equal(SnapshotHash(game.SnapshotFor(0)), SnapshotHash(restored.SnapshotFor(0)));
        Assert.Equal(SnapshotHash(game.SnapshotFor(1)), SnapshotHash(restored.SnapshotFor(1)));
        Assert.Equal(SnapshotHash(game.SnapshotForSpectator()), SnapshotHash(restored.SnapshotForSpectator()));
        Assert.Equal(SnapshotHash(game.SnapshotForReferee()), SnapshotHash(restored.SnapshotForReferee()));
        Assert.Equal(game.State.EventSequence, restored.State.EventSequence);
        Assert.Equal(game.State.Events.Select(item => item.Sequence), restored.State.Events.Select(item => item.Sequence));
    }

    [Fact]
    public void RecipientProjectionKeepsPlayerAndPublicHandsPrivateWhileRefereeSeesBoth()
    {
        var game = CreateEngine();
        var privateInstanceId = Assert.Single(game.State.Players[0].Hand.Take(1)).InstanceId;

        Assert.Contains(privateInstanceId, JsonSerializer.Serialize(game.SnapshotFor(0), WireJson), StringComparison.Ordinal);
        Assert.DoesNotContain(privateInstanceId, JsonSerializer.Serialize(game.SnapshotFor(1), WireJson), StringComparison.Ordinal);
        Assert.DoesNotContain(privateInstanceId, JsonSerializer.Serialize(game.SnapshotForSpectator(), WireJson), StringComparison.Ordinal);
        Assert.Contains(privateInstanceId, JsonSerializer.Serialize(game.SnapshotForReferee(), WireJson), StringComparison.Ordinal);
        Assert.Contains(privateInstanceId, JsonSerializer.Serialize(game.SnapshotForGm(0), WireJson), StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionAndPersistenceReadOrderKeepsTheSameAuthoritativeResult()
    {
        var projectionFirst = CreateEngine();
        var persistenceFirst = CreateEngine();

        var projectionFirstPlayer = SnapshotHash(projectionFirst.SnapshotFor(0));
        var projectionFirstSpectator = SnapshotHash(projectionFirst.SnapshotForSpectator());
        var projectionFirstHash = projectionFirst.ComputeStateHash();
        var projectionFirstState = projectionFirst.SerializeFullState();

        var persistenceFirstState = persistenceFirst.SerializeFullState();
        var persistenceFirstHash = persistenceFirst.ComputeStateHash();
        var persistenceFirstSpectator = SnapshotHash(persistenceFirst.SnapshotForSpectator());
        var persistenceFirstPlayer = SnapshotHash(persistenceFirst.SnapshotFor(0));

        Assert.Equal(Sha256(projectionFirstState), Sha256(persistenceFirstState));
        Assert.Equal(projectionFirstHash, persistenceFirstHash);
        Assert.Equal(projectionFirstPlayer, persistenceFirstPlayer);
        Assert.Equal(projectionFirstSpectator, persistenceFirstSpectator);
        Assert.Equal(projectionFirst.State.Revision, persistenceFirst.State.Revision);
        Assert.Equal(projectionFirst.State.EventSequence, persistenceFirst.State.EventSequence);
        Assert.Equal(
            projectionFirst.State.Events.Select(item => (item.Sequence, item.Type, item.Text)),
            persistenceFirst.State.Events.Select(item => (item.Sequence, item.Type, item.Text)));
    }

    private static ContractManifest Capture(L12GameEngine game)
    {
        var initialState = Sha256(game.SerializeFullState());
        var player0 = PriorPlacementRowsSnapshotHash(game.SnapshotFor(0));
        var player1 = PriorPlacementRowsSnapshotHash(game.SnapshotFor(1));
        var spectator = PriorPlacementRowsSnapshotHash(game.SnapshotForSpectator());
        var referee = PriorPlacementRowsSnapshotHash(game.SnapshotForReferee());
        CompleteMulligan(game);
        return new ContractManifest(
            1,
            KernelPortSurfaceHash(),
            SurfaceHash(typeof(L12Command)),
            SurfaceHash(typeof(L12GameSnapshot)),
            SurfaceHash(typeof(L12Prompt)),
            SurfaceHash(typeof(L12ActionEvent), typeof(L12AuthorityEvent), typeof(CommandResult)),
            AbilityCatalogHash(),
            initialState,
            player0,
            player1,
            spectator,
            referee,
            Sha256(game.SerializeFullState()),
            PriorPlacementRowsSnapshotHash(game.SnapshotFor(0)));
    }

    private static L12GameEngine CreateEngine()
        => new(Catalog, "architecture-p1-fixture", "P1FIXTURE", 20260924,
            ["甲", "乙"], [0, 1], skipPreparation: true, stateFormatVersion: 2);

    private static void CompleteMulligan(L12GameEngine game)
    {
        Assert.True(game.Handle(0, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
        Assert.True(game.Handle(1, new L12Command("mulligan", CardInstanceIds: [])).Accepted);
    }

    private static string SnapshotHash(L12GameSnapshot snapshot)
        => Sha256(JsonSerializer.Serialize(snapshot, WireJson));

    private static string PriorPlacementRowsSnapshotHash(L12GameSnapshot snapshot)
        => SnapshotHash(snapshot with { LegionPlacementRows = null });

    [Fact]
    public void LegionPlacementRowsIsTheOnlyReviewedAdditiveSnapshotProjection()
    {
        var property = typeof(L12GameSnapshot).GetProperty(nameof(L12GameSnapshot.LegionPlacementRows))!;
        Assert.Equal(typeof(int[]), property.PropertyType);
        Assert.Equal(System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>()!.Condition);
        Assert.Equal("0ee35f1a818c800cfe09f0499e32180ed17ff979e984bb18ba008f94362b96e6",
            SurfaceHash(typeof(L12GameSnapshot)));
        Assert.Equal("a439e59f5f16ad4dca1daa04c557a6253e6a38a304e883254952bb2477722209",
            SurfaceHashCore(true, typeof(L12GameSnapshot)));

        var game = CreateEngine();
        var checkpoint = game.SerializeFullState();
        var stateHash = game.ComputeStateHash();
        foreach (var view in new[] { game.SnapshotFor(0), game.SnapshotFor(1),
                     game.SnapshotForSpectator(), game.SnapshotForReferee(), game.SnapshotForGm(0) })
        {
            var current = JsonSerializer.Serialize(view, WireJson);
            var prior = JsonSerializer.Serialize(view with { LegionPlacementRows = null }, WireJson);
            var projected = JsonNode.Parse(current)!.AsObject();
            Assert.Equal(new[] { 0, 1 }, projected["legionPlacementRows"]!.AsArray()
                .Select(row => row!.GetValue<int>()).ToArray());
            Assert.True(projected.Remove("legionPlacementRows"));
            Assert.Equal(prior, projected.ToJsonString(WireJson));
            Assert.DoesNotContain("legionPlacementRows", prior);
            Assert.Equal(stateHash, view.StateHash);
        }
        Assert.Equal(checkpoint, game.SerializeFullState());
        Assert.Equal(stateHash, game.ComputeStateHash());
        Assert.DoesNotContain(nameof(L12GameSnapshot.LegionPlacementRows), checkpoint);
    }

    [Fact]
    public void SusanooDepartureRouteIsTheOnlyReviewedAbilityCatalogChange()
    {
        var abilities = Catalog.AtomicEffects.All.SelectMany(card => card.Abilities).ToArray();
        var current = Assert.Single(abilities, ability => ability.AbilityId ==
            "S01-04M2:ability:leave:47290d11c9fa259d");
        Assert.Equal("leave", current.Trigger);
        Assert.Equal("triggered", current.ExecutionModel);
        Assert.Single(current.Atoms, atom => atom.Kind == L12AtomKinds.CompositeFlow);
        Assert.DoesNotContain(abilities, ability => ability.AbilityId ==
            "S01-04M2:ability:leave:4e83a7191108369b");

        // The reviewed source correction adds this master's concrete settlement
        // route. Reconstruct only the previous declarative catalog entry, not an
        // old engine or an executable legacy source identity. Every other entry
        // remains byte-for-byte inside the original full-catalog hash guard.
        var previous = L12AtomicAbilityIdentity.Assign(current.CardId,
            current with { Atoms = current.Atoms.Where(atom => atom.Kind != L12AtomKinds.CompositeFlow).ToArray() },
            current.Sequence);
        Assert.Equal("S01-04M2:ability:leave:4e83a7191108369b", previous.AbilityId);
        Assert.Equal("14969742d79d606cb39f9d4a358839b4fe98bac699fac1566ff42aae89eb1af2",
            AbilityCatalogHash(abilities.Select(ability => ability == current ? previous : ability)));
        Assert.Equal("a13bd467de6626018e984309c8aeb45e6fa259dd67fe32133df4bfaa7861e665",
            AbilityCatalogHash(abilities));
    }

    private static ContractManifest WithReviewedSusanooDepartureRoute(ContractManifest previous)
    {
        Assert.Equal("14969742d79d606cb39f9d4a358839b4fe98bac699fac1566ff42aae89eb1af2",
            previous.AbilityCatalogSha256);
        // Preserve both historical fixture files and every non-catalog field.
        // The dedicated test above proves the exact one-entry catalog delta.
        return previous with
        {
            AbilityCatalogSha256 = "a13bd467de6626018e984309c8aeb45e6fa259dd67fe32133df4bfaa7861e665",
        };
    }

    private static string AbilityCatalogHash()
        => AbilityCatalogHash(Catalog.AtomicEffects.All.SelectMany(card => card.Abilities));

    private static string AbilityCatalogHash(IEnumerable<L12AtomicAbility> abilities)
    {
        var canonical = abilities
            .OrderBy(ability => ability.AbilityId, StringComparer.Ordinal)
            .Select(ability => string.Join('|',
                ability.CardId,
                ability.AbilityId,
                ability.StructureHash,
                ability.Trigger,
                ability.ExecutionModel,
                string.Join(',', ability.Atoms.OrderBy(atom => atom.Order)
                    .Select(atom => $"{atom.Order}:{atom.Kind}:{atom.Stage}:{atom.RuntimeExecutable}")),
                string.Join(',', ability.Presentations.OrderBy(scene => scene.SceneId, StringComparer.Ordinal)
                    .Select(scene => scene.SceneId))));
        return Sha256(string.Join('\n', canonical));
    }

    private static string SurfaceHash(params Type[] types)
        => SurfaceHashCore(false, types);

    private static string SurfaceHashCore(bool includeLegionPlacementRows, params Type[] types)
    {
        var canonical = types.OrderBy(type => type.FullName, StringComparer.Ordinal)
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                // Exact additive surface is asserted above; do not rebase prior golden hashes.
                .Where(property => type != typeof(L12ActionEvent) || property.Name != nameof(L12ActionEvent.PlayerLibraryReturn))
                .Where(property => includeLegionPlacementRows || type != typeof(L12GameSnapshot)
                    || property.Name != nameof(L12GameSnapshot.LegionPlacementRows))
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => $"{type.FullName}|{property.Name}|{CanonicalType(property.PropertyType)}|"
                    + $"read:{property.GetMethod is not null}|write:{property.SetMethod is not null}"));
        return Sha256(string.Join('\n', canonical));
    }

    private static string KernelPortSurfaceHash()
    {
        var methods = typeof(L12GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ThenBy(method => method.GetParameters().Length)
            .Select(method => $"method|{method.Name}|{CanonicalType(method.ReturnType)}|"
                + string.Join(',', method.GetParameters().Select(parameter => CanonicalType(parameter.ParameterType))));
        var properties = typeof(L12GameEngine).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"property|{property.Name}|{CanonicalType(property.PropertyType)}|"
                + $"read:{property.GetMethod is not null}|write:{property.SetMethod is not null}");
        var constructors = typeof(L12GameEngine).GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(constructor => constructor.GetParameters().Length)
            .Select(constructor => "constructor|" + string.Join(',', constructor.GetParameters()
                .Select(parameter => CanonicalType(parameter.ParameterType))));
        return Sha256(string.Join('\n', methods.Concat(properties).Concat(constructors)));
    }

    private static string CanonicalType(Type type)
    {
        if (type.IsArray) return $"{CanonicalType(type.GetElementType()!)}[]";
        if (!type.IsGenericType) return type.FullName ?? type.Name;
        var name = type.GetGenericTypeDefinition().FullName?.Split('`')[0] ?? type.Name.Split('`')[0];
        return $"{name}<{string.Join(',', type.GetGenericArguments().Select(CanonicalType))}>";
    }

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record ContractManifest(
        int Schema,
        string KernelPortSurfaceSha256,
        string CommandSurfaceSha256,
        string SnapshotSurfaceSha256,
        string PromptSurfaceSha256,
        string EventSurfaceSha256,
        string AbilityCatalogSha256,
        string InitialStateSha256,
        string Player0SnapshotSha256,
        string Player1SnapshotSha256,
        string SpectatorSnapshotSha256,
        string RefereeSnapshotSha256,
        string PostCommandStateSha256,
        string PostCommandPlayer0Sha256);
}
