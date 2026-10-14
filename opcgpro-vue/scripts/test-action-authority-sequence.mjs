import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import vm from 'node:vm'

// Execute the actual event watcher. The mapper/queue clock are controlled test
// boundaries; browser timing and rendered motion are checked separately.
const source = readFileSync(new URL('../src/l12/game/ActionPresentationLayer.vue', import.meta.url), 'utf8')
const start = source.indexOf('watch(() => props.events.map(')
const end = source.indexOf('watch(() => props.paused', start)
assert(start >= 0 && end > start, 'Actual action event watcher must exist')
const eventWatcher = source.slice(start, end)

function event(sequence, kind = 'turn-start', text = '相同内容') {
  return { sequence, kind, text }
}
function harness() {
  const context = vm.createContext({})
  vm.runInContext(`
    const props = { events: [], paused: false }
    const active = { value: null }
    const queue = []
    const emitted = []
    let lastSequence = 0
    let initialized = false
    let eventCallback
    function actionPresentationFromEvent(event) { return { ...event } }
    function showNext() {
      if (active.value || props.paused || !queue.length) return
      active.value = queue.shift()
      emitted.push(active.value.sequence)
    }
    function watch(source, callback, options) {
      eventCallback = callback
      if (options?.immediate) callback()
    }
    ${eventWatcher}
    globalThis.testHarness = {
      feed(events) { props.events = events; eventCallback() },
      finish() {
        while (active.value || queue.length) { active.value = null; showNext() }
        return emitted
      }
    }
  `, context, { timeout: 1000 })
  return context.testHarness
}
const cases = [
  { name: 'same-sequence-retransmission', packets: [[event(1)], [event(1)]], expected: [1] },
  { name: 'two-authorities-same-content-one-batch', packets: [[event(2), event(1)]], expected: [1, 2] },
  { name: 'two-authorities-same-content-during-active', packets: [[event(1)], [event(1), event(2)]], expected: [1, 2] },
  { name: 'duplicate-sequence-conflicting-payload', packets: [[event(1), event(1, 'turn-end', '不同内容')]], expected: [1] },
  { name: 'two-authorities-distinct-content', packets: [[event(2, 'turn-end', '不同内容'), event(1)]], expected: [1, 2] },
]
const failures = []
for (const scenario of cases) {
  const subject = harness()
  for (const packet of scenario.packets) subject.feed(packet)
  const observed = Array.from(subject.finish())
  try { assert.deepEqual(observed, scenario.expected, scenario.name) }
  catch (error) { failures.push(`${scenario.name}: ${error.message}`) }
}
if (failures.length) throw new Error(failures.join('\n'))
console.log('Actual action watcher sequence regression passed: 5/5; sequence identity, not content deduplication.')
