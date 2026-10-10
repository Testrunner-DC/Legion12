import assert from 'node:assert/strict'
import fs from 'node:fs'
import { privateEditorSavedPanelsContract, privateModeLegalityContract, privateLibraryActionsContract } from './private-deck-template-contract.mjs'
const read = name => fs.readFileSync(new URL(name, import.meta.url), 'utf8')
const editor = read('../src/l12/L12DeckEditor.vue'), selector = read('../src/l12/SavedDeckSelector.vue')
let checks = 0
const check = (value, expected, name) => { assert.equal(value, expected, name); checks++ }
check(privateEditorSavedPanelsContract(editor), true, 'actual editor panel bindings')
check(privateEditorSavedPanelsContract(editor.replace('saved-decks-panel grand-panel', 'grand-panel added-state saved-decks-panel')), true, 'class order and extra state are irrelevant')
check(privateEditorSavedPanelsContract(editor.replaceAll('deck.id ? deck.id === activeDeckId : deck.name === activeDeckName', '(deck.id ? deck.id === activeDeckId : deck.name === activeDeckName)')), true, 'parentheses preserve identity selection')
for (const [before, after] of [['chooseMobileSavedDeck(deck)', 'requestNewDeck()'], ['requestLoadDeck(deck)', 'requestDelete(deck)'],
  ['role="dialog" aria-modal="true" aria-labelledby="mobile-saved-decks-title"', 'role="region" aria-labelledby="mobile-saved-decks-title"'],
  ['savedDeckCountLabel(deck)', 'deck.cardIds.length'], ['deck in savedDirectoryDecks', 'deck in []']])
  check(privateEditorSavedPanelsContract(editor.replaceAll(before, after)), false, `editor rejects ${after}`)
check(privateModeLegalityContract(selector), true, 'actual authenticated/guest legality distinction and empty state')
check(privateModeLegalityContract(selector.replace('computed(() => authenticated.value ? serverRows.value : guestRows.value)', 'computed(() => (authenticated.value ? serverRows.value : guestRows.value))')), true, 'parenthesized branch is equivalent')
check(privateModeLegalityContract(selector.replace('rowStatus(row.deck, row.error)', "rowStatus(row.deck, 'not available')")), false, 'literal values are not whitespace-normalized')
check(privateModeLegalityContract(selector.replace('!rows.length && !directoryError', '!rows.length || !directoryError')), false, 'operator semantics remain protected')
for (const [before, after] of [['validateDeck(deck, props.catalog, props.restrictions)', "''"], ['props.restrictions)', '[])'],
  ['!rows.length && !directoryError', '!rows.length'], ['rowStatus(row.deck, row.error)', "'符合当前模式规则'"],
  ['!!row.error || disabled || confirming', 'disabled'], ['v-if="noUsableGuestDeck"', 'v-if="false"']])
  check(privateModeLegalityContract(selector.replaceAll(before, after)), false, `selector rejects ${after}`)
const library = read('../src/l12/site/DeckLibraryPage.vue')
check(privateLibraryActionsContract(library), true, 'real private row actions retain copy, code and confirmed deletion')
check(privateLibraryActionsContract(library.replace('copyMineCode(deck)', '(copyMineCode(deck))')), true, 'parenthesized code action is equivalent')
for (const [before, after] of [['@click="copyMineCode(deck)"', '@click="copyCode(deck)"'],
  ['@click="deleteMine(deck)"', '@click="deleteDeck(entry)"'], ['class="mine-grid"', 'hidden class="mine-grid"'],
  ['window.confirm(message)', 'false']])
  check(privateLibraryActionsContract(library.replaceAll(before, after)), false, `library rejects ${after}`)
console.log(`Private deck template contracts: ${checks}/${checks} positive/negative equivalence checks`)
