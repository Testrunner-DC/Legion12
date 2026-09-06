import type { SiteHomePayload } from '@/l12/platform'

const CACHE_KEY = 'l12-home-published-v2'
const LEGACY_KEYS = ['l12-home-published-v1', 'l12-site-home-v1']

type CachedHome = { version: 2; savedAt: string; payload: SiteHomePayload }

function isPayload(value: unknown): value is SiteHomePayload {
  if (!value || typeof value !== 'object') return false
  const candidate = value as Partial<SiteHomePayload>
  return typeof candidate.composition === 'string'
    && typeof candidate.legal === 'string'
    && Array.isArray(candidate.news)
    && Array.isArray(candidate.videos)
    && Array.isArray(candidate.products)
    && Array.isArray(candidate.media)
}

function parse(value: string | null): SiteHomePayload | null {
  if (!value) return null
  try {
    const decoded = JSON.parse(value) as unknown
    if (isPayload(decoded)) return decoded
    if (decoded && typeof decoded === 'object' && isPayload((decoded as { payload?: unknown }).payload))
      return (decoded as { payload: SiteHomePayload }).payload
  } catch { /* Corrupt cache must never replace the in-memory published state. */ }
  return null
}

export function loadPublishedHomeCache(storage: Pick<Storage, 'getItem'> = localStorage) {
  const current = parse(storage.getItem(CACHE_KEY))
  if (current) return current
  for (const key of LEGACY_KEYS) {
    const legacy = parse(storage.getItem(key))
    if (legacy) return legacy
  }
  return null
}

export function savePublishedHomeCache(payload: SiteHomePayload,
  storage: Pick<Storage, 'setItem'> = localStorage) {
  if (!isPayload(payload)) return false
  try {
    const cached: CachedHome = { version: 2, savedAt: new Date().toISOString(), payload }
    storage.setItem(CACHE_KEY, JSON.stringify(cached))
    return true
  } catch { return false }
}
