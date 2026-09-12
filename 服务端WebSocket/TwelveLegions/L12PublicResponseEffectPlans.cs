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
            ["S01-0020"] = "battle-until-dawn",
            ["S01-0120"] = EmptyCityResponsePlan,
            ["S02-0016"] = RuinedRitualResponsePlan,
            ["S02-0017"] = SupplyPlunderResponsePlan,
        };

    private static bool RequiresPublicResponseDeclaration(string cardId)
        => PublicResponsePlans.ContainsKey(cardId);

    private static bool RequiresMoraleResponseCost(string cardId)
        => PublicResponsePlans.GetValueOrDefault(cardId) == EmptyCityResponsePlan;

    private sealed record PublicResponseDeclarationEvaluation(
        string PlanId,
        string AuthorityTargetStackItemId,
        List<L12ActivationSelectionStep> Steps,
        string? UnavailableReason)
    {
        public bool Available => UnavailableReason is null;
    }

    /// <summary>
    /// 反击声明的无副作用权威评估。响应列表、开始声明和最终提交复验必须读取同一份
    /// 候选/模式条件；这里只生成快照，不揭示卡牌、不支付费用也不修改优先权。
    /// </summary>
    private PublicResponseDeclarationEvaluation EvaluatePublicResponseDeclaration(
        int playerIndex, string cardId, L12StackItem target)
    {
        var player = State.Players[playerIndex];
        var timing = ResponseTimingContext(target);
        var affected = State.Players[timing.Controller];
        var steps = new List<L12ActivationSelectionStep>();
        var responsePlan = PublicResponsePlans.GetValueOrDefault(cardId) ?? string.Empty;
        string? unavailableReason = null;

        if (responsePlan == "battle-until-dawn")
        {
            var drawModes = player.Graveyard.Count < 5
                ? new[] { "mode:none" }
                : ["mode:none", "mode:draw"];
            steps.Add(PublicResponseStep("option", "drawMode",
                "战斗至黎明：墓地卡牌数量不少于5张时，选择是否抽取1张牌", drawModes,
                labels: new Dictionary<string, string>
                {
                    ["mode:none"] = "不抽牌",
                    ["mode:draw"] = "抽取1张牌",
                }));
        }
        else if (responsePlan == EmptyCityResponsePlan)
        {
            var returnableMorale = player.Morale.Select(card => card.InstanceId).ToArray();
            if (returnableMorale.Length == 0)
                unavailableReason = "空城计没有可返还的士气，不能发动";
            steps.Add(PublicResponseStep("resource-return", "returnCost",
                "空城计：预先选择返还的1张士气作为发动费用", returnableMorale));
            var drawModes = !player.Field[0].Any(card => card is not null && IsFieldLegion(card))
                ? new[] { "mode:none", "mode:draw" }
                : ["mode:none"];
            steps.Add(PublicResponseStep("option", "drawMode",
                "空城计：我方前排没有军团时，选择是否抽取1张牌", drawModes,
                labels: new Dictionary<string, string>
                {
                    ["mode:none"] = "不抽牌",
                    ["mode:draw"] = "抽取1张牌",
                }));
        }
        else if (responsePlan == RuinedRitualResponsePlan)
        {
            var choices = new List<string>();
            if (affected.Hand.Count > 0) choices.Add("mode:discard");
            var entered = FindOnField(affected, timing.SourceInstanceId, out _, out _);
            if (entered is not null && IsAuthoritativeFieldLegion(entered))
                choices.Add("mode:suppress");
            if (choices.Count == 0)
                unavailableReason = "破败仪式当前没有可弃置手牌或可无效的场上军团，不能发动";
            steps.Add(PublicResponseStep("option", "mode", "破败仪式：选择以下一项", choices,
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
            var handTargets = affected.Hand.Select(card => card.InstanceId).ToArray();
            if (handTargets.Length == 0)
                unavailableReason = "粮草掠夺当前没有可返回牌库的对方手牌，不能发动";
            steps.Add(PublicResponseStep("opponent-hand-anonymous", "handTarget",
                "粮草掠夺：从随机排列的匿名对方手牌中盲选1张返回牌库顶部", handTargets));
        }
        else
        {
            unavailableReason = "响应效果缺少公共声明计划，不能发动";
        }

        return new PublicResponseDeclarationEvaluation(responsePlan, timing.StackItemId, steps,
            unavailableReason);
    }

    private bool HasAvailablePublicResponseDeclaration(int playerIndex, string cardId, L12StackItem target)
        => !RequiresPublicResponseDeclaration(cardId)
            || EvaluatePublicResponseDeclaration(playerIndex, cardId, target).Available;

    private bool TryBeginPublicResponseDeclaration(int playerIndex, L12CardInstance response,
        string targetStackItemId)
    {
        if (!RequiresPublicResponseDeclaration(response.CardId)) return false;
        var target = State.EffectStack.FirstOrDefault(item => item.StackItemId == targetStackItemId);
        if (target is null) return false;
        var evaluation = EvaluatePublicResponseDeclaration(playerIndex, response.CardId, target);
        if (!evaluation.Available)
        {
            AddEvent("ability-rejected", playerIndex, evaluation.UnavailableReason!);
            State.ResponseWindow = new L12ResponseWindow { PriorityPlayer = playerIndex };
            OfferResponse();
            return true;
        }

        var result = BeginPendingActivationSequence(playerIndex, response, "public-response-declaration",
            evaluation.Steps, null, null, targetStackItemId);
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
            || !LegalResponseSources(activation.Controller, target).Contains(response.InstanceId))
        {
            AddEvent("ability-rejected", activation.Controller,
                "响应来源或响应时点已失效，未支付费用且未进入堆叠");
            ResumeResponseAfterCancelledDeclaration(activation);
            return;
        }

        var timing = ResponseTimingContext(target);
        var declared = activation.DeclaredValues;
        var evaluation = EvaluatePublicResponseDeclaration(activation.Controller, response.CardId, target);
        var error = ValidatePublicResponseDeclaration(evaluation, declared);

        if (error is not null)
        {
            AddEvent("ability-rejected", activation.Controller, error);
            ResumeResponseAfterCancelledDeclaration(activation);
            return;
        }

        if (evaluation.PlanId == EmptyCityResponsePlan)
        {
            var cost = declared.GetValueOrDefault("returnCost", []).Single();
            if (!ReturnSelectedMoraleById(player, [cost], 1))
            {
                AddEvent("ability-rejected", activation.Controller,
                    "空城计声明的士气费用已失效，未支付费用且未进入堆叠");
                ResumeResponseAfterCancelledDeclaration(activation);
                return;
            }
        }

        var planId = $"response:{response.CardId}";
        var data = CompositeFirstSegmentData(planId, declared);
        data["affectedPlayer"] = timing.Controller.ToString();
        data["authorityTarget"] = evaluation.AuthorityTargetStackItemId;
        if (response.CardId.StartsWith("S01-", StringComparison.OrdinalIgnoreCase))
            CommitS1ReactionResponse(activation.Controller, response, target.StackItemId, data: data);
        else
            CommitS2CounterResponse(activation.Controller, response, target.StackItemId, data);
    }

    private static string? ValidatePublicResponseDeclaration(PublicResponseDeclarationEvaluation evaluation,
        IReadOnlyDictionary<string, List<string>> declared)
    {
        if (!evaluation.Available) return evaluation.UnavailableReason;
        foreach (var step in evaluation.Steps)
        {
            if (step.RequiredDeclaredChoice is { } requiredChoice
                && !declared.Values.SelectMany(value => value)
                    .Contains(requiredChoice, StringComparer.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(step.DeclarationKey))
                return "响应声明缺少结构化步骤标识，未支付费用且未进入堆叠";
            var selected = declared.GetValueOrDefault(step.DeclarationKey, []);
            if (selected.Count < step.MinChoose || selected.Count > step.MaxChoose
                || selected.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Count
                || selected.Any(choice => !step.ValidChoices.Contains(choice, StringComparer.OrdinalIgnoreCase)))
                return PublicResponseInvalidStepReason(evaluation.PlanId, step.DeclarationKey);
        }
        return null;
    }

    private static string PublicResponseInvalidStepReason(string planId, string declarationKey)
        => (planId, declarationKey) switch
        {
            ("battle-until-dawn", "drawMode") => "战斗至黎明选择的抽牌效果已失效，响应未进入堆叠",
            (EmptyCityResponsePlan, "returnCost") => "空城计声明的士气费用已失效，未支付费用且未进入堆叠",
            (EmptyCityResponsePlan, "drawMode") => "空城计选择的抽牌效果已失效，未支付费用且未进入堆叠",
            (RuinedRitualResponsePlan, "mode") => "破败仪式选择的效果已失效，响应未进入堆叠",
            (RuinedRitualResponsePlan, "handTarget") => "破败仪式声明的匿名手牌已失效，响应未进入堆叠",
            (SupplyPlunderResponsePlan, "handTarget") => "粮草掠夺声明的匿名手牌已失效，响应未进入堆叠",
            _ => "响应声明已失效，未支付费用且未进入堆叠",
        };

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
