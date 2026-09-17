namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    // 允许支付最后一点血量；声明额度与伤害替换分开，替换仍交给共用伤害流程。
    private static bool CanPayMasterDamageCost(L12PlayerState player, int amount)
        => amount > 0 && player.Hp >= amount;

    // 返回值表示可否继续支付/结算，不表示致命费用未支付。调用者须立即结束原流程。
    private bool PayMasterDamageCostAndCanContinue(int playerIndex, int amount, string reason)
    {
        DamageMaster(playerIndex, amount, reason);
        return State.Phase != L12Phase.GameOver;
    }
}
