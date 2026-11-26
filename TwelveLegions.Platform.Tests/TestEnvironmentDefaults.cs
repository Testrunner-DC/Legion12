using System.Runtime.CompilerServices;
using TwelveLegions.Server;

namespace TwelveLegions.Platform.Tests;

// 见 L12PlatformStore.EligibilityGateEnabled：既有用例按历史语义（player 可直接建赛）运行，
// 赛事资质门槛由专门的用例显式开启验证。
internal static class TestEnvironmentDefaults
{
    [ModuleInitializer]
    internal static void Apply() => L12PlatformStore.EligibilityGateEnabled = false;
}
