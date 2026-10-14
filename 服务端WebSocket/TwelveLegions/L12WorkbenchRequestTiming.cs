using System.Diagnostics;
using System.Globalization;

namespace TwelveLegions.Server;

internal enum L12WorkbenchStage { Auth, Bugs, Renames, Runtime, Storage, Security, Audit }

// Request-local, fixed names and numeric handler durations only. This does not
// measure response serialization/network delivery or expose filters/identities.
internal sealed class L12WorkbenchRequestTiming
{
    private static readonly string[] Names =
        ["wb-auth", "wb-bugs", "wb-renames", "wb-runtime", "wb-storage", "wb-security", "wb-audit"];
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly double?[] durations = new double?[Names.Length];

    internal void EndAuthentication()
        => durations[(int)L12WorkbenchStage.Auth] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    internal IDisposable Measure(L12WorkbenchStage stage)
    {
        if (!Enum.IsDefined(stage)) throw new ArgumentOutOfRangeException(nameof(stage));
        return new Scope(this, stage);
    }

    internal string ToServerTiming()
    {
        var values = new List<string> { Duration("wb-total", Stopwatch.GetElapsedTime(started).TotalMilliseconds) };
        for (var i = 0; i < durations.Length; i++)
            if (durations[i] is { } value) values.Add(Duration(Names[i], value));
        return string.Join(", ", values);
    }

    private static string Duration(string name, double milliseconds)
        => name + ";dur=" + milliseconds.ToString("F2", CultureInfo.InvariantCulture);

    private sealed class Scope(L12WorkbenchRequestTiming owner, L12WorkbenchStage stage) : IDisposable
    {
        private readonly long started = Stopwatch.GetTimestamp();
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            owner.durations[(int)stage] = (owner.durations[(int)stage] ?? 0)
                + Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
    }
}
