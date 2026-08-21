using System.Runtime.CompilerServices;
using TwelveLegions.Server;

namespace TwelveLegions.Tests;

internal static class TestAssemblyBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // 历史卡效测试聚焦结算结果，不逐一模拟没有任何合法响应时的两次匿名让过。
        // 信息隐藏专项测试可在构造引擎时显式传入 autoPassEmptyResponses: false。
        L12GameEngine.AutoPassEmptyResponsesByDefault = true;
    }
}
