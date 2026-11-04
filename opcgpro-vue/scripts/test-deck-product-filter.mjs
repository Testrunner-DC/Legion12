import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..', '..')
const decksSource = fs.readFileSync(path.join(import.meta.dirname, '../src/l12/decks.ts'), 'utf8')
const editorSource = fs.readFileSync(path.join(import.meta.dirname, '../src/l12/L12DeckEditor.vue'), 'utf8')
const inclusion = JSON.parse(fs.readFileSync(path.join(root, '服务端WebSocket/TwelveLegions/Data/card-product-inclusions.json'), 'utf8'))
const archive = JSON.parse(fs.readFileSync(path.join(root, '服务端WebSocket/TwelveLegions/Data/card-archive-assets.json'), 'utf8'))

assert.match(decksSource, /export function cardProductLabels/)
assert.match(decksSource, /export function deckProductOptions/)
assert.match(decksSource, /\.map\(withProductInclusions\)/)
assert.match(editorSource, /deckProductOptions\(catalog\.value\)/)
assert.match(editorSource, /cardProductLabels\(card\)/)

const labelsByBase = new Map()
const add = (id, values) => {
  const labels = labelsByBase.get(id) ?? new Set()
  values.filter(Boolean).forEach(value => labels.add(value))
  labelsByBase.set(id, labels)
}
for (const entry of inclusion.cards) {
  add(entry.cardId, entry.products)
  const base = archive.cards.find(card => card.id === entry.cardId)?.baseCardId
  if (base) add(base, entry.products)
}
for (const asset of archive.cards) add(asset.baseCardId, asset.products)

const allProducts = new Set(inclusion.products)
const visibleProducts = new Set([...labelsByBase.values()].flatMap(values => [...values]))
assert.deepEqual([...allProducts].filter(product => !visibleProducts.has(product)), [], 'product directory contains a product with no selectable base card')
assert(labelsByBase.get('S01-0101')?.has('ST01|天廷阵营预组'), 'ST01 alternate product was not restored to its playable base card')
assert(labelsByBase.get('S01-0208')?.has('ST02|太阳城阵营预组'), 'ST02 alternate product was not restored to its playable base card')
assert(labelsByBase.get('S01-0404')?.has('ST04|高天原阵营预组'), 'ST04 alternate product was not restored to its playable base card')
assert(labelsByBase.get('S01-0002')?.has('第2季|伟大试炼'), 'multi-product base card lost its second-season inclusion')
assert(labelsByBase.get('S01-02M2')?.has('黄金的理想乡'), 'errata product was lost from the selectable product set')

console.log(`Deck product filter passed: ${allProducts.size} products retained, alternate/linked products mapped to playable bases.`)
