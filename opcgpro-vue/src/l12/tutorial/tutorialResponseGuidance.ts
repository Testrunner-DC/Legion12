import type { Card, GameState, PlayerView, Prompt, TutorialProjection } from '../types'
import { cssAttributeValue, tutorialActionSelector, tutorialCardSelector, tutorialPlayerSelector,
  type TutorialDialoguePage } from './tutorialGuidance'

export type ResponseTutorialBinding = NonNullable<TutorialProjection['focus']>
export interface ResponseTutorialFacts {
  self: PlayerView; artifact: Card | null; legion: Card | null; counter: Card | null; discard: Card | null
  artifactId: string | null; legionId: string | null; counterId: string | null
  prompt: Prompt | null; activeMorale: number; handCount: number
}
export interface ResponseTutorialDom {
  selected: boolean; playButton: boolean; availableSlots: boolean; endTurn: boolean
  selectedPromptChoice: boolean; artifactAbility?: boolean
}

export function responseTutorialReturnCost(step: string, facts: ResponseTutorialFacts): boolean {
  return step === 'response-return' && facts.prompt?.kind === 'target-morale'
    && facts.prompt.data?.choiceMode === 'resource-selection'
    && Boolean(facts.counterId && facts.prompt.sourceInstanceId === facts.counterId)
}

export function responseTutorialFacts(game: GameState, binding?: ResponseTutorialBinding): ResponseTutorialFacts {
  const self = game.players.find(player => player.playerIndex === game.you) ?? game.players[game.you]!
  const cards = [...(self.hand ?? []), ...self.field.flat(), self.relic,
    ...(self.extraRelics ?? []), ...(self.resolving ?? []), ...(self.graveyard ?? [])]
    .filter((card): card is Card => Boolean(card && (!card.hidden || card.identityKnown === true)))
  const exact = (id?: string | null) => id ? cards.find(card => card.instanceId === id) ?? null : null
  const prompt = game.prompts?.find(item => item.playerIndex === game.you) ?? null
  const costId = binding?.costCardInstanceId
  const discard = prompt?.kind === 'active-target' && prompt.sourceInstanceId === binding?.artifactInstanceId
    && prompt.presentation?.paymentStatus === 'pending' && costId && prompt.validChoices.includes(costId)
    ? self.hand?.find(card => card.instanceId === costId && !card.hidden) ?? null : null
  // The server reveals the searched identity only when the real search exposes it.
  // Never learn a hidden library card from a matching name or catalog identifier.
  const legionId = binding?.legionInstanceId ?? null
  return { self, artifact: exact(binding?.artifactInstanceId), legion: exact(legionId),
    counter: exact(binding?.counterInstanceId), discard, prompt,
    artifactId: binding?.artifactInstanceId ?? null, counterId: binding?.counterInstanceId ?? null,
    legionId, activeMorale: self.morale.filter(resource => !resource.tapped).length,
    handCount: self.handCount ?? self.hand?.length ?? 0 }
}

const promptSelector = (prompt: Prompt) => `[data-tutorial-prompt-id="${cssAttributeValue(prompt.promptId)}"]`
const promptChoice = (prompt: Prompt, id: string) => `${promptSelector(prompt)} [data-tutorial-choice="${cssAttributeValue(id)}"]`
const handSelector = (id: string) => `.hand-card-wrap${tutorialCardSelector(id)}`
const fieldSelector = (player: number, row?: number) => `${tutorialPlayerSelector(player)} [data-tutorial-anchor="field-slot"]`
  + (row === undefined ? '' : `[data-tutorial-row="${row}"]`)
const cardCost = (card: Card | null) => card?.currentCost ?? card?.playCost ?? card?.minimumPlayCost ?? card?.cost ?? '—'
const page = (id: string, title: string, body: string, reason: string, outcome: string,
  anchor: TutorialDialoguePage['anchor'] = null): TutorialDialoguePage => ({
  id, eyebrow: '第二章 · 卡牌效果与响应', title, body, reason, outcome, anchor,
})
const detail = (id: string | null, label: string, legion = false): TutorialDialoguePage['anchor'] => id ? {
  selector: '.catalog-detail-dialog', detailInstanceId: id,
  detailFields: legion ? ['费用', '兵力', '效果'] : ['费用', '效果'], label, unavailable: '请查看当前卡牌详情。',
} : null

export function responseTutorialDialogue(step: string, facts: ResponseTutorialFacts): TutorialDialoguePage[] {
  switch (step) {
    case 'response-artifact': return [page('response-artifact-card', '用圣物寻找需要的手牌',
      '山河社稷图已在圣物区。发动“弃牌·检索”，用一张手牌换取查看牌库顶部3张、从中选牌的机会。',
      '圣物留场不代表能力会自动发动。', '本段是已备好资源的练习场面，不是标准开局。',
      detail(facts.artifactId, '山河社稷图的费用与效果'))]
    case 'response-search-cost': return [page('response-search-cost-card', '先支付弃牌费用',
      `选择${facts.discard?.name ?? '高亮的合法手牌'}弃置。这是在支付山河社稷图的费用，不是在打出这张牌。`,
      '费用与效果分开处理。', '费用付出后不会因后续效果未成功而自动退回。')]
    case 'response-search-pick': return [page('response-search-card', '把需要的军团加入手牌',
      '从这次看到的3张牌中选择聂隐娘。接下来借助暗度陈仓，让她在对方回合活跃登场。',
      '检索让手牌更贴合下一步计划。', '其他牌仍要按提示安排返回牌库。')]
    case 'response-search-order': return [page('response-search-result', '安排未选中的牌',
      '聂隐娘已加入手牌。其余牌可以调整顺序；本次选择“全部放回顶部”，左侧的牌先抽到。',
      '选牌与余牌返回是同一次效果的不同步骤。', '安排后继续准备反击。',
      detail(facts.legionId, '聂隐娘的费用、兵力与效果', true))]
    case 'response-cover': return [page('response-cover-card', '为对方回合准备反击',
      `暗度陈仓是反击战术，当前费用 ${cardCost(facts.counter)}。将它盖伏到后排，并支付费用；现在还不会使军团登场。`,
      '盖伏是在准备之后的响应。', '对手此时只能看到盖伏卡。', detail(facts.counterId, '暗度陈仓的费用与效果'))]
    case 'response-end-turn': return [page('response-prepared', '把行动权交给对方',
      '暗度陈仓已盖伏，聂隐娘留在手牌。结束回合，观察对方进攻及反击出现的时机。',
      '准备好不等于已经发动。', '原费用不会因为交接回合而退回。')]
    case 'response-pass': return []
    case 'response-defend': return [page('response-defense', '这次先不抵挡',
      '对方正在进攻我方主宰。本次选择“不抵挡”，保留聂隐娘；主宰会先承受这次进攻，再出现暗度陈仓的响应时机。',
      '响应与抵挡是不同选择。', '后续反击不会抹去已经结算的伤害。')]
    case 'response-choose': return [page('response-after-attack', '进攻结束后，再发动反击',
      `我方主宰当前血量 ${facts.self.master.hp}。选择发动暗度陈仓；它不会抵挡或无效刚才的进攻，而是为军团登场创造机会。`,
      '卡的发动与效果的发动要区分；同一效果也可能有多步处理。', '接下来还要支付返还士气的费用。')]
    case 'response-return': return [page('response-return-cost', '返还士气，支付发动费用',
      '选择1张合法士气返还士气牌库。返还会减少场上的士气，不同于支付打出费用时将士气休整。',
      '盖伏费用和本次发动费用是两次不同支付。', '支付后再选择要登场的手牌。')]
    case 'response-summon': return [page('response-entry-card', '选择聂隐娘活跃登场',
      `聂隐娘当前兵力 ${facts.legion?.troops ?? '—'}。这次由暗度陈仓的效果使她活跃登场，不再支付她的打出费用。`,
      '卡牌效果可以改变军团进入战场的方式。', '活跃不等于无条件获得冲锋。', detail(facts.legionId, '聂隐娘的费用、兵力与效果', true))]
    case 'response-position': return [page('response-entry-position', '为军团选择位置',
      '选择亮起的合法空格。军团登场后的站位决定接下来可参与的攻防；本次先放前排。',
      '位置仍受当前效果与真实占位限制。', '不会覆盖已有军团。')]
    case 'completed': return [page('response-result', '看清每一步的代价与结果',
      `聂隐娘已活跃登场，我方主宰当前血量 ${facts.self.master.hp}。弃牌、盖伏和返还士气的费用都保留；刚才的伤害也仍然存在。`,
      '一次响应可能包含费用、选牌与选位，不能把它们当作多次发动。', '本段完成，自动进入下一步。')]
    case 'response-retry': return [page('response-other-result', '这次形成了另一种局面',
      '本次没有完成预定的检索与反击路线。已支付的费用和实际结果保留，可以重新练习。',
      '其他选择可能合法，但不等于完成本段目标。', '不会自动退费或把未完成的步骤记为完成。')]
    default: return [page('response-wait', '等待当前练习场面', '正在准备下一步，请稍候。',
      '先看到场面，再指向操作。', '不会按点击次数跳过步骤。')]
  }
}

export function responseTutorialAction(step: string, facts: ResponseTutorialFacts, dom: ResponseTutorialDom): TutorialDialoguePage {
  const prompt = facts.prompt
  const own = tutorialPlayerSelector(facts.self.playerIndex)
  if (step === 'response-artifact') return page('response-artifact-action', '发动“弃牌·检索”',
    dom.artifactAbility ? '点击高亮的“弃牌·检索”，用一张手牌换取选牌机会。' : '点击我方圣物区的山河社稷图，查看它的能力。',
    '选择圣物只是打开能力。', '发动后进入真实费用选择。', { selector: dom.artifactAbility
      ? `[data-tutorial-ability="artifactSearch"]${tutorialCardSelector(facts.artifactId || '')}`
      : `${own} [data-l12-zone="relic"]`, label: '山河社稷图', unavailable: '请展开我方圣物区。' })
  if (step === 'response-cover') {
    if (dom.availableSlots) return page('response-place', '选择合法后排空位', '点击亮起的后排空位，准备盖伏暗度陈仓。',
      '先选位，再付费。', '盖伏尚未完成。', { selector: `${fieldSelector(facts.self.playerIndex, 1)}.available`,
        label: '后排空位', unavailable: '请重新选择暗度陈仓。', multiple: true })
    return page('response-select-card', dom.playButton ? '打出暗度陈仓' : '选择暗度陈仓',
      dom.playButton ? '点击“打出”，选择盖伏位置。' : '点击高亮的暗度陈仓，准备盖伏。',
      '盖伏须支付打出费用。', '准备之后的响应。', facts.counterId ? {
        selector: dom.playButton ? tutorialActionSelector('play-card', facts.counterId) : handSelector(facts.counterId),
        label: '暗度陈仓', unavailable: '请展开我方手牌。' } : null)
  }
  if (step === 'response-end-turn') return page('response-end-turn-action', '结束回合', '点击“结束回合”，观察对方的进攻。',
    '交接行动权。', '稍后仍要自行选择是否响应。', { selector: tutorialActionSelector('end-turn'), label: '结束回合', unavailable: '结束回合当前不可用。' })
  if (step === 'response-defend') return page('response-defend-action', '选择不抵挡', '这次保留手牌，让主宰承受进攻，观察进攻后的响应。',
    '这是本场练习的选择，并非总应放弃抵挡。', '伤害结算后再响应。', { selector: tutorialActionSelector('decline-defense'), label: '不抵挡', unavailable: '请恢复抵挡面板。' })
  if (step === 'response-position') return page('response-position-action', '选择前排空位', '点击亮起的前排空位，让聂隐娘登场。',
    '效果给出的登场位置仍须合法。', '等待军团实际登场。', { selector: `${fieldSelector(facts.self.playerIndex, 0)}.available`, label: '合法前排', unavailable: '请恢复位置选择。', multiple: true })
  if (step === 'response-search-order' && prompt) return page('response-order-action', '安排返回牌库的顺序', '本次点击“全部放回顶部”；左侧先抽到，可先点击两张牌交换顺序。',
    '其余牌不会凭空消失。', '返回后继续盖伏反击。', { selector: `${promptSelector(prompt)} ${tutorialActionSelector('place-all-top')}`, label: '全部放回顶部', unavailable: '请恢复牌序面板。' })
  const id = step === 'response-search-cost' ? facts.discard?.instanceId
    : step === 'response-search-pick' || step === 'response-summon' ? facts.legionId
      : step === 'response-pass' ? 'pass'
        : step === 'response-choose' ? (prompt?.validChoices.includes('mode:use') ? 'mode:use' : facts.counterId) : null
  if (prompt && id && prompt.validChoices.includes(id)) return page('response-prompt-action', dom.selectedPromptChoice ? '确认选择' : step === 'response-pass' ? '先让过当前响应' : '选择高亮选项',
    step === 'response-pass' ? '本次没有要使用的响应牌，选择“不响应”并确认；这不等于不抵挡。'
      : dom.selectedPromptChoice ? '核对选择并确认，继续费用或效果处理。' : '选择高亮选项，再按当前面板确认。',
    '按当前响应的合法选项操作。', '确认后等待场面变化。', {
      selector: dom.selectedPromptChoice ? `${promptSelector(prompt)} ${tutorialActionSelector('confirm-prompt')}` : promptChoice(prompt, id),
      label: dom.selectedPromptChoice ? '确认选择' : '当前选项', unavailable: '请恢复当前选择面板。' })
  return page('response-action-wait', '等待场面变化', '当前操作正在处理，请稍候。', '费用和效果依次处理。', '处理后进入下一步。')
}
