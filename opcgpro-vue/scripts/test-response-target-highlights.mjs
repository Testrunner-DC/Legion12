import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { computed, ref, reactive, watch, nextTick, effectScope } from 'vue'

// Execute the actual component's reactive selection/reset/presentation code, not a duplicate implementation.
const source = readFileSync(new URL('../src/l12/game/PromptOverlay.vue', import.meta.url), 'utf8')
const start = source.indexOf('watch(() => `${prompt.value?.promptId')
const end = source.indexOf('\nfunction sendAction', start)
assert.ok(start > 0 && end > start)
const code = ts.transpile(source.slice(start, end), { target: ts.ScriptTarget.ES2022 })
const prompt = ref(null), selected = ref([]), minimized = ref(false)
const props = reactive({ game: { phase: 'Main', players: [{ field: [[
  { instanceId: 'same-a', name: '同名军团', hidden: false },
  { instanceId: 'same-b', name: '同名军团', hidden: false },
  { instanceId: 'secret', name: '不可泄露', hidden: true },
]], hand: [{ instanceId: 'hand', hidden: false }] }] } })
const events = [], unmount = []
const scope = effectScope()
scope.run(() => new Function('computed', 'watch', 'onBeforeUnmount', 'prompt', 'props', 'me', 'selected', 'minimized', 'visible', 'emit',
  'hoveredChoice', 'placementTop', 'placementBottom', 'placementSelected', 'draggedChoice', 'placementOrder', code)(
  computed, watch, fn => unmount.push(fn), prompt, props, ref({ mulliganDone: true }), selected, minimized, computed(() => !!prompt.value),
  (event, value) => events.push([event, value]), ...Array.from({ length: 6 }, () => ref(null))))
const highlights = () => events.filter(([event]) => event === 'responseTargetsChange').at(-1)?.[1]
const next = async () => { await nextTick(); await nextTick() }
const targetPrompt = (id = 'p1') => ({ promptId: id, kind: 'response-target', validChoices: ['stack-a', 'stack-b'], data: {
  'stack-a:responseTargetIds': JSON.stringify(['same-a', 'secret', 'hand']),
  'stack-b:responseTargetIds': JSON.stringify(['same-b']),
} })
prompt.value = targetPrompt(); await next()
assert.deepEqual(highlights(), [])
minimized.value = true; await next()
assert.deepEqual(highlights(), ['same-a', 'same-b'])
selected.value = ['stack-b']; await next()
assert.deepEqual(highlights(), ['same-b'], 'same-name cards must be distinguished by instance')
selected.value = []; await next()
assert.deepEqual(highlights(), ['same-a', 'same-b'], 'deselect restores union')
minimized.value = false; await next()
assert.deepEqual(highlights(), [])
minimized.value = true; selected.value = ['stack-b']; await next()
prompt.value = targetPrompt('p2'); await next()
assert.equal(minimized.value, false); assert.deepEqual(selected.value, []); assert.deepEqual(highlights(), [])
prompt.value = { promptId: 'fee', kind: 'effect-decision', data: { responseTargetIds: '["same-a"]' } }; await next()
minimized.value = true; await next(); assert.deepEqual(highlights(), ['same-a'])
prompt.value.data.responseTargetIds = '{broken'; await next(); assert.deepEqual(highlights(), [])
prompt.value = null; await next(); assert.deepEqual(highlights(), [])
unmount.forEach(fn => fn()); scope.stop(); assert.deepEqual(highlights(), [])

const mat = readFileSync(new URL('../src/l12/game/PlayerMat.vue', import.meta.url), 'utf8')
const board = readFileSync(new URL('../src/l12/game/GameBoard.vue', import.meta.url), 'utf8')
assert.equal((mat.match(/responseTargetIds/g) ?? []).length, 2, 'highlight prop is declaration and visual binding only, never permission')
assert.match(mat, /'response-target': !player.field\[row\]\[slot\]\?\.hidden && responseTargetIds\?\.includes/)
assert.equal((board.match(/:response-target-ids="promptMinimized \? responseTargetIds : \[\]"/g) ?? []).length, 2)
assert.match(source, /const context = prompt.value\?\.data\?\.responseContext\?\.trim\(\)/)
console.log('Response-target highlight behavior: same-name instances, union, selection, deselection, expand, prompt switch, bound payment, malformed metadata, hidden/off-field exclusion, close and unmount passed')
