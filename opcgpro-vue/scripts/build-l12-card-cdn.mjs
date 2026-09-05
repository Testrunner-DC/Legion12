import sharp from 'sharp'
import { createHash } from 'node:crypto'
import { mkdir, readFile, readdir, rename, stat, writeFile } from 'node:fs/promises'
import { basename, extname, join, resolve } from 'node:path'

const args = new Map()
for (let index = 2; index < process.argv.length; index += 2) args.set(process.argv[index], process.argv[index + 1])
const required = key => {
  const value = args.get(key)
  if (!value) throw new Error(`缺少参数 ${key}`)
  return resolve(value)
}
const sourceRoot = required('--source')
const outputRoot = required('--output')
const catalogVersion = args.get('--catalog-version') || new Date().toISOString().slice(0, 10).replaceAll('-', '')
const baseUrl = (args.get('--base-url') || '/card-assets').replace(/\/$/, '')
const catalogFiles = (args.get('--catalog-files') || '').split(';').filter(Boolean).map(file => resolve(file))
const presentationCatalogFile = required('--presentation-catalog')
const requestedCardIds = new Set((args.get('--card-ids') || '').split(';').filter(Boolean).map(id => id.toUpperCase()))
const concurrency = Math.max(1, Math.min(12, Number(args.get('--concurrency') || 4)))
const horizontalTypes = new Set(['disaster', 'destruction', 'trial'])
const imageExtensions = new Set(['.png', '.jpg', '.jpeg', '.webp', '.avif'])
const expectedPlayableCardCount = 324
const expectedPresentationCardCount = 38
const expectedAssetCount = expectedPlayableCardCount + expectedPresentationCardCount
const maxVariantBytes = {
  originalWebp: 2_500_000,
  thumbWebp: 90_000,
  boardWebp: 240_000,
  detailWebp: 650_000,
  detailAvif: 450_000,
}
const maxTotalBytes = Number(args.get('--max-total-bytes') || 400 * 1024 * 1024)

async function walk(directory) {
  const result = []
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const full = join(directory, entry.name)
    if (entry.isDirectory()) result.push(...await walk(full))
    else if (imageExtensions.has(extname(entry.name).toLowerCase())) result.push(full)
  }
  return result
}

async function readCatalog() {
  const cards = []
  for (const file of catalogFiles) cards.push(...JSON.parse(await readFile(file, 'utf8')))
  return cards
}

async function readPresentationCatalog() {
  const value = JSON.parse(await readFile(presentationCatalogFile, 'utf8'))
  if (value.schemaVersion !== 1 || !Array.isArray(value.cards)) throw new Error('卡牌档案展示资源目录格式无效')
  return value.cards
}

function matchSource(card, files) {
  const normalizedId = card.id.toLowerCase()
  const exact = files.find(file => basename(file, extname(file)).toLowerCase() === normalizedId)
  return exact || files.find(file => basename(file).toLowerCase().startsWith(`${normalizedId}-`))
}

async function encodeVariant(input, format, width, output) {
  const pipeline = sharp(input, { failOn: 'warning' }).rotate().resize({ width, withoutEnlargement: true })
  await mkdir(resolve(output, '..'), { recursive: true })
  if (format === 'avif') await pipeline.avif({ quality: 58, effort: 6 }).toFile(output)
  else await pipeline.webp({ quality: width >= 900 ? 88 : 82, effort: 5 }).toFile(output)
  return (await stat(output)).size
}

async function buildCard(card, source) {
  const bytes = await readFile(source)
  const hash = createHash('sha256').update(bytes).digest('hex')
  const hashShort = hash.slice(0, 20)
  const metadata = await sharp(bytes).metadata()
  if (!metadata.width || !metadata.height) throw new Error(`${card.id} 无法读取图片尺寸`)
  const keyRoot = `cards/${catalogVersion}/${card.id}/${hashShort}`
  const destination = join(outputRoot, ...keyRoot.split('/'))
  await mkdir(destination, { recursive: true })
  const files = {
    originalWebp: 'original.webp',
    thumbWebp: 'thumb-240.webp',
    boardWebp: 'board-480.webp',
    detailWebp: 'detail-960.webp',
    detailAvif: 'detail-960.avif',
  }
  const sizes = {}
  sizes.originalWebp = await encodeVariant(bytes, 'webp', metadata.width, join(destination, files.originalWebp))
  sizes.thumbWebp = await encodeVariant(bytes, 'webp', 240, join(destination, files.thumbWebp))
  sizes.boardWebp = await encodeVariant(bytes, 'webp', 480, join(destination, files.boardWebp))
  sizes.detailWebp = await encodeVariant(bytes, 'webp', 960, join(destination, files.detailWebp))
  sizes.detailAvif = await encodeVariant(bytes, 'avif', 960, join(destination, files.detailAvif))
  return {
    cardId: card.id,
    name: card.nameZh,
    cardType: card.cardType,
    contentHash: hash,
    width: metadata.width,
    height: metadata.height,
    orientation: horizontalTypes.has(card.cardType) || metadata.width > metadata.height ? 'landscape' : 'portrait',
    sourceArchiveName: basename(source),
    variants: Object.fromEntries(Object.entries(files).map(([name, file]) => [name, `${keyRoot}/${file}`])),
    bytes: sizes,
    presentationOnly: card.presentationOnly === true,
    baseCardId: card.baseCardId || undefined,
  }
}

async function mapLimit(items, limit, mapper) {
  const results = new Array(items.length)
  let next = 0
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (next < items.length) {
      const index = next++
      results[index] = await mapper(items[index], index)
    }
  }))
  return results
}

const sourceFiles = await walk(sourceRoot)
const completeCatalog = await readCatalog()
const catalogIds = new Set(completeCatalog.map(card => card.id.toUpperCase()))
if (completeCatalog.length !== catalogIds.size) throw new Error('卡牌目录存在重复卡号，拒绝生成资源清单')
if (completeCatalog.length !== expectedPlayableCardCount) throw new Error(`完整可玩卡牌目录必须为 ${expectedPlayableCardCount} 张，当前 ${completeCatalog.length} 张`)
if (completeCatalog.some(card => !/^(?:S\d{2}|ST\d{2}|ST)-[A-Z0-9]+$/i.test(card.id))) throw new Error('卡牌目录包含不安全卡号，拒绝生成文件路径')
const presentationCatalog = await readPresentationCatalog()
const playableById = new Map(completeCatalog.map(card => [card.id, card]))
if (presentationCatalog.length !== expectedPresentationCardCount) throw new Error(`卡牌档案展示资源必须为 ${expectedPresentationCardCount} 张，当前 ${presentationCatalog.length} 张`)
const presentationCards = presentationCatalog.map(entry => {
  const base = playableById.get(entry.baseCardId)
  if (!base) throw new Error(`展示资源 ${entry.id} 的规则基底不存在：${entry.baseCardId}`)
  if (catalogIds.has(entry.id.toUpperCase())) throw new Error(`展示资源不得覆盖可玩卡号：${entry.id}`)
  return { ...base, ...entry, nameZh: entry.nameZh || base.nameZh, cardType: base.cardType, presentationOnly: true }
})
const assetCatalog = [...completeCatalog, ...presentationCards]
const assetIds = new Set(assetCatalog.map(card => card.id.toUpperCase()))
if (assetCatalog.length !== expectedAssetCount || assetIds.size !== expectedAssetCount) throw new Error(`完整资源目录必须为 ${expectedAssetCount} 个唯一卡号`)
const catalog = assetCatalog.filter(card => requestedCardIds.size === 0 || requestedCardIds.has(card.id.toUpperCase()))
if (requestedCardIds.size > 0) {
  const found = new Set(catalog.map(card => card.id.toUpperCase()))
  const unknown = [...requestedCardIds].filter(id => !found.has(id))
  if (unknown.length) throw new Error(`卡牌目录中不存在：${unknown.join('、')}`)
}
const jobs = catalog.map(card => ({ card, source: matchSource(card, sourceFiles) }))
const missing = jobs.filter(job => !job.source).map(job => ({ cardId: job.card.id, name: job.card.nameZh, sourceUrl: job.card.imageUrl }))
if (missing.length) throw new Error(`卡图归档不完整（${missing.length}/${catalog.length}）：${missing.map(card => card.cardId).join('、')}`)
await mkdir(outputRoot, { recursive: true })
const cards = await mapLimit(jobs, concurrency, async ({ card, source }, index) => {
  const result = await buildCard(card, source)
  process.stdout.write(`\r已处理 ${index + 1}/${jobs.length}`)
  return result
})
process.stdout.write('\n')

if (requestedCardIds.size === 0 && cards.length !== expectedAssetCount) throw new Error(`发布资源必须完整生成 ${expectedAssetCount} 张，当前 ${cards.length} 张`)
const oversized = cards.flatMap(card => Object.entries(card.bytes)
  .filter(([variant, bytes]) => bytes > maxVariantBytes[variant])
  .map(([variant, bytes]) => `${card.cardId}:${variant}=${bytes}`))
if (oversized.length) throw new Error(`卡图变体超过体积门禁：${oversized.join('、')}`)
const totalBytes = cards.reduce((sum, card) => sum + Object.values(card.bytes).reduce((cardSum, bytes) => cardSum + bytes, 0), 0)
if (totalBytes > maxTotalBytes) throw new Error(`优化卡图总量 ${totalBytes} 超过门禁 ${maxTotalBytes}`)
const assetVersion = createHash('sha256')
  .update(cards.map(card => [
    card.cardId,
    card.contentHash,
    card.presentationOnly ? 'presentation' : 'playable',
    card.baseCardId || '',
  ].join(':')).sort().join('\n'))
  .digest('hex')

const manifest = {
  schemaVersion: 3,
  catalogVersion,
  assetVersion,
  generatedAt: new Date().toISOString(),
  basePath: '/card-assets',
  cdnBaseUrl: /^https:\/\//i.test(baseUrl) ? baseUrl : '',
  complete: requestedCardIds.size === 0,
  cardCount: cards.length,
  playableCardCount: cards.filter(card => !card.presentationOnly).length,
  presentationCardCount: cards.filter(card => card.presentationOnly).length,
  immutableCacheControl: 'public, max-age=31536000, immutable',
  manifestCacheControl: 'public, max-age=300, must-revalidate',
  totalBytes,
  cards: Object.fromEntries(cards.map(card => [card.cardId, card])),
  missing: [],
}
const preload = {
  catalogVersion,
  generatedAt: manifest.generatedAt,
  entries: cards
    .filter(card => !card.presentationOnly && ['master', 'disaster', 'destruction', 'trial'].includes(card.cardType))
    .map(card => ({ cardId: card.cardId, url: `/card-assets/${card.variants.thumbWebp}`, as: 'image', type: 'image/webp' })),
}
async function writeJsonAtomic(name, value) {
  const target = join(outputRoot, name)
  const partial = `${target}.${process.pid}.partial`
  await writeFile(partial, JSON.stringify(value, null, 2))
  await rename(partial, target)
}

// 所有二进制均成功且通过体积门禁后，才原子公布 manifest。
await writeJsonAtomic('card-assets.preload.json', preload)
await writeJsonAtomic('card-assets.manifest.json', manifest)
console.log(`完成 ${cards.length} 张，资源版本 ${assetVersion}，总量 ${totalBytes} 字节。输出：${outputRoot}`)
