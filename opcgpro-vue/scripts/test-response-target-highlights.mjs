import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { computed, ref, reactive, watch, nextTick, effectScope } from 'vue'

// Execute the actual component's reactive selection/reset/presentation code, not a duplicate implementation.
const source = readFileSync(new URL('../src/l12/game/PromptOverlay.vue', import.meta.url), 'utf8')
const start = source.indexOf('watch(() => JSON.stringify([')
const end = source.indexOf('\nfunction sendAction', start)
assert.ok(start > 0 && end > start)
const code = ts.transpile(source.slice(start, end), { target: ts.ScriptTarget.ES2022 })
const targetProjectionSource = readFileSync(new URL('../src/l12/game/battlefieldTargetPresentation.ts', import.meta.url), 'utf8')
const targetProjectionJavascript = ts.transpileModule(targetProjectionSource, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const { battlefieldTargetIds } = await import(`data:text/javascript;base64,${Buffer.from(targetProjectionJavascript).toString('base64')}`)
const prompt = ref(null), selected = ref([]), minimized = ref(false)
const props = reactive({ game: { phase: 'Main', players: [{ field: [[
  { instanceId: 'same-a', name: '同名军团', hidden: false },
  { instanceId: 'same-b', name: '同名军团', hidden: false },
  { instanceId: 'secret', name: '不可泄露', hidden: true },
]], hand: [{ instanceId: 'hand', hidden: false }] }] } })
const events = [], unmount = []
const scope = effectScope()
const activeSelected = computed(() => {
  const valid = new Set(prompt.value?.validChoices ?? [])
  return selected.value.filter((choice, index) => valid.has(choice) && selected.value.indexOf(choice) === index)
})
scope.run(() => new Function('computed', 'watch', 'onBeforeUnmount', 'prompt', 'props', 'me', 'selected', 'activeSelected', 'minimized', 'visible', 'emit', 'battlefieldTargetIds',
  'hoveredChoice', 'placementTop', 'placementBottom', 'placementSelected', 'draggedChoice', 'placementOrder', code)(
  computed, watch, fn => unmount.push(fn), prompt, props, ref({ mulliganDone: true }), selected, activeSelected, minimized, computed(() => !!prompt.value),
  (event, value) => events.push([event, value]), battlefieldTargetIds, ...Array.from({ length: 6 }, () => ref(null))))
const highlights = () => events.filter(([event]) => event === 'responseTargetsChange').at(-1)?.[1]
const next = async () => { await nextTick(); await nextTick() }
const targetPrompt = (id = 'p1') => ({ promptId: id, kind: 'response-target', validChoices: ['stack-a', 'stack-b'], data: {
  'stack-a:responseTargetIds': JSON.stringify(['same-a', 'secret', 'hand']),
  'stack-b:responseTargetIds': JSON.stringify(['same-b']),
} })
prompt.value = targetPrompt(); await next()
assert.deepEqual(highlights(), ['same-a', 'same-b', 'secret'], 'expanded response prompt keeps all declared battlefield targets highlighted')
minimized.value = true; await next()
assert.deepEqual(highlights(), ['same-a', 'same-b', 'secret'], 'minimizing does not change the authoritative targets')
selected.value = ['stack-b']; await next()
assert.deepEqual(highlights(), ['same-b'], 'same-name cards must be distinguished by instance')
selected.value = []; await next()
assert.deepEqual(highlights(), ['same-a', 'same-b', 'secret'], 'deselect restores the current response union')
minimized.value = false; await next()
assert.deepEqual(highlights(), ['same-a', 'same-b', 'secret'], 'restoring the prompt must not clear target highlights')
minimized.value = true; selected.value = ['stack-b']; await next()
prompt.value = targetPrompt('p2'); await next()
assert.equal(minimized.value, false); assert.deepEqual(selected.value, []); assert.deepEqual(highlights(), ['same-a', 'same-b', 'secret'])
prompt.value = { promptId: 'fee', kind: 'effect-decision', data: { responseTargetIds: '["same-a"]' } }; await next()
assert.deepEqual(highlights(), ['same-a'], 'follow-up response payment keeps the bound target while expanded')
minimized.value = true; await next(); assert.deepEqual(highlights(), ['same-a'])
props.game.players[0].field[0][0] = null; await next(); assert.deepEqual(highlights(), [], 'a target leaving the battlefield stops highlighting without replacement')
prompt.value.data.responseTargetIds = '{broken'; await next(); assert.deepEqual(highlights(), [])
prompt.value = { promptId: 'ordinary-target', kind: 'field-target', validChoices: ['same-a', 'same-b'], data: {} }; await next()
selected.value = ['same-b', 'hand', 'secret']; await next()
assert.deepEqual(highlights(), ['same-b'], 'ordinary selected targets only retain valid choices that are still on the battlefield')
prompt.value = null; await next(); assert.deepEqual(highlights(), [])
unmount.forEach(fn => fn()); scope.stop(); assert.deepEqual(highlights(), [])

const mat = readFileSync(new URL('../src/l12/game/PlayerMat.vue', import.meta.url), 'utf8')
const board = readFileSync(new URL('../src/l12/game/GameBoard.vue', import.meta.url), 'utf8')
assert.equal((mat.match(/responseTargetIds/g) ?? []).length, 2, 'highlight prop is declaration and visual binding only, never permission')
assert.match(mat, /return Boolean\(card\?\.instanceId && props\.responseTargetIds\?\.includes\(card\.instanceId\)\)/,
  'response highlight must require a real instance while allowing a covered battlefield target')
assert.equal((board.match(/:response-target-ids="responseTargetIds"/g) ?? []).length, 2,
  'both battlefield halves must keep response targets while the prompt is expanded or minimized')
assert.equal((board.match(/@graveyard="\(!hasBlockingPrompt \|\| inspectionLayerMinimized\) && \(graveyardPlayer = \$event\)"/g) ?? []).length, 2,
  'both public graveyards must remain inspectable while a blocking prompt is minimized')
assert.match(board, /:inspection-only="hasBlockingPrompt"/,
  'graveyard opened during a prompt must expose information without enabling grave abilities')
assert.match(board, /graveyardPlayer\.value !== null \|\| !promptMinimized\.value/,
  'graveyard card detail inspector must remain visible while the original prompt stays minimized')
const graveyard = readFileSync(new URL('../src/l12/game/GraveyardOverlay.vue', import.meta.url), 'utf8')
assert.match(graveyard, /if \(props\.inspectionOnly\) return/)
assert.match(source, /const context = prompt.value\?\.data\?\.responseContext\?\.trim\(\)/)
console.log('Response-target highlights and minimized-prompt public graveyard inspection passed')
