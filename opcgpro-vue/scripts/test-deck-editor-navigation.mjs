import assert from 'node:assert/strict'
import { DEFAULT_DECK_EDITOR_RETURN, deckEditorQuery, deckEditorReturnTarget } from '../src/l12/site/deckEditorNavigation.ts'

assert.equal(deckEditorReturnTarget(undefined), DEFAULT_DECK_EDITOR_RETURN)
assert.equal(deckEditorReturnTarget('/'), DEFAULT_DECK_EDITOR_RETURN)
assert.equal(deckEditorReturnTarget('//example.invalid/decks'), DEFAULT_DECK_EDITOR_RETURN)
assert.equal(deckEditorReturnTarget('/battle'), DEFAULT_DECK_EDITOR_RETURN)
assert.equal(deckEditorReturnTarget('/decks-other'), DEFAULT_DECK_EDITOR_RETURN)

for (const source of [
  '/decks?tab=mine',
  '/decks?tab=plaza&q=%E5%A4%A9%E5%BB%B7',
  '/decks/PD000001?from=/decks%3Ftab%3Dplaza',
]) assert.equal(deckEditorReturnTarget(source), source)

assert.deepEqual(deckEditorQuery('/decks?tab=mine', '我的牌库', 'PD000001'), {
  deck: '我的牌库',
  published: 'PD000001',
  returnTo: '/decks?tab=mine',
})
assert.deepEqual(deckEditorQuery(undefined), { returnTo: DEFAULT_DECK_EDITOR_RETURN })

console.log('Deck editor navigation passed: mine, plaza/detail, refresh fallback and unsafe-source rejection.')
