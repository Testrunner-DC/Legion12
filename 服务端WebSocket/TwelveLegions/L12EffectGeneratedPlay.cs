namespace TwelveLegions.Server;

/// <summary>
/// 由已开始结算的效果生成的“打出/免费打出”事务。隐藏牌库身份仍由来源效果在
/// 合法结算时展示；一旦玩家选择打出，公开位置与战术声明统一进入 PendingActivation，
/// 实际移区只在提交校验通过后发生。这里是生产代码中唯一允许把这类战术压入 play 栈的入口。
/// </summary>
public sealed partial class L12GameEngine
{
    private const string EffectGeneratedFreePlayAbility = "effect-generated-free-play";

    private IEnumerable<string> EffectGeneratedFreePlaySlots(L12PlayerState player)
    {
        for (var row = 0; row < 2; row++)
        for (var slot = 0; slot < 3; slot++)
        {
            if (State.ActiveDisaster?.CardId == "S01-DS03" && row == 1) continue;
            var occupant = player.Field[row][slot];
            if (occupant is null || row == 1 && IsCounterTactic(occupant.CardId))
                yield return $"{row}:{slot}";
        }
    }

    private CommandResult BeginEffectGeneratedFreePlay(int controller, L12CardInstance card,
        L12StackItem parent, string originZone, string reason)
    {
        var locations = AuthoritativeCardLocations(card.InstanceId);
        if (locations.Count != 1 || locations[0].Host.PlayerIndex != controller
            || locations[0].Zone != originZone || !ReferenceEquals(locations[0].Card, card))
        {
            AddEvent("effect-cancelled", controller, $"{reason}的打出来源已失效；不改选且不移动其他实例");
            FinishStackItem(parent);
            return CommandResult.Ok();
        }

        if (card.CardType == "legion")
        {
            var choices = EffectGeneratedFreePlaySlots(State.Players[controller]).ToArray();
            if (choices.Length == 0)
            {
                AddEvent("effect-cancelled", controller, $"{reason}没有合法登场位置；该卡牌保留在原区域", card);
                FinishStackItem(parent);
                return CommandResult.Ok();
            }
            var result = BeginPendingActivationSequence(controller, card, EffectGeneratedFreePlayAbility,
            [
                new L12ActivationSelectionStep
                {
                    Kind = "slot",
                    DeclarationKey = "entrySlot",
                    Text = $"{reason}：预先声明〈{card.Name}〉无需消耗费用登场的位置",
                    ValidChoices = choices.ToList(),
                    MinChoose = 1,
                    MaxChoose = 1,
                    CancellationPolicy = L12ActivationCancellationPolicy.NotAllowed,
                },
            ], triggerCandidateId: null, playCardInstanceId: card.InstanceId, responseTargetStackItemId: null);
            if (!result.Accepted)
            {
                AddEvent("effect-cancelled", controller, result.Error ?? $"{reason}无法建立免费打出声明", card);
                FinishStackItem(parent);
                return CommandResult.Ok();
            }
            var activation = State.PendingActivations.Last(candidate => candidate.Controller == controller
                && candidate.SourceInstanceId == card.InstanceId
                && candidate.Ability == EffectGeneratedFreePlayAbility);
            activation.CommittedParentStackItemId = parent.StackItemId;
            activation.CommittedOriginZone = originZone;
            activation.CommittedReason = reason;
            return CommandResult.Ok();
        }

        var direct = new L12PendingActivation
        {
            ActivationId = $"activation-{++State.ActivationSequence}",
            Controller = controller,
            SourceInstanceId = card.InstanceId,
            SourceCardId = card.CardId,
            Ability = EffectGeneratedFreePlayAbility,
            Text = $"提交{reason}生成的免费打出",
            ValidChoices = [],
            PlayCardInstanceId = card.InstanceId,
            CommittedParentStackItemId = parent.StackItemId,
            CommittedOriginZone = originZone,
            CommittedReason = reason,
        };
        CompleteEffectGeneratedFreePlay(direct);
        return CommandResult.Ok();
    }

    private void CompleteEffectGeneratedFreePlay(L12PendingActivation activation)
    {
        var parent = FindEffectGeneratedPlayParent(activation);
        if (parent is null) return;
        var player = State.Players[activation.Controller];
        var originZone = activation.CommittedOriginZone ?? "library";
        var reason = activation.CommittedReason ?? "效果";
        var locations = AuthoritativeCardLocations(activation.PlayCardInstanceId ?? string.Empty);
        if (locations.Count != 1 || locations[0].Host.PlayerIndex != activation.Controller
            || locations[0].Zone != originZone || locations[0].Card.CardId != activation.SourceCardId)
        {
            AbortEffectGeneratedFreePlay(activation, $"{reason}的打出来源已失效；不移动其他实例");
            return;
        }

        var location = locations[0];
        var card = location.Card;
        card.OwnerIndex ??= activation.Controller;
        if (card.CardType == "legion")
        {
            var declaredSlot = activation.DeclaredValues.GetValueOrDefault("entrySlot", []).SingleOrDefault()
                ?? activation.DeclaredTargets.SingleOrDefault();
            if (declaredSlot is null || !EffectGeneratedFreePlaySlots(player)
                    .Contains(declaredSlot, StringComparer.OrdinalIgnoreCase))
            {
                AbortEffectGeneratedFreePlay(activation,
                    $"{reason}声明的登场位置已失效；不覆盖、不改选，卡牌保留在原区域");
                return;
            }

            var (row, slot) = ParseSlot(declaredSlot);
            var displacedCounter = player.Field[row][slot];
            if (displacedCounter is not null)
            {
                player.Field[row][slot] = null;
                displacedCounter.Hidden = false;
                ResetCardAfterLeavingField(displacedCounter);
                CardOwner(displacedCounter, player).Graveyard.Add(displacedCounter);
                AddEvent("counter-displaced", activation.Controller,
                    $"{reason}打出军团并将自己覆盖的反击战术〈{displacedCounter.Name}〉置入墓地", displacedCounter);
            }
            if (!TrySummonFromAnyPrivateZone(player, activation.Controller, card.InstanceId, declaredSlot, tapped: false))
            {
                AbortEffectGeneratedFreePlay(activation, $"{reason}的登场事务失效；未生成重复实例");
                return;
            }
            AddEvent("play", activation.Controller, $"{reason}使〈{card.Name}〉无需消耗费用打出", card);
            ResolveOnPlayContinuousEffects(activation.Controller, card);
            RecalculateContinuousTroops();
            FinishStackItem(parent);
            return;
        }

        if (!RemoveAuthoritativeLocation(location))
        {
            AbortEffectGeneratedFreePlay(activation, $"{reason}的来源区域在提交时失效；未移动卡牌");
            return;
        }

        card.SummonRound = State.Round;
        if (card.CardType == "artifact")
        {
            if (card.Name.Contains("卡诺匹斯", StringComparison.Ordinal) && player.Relic is not null)
                player.ExtraRelics.Add(card);
            else
            {
                if (player.Relic is not null)
                {
                    DiscardRelic(player, player.Relic);
                    AddEvent("leave", activation.Controller, "原圣物离开圣物区");
                }
                player.Relic = card;
            }
            ApplyDisasterLevelOnEntry(activation.Controller, card, deferTriggerUntilStackSettles: true);
            AddEvent("play", activation.Controller, $"{reason}使〈{card.Name}〉无需消耗费用打出", card);
            ResolveOnPlayContinuousEffects(activation.Controller, card);
            RecalculateContinuousTroops();
            if (HasImmediateEffect(card, "enter"))
                QueueOrPushTriggeredEffect(activation.Controller, card, "enter", "【登场时】效果");
            FinishStackItem(parent);
            return;
        }

        if (card.CardType != "tactic" || IsCounterTactic(card.CardId))
        {
            // 当前已核准的李牧/冲田路径不会命中此分支；保持真实实例在可追溯的墓地，
            // 不把不合法的反击战术伪装成主动战术压入堆叠。
            ResetCardAfterLeavingField(card);
            player.Graveyard.Add(card);
            AbortEffectGeneratedFreePlay(activation, $"{reason}不能在当前时点打出该类型卡牌");
            return;
        }

        player.Resolving.Add(card);
        player.LastActiveTacticCardId = card.CardId;
        player.LastActiveTacticTurnSerial = State.TurnSerial;
        ApplyDisasterLevelOnEntry(activation.Controller, card, deferTriggerUntilStackSettles: true);
        AddEvent("play", activation.Controller, $"{reason}使〈{card.Name}〉无需消耗费用打出", card);
        ResolveOnPlayContinuousEffects(activation.Controller, card);
        RecalculateContinuousTroops();
        if (!HasImmediateEffect(card, "play"))
        {
            player.Resolving.Remove(card);
            ResetCardAfterLeavingField(card);
            player.Graveyard.Add(card);
            FinishStackItem(parent);
            return;
        }
        if (L12CompositeEffectPlans.HasHandPlayPlan(card.CardId))
        {
            var result = BeginCommittedCompositeEffectDeclaration(activation.Controller, card, parent, "finish-parent");
            if (!result.Accepted)
            {
                player.Resolving.Remove(card);
                ResetCardAfterLeavingField(card);
                player.Graveyard.Add(card);
                AddEvent("ability-rejected", activation.Controller, result.Error ?? "复合战术无法建立声明", card);
                FinishStackItem(parent);
            }
            return;
        }
        PushEffect(activation.Controller, card, "play", $"由{reason}打出的战术效果",
            data: new Dictionary<string, string>
            {
                ["effectGeneratedPlay"] = "free",
                ["originZone"] = originZone,
            });
        FinishStackItem(parent);
    }

    private L12StackItem? FindEffectGeneratedPlayParent(L12PendingActivation activation)
        => State.EffectStack.FirstOrDefault(item => item.StackItemId == activation.CommittedParentStackItemId);

    private void AbortEffectGeneratedFreePlay(L12PendingActivation activation, string reason)
    {
        AddEvent("effect-cancelled", activation.Controller, reason);
        if (FindEffectGeneratedPlayParent(activation) is { } parent) FinishStackItem(parent);
    }
}
