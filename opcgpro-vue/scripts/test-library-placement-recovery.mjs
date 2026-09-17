import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { computed, effectScope, nextTick, reactive, ref, watch } from 'vue'
import ts from 'typescript'

// Execute the real component watcher and submit function, with reactive Vue state.
const source = readFileSync(new URL('../src/l12/game/PromptOverlay.vue', import.meta.url), 'utf8')
const start = source.indexOf('watch(() => `${prompt.value?.promptId')
const end = source.indexOf('watch(minimized,', start)
assert(start >= 0 && end > start)
const submitStart = source.indexOf('function confirmAllPlacement(')
const submitEnd = source.indexOf('const isInfoConfirm', submitStart)
const code = ts.transpileModule(source.slice(start, end) + source.slice(submitStart, submitEnd)
  + '\nreturn confirmAllPlacement', { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
let passed = 0
for (const mode of ['all-top-bottom', 'all-bottom']) {
  const scope = effectScope()
  const props = reactive({ game: { phase: 'Main' } })
  const prompt = ref({ promptId: 'restored-order', playerIndex: 0, validChoices: ['a', 'b'], data: { placementMode: mode } })
  const me = computed(() => ({ mulliganDone: true }))
  const states = Array.from({ length: 8 }, () => ref([]))
  const [selected, hoveredChoice, minimized, placementTop, placementBottom, placementSelected, draggedChoice, placementOrder] = states
  const sent = []
  const submit = scope.run(() => new Function('watch', 'prompt', 'props', 'me', 'selected', 'hoveredChoice', 'minimized',
    'placementTop', 'placementBottom', 'placementSelected', 'draggedChoice', 'placementOrder', 'sendAction', 'withPromptBinding', code)(
    watch, prompt, props, me, selected, hoveredChoice, minimized, placementTop, placementBottom, placementSelected,
    draggedChoice, placementOrder, command => sent.push(command), (p, command) => ({ promptId: p.promptId, ...command })))
  assert.deepEqual(placementOrder.value, ['a', 'b'], 'Already-open prompt must initialize on mount/reconnect')
  submit('bottom')
  assert.deepEqual(sent.pop(), { promptId: 'restored-order', topCardInstanceIds: [], bottomCardInstanceIds: ['a', 'b'] })
  placementOrder.value = ['b', 'a']
  await nextTick()
  assert.deepEqual(placementOrder.value, ['b', 'a'], 'Unchanged prompt must preserve the chosen order')
  prompt.value = { ...prompt.value, promptId: 'next-order', validChoices: ['c'] }
  await nextTick()
  assert.deepEqual(placementOrder.value, ['c'], 'Next step must reset the previous ordering')
  submit(mode === 'all-bottom' ? 'bottom' : 'top')
  assert.equal(sent.pop().promptId, 'next-order')
  scope.stop()
  passed += 5
}
console.log(`Library placement mount/reconnect/submit behavior: ${passed}/${passed} passed`)
