import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const sourcePath = path.join(root, 'opcgpro-vue', 'public', 'data', 'l12', 'cards.lookup.json')
const outputPath = path.join(root, '服务端WebSocket', 'TwelveLegions', 'Data', 'cards.s2.json')

const factionMap = new Map([
  ['通用', 'universal'],
  ['天廷', 'tianting'],
  ['太阳城', 'taiyangcheng'],
  ['阿斯加德', 'asgard'],
  ['高天原', 'gaotianyuan'],
  ['奥林匹斯', 'olympus'],
  ['彼界', 'otherworld'],
  ['天灾', 'universal'],
])

const typeMap = new Map([
  ['军团', 'legion'],
  ['衍生卡', 'legion'],
  ['战术', 'tactic'],
  ['反击战术', 'tactic'],
  ['圣物', 'artifact'],
  ['主宰', 'master'],
  ['主城', 'divinity'],
  ['天灾终局', 'destruction'],
  ['士气卡', 'rune'],
  ['试炼卡', 'trial'],
  ['待识别', 'token'],
])

const source = JSON.parse(fs.readFileSync(sourcePath, 'utf8'))
const cards = source
  .filter(card => card.cardNo?.startsWith('S02-'))
  .sort((a, b) => a.cardNo.localeCompare(b.cardNo, 'zh-CN'))
  .map(card => {
    const cardType = typeMap.get(card.type) ?? 'token'
    const isMaster = cardType === 'master'
    return {
      id: card.cardNo,
      number: card.cardNo,
      nameZh: card.name,
      imageUrl: card.image || null,
      cardType,
      product: 'S02',
      faction: factionMap.get(card.faction) ?? 'universal',
      cost: isMaster ? null : card.cost ?? null,
      ...(card.deckLimit !== undefined ? { deckLimit: card.deckLimit } : {}),
      hp: card.health ?? null,
      troops: isMaster ? null : card.attack ?? null,
      disasterLevel: card.disasterLevel ?? null,
      ...(card.trialValue !== undefined ? { trialValue: card.trialValue } : {}),
      effect: card.effectText || null,
      ...(card.tags?.length ? { traits: card.tags } : {}),
      ...(card.subType ? { profession: card.subType }
        : card.cardNo === 'S02-01S1' ? { profession: null } : {}),
    }
  })

if (cards.length !== 115) throw new Error(`S2 card count mismatch: ${cards.length}`)
const ids = new Set(cards.map(card => card.id))
if (ids.size !== cards.length) throw new Error('S2 contains duplicate card numbers')
const invalidMasters = cards.filter(card => card.cardType === 'master'
  && (!Number.isFinite(card.hp) || card.cost !== null || card.troops !== null))
if (invalidMasters.length) {
  throw new Error(`Masters must have hp and must not expose cost/troops: ${invalidMasters.map(card => card.id).join(', ')}`)
}

fs.writeFileSync(outputPath, `${JSON.stringify(cards, null, 2)}\n`, 'utf8')
console.log(`Wrote ${cards.length} S2 cards to ${outputPath}`)
