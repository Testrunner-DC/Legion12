import { createHash } from 'node:crypto'
import { readFile, stat } from 'node:fs/promises'
import { basename, join, resolve, sep } from 'node:path'

const args = new Map()
for (let index = 2; index < process.argv.length; index += 2) args.set(process.argv[index], process.argv[index + 1])
const root = resolve(args.get('--root') || '')
if (!args.get('--root')) throw new Error('缺少 --root 优化卡图目录')
const catalogFiles = (args.get('--catalog-files') || '').split(';').filter(Boolean).map(file => resolve(file))
if (!catalogFiles.length) throw new Error('缺少 --catalog-files 权威卡牌目录')

const manifest = JSON.parse(await readFile(join(root, 'card-assets.manifest.json'), 'utf8'))
if (manifest.schemaVersion !== 2 || manifest.complete !== true || manifest.cardCount !== 248) throw new Error('发布清单必须是完整的 schema v2 248 张清单')
const catalog = (await Promise.all(catalogFiles.map(async file => JSON.parse(await readFile(file, 'utf8'))))).flat()
const expectedIds = new Set(catalog.map(card => card.id))
const entries = Object.entries(manifest.cards ?? {})
if (catalog.length !== 248 || expectedIds.size !== 248 || entries.length !== 248) throw new Error('权威目录、唯一卡号与资源清单必须同时为 248')

const allowedNames = new Set(['original.webp', 'thumb-240.webp', 'board-480.webp', 'detail-960.webp', 'detail-960.avif'])
let totalBytes = 0
for (const [cardId, entry] of entries) {
  if (!expectedIds.has(cardId) || entry.cardId !== cardId) throw new Error(`资源清单含未知或错配卡号：${cardId}`)
  const hashShort = entry.contentHash?.slice(0, 20)
  if (!/^[0-9a-f]{20}$/.test(hashShort)) throw new Error(`${cardId} 内容哈希无效`)
  for (const [variant, relative] of Object.entries(entry.variants ?? {})) {
    if (typeof relative !== 'string' || relative.startsWith('/') || relative.includes('..')) throw new Error(`${cardId}:${variant} 路径不是安全相对路径`)
    const expectedPrefix = `cards/${manifest.catalogVersion}/${cardId}/${hashShort}/`
    if (!relative.startsWith(expectedPrefix) || !allowedNames.has(basename(relative))) throw new Error(`${cardId}:${variant} 未使用内容寻址规范路径`)
    const full = resolve(root, ...relative.split('/'))
    if (!full.startsWith(`${root}${sep}`)) throw new Error(`${cardId}:${variant} 越出优化资源目录`)
    const file = await stat(full)
    if (!file.isFile() || file.size <= 0 || file.size !== entry.bytes?.[variant]) throw new Error(`${cardId}:${variant} 文件大小与清单不一致`)
    totalBytes += file.size
  }
}

if (totalBytes !== manifest.totalBytes || totalBytes > 400 * 1024 * 1024) throw new Error(`优化资源总量门禁失败：${totalBytes}`)
const calculatedVersion = createHash('sha256')
  .update(entries.map(([cardId, entry]) => `${cardId}:${entry.contentHash}`).sort().join('\n'))
  .digest('hex')
if (calculatedVersion !== manifest.assetVersion) throw new Error('assetVersion 与 248 张内容哈希不一致')
console.log(`L12 优化卡图审计通过：248/248，${totalBytes} 字节，版本 ${manifest.assetVersion}`)
