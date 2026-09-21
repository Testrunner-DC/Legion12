export type RuleCenterSection = 'quick-start' | 'term' | 'construction' | 'tournament' | 'version'
export type RuleRulingScope = 'general' | 'card' | 'errata' | 'construction' | 'tournament'
export type RuleRulingStatus = 'published' | 'pending' | 'superseded'
import { QA_ENTRIES } from './officialFaq'
import { RULE_BLOCKS, type RuleBlock } from './officialRules'

export type RuleRulingSourceKind = 'rulebook' | 'official-faq' | 'user-ruling' | 'designer-ruling'

export interface RuleRuling {
  id: string
  scope: RuleRulingScope
  question: string
  answer: string
  category: string
  sourceKind: RuleRulingSourceKind
  sourceRef: string
  recordedAt: string
  effectiveAt?: string
  status: RuleRulingStatus
  cardIds: string[]
  productIds: string[]
  tags: string[]
  /** 原始资料条目的稳定编号；仅用于追溯与后台复核，不向玩家展示原文。 */
  sourceIds: string[]
  supersedes: string[]
}

export interface LegacyFaqSource {
  id: string
  type: string
  question: string
  answer: string
  note?: string
  reviewState: 'pending-review' | 'replaced' | 'conflict-flagged'
}

export interface RuleCenterEntry {
  id: string
  section: RuleCenterSection
  title: string
  body: string
  sourceRef: string
  tags: string[]
  status?: 'published' | 'pending'
}

export interface RuleCenterVersion {
  id: string
  title: string
  kind: 'rulebook' | 'faq' | 'ruling' | 'operations' | 'tournament'
  version?: string
  recordedAt?: string
  effectiveAt?: string
  sourceRef: string
  status: 'published' | 'pending'
  summary: string
}

/** The player page only accepts this document from the published content store. */
export interface RuleCenterDocument {
  coreBlocks: RuleBlock[]
  quickStart: RuleCenterEntry[]
  terms: RuleCenterEntry[]
  tournament: RuleCenterEntry[]
  versions: RuleCenterVersion[]
}

export const QUICK_START_ENTRIES: RuleCenterEntry[] = [
  { id: 'quick-overview', section: 'quick-start', title: '先了解胜利条件与战场', body: '从游戏概览、胜利条件与游戏布局开始；实际对局中的主宰、军团和战场位置均以核心规则为准。', sourceRef: '规则手册 Ver2.0 · 游戏概览与胜利条件 / 游戏布局', tags: ['胜利条件', '主宰', '军团'] },
  { id: 'quick-prepare', section: 'quick-start', title: '完成对局准备', body: '按核心规则完成牌库、起始资源、调度与先后手相关步骤。构筑限制须同时遵守当前公开的运营限制。', sourceRef: '规则手册 Ver2.0 · 对局准备', tags: ['对局准备', '构筑', '士气'] },
  { id: 'quick-turn', section: 'quick-start', title: '按回合流程行动', body: '回合中的具体阶段、可执行行动与限制以规则书的回合流程和主要阶段行动详解为准。', sourceRef: '规则手册 Ver2.0 · 回合流程 / 主要阶段行动详解', tags: ['回合', '行动'] },
  { id: 'quick-battle', section: 'quick-start', title: '进攻前确认目标与支援', body: '进攻、抵挡、支援、伤害与击杀的时序均须按核心规则及适用 FAQ 处理。', sourceRef: '规则手册 Ver2.0 · 进攻时序；FAQ Q15、Q19、Q56', tags: ['进攻', '抵挡', '支援', '击杀'] },
  { id: 'quick-stack', section: 'quick-start', title: '效果进入堆叠后再结算', body: '有目标或额外选择的效果先完成声明；费用、响应与分段结算按效果堆叠规则处理。', sourceRef: '规则手册 Ver2.0 · 效果类型、堆叠与响应；FAQ Q7、Q41、Q45、Q54', tags: ['效果堆叠', '响应', '费用'] },
]

export const TERM_ENTRIES: RuleCenterEntry[] = [
  { id: 'term-master', section: 'term', title: '主宰', body: '主宰位于主宰区，不视为位于战场；对方主宰血量降至 0 时获得胜利。', sourceRef: '规则手册 Ver2.0 · 游戏概览与胜利条件；FAQ Q3', tags: ['主宰', '主宰区'] },
  { id: 'term-legion', section: 'term', title: '军团与战场', body: '军团位于战场区。战场位置、放置与可进攻目标应按核心规则处理。', sourceRef: '规则手册 Ver2.0 · 游戏布局 / 进攻时序；FAQ Q3', tags: ['军团', '战场'] },
  { id: 'term-ready-rested', section: 'term', title: '活跃 / 休整', body: '活跃与休整是独立状态，会影响费用与行动合法性；休整的卡不能再次转为休整。', sourceRef: '规则手册 Ver2.0 · 核心概念 / 主要阶段行动详解', tags: ['活跃', '休整'] },
  { id: 'term-enter', section: 'term', title: '登场', body: '登场时效果属于触发式效果；未写“可”的登场时效果为必发。置入不等同于登场。', sourceRef: '规则手册 Ver2.0 · 触发式效果；FAQ Q1、Q30', tags: ['登场', '触发式效果', '置入'] },
  { id: 'term-stack', section: 'term', title: '效果堆叠与响应', body: '主动式、触发式和反应式效果在适用时进入堆叠；多个触发按规则进入堆叠并按后发先至结算。', sourceRef: '规则手册 Ver2.0 · 效果类型、堆叠与响应；FAQ Q45', tags: ['效果堆叠', '响应'] },
  { id: 'term-support', section: 'term', title: '抵挡与支援', body: '抵挡与支援的可用性、位置和兵力条件以进攻时序及 FAQ 为准。', sourceRef: '规则手册 Ver2.0 · 进攻时序；FAQ Q15', tags: ['抵挡', '支援', '进攻'] },
  { id: 'term-resource', section: 'term', title: '士气与神力', body: '士气和神力是卡牌或规则所引用的资源；费用、返还与翻转的具体处理以卡面和规则书为准。', sourceRef: '规则手册 Ver2.0 · 卡牌详解 / 主要阶段行动详解', tags: ['士气', '神力', '费用'] },
  { id: 'term-trial-disaster', section: 'term', title: '试炼与天灾', body: '试炼和天灾按各自规则及卡面处理；天灾触发属于特殊流程，不能被响应。', sourceRef: '规则手册 Ver2.0 · 阵营机制 / 触发天灾', tags: ['试炼', '天灾'] },
]

export const TOURNAMENT_ENTRIES: RuleCenterEntry[] = [
  { id: 'tournament-current-policy', section: 'tournament', title: '赛事采用的规则版本', body: '每场赛事必须记录其规则快照。规则中心只展示已正式发布的通用赛事规程；具体赛事以自身规则快照为准。', sourceRef: '赛事规则快照模型', tags: ['赛事', '规则版本'], status: 'published' },
  { id: 'tournament-format', section: 'tournament', title: '赛制与轮次', body: '正式赛事赛制、轮次、对局时限、平局与加赛规则尚待发布；在发布前不得沿用其他游戏的制度或术语。', sourceRef: '待发布赛事规程', tags: ['赛事', '赛制'], status: 'pending' },
  { id: 'tournament-officiating', section: 'tournament', title: '裁判与判罚', body: '正式赛事的裁判流程、迟到、弃权、重赛、处罚与申诉规则尚待发布。', sourceRef: '待发布赛事规程', tags: ['赛事', '裁判'], status: 'pending' },
]

export const VERSION_ENTRIES: RuleCenterVersion[] = [
  { id: 'rulebook-v2', title: '规则手册', kind: 'rulebook', version: 'Handbook Ver2.0', sourceRef: 'L12-规则书-文本(0215) 逐页转录', status: 'published', summary: '当前核心规则文字来源。原始资料未提供可公开展示的生效日期。' },
  { id: 'faq-import', title: '原始 FAQ、勘误与卡牌调整', kind: 'faq', sourceRef: 'L12规则及各类 Q&A 问题.xlsx · Q&A', status: 'pending', summary: '已导入 56 条原始记录，作为核对来源逐条改写与确认；原文不直接公开。' },
  { id: 'ruling-20260902', title: '用户明确裁定整理', kind: 'ruling', recordedAt: '2026-09-02', sourceRef: 'FAQ-RULINGS.md · 玩家明确裁定', status: 'published', summary: '以可追溯条目补充既有 FAQ，保留原条目和替代关系。' },
  { id: 'tournament-policy-pending', title: '赛事规则', kind: 'tournament', sourceRef: '待发布赛事规程', status: 'pending', summary: '赛事通用规程尚未形成已发布版本。' },
]

export const BUILT_IN_RULINGS: RuleRuling[] = [
  { id: 'RULING-20260922-FENIAN-REPEAT', scope: 'card', question: '〈芬尼亚传奇〉的完成触发如何重复发动？', answer: '待审核草稿：每次只消耗1符文、选择对方1张军团并生成一个独立响应堆叠；该堆叠完整结算后，若仍有符文和合法目标，再询问是否重复发动。可再次选择同一张仍合法的军团。', category: '单卡裁定', sourceKind: 'user-ruling', sourceRef: '用户确认裁定 · 2026-09-22', recordedAt: '2026-09-22', status: 'pending', cardIds: ['S02-06S5'], productIds: ['S02'], tags: ['芬尼亚传奇', '符文', '重复发动', '独立堆叠'], sourceIds: [], supersedes: [] },
  { id: 'RULING-20260922-SIWA-KABA', scope: 'card', question: '〈锡瓦的卡巴〉从手牌发动登场效果后被无效，或登场位置失效时如何处理？', answer: '待审核草稿：这是〈锡瓦的卡巴〉的单卡特例。该手牌登场效果被无效时，将它从手牌置入墓地；若声明的登场位置在逆结算后已被占用，也将它置入墓地，且不执行后续士气锁定。', category: '单卡裁定', sourceKind: 'user-ruling', sourceRef: '用户确认裁定 · 2026-09-22', recordedAt: '2026-09-22', status: 'pending', cardIds: ['S01-0213'], productIds: ['S01'], tags: ['锡瓦的卡巴', '无效', '登场位置', '墓地'], sourceIds: [], supersedes: [] },
  { id: 'RULING-20260917-LIVE-COST', scope: 'card', question: '奈芙蒂斯、不朽之礼等效果引用费用时，如何判断费用？', answer: '裁定：按该效果适用时的实时费用判断。除非卡牌文字明确要求印刷费用，否则不以卡牌上印刷的费用数值作为判断依据。', category: '单卡裁定', sourceKind: 'user-ruling', sourceRef: '用户确认裁定 · 2026-09-17', recordedAt: '2026-09-17', status: 'published', cardIds: [], productIds: [], tags: ['奈芙蒂斯', '不朽之礼', '实时费用', '费用判断'], sourceIds: ['LEGACY-FAQ-47'], supersedes: ['LEGACY-FAQ-47'] },
  { id: 'RULING-20260902-HOREMHEB', scope: 'card', question: '霍列姆赫布的致命替代如何处理？', answer: '裁定：作为替代结果离场的〈陵墓守卫〉，承受被保护军团原本的致命结果，并按其所有者进入对应区域。〈霍列姆赫布〉不因该替代先离场或重新登场。', category: '单卡裁定', sourceKind: 'user-ruling', sourceRef: '用户确认裁定 · 2026-09-02', recordedAt: '2026-09-02', status: 'published', cardIds: [], productIds: [], tags: ['霍列姆赫布', '陵墓守卫', '致命替代', '阵亡'], sourceIds: [], supersedes: [] },
  { id: 'RULING-20260902-HELEN', scope: 'card', question: '海伦弃置手牌中的军团卡是否视为阵亡或离场？', answer: '裁定：该军团卡从手牌进入其所有者的墓地，属于弃置；这次区域变更不视为战场上的阵亡或离场，因此不触发【阵亡时】或【离场时】。', category: '单卡裁定', sourceKind: 'user-ruling', sourceRef: '用户确认裁定 · 2026-09-02', recordedAt: '2026-09-02', status: 'published', cardIds: [], productIds: [], tags: ['海伦', '弃牌', '阵亡', '离场'], sourceIds: [], supersedes: [] },
  { id: 'RULING-20260902-PTOLEMY', scope: 'card', question: '托勒密十三世再次发动主动战术时，原费用与原战术卡如何处理？', answer: '裁定：只再次发动上一张主动战术的效果；不再次支付该战术原本的打出费用或冒号前费用，原战术卡仍留在原本所在区域，不作为这次效果的一部分再次处理。重复效果所需的公开信息仍须在其进入效果堆叠前声明。', category: '单卡裁定', sourceKind: 'user-ruling', sourceRef: '用户确认裁定 · 2026-09-02', recordedAt: '2026-09-02', status: 'published', cardIds: [], productIds: [], tags: ['托勒密十三世', '主动战术', '费用', '效果堆叠'], sourceIds: [], supersedes: [] },
  { id: 'RULING-20260902-FAITH-ZEALOT', scope: 'card', question: '信仰狂热者选择主宰效果时如何进入效果堆叠？', answer: '裁定：先完成〈信仰狂热者〉自身效果的结算。其后选择的主宰效果作为新的独立效果进入效果堆叠，并依其自身时序声明目标与模式。', category: '单卡裁定', sourceKind: 'user-ruling', sourceRef: '用户确认裁定 · 2026-09-02', recordedAt: '2026-09-02', status: 'published', cardIds: [], productIds: [], tags: ['信仰狂热者', '主宰', '效果堆叠', '费用'], sourceIds: [], supersedes: [] },
  { id: 'RULING-20260902-LI-JING', scope: 'card', question: '李靖的展示与后续引用是否拆分为多个可响应效果？', answer: '裁定：展示与引用“其”的后续处理构成同一次隐藏信息处理，不拆分为多个可响应效果。该展示被无效时，不查看牌库顶牌，也不执行依赖该信息的后续处理。', category: '单卡裁定', sourceKind: 'user-ruling', sourceRef: '用户确认裁定 · 2026-09-02', recordedAt: '2026-09-02', status: 'published', cardIds: [], productIds: [], tags: ['李靖', '展示', '隐藏信息', '响应'], sourceIds: [], supersedes: [] },
  { id: 'RULING-20260902-THUNDER', scope: 'card', question: '雷霆天怒出现并列最低点时如何处理？', answer: '裁定：所有并列最低点的玩家均为输家。先由当前回合玩家处理，再由另一名玩家处理；每名玩家只能选择自己当前控制的军团，使其返回其所有者手牌。', category: '单卡裁定', sourceKind: 'user-ruling', sourceRef: '用户确认裁定 · 2026-09-02', recordedAt: '2026-09-02', status: 'published', cardIds: [], productIds: [], tags: ['雷霆天怒', '天灾', '控制者', '所有者'], sourceIds: [], supersedes: [] },
  { id: 'RULING-20260828-TROOP-LAYER', scope: 'general', question: '临时兵力修正受到伤害后如何记录？', answer: '裁定：临时正兵力修正可被伤害消耗。该修正结束时，只移除尚未被消耗的部分；不得因重算原本兵力而消除已经承受的伤害。', category: '进攻与战斗', sourceKind: 'designer-ruling', sourceRef: '设计者确认裁定 · 2026-08-28', recordedAt: '2026-08-28', status: 'published', cardIds: [], productIds: [], tags: ['兵力', '伤害', '持续效果'], sourceIds: ['LEGACY-FAQ-16'], supersedes: ['LEGACY-FAQ-16'] },
]

/**
 * Maintainer seed only. It is intentionally never used as a player-page fallback:
 * opening the admin editor is the first step, publication is the second.
 */
export function createRuleCenterDraft(): RuleCenterDocument {
  return JSON.parse(JSON.stringify({
    coreBlocks: RULE_BLOCKS,
    quickStart: QUICK_START_ENTRIES,
    terms: TERM_ENTRIES,
    tournament: TOURNAMENT_ENTRIES,
    versions: VERSION_ENTRIES,
  })) as RuleCenterDocument
}

export function createRulingsDraft(): RuleRuling[] {
  return JSON.parse(JSON.stringify(BUILT_IN_RULINGS)) as RuleRuling[]
}

/** Add newly shipped pending-review seeds without replacing an administrator's existing draft. */
export function withPendingRulingSeeds(existing: RuleRuling[]): RuleRuling[] {
  const ids = new Set(existing.map(item => item.id))
  return [...createRulingsDraft().filter(item => item.status === 'pending' && !ids.has(item.id)), ...existing]
}

/** 原始表的内容只供后台逐条复核。它从不构成公开 FAQ 的答案。 */
export const LEGACY_FAQ_SOURCES: LegacyFaqSource[] = QA_ENTRIES.map(entry => ({
  id: `LEGACY-FAQ-${entry.id}`,
  type: entry.type,
  question: entry.question,
  answer: entry.answer,
  note: entry.note || entry.status,
  reviewState: entry.id === 16 || entry.id === 47 ? 'replaced' : 'pending-review',
}))

const scopes = new Set<RuleRulingScope>(['general', 'card', 'errata', 'construction', 'tournament'])
const statuses = new Set<RuleRulingStatus>(['published', 'pending', 'superseded'])
const sourceKinds = new Set<RuleRulingSourceKind>(['rulebook', 'official-faq', 'user-ruling', 'designer-ruling'])

function text(value: unknown, maximum: number) {
  return typeof value === 'string' && value.trim().length > 0 && value.trim().length <= maximum ? value.trim() : undefined
}

function strings(value: unknown, maximumItems: number, maximumLength: number) {
  if (!Array.isArray(value) || value.length > maximumItems) return undefined
  const result = value.map(item => text(item, maximumLength))
  return result.every(Boolean) ? result as string[] : undefined
}

function ruling(value: unknown): RuleRuling | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined
  const row = value as Record<string, unknown>
  const id = text(row.id, 100); const question = text(row.question, 500); const answer = text(row.answer, 8_000)
  const category = text(row.category, 100); const sourceRef = text(row.sourceRef, 300); const recordedAt = text(row.recordedAt, 40)
  const scope = text(row.scope, 30) as RuleRulingScope | undefined
  const status = text(row.status, 30) as RuleRulingStatus | undefined
  const sourceKind = text(row.sourceKind, 30) as RuleRulingSourceKind | undefined
  const cardIds = strings(row.cardIds, 64, 80); const productIds = strings(row.productIds, 64, 80)
  const tags = strings(row.tags, 32, 80); const sourceIds = strings(row.sourceIds, 64, 100); const supersedes = strings(row.supersedes, 32, 100)
  if (!id || !question || !answer || !category || !sourceRef || !recordedAt || !scope || !status || !sourceKind
    || !scopes.has(scope) || !statuses.has(status) || !sourceKinds.has(sourceKind)
    || !cardIds || !productIds || !tags || !sourceIds || !supersedes) return undefined
  const effectiveAt = row.effectiveAt === undefined || row.effectiveAt === null ? undefined : text(row.effectiveAt, 40)
  return { id, scope, question, answer, category, sourceKind, sourceRef, recordedAt, effectiveAt, status, cardIds, productIds, tags, sourceIds, supersedes }
}

export function parseRulingDocument(value: string): RuleRuling[] {
  if (!value.trim()) return []
  try {
    const parsed = JSON.parse(value) as unknown
    const entries = Array.isArray(parsed) ? parsed : (parsed && typeof parsed === 'object' ? (parsed as { entries?: unknown }).entries : undefined)
    if (!Array.isArray(entries)) return []
    const parsedEntries = entries.map(ruling).filter((item): item is RuleRuling => Boolean(item))
    return parsedEntries
  } catch { return [] }
}

export function parsePublishedRulings(value: string): RuleRuling[] {
  return parseRulingDocument(value).filter(item => item.status === 'published')
}

export function mergedRulings(dynamicRulings: RuleRuling[]) {
  const byId = new Map(dynamicRulings.map(item => [item.id, item]))
  const superseded = new Set([...byId.values()].flatMap(item => item.supersedes))
  return [...byId.values()].filter(item => !superseded.has(item.id))
    .sort((left, right) => right.recordedAt.localeCompare(left.recordedAt) || left.id.localeCompare(right.id))
}

function ruleBlock(value: unknown): RuleBlock | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined
  const row = value as Record<string, unknown>
  const page = text(row.page, 20); const topic = row.topic === undefined ? undefined : text(row.topic, 100)
  const chapter = row.chapter === undefined ? undefined : text(row.chapter, 100); const blockText = text(row.text, 12_000)
  return page && blockText ? { page, ...(topic ? { topic } : {}), ...(chapter ? { chapter } : {}), text: blockText } : undefined
}

function centerEntry(value: unknown, section: RuleCenterSection): RuleCenterEntry | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined
  const row = value as Record<string, unknown>
  const id = text(row.id, 100); const title = text(row.title, 300); const body = text(row.body, 12_000); const sourceRef = text(row.sourceRef, 300)
  const tags = strings(row.tags, 32, 80); const status = row.status === undefined ? undefined : text(row.status, 30)
  if (status && status !== 'published') return undefined
  return id && title && body && sourceRef && tags ? { id, section, title, body, sourceRef, tags } : undefined
}

function centerVersion(value: unknown): RuleCenterVersion | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined
  const row = value as Record<string, unknown>
  const id = text(row.id, 100); const title = text(row.title, 300); const kind = text(row.kind, 80) as RuleCenterVersion['kind'] | undefined
  const sourceRef = text(row.sourceRef, 300); const status = text(row.status, 40) as RuleCenterVersion['status'] | undefined; const summary = text(row.summary, 12_000)
  if (!id || !title || !kind || !sourceRef || !status || !summary || status !== 'published') return undefined
  const version = row.version === undefined ? undefined : text(row.version, 100)
  const recordedAt = row.recordedAt === undefined ? undefined : text(row.recordedAt, 40)
  const effectiveAt = row.effectiveAt === undefined ? undefined : text(row.effectiveAt, 40)
  return { id, title, kind, sourceRef, status, summary, ...(version ? { version } : {}), ...(recordedAt ? { recordedAt } : {}), ...(effectiveAt ? { effectiveAt } : {}) }
}

/** Invalid or absent public content deliberately resolves to an empty document. */
export function parsePublishedRuleCenter(value: string): RuleCenterDocument {
  try {
    const parsed = JSON.parse(value) as Record<string, unknown>
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('invalid')
    const list = <T>(key: string, parse: (value: unknown) => T | undefined) => Array.isArray(parsed[key])
      ? parsed[key].map(parse).filter((item): item is T => Boolean(item)) : []
    return {
      coreBlocks: list('coreBlocks', ruleBlock),
      quickStart: list('quickStart', value => centerEntry(value, 'quick-start')),
      terms: list('terms', value => centerEntry(value, 'term')),
      tournament: list('tournament', value => centerEntry(value, 'tournament')),
      versions: list('versions', centerVersion),
    }
  } catch { return { coreBlocks: [], quickStart: [], terms: [], tournament: [], versions: [] } }
}
