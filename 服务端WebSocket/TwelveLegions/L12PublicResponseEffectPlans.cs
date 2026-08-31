namespace TwelveLegions.Server;

/// <summary>
/// 直接响应卡的公共前置声明入口。响应卡在费用、公开模式、公开目标或匿名手牌槽位
/// 全部声明并再次验证之前保持盖伏，不揭示、不支付且不进入堆叠。
/// </summary>
public sealed partial class L12GameEngine
{
    private const string EmptyCityResponsePlan = "empty-city";
    private const string RuinedRitualResponsePlan = "ruined-ritual";
    private const string SupplyPlunderResponsePlan = "supply-plunder";
    private static readonly IReadOnlyDictionary<string, string> PublicResponsePlans =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S01-0120"] = EmptyCityResponsePlan,
            ["S02-0016"] = RuinedRitualResponsePlan,
            ["S02-0017"] = SupplyPlunderResponsePlan,
        };

    private static bool RequiresPublicResponseDeclaration(string cardId)
        => PublicResponsePlans.ContainsKey(cardId);

    private static bool RequiresMoraleResponseCost(string cardId)
        => PublicResponsePlans.GetValueOrDefault(cardId) == EmptyCityResponsePlan;

    private bool TryBeginPublicResponseDeclaration(int playerIndex, L12CardInstance response,
        string targetStackItemId)
    {
        if (!RequiresPublicResponseDeclaration(response.CardId)) return false;
        var player = State.Players[playerIndex];
        var target = State.EffectStack.FirstOrDefault(item => item.StackItemId == targetStackItemId);
        if (target is null) return false;
        var timing = ResponseTimingContext(target);
        var affected = State.Players[timing.Controller];
        var steps = new List<L12ActivationSelectionStep>();
        var responsePlan = PublicResponsePlans.GetValueOrDefault(response.CardId);

        if (responsePlan == EmptyCityResponsePlan)
        {
            steps.Add(PublicResponseStep("resource-return", "returnCost",
                "空城计：预先选择返还的1张士气作为发动费用",
                player.Morale.Select(card => card.InstanceId)));
        }
        else if (responsePlan == RuinedRitualResponsePlan)
        {
            var choices = new List<string>();
            if (affected.Hand.Count > 0) choices.Add("mode:discard");
            if (FindOnField(affected, timing.SourceInstanceId, out _, out _) is not null)
                choices.Add("mode:suppress");
            steps.Add(PublicResponseStep("option", "mode", "破败仪式：预先声明结算模式", choices,
                labels: new Dictionary<string, string>
                {
                    ["mode:discard"] = "盲选并弃置对方1张手牌",
                    ["mode:suppress"] = "令该军团登场效果无效且本回合兵力-3000",
                }));
            steps.Add(PublicResponseStep("opponent-hand-anonymous", "handTarget",
                "破败仪式：从随机排列的匿名对方手牌中盲选1张弃置",
                affected.Hand.Select(card => card.InstanceId), requiredChoice: "mode:discard"));
        }
        else if (responsePlan == SupplyPlunderResponsePlan)
        {
            steps.Add(PublicResponseStep("opponent-hand-anonymous", "handTarget",
                "粮草掠夺：从随机排列的匿名对方手牌中盲选1张返回牌库顶部",
                affected.Hand.Select(card => card.InstanceId)));
        }

        var result = BeginPendingActivationSequence(playerIndex, response, "public-response-declaration",
            steps, null, null, targetStackItemId);
        if (!result.Accepted)
        {
            State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = playerIndex };
            OfferResponse();
        }
        return true;
    }

    private static L12ActivationSelectionStep PublicResponseStep(string kind, string key, string text,
        IEnumerable<string> choices, string? requiredChoice = null,
        Dictionary<string, string>? labels = null)
        => new()
        {
            Kind = kind,
            DeclarationKey = key,
            Text = text,
            ValidChoices = choices.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MinChoose = 1,
            MaxChoose = 1,
            RequiredDeclaredChoice = requiredChoice,
            ChoiceLabels = labels ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

    private void CompletePublicResponseDeclaration(L12PendingActivation activation)
    {
        var player = State.Players[activation.Controller];
        var response = FindOnField(player, activation.SourceInstanceId, out _, out _);
        var target = State.EffectStack.FirstOrDefault(item => item.StackItemId == activation.ResponseTargetStackItemId);
        if (response is null || target is null
            || response.CardId != activation.SourceCardId
            || !(CanUseS1ReactionAtStack(response.CardId, activation.Controller, target)
                || CanUseS2CounterAtStack(response.CardId, activation.Controller, target)))
        {
            AddEvent("ability-rejected", activation.Controller,
                "响应来源或响应时点已失效，未支付费用且未进入堆叠");
            ResumeResponseAfterCancelledDeclaration(activation);
            return;
        }

        var timing = ResponseTimingContext(target);
        var affected = State.Players[timing.Controller];
        var declared = activation.DeclaredValues;
        var responsePlan = PublicResponsePlans.GetValueOrDefault(response.CardId);
        string? error = null;
        if (responsePlan == EmptyCityResponsePlan)
        {
            var cost = declared.GetValueOrDefault("returnCost", []).SingleOrDefault();
            if (cost is null || !CanReturnSelectedMoraleById(player, [cost], 1))
                error = "空城计声明的士气费用已失效，未支付费用且未进入堆叠";
            else
                _ = ReturnSelectedMoraleById(player, [cost], 1);
        }
        else if (responsePlan == RuinedRitualResponsePlan)
        {
            var mode = declared.GetValueOrDefault("mode", []).SingleOrDefault();
            var handTarget = declared.GetValueOrDefault("handTarget", []).SingleOrDefault();
            if (mode == "mode:discard" && (handTarget is null
                    || !affected.Hand.Any(card => card.InstanceId == handTarget)))
                error = "破败仪式声明的匿名手牌已失效，响应未进入堆叠";
            else if (mode == "mode:suppress"
                && FindOnField(affected, timing.SourceInstanceId, out _, out _) is null)
                error = "破败仪式声明的登场军团已失效，响应未进入堆叠";
            else if (mode is not ("mode:discard" or "mode:suppress"))
                error = "破败仪式声明模式无效，响应未进入堆叠";
        }
        else if (responsePlan == SupplyPlunderResponsePlan)
        {
            var handTarget = declared.GetValueOrDefault("handTarget", []).SingleOrDefault();
            if (handTarget is null || !affected.Hand.Any(card => card.InstanceId == handTarget))
                error = "粮草掠夺声明的匿名手牌已失效，响应未进入堆叠";
        }

        if (error is not null)
        {
            AddEvent("ability-rejected", activation.Controller, error);
            ResumeResponseAfterCancelledDeclaration(activation);
            return;
        }

        var planId = $"response:{response.CardId}";
        var data = CompositeFirstSegmentData(planId, declared);
        data["affectedPlayer"] = timing.Controller.ToString();
        data["authorityTarget"] = timing.StackItemId;
        if (response.CardId.StartsWith("S01-", StringComparison.OrdinalIgnoreCase))
            CommitS1ReactionResponse(activation.Controller, response, target.StackItemId, data: data);
        else
            CommitS2CounterResponse(activation.Controller, response, target.StackItemId, data);
    }

    private Dictionary<string, string> DirectPublicResponseData(L12CardInstance response, L12StackItem target)
    {
        var timing = ResponseTimingContext(target);
        var data = CompositeFirstSegmentData($"response:{response.CardId}",
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase));
        data["affectedPlayer"] = timing.Controller.ToString();
        data["authorityTarget"] = timing.StackItemId;
        return data;
    }
}
