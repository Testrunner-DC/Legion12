import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
// Keep the legacy format roundtrip suite independent of browser/IDB ports.
// New production draft persistence is covered by test-deck-transactional-consumers.
const source = readFileSync(new URL('../src/l12/site/deckEditorDraft.ts', import.meta.url), 'utf8')
const pure = source.slice(0, source.indexOf('/** An opaque per-tab-session key'))
  .replace(/^import .*$/gm, '')
const code = ts.transpileModule(pure, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText
const { clearDeckEditorDraft, draftOwner, draftStorageKey, readDeckEditorDraft, writeDeckEditorDraft } = await import('data:text/javascript;base64,' + Buffer.from(code).toString('base64'))

function storage() {
  const rows = new Map()
  return {
    getItem: key => rows.has(key) ? rows.get(key) : null,
    setItem: (key, value) => rows.set(key, value),
    removeItem: key => rows.delete(key),
  }
}
const local = storage()
const guest = storage()
const alpha = draftOwner('account-a')
const beta = draftOwner('account-b')
const guestOwner = draftOwner(null)
const incomplete = {
  name: '未完成构筑', masterId: '', cardIds: ['S01-0001'], moraleIds: [], specialIds: [],
  benchIds: [], updatedAt: '2026-10-02T00:00:00Z',
}
assert.notEqual(draftStorageKey(alpha), draftStorageKey(beta))
assert.notEqual(draftStorageKey(alpha), draftStorageKey(guestOwner))
assert.equal(readDeckEditorDraft(local, alpha), null)
writeDeckEditorDraft(local, alpha, incomplete, null, '2026-10-02T00:00:00Z')
assert.deepEqual(readDeckEditorDraft(local, alpha)?.deck.cardIds, ['S01-0001'])
writeDeckEditorDraft(local, alpha, { ...incomplete, id: 'stable-a', revision: 4 }, '旧名称',
  '2026-10-02T00:00:00Z', 'stable-a', 4)
assert.equal(readDeckEditorDraft(local, alpha)?.schema, 2)
assert.equal(readDeckEditorDraft(local, alpha)?.baseDeckId, 'stable-a')
assert.equal(readDeckEditorDraft(local, alpha)?.baseDeckRevision, 4)
assert.equal(readDeckEditorDraft(local, beta), null)
assert.equal(readDeckEditorDraft(guest, guestOwner), null)
writeDeckEditorDraft(guest, guestOwner, incomplete, null)
assert.equal(readDeckEditorDraft(guest, guestOwner)?.deck.name, incomplete.name)
clearDeckEditorDraft(local, alpha)
assert.equal(readDeckEditorDraft(local, alpha), null)
assert.equal(readDeckEditorDraft(guest, guestOwner)?.deck.name, incomplete.name)

assert.throws(() => writeDeckEditorDraft({ setItem() { throw new Error('quota') } }, alpha, incomplete, null), /quota/)
assert.throws(() => writeDeckEditorDraft(local, alpha, { ...incomplete, cardIds: Array(201).fill('S01-0001') }, null), /暂存/)
local.setItem(draftStorageKey(alpha), '{broken')
assert.throws(() => readDeckEditorDraft(local, alpha), /损坏/)
local.setItem(draftStorageKey(alpha), JSON.stringify({ schema: 1, owner: beta, savedAt: '', baseDeckName: null, deck: incomplete }))
assert.throws(() => readDeckEditorDraft(local, alpha), /归属/)
local.setItem(draftStorageKey(alpha), JSON.stringify({ schema: 1, owner: alpha, savedAt: '', baseDeckName: '旧名称', deck: incomplete }))
assert.equal(readDeckEditorDraft(local, alpha)?.baseDeckName, '旧名称', '旧草稿必须可读，但不能猜测稳定 ID')
local.setItem(draftStorageKey(alpha), JSON.stringify({ schema: 2, owner: alpha, savedAt: '', baseDeckName: '旧名称',
  baseDeckId: 'stable-a', baseDeckRevision: -1, deck: incomplete }))
assert.throws(() => readDeckEditorDraft(local, alpha), /归属/)
{
  const s = storage(), owner = draftOwner('v2-owner')
  writeDeckEditorDraft(s, owner, incomplete, null)
  const legacy = s.getItem(draftStorageKey(owner))
  assert.deepEqual(readDeckEditorDraft(s, owner, 2)?.deck, incomplete)
  const chosen = { ...incomplete, representativeCardIds: ['S01-0001'] }
  writeDeckEditorDraft(s, owner, chosen, null, '2026-10-02T00:00:00Z', null, null, 2)
  assert.equal(s.getItem(draftStorageKey(owner)), legacy)
  assert.deepEqual(readDeckEditorDraft(s, owner, 2)?.deck.representativeCardIds, ['S01-0001'])
  assert.throws(() => writeDeckEditorDraft(s, owner, { ...chosen, representativeCardIds: ['NOT-MAIN'] }, null), /暂存/)
  clearDeckEditorDraft(s, owner, 2)
  assert.equal(readDeckEditorDraft(s, owner, 2), null)
  assert.equal(s.getItem(draftStorageKey(owner)), legacy)
  assert.deepEqual(readDeckEditorDraft(s, owner)?.deck, incomplete)
  s.setItem(draftStorageKey(owner, 2), '{bad')
  assert.throws(() => readDeckEditorDraft(s, owner, 2), /损坏/)
  assert.equal(s.getItem(draftStorageKey(owner)), legacy)
}
console.log('Deck editor local draft passed: incomplete save, account/session isolation, corruption, quota and bounds.')
