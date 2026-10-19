import type { Card, GameState, PlayerView, Prompt } from '../types'
import { cssAttributeValue, tutorialActionSelector, tutorialCardSelector, tutorialPlayerSelector,
  type TutorialDialoguePage } from './tutorialGuidance'

export type ResponseTutorialStep = 'response-legion' | 'response-cover' | 'response-end-turn'
  | 'response-choose' | 'response-discard' | 'response-retry' | 'completed'
export interface ResponseTutorialBinding { legionId: string | null; counterId: string | null }
export interface ResponseTutorialFacts extends ResponseTutorialBinding {
  self: PlayerView; legion: Card | null; counter: Card | null; discard: Card | null
  prompt: Prompt | null; activeMorale: number; handCount: number
}
export interface ResponseTutorialDom {
  selected: boolean; playButton: boolean; availableSlots: boolean; endTurn: boolean
  selectedPromptChoice: boolean
}

function ownCards(player: PlayerView) {
  return [...(player.hand ?? []), ...player.field.flat().filter((card): card is Card => Boolean(card)),
    ...(player.graveyard ?? [])].filter(card => !card.hidden || card.identityKnown === true)
}
export function responseTutorialFacts(game: GameState, binding?: ResponseTutorialBinding): ResponseTutorialFacts {
  const self = game.players.find(player => player.playerIndex === game.you) ?? game.players[game.you]!
  const cards = ownCards(self)
  const exact = (id: string | null | undefined, cardId: string) => id
    ? cards.find(card => card.instanceId === id) ?? null : cards.find(card => card.cardId === cardId) ?? null
  const legion = exact(binding?.legionId, 'S01-0116')
  const counter = exact(binding?.counterId, 'S01-0016')
  const prompt = game.prompts?.find(item => item.playerIndex === game.you) ?? null
  const discard = prompt?.kind === 'discard-cost'
    ? self.hand?.find(card => !card.hidden && prompt.validChoices.includes(card.instanceId)) ?? null : null
  return { self, legion, counter, legionId: binding?.legionId ?? legion?.instanceId ?? null,
    counterId: binding?.counterId ?? counter?.instanceId ?? null, discard, prompt,
    activeMorale: self.morale.filter(resource => !resource.tapped).length,
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

export function responseTutorialDialogue(step: string, facts: ResponseTutorialFacts): TutorialDialoguePage[] {
  const morale = { selector: `${tutorialPlayerSelector(facts.self.playerIndex)} [data-tutorial-anchor="morale"]`,
    label: '我方士气', unavailable: '请展开我方士气区。' }
  switch (step) {
    case 'response-legion': return [
      page('response-goal', '先让西施登场',
        `西施当前费用 ${cardCost(facts.legion)}、兵力 ${facts.legion?.troops ?? '—'}。让她在前排登场，再准备一张反击。`,
        '费用决定打出的资源代价，兵力参与攻防；选择前排是本场练习安排，不是所有军团都应放前排。',
        '打出后少一张手牌，换来一张场上军团。',
        facts.legionId ? { selector: handSelector(facts.legionId), label: '西施', unavailable: '请在我方手牌中找到本次西施。' } : null),
      page('response-resource-plan', '为反击留出资源',
        `现在有 ${facts.activeMorale} 张活跃士气、${facts.handCount} 张手牌。打出西施后，还要支付绝对防御的盖伏费用；本场资源不是开局数值。`,
        '手牌是可选的行动，士气才让这些行动能够支付；不能只看手里有多少张牌。',
        '付费会让所选活跃士气转为休整。', morale),
    ]
    case 'response-cover': return [
      page('response-cover-result', '先看西施登场后的变化',
        `西施已在前排，当前兵力 ${facts.legion?.troops ?? '—'}；现在还有 ${facts.activeMorale} 张活跃士气。`,
        '刚才用手牌和费用换来了场上的军团，而不是额外获得兵力或一次进攻。',
        '接下来盖伏反击，保护这张军团。',
        facts.legionId ? { selector: `${fieldSelector(facts.self.playerIndex)}${tutorialCardSelector(facts.legionId)}`, label: '登场的西施', unavailable: '请展开我方战场。' } : null),
      page('response-cover-card', '盖伏是在准备，不是立刻生效',
        `绝对防御是反击战术，当前打出费用 ${cardCost(facts.counter)}。把它盖伏到合法后排位置，并按提示支付费用。`,
        '先付费准备一张以后可以响应的卡，不表示现在已经无效任何效果。',
        '自己知道盖伏的卡是什么；对手此时只能看见盖伏卡。',
        facts.counterId ? { selector: handSelector(facts.counterId), label: '绝对防御', unavailable: '请在我方手牌中找到绝对防御。' } : null),
    ]
    case 'response-end-turn': return [page('response-prepared', '准备好了，把行动权交给对方',
      '西施已登场，绝对防御已盖伏。现在正常结束回合，观察对方公开的行动。',
      '本例跨回合是练习安排，不是另加一条“所有反击都必须等下一回合”的规则。',
      '对方会按正常规则打出战术；在真实响应出现前，我们不提前宣布它的结果。')]
    case 'response-choose': return [
      page('response-started-not-resolved', '发动已经发生，效果还在等待',
        '对方已打出兵临城下并支付费用，但效果仍在等待处理。',
        '发动与成功处理不是一回事。卡的发动与效果的发动也要分开理解。',
        '之后即使效果被无效，也不会把已经发生的发动和费用抹去。'),
      page('response-counter-window', '回应的是效果，不是抵挡进攻',
        '在当前响应中选择刚才盖伏的绝对防御。我们要阻止的是兵临城下的效果，不是一次进攻。',
        '是否响应，要比较保护场面带来的收益与自己付出的手牌。',
        '选择反击后还要支付它要求的费用；此刻不能提前说无效成功。',
        facts.prompt && facts.counterId ? { selector: promptChoice(facts.prompt, facts.counterId), label: '响应中的绝对防御', unavailable: '请恢复当前响应面板。' } : null),
    ]
    case 'response-discard': return [page('response-discard-cost', '用一张手牌，换取阻止效果的机会',
      `盖伏时已经付过士气。现在另选一张合法手牌弃置；本次可先选择${facts.discard?.name ?? '当前费用询问中的卡牌'}。这不是把它打出，不需要再支付它牌面上的打出费用。`,
      '这张手牌原本也有未来的用途；付出它，是为了争取让眼前效果无效，而不是保证效果成功的押金。',
      '确认后才实际支付弃牌费用，并继续处理堆叠；不额外创造不存在的确认步骤。',
      facts.prompt && facts.discard ? { selector: promptChoice(facts.prompt, facts.discard.instanceId), label: '可弃置的手牌', unavailable: '请恢复当前弃牌费用面板。' } : null)]
    case 'completed': return [page('response-result', '比较结果，也别忘了代价',
      `兵临城下的效果已被无效，西施当前兵力 ${facts.legion?.troops ?? '—'}；打出、盖伏及弃牌的费用不会自动退回。`,
      '本次投入了手牌与士气，换来保护军团的结果。下次面对不同卡，也要先看条件、时机、费用与目标，再决定是否响应。',
      '真实结果已提交，教学会自动进入下一步。')]
    case 'response-retry': return [page('response-observe-other-result', '这次看到了另一种结果',
      '这次没有沿预定的反击路线完成。请看场面中的真实变化；已经支付的费用仍按原规则处理。',
      '不响应也可能是合法选择，但与本次“亲手阻止效果”的练习目标不同。',
      '可以重新练习这段场面；不会把这次结果当作完成响应教学。')]
    default: return [page('response-wait', '等待当前练习场面', '请稍候，当前场面尚未准备好。',
      '先确认当前步骤，才能给出准确的操作说明。', '不会根据客户端计时或点击次数跳过练习。')]
  }
}

export function responseTutorialAction(step: string, facts: ResponseTutorialFacts, dom: ResponseTutorialDom): TutorialDialoguePage {
  if (step === 'response-legion' || step === 'response-cover') {
    const cover = step === 'response-cover'
    const id = cover ? facts.counterId : facts.legionId
    const name = cover ? '绝对防御' : '西施'
    if (dom.availableSlots) return page('response-place', cover ? '选择合法后排位置' : '选择合法前排空位',
      cover ? '选择亮起的合法后排位置，再按原支付提示继续。' : '选择亮起的合法前排空位，再按原支付提示继续。',
      cover ? '盖伏是为之后的响应做准备。' : '本例用前排军团观察兵临城下的效果。',
      '选位尚不代表已经付清费用。', { selector: `${fieldSelector(facts.self.playerIndex, cover ? 1 : 0)}.available`, label: '合法阵地', unavailable: '请重新选择本次卡牌。', multiple: true })
    return page('response-select-card', dom.playButton ? `准备打出${name}` : `选择${name}`,
      dom.playButton ? '点击原界面的“打出”，查看本次合法位置。' : `点击我方手牌中高亮的${name}。`,
      '选择只打开本次真实操作入口，不会直接支付费用。', '随后仍需确认合法位置和本次费用。', id ? {
        selector: dom.playButton ? tutorialActionSelector('play-card', id) : handSelector(id), label: name, unavailable: '请展开或滑动我方手牌区。',
      } : null)
  }
  if (step === 'response-end-turn') return page('response-end-turn-action', '正常结束回合',
    '点击“结束回合”，再观察对方公开的行动。', '交接行动权，才能看到这次真实响应机会。',
    '结束回合本身不会完成反击。', { selector: tutorialActionSelector('end-turn'), label: '结束回合', unavailable: '结束回合当前不可用。' })
  const prompt = facts.prompt
  const id = step === 'response-choose' ? facts.counterId : step === 'response-discard' ? facts.discard?.instanceId : null
  if (prompt && id) return page('response-prompt-action', dom.selectedPromptChoice ? '确认本次选择' : step === 'response-choose' ? '选择绝对防御响应' : '选择一张合法手牌弃置',
    dom.selectedPromptChoice ? '核对当前选择，再点击原确认按钮。' : '先选择高亮的合法卡牌。',
    step === 'response-choose' ? '选择这张反击来回应眼前的战术效果。' : '弃掉一张手牌是本次发动的费用，不是打出这张手牌。',
    '确认后等待实际处理结果，不按按钮点击就提前计作完成。', {
      selector: dom.selectedPromptChoice ? `${promptSelector(prompt)} [data-tutorial-action="confirm-prompt"]` : promptChoice(prompt, id),
      label: dom.selectedPromptChoice ? '确认选择' : '合法卡牌', unavailable: '请恢复当前选择面板。',
    })
  return page('response-action-wait', '等待当前结果', '稍等当前操作处理完成，再一起看结果。',
    '操作、费用和效果需要依真实顺序处理。', '只在场面显示结果后继续。')
}
