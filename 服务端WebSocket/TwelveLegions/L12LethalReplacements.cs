namespace TwelveLegions.Server;

/// <summary>
/// “即将阵亡”替代的公共声明与代替牌事务。受保护军团从不先离场再复活；
/// 场上代替牌承接原致命阵亡动作，手牌代替牌则执行卡面写明的手牌弃置动作。
/// </summary>
public sealed partial class L12GameEngine
{
    private const string DeclineLethalSubstitution = "decline";

    private string CardLethalSubstitutionKey(L12CardInstance protectedCard)
        => $"lethal-substitution:{protectedCard.CardId}:{protectedCard.InstanceId}:{State.TurnSerial}";

    private string PendingCardLethalSubstitutionKey(L12CardInstance protectedCard)
        => $"pending:{CardLethalSubstitutionKey(protectedCard)}";

    private string CurrentLethalEventProtectionKey(L12CardInstance protectedCard)
        => $"lethal-event-protected:{protectedCard.InstanceId}:{State.Revision}";

    private string? CardLethalSubstitutionKind(L12PlayerState controller, L12CardInstance protectedCard)
    {
        if (protectedCard.CardId == "S01-0205") return "horemheb-field";
        if (protectedCard.CardId == "S02-0515"
            && FindOnField(controller, protectedCard.InstanceId, out var row, out _) is not null
            && row == 0) return "helen-hand";
        if (State.ActivePlayer != controller.PlayerIndex
            && L12StructuredCardRules.HasFaction(controller, protectedCard, "gaotianyuan")
            && !L12StructuredCardRules.IsStarterKondoReplacementSource(protectedCard.CardId)
            && PublicLegions(controller).Any(card => card.InstanceId != protectedCard.InstanceId
                && L12StructuredCardRules.IsStarterKondoReplacementSource(card.CardId)))
            return "kondo-field-discard";
        return null;
    }

    private L12CardInstance[] CardLethalSubstitutionCandidates(
        L12PlayerState controller, L12CardInstance protectedCard, string kind)
        => kind switch
        {
            "horemheb-field" => PublicLegions(controller)
                .Where(card => card.CardId == "S01-0212" && card.InstanceId != protectedCard.InstanceId)
                .ToArray(),
            "helen-hand" => controller.Hand
                .Where(card => card.CardType == "legion" && card.CardId != "S02-0515")
                .ToArray(),
            "kondo-field-discard" => PublicLegions(controller)
                .Where(card => card.InstanceId != protectedCard.InstanceId
                    && L12StructuredCardRules.IsStarterKondoReplacementSource(card.CardId))
                .ToArray(),
            _ => [],
        };

    private bool TryOfferCardLethalSubstitution(L12PlayerState controller, L12CardInstance protectedCard,
        string continuation, string reason)
    {
        var kind = CardLethalSubstitutionKind(controller, protectedCard);
        if (kind is null || controller.UsedAbilities.Contains(CardLethalSubstitutionKey(protectedCard)))
            return false;
        if (controller.UsedAbilities.Contains(PendingCardLethalSubstitutionKey(protectedCard)))
            return true;
        var candidates = CardLethalSubstitutionCandidates(controller, protectedCard, kind);
        if (candidates.Length == 0) return false;

        controller.UsedAbilities.Add(PendingCardLethalSubstitutionKey(protectedCard));
        var choices = candidates.Select(card => card.InstanceId).Append(DeclineLethalSubstitution).ToArray();
        var data = new Dictionary<string, string>
        {
            ["lethalEventId"] = Guid.NewGuid().ToString("N"),
            ["replacementKind"] = kind,
            ["cardInstanceId"] = protectedCard.InstanceId,
            ["reason"] = reason,
            [DeclineLethalSubstitution] = "不发动",
        };
        foreach (var candidate in candidates) data[candidate.InstanceId] = candidate.Name;
        var promptText = kind switch
        {
            "horemheb-field" => $"〈{protectedCard.Name}〉即将阵亡，选择我方1张〈陵墓守卫〉代替承受，或不发动",
            "kondo-field-discard" => $"〈{protectedCard.Name}〉即将阵亡，是否弃置我方〈近藤勇〉代替承受？",
            _ => $"〈{protectedCard.Name}〉即将阵亡，弃置手牌中1张其他军团代替承受，或不发动",
        };
        CreatePrompt(controller.PlayerIndex, "option", promptText,
            choices, 1, 1, continuation, isPrivate: kind == "helen-hand", data: data);
        return true;
    }

    private bool ResolveEffectCardLethalSubstitution(
        L12PlayerState controller, L12CardInstance protectedCard, L12Prompt prompt, string choice)
    {
        controller.UsedAbilities.Remove(PendingCardLethalSubstitutionKey(protectedCard));
        if (choice == DeclineLethalSubstitution) return false;
        var kind = prompt.Data.GetValueOrDefault("replacementKind");
        if (!TryApplyCardLethalSubstitution(controller, protectedCard, kind, choice,
                prompt.Data.GetValueOrDefault("reason", "阵亡"), deferFieldDeath: false, out var defeatedInstanceId))
            return false;
        if (defeatedInstanceId is not null)
            ResolveAttachedCardLethalKillSources(prompt, defeatedInstanceId);
        controller.UsedAbilities.Add(CardLethalSubstitutionKey(protectedCard));
        controller.UsedAbilities.Add(CurrentLethalEventProtectionKey(protectedCard));
        AddEvent("replacement-transaction", controller.PlayerIndex,
            $"致死事件 {prompt.Data.GetValueOrDefault("lethalEventId", "legacy")} 已由替代效果消费；同一事件不会再次结算〈{protectedCard.Name}〉",
            protectedCard);
        return true;
    }

    private void AttachPendingStateBasedKillSourcesToCardLethalSubstitution(L12CardInstance protectedCard)
    {
        foreach (var pending in _pendingKillSourceEvents
                     .Where(entry => entry.Event.TargetInstanceIds.Contains(protectedCard.InstanceId,
                         StringComparer.OrdinalIgnoreCase)))
            AttachCardLethalKillSource(protectedCard, pending.Event);
    }

    private void AttachCardLethalKillSource(L12CardInstance protectedCard, L12KillSourceEvent killEvent)
    {
        var prompt = State.PendingPrompts.LastOrDefault(candidate =>
            candidate.Continuation == "effect-lethal-replacement"
            && candidate.Data.GetValueOrDefault("cardInstanceId") == protectedCard.InstanceId
            && candidate.Data.ContainsKey("replacementKind"));
        if (prompt is null) return;
        var count = int.TryParse(prompt.Data.GetValueOrDefault("lethalKillSourceCount"), out var parsed)
            ? parsed : 0;
        for (var index = 0; index < count; index++)
            if (prompt.Data.GetValueOrDefault($"lethalKillSource:{index}:eventId") == killEvent.EventId) return;
        var prefix = $"lethalKillSource:{count}:";
        prompt.Data[$"{prefix}eventId"] = killEvent.EventId;
        prompt.Data[$"{prefix}kind"] = killEvent.Kind.ToString();
        prompt.Data[$"{prefix}sourceController"] = killEvent.SourceController.ToString();
        prompt.Data[$"{prefix}sourceInstanceId"] = killEvent.SourceInstanceId;
        prompt.Data[$"{prefix}sourceCardId"] = killEvent.SourceCardId;
        prompt.Data[$"{prefix}printed"] = killEvent.TriggersPrintedKillTiming.ToString();
        prompt.Data[$"{prefix}caused"] = killEvent.CausedBySourceCard.ToString();
        prompt.Data["lethalKillSourceCount"] = (count + 1).ToString();
    }

    private void ResolveAttachedCardLethalKillSources(L12Prompt prompt, string defeatedInstanceId)
    {
        var count = int.TryParse(prompt.Data.GetValueOrDefault("lethalKillSourceCount"), out var parsed)
            ? parsed : 0;
        for (var index = 0; index < count; index++)
        {
            var prefix = $"lethalKillSource:{index}:";
            if (!Enum.TryParse<L12KillSourceKind>(prompt.Data.GetValueOrDefault($"{prefix}kind"), out var kind)
                || !int.TryParse(prompt.Data.GetValueOrDefault($"{prefix}sourceController"), out var sourceController)
                || prompt.Data.GetValueOrDefault($"{prefix}eventId") is not { Length: > 0 } eventId
                || prompt.Data.GetValueOrDefault($"{prefix}sourceInstanceId") is not { Length: > 0 } sourceInstanceId
                || prompt.Data.GetValueOrDefault($"{prefix}sourceCardId") is not { Length: > 0 } sourceCardId)
                continue;
            _ = bool.TryParse(prompt.Data.GetValueOrDefault($"{prefix}printed"), out var printed);
            _ = bool.TryParse(prompt.Data.GetValueOrDefault($"{prefix}caused"), out var caused);
            ResolveTypedKillSourceEvent(new L12KillSourceEvent(eventId, kind, sourceController,
                sourceInstanceId, sourceCardId, printed, caused, [defeatedInstanceId]));
        }
    }

    private bool ResolveCombatCardLethalSubstitution(L12PlayerState controller,
        L12CardInstance protectedCard, L12PendingDefense pending, L12Prompt prompt, string choice)
    {
        controller.UsedAbilities.Remove(PendingCardLethalSubstitutionKey(protectedCard));
        if (choice == DeclineLethalSubstitution) return false;
        var kind = prompt.Data.GetValueOrDefault("replacementKind");
        if (kind is null || !CardLethalSubstitutionCandidates(controller, protectedCard, kind)
                .Any(card => card.InstanceId == choice)) return false;
        pending.LethalReplacementSubstitutes[protectedCard.InstanceId] = choice;
        controller.UsedAbilities.Add(CardLethalSubstitutionKey(protectedCard));
        return true;
    }

    private bool TryApplyCardLethalSubstitution(L12PlayerState controller, L12CardInstance protectedCard,
        string? kind, string substituteId, string reason, bool deferFieldDeath, out string? defeatedInstanceId)
    {
        defeatedInstanceId = null;
        if (kind == "horemheb-field")
        {
            var substitute = FindOnField(controller, substituteId, out _, out _);
            if (substitute is null || substitute.CardId != "S01-0212") return false;
            var removed = RemoveFromField(controller, substitute, true,
                $"代替〈{protectedCard.Name}〉承受{reason}", queueDeathTrigger: !deferFieldDeath,
                leaveKind: L12FieldLeaveKind.Defeat, bypassLethalReplacement: true,
                deferGraveyard: deferFieldDeath);
            if (removed) defeatedInstanceId = substitute.InstanceId;
            AddEvent("replacement", controller.PlayerIndex,
                $"〈{substitute.Name}〉代替〈{protectedCard.Name}〉承受原致命结果，并沿原阵亡动作进入所有者目的区",
                protectedCard, substitute);
            return true;
        }

        if (kind == "helen-hand")
        {
            var substitute = controller.Hand.FirstOrDefault(card => card.InstanceId == substituteId
                && card.CardType == "legion" && card.CardId != "S02-0515");
            if (substitute is null) return false;
            controller.Hand.Remove(substitute);
            var owner = CardOwner(substitute, controller);
            owner.Graveyard.Add(substitute);
            AddEvent("discard", controller.PlayerIndex,
                $"〈{protectedCard.Name}〉弃置手牌中的〈{substitute.Name}〉代替承受致命结果", substitute);
            NotifyCardDiscarded(controller, substitute, "hand", causedByEffect: true);
            AddEvent("replacement", controller.PlayerIndex,
                $"〈{substitute.Name}〉按卡面从手牌弃置并进入所有者墓地，代替〈{protectedCard.Name}〉承受致命结果",
                protectedCard, substitute);
            return true;
        }

        if (kind == "kondo-field-discard")
        {
            var substitute = FindOnField(controller, substituteId, out _, out _);
            if (substitute is null || substitute.InstanceId == protectedCard.InstanceId
                || !L12StructuredCardRules.IsStarterKondoReplacementSource(substitute.CardId)) return false;
            if (!RemoveFromField(controller, substitute, true,
                    $"作为费用弃置，代替〈{protectedCard.Name}〉承受{reason}",
                    queueDeathTrigger: !deferFieldDeath, leaveKind: L12FieldLeaveKind.Discard,
                    bypassLethalReplacement: true)) return false;
            AddEvent("replacement", controller.PlayerIndex,
                $"〈{substitute.Name}〉作为费用弃置，代替〈{protectedCard.Name}〉承受原致命结果",
                protectedCard, substitute);
            return true;
        }

        return false;
    }
}
