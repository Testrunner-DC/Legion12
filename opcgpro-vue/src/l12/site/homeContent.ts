export type HomeContentField = {
  id: string
  key: string
  label: string
  defaultValue: string
  multiline?: boolean
  rows?: number
}

export type NewsEntry = {
  id: string
  title: string
  category: '官方公告' | '规则勘误' | '赛季更新' | '赛事信息'
  summary: string
  body: string
  coverUrl: string
  publishedAt: string
  pinned: boolean
  published: boolean
}

export const newsContentKey = 'news.entries'

export function createNewsEntry(): NewsEntry {
  return {
    id: globalThis.crypto?.randomUUID?.() ?? `news-${Date.now().toString(36)}`,
    title: '', category: '官方公告', summary: '', body: '', coverUrl: '',
    publishedAt: new Date().toISOString(), pinned: false, published: true,
  }
}

export function parseNewsEntries(value?: string): NewsEntry[] {
  if (!value?.trim()) return []
  try {
    const rows = JSON.parse(value)
    if (!Array.isArray(rows)) return []
    return rows.filter(row => row && typeof row.title === 'string').map(row => ({
      id: String(row.id || `news-${Math.random().toString(36).slice(2)}`),
      title: String(row.title),
      category: ['官方公告', '规则勘误', '赛季更新', '赛事信息'].includes(row.category) ? row.category : '官方公告',
      summary: String(row.summary || ''), body: String(row.body || ''), coverUrl: String(row.coverUrl || ''),
      publishedAt: String(row.publishedAt || new Date().toISOString()),
      pinned: row.pinned === true, published: row.published !== false,
    }))
  } catch { return [] }
}

export function serializeNewsEntries(entries: NewsEntry[]) {
  return JSON.stringify(entries.map(entry => ({ ...entry, title: entry.title.trim() })), null, 2)
}

export const homeContentFields: HomeContentField[] = [
  { id: 'heroEyebrow', key: 'home.heroEyebrow', label: '首页眉题', defaultValue: 'LEGION 12 · OFFICIAL WEB PLATFORM' },
  { id: 'headline', key: 'home.headline', label: '首页标题', defaultValue: '十二军团' },
  { id: 'introduction', key: 'home.introduction', label: '首页介绍', defaultValue: '集结、构筑、开战' },
  { id: 'primaryCta', key: 'home.primaryCta', label: '主按钮文字', defaultValue: '进入对战大厅' },
  { id: 'secondaryCta', key: 'home.secondaryCta', label: '次按钮文字', defaultValue: '浏览卡牌图鉴' },
  { id: 'featureLabels', key: 'home.featureLabels', label: '首页特征（每行一项）', defaultValue: '官方网站\n规则与资料库\n在线对战器', multiline: true, rows: 4 },
  { id: 'playTitle', key: 'home.playTitle', label: '对战模块标题', defaultValue: '在线对战' },
  { id: 'playText', key: 'home.playText', label: '对战模块说明', defaultValue: '公开匹配、好友房与单人测试沙盒。', multiline: true, rows: 3 },
  { id: 'cardsTitle', key: 'home.cardsTitle', label: '卡牌模块标题', defaultValue: '卡牌资料库' },
  { id: 'cardsText', key: 'home.cardsText', label: '卡牌模块说明', defaultValue: '按赛季、阵营、类型、费用与天灾等级检索。', multiline: true, rows: 3 },
  { id: 'decksTitle', key: 'home.decksTitle', label: '牌库模块标题', defaultValue: '牌库与分享' },
  { id: 'decksText', key: 'home.decksText', label: '牌库模块说明', defaultValue: '构筑、校验、牌库广场、牌库码与牌库图。', multiline: true, rows: 3 },
  { id: 'recordsTitle', key: 'home.recordsTitle', label: '回放模块标题', defaultValue: '对局与回放' },
  { id: 'recordsText', key: 'home.recordsText', label: '回放模块说明', defaultValue: '完整操作记录、JSON 导入导出与只读棋盘回放。', multiline: true, rows: 3 },
  { id: 'newsTitle', key: 'home.newsTitle', label: '资讯栏标题', defaultValue: '官方资讯' },
  { id: 'latestNews', key: 'home.latestNews', label: '最新资讯', defaultValue: '', multiline: true, rows: 8 },
  { id: 'newsEmptyTitle', key: 'home.newsEmptyTitle', label: '无资讯标题', defaultValue: '暂无正式资讯' },
  { id: 'newsEmptyText', key: 'home.newsEmptyText', label: '无资讯说明', defaultValue: '管理员可在后台发布公告、赛季更新、勘误与赛事信息。', multiline: true, rows: 3 },
  { id: 'rulesTitle', key: 'home.rulesTitle', label: '规则栏标题', defaultValue: '规则资料' },
  { id: 'cardLinkLabel', key: 'home.cardLinkLabel', label: '卡牌图鉴链接文字', defaultValue: '卡牌图鉴与原文效果' },
  { id: 'rulesLinkLabel', key: 'home.rulesLinkLabel', label: '规则 FAQ 链接文字', defaultValue: '规则书与 FAQ 整理中' },
  { id: 'replayLinkLabel', key: 'home.replayLinkLabel', label: '复盘链接文字', defaultValue: '对局复盘工具' },
  { id: 'developmentTitle', key: 'home.developmentTitle', label: '开发状态标题', defaultValue: '开发状态' },
  { id: 'battleStatus', key: 'home.battleStatus', label: '对战框架状态', defaultValue: '可测试' },
  { id: 's1Status', key: 'home.s1Status', label: 'S01 卡效状态', defaultValue: '回归中' },
  { id: 's2Status', key: 'home.s2Status', label: 'S02 卡效状态', defaultValue: '接入中' },
  { id: 'mobileStatus', key: 'home.mobileStatus', label: '移动端状态', defaultValue: '适配中' },
]

export function createHomeContent() {
  return Object.fromEntries(homeContentFields.map(field => [field.id, field.defaultValue])) as Record<string, string>
}
