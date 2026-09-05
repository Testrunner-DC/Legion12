export type ArticleTextBlockType = 'paragraph' | 'h2' | 'h3' | 'bulletList' | 'orderedList' | 'quote'
export type ArticleBlockType = ArticleTextBlockType | 'image' | 'divider'
export type ArticleMarkType = 'bold' | 'italic' | 'underline' | 'strikethrough' | 'link'
export type ArticleTextAlign = 'left' | 'center' | 'right' | 'justify'

export interface ArticleMark {
  type: ArticleMarkType
  from: number
  to: number
  href?: string
}

export interface ArticleTextBlock {
  id: string
  type: ArticleTextBlockType
  text: string
  marks: ArticleMark[]
  align: ArticleTextAlign
}

export interface ArticleImageBlock {
  id: string
  type: 'image'
  mediaAssetId: string
  alt: string
  caption: string
}

export interface ArticleDividerBlock { id: string; type: 'divider' }
export type ArticleBlock = ArticleTextBlock | ArticleImageBlock | ArticleDividerBlock
export interface ArticleBodyDocument { format: 'l12-blocks'; version: 1; blocks: ArticleBlock[] }
export interface ArticleInlineRun { text: string; bold: boolean; italic: boolean; underline: boolean; strikethrough: boolean; href: string }
export interface ArticleListItem { text: string; from: number; to: number }

const textTypes = new Set<ArticleTextBlockType>(['paragraph', 'h2', 'h3', 'bulletList', 'orderedList', 'quote'])
const markTypes = new Set<ArticleMarkType>(['bold', 'italic', 'underline', 'strikethrough', 'link'])
const alignTypes = new Set<ArticleTextAlign>(['left', 'center', 'right', 'justify'])
export const ARTICLE_BLOCK_ID_MAX_LENGTH = 80

export function articleBlockId() {
  return globalThis.crypto?.randomUUID?.() ?? `block-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
}

function boundedArticleBlockId(value: unknown) {
  return typeof value === 'string' ? value.trim().slice(0, ARTICLE_BLOCK_ID_MAX_LENGTH) : ''
}

function unusedArticleBlockId(used: Set<string>, createId: () => string) {
  const base = boundedArticleBlockId(createId()) || 'block'
  if (!used.has(base)) return base
  for (let sequence = 2; ; sequence += 1) {
    const suffix = `-${sequence}`
    const candidate = `${base.slice(0, ARTICLE_BLOCK_ID_MAX_LENGTH - suffix.length)}${suffix}`
    if (!used.has(candidate)) return candidate
  }
}

export function normalizeArticleBlockIds(blocks: readonly ArticleBlock[], createId: () => string = articleBlockId): ArticleBlock[] {
  const used = new Set<string>()
  return blocks.map(block => {
    const candidate = boundedArticleBlockId(block.id)
    const id = candidate && !used.has(candidate) ? candidate : unusedArticleBlockId(used, createId)
    used.add(id)
    return id === block.id ? block : { ...block, id } as ArticleBlock
  })
}

export function newArticleTextBlock(type: ArticleTextBlockType = 'paragraph', text = ''): ArticleTextBlock {
  return { id: articleBlockId(), type, text, marks: [], align: 'left' }
}

export function newArticleImageBlock(): ArticleImageBlock {
  return { id: articleBlockId(), type: 'image', mediaAssetId: '', alt: '', caption: '' }
}

export function newArticleDividerBlock(): ArticleDividerBlock { return { id: articleBlockId(), type: 'divider' } }

function cleanMarks(value: unknown, textLength: number): ArticleMark[] {
  if (!Array.isArray(value)) return []
  return value.flatMap(mark => {
    if (!mark || typeof mark !== 'object') return []
    const item = mark as Record<string, unknown>
    if (!markTypes.has(item.type as ArticleMarkType)) return []
    const from = Number(item.from); const to = Number(item.to)
    if (!Number.isInteger(from) || !Number.isInteger(to) || from < 0 || to <= from || to > textLength) return []
    const type = item.type as ArticleMarkType
    const href = type === 'link' ? safeArticleHref(String(item.href || '')) : ''
    if (type === 'link' && !href) return []
    return [{ type, from, to, ...(href ? { href } : {}) }]
  }).slice(0, 200)
}

function cleanStructuredBlock(value: unknown): ArticleBlock | null {
  if (!value || typeof value !== 'object') return null
  const block = value as Record<string, unknown>
  const id = typeof block.id === 'string' ? block.id : ''
  if (block.type === 'image') {
    return {
      id, type: 'image', mediaAssetId: String(block.mediaAssetId || '').slice(0, 100),
      alt: String(block.alt || '').slice(0, 180), caption: String(block.caption || '').slice(0, 500),
    }
  }
  if (block.type === 'divider') return { id, type: 'divider' }
  if (!textTypes.has(block.type as ArticleTextBlockType)) return null
  const text = String(block.text || '').slice(0, 20_000)
  const align = alignTypes.has(block.align as ArticleTextAlign) ? block.align as ArticleTextAlign : 'left'
  return { id, type: block.type as ArticleTextBlockType, text, marks: cleanMarks(block.marks, text.length), align }
}

export function parseArticleBody(value?: string | null): ArticleBodyDocument {
  const source = value || ''
  if (source.trimStart().startsWith('{')) {
    try {
      const parsed = JSON.parse(source) as Record<string, unknown>
      if (parsed.format === 'l12-blocks' && parsed.version === 1 && Array.isArray(parsed.blocks)) {
        const blocks = normalizeArticleBlockIds(parsed.blocks.slice(0, 200).map(cleanStructuredBlock)
          .filter((block): block is ArticleBlock => Boolean(block)))
        return { format: 'l12-blocks', version: 1, blocks: blocks.length ? blocks : [newArticleTextBlock()] }
      }
    } catch { /* Legacy plain text may legitimately begin with a brace. */ }
  }
  const paragraphs = source ? source.split(/\r?\n\s*\r?\n/).map(text => text.trim()).filter(Boolean) : []
  return { format: 'l12-blocks', version: 1, blocks: paragraphs.length ? paragraphs.map(text => newArticleTextBlock('paragraph', text)) : [newArticleTextBlock()] }
}

export function serializeArticleBody(document: ArticleBodyDocument) {
  return JSON.stringify({ format: 'l12-blocks', version: 1, blocks: normalizeArticleBlockIds(document.blocks) })
}

export function articleBodyText(value?: string | null, limit = 260) {
  const text = parseArticleBody(value).blocks.map(block => block.type === 'image' ? block.caption : block.type === 'divider' ? '' : block.text)
    .filter(Boolean).join(' ').replace(/\s+/g, ' ').trim()
  return text.length > limit ? `${text.slice(0, limit).trimEnd()}…` : text
}

export function safeArticleHref(value: string) {
  const href = value.trim()
  if (href.startsWith('/') && !href.startsWith('//')) return href
  try {
    const url = new URL(href)
    return url.protocol === 'http:' || url.protocol === 'https:' ? href : ''
  } catch { return '' }
}

export function articleInlineRuns(text: string, marks: ArticleMark[], from = 0, to = text.length): ArticleInlineRun[] {
  const start = Math.max(0, from); const end = Math.min(text.length, to)
  if (end <= start) return []
  const activeMarks = marks.filter(mark => mark.from < end && mark.to > start)
  const boundaries = [...new Set([start, end, ...activeMarks.flatMap(mark => [Math.max(start, mark.from), Math.min(end, mark.to)])])]
    .sort((left, right) => left - right)
  return boundaries.slice(0, -1).map((boundary, index) => {
    const next = boundaries[index + 1]
    const covering = activeMarks.filter(mark => mark.from <= boundary && mark.to >= next)
    return {
      text: text.slice(boundary, next), bold: covering.some(mark => mark.type === 'bold'),
      italic: covering.some(mark => mark.type === 'italic'),
      underline: covering.some(mark => mark.type === 'underline'),
      strikethrough: covering.some(mark => mark.type === 'strikethrough'),
      href: covering.find(mark => mark.type === 'link')?.href || '',
    }
  }).filter(run => run.text)
}

export function articleListItems(text: string): ArticleListItem[] {
  const items: ArticleListItem[] = []
  const expression = /[^\r\n]+/g
  for (const match of text.matchAll(expression)) {
    const from = match.index ?? 0
    const itemText = match[0].trim()
    if (!itemText) continue
    const leading = match[0].indexOf(itemText)
    items.push({ text: itemText, from: from + leading, to: from + leading + itemText.length })
  }
  return items
}
