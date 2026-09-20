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
        "S01-0310:ability:active:0a0575206e996652",
        "S01-0409:ability:active:56a01edf47ee1225",
        "S02-0505:ability:active:bac4cb5d348f29f1",
        "ST01-01:ability:active:69626894e55e27e5",
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
        new SortedDictionary<string, string>(StringComparer.Ordinal))
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
                "S02-05M1:ability:keyword-definition:96aa4e9504b12339",
            ],
            ["strong-attack"] =
            [
                "S02-05M1:ability:keyword-definition:62d5e99aeb08acbb",
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
                ["single-candidate-choice"] = "关键词定义不创建玩家对象选择；实际进攻或致命替代使用当时的公共规则候选。",
                ["duplicate-submit"] = "关键词定义没有独立提交命令；重复读取规则语义必须无副作用。",
            }) { AdditionalChecks = ["parent-grant-boundary", "authoritative-consumer", "leave-or-turn-expiry", "reconnect-state"] };
    }

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

    private static void ValidateOwners(L12LifecycleProfile profile)
    {
        foreach (var owner in profile.RuntimeOwners.Values)
        {
            var parts = owner.Split('.');
            var type = parts.Length == 1 ? typeof(L12GameEngine)
                : parts[0] == nameof(L12StructuredCardRules) ? typeof(L12StructuredCardRules)
                : parts[0] == nameof(L12StructuredCardSemantics) ? typeof(L12StructuredCardSemantics)
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
        ValidateOwners(StructuredContinuousCombatRule);
        ValidateOwners(PrintedEntryCost);
        ValidateOwners(StructuredHandCost);
        ValidateOwners(HandPlayBlock);
        ValidateOwners(SummonTurnCounterProtection);
        ValidateOwners(RamsesProtectionAndEntryCost);
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
        return bindings;
    }
}
