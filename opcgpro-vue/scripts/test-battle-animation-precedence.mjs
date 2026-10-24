import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
import { parse, compileStyle } from '@vue/compiler-sfc'

const read = relative => fs.readFileSync(new URL(`../${relative}`, import.meta.url), 'utf8')
const component = relative => parse(read(relative), { filename: relative }).descriptor
const opening = component('src/l12/game/PromptOverlay.vue')
const movement = component('src/l12/game/ZoneMovementPresentationLayer.vue')
const state = component('src/l12/game/CardStateTransitionLayer.vue')
function runtime(source, name, bindings) {
  const ast = ts.createSourceFile('motion.ts', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const node = ast.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name)
  assert.ok(node, `Missing actual motion function: ${name}`)
  const body = ts.transpileModule(node.getText(ast).replace(/^export\s+/, ''), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.None },
  }).outputText
  return new Function(...Object.keys(bindings), `${body}\nreturn ${name};`)(...Object.values(bindings))
}
let assertions = 0
const equal = (actual, expected, label) => { assert.deepEqual(actual, expected, label); assertions++ }
for (const systemReduced of [true, false]) for (const mode of ['standard', 'fast', 'off']) {
  const scale = mode === 'off' ? 0 : mode === 'fast' ? .55 : 1
  const duration = (ms, min = 0) => Math.max(min, Math.round(ms * scale))
  let delay = 0; let intervalStarts = 0; let settled = 0
  const start = runtime(opening.scriptSetup.content, 'startInitiativeDice', {
    diceTimer: null, diceSettleTimer: null, diceSettled: { value: false },
    diceAnimationReadyToSettle: { value: false }, openingIntroSkipped: { value: false },
    animatedRolls: { value: [1, 1] }, clearInterval() {}, clearTimeout() {},
    setInterval() { intervalStarts++; return 1 }, setTimeout(_fn, ms) { delay = ms; return 2 },
    window: { matchMedia: () => ({ matches: systemReduced }) },
    l12AnimationDuration: duration, l12BattleMotionDisabled: () => mode === 'off',
    syncMasterTargets() {}, settleInitiativeDice() { settled++ },
  })
  start()
  equal({ delay, intervalStarts, settled }, mode === 'off'
    ? { delay: 0, intervalStarts: 0, settled: 1 }
    : { delay: Math.round(1250 * scale), intervalStarts: 1, settled: 0 },
  `Opening must honor ${mode} even when systemReduced=${systemReduced}`)
  const simplified = runtime(movement.scriptSetup.content, 'simplifiedEntryMotion', {
    document: { documentElement: { dataset: { l12Animation: mode } } },
    window: { matchMedia: () => ({ matches: systemReduced }) },
    l12BattleMotionDisabled: () => mode === 'off',
  })
  equal(simplified(), mode === 'off', `Entry/disaster must honor ${mode}; systemReduced=${systemReduced}`)
  const stateDuration = runtime(state.scriptSetup.content, 'duration', {
    window: { matchMedia: () => ({ matches: systemReduced }) }, props: {},
    l12AnimationDuration: duration, l12BattleMotionDisabled: () => mode === 'off',
  })
  equal(stateDuration(), mode === 'off' ? 80 : Math.round(380 * scale),
    `Ready/rest must honor ${mode}; systemReduced=${systemReduced}`)
}
for (const mode of ['standard', 'fast', 'off']) {
  const disabled = runtime(read('src/l12/audioPreferences.ts'), 'l12BattleMotionDisabled', { audioPreferences: { animation: mode } })
  equal(disabled(), mode === 'off', `Single battle preference authority: ${mode}`)
}
for (const name of ['PromptOverlay', 'ZoneMovementPresentationLayer', 'CardStateTransitionLayer',
  'AttackConnectionPresentationLayer', 'ActionPresentationLayer', 'PlayerMat', 'OsirisVictorySequence']) {
  const file = `src/l12/game/${name}.vue`; const source = read(file); const sfc = component(file)
  assert.doesNotMatch(source, /prefers-reduced-motion/, `${name} must not override the in-game battle setting`); assertions++
  for (const block of sfc.styles) {
    const compiled = compileStyle({ source: block.content, filename: file, id: 'data-v-motion-precedence', scoped: true })
    equal(compiled.errors, [], `${name} scoped styles must compile`)
  }
  if (['PromptOverlay', 'AttackConnectionPresentationLayer', 'ActionPresentationLayer', 'PlayerMat', 'OsirisVictorySequence'].includes(name)) {
    assert.ok(source.includes('data-l12-animation="off"'), `${name} must retain explicit off presentation`); assertions++
  }
}
console.log(`Battle animation precedence passed: ${assertions} checks, standard/fast/off × system reduce on/off; opening, entry/disaster, ready/rest and shared CSS.`)
