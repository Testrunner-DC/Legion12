import type { Card, GameState } from '../types'
import { cssAttributeValue, tutorialActionSelector, tutorialCardSelector, tutorialPlayerSelector,
  type TutorialDialoguePage } from './tutorialGuidance'

const page = (id: string, title: string, body: string,
  anchor: TutorialDialoguePage['anchor'] = null): TutorialDialoguePage => ({
  id, eyebrow: '第三章 · 完成一场对局', title, body, anchor,
  reason: '结合当前资源、位置和时机作出选择。', outcome: '完成后直接进入下一步。',
})
export function finaleAttacker(game: GameState): Card | null {
  const own = game.players.find(player => player.playerIndex === game.you)
  return own?.field.flat().find(card => card && !card.hidden
    && game.legalAttackTargets?.[card.instanceId]?.includes('master')) ?? null
}

export function preparationDialogue(game: GameState): TutorialDialoguePage[] {
  const prompt = game.prompts?.find(item => item.playerIndex === game.you)
  if (game.phase === 'Mulligan') return [page('preparation-hand', '调整起始手牌',
    '这套预组有40张主卡和8张士气。现在抽到了6张手牌；可以选牌换掉，本次先保留全部，进入对局。')]
  switch (prompt?.kind) {
    case 'initiative': return [page('preparation-initiative', '决定先后手',
      '骰点决定谁来选择先后手。先攻首回合不抽牌、只追加1张士气；这次选择先攻。')]
    case 'disaster-ban': return [page('preparation-ban', '禁用一张天灾',
      '禁用的天灾不会进入本局。点击卡图查看效果，再从本次可选卡中禁用1张。')]
    case 'disaster-reveal': return [page('preparation-reveal', '看清本局公开的天灾',
      '这些是随机公开的天灾。查看卡图与效果后确认，再继续选用；确认本身不会发动天灾效果。')]
    case 'disaster-pick': return [page('preparation-pick', '选用一张天灾',
      '从剩下的候选中选用1张，加入本局天灾。先看效果与当前主宰，再作选择。')]
    default: return []
  }
}
export function preparationAction(game: GameState, selected: boolean): TutorialDialoguePage {
  const prompt = game.prompts?.find(item => item.playerIndex === game.you)
  if (game.phase === 'Mulligan') return page('preparation-mulligan-action', '确认起始手牌',
    '本次保留全部手牌。确认后，按正常回合流程补充资源。', {
      selector: tutorialActionSelector('confirm-mulligan'), label: '保留全部手牌', unavailable: '请恢复起始手牌面板。',
    })
  if (!prompt) return page('preparation-wait', '等待对方完成准备', '对方完成后，进入下一步。')
  const prefix = `[data-tutorial-prompt-id="${cssAttributeValue(prompt.promptId)}"]`
  if (prompt.kind === 'disaster-reveal' || selected) return page('preparation-confirm-action', '确认当前选择',
    prompt.kind === 'disaster-reveal' ? '看过公开天灾后，点击确认。' : '核对高亮的选择，再点击确认。', {
      selector: `${prefix} ${tutorialActionSelector('confirm-prompt')}`, label: '确认选择', unavailable: '请恢复当前选择面板。',
    })
  return page('preparation-choose-action', prompt.kind === 'initiative' ? '选择先攻' : '选择一张天灾',
    prompt.kind === 'initiative' ? '点击“先攻”，再确认。' : '选择一张候选天灾，再确认本次禁用或选用。', {
      selector: `${prefix} [data-tutorial-choice${prompt.kind === 'initiative' ? '="first"' : ''}]`,
      label: prompt.kind === 'initiative' ? '先攻' : '可选天灾', unavailable: '请恢复当前选择面板。', multiple: prompt.kind !== 'initiative',
    })
}

export function finaleDialogue(step: string, game: GameState): TutorialDialoguePage[] {
  if (step === 'finale-ready') return [page('finale-plan', '把已学的操作串起来',
    '这是已进行数回合的收尾练习，不是标准开局。查看双方血量与我方活跃军团，尝试赢下这场小局。', {
      selector: `${tutorialPlayerSelector(1 - game.you)} [data-tutorial-anchor="master"]`,
      label: '对方主宰血量', unavailable: '请展开对方战场。',
    })]
  return []
}
export function finaleAction(game: GameState, dom: { selected: boolean; attackButton: boolean; targetMaster: boolean }): TutorialDialoguePage {
  if (game.phase === 'GameOver') return page('finale-result', game.winner === game.you ? '你赢下了这场小局' : '再试一次',
    game.winner === game.you ? '对方主宰血量归零。你已把资源、站位与进攻串起来。' : '观察实际结果，可以重新练习。')
  const own = game.players.find(player => player.playerIndex === game.you)!
  const enemy = game.players.find(player => player.playerIndex !== game.you)!
  if (game.pendingDefense?.stage === 'DefenseChoice' && game.pendingDefense.attackerPlayer !== game.you)
    return page('finale-defense', '决定怎样保护主宰', '根据当前血量与手牌，选择抵挡或不抵挡。', {
      selector: '[data-tutorial-anchor="defense-actions"]', label: '防守选择', unavailable: '请恢复防守面板。',
    })
  const card = finaleAttacker(game)
  if (card && game.activePlayer === game.you && game.phase === 'Main') {
    const selector = dom.targetMaster ? `${tutorialPlayerSelector(enemy.playerIndex)} [data-tutorial-anchor="master"].targetable`
      : dom.attackButton ? tutorialActionSelector('attack')
        : `${tutorialPlayerSelector(own.playerIndex)} [data-tutorial-anchor="field-slot"]${tutorialCardSelector(card.instanceId)}`
    return page('finale-act', '轮到你判断进攻', `对方主宰还有 ${enemy.master.hp} 点血量。使用当前可进攻的军团推进胜利；进攻后它会休整。`, {
      selector, label: dom.targetMaster ? '对方主宰' : dom.attackButton ? '进攻' : card.name, unavailable: '请展开当前战场。',
    })
  }
  if (game.activePlayer === game.you && game.phase === 'Main') return page('finale-end-turn', '完成本回合的行动',
    '当前没有可进攻主宰的军团。可以结束回合，等待下次重置、抽牌与士气补充。', {
      selector: tutorialActionSelector('end-turn'), label: '结束回合', unavailable: '请先处理当前询问。',
    })
  return page('finale-wait', '观察场面变化', '当前行动正在处理，等待场面中的结果。')
}
