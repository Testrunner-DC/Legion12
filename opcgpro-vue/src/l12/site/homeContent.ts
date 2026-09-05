export const homeCompositionKey = 'home.composition'
export const siteLegalKey = 'site.footer'

export type HomeHeroSlide = {
  id: string
  eyebrow: string
  title: string
  summary: string
  footer: string
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
  return { id: id('hero'), eyebrow: '', title: '', summary: '', footer: '', href: '', linkLabel: '', mediaAssetId: '', enabled: true }
}

export function createHomeNotice(): HomeNotice {
  return { id: id('notice'), label: '', href: '/news', tone: 'light', enabled: true }
}

export function defaultHomeComposition(): HomeComposition {
  return {
    version: 1,
    heroSlides: [],
    notices: [],
    newsEyebrow: 'NEWS', newsTitle: '资讯一览', newsDescription: '',
    videoEyebrow: 'VIDEO', videoTitle: '最新视频', videoDescription: '',
    productEyebrow: 'PRODUCTS', productTitle: '产品上新', productDescription: '',
  }
}

export function defaultSiteLegal(): SiteLegalContent {
  return {
    copyright: `© 2024–${new Date().getFullYear()} Cynic Games. All rights reserved.`,
    trademark: '《十二军团》及相关名称、标志、卡牌图像、规则文本与数字内容的全部权利归 Cynic Games 所有。',
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
