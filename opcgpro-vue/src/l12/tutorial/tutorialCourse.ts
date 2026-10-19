import type { TutorialLessonId } from '../types'

export const tutorialScenes = ['deploy', 'move', 'combat'] as const satisfies readonly TutorialLessonId[]
export const tutorialChapters = ['初识十二军团', '卡牌效果与响应', '完成一场对局'] as const

export interface TutorialIntroductionStep {
  id: string; title: string; text: string; flow?: string
  cards: readonly { id: string; name: string; kind: string }[]
}
export const tutorialIntroduction: readonly TutorialIntroductionStep[] = [
  { id: 'goal', title: '欢迎，主宰者',
    text: '让对方主宰的血量降至0即可获胜，同时保护自己的主宰。我们先用杨戬认识战场。',
    cards: [{ id: 'S01-01M1', name: '杨戬', kind: '主宰' }] },
  { id: 'cards', title: '认识三种卡牌',
    text: '军团在战场进攻或防守；战术在合法时机使用或响应；圣物留在圣物区发挥效果。',
    cards: [{ id: 'S01-0116', name: '西施', kind: '军团' },
      { id: 'S01-0016', name: '绝对防御', kind: '反击战术' },
      { id: 'S01-0117', name: '山河社稷图', kind: '圣物' }] },
  { id: 'hand', title: '手牌来自牌库',
    text: '标准对局准备时抽6张起始手牌；之后通常在抽牌阶段从牌库顶部抽1张，先攻首回合不抽。卡牌效果也可能补充手牌。',
    flow: '牌库 → 抽牌或卡牌效果 → 手牌 → 打出卡牌',
    cards: [] },
  { id: 'morale', title: '士气用来支付费用',
    text: '士气阶段通常追加2张活跃士气，先攻首回合追加1张。支付1点费用会让1张活跃士气转为休整；重置阶段通常会让可重置的士气恢复活跃。',
    flow: '士气牌库 → 士气阶段追加 → 活跃士气 → 支付费用 → 休整',
    cards: [{ id: 'S01-01C1', name: '天廷士气', kind: '士气' }] },
  { id: 'practice', title: '看清数值，再开始实操',
    text: '费用决定支付多少，兵力用于进攻和抵挡，效果说明特殊能力。接下来依次打出军团、移动、进攻并防守。',
    cards: [{ id: 'S01-0116', name: '西施', kind: '军团' }] },
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
