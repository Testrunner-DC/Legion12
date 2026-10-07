import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
const read = name => fs.readFileSync(new URL('../src/l12/' + name, import.meta.url), 'utf8')
const compile = text => ts.transpileModule(text, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
const url = text => 'data:text/javascript;base64,' + Buffer.from(text).toString('base64')
const binaryUrl = url(compile(read('deckCodeCodec.ts')))
const codecUrl = url(compile(read('deckCacheCodec.ts')).replace(/from ['"]\.\/deckCodeCodec['"]/, `from '${binaryUrl}'`))
const storageUrl = url(compile(read('deckCacheStorage.ts'))
  .replace(/from ['"]\.\/deckCodeCodec['"]/, `from '${binaryUrl}'`)
  .replace(/from ['"]\.\/deckCacheCodec['"]/, `from '${codecUrl}'`))
const { readDeckCache, requireDeckCache, commitDeckCache, interpretDeckSelection } = await import(storageUrl)
const { crc32 } = await import(binaryUrl)
class MemoryStorage {
  rows = new Map()
  writes = 0
  denyRead = false
  denyWrite = false
  getItem(key) { if (this.denyRead) throw Error('permission'); return this.rows.get(key) ?? null }
  setItem(key, value) { if (this.denyWrite) throw Error('QuotaExceededError'); this.writes++; this.rows.set(key, value) }
}
const owner = 'account:a', key = 'l12-custom-decks-v1:a'
const deck = { id: 'stable-a', revision: 2, name: ' 有空白的名字 ', updatedAt: '原时间',
  masterId: ' s01-01M1 ', cardIds: ['S01-0002', 'S01-0001', 'S01-0002'],
  moraleIds: ['s02-01C1', ' S01-01C1 '], specialIds: ['非ASCII✨', ''], benchIds: ['B', 'A', 'B'],
  publicationId: null, publicationVersion: null,
  alternateArtSelections: { A: ' art ' }, alternateArtCopies: { A: ['', 'art', ''] } }
let checks = 0
function check(label, action) { try { action(); checks++ } catch (error) { throw Error(label, { cause: error }) } }
function fixture() {
  const storage = new MemoryStorage()
  const raw = JSON.stringify({ [deck.name]: deck })
  storage.rows.set(key, raw)
  storage.rows.set('selected', '原名')
  storage.rows.set('draft', '原草稿')
  return { storage, raw, snapshot: requireDeckCache(readDeckCache(storage, key, owner)) }
}
check('Missing and unavailable are different states', () => {
  const storage = new MemoryStorage()
  assert.equal(readDeckCache(storage, key, owner).status, 'missing')
  storage.denyRead = true
  const blocked = readDeckCache(storage, key, owner)
  assert.equal(blocked.status, 'unavailable')
  assert.equal(blocked.decks, null)
  assert.throws(() => requireDeckCache(blocked), /无法读取/)
  assert.equal(storage.writes, 0)
})
check('Pure legacy reads have no side effects or normalization', () => {
  const { storage, snapshot, raw } = fixture()
  assert.equal(snapshot.status, 'legacy')
  assert.deepEqual(snapshot.decks, { [deck.name]: deck })
  assert.equal(storage.getItem(key), raw)
  assert.equal(storage.writes, 0)
})
check('Whole-map migration atomically includes reference interpretation', () => {
  const { storage, snapshot } = fixture()
  const aliases = { selected: { raw: '原名', resolved: deck.id } }
  const migrated = commitDeckCache(storage, key, snapshot, snapshot.decks, 'generation-1', aliases)
  assert.equal(migrated.status, 'ready')
  assert.equal(migrated.generation, 'generation-1')
  assert.deepEqual(migrated.decks, snapshot.decks)
  assert.equal(storage.writes, 1)
  assert.equal(storage.getItem('selected'), '原名')
  assert.equal(storage.getItem('draft'), '原草稿')
  assert.equal(interpretDeckSelection(migrated, 'selected', '原名'), deck.id)
  assert.equal(interpretDeckSelection(migrated, 'selected', 'explicit-new-id'), 'explicit-new-id')
  assert.equal(requireDeckCache(readDeckCache(storage, key, owner)).status, 'ready')
  assert.equal(storage.writes, 1, 'Reading again does not repeat migration')
})
for (const ownerName of ['account:a', 'account:b', 'guest']) {
  check('Account/guest envelope has exact ownership: ' + ownerName, () => {
    const storage = new MemoryStorage()
    const snapshot = requireDeckCache(readDeckCache(storage, ownerName, ownerName))
    commitDeckCache(storage, ownerName, snapshot, { [deck.name]: deck }, 'g1')
    assert.equal(readDeckCache(storage, ownerName, ownerName).status, 'ready')
    assert.equal(readDeckCache(storage, ownerName, 'different-owner').status, 'unavailable')
  })
}
for (const value of ['{broken', '[]', 'null', JSON.stringify({ bad: { ...deck, unknown: true } }), JSON.stringify({ good: deck, bad: { ...deck, revision: -1 } })]) {
  check('One invalid legacy entry blocks the entire migration', () => {
    const storage = new MemoryStorage(); storage.rows.set(key, value)
    const snapshot = readDeckCache(storage, key, owner)
    assert.equal(snapshot.status, 'unavailable'); assert.equal(snapshot.raw, value)
    assert.equal(snapshot.decks, null)
    assert.throws(() => requireDeckCache(snapshot))
    assert.equal(storage.getItem(key), value); assert.equal(storage.writes, 0)
  })
}
check('Decks named schema and format remain valid legacy entries', () => {
  const storage = new MemoryStorage()
  storage.rows.set(key, JSON.stringify({ schema: { ...deck, name: 'schema' }, format: { ...deck, name: 'format' } }))
  assert.equal(readDeckCache(storage, key, owner).status, 'legacy')
})
for (const mutate of [
  value => { value.schema = 2 }, value => { value.format = 'future-cache' },
  value => { value.owner = 'account:b' }, value => { value.extra = true },
  value => { value.checksum = 'broken' }, value => { delete value.entries },
  value => { value.entries[deck.name].code = 'L12D2-obsolete' },
]) {
  check('Unknown and damaged new records never become legacy or empty', () => {
    const { storage, snapshot } = fixture()
    commitDeckCache(storage, key, snapshot, snapshot.decks, 'g1')
    const value = JSON.parse(storage.getItem(key)); mutate(value)
    const raw = JSON.stringify(value); storage.rows.set(key, raw)
    const bad = readDeckCache(storage, key, owner)
    assert.equal(bad.status, 'unavailable'); assert.equal(bad.decks, null)
    assert.throws(() => requireDeckCache(bad)); assert.equal(storage.getItem(key), raw)
  })
}
check('Unknown codec version with a valid outer checksum is rejected', () => {
  const { storage, snapshot } = fixture()
  commitDeckCache(storage, key, snapshot, snapshot.decks, 'g1')
  const value = JSON.parse(storage.getItem(key)); value.entries[deck.name].version = 2
  const { checksum: ignored, ...body } = value
  value.checksum = crc32(new TextEncoder().encode(JSON.stringify(body))).toString(16).padStart(8, '0')
  const raw = JSON.stringify(value); storage.rows.set(key, raw)
  assert.equal(readDeckCache(storage, key, owner).status, 'unavailable')
  assert.equal(storage.getItem(key), raw)
})
check('Quota failure preserves raw, selection and draft', () => {
  const { storage, snapshot, raw } = fixture(); storage.denyWrite = true
  assert.throws(() => commitDeckCache(storage, key, snapshot, snapshot.decks, 'g1', { selected: { raw: '原名', resolved: deck.id } }), /写入失败/)
  assert.equal(storage.getItem(key), raw); assert.equal(storage.getItem('selected'), '原名')
  assert.equal(storage.getItem('draft'), '原草稿'); assert.equal(storage.writes, 0)
})
check('Mutation/input codec failure leaves the old map byte-for-byte', () => {
  const { storage, snapshot, raw } = fixture()
  assert.throws(() => commitDeckCache(storage, key, snapshot, { ...snapshot.decks, bad: { ...deck, future: true } }, 'g1'))
  assert.equal(storage.getItem(key), raw); assert.equal(storage.writes, 0)
})
check('Stale raw compare rejects a newer tab', () => {
  const { storage, snapshot } = fixture()
  const newer = JSON.stringify({ newer: { ...deck, revision: 3 } }); storage.rows.set(key, newer)
  assert.throws(() => commitDeckCache(storage, key, snapshot, snapshot.decks, 'g1'), /其他操作/)
  assert.equal(storage.getItem(key), newer); assert.equal(storage.writes, 0)
})
for (const failAt of [1, 2]) {
  check('Context guard fails before any persistence ' + failAt, () => {
    const { storage, snapshot, raw } = fixture(); let guards = 0
    assert.throws(() => commitDeckCache(storage, key, snapshot, snapshot.decks, 'g1', {}, () => {
      if (++guards === failAt) throw Error('account epoch changed')
    }), /epoch/)
    assert.equal(storage.getItem(key), raw); assert.equal(storage.writes, 0)
  })
}
check('Six selection aliases are bounded and deletion can remain unresolved', () => {
  const { storage, snapshot, raw } = fixture()
  const seven = Object.fromEntries(Array.from({ length: 7 }, (_, i) => ['scope' + i, { raw: 'a', resolved: 'b' }]))
  assert.throws(() => commitDeckCache(storage, key, snapshot, snapshot.decks, 'g1', seven))
  assert.equal(storage.getItem(key), raw)
  const result = commitDeckCache(storage, key, snapshot, {}, 'g1', { selected: { raw: '原名', resolved: 'unresolved:原名' } })
  assert.equal(interpretDeckSelection(result, 'selected', '原名'), 'unresolved:原名')
})
console.log(`Deck cache storage passed: ${checks} state/ownership/atomic-migration/failure checks; no legacy decoder, duplicate payload, draft or selection-key write.`)
