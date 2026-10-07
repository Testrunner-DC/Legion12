import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'

const shareSource = fs.readFileSync(new URL('../src/l12/site/deckShare.ts', import.meta.url), 'utf8')
assert.match(shareSource, /export \{ encodeDeckCode, decodeDeckCode \} from '\.\.\/deckCodeCodec'/, 'Share exports must point to the extracted production codec')
const codecSource = fs.readFileSync(new URL('../src/l12/deckCodeCodec.ts', import.meta.url), 'utf8')
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
const GOLDEN = 'L12D2-WXY3D-ZYPC3-JXSD7-VBEXD-GZFAV-2HCPV-GJGRJ-SP8Y5-WK59X-PQBFF-DNSQZ-HEZ23-K2CGS-7XZYJ-NT22Z-SCTNE-J8XK5-6TR7C-XC4PZ-DK9BG-62S9J-PN86K-RBTH5-RBGJP-RVD98-GXC49-MPFWG-SP8X3-6ZB54-MW2EG-7TCG9-W9XF7-2WANS-2EDK9-5HQJ3-7EKAK-QH5SN-HRD43-7'
assert.equal(code, GOLDEN, 'Extracting the codec must preserve the exact pre-change L12D2 bytes')
assert.notEqual(encodeDeckCode({ ...deck, name: '另一名称' }), GOLDEN, 'Share codes retain their existing name-bearing protocol')
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
const entryModule = await import(`data:text/javascript;base64,${Buffer.from(ts.transpileModule(entrySource, {
  compilerOptions: { module: ts.ModuleKind.ESNext },
}).outputText).toString('base64')}`)
assert.equal(entryModule.publicDeckRouteReference({ id: 'official-0', ownerId: 'official', official: true }), 'official-0')
assert.equal(entryModule.publicDeckRouteReference({ id: 'legacy-uuid', ownerId: 'player' }), '')
assert.equal(entryModule.publicDeckRouteReference({ id: 'official-0', ownerId: 'player' }), '')
assert.ok(librarySource.includes('publicDeckRouteReference(entry)'))
assert.ok(detailSource.includes('publicDeckRouteReference(entry.value)'))
function canonicalDetailReplacement(source) {
  const script = source.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)?.[1] ?? source
  const file = ts.createSourceFile('canonical-detail.ts', script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  if (file.parseDiagnostics.length) return false
  const declarations = [], calls = []
  const visit = node => {
    if (ts.isVariableDeclaration(node)) declarations.push(node)
    if (ts.isCallExpression(node)) calls.push(node)
    ts.forEachChild(node, visit)
  }
  visit(file)
  return calls.some(call => {
    if (call.expression.getText(file) !== 'router.replace' || call.arguments.length !== 1) return false
    let target = call.arguments[0]
    if (ts.isIdentifier(target)) {
      const bindings = declarations.filter(node => ts.isIdentifier(node.name) && node.name.text === target.text)
      if (bindings.length !== 1) return false
      target = bindings[0].initializer
    }
    if (!target || !ts.isObjectLiteralExpression(target)) return false
    const properties = new Map(target.properties.filter(ts.isPropertyAssignment)
      .map(node => [node.name.getText(file), node.initializer]))
    const params = properties.get('params')
    return properties.get('name')?.text === 'public-deck-detail'
      && properties.get('query')?.getText(file) === 'route.query'
      && properties.get('hash')?.getText(file) === 'route.hash'
      && params && ts.isObjectLiteralExpression(params)
      && params.properties.some(node => ts.isPropertyAssignment(node)
        && node.name.getText(file) === 'deckId' && node.initializer.getText(file) === 'canonicalReference')
  })
}
assert.ok(canonicalDetailReplacement(detailSource), 'Canonical detail replacement preserves route, reference, query and hash')
assert.ok(canonicalDetailReplacement("router.replace({ name: 'public-deck-detail', params: { deckId: canonicalReference }, query: route.query, hash: route.hash })"), 'The original inline form remains accepted')
for (const [label, source] of [
  ['wrong reference', detailSource.replace('params: { deckId: canonicalReference }', 'params: { deckId: oldReference }')],
  ['wrong route', detailSource.replace("const target = { name: 'public-deck-detail'", "const target = { name: 'another-page'")],
  ['missing query', detailSource.replace('query: route.query, hash: route.hash', 'query: {}, hash: route.hash')],
  ['missing hash', detailSource.replace('query: route.query, hash: route.hash', "query: route.query, hash: ''")],
  ['history push', detailSource.replace('await router.replace(target)', 'await router.push(target)')],
]) {
  assert.notEqual(source, detailSource, `Canonical mutation must be exercised: ${label}`)
  assert.equal(canonicalDetailReplacement(source), false, `Canonical contract rejects ${label}`)
}


assert.equal(decodeDeckCode('L12D2-' + code.slice(6).toLowerCase().replaceAll('-', ' - \n')).masterId, deck.masterId)
assert.equal(decodeDeckCode(encodeDeckCode({ ...deck, cardIds: Array(512).fill('PROMO-X') })).cardIds.length, 512)
assert.throws(() => decodeDeckCode(encodeDeckCode({ ...deck, cardIds: Array(513).fill('PROMO-X') })), /数量无效/)
assert.throws(() => decodeDeckCode(encodeDeckCode({ ...deck, cardIds: Array.from({ length: 257 }, (_, index) => 'P-' + index) })), /种类超出/)
assert.throws(() => decodeDeckCode(encodeDeckCode({ ...deck, masterId: 'X'.repeat(129) })), /文本超出/)
assert.throws(() => decodeDeckCode(encodeDeckCode({ ...deck, masterId: '' })), /内容不完整/)

console.log(`短编码回归通过：L12D2 ${code.length} 字符，旧 L12D1 ${legacyCode(deck).length} 字符已按产品裁定失效，大小写/校验通过`)
