import assert from 'node:assert/strict'
import fs from 'node:fs'

const source = fs.readFileSync(new URL('../src/l12/L12DeckEditor.vue', import.meta.url), 'utf8')
const handler = source.match(/function setMobilePane\(next: 'pool' \| 'deck' \| 'insights'\) \{([\s\S]*?)\n\}/)?.[1]
assert.ok(handler, 'Test the actual editor handler, not a second navigation implementation')
const run = new Function('next', 'workspace', 'mobilePane', 'setWorkspace', handler)
let checks = 0
for (const current of ['gallery', 'stats', 'hand', 'content']) {
  for (const next of ['pool', 'deck', 'insights']) {
    const workspace = { value: current }, mobilePane = { value: 'pool' }, calls = []
    run(next, workspace, mobilePane, value => { calls.push(value); workspace.value = value; mobilePane.value = value === 'gallery' ? 'pool' : 'insights' })
    const expected = next === 'pool' ? 'gallery' : next === 'deck' ? current : ['stats', 'hand'].includes(current) ? current : 'stats'
    assert.equal(workspace.value, expected, `${current} -> ${next} selects the correct workspace`)
    assert.equal(mobilePane.value, next, `${current} -> ${next} selects the correct pane`)
    if (next === 'insights' && !['stats', 'hand'].includes(current)) assert.deepEqual(calls, ['stats'], 'Use the shared workspace setter for entry normalization')
    checks++
  }
}
console.log(`Deck editor workspace transitions passed: ${checks} actual-handler cases; public content cannot retain the insights entry, selected statistics/hand are preserved.`)
