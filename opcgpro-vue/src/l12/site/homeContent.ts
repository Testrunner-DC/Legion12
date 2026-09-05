export const homeCompositionKey = 'home.composition'
export const siteLegalKey = 'site.footer'

export type HomeHeroSlide = {
  id: string
  eyebrow: string
  title: string
  summary: string
  href: string
  linkLabel: string
  mediaAssetId: string
  enabled: boolean
}

export type HomeNotice = {
  id: string
  label: string
  href: string
  tone: 'light' | 'dark' | 'accent'
  enabled: boolean
}

export type HomeComposition = {
  version: 1
  heroSlides: HomeHeroSlide[]
  notices: HomeNotice[]
  newsEyebrow: string
  newsTitle: string
  newsDescription: string
  videoEyebrow: string
  videoTitle: string
  videoDescription: string
  productEyebrow: string
  productTitle: string
  productDescription: string
}

export type SiteLegalContent = {
  copyright: string
  trademark: string
  registration: string
  contactLabel: string
  contactHref: string
}

const id = (prefix: string) => globalThis.crypto?.randomUUID?.() ?? `${prefix}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`

export function createHomeHeroSlide(): HomeHeroSlide {
  return { id: id('hero'), eyebrow: 'LEGION 12', title: '', summary: '', href: '/battle', linkLabel: '了解更多', mediaAssetId: '', enabled: true }
}

export function createHomeNotice(): HomeNotice {
  return { id: id('notice'), label: '', href: '/news', tone: 'light', enabled: true }
}

export function defaultHomeComposition(): HomeComposition {
  return {
    version: 1,
    heroSlides: [],
    notices: [],
    newsEyebrow: 'NEWS', newsTitle: '最新资讯', newsDescription: '规则、赛季、赛事与官方网站的重要更新。',
    videoEyebrow: 'COMMUNITY MOVIE', videoTitle: '社群视频', videoDescription: '对局精选、规则教学与玩家社群动态。',
    productEyebrow: 'PRODUCTS', productTitle: '商品情报', productDescription: '十二军团卡牌系列、预组套牌与官方周边。',
  }
}

export function defaultSiteLegal(): SiteLegalContent {
  return {
    copyright: `© ${new Date().getFullYear()} 十二军团 Twelve Legions.`,
    trademark: '“十二军团”及相关标识为其权利人所有。本网站所示卡牌、规则与线上对战内容仅用于官方产品与社群服务。',
    registration: '', contactLabel: '', contactHref: '',
  }
}

export function parseHomeComposition(value?: string): HomeComposition {
  const fallback = defaultHomeComposition()
  if (!value?.trim()) return fallback
  try {
    const source = JSON.parse(value) as Partial<HomeComposition>
    return {
      ...fallback, ...source, version: 1,
      heroSlides: Array.isArray(source.heroSlides) ? source.heroSlides.map((item, index) => ({
        ...createHomeHeroSlide(), ...item, id: String(item.id || `hero-${index}`), mediaAssetId: String(item.mediaAssetId || ''),
        enabled: item.enabled !== false,
      })) : [],
      notices: Array.isArray(source.notices) ? source.notices.map((item, index) => ({
        ...createHomeNotice(), ...item, id: String(item.id || `notice-${index}`),
        tone: ['light', 'dark', 'accent'].includes(item.tone) ? item.tone : 'light', enabled: item.enabled !== false,
      })) : [],
    }
  } catch { return fallback }
}

export function parseSiteLegal(value?: string): SiteLegalContent {
  const fallback = defaultSiteLegal()
  if (!value?.trim()) return fallback
  try { return { ...fallback, ...(JSON.parse(value) as Partial<SiteLegalContent>) } }
  catch { return fallback }
}

export function serializeHomeComposition(value: HomeComposition) {
  return JSON.stringify({ ...value, version: 1 }, null, 2)
}

export function serializeSiteLegal(value: SiteLegalContent) {
  return JSON.stringify(value, null, 2)
}
