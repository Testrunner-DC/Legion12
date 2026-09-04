export type CardImageIntent = 'thumb' | 'board' | 'detail'

export interface CardAssetVariants {
  originalWebp?: string
  thumbWebp?: string
  boardWebp?: string
  detailWebp?: string
  detailAvif?: string
}

export interface CardAssetManifestEntry {
  cardId: string
  contentHash: string
  width: number
  height: number
  orientation: 'portrait' | 'landscape'
  variants: CardAssetVariants
}

export interface CardAssetManifest {
  schemaVersion: 3
  catalogVersion: string
  assetVersion: string
  basePath: string
  cdnBaseUrl?: string
  cards: Record<string, CardAssetManifestEntry>
}

export interface CardAssetSource {
  kind: 'cdn' | 'sameOrigin' | 'placeholder'
  lowWebp: string
  webp: string
  avif?: string
}

export interface ResolvedCardAsset {
  cardId: string
  intent: CardImageIntent
  orientation?: CardAssetManifestEntry['orientation']
  sources: CardAssetSource[]
}

const MANIFEST_PATH = '/card-assets/card-assets.manifest.json'
const SAME_ORIGIN_ROOT = '/card-assets'
const RETRY_COOLDOWN_MS = 15_000

export const CARD_IMAGE_PLACEHOLDER = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(`
  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 240 336">
    <rect width="240" height="336" fill="#090d0e"/>
    <rect x="7" y="7" width="226" height="322" rx="5" fill="none" stroke="#59625f" stroke-width="2"/>
    <text x="120" y="160" fill="#d5bc70" font-family="Georgia,serif" font-size="36" text-anchor="middle">XII</text>
    <text x="120" y="190" fill="#78817e" font-family="sans-serif" font-size="12" text-anchor="middle">CARD IMAGE</text>
  </svg>
`)}`

let manifestPromise: Promise<CardAssetManifest | null> | null = null
let manifestValue: CardAssetManifest | null = null
let retryAfter = 0
let missingEntryRefreshAfter = 0

function configuredManifestUrl() {
  return import.meta.env.VITE_L12_CARD_ASSET_MANIFEST || MANIFEST_PATH
}

function configuredCdnBase() {
  return (import.meta.env.VITE_L12_CARD_ASSET_CDN || '').replace(/\/$/, '')
}

function isManifest(value: unknown): value is CardAssetManifest {
  if (!value || typeof value !== 'object') return false
  const candidate = value as Partial<CardAssetManifest>
  return candidate.schemaVersion === 3
    && typeof candidate.catalogVersion === 'string'
    && typeof candidate.assetVersion === 'string'
    && typeof candidate.basePath === 'string'
    && !!candidate.cards
    && typeof candidate.cards === 'object'
}

function manifestUrl(force: boolean) {
  const url = configuredManifestUrl()
  if (!force) return url
  const separator = url.includes('?') ? '&' : '?'
  return `${url}${separator}refresh=${Date.now()}`
}

export async function loadCardAssetManifest(force = false): Promise<CardAssetManifest | null> {
  if (!force && manifestValue) return manifestValue
  if (manifestPromise) return manifestPromise
  if (!force && Date.now() < retryAfter) return manifestValue

  const previousManifest = manifestValue

  manifestPromise = fetch(manifestUrl(force), { cache: force ? 'reload' : 'no-cache', credentials: 'same-origin' })
    .then(async response => {
      if (!response.ok) throw new Error(`卡图清单加载失败（${response.status}）`)
      const value: unknown = await response.json()
      if (!isManifest(value)) throw new Error('卡图清单格式无效')
      manifestValue = value
      return value
    })
    .catch(() => {
      retryAfter = Date.now() + RETRY_COOLDOWN_MS
      return previousManifest
    })
    .finally(() => { manifestPromise = null })

  return manifestPromise
}

export function primeCardAssetManifest() {
  void loadCardAssetManifest()
}

function joinAssetUrl(base: string, path: string) {
  const normalizedBase = base.replace(/\/$/, '')
  const normalizedPath = path.replace(/^\//, '')
  if (/^https?:\/\//i.test(normalizedBase)) return `${normalizedBase}/${normalizedPath}`
  return `${normalizedBase.startsWith('/') ? '' : '/'}${normalizedBase}/${normalizedPath}`.replace(/\/+/g, '/')
}

function selectVariants(variants: CardAssetVariants, intent: CardImageIntent) {
  const thumb = variants.thumbWebp || variants.boardWebp || variants.detailWebp || variants.originalWebp
  const board = variants.boardWebp || variants.detailWebp || variants.originalWebp || thumb
  const detail = variants.detailWebp || variants.originalWebp || board || thumb
  if (intent === 'thumb') return { lowWebp: thumb, webp: thumb }
  if (intent === 'board') return { lowWebp: board, webp: board }
  return { lowWebp: board || thumb, webp: detail, avif: variants.detailAvif }
}

function sourceFor(kind: CardAssetSource['kind'], base: string, variants: CardAssetVariants, intent: CardImageIntent): CardAssetSource | null {
  const selected = selectVariants(variants, intent)
  if (!selected.lowWebp || !selected.webp) return null
  return {
    kind,
    lowWebp: joinAssetUrl(base, selected.lowWebp),
    webp: joinAssetUrl(base, selected.webp),
    avif: selected.avif ? joinAssetUrl(base, selected.avif) : undefined,
  }
}

function uniqueSources(sources: Array<CardAssetSource | null>) {
  const seen = new Set<string>()
  return sources.filter((source): source is CardAssetSource => {
    if (!source || seen.has(source.webp)) return false
    seen.add(source.webp)
    return true
  })
}

function resolvedCardAssetFromManifest(
  manifest: CardAssetManifest,
  cardId: string,
  _legacyUrl: string | undefined,
  intent: CardImageIntent,
): ResolvedCardAsset | null {
  const entry = manifest.cards[cardId]
  if (!entry) return null

  const explicitCdnBaseUrl = configuredCdnBase()
  const manifestCdnBaseUrl = manifest.cdnBaseUrl?.replace(/\/$/, '') || ''
  const sameOrigin = manifest.basePath || SAME_ORIGIN_ROOT
  return {
    cardId,
    intent,
    orientation: entry.orientation,
    sources: uniqueSources([
      explicitCdnBaseUrl ? sourceFor('cdn', explicitCdnBaseUrl, entry.variants, intent) : null,
      sourceFor('sameOrigin', sameOrigin, entry.variants, intent),
      !explicitCdnBaseUrl && manifestCdnBaseUrl
        ? sourceFor('cdn', manifestCdnBaseUrl, entry.variants, intent)
        : null,
      { kind: 'placeholder', lowWebp: CARD_IMAGE_PLACEHOLDER, webp: CARD_IMAGE_PLACEHOLDER },
    ]),
  }
}

/**
 * Return the real card asset synchronously once the manifest has been primed.
 * Animation layers use this to avoid mounting a placeholder for one frame while
 * the already-loaded manifest is needlessly awaited again.
 */
export function peekCardAsset(cardId: string, legacyUrl: string | undefined, intent: CardImageIntent) {
  return manifestValue ? resolvedCardAssetFromManifest(manifestValue, cardId, legacyUrl, intent) : null
}

export function fallbackCardAsset(cardId: string, _legacyUrl: string | undefined, intent: CardImageIntent): ResolvedCardAsset {
  return {
    cardId,
    intent,
    sources: [{ kind: 'placeholder', lowWebp: CARD_IMAGE_PLACEHOLDER, webp: CARD_IMAGE_PLACEHOLDER }],
  }
}

export async function resolveCardAsset(cardId: string, legacyUrl: string | undefined, intent: CardImageIntent): Promise<ResolvedCardAsset> {
  let manifest = await loadCardAssetManifest()
  let entry = manifest?.cards[cardId]
  if (manifest && !entry) {
    if (manifestPromise) {
      manifest = await manifestPromise
    } else if (Date.now() >= missingEntryRefreshAfter) {
      missingEntryRefreshAfter = Date.now() + RETRY_COOLDOWN_MS
      manifest = await loadCardAssetManifest(true)
    }
    entry = manifest?.cards[cardId]
  }
  if (!manifest || !entry) return fallbackCardAsset(cardId, legacyUrl, intent)
  return resolvedCardAssetFromManifest(manifest, cardId, legacyUrl, intent)
    ?? fallbackCardAsset(cardId, legacyUrl, intent)
}

export async function resolveCardAssetUrls(cardId: string, legacyUrl: string | undefined, intent: CardImageIntent = 'detail') {
  const resolved = await resolveCardAsset(cardId, legacyUrl, intent)
  return resolved.sources.flatMap(source => [source.avif, source.webp, source.lowWebp]).filter((url, index, urls): url is string => !!url && urls.indexOf(url) === index)
}

