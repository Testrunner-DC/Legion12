namespace TwelveLegions.Server;

/// <summary>
/// 对局内核的命令写入口。外围层只能提交领域命令，不能直接改写状态。
/// </summary>
internal interface IL12KernelCommandPort
{
    CommandResult Handle(int playerIndex, L12Command command);
}

/// <summary>
/// 按接收者生成受限状态投影。投影入口不承诺当前实现无副作用；
/// 在 P1 完成显式收敛前，调用方必须把它视为同一房间事务的一部分。
/// </summary>
internal interface IL12KernelProjectionPort
{
    L12GameSnapshot SnapshotFor(int viewer);
    L12GameSnapshot SnapshotForGm(int viewer);
    L12GameSnapshot SnapshotForSpectator();
    L12GameSnapshot SnapshotForReferee();
}

/// <summary>
/// 当前版本的检查点读取入口。序列化与哈希会先物化 V2 投影状态，
/// 因此必须与命令、Prompt 和持久化保持同一个串行事务边界。
/// </summary>
internal interface IL12KernelCheckpointPort
{
    string SerializeFullState();
    string ComputeStateHash();
}

/// <summary>
/// 房间层可依赖的最小对局端口集合。它只表达调用方向，不转移
/// L12GameEngine 对权威状态的唯一写入权。
/// </summary>
internal interface IL12MatchKernel : IL12KernelCommandPort, IL12KernelProjectionPort,
    IL12KernelCheckpointPort
{
}

/// <summary>
/// P1/P2 统一的接收者投影适配器。房间层不再自行选择隐藏信息字段，
/// 只能选择已经定义好的视角。
/// </summary>
internal static class L12KernelProjection
{
    internal static L12GameSnapshot ForPlayer(IL12KernelProjectionPort kernel, int playerIndex,
        bool revealAllForGm = false)
        => revealAllForGm ? kernel.SnapshotForGm(playerIndex) : kernel.SnapshotFor(playerIndex);

    internal static L12GameSnapshot ForSpectator(IL12KernelProjectionPort kernel)
        => kernel.SnapshotForSpectator();

    internal static L12GameSnapshot ForReferee(IL12KernelProjectionPort kernel)
        => kernel.SnapshotForReferee();
}
