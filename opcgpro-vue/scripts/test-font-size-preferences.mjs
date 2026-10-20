import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import vm from 'node:vm'
import ts from 'typescript'
import { reactive } from 'vue'

const root = path.resolve(import.meta.dirname, '..')
const source = fs.readFileSync(path.join(root, 'src/l12/audioPreferences.ts'), 'utf8')
const stripped = source.replace(/^import \{ reactive \} from 'vue'\r?\n/m, '')
  .replace(/^import \{ useAudioStore \} from '@\/store\/audioStore'\r?\n/m, '')
assert.notEqual(stripped, source, 'Execute the actual preferences module with only its two dependency ports supplied')
assert(!/^import /m.test(stripped), 'Unexpected import: update this test deliberately rather than bypass new dependencies')
const code = ts.transpileModule(stripped, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
function load(stored = {}) {
  let raw = JSON.stringify(stored)
  const properties = {}, dataset = {}, calls = []
  const context = { exports: {}, reactive, Number, Math, JSON,
    localStorage: { getItem: () => raw, setItem: (key, value) => { assert.equal(key, 'l12-audio-preferences-v1'); raw = value } },
    document: { documentElement: { dataset, style: { setProperty: (name, value) => { properties[name] = value } } } },
    useAudioStore: { getState: () => ({ setBgmVolume: value => calls.push(['bgm', value]), setSfxVolume: value => calls.push(['sfx', value]) }) },
  }
  vm.runInNewContext(code, context, { filename: 'actual-audioPreferences.cjs' })
  return { api: context.exports, properties, dataset, stored: () => JSON.parse(raw), calls }
}
const failures = [], results = []
function test(name, run) { try { run(); results.push(name) } catch (error) { failures.push(`${name}: ${error.message}`) } }
test('legacy cache defaults to standard with multiplier one', () => {
  const value = load()
  assert.equal(value.api.audioPreferences.fontSize, 'standard')
  assert.equal(value.properties['--l12-font-scale'], '1')
  assert.equal(value.dataset.l12FontSize, 'standard')
})
for (const [fontSize, multiplier] of [['standard', '1'], ['large', '1.12'], ['extra-large', '1.24']]) {
  test(`actual apply / local persistence / reload ${fontSize}`, () => {
    const value = load({ cardSize: 'medium', musicEnabled: false, animation: 'fast', mobileLayout: 'off' })
    value.api.applyAudioPreferences({ fontSize })
    assert.equal(value.api.audioPreferences.fontSize, fontSize)
    assert.equal(value.properties['--l12-font-scale'], multiplier)
    assert.equal(value.stored().fontSize, fontSize)
    const reloaded = load(value.stored())
    assert.equal(reloaded.api.audioPreferences.fontSize, fontSize)
    assert.equal(reloaded.properties['--l12-font-scale'], multiplier)
    assert.equal(reloaded.api.audioPreferences.cardSize, 'medium')
    assert.equal(reloaded.api.audioPreferences.musicEnabled, false)
    assert.equal(reloaded.api.audioPreferences.animation, 'fast')
    assert.equal(reloaded.api.audioPreferences.mobileLayout, 'off')
  })
}
test('older account response without font does not inherit the preceding account size', () => {
  const value = load({ fontSize: 'extra-large' })
  value.api.applyAudioPreferences({ cardSize: 'large' })
  assert.equal(value.api.audioPreferences.fontSize, 'standard')
  assert.equal(value.properties['--l12-font-scale'], '1')
})
for (const fontSize of [null, '', 'huge', 1.24, {}, ['large']]) {
  test(`invalid stored font size ${JSON.stringify(fontSize)} defaults safely`, () => {
    const value = load({ fontSize })
    assert.equal(value.api.audioPreferences.fontSize, 'standard')
    assert.equal(value.properties['--l12-font-scale'], '1')
  })
}
test('invalid incoming size cannot become arbitrary CSS', () => {
  const value = load({ fontSize: 'large' })
  value.api.applyAudioPreferences({ fontSize: '999; color:red' })
  assert.equal(value.api.audioPreferences.fontSize, 'standard')
  assert.equal(value.properties['--l12-font-scale'], '1')
})
const css = fs.readFileSync(path.join(root, 'src/style.css'), 'utf8')
for (const [selector, base] of [
  ['.l12-effect-body', 'var(--l12-effect-copy,clamp(13px,1.15vw,15px))'],
  ['.l12-effect-body--compact', 'var(--l12-effect-copy,clamp(12.5px,1.05vw,14px))'],
  ['.l12-effect-body--prominent', 'max(15px,var(--l12-effect-copy,clamp(13px,1.15vw,15px)))'],
]) test(`${selector} retains exact default semantic base and scales once`, () => {
  const declaration = css.slice(css.indexOf(`${selector}{`)).split('}')[0]
  assert(declaration.includes(`font-size:calc(${base} * var(--l12-font-scale,1))!important`))
  assert.equal((declaration.match(/--l12-font-scale/g) || []).length, 1)
  assert(declaration.includes('line-height:1.') && declaration.includes('!important'))
})
test('native setting clearly limits this slice to semantic prose', () => {
  const settings = fs.readFileSync(path.join(root, 'src/l12/site/L12SettingsModal.vue'), 'utf8')
  assert(settings.includes('v-model="audioPreferences.fontSize"'))
  for (const size of ['standard', 'large', 'extra-large']) assert(settings.includes(`value="${size}"`))
  assert(settings.includes('卡牌、询问及对局记录正文') && settings.includes('不改变卡图'))
})
test('battle log prose scales once, controls and default board tier remain unchanged', () => {
  const log = fs.readFileSync(path.join(root, 'src/l12/game/BattleEventLog.vue'), 'utf8')
  for (const selector of ['.event-message', '.combat-cards']) {
    const declaration = log.slice(log.indexOf(`${selector}{`)).split('}')[0]
    assert(declaration.includes('font-size:calc(var(--l12-board-copy,13px) * var(--l12-font-scale,1))'))
    assert.equal((declaration.match(/--l12-font-scale/g) || []).length, 1)
    assert(declaration.includes('line-height:1.55') && declaration.includes('overflow-wrap:anywhere'))
  }
  for (const selector of ['.combat-toggle', '.action-toggle', '.jump-latest', '.turn-divider']) {
    const declaration = log.slice(log.indexOf(`${selector}{`)).split('}')[0]
    assert(!declaration.includes('--l12-font-scale'), `${selector} is a control or label, not prose`)
  }
})
console.log(JSON.stringify({ suite: 'actual-font-preferences-module-and-semantic-contract', passed: results.length, failed: failures.length, results, failures }, null, 2))
if (failures.length) throw new Error(failures.join('\n'))
