import type { TutorialLessonId } from '../types'

export const tutorialScenes = ['deploy', 'move', 'combat'] as const satisfies readonly TutorialLessonId[]
export const tutorialChapters = ['初识十二军团', '卡牌效果与响应', '完成一场对局'] as const

export interface TutorialIntroductionStep {
  id: string; title: string; text: string; flow?: string
  cards: readonly { id: string; name: string; kind: string }[]
}
export const tutorialIntroduction: readonly TutorialIntroductionStep[] = [
  { id: 'goal', title: '欢迎，主宰者',
    text: '将对方主宰的血量降至0，赢得对局；同时保护自己的主宰。',
    cards: [{ id: 'ST01-M1', name: '嫦娥', kind: '我方主宰' }, { id: 'ST04-M1', name: '迦具土', kind: '对方主宰' }] },
  { id: 'cards', title: '认识三种卡牌',
    text: '军团参与进攻与抵挡；战术在规定时机使用；圣物留在圣物区发挥效果。点击卡牌查看详情。',
    cards: [{ id: 'S01-0104', name: '韩信', kind: '军团' },
      { id: 'ST01-10', name: '暗度陈仓', kind: '反击战术' },
      { id: 'S01-0117', name: '山河社稷图', kind: '圣物' }] },
  { id: 'hand', title: '手牌来自牌库',
    text: '准备时从牌库抽6张起始手牌。之后通常在己方抽牌阶段从牌库顶部抽1张，先攻首回合不抽；卡牌效果也能补充手牌。',
    flow: '牌库 → 抽牌或卡牌效果 → 手牌 → 打出卡牌',
    cards: [] },
  { id: 'morale', title: '士气用来支付费用',
    text: '士气阶段从士气牌库追加2张活跃士气，先攻首回合追加1张。士气支付后休整，通常在重置阶段恢复活跃。',
    flow: '士气牌库 → 士气阶段追加 → 活跃士气 → 支付费用 → 休整',
    cards: [{ id: 'S01-01C1', name: '天廷士气', kind: '士气' }] },
  { id: 'practice', title: '看清数值，再开始实操',
    text: '费用决定支付多少，兵力用于进攻与抵挡，效果说明特殊能力。接下来进入战场，练习打出军团、移动和攻防。',
    cards: [{ id: 'ST01-05', name: '食铁兽', kind: '军团' }] },
]

export interface TutorialHandoffBinding {
  accountId: string; token: string; sessionId: string; connectionGeneration: number
  roomCode: string; matchId: string
}
export interface TutorialHandoffSnapshot {
  accountId: string; token: string; connectedAccountId: string; sessionId: string
  connectionGeneration: number; online: boolean; available: boolean
  roomCode: string | null; matchId: string | null; leaving: boolean; pending: boolean
}

// The next practice may start only after the real roomLeft acknowledgement.
// A route change or an account/connection replacement is not such an acknowledgement.
export function tutorialHandoffDecision(expected: TutorialHandoffBinding, now: TutorialHandoffSnapshot) {
  if (!now.online || !now.available || now.accountId !== expected.accountId
    || now.connectedAccountId !== expected.accountId || now.token !== expected.token
    || now.sessionId !== expected.sessionId || now.connectionGeneration !== expected.connectionGeneration)
    return 'cancelled' as const
  if ((now.roomCode && now.roomCode !== expected.roomCode) || (now.matchId && now.matchId !== expected.matchId))
    return 'cancelled' as const
  return !now.roomCode && !now.matchId && !now.leaving && !now.pending ? 'ready' as const : 'waiting' as const
}
