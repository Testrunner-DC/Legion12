import type { SavedL12Deck } from './decks'

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


// The cache format reuses only these binary primitives, never the name-bearing share format.
export { DeckCodeReader, writeVarint, writeString, writeCardId, readCardId, crc32 }
