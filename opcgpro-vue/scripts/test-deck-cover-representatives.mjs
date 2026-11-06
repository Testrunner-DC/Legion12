import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')
const decks = read('src/l12/decks.ts')
const platform = read('src/l12/platform.ts')

assert.match(decks, /loadPrivateDeckSummaryPage[\s\S]{0,500}getRepresentativeDeckRequestHeaders\(\)/)
assert.match(platform, /deckLibraryApi[\s\S]{0,500}getRepresentativeDeckRequestHeaders\(\)/)
for (const file of [
  'src/l12/SavedDeckSelector.vue',
  'src/l12/site/BattleHubPage.vue',
  'src/l12/LobbyPage.vue',
  'src/l12/site/SandboxPage.vue',
]) {
  const source = read(file)
  assert.match(source, /cover-card-ids="[^"]*representativeCardIds/, `${file} must pass representative IDs to DeckCover`)
}
assert.match(read('src/l12/L12DeckEditor.vue'), /savedDeckCoverIds[\s\S]*representativeCardIds/)
assert.match(read('src/l12/site/DeckLibraryPage.vue'), /:cover-card-ids="deck\.coverCardIds"/)
assert.match(read('src/l12/site/PublicDeckPicker.vue'), /:cover-card-ids="deck\.coverCardIds"/)
console.log('Deck representative cover paths passed: private summaries negotiate v2 and ranked/friendly/sandbox/library selectors pass selected IDs.')
