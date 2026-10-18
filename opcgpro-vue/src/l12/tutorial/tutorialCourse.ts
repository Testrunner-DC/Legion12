import type { TutorialLessonId } from '../types'

export const tutorialScenes = ['deploy', 'move', 'combat'] as const satisfies readonly TutorialLessonId[]
export const tutorialChapters = ['初识十二军团', '卡牌效果与响应', '完成一场对局'] as const

export interface TutorialIntroductionStep {
  id: string; title: string; text: string; question: string; answer: string; flow?: string
  cards: readonly { id: string; name: string; kind: string }[]
}
export const tutorialIntroduction: readonly TutorialIntroductionStep[] = [
  { id: 'goal', title: '欢迎，主宰者',
    text: '在十二军团中，你将率领军团与对手交战。让对方主宰的血量降至0，就能获得胜利；同时，也要保护好自己的主宰。',
    question: '我该怎样保护主宰？', answer: '军团不仅能进攻，也是主宰的屏障。稍后我们会一起练习抵挡，了解何时让军团站出来、何时保留它们。',
    cards: [] },
  { id: 'master', title: '这是你的主宰',
    text: '主宰是你在对局中的化身，也决定了构筑牌库时可以使用的阵营。我们先以天廷的杨戬认识战场；主宰的血量会显示在它旁边。',
    question: '主宰和军团一样吗？', answer: '不一样。主宰有需要保护的血量，军团则以兵力参与进攻和防守。部分卡牌有特殊效果，不能把所有主宰都当作普通军团。',
    cards: [{ id: 'S01-01M1', name: '杨戬', kind: '主宰' }] },
  { id: 'legion', title: '军团：进攻与防守的主力',
    text: '军团从手牌打出后进入战场，把你手里的选择变成可以进攻、防守或发挥效果的场上力量。代价是支付费用，并少一张手牌。看清费用、兵力和效果，再决定是否打出。稍后，我们会亲手打出这张西施。',
    question: '打出和登场有什么区别？', answer: '“打出”是你进行的行动，“登场”是军团进入战场的结果。由效果让军团登场，不一定等于你从手牌打出这张卡。',
    cards: [{ id: 'S01-0116', name: '西施', kind: '军团' }] },
  { id: 'tactic', title: '战术：在合适的时机行动',
    text: '战术分为主动战术和反击战术。主动战术可以在自己的回合使用；反击战术通常先支付费用盖伏，再在符合卡牌描述的时机响应。这里先认识它们，下一章再实际练习。',
    question: '发动一张卡，就是发动它的每段效果吗？', answer: '不是。卡的发动与效果的发动要分开理解；一张卡可能有多段效果，另一个效果也可能再次发动已有的效果。我们会在真实响应中逐步比较。',
    cards: [{ id: 'S01-0008', name: '兵临城下', kind: '主动战术' }, { id: 'S01-0016', name: '绝对防御', kind: '反击战术' }] },
  { id: 'relic', title: '圣物：留在圣物区的力量',
    text: '圣物打出后放在圣物区，依照卡牌效果为你提供帮助。通常圣物区只能存在1张圣物，打出新圣物时，原有圣物会置入墓地。',
    question: '圣物也占军团的阵地吗？', answer: '圣物使用圣物区，不占军团所在的战场阵地。不同卡牌的效果和例外，以它们的实际说明为准。',
    cards: [{ id: 'S01-0117', name: '山河社稷图', kind: '圣物' }] },
  { id: 'hand', title: '手牌从哪里来？',
    text: '手牌来自你的牌库。标准对局准备时抽取6张起始手牌；之后通常在自己的抽牌阶段，从牌库顶部抽1张，先攻玩家第一回合不抽。卡牌效果也可能让你抽牌或把卡加入手牌。',
    flow: '牌库 → 抽牌或卡牌效果 → 手牌 → 打出卡牌',
    question: '为什么不能把所有手牌一口气打完？',
    answer: '手牌提供行动选择，但每张卡仍有费用、时机和位置要求。打出卡牌能改变场面，也会减少手牌；留下部分手牌，则可能用于后续行动或抵挡。不能只看手牌数量，还要看当前可用士气。',
    cards: [] },
  { id: 'morale', title: '士气：支付费用的资源',
    text: '支付费用通常需要使用士气。士气牌库仍有牌时，自己的士气阶段通常追加2张活跃士气；先攻玩家第一回合只追加1张。将1张活跃士气转为休整，就支付了1点费用。士气让手牌中的计划真正变成行动。',
    flow: '士气牌库 → 士气阶段追加 → 活跃士气 → 支付费用 → 休整',
    question: '休整的士气就失去了吗？', answer: '没有消失，只是暂时不能再次用于同样的支付。自己的重置阶段通常会让可以重置的士气重新活跃；卡牌效果也可能改变资源和重置。追加新士气与恢复已有士气，是两件不同的事。',
    cards: [{ id: 'S01-01C1', name: '天廷士气', kind: '士气' }] },
  { id: 'reading', title: '先看费用，再看兵力和效果',
    text: '费用决定打出卡牌需要支付多少，兵力参与军团的进攻和防守，效果则说明这张卡的特殊能力。它们不是同一个数值。我们马上在真实手牌上逐项查看。',
    question: '只比较兵力就够了吗？', answer: '还要看位置、活跃或休整、进攻距离、费用和卡牌效果。本章会先带你认识基本行动，不要求现在记住所有例外。',
    cards: [{ id: 'S01-0116', name: '西施', kind: '军团' }] },
  { id: 'practice', title: '我们一起走进战场',
    text: '先打出一张军团，再练习移动、进攻与防守。我会指出每一步的卡牌、费用和按钮，完成操作后再一起看结果。准备好了吗？',
    question: '操作错了怎么办？', answer: '先看引导和当前界面的说明。你也可以重新练习当前场面；已经支付的费用仍按原规则处理，不增加教学专用的撤回。',
    cards: [] },
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
