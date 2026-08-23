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
    public string Spectating { get; init; } = "public";
    public string HandVisibility { get; init; } = "request";
    public string DisasterMode { get; init; } = "all";
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
    public int CostModifier { get; set; }
    /// <summary>由当前公开场面持续产生的费用修正，与限时/一次性 CostModifier 分层计算。</summary>
    public int ContinuousCostModifier { get; set; }
    /// <summary>当前场面下从手牌打出此牌实际需要支付的费用，仅用于快照显示。</summary>
    public int? PlayCost { get; set; }
    public string? EffectText { get; init; }
    public int BaseTroops { get; init; }
    public int Troops { get; set; }
    public int ContinuousTroopsModifier { get; set; }
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
    public bool Tapped { get; set; }
    public int SummonRound { get; set; }
    public int LastMovedTurn { get; set; } = -1;
    public int LastCavalryMoveTurn { get; set; } = -1;
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
    public int CanAttackBackAndMasterUntilTurn { get; set; } = -1;
    public int CanAttackMasterOnSummonUntilTurn { get; set; } = -1;
    public int CanAttackLegionsOnSummonUntilTurn { get; set; } = -1;
    /// <summary>由限时效果赋予的挑畔持续到哪个回合结束；前排判定仍由进攻规则实时计算。</summary>
    public int TauntUntilTurn { get; set; } = -1;
    public int ImmortalUses { get; set; }
    public int ImmortalUntilTurn { get; set; } = -1;
    public int SuppressDeathUntilTurn { get; set; } = -1;
    public List<L12AbilityView> Abilities { get; init; } = [];
    public List<L12TimedModifier> TimedModifiers { get; init; } = [];
    public List<L12CardInstance> AttachedCards { get; init; } = [];

    public int CurrentCost => Math.Max(0, Cost + CostModifier + ContinuousCostModifier);
    public bool HasRangeBonus => EffectText?.Contains("进攻距离+1", StringComparison.Ordinal) == true;
    public bool HasRangedNoLoss => EffectText?.Contains("远程进攻无损", StringComparison.Ordinal) == true;
    public bool HasAttackNoLoss => EffectText?.Contains("进攻无损", StringComparison.Ordinal) == true
        && EffectText?.Contains("远程进攻无损", StringComparison.Ordinal) != true;
    public bool CannotBeRanged => EffectText?.Contains("无法被远程进攻", StringComparison.Ordinal) == true;
    public bool HasTrait(string trait) => Traits.Contains(trait, StringComparer.Ordinal);

    public L12CardInstance Clone() => (L12CardInstance)MemberwiseClone();
}

public sealed record L12AbilityView(
    string Id,
    string Label,
    bool Enabled = true,
    string? DisabledReason = null,
    bool TriggerOnly = false);

public sealed class L12TimedModifier
{
    public int TroopsDelta { get; init; }
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
    public bool IsRanged { get; init; }
    public bool RangedNoLoss { get; init; }
    public bool SureHit { get; set; }
    public int MasterDamage { get; set; } = 1;
    public int TemporaryAttackerTroopsBonus { get; set; }
    public int TemporaryDefenderTroopsPenalty { get; set; }
    /// <summary>贯穿等规则生成的进攻仍进入通常响应/抵挡流程，但不会建立【进攻时】卡效。</summary>
    public bool SuppressAttackTriggers { get; set; }
    /// <summary>同一次交战内“即将阵亡”替代效果的决定；true=代替，false=不代替。</summary>
    public Dictionary<string, bool> LethalReplacementDecisions { get; } = new(StringComparer.Ordinal);
    public List<string> DeclaredBlockIds { get; } = [];
    public string? DeclaredSupportId { get; set; }
    public bool ForceInvalidDefense { get; set; }
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
    /// 当前步骤依赖紧邻的前一步选择；前一步合法选择 0 项时直接跳过本步骤。
    /// 用于“选择最多 1 张军团登场”这类只有选中军团后才需要声明位置的效果。
    /// </summary>
    public bool SkipWhenPreviousStepEmpty { get; init; }
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
    public int DisasterPreparationStep { get; set; }
    public L12PendingDefense? PendingDefense { get; set; }
    public List<L12Prompt> PendingPrompts { get; } = [];
    public List<L12PendingActivation> PendingActivations { get; } = [];
    public L12FreeMasterActivation? FreeMasterActivation { get; set; }
    public List<L12StackItem> EffectStack { get; } = [];
    public List<L12StackItem> DeferredEffectStack { get; } = [];
    public List<L12TriggerBatch> PendingTriggerBatches { get; } = [];
    public List<L12AuthorityEvent> AuthorityEvents { get; } = [];
    public L12ResponseWindow? ResponseWindow { get; set; }
    public bool IsResolvingStack { get; set; }
    public bool ResumeTurnStartAfterStack { get; set; }
    public bool CheckDisasterAfterStack { get; set; }
    public int ExtraTurnsForPlayer { get; set; } = -1;
    public int CounterTacticsDisabledUntilTurnSerial { get; set; } = -1;
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
