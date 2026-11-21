using Microsoft.Extensions.Logging;
using TwelveLegions.Server;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>
/// C4b 回归守卫：注入 ILoggerFactory 时日志必须带级别与分类；未注入时必须回落到 Console 且不抛错。
/// </summary>
[Collection("Platform environment")]
public sealed class StructuredLoggingFacadeTests
{
    [Fact]
    public void RegisteredFactoryReceivesLevelAndCategory()
    {
        var factory = new CapturingFactory();
        L12Log.Initialize(factory);
        try
        {
            L12Log.Warn("test.category", "警告样本");

            Assert.True(L12Log.HasStructuredSink);
            var logger = Assert.IsType<CapturingLogger>(factory.Last);
            Assert.Equal("test.category", logger.Category);
            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Contains("警告样本", entry.Message, StringComparison.Ordinal);
        }
        finally
        {
            L12Log.Initialize(null);
        }
    }

    [Fact]
    public void WithoutFactoryTheFacadeFallsBackAndNeverThrows()
    {
        L12Log.Initialize(null);
        Assert.False(L12Log.HasStructuredSink);
        L12Log.Info("test", "无 sink 时回落到 Console");
        L12Log.Error("test", "错误也能回落", new InvalidOperationException("sample"));
    }

    private sealed class CapturingLogger(string category) : ILogger
    {
        public string Category { get; } = category;
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class CapturingFactory : ILoggerFactory
    {
        public ILogger? Last { get; private set; }
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => Last = new CapturingLogger(categoryName);
        public void Dispose() { }
    }
}
