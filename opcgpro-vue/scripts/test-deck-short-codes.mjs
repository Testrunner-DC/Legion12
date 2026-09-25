import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'

const shareSource = fs.readFileSync(new URL('../src/l12/site/deckShare.ts', import.meta.url), 'utf8')
const codecSource = shareSource.slice(shareSource.indexOf('const DECK_CODE_ALPHABET'), shareSource.indexOf('async function loadImage'))
const javascript = ts.transpileModule(codecSource, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const { encodeDeckCode, decodeDeckCode } = await import(`data:text/javascript;base64,${Buffer.from(javascript).toString('base64')}`)

const deck = {
  name: '奥林匹斯雷霆组', masterId: 'S02-05M2',
  cardIds: [
    ...Array(3).fill('S02-0501'), ...Array(3).fill('S02-0502'), ...Array(3).fill('S02-0503'),
    ...Array(3).fill('S02-0504'), ...Array(3).fill('S02-0505'), ...Array(3).fill('S02-0506'),
    ...Array(3).fill('S02-0507'), ...Array(3).fill('S02-0511'), ...Array(3).fill('S02-0517'),
    ...Array(3).fill('S01-0002'), ...Array(3).fill('S02-0520'), ...Array(3).fill('S02-0522'),
    ...Array(2).fill('S02-0521'), 'PROMO-X', 'PROMO-X',
  ],
  moraleIds: Array(8).fill('S02-05C1'), specialIds: ['S02-05S4'], updatedAt: new Date().toISOString(),
}

function legacyCode(value) {
  const payload = { v: 1, n: value.name, m: value.masterId, c: value.cardIds, r: value.moraleIds, s: value.specialIds }
  return `L12D1.${Buffer.from(JSON.stringify(payload)).toString('base64url')}`
}
function counts(values) {
  return [...values.reduce((map, value) => map.set(value, (map.get(value) ?? 0) + 1), new Map()).entries()]
    .sort(([left], [right]) => left.localeCompare(right))
}

const code = encodeDeckCode(deck)
assert.match(code, /^L12D2-[23456789ABCDEFGHJKMNPQRSTVWXYZ-]+$/)
assert.equal(encodeDeckCode({ ...deck, cardIds: [...deck.cardIds].reverse(), moraleIds: [...deck.moraleIds].reverse() }), code)
assert.ok(code.length < legacyCode(deck).length * 0.55, `新牌库码未显著缩短：${code.length}/${legacyCode(deck).length}`)
const decoded = decodeDeckCode(code.toLowerCase())
assert.equal(decoded.name, deck.name)
assert.equal(decoded.masterId, deck.masterId)
assert.deepEqual(counts(decoded.cardIds), counts(deck.cardIds))
assert.deepEqual(counts(decoded.moraleIds), counts(deck.moraleIds))
assert.deepEqual(counts(decoded.specialIds), counts(deck.specialIds))

assert.throws(() => decodeDeckCode(legacyCode(deck)), /不是有效/)

const last = code.at(-1)
const replacement = last === '2' ? '3' : '2'
assert.throws(() => decodeDeckCode(code.slice(0, -1) + replacement), /校验失败/)
assert.throws(() => decodeDeckCode(code.replace(/-[^-]+$/, '-00000')), /易混淆|不支持/)

const entrySource = fs.readFileSync(new URL('../src/l12/site/publicDeckEntry.ts', import.meta.url), 'utf8')
const librarySource = fs.readFileSync(new URL('../src/l12/site/DeckLibraryPage.vue', import.meta.url), 'utf8')
const detailSource = fs.readFileSync(new URL('../src/l12/site/PublicDeckDetailPage.vue', import.meta.url), 'utf8')
assert.ok(entrySource.includes("published.publicCode?.trim() || ''"))
assert.ok(librarySource.includes('publicDeckRouteReference(entry)'))
assert.ok(detailSource.includes('publicDeckRouteReference(entry.value)'))
assert.ok(detailSource.includes("router.replace({ name: 'public-deck-detail', params: { deckId: canonicalReference }"))

console.log(`短编码回归通过：L12D2 ${code.length} 字符，旧 L12D1 ${legacyCode(deck).length} 字符已按产品裁定失效，大小写/校验通过`)
