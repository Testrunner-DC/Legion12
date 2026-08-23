import sharp from 'sharp'
import { createHash } from 'node:crypto'
import { mkdir, readFile, readdir, stat, writeFile } from 'node:fs/promises'
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
const baseUrl = (args.get('--base-url') || 'https://cards.legion12.grand-umi.com').replace(/\/$/, '')
const catalogFiles = (args.get('--catalog-files') || '').split(';').filter(Boolean).map(file => resolve(file))
const requestedCardIds = new Set((args.get('--card-ids') || '').split(';').filter(Boolean).map(id => id.toUpperCase()))
const concurrency = Math.max(1, Math.min(12, Number(args.get('--concurrency') || 4)))
const horizontalTypes = new Set(['disaster', 'destruction', 'trial'])
const imageExtensions = new Set(['.png', '.jpg', '.jpeg', '.webp', '.avif'])

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
    variants: Object.fromEntries(Object.entries(files).map(([name, file]) => [name, `${baseUrl}/${keyRoot}/${file}`])),
    bytes: sizes,
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

await mkdir(outputRoot, { recursive: true })
const sourceFiles = await walk(sourceRoot)
const catalog = (await readCatalog()).filter(card => requestedCardIds.size === 0 || requestedCardIds.has(card.id.toUpperCase()))
if (requestedCardIds.size > 0) {
  const found = new Set(catalog.map(card => card.id.toUpperCase()))
  const unknown = [...requestedCardIds].filter(id => !found.has(id))
  if (unknown.length) throw new Error(`卡牌目录中不存在：${unknown.join('、')}`)
}
const jobs = catalog.map(card => ({ card, source: matchSource(card, sourceFiles) }))
const missing = jobs.filter(job => !job.source).map(job => ({ cardId: job.card.id, name: job.card.nameZh, sourceUrl: job.card.imageUrl }))
const ready = jobs.filter(job => job.source)
const cards = await mapLimit(ready, concurrency, async ({ card, source }, index) => {
  const result = await buildCard(card, source)
  process.stdout.write(`\r已处理 ${index + 1}/${ready.length}`)
  return result
})
process.stdout.write('\n')

const manifest = {
  schemaVersion: 1,
  catalogVersion,
  generatedAt: new Date().toISOString(),
  baseUrl,
  immutableCacheControl: 'public, max-age=31536000, immutable',
  cards: Object.fromEntries(cards.map(card => [card.cardId, card])),
  missing,
}
const preload = {
  catalogVersion,
  generatedAt: manifest.generatedAt,
  entries: cards
    .filter(card => ['master', 'disaster', 'destruction', 'trial'].includes(card.cardType))
    .map(card => ({ cardId: card.cardId, url: card.variants.thumbWebp, as: 'image', type: 'image/webp' })),
}
await writeFile(join(outputRoot, 'card-assets.manifest.json'), JSON.stringify(manifest, null, 2))
await writeFile(join(outputRoot, 'card-assets.preload.json'), JSON.stringify(preload, null, 2))
console.log(`完成 ${cards.length} 张，缺少源文件 ${missing.length} 张。输出：${outputRoot}`)
if (missing.length) process.exitCode = 2
