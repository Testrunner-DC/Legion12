namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    private L12StackItem QueueAuthorityEvent(
        string type,
        int actorPlayer,
        L12CardInstance source,
        string text,
        int? subjectPlayer = null,
        string? targetInstanceId = null,
        string? originZone = null,
        string? destinationZone = null,
        bool causedByEffect = false,
        IReadOnlyDictionary<string, string>? data = null,
        bool publicSource = true)
    {
        var authorityEvent = new L12AuthorityEvent
        {
            EventId = $"authority-{++State.AuthorityEventSequence}",
            Type = type,
            ActorPlayer = actorPlayer,
            SubjectPlayer = subjectPlayer,
            SourceInstanceId = source.InstanceId,
            TargetInstanceId = targetInstanceId,
            OriginZone = originZone,
            DestinationZone = destinationZone,
            CausedByEffect = causedByEffect,
        };
        if (data is not null)
            foreach (var pair in data) authorityEvent.Data[pair.Key] = pair.Value;
        var origin = State.IsResolvingStack ? State.EffectStack.LastOrDefault() : null;
        if (origin is not null) authorityEvent.Data["originStackId"] = origin.StackItemId;
        State.AuthorityEvents.Add(authorityEvent);

        var item = new L12StackItem
        {
            StackItemId = $"stack-{++State.StackSequence}",
            Controller = actorPlayer,
            SourceInstanceId = type == "effect-hand-add" ? origin?.SourceInstanceId ?? string.Empty : source.InstanceId,
            SourceCardId = type == "effect-hand-add" ? origin?.SourceCardId ?? string.Empty : source.CardId,
            SourceName = type == "effect-hand-add" ? origin?.SourceName ?? "加入手牌事件" : source.Name,
            Trigger = "authority-event",
            Text = type == "effect-hand-add" && origin is not null
                ? $"〈{origin.SourceName}〉使{State.Players[actorPlayer].Name}因效果将1张牌加入手牌"
                : text,
        };
        item.Data["eventId"] = authorityEvent.EventId;
        item.Data["eventType"] = type;
        item.Data["actorPlayer"] = actorPlayer.ToString();
        if (subjectPlayer is not null) item.Data["subjectPlayer"] = subjectPlayer.Value.ToString();
        if (targetInstanceId is not null) item.Data["targetInstanceId"] = targetInstanceId;
        if (originZone is not null) item.Data["originZone"] = originZone;
        if (destinationZone is not null) item.Data["destinationZone"] = destinationZone;
        item.Data["causedByEffect"] = causedByEffect ? "true" : "false";
        foreach (var pair in authorityEvent.Data) item.Data[pair.Key] = pair.Value;
        if (type == "effect-ready" && !string.IsNullOrWhiteSpace(targetInstanceId)
            && State.Players.Any(player => FindOnField(player, targetInstanceId, out _, out _) is not null))
        {
            SetResponsePresentationTargets(item.Data, [targetInstanceId]);
        }
        if (publicSource) FreezeAndRecordPublicResponseTargets(item, source);

        if (State.IsResolvingStack)
        {
            State.DeferredEffectStack.Add(item);
            if (publicSource) AddEvent("authority-event", actorPlayer, $"{text}已登记，将在当前堆叠关闭后处理", source);
            else AddEvent("authority-event", actorPlayer, $"{State.Players[actorPlayer].Name}因效果将1张牌加入手牌，等待当前堆叠关闭后处理");
        }
        else
        {
            State.EffectStack.Add(item);
            if (publicSource) AddEvent("authority-event", actorPlayer, $"{text}进入响应时点", source);
            else AddEvent("authority-event", actorPlayer, $"{State.Players[actorPlayer].Name}因效果将1张牌加入手牌，进入响应时点");
            BeginResponseWindow(item);
        }
        return item;
    }

    private L12AuthorityEvent? FindAuthorityEvent(L12StackItem item)
        => State.AuthorityEvents.FirstOrDefault(candidate => candidate.EventId == item.Data.GetValueOrDefault("eventId"));

    private bool DeclareEffectBlock(L12StackItem item, L12StackItem attackRoot)
    {
        var pending = State.PendingDefense;
        if (attackRoot.Trigger != "opponent-attack" || pending is null) return false;
        var attacker = FindOnField(State.Players[pending.AttackerPlayer], pending.AttackerInstanceId, out _, out _);
        var targetExists = pending.Target.Type == "master"
            || FindOnField(State.Players[1 - pending.AttackerPlayer], pending.Target.InstanceId, out _, out _) is not null;
        if (attacker is null || !targetExists
            || attackRoot.Controller != pending.AttackerPlayer
            || attackRoot.SourceInstanceId != pending.AttackerInstanceId) return false;
        // 旧V2检查点可能没有CombatId。只能在仍持有原进攻根项时补写一次稳定身份；
        // 不允许等到后续权威事件结算时借用“当前”交战，从而误绑下一次进攻。
        if (string.IsNullOrWhiteSpace(pending.CombatId))
            pending.CombatId = $"combat-legacy-{attackRoot.StackItemId}";
        item.Data["effectBlock"] = "true";
        item.Data["effectBlockCombatId"] = pending.CombatId;
        item.Data["effectBlockAttackStackItemId"] = attackRoot.StackItemId;
        item.Data["effectBlockAttackerPlayer"] = pending.AttackerPlayer.ToString();
        item.Data["effectBlockAttackerInstanceId"] = pending.AttackerInstanceId;
        item.Data["effectBlockTargetType"] = pending.Target.Type;
        item.Data["effectBlockTargetInstanceId"] = pending.Target.InstanceId ?? string.Empty;
        return true;
    }

    private void QueueEffectBlockAuthorityEvent(L12StackItem item, L12CardInstance? source)
    {
        if (item.Trigger == "authority-event" || item.Negated || source is null
            || item.Data.GetValueOrDefault("effectBlock") != "true"
            || item.Data.GetValueOrDefault("effectBlockAuthorityQueued") == "true") return;
        item.Data["effectBlockAuthorityQueued"] = "true";
        var data = new Dictionary<string, string>
        {
            ["action"] = "block",
            ["effectBlock"] = "true",
            ["effectBlockCombatId"] = item.Data["effectBlockCombatId"],
            ["effectBlockAttackStackItemId"] = item.Data["effectBlockAttackStackItemId"],
            ["effectBlockAttackerPlayer"] = item.Data["effectBlockAttackerPlayer"],
            ["effectBlockAttackerInstanceId"] = item.Data["effectBlockAttackerInstanceId"],
            ["effectBlockTargetType"] = item.Data["effectBlockTargetType"],
            ["effectBlockTargetInstanceId"] = item.Data["effectBlockTargetInstanceId"],
        };
        QueueAuthorityEvent("defense", item.Controller, source,
            $"{source.Name}声明抵挡", subjectPlayer: item.Controller,
            targetInstanceId: item.Data.GetValueOrDefault("effectBlockTargetInstanceId"),
            causedByEffect: true, data: data);
    }

    private bool EffectBlockContextMatches(
        L12StackItem item,
        L12AuthorityEvent authorityEvent,
        L12PendingDefense? pending,
        L12CardInstance? attacker)
    {
        if (pending is null || attacker is null) return false;
        static bool SameBoundValue(L12StackItem stackItem, L12AuthorityEvent eventData, string key)
            => !string.IsNullOrWhiteSpace(stackItem.Data.GetValueOrDefault(key))
                && stackItem.Data.GetValueOrDefault(key) == eventData.Data.GetValueOrDefault(key);
        if (!SameBoundValue(item, authorityEvent, "effectBlockCombatId")
            || !SameBoundValue(item, authorityEvent, "effectBlockAttackStackItemId")
            || !SameBoundValue(item, authorityEvent, "effectBlockAttackerPlayer")
            || !SameBoundValue(item, authorityEvent, "effectBlockAttackerInstanceId")
            || !SameBoundValue(item, authorityEvent, "effectBlockTargetType")) return false;
        if (item.Data.GetValueOrDefault("effectBlockTargetInstanceId")
            != authorityEvent.Data.GetValueOrDefault("effectBlockTargetInstanceId")) return false;
        return item.Data["effectBlockCombatId"] == pending.CombatId
            && int.TryParse(item.Data["effectBlockAttackerPlayer"], out var attackerPlayer)
            && attackerPlayer == pending.AttackerPlayer
            && item.Data["effectBlockAttackerInstanceId"] == pending.AttackerInstanceId
            && item.Data["effectBlockTargetType"] == pending.Target.Type
            && item.Data["effectBlockTargetInstanceId"] == (pending.Target.InstanceId ?? string.Empty);
    }

    private void ResolveEffectBlockAuthority(L12StackItem item, L12AuthorityEvent authorityEvent)
    {
        var pending = State.PendingDefense;
        if (item.Data.GetValueOrDefault("invalid") == "true" || pending is null) return;
        pending.BlockedByResponse = true;
        var source = FindSource(item) ?? item.SourceSnapshot;
        AddPlayerCombatEvent("defense", authorityEvent.ActorPlayer,
            $"〈{item.SourceName}〉抵挡本次进攻",
            new(pending.CombatId, "defense", "blocked"),
            source is null ? [] : [source]);
        AddEvent("combat-stage", authorityEvent.ActorPlayer,
            $"〈{item.SourceName}〉抵挡本次进攻；已结算的进攻时效果不回退");
    }

    private void ResolveAuthorityEvent(L12StackItem item)
    {
        var authorityEvent = FindAuthorityEvent(item);
        if (authorityEvent is null) { FinishStackItem(item); return; }

        switch (authorityEvent.Type)
        {
            case "defense":
            {
                var blockIds = item.Data.GetValueOrDefault("blockIds", string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
                var supportIds = item.Data.GetValueOrDefault("supportIds")?.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    ?? (string.IsNullOrWhiteSpace(item.Data.GetValueOrDefault("supportId"))
                        ? [] : [item.Data.GetValueOrDefault("supportId")!]);
                var pending = State.PendingDefense;
                var attacker = pending is null ? null : FindOnField(State.Players[pending.AttackerPlayer],
                    pending.AttackerInstanceId, out _, out _);
                var effectBlock = item.Data.GetValueOrDefault("effectBlock") == "true";
                if (item.Data.GetValueOrDefault("invalid") != "true")
                {
                    if (pending is null || attacker is null
                        || effectBlock && !EffectBlockContextMatches(item, authorityEvent, pending, attacker))
                    {
                        item.Data["invalid"] = "true";
                        AddPlayerCombatEvent("defense-invalid", authorityEvent.ActorPlayer,
                            "本次进攻已结束，不再支付额外费用",
                            new(pending?.CombatId, "defense-invalid",
                                supportIds.Length > 0 ? "invalid-support" : "invalid-block", "context-unavailable"));
                    }
                    else if (!effectBlock)
                    {
                        var validation = ValidateDefenseChoice(authorityEvent.ActorPlayer, pending, attacker, blockIds, supportIds);
                        if (!validation.Accepted)
                        {
                            item.Data["invalid"] = "true";
                            AddPlayerCombatEvent("defense-invalid", authorityEvent.ActorPlayer,
                                "本次抵挡或支援已无法继续，未支付额外费用",
                                new(pending.CombatId, "defense-invalid",
                                    supportIds.Length > 0 ? "invalid-support" : "invalid-block", "choice-unavailable"));
                        }
                    }
                }
                if (BeginRequiredDefenseExtraDiscard(item)) return;
                if (effectBlock)
                    ResolveEffectBlockAuthority(item, authorityEvent);
                else
                    ResolveDefenseCore(
                        authorityEvent.ActorPlayer,
                        blockIds,
                        supportIds,
                        item.Data.GetValueOrDefault("invalid") == "true");
                break;
            }
            case "non-hand-entry":
            {
                var card = FindOnField(State.Players[authorityEvent.ActorPlayer], authorityEvent.SourceInstanceId, out _, out _);
                if (card is not null && item.Data.GetValueOrDefault("suppressEnter") != "true")
                {
                    var hasPrintedEntry = HasImmediateEffect(card, "enter");
                    var thorCandidate = BuildThorGrantedEntryChargeCandidate(authorityEvent.ActorPlayer, card);
                    if (hasPrintedEntry && thorCandidate is null)
                    {
                        QueueOrPushTriggeredEffect(authorityEvent.ActorPlayer, card, "enter", "【登场时】效果");
                        break;
                    }
                    var candidates = new List<L12TriggerCandidate>();
                    if (hasPrintedEntry)
                        candidates.Add(CreateTriggerCandidate(authorityEvent.ActorPlayer, card, "enter", "【登场时】效果"));
                    if (thorCandidate is not null)
                        candidates.Add(thorCandidate);
                    if (candidates.Count > 0) QueueTriggerCandidates(candidates);
                }
                break;
            }
            case "effect-ready":
                CommitEffectReady(authorityEvent, item);
                break;
            case "effect-hand-add":
                // 加入手牌本身已经由 LibraryOps/区域操作提交；此事件只提供统一响应时点。
                break;
        }
        authorityEvent.Resolved = true;
        FinishStackItem(item);
    }

    private bool BeginRequiredDefenseExtraDiscard(L12StackItem item)
    {
        if (item.Data.GetValueOrDefault("richardExtraResolved") == "true"
            || item.Data.GetValueOrDefault("invalid") == "true"
            || State.PendingDefense?.RichardDefenseTaxActive != true) return false;
        var hasDeclaredDefense = item.Data.GetValueOrDefault("action") is "block" or "support"
            && (item.Data.GetValueOrDefault("effectBlock") == "true"
                || !string.IsNullOrWhiteSpace(item.Data.GetValueOrDefault("blockIds"))
                || !string.IsNullOrWhiteSpace(item.Data.GetValueOrDefault("supportIds"))
                || !string.IsNullOrWhiteSpace(item.Data.GetValueOrDefault("supportId")));
        if (!hasDeclaredDefense) return false;

        var excluded = item.Data.GetValueOrDefault("blockIds", string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var defender = State.Players[item.Controller];
        var choices = defender.Hand.Where(card => !excluded.Contains(card.InstanceId)).Select(card => card.InstanceId).ToList();
        if (choices.Count == 0)
        {
            item.Data["richardExtraResolved"] = "true";
            item.Data["invalid"] = "true";
            AddPlayerCombatEvent("defense", item.Controller,
                "没有手牌可支付〈狮心王理查一世〉要求的额外弃牌费用，本次抵挡/支援无效",
                new(State.PendingDefense?.CombatId, "defense-invalid",
                    item.Data.GetValueOrDefault("action") == "support" ? "invalid-support" : "invalid-block",
                    "extra-cost-unpaid"));
            return false;
        }
        choices.Add("decline");
        CreatePrompt(item.Controller, "discard-or-decline", "狮心王理查一世：额外弃置1张手牌，否则本次抵挡/支援无效",
            choices, 1, 1, "card-effect", item.StackItemId, isPrivate: true,
            data: new Dictionary<string, string>
            {
                ["action"] = "s2-richard-defense-extra-discard", ["choiceMode"] = "instant",
                ["decline"] = "不弃置，本次抵挡/支援无效",
            });
        return true;
    }

    private void CommitEffectReady(L12AuthorityEvent authorityEvent, L12StackItem item)
    {
        var player = State.Players[authorityEvent.ActorPlayer];
        var zone = authorityEvent.OriginZone;
        // Legacy checkpoints lack a zone; newly queued events bind the original zone.
        var card = zone is null or "field"
            ? FindOnField(player, authorityEvent.TargetInstanceId, out _, out _) : null;
        if (zone is null or "relic")
            card ??= (player.Relic?.InstanceId == authorityEvent.TargetInstanceId ? player.Relic : null)
                ?? player.ExtraRelics.FirstOrDefault(candidate => candidate.InstanceId == authorityEvent.TargetInstanceId);
        var morale = zone is null or "morale"
            ? player.Morale.FirstOrDefault(candidate => candidate.InstanceId == authorityEvent.TargetInstanceId) : null;
        if (card is null && morale is null)
        {
            RecordTargetSettlementFailure(item, authorityEvent.TargetInstanceId,
                "转为活跃的目标已离开原区域");
            return;
        }
        if (card is { Tapped: false } || morale is { Tapped: false })
        {
            RecordTargetSettlementFailure(item, authorityEvent.TargetInstanceId,
                "转为活跃的目标已不再为休整状态");
            return;
        }
        if (card is not null && !CanReadyCardByEffect(card))
        {
            RecordTargetSettlementFailure(item, authorityEvent.TargetInstanceId,
                $"{card.Name}本回合无法因效果转为活跃");
            return;
        }
        if (card is not null) card.Tapped = false;
        if (morale is not null) morale.Tapped = false;
        if (card is not null && item.Data.GetValueOrDefault("lockTrialCardUntilTurnEnd") == "true")
            player.UsedAbilities.Add($"trial-card-lock:{card.InstanceId}:{State.TurnSerial}");
        AddEvent("effect", authorityEvent.ActorPlayer, item.Text,
            card is null ? [] : [card]);
    }

    private void QueueNonHandEntry(int playerIndex, L12CardInstance card, string originZone)
    {
        QueueAuthorityEvent("non-hand-entry", playerIndex, card,
            $"{card.Name}从{ZoneLabel(originZone)}登场", subjectPlayer: playerIndex,
            targetInstanceId: card.InstanceId, originZone: originZone, destinationZone: "field", causedByEffect: true);
        QueueS2GrailRoundTableEntry(playerIndex, card);
    }

    /// <summary>
    /// 效果使军团登场后的统一出口。来源区域只决定是否还需要建立“非手牌登场”
    /// 权威事件；军团本身的【登场时】与公共登场观察者都不得因效果来源而遗漏。
    /// </summary>
    private void CompleteEffectLegionEntry(int playerIndex, L12CardInstance card, string originZone)
    {
        ApplyDisasterLevelOnEntry(playerIndex, card, deferTriggerUntilStackSettles: true);
        if (!originZone.Equals("hand", StringComparison.OrdinalIgnoreCase))
        {
            QueueNonHandEntry(playerIndex, card, originZone);
            return;
        }

        var hasPrintedEntry = HasImmediateEffect(card, "enter");
        var thorCandidate = BuildThorGrantedEntryChargeCandidate(playerIndex, card);
        var grailCandidate = BuildS2GrailRoundTableEntryCandidate(playerIndex, card);
        if (hasPrintedEntry && thorCandidate is null && grailCandidate is null)
        {
            QueueOrPushTriggeredEffect(playerIndex, card, "enter", "【登场时】效果");
            return;
        }
        var candidates = new List<L12TriggerCandidate>();
        if (hasPrintedEntry)
            candidates.Add(CreateTriggerCandidate(playerIndex, card, "enter", "【登场时】效果"));
        if (thorCandidate is not null)
            candidates.Add(thorCandidate);
        if (grailCandidate is not null)
            candidates.Add(grailCandidate);
        if (candidates.Count > 0) QueueTriggerCandidates(candidates);
    }

    private bool CanReadyCardByEffect(L12CardInstance target)
        => target.Tapped && target.CannotReadyByEffectUntilTurn < State.TurnSerial;

    private L12StackItem? ReadyCardByEffect(int playerIndex, L12CardInstance source, L12CardInstance target, string reason,
        L12StackItem? resultOwner = null)
    {
        if (!target.Tapped)
        {
            if (resultOwner is not null)
                RecordTargetSettlementFailure(resultOwner, target.InstanceId, "转为活跃的目标已不再为休整状态");
            return null;
        }
        if (!CanReadyCardByEffect(target))
        {
            var failure = $"{target.Name}本回合无法因效果转为活跃";
            if (resultOwner is not null) RecordTargetSettlementFailure(resultOwner, target.InstanceId, failure);
            else AddEvent("effect-failed", playerIndex, failure, source, target);
            return null;
        }
        var player = State.Players[playerIndex];
        var originZone = ReferenceEquals(FindOnField(player, target.InstanceId, out _, out _), target)
            ? "field" : ReferenceEquals(player.Relic, target) || player.ExtraRelics.Contains(target)
                ? "relic" : "unavailable";
        var readyItem = QueueAuthorityEvent("effect-ready", playerIndex, source, reason, subjectPlayer: playerIndex,
            targetInstanceId: target.InstanceId, originZone: originZone, causedByEffect: true);
        if (resultOwner is null) return readyItem;

        // A single-segment ready effect is not complete until its independent authority event
        // survives responses and revalidates the target. Transfer that segment's presentation
        // ownership to the authority item so logs never publish an early "resolved" result and
        // then a contradictory failure when the target changed during the second window.
        foreach (var key in new[] { "presentationSceneId", "presentationFlow", "triggerEffectText" })
            if (resultOwner.Data.GetValueOrDefault(key) is { Length: > 0 } value)
                readyItem.Data[key] = value;
        resultOwner.Data["effectResultPublished"] = "true";
        return readyItem;
    }

    private void ReadyMoraleByEffect(int playerIndex, L12CardInstance source, L12MoraleCard target, string reason)
    {
        if (!target.Tapped) return;
        if (source.CardType == "master" && PublicLegions(State.Players[playerIndex]).Any(card => card.CardId == "S02-0401"))
        {
            AddEvent("effect-prevented", playerIndex, "武田信玄使我方士气无法因主宰效果转为活跃", source);
            return;
        }
        QueueAuthorityEvent("effect-ready", playerIndex, source, reason, subjectPlayer: playerIndex,
            targetInstanceId: target.InstanceId, originZone: "morale", causedByEffect: true);
    }

    private void NotifyCardAddedToHandByEffect(L12PlayerState player, L12CardInstance card, string originZone, string reason)
    {
        // 裁定（2026-09-23）：天灾导致的回手/抽牌不产生入手响应窗。
        if (IsDisasterAuthorityActive()) return;
        QueueAuthorityEvent("effect-hand-add", player.PlayerIndex, card, $"{player.Name}因效果将1张牌加入手牌", subjectPlayer: player.PlayerIndex,
            targetInstanceId: card.InstanceId, originZone: originZone, destinationZone: "hand", causedByEffect: true,
            publicSource: false);
    }

    private void AddCardToHandByEffect(L12PlayerState player, L12CardInstance card, string originZone, string reason)
    {
        ResetCardForPrivateZone(card);
        player.Hand.Add(card);
        TrackCardFact("search-or-hand-add", player.PlayerIndex, card, originZone, "hand");
        NotifyCardAddedToHandByEffect(player, card, originZone, reason);
    }

    private void PubliclyRevealThenAddCardToHandByEffect(L12PlayerState player, L12CardInstance card,
        string originZone, string revealText, string handAddReason, string producerCardId,
        string presentationSceneKey, IReadOnlyDictionary<string, string>? presentationValues = null)
    {
        presentationValues ??= new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cardName"] = card.Name,
        };
        AddPresentationEvent("reveal", player.PlayerIndex, revealText, producerCardId,
            presentationSceneKey, presentationValues, card);
        AddCardToHandByEffect(player, card, originZone, handAddReason);
    }

    /// <summary>
    /// 复合流程中的公开区域/条件检索统一出口。公开对象复用当前效果段的呈现场景
    /// 形成 reveal 权威事件，再进入 effect-hand-add；前端、回放与后台共用同一事件链。
    /// </summary>
    private void PubliclyRevealThenAddCardToHandByEffect(L12PlayerState player, L12CardInstance card,
        string originZone, string revealText, string handAddReason, L12StackItem item)
    {
        var source = FindSource(item) ?? item.SourceSnapshot;
        var sceneId = item.Data.GetValueOrDefault("presentationSceneId");
        if (string.IsNullOrWhiteSpace(sceneId) && source is not null)
            sceneId = ResolveEffectPresentationSceneId(source, item.Trigger, item.Data, item.Text);
        AddPresentationEventByProducerIdWithPlayerLog("reveal", player.PlayerIndex, revealText,
            item.SourceCardId, sceneId, null, card);
        AddCardToHandByEffect(player, card, originZone, handAddReason);
    }

    /// <summary>
    /// 对象已由上一步权威 reveal 公开时只收敛入手出口，避免重复卡面动画。
    /// </summary>
    private void AddPreviouslyRevealedCardToHandByEffect(L12PlayerState player, L12CardInstance card,
        string originZone, string handAddReason)
        => AddCardToHandByEffect(player, card, originZone, handAddReason);

    private bool MoveLibraryCardToHandByEffect(L12PlayerState player, string instanceId, string reason)
    {
        var index = player.Library.FindIndex(card => card.InstanceId == instanceId);
        if (index < 0) return false;
        var card = player.Library[index];
        player.Library.RemoveAt(index);
        AddCardToHandByEffect(player, card, "library", reason);
        return true;
    }

    private bool PubliclyRevealThenMoveLibraryCardToHandByEffect(L12PlayerState player,
        string instanceId, string revealText, string handAddReason, string producerCardId,
        string presentationSceneKey, IReadOnlyDictionary<string, string>? presentationValues = null)
    {
        var card = player.Library.FirstOrDefault(candidate => candidate.InstanceId == instanceId);
        if (card is null) return false;
        presentationValues ??= new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cardName"] = card.Name,
        };
        AddPresentationEvent("reveal", player.PlayerIndex, revealText, producerCardId,
            presentationSceneKey, presentationValues, card);
        return MoveLibraryCardToHandByEffect(player, instanceId, handAddReason);
    }

    private static string ZoneLabel(string zone) => zone switch
    {
        "library" => "牌库",
        "graveyard" => "墓地",
        "removed" => "移出区",
        "morale" => "士气区",
        _ => zone,
    };
}
