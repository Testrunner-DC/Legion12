import type { ActiveTutorialState, Card, GameState, PlayerView, Prompt } from '../types'

export const tutorialPracticeCardId = 'ST01-05'

export function tutorialPaymentSelectionCount(identities: Iterable<string | null>) {
  return new Set(Array.from(identities).filter(identity => Boolean(identity))).size
}

export interface TutorialSceneSnapshot {
  revision: number
  practiceInstanceId: string | null
  practicePosition: string | null
  practiceTapped: boolean | null
  activeMorale: number
  moraleLimit: number
  spendableMorale: number
  spendableMoraleAuthoritative: boolean
  selfMasterHp: number
  enemyMasterHp: number
  pendingAttackValue: number | null
  pendingDefenseTarget: string | null
  handCount: number
  libraryCount: number
}

export interface TutorialSceneFacts extends TutorialSceneSnapshot {
  self: PlayerView
  enemy: PlayerView
  practiceCard: Card | null
  practiceCost: number | null
  resourcePrompt: Prompt | null
}

export interface TutorialDialoguePage {
  id: string
  eyebrow: string
  title: string
  body: string
  reason: string
  outcome: string
  anchor: TutorialAnchor | null
}

export interface TutorialAnchor {
  selector: string
  label: string
  unavailable: string
  multiple?: boolean
  detailFields?: readonly ('费用' | '兵力' | '血量' | '效果')[]
  detailInstanceId?: string
}

export function cssAttributeValue(value: string | number) {
  return String(value).replaceAll('\\', '\\\\').replaceAll('"', '\\"')
}

export function tutorialPlayerSelector(playerIndex: number) {
  return `[data-tutorial-player-index="${cssAttributeValue(playerIndex)}"]`
}

export function tutorialCardSelector(instanceId: string) {
  return `[data-tutorial-card-instance="${cssAttributeValue(instanceId)}"]`
}

export function tutorialActionSelector(action: string, instanceId?: string | null) {
  const actionSelector = `[data-tutorial-action="${cssAttributeValue(action)}"]`
  return instanceId ? `${actionSelector}[data-tutorial-card-instance="${cssAttributeValue(instanceId)}"]` : actionSelector
}

function selfPlayer(game: GameState) {
  return game.players.find(player => player.playerIndex === game.you) ?? game.players[game.you]
}

function enemyPlayer(game: GameState) {
  return game.players.find(player => player.playerIndex !== game.you) ?? game.players[1 - game.you]
}

export function tutorialOwnedDetailCard(game: GameState, instanceId?: string | null): Card | null {
  if (!instanceId) return null
  const self = selfPlayer(game)
  if (!self) return null
  const cards = [...(self.hand ?? []), ...self.field.flat(), self.relic,
    ...(self.extraRelics ?? []), ...(self.resolving ?? []), ...(self.graveyard ?? [])]
  return cards.find((card): card is Card => Boolean(card && card.instanceId === instanceId
    && (!card.hidden || card.identityKnown === true))) ?? null
}

export function tutorialInstructionBlocked(game: GameState, busy: boolean, presentationBusy: boolean) {
  if (busy) return true
  const ownChoice = game.prompts?.some(prompt => prompt.playerIndex === game.you)
    || game.pendingDefense?.stage === 'DefenseChoice' && game.pendingDefense.attackerPlayer !== game.you
  // A queued reveal can wait for this very choice. Reading its instructions is
  // safe; treating that wait as a chapter-completion barrier remains separate.
  return Boolean(presentationBusy && !ownChoice)
}

function cardAt(player: PlayerView, row: number, slot: number) {
  return player.field[row]?.[slot] ?? null
}

function positionOf(player: PlayerView, instanceId: string | null) {
  if (!instanceId) return null
  for (let row = 0; row < player.field.length; row++) {
    for (let slot = 0; slot < (player.field[row]?.length ?? 0); slot++) {
      if (player.field[row]?.[slot]?.instanceId === instanceId) return `${row}:${slot}`
    }
  }
  return null
}

/**
 * The version-1 server recipe records the first practice legion in its ordered hand as
 * PracticeInstanceId. That private server field is intentionally not projected,
 * so the client mirrors that one frozen recipe choice from the ordered Card DTO
 * and immediately addresses the real DOM by the resulting instanceId. This is
 * not a general rule for selecting among same-name cards.
 */
export function tutorialPracticeCard(tutorial: ActiveTutorialState, game: GameState, preferredInstanceId?: string | null) {
  const self = selfPlayer(game)
  if (preferredInstanceId) {
    const exact = [...(self.hand ?? []), ...self.field.flat().filter((card): card is Card => Boolean(card))]
      .find(card => card.instanceId === preferredInstanceId && !card.hidden)
    return exact ?? null
  }
  const fieldPractice = self.field.flat().find(card => card?.cardId === tutorialPracticeCardId && !card.hidden) ?? null
  if (tutorial.lessonId === 'deploy' || tutorial.lessonId === 'response')
    return self.hand?.find(card => card.cardId === tutorialPracticeCardId && !card.hidden) ?? fieldPractice
  if (tutorial.lessonId === 'move') return cardAt(self, 1, 1) ?? fieldPractice
  return cardAt(self, 0, 0)
}

export function tutorialCardCost(card: Card | null) {
  if (!card) return null
  return card.currentCost ?? card.playCost ?? card.minimumPlayCost ?? card.cost
}

function tutorialResourcePrompt(game: GameState) {
  return game.prompts?.find(prompt => prompt.kind === 'resource-payment'
    || prompt.data?.choiceMode === 'resource-payment'
    || prompt.data?.choiceMode === 'resource-selection') ?? null
}

export function tutorialFacts(tutorial: ActiveTutorialState, game: GameState, preferredInstanceId?: string | null): TutorialSceneFacts {
  const self = selfPlayer(game)
  const enemy = enemyPlayer(game)
  const practiceCard = tutorialPracticeCard(tutorial, game, preferredInstanceId)
  const activeMorale = self.morale.filter(resource => !resource.tapped).length
  const visibleFallback = activeMorale + Math.max(0, Math.floor(self.temporaryMorale ?? 0))
  const spendableMoraleAuthoritative = Number.isFinite(self.spendableResourceCount)
  const spendableMorale = spendableMoraleAuthoritative ? Math.max(0, self.spendableResourceCount!) : visibleFallback
  return {
    self,
    enemy,
    practiceCard,
    practiceInstanceId: practiceCard?.instanceId ?? null,
    practicePosition: positionOf(self, practiceCard?.instanceId ?? null),
    practiceTapped: practiceCard?.tapped ?? null,
    practiceCost: tutorialCardCost(practiceCard),
    activeMorale,
    moraleLimit: self.morale.length,
    spendableMorale,
    spendableMoraleAuthoritative,
    selfMasterHp: self.master.hp,
    enemyMasterHp: enemy.master.hp,
    pendingAttackValue: game.pendingDefense?.attackValue ?? null,
    pendingDefenseTarget: game.pendingDefense?.target.type ?? null,
    handCount: self.handCount ?? self.hand?.length ?? 0,
    libraryCount: self.libraryCount,
    resourcePrompt: tutorialResourcePrompt(game),
    revision: game.revision,
  }
}

export function tutorialSnapshot(facts: TutorialSceneFacts): TutorialSceneSnapshot {
  return {
    revision: facts.revision,
    practiceInstanceId: facts.practiceInstanceId,
    practicePosition: facts.practicePosition,
    practiceTapped: facts.practiceTapped,
    activeMorale: facts.activeMorale,
    moraleLimit: facts.moraleLimit,
    spendableMorale: facts.spendableMorale,
    spendableMoraleAuthoritative: facts.spendableMoraleAuthoritative,
    selfMasterHp: facts.selfMasterHp,
    enemyMasterHp: facts.enemyMasterHp,
    pendingAttackValue: facts.pendingAttackValue,
    pendingDefenseTarget: facts.pendingDefenseTarget,
    handCount: handCount(facts.self),
    libraryCount: facts.self.libraryCount,
  }
}

export function battlefieldPositionLabel(position: string | null) {
  if (!position) return '战场'
  const [row, slot] = position.split(':').map(Number)
  const rowLabel = row === 0 ? '前排' : row === 1 ? '后排' : `第 ${row + 1} 排`
  const slotLabel = ['左格', '中格', '右格'][slot] ?? `第 ${slot + 1} 格`
  return `${rowLabel}${slotLabel}`
}

function exactCardAnchor(facts: TutorialSceneFacts, unavailable: string): TutorialAnchor | null {
  if (!facts.practiceInstanceId) return null
  const exact = `[data-tutorial-card-instance="${cssAttributeValue(facts.practiceInstanceId)}"]`
  const selector = facts.practicePosition
    ? `${tutorialPlayerSelector(facts.self.playerIndex)} [data-tutorial-anchor="field-slot"]${exact}`
    : `.hand-card-wrap${exact}`
  return { selector, label: facts.practiceCard?.name || '练习军团', unavailable }
}

function practiceCardDetailAnchor(): TutorialAnchor {
  return { selector: '.catalog-detail-dialog', label: '练习军团卡牌详情',
    unavailable: '卡牌详情正在打开，请稍候。', detailFields: ['费用', '兵力', '效果'] }
}

function masterAnchor(playerIndex: number, label: string): TutorialAnchor {
  return {
    selector: `${tutorialPlayerSelector(playerIndex)} [data-tutorial-anchor="master"]`,
    label,
    unavailable: '主宰目前不在可见战场内。请先展开对应战场区域。',
  }
}

function moraleAnchor(playerIndex: number): TutorialAnchor {
  return {
    selector: `${tutorialPlayerSelector(playerIndex)} [data-tutorial-anchor="morale"]`,
    label: '我方士气',
    unavailable: '士气区目前不在可见范围。请先展开我方战场或士气面板。',
  }
}

function cardSourceAnchor(playerIndex: number): TutorialAnchor {
  const player = tutorialPlayerSelector(playerIndex)
  return {
    selector: `${player} [data-tutorial-anchor="library"], [data-tutorial-anchor="hand"][data-tutorial-player-index="${cssAttributeValue(playerIndex)}"]`,
    label: '牌库与手牌',
    unavailable: '牌库或手牌目前不在可见范围。请展开我方战场与手牌。',
    multiple: true,
  }
}

function handCount(player: PlayerView) {
  return player.handCount ?? player.hand?.length ?? 0
}

function moraleDeckCount(player: PlayerView) {
  return player.moraleDeckCount ?? player.moraleDeck?.length ?? null
}

function publicResourceSummary(player: PlayerView) {
  const active = player.morale.filter(resource => !resource.tapped).length
  return `公开手牌数 ${handCount(player)}，士气区 ${player.morale.length} 张，其中活跃 ${active} 张`
}

export function tutorialDialoguePages(tutorial: ActiveTutorialState, facts: TutorialSceneFacts,
  baseline: TutorialSceneSnapshot): TutorialDialoguePage[] {
  if (tutorial.status === 'completed') {
    const observedTransition = baseline.revision < facts.revision
    const resourceResult = observedTransition
      ? `活跃士气从 ${baseline.activeMorale} 变为 ${facts.activeMorale}`
      : `当前活跃士气为 ${facts.activeMorale}/${facts.moraleLimit}`
    const result = tutorial.lessonId === 'deploy'
      ? `${facts.practiceCard?.name || '练习军团'}已经在${battlefieldPositionLabel(facts.practicePosition)}登场。${resourceResult}，当前军团为${facts.practiceTapped ? '休整' : '活跃'}。`
      : tutorial.lessonId === 'move'
        ? `${facts.practiceCard?.name || '练习军团'}已经${observedTransition ? `从${battlefieldPositionLabel(baseline.practicePosition)}移动到` : '位于'}${battlefieldPositionLabel(facts.practicePosition)}。${resourceResult}。`
        : `防守已结算。你的主宰当前血量为 ${facts.selfMasterHp}。`
    return [{ id: 'result', eyebrow: '真实结果', title: '场面已经更新', body: result,
      reason: tutorial.lessonId === 'deploy'
        ? '把手牌中的军团打到战场，才能让它成为之后可进攻或防守的场上力量。'
        : tutorial.lessonId === 'move'
          ? '移动用资源换取站位，能带来保护或进攻机会，但不会凭空增加军团兵力。'
          : '防守是在保护主宰或军团与付出手牌、场上兵力之间作选择。',
      outcome: tutorial.lessonId === 'deploy' ? `手牌 ${baseline.handCount}→${handCount(facts.self)}，牌库仍为 ${facts.self.libraryCount}。`
        : tutorial.lessonId === 'move' ? '格位已改变，兵力没有增加。' : `主宰血量 ${baseline.selfMasterHp}→${facts.selfMasterHp}。`,
      anchor: exactCardAnchor(facts, '结果中的练习军团当前不在可见范围，请先展开我方战场。')
        ?? masterAnchor(facts.self.playerIndex, '我方主宰') }]
  }

  if (tutorial.lessonId === 'deploy') return [
    { id: 'scene', eyebrow: '本次场面', title: '先看主宰与目标',
      body: `这是备好资源的练习场面。${facts.self.master.masterName}血量 ${facts.selfMasterHp}/${facts.self.master.maxHp}，先打出一张军团。`,
      reason: '主宰血量降到 0 通常会输掉对局；打出军团能建立保护主宰、继续进攻的场上力量。',
      outcome: '军团会离开手牌，在合法阵地登场。',
      anchor: masterAnchor(facts.self.playerIndex, '我方主宰') },
    { id: 'card-source', eyebrow: '牌从哪里来', title: '从牌库抽到手牌',
      body: `当前牌库 ${facts.self.libraryCount} 张、手牌 ${handCount(facts.self)} 张。本段使用手牌中的军团。`,
      reason: '牌库提供后续可抽到的牌，手牌才是当前可按合法时机使用的牌。',
      outcome: '抽牌会让牌库减少、手牌增加。',
      anchor: cardSourceAnchor(facts.self.playerIndex) },
    { id: 'card', eyebrow: '练习军团', title: `认识${facts.practiceCard?.name || '这张军团'}`,
      body: `${facts.practiceCard?.name || '这张军团'}的费用为 ${facts.practiceCost ?? '—'}，决定本次支付；兵力 ${facts.practiceCard?.troops ?? '—'}，用于进攻与抵挡。`,
      reason: '手牌提供可使用的卡，军团登场后才能在战场参与后续战斗。先区分费用和兵力，才能衡量用多少资源换来怎样的场面。',
      outcome: '费用决定支付，兵力用于进攻与抵挡。',
      anchor: practiceCardDetailAnchor() },
    { id: 'morale', eyebrow: '支付费用', title: '看清活跃与休整',
      body: `士气牌库${moraleDeckCount(facts.self) === null ? '数量未公开' : ` ${moraleDeckCount(facts.self)} 张`}。本场可支付 ${facts.spendableMorale} 点费用；用于支付的活跃士气会转为休整。`,
      reason: '费用限制了同一回合能打出多少卡，选择支付哪些活跃士气也是资源安排。',
      outcome: '支付后，选中的活跃士气转为休整。',
      anchor: moraleAnchor(facts.self.playerIndex) },
  ]

  if (tutorial.lessonId === 'move') return [
    { id: 'scene', eyebrow: '本次场面', title: '普通移动需要支付',
      body: `本场${facts.practiceCard?.name || '练习军团'}在后排。移到前排，可利用对面的空位进攻主宰；移动不会增加兵力。`,
      reason: '移动用资源换站位，可以调整保护范围或创造进攻机会，但不会直接提高兵力。',
      outcome: '移动只改变格位，不增加兵力。',
      anchor: exactCardAnchor(facts, '后排中格的练习军团目前不在可见战场内。') },
    { id: 'morale', eyebrow: '移动代价', title: '先看可用士气',
      body: `${facts.spendableMoraleAuthoritative ? '本场可支付' : '场面可见'}士气 ${facts.spendableMorale}。普通移动需支付1点费用，换取新的站位。`,
      reason: '普通移动不是免费改位；先确认资源，才能判断这次站位交换是否值得。',
      outcome: '支付后活跃士气减少，军团改变格位。',
      anchor: moraleAnchor(facts.self.playerIndex) },
  ]

  if (tutorial.stepId === 'attack') return [
    { id: 'scene', eyebrow: '本次场面', title: '军团向主宰进攻',
      body: `${facts.practiceCard?.name || '练习军团'}位于${battlefieldPositionLabel(facts.practicePosition)}。选择它并点击“进攻”。`,
      reason: '进攻对方主宰是在推进“把对方主宰血量降到 0”这一目标。',
      outcome: '进攻通常会让这张军团休整。',
      anchor: exactCardAnchor(facts, '前排左格的练习军团目前不在可见战场内。') },
    { id: 'target', eyebrow: '进攻目标', title: `对方主宰：${facts.enemy.master.masterName}`,
      body: `对方主宰当前血量 ${facts.enemyMasterHp}/${facts.enemy.master.maxHp}。点它作为本次进攻目标。`,
      reason: '选择主宰作为目标，直接把军团的进攻用于对局的核心胜负目标。',
      outcome: '结算后查看主宰血量与军团状态。',
      anchor: masterAnchor(facts.enemy.playerIndex, '对方主宰') },
  ]

  if (tutorial.stepId === 'end-turn') return [
    { id: 'attack-result', eyebrow: '进攻结果', title: '军团已经完成进攻',
      body: `${facts.practiceCard?.name || '练习军团'}现在为${facts.practiceTapped ? '休整' : '活跃'}，对方主宰血量 ${facts.enemyMasterHp}/${facts.enemy.master.maxHp}。对手当前${publicResourceSummary(facts.enemy)}。`,
      reason: '结束回合会把行动权交给对方，也让你看到进攻之后必须面对的防守取舍。',
      outcome: '结束回合后，对手按规则补充资源并行动。',
      anchor: exactCardAnchor(facts, '完成进攻的军团目前不在可见战场内。') },
  ]

  return [{ id: 'defense', eyebrow: '防守选择', title: '决定抵挡或不抵挡',
    body: `对方以 ${facts.pendingAttackValue ?? '—'} 点兵力进攻${facts.pendingDefenseTarget === 'master' ? '你的主宰' : '你的军团'}；对手当前${publicResourceSummary(facts.enemy)}。选择抵挡或不抵挡。`,
    reason: '抵挡能保护被进攻的主宰或军团，但可能需要投入手牌或场上军团；不抵挡则保留资源并承受结果。',
    outcome: '抵挡可保护目标；不抵挡则承受本次进攻。',
    anchor: { selector: '[data-tutorial-anchor="defense-actions"]', label: '抵挡选择',
      unavailable: '抵挡选择目前被最小化或不在可见范围，请先恢复抵挡面板。' } }]
}
