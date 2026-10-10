namespace TwelveLegions.Server;

/// <summary>
/// 当前对局状态、命令日志与回放的兼容边界。版本判断必须集中在此处，
/// 不允许房间、前端或分析查询自行猜测能否恢复旧状态。
/// </summary>
internal static class L12PersistenceContract
{
    // V2 状态允许增加可选字段：缺失字段按默认值恢复，因而旧检查点仍可读取。
    // 命令尾重放仍逐步校验已持久化的 revision/state hash；若规则修复改变旧尾部的
    // 结算结果，恢复必须以哈希不一致失败关闭，不能静默接受另一条权威状态历史。
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
