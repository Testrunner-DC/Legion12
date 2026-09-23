using System.Reflection;
using TwelveLegions.Server;

namespace TwelveLegions.Tests;

// Audit metadata only: these bindings do not execute effects or create new atoms.
// Bind exact reviewed ability identities; a new structure must be reviewed again.
internal sealed record L12LifecycleProfile(string Id, IReadOnlyDictionary<string, string> RuntimeOwners,
    IReadOnlyDictionary<string, string> NotApplicable)
{
    public IReadOnlyList<string> AdditionalChecks { get; init; } = [];
}

internal static class EffectLifecycleProfiles
{
    // 〈沙漠君临〉的手牌军团必须在声明时明确选择；结算不能替换成另一张候选。
    internal const string DesertHandSummonAbilityId = "S02-0207:ability:play:528a4430c4b87fb5";

    private static readonly L12LifecycleProfile DesertHandSummon = new("composite:desert-hand-summon",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["candidate-generation"] = "IsDesertHandSummonCandidate",
            ["cost-commit"] = "TryCommitCompositePreStackCosts",
            ["settlement-revalidation"] = "TryResolveS2FactionTactic",
            ["presentation"] = "ResolveEffectPresentationSceneId",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["no-target"] = "本效果必须先声明1张合格手牌军团；不存在候选时不能发动，且尚未提交弃置费用。",
        }) { AdditionalChecks = ["cost-prepaid", "settlement-slot-invalidated", "single-candidate-choice"] };

    internal static readonly string[] CounterDeploymentAbilityIds =
    [
        "S01-0403:ability:death:c3e5fc27d01fe269",
        "S02-0009:ability:play:ff53cfd909161da1",
    ];

    private static readonly L12LifecycleProfile CounterDeployment = new("composite:counter-deployment",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["candidate-generation"] = "IsCounterDeploymentCandidate",
            ["slot-declaration"] = "CreateActivationStepPrompt",
            ["settlement-revalidation"] = "SetDeclaredCounterTactics",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal))
        { AdditionalChecks = ["private-hand-redaction", "independent-target-settlement", "slot-invalidated"] };

    internal static readonly string[] StrictHandEntryAbilityIds =
    [
        "S01-0105:ability:enter:ee4ec5ee9f9e1cce",
        "S01-0116:ability:static:74c527aaab5e91cd",
        "S01-0213:ability:after-attack:55cfe31dc7ed5969",
    ];

    private static readonly L12LifecycleProfile StrictHandEntry = new("private-zone:strict-hand-entry",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["declaration"] = "CreateActivationStepPrompt",
            ["settlement-revalidation"] = "TrySummonFromHand",
            ["failed-settlement"] = "RecordTargetSettlementFailure",
            ["source-failure"] = "RecordResolutionFailure",
            ["dependent-continuation"] = "QueueNextCompositeSegment",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["replacement"] = "已声明的手牌实例若离开手牌区，结算仅失败；不得从墓地、牌库或其他手牌替代。",
            ["slot-invalidated"] = "已声明位置失效时不得覆盖或改选；本段记录失败。",
        }) { AdditionalChecks = ["private-hand-redaction", "settlement-slot-invalidated", "stale-instance-no-replacement", "then-requires-success"] };

    internal static readonly string[] NativeCavalryAbilityIds =
    [
        "S01-0002:ability:active:2786430f57a9abaa",
        "S01-0106:ability:active:2786430f57a9abaa",
        "S01-0310:ability:active:0a0575206e996652",
        "S01-0409:ability:active:56a01edf47ee1225",
        "S02-0505:ability:active:bac4cb5d348f29f1",
        "ST01-01:ability:active:69626894e55e27e5",
        "ST04-01:ability:active:2786430f57a9abaa",
        "ST06-04:ability:active:719cc1c7c1084fa0",
    ];

    private static readonly L12LifecycleProfile NativeCavalry = new("rule-action:cavalry-move",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["command"] = "CavalryMove",
            ["timing"] = "CavalryMoveTimingUnavailableReason",
            ["source-eligibility"] = "CavalryMoveSourceUnavailableReason",
            ["candidate-generation"] = "CavalryMoveDestinationKeys",
            ["destination-revalidation"] = "IsLegalCavalryMoveDestination",
            ["button"] = "BuildRuleActionViews",
            ["presentation"] = "NativeCavalryMovePresentation",
            ["movement-event"] = "RecordLegionMovement",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "原生职介位移是立即执行的规则动作，不创建可响应或无效的效果堆叠；后续位移触发另行验收。",
            ["target-invalidated"] = "无入栈后目标窗口；改以提交时来源/目的地复验覆盖过期客户端选择。",
            ["payment-cancel"] = "没有卡牌或资源费用，也无支付Prompt；未提交目的地不产生动作。",
            ["multi-target-applicability"] = "一次只移动来源军团到一个空位，不存在独立多目标结算。",
        }) { AdditionalChecks = ["source-invalidated", "destination-invalidated", "single-candidate-choice"] };

    internal static readonly string[] PrintedRangedAbilityIds =
    [
        "S01-0003:ability:static:e3471cd2a7042e59",
        "S01-0110:ability:static:e3471cd2a7042e59",
        "S01-0111:ability:static:e3471cd2a7042e59",
        "S01-0112:ability:static:e3471cd2a7042e59",
        "S01-0113:ability:static:e3471cd2a7042e59",
        "S01-0114:ability:static:e3471cd2a7042e59",
        "S01-0115:ability:static:9ba2f4f5354a2a05",
        "S01-0116:ability:static:e3471cd2a7042e59",
        "S01-0208:ability:static:e3471cd2a7042e59",
        "S01-0209:ability:static:e3471cd2a7042e59",
        "S01-0210:ability:static:e3471cd2a7042e59",
        "S01-0211:ability:static:e3471cd2a7042e59",
        "S01-0213:ability:static:9ba2f4f5354a2a05",
        "S01-0214:ability:static:e3471cd2a7042e59",
        "S01-0309:ability:static:e3471cd2a7042e59",
        "S01-0313:ability:static:e3471cd2a7042e59",
        "S01-0314:ability:static:e3471cd2a7042e59",
        "S01-0316:ability:static:9ba2f4f5354a2a05",
        "S01-0409:ability:static:6c03e83e9e18abb1",
        "S01-0410:ability:static:e3471cd2a7042e59",
        "S01-0411:ability:static:e3471cd2a7042e59",
        "S01-0413:ability:static:e3471cd2a7042e59",
        "S01-0415:ability:static:9ba2f4f5354a2a05",
        "S01-0416:ability:static:e3471cd2a7042e59",
        "S02-0003:ability:continuous:e9823ffd970d6ce6",
        "S02-0204:ability:continuous:e9823ffd970d6ce6",
        "S02-0304:ability:continuous:e9823ffd970d6ce6",
        "S02-0507:ability:static:3f520b391281b325",
        "S02-0508:ability:static:aa41bff900061e1d",
        "S02-0513:ability:static:e3471cd2a7042e59",
        "S02-0514:ability:static:e3471cd2a7042e59",
        "S02-0515:ability:static:e3471cd2a7042e59",
        "S02-0517:ability:static:9ba2f4f5354a2a05",
        "S02-0614:ability:continuous:e9823ffd970d6ce6",
        "S02-0617:ability:continuous:e9823ffd970d6ce6",
        "S02-0618:ability:continuous:e9823ffd970d6ce6",
        "S02-0619:ability:continuous:f0839056592c5ee2",
        "ST01-07:ability:static:e3471cd2a7042e59",
        "ST01-08:ability:static:9ba2f4f5354a2a05",
        "ST01-09:ability:static:e3471cd2a7042e59",
        "ST02-08:ability:static:e3471cd2a7042e59",
        "ST03-05:ability:static:efd7771da618f0ac",
        "ST04-07:ability:static:e3471cd2a7042e59",
        "ST05-03:ability:continuous:3119db9911c31cf3",
        "ST05-04:ability:static:e3471cd2a7042e59",
        "ST05-08:ability:static:e3471cd2a7042e59",
        "ST05-09:ability:static:e3471cd2a7042e59",
    ];

    private static readonly L12LifecycleProfile PrintedRanged = new("continuous:printed-range",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["definition"] = "L12StructuredCardRules.GetCombatRuleAbilities",
            ["condition-and-permission"] = "L12StructuredCardRules.CombatProfile",
            ["source-row"] = "CanAttackFromRow",
            ["candidate-generation"] = "BuildLegalAttackTargets",
            ["target-revalidation"] = "TryValidateAttackTarget",
            ["combat-declaration"] = "Attack",
            ["damage-settlement"] = "ResolveDefenseCore",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "该持续能力没有独立入栈、支付或响应窗口，不能作为堆叠效果单独无效；对进攻事件的无效仍属战斗动作验收。",
            ["payment-cancel"] = "本段没有费用或支付Prompt；相邻付费扩展射程是另一段，不继承此豁免。",
            ["duplicate-submit"] = "本段没有发动命令；重复读取条件/候选须无副作用，进攻命令重复提交仍由共用战斗协议验收。",
            ["single-candidate-choice"] = "本段不创建对象选择Prompt；玩家主动提交进攻目标由进攻规则处理，不能自动替玩家进攻。",
            ["multi-target-applicability"] = "本段仅提供来源军团的持续进攻权限，不独立结算多个对象。",
        }) { AdditionalChecks = ["source-row-change", "attack-preview", "ranged-no-loss", "profession-grant"] };

    internal static readonly string[] PaidExtendedRangeAbilityIds =
    [
        "S01-0003:ability:active:73c59f9367069790",
        "S01-0113:ability:active:e1b5cdab435b4c1f",
    ];

    private static readonly L12LifecycleProfile PaidExtendedRange = new("active:paid-extended-range",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["definition"] = "L12StructuredCardSemantics.ExtendedRangeRule",
            ["activation-eligibility"] = "ExtendedRangeSourceUnavailableReason",
            ["cost-commit"] = "TryCommitS1ExtendedActiveAbility",
            ["response-stack"] = "PushEffect",
            ["settlement"] = "TryResolveS1ExtendedActive",
            ["attack-candidates"] = "BuildLegalAttackTargets",
            ["attack-revalidation"] = "TryValidateAttackTarget",
            ["expiry"] = "ResetTemporaryCardState",
            ["presentation"] = "ResolveEffectPresentationSceneId",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["no-target"] = "本效果不选择进攻对象，只赋予来源本回合的进攻权限；即使当前没有对方对象也可支付并发动。",
            ["target-invalidated"] = "本效果入栈时不声明进攻对象；实际进攻另由公共战斗入口按当时状态生成并复验目标。",
            ["single-candidate-choice"] = "没有效果目标选择Prompt；玩家之后主动提交具体进攻目标。",
            ["multi-target-applicability"] = "一次结算只更新来源军团的权限，不同时处理多个进攻对象。",
        }) { AdditionalChecks = ["source-invalidated", "payment-cancel", "paid-cost-preserved", "repeat-activation", "turn-end-expiry", "authoritative-attack"] };

    // 27个印刷能力段映射为31个运行能力；安卡神碑、八尺琼勾玉和匠神锻造炉的
    // 分支共用同一印刷段。这里只归属公共主动休整Cost，不冒充逐卡后段已使用同一结算器。
    internal static readonly string[] ActiveRestAbilityIds =
    [
        "S01-0105:ability:active:0e81cd47a6221fd8",
        "S01-0109:ability:active:88c64e7a7e50fb25",
        "S01-0117:ability:active:ba48403c4da1e24c",
        "S01-01D1:ability:active:2b7ae6d9b09b600b",
        "S01-0214:ability:active:30e47404439f2371",
        "S01-0215:ability:active:6984859bdd4fa8b1",
        "S01-0317:ability:active:90c21e26f3d58b69",
        "S01-03D1:ability:active:79829ccbe13dcca0",
        "S01-04D1:ability:active:67457fb394219836",
        "S02-0003:ability:active:484fb98a6af8df3f",
        "S02-0104:ability:active:1687d445c6acc308",
        "S02-0204:ability:active:4257a82eec559a94",
        "S02-0205:ability:active:8023ed21f8771697",
        "S02-0404:ability:active:b30de444d37a3b6e",
        "S02-0510:ability:active:2ee4c7f29b568e48",
        "S02-0513:ability:active:0b4d5245336709f8",
        "S02-0520:ability:active:e4e320d416a9c103",
        "S02-05D1:ability:active:f160e84288ecb28c",
        "S02-0603:ability:active:8768d3f1fcb44728",
        "S02-0616:ability:active:3616b237df312569",
        "S02-06D1:ability:active:30a9d18991dc8481",
        "ST02-05:ability:active:80aa98cc24ef764e",
        "ST03-05:ability:active:87d142bd0e12a218",
        "ST03-07:ability:active:0d4ebc1a2ab8b128",
        "ST04-06:ability:active:8f6b1b9dfc246e36",
        "ST05-06:ability:active:cc5d71f55d3a253f",
        "ST06-09:ability:active:e533dbf15f08cea0",
    ];

    private static readonly L12LifecycleProfile ActiveRest = new("cost:active-rest",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["runtime-identity"] = "L12StructuredCardRules.IsActiveRestAbility",
            ["button-eligibility"] = "BuildAbilityViews",
            ["cost-commit"] = "CommitStructuredActiveRestCost",
            ["cost-presentation"] = "AddActivePaidCostPresentation",
            ["response-stack"] = "PushEffect",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["no-target"] = "本档案只验收主动休整的共用Cost；各父能力是否需要目标及无目标时能否发动由对应效果档案验收。",
            ["target-invalidated"] = "来源休整在入栈前已经支付；效果对象逆结算失效由父能力档案验收，不改变共用Cost。",
            ["payment-cancel"] = "对象或分支选择取消发生在PushEffect共用Cost边界之前，由父能力声明档案验收；未进入本边界即不得休整来源。",
            ["single-candidate-choice"] = "主动休整Cost本身不选择效果对象；唯一候选仍选择属于父能力声明协议。",
            ["multi-target-applicability"] = "主动休整Cost只改变能力来源状态，不处理父能力的多个效果对象。",
        })
        { AdditionalChecks = ["active-rest-cost", "paid-cost-preserved", "readied-source-reuse", "runtime-branch-mapping"] };

    internal static readonly string[] SelfDamageEntryDiscountAbilityIds =
    [
        "S01-0303:ability:hand-play:5e06807975eda2b7",
        "S01-0304:ability:hand-play:5e06807975eda2b7",
        "S01-0308:ability:hand-play:5e06807975eda2b7",
        "S01-0310:ability:hand-play:5e06807975eda2b7",
        "S01-0314:ability:hand-play:5e06807975eda2b7",
        "S02-0303:ability:hand-play:5e06807975eda2b7",
    ];

    private static readonly L12LifecycleProfile SelfDamageEntryDiscount = new("hand-play:self-damage-entry-discount",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["definition"] = "L12StructuredCardRules.SelfDamageEntryDiscount",
            ["declaration-and-choice"] = "PlayCard",
            ["cost-calculation"] = "GetPlayCostWithSigurdDiscount",
            ["resource-payment"] = "EnsurePlayResourcePaymentChoice",
            ["self-damage-payment"] = "PayMasterDamageCostAndCanContinue",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["no-target"] = "该手牌费用能力不选择效果对象；合法位置和打出资源属于打出动作本身。",
            ["negated"] = "冒号前自伤只改变本次打出费用，不生成独立可响应效果；支付最后1血会立即判败并终止打出。",
            ["target-invalidated"] = "没有效果目标；提交时只复验手牌实例、位置与实际支付资源。",
            ["multi-target-applicability"] = "一次只修改当前手牌军团的本次打出费用。",
        }) { AdditionalChecks = ["optional-choice", "payment-cancel", "last-health-terminal", "reconnect-payment", "card-remains-on-lethal-cost"] };

    internal static readonly IReadOnlyDictionary<string, string[]> CombatKeywordDefinitionAbilityIds =
        new SortedDictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["taunt"] =
            [
                "S02-0302:ability:keyword-definition:eaba79729a9d7a65",
                "S02-0503:ability:keyword-definition:6692b63a59c971d0",
                "S02-0512:ability:keyword-definition:6692b63a59c971d0",
                "S02-0615:ability:keyword-definition:8a4c9aff096f6526",
                "ST01-04:ability:keyword-definition:c24a6b9d8de8435a",
                "ST02-02:ability:keyword-definition:c24a6b9d8de8435a",
                "ST04-01:ability:keyword-definition:c24a6b9d8de8435a",
                "ST06-02:ability:keyword-definition:c24a6b9d8de8435a",
            ],
            ["charge"] =
            [
                "S02-0505:ability:keyword-definition:cf232142ca7d10f9",
                "S02-0602:ability:keyword-definition:beff9037e2c10a9d",
                "S02-0612:ability:keyword-definition:beff9037e2c10a9d",
            ],
            ["shock"] =
            [
                "S02-0511:ability:keyword-definition:96aa4e9504b12339",
                "S02-05M1:ability:keyword-definition:41657ed47ef085ae",
            ],
            ["strong-attack"] =
            [
                "S02-05M1:ability:keyword-definition:995c52041c470ca4",
                "S02-0605:ability:keyword-definition:60bccaeb6d982ea8",
            ],
            ["piercing"] =
            [
                "S02-0606:ability:keyword-definition:672734be0285300f",
                "S02-0611:ability:keyword-definition:672734be0285300f",
            ],
            ["death-immunity"] =
            [
                "S02-0608:ability:keyword-definition:4d1e472a814a1e0b",
                "S02-0611:ability:keyword-definition:4d1e472a814a1e0b",
            ],
        };

    private static readonly IReadOnlyDictionary<string, L12LifecycleProfile> CombatKeywordProfiles =
        new SortedDictionary<string, L12LifecycleProfile>(StringComparer.Ordinal)
        {
            ["taunt"] = KeywordProfile("taunt",
                ("active-state", "L12StructuredCardRules.HasTaunt"),
                ("attack-candidates", "BuildLegalAttackTargets"),
                ("attack-revalidation", "TryValidateAttackTarget"),
                ("presentation", "BuildActiveKeywords")),
            ["charge"] = KeywordProfile("charge",
                ("attack-candidates", "BuildLegalAttackTargets"),
                ("attack-revalidation", "TryValidateAttackTarget"),
                ("presentation", "BuildActiveKeywords"),
                ("leave-reset", "ResetCardAfterLeavingField")),
            ["shock"] = KeywordProfile("shock",
                ("attack-trigger", "ApplyS2Shock"),
                ("combat-settlement", "ResolveDefenseCore"),
                ("presentation", "BuildActiveKeywords"),
                ("turn-expiry", "ResetTemporaryCardState")),
            ["strong-attack"] = KeywordProfile("strong-attack",
                ("active-state", "L12StructuredCardSemantics.HasEffectiveStrongAttack"),
                ("grant", "GrantStrongAttack"),
                ("combat-settlement", "Attack"),
                ("presentation", "BuildActiveKeywords"),
                ("turn-expiry", "ResetTemporaryCardState")),
            ["piercing"] = KeywordProfile("piercing",
                ("printed-identity", "L12StructuredCardRules.HasPrintedKeywordReference"),
                ("kill-fact-gate", "ResolveTypedKillSourceEvent"),
                ("printed-settlement", "TryResolveS2FactionAfterAttack"),
                ("generated-attack", "BeginPiercingAttack"),
                ("master-target-revalidation", "CanAttackMasterTarget"),
                ("combat-settlement", "ResolveDefenseCore")),
            ["death-immunity"] = KeywordProfile("death-immunity",
                ("grant", "GrantImmortalUntilNextTurnStart"),
                ("active-state", "HasActiveImmortal"),
                ("lethal-replacement", "RemoveFromField"),
                ("presentation", "BuildActiveKeywords"),
                ("turn-expiry", "ExpireEffectsAtPlayerTurnStart")),
        };

    private static L12LifecycleProfile KeywordProfile(string keyword,
        params (string Boundary, string Owner)[] runtimeOwners)
    {
        var owners = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["definition"] = "L12StructuredCardRules.HasKeywordDefinition",
        };
        foreach (var (boundary, owner) in runtimeOwners) owners.Add(boundary, owner);
        return new($"keyword:{keyword}", owners,
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["negated"] = "关键词定义不是独立发动的效果；授予它的父能力是否被响应或无效另行验收。",
                ["payment-cancel"] = "关键词定义本身没有费用或支付Prompt；费用属于引用它的父能力。",
                ["no-target"] = "关键词定义只描述共享规则语义，不独立生成对象候选；对象属于授予它的父能力或后续规则动作。",
                ["target-invalidated"] = "关键词定义没有已声明对象；实际进攻、致命替代或状态检查均读取当前实例状态。",
                ["single-candidate-choice"] = "关键词定义不创建玩家对象选择；实际进攻或致命替代使用当时的公共规则候选。",
                ["duplicate-submit"] = "关键词定义没有独立提交命令；重复读取规则语义必须无副作用。",
            }) { AdditionalChecks = ["parent-grant-boundary", "authoritative-consumer", "leave-or-turn-expiry", "reconnect-state"] };
    }

    // granted 形态的关键词定义段：ST 早期结构使用 granted 子能力、S2 使用 keyword-definition，
    // 两种形态承载同一规则语义；授予它的父能力另行验收，本族只绑定定义与共享消费端。
    internal static readonly IReadOnlyDictionary<string, string[]> GrantedKeywordDefinitionAbilityIds =
        new SortedDictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["taunt"] =
            [
                "S02-0004:ability:granted:be3174252606645e",
                "S02-0007:ability:granted:be3174252606645e",
            ],
            ["charge"] =
            [
                "S02-03M1:ability:granted:f4dd24f1fb07f3d5",
                "S02-0403:ability:granted:f4dd24f1fb07f3d5",
                "S02-0405:ability:granted:f4dd24f1fb07f3d5",
                "ST01-01:ability:granted:c502e9ac1489cd1a",
            ],
            ["must-hit"] =
            [
                "S02-0206:ability:granted:1aba3f5bd15a426d",
            ],
            ["death-immunity"] =
            [
                "S02-0404:ability:granted:e3ff02735b6b18f4",
            ],
            ["piercing"] =
            [
                "ST01-01:ability:granted:6ec4b634ed12b206",
            ],
        };

    private static readonly IReadOnlyDictionary<string, L12LifecycleProfile> GrantedKeywordProfiles =
        new SortedDictionary<string, L12LifecycleProfile>(StringComparer.Ordinal)
        {
            ["taunt"] = GrantedKeywordProfile("taunt",
                ("active-state", "L12StructuredCardRules.HasTaunt"),
                ("attack-candidates", "BuildLegalAttackTargets"),
                ("attack-revalidation", "TryValidateAttackTarget"),
                ("presentation", "BuildActiveKeywords")),
            ["charge"] = GrantedKeywordProfile("charge",
                ("attack-candidates", "BuildLegalAttackTargets"),
                ("attack-revalidation", "TryValidateAttackTarget"),
                ("presentation", "BuildActiveKeywords"),
                ("leave-reset", "ResetCardAfterLeavingField")),
            ["must-hit"] = GrantedKeywordProfile("must-hit",
                ("attack-declaration", "Attack"),
                ("defense-submit", "ValidateDefenseChoice"),
                ("presentation", "BuildActiveKeywords"),
                ("leave-reset", "ResetCardAfterLeavingField")),
            ["death-immunity"] = GrantedKeywordProfile("death-immunity",
                ("grant", "GrantImmortalUntilNextTurnStart"),
                ("active-state", "HasActiveImmortal"),
                ("lethal-replacement", "RemoveFromField"),
                ("presentation", "BuildActiveKeywords"),
                ("turn-expiry", "ExpireEffectsAtPlayerTurnStart")),
            ["piercing"] = GrantedKeywordProfile("piercing",
                ("kill-fact-gate", "ResolveTypedKillSourceEvent"),
                ("printed-settlement", "TryResolveS2FactionAfterAttack"),
                ("generated-attack", "BeginPiercingAttack"),
                ("master-target-revalidation", "CanAttackMasterTarget"),
                ("combat-settlement", "ResolveDefenseCore")),
        };

    private static L12LifecycleProfile GrantedKeywordProfile(string keyword,
        params (string Boundary, string Owner)[] runtimeOwners)
    {
        var owners = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["definition"] = "L12StructuredCardRules.HasPrintedKeywordReference",
        };
        foreach (var (boundary, owner) in runtimeOwners) owners.Add(boundary, owner);
        return new($"keyword-granted:{keyword}", owners,
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["negated"] = "授予形态的关键词定义不是独立发动的效果；授予它的父能力是否被响应或无效另行验收。",
                ["payment-cancel"] = "关键词定义本身没有费用或支付Prompt；费用属于授予它的父能力。",
                ["no-target"] = "授予形态的定义段只描述共享关键词，不独立生成对象候选；对象由父能力声明。",
                ["target-invalidated"] = "定义段没有已声明对象；父能力结算及后续规则动作分别按当前实例复验。",
                ["single-candidate-choice"] = "关键词定义不创建玩家对象选择；实际进攻或致命替代使用当时的公共规则候选。",
                ["duplicate-submit"] = "关键词定义没有独立提交命令；重复读取规则语义必须无副作用。",
            }) { AdditionalChecks = ["parent-grant-boundary", "authoritative-consumer", "leave-or-turn-expiry", "reconnect-state"] };
    }

    // 「位于前排」获得【挑衅】：S01 三卡由共享 overlay 模板注入；ST01-04 程咬金经
    // 结构化批4接管后产出同文同原子的段，本档案按文本与原子形状统一绑定。
    // ST02-02/ST04-01/ST06-02 已由结构化组合行（含对方回合兵力）承载，不在本族。
    internal static readonly string[] FrontRowTauntOverlayAbilityIds =
    [
        "S01-0107:ability:static:af427a4637e1c138",
        "S01-0204:ability:static:af427a4637e1c138",
        "S01-0312:ability:static:af427a4637e1c138",
        "ST01-04:ability:static:af427a4637e1c138",
    ];

    private static readonly L12LifecycleProfile FrontRowTauntOverlay = new("continuous:front-row-taunt-overlay",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["definition"] = "L12StructuredCardRules.GetCombatOverlayAbilities",
            ["condition-and-active-state"] = "L12StructuredCardRules.HasTaunt",
            ["attack-candidates"] = "BuildLegalAttackTargets",
            ["attack-revalidation"] = "TryValidateAttackTarget",
            ["master-attack-rule"] = "CanAttackMasterTarget",
            ["presentation"] = "BuildActiveKeywords",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "位置条件的持续关键词授予不独立入栈，不能作为一次效果被无效。",
            ["payment-cancel"] = "本段没有费用或支付Prompt。",
            ["no-target"] = "本段不选择效果对象，只按来源当前行位决定是否授予挑衅。",
            ["target-invalidated"] = "无效果目标；进攻候选与提交复验每次都读取当前行位与关键词状态。",
            ["duplicate-submit"] = "持续授予读取无副作用；重复进攻提交仍由公共战斗协议拒绝。",
        })
    {
        AdditionalChecks = ["row-condition-current", "authoritative-consumer", "closed-overlay-card-set"],
    };

    // “试炼 N”印刷段是声明性规则身份：运行时数值读卡牌数据 TrialValue，发动试炼是
    // 规则行动（不入栈），翻面由试炼完成规则行动处理。ST06-06/07/08 三张试炼军团的
    // 结构化路径不生成 trial 段，其身份纯由数据字段承载；封闭集合守卫要求未来新增
    // 印刷段必须重新审查。
    internal static readonly string[] TrialValueAbilityIds =
    [
        "S02-0604:ability:trial:2117897dcefd3125",
        "S02-0606:ability:trial:bb29c925c9fcdc82",
        "S02-0609:ability:trial:bb29c925c9fcdc82",
        "S02-0610:ability:trial:bb29c925c9fcdc82",
        "S02-0613:ability:trial:bb29c925c9fcdc82",
        "S02-0614:ability:trial:bb29c925c9fcdc82",
        "S02-0617:ability:trial:bb29c925c9fcdc82",
        "S02-0618:ability:trial:2117897dcefd3125",
    ];

    private static readonly L12LifecycleProfile TrialValue = new("rule:trial-value",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["printed-identity"] = "L12StructuredCardRules.IsTrialLegion",
            ["button"] = "BuildAbilityViews",
            ["commit"] = "TryCommitTrialAdvanceActivation",
            ["settlement"] = "ResolveUsualTrialAdvance",
            ["advance-core"] = "AdvanceTrialCore",
            ["completion"] = "CompleteTrialRuleAction",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "发动试炼是规则行动而非卡牌效果，不创建可响应或无效的效果堆叠。",
            ["payment-cancel"] = "试炼推进不支付士气或符文；代价是来源军团休整，由规则行动提交复验。",
            ["no-target"] = "不选择效果对象，只推进来源军团自身的试炼进度。",
            ["duplicate-submit"] = "重复提交由规则行动入口按当前试炼进度、回合与发动锁复验。",
        })
    {
        AdditionalChecks = ["trial-value-matches-card-data", "summon-round-lock", "completion-flip", "st06-no-printed-segment"],
    };

    // 「晋升」印刷段：消耗并翻转N神力，叠放至同名非【晋升者】军团上方登场。
    // ST05-01 埃涅阿斯·晋升同为【晋升者】且运行时晋升能力完整（基底映射 S02-0512、
    // 费用由共享入口读卡文），但其结构化目录不生成 promotion 印刷段；
    // 封闭集合守卫锁定该不对称，未来补印段或改动运行时晋升都必须重新审查。
    internal static readonly string[] PromotionEntryAbilityIds =
    [
        "S02-0501:ability:promotion:3eb467465ef47272",
        "S02-0503:ability:promotion:3eb467465ef47272",
        "S02-0505:ability:promotion:e890e8664470e824",
        "S02-0507:ability:promotion:e890e8664470e824",
    ];

    private static readonly L12LifecycleProfile PromotionEntry = new("summon-flow:promotion-entry",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["identity"] = "IsS2PromotionCard",
            ["foundation-candidates"] = "S2PromotionFoundations",
            ["cost-calculation"] = "S2PromotionGodPowerCost",
            ["options"] = "BuildS2PromotionOptions",
            ["entry"] = "BeginS2PromotionEntry",
            ["commit"] = "PlayS2Promotion",
            ["state-inheritance"] = "L12S2ZoneOps.InheritPromotionState",
            ["foundation-detach"] = "DetachPromotionFoundations",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "晋升登场是打出流程中的规则行动，不创建可响应或无效的独立效果堆叠；晋升登场触发的效果另行验收。",
            ["multi-target-applicability"] = "一次只叠放一个同名非【晋升者】基底，不存在多目标结算。",
            ["duplicate-submit"] = "重复提交由打出/晋升流程的命令与Prompt协议复验。",
        })
    {
        AdditionalChecks = ["payment-cancel", "foundation-invalidated", "god-power-consume-and-flip", "promotion-discount", "single-candidate-choice"],
    };

    // 「位于前排」获得关键词且对方回合兵力+1000 的组合行：S02 三张与结构化批4接管后的
    // ST 三卡同型。挑衅授予经 abilityRef 引用本卡关键词定义段；对方回合兵力+1000 为
    // 声明性原子，运行时由 OpponentTurnFrontTroopsBonus 单入口承载。
    internal static readonly string[] FrontRowKeywordTroopsAbilityIds =
    [
        "S02-0004:ability:continuous:16dc08d7324d1649",
        "S02-0007:ability:continuous:58ce6286f39b73ee",
        "S02-0615:ability:continuous:16dc08d7324d1649",
        "ST02-02:ability:continuous:c51a646e6338a9d1",
        "ST04-01:ability:continuous:59dd263106457575",
        "ST06-02:ability:continuous:c51a646e6338a9d1",
    ];

    private static readonly L12LifecycleProfile FrontRowKeywordTroops = new("continuous:front-row-keyword-troops",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["definition"] = "L12StructuredCardRules.GetCombatRuleAbilities",
            ["condition-and-active-state"] = "L12StructuredCardRules.ConditionMatches",
            ["keyword-grant-chain"] = "L12StructuredCardRules.HasTaunt",
            ["troops-bonus-definition"] = "L12StructuredCardRules.OpponentTurnFrontTroopsBonus",
            ["troops-consumer"] = "RecalculateContinuousTroops",
            ["attack-candidates"] = "BuildLegalAttackTargets",
            ["attack-revalidation"] = "TryValidateAttackTarget",
            ["presentation"] = "BuildActiveKeywords",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "位置条件的持续授予与兵力修正不独立入栈，不能作为一次效果被无效。",
            ["payment-cancel"] = "本段没有费用或支付Prompt。",
            ["no-target"] = "本段不选择效果对象；abilityRef 是本卡能力段的静态引用，不是玩家选择。",
            ["target-invalidated"] = "无效果目标；挑衅授予与兵力修正每次都按当前行位与回合归属重新判定。",
            ["duplicate-submit"] = "持续规则读取无副作用；重复进攻提交仍由公共战斗协议拒绝。",
        })
    {
        AdditionalChecks = ["row-condition-current", "opponent-turn-troops", "ability-ref-chain", "authoritative-consumer"],
    };

    // 「位于后排」进攻时兵力视为固定值：条件与取值由 CombatProfile 统一读取
    // ModifyTroops set 原子，进攻动作内应用并在进攻后还原；不独立入栈。
    internal static readonly string[] BackRowAttackTroopsSetAbilityIds =
    [
        "S01-0409:ability:attack:c900a6435336564c",
        "S02-0507:ability:attack:d20040947938d125",
    ];

    private static readonly L12LifecycleProfile BackRowAttackTroopsSet = new("attack:troops-set-on-attack",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["definition"] = "L12StructuredCardRules.CombatProfile",
            ["attack-settlement"] = "Attack",
            ["post-attack-revert"] = "RevertPendingCombatTroopsModifiers",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "进攻时兵力视为是进攻动作内的条件修正，不创建可响应或无效的独立效果堆叠。",
            ["payment-cancel"] = "本段没有费用或支付Prompt。",
            ["no-target"] = "本段只作用于来源军团自身，不选择效果对象。",
            ["target-invalidated"] = "无外部对象；进攻合法性由公共战斗入口按当前状态复验。",
            ["duplicate-submit"] = "重复进攻提交由公共战斗协议拒绝；条件修正在进攻时按当前状态重新计算。",
        })
    {
        AdditionalChecks = ["row-condition-current", "set-value-parameter", "post-attack-revert", "authoritative-consumer"],
    };

    // granted「获得1符文」段：父能力（兰斯洛特击杀时、康斯坦丝登场时的分支选择）负责声明与
    // 响应，段本身只是共享 GainRune 原子的定义引用，不独立入栈。
    internal static readonly string[] GrantedGainRuneAbilityIds =
    [
        "S02-0602:ability:granted:6235a3f3a12afdbb",
        "S02-0614:ability:granted:6235a3f3a12afdbb",
    ];

    private static readonly L12LifecycleProfile GrantedGainRune = new("granted:gain-rune",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["settlement"] = "L12S2ZoneOps.GainRunes",
            ["presentation"] = "ResolveEffectPresentationSceneId",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "granted 获得符文段不独立入栈；授予它的父能力是否被响应或无效另行验收。",
            ["payment-cancel"] = "本段没有费用；父能力的费用由父能力自身协议处理。",
            ["no-target"] = "获得符文不选择效果对象。",
            ["target-invalidated"] = "无效果目标；分支失效由父能力的结算复验处理。",
            ["duplicate-submit"] = "本段没有独立提交命令；重复提交由父能力的计划/堆叠协议复验。",
            ["single-candidate-choice"] = "“获得符文”作为父能力选项之一，分支选择由父能力的声明协议负责。",
        })
    {
        AdditionalChecks = ["parent-grant-boundary", "rune-cap", "authoritative-consumer"],
    };

    // 士气卡（cardType=rune）主动效果段：S01 基准卡与 ST 版本卡经士气身份目录归一后
    // 零分支共用同一管线——按钮/资格/费用表/回合次数/提交/入栈/结算分发均为共享入口；
    // 各阵营效果语义差异在结算体内的参数化分支，属于能力自身内容而非独立生命周期。
    internal static readonly string[] MoraleActiveEffectAbilityIds =
    [
        "S01-01C1:ability:active:3a8789b35c0c2be4",
        "ST01-C1:ability:static:6907bfcf5dbbfeb4",
        "S01-02C1:ability:static:91802cda49d575fb",
        "S01-02C1:ability:static:ddab147dd97c360f",
        "ST02-C1:ability:static:f2b97501194b5c40",
        "ST02-C1:ability:static:29d1864e955f856e",
        "S01-03C1:ability:static:fa92f5d792a32bdc",
        "ST03-C1:ability:static:36b1c5751cc508f9",
        "S01-04C1:ability:static:7f60c31c00b0f718",
        "ST04-C1:ability:static:d9cac21fb706e3c8",
        "S02-05C1:ability:active:5dec5c18aaf62a03",
        "S02-05C1A:ability:active:5dec5c18aaf62a03",
        "S02-06C1:ability:static:7339369656140c39",
        "ST06-C1:ability:static:88a76dc195d499ee",
    ];

    private static readonly L12LifecycleProfile MoraleActiveEffect = new("morale:active-effect-pipeline",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["identity-normalization"] = "L12MoraleIdentityCatalog.CanonicalEffectCardId",
            ["button"] = "FactionEffectSnapshot",
            ["eligibility"] = "ActiveAbilityUnavailableReason",
            ["cost-table"] = "GetActiveAbilityMoraleCost",
            ["usage-rule"] = "L12ActiveUsageRules.Find",
            ["commit"] = "CommitActiveAbilityCore",
            ["settlement-dispatch"] = "ResolveActiveEffect",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["payment-cancel"] = "支付取消与预付返还由共享提交管线 CommitActiveAbilityCore 统一兜底。",
            ["duplicate-submit"] = "重复提交由共享主动能力管线按当前费用/次数/状态复验。",
        })
    {
        AdditionalChecks = ["canonical-version-parity", "once-per-turn", "morale-cost-table", "authoritative-consumer"],
    };

    // 士气资源身份声明段：「额外通用士气」与「规则上此卡可视为1张士气」没有独立代码分支；
    // 其语义由士气区成员身份结构性满足，消费端是共享的资源计数与支付入口。
    internal static readonly string[] MoraleResourceIdentityAbilityIds =
    [
        "S01-00C1:ability:static:db1ae0a9efb4bff8",
        "S02-05C1:ability:static:5879d4c3fe97b3cf",
        "S02-05C1A:ability:static:5879d4c3fe97b3cf",
    ];

    private static readonly L12LifecycleProfile MoraleResourceIdentity = new("morale:resource-identity",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["resource-count"] = "ActiveResourceCount",
            ["payment"] = "TryConsumeMorale",
            ["manual-selection"] = "CanConsumeSelectedResources",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "资源身份声明不是效果，不独立入栈，不能被无效。",
            ["payment-cancel"] = "本段自身没有费用；它声明的卡作为支付资源时的取消由公共支付协议处理。",
            ["no-target"] = "资源身份声明不选择效果对象。",
            ["target-invalidated"] = "无效果对象；支付时按士气区当前成员身份重新计数。",
            ["duplicate-submit"] = "声明读取无副作用；支付重复提交由公共支付协议复验。",
        })
    {
        AdditionalChecks = ["counts-as-morale-structural", "authoritative-consumer"],
    };

    // 「阵亡时，将兵力在本回合变为1000作为代替」：卡诺匹斯罐四与安倍晴明的括号段共用
    // 致命替代管线——登场时授予一次免死、致命时兵力置1000代替；替代不走弹框。
    internal static readonly string[] ImmortalReplacementAbilityIds =
    [
        "S01-0220:ability:death:00f139f8bc316591",
        "S01-0411:ability:death:00f139f8bc316591",
    ];

    private static readonly L12LifecycleProfile ImmortalReplacement = new("death:immortal-replacement",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant"] = "GrantImmortalUntilNextTurnStart",
            ["active-state"] = "HasActiveImmortal",
            ["lethal-replacement"] = "RemoveFromField",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "代替在阵亡处理内裁定，不创建可响应或无效的独立效果堆叠；授予它的登场效果另行验收。",
            ["payment-cancel"] = "本段没有费用或支付Prompt。",
            ["no-target"] = "本段的代替对象由授予它的登场效果选择；代替裁定本身不再选对象。",
            ["target-invalidated"] = "代替在当前致命处理内读取仍位于战场的当前实例，不保存可被逆结算改变的对象声明。",
            ["duplicate-submit"] = "代替在致命裁定中按实例状态执行一次，没有独立提交命令。",
        })
    {
        AdditionalChecks = ["single-use", "troops-set-to-1000", "authoritative-consumer"],
    };

    // 「击杀时 本回合获得【贯穿】」：帕西瓦尔与库丘林的击杀段共用印刷贯穿出口——
    // 击杀事实资格审查、印刷贯穿身份与生成进攻均为共享入口；不存在实例级授予状态。
    internal static readonly string[] AfterKillPiercingAbilityIds =
    [
        "S02-0606:ability:after-kill:7680beaaf4313595",
        "S02-0611:ability:after-kill:7680beaaf4313595",
    ];

    private static readonly L12LifecycleProfile AfterKillPiercing = new("after-kill:printed-piercing",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["kill-fact-gate"] = "ResolveTypedKillSourceEvent",
            ["printed-settlement"] = "TryResolveS2FactionAfterAttack",
            ["generated-attack"] = "BeginPiercingAttack",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "击杀时获得贯穿是印刷身份驱动的规则判定；是否可被响应由击杀触发族的响应范围另行验收。",
            ["payment-cancel"] = "本段没有费用或支付Prompt。",
            ["no-target"] = "本段不选择效果对象；贯穿进攻的主宰目标由生成进攻的公共复验决定。",
            ["duplicate-submit"] = "击杀事实由共享时间线裁定一次；没有独立提交命令。",
        })
    {
        AdditionalChecks = ["original-combat-kill-only", "no-attack-trigger-on-generated", "authoritative-consumer"],
    };

    // 「位于前排」获得【挑衅】纯授予行（埃涅阿斯）：结构化扫描经 abilityRef 链到关键词定义段，
    // HasTaunt/ConditionMatches/AbilityGrantsKeyword 即全部消费端。
    internal const string AeneasFrontRowGrantAbilityId = "S02-0512:ability:static:e44e97f2fb745816";

    private static readonly L12LifecycleProfile FrontRowKeywordGrant = new("continuous:front-row-keyword-grant",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["definition"] = "L12StructuredCardRules.GetCombatRuleAbilities",
            ["condition"] = "L12StructuredCardRules.ConditionMatches",
            ["grant-chain"] = "L12StructuredCardRules.AbilityGrantsKeyword",
            ["active-state"] = "L12StructuredCardRules.HasTaunt",
            ["presentation"] = "BuildActiveKeywords",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "位置条件的持续授予不独立入栈，不能作为一次效果被无效。",
            ["payment-cancel"] = "本段没有费用或支付Prompt。",
            ["no-target"] = "本段不选择效果对象；abilityRef 是本卡能力段的静态引用。",
            ["duplicate-submit"] = "持续授予读取无副作用。",
        })
    {
        AdditionalChecks = ["row-condition-current", "ability-ref-chain", "authoritative-consumer"],
    };

    // 「位于前排」获得【挑衅】授予定义（阿喀琉斯·晋升）：granted-static 段被 HasTaunt 的
    // 结构化扫描明确排除；实际授予由击杀后的共享实例授予入口完成，有效期到下个我方回合结束。
    internal const string AchillesFrontRowGrantAbilityId = "S02-0503:ability:granted-static:e67d03cee97f98a6";

    private static readonly L12LifecycleProfile GrantedFrontRowTauntOnKill = new("granted-static:front-row-taunt-on-kill",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant"] = "GrantTauntUntilNextOwnTurnEnd",
            ["active-state"] = "L12StructuredCardRules.HasTaunt",
            ["presentation"] = "BuildActiveKeywords",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "授予定义段不独立入栈；授予它的击杀时父能力是否被响应另行验收。",
            ["payment-cancel"] = "本段没有费用；击杀时父能力的费用由父能力自身协议处理。",
            ["no-target"] = "授予对象是来源军团自身，不选择效果对象。",
            ["duplicate-submit"] = "授予由父能力结算执行一次；本段没有独立提交命令。",
        })
    {
        AdditionalChecks = ["parent-grant-boundary", "front-row-required", "turn-expiry", "authoritative-consumer"],
    };

    // 吕布「进攻无损，无法被远程进攻」整行段：原子引用整行是声明文本；结构化语义由同卡
    // #4/#5 段（已归属 structured-combat-rule 族）与 overlay 模板承载，消费端为 CombatProfile 单出口。
    internal const string DuelCombatLineAbilityId = "S01-0101:ability:static:b5c9e323c0a061cc";

    private static readonly L12LifecycleProfile DuelCombatLine = new("continuous:duel-combat-line",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["condition-and-active-state"] = "L12StructuredCardRules.CombatProfile",
            ["attack"] = "Attack",
            ["combat-settlement"] = "ResolveDefenseCore",
            ["attack-revalidation"] = "TryValidateAttackTarget",
            ["disaster-target"] = "HasMandatoryDisasterLegionTarget",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "持续战斗规则不独立入栈，不能作为一次效果被无效。",
            ["payment-cancel"] = "本段没有费用或支付Prompt。",
            ["no-target"] = "本段只约束来源军团自身的战斗规则，不选择效果对象。",
            ["target-invalidated"] = "无效果目标；进攻与交战每次按当前状态复验。",
            ["duplicate-submit"] = "持续规则读取无副作用；重复进攻提交由公共战斗协议拒绝。",
        })
    {
        AdditionalChecks = ["structured-split-siblings", "authoritative-consumer"],
    };

    // 简单持续兵力修正段：四张卡的消费端均在共享连续重算出口内（RecalculateContinuousTroops /
    // GetTurnAndPositionContinuousTroops），段原子为声明性参数，未直接驱动运行时。
    internal static readonly string[] SimpleContinuousTroopsAbilityIds =
    [
        "S01-0203:ability:static:0a317a499dc4420e",
        "S02-0516:ability:static:a29458736f52d0a9",
        "S02-0519:ability:static:2b21805b14115304",
        "S02-0523:ability:static:05da64c53e8a7606",
    ];

    private static readonly L12LifecycleProfile SimpleContinuousTroops = new("continuous:simple-troops-rule",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["continuous-recalc"] = "RecalculateContinuousTroops",
            ["turn-and-position-bonus"] = "GetTurnAndPositionContinuousTroops",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "持续兵力修正不独立入栈，不能作为一次效果被无效。",
            ["payment-cancel"] = "本段没有费用或支付Prompt。",
            ["no-target"] = "修正对象由规则文本固定（自身/相邻/对方全场），不创建玩家选择。",
            ["target-invalidated"] = "无声明对象；每次重算按当前战场状态重新判定条件。",
            ["duplicate-submit"] = "持续修正读取无副作用。",
        })
    {
        AdditionalChecks = ["shared-recalc-outlet", "condition-current", "authoritative-consumer"],
    };

    // 希波吕忒「此军团休整时，我方军团前后位移无需消耗费用」：消费端为共享位移命令 Move
    // 内的来源状态判定；段原子为声明，未直接驱动。
    internal const string RestedFreeFrontBackMoveAbilityId = "S02-0510:ability:static:5193793609facf70";

    private static readonly L12LifecycleProfile RestedFreeFrontBackMove = new("continuous:rested-free-front-back-move",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["move-command"] = "Move",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "持续费用豁免不独立入栈，不能作为一次效果被无效。",
            ["payment-cancel"] = "豁免本身没有费用；被豁免的位移动作没有支付Prompt。",
            ["no-target"] = "豁免不选择对象；位移目的地由玩家按公共位移协议选择。",
            ["target-invalidated"] = "无声明对象；位移提交按当前来源休整状态与目的地合法性复验。",
            ["duplicate-submit"] = "重复位移提交由公共位移协议拒绝。",
        })
    {
        AdditionalChecks = ["source-rested-current", "authoritative-consumer"],
    };

    // 特殊响应段：候选枚举、公开卡池判定、提交与费用分支的卡号谓词已全部收敛到
    // L12StructuredCardSemantics 响应身份注册表；入栈提交与结算为共享管线。
    // 绝对防御/落穴走 response-negate，佣兵部队走 response-block（抵挡）。
    internal static readonly string[] NegateResponseAbilityIds =
    [
        "S01-0016:ability:reaction:eda8f9987e9ccfe3",
        "S01-0018:ability:reaction:248207b49df4bd77",
    ];

    private static readonly L12LifecycleProfile NegateResponse = new("reaction:negate-pipeline",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["candidates"] = "LegalResponseSources",
            ["pool-timing"] = "IsPoolCounterResponseAtTiming",
            ["submit"] = "BeginSelectedStackResponse",
            ["commit"] = "CommitNegateResponse",
            ["settlement"] = "ResolveTopStack",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["no-target"] = "响应对象是堆叠顶部的对方进攻或效果，不另行选择效果目标。",
            ["duplicate-submit"] = "重复提交由堆叠响应协议按当前栈顶与响应窗口复验。",
        })
    {
        AdditionalChecks = ["payment-cancel", "capability-registry", "pool-parity", "authoritative-consumer"],
    };

    internal const string MercenaryHandBlockAbilityId = "S01-0002:ability:reaction:a472c4e7c34abf4b";

    private static readonly L12LifecycleProfile MercenaryHandBlock = new("reaction:hand-block",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["candidates"] = "LegalResponseSources",
            ["pool-timing"] = "CanMasterCardPoolRespondAtTiming",
            ["submit"] = "BeginSelectedStackResponse",
            ["commit"] = "CommitMercenaryResponse",
            ["settlement"] = "ResolveTopStack",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["no-target"] = "抵挡对象即当前对我方军团的进攻，不另行选择效果目标。",
            ["duplicate-submit"] = "重复提交由堆叠响应协议按当前栈顶与响应窗口复验。",
        })
    {
        AdditionalChecks = ["self-discard-cost", "defender-only", "capability-registry", "authoritative-consumer"],
    };

    // 天灾持续规则段：全部经`L12ActiveDisasterRules`查询层判定，引擎与效果计划不再按
    // 天灾卡号分支。ST-DS02 色欲之罪已由 ST 语义谓词（Batch3B）覆盖并保持 composite-definition，
    // 不属于本族；黯陨晨星的掷骰由天灾管线 BeginMainPhaseDisasterEffect 承载。
    internal static readonly string[] DisasterContinuousRuleAbilityIds =
    [
        "S01-DS01:ability:static:9d604388d9725837",
        "S01-DS02:ability:static:4408d437a8ab5e5a",
        "S01-DS03:ability:static:70004a014a03d2a0",
        "S01-DS04:ability:static:017c7359962a2512",
        "S01-DS08:ability:static:3e7cd5724f09420c",
        "S01-DS10:ability:static:33501d2503c08b73",
        "S02-DS01:ability:static:31558cb4f3e2c3da",
        "S02-DS02:ability:static:01aeea1f7fc7e317",
        "S02-DS03:ability:continuous:fed4f60f2af1523c",
        "S02-DS04:ability:static:00575cc9fcb1aaaa",
        "S02-DS05:ability:static:335d304b639c5d3f",
        "S02-DS06:ability:static:c1632b7b22b87c4f",
        "S01-DS04:ability:attack:68f2ff0b600a41e8",
        "S02-DS05:ability:attack:4326fa5eef9e6e3a",
    ];

    private static readonly L12LifecycleProfile DisasterContinuousRule = new("disaster:continuous-rule",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["rule-registry"] = "L12ActiveDisasterRules.HasRegisteredContinuousRule",
            ["attack-legion-validation"] = "TryValidateAttackTarget",
            ["attack-master-validation"] = "CanAttackMasterTarget",
            ["placement-and-movement"] = "PlayCard",
            ["hand-cost"] = "GetPlayCostWithSigurdDiscount",
            ["master-ability-quote"] = "QuoteActiveMorale",
            ["disaster-value"] = "SetDisasterValue",
            ["effect-hook"] = "PushEffect",
            ["main-phase-effect"] = "BeginMainPhaseDisasterEffect",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "天灾机制无法被任何效果抵挡或规避，持续规则不独立入栈、不能被无效。",
            ["payment-cancel"] = "持续规则本身没有费用；其施加的费用修正由被打出的卡自身协议处理。",
            ["no-target"] = "持续规则不选择效果对象，按规则文本对全场生效。",
            ["target-invalidated"] = "无声明对象；每次判定读取当前活跃天灾。",
            ["duplicate-submit"] = "规则判定读取无副作用。",
        })
    {
        AdditionalChecks = ["registry-closed-set", "condition-current", "authoritative-consumer"],
    };

    // 「即将阵亡时…代替承受」段：阿喀琉斯（付费神力）与海伦（弃手牌）共用同一条致命替代
    // 弹框管线（TryOfferEffectLethalReplacement → 替代种类/候选/弹框/应用）；费用由各自分支承担。
    internal static readonly string[] LethalReplacementOfferAbilityIds =
    [
        "S01-0205:ability:death:7016351513168cdb",
        "S02-0504:ability:lethal-replacement:3fb565d50830f260",
        "S02-0515:ability:lethal-replacement:654c3040d6da8b4d",
    ];

    private static readonly L12LifecycleProfile LethalReplacementOffer = new("lethal-replacement:offer-pipeline",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["offer"] = "TryOfferEffectLethalReplacement",
            ["substitution-kind"] = "CardLethalSubstitutionKind",
            ["eligibility"] = "CanUseAchillesLethalReplacement",
            ["candidates"] = "CardLethalSubstitutionCandidates",
            ["apply"] = "TryApplyCardLethalSubstitution",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "致命替代在阵亡处理内以弹框裁定，不创建可无效的独立效果堆叠；天灾结算不建立替代窗口。",
            ["no-target"] = "替代对象即即将阵亡的受保护卡本身；费用由分支协议支付，不另行选择效果目标。",
            ["duplicate-submit"] = "重复提交由 pending 键与回合次数键复验，弹框关闭后状态不残留。",
        })
    {
        AdditionalChecks = ["payment-cancel", "once-per-turn", "front-row-required", "recovery-resume", "authoritative-consumer"],
    };

    // 规则声明段：卡文明确点名的每回合限制由卡名共享次数注册表统一记账；
    // 万物统御之戒的阵营映射由有效阵营出口承载；雷神之锤的主宰条件由能力按钮门禁承载；
    // 开场规则段由开局管线承载。规则声明不入栈、无费用、不选对象。
    internal static readonly string[] CardNameOncePerTurnAbilityIds =
    [
        "S02-0006:ability:continuous:7f3bdf9055e53845",
        "S02-0306:ability:continuous:a5a8e191442bbfac",
    ];

    private static readonly L12LifecycleProfile CardNameOncePerTurn = new("rule:once-per-turn-by-name",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["usage-key"] = "L12CardNameUsageRules.Key",
            ["usage-check"] = "L12CardNameUsageRules.HasUsed",
            ["usage-commit"] = "L12CardNameUsageRules.TryUse",
        },
        RuleDeclarationExemptions("每回合一次的限制声明不创建效果，只约束同名卡的共享次数记账。"));

    internal const string UniversalFactionMappingAbilityId = "S02-0008:ability:continuous:766cca673a9815ad";

    private static readonly L12LifecycleProfile UniversalFactionMapping = new("rule:universal-faction-mapping",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["effective-faction"] = "L12StructuredCardRules.EffectiveFaction",
            ["effective-traits"] = "L12StructuredCardRules.EffectiveTraits",
            ["faction-check"] = "L12StructuredCardRules.HasFaction",
            ["candidate-consumer"] = "IsDesertHandSummonCandidate",
        },
        RuleDeclarationExemptions("阵营映射声明不创建效果，只参与有效阵营/特征的统一计算。"));

    internal const string ThorHammerMasterGateAbilityId = "S02-0301:ability:continuous:e48cf407ce847427";

    private static readonly L12LifecycleProfile ThorHammerMasterGate = new("rule:thor-hammer-master-gate",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["master-gate"] = "L12StructuredCardSemantics.MasterAbilityGate",
            ["button-projection"] = "BuildAbilityViews",
            ["declaration"] = "TryBeginS2RemainingAbility",
            ["commit"] = "TryCommitS2RemainingAbility",
        },
        RuleDeclarationExemptions("主宰条件声明不创建效果；按钮、声明与提交入口共读同一结构化门禁。"));

    internal static readonly string[] GameSetupRuleAbilityIds =
    [
        "S02-0305:ability:game-setup:cf14affeb486a9f7",
        "S02-03M1:ability:game-setup:46b2a85c54cecc56",
    ];

    // 奥林匹斯诸神巅#4「主神开场即可追加2张额外士气」：台账标 setup（而非 game-setup）。
    // 备注：印刷「即可」的选发窗口未实现，当前由 PrepareLibrariesAndHands 自动追加——疑似缺口已记录。
    internal const string GameSetupAutoMoraleAbilityId = "S02-05D1:ability:setup:cb6a45eff0631d64";

    private static readonly L12LifecycleProfile GameSetupRule = new("rule:game-setup",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["setup-defaults"] = "BeginOptionalS2Setup",
            ["hand-preparation"] = "PrepareLibrariesAndHands",
        },
        RuleDeclarationExemptions("开场规则只在开局管线生效，不创建效果、费用或对象选择。"));

    private static SortedDictionary<string, string> RuleDeclarationExemptions(string reason)
        => new(StringComparer.Ordinal)
        {
            ["negated"] = "规则声明不入栈，不能被响应或无效。",
            ["payment-cancel"] = "规则声明本身没有费用或支付Prompt。",
            ["no-target"] = "规则声明不选择效果对象。",
            ["target-invalidated"] = "无效果对象；每次判定读取当前状态。",
            ["duplicate-submit"] = "规则判定读取无副作用。",
            ["note"] = reason,
        };

    // 「位于后排」获得协防：结构化关键词定义挂到共享支援合法性判定
    // （非同列后排须协防、支援候选过滤），全池仅木下藤吉郎一张。
    internal const string CooperativeSupportAbilityId = "ST04-07:ability:continuous:8f395636980d57ca";

    private static readonly L12LifecycleProfile CooperativeSupport = new("continuous:cooperative-support",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["definition"] = "L12StructuredCardRules.HasCooperativeSupport",
            ["support-validation"] = "ValidateDefenseChoice",
            ["support-candidates"] = "HasLegalLegionSupport",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "持续关键词授予不独立入栈，不能作为一次效果被无效。",
            ["payment-cancel"] = "本段没有费用或支付Prompt。",
            ["no-target"] = "本段不选择效果对象；支援对象由玩家按公共防御协议选择。",
            ["target-invalidated"] = "无声明对象；支援合法性按当前行位与状态复验。",
            ["duplicate-submit"] = "持续授予读取无副作用；重复支援提交由公共防御协议拒绝。",
        })
    {
        AdditionalChecks = ["row-condition-current", "authoritative-consumer"],
    };

    internal static readonly string[] StructuredContinuousCombatRuleAbilityIds =
    [
        "S01-0004:ability:static:1644ef88125b05c1",
        "S01-0101:ability:static:1f027ad861ea0006",
        "S01-0101:ability:static:1041797d91099ae1",
        "S02-0002:ability:continuous:5643b9f0c6e298e6",
        "S02-0005:ability:continuous:0663e3d5b31edc67",
        "S02-0007:ability:continuous:602cafbbc29faa3f",
        "S02-0101:ability:continuous:4cd3104ae17d316d",
        "S02-0201:ability:continuous:39b0b1524eaed536",
        "S02-02M1:ability:continuous:a83e1e0971bbe6f0",
        "S02-0302:ability:continuous:48719a94741bbf36",
        "S02-0503:ability:static:5e2fcb0f2798f57a",
        "S02-0504:ability:static:0ada28f438439ac2",
        "S02-0516:ability:static:17774ead9eb8ed69",
        "S02-0603:ability:continuous:5e0d666ac6a386ba",
        "S02-0609:ability:continuous:dc2aa603cc3d136c",
        "S02-0616:ability:continuous:5afe2828d587391f",
    ];

    private static readonly L12LifecycleProfile StructuredContinuousCombatRule =
        new("continuous:structured-combat-rule",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["definition"] = "L12StructuredCardRules.GetCombatRuleAbilities",
                ["condition-and-active-state"] = "L12StructuredCardRules.CombatProfile",
                ["attack-candidates"] = "BuildLegalAttackTargets",
                ["attack-revalidation"] = "TryValidateAttackTarget",
                ["master-protection"] = "L12StructuredCardRules.ProtectsMasterFromTroops",
                ["support-source-revalidation"] = "L12StructuredCardRules.CannotSupport",
                ["support-target-revalidation"] = "L12StructuredCardRules.CannotReceiveBackRowSupport",
                ["trial-protection"] = "L12StructuredCardRules.ProtectsActiveTrialLegions",
                ["combat-settlement"] = "ResolveDefenseCore",
            },
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["negated"] = "印刷持续战斗规则不独立入栈，不能被一次效果无效；授予它的父效果若存在则另行验收。",
                ["payment-cancel"] = "本族持续规则本身没有费用或支付Prompt。",
                ["single-candidate-choice"] = "本族只约束公共进攻/支援候选与提交复验，不代替玩家选择合法目标。",
                ["duplicate-submit"] = "持续规则读取无副作用；重复进攻或支援提交仍由公共动作协议拒绝。",
            })
        {
            AdditionalChecks = ["row-and-ready-condition", "source-current-type", "candidate-and-submit-parity", "reconnect-derived-state"],
        };

    private static readonly HashSet<string> ReviewedContinuousCombatRuleParameters = new(StringComparer.Ordinal)
    {
        "cannotAttack", "cannotSupport", "attackNoLoss", "cannotBeRanged",
        "protectMasterFromTroopsAtMost", "cannotAttackMaster", "cannotReceiveBackRowSupport",
        "incomingRangedCombatDamageAdjustment", "targetableByAttack", "protect",
    };

    private static bool IsStructuredContinuousCombatRule(L12AtomicAbility ability)
    {
        if (ability.ExecutionModel is not ("continuous" or "granted-continuous")) return false;
        var rules = ability.Atoms.Where(atom => atom.Kind == L12AtomKinds.AttackRule).ToArray();
        return rules.Length > 0 && rules.All(atom => !atom.Parameters.ContainsKey("text"))
            && rules.Any(atom => atom.Parameters.Keys.Any(ReviewedContinuousCombatRuleParameters.Contains));
    }

    internal static readonly string[] PrintedEntryCostAbilityIds =
    [
        "S01-0104:ability:static:a91d7d481db612a9",
        "S01-0114:ability:static:a91d7d481db612a9",
        "S01-0301:ability:static:71dd875155781eb0",
        "S01-0302:ability:static:acc29b0ca499d087",
        "S01-0305:ability:static:9ed1ca8df2e5f029",
        "S01-0306:ability:static:9ed1ca8df2e5f029",
        "S02-0202:ability:continuous:94759febdd62fd32",
        "S02-0203:ability:continuous:418e71545576e12d",
    ];

    private static readonly L12LifecycleProfile PrintedEntryCost =
        new("hand-play:printed-entry-cost-condition",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["definition"] = "L12StructuredCardSemantics.PrintedEntryCostRule",
                ["condition-and-calculation"] = "PrintedEntryCostModifier",
                ["combined-play-cost"] = "GetPlayCostWithSigurdDiscount",
                ["button-and-snapshot"] = "SnapshotHand",
                ["resource-payment"] = "EnsurePlayResourcePaymentChoice",
            },
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["no-target"] = "本族只按当前公开状态修改手牌打出费用，不选择效果对象。",
                ["negated"] = "印刷持续减费在支付前参与实际费用计算，不独立入栈，不能作为一次效果被无效。",
                ["target-invalidated"] = "没有效果目标；资源支付提交时重新计算当前状态下的实际费用。",
                ["multi-target-applicability"] = "一次只计算当前待打出手牌实例的费用。",
            })
        {
            AdditionalChecks = ["condition-false", "zero-floor", "payment-cancel", "reconnect-payment", "display-and-payment-parity"],
        };

    internal static readonly string[] StructuredHandCostAbilityIds =
    [
        "S02-0509:ability:static:fff4ed8e0ac25ed9",
        "S02-0510:ability:static:52b46f1b508e6aa1",
        "S02-0512:ability:static:fff4ed8e0ac25ed9",
        "S02-0518:ability:static:fff4ed8e0ac25ed9",
        "S02-0605:ability:continuous:5ff487de55c0ca1d",
        "S02-0611:ability:continuous:5745356459e85080",
        "S02-0612:ability:continuous:064a0a1c5382575c",
        "ST03-02:ability:continuous:057a02a660ebfae1",
        "ST04-10:ability:continuous:2a1c905931cd7b32",
        "ST06-01:ability:continuous:3ced1d4d38141877",
    ];

    private static readonly L12LifecycleProfile StructuredHandCost =
        new("hand-play:structured-hand-condition-cost",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["definition"] = "L12StructuredCardRules.TryGetStructuredAbilities",
                ["condition-and-calculation"] = "L12StructuredCardRules.HandPlayCostModifier",
                ["combined-play-cost"] = "GetPlayCostWithSigurdDiscount",
                ["button-and-snapshot"] = "SnapshotHand",
                ["resource-payment"] = "EnsurePlayResourcePaymentChoice",
            },
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["no-target"] = "本族只读取手牌实例与当前公开状态计算打出费用，不创建效果对象。",
                ["negated"] = "满足条件期间的持续费用修正不独立入栈，不能作为一次效果被无效。",
                ["target-invalidated"] = "没有效果目标；支付时必须按当前状态重新计算实际费用。",
                ["multi-target-applicability"] = "一次只计算当前待打出手牌实例，不修改其他手牌实例。",
            })
        {
            AdditionalChecks = ["condition-false", "source-still-in-hand", "effective-faction", "zero-floor",
                "payment-cancel", "reconnect-payment", "display-and-payment-parity"],
        };

    internal static bool IsStructuredHandCostAbility(L12AtomicAbility ability)
    {
        if (ability.ExecutionModel != "continuous") return false;
        var hasHandCondition = ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Condition
            && atom.Parameters.GetValueOrDefault("expression")?.Contains("source.zone=hand", StringComparison.Ordinal) == true);
        if (!hasHandCondition) return false;
        return ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.SetState
                && atom.Parameters.GetValueOrDefault("key") == "source.derived-cost"
                && atom.Parameters.GetValueOrDefault("operation") == "add")
            || ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Special
                && atom.Parameters.GetValueOrDefault("semantic") == "entry-cost-minus-per-friendly-faction-legion");
    }

    internal static readonly string[] HandPlayBlockAbilityIds =
    [
        "S02-0205:ability:continuous:44bfa636b58de089",
        "S02-0305:ability:continuous:26b824128ffced1a",
    ];

    private static readonly L12LifecycleProfile HandPlayBlock =
        new("hand-play:artifact-block",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["definition"] = "L12StructuredCardSemantics.HandPlayBlockRule",
                ["condition-and-reason"] = "L12StructuredCardRules.HandPlayBlockReason",
                ["button-and-snapshot"] = "SnapshotHand",
                ["authoritative-submit"] = "PlayCard",
            },
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["no-target"] = "持续封锁只判断待打出的手牌卡种，不创建对象选择。",
                ["negated"] = "来源位于圣物区期间的持续规则不独立入栈，不能作为一次效果被无效。",
                ["payment-cancel"] = "封锁在资源支付前拒绝，未创建支付Prompt，也不会扣除资源。",
                ["target-invalidated"] = "没有效果目标；提交时按当前圣物区来源重新判断，旧按钮状态不具权威性。",
                ["multi-target-applicability"] = "每次只判断当前提交的一张手牌；其他手牌各自读取同一规则。",
            })
        {
            AdditionalChecks = ["source-zone", "same-card-exception", "priority", "non-artifact-unaffected",
                "display-and-submit-parity", "reconnect-derived-state"],
        };

    internal static readonly string[] RelicZoneLimitExemptAbilityIds =
    [
        "S01-0216:ability:static:bf632dc8776cd134",
        "S01-0217:ability:static:bf632dc8776cd134",
        "S01-0218:ability:static:bf632dc8776cd134",
        "S01-0219:ability:static:bf632dc8776cd134",
        "S01-0220:ability:static:bf632dc8776cd134",
    ];

    private static readonly L12LifecycleProfile RelicZoneLimitExempt =
        new("continuous:relic-zone-limit-exempt",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["definition"] = "L12StructuredCardSemantics.IgnoresRelicZoneLimit",
                ["artifact-zone-placement"] = "PlaceArtifactInRelicZone",
            },
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["no-target"] = "持续上限豁免不选择对象；仅决定该圣物进入主圣物位或额外圣物位。",
                ["negated"] = "规则持续生效且不独立入栈，不能作为一次效果被无效。",
                ["payment-cancel"] = "豁免不改变打出费用；支付取消仍由手牌打出协议处理。",
                ["target-invalidated"] = "没有效果目标；落位时按当前主圣物位状态重新判断。",
                ["multi-target-applicability"] = "每张符合身份的圣物独立进入额外圣物位，不替换既有主圣物。",
            })
        {
            AdditionalChecks = ["exact-card-family", "artifact-only", "primary-empty", "primary-occupied",
                "ordinary-artifact-replaces", "hand-play", "effect-generated-play", "zhuge-generated-play",
                "gm-play", "reconnect-zone-state"],
        };

    internal static readonly string[] OutOfDeckGraveyardLifecycleAbilityIds =
    [
        "S01-0212:ability:static:6d8b57888db9839b",
        "S02-0201:ability:continuous:16b90b36ef8afe2c",
    ];

    private static readonly L12LifecycleProfile OutOfDeckGraveyardLifecycle =
        new("continuous:out-of-deck-graveyard-lifecycle",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["definition"] = "L12StructuredCardSemantics.HasOutOfDeckGraveyardLifecycle",
                ["deck-size-rule"] = "L12SpecialDeckRules.DoesNotCountTowardMainDeck",
                ["opening-zone-rule"] = "L12SpecialDeckRules.StartsInGraveyard",
                ["hand-library-replacement"] = "L12SpecialDeckRules.CannotEnterHandOrLibrary",
                ["departure-replacement"] = "L12SpecialDeckRules.AlwaysReturnsToOwnerGraveyard",
                ["authoritative-departure"] = "MoveFieldCardToZone",
            },
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["no-target"] = "规则能力不选择对象；其他效果选择该卡时由目标效果自己的声明协议处理。",
                ["negated"] = "规则能力不独立入栈，不能作为一次效果被无效；离场替代在目标效果结算时适用。",
                ["payment-cancel"] = "规则能力没有费用；以该卡支付其他费用时仍按支付效果处理，并在离场后进入所有者墓地。",
                ["target-invalidated"] = "没有自身目标；通用回手/回库候选与提交均从同一身份判断，已离区实例不得替换。",
                ["multi-target-applicability"] = "多张同族卡分别应用区域替代，不因同批移动而合并或补位。",
            })
        {
            AdditionalChecks = ["exact-card-family", "text-independent", "deck-count", "opening-graveyard",
                "hand-filter", "library-filter", "owner-graveyard", "all-departure-destinations",
                "derived-card-precedence", "controller-owner-split", "reconnect-authoritative-zone"],
        };

    private static bool IsOutOfDeckGraveyardLifecycleAbility(L12AtomicAbility ability)
        => ability.ExecutionModel is "continuous" or "rule"
           && L12StructuredCardSemantics.HasOutOfDeckGraveyardLifecycle(ability.CardId)
           && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.MoveZone);

    internal const string FieldMoraleResourceAbilityId =
        "S01-0212:ability:static:025749085872cdff";

    private static readonly L12LifecycleProfile FieldMoraleResource =
        new("continuous:field-morale-resource",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["definition"] = "L12StructuredCardSemantics.FieldMoraleResourceRule",
                ["candidate-generation"] = "SpendableFieldMoraleResources",
                ["snapshot-count"] = "ActiveResourceCount",
                ["manual-payment"] = "CreateResourcePaymentPrompt",
                ["selected-payment-revalidation"] = "CanConsumeSelectedResources",
                ["selected-payment-commit"] = "TryConsumeSelectedResources",
                ["automatic-payment"] = "TryConsumeMorale",
                ["composite-reservation"] = "CompositeOrdinaryPaymentChoices",
                ["effect-payment-retry"] = "ContinueEffectMoralePayment",
                ["rejected-submit-rollback"] = "RestoreActiveResourceRollback",
                ["snapshot-projection"] = "SnapshotField",
                ["paid-cost-presentation"] = "AddPaidCostPresentationFromSnapshot",
            },
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["no-target"] = "持续资源能力不选择效果目标；支付协议只要求玩家选择实际消耗的资源实例。",
                ["negated"] = "持续资源能力不独立入栈，不能作为一次效果被无效；已支付费用不因后续效果无效而恢复。",
                ["payment-cancel"] = "允许取消的支付流程由公共支付Prompt释放声明；未提交前不改变军团状态。",
                ["target-invalidated"] = "支付提交时按原实例、当前控制者、当前回合、当前军团与活跃状态复验；失效时不换资源补位。",
                ["multi-target-applicability"] = "每个合法场上实例各代表1份资源；混合支付按实例去重并一次性提交。",
            })
        {
            AdditionalChecks = ["exact-card-family", "controller-turn", "opponent-turn", "front-row",
                "back-row", "current-controller", "active-only", "hidden-or-non-legion", "mixed-payment",
                "reservation", "effect-payment-cancel", "stale-effect-payment-retry",
                "rejected-active-rollback", "paid-cost-presentation", "stale-resource",
                "duplicate-submit", "reconnect-derived-state", "authoritative-snapshot-projection"],
        };

    internal const string BlackLotusMoraleReturnAbilityId =
        "S02-0010:ability:return-as-morale:9169de0e99d296e2";

    private static readonly L12LifecycleProfile MoraleZoneResource =
        new("replacement:morale-zone-resource",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["definition"] = "L12StructuredCardSemantics.MoraleZoneResourceRule",
                ["payment-identity"] = "OrdinaryPaymentSemanticKey",
                ["payment-prompt"] = "CreateResourcePaymentPrompt",
                ["return-prompt"] = "CreateReturnMoralePrompt",
                ["return-settlement"] = "ReturnMoraleCardToDestination",
                ["snapshot-projection"] = "SnapshotMorale",
                ["presentation-projection"] = "SnapshotMorale",
            },
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["no-target"] = "替代规则自身不选择对象；返还效果仍按公共协议选择实际士气实例。",
                ["negated"] = "替代规则不独立入栈，不可单独响应或无效；已支付的返还费用不因后续效果无效而恢复。",
                ["payment-cancel"] = "替代规则没有自身费用；它作为资源被消耗或返还时，由父级支付协议处理取消。",
                ["target-invalidated"] = "提交时选定实例必须仍在当前玩家士气区；失效后不改选其他资源补位。",
                ["multi-target-applicability"] = "同批返还的每个资源分别按当前身份决定去向，不合并、不转移到其他实例。",
            })
        {
            AdditionalChecks = ["exact-card-family", "entered-tapped", "payment-distinct-identity",
                "return-owner-graveyard", "automatic-return", "duplicate-submit",
                "v2-snapshot", "replay-projection", "frontend-structured-identity"],
        };

    internal static readonly string[] MoraleFaceFlipAbilityIds =
    [
        "S02-0508:ability:death:9aea23b4138e399e",
        "S02-0513:ability:enter:eef83ec51f2ef093",
        "S02-0518:ability:enter:6e9ddf89fefa712f",
        "S02-0520:ability:enter:361ec387b847ecee",
        "S02-0521:ability:play:4ae24413479102d1",
        "S02-05C1:ability:active:1ae9b19504eac93a",
        "S02-05C1A:ability:active:1ae9b19504eac93a",
        "S02-05D1:ability:active:519ab3c1379a9256",
        "S02-05M1:ability:friendly-ranged-death:049d5f20b59f5888",
        "ST05-C1:ability:static:6fe475d8923feb65",
        "ST05-M1:ability:active:b1f11ab05f68dda0",
    ];

    private static readonly L12LifecycleProfile MoraleFaceFlip =
        new("resource:morale-face-flip",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["identity-definition"] = "L12MoraleIdentityCatalog.CanUseGodPowerFace",
                ["candidate-generation"] = "CanFlipMoraleToGodPower",
                ["toggle-candidate-generation"] = "CanToggleMoraleFace",
                ["resolution-prompt"] = "PromptS2FlipMorale",
                ["settlement-mutation"] = "L12S2ZoneOps.FlipMoraleFace",
            },
            new SortedDictionary<string, string>(StringComparer.Ordinal))
        {
            AdditionalChecks = ["exact-printed-family", "version-alias", "black-lotus-excluded",
                "candidate-settlement-parity", "rested-only-filter", "single-candidate-choice",
                "multi-target-independent-revalidation", "v2-prompt-reconnect"],
        };

    private static bool IsMoraleFaceFlipAbility(L12AtomicAbility ability)
        => ability.Text.Contains("翻转", StringComparison.Ordinal)
            && ability.Text.Contains("士气", StringComparison.Ordinal);

    internal const string OpponentTurnFieldRuleAbilityId =
        "S01-0212:ability:static:2f33fb3652e7bd28";

    private static readonly L12LifecycleProfile OpponentTurnFieldRule =
        new("continuous:opponent-turn-field-rule",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["definition"] = "L12StructuredCardSemantics.OpponentTurnFieldRule",
                ["cost-derivation"] = "L12StructuredCardRules.OpponentTurnCostModifier",
                ["front-troops-derivation"] = "L12StructuredCardRules.OpponentTurnFrontTroopsBonus",
                ["authoritative-recalculation"] = "RecalculateContinuousTroops",
                ["public-projection"] = "SnapshotField",
            },
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["no-target"] = "持续能力只修改自身衍生数值，不创建对象选择。",
                ["negated"] = "持续能力不独立入栈，不能作为一次效果被无效。",
                ["payment-cancel"] = "能力没有费用；衍生费用只供其他支付协议读取。",
                ["target-invalidated"] = "没有效果目标；每次快照与结算前按当前回合、控制者和位置重算。",
                ["duplicate-submit"] = "持续能力没有发动或选择提交；重复生成双方快照只重新计算当前衍生值且没有副作用。",
                ["multi-target-applicability"] = "每个同名实例分别重算，不共享或累积到其他军团。",
            })
        {
            AdditionalChecks = ["exact-card-family", "opponent-turn", "controller-turn", "front-row",
                "back-row", "current-controller", "cost-and-troops-same-definition", "leave-reset",
                "reconnect-idempotence"],
        };

    internal const string TombGuardMasterAuraAbilityId =
        "S01-02D1:ability:static:d1339da6822c9ae1";

    private static readonly L12LifecycleProfile TombGuardMasterAura =
        new("continuous:tomb-guard-master-aura",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["definition"] = "L12StructuredCardSemantics.MasterFieldAuraRule",
                ["current-cost"] = "RecalculateContinuousTroops",
                ["current-troops"] = "GetTurnAndPositionContinuousTroops",
                ["presentation"] = "SnapshotField",
            },
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["no-target"] = "持续能力按当前战场上的陵墓守卫逐张应用，不建立玩家目标选择。",
                ["negated"] = "持续能力不独立入栈，不能作为一次效果被无效。",
                ["payment-cancel"] = "本段没有费用或支付Prompt。",
                ["target-invalidated"] = "没有声明对象；离场、控制权或主神变化后由共享重算立即撤销。",
                ["duplicate-submit"] = "持续重算为幂等读取，不产生次数、日志或重复状态。",
                ["single-candidate-choice"] = "持续能力没有选择步骤。",
                ["multi-target-applicability"] = "我方战场每张陵墓守卫各自同时获得兵力与当前费用修正。",
            })
        {
            AdditionalChecks = ["field-only", "cost-and-troops-same-definition", "current-controller",
                "current-cost-consumers", "presentation-consumers", "reconnect-derived-state"],
        };

    internal static readonly string[] PureSummonTurnCounterProtectionAbilityIds =
    [
        "S01-0201:ability:static:7d31de8999ce168a",
        "ST02-01:ability:continuous:42ada4e462a2fb94",
    ];

    internal const string RamsesProtectionAndEntryCostAbilityId =
        "S01-0202:ability:static:76a4a87caae11a73";

    private static readonly L12LifecycleProfile SummonTurnCounterProtection =
        new("continuous:summon-turn-counter-protection",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["definition"] = "L12StructuredCardSemantics.HasSummonTurnCounterTacticProtection",
                ["current-round-condition"] = "L12StructuredCardRules.HasSummonTurnCounterTacticProtection",
                ["response-candidate-and-submit"] = "IsProtectedFromCounterTactics",
                ["delegated-entry-inheritance"] = "ResolveBatch6JAEnterEffect",
                ["legacy-delegated-entry-inheritance"] = "TryContinueS1Faction",
            },
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["no-target"] = "持续保护不选择对象；它只过滤会影响受保护效果的反击响应。",
                ["negated"] = "保护本身是登场回合持续规则，不独立入栈；不能先无效保护再响应受保护效果。",
                ["payment-cancel"] = "保护本身没有费用；反击战术是否支付费用由其自身协议处理。",
                ["target-invalidated"] = "没有效果目标；每次响应候选与提交均按当前堆叠来源和回合复验。",
                ["multi-target-applicability"] = "每个被转发的登场效果分别携带保护标记，不把多段效果合并为一个响应对象。",
            })
        {
            AdditionalChecks = ["summon-round", "four-response-types", "delegated-entry", "expiry",
                "anonymous-availability", "reconnect-derived-state"],
        };

    private static readonly L12LifecycleProfile RamsesProtectionAndEntryCost =
        new("continuous:ramses-protection-and-entry-cost",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["definition"] = "L12StructuredCardSemantics.HasSummonTurnCounterTacticProtection",
                ["current-round-condition"] = "L12StructuredCardRules.HasSummonTurnCounterTacticProtection",
                ["response-candidate-and-submit"] = "IsProtectedFromCounterTactics",
                ["delegated-entry-inheritance"] = "ResolveBatch6JAEnterEffect",
                ["legacy-delegated-entry-inheritance"] = "TryContinueS1Faction",
                ["entry-cost-definition"] = "L12StructuredCardSemantics.PrintedEntryCostRule",
                ["entry-cost-calculation"] = "PrintedEntryCostModifier",
                ["combined-play-cost"] = "GetPlayCostWithSigurdDiscount",
                ["button-and-snapshot"] = "SnapshotHand",
                ["resource-payment"] = "EnsurePlayResourcePaymentChoice",
            },
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["no-target"] = "持续保护与持续减费均不创建效果对象。",
                ["negated"] = "两项持续规则都不独立入栈，不能作为一次效果被无效。",
                ["payment-cancel"] = "保护没有费用；登场资源支付取消由公共手牌打出协议处理。",
                ["target-invalidated"] = "保护按当前回合和堆叠来源复验；减费在支付提交时按当前场上陵墓守卫复算。",
                ["multi-target-applicability"] = "减费只计算当前手牌实例；转发的每个登场效果分别携带保护标记。",
            })
        {
            AdditionalChecks = [.. SummonTurnCounterProtection.AdditionalChecks, "entry-cost-condition-false",
                "entry-cost-display-and-payment-parity", "entry-cost-zero-floor"],
        };

    // ================= P0 长尾归属批：共享管线档案 =================
    // 映射来源：temp/longtail-mapping.md（段→管线档案）。段序号以台账
    // docs/l12/EFFECT-ABILITY-INVENTORY.md 第一列为准；映射编写于较早分段基线，若干卡的
    // 段序号已漂移（莫瑞甘#1→#2、梅林#1/#2→#4/#5、八尺琼勾玉#1/#2→#3/#4、
    // 匠神锻造炉#1/#2→#3/#4、狮心王#2/#3→#3/#4、古斯塔夫#1/#2→#2/#3 等），
    // 以下一律按台账当前精确 ID 绑定。归属不等于验收：档案只声明已核实的共享管线入口与逐卡分支点。

    // A. 共享主动管线。主神/主城/代币的印刷主动行在台账多以 static 文本段出现（分段器不识别
    // 主神区主动句式），运行时统一走 BeginActiveAbility → CommitActiveAbilityCore → PushEffect
    // → ResolveActiveEffect，次数/资格由 L12ActiveUsageRules 注册表承载。
    // S02-06S6 十字军东征台账标 after-attack，但运行时是「可消耗X符文」三模式主动能力，以台账 ID 绑定。
    // S02-06S1 符文代币无独立卡实体，能力由 S02-06C1 的 runeUse 承载，此处按合成代币身份归账。
    // 梅林/八尺琼勾玉的 granted 段与匠神锻造炉的 mode-* 段是各自主动能力的模式分支，随父能力归入本族；
    // 须佐之男#2 台账标 attack（进攻时兵力+2000 与付费置入草薙剑同行），其付费分支走主动管线；
    // 神圣伽锁#3 是叠放门禁下对方可用的主动弃置出口，同属共享主动管线。
    // 补绑：孟婆/洛基的二选一主动行（mengpo-choice/loki 注册表组）、阿尔维达的弃置登场主动、
    // 安卡神碑的 mode-ready-guard/mode-rest-and-draw 段（ankhReady/ankhDraw，台账按 mode-* 登记）随父主动归入本族。
    private static readonly (string Id, string Phrase)[] PipelineActiveEffectChecks =
    [
        ("S01-01M1:ability:static:c03878ecc263c0e6", "可消耗1士气：抽取1张牌"),
        ("S01-01M1:ability:static:d024f673ff236321", "可返还4士气"),
        ("S01-02D1:ability:static:dbf8222a61a31140", "公开牌库顶部3张牌"),
        ("S01-02D1:ability:static:0c86a6851cf9d2ce", "返回所有者牌库底部"),
        ("S01-02M3:ability:static:705baec08fc6bc02", "本回合兵力-1000"),
        ("S01-03D1:ability:static:d89d0b3dade7b6c8", "手牌所有【阿斯加德】军团本回合费用-1"),
        ("S01-03D1:ability:static:342ed2c72fcd22aa", "弃置牌库顶部2张牌"),
        ("S01-03M1:ability:static:d047647f18d541e4", "选择墓地2张牌"),
        ("S01-04D1:ability:static:fcd47c32a0a46e1a", "登场费用-2"),
        ("S01-04D1:ability:static:3c467d3eba318af6", "对方所有军团在本回合费用-1"),
        ("S01-04M1:ability:static:2c285709f5669922", "本回合费用-1"),
        ("S01-04M1:ability:static:51c3f1e1976210f8", "我方最多2张士气转为活跃"),
        ("S01-04M2:ability:static:ce8699cac703af1c", "本回合位于前排"),
        ("S01-04M2:ability:attack:ebb2054e23f75cd1", "草薙剑"),
        ("S01-0314:ability:active:a923615d65edc8ea", "可弃置此军团"),
        ("S01-0417:ability:static:f10ff922d718f82e", "可消耗1士气"),
        ("S01-0004:ability:active:6f9f6988e1ea4be0", "击杀此军团"),
        ("S01-02M1:ability:static:53475d8f080332f1", "卡诺匹斯"),
        ("S01-02M2:ability:static:f3398615ac233d89", "可将此主宰替换"),
        ("S02-01M1:ability:active:4834e3b50d036f27", "将此主宰作为【斗士】军团"),
        ("S02-0301:ability:active:61c655977499e4be", "将此军团活跃登场"),
        ("S02-03M1:ability:active:54e6f9c40764f804", "血量不高于3"),
        ("S02-02M1:ability:active:014219b1c6c557fa", "登场费用-1"),
        ("S02-0205:ability:active:bf422a987e0ab5de", "本回合兵力-1000"),
        ("S02-06D1:ability:static:65b6607da57e5096", "可消耗2符文"),
        ("S02-06M1:ability:active:08922e53e852b78f", "击杀对方军团后转为活跃"),
        ("S02-06S1:ability:static:75769d93e0ca669f", "试炼+1"),
        ("S02-06S5:ability:static:5444a7c87e0351bd", "转为活跃"),
        ("S02-06S6:ability:after-attack:b158f5749a6c161e", "可消耗X符文"),
        ("S02-0404:ability:granted:2c2b9693ca8cf3b8", "骑兵位移"),
        ("S02-0404:ability:granted:e7c384ccba9ff2f3", "本回合位移过的军团"),
        ("S02-05M1:ability:active:6fe03f6c35407ac7", "获得强攻或震击"),
        ("S02-05M2:ability:active:e4b2c63a32960f8e", "查看牌库顶部3张牌"),
        ("S02-05D1:ability:active:1e9195c93dff4ee9", "可消耗并翻转2神力"),
        ("S02-0520:ability:mode-promotion-discount:98eb71c68928c091", "神力-1"),
        ("S02-0520:ability:mode-ready-after-kill:927badbb354c7607", "击杀对方军团后转为活跃"),
        ("S02-0603:ability:granted:8cd73702b7db90b0", "本回合兵力-3000"),
        ("S02-0603:ability:granted:ee3b46417c9fc4f7", "主动战术"),
        ("S02-0604:ability:trial-completed:9d25a05a194bedc1", "可弃置此军团"),
        ("S02-0013:ability:active-while-attached:f64dc7647e481c5f", "可消耗3士气"),
        ("S01-01M2:ability:static:f3ee48a69ee29306", "可选择以下一项"),
        ("S01-0307:ability:static:b89287bced985f8c", "可弃置此军团"),
        ("S01-03M2:ability:static:e3e85412fe04e44b", "可消耗1士气"),
        ("S01-0215:ability:mode-ready-guard:3e3294affff84b58", "休整的<陵墓守卫>转为活跃"),
        ("S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53", "转为休整：抽取1张牌"),
    ];

    internal static readonly string[] PipelineActiveEffectAbilityIds =
        PipelineActiveEffectChecks.Select(check => check.Id).ToArray();

    private static readonly L12LifecycleProfile PipelineActiveEffect = new("pipeline:active-effect",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["begin"] = "BeginActiveAbility",
            ["views"] = "BuildAbilityViews",
            ["commit"] = "CommitActiveAbilityCore",
            ["stack"] = "PushEffect",
            ["settle"] = "ResolveActiveEffect",
            ["usage"] = "L12ActiveUsageRules.Find",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["duplicate-submit"] = "重复提交由共享主动管线按当前费用/次数/状态复验。",
            ["payment-cancel"] = "支付取消与预付返还由共享提交管线统一兜底。",
        })
    {
        AdditionalChecks = ["per-card-branch", "authoritative-consumer"],
    };

    internal static readonly string[] PaidSelfStateTriggerAbilityIds =
    [
        "S02-0602:ability:enter:1ec4fb001f87c88e",
        "S02-0610:ability:after-trial:451b6d549a5c98c4",
    ];

    private static readonly L12LifecycleProfile PaidSelfStateTrigger = new("trigger:paid-self-state",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["candidate"] = "CreateTriggerCandidate",
            ["begin-declaration"] = "TryBeginTrialAdvanceTriggerDeclaration",
            ["cost-commit"] = "TryCompleteTrialAdvanceTriggerDeclaration",
            ["settlement"] = "TryResolveTrialAdvanceEffect",
            ["source-failure"] = "RecordResolutionFailure",
            ["presentation"] = "ResolveEffectPresentationSceneId",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal))
    {
        AdditionalChecks = ["paid-cost-preserved", "source-invalidated", "optional-decline"],
    };

    // B. 共享公开触发管线：候选入队 → 公开声明开始/完成 → 栈顶结算，批次规划由
    // L12TriggerBatchPlanner.Plan 承载。梅杰德#2/#3 与古斯塔夫#2/#3 是同一句被分段器拆开的两半；
    // S01-01D1 凌霄宝殿实为触发式复合计划（经 L12CompositeEffectPlans），以公开计划为准归入本族；
    // S02-0401 武田转活跃限制由 ReadyMoraleByEffect 权威事件管线承载，归入本族；
    // S02-06S5#1/S02-06S3#1 是试炼完成后的公开触发段（台账按 static 文本段登记）。
    // 补绑：信仰狂热者弃置触发（NotifyCardDiscarded 候选）、哮天犬·稚主宰士气返还触发
    // （QueueS2MasterMoraleReturnTriggers）、月读三段位移联动（RecordLegionMovement 候选，
    // 结算分支 tsukuyomiFollowMove/tsukuyomiFrontAttackBuff/tsukuyomiReadyMorale 在 L12S2RemainingEffects）、
    // 阿喀琉斯·晋升击杀授予前排挑衅（GrantTauntUntilNextOwnTurnEnd）、特洛伊木马进攻后响应
    // （ResolveS2TrojanHorseAfterAttack）、士气·天廷零士气恢复段（简单资源触发管线
    // TryResolveSimpleResourceTrigger，来源经 morale-identities 归一）。
    private static readonly (string Id, string Phrase)[] PipelinePublicTriggerChecks =
    [
        ("S01-01M1:ability:static:0924c3a5995ba164", "哮天犬·稚"),
        ("S01-01M1:ability:death:ee5adb706424f233", "追加1张休整的士气"),
        ("S01-04M2:ability:leave:4e83a7191108369b", "放回牌库顶部"),
        ("S01-0204:ability:leave:a59801f7c2874f4a", "陵墓守卫"),
        ("S01-0311:ability:static:3409dd9fa29f684f", "回合1次"),
        ("S01-0311:ability:after-attack:65ce2315ff4c0465", "转为活跃"),
        ("S01-0414:ability:static:e001b352b3693d93", "此军团"),
        ("S02-0001:ability:after-opponent-tactic:6d30a9b672845491", "回到手牌"),
        ("S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b", "增加1点血量"),
        ("S02-0305:ability:master-damaged:a4a2c92cad3ad28c", "可抽取1张牌"),
        ("S02-02M1:ability:friendly-legion-death:a366c9a7f75b5f29", "增殖的甲虫"),
        ("S02-06M2:ability:tactic-effect-resolved:e802cc6dcf73fe92", "试炼+1"),
        ("S01-02M3:ability:static:3a86c87f975d5851", "我方主宰因对方进攻或效果"),
        ("S01-02M3:ability:static:c339139cc1c9b00c", "受到伤害时"),
        ("S02-0103:ability:attack:607e6460eed6637b", "展示牌库顶部1张牌"),
        ("S02-0511:ability:attack:c367ee3457cbd5f4", "可消耗并翻转1神力"),
        ("S02-0516:ability:attack:077dc7337586413c", "双方各1张军团"),
        ("S02-0605:ability:attack:82a5bf2622bf4d20", "可消耗1士气"),
        ("S02-0607:ability:attack:25d5c998d14502d7", "可消耗X符文"),
        ("S02-0608:ability:attack:4581df1cc635dd68", "抵挡/支援"),
        ("S02-0608:ability:attack:0999d120e02e3c50", "侍从骑士"),
        ("S02-0612:ability:attack:c195f409c875e9eb", "进攻无损且兵力+2000"),
        ("S02-0617:ability:attack:c8dd6c6601a73ebb", "获得1符文"),
        ("S02-0611:ability:enter:0cc32f023a1b4f11", "直到下个我方回合开始前"),
        ("S02-0614:ability:enter:601eddfb8abbb8d2", "可选择"),
        ("S02-0602:ability:after-kill:e290e1e434e45531", "击杀时"),
        ("S02-06S4:ability:trial-complete:f95fed6f3ff0efc0", "【彼界】军团"),
        ("S02-06S5:ability:static:1e799825eedf3331", "本效果可重复发动"),
        ("S02-06S3:ability:static:3616e3ca17ffd729", "亚瑟王"),
        ("S01-01D1:ability:static:103012fd4239104f", "追加2张休整的士气"),
        ("S02-0401:ability:continuous:9601da1d8445f865", "无法因主宰效果转为活跃"),
        ("S02-0006:ability:discarded:89d3ee4207648aa1", "无视消耗触发1次"),
        ("S02-01S1:ability:master-morale-return:8d098fe32e7b253b", "可在前排活跃登场"),
        ("S02-04M1:ability:friendly-legion-moves:654df25d049352f7", "进行1格位移"),
        ("S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3", "本回合进攻时兵力+1000"),
        ("S02-04M1:ability:friendly-front-to-back:e46218b2ac936410", "将我方1张士气转为活跃"),
        ("S02-0503:ability:after-attack:e3ced12ddde14fdb", "获得 ABILITY 5"),
        ("S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba", "置入对方战场任意空位"),
        ("ST01-C1:ability:static:605b9aa3d8a1ed93", "士气为0张时"),
    ];

    internal static readonly string[] PipelinePublicTriggerAbilityIds =
        PipelinePublicTriggerChecks.Select(check => check.Id).ToArray();

    private static readonly L12LifecycleProfile PipelinePublicTrigger = new("pipeline:public-trigger",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["candidates"] = "QueueTriggerCandidates",
            ["begin-declaration"] = "TryBeginPublicTriggerDeclaration",
            ["complete-declaration"] = "TryCompletePublicTriggerDeclaration",
            ["settle"] = "ResolveTopStack",
            ["batch-plan"] = "L12TriggerBatchPlanner.Plan",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["duplicate-submit"] = "重复提交由触发批次协议复验。",
        })
    {
        AdditionalChecks = ["per-card-plan", "authoritative-consumer"],
    };

    // C. 共享打出管线：战术/手牌登场段统一走 PlayCard → 复合打出声明/校验 → ResolveTacticEffect，
    // 费用读 GetPlayCostWithSigurdDiscount。密米尔之泉#2 台账标 master-effect-damage-threshold，
    // 印刷上是战术的追加结算段，归本族；槲寄生#1 是手牌费用修正段，罗洛#1 是手牌特殊登场段。
    // 补绑：蛇眼西格德#1 墓地回库减费段（台账标 entry-discount，运行时是 PlayCard 的 sigurd 墓地费用分支）。
    private static readonly (string Id, string Phrase)[] PipelineHandPlayChecks =
    [
        ("S02-0012:ability:play:bafe1ab6a18493c0", "询问对方是否同意"),
        ("S02-0206:ability:play:ca021e5c16b59965", "兵力+3000"),
        ("S02-0206:ability:play:bd784d08e38e0ed8", "回合结束时弃置此军团"),
        ("S02-0307:ability:play:f9b21f21c30d2eb3", "弃置我方牌库顶部1张牌"),
        ("S02-0306:ability:master-effect-damage-threshold:978e2dc72d59418c", "累计2点"),
        ("S02-0405:ability:play:0a13775c2081e642", "牌库顶部5张牌"),
        ("S02-0405:ability:play:b03190adf1322a1a", "登场费用-2"),
        ("S02-0406:ability:play:35815c7115c7ce71", "选择ABILITY 2"),
        ("S02-0521:ability:play-additional:2b5a094468a81a7a", "可消耗并翻转2神力"),
        ("S02-0522:ability:play:a09dadaebc5e13de", "本回合兵力-3000"),
        ("S02-0522:ability:play-additional:49fb773d1512e5b3", "可消耗并翻转1神力"),
        ("S02-0620:ability:play:ac4a80f231805917", "获得1符文"),
        ("S02-0620:ability:play:c2e3d34e7ac83c86", "可消耗1士气"),
        ("S02-0621:ability:play:9a7d744018bd9e66", "圆桌骑士"),
        ("S02-0621:ability:play:ecdfaa719e9112ba", "兵力+2000"),
        ("S02-0622:ability:hand-play:5b5e4bf8f495f21a", "费用-2"),
        ("S02-0622:ability:play:d5226a525c565d25", "兵力-6000"),
        ("S02-0302:ability:hand-play:4e8ff9ea92325bac", "登场费用-1"),
        ("ST03-01:ability:entry-discount:373b8092202cdf17", "登场费用-1"),
    ];

    internal static readonly string[] PipelineHandPlayAbilityIds =
        PipelineHandPlayChecks.Select(check => check.Id).ToArray();

    private static readonly L12LifecycleProfile PipelineHandPlay = new("pipeline:hand-play",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["play"] = "PlayCard",
            ["composite-declaration"] = "BeginCompositeHandPlayDeclaration",
            ["composite-validation"] = "ValidateCompositeHandPlayDeclaration",
            ["settle"] = "ResolveTacticEffect",
            ["cost"] = "GetPlayCostWithSigurdDiscount",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["duplicate-submit"] = "重复提交由打出事务复验。",
            ["payment-cancel"] = "打出取消由公共打出事务兜底。",
        })
    {
        AdditionalChecks = ["per-card-flow", "authoritative-consumer"],
    };

    // D. 共享响应管线。备注：戏法师的傀儡（5 处硬编码资格谓词）与乾坤·阴
    // （CanUseS2CounterAtStack）的响应资格仍逐卡分支，未纳入响应身份注册表——后续收敛项。
    private static readonly (string Id, string Phrase)[] PipelineResponseChecks =
    [
        ("S02-0005:ability:opponent-attacks-master:806afb384f303aee", "将本次进攻目标改为此军团"),
        ("S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c", "展示牌库顶部1张牌"),
    ];

    internal static readonly string[] PipelineResponseAbilityIds =
        PipelineResponseChecks.Select(check => check.Id).ToArray();

    private static readonly L12LifecycleProfile PipelineResponse = new("pipeline:response",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["candidates"] = "LegalResponseSources",
            ["pool-timing"] = "IsPoolCounterResponseAtTiming",
            ["submit"] = "BeginSelectedStackResponse",
            ["settle"] = "ResolveTopStack",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["duplicate-submit"] = "重复提交由堆叠响应协议复验。",
        })
    {
        AdditionalChecks = ["capability-registry-pending", "authoritative-consumer"],
    };

    // E. 整行印刷声明段：「位于前排」获得挑衅＋对方回合兵力+1000（张飞另含登场费用-1 半句）。
    // 语义由同卡已归属的结构化拆分段（overlay/关键词族）承载，本档案只声明整行声明的消费出口。
    internal static readonly string[] FrontRowCompositeLineAbilityIds =
    [
        "S01-0107:ability:static:715fe715dcb8ea28",
        "S01-0312:ability:static:b2e1a67373ad69cc",
        "S01-0204:ability:static:4108715d77479b32",
    ];

    private static readonly L12LifecycleProfile FrontRowCompositeLine = new("declaration:front-row-composite-line",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["taunt"] = "L12StructuredCardRules.HasTaunt",
            ["troops"] = "L12StructuredCardRules.OpponentTurnFrontTroopsBonus",
            ["entry-cost"] = "PrintedEntryCostModifier",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "位置条件的持续关键词授予不独立入栈，不能作为一次效果被无效。",
            ["payment-cancel"] = "本段没有费用或支付Prompt。",
            ["no-target"] = "本段不选择效果对象，只按来源当前行位决定是否授予挑衅。",
            ["target-invalidated"] = "无效果目标；进攻候选与提交复验每次都读取当前行位与关键词状态。",
            ["duplicate-submit"] = "持续授予读取无副作用；重复进攻提交仍由公共战斗协议拒绝。",
        })
    {
        AdditionalChecks = ["row-condition-current", "authoritative-consumer", "structured-split-siblings"],
    };

    // F. 天灾管线段：ST 天灾的触发段与 S1 天灾的回合开始/结束段统一走天灾触发/结算管线；
    // 与 disaster:continuous-rule（持续规则查询层）互补，不重叠。
    internal static readonly string[] PipelineDisasterAuthorityAbilityIds =
    [
        "ST-DS01:ability:disaster:00612b44a6a3ac99",
        "ST-DS03:ability:disaster:c974ef724419ccdf",
        "S01-DS02:ability:turn-end:9d632a451357ff71",
        "S01-DS10:ability:turn-start:a790e35d0012c86f",
    ];

    private static readonly L12LifecycleProfile PipelineDisasterAuthority = new("pipeline:disaster-authority",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["trigger"] = "BeginDisasterTrigger",
            ["settle"] = "ResolveDisasterEffect",
            ["turn-start"] = "ResolveTurnStartDisasterEffectIfNeeded",
            ["turn-end"] = "ResolveEndPhaseDisasterEffect",
            ["damage"] = "DamageMasterNonLethalFromDisaster",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "天灾不可响应不可无效。",
            ["no-target"] = "天灾效果按印刷文本生效，不创建玩家对象选择Prompt。",
            ["duplicate-submit"] = "天灾管线内不重复结算。",
        })
    {
        AdditionalChecks = ["authoritative-consumer"],
    };

    // G. 逐卡专用出口。
    internal const string ValkyrieDrawPhaseAbilityId = "S01-03M1:ability:static:f1ba346550e4decc";

    private static readonly L12LifecycleProfile ValkyrieDrawPhase = new("rule:valkyrie-draw-phase",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["turn-start"] = "ContinueAutomaticTurnStart",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["no-target"] = "抽牌阶段替代按牌库当前顺序逐张弃置，不创建对象选择。",
            ["negated"] = "阶段规则行动不入效果堆叠，不能被响应或无效。",
            ["target-invalidated"] = "没有已声明效果对象；每次弃置只读取当时牌库顶。",
            ["duplicate-submit"] = "没有玩家发动命令；同一回合的自动阶段推进由回合状态机执行一次。",
            ["payment-cancel"] = "本段没有费用或支付Prompt。",
        })
    {
        AdditionalChecks = ["authoritative-consumer"],
    };

    internal const string IsisSetupAbilityId = "S01-02M1:ability:static:68187ab0edb25d9c";

    private static readonly L12LifecycleProfile IsisSetup = new("rule:isis-setup",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["setup"] = "PrepareLibrariesAndHands",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["no-target"] = "开局规则直接把固定的<复苏的奥西里斯>置入所属玩家墓地，不创建对象选择。",
            ["negated"] = "开局规则行动不入效果堆叠，不能被响应或无效。",
            ["target-invalidated"] = "没有已声明效果对象；所属玩家与固定卡身份在建局时已确定。",
            ["duplicate-submit"] = "没有玩家提交命令；建局只构造一次初始状态，恢复读取持久化结果而不重复执行。",
            ["payment-cancel"] = "本段没有费用或支付Prompt。",
        })
    {
        AdditionalChecks = ["authoritative-consumer"],
    };

    internal const string MasterLegionReturnAbilityId = "S02-01M1:ability:leave:cf42cfffe1b9b9bc";

    private static readonly L12LifecycleProfile MasterLegionReturn = new("leave:master-legion-return",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["departure"] = "CompleteMasterLegionDeparture",
            ["morale-trigger"] = "TryResolveSimpleResourceTrigger",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["no-target"] = "离场替代只在当前作为军团的孙悟空实际离场时执行，不存在独立发动或对象候选阶段。",
            ["negated"] = "返回主宰区是离场规则替代，不能被响应或无效；返回后的士气追加才是独立可选效果。",
            ["target-invalidated"] = "没有入栈后等待复验的已声明对象；离场动作持有当前实例并立即应用区域替代。",
            ["duplicate-submit"] = "离场替代没有独立玩家提交；同一实例移出战场后不能再次执行同一次离场。",
            ["payment-cancel"] = "离场替代没有费用；返回后的可选士气效果另由资源触发生命周期管理。",
        })
    {
        AdditionalChecks = ["authoritative-consumer"],
    };

    internal const string AttachedTacticsDiscardAbilityId = "S02-0013:ability:host-leaves-artifact:b2720c3b205be910";

    private static readonly L12LifecycleProfile AttachedTacticsDiscard = new("leave:attached-tactics-discard",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["discard"] = "DiscardAttachedCards",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["no-target"] = "宿主圣物实际离开圣物区时按当前叠放关系自动弃置，不存在独立发动或对象候选阶段。",
            ["negated"] = "宿主离场后的叠放卡清理是区域规则处理，不创建可响应或无效的效果。",
            ["target-invalidated"] = "没有入栈后等待复验的对象；清理时枚举宿主当前仍叠放的卡牌实例。",
            ["duplicate-submit"] = "清理没有独立玩家提交，完成后清空叠放集合，同一关系不能重复弃置。",
            ["payment-cancel"] = "宿主离场清理没有费用或支付Prompt。",
        })
    {
        AdditionalChecks = ["authoritative-consumer"],
    };

    internal const string AnderstorpDamageFloorAbilityId = "S02-0305:ability:master-damaged:4c8ce907eed1f778";

    private static readonly L12LifecycleProfile AnderstorpDamageFloor = new("replacement:anderstorp-damage-floor",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["damage-floor"] = "AdjustAnderstorpRingDamage",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["no-target"] = "伤害替代只读取本回合主宰首次受伤事实，不创建对象选择。",
            ["negated"] = "替代规则在伤害入口内裁定，不创建可响应或无效的独立效果。",
            ["target-invalidated"] = "没有已声明效果对象；每次伤害按当前控制者回合与累计受伤事实复验。",
            ["duplicate-submit"] = "替代规则没有独立提交命令；重复进攻或伤害由其原始动作协议处理。",
            ["payment-cancel"] = "本段没有费用或支付Prompt。",
        })
    {
        AdditionalChecks = ["authoritative-consumer"],
    };

    internal static readonly string[] LakeLadySwordAbilityIds =
    [
        "S02-06S3:ability:static:f7e019a543066afd",
        "S02-06S3:ability:death:84330d935c195208",
    ];

    private static readonly L12LifecycleProfile LakeLadySword = new("replacement:lake-lady-sword",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["replacement"] = "TryApplyLakeLadySwordReplacement",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["no-target"] = "持续替代只在已完成试炼、当前亚瑟王及其当前叠放王者之剑同时满足时同步应用，不建立对象候选。",
            ["negated"] = "持续替代属于致命离场前的规则处理，不进入效果堆叠，不能被响应或无效。",
            ["target-invalidated"] = "没有声明后等待结算的对象；每次致命检查都读取当前亚瑟王、试炼完成状态和当前叠放关系。",
            ["duplicate-submit"] = "替代没有独立玩家提交；支付后王者之剑已离开叠放区，同一次致命检查由战斗决定表防止重复。",
            ["payment-cancel"] = "移除当前唯一的王者之剑是必行费用，满足替代条件时自动支付且没有取消Prompt。",
        })
    {
        AdditionalChecks = ["authoritative-consumer"],
    };

    // 「可携带1张已完成的试炼」备注：「已完成」语义未实现（开局试炼不标记 TrialCompleted，
    // 且彼界主宰默认容量已为 1）——疑似缺口，归属只声明容量与构筑校验出口。
    internal static readonly string[] TrialCapacityAbilityIds =
    [
        "S02-06D1:ability:static:b173428fa383ae26",
        "S02-06M2:ability:rule:f86cd3914a10b001",
    ];

    private static readonly L12LifecycleProfile TrialCapacity = new("rule:trial-capacity",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["capacity"] = "L12SpecialDeckRules.TrialCapacity",
            ["validator"] = "L12DeckValidator.TryValidate",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal))
    {
        AdditionalChecks = ["authoritative-consumer"],
    };

    internal static readonly string[] RuinedRitualModesAbilityIds =
    [
        "S02-0016:ability:granted:dfd998389876f15e",
        "S02-0016:ability:granted:df2c369f365d4497",
    ];

    private static readonly L12LifecycleProfile RuinedRitualModes = new("granted:ruined-ritual-modes",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["response-commit"] = "CommitS2CounterResponse",
            ["settle"] = "ResolveS2CounterEffect",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal))
    {
        AdditionalChecks = ["authoritative-consumer"],
    };

    internal static readonly string[] PrayerModesAbilityIds =
    [
        "S02-0012:ability:granted:e5bb0cce96aba072",
        "S02-0012:ability:granted:1c5ef0343f70615c",
    ];

    private static readonly L12LifecycleProfile PrayerModes = new("granted:prayer-modes",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["settle"] = "TryResolveS2UniversalTactic",
            ["preview"] = "BeginPrayerPublicPreview",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal))
    {
        AdditionalChecks = ["authoritative-consumer"],
    };

    internal static readonly string[] TenkaModesAbilityIds =
    [
        "S02-0406:ability:granted:6aa04cbf27f6b4b7",
        "S02-0406:ability:granted:4f1f5a1d4791b5ef",
        "S02-0406:ability:granted:4f26e688b66affd4",
    ];

    private static readonly L12LifecycleProfile TenkaModes = new("granted:tenka-modes",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["settle"] = "TryResolveS2FactionTactic",
            ["attack-bonus"] = "Attack",
            ["free-move"] = "Move",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal))
    {
        AdditionalChecks = ["authoritative-consumer"],
    };

    internal const string LancelotKillModesAbilityId = "S02-0602:ability:granted:7a7545729484412a";

    private static readonly L12LifecycleProfile LancelotKillModes = new("granted:lancelot-kill-modes",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["settle"] = "TryResolveTrialAdvanceEffect",
            ["presentation"] = "ResolveEffectPresentationSceneId",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["target-invalidated"] = "推进试炼是全局结算，不声明效果对象；来源离场不改变已公开选择。",
        })
    {
        AdditionalChecks = ["authoritative-consumer"],
    };

    internal const string ConstanceModesAbilityId = "S02-0614:ability:granted:45f31f84b8f800cd";

    private static readonly L12LifecycleProfile ConstanceModes = new("granted:constance-modes",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["settle"] = "TryResolveTrialAdvanceEffect",
            ["presentation"] = "ResolveEffectPresentationSceneId",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["target-invalidated"] = "推进试炼是全局结算，不声明效果对象；来源在声明时休整后不再作为结算目标。",
        })
    {
        AdditionalChecks = ["authoritative-consumer"],
    };

    internal const string AvalonTurnStartAbilityId = "S02-06D1:ability:turn-start:97dca04b36fe51bf";

    private static readonly L12LifecycleProfile AvalonTurnStart = new("turn-start:avalon",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["queue"] = "QueueAvalonTurnStart",
            ["settle"] = "TryResolveTrialAdvanceEffect",
            ["presentation"] = "ResolveEffectPresentationSceneId",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["target-invalidated"] = "本段没有声明对象；结算时仅处理当前未完成试炼，并独立获得1符文。",
        })
    {
        AdditionalChecks = ["authoritative-consumer"],
    };

    // 王者之剑叠放持续段：叠放在我方<亚瑟王>下方时使其原本兵力+1000并获得强攻；
    // 兵力经共享持续重算出口、强攻身份经结构语义注册表读取。
    internal const string KingsSwordAttachedAbilityId = "S02-06S2:ability:static:0f86ac377c8c63ee";

    private static readonly L12LifecycleProfile KingsSwordAttached = new("continuous:kings-sword-attached",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["troops"] = "GetTurnAndPositionContinuousTroops",
            ["strong-attack"] = "L12StructuredCardSemantics.GrantsStrongAttackWhileAttached",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "叠放持续规则不独立入栈，不能作为一次效果被无效。",
            ["payment-cancel"] = "本段没有费用或支付Prompt。",
            ["no-target"] = "本段不选择效果对象，只按叠放关系作用于被叠放的<亚瑟王>。",
            ["target-invalidated"] = "无效果目标；每次重算读取当前叠放关系与位置。",
            ["duplicate-submit"] = "持续规则读取无副作用。",
        })
    {
        AdditionalChecks = ["attached-source-current", "authoritative-consumer"],
    };

    // 萨拉丁整行印刷声明：「回合1次位移」+「前排相邻太阳城进攻增益」复合行；
    // 位移语义由共享骑兵位移规则动作承载，进攻被动由 S1 阵营攻击被动入口承载。
    internal const string SaladinLineAbilityId = "S01-0206:ability:static:cb39cf42a7feea7b";

    private static readonly L12LifecycleProfile SaladinLine = new("declaration:saladin-line",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["move"] = "CavalryMove",
            ["attack-passive"] = "ApplyS1FactionAttackPassives",
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["negated"] = "持续印刷声明不独立入栈，不能作为一次效果被无效。",
            ["payment-cancel"] = "本段没有费用或支付Prompt；位移动作本身无卡牌费用。",
            ["no-target"] = "本段不选择效果对象；位移目的地与相邻增益目标按公共规则候选判定。",
            ["target-invalidated"] = "无效果目标；位移与进攻增益每次按当前场上状态复验。",
            ["duplicate-submit"] = "持续声明读取无副作用；重复位移提交由公共动作协议复验。",
        })
    {
        AdditionalChecks = ["composite-line-declaration", "authoritative-consumer"],
    };

    private static void ValidateOwners(L12LifecycleProfile profile)
    {
        foreach (var owner in profile.RuntimeOwners.Values)
        {
            var parts = owner.Split('.');
            var type = parts.Length == 1 ? typeof(L12GameEngine)
                : parts[0] == nameof(L12StructuredCardRules) ? typeof(L12StructuredCardRules)
                : parts[0] == nameof(L12StructuredCardSemantics) ? typeof(L12StructuredCardSemantics)
                : parts[0] == nameof(L12SpecialDeckRules) ? typeof(L12SpecialDeckRules)
                : parts[0] == nameof(L12MoraleIdentityCatalog) ? typeof(L12MoraleIdentityCatalog)
                : parts[0] == nameof(L12S2ZoneOps) ? typeof(L12S2ZoneOps)
                : parts[0] == nameof(L12ActiveUsageRules) ? typeof(L12ActiveUsageRules)
                : parts[0] == nameof(L12ActiveDisasterRules) ? typeof(L12ActiveDisasterRules)
                : parts[0] == nameof(L12CardNameUsageRules) ? typeof(L12CardNameUsageRules)
                : parts[0] == nameof(L12TriggerBatchPlanner) ? typeof(L12TriggerBatchPlanner)
                : parts[0] == nameof(L12DeckValidator) ? typeof(L12DeckValidator)
                : throw new InvalidOperationException($"Unknown lifecycle owner type: {owner}");
            if (!type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                    .Any(method => method.Name == parts[^1]))
                throw new InvalidOperationException($"Missing reviewed lifecycle owner: {owner}");
        }
    }

    internal static IReadOnlyDictionary<string, L12LifecycleProfile> Read(L12Catalog catalog)
    {
        var abilities = catalog.AtomicEffects.All.SelectMany(card => card.Abilities)
            .ToDictionary(ability => ability.AbilityId, StringComparer.Ordinal);
        ValidateOwners(DesertHandSummon);
        ValidateOwners(CounterDeployment);
        ValidateOwners(StrictHandEntry);
        ValidateOwners(NativeCavalry);
        ValidateOwners(PrintedRanged);
        ValidateOwners(PaidExtendedRange);
        ValidateOwners(ActiveRest);
        ValidateOwners(SelfDamageEntryDiscount);
        foreach (var profile in CombatKeywordProfiles.Values) ValidateOwners(profile);
        foreach (var profile in GrantedKeywordProfiles.Values) ValidateOwners(profile);
        ValidateOwners(FrontRowTauntOverlay);
        ValidateOwners(FrontRowKeywordTroops);
        ValidateOwners(BackRowAttackTroopsSet);
        ValidateOwners(GrantedGainRune);
        ValidateOwners(MoraleActiveEffect);
        ValidateOwners(MoraleResourceIdentity);
        ValidateOwners(ImmortalReplacement);
        ValidateOwners(AfterKillPiercing);
        ValidateOwners(FrontRowKeywordGrant);
        ValidateOwners(GrantedFrontRowTauntOnKill);
        ValidateOwners(DuelCombatLine);
        ValidateOwners(SimpleContinuousTroops);
        ValidateOwners(RestedFreeFrontBackMove);
        ValidateOwners(NegateResponse);
        ValidateOwners(MercenaryHandBlock);
        ValidateOwners(DisasterContinuousRule);
        ValidateOwners(LethalReplacementOffer);
        ValidateOwners(CardNameOncePerTurn);
        ValidateOwners(UniversalFactionMapping);
        ValidateOwners(ThorHammerMasterGate);
        ValidateOwners(GameSetupRule);
        ValidateOwners(CooperativeSupport);
        ValidateOwners(TrialValue);
        ValidateOwners(PromotionEntry);
        ValidateOwners(StructuredContinuousCombatRule);
        ValidateOwners(PrintedEntryCost);
        ValidateOwners(StructuredHandCost);
        ValidateOwners(HandPlayBlock);
        ValidateOwners(RelicZoneLimitExempt);
        ValidateOwners(OutOfDeckGraveyardLifecycle);
        ValidateOwners(MoraleZoneResource);
        ValidateOwners(MoraleFaceFlip);
        ValidateOwners(OpponentTurnFieldRule);
        ValidateOwners(TombGuardMasterAura);
        ValidateOwners(SummonTurnCounterProtection);
        ValidateOwners(RamsesProtectionAndEntryCost);
        ValidateOwners(PipelineActiveEffect);
        ValidateOwners(PaidSelfStateTrigger);
        ValidateOwners(PipelinePublicTrigger);
        ValidateOwners(PipelineHandPlay);
        ValidateOwners(PipelineResponse);
        ValidateOwners(FrontRowCompositeLine);
        ValidateOwners(PipelineDisasterAuthority);
        ValidateOwners(ValkyrieDrawPhase);
        ValidateOwners(IsisSetup);
        ValidateOwners(MasterLegionReturn);
        ValidateOwners(AttachedTacticsDiscard);
        ValidateOwners(AnderstorpDamageFloor);
        ValidateOwners(LakeLadySword);
        ValidateOwners(TrialCapacity);
        ValidateOwners(RuinedRitualModes);
        ValidateOwners(PrayerModes);
        ValidateOwners(TenkaModes);
        ValidateOwners(LancelotKillModes);
        ValidateOwners(ConstanceModes);
        ValidateOwners(AvalonTurnStart);
        ValidateOwners(KingsSwordAttached);
        ValidateOwners(SaladinLine);
        var bindings = new Dictionary<string, L12LifecycleProfile>(StringComparer.Ordinal);
        if (!abilities.TryGetValue(DesertHandSummonAbilityId, out var desertHandSummon)
            || desertHandSummon.CardId != "S02-0207" || desertHandSummon.Trigger != "play"
            || !desertHandSummon.Text.Contains("天灾等级与弃置军团数量相同", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed desert hand-summon profile: {DesertHandSummonAbilityId}");
        bindings.Add(DesertHandSummonAbilityId, DesertHandSummon);
        foreach (var id in CounterDeploymentAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var counterDeployment)
                || counterDeployment.Text is null || !counterDeployment.Text.Contains("反击战术", StringComparison.Ordinal)
                || counterDeployment.ExecutionModel is not ("spell" or "triggered"))
                throw new InvalidOperationException($"Stale reviewed counter-deployment profile: {id}");
            bindings.Add(id, CounterDeployment);
        }
        foreach (var id in StrictHandEntryAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var strictHandEntry)
                || strictHandEntry.CardId is not ("S01-0105" or "S01-0116" or "S01-0213")
                || strictHandEntry.ExecutionModel is not ("continuous" or "triggered"))
                throw new InvalidOperationException($"Stale reviewed strict hand-entry profile: {id}");
            bindings.Add(id, StrictHandEntry);
        }
        foreach (var id in NativeCavalryAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.ExecutionModel != "rule-action"
                || !L12EffectPresentationScenes.IsCavalryMoveRuleAction(ability))
                throw new InvalidOperationException($"Stale reviewed rule-action profile: {id}");
            bindings.Add(id, NativeCavalry);
        }
        // New matching cards remain review work; do not silently grant old evidence to them.
        var matching = abilities.Values.Where(ability => ability.ExecutionModel == "rule-action"
            && L12EffectPresentationScenes.IsCavalryMoveRuleAction(ability)).Select(ability => ability.AbilityId);
        if (!matching.ToHashSet(StringComparer.Ordinal).SetEquals(NativeCavalryAbilityIds))
            throw new InvalidOperationException("Native cavalry family changed; review its per-ability bindings.");
        foreach (var id in PrintedRangedAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.ExecutionModel != "continuous"
                || !ability.Text.Contains("进攻距离+1，远程进攻无损", StringComparison.Ordinal)
                || ability.CostText is not null)
                throw new InvalidOperationException($"Stale reviewed printed-range profile: {id}");
            bindings.Add(id, PrintedRanged);
        }
        var printedRanged = abilities.Values.Where(ability => ability.ExecutionModel == "continuous"
            && ability.Text.Contains("进攻距离+1，远程进攻无损", StringComparison.Ordinal)).Select(ability => ability.AbilityId);
        if (!printedRanged.ToHashSet(StringComparer.Ordinal).SetEquals(PrintedRangedAbilityIds))
            throw new InvalidOperationException("Printed ranged family changed; review its per-ability bindings.");
        foreach (var id in PaidExtendedRangeAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.ExecutionModel != "activated"
                || ability.Trigger != "active" || L12StructuredCardSemantics.ExtendedRangeRule(ability.CardId) is null)
                throw new InvalidOperationException($"Stale reviewed paid extended-range profile: {id}");
            bindings.Add(id, PaidExtendedRange);
        }
        var paidExtendedRange = abilities.Values.Where(ability => ability.Trigger == "active"
            && L12StructuredCardSemantics.ExtendedRangeRule(ability.CardId) is not null).Select(ability => ability.AbilityId);
        if (!paidExtendedRange.ToHashSet(StringComparer.Ordinal).SetEquals(PaidExtendedRangeAbilityIds))
            throw new InvalidOperationException("Paid extended-range family changed; review its per-ability bindings.");
        foreach (var id in ActiveRestAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.Trigger != "active"
                || !ability.Text.Contains("主动休整", StringComparison.Ordinal)
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.RestSource && atom.Stage == "cost"))
                throw new InvalidOperationException($"Stale reviewed active-rest profile: {id}");
            bindings.Add(id, ActiveRest);
        }
        var activeRest = abilities.Values.Where(ability => ability.Trigger == "active"
            && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.RestSource && atom.Stage == "cost"))
            .Select(ability => ability.AbilityId);
        if (!activeRest.ToHashSet(StringComparer.Ordinal).SetEquals(ActiveRestAbilityIds))
            throw new InvalidOperationException("Active-rest family changed; review its per-ability bindings.");
        foreach (var id in SelfDamageEntryDiscountAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.Trigger != "hand-play"
                || ability.ExecutionModel != "special-summon"
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.DamageMaster && atom.Stage == "cost"
                    && atom.Parameters.GetValueOrDefault("semantic") == "self-damage-entry-discount-cost"))
                throw new InvalidOperationException($"Stale reviewed self-damage entry-discount profile: {id}");
            bindings.Add(id, SelfDamageEntryDiscount);
        }
        var selfDamageEntryDiscounts = abilities.Values.Where(ability => ability.Trigger == "hand-play"
            && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.DamageMaster && atom.Stage == "cost"
                && atom.Parameters.GetValueOrDefault("semantic") == "self-damage-entry-discount-cost"))
            .Select(ability => ability.AbilityId);
        if (!selfDamageEntryDiscounts.ToHashSet(StringComparer.Ordinal).SetEquals(SelfDamageEntryDiscountAbilityIds))
            throw new InvalidOperationException("Self-damage entry-discount family changed; review its per-ability bindings.");
        foreach (var (keyword, ids) in CombatKeywordDefinitionAbilityIds)
        {
            var profile = CombatKeywordProfiles[keyword];
            foreach (var id in ids)
            {
                if (!abilities.TryGetValue(id, out var ability) || ability.Trigger != "keyword-definition"
                    || ability.ExecutionModel is not ("keyword-definition" or "granted-continuous")
                    || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Keyword
                        && atom.Parameters.GetValueOrDefault("keywordRef") == keyword)
                    || !L12StructuredCardRules.HasKeywordDefinition(ability.CardId, keyword))
                    throw new InvalidOperationException($"Stale reviewed {keyword} keyword definition: {id}");
                bindings.Add(id, profile);
            }
        }
        var reviewedKeywordIds = CombatKeywordDefinitionAbilityIds.Values.SelectMany(ids => ids)
            .ToHashSet(StringComparer.Ordinal);
        var keywordDefinitions = abilities.Values.Where(ability => ability.Trigger == "keyword-definition")
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!keywordDefinitions.SetEquals(reviewedKeywordIds))
            throw new InvalidOperationException("Combat keyword-definition family changed; review its per-ability bindings.");
        foreach (var (keyword, ids) in GrantedKeywordDefinitionAbilityIds)
        {
            var profile = GrantedKeywordProfiles[keyword];
            foreach (var id in ids)
            {
                if (!abilities.TryGetValue(id, out var ability) || ability.Trigger != "granted"
                    || ability.ExecutionModel is not ("granted-continuous" or "granted-effect")
                    || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Keyword
                        && atom.Parameters.GetValueOrDefault("keywordRef") == keyword)
                    || !L12StructuredCardRules.HasPrintedKeywordReference(ability.CardId, keyword))
                    throw new InvalidOperationException($"Stale reviewed granted {keyword} keyword definition: {id}");
                bindings.Add(id, profile);
            }
        }
        var reviewedGrantedKeywordIds = GrantedKeywordDefinitionAbilityIds.Values.SelectMany(ids => ids)
            .ToHashSet(StringComparer.Ordinal);
        var grantedKeywordDefinitions = abilities.Values.Where(ability => ability.Trigger == "granted"
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Keyword))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!grantedKeywordDefinitions.SetEquals(reviewedGrantedKeywordIds))
            throw new InvalidOperationException("Granted keyword-definition family changed; review its per-ability bindings.");
        foreach (var id in FrontRowTauntOverlayAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.Trigger != "static"
                || ability.ExecutionModel != "continuous"
                || !ability.Text.Contains("「位于前排」获得【挑衅】", StringComparison.Ordinal)
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Condition
                    && atom.Parameters.GetValueOrDefault("expression") == "source.row=front")
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Keyword
                    && atom.Parameters.GetValueOrDefault("keywordRef") == "taunt"))
                throw new InvalidOperationException($"Stale reviewed front-row taunt overlay: {id}");
            bindings.Add(id, FrontRowTauntOverlay);
        }
        var frontRowTaunts = abilities.Values.Where(ability => ability.ExecutionModel == "continuous"
                && ability.Text.Contains("「位于前排」获得【挑衅】", StringComparison.Ordinal))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!frontRowTaunts.SetEquals(FrontRowTauntOverlayAbilityIds))
            throw new InvalidOperationException("Front-row taunt overlay family changed; review its per-ability bindings.");
        foreach (var id in FrontRowKeywordTroopsAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.Trigger != "continuous"
                || ability.ExecutionModel != "continuous"
                || !ability.Text.Contains("「位于前排」获得ABILITY", StringComparison.Ordinal)
                || !ability.Text.Contains("对方回合此军团兵力+1000", StringComparison.Ordinal)
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Condition
                    && atom.Parameters.GetValueOrDefault("expression")?.Contains("source.row=front", StringComparison.Ordinal) == true)
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.ModifyTroops
                    && atom.Parameters.GetValueOrDefault("value") == "1000")
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.SetState
                    && atom.Parameters.GetValueOrDefault("abilityRef") is not null)
                || L12StructuredCardRules.OpponentTurnFrontTroopsBonus(ability.CardId) != 1000
                || !L12StructuredCardRules.HasPrintedKeywordReference(ability.CardId, "taunt"))
                throw new InvalidOperationException($"Stale reviewed front-row keyword/troops line: {id}");
            bindings.Add(id, FrontRowKeywordTroops);
        }
        var frontRowKeywordTroops = abilities.Values.Where(ability => ability.ExecutionModel == "continuous"
                && ability.Text.Contains("「位于前排」获得ABILITY", StringComparison.Ordinal)
                && ability.Text.Contains("对方回合此军团兵力+1000", StringComparison.Ordinal))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!frontRowKeywordTroops.SetEquals(FrontRowKeywordTroopsAbilityIds))
            throw new InvalidOperationException("Front-row keyword/troops family changed; review its per-ability bindings.");
        foreach (var id in BackRowAttackTroopsSetAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.Trigger != "attack"
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Condition
                    && atom.Parameters.GetValueOrDefault("expression")?.Contains("source.row=back", StringComparison.Ordinal) == true)
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.ModifyTroops
                    && atom.Parameters.GetValueOrDefault("operation") == "set"))
                throw new InvalidOperationException($"Stale reviewed back-row troops-set segment: {id}");
            bindings.Add(id, BackRowAttackTroopsSet);
        }
        var backRowTroopsSets = abilities.Values.Where(ability => ability.Trigger == "attack"
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.ModifyTroops
                    && atom.Parameters.GetValueOrDefault("operation") == "set"))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!backRowTroopsSets.SetEquals(BackRowAttackTroopsSetAbilityIds))
            throw new InvalidOperationException("Back-row troops-set family changed; review its per-ability bindings.");
        foreach (var id in GrantedGainRuneAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.Trigger != "granted"
                || ability.ExecutionModel != "granted-effect"
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.GainRune))
                throw new InvalidOperationException($"Stale reviewed granted gain-rune segment: {id}");
            bindings.Add(id, GrantedGainRune);
        }
        var grantedGainRunes = abilities.Values.Where(ability => ability.Trigger == "granted"
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.GainRune))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!grantedGainRunes.SetEquals(GrantedGainRuneAbilityIds))
            throw new InvalidOperationException("Granted gain-rune family changed; review its per-ability bindings.");
        bool IsRuneCard(string cardId) => catalog.Cards.GetValueOrDefault(cardId)?.CardType == "rune";
        foreach (var id in MoraleActiveEffectAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || !IsRuneCard(ability.CardId)
                || !ability.Text.Contains("回合1次", StringComparison.Ordinal)
                || !ability.Text.Contains("可消耗", StringComparison.Ordinal)
                || !ability.Atoms.Any(atom => atom.Stage == "cost"))
                throw new InvalidOperationException($"Stale reviewed morale active-effect segment: {id}");
            bindings.Add(id, MoraleActiveEffect);
        }
        // 士气卡付费效果段的封闭集合：本族 14 段 + 士气翻面族已归属的 3 段，二者瓜分全部
        // rune 卡的付费能力段；新增任何一段都必须进入其中一族重新审查。
        var moralePaidSegments = abilities.Values.Where(ability => IsRuneCard(ability.CardId)
                && ability.Atoms.Any(atom => atom.Stage == "cost"))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        var expectedMoralePaid = MoraleActiveEffectAbilityIds
            .Concat(MoraleFaceFlipAbilityIds.Where(id => IsRuneCard(id.Split(':')[0])))
            .ToHashSet(StringComparer.Ordinal);
        if (!moralePaidSegments.SetEquals(expectedMoralePaid))
            throw new InvalidOperationException("Morale paid-effect family changed; review its per-ability bindings.");
        foreach (var id in MoraleResourceIdentityAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || !IsRuneCard(ability.CardId)
                || ability.ExecutionModel != "continuous"
                || ability.Atoms.Any(atom => atom.Stage == "cost"))
                throw new InvalidOperationException($"Stale reviewed morale resource-identity segment: {id}");
            bindings.Add(id, MoraleResourceIdentity);
        }
        var moraleResourceIdentities = abilities.Values.Where(ability => IsRuneCard(ability.CardId)
                && ability.ExecutionModel == "continuous"
                && !ability.Atoms.Any(atom => atom.Stage == "cost")
                && (ability.Text == "额外通用士气" || ability.Text.Contains("视为", StringComparison.Ordinal)))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!moraleResourceIdentities.SetEquals(MoraleResourceIdentityAbilityIds))
            throw new InvalidOperationException("Morale resource-identity family changed; review its per-ability bindings.");
        foreach (var id in ImmortalReplacementAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.Trigger != "death"
                || !ability.Text.Contains("作为代替", StringComparison.Ordinal)
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.ModifyTroops))
                throw new InvalidOperationException($"Stale reviewed immortal-replacement segment: {id}");
            bindings.Add(id, ImmortalReplacement);
        }
        var immortalReplacements = abilities.Values.Where(ability => ability.Trigger == "death"
                && ability.Text.Contains("作为代替", StringComparison.Ordinal)
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.ModifyTroops))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!immortalReplacements.SetEquals(ImmortalReplacementAbilityIds))
            throw new InvalidOperationException("Immortal-replacement family changed; review its per-ability bindings.");
        foreach (var id in AfterKillPiercingAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.Trigger != "after-kill"
                || !ability.Text.Contains("本回合获得", StringComparison.Ordinal)
                || !L12StructuredCardRules.HasPrintedKeywordReference(ability.CardId, "piercing"))
                throw new InvalidOperationException($"Stale reviewed after-kill piercing segment: {id}");
            bindings.Add(id, AfterKillPiercing);
        }
        var afterKillPiercing = abilities.Values.Where(ability => ability.Trigger == "after-kill"
                && ability.Text.Contains("本回合获得", StringComparison.Ordinal))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!afterKillPiercing.SetEquals(AfterKillPiercingAbilityIds))
            throw new InvalidOperationException("After-kill piercing family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(AeneasFrontRowGrantAbilityId, out var aeneasGrant)
            || aeneasGrant.Trigger != "static" || aeneasGrant.ExecutionModel != "continuous"
            || !aeneasGrant.Text.Contains("「位于前排」获得 ABILITY", StringComparison.Ordinal)
            || !aeneasGrant.Atoms.Any(atom => atom.Kind == L12AtomKinds.SetState
                && atom.Parameters.GetValueOrDefault("abilityRef") is not null))
            throw new InvalidOperationException($"Stale reviewed front-row grant segment: {AeneasFrontRowGrantAbilityId}");
        bindings.Add(AeneasFrontRowGrantAbilityId, FrontRowKeywordGrant);
        var frontRowGrants = abilities.Values.Where(ability => ability.Trigger == "static"
                && ability.ExecutionModel == "continuous"
                && ability.Text.Contains("「位于前排」获得 ABILITY", StringComparison.Ordinal))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!frontRowGrants.SetEquals([AeneasFrontRowGrantAbilityId]))
            throw new InvalidOperationException("Front-row keyword-grant family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(AchillesFrontRowGrantAbilityId, out var achillesGrant)
            || achillesGrant.Trigger != "granted-static" || achillesGrant.ExecutionModel != "granted-continuous"
            || !achillesGrant.Text.Contains("「位于前排」获得 ABILITY", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed granted front-row taunt segment: {AchillesFrontRowGrantAbilityId}");
        bindings.Add(AchillesFrontRowGrantAbilityId, GrantedFrontRowTauntOnKill);
        var grantedFrontRowGrants = abilities.Values.Where(ability => ability.Trigger == "granted-static"
                && ability.Text.Contains("「位于前排」获得 ABILITY", StringComparison.Ordinal))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!grantedFrontRowGrants.SetEquals([AchillesFrontRowGrantAbilityId]))
            throw new InvalidOperationException("Granted front-row keyword family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(DuelCombatLineAbilityId, out var duelCombatLine)
            || duelCombatLine.CardId != "S01-0101" || duelCombatLine.ExecutionModel != "continuous"
            || !duelCombatLine.Text.Contains("进攻无损", StringComparison.Ordinal)
            || !duelCombatLine.Text.Contains("无法被远程进攻", StringComparison.Ordinal)
            || !duelCombatLine.Atoms.Any(atom => atom.Kind == L12AtomKinds.AttackRule))
            throw new InvalidOperationException($"Stale reviewed duel-combat line: {DuelCombatLineAbilityId}");
        bindings.Add(DuelCombatLineAbilityId, DuelCombatLine);
        var duelCombatLines = abilities.Values.Where(ability => ability.ExecutionModel == "continuous"
                && ability.Text.Contains("进攻无损，无法被远程进攻", StringComparison.Ordinal))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!duelCombatLines.SetEquals([DuelCombatLineAbilityId]))
            throw new InvalidOperationException("Duel-combat line family changed; review its per-ability bindings.");
        foreach (var id in SimpleContinuousTroopsAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.ExecutionModel != "continuous"
                || ability.Atoms.Any(atom => atom.Stage == "cost")
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.ModifyTroops))
                throw new InvalidOperationException($"Stale reviewed simple continuous-troops rule: {id}");
            bindings.Add(id, SimpleContinuousTroops);
        }
        var simpleContinuousTroopsCards = new[] { "S01-0203", "S02-0516", "S02-0519", "S02-0523" };
        var simpleContinuousTroops = abilities.Values.Where(ability => ability.ExecutionModel == "continuous"
                && simpleContinuousTroopsCards.Contains(ability.CardId, StringComparer.Ordinal)
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.ModifyTroops)
                && ability.Atoms.All(atom => atom.Stage != "cost"))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!simpleContinuousTroops.SetEquals(SimpleContinuousTroopsAbilityIds))
            throw new InvalidOperationException("Simple continuous-troops family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(RestedFreeFrontBackMoveAbilityId, out var restedFreeMove)
            || restedFreeMove.CardId != "S02-0510" || restedFreeMove.ExecutionModel != "continuous"
            || !restedFreeMove.Text.Contains("前后位移无需消耗费用", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed rested free-move segment: {RestedFreeFrontBackMoveAbilityId}");
        bindings.Add(RestedFreeFrontBackMoveAbilityId, RestedFreeFrontBackMove);
        var restedFreeMoves = abilities.Values.Where(ability => ability.ExecutionModel == "continuous"
                && ability.Text.Contains("前后位移无需消耗费用", StringComparison.Ordinal))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!restedFreeMoves.SetEquals([RestedFreeFrontBackMoveAbilityId]))
            throw new InvalidOperationException("Rested free front-back move family changed; review its per-ability bindings.");
        foreach (var id in NegateResponseAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.Trigger != "reaction"
                || !(id.StartsWith("S01-0016:", StringComparison.Ordinal)
                    ? L12StructuredCardSemantics.IsAbsoluteDefenseResponse(ability.CardId)
                        && ability.Text.Contains("无效", StringComparison.Ordinal)
                    : L12StructuredCardSemantics.IsPitfallEntryNegationResponse(ability.CardId)
                        && ability.Text.Contains("登场效果无效", StringComparison.Ordinal)))
                throw new InvalidOperationException($"Stale reviewed negate-response segment: {id}");
            bindings.Add(id, NegateResponse);
        }
        var negateResponses = abilities.Values.Where(ability => ability.Trigger == "reaction"
                && (L12StructuredCardSemantics.IsAbsoluteDefenseResponse(ability.CardId)
                    || L12StructuredCardSemantics.IsPitfallEntryNegationResponse(ability.CardId)))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!negateResponses.SetEquals(NegateResponseAbilityIds))
            throw new InvalidOperationException("Negate-response family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(MercenaryHandBlockAbilityId, out var mercenaryBlock)
            || mercenaryBlock.Trigger != "reaction"
            || !L12StructuredCardSemantics.IsMercenaryHandBlockResponse(mercenaryBlock.CardId)
            || !mercenaryBlock.Text.Contains("可从手牌中弃置此军团", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed mercenary hand-block segment: {MercenaryHandBlockAbilityId}");
        bindings.Add(MercenaryHandBlockAbilityId, MercenaryHandBlock);
        var handBlocks = abilities.Values.Where(ability => ability.Trigger == "reaction"
                && L12StructuredCardSemantics.IsMercenaryHandBlockResponse(ability.CardId))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!handBlocks.SetEquals([MercenaryHandBlockAbilityId]))
            throw new InvalidOperationException("Mercenary hand-block family changed; review its per-ability bindings.");
        foreach (var id in DisasterContinuousRuleAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability)
                || catalog.Cards.GetValueOrDefault(ability.CardId)?.CardType != "destruction"
                || !L12ActiveDisasterRules.HasRegisteredContinuousRule(ability.CardId)
                || !(ability.Text.Contains("持续", StringComparison.Ordinal)
                    || ability.Trigger == "attack"))
                throw new InvalidOperationException($"Stale reviewed disaster continuous-rule segment: {id}");
            bindings.Add(id, DisasterContinuousRule);
        }
        // ST-DS02 色欲之罪由 ST 语义谓词覆盖并保持 composite-definition，不进入本族。
        // 「持续」文本段与天灾卡上的进攻规则段（雷霆天怒掷骰、暴怒之罪优先进攻）同属本族。
        var disasterContinuousRules = abilities.Values.Where(ability =>
                catalog.Cards.GetValueOrDefault(ability.CardId)?.CardType == "destruction"
                && ability.CardId != "ST-DS02"
                && ((ability.ExecutionModel is "continuous" or "rule"
                        && ability.Text.Contains("持续", StringComparison.Ordinal))
                    || ability.Trigger == "attack"))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!disasterContinuousRules.SetEquals(DisasterContinuousRuleAbilityIds))
            throw new InvalidOperationException("Disaster continuous-rule family changed; review its per-ability bindings.");
        foreach (var id in LethalReplacementOfferAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability)
                || ability.Trigger is not ("lethal-replacement" or "death")
                || !ability.Text.Contains("代替承受", StringComparison.Ordinal))
                throw new InvalidOperationException($"Stale reviewed lethal-replacement segment: {id}");
            bindings.Add(id, LethalReplacementOffer);
        }
        // 霍列姆赫布的替代段按 death 触发登记（替代候选弹框由共享管线提供）；
        // 湖中仙女的馈赠走 RemoveFromField 内的剑替代路径，不属本族。
        var lethalReplacementOffers = abilities.Values.Where(ability =>
                (ability.Trigger == "lethal-replacement"
                    || (ability.Trigger == "death" && ability.Text.Contains("代替承受", StringComparison.Ordinal)))
                && ability.CardId != "S02-06S3")
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!lethalReplacementOffers.SetEquals(LethalReplacementOfferAbilityIds))
            throw new InvalidOperationException("Lethal-replacement offer family changed; review its per-ability bindings.");
        foreach (var id in CardNameOncePerTurnAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability)
                || !L12CardNameUsageRules.Keys.ContainsKey(ability.CardId)
                || !ability.Text.Contains("每回合只可使用1次", StringComparison.Ordinal))
                throw new InvalidOperationException($"Stale reviewed card-name once-per-turn segment: {id}");
            bindings.Add(id, CardNameOncePerTurn);
        }
        var cardNameOncePerTurns = abilities.Values.Where(ability => ability.Trigger == "continuous"
                && ability.ExecutionModel == "rule"
                && L12CardNameUsageRules.Keys.ContainsKey(ability.CardId))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!cardNameOncePerTurns.SetEquals(CardNameOncePerTurnAbilityIds))
            throw new InvalidOperationException("Card-name once-per-turn family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(UniversalFactionMappingAbilityId, out var universalMapping)
            || universalMapping.CardId != "S02-0008"
            || !universalMapping.Text.Contains("视为与我方主宰阵营相同", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed universal faction-mapping segment: {UniversalFactionMappingAbilityId}");
        bindings.Add(UniversalFactionMappingAbilityId, UniversalFactionMapping);
        var universalFactionMappings = abilities.Values.Where(ability => ability.ExecutionModel == "rule"
                && ability.Text.Contains("视为与我方主宰阵营相同", StringComparison.Ordinal))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!universalFactionMappings.SetEquals([UniversalFactionMappingAbilityId]))
            throw new InvalidOperationException("Universal faction-mapping family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(ThorHammerMasterGateAbilityId, out var thorGate)
            || thorGate.CardId != "S02-0301"
            || !thorGate.Text.Contains("当我方主宰为", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed thor-hammer master-gate segment: {ThorHammerMasterGateAbilityId}");
        bindings.Add(ThorHammerMasterGateAbilityId, ThorHammerMasterGate);
        var thorHammerGates = abilities.Values.Where(ability => ability.ExecutionModel == "rule"
                && ability.Text.Contains("当我方主宰为", StringComparison.Ordinal))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!thorHammerGates.SetEquals([ThorHammerMasterGateAbilityId]))
            throw new InvalidOperationException("Thor-hammer master-gate family changed; review its per-ability bindings.");
        foreach (var id in GameSetupRuleAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.Trigger != "game-setup"
                || ability.ExecutionModel != "setup")
                throw new InvalidOperationException($"Stale reviewed game-setup segment: {id}");
            bindings.Add(id, GameSetupRule);
        }
        var gameSetupRules = abilities.Values.Where(ability => ability.Trigger == "game-setup")
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!gameSetupRules.SetEquals(GameSetupRuleAbilityIds))
            throw new InvalidOperationException("Game-setup rule family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(CooperativeSupportAbilityId, out var cooperativeSupport)
            || cooperativeSupport.ExecutionModel != "continuous"
            || !cooperativeSupport.Text.Contains("协防", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed cooperative-support segment: {CooperativeSupportAbilityId}");
        bindings.Add(CooperativeSupportAbilityId, CooperativeSupport);
        var cooperativeSupports = abilities.Values.Where(ability => ability.ExecutionModel == "continuous"
                && ability.Text.Contains("协防", StringComparison.Ordinal))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!cooperativeSupports.SetEquals([CooperativeSupportAbilityId]))
            throw new InvalidOperationException("Cooperative-support family changed; review its per-ability bindings.");
        // 诸神巅#4 台账标 setup/triggered（区别于 game-setup 印刷规则段），同属开场管线档案。
        if (!abilities.TryGetValue(GameSetupAutoMoraleAbilityId, out var autoMoraleSetup)
            || autoMoraleSetup.CardId != "S02-05D1" || autoMoraleSetup.Trigger != "setup"
            || !autoMoraleSetup.Text.Contains("主神开场即可追加2张额外士气", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed auto-morale setup segment: {GameSetupAutoMoraleAbilityId}");
        bindings.Add(GameSetupAutoMoraleAbilityId, GameSetupRule);
        var autoMoraleSetups = abilities.Values.Where(ability => ability.Trigger == "setup")
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!autoMoraleSetups.SetEquals([GameSetupAutoMoraleAbilityId]))
            throw new InvalidOperationException("Auto-morale setup family changed; review its per-ability bindings.");
        foreach (var id in TrialValueAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.Trigger != "trial"
                || ability.ExecutionModel != "rule"
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Special
                    && atom.Parameters.GetValueOrDefault("semantic") == "trial-value"))
                throw new InvalidOperationException($"Stale reviewed trial-value segment: {id}");
            bindings.Add(id, TrialValue);
        }
        var trialValueSegments = abilities.Values.Where(ability => ability.Trigger == "trial")
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!trialValueSegments.SetEquals(TrialValueAbilityIds))
            throw new InvalidOperationException("Printed trial-value family changed; review its per-ability bindings.");
        foreach (var id in PromotionEntryAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.Trigger != "promotion"
                || ability.ExecutionModel != "summon-flow"
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.Special && atom.Stage == "cost"
                    && atom.Parameters.GetValueOrDefault("domain") == "god-power"
                    && atom.Parameters.GetValueOrDefault("operation") == "consume-and-flip")
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.SelectTarget
                    && atom.Parameters.GetValueOrDefault("filter") == "same-name-non-promoted"))
                throw new InvalidOperationException($"Stale reviewed promotion-entry segment: {id}");
            bindings.Add(id, PromotionEntry);
        }
        var promotionSegments = abilities.Values.Where(ability => ability.Trigger == "promotion")
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!promotionSegments.SetEquals(PromotionEntryAbilityIds))
            throw new InvalidOperationException("Promotion-entry family changed; review its per-ability bindings.");
        foreach (var id in StructuredContinuousCombatRuleAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || !IsStructuredContinuousCombatRule(ability))
                throw new InvalidOperationException($"Stale reviewed structured continuous combat rule: {id}");
            bindings.Add(id, StructuredContinuousCombatRule);
        }
        var structuredContinuousCombatRules = abilities.Values.Where(IsStructuredContinuousCombatRule)
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!structuredContinuousCombatRules.SetEquals(StructuredContinuousCombatRuleAbilityIds))
            throw new InvalidOperationException("Structured continuous combat-rule family changed; review its per-ability bindings.");
        foreach (var id in PrintedEntryCostAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.ExecutionModel != "continuous"
                || L12StructuredCardSemantics.PrintedEntryCostRule(ability.CardId) is null)
                throw new InvalidOperationException($"Stale reviewed printed entry-cost rule: {id}");
            bindings.Add(id, PrintedEntryCost);
        }
        var reviewedPrintedEntryCostCards = PrintedEntryCostAbilityIds
            .Select(id => id[..id.IndexOf(":ability:", StringComparison.Ordinal)])
            .Concat(["S01-0107", "S01-0202"]).ToHashSet(StringComparer.Ordinal);
        var actualPrintedEntryCostCards = abilities.Values.Select(ability => ability.CardId)
            .Distinct(StringComparer.Ordinal)
            .Where(cardId => L12StructuredCardSemantics.PrintedEntryCostRule(cardId) is not null)
            .ToHashSet(StringComparer.Ordinal);
        if (!actualPrintedEntryCostCards.SetEquals(reviewedPrintedEntryCostCards))
            throw new InvalidOperationException("Printed entry-cost rule family changed; review its per-ability bindings.");
        foreach (var id in StructuredHandCostAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || !IsStructuredHandCostAbility(ability))
                throw new InvalidOperationException($"Stale reviewed structured hand-cost rule: {id}");
            bindings.Add(id, StructuredHandCost);
        }
        var structuredHandCosts = abilities.Values.Where(IsStructuredHandCostAbility)
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!structuredHandCosts.SetEquals(StructuredHandCostAbilityIds))
            throw new InvalidOperationException("Structured hand-condition cost family changed; review its per-ability bindings.");
        foreach (var id in HandPlayBlockAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.ExecutionModel != "continuous"
                || L12StructuredCardSemantics.HandPlayBlockRule(ability.CardId) is null)
                throw new InvalidOperationException($"Stale reviewed hand-play block rule: {id}");
            bindings.Add(id, HandPlayBlock);
        }
        var handPlayBlocks = abilities.Values.Where(ability => ability.ExecutionModel == "continuous"
                && L12StructuredCardSemantics.HandPlayBlockRule(ability.CardId) is not null)
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!handPlayBlocks.SetEquals(HandPlayBlockAbilityIds))
            throw new InvalidOperationException("Hand-play block family changed; review its per-ability bindings.");
        foreach (var id in RelicZoneLimitExemptAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.ExecutionModel != "continuous"
                || !L12StructuredCardSemantics.IgnoresRelicZoneLimit(ability.CardId))
                throw new InvalidOperationException($"Stale reviewed relic-zone limit exemption: {id}");
            bindings.Add(id, RelicZoneLimitExempt);
        }
        var relicZoneLimitExemptions = abilities.Values.Where(ability => ability.ExecutionModel == "continuous"
                && L12StructuredCardSemantics.IgnoresRelicZoneLimit(ability.CardId))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!relicZoneLimitExemptions.SetEquals(RelicZoneLimitExemptAbilityIds))
            throw new InvalidOperationException("Relic-zone limit exemption family changed; review its per-ability bindings.");
        foreach (var id in OutOfDeckGraveyardLifecycleAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability)
                || ability.ExecutionModel is not ("continuous" or "rule")
                || !L12StructuredCardSemantics.HasOutOfDeckGraveyardLifecycle(ability.CardId))
                throw new InvalidOperationException($"Stale reviewed out-of-deck graveyard lifecycle: {id}");
            bindings.Add(id, OutOfDeckGraveyardLifecycle);
        }
        var outOfDeckGraveyardLifecycles = abilities.Values
            .Where(IsOutOfDeckGraveyardLifecycleAbility)
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!outOfDeckGraveyardLifecycles.SetEquals(OutOfDeckGraveyardLifecycleAbilityIds))
            throw new InvalidOperationException("Out-of-deck graveyard lifecycle family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(FieldMoraleResourceAbilityId, out var fieldMoraleResource)
            || fieldMoraleResource.ExecutionModel != "continuous"
            || L12StructuredCardSemantics.FieldMoraleResourceRule(fieldMoraleResource.CardId) is null)
            throw new InvalidOperationException($"Stale reviewed field morale resource: {FieldMoraleResourceAbilityId}");
        bindings.Add(FieldMoraleResourceAbilityId, FieldMoraleResource);
        var fieldMoraleResourceCards = abilities.Values
            .Where(ability => L12StructuredCardSemantics.FieldMoraleResourceRule(ability.CardId) is not null)
            .Select(ability => ability.CardId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (!fieldMoraleResourceCards.SetEquals(["S01-0212"]))
            throw new InvalidOperationException("Field morale-resource family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(BlackLotusMoraleReturnAbilityId, out var moraleZoneResource)
            || moraleZoneResource.CardId != "S02-0010"
            || moraleZoneResource.Trigger != "return-as-morale"
            || moraleZoneResource.ExecutionModel != "replacement"
            || L12StructuredCardSemantics.MoraleZoneResourceRule(moraleZoneResource.CardId) is null)
            throw new InvalidOperationException(
                $"Stale reviewed morale-zone resource replacement: {BlackLotusMoraleReturnAbilityId}");
        bindings.Add(BlackLotusMoraleReturnAbilityId, MoraleZoneResource);
        var moraleZoneResourceCards = abilities.Values
            .Where(ability => L12StructuredCardSemantics.MoraleZoneResourceRule(ability.CardId) is not null
                && ability.Trigger == "return-as-morale" && ability.ExecutionModel == "replacement")
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!moraleZoneResourceCards.SetEquals([BlackLotusMoraleReturnAbilityId]))
            throw new InvalidOperationException(
                "Morale-zone resource replacement family changed; review its per-ability bindings.");
        foreach (var id in MoraleFaceFlipAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || !IsMoraleFaceFlipAbility(ability))
                throw new InvalidOperationException($"Stale reviewed morale-face flip ability: {id}");
            bindings.Add(id, MoraleFaceFlip);
        }
        var moraleFaceFlipAbilities = abilities.Values.Where(IsMoraleFaceFlipAbility)
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!moraleFaceFlipAbilities.SetEquals(MoraleFaceFlipAbilityIds))
            throw new InvalidOperationException(
                "Printed morale-face flip family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(OpponentTurnFieldRuleAbilityId, out var opponentTurnFieldRule)
            || opponentTurnFieldRule.ExecutionModel != "continuous"
            || L12StructuredCardSemantics.OpponentTurnFieldRule(opponentTurnFieldRule.CardId) is null)
            throw new InvalidOperationException($"Stale reviewed opponent-turn field rule: {OpponentTurnFieldRuleAbilityId}");
        bindings.Add(OpponentTurnFieldRuleAbilityId, OpponentTurnFieldRule);
        var opponentTurnFieldRules = abilities.Values
            .Where(ability => ability.ExecutionModel == "continuous"
                && L12StructuredCardSemantics.OpponentTurnFieldRule(ability.CardId) is not null
                && ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.ModifyTroops))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!opponentTurnFieldRules.SetEquals([OpponentTurnFieldRuleAbilityId]))
            throw new InvalidOperationException("Opponent-turn field-rule family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(TombGuardMasterAuraAbilityId, out var tombGuardMasterAura)
            || tombGuardMasterAura.ExecutionModel != "continuous"
            || L12StructuredCardSemantics.MasterFieldAuraRule(tombGuardMasterAura.CardId) is null)
            throw new InvalidOperationException($"Stale reviewed tomb-guard master aura: {TombGuardMasterAuraAbilityId}");
        bindings.Add(TombGuardMasterAuraAbilityId, TombGuardMasterAura);
        foreach (var id in PureSummonTurnCounterProtectionAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.ExecutionModel != "continuous"
                || !L12StructuredCardSemantics.HasSummonTurnCounterTacticProtection(ability.CardId))
                throw new InvalidOperationException($"Stale reviewed summon-turn counter protection: {id}");
            bindings.Add(id, SummonTurnCounterProtection);
        }
        if (!abilities.TryGetValue(RamsesProtectionAndEntryCostAbilityId, out var ramses)
            || ramses.ExecutionModel != "continuous"
            || !L12StructuredCardSemantics.HasSummonTurnCounterTacticProtection(ramses.CardId)
            || L12StructuredCardSemantics.PrintedEntryCostRule(ramses.CardId) is null)
            throw new InvalidOperationException($"Stale reviewed Ramses combined continuous rule: {RamsesProtectionAndEntryCostAbilityId}");
        bindings.Add(RamsesProtectionAndEntryCostAbilityId, RamsesProtectionAndEntryCost);
        var expectedProtectionCards = PureSummonTurnCounterProtectionAbilityIds
            .Append(RamsesProtectionAndEntryCostAbilityId)
            .Select(id => id[..id.IndexOf(":ability:", StringComparison.Ordinal)])
            .ToHashSet(StringComparer.Ordinal);
        var actualProtectionCards = abilities.Values.Select(ability => ability.CardId)
            .Distinct(StringComparer.Ordinal)
            .Where(L12StructuredCardSemantics.HasSummonTurnCounterTacticProtection)
            .ToHashSet(StringComparer.Ordinal);
        if (!actualProtectionCards.SetEquals(expectedProtectionCards))
            throw new InvalidOperationException("Summon-turn counter protection family changed; review its per-ability bindings.");
        // ===== P0 长尾归属批：per-ID 校验 + 封闭集合守卫 =====
        foreach (var (id, phrase) in PipelineActiveEffectChecks)
        {
            if (!abilities.TryGetValue(id, out var ability)
                || !ability.Text.Contains(phrase, StringComparison.Ordinal))
                throw new InvalidOperationException($"Stale reviewed pipeline active-effect binding: {id}");
            bindings.Add(id, PipelineActiveEffect);
        }
        // A 族封闭集合：按卡号集合重扫共享主动管线可见段——引擎主动触发段（剔除已归属的
        // 主动休整/士气翻面段）、印刷主动文本段（剔除触发句前缀与伤害拆段）与主动模式分支段。
        var pipelineActiveCards = PipelineActiveEffectChecks
            .Select(check => check.Id.Split(':')[0]).ToHashSet(StringComparer.Ordinal);
        bool IsPrintedActiveText(string text) =>
            (text.Contains("可消耗", StringComparison.Ordinal) || text.Contains("可弃置", StringComparison.Ordinal)
                || text.Contains("可返还", StringComparison.Ordinal)
                || text.Contains("可对我方主宰造成", StringComparison.Ordinal)
                || text.Contains("可将", StringComparison.Ordinal)
                || text.Contains("可选择以下一项", StringComparison.Ordinal)
                || text.Contains("回合1次 本回合", StringComparison.Ordinal))
            && !text.StartsWith("受到伤害时", StringComparison.Ordinal)
            && !text.StartsWith("触发", StringComparison.Ordinal);
        var pipelineActiveRescan = abilities.Values.Where(ability => pipelineActiveCards.Contains(ability.CardId)
                && !ActiveRestAbilityIds.Contains(ability.AbilityId, StringComparer.Ordinal)
                && !MoraleFaceFlipAbilityIds.Contains(ability.AbilityId, StringComparer.Ordinal)
                && (ability.Trigger is "active" or "active-while-attached" or "trial-completed"
                    || ability.Trigger.StartsWith("mode-", StringComparison.Ordinal)
                    || (ability.Trigger == "granted" && ability.ExecutionModel == "granted-effect"
                        && ability.CardId is "S02-0404" or "S02-0603")
                    || (ability.Trigger == "attack" && ability.CardId == "S01-04M2")
                    || (ability.Trigger == "after-attack" && ability.CardId == "S02-06S6")
                    || (ability.Trigger == "static" && IsPrintedActiveText(ability.Text))))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!pipelineActiveRescan.SetEquals(PipelineActiveEffectAbilityIds))
            throw new InvalidOperationException("Pipeline active-effect family changed; review its per-ability bindings.");
        foreach (var id in PaidSelfStateTriggerAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability)
                || ability.ExecutionModel != "triggered"
                || !ability.Atoms.Any(atom => atom.Stage == "cost")
                || !ability.Atoms.Any(atom => atom.Kind == L12AtomKinds.SetState
                    || atom.Kind == L12AtomKinds.Ready))
                throw new InvalidOperationException($"Stale reviewed paid self-state trigger binding: {id}");
            bindings.Add(id, PaidSelfStateTrigger);
        }
        var paidSelfStateRescan = abilities.Values.Where(ability =>
                ability.CardId == "S02-0602" && ability.Trigger == "enter"
                || ability.CardId == "S02-0610" && ability.Trigger == "after-trial")
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!paidSelfStateRescan.SetEquals(PaidSelfStateTriggerAbilityIds))
            throw new InvalidOperationException("Paid self-state trigger family changed; review its per-ability bindings.");
        foreach (var (id, phrase) in PipelinePublicTriggerChecks)
        {
            if (!abilities.TryGetValue(id, out var ability)
                || !ability.Text.Contains(phrase, StringComparison.Ordinal))
                throw new InvalidOperationException($"Stale reviewed pipeline public-trigger binding: {id}");
            bindings.Add(id, PipelinePublicTrigger);
        }
        // B 族封闭集合：按卡号集合重扫公开触发管线可见段（进攻/阵亡/离场/登场/伤害/试炼完成等
        // 公开事件段 + 主神区触发文本段）。康斯坦丝登场段此前为 composite-definition，本批按映射
        // 改绑共享公开触发管线。
        var publicTriggerCards = PipelinePublicTriggerChecks
            .Select(check => check.Id.Split(':')[0]).ToHashSet(StringComparer.Ordinal);
        var publicTriggerAttackCards = new[] { "S02-0103", "S02-0511", "S02-0516", "S02-0605",
            "S02-0607", "S02-0608", "S02-0612", "S02-0617" };
        var publicTriggerRescan = abilities.Values.Where(ability => publicTriggerCards.Contains(ability.CardId)
                && ((ability.Trigger == "attack"
                        && publicTriggerAttackCards.Contains(ability.CardId, StringComparer.Ordinal))
                    || (ability.Trigger == "death" && ability.CardId == "S01-01M1")
                    || (ability.Trigger == "leave" && ability.CardId is "S01-04M2" or "S01-0204")
                    || (ability.Trigger == "after-attack" && ability.CardId is "S01-0311" or "S02-0503")
                    || ability.Trigger is "after-opponent-tactic" or "master-damaged-by-effect"
                        or "tactic-effect-resolved" or "after-trial" or "trial-complete" or "friendly-legion-death"
                        or "discarded" or "master-morale-return" or "friendly-legion-moves"
                        or "friendly-back-to-front" or "friendly-front-to-back" or "after-opponent-attack"
                    || (ability.Trigger == "master-damaged" && ability.ExecutionModel == "triggered")
                    || (ability.Trigger == "enter" && ability.CardId is "S02-0611" or "S02-0614")
                    || (ability.Trigger == "after-kill" && ability.CardId == "S02-0602")
                    || (ability.Trigger == "continuous" && ability.CardId == "S02-0401")
                    || (ability.Trigger == "static"
                        && (ability.CardId is "S01-0414" or "S01-01D1" or "S01-0311"
                            || (ability.CardId == "S01-01M1"
                                && ability.Text.Contains("可在前排活跃登场", StringComparison.Ordinal))
                            || (ability.CardId == "S01-02M3"
                                && (ability.Text.StartsWith("对方 ", StringComparison.Ordinal)
                                    || ability.Text.StartsWith("受到伤害时", StringComparison.Ordinal)))
                            || ((ability.CardId is "S02-06S5" or "S02-06S3")
                                && ability.Text.StartsWith("触发", StringComparison.Ordinal))
                            || (ability.CardId == "ST01-C1"
                                && ability.Text.Contains("士气为0张时", StringComparison.Ordinal))))))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!publicTriggerRescan.SetEquals(PipelinePublicTriggerAbilityIds))
            throw new InvalidOperationException("Pipeline public-trigger family changed; review its per-ability bindings.");
        foreach (var (id, phrase) in PipelineHandPlayChecks)
        {
            if (!abilities.TryGetValue(id, out var ability)
                || !ability.Text.Contains(phrase, StringComparison.Ordinal))
                throw new InvalidOperationException($"Stale reviewed pipeline hand-play binding: {id}");
            bindings.Add(id, PipelineHandPlay);
        }
        // C 族封闭集合：打出管线卡的 play/play-additional/hand-play 段与密米尔之泉战术段；
        // 荣耀之路#1「翻转最多3张士气」已归属士气翻面族，显式剔除。祷告仪式#1 此前为
        // composite-definition，本批按映射改绑共享打出管线。
        var handPlayCards = PipelineHandPlayChecks
            .Select(check => check.Id.Split(':')[0]).ToHashSet(StringComparer.Ordinal);
        var handPlayRescan = abilities.Values.Where(ability => handPlayCards.Contains(ability.CardId)
                && !MoraleFaceFlipAbilityIds.Contains(ability.AbilityId, StringComparer.Ordinal)
                && (ability.Trigger is "play" or "play-additional" or "hand-play"
                    or "master-effect-damage-threshold" or "entry-discount"))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!handPlayRescan.SetEquals(PipelineHandPlayAbilityIds))
            throw new InvalidOperationException("Pipeline hand-play family changed; review its per-ability bindings.");
        foreach (var (id, phrase) in PipelineResponseChecks)
        {
            if (!abilities.TryGetValue(id, out var ability)
                || !ability.Text.Contains(phrase, StringComparison.Ordinal))
                throw new InvalidOperationException($"Stale reviewed pipeline response binding: {id}");
            bindings.Add(id, PipelineResponse);
        }
        // D 族封闭集合：两张响应卡的改目标/反击响应段。资格谓词仍逐卡（见档案注释），
        // 故按卡号+触发重扫而不是按注册表。
        var pipelineResponseRescan = abilities.Values.Where(ability =>
                ability.CardId is "S02-0005" or "S02-0106"
                && ability.Trigger is "opponent-attacks-master" or "opponent-attack-or-effect")
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!pipelineResponseRescan.SetEquals(PipelineResponseAbilityIds))
            throw new InvalidOperationException("Pipeline response family changed; review its per-ability bindings.");
        foreach (var id in FrontRowCompositeLineAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.ExecutionModel != "continuous"
                || !ability.Text.Contains("「位于前排」获得挑衅", StringComparison.Ordinal)
                || !ability.Text.Contains("（对方只可进攻带有此效果的军团）", StringComparison.Ordinal))
                throw new InvalidOperationException($"Stale reviewed front-row composite line: {id}");
            bindings.Add(id, FrontRowCompositeLine);
        }
        // E 族封闭集合：全池重扫整行印刷声明（无【】括号的「获得挑衅」措辞，区别于 overlay 拆分段）。
        var frontRowCompositeLines = abilities.Values.Where(ability => ability.ExecutionModel == "continuous"
                && ability.Text.Contains("「位于前排」获得挑衅", StringComparison.Ordinal))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!frontRowCompositeLines.SetEquals(FrontRowCompositeLineAbilityIds))
            throw new InvalidOperationException("Front-row composite-line family changed; review its per-ability bindings.");
        foreach (var id in PipelineDisasterAuthorityAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability)
                || catalog.Cards.GetValueOrDefault(ability.CardId)?.CardType != "destruction"
                || ability.Trigger is not ("disaster" or "turn-start" or "turn-end"))
                throw new InvalidOperationException($"Stale reviewed disaster-authority binding: {id}");
            bindings.Add(id, PipelineDisasterAuthority);
        }
        // F 族封闭集合：ST 天灾的触发段（S01/S02 天灾触发段已有细原子/复合定义，不入本族）
        // + 全部天灾卡的回合开始/结束段。
        var disasterAuthorityRescan = abilities.Values.Where(ability =>
                catalog.Cards.GetValueOrDefault(ability.CardId)?.CardType == "destruction"
                && ((ability.Trigger == "disaster" && ability.CardId is "ST-DS01" or "ST-DS03")
                    || ability.Trigger is "turn-start" or "turn-end"))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!disasterAuthorityRescan.SetEquals(PipelineDisasterAuthorityAbilityIds))
            throw new InvalidOperationException("Disaster-authority family changed; review its per-ability bindings.");
        // G 族逐卡专用出口。
        if (!abilities.TryGetValue(ValkyrieDrawPhaseAbilityId, out var valkyrieDraw)
            || valkyrieDraw.CardId != "S01-03M1"
            || !valkyrieDraw.Text.Contains("抽牌阶段改为弃置", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed valkyrie draw-phase segment: {ValkyrieDrawPhaseAbilityId}");
        bindings.Add(ValkyrieDrawPhaseAbilityId, ValkyrieDrawPhase);
        if (!abilities.TryGetValue(IsisSetupAbilityId, out var isisSetup)
            || isisSetup.CardId != "S01-02M1"
            || !isisSetup.Text.Contains("复苏的奥西里斯", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed isis setup segment: {IsisSetupAbilityId}");
        bindings.Add(IsisSetupAbilityId, IsisSetup);
        if (!abilities.TryGetValue(MasterLegionReturnAbilityId, out var masterReturn)
            || masterReturn.CardId != "S02-01M1" || masterReturn.Trigger != "leave"
            || !masterReturn.Text.Contains("返回主宰区", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed master-legion return segment: {MasterLegionReturnAbilityId}");
        bindings.Add(MasterLegionReturnAbilityId, MasterLegionReturn);
        if (!abilities.TryGetValue(AttachedTacticsDiscardAbilityId, out var attachedDiscard)
            || attachedDiscard.CardId != "S02-0013" || attachedDiscard.Trigger != "host-leaves-artifact"
            || !attachedDiscard.Text.Contains("弃置此战术", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed attached-tactics discard segment: {AttachedTacticsDiscardAbilityId}");
        bindings.Add(AttachedTacticsDiscardAbilityId, AttachedTacticsDiscard);
        if (!abilities.TryGetValue(AnderstorpDamageFloorAbilityId, out var anderstorpFloor)
            || anderstorpFloor.CardId != "S02-0305" || anderstorpFloor.ExecutionModel != "replacement"
            || !anderstorpFloor.Text.Contains("第一次伤害变为2", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed anderstorp damage-floor segment: {AnderstorpDamageFloorAbilityId}");
        bindings.Add(AnderstorpDamageFloorAbilityId, AnderstorpDamageFloor);
        foreach (var id in LakeLadySwordAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.CardId != "S02-06S3"
                || !ability.Text.Contains("王者之剑", StringComparison.Ordinal))
                throw new InvalidOperationException($"Stale reviewed lake-lady sword segment: {id}");
            bindings.Add(id, LakeLadySword);
        }
        // 湖中仙女剑替代封闭集合：持续声明段 + 阵亡代替段（RemoveFromField 内剑替代路径）。
        var lakeLadySwordRescan = abilities.Values.Where(ability => ability.CardId == "S02-06S3"
                && ((ability.Trigger == "static" && ability.Text.StartsWith("持续", StringComparison.Ordinal))
                    || ability.Trigger == "death"))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!lakeLadySwordRescan.SetEquals(LakeLadySwordAbilityIds))
            throw new InvalidOperationException("Lake-lady sword family changed; review its per-ability bindings.");
        foreach (var id in TrialCapacityAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability)
                || !(ability.CardId == "S02-06D1"
                        && ability.Text.Contains("可携带1张已完成的试炼", StringComparison.Ordinal)
                    || ability.CardId == "S02-06M2"
                        && ability.Text.Contains("可完成的试炼数量增加1张", StringComparison.Ordinal)))
                throw new InvalidOperationException($"Stale reviewed trial-capacity segment: {id}");
            bindings.Add(id, TrialCapacity);
        }
        // 试炼容量封闭集合：阿瓦隆印刷容量行 + 安格斯容量规则段。
        var trialCapacityRescan = abilities.Values.Where(ability =>
                (ability.CardId == "S02-06D1" && ability.Trigger == "static"
                    && ability.Text.Contains("可携带", StringComparison.Ordinal))
                || (ability.CardId == "S02-06M2" && ability.Trigger == "rule"))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!trialCapacityRescan.SetEquals(TrialCapacityAbilityIds))
            throw new InvalidOperationException("Trial-capacity family changed; review its per-ability bindings.");
        foreach (var id in RuinedRitualModesAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.CardId != "S02-0016"
                || ability.Trigger != "granted")
                throw new InvalidOperationException($"Stale reviewed ruined-ritual mode segment: {id}");
            bindings.Add(id, RuinedRitualModes);
        }
        var ruinedRitualRescan = abilities.Values.Where(ability => ability.CardId == "S02-0016"
                && ability.Trigger == "granted")
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!ruinedRitualRescan.SetEquals(RuinedRitualModesAbilityIds))
            throw new InvalidOperationException("Ruined-ritual modes family changed; review its per-ability bindings.");
        foreach (var id in PrayerModesAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.CardId != "S02-0012"
                || ability.Trigger != "granted")
                throw new InvalidOperationException($"Stale reviewed prayer mode segment: {id}");
            bindings.Add(id, PrayerModes);
        }
        var prayerModesRescan = abilities.Values.Where(ability => ability.CardId == "S02-0012"
                && ability.Trigger == "granted")
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!prayerModesRescan.SetEquals(PrayerModesAbilityIds))
            throw new InvalidOperationException("Prayer modes family changed; review its per-ability bindings.");
        foreach (var id in TenkaModesAbilityIds)
        {
            if (!abilities.TryGetValue(id, out var ability) || ability.CardId != "S02-0406"
                || ability.Trigger != "granted")
                throw new InvalidOperationException($"Stale reviewed tenka mode segment: {id}");
            bindings.Add(id, TenkaModes);
        }
        var tenkaModesRescan = abilities.Values.Where(ability => ability.CardId == "S02-0406"
                && ability.Trigger == "granted")
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!tenkaModesRescan.SetEquals(TenkaModesAbilityIds))
            throw new InvalidOperationException("Tenka modes family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(LancelotKillModesAbilityId, out var lancelotMode)
            || lancelotMode.CardId != "S02-0602" || lancelotMode.Trigger != "granted"
            || !lancelotMode.Text.Contains("试炼+1", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed lancelot kill-mode segment: {LancelotKillModesAbilityId}");
        bindings.Add(LancelotKillModesAbilityId, LancelotKillModes);
        // 兰斯洛特 granted 封闭集合：「试炼+1」段归本档案；同卡「获得1符文」段属 granted 符文族。
        var lancelotGrantedRescan = abilities.Values.Where(ability => ability.CardId == "S02-0602"
                && ability.Trigger == "granted" && ability.ExecutionModel == "granted-effect"
                && !ability.Text.Contains("获得1符文", StringComparison.Ordinal))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!lancelotGrantedRescan.SetEquals([LancelotKillModesAbilityId]))
            throw new InvalidOperationException("Lancelot granted-mode family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(ConstanceModesAbilityId, out var constanceMode)
            || constanceMode.CardId != "S02-0614" || constanceMode.Trigger != "granted"
            || !constanceMode.Text.Contains("发动试炼", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed constance mode segment: {ConstanceModesAbilityId}");
        bindings.Add(ConstanceModesAbilityId, ConstanceModes);
        // 康斯坦丝 granted 封闭集合：「发动试炼」段归本档案；同卡「获得1符文」段属 granted 符文族。
        var constanceGrantedRescan = abilities.Values.Where(ability => ability.CardId == "S02-0614"
                && ability.Trigger == "granted"
                && !ability.Text.Contains("获得1符文", StringComparison.Ordinal))
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!constanceGrantedRescan.SetEquals([ConstanceModesAbilityId]))
            throw new InvalidOperationException("Constance granted-mode family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(AvalonTurnStartAbilityId, out var avalonTurnStart)
            || avalonTurnStart.CardId != "S02-06D1" || avalonTurnStart.Trigger != "turn-start"
            || !avalonTurnStart.Text.Contains("试炼+1并获得1符文", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed avalon turn-start segment: {AvalonTurnStartAbilityId}");
        bindings.Add(AvalonTurnStartAbilityId, AvalonTurnStart);
        var avalonTurnStartRescan = abilities.Values.Where(ability => ability.CardId == "S02-06D1"
                && ability.Trigger == "turn-start")
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!avalonTurnStartRescan.SetEquals([AvalonTurnStartAbilityId]))
            throw new InvalidOperationException("Avalon turn-start family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(KingsSwordAttachedAbilityId, out var kingsSword)
            || kingsSword.CardId != "S02-06S2" || kingsSword.ExecutionModel != "continuous"
            || !kingsSword.Text.Contains("叠放在我方<亚瑟王>下方时", StringComparison.Ordinal)
            || !L12StructuredCardSemantics.GrantsStrongAttackWhileAttached(kingsSword.CardId))
            throw new InvalidOperationException($"Stale reviewed kings-sword attached segment: {KingsSwordAttachedAbilityId}");
        bindings.Add(KingsSwordAttachedAbilityId, KingsSwordAttached);
        // 王者之剑封闭集合：该卡全池仅此一段；附叠强攻身份表新增卡号必须重新审查。
        var kingsSwordRescan = abilities.Values.Where(ability => ability.CardId == "S02-06S2")
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!kingsSwordRescan.SetEquals([KingsSwordAttachedAbilityId]))
            throw new InvalidOperationException("Kings-sword attached family changed; review its per-ability bindings.");
        if (!abilities.TryGetValue(SaladinLineAbilityId, out var saladin)
            || saladin.CardId != "S01-0206" || saladin.ExecutionModel != "continuous"
            || !saladin.Text.Contains("可进行1次位移", StringComparison.Ordinal)
            || !saladin.Text.Contains("相邻的【太阳城】军团", StringComparison.Ordinal))
            throw new InvalidOperationException($"Stale reviewed saladin composite line: {SaladinLineAbilityId}");
        bindings.Add(SaladinLineAbilityId, SaladinLine);
        // 萨拉丁复合行封闭集合：该卡的 static/continuous 印刷行仅此一段（其余为进攻/阵亡触发段）。
        var saladinRescan = abilities.Values.Where(ability => ability.CardId == "S01-0206"
                && ability.Trigger == "static")
            .Select(ability => ability.AbilityId).ToHashSet(StringComparer.Ordinal);
        if (!saladinRescan.SetEquals([SaladinLineAbilityId]))
            throw new InvalidOperationException("Saladin composite-line family changed; review its per-ability bindings.");
        return bindings;
    }
}
