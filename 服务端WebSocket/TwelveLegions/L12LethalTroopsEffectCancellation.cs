using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TwelveLegions.Server;

/// <summary>
/// 在一次权威效果工作内，把刚写入的单个负兵力限时层绑定到随后建立的效果致死替代。
/// 临时绑定从不进入检查点；一旦出现需要玩家决定的替代，只把可恢复的精确撤销配方写入
/// 既有 Prompt.Data。接受替代只撤销该兵力贡献，不重置整张卡或其他状态层。
/// </summary>
public sealed partial class L12GameEngine
{
    private const string LethalTroopsCancellationPrefix = "lethalTroopsCancellation:";
    private const string LethalTroopsCancellationVersion = "1";
    private const string StateBasedLethalTroopsReason = "因兵力不高于0阵亡";

    private sealed record PendingLethalTroopsEffectContribution(
        string TargetInstanceId,
        int ModifierIndex,
        string Fingerprint);

    private sealed record ValidatedLethalTroopsEffectCancellation(
        int ModifierIndex,
        L12TimedModifier Modifier);

    // 与 _pendingKillSourceEvents 相同，这只是一次同步效果工作的调用栈事实；
    // 若结算暂停，恢复所需的内容必须已经进入 Prompt.Data。
    private readonly List<PendingLethalTroopsEffectContribution> _pendingLethalTroopsEffectContributions = [];

    private void RecordPendingLethalTroopsEffectContribution(
        L12CardInstance target, int modifierIndex)
    {
        if (modifierIndex < 0 || modifierIndex >= target.TimedModifiers.Count) return;
        var modifier = target.TimedModifiers[modifierIndex];
        if (modifier.TroopsDelta >= 0) return;
        _pendingLethalTroopsEffectContributions.Add(new(
            target.InstanceId,
            modifierIndex,
            LethalTroopsModifierFingerprint(target, modifier)));
    }

    private void ClearPendingLethalTroopsEffectContributions()
        => _pendingLethalTroopsEffectContributions.Clear();

    private void AttachPendingLethalTroopsEffectCancellation(
        L12CardInstance protectedCard, L12Prompt prompt, string reason)
    {
        if (!string.Equals(reason, StateBasedLethalTroopsReason, StringComparison.Ordinal)) return;
        var pending = _pendingLethalTroopsEffectContributions.LastOrDefault(entry =>
            string.Equals(entry.TargetInstanceId, protectedCard.InstanceId,
                StringComparison.OrdinalIgnoreCase));
        if (pending is null) return;

        prompt.Data[$"{LethalTroopsCancellationPrefix}version"] = LethalTroopsCancellationVersion;
        prompt.Data[$"{LethalTroopsCancellationPrefix}modifierIndex"] =
            pending.ModifierIndex.ToString(CultureInfo.InvariantCulture);
        prompt.Data[$"{LethalTroopsCancellationPrefix}fingerprint"] = pending.Fingerprint;
        _pendingLethalTroopsEffectContributions.Remove(pending);
    }

    private bool TryCancelPendingLethalTroopsEffectContribution(
        L12CardInstance protectedCard, string reason)
    {
        if (!string.Equals(reason, StateBasedLethalTroopsReason, StringComparison.Ordinal)) return false;
        var pending = _pendingLethalTroopsEffectContributions.LastOrDefault(entry =>
            string.Equals(entry.TargetInstanceId, protectedCard.InstanceId,
                StringComparison.OrdinalIgnoreCase));
        if (pending is null) return false;
        var validated = ValidateLethalTroopsEffectContribution(protectedCard,
            pending.ModifierIndex, pending.Fingerprint);
        ApplyLethalTroopsEffectCancellation(protectedCard, validated);
        _pendingLethalTroopsEffectContributions.Remove(pending);
        return true;
    }

    private void ValidateEffectLethalTroopsCancellationRecipe(L12Prompt prompt)
    {
        if (!HasAnyLethalTroopsCancellationRecipeData(prompt)) return; // 修复前Prompt沿既有legacy语义恢复。
        _ = ReadValidatedLethalTroopsEffectCancellation(prompt);
    }

    private bool TryApplyEffectLethalTroopsCancellation(
        L12CardInstance protectedCard, L12Prompt prompt)
    {
        if (!HasAnyLethalTroopsCancellationRecipeData(prompt)) return false;
        var validated = ReadValidatedLethalTroopsEffectCancellation(prompt, protectedCard);
        ApplyLethalTroopsEffectCancellation(protectedCard, validated);
        return true;
    }

    private ValidatedLethalTroopsEffectCancellation ReadValidatedLethalTroopsEffectCancellation(
        L12Prompt prompt, L12CardInstance? expectedCard = null)
    {
        var versionKey = $"{LethalTroopsCancellationPrefix}version";
        var indexKey = $"{LethalTroopsCancellationPrefix}modifierIndex";
        var fingerprintKey = $"{LethalTroopsCancellationPrefix}fingerprint";
        if (prompt.Data.GetValueOrDefault(versionKey) != LethalTroopsCancellationVersion
            || !int.TryParse(prompt.Data.GetValueOrDefault(indexKey), NumberStyles.None,
                CultureInfo.InvariantCulture, out var modifierIndex)
            || prompt.Data.GetValueOrDefault(fingerprintKey) is not { Length: > 0 } fingerprint
            || prompt.Data.Keys.Any(key => key.StartsWith(LethalTroopsCancellationPrefix,
                    StringComparison.Ordinal)
                && key != versionKey && key != indexKey && key != fingerprintKey))
            throw new InvalidDataException("致命效果抵消配方无效");

        var cardInstanceId = prompt.Data.GetValueOrDefault("cardInstanceId");
        if (prompt.PlayerIndex is < 0 or > 1
            || string.IsNullOrWhiteSpace(cardInstanceId)
            || !string.Equals(prompt.Data.GetValueOrDefault("reason"),
                StateBasedLethalTroopsReason, StringComparison.Ordinal))
            throw new InvalidDataException("致命效果抵消配方来源无效");
        var protectedCard = expectedCard
            ?? FindOnField(State.Players[prompt.PlayerIndex], cardInstanceId, out _, out _);
        if (protectedCard is null
            || !string.Equals(protectedCard.InstanceId, cardInstanceId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("致命效果抵消对象已失效");
        return ValidateLethalTroopsEffectContribution(protectedCard, modifierIndex, fingerprint);
    }

    private static ValidatedLethalTroopsEffectCancellation ValidateLethalTroopsEffectContribution(
        L12CardInstance protectedCard, int modifierIndex, string fingerprint)
    {
        if (modifierIndex < 0 || modifierIndex >= protectedCard.TimedModifiers.Count)
            throw new InvalidDataException("致命效果抵消层不存在");
        var modifier = protectedCard.TimedModifiers[modifierIndex];
        if (modifier.TroopsDelta >= 0 || modifier.ConsumedTroopsBonus != 0
            || !string.Equals(LethalTroopsModifierFingerprint(protectedCard, modifier),
                fingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("致命效果抵消层指纹不匹配");
        return new(modifierIndex, modifier);
    }

    private static void ApplyLethalTroopsEffectCancellation(
        L12CardInstance protectedCard, ValidatedLethalTroopsEffectCancellation cancellation)
    {
        var modifier = cancellation.Modifier;
        protectedCard.Troops -= modifier.TroopsDelta;
        if (modifier.CostDelta == 0)
        {
            protectedCard.TimedModifiers.RemoveAt(cancellation.ModifierIndex);
            return;
        }

        // 同一既有层若还包含费用贡献，只撤销致命的兵力维度。
        protectedCard.TimedModifiers[cancellation.ModifierIndex] = new L12TimedModifier
        {
            TroopsDelta = 0,
            ConsumedTroopsBonus = 0,
            CostDelta = modifier.CostDelta,
            ExpiresAfterTurn = modifier.ExpiresAfterTurn,
            OwnerTurnEndExpiry = modifier.OwnerTurnEndExpiry,
            Source = modifier.Source,
        };
    }

    private static bool HasAnyLethalTroopsCancellationRecipeData(L12Prompt prompt)
        => prompt.Data.Keys.Any(key => key.StartsWith(LethalTroopsCancellationPrefix,
            StringComparison.Ordinal));

    private static string LethalTroopsModifierFingerprint(
        L12CardInstance target, L12TimedModifier modifier)
    {
        var raw = new StringBuilder();
        static void Append(StringBuilder builder, string name, string? value)
        {
            builder.Append(name).Append('=').Append(value?.Length ?? -1).Append(':')
                .Append(value).Append(';');
        }

        Append(raw, "target", target.InstanceId);
        Append(raw, "troops", modifier.TroopsDelta.ToString(CultureInfo.InvariantCulture));
        Append(raw, "consumed", modifier.ConsumedTroopsBonus.ToString(CultureInfo.InvariantCulture));
        Append(raw, "cost", modifier.CostDelta.ToString(CultureInfo.InvariantCulture));
        Append(raw, "expires", modifier.ExpiresAfterTurn.ToString(CultureInfo.InvariantCulture));
        Append(raw, "owner", modifier.OwnerTurnEndExpiry is { } ownerExpiry
            ? ownerExpiry.Controller.ToString(CultureInfo.InvariantCulture) : null);
        Append(raw, "granted", modifier.OwnerTurnEndExpiry is { } grantedExpiry
            ? grantedExpiry.GrantedTurnSerial.ToString(CultureInfo.InvariantCulture) : null);
        Append(raw, "source", modifier.Source);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw.ToString())));
    }
}
