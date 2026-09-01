using System.Text.Json.Serialization;

namespace TwelveLegions.Server;

[JsonConverter(typeof(JsonStringEnumConverter<L12Phase>))]
public enum L12Phase
{
    Initiative,
    DisasterPreparation,
    Mulligan,
    Disaster,
    Reset,
    Draw,
    Morale,
    Main,
    End,
    Defense,
    GameOver,
}

public enum L12FieldLeaveKind
{
    Defeat,
    Discard,
    PutIntoGraveyard,
}

[JsonConverter(typeof(JsonStringEnumConverter<L12CombatStage>))]
public enum L12CombatStage
{
    AttackerAttackTiming,
    DefenderAttackTiming,
    DefenseChoice,
    CombatDamage,
    KillTriggers,
    DefenderKillTriggers,
    AttackerDeathTriggers,
    DefenderDeathTriggers,
    FinalizeDeaths,
    AttackerAfterAttack,
    DefenderAfterAttack,
    Complete,
}

public sealed class L12CardDefinition
{
    public required string Id { get; init; }
    public required string Number { get; init; }
    public required string NameZh { get; init; }
    public string? ImageUrl { get; init; }
    public required string CardType { get; init; }
    public required string Product { get; init; }
    public required string Faction { get; init; }
    public int? Cost { get; init; }
    public int? Hp { get; init; }
    public int? Troops { get; init; }
    public int? DisasterLevel { get; init; }
    public int? TrialValue { get; init; }
    public int DeckLimit { get; init; } = 3;
    public List<string> Traits { get; init; } = [];
    public string? Profession { get; init; }
    public string? Effect { get; init; }
}

public sealed class L12PresetDeckDefinition
{
    public required string Name { get; init; }
    public required string MasterId { get; init; }
    public required List<string> CardIds { get; init; }
    public required List<string> MoraleIds { get; init; }
    public List<string> SpecialIds { get; init; } = [];
}

public sealed class L12CustomDeckSubmission
{
    public string Name { get; init; } = string.Empty;
    public string MasterId { get; init; } = string.Empty;
    public List<string> CardIds { get; init; } = [];
    public List<string> MoraleIds { get; init; } = [];
    public List<string> SpecialIds { get; init; } = [];
}

public sealed class L12RoomOptions
{
    // Empty values are intentional: the room manager authoritatively normalizes legacy/partial payloads
    // into a friendly-room scope; clients cannot select ranked rules or inject a season disaster pool here.
    public string MatchModeId { get; init; } = string.Empty;
    public string Spectating { get; init; } = string.Empty;
    public string HandVisibility { get; init; } = string.Empty;
    public string DisasterMode { get; init; } = string.Empty;
    public bool UseCardRestrictions { get; init; }
}

public sealed class L12CardInstance
{
    public required string InstanceId { get; init; }
    public required string CardId { get; init; }
    public required string Name { get; init; }
    public required string CardType { get; init; }
    public required string Faction { get; init; }
    public string? ImageUrl { get; init; }
    public int Cost { get; init; }
    /// <summary>卡面是否实际印刷费用；与支付计算使用的数值0分开保存。</summary>
    public bool HasPrintedCost { get; init; } = true;
    public int CostModifier { get; set; }
    /// <summary>由当前公开场面持续产生的费用修正，与限时/一次性 CostModifier 分层计算。</summary>
    public int ContinuousCostModifier { get; set; }
    /// <summary>当前场面下从手牌打出此牌实际需要支付的费用，仅用于快照显示。</summary>
    public int? PlayCost { get; set; }
    /// <summary>当前公开场面下禁止从手牌打出此牌的权威原因；为空表示未被静态规则禁止。</summary>
    public string? PlayBlockedReason { get; set; }
    public string? EffectText { get; init; }
    public int BaseTroops { get; init; }
    public int Troops { get; set; }
    public int ContinuousTroopsModifier { get; set; }
    /// <summary>当前条件声明的持续正兵力层总量；伤害只消耗其未消耗部分。</summary>
    public int ContinuousTroopsBonusGranted { get; set; }
    /// <summary>当前持续正兵力层已经吸收的伤害；条件失效时不得再次扣除此部分。</summary>
    public int ContinuousTroopsBonusConsumed { get; set; }
    /// <summary>持续负兵力修正单独保存，避免与可消耗的正兵力层混算。</summary>
    public int ContinuousTroopsPenalty { get; set; }
    /// <summary>按实际持续效果来源保存正兵力层；不同来源失效时只移除自己的未消耗部分。</summary>
    public Dictionary<string, L12TroopsBonusLayer> ContinuousTroopsBonusLayers { get; init; } = new(StringComparer.Ordinal);
    public int? SetTroopsValue { get; set; }
    public int SetTroopsUntilTurn { get; set; } = -1;
    public int DisasterLevel { get; init; }
    public int TrialValue { get; init; }
    public List<string> Traits { get; init; } = [];
    public string? Profession { get; init; }
    /// <summary>随位置或持续效果变化后的当前职介；离场时恢复印刷职介。</summary>
    public string? EffectiveProfession { get; set; }
    /// <summary>离场前最后一次公开场面中的派生职介，供离场触发读取。</summary>
    public string? LastKnownEffectiveProfession { get; set; }
    /// <summary>离场前是否满足远程进攻配置，避免重置后按印刷文本误判位置条件。</summary>
    public bool LastKnownWasRanged { get; set; }
    /// <summary>离场前叠放卡实例快照；离场触发不得依赖已清空的 AttachedCards。</summary>
    public List<string> LastKnownAttachedCardIds { get; set; } = [];
    public int? OwnerIndex { get; set; }
    public bool HasCharge { get; set; }
    public bool HasStrongAttack { get; set; }
    public bool HasSureHit { get; set; }
    public bool HasShock { get; set; }
    public int AttackNoLossUntilTurn { get; set; } = -1;
    public int NextAttackNoLossUses { get; set; }
    public int ReadyAfterNextKillUntilTurn { get; set; } = -1;
    public string? ReadyAfterNextKillSourceName { get; set; }
    public int SureHitAgainstLegionsUntilTurn { get; set; } = -1;
    public int CannotReadyByEffectUntilTurn { get; set; } = -1;
    public int DiscardAtEndOfTurnUntilTurn { get; set; } = -1;
    public bool Hidden { get; set; }
    /// <summary>仅用于按观察者投影的快照；权威状态中的盖伏卡保持 false。</summary>
    public bool IdentityKnown { get; set; }
    public bool Tapped { get; set; }
    public int SummonRound { get; set; }
    public int LastMovedTurn { get; set; } = -1;
    public int LastCavalryMoveTurn { get; set; } = -1;
    /// <summary>月读：本回合该军团由后排位移至前排的累计次数。</summary>
    public int TsukuyomiFrontMoveBonusCount { get; set; }
    public int TsukuyomiFrontMoveBonusTurn { get; set; } = -1;
    /// <summary>高文已结算的本回合后续进攻主宰伤害增量。</summary>
    public int GawainMasterDamageBonus { get; set; }
    public int GawainMasterDamageBonusUntilTurn { get; set; } = -1;
    public int CannotUntapUntilRound { get; set; }
    public int CannotRespondUntilRound { get; set; }
    public int SetRound { get; set; }
    public int AttacksThisTurn { get; set; }
    public int TrialProgress { get; set; }
    public bool TrialCompleted { get; set; }
    public bool CannotAttack { get; set; }
    public bool CannotSupport { get; set; }
    /// <summary>孙悟空等主宰临时作为军团登场时的权威标记。</summary>
    public bool IsMasterLegion { get; set; }
    /// <summary>当前公开场面中实际生效的关键词；由快照层按位置与限时状态计算。</summary>
    public List<string> ActiveKeywords { get; set; } = [];
    /// <summary>当前公开场面中实际生效的短期状态图标；由快照层权威派生。</summary>
    public List<string> StatusIcons { get; set; } = [];
    public List<L12StatusEffectView> StatusEffects { get; set; } = [];
    public int CanAttackBackAndMasterUntilTurn { get; set; } = -1;
    public int CanAttackMasterOnSummonUntilTurn { get; set; } = -1;
    public int CanAttackLegionsOnSummonUntilTurn { get; set; } = -1;
    /// <summary>由限时效果赋予的挑畔持续到哪个回合结束；前排判定仍由进攻规则实时计算。</summary>
    public int TauntUntilTurn { get; set; } = -1;
    public bool TauntRequiresFrontRow { get; set; }
    /// <summary>若不小于0，则挑衅在该玩家的下个回合开始时精确失效；用于兼容额外回合。</summary>
    public int TauntExpiresAtPlayerTurnStart { get; set; } = -1;
    public int ImmortalUses { get; set; }
    public int ImmortalUntilTurn { get; set; } = -1;
    public bool ImmortalRequiresFrontRow { get; set; }
    /// <summary>若不小于0，则免死在该玩家的下个回合开始时精确失效；用于兼容额外回合。</summary>
    public int ImmortalExpiresAtPlayerTurnStart { get; set; } = -1;
    public int SuppressDeathUntilTurn { get; set; } = -1;
    public List<L12AbilityView> Abilities { get; set; } = [];
    public List<L12TimedModifier> TimedModifiers { get; init; } = [];
    public List<L12CardInstance> AttachedCards { get; init; } = [];

    public int CurrentCost => Math.Max(0, Cost + CostModifier + ContinuousCostModifier);
    /// <summary>场面兵力 UI 的比较基准；设定兵力不是兵力增益。</summary>
    public int DisplayBaseTroops => SetTroopsValue ?? BaseTroops;
    public bool HasRangeBonus => L12StructuredCardRules.HasAnyRowRangeBonus(this);
    public bool HasRangedNoLoss => L12StructuredCardRules.HasAnyRowRangedNoLoss(this);
    public bool HasAttackNoLoss => L12StructuredCardRules.HasAnyRowAttackNoLoss(this);
    public bool CannotBeRanged => L12StructuredCardRules.CannotBeRangedInAnyRow(this);
    public bool HasTrait(string trait) => Traits.Contains(trait, StringComparer.Ordinal);

    public L12CardInstance Clone() => (L12CardInstance)MemberwiseClone();
}

public sealed record L12AbilityView(
    string Id,
    string Label,
    bool Enabled = true,
    string? DisabledReason = null,
    bool TriggerOnly = false);

/// <summary>卡面短期状态的结构化投影；Kind 决定图标，Label/Source 用于提示。</summary>
public sealed record L12StatusEffectView(string Kind, string Label, string? Source = null);

public sealed class L12TimedModifier
{
    public int TroopsDelta { get; init; }
    /// <summary>正兵力加成在当前回合已经吸收的伤害；回合结束清伤时与仍有效层一并归零。</summary>
    public int ConsumedTroopsBonus { get; set; }
    public int CostDelta { get; init; }
    public required int ExpiresAfterTurn { get; init; }
    public required string Source { get; init; }
}

public sealed class L12MoraleCard
{
    public required string InstanceId { get; init; }
    public required string CardId { get; init; }
    public bool Tapped { get; set; }
    // 奥林匹斯士气的卡面与活跃/休整是两个独立维度：
    // false = 士气面，true = 神力面；Tapped 只描述活跃/休整。
    public bool IsGodPower { get; set; }
    public int CannotUntapUntilRound { get; set; }
}

public sealed class L12PlayerState
{
    public int PlayerIndex { get; init; }
    public required string Name { get; init; }
    public required string DeckName { get; init; }
    public required string Faction { get; init; }
    public required string MasterId { get; init; }
    public required string MasterName { get; init; }
    public string? MasterImageUrl { get; init; }
    public int Hp { get; set; }
    public int MaxHp { get; init; }
    public bool MasterTapped { get; set; }
    public List<L12CardInstance> Library { get; } = [];
    public List<L12CardInstance> Hand { get; } = [];
    public List<L12MoraleCard> MoraleDeck { get; } = [];
    public List<L12MoraleCard> Morale { get; } = [];
    public L12CardInstance?[][] Field { get; } =
    [
        new L12CardInstance?[3],
        new L12CardInstance?[3],
    ];
    public L12CardInstance? Relic { get; set; }
    public List<L12CardInstance> ExtraRelics { get; } = [];
    public List<L12CardInstance> Resolving { get; } = [];
    public List<L12CardInstance> Graveyard { get; } = [];
    public List<L12CardInstance> Removed { get; } = [];
    public L12S2SpecialZones SpecialZones { get; } = new();
    public HashSet<string> UsedAbilities { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int? NextLegionChargeMaxCost { get; set; }
    public int FreeTacticCount { get; set; }
    public int TemporaryMorale { get; set; }
    public bool BackRowCannotSupport { get; set; }
    public string? LastActiveTacticCardId { get; set; }
    public int ReturnedMoraleThisTurn { get; set; }
    public int MasterDamageTakenThisTurn { get; set; }
    public int NextFactionLegionDiscount { get; set; }
    public int NextS2SunDisasterLegionDiscount { get; set; }
    public int NextS2OlympusLegionDiscount { get; set; }
    public int NextS2PromotionGodPowerDiscount { get; set; }
    /// <summary>平阳昭公主：本回合主宰对对方主宰造成的下一次伤害变为2。</summary>
    public int NextMasterDamageToOpponentBecomesTwoUntilTurn { get; set; } = -1;
    public int S2ArthurDiscountUntilTurn { get; set; } = -1;
    public int FactionMoraleAdditionForbiddenUntilTurn { get; set; } = -1;
    public int MasterCannotBeAttackedUntilTurn { get; set; } = -1;
    public int MasterCannotBeAttackedExpiresAtPlayerTurnStart { get; set; } = -1;
    public int TombNamedLegionsLeftThisTurn { get; set; }
    public int NextActiveTacticSurcharge { get; set; }
    public bool MulliganDone { get; set; }
    public bool TrialOrderDone { get; set; }
    /// <summary>雷神索尔发动第二项效果后，本局不能再因效果增加血量。</summary>
    public bool MasterCannotHeal { get; set; }
    /// <summary>玛格丽特一世：在指定回合内不能因军团效果增加血量。</summary>
    public int LegionEffectHealForbiddenUntilTurn { get; set; } = -1;
}

public sealed class L12S2SpecialZones
{
    public int Runes { get; set; }
    public int TrialLevel { get; set; }
    public int TrialCapacity { get; set; }
    public List<L12CardInstance> GodPower { get; } = [];
    public List<L12CardInstance> Trials { get; } = [];
    /// <summary>伊西斯以主宰效果完成的五种卡诺匹斯圣物进度；不占用通常圣物区。</summary>
    public List<L12CardInstance> CanopicProgress { get; } = [];
}

public sealed record L12AttackTarget(string Type, string? InstanceId = null);

public sealed class L12PendingDefense
{
    public required int AttackerPlayer { get; init; }
    public required string AttackerInstanceId { get; init; }
    public required L12AttackTarget Target { get; set; }
    /// <summary>可持久化的进攻子阶段；快照和重连均以此为唯一流程依据。</summary>
    public L12CombatStage Stage { get; set; } = L12CombatStage.AttackerAttackTiming;
    /// <summary>双方【进攻时】时点全部结束、进入防御时冻结的本次进攻数值。</summary>
    public int AttackValue { get; set; }
    public bool IsRanged { get; init; }
    public bool RangedNoLoss { get; init; }
    public bool AttackNoLoss { get; init; }
    public bool SureHit { get; set; }
    public int MasterDamage { get; set; } = 1;
    /// <summary>理查的独立抵挡费用段成功结算后，才对本次进攻生效。</summary>
    public bool RichardDefenseTaxActive { get; set; }
    public int TemporaryAttackerTroopsBonus { get; set; }
    /// <summary>贯穿等规则生成的进攻仍进入通常响应/抵挡流程，但不会建立【进攻时】卡效。</summary>
    public bool SuppressAttackTriggers { get; set; }
    /// <summary>同一次交战内“即将阵亡”替代效果的决定；true=代替，false=不代替。</summary>
    public Dictionary<string, bool> LethalReplacementDecisions { get; } = new(StringComparer.Ordinal);
    public List<string> DeclaredBlockIds { get; } = [];
    public string? DeclaredSupportId { get; set; }
    public bool ForceInvalidDefense { get; set; }
    /// <summary>佣兵部队等响应已抵挡进攻；仍须结算已发动的【进攻时】效果。</summary>
    public bool BlockedByResponse { get; set; }
    public bool DefenderAttackTimingOpened { get; set; }
    public bool AttackerAfterAttackStarted { get; set; }
    public bool StageEffectsQueued { get; set; }
    /// <summary>战斗伤害已确认阵亡、等待各自触发完成后才进入墓地的实例。</summary>
    public string? DefeatedAttackerInstanceId { get; set; }
    public string? DefeatedDefenderInstanceId { get; set; }
}

public sealed class L12TroopsBonusLayer
{
    public int Granted { get; set; }
    public int Consumed { get; set; }
}

public sealed class L12Prompt
{
    public required string PromptId { get; init; }
    public required int PlayerIndex { get; init; }
    public required string Kind { get; init; }
    public required string Text { get; init; }
    public required List<string> ValidChoices { get; init; }
    public int MinChoose { get; init; }
    public int MaxChoose { get; init; }
    public bool IsPrivate { get; init; }
    public required string Continuation { get; init; }
    public string? StackItemId { get; init; }
    public Dictionary<string, string> Data { get; init; } = [];
    /// <summary>
    /// 玩家界面专用的自然语言选项标签。规则提交始终使用 ValidChoices 中的稳定协议值；
    /// 客户端不得把协议值、Continuation 或 Data 中的 action 标识直接显示给玩家。
    /// </summary>
    public Dictionary<string, string> ChoiceLabels { get; init; } = [];
    /// <summary>
    /// 服务端专用的匿名选项映射。公开快照只投影 ValidChoices 与 Data，绝不传输此映射；
    /// 用于从随机槽位恢复隐藏区域中的真实实例，避免客户端获得手牌顺序或实例标识。
    /// </summary>
    public Dictionary<string, string> HiddenChoiceMap { get; init; } = [];
}

public sealed class L12StackItem
{
    public required string StackItemId { get; init; }
    public required int Controller { get; init; }
    public required string SourceInstanceId { get; init; }
    public required string SourceCardId { get; init; }
    public required string SourceName { get; init; }
    public required string Trigger { get; init; }
    public required string Text { get; init; }
    /// <summary>由触发候选传入的来源最后已知快照，不参与区域归属。</summary>
    public L12CardInstance? SourceSnapshot { get; init; }
    public int Step { get; set; }
    public bool Negated { get; set; }
    public List<string> Targets { get; } = [];
    public Dictionary<string, string> Data { get; } = [];
}

public sealed class L12PendingActivation
{
    public required string ActivationId { get; init; }
    public required int Controller { get; init; }
    public required string SourceInstanceId { get; init; }
    public required string SourceCardId { get; init; }
    public required string Ability { get; init; }
    public required string Text { get; init; }
    public required List<string> ValidChoices { get; init; }
    public int MinChoose { get; init; } = 1;
    public int MaxChoose { get; init; } = 1;
    public List<L12ActivationSelectionStep> SelectionSteps { get; init; } = [];
    public int CurrentStep { get; set; }
    public List<string> DeclaredTargets { get; } = [];
    /// <summary>
    /// 复合效果按声明键保存每一步的选择边界。旧主动/触发流程继续读取扁平的
    /// DeclaredTargets；事务化复合计划不得再靠实例类型猜测“哪个目标属于哪一段”。
    /// </summary>
    public Dictionary<string, List<string>> DeclaredValues { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// 非空时表示这不是主动效果，而是尚未进入堆叠的触发效果声明。
    /// 目标与费用全部声明完成后，才会把对应候选压入堆叠。
    /// </summary>
    public string? TriggerCandidateId { get; init; }
    /// <summary>非空时表示这是尚未支付费用、尚未入栈的手牌打出声明。</summary>
    public string? PlayCardInstanceId { get; init; }
    /// <summary>
    /// 非空时表示复合战术已由另一效果免费打出，声明完成或取消后必须
    /// 恢复该父效果，不得把已离开隐藏区的卡或父堆栈卡死。
    /// </summary>
    public string? CommittedParentStackItemId { get; set; }
    public string? CommittedCompletion { get; set; }
    /// <summary>非空时表示这是尚未揭示、尚未入栈的响应卡目标声明。</summary>
    public string? ResponseTargetStackItemId { get; init; }
}

public sealed class L12FreeMasterActivation
{
    public required int Controller { get; init; }
    public required string Ability { get; init; }
    public required string SourceInstanceId { get; init; }
}

public sealed class L12ActivationSelectionStep
{
    public required string Kind { get; init; }
    public required string Text { get; init; }
    public required List<string> ValidChoices { get; init; }
    public int MinChoose { get; init; } = 1;
    public int MaxChoose { get; init; } = 1;
    /// <summary>
    /// 是否允许取消整次声明。包含后续强制独立段的触发会关闭此项，并以显式 mode:none
    /// 仅跳过可选段，避免把同一张卡的强制后段一并吞掉。
    /// </summary>
    public bool AllowCancel { get; init; } = true;
    /// <summary>
    /// 合法候选数量恰好等于固定选择数量时，服务端直接记录整个集合而不弹出无意义选择。
    /// 仅用于“必须选择全部现有公开对象”的声明步；候选更多时仍由玩家明确选择。
    /// </summary>
    public bool AutoSelectWhenExact { get; init; }
    /// <summary>复合效果预声明中的稳定字段名；为空时保持旧的扁平声明协议。</summary>
    public string? DeclarationKey { get; init; }
    /// <summary>动态声明步读取哪一个已声明字段作为公开对象来源。</summary>
    public string? ReferenceDeclarationKey { get; init; }
    /// <summary>引用列表至少达到此数量才执行本步；用于“最多选择多张对象”后逐个声明位置。</summary>
    public int MinimumReferenceCount { get; init; }
    /// <summary>引用的稳定数值选项至少达到此值才执行本步；用于可变数量费用后的逐项目标声明。</summary>
    public int MinimumReferenceNumericValue { get; init; }
    /// <summary>从引用选项解析数值时使用的稳定前缀，例如 rune-count:。</summary>
    public string? ReferenceNumericChoicePrefix { get; init; }
    /// <summary>本步对应引用列表中的第几个对象；用于公共多对象位移的逐一位置声明。</summary>
    public int ReferenceChoiceIndex { get; init; }
    /// <summary>引用字段为 mode:none 时跳过本步；用于可选登场对象之后的战场/位置声明。</summary>
    public bool SkipWhenReferenceIsNone { get; init; }
    /// <summary>公开位置属于哪位玩家的战场；为空时沿用能力控制者。</summary>
    public int? TargetPlayerIndex { get; init; }
    /// <summary>动态费用目标筛选阈值；只承载规则数值，不承载卡牌实例。</summary>
    public int? CostThreshold { get; init; }
    /// <summary>仅用于界面呈现；规则判断始终使用稳定的 ValidChoices 标识。</summary>
    public Dictionary<string, string> ChoiceLabels { get; init; } = [];
    /// <summary>
    /// 当前步骤依赖紧邻的前一步选择；前一步合法选择 0 项时直接跳过本步骤。
    /// 用于“选择最多 1 张军团登场”这类只有选中军团后才需要声明位置的效果。
    /// </summary>
    public bool SkipWhenPreviousStepEmpty { get; init; }
    /// <summary>
    /// 非空时，仅当此前已经声明该稳定选项才执行本步骤。
    /// 用于“先选模式，再仅为强模式声明额外费用对象”的公共预声明流程。
    /// </summary>
    public string? RequiredDeclaredChoice { get; init; }
}

public sealed class L12TriggerCandidate
{
    public required string CandidateId { get; init; }
    public required int Controller { get; init; }
    public required string SourceInstanceId { get; init; }
    public required string SourceCardId { get; init; }
    public required string SourceName { get; init; }
    public required string Trigger { get; init; }
    public required string Text { get; init; }
    /// <summary>触发时来源的最后已知快照；来源随后离场或衍生消灭时仍由此快照结算。</summary>
    public L12CardInstance? SourceSnapshot { get; init; }
    public Dictionary<string, string> Data { get; init; } = [];
}

public sealed class L12TriggerBatch
{
    public required string BatchId { get; init; }
    public required int Controller { get; init; }
    public required List<L12TriggerCandidate> Candidates { get; init; }
}

/// <summary>
/// 由规则内核建立的权威动作事件。卡效只能响应这里登记的时点，不能再从日志文本或
/// UI 操作反推时机。事件先进入堆叠，响应完成后才由规则内核提交其实际状态变更。
/// </summary>
public sealed class L12AuthorityEvent
{
    public required string EventId { get; init; }
    public required string Type { get; init; }
    public required int ActorPlayer { get; init; }
    public int? SubjectPlayer { get; init; }
    public string? SourceInstanceId { get; init; }
    public string? TargetInstanceId { get; init; }
    public string? OriginZone { get; init; }
    public string? DestinationZone { get; init; }
    public bool CausedByEffect { get; init; }
    public bool Resolved { get; set; }
    public Dictionary<string, string> Data { get; init; } = [];
}

public sealed class L12ResponseWindow
{
    public int PriorityPlayer { get; set; }
    public int ConsecutivePasses { get; set; }
}

public sealed record L12ActionEvent(
    long Sequence,
    string Type,
    int? PlayerIndex,
    string Text,
    L12CardInstance[] Cards);

public sealed class L12GameState
{
    public required string MatchId { get; init; }
    public required string RoomCode { get; init; }
    public required int Seed { get; init; }
    public required L12PlayerState[] Players { get; init; }
    public required L12OperationsPolicySnapshot OperationsPolicy { get; init; }
    public int ActivePlayer { get; set; }
    public int FirstPlayer { get; set; }
    public int DiceWinner { get; set; }
    public int[] InitiativeRolls { get; set; } = [0, 0];
    public L12Phase Phase { get; set; } = L12Phase.Initiative;
    public int Round { get; set; } = 1;
    public int TurnSerial { get; set; }
    public int DisasterValue { get; set; }
    public string DisasterMode { get; init; } = "all";
    public List<L12CardInstance> DisasterPool { get; } = [];
    public List<L12CardInstance> DisasterDeck { get; } = [];
    public List<L12CardInstance> BannedDisasters { get; } = [];
    public List<L12CardInstance> RemovedDisasters { get; } = [];
    public List<L12CardInstance> SelectedDisasters { get; } = [];
    public List<L12CardInstance> RevealedDisasters { get; } = [];
    public List<L12CardInstance> ChosenDisasters { get; } = [];
    /// <summary>测试沙盒自定天灾的稳定四槽清单；第四槽固定为最终天灾〈堙灭〉。</summary>
    public List<L12CardInstance> CustomDisasters { get; } = [];
    public Dictionary<string, int> ChosenDisasterOwners { get; } = [];
    public L12CardInstance? ActiveDisaster { get; set; }
    /// <summary>〈天地异变〉持续期间牌库是否已按权威列表翻转，防止重连或 GM 刷新重复翻转。</summary>
    public bool LibrariesReversedByDisaster { get; set; }
    public int DisasterPreparationStep { get; set; }
    public L12PendingDefense? PendingDefense { get; set; }
    /// <summary>贯穿等击杀时生成进攻使用的可恢复父交战栈。</summary>
    public List<L12PendingDefense> SuspendedCombatContexts { get; } = [];
    public List<L12Prompt> PendingPrompts { get; } = [];
    public List<L12PendingActivation> PendingActivations { get; } = [];
    public L12FreeMasterActivation? FreeMasterActivation { get; set; }
    public List<L12StackItem> EffectStack { get; } = [];
    public List<L12StackItem> DeferredEffectStack { get; } = [];
    public List<L12TriggerBatch> PendingTriggerBatches { get; } = [];
    public List<L12TriggerCandidate> PendingTriggerStackCandidates { get; } = [];
    public List<L12AuthorityEvent> AuthorityEvents { get; } = [];
    public L12ResponseWindow? ResponseWindow { get; set; }
    public bool IsResolvingStack { get; set; }
    public bool ResumeTurnStartAfterStack { get; set; }
    public bool ResumeGmResetAfterStack { get; set; }
    /// <summary>防止同一回合开始流程因同步结算重入而重复执行同一张天灾的回合开始效果。</summary>
    public int LastTurnStartDisasterEffectTurn { get; set; } = -1;
    public string? LastTurnStartDisasterEffectInstanceId { get; set; }
    public bool CheckDisasterAfterStack { get; set; }
    public int ExtraTurnsForPlayer { get; set; } = -1;
    public int CounterTacticsDisabledUntilTurnSerial { get; set; } = -1;
    public int CounterTacticsDisabledExpiresAtPlayerTurnStart { get; set; } = -1;
    public int? Winner { get; set; }
    public string? WinnerReason { get; set; }
    public long Revision { get; set; }
    public long EventSequence { get; set; }
    public long PromptSequence { get; set; }
    public long StackSequence { get; set; }
    public long ActivationSequence { get; set; }
    public long TriggerBatchSequence { get; set; }
    public long AuthorityEventSequence { get; set; }
    public L12ActionEvent? LastAction { get; set; }
    public List<L12ActionEvent> Events { get; } = [];
    public List<string> Log { get; } = [];
}

public sealed record L12GameSnapshot(
    string MatchId,
    string RoomCode,
    long OperationsPolicyVersion,
    int You,
    long Revision,
    int ActivePlayer,
    int FirstPlayer,
    int DiceWinner,
    int[] InitiativeRolls,
    L12Phase Phase,
    int Round,
    int TurnSerial,
    string DisasterMode,
    int DisasterValue,
    object? ActiveDisaster,
    object[] DisasterDeck,
    object[] BannedDisasters,
    object[] RemovedDisasters,
    object[] RevealedDisasters,
    object[] ChosenDisasters,
    object[] SessionDisasters,
    int DisasterPreparationStep,
    object? WaitingPrompt,
    object[] Prompts,
    object[] EffectStack,
    object? PendingDefense,
    int? Winner,
    string? WinnerReason,
    object[] Players,
    L12ActionEvent? LastAction,
    L12ActionEvent[] RecentEvents,
    Dictionary<string, string[]> LegalAttackTargets,
    string StateHash);

public sealed record L12Command(
    string Type,
    string? CardInstanceId = null,
    int? Row = null,
    int? Slot = null,
    L12AttackTarget? Target = null,
    List<string>? CardInstanceIds = null,
    List<string>? TopCardInstanceIds = null,
    List<string>? BottomCardInstanceIds = null,
    string? SupportInstanceId = null,
    string? PromptId = null,
    string? Choice = null,
    string? Ability = null,
    string? Destination = null,
    int? TargetPlayerIndex = null);

/// <summary>
/// 单人测试沙盒专用的服务端权威调试指令。该结构不进入普通 gameAction；
/// 房间管理器只会在 IsSandbox 且调用会话为沙盒控制者时转交给规则内核。
/// </summary>
public sealed record L12GmCommand(
    string Type,
    int TargetPlayer = 0,
    string? CardId = null,
    string? CardInstanceId = null,
    string? Destination = null,
    int? Row = null,
    int? Slot = null,
    int? Value = null,
    string? Phase = null,
    bool TriggerEffects = true,
    string? TargetInstanceId = null);

public sealed record L12SandboxRequest(
    L12CustomDeckSubmission? PlayerDeck = null,
    L12CustomDeckSubmission? OpponentDeck = null,
    string DisasterMode = "none");

public sealed record CommandResult(bool Accepted, string? Error = null)
{
    public static CommandResult Ok() => new(true);
    public static CommandResult Reject(string error) => new(false, error);
}
