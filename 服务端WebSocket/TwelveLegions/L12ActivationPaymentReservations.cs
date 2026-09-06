namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private const string PrideMasterSurchargeDeclarationKey = "prideMasterSurcharge";
    private const string PrideMasterSurchargeCommitBarrier = "pride-surcharge-commit-pending";

    private static bool HasPrideMasterSurchargeStep(L12PendingActivation activation)
        => activation.SelectionSteps.Any(step => step.DeclarationKey?.Equals(
            PrideMasterSurchargeDeclarationKey, StringComparison.OrdinalIgnoreCase) == true);

    private static bool IsOrdinaryPaymentSelectionStep(L12ActivationSelectionStep step)
        => step.Kind is "resource-payment" or "composite-ordinary-payment";

    private static bool IsResourceClaimSelectionStep(L12ActivationSelectionStep step)
        => IsOrdinaryPaymentSelectionStep(step)
            || step.Kind is "mixed-board-payment" or "composite-glory-god-power-cost"
                or "field-legion-cost" or "resource-return"
            || step.DeclarationKey?.Contains("cost", StringComparison.OrdinalIgnoreCase) == true;

    private void RefreshReservedOrdinaryPaymentChoices(
        L12PendingActivation activation, L12ActivationSelectionStep step)
    {
        var player = State.Players[activation.Controller];
        var reserved = DeclaredResourceClaimsBeforeCurrentStep(activation);
        var available = CompositeOrdinaryPaymentChoices(player)
            .Where(id => !reserved.Contains(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var choices = step.Kind == "composite-ordinary-payment"
            ? CompositeOrdinaryPaymentChoices(player).Where(available.Contains).ToList()
            : step.ValidChoices.Where(available.Contains).ToList();
        step.ValidChoices.Clear();
        step.ValidChoices.AddRange(choices);
    }

    private static HashSet<string> DeclaredResourceClaimsBeforeCurrentStep(L12PendingActivation activation)
    {
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in activation.SelectionSteps.Take(activation.CurrentStep))
        {
            if (!IsResourceClaimSelectionStep(step) || string.IsNullOrWhiteSpace(step.DeclarationKey)) continue;
            claimed.UnionWith(activation.DeclaredValues.GetValueOrDefault(step.DeclarationKey, []));
        }
        return claimed;
    }

    private bool TryPreparePrideMasterSurchargeCommit(
        L12TriggerCandidate candidate, L12PendingActivation activation)
    {
        candidate.Data.Remove(PrideMasterSurchargeCommitBarrier);
        var source = FindAuthoritativeCard(candidate.SourceInstanceId) ?? candidate.SourceSnapshot
            ?? CreateCard(candidate.SourceCardId, candidate.SourceInstanceId);
        if (!RequiresPrideMasterSurcharge(candidate.Controller, source)
            || candidate.Data.GetValueOrDefault("prideMasterSurchargePrepaid") == "true"
            || activation.DeclaredValues.GetValueOrDefault("mode", []).SingleOrDefault()
                ?.Equals("mode:none", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        var surchargeStepIndex = activation.SelectionSteps.FindIndex(step => step.DeclarationKey?.Equals(
            PrideMasterSurchargeDeclarationKey, StringComparison.OrdinalIgnoreCase) == true);
        var surcharge = activation.DeclaredValues.GetValueOrDefault(PrideMasterSurchargeDeclarationKey, []);
        var priorClaims = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in activation.SelectionSteps.Take(Math.Max(0, surchargeStepIndex)))
        {
            if (!IsResourceClaimSelectionStep(step) || string.IsNullOrWhiteSpace(step.DeclarationKey)) continue;
            priorClaims.UnionWith(activation.DeclaredValues.GetValueOrDefault(step.DeclarationKey, []));
        }

        var ordinaryPayments = DeclaredOrdinaryPaymentResources(activation).ToArray();
        var player = State.Players[candidate.Controller];
        if (surchargeStepIndex < 0 || surcharge.Count != 1 || priorClaims.Contains(surcharge[0])
            || !CanConsumeSelectedResources(player, ordinaryPayments.Length, ordinaryPayments))
        {
            RemoveUnstackedTriggerCandidate(candidate,
                "〈傲慢之罪〉与主宰效果的全部费用无法同时提交；未扣除任何附加费且效果未入栈");
            return false;
        }

        // Public declaration handlers validate and commit their own base cost.  The barrier keeps the
        // completed candidate out of the stack until that succeeds and this reserved surcharge is committed.
        candidate.Data[PrideMasterSurchargeCommitBarrier] = "true";
        return true;
    }

    private IEnumerable<string> DeclaredOrdinaryPaymentResources(L12PendingActivation activation)
    {
        var player = State.Players[activation.Controller];
        foreach (var step in activation.SelectionSteps)
        {
            if (string.IsNullOrWhiteSpace(step.DeclarationKey)) continue;
            var values = activation.DeclaredValues.GetValueOrDefault(step.DeclarationKey, []);
            if (IsOrdinaryPaymentSelectionStep(step))
            {
                foreach (var value in values) yield return value;
                continue;
            }
            if (step.Kind != "mixed-board-payment") continue;
            foreach (var value in values.Where(id => IsOrdinaryResourceIdentity(player, id)))
                yield return value;
        }
    }

    private bool IsOrdinaryResourceIdentity(L12PlayerState player, string id)
        => TryParseTemporaryMoralePaymentChoice(id, out _)
            || player.Morale.Any(card => card.InstanceId.Equals(id, StringComparison.OrdinalIgnoreCase))
            || FindAuthoritativeCard(id)?.CardId == "S01-0212";

    private bool TryCommitPreparedPrideMasterSurcharge(
        L12TriggerCandidate candidate, L12PendingActivation activation)
    {
        if (!candidate.Data.ContainsKey(PrideMasterSurchargeCommitBarrier)) return true;
        var player = State.Players[candidate.Controller];
        var declared = activation.DeclaredValues.GetValueOrDefault(PrideMasterSurchargeDeclarationKey, []);
        var selected = RemapReservedTemporaryMorale(player, declared);
        if (!TryConsumeSelectedResources(player, 1, selected))
        {
            candidate.Data.Remove(PrideMasterSurchargeCommitBarrier);
            RemoveUnstackedTriggerCandidate(candidate,
                "〈傲慢之罪〉附加费的已预留资源异常失效；效果未入栈");
            return false;
        }

        candidate.Data.Remove(PrideMasterSurchargeCommitBarrier);
        candidate.Data["prideMasterSurchargePrepaid"] = "true";
        var source = FindAuthoritativeCard(candidate.SourceInstanceId) ?? candidate.SourceSnapshot
            ?? CreateCard(candidate.SourceCardId, candidate.SourceInstanceId);
        AddEvent("cost", candidate.Controller, "〈傲慢之罪〉使主宰效果额外消耗1士气", source);
        return true;
    }

    private static IReadOnlyCollection<string> RemapReservedTemporaryMorale(
        L12PlayerState player, IReadOnlyCollection<string> declared)
    {
        if (declared.Count != 1 || !TryParseTemporaryMoralePaymentChoice(declared.Single(), out _))
            return declared;
        return TemporaryMoralePaymentChoices(player).Take(1).ToArray();
    }
}
