namespace TwelveLegions.Server;

public sealed record L12LibraryResult(bool Success, IReadOnlyList<L12CardInstance> Cards, string? Error = null);

public static class L12LibraryOps
{
    public static L12LibraryResult Draw(L12PlayerState player, int count)
    {
        if (count < 0) return new(false, [], "抽牌数量不能为负数");
        if (player.Library.Count < count) return new(false, [], "牌库数量不足");
        var cards = player.Library.Take(count).ToArray();
        player.Library.RemoveRange(0, count);
        player.Hand.AddRange(cards);
        return new(true, cards);
    }

    public static L12LibraryResult ViewTop(L12PlayerState player, int count)
    {
        if (count < 0) return new(false, [], "查看数量不能为负数");
        if (player.Library.Count < count) return new(false, [], "牌库数量不足");
        return new(true, player.Library.Take(count).ToArray());
    }

    public static L12LibraryResult Mill(L12PlayerState player, int count)
    {
        if (count < 0) return new(false, [], "弃置数量不能为负数");
        if (player.Library.Count < count) return new(false, [], "牌库数量不足");
        var cards = player.Library.Take(count).ToArray();
        player.Library.RemoveRange(0, count);
        player.Graveyard.AddRange(cards);
        return new(true, cards);
    }

    public static IReadOnlyList<L12CardInstance> Search(L12PlayerState player, Func<L12CardInstance, bool> predicate)
        => player.Library.Where(predicate).ToArray();

    public static bool PutOnTop(L12PlayerState player, IEnumerable<L12CardInstance> cards)
        => MoveKnownCards(player, cards, top: true);

    public static bool PutOnBottom(L12PlayerState player, IEnumerable<L12CardInstance> cards)
        => MoveKnownCards(player, cards, top: false);

    public static bool ReorderTop(L12PlayerState player, IReadOnlyList<string> orderedIds)
    {
        if (orderedIds.Count != orderedIds.Distinct(StringComparer.OrdinalIgnoreCase).Count()) return false;
        var top = player.Library.Take(orderedIds.Count).ToArray();
        if (top.Select(card => card.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(orderedIds) is false) return false;
        var byId = top.ToDictionary(card => card.InstanceId, StringComparer.OrdinalIgnoreCase);
        player.Library.RemoveRange(0, orderedIds.Count);
        player.Library.InsertRange(0, orderedIds.Select(id => byId[id]));
        return true;
    }

    public static void Shuffle<T>(IList<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static bool MoveKnownCards(L12PlayerState player, IEnumerable<L12CardInstance> cards, bool top)
    {
        var ordered = cards.ToArray();
        if (ordered.Length != ordered.Select(card => card.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count()) return false;
        foreach (var card in ordered)
        {
            player.Hand.Remove(card);
            player.Graveyard.Remove(card);
            player.Removed.Remove(card);
            player.Library.Remove(card);
        }
        if (top) player.Library.InsertRange(0, ordered);
        else player.Library.AddRange(ordered);
        return true;
    }
}

public static class L12DerivedStats
{
    public static int CurrentTroops(L12CardInstance card, int turnSerial)
    {
        var setValue = card.SetTroopsValue is not null && card.SetTroopsUntilTurn >= turnSerial
            ? card.SetTroopsValue.Value
            : card.Troops - card.ContinuousTroopsModifier;
        return setValue + card.ContinuousTroopsModifier;
    }

    public static void SetUntilTurnEnd(L12CardInstance card, int value, int turnSerial)
    {
        card.SetTroopsValue = value;
        card.SetTroopsUntilTurn = turnSerial;
        card.Troops = value + card.ContinuousTroopsModifier;
    }

    public static void ApplyContinuousModifier(L12CardInstance card, int modifier, int turnSerial)
        => ApplyContinuousModifiers(card, Math.Max(0, modifier), Math.Min(0, modifier), turnSerial);

    public static void ApplyContinuousModifiers(L12CardInstance card, int bonus, int penalty, int turnSerial)
        => ApplyContinuousModifiers(card,
            bonus > 0 ? new Dictionary<string, int>(StringComparer.Ordinal) { ["aggregate"] = bonus } : null,
            penalty, turnSerial);

    public static void ApplyContinuousModifiers(L12CardInstance card,
        IReadOnlyDictionary<string, int>? bonuses, int penalty, int turnSerial)
    {
        bonuses ??= new Dictionary<string, int>(StringComparer.Ordinal);
        if (bonuses.Values.Any(value => value <= 0)) throw new ArgumentOutOfRangeException(nameof(bonuses));
        if (penalty > 0) throw new ArgumentOutOfRangeException(nameof(penalty));
        RestoreLegacyContinuousLayers(card);

        var oldRemaining = card.ContinuousTroopsBonusLayers.Values
            .Sum(layer => Math.Max(0, layer.Granted - layer.Consumed));
        var oldPenalty = card.ContinuousTroopsPenalty;
        foreach (var key in card.ContinuousTroopsBonusLayers.Keys
                     .Concat(bonuses.Keys).Distinct(StringComparer.Ordinal).ToArray())
        {
            card.ContinuousTroopsBonusLayers.TryGetValue(key, out var oldLayer);
            var oldGranted = oldLayer?.Granted ?? 0;
            var layerRemaining = Math.Max(0, oldGranted - (oldLayer?.Consumed ?? 0));
            var newGranted = bonuses.GetValueOrDefault(key);
            var newRemaining = newGranted >= oldGranted
                ? layerRemaining + newGranted - oldGranted
                : Math.Min(layerRemaining, newGranted);
            if (newGranted == 0)
            {
                card.ContinuousTroopsBonusLayers.Remove(key);
                continue;
            }
            card.ContinuousTroopsBonusLayers[key] = new L12TroopsBonusLayer
            {
                Granted = newGranted,
                Consumed = newGranted - newRemaining,
            };
        }

        var totalGranted = card.ContinuousTroopsBonusLayers.Values.Sum(layer => layer.Granted);
        var totalConsumed = card.ContinuousTroopsBonusLayers.Values.Sum(layer => layer.Consumed);
        var totalRemaining = totalGranted - totalConsumed;
        card.Troops += totalRemaining + penalty - oldRemaining - oldPenalty;
        card.ContinuousTroopsBonusGranted = totalGranted;
        card.ContinuousTroopsBonusConsumed = totalConsumed;
        card.ContinuousTroopsPenalty = penalty;
        card.ContinuousTroopsModifier = totalRemaining + penalty;
    }

    private static void RestoreLegacyContinuousLayers(L12CardInstance card)
    {
        // 兼容旧快照：此前只有聚合后的 ContinuousTroopsModifier。
        if (card.ContinuousTroopsBonusLayers.Count == 0 && card.ContinuousTroopsBonusGranted > 0)
            card.ContinuousTroopsBonusLayers["legacy"] = new L12TroopsBonusLayer
            {
                Granted = card.ContinuousTroopsBonusGranted,
                Consumed = card.ContinuousTroopsBonusConsumed,
            };
        else if (card.ContinuousTroopsBonusLayers.Count == 0 && card.ContinuousTroopsBonusGranted == 0
            && card.ContinuousTroopsBonusConsumed == 0 && card.ContinuousTroopsPenalty == 0
            && card.ContinuousTroopsModifier > 0)
            card.ContinuousTroopsBonusLayers["legacy"] = new L12TroopsBonusLayer
            {
                Granted = card.ContinuousTroopsModifier,
            };
        if (card.ContinuousTroopsPenalty == 0 && card.ContinuousTroopsModifier < 0)
        {
            card.ContinuousTroopsPenalty = card.ContinuousTroopsModifier;
        }
    }

    /// <summary>
    /// 兵力伤害的唯一数值入口。伤害依次消耗本次进攻、即将到期的本回合加成和持续加成，
    /// 再减损军团自身兵力；返回本次进攻加成的剩余量，供进攻结束时只撤销未消耗部分。
    /// </summary>
    public static int ApplyTroopsDamage(L12CardInstance card, int amount, int immediateBonus = 0)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var damageLeft = amount;
        var remainingImmediateBonus = immediateBonus;
        if (remainingImmediateBonus > 0)
        {
            var consumed = Math.Min(remainingImmediateBonus, damageLeft);
            remainingImmediateBonus -= consumed;
            damageLeft -= consumed;
        }

        foreach (var modifier in card.TimedModifiers
                     .Where(modifier => modifier.TroopsDelta > modifier.ConsumedTroopsBonus)
                     .OrderBy(modifier => modifier.ExpiresAfterTurn))
        {
            if (damageLeft == 0) break;
            var available = modifier.TroopsDelta - modifier.ConsumedTroopsBonus;
            var consumed = Math.Min(available, damageLeft);
            modifier.ConsumedTroopsBonus += consumed;
            damageLeft -= consumed;
        }

        if (damageLeft > 0)
        {
            foreach (var layer in card.ContinuousTroopsBonusLayers.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                         .Select(entry => entry.Value))
            {
                if (damageLeft == 0) break;
                var available = Math.Max(0, layer.Granted - layer.Consumed);
                var consumed = Math.Min(available, damageLeft);
                layer.Consumed += consumed;
                damageLeft -= consumed;
                card.ContinuousTroopsBonusConsumed += consumed;
                card.ContinuousTroopsModifier -= consumed;
            }
        }

        card.Troops -= amount;
        return remainingImmediateBonus;
    }

    /// <summary>
    /// 回合边界移除到期状态并清除累计伤害，从仍有效的设定值、限时层和持续层重建无伤派生兵力。
    /// </summary>
    public static void ResetForCompletedTurn(L12CardInstance card, int completedTurn)
    {
        card.TimedModifiers.RemoveAll(modifier => modifier.ExpiresAfterTurn <= completedTurn);
        if (card.SetTroopsUntilTurn <= completedTurn)
        {
            card.SetTroopsValue = null;
            card.SetTroopsUntilTurn = -1;
        }

        foreach (var modifier in card.TimedModifiers)
            modifier.ConsumedTroopsBonus = 0;
        RestoreLegacyContinuousLayers(card);
        foreach (var layer in card.ContinuousTroopsBonusLayers.Values)
            layer.Consumed = 0;
        card.ContinuousTroopsBonusGranted = card.ContinuousTroopsBonusLayers.Values.Sum(layer => layer.Granted);
        card.ContinuousTroopsBonusConsumed = 0;
        card.ContinuousTroopsModifier = card.ContinuousTroopsBonusGranted + card.ContinuousTroopsPenalty;
        card.Troops = card.DisplayBaseTroops
            + card.TimedModifiers.Sum(modifier => modifier.TroopsDelta)
            + card.ContinuousTroopsModifier;
        card.CostModifier = card.TimedModifiers.Sum(modifier => modifier.CostDelta);
    }
}

public static class L12TriggerBatchPlanner
{
    public static IReadOnlyList<IReadOnlyList<L12TriggerCandidate>> Plan(
        IEnumerable<L12TriggerCandidate> candidates, int activePlayer)
    {
        var materialized = candidates.ToArray();
        return new[] { activePlayer, 1 - activePlayer }
            .Select(controller => (IReadOnlyList<L12TriggerCandidate>)materialized
                .Where(candidate => candidate.Controller == controller).ToArray())
            .Where(batch => batch.Count > 0)
            .ToArray();
    }
}

public static class L12S2ZoneOps
{
    public static void GainRunes(L12PlayerState player, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        player.SpecialZones.Runes = Math.Min(3, player.SpecialZones.Runes + count);
    }

    public static bool SpendRunes(L12PlayerState player, int count)
    {
        if (count < 0 || player.SpecialZones.Runes < count) return false;
        player.SpecialZones.Runes -= count;
        return true;
    }

    public static bool ConsumeAndFlipGodPower(L12PlayerState player, int count)
    {
        if (count < 0) return false;
        var moralePower = player.Morale
            .Where(card => card.IsGodPower && !card.Tapped)
            .Take(count)
            .ToArray();
        if (moralePower.Length < count) return false;
        foreach (var card in moralePower)
        {
            card.Tapped = true;
            card.IsGodPower = false;
        }
        return true;
    }

    public static bool ConsumeGodPower(L12PlayerState player, int count)
    {
        if (count < 0) return false;
        var moralePower = player.Morale
            .Where(card => card.IsGodPower && !card.Tapped)
            .Take(count)
            .ToArray();
        if (moralePower.Length < count) return false;
        foreach (var card in moralePower) card.Tapped = true;
        return true;
    }

    public static bool FlipMoraleFace(L12PlayerState player, string instanceId, bool? toGodPower = null)
    {
        var morale = player.Morale.FirstOrDefault(card => card.InstanceId == instanceId);
        if (morale is null || morale.CardId is not ("S02-05C1" or "S02-05C1A")) return false;
        morale.IsGodPower = toGodPower ?? !morale.IsGodPower;
        return true;
    }

    public static bool Promote(L12PlayerState player, L12CardInstance foundation, L12CardInstance promoted, int godPowerCost)
    {
        if (!promoted.HasTrait("晋升者") || foundation.HasTrait("晋升者")) return false;
        var normalizedPromotedName = promoted.Name.Replace("·晋升", string.Empty, StringComparison.Ordinal);
        var knownPair = (promoted.CardId, foundation.CardId) is
            ("S02-0501", "S02-0502") or ("S02-0503", "S02-0504")
            or ("S02-0505", "S02-0506") or ("S02-0507", "S02-0508");
        if (!(knownPair || foundation.Name.Equals(normalizedPromotedName, StringComparison.Ordinal))
            || foundation.Faction != "olympus" || promoted.Faction != "olympus") return false;
        var position = (Row: -1, Slot: -1);
        for (var row = 0; row < player.Field.Length; row++)
        for (var slot = 0; slot < player.Field[row].Length; slot++)
            if (player.Field[row][slot]?.InstanceId == foundation.InstanceId) position = (row, slot);
        if (position.Row < 0 || !ConsumeAndFlipGodPower(player, godPowerCost)) return false;

        promoted.Tapped = foundation.Tapped;
        promoted.HasCharge |= foundation.HasCharge;
        promoted.HasStrongAttack |= foundation.HasStrongAttack;
        promoted.HasSureHit |= foundation.HasSureHit;
        promoted.ImmortalUses += foundation.ImmortalUses;
        promoted.ImmortalUntilTurn = Math.Max(promoted.ImmortalUntilTurn, foundation.ImmortalUntilTurn);
        promoted.SummonRound = foundation.SummonRound;
        promoted.AttacksThisTurn = foundation.AttacksThisTurn;
        promoted.AttachedCards.Add(foundation);
        player.Hand.Remove(promoted);
        player.Field[position.Row][position.Slot] = promoted;
        return true;
    }
}
