namespace TwelveLegions.Server;

/// <summary>
/// 当前对局状态、命令日志与回放的兼容边界。版本判断必须集中在此处，
/// 不允许房间、前端或分析查询自行猜测能否恢复旧状态。
/// </summary>
internal static class L12PersistenceContract
{
    internal const int CurrentStateFormatVersion = 2;
    internal const int CurrentJournalStorageVersion = 2;
    internal const int MinimumCheckpointRecoveryVersion = 2;
    internal const string ExpiredReplayMessage = "该版本已过期，回放无法生成";

    internal static bool SupportsCheckpointRecovery(int stateFormatVersion)
        => stateFormatVersion is >= MinimumCheckpointRecoveryVersion and <= CurrentStateFormatVersion;

    internal static void EnsureCheckpointRecoverySupported(int stateFormatVersion)
    {
        if (!SupportsCheckpointRecovery(stateFormatVersion))
            throw new InvalidDataException(ExpiredReplayMessage);
    }
}
