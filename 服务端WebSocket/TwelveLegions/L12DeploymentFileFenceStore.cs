using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwelveLegions.Server;

/// <summary>
/// Fixed runtime marker, durable atomic replacement and compare-before-clear.
/// No gameplay/database data is stored here; stale processes cannot remove a newer marker.
/// </summary>
internal sealed class L12DeploymentFileFenceStore : IL12DeploymentDrainFenceStore
{
    internal const string FileName = ".deployment-admission-drain.json";
    private const int MaximumBytes = 8192;
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<L12DeploymentDrainPhase>(allowIntegerValues: false) },
    };
    private readonly object _gate = new();
    private readonly string _path;
    private string? _expectedFingerprint;
    private bool _observed;

    internal L12DeploymentFileFenceStore(string runtimeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        _path = Path.Combine(Path.GetFullPath(runtimeDirectory), FileName);
    }

    public L12DeploymentDrainFenceLoadResult Load()
    {
        lock (_gate)
        {
            var loaded = Read(out var fingerprint);
            _observed = loaded.Kind != L12DeploymentDrainFenceLoadKind.Unknown;
            _expectedFingerprint = fingerprint;
            return loaded;
        }
    }

    public bool TryWrite(L12DeploymentPersistedFence fence)
    {
        if (!Valid(fence)) return false;
        lock (_gate)
        {
            string? temporary = null;
            try
            {
                if (!_observed || IsLink(_path + ".lock")) return false;
                using var exclusive = new FileStream(_path + ".lock", FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
                var current = Read(out var fingerprint);
                if (current.Kind == L12DeploymentDrainFenceLoadKind.Unknown
                    || fingerprint != _expectedFingerprint) return false;
                if (current.Fence is { } existing
                    && (existing.OperationId != fence.OperationId || existing.TargetCommit != fence.TargetCommit
                        || existing.Epoch > fence.Epoch)) return false;

                var bytes = JsonSerializer.SerializeToUtf8Bytes(fence, Json);
                if (bytes.Length > MaximumBytes) return false;
                temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var writer = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    writer.Write(bytes);
                    writer.Flush(flushToDisk: true);
                }
                File.Move(temporary, _path, overwrite: true);
                temporary = null;
                _expectedFingerprint = Convert.ToHexString(SHA256.HashData(bytes));
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                return false;
            }
            finally
            {
                if (temporary is not null)
                {
                    // Only the exact file created by this operation is eligible for cleanup.
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }

    public bool TryClear(string operationId, string targetCommit)
    {
        lock (_gate)
        {
            try
            {
                if (!_observed || _expectedFingerprint is null || IsLink(_path + ".lock")) return false;
                using var exclusive = new FileStream(_path + ".lock", FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
                var current = Read(out var fingerprint);
                if (current.Kind != L12DeploymentDrainFenceLoadKind.Found
                    || fingerprint != _expectedFingerprint || current.Fence!.OperationId != operationId
                    || current.Fence.TargetCommit != targetCommit) return false;
                File.Delete(_path);
                _expectedFingerprint = null;
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private L12DeploymentDrainFenceLoadResult Read(out string? fingerprint)
    {
        fingerprint = null;
        try
        {
            if (IsLink(_path) || Directory.Exists(_path)) return L12DeploymentDrainFenceLoadResult.Unknown;
            // File.Exists suppresses access errors, so absence must be proven by the open exception.
            using var reader = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (reader.Length is <= 0 or > MaximumBytes) return L12DeploymentDrainFenceLoadResult.Unknown;
            var bytes = new byte[checked((int)reader.Length)];
            reader.ReadExactly(bytes);
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return L12DeploymentDrainFenceLoadResult.Unknown;
            var required = new HashSet<string>(["ProtocolVersion", "Phase", "Epoch", "OperationId",
                "TargetCommit", "ProcessInstance", "ActiveCommit", "SealId"], StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!required.Remove(property.Name)) return L12DeploymentDrainFenceLoadResult.Unknown;
            if (required.Count != 0) return L12DeploymentDrainFenceLoadResult.Unknown;
            var fence = JsonSerializer.Deserialize<L12DeploymentPersistedFence>(bytes, Json);
            if (fence is null || !Valid(fence)) return L12DeploymentDrainFenceLoadResult.Unknown;
            fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
            return L12DeploymentDrainFenceLoadResult.Found(fence);
        }
        catch (FileNotFoundException) { return L12DeploymentDrainFenceLoadResult.Missing; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return L12DeploymentDrainFenceLoadResult.Unknown;
        }
    }

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
    }

    private static bool Valid(L12DeploymentPersistedFence fence)
        => fence.ProtocolVersion == L12DeploymentDrainCoordinator.ProtocolVersion
           && fence.Phase is L12DeploymentDrainPhase.Draining or L12DeploymentDrainPhase.Sealing
               or L12DeploymentDrainPhase.Sealed
           && fence.Epoch is >= 0 and < long.MaxValue
           && L12DeploymentDrainOwner.TryCreate(fence.OperationId, fence.TargetCommit, fence.ProcessInstance, out _)
           && L12DeploymentDrainCoordinator.IsCommit(fence.ActiveCommit)
           && (fence.Phase == L12DeploymentDrainPhase.Sealed
               ? L12DeploymentDrainCoordinator.IsCanonicalGuid(fence.SealId) : fence.SealId is null);
}
