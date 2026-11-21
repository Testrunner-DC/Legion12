using Microsoft.Extensions.Logging;

namespace TwelveLegions.Server;

/// <summary>
/// 结构化日志门面。启动时注入 ILoggerFactory；未注入时（单元测试、离线工具、早期启动）
/// 回落到 Console，保持既有行为不变。这里只做级别与分类转发，不拼业务字段。
/// </summary>
internal static class L12Log
{
    private static ILoggerFactory? factory;

    internal static void Initialize(ILoggerFactory? loggerFactory) => factory = loggerFactory;

    internal static bool HasStructuredSink => factory is not null;

    internal static void Info(string category, string message) => Write(LogLevel.Information, category, message, null);

    internal static void Warn(string category, string message) => Write(LogLevel.Warning, category, message, null);

    internal static void Error(string category, string message, Exception? error = null)
        => Write(LogLevel.Error, category, message, error);

    private static void Write(LogLevel level, string category, string message, Exception? error)
    {
        var sink = factory;
        if (sink is not null)
        {
            try
            {
                sink.CreateLogger(category).Log(level, error, "{Message}", message);
                return;
            }
            catch
            {
                // sink 可能已被所属 host 释放（例如测试构造后销毁）；日志绝不能让业务失败，
                // 因此任何 sink 异常都退回到 Console 而不是向上抛。
                factory = null;
            }
        }
        if (level >= LogLevel.Error) Console.Error.WriteLine(message);
        else Console.WriteLine(message);
    }
}
