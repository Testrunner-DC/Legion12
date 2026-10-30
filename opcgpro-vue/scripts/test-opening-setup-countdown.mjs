import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import ts from 'typescript'
import * as vue from 'vue'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'

const source = fs.readFileSync(new URL('../src/l12/game/SetupDecisionClock.vue', import.meta.url), 'utf8')
const script = source.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)[1]
const state = vue.reactive({ phase: 'Initiative', playerIndex: 1, roleLabel: '对手正在选择先后手',
  rankedClock: { receivedAtMs: 1000, operationLimitMs: 60000, paused: false, players: [
    { playerIndex: 0, acting: false, operationRemainingMs: 60000, connected: true },
    { playerIndex: 1, acting: true, operationRemainingMs: 43000, connected: true },
  ] }, showAllActing: false, playerLabels: { 0: '先攻玩家准备', 1: '后攻玩家准备' } })
const module = { exports: {} }
const code = ts.transpileModule(`${script}\nexport { clock, nowMs };`, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
vm.runInNewContext(code, { module, exports: module.exports, defineProps: () => state, Date: { now: () => 1000 },
  require: id => { assert.equal(id, 'vue'); return { ...vue, onMounted: () => {}, onBeforeUnmount: () => {} } } })
const model = module.exports
assert.ok(model.clock.value, 'Existing server-timed initiative must be visible for the waiting viewer too')
const entries = () => model.clock.value.entries
assert.equal(entries()[0].remaining, 43000)
model.nowMs.value = 18000
assert.equal(entries()[0].remaining, 26000)
state.rankedClock.paused = true
assert.equal(entries()[0].remaining, 43000, 'Paused clock follows authority snapshot without local ticking')
state.rankedClock.paused = false
state.phase = 'DisasterPreparation'; state.showAllActing = true
state.rankedClock.players[0].acting = true
assert.equal(entries().length, 2, 'Both simultaneous public confirmations expose independent public remaining time')
state.rankedClock.players[0].acting = false
assert.equal(entries().length, 1)
state.rankedClock.players[1].connected = false
assert.equal(entries()[0].remaining, 26000, 'Disconnection does not restart or pause preparation budget')
model.nowMs.value = 99000
assert.equal(entries()[0].remaining, 0, 'Zero never goes negative')
state.rankedClock.operationLimitMs = 0
assert.equal(model.clock.value, null, 'Untimed modes do not fabricate a deadline')
console.log('PASS authoritative initiative/disaster clock, elapsed/pause/disconnect/dual/untimed boundaries')

const overlay = fs.readFileSync(new URL('../src/l12/game/PromptOverlay.vue', import.meta.url), 'utf8')
const opening = overlay.slice(overlay.indexOf('<section v-else-if="isInitiative"'), overlay.indexOf('<section v-else-if="prompt"'))
assert.match(opening, /<SetupDecisionClock/)
const setupAst = ts.createSourceFile('overlay.ts', parse(overlay).descriptor.scriptSetup.content, ts.ScriptTarget.Latest, true)
const actor = setupAst.statements.filter(ts.isVariableStatement).flatMap(x => x.declarationList.declarations).find(x => x.name.getText(setupAst) === 'setupClockPlayerIndex')
assert.match(actor.initializer.getText(setupAst), /isInitiative/)
const role = setupAst.statements.find(x => ts.isFunctionDeclaration(x) && x.name?.text === 'setupRoleLabel')
const actorModule = { exports: {} }
const actorContext = {
  computed: vue.computed, isInitiative: vue.ref(true), diceSettled: vue.ref(false),
  prompt: vue.ref({ playerIndex: 1 }), waitingPrompt: vue.ref(null),
  l12State: vue.reactive({ rankedClock: { players: [{ playerIndex: 1, acting: true }] } }),
  props: vue.reactive({ game: { you: 1, firstPlayer: 0, players: [] } }),
}
vm.runInNewContext(ts.transpileModule(`const ${actor.getText(setupAst)};\n${role.getText(setupAst)}\nexport { setupClockPlayerIndex, setupRoleLabel };`,
  { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText,
  { ...actorContext, module: actorModule, exports: actorModule.exports })
assert.equal(actorModule.exports.setupClockPlayerIndex.value, 1)
assert.equal(actorModule.exports.setupRoleLabel(1), '先后手选择', 'Rolling must not claim a player is already choosing')
actorContext.diceSettled.value = true
assert.equal(actorModule.exports.setupRoleLabel(1), '你正在选择先后手')
actorContext.props.game.you = 0
actorContext.prompt.value = null
actorContext.waitingPrompt.value = { playerIndex: 1 }
assert.equal(actorModule.exports.setupClockPlayerIndex.value, 1, 'Waiting viewer follows the same authoritative actor')
assert.equal(actorModule.exports.setupRoleLabel(1), '对手正在选择先后手')
actorContext.waitingPrompt.value = null
assert.equal(actorModule.exports.setupClockPlayerIndex.value, 1, 'Public clock actor covers a delayed prompt projection')
for (const text of [source, opening]) assert.doesNotMatch(text, /emit\(['"]resolve|gameAction\(|sandboxAction\(/, 'Display code must not choose when local clock hits zero')
for (const file of ['SetupDecisionClock.vue', 'PromptOverlay.vue']) {
  const { descriptor, errors } = parse(fs.readFileSync(new URL(`../src/l12/game/${file}`, import.meta.url), 'utf8'))
  assert.equal(errors.length, 0)
  const compiled = compileScript(descriptor, { id: 'opening-clock' })
  assert.equal(compileTemplate({ source: descriptor.template.content, filename: file, id: 'opening-clock', compilerOptions: { bindingMetadata: compiled.bindings } }).errors.length, 0)
}
console.log('PASS real opening clock wiring and SFC compilation')

const helper = fs.readFileSync(new URL('../src/l12/game/openingDiceLayout.ts', import.meta.url), 'utf8')
const geometry = { exports: {} }
vm.runInNewContext(ts.transpileModule(helper, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText, { module: geometry, exports: geometry.exports })
for (const [width, height, viewport] of [[1440,900,1440],[1024,768,1024],[844,390,844],[844,390,390],[390,844,390],[320,568,320]]) {
  const g = geometry.exports.openingDiceLayout(width,height,viewport)
  assert.ok(g.offset - g.masterWidth / 2 >= g.diceRowWidth / 2 + g.gap - .01, `No card/die intersection ${width}x${height}`)
  assert.ok(g.offset + g.masterWidth / 2 <= width / 2 + .01, 'Masters remain inside the stage while rolling')
  assert.ok(g.masterWidth > 0 && g.diceFont > 0)
}
console.log('PASS rolling dice safety geometry at six logical/rotated sizes')
