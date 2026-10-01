import assert from 'node:assert/strict'
import {
  clearDeckEditorDraft, draftOwner, draftStorageKey, readDeckEditorDraft, writeDeckEditorDraft,
} from '../src/l12/site/deckEditorDraft.ts'

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
console.log('Deck editor local draft passed: incomplete save, account/session isolation, corruption, quota and bounds.')
