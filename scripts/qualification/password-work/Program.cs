using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using TwelveLegions.Server;

// Pure numeric qualification: no credential/file/URL input, Store, HTTP server or DB.
internal static class Program
{
    private const string SyntheticPassword = "synthetic-probe-only-20261025";
    private const int Queued = 8;
    private static readonly TimeSpan QueueWait = TimeSpan.FromMilliseconds(250);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var settings = Parse(args);
            var legacy = L12PasswordCredential.Create(SyntheticPassword);
            var future = L12PasswordCredential.Create(SyntheticPassword, L12PasswordCredential.RecognizedUpgradeIterations);
            var readers = new
            {
                CurrentReaderAcceptsLegacy = Verify(legacy, L12PasswordCredential.ActiveIterations),
                CurrentReaderAcceptsCandidate = Verify(future, L12PasswordCredential.ActiveIterations),
                CompatibleReaderAcceptsLegacy = Verify(legacy, L12PasswordCredential.RecognizedUpgradeIterations),
                CompatibleReaderAcceptsCandidate = Verify(future, L12PasswordCredential.RecognizedUpgradeIterations),
                CurrentWriterIterations = legacy.Iterations,
                RollbackRequiresCompatibleReader = !Verify(future, L12PasswordCredential.ActiveIterations),
            };
            Check(readers.CurrentReaderAcceptsLegacy && readers.CompatibleReaderAcceptsLegacy && readers.CompatibleReaderAcceptsCandidate);
            if (settings.SelfTest)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { Schema = "l12-password-work-selftest-v1", Readers = readers }, Json));
                return 0;
            }
            var operations = new (string Name, Action Work)[]
            {
                ("create-120k", () => Check(L12PasswordCredential.Create(SyntheticPassword).Iterations == legacy.Iterations)),
                ("verify-120k", () => Check(Verify(legacy, legacy.Iterations))),
                ("create-600k", () => Check(L12PasswordCredential.Create(SyntheticPassword, future.Iterations).Iterations == future.Iterations)),
                ("verify-600k", () => Check(Verify(future, future.Iterations))),
                ("verify-legacy-then-create-600k", () =>
                {
                    Check(Verify(legacy, legacy.Iterations));
                    Check(L12PasswordCredential.Create(SyntheticPassword, future.Iterations).Iterations == future.Iterations);
                }),
            };
            var serial = operations.Select(operation => Measure(operation.Name, operation.Work, settings.Samples)).ToArray();
            var bursts = new List<BurstSummary>();
            foreach (var workers in new[] { 1, 2 })
            foreach (var operation in operations.Where(operation => !operation.Name.StartsWith("create-", StringComparison.Ordinal)))
                bursts.Add(await Burst(operation.Name, operation.Work, workers, settings.Burst));
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Schema = "l12-password-work-v1", Scope = "local-synthetic-pure-helper-only",
                Runtime = RuntimeInformation.FrameworkDescription, Platform = RuntimeInformation.OSDescription,
                ProcessorCount = Environment.ProcessorCount, CpuQuotaEmulated = false,
                SharedDefaultWorkers = Math.Clamp(Environment.ProcessorCount / 2, 1, 2),
                Queued, MaximumQueueWaitMs = QueueWait.TotalMilliseconds, Readers = readers,
                Serial = serial, Bursts = bursts, ProductionCapacityQualified = false,
                ServerHttpLatencyMeasured = false, PersistentMigrationPerformed = false,
            }, Json));
            return 0;
        }
        catch (Exception error)
        {
            // Do not print arguments, password/hash/salt or general exception payloads.
            Console.Error.WriteLine($"Password qualification failed: {error.GetType().Name}.");
            return 1;
        }
    }

    private static (int Samples, int Burst, bool SelfTest) Parse(string[] args)
    {
        if (args.SequenceEqual(new[] { "--self-test" })) return (0, 0, true);
        if (args.Length % 2 != 0) throw new ArgumentException("Options need bounded numbers.");
        var samples = 8; var burst = 24;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!seen.Add(args[index]) || !int.TryParse(args[index + 1], out var value)) throw new ArgumentException("Invalid options.");
            switch (args[index])
            {
                case "--samples" when value is >= 3 and <= 20: samples = value; break;
                case "--burst" when value is >= 3 and <= 64: burst = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(args));
            }
        }
        return (samples, burst, false);
    }

    private static bool Verify(L12PasswordCredentialFields value, int maximum)
        => L12PasswordCredential.Verify(SyntheticPassword, value.PasswordHash, value.Salt, value.Algorithm, value.Iterations, value.Version, maximum);
    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Synthetic calculation failed.");
    }
    private static SerialSummary Measure(string operation, Action work, int count)
    {
        work(); // Warm-up excluded from the numeric samples.
        var samples = new List<double>();
        using var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        for (var index = 0; index < count; index++)
        {
            var start = Stopwatch.GetTimestamp(); work();
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        return new(operation, count, Distribution.From(samples), (process.TotalProcessorTime - cpuStart).TotalMilliseconds);
    }

    private static async Task<BurstSummary> Burst(string operation, Action work, int workers, int requests)
    {
        var budget = new L12PasswordWorkBudget(workers, Queued, QueueWait);
        using var ready = new CountdownEvent(requests);
        using var start = new ManualResetEventSlim();
        var active = 0; var maximumActive = 0;
        var tasks = Enumerable.Range(0, requests).Select(_ => Task.Factory.StartNew(() =>
        {
            ready.Signal();
            if (!start.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic launch barrier.");
            var enteredAt = Stopwatch.GetTimestamp();
            try
            {
                using var lease = budget.Acquire();
                var acquiredAt = Stopwatch.GetTimestamp();
                UpdateMaximum(ref maximumActive, Interlocked.Increment(ref active));
                try { work(); } finally { Interlocked.Decrement(ref active); }
                return new Sample(true, Stopwatch.GetElapsedTime(enteredAt, acquiredAt).TotalMilliseconds,
                    Stopwatch.GetElapsedTime(acquiredAt).TotalMilliseconds, Stopwatch.GetElapsedTime(enteredAt).TotalMilliseconds);
            }
            catch (L12AuthenticationBusyException)
            {
                return new Sample(false, 0, 0, Stopwatch.GetElapsedTime(enteredAt).TotalMilliseconds);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        var all = Task.WhenAll(tasks);
        using var process = Process.GetCurrentProcess();
        TimeSpan cpuStart; long wallStart; Sample[] observations;
        try
        {
            if (!ready.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic workers did not start.");
            cpuStart = process.TotalProcessorTime; wallStart = Stopwatch.GetTimestamp();
            start.Set();
            observations = await all.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            start.Set();
            try { await all.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception) when (all.IsCompleted) { }
        }
        var cpuMs = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
        var wallMs = Stopwatch.GetElapsedTime(wallStart).TotalMilliseconds;
        Check(maximumActive <= workers && active == 0);
        using (budget.Acquire()) { } // Success and busy paths must release admission.
        var succeeded = observations.Where(sample => sample.Accepted).ToArray();
        var busy = observations.Where(sample => !sample.Accepted).ToArray();
        return new(operation, workers, requests, succeeded.Length, busy.Length, maximumActive,
            Distribution.From(succeeded.Select(sample => sample.WaitMs)), Distribution.From(succeeded.Select(sample => sample.WorkMs)),
            Distribution.From(succeeded.Select(sample => sample.TotalMs)), Distribution.From(busy.Select(sample => sample.TotalMs)), wallMs, cpuMs);
    }
    private static void UpdateMaximum(ref int maximum, int value)
    {
        int old;
        do { old = Volatile.Read(ref maximum); if (old >= value) return; }
        while (Interlocked.CompareExchange(ref maximum, value, old) != old);
    }
    private sealed record Sample(bool Accepted, double WaitMs, double WorkMs, double TotalMs);
    private sealed record SerialSummary(string Operation, int Samples, Distribution WorkMs, double ProcessCpuMs);
    private sealed record BurstSummary(string Operation, int Workers, int Requests, int Completed, int Busy,
        int MaximumActive, Distribution WaitMs, Distribution WorkMs, Distribution TotalMs,
        Distribution BusyResponseMs, double WallMs, double ProcessCpuMs);
    private sealed record Distribution(int Samples, double? P50, double? P95, double? Maximum)
    {
        internal static Distribution From(IEnumerable<double> values)
        {
            var sorted = values.Order().ToArray();
            return sorted.Length == 0 ? new(0, null, null, null)
                : new(sorted.Length, sorted[(int)Math.Ceiling(sorted.Length * .50) - 1], sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], sorted[^1]);
        }
    }
}
