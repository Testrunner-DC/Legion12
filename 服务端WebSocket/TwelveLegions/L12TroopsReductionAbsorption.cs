using System.Text.Json;

namespace TwelveLegions.Server;

public static partial class L12DerivedStats
{
    internal static bool HasTroopsReductionState(L12CardInstance card)
    {
        if (card.TroopsLayerSequence != 0) return true;
        foreach (var modifier in card.TimedModifiers)
            if (modifier.TroopsLayerId is not null || modifier.TroopsReduction is not null
                || modifier.ReductionConsumedTroopsBonus != 0) return true;
        foreach (var layer in card.ContinuousTroopsBonusLayers.Values)
            if (layer.GrantId is not null || layer.ReductionConsumed != 0) return true;
        foreach (var attached in card.AttachedCards)
            if (HasTroopsReductionState(attached)) return true;
        return false;
    }

    internal static long NextTroopsLayerId(L12CardInstance card)
        => card.TroopsLayerSequence = checked(card.TroopsLayerSequence + 1);

    internal static int RemainingTroopsBonus(L12TimedModifier modifier)
        => Math.Max(0, modifier.TroopsDelta - modifier.ConsumedTroopsBonus - modifier.ReductionConsumedTroopsBonus);

    internal static int RemainingTroopsBonus(L12TroopsBonusLayer layer)
        => Math.Max(0, layer.Granted - layer.Consumed - layer.ReductionConsumed);

    internal static int EffectiveTimedTroopsDelta(L12TimedModifier modifier)
        => modifier.TroopsDelta > 0 ? modifier.TroopsDelta - modifier.ReductionConsumedTroopsBonus
            : modifier.TroopsDelta + (modifier.TroopsReduction?.Absorptions.Sum(entry => entry.Amount) ?? 0);

    // Retire only the named source's refundable reservations. Historical Amount
    // survives: an already absorbed reduction never returns merely because its
    // positive source disappeared. Newest reductions retire first on grant shrink.
    internal static void RetireTroopsAbsorptions(L12CardInstance card, string kind, long? layerId,
        string? sourceKey = null, int amount = int.MaxValue)
    {
        foreach (var modifier in card.TimedModifiers.AsEnumerable().Reverse())
        foreach (var entry in modifier.TroopsReduction?.Absorptions.AsEnumerable().Reverse()
                     ?? Enumerable.Empty<L12TroopsAbsorption>())
        {
            if (entry.Kind != kind || entry.LayerId != layerId || entry.SourceKey != sourceKey) continue;
            var retired = Math.Min(entry.RestorableAmount, amount);
            entry.RestorableAmount -= retired;
            amount -= retired;
            if (amount == 0) return;
        }
    }

    internal static void ClearTroopsReductionState(L12CardInstance card)
    {
        card.TroopsLayerSequence = 0;
        foreach (var modifier in card.TimedModifiers)
        {
            modifier.TroopsLayerId = null;
            modifier.ReductionConsumedTroopsBonus = 0;
            modifier.TroopsReduction = null;
        }
        foreach (var layer in card.ContinuousTroopsBonusLayers.Values)
        {
            layer.GrantId = null;
            layer.ReductionConsumed = 0;
        }
        foreach (var attached in card.AttachedCards) ClearTroopsReductionState(attached);
    }
}

public sealed partial class L12GameEngine
{
    private static void ValidateTroopsReductionCard(L12GameState state, L12CardInstance card)
    {
        var timed = card.TimedModifiers;
        var layers = card.ContinuousTroopsBonusLayers;
        var hasLedger = card.TroopsLayerSequence != 0 || timed.Any(entry => entry.TroopsLayerId is not null
            || entry.TroopsReduction is not null || entry.ReductionConsumedTroopsBonus != 0)
            || layers.Values.Any(layer => layer.GrantId is not null || layer.ReductionConsumed != 0);
        if (!state.TimedTroopsReductionAbsorptionProtocolEnabled)
        {
            if (hasLedger) throw new InvalidDataException("旧协议对局不能恢复兵力抵消账");
            return;
        }
        if (card.TroopsLayerSequence < 0) throw new InvalidDataException("兵力层序列无效");
        var identities = new HashSet<long>();
        void Identity(long? id)
        {
            if (id is null) return;
            if (id <= 0 || id > card.TroopsLayerSequence || !identities.Add(id.Value))
                throw new InvalidDataException("兵力层身份重复或越界");
        }
        foreach (var modifier in timed) Identity(modifier.TroopsLayerId);
        foreach (var layer in layers.Values) Identity(layer.GrantId);
        var reservations = new Dictionary<(string Kind, long Id, string? Key), long>();
        var combatReservations = new Dictionary<string, long>(StringComparer.Ordinal);
        var contexts = (state.PendingDefense is null ? Enumerable.Empty<L12PendingDefense>() : [state.PendingDefense])
            .Concat(state.SuspendedCombatContexts).ToArray();
        foreach (var modifier in timed)
        {
            if (modifier.ReductionConsumedTroopsBonus < 0
                || modifier.ReductionConsumedTroopsBonus > 0 && (modifier.TroopsDelta <= 0
                    || modifier.TroopsLayerId is null || modifier.ConsumedTroopsBonus < 0
                    || (long)modifier.ConsumedTroopsBonus + modifier.ReductionConsumedTroopsBonus > modifier.TroopsDelta))
                throw new InvalidDataException("限时正兵力层消费越额");
            if (modifier.TroopsReduction is not { } reduction) continue;
            if (modifier.TroopsDelta >= 0 || modifier.ConsumedTroopsBonus != 0 || modifier.TroopsLayerId is null
                || !string.Equals(reduction.TargetInstanceId, card.InstanceId, StringComparison.OrdinalIgnoreCase)
                || reduction.Absorptions is null)
                throw new InvalidDataException("负兵力层归属无效");
            long absorbed = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in reduction.Absorptions)
            {
                if (entry is null || entry.Amount <= 0 || entry.RestorableAmount < 0 || entry.RestorableAmount > entry.Amount)
                    throw new InvalidDataException("负兵力层抵消份额无效");
                absorbed += entry.Amount;
                if (absorbed > -(long)modifier.TroopsDelta) throw new InvalidDataException("负兵力层抵消总额越界");
                if (entry.Kind == "timed" || entry.Kind == "continuous")
                {
                    if (entry.LayerId is null or <= 0 || entry.LayerId > card.TroopsLayerSequence
                        || entry.CombatId is not null || entry.AttackerInstanceId is not null
                        || entry.Kind == "timed" && entry.SourceKey is not null
                        || entry.Kind == "continuous" && string.IsNullOrWhiteSpace(entry.SourceKey))
                        throw new InvalidDataException("正兵力来源身份无效");
                    if (!seen.Add($"{entry.Kind}:{entry.LayerId}:{entry.SourceKey}"))
                        throw new InvalidDataException("同一负层重复抵消来源");
                    if (entry.RestorableAmount == 0) continue;
                    var exists = entry.Kind == "timed"
                        ? timed.Any(positive => positive.TroopsLayerId == entry.LayerId && positive.TroopsDelta > 0)
                        : layers.TryGetValue(entry.SourceKey!, out var layer) && layer.GrantId == entry.LayerId;
                    if (!exists) throw new InvalidDataException("可归还正兵力来源已失效");
                    var key = (entry.Kind, entry.LayerId.Value, entry.SourceKey);
                    reservations[key] = reservations.GetValueOrDefault(key) + entry.RestorableAmount;
                }
                else if (entry.Kind == "attack")
                {
                    if (entry.LayerId is not null || entry.SourceKey is not null || string.IsNullOrWhiteSpace(entry.CombatId)
                        || !string.Equals(entry.AttackerInstanceId, card.InstanceId, StringComparison.OrdinalIgnoreCase)
                        || !seen.Add($"attack:{entry.CombatId}:{entry.AttackerInstanceId}"))
                        throw new InvalidDataException("本次进攻抵消身份无效");
                    if (entry.RestorableAmount == 0) continue;
                    var matches = contexts.Where(context => context.CombatId == entry.CombatId
                        && context.AttackerInstanceId == entry.AttackerInstanceId).ToArray();
                    if (matches.Length != 1 || matches[0].ActualAttackerTroopsBonus is null)
                        throw new InvalidDataException("可归还本次进攻已失效");
                    combatReservations[entry.CombatId!] = combatReservations.GetValueOrDefault(entry.CombatId!) + entry.RestorableAmount;
                }
                else throw new InvalidDataException("未知兵力抵消来源");
            }
        }
        foreach (var modifier in timed)
            if (reservations.GetValueOrDefault(("timed", modifier.TroopsLayerId ?? 0, null)) != modifier.ReductionConsumedTroopsBonus)
                throw new InvalidDataException("限时正层与负层份额不守恒");
        foreach (var pair in layers)
            if (pair.Value.ReductionConsumed < 0 || pair.Value.ReductionConsumed > pair.Value.Granted
                || reservations.GetValueOrDefault(("continuous", pair.Value.GrantId ?? 0, pair.Key)) != pair.Value.ReductionConsumed
                || (long)pair.Value.Consumed + pair.Value.ReductionConsumed > pair.Value.Granted)
                throw new InvalidDataException("持续正层与负层份额不守恒");
        foreach (var context in contexts.Where(context => context.AttackerInstanceId == card.InstanceId))
            if (context.ActualAttackerTroopsBonus is { } actual
                && ((long)actual + combatReservations.GetValueOrDefault(context.CombatId ?? "") > context.GrantedAttackerTroopsBonus))
                throw new InvalidDataException("本次进攻正层与负层份额越界");
    }

    private static void ValidateTroopsReductionCombats(L12GameState state)
    {
        var contexts = (state.PendingDefense is null ? Enumerable.Empty<L12PendingDefense>() : [state.PendingDefense])
            .Concat(state.SuspendedCombatContexts).ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var context in contexts)
        {
            if (context.ActualAttackerTroopsBonus is null && context.GrantedAttackerTroopsBonus is null) continue;
            if (!state.TimedTroopsReductionAbsorptionProtocolEnabled || context.ActualAttackerTroopsBonus is null or < 0
                || context.GrantedAttackerTroopsBonus is null or < 0
                || context.ActualAttackerTroopsBonus > context.GrantedAttackerTroopsBonus
                || string.IsNullOrWhiteSpace(context.CombatId) || !ids.Add(context.CombatId))
                throw new InvalidDataException("本次进攻正兵力账无效");
        }
    }

    private IEnumerable<L12PendingDefense> TroopsBonusCombatContexts(L12CardInstance card)
        => EnumerateCombatContexts().Where(context => context.AttackerInstanceId == card.InstanceId
            && context.ActualAttackerTroopsBonus is not null);

    private long TroopsReductionExpiryOrder(L12TimedModifier modifier)
    {
        if (modifier.OwnerTurnEndExpiry is not { } expiry)
            return (long)modifier.ExpiresAfterTurn - State.TurnSerial;
        var player = State.ActivePlayer;
        var extraTurn = State.ExtraTurnsForPlayer;
        // This mirrors the already-authoritative one-extra-turn transition;
        // it neither advances state nor predicts effects that have not resolved.
        for (var offset = 0; offset < 4; offset++)
        {
            if (player == expiry.Controller && (long)State.TurnSerial + offset > expiry.GrantedTurnSerial)
                return offset;
            if (extraTurn == player) extraTurn = -1;
            else player = 1 - player;
        }
        throw new InvalidDataException("所属回合兵力层的到期顺序无效");
    }

    private void AbsorbTimedTroopsReduction(L12CardInstance card, L12TimedModifier modifier)
    {
        if (!State.TimedTroopsReductionAbsorptionProtocolEnabled || modifier.TroopsDelta >= 0) return;
        modifier.TroopsLayerId ??= L12DerivedStats.NextTroopsLayerId(card);
        modifier.TroopsReduction = new() { TargetInstanceId = card.InstanceId };
        var left = checked(-modifier.TroopsDelta);
        void Add(L12TroopsAbsorption entry)
        {
            modifier.TroopsReduction.Absorptions.Add(entry);
            left -= entry.Amount;
        }

        // Generated attacks may suspend another materialized attack on the same
        // physical attacker. Never infer ownership from the first matching card.
        foreach (var context in TroopsBonusCombatContexts(card))
        {
            var amount = Math.Min(left, context.ActualAttackerTroopsBonus!.Value);
            if (amount <= 0) continue;
            context.ActualAttackerTroopsBonus -= amount;
            context.TemporaryAttackerTroopsBonus -= amount;
            Add(new() { Kind = "attack", CombatId = context.CombatId,
                AttackerInstanceId = card.InstanceId, Amount = amount, RestorableAmount = amount });
        }
        foreach (var positive in card.TimedModifiers.Where(entry => entry.TroopsDelta > 0)
                     .OrderBy(TroopsReductionExpiryOrder))
        {
            var amount = Math.Min(left, L12DerivedStats.RemainingTroopsBonus(positive));
            if (amount <= 0) continue;
            positive.TroopsLayerId ??= L12DerivedStats.NextTroopsLayerId(card);
            positive.ReductionConsumedTroopsBonus += amount;
            Add(new() { Kind = "timed", LayerId = positive.TroopsLayerId,
                Amount = amount, RestorableAmount = amount });
        }
        foreach (var pair in card.ContinuousTroopsBonusLayers.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var amount = Math.Min(left, L12DerivedStats.RemainingTroopsBonus(pair.Value));
            if (amount <= 0) continue;
            pair.Value.GrantId ??= L12DerivedStats.NextTroopsLayerId(card);
            pair.Value.ReductionConsumed += amount;
            card.ContinuousTroopsBonusConsumed += amount;
            card.ContinuousTroopsModifier -= amount;
            Add(new() { Kind = "continuous", LayerId = pair.Value.GrantId, SourceKey = pair.Key,
                Amount = amount, RestorableAmount = amount });
        }
    }

    private void CancelTroopsReduction(L12CardInstance card, L12TimedModifier modifier)
    {
        var reduction = modifier.TroopsReduction
            ?? throw new InvalidDataException("负兵力层缺少抵消归属");
        // Only the unabsorbed part was a direct reduction of the card's own
        // troops. Other parts return solely through their still-live source.
        card.Troops += checked(-modifier.TroopsDelta - reduction.Absorptions.Sum(entry => entry.Amount));
        foreach (var entry in reduction.Absorptions)
        {
            if (entry.RestorableAmount == 0) continue;
            if (entry.Kind == "timed")
            {
                var positive = card.TimedModifiers.SingleOrDefault(layer => layer.TroopsLayerId == entry.LayerId
                    && layer.TroopsDelta > 0);
                if (positive is not null)
                {
                    var before = L12DerivedStats.RemainingTroopsBonus(positive);
                    positive.ReductionConsumedTroopsBonus -= entry.RestorableAmount;
                    card.Troops += L12DerivedStats.RemainingTroopsBonus(positive) - before;
                }
            }
            else if (entry.Kind == "continuous")
            {
                if (card.ContinuousTroopsBonusLayers.TryGetValue(entry.SourceKey!, out var layer)
                    && layer.GrantId == entry.LayerId)
                {
                    var before = L12DerivedStats.RemainingTroopsBonus(layer);
                    layer.ReductionConsumed -= entry.RestorableAmount;
                    var restored = L12DerivedStats.RemainingTroopsBonus(layer) - before;
                    card.Troops += restored;
                    card.ContinuousTroopsBonusConsumed -= entry.RestorableAmount;
                    card.ContinuousTroopsModifier += restored;
                }
            }
            else if (entry.Kind == "attack")
            {
                var context = TroopsBonusCombatContexts(card).SingleOrDefault(context =>
                    context.CombatId == entry.CombatId && context.AttackerInstanceId == entry.AttackerInstanceId);
                if (context is not null)
                {
                    context.ActualAttackerTroopsBonus += entry.RestorableAmount;
                    context.TemporaryAttackerTroopsBonus += entry.RestorableAmount;
                    card.Troops += entry.RestorableAmount;
                }
            }
            entry.RestorableAmount = 0;
        }
    }

    private void RetireCombatTroopsAbsorptions(L12PendingDefense pending, L12CardInstance? attacker)
    {
        if (attacker is null) return;
        foreach (var modifier in attacker.TimedModifiers)
        foreach (var entry in modifier.TroopsReduction?.Absorptions ?? [])
            if (entry.Kind == "attack" && entry.CombatId == pending.CombatId
                && entry.AttackerInstanceId == pending.AttackerInstanceId)
                entry.RestorableAmount = 0;
    }

    private void RebuildTimedTroopsAbsorptions(L12CardInstance card)
    {
        if (!State.TimedTroopsReductionAbsorptionProtocolEnabled) return;
        foreach (var modifier in card.TimedModifiers)
        {
            modifier.ReductionConsumedTroopsBonus = 0;
            modifier.TroopsReduction = null;
        }
        foreach (var layer in card.ContinuousTroopsBonusLayers.Values) layer.ReductionConsumed = 0;
        foreach (var modifier in card.TimedModifiers.Where(entry => entry.TroopsDelta < 0))
            AbsorbTimedTroopsReduction(card, modifier);
    }

    private static string TroopsReductionFingerprintData(L12TimedModifier modifier)
        => JsonSerializer.Serialize(new
        {
            modifier.TroopsLayerId,
            modifier.TroopsReduction?.TargetInstanceId,
            Absorptions = modifier.TroopsReduction?.Absorptions.Select(entry => new
            {
                entry.Kind, entry.LayerId, entry.SourceKey, entry.CombatId,
                entry.AttackerInstanceId, entry.Amount,
            }),
        });
}
