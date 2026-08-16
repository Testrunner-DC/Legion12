import type { DeckCard, SavedL12Deck } from '@/l12/decks'

interface DeckCodePayload { v: 1; n: string; m: string; c: string[]; r: string[] }

export function encodeDeckCode(deck: SavedL12Deck) {
  const payload: DeckCodePayload = { v: 1, n: deck.name, m: deck.masterId, c: deck.cardIds, r: deck.moraleIds }
  const bytes = new TextEncoder().encode(JSON.stringify(payload))
  let binary = ''
  bytes.forEach(byte => { binary += String.fromCharCode(byte) })
  return `L12D1.${btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')}`
}

export function decodeDeckCode(code: string): SavedL12Deck {
  const trimmed = code.trim()
  if (!trimmed.startsWith('L12D1.')) throw new Error('不是有效的十二军团牌库码')
  const base64 = trimmed.slice(6).replace(/-/g, '+').replace(/_/g, '/')
  const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, '=')
  const binary = atob(padded)
  const bytes = Uint8Array.from(binary, char => char.charCodeAt(0))
  const payload = JSON.parse(new TextDecoder().decode(bytes)) as DeckCodePayload
  if (payload.v !== 1 || !payload.n || !payload.m || !Array.isArray(payload.c) || !Array.isArray(payload.r)) throw new Error('牌库码内容不完整')
  return { name: payload.n.slice(0, 24), masterId: payload.m, cardIds: payload.c, moraleIds: payload.r, updatedAt: new Date().toISOString() }
}

async function loadImage(url?: string) {
  if (!url) return null
  try {
    const response = await fetch(url)
    if (!response.ok) return null
    return await createImageBitmap(await response.blob())
  } catch { return null }
}

function roundedRect(context: CanvasRenderingContext2D, x: number, y: number, width: number, height: number, radius: number) {
  context.beginPath()
  context.roundRect(x, y, width, height, radius)
  context.fill()
}

export async function createDeckImageBlob(deck: SavedL12Deck, catalog: DeckCard[]) {
  const byId = new Map(catalog.map(card => [card.id, card]))
  const groups = [...deck.cardIds.reduce((map, id) => map.set(id, (map.get(id) || 0) + 1), new Map<string, number>())]
    .sort(([left], [right]) => {
      const a = byId.get(left); const b = byId.get(right)
      return (a?.cost ?? 99) - (b?.cost ?? 99) || (a?.number || left).localeCompare(b?.number || right)
    })
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
  context.fillStyle = '#a7b0b4'; context.font = '700 18px Microsoft YaHei'; context.fillText(`主宰 ${master?.nameZh || deck.masterId}  ·  主牌 ${deck.cardIds.length}  ·  士气 ${deck.moraleIds.length}`, 410, 145)
  context.fillStyle = '#e1bf6d'; context.fillRect(410, 168, 1464, 3)

  const [masterBitmap, ...bitmaps] = await Promise.all([loadImage(master?.imageUrl), ...groups.map(([id]) => loadImage(byId.get(id)?.imageUrl))])
  context.fillStyle = '#10171b'; roundedRect(context, 74, 104, 254, 356, 4)
  if (masterBitmap) context.drawImage(masterBitmap, 74, 104, 254, 356)
  else { context.fillStyle = '#263139'; context.fillRect(74, 104, 254, 356) }
  context.fillStyle = '#f4f0e6'; context.font = '900 25px Microsoft YaHei'; context.textAlign = 'center'; context.fillText(master?.nameZh || '主宰', 201, 505)
  const factionName = ({ tianting: '天廷', gaotianyuan: '高天原', asgard: '阿斯加德', taiyangcheng: '太阳城', olympus: '奥林匹斯', otherworld: '彼界' } as Record<string, string>)[master?.faction || ''] || master?.faction || ''
  context.fillStyle = '#55c4cb'; context.font = '900 16px Microsoft YaHei'; context.fillText(factionName, 201, 534)
  context.fillStyle = '#10171b'; roundedRect(context, 74, 580, 254, 170, 4)
  context.fillStyle = '#e1bf6d'; context.font = '900 54px Microsoft YaHei'; context.fillText(String(deck.moraleIds.length), 201, 660)
  context.fillStyle = '#f4f0e6'; context.font = '900 20px Microsoft YaHei'; context.fillText('士气', 201, 704)
  context.fillStyle = '#89959a'; context.font = '700 14px Microsoft YaHei'; context.fillText(`${groups.length} 种卡牌`, 201, 730)
  context.textAlign = 'left'

  const areaX = 410; const areaY = 198; const areaWidth = 1464; const areaHeight = 784
  const gapX = 13
  const rowPitch = areaHeight / rows
  const cardWidth = Math.min(162, (areaWidth - gapX * (columns - 1)) / columns, (rowPitch - 42) / 1.4)
  const cardHeight = cardWidth * 1.4
  groups.forEach(([id, count], index) => {
    const card = byId.get(id)
    const col = index % columns
    const row = Math.floor(index / columns)
    const x = areaX + col * (cardWidth + gapX)
    const y = areaY + row * rowPitch
    context.fillStyle = '#0b1116'; roundedRect(context, x, y, cardWidth, cardHeight, 3)
    const bitmap = bitmaps[index]
    if (bitmap) context.drawImage(bitmap, x, y, cardWidth, cardHeight)
    else { context.fillStyle = '#263139'; context.fillRect(x, y, cardWidth, cardHeight); context.fillStyle = '#77858c'; context.font = '900 13px Microsoft YaHei'; context.fillText('暂无卡图', x + 25, y + cardHeight / 2) }
    context.fillStyle = '#f1ede3'; context.font = '900 13px Microsoft YaHei'; context.fillText((card?.nameZh || id).slice(0, 10), x + 2, y + cardHeight + 18)
    context.fillStyle = '#7f8b90'; context.font = '700 10px Microsoft YaHei'; context.fillText(card?.number || id, x + 2, y + cardHeight + 33)
    context.fillStyle = '#e1bf6d'
    const badgeX = x + cardWidth - 16
    context.beginPath(); context.arc(badgeX, y + 16, 16, 0, Math.PI * 2); context.fill()
    context.fillStyle = '#0b0e10'; context.font = '900 15px Microsoft YaHei'; context.textAlign = 'center'; context.fillText(`×${count}`, badgeX, y + 21); context.textAlign = 'left'
  })
  context.fillStyle = '#7f8b90'; context.font = '700 14px Microsoft YaHei'; context.fillText('由十二军团网页平台生成 · 可使用牌库码导入', 74, canvas.height - 70)
  context.fillStyle = '#e1bf6d'; context.font = '900 19px Microsoft YaHei'; context.fillText('LEGION12', 74, canvas.height - 42)
  return await new Promise<Blob>((resolve, reject) => canvas.toBlob(value => value ? resolve(value) : reject(new Error('牌库图生成失败')), 'image/png'))
}

export async function downloadDeckImage(deck: SavedL12Deck, catalog: DeckCard[], existingBlob?: Blob) {
  const blob = existingBlob || await createDeckImageBlob(deck, catalog)
  const anchor = document.createElement('a')
  anchor.href = URL.createObjectURL(blob)
  anchor.download = `${deck.name.replace(/[\\/:*?"<>|]/g, '_')}-牌库图.png`
  anchor.click()
  setTimeout(() => URL.revokeObjectURL(anchor.href), 1000)
}
