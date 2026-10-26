using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using TwelveLegions.Server;

// Qualification only. No production entry point or credentials are inherited.
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedEnvironment = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH", "SystemRoot", "WINDIR", "DOTNET_ROOT", "TEMP", "TMP", "TMPDIR",
        "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_NOLOGO", "L12_LISTEN_HOST",
    };
    private static string Stage = "arguments";

    private static async Task<int> Main(string[] args)
    {
        var output = Console.Out;
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        try
        {
            if (args.SequenceEqual(new[] { "--self-test" }))
            {
                ValidateEnvironment();
                output.WriteLine(JsonSerializer.Serialize(new { kind = "self-test", passed = true }, Json));
                return 0;
            }
            var options = Parse(args);
            Stage = "environment";
            ValidateEnvironment(); // Before any Store, catalog or server construction.
            Stage = "sandbox";
            var root = Path.GetFullPath(options["--sandbox-root"]);
            if (OperatingSystem.IsWindows())
                Require(!string.Equals(Path.GetPathRoot(root), "C:\\", StringComparison.OrdinalIgnoreCase));
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            Require(Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any());
            Require(Path.GetDirectoryName(root) == parent && Path.GetFileName(root).StartsWith("l12-auth-http-", StringComparison.Ordinal));
            PlainAncestors(root);
            Require(Path.GetFullPath(Environment.CurrentDirectory) == root);
            Stage = "binary";
            var serverRoot = Path.GetFullPath(options["--server-root"]);
            PlainAncestors(serverRoot);
            var assemblyPath = Path.Combine(serverRoot, "GrandUMIServer.dll");
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))) == options["--server-sha256"]);
            var resolver = new AssemblyDependencyResolver(assemblyPath);
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                var path = resolver.ResolveAssemblyToPath(name);
                if (path is null && name.Name is { } simple && Path.GetFileName(simple) == simple)
                {
                    var local = Path.Combine(serverRoot, simple + ".dll");
                    if (File.Exists(local)) path = local;
                }
                return path is null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            };
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
            {
                var path = resolver.ResolveUnmanagedDllToPath(name);
                return path is null ? IntPtr.Zero : System.Runtime.InteropServices.NativeLibrary.Load(path);
            };
            AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            Environment.SetEnvironmentVariable("L12_ADMIN_PASSWORD", Convert.ToHexString(RandomNumberGenerator.GetBytes(24)));
            return await Serve(root, serverRoot, int.Parse(options["--iterations"]), output);
        }
        catch (Exception error)
        {
            // Never emit exception payloads, account identities, passwords, salts or tokens.
            output.WriteLine(JsonSerializer.Serialize(new { kind = "failure", stage = Stage, errorType = error.GetType().Name }, Json));
            return 1;
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        Require(args.Length == 8);
        var allowed = new[] { "--server-root", "--server-sha256", "--sandbox-root", "--iterations" };
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
            Require(allowed.Contains(args[i], StringComparer.Ordinal) && result.TryAdd(args[i], args[i + 1]));
        Require(result["--iterations"] is "120000" or "600000");
        Require(result["--server-sha256"].Length == 64 && result["--server-sha256"].All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F'));
        Require(Path.IsPathFullyQualified(result["--server-root"]) && Path.IsPathFullyQualified(result["--sandbox-root"]));
        return result;
    }

    private static void ValidateEnvironment()
    {
        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
            Require(variable.Key is string key && AllowedEnvironment.Contains(key));
        Require(Environment.GetEnvironmentVariable("L12_LISTEN_HOST") == "127.0.0.1");
        Require(Environment.GetEnvironmentVariable("L12_ADMIN_PASSWORD") is null);
    }

    private static void PlainAncestors(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            Require(!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) == 0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> Serve(string root, string serverRoot, int iterations, TextWriter output)
    {
        Stage = "catalog";
        var catalog = L12Catalog.Load(Path.Combine(serverRoot, "TwelveLegions", "Data"));
        Stage = "store";
        var store = new L12PlatformStore(Path.Combine(root, "platform.json"));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        Stage = "register";
        var owners = Enumerable.Range(0, 8).Select(index =>
            store.Register($"qau{index}" + Guid.NewGuid().ToString("N")[..6], password)).ToArray();
        var reader = store.Register("qread" + Guid.NewGuid().ToString("N")[..6], password);
        Require(owners.All(owner => owner.Success && owner.Account is not null) && reader.Success && reader.Account is not null);
        foreach (var owner in owners)
        {
            var row = AccountRow(store, owner.Account!.Id);
            if (iterations != 600_000) continue;
            Stage = "seed";
            // Reuse the real credential implementation, exactly as the existing reader fixture.
            var type = typeof(L12PlatformStore).Assembly.GetType("TwelveLegions.Server.L12PasswordCredential", true)!;
            var credential = type.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [password, iterations])!;
            Func<bool> seed = () =>
            {
                foreach (var (target, source) in new[] { ("PasswordHash", "PasswordHash"), ("Salt", "Salt"),
                    ("PasswordAlgorithm", "Algorithm"), ("PasswordIterations", "Iterations"), ("PasswordCredentialVersion", "Version") })
                    row.GetType().GetProperty(target)!.SetValue(row, credential.GetType().GetProperty(source)!.GetValue(credential));
                typeof(L12PlatformStore).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(store, [false]);
                return true;
            };
            typeof(L12PlatformStore).GetMethod("ExecuteAdminTransaction", BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(typeof(bool)).Invoke(store, [seed]);
        }
        var credentialBefore = owners.ToDictionary(owner => owner.Account!.Id,
            owner => Credential(AccountRow(store, owner.Account!.Id)));
        foreach (var owner in owners)
        {
            var row = AccountRow(store, owner.Account!.Id);
            Require((int)row.GetType().GetProperty("PasswordIterations")!.GetValue(row)! == iterations);
        }
        Stage = "recorder";
        var recorder = new MatchRecorder(Path.Combine(root, "matches.db"));
        L12WebSocketServer? server = null;
        try
        {
            await recorder.InitializeAsync();
            server = new L12WebSocketServer(new L12RoomManager(catalog, recorder, store), recorder, store, catalog);
            Stage = "listen";
            await server.StartAsync(0).WaitAsync(TimeSpan.FromSeconds(10));
            Require(server.Addresses.Count == 1);
            var address = new Uri(server.Addresses[0]);
            Require(address.Scheme == "http" && address.Host == "127.0.0.1" && address.Port > 0);
            // Private pipe handshake is consumed in memory by the driver and never saved as evidence.
            output.WriteLine(JsonSerializer.Serialize(new { kind = "ready", address = address.AbsoluteUri,
                accounts = owners.Select(owner => new { username = owner.Account!.Username, ownerId = owner.Account.Id }), password,
                readerId = reader.Account!.Id, readerToken = reader.Token, iterations }, Json));
            await output.FlushAsync();
            Stage = "finish";
            Require(await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(40)) == "finish");
            foreach (var pair in credentialBefore)
                Require(Credential(AccountRow(store, pair.Key)) == pair.Value);
            foreach (var account in owners.Select(owner => owner.Account!).Append(reader.Account!))
            {
                var sessions = store.Sessions(account.Id);
                if (sessions.Count > 0)
                    store.RevokeOwnSessions(new L12AuthenticatedSession(account, sessions[0].Id));
                Require(store.Sessions(account.Id).Count == 0);
            }
        }
        finally
        {
            Stage = "stop";
            if (server is not null)
            {
                await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
            await recorder.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        output.WriteLine(JsonSerializer.Serialize(new { kind = "finished", iterations, credentialsUnchanged = true,
            syntheticSessionsRevoked = true, serverStopped = true, existingAccountWrites = 0,
            productionBusinessOrStatisticsWrites = 0 }, Json));
        return 0;
    }

    private static object AccountRow(L12PlatformStore store, string id)
    {
        var data = typeof(L12PlatformStore).GetProperty("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        return ((IEnumerable)data.GetType().GetProperty("Accounts")!.GetValue(data)!).Cast<object>()
            .Single(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == id);
    }

    private static string Credential(object row) => JsonSerializer.Serialize(new[] { "PasswordHash", "Salt", "PasswordAlgorithm", "PasswordIterations", "PasswordCredentialVersion" }
        .Select(name => row.GetType().GetProperty(name)!.GetValue(row)).ToArray());

    private static void Require(bool valid)
    {
        if (!valid) throw new InvalidOperationException("qualification boundary");
    }
}
