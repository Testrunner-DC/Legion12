namespace TwelveLegions.Server;

/// <summary>
/// 不进入效果堆叠的规则动作统一入口。卡牌结构定义负责提供文字和后台呈现场景，
/// 当前对局状态只在这里回答资格；命令提交必须复用同一判断。
/// </summary>
public sealed partial class L12GameEngine
{
    private L12EffectPresentationScene? NativeCavalryMovePresentation(string cardId)
        => _catalog.AtomicEffects.Find(cardId)?.Abilities
            .Where(L12EffectPresentationScenes.IsCavalryMoveRuleAction)
            .SelectMany(ability => ability.Presentations)
            .SingleOrDefault(scene => scene.EventType == "rule-action"
                && scene.Flow == "rule-action:cavalry-move");

    private List<L12RuleActionView> BuildRuleActionViews(L12PlayerState player,
        L12CardInstance card, int row)
    {
        if (!IsFieldLegion(card)) return [];
        var actions = new List<L12RuleActionView>();
        if (L12StructuredCardRules.HasProfession(card, row, "骑兵"))
        {
            var scene = NativeCavalryMovePresentation(card.CardId);
            var text = scene is null
                ? "我方 回合1次 可进行1次骑兵位移。"
                : State.EffectPresentationSnapshot?.FirstOrDefault(item => item.SceneId == scene.SceneId)?.Text
                    ?? scene.DefaultText;
            var targetKeys = CavalryMoveDestinationKeys(player);
            var reason = CavalryMoveTimingUnavailableReason(player.PlayerIndex)
                ?? CavalryMoveSourceUnavailableReason(player, card, row)
                    ?? (targetKeys.Count == 0
                        ? L12ActiveDisasterRules.ForbidsBackRowLegionPlacement(State.ActiveDisaster?.CardId)
                            ? "〈腐秽大地〉持续期间没有可位移的前排空位"
                            : "战场没有可位移的空位"
                        : null);
            actions.Add(new L12RuleActionView("cavalryMove", scene?.Label ?? "骑兵位移", text,
                reason is null, reason, scene?.SceneId, targetKeys));
        }

        if (CanAct(player.PlayerIndex) && !card.Tapped && !card.Hidden
            && FindOnField(player, card.InstanceId, out var sourceRow, out var sourceSlot) is not null)
        {
            var freeTargets = FreeOrdinaryMoveDestinationKeys(player, card, sourceRow, sourceSlot);
            if (freeTargets.Count > 0)
                actions.Add(new L12RuleActionView("freeMove", "免费位移",
                    "可将该军团免费移动至相邻的合法空位。", TargetKeys: freeTargets));
        }
        return actions;
    }

    private string TenkaFreeMoveKey(L12CardInstance card)
        => $"s2-tenka-free-move:{card.InstanceId}:{State.TurnSerial}";

    private bool HasFreeOrdinaryMove(L12PlayerState player, L12CardInstance card,
        int sourceRow, int targetRow, out string? consumableKey)
    {
        var tenkaKey = TenkaFreeMoveKey(card);
        if (player.UsedAbilities.Contains(tenkaKey))
        {
            consumableKey = tenkaKey;
            return true;
        }
        consumableKey = null;
        return sourceRow != targetRow
            && PublicLegions(player).Any(candidate => candidate.CardId == "S02-0510" && candidate.Tapped);
    }

    private string? OrdinaryMoveDestinationUnavailableReason(L12PlayerState player,
        int sourceRow, int sourceSlot, int targetRow, int targetSlot)
    {
        if (L12ActiveDisasterRules.ForbidsBackRowLegionPlacement(State.ActiveDisaster?.CardId) && targetRow == 1)
            return "〈腐秽大地〉持续期间无法位移至后排";
        if (Math.Abs(sourceRow - targetRow) + Math.Abs(sourceSlot - targetSlot) != 1)
            return "规则位移每次只能移动至相邻空格";
        return player.Field[targetRow][targetSlot] is null ? null : "目标阵地已占用";
    }

    private List<string> FreeOrdinaryMoveDestinationKeys(L12PlayerState player,
        L12CardInstance card, int sourceRow, int sourceSlot)
        => player.Field.SelectMany((slots, row) => slots.Select((_, slot) => (row, slot)))
            .Where(target => OrdinaryMoveDestinationUnavailableReason(player,
                    sourceRow, sourceSlot, target.row, target.slot) is null
                && HasFreeOrdinaryMove(player, card, sourceRow, target.row, out _))
            .Select(target => $"{target.row}:{target.slot}")
            .ToList();

    private string? CavalryMoveTimingUnavailableReason(int playerIndex)
        => CanAct(playerIndex) ? null : "仅在我方主要阶段且没有待处理操作时可以进行骑兵位移";

    private string? CavalryMoveSourceUnavailableReason(L12PlayerState player,
        L12CardInstance card, int row)
    {
        if (!IsFieldLegion(card) || card.Tapped || card.Hidden
            || !L12StructuredCardRules.HasProfession(card, row, "骑兵"))
            return "只能令活跃且未覆盖的【骑兵】进行骑兵位移";
        if (card.LastCavalryMoveTurn == State.TurnSerial)
            return "该军团本回合已经进行过骑兵位移";
        return null;
    }

    private List<string> CavalryMoveDestinationKeys(L12PlayerState player)
        => player.Field.SelectMany((slots, row) => slots.Select((card, slot) => (row, slot, card)))
            .Where(candidate => IsLegalCavalryMoveDestination(player, candidate.row, candidate.slot))
            .Select(candidate => $"{candidate.row}:{candidate.slot}")
            .ToList();

    private bool IsLegalCavalryMoveDestination(L12PlayerState player, int row, int slot)
        => row is >= 0 and <= 1 && slot is >= 0 and <= 2
            && player.Field[row][slot] is null
            && (!L12ActiveDisasterRules.ForbidsBackRowLegionPlacement(State.ActiveDisaster?.CardId) || row == 0);
}
