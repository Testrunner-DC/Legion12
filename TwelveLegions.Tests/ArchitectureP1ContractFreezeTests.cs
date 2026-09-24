using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        var first = CreateEngine();
        var second = CreateEngine();
        var actual = Capture(first);

        Assert.Equal(actual, Capture(second));
        var expected = JsonSerializer.Deserialize<ContractManifest>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "architecture-p1-contracts.json")),
            FixtureJson);
        Assert.True(expected == actual,
            $"P1 内核契约发生变化；必须先审查等价性并显式更新黄金夹具。当前值：{Environment.NewLine}{JsonSerializer.Serialize(actual, FixtureJson)}");
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
    public void RecipientProjectionNeverLeaksTheOtherPlayersPrivateHand()
    {
        var game = CreateEngine();
        var privateInstanceId = Assert.Single(game.State.Players[0].Hand.Take(1)).InstanceId;

        Assert.Contains(privateInstanceId, JsonSerializer.Serialize(game.SnapshotFor(0), WireJson), StringComparison.Ordinal);
        Assert.DoesNotContain(privateInstanceId, JsonSerializer.Serialize(game.SnapshotFor(1), WireJson), StringComparison.Ordinal);
        Assert.DoesNotContain(privateInstanceId, JsonSerializer.Serialize(game.SnapshotForSpectator(), WireJson), StringComparison.Ordinal);
        Assert.DoesNotContain(privateInstanceId, JsonSerializer.Serialize(game.SnapshotForReferee(), WireJson), StringComparison.Ordinal);
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
        var player0 = SnapshotHash(game.SnapshotFor(0));
        var player1 = SnapshotHash(game.SnapshotFor(1));
        var spectator = SnapshotHash(game.SnapshotForSpectator());
        var referee = SnapshotHash(game.SnapshotForReferee());
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
            SnapshotHash(game.SnapshotFor(0)));
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

    private static string AbilityCatalogHash()
    {
        var canonical = Catalog.AtomicEffects.All
            .SelectMany(card => card.Abilities)
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
    {
        var canonical = types.OrderBy(type => type.FullName, StringComparer.Ordinal)
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
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
