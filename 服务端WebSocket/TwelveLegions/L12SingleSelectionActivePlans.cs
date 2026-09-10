namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    // A read-only declaration proposal, not a reservation or a payment. Rebuilt
    // for every snapshot/command; the existing activation transaction revalidates.
    private sealed record SingleActiveSelection(string Text, string[] Choices, string? Rejection = null)
    {
        public string? UnavailableReason => Rejection
            ?? (Choices.Length == 0 ? "当前没有合法的选择对象" : null);
    }

    private SingleActiveSelection? EvaluateSingleActiveSelection(L12PlayerState player,
        L12CardInstance source, string ability)
        => (source.CardId, ability) switch
        {
            ("S01-04M2", "frontBuff") => new("选择我方 1 张【高天原】军团",
                PublicFactionLegions(player, "gaotianyuan").Select(card => card.InstanceId).ToArray()),
            ("S01-04M2", "kusanagi") => new("选择〈草薙剑〉置入前排的位置",
                Enumerable.Range(0, 3).Where(slot => player.Field[0][slot] is null).Select(slot => $"0:{slot}").ToArray(),
                player.Relic?.CardId == "S01-0417" ? null : "圣物区没有〈草薙剑〉"),
            ("S01-0117", "artifactSearch") => new("选择弃置的 1 张手牌",
                player.Hand.Select(card => card.InstanceId).ToArray()),
            ("S01-0417", "kusanagiDebuff") => new("选择对方 1 张军团，本回合费用 -1",
                PublicLegions(State.Players[1 - player.PlayerIndex]).Select(card => card.InstanceId).ToArray()),
            ("S01-0417", "kusanagiStrong") => new("选择我方 1 张【高天原】军团，本回合获得强攻",
                PublicFactionLegions(player, "gaotianyuan").Select(card => card.InstanceId).ToArray()),
            _ => null,
        };
}
