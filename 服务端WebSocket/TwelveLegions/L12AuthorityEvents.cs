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
        State.AuthorityEvents.Add(authorityEvent);

        var item = new L12StackItem
        {
            StackItemId = $"stack-{++State.StackSequence}",
            Controller = actorPlayer,
            SourceInstanceId = source.InstanceId,
            SourceCardId = source.CardId,
            SourceName = source.Name,
            Trigger = "authority-event",
            Text = text,
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

    private void ResolveAuthorityEvent(L12StackItem item)
    {
        var authorityEvent = FindAuthorityEvent(item);
        if (authorityEvent is null) { FinishStackItem(item); return; }

        switch (authorityEvent.Type)
        {
            case "defense":
                if (BeginRequiredDefenseExtraDiscard(item)) return;
                ResolveDefenseCore(
                    authorityEvent.ActorPlayer,
                    item.Data.GetValueOrDefault("blockIds", string.Empty)
                        .Split('|', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    item.Data.GetValueOrDefault("supportId"),
                    item.Data.GetValueOrDefault("invalid") == "true");
                break;
            case "non-hand-entry":
            {
                var card = FindOnField(State.Players[authorityEvent.ActorPlayer], authorityEvent.SourceInstanceId, out _, out _);
                if (card is not null && item.Data.GetValueOrDefault("suppressEnter") != "true"
                    && HasImmediateEffect(card, "enter"))
                    PushEffect(authorityEvent.ActorPlayer, card, "enter", "【登场时】效果");
                break;
            }
            case "effect-ready":
                CommitEffectReady(authorityEvent);
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
            || item.SourceCardId != "S02-0608") return false;
        var hasDeclaredDefense = item.Data.GetValueOrDefault("action") is "block" or "support"
            && (!string.IsNullOrWhiteSpace(item.Data.GetValueOrDefault("blockIds"))
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
            AddEvent("defense", item.Controller, "没有手牌可支付〈狮心王理查一世〉要求的额外弃牌费用，本次抵挡/支援无效");
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

    private void CommitEffectReady(L12AuthorityEvent authorityEvent)
    {
        var player = State.Players[authorityEvent.ActorPlayer];
        var card = FindOnField(player, authorityEvent.TargetInstanceId, out _, out _)
            ?? (player.Relic?.InstanceId == authorityEvent.TargetInstanceId ? player.Relic : null)
            ?? player.ExtraRelics.FirstOrDefault(candidate => candidate.InstanceId == authorityEvent.TargetInstanceId);
        if (card is not null) card.Tapped = false;
        var morale = player.Morale.FirstOrDefault(candidate => candidate.InstanceId == authorityEvent.TargetInstanceId);
        if (morale is not null) morale.Tapped = false;
    }

    private void QueueNonHandEntry(int playerIndex, L12CardInstance card, string originZone)
    {
        QueueAuthorityEvent("non-hand-entry", playerIndex, card,
            $"{card.Name}从{ZoneLabel(originZone)}登场", subjectPlayer: playerIndex,
            targetInstanceId: card.InstanceId, originZone: originZone, destinationZone: "field", causedByEffect: true);
    }

    private void ReadyCardByEffect(int playerIndex, L12CardInstance source, L12CardInstance target, string reason)
    {
        if (!target.Tapped) return;
        QueueAuthorityEvent("effect-ready", playerIndex, source, reason, subjectPlayer: playerIndex,
            targetInstanceId: target.InstanceId, causedByEffect: true);
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
            targetInstanceId: target.InstanceId, causedByEffect: true);
    }

    private void NotifyCardAddedToHandByEffect(L12PlayerState player, L12CardInstance card, string originZone, string reason)
    {
        QueueAuthorityEvent("effect-hand-add", player.PlayerIndex, card, $"{player.Name}因效果将1张牌加入手牌", subjectPlayer: player.PlayerIndex,
            targetInstanceId: card.InstanceId, originZone: originZone, destinationZone: "hand", causedByEffect: true,
            publicSource: false);
    }

    private void AddCardToHandByEffect(L12PlayerState player, L12CardInstance card, string originZone, string reason)
    {
        player.Hand.Add(card);
        NotifyCardAddedToHandByEffect(player, card, originZone, reason);
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
