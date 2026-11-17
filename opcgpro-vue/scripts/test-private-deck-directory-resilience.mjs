import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')
const decks = read('src/l12/decks.ts')
const page = read('src/l12/site/DeckLibraryPage.vue')

// Keep this as a small structural contract: the performance fix must remain
// visible to reviewers and cannot silently regress to no-store on every mount.
assert.match(decks, /privateDeckSummaryCache\s*=\s*new Map/)
assert.match(decks, /PRIVATE_DECK_SUMMARY_TTL_MS\s*=\s*15_000/)
assert.match(decks, /PRIVATE_DECK_SUMMARY_STALE_MS\s*=\s*5 \* 60_000/)
assert.match(decks, /entry\.request\) return await entry\.request/)
assert.match(decks, /cacheable = !query\.exactName && !query\.publicationId/)
assert.match(page, /privateDirectoryRequests\s*=\s*new Map/)
assert.match(page, /if \(!privateSummaryPage\.value\) privateLoadState\.value = 'loading'/)
assert.match(page, /if \(privateSummaryPage\.value\) \{[\s\S]*?privateLoadState\.value = 'available'/)
assert.match(page, /if \(!summaryPage\.value\) publicLoadState\.value = 'loading'/)
assert.match(page, /if \(summaryPage\.value\) \{[\s\S]*?publicLoadState\.value = 'available'/)

console.log('PASS: private deck directory single-flight and stale-while-revalidate contract')
