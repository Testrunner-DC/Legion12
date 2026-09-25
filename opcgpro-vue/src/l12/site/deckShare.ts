import { automaticExtraCardIdsForMaster, deckCountSummary, type DeckCard, type SavedL12Deck } from '@/l12/decks'
import { compareDeckCardIds } from '@/l12/deckOrdering'
import { resolveCardAssetUrls } from '@/l12/cardAssets'
import { isHorizontalCardType } from '@/l12/cardPresentation'
import QRCode from 'qrcode'

const DECK_CODE_ALPHABET = '23456789ABCDEFGHJKMNPQRSTVWXYZ'
const DECK_CODE_MAX_BYTES = 4096

class DeckCodeReader {
  private offset = 0
  constructor(private readonly bytes: Uint8Array) {}
  get done() { return this.offset === this.bytes.length }
  byte() {
    if (this.offset >= this.bytes.length) throw new Error('牌库码内容不完整')
    return this.bytes[this.offset++]!
  }
  varint() {
    let value = 0; let factor = 1
    for (let index = 0; index < 5; index++) {
      const byte = this.byte()
      value += (byte & 0x7f) * factor
      if ((byte & 0x80) === 0) return value
      factor *= 128
    }
    throw new Error('牌库码数值超出限制')
  }
  string(limit: number) {
    const length = this.varint()
    if (length > limit || this.offset + length > this.bytes.length) throw new Error('牌库码文本超出限制')
    const value = new TextDecoder('utf-8', { fatal: true }).decode(this.bytes.subarray(this.offset, this.offset + length))
    this.offset += length
    return value
  }
}

function writeVarint(output: number[], value: number) {
  if (!Number.isSafeInteger(value) || value < 0) throw new Error('牌库数据无效')
  do {
    const next = value % 128
    value = Math.floor(value / 128)
    output.push(next | (value ? 0x80 : 0))
  } while (value)
}

function writeString(output: number[], value: string) {
  const bytes = new TextEncoder().encode(value)
  writeVarint(output, bytes.length)
  output.push(...bytes)
}

function writeCardId(output: number[], cardId: string) {
  const compact = /^S(\d{2})-([0-9A-Z]{4})$/.exec(cardId)
  if (!compact) { output.push(1); writeString(output, cardId); return }
  output.push(0)
  writeVarint(output, Number(compact[1]))
  writeVarint(output, Number.parseInt(compact[2]!, 36))
}

function readCardId(reader: DeckCodeReader) {
  const kind = reader.byte()
  if (kind === 1) return reader.string(128)
  if (kind !== 0) throw new Error('牌库码卡牌标识无效')
  const season = reader.varint()
  const suffix = reader.varint()
  if (season > 99 || suffix >= 36 ** 4) throw new Error('牌库码卡牌标识超出限制')
  return `S${String(season).padStart(2, '0')}-${suffix.toString(36).toUpperCase().padStart(4, '0')}`
}

function writeCardList(output: number[], values: readonly string[]) {
  const counts = new Map<string, number>()
  values.forEach(cardId => counts.set(cardId, (counts.get(cardId) ?? 0) + 1))
  writeVarint(output, counts.size)
  ;[...counts].sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0)
    .forEach(([cardId, quantity]) => { writeCardId(output, cardId); writeVarint(output, quantity) })
}

function readCardList(reader: DeckCodeReader) {
  const groups = reader.varint()
  if (groups > 256) throw new Error('牌库码卡牌种类超出限制')
  const values: string[] = []
  for (let index = 0; index < groups; index++) {
    const cardId = readCardId(reader)
    const quantity = reader.varint()
    if (!cardId || quantity < 1 || values.length + quantity > 512) throw new Error('牌库码卡牌数量无效')
    for (let copy = 0; copy < quantity; copy++) values.push(cardId)
  }
  return values
}

function crc32(bytes: Uint8Array) {
  let crc = 0xffffffff
  for (const byte of bytes) {
    crc ^= byte
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0)
  }
  return (crc ^ 0xffffffff) >>> 0
}

function encodeBase30(bytes: Uint8Array) {
  const digits = [0]
  for (const byte of bytes) {
    let carry = byte
    for (let index = 0; index < digits.length; index++) {
      const value = digits[index]! * 256 + carry
      digits[index] = value % 30
      carry = Math.floor(value / 30)
    }
    while (carry) { digits.push(carry % 30); carry = Math.floor(carry / 30) }
  }
  return digits.reverse().map(value => DECK_CODE_ALPHABET[value]).join('')
}

function decodeBase30(value: string) {
  const bytes = [0]
  for (const character of value) {
    const digit = DECK_CODE_ALPHABET.indexOf(character)
    if (digit < 0) throw new Error('牌库码包含易混淆或不支持的字符')
    let carry = digit
    for (let index = 0; index < bytes.length; index++) {
      const current = bytes[index]! * 30 + carry
      bytes[index] = current & 0xff
      carry = current >>> 8
    }
    while (carry) { bytes.push(carry & 0xff); carry >>>= 8 }
    if (bytes.length > DECK_CODE_MAX_BYTES) throw new Error('牌库码超出长度限制')
  }
  return Uint8Array.from(bytes.reverse())
}

export function encodeDeckCode(deck: SavedL12Deck) {
  const output = [2]
  writeString(output, deck.name.slice(0, 24))
  writeCardId(output, deck.masterId)
  writeCardList(output, deck.cardIds)
  writeCardList(output, deck.moraleIds)
  writeCardList(output, deck.specialIds ?? [])
  const body = Uint8Array.from(output)
  const checksum = crc32(body)
  const bytes = Uint8Array.from([...body, checksum >>> 24, checksum >>> 16 & 0xff, checksum >>> 8 & 0xff, checksum & 0xff])
  const encoded = encodeBase30(bytes)
  return `L12D2-${encoded.match(/.{1,5}/g)?.join('-') ?? encoded}`
}

export function decodeDeckCode(code: string): SavedL12Deck {
  const trimmed = code.trim()
  if (!/^L12D2-/i.test(trimmed)) throw new Error('不是有效的十二军团牌库码')
  const encoded = trimmed.slice(6).replace(/[\s-]/g, '').toUpperCase()
  if (!encoded || encoded.length > DECK_CODE_MAX_BYTES * 2) throw new Error('牌库码超出长度限制')
  const bytes = decodeBase30(encoded)
  if (bytes.length < 6) throw new Error('牌库码内容不完整')
  const body = bytes.subarray(0, -4)
  const expected = ((bytes.at(-4)! << 24) | (bytes.at(-3)! << 16) | (bytes.at(-2)! << 8) | bytes.at(-1)!) >>> 0
  if (crc32(body) !== expected) throw new Error('牌库码校验失败，请检查是否复制完整')
  const reader = new DeckCodeReader(body)
  if (reader.byte() !== 2) throw new Error('牌库码版本不受支持')
  const name = reader.string(256)
  const masterId = readCardId(reader)
  const cardIds = readCardList(reader)
  const moraleIds = readCardList(reader)
  const specialIds = readCardList(reader)
  if (!reader.done || !name || !masterId) throw new Error('牌库码内容不完整')
  return { name: name.slice(0, 24), masterId, cardIds, moraleIds, specialIds, updatedAt: new Date().toISOString() }
}

async function loadImage(cardId: string | undefined, legacyUrl?: string) {
  const candidates = await resolveCardAssetUrls(cardId ?? '', legacyUrl, 'detail')
  for (const url of candidates) {
    try {
      const response = await fetch(url, { credentials: url.startsWith('/') ? 'same-origin' : 'omit' })
      if (!response.ok) continue
      return await createImageBitmap(await response.blob())
    } catch {
      // 单向尝试下一候选；任何单图失败都不能中断整张牌库图。
    }
  }
  return null
}

function roundedRect(context: CanvasRenderingContext2D, x: number, y: number, width: number, height: number, radius: number) {
  context.beginPath()
  context.roundRect(x, y, width, height, radius)
  context.fill()
}

function loadDataImage(url: string) {
  return new Promise<HTMLImageElement>((resolve, reject) => {
    const image = new Image()
    image.onload = () => resolve(image)
    image.onerror = () => reject(new Error('二维码图像生成失败'))
    image.src = url
  })
}

export interface DeckImageAlternateArt {
  id: string
  artCode?: string
  displayName?: string
  imageUrl?: string
  thumbnailUrl?: string
  cardImageId?: string
  builtIn?: boolean
}

export interface DeckImageOptions {
  publicUrl?: string
  alternateArts?: readonly DeckImageAlternateArt[]
}

export interface DeckImageAppearance {
  artId: string
  label: string
  cardImageId: string
  legacyUrl?: string
}

export interface DeckImageGroup extends DeckImageAppearance {
  cardId: string
  count: number
}

function appearanceFor(cardId: string, artId: string, arts: ReadonlyMap<string, DeckImageAlternateArt>, byId: ReadonlyMap<string, DeckCard>): DeckImageAppearance {
  const art = artId ? arts.get(artId) : undefined
  return {
    artId,
    label: art ? [art.artCode, art.displayName].filter(Boolean).join(' · ') : artId ? `异画 · ${artId}` : '原画',
    cardImageId: art?.cardImageId || (artId || cardId),
    legacyUrl: art ? (art.builtIn ? undefined : art.thumbnailUrl || art.imageUrl) : artId ? undefined : byId.get(cardId)?.imageUrl,
  }
}

export function deckImageGroups(deck: SavedL12Deck, catalog: DeckCard[], alternateArts: readonly DeckImageAlternateArt[] = []): DeckImageGroup[] {
  const byId = new Map(catalog.map(card => [card.id, card]))
  const arts = new Map(alternateArts.map(art => [art.id, art]))
  const masterFaction = byId.get(deck.masterId)?.faction
  const totals = deck.cardIds.reduce((map, id) => map.set(id, (map.get(id) || 0) + 1), new Map<string, number>())
  return [...totals.keys()].sort((left, right) => compareDeckCardIds(left, right, byId, masterFaction)).flatMap(cardId => {
    const count = totals.get(cardId) ?? 0
    const explicit = [...(deck.alternateArtCopies?.[cardId] ?? [])].slice(0, count)
    const appearances = explicit.length ? explicit : Array(count).fill(deck.alternateArtSelections?.[cardId] ?? '') as string[]
    while (appearances.length < count) appearances.push('')
    const grouped = new Map<string, number>()
    appearances.forEach(artId => grouped.set(artId, (grouped.get(artId) ?? 0) + 1))
    return [...grouped].map(([artId, appearanceCount]) => ({
      cardId,
      count: appearanceCount,
      ...appearanceFor(cardId, artId, arts, byId),
    }))
  })
}

export async function createDeckImageBlob(deck: SavedL12Deck, catalog: DeckCard[], options: DeckImageOptions = {}) {
  const byId = new Map(catalog.map(card => [card.id, card]))
  const publicUrl = /^https?:\/\//i.test(options.publicUrl?.trim() ?? '') ? options.publicUrl!.trim() : ''
  const alternateArts = new Map((options.alternateArts ?? []).map(art => [art.id, art]))
  const groups = deckImageGroups(deck, catalog, options.alternateArts)
  const extraIds = [...new Set([
    ...(deck.specialIds ?? []),
    ...automaticExtraCardIdsForMaster(deck.masterId),
  ])]
  const columns = Math.min(10, Math.max(5, Math.ceil(groups.length / 2)))
  const rows = Math.max(1, Math.ceil(groups.length / columns))
  const canvas = document.createElement('canvas')
  canvas.width = 1920
  canvas.height = 1080
  const context = canvas.getContext('2d')!
  const gradient = context.createLinearGradient(0, 0, canvas.width, canvas.height)
  gradient.addColorStop(0, '#08151a'); gradient.addColorStop(.48, '#080d11'); gradient.addColorStop(1, '#260c12')
  context.fillStyle = gradient; context.fillRect(0, 0, canvas.width, canvas.height)
  context.strokeStyle = 'rgba(225,194,115,.22)'; context.lineWidth = 1
  for (let x = -300; x < 2100; x += 58) { context.beginPath(); context.moveTo(x, 0); context.lineTo(x + 430, 1080); context.stroke() }
  context.fillStyle = 'rgba(4,8,10,.72)'; roundedRect(context, 34, 34, 334, 1012, 8)
  context.strokeStyle = 'rgba(225,194,115,.62)'; context.lineWidth = 2; context.strokeRect(24, 24, canvas.width - 48, canvas.height - 48)
  context.fillStyle = '#55c4cb'; context.font = '900 18px Microsoft YaHei'; context.fillText('LEGION 12 · DECK ARCHIVE', 410, 60)
  context.fillStyle = '#f4f0e6'; context.font = '900 42px Microsoft YaHei'; context.fillText(deck.name, 410, 112)
  const master = byId.get(deck.masterId)
  context.fillStyle = '#a7b0b4'; context.font = '700 18px Microsoft YaHei'; context.fillText(`主宰 ${master?.nameZh || deck.masterId}  ·  主牌 ${deckCountSummary(deck.cardIds, byId).label}  ·  士气 ${deck.moraleIds.length}  ·  额外 ${extraIds.length}`, 410, 145)
  context.fillStyle = '#e1bf6d'; context.fillRect(410, 168, 1464, 3)

  const loadedBitmaps = await Promise.all([
    (() => { const appearance = appearanceFor(deck.masterId, deck.alternateArtSelections?.[deck.masterId] ?? '', alternateArts, byId); return loadImage(appearance.cardImageId, appearance.legacyUrl) })(),
    ...groups.map(group => loadImage(group.cardImageId, group.legacyUrl)),
    ...extraIds.map(id => { const appearance = appearanceFor(id, deck.alternateArtSelections?.[id] ?? '', alternateArts, byId); return loadImage(appearance.cardImageId, appearance.legacyUrl) }),
  ])
  const masterBitmap = loadedBitmaps[0]
  const bitmaps = loadedBitmaps.slice(1, 1 + groups.length)
  const extraBitmaps = loadedBitmaps.slice(1 + groups.length)
  const qrImage = publicUrl
    ? await QRCode.toDataURL(publicUrl, { errorCorrectionLevel: 'M', margin: 3, width: 180, color: { dark: '#050708', light: '#ffffff' } })
      .then(loadDataImage)
    : null
  context.fillStyle = '#10171b'; roundedRect(context, 74, 104, 254, 356, 4)
  if (masterBitmap) context.drawImage(masterBitmap, 74, 104, 254, 356)
  else { context.fillStyle = '#263139'; context.fillRect(74, 104, 254, 356) }
  context.fillStyle = '#f4f0e6'; context.font = '900 25px Microsoft YaHei'; context.textAlign = 'center'; context.fillText(master?.nameZh || '主宰', 201, 505)
  const factionName = ({ tianting: '天廷', gaotianyuan: '高天原', asgard: '阿斯加德', taiyangcheng: '太阳城', olympus: '奥林匹斯', otherworld: '彼界' } as Record<string, string>)[master?.faction || ''] || master?.faction || ''
  context.fillStyle = '#55c4cb'; context.font = '900 16px Microsoft YaHei'; context.fillText(factionName, 201, 534)
  context.fillStyle = '#10171b'; roundedRect(context, 74, 570, 254, 76, 4)
  context.fillStyle = '#e1bf6d'; context.font = '900 34px Microsoft YaHei'; context.fillText(String(deck.moraleIds.length), 146, 620)
  context.fillStyle = '#f4f0e6'; context.font = '900 18px Microsoft YaHei'; context.fillText('士气', 214, 618)
  context.fillStyle = '#89959a'; context.font = '700 12px Microsoft YaHei'; context.fillText(`${groups.length} 种主牌`, 214, 638)
  context.textAlign = 'left'

  if (extraIds.length) {
    context.fillStyle = '#e1bf6d'; context.font = '900 15px Microsoft YaHei'; context.fillText(`额外区 · ${extraIds.length} 张`, 74, 683)
    const extraAreaX = 74; const extraAreaY = 700; const extraAreaWidth = 254; const extraAreaHeight = 274
    const extraColumns = Math.min(2, extraIds.length)
    const extraRows = Math.ceil(extraIds.length / extraColumns)
    const extraGapX = 10; const extraGapY = 8
    const extraCellWidth = (extraAreaWidth - extraGapX * (extraColumns - 1)) / extraColumns
    const extraCellHeight = (extraAreaHeight - extraGapY * (extraRows - 1)) / extraRows
    extraIds.forEach((id, index) => {
      const card = byId.get(id)
      const aspect = isHorizontalCardType(card?.cardType) ? 1752 / 1255 : 5 / 7
      const imageWidth = Math.min(extraCellWidth, (extraCellHeight - 28) * aspect)
      const imageHeight = imageWidth / aspect
      const cellX = extraAreaX + (index % extraColumns) * (extraCellWidth + extraGapX)
      const cellY = extraAreaY + Math.floor(index / extraColumns) * (extraCellHeight + extraGapY)
      const x = cellX + (extraCellWidth - imageWidth) / 2
      context.fillStyle = '#10171b'; roundedRect(context, x, cellY, imageWidth, imageHeight, 3)
      const bitmap = extraBitmaps[index]
      if (bitmap) context.drawImage(bitmap, x, cellY, imageWidth, imageHeight)
      else { context.fillStyle = '#263139'; context.fillRect(x, cellY, imageWidth, imageHeight) }
      context.fillStyle = '#f1ede3'; context.font = '900 11px Microsoft YaHei'; context.textAlign = 'center'
      context.fillText((card?.nameZh || id).slice(0, 9), cellX + extraCellWidth / 2, cellY + imageHeight + 16)
    })
    context.textAlign = 'left'
  }

  const areaX = 410; const areaY = 198; const areaWidth = 1464; const areaHeight = publicUrl ? 650 : 784
  const gapX = 13
  const rowPitch = areaHeight / rows
  const cardWidth = Math.min(162, (areaWidth - gapX * (columns - 1)) / columns, (rowPitch - 42) / 1.4)
  const cardHeight = cardWidth * 1.4
  groups.forEach((group, index) => {
    const card = byId.get(group.cardId)
    const col = index % columns
    const row = Math.floor(index / columns)
    const x = areaX + col * (cardWidth + gapX)
    const y = areaY + row * rowPitch
    context.fillStyle = '#0b1116'; roundedRect(context, x, y, cardWidth, cardHeight, 3)
    const bitmap = bitmaps[index]
    if (bitmap) context.drawImage(bitmap, x, y, cardWidth, cardHeight)
    else { context.fillStyle = '#263139'; context.fillRect(x, y, cardWidth, cardHeight); context.fillStyle = '#77858c'; context.font = '900 13px Microsoft YaHei'; context.fillText('暂无卡图', x + 25, y + cardHeight / 2) }
    context.fillStyle = '#f1ede3'; context.font = '900 13px Microsoft YaHei'; context.fillText((card?.nameZh || group.cardId).slice(0, 10), x + 2, y + cardHeight + 18)
    context.fillStyle = '#7f8b90'; context.font = '700 10px Microsoft YaHei'; context.fillText(group.label.slice(0, 18), x + 2, y + cardHeight + 33)
    context.fillStyle = '#e1bf6d'
    const badgeX = x + cardWidth - 16
    context.beginPath(); context.arc(badgeX, y + 16, 16, 0, Math.PI * 2); context.fill()
    context.fillStyle = '#0b0e10'; context.font = '900 15px Microsoft YaHei'; context.textAlign = 'center'; context.fillText(`×${group.count}`, badgeX, y + 21); context.textAlign = 'left'
  })
  if (qrImage && publicUrl) {
    const qrSize = 132
    const qrX = 1860 - qrSize
    const qrY = 864
    context.fillStyle = '#ffffff'; context.fillRect(qrX - 6, qrY - 6, qrSize + 12, qrSize + 12)
    context.drawImage(qrImage, qrX, qrY, qrSize, qrSize)
  }
  context.fillStyle = '#7f8b90'; context.font = '700 14px Microsoft YaHei'; context.fillText('由十二军团网页平台生成 · 可使用牌库码导入', 74, canvas.height - 70)
  context.fillStyle = '#e1bf6d'; context.font = '900 19px Microsoft YaHei'; context.fillText('LEGION12', 74, canvas.height - 42)
  masterBitmap?.close()
  bitmaps.forEach(bitmap => bitmap?.close())
  extraBitmaps.forEach(bitmap => bitmap?.close())
  return await new Promise<Blob>((resolve, reject) => canvas.toBlob(value => value ? resolve(value) : reject(new Error('牌库图生成失败')), 'image/png'))
}

export async function downloadDeckImage(deck: SavedL12Deck, catalog: DeckCard[], existingBlob?: Blob, options: DeckImageOptions = {}) {
  const blob = existingBlob || await createDeckImageBlob(deck, catalog, options)
  const anchor = document.createElement('a')
  anchor.href = URL.createObjectURL(blob)
  anchor.download = `${deck.name.replace(/[\\/:*?"<>|]/g, '_')}-牌库图.png`
  anchor.click()
  setTimeout(() => URL.revokeObjectURL(anchor.href), 1000)
}
