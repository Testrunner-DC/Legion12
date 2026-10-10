import assert from 'node:assert/strict'
import fs from 'node:fs'
import { createHash } from 'node:crypto'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'

const source = path => fs.readFileSync(new URL(path, import.meta.url), 'utf8')
const transpile = text => {
  const result = ts.transpileModule(text, {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
    reportDiagnostics: true,
  })
  assert.deepEqual(result.diagnostics, [], 'Production codec must transpile without diagnostics')
  return result.outputText
}
const asModule = javascript => `data:text/javascript;base64,${Buffer.from(javascript).toString('base64')}`
const shareSource = source('../src/l12/deckCodeCodec.ts')
const cacheSource = source('../src/l12/deckCacheCodec.ts')
const shareUrl = asModule(transpile(shareSource))
const { crc32 } = await import(shareUrl)
const cacheJavaScript = transpile(cacheSource)
assert.match(cacheJavaScript, /from ['"]\.\/deckCodeCodec['"]/)
const { encodeDeckCache, decodeDeckCache, DECK_CACHE_LIMITS } = await import(asModule(
  cacheJavaScript.replace(/from ['"]\.\/deckCodeCodec['"]/, `from '${shareUrl}'`),
))

let checks = 0
function check(label, run) {
  try { run(); checks++ } catch (error) { throw new Error(label, { cause: error }) }
}
// Type-check only the two codecs against the actual SavedL12Deck interface in memory.
// No Vue consumer, build output, generated source or shared cache is touched.
check('Pure codec TypeScript contract', () => {
  const deckTypes = source('../src/l12/decks.ts')
  const start = deckTypes.indexOf('export interface SavedL12Deck {')
  const end = deckTypes.indexOf('export interface OfficialL12PresetDeck', start)
  assert.ok(start >= 0 && end > start)
  const virtual = fileURLToPath(new URL('../src/l12/decks.codec-contract.ts', import.meta.url))
  const options = { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext,
    moduleResolution: ts.ModuleResolutionKind.Bundler, strict: true, noUncheckedIndexedAccess: true,
    noEmit: true, skipLibCheck: true, types: [], lib: ['lib.es2022.d.ts', 'lib.dom.d.ts'] }
  const host = ts.createCompilerHost(options)
  const getSourceFile = host.getSourceFile.bind(host)
  host.getSourceFile = (file, language, ...args) => file === virtual
    ? ts.createSourceFile(file, deckTypes.slice(start, end), language) : getSourceFile(file, language, ...args)
  host.resolveModuleNames = (names, containing) => names.map(name => name === './decks'
    ? { resolvedFileName: virtual, extension: ts.Extension.Ts }
    : ts.resolveModuleName(name, containing, options, host).resolvedModule)
  const program = ts.createProgram(['../src/l12/deckCodeCodec.ts', '../src/l12/deckCacheCodec.ts']
    .map(path => fileURLToPath(new URL(path, import.meta.url))), options, host)
  const diagnostics = ts.getPreEmitDiagnostics(program)
  assert.deepEqual(diagnostics.map(item => ts.flattenDiagnosticMessageText(item.messageText, '\n')), [])
})

const empty = { name: '', updatedAt: '', masterId: '', cardIds: [], moraleIds: [], specialIds: [] }
function roundTrip(deck) {
  const encoded = encodeDeckCache(deck)
  const decoded = decodeDeckCache(JSON.parse(JSON.stringify(encoded)))
  assert.deepEqual(decoded, deck, 'All fields, original strings, order, null and undefined must survive')
  assert.deepEqual(encodeDeckCache(decoded), encoded, 'Storage serialization must be deterministic')
  assert.notEqual(decoded, deck)
  assert.notEqual(decoded.cardIds, deck.cardIds)
  return encoded
}
function expectBad(value) { assert.throws(() => decodeDeckCache(value), /牌库缓存|牌库码/) }
function frame(body) {
  const checksum = crc32(Uint8Array.from(body))
  const bytes = Buffer.from([...body, checksum >>> 24, (checksum >>> 16) & 255, (checksum >>> 8) & 255, checksum & 255])
  return { version: 1, code: 'L12C1-' + bytes.toString('base64url') }
}
function rewrite(encoded, mutate) {
  const body = [...Buffer.from(encoded.code.slice(6), 'base64url').subarray(0, -4)]
  mutate(body)
  return frame(body)
}

const orderedIds = ['S01-0001', 's01-0001', ' S01-0001 ', '卡牌✨', 'PROMO-X', '', 'S02-05C1B', 'S01-0001']
const full = {
  ...empty, id: 'stable deck / 中文', revision: 7, name: '长中文名称'.repeat(12),
  updatedAt: '2026-10-06T03:04:05.006+08:00', publicationId: 'publication/9', publicationVersion: 3,
  masterId: ' 主宰✨ ', cardIds: [...orderedIds, 'PROMO-X', 'PROMO-X'],
  moraleIds: ['S02-05C1', 'S01-05C1', 'S02-05C1'], specialIds: ['s01-special', '', ' 卡牌✨ '],
  benchIds: ['S01-0001', 'S01-0002', 'S01-0001', 'S01-0001'],
  alternateArtSelections: { 'S01-0001': 'art-alpha', '卡牌✨': '' },
  alternateArtCopies: { 'S01-0001': ['art-alpha', '', 'art-beta'], '卡牌✨': [] },
}

check('Empty unfinished deck', () => roundTrip(empty))
check('Every field and every array order', () => roundTrip(full))
check('Independent account IDs', () => {
  const alpha = roundTrip({ ...full, id: 'account-a-deck' })
  const beta = roundTrip({ ...full, id: 'account-b-deck' })
  assert.notEqual(alpha.code, beta.code)
  assert.equal(decodeDeckCache(alpha).id, 'account-a-deck')
})
check('Same construction with different names', () => {
  const first = roundTrip(full)
  const second = roundTrip({ ...full, name: '另一名字' })
  assert.notEqual(first.code, second.code)
})
check('Repeated and interleaved card order', () => {
  for (const cards of [Array(50).fill('S01-0001'), Array.from({ length: 50 }, (_, i) => i % 2 ? 'S01-0002' : 'S01-0001')]) {
    roundTrip({ ...empty, cardIds: cards, moraleIds: [...cards].reverse(), specialIds: cards, benchIds: cards })
  }
})
check('All ID representations in all sections', () => {
  for (const id of [...orderedIds, 'S00-0000', 'S99-ZZZZ', 'X'.repeat(128), '\t ID \r\n']) {
    roundTrip({ ...empty, masterId: id, cardIds: [id], moraleIds: [id], specialIds: [id], benchIds: [id] })
  }
})
check('Absent optional fields stay absent', () => {
  const decoded = decodeDeckCache(roundTrip(empty))
  for (const key of ['id', 'revision', 'publicationId', 'publicationVersion', 'alternateArtSelections', 'alternateArtCopies', 'benchIds']) {
    assert.equal(Object.hasOwn(decoded, key), false)
  }
})
check('Explicit undefined remains present', () => {
  roundTrip({ ...empty, id: undefined, revision: undefined, publicationId: undefined, publicationVersion: undefined,
    alternateArtSelections: undefined, alternateArtCopies: undefined, benchIds: undefined })
})
check('Legal null and empty optional metadata', () => {
  roundTrip({ ...empty, id: '', publicationId: null, publicationVersion: null, alternateArtSelections: {}, alternateArtCopies: {}, benchIds: [] })
})
check('Names and timestamps survive escaped Unicode verbatim', () => {
  roundTrip({ ...empty, name: '中'.repeat(1000) + '\ud800', updatedAt: ' original timestamp \t' })
})
check('Decoded arrays and art do not alias input or another decode', () => {
  const encoded = roundTrip(full)
  const decoded = decodeDeckCache(encoded)
  decoded.cardIds.reverse()
  decoded.alternateArtCopies['S01-0001'][0] = 'changed'
  assert.deepEqual(decodeDeckCache(encoded), full)
})
check('512 cards per section', () => {
  roundTrip({ ...empty, cardIds: Array(512).fill('S01-0001'), moraleIds: Array(512).fill('S01-0001'),
    specialIds: Array(512).fill('S01-0001'), benchIds: Array(200).fill('S01-0001') })
  roundTrip({ ...empty, cardIds: Array.from({ length: 512 }, (_, i) => i % 2 ? 'A' : 'B') })
})
check('Large valid metadata does not hit argument-count limits', () => roundTrip({ ...empty, name: '中'.repeat(60_000) }))

for (const key of ['cardIds', 'moraleIds', 'specialIds']) {
  check(`${key} count boundary`, () => assert.throws(() => encodeDeckCache({ ...empty, [key]: Array(513).fill('A') }), /限制/))
}
check('Bench count boundary', () => assert.throws(() => encodeDeckCache({ ...empty, benchIds: Array(201).fill('A') }), /限制/))
for (const deck of [
  { ...empty, masterId: '中'.repeat(43) }, { ...empty, cardIds: ['\ud800'] }, { ...empty, cardIds: Array(1) },
  { ...empty, cardIds: [1] }, { ...empty, benchIds: null }, { ...empty, specialIds: undefined },
  { ...empty, id: null }, { ...empty, revision: 0 }, { ...empty, revision: NaN },
  { ...empty, publicationVersion: 1.5 }, { ...empty, alternateArtSelections: null },
  { ...empty, alternateArtCopies: { A: Array(1) } }, { ...empty, alternateArtCopies: { A: Array(51).fill('') } },
  { ...empty, alternateArtSelections: { A: 'x'.repeat(257) } }, { ...empty, futureField: true },
  { ...empty, name: undefined }, { ...empty, updatedAt: null },
]) {
  check('Invalid input strictly refuses truncation or coercion', () => assert.throws(() => encodeDeckCache(deck)))
}
check('Maximum art entries, copies and art ID length', () => {
  const art = Object.fromEntries(Array.from({ length: 128 }, (_, i) => ['C' + i, 'x'.repeat(256)]))
  roundTrip({ ...empty, alternateArtSelections: art, alternateArtCopies: { A: Array(50).fill('x'.repeat(256)) } })
  assert.throws(() => encodeDeckCache({ ...empty, alternateArtSelections: { ...art, extra: '' } }), /限制/)
})
check('Invalid extended arrays cannot lose own fields', () => {
  const cards = ['A']; cards.extra = true
  assert.throws(() => encodeDeckCache({ ...empty, cardIds: cards }))
  const copies = ['']; copies.extra = true
  assert.throws(() => encodeDeckCache({ ...empty, alternateArtCopies: { A: copies } }))
})
check('Hidden art fields cannot disappear during serialization', () => {
  const art = {}; Object.defineProperty(art, 'A', { value: 'art-1', enumerable: false })
  assert.throws(() => encodeDeckCache({ ...empty, alternateArtSelections: art }))
})
check('Metadata byte limit', () => assert.throws(() => encodeDeckCache({ ...empty, name: '中'.repeat(65_536) }), /容量/))
check('Combined record byte limit', () => {
  const cards = Array.from({ length: 512 }, (_, i) => 'X'.repeat(125) + String(i).padStart(3, '0'))
  assert.throws(() => encodeDeckCache({ ...empty, name: 'N'.repeat(70_000), cardIds: cards, moraleIds: cards, specialIds: cards }), /容量/)
})
check('Record does not tolerate unknown properties', () => {
  assert.throws(() => encodeDeckCache({ ...empty, [Symbol('future')]: true }))
})

const encoded = roundTrip(full)
const bytes = Buffer.from(encoded.code.slice(6), 'base64url')
for (let index = 0; index < bytes.length; index++) {
  check(`Checksum detects corrupt byte ${index}`, () => {
    const damaged = Buffer.from(bytes)
    damaged[index] ^= 1
    expectBad({ version: 1, code: 'L12C1-' + damaged.toString('base64url') })
  })
}
for (const value of [null, {}, { ...encoded, version: 0 }, { ...encoded, version: 2 }, { ...encoded, code: encoded.code.replace('L12C1-', 'L12D2-') },
  { ...encoded, code: 'L12C1-!' }, { ...encoded, code: 'L12C1-A' }, { ...encoded, code: 'L12C1-' },
  { ...encoded, code: encoded.code + '=' }, { ...encoded, futureField: true }, { ...encoded, code: 'L12C1-' + 'A'.repeat(400_000) },
]) {
  check('Unknown, malformed or oversized envelope', () => expectBad(value))
}
const tiny = roundTrip(empty)
check('Unknown binary version with a valid checksum', () => expectBad(rewrite(tiny, body => { body[0] = 2 })))
check('Unknown presence bits', () => expectBad(rewrite(tiny, body => { body.splice(1, 1, 128, 2) })))
check('Undefined bits must refer to present fields', () => expectBad(rewrite(tiny, body => { body[2] = 1 })))
check('Missing required metadata', () => expectBad(rewrite(tiny, body => { body[1] = 0 })))
check('Invalid UTF8 metadata', () => expectBad(rewrite(tiny, body => { body[4] = 255 })))
check('Unknown master representation', () => expectBad(rewrite(tiny, body => { body[4 + body[3]] = 2 })))
check('Bench marker out of range', () => expectBad(rewrite(tiny, body => { body[body.length - 1] = 3 })))
check('Trailing body must not be ignored', () => expectBad(rewrite(tiny, body => { body.push(0) })))
check('Truncated bodies with valid checksums', () => {
  for (const body of [[], [1], [1, 12], [1, 12, 0], [1, 12, 0, 127]]) expectBad(frame(body))
})
check('Zero copy run with valid checksum', () => {
  const one = roundTrip({ ...empty, cardIds: ['A'] })
  expectBad(rewrite(one, body => { body[body.length - 4] = 0 }))
})
check('Base64 noncanonical trailing bits', () => {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_'
  let candidate
  for (let length = 0; length < 3; length++) {
    const item = encodeDeckCache({ ...empty, name: 'a'.repeat(length) })
    if (item.code.slice(6).length % 4) { candidate = item; break }
  }
  assert.ok(candidate)
  const last = alphabet.indexOf(candidate.code.at(-1))
  expectBad({ ...candidate, code: candidate.code.slice(0, -1) + alphabet[last | 1] })
})

const presets = ['s1', 's2'].flatMap(season => JSON.parse(
  source(`../public/data/l12/preset-decks.${season}.json`).replace(/^\uFEFF/, ''),
))
function measure(deck) {
  const representation = roundTrip(deck)
  return { name: deck.name, originalJsonBytes: Buffer.byteLength(JSON.stringify(deck)),
    encodedEnvelopeBytes: Buffer.byteLength(JSON.stringify(representation)),
    rawCodecBytes: Buffer.from(representation.code.slice(6), 'base64url').length }
}
const additionalMeasurements = [measure(empty), measure(full), measure({ ...empty, name: '交错重复',
  cardIds: Array.from({ length: 512 }, (_, i) => i % 2 ? 'S01-0001' : 'S01-0002'), benchIds: Array(200).fill('PROMO-X') })]
const measurements = presets.map(preset => {
  const deck = { ...preset, specialIds: preset.specialIds ?? [], updatedAt: '2026-10-06T00:00:00.000Z' }
  let representation
  check(`Official ${deck.name} exact order`, () => { representation = roundTrip(deck) })
  return { name: deck.name, originalJsonBytes: Buffer.byteLength(JSON.stringify(deck)),
    encodedEnvelopeBytes: Buffer.byteLength(JSON.stringify(representation)),
    rawCodecBytes: Buffer.from(representation.code.slice(6), 'base64url').length }
})
check('No browser storage or UI dependencies', () => {
  assert.doesNotMatch(cacheSource, /\b(?:localStorage|sessionStorage|document|window|navigator)\b|from ['"][^'"]*(?:platform|qrcode|cardAssets)/)
})
console.log(JSON.stringify({ passed: checks, limits: DECK_CACHE_LIMITS,
  sourceSha256: Object.fromEntries([['deckCodeCodec.ts', shareSource], ['deckCacheCodec.ts', cacheSource]]
    .map(([file, text]) => [file, createHash('sha256').update(text).digest('hex')])),
  measurements, additionalMeasurements, scope: 'Pure codecs only; inactive cache representation; excludes storage migration, backups, database and memory savings.' }, null, 2))
